/// The browser UI bridge: the backend end of bozzetto-web's one WebSocket.
///
/// MVU as a metaphor only. The page's stores are the model and this module is
/// the update function: Commands arrive, run against the daemon's one
/// ComposerSupervisor exactly as POST /api/composer/{op} and the composer_*
/// MCP tools run them, and Events go back. Every command frame is answered by
/// exactly one Accepted or Refused carrying its correlation (Run sends its
/// RunOutput first).
///
/// Snapshot is sent on connect, in answer to RequestSnapshot, and when the
/// supervisor's Changed signal fires (coalesced). Lease owner changes push
/// Leases, including the pool's one-shot expiry deadline. Health remains a
/// connection/request snapshot, explicitly labelled by the page. The hub's
/// uptime clock runs only while a page is connected and owns Uptime pushes.
/// None of these subscriptions scans the host or admits compiler work.
///
/// The vocabulary is bozzetto-web/src/Shared/Protocol.fs, compiled into this
/// assembly from that same file. The wire is UiBridgeCodec's alone.
module Bozzetto.Server.UiBridge

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Globalization
open System.IO
open System.Net.WebSockets
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Bozzetto.Utils
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration
open Bozzetto.Web.Shared.Protocol

/// What the health projection reads: the daemon's own observations, with the
/// values to report before its first health sample.
type HealthSources = {
  McpPort: int
  Version: string
  ProcessId: int
  Health: Bozzetto.Features.HealthSnapshot option
  Telemetry: DaemonTelemetry.Snapshot option
  Machine: Bozzetto.Features.MachineMemory.Stats
}

