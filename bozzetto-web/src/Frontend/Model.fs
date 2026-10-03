/// The model, MVU as a metaphor without the Elmish machinery: Solid stores are
/// the model, the backend is the update function, `dispatch` sends a Command
/// over the bridge, and every Event that comes back folds into the stores.
///
/// Store contents are F# anonymous records, which Fable emits as plain
/// JavaScript objects. Solid stores proxy only plain objects and arrays (an
/// F# record or union is a class instance, an opaque leaf), so this is what
/// lets `reconcile` diff a whole snapshot field by field and touch only the
/// DOM that changed. Arrays are matched on each element's `id`.
module Bozzetto.Web.Frontend.Model

open System.Collections.Generic
open Fable.Core
open Partas.Solid
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Web.Frontend

// ── View records (store contents) ──────────────────────────────────────────

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
  |}

type NoticeView = {| id: string; text: string |}

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
    /// Meaningful only when hasCurrent (stores hold no options).
    current: ArtifactView
    notices: NoticeView array
  |}

type WorkerView = {| state: string; host: string; epoch: string; compilerVersion: string; pid: string |}

type ProcessView = {| id: string; pid: int; role: string; rss: float; cpu: float |}

type AlarmView = {| id: string; signal: string; state: string; message: string |}

type HealthView =
  {|
    version: string
    pid: int
    port: int
    overall: string
    pressure: string
    rss: float
    available: float
    total: float
    cpu: float
    processes: ProcessView array
    alarms: AlarmView array
  |}

type LeaseView = {| id: string; kind: string; holder: string; grantedAt: float; expiresAt: float |}

type WaitView = {| id: string; kind: string; holder: string; requestedAt: float |}

/// One command outcome, as the activity log and a session card show it.
type OutcomeView = {| id: int; at: float; ok: bool; scope: string; text: string |}

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

/// What the backend has told us, plus the UI's own logs.
type ModelView =
  {|
    welcomed: bool
    daemonVersion: string
    /// When the daemon started (epoch ms), from Welcome: shown as a fixed
    /// time. The running uptime is the daemon's own push (Stores.Uptime).
    startedAt: float
    protocolMismatch: bool
    revision: string
    worker: WorkerView
    sessions: SessionView array
    healthSeen: bool
    /// Meaningful only when healthSeen.
    health: HealthView
    leases: LeaseView array
    queue: WaitView array
    activity: OutcomeView array
    runs: RunView array
  |}

/// UI-local facts per key (a session id, "open" or "worker"). Reservation
/// tokens live here because the backend does not report them back (a gap:
/// a reload forgets them, and another tab never sees them).
type LocalView =
  {|
    reservations: Interop.JsDict<string>
    outcomes: Interop.JsDict<OutcomeView>
    pending: Interop.JsDict<string>
  |}

type Stores =
  { Model: ModelView
    SetModel: SolidStoreSetter<ModelView>
    Local: LocalView
    SetLocal: SolidStoreSetter<LocalView>
    /// The daemon's uptime in ms as its clock last pushed it; negative until
    /// the first Uptime of a connection. A signal rather than a store field:
    /// it changes every second and only the text that shows it reads it. The
    /// page runs no timer for it.
    Uptime: Accessor<float>
    SetUptime: Setter<float> }

[<Literal>]
let OpenKey = "open"

[<Literal>]
let WorkerKey = "worker"

// Store hazard: setting a store path to an object whose previous value is an
// object MERGES into that node, and reconcile mutates the existing node in
// place. An object placed at two store locations is therefore one node seen
// twice. Placeholders are functions and every location gets its own object.

let private noWorker () : WorkerView = {| state = "idle"; host = ""; epoch = ""; compilerVersion = ""; pid = "" |}

let private noArtifact () : ArtifactView =
  {| generation = ""; sourceVersion = ""; path = ""; sha = ""; compiled = 0; reused = 0; retired = 0; changed = 0 |}

let private noHealth () : HealthView =
  {|
    version = ""
    pid = 0
    port = 0
    overall = ""
    pressure = ""
    rss = 0.0
    available = 0.0
    total = 0.0
    cpu = 0.0
    processes = Array.empty
    alarms = Array.empty
  |}

let create () : Stores =
  let model, setModel =
    createStore
      {|
        welcomed = false
        daemonVersion = ""
        startedAt = 0.0
        protocolMismatch = false
        revision = ""
        worker = noWorker ()
        sessions = Array.empty<SessionView>
        healthSeen = false
        health = noHealth ()
        leases = Array.empty<LeaseView>
        queue = Array.empty<WaitView>
        activity = Array.empty<OutcomeView>
        runs = Array.empty<RunView>
      |}
  let local, setLocal =
    createStore
      {|
        reservations = Interop.emptyDict<string> ()
        outcomes = Interop.emptyDict<OutcomeView> ()
        pending = Interop.emptyDict<string> ()
      |}
  let uptime, setUptime = createSignal (-1.0)
  { Model = model; SetModel = setModel; Local = local; SetLocal = setLocal; Uptime = uptime; SetUptime = setUptime }

