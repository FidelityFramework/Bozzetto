// The model, MVU as a metaphor without the Elmish machinery: Ranvier signals
// are the model, the backend is the update function, `dispatch` sends a
// Command over the bridge, and every Event that comes back folds into the
// signals inside one batch.
//
// The model is fine-grained: one signal per slice the backend reports (health,
// uptime, leases, work nodes, sessions, ...). An Event writes only the slices
// it carries, so only the bindings that read those slices recompute, and a
// binding rewrites its own DOM node only when the value it shows changed.
module Bozzetto.Web.Frontend.Model

open System.Collections.Generic
open global.Ranvier
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Web.Frontend

// ── View records (signal contents) ─────────────────────────────────────────

type ArtifactView =
  {|
    generation: string
    sourceVersion: string
    path: string
    sha: string
    compiled: int
    reused: int
    retired: int
    changed: int
    regions: RegionRow array
  |}

type NoticeView = {| id: string; label: string; text: string |}

type SessionView =
  {|
    id: string
    host: string
    epoch: string
    session: string
    generation: string
    project: string
    closed: bool
    busy: bool
    fresh: bool
    revocationPending: bool
    cleanupPending: bool
    formatterCleanupPending: bool
    hasCurrent: bool
    // Meaningful only when hasCurrent (view records hold no options).
    current: ArtifactView
    notices: NoticeView array
  |}

type WorkerView = {| state: string; host: string; epoch: string; compilerVersion: string; pid: string |}

type ProcessView = {| id: string; pid: int; role: string; rss: float; cpu: float |}

type ResourceMetricView = {| id: string; known: bool; value: float; unit: string |}
type ResourceProcessView = {| id: string; pid: int; parent: int; name: string; context: string; cpuKnown: bool; cpu: float; rss: float; threads: int |}
type ResourcesView = {| sequence: string; status: string; sampledAt: float; interval: float; cost: float; count: int; unreadable: int; omitted: int; note: string |}

let private noResources () : ResourcesView =
  {| sequence = ""; status = "stopped"; sampledAt = 0.; interval = 0.; cost = 0.; count = 0; unreadable = 0; omitted = 0
     note = "No measurements yet. Start an explicit two-minute observation window." |}

type AgentView = {| id: string; memberId: string; name: string; role: string; model: string; endpoint: string; session: string
                    project: string; focus: string; operation: string; status: string; aged: bool; updatedAt: float; reportSequence: string
                    usage: string; cost: string; activity: string; omitted: string |}

// An agent the daemon derives from a fact it owns (a lease, a membership),
// shown whether or not the agent ever reports.
type PresenceView = {| id: string; name: string; source: string; project: string; detail: string; since: float |}

type AlarmView = {| id: string; signal: string; state: string; message: string |}

type HealthView =
  {|
    revision: float
    version: string
    pid: int
    port: int
    overall: string
    pressure: string
    note: string
    rss: float
    available: float
    total: float
    cpu: float
    processes: ProcessView array
    alarms: AlarmView array
    // When the daemon last read the machine and last took its record (epoch ms; 0 = never).
    sampledAt: float
    recordAt: float
  |}

type RetrievalView =
  { revision: float
    state: string
    branchStatus: string
    branch: string
    generation: int64
    snapshotId: string
    publishedAt: float
    queuedRetrievals: int
    expectedSources: string array
    pendingSources: string array
    staleSources: string array
    reasons: string array
    note: string }

type FleetBalanceCandidateView = {|
  nodeId: string
  freeSlots: int
  activeLeases: int
  leaseCap: int
  queuedLeases: int
  availableRamBytes: float
  totalRamBytes: float
  pressure: string
|}

type RebalanceAdvisoryView = {|
  recommendedNode: string
  reason: string
  policy: string
  throttled: bool
  candidates: FleetBalanceCandidateView array
|}

type SilentAgentAlertView = {|
  memberKey: string
  displayName: string
  agentFamily: string
  role: string
  kind: string
  idleSeconds: float
  lastActivity: float
  reason: string
|}

type FleetBalanceView = {|
  revision: float
  policy: string
  silentThresholdSeconds: int
  localNodeId: string
  localBusy: bool
  hasAdvisory: bool
  advisory: RebalanceAdvisoryView
  silentAlerts: SilentAgentAlertView array
  activeLeaseCount: int
  openClaimCount: int
  unauditedRunCount: int
  dirtyTrancheCount: int
|}

let private noAdvisory : RebalanceAdvisoryView =
  {| recommendedNode = ""; reason = ""; policy = ""; throttled = false; candidates = [||] |}

let private noFleetBalance : FleetBalanceView =
  {| revision = 0.0; policy = "advisory_only"; silentThresholdSeconds = 300; localNodeId = ""
     localBusy = false; hasAdvisory = false; advisory = noAdvisory; silentAlerts = [||]
     activeLeaseCount = 0; openClaimCount = 0; unauditedRunCount = 0; dirtyTrancheCount = 0 |}

type LeaseView = {| id: string; kind: string; holder: string; grantedAt: float; expiresAt: float |}

type WaitView = {| id: string; kind: string; holder: string; requestedAt: float |}

type NodeOperationView = {|
  id: string; sequence: string; label: string; caller: string; code: string; ok: bool; project: string; operation: string; outcome: string; message: string; at: float
  hasRun: bool; exitCode: int; dropped: bool; hasLease: bool; leaseId: string; runId: string
  executable: string; arguments: string array; pid: int; startedAt: float
|}
type TranscriptView = {|
  node: string; incarnation: string; journal: string; sequence: string; stream: TranscriptStream
  correlation: int; bytes: byte array; text: string; next: string; total: string; truncated: bool; status: string
|}

