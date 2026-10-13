/// A canned, in-memory stand-in for Bozzetto's `/ui/bridge`, so the UI can be
/// exercised without a daemon (`npm run dev:mock`). It speaks the real
/// protocol through the backend-side codec and follows the same contract the
/// backend must honour (see Shared/Protocol.fs): one outcome per command, and
/// pushes only when something changed. Snapshot goes to every client on a
/// session or worker change, Leases when the mock pool grants, queues or
/// releases, and Health when one of its discrete facts changes (the verdict,
/// the process set, the alarms); none of these is pushed on a timer. Uptime
/// comes from the one clock, as in the daemon: it ticks once a second while at
/// least one page is connected and stops with the last. State is shared by all
/// connections, like the daemon's.
///
/// Canned behavior: any absolute *.fidproj path opens; a path containing
/// "broken" builds into a compiler refusal; reservations are single-use; runs
/// echo their arguments. A build holds a full_build lease and a run a run_app
/// lease, one of each kind at a time, so overlapping builds queue. The build
/// and run delays stand in for compiler work; they are not polling.
module Bozzetto.Web.Mock.Bridge

open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop
open Bozzetto.Web.Shared.Protocol

[<Emit("setTimeout($1, $0)")>]
let private after (milliseconds: int) (action: unit -> unit) : unit = jsNative

[<Emit("setInterval($1, $0)")>]
let private every (milliseconds: int) (action: unit -> unit) : obj = jsNative

[<Emit("clearInterval($0)")>]
let private stopEvery (handle: obj) : unit = jsNative

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

[<Emit("Math.random()")>]
let private random () : float = jsNative

[<Emit("Math.floor(Math.random() * 0x100000000).toString(16).padStart(8, '0')")>]
let private token () : string = jsNative

[<Emit("new TextEncoder().encode($0)")>]
let private transcriptBytes (text: string) : byte array = jsNative

type private MockSession = { mutable Status: SessionStatus; mutable Reservation: string option }

let private daemonVersion = "0.6.834-mock"
let private startedAt = nowMs ()
let private clients = Dictionary<int, byte array -> unit>()
let private sessions = ResizeArray<MockSession>()
let mutable private nextClient = 0
let mutable private nextSession = 0
let mutable private worker = Idle
let mutable private revision = 0L
let mutable private nextLease = 0
let mutable private lastHealthFacts = ""
let mutable private clock: obj option = None
let private active = ResizeArray<LeaseGrant>()
let private queued = ResizeArray<LeaseWait * int * (string -> unit)>()

let private noTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }

let private snapshot () =
  Snapshot {
    ComposerSnapshot.Revision = revision
    Worker = worker
    Sessions = sessions |> Seq.map (fun s -> s.Status) |> Seq.toArray
  }

let mutable private healthRevision = 0L
let private healthValue () : DaemonHealth =
  let workerProcesses =
    match worker with
    | Running info ->
      [|
        { ProcessSample.ProcessId = info.ProcessId |> Option.defaultValue 0
          Role = "composer-worker"
          ResidentBytes = 412_000_000L + int64 (random () * 30_000_000.0)
          CpuPercent = random () * 20.0 }
      |]
    | Unconfigured | Idle -> [||]
  let open' = sessions |> Seq.filter (fun s -> not s.Status.Closed) |> Seq.length
  let alarms =
    if open' >= 3 then
      [| { HealthAlarm.Signal = "WorkerRss"; State = "Drifting"; Message = "Composer worker RSS is 2.4 sigma above its baseline (mock)." } |]
    else [||]
  healthRevision <- healthRevision + 1L
  {
    DaemonHealth.Revision = healthRevision
    Version = daemonVersion
    ProcessId = 4242
    McpPort = 47759
    Overall = (if alarms.Length > 0 then "Degraded" else "Healthy")
    MemoryPressure = "Normal"
    MemoryPressureNote = "60.0% of RAM available — the machine has room; tight at or below 20%"
    ResidentBytes = 186_000_000L + int64 (random () * 12_000_000.0)
    MachineAvailableBytes = 38_500_000_000L + int64 (random () * 400_000_000.0)
    MachineTotalBytes = 66_571_993_088L
    SampledAtMs = int64 (nowMs ())
    RecordAtMs = int64 (nowMs ())
    CpuPercent = random () * 6.0
    Processes =
      Array.append
        [| { ProcessSample.ProcessId = 4242; Role = "daemon"; ResidentBytes = 186_000_000L; CpuPercent = 1.5 } |]
        workerProcesses
    Alarms = alarms
  }

