namespace Bozzetto.ComposerIntegration

open Fidelity.Data.JSON
open Bozzetto.Providers
open Bozzetto.Composer.Protocol

/// External MCP/browser representation. Never used on the worker socket or by
/// compiler authority decisions; that boundary consumes the closed typed contract.
/// Explicit values keep the public schema independent of CLR record reflection.
module ComposerClientJson =
  let private empty = JsonValue.Object []
  let private text = JsonValue.String
  let private flag = JsonValue.Bool
  let private signed = JsonValue.ofInt64
  let private unsigned = JsonValue.ofUInt64
  let private strings values = values |> Array.toList |> List.map text |> JsonValue.Array
  let private optional project = Option.map project >> Option.defaultValue JsonValue.Null
  let private authority (item: Authority) =
    JsonValue.Object [
      "host", text item.Host; "session", text item.Session; "epoch", text item.Epoch
      "provider", text (ProviderIdentity.name item.Provider); "generation", signed item.Generation
    ]
  let private artifact (item: AcceptedArtifact) =
    JsonValue.Object [
      "generation", signed item.Generation; "sourceVersion", text item.SourceVersion
      "artifactPath", text item.ArtifactPath; "artifactSha256", text item.ArtifactSha256
      "objectManifest", text item.ObjectManifest
      "changedWitnesses", strings item.ChangedWitnesses; "retainedWitnesses", strings item.RetainedWitnesses
      "retiredWitnesses", strings item.RetiredWitnesses
      "witnessVisits", JsonValue.Array [
        for name, count in item.WitnessVisits do
          // Preserve the established tuple projection in the public JSON schema.
          JsonValue.Object [ "item1", text name; "item2", signed (int64 count) ]
      ]
      "compiledObjects", strings item.CompiledObjects; "reusedObjects", strings item.ReusedObjects
      "retiredObjects", strings item.RetiredObjects
    ]
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
      JsonValue.Object [
        "compilerAssembly", text hello.Compiler.AssemblyPath; "compilerSha256", text hello.Compiler.Sha256
        "compilerVersion", text hello.Compiler.Version
        "operations", hello.Operations |> Array.map operationName |> strings
        "inMemoryPatchAllowed", flag hello.InMemoryPatchAllowed; "transport", text "barewire-unix-socket"
        "workerProtocolVersion", signed (int64 hello.Agreement.ProtocolVersion)
        "contractDigest", text hello.Agreement.ContractDigest
        "psg", JsonValue.Object [
          "schema", signed (int64 hello.Psg.Schema); "assemblySha256", text hello.Psg.AssemblySha256
          "formatVersion", unsigned (uint64 hello.Psg.FormatVersion); "contractFingerprint", text hello.Psg.ContractFingerprint
        ]
      ]
    | Opened item ->
      JsonValue.Object [
        "observation", unsigned item.Observation; "project", text item.Project; "manifestPath", text item.ManifestPath
      ]
    | Reserved item -> JsonValue.Object [ "reservation", text item.Reservation ]
    | Built item -> artifact item
    | Observed item ->
      JsonValue.Object [
        "observation", unsigned item.Observation; "project", text item.Project; "manifestPath", text item.ManifestPath
        "closed", flag item.Closed; "busy", flag item.Busy; "current", optional artifact item.Current
        "revocationPending", flag item.RevocationPending; "backendError", optional text item.BackendError
        "formatterError", optional text item.FormatterError; "formatterCleanupPending", flag item.FormatterCleanupPending
        "workerRetirementRequired", optional text item.WorkerRetirementRequired
        "cleanupPending", flag item.CleanupPending; "cleanupError", optional text item.CleanupError
        "statusFresh", flag projection.StatusFresh; "statusError", optional text projection.StatusError
        "workerAvailable", flag projection.WorkerAvailable; "workerError", optional text projection.WorkerError
        "executionRequiresRevalidation", flag true
      ]
    | Ran item ->
      JsonValue.Object [
        "generation", signed item.Generation; "sourceVersion", text item.SourceVersion; "exitCode", signed (int64 item.ExitCode)
        "standardOutput", text item.StandardOutput; "standardError", text item.StandardError
      ]
    | Canceled -> empty
    | RequestCanceled item ->
      JsonValue.Object [ "targetRequestId", text item.TargetRequestId; "cancellationRequested", flag item.CancellationRequested ]
    | Closed item ->
      JsonValue.Object [
        "observation", unsigned item.Observation; "closed", flag item.Closed
        "cleanupPending", flag item.CleanupPending; "cleanupError", optional text item.CleanupError
      ]
    | CompilerRetired item ->
      JsonValue.Object [ "restartRequired", flag item.RestartRequired; "inMemoryPatchAllowed", flag item.InMemoryPatchAllowed ]
    | Formatted item ->
      JsonValue.Object [
        "document", text item.Document; "incarnation", text item.Incarnation; "revision", unsigned item.Revision
        "sourceSha256", text item.SourceSha256; "configuration", text item.Configuration
        "formatterIdentity", text item.FormatterIdentity; "formatted", text item.Formatted
      ]
  let private envelope requestId owner success result error =
    JsonValue.Object [
      "protocolVersion", signed 1L; "requestId", text requestId; "authority", authority owner
      "success", flag success; "result", result; "error", error
    ]
  let reply projection =
    let result, error =
      match projection.Reply.Outcome with
      | Result.Ok body -> resultBody projection body, empty
      | Result.Error refusal -> empty, JsonValue.Object [ "code", text (refusalCode refusal.Code); "message", text refusal.Message ]
    // Public client envelope remains version 1; worker agreement is independent.
    envelope projection.Reply.RequestId projection.Reply.Authority (Result.isOk projection.Reply.Outcome) result error
  let wireReply item = ComposerState.response item |> reply
  let framingError code message =
    envelope "" { Host = ""; Session = ""; Epoch = ""; Provider = ProviderIdentity.ClefComposer; Generation = 0L }
      false empty (JsonValue.Object [ "code", text code; "message", text message ])
  let directory (item: ComposerDirectory) =
    JsonValue.Object [
      "protocolVersion", signed 1L; "provider", text "clef-composer"; "configured", flag item.Configured
      "revision", signed item.Revision; "worker", optional wireReply item.Worker
      "sessions", item.Sessions |> Array.toList |> List.map reply |> JsonValue.Array
    ]
  let request operation body =
    // The last occurrence wins, matching the existing browser request contract.
    let property name =
      match body with
      | JsonValue.Object pairs -> pairs |> List.rev |> List.tryFind (fun (key, _) -> key = name) |> Option.map snd
      | _ -> None
    let stringField name =
      match property name with
      | Some(JsonValue.String value) -> Result.Ok value
      | None -> Result.Ok ""
      | _ -> Result.Error(name + " must be a string.")
    let rec readStrings fields =
      match fields with
      | [] -> Result.Ok Map.empty
      | name :: remaining ->
        match stringField name, readStrings remaining with
        | Result.Ok value, Result.Ok values -> Result.Ok(Map.add name value values)
        | Result.Error error, _ | _, Result.Error error -> Result.Error error
    let integerField name extract kind =
      match property name |> Option.bind extract with
      | Some value -> Result.Ok value
      | None -> Result.Error(name + " must be a " + kind + ".")
    let readArguments () =
      match property "arguments" with
      | None -> Result.Ok [||]
      | Some(JsonValue.Array values) ->
        let collect state value =
          match state, value with
          | Result.Ok arguments, JsonValue.String argument -> Result.Ok(argument :: arguments)
          | Result.Error error, _ -> Result.Error error
          | _ -> Result.Error "arguments must contain strings."
        values |> List.fold collect (Result.Ok []) |> Result.map (List.rev >> List.toArray)
      | _ -> Result.Error "arguments must be an array."
    let specificFields =
      match operation with
      | "open" -> [ "project" ]
      | "reserve" -> [ "label" ]
      | "build" -> [ "reservation" ]
      | "format" -> [ "document"; "incarnation"; "source"; "configuration" ]
      | _ -> []
    match body with
    | JsonValue.Object _ ->
      match readStrings ([ "provider"; "host"; "epoch"; "session" ] @ specificFields) with
      | Result.Error error -> Result.Error(RefusalCode.InvalidRequest, error)
      | Result.Ok fields ->
        let field name = Map.find name fields
        let provider = field "provider"
        if provider <> "" && provider <> "clef-composer" then Result.Error(RefusalCode.WrongProvider, "Use provider clef-composer.")
        else
          let worker = { Host = field "host"; Epoch = field "epoch"; Provider = ProviderIdentity.ClefComposer }
          let session = { Worker = worker; Session = field "session" }
          match operation with
          | "open" -> Result.Ok(Open(worker, field "project"))
          | "reserve" -> Result.Ok(Reserve(session, field "label"))
          | "build" -> Result.Ok(Build(session, field "reservation"))
          | "status" -> Result.Ok(Status session)
          | "run" -> readArguments () |> Result.map (fun arguments -> Run(session, arguments)) |> Result.mapError (fun error -> RefusalCode.InvalidRequest, error)
          | "cancel" -> Result.Ok(Cancel session)
          | "close" -> Result.Ok(Close session)
          | "prepare_compiler_change" -> Result.Ok(PrepareCompilerChange worker)
          | "format" ->
            if property "source" |> Option.isNone then Result.Error(RefusalCode.InvalidRequest, "source is required.")
            else
              match integerField "generation" JsonValue.tryAsInt64 "int64", integerField "revision" JsonValue.tryAsUInt64 "uint64" with
              | Result.Ok generation, Result.Ok revision ->
                Result.Ok(Format(session, generation, {
                  Document = field "document"; Incarnation = field "incarnation"; Revision = revision
                  Source = field "source"; Configuration = field "configuration" }))
              | Result.Error error, _ | _, Result.Error error -> Result.Error(RefusalCode.InvalidRequest, error)
          | _ -> Result.Error(RefusalCode.UnsupportedOperation, "Unknown Composer operation.")
    | _ -> Result.Error(RefusalCode.InvalidRequest, "Request must be an object.")
