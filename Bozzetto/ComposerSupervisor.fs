namespace Bozzetto.ComposerIntegration

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks

type ComposerRequest = {
  Operation: string
  Session: string
  Host: string
  Epoch: string
  Provider: string
  Parameters: (string * obj) list
}

module private ComposerJson =
  let options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
  let value data = JsonSerializer.SerializeToElement(data, options)
  let empty = value {| |}
  let field name (json: JsonElement) =
    match json.TryGetProperty(name: string) with
    | true, item -> item
    | _ -> empty
  let text name json =
    let item = field name json
    if item.ValueKind = JsonValueKind.String then item.GetString() |> Option.ofObj |> Option.defaultValue "" else ""
  let success json = (field "success" json).ValueKind = JsonValueKind.True
  let failure identity code message =
    value {| protocolVersion = 1; requestId = Guid.NewGuid().ToString("N")
             authority = identity; success = false; result = empty
             error = {| code = code; message = message |} |}
  let hostIdentity worker = field "authority" worker
  let generation identity =
    let number = field "generation" identity
    if number.ValueKind <> JsonValueKind.Number then -1L
    else match number.TryGetInt64() with true, value -> value | _ -> -1L
  let sameState left right =
    (field "authority" left).GetRawText() = (field "authority" right).GetRawText()
    && (field "result" left).GetRawText() = (field "result" right).GetRawText()
  let unknownIdentity (request: ComposerRequest) =
    value {| host = request.Host; session = request.Session; epoch = request.Epoch
             provider = "clef-composer"; generation = 0L |}
  let terminal reason (snapshot: JsonElement) =
    let node = JsonNode.Parse(snapshot.GetRawText()) |> Option.ofObj |> Option.defaultWith (fun () -> invalidOp "Missing snapshot.")
    let result = node["result"] |> Option.ofObj |> Option.defaultWith (fun () -> invalidOp "Missing snapshot result.")
    result["closed"] <- JsonValue.Create true
    result["busy"] <- JsonValue.Create false
    result["current"] <- null
    result["workerAvailable"] <- JsonValue.Create false
    result["workerError"] <- JsonValue.Create(reason: string)
    JsonSerializer.SerializeToElement node
  let projection fresh reason (snapshot: JsonElement) =
    let node = JsonNode.Parse(snapshot.GetRawText()) |> Option.ofObj |> Option.get
    let result = node["result"] |> Option.ofObj |> Option.get
    for name in [ "closed"; "busy"; "revocationPending"; "cleanupPending" ] do
      if isNull result[name] then result[name] <- JsonValue.Create false
    if not fresh then result["current"] <- null
    result["statusFresh"] <- JsonValue.Create fresh
    result["statusError"] <- (if fresh then null else JsonValue.Create(reason: string))
    result["executionRequiresRevalidation"] <- JsonValue.Create true
    JsonSerializer.SerializeToElement node
  let withAuthority authority (snapshot: JsonElement) =
    let node = JsonNode.Parse(snapshot.GetRawText()) |> Option.ofObj |> Option.get
    node["authority"] <- JsonNode.Parse((authority: JsonElement).GetRawText())
    JsonSerializer.SerializeToElement node
  let needsReconciliation snapshot =
    let result = field "result" snapshot
    let enabled name = (field name result).ValueKind = JsonValueKind.True
    (field "statusFresh" result).ValueKind = JsonValueKind.False
    || enabled "busy" || enabled "revocationPending"
    || (enabled "cleanupPending" && (field "cleanupError" result).ValueKind <> JsonValueKind.String)

