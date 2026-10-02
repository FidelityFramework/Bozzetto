module Bozzetto.Tests.ComposerSupervisorTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration
open Expecto
open Expecto.Flip

let private success (response: ComposerResponse) =
  match response.Reply.Outcome with
  | Result.Ok _ -> response
  | Result.Error refusal -> failtestf "Unexpected refusal %A: %s" refusal.Code refusal.Message
let private observed response =
  match (success response).Reply.Outcome with
  | Result.Ok(Observed status) -> status
  | other -> failtestf "Expected observed session, got %A" other
let private artifact response =
  match (success response).Reply.Outcome with
  | Result.Ok(Built accepted) -> accepted
  | other -> failtestf "Expected accepted artifact, got %A" other
let private refused code (response: ComposerResponse) =
  match response.Reply.Outcome with
  | Result.Error refusal -> refusal.Code |> Expect.equal "refusal classification" code
  | Result.Ok value -> failtestf "Expected refusal %A, got %A" code value
let private sessionOf (response: ComposerResponse) = response.Reply.Authority.Session
let private completion<'a> () = TaskCompletionSource<'a>(TaskCreationOptions.RunContinuationsAsynchronously)
let private bounded (work: Task<'a>) = work.WaitAsync(TimeSpan.FromSeconds 5.)
let private taskCase name work = testCaseAsync name (async { do! work () |> bounded |> Async.AwaitTask })

type private SessionState = {
  mutable Generation: int64
  mutable Observation: uint64
  mutable Current: AcceptedArtifact option
  mutable Closed: bool
  mutable Busy: bool
}

/// Typed process boundary; these tests exercise actual supervisor ownership,
/// ordering and reconciliation without relying on a JSON-shaped fake protocol.
type private Worker(host: string, pid: int) =
  let gate = obj ()
  let exited = Event<string>()
  let requests = ConcurrentQueue<Operation * string>()
  let tokens = ConcurrentQueue<Operation * CancellationToken>()
  let sessions = Dictionary<string, SessionState>()
  let mutable alive = true
  let mutable stops = 0
  let identity session generation : Authority =
    { Host = host; Session = session; Epoch = host + "-epoch"
      Provider = ProviderIdentity.ClefComposer; Generation = generation }
  let reply session generation outcome : Reply =
    { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = Guid.NewGuid().ToString("N")
      Authority = identity session generation; Outcome = outcome }
  let accepted generation : AcceptedArtifact =
    { Generation = generation; SourceVersion = "source-" + string generation
      ArtifactPath = "/external/provider/program"; ArtifactSha256 = "artifact-digest"
      ObjectManifest = "/external/provider/objects.json"
      ChangedWitnesses = [||]; RetainedWitnesses = [||]; RetiredWitnesses = [||]
      WitnessVisits = [||]; CompiledObjects = [||]; ReusedObjects = [||]; RetiredObjects = [||] }
  member _.Host = host
  member _.Epoch = host + "-epoch"
  member _.Address: WorkerAddress = { Host = host; Epoch = host + "-epoch"; Provider = ProviderIdentity.ClefComposer }
  member this.SessionAddress session: SessionAddress = { Worker = this.Address; Session = session }
  member _.Requests = requests.ToArray()
  member _.Tokens = tokens.ToArray()
  member _.StopCount = Volatile.Read &stops
  member _.Alive = lock gate (fun () -> alive)
  member val Intercept: Operation -> string -> Task<Reply> option = (fun _ _ -> None) with get, set
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
      state.Current <- None
      state.Busy <- busy)
  member _.SetBusy(session, busy) = lock gate (fun () -> sessions[session].Busy <- busy)
  member this.Response(operation, session) =
    lock gate (fun () ->
      match operation with
      | Operation.Open ->
        let id = sprintf "%s-session-%d" host (sessions.Count + 1)
        sessions.Add(id, { Generation = 0L; Observation = 0UL; Current = None; Closed = false; Busy = false })
        reply id 0L (Result.Ok(Opened { Observation = 0UL; Project = "/project/fixture.fidproj"; ManifestPath = "/external/provider/current.json" }))
      | Operation.PrepareCompilerChange ->
        for state in sessions.Values do
          state.Closed <- true
          state.Current <- None
        if this.CleanupFails then
          reply "" 0L (Result.Error { Code = RefusalCode.CleanupFailed; Message = "status persistence failed" })
        else reply "" 0L (Result.Ok(CompilerRetired { RestartRequired = true; InMemoryPatchAllowed = false }))
      | _ ->
        let state = sessions[session]
        let value =
          match operation with
          | Operation.Reserve ->
            state.Generation <- state.Generation + 1L
            state.Current <- None
            Reserved { Reservation = "opaque-ticket" }
          | Operation.Build ->
            let value = accepted state.Generation
            state.Current <- Some value
            Built value
          | Operation.Run ->
            Ran { Generation = state.Generation; SourceVersion = "source-" + string state.Generation
                  ExitCode = 0; StandardOutput = "native result\n"; StandardError = "" }
          | Operation.Cancel -> state.Current <- None; Canceled
          | Operation.Close ->
            state.Current <- None
            state.Closed <- true
            state.Observation <- state.Observation + 1UL
            Closed { Observation = state.Observation; Closed = true; CleanupPending = false; CleanupError = None }
          | Operation.Status ->
            state.Observation <- state.Observation + 1UL
            Observed { Observation = state.Observation; Project = "/project/fixture.fidproj"; ManifestPath = "/external/provider/current.json"
                       Closed = state.Closed; Busy = state.Busy; Current = state.Current
                       RevocationPending = false; BackendError = None; CleanupPending = false; CleanupError = None }
          | other -> failwithf "Unexpected fake-worker operation: %A" other
        reply session state.Generation (Result.Ok value))
  interface IComposerWorker with
    member _.Handshake =
      reply "" 0L (Result.Ok(HelloAccepted {
        Agreement = BAREWireCodec.agreement
        Compiler = { AssemblyPath = "/external/Composer.dll"; Sha256 = "compiler-digest"; Version = "test" }
        Psg = { Schema = Fidelity.PSG.Revision.Schema; AssemblySha256 = "psg-digest"
                FormatVersion = Fidelity.PSG.Binary.FormatVersion; ContractFingerprint = Fidelity.PSG.Binary.ContractFingerprint }
        Operations = [| Operation.Open; Operation.Reserve; Operation.Build; Operation.Status; Operation.Run
                        Operation.Cancel; Operation.Close; Operation.PrepareCompilerChange |]
        InMemoryPatchAllowed = false }))
    member _.ProcessId = pid
    member this.IsAlive = this.Alive
    member _.Exited = exited.Publish
    member this.RequestAsync(body, cancellation) =
      let operation = BAREWireCodec.requestOperation body
      let session = ComposerWire.session body
      requests.Enqueue(operation, session)
      tokens.Enqueue(operation, cancellation)
      match this.Intercept operation session with
      | Some result -> result
      | None -> Task.FromResult(this.Response(operation, session))
    member this.StopAsync() =
      Interlocked.Increment &stops |> ignore
      if this.StopThrows then Task.FromException(InvalidOperationException "termination failed; process still alive")
      elif this.StopLeavesAlive then Task.CompletedTask
      else this.Die(); Task.CompletedTask

