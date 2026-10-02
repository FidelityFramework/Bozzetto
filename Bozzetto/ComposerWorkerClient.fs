namespace Bozzetto.ComposerIntegration

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks
open Bozzetto.Providers
open Bozzetto.Composer.Protocol

type WorkerLaunch = {
  WorkerPath: string
  DotnetPath: string
  EvidenceDirectory: string
}

type IComposerWorker =
  abstract Handshake: Reply
  abstract ProcessId: int
  abstract IsAlive: bool
  abstract Exited: IEvent<string>
  abstract RequestAsync: body: RequestBody * cancellation: CancellationToken -> Task<Reply>
  abstract StopAsync: unit -> Task

module ComposerWire =
  let session = function
    | Reserve(target, _) | Build(target, _) | Run(target, _) | Format(target, _, _)
    | Status target | Cancel target | Close target -> target.Session
    | _ -> ""
  let target = function
    | Hello _ -> None
    | Open(target, _) | CancelRequest(target, _) | PrepareCompilerChange target -> Some target
    | Reserve(target, _) | Build(target, _) | Run(target, _) | Format(target, _, _)
    | Status target | Cancel target | Close target -> Some target.Worker
  let workerAddress (authority: Authority): WorkerAddress =
    { Host = authority.Host; Epoch = authority.Epoch; Provider = authority.Provider }
  let sessionAddress (authority: Authority): SessionAddress =
    { Worker = workerAddress authority; Session = authority.Session }

