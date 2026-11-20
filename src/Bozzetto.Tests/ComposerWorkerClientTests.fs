module Bozzetto.Tests.ComposerWorkerClientTests

open System
open System.IO
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration

type private FixtureMarker = class end

let private workerAddress: WorkerAddress =
  { Host = "transport-fixture"; Epoch = "transport-fixture-epoch"; Provider = ProviderIdentity.ClefComposer }
let private address session = { Worker = workerAddress; Session = session }
let private status session count: SessionSnapshot =
  { Observation = uint64 count; Project = "fixture.fidproj"; ManifestPath = "fixture.manifest"
    Closed = false; Busy = false; Current = None; RevocationPending = false; BackendError = None; FormatterError = None
    FormatterCleanupPending = false; WorkerRetirementRequired = None
    CleanupPending = false; CleanupError = None }
let private artifact: AcceptedArtifact =
  { Generation = 0L; SourceVersion = "fixture"; ArtifactPath = "fixture"; ArtifactSha256 = "fixture"
    ObjectManifest = "fixture"; ChangedWitnesses = [||]; RetainedWitnesses = [||]; RetiredWitnesses = [||]
    WitnessVisits = [||]; CompiledObjects = [||]; ReusedObjects = [||]; RetiredObjects = [||] }
let private formatBuffer: FormatBuffer =
  { Document = "buffer://transport/quoted.clef"; Incarnation = "00000000-0000-0000-0000-000000000001"
    Revision = 1UL; Source = "module Quoted\nlet value = <@ 1 @>\n"; Configuration = "fixture" }
let private ownedRequest operation session =
  match operation with
  | Operation.Build -> Build(address session, "reservation")
  | Operation.Run -> Run(address session, [||])
  | Operation.Format -> Format(address session, 0L, formatBuffer)
  | _ -> failwith "Expected an operation with a worker-side demand."
let private liveDiagnostic = "Boundary witness refused: callback environment has no owning region.\nThe compiler's exact diagnostic must remain readable while its worker is alive."
let private liveStandardOutput = "Compiler worker diagnostic evidence is live."

/// Child-only typed transport fixture; control barriers use fixture session IDs.
/// It records individual requests, not Calque's shared producer/demand state.
let runFixture socketPath =
  use socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
  socket.Connect(UnixDomainSocketEndPoint socketPath)
  use stream = new NetworkStream(socket, ownsSocket = false)
  let held = ResizeArray<Request>()
  let withdrawn = Collections.Generic.HashSet<string>()
  let openedSessions = Collections.Generic.Dictionary<string, string * string * bool>()
  let mutable nextSession = 0
  let replyOutcomeForSession (session: string) (request: Request) outcome =
    let response: Reply =
      { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = request.RequestId
        Authority = { Host = workerAddress.Host; Epoch = workerAddress.Epoch; Provider = workerAddress.Provider
                      Generation = 0L; Session = session }
        Outcome = outcome }
    StreamFrames.writeReplyAsync stream CancellationToken.None response |> fun work -> work.GetAwaiter().GetResult()
    |> Result.defaultWith (fun error -> failwithf "Fixture encoding: %A" error)
  let replyOutcome request outcome = replyOutcomeForSession (ComposerWire.session request.Body) request outcome
  let reply request result = replyOutcome request (Result.Ok result)
  let complete (request: Request) =
    match request.Body with
    | Build _ -> reply request (Built artifact)
    | Run _ -> reply request (Ran { Generation = 0L; SourceVersion = "fixture"; ExitCode = 0; StandardOutput = ""; StandardError = "" })
    | Format(_, _, buffer) ->
      if withdrawn.Contains request.RequestId then
        replyOutcome request (Result.Error { Code = RefusalCode.Canceled; Message = "Only this preview observer was withdrawn." })
      else
        reply request (Formatted {
          Document = buffer.Document; Incarnation = buffer.Incarnation; Revision = buffer.Revision
          SourceSha256 = "fixture"; Configuration = buffer.Configuration; FormatterIdentity = "fixture"; Formatted = buffer.Source })
    | Status target -> reply request (Observed(status target.Session 0))
    | _ -> failwith "Unexpected held fixture operation."
  let mutable reading = true
  let mutable holdAfterClose = false
  while reading do
    match StreamFrames.readRequestAsync stream CancellationToken.None |> fun work -> work.GetAwaiter().GetResult() with
    | Result.Ok None ->
      // A real process that outlives socket withdrawal, for the ownership test.
      // The parent fixture owns its exact PID and terminates it after assertions.
      if holdAfterClose then Thread.Sleep Timeout.Infinite
      reading <- false
    | Result.Error error -> failwithf "Fixture framing: %A" error
    | Result.Ok(Some request) ->
      match request.Body with
      | Hello agreement ->
        reply request (HelloAccepted {
          Agreement = agreement; Compiler = { AssemblyPath = "fixture"; Sha256 = "fixture"; Version = "fixture" }
          Psg = { Schema = Fidelity.PSG.Revision.Schema; AssemblySha256 = "fixture"
                  FormatVersion = Fidelity.PSG.Binary.FormatVersion; ContractFingerprint = Fidelity.PSG.Binary.ContractFingerprint }
          Operations = [|Operation.Hello; Operation.Open; Operation.Close; Operation.Status; Operation.Build; Operation.Run; Operation.Format; Operation.CancelRequest|]
          InMemoryPatchAllowed = false })
      | Open(_, project) ->
        nextSession <- nextSession + 1
        let sid = sprintf "fixture-session-%d" nextSession
        let manifest =
          if project.EndsWith(".cache-probe.fidproj", StringComparison.Ordinal) then
            let dir = Path.Combine(Path.GetDirectoryName project, "provider-sessions", workerAddress.Host, sid, workerAddress.Epoch)
            Directory.CreateDirectory dir |> ignore
            let path = Path.Combine(dir, "current.json")
            File.WriteAllText(path, """{"status":"open"}""")
            path
          else "fixture.manifest"
        openedSessions[sid] <- (project, manifest, false)
        replyOutcomeForSession sid request (Result.Ok(Opened { Observation = 1UL; Project = project; ManifestPath = manifest }))
      | Close target when openedSessions.ContainsKey target.Session ->
        let project, manifest, _ = openedSessions[target.Session]
        openedSessions[target.Session] <- (project, manifest, true)
        if project.EndsWith(".cache-probe.fidproj", StringComparison.Ordinal) then
          File.WriteAllText(manifest, """{"status":"closed"}""")
        reply request (Closed { Observation = 2UL; Closed = true; CleanupPending = true; CleanupError = None })
      | Status target when openedSessions.ContainsKey target.Session ->
        let project, manifest, isClosed = openedSessions[target.Session]
        let snap = { status target.Session 1 with Project = project; ManifestPath = manifest; Closed = isClosed; CleanupPending = isClosed }
        reply request (Observed snap)
      | Status target when target.Session = "barrier" -> reply request (Observed(status target.Session held.Count))
      | Status target when target.Session = "hold-after-close" ->
        holdAfterClose <- true
        reply request (Observed(status target.Session 0))
      | Status target when target.Session = "spawn-detached" ->
        if OperatingSystem.IsLinux() then
          let psi = System.Diagnostics.ProcessStartInfo("/bin/sh")
          psi.ArgumentList.Add "-c"
          psi.ArgumentList.Add "sleep 60 & sleep 60 &"
          psi.UseShellExecute <- false
          System.Diagnostics.Process.Start psi |> ignore
        reply request (Observed(status target.Session 0))
      | Status target when target.Session = "diagnostic-evidence" ->
        let authority: Bozzetto.Providers.Authority =
          { Host = workerAddress.Host; Epoch = workerAddress.Epoch; Provider = workerAddress.Provider
            Session = target.Session; Generation = 7L }
        ProviderDiagnostics.reportFailure Console.Error authority
          (Fidelity.FSharp.Incremental.ScopeId 11UL) (Fidelity.FSharp.Incremental.WorkId 13UL) liveDiagnostic
        Console.WriteLine liveStandardOutput
        reply request (Observed(status target.Session 0))
      | Status target when target.Session = "withdrawals" -> reply request (Observed(status target.Session withdrawn.Count))
      | Status target when target.Session = "format-requests" ->
        let retained =
          held |> Seq.filter (fun request ->
            match request.Body with Format _ -> not (withdrawn.Contains request.RequestId) | _ -> false) |> Seq.length
        reply request (Observed(status target.Session retained))
      | CancelRequest(_, id) ->
        let found = held |> Seq.exists (fun request -> request.RequestId = id)
        if found then withdrawn.Add id |> ignore
        reply request (RequestCanceled { TargetRequestId = id; CancellationRequested = found })
      | Status target when target.Session = "release" ->
        let count = held.Count
        for request in held do complete request
        held.Clear()
        reply request (Observed(status target.Session count))
      | Build _ | Run _ | Format _ | Status _ -> held.Add request
      | _ -> failwith "Unexpected fixture request."
  0

