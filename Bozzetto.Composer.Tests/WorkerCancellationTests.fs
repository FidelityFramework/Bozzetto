module Bozzetto.Composer.Tests.WorkerCancellationTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Bozzetto.Composer.WorkerProtocol
open Bozzetto.Providers
open Expecto
open Expecto.Flip

let private describeCompiler () =
  { AssemblyPath = "injected-compiler"; Sha256 = "injected-sha"; Version = "test" }

let private bounded (work: Task<'a>) = work.WaitAsync(TimeSpan.FromSeconds 5.)
let private property name (json: JsonElement) = json.GetProperty(name: string)
let private text name json = (property name json).GetString()
let private success json =
  (property "success" json).GetBoolean() |> Expect.isTrue (json.GetRawText())
  property "result" json
let private refusal code json =
  (property "success" json).GetBoolean() |> Expect.isFalse (json.GetRawText())
  text "code" (property "error" json) |> Expect.equal "refusal code" code
let private taskCase name work = testCaseAsync name (async { do! work () |> bounded |> Async.AwaitTask })

let private directory () =
  let cache =
    match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" |> Option.ofObj with
    | Some value when not (String.IsNullOrWhiteSpace value) && Path.IsPathFullyQualified value -> value
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
  let root = Path.Combine(cache, "bozzetto", "worker-contract-tests", Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory root |> ignore
  root

let private request id operation (identity: JsonElement option) extra =
  let fields = Dictionary<string, obj | null>()
  fields["protocolVersion"] <- box 1
  fields["requestId"] <- box id
  fields["operation"] <- box operation
  fields["provider"] <- box "clef-composer"
  match identity with
  | Some value ->
    for name in [ "host"; "session"; "epoch" ] do fields[name] <- box (text name value)
  | None -> ()
  for name, value in extra do fields[name] <- value
  JsonSerializer.SerializeToElement fields

let private hello (worker: Worker<int64>) = task {
  let! response = worker.Handle(request "hello" "hello" None [])
  success response |> ignore
  return property "authority" response
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
  let! opened = worker.Handle(request "open" "open" (Some identity) [ "project", box project ])
  success opened |> ignore
  let identity = property "authority" opened
  let! reserved = worker.Handle(request "reserve" "reserve" (Some identity) [ "label", box "initial" ])
  let reservation = reserved |> success |> text "reservation"
  if runOperation then
    let! accepted = worker.Handle(request "first-build" "build" (Some identity) [ "reservation", box reservation ])
    success accepted |> ignore
  use canceled = new CancellationTokenSource()
  canceled.Cancel()
  let operation, parameters = if runOperation then "run", [] else "build", [ "reservation", box reservation ]
  let! refused = worker.Handle(request "canceled-operation" operation (Some identity) parameters, cancellation = canceled.Token)
  refusal "canceled" refused
  let! status = worker.Handle(request "status" "status" (Some identity) [])
  (status |> success |> property "busy").GetBoolean() |> Expect.isFalse "forwarded cancellation leaves no stranded active request"
  ((status |> success |> property "current").ValueKind <> JsonValueKind.Null)
  |> Expect.equal "forwarded observer cancellation preserves current artifact authority" runOperation
  backend.Builds |> Expect.equal "pre-canceled build never enters backend" (if runOperation then 1 else 0)
  backend.Runs |> Expect.equal "pre-canceled run never launches an artifact" 0
  let! retired = worker.RetireAsync()
  retired |> Result.isOk |> Expect.isTrue "clean worker retirement"
}

[<Tests>]
let tests = testList "Composer worker request lifetime" [
  taskCase "private cancellation validates authority and targets only its exact request" <| fun () -> task {
    use target = new CancellationTokenSource()
    use unrelated = new CancellationTokenSource()
    let observed = ResizeArray<string>()
    let cancel id =
      observed.Add id
      if id = "held-build" then
        target.Cancel()
        true
      elif id = "other-build" then
        unrelated.Cancel()
        true
      else false
    use worker = new Worker<int64>(directory (), (fun _ _ -> failwith "No project should be opened"), describeCompiler, cancelRequest = cancel)
    let! identity = hello worker
    for overrides, code in [ [ "host", box "foreign" ], "wrong_authority"
                             [ "epoch", box "foreign" ], "wrong_authority"
                             [ "provider", box "fsharp" ], "wrong_provider" ] do
      let! refused = worker.Handle(request "invalid" "cancel_request" (Some identity) ([ "targetRequestId", box "held-build" ] @ overrides))
      refusal code refused
    observed.Count |> Expect.equal "invalid authority never reaches request cancellation" 0
    let! missing = worker.Handle(request "missing" "cancel_request" (Some identity) [])
    refusal "invalid_request" missing
    let! response = worker.Handle(request "cancel" "cancel_request" (Some identity) [ "targetRequestId", box "held-build" ])
    let result = success response
    (property "cancellationRequested" result).GetBoolean() |> Expect.isTrue "target acknowledged"
    text "targetRequestId" result |> Expect.equal "acknowledgement retains exact request id" "held-build"
    (property "authority" response).GetRawText() |> Expect.equal "request cancellation reports host authority" (identity.GetRawText())
    target.IsCancellationRequested |> Expect.isTrue "target token is canceled"
    unrelated.IsCancellationRequested |> Expect.isFalse "unrelated request token remains live"
    let! unknown = worker.Handle(request "unknown" "cancel_request" (Some identity) [ "targetRequestId", box "completed-request" ])
    (unknown |> success |> property "cancellationRequested").GetBoolean() |> Expect.isFalse "completed or unknown target is harmless"
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
    let opening = Task.Run<JsonElement>(Func<Task<JsonElement>>(fun () ->
      worker.Handle(request "held-open" "open" (Some identity) [ "project", box project ])))
    try
      do! bounded entered.Task
      let retirement = worker.RetireAsync()
      retirement.IsCompleted |> Expect.isFalse "fence accounts for already-admitted construction"
      release.TrySetResult() |> ignore
      let! refused = bounded opening
      refusal "compiler_retired" refused
      let! first = bounded retirement
      match first with
      | Result.Ok () -> failtest "First retirement forgot the overtaken open's failed cleanup"
      | Result.Error error -> error.Code |> Expect.equal "first fence reports cleanup failure" "cleanup_failed"
      let! second = bounded (worker.RetireAsync())
      second |> Expect.equal "later fence retains the failed detached session's cleanup evidence" first
      backend.Disposals |> Expect.equal "failure remains recorded without unsafe repeated backend cleanup" 1
      let! laterOpen = worker.Handle(request "late-open" "open" (Some identity) [ "project", box project ])
      refusal "compiler_retired" laterOpen
    finally
      release.TrySetResult() |> ignore
  }
]