let private health () = Health(healthValue ())

/// Explicitly synthetic reports cover populated rows, unknown usage and known
/// zero. They are never model-worker, compiler or installed-delivery evidence.
let private agentWork () =
  // Existing project-open controls advance a synthetic pass: progress, then
  // completion. No timer, extra command or live-daemon activity is invented.
  let stage = min 2 sessions.Count
  let run id memberId name role usage status offset : AgentRun = {
    Id = id; Member = memberId; Role = role; Name = name; Session = "mock-session"
    ReportSequence = 1L; Model = "mock-model"; Endpoint = "mock inference"
    Project = "/mock/Visibility.fidproj"; Focus = "mock visibility check"; Operation = "read"
    Status = status; UpdatedAtMs = int64 startedAt + offset; Activity = [| "read" |]
    OmittedActivity = 0L; Usage = usage
  }
  let implementation =
    run "mock-implementation" "mock:impl" "Mock implementer" "Implementer"
      (Some { Input = 100L; Output = 50L; CacheRead = 100L; CacheWrite = 0L; Total = 250L; EstimateUsd = Some 0.125 }) "running" -3000L
  let implementation =
    if stage = 0 then implementation
    else
      let operation = if stage = 1 then "mock read progressed" else "mock read completed"
      { implementation with Status = (if stage = 1 then "running" else "completed")
                            ReportSequence = int64 stage + 1L; UpdatedAtMs = int64 startedAt + int64 stage
                            Operation = operation; Activity = [| operation; "read" |] }
  AgentWork {
    Incarnation = "mock-incarnation"; Sequence = 4L + int64 stage; ExecutionHost = "mock-host"; Capacity = 256
    Runs = [|
      implementation
      run "mock-audit" "mock:verifier" "Mock verifier" "Verifier" None "completed" -2000L
      run "mock-zero" "mock:observer" "Mock observer" "Observer"
        (Some { Input = 0L; Output = 0L; CacheRead = 0L; CacheWrite = 0L; Total = 0L; EstimateUsd = None }) "disconnected" -1000L
      run "mock-uncertain" "mock:uncertain" "Mock uncertain" "Observer" None "unknown" -1000L
    |]
    // Derived presence: a lease the mock implementer holds and a member that
    // has made no report, so the panel shows both shapes.
    Presence = [|
      { Name = "Mock builder"; Source = "lease"; Member = "Mock builder · /mock/clef"; Project = "/mock/clef"; Detail = "holds full_build"; SinceMs = int64 startedAt - 4000L }
      { Name = "mcp:quiet"; Source = "member"; Member = "mcp:quiet"; Project = ""; Detail = "Observer member · no report yet"; SinceMs = int64 startedAt - 9000L }
    |]
  }

/// The discrete facts a Health push is for. The byte and CPU readings are
/// not among them: they ride along when Health is sent for one of these.
let private healthFacts (h: DaemonHealth) =
  let processes = h.Processes |> Array.map (fun p -> string p.ProcessId + ":" + p.Role) |> String.concat ","
  let alarms = h.Alarms |> Array.map (fun a -> a.Signal + ":" + a.State) |> String.concat ","
  h.Overall + "|" + h.MemoryPressure + "|" + processes + "|" + alarms