/// Pure projections from Composer's typed replies and the daemon's own
/// observations onto the bridge vocabulary.
module Project =

  let agentWork (state: Bozzetto.AgentWork.State) : AgentWorkBoard =
    { Incarnation = state.Incarnation; Sequence = state.Sequence; ExecutionHost = Environment.MachineName; Capacity = 256
      Runs = state.Runs |> Map.toArray |> Array.map (fun (key, run) ->
        let r = run.Report
        { Id = key; Member = Bozzetto.MemberTable.MemberId.display run.Member; Role = string run.Role
          Name = r.Name; Session = r.Session; ReportSequence = r.Sequence; Model = r.Model; Endpoint = r.Endpoint
          Project = r.Project; Focus = r.Focus; Operation = r.Operation; Status = r.Status
          UpdatedAtMs = DateTimeOffset(run.UpdatedAt).ToUnixTimeMilliseconds()
          Activity = List.toArray run.Activity; OmittedActivity = run.OmittedActivity
          Usage = r.Usage |> Option.map (fun u -> { Input = u.Input; Output = u.Output; CacheRead = u.CacheRead; CacheWrite = u.CacheWrite; Total = u.Total; EstimateUsd = u.EstimateUsd }) }) }

  let targetOf (authority: Authority) : SessionTarget =
    { SessionTarget.Worker = { WorkerTarget.Host = authority.Host; Epoch = authority.Epoch }; Session = authority.Session }

  let artifactSummary (artifact: AcceptedArtifact) : ArtifactSummary =
    { ArtifactSummary.Generation = artifact.Generation
      SourceVersion = artifact.SourceVersion
      ArtifactPath = artifact.ArtifactPath
      ArtifactSha256 = artifact.ArtifactSha256
      CompiledObjects = artifact.CompiledObjects.Length
      ReusedObjects = artifact.ReusedObjects.Length
      RetiredObjects = artifact.RetiredObjects.Length
      ChangedWitnesses = artifact.ChangedWitnesses.Length }

  /// The supervisor caches only observed statuses. Anything else is shown as
  /// a stale entry rather than hidden, so the page never loses a session.
  let sessionStatus (response: ComposerResponse) : SessionStatus =
    let authority = response.Reply.Authority
    let unobserved =
      { SessionStatus.Target = targetOf authority
        Generation = authority.Generation
        Project = ""
        Closed = false
        Busy = false
        StatusFresh = false
        RevocationPending = false
        CleanupPending = false
        FormatterCleanupPending = false
        Current = None
        BackendError = None
        FormatterError = None
        CleanupError = None
        WorkerRetirementRequired = None
        StatusError =
          Some(response.StatusError |> Option.defaultValue "The supervisor holds no status observation for this session.")
        WorkerError = response.WorkerError }
    match response.Reply.Outcome with
    | Result.Ok(ReplyBody.Observed status) ->
      { unobserved with
          Project = status.Project
          Closed = status.Closed
          Busy = status.Busy
          StatusFresh = response.StatusFresh
          RevocationPending = status.RevocationPending
          CleanupPending = status.CleanupPending
          FormatterCleanupPending = status.FormatterCleanupPending
          Current = status.Current |> Option.map artifactSummary
          BackendError = status.BackendError
          FormatterError = status.FormatterError
          CleanupError = status.CleanupError
          WorkerRetirementRequired = status.WorkerRetirementRequired
          StatusError = response.StatusError }
    | _ -> unobserved

  let workerState (directory: ComposerDirectory) (workerPid: int option) : WorkerState =
    match directory.Configured, directory.Worker with
    | false, _ -> Unconfigured
    | true, Some reply ->
      match reply.Outcome with
      | Result.Ok(ReplyBody.HelloAccepted hello) ->
        Running {
          WorkerInfo.Target = (targetOf reply.Authority).Worker
          CompilerVersion = hello.Compiler.Version
          ProcessId = workerPid
        }
      | _ -> Idle
    | true, None -> Idle

  let snapshot (directory: ComposerDirectory) (workerPid: int option) : ComposerSnapshot =
    { ComposerSnapshot.Revision = directory.Revision
      Worker = workerState directory workerPid
      Sessions = directory.Sessions |> Array.map sessionStatus }

  let private workerAddress (target: WorkerTarget) : WorkerAddress =
    { WorkerAddress.Host = target.Host; Epoch = target.Epoch; Provider = ProviderIdentity.ClefComposer }

  let private sessionAddress (target: SessionTarget) : SessionAddress =
    { SessionAddress.Worker = workerAddress target.Worker; Session = target.Session }

  /// The Composer operation a command runs: the same request the composer_*
  /// MCP tools and POST /api/composer/{op} build. None for RequestSnapshot,
  /// which the bridge answers itself.
  let toRequest (command: Command) : RequestBody option =
    match command with
    | RequestSnapshot | ObserveResources _ -> None
    // The supervisor chooses the worker for an open; the address is empty,
    // as composer_open_project sends it.
    | OpenProject project -> Some(RequestBody.Open(workerAddress { WorkerTarget.Host = ""; Epoch = "" }, project))
    | Reserve edit -> Some(RequestBody.Reserve(sessionAddress edit.Target, edit.Label))
    | Build build -> Some(RequestBody.Build(sessionAddress build.Target, build.Reservation))
    | Run run -> Some(RequestBody.Run(sessionAddress run.Target, run.Arguments))
    | Cancel target -> Some(RequestBody.Cancel(sessionAddress target))
    | CloseSession target -> Some(RequestBody.Close(sessionAddress target))
    | RetireWorker worker -> Some(RequestBody.PrepareCompilerChange(workerAddress worker))

  /// The events that answer one command: exactly one Accepted or Refused,
  /// preceded by the transcript when the reply is a run.
  let outcome (correlation: int32) (response: ComposerResponse) : Event list =
    let reply = response.Reply
    let target = targetOf reply.Authority
    let accepted completion =
      Accepted { CommandAcceptance.Correlation = correlation; Target = target; Completion = completion }
    let refused code message =
      Refused { CommandRefusal.Correlation = correlation; Target = target; Code = code; Message = message }
    match reply.Outcome with
    | Result.Error refusal -> [ refused (ComposerClientJson.refusalCode refusal.Code) refusal.Message ]
    | Result.Ok(ReplyBody.Opened _) -> [ accepted SessionOpened ]
    | Result.Ok(ReplyBody.Reserved reserved) -> [ accepted (EditReserved reserved.Reservation) ]
    | Result.Ok(ReplyBody.Built artifact) -> [ accepted (ReservationBuilt(artifactSummary artifact)) ]
    | Result.Ok(ReplyBody.Ran ran) ->
      [ RunOutput {
          RunTranscript.Correlation = correlation
          Target = target
          Generation = ran.Generation
          SourceVersion = ran.SourceVersion
          ExitCode = ran.ExitCode
          StandardOutput = ran.StandardOutput
          StandardError = ran.StandardError
        }
        accepted (RunFinished ran.ExitCode) ]
    | Result.Ok ReplyBody.Canceled -> [ accepted WorkCanceled ]
    | Result.Ok(ReplyBody.Closed _) -> [ accepted SessionClosed ]
    | Result.Ok(ReplyBody.CompilerRetired _) -> [ accepted WorkerRetired ]
    | Result.Ok(ReplyBody.HelloAccepted _ | ReplyBody.Observed _ | ReplyBody.RequestCanceled _ | ReplyBody.Formatted _) ->
      [ refused "contract_mismatch" "Composer answered with a reply this command cannot produce." ]

  /// Protocol.fs spells pressure as the level's name ("Normal", "Tight",
  /// "Critical"); /api/system/status uses MemoryPressure.describe's lower case.
  let pressureName (pressure: Bozzetto.MemoryPressure) =
    match pressure with
    | Bozzetto.MemoryPressure.Normal -> "Normal"
    | Bozzetto.MemoryPressure.Tight -> "Tight"
    | Bozzetto.MemoryPressure.Critical -> "Critical"

  /// The anomaly verdicts that carry evidence, as /api/system/status lists them.
  let alarms (verdicts: Bozzetto.Features.HealthAnomaly.Verdict list) : HealthAlarm array =
    verdicts
    |> List.choose (fun verdict ->
      Bozzetto.Features.HealthAnomaly.evidenceOf verdict
      |> Option.map (fun evidence ->
        { HealthAlarm.Signal = Bozzetto.Features.HealthAnomaly.signalName evidence.Signal
          State = Bozzetto.Features.HealthAnomaly.verdictName verdict
          Message = Bozzetto.Features.HealthAnomaly.describe verdict |> Option.defaultValue "" }))
    |> List.toArray

  /// The DaemonStatusPayload sources, projected. A non-finite reading becomes
  /// 0: the page's decoder accepts only finite numbers.
  let health (sources: HealthSources) : DaemonHealth =
    let finite value = if Double.IsFinite value then value else 0.0
    let observed = sources.Health
    let telemetry = sources.Telemetry
    { DaemonHealth.Version = observed |> Option.map _.Version |> Option.defaultValue sources.Version
      ProcessId = observed |> Option.map _.DaemonPid |> Option.defaultValue sources.ProcessId
      McpPort = observed |> Option.map _.DaemonPort |> Option.defaultValue sources.McpPort
      Overall =
        observed
        |> Option.map (fun h -> Bozzetto.Features.DaemonHealth.healthLabel (Bozzetto.Features.DaemonHealth.overallStatus h))
        |> Option.defaultValue "Unknown"
      MemoryPressure = observed |> Option.map _.MemoryPressure |> Option.defaultValue Bozzetto.MemoryPressure.Normal |> pressureName
      ResidentBytes = observed |> Option.map (fun h -> int64 h.MemoryMB * 1_048_576L) |> Option.defaultValue 0L
      MachineAvailableBytes = sources.Machine.AvailableBytes
      MachineTotalBytes = sources.Machine.TotalBytes
      CpuPercent = telemetry |> Option.map (fun t -> finite t.AggregateCpuPercent) |> Option.defaultValue 0.0
      Processes =
        telemetry
        |> Option.map (fun t ->
          t.Processes
          |> List.map (fun p ->
            { ProcessSample.ProcessId = p.ProcessId; Role = p.Role; ResidentBytes = p.ResidentBytes; CpuPercent = finite p.CpuPercent })
          |> List.toArray)
        |> Option.defaultValue [||]
      Alarms = observed |> Option.map (fun h -> alarms h.Anomalies) |> Option.defaultValue [||] }

  /// A stable, non-secret identity for one grant, derived only from what the
  /// board already shows (a holder holds at most one lease at a time).
  let leaseDisplayId (holder: string) (kind: string) (grantedAt: DateTimeOffset) =
    let identity = holder + "\n" + kind + "\n" + grantedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)
    "lease-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes identity), 0, 4).ToLowerInvariant()

  /// The pool as `ExpensiveWorkLease.snapshot` reports it, which carries no LeaseId.
  let leaseBoard (active: Bozzetto.ExpensiveWorkLease.GrantView list) (queue: Bozzetto.ExpensiveWorkLease.QueueView list) : LeaseBoard =
    { LeaseBoard.Active =
        active
        |> List.map (fun lease ->
          { LeaseGrant.Id = leaseDisplayId lease.Holder lease.Kind lease.GrantedAt
            Kind = lease.Kind
            Holder = lease.Holder
            GrantedAtMs = lease.GrantedAt.ToUnixTimeMilliseconds()
            ExpiresAtMs = lease.ExpiresAt.ToUnixTimeMilliseconds() })
        |> List.toArray
      Queued =
        queue
        |> List.map (fun wait ->
          { LeaseWait.Kind = wait.Kind; Holder = wait.Holder; RequestedAtMs = wait.RequestedAt.ToUnixTimeMilliseconds() })
        |> List.toArray }

