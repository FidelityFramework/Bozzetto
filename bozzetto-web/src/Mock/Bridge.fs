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

type private MockSession = { mutable Status: SessionStatus; mutable Reservation: string option }

let private daemonVersion = "0.6.834-mock"
let private startedAt = nowMs ()
let private clients = Dictionary<int, string -> unit>()
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
  {
    DaemonHealth.Version = daemonVersion
    ProcessId = 4242
    McpPort = 47759
    Overall = (if alarms.Length > 0 then "Degraded" else "Healthy")
    MemoryPressure = "Normal"
    ResidentBytes = 186_000_000L + int64 (random () * 12_000_000.0)
    MachineAvailableBytes = 38_500_000_000L + int64 (random () * 400_000_000.0)
    MachineTotalBytes = 66_571_993_088L
    CpuPercent = random () * 6.0
    Processes =
      Array.append
        [| { ProcessSample.ProcessId = 4242; Role = "daemon"; ResidentBytes = 186_000_000L; CpuPercent = 1.5 } |]
        workerProcesses
    Alarms = alarms
  }

let private health () = Health(healthValue ())

/// The discrete facts a Health push is for. The byte and CPU readings are
/// not among them: they ride along when Health is sent for one of these.
let private healthFacts (h: DaemonHealth) =
  let processes = h.Processes |> Array.map (fun p -> string p.ProcessId + ":" + p.Role) |> String.concat ","
  let alarms = h.Alarms |> Array.map (fun a -> a.Signal + ":" + a.State) |> String.concat ","
  h.Overall + "|" + h.MemoryPressure + "|" + processes + "|" + alarms

let private leases () =
  Leases {
    LeaseBoard.Active = active.ToArray()
    Queued = queued |> Seq.map (fun (wait, _, _) -> wait) |> Seq.toArray
  }

let private broadcast (event: Event) =
  let frame = Codec.encodeEvent event
  for send in clients.Values |> Seq.toArray do send frame

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

let private reply (send: string -> unit) (event: Event) = send (Codec.encodeEvent event)

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
    CompiledObjects = 1 + int (random () * 6.0)
    ReusedObjects = 10 + int (random () * 40.0)
    RetiredObjects = int (random () * 2.0)
    ChangedWitnesses = 1 + int (random () * 4.0) }

let private handle (send: string -> unit) (frame: CommandFrame) =
  let correlation = frame.Correlation
  match frame.Command with
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
let connect (send: string -> unit) : obj =
  nextClient <- nextClient + 1
  let id = nextClient
  reply send (Welcome { BridgeWelcome.ProtocolVersion = ProtocolVersion; DaemonVersion = daemonVersion; StartedAtMs = int64 startedAt })
  reply send (snapshot ())
  reply send (health ())
  reply send (leases ())
  // Pushes (snapshots, leases, health, uptime) follow the prologue.
  clients[id] <- send
  attachClock ()
  let receive (payload: string) =
    match Codec.decodeCommand payload with
    | Ok frame -> handle send frame
    | Error message -> refuse send (Codec.correlationOf payload) noTarget "invalid_request" message
  let close () =
    clients.Remove id |> ignore
    detachClock ()
  createObj [ "receive" ==> receive; "close" ==> close ]
