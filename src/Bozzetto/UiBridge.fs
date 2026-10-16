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
/// supervisor's Changed signal fires (coalesced). Health, Leases and the agent
/// board are the daemon's own facts: their owners publish them and the fact
/// plane below pushes each as it settles, with the current values on connect.
/// The hub's uptime clock runs only while a page is connected and owns Uptime
/// pushes. None of these subscriptions scans the host or admits compiler work.
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
  /// The memory level that reading implies, as the level's owner settled it.
  Level: Bozzetto.MemoryPressure
  /// When the machine was read and when the record was taken (Unix epoch ms).
  SampledAtMs: int64
  RecordAtMs: int64
}

/// Pure projections from Composer's typed replies and the daemon's own
/// observations onto the bridge vocabulary.
module Project =

  /// Who is here, from facts the daemon owns: every lease held or awaited, and
  /// every present cohort member as the cohort's frame lists them (identity,
  /// role, seat, last renewal). A lease holder is spelled `agent · project` by
  /// the lease script; a member's name and project come from its latest report
  /// when it has made one, else the member's connection identity.
  let presence
      (active: Bozzetto.ExpensiveWorkLease.GrantView list)
      (queue: Bozzetto.ExpensiveWorkLease.QueueView list)
      (members: (Bozzetto.MemberTable.MemberId * Bozzetto.Cohort.JoinableRole * Bozzetto.Cohort.SeatState * DateTime) list)
      (runs: Map<string, Bozzetto.AgentWork.Run>) : AgentPresence array =
    let split (holder: string) =
      match holder.IndexOf(" · ", StringComparison.Ordinal) with
      | -1 -> holder, ""
      | at -> holder.Substring(0, at), holder.Substring(at + 3)
    let ms (at: DateTimeOffset) = at.ToUnixTimeMilliseconds()
    let leases =
      [| for lease in active do
           let name, project = split lease.Holder
           { AgentPresence.Name = name; Source = "lease"; Member = lease.Holder; Project = project; Detail = "holds " + lease.Kind; SinceMs = ms lease.GrantedAt }
         for ask in queue do
           let name, project = split ask.Holder
           { AgentPresence.Name = name; Source = "lease"; Member = ask.Holder; Project = project; Detail = "queued for " + ask.Kind; SinceMs = ms ask.RequestedAt } |]
    let latestRun (id: Bozzetto.MemberTable.MemberId) =
      runs |> Map.toSeq |> Seq.map snd |> Seq.filter (fun run -> run.Member = id)
      |> Seq.sortByDescending (fun run -> run.UpdatedAt, run.Report.Sequence) |> Seq.tryHead
    let membersHere =
      [| for id, role, seat, renewal in members do
           match seat with
           | Bozzetto.Cohort.SeatState.Departed _ -> ()
           | Bozzetto.Cohort.SeatState.Present ->
             let run = latestRun id
             { AgentPresence.Name = run |> Option.map (fun r -> r.Report.Name) |> Option.defaultValue (Bozzetto.MemberTable.MemberId.display id)
               Source = "member"
               Member = Bozzetto.MemberTable.MemberId.display id
               Project = run |> Option.map (fun r -> r.Report.Project) |> Option.defaultValue ""
               Detail =
                 string role + " member"
                 + (run |> Option.map (fun r -> " · last report " + r.Report.Status) |> Option.defaultValue " · no report yet")
               SinceMs = DateTimeOffset(DateTime.SpecifyKind(renewal, DateTimeKind.Utc)).ToUnixTimeMilliseconds() } |]
    Array.append leases membersHere

  let private agentWorkRuns (state: Bozzetto.AgentWork.State) : AgentRun array =
    state.Runs |> Map.toArray |> Array.map (fun (key, run) ->
      let r = run.Report
      { Id = key; Member = Bozzetto.MemberTable.MemberId.display run.Member; Role = string run.Role
        Name = r.Name; Session = r.Session; ReportSequence = r.Sequence; Model = r.Model; Endpoint = r.Endpoint
        Project = r.Project; Focus = r.Focus; Operation = r.Operation; Status = r.Status
        UpdatedAtMs = DateTimeOffset(run.UpdatedAt).ToUnixTimeMilliseconds()
        Activity = List.toArray run.Activity; OmittedActivity = run.OmittedActivity
        Usage = r.Usage |> Option.map (fun u -> { Input = u.Input; Output = u.Output; CacheRead = u.CacheRead; CacheWrite = u.CacheWrite; Total = u.Total; EstimateUsd = u.EstimateUsd }) })

  /// The board a page receives: the reports with their presence, sequenced by
  /// the publication that produced it, not by the owner's report count,
  /// because presence moves without a report.
  let agentBoard (revision: int64) (state: Bozzetto.AgentWork.State) (presence: AgentPresence array) : AgentWorkBoard =
    { Incarnation = state.Incarnation; Sequence = revision; ExecutionHost = Environment.MachineName; Capacity = 256
      Presence = presence
      Runs = agentWorkRuns state }

  /// The reports alone, sequenced by the owner's own count: for a reader that
  /// has no presence facts to add.
  let agentWork (state: Bozzetto.AgentWork.State) : AgentWorkBoard =
    { Incarnation = state.Incarnation; Sequence = state.Sequence; ExecutionHost = Environment.MachineName; Capacity = 256
      Presence = [||]
      Runs = agentWorkRuns state }

  /// Compiler operations and leased process observations as a page shows them,
  /// stamped by the publication that produced it. The caller travels as the
  /// adapter's label, which is all it is.
  let operations (revision: int64) (journal: ComposerJournal.Journal) : OperationsBoard =
    { OperationsBoard.Revision = revision
      Incarnation = journal.Incarnation
      StorageError = journal.StorageError
      Entries =
        journal.Entries
        |> List.map (fun entry ->
          let ok, code, message =
            match entry.Outcome with
            | ComposerJournal.Outcome.Running -> false, "running", ""
            | ComposerJournal.Outcome.Succeeded -> true, "", ""
            | ComposerJournal.Outcome.Refused(code, message) -> false, ComposerClientJson.refusalCode code, message
            | ComposerJournal.Outcome.Failed message -> false, "exception", message
          let operation, session, lease =
            match entry.Context with
            | ComposerJournal.Context.Composer(operation, session) -> ComposerClientJson.operationName operation, session, None
            | ComposerJournal.Context.LeasedCommand command ->
              let kind = match command.Kind with ComposerJournal.LeasedKind.Build -> "full_build" | ComposerJournal.LeasedKind.Test -> "test_suite_run"
              kind, "", Some { LeasedCommandInfo.Kind = kind; LeaseId = command.LeaseDisplayId; RunId = command.RunId
                               Executable = command.Executable; Arguments = List.toArray command.Arguments
                               ProcessId = command.ProcessId; StartedAtMs = command.StartedAtMs }
          { OperationEntry.Sequence = entry.Sequence; AtMs = entry.AtMs; CallerLabel = entry.Caller
            Operation = operation; Session = session; Project = entry.Project; Lease = lease
            Ok = ok; Code = code; Message = message
            Run =
              entry.Run |> Option.map (fun run ->
                { OperationRun.ExitCode = run.ExitCode
                  Compiler = run.Compiler |> Option.map (fun c -> { OperationCompiler.Generation = c.Generation; SourceVersion = c.SourceVersion })
                  StandardOutput = run.StandardOutput; StandardError = run.StandardError; Dropped = run.Dropped }) })
        |> List.toArray }

  let transcript (read: ComposerJournal.TranscriptRequest -> Result<ComposerJournal.TranscriptPage, ComposerJournal.TranscriptRefusal>) (request: TranscriptReadRequest) =
    let stream =
      match request.Stream with
      | Stdout -> ComposerJournal.TranscriptStream.Stdout
      | Stderr -> ComposerJournal.TranscriptStream.Stderr
    match read { Incarnation = request.Journal; Sequence = request.Sequence; Stream = stream; Offset = request.Offset; MaxBytes = request.MaxBytes } with
    | Ok page ->
      TranscriptPage { Journal = page.Incarnation; Sequence = page.Sequence; Stream = request.Stream; Offset = page.Offset
                       NextOffset = page.NextOffset; TotalBytes = page.TotalBytes; Bytes = page.Bytes; Truncated = page.Truncated }
    | Error refusal ->
      let code, message =
        match refusal with
        | ComposerJournal.TranscriptRefusal.ForeignIncarnation -> "journal_mismatch", "This transcript belongs to a different journal."
        | ComposerJournal.TranscriptRefusal.MissingEntry -> "entry_missing", "This operation is no longer retained."
        | ComposerJournal.TranscriptRefusal.NoTranscript -> "no_transcript", "This operation has no retained transcript."
        | ComposerJournal.TranscriptRefusal.InvalidCursor -> "invalid_cursor", "The transcript cursor is outside the retained stream."
        | ComposerJournal.TranscriptRefusal.InvalidLimit -> "invalid_limit", "The requested transcript page is outside the byte limit."
        | ComposerJournal.TranscriptRefusal.StorageFailure _ -> "storage_unavailable", "The node could not read its operation store."
      TranscriptRefused(code, message)

  /// Consolidated work carries outcomes and references. Omission from this
  /// projection is distinct from eviction in the source journal.
  let workSummary (value: WorkSnapshot) =
    let active entry =
      entry.Code = "running"
      || (entry.Lease |> Option.exists (fun command -> value.Leases.Active |> Array.exists (fun lease -> lease.Id = command.LeaseId)))
    let recent =
      value.Operations.Entries |> Array.filter (active >> not) |> Array.truncate 20
      |> Array.map _.Sequence |> Set.ofArray
    let entries =
      value.Operations.Entries |> Array.filter (fun entry -> active entry || Set.contains entry.Sequence recent)
      |> Array.map (fun entry ->
        let run = entry.Run |> Option.map (fun run -> { run with StandardOutput = ""; StandardError = "" })
        { entry with Run = run })
    let rec fit entries =
      let summary = { value with Operations = { value.Operations with Entries = entries }; TranscriptsOmitted = true }
      let fits =
        try Bozzetto.Web.Shared.BinaryCodec.encodePeerFrame(PeerWork summary).Length <= MaxWorkSnapshotBytes
        with :? InvalidOperationException -> false
      if fits then summary
      else
        match entries |> Array.tryFindIndexBack (active >> not) with
        | Some oldest -> entries |> Array.indexed |> Array.choose (fun (index, entry) -> if index = oldest then None else Some entry) |> fit
        // Current work is never hidden to make a history projection fit.
        // PublishWork retains its loud refusal when this core alone is oversized.
        | None -> summary
    fit entries

  let targetOf (authority: Authority) : SessionTarget =
    { SessionTarget.Worker = { WorkerTarget.Host = authority.Host; Epoch = authority.Epoch }; Session = authority.Session }

  /// One row per region identity named anywhere in the accepted generation, in
  /// identity order: the witness state Alex reported and the object state the
  /// back end reported, with the nodes Alex visited for it.
  let artifactSummary (artifact: AcceptedArtifact) : ArtifactSummary =
    let visits = artifact.WitnessVisits |> Map.ofArray
    let stateOf (first: string array) (second: string array) (third: string array) (labels: string * string * string) identity =
      let a, b, c = labels
      if Array.contains identity first then a
      elif Array.contains identity second then b
      elif Array.contains identity third then c
      else ""
    let identities =
      Seq.concat [ artifact.ChangedWitnesses; artifact.RetainedWitnesses; artifact.RetiredWitnesses
                   artifact.WitnessVisits |> Array.map fst
                   artifact.CompiledObjects; artifact.ReusedObjects; artifact.RetiredObjects ]
      |> Seq.distinct |> Seq.sort |> Seq.toArray
    { ArtifactSummary.Generation = artifact.Generation
      SourceVersion = artifact.SourceVersion
      ArtifactPath = artifact.ArtifactPath
      ArtifactSha256 = artifact.ArtifactSha256
      Regions =
        identities |> Array.map (fun identity ->
          { RegionRow.Identity = identity
            Witness = stateOf artifact.ChangedWitnesses artifact.RetainedWitnesses artifact.RetiredWitnesses ("changed", "retained", "retired") identity
            Visits = visits |> Map.tryFind identity |> Option.defaultValue 0
            Object = stateOf artifact.CompiledObjects artifact.ReusedObjects artifact.RetiredObjects ("compiled", "reused", "retired") identity }) }

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
    | RequestSnapshot | ObserveResources _ | ObserveNodeResources _ | ReadNodeTranscript _ -> None
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
    | Result.Ok(ReplyBody.HelloAccepted _ | ReplyBody.Observed _ | ReplyBody.RequestCanceled _ | ReplyBody.Formatted _ | ReplyBody.InputsRevoked _) ->
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
    // The level is the level owner's, settled from the same reading as the
    // machine figures; a record's own copy may be older.
    let observed = sources.Health |> Option.map (fun h -> { h with MemoryPressure = sources.Level })
    let telemetry = sources.Telemetry
    { DaemonHealth.Revision = 0L
      Version = observed |> Option.map _.Version |> Option.defaultValue sources.Version
      ProcessId = observed |> Option.map _.DaemonPid |> Option.defaultValue sources.ProcessId
      McpPort = observed |> Option.map _.DaemonPort |> Option.defaultValue sources.McpPort
      Overall =
        observed
        |> Option.map (fun h -> Bozzetto.Features.DaemonHealth.healthLabel (Bozzetto.Features.DaemonHealth.overallStatus h))
        |> Option.defaultValue "Unknown"
      MemoryPressure = pressureName sources.Level
      MemoryPressureNote =
        let level = sources.Level
        let availableFrac =
          if sources.Machine.TotalBytes > 0L then float sources.Machine.AvailableBytes / float sources.Machine.TotalBytes else 0.0
        let thresholds = Bozzetto.MemorySupervisor.pressureThresholds Bozzetto.MemorySupervisor.defaultThresholds
        Bozzetto.MemoryPressure.explainAt thresholds availableFrac level
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
      Alarms = observed |> Option.map (fun h -> alarms h.Anomalies) |> Option.defaultValue [||]
      SampledAtMs = sources.SampledAtMs
      RecordAtMs = sources.RecordAtMs }

  /// A stable, non-secret identity for one grant, derived only from what the
  /// board already shows (a holder holds at most one lease at a time).
  let leaseDisplayId (holder: string) (kind: string) (grantedAt: DateTimeOffset) =
    let identity = holder + "\n" + kind + "\n" + grantedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)
    "lease-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes identity), 0, 4).ToLowerInvariant()

  /// The pool as `ExpensiveWorkLease.snapshot` reports it, which carries no
  /// LeaseId, stamped by the publication that produced it.
  let leaseBoard (revision: int64) (active: Bozzetto.ExpensiveWorkLease.GrantView list) (queue: Bozzetto.ExpensiveWorkLease.QueueView list) : LeaseBoard =
    { LeaseBoard.Revision = revision
      Active =
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
type Outbound = { Bytes: byte array; Revision: int64 voption }

module Outbound =
  let ofEvent (event: Event) : Outbound =
    { Bytes = UiBridgeCodec.encodeEvent event
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
  | Operations
  | WorkNodes
  | NodeResources

[<RequireQualifiedAccess>]
type Inbound =
  | Binary of byte array
  /// A frame that cannot carry a command; refused with correlation 0.
  | Unusable of code: string * message: string
  | Closed

/// The transport under one connection: a WebSocket in the daemon, a fake in
/// the tests. One receiver and one writer use it, never concurrently.
type IBridgeSocket =
  abstract Receive: CancellationToken -> Task<Inbound>
  abstract Send: bytes: byte array * cancellation: CancellationToken -> Task
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
  let pushes: Outbound option array = Array.create 9 None
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
    | Push.Operations -> 6
    | Push.WorkNodes -> 7
    | Push.NodeResources -> 8

  let send (frame: Outbound) = async {
    return!
      task {
        use timeout = new CancellationTokenSource(sendTimeout)
        try
          do! socket.Send(frame.Bytes, timeout.Token)
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

// ── The fact plane ─────────────────────────────────────────────────────────

/// The daemon's own facts as a page receives them. Each fact has one owner,
/// which publishes a complete immutable value with its own serial; the plane
/// admits publications in serial order and projects them onto the wire with
/// pure works on the shared incremental core (Fidelity.FSharp.Incremental, the
/// foundation ResourceGraph and the agent publication already stand on). A
/// page registers a sink and receives the current values in the same critical
/// section; every later publication reaches it through the sink, latest-wins
/// per fact. Pages hold no demand: the projections are demanded for the
/// daemon's lifetime, so a connecting page is answered at once.
type BridgeFacts = {
  /// Registers a page's sink and returns the current values in the same
  /// critical section, so nothing settled between the two is lost or doubled.
  Register: (Push -> Event -> unit) -> Event list * IDisposable
  /// The current values, for an explicit resync.
  Current: unit -> Event list
  /// Asks the health owner for a fresh record: a page connecting or resyncing.
  Refresh: unit -> unit
}

module Facts =
  open Fidelity.FSharp.Incremental

  /// The cohort's membership as the board shows it, from the cohort owner's
  /// frame, with the ledger sequence that produced it.
  type Membership = {
    Version: int64
    Members: (Bozzetto.MemberTable.MemberId * Bozzetto.Cohort.JoinableRole * Bozzetto.Cohort.SeatState * DateTime) list
  }

  let membershipOf (frame: Bozzetto.Cohort.CohortFrame<Bozzetto.MemberTable.MemberId>) : Membership =
    { Version = int64 frame.Version
      Members = List.init frame.MemberIds.Length (fun i -> frame.MemberIds[i], frame.MemberRole[i], frame.MemberSeat[i], frame.MemberRenewal[i]) }

  /// What the health projection cannot derive from a fact: the daemon's identity.
  type Constants = { McpPort: int; Version: string; ProcessId: int }

  /// The transport establishes identity before the graph admits a producer.
  /// This type and its correlation do not provide remote authentication.
  type ObservationSource = NodeIdentity
  /// Trusted setup supplies this correlation to the producer and graph only;
  /// it must not be included in consumer snapshots or subscription handles.
  type ObservationProducer = { Source: ObservationSource; Correlation: Guid }
  type ObservationBinding = { Source: ObservationSource; Subscription: Guid }
  type Observation = {
    Binding: ObservationBinding
    Sequence: int64 option
    Value: Result<HostResources, string>
  }

  /// The owners the plane subscribes to: each as its change signal and its
  /// current value.
  type Owners = {
    Tick: IObservable<Bozzetto.Server.HealthTick.Tick> * (unit -> Bozzetto.Server.HealthTick.Tick option)
    Pool: IObservable<Bozzetto.Features.LeaseWatch.Pool> * (unit -> Bozzetto.Features.LeaseWatch.Pool)
    Agents: (IObservable<unit> * (unit -> Bozzetto.AgentWork.State)) option
    Membership: (IObservable<unit> * (unit -> Bozzetto.Cohort.CohortFrame<Bozzetto.MemberTable.MemberId>)) option
    Journal: IObservable<unit> * (unit -> ComposerJournal.Journal)
  }

  /// Why a publication or subscription was not admitted.
  [<RequireQualifiedAccess>]
  type Refusal =
    | Older
    | Duplicate
    | Conflict
    | ForeignIncarnation
    | UnknownSource
    | ForeignProducer
    | Oversized
    /// The plane has retired its epoch at shutdown; nothing is admitted after.
    | Retired

  [<RequireQualifiedAccess>]
  type private Fact =
    | Tick of Bozzetto.Server.HealthTick.Tick
    | Pool of Bozzetto.Features.LeaseWatch.Pool
    | Agents of Bozzetto.AgentWork.State
    | Membership of Membership
    | Journal of ComposerJournal.Journal
    | Observation of int64 * Result<HostResources, string>
    | PeerObservation of HostResources option * Result<HostResources, string>
    | NodeWork of NodeWork

  /// What a value token names: an admitted fact, or a settled projection.
  [<RequireQualifiedAccess>]
  type private Payload =
    | Fact of Fact
    | Value of Event
    | Observation of int64 option * Result<HostResources, string>

  let private tickInput, poolInput, agentsInput, membershipInput, journalInput = InputId 1UL, InputId 2UL, InputId 3UL, InputId 4UL, InputId 5UL
  let private healthWork, leasesWork, boardWork, operationsWork = WorkId 1UL, WorkId 2UL, WorkId 3UL, WorkId 4UL
  let private workBoard = WorkId 5UL
  let private scope = ScopeId 1UL

  /// Sets admitted per epoch before the plane retires it and starts the next:
  /// the core keeps every stamp and completion of an epoch, so the plane bounds them.
  let recycleBound = 4096

  /// The plane of one daemon. `incarnation` is the agent board owner's, bound
  /// at start: a publication from any other incarnation is refused, so a
  /// retired owner can never roll the board back.
  type Graph(constants: Constants, incarnation: string, journalIncarnation: string, ?observationOwners: ObservationProducer list, ?localNode: string, ?peerNodes: string list) =
    let owners = defaultArg observationOwners []
    let peers = defaultArg peerNodes []
    do
      if owners |> List.exists (fun owner -> String.IsNullOrWhiteSpace owner.Source.Node || String.IsNullOrWhiteSpace owner.Source.Incarnation) then
        invalidArg (nameof observationOwners) "Observation owners require a node and incarnation."
      if owners |> List.distinctBy _.Source.Node |> List.length <> owners.Length then
        invalidArg (nameof observationOwners) "Observation owners require distinct nodes."
      if owners |> List.exists (fun owner -> owner.Correlation = Guid.Empty) then
        invalidArg (nameof observationOwners) "Observation owners require a producer correlation."
      let nodes = (owners |> List.map _.Source.Node) @ peers
      if (nodes |> List.exists String.IsNullOrWhiteSpace) || List.distinct nodes <> nodes then
        invalidArg (nameof peerNodes) "Work observation requires distinct, nonempty configured nodes."
      if peers.Length > MaxObservedPeers then invalidArg (nameof peerNodes) "Too many configured work observation peers."
      if nodes |> List.exists (fun node -> node.Length > 64) then invalidArg (nameof peerNodes) "Work observation node names exceed 64 characters."
      if owners |> List.exists (fun owner -> owner.Source.Incarnation.Length > 128) then invalidArg (nameof observationOwners) "Work observation incarnations exceed 128 characters."
      match localNode with
      | Some node when owners.Length <> 1 || owners.Head.Source.Node <> node ->
        invalidArg (nameof observationOwners) "A work board requires exactly its one local producer plus configured peers."
      | _ -> ()
    // Node slots are configured once. Only the authenticated connection capability
    // changes; no transport registry or duplicate fact store lives here.
    let mutable sourceSlots =
      owners @ (peers |> List.map (fun node -> { Source = { Node = node; Incarnation = "" }; Correlation = Guid.Empty }))
      |> List.mapi (fun index owner -> owner.Source.Node, (owner, InputId(uint64 index * 2UL + 6UL), WorkId(uint64 index + 6UL)))
      |> Map.ofList
    let observationWorks = sourceSlots |> Map.toList |> List.map (fun (_, (_, input, work)) -> work, input) |> Map.ofList
    let nodeInputs = sourceSlots |> Map.toList |> List.map (fun (node, (_, InputId input, _)) -> node, InputId(input + 1UL)) |> Map.ofList
    let gate = obj ()
    let mutable epoch = 1UL
    let mutable core = Core.init (EpochId epoch)
    /// One counter for input stamps and value tokens: unique for the plane's lifetime.
    let mutable serial = 0UL
    let mutable store : Map<ValueToken, Payload> = Map.empty
    let mutable inputs : Map<InputId, ValueToken> = Map.empty
    let mutable admitted : Map<InputId, int64> = Map.empty
    /// The publication serial each work's current value carries on the wire.
    let mutable stamps : Map<WorkId, int64> = Map.empty
    let mutable failed : Set<WorkId> = Set.empty
    let mutable sets = 0
    let mutable handover = false
    let mutable stopped = false
    let mutable faults = 0
    let sinks = System.Collections.Generic.Dictionary<int64, Push -> Event -> unit>()
    let mutable nextSink = 0L
    let observers = System.Collections.Generic.Dictionary<Guid, ObservationBinding * DemandId * (Observation -> unit)>()
    let mutable nextObserver = 5UL

    let pushOf (work: WorkId) =
      if work = healthWork then Push.Health
      elif work = leasesWork then Push.Leases
      elif work = operationsWork then Push.Operations
      elif work = workBoard then Push.WorkNodes
      else Push.AgentWork

    /// The projection of one work over the facts its frozen reads name, with
    /// the publication serial the result carries on the wire.
    let project (work: WorkId) (stamp: int64) (facts: Map<ReadSlotId, Fact>) : Payload =
      let fact slot = Map.tryFind (ReadSlotId slot) facts
      if work = healthWork then
        match fact 1UL with
        | Some(Fact.Tick tick) ->
          Payload.Value(Health
            { Project.health
                { HealthSources.McpPort = constants.McpPort; Version = constants.Version; ProcessId = constants.ProcessId
                  Health = tick.Record; Telemetry = tick.Telemetry; Machine = tick.Machine; Level = tick.Level
                  SampledAtMs = tick.SampledAtMs; RecordAtMs = tick.RecordAtMs }
              with Revision = stamp })
        | other -> invalidOp (sprintf "Bridge facts: Health read %A" other)
      elif work = leasesWork then
        match fact 1UL with
        | Some(Fact.Pool pool) -> Payload.Value(Leases(Project.leaseBoard stamp pool.Active pool.Queue))
        | other -> invalidOp (sprintf "Bridge facts: Leases read %A" other)
      elif work = operationsWork then
        match fact 1UL with
        | Some(Fact.Journal journal) -> Payload.Value(Operations(Project.operations stamp journal))
        | other -> invalidOp (sprintf "Bridge facts: Operations read %A" other)
      elif Map.containsKey work observationWorks then
        match fact 1UL with
        | Some(Fact.Observation(sequence, value)) -> Payload.Observation(Some sequence, value)
        | Some(Fact.PeerObservation(retained, value)) -> Payload.Observation(retained |> Option.map _.Sequence, value)
        | other -> invalidOp (sprintf "Bridge facts: Observation read %A" other)
      elif work = workBoard then
        let nodes = facts |> Map.toArray |> Array.map (fun (_, value) ->
          match value with Fact.NodeWork node -> node | other -> invalidOp (sprintf "Bridge facts: Work node read %A" other))
        Payload.Value(WorkNodes { Revision = stamp; Nodes = nodes })
      else
        match fact 1UL, fact 2UL, fact 3UL with
        | Some(Fact.Agents state), Some(Fact.Pool pool), Some(Fact.Membership membership) ->
          Payload.Value(AgentWork(Project.agentBoard stamp state (Project.presence pool.Active pool.Queue membership.Members state.Runs)))
        | other -> invalidOp (sprintf "Bridge facts: Board read %A" other)

    let deliver (work: WorkId) (token: ValueToken) (payload: Payload) =
      match payload with
      | Payload.Value event ->
        let kind = pushOf work
        for KeyValue(id, sink) in sinks |> Seq.toArray do
          if sinks.ContainsKey id && (Core.tryResult work core |> Option.map _.Value) = Some token then
            try sink kind event with _ -> () // Sinks enqueue; never block the plane.
      | Payload.Observation(sequence, value) ->
        for binding, _, sink in observers.Values |> Seq.toArray do
          let _, _, sourceWork = sourceSlots[binding.Source.Node]
          // A preceding callback may detach this focus or publish a newer
          // source fact. Foundation's current receipt, not a delivery clock,
          // decides whether this offer is still eligible.
          let current = Core.tryResult work core |> Option.map _.Value
          if work = sourceWork && observers.ContainsKey binding.Subscription && current = Some token then
            try sink { Binding = binding; Sequence = sequence; Value = value } with _ -> ()
      | Payload.Fact _ -> ()

    /// One command, its state committed, then its effects in their returned
    /// order. A projection runs here, synchronously, over frozen receipts:
    /// Start, store, Finished, Drained; the Offer that follows is delivered.
    let rec apply (action: Action) : unit =
      let next, effects =
        Core.step { Epoch = EpochId epoch; Action = action } core
        |> Result.defaultWith (fun error -> invalidOp (sprintf "Bridge facts: %A" error))
      core <- next
      for effect in effects do
        match effect.Action with
        | EffectAction.Start request ->
          let work = request.Definition.Work
          let facts =
            request.Reads
            |> List.choose (fun read ->
              match read.Value with
              | ReadValue.Input(_, _, token) ->
                match Map.tryFind token store with
                | Some(Payload.Fact fact) -> Some(read.Slot, fact)
                | _ -> None
              | ReadValue.Work _ -> None)
            |> Map.ofList
          // A recomputation during an epoch handover is not news: it keeps its stamp.
          let stamp = (Map.tryFind work stamps |> Option.defaultValue 0L) + (if handover then 0L else 1L)
          match (try Ok(project work stamp facts) with error -> Error error) with
          | Ok projection ->
            serial <- serial + 1UL
            let token = ValueToken serial
            store <- Map.add token projection store
            stamps <- Map.add work stamp stamps
            apply (Action.Finished(request.Attempt, Completion.Succeeded token))
            apply (Action.Drained request.Attempt)
          | Error error ->
            Log.warn "[ui-bridge] projecting %A failed: %s" work error.Message
            faults <- faults + 1
            failed <- Set.add work failed
            apply (Action.Finished(request.Attempt, Completion.Failed({ Code = "projection"; Message = error.Message }: Failure)))
            apply (Action.Drained request.Attempt)
        | EffectAction.Offer handle ->
          match Map.tryFind handle.Value store with
          | Some payload when not handover -> deliver handle.Work handle.Value payload
          | _ -> ()
        | EffectAction.Withdraw handle -> store <- Map.remove handle.Value store
        | EffectAction.Cancel attempt ->
          apply (Action.Finished(attempt, Completion.Cancelled))
          apply (Action.Drained attempt)
        | EffectAction.Drain attempt -> apply (Action.Drained attempt)
        | EffectAction.Suspended _ | EffectAction.Continue _ | EffectAction.ScopeClosed _ | EffectAction.EpochDrained -> ()

    let define () =
      apply (Action.ReserveScope(scope, RevisionId 1UL))
      apply (Action.ReplaceScope(scope, RevisionId 1UL, [
        ScopeEntry.Define { Work = healthWork; Stamp = DefinitionStamp 1UL; Reads = [ { Slot = ReadSlotId 1UL; Source = ReadSource.Input tickInput } ] }
        ScopeEntry.Define { Work = leasesWork; Stamp = DefinitionStamp 1UL; Reads = [ { Slot = ReadSlotId 1UL; Source = ReadSource.Input poolInput } ] }
        ScopeEntry.Define {
          Work = boardWork; Stamp = DefinitionStamp 1UL
          Reads = [ { Slot = ReadSlotId 1UL; Source = ReadSource.Input agentsInput }
                    { Slot = ReadSlotId 2UL; Source = ReadSource.Input poolInput }
                    { Slot = ReadSlotId 3UL; Source = ReadSource.Input membershipInput } ] }
        ScopeEntry.Define { Work = operationsWork; Stamp = DefinitionStamp 1UL; Reads = [ { Slot = ReadSlotId 1UL; Source = ReadSource.Input journalInput } ] }
        if localNode.IsSome then
          ScopeEntry.Define { Work = workBoard; Stamp = DefinitionStamp 1UL
                              Reads = nodeInputs |> Map.toList |> List.mapi (fun i (_, input) -> { Slot = ReadSlotId(uint64 i + 1UL); Source = ReadSource.Input input }) }
        for KeyValue(work, input) in observationWorks do
          ScopeEntry.Define { Work = work; Stamp = DefinitionStamp 1UL; Reads = [ { Slot = ReadSlotId 1UL; Source = ReadSource.Input input } ] } ]))
      apply (Action.Demand(DemandId 1UL, healthWork))
      apply (Action.Demand(DemandId 2UL, leasesWork))
      apply (Action.Demand(DemandId 3UL, boardWork))
      apply (Action.Demand(DemandId 4UL, operationsWork))
      if localNode.IsSome then apply (Action.Demand(DemandId 5UL, workBoard))
      for binding, demand, _ in observers.Values do
        let _, _, work = sourceSlots[binding.Source.Node]
        apply (Action.Demand(demand, work))

    /// Retire the epoch and start the next with the same inputs, in one
    /// section. The recomputations are an artifact of the handover, not news:
    /// nothing is delivered and no stamp advances; wire identity is preserved.
    let recycle () =
      handover <- true
      try
        apply Action.Retire
        if not (Core.isDrained core) then invalidOp "Bridge facts: the retired epoch did not drain"
        epoch <- epoch + 1UL
        core <- Core.init (EpochId epoch)
        sets <- 0
        failed <- Set.empty
        define ()
        let values =
          inputs |> Map.toList |> List.map (fun (input, token) ->
            serial <- serial + 1UL
            { InputValue.Input = input; Stamp = InputStamp serial; Value = token })
        if not values.IsEmpty then apply (Action.SetInputs values)
      finally handover <- false

    /// Admission, before SetInputs and under the gate: the owner's serial
    /// orders its publications, since the core itself only refuses a reused
    /// stamp and treats an exact duplicate as a no-op. Every fact is a complete
    /// replacement snapshot, so an older serial is dropped whole.
    let admit (input: InputId) (ownerSerial: int64) (fact: Fact) : Result<unit, Refusal> =
      lock gate (fun () ->
        match Map.tryFind input admitted with
        | _ when stopped -> Error Refusal.Retired
        | Some last when ownerSerial < last -> Error Refusal.Older
        | Some last when ownerSerial = last ->
          let current = Map.tryFind input inputs |> Option.bind (fun token -> Map.tryFind token store)
          if current = Some(Payload.Fact fact) then Error Refusal.Duplicate
          else
            faults <- faults + 1
            Log.warn "[ui-bridge] %A republished serial %d with different content" input ownerSerial
            Error Refusal.Conflict
        | _ ->
          for work in failed do apply (Action.Retry work)
          failed <- Set.empty
          admitted <- Map.add input ownerSerial admitted
          serial <- serial + 1UL
          let token = ValueToken serial
          let previous = Map.tryFind input inputs
          store <- Map.add token (Payload.Fact fact) store
          inputs <- Map.add input token inputs
          apply (Action.SetInputs [ { InputValue.Input = input; Stamp = InputStamp serial; Value = token } ])
          previous |> Option.iter (fun old -> store <- Map.remove old store)
          sets <- sets + 1
          if sets >= recycleBound then recycle ()
          Ok())

    /// The eligible value of each work, in wire order: Health, Leases, the board, the operations.
    let current () : Event list =
      [ healthWork; leasesWork; boardWork; operationsWork; if localNode.IsSome then workBoard ]
      |> List.choose (fun work ->
        Core.tryResult work core
        |> Option.bind (fun handle ->
          match Map.tryFind handle.Value store with
          | Some(Payload.Value event) -> Some event
          | _ -> None))

    let observationSource (source: ObservationSource) =
      if stopped then Error Refusal.Retired
      else
        match Map.tryFind source.Node sourceSlots with
        | None -> Error Refusal.UnknownSource
        | Some(expected, _, _) when source <> expected.Source -> Error Refusal.ForeignIncarnation
        | Some slot -> Ok slot

    let observationCurrent binding work =
      let current = Core.tryResult work core |> Option.bind (fun handle -> Map.tryFind handle.Value store)
      match current with
      | Some(Payload.Observation(sequence, value)) -> { Binding = binding; Sequence = sequence; Value = value }
      | _ -> { Binding = binding; Sequence = None; Value = Error "Source has not published an observation." }

    let publishObservation (producer: ObservationProducer) sequence value =
      lock gate (fun () ->
        match observationSource producer.Source with
        | Error Refusal.ForeignIncarnation ->
          faults <- faults + 1
          Error Refusal.ForeignIncarnation
        | Error reason -> Error reason
        | Ok(expected, _, _) when producer.Correlation <> expected.Correlation ->
          faults <- faults + 1
          Error Refusal.ForeignProducer
        | Ok(_, input, _) -> admit input sequence (Fact.Observation(sequence, value)))

    let producerSlot (producer: ObservationProducer) =
      observationSource producer.Source |> Result.bind (fun (expected, input, work) ->
        if expected.Correlation = Guid.Empty || producer.Correlation <> expected.Correlation then Error Refusal.ForeignProducer
        else Ok(input, work))

    let inputFact input =
      Map.tryFind input inputs |> Option.bind (fun token ->
        match Map.tryFind token store with Some(Payload.Fact fact) -> Some fact | _ -> None)

    // Status publication is controller-owned. It must not advance or fabricate
    // the remote sample/snapshot sequence, which stays inside the admitted fact.
    let publishStatus input fact = admit input ((Map.tryFind input admitted |> Option.defaultValue -1L) + 1L) fact
    let nodeValue node =
      match inputFact nodeInputs[node] with Some(Fact.NodeWork value) -> Some value | _ -> None
    let retainedResources input =
      match inputFact input with
      | Some(Fact.PeerObservation(retained, _)) -> retained
      | Some(Fact.Observation(_, Ok value)) -> Some value
      | _ -> None
    let boundedReason (reason: string) = if reason.Length <= 512 then reason else reason.Substring(0, 512)
    let withinWorkBudget value =
      try Bozzetto.Web.Shared.BinaryCodec.encodePeerFrame(PeerWork value).Length <= MaxWorkSnapshotBytes
      with :? InvalidOperationException -> false

    do
      define ()
      if localNode.IsSome then
        for KeyValue(node, (producer, _, _)) in sourceSlots do
          publishStatus nodeInputs[node] (Fact.NodeWork {
            Source = producer.Source; Local = localNode = Some node; Status = "unavailable"
            Note = "Source has not published work."; Value = None }) |> ignore

    new(constants: Constants, incarnation: string) = Graph(constants, incarnation, "")

    /// Registers a page's sink and returns the current values in the same
    /// critical section: every publication settled after it reaches the sink,
    /// none settled before it is replayed after a later one.
    member _.Register(sink: Push -> Event -> unit) : Event list * IDisposable =
      lock gate (fun () ->
        nextSink <- nextSink + 1L
        let id = nextSink
        sinks[id] <- sink
        current (), { new IDisposable with member _.Dispose() = lock gate (fun () -> sinks.Remove id |> ignore) })

    /// Atomic current/register for one preadmitted source. Detach releases only
    /// this observation demand; it neither retires the source nor affects work.
    /// Sinks run under the graph gate: enqueue only, never block or wait here.
    member _.RegisterObservation(source, sink: Observation -> unit) =
      lock gate (fun () ->
        observationSource source |> Result.map (fun (_, _, work) ->
          let binding = { Source = source; Subscription = Guid.NewGuid() }
          nextObserver <- nextObserver + 1UL
          let demand = DemandId nextObserver
          apply (Action.Demand(demand, work))
          observers.Add(binding.Subscription, (binding, demand, sink))
          observationCurrent binding work,
          { new IDisposable with
              member _.Dispose() =
                lock gate (fun () ->
                  if observers.Remove binding.Subscription && not stopped then apply (Action.Release demand)) }))

    member _.PublishObservation(producer: ObservationProducer, value: HostResources) = publishObservation producer value.Sequence (Ok value)
    /// A bound producer can recover after unavailability with a newer serial,
    /// independently of whether any consumer currently demands its projection.
    member _.UnavailableObservation(producer: ObservationProducer, sequence, reason) = publishObservation producer sequence (Error reason)

    /// Trusted transport setup replaces a configured connection. Even a reconnect
    /// to the same incarnation gets a fresh capability; retired callbacks fail.
    member _.BindPeer(source: NodeIdentity) =
      lock gate (fun () ->
        if stopped then invalidOp "Work observation graph is retired."
        if String.IsNullOrWhiteSpace source.Incarnation || source.Incarnation.Length > 128 then invalidArg (nameof source) "An authenticated source requires an incarnation of at most 128 characters."
        match Map.tryFind source.Node sourceSlots with
        | None -> invalidArg (nameof source) "Work observation source is not configured."
        | Some _ when localNode = Some source.Node -> invalidArg (nameof source) "The local source cannot be rebound by a peer."
        | Some(previous, input, work) ->
          let same = previous.Source = source
          let retained = if same then retainedResources input else None
          let value = if same then nodeValue source.Node |> Option.bind _.Value else None
          let producer = { Source = source; Correlation = Guid.NewGuid() }
          for binding, demand, sink in observers.Values |> Seq.toArray do
            if binding.Source.Node = source.Node && observers.Remove binding.Subscription then
              apply (Action.Release demand)
              try sink { Binding = binding; Sequence = None; Value = Error "Source connection was replaced; select the node again." } with _ -> ()
          sourceSlots <- Map.add source.Node (producer, input, work) sourceSlots
          publishStatus input (Fact.PeerObservation(retained, Error "Source has not published resources on this connection.")) |> ignore
          publishStatus nodeInputs[source.Node] (Fact.NodeWork {
            Source = source; Local = false; Status = "unavailable"; Note = "Source has not published work on this connection."; Value = value }) |> ignore
          producer)

    member _.Source(node: string) =
      lock gate (fun () ->
        if stopped then Error Refusal.Retired
        else Map.tryFind node sourceSlots |> Option.map (fun (producer, _, _) -> Ok producer.Source) |> Option.defaultValue (Error Refusal.UnknownSource))

    member _.PublishWork(producer: ObservationProducer, value: WorkSnapshot) =
      lock gate (fun () ->
        producerSlot producer |> Result.bind (fun _ ->
          let previous = nodeValue producer.Source.Node
          match previous |> Option.bind _.Value with
          | Some old when value.Sequence < old.Sequence -> Error Refusal.Older
          | Some old when value.Sequence = old.Sequence && value <> old -> Error Refusal.Conflict
          | _ when not (withinWorkBudget value) ->
            publishStatus nodeInputs[producer.Source.Node] (Fact.NodeWork {
              Source = producer.Source; Local = localNode = Some producer.Source.Node; Status = "unavailable"
              Note = "Work snapshot exceeds the encoded observation budget."; Value = previous |> Option.bind _.Value }) |> ignore
            Error Refusal.Oversized
          | Some old when value = old && (previous |> Option.exists (fun node -> node.Status = "available")) -> Error Refusal.Duplicate
          | _ -> publishStatus nodeInputs[producer.Source.Node] (Fact.NodeWork {
              Source = producer.Source; Local = localNode = Some producer.Source.Node
              Status = "available"; Note = ""; Value = Some value })))

    member _.PeerUnavailable(producer: ObservationProducer, reason: string) =
      lock gate (fun () ->
        producerSlot producer |> Result.bind (fun _ ->
          let value = nodeValue producer.Source.Node |> Option.bind _.Value
          publishStatus nodeInputs[producer.Source.Node] (Fact.NodeWork {
            Source = producer.Source; Local = localNode = Some producer.Source.Node; Status = "unavailable"; Note = boundedReason reason; Value = value })))

    member _.PeerConnectionUnavailable(node: string, reason: string) =
      lock gate (fun () ->
        match Map.tryFind node sourceSlots with
        | _ when stopped -> Error Refusal.Retired
        | None -> Error Refusal.UnknownSource
        | Some(producer, _, _) when producer.Correlation <> Guid.Empty -> Error Refusal.ForeignProducer
        | Some(producer, _, _) -> publishStatus nodeInputs[node] (Fact.NodeWork {
            Source = producer.Source; Local = false; Status = "unavailable"; Note = boundedReason reason; Value = None }))

    member _.PublishPeerResources(producer: ObservationProducer, value: HostResources) =
      lock gate (fun () ->
        producerSlot producer |> Result.bind (fun (input, _) ->
          match retainedResources input with
          | Some old when value.Sequence < old.Sequence -> Error Refusal.Older
          | Some old when value.Sequence = old.Sequence && value.Status = old.Status && value <> old -> Error Refusal.Conflict
          | Some old when value.Sequence = old.Sequence && { old with Status = value.Status; Note = value.Note } <> value -> Error Refusal.Conflict
          | _ ->
            let fact = Fact.PeerObservation(Some value, Ok value)
            if inputFact input = Some fact then Error Refusal.Duplicate else publishStatus input fact))

    member _.PeerResourcesUnavailable(producer: ObservationProducer, reason: string) =
      lock gate (fun () -> producerSlot producer |> Result.bind (fun (input, _) ->
        publishStatus input (Fact.PeerObservation(retainedResources input, Error(boundedReason reason)))))

    /// The local source composes the current owner facts under their graph gate.
    /// The complete value is then admitted like a peer value; no second ledger.
    member this.PublishLocalWork(producer: ObservationProducer, snapshot: ComposerSnapshot, sampledAtMs: int64) =
      lock gate (fun () ->
        let values = current ()
        let leases = values |> List.tryPick (function Leases value -> Some value | _ -> None)
        let operations = values |> List.tryPick (function Operations value -> Some value | _ -> None)
        let previous = nodeValue producer.Source.Node |> Option.bind _.Value
        match leases, operations with
        | Some leases, Some operations when previous |> Option.exists (fun old -> snapshot.Revision < old.Snapshot.Revision) -> Error Refusal.Older
        | Some leases, Some operations ->
          this.PublishWork(producer, Project.workSummary {
            Sequence = (previous |> Option.map _.Sequence |> Option.defaultValue -1L) + 1L
            SampledAtMs = sampledAtMs; Snapshot = snapshot; Leases = leases; Operations = operations; TranscriptsOmitted = false })
        | _ -> Error Refusal.UnknownSource)

    /// Effective demand is Foundation-owned and differs from retained cache.
    member _.ObservationDemanded(source) =
      lock gate (fun () ->
        observationSource source |> Result.map (fun (_, _, work) ->
          (Core.snapshot core).Works |> List.exists (fun view -> view.Work = work && view.Demanded)))

    member _.Current() : Event list = lock gate current
    /// Owner protocol faults seen: a serial republished with other content, a
    /// foreign incarnation or producer, a projection that threw.
    member _.Faults = lock gate (fun () -> faults)
    member _.Epoch = lock gate (fun () -> epoch)
    /// Settled projections the store holds: one per work with a value, never
    /// a superseded one (Withdraw releases it in the same step sequence).
    member _.StoredResults =
      lock gate (fun () -> store |> Map.toSeq |> Seq.filter (fun (_, payload) -> match payload with Payload.Value _ | Payload.Observation _ -> true | _ -> false) |> Seq.length)
    member _.Drained = lock gate (fun () -> Core.isDrained core)

    /// Shutdown: retire the epoch, interpret its effects, and forget the pages.
    /// A callback already waiting on the gate is refused afterwards as Retired.
    member _.Stop() =
      lock gate (fun () ->
        if not stopped then
          stopped <- true
          handover <- true
          try apply Action.Retire finally handover <- false
          sinks.Clear()
          observers.Clear())

    member _.Tick(tick: Bozzetto.Server.HealthTick.Tick) = admit tickInput tick.Serial (Fact.Tick tick)
    member _.Pool(pool: Bozzetto.Features.LeaseWatch.Pool) = admit poolInput pool.Serial (Fact.Pool pool)
    member _.Agents(state: Bozzetto.AgentWork.State) =
      if state.Incarnation <> incarnation then
        lock gate (fun () -> faults <- faults + 1)
        Log.warn "[ui-bridge] agent work from incarnation %s refused; the plane is bound to %s" state.Incarnation incarnation
        Error Refusal.ForeignIncarnation
      else admit agentsInput state.Sequence (Fact.Agents state)
    member _.Membership(frame: Bozzetto.Cohort.CohortFrame<Bozzetto.MemberTable.MemberId>) =
      let membership = membershipOf frame
      admit membershipInput membership.Version (Fact.Membership membership)
    /// The operations journal names its owner's incarnation; the plane binds to
    /// the one it started with and refuses any other.
    member _.Journal(journal: ComposerJournal.Journal) =
      if journal.Incarnation <> journalIncarnation then
        lock gate (fun () -> faults <- faults + 1)
        Log.warn "[ui-bridge] operations journal from incarnation %s refused; the plane is bound to %s" journal.Incarnation journalIncarnation
        Error Refusal.ForeignIncarnation
      else admit journalInput journal.Sequence (Fact.Journal journal)

    /// Subscribe to every owner and take its current value in one critical
    /// section, so a publication between the two is applied after it, by serial.
    member this.Start(owners: Owners) : IDisposable =
      lock gate (fun () ->
        let tickChanged, tickNow = owners.Tick
        let poolChanged, poolNow = owners.Pool
        let journalChanged, journalNow = owners.Journal
        let subscriptions =
          [ yield tickChanged.Subscribe(fun tick -> this.Tick tick |> ignore)
            yield poolChanged.Subscribe(fun pool -> this.Pool pool |> ignore)
            yield journalChanged.Subscribe(fun () -> this.Journal(journalNow ()) |> ignore)
            yield! owners.Agents |> Option.map (fun (changed, read) -> changed.Subscribe(fun () -> this.Agents(read ()) |> ignore)) |> Option.toList
            yield! owners.Membership |> Option.map (fun (changed, read) -> changed.Subscribe(fun () -> this.Membership(read ()) |> ignore)) |> Option.toList ]
        tickNow () |> Option.iter (fun tick -> this.Tick tick |> ignore)
        this.Pool(poolNow ()) |> ignore
        this.Journal(journalNow ()) |> ignore
        owners.Agents |> Option.iter (fun (_, read) -> this.Agents(read ()) |> ignore)
        owners.Membership |> Option.iter (fun (_, read) -> this.Membership(read ()) |> ignore)
        { new IDisposable with member _.Dispose() = for subscription in subscriptions do subscription.Dispose() })

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
  /// The daemon's own facts: pushed as their owners publish, current on connect.
  Facts: BridgeFacts
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
    MaxFrameBytes = 4 * 1024 * 1024
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
/// A resource sample carries the machine's memory: one more reading for the
/// daemon's one sequencer, so the level and Health stay as live as the page
/// that asked for samples.
let private observeMachine (value: HostResources) =
  let metric name = value.Metrics |> Array.tryFind (fun m -> m.Name = name) |> Option.bind _.Value
  match metric "RAM available", metric "RAM total (OS managed)" with
  | Some available, Some total when total > 0. ->
    Bozzetto.Server.HealthTick.observe { Bozzetto.Features.MachineMemory.Stats.TotalBytes = int64 total; AvailableBytes = int64 available }
    |> ignore
  | _ -> ()

type Hub(sources: BridgeSources, options: BridgeOptions, stopping: CancellationToken, pause: TimeSpan -> Async<unit>, uptime: Async<int64>, ?observation: Facts.Graph * Facts.ObservationProducer * (string -> int -> (string -> unit) -> Async<IDisposable>), ?localTranscript: TranscriptReadRequest -> TranscriptReadOutcome, ?peerTranscript: TranscriptReadRequest -> CancellationToken -> Async<TranscriptReadOutcome>) =
  let resources = ResourceMonitor.create observeMachine
  let connections = ConcurrentDictionary<int64, Outbox>()
  let heartbeatSinks = ConcurrentDictionary<int64, UptimeTick -> unit>()
  let changes = Channel.CreateBounded<unit>(BoundedChannelOptions(1, FullMode = BoundedChannelFullMode.DropOldest))
  let mutable nextConnection = 0L

  let snapshotEvent (directory: ComposerDirectory) = Snapshot(Project.snapshot directory (sources.WorkerPid ()))

  /// The directory's snapshot, then the daemon's current facts.
  let stateEvents (directory: unit -> ComposerDirectory) =
    (attempt "snapshot" (fun () -> snapshotEvent (directory ())) |> Option.toList) @ sources.Facts.Current ()

  let broadcast (kind: Push) (event: Event) =
    let frame = Outbound.ofEvent event
    for outbox in connections.Values do
      outbox.Push(kind, frame)

  let clock = UptimeClock(uptime, fun uptimeMs ->
    let tick = { UptimeTick.UptimeMs = uptimeMs }
    broadcast Push.Uptime (Uptime tick)
    for sink in heartbeatSinks.Values do
      try sink tick with _ -> ())

  let graphAndOwner () =
    observation |> Option.map (fun (graph, producer, _) -> graph, producer)
    |> Option.defaultWith (fun () -> invalidOp "This bridge has no work observation owner.")

  let refreshLocalWork () =
    observation |> Option.iter (fun (graph, producer, _) ->
      attempt "local work" (fun () ->
        graph.PublishLocalWork(producer, Project.snapshot (sources.CachedDirectory ()) (sources.WorkerPid ()), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) |> ignore)
      |> ignore)

  let unavailableResources sequence reason : HostResources =
    { ResourceMonitor.empty "unavailable" with Sequence = defaultArg sequence 0L; Note = reason }

  let executeCompiler (frame: CommandFrame) (connection: CancellationToken) : Async<Outbound list> = async {
    let refuse message =
      [ Refused(UiBridgeCodec.frameRefusal frame.Correlation "provider_unavailable" message) ]
    match Project.toRequest frame.Command with
    | Some request ->
      let! response = settled (fun () -> sources.Execute request stopping)
      match response with
      | Ok reply -> return Project.outcome frame.Correlation reply |> List.map Outbound.ofEvent
      | Error message -> return refuse message |> List.map Outbound.ofEvent
    | None ->
      // An explicit resync asks the daemon how it is before answering.
      sources.Facts.Refresh ()
      let! directory = settled (fun () -> sources.Directory connection)
      match directory with
      | Ok fresh ->
        let done' =
          Accepted { CommandAcceptance.Correlation = frame.Correlation; Target = UiBridgeCodec.noTarget; Completion = SnapshotSent }
        return stateEvents (fun () -> fresh) @ [ done' ] |> List.map Outbound.ofEvent
      | Error message -> return refuse message |> List.map Outbound.ofEvent
  }

  let readLocal (request: TranscriptReadRequest) =
    let _, producer = graphAndOwner ()
    if request.Source <> producer.Source then TranscriptRefused("node_mismatch", "Select the current node incarnation.")
    else
      localTranscript |> Option.map (fun read -> read request)
      |> Option.defaultValue (TranscriptRefused("storage_unavailable", "Node operation history is unavailable."))

  let execute (frame: CommandFrame) (connection: CancellationToken) : Async<Outbound list> = async {
    match frame.Command with
    | ReadNodeTranscript request ->
      let! outcome = async {
        match observation with
        | None -> return TranscriptRefused("storage_unavailable", "Node operation history is unavailable.")
        | Some(_, producer, _) when request.Source.Node = producer.Source.Node -> return readLocal request
        | Some _ ->
          match peerTranscript with
          | Some read -> return! read request connection
          | None -> return TranscriptRefused("node_unavailable", "Node operation history is unavailable.")
      }
      return [ Outbound.ofEvent(NodeTranscript { Source = request.Source; Correlation = frame.Correlation; Outcome = outcome }) ]
    | _ -> return! executeCompiler frame connection
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
  member _.LocalNode = let _, producer = graphAndOwner () in producer.Source
  member _.ReadLocalTranscript(request: TranscriptReadRequest) = readLocal request
  member _.BindPeer(source) = let graph, _ = graphAndOwner () in graph.BindPeer source
  member _.PublishPeerWork(producer, value) = let graph, _ = graphAndOwner () in graph.PublishWork(producer, value)
  member _.PublishPeerResources(producer, value) = let graph, _ = graphAndOwner () in graph.PublishPeerResources(producer, value)
  member _.PeerUnavailable(producer, reason) = let graph, _ = graphAndOwner () in graph.PeerUnavailable(producer, reason)
  member _.PeerResourcesUnavailable(producer, reason) = let graph, _ = graphAndOwner () in graph.PeerResourcesUnavailable(producer, reason)
  member _.PeerConnectionUnavailable(node, reason) = let graph, _ = graphAndOwner () in graph.PeerConnectionUnavailable(node, reason)
  member _.ObserveLocalResources(id, seconds, send) : Task = resources.Observe(id, seconds, send) |> Async.StartAsTask :> Task
  member _.RemoveLocalResources(id) : Task = resources.Remove id |> Async.StartAsTask :> Task
  member _.RegisterLocalWork(send: NodeWork -> unit) =
    let graph, producer = graphAndOwner ()
    let local = function
      | WorkNodes board -> board.Nodes |> Array.tryFind (fun node -> node.Source = producer.Source)
      | _ -> None
    let current, registration = graph.Register(fun _ event -> local event |> Option.iter send)
    current |> List.tryPick local, registration
  member _.RegisterHeartbeat(send: UptimeTick -> unit) =
    let id = Interlocked.Increment &nextConnection
    heartbeatSinks[id] <- send
    clock.Attach()
    { new IDisposable with member _.Dispose() = if heartbeatSinks.TryRemove id |> fst then clock.Detach() }

  /// Begin the snapshot pushes. Status reads never raise Changed, so a push
  /// cannot feed back into another one. The daemon's facts need no loop here:
  /// their owners' publications reach every registered page through the plane.
  member _.Start() =
    let subscription = sources.Changed.Subscribe(fun () ->
      refreshLocalWork ()
      changes.Writer.TryWrite(()) |> ignore)
    stopping.Register(fun () -> subscription.Dispose()) |> ignore
    observation |> Option.iter (fun (graph, _, _) ->
      let _, changed = graph.Register(fun kind _ ->
        if kind = Push.Leases || kind = Push.Operations then refreshLocalWork ())
      stopping.Register(fun () -> changed.Dispose()) |> ignore
      refreshLocalWork ())
    Async.Start(snapshotPushes (), stopping)

  /// Serve one connection until the page or the daemon closes it.
  member _.RunAsync(socket: IBridgeSocket, aborted: CancellationToken) : Task = task {
    use lifetime = CancellationTokenSource.CreateLinkedTokenSource(aborted, stopping)
    let outbox = new Outbox(socket, options.SendTimeout, options.CloseTimeout)
    let inflight = new SemaphoreSlim(options.MaxInFlight)
    let release () = try inflight.Release() |> ignore with _ -> ()
    let id = Interlocked.Increment &nextConnection
    let focusGate = new SemaphoreSlim(1, 1)
    let mutable focused: (Guid * IDisposable) option = None
    let mutable focusClosed = false
    let disposeFocus () =
      let previous = focused
      focused <- None
      previous |> Option.iter (fun (_, subscription) -> subscription.Dispose())
    let focus correlation (request: NodeObservationRequest) = async {
      do! focusGate.WaitAsync() |> Async.AwaitTask
      try
        disposeFocus ()
        do! resources.Remove id
        if not focusClosed && request.Seconds > 0 then
          match observation with
          | None -> return Error "This bridge has no node observation owner."
          | Some(graph, producer, observePeer) ->
            match graph.Source request.Node with
            | Error reason -> return Error(sprintf "Node observation refused: %A." reason)
            | Ok source ->
              let generation = Guid.NewGuid()
              let mutable latest = unavailableResources None "Source has not published resources."
              let send (value: Facts.Observation) =
                let resources = value.Value |> Result.defaultWith (unavailableResources value.Sequence)
                latest <- resources
                outbox.Push(Push.NodeResources, Outbound.ofEvent(NodeResources { Correlation = correlation; Source = value.Binding.Source; Value = resources }))
              // A window's terminal status belongs to this focus, not the shared
              // source fact. Join its stream outside the graph callback/gate.
              let finished status =
                Async.Start(async {
                  do! focusGate.WaitAsync() |> Async.AwaitTask
                  try
                    match focused with
                    | Some(live, _) when live = generation && not focusClosed ->
                      disposeFocus ()
                      outbox.Push(Push.NodeResources, Outbound.ofEvent(NodeResources { Correlation = correlation; Source = source; Value = { latest with Status = status } }))
                      do! resources.Remove id
                    | _ -> ()
                  finally focusGate.Release() |> ignore
                }, CancellationToken.None)
              match graph.RegisterObservation(source, send) with
              | Error reason -> return Error(sprintf "Node observation refused: %A." reason)
              | Ok(current, registration) ->
                send current
                try
                  let! stream =
                    if source = producer.Source then async {
                      do! resources.Observe(id, int request.Seconds, fun value ->
                        match value.Status with
                        | "complete" | "stopped" | "error" | "unavailable" -> finished value.Status
                        | _ -> graph.PublishPeerResources(producer, value) |> ignore)
                      return { new IDisposable with member _.Dispose() = () }
                    }
                    else observePeer request.Node (int request.Seconds) finished
                  focused <- Some(generation, { new IDisposable with member _.Dispose() = registration.Dispose(); stream.Dispose() })
                  return Ok()
                with _ ->
                  send { current with Value = Error "Selected node resource observation is unavailable." }
                  registration.Dispose()
                  return Error "Selected node resource observation is unavailable."
        else return Ok()
      finally focusGate.Release() |> ignore
    }
    let observeLocal seconds = async {
      do! focusGate.WaitAsync() |> Async.AwaitTask
      try
        disposeFocus ()
        do! resources.Remove id
        if not focusClosed && seconds > 0 then
          do! resources.Observe(id, seconds, fun value -> outbox.Push(Push.Resources, Outbound.ofEvent(Resources value)))
      finally focusGate.Release() |> ignore
    }
    connections[id] <- outbox
    // Uptime ticks reach this page through its outbox, after the prologue.
    clock.Attach()
    // A connecting page asks the daemon how it is, then registers for every
    // later publication and receives the current values in the same section;
    // the outbox holds pushes until the prologue is written.
    sources.Facts.Refresh ()
    let current, registration = sources.Facts.Register(fun kind event -> outbox.Push(kind, Outbound.ofEvent event))
    try
      try
        let welcome =
          Welcome {
            BridgeWelcome.ProtocolVersion = ProtocolVersion
            DaemonVersion = sources.DaemonVersion
            StartedAtMs = sources.StartedAtMs
          }
        outbox.Prime(
          welcome :: (attempt "snapshot" (fun () -> snapshotEvent (sources.CachedDirectory ())) |> Option.toList) @ current
          |> List.map Outbound.ofEvent)
        let mutable reading = true
        while reading do
          let! inbound = receive socket lifetime.Token
          match inbound with
          | Inbound.Closed -> reading <- false
          | Inbound.Unusable(code, message) ->
            outbox.Answer([ Outbound.ofEvent (Refused(UiBridgeCodec.frameRefusal 0 code message)) ], ignore)
          | Inbound.Binary payload ->
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
                    do! observeLocal (int seconds)
                    outbox.Answer([ Outbound.ofEvent(Accepted { Correlation = frame.Correlation; Target = UiBridgeCodec.noTarget; Completion = SnapshotSent }) ], release)
                  | ObserveNodeResources request ->
                    let! result = focus frame.Correlation request
                    let answer =
                      match result with
                      | Ok() -> Accepted { Correlation = frame.Correlation; Target = UiBridgeCodec.noTarget; Completion = SnapshotSent }
                      | Error reason -> Refused(UiBridgeCodec.frameRefusal frame.Correlation "observation_unavailable" reason)
                    outbox.Answer([ Outbound.ofEvent answer ], release)
                  | _ ->
                    let! frames = execute frame lifetime.Token
                    outbox.Answer(frames, release)
                }
                Async.Start(work, CancellationToken.None)
              else reading <- false
      with error -> Log.warn "[ui-bridge] connection %d failed: %s" id error.Message
    finally
      registration.Dispose()
      connections.TryRemove(id) |> ignore
      clock.Detach()
      // Ends any RequestSnapshot read still running for this page; Composer
      // operations hold the daemon's token, not this one.
      lifetime.Cancel()
    do! focusGate.WaitAsync()
    try
      focusClosed <- true
      disposeFocus ()
    finally focusGate.Release() |> ignore
    do! resources.Remove id |> Async.StartAsTask
    do! outbox.CloseAsync()
  }

// ── The daemon's bridge ────────────────────────────────────────────────────

module BridgeSources =
  let ofSupervisor (supervisor: ComposerSupervisor) (version: string) (startedAtMs: int64) (facts: BridgeFacts) =
    { DaemonVersion = version
      StartedAtMs = startedAtMs
      Execute = fun request cancellation -> supervisor.ExecuteAsync(request, "browser", cancellation)
      Directory = fun cancellation -> supervisor.SessionsAsync cancellation
      CachedDirectory = supervisor.CachedDirectory
      WorkerPid = fun () -> supervisor.WorkerPid
      Changed = supervisor.Changed :> IObservable<unit>
      Facts = facts }

/// When this daemon process started, as the operating system recorded it.
let private processStartedAtMs () : int64 =
  use self = Process.GetCurrentProcess()
  DateTimeOffset(self.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds()

/// The bridge of one daemon, pushing until `stopping`. The fact plane
/// subscribes to the daemon's owners — the health tick, the lease pool and,
/// when a cohort owner exists, the agent work publication and the cohort frame
/// — and takes their current values in the same section. Without a cohort
/// owner there is no agent board.
let createObservedWithTranscripts (supervisor: ComposerSupervisor) (mcpPort: int) (getHealth: unit -> Bozzetto.Features.HealthSnapshot option) (stopping: CancellationToken) (owner: Bozzetto.Features.CohortOwner.Handle option) (localNode: string) (peerNodes: string list) (observePeer: string -> int -> (string -> unit) -> Async<IDisposable>)
    (readLocalTranscript: TranscriptReadRequest -> TranscriptReadOutcome)
    (readPeerTranscript: TranscriptReadRequest -> CancellationToken -> Async<TranscriptReadOutcome>) =
  Bozzetto.Server.HealthTick.install getHealth
  let producer: Facts.ObservationProducer =
    { Source = { Node = localNode; Incarnation = Guid.NewGuid().ToString("N") }; Correlation = Guid.NewGuid() }
  let graph =
    Facts.Graph(
      { Facts.Constants.McpPort = mcpPort; Version = DaemonInfo.version; ProcessId = Environment.ProcessId },
      owner |> Option.map (fun o -> o.ReadWork().Incarnation) |> Option.defaultValue "",
      supervisor.Journal.Incarnation,
      observationOwners = [producer], localNode = localNode, peerNodes = peerNodes)
  let subscriptions =
    graph.Start
      { Facts.Owners.Tick = Bozzetto.Server.HealthTick.Changed, Bozzetto.Server.HealthTick.current
        Pool = Bozzetto.Features.LeaseWatch.changed, Bozzetto.Features.LeaseWatch.current
        Agents = owner |> Option.map (fun o -> o.WorkChanged, (fun () -> o.ReadWork()))
        Membership = owner |> Option.map (fun o -> (o.Events |> Observable.map ignore), (fun () -> o.ReadFrame()))
        Journal = supervisor.JournalChanged, (fun () -> supervisor.Journal) }
  stopping.Register(fun () ->
    subscriptions.Dispose()
    graph.Stop()) |> ignore
  // The first record: a page connecting later finds Health settled.
  Bozzetto.Server.HealthTick.refresh () |> ignore
  let facts = { BridgeFacts.Register = graph.Register; Current = graph.Current; Refresh = fun () -> Bozzetto.Server.HealthTick.refresh () |> ignore }
  let startedAtMs = processStartedAtMs ()
  let hub = Hub(
    BridgeSources.ofSupervisor supervisor DaemonInfo.version startedAtMs facts, BridgeOptions.defaults, stopping,
    (fun interval -> Async.Sleep(int interval.TotalMilliseconds)), UptimeClock.system startedAtMs,
    observation = (graph, producer, observePeer), localTranscript = readLocalTranscript, peerTranscript = readPeerTranscript)
  hub.Start()
  hub

let createObserved supervisor mcpPort getHealth stopping owner localNode peerNodes observePeer =
  createObservedWithTranscripts supervisor mcpPort getHealth stopping owner localNode peerNodes observePeer
    (fun _ -> TranscriptRefused("storage_unavailable", "Node operation history is unavailable."))
    (fun _ _ -> async { return TranscriptRefused("node_unavailable", "Node operation history is unavailable.") })

let create supervisor mcpPort getHealth stopping owner =
  createObserved supervisor mcpPort getHealth stopping owner Environment.MachineName []
    (fun _ _ _ -> async { return invalidOp "No peer observation transport is configured." })

/// A WebSocket as the bridge's transport: typed binary frames up to a size cap.
type private WebSocketTransport(socket: WebSocket, maxFrameBytes: int) =
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
                elif received.MessageType <> WebSocketMessageType.Binary then
                  Inbound.Unusable("invalid_request", "frame: the UI bridge requires a typed binary frame.")
                else
                  Inbound.Binary(content.ToArray()))
      return inbound.Value
    }
    member _.Send(bytes, cancellation) =
      socket.SendAsync(ReadOnlyMemory<byte>(bytes), WebSocketMessageType.Binary, true, cancellation).AsTask()
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

/// GET /dashboard and /work serve one welded bundle with route-selected
/// layouts (resources on Dashboard; compiler operations and leases on Work), GET /favicon.*
/// its tab icons, and GET /ui/bridge upgrades to the bridge. All sit behind
/// the daemon's origin guard.
let mapRoutes (headless: bool) (app: WebApplication) (createHub: unit -> Hub) =
  if not headless then
    let hub = createHub()
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
    for route in [ "/dashboard"; "/work" ] do
      app.MapGet(route, servePage) |> ignore

    // Passive latest-observation API; reading it never starts acquisition.
    app.MapGet("/api/resources", RequestDelegate(fun ctx -> task {
      ctx.Response.ContentType <- "application/octet-stream"
      ctx.Response.Headers.CacheControl <- StringValues "no-store"
      do! ctx.Response.Body.WriteAsync(UiBridgeCodec.encodeEvent(Resources hub.Resources), ctx.RequestAborted)
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