// ── Identities ─────────────────────────────────────────────────────────────

let sessionKey (target: SessionTarget) =
  target.Worker.Host + "|" + target.Worker.Epoch + "|" + target.Session

let targetOf (session: SessionView) : SessionTarget =
  { SessionTarget.Worker = { WorkerTarget.Host = session.host; Epoch = session.epoch }; Session = session.session }

/// Which UI element a command's pending state and outcome belong to.
let commandKey (command: Command) =
  match command with
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
  | RequestSnapshot -> "refresh"
  | OpenProject _ -> "open"
  | Reserve _ -> "reserve"
  | Build _ -> "build"
  | Run _ -> "run"
  | Cancel _ -> "cancel"
  | CloseSession _ -> "close"
  | RetireWorker _ -> "retire worker"

// ── Formatting (pure) ──────────────────────────────────────────────────────
// Times are shown as the instants they are, never relative to "now": a
// relative time would need a ticking clock, and the page keeps none. The one
// duration shown, the uptime, is the value the daemon's clock pushed.

[<Emit("new Date($0).toLocaleTimeString()")>]
let clockTime (epochMs: float) : string = jsNative

[<Emit("new Date($0).toLocaleString()")>]
let dateTime (epochMs: float) : string = jsNative

[<Emit("$0.toFixed($1)")>]
let fixedPoint (value: float) (digits: int) : string = jsNative

let fileName (path: string) =
  let cut = path.LastIndexOf '/'
  if cut >= 0 && cut < path.Length - 1 then path.Substring(cut + 1) else path

let shortHash (sha: string) = if sha.Length > 12 then sha.Substring(0, 12) else sha

/// A pushed uptime as the page shows it: "42s", "7m 05s", "3h 04m 05s",
/// "2d 03h 04m 05s".
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
    compiled = a.CompiledObjects
    reused = a.ReusedObjects
    retired = a.RetiredObjects
    changed = a.ChangedWitnesses
  |}

let private sessionView (s: SessionStatus) : SessionView =
  let notice label (value: string option) =
    value |> Option.map (fun text -> {| id = label; text = label + ": " + text |})
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
        notice "backend" s.BackendError
        notice "worker retirement required" s.WorkerRetirementRequired
        notice "cleanup" s.CleanupError
        notice "formatter" s.FormatterError
        notice "worker" s.WorkerError
        notice "status" s.StatusError
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
    version = h.Version
    pid = h.ProcessId
    port = h.McpPort
    overall = h.Overall
    pressure = h.MemoryPressure
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
  |}

let private leaseView (l: LeaseGrant) : LeaseView =
  {| id = l.Id; kind = l.Kind; holder = l.Holder; grantedAt = float l.GrantedAtMs; expiresAt = float l.ExpiresAtMs |}

let private waitView (w: LeaseWait) : WaitView =
  {| id = w.Holder + "|" + w.Kind; kind = w.Kind; holder = w.Holder; requestedAt = float w.RequestedAtMs |}

// ── Store writes ───────────────────────────────────────────────────────────

/// Replace one top-level slice, diffing it against what is there.
let private reconcileSlice (stores: Stores) (slice: string) (value: obj) =
  stores.SetModel.UpdatePath [| box slice; box (reconcile<obj, obj> value) |]

let private setSlice (stores: Stores) (slice: string) (value: obj) =
  stores.SetModel.UpdatePath [| box slice; value |]

/// Set (Some) or delete (None) one key of a LocalView dictionary.
let private setLocal (stores: Stores) (dict: string) (key: string) (value: obj option) =
  let written = match value with Some v -> v | None -> unbox<obj> JS.undefined
  stores.SetLocal.UpdatePath [| box dict; box key; written |]

let mutable private nextEntry = 0

let private projectOf (stores: Stores) (key: string) =
  stores.Model.sessions
  |> Array.tryFind (fun s -> s.id = key)
  |> Option.map (fun s -> fileName s.project)
  |> Option.defaultValue ""

/// Record an outcome once in the activity log and as the last outcome of
/// every given key ("" = no card, e.g. a frame-level refusal).
let private log (stores: Stores) (keys: string list) (ok: bool) (text: string) =
  nextEntry <- nextEntry + 1
  let scope =
    match keys |> List.tryFind (fun k -> k <> OpenKey && k <> WorkerKey && k <> "") with
    | Some session -> projectOf stores session
    | None -> keys |> List.tryHead |> Option.defaultValue ""
  let at = Interop.nowMs ()
  // A fresh object per location (see the store hazard note above).
  let entry () : OutcomeView = {| id = nextEntry; at = at; ok = ok; scope = scope; text = text |}
  setSlice stores "activity" (box (Array.append [| entry () |] (Array.truncate 199 stores.Model.activity)))
  for key in keys do
    if key <> "" then setLocal stores "outcomes" key (Some(box (entry ())))

/// Correlation -> (key, label) of every command still awaiting its outcome.
type Dispatcher =
  { InFlight: Dictionary<int32, string * string>
    mutable Next: int32 }

let dispatcher () = { InFlight = Dictionary(); Next = 0 }