let private bounded (work: Task<'T>) = work.WaitAsync(TimeSpan.FromSeconds 20.)
let private request session = Status(address session)
let private count (reply: Reply) =
  match reply.Outcome with Result.Ok(Observed value) -> int value.Observation | _ -> failtestf "Expected fixture count: %A" reply
let private succeeded (reply: Reply) = Result.isOk reply.Outcome
let private canceled (work: Task<Reply>) = task {
  try let! _ = bounded work in return false
  with :? OperationCanceledException -> return true
}
let private config () =
  let dotnet = Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" |> Option.ofObj |> Option.defaultValue "dotnet"
  { WorkerPath = typeof<FixtureMarker>.Assembly.Location; DotnetPath = dotnet
    EvidenceDirectory = Path.Combine(Path.GetTempPath(), "bozzetto-transport-tests", Guid.NewGuid().ToString("N")) }
let private start beforeCancellation beforeWrite =
  ComposerWorkerClient.startWithBoundaries beforeCancellation beforeWrite ["--composer-worker-client-fixture"] (config ())

// Fault injection after a real Cleared sweep: the connection's socket directory
// vanishes, so StopAsync's final resource release throws once the group is clear.
let private cleanupFaultAfterCleared () =
  let socketDirectory = ref None
  let terminate pid pgid startTicks graceMs = async {
    let! outcome = Bozzetto.Server.LinuxProcessEdge.terminateProcessGroupWithFenceAsync pid pgid startTicks graceMs
    match outcome, socketDirectory.Value with
    | Bozzetto.ProcessWatchdog.TerminationOutcome.Cleared, Some directory -> Directory.Delete(directory, true)
    | _ -> ()
    return outcome
  }
  let register (client: IComposerWorker) =
    let arguments = File.ReadAllText(sprintf "/proc/%d/cmdline" client.ProcessId).Split('\000')
    socketDirectory.Value <- Some(Path.GetDirectoryName arguments[Array.IndexOf(arguments, "--socket") + 1])
    client
  terminate, register

let private evidenceWhileAlive () = task {
  let readEvidence path =
    use input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
    use reader = new StreamReader(input)
    reader.ReadToEnd()
  let launch = config ()
  let! client = ComposerWorkerClient.startWithBoundaries ignore (fun _ _ -> Task.CompletedTask) ["--composer-worker-client-fixture"] launch
  use cleanup =
    { new IAsyncDisposable with
        member _.DisposeAsync() = ValueTask(client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.)) }
  let! emitted = client.RequestAsync(request "diagnostic-evidence", CancellationToken.None) |> bounded
  succeeded emitted |> Expect.isTrue "The live child emitted its diagnostic before acknowledging the request."
  let stderrPath = Directory.GetFiles(launch.EvidenceDirectory, "worker-*-stderr.log") |> Array.exactlyOne
  let stdoutPath = Directory.GetFiles(launch.EvidenceDirectory, "worker-*-stdout.log") |> Array.exactlyOne
  let expectedError =
    "Composer producer host=transport-fixture session=diagnostic-evidence epoch=transport-fixture-epoch generation=7 scope=11 work=13 compiler_refused: "
    + liveDiagnostic + Environment.NewLine
  let expectedOutput = liveStandardOutput + Environment.NewLine
  (System.Text.Encoding.UTF8.GetByteCount expectedError, 4096) |> Expect.isLessThan "The diagnostic cannot fill the former 4096-byte FileStream buffer."
  let elapsed = System.Diagnostics.Stopwatch.StartNew()
  let mutable stderr = ""
  let mutable stdout = ""
  while (stderr <> expectedError || stdout <> expectedOutput) && elapsed.Elapsed < TimeSpan.FromSeconds 5. do
    client.IsProcessAlive |> Expect.isTrue "Evidence is read before the child exits."
    client.IsAlive |> Expect.isTrue "The shared transport remains available while diagnostics are read."
    stderr <- readEvidence stderrPath
    stdout <- readEvidence stdoutPath
    if stderr <> expectedError || stdout <> expectedOutput then do! Task.Delay 10
  stderr |> Expect.equal "The existing stderr evidence path retains the exact scoped diagnostic without retirement." expectedError
  stdout |> Expect.equal "The existing stdout evidence path is also readable before retirement." expectedOutput
  let! responsive = client.RequestAsync(request "barrier", CancellationToken.None) |> bounded
  count responsive |> Expect.equal "The same live child still responds after evidence became visible." 0
  client.IsProcessAlive |> Expect.isTrue "No exit was used to flush the diagnostic."
}

