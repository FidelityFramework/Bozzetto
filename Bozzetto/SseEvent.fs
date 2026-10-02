namespace Bozzetto.Server

open Fidelity.Data.JSON
open Bozzetto

/// The SSE `event:` name a case rides on. Editor clients (the in-repo VS Code
/// extension, and the external sagefs.nvim plugin) dispatch on this outer
/// name — pinned by SseParityTests — so it stays "state"/"session" even
/// though the F# side now has ONE type instead of two (roast-5 §1).
[<RequireQualifiedAccess>]
type SseChannel =
  | State
  | Session

/// Unified SSE event vocabulary for daemon -> editor/dashboard push
/// notifications. Replaces the formerly-separate DaemonStateChange
/// ("state" channel) and SessionEvents.SessionEvent ("session" channel)
/// types: the same worker occurrence (e.g. a session-ready notification) used to be
/// represented by two unrelated DUs, serialized by two different ad hoc
/// techniques (sprintf string-templating vs. a hand-rolled Utf8JsonWriter),
/// and SseWriter had to track "two event types" separately. Now there is one
/// DU, one classifier (`SseEvent.channel`), and one serializer
/// (`SseEvent.toJson`).
type SseEvent =
  // ── State channel (was DaemonStateChange) — thin, mostly-global notifications ──
  | SessionProgress
  | SessionReady of sessionId: WorkerProtocol.SessionId
  | SessionSwitched of sessionId: WorkerProtocol.SessionId
  | FileReloaded of sessionId: WorkerProtocol.SessionId * path: string
  | SessionFaulted of sessionId: WorkerProtocol.SessionId * error: string
  | ModelChanged of outputCount: int * diagCount: int
  | WarmupProgress of sessionId: WorkerProtocol.SessionId * step: int * total: int * message: string
  | SystemAlarm of phase: string * message: string
  /// The daemon's cohort changed: someone joined, left, claimed, released or
  /// landed. Global like `SystemAlarm`. The dashboard needs it to redraw the
  /// cohort panel, which only shows while members are present; before this
  /// nothing pushed on a cohort change, so the panel went stale until some
  /// unrelated event happened to arrive.
  | CohortChanged
  // ── Session channel (was SessionEvents.SessionEvent) — rich, session-scoped snapshots ──
  | WarmupContextSnapshot of sessionId: string * context: WarmupContext
  | SessionActivated of sessionId: string
  | SessionCreated of sessionId: string * projectNames: string list
  | SessionStopped of sessionId: string
  | WorkflowSwitching of sessionId: string * fromLabel: string * toLabel: string
  | WorkflowSwitched of sessionId: string * label: string
  /// A session's `SessionHealth` verdict (`Bozzetto.SessionHealth.classify`)
  /// changed since the last one pushed for it — the SSE half of roast-8 §1:
  /// `/health` and `/api/sessions` compute this verdict on every GET, but
  /// nothing pushed it, so a session going Healthy -> Degraded mid-session
  /// was invisible to every connected client until an unrelated refetch.
  /// Session-scoped like `WarmupContextSnapshot`, so it
  /// rides the "session" channel and gets the same connect-time replay.
  | SessionHealthChanged of sessionId: string * health: SessionHealth

