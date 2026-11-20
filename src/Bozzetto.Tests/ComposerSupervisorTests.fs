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
  mutable CleanupPending: bool
  mutable FormatterCleanupPending: bool
  mutable WorkerRetirementRequired: string option
  mutable Project: string
  mutable ManifestPath: string
}

/// Typed process boundary; these tests exercise actual supervisor ownership,
/// ordering and reconciliation without relying on a JSON-shaped fake protocol.
type private Worker(host: string, pid: int) =
  let gate = obj ()
  let exited = Event<string>()
  let processExited = Event<unit>()
  let inputsRevoked = Event<Reply>()
  let requests = ConcurrentQueue<Operation * string>()
  let tokens = ConcurrentQueue<Operation * CancellationToken>()
  let sessions = Dictionary<string, SessionState>()
  let mutable alive = true
  let mutable transportAlive = true
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
  member _.TransportAlive = lock gate (fun () -> transportAlive)
  member _.PublishInputNotification notification = inputsRevoked.Trigger notification
  member val Intercept: Operation -> string -> Task<Reply> option = (fun _ _ -> None) with get, set
  member val CleanupFails = false with get, set
  member val StopThrows = false with get, set
  member val StopReportsSurvivors = false with get, set
  member val StopLeavesAlive = false with get, set
  member val BeforeStop: unit -> Task = (fun () -> Task.CompletedTask) with get, set
  member _.Die() =
    lock gate (fun () ->
      alive <- false
      transportAlive <- false)
    exited.Trigger "worker process exited"
    processExited.Trigger()
  member _.LoseRootWithoutVerification() =
    lock gate (fun () ->
      alive <- false
      transportAlive <- false)
    exited.Trigger "worker process exited"
  member _.DisconnectTransport(reason: string) =
    lock gate (fun () -> transportAlive <- false)
    exited.Trigger reason
  member _.SignalProcessExited() =
    lock gate (fun () -> alive <- false)
    processExited.Trigger()
  member _.Withdraw(session, busy) =
    lock gate (fun () ->
      let state = sessions[session]
      state.Generation <- state.Generation + 1L
      state.Current <- None
      state.Busy <- busy)
  member _.SetBusy(session, busy) = lock gate (fun () -> sessions[session].Busy <- busy)
  member val DefaultProject = "/project/fixture.fidproj" with get, set
  member val DefaultManifestPath = "/external/provider/current.json" with get, set
  member val CloseLeavesCleanupPending = false with get, set
  member _.SetSessionPaths(session, project, manifestPath) =
    lock gate (fun () ->
      sessions[session].Project <- project
      sessions[session].ManifestPath <- manifestPath)
  member _.SetCleanupPending(session, pending) =
    lock gate (fun () -> sessions[session].CleanupPending <- pending)
  member _.SetFormatterCleanup(session, pending, retirement) =
    lock gate (fun () ->
      sessions[session].FormatterCleanupPending <- pending
      sessions[session].WorkerRetirementRequired <- retirement)
  member this.Response(operation, session) =
    lock gate (fun () ->
      match operation with
      | Operation.Open ->
        let id = sprintf "%s-session-%d" host (sessions.Count + 1)
        let project = this.DefaultProject
        let manifest = this.DefaultManifestPath
        sessions.Add(id, { Generation = 0L; Observation = 0UL; Current = None; Closed = false; Busy = false; CleanupPending = false; FormatterCleanupPending = false; WorkerRetirementRequired = None; Project = project; ManifestPath = manifest })
        reply id 0L (Result.Ok(Opened { Observation = 0UL; Project = project; ManifestPath = manifest }))
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
            state.CleanupPending <- this.CloseLeavesCleanupPending
            state.Observation <- state.Observation + 1UL
            Closed { Observation = state.Observation; Closed = true; CleanupPending = state.CleanupPending; CleanupError = None }
          | Operation.Status ->
            state.Observation <- state.Observation + 1UL
            Observed { Observation = state.Observation; Project = state.Project; ManifestPath = state.ManifestPath
                       Closed = state.Closed; Busy = state.Busy; Current = state.Current
                       RevocationPending = false; BackendError = None; FormatterError = None
                       FormatterCleanupPending = state.FormatterCleanupPending; WorkerRetirementRequired = state.WorkerRetirementRequired
                       CleanupPending = state.CleanupPending; CleanupError = None }
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
    member this.IsProcessAlive = this.Alive
    member this.IsAlive = this.Alive && this.TransportAlive
    member _.Exited = exited.Publish
    member _.ProcessExited = processExited.Publish
    member _.InputsRevoked = inputsRevoked.Publish
    member this.RequestAsync(body, cancellation) =
      let operation = BAREWireCodec.requestOperation body
      let session = ComposerWire.session body
      requests.Enqueue(operation, session)
      tokens.Enqueue(operation, cancellation)
      match this.Intercept operation session with
      | Some result -> result
      | None -> Task.FromResult(this.Response(operation, session))
    member this.StopAsync() = (task {
      Interlocked.Increment &stops |> ignore
      do! this.BeforeStop ()
      if this.StopThrows then return raise (InvalidOperationException "termination failed; process still alive")
      elif this.StopReportsSurvivors then
        this.LoseRootWithoutVerification()
        return raise (IOException "Composer worker process group left surviving descendants")
      elif not this.StopLeavesAlive then this.Die()
    } :> Task)

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
  | Operation.Format -> Format(address, 0L, {
      Document = "buffer://supervisor.clef"; Incarnation = "00000000-0000-0000-0000-000000000001"
      Revision = 1UL; Source = "let value = 1\n"; Configuration = "clef-two-space-lf-v1" })
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

let private cleanupAcrossMonitorWindow knownPending () = task {
  let worker = Worker("formatter-monitor-renewal", 128)
  let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true,
    (fun () -> Task.CompletedTask), monitorWindow = TimeSpan.FromMilliseconds 250.)
  let heldFormat = completion<Reply> ()
  let readEntered = completion<unit> ()
  let stopEntered = completion<unit> ()
  worker.BeforeStop <- fun () -> stopEntered.TrySetResult() |> ignore; Task.CompletedTask
  use cleanup =
    { new IAsyncDisposable with
        member _.DisposeAsync() =
          ValueTask(task {
            heldFormat.TrySetException(OperationCanceledException "test cleanup") |> ignore
            do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.)
          }) }
  do! task {
    let! session = openSession owner worker
    let mutable reads = 0
    let mutable expired = false
    worker.Intercept <- fun operation current ->
      if current = session && operation = Operation.Format then
        worker.SetFormatterCleanup(session, true, None)
        Some heldFormat.Task
      elif current = session && operation = Operation.Status then
        let token = worker.Tokens |> Array.last |> snd
        if not token.CanBeCanceled then None
        elif Interlocked.Increment &reads = 1 then
          readEntered.TrySetResult() |> ignore
          Some(task {
            try
              do! Task.Delay(Timeout.Infinite, token)
              return worker.Response(Operation.Status, session)
            finally Volatile.Write(&expired, token.IsCancellationRequested)
          })
        else
          Volatile.Read &expired |> Expect.isTrue "the prior monitoring window expired before renewal"
          worker.SetFormatterCleanup(session, true, Some "Formatter cleanup exceeded its deadline after monitor renewal.")
          None
      else None
    let! pendingFormat = task {
      if knownPending then
        worker.SetFormatterCleanup(session, true, None)
        let! initial = execute owner (request Operation.Status worker session)
        (observed initial).FormatterCleanupPending |> Expect.isTrue "cleanup is observed before monitoring its deadline"
        return None
      else
        return Some(execute owner (request Operation.Format worker session))
    }
    do! readEntered.Task
    // No subsequent client request drives this: the monitoring window expires
    // during a failed status read and its successor must observe retirement.
    do! stopEntered.Task
    Volatile.Read &reads >= 2 |> Expect.isTrue "owned cleanup survives expiry of the general reconciliation window"
    match pendingFormat with
    | Some pending ->
      heldFormat.TrySetResult(worker.Response(Operation.Status, session)) |> ignore
      let! response = pending
      response |> refused RefusalCode.Closed
    | None -> ()
    let! view = snapshots owner
    snapshot session view |> assertWithdrawn
  }
}

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
  let late =
    match late.Outcome with
    | Result.Ok(Observed status) ->
      { late with Outcome = Result.Ok(Observed { status with WorkerRetirementRequired = Some "Old formatter cleanup expired." }) }
    | _ -> late
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
  fresh.StopCount |> Expect.equal "late old retirement signals cannot stop the new epoch" 0
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

// Places a manifest where Bozzetto.Composer's Worker.fs keeps it: <root>/<host>/<session>/<epoch>/current.json,
// with the fake worker's n-th opened session id.
let private writeManifest (root: string) (worker: Worker) (n: int) (status: string) =
  let dir = Path.Combine(root, worker.Host, sprintf "%s-session-%d" worker.Host n, worker.Epoch)
  Directory.CreateDirectory dir |> ignore
  let manifest = Path.Combine(dir, "current.json")
  File.WriteAllText(manifest, sprintf """{"status":"%s"}""" status)
  manifest

// Completes once the supervisor's own background monitors have issued `count` more status reads.
let private afterStatusReads (worker: Worker) count =
  let reached = completion<unit> ()
  let reads = ref 0
  worker.Intercept <- fun operation _ ->
    if operation = Operation.Status && Interlocked.Increment &reads.contents >= count then reached.TrySetResult() |> ignore
    None
  reached.Task

