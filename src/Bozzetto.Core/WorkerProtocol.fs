namespace Bozzetto

open System
open System.Globalization
open System.IO
open Fidelity.Data.JSON
open Bozzetto.ProjectLoading

/// Cross-boundary protocol types shared between daemon and worker processes.
/// This module defines the wire contract — changes here affect all editor integrations.
module WorkerProtocol =

  /// Session identifier — opaque single-case DU enforcing validated 8-char hex format.
  /// Construct via SessionId.newId() or SessionId.validate; extract via SessionId.value.
  [<Struct; CustomComparison; CustomEquality>]
  type SessionId = private SessionId of string
    with
    override x.ToString() = let (SessionId s) = x in s
    override x.GetHashCode() = let (SessionId s) = x in s.GetHashCode()
    override x.Equals(obj) =
      match obj with
      | :? SessionId as other -> let (SessionId a) = x in let (SessionId b) = other in a = b
      | _ -> false
    interface IComparable with
      member x.CompareTo(obj) =
        match obj with
        | :? SessionId as other -> let (SessionId a) = x in let (SessionId b) = other in String.Compare(a, b, StringComparison.Ordinal)
        | _ -> invalidArg "obj" "not a SessionId"
    interface IComparable<SessionId> with
      member x.CompareTo(other) = let (SessionId a) = x in let (SessionId b) = other in String.Compare(a, b, StringComparison.Ordinal)
    interface IEquatable<SessionId> with
      member x.Equals(other) = let (SessionId a) = x in let (SessionId b) = other in a = b

  /// Operations on SessionId values.
  module SessionId =
    /// Compiled regex pattern matching valid session IDs: exactly 8 lowercase hex chars.
    let validPattern = System.Text.RegularExpressions.Regex(@"^[0-9a-f]{8}$", System.Text.RegularExpressions.RegexOptions.Compiled)

    /// Extract the raw string value from a SessionId.
    let value (SessionId s) = s

    /// Generate a new random SessionId (8-char lowercase hex).
    let newId () = SessionId (Guid.NewGuid().ToString("N").[..7])

    /// Validate a session ID from an untrusted source (HTTP, MCP).
    /// Session IDs are 8-char lowercase hex strings (truncated GUID).
    let validate (raw: string) : Result<SessionId, string> =
      match System.String.IsNullOrEmpty(raw) with
      | true -> Error "session ID is empty"
      | false ->
        match validPattern.IsMatch(raw) with
        | true -> Ok (SessionId raw)
        | false -> Error (sprintf "invalid session ID format: '%s'" raw)

  /// Lifecycle state of a managed session — no stringly-typed matching.
  [<RequireQualifiedAccess>]
  type SessionStatus =
    | Starting
    | Ready
    | Evaluating
    /// Worker is running a dotnet build or similar multi-second compilation step.
    /// Sets "Building…" status in the UI so the tool doesn't appear hung.
    | Building of buildReason: string
    | Faulted
    | Restarting
    | Stopped

  /// Conversion and parsing utilities for SessionStatus.
  module SessionStatus =
    /// Convert a SessionStatus to its human-readable label string.
    let label = function
      | SessionStatus.Starting -> "Starting"
      | SessionStatus.Ready -> "Ready"
      | SessionStatus.Evaluating -> "Evaluating"
      | SessionStatus.Building reason -> sprintf "Building (%s)" reason
      | SessionStatus.Faulted -> "Faulted"
      | SessionStatus.Restarting -> "Restarting"
      | SessionStatus.Stopped -> "Stopped"

    /// Convert to SessionState for affordance checking.
    /// Building counts as Evaluating — the session is busy but accepting status queries.
    let toSessionState = function
      | SessionStatus.Starting -> SessionState.WarmingUp
      | SessionStatus.Ready -> SessionState.Ready
      | SessionStatus.Evaluating -> SessionState.Evaluating
      | SessionStatus.Building _ -> SessionState.Evaluating
      | SessionStatus.Faulted -> SessionState.Faulted
      | SessionStatus.Restarting -> SessionState.WarmingUp
      | SessionStatus.Stopped -> SessionState.Faulted

    /// Parse a status label back into a SessionStatus. Handles "Building (reason)" format.
    let parse = function
      | "Starting" -> Result.Ok SessionStatus.Starting
      | "Ready" -> Result.Ok SessionStatus.Ready
      | "Evaluating" -> Result.Ok SessionStatus.Evaluating
      | "Faulted" -> Result.Ok SessionStatus.Faulted
      | "Restarting" -> Result.Ok SessionStatus.Restarting
      | "Stopped" -> Result.Ok SessionStatus.Stopped
      | s when s.StartsWith("Building (", StringComparison.Ordinal) ->
        let reason = s.[10 .. s.Length - 2]
        Result.Ok (SessionStatus.Building reason)
      | unknown -> Result.Error (sprintf "Unknown session status: '%s'" unknown)

    /// Can accept new work?
    let isOperational = function
      | SessionStatus.Ready -> true
      | _ -> false

    /// Alive (not stopped or faulted)?
    let isAlive = function
      | SessionStatus.Starting | SessionStatus.Ready
      | SessionStatus.Evaluating | SessionStatus.Building _
      | SessionStatus.Restarting -> true
      | SessionStatus.Faulted | SessionStatus.Stopped -> false

  /// A daemon-tracked worker process's id and, once it reports one, its HTTP port.
  type WorkerHandle = { Pid: int; Port: int option }

  /// The daemon's own live status for one managed session (SessionInfo.Status).
  /// Distinct from the worker's simpler self-reported SessionStatus (used in
  /// WorkerStatusSnapshot) — a worker process has no notion of "my own pid as
  /// tracked by my supervisor" or "the fault reason the daemon chose to record
  /// for me," so folding those into the wire type would force a worker to
  /// fabricate data it doesn't own. This type instead folds what used to be
  /// three independent optional fields on SessionInfo (FaultReason/WorkerPid/
  /// WorkerPort) into the status itself, so illegal combinations — Ready with
  /// no pid, a stale fault reason surviving into Ready, a stale port
  /// surviving into Faulted — become unrepresentable.
  [<RequireQualifiedAccess>]
  type SessionLifecycleStatus =
    | Starting of WorkerHandle
    | Ready of WorkerHandle
    | Evaluating of WorkerHandle
    /// Worker is running a dotnet build or similar multi-second compilation step.
    | Building of buildReason: string * worker: WorkerHandle
    | Faulted of reason: string option
    /// A restart in flight. Carries the OLD worker's pid ONLY so a late
    /// exit/ready event from that dying process can be recognized as stale
    /// and ignored (see the WorkerExited/WorkerReady stale-pid guards in
    /// SessionManager) — it is not a live worker. None when there was no
    /// prior worker (a cold restart after a Faulted/Stopped session).
    | Restarting of previousWorkerPid: int option
    | Stopped

  /// Conversion and query utilities for SessionLifecycleStatus.
  module SessionLifecycleStatus =
    let workerPid = function
      | SessionLifecycleStatus.Starting w
      | SessionLifecycleStatus.Ready w
      | SessionLifecycleStatus.Evaluating w -> Some w.Pid
      | SessionLifecycleStatus.Building(_, w) -> Some w.Pid
      | SessionLifecycleStatus.Restarting pid -> pid
      | SessionLifecycleStatus.Faulted _ | SessionLifecycleStatus.Stopped -> None

    let workerPort = function
      | SessionLifecycleStatus.Starting w
      | SessionLifecycleStatus.Ready w
      | SessionLifecycleStatus.Evaluating w -> w.Port
      | SessionLifecycleStatus.Building(_, w) -> w.Port
      | SessionLifecycleStatus.Faulted _
      | SessionLifecycleStatus.Restarting _
      | SessionLifecycleStatus.Stopped -> None

    let faultReason = function
      | SessionLifecycleStatus.Faulted reason -> reason
      | _ -> None

    /// True for a status with no live worker and nothing coming back on its
    /// own — safe to release retained per-session state (recent output,
    /// adaptive live bindings) even though the session RECORD itself may
    /// still be kept around so the user can see why it died. `Restarting`
    /// is deliberately NOT dead: a replacement worker is already spawning,
    /// and the session is expected back shortly — reaping its state mid-swap
    /// would just make the dashboard flash empty for no reason. See
    /// `MemorySupervisor.SessionMemoryStatus.Dead`, which this maps onto.
    let isDead = function
      | SessionLifecycleStatus.Faulted _
      | SessionLifecycleStatus.Stopped -> true
      | SessionLifecycleStatus.Starting _
      | SessionLifecycleStatus.Ready _
      | SessionLifecycleStatus.Evaluating _
      | SessionLifecycleStatus.Building _
      | SessionLifecycleStatus.Restarting _ -> false

    /// Update the port on a status that carries a worker handle; a no-op on
    /// any status that doesn't (Faulted/Restarting/Stopped never do).
    let withWorkerPort (port: int option) = function
      | SessionLifecycleStatus.Starting w -> SessionLifecycleStatus.Starting { w with Port = port }
      | SessionLifecycleStatus.Ready w -> SessionLifecycleStatus.Ready { w with Port = port }
      | SessionLifecycleStatus.Evaluating w -> SessionLifecycleStatus.Evaluating { w with Port = port }
      | SessionLifecycleStatus.Building(reason, w) -> SessionLifecycleStatus.Building(reason, { w with Port = port })
      | other -> other

    let label = function
      | SessionLifecycleStatus.Starting _ -> "Starting"
      | SessionLifecycleStatus.Ready _ -> "Ready"
      | SessionLifecycleStatus.Evaluating _ -> "Evaluating"
      | SessionLifecycleStatus.Building(reason, _) -> sprintf "Building (%s)" reason
      | SessionLifecycleStatus.Faulted _ -> "Faulted"
      | SessionLifecycleStatus.Restarting _ -> "Restarting"
      | SessionLifecycleStatus.Stopped -> "Stopped"

    /// Convert to SessionState for affordance checking.
    let toSessionState = function
      | SessionLifecycleStatus.Starting _ -> SessionState.WarmingUp
      | SessionLifecycleStatus.Ready _ -> SessionState.Ready
      | SessionLifecycleStatus.Evaluating _ -> SessionState.Evaluating
      | SessionLifecycleStatus.Building _ -> SessionState.Evaluating
      | SessionLifecycleStatus.Faulted _ -> SessionState.Faulted
      | SessionLifecycleStatus.Restarting _ -> SessionState.WarmingUp
      | SessionLifecycleStatus.Stopped -> SessionState.Faulted

    /// Can accept new work?
    let isOperational = function
      | SessionLifecycleStatus.Ready _ -> true
      | _ -> false

    /// Alive (not stopped or faulted)?
    let isAlive = function
      | SessionLifecycleStatus.Starting _ | SessionLifecycleStatus.Ready _
      | SessionLifecycleStatus.Evaluating _ | SessionLifecycleStatus.Building _
      | SessionLifecycleStatus.Restarting _ -> true
      | SessionLifecycleStatus.Faulted _ | SessionLifecycleStatus.Stopped -> false

    /// Reconcile the daemon's own lifecycle status with what the worker
    /// itself just self-reported (WorkerStatusSnapshot.Status, the simpler
    /// wire-protocol SessionStatus). The worker's report carries no pid/port
    /// — it has no notion of how the daemon is tracking it — so those are
    /// carried over from whatever the daemon currently has recorded.
    ///
    /// Faulted and Stopped are TERMINAL: once the daemon has recorded one, a
    /// live worker report can never undo it, no matter what it says. A `Get
    /// Status` reply can legitimately arrive from a worker that has not
    /// caught up to its own death yet — a hung process the parent-death
    /// watchdog hasn't reaped, a reply that raced the fault the daemon
    /// already learned about through another channel (WorkerExited, a spawn
    /// failure) — and taking that reply at face value flips the registry
    /// right back to "Starting"/"Ready". That is exactly how `/health` and
    /// `get_fsi_status` could disagree with a session's own registry entry:
    /// the caller of this function (`get_fsi_status`, Mcp.fs) writes
    /// `reconciled` straight back into the registry whenever it differs, so
    /// the old, un-sticky version did not just MISREPORT a faulted session —
    /// polling it could resurrect it. A genuine restart never goes through
    /// this function: it replaces the registry entry outright with a fresh
    /// status, so making the terminal cases sticky here closes only the
    /// stale-poll hole, not restart.
    let ofWorkerReport (current: SessionLifecycleStatus) (reported: SessionStatus) : SessionLifecycleStatus =
      match current with
      | SessionLifecycleStatus.Faulted _
      | SessionLifecycleStatus.Stopped -> current
      | _ ->
      let handle () : WorkerHandle =
        { Pid = workerPid current |> Option.defaultValue 0
          Port = workerPort current }
      match reported with
      | SessionStatus.Starting -> SessionLifecycleStatus.Starting (handle ())
      | SessionStatus.Ready -> SessionLifecycleStatus.Ready (handle ())
      | SessionStatus.Evaluating -> SessionLifecycleStatus.Evaluating (handle ())
      | SessionStatus.Building reason -> SessionLifecycleStatus.Building (reason, handle ())
      | SessionStatus.Faulted -> SessionLifecycleStatus.Faulted (faultReason current)
      | SessionStatus.Restarting -> SessionLifecycleStatus.Restarting (workerPid current)
      | SessionStatus.Stopped -> SessionLifecycleStatus.Stopped

  /// All messages the daemon can send to a worker process.
  [<RequireQualifiedAccess>]
  type WorkerMessage =
    | EvalCode of code: string * replyId: string
    | CheckCode of code: string * replyId: string
    | TypeCheckWithSymbols of code: string * filePath: string * replyId: string
    | GetCompletions of code: string * cursorPos: int * replyId: string
    | CancelEval
    | LoadScript of filePath: string * replyId: string
    | ResetSession of replyId: string
    | HardResetSession of rebuild: bool * replyId: string
    | GetStatus of replyId: string
    /// Pulled by the daemon on demand, AFTER an eval reply — never attached
    /// to one. Building the live-value snapshot (reflection walk + JSON) used
    /// to sit between the eval finishing and the caller getting its result;
    /// this request lets the daemon ask for it separately (roast-4 #2).
    | GetLiveValues of replyId: string
    | RunTests of tests: Features.LiveTesting.TestCase array * maxParallelism: int * replyId: string
    | GetTestDiscovery of replyId: string
    /// Identity-preserving whole-file eval of a (possibly unsaved) editor
    /// buffer for as-you-type live testing (live-testing-asyoutype-plan.md
    /// Brief 3): `content` is evaluated through the same `EvalMode.File`
    /// CompilationContext transform the file-watcher's on-disk reload uses,
    /// with eval-time test discovery FORCED (Brief 2's `liveTestRediscover`
    /// flag) so a brand-new or edited `[<Tests>]` value is scanned even when
    /// it detours no existing method. `filePath` is used only for parsing/
    /// identity (module-path derivation, diagnostics); the file on disk is
    /// never read or written by this message.
    | EvalLiveTestFile of filePath: string * content: string * replyId: string
    | GetInstrumentationMaps of replyId: string
    | RunApp of project: string * previous: AppRun.PreviousAddress * replyId: string
    | StopApp of scope: AppRun.StopScope * replyId: string
    /// Long poll: answered when the app is no longer Running with this run id.
    | AwaitAppChange of runId: string * replyId: string
    | Shutdown

  /// F# compiler diagnostic serialized for worker→daemon transport.
  type WorkerDiagnostic = {
    Severity: Features.Diagnostics.DiagnosticSeverity
    Message: string
    StartLine: int
    StartColumn: int
    EndLine: int
    EndColumn: int
    /// The FCS diagnostic's stable `ErrorNumber` (the "39" in "FS0039"), or 0
    /// when not derived from a compiler diagnostic. See roast-5 item #10.
    ErrorNumber: int
  }

  /// Conversion between wire-format WorkerDiagnostic and domain Diagnostic.
  module WorkerDiagnostic =
    let ofDiagnostic (d: Features.Diagnostics.Diagnostic) : WorkerDiagnostic =
      { Severity = d.Severity
        Message = d.Message
        StartLine = d.Range.StartLine
        StartColumn = d.Range.StartColumn
        EndLine = d.Range.EndLine
        EndColumn = d.Range.EndColumn
        ErrorNumber = d.ErrorNumber }

    /// Convert a WorkerDiagnostic to the rich Features.Diagnostics.Diagnostic type.
    let toDiagnostic (wd: WorkerDiagnostic) : Features.Diagnostics.Diagnostic =
      { Message = wd.Message
        Subcategory = ""
        Range = { StartLine = wd.StartLine; StartColumn = wd.StartColumn
                  EndLine = wd.EndLine; EndColumn = wd.EndColumn }
        Severity = wd.Severity
        ErrorNumber = wd.ErrorNumber }

  /// Point-in-time snapshot of worker session health and performance metrics.
  type WorkerStatusSnapshot = {
    Status: SessionStatus
    StatusMessage: string option
    EvalCount: int
    AvgDurationMs: int64
    MinDurationMs: int64
    MaxDurationMs: int64
    /// The loaded projects as the worker classified them (only it has the MSBuild properties).
    Projects: ClassifiedProject list
    /// The version of Bozzetto.Core the WORKER PROCESS actually has loaded —
    /// reflected inside the worker itself (Bozzetto.Host/WorkerMain.fs), not
    /// re-derived by the daemon. The daemon's own Bozzetto.Core.dll is a
    /// SEPARATE process's assembly and can be older than what a freshly
    /// rebuilt session's worker just loaded (the daemon isn't redeployed on
    /// every `hard_reset_fsi_session rebuild=true` — only the worker is) —
    /// get_fsi_status used to report the daemon's own version labeled as
    /// "the session's" (Mcp.fs's old loadedCoreVersion(), reflecting
    /// typeof<BozzettoError>.Assembly from INSIDE the daemon process), which
    /// told an agent to trust a number that was never the worker's.
    CoreVersion: string
  }

  /// Wire-friendly symbol reference for TypeCheckWithSymbols response
  type WorkerSymbolRef = {
    SymbolFullName: string
    IsFromDefinition: bool
    FilePath: string
    Line: int
  }

  /// Conversion between wire-format WorkerSymbolRef and domain SymbolReference.
  module WorkerSymbolRef =
    /// Convert a domain SymbolReference to the wire-friendly WorkerSymbolRef format.
    let fromDomain (sr: Features.LiveTesting.SymbolReference) : WorkerSymbolRef =
      { SymbolFullName = sr.SymbolFullName
        IsFromDefinition = sr.UseKind = Features.LiveTesting.SymbolUseKind.Definition
        FilePath = sr.FilePath
        Line = sr.Line }

    /// Convert a wire-friendly WorkerSymbolRef back to the domain SymbolReference.
    /// Definition/Reference distinction is encoded as a bool on the wire.
    let toDomain (ws: WorkerSymbolRef) : Features.LiveTesting.SymbolReference =
      { SymbolFullName = ws.SymbolFullName
        UseKind =
          match ws.IsFromDefinition with
          | true -> Features.LiveTesting.SymbolUseKind.Definition
          | false -> Features.LiveTesting.SymbolUseKind.Reference
        UsedInTestId = None
        FilePath = ws.FilePath
        Line = ws.Line }

  /// All responses a worker process can send back to the daemon.
  [<RequireQualifiedAccess>]
  type WorkerResponse =
    | EvalResult of replyId: string * result: Result<string, BozzettoError> * diagnostics: WorkerDiagnostic list * metadata: Map<string, string>
    | CheckResult of replyId: string * diagnostics: WorkerDiagnostic list
    | TypeCheckWithSymbolsResult of replyId: string * diagnostics: WorkerDiagnostic list * symbolRefs: WorkerSymbolRef list
    | CompletionResult of replyId: string * completions: string list
    | StatusResult of replyId: string * status: WorkerStatusSnapshot
    /// Reply to GetLiveValues: the same JSON a Features.LiveValueTree.LiveValueSnapshot
    /// serializes to today (the daemon already deserializes that type).
    | LiveValuesResult of replyId: string * snapshotJson: string
    | EvalCancelled of wasRunning: bool
    | ResetResult of replyId: string * result: Result<unit, BozzettoError>
    | HardResetResult of replyId: string * result: Result<string, BozzettoError>
    | ScriptLoaded of replyId: string * result: Result<string, BozzettoError>
    | TestRunResults of replyId: string * results: Features.LiveTesting.TestRunResult array
    | InitialTestDiscovery of tests: Features.LiveTesting.TestCase array * providers: Features.LiveTesting.ProviderDescription list
    /// Reply to EvalLiveTestFile. Mirrors InitialTestDiscovery's `(tests,
    /// providers)` shape — same wire vocabulary, not a forked one — but
    /// wrapped in a Result so a parse/eval failure is a first-class DU case
    /// (fail-closed, Invariant 4 of live-testing-asyoutype-plan.md §2)
    /// instead of an exception: on Error, the worker's dynamic discovery and
    /// run-test slots are left untouched and the caller must retain its
    /// last-good state rather than wipe to a false-empty or false-green view.
    /// On Ok, `tests` is already the LIVE MERGE (TestDiscoveryMerge.merge) of
    /// the compiled baseline with the freshly eval'd dynamic discovery.
    | EvalLiveTestFileResult of
        replyId: string *
        result: Result<Features.LiveTesting.TestCase array * Features.LiveTesting.ProviderDescription list, BozzettoError>
    | InstrumentationMapsResult of replyId: string * maps: Features.LiveTesting.InstrumentationMap array
    | AppRunResult of replyId: string * result: Result<AppRun.AppRunState, BozzettoError>
    | WorkerReady
    | WorkerShuttingDown
    | WorkerError of BozzettoError

  /// Transport abstraction — a function, not an interface.
  /// Same signature works for named pipes, HTTP, or in-process.
  type SessionProxy = WorkerMessage -> Async<WorkerResponse>

  /// Which messages count as the session actually being USED, as opposed to
  /// merely being checked on. `SessionInfo.LastActivity` (and therefore the
  /// dashboard's idle detection, `SessionDisplay.displayStatus`) is only ever
  /// as honest as this classification — a `GetStatus` poll must never count,
  /// or a genuinely idle session could never show idle again.
  [<RequireQualifiedAccess>]
  module WorkerMessage =
    let isActivity = function
      | WorkerMessage.EvalCode _
      | WorkerMessage.CheckCode _
      | WorkerMessage.TypeCheckWithSymbols _
      | WorkerMessage.GetCompletions _
      | WorkerMessage.LoadScript _
      | WorkerMessage.RunTests _
      | WorkerMessage.EvalLiveTestFile _
      | WorkerMessage.RunApp _
      | WorkerMessage.StopApp _ -> true
      | WorkerMessage.CancelEval
      | WorkerMessage.ResetSession _
      | WorkerMessage.HardResetSession _
      | WorkerMessage.GetStatus _
      | WorkerMessage.GetLiveValues _
      | WorkerMessage.GetTestDiscovery _
      | WorkerMessage.GetInstrumentationMaps _
      | WorkerMessage.AwaitAppChange _
      | WorkerMessage.Shutdown -> false

  /// Wrapping helpers for `SessionProxy` — kept next to the type so every
  /// place that resolves a proxy from worker URLs (`SessionManagementOps.GetProxy`,
  /// `EffectDeps.GetProxy`) can apply the SAME behavior instead of hand-rolling it.
  module SessionProxy =
    /// Wrap a proxy so every activity-bearing message (see `WorkerMessage.isActivity`)
    /// calls `touch` before being sent. This is the one place `TouchSession`
    /// gets posted — no route into a worker can forget it, because none of
    /// them call the worker any other way than through a `SessionProxy`.
    let touching (touch: unit -> unit) (proxy: SessionProxy) : SessionProxy =
      fun msg ->
        match WorkerMessage.isActivity msg with
        | true -> touch ()
        | false -> ()
        proxy msg

  /// Metadata for a managed session — displayed in dashboard, stored in persistence.
  type SessionInfo = {
    Id: SessionId
    Name: string option
    Projects: string list
    WorkingDirectory: string
    SolutionRoot: string option
    CreatedAt: DateTime
    LastActivity: DateTime
    Status: SessionLifecycleStatus
    Workflow: WorkflowTypes.SessionWorkflow
    /// The project currently in focus for "Run App" operations.
    ActiveProject: string option
    /// Classification of all projects loaded in this session.
    ProjectRoles: ClassifiedProject list
    /// The app this session runs ("Run App"), as the user should see it.
    App: AppRun.AppRunState
  }

  /// Utilities for deriving display-friendly paths from session metadata.
  module SessionInfo =
    /// Walk up from `startDir` looking for the checkout root — a directory
    /// carrying a checkout marker (`.git`, directory OR file — a worktree's
    /// `.git` is a file). Delegates to `Checkout.classify`/`Checkout.root`;
    /// kept as its own function (same name/signature as before) because
    /// every existing caller resolves a plain `string option` root and does
    /// not need the fuller `Checkout` classification (worktree branch, "not
    /// a git checkout" vs "no marker found") — see `SessionInfo.checkout`
    /// below for that. FIXED: this used to check `Directory.Exists ".git"`
    /// only, so from inside a git worktree it walked past the worktree's
    /// `.git` FILE straight to the main checkout's `.git` DIRECTORY further
    /// up and returned the WRONG (main checkout's) root — see
    /// bozzetto-multiagent-vision.md §1.6 #2.
    let findGitRoot (startDir: string) : string option =
      Checkout.root (Checkout.classify startDir)

    /// The checkout a session's working directory sits in — a MAIN checkout,
    /// a git WORKTREE (with its own branch), or no git checkout at all.
    /// Computed on demand from `WorkingDirectory` rather than stored on
    /// `SessionInfo`: `SessionInfo` is constructed as a full record literal
    /// at ~50 call sites across the daemon and test suite, and `Checkout`
    /// classification is a few cheap filesystem stats, so storing it would
    /// only risk staleness for zero benefit over computing it where needed
    /// (routing, list_sessions, the sidebar).
    let checkout (info: SessionInfo) : Checkout.Checkout =
      Checkout.classify info.WorkingDirectory

    /// Walk up from workingDir to find the nearest directory containing .sln or .slnx.
    /// Skips directories that do not exist (graceful for tests and missing paths).
    let findSolutionRoot (workingDir: string) =
      let rec walk (dir: string) =
        let parent = Path.GetDirectoryName dir
        match isNull parent || parent = dir with
        | true -> None
        | false ->
          let hasSln =
            Directory.Exists dir &&
            (Directory.GetFiles(dir, "*.sln")
             |> Array.append (Directory.GetFiles(dir, "*.slnx"))
             |> Array.isEmpty
             |> not)
          match hasSln with
          | true -> Some dir
          | false -> walk parent
      walk workingDir

    /// Extract a short display name: last path segment of solution root or working dir.
    let displayName (info: SessionInfo) =
      let getLastSegment (path: string) =
        let normalized = path.TrimEnd('/', '\\').Replace('\\', '/')
        Path.GetFileName normalized
      match info.SolutionRoot with
      | Some root -> getLastSegment root
      | None -> getLastSegment info.WorkingDirectory

  /// Explicit Fidelity.Data.JSON codecs for the daemon↔worker wire protocol
  /// (adjacent tag encoding: {"type": caseName, "value": ...}).
  module Serialization =
    let private failDecode msg =
      invalidOp (sprintf "WorkerProtocol JSON decode error: %s" msg)

    let private getProp (name: string) (v: JsonValue) : JsonValue =
      match JsonValue.prop name v with
      | Some p -> p
      | None -> failDecode (sprintf "missing property '%s'" name)

    let private encodeString (s: string) =
      match s with
      | null -> JsonValue.String ""
      | s -> JsonValue.String s

    let private decodeString (v: JsonValue) =
      match JsonValue.asString v with
      | Some s -> s
      | None -> failDecode "expected string"

    let private encodeInt (n: int) = JsonValue.ofInt64 (int64 n)

    let private decodeInt (v: JsonValue) =
      match JsonValue.asInt v with
      | Some n -> int n
      | None -> failDecode "expected int"

    let private encodeInt64 (n: int64) = JsonValue.ofInt64 n

    let private decodeInt64 (v: JsonValue) =
      match JsonValue.asInt v with
      | Some n -> n
      | None -> failDecode "expected int64"

    let private encodeFloat (f: float) = WireJson.number f

    let private decodeFloat (v: JsonValue) =
      match JsonValue.asNumber v with
      | Some f -> f
      | None -> failDecode "expected float"

    let private encodeBool (b: bool) = JsonValue.Bool b

    let private decodeBool (v: JsonValue) =
      match JsonValue.asBool v with
      | Some b -> b
      | None -> failDecode "expected bool"

    let private encodeUnit () = JsonValue.Null

    let private decodeUnit (v: JsonValue) =
      match v with
      | JsonValue.Null -> ()
      | _ -> failDecode "expected null for unit"

    let private encodeDateTime (dt: DateTime) =
      JsonValue.String (dt.ToString("O", CultureInfo.InvariantCulture))

    let private decodeDateTime (v: JsonValue) =
      DateTime.Parse(decodeString v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)

    let private encodeDateTimeOffset (dto: DateTimeOffset) =
      JsonValue.String (dto.ToString("O", CultureInfo.InvariantCulture))

    let private decodeDateTimeOffset (v: JsonValue) =
      DateTimeOffset.Parse(decodeString v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)

    let private encodeTimeSpan (ts: TimeSpan) =
      JsonValue.String (ts.ToString("c", CultureInfo.InvariantCulture))

    let private decodeTimeSpan (v: JsonValue) =
      TimeSpan.ParseExact(decodeString v, "c", CultureInfo.InvariantCulture)

    let private encodeOption encode (v: 'a option) =
      match v with
      | None -> JsonValue.Null
      | Some x -> encode x

    let private decodeOption decode (v: JsonValue) : 'a option =
      match v with
      | JsonValue.Null -> None
      | other -> Some (decode other)

    let private encodeList encode (xs: 'a list) =
      JsonValue.Array (xs |> List.map encode)

    let private decodeList decode (v: JsonValue) : 'a list =
      match v with
      | JsonValue.Array xs -> xs |> List.map decode
      | _ -> failDecode "expected array"

    let private encodeArray encode (xs: 'a array) =
      JsonValue.Array (xs |> Array.toList |> List.map encode)

    let private decodeArray decode (v: JsonValue) : 'a array =
      decodeList decode v |> List.toArray

    let private encodeStringMap (m: Map<string, string>) =
      JsonValue.Object (m |> Map.toList |> List.map (fun (k, v) -> k, encodeString v))

    let private decodeStringMap (v: JsonValue) : Map<string, string> =
      match v with
      | JsonValue.Object pairs ->
        pairs |> List.map (fun (k, pv) -> k, decodeString pv) |> Map.ofList
      | _ -> failDecode "expected object for Map<string, string>"

    let private adj0 (tag: string) =
      JsonValue.Object [ "type", JsonValue.String tag ]

    let private adj1 (tag: string) (v: JsonValue) =
      JsonValue.Object [ "type", JsonValue.String tag; "value", v ]

    let private adjN (tag: string) (vs: JsonValue list) =
      JsonValue.Object [ "type", JsonValue.String tag; "value", JsonValue.Array vs ]

    let private readAdjTag (v: JsonValue) =
      getProp "type" v |> decodeString

    let private readAdj1 (v: JsonValue) =
      getProp "value" v

    let private readAdj2 d1 d2 (v: JsonValue) =
      match getProp "value" v with
      | JsonValue.Array [ a; b ] -> d1 a, d2 b
      | _ -> failDecode "expected 2-element value array"

    let private readAdj3 d1 d2 d3 (v: JsonValue) =
      match getProp "value" v with
      | JsonValue.Array [ a; b; c ] -> d1 a, d2 b, d3 c
      | _ -> failDecode "expected 3-element value array"

    let private readAdj4 d1 d2 d3 d4 (v: JsonValue) =
      match getProp "value" v with
      | JsonValue.Array [ a; b; c; d ] -> d1 a, d2 b, d3 c, d4 d
      | _ -> failDecode "expected 4-element value array"

    let private encodeResult encodeOk encodeErr (r: Result<'Ok, 'Err>) =
      match r with
      | Ok x -> adj1 "Ok" (encodeOk x)
      | Error e -> adj1 "Error" (encodeErr e)

    let private decodeResult decodeOk decodeErr (v: JsonValue) : Result<'Ok, 'Err> =
      match readAdjTag v with
      | "Ok" -> Ok (decodeOk (readAdj1 v))
      | "Error" -> Error (decodeErr (readAdj1 v))
      | t -> failDecode (sprintf "unknown Result tag '%s'" t)

    let private encodePair e1 e2 (a, b) =
      JsonValue.Array [ e1 a; e2 b ]

    let private decodePair d1 d2 (v: JsonValue) =
      match v with
      | JsonValue.Array [ a; b ] -> d1 a, d2 b
      | _ -> failDecode "expected 2-element tuple array"

    // ── SessionId & SessionStatus & SessionLifecycleStatus ──

    let encodeSessionId (SessionId s) = adj1 "SessionId" (encodeString s)

    let decodeSessionId (v: JsonValue) =
      match readAdjTag v with
      | "SessionId" -> SessionId (decodeString (readAdj1 v))
      | t -> failDecode (sprintf "unknown SessionId tag '%s'" t)

    let encodeSessionStatus (s: SessionStatus) =
      match s with
      | SessionStatus.Starting -> adj0 "Starting"
      | SessionStatus.Ready -> adj0 "Ready"
      | SessionStatus.Evaluating -> adj0 "Evaluating"
      | SessionStatus.Building reason -> adj1 "Building" (encodeString reason)
      | SessionStatus.Faulted -> adj0 "Faulted"
      | SessionStatus.Restarting -> adj0 "Restarting"
      | SessionStatus.Stopped -> adj0 "Stopped"

    let decodeSessionStatus (v: JsonValue) =
      match readAdjTag v with
      | "Starting" -> SessionStatus.Starting
      | "Ready" -> SessionStatus.Ready
      | "Evaluating" -> SessionStatus.Evaluating
      | "Building" -> SessionStatus.Building (decodeString (readAdj1 v))
      | "Faulted" -> SessionStatus.Faulted
      | "Restarting" -> SessionStatus.Restarting
      | "Stopped" -> SessionStatus.Stopped
      | t -> failDecode (sprintf "unknown SessionStatus tag '%s'" t)

    let encodeWorkerHandle (w: WorkerHandle) =
      JsonValue.Object [
        "pid", encodeInt w.Pid
        "port", encodeOption encodeInt w.Port
      ]

    let decodeWorkerHandle (v: JsonValue) : WorkerHandle =
      { Pid = getProp "pid" v |> decodeInt
        Port = getProp "port" v |> decodeOption decodeInt }

    let encodeSessionLifecycleStatus (s: SessionLifecycleStatus) =
      match s with
      | SessionLifecycleStatus.Starting w -> adj1 "Starting" (encodeWorkerHandle w)
      | SessionLifecycleStatus.Ready w -> adj1 "Ready" (encodeWorkerHandle w)
      | SessionLifecycleStatus.Evaluating w -> adj1 "Evaluating" (encodeWorkerHandle w)
      | SessionLifecycleStatus.Building(reason, w) -> adjN "Building" [ encodeString reason; encodeWorkerHandle w ]
      | SessionLifecycleStatus.Faulted reason -> adj1 "Faulted" (encodeOption encodeString reason)
      | SessionLifecycleStatus.Restarting pid -> adj1 "Restarting" (encodeOption encodeInt pid)
      | SessionLifecycleStatus.Stopped -> adj0 "Stopped"

    let decodeSessionLifecycleStatus (v: JsonValue) =
      match readAdjTag v with
      | "Starting" -> SessionLifecycleStatus.Starting (decodeWorkerHandle (readAdj1 v))
      | "Ready" -> SessionLifecycleStatus.Ready (decodeWorkerHandle (readAdj1 v))
      | "Evaluating" -> SessionLifecycleStatus.Evaluating (decodeWorkerHandle (readAdj1 v))
      | "Building" ->
        let reason, w = readAdj2 decodeString decodeWorkerHandle v
        SessionLifecycleStatus.Building(reason, w)
      | "Faulted" -> SessionLifecycleStatus.Faulted (decodeOption decodeString (readAdj1 v))
      | "Restarting" -> SessionLifecycleStatus.Restarting (decodeOption decodeInt (readAdj1 v))
      | "Stopped" -> SessionLifecycleStatus.Stopped
      | t -> failDecode (sprintf "unknown SessionLifecycleStatus tag '%s'" t)

    // ── ProjectRole & ClassifiedProject & SessionWorkflow ──

    let encodeProjectRole (r: ProjectRole) =
      match r with
      | ProjectRole.Executable -> adj0 "Executable"
      | ProjectRole.Library -> adj0 "Library"
      | ProjectRole.Test -> adj0 "Test"

    let decodeProjectRole (v: JsonValue) =
      match readAdjTag v with
      | "Executable" -> ProjectRole.Executable
      | "Library" -> ProjectRole.Library
      | "Test" -> ProjectRole.Test
      | t -> failDecode (sprintf "unknown ProjectRole tag '%s'" t)

    let encodeClassifiedProject (p: ClassifiedProject) =
      JsonValue.Object [
        "path", encodeString p.Path
        "role", encodeProjectRole p.Role
        "packageRefs", encodeList encodeString p.PackageRefs
      ]

    let decodeClassifiedProject (v: JsonValue) : ClassifiedProject =
      { Path = getProp "path" v |> decodeString
        Role = getProp "role" v |> decodeProjectRole
        PackageRefs = getProp "packageRefs" v |> decodeList decodeString }

    let encodeSessionWorkflow (w: WorkflowTypes.SessionWorkflow) =
      match w with
      | WorkflowTypes.SessionWorkflow.Interactive -> adj0 "Interactive"
      | WorkflowTypes.SessionWorkflow.LiveTesting -> adj0 "LiveTesting"

    let decodeSessionWorkflow (v: JsonValue) =
      match readAdjTag v with
      | "Interactive" -> WorkflowTypes.SessionWorkflow.Interactive
      | "LiveTesting" -> WorkflowTypes.SessionWorkflow.LiveTesting
      | t -> failDecode (sprintf "unknown SessionWorkflow tag '%s'" t)

    // ── SessionState, ProjectCompatibility, BuildDiagnostic, BozzettoError ──

    let encodeSessionState (s: SessionState) =
      match s with
      | SessionState.Uninitialized -> adj0 "Uninitialized"
      | SessionState.WarmingUp -> adj0 "WarmingUp"
      | SessionState.Ready -> adj0 "Ready"
      | SessionState.Evaluating -> adj0 "Evaluating"
      | SessionState.Faulted -> adj0 "Faulted"

    let decodeSessionState (v: JsonValue) =
      match readAdjTag v with
      | "Uninitialized" -> SessionState.Uninitialized
      | "WarmingUp" -> SessionState.WarmingUp
      | "Ready" -> SessionState.Ready
      | "Evaluating" -> SessionState.Evaluating
      | "Faulted" -> SessionState.Faulted
      | t -> failDecode (sprintf "unknown SessionState tag '%s'" t)

    let encodeUnsupportedTfmReason (r: ProjectCompatibility.UnsupportedTfmReason) =
      match r with
      | ProjectCompatibility.UnsupportedTfmReason.NetFramework -> adj0 "NetFramework"

    let decodeUnsupportedTfmReason (v: JsonValue) =
      match readAdjTag v with
      | "NetFramework" -> ProjectCompatibility.UnsupportedTfmReason.NetFramework
      | t -> failDecode (sprintf "unknown UnsupportedTfmReason tag '%s'" t)

    let encodeBuildDiagnosticSeverity (s: BuildDiagnosticSeverity) =
      match s with
      | BuildDiagnosticSeverity.Blocking -> adj0 "Blocking"
      | BuildDiagnosticSeverity.Warning -> adj0 "Warning"

    let decodeBuildDiagnosticSeverity (v: JsonValue) =
      match readAdjTag v with
      | "Blocking" -> BuildDiagnosticSeverity.Blocking
      | "Warning" -> BuildDiagnosticSeverity.Warning
      | t -> failDecode (sprintf "unknown BuildDiagnosticSeverity tag '%s'" t)

    let encodeBuildDiagnostic (d: BuildDiagnostic) =
      JsonValue.Object [
        "file", encodeOption encodeString d.File
        "line", encodeOption encodeInt d.Line
        "column", encodeOption encodeInt d.Column
        "severity", encodeBuildDiagnosticSeverity d.Severity
        "code", encodeOption encodeString d.Code
        "message", encodeString d.Message
      ]

    let decodeBuildDiagnostic (v: JsonValue) : BuildDiagnostic =
      { File = getProp "file" v |> decodeOption decodeString
        Line = getProp "line" v |> decodeOption decodeInt
        Column = getProp "column" v |> decodeOption decodeInt
        Severity = getProp "severity" v |> decodeBuildDiagnosticSeverity
        Code = getProp "code" v |> decodeOption decodeString
        Message = getProp "message" v |> decodeString }

    let encodeBozzettoError (err: BozzettoError) =
      match err with
      | BozzettoError.ToolNotAvailable(toolName, currentState, availableTools) ->
        adjN "ToolNotAvailable" [ encodeString toolName; encodeSessionState currentState; encodeList encodeString availableTools ]
      | BozzettoError.SessionNotFound sessionId -> adj1 "SessionNotFound" (encodeString sessionId)
      | BozzettoError.NoActiveSessions -> adj0 "NoActiveSessions"
      | BozzettoError.AmbiguousSessions descriptions -> adj1 "AmbiguousSessions" (encodeList encodeString descriptions)
      | BozzettoError.SessionCreationFailed reason -> adj1 "SessionCreationFailed" (encodeString reason)
      | BozzettoError.NeedsRebuild missing -> adj1 "NeedsRebuild" (encodeList encodeString missing)
      | BozzettoError.DuplicateSession(existingId, workingDir) -> adjN "DuplicateSession" [ encodeString existingId; encodeString workingDir ]
      | BozzettoError.UnsafeSessionPath(path, reason) -> adjN "UnsafeSessionPath" [ encodeString path; encodeString reason ]
      | BozzettoError.ProjectFrameworkNotHostable(project, tfms, reason) ->
        adjN "ProjectFrameworkNotHostable" [ encodeString project; encodeList encodeString tfms; encodeUnsupportedTfmReason reason ]
      | BozzettoError.SessionStopFailed(sessionId, reason) -> adjN "SessionStopFailed" [ encodeString sessionId; encodeString reason ]
      | BozzettoError.SessionSwitchFailed(sessionId, reason) -> adjN "SessionSwitchFailed" [ encodeString sessionId; encodeString reason ]
      | BozzettoError.SupervisorBusy(pending, capacity) -> adjN "SupervisorBusy" [ encodeInt pending; encodeInt capacity ]
      | BozzettoError.MemoryPressureRefused reason -> adj1 "MemoryPressureRefused" (encodeString reason)
      | BozzettoError.SessionNotRoutable reason -> adj1 "SessionNotRoutable" (encodeString reason)
      | BozzettoError.WorkerCommunicationFailed(sessionId, reason) -> adjN "WorkerCommunicationFailed" [ encodeString sessionId; encodeString reason ]
      | BozzettoError.WorkerSpawnFailed reason -> adj1 "WorkerSpawnFailed" (encodeString reason)
      | BozzettoError.WorkerTimeout(sessionId, op, timeoutSec) -> adjN "WorkerTimeout" [ encodeString sessionId; encodeString op; encodeFloat timeoutSec ]
      | BozzettoError.WorkerHttpError(sessionId, endpoint, statusCode) -> adjN "WorkerHttpError" [ encodeString sessionId; encodeString endpoint; encodeInt statusCode ]
      | BozzettoError.PipeClosed -> adj0 "PipeClosed"
      | BozzettoError.EvalFailed reason -> adj1 "EvalFailed" (encodeString reason)
      | BozzettoError.ResetFailed reason -> adj1 "ResetFailed" (encodeString reason)
      | BozzettoError.HardResetFailed reason -> adj1 "HardResetFailed" (encodeString reason)
      | BozzettoError.BuildFailed(exitCode, diags) -> adjN "BuildFailed" [ encodeInt exitCode; encodeList encodeBuildDiagnostic diags ]
      | BozzettoError.ScriptLoadFailed reason -> adj1 "ScriptLoadFailed" (encodeString reason)
      | BozzettoError.CheckFailed reason -> adj1 "CheckFailed" (encodeString reason)
      | BozzettoError.CompletionFailed(sessionId, reason) -> adjN "CompletionFailed" [ encodeString sessionId; encodeString reason ]
      | BozzettoError.CancelFailed reason -> adj1 "CancelFailed" (encodeString reason)
      | BozzettoError.EvalSupersededByReset -> adj0 "EvalSupersededByReset"
      | BozzettoError.WarmupOpenFailed(name, reason) -> adjN "WarmupOpenFailed" [ encodeString name; encodeString reason ]
      | BozzettoError.WarmupContextFailed(sessionId, reason) -> adjN "WarmupContextFailed" [ encodeString sessionId; encodeString reason ]
      | BozzettoError.LoadedStateStale(sessionId, reason) -> adjN "LoadedStateStale" [ encodeString sessionId; encodeString reason ]
      | BozzettoError.AppRunFailed(project, reason) -> adjN "AppRunFailed" [ encodeString project; encodeString reason ]
      | BozzettoError.RestartLimitExceeded(count, windowMinutes) -> adjN "RestartLimitExceeded" [ encodeInt count; encodeFloat windowMinutes ]
      | BozzettoError.DaemonStartFailed reason -> adj1 "DaemonStartFailed" (encodeString reason)
      | BozzettoError.DaemonNotRunning -> adj0 "DaemonNotRunning"
      | BozzettoError.PortInUse port -> adj1 "PortInUse" (encodeInt port)
      | BozzettoError.SseConnectionError reason -> adj1 "SseConnectionError" (encodeString reason)
      | BozzettoError.JsonParseError(ctx, reason) -> adjN "JsonParseError" [ encodeString ctx; encodeString reason ]
      | BozzettoError.CohortActionFailed(reason, suggestion) -> adjN "CohortActionFailed" [ encodeString reason; encodeString suggestion ]
      | BozzettoError.Unexpected ex ->
        adj1 "Unexpected" (encodeString (match ex with null -> "" | e -> e.Message))

    let decodeBozzettoError (v: JsonValue) : BozzettoError =
      match readAdjTag v with
      | "ToolNotAvailable" ->
        let toolName, state, tools = readAdj3 decodeString decodeSessionState (decodeList decodeString) v
        BozzettoError.ToolNotAvailable(toolName, state, tools)
      | "SessionNotFound" -> BozzettoError.SessionNotFound (decodeString (readAdj1 v))
      | "NoActiveSessions" -> BozzettoError.NoActiveSessions
      | "AmbiguousSessions" -> BozzettoError.AmbiguousSessions (decodeList decodeString (readAdj1 v))
      | "SessionCreationFailed" -> BozzettoError.SessionCreationFailed (decodeString (readAdj1 v))
      | "NeedsRebuild" -> BozzettoError.NeedsRebuild (decodeList decodeString (readAdj1 v))
      | "DuplicateSession" ->
        let id, dir = readAdj2 decodeString decodeString v
        BozzettoError.DuplicateSession(id, dir)
      | "UnsafeSessionPath" ->
        let path, reason = readAdj2 decodeString decodeString v
        BozzettoError.UnsafeSessionPath(path, reason)
      | "ProjectFrameworkNotHostable" ->
        let proj, tfms, reason = readAdj3 decodeString (decodeList decodeString) decodeUnsupportedTfmReason v
        BozzettoError.ProjectFrameworkNotHostable(proj, tfms, reason)
      | "SessionStopFailed" ->
        let id, reason = readAdj2 decodeString decodeString v
        BozzettoError.SessionStopFailed(id, reason)
      | "SessionSwitchFailed" ->
        let id, reason = readAdj2 decodeString decodeString v
        BozzettoError.SessionSwitchFailed(id, reason)
      | "SupervisorBusy" ->
        let pending, cap = readAdj2 decodeInt decodeInt v
        BozzettoError.SupervisorBusy(pending, cap)
      | "MemoryPressureRefused" -> BozzettoError.MemoryPressureRefused (decodeString (readAdj1 v))
      | "SessionNotRoutable" -> BozzettoError.SessionNotRoutable (decodeString (readAdj1 v))
      | "WorkerCommunicationFailed" ->
        let id, reason = readAdj2 decodeString decodeString v
        BozzettoError.WorkerCommunicationFailed(id, reason)
      | "WorkerSpawnFailed" -> BozzettoError.WorkerSpawnFailed (decodeString (readAdj1 v))
      | "WorkerTimeout" ->
        let id, op, sec = readAdj3 decodeString decodeString decodeFloat v
        BozzettoError.WorkerTimeout(id, op, sec)
      | "WorkerHttpError" ->
        let id, ep, code = readAdj3 decodeString decodeString decodeInt v
        BozzettoError.WorkerHttpError(id, ep, code)
      | "PipeClosed" -> BozzettoError.PipeClosed
      | "EvalFailed" -> BozzettoError.EvalFailed (decodeString (readAdj1 v))
      | "ResetFailed" -> BozzettoError.ResetFailed (decodeString (readAdj1 v))
      | "HardResetFailed" -> BozzettoError.HardResetFailed (decodeString (readAdj1 v))
      | "BuildFailed" ->
        let code, diags = readAdj2 decodeInt (decodeList decodeBuildDiagnostic) v
        BozzettoError.BuildFailed(code, diags)
      | "ScriptLoadFailed" -> BozzettoError.ScriptLoadFailed (decodeString (readAdj1 v))
      | "CheckFailed" -> BozzettoError.CheckFailed (decodeString (readAdj1 v))
      | "CompletionFailed" ->
        let id, reason = readAdj2 decodeString decodeString v
        BozzettoError.CompletionFailed(id, reason)
      | "CancelFailed" -> BozzettoError.CancelFailed (decodeString (readAdj1 v))
      | "EvalSupersededByReset" -> BozzettoError.EvalSupersededByReset
      | "WarmupOpenFailed" ->
        let name, reason = readAdj2 decodeString decodeString v
        BozzettoError.WarmupOpenFailed(name, reason)
      | "WarmupContextFailed" ->
        let id, reason = readAdj2 decodeString decodeString v
        BozzettoError.WarmupContextFailed(id, reason)
      | "LoadedStateStale" ->
        let id, reason = readAdj2 decodeString decodeString v
        BozzettoError.LoadedStateStale(id, reason)
      | "AppRunFailed" ->
        let proj, reason = readAdj2 decodeString decodeString v
        BozzettoError.AppRunFailed(proj, reason)
      | "RestartLimitExceeded" ->
        let count, mins = readAdj2 decodeInt decodeFloat v
        BozzettoError.RestartLimitExceeded(count, mins)
      | "DaemonStartFailed" -> BozzettoError.DaemonStartFailed (decodeString (readAdj1 v))
      | "DaemonNotRunning" -> BozzettoError.DaemonNotRunning
      | "PortInUse" -> BozzettoError.PortInUse (decodeInt (readAdj1 v))
      | "SseConnectionError" -> BozzettoError.SseConnectionError (decodeString (readAdj1 v))
      | "JsonParseError" ->
        let ctx, reason = readAdj2 decodeString decodeString v
        BozzettoError.JsonParseError(ctx, reason)
      | "CohortActionFailed" ->
        let reason, suggestion = readAdj2 decodeString decodeString v
        BozzettoError.CohortActionFailed(reason, suggestion)
      | "Unexpected" ->
        BozzettoError.Unexpected (Exception (decodeString (readAdj1 v)))
      | t -> failDecode (sprintf "unknown BozzettoError tag '%s'" t)

    // ── AppRun ──

    let encodePreviousAddress (p: AppRun.PreviousAddress) =
      match p with
      | AppRun.PreviousAddress.NoPreviousAddress -> adj0 "NoPreviousAddress"
      | AppRun.PreviousAddress.ReuseAddress url -> adj1 "ReuseAddress" (encodeString url)

    let decodePreviousAddress (v: JsonValue) =
      match readAdjTag v with
      | "NoPreviousAddress" -> AppRun.PreviousAddress.NoPreviousAddress
      | "ReuseAddress" -> AppRun.PreviousAddress.ReuseAddress (decodeString (readAdj1 v))
      | t -> failDecode (sprintf "unknown PreviousAddress tag '%s'" t)

    let encodeStopScope (s: AppRun.StopScope) =
      match s with
      | AppRun.StopScope.CurrentApp -> adj0 "CurrentApp"
      | AppRun.StopScope.OnlyRun runId -> adj1 "OnlyRun" (encodeString runId)

    let decodeStopScope (v: JsonValue) =
      match readAdjTag v with
      | "CurrentApp" -> AppRun.StopScope.CurrentApp
      | "OnlyRun" -> AppRun.StopScope.OnlyRun (decodeString (readAdj1 v))
      | t -> failDecode (sprintf "unknown StopScope tag '%s'" t)

    let encodeAppEndpoint (ep: AppRun.AppEndpoint) =
      match ep with
      | AppRun.AppEndpoint.Http(primary, others) -> adjN "Http" [ encodeString primary; encodeList encodeString others ]
      | AppRun.AppEndpoint.NoServer -> adj0 "NoServer"

    let decodeAppEndpoint (v: JsonValue) =
      match readAdjTag v with
      | "Http" ->
        let primary, others = readAdj2 decodeString (decodeList decodeString) v
        AppRun.AppEndpoint.Http(primary, others)
      | "NoServer" -> AppRun.AppEndpoint.NoServer
      | t -> failDecode (sprintf "unknown AppEndpoint tag '%s'" t)

    let encodeRunningApp (app: AppRun.RunningApp) =
      JsonValue.Object [
        "runId", encodeString app.RunId
        "project", encodeString app.Project
        "entryPoint", encodeString app.EntryPoint
        "endpoint", encodeAppEndpoint app.Endpoint
        "startedAt", encodeDateTime app.StartedAt
      ]

    let decodeRunningApp (v: JsonValue) : AppRun.RunningApp =
      { RunId = getProp "runId" v |> decodeString
        Project = getProp "project" v |> decodeString
        EntryPoint = getProp "entryPoint" v |> decodeString
        Endpoint = getProp "endpoint" v |> decodeAppEndpoint
        StartedAt = getProp "startedAt" v |> decodeDateTime }

    let encodeStartPhase (p: AppRun.StartPhase) =
      match p with
      | AppRun.StartPhase.RebuildingSession -> adj0 "RebuildingSession"
      | AppRun.StartPhase.LaunchingEntryPoint -> adj0 "LaunchingEntryPoint"

    let decodeStartPhase (v: JsonValue) =
      match readAdjTag v with
      | "RebuildingSession" -> AppRun.StartPhase.RebuildingSession
      | "LaunchingEntryPoint" -> AppRun.StartPhase.LaunchingEntryPoint
      | t -> failDecode (sprintf "unknown StartPhase tag '%s'" t)

    let encodeAppRunState (s: AppRun.AppRunState) =
      match s with
      | AppRun.AppRunState.NotRunning -> adj0 "NotRunning"
      | AppRun.AppRunState.Starting(proj, phase, since) ->
        adjN "Starting" [ encodeString proj; encodeStartPhase phase; encodeDateTime since ]
      | AppRun.AppRunState.Running app -> adj1 "Running" (encodeRunningApp app)
      | AppRun.AppRunState.Exited(proj, code, at) ->
        adjN "Exited" [ encodeString proj; encodeInt code; encodeDateTime at ]
      | AppRun.AppRunState.Crashed(proj, reason, at) ->
        adjN "Crashed" [ encodeString proj; encodeString reason; encodeDateTime at ]
      | AppRun.AppRunState.CouldNotStart(proj, reason, at) ->
        adjN "CouldNotStart" [ encodeString proj; encodeBozzettoError reason; encodeDateTime at ]
      | AppRun.AppRunState.BuildFailed(proj, reason, at, lastAddr) ->
        adjN "BuildFailed" [ encodeString proj; encodeString reason; encodeDateTime at; encodePreviousAddress lastAddr ]
      | AppRun.AppRunState.LostTrack(proj, reason, at) ->
        adjN "LostTrack" [ encodeString proj; encodeBozzettoError reason; encodeDateTime at ]

    let decodeAppRunState (v: JsonValue) : AppRun.AppRunState =
      match readAdjTag v with
      | "NotRunning" -> AppRun.AppRunState.NotRunning
      | "Starting" ->
        let proj, phase, since = readAdj3 decodeString decodeStartPhase decodeDateTime v
        AppRun.AppRunState.Starting(proj, phase, since)
      | "Running" -> AppRun.AppRunState.Running (decodeRunningApp (readAdj1 v))
      | "Exited" ->
        let proj, code, at = readAdj3 decodeString decodeInt decodeDateTime v
        AppRun.AppRunState.Exited(proj, code, at)
      | "Crashed" ->
        let proj, reason, at = readAdj3 decodeString decodeString decodeDateTime v
        AppRun.AppRunState.Crashed(proj, reason, at)
      | "CouldNotStart" ->
        let proj, reason, at = readAdj3 decodeString decodeBozzettoError decodeDateTime v
        AppRun.AppRunState.CouldNotStart(proj, reason, at)
      | "BuildFailed" ->
        let proj, reason, at, lastAddr = readAdj4 decodeString decodeString decodeDateTime decodePreviousAddress v
        AppRun.AppRunState.BuildFailed(proj, reason, at, lastAddr)
      | "LostTrack" ->
        let proj, reason, at = readAdj3 decodeString decodeBozzettoError decodeDateTime v
        AppRun.AppRunState.LostTrack(proj, reason, at)
      | t -> failDecode (sprintf "unknown AppRunState tag '%s'" t)

    // ── Diagnostics, WorkerDiagnostic, WorkerSymbolRef, WorkerStatusSnapshot ──

    let encodeDiagnosticSeverity (s: Features.Diagnostics.DiagnosticSeverity) =
      match s with
      | Features.Diagnostics.DiagnosticSeverity.Blocking -> adj0 "Blocking"
      | Features.Diagnostics.DiagnosticSeverity.Hidden -> adj0 "Hidden"
      | Features.Diagnostics.DiagnosticSeverity.Info -> adj0 "Info"
      | Features.Diagnostics.DiagnosticSeverity.Warning -> adj0 "Warning"

    let decodeDiagnosticSeverity (v: JsonValue) =
      match readAdjTag v with
      | "Blocking" -> Features.Diagnostics.DiagnosticSeverity.Blocking
      | "Hidden" -> Features.Diagnostics.DiagnosticSeverity.Hidden
      | "Info" -> Features.Diagnostics.DiagnosticSeverity.Info
      | "Warning" -> Features.Diagnostics.DiagnosticSeverity.Warning
      | t -> failDecode (sprintf "unknown DiagnosticSeverity tag '%s'" t)

    let encodeWorkerDiagnostic (d: WorkerDiagnostic) =
      JsonValue.Object [
        "severity", encodeDiagnosticSeverity d.Severity
        "message", encodeString d.Message
        "startLine", encodeInt d.StartLine
        "startColumn", encodeInt d.StartColumn
        "endLine", encodeInt d.EndLine
        "endColumn", encodeInt d.EndColumn
        "errorNumber", encodeInt d.ErrorNumber
      ]

    let decodeWorkerDiagnostic (v: JsonValue) : WorkerDiagnostic =
      { Severity = getProp "severity" v |> decodeDiagnosticSeverity
        Message = getProp "message" v |> decodeString
        StartLine = getProp "startLine" v |> decodeInt
        StartColumn = getProp "startColumn" v |> decodeInt
        EndLine = getProp "endLine" v |> decodeInt
        EndColumn = getProp "endColumn" v |> decodeInt
        ErrorNumber = getProp "errorNumber" v |> decodeInt }

    let encodeWorkerSymbolRef (s: WorkerSymbolRef) =
      JsonValue.Object [
        "symbolFullName", encodeString s.SymbolFullName
        "isFromDefinition", encodeBool s.IsFromDefinition
        "filePath", encodeString s.FilePath
        "line", encodeInt s.Line
      ]

    let decodeWorkerSymbolRef (v: JsonValue) : WorkerSymbolRef =
      { SymbolFullName = getProp "symbolFullName" v |> decodeString
        IsFromDefinition = getProp "isFromDefinition" v |> decodeBool
        FilePath = getProp "filePath" v |> decodeString
        Line = getProp "line" v |> decodeInt }

    let encodeWorkerStatusSnapshot (s: WorkerStatusSnapshot) =
      JsonValue.Object [
        "status", encodeSessionStatus s.Status
        "statusMessage", encodeOption encodeString s.StatusMessage
        "evalCount", encodeInt s.EvalCount
        "avgDurationMs", encodeInt64 s.AvgDurationMs
        "minDurationMs", encodeInt64 s.MinDurationMs
        "maxDurationMs", encodeInt64 s.MaxDurationMs
        "projects", encodeList encodeClassifiedProject s.Projects
        "coreVersion", encodeString s.CoreVersion
      ]

    let decodeWorkerStatusSnapshot (v: JsonValue) : WorkerStatusSnapshot =
      { Status = getProp "status" v |> decodeSessionStatus
        StatusMessage = getProp "statusMessage" v |> decodeOption decodeString
        EvalCount = getProp "evalCount" v |> decodeInt
        AvgDurationMs = getProp "avgDurationMs" v |> decodeInt64
        MinDurationMs = getProp "minDurationMs" v |> decodeInt64
        MaxDurationMs = getProp "maxDurationMs" v |> decodeInt64
        Projects = getProp "projects" v |> decodeList decodeClassifiedProject
        CoreVersion = getProp "coreVersion" v |> decodeString }

    // ── Features.LiveTesting ──

    let encodeTestId (id: Features.LiveTesting.TestId) =
      adj1 "TestId" (encodeString (Features.LiveTesting.TestId.value id))

    let decodeTestId (v: JsonValue) =
      match readAdjTag v with
      | "TestId" -> Features.LiveTesting.TestId.TestId (decodeString (readAdj1 v))
      | t -> failDecode (sprintf "unknown TestId tag '%s'" t)

    let encodeTestFramework (fw: TestFramework) =
      match fw with
      | TestFramework.Expecto -> adj0 "Expecto"
      | TestFramework.XUnit -> adj0 "XUnit"
      | TestFramework.NUnit -> adj0 "NUnit"
      | TestFramework.MSTest -> adj0 "MSTest"
      | TestFramework.TUnit -> adj0 "TUnit"
      | TestFramework.Unknown s -> adj1 "Unknown" (encodeString s)

    let decodeTestFramework (v: JsonValue) =
      match readAdjTag v with
      | "Expecto" -> TestFramework.Expecto
      | "XUnit" -> TestFramework.XUnit
      | "NUnit" -> TestFramework.NUnit
      | "MSTest" -> TestFramework.MSTest
      | "TUnit" -> TestFramework.TUnit
      | "Unknown" -> TestFramework.Unknown (decodeString (readAdj1 v))
      | t -> failDecode (sprintf "unknown TestFramework tag '%s'" t)

    let encodeTestCategory (cat: Features.LiveTesting.TestCategory) =
      match cat with
      | Features.LiveTesting.TestCategory.Unit -> adj0 "Unit"
      | Features.LiveTesting.TestCategory.Integration -> adj0 "Integration"
      | Features.LiveTesting.TestCategory.Browser -> adj0 "Browser"
      | Features.LiveTesting.TestCategory.Benchmark -> adj0 "Benchmark"
      | Features.LiveTesting.TestCategory.Architecture -> adj0 "Architecture"
      | Features.LiveTesting.TestCategory.Property -> adj0 "Property"
      | Features.LiveTesting.TestCategory.Custom s -> adj1 "Custom" (encodeString s)

    let decodeTestCategory (v: JsonValue) =
      match readAdjTag v with
      | "Unit" -> Features.LiveTesting.TestCategory.Unit
      | "Integration" -> Features.LiveTesting.TestCategory.Integration
      | "Browser" -> Features.LiveTesting.TestCategory.Browser
      | "Benchmark" -> Features.LiveTesting.TestCategory.Benchmark
      | "Architecture" -> Features.LiveTesting.TestCategory.Architecture
      | "Property" -> Features.LiveTesting.TestCategory.Property
      | "Custom" -> Features.LiveTesting.TestCategory.Custom (decodeString (readAdj1 v))
      | t -> failDecode (sprintf "unknown TestCategory tag '%s'" t)

    let encodeTestOrigin (orig: Features.LiveTesting.TestOrigin) =
      match orig with
      | Features.LiveTesting.TestOrigin.SourceMapped(file, line) ->
        adjN "SourceMapped" [ encodeString file; encodeInt line ]
      | Features.LiveTesting.TestOrigin.ReflectionOnly -> adj0 "ReflectionOnly"

    let decodeTestOrigin (v: JsonValue) =
      match readAdjTag v with
      | "SourceMapped" ->
        let file, line = readAdj2 decodeString decodeInt v
        Features.LiveTesting.TestOrigin.SourceMapped(file, line)
      | "ReflectionOnly" -> Features.LiveTesting.TestOrigin.ReflectionOnly
      | t -> failDecode (sprintf "unknown TestOrigin tag '%s'" t)

    let encodeTestCase (tc: Features.LiveTesting.TestCase) =
      JsonValue.Object [
        "id", encodeTestId tc.Id
        "fullName", encodeString tc.FullName
        "displayName", encodeString tc.DisplayName
        "origin", encodeTestOrigin tc.Origin
        "labels", encodeList encodeString tc.Labels
        "framework", encodeTestFramework tc.Framework
        "category", encodeTestCategory tc.Category
      ]

    let decodeTestCase (v: JsonValue) : Features.LiveTesting.TestCase =
      { Id = getProp "id" v |> decodeTestId
        FullName = getProp "fullName" v |> decodeString
        DisplayName = getProp "displayName" v |> decodeString
        Origin = getProp "origin" v |> decodeTestOrigin
        Labels = getProp "labels" v |> decodeList decodeString
        Framework = getProp "framework" v |> decodeTestFramework
        Category = getProp "category" v |> decodeTestCategory }

    let encodeTestFailure (f: Features.LiveTesting.TestFailure) =
      match f with
      | Features.LiveTesting.TestFailure.AssertionFailed msg -> adj1 "AssertionFailed" (encodeString msg)
      | Features.LiveTesting.TestFailure.ExceptionThrown(msg, stack) ->
        adjN "ExceptionThrown" [ encodeString msg; encodeString stack ]
      | Features.LiveTesting.TestFailure.TimedOut after -> adj1 "TimedOut" (encodeTimeSpan after)

    let decodeTestFailure (v: JsonValue) =
      match readAdjTag v with
      | "AssertionFailed" -> Features.LiveTesting.TestFailure.AssertionFailed (decodeString (readAdj1 v))
      | "ExceptionThrown" ->
        let msg, stack = readAdj2 decodeString decodeString v
        Features.LiveTesting.TestFailure.ExceptionThrown(msg, stack)
      | "TimedOut" -> Features.LiveTesting.TestFailure.TimedOut (decodeTimeSpan (readAdj1 v))
      | t -> failDecode (sprintf "unknown TestFailure tag '%s'" t)

    let encodeNoResultReason (r: Features.LiveTesting.NoResultReason) =
      match r with
      | Features.LiveTesting.NoResultReason.StreamEnded -> adj0 "StreamEnded"
      | Features.LiveTesting.NoResultReason.StreamStalled after -> adj1 "StreamStalled" (encodeTimeSpan after)
      | Features.LiveTesting.NoResultReason.TransportFailed msg -> adj1 "TransportFailed" (encodeString msg)
      | Features.LiveTesting.NoResultReason.RunCancelled -> adj0 "RunCancelled"

    let decodeNoResultReason (v: JsonValue) =
      match readAdjTag v with
      | "StreamEnded" -> Features.LiveTesting.NoResultReason.StreamEnded
      | "StreamStalled" -> Features.LiveTesting.NoResultReason.StreamStalled (decodeTimeSpan (readAdj1 v))
      | "TransportFailed" -> Features.LiveTesting.NoResultReason.TransportFailed (decodeString (readAdj1 v))
      | "RunCancelled" -> Features.LiveTesting.NoResultReason.RunCancelled
      | t -> failDecode (sprintf "unknown NoResultReason tag '%s'" t)

    let encodeTestResult (r: Features.LiveTesting.TestResult) =
      match r with
      | Features.LiveTesting.TestResult.Passed dur -> adj1 "Passed" (encodeTimeSpan dur)
      | Features.LiveTesting.TestResult.Failed(failure, dur) ->
        adjN "Failed" [ encodeTestFailure failure; encodeTimeSpan dur ]
      | Features.LiveTesting.TestResult.Skipped reason -> adj1 "Skipped" (encodeString reason)
      | Features.LiveTesting.TestResult.NotRun -> adj0 "NotRun"
      | Features.LiveTesting.TestResult.NoResult reason -> adj1 "NoResult" (encodeNoResultReason reason)

    let decodeTestResult (v: JsonValue) =
      match readAdjTag v with
      | "Passed" -> Features.LiveTesting.TestResult.Passed (decodeTimeSpan (readAdj1 v))
      | "Failed" ->
        let failure, dur = readAdj2 decodeTestFailure decodeTimeSpan v
        Features.LiveTesting.TestResult.Failed(failure, dur)
      | "Skipped" -> Features.LiveTesting.TestResult.Skipped (decodeString (readAdj1 v))
      | "NotRun" -> Features.LiveTesting.TestResult.NotRun
      | "NoResult" -> Features.LiveTesting.TestResult.NoResult (decodeNoResultReason (readAdj1 v))
      | t -> failDecode (sprintf "unknown TestResult tag '%s'" t)

    let encodeTestRunResult (r: Features.LiveTesting.TestRunResult) =
      JsonValue.Object [
        "testId", encodeTestId r.TestId
        "testName", encodeString r.TestName
        "result", encodeTestResult r.Result
        "timestamp", encodeDateTimeOffset r.Timestamp
        "output", encodeOption encodeString r.Output
      ]

    let decodeTestRunResult (v: JsonValue) : Features.LiveTesting.TestRunResult =
      { TestId = getProp "testId" v |> decodeTestId
        TestName = getProp "testName" v |> decodeString
        Result = getProp "result" v |> decodeTestResult
        Timestamp = getProp "timestamp" v |> decodeDateTimeOffset
        Output = getProp "output" v |> decodeOption decodeString }

    let encodeAttributeProviderDescription (p: Features.LiveTesting.AttributeProviderDescription) =
      JsonValue.Object [
        "name", encodeTestFramework p.Name
        "testAttributes", encodeList encodeString p.TestAttributes
        "assemblyMarker", encodeString p.AssemblyMarker
      ]

    let decodeAttributeProviderDescription (v: JsonValue) : Features.LiveTesting.AttributeProviderDescription =
      { Name = getProp "name" v |> decodeTestFramework
        TestAttributes = getProp "testAttributes" v |> decodeList decodeString
        AssemblyMarker = getProp "assemblyMarker" v |> decodeString }

    let encodeCustomProviderDescription (p: Features.LiveTesting.CustomProviderDescription) =
      JsonValue.Object [
        "name", encodeTestFramework p.Name
        "assemblyMarker", encodeString p.AssemblyMarker
      ]

    let decodeCustomProviderDescription (v: JsonValue) : Features.LiveTesting.CustomProviderDescription =
      { Name = getProp "name" v |> decodeTestFramework
        AssemblyMarker = getProp "assemblyMarker" v |> decodeString }

    let encodeProviderDescription (p: Features.LiveTesting.ProviderDescription) =
      match p with
      | Features.LiveTesting.ProviderDescription.AttributeBased a ->
        adj1 "AttributeBased" (encodeAttributeProviderDescription a)
      | Features.LiveTesting.ProviderDescription.Custom c ->
        adj1 "Custom" (encodeCustomProviderDescription c)

    let decodeProviderDescription (v: JsonValue) =
      match readAdjTag v with
      | "AttributeBased" ->
        Features.LiveTesting.ProviderDescription.AttributeBased (decodeAttributeProviderDescription (readAdj1 v))
      | "Custom" ->
        Features.LiveTesting.ProviderDescription.Custom (decodeCustomProviderDescription (readAdj1 v))
      | t -> failDecode (sprintf "unknown ProviderDescription tag '%s'" t)

    let encodeSequencePoint (sp: Features.LiveTesting.SequencePoint) =
      JsonValue.Object [
        "file", encodeString sp.File
        "line", encodeInt sp.Line
        "column", encodeInt sp.Column
        "endLine", encodeInt sp.EndLine
        "endColumn", encodeInt sp.EndColumn
        "branchId", encodeInt sp.BranchId
      ]

    let decodeSequencePoint (v: JsonValue) : Features.LiveTesting.SequencePoint =
      { File = getProp "file" v |> decodeString
        Line = getProp "line" v |> decodeInt
        Column = getProp "column" v |> decodeInt
        EndLine = getProp "endLine" v |> decodeInt
        EndColumn = getProp "endColumn" v |> decodeInt
        BranchId = getProp "branchId" v |> decodeInt }

    let encodeInstrumentationMap (m: Features.LiveTesting.InstrumentationMap) =
      JsonValue.Object [
        "slots", encodeArray encodeSequencePoint m.Slots
        "totalProbes", encodeInt m.TotalProbes
        "trackerTypeName", encodeString m.TrackerTypeName
        "hitsFieldName", encodeString m.HitsFieldName
      ]

    let decodeInstrumentationMap (v: JsonValue) : Features.LiveTesting.InstrumentationMap =
      { Slots = getProp "slots" v |> decodeArray decodeSequencePoint
        TotalProbes = getProp "totalProbes" v |> decodeInt
        TrackerTypeName = getProp "trackerTypeName" v |> decodeString
        HitsFieldName = getProp "hitsFieldName" v |> decodeString }

    let encodeLiveTestHookResultDto (dto: Features.LiveTesting.LiveTestHookResultDto) =
      JsonValue.Object [
        "detectedProviders", encodeList encodeProviderDescription dto.DetectedProviders
        "discoveredTests", encodeArray encodeTestCase dto.DiscoveredTests
        "affectedTestIds", encodeArray encodeTestId dto.AffectedTestIds
        "changedSymbolNames", encodeArray encodeString dto.ChangedSymbolNames
      ]

    let decodeLiveTestHookResultDto (v: JsonValue) : Features.LiveTesting.LiveTestHookResultDto =
      { DetectedProviders = getProp "detectedProviders" v |> decodeList decodeProviderDescription
        DiscoveredTests = getProp "discoveredTests" v |> decodeArray decodeTestCase
        AffectedTestIds = getProp "affectedTestIds" v |> decodeArray decodeTestId
        ChangedSymbolNames = getProp "changedSymbolNames" v |> decodeArray decodeString }

    let encodeAssemblyLoadError (e: Features.LiveTesting.AssemblyLoadError) =
      match e with
      | Features.LiveTesting.AssemblyLoadError.FileNotFound(p, m) -> adjN "FileNotFound" [ encodeString p; encodeString m ]
      | Features.LiveTesting.AssemblyLoadError.LoadFailed(p, m) -> adjN "LoadFailed" [ encodeString p; encodeString m ]
      | Features.LiveTesting.AssemblyLoadError.BadImage(p, m) -> adjN "BadImage" [ encodeString p; encodeString m ]

    let decodeAssemblyLoadError (v: JsonValue) =
      match readAdjTag v with
      | "FileNotFound" ->
        let p, m = readAdj2 decodeString decodeString v
        Features.LiveTesting.AssemblyLoadError.FileNotFound(p, m)
      | "LoadFailed" ->
        let p, m = readAdj2 decodeString decodeString v
        Features.LiveTesting.AssemblyLoadError.LoadFailed(p, m)
      | "BadImage" ->
        let p, m = readAdj2 decodeString decodeString v
        Features.LiveTesting.AssemblyLoadError.BadImage(p, m)
      | t -> failDecode (sprintf "unknown AssemblyLoadError tag '%s'" t)

    // ── Features.LiveValueTree ──

    let encodeNodeKind (k: Features.LiveValueTree.NodeKind) =
      match k with
      | Features.LiveValueTree.NodeKind.Leaf -> adj0 "Leaf"
      | Features.LiveValueTree.NodeKind.Record -> adj0 "Record"
      | Features.LiveValueTree.NodeKind.List -> adj0 "List"
      | Features.LiveValueTree.NodeKind.Map -> adj0 "Map"
      | Features.LiveValueTree.NodeKind.Option -> adj0 "Option"
      | Features.LiveValueTree.NodeKind.Union -> adj0 "Union"
      | Features.LiveValueTree.NodeKind.Tuple -> adj0 "Tuple"
      | Features.LiveValueTree.NodeKind.Array -> adj0 "Array"
      | Features.LiveValueTree.NodeKind.Class -> adj0 "Class"
      | Features.LiveValueTree.NodeKind.Closure -> adj0 "Closure"
      | Features.LiveValueTree.NodeKind.Cycle -> adj0 "Cycle"
      | Features.LiveValueTree.NodeKind.Truncated -> adj0 "Truncated"

    let decodeNodeKind (v: JsonValue) =
      match readAdjTag v with
      | "Leaf" -> Features.LiveValueTree.NodeKind.Leaf
      | "Record" -> Features.LiveValueTree.NodeKind.Record
      | "List" -> Features.LiveValueTree.NodeKind.List
      | "Map" -> Features.LiveValueTree.NodeKind.Map
      | "Option" -> Features.LiveValueTree.NodeKind.Option
      | "Union" -> Features.LiveValueTree.NodeKind.Union
      | "Tuple" -> Features.LiveValueTree.NodeKind.Tuple
      | "Array" -> Features.LiveValueTree.NodeKind.Array
      | "Class" -> Features.LiveValueTree.NodeKind.Class
      | "Closure" -> Features.LiveValueTree.NodeKind.Closure
      | "Cycle" -> Features.LiveValueTree.NodeKind.Cycle
      | "Truncated" -> Features.LiveValueTree.NodeKind.Truncated
      | t -> failDecode (sprintf "unknown NodeKind tag '%s'" t)

    let rec encodeLiveValueNode (n: Features.LiveValueTree.LiveValueNode) =
      JsonValue.Object [
        "label", encodeString n.Label
        "typeName", encodeString n.TypeName
        "preview", encodeString n.Preview
        "kind", encodeNodeKind n.Kind
        "children", encodeList encodeLiveValueNode n.Children
        "bestEffort", encodeBool n.BestEffort
        "depth", encodeInt n.Depth
      ]

    let rec decodeLiveValueNode (v: JsonValue) : Features.LiveValueTree.LiveValueNode =
      { Label = getProp "label" v |> decodeString
        TypeName = getProp "typeName" v |> decodeString
        Preview = getProp "preview" v |> decodeString
        Kind = getProp "kind" v |> decodeNodeKind
        Children = getProp "children" v |> decodeList decodeLiveValueNode
        BestEffort = getProp "bestEffort" v |> decodeBool
        Depth = getProp "depth" v |> decodeInt }

    let encodeLiveBindingValue (b: Features.LiveValueTree.LiveBindingValue) =
      JsonValue.Object [
        "name", encodeString b.Name
        "typeSignature", encodeString b.TypeSignature
        "root", encodeLiveValueNode b.Root
      ]

    let decodeLiveBindingValue (v: JsonValue) : Features.LiveValueTree.LiveBindingValue =
      { Name = getProp "name" v |> decodeString
        TypeSignature = getProp "typeSignature" v |> decodeString
        Root = getProp "root" v |> decodeLiveValueNode }

    let encodeLiveValueSnapshot (s: Features.LiveValueTree.LiveValueSnapshot) =
      JsonValue.Object [
        "sessionId", encodeString s.SessionId
        "generation", encodeInt64 s.Generation
        "bindings", encodeList encodeLiveBindingValue s.Bindings
        "truncated", encodeBool s.Truncated
        "capturedAt", encodeDateTimeOffset s.CapturedAt
      ]

    let decodeLiveValueSnapshot (v: JsonValue) : Features.LiveValueTree.LiveValueSnapshot =
      { SessionId = getProp "sessionId" v |> decodeString
        Generation = getProp "generation" v |> decodeInt64
        Bindings = getProp "bindings" v |> decodeList decodeLiveBindingValue
        Truncated = getProp "truncated" v |> decodeBool
        CapturedAt = getProp "capturedAt" v |> decodeDateTimeOffset }

    // ── WarmupContext ──

    let encodeLoadedAssembly (a: LoadedAssembly) =
      JsonValue.Object [
        "name", encodeString a.Name
        "path", encodeString a.Path
        "namespaceCount", encodeInt a.NamespaceCount
        "moduleCount", encodeInt a.ModuleCount
      ]

    let decodeLoadedAssembly (v: JsonValue) : LoadedAssembly =
      { Name = getProp "name" v |> decodeString
        Path = getProp "path" v |> decodeString
        NamespaceCount = getProp "namespaceCount" v |> decodeInt
        ModuleCount = getProp "moduleCount" v |> decodeInt }

    let encodeOpenableKind (k: WarmUp.OpenableKind) =
      match k with
      | WarmUp.OpenableKind.Module -> adj0 "Module"
      | WarmUp.OpenableKind.Namespace -> adj0 "Namespace"

    let decodeOpenableKind (v: JsonValue) =
      match readAdjTag v with
      | "Module" -> WarmUp.OpenableKind.Module
      | "Namespace" -> WarmUp.OpenableKind.Namespace
      | t -> failDecode (sprintf "unknown OpenableKind tag '%s'" t)

    let encodeOpenedBinding (b: WarmUp.OpenedBinding) =
      JsonValue.Object [
        "name", encodeString b.Name
        "kind", encodeOpenableKind b.Kind
        "source", encodeString b.Source
        "durationMs", encodeFloat b.DurationMs
      ]

    let decodeOpenedBinding (v: JsonValue) : WarmUp.OpenedBinding =
      { Name = getProp "name" v |> decodeString
        Kind = getProp "kind" v |> decodeOpenableKind
        Source = getProp "source" v |> decodeString
        DurationMs = getProp "durationMs" v |> decodeFloat }

    let encodeWarmupFcsDiagnostic (d: WarmUp.WarmupFcsDiagnostic) =
      JsonValue.Object [
        "message", encodeString d.Message
        "severity", encodeString d.Severity
        "errorNumber", encodeInt d.ErrorNumber
        "fileName", encodeOption encodeString d.FileName
        "startLine", encodeInt d.StartLine
        "endLine", encodeInt d.EndLine
        "startColumn", encodeInt d.StartColumn
        "endColumn", encodeInt d.EndColumn
      ]

    let decodeWarmupFcsDiagnostic (v: JsonValue) : WarmUp.WarmupFcsDiagnostic =
      { Message = getProp "message" v |> decodeString
        Severity = getProp "severity" v |> decodeString
        ErrorNumber = getProp "errorNumber" v |> decodeInt
        FileName = getProp "fileName" v |> decodeOption decodeString
        StartLine = getProp "startLine" v |> decodeInt
        EndLine = getProp "endLine" v |> decodeInt
        StartColumn = getProp "startColumn" v |> decodeInt
        EndColumn = getProp "endColumn" v |> decodeInt }

    let encodeWarmupOpenFailure (f: WarmUp.WarmupOpenFailure) =
      JsonValue.Object [
        "name", encodeString f.Name
        "kind", encodeOpenableKind f.Kind
        "errorMessage", encodeString f.ErrorMessage
        "diagnostics", encodeList encodeWarmupFcsDiagnostic f.Diagnostics
        "retryCount", encodeInt f.RetryCount
        "durationMs", encodeFloat f.DurationMs
      ]

    let decodeWarmupOpenFailure (v: JsonValue) : WarmUp.WarmupOpenFailure =
      { Name = getProp "name" v |> decodeString
        Kind = getProp "kind" v |> decodeOpenableKind
        ErrorMessage = getProp "errorMessage" v |> decodeString
        Diagnostics = getProp "diagnostics" v |> decodeList decodeWarmupFcsDiagnostic
        RetryCount = getProp "retryCount" v |> decodeInt
        DurationMs = getProp "durationMs" v |> decodeFloat }

    let encodeWarmupPhaseTiming (t: WarmUp.WarmupPhaseTiming) =
      JsonValue.Object [
        "scanSourceFilesMs", encodeInt64 t.ScanSourceFilesMs
        "scanAssembliesMs", encodeInt64 t.ScanAssembliesMs
        "openNamespacesMs", encodeInt64 t.OpenNamespacesMs
        "totalMs", encodeInt64 t.TotalMs
      ]

    let decodeWarmupPhaseTiming (v: JsonValue) : WarmUp.WarmupPhaseTiming =
      { ScanSourceFilesMs = getProp "scanSourceFilesMs" v |> decodeInt64
        ScanAssembliesMs = getProp "scanAssembliesMs" v |> decodeInt64
        OpenNamespacesMs = getProp "openNamespacesMs" v |> decodeInt64
        TotalMs = getProp "totalMs" v |> decodeInt64 }

    let encodeWarmupContext (ctx: WarmupContext) =
      JsonValue.Object [
        "sourceFilesScanned", encodeInt ctx.SourceFilesScanned
        "assembliesLoaded", encodeList encodeLoadedAssembly ctx.AssembliesLoaded
        "namespacesOpened", encodeList encodeOpenedBinding ctx.NamespacesOpened
        "failedOpens", encodeList encodeWarmupOpenFailure ctx.FailedOpens
        "phaseTiming", encodeWarmupPhaseTiming ctx.PhaseTiming
        "startedAt", encodeDateTimeOffset ctx.StartedAt
      ]

    let decodeWarmupContext (v: JsonValue) : WarmupContext =
      { SourceFilesScanned = getProp "sourceFilesScanned" v |> decodeInt
        AssembliesLoaded = getProp "assembliesLoaded" v |> decodeList decodeLoadedAssembly
        NamespacesOpened = getProp "namespacesOpened" v |> decodeList decodeOpenedBinding
        FailedOpens = getProp "failedOpens" v |> decodeList decodeWarmupOpenFailure
        PhaseTiming = getProp "phaseTiming" v |> decodeWarmupPhaseTiming
        StartedAt = getProp "startedAt" v |> decodeDateTimeOffset }

    // ── WorkerMessage, WorkerResponse, SessionInfo ──

    let encodeWorkerMessage (msg: WorkerMessage) =
      match msg with
      | WorkerMessage.EvalCode(code, replyId) -> adjN "EvalCode" [ encodeString code; encodeString replyId ]
      | WorkerMessage.CheckCode(code, replyId) -> adjN "CheckCode" [ encodeString code; encodeString replyId ]
      | WorkerMessage.TypeCheckWithSymbols(code, filePath, replyId) ->
        adjN "TypeCheckWithSymbols" [ encodeString code; encodeString filePath; encodeString replyId ]
      | WorkerMessage.GetCompletions(code, cursorPos, replyId) ->
        adjN "GetCompletions" [ encodeString code; encodeInt cursorPos; encodeString replyId ]
      | WorkerMessage.CancelEval -> adj0 "CancelEval"
      | WorkerMessage.LoadScript(filePath, replyId) -> adjN "LoadScript" [ encodeString filePath; encodeString replyId ]
      | WorkerMessage.ResetSession replyId -> adj1 "ResetSession" (encodeString replyId)
      | WorkerMessage.HardResetSession(rebuild, replyId) -> adjN "HardResetSession" [ encodeBool rebuild; encodeString replyId ]
      | WorkerMessage.GetStatus replyId -> adj1 "GetStatus" (encodeString replyId)
      | WorkerMessage.GetLiveValues replyId -> adj1 "GetLiveValues" (encodeString replyId)
      | WorkerMessage.RunTests(tests, maxParallelism, replyId) ->
        adjN "RunTests" [ encodeArray encodeTestCase tests; encodeInt maxParallelism; encodeString replyId ]
      | WorkerMessage.GetTestDiscovery replyId -> adj1 "GetTestDiscovery" (encodeString replyId)
      | WorkerMessage.EvalLiveTestFile(filePath, content, replyId) ->
        adjN "EvalLiveTestFile" [ encodeString filePath; encodeString content; encodeString replyId ]
      | WorkerMessage.GetInstrumentationMaps replyId -> adj1 "GetInstrumentationMaps" (encodeString replyId)
      | WorkerMessage.RunApp(project, previous, replyId) ->
        adjN "RunApp" [ encodeString project; encodePreviousAddress previous; encodeString replyId ]
      | WorkerMessage.StopApp(scope, replyId) -> adjN "StopApp" [ encodeStopScope scope; encodeString replyId ]
      | WorkerMessage.AwaitAppChange(runId, replyId) -> adjN "AwaitAppChange" [ encodeString runId; encodeString replyId ]
      | WorkerMessage.Shutdown -> adj0 "Shutdown"

    let decodeWorkerMessage (v: JsonValue) : WorkerMessage =
      match readAdjTag v with
      | "EvalCode" ->
        let code, replyId = readAdj2 decodeString decodeString v
        WorkerMessage.EvalCode(code, replyId)
      | "CheckCode" ->
        let code, replyId = readAdj2 decodeString decodeString v
        WorkerMessage.CheckCode(code, replyId)
      | "TypeCheckWithSymbols" ->
        let code, filePath, replyId = readAdj3 decodeString decodeString decodeString v
        WorkerMessage.TypeCheckWithSymbols(code, filePath, replyId)
      | "GetCompletions" ->
        let code, cursorPos, replyId = readAdj3 decodeString decodeInt decodeString v
        WorkerMessage.GetCompletions(code, cursorPos, replyId)
      | "CancelEval" -> WorkerMessage.CancelEval
      | "LoadScript" ->
        let filePath, replyId = readAdj2 decodeString decodeString v
        WorkerMessage.LoadScript(filePath, replyId)
      | "ResetSession" -> WorkerMessage.ResetSession (decodeString (readAdj1 v))
      | "HardResetSession" ->
        let rebuild, replyId = readAdj2 decodeBool decodeString v
        WorkerMessage.HardResetSession(rebuild, replyId)
      | "GetStatus" -> WorkerMessage.GetStatus (decodeString (readAdj1 v))
      | "GetLiveValues" -> WorkerMessage.GetLiveValues (decodeString (readAdj1 v))
      | "RunTests" ->
        let tests, maxParallelism, replyId = readAdj3 (decodeArray decodeTestCase) decodeInt decodeString v
        WorkerMessage.RunTests(tests, maxParallelism, replyId)
      | "GetTestDiscovery" -> WorkerMessage.GetTestDiscovery (decodeString (readAdj1 v))
      | "EvalLiveTestFile" ->
        let filePath, content, replyId = readAdj3 decodeString decodeString decodeString v
        WorkerMessage.EvalLiveTestFile(filePath, content, replyId)
      | "GetInstrumentationMaps" -> WorkerMessage.GetInstrumentationMaps (decodeString (readAdj1 v))
      | "RunApp" ->
        let project, previous, replyId = readAdj3 decodeString decodePreviousAddress decodeString v
        WorkerMessage.RunApp(project, previous, replyId)
      | "StopApp" ->
        let scope, replyId = readAdj2 decodeStopScope decodeString v
        WorkerMessage.StopApp(scope, replyId)
      | "AwaitAppChange" ->
        let runId, replyId = readAdj2 decodeString decodeString v
        WorkerMessage.AwaitAppChange(runId, replyId)
      | "Shutdown" -> WorkerMessage.Shutdown
      | t -> failDecode (sprintf "unknown WorkerMessage tag '%s'" t)

    let encodeWorkerResponse (resp: WorkerResponse) =
      match resp with
      | WorkerResponse.EvalResult(replyId, result, diagnostics, metadata) ->
        adjN "EvalResult" [
          encodeString replyId
          encodeResult encodeString encodeBozzettoError result
          encodeList encodeWorkerDiagnostic diagnostics
          encodeStringMap metadata
        ]
      | WorkerResponse.CheckResult(replyId, diagnostics) ->
        adjN "CheckResult" [ encodeString replyId; encodeList encodeWorkerDiagnostic diagnostics ]
      | WorkerResponse.TypeCheckWithSymbolsResult(replyId, diagnostics, symbolRefs) ->
        adjN "TypeCheckWithSymbolsResult" [
          encodeString replyId
          encodeList encodeWorkerDiagnostic diagnostics
          encodeList encodeWorkerSymbolRef symbolRefs
        ]
      | WorkerResponse.CompletionResult(replyId, completions) ->
        adjN "CompletionResult" [ encodeString replyId; encodeList encodeString completions ]
      | WorkerResponse.StatusResult(replyId, status) ->
        adjN "StatusResult" [ encodeString replyId; encodeWorkerStatusSnapshot status ]
      | WorkerResponse.LiveValuesResult(replyId, snapshotJson) ->
        adjN "LiveValuesResult" [ encodeString replyId; encodeString snapshotJson ]
      | WorkerResponse.EvalCancelled wasRunning -> adj1 "EvalCancelled" (encodeBool wasRunning)
      | WorkerResponse.ResetResult(replyId, result) ->
        adjN "ResetResult" [ encodeString replyId; encodeResult encodeUnit encodeBozzettoError result ]
      | WorkerResponse.HardResetResult(replyId, result) ->
        adjN "HardResetResult" [ encodeString replyId; encodeResult encodeString encodeBozzettoError result ]
      | WorkerResponse.ScriptLoaded(replyId, result) ->
        adjN "ScriptLoaded" [ encodeString replyId; encodeResult encodeString encodeBozzettoError result ]
      | WorkerResponse.TestRunResults(replyId, results) ->
        adjN "TestRunResults" [ encodeString replyId; encodeArray encodeTestRunResult results ]
      | WorkerResponse.InitialTestDiscovery(tests, providers) ->
        adjN "InitialTestDiscovery" [ encodeArray encodeTestCase tests; encodeList encodeProviderDescription providers ]
      | WorkerResponse.EvalLiveTestFileResult(replyId, result) ->
        adjN "EvalLiveTestFileResult" [
          encodeString replyId
          encodeResult (encodePair (encodeArray encodeTestCase) (encodeList encodeProviderDescription)) encodeBozzettoError result
        ]
      | WorkerResponse.InstrumentationMapsResult(replyId, maps) ->
        adjN "InstrumentationMapsResult" [ encodeString replyId; encodeArray encodeInstrumentationMap maps ]
      | WorkerResponse.AppRunResult(replyId, result) ->
        adjN "AppRunResult" [ encodeString replyId; encodeResult encodeAppRunState encodeBozzettoError result ]
      | WorkerResponse.WorkerReady -> adj0 "WorkerReady"
      | WorkerResponse.WorkerShuttingDown -> adj0 "WorkerShuttingDown"
      | WorkerResponse.WorkerError err -> adj1 "WorkerError" (encodeBozzettoError err)

    let decodeWorkerResponse (v: JsonValue) : WorkerResponse =
      match readAdjTag v with
      | "EvalResult" ->
        let replyId, result, diags, meta =
          readAdj4 decodeString (decodeResult decodeString decodeBozzettoError) (decodeList decodeWorkerDiagnostic) decodeStringMap v
        WorkerResponse.EvalResult(replyId, result, diags, meta)
      | "CheckResult" ->
        let replyId, diags = readAdj2 decodeString (decodeList decodeWorkerDiagnostic) v
        WorkerResponse.CheckResult(replyId, diags)
      | "TypeCheckWithSymbolsResult" ->
        let replyId, diags, refs =
          readAdj3 decodeString (decodeList decodeWorkerDiagnostic) (decodeList decodeWorkerSymbolRef) v
        WorkerResponse.TypeCheckWithSymbolsResult(replyId, diags, refs)
      | "CompletionResult" ->
        let replyId, comps = readAdj2 decodeString (decodeList decodeString) v
        WorkerResponse.CompletionResult(replyId, comps)
      | "StatusResult" ->
        let replyId, status = readAdj2 decodeString decodeWorkerStatusSnapshot v
        WorkerResponse.StatusResult(replyId, status)
      | "LiveValuesResult" ->
        let replyId, json = readAdj2 decodeString decodeString v
        WorkerResponse.LiveValuesResult(replyId, json)
      | "EvalCancelled" -> WorkerResponse.EvalCancelled (decodeBool (readAdj1 v))
      | "ResetResult" ->
        let replyId, result = readAdj2 decodeString (decodeResult decodeUnit decodeBozzettoError) v
        WorkerResponse.ResetResult(replyId, result)
      | "HardResetResult" ->
        let replyId, result = readAdj2 decodeString (decodeResult decodeString decodeBozzettoError) v
        WorkerResponse.HardResetResult(replyId, result)
      | "ScriptLoaded" ->
        let replyId, result = readAdj2 decodeString (decodeResult decodeString decodeBozzettoError) v
        WorkerResponse.ScriptLoaded(replyId, result)
      | "TestRunResults" ->
        let replyId, results = readAdj2 decodeString (decodeArray decodeTestRunResult) v
        WorkerResponse.TestRunResults(replyId, results)
      | "InitialTestDiscovery" ->
        let tests, providers = readAdj2 (decodeArray decodeTestCase) (decodeList decodeProviderDescription) v
        WorkerResponse.InitialTestDiscovery(tests, providers)
      | "EvalLiveTestFileResult" ->
        let replyId, result =
          readAdj2 decodeString (decodeResult (decodePair (decodeArray decodeTestCase) (decodeList decodeProviderDescription)) decodeBozzettoError) v
        WorkerResponse.EvalLiveTestFileResult(replyId, result)
      | "InstrumentationMapsResult" ->
        let replyId, maps = readAdj2 decodeString (decodeArray decodeInstrumentationMap) v
        WorkerResponse.InstrumentationMapsResult(replyId, maps)
      | "AppRunResult" ->
        let replyId, result = readAdj2 decodeString (decodeResult decodeAppRunState decodeBozzettoError) v
        WorkerResponse.AppRunResult(replyId, result)
      | "WorkerReady" -> WorkerResponse.WorkerReady
      | "WorkerShuttingDown" -> WorkerResponse.WorkerShuttingDown
      | "WorkerError" -> WorkerResponse.WorkerError (decodeBozzettoError (readAdj1 v))
      | t -> failDecode (sprintf "unknown WorkerResponse tag '%s'" t)

    let encodeSessionInfo (info: SessionInfo) =
      JsonValue.Object [
        "id", encodeSessionId info.Id
        "name", encodeOption encodeString info.Name
        "projects", encodeList encodeString info.Projects
        "workingDirectory", encodeString info.WorkingDirectory
        "solutionRoot", encodeOption encodeString info.SolutionRoot
        "createdAt", encodeDateTime info.CreatedAt
        "lastActivity", encodeDateTime info.LastActivity
        "status", encodeSessionLifecycleStatus info.Status
        "workflow", encodeSessionWorkflow info.Workflow
        "activeProject", encodeOption encodeString info.ActiveProject
        "projectRoles", encodeList encodeClassifiedProject info.ProjectRoles
        "app", encodeAppRunState info.App
      ]

    let decodeSessionInfo (v: JsonValue) : SessionInfo =
      { Id = getProp "id" v |> decodeSessionId
        Name = getProp "name" v |> decodeOption decodeString
        Projects = getProp "projects" v |> decodeList decodeString
        WorkingDirectory = getProp "workingDirectory" v |> decodeString
        SolutionRoot = getProp "solutionRoot" v |> decodeOption decodeString
        CreatedAt = getProp "createdAt" v |> decodeDateTime
        LastActivity = getProp "lastActivity" v |> decodeDateTime
        Status = getProp "status" v |> decodeSessionLifecycleStatus
        Workflow = getProp "workflow" v |> decodeSessionWorkflow
        ActiveProject = getProp "activeProject" v |> decodeOption decodeString
        ProjectRoles = getProp "projectRoles" v |> decodeList decodeClassifiedProject
        App = getProp "app" v |> decodeAppRunState }

    /// Serialize a supported wire value to a JSON string using explicit Fidelity.Data.JSON codecs.
    let serialize<'T> (value: 'T) : string =
      let jv =
        match box value with
        | :? WorkerMessage as m -> encodeWorkerMessage m
        | :? WorkerResponse as r -> encodeWorkerResponse r
        | :? SessionInfo as s -> encodeSessionInfo s
        | :? WarmupContext as w -> encodeWarmupContext w
        | :? Features.LiveTesting.LiveTestHookResultDto as d -> encodeLiveTestHookResultDto d
        | :? Features.LiveValueTree.LiveValueSnapshot as l -> encodeLiveValueSnapshot l
        | :? (Features.LiveTesting.AssemblyLoadError list) as errs -> encodeList encodeAssemblyLoadError errs
        | _ -> invalidOp (sprintf "Unsupported type for WorkerProtocol.Serialization.serialize: %s" typeof<'T>.FullName)
      Json.serialize jv

    /// Deserialize a JSON string to a supported wire value using explicit Fidelity.Data.JSON codecs.
    let deserialize<'T> (json: string) : 'T =
      let root =
        match Json.parse json with
        | Ok v -> v
        | Error err -> invalidOp (sprintf "Invalid JSON in WorkerProtocol.Serialization.deserialize: %s" err)
      let t = typeof<'T>
      if t = typeof<WorkerMessage> then unbox<'T> (decodeWorkerMessage root)
      elif t = typeof<WorkerResponse> then unbox<'T> (decodeWorkerResponse root)
      elif t = typeof<SessionInfo> then unbox<'T> (decodeSessionInfo root)
      elif t = typeof<WarmupContext> then unbox<'T> (decodeWarmupContext root)
      elif t = typeof<Features.LiveTesting.LiveTestHookResultDto> then unbox<'T> (decodeLiveTestHookResultDto root)
      elif t = typeof<Features.LiveValueTree.LiveValueSnapshot> then unbox<'T> (decodeLiveValueSnapshot root)
      elif t = typeof<Features.LiveTesting.AssemblyLoadError list> then unbox<'T> (decodeList decodeAssemblyLoadError root)
      else invalidOp (sprintf "Unsupported type for WorkerProtocol.Serialization.deserialize: %s" t.FullName)