let private request operation (worker: Worker) session : RequestBody =
  let address = worker.SessionAddress session
  match operation with
  | Operation.Open -> Open(worker.Address, "/project/fixture.fidproj")
  | Operation.Reserve -> Reserve(address, "edit")
  | Operation.Build -> Build(address, "opaque-ticket")
  | Operation.Status -> Status address
  | Operation.Run -> RequestBody.Run(address, [||])
  | Operation.Cancel -> Cancel address
  | Operation.Close -> Close address
  | Operation.PrepareCompilerChange -> PrepareCompilerChange worker.Address
  | other -> failwithf "Unexpected test request: %A" other
let private execute (owner: ComposerSupervisor) request = owner.ExecuteAsync(request, CancellationToken.None)
let private openSession owner worker = task {
  let! response = execute owner (request Operation.Open worker "")
  success response |> ignore
  return sessionOf response
}
let private snapshots (owner: ComposerSupervisor) = task {
  let! view = owner.SessionsAsync CancellationToken.None
  return view.Sessions
}
let private snapshot session entries = entries |> Array.find (fun entry -> sessionOf entry = session)
let private assertWithdrawn response =
  let status = observed response
  status.Closed |> Expect.isTrue "session is terminal"
  status.Current |> Expect.isNone "terminal session has no runnable artifact"