module ComposerWorkerClient =
  type private Pending = {
    Operation: Operation
    Session: string
    Completion: TaskCompletionSource<Reply>
    mutable Sent: bool
    mutable Writing: bool
    mutable Abandonment: exn option
  }

  type private Connection(worker: Process, socket: Socket, socketDirectory: string,
                          wire: StreamWriter, outputFile: FileStream, errorFile: FileStream,
                          stdout: Task, stderr: Task, beforeCancellationAdmission: unit -> unit,
                          beforeEncode: RequestBody -> unit, beforeWrite: Operation -> string -> Task) =
    let stream = new NetworkStream(socket, ownsSocket = true)
    let gate, evidenceGate, stopGate = obj (), obj (), obj ()
    let writes = new SemaphoreSlim(1, 1)
    let lifetime = new CancellationTokenSource()
    let pending = Dictionary<string, Pending>()
    let abandoned = Dictionary<string, Pending>()
    let cancellationSlots = HashSet<string>()
    let children = Dictionary<Guid, Task>()
    let exited = Event<string>()
    let mutable identity: WorkerAddress option = None
    let mutable handshake: Reply option = None
    let mutable failure: exn option = None
    let mutable stopTask: Task option = None
    let processId = worker.Id

    let record direction id operation =
      lock evidenceGate (fun () -> wire.WriteLine("{0:O}\t{1}\t{2}\t{3}", DateTimeOffset.UtcNow, direction, id, operation))

    let killOwned () =
      try if not worker.HasExited then worker.Kill(true)
      with :? InvalidOperationException -> ()

    let invalidate (error: exn) kill =
      let revoked = lock gate (fun () ->
        match failure with
        | Some _ -> None
        | None ->
          failure <- Some error
          let requests = Seq.append pending.Values abandoned.Values |> Seq.toArray
          pending.Clear()
          abandoned.Clear()
          cancellationSlots.Clear()
          Some requests)
      match revoked with
      | None -> ()
      | Some requests ->
        lifetime.Cancel()
        for request in requests do request.Completion.TrySetException error |> ignore
        if kill then try killOwned () with _ -> ()
        ThreadPool.QueueUserWorkItem(WaitCallback(fun (_: objnull) ->
          try exited.Trigger error.Message with _ -> ())) |> ignore

    // Register physical work before starting it, including targeted withdrawal.
    let startOwned (work: unit -> Task<unit>) =
      let id = Guid.NewGuid()
      let completion = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      let admitted = lock gate (fun () ->
        if failure.IsSome then false
        else
          children.Add(id, completion.Task)
          true)
      if admitted then
        let run = task {
          try
            try do! work ()
            with error -> invalidate (IOException("Composer transport work failed.", error)) true
          finally
            completion.TrySetResult() |> ignore
            lock gate (fun () -> children.Remove id |> ignore)
        }
        run |> ignore
      admitted

    let validate (request: Pending) (response: Reply) =
      if response.ProtocolVersion <> BAREWireCodec.ProtocolVersion then
        raise (InvalidDataException "Unsupported Composer worker protocol version.")
      let authority = response.Authority
      if String.IsNullOrWhiteSpace authority.Host || String.IsNullOrWhiteSpace authority.Epoch
         || authority.Provider <> ProviderIdentity.ClefComposer || authority.Generation < 0L then
        raise (InvalidDataException "Invalid Composer worker authority.")
      let success = Result.isOk response.Outcome
      let validSession =
        if not success && authority.Session = "" then true
        elif request.Operation = Operation.Open && success then authority.Session <> ""
        else authority.Session = request.Session
      if not validSession then raise (InvalidDataException "Composer response belongs to another session.")
      match response.Outcome with
      | Result.Ok body when BAREWireCodec.replyOperation body <> request.Operation ->
        raise (InvalidDataException "Composer reply has the wrong operation.")
      | _ -> ()
      lock gate (fun () ->
        let address = ComposerWire.workerAddress authority
        match identity, response.Outcome with
        | Some expected, _ when address <> expected ->
          raise (InvalidDataException "Composer response belongs to another worker or compiler epoch.")
        | Some _, _ -> ()
        | None, Result.Ok(HelloAccepted hello) when request.Operation = Operation.Hello && authority.Session = "" ->
          if hello.Agreement <> BAREWireCodec.agreement
             || hello.Psg.Schema <> Fidelity.PSG.Revision.Schema
             || hello.Psg.FormatVersion <> Fidelity.PSG.Binary.FormatVersion
             || hello.Psg.ContractFingerprint <> Fidelity.PSG.Binary.ContractFingerprint then
            raise (InvalidDataException "Composer worker contract does not match the daemon.")
          identity <- Some address
          handshake <- Some response
        | _ -> raise (InvalidDataException "Composer worker did not establish a valid hello authority."))

    let accept (response: Reply) =
      if String.IsNullOrWhiteSpace response.RequestId then raise (InvalidDataException "Composer reply has no request identity.")
      record "response" response.RequestId (response.Outcome |> Result.map BAREWireCodec.replyOperation |> sprintf "%A")
      let request, wasPending = lock gate (fun () ->
        match pending.TryGetValue response.RequestId with
        | true, request -> request, true
        | _ ->
          match abandoned.TryGetValue response.RequestId with
          | true, request -> abandoned.Remove response.RequestId |> ignore; request, false
          | _ -> raise (InvalidDataException "Uncorrelated Composer worker response."))
      validate request response
      if wasPending then
        let admitted = lock gate (fun () ->
          if pending.Remove response.RequestId then true
          else
            abandoned.Remove response.RequestId |> ignore
            false)
        if admitted then request.Completion.TrySetResult response |> ignore

    let input = task {
      try
        let mutable reading = true
        while reading do
          let! received = StreamFrames.readReplyAsync stream lifetime.Token
          match received with
          | Result.Ok(Some response) -> accept response
          | Result.Ok None ->
            reading <- false
            invalidate (EndOfStreamException "Composer worker closed its socket.") true
          | Result.Error error -> raise (InvalidDataException(sprintf "Invalid Composer frame: %A" error))
      with
      | :? OperationCanceledException when lifetime.IsCancellationRequested -> ()
      | error -> invalidate error true
    }

    do
      worker.Exited.Add(fun _ -> invalidate (EndOfStreamException "Composer worker process exited.") false)
      worker.EnableRaisingEvents <- true
      for drain in [stdout; stderr] do
        drain.ContinueWith((fun (finished: Task) ->
          if finished.IsFaulted then invalidate (IOException("Composer diagnostic drain failed.", finished.Exception)) true), TaskScheduler.Default) |> ignore

    member _.Handshake = lock gate (fun () -> handshake |> Option.defaultWith (fun () -> invalidOp "Composer hello has not completed."))
    member _.IsAlive =
      lock gate (fun () -> failure.IsNone) &&
        (try not worker.HasExited with :? InvalidOperationException -> false)

    member private this.RequestAsyncCore(body: RequestBody, cancellation: CancellationToken, cancellationSlot: string option): Task<Reply> = task {
      cancellation.ThrowIfCancellationRequested()
      let operation = BAREWireCodec.requestOperation body
      let session = ComposerWire.session body
      let requestId = Guid.NewGuid().ToString("N")
      let completion = TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously)
      let request = { Operation = operation; Session = session; Completion = completion; Sent = false; Writing = false; Abandonment = None }
      let checkCapacity () =
        match cancellationSlot with
        | Some original when cancellationSlots.Contains original -> ()
        | Some _ -> invalidOp "Composer cancellation admission slot is unavailable."
        | None when pending.Count + cancellationSlots.Count >= 256 -> invalidOp "Too many pending Composer requests."
        | None -> ()
      lock gate (fun () ->
        failure |> Option.iter (fun error -> raise (IOException("Composer worker is unavailable.", error)))
        checkCapacity ()
        match identity, ComposerWire.target body with
        | None, None -> ()
        | Some expected, Some actual when expected = actual -> ()
        | _ -> raise (InvalidOperationException "Request does not address the negotiated Composer worker."))
      beforeEncode body
      let frame =
        StreamFrames.encodeRequest { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = requestId; Body = body }
        |> Result.defaultWith (fun error -> raise (InvalidDataException(sprintf "Composer request refused: %A" error)))
      cancellation.ThrowIfCancellationRequested()
      lock gate (fun () ->
        failure |> Option.iter (fun error -> raise (IOException("Composer worker is unavailable.", error)))
        checkCapacity ()
        cancellationSlot |> Option.iter (fun original -> cancellationSlots.Remove original |> ignore)
        pending.Add(requestId, request))
      let requiresRevocation = operation = Operation.Build || operation = Operation.Run
      let completeAbandonment (error: exn) =
        match error with
        | :? OperationCanceledException -> completion.TrySetCanceled cancellation |> ignore
        | _ -> completion.TrySetException error |> ignore
      let cancelTarget (reason: exn) () = task {
        try
          beforeCancellationAdmission ()
          let address = lock gate (fun () -> identity |> Option.defaultWith (fun () -> invalidOp "No worker identity."))
          let! reply = this.RequestAsyncCore(CancelRequest(address, requestId), CancellationToken.None, Some requestId)
          match reply.Outcome with
          | Result.Ok(RequestCanceled value) when value.TargetRequestId = requestId -> completeAbandonment reason
          | _ -> raise (InvalidDataException "Composer cancellation did not acknowledge the exact request.")
        with error ->
          let problem = IOException("Composer targeted cancellation failed.", error)
          invalidate problem true
          completion.TrySetException problem |> ignore
      }
      let abandon (error: exn) =
        let removed, sent, writing, overflow = lock gate (fun () ->
          let removed = pending.Remove requestId
          if removed then
            if requiresRevocation && (request.Sent || request.Writing) then cancellationSlots.Add requestId |> ignore
            request.Abandonment <- Some error
            if request.Sent || request.Writing then abandoned.Add(requestId, request)
          removed, request.Sent, request.Writing, abandoned.Count > 4096)
        if removed then
          if requiresRevocation && sent then startOwned (cancelTarget error) |> ignore
          elif not requiresRevocation || not writing then completeAbandonment error
        if overflow then invalidate (IOException "Composer worker exceeded its abandoned-request limit.") true
      let timeout = if operation = Operation.Build then TimeSpan.FromMinutes 8. else TimeSpan.FromSeconds 30.
      use deadline = new Timer(TimerCallback(fun (_: objnull) -> abandon (TimeoutException(sprintf "Composer %A timed out." operation))), box (), timeout, Timeout.InfiniteTimeSpan)
      use registration = cancellation.Register(fun () -> abandon (OperationCanceledException cancellation))
      let send () = task {
        try
          use sendDeadline = CancellationTokenSource.CreateLinkedTokenSource lifetime.Token
          sendDeadline.CancelAfter(TimeSpan.FromSeconds 30.)
          do! writes.WaitAsync sendDeadline.Token
          try
            let admitted = lock gate (fun () ->
              if pending.ContainsKey requestId then
                request.Writing <- true
                true
              else false)
            if admitted then
              do! (beforeWrite operation session).WaitAsync(sendDeadline.Token)
              record "request" requestId (sprintf "%A" operation)
              do! stream.WriteAsync(frame.AsMemory(), sendDeadline.Token)
              do! stream.FlushAsync sendDeadline.Token
              let canceled = lock gate (fun () -> request.Sent <- true; request.Abandonment)
              match canceled with
              | Some reason when requiresRevocation -> startOwned (cancelTarget reason) |> ignore
              | _ -> ()
          finally writes.Release() |> ignore
        with error ->
          let problem = IOException("Composer request transport failed.", error)
          invalidate problem true
          completion.TrySetException problem |> ignore
      }
      if not (startOwned send) then completion.TrySetException(ObjectDisposedException "Composer transport retired.") |> ignore
      return! completion.Task
    }

    member this.RequestAsync(body, cancellation) = this.RequestAsyncCore(body, cancellation, None)

    member private _.StopCore() = task {
      invalidate (ObjectDisposedException "Composer worker stopped.") false
      try socket.Shutdown SocketShutdown.Both with :? SocketException -> ()
      stream.Dispose()
      let mutable terminated = false
      try
        do! worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 5.)
        terminated <- true
      with :? TimeoutException -> ()
      if not terminated then
        killOwned ()
        // Failure to terminate is an explicit failed retirement. Keep the
        // remaining owned resources intact instead of disposing live drains.
        do! worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 2.)
      let owned = lock gate (fun () -> children.Values |> Seq.toArray)
      try
        // Physical exit does not join an admitted callback or diagnostic IO.
        // Retain the one StopTask until every owned task is actually terminal.
        do! Task.WhenAll(Array.append [|input :> Task; stdout; stderr|] owned)
      finally
        stream.Dispose()
        outputFile.Dispose()
        errorFile.Dispose()
        lock evidenceGate (fun () -> wire.Dispose())
        worker.Dispose()
        writes.Dispose()
        lifetime.Dispose()
        Directory.Delete(socketDirectory, true)
    }

    member this.StopAsync() = lock stopGate (fun () ->
      match stopTask with
      | Some running -> running
      | None -> let running = this.StopCore() :> Task in stopTask <- Some running; running)

    interface IComposerWorker with
      member this.Handshake = this.Handshake
      member _.ProcessId = processId
      member this.IsAlive = this.IsAlive
      member _.Exited = exited.Publish
      member this.RequestAsync(body, cancellation) = this.RequestAsync(body, cancellation)
      member this.StopAsync() = this.StopAsync()

  let internal startWithEncodingBoundary beforeEncode beforeCancellationAdmission beforeWrite (arguments: string list) (config: WorkerLaunch): Task<IComposerWorker> = task {
    if not Socket.OSSupportsUnixDomainSockets then raise (PlatformNotSupportedException "Composer requires local Unix-domain sockets.")
    if not (Path.IsPathFullyQualified config.WorkerPath && File.Exists config.WorkerPath) then invalidArg "config" "Composer worker must be an existing absolute file path."
    if not (Path.IsPathFullyQualified config.EvidenceDirectory) then invalidArg "config" "Composer evidence directory must be absolute."
    if config.WorkerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && String.IsNullOrWhiteSpace config.DotnetPath then
      invalidArg "config" "An explicit dotnet host is required for a Composer DLL."
    let cleanup = ResizeArray<unit -> unit>()
    let mutable transferred = false
    use rollback = { new IDisposable with
      member _.Dispose() =
        if not transferred then
          let errors = ResizeArray<exn>()
          for dispose in Seq.rev cleanup do
            try dispose () with error -> errors.Add error
          if errors.Count > 0 then raise (AggregateException("Composer startup resource cleanup failed.", errors)) }
    Directory.CreateDirectory config.EvidenceDirectory |> ignore
    let socketDirectory = Path.Combine(Path.GetTempPath(), "boz-" + Guid.NewGuid().ToString("N"))
    cleanup.Add(fun () -> if Directory.Exists socketDirectory then Directory.Delete(socketDirectory, true))
    if OperatingSystem.IsWindows() then Directory.CreateDirectory socketDirectory |> ignore
    else Directory.CreateDirectory(socketDirectory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    let endpoint = UnixDomainSocketEndPoint(Path.Combine(socketDirectory, "worker.sock"))
    use listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
    listener.Bind endpoint
    listener.Listen 1
    let info = ProcessStartInfo()
    if config.WorkerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) then
      info.FileName <- config.DotnetPath
      info.ArgumentList.Add config.WorkerPath
    else info.FileName <- config.WorkerPath
    for argument in arguments do info.ArgumentList.Add argument
    info.ArgumentList.Add "--socket"
    info.ArgumentList.Add(endpoint.ToString())
    info.UseShellExecute <- false
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    info.WorkingDirectory <- config.EvidenceDirectory
    let prefix = Path.Combine(config.EvidenceDirectory, "worker-" + Guid.NewGuid().ToString("N"))
    let wire = new StreamWriter(prefix + "-wire.tsv", false, UTF8Encoding(false), AutoFlush = true)
    cleanup.Add wire.Dispose
    let stdoutFile = new FileStream(prefix + "-stdout.log", FileMode.CreateNew, FileAccess.Write, FileShare.Read)
    cleanup.Add stdoutFile.Dispose
    let stderrFile = new FileStream(prefix + "-stderr.log", FileMode.CreateNew, FileAccess.Write, FileShare.Read)
    cleanup.Add stderrFile.Dispose
    let worker = new Process(StartInfo = info)
    cleanup.Add worker.Dispose
    let mutable started = false
    let mutable connection: Connection option = None
    let mutable stdout = Task.CompletedTask
    let mutable stderr = Task.CompletedTask
    try
      if not (worker.Start()) then raise (IOException "Composer worker process did not start.")
      started <- true
      stdout <- worker.StandardOutput.BaseStream.CopyToAsync stdoutFile
      stderr <- worker.StandardError.BaseStream.CopyToAsync stderrFile
      use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 15.)
      let! socket = listener.AcceptAsync(deadline.Token)
      cleanup.Add socket.Dispose
      let client = new Connection(worker, socket, socketDirectory, wire, stdoutFile, stderrFile, stdout, stderr, beforeCancellationAdmission, beforeEncode, beforeWrite)
      connection <- Some client
      transferred <- true
      let! _ = client.RequestAsync(Hello BAREWireCodec.agreement, CancellationToken.None)
      if not client.IsAlive then raise (IOException "Composer worker exited during its handshake.")
      return client :> IComposerWorker
    with error ->
      match connection with
      | Some client ->
        try do! client.StopAsync()
        with cleanup -> raise (AggregateException("Composer startup and cleanup failed.", error, cleanup))
      | None ->
        if started && not worker.HasExited then
          worker.Kill(true)
          do! worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 2.)
        do! Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds 2.)
      return raise error
  }

  let internal startWithBoundaries beforeCancellationAdmission beforeWrite arguments config =
    startWithEncodingBoundary ignore beforeCancellationAdmission beforeWrite arguments config
  let internal startWithCancellationBoundary beforeCancellationAdmission arguments config =
    startWithBoundaries beforeCancellationAdmission (fun _ _ -> Task.CompletedTask) arguments config
  let start config = startWithCancellationBoundary ignore [] config