let private noTranscript () : TranscriptView =
  {| node = ""; incarnation = ""; journal = ""; sequence = ""; stream = Stdout; correlation = 0
     bytes = [||]; text = ""; next = ""; total = ""; truncated = false; status = "not loaded" |}

type NodeCapacityView = {|
  hasLeaseCap: bool
  leaseCap: int
  pidVerifiedLeases: int
  unverifiedLeases: int
  stalePidLeases: int
  memoryPressure: string
  machineAvailable: float
  machineTotal: float
  resident: float
  peakResident: float
|}

let private noCapacity () : NodeCapacityView =
  {| hasLeaseCap = false; leaseCap = 0; pidVerifiedLeases = 0; unverifiedLeases = 0; stalePidLeases = 0; memoryPressure = ""
     machineAvailable = 0.; machineTotal = 0.; resident = 0.; peakResident = 0. |}

// Controller-admitted node facts. Availability is separate from a retained sample.
type NodeWorkView = {|
  id: string; incarnation: string; local: bool; status: string; note: string
  hasValue: bool; sequence: string; sampledAt: float
  hasCapacity: bool; capacity: NodeCapacityView
  worker: WorkerView; sessions: SessionView array; leases: LeaseView array; queue: WaitView array
  journal: string; storageError: string; transcriptsOmitted: bool; operations: NodeOperationView array
|}

// One command outcome, as the activity log and a session card show it.
type OutcomeView = {| id: int; at: float; ok: bool; scope: string; text: string |}

// One settled Composer operation from the daemon's journal: every caller's,
// with the adapter's label for who asked (a label, not an identity).
type OperationView =
  {|
    id: string; sequence: float; at: float; caller: string; operation: string; session: string; project: string
    ok: bool; code: string; message: string
    hasRun: bool; exitCode: int; generation: string; sourceVersion: string; stdout: string; stderr: string; dropped: bool
  |}

type RunView =
  {|
    id: int
    at: float
    project: string
    exitCode: int
    generation: string
    sourceVersion: string
    stdout: string
    stderr: string
  |}

// What the backend has told us, plus the UI's own logs, one signal per slice.
// UI-local facts keyed by a session id, "open" or "worker" live beside them:
// reservation tokens are here because the backend does not report them back
// (a gap: a reload forgets them, and another tab never sees them).
type Stores =
  { Welcomed: Signal<bool>
    DaemonVersion: Signal<string>
    // When the daemon started (epoch ms), from Welcome: shown as a fixed
    // time. The running uptime is the daemon's own push (Uptime).
    StartedAt: Signal<float>
    ProtocolMismatch: Signal<bool>
    Revision: Signal<string>
    Worker: Signal<WorkerView>
    Sessions: Signal<SessionView array>
    Agents: Signal<AgentView array>
    Presence: Signal<PresenceView array>
    AgentIncarnation: Signal<string>
    AgentSequence: Signal<string>
    ExecutionHost: Signal<string>
    AgentOmittedRuns: Signal<string>
    AgentRefusal: Signal<string>
    Nodes: Signal<NodeWorkView array>
    NodesRevision: Signal<string>
    NodeFocus: Signal<string>
    NodeIncarnation: Signal<string>
    NodeCorrelation: Signal<int>
    Transcript: Signal<TranscriptView>
    Resources: Signal<ResourcesView>
    ResourceMetrics: Signal<ResourceMetricView array>
    ResourceProcesses: Signal<ResourceProcessView array>
    HealthSeen: Signal<bool>
    // Meaningful only when HealthSeen.
    Health: Signal<HealthView>
    RetrievalSeen: Signal<bool>
    Retrieval: Signal<RetrievalView>
    FleetBalanceSeen: Signal<bool>
    FleetBalance: Signal<FleetBalanceView>
    Leases: Signal<LeaseView array>
    Queue: Signal<WaitView array>
    // The lease board's stamp as last applied: a page keeps the newest.
    LeaseRevision: Signal<float>
    // The daemon's operations journal, newest first, and its stamp as last applied.
    Operations: Signal<OperationView array>
    OperationsRevision: Signal<float>
    Activity: Signal<OutcomeView array>
    Runs: Signal<RunView array>
    Reservations: Signal<Map<string, string>>
    Outcomes: Signal<Map<string, OutcomeView>>
    Pending: Signal<Map<string, string>>
    // The daemon's uptime in ms as its clock last pushed it; negative until
    // the first Uptime of a connection. It changes every second and only the
    // text that shows it reads it. The page runs no timer for it.
    Uptime: Signal<float> }

[<Literal>]
let OpenKey = "open"

[<Literal>]
let WorkerKey = "worker"

let private noWorker () : WorkerView = {| state = "idle"; host = ""; epoch = ""; compilerVersion = ""; pid = "" |}

let private noArtifact () : ArtifactView =
  {| generation = ""; sourceVersion = ""; path = ""; sha = ""; compiled = 0; reused = 0; retired = 0; changed = 0; regions = [||] |}

let private noHealth () : HealthView =
  {|
    revision = 0.0
    version = ""
    pid = 0
    port = 0
    overall = ""
    pressure = ""
    note = ""
    rss = 0.0
    available = 0.0
    total = 0.0
    cpu = 0.0
    processes = Array.empty
    alarms = Array.empty
    sampledAt = 0.0
    recordAt = 0.0
  |}