let private capacity operation = task {
  use entered = new ManualResetEventSlim()
  use release = new ManualResetEventSlim()
  let boundary () = entered.Set(); if not (release.Wait(TimeSpan.FromSeconds 20.)) then failwith "Cancellation barrier timeout."
  let! client = start boundary (fun _ _ -> Task.CompletedTask)
  use cancellation = new CancellationTokenSource()
  let victimBody = ownedRequest operation "victim"
  let victim = client.RequestAsync(victimBody, cancellation.Token)
  let held = [|for index in 1..255 -> client.RequestAsync(request (string index), CancellationToken.None)|]
  let canceling = Task.Run(Action(fun () -> cancellation.Cancel()))
  use cleanup =
    { new IAsyncDisposable with
        member _.DisposeAsync() =
          ValueTask(task {
            release.Set()
            do! canceling.WaitAsync(TimeSpan.FromSeconds 20.)
            do! client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.)
          }) }
  do! task {
    entered.Wait(TimeSpan.FromSeconds 20.) |> Expect.isTrue "Targeted cancellation reached its reserved-slot boundary."
    let! refused = task {
      try let! _ = client.RequestAsync(request "overflow", CancellationToken.None) |> bounded in return false
      with :? InvalidOperationException as error -> return error.Message = "Too many pending Composer requests."
    }
    refused |> Expect.isTrue "Ordinary requests cannot steal the withdrawal slot."
    release.Set()
    let! detached = canceled victim
    detached |> Expect.isTrue "Victim detaches after exact cancellation acknowledgement."
    let! released = client.RequestAsync(request "release", CancellationToken.None) |> bounded
    count released |> Expect.equal "Original plus all 255 peers reached the child." 256
    let! survivors = Task.WhenAll held |> bounded
    survivors |> Array.forall succeeded |> Expect.isTrue "All peers survive cancellation at capacity."
    client.IsAlive |> Expect.isTrue "Late victim reply is correlated and drained."
  }
}

