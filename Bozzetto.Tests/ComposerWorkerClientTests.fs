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

/// Child-only typed transport fixture; control barriers use fixture session IDs.
/// It records individual requests, not Calque's shared producer/demand state.
let runFixture socketPath =
  use socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
  socket.Connect(UnixDomainSocketEndPoint socketPath)
  use stream = new NetworkStream(socket, ownsSocket = false)
  let held = ResizeArray<Request>()
  let withdrawn = Collections.Generic.HashSet<string>()
  let replyOutcome (request: Request) outcome =
    let response: Reply =
      { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = request.RequestId
        Authority = { Host = workerAddress.Host; Epoch = workerAddress.Epoch; Provider = workerAddress.Provider
                      Generation = 0L; Session = ComposerWire.session request.Body }
        Outcome = outcome }
    StreamFrames.writeReplyAsync stream CancellationToken.None response |> fun work -> work.GetAwaiter().GetResult()
    |> Result.defaultWith (fun error -> failwithf "Fixture encoding: %A" error)
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
          Operations = [|Operation.Hello; Operation.Status; Operation.Build; Operation.Run; Operation.Format; Operation.CancelRequest|]
          InMemoryPatchAllowed = false })
      | Status target when target.Session = "barrier" -> reply request (Observed(status target.Session held.Count))
      | Status target when target.Session = "hold-after-close" ->
        holdAfterClose <- true
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
  try
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
  finally
    release.Set()
    canceling.WaitAsync(TimeSpan.FromSeconds 20.).GetAwaiter().GetResult()
    client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.).GetAwaiter().GetResult()
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
  try
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
  finally
    release.TrySetResult() |> ignore
    client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.).GetAwaiter().GetResult()
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
  try
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
  finally
    release.Set()
    canceling.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    try client.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    with _ when not verified -> ()
}

[<Tests>]
let tests = testList "Composer worker socket client" [
  testTask "build withdrawal retains capacity while peer requests survive" { do! capacity Operation.Build }
  testTask "run withdrawal retains capacity while peer requests survive" { do! capacity Operation.Run }
  testTask "format withdrawal retains capacity while peer requests survive" { do! capacity Operation.Format }
  for phase in ["initial"; "encoding"; "queued"; "writing"; "sent"] do
    testTask (phase + " cancellation drains owned work without consuming unrelated capacity") { do! cancellationPhase Operation.Build phase }
    testTask ("format " + phase + " cancellation drains owned work without consuming unrelated capacity") { do! cancellationPhase Operation.Format phase }
  testCaseAsync "format cancellation targets one wire request when session and buffer are identical" (async {
    let! client = start ignore (fun _ _ -> Task.CompletedTask) |> Async.AwaitTask
    use cancellation = new CancellationTokenSource()
    try
      let body = ownedRequest Operation.Format "shared-session"
      let victim = client.RequestAsync(body, cancellation.Token)
      let peer = client.RequestAsync(body, CancellationToken.None)
      let! before = client.RequestAsync(request "format-requests", CancellationToken.None) |> bounded |> Async.AwaitTask
      count before |> Expect.equal "Both exact-buffer requests have reached the worker." 2
      cancellation.Cancel()
      let! detached = canceled victim |> Async.AwaitTask
      detached |> Expect.isTrue "Caller cancellation completes after its targeted withdrawal acknowledgement."
      let! remaining = client.RequestAsync(request "format-requests", CancellationToken.None) |> bounded |> Async.AwaitTask
      count remaining |> Expect.equal "Only the canceled request is withdrawn at the worker boundary." 1
      let! withdrawals = client.RequestAsync(request "withdrawals", CancellationToken.None) |> bounded |> Async.AwaitTask
      count withdrawals |> Expect.equal "The worker received one exact request cancellation." 1
      peer.IsCompleted |> Expect.isFalse "The other observer remains attached to its result."
      let! released = client.RequestAsync(request "release", CancellationToken.None) |> bounded |> Async.AwaitTask
      count released |> Expect.equal "Both physical responses are still correlated and drained." 2
      let! survived = peer |> bounded |> Async.AwaitTask
      match survived.Outcome with
      | Result.Ok(Formatted preview) ->
        preview.Document |> Expect.equal "The peer keeps its immutable document identity." formatBuffer.Document
        preview.Formatted |> Expect.equal "The peer receives its requested preview." formatBuffer.Source
      | outcome -> failtestf "The uncanceled formatter observer did not complete: %A" outcome
      let! final = client.RequestAsync(request "barrier", CancellationToken.None) |> bounded |> Async.AwaitTask
      count final |> Expect.equal "No physical request remains held." 0
      client.IsAlive |> Expect.isTrue "Targeted withdrawal preserves the shared worker."
    finally client.StopAsync().WaitAsync(TimeSpan.FromSeconds 15.).GetAwaiter().GetResult()
  })
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
  testTask "close retains ownership of a held cancellation callback after physical child exit" { do! closeRetainsCallback Operation.Build }
  testTask "close retains ownership of a held format cancellation callback after physical child exit" { do! closeRetainsCallback Operation.Format }
]
