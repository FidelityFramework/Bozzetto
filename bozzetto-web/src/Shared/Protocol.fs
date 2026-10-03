/// Bozzetto browser UI shared protocol: the single vocabulary of the bridge.
///
/// Compiled today by Fable (the frontend in src/Frontend, the mock bridge in
/// src/Mock, and the codec round-trip tests) and by .NET Bozzetto, which is
/// the backend-for-frontend until a Clef backend compiled by Composer takes
/// its place. The destination is WrenHello's: this same file compiled by both
/// Fable and Composer (see ../WrenHello/README.md, "The Bridge").
///
/// Types only, no behavior. Codecs are per-side module functions
/// (src/Frontend/Codec.fs here; the backend codec lives inside Bozzetto), so
/// each side stays inside its compiler's proven surface. When BAREWire's
/// encoder/decoder compile under both Fable and Composer, the codecs swap to
/// binary frames without touching this file or the UI.
///
/// Shape discipline, so Composer can compile this file later (BAREWire
/// docs/12 Intersection Subset.md; WrenHello docs/composer-findings.md):
///   - a union case carries at most one payload (`single-payload`); several
///     fields travel as a record, never as a tuple or named case fields
///   - every union has at least two cases (`two-cases`)
///   - arrays, not lists (`arrays`); no Map or Set (`no-map`)
///   - no recursive unions, generic records, interfaces, classes or members
///   - integers that cross the wire are int32 or int64, never `int`, which is
///     the platform word under Clef (`platform-word`)
///   - open vocabularies (refusal codes, lease kinds, health labels) are
///     strings, so a newer backend never makes an older page fail to decode
///   - type names avoid BAREWire's record names and Bozzetto's provider
///     names (Refusal, SessionAddress, ...) so the backend can open both
///
/// Wire format (interim: one JSON object per WebSocket text frame on the
/// daemon's `/ui/bridge`, MCP port; the binary BAREWire frame replaces it).
/// int64 values travel as decimal strings ("9223372036854775807"); int32
/// and float as JSON numbers; `option` as null or the value; arrays as JSON
/// arrays. A SessionTarget is flattened to {"host","epoch","session"} and a
/// WorkerTarget to {"host","epoch"}.
///
///   UI -> backend, every frame: {"correlation":<int32 > 0>,"command":<tag>,...}
///     "request_snapshot"
///     "open_project"    "project":<absolute .fidproj path>
///     "reserve"         "target":T, "label":<string>
///     "build"           "target":T, "reservation":<string>
///     "run"             "target":T, "arguments":[<string>...]
///     "cancel"          "target":T
///     "close_session"   "target":T
///     "retire_worker"   "worker":{"host","epoch"}
///
///   backend -> UI: {"event":<tag>,...}
///     "welcome"     "protocolVersion":3, "daemonVersion", "startedAtMs":<int64>
///     "snapshot"    "revision":<int64>, "worker":W, "sessions":[S...]
///                   W = {"state":"unconfigured"} | {"state":"idle"}
///                     | {"state":"running","host","epoch","compilerVersion","processId":<int32>|null}
///                   S = {"host","epoch","session","generation":<int64>,"project",
///                        "closed","busy","statusFresh","revocationPending",
///                        "cleanupPending","formatterCleanupPending",
///                        "current":A|null,"backendError","formatterError",
///                        "cleanupError","workerRetirementRequired",
///                        "statusError","workerError"}   (the last six: string|null)
///                   A = {"generation":<int64>,"sourceVersion","artifactPath",
///                        "artifactSha256","compiledObjects","reusedObjects",
///                        "retiredObjects","changedWitnesses"}   (counts: int32)
///     "accepted"    "correlation", "target":T, "completion":C
///                   C = {"kind":"session_opened"} | {"kind":"edit_reserved","reservation"}
///                     | {"kind":"reservation_built","artifact":A}
///                     | {"kind":"run_finished","exitCode"} | {"kind":"work_canceled"}
///                     | {"kind":"session_closed"} | {"kind":"worker_retired"}
///                     | {"kind":"snapshot_sent"}
///     "refused"     "correlation", "target":T, "code", "message"
///     "run_output"  "correlation", "target":T, "generation":<int64>,
///                   "sourceVersion", "exitCode", "standardOutput", "standardError"
///     "health"      "version", "processId", "mcpPort", "overall",
///                   "memoryPressure", "residentBytes":<int64>,
///                   "machineAvailableBytes":<int64>, "machineTotalBytes":<int64>,
///                   "cpuPercent", "processes":[{"processId","role",
///                   "residentBytes":<int64>,"cpuPercent"}],
///                   "alarms":[{"signal","state","message"}]
///     "leases"      "active":[{"id","kind","holder","grantedAtMs":<int64>,
///                   "expiresAtMs":<int64>}], "queued":[{"kind","holder",
///                   "requestedAtMs":<int64>}]
///     "uptime"      "uptimeMs":<int64>
///                   e.g. {"event":"uptime","uptimeMs":"<int64 decimal string>"}
///
/// Contract: every command frame is answered by exactly one "accepted" or
/// "refused" carrying its correlation ("run" sends "run_output" first). A
/// frame the backend cannot decode is refused with correlation 0 and an empty
/// target. "welcome" opens every connection, once. "snapshot", "health" and
/// "leases" are pushed on connect and in answer to "request_snapshot" (the
/// explicit pull); after that, only when their owner reports a discrete
/// change, never on a timer: a continuous figure (RSS, machine memory, CPU)
/// rides along in a "health" sent for another reason or pulled. The start
/// time travels once, in "welcome". "uptime" is the one clock-owned push: the
/// backend's clock ticks once a second while at least one page is connected
/// and each tick is pushed as it is, so the page shows a live uptime without
/// running a timer of its own. It is a push from the clock that owns the
/// value, never a poll: nothing is re-read or diffed to produce it.
module Bozzetto.Web.Shared.Protocol