let private noRetrieval : RetrievalView =
  { revision = 0.0
    state = ""
    branchStatus = ""
    branch = ""
    generation = 0L
    snapshotId = ""
    publishedAt = 0.0
    queuedRetrievals = 0
    expectedSources = [||]
    pendingSources = [||]
    staleSources = [||]
    reasons = [||]
    note = "" }

// Signals belong to the ambient graph: call inside the page graph's Run.
let create () : Stores =
  { Welcomed = Api.createSignal false
    DaemonVersion = Api.createSignal ""
    StartedAt = Api.createSignal 0.0
    ProtocolMismatch = Api.createSignal false
    Revision = Api.createSignal ""
    Worker = Api.createSignal (noWorker ())
    Sessions = Api.createSignal Array.empty<SessionView>
    Agents = Api.createSignal Array.empty<AgentView>
    Presence = Api.createSignal Array.empty<PresenceView>
    AgentIncarnation = Api.createSignal ""
    AgentSequence = Api.createSignal ""
    ExecutionHost = Api.createSignal ""
    AgentOmittedRuns = Api.createSignal "0"
    AgentRefusal = Api.createSignal ""
    Nodes = Api.createSignal Array.empty<NodeWorkView>
    NodesRevision = Api.createSignal ""
    NodeFocus = Api.createSignal ""
    NodeIncarnation = Api.createSignal ""
    NodeCorrelation = Api.createSignal 0
    Transcript = Api.createSignal (noTranscript ())
    Resources = Api.createSignal (noResources ())
    ResourceMetrics = Api.createSignal Array.empty<ResourceMetricView>
    ResourceProcesses = Api.createSignal Array.empty<ResourceProcessView>
    HealthSeen = Api.createSignal false
    Health = Api.createSignal (noHealth ())
    RetrievalSeen = Api.createSignal false
    Retrieval = Api.createSignal noRetrieval
    FleetBalanceSeen = Api.createSignal false
    FleetBalance = Api.createSignal noFleetBalance
    Leases = Api.createSignal Array.empty<LeaseView>
    Queue = Api.createSignal Array.empty<WaitView>
    LeaseRevision = Api.createSignal 0.0
    Operations = Api.createSignal Array.empty<OperationView>
    OperationsRevision = Api.createSignal 0.0
    Activity = Api.createSignal Array.empty<OutcomeView>
    Runs = Api.createSignal Array.empty<RunView>
    Reservations = Api.createSignal Map.empty
    Outcomes = Api.createSignal Map.empty
    Pending = Api.createSignal Map.empty
    Uptime = Api.createSignal (-1.0) }

// ── Identities ─────────────────────────────────────────────────────────────

let sessionKey (target: SessionTarget) =
  target.Worker.Host + "|" + target.Worker.Epoch + "|" + target.Session

let targetOf (session: SessionView) : SessionTarget =
  { SessionTarget.Worker = { WorkerTarget.Host = session.host; Epoch = session.epoch }; Session = session.session }

// Which UI element a command's pending state and outcome belong to.
let commandKey (command: Command) =
  match command with
  | ObserveResources _ | ObserveNodeResources _ -> "resources"
  | ReadNodeTranscript _ -> "transcript"
  | RequestSnapshot -> "snapshot"
  | OpenProject _ -> OpenKey
  | Reserve edit -> sessionKey edit.Target
  | Build build -> sessionKey build.Target
  | Run run -> sessionKey run.Target
  | Cancel target
  | CloseSession target -> sessionKey target
  | RetireWorker _ -> WorkerKey

let commandLabel (command: Command) =
  match command with
  | ObserveResources _ | ObserveNodeResources _ -> "observe resources"
  | ReadNodeTranscript _ -> "read transcript"
  | RequestSnapshot -> "refresh"
  | OpenProject _ -> "open"
  | Reserve _ -> "reserve"
  | Build _ -> "build"
  | Run _ -> "run"
  | Cancel _ -> "cancel"
  | CloseSession _ -> "close"
  | RetireWorker _ -> "retire worker"

// ── Formatting (pure) ──────────────────────────────────────────────────────
// Times are shown as the instants they are (LocalTime.fs). The one duration
// shown, the uptime, is the value the daemon's clock pushed.

let localTimestampAt = LocalTime.localTimestampAt

let clockTime = LocalTime.clockTime

let dateTime = LocalTime.dateTime

[<Fable.Core.Emit("Date.parse($0)")>]
let parseIsoMs (iso: string) : float = Fable.Core.Util.jsNative

[<Fable.Core.Emit("$0.toFixed($1)")>]
let fixedPoint (value: float) (digits: int) : string = Fable.Core.Util.jsNative

let fileName (path: string) =
  let cut = path.LastIndexOf '/'
  if cut >= 0 && cut < path.Length - 1 then path.Substring(cut + 1) else path

let shortHash (sha: string) = if sha.Length > 12 then sha.Substring(0, 12) else sha

// A pushed uptime as the page shows it: "42s", "7m 05s", "3h 04m 05s",
// "2d 03h 04m 05s".
let duration (milliseconds: float) =
  let total = int (floor (max 0.0 milliseconds / 1000.0))
  let two (n: int) = (if n < 10 then "0" else "") + string n
  let days, hours, minutes, seconds = total / 86400, total % 86400 / 3600, total % 3600 / 60, total % 60
  if days > 0 then string days + "d " + two hours + "h " + two minutes + "m " + two seconds + "s"
  elif hours > 0 then string hours + "h " + two minutes + "m " + two seconds + "s"
  elif minutes > 0 then string minutes + "m " + two seconds + "s"
  else string seconds + "s"