/// The welded page and the rules for serving it and upgrading to the bridge.
module Page =

  /// CSP source expressions for every inline <script> or <style> element.
  /// The bundle is one static page, so hashes admit exactly its own code where
  /// 'unsafe-inline' would admit any injected script. Line breaks are
  /// normalized first, as the HTML parser does before the browser hashes.
  let inlineHashes (html: string) (element: string) : string list =
    let opening = "<" + element
    let closing = "</" + element + ">"
    let rec collect (from: int) acc =
      match html.IndexOf(opening, from, StringComparison.OrdinalIgnoreCase) with
      | -1 -> List.rev acc
      | start ->
        match html.IndexOf('>', start) with
        | -1 -> List.rev acc
        | tagEnd ->
          match html.IndexOf(closing, tagEnd + 1, StringComparison.OrdinalIgnoreCase) with
          | -1 -> List.rev acc
          | finish ->
            let body = html.Substring(tagEnd + 1, finish - tagEnd - 1).Replace("\r\n", "\n").Replace('\r', '\n')
            let digest = SHA256.HashData(Encoding.UTF8.GetBytes body)
            collect (finish + closing.Length) (("'sha256-" + Convert.ToBase64String digest + "'") :: acc)
    collect 0 []

  /// A Host value fit to name the page's own WebSocket origin: loopback, and
  /// nothing that could end or extend a CSP directive.
  let isSocketHost (host: string) =
    HttpOriginGuard.isLoopbackHost host
    && host |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '.' || c = ':' || c = '[' || c = ']' || c = '-')

  /// The page needs its own inline module script and styles, one WebSocket
  /// to its own origin and its tab icons from its own origin; nothing else.
  /// ws://host is named as well as 'self' because older engines do not let
  /// 'self' match a ws: URL.
  let contentSecurityPolicy (scripts: string list) (styles: string list) (host: string option) =
    let socket =
      match host with
      | Some value when isSocketHost value -> " ws://" + value
      | _ -> ""
    String.concat "; " [
      "default-src 'none'"
      "script-src " + (match scripts with [] -> "'none'" | hashes -> String.concat " " hashes)
      "style-src " + (match styles with [] -> "'none'" | hashes -> String.concat " " hashes)
      "img-src 'self'"
      "connect-src 'self'" + socket
      "base-uri 'none'"
      "form-action 'none'"
      "frame-ancestors 'none'"
    ]

  /// The origin guard has already refused foreign Origins. Browsers always
  /// send Origin on a WebSocket handshake; a browser-shaped upgrade without
  /// one is refused, so a GET upgrade that carries commands never rides the
  /// guard's allowance for safe same-site GETs.
  let admitUpgrade (origin: string option) (fetchSite: string option) : Result<unit, string> =
    match origin, fetchSite with
    | None, Some site -> Error("A browser WebSocket upgrade (Sec-Fetch-Site: " + site + ") must carry this daemon's Origin.")
    | _ -> Ok()

/// One tab icon, served from memory.
type Icon = { Route: string; ContentType: string; Bytes: byte array }

/// The page's tab icons: the Clef logo, as clef-lang.com links it (an .ico, an
/// SVG, and an SVG for dark color schemes). bozzetto-web/icons holds the
/// files; they are embedded in this assembly, so serving them reads no file.
/// The MCP listener serves all three beside the page, and the control
/// listener serves the .ico that browsers probe on its port.
module Icons =

  let private resource (name: string) : byte array =
    let assembly = Reflection.Assembly.GetExecutingAssembly()
    use stream = assembly.GetManifestResourceStream("ui/" + name)
    if isNull stream then failwithf "The UI icon %s is not embedded in %s." name assembly.FullName
    use copy = new MemoryStream()
    stream.CopyTo copy
    copy.ToArray()

  let private icon route contentType name =
    lazy { Route = route; ContentType = contentType; Bytes = resource name }

  let svg = icon "/favicon.svg" "image/svg+xml" "favicon.svg"
  let darkSvg = icon "/favicon-dark.svg" "image/svg+xml" "favicon-dark.svg"
  let ico = icon "/favicon.ico" "image/x-icon" "favicon.ico"

  let all () = [ svg.Value; darkSvg.Value; ico.Value ]

  /// Write one icon. An SVG opened on its own runs nothing: its policy admits
  /// only its inline styles.
  let write (icon: Icon) (ctx: HttpContext) : Task = task {
    ctx.Response.ContentType <- icon.ContentType
    ctx.Response.ContentLength <- int64 icon.Bytes.Length
    ctx.Response.Headers.CacheControl <- StringValues "public, max-age=86400"
    ctx.Response.Headers.XContentTypeOptions <- StringValues "nosniff"
    ctx.Response.Headers.ContentSecurityPolicy <- StringValues "default-src 'none'; style-src 'unsafe-inline'"
    do! ctx.Response.Body.WriteAsync(icon.Bytes, 0, icon.Bytes.Length, ctx.RequestAborted)
  }