/// Version of this vocabulary and its interim wire format, sent in Welcome.
/// 2: Welcome carries StartedAtMs; Health no longer carries an uptime.
/// 3: the Uptime event, pushed by the backend's clock while a page is connected.
[<Literal>]
let ProtocolVersion = 3

// ── Addresses ──────────────────────────────────────────────────────────────

/// A Composer worker's host and compiler epoch, exactly as the backend issued
/// them. Opaque to the UI.
type WorkerTarget = { Host: string; Epoch: string }

/// One Composer session's address: its worker plus the opaque session id.
type SessionTarget = { Worker: WorkerTarget; Session: string }

// ── Commands (UI -> backend) ──────────────────────────────────────────────

/// Reserve an edit before changing sources (composer_reserve_edit).
type ReserveEdit = { Target: SessionTarget; Label: string }

/// Build the revision a reservation admitted (composer_build). The UI keeps
/// the reservation token it was given; the backend does not report it back.
type BuildReserved = { Target: SessionTarget; Reservation: string }

/// Run the current accepted artifact with these arguments (composer_run_current).
type RunCurrent = { Target: SessionTarget; Arguments: string array }

/// What the UI asks of the backend. The backend is the update function.
type Command =
  /// Push Snapshot, Health and Leases now.
  | RequestSnapshot
  /// Open an absolute Clef .fidproj (composer_open_project).
  | OpenProject of string
  | Reserve of ReserveEdit
  | Build of BuildReserved
  | Run of RunCurrent
  /// Withdraw the current artifact and cancel outstanding work (composer_cancel).
  | Cancel of SessionTarget
  /// Logically close the session and begin cleanup (composer_close_session).
  | CloseSession of SessionTarget
  /// Retire every session of a worker before compiler replacement
  /// (composer_retire_worker).
  | RetireWorker of WorkerTarget

/// One frame from the UI: a command and the correlation the UI chose for it
/// (positive, unique per page lifetime). Its outcome echoes the correlation.
type CommandFrame = { Correlation: int32; Command: Command }

// ── Composer state (backend -> UI) ─────────────────────────────────────────

/// The accepted artifact a session currently holds, summarized for display.
type ArtifactSummary = {
  Generation: int64
  SourceVersion: string
  ArtifactPath: string
  ArtifactSha256: string
  CompiledObjects: int32
  ReusedObjects: int32
  RetiredObjects: int32
  ChangedWitnesses: int32
}

/// One session as the supervisor last observed it. Status grants nothing:
/// running still revalidates inputs and artifact bytes.
type SessionStatus = {
  Target: SessionTarget
  /// Adapter revision of the session authority.
  Generation: int64
  Project: string
  Closed: bool
  Busy: bool
  /// False while activity is in flight or the last status read failed.
  StatusFresh: bool
  RevocationPending: bool
  CleanupPending: bool
  FormatterCleanupPending: bool
  Current: ArtifactSummary option
  BackendError: string option
  FormatterError: string option
  CleanupError: string option
  WorkerRetirementRequired: string option
  StatusError: string option
  WorkerError: string option
}