let bytes (value: float) =
  let units = [| "B"; "KiB"; "MiB"; "GiB"; "TiB" |]
  let mutable scaled = value
  let mutable unit = 0
  while scaled >= 1024.0 && unit < units.Length - 1 do
    scaled <- scaled / 1024.0
    unit <- unit + 1
  (if unit = 0 then fixedPoint scaled 0 else fixedPoint scaled 1) + " " + units[unit]

// ── Projections: protocol values -> view records ──────────────────────────

let private artifactView (a: ArtifactSummary) : ArtifactView =
  {|
    generation = string a.Generation
    sourceVersion = a.SourceVersion
    path = a.ArtifactPath
    sha = a.ArtifactSha256
    compiled = a.Regions |> Array.filter (fun r -> r.Object = "compiled") |> Array.length
    reused = a.Regions |> Array.filter (fun r -> r.Object = "reused") |> Array.length
    retired = a.Regions |> Array.filter (fun r -> r.Object = "retired") |> Array.length
    changed = a.Regions |> Array.filter (fun r -> r.Witness = "changed") |> Array.length
    regions = a.Regions
  |}

let private sessionView (s: SessionStatus) : SessionView =
  let notice id label (value: string option) =
    value |> Option.map (fun text -> {| id = id; label = label; text = text |})
  {|
    id = sessionKey s.Target
    host = s.Target.Worker.Host
    epoch = s.Target.Worker.Epoch
    session = s.Target.Session
    generation = string s.Generation
    project = s.Project
    closed = s.Closed
    busy = s.Busy
    fresh = s.StatusFresh
    revocationPending = s.RevocationPending
    cleanupPending = s.CleanupPending
    formatterCleanupPending = s.FormatterCleanupPending
    hasCurrent = s.Current.IsSome
    current = s.Current |> Option.map artifactView |> Option.defaultWith noArtifact
    notices =
      [|
        notice "backend" "Backend diagnostic" s.BackendError
        notice "worker retirement required" "Worker retirement required" s.WorkerRetirementRequired
        notice "cleanup" "Cleanup diagnostic" s.CleanupError
        notice "formatter" "Formatter diagnostic" s.FormatterError
        notice "worker" "Worker diagnostic" s.WorkerError
        notice "status" "Status diagnostic" s.StatusError
      |]
      |> Array.choose id
  |}

let private workerView (w: WorkerState) : WorkerView =
  match w with
  | Unconfigured -> {| noWorker () with state = "unconfigured" |}
  | Idle -> noWorker ()
  | Running info ->
    {|
      state = "running"
      host = info.Target.Host
      epoch = info.Target.Epoch
      compilerVersion = info.CompilerVersion
      pid = info.ProcessId |> Option.map string |> Option.defaultValue ""
    |}

let private healthView (h: DaemonHealth) : HealthView =
  {|
    revision = float h.Revision
    version = h.Version
    pid = h.ProcessId
    port = h.McpPort
    overall = h.Overall
    pressure = h.MemoryPressure
    note = h.MemoryPressureNote
    rss = float h.ResidentBytes
    available = float h.MachineAvailableBytes
    total = float h.MachineTotalBytes
    cpu = h.CpuPercent
    processes =
      h.Processes
      |> Array.map (fun p -> {| id = string p.ProcessId; pid = p.ProcessId; role = p.Role; rss = float p.ResidentBytes; cpu = p.CpuPercent |})
    alarms =
      h.Alarms
      |> Array.map (fun a -> {| id = a.Signal + "|" + a.State; signal = a.Signal; state = a.State; message = a.Message |})
    sampledAt = float h.SampledAtMs
    recordAt = float h.RecordAtMs
  |}

let private retrievalView (status: RetrievalStatus) : RetrievalView =
  { revision = float status.Revision
    state = status.State
    branchStatus = status.BranchStatus
    branch = status.BranchName
    generation = status.Generation
    snapshotId = status.SnapshotId
    publishedAt = float status.PublishedAtMs
    queuedRetrievals = status.QueuedRetrievals
    expectedSources = status.ExpectedSources
    pendingSources = status.PendingSources
    staleSources = status.StaleSources
    reasons = status.Reasons
    note = status.Note }

let private fleetBalanceView (status: FleetBalanceStatus) : FleetBalanceView =
  let candidateView (c: RebalanceCandidate) : FleetBalanceCandidateView =
    {| nodeId = c.NodeId; freeSlots = c.FreeSlots; activeLeases = c.ActiveLeases; leaseCap = c.LeaseCap
       queuedLeases = c.QueuedLeases; availableRamBytes = float c.AvailableRamBytes; totalRamBytes = float c.TotalRamBytes
       pressure = c.Pressure |}
  let adv =
    match status.Advisory with
    | Some a ->
      {| recommendedNode = a.RecommendedNode; reason = a.Reason; policy = a.Policy; throttled = a.Throttled
         candidates = a.Candidates |> Array.map candidateView |}
    | None -> noAdvisory
  {| revision = float status.Revision
     policy = status.Policy
     silentThresholdSeconds = status.SilentThresholdSeconds
     localNodeId = status.LocalNodeId
     localBusy = status.LocalBusy
     hasAdvisory = status.Advisory.IsSome
     advisory = adv
     silentAlerts =
       status.SilentAlerts
       |> Array.map (fun s ->
         {| memberKey = s.MemberKey; displayName = s.DisplayName; agentFamily = s.AgentFamily; role = s.Role
            kind = s.Kind; idleSeconds = float s.IdleSeconds; lastActivity = float s.LastActivityMs; reason = s.Reason |})
     activeLeaseCount = status.ActiveLeaseCount
     openClaimCount = status.OpenClaimCount
     unauditedRunCount = status.UnauditedRunCount
     dirtyTrancheCount = status.DirtyTrancheCount |}

