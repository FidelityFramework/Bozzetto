namespace Bozzetto

open Microsoft.Extensions.Logging
open Fidelity.Data.JSON

[<RequireQualifiedAccess>]
type BuildDiagnosticSeverity =
  | Blocking
  | Warning

/// One line of `dotnet build` output classified as a diagnostic. Location is
/// None when the line has no MSBuild `path(line,col):` shape — a build-tool
/// crash or a bare summary line has nothing to point at, and that absence is
/// a real fact about the diagnostic, not something to paper over with a
/// fabricated 0,0 location.
type BuildDiagnostic = {
  File: string option
  Line: int option
  Column: int option
  Severity: BuildDiagnosticSeverity
  /// The compiler/MSBuild diagnostic code (e.g. "FS0039"), when the line
  /// carried one — kept separate from Message so a surface can link or
  /// filter by code without re-parsing text.
  Code: string option
  Message: string
}

module BuildDiagnostic =
  let private msbuildLine =
    System.Text.RegularExpressions.Regex(
      @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<severity>error|warning)\s+(?<code>\S+):\s*(?<message>.*?)\s*(?:\[.*\])?$",
      System.Text.RegularExpressions.RegexOptions.Compiled)

  /// Parse one line of `dotnet build` output. A line that doesn't match
  /// MSBuild's "path(line,col): severity CODE: message [project]" shape
  /// becomes a location-less Error diagnostic carrying the raw text verbatim.
  let ofLine (line: string) : BuildDiagnostic =
    let trimmed = line.Trim()
    let m = msbuildLine.Match(trimmed)
    match m.Success with
    | true ->
      { File = Some m.Groups.["file"].Value
        Line = Some (int m.Groups.["line"].Value)
        Column = Some (int m.Groups.["col"].Value)
        Severity =
          match m.Groups.["severity"].Value with
          | "warning" -> BuildDiagnosticSeverity.Warning
          | _ -> BuildDiagnosticSeverity.Blocking
        Code = Some m.Groups.["code"].Value
        Message = m.Groups.["message"].Value }
    | false ->
      { File = None; Line = None; Column = None; Severity = BuildDiagnosticSeverity.Blocking; Code = None; Message = trimmed }

  /// Plain factual text — no call to action. Every surface (dashboard card,
  /// MCP tool response, HTTP body) words its own hint from this data instead
  /// of inheriting one baked into the domain error. Faithfully reconstructs
  /// MSBuild's own line shape for a parsed diagnostic.
  let describe (diagnostics: BuildDiagnostic list) : string =
    let severityWord = function
      | BuildDiagnosticSeverity.Blocking -> "error"
      | BuildDiagnosticSeverity.Warning -> "warning"
    diagnostics
    |> List.map (fun d ->
      match d.File, d.Line, d.Column, d.Code with
      | Some f, Some l, Some c, Some code -> sprintf "%s(%d,%d): %s %s: %s" f l c (severityWord d.Severity) code d.Message
      | Some f, Some l, Some c, None -> sprintf "%s(%d,%d): %s" f l c d.Message
      | _ -> d.Message)
    |> String.concat "\n"