let private lateReplyDuringReplacement holdRefresh () = task {
  let old = Worker("old", 101)
  let fresh = Worker("fresh", 102)
  let mutable creations = 0
  let owner = ComposerSupervisor((fun () ->
    let count = Interlocked.Increment &creations
    Task.FromResult((if count = 1 then old else fresh) :> IComposerWorker)), true)
  let! session = openSession owner old
  let entered = completion<unit> ()
  let released = completion<Reply> ()
  old.Intercept <- fun operation current ->
    if current = session && operation = (if holdRefresh then Operation.Status else Operation.Build) then
      entered.TrySetResult() |> ignore
      Some released.Task
    else None
  let pending = execute owner (request Operation.Build old session)
  do! entered.Task
  // Capture the old successful reply before retirement; delivery is delayed.
  let late = old.Response((if holdRefresh then Operation.Status else Operation.Build), session)
  let! retired = execute owner (request Operation.PrepareCompilerChange old "")
  success retired |> ignore
  let! replacement = openSession owner fresh
  released.TrySetResult late |> ignore
  let! response = pending
  response |> refused RefusalCode.Closed
  let! entries = snapshots owner
  snapshot session entries |> assertWithdrawn
  let current = snapshot replacement entries |> observed
  current.Current |> Expect.isNone "late old success cannot lend an artifact to the replacement"
  creations |> Expect.equal "replacement uses one fresh worker" 2
  do! owner.StopAsync()
}