let mutable private leaseRevision = 0L
let private leases () =
  leaseRevision <- leaseRevision + 1L
  Leases {
    LeaseBoard.Revision = leaseRevision
    Active = active.ToArray()
    Queued = queued |> Seq.map (fun (wait, _, _) -> wait) |> Seq.toArray
  }

let mutable private workRevision = 0L
let private workNodes () =
  workRevision <- workRevision + 1L
  let snapshot : WorkSnapshot = {
    TranscriptsOmitted = true
    Sequence = workRevision; SampledAtMs = int64 (nowMs ())
    Snapshot = { Revision = revision; Worker = worker; Sessions = sessions |> Seq.map _.Status |> Seq.toArray }
    Leases = { Revision = leaseRevision; Active = active.ToArray(); Queued = queued |> Seq.map (fun (wait, _, _) -> wait) |> Seq.toArray }
    Operations = { Revision = 0L; Incarnation = "mock-journal"; StorageError = None; Entries = [||] }
  }
  let remote = { snapshot with Snapshot = { Revision = 0L; Worker = Idle; Sessions = [||] }; Leases = { Revision = 0L; Active = [||]; Queued = [||] } }
  let finished = sessions.Count >= 2
  let run = {
    OperationEntry.Sequence = 7L; AtMs = int64 startedAt; CallerLabel = "fixture"; Operation = "test_suite_run"; Session = ""
    Project = "/mock/clef"; Ok = false; Code = (if finished then "command_failed" else "running"); Message = (if finished then "dotnet exited 1" else "")
    Lease = Some { Kind = "test_suite_run"; LeaseId = "fixture-lease"; RunId = "fixture-test-run"; Executable = "/mock/dotnet"
                   Arguments = [| "vstest"; "/mock/clef/tests.dll"; "--TestCaseFilter:ForeignReferenceTests" |]
                   ProcessId = 6543; StartedAtMs = int64 startedAt }
    Run = if finished then Some { ExitCode = 1; Compiler = None; StandardOutput = ""; StandardError = ""; Dropped = false } else None
  }
  let busy = { remote with
                Snapshot = { remote.Snapshot with Worker = Running { Target = { Host = "fixture-host"; Epoch = "fixture-epoch" }; CompilerVersion = "fixture-compiler"; ProcessId = Some 7654 } }
                Operations = { remote.Operations with Entries = [| run |] }
                Leases = { remote.Leases with
                             Active = if finished then [||] else [| { Id = "fixture-lease"; Kind = "test_suite_run"; Holder = "fixture ONE test · /mock/clef"; GrantedAtMs = int64 startedAt; ExpiresAtMs = int64 startedAt + 120000L } |]
                             Queued = if finished then [||] else [| { Kind = "full_build"; Holder = "fixture queued build · /mock/Farscape"; RequestedAtMs = int64 startedAt } |] } }
  WorkNodes { Revision = workRevision; Nodes = [|
    { Source = { Node = "controller"; Incarnation = "mock-controller" }; Local = true; Status = "available"; Note = "Synthetic controller fixture"; Value = Some snapshot }
    { Source = { Node = "one"; Incarnation = "mock-one" }; Local = false; Status = "available"; Note = "Synthetic remote build fixture"; Value = Some busy }
    { Source = { Node = "two"; Incarnation = "mock-two" }; Local = false; Status = "available"; Note = "Synthetic observed idle fixture"; Value = Some remote }
    { Source = { Node = "three"; Incarnation = "mock-three" }; Local = false; Status = "unavailable"; Note = "Synthetic disconnected fixture"; Value = None }
  |] }

let private broadcast (event: Event) =
  let frame = Codec.encodeEvent event
  for send in clients.Values |> Seq.toArray do send frame
  match event with
  | Snapshot _ | Leases _ ->
    let nodes = Codec.encodeEvent (workNodes ())
    for send in clients.Values |> Seq.toArray do send nodes
  | _ -> ()

