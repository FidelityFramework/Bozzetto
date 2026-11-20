namespace Bozzetto.ComposerIntegration

open System
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Bozzetto.Providers
open Bozzetto.Composer.Protocol

/// Projection metadata is local observation state, never compiler authority.
type ComposerResponse = {
  Reply: Reply
  StatusFresh: bool
  StatusError: string option
  WorkerAvailable: bool
  WorkerError: string option
}

type ComposerDirectory = {
  Configured: bool
  Revision: int64
  Worker: Reply option
  Sessions: ComposerResponse array
}

type private StatusObservation = { Activity: int64; Active: int; Last: uint64 option }

module ComposerState =
  let response reply =
    { Reply = reply; StatusFresh = true; StatusError = None; WorkerAvailable = true; WorkerError = None }
  let failure authority code message =
    response { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = Guid.NewGuid().ToString("N")
               Authority = authority; Outcome = Result.Error { Code = code; Message = message } }
  let projection fresh reason snapshot =
    let reply =
      match snapshot.Reply.Outcome with
      | Result.Ok(Observed status) when not fresh -> { snapshot.Reply with Outcome = Result.Ok(Observed { status with Current = None }) }
      | _ -> snapshot.Reply
    { snapshot with Reply = reply; StatusFresh = fresh; StatusError = if fresh then None else Some reason }
  let terminal reason snapshot =
    let reply =
      match snapshot.Reply.Outcome with
      | Result.Ok(Observed status) ->
        { snapshot.Reply with Outcome = Result.Ok(Observed { status with Closed = true; Busy = false; Current = None }) }
      | _ -> snapshot.Reply
    { snapshot with Reply = reply; WorkerAvailable = false; WorkerError = Some reason; StatusFresh = false }
  /// A session the worker reports closed with its cleanup finished has nothing
  /// further to observe: it leaves the directory instead of standing in it as
  /// a card that can never be fresh again. A session withdrawn by a worker's
  /// retirement is not finished in this sense: it carries the reason.
  let finished snapshot =
    match snapshot.Reply.Outcome with
    | Result.Ok(Closed item) -> item.Closed && not item.CleanupPending && item.CleanupError.IsNone
    | Result.Ok(Observed status) ->
      status.Closed && not status.Busy && not status.CleanupPending && not status.FormatterCleanupPending && status.CleanupError.IsNone
      && status.WorkerRetirementRequired.IsNone && snapshot.WorkerError.IsNone
    | _ -> false
  let needsReconciliation snapshot =
    match snapshot.Reply.Outcome with
    | Result.Ok(Observed status) ->
      not snapshot.StatusFresh || status.Busy || status.RevocationPending || status.FormatterCleanupPending
      || status.WorkerRetirementRequired.IsSome || status.CleanupPending
    | _ -> false
  let wasClosed snapshot =
    match snapshot.Reply.Outcome with
    | Result.Ok(Closed _) -> true
    | Result.Ok(Observed status) -> status.Closed
    | _ -> false
  let projectAndManifest snapshot =
    match snapshot.Reply.Outcome with
    | Result.Ok(Observed status) -> Some(status.Project, status.ManifestPath)
    | _ -> None
  let markWorkerRetirement reason snapshot =
    match snapshot.Reply.Outcome with
    | Result.Ok(Observed status) when status.WorkerRetirementRequired.IsNone ->
      { snapshot with Reply = { snapshot.Reply with Outcome = Result.Ok(Observed { status with WorkerRetirementRequired = Some reason }) } }
    | _ -> snapshot
  let sameState left right =
    let normalize value =
      match value.Reply.Outcome with
      | Result.Ok(Observed status) -> { value with Reply = { value.Reply with RequestId = ""; Outcome = Result.Ok(Observed { status with Observation = 0UL }) } }
      | _ -> { value with Reply = { value.Reply with RequestId = "" } }
    normalize left = normalize right

// Immutable owner-published liveness snapshot admitted into UiBridge Facts.Graph.
type LeasedLivenessSnapshot = {
  Serial: int64
  VerifiedAtMs: Map<string, int64>
}

module LeasedLivenessSnapshot =
  let empty : LeasedLivenessSnapshot = { Serial = 0L; VerifiedAtMs = Map.empty }