/// Unified error type for the entire Bozzetto system.
/// Every Result<..., BozzettoError> across all layers uses this single DU.
/// NO wildcard matches in module functions — compiler catches missing cases.
[<RequireQualifiedAccess>]
type BozzettoError =
  // ── Tool availability ──
  | ToolNotAvailable of toolName: string * currentState: SessionState * availableTools: string list
  // ── Session operations ──
  | SessionNotFound of sessionId: string
  | NoActiveSessions
  | AmbiguousSessions of sessionDescriptions: string list
  | SessionCreationFailed of reason: string
  | NeedsRebuild of missing: string list
  | DuplicateSession of existingSessionId: string * workingDirectory: string
  /// A session-create request's `workingDirectory` or a `projects` entry
  /// failed path-safety validation — missing/non-existent directory, a UNC
  /// path, or a project path that canonicalizes to somewhere outside
  /// `workingDirectory`. `path` is the exact offending value so the caller
  /// can see precisely what was refused (see McpServer.fs's
  /// `validateSessionCreateRequest`, mirroring the containment discipline
  /// `DashboardTypes.resolveSessionProjects` already applies on the
  /// dashboard's own session-create path — bozzetto-roast.md Finding #13).
  | UnsafeSessionPath of path: string * reason: string
  /// A session-create request named a project whose target framework the
  /// FSI host cannot load — the classic case is .NET Framework (net48,
  /// net472, ...). Computed once, at the single owner of session creation
  /// (SessionManager's CreateSession handling), from
  /// `ProjectCompatibility.findUnhostable` — every on-ramp (MCP, HTTP,
  /// dashboard) inherits the refusal for free instead of each one guessing
  /// from a build-detection failure the way RuntimeCompat's "no bin/
  /// directory" message used to be for .NET Framework projects, which was
  /// false: the project builds fine, it just produces output the FSI host
  /// cannot load. `targetFrameworks` keeps every declared TFM (not just the
  /// unsupported one) so multi-targeting isn't hidden from the message.
  | ProjectFrameworkNotHostable of
      project: string *
      targetFrameworks: string list *
      reason: ProjectCompatibility.UnsupportedTfmReason
  | SessionStopFailed of sessionId: string * reason: string
  | SessionSwitchFailed of sessionId: string * reason: string
  /// The SessionManager mailbox's queue is at or past its admission
  /// ceiling (`Timeouts.sessionManagerQueueCapacity`) — refused BEFORE
  /// posting, not left to queue indefinitely behind whatever is already
  /// backed up. Mirrors ElmLoop's own 256-message high-watermark alarm
  /// (`ElmLoop.fs`) for the mailbox that didn't have one (observed
  /// 2026-09-22: no bound at all on `MailboxProcessor<SessionCommand>`'s
  /// queue, so a burst of concurrent callers had no signal short of the
  /// caller's own external timeout). `pending`/`capacity` let the caller
  /// (and the log line) say exactly how overloaded, not just "busy."
  | SupervisorBusy of pending: int * capacity: int
  /// The daemon refused a new session because the MACHINE, not the mailbox,
  /// is short on memory — `MemoryPressure.Critical`
  /// (see the same module's doc comment for the two incidents this exists
  /// for: a daemon RSS climbing to 51.7GB, then 55GB, of a 62GB box with
  /// nothing refusing new work along the way). Distinct from
  /// `SupervisorBusy`, which is about the mailbox's own queue depth, not the
  /// box's memory — the two causes need different words, so this carries
  /// `MemorySupervisor.step`'s own reason string rather than reusing
  /// `SupervisorBusy`'s "commands pending" phrasing for a cause it doesn't
  /// describe. Same retry semantics as `SupervisorBusy` (503, retryable).
  | MemoryPressureRefused of reason: string
  /// The target session could not be routed to at all — gone, still warming
  /// up, faulted, or otherwise unroutable. `reason` is the resolution's own
  /// description (SessionResolution/RouteError already computed it; this
  /// case just carries it instead of it being formatted straight to a
  /// display string with no structure for a caller to branch on).
  | SessionNotRoutable of reason: string
  // ── Worker communication ──
  | WorkerCommunicationFailed of sessionId: string * reason: string
  | WorkerSpawnFailed of reason: string
  | WorkerTimeout of sessionId: string * operation: string * timeoutSec: float
  | WorkerHttpError of sessionId: string * endpoint: string * statusCode: int
  | PipeClosed
  // ── Eval/reset/check operations ──
  | EvalFailed of reason: string
  | ResetFailed of reason: string
  | HardResetFailed of reason: string
  /// `dotnet build` failed. Structured diagnostics, not prose with a baked-in
  /// UI hint — a surface words its own call to action from `suggestedAction`
  /// or its own display logic (see AppRun.fs's dashboard-card describe).
  | BuildFailed of exitCode: int * diagnostics: BuildDiagnostic list
  | ScriptLoadFailed of reason: string
  | CheckFailed of reason: string
  | CompletionFailed of sessionId: string * reason: string
  | CancelFailed of reason: string
  /// A reset replaced the session while this evaluation was still running, so
  /// its result belongs to a session that no longer exists and was discarded.
  | EvalSupersededByReset
  // ── Warm-up ──
  | WarmupOpenFailed of name: string * reason: string
  | WarmupContextFailed of sessionId: string * reason: string
  // ── Loaded definitions ──
  | LoadedStateStale of sessionId: string * reason: string
  // ── Running apps ──
  | AppRunFailed of project: string * reason: string
  // ── Restart policy ──
  | RestartLimitExceeded of restartCount: int * windowMinutes: float
  // ── Infrastructure ──
  | DaemonStartFailed of reason: string
  | DaemonNotRunning
  | PortInUse of port: int
  | SseConnectionError of reason: string
  | JsonParseError of context: string * reason: string
  // ── Cohort coordination (multi-agent vision) ──
  /// A cohort command was refused by the pure `Cohort.decide` core — the one
  /// boundary that maps `Cohort.CohortError` into this algebra. `reason` and
  /// `suggestion` are built per `CohortError` case at the MCP boundary
  /// (`Mcp.fs`'s `cohortErrorToBozzettoError`), where the caller's `MemberId`
  /// display is available, so the agent gets an accurate what + actionable next
  /// step instead of a mismatched session/worker error's advice.
  | CohortActionFailed of reason: string * suggestion: string
  | Unexpected of exn