/// The mock's one clock, as the daemon's: a clock-owned push, not a poll. Each
/// tick pushes the uptime it measures and reads nothing else. It runs only
/// while a page is connected: the first connect starts it, the last close
/// stops it.
let private tick () = broadcast (Uptime { UptimeTick.UptimeMs = int64 (nowMs () - startedAt) })

let private attachClock () =
  if clock.IsNone then clock <- Some(every 1000 tick)

let private detachClock () =
  if clients.Count = 0 then
    clock |> Option.iter stopEvery
    clock <- None

/// Edge-triggered: Health goes out only when its discrete facts changed.
let private healthChanged () =
  let current = healthValue ()
  let facts = healthFacts current
  if facts <> lastHealthFacts then
    lastHealthFacts <- facts
    broadcast (Health current)

let private changed () =
  revision <- revision + 1L
  broadcast (snapshot ())
  healthChanged ()

let private grant kind holder (minutes: int) =
  nextLease <- nextLease + 1
  let now = int64 (nowMs ())
  let id = "lease-" + string nextLease
  active.Add { LeaseGrant.Id = id; Kind = kind; Holder = holder; GrantedAtMs = now; ExpiresAtMs = now + int64 minutes * 60_000L }
  id

/// The mock pool: one lease of each kind at a time, queued in arrival order.
/// `start` runs with the lease id once it is granted; every change is pushed.
let private acquire kind holder minutes (start: string -> unit) =
  if active |> Seq.exists (fun l -> l.Kind = kind) then
    queued.Add(({ LeaseWait.Kind = kind; Holder = holder; RequestedAtMs = int64 (nowMs ()) }, minutes, start))
    broadcast (leases ())
  else
    let id = grant kind holder minutes
    broadcast (leases ())
    start id

let private release (id: string) =
  match active |> Seq.tryFindIndex (fun l -> l.Id = id) with
  | None -> ()
  | Some index ->
    let kind = active[index].Kind
    active.RemoveAt index
    match queued |> Seq.tryFindIndex (fun (wait, _, _) -> wait.Kind = kind) with
    | Some next ->
      let wait, minutes, start = queued[next]
      queued.RemoveAt next
      let granted = grant wait.Kind wait.Holder minutes
      broadcast (leases ())
      start granted
    | None -> broadcast (leases ())

let private reply (send: byte array -> unit) (event: Event) = send (Codec.encodeEvent event)

let private refuse send correlation target code message =
  reply send (Refused { CommandRefusal.Correlation = correlation; Target = target; Code = code; Message = message })

let private accept send correlation target completion =
  reply send (Accepted { CommandAcceptance.Correlation = correlation; Target = target; Completion = completion })

let private update (session: MockSession) (change: SessionStatus -> SessionStatus) =
  session.Status <- change session.Status
  changed ()

/// Resolve a target the way the supervisor does: worker authority first.
let private withSession send correlation (target: SessionTarget) (action: MockSession -> unit) =
  match worker with
  | Running info when info.Target = target.Worker ->
    match sessions |> Seq.tryFind (fun s -> s.Status.Target.Session = target.Session) with
    | Some session when session.Status.Closed -> refuse send correlation target "closed" "The session is closed and cannot be reused."
    | Some session -> action session
    | None -> refuse send correlation target "unknown_session" "No Composer session has this id."
  | Running _ -> refuse send correlation target "wrong_authority" "Use the owning host and epoch from the Composer session response."
  | Unconfigured | Idle -> refuse send correlation target "provider_unavailable" "No active Composer worker owns this operation."

let private ensureWorker () =
  match worker with
  | Running info -> info.Target
  | Unconfigured | Idle ->
    let target = { WorkerTarget.Host = "mock-host"; Epoch = "epoch-" + token () }
    worker <- Running { WorkerInfo.Target = target; CompilerVersion = "0.0.2+mock"; ProcessId = Some(5000 + int (random () * 1000.0)) }
    target

