namespace Bozzetto.ComposerIntegration

open System
open System.Text.Json
open Bozzetto.Providers
open Bozzetto.Composer.Protocol

/// External MCP/browser representation. Never used on the worker socket or by
/// compiler authority decisions; that boundary consumes the closed typed contract.
module ComposerClientJson =
  let private options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
  let value item = JsonSerializer.SerializeToElement(item, options)
  let private empty = value {| |}
  let private nullableString item = item |> Option.toObj
  let private authority (item: Authority) =
    {| host = item.Host; session = item.Session; epoch = item.Epoch
       provider = ProviderIdentity.name item.Provider; generation = item.Generation |}
  let refusalCode = function
    | RefusalCode.InvalidRequest -> "invalid_request" | RefusalCode.ProtocolVersion -> "protocol_version"
    | RefusalCode.EncodingMismatch -> "encoding_mismatch" | RefusalCode.ContractMismatch -> "contract_mismatch"
    | RefusalCode.WrongAuthority -> "wrong_authority" | RefusalCode.WrongProvider -> "wrong_provider"
    | RefusalCode.InvalidProject -> "invalid_project" | RefusalCode.InvalidCache -> "invalid_cache"
    | RefusalCode.CompilerRetired -> "compiler_retired" | RefusalCode.UnknownSession -> "unknown_session"
    | RefusalCode.Closed -> "closed" | RefusalCode.UnsupportedOperation -> "unsupported_operation"
    | RefusalCode.RequestRefused -> "request_refused" | RefusalCode.DuplicateRequest -> "duplicate_request"
    | RefusalCode.CleanupFailed -> "cleanup_failed" | RefusalCode.Superseded -> "superseded"
    | RefusalCode.CompilerRefused -> "compiler_refused" | RefusalCode.Canceled -> "canceled"
    | RefusalCode.ObservationCapacity -> "observation_capacity" | RefusalCode.Busy -> "busy"
    | RefusalCode.SessionCapacity -> "session_capacity" | RefusalCode.BackendFailed -> "backend_failed"
    | RefusalCode.InvalidReservation -> "invalid_reservation" | RefusalCode.NotAccepted -> "not_accepted"
    | RefusalCode.FrameTooLarge -> "frame_too_large" | RefusalCode.MalformedPayload -> "malformed_payload"
    | RefusalCode.ProviderRetiring -> "provider_retiring" | RefusalCode.ProviderUnavailable -> "provider_unavailable"
    | RefusalCode.RetirementFailed -> "retirement_failed" | RefusalCode.Timeout -> "timeout"
  let operationName = function
    | Operation.Hello -> "hello" | Operation.Open -> "open" | Operation.Reserve -> "reserve"
    | Operation.Build -> "build" | Operation.Status -> "status" | Operation.Run -> "run"
    | Operation.Cancel -> "cancel" | Operation.CancelRequest -> "cancel_request"
    | Operation.Close -> "close" | Operation.PrepareCompilerChange -> "prepare_compiler_change"
    | Operation.Format -> "format"
  let private resultBody (projection: ComposerResponse) body =
    match body with
    | HelloAccepted hello ->
      value {| compilerAssembly = hello.Compiler.AssemblyPath; compilerSha256 = hello.Compiler.Sha256
               compilerVersion = hello.Compiler.Version; operations = Array.map operationName hello.Operations
               inMemoryPatchAllowed = hello.InMemoryPatchAllowed; transport = "barewire-unix-socket"
               workerProtocolVersion = hello.Agreement.ProtocolVersion; contractDigest = hello.Agreement.ContractDigest
               psg = hello.Psg |}
    | Opened item -> value item
    | Reserved item -> value item
    | Built item -> value item
    | Observed item ->
      value {| observation = item.Observation; project = item.Project; manifestPath = item.ManifestPath
               closed = item.Closed; busy = item.Busy
               current = item.Current |> Option.map value |> Option.defaultValue (value (null: objnull))
               revocationPending = item.RevocationPending; backendError = nullableString item.BackendError
               formatterError = nullableString item.FormatterError
               cleanupPending = item.CleanupPending; cleanupError = nullableString item.CleanupError
               statusFresh = projection.StatusFresh; statusError = nullableString projection.StatusError
               workerAvailable = projection.WorkerAvailable; workerError = nullableString projection.WorkerError
               executionRequiresRevalidation = true |}
    | Ran item -> value item
    | Canceled -> empty
    | RequestCanceled item -> value item
    | Closed item -> value {| observation = item.Observation; closed = item.Closed; cleanupPending = item.CleanupPending; cleanupError = nullableString item.CleanupError |}
    | CompilerRetired item -> value item
    | Formatted item -> value item
  let reply projection =
    let result, error =
      match projection.Reply.Outcome with
      | Result.Ok body -> resultBody projection body, empty
      | Result.Error refusal -> empty, value {| code = refusalCode refusal.Code; message = refusal.Message |}
    // Public client envelope remains version 1; worker agreement is independent.
    value {| protocolVersion = 1; requestId = projection.Reply.RequestId
             authority = authority projection.Reply.Authority; success = Result.isOk projection.Reply.Outcome
             result = result; error = error |}
  let wireReply item = ComposerState.response item |> reply
  let directory (item: ComposerDirectory) =
    value {| protocolVersion = 1; provider = "clef-composer"; configured = item.Configured; revision = item.Revision
             worker = item.Worker |> Option.map wireReply |> Option.defaultValue (value (null: objnull))
             sessions = Array.map reply item.Sessions |}
  let request operation (body: JsonElement) =
    let text name =
      match body.TryGetProperty(name: string) with
      | true, item when item.ValueKind = JsonValueKind.String -> item.GetString() |> Option.ofObj |> Option.defaultValue ""
      | false, _ -> ""
      | _ -> invalidArg "body" (name + " must be a string.")
    let signed name =
      match body.TryGetProperty(name: string) with
      | true, item when item.ValueKind = JsonValueKind.Number ->
        match item.TryGetInt64() with true, value -> value | _ -> invalidArg "body" (name + " must be an int64.")
      | _ -> invalidArg "body" (name + " must be an int64.")
    let unsigned name =
      match body.TryGetProperty(name: string) with
      | true, item when item.ValueKind = JsonValueKind.Number ->
        match item.TryGetUInt64() with true, value -> value | _ -> invalidArg "body" (name + " must be a uint64.")
      | _ -> invalidArg "body" (name + " must be a uint64.")
    try
      if body.ValueKind <> JsonValueKind.Object then invalidArg "body" "Request must be an object."
      let provider = match text "provider" with "" | "clef-composer" -> ProviderIdentity.ClefComposer | _ -> ProviderIdentity.FSharp
      if provider <> ProviderIdentity.ClefComposer then Result.Error(RefusalCode.WrongProvider, "Use provider clef-composer.")
      else
        let worker = { Host = text "host"; Epoch = text "epoch"; Provider = provider }
        let session = { Worker = worker; Session = text "session" }
        match operation with
        | "open" -> Result.Ok(Open(worker, text "project"))
        | "reserve" -> Result.Ok(Reserve(session, text "label"))
        | "build" -> Result.Ok(Build(session, text "reservation"))
        | "status" -> Result.Ok(Status session)
        | "run" ->
          let arguments =
            match body.TryGetProperty "arguments" with
            | false, _ -> [||]
            | true, items when items.ValueKind = JsonValueKind.Array ->
              items.EnumerateArray() |> Seq.map (fun item ->
                if item.ValueKind <> JsonValueKind.String then invalidArg "body" "arguments must contain strings."
                item.GetString() |> Option.ofObj |> Option.defaultValue "") |> Seq.toArray
            | _ -> invalidArg "body" "arguments must be an array."
          Result.Ok(Run(session, arguments))
        | "cancel" -> Result.Ok(Cancel session)
        | "close" -> Result.Ok(Close session)
        | "prepare_compiler_change" -> Result.Ok(PrepareCompilerChange worker)
        | "format" ->
          if not (fst (body.TryGetProperty "source")) then invalidArg "body" "source is required."
          Result.Ok(Format(session, signed "generation", {
            Document = text "document"; Incarnation = text "incarnation"; Revision = unsigned "revision"
            Source = text "source"; Configuration = text "configuration" }))
        | _ -> Result.Error(RefusalCode.UnsupportedOperation, "Unknown Composer operation.")
    with :? ArgumentException as error -> Result.Error(RefusalCode.InvalidRequest, error.Message)