let private leaseView (l: LeaseGrant) : LeaseView =
  {| id = l.Id; kind = l.Kind; holder = l.Holder; grantedAt = float l.GrantedAtMs; expiresAt = float l.ExpiresAtMs |}

let private waitView (w: LeaseWait) : WaitView =
  {| id = w.Holder + "|" + w.Kind; kind = w.Kind; holder = w.Holder; requestedAt = float w.RequestedAtMs |}

let private nodeView (node: NodeWork) : NodeWorkView =
  let capacity = node.Value |> Option.bind _.Capacity
  {| id = node.Source.Node; incarnation = node.Source.Incarnation; local = node.Local
     status = node.Status; note = node.Note; hasValue = node.Value.IsSome
     sequence = node.Value |> Option.map (fun v -> string v.Sequence) |> Option.defaultValue ""
     sampledAt = node.Value |> Option.map (fun v -> float v.SampledAtMs) |> Option.defaultValue 0.
     hasCapacity = capacity.IsSome
     capacity = capacity |> Option.map (fun c ->
       {| hasLeaseCap = c.LeaseCap.IsSome; leaseCap = Option.defaultValue 0 c.LeaseCap
          pidVerifiedLeases = c.PidVerifiedLeases; unverifiedLeases = c.UnverifiedLeases
          stalePidLeases = c.StalePidLeases; memoryPressure = c.MemoryPressure
          machineAvailable = float c.MachineAvailableBytes; machineTotal = float c.MachineTotalBytes
          resident = float c.ResidentBytes; peakResident = float c.PeakResidentBytes |}) |> Option.defaultWith noCapacity
     worker = node.Value |> Option.map (fun v -> workerView v.Snapshot.Worker) |> Option.defaultWith noWorker
     sessions = node.Value |> Option.map (fun v -> Array.map sessionView v.Snapshot.Sessions) |> Option.defaultValue [||]
     leases = node.Value |> Option.map (fun v -> Array.map leaseView v.Leases.Active) |> Option.defaultValue [||]
     queue = node.Value |> Option.map (fun v -> Array.map waitView v.Leases.Queued) |> Option.defaultValue [||]
     journal = node.Value |> Option.map (fun v -> v.Operations.Incarnation) |> Option.defaultValue ""
     storageError = node.Value |> Option.bind (fun v -> v.Operations.StorageError) |> Option.defaultValue ""
     transcriptsOmitted = node.Value |> Option.exists _.TranscriptsOmitted
     operations = node.Value |> Option.map (fun v -> v.Operations.Entries |> Array.map (fun e ->
       let outcome = if e.Code = "running" then "running" elif e.Ok then "completed" else e.Code + ": " + e.Message
       {| id = string e.Sequence; sequence = string e.Sequence; caller = e.CallerLabel; code = e.Code; ok = e.Ok; hasRun = e.Run.IsSome; dropped = e.Run |> Option.exists _.Dropped
          project = e.Project; operation = e.Operation; outcome = outcome; message = e.Message; at = float e.AtMs
          exitCode = e.Run |> Option.map _.ExitCode |> Option.defaultValue 0
          hasLease = e.Lease.IsSome
          leaseId = e.Lease |> Option.map _.LeaseId |> Option.defaultValue ""
          runId = e.Lease |> Option.map _.RunId |> Option.defaultValue ""
          executable = e.Lease |> Option.map _.Executable |> Option.defaultValue ""
          arguments = e.Lease |> Option.map _.Arguments |> Option.defaultValue [||]
          pid = e.Lease |> Option.map _.ProcessId |> Option.defaultValue 0
          startedAt = e.Lease |> Option.map (fun l -> float l.StartedAtMs) |> Option.defaultValue 0.
          label = clockTime (float e.AtMs) + " · " + e.Operation + " · " + e.Project + " · " + outcome |})) |> Option.defaultValue [||] |}

// ── Signal writes ──────────────────────────────────────────────────────────

let private set (signal: Signal<'T>) (value: 'T) = signal.Value <- value

// A focus change withdraws only the view; the caller sends the matching stop/start.
let focusNode (stores: Stores) node incarnation =
  set stores.NodeFocus node
  set stores.NodeIncarnation incarnation
  set stores.NodeCorrelation 0
  set stores.Resources (noResources ())
  set stores.ResourceMetrics Array.empty
  set stores.ResourceProcesses Array.empty

let private applyResources (stores: Stores) (r: HostResources) =
  let current = stores.Resources.Value
  if current.sequence = "" || r.Sequence >= int64 current.sequence then
    set stores.Resources
      {| sequence = string r.Sequence; status = r.Status; sampledAt = float r.SampledAtMs; interval = r.IntervalMs; cost = r.SampleCostMs
         count = r.ProcessCount; unreadable = r.UnreadableCount; omitted = r.OmittedCount; note = r.Note |}
    set stores.ResourceMetrics (r.Metrics |> Array.map (fun m ->
      {| id = m.Name; known = m.Value.IsSome; value = Option.defaultValue 0. m.Value; unit = m.Unit |}))
    set stores.ResourceProcesses (r.Processes |> Array.map (fun p ->
      {| id = string p.ProcessId + ":" + p.StartTicks; pid = p.ProcessId; parent = p.ParentId; name = p.Name; context = p.Context
         cpuKnown = p.CpuPercent.IsSome; cpu = Option.defaultValue 0. p.CpuPercent; rss = p.ResidentBytes; threads = p.Threads |}))

