/// Shared typed binary contract for browser and peer observations.
/// BinaryCodec is the single CLR/Fable codec; no JSON compatibility path.
/// Observations convey facts and never grant compiler execution authority.
module Bozzetto.Web.Shared.Protocol

/// Binary envelope and vocabulary version, also carried in Welcome.
[<Literal>]
let ProtocolVersion = 10

/// Existing observation requests are unchanged; deployed v9 peers remain readable.
[<Literal>]
let LegacyProtocolVersion = 9

[<Literal>]
let MaxObservedPeers = 16

[<Literal>]
let MaxWorkSnapshotBytes = 128 * 1024

[<Literal>]
let MaxTranscriptPageBytes = 32768

[<Literal>]
let MaxTranscriptBytes = 65536

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

type NodeIdentity = { Node: string; Incarnation: string }
type NodeObservationRequest = { Node: string; Seconds: int32 }
type TranscriptStream = Stdout | Stderr
type TranscriptReadRequest = {
  Source: NodeIdentity; Journal: string; Sequence: int64; Stream: TranscriptStream
  Offset: int64; MaxBytes: int32
}
type TranscriptPage = {
  Journal: string; Sequence: int64; Stream: TranscriptStream; Offset: int64
  NextOffset: int64 option; TotalBytes: int64; Bytes: byte array; Truncated: bool
}
type TranscriptReadOutcome =
  | TranscriptPage of TranscriptPage
  | TranscriptRefused of code: string * message: string
type NodeTranscript = { Source: NodeIdentity; Correlation: int32; Outcome: TranscriptReadOutcome }
type PeerTranscriptRequest = { ProtocolVersion: int32; Request: TranscriptReadRequest }
type PeerTranscriptReply = { Source: NodeIdentity; Outcome: TranscriptReadOutcome }

/// What the UI asks of the backend. The backend is the update function.
type Command =
  /// Push Snapshot, Health and Leases now.
  | RequestSnapshot
  /// Explicit counter-only acquisition: 1–120 seconds, 0 to unsubscribe.
  | ObserveResources of int32
  | ObserveNodeResources of NodeObservationRequest
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
  | ReadNodeTranscript of TranscriptReadRequest

/// One frame from the UI: a command and the correlation the UI chose for it
/// (positive, unique per page lifetime). Its outcome echoes the correlation.
type CommandFrame = { Correlation: int32; Command: Command }

// ── Composer state (backend -> UI) ─────────────────────────────────────────

/// One witness region of an accepted generation: how Alex treated it and how
/// the back end treated its object. Witness is "changed", "retained" or
/// "retired" (or "" for an object with no witness row); Object is "compiled",
/// "reused", "retired" or "" when the region produced no object of its own.
type RegionRow = { Identity: string; Witness: string; Visits: int32; Object: string }