// ── Connections ────────────────────────────────────────────────────────────

/// One encoded frame. A snapshot carries its revision, so the writer never
/// lets a slower read overwrite a newer snapshot already on the page.
type Outbound = { Text: string; Revision: int64 voption }

module Outbound =
  let ofEvent (event: Event) : Outbound =
    { Text = UiBridgeCodec.encodeEvent event
      Revision = match event with Snapshot snapshot -> ValueSome snapshot.Revision | _ -> ValueNone }

/// Pushed state, where only the latest value matters to a slow page. Snapshot
/// is pushed on supervisor changes and Uptime on clock ticks; Health and Leases
/// keep their slots for the owner-driven pushes described at the top of this
/// module.
[<RequireQualifiedAccess>]
type Push =
  | Snapshot
  | Health
  | Leases
  | Uptime
  | Resources
  | AgentWork

[<RequireQualifiedAccess>]
type Inbound =
  | Text of string
  /// A frame that cannot carry a command; refused with correlation 0.
  | Unusable of code: string * message: string
  | Closed

/// The transport under one connection: a WebSocket in the daemon, a fake in
/// the tests. One receiver and one writer use it, never concurrently.
type IBridgeSocket =
  abstract Receive: CancellationToken -> Task<Inbound>
  abstract Send: text: string * cancellation: CancellationToken -> Task
  abstract Close: CancellationToken -> Task
  abstract Abort: unit -> unit

type private Write =
  | Prologue of Outbound list
  | Answer of Outbound list * settle: (unit -> unit)
  | Wake
  | Stop of TaskCompletionSource<unit>

/// The single writer of one socket. Answers keep their order; pushes are
/// latest-wins slots, so a page that reads slowly receives current state
/// instead of a backlog. Posting happens under the same lock that closes the
/// outbox, so nothing is written after CloseAsync, and every answer's settle
/// still runs.
type internal Outbox(socket: IBridgeSocket, sendTimeout: TimeSpan, closeTimeout: TimeSpan) =
  let gate = obj ()
  let pushes: Outbound option array = Array.create 6 None
  let mutable closed = false
  let mutable wakePending = false
  // Owned by the writer agent.
  let mutable primed = false
  let mutable broken = false
  let mutable lastRevision = Int64.MinValue

  let slot (kind: Push) =
    match kind with
    | Push.Snapshot -> 0
    | Push.Health -> 1
    | Push.Leases -> 2
    | Push.Uptime -> 3
    | Push.Resources -> 4
    | Push.AgentWork -> 5

  let send (frame: Outbound) = async {
    return!
      task {
        use timeout = new CancellationTokenSource(sendTimeout)
        try
          do! socket.Send(frame.Text, timeout.Token)
          return true
        with _ -> return false
      }
      |> Async.AwaitTask
  }

  let write (frame: Outbound) = async {
    let regresses =
      match frame.Revision with
      | ValueSome revision -> revision < lastRevision
      | ValueNone -> false
    if not broken && not regresses && not (lock gate (fun () -> closed)) then
      let! sent = send frame
      if sent then
        match frame.Revision with
        | ValueSome revision -> lastRevision <- revision
        | ValueNone -> ()
      else
        // A failed or stalled send ends the connection; aborting also ends
        // the receive loop that owns the close.
        broken <- true
        socket.Abort()
  }

  let drainPushes () = async {
    let pending =
      lock gate (fun () ->
        wakePending <- false
        let taken = Array.copy pushes
        Array.fill pushes 0 pushes.Length None
        taken)
    for frame in pending do
      match frame with
      | Some outbound -> do! write outbound
      | None -> ()
  }

  let close () = async {
    return!
      task {
        use timeout = new CancellationTokenSource(closeTimeout)
        try do! socket.Close timeout.Token
        with _ -> socket.Abort()
      }
      |> Async.AwaitTask
  }

  let agent =
    MailboxProcessor<Write>.Start(fun inbox ->
      let rec loop () = async {
        let! message = inbox.Receive()
        match message with
        | Prologue frames ->
          for frame in frames do
            do! write frame
          primed <- true
          do! drainPushes ()
          return! loop ()
        | Answer(frames, settle) ->
          for frame in frames do
            do! write frame
          try settle () with _ -> ()
          return! loop ()
        | Wake ->
          // Pushes wait for the prologue, so Welcome is always first.
          if primed then do! drainPushes ()
          else lock gate (fun () -> wakePending <- false)
          return! loop ()
        | Stop stopped ->
          if not broken then do! close ()
          stopped.TrySetResult() |> ignore
      }
      loop ())

  do agent.Error.Add(fun error -> Log.warn "[ui-bridge] writer failed: %s" error.Message)

  /// The connection's first frames; pushes are held until they are written.
  member _.Prime(frames: Outbound list) =
    lock gate (fun () -> if not closed then agent.Post(Prologue frames))

  /// One command's frames, written together and in order. `settle` runs once
  /// they are written, or dropped because the connection has closed.
  member _.Answer(frames: Outbound list, settle: unit -> unit) =
    let posted =
      lock gate (fun () ->
        if closed then false
        else
          agent.Post(Answer(frames, settle))
          true)
    if not posted then
      try settle () with _ -> ()

  member _.Push(kind: Push, frame: Outbound) =
    lock gate (fun () ->
      if not closed then
        pushes[slot kind] <- Some frame
        if not wakePending then
          wakePending <- true
          agent.Post Wake)

  /// Stop writing and close the socket. Frames still queued are dropped.
  member _.CloseAsync() : Task = task {
    let stopped = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let first =
      lock gate (fun () ->
        if closed then false
        else
          closed <- true
          agent.Post(Stop stopped)
          true)
    if first then
      try do! stopped.Task.WaitAsync(sendTimeout + closeTimeout)
      with _ -> socket.Abort()
      (agent :> IDisposable).Dispose()
  }

