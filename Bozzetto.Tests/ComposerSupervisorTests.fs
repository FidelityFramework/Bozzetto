module Bozzetto.Tests.ComposerSupervisorTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Bozzetto.ComposerIntegration
open Expecto
open Expecto.Flip

let private json value = JsonSerializer.SerializeToElement value
let private empty = json {| |}
let private field name (value: JsonElement) = value.GetProperty(name: string)
let private text name value = (field name value).GetString()
let private success response =
  (field "success" response).GetBoolean() |> Expect.isTrue ("successful response: " + response.GetRawText())
  field "result" response
let private refused code response =
  (field "success" response).GetBoolean() |> Expect.isFalse ("refused response: " + response.GetRawText())
  text "code" (field "error" response) |> Expect.equal "refusal classification" code
let private sessionOf response = text "session" (field "authority" response)
let private completion<'a> () = TaskCompletionSource<'a>(TaskCreationOptions.RunContinuationsAsynchronously)
let private bounded (work: Task<'a>) = work.WaitAsync(TimeSpan.FromSeconds 5.)
let private taskCase name work = testCaseAsync name (async { do! work () |> bounded |> Async.AwaitTask })

type private SessionState = {
  mutable Generation: int64
  mutable Observation: uint64
  mutable Current: JsonElement
  mutable Closed: bool
  mutable Busy: bool
}

/// In-memory process boundary with controllable responses and termination.
/// The supervisor still receives full worker protocol envelopes and owns all
/// routing, cache withdrawal and lifecycle decisions exercised below.
type private Worker(host: string, pid: int) =
  let gate = obj ()
  let exited = Event<string>()
  let requests = ConcurrentQueue<string * string>()
  let tokens = ConcurrentQueue<string * CancellationToken>()
  let sessions = Dictionary<string, SessionState>()
  let mutable alive = true
  let mutable stops = 0
  let identity session generation =
    {| host = host; session = session; epoch = host + "-epoch"
       provider = "clef-composer"; generation = generation |}
  let reply session generation (result: JsonElement) (error: JsonElement option) =
    json {| protocolVersion = 1; requestId = Guid.NewGuid().ToString("N")
            authority = identity session generation
            success = error.IsNone; result = result
            error = error |> Option.defaultValue empty |}
  let accepted generation =
    json {| generation = generation; sourceVersion = "source-" + string generation
            artifactPath = "/external/provider/program"; artifactSha256 = "artifact-digest"
            objectManifest = "/external/provider/objects.json"
            changedWitnesses = [||]; retainedWitnesses = [||]; retiredWitnesses = [||]
            witnessVisits = [||]; compiledObjects = [||]; reusedObjects = [||]; retiredObjects = [||] |}
  member _.Host = host
  member _.Epoch = host + "-epoch"
  member _.Requests = requests.ToArray()
  member _.Tokens = tokens.ToArray()
  member _.StopCount = Volatile.Read &stops
  member _.Alive = lock gate (fun () -> alive)
  member val Intercept: string -> string -> Task<JsonElement> option = (fun _ _ -> None) with get, set
  member val CleanupFails = false with get, set
  member val StopThrows = false with get, set
  member val StopLeavesAlive = false with get, set
  member _.Die() =
    lock gate (fun () -> alive <- false)
    exited.Trigger "worker process exited"
  member _.Withdraw(session, busy) =
    lock gate (fun () ->
      let state = sessions[session]
      state.Generation <- state.Generation + 1L
      state.Current <- json (null: obj)
      state.Busy <- busy)
  member _.SetBusy(session, busy) = lock gate (fun () -> sessions[session].Busy <- busy)
  member this.Response(operation, session) =
    lock gate (fun () ->
      if operation = "open" then
        let id = sprintf "%s-session-%d" host (sessions.Count + 1)
        sessions.Add(id, { Generation = 0L; Observation = 0UL; Current = json (null: obj); Closed = false; Busy = false })
        reply id 0L (json {| session = id; project = "/project/fixture.fidproj" |}) None
      elif operation = "prepare_compiler_change" then
        for state in sessions.Values do
          state.Closed <- true
          state.Current <- json (null: obj)
        if this.CleanupFails then
          reply "" 0L empty (Some(json {| code = "cleanup_failed"; message = "status persistence failed" |}))
        else reply "" 0L (json {| restartRequired = true; inMemoryPatchAllowed = false |}) None
      else
        let state = sessions[session]
        match operation with
        | "reserve" ->
          state.Generation <- state.Generation + 1L
          state.Current <- json (null: obj)
          reply session state.Generation (json {| reservation = "opaque-ticket" |}) None
        | "build" ->
          state.Current <- accepted state.Generation
          reply session state.Generation state.Current None
        | "run" ->
          reply session state.Generation
            (json {| generation = state.Generation; sourceVersion = "source-" + string state.Generation
                     exitCode = 0; standardOutput = "native result\n"; standardError = "" |}) None
        | "cancel" | "close" ->
          state.Current <- json (null: obj)
          if operation = "close" then state.Closed <- true
          reply session state.Generation empty None
        | "status" ->
          state.Observation <- state.Observation + 1UL
          reply session state.Generation
            (json {| observation = state.Observation; project = "/project/fixture.fidproj"; manifestPath = "/external/provider/current.json"
                     closed = state.Closed; busy = state.Busy; current = state.Current
                     revocationPending = false; backendError = (null: string)
                     cleanupPending = false; cleanupError = (null: string)
                     executionRequiresRevalidation = true |}) None
        | other -> failwith ("Unexpected fake-worker operation: " + other))
  interface IComposerWorker with
    member _.Handshake =
      reply "" 0L
        (json {| protocolVersion = 1; provider = "clef-composer"; inMemoryPatchAllowed = false
                 operations = [| "open"; "reserve"; "build"; "status"; "run"; "cancel"; "close"; "prepare_compiler_change" |] |}) None
    member _.ProcessId = pid
    member this.IsAlive = this.Alive
    member _.Exited = exited.Publish
    member this.RequestAsync(operation, session, _, cancellation) =
      requests.Enqueue(operation, session)
      tokens.Enqueue(operation, cancellation)
      match this.Intercept operation session with
      | Some result -> result
      | None -> Task.FromResult(this.Response(operation, session))
    member this.StopAsync() =
      Interlocked.Increment &stops |> ignore
      if this.StopThrows then Task.FromException(InvalidOperationException "termination failed; process still alive")
      elif this.StopLeavesAlive then Task.CompletedTask
      else
        this.Die()
        Task.CompletedTask

let private request operation (worker: Worker) session : ComposerRequest = {
  Operation = operation; Session = session; Host = worker.Host; Epoch = worker.Epoch
  Provider = "clef-composer"; Parameters = []
}
let private execute (owner: ComposerSupervisor) request = owner.ExecuteAsync(request, CancellationToken.None)
let private openSession owner worker = task {
  let! response = execute owner (request "open" worker "")
  success response |> ignore
  return sessionOf response
}
let private snapshots (owner: ComposerSupervisor) = task {
  let! view = owner.SessionsAsync CancellationToken.None
  return field "sessions" view |> fun entries -> entries.EnumerateArray() |> Seq.toArray
}
let private snapshot session entries = entries |> Array.find (fun entry -> sessionOf entry = session)
let private assertWithdrawn response =
  let status = success response
  (field "closed" status).GetBoolean() |> Expect.isTrue "session is terminal"
  (field "current" status).ValueKind |> Expect.equal "terminal session has no runnable artifact" JsonValueKind.Null

let private lateReplyDuringReplacement holdRefresh () = task {
  let old = Worker("old", 101)
  let fresh = Worker("fresh", 102)
  let mutable creations = 0
  let owner = ComposerSupervisor((fun () ->
    let count = Interlocked.Increment &creations
    Task.FromResult((if count = 1 then old else fresh) :> IComposerWorker)), true)
  let! session = openSession owner old
  let entered = completion<unit> ()
  let released = completion<JsonElement> ()
  old.Intercept <- fun operation current ->
    if current = session && operation = (if holdRefresh then "status" else "build") then
      entered.TrySetResult() |> ignore
      Some released.Task
    else None
  let pending = execute owner (request "build" old session)
  do! entered.Task
  // Capture the old successful reply before retirement; delivery is delayed.
  let late = old.Response((if holdRefresh then "status" else "build"), session)
  let! retired = execute owner (request "prepare_compiler_change" old "")
  success retired |> ignore
  let! replacement = openSession owner fresh
  released.TrySetResult late |> ignore
  let! response = pending
  response |> refused "closed"
  let! entries = snapshots owner
  snapshot session entries |> assertWithdrawn
  let current = snapshot replacement entries |> success |> field "current"
  current.ValueKind |> Expect.equal "late old success cannot lend an artifact to the replacement" JsonValueKind.Null
  creations |> Expect.equal "replacement uses one fresh worker" 2
  do! owner.StopAsync()
}

let private delayedSameGenerationStatus humanView duringBuild failedRead () = task {
  let worker = Worker("same-generation", 121)
  let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
  let! session = openSession owner worker
  let entered = completion<unit> ()
  let released = completion<JsonElement> ()
  let buildEntered = completion<unit> ()
  let buildReleased = completion<JsonElement> ()
  let mutable calls = 0
  worker.Intercept <- fun operation _ ->
    if operation = "status" && Interlocked.Increment &calls = 1 then
      entered.TrySetResult() |> ignore
      Some released.Task
    elif operation = "build" && duringBuild then
      buildEntered.TrySetResult() |> ignore
      Some buildReleased.Task
    else None
  let build =
    if duringBuild then execute owner (request "build" worker session)
    else Task.FromResult empty
  if duringBuild then do! buildEntered.Task
  let oldStatus = worker.Response("status", session)
  let delayed =
    if humanView then owner.SessionsAsync CancellationToken.None
    else execute owner (request "status" worker session)
  do! entered.Task
  if duringBuild then buildReleased.TrySetResult(worker.Response("build", session)) |> ignore
  let! built = if duringBuild then build else execute owner (request "build" worker session)
  success built |> ignore
  if failedRead then released.TrySetException(IOException "old status read failed") |> ignore
  else released.TrySetResult oldStatus |> ignore
  let! response = delayed
  if humanView then
    let entries = field "sessions" response |> fun values -> values.EnumerateArray() |> Seq.toArray
    let current = snapshot session entries |> success
    (field "current" current).ValueKind |> Expect.equal "late read preserves the accepted artifact" JsonValueKind.Object
    (field "statusFresh" current).GetBoolean() |> Expect.isTrue "late read cannot erase a newer successful observation"
  else
    response |> refused "superseded"
  do! owner.StopAsync()
}

[<Tests>]
let tests =
  testList "Composer daemon supervisor" [
    taskCase "same-generation late tool status is refused after build settles" (delayedSameGenerationStatus false false false)
    taskCase "same-generation late browser status preserves the completed build" (delayedSameGenerationStatus true false false)
    taskCase "same-generation status begun during build cannot replace its completion" (delayedSameGenerationStatus true true false)
    taskCase "same-generation late status error cannot erase the completed build" (delayedSameGenerationStatus true false true)
    taskCase "admitted mutation exposes busy progress without claiming a fresh artifact" <| fun () -> task {
      let worker = Worker("active-observation", 122)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let entered = completion<unit> ()
      let released = completion<JsonElement> ()
      worker.Intercept <- fun operation _ ->
        if operation = "build" then
          entered.TrySetResult() |> ignore
          Some released.Task
        else None
      let pending = execute owner (request "build" worker session)
      do! entered.Task
      worker.SetBusy(session, true)
      let! response = execute owner (request "status" worker session)
      let progress = success response
      (field "busy" progress).GetBoolean() |> Expect.isTrue "clients can observe provider progress before canceling work"
      (field "statusFresh" progress).GetBoolean() |> Expect.isFalse "progress does not grant fresh artifact authority"
      (field "current" progress).ValueKind |> Expect.equal "progress never presents a cached artifact as current" JsonValueKind.Null
      let! entries = snapshots owner
      let current = snapshot session entries |> success
      (field "statusFresh" current).GetBoolean() |> Expect.isFalse "in-flight work retains uncertainty"
      (field "current" current).ValueKind |> Expect.equal "no cached artifact is presented as current" JsonValueKind.Null
      worker.SetBusy(session, false)
      released.TrySetResult(worker.Response("build", session)) |> ignore
      let! result = pending
      success result |> ignore
      do! owner.StopAsync()
    }
    taskCase "provider snapshot order wins when request dispatch and delivery orders disagree" <| fun () -> task {
      let worker = Worker("snapshot-order", 123)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let firstReply = completion<JsonElement> ()
      let secondReply = completion<JsonElement> ()
      let mutable calls = 0
      worker.Intercept <- fun operation _ ->
        if operation = "status" then
          match Interlocked.Increment &calls with
          | 1 -> Some firstReply.Task
          | 2 -> Some secondReply.Task
          | _ -> None
        else None
      let first = execute owner (request "status" worker session)
      let second = execute owner (request "status" worker session)
      // The later-dispatched request captures first. A producer then settles;
      // the earlier-dispatched request captures the newer provider state.
      let oldStatus = worker.Response("status", session)
      worker.Response("build", session) |> ignore
      let newerStatus = worker.Response("status", session)
      firstReply.TrySetResult newerStatus |> ignore
      let! current = first
      (success current |> field "current").ValueKind |> Expect.equal "newer capture is accepted" JsonValueKind.Object
      secondReply.TrySetResult oldStatus |> ignore
      let! late = second
      late |> refused "superseded"
      do! owner.StopAsync()
    }
    taskCase "status without provider observation cannot retain a fresh cached artifact" <| fun () -> task {
      let worker = Worker("unsequenced-status", 124)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request "build" worker session)
      success built |> ignore
      let node = System.Text.Json.Nodes.JsonNode.Parse((worker.Response("status", session)).GetRawText())
      node["result"].AsObject().Remove "observation" |> ignore
      let unsequenced = JsonSerializer.SerializeToElement node
      worker.Intercept <- fun operation _ ->
        if operation = "status" then Some(Task.FromResult unsequenced) else None
      let! response = execute owner (request "status" worker session)
      response |> refused "superseded"
      let! entries = snapshots owner
      let current = snapshot session entries |> success
      (field "statusFresh" current).GetBoolean() |> Expect.isFalse "unsequenced response leaves status uncertain"
      (field "current" current).ValueKind |> Expect.equal "no artifact is inferred from unsequenced status" JsonValueKind.Null
      do! owner.StopAsync()
    }
    taskCase "disabled provider refuses open without invoking its worker factory" <| fun () -> task {
      let mutable calls = 0
      let worker = Worker("disabled", 100)
      let owner = ComposerSupervisor((fun () -> calls <- calls + 1; Task.FromResult(worker :> IComposerWorker)), false)
      let! response = execute owner (request "open" worker "")
      response |> refused "provider_unavailable"
      calls |> Expect.equal "disabled provider allocates no process" 0
      let! view = owner.SessionsAsync CancellationToken.None
      (field "configured" view).GetBoolean() |> Expect.isFalse "availability is visible to both client surfaces"
      do! owner.StopAsync()
    }

    taskCase "concurrent opens share one worker while creating distinct sessions" <| fun () -> task {
      let worker = Worker("shared", 101)
      let entered = completion<unit> ()
      let released = completion<IComposerWorker> ()
      let mutable calls = 0
      let owner = ComposerSupervisor((fun () ->
        Interlocked.Increment &calls |> ignore
        entered.TrySetResult() |> ignore
        released.Task), true)
      let first = openSession owner worker
      do! entered.Task
      let second = openSession owner worker
      released.TrySetResult(worker :> IComposerWorker) |> ignore
      let! sessions = Task.WhenAll [| first; second |]
      calls |> Expect.equal "only one worker factory runs" 1
      sessions[0] |> Expect.notEqual "opens retain independent session identity" sessions[1]
      let! view = snapshots owner
      view.Length |> Expect.equal "both opens appear in shared session view" 2
      do! owner.StopAsync()
    }

    taskCase "tool operations and human session reads share exact provider authority and current state" <| fun () -> task {
      let worker = Worker("human-and-agent", 102)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! first = openSession owner worker
      let! second = openSession owner worker
      let! reservation = execute owner (request "reserve" worker first)
      success reservation |> ignore
      let! built = execute owner (request "build" worker first)
      success built |> ignore
      let! toolStatus = execute owner (request "status" worker first)
      let! humanView = snapshots owner
      let displayed = snapshot first humanView
      (field "authority" displayed).GetRawText()
      |> Expect.equal "human and tool response carry identical host/session/epoch/generation" ((field "authority" toolStatus).GetRawText())
      (success displayed |> field "current").GetRawText()
      |> Expect.equal "human sees the accepted artifact returned to the tool" ((success built).GetRawText())
      (snapshot second humanView |> success |> field "current").ValueKind
      |> Expect.equal "other project's current artifact stays absent" JsonValueKind.Null
      do! owner.StopAsync()
    }

    taskCase "wrong host epoch or provider is refused without forwarding to the compiler" <| fun () -> task {
      let worker = Worker("owner", 103)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let original = request "run" worker session
      let before = worker.Requests.Length
      for invalid, code in [ { original with Host = "foreign" }, "wrong_authority"
                             { original with Epoch = "retired" }, "wrong_authority"
                             { original with Provider = "fsharp" }, "wrong_provider" ] do
        let! response = execute owner invalid
        response |> refused code
      worker.Requests.Length |> Expect.equal "invalid authority never reaches backend" before
      do! owner.StopAsync()
    }

    taskCase "retirement cleanup failure still stops the old process before creating a replacement" <| fun () -> task {
      let old = Worker("cleanup-failure", 104)
      old.CleanupFails <- true
      let fresh = Worker("replacement", 105)
      let mutable calls = 0
      let owner = ComposerSupervisor((fun () ->
        let count = Interlocked.Increment &calls
        if count > 1 then old.Alive |> Expect.isFalse "old process must exit before replacement factory runs"
        Task.FromResult((if count = 1 then old else fresh) :> IComposerWorker)), true)
      let! session = openSession owner old
      let! response = execute owner (request "prepare_compiler_change" old "")
      response |> refused "cleanup_failed"
      old.StopCount |> Expect.equal "physical process stop follows failed compiler cleanup" 1
      let! _ = openSession owner fresh
      let! view = snapshots owner
      snapshot session view |> assertWithdrawn
      do! owner.StopAsync()
    }

    taskCase "late worker replies and late status refreshes cannot revive a retired compiler epoch" <| fun () -> task {
      do! lateReplyDuringReplacement false ()
      do! lateReplyDuringReplacement true ()
    }

    taskCase "worker death withdraws a previously accepted artifact from the shared view" <| fun () -> task {
      let worker = Worker("dies", 106)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request "build" worker session)
      success built |> ignore
      worker.Die()
      let! view = snapshots owner
      snapshot session view |> assertWithdrawn
      owner.WorkerPid |> Expect.isNone "dead worker is unavailable"
      let before = worker.Requests.Length
      let! run = execute owner (request "run" worker session)
      run |> refused "provider_unavailable"
      worker.Requests.Length |> Expect.equal "dead worker receives no execution request" before
      do! owner.StopAsync()
    }

    taskCase "daemon shutdown overtaking worker creation stops the unpublished connection" <| fun () -> task {
      let worker = Worker("late-factory", 107)
      let entered = completion<unit> ()
      let released = completion<IComposerWorker> ()
      let mutable calls = 0
      let owner = ComposerSupervisor((fun () ->
        Interlocked.Increment &calls |> ignore
        entered.TrySetResult() |> ignore
        released.Task), true)
      let opening = execute owner (request "open" worker "")
      do! entered.Task
      let stopping = owner.StopAsync()
      released.TrySetResult(worker :> IComposerWorker) |> ignore
      let! response = opening
      response |> refused "provider_unavailable"
      do! stopping
      worker.Alive |> Expect.isFalse "unpublished worker cannot outlive shutdown"
      worker.StopCount |> Expect.equal "overtaken connection was explicitly stopped" 1
      worker.Requests.Length |> Expect.equal "shutdown prevents forwarding the pending open" 0
      let! retry = execute owner (request "open" worker "")
      retry |> refused "provider_unavailable"
      calls |> Expect.equal "stopped owner never starts another worker" 1
    }

    taskCase "status and resource reads do not generate an unconditional change feedback loop" <| fun () -> task {
      let worker = Worker("observed", 108)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let mutable changes = 0
      use subscription = owner.Changed.Subscribe(fun () -> Interlocked.Increment &changes |> ignore)
      let! session = openSession owner worker
      let before = Volatile.Read &changes
      (before, 0) |> Expect.isGreaterThan "opening produces an observable change"
      let! initial = owner.SessionsAsync CancellationToken.None
      for _ in 1 .. 3 do
        let! status = execute owner (request "status" worker session)
        success status |> ignore
        let! view = owner.SessionsAsync CancellationToken.None
        (field "revision" view).GetInt64() |> Expect.equal "reads preserve revision" ((field "revision" initial).GetInt64())
      changes |> Expect.equal "reads do not recursively trigger more reads" before
      do! owner.StopAsync()
    }

    taskCase "failed process termination leaves the old worker fenced and forbids a replacement" <| fun () -> task {
      for throws in [ true; false ] do
        let worker = Worker("stop-failure-" + string throws, 109)
        worker.StopThrows <- throws
        worker.StopLeavesAlive <- not throws
        let mutable calls = 0
        let owner = ComposerSupervisor((fun () -> calls <- calls + 1; Task.FromResult(worker :> IComposerWorker)), true)
        let! session = openSession owner worker
        let! retirement = execute owner (request "prepare_compiler_change" worker "")
        (field "success" retirement).GetBoolean() |> Expect.isFalse "live process is never reported successfully retired"
        let before = worker.Requests.Length
        let! run = execute owner (request "run" worker session)
        (field "success" run).GetBoolean() |> Expect.isFalse "failed stop cannot restore execution authority"
        let! opening = execute owner (request "open" worker "")
        (field "success" opening).GetBoolean() |> Expect.isFalse "failed stop cannot restore open authority"
        calls |> Expect.equal "no replacement may overlap a worker which failed to stop" 1
        worker.Requests.Length |> Expect.equal "fenced process receives no subsequent operation" before
        let! view = snapshots owner
        snapshot session view |> assertWithdrawn
        worker.StopThrows <- false
        worker.StopLeavesAlive <- false
        do! owner.StopAsync()
    }

    taskCase "an opened session remains discoverable when its first status refresh fails" <| fun () -> task {
      let worker = Worker("open-status-failure", 110)
      worker.Intercept <- fun operation _ ->
        if operation = "status" then Some(Task.FromException<JsonElement>(IOException "status temporarily unavailable")) else None
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! view = snapshots owner
      view.Length |> Expect.equal "successful open remains registered despite failed refresh" 1
      let status = snapshot session view |> success
      (field "current" status).ValueKind |> Expect.equal "unknown status claims no artifact" JsonValueKind.Null
      (field "statusFresh" status).GetBoolean() |> Expect.isFalse "unavailable status is explicit"
      (field "closed" status).GetBoolean() |> Expect.isFalse "temporary status failure does not fabricate session closure"
      do! owner.StopAsync()
    }

    taskCase "failed status after an acknowledged mutation withdraws the previously cached artifact" <| fun () -> task {
      let worker = Worker("mutated-status-failure", 111)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request "build" worker session)
      success built |> ignore
      worker.Intercept <- fun operation _ ->
        if operation = "status" then Some(Task.FromException<JsonElement>(IOException "status temporarily unavailable")) else None
      let! reserved = execute owner (request "reserve" worker session)
      success reserved |> ignore
      let! view = snapshots owner
      let cached = snapshot session view
      (field "generation" (field "authority" cached)).GetInt64()
      |> Expect.equal "acknowledged mutation revision survives a failed status refresh" 1L
      let status = success cached
      (field "current" status).ValueKind |> Expect.equal "old artifact cannot survive an unknown newer status" JsonValueKind.Null
      (field "statusFresh" status).GetBoolean() |> Expect.isFalse "newer status is explicitly unknown"
      do! owner.StopAsync()
    }

    taskCase "timeout and caller cancellation reconcile withdrawn authority before returning their refusal" <| fun () -> task {
      for timeout in [ true; false ] do
        let worker = Worker("canceled-" + string timeout, 112)
        let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
        let! session = openSession owner worker
        let! built = execute owner (request "build" worker session)
        success built |> ignore
        use cancellation = new CancellationTokenSource()
        let mutable statusCalls = 0
        worker.Intercept <- fun operation current ->
          if operation = "status" then
            Interlocked.Increment &statusCalls |> ignore
            None
          elif operation = "build" then
            worker.Withdraw(current, false)
            if timeout then Some(Task.FromException<JsonElement>(TimeoutException "worker request deadline elapsed"))
            else
              cancellation.Cancel()
              Some(Task.FromCanceled<JsonElement>(cancellation.Token))
          else None
        let! response = owner.ExecuteAsync(request "build" worker session, cancellation.Token)
        response |> refused (if timeout then "timeout" else "canceled")
        (statusCalls, 0) |> Expect.isGreaterThan "refusal follows actual status reconciliation"
        worker.Intercept <- fun operation _ ->
          if operation = "status" then Some(Task.FromException<JsonElement>(IOException "later observer cannot refresh")) else None
        let! view = snapshots owner
        let cached = snapshot session view
        (field "generation" (field "authority" cached)).GetInt64()
        |> Expect.equal "withdrawn revision was cached before returning" 1L
        (cached |> success |> field "current").ValueKind
        |> Expect.equal "refusal never leaves prior artifact runnable in shared cache" JsonValueKind.Null
        do! owner.StopAsync()
    }

    taskCase "a delayed status cannot regress authority after a newer edit reservation" <| fun () -> task {
      let worker = Worker("generation-order", 113)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request "build" worker session)
      success built |> ignore
      let oldStatus = worker.Response("status", session)
      let entered = completion<unit> ()
      let released = completion<JsonElement> ()
      let mutable calls = 0
      worker.Intercept <- fun operation _ ->
        if operation = "status" && Interlocked.Increment &calls = 1 then
          entered.TrySetResult() |> ignore
          Some released.Task
        else None
      let delayed = execute owner (request "status" worker session)
      do! entered.Task
      let! reserved = execute owner (request "reserve" worker session)
      success reserved |> ignore
      released.TrySetResult oldStatus |> ignore
      let! response = delayed
      if (field "success" response).GetBoolean() then
        (field "generation" (field "authority" response)).GetInt64()
        |> Expect.equal "successful late status cannot claim older authority" 1L
      worker.Intercept <- fun operation _ ->
        if operation = "status" then Some(Task.FromException<JsonElement>(IOException "inspect cache without refresh")) else None
      let! view = snapshots owner
      let cached = snapshot session view
      (field "generation" (field "authority" cached)).GetInt64()
      |> Expect.equal "late status cannot move cache backwards" 1L
      (cached |> success |> field "current").ValueKind |> Expect.equal "old current never reappears" JsonValueKind.Null
      do! owner.StopAsync()
    }

    taskCase "canceled busy work publishes its eventual settled status without observer polling" <| fun () -> task {
      let worker = Worker("busy-cancel", 114)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request "build" worker session)
      success built |> ignore
      let monitorEntered = completion<unit> ()
      let monitorReleased = completion<JsonElement> ()
      let settledNotice = completion<unit> ()
      let mutable statusCalls = 0
      let mutable allowSettledNotice = 0
      use subscription = owner.Changed.Subscribe(fun () ->
        if Volatile.Read &allowSettledNotice = 1 then settledNotice.TrySetResult() |> ignore)
      worker.Intercept <- fun operation current ->
        if operation = "build" then
          worker.Withdraw(current, true)
          Some(Task.FromException<JsonElement>(OperationCanceledException "exact request cancellation acknowledged"))
        elif operation = "status" && Interlocked.Increment &statusCalls = 2 then
          monitorEntered.TrySetResult() |> ignore
          Some monitorReleased.Task
        else None
      let! canceled = execute owner (request "build" worker session)
      canceled |> refused "canceled"
      do! monitorEntered.Task
      worker.SetBusy(session, false)
      Volatile.Write(&allowSettledNotice, 1)
      monitorReleased.TrySetResult(worker.Response("status", session)) |> ignore
      do! settledNotice.Task
      worker.Intercept <- fun operation _ ->
        if operation = "status" then Some(Task.FromException<JsonElement>(IOException "inspect settled cache")) else None
      let! view = snapshots owner
      let status = snapshot session view |> success
      (field "busy" status).GetBoolean() |> Expect.isFalse "background reconciliation published completion"
      (field "current" status).ValueKind |> Expect.equal "canceled operation remains withdrawn" JsonValueKind.Null
      do! owner.StopAsync()
    }

    taskCase "caller disconnect after open dispatch cannot orphan the newly created session" <| fun () -> task {
      let worker = Worker("open-disconnect", 115)
      let entered = completion<unit> ()
      let released = completion<JsonElement> ()
      worker.Intercept <- fun operation _ ->
        if operation = "open" then
          entered.TrySetResult() |> ignore
          Some released.Task
        else None
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      use cancellation = new CancellationTokenSource()
      let opening = owner.ExecuteAsync(request "open" worker "", cancellation.Token)
      do! entered.Task
      cancellation.Cancel()
      released.TrySetResult(worker.Response("open", "")) |> ignore
      let! response = opening
      success response |> ignore
      let dispatchedToken = worker.Tokens |> Array.find (fun (operation, _) -> operation = "open") |> snd
      dispatchedToken.CanBeCanceled |> Expect.isFalse "sent open remains observable independently of disconnected caller"
      let! view = snapshots owner
      view.Length |> Expect.equal "created session is retained for shared inspection and cleanup" 1
      sessionOf view[0] |> Expect.equal "registered identity is the completed open identity" (sessionOf response)
      do! owner.StopAsync()
    }

    taskCase "ambiguous open timeout retires its process before replacement and keeps failed stops fenced" <| fun () -> task {
      for stopFails in [ false; true ] do
        let old = Worker("ambiguous-open-" + string stopFails, 116)
        let fresh = Worker("after-ambiguous-open-" + string stopFails, 117)
        let mutable creations = 0
        let owner = ComposerSupervisor((fun () ->
          let count = Interlocked.Increment &creations
          if count > 1 then old.Alive |> Expect.isFalse "unknown open cannot survive into a replacement worker"
          Task.FromResult((if count = 1 then old else fresh) :> IComposerWorker)), true)
        let! known = openSession owner old
        let! accepted = execute owner (request "build" old known)
        success accepted |> ignore
        old.StopThrows <- stopFails
        old.Intercept <- fun operation _ ->
          if operation = "open" then
            // The compiler accepted an additional session, but its successful
            // response was lost. The supervisor cannot address that session.
            old.Response("open", "") |> ignore
            Some(Task.FromException<JsonElement>(TimeoutException "open reply lost after acceptance"))
          else None
        // A supplied old session must not disguise the unknown new session.
        let! refusedOpen = execute owner (request "open" old known)
        refusedOpen |> refused (if stopFails then "retirement_failed" else "timeout")
        old.StopCount |> Expect.equal "ambiguous open requires physical process cleanup" 1
        let! view = snapshots owner
        snapshot known view |> assertWithdrawn
        if stopFails then
          let before = old.Requests.Length
          let! denied = execute owner (request "open" fresh "")
          (field "success" denied).GetBoolean() |> Expect.isFalse "failed process stop prohibits another open"
          creations |> Expect.equal "failed stop never invokes replacement factory" 1
          old.Requests.Length |> Expect.equal "fenced old process receives no more operations" before
          old.StopThrows <- false
        else
          old.Alive |> Expect.isFalse "timed-out open's worker is gone"
          let! replacement = openSession owner fresh
          replacement.StartsWith(fresh.Host, StringComparison.Ordinal)
          |> Expect.isTrue "replacement owns a fresh session identity"
          creations |> Expect.equal "successful cleanup permits one replacement" 2
        do! owner.StopAsync()
    }
  ]
