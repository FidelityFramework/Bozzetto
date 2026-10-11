module Bozzetto.Composer.WorkerProtocol

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Bozzetto.Providers
open Bozzetto.Composer.Protocol

let private typedRefusal (error: Bozzetto.Providers.Refusal) : Bozzetto.Composer.Protocol.Refusal =
  let code =
    match error.Code with
    | "invalid_request" -> RefusalCode.InvalidRequest
    | "closed" -> RefusalCode.Closed
    | "cleanup_failed" -> RefusalCode.CleanupFailed
    | "superseded" -> RefusalCode.Superseded
    | "compiler_refused" -> RefusalCode.CompilerRefused
    | "canceled" -> RefusalCode.Canceled
    | "observation_capacity" -> RefusalCode.ObservationCapacity
    | "busy" -> RefusalCode.Busy
    | "session_capacity" -> RefusalCode.SessionCapacity
    | "backend_failed" -> RefusalCode.BackendFailed
    | "invalid_reservation" -> RefusalCode.InvalidReservation
    | "not_accepted" -> RefusalCode.NotAccepted
    | _ -> RefusalCode.RequestRefused
  { Code = code; Message = error.Code + ": " + error.Message }

/// The worker owns all compiler tickets. The transport copies only closed data.
type Worker<'Ticket>(root: string, createBackend: string -> string -> IProjectBackend<'Ticket>, describeCompiler: unit -> CompilerIdentity, ?cancelRequest: (string -> bool), ?createFormatter: (unit -> IFormatBackend), ?formatterCleanupTimestamp: (unit -> int64)) =
  let gate = obj ()
  let host, epoch = Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")
  let sessions = Collections.Generic.Dictionary<string, ProviderSession<'Ticket>>()
  let opening = Collections.Generic.Dictionary<string, Task<Result<unit, Bozzetto.Providers.Refusal>>>()
  let mutable retired = false
  let mutable agreed = false
  let cancelRequest = defaultArg cancelRequest (fun _ -> false)
  let hostIdentity: Authority =
    { Host = host; Session = ""; Epoch = epoch; Provider = ProviderIdentity.ClefComposer; Generation = 0L }
  let response id identity outcome : Bozzetto.Composer.Protocol.Reply =
    { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = id; Authority = identity; Outcome = outcome }
  let rejectAt id identity code message = response id identity (Result.Error { Code = code; Message = message })
  let reject id code message = rejectAt id hostIdentity code message
  let convert id map (reply: Bozzetto.Providers.Reply<'a>) =
    response id reply.Authority (reply.Outcome |> Result.map map |> Result.mapError typedRefusal)
  let target = function
    | Hello _ -> None
    | Open(target, _) | CancelRequest(target, _) | PrepareCompilerChange target -> Some target
    | Reserve(target, _) | Build(target, _) | Run(target, _) | Format(target, _, _)
    | Status target | Cancel target | Close target -> Some target.Worker
  let sessionAddress = function
    | Reserve(target, _) | Build(target, _) | Run(target, _) | Format(target, _, _)
    | Status target | Cancel target | Close target -> Some target
    | _ -> None

  member _.RejectFrame(id, code, message) = reject id code message

  member private _.BeginRetirement() =
    lock gate (fun () ->
      retired <- true
      let owned = sessions.Values |> Seq.toArray
      for session in owned do session.BeginClose() |> ignore
      owned, opening.Values |> Seq.toArray)

  member this.RetireAsync() : Task<Result<unit, Bozzetto.Providers.Refusal>> = task {
    let owned, pendingOpens = this.BeginRetirement()
    let cleanup = owned |> Array.map (fun session -> task {
      try
        let! reply = session.CloseAsync()
        return reply.Outcome |> Result.mapError (fun error ->
          { error with Message = session.Identity.Session + ": " + error.Message })
      with error ->
        return Result.Error ({ Code = "cleanup_failed"; Message = session.Identity.Session + ": " + error.Message }: Bozzetto.Providers.Refusal)
    })
    let! results = Task.WhenAll(Array.append cleanup pendingOpens)
    let failures = results |> Array.choose (function Result.Error error -> Some error.Message | _ -> None)
    if failures.Length = 0 then return Result.Ok ()
    else return Result.Error ({ Code = "cleanup_failed"; Message = "All sessions are retired; cleanup failed: " + String.concat "; " failures }: Bozzetto.Providers.Refusal)
  }

  member this.Handle(request: Request, ?cancellation: CancellationToken) = task {
    let cancellation = defaultArg cancellation CancellationToken.None
    let id = request.RequestId
    let mutable resolvedAuthority = hostIdentity
    try
      if String.IsNullOrWhiteSpace id then return reject id RefusalCode.InvalidRequest "requestId is required."
      elif request.ProtocolVersion <> BAREWireCodec.ProtocolVersion then
        return reject id RefusalCode.ProtocolVersion "This worker accepts only binary protocol version 2."
      else
        match request.Body with
        | Hello agreement ->
          if agreement.ProtocolVersion <> BAREWireCodec.ProtocolVersion then
            return reject id RefusalCode.ProtocolVersion "Handshake version differs."
          elif agreement.Encoding <> EncodingId.BAREWire1 then
            return reject id RefusalCode.EncodingMismatch "Handshake encoding differs."
          elif agreement.ContractDigest <> BAREWireCodec.ContractDigest then
            return reject id RefusalCode.ContractMismatch "Handshake contract digest differs."
          else
            let assembly = typeof<Fidelity.PSG.Revision>.Assembly
            let digest = File.ReadAllBytes assembly.Location |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
            let result = response id hostIdentity (Result.Ok(HelloAccepted {
              Agreement = BAREWireCodec.agreement; Compiler = describeCompiler ()
              Psg = { Schema = Fidelity.PSG.Revision.Schema; AssemblySha256 = digest
                      FormatVersion = Fidelity.PSG.Binary.FormatVersion; ContractFingerprint = Fidelity.PSG.Binary.ContractFingerprint }
              Operations = [| Operation.Hello; Operation.Open; Operation.Reserve; Operation.Build; Operation.Status
                              Operation.Run; Operation.Cancel; Operation.CancelRequest; Operation.Close
                              Operation.PrepareCompilerChange; Operation.Format |]
              InMemoryPatchAllowed = false }))
            lock gate (fun () -> agreed <- true)
            return result
        | body ->
          match target body with
          | None -> return reject id RefusalCode.InvalidRequest "Request lacks a worker address."
          | Some _ when not (lock gate (fun () -> agreed)) ->
            return reject id RefusalCode.ProtocolVersion "Complete the exact binary handshake first."
          | Some address when address.Host <> host || address.Epoch <> epoch ->
            return reject id RefusalCode.WrongAuthority "Use the host and epoch returned by hello."
          | Some address when address.Provider <> ProviderIdentity.ClefComposer ->
            return reject id RefusalCode.WrongProvider "This worker accepts only the Clef/Composer provider."
          | Some _ ->
            match body with
            | CancelRequest(_, targetId) ->
              if String.IsNullOrWhiteSpace targetId then return reject id RefusalCode.InvalidRequest "targetRequestId is required."
              else return response id hostIdentity (Result.Ok(RequestCanceled { TargetRequestId = targetId; CancellationRequested = cancelRequest targetId }))
            | PrepareCompilerChange _ ->
              let! result = this.RetireAsync()
              return response id hostIdentity (result |> Result.map (fun () -> CompilerRetired { RestartRequired = true; InMemoryPatchAllowed = false }) |> Result.mapError typedRefusal)
            | Open(_, project) ->
              if String.IsNullOrWhiteSpace project || not (Path.IsPathFullyQualified project) then
                return reject id RefusalCode.InvalidProject "An absolute .fidproj path is required."
              elif not (project.EndsWith(".fidproj", StringComparison.OrdinalIgnoreCase)) || not (File.Exists project) then
                return reject id RefusalCode.InvalidProject "Open requires an existing .fidproj file."
              elif Path.GetDirectoryName(Path.GetFullPath project) |> Option.ofObj |> Option.exists (fun directory ->
                Path.GetFullPath(root) = directory || Path.GetFullPath(root).StartsWith(directory + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) then
                return reject id RefusalCode.InvalidCache "Provider scratch must reside outside the project directory."
              else
                let sessionId = Guid.NewGuid().ToString("N")
                let completion = TaskCompletionSource<Result<unit, Bozzetto.Providers.Refusal>>(TaskCreationOptions.RunContinuationsAsynchronously)
                let admitted = lock gate (fun () ->
                  if retired then false
                  else
                    opening.Add(sessionId, completion.Task)
                    true)
                if not admitted then return reject id RefusalCode.CompilerRetired "Start a fresh worker before accepting compiler work."
                else
                  let mutable cleanup = Result.Ok ()
                  try
                    try
                      let backend = createBackend (Path.GetFullPath project) (Path.Combine(root, host, sessionId, epoch))
                      let session = new ProviderSession<'Ticket>(host, sessionId, epoch, Path.GetFullPath project, backend,
                        ?formatter = (createFormatter |> Option.map (fun create -> create ())),
                        ?formatterCleanupTimestamp = formatterCleanupTimestamp)
                      let installed = lock gate (fun () ->
                        if retired then false
                        else
                          sessions.Add(sessionId, session)
                          true)
                      if installed then
                        return session.Status() |> convert id (fun status -> Opened { Observation = status.Observation; Project = status.Project; ManifestPath = status.ManifestPath })
                      else
                        session.BeginClose() |> ignore
                        let! closed = session.CloseAsync()
                        cleanup <- closed.Outcome
                        return reject id RefusalCode.CompilerRetired "Compiler retirement overtook this project open."
                    with error ->
                      cleanup <- Result.Error { Code = "cleanup_failed"; Message = "Opening " + sessionId + ": " + error.Message }
                      return reject id RefusalCode.RequestRefused error.Message
                  finally
                    completion.TrySetResult cleanup |> ignore
                    match cleanup with
                    | Result.Ok () -> lock gate (fun () -> opening.Remove sessionId |> ignore)
                    | Result.Error _ -> ()
            | _ ->
              let found, isRetired = lock gate (fun () ->
                let found = sessionAddress body |> Option.bind (fun address ->
                  match sessions.TryGetValue address.Session with true, session -> Some session | _ -> None)
                found, retired)
              match found with
              | None -> return reject id RefusalCode.UnknownSession "The session is not owned by this worker."
              | Some session ->
                // The status monitor drives deadline observation. Any arriving
                // session request also seals the entire worker before dispatch
                // once the owner reports an expired formatter cleanup.
                let cleanupExpired = session.WorkerRetirementRequired.IsSome
                if cleanupExpired then this.BeginRetirement() |> ignore
                let isRetired = isRetired || cleanupExpired
                resolvedAuthority <- session.Identity
                match body with
                | Reserve _ | Build _ | Run _ | Cancel _ | Format _ when isRetired ->
                  return rejectAt id session.Identity RefusalCode.Closed "The compiler worker is retired."
                | Reserve(_, label) ->
                  let! reply = session.ReserveAsync label
                  return convert id (fun token -> Reserved { Reservation = token }) reply
                | Build(_, token) ->
                  let! reply = session.BuildAsync(token, cancellation)
                  return convert id Built reply
                | Run(_, arguments) ->
                  let! reply = session.RunAsync(Array.toList arguments, cancellation)
                  return convert id Ran reply
                | Format(_, generation, buffer) ->
                  let! reply = session.FormatAsync(generation, buffer, cancellation)
                  return convert id Formatted reply
                | Status _ -> return session.Status() |> convert id Observed
                | Cancel _ -> return session.Cancel() |> convert id (fun () -> Canceled)
                | Close _ ->
                  let closing = session.CloseAsync()
                  if closing.IsCompleted then
                    let! reply = closing
                    return convert id (fun () -> Closed { Observation = 0UL; Closed = true; CleanupPending = false; CleanupError = None }) reply
                  else
                    return session.Status() |> convert id (fun status -> Closed {
                      Observation = status.Observation; Closed = status.Closed; CleanupPending = status.CleanupPending; CleanupError = status.CleanupError })
                | _ -> return rejectAt id session.Identity RefusalCode.UnsupportedOperation "Operation is not supported."
    with error -> return rejectAt id resolvedAuthority RefusalCode.RequestRefused error.Message
  }

  interface IDisposable with
    member this.Dispose() = this.BeginRetirement() |> ignore