// Set (Some) or delete (None) one key of a UI-local map.
let private setLocal (signal: Signal<Map<string, 'T>>) (key: string) (value: 'T option) =
  match value with
  | Some v -> signal.Value <- Map.add key v signal.Value
  | None -> if signal.Value.ContainsKey key then signal.Value <- Map.remove key signal.Value

let mutable private nextEntry = 0

let private projectOf (stores: Stores) (key: string) =
  stores.Sessions.Value
  |> Array.tryFind (fun s -> s.id = key)
  |> Option.map (fun s -> fileName s.project)
  |> Option.defaultValue ""

// Record an outcome once in the activity log and as the last outcome of
// every given key ("" = no card, e.g. a frame-level refusal).
let private log (stores: Stores) (keys: string list) (ok: bool) (text: string) =
  nextEntry <- nextEntry + 1
  let scope =
    match keys |> List.tryFind (fun k -> k <> OpenKey && k <> WorkerKey && k <> "") with
    | Some session -> projectOf stores session
    | None -> keys |> List.tryHead |> Option.defaultValue ""
  let entry: OutcomeView = {| id = nextEntry; at = Interop.nowMs (); ok = ok; scope = scope; text = text |}
  set stores.Activity (Array.append [| entry |] (Array.truncate 199 stores.Activity.Value))
  for key in keys do
    if key <> "" then setLocal stores.Outcomes key (Some entry)

// Correlation -> (key, label, typed request) still awaiting its outcome.
type Dispatcher =
  { InFlight: Dictionary<int32, string * string * Command>
    mutable Next: int32 }

let dispatcher () = { InFlight = Dictionary(); Next = 0 }

// Allocate a correlation and mark the command pending. Returns the frame.
let begin' (stores: Stores) (d: Dispatcher) (command: Command) : CommandFrame =
  d.Next <- d.Next + 1
  let key, label = commandKey command, commandLabel command
  d.InFlight[d.Next] <- (key, label, command)
  setLocal stores.Pending key (Some label)
  match command with
  | Build build -> setLocal stores.Reservations (sessionKey build.Target) None // single use
  | ObserveNodeResources request when request.Node = stores.NodeFocus.Value ->
    set stores.NodeCorrelation d.Next
  | ReadNodeTranscript request ->
    let previous = if request.Offset = 0L then noTranscript () else stores.Transcript.Value
    set stores.Transcript
      {| previous with
          node = request.Source.Node; incarnation = request.Source.Incarnation; journal = request.Journal
          sequence = string request.Sequence; stream = request.Stream; correlation = d.Next; status = "reading transcript" |}
  | _ -> ()
  { CommandFrame.Correlation = d.Next; Command = command }

// The frame never left (link down): settle it locally as a refusal.
let abandon (stores: Stores) (d: Dispatcher) (frame: CommandFrame) =
  match d.InFlight.TryGetValue frame.Correlation with
  | true, (key, label, _) ->
    d.InFlight.Remove frame.Correlation |> ignore
    setLocal stores.Pending key None
    let reason =
      if stores.ProtocolMismatch.Value then label + " not sent: UI version changed — reload this page."
      elif not stores.Welcomed.Value then label + " not sent: awaiting daemon handshake."
      else label + " not sent: the bridge is disconnected."
    log stores [ key ] false reason
  | _ -> ()

// Every pending command is lost with the connection that carried it.
let abandonAll (stores: Stores) (d: Dispatcher) =
  set stores.Welcomed false
  for KeyValue(_, (key, label, _)) in d.InFlight |> Seq.toArray do
    setLocal stores.Pending key None
    log stores [ key ] false (label + ": connection lost before its outcome arrived; check the session status.")
  d.InFlight.Clear()

let private settle (stores: Stores) (d: Dispatcher) (correlation: int32) (fallbackKey: string) =
  match d.InFlight.TryGetValue correlation with
  | true, (key, label, _) ->
    d.InFlight.Remove correlation |> ignore
    setLocal stores.Pending key None
    key, label
  | _ -> fallbackKey, "command"

let private completionText (c: Completion) =
  match c with
  | SessionOpened -> "session opened"
  | EditReserved reservation -> "edit reserved (" + shortHash reservation + "…); edit sources, then build"
  | ReservationBuilt a ->
    "built generation " + string a.Generation + " · " + shortHash a.ArtifactSha256
    + " · " + string (a.Regions |> Array.filter (fun r -> r.Object = "compiled") |> Array.length) + " compiled, "
    + string (a.Regions |> Array.filter (fun r -> r.Object = "reused") |> Array.length) + " reused"
  | RunFinished exitCode -> "run finished, exit " + string exitCode
  | WorkCanceled -> "canceled; artifact authority withdrawn"
  | SessionClosed -> "closed; cleanup started"
  | WorkerRetired -> "worker retired; reopen projects on a fresh epoch"
  | SnapshotSent -> "refreshed"