let private cancellationPhase operation phase = task {
  let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let beforeWrite _ session =
    if session = "writer" then entered.TrySetResult() |> ignore; release.Task :> Task
    else Task.CompletedTask
  let mutable encodingCancellation: CancellationTokenSource option = None
  let mutable encoded = 0
  let beforeEncode body =
    if ComposerWire.session body = "victim" then
      encoded <- encoded + 1
      encodingCancellation |> Option.iter _.Cancel()
  let! client = ComposerWorkerClient.startWithEncodingBoundary beforeEncode ignore beforeWrite ["--composer-worker-client-fixture"] (config ())
  use cleanup =
    { new IAsyncDisposable with
        member _.DisposeAsync() =
          ValueTask(task {
            release.TrySetResult() |> ignore
            do! client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.)
          }) }
  do! task {
    let peer = client.RequestAsync(request "peer", CancellationToken.None)
    let! barrier = client.RequestAsync(request "barrier", CancellationToken.None) |> bounded
    count barrier |> Expect.equal "Peer is physically held." 1
    if phase = "writing" || phase = "sent" then
      use cancellation = new CancellationTokenSource()
      let victim = client.RequestAsync(ownedRequest operation (if phase = "writing" then "writer" else "victim"), cancellation.Token)
      if phase = "writing" then
        do! entered.Task |> bounded
        cancellation.Cancel()
        victim.IsCompleted |> Expect.isFalse "Possibly-written work requires acknowledgement."
        release.TrySetResult() |> ignore
      else
        let! barrier = client.RequestAsync(request "barrier", CancellationToken.None) |> bounded
        count barrier |> Expect.equal "Victim crossed socket before cancellation." 2
        cancellation.Cancel()
      let! detached = canceled victim
      detached |> Expect.isTrue "Targeted cancellation completes the exact observer."
      let! response = client.RequestAsync(request "release", CancellationToken.None) |> bounded
      count response |> Expect.equal "Both late correlated replies are drained." 2
    else
      let writer = if phase = "queued" then Some(client.RequestAsync(request "writer", CancellationToken.None)) else None
      if writer.IsSome then do! entered.Task |> bounded
      for _ in 1..4097 do
        use cancellation = new CancellationTokenSource()
        if phase = "initial" then cancellation.Cancel()
        encodingCancellation <- if phase = "encoding" then Some cancellation else None
        let victim = client.RequestAsync(ownedRequest operation "victim", cancellation.Token)
        if phase = "queued" then cancellation.Cancel()
        let! detached = canceled victim
        detached |> Expect.isTrue "Every never-written request detaches."
      encodingCancellation <- None
      encoded |> Expect.equal "Pre-canceled requests never enter encoding." (if phase = "initial" then 0 else 4097)
      client.IsAlive |> Expect.isTrue "Never-written requests do not exhaust late-reply capacity."
      peer.IsCompleted |> Expect.isFalse "Unrelated peer is still retained."
      release.TrySetResult() |> ignore
      let! response = client.RequestAsync(request "release", CancellationToken.None) |> bounded
      count response |> Expect.equal "Unsent victims never reached the child." (if writer.IsSome then 2 else 1)
      match writer with
      | Some work -> let! reply = bounded work in succeeded reply |> Expect.isTrue "Writer survives queued cancellation."
      | None -> ()
    let! peerReply = peer |> bounded
    succeeded peerReply |> Expect.isTrue "Independent peer completes."
    let! final = client.RequestAsync(request "barrier", CancellationToken.None) |> bounded
    count final |> Expect.equal "All physical replies drained." 0
  }
}

let private closeRetainsCallback operation = task {
  use entered = new ManualResetEventSlim()
  use release = new ManualResetEventSlim()
  let boundary () =
    entered.Set()
    if not (release.Wait(TimeSpan.FromSeconds 30.)) then failwith "Held cancellation callback was not released."
  let! client = start boundary (fun _ _ -> Task.CompletedTask)
  use child = System.Diagnostics.Process.GetProcessById client.ProcessId
  use cancellation = new CancellationTokenSource()
  let victim = client.RequestAsync(ownedRequest operation "held-close", cancellation.Token)
  let mutable canceling = Task.CompletedTask
  let mutable closing = Task.CompletedTask
  let mutable verified = false
  use cleanup =
    { new IAsyncDisposable with
        member _.DisposeAsync() =
          ValueTask(task {
            release.Set()
            do! canceling.WaitAsync(TimeSpan.FromSeconds 10.)
            try do! client.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
            with _ when not verified -> ()
          }) }
  do! task {
    let! barrier = client.RequestAsync(request "barrier", CancellationToken.None) |> bounded
    count barrier |> Expect.equal "The child has received the owned request." 1
    canceling <- Task.Run(Action(fun () -> cancellation.Cancel()))
    entered.Wait(TimeSpan.FromSeconds 20.) |> Expect.isTrue "The cancellation callback owns admitted transport work."
    closing <- client.StopAsync()
    obj.ReferenceEquals(closing, client.StopAsync()) |> Expect.isTrue "Retirement retains one join receipt."
    do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
    // State readiness is proven by the callback and actual process exit.
    // Hold past the removed five-second join deadline to discriminate the
    // prior premature disposal, not to infer readiness from elapsed time.
    let deadline = Task.Delay(TimeSpan.FromSeconds 6.)
    let! observed = Task.WhenAny(closing, deadline)
    obj.ReferenceEquals(observed, deadline) |> Expect.isTrue "Close must not abandon owned cleanup at its former five-second timeout."
    closing.IsCompleted |> Expect.isFalse "A dead process does not mean its admitted callback drained."
    release.Set()
    do! canceling.WaitAsync(TimeSpan.FromSeconds 10.)
    do! closing.WaitAsync(TimeSpan.FromSeconds 10.)
    // Completion was settled during invalidation; delivery to this observer
    // may be scheduled after the physically owned callback/drain has joined.
    let! withdrawn = task {
      try
        let! _ = victim.WaitAsync(TimeSpan.FromSeconds 10.)
        return false
      with :? ObjectDisposedException -> return true
    }
    withdrawn |> Expect.isTrue "The withdrawn observer receives the stop refusal."
    client.IsAlive |> Expect.isFalse "Joined retirement cannot expose live worker authority."
    verified <- true
  }
}