/// One daemon owner, shared by HTTP, MCP tools and resources. Compiler authority
/// remains in the worker; the daemon never loads Composer or artifact assemblies.
type ComposerSupervisor(factory: unit -> Task<IComposerWorker>, configured: bool) =
  let gate = obj ()
  let lifecycle = new SemaphoreSlim(1, 1)
  let changedEvent = Event<unit>()
  let snapshots = Dictionary<string, JsonElement>()
  let monitors = HashSet<string * string * string>()
  let mutable worker: IComposerWorker option = None
  let mutable retiring = false
  let mutable stopped = false
  let mutable revision = 0L

  let changed () =
    lock gate (fun () -> revision <- revision + 1L)
    try changedEvent.Trigger() with _ -> ()

  let isOwner (connection: IComposerWorker) =
    worker |> Option.exists (fun current -> obj.ReferenceEquals(current, connection))

  let withdrawOwned (connection: IComposerWorker) reason =
    let identity = ComposerJson.hostIdentity connection.Handshake
    lock gate (fun () ->
      for session in snapshots.Keys |> Seq.toArray do
        let snapshot = snapshots[session]
        let authority = ComposerJson.field "authority" snapshot
        if ComposerJson.text "host" authority = ComposerJson.text "host" identity
           && ComposerJson.text "epoch" authority = ComposerJson.text "epoch" identity then
          snapshots[session] <- ComposerJson.terminal reason snapshot)
    changed ()

  let remember (connection: IComposerWorker) (snapshot: JsonElement) =
    if ComposerJson.success snapshot then
      let identity = ComposerJson.field "authority" snapshot
      let session = ComposerJson.text "session" identity
      let expected = ComposerJson.hostIdentity connection.Handshake
      lock gate (fun () ->
        let currentEnough =
          match snapshots.TryGetValue session with
          | true, previous -> ComposerJson.generation identity >= ComposerJson.generation (ComposerJson.field "authority" previous)
          | _ -> true
        if isOwner connection && connection.IsAlive && not stopped && not retiring && session <> ""
           && ComposerJson.text "host" identity = ComposerJson.text "host" expected
           && ComposerJson.text "epoch" identity = ComposerJson.text "epoch" expected && currentEnough then
          snapshots[session] <- snapshot)

  let uncertain (connection: IComposerWorker) session reason =
    lock gate (fun () ->
      match snapshots.TryGetValue session with
      | true, previous when isOwner connection && connection.IsAlive && not stopped && not retiring ->
        snapshots[session] <- ComposerJson.projection false reason previous
      | _ -> ())

  let cached session =
    lock gate (fun () ->
      match snapshots.TryGetValue session with
      | true, snapshot -> Some snapshot
      | _ -> None)

  let stale response =
    let identity = ComposerJson.field "authority" response
    match cached (ComposerJson.text "session" identity) with
    | Some previous when ComposerJson.generation identity < ComposerJson.generation (ComposerJson.field "authority" previous) ->
      Some(ComposerJson.failure (ComposerJson.field "authority" previous) "superseded" "A newer session generation has already been observed.")
    | _ -> None

  let refusal (request: ComposerRequest) code message =
    let identity = lock gate (fun () ->
      match snapshots.TryGetValue request.Session with
      | true, snapshot -> ComposerJson.field "authority" snapshot
      | _ -> ComposerJson.unknownIdentity request)
    ComposerJson.failure identity code message

  let ensureWorker cancellation = task {
    do! lifecycle.WaitAsync(cancellation: CancellationToken)
    try
      let existing, denied = lock gate (fun () -> worker, stopped || retiring)
      if denied then return Result.Error "The Composer owner is retiring or has stopped."
      elif not configured then
        return Result.Error "Set BOZZETTO_COMPOSER_WORKER to an absolute built worker path before starting Bozzetto."
      else
        match existing with
        | Some connection when connection.IsAlive -> return Result.Ok connection
        | _ ->
          match existing with
          | Some connection -> do! connection.StopAsync()
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
            connection.Exited.Add(fun reason -> withdrawOwned connection reason)
            if not connection.IsAlive then withdrawOwned connection "Worker exited during startup."
            changed ()
            return Result.Ok connection
    finally
      lifecycle.Release() |> ignore
  }

  let refresh (connection: IComposerWorker) session cancellation = task {
    try
      let! snapshot = connection.RequestAsync("status", session, [], cancellation)
      if ComposerJson.success snapshot then remember connection (ComposerJson.projection true "" snapshot)
      else uncertain connection session "The worker refused its session status."
      return snapshot
    with error ->
      uncertain connection session error.Message
      return raise error
  }

  let reconcile (connection: IComposerWorker) session =
    let identity = ComposerJson.hostIdentity connection.Handshake
    let key = ComposerJson.text "host" identity, ComposerJson.text "epoch" identity, session
    let active () = lock gate (fun () -> isOwner connection && connection.IsAlive && not stopped && not retiring)
    let needed () = cached session |> Option.exists ComposerJson.needsReconciliation
    let admitted = lock gate (fun () -> if active () && needed () then monitors.Add key else false)
    if admitted then
      Task.Run(Func<Task>(fun () -> task {
        use deadline = new CancellationTokenSource(TimeSpan.FromMinutes 8.)
        try
          try
            let mutable polling = true
            while polling && active () && not deadline.IsCancellationRequested do
              do! Task.Delay(100, deadline.Token)
              if active () then
                let previous = cached session
                try
                  let! _ = refresh connection session deadline.Token
                  ()
                with _ -> ()
                let current = cached session
                match previous, current with
                | Some before, Some after when not (ComposerJson.sameState before after) -> changed ()
                | _ -> ()
                polling <- needed ()
              else polling <- false
          with :? OperationCanceledException -> ()
        finally
          lock gate (fun () -> monitors.Remove key |> ignore)
      })) |> ignore

  let retire request connection = task {
    let admitted = lock gate (fun () ->
      if stopped || retiring || not (isOwner connection) then false
      else
        retiring <- true
        true)
    if not admitted then return refusal request "provider_retiring" "The Composer worker is already retiring."
    else
      withdrawOwned connection "Compiler retirement requested."
      do! lifecycle.WaitAsync()
      let mutable exited = false
      try
        let! result = task {
          try
            return! (connection.RequestAsync("prepare_compiler_change", "", [], CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds 10.)
          with error -> return refusal request "retirement_failed" error.Message
        }
        // Reopening cannot race the old process, including after failed cleanup.
        do! connection.StopAsync()
        if connection.IsAlive then invalidOp "The retired Composer process did not exit."
        exited <- true
        lock gate (fun () -> if isOwner connection then worker <- None)
        return result
      finally
        // A failed process stop keeps the owner fenced. Never reopen against
        // an old process merely because the caller received a failure.
        if exited then lock gate (fun () -> retiring <- false)
        lifecycle.Release() |> ignore
        changed ()
  }

  member _.Changed = changedEvent.Publish
  member _.WorkerPid = lock gate (fun () -> worker |> Option.filter _.IsAlive |> Option.map _.ProcessId)

  member _.ExecuteAsync(request: ComposerRequest, cancellation: CancellationToken) = task {
    let allowed = set [ "open"; "reserve"; "build"; "status"; "run"; "cancel"; "close"; "prepare_compiler_change" ]
    if request.Provider <> "clef-composer" then
      return refusal request "wrong_provider" "Composer operations require provider clef-composer."
    elif not (Set.contains request.Operation allowed) then
      return refusal request "unsupported_operation" "Unknown Composer operation."
    else
      let mutable selectedOwner: IComposerWorker option = None
      let mutable resolvedSession = if request.Operation = "open" then "" else request.Session
      let mutable openDispatched = false
      let interrupted code message = task {
        match selectedOwner with
        | Some current when resolvedSession <> "" && current.IsAlive
                            && lock gate (fun () -> isOwner current && not stopped && not retiring) ->
          try
            let! _ = refresh current resolvedSession CancellationToken.None
            ()
          with _ -> ()
          reconcile current resolvedSession
        | _ -> ()
        changed ()
        return refusal { request with Session = resolvedSession } code message
      }
      try
        let! selected =
          if request.Operation = "open" then ensureWorker cancellation
          else Task.FromResult(lock gate (fun () ->
            match worker with
            | Some connection when not stopped && not retiring && connection.IsAlive -> Result.Ok connection
            | _ -> Result.Error "No active Composer worker owns this operation."))
        match selected with
        | Result.Error message -> return refusal request "provider_unavailable" message
        | Result.Ok connection ->
          selectedOwner <- Some connection
          let identity = ComposerJson.hostIdentity connection.Handshake
          let scopeMatches =
            request.Host = ComposerJson.text "host" identity && request.Epoch = ComposerJson.text "epoch" identity
          if request.Operation <> "open" && not scopeMatches then
            return refusal request "wrong_authority" "Use the owning host and epoch from the Composer session response."
          elif request.Operation = "prepare_compiler_change" then
            return! retire request connection
          elif request.Operation <> "open" && String.IsNullOrWhiteSpace request.Session then
            return refusal request "invalid_request" "An explicit Composer session is required."
          else
            let parameters = request.Parameters |> List.filter (fun (name, _) ->
              Set.contains name (set [ "project"; "label"; "reservation"; "arguments" ]))
            // Once an open is dispatched, finish registering its session even
            // if the requesting observer disconnects before the reply arrives.
            cancellation.ThrowIfCancellationRequested()
            let operationCancellation = if request.Operation = "open" then CancellationToken.None else cancellation
            openDispatched <- request.Operation = "open"
            let! response = connection.RequestAsync(request.Operation, request.Session, parameters, operationCancellation)
            let session = ComposerJson.text "session" (ComposerJson.field "authority" response)
            resolvedSession <- session
            let active = lock gate (fun () -> isOwner connection && not retiring && not stopped && connection.IsAlive)
            if not active then
              return ComposerJson.failure (ComposerJson.field "authority" response) "closed" "The response belongs to a retired worker."
            else
              if session <> "" then
                if request.Operation = "status" then
                  if ComposerJson.success response then remember connection (ComposerJson.projection true "" response)
                  else uncertain connection session "The worker refused its session status."
                else
                  if request.Operation = "open" && ComposerJson.success response then
                    remember connection (ComposerJson.projection false "Initial status has not yet been read." response)
                  elif ComposerJson.success response then
                    cached session |> Option.iter (fun previous ->
                      previous
                      |> ComposerJson.withAuthority (ComposerJson.field "authority" response)
                      |> ComposerJson.projection false "Session status requires refresh."
                      |> remember connection)
                  try
                    let! _ = refresh connection session CancellationToken.None
                    ()
                  with _ -> ()
                reconcile connection session
              let stillActive = lock gate (fun () -> isOwner connection && not retiring && not stopped && connection.IsAlive)
              if not stillActive then
                return ComposerJson.failure (ComposerJson.field "authority" response) "closed" "The response belongs to a retired worker."
              else
                if request.Operation <> "status" then changed ()
                return stale response |> Option.defaultValue response
      with
      | :? OperationCanceledException ->
        // The client has acknowledged exact-request cancellation before this
        // continuation. Refresh its withdrawn authority for every observer.
        return! interrupted "canceled" "The caller canceled this operation."
      | :? TimeoutException as error ->
        match selectedOwner with
        | Some connection when openDispatched && resolvedSession = "" ->
          // A late open may already own compiler state without a session ID
          // known to this daemon. Retire that exact process before returning;
          // neither a lost reply nor failed cleanup may leave an orphan owner.
          try
            let! _ = retire request connection
            return refusal request "timeout" (error.Message + " The worker was retired because the open outcome was unknown.")
          with retirementError ->
            return refusal request "retirement_failed" (error.Message + " Worker retirement failed: " + retirementError.Message)
        | _ -> return! interrupted "timeout" error.Message
      | error ->
        changed ()
        return refusal request "provider_unavailable" error.Message
  }

  member _.SessionsAsync(cancellation: CancellationToken) = task {
    let connection, owned = lock gate (fun () ->
      (if stopped || retiring then None else worker), snapshots.Keys |> Seq.toArray)
    match connection with
    | Some current when current.IsAlive ->
      let currentHost = ComposerJson.text "host" (ComposerJson.hostIdentity current.Handshake)
      let currentEpoch = ComposerJson.text "epoch" (ComposerJson.hostIdentity current.Handshake)
      let relevant = lock gate (fun () -> owned |> Array.filter (fun session ->
        let identity = ComposerJson.field "authority" snapshots[session]
        ComposerJson.text "host" identity = currentHost && ComposerJson.text "epoch" identity = currentEpoch))
      let pending =
        relevant |> Array.map (fun session -> task {
          try
            let! _ = refresh current session cancellation
            ()
          with _ -> ()
          reconcile current session
        })
      let! _ = Task.WhenAll pending
      ()
    | _ -> ()
    return lock gate (fun () ->
      ComposerJson.value {|
        protocolVersion = 1; provider = "clef-composer"; configured = configured; revision = revision
        worker = worker |> Option.filter _.IsAlive |> Option.map _.Handshake |> Option.defaultValue (ComposerJson.value (null: objnull))
        sessions = snapshots.Values |> Seq.toArray |})
  }

  member _.StopAsync() = task {
    let connection = lock gate (fun () -> stopped <- true; retiring <- true; worker)
    connection |> Option.iter (fun current -> withdrawOwned current "Daemon stopped.")
    do! lifecycle.WaitAsync()
    try
      match lock gate (fun () -> worker) with
      | Some current -> do! current.StopAsync()
      | None -> ()
      lock gate (fun () -> worker <- None)
    finally
      lifecycle.Release() |> ignore
  }

module ComposerSupervisor =
  let disabled () =
    ComposerSupervisor((fun () -> Task.FromException<IComposerWorker>(InvalidOperationException "Composer is not configured.")), false)

  let fromEnvironment () =
    let environment name = Environment.GetEnvironmentVariable name |> Option.ofObj |> Option.defaultValue ""
    let worker = environment "BOZZETTO_COMPOSER_WORKER"
    if String.IsNullOrWhiteSpace worker then disabled ()
    else
      let dotnet =
        match environment "DOTNET_HOST_PATH" with
        | value when not (String.IsNullOrWhiteSpace value) -> value
        | _ -> "dotnet"
      let cache =
        match environment "XDG_CACHE_HOME" with
        | value when not (String.IsNullOrWhiteSpace value) && Path.IsPathFullyQualified value -> value
        | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
      let factory () =
        ComposerWorkerClient.start {
          WorkerPath = worker; DotnetPath = dotnet
          EvidenceDirectory = Path.Combine(cache, "bozzetto", "composer-workers", Guid.NewGuid().ToString("N")) }
      ComposerSupervisor(factory, true)