// The fold: one backend Event into the signals, as one batch.
let apply (stores: Stores) (d: Dispatcher) (event: Event) =
  Api.batch (fun () ->
    match event with
    | Welcome w ->
      // A new connection, perhaps to a restarted daemon: until its clock
      // ticks, the page shows only the start time.
      set stores.Uptime (-1.0)
      set stores.Agents Array.empty
      set stores.Presence Array.empty
      set stores.AgentIncarnation ""
      set stores.AgentSequence ""
      set stores.ExecutionHost ""
      set stores.AgentOmittedRuns "0"
      set stores.AgentRefusal ""
      set stores.Nodes Array.empty
      set stores.NodesRevision ""
      focusNode stores "" ""
      set stores.Transcript (noTranscript ())
      // A restarted daemon stamps its facts from one again: forget the old stamps,
      // and wait for the new daemon's health and retrieval facts.
      set stores.HealthSeen false
      set stores.RetrievalSeen false
      set stores.FleetBalanceSeen false
      set stores.FleetBalance noFleetBalance
      set stores.LeaseRevision 0.0
      set stores.OperationsRevision 0.0
      set stores.Operations Array.empty
      set stores.Welcomed true
      set stores.DaemonVersion w.DaemonVersion
      set stores.StartedAt (float w.StartedAtMs)
      set stores.ProtocolMismatch (w.ProtocolVersion <> ProtocolVersion)
    | Snapshot snapshot ->
      set stores.Revision (string snapshot.Revision)
      set stores.Worker (workerView snapshot.Worker)
      let sessions = snapshot.Sessions |> Array.map sessionView
      set stores.Sessions sessions
      // A reservation cannot outlive its open session.
      for KeyValue(key, _) in stores.Reservations.Value do
        let live = sessions |> Array.exists (fun s -> s.id = key && not s.closed)
        if not live then setLocal stores.Reservations key None
    | Accepted accepted ->
      match d.InFlight.TryGetValue accepted.Correlation with
      | true, (_, _, ObserveNodeResources request)
          when request.Seconds = 0 && request.Node = stores.NodeFocus.Value
               && accepted.Correlation = stores.NodeCorrelation.Value
               && accepted.Completion = SnapshotSent ->
        // This exact stop acknowledgement follows the owner's disposal/join.
        // Retain the sampled values and timestamp; it is not a new host sample.
        set stores.Resources {| stores.Resources.Value with status = "stopped"; note = "Observation stopped; sampled values are retained." |}
      | _ -> ()
      let target = sessionKey accepted.Target
      let key, label = settle stores d accepted.Correlation target
      match accepted.Completion with
      | EditReserved reservation -> setLocal stores.Reservations target (Some reservation)
      | WorkCanceled | SessionClosed -> setLocal stores.Reservations target None
      | WorkerRetired -> set stores.Reservations Map.empty
      | SessionOpened | ReservationBuilt _ | RunFinished _ | SnapshotSent -> ()
      let text = label + ": " + completionText accepted.Completion
      if accepted.Completion = SnapshotSent then ()
      elif key = OpenKey then log stores [ target; OpenKey ] true text
      else log stores [ key ] true text
    | Refused refused ->
      if refused.Correlation = stores.Transcript.Value.correlation then
        set stores.Transcript {| stores.Transcript.Value with status = refused.Code + ": " + refused.Message |}
      let key, label = settle stores d refused.Correlation (sessionKey refused.Target)
      // An empty target ("||") is a frame-level refusal: no card owns it.
      let key = if key = "||" then "" else key
      if key <> "" && (refused.Code = "invalid_reservation" || refused.Code = "closed") then
        setLocal stores.Reservations key None
      log stores [ key ] false (label + " refused · " + refused.Code + ": " + refused.Message)
    | RunOutput run ->
      nextEntry <- nextEntry + 1
      let view: RunView =
        {|
          id = nextEntry
          at = Interop.nowMs ()
          project = projectOf stores (sessionKey run.Target)
          exitCode = run.ExitCode
          generation = string run.Generation
          sourceVersion = run.SourceVersion
          stdout = run.StandardOutput
          stderr = run.StandardError
        |}
      set stores.Runs (Array.append [| view |] (Array.truncate 19 stores.Runs.Value))
    | Operations board ->
      // Stamped per publication; an older journal arriving after a newer one is kept out.
      if float board.Revision >= stores.OperationsRevision.Value then
        set stores.OperationsRevision (float board.Revision)
        // This secondary pane is compiler-owned; all node operations remain in WorkNodes.
        set stores.Operations (board.Entries |> Array.filter (fun e -> e.Lease.IsNone) |> Array.map (fun e ->
          let run = e.Run
          {|
            id = string e.Sequence; sequence = float e.Sequence; at = float e.AtMs; caller = e.CallerLabel; operation = e.Operation
            session = e.Session; project = fileName e.Project; ok = e.Ok; code = e.Code; message = e.Message
            hasRun = run.IsSome
            exitCode = run |> Option.map (fun r -> r.ExitCode) |> Option.defaultValue 0
            generation = run |> Option.bind _.Compiler |> Option.map (fun c -> string c.Generation) |> Option.defaultValue ""
            sourceVersion = run |> Option.bind _.Compiler |> Option.map _.SourceVersion |> Option.defaultValue ""
            stdout = run |> Option.map (fun r -> r.StandardOutput) |> Option.defaultValue ""
            stderr = run |> Option.map (fun r -> r.StandardError) |> Option.defaultValue ""
            dropped = run |> Option.map (fun r -> r.Dropped) |> Option.defaultValue false
          |} : OperationView))
    | AgentWork board ->
      if stores.AgentIncarnation.Value <> board.Incarnation || stores.AgentSequence.Value = "" || board.Sequence >= int64 stores.AgentSequence.Value then
        set stores.AgentIncarnation board.Incarnation
        set stores.AgentSequence (string board.Sequence)
        set stores.ExecutionHost board.ExecutionHost
        set stores.AgentOmittedRuns (string board.OmittedRuns)
        set stores.AgentRefusal board.Refusal
        set stores.Agents (board.Runs |> Array.map (fun r ->
          {| id = r.Id; memberId = r.Member; name = r.Name; role = r.Role; model = r.Model; endpoint = r.Endpoint; session = r.Session
             project = r.Project; focus = r.Focus; operation = r.Operation; status = r.Status; aged = r.Aged
             updatedAt = float r.UpdatedAtMs; reportSequence = string r.ReportSequence
             usage = r.Usage |> Option.map (fun u -> string u.Total + " tokens (cumulative)") |> Option.defaultValue "usage unavailable"
             cost = r.Usage |> Option.bind _.EstimateUsd |> Option.map (fun c -> "$" + string c + " Pi estimate") |> Option.defaultValue "price unavailable"
             activity = String.concat " · " r.Activity; omitted = string r.OmittedActivity |}))
        set stores.Presence (board.Presence |> Array.map (fun p ->
          {| id = p.Source + "|" + p.Member + "|" + p.Detail; name = p.Name; source = p.Source
             project = p.Project; detail = p.Detail; since = float p.SinceMs |}))
    | WorkNodes board ->
      if stores.NodesRevision.Value = "" || board.Revision >= int64 stores.NodesRevision.Value then
        set stores.NodesRevision (string board.Revision)
        set stores.Nodes (board.Nodes |> Array.map nodeView)
        let focus = stores.NodeFocus.Value
        if focus <> "" then
          match board.Nodes |> Array.tryFind (fun n -> n.Source.Node = focus) with
          | Some node when node.Source.Incarnation = stores.NodeIncarnation.Value -> ()
          | Some node -> focusNode stores node.Source.Node node.Source.Incarnation
          | None -> focusNode stores "" ""
    | NodeResources resource ->
      if resource.Correlation = stores.NodeCorrelation.Value && resource.Correlation > 0
         && resource.Source.Node = stores.NodeFocus.Value && resource.Source.Incarnation = stores.NodeIncarnation.Value then
        applyResources stores resource.Value
    | NodeTranscript response ->
      let current = stores.Transcript.Value
      match d.InFlight.TryGetValue response.Correlation with
      | true, (_, _, ReadNodeTranscript request)
          when response.Correlation = current.correlation && response.Source = request.Source
               && response.Source.Node = current.node && response.Source.Incarnation = current.incarnation
               && request.Journal = current.journal && string request.Sequence = current.sequence && request.Stream = current.stream
               && (stores.Nodes.Value |> Array.exists (fun node -> node.id = current.node && node.incarnation = current.incarnation && node.journal = current.journal)) ->
        let refuse message = set stores.Transcript {| current with status = message; next = "" |}
        match response.Outcome with
        | TranscriptRefused(code, message) -> refuse (code + ": " + message)
        | TranscriptPage page ->
          let endOffset = page.Offset + int64 page.Bytes.Length
          let validNext =
            match page.NextOffset with
            | Some next -> next = endOffset && next < page.TotalBytes && page.Bytes.Length > 0
            | None -> endOffset = page.TotalBytes
          if page.Journal <> request.Journal || page.Sequence <> request.Sequence || page.Stream <> request.Stream
             || page.Offset <> request.Offset || page.Offset <> int64 current.bytes.Length
             || page.Bytes.Length > request.MaxBytes || page.TotalBytes < 0L || page.TotalBytes > int64 MaxTranscriptBytes
             || endOffset > page.TotalBytes || not validNext
             || (current.total <> "" && int64 current.total <> page.TotalBytes) then
            refuse "invalid_transcript_page: identity, byte cursor or size mismatch"
          else
            let bytes = Array.append current.bytes page.Bytes
            let decoded = if page.NextOffset.IsSome then Some "" else Interop.transcriptText bytes
            match decoded with
            | None -> refuse "invalid_transcript_utf8: stored bytes are not valid UTF-8"
            | Some text ->
              set stores.Transcript
                {| current with
                    bytes = bytes; text = text; next = page.NextOffset |> Option.map string |> Option.defaultValue ""
                    total = string page.TotalBytes; truncated = current.truncated || page.Truncated
                    status = if page.NextOffset.IsSome then "more available" else "loaded" |}
        settle stores d response.Correlation "transcript" |> ignore
      | _ -> ()
    | Resources r ->
      if stores.NodeFocus.Value = "" then applyResources stores r
    | Health health ->
      // A connect or snapshot answer can arrive after a pushed change; the
      // revision stamped at the daemon's read keeps the newest.
      if not stores.HealthSeen.Value || float health.Revision >= stores.Health.Value.revision then
        set stores.Health (healthView health)
        set stores.HealthSeen true
    | Retrieval status ->
      if not stores.RetrievalSeen.Value || float status.Revision >= stores.Retrieval.Value.revision then
        set stores.Retrieval (retrievalView status)
        set stores.RetrievalSeen true
    | FleetBalance status ->
      if not stores.FleetBalanceSeen.Value || float status.Revision >= stores.FleetBalance.Value.revision then
        set stores.FleetBalance (fleetBalanceView status)
        set stores.FleetBalanceSeen true
    | Leases board ->
      // Stamped per publication; an older board arriving after a newer one is kept out.
      if float board.Revision >= stores.LeaseRevision.Value then
        set stores.LeaseRevision (float board.Revision)
        set stores.Leases (board.Active |> Array.map leaseView)
        set stores.Queue (board.Queued |> Array.map waitView)
    | Uptime tick -> set stores.Uptime (float tick.UptimeMs))