/// Shared typed compiler owner. JSON exists only at external client adapters.
type ComposerSupervisor internal (
    factory: unit -> Task<IComposerWorker>,
    configured: bool,
    beforeMonitorExit: unit -> Task,
    ?monitorWindow: TimeSpan,
    ?journalStore: Result<ComposerJournal.Store, string>,
    ?cleanupRetirementThreshold: TimeSpan,
    ?utcNow: (unit -> DateTimeOffset),
    ?providerCacheRoot: string) =
  let gate = obj ()
  let pruneGate = obj ()
  let lifecycle = new SemaphoreSlim(1, 1)
  let lifetime = new CancellationTokenSource()
  let monitorWindow = defaultArg monitorWindow (TimeSpan.FromMinutes 8.)
  let cleanupRetirementThreshold = defaultArg cleanupRetirementThreshold (TimeSpan.FromSeconds 30.)
  let utcNow = defaultArg utcNow (fun () -> DateTimeOffset.UtcNow)
  let changedEvent = Event<unit>()
  let snapshots = Dictionary<string, ComposerResponse>()
  let mutable openOrder : Map<string, int64> = Map.empty
  let mutable nextOpenOrder = 0L
  let observations = Dictionary<string, StatusObservation>()
  let cleanupPendingSince = Dictionary<string, DateTimeOffset>()
  let mutable inFlightOpens = 0
  let preClosedSessions = HashSet<string>()
  let diskBackedProjects = HashSet<string>()
  let verifiedEpochsWithSessions = HashSet<WorkerAddress>()
  let pendingManifestPrunes = Dictionary<WorkerAddress, ResizeArray<WorkerAddress * string * string>>()
  /// Sessions forgotten as finished, with the generation they were last seen
  /// at: a reply delayed past the close cannot bring one back. Cleared when the
  /// worker retires, since its session identities never recur.
  let forgotten = Dictionary<string, int64>()
  let monitors = Dictionary<string * string * string, TaskCompletionSource<unit>>()
  let mutable worker: IComposerWorker option = None
  let mutable retiring = false
  let mutable retirement: Task option = None
  let mutable stopped = false
  let mutable revision = 0L
  /// The journal of settled operations, this supervisor's own; published whole.
  let mutable journal =
    let empty = ComposerJournal.empty (Guid.NewGuid().ToString("N"))
    match journalStore |> Option.map (Result.bind (fun store -> store.Read())) with
    | None -> empty
    | Some(Ok stored) -> stored
    | Some(Error reason) -> { empty with StorageError = Some reason }
  let mutable lastLivenessVerifiedAtMs: Map<string, int64> = Map.empty
  let mutable firstAbsentObservedAtMs: Map<string, int64> = Map.empty
  let mutable livenessSerial = 0L
  let journalChanged = Event<unit>()
  let livenessChanged = Event<LeasedLivenessSnapshot>()

  let changed () =
    lock gate (fun () -> revision <- revision + 1L)
    try changedEvent.Trigger() with _ -> ()
  let isOwner connection = worker |> Option.exists (fun current -> obj.ReferenceEquals(current, connection))
  let active (connection: IComposerWorker) = isOwner connection && connection.IsAlive && not stopped && not retiring
  let address (connection: IComposerWorker) session = { Worker = ComposerWire.workerAddress connection.Handshake.Authority; Session = session }
  let observation session =
    match observations.TryGetValue session with
    | true, value -> value
    | _ -> { Activity = 0L; Active = 0; Last = None }
  let cached session = lock gate (fun () -> match snapshots.TryGetValue session with true, snapshot -> Some snapshot | _ -> None)
  let directory () =
    lock gate (fun () ->
      { Configured = configured; Revision = revision; Worker = worker |> Option.filter _.IsProcessAlive |> Option.map _.Handshake
        Sessions = snapshots.Values |> Seq.toArray })
  let unknownAuthority request =
    let target = ComposerWire.target request |> Option.defaultValue { Host = ""; Epoch = ""; Provider = ProviderIdentity.ClefComposer }
    { Host = target.Host; Epoch = target.Epoch; Provider = target.Provider; Session = ComposerWire.session request; Generation = 0L }
  let refusal request code message =
    let authority = cached (ComposerWire.session request) |> Option.map _.Reply.Authority |> Option.defaultValue (unknownAuthority request)
    ComposerState.failure authority code message

  // Every prune names its owner: the snapshot's worker host and epoch, and its session (#37).
  let manifestTarget (snap: ComposerResponse) : (WorkerAddress * string * string) option =
    match ComposerState.projectAndManifest snap with
    | Some(_, manifest) when not (String.IsNullOrWhiteSpace manifest) ->
      Some(ComposerWire.workerAddress snap.Reply.Authority, snap.Reply.Authority.Session, manifest)
    | _ -> None

  let pruneManifests (targets: (WorkerAddress * string * string) list) =
    // Serialize filesystem pruning (even for empty target lists in reconcileExitedEpoch) so a
    // concurrent ProcessExited/teardown call cannot return while another thread is mid-unlink.
    lock pruneGate (fun () ->
      for owner, session, manifest in targets do
        match ProviderSessionCache.tryPruneManifestPath providerCacheRoot owner session manifest with
        | ProviderSessionCache.PruneOutcome.Pruned
        | ProviderSessionCache.PruneOutcome.Missing
        | ProviderSessionCache.PruneOutcome.NoCacheRoot -> ()
        | refused -> Bozzetto.Utils.Log.warn "[provider-cache] kept %s for %s: %A" manifest session refused)

  // Probe disk existence of tracked project files strictly outside lock gate.
  let probeDeletedDiskProjects () : Set<string> =
    let candidates =
      lock gate (fun () ->
        [ for session in diskBackedProjects do
            match snapshots.TryGetValue session with
            | true, snap ->
              match ComposerState.projectAndManifest snap with
              | Some(proj, _) when not (String.IsNullOrWhiteSpace proj) -> yield session, proj
              | _ -> ()
            | _ -> () ])
    candidates
    |> List.choose (fun (session, proj) -> if not (File.Exists proj) then Some session else None)
    |> Set.ofList

  let projectExistsOnDisk (snapshot: ComposerResponse) : bool =
    match snapshot.Reply.Outcome with
    | Result.Ok(Observed status) when not status.Closed && not (String.IsNullOrWhiteSpace status.Project) ->
      File.Exists status.Project
    | _ -> false

  let evictSessionLocked (session: string) =
    snapshots.Remove session |> ignore
    openOrder <- Map.remove session openOrder
    observations.Remove session |> ignore
    cleanupPendingSince.Remove session |> ignore
    preClosedSessions.Remove session |> ignore
    diskBackedProjects.Remove session |> ignore

  let queueDeferredManifestLocked (owner: WorkerAddress) (target: WorkerAddress * string * string) =
    match pendingManifestPrunes.TryGetValue owner with
    | true, list -> list.Add target
    | _ ->
      let list = ResizeArray<WorkerAddress * string * string>()
      list.Add target
      pendingManifestPrunes[owner] <- list

  let drainDeferredManifestsLocked (owner: WorkerAddress) (target: ResizeArray<WorkerAddress * string * string>) =
    match pendingManifestPrunes.TryGetValue owner with
    | true, list ->
      target.AddRange list
      pendingManifestPrunes.Remove owner |> ignore
    | _ -> ()

  let isAlreadyClosedLocked (session: string) (snap: ComposerResponse) =
    preClosedSessions.Contains session || (snap.WorkerError.IsNone && ComposerState.wasClosed snap)

  let ownedSessionsLocked (owner: WorkerAddress) =
    [ for KeyValue(session, snap) in snapshots do
        if ComposerWire.workerAddress snap.Reply.Authority = owner then yield session, snap ]

  let withdrawOwnedWith (overwriteExistingError: bool) (connection: IComposerWorker) reason : bool =
    let owner = ComposerWire.workerAddress connection.Handshake.Authority
    let deletedProjects = probeDeletedDiskProjects ()
    let unavailable = not connection.IsAlive
    let manifestsToPrune = ResizeArray<WorkerAddress * string * string>()
    let mutated =
      lock gate (fun () ->
        let verified = verifiedEpochsWithSessions.Contains owner
        let mutable changedAny = false
        for session, snap in ownedSessionsLocked owner do
          let alreadyClosed = isAlreadyClosedLocked session snap
          if unavailable && (alreadyClosed || Set.contains session deletedProjects) then
            manifestTarget snap
            |> Option.iter (fun target ->
              if verified then manifestsToPrune.Add target
              else queueDeferredManifestLocked owner target)
            evictSessionLocked session
            changedAny <- true
          else
            if alreadyClosed && preClosedSessions.Add session then changedAny <- true
            cleanupPendingSince.Remove session |> ignore
            let effectiveReason =
              if overwriteExistingError then reason
              else snap.WorkerError |> Option.defaultValue reason
            let nextSnap = ComposerState.terminal effectiveReason snap
            if nextSnap <> snap then
              snapshots[session] <- nextSnap
              changedAny <- true
        if (ownedSessionsLocked owner).IsEmpty then verifiedEpochsWithSessions.Remove owner |> ignore
        if isOwner connection then forgotten.Clear()
        changedAny)
    pruneManifests (Seq.toList manifestsToPrune)
    if mutated then changed ()
    mutated

  let withdrawOwned connection reason =
    withdrawOwnedWith false connection reason

  let reconcileExitedEpoch (connection: IComposerWorker) =
    let owner = ComposerWire.workerAddress connection.Handshake.Authority
    let deletedProjects = probeDeletedDiskProjects ()
    let manifestsToPrune = ResizeArray<WorkerAddress * string * string>()
    let removedAny =
      lock gate (fun () ->
        drainDeferredManifestsLocked owner manifestsToPrune
        let owned = ownedSessionsLocked owner
        let evicted =
          [ for session, snap in owned do
              if isAlreadyClosedLocked session snap || Set.contains session deletedProjects then
                manifestTarget snap |> Option.iter manifestsToPrune.Add
                yield session ]
        for session in evicted do evictSessionLocked session
        if owned.Length > evicted.Length then verifiedEpochsWithSessions.Add owner |> ignore
        not evicted.IsEmpty)
    pruneManifests (Seq.toList manifestsToPrune)
    if removedAny || manifestsToPrune.Count > 0 then changed ()

  // Must be called while holding lock gate; returns (accepted, manifestToPrune) so
  // callers perform all filesystem pruning strictly outside lock gate.
  let rememberLocked (connection: IComposerWorker) (snapshot: ComposerResponse) (projectOnDisk: bool) : bool * (WorkerAddress * string * string) option =
    let identity = snapshot.Reply.Authority
    let currentEnough = cached identity.Session |> Option.forall (fun previous -> identity.Generation >= previous.Reply.Authority.Generation)
    let notForgotten =
      match forgotten.TryGetValue identity.Session with
      | true, generation -> identity.Generation > generation
      | _ -> true
    if Result.isOk snapshot.Reply.Outcome && active connection && identity.Session <> "" && currentEnough && notForgotten
       && ComposerWire.workerAddress identity = ComposerWire.workerAddress connection.Handshake.Authority then
      if ComposerState.finished snapshot then
        let manifest = manifestTarget snapshot
        evictSessionLocked identity.Session
        forgotten[identity.Session] <- identity.Generation
        true, manifest
      else
        match snapshot.Reply.Outcome with
        | Result.Ok(Observed status) ->
          if projectOnDisk then
            diskBackedProjects.Add identity.Session |> ignore
          if status.Closed then
            preClosedSessions.Add identity.Session |> ignore
        | _ -> ()
        snapshots[identity.Session] <- snapshot
        if not (openOrder.ContainsKey identity.Session) then
          nextOpenOrder <- nextOpenOrder + 1L
          openOrder <- Map.add identity.Session nextOpenOrder openOrder
        true, None
    else false, None

  let remember (connection: IComposerWorker) snapshot =
    let projectOnDisk = projectExistsOnDisk snapshot
    let accepted, manifestToPrune = lock gate (fun () -> rememberLocked connection snapshot projectOnDisk)
    manifestToPrune |> Option.iter (fun m -> pruneManifests [ m ])
    accepted

  // A worker's Close acknowledgement is recorded before the caller checks that the worker is
  // still active, so an exit racing the reply still finds the session closed (#37).
  let recordCloseAcknowledgement (connection: IComposerWorker) (response: ComposerResponse) (closed: CloseResult) =
    let session = response.Reply.Authority.Session
    let owner = ComposerWire.workerAddress connection.Handshake.Authority
    let merged =
      lock gate (fun () ->
        match snapshots.TryGetValue session with
        | true, previous when ComposerWire.workerAddress previous.Reply.Authority = owner
                              && ComposerWire.workerAddress response.Reply.Authority = owner
                              && response.Reply.Authority.Generation >= previous.Reply.Authority.Generation ->
          match previous.Reply.Outcome with
          | Result.Ok(Observed status) ->
            let updated =
              { status with
                  Observation = max status.Observation closed.Observation
                  Closed = closed.Closed
                  Current = None
                  CleanupPending = closed.CleanupPending
                  CleanupError = closed.CleanupError }
            let snapshot =
              { previous with Reply = { response.Reply with Outcome = Result.Ok(Observed updated) } }
              |> ComposerState.projection false "Close acknowledged; status still needs observation."
            if closed.Closed then preClosedSessions.Add session |> ignore
            if not (active connection) then snapshots[session] <- snapshot
            Some snapshot
          | _ -> None
        | _ -> None)
    merged |> Option.iter (remember connection >> ignore)
  let uncertain connection session reason =
    lock gate (fun () ->
      match cached session with
      | Some previous when active connection -> snapshots[session] <- ComposerState.projection false reason previous
      | _ -> ())
  let activity connection session delta =
    if session <> "" then lock gate (fun () ->
      let previous = observation session
      observations[session] <- { previous with Activity = previous.Activity + 1L; Active = previous.Active + delta }
      uncertain connection session "Session activity requires a new status observation.")
  let stale response =
    let identity = response.Reply.Authority
    match cached identity.Session with
    | Some previous when identity.Generation < previous.Reply.Authority.Generation ->
      ComposerState.failure previous.Reply.Authority RefusalCode.Superseded "A newer session generation has already been observed."
    | _ -> response

  // Fence unprompted worker exits under lock gate while executing StopAsync/teardown
  // strictly outside the lock and joined by StopAsync via retirement.
  let fenceUnpromptedExitLocked (connection: IComposerWorker) =
    if isOwner connection && not stopped && not retiring then
      retiring <- true
      let tcs = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      retirement <- Some(tcs.Task :> Task)
      Some tcs
    else None

  let launchExitedTeardown (connection: IComposerWorker) (reason: string) (tcs: TaskCompletionSource<unit>) =
    let _ = Task.Run<unit>(fun () -> task {
      let mutable exited = false
      let mutable clearedWorker = false
      try
        try
          do! lifecycle.WaitAsync()
          try
            let shouldStop = lock gate (fun () -> isOwner connection && not stopped)
            if shouldStop then
              do! connection.StopAsync()
              if connection.IsProcessAlive then invalidOp "The exited Composer process did not exit."
              exited <- true
              withdrawOwned connection reason |> ignore
              // Publish the worker's removal once, even with nothing to withdraw or prune.
              lock gate (fun () ->
                if isOwner connection then
                  worker <- None
                  clearedWorker <- true)
            else
              exited <- true
          finally
            if exited then lock gate (fun () -> if not stopped then retiring <- false)
            lifecycle.Release() |> ignore
          if clearedWorker then changed ()
        with error ->
          if not (lock gate (fun () -> stopped)) then
            withdrawOwnedWith true connection (reason + " Worker teardown failed: " + error.Message) |> ignore
      finally
        tcs.TrySetResult() |> ignore
    })
    ()

  let ensureWorker cancellation = task {
    do! lifecycle.WaitAsync(cancellation: CancellationToken)
    try
      let existing, denied = lock gate (fun () -> worker, stopped || retiring)
      if denied then return Result.Error "The Composer owner is retiring or has stopped."
      elif not configured then return Result.Error "Set BOZZETTO_COMPOSER_WORKER to an absolute built worker path."
      else
        match existing with
        | Some connection when connection.IsAlive -> return Result.Ok connection
        | _ ->
          match existing with
          | Some connection ->
            do! connection.StopAsync()
            if connection.IsProcessAlive then invalidOp "The unavailable Composer process did not exit."
          | None -> ()
          let! connection = factory ()
          let accepted = lock gate (fun () ->
            if stopped || retiring || not connection.IsAlive then false
            else
              worker <- Some connection
              true)
          if not accepted then
            do! connection.StopAsync()
            return Result.Error "The Composer owner retired while its worker was opening."
          else
            connection.Exited.Add(fun reason ->
              let tcsOpt, isCurrentOwner = lock gate (fun () ->
                let ownerMatch = isOwner connection
                let tcs = if ownerMatch then fenceUnpromptedExitLocked connection else None
                tcs, ownerMatch)
              if isCurrentOwner then
                withdrawOwned connection reason |> ignore
              tcsOpt |> Option.iter (launchExitedTeardown connection reason))
            connection.ProcessExited.Add(fun () ->
              reconcileExitedEpoch connection)
            connection.InputsRevoked.Add(fun notification ->
              let projectOnDisk =
                match notification.Outcome with
                | Result.Ok(InputsRevoked status) when not status.Closed && not (String.IsNullOrWhiteSpace status.Project) ->
                  File.Exists status.Project
                | _ -> false
              let accepted, manifestToPrune = lock gate (fun () ->
                match notification.Outcome, cached notification.Authority.Session with
                | Result.Ok(InputsRevoked status), Some _
                    when notification.RequestId = "" && status.Current.IsNone
                         && notification.ProtocolVersion = BAREWireCodec.ProtocolVersion
                         && ComposerWire.workerAddress notification.Authority = ComposerWire.workerAddress connection.Handshake.Authority
                         && ((observation notification.Authority.Session).Last |> Option.forall (fun previous -> status.Observation > previous)) ->
                  let projected = { notification with Outcome = Result.Ok(Observed status) } |> ComposerState.response
                  let ok, manifest = rememberLocked connection projected projectOnDisk
                  if ok then
                    let prior = observation notification.Authority.Session
                    observations[notification.Authority.Session] <- { prior with Last = Some status.Observation }
                    true, manifest
                  else false, None
                | _ -> false, None)
              manifestToPrune |> Option.iter (fun m -> pruneManifests [ m ])
              if accepted then changed ())
            if not connection.IsAlive then
              let tcsOpt = lock gate (fun () -> fenceUnpromptedExitLocked connection)
              withdrawOwned connection "Worker exited during startup." |> ignore
              tcsOpt |> Option.iter (launchExitedTeardown connection "Worker exited during startup.")
            changed ()
            return Result.Ok connection
    finally lifecycle.Release() |> ignore
  }

  let retireOwnedAdmitted request (connection: IComposerWorker) reason prepare = task {
    withdrawOwned connection reason |> ignore
    do! lifecycle.WaitAsync()
    let mutable exited = false
    try
      let! result = task {
        if prepare then
          try
            let! reply = connection.RequestAsync(PrepareCompilerChange(ComposerWire.workerAddress connection.Handshake.Authority), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds 10.)
            return ComposerState.response reply
          with error -> return refusal request RefusalCode.RetirementFailed error.Message
        else
          return refusal request RefusalCode.ProviderRetiring reason
      }
      do! connection.StopAsync()
      if connection.IsProcessAlive then invalidOp "The retired Composer process did not exit."
      exited <- true
      withdrawOwned connection reason |> ignore
      lock gate (fun () -> if isOwner connection then worker <- None)
      return result
    finally
      if exited then lock gate (fun () -> if not stopped then retiring <- false)
      lifecycle.Release() |> ignore
      changed ()
  }

  let retireOwned request (connection: IComposerWorker) reason prepare = task {
    let admitted = lock gate (fun () ->
      if stopped || retiring || not (isOwner connection) then false
      else
        retiring <- true
        true)
    if not admitted then return refusal request RefusalCode.ProviderRetiring "The Composer worker is already retiring."
    else return! retireOwnedAdmitted request connection reason prepare
  }

  let retire request connection = retireOwned request connection "Compiler retirement requested." true

  // Fencing (retiring <- true + retirement Task slot) happens synchronously under lock gate;
  // callers hold the gate. Withdrawal, manifest pruning and Changed notifications run in
  // launchRetirement, strictly after the gate is released, to prevent lock inversion.
  let fenceRetirementLocked (connection: IComposerWorker) =
    if active connection then
      retiring <- true
      let tcs = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      retirement <- Some(tcs.Task :> Task)
      Some tcs
    else None

  let launchRetirement (connection: IComposerWorker) reason (tcs: TaskCompletionSource<unit>) =
    let request = PrepareCompilerChange(ComposerWire.workerAddress connection.Handshake.Authority)
    let _ = task {
      try
        try
          let! _ = retireOwnedAdmitted request connection reason false
          ()
        with error ->
          withdrawOwnedWith true connection (reason + " Worker retirement failed: " + error.Message) |> ignore
      finally
        tcs.TrySetResult() |> ignore
    }
    ()

  let escalateRetirement (connection: IComposerWorker) reason =
    lock gate (fun () -> fenceRetirementLocked connection)
    |> Option.iter (launchRetirement connection reason)

  // Autonomous worker retirement for closed sessions left in cleanupPending (#33). The
  // supervisor's own threshold retires a worker only when every session it owns is closed, so
  // closing one session never withdraws a sibling's open or building session. A session whose
  // post-drain disposal hangs is retired through the worker's sticky WorkerRetirementRequired
  // (ProviderSession), which refresh honors whatever the siblings are doing.
  let checkCleanupRetirement (connection: IComposerWorker) =
    let closedIdle snapshot =
      match snapshot.Reply.Outcome with
      | Result.Ok(Observed status) -> snapshot.StatusFresh && status.Closed && not status.Busy
      | _ -> false
    let closedPending snapshot =
      match snapshot.Reply.Outcome with
      | Result.Ok(Observed status) -> closedIdle snapshot && status.CleanupPending
      | _ -> false
    let fenced =
      lock gate (fun () ->
        if not (active connection) then None
        else
          let now = utcNow ()
          let owned = ownedSessionsLocked (ComposerWire.workerAddress connection.Handshake.Authority)
          // One explicit pass owns this worker's cleanup clocks: start new ones, drop settled ones.
          for session, snapshot in owned do
            if not (closedPending snapshot) then cleanupPendingSince.Remove session |> ignore
            elif not (cleanupPendingSince.ContainsKey session) then cleanupPendingSince[session] <- now
          let expired =
            owned |> List.exists (fun (session, snapshot) ->
              closedPending snapshot && now - cleanupPendingSince[session] >= cleanupRetirementThreshold)
          let allClosedIdle = not owned.IsEmpty && owned |> List.forall (snd >> closedIdle)
          if inFlightOpens <> 0 || not (expired && allClosedIdle) then None
          else
            let reason = "All Composer sessions are closed and cleanupPending exceeded threshold; retiring compiler worker to reclaim resident memory."
            fenceRetirementLocked connection
            |> Option.map (fun tcs ->
              for session, snapshot in owned do
                if closedPending snapshot then snapshots[session] <- ComposerState.markWorkerRetirement reason snapshot
              reason, tcs))
    match fenced with
    | Some(reason, tcs) ->
      changed ()
      launchRetirement connection reason tcs
    | None -> ()

  let refresh (connection: IComposerWorker) session cancellation = task {
    let started = lock gate (fun () -> observation session)
    let applicable () = active connection && started.Activity = (observation session).Activity
    let failed reason = lock gate (fun () ->
      let current = observation session
      if applicable () && current.Last = started.Last then
        uncertain connection session reason
        observations[session] <- { current with Activity = current.Activity + 1L })
    try
      let! reply = connection.RequestAsync(Status(address connection session), cancellation)
      match reply.Outcome with
      | Result.Ok(Observed status) ->
        let projectOnDisk = not status.Closed && not (String.IsNullOrWhiteSpace status.Project) && File.Exists status.Project
        let observed, manifestToPrune = lock gate (fun () ->
          let current = observation session
          if applicable () && (current.Last |> Option.forall (fun previous -> status.Observation > previous)) && reply.Authority.Session = session then
            let projected = ComposerState.response reply |> ComposerState.projection (current.Active = 0) "Session activity is still in progress."
            let ok, manifest = rememberLocked connection projected projectOnDisk
            if ok then
              observations[session] <- { current with Last = Some status.Observation }
              Some projected, manifest
            else None, None
          else None, None)
        manifestToPrune |> Option.iter (fun m -> pruneManifests [ m ])
        // Retirement is a sticky worker-owned fact. An overlapping operation
        // may make this projection stale, but cannot revoke that safety signal.
        if reply.Authority.Session = session
           && ComposerWire.workerAddress reply.Authority = ComposerWire.workerAddress connection.Handshake.Authority then
          match status.WorkerRetirementRequired with
          | Some reason -> escalateRetirement connection reason
          | None -> checkCleanupRetirement connection
        return observed |> Option.defaultWith (fun () -> ComposerState.failure reply.Authority RefusalCode.Superseded "Session activity or a newer observation superseded this read.")
      | Result.Ok _ -> return raise (InvalidDataException "Worker returned a non-status reply to a status request.")
      | Result.Error _ ->
        failed "The worker refused its session status."
        return ComposerState.response reply
    with error ->
      failed error.Message
      return raise error
  }

  let rec reconcile (connection: IComposerWorker) session =
    let owner = connection.Handshake.Authority
    let key = owner.Host, owner.Epoch, session
    let needed () = cached session |> Option.exists ComposerState.needsReconciliation
    let completion = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let admitted = lock gate (fun () ->
      if active connection && needed () && not (monitors.ContainsKey key) then
        monitors.Add(key, completion)
        true
      else false)
    if admitted then
      let run = task {
        use deadline = CancellationTokenSource.CreateLinkedTokenSource lifetime.Token
        deadline.CancelAfter monitorWindow
        let mutable observedIdle = false
        try
          try
            let mutable polling = true
            while polling && lock gate (fun () -> active connection) && not deadline.IsCancellationRequested do
              do! Task.Delay(100, deadline.Token)
              let previous = cached session
              if lock gate (fun () -> active connection) then
                try let! _ = refresh connection session deadline.Token in () with _ -> ()
                checkCleanupRetirement connection
              match previous, cached session with
              | Some before, Some after when not (ComposerState.sameState before after) -> changed ()
              | _ -> ()
              polling <- needed ()
            if not polling then
              observedIdle <- true
              do! (beforeMonitorExit ()).WaitAsync(deadline.Token)
          with :? OperationCanceledException -> ()
        finally
          let restart = lock gate (fun () ->
            monitors.Remove key |> ignore
            // A general reconciliation window cannot abandon an owned cleanup
            // deadline. An in-flight mutation may also have begun cleanup before
            // its first successful status read, so retain that observation owner.
            let ownedCleanup =
              cached session |> Option.exists (fun snapshot ->
                match snapshot.Reply.Outcome with
                | Result.Ok(Observed status) -> status.FormatterCleanupPending || status.CleanupPending
                | _ -> false)
            let renew = deadline.IsCancellationRequested && (ownedCleanup || (observation session).Active > 0)
            not lifetime.IsCancellationRequested && active connection && needed ()
            && ((observedIdle && not deadline.IsCancellationRequested) || renew))
          completion.TrySetResult() |> ignore
          if restart then reconcile connection session
      }
      run |> ignore

  new(factory, configured) = ComposerSupervisor(factory, configured, fun () -> Task.CompletedTask)
  member _.Changed = changedEvent.Publish
  member _.WorkerPid = lock gate (fun () -> worker |> Option.filter _.IsProcessAlive |> Option.map _.ProcessId)

  /// Journal one settled operation: the caller's label, the operation, the
  /// session and its project, the outcome, and a run's bounded output. Status
  /// reads are observation, not operations, and are not journaled.
  member private _.Record(caller: string, request: RequestBody, before: ComposerResponse option, settled: Result<ComposerResponse, exn>) =
    let operation = BAREWireCodec.requestOperation request
    if operation <> Operation.Status then
      let session =
        match settled with
        | Result.Ok response when response.Reply.Authority.Session <> "" -> response.Reply.Authority.Session
        | _ -> ComposerWire.session request
      let project =
        match request, settled with
        | Open(_, project), _ -> project
        | _, Result.Ok { Reply = { Outcome = Result.Ok(Observed status) } } -> status.Project
        | _ ->
          match before |> Option.map (fun b -> b.Reply.Outcome) with
          | Some(Result.Ok(Observed status)) -> status.Project
          | _ -> ""
      let entry =
        ComposerJournal.settle caller operation session project (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
          (settled |> Result.map (fun response -> response.Reply.Outcome))
      lock gate (fun () ->
        match journalStore, journal.StorageError with
        | Some(Ok store), None ->
          match store.Append entry with
          | Ok stored -> journal <- stored
          | Error reason ->
            journal <- { ComposerJournal.append entry journal with StorageError = Some reason }
        | _ -> journal <- ComposerJournal.append entry journal)
      try journalChanged.Trigger() with _ -> ()

  /// The journal as last published, and its change signal (one firing per entry).
  member _.Journal : ComposerJournal.Journal = lock gate (fun () -> journal)
  member _.JournalChanged : IObservable<unit> = journalChanged.Publish
  member _.ReadTranscript(request: ComposerJournal.TranscriptRequest) =
    lock gate (fun () ->
      match journal.StorageError, journalStore with
      | Some reason, _ -> Error(ComposerJournal.TranscriptRefusal.StorageFailure reason)
      | None, Some(Ok store) -> store.ReadTranscript request
      | _ -> Error(ComposerJournal.TranscriptRefusal.StorageFailure "The operations journal has no durable store."))

  /// These observations require durable publication. They never launch work or
  /// grant a lease; ingress validates admission before beginning a command.
  member private _.ChangeLeasedCommand(change: ComposerJournal.Store -> Result<ComposerJournal.Journal * ComposerJournal.Entry, string>) =
    let result, publish = lock gate (fun () ->
      match journal.StorageError, journalStore with
      | Some reason, _ -> Error reason, false
      | None, Some(Ok store) ->
        match change store with
        | Error reason -> Error reason, false
        | Ok(stored, entry) ->
          let publish = stored.Sequence <> journal.Sequence
          journal <- stored
          Ok entry, publish
      | _ -> Error "Leased command observation requires a durable operations journal.", false)
    if publish then
      try journalChanged.Trigger() with _ -> ()
    result

  member this.BeginLeasedCommand(caller: string, project: string, command: ComposerJournal.LeasedCommand) =
    this.ChangeLeasedCommand(fun store -> store.BeginCommand caller project command)

  member this.SettleLeasedCommand(caller: string, project: string, command: ComposerJournal.LeasedCommand, result: ComposerJournal.CommandResult) =
    let outcome = this.ChangeLeasedCommand(fun store -> store.SettleCommand caller project command result)
    let livenessUpdate =
      match outcome with
      | Ok _ ->
        lock gate (fun () ->
          firstAbsentObservedAtMs <- Map.remove command.RunId firstAbsentObservedAtMs
          if Map.containsKey command.RunId lastLivenessVerifiedAtMs then
            lastLivenessVerifiedAtMs <- Map.remove command.RunId lastLivenessVerifiedAtMs
            livenessSerial <- livenessSerial + 1L
            Some { Serial = livenessSerial; VerifiedAtMs = lastLivenessVerifiedAtMs }
          else None)
      | Error _ -> None
    livenessUpdate |> Option.iter (fun snap -> try livenessChanged.Trigger snap with _ -> ())
    outcome

  // Last Unix ms when OS liveness actually confirmed a running leased command alive.
  // Distinct from WorkSnapshot.SampledAtMs (transport refresh time) and Entry.AtMs.
  member _.LastLivenessVerifiedAtMs(runId: string) : int64 option =
    lock gate (fun () -> Map.tryFind runId lastLivenessVerifiedAtMs)

  // Immutable monotonic owner-liveness publication for Fidelity.FSharp.Incremental consumers.
  member _.LivenessChanged : IObservable<LeasedLivenessSnapshot> = livenessChanged.Publish
  member _.CurrentLivenessSnapshot() : LeasedLivenessSnapshot =
    lock gate (fun () -> { Serial = livenessSerial; VerifiedAtMs = lastLivenessVerifiedAtMs })

  // Snapshot of active/unknown leased commands pairing OS liveness verification timestamps
  // with live lease heartbeat expiries (distinct from transport snapshot age).
  member _.LeasedCommandLivenessSnapshot(leaseExpiresAt: string -> DateTimeOffset option) : Bozzetto.Server.DaemonTelemetry.LeasedCommandLiveness list =
    let entries, verifiedMap = lock gate (fun () -> journal.Entries, lastLivenessVerifiedAtMs)
    entries
    |> List.choose (fun entry ->
      match entry.Outcome, entry.Context with
      | ComposerJournal.Outcome.Running, ComposerJournal.Context.LeasedCommand cmd ->
        let expires = leaseExpiresAt cmd.LeaseId
        Some ({
          RunId = cmd.RunId
          LeaseId = cmd.LeaseDisplayId
          ProcessId = cmd.ProcessId
          Outcome = "running"
          StartedAtMs = cmd.StartedAtMs
          LastLivenessVerifiedAtMs = Map.tryFind cmd.RunId verifiedMap
          LeaseLive = expires.IsSome
          LeaseExpiresAt = expires
        } : Bozzetto.Server.DaemonTelemetry.LeasedCommandLiveness)
      | ComposerJournal.Outcome.Unknown detail, ComposerJournal.Context.LeasedCommand cmd ->
        let expires = leaseExpiresAt cmd.LeaseId
        Some ({
          RunId = cmd.RunId
          LeaseId = cmd.LeaseDisplayId
          ProcessId = cmd.ProcessId
          Outcome = "unknown: " + detail
          StartedAtMs = cmd.StartedAtMs
          LastLivenessVerifiedAtMs = Map.tryFind cmd.RunId verifiedMap
          LeaseLive = expires.IsSome
          LeaseExpiresAt = expires
        } : Bozzetto.Server.DaemonTelemetry.LeasedCommandLiveness)
      | _ -> None)

  // Reconcile pending leased commands against OS liveness and live-lease ownership.
  // OS probing runs outside lock gate and SQLite transactions; an exited process
  // whose lease is still live gets a short settleGraceMs window before abandonment
  // so normal lease.fsx completion never races periodic reconciliation.
  member _.ReconcileLeasedCommands(
      probe: int -> int64 -> Bozzetto.ProcessWatchdog.ProcessLiveness,
      hasLiveLease: string -> bool,
      settleGraceMs: int64,
      observedAtMs: int64,
      releaseLease: string -> unit) : ComposerJournal.ReconcileResult =
    let storeChoice = lock gate (fun () -> journal.StorageError, journalStore, journal)
    match storeChoice with
    | Some reason, _, _ -> ComposerJournal.ReconcileResult.StorageFailure reason
    | None, Some(Ok store), _ ->
      let shouldAbandonExited (cmd: ComposerJournal.LeasedCommand) =
        if not (hasLiveLease cmd.LeaseId) || settleGraceMs <= 0L then true
        else
          lock gate (fun () ->
            match Map.tryFind cmd.RunId firstAbsentObservedAtMs with
            | None ->
              firstAbsentObservedAtMs <- Map.add cmd.RunId observedAtMs firstAbsentObservedAtMs
              false
            | Some firstSeen ->
              observedAtMs - firstSeen >= settleGraceMs)
      let result = store.ReconcileRunning probe shouldAbandonExited observedAtMs
      match result with
      | ComposerJournal.ReconcileResult.StorageFailure _ -> result
      | ComposerJournal.ReconcileResult.Reconciled(stored, abandoned, unknown, verifiedAlive) ->
        let publishJournal, livenessPublish = lock gate (fun () ->
          let updated = stored.Sequence > journal.Sequence
          if updated then journal <- stored
          let pendingRunIds =
            journal.Entries
            |> List.choose (fun entry ->
              match entry.Outcome, entry.Context with
              | ComposerJournal.Outcome.Running, ComposerJournal.Context.LeasedCommand cmd
              | ComposerJournal.Outcome.Unknown _, ComposerJournal.Context.LeasedCommand cmd -> Some cmd.RunId
              | _ -> None)
            |> Set.ofList
          let beforeLiveness = lastLivenessVerifiedAtMs
          let mutable nextLiveness =
            lastLivenessVerifiedAtMs |> Map.filter (fun runId _ -> Set.contains runId pendingRunIds)
          firstAbsentObservedAtMs <-
            firstAbsentObservedAtMs |> Map.filter (fun runId _ -> Set.contains runId pendingRunIds)
          for runId in verifiedAlive do
            if Set.contains runId pendingRunIds then
              nextLiveness <- Map.add runId observedAtMs nextLiveness
              firstAbsentObservedAtMs <- Map.remove runId firstAbsentObservedAtMs
          for cmd, _ in unknown do
            firstAbsentObservedAtMs <- Map.remove cmd.RunId firstAbsentObservedAtMs
          for cmd in abandoned do
            nextLiveness <- Map.remove cmd.RunId nextLiveness
            firstAbsentObservedAtMs <- Map.remove cmd.RunId firstAbsentObservedAtMs
          lastLivenessVerifiedAtMs <- nextLiveness
          let livenessSnap =
            if nextLiveness <> beforeLiveness then
              livenessSerial <- livenessSerial + 1L
              Some { Serial = livenessSerial; VerifiedAtMs = nextLiveness }
            else None
          updated, livenessSnap)
        for cmd in abandoned do
          releaseLease cmd.LeaseId
        livenessPublish |> Option.iter (fun snap -> try livenessChanged.Trigger snap with _ -> ())
        if publishJournal then
          try journalChanged.Trigger() with _ -> ()
        result
    | _, _, current -> ComposerJournal.ReconcileResult.Reconciled(current, [], [], [])

  member this.ReconcileLeasedCommands(
      probe: int -> int64 -> Bozzetto.ProcessWatchdog.ProcessLiveness,
      observedAtMs: int64,
      releaseLease: string -> unit) : ComposerJournal.ReconcileResult =
    this.ReconcileLeasedCommands(probe, (fun _ -> false), 0L, observedAtMs, releaseLease)

  member _.FindLeasedCommand(leaseId: string, runId: string) =
    lock gate (fun () ->
      match journal.StorageError, journalStore with
      | Some reason, _ -> Error reason
      | None, Some(Ok store) -> store.FindCommand leaseId runId
      | _ -> Error "Leased command observation requires a durable operations journal.")

  /// Run one operation for `caller` (the adapter's label for who asked) and journal how it settled.
  member this.ExecuteAsync(request: RequestBody, caller: string, cancellation: CancellationToken): Task<ComposerResponse> = task {
    let before = cached (ComposerWire.session request)
    try
      let! response = this.ExecuteCore(request, cancellation)
      this.Record(caller, request, before, Result.Ok response)
      return response
    with error ->
      this.Record(caller, request, before, Result.Error error)
      return raise error
  }

  member this.ExecuteAsync(request: RequestBody, cancellation: CancellationToken): Task<ComposerResponse> =
    this.ExecuteAsync(request, "unknown", cancellation)

  member private _.ExecuteCore(request: RequestBody, cancellation: CancellationToken): Task<ComposerResponse> = task {
    let operation = BAREWireCodec.requestOperation request
    let target = ComposerWire.target request
    if target |> Option.exists (fun value -> value.Provider <> ProviderIdentity.ClefComposer) then
      return refusal request RefusalCode.WrongProvider "Composer operations require provider clef-composer."
    elif operation = Operation.Hello || operation = Operation.CancelRequest then
      return refusal request RefusalCode.UnsupportedOperation "This control is private to the socket owner."
    else
      let mutable selectedOwner: IComposerWorker option = None
      let mutable resolvedSession = ComposerWire.session request
      let mutable openDispatched = false
      let interrupted code message = task {
        match selectedOwner with
        | Some current when resolvedSession <> "" && lock gate (fun () -> active current) ->
          try let! _ = refresh current resolvedSession CancellationToken.None in () with _ -> ()
          reconcile current resolvedSession
        | _ -> ()
        changed ()
        return refusal request code message
      }
      try
        try
          let! selected =
            if operation = Operation.Open then ensureWorker cancellation
            else Task.FromResult(lock gate (fun () -> match worker with Some connection when active connection -> Result.Ok connection | _ -> Result.Error "No active Composer worker owns this operation."))
          match selected with
          | Result.Error message -> return refusal request RefusalCode.ProviderUnavailable message
          | Result.Ok connection ->
            selectedOwner <- Some connection
            let workerAddress = ComposerWire.workerAddress connection.Handshake.Authority
            if operation <> Operation.Open && target <> Some workerAddress then
              return refusal request RefusalCode.WrongAuthority "Use the owning host and epoch from the Composer session response."
            elif operation = Operation.PrepareCompilerChange then return! retire request connection
            elif operation <> Operation.Open && String.IsNullOrWhiteSpace resolvedSession then
              return refusal request RefusalCode.InvalidRequest "An explicit Composer session is required."
            else
              cancellation.ThrowIfCancellationRequested()
              let body = match request with Open(_, project) -> Open(workerAddress, project) | _ -> request
              let operationCancellation = if operation = Operation.Open then CancellationToken.None else cancellation
              let admitted =
                lock gate (fun () ->
                  if operation <> Operation.Open then true
                  elif not (active connection) then false
                  else
                    inFlightOpens <- inFlightOpens + 1
                    openDispatched <- true
                    true)
              if not admitted then
                return refusal request RefusalCode.ProviderRetiring "The Composer worker is retiring."
              else
                let! response = task {
                  if operation = Operation.Status then return! refresh connection resolvedSession operationCancellation
                  else
                    activity connection resolvedSession 1
                    // A held mutation can initiate formatter retirement. Observe
                    // that owned cleanup while its original reply is still pending.
                    if resolvedSession <> "" then reconcile connection resolvedSession
                    try
                      let! reply = connection.RequestAsync(body, operationCancellation)
                      return ComposerState.response reply
                    finally activity connection resolvedSession -1
                }
                let session = response.Reply.Authority.Session
                resolvedSession <- session
                match response.Reply.Outcome with
                | Result.Ok(Closed closed) when session <> "" -> recordCloseAcknowledgement connection response closed
                | _ -> ()
                if not (lock gate (fun () -> active connection)) then
                  if not connection.IsAlive then withdrawOwned connection "The Composer worker disconnected." |> ignore
                  return ComposerState.failure response.Reply.Authority RefusalCode.Closed "The response belongs to a retired worker."
                else
                  if session <> "" then
                    if operation <> Operation.Status then
                      match response.Reply.Outcome with
                      | Result.Ok(Opened opened) ->
                        let status: SessionSnapshot =
                          { Observation = opened.Observation; Project = opened.Project; ManifestPath = opened.ManifestPath
                            Closed = false; Busy = false; Current = None; RevocationPending = false; BackendError = None; FormatterError = None; FormatterCleanupPending = false; WorkerRetirementRequired = None; CleanupPending = false; CleanupError = None }
                        { response with Reply = { response.Reply with Outcome = Result.Ok(Observed status) } }
                        |> ComposerState.projection false "Initial status has not yet been read." |> remember connection |> ignore
                      // Already recorded by recordCloseAcknowledgement before the liveness check.
                      | Result.Ok(Closed _) -> ()
                      | Result.Ok _ ->
                        cached session |> Option.iter (fun previous ->
                          { previous with Reply = { previous.Reply with Authority = response.Reply.Authority } }
                          |> ComposerState.projection false "Session status requires refresh." |> remember connection |> ignore)
                      | _ -> ()
                      try let! _ = refresh connection session CancellationToken.None in () with _ -> ()
                    reconcile connection session
                  if not (lock gate (fun () -> active connection)) then
                    return ComposerState.failure response.Reply.Authority RefusalCode.Closed "The response belongs to a retired worker."
                  else
                    if operation <> Operation.Status then changed ()
                    return stale response
        with
        | :? OperationCanceledException -> return! interrupted RefusalCode.Canceled "The caller canceled this operation."
        | :? TimeoutException as error ->
          match selectedOwner with
          | Some connection when openDispatched && resolvedSession = "" ->
            try
              let! _ = retire request connection
              return refusal request RefusalCode.Timeout (error.Message + " Worker retired because the open outcome was unknown.")
            with cleanup -> return refusal request RefusalCode.RetirementFailed (error.Message + " Worker retirement failed: " + cleanup.Message)
          | _ -> return! interrupted RefusalCode.Timeout error.Message
        | error ->
          changed ()
          return refusal request RefusalCode.ProviderUnavailable error.Message
      finally
        if openDispatched then
          lock gate (fun () -> inFlightOpens <- inFlightOpens - 1)
          selectedOwner |> Option.iter checkCleanupRetirement
  }

  member _.SessionsAsync(cancellation: CancellationToken): Task<ComposerDirectory> = task {
    let connection, owned = lock gate (fun () -> (if stopped || retiring then None else worker), snapshots.Keys |> Seq.toArray)
    match connection with
    | Some current when current.IsAlive ->
      let owner = ComposerWire.workerAddress current.Handshake.Authority
      let relevant = lock gate (fun () -> owned |> Array.filter (fun session -> snapshots.ContainsKey session && ComposerWire.workerAddress snapshots[session].Reply.Authority = owner))
      let pending = relevant |> Array.map (fun session -> task {
        try let! _ = refresh current session cancellation in () with _ -> ()
        reconcile current session
      })
      let! _ = Task.WhenAll pending
      checkCleanupRetirement current
    | Some current ->
      withdrawOwned current "The Composer worker disconnected." |> ignore
    | _ -> ()
    // Evict dead-epoch closed sessions and closed/withdrawn sessions whose disk .fidproj was deleted (#37).
    let deletedProjects = probeDeletedDiskProjects ()
    let manifestsToPrune = ResizeArray<WorkerAddress * string * string>()
    let removedDead =
      lock gate (fun () ->
        let liveOwner = worker |> Option.filter _.IsAlive |> Option.map (fun w -> ComposerWire.workerAddress w.Handshake.Authority)
        let mutable removed = false
        for session in snapshots.Keys |> Seq.toArray do
          let snap = snapshots[session]
          let belongsToLive = liveOwner |> Option.exists (fun lo -> ComposerWire.workerAddress snap.Reply.Authority = lo)
          if not belongsToLive then
            if isAlreadyClosedLocked session snap || Set.contains session deletedProjects then
              let epoch = ComposerWire.workerAddress snap.Reply.Authority
              manifestTarget snap |> Option.iter (fun target ->
                if verifiedEpochsWithSessions.Contains epoch then manifestsToPrune.Add target
                else queueDeferredManifestLocked epoch target)
              evictSessionLocked session
              if (ownedSessionsLocked epoch).IsEmpty then verifiedEpochsWithSessions.Remove epoch |> ignore
              removed <- true
        removed)
    pruneManifests (Seq.toList manifestsToPrune)
    if removedDead then changed ()
    return directory ()
  }

  /// The directory as last observed, without a status read through the
  /// worker. Change pushes read this, so observing a change cannot cause one.
  member _.CachedDirectory() = directory ()

  member _.NewestOpenProjectDirectory() : string option =
    let newestProj =
      lock gate (fun () ->
        snapshots
        |> Seq.choose (fun (KeyValue(session, snap)) ->
          match snap.Reply.Outcome with
          | Result.Ok(Observed status)
              when not status.Closed
                   && not status.CleanupPending
                   && not status.FormatterCleanupPending
                   && snap.WorkerError.IsNone
                   && not (String.IsNullOrWhiteSpace status.Project) ->
            let order = openOrder |> Map.tryFind session |> Option.defaultValue 0L
            Some(order, status.Project)
          | _ -> None)
        |> Seq.sortByDescending fst
        |> Seq.tryHead
        |> Option.map snd)
    newestProj
    |> Option.bind (fun proj ->
      if Directory.Exists proj then Some proj
      else
        match Path.GetDirectoryName proj with
        | null | "" -> None
        | dir -> Some dir)

  member _.StopAsync() = task {
    let failures = ResizeArray<exn>()
    let settle (work: unit -> Task) = task {
      try do! work ()
      with error -> failures.Add error
    }
    let connection, ownedRetirement = lock gate (fun () ->
      stopped <- true
      retiring <- true
      worker, retirement)
    lifetime.Cancel()
    if ownedRetirement |> Option.forall _.IsCompleted then
      connection |> Option.iter (fun current -> withdrawOwned current "Daemon stopped." |> ignore)
    do! lifecycle.WaitAsync()
    try
      match lock gate (fun () -> worker) with
      | Some current ->
        try
          do! current.StopAsync()
          if current.IsProcessAlive then invalidOp "The stopped Composer process did not exit."
          withdrawOwned current "Daemon stopped." |> ignore
          lock gate (fun () -> worker <- None)
        with error ->
          // A terminal stop task can still describe a live, fenced process.
          // Keep its ownership and evidence while joining the other children.
          failures.Add error
          withdrawOwnedWith true current ("Daemon shutdown could not retire the Composer worker: " + error.Message) |> ignore
      | None -> ()
    finally lifecycle.Release() |> ignore
    let owned = lock gate (fun () -> monitors.Values |> Seq.map (fun value -> value.Task :> Task) |> Seq.toArray)
    do! settle (fun () -> Task.WhenAll owned)
    // A concurrent escalation may have queued for the same lifecycle fence.
    // Joining it while holding that fence would deadlock shutdown.
    match ownedRetirement with
    | Some owned -> do! settle (fun () -> owned)
    | None -> ()
    match failures.Count with
    | 0 -> ()
    | 1 -> return raise failures[0]
    | _ -> return raise (AggregateException("Composer shutdown failed.", failures))
  }

module ComposerSupervisor =
  let disabled () = ComposerSupervisor((fun () -> Task.FromException<IComposerWorker>(InvalidOperationException "Composer is not configured.")), false)
  let private createFromEnvironment journalStore =
    let providerCache = ProviderSessionCache.defaultRoot ()
    let environment name = Environment.GetEnvironmentVariable name |> Option.ofObj |> Option.defaultValue ""
    let worker = environment "BOZZETTO_COMPOSER_WORKER"
    if String.IsNullOrWhiteSpace worker then
      ComposerSupervisor((fun () -> Task.FromException<IComposerWorker>(InvalidOperationException "Composer is not configured.")), false,
        (fun () -> Task.CompletedTask), ?journalStore = journalStore, providerCacheRoot = providerCache)
    else
      let dotnet = match environment "DOTNET_HOST_PATH" with value when not (String.IsNullOrWhiteSpace value) -> value | _ -> "dotnet"
      let cache = match environment "XDG_CACHE_HOME" with value when not (String.IsNullOrWhiteSpace value) && Path.IsPathFullyQualified value -> value | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
      ComposerSupervisor((fun () -> ComposerWorkerClient.start { WorkerPath = worker; DotnetPath = dotnet; EvidenceDirectory = Path.Combine(cache, "bozzetto", "composer-workers", Guid.NewGuid().ToString("N")) }), true,
        (fun () -> Task.CompletedTask), ?journalStore = journalStore, providerCacheRoot = providerCache)

  let fromEnvironment () = createFromEnvironment None
  let fromEnvironmentWithJournal dbPath = createFromEnvironment (Some(ComposerJournal.openStore dbPath))
