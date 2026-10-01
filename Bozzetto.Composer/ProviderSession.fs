namespace Bozzetto.Providers

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Fidelity.FSharp.Incremental
open Fidelity.FSharp.Incremental.Hosting

type private Producer<'a> = {
  Scope: ScopeId
  Work: WorkId
  Authority: Authority
  mutable Value: Result<'a, string> option
  mutable Observation: Task<Result<'a, string>> option
  mutable Observers: int
  mutable Abandoned: bool
}

/// One mailbox owns this explicit compiler session. Tasks here are projections
/// for the CLR worker protocol; evaluator and callback lifetime belongs to the
/// functional host. A producer's scope also identifies its physical drain.
type ProviderSession<'Ticket>(host, id, epoch, project, backend: IProjectBackend<'Ticket>) =
  let gate = obj ()
  let invocation = new SemaphoreSlim(1, 1)
  let manifestPath = backend.ManifestPath
  let evaluators = Dictionary<WorkId, WorkCancellation -> Async<StepOutcome>>()
  let liveScopes = HashSet<ScopeId>()
  let active = HashSet<WorkId>()
  let observations = Dictionary<WorkId, Task>()
  let controls = Dictionary<uint64, Task>()
  let mutable serial = 0UL
  let mutable demandSerial = 0UL
  let mutable controlSerial = 0UL
  let mutable generation = 0L
  let mutable closed = false
  let mutable reservation: (string * Producer<AcceptedArtifact>) option = None
  let mutable current: AcceptedArtifact option = None
  let mutable revocation: int64 option = None
  let mutable backendError: string option = None
  let mutable cleanupComplete = false
  let mutable cleanupError: string option = None
  let mutable closeTask: Task<Reply<unit>> option = None
  // The core keeps protocol tombstones for the epoch. Bound the epoch rather
  // than silently discard their duplicate/conflict evidence. Reopen on refusal.
  let scopeLimit = 1024UL
  let demandLimit = 8192UL
  let controlLimit = 8192UL

  let authority () =
    { Host = host; Session = id; Provider = ProviderIdentity.ClefComposer
      Epoch = epoch; Generation = generation }
  let refuse code message : Result<'a, Refusal> = Result.Error { Code = code; Message = message }
  let reply outcome = { Authority = authority (); Outcome = outcome }
  let obsolete (expected: Authority) =
    if closed then Some(refuse "closed" "Open a fresh provider session after compiler replacement.")
    elif generation <> expected.Generation then Some(refuse "superseded" "A newer reservation withdrew this operation's authority.")
    else None

  let evaluator invocation cancellation = async {
    let work =
      match invocation with
      | StepInvocation.Start request -> request.Definition.Work
      | StepInvocation.Resume request -> request.Start.Definition.Work
    let selected = lock gate (fun () -> evaluators.TryGetValue work)
    match selected with
    | true, evaluate -> return! evaluate cancellation
    | _ -> return StepOutcome.Complete(Completion.Failed { Code = "missing-producer"; Message = "The provider no longer owns this producer." })
  }
  let mailbox =
    AsyncMailbox.create { Epoch = EpochId 1UL; MaxConcurrency = 4; CommandCapacity = 32768 } evaluator
    |> function Result.Ok value -> value | Result.Error error -> invalidOp (string error)
  do AsyncMailbox.start mailbox |> Result.defaultWith (fun error -> invalidOp (string error))

  let admit action =
    AsyncMailbox.admit action mailbox
    |> Result.defaultWith (fun error -> invalidOp (sprintf "Incremental admission refused: %A" error))
  let observe operation = async {
    let! receipt = AsyncMailbox.observe operation
    return receipt |> Result.defaultWith (fun error -> invalidOp (sprintf "Incremental command refused: %A" error))
  }
  let observeAll operations = async {
    for operation in operations do
      let! _ = observe operation
      ()
  }
  let drainObservations () =
    AsyncMailbox.drainEvents mailbox |> ignore
    let errors = AsyncMailbox.drainDiagnostics mailbox
    if not errors.IsEmpty then
      lock gate (fun () -> backendError <- Some(errors |> List.map (fun error -> error.Failure.Message) |> String.concat "; "))

  // Call under gate: admission order is the authority order. No compiler call
  // or user cancellation callback executes on this stack.
  let revoke () =
    generation <- generation + 1L
    reservation <- None
    current <- None
    let operations = liveScopes |> Seq.map (fun scope -> admit (Action.CloseScope scope)) |> Seq.toList
    for ScopeId value in liveScopes do
      let work = WorkId value
      if not (active.Contains work) then evaluators.Remove work |> ignore
    liveScopes.Clear()
    authority (), operations

  let startControl workflow =
    controlSerial <- controlSerial + 1UL
    let key = controlSerial
    let work = ClrInterop.toTask CancellationToken.None (async {
      try return! workflow
      finally lock gate (fun () -> controls.Remove key |> ignore)
    })
    controls.Add(key, work :> Task)
    work

  let allocate expected =
    serial <- serial + 1UL
    let producer = {
      Scope = ScopeId serial; Work = WorkId serial; Authority = expected
      Value = None; Observation = None; Observers = 0; Abandoned = false }
    liveScopes.Add producer.Scope |> ignore
    producer

  let define (producer: Producer<'a>) invoke =
    let evaluate cancellation = async {
      do! invocation.WaitAsync() |> Async.AwaitTask
      let mutable running: Task<Result<'a, string>> option = None
      try
        let allowed = lock gate (fun () -> (obsolete producer.Authority).IsNone && not (WorkCancellation.isRequested cancellation))
        if allowed then
          // This named CLR boundary invokes the factory under the launch
          // fence. Its returned Task must include all native process cleanup.
          try running <- Some(invoke (ClrInterop.cancellationToken cancellation))
          with error -> running <- Some(Task.FromResult(Result.Error error.Message))
      finally invocation.Release() |> ignore
      let! result = async {
        match running with
        | None -> return Result.Error "Operation authority was withdrawn before compiler invocation."
        | Some work ->
          try return! ClrInterop.fromTask (fun _ -> work) cancellation
          with error -> return Result.Error error.Message
      }
      lock gate (fun () -> producer.Value <- Some result)
      return StepOutcome.Complete(
        match result with
        | Result.Ok _ -> let (WorkId value) = producer.Work in Completion.Succeeded(ValueToken value)
        | Result.Error message -> Completion.Failed { Code = "compiler_refused"; Message = message })
    }
    evaluators.Add(producer.Work, evaluate)
    [ admit (Action.ReserveScope(producer.Scope, RevisionId 1UL))
      admit (Action.ReplaceScope(producer.Scope, RevisionId 1UL,
        [ ScopeEntry.Define { Work = producer.Work; Stamp = DefinitionStamp 1UL; Reads = [] } ])) ]

  let rec awaitProducer (producer: Producer<'a>) = async {
    let snapshot, changed = AsyncMailbox.watch mailbox
    let pending = snapshot.Graph.Scopes |> List.tryFind (fun scope -> scope.Scope = producer.Scope)
    if snapshot.IsClosing then
      let! closed = AsyncMailbox.beginClose mailbox |> AsyncMailbox.awaitClose
      return closed |> Result.mapError (fun error -> error.Message)
    else
      match pending with
      | Some scope when scope.PendingAttempts = 0 -> return Result.Ok ()
      | _ ->
        do! changed
        return! awaitProducer producer
  }

  let startObservation producer operation publish =
    active.Add producer.Work |> ignore
    let observation = ClrInterop.toTask CancellationToken.None (async {
      let! _ = observe operation
      let! joined = awaitProducer producer
      drainObservations ()
      return lock gate (fun () ->
        active.Remove producer.Work |> ignore
        observations.Remove producer.Work |> ignore
        evaluators.Remove producer.Work |> ignore
        let result =
          joined |> Result.bind (fun () ->
            producer.Value |> Option.defaultValue (Result.Error "The producer was canceled before invocation."))
          |> Result.bind (fun value ->
            let (WorkId token) = producer.Work
            match AsyncMailbox.tryResult producer.Work mailbox with
            | Some result when result.Scope = producer.Scope && result.Value = ValueToken token
                               && AsyncMailbox.isEligible result mailbox -> Result.Ok value
            | _ -> Result.Error "The incremental producer no longer has eligible result authority.")
        if not producer.Abandoned && (obsolete producer.Authority).IsNone then
          match result with Result.Ok value -> publish value | Result.Error _ -> ()
        result)
    })
    observations.Add(producer.Work, observation :> Task)
    producer.Observation <- Some observation
    observation

  let attach producer publish =
    if demandSerial >= demandLimit then Result.Error "Session demand capacity reached; open a fresh session."
    else
      demandSerial <- demandSerial + 1UL
      let demand = DemandId demandSerial
      let operation = admit (Action.Demand(demand, producer.Work))
      producer.Observers <- producer.Observers + 1
      let observation =
        match producer.Observation with
        | Some observation -> observation
        | None -> startObservation producer operation publish
      Result.Ok(demand, observation)

  let complete expected result =
    lock gate (fun () ->
      { Authority = expected
        Outcome =
          match obsolete expected with
          | Some refusal -> refusal
          | None -> result |> Result.mapError (fun message -> { Code = "compiler_refused"; Message = message }) })

  let projectReply producer demand (observation: Task<Result<'a, string>>) cancellation = task {
    try
      try
        let! result = observation.WaitAsync(cancellation: CancellationToken)
        return complete producer.Authority result
      with :? OperationCanceledException ->
        return { Authority = producer.Authority; Outcome = refuse "canceled" "This request detached its demand; other clients retain theirs." }
    finally
      lock gate (fun () ->
        producer.Observers <- producer.Observers - 1
        if producer.Observers = 0 && not observation.IsCompleted then producer.Abandoned <- true
        if not closed then
          admit (Action.Release demand) |> ignore)
  }

  member _.Identity = lock gate authority

  member _.Status() = lock gate (fun () ->
    drainObservations ()
    reply (Result.Ok {
      Project = project; ManifestPath = manifestPath; Closed = closed
      Busy = active.Count > 0; Current = current
      RevocationPending = revocation.IsSome; BackendError = backendError
      CleanupPending = closed && not cleanupComplete; CleanupError = cleanupError }))

  member _.ReserveAsync(label: string) = lock gate (fun () ->
    if closed then Task.FromResult(reply (refuse "closed" "The provider session is closed."))
    elif String.IsNullOrWhiteSpace label then Task.FromResult(reply (refuse "invalid_request" "An edit label is required."))
    elif controls.Count >= 128 then Task.FromResult(reply (refuse "busy" "The compiler control queue is full; retry after pending controls settle."))
    elif controlSerial >= controlLimit then Task.FromResult(reply (refuse "session_capacity" "Session control capacity reached; open a fresh session."))
    elif serial >= scopeLimit then Task.FromResult(reply (refuse "session_capacity" "Session scope capacity reached; open a fresh session."))
    else
      let expected, withdrawals = revoke ()
      revocation <- None
      startControl (async {
        do! invocation.WaitAsync() |> Async.AwaitTask
        try
          let allowed = lock gate (fun () -> (obsolete expected).IsNone)
          let result =
            if allowed then
              try Result.Ok(backend.Reserve label)
              with error -> Result.Error error.Message
            else Result.Error "Reservation was superseded before compiler invocation."
          do! observeAll withdrawals
          let selected = lock gate (fun () ->
            match obsolete expected, result with
            | Some refusal, _ -> Result.Error { Authority = expected; Outcome = refusal }
            | None, Result.Error message ->
              backendError <- Some message
              Result.Error { Authority = expected; Outcome = refuse "backend_failed" message }
            | None, Result.Ok ticket ->
              let producer = allocate expected
              let operations = define producer (fun cancellation -> backend.BuildAsync(ticket, cancellation))
              Result.Ok(producer, operations))
          match selected with
          | Result.Error response -> return response
          | Result.Ok(producer, operations) ->
            do! observeAll operations
            return lock gate (fun () ->
              let outcome =
                match obsolete expected with
                | Some refusal -> refusal
                | None ->
                  let token = Guid.NewGuid().ToString("N")
                  reservation <- Some(token, producer)
                  backendError <- None
                  Result.Ok token
              { Authority = expected; Outcome = outcome })
        finally
          invocation.Release() |> ignore
          drainObservations ()
      }))

  /// Edit permission includes synchronous Composer reservation and committed
  /// library invalidation. Compiler IO is never put behind evaluator dispatch.
  member this.Reserve(label) = this.ReserveAsync(label).GetAwaiter().GetResult()

  /// Explicit Cancel still revokes the generation; cancel_request detaches only
  /// one caller's demand through BuildAsync/RunAsync's observation boundary.
  member _.Cancel() = lock gate (fun () ->
    if closed then reply (refuse "closed" "The provider session is closed.")
    elif controls.Count >= 128 then reply (refuse "busy" "The compiler control queue is full; retry after pending controls settle.")
    elif controlSerial >= controlLimit then reply (refuse "session_capacity" "Session control capacity reached; open a fresh session.")
    else
      let expected, withdrawals = revoke ()
      revocation <- Some expected.Generation
      startControl (async {
        do! invocation.WaitAsync() |> Async.AwaitTask
        try
          if lock gate (fun () -> (obsolete expected).IsNone) then
            let error = try backend.Reserve "canceled" |> ignore; None with error -> Some error.Message
            lock gate (fun () -> if generation = expected.Generation then backendError <- error)
          do! observeAll withdrawals
        finally
          invocation.Release() |> ignore
          lock gate (fun () -> if revocation = Some expected.Generation then revocation <- None)
          drainObservations ()
      }) |> ignore
      { Authority = expected; Outcome = Result.Ok () })

  member _.BuildAsync(token: string, cancellation: CancellationToken) = lock gate (fun () ->
    if closed then Task.FromResult(reply (refuse "closed" "The provider session is closed."))
    elif cancellation.IsCancellationRequested then Task.FromResult(reply (refuse "canceled" "This request was canceled before admission."))
    else
      match reservation with
      | Some(expected, producer) when String.Equals(token, expected, StringComparison.Ordinal) ->
        if producer.Abandoned then Task.FromResult(reply (refuse "canceled" "All demands for this ticket were released; reserve again."))
        else
          match producer.Observation with
          | Some observation when observation.IsCompleted -> task {
            let! result = observation
            return complete producer.Authority result }
          | _ ->
            match attach producer (fun accepted -> current <- Some accepted) with
            | Result.Error message -> Task.FromResult(reply (refuse "session_capacity" message))
            | Result.Ok(demand, observation) -> projectReply producer demand observation cancellation
      | _ -> Task.FromResult(reply (refuse "invalid_reservation" "Reservation is foreign or superseded.")))

  member _.RunAsync(arguments: string list, cancellation: CancellationToken) = lock gate (fun () ->
    if closed then Task.FromResult(reply (refuse "closed" "The provider session is closed."))
    elif cancellation.IsCancellationRequested then Task.FromResult(reply (refuse "canceled" "This request was canceled before admission."))
    elif current.IsNone then Task.FromResult(reply (refuse "not_accepted" "No accepted current artifact; reserve and build first."))
    elif serial >= scopeLimit || demandSerial >= demandLimit then Task.FromResult(reply (refuse "session_capacity" "Session work capacity reached; open a fresh session."))
    else
      let producer = allocate (authority ())
      define producer (fun token -> backend.RunCurrentAsync(arguments, token)) |> ignore
      match attach producer ignore with
      | Result.Error message -> Task.FromResult(reply (refuse "session_capacity" message))
      | Result.Ok(demand, observation) -> projectReply producer demand observation cancellation)

  member _.BeginClose() = lock gate (fun () ->
    if not closed then
      closed <- true
      generation <- generation + 1L
      reservation <- None
      current <- None
      revocation <- None
      liveScopes.Clear()
      AsyncMailbox.beginClose mailbox |> ignore
    reply (Result.Ok ()))

  member this.CloseAsync() = lock gate (fun () ->
    this.BeginClose() |> ignore
    match closeTask with
    | Some work when not work.IsCompleted || cleanupComplete -> work
    | _ ->
      let ownedControls = controls.Values |> Seq.toArray
      let ownedObservations = observations.Values |> Seq.toArray
      let work = ClrInterop.toTask CancellationToken.None (async {
        let! joined = AsyncMailbox.beginClose mailbox |> AsyncMailbox.awaitClose
        let mutable projectionError = None
        try do! Task.WhenAll(Array.append ownedControls ownedObservations) |> Async.AwaitTask
        with error -> projectionError <- Some error.Message
        do! invocation.WaitAsync() |> Async.AwaitTask
        try
          let disposalError = try backend.Dispose(); None with error -> Some error.Message
          let error =
            [ (match joined with Result.Error error -> Some error.Message | _ -> None)
              projectionError; disposalError ]
            |> List.choose (fun value -> value)
            |> function [] -> None | messages -> Some(String.concat "; " messages)
          return lock gate (fun () ->
            cleanupError <- error
            cleanupComplete <- error.IsNone
            evaluators.Clear()
            reply (match error with Some message -> refuse "cleanup_failed" message | None -> Result.Ok ()))
        finally invocation.Release() |> ignore
      })
      closeTask <- Some work
      work)

  member this.Close() = this.CloseAsync().GetAwaiter().GetResult()

  interface IDisposable with
    member this.Dispose() = this.Close() |> ignore