/// The accepted artifact a session currently holds: its identity and one row
/// per region, so a build shows what a change recompiled and what it reused.
type ArtifactSummary = {
  Generation: int64
  SourceVersion: string
  ArtifactPath: string
  ArtifactSha256: string
  Regions: RegionRow array
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
  /// Stamped when the daemon read it, one per read, increasing: a page keeps
  /// the newest and drops an older read that arrives later (a connect or
  /// snapshot answer racing a pushed change).
  Revision: int64
  Version: string
  ProcessId: int32
  McpPort: int32
  /// "Healthy", "Degraded", ... as the daemon labels it.
  Overall: string
  /// "Normal", "Tight" or "Critical" today; rendered as given.
  MemoryPressure: string
  /// Why the level is what it is: the fraction of RAM available and the
  /// thresholds in force, as the daemon words it.
  MemoryPressureNote: string
  ResidentBytes: int64
  MachineAvailableBytes: int64
  MachineTotalBytes: int64
  CpuPercent: float
  Processes: ProcessSample array
  Alarms: HealthAlarm array
  /// When the daemon last read the machine (Unix epoch ms): the age of the
  /// figures and of the level.
  SampledAtMs: int64
  /// When the daemon last took its own record (Unix epoch ms); 0 before the
  /// first, so a page can say how old what it shows is.
  RecordAtMs: int64
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

type LeaseBoard = {
  /// Stamped by the daemon per publication, increasing: a page keeps the newest.
  Revision: int64
  Active: LeaseGrant array
  Queued: LeaseWait array
}

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

/// A kernel reading. Missing/unsupported readings are null, never zero.
/// Units are percent (host capacity), bytes, count, or load (runnable tasks).
type ResourceMetric = { Name: string; Value: float option; Unit: string }

/// PID plus start ticks identifies a process incarnation. CPU is percent of
/// one logical CPU (100% = one busy CPU); RSS includes shared pages.
type ResourceProcess = {
  ProcessId: int32
  ParentId: int32
  StartTicks: string
  Name: string
  Context: string
  CpuPercent: float option
  ResidentBytes: float
  Threads: int32
}

/// One host acquisition in an explicitly requested bounded window. Metrics do not
/// affect lease policy. Processes are the union of top CPU and top RSS rows.
type HostResources = {
  Sequence: int64
  Status: string
  SampledAtMs: int64
  IntervalMs: float
  SampleCostMs: float
  Metrics: ResourceMetric array
  Processes: ResourceProcess array
  ProcessCount: int32
  UnreadableCount: int32
  OmittedCount: int32
  Note: string
}

/// Client-reported agent accounting, separate from leases/compiler evidence.
type AgentUsage = { Input: int64; Output: int64; CacheRead: int64; CacheWrite: int64; Total: int64; EstimateUsd: float option }
type AgentRun = {
  Id: string; Member: string; Role: string; Name: string; Session: string
  ReportSequence: int64; Model: string; Endpoint: string; Project: string
  Focus: string; Operation: string; Status: string; UpdatedAtMs: int64
  Activity: string array; OmittedActivity: int64; Usage: AgentUsage option
}
/// An agent the daemon knows to be here from a fact it owns: a lease it holds
/// or waits for (`Source = "lease"`), or a cohort membership (`Source =
/// "member"`). Derived, not reported, so an agent that never reports is still
/// seen; `Detail` says what the fact is and `SinceMs` when it began.
type AgentPresence = { Name: string; Source: string; Member: string; Project: string; Detail: string; SinceMs: int64 }
type AgentWorkBoard = {
  Incarnation: string; Sequence: int64; ExecutionHost: string
  Runs: AgentRun array
  Presence: AgentPresence array
}

/// A run's output as the daemon journaled it; `Dropped` marks a transcript
/// given up for space, its entry kept.
type OperationCompiler = { Generation: int64; SourceVersion: string }
type LeasedCommandInfo = {
  Kind: string
  /// Non-secret display identity, matching the grant board; never a release token.
  LeaseId: string; RunId: string; Executable: string
  Arguments: string array; ProcessId: int32; StartedAtMs: int64
}
type OperationRun = {
  ExitCode: int32; Compiler: OperationCompiler option
  StandardOutput: string; StandardError: string
  Dropped: bool
}

/// Private loopback reporting, separate from public Work snapshots and compiler commands.
[<Literal>]
let CommandObservationMediaType = "application/vnd.bozzetto.command-observation"
[<Literal>]
let MaxCommandObservationBytes = 256 * 1024
[<Literal>]
let MaxCommandObservationReplyBytes = 16 * 1024

type CommandObservationResult = {
  FinishedAtMs: int64; ExitCode: int32
  StandardOutput: string; StandardError: string
  OutputTruncated: bool; ErrorTruncated: bool
}
type CommandObservation =
  | CommandStarted
  | CommandFinished of CommandObservationResult

type CommandObservationRequest = {
  Holder: string; Project: string; Kind: string
  /// Admission capability, private to the caller and its local daemon.
  LeaseId: string
  RunId: string; Executable: string; Arguments: string array
  ProcessId: int32; StartedAtMs: int64; Observation: CommandObservation
}
type CommandObservationReply =
  | CommandObservationAccepted of sequence: int64
  | CommandObservationRefused of reason: string

/// One compiler operation or leased process observation, as the daemon journaled it.
/// `Caller` is the adapter's report of who asked ("mcp:<transport>", "http",
/// "browser"): a label, never an authenticated identity.
type OperationEntry = {
  Sequence: int64; AtMs: int64
  CallerLabel: string; Operation: string; Session: string; Project: string
  Ok: bool; Code: string; Message: string
  Run: OperationRun option
  Lease: LeasedCommandInfo option
}

/// The daemon's journal of running and settled operations, newest first, bounded by the
/// daemon; stamped per publication, a page keeps the newest.
type OperationsBoard = { Revision: int64; Incarnation: string; Entries: OperationEntry array; StorageError: string option }

/// The peer's own complete work observation. It carries no admission capability.
type WorkSnapshot = {
  Sequence: int64
  SampledAtMs: int64
  Snapshot: ComposerSnapshot
  Leases: LeaseBoard
  Operations: OperationsBoard
  TranscriptsOmitted: bool
}
/// Local is assigned by the controller, never accepted from a peer.
type NodeWork = { Source: NodeIdentity; Local: bool; Status: string; Note: string; Value: WorkSnapshot option }
type WorkBoard = { Revision: int64; Nodes: NodeWork array }
type NodeResources = { Source: NodeIdentity; Correlation: int32; Value: HostResources }
type PeerObserveRequest = { ProtocolVersion: int32; Node: string; Topic: string; Seconds: int32 }
type PeerFrame =
  | PeerHello of NodeIdentity
  | PeerWork of WorkSnapshot
  | PeerResources of HostResources
  | PeerUnavailable of string
  | PeerHeartbeat of UptimeTick

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
  | Resources of HostResources
  | AgentWork of AgentWorkBoard
  | Operations of OperationsBoard
  | WorkNodes of WorkBoard
  | NodeResources of NodeResources
  | NodeTranscript of NodeTranscript
