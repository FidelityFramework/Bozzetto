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
      status.Closed && not status.CleanupPending && not status.FormatterCleanupPending && status.CleanupError.IsNone
      && status.WorkerRetirementRequired.IsNone && snapshot.WorkerError.IsNone
    | _ -> false
  let needsReconciliation snapshot =
    match snapshot.Reply.Outcome with
    | Result.Ok(Observed status) ->
      not snapshot.StatusFresh || status.Busy || status.RevocationPending || status.FormatterCleanupPending
      || status.WorkerRetirementRequired.IsSome || (status.CleanupPending && status.CleanupError.IsNone)
    | _ -> false
  let sameState left right =
    let normalize value =
      match value.Reply.Outcome with
      | Result.Ok(Observed status) -> { value with Reply = { value.Reply with RequestId = ""; Outcome = Result.Ok(Observed { status with Observation = 0UL }) } }
      | _ -> { value with Reply = { value.Reply with RequestId = "" } }
    normalize left = normalize right

/// Shared typed compiler owner. JSON exists only at external client adapters.
type ComposerSupervisor internal (factory: unit -> Task<IComposerWorker>, configured: bool, beforeMonitorExit: unit -> Task, ?monitorWindow: TimeSpan, ?journalStore: Result<ComposerJournal.Store, string>) =
  let gate = obj ()
  let lifecycle = new SemaphoreSlim(1, 1)
  let lifetime = new CancellationTokenSource()
  let monitorWindow = defaultArg monitorWindow (TimeSpan.FromMinutes 8.)
  let changedEvent = Event<unit>()
  let snapshots = Dictionary<string, ComposerResponse>()
  let observations = Dictionary<string, StatusObservation>()
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
  let journalChanged = Event<unit>()

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

  let withdrawOwned (connection: IComposerWorker) reason =
    let owner = ComposerWire.workerAddress connection.Handshake.Authority
    lock gate (fun () ->
      for session in snapshots.Keys |> Seq.toArray do
        if ComposerWire.workerAddress snapshots[session].Reply.Authority = owner then
          snapshots[session] <- ComposerState.terminal reason snapshots[session]
      forgotten.Clear())
    changed ()
  let remember (connection: IComposerWorker) snapshot =
    let identity = snapshot.Reply.Authority
    lock gate (fun () ->
      let currentEnough = cached identity.Session |> Option.forall (fun previous -> identity.Generation >= previous.Reply.Authority.Generation)
      let notForgotten =
        match forgotten.TryGetValue identity.Session with
        | true, generation -> identity.Generation > generation
        | _ -> true
      if Result.isOk snapshot.Reply.Outcome && active connection && identity.Session <> "" && currentEnough && notForgotten
         && ComposerWire.workerAddress identity = ComposerWire.workerAddress connection.Handshake.Authority then
        if ComposerState.finished snapshot then
          snapshots.Remove identity.Session |> ignore
          observations.Remove identity.Session |> ignore
          forgotten[identity.Session] <- identity.Generation
        else snapshots[identity.Session] <- snapshot
        true
      else false)
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
              if lock gate (fun () -> isOwner connection && not retiring) then withdrawOwned connection reason)
            connection.InputsRevoked.Add(fun notification ->
              let accepted = lock gate (fun () ->
                match notification.Outcome, cached notification.Authority.Session with
                | Result.Ok(InputsRevoked status), Some _
                    when notification.RequestId = "" && status.Current.IsNone
                         && notification.ProtocolVersion = BAREWireCodec.ProtocolVersion
                         && ComposerWire.workerAddress notification.Authority = ComposerWire.workerAddress connection.Handshake.Authority
                         && ((observation notification.Authority.Session).Last |> Option.forall (fun previous -> status.Observation > previous)) ->
                  let projected = { notification with Outcome = Result.Ok(Observed status) } |> ComposerState.response
                  if remember connection projected then
                    let prior = observation notification.Authority.Session
                    observations[notification.Authority.Session] <- { prior with Last = Some status.Observation }
                    true
                  else false
                | _ -> false)
              if accepted then changed ())
            if not connection.IsAlive then withdrawOwned connection "Worker exited during startup."
            changed ()
            return Result.Ok connection
    finally lifecycle.Release() |> ignore
  }

  let retireOwned request (connection: IComposerWorker) reason prepare = task {
    let admitted = lock gate (fun () ->
      if stopped || retiring || not (isOwner connection) then false
      else
        retiring <- true
        true)
    if not admitted then return refusal request RefusalCode.ProviderRetiring "The Composer worker is already retiring."
    else
      withdrawOwned connection reason
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
        lock gate (fun () -> if isOwner connection then worker <- None)
        return result
      finally
        if exited then lock gate (fun () -> retiring <- false)
        lifecycle.Release() |> ignore
        changed ()
  }

  let retire request connection = retireOwned request connection "Compiler retirement requested." true

  let escalateRetirement (connection: IComposerWorker) reason =
    lock gate (fun () ->
      if active connection then
        // This hot task is owned by the supervisor, alongside its status
        // monitors. Fencing happens synchronously before the first await.
        let request = PrepareCompilerChange(ComposerWire.workerAddress connection.Handshake.Authority)
        let stopping = task {
          try
            let! _ = retireOwned request connection reason false
            ()
          with error ->
            withdrawOwned connection (reason + " Worker retirement failed: " + error.Message)
        }
        retirement <- Some(stopping :> Task))

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
        let observed = lock gate (fun () ->
          let current = observation session
          if applicable () && (current.Last |> Option.forall (fun previous -> status.Observation > previous)) && reply.Authority.Session = session then
            let projected = ComposerState.response reply |> ComposerState.projection (current.Active = 0) "Session activity is still in progress."
            if remember connection projected then
              observations[session] <- { current with Last = Some status.Observation }
              Some projected
            else None
          else None)
        // Retirement is a sticky worker-owned fact. An overlapping operation
        // may make this projection stale, but cannot revoke that safety signal.
        if reply.Authority.Session = session
           && ComposerWire.workerAddress reply.Authority = ComposerWire.workerAddress connection.Handshake.Authority then
          status.WorkerRetirementRequired |> Option.iter (escalateRetirement connection)
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
                | Result.Ok(Observed status) -> status.FormatterCleanupPending
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
    this.ChangeLeasedCommand(fun store -> store.SettleCommand caller project command result)

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
            openDispatched <- operation = Operation.Open
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
            if not (lock gate (fun () -> active connection)) then
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
  }

  member _.SessionsAsync(cancellation: CancellationToken): Task<ComposerDirectory> = task {
    let connection, owned = lock gate (fun () -> (if stopped || retiring then None else worker), snapshots.Keys |> Seq.toArray)
    match connection with
    | Some current when current.IsAlive ->
      let owner = ComposerWire.workerAddress current.Handshake.Authority
      let relevant = lock gate (fun () -> owned |> Array.filter (fun session -> ComposerWire.workerAddress snapshots[session].Reply.Authority = owner))
      let pending = relevant |> Array.map (fun session -> task {
        try let! _ = refresh current session cancellation in () with _ -> ()
        reconcile current session
      })
      let! _ = Task.WhenAll pending
      ()
    | _ -> ()
    return directory ()
  }

  /// The directory as last observed, without a status read through the
  /// worker. Change pushes read this, so observing a change cannot cause one.
  member _.CachedDirectory() = directory ()

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
      connection |> Option.iter (fun current -> withdrawOwned current "Daemon stopped.")
    do! lifecycle.WaitAsync()
    try
      match lock gate (fun () -> worker) with
      | Some current ->
        try
          do! current.StopAsync()
          if current.IsProcessAlive then invalidOp "The stopped Composer process did not exit."
          lock gate (fun () -> worker <- None)
        with error ->
          // A terminal stop task can still describe a live, fenced process.
          // Keep its ownership and evidence while joining the other children.
          failures.Add error
          withdrawOwned current ("Daemon shutdown could not retire the Composer worker: " + error.Message)
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
    let environment name = Environment.GetEnvironmentVariable name |> Option.ofObj |> Option.defaultValue ""
    let worker = environment "BOZZETTO_COMPOSER_WORKER"
    if String.IsNullOrWhiteSpace worker then
      ComposerSupervisor((fun () -> Task.FromException<IComposerWorker>(InvalidOperationException "Composer is not configured.")), false,
        (fun () -> Task.CompletedTask), ?journalStore = journalStore)
    else
      let dotnet = match environment "DOTNET_HOST_PATH" with value when not (String.IsNullOrWhiteSpace value) -> value | _ -> "dotnet"
      let cache = match environment "XDG_CACHE_HOME" with value when not (String.IsNullOrWhiteSpace value) && Path.IsPathFullyQualified value -> value | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
      ComposerSupervisor((fun () -> ComposerWorkerClient.start { WorkerPath = worker; DotnetPath = dotnet; EvidenceDirectory = Path.Combine(cache, "bozzetto", "composer-workers", Guid.NewGuid().ToString("N")) }), true,
        (fun () -> Task.CompletedTask), ?journalStore = journalStore)

  let fromEnvironment () = createFromEnvironment None
  let fromEnvironmentWithJournal dbPath = createFromEnvironment (Some(ComposerJournal.openStore dbPath))
