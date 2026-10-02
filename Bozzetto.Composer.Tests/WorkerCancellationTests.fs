module Bozzetto.Composer.Tests.WorkerCancellationTests

open System
open System.IO
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

let private canceledOperation runOperation () = task {
  let root = directory ()
  let projectDirectory = Path.Combine(root, "project")
  Directory.CreateDirectory projectDirectory |> ignore
  let project = Path.Combine(projectDirectory, "test.fidproj")
  File.WriteAllText(project, "test fixture: backend injected; no compiler invoked")
  let backend = new Backend(false)
  use worker = new Worker<int64>(Path.Combine(root, "sessions"), (fun _ _ -> backend), describeCompiler)
  let! identity = hello worker
  let! opened = worker.Handle(request "open" (Open(address identity, project)))
  success opened |> ignore
  let target = session opened.Authority
  let! reserved = worker.Handle(request "reserve" (Reserve(target, "initial")))
  let token = reservation reserved
  if runOperation then
    let! accepted = worker.Handle(request "first-build" (Build(target, token)))
    success accepted |> ignore
  use canceled = new CancellationTokenSource()
  canceled.Cancel()
  let body = if runOperation then RequestBody.Run(target, [||]) else Build(target, token)
  let! refused = worker.Handle(request "canceled-operation" body, cancellation = canceled.Token)
  refusal RefusalCode.Canceled refused
  let! status = worker.Handle(request "status" (Status target))
  let status = observed status
  status.Busy |> Expect.isFalse "forwarded cancellation leaves no stranded active request"
  status.Current.IsSome |> Expect.equal "forwarded observer cancellation preserves current artifact authority" runOperation
  backend.Builds |> Expect.equal "pre-canceled build never enters backend" (if runOperation then 1 else 0)
  backend.Runs |> Expect.equal "pre-canceled run never launches an artifact" 0
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

  taskCase "worker forwards pre-canceled build token to captured session revision" (canceledOperation false)
  taskCase "worker forwards pre-canceled run token before native invocation" (canceledOperation true)

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