[<Tests>]
let tests =
  testList "Composer daemon supervisor" [
    taskCase "typed archive revocation pushes the directory while stale and foreign notifications cannot restore or alter it" <| fun () -> task {
      let worker = Worker("archive-push", 123)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      use cleanup =
        { new IAsyncDisposable with
            member _.DisposeAsync() = ValueTask(owner.StopAsync()) }
      do! task {
        let! session = openSession owner worker
        let! _ = execute owner (request Operation.Reserve worker session)
        let! _ = execute owner (request Operation.Build worker session)
        let! _ = execute owner (request Operation.Status worker session)
        let mutable pushes = 0
        use changed = owner.Changed.Subscribe(fun () -> pushes <- pushes + 1)
        worker.Withdraw(session, false)
        let fresh = worker.Response(Operation.Status, session)
        let notification =
          match fresh.Outcome with
          | Result.Ok(Observed status) -> { fresh with RequestId = ""; Outcome = Result.Ok(InputsRevoked status) }
          | _ -> failwith "Expected status"
        let before = worker.Requests.Length
        worker.PublishInputNotification notification
        pushes |> Expect.equal "one event pushes without a request" 1
        let current = owner.CachedDirectory().Sessions
        let projected = snapshot session current
        (observed projected).Current |> Expect.isNone "archive event clears directory authority"
        projected.StatusFresh |> Expect.isTrue "worker notification is an actual observation"
        worker.Requests.Length |> Expect.equal "archive notification adds no status polling" before
        worker.PublishInputNotification notification
        let newerObservation =
          match notification.Outcome with
          | Result.Ok(InputsRevoked status) -> { notification with Outcome = Result.Ok(InputsRevoked { status with Observation = status.Observation + 1UL }) }
          | _ -> failwith "Expected notification"
        worker.PublishInputNotification { newerObservation with Authority = { notification.Authority with Host = "foreign" } }
        worker.PublishInputNotification { newerObservation with Authority = { notification.Authority with Generation = 0L } }
        pushes |> Expect.equal "duplicate, foreign and old-generation notifications are refused" 1
      }
    }
    taskCase "a session closed with its cleanup finished leaves the directory; a retired worker's sessions stay, withdrawn" <| fun () -> task {
      // Mutation caught: keep storing the Closed reply and the directory would list a card that is never fresh again.
      let worker = Worker("closing", 123)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! first = openSession owner worker
      let! second = openSession owner worker
      let! closed = execute owner (request Operation.Close worker first)
      success closed |> ignore
      let! entries = snapshots owner
      entries |> Array.map sessionOf |> Expect.equal "the closed session is gone; the open one stands" [| second |]
      let! retired = execute owner (request Operation.PrepareCompilerChange worker "")
      success retired |> ignore
      let! after = snapshots owner
      snapshot second after |> assertWithdrawn
      do! owner.StopAsync()
    }
    taskCase "every caller's operations are journaled with their outcome, newest first; status reads are not; refusals are" <| fun () -> task {
      // Mutations caught: journal status reads and the list would carry a fourth entry; drop the refusal branch and the refused build would be missing.
      let worker = Worker("journal", 125)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let changes = ref 0
      use _ = owner.JournalChanged.Subscribe(fun () -> Interlocked.Increment &changes.contents |> ignore)
      let! opened = owner.ExecuteAsync(request Operation.Open worker "", "mcp:t1", CancellationToken.None)
      let session = sessionOf (success opened)
      let! _ = owner.ExecuteAsync(request Operation.Status worker session, "mcp:t1", CancellationToken.None)
      let! ran = owner.ExecuteAsync(request Operation.Run worker session, "browser", CancellationToken.None)
      success ran |> ignore
      let! refusedBuild = owner.ExecuteAsync(request Operation.Build worker "", "http", CancellationToken.None)
      refusedBuild |> refused RefusalCode.InvalidRequest
      let journal = owner.Journal
      journal.Entries |> List.map (fun e -> e.Caller, (match e.Context with ComposerJournal.Context.Composer(operation, _) -> operation | _ -> failtest "expected compiler context"), (match e.Outcome with ComposerJournal.Outcome.Succeeded -> "ok" | ComposerJournal.Outcome.Refused _ -> "refused" | ComposerJournal.Outcome.Failed _ -> "failed" | ComposerJournal.Outcome.Running -> "running" | ComposerJournal.Outcome.Abandoned _ -> "abandoned" | ComposerJournal.Outcome.Unknown _ -> "unknown"))
      |> Expect.equal "newest first, no status read, the refusal kept"
           [ "http", Operation.Build, "refused"; "browser", Operation.Run, "ok"; "mcp:t1", Operation.Open, "ok" ]
      journal.Entries |> List.map _.Sequence |> Expect.equal "stamped one on per entry" [ 3L; 2L; 1L ]
      (journal.Entries.[1].Run |> Option.map _.StandardOutput) |> Expect.equal "the run carries its transcript" (Some "native result\n")
      (journal.Entries.[1].Context, journal.Entries.[2].Project <> "")
      |> Expect.equal
        "the run names its session; the open names its project"
        (ComposerJournal.Context.Composer(Operation.Run, session), true)
      changes.Value |> Expect.equal "one announcement per entry" 3
      do! owner.StopAsync()
    }
    taskCase "the MCP adapter labels the caller with its transport binding, as a report" <| fun () -> task {
      // Mutation caught: label every MCP call "mcp" and the transport would be lost; label with a self-declared name and it would pose as identity.
      Bozzetto.Server.ComposerTools.callerLabel (Some "t9") |> Expect.equal "bound" "mcp:t9"
      Bozzetto.Server.ComposerTools.callerLabel None |> Expect.equal "unbound" "mcp"
      Bozzetto.Server.ComposerTools.callerLabel (Some " ") |> Expect.equal "blank is unbound" "mcp"
      let worker = Worker("mcp-label", 126)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let tools = Bozzetto.Server.ComposerTools.ComposerTools(owner)
      Bozzetto.McpTools.currentTransportSessionId.Value <- Some "t9"
      try
        let! _ = tools.composer_open_project("/project/fixture.fidproj", CancellationToken.None)
        ()
      finally Bozzetto.McpTools.currentTransportSessionId.Value <- None
      owner.Journal.Entries
      |> List.map (fun e ->
        e.Caller,
        (match e.Context with
         | ComposerJournal.Context.Composer(operation, _) -> operation
         | _ -> failtest "expected compiler context"))
      |> Expect.equal "the open is journaled under the transport label" [ "mcp:t9", Operation.Open ]
      do! owner.StopAsync()
    }
    taskCase "a status reply delayed past the close cannot bring the forgotten session back" <| fun () -> task {
      // Mutation caught: drop the tombstone and the late open observation would re-enter the directory as a card.
      let worker = Worker("late-status", 124)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      let! session = openSession owner worker
      let entered = completion<unit> ()
      let released = completion<Reply> ()
      worker.Intercept <- fun operation current ->
        if current = session && operation = Operation.Status then
          entered.TrySetResult() |> ignore
          Some released.Task
        else None
      let pending = execute owner (request Operation.Status worker session)
      do! entered.Task
      // An open observation captured before the close; its delivery is delayed past it.
      let late = worker.Response(Operation.Status, session)
      worker.Intercept <- fun _ _ -> None
      let! closed = execute owner (request Operation.Close worker session)
      success closed |> ignore
      released.TrySetResult late |> ignore
      let! _ = pending
      let! entries = snapshots owner
      entries |> Array.exists (fun entry -> sessionOf entry = session) |> Expect.isFalse "the forgotten session is not brought back by a delayed observation"
      do! owner.StopAsync()
    }
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

    taskCase "formatter cleanup deadline retires every session without another editor request and joins before replacement" <| fun () -> task {
      let old = Worker("formatter-deadline", 125)
      let fresh = Worker("formatter-replacement", 126)
      let mutable creations = 0
      let owner = ComposerSupervisor((fun () ->
        let count = Interlocked.Increment &creations
        if count > 1 then old.Alive |> Expect.isFalse "physical exit precedes replacement construction"
        Task.FromResult((if count = 1 then old else fresh) :> IComposerWorker)), true)
      let stopEntered = completion<unit> ()
      let stopReleased = completion<unit> ()
      old.BeforeStop <- fun () ->
        stopEntered.TrySetResult() |> ignore
        stopReleased.Task :> Task
      let lateBuild = completion<Reply> ()
      let lateFormat = completion<Reply> ()
      let monitorEntered = completion<unit> ()
      let monitorReleased = completion<Reply> ()
      try
        let! first = openSession owner old
        let! second = openSession owner old
        let! firstBuilt = execute owner (request Operation.Build old first)
        let! secondBuilt = execute owner (request Operation.Build old second)
        success firstBuilt |> ignore
        success secondBuilt |> ignore
        let mutable reads = 0
        old.Intercept <- fun operation session ->
          if operation = Operation.Status && session = first && Interlocked.Increment &reads = 2 then
            monitorEntered.TrySetResult() |> ignore
            Some monitorReleased.Task
          elif operation = Operation.Format && session = first then
            old.SetFormatterCleanup(first, true, None)
            Some lateFormat.Task
          elif operation = Operation.Build && session = second then Some lateBuild.Task
          else None
        let pendingBuild = execute owner (request Operation.Build old second)
        let lateSuccess = old.Response(Operation.Build, second)
        let pendingFormat = execute owner (request Operation.Format old first)
        do! monitorEntered.Task
        pendingFormat.IsCompleted |> Expect.isFalse "cleanup supervision starts before the original format replies"
        let reason = "Formatter cleanup exceeded its 30-second deadline."
        old.SetFormatterCleanup(first, true, Some reason)
        // This is the already-running monitor's reply. No further client
        // status, save, format or close is needed to trigger supervision.
        monitorReleased.TrySetResult(old.Response(Operation.Status, first)) |> ignore
        do! stopEntered.Task
        let! duringStop = snapshots owner
        for entry in duringStop do
          assertWithdrawn entry
          entry.WorkerError |> Expect.equal "all sessions retain the escalation cause" (Some reason)
        (snapshot first duringStop |> observed).FormatterCleanupPending
        |> Expect.isTrue "the deadline never fabricates completed formatter cleanup"
        old.Requests |> Array.exists (fun (operation, _) -> operation = Operation.PrepareCompilerChange)
        |> Expect.isFalse "expired cleanup proceeds directly to the owned process stop"
        lateBuild.TrySetResult lateSuccess |> ignore
        let! late = pendingBuild
        late |> refused RefusalCode.Closed
        lateFormat.TrySetResult(old.Response(Operation.Status, first)) |> ignore
        let! formatted = pendingFormat
        formatted |> refused RefusalCode.Closed
        let opening = execute owner (request Operation.Open fresh "")
        opening.IsCompleted |> Expect.isFalse "replacement waits for physical process join"
        creations |> Expect.equal "no replacement factory runs during stop" 1
        stopReleased.TrySetResult() |> ignore
        let! replacement = opening
        success replacement |> ignore
        creations |> Expect.equal "one replacement opens after the old process joins" 2
        let! afterStop = snapshots owner
        snapshot first afterStop |> assertWithdrawn
        snapshot second afterStop |> assertWithdrawn
        (snapshot first afterStop).WorkerError |> Expect.equal "exit notification retains the initiating deadline" (Some reason)
      finally
        stopReleased.TrySetResult() |> ignore
        monitorReleased.TrySetException(OperationCanceledException "test cleanup") |> ignore
        lateBuild.TrySetException(OperationCanceledException "test cleanup") |> ignore
        lateFormat.TrySetException(OperationCanceledException "test cleanup") |> ignore
        owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.).GetAwaiter().GetResult()
    }

    taskCase "pending formatter cleanup renews an expired monitor after a failed status read" (cleanupAcrossMonitorWindow true)

    taskCase "held mutation renews an expired monitor before its first cleanup observation" (cleanupAcrossMonitorWindow false)

    taskCase "failed formatter-deadline process stop stays fenced and observable" <| fun () -> task {
      let worker = Worker("formatter-stop-failure", 127)
      worker.StopThrows <- true
      let mutable creations = 0
      let owner = ComposerSupervisor((fun () ->
        Interlocked.Increment &creations |> ignore
        Task.FromResult(worker :> IComposerWorker)), true)
      use cleanup =
        { new IAsyncDisposable with
            member _.DisposeAsync() =
              ValueTask(task {
                worker.StopThrows <- false
                do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.)
              }) }
      do! task {
        let! session = openSession owner worker
        worker.SetFormatterCleanup(session, true, Some "Formatter cleanup exceeded its deadline.")
        let! status = execute owner (request Operation.Status worker session)
        status |> refused RefusalCode.Closed
        let! view = snapshots owner
        let terminal = snapshot session view
        assertWithdrawn terminal
        terminal.WorkerError.Value.Contains("termination failed", StringComparison.Ordinal)
        |> Expect.isTrue "failed physical termination remains observable"
        (observed terminal).FormatterCleanupPending |> Expect.isTrue "failed termination never claims a drain"
        let! retry = execute owner (request Operation.Open worker "")
        retry |> refused RefusalCode.ProviderUnavailable
        creations |> Expect.equal "failed retirement forbids a replacement process" 1
      }
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

    taskCase "shutdown joins monitors after a memoized termination failure and retains the live worker" <| fun () -> task {
      let worker = Worker("memoized-stop-failure", 130)
      let underlying = worker :> IComposerWorker
      let failure = InvalidOperationException "owned process could not be terminated"
      let failedStop = Task.FromException failure
      let mutable transportAvailable = true
      let connection =
        { new IComposerWorker with
            member _.Handshake = underlying.Handshake
            member _.ProcessId = underlying.ProcessId
            member _.IsProcessAlive = underlying.IsProcessAlive
            member _.IsAlive = transportAvailable && underlying.IsAlive
            member _.Exited = underlying.Exited
            member _.ProcessExited = underlying.ProcessExited
            member _.InputsRevoked = underlying.InputsRevoked
            member _.RequestAsync(body, cancellation) = underlying.RequestAsync(body, cancellation)
            member _.StopAsync() =
              transportAvailable <- false
              failedStop }
      let owner = ComposerSupervisor((fun () -> Task.FromResult connection), true)
      let readEntered = completion<unit> ()
      let readReleased = completion<Reply> ()
      use cleanup =
        { new IAsyncDisposable with
            member _.DisposeAsync() = ValueTask(task {
              readReleased.TrySetException(OperationCanceledException "test cleanup") |> ignore
              try do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.)
              with :? InvalidOperationException -> ()
            }) }
      let! session = openSession owner worker
      worker.SetBusy(session, true)
      let mutable reads = 0
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Status && Interlocked.Increment(&reads) > 1 then
          readEntered.TrySetResult() |> ignore
          Some readReleased.Task
        else None
      let! status = execute owner (request Operation.Status worker session)
      (observed status).Busy |> Expect.isTrue "busy work starts its owned status monitor"
      do! readEntered.Task
      let! retirement = execute owner (request Operation.PrepareCompilerChange worker "")
      retirement |> refused RefusalCode.ProviderUnavailable
      failedStop.IsFaulted |> Expect.isTrue "the previous retirement already settled as a fault"
      let stopping = task {
        try
          do! owner.StopAsync()
          return None
        with error -> return Some error
      }
      stopping.IsCompleted |> Expect.isFalse "a failed stop cannot bypass a still-owned monitor read"
      readReleased.TrySetResult(worker.Response(Operation.Status, session)) |> ignore
      let! error = stopping
      error |> Expect.isSome "shutdown reports the failed physical termination after joining"
      obj.ReferenceEquals(error.Value, failure) |> Expect.isTrue "the original termination failure survives shutdown"
      worker.Alive |> Expect.isTrue "settled fault is not process exit"
      connection.IsAlive |> Expect.isFalse "failed termination leaves transport unavailable"
      owner.WorkerPid |> Expect.equal "failed shutdown retains ownership of the live process" (Some 130)
      let! directory = owner.SessionsAsync CancellationToken.None
      directory.Worker |> Expect.isSome "directory retains the physical worker after transport withdrawal"
      let terminal = snapshot session directory.Sessions
      terminal |> assertWithdrawn
      terminal.WorkerError.Value.Contains(failure.Message, StringComparison.Ordinal)
      |> Expect.isTrue "failed shutdown remains observable in the session view"
      let! opening = execute owner (request Operation.Open worker "")
      opening |> refused RefusalCode.ProviderUnavailable
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
      use cleanup =
        { new IAsyncDisposable with
            member _.DisposeAsync() =
              ValueTask(task {
                removeAllowed.TrySetResult() |> ignore
                do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.)
              }) }
      do! task {
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
      }
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

    taskCase "closing all sessions with cleanupPending=true autonomously retires composer-worker after threshold and prunes closed sessions (#33 #37)" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-b1b-33-" + Guid.NewGuid().ToString("N"))
      Directory.CreateDirectory tempRoot |> ignore
      try
        let nowRef = ref (DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))
        let worker = Worker("cleanup-retire", 235424)
        worker.CloseLeavesCleanupPending <- true
        let manifest1 = writeManifest tempRoot worker 1 "closed"
        let manifest2 = writeManifest tempRoot worker 2 "closed"
        let owner =
          ComposerSupervisor(
            (fun () -> Task.FromResult(worker :> IComposerWorker)),
            true,
            (fun () -> Task.CompletedTask),
            cleanupRetirementThreshold = TimeSpan.FromSeconds 30.0,
            utcNow = (fun () -> nowRef.Value),
            providerCacheRoot = tempRoot)
        use _stopOwner =
          { new IAsyncDisposable with
              member _.DisposeAsync() =
                ValueTask(task { do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.) }) }
        worker.DefaultManifestPath <- manifest1
        let! first = openSession owner worker
        worker.DefaultManifestPath <- manifest2
        let! second = openSession owner worker
        let! _ = execute owner (request Operation.Close worker first)
        let! _ = execute owner (request Operation.Close worker second)
        // Negative control: before the 30s threshold the background monitors keep observing,
        // and the worker is not retired. No client SessionsAsync call drives any of this.
        nowRef.Value <- nowRef.Value.AddSeconds 20.0
        do! afterStatusReads worker 3
        worker.StopCount |> Expect.equal "worker not retired before cleanup threshold" 0
        owner.CachedDirectory().Sessions.Length |> Expect.equal "closed cleanupPending sessions stay visible while worker is alive" 2
        // Observe the retirement marking and autonomous retirement strictly via owner.Changed and
        // worker.BeforeStop. At the first observation the retirement fence already holds, so a
        // further operation is refused synchronously instead of reaching the retiring worker.
        let retirementMarked = TaskCompletionSource<string option * RefusalCode option>(TaskCreationOptions.RunContinuationsAsynchronously)
        let stopEntered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let directoryCleared = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        use _sub =
          owner.Changed.Subscribe(fun () ->
            let dir = owner.CachedDirectory()
            for snap in dir.Sessions do
              match snap.Reply.Outcome with
              | Result.Ok(Observed st) when st.WorkerRetirementRequired.IsSome && not retirementMarked.Task.IsCompleted ->
                let probe = owner.ExecuteAsync(request Operation.Status worker first, CancellationToken.None)
                let refusal =
                  if not probe.IsCompleted then None
                  else
                    match probe.Result.Reply.Outcome with
                    | Result.Error refusal -> Some refusal.Code
                    | Result.Ok _ -> None
                retirementMarked.TrySetResult((st.WorkerRetirementRequired, refusal)) |> ignore
              | _ -> ()
            if dir.Worker.IsNone && dir.Sessions.Length = 0 && not (File.Exists manifest1 || File.Exists manifest2) then
              directoryCleared.TrySetResult() |> ignore)
        worker.BeforeStop <- fun () ->
          stopEntered.TrySetResult() |> ignore
          Task.CompletedTask
        // Advance past the 30s threshold: background reconciliation marks workerRetirementRequired
        // and autonomously retires the worker without any caller polling SessionsAsync.
        nowRef.Value <- nowRef.Value.AddSeconds 15.0
        let! observedReason, fencedRefusal = retirementMarked.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        observedReason |> Expect.isSome "supervisor marked WorkerRetirementRequired before retirement"
        fencedRefusal |> Expect.equal "retirement fence holds before Changed publishes the marking" (Some RefusalCode.ProviderUnavailable)
        do! stopEntered.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        do! directoryCleared.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        worker.StopCount |> Expect.equal "composer-worker autonomously retired past cleanupPending threshold" 1
        let afterDir = owner.CachedDirectory()
        afterDir.Worker |> Expect.isNone "retired worker cleared from cached directory without polling"
        afterDir.Sessions |> Expect.isEmpty "closed sessions from retired epoch are pruned from /work"
        File.Exists manifest1 |> Expect.isFalse "pruned closed session 1 manifest directory from provider-sessions"
        File.Exists manifest2 |> Expect.isFalse "pruned closed session 2 manifest directory from provider-sessions"
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    taskCase "closing one session while a sibling is open and building past the cleanup threshold never retires the worker; the last close does" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-sibling-" + Guid.NewGuid().ToString("N"))
      Directory.CreateDirectory tempRoot |> ignore
      try
        let nowRef = ref (DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))
        let worker = Worker("sibling-host", 4401)
        worker.CloseLeavesCleanupPending <- true
        let manifestA = writeManifest tempRoot worker 1 "closed"
        let manifestB = writeManifest tempRoot worker 2 "open"
        let owner =
          ComposerSupervisor(
            (fun () -> Task.FromResult(worker :> IComposerWorker)), true, (fun () -> Task.CompletedTask),
            cleanupRetirementThreshold = TimeSpan.FromSeconds 30.0,
            utcNow = (fun () -> nowRef.Value),
            providerCacheRoot = tempRoot)
        use _stopOwner =
          { new IAsyncDisposable with
              member _.DisposeAsync() = ValueTask(task { do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.) }) }
        worker.DefaultManifestPath <- manifestA
        let! sessionA = openSession owner worker
        worker.DefaultManifestPath <- manifestB
        let! sessionB = openSession owner worker
        // Session B is building: its long compile keeps it open and busy.
        worker.SetBusy(sessionB, true)
        let! _ = execute owner (request Operation.Status worker sessionB)
        let! _ = execute owner (request Operation.Close worker sessionA)
        // A's cleanup has been pending far beyond the threshold while B keeps building.
        nowRef.Value <- nowRef.Value.AddSeconds 45.0
        do! afterStatusReads worker 4
        nowRef.Value <- nowRef.Value.AddSeconds 300.0
        do! afterStatusReads worker 4
        worker.StopCount |> Expect.equal "closing A never retires the worker while sibling B is open" 0
        let during = owner.CachedDirectory()
        during.Worker |> Expect.isSome "worker stays published"
        let siblingB = snapshot sessionB during.Sessions
        siblingB.WorkerError |> Expect.isNone "sibling B is not withdrawn"
        let statusB = observed siblingB
        statusB.Closed |> Expect.isFalse "sibling B stays open"
        statusB.Busy |> Expect.isTrue "sibling B is still building"
        statusB.WorkerRetirementRequired |> Expect.isNone "sibling B carries no retirement"
        (snapshot sessionA during.Sessions |> observed).WorkerRetirementRequired
        |> Expect.isNone "closed A is not marked for retirement while B is open"
        File.Exists manifestA |> Expect.isTrue "A's manifest is kept while its worker lives"
        // B finishes its build and closes: every owned session is now closed and A's cleanup is
        // past the threshold, so the supervisor retires the worker autonomously.
        let stopEntered = completion<unit> ()
        let cleared = completion<unit> ()
        worker.BeforeStop <- fun () -> stopEntered.TrySetResult() |> ignore; Task.CompletedTask
        use _sub =
          owner.Changed.Subscribe(fun () ->
            let dir = owner.CachedDirectory()
            if dir.Worker.IsNone && dir.Sessions.Length = 0 && not (File.Exists manifestA || File.Exists manifestB) then
              cleared.TrySetResult() |> ignore)
        worker.SetBusy(sessionB, false)
        let! _ = execute owner (request Operation.Close worker sessionB)
        do! stopEntered.Task
        do! cleared.Task
        worker.StopCount |> Expect.equal "the last close retires the worker exactly once" 1
        File.Exists manifestA |> Expect.isFalse "A's manifest pruned after the epoch exits"
        File.Exists manifestB |> Expect.isFalse "B's manifest pruned after the epoch exits"
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    taskCase "an Open awaiting its reply blocks fallback retirement after the last known session closes" <| fun () -> task {
      let worker = Worker("open-in-flight", 4402)
      worker.CloseLeavesCleanupPending <- true
      let now = ref (DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true, (fun () -> Task.CompletedTask), utcNow = (fun () -> now.Value))
      use _stop = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(owner.StopAsync()) }
      let! first = openSession owner worker
      let entered = completion<unit> ()
      let reply = completion<Reply> ()
      worker.Intercept <- fun operation _ ->
        if operation = Operation.Open then
          entered.TrySetResult() |> ignore
          Some reply.Task
        else None
      let opening = execute owner (request Operation.Open worker "")
      try
        do! entered.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        let! closed = execute owner (request Operation.Close worker first)
        success closed |> ignore
        now.Value <- now.Value.AddSeconds 45.0
        let! status = execute owner (request Operation.Status worker first)
        success status |> ignore
        worker.StopCount |> Expect.equal "an admitted Open protects the worker past the threshold" 0
        reply.TrySetResult(worker.Response(Operation.Open, "")) |> ignore
        let! opened = opening.WaitAsync(TimeSpan.FromSeconds 5.)
        success opened |> ignore
        worker.StopCount |> Expect.equal "the published session takes over protection" 0
      finally
        reply.TrySetCanceled() |> ignore
    }

    taskCase "the last closed session drains its build before the fallback disposal clock starts" <| fun () -> task {
      let worker = Worker("last-build-drain", 4403)
      worker.CloseLeavesCleanupPending <- true
      let now = ref (DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true, (fun () -> Task.CompletedTask), utcNow = (fun () -> now.Value))
      use _stop = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(owner.StopAsync()) }
      let! session = openSession owner worker
      worker.SetBusy(session, true)
      let! _ = execute owner (request Operation.Close worker session)
      now.Value <- now.Value.AddSeconds 120.0
      let! draining = execute owner (request Operation.Status worker session)
      let drainingStatus = observed draining
      (drainingStatus.Closed && drainingStatus.Busy && drainingStatus.CleanupPending) |> Expect.isTrue "the last session is closed and still building"
      worker.StopCount |> Expect.equal "elapsed build drain does not retire the worker" 0
      worker.SetBusy(session, false)
      let! _ = execute owner (request Operation.Status worker session)
      worker.StopCount |> Expect.equal "disposal gets its own threshold after the drain" 0
      let stopEntered = completion<unit> ()
      worker.BeforeStop <- fun () -> stopEntered.TrySetResult() |> ignore; Task.CompletedTask
      now.Value <- now.Value.AddSeconds 30.0
      let! _ = execute owner (request Operation.Status worker session)
      do! stopEntered.Task.WaitAsync(TimeSpan.FromSeconds 5.)
      worker.StopCount |> Expect.equal "only overdue disposal triggers fallback retirement" 1
    }

    taskCase "dead worker epoch clears cleanupPending and prunes closed sessions without transport-reset diagnostics, and evicts deleted .fidproj sessions (#37)" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-b1b-37-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      Directory.CreateDirectory cacheRoot |> ignore
      try
        let projClosed = Path.Combine(tempRoot, "ClosedScratch.fidproj")
        let projOpen = Path.Combine(tempRoot, "OpenScratch.fidproj")
        File.WriteAllText(projClosed, """{"name":"ClosedScratch"}""")
        File.WriteAllText(projOpen, """{"name":"OpenScratch"}""")
        let worker = Worker("dead-epoch", 9876)
        worker.CloseLeavesCleanupPending <- true
        let closedManifest = writeManifest cacheRoot worker 1 "closed"
        let openManifest = writeManifest cacheRoot worker 2 "open"
        let owner =
          ComposerSupervisor(
            (fun () -> Task.FromResult(worker :> IComposerWorker)),
            true,
            (fun () -> Task.CompletedTask),
            providerCacheRoot = cacheRoot)
        use _stopOwner =
          { new IAsyncDisposable with
              member _.DisposeAsync() =
                ValueTask(task { do! owner.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.) }) }
        worker.DefaultProject <- projClosed
        worker.DefaultManifestPath <- closedManifest
        let! closedSess = openSession owner worker
        worker.DefaultProject <- projOpen
        worker.DefaultManifestPath <- openManifest
        let! openSess = openSession owner worker
        // Close closedSess leaving CleanupPending = true, while openSess stays open.
        let! _ = execute owner (request Operation.Close worker closedSess)
        let beforeDeath = owner.CachedDirectory().Sessions
        (snapshot closedSess beforeDeath |> observed).CleanupPending |> Expect.isTrue "closed session is cleanupPending while worker lives"
        // Worker process exits abruptly (simulating Connection reset by peer).
        worker.Die()
        let reqsAtDeath = worker.Requests.Length
        let! afterDeath = owner.SessionsAsync CancellationToken.None
        worker.Requests.Length |> Expect.equal "dead worker socket is never polled after epoch exit" reqsAtDeath
        afterDeath.Worker |> Expect.isNone "dead worker is null in directory"
        // Closed session from dead epoch is reconciled & pruned (no cleanupPending, no Connection reset by peer).
        afterDeath.Sessions |> Array.exists (fun s -> sessionOf s = closedSess)
        |> Expect.isFalse "closed session from dead epoch is pruned from /work COMPOSER SESSIONS"
        File.Exists closedManifest |> Expect.isFalse "closed session manifest pruned from provider-sessions"
        // Previously open session whose .fidproj still exists stays withdrawn so user sees why it stopped.
        snapshot openSess afterDeath.Sessions |> assertWithdrawn
        // Deleting the open session's scratch .fidproj from disk evicts it on observation too.
        File.Delete projOpen
        let! afterDelete = owner.SessionsAsync CancellationToken.None
        afterDelete.Sessions |> Expect.isEmpty "withdrawn session for deleted .fidproj is evicted"
        File.Exists openManifest |> Expect.isFalse "evicted deleted-.fidproj manifest removed from provider-sessions"
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    testCase "provider-session cache prunes only its exact owner's manifest and refuses every other path with its reason" <| fun () ->
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-cache-owner-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      let outsideDir = Path.Combine(tempRoot, "outside-cache")
      Directory.CreateDirectory cacheRoot |> ignore
      Directory.CreateDirectory outsideDir |> ignore
      try
        let owner: WorkerAddress = { Host = "h1"; Epoch = "e1"; Provider = ProviderIdentity.ClefComposer }
        let place (host: string) (session: string) (epoch: string) =
          let dir = Path.Combine(cacheRoot, host, session, epoch)
          Directory.CreateDirectory dir |> ignore
          let manifest = Path.Combine(dir, "current.json")
          File.WriteAllText(manifest, """{"status":"closed"}""")
          manifest
        let prune = ProviderSessionCache.tryPruneManifestPath (Some cacheRoot)
        let mine = place "h1" "s1" "e1"
        let otherSession = place "h1" "s2" "e1"
        let otherEpoch = place "h1" "s1" "e2"
        let otherHost = place "h2" "s1" "e1"
        let outsideManifest = Path.Combine(outsideDir, "current.json")
        File.WriteAllText(outsideManifest, """{"status":"closed"}""")
        // Exact-reason negative controls.
        prune owner "s1" otherSession |> Expect.equal "another session of the owner is refused" (ProviderSessionCache.PruneOutcome.OwnerMismatch("h1/s1/e1", "h1/s2/e1"))
        prune owner "s1" otherEpoch |> Expect.equal "another epoch of the session is refused" (ProviderSessionCache.PruneOutcome.OwnerMismatch("h1/s1/e1", "h1/s1/e2"))
        prune owner "s1" otherHost |> Expect.equal "another host's session is refused" (ProviderSessionCache.PruneOutcome.OwnerMismatch("h1/s1/e1", "h2/s1/e1"))
        prune owner "s1" outsideManifest |> Expect.equal "a path outside the root is refused" (ProviderSessionCache.PruneOutcome.WrongLayout(Path.Combine("..", "outside-cache")))
        prune owner "s1" (Path.Combine(cacheRoot, "h1", "..", "..", "outside-cache", "current.json"))
        |> Expect.equal ".. traversal is refused" (ProviderSessionCache.PruneOutcome.WrongLayout(Path.Combine("..", "outside-cache")))
        prune owner "s1" (Path.Combine(cacheRoot, "h1", "s1", "current.json"))
        |> Expect.equal "a 2-segment path is refused" (ProviderSessionCache.PruneOutcome.WrongLayout(Path.Combine("h1", "s1")))
        prune owner "s1" (Path.Combine(cacheRoot, "h1", "s1", "e1", "other.json"))
        |> Expect.equal "a different file name is refused" (ProviderSessionCache.PruneOutcome.WrongFileName "other.json")
        prune owner ".." mine |> Expect.equal "an invalid owner segment is refused" (ProviderSessionCache.PruneOutcome.OwnerMismatch("h1/../e1", "h1/s1/e1"))
        ProviderSessionCache.tryPruneManifestPath None owner "s1" mine |> Expect.equal "no configured root prunes nothing" ProviderSessionCache.PruneOutcome.NoCacheRoot
        // Fault injection: the owner's epoch directory replaced by a link to outside the root.
        let linkedOwner: WorkerAddress = { owner with Host = "h-link" }
        let linkParent = Path.Combine(cacheRoot, "h-link", "s1")
        Directory.CreateDirectory linkParent |> ignore
        Directory.CreateSymbolicLink(Path.Combine(linkParent, "e1"), outsideDir) |> ignore
        prune linkedOwner "s1" (Path.Combine(linkParent, "e1", "current.json"))
        |> Expect.equal "a linked epoch directory is refused" (ProviderSessionCache.PruneOutcome.LinkInPath(Path.Combine(linkParent, "e1")))
        File.Exists outsideManifest |> Expect.isTrue "the link target outside the root is untouched"
        File.Exists otherSession |> Expect.isTrue "other session untouched"
        File.Exists otherEpoch |> Expect.isTrue "other epoch untouched"
        File.Exists otherHost |> Expect.isTrue "other host untouched"
        // Positive control.
        prune owner "s1" mine |> Expect.equal "the owner's exact manifest is pruned" ProviderSessionCache.PruneOutcome.Pruned
        Directory.Exists(Path.GetDirectoryName mine) |> Expect.isFalse "the owner's epoch directory is gone"
        Directory.Exists(Path.Combine(cacheRoot, "h1", "s1")) |> Expect.isTrue "a session directory with another epoch stays"
        prune owner "s1" mine |> Expect.equal "a second prune finds nothing" ProviderSessionCache.PruneOutcome.Missing
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)

    testCase "a linked root ancestor resolves to its physical root; deletion stays below it and a link under it is refused" <| fun () ->
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-cache-ancestor-" + Guid.NewGuid().ToString("N"))
      let physicalBase = Path.Combine(tempRoot, "physical")
      let outsideDir = Path.Combine(tempRoot, "outside")
      Directory.CreateDirectory(Path.Combine(physicalBase, "provider-sessions")) |> ignore
      Directory.CreateDirectory outsideDir |> ignore
      try
        let linkedBase = Path.Combine(tempRoot, "linked")
        Directory.CreateSymbolicLink(linkedBase, physicalBase) |> ignore
        let configuredRoot = Path.Combine(linkedBase, "provider-sessions")
        let owner: WorkerAddress = { Host = "h1"; Epoch = "e1"; Provider = ProviderIdentity.ClefComposer }
        let epochDir = Path.Combine(configuredRoot, "h1", "s1", "e1")
        Directory.CreateDirectory epochDir |> ignore
        File.WriteAllText(Path.Combine(epochDir, "current.json"), """{"status":"closed"}""")
        let keep = Path.Combine(physicalBase, "keep.txt")
        File.WriteAllText(keep, "sibling of the physical root")
        // Fault injection: below the (link-resolved) root, the host segment is a link elsewhere.
        let linkedHostOwner: WorkerAddress = { owner with Host = "h-out" }
        Directory.CreateSymbolicLink(Path.Combine(physicalBase, "provider-sessions", "h-out"), outsideDir) |> ignore
        Directory.CreateDirectory(Path.Combine(outsideDir, "s1", "e1")) |> ignore
        File.WriteAllText(Path.Combine(outsideDir, "s1", "e1", "current.json"), """{"status":"closed"}""")
        ProviderSessionCache.tryPruneManifestPath (Some configuredRoot) linkedHostOwner "s1" (Path.Combine(configuredRoot, "h-out", "s1", "e1", "current.json"))
        |> Expect.equal "a link below the physical root is refused" (ProviderSessionCache.PruneOutcome.LinkInPath(Path.Combine(physicalBase, "provider-sessions", "h-out")))
        File.Exists(Path.Combine(outsideDir, "s1", "e1", "current.json")) |> Expect.isTrue "the outside target is untouched"
        // Positive control through the linked ancestor.
        ProviderSessionCache.tryPruneManifestPath (Some configuredRoot) owner "s1" (Path.Combine(epochDir, "current.json"))
        |> Expect.equal "the owner's manifest is pruned at its physical location" ProviderSessionCache.PruneOutcome.Pruned
        Directory.Exists(Path.Combine(physicalBase, "provider-sessions", "h1", "s1", "e1")) |> Expect.isFalse "physical epoch directory removed"
        Directory.Exists(Path.Combine(physicalBase, "provider-sessions")) |> Expect.isTrue "the physical root itself stays"
        File.Exists keep |> Expect.isTrue "nothing beside the physical root is touched"
        Directory.Exists linkedBase |> Expect.isTrue "the configured ancestor link stays"
        Directory.CreateDirectory epochDir |> ignore
        File.WriteAllText(Path.Combine(epochDir, "current.json"), """{"status":"closed"}""")
        let linkedRoot = Path.Combine(tempRoot, "root-link")
        Directory.CreateSymbolicLink(linkedRoot, configuredRoot) |> ignore
        ProviderSessionCache.pruneStaleCacheDirectories linkedRoot None
        |> Expect.equal "startup resolves the cache root itself and leaves linked hosts alone" 1
        Directory.Exists epochDir |> Expect.isFalse "the physical closed epoch is pruned through the linked root"
        File.Exists(Path.Combine(outsideDir, "s1", "e1", "current.json")) |> Expect.isTrue "startup preserves the outside manifest"
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)

    testCase "startup cache sweep runs only when no composer-worker is alive and the process table was read" <| fun () ->
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-startup-sweep-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      try
        let place (session: string) (status: string) =
          let dir = Path.Combine(cacheRoot, "h1", session, "e-dead")
          Directory.CreateDirectory dir |> ignore
          File.WriteAllText(Path.Combine(dir, "current.json"), sprintf """{"status":"%s"}""" status)
          dir
        let closedDir = place "closed-session" "closed"
        let openDir = place "open-session" "open"
        let names = Set.ofList [ "Bozzetto.Composer.dll" ]
        let proc pid state (argv: string list) : Bozzetto.ProcessWatchdog.ProcRecord =
          { Pid = pid; Ppid = 1; Pgid = pid; State = state; StartTimeTicks = 1L; ResidentBytes = 0L
            Comm = "dotnet"; Cmdline = String.concat " " argv; Argv = argv; StartTimeMsOpt = None }
        let liveWorker = proc 4100 'S' [ "/usr/bin/dotnet"; "/opt/boz/Bozzetto.Composer.dll"; "--socket"; "/run/boz/w.sock" ]
        let zombieWorker = proc 4101 'Z' [ "/usr/bin/dotnet"; "/opt/boz/Bozzetto.Composer.dll"; "--socket"; "/run/boz/w.sock" ]
        let socketless = proc 4102 'S' [ "/usr/bin/dotnet"; "/opt/boz/Bozzetto.Composer.dll" ]
        let otherTool = proc 4103 'S' [ "/usr/bin/dotnet"; "/opt/other/Other.dll"; "--socket"; "/run/x.sock" ]
        ProviderSessionCache.liveWorkerPids names [ liveWorker; zombieWorker; socketless; otherTool ]
        |> Expect.equal "only a live process running the worker with --socket counts" [ 4100 ]
        // Exact-reason negative controls: nothing is deleted.
        ProviderSessionCache.startupSweep cacheRoot names (Ok [ liveWorker; otherTool ])
        |> Expect.equal "a live composer-worker protects the cache" (ProviderSessionCache.StartupSweep.SkippedLiveWorkers [ 4100 ])
        ProviderSessionCache.startupSweep cacheRoot names (Error(Bozzetto.ProcessWatchdog.ProcScanError.ScanTruncated 7))
        |> Expect.equal "an unread process table proves nothing" (ProviderSessionCache.StartupSweep.SkippedUnverified "/proc entry count exceeded scan cap 7")
        Directory.Exists closedDir |> Expect.isTrue "closed manifest kept while unproven"
        // Positive control: no live worker; only the closed dead-epoch manifest goes.
        ProviderSessionCache.startupSweep cacheRoot names (Ok [ zombieWorker; socketless; otherTool ])
        |> Expect.equal "a dead epoch's closed manifest is swept" (ProviderSessionCache.StartupSweep.Pruned 1)
        Directory.Exists closedDir |> Expect.isFalse "closed dead-epoch manifest removed"
        Directory.Exists openDir |> Expect.isTrue "open manifest kept"
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)

    testCase "an unreadable proc cmdline skips the startup sweep while a genuinely empty one permits it" <| fun () ->
      if OperatingSystem.IsLinux() then
        let tempRoot = Path.Combine(Path.GetTempPath(), "boz-unreadable-proc-" + Guid.NewGuid().ToString("N"))
        let procRoot = Path.Combine(tempRoot, "proc")
        let pidDir = Path.Combine(procRoot, string Environment.ProcessId)
        let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
        let epoch = Path.Combine(cacheRoot, "h1", "s1", "e1")
        Directory.CreateDirectory pidDir |> ignore
        Directory.CreateDirectory epoch |> ignore
        File.WriteAllText(Path.Combine(epoch, "current.json"), """{"status":"closed"}""")
        try
          let stat = File.ReadAllText "/proc/self/stat"
          let scan readCmdline =
            Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesFromStrict procRoot
              (fun path -> if Path.GetFileName path = "cmdline" then readCmdline () else stat)
          let sweep scanResult = ProviderSessionCache.startupSweep cacheRoot (Set.singleton "Bozzetto.Composer.dll") scanResult
          for error in [ UnauthorizedAccessException("cmdline permission denied") :> exn; IOException("cmdline input/output error") :> exn ] do
            scan (fun () -> raise error) |> sweep
            |> Expect.equal "a cmdline read error is carried into the skip reason"
                (ProviderSessionCache.StartupSweep.SkippedUnverified("/proc inaccessible: " + error.Message))
            Directory.Exists epoch |> Expect.isTrue "unverified worker identity protects the cache"
          scan (fun () -> "") |> sweep
          |> Expect.equal "an actual empty cmdline identifies no worker" (ProviderSessionCache.StartupSweep.Pruned 1)
          // Transient ESRCH (errno 3) on an exiting PID is Absent, not Inaccessible.
          let esrchEpoch = Path.Combine(cacheRoot, "h1", "s1", "e2")
          Directory.CreateDirectory esrchEpoch |> ignore
          File.WriteAllText(Path.Combine(esrchEpoch, "current.json"), """{"status":"closed"}""")
          let exitingPidDir = Path.Combine(procRoot, string (Environment.ProcessId + 1))
          Directory.CreateDirectory exitingPidDir |> ignore
          Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesFromStrict procRoot
            (fun path ->
              if path.StartsWith(exitingPidDir, StringComparison.Ordinal) then
                raise (IOException("No such process", 3))
              elif Path.GetFileName path = "cmdline" then ""
              else stat)
          |> sweep
          |> Expect.equal "transient ESRCH on an exiting PID does not block strict startup sweep" (ProviderSessionCache.StartupSweep.Pruned 1)
        finally
          if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)

    testCase "readLinuxProcessesFrom skips transient ESRCH and unrelated inaccessible PIDs while strict mode fails closed" <| fun () ->
      if OperatingSystem.IsLinux() then
        let tempRoot = Path.Combine(Path.GetTempPath(), "boz-proc-resilience-" + Guid.NewGuid().ToString("N"))
        let procRoot = Path.Combine(tempRoot, "proc")
        let selfPid = Environment.ProcessId
        let esrchStatPid = selfPid + 10
        let deniedStatPid = selfPid + 11
        let esrchCmdPid = selfPid + 12
        let deniedCmdPid = selfPid + 13
        for p in [ selfPid; esrchStatPid; deniedStatPid; esrchCmdPid; deniedCmdPid ] do
          Directory.CreateDirectory(Path.Combine(procRoot, string p)) |> ignore
        try
          let selfStat = File.ReadAllText "/proc/self/stat"
          let statFor (pid: int) =
            let idx = selfStat.IndexOf('(')
            sprintf "%d %s" pid (selfStat.Substring idx)
          let readFake (path: string) =
            let dirName = Path.GetFileName(Path.GetDirectoryName path)
            let fileName = Path.GetFileName path
            match Int32.Parse dirName, fileName with
            | p, "stat" when p = esrchStatPid -> raise (IOException("No such process", 3))
            | p, "stat" when p = deniedStatPid -> raise (UnauthorizedAccessException "stat permission denied")
            | p, "cmdline" when p = esrchCmdPid -> raise (IOException("No such process", 3))
            | p, "cmdline" when p = deniedCmdPid -> raise (UnauthorizedAccessException "cmdline permission denied")
            | p, "stat" -> statFor p
            | _, "cmdline" -> "/usr/bin/dotnet\000worker.dll"
            | _ -> ""
          let withoutCmd = Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesFrom procRoot readFake false
          match withoutCmd with
          | Error err -> failtestf "best-effort scan without cmdline must skip ESRCH and restricted PIDs, got %A" err
          | Ok procs ->
            let pids = procs |> List.map _.Pid |> Set.ofList
            pids |> Expect.equal "best-effort stat scan keeps self and accessible PIDs" (Set.ofList [ selfPid; esrchCmdPid; deniedCmdPid ])
          let withCmd = Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesFrom procRoot readFake true
          match withCmd with
          | Error err -> failtestf "best-effort scan with cmdline must skip ESRCH and restricted PIDs, got %A" err
          | Ok procs ->
            let pids = procs |> List.map _.Pid |> Set.ofList
            pids |> Expect.equal "best-effort cmdline scan treats mid-probe ESRCH as absent and keeps restricted cmdline PID" (Set.ofList [ selfPid; deniedCmdPid ])
            (procs |> List.find (fun p -> p.Pid = deniedCmdPid)).Cmdline |> Expect.equal "unreadable cmdline falls back to empty in best-effort mode" ""
          let strict = Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesFromStrict procRoot readFake
          match strict with
          | Error(Bozzetto.ProcessWatchdog.ProcScanError.ProcDirectoryInaccessible reason) ->
            (reason = "stat permission denied" || reason = "cmdline permission denied")
            |> Expect.isTrue (sprintf "strict scan fails closed on restricted PID, got %s" reason)
          | other -> failtestf "expected strict scan to fail closed, got %A" other
          // Exact-reason controls: stat restriction and cmdline restriction each fail closed independently.
          let readStatDenied (path: string) =
            let dirName = Path.GetFileName(Path.GetDirectoryName path)
            let fileName = Path.GetFileName path
            match Int32.Parse dirName, fileName with
            | p, "stat" when p = deniedStatPid -> raise (UnauthorizedAccessException "stat permission denied")
            | p, "stat" -> statFor p
            | _, "cmdline" -> "/usr/bin/dotnet\000worker.dll"
            | _ -> ""
          Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesFromStrict procRoot readStatDenied
          |> Expect.equal "strict scan fails closed on stat EACCES" (Error(Bozzetto.ProcessWatchdog.ProcScanError.ProcDirectoryInaccessible "stat permission denied"))
          let readCmdDenied (path: string) =
            let dirName = Path.GetFileName(Path.GetDirectoryName path)
            let fileName = Path.GetFileName path
            match Int32.Parse dirName, fileName with
            | p, "cmdline" when p = deniedCmdPid -> raise (UnauthorizedAccessException "cmdline permission denied")
            | p, "stat" -> statFor p
            | _, "cmdline" -> "/usr/bin/dotnet\000worker.dll"
            | _ -> ""
          Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesFromStrict procRoot readCmdDenied
          |> Expect.equal "strict scan fails closed on cmdline EACCES" (Error(Bozzetto.ProcessWatchdog.ProcScanError.ProcDirectoryInaccessible "cmdline permission denied"))
        finally
          if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)

    taskCase "startup cache sweep skips while a real worker-shaped process lives and sweeps after it exits" <| fun () -> task {
      if not (OperatingSystem.IsLinux()) then skiptest "Linux /proc strict scan is required for real-process startup sweep."
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-startup-real-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      let closedDir = Path.Combine(cacheRoot, "h1", "closed-session", "e-dead")
      Directory.CreateDirectory closedDir |> ignore
      File.WriteAllText(Path.Combine(closedDir, "current.json"), """{"status":"closed"}""")
      // A unique worker file name keeps any real composer-worker on this host out of the probe.
      let probeName = "boz-sweep-probe-" + Guid.NewGuid().ToString("N") + ".dll"
      let info = Diagnostics.ProcessStartInfo("/bin/sh")
      for argument in [ "-c"; "sleep 30; :"; Path.Combine(tempRoot, probeName); "--socket"; Path.Combine(tempRoot, "probe.sock") ] do
        info.ArgumentList.Add argument
      use probe = Diagnostics.Process.Start info
      try
        try
          let names = Set.ofList [ probeName ]
          match ProviderSessionCache.startupSweep cacheRoot names (Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesStrict ()) with
          | ProviderSessionCache.StartupSweep.SkippedLiveWorkers pids -> pids |> Expect.equal "the live probe is the protecting worker" [ probe.Id ]
          | other -> failtestf "Expected the live probe to protect the cache, got %A" other
          Directory.Exists closedDir |> Expect.isTrue "closed manifest kept while the worker lives"
          probe.Kill true
          do! probe.WaitForExitAsync()
          ProviderSessionCache.startupSweep cacheRoot names (Bozzetto.Server.LinuxProcessEdge.readLinuxProcessesStrict ())
          |> Expect.equal "the exited worker's closed manifest is swept" (ProviderSessionCache.StartupSweep.Pruned 1)
          Directory.Exists closedDir |> Expect.isFalse "closed manifest removed after the worker exits"
        finally
          if not probe.HasExited then probe.Kill true
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    taskCase "closed session evicts immediately on socket disconnect before PID exit and deletes manifest autonomously on ProcessExited (#37)" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "bozzetto-disconnect-race-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      let projPath = Path.Combine(tempRoot, "Race.fidproj")
      Directory.CreateDirectory cacheRoot |> ignore
      File.WriteAllText(projPath, "<Project />")
      let processReaped = completion<unit> ()
      try
        let worker = Worker("race-host", 8104)
        let manifestPath = writeManifest cacheRoot worker 1 "closed"
        worker.CloseLeavesCleanupPending <- true
        worker.DefaultProject <- projPath
        worker.DefaultManifestPath <- manifestPath
        worker.BeforeStop <- fun () -> processReaped.Task :> Task
        let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true, (fun () -> Task.CompletedTask), providerCacheRoot = cacheRoot)
        use _stop = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(task { processReaped.TrySetResult() |> ignore; do! owner.StopAsync() }) }
        let! session = openSession owner worker
        let! closed = execute owner (request Operation.Close worker session)
        success closed |> ignore
        let reqsBeforeDisconnect = worker.Requests.Length
        let mutable changedCount = 0
        let manifestPruned = completion<unit> ()
        use _sub =
          owner.Changed.Subscribe(fun () ->
            Interlocked.Increment(&changedCount) |> ignore
            if not (File.Exists manifestPath) then manifestPruned.TrySetResult() |> ignore)
        // Fault injection: Unix socket drops with Connection reset by peer while OS PID is still unreaped.
        worker.DisconnectTransport "Unable to read data from the transport connection: Connection reset by peer."
        // Evicted IMMEDIATELY on CachedDirectory() without ANY SessionsAsync polling!
        owner.CachedDirectory().Sessions |> Expect.isEmpty "closed session evicted immediately from /work on transport disconnect"
        (Volatile.Read &changedCount > 0) |> Expect.isTrue "Changed fired to push eviction to /work"
        // Manifest deferred until physical process exit so live-PID teardown isn't raced.
        File.Exists manifestPath |> Expect.isTrue "manifest retained until physical OS process exit"
        worker.Requests.Length |> Expect.equal "dead socket is not polled" reqsBeforeDisconnect
        // OS finishes reaping the PID: StopAsync completes and ProcessExited fires autonomously (ZERO SessionsAsync calls!).
        processReaped.TrySetResult() |> ignore
        do! manifestPruned.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        File.Exists manifestPath |> Expect.isFalse "verified group cleanup prunes the manifest without polling"
        worker.Requests.Length |> Expect.equal "still zero dead-socket polling" reqsBeforeDisconnect
      finally
        processReaped.TrySetResult() |> ignore
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    taskCase "surviving descendants keep the closed manifest after StopAsync fails and the root is gone" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-group-survivors-" + Guid.NewGuid().ToString("N"))
      let worker = Worker("group-survivors", 8107)
      let manifest = writeManifest tempRoot worker 1 "closed"
      worker.DefaultManifestPath <- manifest
      worker.CloseLeavesCleanupPending <- true
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true, (fun () -> Task.CompletedTask), providerCacheRoot = tempRoot)
      try
        let! session = openSession owner worker
        let! _ = execute owner (request Operation.Close worker session)
        worker.StopReportsSurvivors <- true
        let! failure = task {
          try
            do! owner.StopAsync()
            return ""
          with :? IOException as error -> return error.Message
        }
        failure |> Expect.stringContains "StopAsync reports the group cleanup failure" "surviving descendants"
        worker.Alive |> Expect.isFalse "only the worker root has exited"
        owner.CachedDirectory().Sessions |> Expect.isEmpty "the closed session evicts immediately from /work"
        let! directory = owner.SessionsAsync CancellationToken.None
        directory.Sessions |> Expect.isEmpty "listing cannot reintroduce the disconnected session"
        File.Exists manifest |> Expect.isTrue "neither root exit nor listing releases the deferred manifest"
        worker.SignalProcessExited()
        File.Exists manifest |> Expect.isFalse "verified process-group cleanup releases the manifest"
        worker.StopReportsSurvivors <- false
        do! owner.StopAsync()
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    taskCase "worker crash between Close reply and follow-up Status still classifies session as closed and evicts cleanly (#37)" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "bozzetto-close-crash-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      try
        let worker = Worker("crash-host", 8105)
        let manifestPath = writeManifest cacheRoot worker 1 "closed"
        worker.CloseLeavesCleanupPending <- true
        worker.DefaultManifestPath <- manifestPath
        let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true, (fun () -> Task.CompletedTask), providerCacheRoot = cacheRoot)
        use _stop = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(owner.StopAsync()) }
        let! session = openSession owner worker
        let killOnNextStatus = ref false
        worker.Intercept <- fun op _ ->
          if op = Operation.Close then
            killOnNextStatus.Value <- true
            None
          elif op = Operation.Status && killOnNextStatus.Value then
            killOnNextStatus.Value <- false
            worker.Die()
            Some(Task.FromException<Reply>(IOException "Unable to read data from the transport connection: Connection reset by peer."))
          else None
        let! _ = execute owner (request Operation.Close worker session)
        owner.CachedDirectory().Sessions |> Expect.isEmpty "closed session evicted with zero Connection reset by peer despite crash before Status"
        File.Exists manifestPath |> Expect.isFalse "closed session manifest pruned after crash between Close and Status"
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    taskCase "a Close acknowledgement racing the worker's exit still evicts the closed session and prunes its manifest" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "bozzetto-close-exit-" + Guid.NewGuid().ToString("N"))
      let cacheRoot = Path.Combine(tempRoot, "provider-sessions")
      try
        for exitKind in [ "process"; "transport" ] do
          let worker = Worker("close-exit-" + exitKind, 8106)
          let manifestPath = writeManifest cacheRoot worker 1 "closed"
          worker.CloseLeavesCleanupPending <- true
          worker.DefaultManifestPath <- manifestPath
          let processReaped = completion<unit> ()
          let pruned = completion<unit> ()
          if exitKind = "transport" then
            worker.BeforeStop <- fun () -> processReaped.Task :> Task
          let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true, (fun () -> Task.CompletedTask), providerCacheRoot = cacheRoot)
          use _sub =
            owner.Changed.Subscribe(fun () ->
              if not (File.Exists manifestPath) then pruned.TrySetResult() |> ignore)
          let! session = openSession owner worker
          // Fault injection: the worker answers Close, then exits before ExecuteCore resumes and
          // checks that the connection is still active.
          worker.Intercept <- fun op current ->
            if op = Operation.Close && current = session then
              let reply = worker.Response(Operation.Close, session)
              if exitKind = "process" then worker.Die()
              else worker.DisconnectTransport "Unable to read data from the transport connection: Connection reset by peer."
              Some(Task.FromResult reply)
            else None
          let! response = execute owner (request Operation.Close worker session)
          response |> refused RefusalCode.Closed
          let sessions = owner.CachedDirectory().Sessions
          sessions |> Array.exists (fun s -> sessionOf s = session)
          |> Expect.isFalse (exitKind + ": the acknowledged close is evicted, never left as a reset diagnostic")
          sessions |> Array.exists (fun s -> s.WorkerError |> Option.exists (fun e -> e.Contains "Connection reset by peer"))
          |> Expect.isFalse (exitKind + ": no transport-reset diagnostic survives")
          if exitKind = "process" then
            File.Exists manifestPath |> Expect.isFalse "process exit: the manifest is pruned at once"
          else
            File.Exists manifestPath |> Expect.isTrue "transport loss: the manifest waits for process exit"
            processReaped.TrySetResult() |> ignore
            do! pruned.Task.WaitAsync(TimeSpan.FromSeconds 5.)
            File.Exists manifestPath |> Expect.isFalse "transport loss: the manifest is pruned on process exit"
          do! owner.StopAsync()
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
    }

    taskCase "calling SessionsAsync repeatedly after a worker crash or silent death fires zero additional Changed notifications and preserves WorkerError" <| fun () -> task {
      // 1. After normal crash teardown (worker = None, open session withdrawn with crash cause).
      let worker = Worker("no-loop-after-crash", 8201)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      use _stop = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(owner.StopAsync()) }
      let! openSess = openSession owner worker
      // Teardown publishes the worker's removal once; count only the polls after it.
      let teardownPublished = completion<unit> ()
      use _teardown = owner.Changed.Subscribe(fun () -> if worker.StopCount > 0 then teardownPublished.TrySetResult() |> ignore)
      worker.Die()
      do! teardownPublished.Task.WaitAsync(TimeSpan.FromSeconds 5.)
      let mutable changedAfterCrash = 0
      use _sub = owner.Changed.Subscribe(fun () -> Interlocked.Increment &changedAfterCrash |> ignore)
      let! first = owner.SessionsAsync CancellationToken.None
      let! second = owner.SessionsAsync CancellationToken.None
      Volatile.Read &changedAfterCrash |> Expect.equal "SessionsAsync after crash teardown fires 0 Changed notifications" 0
      second.Revision |> Expect.equal "revision does not advance across SessionsAsync polls" first.Revision
      (snapshot openSess second.Sessions).WorkerError |> Expect.equal "crash WorkerError stays intact" (Some "worker process exited")

      // 2. While worker is still Some deadConnection (silent death without Exited, retiring = false):
      // first SessionsAsync withdraws it once; subsequent SessionsAsync calls emit 0 Changed and preserve WorkerError.
      let silentWorker = Worker("silent-death", 8202)
      let mutable silentDead = false
      let underlying = silentWorker :> IComposerWorker
      let silentConn =
        { new IComposerWorker with
            member _.Handshake = underlying.Handshake
            member _.ProcessId = underlying.ProcessId
            member _.IsProcessAlive = not silentDead && underlying.IsProcessAlive
            member _.IsAlive = not silentDead && underlying.IsAlive
            member _.Exited = underlying.Exited
            member _.ProcessExited = underlying.ProcessExited
            member _.InputsRevoked = underlying.InputsRevoked
            member _.RequestAsync(body, cancellation) = underlying.RequestAsync(body, cancellation)
            member _.StopAsync() = underlying.StopAsync() }
      let owner2 = ComposerSupervisor((fun () -> Task.FromResult silentConn), true)
      use _stop2 = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(owner2.StopAsync()) }
      let! s2 = openSession owner2 silentWorker
      let mutable changes2 = 0
      use _sub2 = owner2.Changed.Subscribe(fun () -> Interlocked.Increment &changes2 |> ignore)
      silentDead <- true
      let! dir1 = owner2.SessionsAsync CancellationToken.None
      Volatile.Read &changes2 |> Expect.equal "first SessionsAsync after silent death withdraws and fires Changed once" 1
      let expectedErr = (snapshot s2 dir1.Sessions).WorkerError
      expectedErr |> Expect.equal "first withdrawal records disconnect reason" (Some "The Composer worker disconnected.")
      Interlocked.Exchange(&changes2, 0) |> ignore
      let! dir2 = owner2.SessionsAsync CancellationToken.None
      let! dir3 = owner2.SessionsAsync CancellationToken.None
      Volatile.Read &changes2 |> Expect.equal "repeated SessionsAsync calls on dead worker fire 0 additional Changed events" 0
      dir2.Revision |> Expect.equal "second poll leaves revision unchanged" dir1.Revision
      dir3.Revision |> Expect.equal "third poll leaves revision unchanged" dir1.Revision
      (snapshot s2 dir3.Sessions).WorkerError |> Expect.equal "existing WorkerError is preserved" expectedErr

      // 3. Specific WorkerError (e.g. failed stop / specific exit reason) is NOT overwritten by SessionsAsync.
      let customWorker = Worker("preserve-error", 8203)
      let owner3 = ComposerSupervisor((fun () -> Task.FromResult(customWorker :> IComposerWorker)), true)
      use _stop3 = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(owner3.StopAsync()) }
      let! s3 = openSession owner3 customWorker
      let specificReason = "Specific worker crash diagnostic: exit code 139."
      let workerCleared = completion<unit> ()
      let mutable changes3 = 0
      use _sub3 =
        owner3.Changed.Subscribe(fun () ->
          Interlocked.Increment &changes3 |> ignore
          if owner3.WorkerPid.IsNone then workerCleared.TrySetResult() |> ignore)
      customWorker.DisconnectTransport specificReason
      do! workerCleared.Task.WaitAsync(TimeSpan.FromSeconds 5.)
      Interlocked.Exchange(&changes3, 0) |> ignore
      let! _ = owner3.SessionsAsync CancellationToken.None
      let! poll2 = owner3.SessionsAsync CancellationToken.None
      Volatile.Read &changes3 |> Expect.equal "polls after specific exit emit 0 Changed events" 0
      (snapshot s3 poll2.Sessions).WorkerError |> Expect.equal "specific WorkerError is never overwritten" (Some specificReason)
    }

    taskCase "a zero-session worker crash publishes Changed once when teardown clears the worker, and later polls publish nothing" <| fun () -> task {
      let worker = Worker("zero-session-crash", 8204)
      let owner = ComposerSupervisor((fun () -> Task.FromResult(worker :> IComposerWorker)), true)
      use _stop = { new IAsyncDisposable with member _.DisposeAsync() = ValueTask(owner.StopAsync()) }
      let! session = openSession owner worker
      let! _ = execute owner (request Operation.Close worker session)
      owner.CachedDirectory().Sessions |> Expect.isEmpty "the finished close leaves the worker owning zero sessions"
      owner.WorkerPid |> Expect.equal "the worker is live before the crash" (Some 8204)
      let before = owner.CachedDirectory().Revision
      let mutable changes = 0
      let teardownPublished = completion<unit> ()
      use _sub =
        owner.Changed.Subscribe(fun () ->
          Interlocked.Increment &changes |> ignore
          if worker.StopCount > 0 then teardownPublished.TrySetResult() |> ignore)
      // The PID is gone before teardown starts: nothing to withdraw, reconcile or prune.
      worker.Die()
      do! teardownPublished.Task.WaitAsync(TimeSpan.FromSeconds 5.)
      worker.StopCount |> Expect.equal "teardown stopped the crashed worker once" 1
      Volatile.Read &changes |> Expect.equal "clearing the worker publishes exactly one Changed" 1
      let cleared = owner.CachedDirectory()
      cleared.Worker |> Expect.isNone "the published directory no longer shows the dead worker"
      (cleared.Revision > before) |> Expect.isTrue "the removal advanced the published revision"
      let! first = owner.SessionsAsync CancellationToken.None
      let! second = owner.SessionsAsync CancellationToken.None
      Volatile.Read &changes |> Expect.equal "repeated SessionsAsync after the crash publishes 0 additional Changed" 1
      second.Revision |> Expect.equal "polls leave the revision unchanged" first.Revision
      first.Revision |> Expect.equal "the first poll adds nothing to the removal" cleared.Revision
    }

    testCase "tryPruneManifestPath accepts differently-spelled symlinked ancestor prefixes between cacheRoot and manifestPath" <| fun () ->
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-cache-mismatch-prefix-" + Guid.NewGuid().ToString("N"))
      let physicalBase = Path.Combine(tempRoot, "physical")
      let linkedBase = Path.Combine(tempRoot, "linked")
      let outsideDir = Path.Combine(tempRoot, "outside")
      let physicalCache = Path.Combine(physicalBase, "provider-sessions")
      let linkedCache = Path.Combine(linkedBase, "provider-sessions")
      Directory.CreateDirectory physicalCache |> ignore
      Directory.CreateDirectory outsideDir |> ignore
      Directory.CreateSymbolicLink(linkedBase, physicalBase) |> ignore
      try
        let owner: WorkerAddress = { Host = "h1"; Epoch = "e1"; Provider = ProviderIdentity.ClefComposer }
        let placeManifest root =
          let dir = Path.Combine(root, "h1", "s1", "e1")
          Directory.CreateDirectory dir |> ignore
          let m = Path.Combine(dir, "current.json")
          File.WriteAllText(m, """{"status":"closed"}""")
          m
        // Case A: cacheRoot uses linked prefix, manifestPath uses physical prefix.
        let physManifest = placeManifest physicalCache
        ProviderSessionCache.tryPruneManifestPath (Some linkedCache) owner "s1" physManifest
        |> Expect.equal "linked cacheRoot + physical manifestPath resolves and prunes" ProviderSessionCache.PruneOutcome.Pruned
        File.Exists physManifest |> Expect.isFalse "manifest pruned under Case A"
        // Case B: cacheRoot uses physical prefix, manifestPath uses linked prefix.
        let _ = placeManifest physicalCache
        let linkedManifest = Path.Combine(linkedCache, "h1", "s1", "e1", "current.json")
        ProviderSessionCache.tryPruneManifestPath (Some physicalCache) owner "s1" linkedManifest
        |> Expect.equal "physical cacheRoot + linked manifestPath resolves and prunes" ProviderSessionCache.PruneOutcome.Pruned
        File.Exists linkedManifest |> Expect.isFalse "manifest pruned under Case B"
        // Negative control: symlink below physical root still fails with LinkInPath even when ancestor prefix differs.
        let linkHostOwner: WorkerAddress = { owner with Host = "h-out" }
        Directory.CreateSymbolicLink(Path.Combine(physicalCache, "h-out"), outsideDir) |> ignore
        Directory.CreateDirectory(Path.Combine(outsideDir, "s1", "e1")) |> ignore
        File.WriteAllText(Path.Combine(outsideDir, "s1", "e1", "current.json"), """{"status":"closed"}""")
        ProviderSessionCache.tryPruneManifestPath (Some physicalCache) linkHostOwner "s1" (Path.Combine(linkedCache, "h-out", "s1", "e1", "current.json"))
        |> Expect.equal "symlink below physical root is still refused with LinkInPath" (ProviderSessionCache.PruneOutcome.LinkInPath(Path.Combine(physicalCache, "h-out")))
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)

    testCase "tryPruneManifestPath refuses an alias back to the physical root from below it" <| fun () ->
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-cache-root-alias-" + Guid.NewGuid().ToString("N"))
      let physicalRoot = Path.Combine(tempRoot, "provider-sessions")
      let epochDir = Path.Combine(physicalRoot, "h1", "s1", "e1")
      Directory.CreateDirectory epochDir |> ignore
      try
        let owner: WorkerAddress = { Host = "h1"; Epoch = "e1"; Provider = ProviderIdentity.ClefComposer }
        let manifest = Path.Combine(epochDir, "current.json")
        let manifestText = """{"status":"closed"}"""
        File.WriteAllText(manifest, manifestText)
        let alias = Path.Combine(physicalRoot, "alias")
        Directory.CreateSymbolicLink(alias, physicalRoot) |> ignore
        let aliasManifest = Path.Combine(alias, "h1", "s1", "e1", "current.json")
        match ProviderSessionCache.tryPruneManifestPath (Some physicalRoot) owner "s1" aliasManifest with
        | ProviderSessionCache.PruneOutcome.LinkInPath path ->
          Path.GetFileName path |> Expect.equal "the in-root alias is reported" (Path.GetFileName alias)
        | outcome -> failtestf "Expected LinkInPath for the in-root alias, got %O" outcome
        Directory.Exists epochDir |> Expect.isTrue "the epoch directory is untouched"
        File.ReadAllText manifest |> Expect.equal "the epoch manifest is untouched" manifestText

        let linkedRoot = Path.Combine(tempRoot, "linked-root")
        Directory.CreateSymbolicLink(linkedRoot, physicalRoot) |> ignore
        ProviderSessionCache.tryPruneManifestPath (Some physicalRoot) owner "s1" (Path.Combine(linkedRoot, "h1", "s1", "e1", "current.json"))
        |> Expect.equal "a link outside the root may resolve directly to it" ProviderSessionCache.PruneOutcome.Pruned
      finally
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)
  ]