module SseEvent =
  /// Which SSE channel a case rides on. Exhaustive — a new case must be
  /// classified here before it compiles, so the channel split can never
  /// silently drift out of sync with the case list again.
  let channel = function
    | SessionProgress
    | SessionReady _
    | SessionSwitched _
    | FileReloaded _
    | SessionFaulted _
    | ModelChanged _
    | WarmupProgress _
    | SystemAlarm _
    | CohortChanged -> SseChannel.State
    | WarmupContextSnapshot _
    | SessionActivated _
    | SessionCreated _
    | SessionStopped _
    | WorkflowSwitching _
    | WorkflowSwitched _
    | SessionHealthChanged _ -> SseChannel.Session

  let channelName = function
    | SseChannel.State -> "state"
    | SseChannel.Session -> "session"

  /// SSE `event:` name for a given event. Single lookup — replaces the two
  /// separately-tracked sseEventType/sessionEventType constants.
  let sseEventType (evt: SseEvent) = evt |> channel |> channelName

  /// Preserved for the existing wire-contract name ("state" channel only).
  let sseEventTypeState = channelName SseChannel.State
  /// Preserved for the existing wire-contract name ("session" channel only).
  let sseEventTypeSession = channelName SseChannel.Session

  let private sid (s: WorkerProtocol.SessionId) = WorkerProtocol.SessionId.value s |> JsonValue.String
  let private integer value = JsonValue.ofInt64 (int64 value)
  let private strings values = values |> List.map JsonValue.String |> JsonValue.Array

  let private sessionHealthJson (health: SessionHealth) =
    JsonValue.Object [
      "status", JsonValue.String (SessionHealth.label health)
      match SessionHealth.reason health with
      | Some reason -> "reason", JsonValue.String reason
      | None -> ()
    ]

  let private diagnosticJson (d: WarmUp.WarmupFcsDiagnostic) =
    JsonValue.Object [
      "message", JsonValue.String d.Message
      "severity", JsonValue.String d.Severity
      "errorNumber", integer d.ErrorNumber
      match d.FileName with
      | Some name -> "fileName", JsonValue.String name
      | None -> ()
      "startLine", integer d.StartLine
      "endLine", integer d.EndLine
      "startColumn", integer d.StartColumn
      "endColumn", integer d.EndColumn
    ]

  let private failedOpenJson (f: WarmUp.WarmupOpenFailure) =
    JsonValue.Object [
      "name", JsonValue.String f.Name
      "isModule", JsonValue.Bool (WarmUp.OpenableKind.toBool f.Kind)
      "error", JsonValue.String f.ErrorMessage
      "retryCount", integer f.RetryCount
      "diagnostics", JsonValue.Array (List.map diagnosticJson f.Diagnostics)
    ]

  let private namespaceOpenedJson (b: WarmUp.OpenedBinding) =
    JsonValue.Object [
      "name", JsonValue.String b.Name
      "isModule", JsonValue.Bool (WarmUp.OpenableKind.toBool b.Kind)
      "source", JsonValue.String b.Source
      "durationMs", JsonValue.Number b.DurationMs
    ]

  let private assemblyLoadedJson (a: LoadedAssembly) =
    JsonValue.Object [
      "name", JsonValue.String a.Name
      "path", JsonValue.String a.Path
      "namespaceCount", integer a.NamespaceCount
      "moduleCount", integer a.ModuleCount
    ]

  let private warmupContextJson (ctx: WarmupContext) =
    JsonValue.Object [
      "sourceFilesScanned", integer ctx.SourceFilesScanned
      "warmupDurationMs", JsonValue.ofInt64 (WarmupContext.totalDurationMs ctx)
      "phaseTiming", JsonValue.Object [
        "scanSourceFilesMs", JsonValue.ofInt64 ctx.PhaseTiming.ScanSourceFilesMs
        "scanAssembliesMs", JsonValue.ofInt64 ctx.PhaseTiming.ScanAssembliesMs
        "openNamespacesMs", JsonValue.ofInt64 ctx.PhaseTiming.OpenNamespacesMs
        "totalMs", JsonValue.ofInt64 ctx.PhaseTiming.TotalMs
      ]
      "assembliesLoaded", JsonValue.Array (List.map assemblyLoadedJson ctx.AssembliesLoaded)
      "namespacesOpened", JsonValue.Array (List.map namespaceOpenedJson ctx.NamespacesOpened)
      "failedOpens", JsonValue.Array (List.map failedOpenJson ctx.FailedOpens)
    ]

  /// Each event owns its explicit wire schema. Null optional fields remain
  /// omitted for compatibility with the existing SSE consumers.
  let toJson (evt: SseEvent) : string =
    let session kind id fields =
      ("type", JsonValue.String kind) :: ("sessionId", JsonValue.String id) :: fields
    let fields =
      match evt with
      | SessionProgress -> [ "sessionProgress", JsonValue.Bool true ]
      | SessionReady s -> [ "sessionReady", sid s ]
      | SessionSwitched s -> [ "sessionSwitched", sid s ]
      | FileReloaded (s, path) -> [ "fileReloaded", JsonValue.String path; "sessionId", sid s ]
      | SessionFaulted (s, error) -> [ "sessionFaulted", sid s; "error", JsonValue.String error ]
      | ModelChanged (outputCount, diagCount) -> [ "outputCount", integer outputCount; "diagCount", integer diagCount ]
      | WarmupProgress (s, step, total, _msg) ->
        [ "warmupProgress", JsonValue.Bool true; "sessionId", sid s; "step", integer step; "total", integer total ]
      | SystemAlarm (phase, message) ->
        [ "systemAlarm", JsonValue.Bool true; "phase", JsonValue.String phase; "message", JsonValue.String message ]
      | CohortChanged -> [ "cohortChanged", JsonValue.Bool true ]
      | WarmupContextSnapshot (s, ctx) -> session "warmup_context_snapshot" s [ "context", warmupContextJson ctx ]
      | SessionActivated s -> session "session_activated" s []
      | SessionCreated (s, projectNames) -> session "session_created" s [ "projectNames", strings projectNames ]
      | SessionStopped s -> session "session_stopped" s []
      | WorkflowSwitching (s, fromLabel, toLabel) ->
        session "workflow_switching" s [ "fromWorkflow", JsonValue.String fromLabel; "toWorkflow", JsonValue.String toLabel ]
      | WorkflowSwitched (s, label) -> session "workflow_switched" s [ "workflowLabel", JsonValue.String label ]
      | SessionHealthChanged (s, health) -> session "session_health_changed" s [ "health", sessionHealthJson health ]
    JsonValue.Object fields |> Json.serialize

  /// Format a complete SSE frame (`event: ...\ndata: ...\n\n`) for an event.
  let format (evt: SseEvent) : string =
    SseWriter.formatSseEvent (sseEventType evt) (toJson evt)