let private artifactFor (session: MockSession) =
  let generation = session.Status.Generation + 1L
  { ArtifactSummary.Generation = generation
    SourceVersion = "src-" + token ()
    ArtifactPath = "/tmp/composer-mock/" + session.Status.Target.Session + "/a.out"
    ArtifactSha256 = token () + token () + token () + token () + token () + token () + token () + token ()
    Regions =
      let secondChanged = random () < 0.5
      [| { RegionRow.Identity = "common"; Witness = "changed"; Visits = 40 + int (random () * 400.0); Object = "compiled" }
         { RegionRow.Identity = "callable:Demo:first"; Witness = "retained"; Visits = 0; Object = "reused" }
         { RegionRow.Identity = "callable:Demo:second"; Witness = (if secondChanged then "changed" else "retained"); Visits = (if secondChanged then 9 else 0); Object = (if secondChanged then "compiled" else "reused") } |] }

let private resourceCorrelations = Dictionary<string, int>()

let private handle (send: byte array -> unit) (frame: CommandFrame) =
  let correlation = frame.Correlation
  match frame.Command with
  | ObserveResources _ ->
    refuse send correlation noTarget "unsupported" "Host resource acquisition is available only on the Linux daemon, not the mock."
  | ReadNodeTranscript request ->
    let outcome =
      if request.Journal <> "mock-journal" || request.Sequence <> 7L then TranscriptRefused("missing_entry", "The selected stored entry is unavailable.")
      elif request.Stream = Stderr then TranscriptRefused("storage_failure", "Synthetic transcript store refusal.")
      else
        // The first page splits a four-byte symbol; decoding waits for exact byte assembly.
        let bytes = transcriptBytes (String.replicate 32767 "x" + "🎼 stored tail")
        let offset = int request.Offset
        let count = min request.MaxBytes (bytes.Length - offset)
        if offset < 0 || offset > bytes.Length then TranscriptRefused("invalid_cursor", "Outside stored stream.")
        else TranscriptPage {
          Journal = request.Journal; Sequence = request.Sequence; Stream = request.Stream; Offset = request.Offset
          NextOffset = (if offset + count < bytes.Length then Some(int64 (offset + count)) else None)
          TotalBytes = int64 bytes.Length; Bytes = bytes[offset .. offset + count - 1]; Truncated = true }
    reply send (NodeTranscript { Source = { request.Source with Incarnation = "retired-source" }; Correlation = correlation; Outcome = TranscriptRefused("stale_source", "must be ignored") })
    reply send (NodeTranscript { Source = request.Source; Correlation = correlation; Outcome = outcome })
  | ObserveNodeResources request when request.Seconds = 0 ->
    // A stopped demand acknowledges physical observation cleanup; it does not
    // manufacture another shared node sample or change its sample timestamp.
    accept send correlation noTarget SnapshotSent
  | ObserveNodeResources request when request.Node = "three" ->
    refuse send correlation noTarget "node_unavailable" "Synthetic disconnected fixture"
  | ObserveNodeResources request ->
    let resource scope node incarnation sequence value = NodeResources {
      Source = { Node = node; Incarnation = incarnation }
      Correlation = scope
      Value = { Sequence = sequence; Status = "observing"; SampledAtMs = int64 (nowMs ())
                IntervalMs = 2000.; SampleCostMs = 1.; Metrics = [| { Name = "Host CPU busy"; Value = Some value; Unit = "percent" } |]
                Processes = [||]; ProcessCount = 0; UnreadableCount = 0; OmittedCount = 0; Note = "Synthetic resource fixture" }
    }
    reply send (resource correlation request.Node ("mock-" + request.Node) 10L (if request.Node = "one" then 17. else 23.))
    // Deliberately adversarial fixture frames prove the UI's node/incarnation/sequence boundary.
    reply send (resource correlation request.Node ("mock-" + request.Node) 9L 91.)
    reply send (resource correlation request.Node "retired-incarnation" 999L 92.)
    reply send (resource correlation "foreign-node" "mock-foreign-node" 999L 93.)
    match resourceCorrelations.TryGetValue request.Node with
    | true, previous -> reply send (resource previous request.Node ("mock-" + request.Node) 999L 94.)
    | _ -> ()
    resourceCorrelations[request.Node] <- correlation
    accept send correlation noTarget SnapshotSent
  | RequestSnapshot ->
    reply send (snapshot ())
    reply send (health ())
    reply send (leases ())
    accept send correlation noTarget SnapshotSent
  | OpenProject project ->
    if not (project.StartsWith "/") || not (project.EndsWith ".fidproj") then
      refuse send correlation noTarget "invalid_project" "Open an absolute path to a Clef .fidproj file."
    else
      let workerTarget = ensureWorker ()
      nextSession <- nextSession + 1
      let target = { SessionTarget.Worker = workerTarget; Session = "session-" + string nextSession + "-" + token () }
      let status = {
        SessionStatus.Target = target
        Generation = 1L
        Project = project
        Closed = false
        Busy = false
        StatusFresh = true
        RevocationPending = false
        CleanupPending = false
        FormatterCleanupPending = false
        Current = None
        BackendError = None
        FormatterError = None
        CleanupError = None
        WorkerRetirementRequired = None
        StatusError = None
        WorkerError = None
      }
      sessions.Add { Status = status; Reservation = None }
      changed ()
      broadcast (agentWork ())
      accept send correlation target SessionOpened
  | Reserve edit ->
    withSession send correlation edit.Target (fun session ->
      if session.Status.Busy then refuse send correlation edit.Target "busy" "A build or run is in progress."
      else
        let reservation = "rsv-" + token () + token ()
        session.Reservation <- Some reservation
        update session (fun s -> { s with Generation = s.Generation + 1L; Current = None })
        accept send correlation session.Status.Target (EditReserved reservation))
  | Build build ->
    withSession send correlation build.Target (fun session ->
      match session.Reservation with
      | Some held when held = build.Reservation ->
        session.Reservation <- None
        update session (fun s -> { s with Busy = true; StatusFresh = false })
        acquire "full_build" ("mock-build-" + string correlation) 10 (fun lease ->
          after 1200 (fun () ->
            release lease
            if session.Status.Project.Contains "broken" then
              update session (fun s -> { s with Busy = false; StatusFresh = true })
              refuse send correlation session.Status.Target "compiler_refused" "error CCS0001: Main.clef(3,5): value 'pritnfn' is not defined (mock diagnostic)"
            else
              let artifact = artifactFor session
              update session (fun s -> { s with Busy = false; StatusFresh = true; Generation = s.Generation + 1L; Current = Some artifact })
              accept send correlation session.Status.Target (ReservationBuilt artifact)))
      | _ -> refuse send correlation build.Target "invalid_reservation" "Reserve an edit first; a reservation builds once.")
  | Run run ->
    withSession send correlation run.Target (fun session ->
      match session.Status.Current with
      | None -> refuse send correlation run.Target "not_accepted" "No accepted artifact: reserve, edit and build first."
      | Some artifact ->
        update session (fun s -> { s with Busy = true })
        acquire "run_app" ("mock-run-" + string correlation) 240 (fun lease ->
          after 600 (fun () ->
            release lease
            update session (fun s -> { s with Busy = false })
            let exitCode = if run.Arguments |> Array.contains "--fail" then 3 else 0
            let output =
              "Hello from " + session.Status.Project + " — génération " + string artifact.Generation + " ✓\n"
              + (run.Arguments |> Array.mapi (fun i a -> "argv[" + string i + "] = " + a) |> String.concat "\n")
            reply send (RunOutput {
              RunTranscript.Correlation = correlation
              Target = session.Status.Target
              Generation = artifact.Generation
              SourceVersion = artifact.SourceVersion
              ExitCode = exitCode
              StandardOutput = output
              StandardError = (if exitCode = 0 then "" else "fatal: --fail was requested (mock)\n")
            })
            accept send correlation session.Status.Target (RunFinished exitCode))))
  | Cancel target ->
    withSession send correlation target (fun session ->
      session.Reservation <- None
      update session (fun s -> { s with Busy = false; Current = None; RevocationPending = true })
      after 500 (fun () -> update session (fun s -> { s with RevocationPending = false }))
      accept send correlation session.Status.Target WorkCanceled)
  | CloseSession target ->
    withSession send correlation target (fun session ->
      session.Reservation <- None
      update session (fun s -> { s with Closed = true; Busy = false; Current = None; CleanupPending = true })
      after 900 (fun () -> update session (fun s -> { s with CleanupPending = false }))
      accept send correlation session.Status.Target SessionClosed)
  | RetireWorker target ->
    match worker with
    | Running info when info.Target = target ->
      for session in sessions do
        session.Reservation <- None
        session.Status <- { session.Status with Closed = true; Busy = false; Current = None; WorkerError = Some "Compiler retirement requested." }
      worker <- Idle
      changed ()
      accept send correlation { noTarget with Worker = target } WorkerRetired
    | Running _ ->
      refuse send correlation { noTarget with Worker = target } "wrong_authority" "Use the owning host and epoch of the live worker."
    | Unconfigured | Idle ->
      refuse send correlation { noTarget with Worker = target } "provider_unavailable" "No Composer worker is running."