/// Allocate a correlation and mark the command pending. Returns the frame.
let begin' (stores: Stores) (d: Dispatcher) (command: Command) : CommandFrame =
  d.Next <- d.Next + 1
  let key, label = commandKey command, commandLabel command
  d.InFlight[d.Next] <- (key, label)
  setLocal stores "pending" key (Some(box label))
  match command with
  | Build build -> setLocal stores "reservations" (sessionKey build.Target) None // single use
  | _ -> ()
  { CommandFrame.Correlation = d.Next; Command = command }

/// The frame never left (link down): settle it locally as a refusal.
let abandon (stores: Stores) (d: Dispatcher) (frame: CommandFrame) =
  match d.InFlight.TryGetValue frame.Correlation with
  | true, (key, label) ->
    d.InFlight.Remove frame.Correlation |> ignore
    setLocal stores "pending" key None
    log stores [ key ] false (label + " not sent: the bridge is disconnected.")
  | _ -> ()

/// Every pending command is lost with the connection that carried it.
let abandonAll (stores: Stores) (d: Dispatcher) =
  for KeyValue(_, (key, label)) in d.InFlight |> Seq.toArray do
    setLocal stores "pending" key None
    log stores [ key ] false (label + ": connection lost before its outcome arrived; check the session status.")
  d.InFlight.Clear()

let private settle (stores: Stores) (d: Dispatcher) (correlation: int32) (fallbackKey: string) =
  match d.InFlight.TryGetValue correlation with
  | true, (key, label) ->
    d.InFlight.Remove correlation |> ignore
    setLocal stores "pending" key None
    key, label
  | _ -> fallbackKey, "command"

let private completionText (c: Completion) =
  match c with
  | SessionOpened -> "session opened"
  | EditReserved reservation -> "edit reserved (" + shortHash reservation + "…); edit sources, then build"
  | ReservationBuilt a ->
    "built generation " + string a.Generation + " · " + shortHash a.ArtifactSha256
    + " · " + string a.CompiledObjects + " compiled, " + string a.ReusedObjects + " reused"
  | RunFinished exitCode -> "run finished, exit " + string exitCode
  | WorkCanceled -> "canceled; artifact authority withdrawn"
  | SessionClosed -> "closed; cleanup started"
  | WorkerRetired -> "worker retired; reopen projects on a fresh epoch"
  | SnapshotSent -> "refreshed"

/// The fold: one backend Event into the stores.
let apply (stores: Stores) (d: Dispatcher) (event: Event) =
  batch (fun () ->
    match event with
    | Welcome w ->
      // A new connection, perhaps to a restarted daemon: until its clock
      // ticks, the page shows only the start time.
      stores.SetUptime(-1.0)
      setSlice stores "welcomed" (box true)
      setSlice stores "daemonVersion" (box w.DaemonVersion)
      setSlice stores "startedAt" (box (float w.StartedAtMs))
      setSlice stores "protocolMismatch" (box (w.ProtocolVersion <> ProtocolVersion))
    | Snapshot snapshot ->
      setSlice stores "revision" (box (string snapshot.Revision))
      reconcileSlice stores "worker" (box (workerView snapshot.Worker))
      let sessions = snapshot.Sessions |> Array.map sessionView
      reconcileSlice stores "sessions" (box sessions)
      // A reservation cannot outlive its open session.
      for key in Interop.dictKeys stores.Local.reservations do
        let live = sessions |> Array.exists (fun s -> s.id = key && not s.closed)
        if not live then setLocal stores "reservations" key None
    | Accepted accepted ->
      let target = sessionKey accepted.Target
      let key, label = settle stores d accepted.Correlation target
      match accepted.Completion with
      | EditReserved reservation -> setLocal stores "reservations" target (Some(box reservation))
      | WorkCanceled | SessionClosed -> setLocal stores "reservations" target None
      | WorkerRetired ->
        for reserved in Interop.dictKeys stores.Local.reservations do
          setLocal stores "reservations" reserved None
      | SessionOpened | ReservationBuilt _ | RunFinished _ | SnapshotSent -> ()
      let text = label + ": " + completionText accepted.Completion
      if accepted.Completion = SnapshotSent then ()
      elif key = OpenKey then log stores [ target; OpenKey ] true text
      else log stores [ key ] true text
    | Refused refused ->
      let key, label = settle stores d refused.Correlation (sessionKey refused.Target)
      // An empty target ("||") is a frame-level refusal: no card owns it.
      let key = if key = "||" then "" else key
      if key <> "" && (refused.Code = "invalid_reservation" || refused.Code = "closed") then
        setLocal stores "reservations" key None
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
      setSlice stores "runs" (box (Array.append [| view |] (Array.truncate 19 stores.Model.runs)))
    | Health health ->
      setSlice stores "healthSeen" (box true)
      reconcileSlice stores "health" (box (healthView health))
    | Leases board ->
      reconcileSlice stores "leases" (box (board.Active |> Array.map leaseView))
      reconcileSlice stores "queue" (box (board.Queued |> Array.map waitView))
    | Uptime tick -> stores.SetUptime(float tick.UptimeMs))