[<Tests>]
let tests = testList "Composer worker socket client" [
  testTask "short worker diagnostics are visible in existing evidence files before the child exits" { do! evidenceWhileAlive () }
  testTask "build withdrawal retains capacity while peer requests survive" { do! capacity Operation.Build }
  testTask "run withdrawal retains capacity while peer requests survive" { do! capacity Operation.Run }
  testTask "format withdrawal retains capacity while peer requests survive" { do! capacity Operation.Format }
  for phase in ["initial"; "encoding"; "queued"; "writing"; "sent"] do
    testTask (phase + " cancellation drains owned work without consuming unrelated capacity") { do! cancellationPhase Operation.Build phase }
    testTask ("format " + phase + " cancellation drains owned work without consuming unrelated capacity") { do! cancellationPhase Operation.Format phase }
  testTask "format cancellation targets one wire request when session and buffer are identical" {
    do! task {
      let! client = start ignore (fun _ _ -> Task.CompletedTask)
      use cancellation = new CancellationTokenSource()
      use cleanup =
        { new IAsyncDisposable with
            member _.DisposeAsync() = ValueTask(client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.)) }
      do! task {
        let body = ownedRequest Operation.Format "shared-session"
        let victim = client.RequestAsync(body, cancellation.Token)
        let peer = client.RequestAsync(body, CancellationToken.None)
        let! before = client.RequestAsync(request "format-requests", CancellationToken.None) |> bounded
        count before |> Expect.equal "Both exact-buffer requests have reached the worker." 2
        cancellation.Cancel()
        let! detached = canceled victim
        detached |> Expect.isTrue "Caller cancellation completes after its targeted withdrawal acknowledgement."
        let! remaining = client.RequestAsync(request "format-requests", CancellationToken.None) |> bounded
        count remaining |> Expect.equal "Only the canceled request is withdrawn at the worker boundary." 1
        let! withdrawals = client.RequestAsync(request "withdrawals", CancellationToken.None) |> bounded
        count withdrawals |> Expect.equal "The worker received one exact request cancellation." 1
        peer.IsCompleted |> Expect.isFalse "The other observer remains attached to its result."
        let! released = client.RequestAsync(request "release", CancellationToken.None) |> bounded
        count released |> Expect.equal "Both physical responses are still correlated and drained." 2
        let! survived = peer |> bounded
        match survived.Outcome with
        | Result.Ok(Formatted preview) ->
          preview.Document |> Expect.equal "The peer keeps its immutable document identity." formatBuffer.Document
          preview.Formatted |> Expect.equal "The peer receives its requested preview." formatBuffer.Source
        | outcome -> failtestf "The uncanceled formatter observer did not complete: %A" outcome
        let! final = client.RequestAsync(request "barrier", CancellationToken.None) |> bounded
        count final |> Expect.equal "No physical request remains held." 0
        client.IsAlive |> Expect.isTrue "Targeted withdrawal preserves the shared worker."
      }
    }
  }
  testTask "close terminates the owned socket child and drains diagnostics" {
    let! (client: IComposerWorker) = start ignore (fun _ _ -> Task.CompletedTask)
    let pid = client.ProcessId
    do! client.StopAsync()
    client.IsAlive |> Expect.isFalse "Stopped child cannot retain authority."
    let exited =
      try use childProcess = System.Diagnostics.Process.GetProcessById pid in childProcess.HasExited
      with :? ArgumentException -> true
    exited |> Expect.isTrue "Close physically joins the child process."
  }
  testTask "transport withdrawal retains physical ownership until the actual child exits" {
    let! (client: IComposerWorker) = start ignore (fun _ _ -> Task.CompletedTask)
    let child = System.Diagnostics.Process.GetProcessById client.ProcessId
    use cleanup =
      { new IAsyncDisposable with
          member _.DisposeAsync() = ValueTask(task {
            try
              if not child.HasExited then child.Kill(true)
              do! client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.)
            finally child.Dispose()
          }) }
    let! held = client.RequestAsync(request "hold-after-close", CancellationToken.None) |> bounded
    succeeded held |> Expect.isTrue "child has acknowledged its physical hold"
    let stopping = client.StopAsync()
    client.IsAlive |> Expect.isFalse "transport authority is withdrawn immediately"
    client.IsProcessAlive |> Expect.isTrue "unavailable transport still owns the live process"
    child.HasExited |> Expect.isFalse "physical state comes from the actual child"
    stopping.IsCompleted |> Expect.isFalse "shutdown has not mistaken withdrawal for exit"
    child.Kill(true)
    do! stopping.WaitAsync(TimeSpan.FromSeconds 10.)
    client.IsProcessAlive |> Expect.isFalse "joined child no longer owns a live process"
  }
  testTask "close terminates detached background grandchildren inside the worker process group with zero survivors" {
    if OperatingSystem.IsLinux() then
      let! (client: IComposerWorker) = start ignore (fun _ _ -> Task.CompletedTask)
      let workerPgid = client.ProcessId
      let! spawned = client.RequestAsync(request "spawn-detached", CancellationToken.None) |> bounded
      succeeded spawned |> Expect.isTrue "fixture spawned background grandchildren"
      let mutable beforePids = Set.empty
      let deadline = DateTimeOffset.UtcNow.AddSeconds 3.0
      while Set.count beforePids < 3 && DateTimeOffset.UtcNow < deadline do
        do! Task.Delay 50
        match Bozzetto.Server.LinuxProcessEdge.readLinuxProcesses false with
        | Ok procs -> beforePids <- Bozzetto.ProcessWatchdog.subtreePids workerPgid (Some workerPgid) procs
        | Error err -> failtestf "Unexpected /proc scan failure: %A" err
      (Set.count beforePids >= 3) |> Expect.isTrue (sprintf "expected worker + nested grandchildren in PGID %d, saw %A" workerPgid beforePids)
      do! client.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
      client.IsProcessAlive |> Expect.isFalse "worker process exited on StopAsync"
      let afterProcs =
        match Bozzetto.Server.LinuxProcessEdge.readLinuxProcesses false with
        | Ok procs -> procs
        | Error err -> failtestf "Unexpected /proc scan failure: %A" err
      let survivors = Bozzetto.ProcessWatchdog.subtreePids workerPgid (Some workerPgid) afterProcs
      Set.isEmpty survivors |> Expect.isTrue (sprintf "StopAsync terminated every detached grandchild in the worker process group, remaining=%A" survivors)
  }
  testTask "close retains ownership of a held cancellation callback after physical child exit" { do! closeRetainsCallback Operation.Build }
  testTask "close retains ownership of a held format cancellation callback after physical child exit" { do! closeRetainsCallback Operation.Format }
  testTask "unexpected real child exit fires ProcessExited only after StopAsync verifies Cleared, and never on Survivors or Unverified" {
    do! task {
      // Positive control: real child killed externally fires Exited, holds ProcessExited until StopAsync clears it.
      let launch = config ()
      let! (client: IComposerWorker) =
        ComposerWorkerClient.startWithBoundaries ignore (fun _ _ -> Task.CompletedTask) ["--composer-worker-client-fixture"] launch
      let exitedTcs = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
      let mutable processExitedCount = 0
      use _exitedSub = client.Exited.Subscribe(fun msg -> exitedTcs.TrySetResult msg |> ignore)
      use _procSub = client.ProcessExited.Subscribe(fun () -> Interlocked.Increment &processExitedCount |> ignore)
      use child = System.Diagnostics.Process.GetProcessById client.ProcessId
      try
        child.Kill true
        do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
        let! _ = exitedTcs.Task.WaitAsync(TimeSpan.FromSeconds 10.)
        Volatile.Read &processExitedCount |> Expect.equal "ProcessExited must not fire before StopAsync verifies group cleanup" 0
        do! client.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
        Volatile.Read &processExitedCount |> Expect.equal "StopAsync fires ProcessExited once Cleared" 1
        client.IsAlive |> Expect.isFalse "stopped client has no live authority"
        client.IsProcessAlive |> Expect.isFalse "stopped client has no live child"
      finally
        try if not child.HasExited then child.Kill true with _ -> ()
        if Directory.Exists launch.EvidenceDirectory then Directory.Delete(launch.EvidenceDirectory, true)

      // Fault-injection negative controls: real child exits, but termination reports Survivors or Unverified.
      // Only Linux consults the group terminator; elsewhere StopAsync verifies the root exit alone.
      let faultCases = [
        Bozzetto.ProcessWatchdog.TerminationOutcome.Survivors([ 99999 ], [ "injected survivor" ]), "surviving descendants"
        Bozzetto.ProcessWatchdog.TerminationOutcome.Unverified(Bozzetto.ProcessWatchdog.ProcScanError.ProcDirectoryInaccessible "injected /proc fault"), "could not be verified"
      ]
      for outcome, expectedText in (if OperatingSystem.IsLinux() then faultCases else []) do
        let faultLaunch = config ()
        let! (faultClient: IComposerWorker) =
          ComposerWorkerClient.startWithTerminationBoundary
            (fun _ _ _ _ -> async.Return outcome)
            ["--composer-worker-client-fixture"]
            faultLaunch
        let mutable faultProcessExited = 0
        use _faultSub = faultClient.ProcessExited.Subscribe(fun () -> Interlocked.Increment &faultProcessExited |> ignore)
        use faultChild = System.Diagnostics.Process.GetProcessById faultClient.ProcessId
        try
          faultChild.Kill true
          do! faultChild.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
          let! errMsg = task {
            try
              do! faultClient.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
              return ""
            with :? IOException as ex -> return ex.Message
          }
          errMsg |> Expect.stringContains "faulted group termination raises exact IOException" expectedText
          Volatile.Read &faultProcessExited |> Expect.equal "ProcessExited must NEVER fire when termination is Survivors or Unverified" 0
        finally
          try if not faultChild.HasExited then faultChild.Kill true with _ -> ()
          if Directory.Exists faultLaunch.EvidenceDirectory then Directory.Delete(faultLaunch.EvidenceDirectory, true)
    }
  }
  testTask "unprompted real worker crash triggers supervised StopAsync, fires ProcessExited, and prunes closed manifests without Open" {
    do! task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-real-crash-prune-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      let projPath = Path.Combine(tempRoot, "CrashProbe.cache-probe.fidproj")
      Directory.CreateDirectory cacheRoot |> ignore
      File.WriteAllText(projPath, """{"name":"CrashProbe"}""")
      let mutable creations = 0
      let evidenceDirs = ResizeArray<string>()
      let startWorker () =
        Interlocked.Increment &creations |> ignore
        let launch = config ()
        evidenceDirs.Add launch.EvidenceDirectory
        ComposerWorkerClient.startWithBoundaries ignore (fun _ _ -> Task.CompletedTask) ["--composer-worker-client-fixture"] launch
      let owner =
        ComposerSupervisor(
          startWorker,
          true,
          (fun () -> Task.CompletedTask),
          providerCacheRoot = cacheRoot)
      try
        let! opened = owner.ExecuteAsync(Open(workerAddress, projPath), CancellationToken.None)
        Result.isOk opened.Reply.Outcome |> Expect.isTrue "session opened on real fixture worker"
        let sid = opened.Reply.Authority.Session
        let manifest =
          match opened.Reply.Outcome with
          | Result.Ok(Opened info) -> info.ManifestPath
          | other -> failtestf "expected Opened reply, got %A" other
        File.Exists manifest |> Expect.isTrue "fixture created manifest on disk"
        let! closed = owner.ExecuteAsync(Close(address sid), CancellationToken.None)
        Result.isOk closed.Reply.Outcome |> Expect.isTrue "session closed leaving cleanupPending=true"
        File.Exists manifest |> Expect.isTrue "closed manifest stays on disk while worker lives"
        let pid = owner.WorkerPid |> Option.defaultWith (fun () -> failtest "expected live worker PID")
        let evictedAndPruned = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        use _sub =
          owner.Changed.Subscribe(fun () ->
            let dir = owner.CachedDirectory()
            if dir.Worker.IsNone && dir.Sessions.Length = 0 && not (File.Exists manifest) then
              evictedAndPruned.TrySetResult() |> ignore)
        use child = System.Diagnostics.Process.GetProcessById pid
        child.Kill true
        do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
        // Wait for autonomous background teardown with ZERO Open or SessionsAsync calls.
        do! evictedAndPruned.Task.WaitAsync(TimeSpan.FromSeconds 10.)
        File.Exists manifest |> Expect.isFalse "unprompted crash teardown pruned closed manifest without Open or SessionsAsync"
        creations |> Expect.equal "only the initial worker was created" 1
        // A subsequent Open cleanly spawns a replacement worker (retiring fence was released).
        let! replacement = owner.ExecuteAsync(Open(workerAddress, projPath), CancellationToken.None)
        Result.isOk replacement.Reply.Outcome |> Expect.isTrue "replacement worker opens cleanly after unprompted crash teardown"
        creations |> Expect.equal "replacement worker launched after teardown cleared worker" 2
        do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
      finally
        for dir in evidenceDirs do
          if Directory.Exists dir then Directory.Delete(dir, true)
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }
  }
  testTask "unprompted real worker crash with StopAsync Survivors keeps worker fenced and preserves deferred manifest" {
    if not (OperatingSystem.IsLinux()) then skiptest "Only Linux StopAsync consults the injected process-group terminator."
    do! task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-real-crash-fault-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      let projClosed = Path.Combine(tempRoot, "ClosedProbe.cache-probe.fidproj")
      let projOpen = Path.Combine(tempRoot, "OpenProbe.cache-probe.fidproj")
      Directory.CreateDirectory cacheRoot |> ignore
      File.WriteAllText(projClosed, """{"name":"ClosedProbe"}""")
      File.WriteAllText(projOpen, """{"name":"OpenProbe"}""")
      let mutable creations = 0
      let evidenceDirs = ResizeArray<string>()
      let startFaultedWorker () =
        Interlocked.Increment &creations |> ignore
        let launch = config ()
        evidenceDirs.Add launch.EvidenceDirectory
        ComposerWorkerClient.startWithTerminationBoundary
          (fun pid _ _ _ -> async.Return(Bozzetto.ProcessWatchdog.TerminationOutcome.Survivors([ pid ], [ "injected survivor" ])))
          ["--composer-worker-client-fixture"]
          launch
      let owner =
        ComposerSupervisor(
          startFaultedWorker,
          true,
          (fun () -> Task.CompletedTask),
          providerCacheRoot = cacheRoot)
      try
        let! openedClosed = owner.ExecuteAsync(Open(workerAddress, projClosed), CancellationToken.None)
        Result.isOk openedClosed.Reply.Outcome |> Expect.isTrue "first session opened"
        let closedSid = openedClosed.Reply.Authority.Session
        let closedManifest =
          match openedClosed.Reply.Outcome with
          | Result.Ok(Opened info) -> info.ManifestPath
          | other -> failtestf "expected Opened reply, got %A" other
        let! openedOpen = owner.ExecuteAsync(Open(workerAddress, projOpen), CancellationToken.None)
        Result.isOk openedOpen.Reply.Outcome |> Expect.isTrue "second session opened"
        let openSid = openedOpen.Reply.Authority.Session
        let! _ = owner.ExecuteAsync(Close(address closedSid), CancellationToken.None)
        let pid = owner.WorkerPid |> Option.defaultWith (fun () -> failtest "expected live worker PID")
        let teardownFailed = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
        use _sub =
          owner.Changed.Subscribe(fun () ->
            let dir = owner.CachedDirectory()
            dir.Sessions
            |> Array.tryFind (fun s -> s.Reply.Authority.Session = openSid)
            |> Option.iter (fun s ->
              match s.WorkerError with
              | Some err when err.Contains("Worker teardown failed:", StringComparison.Ordinal) ->
                teardownFailed.TrySetResult err |> ignore
              | _ -> ()))
        use child = System.Diagnostics.Process.GetProcessById pid
        child.Kill true
        do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
        let! recordedError = teardownFailed.Task.WaitAsync(TimeSpan.FromSeconds 10.)
        recordedError |> Expect.stringContains "teardown failure is surfaced on the remaining session" "surviving descendants"
        File.Exists closedManifest |> Expect.isTrue "unverified teardown must NEVER prune deferred manifest"
        let! refusedOpen = owner.ExecuteAsync(Open(workerAddress, projOpen), CancellationToken.None)
        match refusedOpen.Reply.Outcome with
        | Result.Error refusal -> refusal.Code |> Expect.equal "unverified teardown keeps worker fenced against replacement" RefusalCode.ProviderUnavailable
        | Result.Ok value -> failtestf "expected replacement Open refusal, got %A" value
        creations |> Expect.equal "no replacement worker started while previous worker teardown failed" 1
        let! shutdownErr = task {
          try
            do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
            return ""
          with :? IOException as ex -> return ex.Message
        }
        shutdownErr |> Expect.stringContains "owner StopAsync still surfaces memoized teardown failure" "surviving descendants"
        File.Exists closedManifest |> Expect.isTrue "manifest remains unpruned after unverified shutdown"
      finally
        for dir in evidenceDirs do
          if Directory.Exists dir then Directory.Delete(dir, true)
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }
  }
  testTask "a resource-cleanup fault after Cleared fails StopAsync, never fires ProcessExited, and keeps the closed manifest" {
    if not (OperatingSystem.IsLinux()) then skiptest "Only Linux StopAsync consults the injected process-group terminator."
    do! task {
      // Direct client: the real sweep clears, then StopAsync's final release faults.
      let launch = config ()
      let terminate, register = cleanupFaultAfterCleared ()
      let! (started: IComposerWorker) =
        ComposerWorkerClient.startWithTerminationBoundary terminate ["--composer-worker-client-fixture"] launch
      let client = register started
      let mutable processExitedCount = 0
      use _procSub = client.ProcessExited.Subscribe(fun () -> Interlocked.Increment &processExitedCount |> ignore)
      try
        let! failure = task {
          try
            do! client.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
            return None
          with :? DirectoryNotFoundException as error -> return Some error.Message
        }
        failure.IsSome |> Expect.isTrue "the post-Cleared socket-directory release fault fails StopAsync with its exact reason"
        client.IsProcessAlive |> Expect.isFalse "the fault follows a verified, real process exit"
        Volatile.Read &processExitedCount |> Expect.equal "ProcessExited never fires when StopAsync faults after Cleared" 0
      finally
        if Directory.Exists launch.EvidenceDirectory then Directory.Delete(launch.EvidenceDirectory, true)

      // Supervisor: an unprompted crash whose teardown faults after Cleared prunes nothing.
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-real-crash-cleanup-fault-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      let projClosed = Path.Combine(tempRoot, "ClosedProbe.cache-probe.fidproj")
      let projOpen = Path.Combine(tempRoot, "OpenProbe.cache-probe.fidproj")
      Directory.CreateDirectory cacheRoot |> ignore
      File.WriteAllText(projClosed, """{"name":"ClosedProbe"}""")
      File.WriteAllText(projOpen, """{"name":"OpenProbe"}""")
      let mutable creations = 0
      let evidenceDirs = ResizeArray<string>()
      let startFaultedWorker () = task {
        Interlocked.Increment &creations |> ignore
        let launch = config ()
        evidenceDirs.Add launch.EvidenceDirectory
        let terminate, register = cleanupFaultAfterCleared ()
        let! started = ComposerWorkerClient.startWithTerminationBoundary terminate ["--composer-worker-client-fixture"] launch
        return register started
      }
      let owner = ComposerSupervisor(startFaultedWorker, true, (fun () -> Task.CompletedTask), providerCacheRoot = cacheRoot)
      try
        let! openedClosed = owner.ExecuteAsync(Open(workerAddress, projClosed), CancellationToken.None)
        let closedSid = openedClosed.Reply.Authority.Session
        let closedManifest =
          match openedClosed.Reply.Outcome with
          | Result.Ok(Opened info) -> info.ManifestPath
          | other -> failtestf "expected Opened reply, got %A" other
        let! openedOpen = owner.ExecuteAsync(Open(workerAddress, projOpen), CancellationToken.None)
        Result.isOk openedOpen.Reply.Outcome |> Expect.isTrue "sibling session opened"
        let openSid = openedOpen.Reply.Authority.Session
        let! closed = owner.ExecuteAsync(Close(address closedSid), CancellationToken.None)
        Result.isOk closed.Reply.Outcome |> Expect.isTrue "closed session leaves its manifest deferred"
        let pid = owner.WorkerPid |> Option.defaultWith (fun () -> failtest "expected live worker PID")
        let teardownFailed = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
        use _sub =
          owner.Changed.Subscribe(fun () ->
            owner.CachedDirectory().Sessions
            |> Array.tryFind (fun s -> s.Reply.Authority.Session = openSid)
            |> Option.iter (fun s ->
              match s.WorkerError with
              | Some err when err.Contains("Worker teardown failed:", StringComparison.Ordinal) -> teardownFailed.TrySetResult err |> ignore
              | _ -> ()))
        use child = System.Diagnostics.Process.GetProcessById pid
        child.Kill true
        do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
        let! recordedError = teardownFailed.Task.WaitAsync(TimeSpan.FromSeconds 10.)
        recordedError |> Expect.stringContains "the cleanup fault is surfaced on the remaining session" "Could not find a part of the path"
        File.Exists closedManifest |> Expect.isTrue "a faulted StopAsync after Cleared must never prune the deferred manifest"
        let! refusedOpen = owner.ExecuteAsync(Open(workerAddress, projOpen), CancellationToken.None)
        match refusedOpen.Reply.Outcome with
        | Result.Error refusal -> refusal.Code |> Expect.equal "the faulted teardown keeps the worker fenced" RefusalCode.ProviderUnavailable
        | Result.Ok value -> failtestf "expected replacement Open refusal, got %A" value
        creations |> Expect.equal "no replacement worker starts after the faulted teardown" 1
        let! shutdownFailed = task {
          try
            do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.)
            return false
          with :? DirectoryNotFoundException -> return true
        }
        shutdownFailed |> Expect.isTrue "owner StopAsync surfaces the memoized cleanup fault"
        File.Exists closedManifest |> Expect.isTrue "the manifest stays unpruned after the faulted shutdown"
      finally
        for dir in evidenceDirs do
          if Directory.Exists dir then Directory.Delete(dir, true)
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }
  }
]