// ── The uptime clock ───────────────────────────────────────────────────────

type private ClockSignal =
  | Attach
  | Detach
  | Tick of run: int64 * uptimeMs: int64
  | Halt

/// The hub's one clock: a clock-owned push, not a poll. It is the owner and
/// only source of Uptime events; each tick pushes the uptime its clock just
/// measured, reading no other state and diffing nothing. It runs only while a
/// page is connected: the first Attach starts it, the Detach of the last page
/// stops it, and with no page connected no clock runs. `next` is the clock
/// itself, a cold Async that waits out one period and returns the uptime then
/// (UptimeClock.system in the daemon, a clock the test holds in the tests); it
/// must wait before it returns. A tick from a run that has since stopped is
/// dropped, so a stopped clock never pushes.
type internal UptimeClock(next: Async<int64>, push: int64 -> unit) =
  let agent =
    MailboxProcessor<ClockSignal>.Start(fun inbox ->
      let rec ticking (run: int64) = async {
        let! uptimeMs = next
        inbox.Post(Tick(run, uptimeMs))
        return! ticking run
      }
      let start (run: int64) (clock: CancellationTokenSource) =
        let guarded = async {
          try return! ticking run
          with error -> Log.warn "[ui-bridge] uptime clock failed: %s" error.Message
        }
        // Runs here up to the clock's first wait, so the clock is waiting
        // before the next signal is read.
        Async.StartImmediate(guarded, clock.Token)
      // A plain source holds no timer and no registration, so cancelling is
      // all a run needs; it is not disposed, because the run it cancels may
      // still be unwinding on another thread.
      let rec loop (pages: int) (current: (int64 * CancellationTokenSource) option) (runs: int64) = async {
        let! signal = inbox.Receive()
        match signal, current with
        | Attach, None ->
          let run = runs + 1L
          let clock = new CancellationTokenSource()
          start run clock
          return! loop (pages + 1) (Some(run, clock)) run
        | Attach, Some _ -> return! loop (pages + 1) current runs
        | Detach, Some(_, clock) when pages <= 1 ->
          clock.Cancel()
          return! loop 0 None runs
        | Detach, _ -> return! loop (max 0 (pages - 1)) current runs
        | Tick(run, uptimeMs), Some(live, _) when run = live ->
          try push uptimeMs
          with error -> Log.warn "[ui-bridge] uptime push failed: %s" error.Message
          return! loop pages current runs
        | Tick _, _ -> return! loop pages current runs
        | Halt, _ -> current |> Option.iter (fun (_, clock) -> clock.Cancel())
      }
      loop 0 None 0L)

  do agent.Error.Add(fun error -> Log.warn "[ui-bridge] uptime clock agent failed: %s" error.Message)

  /// A page connected; the first one starts the clock.
  member _.Attach() = agent.Post Attach
  /// A page closed; the last one stops the clock.
  member _.Detach() = agent.Post Detach
  /// The daemon is stopping: stop the clock for good.
  member _.Halt() = agent.Post Halt

module UptimeClock =
  /// The daemon's clock: waits for the next whole second of uptime, then
  /// reports it. Uptime is measured on the monotonic tick count from an origin
  /// fixed once from the process start time, so a wall-clock step never moves
  /// it backwards.
  let system (startedAtMs: int64) : Async<int64> =
    let origin = Environment.TickCount64 - max 0L (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAtMs)
    let uptime () = Environment.TickCount64 - origin
    async {
      do! Async.Sleep(int (1000L - uptime () % 1000L))
      return uptime ()
    }

// ── The hub ────────────────────────────────────────────────────────────────

/// What the bridge reads and runs. The daemon builds it from its one
/// ComposerSupervisor and its own watches; tests supply fakes.
type BridgeSources = {
  DaemonVersion: string
  /// When the daemon process started (Unix epoch ms). Welcome carries it once
  /// per connection; the page shows it as a fixed time, never a running clock.
  StartedAtMs: int64
  /// Runs one Composer operation, exactly as the HTTP and MCP adapters do.
  Execute: RequestBody -> CancellationToken -> Task<ComposerResponse>
  /// The directory refreshed through the worker; answers RequestSnapshot.
  Directory: CancellationToken -> Task<ComposerDirectory>
  /// The directory as last observed. Connects and change pushes read this, so
  /// observing a change never causes worker traffic or another change.
  CachedDirectory: unit -> ComposerDirectory
  WorkerPid: unit -> int option
  /// The supervisor's own change signal. Each firing may push one Snapshot.
  Changed: IObservable<unit>
  /// Read on connect and for RequestSnapshot only, never on a schedule.
  Health: unit -> DaemonHealth
  /// Read on connect, explicit resync and lease-owner changes only.
  Leases: unit -> LeaseBoard
  LeaseChanged: IObservable<unit> option
  AgentWork: (unit -> AgentWorkBoard) option
  AgentWorkChanged: IObservable<unit> option
}