let private delayedSameGenerationStatus humanView duringBuild failedRead () = task {
  let worker = Worker("same-generation", 121)
  let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
  let! session = openSession owner worker
  let entered = completion<unit> ()
  let released = completion<Reply> ()
  let buildEntered = completion<unit> ()
  let buildReleased = completion<Reply> ()
  let mutable calls = 0
  worker.Intercept <- fun operation _ ->
    if operation = Operation.Status && Interlocked.Increment &calls = 1 then
      entered.TrySetResult() |> ignore
      Some released.Task
    elif operation = Operation.Build && duringBuild then
      buildEntered.TrySetResult() |> ignore
      Some buildReleased.Task
    else None
  let build = if duringBuild then Some(execute owner (request Operation.Build worker session)) else None
  if duringBuild then do! buildEntered.Task
  let oldStatus = worker.Response(Operation.Status, session)
  let delayed =
    if humanView then Choice1Of2(owner.SessionsAsync CancellationToken.None)
    else Choice2Of2(execute owner (request Operation.Status worker session))
  do! entered.Task
  if duringBuild then buildReleased.TrySetResult(worker.Response(Operation.Build, session)) |> ignore
  let! built = match build with Some pending -> pending | None -> execute owner (request Operation.Build worker session)
  success built |> ignore
  if failedRead then released.TrySetException(IOException "old status read failed") |> ignore
  else released.TrySetResult oldStatus |> ignore
  match delayed with
  | Choice1Of2 pending ->
    let! response = pending
    let current = snapshot session response.Sessions |> success
    (observed current).Current.IsSome |> Expect.equal "late read preserves the accepted artifact" true
    current.StatusFresh |> Expect.isTrue "late read cannot erase a newer successful observation"
  | Choice2Of2 pending ->
    let! response = pending
    response |> refused RefusalCode.Superseded
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
      let released = completion<Reply> ()
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Build then
          entered.TrySetResult() |> ignore
          Some released.Task
        else None
      let pending = execute owner (request Operation.Build worker session)
      do! entered.Task
      worker.SetBusy(session, true)
      let! response = execute owner (request Operation.Status worker session)
      let progress = success response
      (observed progress).Busy |> Expect.isTrue "clients can observe provider progress before canceling work"
      progress.StatusFresh |> Expect.isFalse "progress does not grant fresh artifact authority"
      (observed progress).Current.IsSome |> Expect.equal "progress never presents a cached artifact as current" false
      let! entries = snapshots owner
      let current = snapshot session entries |> success
      current.StatusFresh |> Expect.isFalse "in-flight work retains uncertainty"
      (observed current).Current.IsSome |> Expect.equal "no cached artifact is presented as current" false
      worker.SetBusy(session, false)
      released.TrySetResult(worker.Response(Operation.Build, session)) |> ignore
      let! result = pending
      success result |> ignore
      do! owner.StopAsync()
    }
    taskCase "provider snapshot order wins when request dispatch and delivery orders disagree" <| fun () -> task {
      let worker = Worker("snapshot-order", 123)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let firstReply = completion<Reply> ()
      let secondReply = completion<Reply> ()
      let mutable calls = 0
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Status then
          match Interlocked.Increment &calls with
          | 1 -> Some firstReply.Task
          | 2 -> Some secondReply.Task
          | _ -> None
        else None
      let first = execute owner (request Operation.Status worker session)
      let second = execute owner (request Operation.Status worker session)
      // The later-dispatched request captures first. A producer then settles;
      // the earlier-dispatched request captures the newer provider state.
      let oldStatus = worker.Response(Operation.Status, session)
      worker.Response(Operation.Build, session) |> ignore
      let newerStatus = worker.Response(Operation.Status, session)
      firstReply.TrySetResult newerStatus |> ignore
      let! current = first
      (observed current).Current |> Expect.isSome "newer capture is accepted"
      secondReply.TrySetResult oldStatus |> ignore
      let! late = second
      late |> refused RefusalCode.Superseded
      do! owner.StopAsync()
    }
    taskCase "a status response of the wrong typed operation cannot retain fresh cached authority" <| fun () -> task {
      let worker = Worker("wrong-status-operation", 124)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request Operation.Build worker session)
      success built |> ignore
      let wrong = worker.Response(Operation.Run, session)
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Status then Some(Task.FromResult wrong) else None
      let! response = execute owner (request Operation.Status worker session)
      response |> refused RefusalCode.ProviderUnavailable
      let! entries = snapshots owner
      let current = snapshot session entries |> success
      current.StatusFresh |> Expect.isFalse "wrong operation leaves status uncertain"
      (observed current).Current |> Expect.isNone "no artifact is inferred from a non-status reply"
      do! owner.StopAsync()
    }
    taskCase "disabled provider refuses open without invoking its worker factory" <| fun () -> task {
      let mutable calls = 0
      let worker = Worker("disabled", 100)
      let owner = ComposerSupervisor((fun () -> calls <- calls + 1; Task.FromResult(worker :> IComposerWorker)), false)
      let! response = execute owner (request Operation.Open worker "")
      response |> refused RefusalCode.ProviderUnavailable
      calls |> Expect.equal "disabled provider allocates no process" 0
      let! view = owner.SessionsAsync CancellationToken.None
      view.Configured |> Expect.isFalse "availability is visible to both client surfaces"
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
      let! reservation = execute owner (request Operation.Reserve worker first)
      success reservation |> ignore
      let! built = execute owner (request Operation.Build worker first)
      success built |> ignore
      let! toolStatus = execute owner (request Operation.Status worker first)
      let! humanView = snapshots owner
      let displayed = snapshot first humanView
      displayed.Reply.Authority
      |> Expect.equal "human and tool response carry identical host/session/epoch/generation" toolStatus.Reply.Authority
      (observed displayed).Current
      |> Expect.equal "human sees the accepted artifact returned to the tool" (Some(artifact built))
      (snapshot second humanView |> observed).Current
      |> Expect.isNone "other project's current artifact stays absent"
      do! owner.StopAsync()
    }

    taskCase "wrong host epoch or provider is refused without forwarding to the compiler" <| fun () -> task {
      let worker = Worker("owner", 103)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let original = worker.SessionAddress session
      let before = worker.Requests.Length
      for invalid, code in [ { original.Worker with Host = "foreign" }, RefusalCode.WrongAuthority
                             { original.Worker with Epoch = "retired" }, RefusalCode.WrongAuthority
                             { original.Worker with Provider = ProviderIdentity.FSharp }, RefusalCode.WrongProvider ] do
        let! response = execute owner (RequestBody.Run({ original with Worker = invalid }, [||]))
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
      let! response = execute owner (request Operation.PrepareCompilerChange old "")
      response |> refused RefusalCode.CleanupFailed
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
      let! built = execute owner (request Operation.Build worker session)
      success built |> ignore
      worker.Die()
      let! view = snapshots owner
      snapshot session view |> assertWithdrawn
      owner.WorkerPid |> Expect.isNone "dead worker is unavailable"
      let before = worker.Requests.Length
      let! run = execute owner (request Operation.Run worker session)
      run |> refused RefusalCode.ProviderUnavailable
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
      let opening = execute owner (request Operation.Open worker "")
      do! entered.Task
      let stopping = owner.StopAsync()
      released.TrySetResult(worker :> IComposerWorker) |> ignore
      let! response = opening
      response |> refused RefusalCode.ProviderUnavailable
      do! stopping
      worker.Alive |> Expect.isFalse "unpublished worker cannot outlive shutdown"
      worker.StopCount |> Expect.equal "overtaken connection was explicitly stopped" 1
      worker.Requests.Length |> Expect.equal "shutdown prevents forwarding the pending open" 0
      let! retry = execute owner (request Operation.Open worker "")
      retry |> refused RefusalCode.ProviderUnavailable
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
        let! status = execute owner (request Operation.Status worker session)
        success status |> ignore
        let! view = owner.SessionsAsync CancellationToken.None
        view.Revision |> Expect.equal "reads preserve revision" (initial.Revision)
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
        let! retirement = execute owner (request Operation.PrepareCompilerChange worker "")
        Result.isOk retirement.Reply.Outcome |> Expect.isFalse "live process is never reported successfully retired"
        let before = worker.Requests.Length
        let! run = execute owner (request Operation.Run worker session)
        Result.isOk run.Reply.Outcome |> Expect.isFalse "failed stop cannot restore execution authority"
        let! opening = execute owner (request Operation.Open worker "")
        Result.isOk opening.Reply.Outcome |> Expect.isFalse "failed stop cannot restore open authority"
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
        if operation = Operation.Status then Some(Task.FromException<Reply>(IOException "status temporarily unavailable")) else None
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! view = snapshots owner
      view.Length |> Expect.equal "successful open remains registered despite failed refresh" 1
      let status = snapshot session view |> success
      (observed status).Current.IsSome |> Expect.equal "unknown status claims no artifact" false
      status.StatusFresh |> Expect.isFalse "unavailable status is explicit"
      (observed status).Closed |> Expect.isFalse "temporary status failure does not fabricate session closure"
      do! owner.StopAsync()
    }

    taskCase "failed status after an acknowledged mutation withdraws the previously cached artifact" <| fun () -> task {
      let worker = Worker("mutated-status-failure", 111)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request Operation.Build worker session)
      success built |> ignore
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Status then Some(Task.FromException<Reply>(IOException "status temporarily unavailable")) else None
      let! reserved = execute owner (request Operation.Reserve worker session)
      success reserved |> ignore
      let! view = snapshots owner
      let cached = snapshot session view
      cached.Reply.Authority.Generation
      |> Expect.equal "acknowledged mutation revision survives a failed status refresh" 1L
      let status = success cached
      (observed status).Current.IsSome |> Expect.equal "old artifact cannot survive an unknown newer status" false
      status.StatusFresh |> Expect.isFalse "newer status is explicitly unknown"
      do! owner.StopAsync()
    }

    taskCase "timeout and caller cancellation reconcile withdrawn authority before returning their refusal" <| fun () -> task {
      for timeout in [ true; false ] do
        let worker = Worker("canceled-" + string timeout, 112)
        let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
        let! session = openSession owner worker
        let! built = execute owner (request Operation.Build worker session)
        success built |> ignore
        use cancellation = new CancellationTokenSource()
        let mutable statusCalls = 0
        worker.Intercept <- fun operation current ->
          if operation = Operation.Status then
            Interlocked.Increment &statusCalls |> ignore
            None
          elif operation = Operation.Build then
            worker.Withdraw(current, false)
            if timeout then Some(Task.FromException<Reply>(TimeoutException "worker request deadline elapsed"))
            else
              cancellation.Cancel()
              Some(Task.FromCanceled<Reply>(cancellation.Token))
          else None
        let! response = owner.ExecuteAsync(request Operation.Build worker session, cancellation.Token)
        response |> refused (if timeout then RefusalCode.Timeout else RefusalCode.Canceled)
        (statusCalls, 0) |> Expect.isGreaterThan "refusal follows actual status reconciliation"
        worker.Intercept <- fun operation _ ->
          if operation = Operation.Status then Some(Task.FromException<Reply>(IOException "later observer cannot refresh")) else None
        let! view = snapshots owner
        let cached = snapshot session view
        cached.Reply.Authority.Generation
        |> Expect.equal "withdrawn revision was cached before returning" 1L
        (observed cached).Current
        |> Expect.isNone "refusal never leaves prior artifact runnable in shared cache"
        do! owner.StopAsync()
    }

    taskCase "a delayed status cannot regress authority after a newer edit reservation" <| fun () -> task {
      let worker = Worker("generation-order", 113)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request Operation.Build worker session)
      success built |> ignore
      let oldStatus = worker.Response(Operation.Status, session)
      let entered = completion<unit> ()
      let released = completion<Reply> ()
      let mutable calls = 0
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Status && Interlocked.Increment &calls = 1 then
          entered.TrySetResult() |> ignore
          Some released.Task
        else None
      let delayed = execute owner (request Operation.Status worker session)
      do! entered.Task
      let! reserved = execute owner (request Operation.Reserve worker session)
      success reserved |> ignore
      released.TrySetResult oldStatus |> ignore
      let! response = delayed
      if Result.isOk response.Reply.Outcome then
        response.Reply.Authority.Generation
        |> Expect.equal "successful late status cannot claim older authority" 1L
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Status then Some(Task.FromException<Reply>(IOException "inspect cache without refresh")) else None
      let! view = snapshots owner
      let cached = snapshot session view
      cached.Reply.Authority.Generation
      |> Expect.equal "late status cannot move cache backwards" 1L
      (observed cached).Current |> Expect.isNone "old current never reappears"
      do! owner.StopAsync()
    }

    taskCase "monitor idle exit hands off reconciliation admitted before deregistration" <| fun () -> task {
      let worker = Worker("monitor-handoff", 116)
      let idleObserved = completion<unit> ()
      let removeAllowed = completion<unit> ()
      let successorRead = completion<unit> ()
      let settledNotice = completion<unit> ()
      let mutable exits = 0
      let mutable phase = 0
      let beforeExit () =
        if Interlocked.Increment &exits = 1 then
          idleObserved.TrySetResult() |> ignore
          removeAllowed.Task :> Task
        else Task.CompletedTask
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true, beforeExit)
      try
        let! session = openSession owner worker
        use subscription = owner.Changed.Subscribe(fun () ->
          if successorRead.Task.IsCompleted then settledNotice.TrySetResult() |> ignore)
        worker.Intercept <- fun operation current ->
          if operation = Operation.Build then
            worker.Withdraw(current, true)
            Some(Task.FromException<Reply>(OperationCanceledException "withdraw exact demand"))
          elif operation = Operation.Status && Volatile.Read &phase = 1 then
            worker.SetBusy(current, false)
            Some(Task.FromResult(worker.Response(Operation.Status, current)))
          elif operation = Operation.Status && Volatile.Read &phase = 3 then
            worker.SetBusy(current, false)
            let response = worker.Response(Operation.Status, current)
            successorRead.TrySetResult() |> ignore
            Some(Task.FromResult response)
          else None
        let! first = execute owner (request Operation.Build worker session)
        first |> refused RefusalCode.Canceled
        Volatile.Write(&phase, 1)
        do! idleObserved.Task |> bounded
        // The first monitor has observed idle, but still owns its registry key.
        Volatile.Write(&phase, 2)
        let! second = execute owner (request Operation.Build worker session)
        second |> refused RefusalCode.Canceled
        Volatile.Write(&phase, 3)
        removeAllowed.TrySetResult() |> ignore
        // No SessionsAsync/status request may restart monitoring for this wait.
        do! successorRead.Task.WaitAsync(TimeSpan.FromSeconds 2.)
        do! settledNotice.Task |> bounded
        worker.Intercept <- fun operation _ ->
          if operation = Operation.Status then Some(Task.FromException<Reply>(IOException "inspect retained background result"))
          else None
        let! view = snapshots owner
        let status = snapshot session view |> success
        (observed status).Busy |> Expect.isFalse "The successor published settled cleanup."
        (observed status).Current.IsSome |> Expect.equal "Reconciliation does not recreate execution authority." false
      finally
        removeAllowed.TrySetResult() |> ignore
        owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.).GetAwaiter().GetResult()
    }

    taskCase "canceled busy work publishes its eventual settled status without observer polling" <| fun () -> task {
      let worker = Worker("busy-cancel", 114)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let! built = execute owner (request Operation.Build worker session)
      success built |> ignore
      let monitorEntered = completion<unit> ()
      let monitorReleased = completion<Reply> ()
      let settledNotice = completion<unit> ()
      let mutable statusCalls = 0
      let mutable allowSettledNotice = 0
      use subscription = owner.Changed.Subscribe(fun () ->
        if Volatile.Read &allowSettledNotice = 1 then settledNotice.TrySetResult() |> ignore)
      worker.Intercept <- fun operation current ->
        if operation = Operation.Build then
          worker.Withdraw(current, true)
          Some(Task.FromException<Reply>(OperationCanceledException "exact request cancellation acknowledged"))
        elif operation = Operation.Status && Interlocked.Increment &statusCalls = 2 then
          monitorEntered.TrySetResult() |> ignore
          Some monitorReleased.Task
        else None
      let! canceled = execute owner (request Operation.Build worker session)
      canceled |> refused RefusalCode.Canceled
      do! monitorEntered.Task
      worker.SetBusy(session, false)
      Volatile.Write(&allowSettledNotice, 1)
      monitorReleased.TrySetResult(worker.Response(Operation.Status, session)) |> ignore
      do! settledNotice.Task
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Status then Some(Task.FromException<Reply>(IOException "inspect settled cache")) else None
      let! view = snapshots owner
      let status = snapshot session view |> success
      (observed status).Busy |> Expect.isFalse "background reconciliation published completion"
      (observed status).Current.IsSome |> Expect.equal "canceled operation remains withdrawn" false
      do! owner.StopAsync()
    }

    taskCase "caller disconnect after open dispatch cannot orphan the newly created session" <| fun () -> task {
      let worker = Worker("open-disconnect", 115)
      let entered = completion<unit> ()
      let released = completion<Reply> ()
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Open then
          entered.TrySetResult() |> ignore
          Some released.Task
        else None
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      use cancellation = new CancellationTokenSource()
      let opening = owner.ExecuteAsync(request Operation.Open worker "", cancellation.Token)
      do! entered.Task
      cancellation.Cancel()
      released.TrySetResult(worker.Response(Operation.Open, "")) |> ignore
      let! response = opening
      success response |> ignore
      let dispatchedToken = worker.Tokens |> Array.find (fun (operation, _) -> operation = Operation.Open) |> snd
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
        let! accepted = execute owner (request Operation.Build old known)
        success accepted |> ignore
        old.StopThrows <- stopFails
        old.Intercept <- fun operation _ ->
          if operation = Operation.Open then
            // The compiler accepted an additional session, but its successful
            // response was lost. The supervisor cannot address that session.
            old.Response(Operation.Open, "") |> ignore
            Some(Task.FromException<Reply>(TimeoutException "open reply lost after acceptance"))
          else None
        // Unknown open outcome retires the known sessions as well; the typed
        // Open request carries no preexisting session identity to disguise it.
        let! refusedOpen = execute owner (request Operation.Open old "")
        refusedOpen |> refused (if stopFails then RefusalCode.RetirementFailed else RefusalCode.Timeout)
        old.StopCount |> Expect.equal "ambiguous open requires physical process cleanup" 1
        let! view = snapshots owner
        snapshot known view |> assertWithdrawn
        if stopFails then
          let before = old.Requests.Length
          let! denied = execute owner (request Operation.Open fresh "")
          Result.isOk denied.Reply.Outcome |> Expect.isFalse "failed process stop prohibits another open"
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
