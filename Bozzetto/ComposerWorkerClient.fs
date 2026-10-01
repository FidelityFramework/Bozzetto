namespace Bozzetto.ComposerIntegration

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

type WorkerLaunch = {
  WorkerPath: string
  DotnetPath: string
  EvidenceDirectory: string
}

type IComposerWorker =
  abstract Handshake: JsonElement
  abstract ProcessId: int
  abstract IsAlive: bool
  abstract Exited: IEvent<string>
  abstract RequestAsync:
    operation: string * session: string * parameters: (string * obj) list * cancellation: CancellationToken -> Task<JsonElement>
  abstract StopAsync: unit -> Task

module ComposerWorkerClient =
  let private maximumFrame = 1024 * 1024
  let private encoding = UTF8Encoding(false, true)

  let private field name (value: JsonElement) =
    if value.ValueKind <> JsonValueKind.Object then raise (InvalidDataException "Composer protocol frame must be an object.")
    match value.TryGetProperty(name: string) with
    | true, property -> property
    | _ -> raise (InvalidDataException("Missing protocol field: " + name))

  let private text name value =
    let property = field name value
    if property.ValueKind <> JsonValueKind.String then
      raise (InvalidDataException("Protocol field is not a string: " + name))
    property.GetString() |> Option.ofObj |> Option.defaultValue ""

  type private Pending = {
    Operation: string
    Session: string
    Completion: TaskCompletionSource<JsonElement>
    mutable Sent: bool
    mutable Writing: bool
    mutable Abandonment: exn option
  }

  type private Connection(worker: Process, wire: StreamWriter, errors: FileStream, beforeCancellationAdmission: unit -> unit) =
    let gate = obj ()
    let evidenceGate = obj ()
    let stopGate = obj ()
    let writes = new SemaphoreSlim(1, 1)
    let lifetime = new CancellationTokenSource()
    let pending = Dictionary<string, Pending>()
    let abandoned = Dictionary<string, Pending>()
    // A sent or writing request keeps its admission slot until its withdrawal
    // takes ownership of that slot, so ordinary requests cannot steal it.
    let cancellationSlots = HashSet<string>()
    let exited = Event<string>()
    let mutable identity: (string * string) option = None
    let mutable handshake = Unchecked.defaultof<JsonElement>
    let mutable failure: exn option = None
    let mutable stopTask: Task option = None
    let processId = worker.Id

    let record direction payload =
      lock evidenceGate (fun () ->
        wire.WriteLine(JsonSerializer.Serialize {| at = DateTimeOffset.UtcNow; direction = direction; payload = payload |}))

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
        if kill then
          try killOwned () with _ -> ()
        // Subscribers cannot delay pipe drainage or the bounded shutdown path.
        ThreadPool.QueueUserWorkItem(WaitCallback(fun (_: objnull) ->
          try exited.Trigger error.Message with _ -> ())) |> ignore

    let validate (request: Pending) (response: JsonElement) =
      if (field "protocolVersion" response).GetInt32() <> 1 then
        raise (InvalidDataException "Unsupported Composer worker protocol version.")
      let success = (field "success" response).GetBoolean()
      let authority = field "authority" response
      let host, epoch = text "host" authority, text "epoch" authority
      let session = text "session" authority
      if String.IsNullOrWhiteSpace host || String.IsNullOrWhiteSpace epoch
         || text "provider" authority <> "clef-composer"
         || (field "generation" authority).GetInt64() < 0L then
        raise (InvalidDataException "Invalid Composer worker authority.")
      let validSession =
        if not success && session = "" then true // Host refusals can precede session resolution.
        elif request.Operation = "open" && success then session <> ""
        else session = request.Session
      if not validSession then raise (InvalidDataException "Composer response belongs to another session.")
      field (if success then "result" else "error") response |> ignore
      lock gate (fun () ->
        match identity with
        | Some(expectedHost, expectedEpoch) when host <> expectedHost || epoch <> expectedEpoch ->
          raise (InvalidDataException "Composer response belongs to another worker or compiler epoch.")
        | Some _ -> ()
        | None when request.Operation = "hello" && success && session = "" ->
          identity <- Some(host, epoch)
          handshake <- response.Clone()
        | None -> raise (InvalidDataException "Composer worker did not establish a valid hello authority."))

    let accept (line: string) =
      record "response" line
      use document = JsonDocument.Parse line
      let response = document.RootElement.Clone()
      let requestId = text "requestId" response
      if String.IsNullOrWhiteSpace requestId then raise (InvalidDataException "Composer response has no requestId.")
      let request, wasPending = lock gate (fun () ->
        match pending.TryGetValue requestId with
        | true, request -> request, true
        | _ ->
          match abandoned.TryGetValue requestId with
          | true, request ->
            abandoned.Remove requestId |> ignore
            request, false
          | _ -> raise (InvalidDataException "Uncorrelated Composer worker response."))
      // Late replies are still required to belong to this protocol/authority;
      // only their already-canceled completion is discarded.
      validate request response
      if wasPending then
        let admitted = lock gate (fun () ->
          if pending.Remove requestId then true
          else
            abandoned.Remove requestId |> ignore
            false)
        if admitted then request.Completion.TrySetResult response |> ignore

    let output = Task.Run(Action(fun () ->
      try
        use input = new BufferedStream(worker.StandardOutput.BaseStream, 8192)
        use frame = new MemoryStream()
        let mutable reading = true
        while reading do
          match input.ReadByte() with
          | -1 ->
            reading <- false
            if frame.Length <> 0L then raise (InvalidDataException "Truncated Composer protocol frame.")
            invalidate (EndOfStreamException "Composer worker closed stdout.") true
          | 10 ->
            accept (encoding.GetString(frame.GetBuffer(), 0, int frame.Length))
            frame.SetLength 0L
          | value ->
            if frame.Length >= int64 maximumFrame then
              raise (InvalidDataException "Composer response exceeds 1 MiB.")
            frame.WriteByte(byte value)
      with error -> invalidate error true))

    let stderr = Task.Run(Action(fun () ->
      try
        worker.StandardError.BaseStream.CopyTo errors
        errors.Flush()
      with error -> invalidate (IOException("Composer stderr drain failed.", error)) true))

    do
      worker.Exited.Add(fun _ ->
        invalidate (EndOfStreamException "Composer worker process exited.") false)
      worker.EnableRaisingEvents <- true

    member _.Handshake = lock gate (fun () -> handshake)
    member _.IsAlive =
      lock gate (fun () -> failure.IsNone) &&
        (try not worker.HasExited with :? InvalidOperationException -> false)

    member private this.RequestAsyncCore(operation, session, parameters: (string * obj) list, cancellation: CancellationToken, cancellationSlot: string option): Task<JsonElement> = task {
      cancellation.ThrowIfCancellationRequested()
      let requestId = Guid.NewGuid().ToString("N")
      let completion = TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously)
      let request = {
        Operation = operation; Session = session; Completion = completion
        Sent = false; Writing = false; Abandonment = None }
      let values = Dictionary<string, objnull>()
      for key, value in parameters do
        if Set.contains key (set [ "protocolVersion"; "requestId"; "operation"; "host"; "epoch"; "provider"; "session" ]) then
          invalidArg "parameters" ("Reserved Composer protocol field: " + key)
        values.Add(key, value)
      let checkCapacity () =
        match cancellationSlot with
        | Some original when cancellationSlots.Contains original -> ()
        | Some _ -> invalidOp "Composer cancellation admission slot is unavailable."
        | None when pending.Count + cancellationSlots.Count >= 256 ->
          invalidOp "Too many pending Composer requests."
        | None -> ()
      let host, epoch = lock gate (fun () ->
        match failure with
        | Some error -> raise (IOException("Composer worker is unavailable.", error))
        | None ->
          checkCapacity ()
          if identity.IsNone && operation <> "hello" then
            raise (InvalidOperationException "Composer hello has not completed.")
          identity |> Option.defaultValue ("", ""))
      for key, value in
        [ "protocolVersion", box 1; "requestId", box requestId; "operation", box operation
          "host", box host; "epoch", box epoch; "provider", box "clef-composer"; "session", box session ] do
        values.Add(key, value)
      let line = JsonSerializer.Serialize values
      if encoding.GetByteCount line > maximumFrame then invalidArg "parameters" "Composer request exceeds 1 MiB."
      lock gate (fun () ->
        match failure with
        | Some error -> raise (IOException("Composer worker is unavailable.", error))
        | None ->
          checkCapacity ()
          cancellationSlot |> Option.iter (fun original -> cancellationSlots.Remove original |> ignore)
          pending.Add(requestId, request))
      let requiresRevocation = operation = "build" || operation = "run"
      let completeAbandonment (error: exn) =
        match error with
        | :? OperationCanceledException -> completion.TrySetCanceled cancellation |> ignore
        | _ -> completion.TrySetException error |> ignore
      let cancelTarget (reason: exn) = task {
        try
          beforeCancellationAdmission ()
          let! reply = this.RequestAsyncCore(
            "cancel_request", "", [ "targetRequestId", requestId :> obj ], CancellationToken.None, Some requestId)
          if not ((field "success" reply).GetBoolean()) then
            raise (InvalidDataException "Composer worker refused targeted request cancellation.")
          let acknowledgement = field "result" reply
          if text "targetRequestId" acknowledgement <> requestId then
            raise (InvalidDataException "Composer cancellation acknowledged another request.")
          (field "cancellationRequested" acknowledgement).GetBoolean() |> ignore
          // This acknowledges the exact cancellation request. Demand release
          // and physical cleanup can still be pending; refresh actual status
          // without assuming that another client's authority was withdrawn.
          completeAbandonment reason
        with error ->
          let failure = IOException("Composer targeted cancellation failed.", error)
          invalidate failure true
          completion.TrySetException failure |> ignore
      }
      let abandon (error: exn) =
        let removed, sent, writing, overflow = lock gate (fun () ->
          let removed = pending.Remove requestId
          if removed then
            if requiresRevocation && (request.Sent || request.Writing) then
              cancellationSlots.Add requestId |> ignore
            request.Abandonment <- Some error
            abandoned.Add(requestId, request)
          removed, request.Sent, request.Writing, abandoned.Count > 4096)
        if removed then
          if requiresRevocation && sent then cancelTarget error |> ignore
          elif not requiresRevocation || not writing then completeAbandonment error
        if overflow then invalidate (IOException "Composer worker exceeded its abandoned-request limit.") true
      let timeout = if operation = "build" then TimeSpan.FromMinutes 8. else TimeSpan.FromSeconds 30.
      use deadline = new Timer(TimerCallback(fun (_: objnull) -> abandon (TimeoutException("Composer " + operation + " timed out."))), box (), timeout, Timeout.InfiniteTimeSpan)
      use registration = cancellation.Register(fun () -> abandon (OperationCanceledException cancellation))
      let send = task {
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
              record "request" line
              do! worker.StandardInput.WriteLineAsync(line.AsMemory(), sendDeadline.Token)
              do! worker.StandardInput.FlushAsync sendDeadline.Token
              let canceled = lock gate (fun () ->
                request.Sent <- true
                request.Abandonment)
              // If cancellation raced the write, queue its targeted revocation
              // after the complete original frame, never before it.
              match canceled with
              | Some reason when requiresRevocation -> cancelTarget reason |> ignore
              | _ -> ()
          finally
            writes.Release() |> ignore
        with error ->
          let failure = IOException("Composer request transport failed.", error)
          invalidate failure true
          completion.TrySetException failure |> ignore
      }
      // Completion also covers cancellation while a different request owns stdin.
      // The independent sender always finishes a frame or retires the connection.
      send |> ignore
      return! completion.Task
    }

    member this.RequestAsync(operation, session, parameters, cancellation) =
      this.RequestAsyncCore(operation, session, parameters, cancellation, None)

    member private _.StopCore() = task {
      invalidate (ObjectDisposedException "Composer worker stopped.") false
      // Close the pipe itself: disposing StreamWriter could synchronously flush
      // into a stalled worker while another request is writing.
      try
        try worker.StandardInput.BaseStream.Close() with _ -> ()
        let mutable terminated = false
        try
          do! worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 5.)
          terminated <- true
        with :? TimeoutException -> ()
        if not terminated then
          killOwned ()
          do! worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 2.)
        do! Task.WhenAll(output, stderr).WaitAsync(TimeSpan.FromSeconds 2.)
      finally
        try worker.StandardOutput.BaseStream.Close() with _ -> ()
        try worker.StandardError.BaseStream.Close() with _ -> ()
        lock evidenceGate (fun () -> wire.Dispose())
        errors.Dispose()
        worker.Dispose()
    }

    member this.StopAsync() =
      lock stopGate (fun () ->
        match stopTask with
        | Some running -> running
        | None ->
          let running = this.StopCore() :> Task
          stopTask <- Some running
          running)

    interface IComposerWorker with
      member this.Handshake = this.Handshake
      member _.ProcessId = processId
      member this.IsAlive = this.IsAlive
      member _.Exited = exited.Publish
      member this.RequestAsync(operation, session, parameters, cancellation) =
        this.RequestAsync(operation, session, parameters, cancellation)
      member this.StopAsync() = this.StopAsync()

  let internal startWithCancellationBoundary beforeCancellationAdmission (arguments: string list) (config: WorkerLaunch): Task<IComposerWorker> = task {
    if not (Path.IsPathFullyQualified config.WorkerPath && File.Exists config.WorkerPath) then
      invalidArg "config" "Composer worker must be an existing absolute file path."
    if not (Path.IsPathFullyQualified config.EvidenceDirectory) then
      invalidArg "config" "Composer evidence directory must be absolute and outside project checkouts."
    let info = ProcessStartInfo()
    if config.WorkerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) then
      if String.IsNullOrWhiteSpace config.DotnetPath then invalidArg "config" "An explicit dotnet host is required for a Composer DLL."
      info.FileName <- config.DotnetPath
      info.ArgumentList.Add config.WorkerPath
    else info.FileName <- config.WorkerPath
    for argument in arguments do info.ArgumentList.Add argument
    info.ArgumentList.Add "--stdio"
    info.UseShellExecute <- false
    info.RedirectStandardInput <- true
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    info.StandardInputEncoding <- encoding
    info.WorkingDirectory <- config.EvidenceDirectory
    Directory.CreateDirectory config.EvidenceDirectory |> ignore
    let logPrefix = Path.Combine(config.EvidenceDirectory, "worker-" + Guid.NewGuid().ToString("N"))
    let wire = new StreamWriter(logPrefix + "-wire.jsonl", false, encoding, AutoFlush = true)
    let errors = new FileStream(logPrefix + "-stderr.log", FileMode.CreateNew, FileAccess.Write, FileShare.Read)
    let worker = new Process(StartInfo = info)
    let mutable started = false
    try
      if not (worker.Start()) then raise (IOException "Composer worker process did not start.")
      started <- true
      let client = new Connection(worker, wire, errors, beforeCancellationAdmission)
      try
        let! _ = client.RequestAsync("hello", "", [], CancellationToken.None)
        if not client.IsAlive then raise (IOException "Composer worker exited during its handshake.")
        return client :> IComposerWorker
      with error ->
        try do! client.StopAsync() with _ -> ()
        return raise error
    with error ->
      // Also cover failures while constructing the connection after Start.
      try
        if started && not worker.HasExited then
          worker.Kill(true)
          do! worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 2.)
      with _ -> ()
      worker.Dispose()
      wire.Dispose()
      errors.Dispose()
      return raise error
  }

  let start config = startWithCancellationBoundary ignore [] config