type BridgeOptions = {
  /// Minimum spacing of change-driven snapshot pushes. The first change after
  /// a quiet spell is pushed at once; changes inside the spacing collapse into
  /// one trailing push. With no change, nothing is read or sent.
  SnapshotInterval: TimeSpan
  MaxFrameBytes: int
  /// Commands one connection may have in flight before its receive pauses.
  MaxInFlight: int
  SendTimeout: TimeSpan
  CloseTimeout: TimeSpan
}

module BridgeOptions =
  let defaults = {
    SnapshotInterval = TimeSpan.FromMilliseconds 250.
    MaxFrameBytes = 1024 * 1024
    MaxInFlight = 32
    SendTimeout = TimeSpan.FromSeconds 30.
    CloseTimeout = TimeSpan.FromSeconds 5.
  }

let private attempt (label: string) (read: unit -> 'a) : 'a option =
  try Some(read ())
  with error ->
    Log.warn "[ui-bridge] reading %s failed: %s" label error.Message
    None

/// Await a task as a value: a fault or a cancellation is a Result, never an
/// escape from the async that answers a command.
let private settled (start: unit -> Task<'a>) : Async<Result<'a, string>> = async {
  return!
    task {
      try
        let! value = start ()
        return Ok value
      with error -> return Error error.Message
    }
    |> Async.AwaitTask
}

let private receive (socket: IBridgeSocket) (cancellation: CancellationToken) : Task<Inbound> = task {
  try return! socket.Receive cancellation
  with _ -> return Inbound.Closed
}

let private admit (inflight: SemaphoreSlim) (cancellation: CancellationToken) : Task<bool> = task {
  try
    do! inflight.WaitAsync cancellation
    return true
  with _ -> return false
}

/// Every connection of one daemon. `stopping` ends the pushes and is the only
/// token Composer operations see: a page closing never cancels a build or a
/// run (the Cancel command does). `pause` spaces snapshot pushes; the daemon
/// sleeps for the interval, and tests hold and release it to step the
/// coalescing deterministically. `uptime` is the uptime clock's tick (see
/// UptimeClock): UptimeClock.system in the daemon, held by the tests.
type Hub(sources: BridgeSources, options: BridgeOptions, stopping: CancellationToken, pause: TimeSpan -> Async<unit>, uptime: Async<int64>) =
  let resources = ResourceMonitor.create ()
  let connections = ConcurrentDictionary<int64, Outbox>()
  let changes = Channel.CreateBounded<unit>(BoundedChannelOptions(1, FullMode = BoundedChannelFullMode.DropOldest))
  let mutable nextConnection = 0L

  let snapshotEvent (directory: ComposerDirectory) = Snapshot(Project.snapshot directory (sources.WorkerPid ()))

  let stateEvents (directory: unit -> ComposerDirectory) =
    [ attempt "snapshot" (fun () -> snapshotEvent (directory ()))
      attempt "health" sources.Health |> Option.map Health
      attempt "leases" sources.Leases |> Option.map Leases
      sources.AgentWork |> Option.bind (attempt "agent work") |> Option.map AgentWork ]
    |> List.choose id

  let broadcast (kind: Push) (event: Event) =
    let frame = Outbound.ofEvent event
    for outbox in connections.Values do
      outbox.Push(kind, frame)

  let clock = UptimeClock(uptime, fun uptimeMs -> broadcast Push.Uptime (Uptime { UptimeTick.UptimeMs = uptimeMs }))

  let execute (frame: CommandFrame) (connection: CancellationToken) : Async<Outbound list> = async {
    let refuse message =
      [ Refused(UiBridgeCodec.frameRefusal frame.Correlation "provider_unavailable" message) ]
    match Project.toRequest frame.Command with
    | Some request ->
      let! response = settled (fun () -> sources.Execute request stopping)
      match response with
      | Ok reply -> return Project.outcome frame.Correlation reply |> List.map Outbound.ofEvent
      | Error message -> return refuse message |> List.map Outbound.ofEvent
    | None ->
      let! directory = settled (fun () -> sources.Directory connection)
      match directory with
      | Ok fresh ->
        let done' =
          Accepted { CommandAcceptance.Correlation = frame.Correlation; Target = UiBridgeCodec.noTarget; Completion = SnapshotSent }
        return stateEvents (fun () -> fresh) @ [ done' ] |> List.map Outbound.ofEvent
      | Error message -> return refuse message |> List.map Outbound.ofEvent
  }

  /// Waits on the supervisor's Changed signal, never on a clock: each wakeup
  /// is a change. The pause after a push only spaces the next one.
  let rec snapshotPushes () = async {
    let! available = changes.Reader.WaitToReadAsync(stopping).AsTask() |> Async.AwaitTask
    if available then
      changes.Reader.TryRead() |> ignore
      if not connections.IsEmpty then
        attempt "snapshot" (fun () -> snapshotEvent (sources.CachedDirectory ())) |> Option.iter (broadcast Push.Snapshot)
      do! pause options.SnapshotInterval
      return! snapshotPushes ()
  }

  do stopping.Register(fun () ->
    clock.Halt()
    Async.Start(resources.Close())) |> ignore

  new(sources, options, stopping) =
    Hub(
      sources,
      options,
      stopping,
      (fun interval -> Async.Sleep(int interval.TotalMilliseconds)),
      UptimeClock.system sources.StartedAtMs
    )

  member _.Options = options
  member _.ConnectionCount = connections.Count
  member _.Resources = resources.Current

  /// Begin the snapshot pushes. Status reads never raise Changed, so a push
  /// cannot feed back into another one.
  member _.Start() =
    let subscription = sources.Changed.Subscribe(fun () -> changes.Writer.TryWrite(()) |> ignore)
    let leaseSubscription = sources.LeaseChanged |> Option.map (fun changed ->
      changed.Subscribe(fun () ->
        if not connections.IsEmpty then
          attempt "leases" sources.Leases |> Option.iter (fun board -> broadcast Push.Leases (Leases board))))
    let workSubscription = sources.AgentWorkChanged |> Option.map (fun changed ->
      changed.Subscribe(fun () ->
        if not connections.IsEmpty then
          sources.AgentWork |> Option.bind (attempt "agent work") |> Option.iter (fun board -> broadcast Push.AgentWork (AgentWork board))))
    stopping.Register(fun () ->
      subscription.Dispose()
      leaseSubscription |> Option.iter _.Dispose()
      workSubscription |> Option.iter _.Dispose()) |> ignore
    Async.Start(snapshotPushes (), stopping)

  /// Serve one connection until the page or the daemon closes it.
  member _.RunAsync(socket: IBridgeSocket, aborted: CancellationToken) : Task = task {
    use lifetime = CancellationTokenSource.CreateLinkedTokenSource(aborted, stopping)
    let outbox = new Outbox(socket, options.SendTimeout, options.CloseTimeout)
    let inflight = new SemaphoreSlim(options.MaxInFlight)
    let release () = try inflight.Release() |> ignore with _ -> ()
    let id = Interlocked.Increment &nextConnection
    connections[id] <- outbox
    // Uptime ticks reach this page through its outbox, after the prologue.
    clock.Attach()
    try
      try
        let welcome =
          Welcome {
            BridgeWelcome.ProtocolVersion = ProtocolVersion
            DaemonVersion = sources.DaemonVersion
            StartedAtMs = sources.StartedAtMs
          }
        outbox.Prime(welcome :: stateEvents sources.CachedDirectory |> List.map Outbound.ofEvent)
        let mutable reading = true
        while reading do
          let! inbound = receive socket lifetime.Token
          match inbound with
          | Inbound.Closed -> reading <- false
          | Inbound.Unusable(code, message) ->
            outbox.Answer([ Outbound.ofEvent (Refused(UiBridgeCodec.frameRefusal 0 code message)) ], ignore)
          | Inbound.Text payload ->
            match UiBridgeCodec.decodeCommand payload with
            | Error refusal -> outbox.Answer([ Outbound.ofEvent (Refused refusal) ], ignore)
            | Ok frame ->
              // Commands run concurrently; a build that takes minutes does not
              // hold up the commands behind it.
              let! admitted = admit inflight lifetime.Token
              if admitted then
                let work = async {
                  match frame.Command with
                  | ObserveResources _ when not (OperatingSystem.IsLinux()) ->
                    outbox.Answer([ Outbound.ofEvent(Refused(UiBridgeCodec.frameRefusal frame.Correlation "unsupported" "Host resource acquisition is Linux-only in this build.")) ], release)
                  | ObserveResources seconds ->
                    if seconds = 0 then do! resources.Remove id
                    else
                      do! resources.Observe(id, int seconds, fun value -> outbox.Push(Push.Resources, Outbound.ofEvent(Resources value)))
                    outbox.Answer([ Outbound.ofEvent(Accepted { Correlation = frame.Correlation; Target = UiBridgeCodec.noTarget; Completion = SnapshotSent }) ], release)
                  | _ ->
                    let! frames = execute frame lifetime.Token
                    outbox.Answer(frames, release)
                }
                Async.Start(work, CancellationToken.None)
              else reading <- false
      with error -> Log.warn "[ui-bridge] connection %d failed: %s" id error.Message
    finally
      connections.TryRemove(id) |> ignore
      clock.Detach()
      // Ends any RequestSnapshot read still running for this page; Composer
      // operations hold the daemon's token, not this one.
      lifetime.Cancel()
    do! resources.Remove id |> Async.StartAsTask
    do! outbox.CloseAsync()
  }

// ── The daemon's bridge ────────────────────────────────────────────────────

module BridgeSources =
  let ofSupervisor (supervisor: ComposerSupervisor) (version: string) (startedAtMs: int64) (health: unit -> DaemonHealth) (leases: unit -> LeaseBoard) =
    { DaemonVersion = version
      StartedAtMs = startedAtMs
      Execute = fun request cancellation -> supervisor.ExecuteAsync(request, cancellation)
      Directory = fun cancellation -> supervisor.SessionsAsync cancellation
      CachedDirectory = supervisor.CachedDirectory
      WorkerPid = fun () -> supervisor.WorkerPid
      Changed = supervisor.Changed :> IObservable<unit>
      Health = health
      Leases = leases
      LeaseChanged = None
      AgentWork = None
      AgentWorkChanged = None }

let readHealth (mcpPort: int) (getHealth: unit -> Bozzetto.Features.HealthSnapshot option) () : DaemonHealth =
  Project.health {
    HealthSources.McpPort = mcpPort
    Version = DaemonInfo.version
    ProcessId = Environment.ProcessId
    Health = getHealth ()
    Telemetry = DaemonTelemetry.current ()
    Machine = Bozzetto.Features.MachineMemory.current ()
  }

let readLeases () : LeaseBoard =
  let pool = Bozzetto.Features.LeaseWatch.snapshot ()
  Project.leaseBoard pool.Active pool.Queue

/// When this daemon process started, as the operating system recorded it.
let private processStartedAtMs () : int64 =
  use self = Process.GetCurrentProcess()
  DateTimeOffset(self.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds()

/// The bridge of one daemon, pushing until `stopping`.
let create (supervisor: ComposerSupervisor) (mcpPort: int) (getHealth: unit -> Bozzetto.Features.HealthSnapshot option) (stopping: CancellationToken) =
  let sources =
    { BridgeSources.ofSupervisor supervisor DaemonInfo.version (processStartedAtMs ()) (readHealth mcpPort getHealth) readLeases with
        LeaseChanged = Some(Bozzetto.Features.LeaseWatch.changed :> IObservable<unit>) }
  let hub = Hub(sources, BridgeOptions.defaults, stopping)
  hub.Start()
  hub

/// Production cohort-backed bridge; retained create remains for compatibility callers.
let createWithAgentWork supervisor mcpPort getHealth stopping (owner: Bozzetto.Features.CohortOwner.Handle option) =
  let sources =
    { BridgeSources.ofSupervisor supervisor DaemonInfo.version (processStartedAtMs()) (readHealth mcpPort getHealth) readLeases with
        LeaseChanged = Some(Bozzetto.Features.LeaseWatch.changed :> IObservable<unit>)
        AgentWork = owner |> Option.map (fun o -> fun () -> Project.agentWork (o.ReadWork()))
        AgentWorkChanged = owner |> Option.map _.WorkChanged }
  let hub = Hub(sources, BridgeOptions.defaults, stopping)
  hub.Start()
  hub

/// A WebSocket as the bridge's transport: UTF-8 text frames up to a size cap.
type private WebSocketTransport(socket: WebSocket, maxFrameBytes: int) =
  let strict = UTF8Encoding(false, true)
  // One receiver per socket, so one buffer serves every frame.
  let buffer = Array.zeroCreate<byte> 16384
  interface IBridgeSocket with
    member _.Receive(cancellation) = task {
      use content = new MemoryStream()
      let mutable oversized = false
      let mutable inbound = ValueNone
      while inbound.IsNone do
        let! received = socket.ReceiveAsync(Memory<byte>(buffer), cancellation)
        if received.MessageType = WebSocketMessageType.Close then inbound <- ValueSome Inbound.Closed
        else
          if not oversized then
            if content.Length + int64 received.Count > int64 maxFrameBytes then
              oversized <- true
              content.SetLength 0L
            else content.Write(buffer, 0, received.Count)
          if received.EndOfMessage then
            inbound <-
              ValueSome(
                if oversized then
                  Inbound.Unusable("frame_too_large", sprintf "frame: exceeds %d bytes." maxFrameBytes)
                elif received.MessageType = WebSocketMessageType.Binary then
                  Inbound.Unusable("invalid_request", "frame: binary frames are not supported yet; send one JSON text frame.")
                else
                  try Inbound.Text(strict.GetString(content.GetBuffer(), 0, int content.Length))
                  with :? DecoderFallbackException -> Inbound.Unusable("invalid_request", "frame: not valid UTF-8"))
      return inbound.Value
    }
    member _.Send(text, cancellation) =
      socket.SendAsync(ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes text), WebSocketMessageType.Text, true, cancellation).AsTask()
    member _.Close(cancellation) =
      match socket.State with
      | WebSocketState.Open
      | WebSocketState.CloseReceived -> socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cancellation)
      | _ -> Task.CompletedTask
    member _.Abort() = socket.Abort()

let private page: string = WebAssets.IndexHtml

let private pageHashes = lazy (Page.inlineHashes page "script", Page.inlineHashes page "style")

let private header (ctx: HttpContext) (name: string) =
  match ctx.Request.Headers.TryGetValue name with
  | true, values when not (String.IsNullOrWhiteSpace(string values)) -> Some(string values)
  | _ -> None

/// GET /dashboard and /composer serve one welded bundle with route-selected
/// layouts (resources/leases and compiler operations), GET /favicon.*
/// its tab icons, and GET /ui/bridge upgrades to the bridge. All sit behind
/// the daemon's origin guard.
let mapRoutes (app: WebApplication) (hub: Hub) =
  for icon in Icons.all () do
    app.MapGet(icon.Route, RequestDelegate(fun ctx -> Icons.write icon ctx)) |> ignore

  let servePage = RequestDelegate(fun (ctx: HttpContext) -> task {
    let scripts, styles = pageHashes.Value
    let host = if ctx.Request.Host.HasValue then Some(string ctx.Request.Host) else None
    ctx.Response.ContentType <- "text/html; charset=utf-8"
    ctx.Response.Headers.CacheControl <- StringValues "no-store"
    ctx.Response.Headers.ContentSecurityPolicy <- StringValues(Page.contentSecurityPolicy scripts styles host)
    ctx.Response.Headers.XContentTypeOptions <- StringValues "nosniff"
    ctx.Response.Headers["Referrer-Policy"] <- StringValues "no-referrer"
    do! ctx.Response.WriteAsync(page, ctx.RequestAborted)
  })
  // Both browser entry points share a bundle, theme and security policy.
  // The frontend selects its layout from the route; no second bridge owner.
  for route in [ "/dashboard"; "/composer" ] do
    app.MapGet(route, servePage) |> ignore

  // Passive latest-observation API; reading it never starts acquisition.
  app.MapGet("/api/resources", RequestDelegate(fun ctx -> task {
    ctx.Response.ContentType <- "application/json"
    ctx.Response.Headers.CacheControl <- StringValues "no-store"
    do! ctx.Response.WriteAsync(UiBridgeCodec.encodeEvent(Resources hub.Resources), ctx.RequestAborted)
  })) |> ignore

  app.MapGet("/ui/bridge", RequestDelegate(fun (ctx: HttpContext) -> task {
    if not ctx.WebSockets.IsWebSocketRequest then
      ctx.Response.StatusCode <- 400
      do! ctx.Response.WriteAsync("The UI bridge accepts WebSocket upgrades only.", ctx.RequestAborted)
    else
      match Page.admitUpgrade (header ctx "Origin") (header ctx "Sec-Fetch-Site") with
      | Error reason ->
        ctx.Response.StatusCode <- 403
        do! ctx.Response.WriteAsync(reason, ctx.RequestAborted)
      | Ok() ->
        use! socket = ctx.WebSockets.AcceptWebSocketAsync()
        do! hub.RunAsync(WebSocketTransport(socket, hub.Options.MaxFrameBytes), ctx.RequestAborted)
  })) |> ignore
