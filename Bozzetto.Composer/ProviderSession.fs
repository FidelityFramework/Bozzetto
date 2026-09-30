namespace Bozzetto.Providers

open System
open System.Threading
open System.Threading.Tasks

/// Authority changes contain no backend calls. Backend invocation prefixes are
/// ordered separately; their filesystem work cannot block Status, Cancel or
/// BeginClose. Successful reservation remains the editor's permission to edit.
type ProviderSession(host: string, id: string, epoch: string, project: string, backend: IProjectBackend) =
  let gate = obj ()
  let invocation = new SemaphoreSlim(1, 1)
  let manifestPath = backend.ManifestPath
  let mutable generation = 0L
  let mutable closed = false
  let mutable reservation: (string * obj) option = None
  let mutable current: AcceptedArtifact option = None
  let mutable revocation: int64 option = None
  let mutable backendError: string option = None
  let mutable cleanupComplete = false
  let mutable cleanupError: string option = None
  let active = Collections.Generic.Dictionary<string, CancellationTokenSource>()

  let authority () =
    { Host = host; Session = id; Provider = ProviderIdentity.ClefComposer
      Epoch = epoch; Generation = generation }

  let refuse code message = Result.Error { Code = code; Message = message }
  let reply outcome = { Authority = authority (); Outcome = outcome }
  let cancelSources sources =
    for source: CancellationTokenSource in sources do
      // Callbacks run asynchronously and never inside our authority lock.
      try source.CancelAsync() |> ignore with :? ObjectDisposedException -> ()

  let revoke () =
    generation <- generation + 1L
    reservation <- None
    current <- None
    authority (), active.Values |> Seq.toArray

  let obsolete (expected: Authority) =
    if closed then Some(refuse "closed" "The provider session has been closed or its compiler retired.")
    elif generation <> expected.Generation then Some(refuse "superseded" "The response belongs to an earlier revision.")
    else None

  let withdraw (expected: Authority) =
    Task.Run(Func<Task>(fun () -> task {
      do! invocation.WaitAsync()
      try
        let needed = lock gate (fun () -> not closed && generation = expected.Generation)
        if needed then
          let error =
            try backend.Reserve "canceled" |> ignore; None
            with error -> Some error.Message
          lock gate (fun () ->
            if generation = expected.Generation then backendError <- error)
      finally
        lock gate (fun () ->
          if revocation = Some expected.Generation then revocation <- None)
        invocation.Release() |> ignore
    })) |> ignore

  let cancelGeneration (expected: Authority) =
    let changed =
      lock gate (fun () ->
        if not closed && generation = expected.Generation then
          let identity, sources = revoke ()
          revocation <- Some identity.Generation
          Some(identity, sources)
        else None)
    match changed with
    | Some(identity, sources) -> cancelSources sources; withdraw identity
    | None -> ()

  let complete (expected: Authority) (source: CancellationTokenSource) publish result =
    lock gate (fun () ->
      let outcome =
        match obsolete expected with
        | Some refusal -> refusal
        | None when source.IsCancellationRequested ->
          current <- None
          refuse "canceled" "The operation was canceled; its artifact authority is withdrawn."
        | None ->
          match result with
          | Result.Ok value -> publish value; Result.Ok value
          | Result.Error message -> current <- None; refuse "compiler_refused" message
      { Authority = expected; Outcome = outcome })

  // Invoke on a worker thread even when the semaphore is immediately available:
  // an F# task's synchronous prefix can include blocking compiler IO.
  let execute (expected: Authority) (source: CancellationTokenSource) (invoke: unit -> Task<Result<'a, string>>) =
    Task.Run<Result<'a, string>>(Func<Task<Result<'a, string>>>(fun () -> task {
      do! invocation.WaitAsync()
      let mutable running: Task<Result<'a, string>> option = None
      try
        let allowed = lock gate (fun () -> (obsolete expected).IsNone && not source.IsCancellationRequested)
        if allowed then
          try running <- Some(invoke ())
          with error -> running <- Some(Task.FromResult(Result.Error error.Message))
      finally
        invocation.Release() |> ignore
      match running with
      | Some work ->
        try return! work
        with error -> return Result.Error error.Message
      | None -> return Result.Error "Operation authority was withdrawn before compiler invocation."
    }))

  member _.Identity = lock gate authority

  member _.Status() =
    lock gate (fun () ->
      reply (Result.Ok {
        Project = project; ManifestPath = manifestPath; Closed = closed
        Busy = active.Count > 0; Current = current
        RevocationPending = revocation.IsSome; BackendError = backendError
        CleanupPending = closed && not cleanupComplete; CleanupError = cleanupError }))

  member _.ReserveAsync(label: string) =
    let selected =
      lock gate (fun () ->
        if closed then Result.Error(reply (refuse "closed" "Open a fresh provider session after compiler replacement."))
        elif String.IsNullOrWhiteSpace label then Result.Error(reply (refuse "invalid_request" "An edit label is required."))
        else
          let expected, sources = revoke ()
          revocation <- None
          Result.Ok(expected, sources))
    match selected with
    | Result.Error response -> Task.FromResult response
    | Result.Ok(expected, sources) ->
      cancelSources sources
      Task.Run<Reply<string>>(Func<Task<Reply<string>>>(fun () -> task {
        do! invocation.WaitAsync()
        try
          let allowed = lock gate (fun () -> (obsolete expected).IsNone)
          let result =
            if allowed then
              try Result.Ok(backend.Reserve label)
              with error -> Result.Error error.Message
            else Result.Error "Reservation was superseded before compiler invocation."
          return lock gate (fun () ->
            let outcome =
              match obsolete expected with
              | Some refusal -> refusal
              | None ->
                match result with
                | Result.Ok ticket ->
                  let token = Guid.NewGuid().ToString("N")
                  reservation <- Some(token, ticket)
                  backendError <- None
                  Result.Ok token
                | Result.Error message ->
                  backendError <- Some message
                  refuse "backend_failed" message
            { Authority = expected; Outcome = outcome })
        finally
          invocation.Release() |> ignore
      }))

  /// Blocking convenience for callers which explicitly permit compiler IO.
  /// Production supervision uses ReserveAsync and awaits its edit permission.
  member this.Reserve(label) = this.ReserveAsync(label).GetAwaiter().GetResult()

  /// Acknowledges logical withdrawal, not completed compiler cleanup. Status
  /// exposes any pending physical withdrawal and its failure.
  member _.Cancel() =
    let selected =
      lock gate (fun () ->
        if closed then Result.Error(reply (refuse "closed" "The provider session is closed."))
        else
          let expected, sources = revoke ()
          revocation <- Some expected.Generation
          Result.Ok(expected, sources))
    match selected with
    | Result.Error response -> response
    | Result.Ok(expected, sources) ->
      cancelSources sources
      withdraw expected
      { Authority = expected; Outcome = Result.Ok () }

  member _.BuildAsync(token: string, cancellation: CancellationToken) = task {
    let operation = Guid.NewGuid().ToString("N")
    let selected =
      lock gate (fun () ->
        if closed then Result.Error(reply (refuse "closed" "The provider session is closed."))
        else
          match reservation with
          | Some(expected, ticket) when String.Equals(token, expected, StringComparison.Ordinal) ->
            reservation <- None
            let source = new CancellationTokenSource()
            active.Add(operation, source)
            Result.Ok(authority (), ticket, source)
          | _ -> Result.Error(reply (refuse "invalid_reservation" "Reservation is foreign, superseded, or already consumed.")))
    match selected with
    | Result.Error response -> return response
    | Result.Ok(expected, ticket, source) ->
      use source = source
      try
        use _registration = cancellation.Register(fun () ->
          cancelGeneration expected
          cancelSources [| source |])
        let! result = execute expected source (fun () -> backend.BuildAsync(ticket, source.Token))
        return complete expected source (fun accepted -> current <- Some accepted) result
      finally
        lock gate (fun () -> active.Remove operation |> ignore)
  }

  member _.RunAsync(arguments: string list, cancellation: CancellationToken) = task {
    let operation = Guid.NewGuid().ToString("N")
    let selected =
      lock gate (fun () ->
        if closed then Result.Error(reply (refuse "closed" "The provider session is closed."))
        elif current.IsNone then Result.Error(reply (refuse "not_accepted" "No accepted current artifact; reserve and build first."))
        else
          let source = new CancellationTokenSource()
          active.Add(operation, source)
          Result.Ok(authority (), source))
    match selected with
    | Result.Error response -> return response
    | Result.Ok(expected, source) ->
      use source = source
      try
        use _registration = cancellation.Register(fun () ->
          cancelGeneration expected
          cancelSources [| source |])
        let! result = execute expected source (fun () -> backend.RunCurrentAsync(arguments, source.Token))
        return complete expected source ignore result
      finally
        lock gate (fun () -> active.Remove operation |> ignore)
  }

  member _.BeginClose() =
    let response, sources =
      lock gate (fun () ->
        let sources =
          if closed then [||]
          else
            closed <- true
            revocation <- None
            let _, sources = revoke ()
            sources
        reply (Result.Ok ()), sources)
    cancelSources sources
    response

  member this.CloseAsync() =
    this.BeginClose() |> ignore
    Task.Run<Reply<unit>>(Func<Task<Reply<unit>>>(fun () -> task {
      do! invocation.WaitAsync()
      try
        let needed = lock gate (fun () -> not cleanupComplete)
        if needed then
          let error =
            try backend.Dispose(); None
            with error -> Some error.Message
          lock gate (fun () ->
            cleanupError <- error
            cleanupComplete <- error.IsNone)
        return lock gate (fun () ->
          reply (match cleanupError with
                 | Some message -> refuse "cleanup_failed" message
                 | None -> Result.Ok ()))
      finally
        invocation.Release() |> ignore
    }))

  /// Blocking convenience; production supervision awaits CloseAsync separately
  /// from the immediate BeginClose fence.
  member this.Close() = this.CloseAsync().GetAwaiter().GetResult()

  interface IDisposable with
    member this.Dispose() = this.CloseAsync() |> ignore