module BozzettoError =
  let describe = function
    | BozzettoError.ToolNotAvailable(toolName, state, available) ->
      sprintf "Cannot %s: session is %s. Available: %s"
        toolName (SessionState.label state) (available |> String.concat ", ")
    | BozzettoError.SessionNotFound id ->
      sprintf "Session '%s' not found. Use list_sessions to see available sessions." id
    | BozzettoError.NoActiveSessions ->
      "No active sessions. Use create_project_session, create_solution_session, or create_bare_session to start one."
    | BozzettoError.AmbiguousSessions descriptions ->
      sprintf "Multiple sessions active. Specify sessionId:\n%s" (descriptions |> String.concat "\n")
    | BozzettoError.SessionCreationFailed reason ->
      sprintf "Failed to create session: %s. Check the project path exists and contains a valid .fsproj." reason
    | BozzettoError.NeedsRebuild missing ->
      sprintf "Session not created: generated build state is missing (%s)." (String.concat ", " missing)
    | BozzettoError.DuplicateSession(existingId, dir) ->
      sprintf "A session for this project already exists (session %s, working directory %s). Use switch_session to select it instead of creating a duplicate." existingId dir
    | BozzettoError.UnsafeSessionPath(path, reason) ->
      sprintf "Refused session path '%s': %s" path reason
    | BozzettoError.ProjectFrameworkNotHostable(project, targetFrameworks, reason) ->
      ProjectCompatibility.describeUnhostable project targetFrameworks reason
    | BozzettoError.SessionStopFailed(id, reason) ->
      sprintf "Failed to stop session '%s': %s" id reason
    | BozzettoError.SessionSwitchFailed(id, reason) ->
      sprintf "Failed to switch to session '%s': %s. Use list_sessions to check available sessions." id reason
    | BozzettoError.SupervisorBusy(pending, capacity) ->
      sprintf "The session supervisor is overloaded (%d commands pending, capacity %d). Wait a moment and retry." pending capacity
    | BozzettoError.MemoryPressureRefused reason ->
      sprintf "Refused: the machine is low on memory (%s)." reason
    | BozzettoError.SessionNotRoutable reason ->
      sprintf "Session not reachable: %s" reason
    | BozzettoError.WorkerCommunicationFailed(id, reason) ->
      sprintf "Cannot reach session '%s': %s. The worker may have crashed — try hard_reset_fsi_session." id reason
    | BozzettoError.WorkerSpawnFailed reason ->
      sprintf "Failed to start worker: %s. Ensure the .NET SDK is installed and the project builds with 'dotnet build'." reason
    | BozzettoError.WorkerTimeout(id, operation, sec) ->
      sprintf "Session '%s' timed out during %s after %.1fs. Try again or use hard_reset_fsi_session." id operation sec
    | BozzettoError.WorkerHttpError(id, endpoint, status) ->
      sprintf "Session '%s' returned HTTP %d for %s" id status endpoint
    | BozzettoError.PipeClosed ->
      "Pipe closed unexpectedly. The worker process may have crashed — try hard_reset_fsi_session to recover."
    | BozzettoError.EvalFailed reason ->
      sprintf "Evaluation failed: %s" reason
    | BozzettoError.ResetFailed reason ->
      sprintf "Reset failed: %s. Try hard_reset_fsi_session for a full restart." reason
    | BozzettoError.HardResetFailed reason ->
      sprintf "Hard reset failed: %s. Check that the project builds with 'dotnet build'." reason
    | BozzettoError.BuildFailed(exitCode, diagnostics) ->
      sprintf "Build failed (exit %d):\n%s" exitCode (BuildDiagnostic.describe diagnostics)
    | BozzettoError.ScriptLoadFailed reason ->
      sprintf "Script load failed: %s. Check that the file exists and has valid F# syntax." reason
    | BozzettoError.CheckFailed reason ->
      sprintf "Type check failed: %s" reason
    | BozzettoError.CompletionFailed(id, reason) ->
      sprintf "Code completion failed for session '%s': %s" id reason
    | BozzettoError.CancelFailed reason ->
      sprintf "Cancel failed: %s" reason
    | BozzettoError.EvalSupersededByReset ->
      "The session was reset while this evaluation was running, so its result was discarded: it ran against the session the reset replaced, and none of its definitions exist in the fresh session."
    | BozzettoError.WarmupOpenFailed(name, reason) ->
      sprintf "Failed to open '%s' during warm-up: %s" name reason
    | BozzettoError.WarmupContextFailed(id, reason) ->
      sprintf "Failed to get warmup context for session '%s': %s" id reason
    | BozzettoError.LoadedStateStale(id, reason) ->
      sprintf "Loaded definition is stale in session '%s': %s" id reason
    | BozzettoError.AppRunFailed("", reason) ->
      sprintf "Could not run the app: %s" reason
    | BozzettoError.AppRunFailed(project, reason) ->
      sprintf "Could not run '%s': %s" project reason
    | BozzettoError.RestartLimitExceeded(count, windowMin) ->
      sprintf "Worker restarted %d times within %.0f minutes — giving up. Check the log file for crash details and restart Bozzetto." count windowMin
    | BozzettoError.DaemonStartFailed reason ->
      sprintf "Failed to start daemon: %s" reason
    | BozzettoError.DaemonNotRunning ->
      "Bozzetto daemon is not running. Start it with 'bozzetto' in your project directory."
    | BozzettoError.PortInUse port ->
      sprintf "Port %d is already in use. Another Bozzetto instance may be running — try 'boz status' or use --mcp-port to pick a different port." port
    | BozzettoError.SseConnectionError reason ->
      sprintf "SSE connection failed: %s" reason
    | BozzettoError.JsonParseError(context, reason) ->
      sprintf "JSON parse error in %s: %s" context reason
    | BozzettoError.CohortActionFailed(reason, _) -> reason
    | BozzettoError.Unexpected exn ->
      sprintf "Unexpected error: %s" exn.Message

  let toLogLevel = function
    // Critical — system-level failures, daemon can't operate
    | BozzettoError.DaemonStartFailed _ -> LogLevel.Critical
    | BozzettoError.PortInUse _ -> LogLevel.Critical
    | BozzettoError.RestartLimitExceeded _ -> LogLevel.Critical
    // Error — operation failed, user action needed
    | BozzettoError.WorkerSpawnFailed _ -> LogLevel.Error
    | BozzettoError.WorkerCommunicationFailed _ -> LogLevel.Error
    | BozzettoError.WorkerTimeout _ -> LogLevel.Error
    | BozzettoError.WorkerHttpError _ -> LogLevel.Error
    | BozzettoError.PipeClosed -> LogLevel.Error
    | BozzettoError.SessionCreationFailed _ -> LogLevel.Error
    | BozzettoError.NeedsRebuild _ -> LogLevel.Information
    | BozzettoError.DuplicateSession _ -> LogLevel.Information
    | BozzettoError.UnsafeSessionPath _ -> LogLevel.Warning
    | BozzettoError.ProjectFrameworkNotHostable _ -> LogLevel.Information
    | BozzettoError.EvalFailed _ -> LogLevel.Error
    | BozzettoError.ResetFailed _ -> LogLevel.Error
    | BozzettoError.HardResetFailed _ -> LogLevel.Error
    | BozzettoError.BuildFailed _ -> LogLevel.Error
    | BozzettoError.ScriptLoadFailed _ -> LogLevel.Error
    | BozzettoError.AppRunFailed _ -> LogLevel.Error
    | BozzettoError.SseConnectionError _ -> LogLevel.Error
    | BozzettoError.Unexpected _ -> LogLevel.Error
    // Warning — degraded but recoverable
    | BozzettoError.SessionStopFailed _ -> LogLevel.Warning
    | BozzettoError.SessionSwitchFailed _ -> LogLevel.Warning
    | BozzettoError.SupervisorBusy _ -> LogLevel.Warning
    | BozzettoError.MemoryPressureRefused _ -> LogLevel.Warning
    | BozzettoError.CheckFailed _ -> LogLevel.Warning
    | BozzettoError.CompletionFailed _ -> LogLevel.Warning
    | BozzettoError.CancelFailed _ -> LogLevel.Warning
    | BozzettoError.EvalSupersededByReset -> LogLevel.Warning
    | BozzettoError.WarmupOpenFailed _ -> LogLevel.Warning
    | BozzettoError.WarmupContextFailed _ -> LogLevel.Warning
    | BozzettoError.LoadedStateStale _ -> LogLevel.Warning
    | BozzettoError.JsonParseError _ -> LogLevel.Warning
    // Information — expected conditions, not bugs
    | BozzettoError.ToolNotAvailable _ -> LogLevel.Information
    | BozzettoError.SessionNotFound _ -> LogLevel.Information
    | BozzettoError.SessionNotRoutable _ -> LogLevel.Information
    | BozzettoError.NoActiveSessions -> LogLevel.Information
    | BozzettoError.AmbiguousSessions _ -> LogLevel.Information
    | BozzettoError.DaemonNotRunning -> LogLevel.Information
    | BozzettoError.CohortActionFailed _ -> LogLevel.Information

  let toHttpStatus = function
    // 404 Not Found
    | BozzettoError.SessionNotFound _ -> 404
    | BozzettoError.SessionNotRoutable _ -> 404
    | BozzettoError.NoActiveSessions -> 404
    | BozzettoError.DaemonNotRunning -> 404
    // 400 Bad Request
    | BozzettoError.AmbiguousSessions _ -> 400
    | BozzettoError.JsonParseError _ -> 400
    | BozzettoError.ToolNotAvailable _ -> 400
    | BozzettoError.UnsafeSessionPath _ -> 400
    | BozzettoError.ProjectFrameworkNotHostable _ -> 400
    // A cohort command invalid for the current cohort state/authority (not the
    // holder, not the conductor, scope already claimed, stale fence). 400 not
    // 409: 409 is reserved here for infrastructure conflicts (isInfraError).
    | BozzettoError.CohortActionFailed _ -> 400
    // 409 Conflict
    | BozzettoError.PortInUse _ -> 409
    | BozzettoError.RestartLimitExceeded _ -> 409
    | BozzettoError.DuplicateSession _ -> 409
    | BozzettoError.NeedsRebuild _ -> 409
    // 503 Service Unavailable — overloaded, retry later (not the caller's fault)
    | BozzettoError.SupervisorBusy _ -> 503
    | BozzettoError.MemoryPressureRefused _ -> 503
    // 504 Gateway Timeout
    | BozzettoError.WorkerTimeout _ -> 504
    // 502 Bad Gateway
    | BozzettoError.WorkerHttpError _ -> 502
    | BozzettoError.WorkerCommunicationFailed _ -> 502
    | BozzettoError.PipeClosed -> 502
    | BozzettoError.WorkerSpawnFailed _ -> 502
    | BozzettoError.SseConnectionError _ -> 502
    // 500 Internal Server Error
    | BozzettoError.SessionCreationFailed _ -> 500
    | BozzettoError.SessionStopFailed _ -> 500
    | BozzettoError.SessionSwitchFailed _ -> 500
    | BozzettoError.EvalFailed _ -> 500
    | BozzettoError.ResetFailed _ -> 500
    | BozzettoError.HardResetFailed _ -> 500
    | BozzettoError.BuildFailed _ -> 500
    | BozzettoError.ScriptLoadFailed _ -> 500
    | BozzettoError.CheckFailed _ -> 500
    | BozzettoError.CompletionFailed _ -> 500
    | BozzettoError.CancelFailed _ -> 500
    | BozzettoError.EvalSupersededByReset -> 500
    | BozzettoError.WarmupOpenFailed _ -> 500
    | BozzettoError.WarmupContextFailed _ -> 500
    | BozzettoError.LoadedStateStale _ -> 500
    | BozzettoError.AppRunFailed _ -> 500
    | BozzettoError.DaemonStartFailed _ -> 500
    | BozzettoError.Unexpected _ -> 500

  /// Client errors: 4xx — the request was malformed or referred to missing resources.
  let isClientError = function
    | BozzettoError.SessionNotFound _ -> true
    | BozzettoError.SessionNotRoutable _ -> true
    | BozzettoError.NoActiveSessions -> true
    | BozzettoError.DaemonNotRunning -> true
    | BozzettoError.AmbiguousSessions _ -> true
    | BozzettoError.JsonParseError _ -> true
    | BozzettoError.ToolNotAvailable _ -> true
    | BozzettoError.UnsafeSessionPath _ -> true
    | BozzettoError.ProjectFrameworkNotHostable _ -> true
    | BozzettoError.NeedsRebuild _ -> true
    | BozzettoError.CohortActionFailed _ -> true
    | BozzettoError.AppRunFailed _
    | BozzettoError.DuplicateSession _
    | BozzettoError.SessionCreationFailed _
    | BozzettoError.SessionStopFailed _
    | BozzettoError.SessionSwitchFailed _
    | BozzettoError.WorkerCommunicationFailed _
    | BozzettoError.WorkerSpawnFailed _
    | BozzettoError.WorkerTimeout _
    | BozzettoError.WorkerHttpError _
    | BozzettoError.PipeClosed
    | BozzettoError.EvalFailed _
    | BozzettoError.ResetFailed _
    | BozzettoError.HardResetFailed _
    | BozzettoError.BuildFailed _
    | BozzettoError.ScriptLoadFailed _
    | BozzettoError.CheckFailed _
    | BozzettoError.CompletionFailed _
    | BozzettoError.CancelFailed _
    | BozzettoError.EvalSupersededByReset
    | BozzettoError.WarmupOpenFailed _
    | BozzettoError.WarmupContextFailed _
    | BozzettoError.LoadedStateStale _
    | BozzettoError.RestartLimitExceeded _
    | BozzettoError.DaemonStartFailed _
    | BozzettoError.PortInUse _
    | BozzettoError.SseConnectionError _
    | BozzettoError.SupervisorBusy _
    | BozzettoError.MemoryPressureRefused _
    | BozzettoError.Unexpected _ -> false

  /// Server errors: 500 — internal failures not caused by the client.
  let isServerError = function
    | BozzettoError.SessionCreationFailed _ -> true
    | BozzettoError.NeedsRebuild _ -> false
    | BozzettoError.SessionStopFailed _ -> true
    | BozzettoError.SessionSwitchFailed _ -> true
    | BozzettoError.EvalFailed _ -> true
    | BozzettoError.ResetFailed _ -> true
    | BozzettoError.HardResetFailed _ -> true
    | BozzettoError.BuildFailed _ -> true
    | BozzettoError.ScriptLoadFailed _ -> true
    | BozzettoError.CheckFailed _ -> true
    | BozzettoError.CompletionFailed _ -> true
    | BozzettoError.CancelFailed _ -> true
    | BozzettoError.EvalSupersededByReset -> true
    | BozzettoError.WarmupOpenFailed _ -> true
    | BozzettoError.WarmupContextFailed _ -> true
    | BozzettoError.LoadedStateStale _ -> true
    | BozzettoError.DaemonStartFailed _ -> true
    | BozzettoError.AppRunFailed _ -> true
    | BozzettoError.Unexpected _ -> true
    | BozzettoError.CohortActionFailed _
    | BozzettoError.ToolNotAvailable _
    | BozzettoError.UnsafeSessionPath _
    | BozzettoError.ProjectFrameworkNotHostable _
    | BozzettoError.NeedsRebuild _
    | BozzettoError.SessionNotFound _
    | BozzettoError.SessionNotRoutable _
    | BozzettoError.NoActiveSessions
    | BozzettoError.AmbiguousSessions _
    | BozzettoError.JsonParseError _
    | BozzettoError.DaemonNotRunning
    | BozzettoError.DuplicateSession _
    | BozzettoError.WorkerCommunicationFailed _
    | BozzettoError.WorkerSpawnFailed _
    | BozzettoError.WorkerTimeout _
    | BozzettoError.WorkerHttpError _
    | BozzettoError.PipeClosed
    | BozzettoError.SseConnectionError _
    | BozzettoError.RestartLimitExceeded _
    | BozzettoError.PortInUse _
    | BozzettoError.SupervisorBusy _
    | BozzettoError.MemoryPressureRefused _ -> false

  /// Gateway errors: 502/504 — the worker (upstream) is unreachable or timed out.
  let isGatewayError = function
    | BozzettoError.WorkerCommunicationFailed _ -> true
    | BozzettoError.WorkerSpawnFailed _ -> true
    | BozzettoError.WorkerTimeout _ -> true
    | BozzettoError.WorkerHttpError _ -> true
    | BozzettoError.PipeClosed -> true
    | BozzettoError.SseConnectionError _ -> true
    | BozzettoError.CohortActionFailed _
    | BozzettoError.AppRunFailed _
    | BozzettoError.ToolNotAvailable _
    | BozzettoError.UnsafeSessionPath _
    | BozzettoError.ProjectFrameworkNotHostable _
    | BozzettoError.NeedsRebuild _
    | BozzettoError.SessionNotFound _
    | BozzettoError.SessionNotRoutable _
    | BozzettoError.NoActiveSessions
    | BozzettoError.AmbiguousSessions _
    | BozzettoError.JsonParseError _
    | BozzettoError.DaemonNotRunning
    | BozzettoError.DuplicateSession _
    | BozzettoError.SessionCreationFailed _
    | BozzettoError.SessionStopFailed _
    | BozzettoError.SessionSwitchFailed _
    | BozzettoError.EvalFailed _
    | BozzettoError.ResetFailed _
    | BozzettoError.HardResetFailed _
    | BozzettoError.BuildFailed _
    | BozzettoError.ScriptLoadFailed _
    | BozzettoError.CheckFailed _
    | BozzettoError.CompletionFailed _
    | BozzettoError.CancelFailed _
    | BozzettoError.EvalSupersededByReset
    | BozzettoError.WarmupOpenFailed _
    | BozzettoError.WarmupContextFailed _
    | BozzettoError.LoadedStateStale _
    | BozzettoError.RestartLimitExceeded _
    | BozzettoError.DaemonStartFailed _
    | BozzettoError.PortInUse _
    | BozzettoError.SupervisorBusy _
    | BozzettoError.MemoryPressureRefused _
    | BozzettoError.Unexpected _ -> false

  /// Infrastructure errors: 409 — system-level conflicts (port in use, restart limit, duplicate session).
  let isInfraError = function
    | BozzettoError.PortInUse _ -> true
    | BozzettoError.RestartLimitExceeded _ -> true
    | BozzettoError.DuplicateSession _ -> true
    | BozzettoError.NeedsRebuild _ -> false
    | BozzettoError.SupervisorBusy _ -> false
    | BozzettoError.MemoryPressureRefused _ -> false
    | BozzettoError.CohortActionFailed _
    | BozzettoError.AppRunFailed _
    | BozzettoError.ToolNotAvailable _
    | BozzettoError.UnsafeSessionPath _
    | BozzettoError.ProjectFrameworkNotHostable _
    | BozzettoError.SessionNotFound _
    | BozzettoError.SessionNotRoutable _
    | BozzettoError.NoActiveSessions
    | BozzettoError.AmbiguousSessions _
    | BozzettoError.JsonParseError _
    | BozzettoError.DaemonNotRunning
    | BozzettoError.SessionCreationFailed _
    | BozzettoError.SessionStopFailed _
    | BozzettoError.SessionSwitchFailed _
    | BozzettoError.WorkerCommunicationFailed _
    | BozzettoError.WorkerSpawnFailed _
    | BozzettoError.WorkerTimeout _
    | BozzettoError.WorkerHttpError _
    | BozzettoError.PipeClosed
    | BozzettoError.EvalFailed _
    | BozzettoError.ResetFailed _
    | BozzettoError.HardResetFailed _
    | BozzettoError.BuildFailed _
    | BozzettoError.ScriptLoadFailed _
    | BozzettoError.CheckFailed _
    | BozzettoError.CompletionFailed _
    | BozzettoError.CancelFailed _
    | BozzettoError.EvalSupersededByReset
    | BozzettoError.WarmupOpenFailed _
    | BozzettoError.WarmupContextFailed _
    | BozzettoError.LoadedStateStale _
    | BozzettoError.DaemonStartFailed _
    | BozzettoError.SseConnectionError _
    | BozzettoError.Unexpected _ -> false

  /// Overload errors: 503 — a real, bounded capacity limit was hit
  /// (`SupervisorBusy`, the SessionManager mailbox's admission ceiling).
  /// Distinct from `isInfraError`'s 409 conflicts: nothing about the
  /// REQUEST conflicts with anything — the daemon is just handling more
  /// concurrent work than its configured ceiling right now, and the fix is
  /// "wait and retry," not "resolve a conflict."
  let isOverloadError = function
    | BozzettoError.SupervisorBusy _ -> true
    | BozzettoError.MemoryPressureRefused _ -> true
    | BozzettoError.ToolNotAvailable _
    | BozzettoError.SessionNotFound _
    | BozzettoError.NoActiveSessions
    | BozzettoError.AmbiguousSessions _
    | BozzettoError.SessionCreationFailed _
    | BozzettoError.DuplicateSession _
    | BozzettoError.UnsafeSessionPath _
    | BozzettoError.ProjectFrameworkNotHostable _
    | BozzettoError.NeedsRebuild _
    | BozzettoError.SessionStopFailed _
    | BozzettoError.SessionSwitchFailed _
    | BozzettoError.SessionNotRoutable _
    | BozzettoError.WorkerCommunicationFailed _
    | BozzettoError.WorkerSpawnFailed _
    | BozzettoError.WorkerTimeout _
    | BozzettoError.WorkerHttpError _
    | BozzettoError.PipeClosed
    | BozzettoError.EvalFailed _
    | BozzettoError.ResetFailed _
    | BozzettoError.HardResetFailed _
    | BozzettoError.BuildFailed _
    | BozzettoError.ScriptLoadFailed _
    | BozzettoError.CheckFailed _
    | BozzettoError.CompletionFailed _
    | BozzettoError.CancelFailed _
    | BozzettoError.EvalSupersededByReset
    | BozzettoError.WarmupOpenFailed _
    | BozzettoError.WarmupContextFailed _
    | BozzettoError.LoadedStateStale _
    | BozzettoError.AppRunFailed _
    | BozzettoError.RestartLimitExceeded _
    | BozzettoError.DaemonStartFailed _
    | BozzettoError.DaemonNotRunning
    | BozzettoError.PortInUse _
    | BozzettoError.SseConnectionError _
    | BozzettoError.JsonParseError _
    | BozzettoError.CohortActionFailed _
    | BozzettoError.Unexpected _ -> false

  /// Actionable suggestion for each error case.
  let suggestedAction = function
    | BozzettoError.ToolNotAvailable _ -> "Wait for session to reach Ready state"
    | BozzettoError.SessionNotFound _ -> "Run list_sessions to see available sessions"
    | BozzettoError.NoActiveSessions -> "Choose create_project_session, create_solution_session, or create_bare_session to start one"
    | BozzettoError.AmbiguousSessions _ -> "Specify a sessionId explicitly"
    | BozzettoError.SessionCreationFailed _ -> "Check the project path and run 'dotnet build'"
    | BozzettoError.NeedsRebuild _ -> "Build the project, then retry creating the session"
    | BozzettoError.DuplicateSession _ -> "Run switch_session to select the existing session"
    | BozzettoError.UnsafeSessionPath _ -> "Use an existing directory and keep project paths inside it — no UNC paths or '..' escapes"
    | BozzettoError.ProjectFrameworkNotHostable _ -> "Point Bozzetto at a .NET (Core) project (net5.0 or newer) for now — .NET Framework support is not shipped yet, and the message names the issue tracking it"
    | BozzettoError.SessionStopFailed _ -> "Try hard_reset_fsi_session"
    | BozzettoError.SupervisorBusy _ -> "Wait a few seconds and retry — the daemon is handling a burst of concurrent session activity"
    | BozzettoError.MemoryPressureRefused _ -> "Stop unused sessions, or wait for machine memory to free up, then retry"
    | BozzettoError.SessionSwitchFailed _ -> "Run list_sessions to check available sessions"
    | BozzettoError.SessionNotRoutable _ -> "Run get_session_status or list_sessions to check session state"
    | BozzettoError.WorkerCommunicationFailed _ -> "Run hard_reset_fsi_session"
    | BozzettoError.WorkerSpawnFailed _ -> "Check .NET SDK installation with 'dotnet --info'"
    | BozzettoError.WorkerTimeout _ -> "Retry or run hard_reset_fsi_session"
    | BozzettoError.WorkerHttpError _ -> "Run hard_reset_fsi_session"
    | BozzettoError.PipeClosed -> "Run hard_reset_fsi_session"
    | BozzettoError.EvalFailed _ -> "Fix the code and resubmit"
    | BozzettoError.ResetFailed _ -> "Run hard_reset_fsi_session"
    | BozzettoError.HardResetFailed _ -> "Check that the project builds with 'dotnet build'"
    | BozzettoError.BuildFailed _ -> "Fix the build errors, then run the app again"
    | BozzettoError.ScriptLoadFailed _ -> "Check file exists and has valid F# syntax"
    | BozzettoError.CheckFailed _ -> "Fix the code and resubmit"
    | BozzettoError.CompletionFailed _ -> "Retry or run reset_fsi_session"
    | BozzettoError.CancelFailed _ -> "Retry or run hard_reset_fsi_session"
    | BozzettoError.EvalSupersededByReset -> "Run the code again in the fresh session"
    | BozzettoError.WarmupOpenFailed _ -> "Run 'dotnet build' for the project, then hard_reset_fsi_session. If it still doesn't resolve, it is not a public, top-level, fully-qualified name in any loaded assembly — Bozzetto cannot auto-open a nested or private module by its short name."
    | BozzettoError.WarmupContextFailed _ -> "Run hard_reset_fsi_session"
    | BozzettoError.LoadedStateStale _ -> "Run hard_reset_fsi_session"
    | BozzettoError.AppRunFailed _ -> "Run list_runnable_projects to see which projects can run"
    | BozzettoError.RestartLimitExceeded _ -> "Check the log file and restart Bozzetto"
    | BozzettoError.DaemonStartFailed _ -> "Check port availability and .NET SDK"
    | BozzettoError.DaemonNotRunning -> "Start Bozzetto with 'bozzetto'"
    | BozzettoError.PortInUse _ -> "Stop the other process or use --mcp-port"
    | BozzettoError.SseConnectionError _ -> "Check daemon is running and retry"
    | BozzettoError.JsonParseError _ -> "Check request payload format"
    | BozzettoError.CohortActionFailed(_, suggestion) -> suggestion
    | BozzettoError.Unexpected _ -> "Check the Bozzetto log for details"

  /// Agent-facing error description: compose describe + suggestedAction.
  /// Use at MCP boundary so every error an agent sees ends with an actionable next step.
  let describeForAgent (err: BozzettoError) =
    sprintf "%s → Next: %s" (describe err) (suggestedAction err)

  /// Pure admission decision for a bounded mailbox: refuse once `pending`
  /// reaches `capacity`, otherwise admit. Extracted so the decision itself
  /// (not the real `MailboxProcessor.CurrentQueueLength` it's checked
  /// against in production — `DaemonMode.checkMailboxAdmission`) is
  /// unit-testable without spinning up a real mailbox.
  let admissionDecision (pending: int) (capacity: int) : Result<unit, BozzettoError> =
    match pending >= capacity with
    | true -> Result.Error (BozzettoError.SupervisorBusy(pending, capacity))
    | false -> Result.Ok ()

  let private diagnosticValue (diagnostic: BuildDiagnostic) =
    let optional encode value = value |> Option.map encode |> Option.defaultValue JsonValue.Null
    let integer value = JsonValue.ofInt64 (int64 value)
    JsonValue.Object [
      "File", optional JsonValue.String diagnostic.File
      "Line", optional integer diagnostic.Line
      "Column", optional integer diagnostic.Column
      "Severity", JsonValue.String (match diagnostic.Severity with BuildDiagnosticSeverity.Blocking -> "Blocking" | BuildDiagnosticSeverity.Warning -> "Warning")
      "Code", optional JsonValue.String diagnostic.Code
      "Message", JsonValue.String diagnostic.Message ]

  /// Explicit error schema; adding a case requires choosing its field representation.
  let toJson (error: BozzettoError) =
    let caseName, fields =
      match error with
      | BozzettoError.ToolNotAvailable(toolName, currentState, availableTools) ->
        "ToolNotAvailable", ["toolName", toolName |> (JsonValue.String); "currentState", currentState |> (SessionState.label >> JsonValue.String); "availableTools", availableTools |> (List.map JsonValue.String >> JsonValue.Array)]
      | BozzettoError.SessionNotFound(sessionId) ->
        "SessionNotFound", ["sessionId", sessionId |> (JsonValue.String)]
      | BozzettoError.NoActiveSessions ->
        "NoActiveSessions", []
      | BozzettoError.AmbiguousSessions(sessionDescriptions) ->
        "AmbiguousSessions", ["sessionDescriptions", sessionDescriptions |> (List.map JsonValue.String >> JsonValue.Array)]
      | BozzettoError.SessionCreationFailed(reason) ->
        "SessionCreationFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.NeedsRebuild(missing) ->
        "NeedsRebuild", ["missing", missing |> (List.map JsonValue.String >> JsonValue.Array)]
      | BozzettoError.DuplicateSession(existingSessionId, workingDirectory) ->
        "DuplicateSession", ["existingSessionId", existingSessionId |> (JsonValue.String); "workingDirectory", workingDirectory |> (JsonValue.String)]
      | BozzettoError.UnsafeSessionPath(path, reason) ->
        "UnsafeSessionPath", ["path", path |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.ProjectFrameworkNotHostable(project, targetFrameworks, reason) ->
        "ProjectFrameworkNotHostable", ["project", project |> (JsonValue.String); "targetFrameworks", targetFrameworks |> (List.map JsonValue.String >> JsonValue.Array); "reason", reason |> (fun ProjectCompatibility.UnsupportedTfmReason.NetFramework -> JsonValue.String "NetFramework")]
      | BozzettoError.SessionStopFailed(sessionId, reason) ->
        "SessionStopFailed", ["sessionId", sessionId |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.SessionSwitchFailed(sessionId, reason) ->
        "SessionSwitchFailed", ["sessionId", sessionId |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.SupervisorBusy(pending, capacity) ->
        "SupervisorBusy", ["pending", pending |> (int64 >> JsonValue.ofInt64); "capacity", capacity |> (int64 >> JsonValue.ofInt64)]
      | BozzettoError.MemoryPressureRefused(reason) ->
        "MemoryPressureRefused", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.SessionNotRoutable(reason) ->
        "SessionNotRoutable", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.WorkerCommunicationFailed(sessionId, reason) ->
        "WorkerCommunicationFailed", ["sessionId", sessionId |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.WorkerSpawnFailed(reason) ->
        "WorkerSpawnFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.WorkerTimeout(sessionId, operation, timeoutSec) ->
        "WorkerTimeout", ["sessionId", sessionId |> (JsonValue.String); "operation", operation |> (JsonValue.String); "timeoutSec", timeoutSec |> (JsonValue.Number)]
      | BozzettoError.WorkerHttpError(sessionId, endpoint, statusCode) ->
        "WorkerHttpError", ["sessionId", sessionId |> (JsonValue.String); "endpoint", endpoint |> (JsonValue.String); "statusCode", statusCode |> (int64 >> JsonValue.ofInt64)]
      | BozzettoError.PipeClosed ->
        "PipeClosed", []
      | BozzettoError.EvalFailed(reason) ->
        "EvalFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.ResetFailed(reason) ->
        "ResetFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.HardResetFailed(reason) ->
        "HardResetFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.BuildFailed(exitCode, diagnostics) ->
        "BuildFailed", ["exitCode", exitCode |> (int64 >> JsonValue.ofInt64); "diagnostics", diagnostics |> (List.map diagnosticValue >> JsonValue.Array)]
      | BozzettoError.ScriptLoadFailed(reason) ->
        "ScriptLoadFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.CheckFailed(reason) ->
        "CheckFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.CompletionFailed(sessionId, reason) ->
        "CompletionFailed", ["sessionId", sessionId |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.CancelFailed(reason) ->
        "CancelFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.EvalSupersededByReset ->
        "EvalSupersededByReset", []
      | BozzettoError.WarmupOpenFailed(name, reason) ->
        "WarmupOpenFailed", ["name", name |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.WarmupContextFailed(sessionId, reason) ->
        "WarmupContextFailed", ["sessionId", sessionId |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.LoadedStateStale(sessionId, reason) ->
        "LoadedStateStale", ["sessionId", sessionId |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.AppRunFailed(project, reason) ->
        "AppRunFailed", ["project", project |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.RestartLimitExceeded(restartCount, windowMinutes) ->
        "RestartLimitExceeded", ["restartCount", restartCount |> (int64 >> JsonValue.ofInt64); "windowMinutes", windowMinutes |> (JsonValue.Number)]
      | BozzettoError.DaemonStartFailed(reason) ->
        "DaemonStartFailed", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.DaemonNotRunning ->
        "DaemonNotRunning", []
      | BozzettoError.PortInUse(port) ->
        "PortInUse", ["port", port |> (int64 >> JsonValue.ofInt64)]
      | BozzettoError.SseConnectionError(reason) ->
        "SseConnectionError", ["reason", reason |> (JsonValue.String)]
      | BozzettoError.JsonParseError(context, reason) ->
        "JsonParseError", ["context", context |> (JsonValue.String); "reason", reason |> (JsonValue.String)]
      | BozzettoError.CohortActionFailed(reason, suggestion) ->
        "CohortActionFailed", ["reason", reason |> (JsonValue.String); "suggestion", suggestion |> (JsonValue.String)]
      | BozzettoError.Unexpected(cause) ->
        "Unexpected", ["Item", cause |> (fun (error: exn) -> JsonValue.String error.Message)]
    {| case = caseName
       fields = Map.ofList fields
       message = describe error
       suggestedAction = suggestedAction error |}

  let toJsonValue (error: BozzettoError) : JsonValue =
    let value = toJson error
    JsonValue.Object [
      "case", JsonValue.String value.case
      "fields", JsonValue.Object (Map.toList value.fields)
      "message", JsonValue.String value.message
      "suggestedAction", JsonValue.String value.suggestedAction ]

/// Carries a BozzettoError through an exception-typed error channel (the eval
/// actor's `EvalResponse.EvaluationResult`) so the worker boundary recovers
/// the structured case instead of flattening it to `ex.ToString()`.
type BozzettoErrorException(error: BozzettoError) =
  inherit System.Exception(BozzettoError.describe error)
  member _.Error = error
