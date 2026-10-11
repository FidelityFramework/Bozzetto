module Bozzetto.Composer.Tests.WorkerCancellationTests

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Bozzetto.Composer.WorkerProtocol
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Expecto
open Expecto.Flip

let private describeCompiler () : CompilerIdentity =
  { AssemblyPath = "injected-compiler"; Sha256 = "injected-sha"; Version = "test" }

let private bounded (work: Task<'a>) = work.WaitAsync(TimeSpan.FromSeconds 5.)
let private success (reply: Bozzetto.Composer.Protocol.Reply) =
  match reply.Outcome with
  | Result.Ok body -> body
  | Result.Error error -> failtestf "Unexpected typed refusal: %A" error
let private refusal code (reply: Bozzetto.Composer.Protocol.Reply) =
  match reply.Outcome with
  | Result.Error error -> error.Code |> Expect.equal "refusal code" code
  | Result.Ok body -> failtestf "Expected typed refusal, received %A" body
let private taskCase name work = testCaseAsync name (async { do! work () |> bounded |> Async.AwaitTask })
let private address (identity: Authority) : WorkerAddress =
  { Host = identity.Host; Epoch = identity.Epoch; Provider = identity.Provider }
let private session (identity: Authority) : SessionAddress =
  { Worker = address identity; Session = identity.Session }
let private request id body : Request =
  { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = id; Body = body }
let private observed reply =
  match success reply with Observed value -> value | body -> failtestf "Expected status, received %A" body
let private reservation reply =
  match success reply with Reserved value -> value.Reservation | body -> failtestf "Expected reservation, received %A" body

let private directory () =
  let cache =
    match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" |> Option.ofObj with
    | Some value when not (String.IsNullOrWhiteSpace value) && Path.IsPathFullyQualified value -> value
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
  let root = Path.Combine(cache, "bozzetto", "worker-contract-tests", Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory root |> ignore
  root

let private hello (worker: Worker<int64>) = task {
  let! response = worker.Handle(request "hello" (Hello BAREWireCodec.agreement))
  match success response with HelloAccepted _ -> () | body -> failtestf "Expected hello, received %A" body
  return response.Authority
}

type private Backend(failCleanup: bool) =
  let mutable generation = 0L
  let mutable builds = 0
  let mutable runs = 0
  let mutable disposals = 0
  member _.Builds = builds
  member _.Runs = runs
  member _.Disposals = disposals
  interface IProjectBackend<int64> with
    member _.ManifestPath = "unused-in-memory-manifest"
    member _.Current = None
    member _.Reserve _ = generation <- generation + 1L; generation
    member _.BuildAsync(ticket, _) =
      builds <- builds + 1
      Task.FromResult(Result.Ok {
        Generation = ticket; SourceVersion = "in-memory-source"
        ArtifactPath = "unused"; ArtifactSha256 = "unused"; ObjectManifest = "unused"
        ChangedWitnesses = [||]; RetainedWitnesses = [||]; RetiredWitnesses = [||]
        WitnessVisits = [||]; CompiledObjects = [||]; ReusedObjects = [||]; RetiredObjects = [||] })
    member _.RunCurrentAsync(_, _) =
      runs <- runs + 1
      Task.FromResult(Result.Ok {
        Generation = generation; SourceVersion = "in-memory-source"
        ExitCode = 0; StandardOutput = "unused"; StandardError = "" })
    member _.Dispose() =
      disposals <- disposals + 1
      if failCleanup then failwith "sticky injected cleanup failure"

/// Every accepted preview has a separate withdrawal signal and a physical join.
/// Holding Observe lets the test drive the worker token after admission, instead
/// of accepting a pre-canceled reply as evidence that the callback ran.
type private PendingPreview(buffer: FormatBuffer, held: bool) =
  let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let withdrawn = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let joined = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  do if not held then release.SetResult()
  member _.Entered = entered.Task
  member _.Withdrawn = withdrawn.Task
  member _.Joined = joined.Task
  member _.Release() = release.TrySetResult() |> ignore
  member _.Demand = {
    Observe = async {
      entered.TrySetResult() |> ignore
      try
        do! release.Task |> Async.AwaitTask
        return Result.Ok {
          Document = buffer.Document; Incarnation = buffer.Incarnation; Revision = buffer.Revision
          SourceSha256 = FormatPolicy.sourceSha256 buffer.Source; Configuration = buffer.Configuration
          FormatterIdentity = "held-format-owner"; Formatted = buffer.Source
        }
      finally joined.TrySetResult() |> ignore
    }
    Withdraw = fun () -> withdrawn.TrySetResult() |> ignore
  }

type private RecordingFormatter(held: bool) =
  let previews = Collections.Concurrent.ConcurrentQueue<PendingPreview>()
  let diagnostics = Collections.Concurrent.ConcurrentQueue<FormatterDiagnostic>()
  let first = TaskCompletionSource<PendingPreview>(TaskCreationOptions.RunContinuationsAsynchronously)
  let second = TaskCompletionSource<PendingPreview>(TaskCreationOptions.RunContinuationsAsynchronously)
  let mutable count = 0
  member _.Admissions = Volatile.Read(&count)
  member _.First = first.Task
  member _.Second = second.Task
  member _.QueueDiagnostic diagnostic = diagnostics.Enqueue diagnostic
  member _.ReleaseAll() = for preview in previews do preview.Release()
  interface IFormatBackend with
    member _.RequestPreview buffer =
      let preview = PendingPreview(buffer, held)
      previews.Enqueue preview
      match Interlocked.Increment(&count) with
      | 1 -> first.TrySetResult preview |> ignore
      | 2 -> second.TrySetResult preview |> ignore
      | _ -> ()
      Result.Ok preview.Demand
    member _.BeginClose() = for preview in previews do preview.Demand.Withdraw()
    member _.DrainRetirements() = []
    member _.CloseAsync() = async {
      for preview in previews do do! preview.Joined |> Async.AwaitTask
      return Result.Ok ()
    }
    member _.DrainDiagnostics() =
      let captured = ResizeArray<FormatterDiagnostic>()
      let mutable diagnostic = Unchecked.defaultof<FormatterDiagnostic>
      while diagnostics.TryDequeue(&diagnostic) do captured.Add diagnostic
      captured |> Seq.toList

let private canceledOperation operation () = task {
  let root = directory ()
  let projectDirectory = Path.Combine(root, "project")
  Directory.CreateDirectory projectDirectory |> ignore
  let project = Path.Combine(projectDirectory, "test.fidproj")
  File.WriteAllText(project, "test fixture: backend injected; no compiler invoked")
  let backend = new Backend(false)
  let formatter = RecordingFormatter(false)
  use worker = new Worker<int64>(Path.Combine(root, "sessions"), (fun _ _ -> backend), describeCompiler,
    createFormatter = (fun () -> formatter))
  let! identity = hello worker
  let! opened = worker.Handle(request "open" (Open(address identity, project)))
  success opened |> ignore
  let target = session opened.Authority
  let! reserved = worker.Handle(request "reserve" (Reserve(target, "initial")))
  let token = reservation reserved
  let runOperation = operation = Operation.Run
  if runOperation then
    let! accepted = worker.Handle(request "first-build" (Build(target, token)))
    success accepted |> ignore
  use canceled = new CancellationTokenSource()
  canceled.Cancel()
  let body =
    match operation with
    | Operation.Build -> Build(target, token)
    | Operation.Run -> RequestBody.Run(target, [||])
    | Operation.Format ->
      Format(target, reserved.Authority.Generation, {
        Document = "buffer://cancellation/main.clef"; Incarnation = Guid.NewGuid().ToString("D")
        Revision = 1UL; Source = "module Main\nlet value=42\n"; Configuration = FormatPolicy.Configuration })
    | _ -> failtest "Expected build, run or format cancellation."
  let! refused = worker.Handle(request "canceled-operation" body, cancellation = canceled.Token)
  refusal RefusalCode.Canceled refused
  let! status = worker.Handle(request "status" (Status target))
  let status = observed status
  status.Busy |> Expect.isFalse "forwarded cancellation leaves no stranded active request"
  status.Current.IsSome |> Expect.equal "forwarded observer cancellation preserves current artifact authority" runOperation
  backend.Builds |> Expect.equal "pre-canceled build never enters backend" (if runOperation then 1 else 0)
  backend.Runs |> Expect.equal "pre-canceled run never launches an artifact" 0
  formatter.Admissions |> Expect.equal "pre-canceled format never attaches a formatter demand" 0
  let! retired = worker.RetireAsync()
  retired |> Result.isOk |> Expect.isTrue "clean worker retirement"
}

[<Tests>]
let tests = testList "Composer worker request lifetime" [
  taskCase "wire status preserves provider observation order within one compiler generation" <| fun () -> task {
    let root = directory ()
    let projectDirectory = Path.Combine(root, "project")
    Directory.CreateDirectory projectDirectory |> ignore
    let project = Path.Combine(projectDirectory, "status.fidproj")
    File.WriteAllText(project, "in-memory backend fixture")
    let backend = new Backend(false)
    use worker = new Worker<int64>(Path.Combine(root, "sessions"), (fun _ _ -> backend), describeCompiler)
    let! identity = hello worker
    let! opened = worker.Handle(request "open" (Open(address identity, project)))
    let first = match success opened with Opened value -> value.Observation | body -> failtestf "Expected opened, received %A" body
    let target = session opened.Authority
    let! before = worker.Handle(request "before" (Status target))
    let! after = worker.Handle(request "after" (Status target))
    before.Authority |> Expect.equal "observation reads do not mint compiler authority" after.Authority
    [ first; (observed before).Observation; (observed after).Observation ]
    |> Expect.equal "wire values retain actual capture ordering" [ 1UL; 2UL; 3UL ]
    let! retired = worker.RetireAsync()
    retired |> Result.isOk |> Expect.isTrue "status fixture closes its owner"
  }

  taskCase "private cancellation validates authority and targets only its exact request" <| fun () -> task {
    use target = new CancellationTokenSource()
    use unrelated = new CancellationTokenSource()
    let observed = ResizeArray<string>()
    let cancel id =
      observed.Add id
      if id = "held-build" then target.Cancel(); true
      elif id = "other-build" then unrelated.Cancel(); true
      else false
    use worker = new Worker<int64>(directory (), (fun _ _ -> failwith "No project should be opened"), describeCompiler, cancelRequest = cancel)
    let! identity = hello worker
    let targetAddress = address identity
    for invalidAddress, code in [
      { targetAddress with Host = "foreign" }, RefusalCode.WrongAuthority
      { targetAddress with Epoch = "foreign" }, RefusalCode.WrongAuthority
      { targetAddress with Provider = ProviderIdentity.FSharp }, RefusalCode.WrongProvider ] do
      let! refused = worker.Handle(request "invalid" (CancelRequest(invalidAddress, "held-build")))
      refusal code refused
    observed.Count |> Expect.equal "invalid authority never reaches request cancellation" 0
    let! missing = worker.Handle(request "missing" (CancelRequest(targetAddress, "")))
    refusal RefusalCode.InvalidRequest missing
    let! response = worker.Handle(request "cancel" (CancelRequest(targetAddress, "held-build")))
    let result = match success response with RequestCanceled value -> value | body -> failtestf "Expected cancellation, received %A" body
    result.CancellationRequested |> Expect.isTrue "target acknowledged"
    result.TargetRequestId |> Expect.equal "acknowledgement retains exact request id" "held-build"
    response.Authority |> Expect.equal "request cancellation reports host authority" identity
    target.IsCancellationRequested |> Expect.isTrue "target token is canceled"
    unrelated.IsCancellationRequested |> Expect.isFalse "unrelated request token remains live"
    let! unknown = worker.Handle(request "unknown" (CancelRequest(targetAddress, "completed-request")))
    let unknown = match success unknown with RequestCanceled value -> value | body -> failtestf "Expected cancellation, received %A" body
    unknown.CancellationRequested |> Expect.isFalse "completed or unknown target is harmless"
    observed |> Seq.toList |> Expect.equal "callback receives exact explicit targets only" [ "held-build"; "completed-request" ]
  }

  taskCase "worker forwards pre-canceled build token to captured session revision" (canceledOperation Operation.Build)
  taskCase "worker forwards pre-canceled run token before native invocation" (canceledOperation Operation.Run)
  taskCase "worker forwards pre-canceled format token before attaching preview demand" (canceledOperation Operation.Format)

  taskCase "mid-flight format token withdraws only its admitted demand and joins observation" <| fun () -> task {
    let root = directory ()
    let project = Path.Combine(root, "project", "mid-flight.fidproj")
    Directory.CreateDirectory(Path.GetDirectoryName project) |> ignore
    File.WriteAllText(project, "injected formatter owner; no compiler invocation")
    let backend = new Backend(false)
    let formatter = RecordingFormatter(true)
    use worker = new Worker<int64>(Path.Combine(root, "sessions"), (fun _ _ -> backend), describeCompiler,
      createFormatter = (fun () -> formatter))
    use cancellation = new CancellationTokenSource()
    try
      let! identity = hello worker
      let! opened = worker.Handle(request "open" (Open(address identity, project)))
      success opened |> ignore
      let target = session opened.Authority
      let buffer = {
        Document = "buffer://cancellation/mid-flight.clef"; Incarnation = Guid.NewGuid().ToString("D")
        Revision = 1UL; Source = "module Main\nlet value=42\n"; Configuration = FormatPolicy.Configuration
      }
      let canceled = worker.Handle(request "first-format" (Format(target, 0L, buffer)), cancellation = cancellation.Token)
      let! first = formatter.First
      do! first.Entered
      let peer = worker.Handle(request "peer-format" (Format(target, 0L, buffer)))
      let! second = formatter.Second
      do! second.Entered
      formatter.Admissions |> Expect.equal "both requests entered the formatter before cancellation" 2
      cancellation.Cancel()
      do! first.Withdrawn
      second.Withdrawn.IsCompleted |> Expect.isFalse "caller token cannot release the peer demand"
      canceled.IsCompleted |> Expect.isFalse "withdrawal preserves the original observation's cleanup join"
      peer.IsCompleted |> Expect.isFalse "peer still owns its live observation"
      formatter.ReleaseAll()
      let! canceledReply = canceled
      refusal RefusalCode.Canceled canceledReply
      first.Joined.IsCompleted |> Expect.isTrue "canceled reply follows physical observation completion"
      let! peerReply = peer
      match success peerReply with
      | Formatted preview -> preview.Revision |> Expect.equal "peer retains the same immutable revision" 1UL
      | body -> failtestf "Expected peer format result, received %A" body
      let! status = worker.Handle(request "status" (Status target))
      status.Authority.Generation |> Expect.equal "request cancellation does not reserve the shared session" 0L
      (observed status).FormatterError |> Expect.isNone "normal request cancellation is not a formatter fault"
    finally formatter.ReleaseAll()
    let! retired = worker.RetireAsync()
    retired |> Result.isOk |> Expect.isTrue "both formatter observations and worker owner close"
  }

  taskCase "formatter diagnostics survive compiler reservation cancellation and close in a distinct field" <| fun () -> task {
    let backend = new Backend(false)
    let formatter = RecordingFormatter(false)
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/injected/project.fidproj", backend, formatter = formatter)
    formatter.QueueDiagnostic {
      Document = { Uri = "buffer://diagnostics/main.clef"; Incarnation = Guid.Parse("00000000-0000-0000-0000-000000000001") }
      Diagnostic = { Attempt = Fidelity.FSharp.Incremental.AttemptId 19UL
                     Failure = { Code = "formatter-host-failure"; Message = "retained formatter evidence" } }
    }
    let snapshot () = owner.Status().Outcome |> Result.defaultWith (fun error -> failtestf "Status failed: %A" error)
    let first = snapshot ()
    first.BackendError |> Expect.isNone "formatter evidence never occupies the compiler error field"
    let evidence = first.FormatterError |> Option.defaultWith (fun () -> failtest "Formatter diagnostic is missing")
    for text in [ "buffer://diagnostics/main.clef"; "19UL"; "formatter-host-failure"; "retained formatter evidence" ] do
      evidence.Contains text |> Expect.isTrue "formatter status preserves complete typed evidence"
    let! reservation = owner.ReserveAsync "compiler edit"
    reservation.Outcome |> Result.isOk |> Expect.isTrue "successful compiler reservation"
    (snapshot ()).FormatterError |> Expect.equal "reservation cannot clear formatter evidence" (Some evidence)
    owner.Cancel().Outcome |> Result.isOk |> Expect.isTrue "compiler cancellation accepted"
    (snapshot ()).FormatterError |> Expect.equal "cancellation cannot overwrite formatter evidence" (Some evidence)
    let! closed = owner.CloseAsync()
    closed.Outcome |> Result.isOk |> Expect.isTrue "all compiler controls and formatter cleanup joined"
    let final = snapshot ()
    final.FormatterError |> Expect.equal "closed status retains evidence for the retired session" (Some evidence)
    final.BackendError |> Expect.isNone "successful compiler controls have their own status"
  }

  taskCase "formatter evidence stays bounded and wire observable after repeated and oversized Unicode faults" <| fun () -> task {
    let root = directory ()
    let projectDirectory = Path.Combine(root, "project")
    Directory.CreateDirectory projectDirectory |> ignore
    let project = Path.Combine(projectDirectory, "bounded-diagnostics.fidproj")
    File.WriteAllText(project, "injected formatter diagnostics; no compiler invocation")
    let backend = new Backend(false)
    let formatter = RecordingFormatter(false)
    use worker = new Worker<int64>(Path.Combine(root, "sessions"), (fun _ _ -> backend), describeCompiler,
      createFormatter = (fun () -> formatter))
    let! identity = hello worker
    let! opened = worker.Handle(request "open" (Open(address identity, project)))
    success opened |> ignore
    let target = session opened.Authority
    let utf8 = UTF8Encoding(false, true)
    let record attempt message = formatter.QueueDiagnostic {
      Document = { Uri = "buffer://diagnostics/bounded.clef"; Incarnation = Guid.Parse("00000000-0000-0000-0000-000000000001") }
      Diagnostic = { Attempt = Fidelity.FSharp.Incremental.AttemptId attempt
                     Failure = { Code = "formatter-host-failure"; Message = message } }
    }
    let roundtrip reply =
      let bytes = BAREWireCodec.encodeReply reply |> Result.defaultWith (fun error -> failtestf "Evidence prevented reply encoding: %A" error)
      BAREWireCodec.decodeReply bytes |> Expect.equal "bounded evidence roundtrips through strict UTF-8 binary replies" (Result.Ok reply)
      reply
    let readEvidence reply =
      let value = observed (roundtrip reply)
      value.BackendError |> Expect.isNone "formatter evidence remains separate from compiler failure"
      let evidence = value.FormatterError |> Option.defaultWith (fun () -> failtest "Formatter evidence is missing")
      utf8.GetByteCount evidence <= 16 * 1024 |> Expect.isTrue "formatter evidence including marker fits its UTF-8 byte budget"
      evidence
    let mutable cumulativeBytes = 0
    let mutable retained = ""
    for number in 1 .. 300 do
      let message = sprintf "fault-%04d|%s|complete-%04d" number (String('x', 4096)) number
      cumulativeBytes <- cumulativeBytes + utf8.GetByteCount message
      record (uint64 number) message
      let! reply = worker.Handle(request "repeated-status" (Status target))
      retained <- readEvidence reply
    cumulativeBytes > BAREWireCodec.MaximumBody |> Expect.isTrue "fixture exceeds a full worker frame cumulatively"
    retained.Contains "[formatter evidence truncated]" |> Expect.isTrue "status reports historical evidence loss"
    retained.Contains "fault-0001|" |> Expect.isFalse "oldest whole entries are evicted"
    retained.Contains "fault-0300|" |> Expect.isTrue "newest complete entry remains available"
    retained.EndsWith "|complete-0300" |> Expect.isTrue "ordinary retained entries are not clipped to fill the budget"
    record 301UL (String.replicate 100000 "\U0001F600\uD800界")
    let! oversized = worker.Handle(request "oversized-status" (Status target))
    let oversizedEvidence = readEvidence oversized
    oversizedEvidence.Contains "Attempt=301UL" |> Expect.isTrue "oversized entry retains diagnostic identity"
    oversizedEvidence.Contains "\U0001F600" |> Expect.isTrue "four-byte Unicode survives at scalar boundaries"
    oversizedEvidence.Contains "\uFFFD" |> Expect.isTrue "malformed surrogate text is normalized for the strict wire"
    oversizedEvidence.Contains "[formatter evidence truncated]" |> Expect.isTrue "oversized evidence is explicitly marked"
    record 302UL "post-truncation evidence"
    let! latest = worker.Handle(request "latest-status" (Status target))
    let latestEvidence = readEvidence latest
    latestEvidence.Contains "post-truncation evidence" |> Expect.isTrue "a later small fault remains recordable"
    latestEvidence.Contains "[formatter evidence truncated]" |> Expect.isTrue "truncation remains visible after the oversized entry leaves"
    let! reserved = worker.Handle(request "reserve" (Reserve(target, "compiler edit")))
    success (roundtrip reserved) |> ignore
    let! canceled = worker.Handle(request "cancel" (Cancel target))
    success (roundtrip canceled) |> ignore
    let! closed = worker.Handle(request "close" (Close target))
    success (roundtrip closed) |> ignore
    let! final = worker.Handle(request "closed-status" (Status target))
    (observed final).Closed |> Expect.isTrue "fault retention cannot prevent close observation"
    readEvidence final |> Expect.equal "reservation cancellation and close preserve the bounded evidence" latestEvidence
    let! retired = worker.RetireAsync()
    retired |> Result.isOk |> Expect.isTrue "bounded evidence does not strand worker retirement"
  }

  taskCase "formatter close deadline seals every worker session while physical cleanup remains owned" <| fun () -> task {
    let root = directory ()
    let projectDirectory = Path.Combine(root, "project")
    Directory.CreateDirectory projectDirectory |> ignore
    let project = Path.Combine(projectDirectory, "deadline.fidproj")
    File.WriteAllText(project, "injected worker with owned formatter cleanup")
    let formatters = ResizeArray<RecordingFormatter>()
    let createFormatter () =
      let formatter = RecordingFormatter(true)
      formatters.Add formatter
      formatter :> IFormatBackend
    let mutable timestamp = 0L
    use worker = new Worker<int64>(Path.Combine(root, "sessions"), (fun _ _ -> new Backend(false)), describeCompiler,
      createFormatter = createFormatter, formatterCleanupTimestamp = (fun () -> Interlocked.Read(&timestamp)))
    try
      let! identity = hello worker
      let! first = worker.Handle(request "first-open" (Open(address identity, project)))
      let! second = worker.Handle(request "second-open" (Open(address identity, project)))
      success first |> ignore
      success second |> ignore
      let firstTarget, peerTarget = session first.Authority, session second.Authority
      let buffer = {
        Document = "buffer://deadline/main.clef"; Incarnation = Guid.NewGuid().ToString("D")
        Revision = 1UL; Source = "module Deadline\nlet value=1\n"; Configuration = FormatPolicy.Configuration
      }
      let formatting = worker.Handle(request "format" (Format(firstTarget, 0L, buffer)))
      let! pending = formatters[0].First
      do! pending.Entered
      let! closing = worker.Handle(request "close" (Close firstTarget))
      match success closing with
      | Closed state -> state.CleanupPending |> Expect.isTrue "close returns observable pending cleanup"
      | body -> failtestf "Expected close, received %A" body
      let! initial = worker.Handle(request "pending-status" (Status firstTarget))
      (observed initial).FormatterCleanupPending |> Expect.isTrue "formatter close is monitored independently of compiler drain"
      (observed initial).WorkerRetirementRequired |> Expect.isNone "initial cleanup has its grace interval"
      Interlocked.Exchange(&timestamp, 30L * Diagnostics.Stopwatch.Frequency) |> ignore
      let! expired = worker.Handle(request "expired-status" (Status firstTarget))
      let state = observed expired
      state.WorkerRetirementRequired |> Expect.isSome "owner issues a sticky worker-level escalation"
      state.FormatterCleanupPending |> Expect.isTrue "expiration does not settle the held observer"
      let! peer = worker.Handle(request "peer-reserve" (Reserve(peerTarget, "must be fenced")))
      refusal RefusalCode.Closed peer
      let! reopen = worker.Handle(request "reopen" (Open(address identity, project)))
      refusal RefusalCode.CompilerRetired reopen
      formatting.IsCompleted |> Expect.isFalse "worker keeps the original observation until physical release"
      for formatter in formatters do formatter.ReleaseAll()
      let! _ = formatting
      let! retired = worker.RetireAsync()
      retired |> Result.isOk |> Expect.isTrue "late cleanup physically joins the sealed worker"
      let! final = worker.Handle(request "final-status" (Status firstTarget))
      (observed final).WorkerRetirementRequired |> Expect.equal "late completion cannot rescind worker retirement" state.WorkerRetirementRequired
      (observed final).FormatterCleanupPending |> Expect.isFalse "physical completion is reported separately"
    finally
      for formatter in formatters do formatter.ReleaseAll()
  }

  taskCase "retirement remembers cleanup failure from an overtaken open across later fences" <| fun () -> task {
    let root = directory ()
    let projectDirectory = Path.Combine(root, "project")
    Directory.CreateDirectory projectDirectory |> ignore
    let project = Path.Combine(projectDirectory, "test.fidproj")
    File.WriteAllText(project, "backend-injected fixture")
    let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let backend = new Backend(true)
    let create _ _ =
      entered.TrySetResult() |> ignore
      release.Task.GetAwaiter().GetResult()
      backend :> IProjectBackend<int64>
    use worker = new Worker<int64>(Path.Combine(root, "sessions"), create, describeCompiler)
    let! identity = hello worker
    let opening = Task.Run<Bozzetto.Composer.Protocol.Reply>(Func<Task<Bozzetto.Composer.Protocol.Reply>>(fun () ->
      worker.Handle(request "held-open" (Open(address identity, project)))))
    try
      do! bounded entered.Task
      let retirement = worker.RetireAsync()
      retirement.IsCompleted |> Expect.isFalse "fence accounts for already-admitted construction"
      release.TrySetResult() |> ignore
      let! refused = bounded opening
      refusal RefusalCode.CompilerRetired refused
      let! first = bounded retirement
      match first with
      | Result.Ok () -> failtest "First retirement forgot the overtaken open's failed cleanup"
      | Result.Error error -> error.Code |> Expect.equal "first fence reports cleanup failure" "cleanup_failed"
      let! second = bounded (worker.RetireAsync())
      second |> Expect.equal "later fence retains the failed detached session's cleanup evidence" first
      backend.Disposals |> Expect.equal "failure remains recorded without unsafe repeated backend cleanup" 1
      let! laterOpen = worker.Handle(request "late-open" (Open(address identity, project)))
      refusal RefusalCode.CompilerRetired laterOpen
    finally
      release.TrySetResult() |> ignore
  }
]