/// The live compiler worker.
type WorkerInfo = {
  Target: WorkerTarget
  CompilerVersion: string
  ProcessId: int32 option
}

type WorkerState =
  /// BOZZETTO_COMPOSER_WORKER is not set; Composer is unavailable.
  | Unconfigured
  /// Configured, no live worker; opening a project starts one.
  | Idle
  | Running of WorkerInfo

/// The Composer directory: worker plus every observed session.
type ComposerSnapshot = {
  /// Supervisor change revision; increases on every shared change.
  Revision: int64
  Worker: WorkerState
  Sessions: SessionStatus array
}

// ── Command outcomes ───────────────────────────────────────────────────────

/// What an accepted command completed.
type Completion =
  | SessionOpened
  /// The opaque reservation token for a later Build.
  | EditReserved of string
  | ReservationBuilt of ArtifactSummary
  /// Exit code; the transcript arrives as a RunOutput event.
  | RunFinished of int32
  | WorkCanceled
  | SessionClosed
  | WorkerRetired
  | SnapshotSent

type CommandAcceptance = {
  Correlation: int32
  /// The authority the backend answered with (for OpenProject: the new session).
  Target: SessionTarget
  Completion: Completion
}

type CommandRefusal = {
  /// 0 when the backend could not decode the frame at all.
  Correlation: int32
  Target: SessionTarget
  /// Refusal code as Composer spells it ("busy", "invalid_reservation", ...).
  Code: string
  Message: string
}

/// A finished run's transcript.
type RunTranscript = {
  Correlation: int32
  Target: SessionTarget
  Generation: int64
  SourceVersion: string
  ExitCode: int32
  StandardOutput: string
  StandardError: string
}

// ── Daemon observation ─────────────────────────────────────────────────────

/// One process the daemon owns (itself, Composer workers, compiler children).
type ProcessSample = {
  ProcessId: int32
  Role: string
  ResidentBytes: int64
  CpuPercent: float
}

/// An anomaly verdict from the daemon's health watch.
type HealthAlarm = { Signal: string; State: string; Message: string }

/// The daemon's health. Pushed for its discrete facts (the verdict, the
/// memory pressure level, the process set, the alarms); the byte and CPU
/// figures are readings taken when it is sent, never a reason to send it.
type DaemonHealth = {
  Version: string
  ProcessId: int32
  McpPort: int32
  /// "Healthy", "Degraded", ... as the daemon labels it.
  Overall: string
  /// "Normal", "Tight" or "Critical" today; rendered as given.
  MemoryPressure: string
  ResidentBytes: int64
  MachineAvailableBytes: int64
  MachineTotalBytes: int64
  CpuPercent: float
  Processes: ProcessSample array
  Alarms: HealthAlarm array
}

/// A granted expensive-work lease. Id is a display identity, never the
/// release capability.
type LeaseGrant = {
  Id: string
  /// "full_build", "test_suite_run", ... as the lease pool spells it.
  Kind: string
  Holder: string
  /// Unix epoch milliseconds.
  GrantedAtMs: int64
  ExpiresAtMs: int64
}

/// A queued lease request.
type LeaseWait = { Kind: string; Holder: string; RequestedAtMs: int64 }

type LeaseBoard = { Active: LeaseGrant array; Queued: LeaseWait array }

/// First event on every connection, sent once. The daemon's start time is a
/// fact of its process, fixed for its lifetime like its version: a restart
/// drops the socket, and the reconnect's Welcome carries the new one.
type BridgeWelcome = {
  ProtocolVersion: int32
  DaemonVersion: string
  /// When the daemon process started, in Unix epoch milliseconds.
  StartedAtMs: int64
}

/// One tick of the backend's uptime clock. The clock owns the value and
/// pushes it once a second while at least one page is connected; with no page
/// connected, no clock runs.
type UptimeTick = {
  /// How long the daemon process has been up, in milliseconds.
  UptimeMs: int64
}

/// What the backend pushes to the UI. Events fold into the UI's model.
type Event =
  | Welcome of BridgeWelcome
  | Snapshot of ComposerSnapshot
  | Accepted of CommandAcceptance
  | Refused of CommandRefusal
  | RunOutput of RunTranscript
  | Health of DaemonHealth
  | Leases of LeaseBoard
  | Uptime of UptimeTick