/// Called by the dev-server plugin for each accepted WebSocket. `send` writes
/// one text frame to that socket. Returns {receive(text), close()}.
/// The daemon's operations journal as the mock publishes it: three settled operations from two callers.
let mutable private operationsRevision = 0L
let private operations () =
  operationsRevision <- operationsRevision + 1L
  let at = int64 (nowMs ())
  Operations {
    StorageError = None
    OperationsBoard.Revision = operationsRevision
    Incarnation = "mock-journal"
    Entries = [|
      { OperationEntry.Sequence = 3L; AtMs = at - 20_000L; CallerLabel = "mcp:3f9c"; Operation = "run"; Session = "s-1"; Project = "/home/mock/Hello/Hello.fidproj"
        Ok = true; Code = ""; Message = ""; Lease = None
        Run = Some { OperationRun.ExitCode = 0; Compiler = Some { Generation = 4L; SourceVersion = "src-4" }; StandardOutput = "hello from the mock run\n"; StandardError = ""; Dropped = false } }
      { OperationEntry.Sequence = 2L; AtMs = at - 60_000L; CallerLabel = "mcp:3f9c"; Operation = "build"; Session = "s-1"; Project = "/home/mock/Hello/Hello.fidproj"
        Ok = false; Code = "invalid_reservation"; Message = "The reservation was superseded."; Run = None; Lease = None }
      { OperationEntry.Sequence = 1L; AtMs = at - 120_000L; CallerLabel = "browser"; Operation = "open"; Session = "s-1"; Project = "/home/mock/Hello/Hello.fidproj"
        Ok = true; Code = ""; Message = ""; Run = None; Lease = None } |] }

let connect (send: byte array -> unit) : obj =
  nextClient <- nextClient + 1
  let id = nextClient
  reply send (Welcome { BridgeWelcome.ProtocolVersion = ProtocolVersion; DaemonVersion = daemonVersion; StartedAtMs = int64 startedAt })
  reply send (snapshot ())
  reply send (health ())
  reply send (leases ())
  reply send (agentWork ())
  reply send (operations ())
  reply send (workNodes ())
  // Pushes (snapshots, leases, health, uptime) follow the prologue.
  clients[id] <- send
  attachClock ()
  let receive (payload: byte array) =
    match Codec.decodeCommand payload with
    | Ok frame -> handle send frame
    | Error message -> refuse send (0) noTarget "invalid_request" message
  let close () =
    clients.Remove id |> ignore
    detachClock ()
  createObj [ "receive" ==> receive; "close" ==> close ]
