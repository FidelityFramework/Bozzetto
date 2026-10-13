/// The browser UI bridge, pinned from both ends: the backend codec against the
/// exact bytes bozzetto-web's Fable codecs produce for the samples of
/// bozzetto-web/tests/RoundTrip.fs, the pure projections from Composer's
/// replies, and the connection contract (one outcome per command, pushes after
/// the prologue, nothing written after close) over a fake supervisor and a
/// fake socket.
module Bozzetto.Tests.UiBridgeTests

open System
open System.Collections.Concurrent
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.FSharp.Reflection
open Expecto
open Expecto.Flip
open FsCheck
open FsCheck.FSharp
open Fidelity.Data.JSON
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration
open Bozzetto.Server.UiBridge
open Bozzetto.Web.Shared.Protocol

module Codec = Bozzetto.Server.UiBridgeCodec
module Observe = Bozzetto.Server.WorkObservation

// ── The UI side, in .NET ───────────────────────────────────────────────────

/// CLR and Fable consume this exact codec owner. Independent golden bytes and
/// malformed raw frames below prevent two copies from agreeing on a mistake.
module private Ui =
  let encodeCommand = Bozzetto.Web.Shared.BinaryCodec.encodeCommand
  let decodeEvent = Bozzetto.Web.Shared.BinaryCodec.decodeEvent

let private worker = { WorkerTarget.Host = "host-α"; Epoch = "epoch-0001" }
let private target = { SessionTarget.Worker = worker; Session = "session-日本-🎼" }
let private emptyTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }

let private artifact = {
  ArtifactSummary.Generation = Int64.MaxValue
  SourceVersion = "src \"quoted\" \\ back"
  ArtifactPath = "/tmp/a b/naïve.out"
  ArtifactSha256 = String.replicate 64 "f"
  Regions = [|
    { RegionRow.Identity = "common"; Witness = "changed"; Visits = Int32.MaxValue; Object = "compiled" }
    { RegionRow.Identity = "callable:M:f \"q\""; Witness = "retained"; Visits = 0; Object = "" }
    { RegionRow.Identity = "o-retired"; Witness = ""; Visits = Int32.MinValue; Object = "retired" }
  |]
}

let private fullSession = {
  SessionStatus.Target = target
  Generation = 9007199254740993L
  Project = "/home/ü/Ünïcode.fidproj"
  Closed = true
  Busy = true
  StatusFresh = false
  RevocationPending = true
  CleanupPending = true
  FormatterCleanupPending = true
  Current = Some artifact
  BackendError = Some "backend\nline two"
  FormatterError = Some "formatter"
  CleanupError = Some "cleanup"
  WorkerRetirementRequired = Some "retire"
  StatusError = Some "stale"
  WorkerError = Some ""
}

let private bareSession = {
  fullSession with
    Generation = Int64.MinValue
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

let private commands = [
  RequestSnapshot
  OpenProject "/abs/Ünï 🎼.fidproj"
  OpenProject ""
  Reserve { ReserveEdit.Target = target; Label = "edit: \"main\" — ✓" }
  Build { BuildReserved.Target = target; Reservation = "rsv-0123" }
  Run { RunCurrent.Target = target; Arguments = [||] }
  Run { RunCurrent.Target = target; Arguments = [| "a b"; "\"q\""; "🎼"; ""; "\\" |] }
  Cancel target
  CloseSession target
  RetireWorker worker
  ObserveResources 120
  ObserveResources 0
]

let private completions = [
  SessionOpened
  EditReserved "rsv-ü"
  ReservationBuilt artifact
  RunFinished -1
  WorkCanceled
  SessionClosed
  WorkerRetired
  SnapshotSent
]

let private events = [
  Welcome { BridgeWelcome.ProtocolVersion = ProtocolVersion; DaemonVersion = "0.6.834"; StartedAtMs = 1759420800123L }
  Welcome { BridgeWelcome.ProtocolVersion = Int32.MaxValue; DaemonVersion = ""; StartedAtMs = Int64.MinValue }
  Snapshot { ComposerSnapshot.Revision = 0L; Worker = Unconfigured; Sessions = [||] }
  Snapshot { ComposerSnapshot.Revision = 1L; Worker = Idle; Sessions = [| bareSession |] }
  Snapshot {
    ComposerSnapshot.Revision = Int64.MaxValue
    Worker = Running { WorkerInfo.Target = worker; CompilerVersion = "0.0.2+abc"; ProcessId = Some 31337 }
    Sessions = [| fullSession; bareSession |]
  }
  Snapshot {
    ComposerSnapshot.Revision = 2L
    Worker = Running { WorkerInfo.Target = worker; CompilerVersion = ""; ProcessId = None }
    Sessions = [||]
  }
  yield! completions |> List.mapi (fun i c ->
    Accepted { CommandAcceptance.Correlation = i + 1; Target = target; Completion = c })
  Refused { CommandRefusal.Correlation = 0; Target = emptyTarget; Code = "invalid_request"; Message = "Frame is not JSON." }
  Refused { CommandRefusal.Correlation = Int32.MaxValue; Target = target; Code = "busy"; Message = "ünïcode ⚠" }
  RunOutput {
    RunTranscript.Correlation = 7
    Target = target
    Generation = Int64.MinValue
    SourceVersion = "src-2"
    ExitCode = -2147483648
    StandardOutput = "line one\r\nline two\ttab — 日本語 🎼\u0000nul"
    StandardError = ""
  }
  Health {
    DaemonHealth.Revision = 1L
    Version = "0.6.834"
    ProcessId = 4242
    McpPort = 47749
    Overall = "Healthy"
    MemoryPressure = "Normal"
    MemoryPressureNote = "60.0% of RAM available — the machine has room; tight at or below 20%"
    ResidentBytes = 0L
    MachineAvailableBytes = Int64.MaxValue
    MachineTotalBytes = 66571993088L
    CpuPercent = 1e-7
    Processes = [||]
    Alarms = [||]
    SampledAtMs = 1759420800000L
    RecordAtMs = 0L
  }
  Health {
    DaemonHealth.Revision = 2L
    Version = "v"
    ProcessId = 1
    McpPort = 47759
    Overall = "Degraded"
    MemoryPressure = "Critical"
    MemoryPressureNote = "7.0% of RAM available — critical is entered at or below 8% and clears at 15%; expensive work is queued until pressure eases"
    ResidentBytes = 55_000_000_000L
    MachineAvailableBytes = 1L
    MachineTotalBytes = 2L
    CpuPercent = 99.5
    Processes = [|
      { ProcessSample.ProcessId = 1; Role = "daemon"; ResidentBytes = 1L; CpuPercent = 0.0 }
      { ProcessSample.ProcessId = 2; Role = "composer-worker"; ResidentBytes = Int64.MaxValue; CpuPercent = 12.5 }
    |]
    Alarms = [| { HealthAlarm.Signal = "WorkerRss"; State = "Broken"; Message = "RSS ↑" } |]
    SampledAtMs = Int64.MaxValue
    RecordAtMs = -1L
  }
  Leases { LeaseBoard.Revision = 0L; Active = [||]; Queued = [||] }
  Leases {
    LeaseBoard.Revision = 9007199254740993L
    Active = [|
      { LeaseGrant.Id = "lease-1"; Kind = "full_build"; Holder = "gate-command-1"; GrantedAtMs = 1759420800000L; ExpiresAtMs = Int64.MaxValue }
    |]
    Queued = [| { LeaseWait.Kind = "test_suite_run"; Holder = "agent-ü"; RequestedAtMs = -1L } |]
  }
  Operations { OperationsBoard.Revision = 0L; Incarnation = ""; StorageError = None; Entries = [||] }
  Operations {
    StorageError = None
    OperationsBoard.Revision = 9007199254740993L
    Incarnation = "daemon-ü"
    Entries = [|
      { OperationEntry.Sequence = Int64.MaxValue; AtMs = -1L; CallerLabel = "mcp:3f9c 🎼"; Operation = "run"; Session = "s"; Project = "/p/Ü.fidproj"
        Ok = true; Code = ""; Message = ""; Lease = None
        Run = Some { OperationRun.ExitCode = -2147483648; Compiler = Some { Generation = 7L; SourceVersion = "src" }; StandardOutput = "[cut: 5 characters omitted]\nout\u0000"; StandardError = "err — 日本"; Dropped = false } }
      { OperationEntry.Sequence = 1L; AtMs = 0L; CallerLabel = "http"; Operation = "build"; Session = ""; Project = ""; Ok = false; Code = "invalid_reservation"; Message = "⚠"; Run = None; Lease = None }
      { OperationEntry.Sequence = 2L; AtMs = 1L; CallerLabel = "browser"; Operation = "run"; Session = "s"; Project = "p"; Ok = true; Code = ""; Message = ""
        Lease = None; Run = Some { OperationRun.ExitCode = 0; Compiler = Some { Generation = 0L; SourceVersion = "" }; StandardOutput = ""; StandardError = ""; Dropped = true } } |] }
  Uptime { UptimeTick.UptimeMs = 0L }
  Uptime { UptimeTick.UptimeMs = 93784005L }
  Uptime { UptimeTick.UptimeMs = Int64.MaxValue }
  Resources {
    Sequence = 1L; Status = "complete"; SampledAtMs = 123L; IntervalMs = 2000.; SampleCostMs = 12.5
    Metrics = [||]; Processes = [||]; ProcessCount = 0; UnreadableCount = 0; OmittedCount = 0; Note = "test" }
  AgentWork { Incarnation = "daemon-a"; Sequence = 1L; ExecutionHost = "local"; Capacity = 256; Runs = [||]; Presence = [||] }
  AgentWork { Incarnation = "daemon-b"; Sequence = 2L; ExecutionHost = "local"; Capacity = 256; Runs = [||]
              Presence = [| { Name = "pi"; Source = "lease"; Member = "pi · /repo/clef"; Project = "/repo/clef"; Detail = "holds full_build"; SinceMs = 1759420800000L } |] }
]

/// The frames bozzetto-web's Fable codecs produce for the samples above:
let private caseName (value: obj) (unionType: Type) =
  let case, _ = FSharpValue.GetUnionFields(value, unionType)
  case.Name

// ── Generators ─────────────────────────────────────────────────────────────

let private bytes count = Gen.arrayOfLength count (Gen.choose (0, 255) |> Gen.map byte)

/// Well-formed text with every escape the wire must carry.
let private wireText =
  Gen.elements [ "a"; "Z"; "0"; " "; "\""; "\\"; "/"; "\u0000"; "\u001b"; "\u007f"; "\n"; "\r"; "\t"; "é"; "日本"; "🎼"; " "; "�"; "</script>" ]
  |> Gen.listOf
  |> Gen.map (String.concat "")

let private finite =
  Gen.oneof [
    Gen.elements [ 0.0; -0.0; 0.1; 1e-7; 1e21; 5e-324; Double.MaxValue; Double.MinValue; Double.Epsilon; 123456.789 ]
    bytes 8 |> Gen.map (fun b -> BitConverter.ToDouble(b, 0)) |> Gen.filter Double.IsFinite
  ]

let private extremeInt64 =
  Gen.oneof [
    Gen.elements [ Int64.MinValue; Int64.MaxValue; 0L; -1L; 9007199254740993L ]
    bytes 8 |> Gen.map (fun b -> BitConverter.ToInt64(b, 0))
  ]

type private WireGenerators =
  static member Text() : Arbitrary<string> = Arb.fromGen wireText
  static member Float() : Arbitrary<float> = Arb.fromGen finite
  static member Wide() : Arbitrary<int64> = Arb.fromGen extremeInt64

let private wireConfig = { FsCheckConfig.defaultConfig with maxTest = 200; arbitrary = [ typeof<WireGenerators> ] }

// ── Composer fixtures ──────────────────────────────────────────────────────

let private authority session generation : Authority =
  { Authority.Host = "h"; Session = session; Provider = ProviderIdentity.ClefComposer; Epoch = "e"; Generation = generation }

let private reply session outcome : Reply =
  { Reply.ProtocolVersion = 1us; RequestId = "request"; Authority = authority session 4L; Outcome = outcome }

let private accepted : AcceptedArtifact =
  { AcceptedArtifact.Generation = 3L; SourceVersion = "v3"; ArtifactPath = "/external/a.out"; ArtifactSha256 = "sha"
    ObjectManifest = "/external/objects.json"; ChangedWitnesses = [| "w1"; "w2" |]; RetainedWitnesses = [||]
    RetiredWitnesses = [||]; WitnessVisits = [||]; CompiledObjects = [| "o1"; "o2" |]; ReusedObjects = [| "o3" |]
    RetiredObjects = [||] }

let private observed : SessionSnapshot =
  { SessionSnapshot.Observation = 9UL; Project = "/p/Main.fidproj"; ManifestPath = "/external/current.json"; Closed = false
    Busy = true; Current = Some accepted; RevocationPending = true; BackendError = Some "backend"
    FormatterError = Some "formatter"; FormatterCleanupPending = true; WorkerRetirementRequired = Some "retire"
    CleanupPending = true; CleanupError = Some "cleanup" }

let private response outcome : ComposerResponse =
  { ComposerResponse.Reply = reply "s1" outcome; StatusFresh = true; StatusError = None; WorkerAvailable = true; WorkerError = None }

let private hello : Reply =
  { Reply.ProtocolVersion = 1us; RequestId = "hello"; Authority = authority "" 0L
    Outcome =
      Result.Ok(HelloAccepted {
        HelloResult.Agreement = { Agreement.ProtocolVersion = 2us; Encoding = EncodingId.BAREWire1; ContractDigest = "digest" }
        Compiler = { CompilerIdentity.AssemblyPath = "/external/Composer.dll"; Sha256 = "compiler"; Version = "0.0.2+abc" }
        Psg = { PsgIdentity.Schema = 1; AssemblySha256 = "psg"; FormatVersion = 1u; ContractFingerprint = "fingerprint" }
        Operations = [| Operation.Open |]
        InMemoryPatchAllowed = false }) }

/// The daemon's own observation types, constructed where their labels are in scope.
module private Readings =
  open Bozzetto.Features
  open Bozzetto.Features.HealthAnomaly
  open Bozzetto.Features.MachineMemory
  open Bozzetto.Server.DaemonTelemetry

  let machine: Stats = { TotalBytes = 64L; AvailableBytes = 32L }

  let drifting =
    Verdict.Drifting {
      Signal = SignalId.WorkerRss; ObservedAt = DateTimeOffset.UnixEpoch; ObservedValue = 9.0
      BaselineMean = 1.0; BaselineStdDev = 1.0; Direction = SignalDirection.Increased
      DeviationInSigmas = 8.0; SustainedFor = TimeSpan.FromSeconds 3.; SamplesSustained = 3 }

  let health: HealthSnapshot =
    { DaemonPid = 77; DaemonPort = 47749; Uptime = TimeSpan.FromSeconds 12.5; Version = "9.9"; SessionSummaries = []
      LiveTestingSummary = None; MemoryMB = 3; GcDumpOutcome = None; MemoryPressure = Bozzetto.MemoryPressure.Tight
      Anomalies = [ Verdict.Normal; drifting ] }

  let telemetry: Snapshot =
    { SampledAt = DateTimeOffset.UnixEpoch; AggregateResidentBytes = 10L; AggregateCpuPercent = nan
      Processes = [ { ProcessId = 77; Role = "daemon"; ResidentBytes = 10L; CpuTime = TimeSpan.Zero; CpuPercent = infinity } ] }

  /// One reading as the health tick publishes it: the fact Health derives from.
  let tick serial level : Bozzetto.Server.HealthTick.Tick =
    { Serial = serial; SampledAtMs = serial * 1000L; Machine = machine; Level = level; Record = None; Telemetry = None; RecordAtMs = 0L }

let private sampleHealth = {
  DaemonHealth.Revision = 7L
  Version = "0.6.834-test"
  ProcessId = 99
  McpPort = 47759
  Overall = "Healthy"
  MemoryPressure = "Normal"
  MemoryPressureNote = ""
  ResidentBytes = 1L
  MachineAvailableBytes = 2L
  MachineTotalBytes = 3L
  CpuPercent = 0.5
  Processes = [||]
  Alarms = [||]
  SampledAtMs = 0L
  RecordAtMs = 0L
}

// ── Fakes for the connection contract ──────────────────────────────────────

/// A page at the other end of the socket. Records what the bridge wrote and
/// whether anything was written after the bridge closed the socket.
type private FakeSocket() =
  let inbound = Channel.CreateUnbounded<Inbound>()
  let written = Channel.CreateUnbounded<byte array>()
  let mutable closed = 0
  let mutable lateWrites = 0
  member _.Deliver(payload: byte array) = inbound.Writer.TryWrite(Inbound.Binary payload) |> ignore
  member _.DeliverUnusable(code: string, message: string) = inbound.Writer.TryWrite(Inbound.Unusable(code, message)) |> ignore
  member _.Hangup() = inbound.Writer.TryWrite Inbound.Closed |> ignore
  member _.LateWrites = Volatile.Read &lateWrites
  member _.WasClosed = Volatile.Read &closed = 1
  member _.Waiting = written.Reader.Count
  /// The next frame the bridge wrote, decoded by the UI twin.
  member _.Next() : Task<Event> = task {
    use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 5.)
    let! text = written.Reader.ReadAsync(timeout.Token)
    return
      match Ui.decodeEvent text with
      | Ok event -> event
      | Error message -> failtestf "the page cannot decode the %d-byte frame: %s" text.Length message
  }
  interface IBridgeSocket with
    member _.Receive(cancellation) = inbound.Reader.ReadAsync(cancellation).AsTask()
    member _.Send(text, _) =
      if Volatile.Read &closed = 1 then Interlocked.Increment &lateWrites |> ignore
      written.Writer.TryWrite text |> ignore
      Task.CompletedTask
    member _.Close(_) =
      Volatile.Write(&closed, 1)
      Task.CompletedTask
    member _.Abort() = Volatile.Write(&closed, 1)

/// The supervisor as the bridge sees it, with the daemon's fact plane fed by
/// this fake's owners. A build is held until the test releases it, so a socket
/// can close while the build is in flight. Asks of the health owner are
/// counted: the bridge may ask only for a connect or a RequestSnapshot.
type private FakeComposer(?observationOwners: Facts.ObservationProducer list) =
  let changed = Microsoft.FSharp.Control.Event<unit>()
  let buildEntered = TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously)
  let buildReleased = TaskCompletionSource<ComposerResponse>(TaskCreationOptions.RunContinuationsAsynchronously)
  let mutable revision = 1L
  let mutable refreshes = 0
  let mutable tickSerial = 1L
  let mutable poolSerial = 1L
  let graph = Facts.Graph({ Facts.Constants.McpPort = 47759; Version = "0.6.834-test"; ProcessId = 99 }, "daemon-o", "", ?observationOwners = observationOwners)
  do
    // The owners' first values, so a page's prologue carries Health and Leases.
    graph.Tick(Readings.tick tickSerial Bozzetto.MemoryPressure.Normal) |> ignore
    graph.Pool({ Serial = poolSerial; Active = []; Queue = [] }: Bozzetto.Features.LeaseWatch.Pool) |> ignore
  member val HoldBuilds = false with get, set
  /// Asks of the health owner: one per connect and one per explicit resync.
  member _.Refreshes = Volatile.Read &refreshes
  member _.Graph = graph
  member _.Revision
    with get () = Volatile.Read &revision
    and set value = Volatile.Write(&revision, value)
  member _.BuildEntered = buildEntered.Task
  member _.ReleaseBuild() = buildReleased.TrySetResult(response (Result.Ok(Built accepted))) |> ignore
  member _.Change() = changed.Trigger()
  /// The health owner publishes a reading at `level`.
  member _.PublishLevel(level: Bozzetto.MemoryPressure) =
    tickSerial <- tickSerial + 1L
    graph.Tick(Readings.tick tickSerial level) |> ignore
  /// The lease owner publishes its pool.
  member _.PublishPool(active: Bozzetto.ExpensiveWorkLease.GrantView list, queue: Bozzetto.ExpensiveWorkLease.QueueView list) =
    poolSerial <- poolSerial + 1L
    graph.Pool({ Serial = poolSerial; Active = active; Queue = queue }: Bozzetto.Features.LeaseWatch.Pool) |> ignore
  /// What the plane projects for the latest facts, stamped as it stamps.
  member _.CurrentHealth = graph.Current() |> List.pick (function Health h -> Some h | _ -> None)
  member _.CurrentLeases = graph.Current() |> List.pick (function Leases l -> Some l | _ -> None)
  member this.Directory() : ComposerDirectory =
    { Configured = true; Revision = this.Revision; Worker = Some hello
      Sessions = [| response (Result.Ok(Observed observed)) |] }
  member this.Sources : BridgeSources = {
    DaemonVersion = "0.6.834-test"
    StartedAtMs = 1759420800123L
    Execute =
      fun request cancellation ->
        match request with
        | RequestBody.Open _ -> Task.FromResult(response (Result.Ok(Opened { OpenResult.Observation = 1UL; Project = "/p/Main.fidproj"; ManifestPath = "/m" })))
        | RequestBody.Reserve _ -> Task.FromResult(response (Result.Ok(Reserved { ReserveResult.Reservation = "rsv" })))
        | RequestBody.Build _ when this.HoldBuilds ->
          buildEntered.TrySetResult cancellation |> ignore
          buildReleased.Task
        | RequestBody.Build _ -> Task.FromResult(response (Result.Ok(Built accepted)))
        | RequestBody.Run _ ->
          Task.FromResult(response (Result.Ok(Ran { RunResult.Generation = 3L; SourceVersion = "v3"; ExitCode = 3; StandardOutput = "out"; StandardError = "err" })))
        | RequestBody.Cancel _ -> Task.FromResult(response (Result.Error { Refusal.Code = RefusalCode.Busy; Message = "busy now" }))
        | _ -> Task.FromException<ComposerResponse>(InvalidOperationException "worker connection lost")
    Directory = fun _ -> Task.FromResult(this.Directory())
    CachedDirectory = fun () -> this.Directory()
    WorkerPid = fun () -> Some 4242
    Changed = changed.Publish :> IObservable<unit>
    Facts = { Register = graph.Register; Current = graph.Current; Refresh = fun () -> Interlocked.Increment &refreshes |> ignore }
  }

/// The snapshot spacing, held by the test. Each pause the hub enters is
/// announced, after the push before it, and lasts until the test releases it.
/// While a pause is held the hub can push nothing more, so a test can count
/// pushes exactly, with no clock.
type private PauseGate() =
  let entered = Channel.CreateUnbounded<TaskCompletionSource<unit>>()
  member _.Pause(_: TimeSpan) : Async<unit> = async {
    let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    entered.Writer.TryWrite release |> ignore
    let! cancellation = Async.CancellationToken
    do! release.Task.WaitAsync(cancellation) |> Async.AwaitTask
  }
  /// The next pause the hub entered: it has pushed, and waits for this release.
  member _.Entered() : Task<TaskCompletionSource<unit>> = task {
    use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 5.)
    return! entered.Reader.ReadAsync(timeout.Token)
  }

/// One period of the uptime clock the test holds: the hub's clock has begun
/// it and waits for the test to tick it. Stopped completes when the hub stops
/// the clock during this period.
type private ClockPeriod = { Sequence: int; Tick: TaskCompletionSource<int64>; Stopped: Task }

/// The uptime clock, held by the test. Each period the hub's clock begins is
/// announced and lasts until the test ticks it with an uptime, so a test
/// counts ticks and pushes exactly while no time passes.
type private ClockGate() =
  let entered = Channel.CreateUnbounded<ClockPeriod>()
  let mutable started = 0
  /// Periods begun so far, ticked or not.
  member _.Started = Volatile.Read &started
  /// Periods begun that the test has not yet taken with Entered.
  member _.Pending = entered.Reader.Count
  member _.Tick: Async<int64> = async {
    let! cancellation = Async.CancellationToken
    let tick = TaskCompletionSource<int64>(TaskCreationOptions.RunContinuationsAsynchronously)
    let stopped = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    cancellation.Register(fun () -> stopped.TrySetResult() |> ignore) |> ignore
    let sequence = Interlocked.Increment &started
    entered.Writer.TryWrite { Sequence = sequence; Tick = tick; Stopped = stopped.Task } |> ignore
    return! tick.Task.WaitAsync(cancellation) |> Async.AwaitTask
  }
  /// The next period the hub's clock began.
  member _.Entered() : Task<ClockPeriod> = task {
    use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 5.)
    return! entered.Reader.ReadAsync(timeout.Token)
  }

let private quiet = BridgeOptions.defaults

let private unpaused (_: TimeSpan) : Async<unit> = async { return () }

/// A hub over the fake supervisor whose uptime clock ticks only when the test
/// ticks it.
let private hubOf (composer: FakeComposer) (stopping: CancellationToken) (pause: TimeSpan -> Async<unit>) (clock: ClockGate) =
  Hub(composer.Sources, quiet, stopping, pause, clock.Tick)

let private uptimeOf (event: Event) =
  match event with
  | Uptime tick -> Some tick.UptimeMs
  | _ -> None

/// The tab icons as clef-lang-site ships them (bozzetto-web/icons): route,
/// content type and SHA-256 of the exact bytes.
let private clefIcons = [
  "/favicon.svg", "image/svg+xml", "3b619e4a8a1af51a05ddbda031c0dd5593c9dd26e41df7f4e0a5db5bd4c3748b"
  "/favicon-dark.svg", "image/svg+xml", "cdef81791d293f81f42ddff7f81d7078ece768554d4732ce71e375b42b85601a"
  "/favicon.ico", "image/x-icon", "90ebd38f6e0dfdab67c93c62d3270a48e55c3509d6ed82345db9488b524943bf"
]

/// A loopback Kestrel host on an ephemeral port, running only the routes
/// `map` adds.
module private Hosting =
  open Microsoft.AspNetCore.Builder
  open Microsoft.AspNetCore.Hosting
  open Microsoft.AspNetCore.Hosting.Server
  open Microsoft.AspNetCore.Hosting.Server.Features
  open Microsoft.Extensions.DependencyInjection
  open Microsoft.Extensions.Logging

  let start (map: WebApplication -> unit) : Task<WebApplication * string> = task {
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
    builder.Logging.ClearProviders() |> ignore
    let app = builder.Build()
    map app
    do! app.StartAsync()
    let addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses
    return app, Seq.head addresses
  }

  let stop (app: WebApplication) =
    (app :> IAsyncDisposable).DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds 10.) |> ignore

  /// Status, content type and body of one GET.
  let get (client: Net.Http.HttpClient) (url: string) : Task<int * string * byte array> = task {
    use! response = client.GetAsync(url)
    let! body = response.Content.ReadAsByteArrayAsync()
    return int response.StatusCode, string response.Content.Headers.ContentType, body
  }

let private frame correlation (command: Command) =
  Ui.encodeCommand { CommandFrame.Correlation = correlation; Command = command }

let private correlationOf (event: Event) =
  match event with
  | Accepted a -> Some a.Correlation
  | Refused r -> Some r.Correlation
  | _ -> None

let private readPrologue (socket: FakeSocket) = task {
  let! first = socket.Next()
  let! second = socket.Next()
  let! third = socket.Next()
  let! fourth = socket.Next()
  return [ first; second; third; fourth ]
}

/// Read frames until `enough` holds of everything read so far.
let private readUntil (socket: FakeSocket) (enough: Event list -> bool) = task {
  let mutable seen = []
  while not (enough seen) do
    let! next = socket.Next()
    seen <- seen @ [ next ]
  return seen
}

let private settle (running: Task) = running.WaitAsync(TimeSpan.FromSeconds 5.)

[<Tests>]
let tests =
  testList "UI bridge" [

    testList "binary codec" [
      test "fixed command golden binds envelope and primitive byte order" {
        let frame = { CommandFrame.Correlation = 1; Command = RequestSnapshot }
        let golden = [| 0x42uy; 0x5Auy; 0x57uy; 0x31uy; 10uy; 1uy; 1uy; 0uy; 0uy; 0uy; 0uy |]
        Ui.encodeCommand frame |> Expect.equal "exact independently specified bytes" golden
        Codec.decodeCommand golden |> Expect.equal "golden decoded" (Ok frame)
      }
      test "every command and event fixture round trips" {
        for command in ObserveNodeResources { Node = "one"; Seconds = 120 } :: commands do
          let frame = { CommandFrame.Correlation = 7; Command = command }
          Codec.decodeCommand (Ui.encodeCommand frame) |> Expect.equal "command" (Ok frame)
        for event in events do
          Ui.decodeEvent (Codec.encodeEvent event) |> Expect.equal "event" (Ok event)
      }
      testPropertyWithConfig wireConfig "finite floats and exact int64 survive typed binary" <|
        fun (value: float) (stamp: int64) ->
          let event = Resources {
            Sequence = stamp; Status = "observing"; SampledAtMs = stamp
            IntervalMs = value; SampleCostMs = value; Metrics = [||]; Processes = [||]
            ProcessCount = 0; UnreadableCount = 0; OmittedCount = 0; Note = ""
          }
          Ui.decodeEvent (Codec.encodeEvent event) = Ok event
      test "invalid envelopes and trailing data refuse with precise reasons" {
        let golden = Ui.encodeCommand { Correlation = 1; Command = RequestSnapshot }
        let change index value = let copy = Array.copy golden in copy[index] <- value; copy
        let cases = [
          golden[..3], "truncated"
          change 0 0uy, "magic"
          change 4 8uy, "version"
          change 5 2uy, "family"
          change 10 127uy, "unknown Command tag"
          Array.append golden [| 0uy |], "trailing"
          Array.zeroCreate (Bozzetto.Web.Shared.BinaryCodec.MaxFrameBytes + 1), "too large"
        ]
        for bytes, reason in cases do
          match Codec.decodeCommand bytes with
          | Ok _ -> failtestf "accepted malformed %s" reason
          | Error error -> error.Message |> Expect.stringContains "specific refusal" reason
      }
      test "UTF8 length and scalar validation never repairs malformed input" {
        let prefix = [| 0x42uy; 0x5Auy; 0x57uy; 0x31uy; 9uy; 1uy; 1uy; 0uy; 0uy; 0uy; 3uy |]
        for body, reason in [ [| 2uy; 0xC0uy; 0xAFuy |], "UTF-8"; [| 1uy; 0xC2uy |], "UTF-8"; [| 3uy; 0xEDuy; 0xA0uy; 0x80uy |], "UTF-8"; [| 127uy |], "length"; [| 0x80uy; 0uy |], "noncanonical" ] do
          match Codec.decodeCommand (Array.append prefix body) with
          | Error refusal -> refusal.Message |> Expect.stringContains "precise malformed string" reason
          | Ok _ -> failtest "malformed UTF8 admitted"
        let command = { Correlation = 1; Command = OpenProject "日本語 🎼\u0000" }
        Codec.decodeCommand (Ui.encodeCommand command) |> Expect.equal "unicode scalar/control preserved" (Ok command)
      }
      test "peer identity work heartbeat and unavailability stay typed" {
        let source = { NodeIdentity.Node = "one"; Incarnation = "epoch-1" }
        let work : WorkSnapshot = {
          TranscriptsOmitted = false
          Sequence = 9007199254740993L; SampledAtMs = 123L
          Snapshot = { Revision = 2L; Worker = Idle; Sessions = [||] }
          Leases = { Revision = 3L; Active = [||]; Queued = [||] }
          Operations = { Revision = 4L; Incarnation = "j"; StorageError = None; Entries = [||] }
        }
        let resources = { Sequence = 5L; Status = "unavailable"; SampledAtMs = 123L; IntervalMs = 0.; SampleCostMs = 0.
                          Metrics = [||]; Processes = [||]; ProcessCount = 0; UnreadableCount = 0; OmittedCount = 0; Note = "unsupported" }
        for value in [ PeerHello source; PeerWork work; PeerResources resources; PeerUnavailable "disconnected"; PeerHeartbeat { UptimeMs = 77L } ] do
          Bozzetto.Web.Shared.BinaryCodec.decodePeerFrame (Bozzetto.Web.Shared.BinaryCodec.encodePeerFrame value)
          |> Expect.equal "peer case preserved" (Ok value)
        for value in [ WorkNodes { Revision = 8L; Nodes = [| { Source = source; Local = false; Status = "unavailable"; Note = "last observed"; Value = Some work } |] }; NodeResources { Source = source; Correlation = 7; Value = resources } ] do
          Ui.decodeEvent (Codec.encodeEvent value) |> Expect.equal "scoped browser event" (Ok value)
        let request = { PeerObserveRequest.ProtocolVersion = ProtocolVersion; Node = "one"; Topic = "work"; Seconds = 0 }
        Bozzetto.Web.Shared.BinaryCodec.decodePeerObserveRequest (Bozzetto.Web.Shared.BinaryCodec.encodePeerObserveRequest request)
        |> Expect.equal "bounded request" (Ok request)
        Bozzetto.Web.Shared.BinaryCodec.decodePeerObserveRequest (Array.zeroCreate 4097)
        |> Expect.equal "request cap before parsing" (Error "binary: frame too large")
      }
    ]

    testList "command observation codec" [
      test "private typed observations preserve arguments and refuse legacy or browser frames" {
        let report : CommandObservationRequest = {
          Holder = "agent · /repo"; Project = "/repo"; Kind = "test_suite_run"; LeaseId = "private-token"
          RunId = "run"; Executable = "/sdk/dotnet"; Arguments = [|"vstest"; "path with space"; ""|]
          ProcessId = 4321; StartedAtMs = 1L; Observation = CommandStarted }
        let result = { FinishedAtMs = 2L; ExitCode = 1; StandardOutput = "out"; StandardError = "err"
                       OutputTruncated = true; ErrorTruncated = false }
        for value in [report; {report with Observation = CommandFinished result}] do
          let bytes = Bozzetto.Web.Shared.BinaryCodec.encodeCommandObservationRequest value
          Bozzetto.Web.Shared.BinaryCodec.decodeCommandObservationRequest bytes |> Expect.equal "exact command context" (Ok value)
          Bozzetto.Web.Shared.BinaryCodec.decodeCommand bytes |> Expect.equal "private reporting is not a browser command" (Error "binary: wrong message family")
          let old = Array.copy bytes
          old[4] <- byte LegacyProtocolVersion
          Bozzetto.Web.Shared.BinaryCodec.decodeCommandObservationRequest old |> Expect.equal "no invented v9 reporting" (Error "binary: unsupported version")
        Bozzetto.Web.Shared.BinaryCodec.decodeCommandObservationRequest [|123uy;125uy|] |> Result.isError |> Expect.isTrue "no JSON fallback"
        Bozzetto.Web.Shared.BinaryCodec.decodeCommandObservationRequest (Array.zeroCreate (MaxCommandObservationBytes + 1))
        |> Expect.equal "bounded before decoding" (Error "binary: frame too large")
        let accepted = [|66uy;90uy;87uy;49uy;10uy;8uy;0uy;7uy;0uy;0uy;0uy;0uy;0uy;0uy;0uy|]
        Bozzetto.Web.Shared.BinaryCodec.encodeCommandObservationReply (CommandObservationAccepted 7L) |> Expect.equal "independent reply golden" accepted
        Bozzetto.Web.Shared.BinaryCodec.decodeCommandObservationReply accepted |> Expect.equal "golden reply decode" (Ok(CommandObservationAccepted 7L))
      }
    ]

    testList "projections" [
      test "leased commands retain process and result without compiler authority" {
        let command : Bozzetto.ComposerIntegration.ComposerJournal.LeasedCommand = {
          Kind = Bozzetto.ComposerIntegration.ComposerJournal.LeasedKind.Test
          LeaseId = "private-release-capability"; LeaseDisplayId = "lease-display"; RunId = "run"; Executable = "/sdk/dotnet"; Arguments = ["vstest"; "/repo/tests.dll"]
          ProcessId = 4321; StartedAtMs = 10L }
        let entry : Bozzetto.ComposerIntegration.ComposerJournal.Entry = {
          Sequence = 7L; AtMs = 10L; Caller = "tester"; Project = "/repo/clef"
          Context = Bozzetto.ComposerIntegration.ComposerJournal.Context.LeasedCommand command
          Outcome = Bozzetto.ComposerIntegration.ComposerJournal.Outcome.Running; Run = None }
        let journal : Bozzetto.ComposerIntegration.ComposerJournal.Journal = {
          Incarnation = "journal"; Sequence = 1L; Entries = [entry]; StorageError = None }
        let started = (Project.operations 1L journal).Entries[0]
        (started.Operation, started.Project, started.Session, started.Code, started.Run)
        |> Expect.equal "running test is not a compiler session or successful run" ("test_suite_run", "/repo/clef", "", "running", None)
        started.Lease |> Option.map (fun value -> value.LeaseId, value.RunId, value.ProcessId, value.Arguments)
        |> Expect.equal "exact reported process context" (Some("lease-display", "run", 4321, [|"vstest"; "/repo/tests.dll"|]))
        let finished = { entry with AtMs = 20L; Outcome = Bozzetto.ComposerIntegration.ComposerJournal.Outcome.Failed "exit 1"
                                    Run = Some { ExitCode = 1; Compiler = None; StandardOutput = "failed"; StandardError = "detail"
                                                 OutputTruncated = false; ErrorTruncated = false; Dropped = false } }
        let board = Project.operations 2L { journal with Sequence = 2L; Entries = [finished] }
        let settled = board.Entries[0]
        (settled.Sequence, settled.Lease, settled.Run |> Option.bind _.Compiler)
        |> Expect.equal "settlement preserves one operation and never invents compiler evidence" (started.Sequence, started.Lease, None)
        settled.Run |> Option.map _.ExitCode |> Expect.equal "actual failure retained" (Some 1)
        let wire = Codec.encodeEvent (Operations board)
        Encoding.UTF8.GetString(wire).Contains command.LeaseId |> Expect.isFalse "the release capability never leaves the journal owner"
        Ui.decodeEvent wire |> Expect.equal "typed command context survives binary projection" (Ok(Operations board))
      }

      test "commands become the requests the MCP tools send" {
        let address = { SessionAddress.Worker = { WorkerAddress.Host = "host-α"; Epoch = "epoch-0001"; Provider = ProviderIdentity.ClefComposer }; Session = "session-日本-🎼" }
        let anyWorker = { WorkerAddress.Host = ""; Epoch = ""; Provider = ProviderIdentity.ClefComposer }
        let requests = commands |> List.map Project.toRequest
        requests
        |> Expect.equal "one Composer operation per command; RequestSnapshot is the bridge's own" [
          None
          Some(RequestBody.Open(anyWorker, "/abs/Ünï 🎼.fidproj"))
          Some(RequestBody.Open(anyWorker, ""))
          Some(RequestBody.Reserve(address, "edit: \"main\" — ✓"))
          Some(RequestBody.Build(address, "rsv-0123"))
          Some(RequestBody.Run(address, [||]))
          Some(RequestBody.Run(address, [| "a b"; "\"q\""; "🎼"; ""; "\\" |]))
          Some(RequestBody.Cancel address)
          Some(RequestBody.Close address)
          Some(RequestBody.PrepareCompilerChange address.Worker)
          None // Resource observations never reach the compiler worker.
          None
        ]
      }

      test "each reply becomes exactly one outcome, a run's transcript first" {
        let target = Project.targetOf (authority "s1" 4L)
        let outcome body = Project.outcome 5 (response (Result.Ok body))
        let acceptance completion = Accepted { CommandAcceptance.Correlation = 5; Target = target; Completion = completion }
        outcome (Opened { OpenResult.Observation = 1UL; Project = "/p"; ManifestPath = "/m" }) |> Expect.equal "opened" [ acceptance SessionOpened ]
        outcome (Reserved { ReserveResult.Reservation = "rsv" }) |> Expect.equal "reserved" [ acceptance (EditReserved "rsv") ]
        outcome (Built accepted)
        |> Expect.equal "built, summarized with counts" [
          acceptance (ReservationBuilt {
            ArtifactSummary.Generation = 3L; SourceVersion = "v3"; ArtifactPath = "/external/a.out"; ArtifactSha256 = "sha"
            Regions = [|
              { RegionRow.Identity = "o1"; Witness = ""; Visits = 0; Object = "compiled" }
              { RegionRow.Identity = "o2"; Witness = ""; Visits = 0; Object = "compiled" }
              { RegionRow.Identity = "o3"; Witness = ""; Visits = 0; Object = "reused" }
              { RegionRow.Identity = "w1"; Witness = "changed"; Visits = 0; Object = "" }
              { RegionRow.Identity = "w2"; Witness = "changed"; Visits = 0; Object = "" } |] }) ]
        outcome (Ran { RunResult.Generation = 3L; SourceVersion = "v3"; ExitCode = 3; StandardOutput = "out"; StandardError = "err" })
        |> Expect.equal "transcript, then the exit code" [
          RunOutput { RunTranscript.Correlation = 5; Target = target; Generation = 3L; SourceVersion = "v3"; ExitCode = 3; StandardOutput = "out"; StandardError = "err" }
          acceptance (RunFinished 3) ]
        outcome Canceled |> Expect.equal "canceled" [ acceptance WorkCanceled ]
        outcome (Closed { CloseResult.Observation = 2UL; Closed = true; CleanupPending = false; CleanupError = None }) |> Expect.equal "closed" [ acceptance SessionClosed ]
        outcome (CompilerRetired { RetirementResult.RestartRequired = true; InMemoryPatchAllowed = false }) |> Expect.equal "retired" [ acceptance WorkerRetired ]
        match outcome (Observed observed) with
        | [ Refused refusal ] -> refusal.Code |> Expect.equal "a reply no command produces is a contract mismatch" "contract_mismatch"
        | other -> failtestf "unexpected %A" other
        Project.outcome 6 (response (Result.Error { Refusal.Code = RefusalCode.InvalidReservation; Message = "used" }))
        |> Expect.equal "a refusal keeps Composer's code and the authority" [
          Refused { CommandRefusal.Correlation = 6; Target = target; Code = "invalid_reservation"; Message = "used" } ]
      }

      test "the directory becomes the snapshot the page renders" {
        let empty = { Configured = false; Revision = 3L; Worker = None; Sessions = [||] }
        (Project.snapshot empty None).Worker |> Expect.equal "no worker path configured" Unconfigured
        (Project.snapshot { empty with Configured = true } None).Worker |> Expect.equal "configured, nothing running" Idle
        let live = { empty with Configured = true; Worker = Some hello; Sessions = [| { response (Result.Ok(Observed observed)) with StatusFresh = false; StatusError = Some "reading"; WorkerError = Some "gone" } |] }
        let snapshot = Project.snapshot live (Some 4242)
        snapshot.Revision |> Expect.equal "the supervisor's revision" 3L
        snapshot.Worker
        |> Expect.equal "the live worker" (Running { WorkerInfo.Target = { WorkerTarget.Host = "h"; Epoch = "e" }; CompilerVersion = "0.0.2+abc"; ProcessId = Some 4242 })
        snapshot.Sessions
        |> Expect.equal "every observed field, and the supervisor's own view of it" [|
          { SessionStatus.Target = { SessionTarget.Worker = { WorkerTarget.Host = "h"; Epoch = "e" }; Session = "s1" }
            Generation = 4L; Project = "/p/Main.fidproj"; Closed = false; Busy = true; StatusFresh = false
            RevocationPending = true; CleanupPending = true; FormatterCleanupPending = true
            Current = Some { ArtifactSummary.Generation = 3L; SourceVersion = "v3"; ArtifactPath = "/external/a.out"; ArtifactSha256 = "sha"; Regions = [|
              { RegionRow.Identity = "o1"; Witness = ""; Visits = 0; Object = "compiled" }
              { RegionRow.Identity = "o2"; Witness = ""; Visits = 0; Object = "compiled" }
              { RegionRow.Identity = "o3"; Witness = ""; Visits = 0; Object = "reused" }
              { RegionRow.Identity = "w1"; Witness = "changed"; Visits = 0; Object = "" }
              { RegionRow.Identity = "w2"; Witness = "changed"; Visits = 0; Object = "" } |] }
            BackendError = Some "backend"; FormatterError = Some "formatter"; CleanupError = Some "cleanup"
            WorkerRetirementRequired = Some "retire"; StatusError = Some "reading"; WorkerError = Some "gone" } |]
        let unobserved = Project.sessionStatus (response (Result.Ok(Reserved { Reservation = "rsv" })))
        unobserved.StatusFresh |> Expect.isFalse "an entry without an observation is never fresh"
        unobserved.StatusError |> Expect.isSome "and says why"
      }

      test "health reads the daemon's own observations, spelled as Protocol.fs spells them" {
        let sources = { HealthSources.McpPort = 47759; Version = "fallback"; ProcessId = 11; Health = None; Telemetry = None; Machine = Readings.machine; Level = Bozzetto.MemoryPressure.Normal; SampledAtMs = 0L; RecordAtMs = 0L }
        let cold = Project.health sources
        (cold.Version, cold.ProcessId, cold.McpPort, cold.Overall, cold.MemoryPressure)
        |> Expect.equal "before the first health tick" ("fallback", 11, 47759, "Unknown", "Normal")
        (cold.MachineAvailableBytes, cold.MachineTotalBytes) |> Expect.equal "machine memory" (32L, 64L)
        let warm = Project.health { sources with Health = Some Readings.health; Telemetry = Some Readings.telemetry; Level = Bozzetto.MemoryPressure.Tight }
        (warm.Version, warm.ProcessId, warm.McpPort) |> Expect.equal "the health snapshot" ("9.9", 77, 47749)
        (warm.Overall, warm.MemoryPressure) |> Expect.equal "tight memory degrades the daemon" ("Degraded", "Tight")
        warm.ResidentBytes |> Expect.equal "MiB to bytes" (3L * 1_048_576L)
        warm.CpuPercent |> Expect.equal "a non-finite reading is 0, which the page can decode" 0.0
        warm.Processes |> Expect.equal "processes" [| { ProcessSample.ProcessId = 77; Role = "daemon"; ResidentBytes = 10L; CpuPercent = 0.0 } |]
        warm.Alarms |> Array.map (fun a -> a.Signal, a.State) |> Expect.equal "only verdicts with evidence alarm" [| "worker_rss", "drifting" |]
        Ui.decodeEvent (Codec.encodeEvent (Health warm)) |> Expect.equal "the page decodes it" (Ok(Health warm))
      }

      test "the sampled process set names the Composer worker while one is alive" {
        Bozzetto.Server.DaemonTelemetry.ownedProcesses 10 (Some 20) [ 30, "worker:s1" ]
        |> Expect.equal "the daemon, its Composer worker, then inherited workers" [ 10, "daemon"; 20, "composer-worker"; 30, "worker:s1" ]
        Bozzetto.Server.DaemonTelemetry.ownedProcesses 10 None []
        |> Expect.equal "no live worker, no row" [ 10, "daemon" ]
      }

      test "the lease board never carries a LeaseId" {
        let now = DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero)
        let request (pool, granted) (at: DateTimeOffset, pressure, holder, kind) =
          let pool, decision = Bozzetto.ExpensiveWorkLease.request at pressure pool holder kind
          match decision with
          | Bozzetto.ExpensiveWorkLease.Decision.Granted(id, _) -> pool, Bozzetto.ExpensiveWorkLease.LeaseId.value id :: granted
          | _ -> pool, granted
        let pool, leaseIds =
          [ now, Bozzetto.MemoryPressure.Normal, "agent-a", Bozzetto.ExpensiveWorkLease.Kind.FullBuild
            now.AddSeconds 1., Bozzetto.MemoryPressure.Normal, "agent-b", Bozzetto.ExpensiveWorkLease.Kind.TestSuiteRun
            now.AddSeconds 2., Bozzetto.MemoryPressure.Tight, "agent-c", Bozzetto.ExpensiveWorkLease.Kind.Rebuild ]
          |> List.fold request (Bozzetto.ExpensiveWorkLease.empty, [])
        leaseIds.Length |> Expect.equal "two grants, one queued" 2
        let snapshot = Bozzetto.ExpensiveWorkLease.snapshot (now.AddSeconds 3.) pool
        let board = Project.leaseBoard 1L snapshot.Active snapshot.Queue
        let wire = Codec.encodeEvent (Leases board)
        for leaseId in leaseIds do
          Encoding.UTF8.GetString(wire).Contains leaseId |> Expect.isFalse "the release capability never leaves the daemon"
        board.Active |> Array.map _.Holder |> Expect.equal "grants" [| "agent-a"; "agent-b" |]
        board.Active |> Array.map _.GrantedAtMs |> Expect.equal "granted at" [| now.ToUnixTimeMilliseconds(); now.AddSeconds(1.).ToUnixTimeMilliseconds() |]
        board.Active |> Array.map _.Id |> Array.distinct |> Array.length |> Expect.equal "display ids are distinct" 2
        board.Active |> Array.forall (fun grant -> grant.Id.StartsWith "lease-") |> Expect.isTrue "display ids are labelled"
        Project.leaseBoard 1L snapshot.Active snapshot.Queue |> Expect.equal "display ids are stable" board
        board.Queued |> Expect.equal "the queued request" [| { LeaseWait.Kind = "rebuild"; Holder = "agent-c"; RequestedAtMs = now.AddSeconds(2.).ToUnixTimeMilliseconds() } |]
      }
    ]

    testList "page" [
      testTask "headless omits browser routes and refuses upgrades without constructing the UI hub" {
        do! task {
          let mutable created = false
          let! app, url = Hosting.start (fun app ->
            Microsoft.AspNetCore.Builder.WebSocketMiddlewareExtensions.UseWebSockets(app) |> ignore
            mapRoutes true app (fun () ->
              created <- true
              failwith "Headless startup must not construct the UI hub."))
          use client = new Net.Http.HttpClient(Timeout = TimeSpan.FromSeconds 10.)
          try
            created |> Expect.isFalse "no UI subscriptions or resource acquisition are started"
            for route in [ "/work"; "/composer"; "/dashboard"; "/ui/bridge"; "/api/resources" ] @ (clefIcons |> List.map (fun (route, _, _) -> route)) do
              use! response = client.GetAsync(url + route)
              int response.StatusCode |> Expect.equal (route + " is not installed") 404
            use upgrade = new Net.Http.HttpRequestMessage(Net.Http.HttpMethod.Get, url + "/ui/bridge")
            upgrade.Headers.Add("Connection", "Upgrade")
            upgrade.Headers.Add("Upgrade", "websocket")
            upgrade.Headers.Add("Sec-WebSocket-Version", "13")
            upgrade.Headers.Add("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==")
            use! response = client.SendAsync upgrade
            int response.StatusCode |> Expect.equal "the upgrade cannot reach a UI socket" 404
          finally
            Hosting.stop app
        }
      }

      testTask "headless control listener omits browser redirects but retains discovery and shutdown" {
        do! task {
          let mutable stopped = false
          let deps: Bozzetto.Server.ControlListener.ControlDeps =
            { Version = "headless-test"; McpPort = 47749; Headless = true
              GetSessionCount = (fun () -> Task.FromResult 3); Shutdown = (fun () -> stopped <- true) }
          let! app, url = Hosting.start (Bozzetto.Server.ControlListener.mapRoutes deps)
          use handler = new Net.Http.HttpClientHandler(AllowAutoRedirect = false)
          use client = new Net.Http.HttpClient(handler, Timeout = TimeSpan.FromSeconds 10.)
          try
            for route in [ "/work"; "/composer"; "/dashboard"; "/favicon.ico" ] do
              use! response = client.GetAsync(url + route)
              int response.StatusCode |> Expect.equal (route + " is not installed") 404
              response.Headers.Location |> Expect.isNull "no browser redirect is advertised"
            use! discovery = client.GetAsync(url + "/api/daemon-info")
            int discovery.StatusCode |> Expect.equal "daemon discovery remains available" 200
            let! info = discovery.Content.ReadAsStringAsync()
            match Json.parse info with
            | Ok value ->
              JsonValue.prop "sessionCount" value |> Option.bind JsonValue.tryAsInt64
              |> Expect.equal "discovery still reads the control owner" (Some 3L)
            | Error reason -> failtestf "invalid discovery response: %A" reason
            use content = new Net.Http.StringContent("{}", Encoding.UTF8, "application/json")
            use! shutdown = client.PostAsync(url + "/api/shutdown", content)
            int shutdown.StatusCode |> Expect.equal "shutdown remains available" 200
            stopped |> Expect.isTrue "shutdown still reaches the control owner"
          finally
            Hosting.stop app
        }
      }

      testTask "Work and dashboard bookmarks serve the same live Braidpoint page and policy without a Composer alias" {
        do! task {
          use stopping = new CancellationTokenSource()
          let hub = hubOf (FakeComposer()) stopping.Token unpaused (ClockGate())
          let! app, url = Hosting.start (fun app -> mapRoutes false app (fun () -> hub))
          use client = new Net.Http.HttpClient(Timeout = TimeSpan.FromSeconds 10.)
          try
            let mutable pages = []
            for route in [ "/work"; "/dashboard" ] do
              use! response = client.GetAsync(url + route)
              let! html = response.Content.ReadAsStringAsync()
              int response.StatusCode |> Expect.equal (route + " is a browser page") 200
              string response.Content.Headers.ContentType |> Expect.equal (route + " serves HTML") "text/html; charset=utf-8"
              html |> Expect.equal (route + " uses the complete live UI bundle") Bozzetto.Server.WebAssets.IndexHtml
              html |> Expect.stringContains (route + " uses the saved-choice Braidpoint theme") "data-theme=\"dark\""
              html |> Expect.stringContains (route + " retains the Braidpoint logo palette") "--bp-plum-ink"
              let policy = response.Headers.GetValues("Content-Security-Policy") |> Seq.exactlyOne
              policy |> Expect.stringContains (route + " connects to the shared live bridge") ("connect-src 'self' ws://" + Uri(url).Authority)
              policy.Contains "unsafe-inline" |> Expect.isFalse (route + " admits only its bundled code")
              response.Headers.CacheControl.NoStore |> Expect.isTrue (route + " does not cache an obsolete display")
              response.Headers.GetValues("X-Content-Type-Options") |> Seq.exactlyOne |> Expect.equal (route + " disables content guessing") "nosniff"
              pages <- (html, policy) :: pages
            pages |> List.distinct |> List.length |> Expect.equal "the bookmarks cannot drift into separate displays" 1
            use! retired = client.GetAsync(url + "/composer")
            int retired.StatusCode |> Expect.equal "the retired monitoring route has no alias" 404
          finally
            stopping.Cancel()
            Hosting.stop app
        }
      }

      testTask "control-port Work and dashboard bookmarks redirect without a Composer alias" {
        do! task {
          let deps: Bozzetto.Server.ControlListener.ControlDeps =
            { Version = "0.6.834-test"; McpPort = 47749; Headless = false; GetSessionCount = (fun () -> Task.FromResult 0); Shutdown = ignore }
          let! app, url = Hosting.start (Bozzetto.Server.ControlListener.mapRoutes deps)
          use handler = new Net.Http.HttpClientHandler(AllowAutoRedirect = false)
          use client = new Net.Http.HttpClient(handler, Timeout = TimeSpan.FromSeconds 10.)
          try
            for route in [ "/work"; "/dashboard" ] do
              use! response = client.GetAsync(url + route)
              int response.StatusCode |> Expect.equal (route + " redirects") 302
              string response.Headers.Location |> Expect.equal (route + " keeps its entry point on the MCP listener") ("http://127.0.0.1:47749" + route)
            use! retired = client.GetAsync(url + "/composer")
            int retired.StatusCode |> Expect.equal "the retired monitoring route is not redirected" 404
            retired.Headers.Location |> Expect.isNull "no fallback location is advertised"
          finally
            Hosting.stop app
        }
      }

      test "the CSP admits exactly the welded page's inline script and styles" {
        let html = Bozzetto.Server.WebAssets.IndexHtml
        let scripts = Page.inlineHashes html "script"
        let styles = Page.inlineHashes html "style"
        scripts.Length |> Expect.equal "the bundle's one module script" 1
        styles.Length |> Expect.equal "the bundle's two styles" 2
        let policy = Page.contentSecurityPolicy scripts styles (Some "127.0.0.1:47749")
        policy |> Expect.stringContains "default-src 'none'" "default-src 'none'"
        policy |> Expect.stringContains "the page's socket" "connect-src 'self' ws://127.0.0.1:47749"
        policy |> Expect.stringContains "no framing" "frame-ancestors 'none'"
        policy |> Expect.stringContains "images (the tab icons) from the page's own origin only" "img-src 'self'"
        html |> Expect.stringContains "the page links its tab icon relatively" "href=\"favicon.svg\""
        policy.Contains "unsafe-inline" |> Expect.isFalse "no inline code but the page's own"
        for hash in scripts @ styles do
          policy |> Expect.stringContains "every inline element is admitted" hash
      }

      test "inline hashes are the browser's: SHA-256 of the element text after line-break normalization" {
        Page.inlineHashes "<style></style>" "style"
        |> Expect.equal "SHA-256 of nothing" [ "'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='" ]
        Page.inlineHashes "<script type=module>a\r\nb\rc</script>" "script"
        |> Expect.equal "CRLF and CR read as LF" (Page.inlineHashes "<SCRIPT>a\nb\nc</SCRIPT>" "script")
      }

      test "only a loopback Host names the page's socket" {
        for host in [ "evil.example:47749"; "127.0.0.1:1; script-src *"; "localhost.evil.com" ] do
          Page.contentSecurityPolicy [] [] (Some host) |> Expect.stringContains host "connect-src 'self'; "
        Page.contentSecurityPolicy [] [] (Some "[::1]:47749") |> Expect.stringContains "IPv6 loopback" "ws://[::1]:47749"
      }

      testTask "both listeners serve the Clef tab icons from memory, with their content types" {
        let stopping = new CancellationTokenSource()
        let hub = hubOf (FakeComposer()) stopping.Token unpaused (ClockGate())
        let deps: Bozzetto.Server.ControlListener.ControlDeps =
          { Version = "0.6.834-test"; McpPort = 0; Headless = false; GetSessionCount = (fun () -> Task.FromResult 0); Shutdown = ignore }
        let! (mcp: Microsoft.AspNetCore.Builder.WebApplication), (mcpUrl: string) = Hosting.start (fun app -> mapRoutes false app (fun () -> hub))
        let! (control: Microsoft.AspNetCore.Builder.WebApplication), (controlUrl: string) =
          Hosting.start (Bozzetto.Server.ControlListener.mapRoutes deps)
        let client = new Net.Http.HttpClient()
        try
          let digest (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
          for route, contentType, sha in clefIcons do
            let! (status: int), (served: string), (body: byte array) = Hosting.get client (mcpUrl + route)
            (status, served) |> Expect.equal (route + " on the MCP port") (200, contentType)
            digest body |> Expect.equal (route + " is the Clef icon, byte for byte") sha
          let! (status: int), (served: string), (body: byte array) = Hosting.get client (controlUrl + "/favicon.ico")
          (status, served) |> Expect.equal "/favicon.ico on the control port" (200, "image/x-icon")
          let _, _, icoSha = clefIcons |> List.find (fun (route, _, _) -> route = "/favicon.ico")
          digest body |> Expect.equal "the same Clef icon" icoSha
        finally
          client.Dispose()
          stopping.Cancel()
          Hosting.stop mcp
          Hosting.stop control
      }

      test "a browser-shaped upgrade must carry an Origin" {
        Page.admitUpgrade (Some "http://127.0.0.1:47749") (Some "same-origin") |> Expect.isOk "the page itself"
        Page.admitUpgrade None None |> Expect.isOk "local tooling"
        Page.admitUpgrade None (Some "same-site") |> Expect.isError "a browser without an Origin"
      }
    ]

    testList "connection contract" [
      testTask "a page is welcomed, then given the snapshot, health and leases" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let hub = hubOf composer stopping.Token unpaused (ClockGate())
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! (prologue: Event list) = readPrologue socket
        prologue |> List.map (fun e -> caseName (box e) typeof<Event>) |> Expect.equal "in order" [ "Welcome"; "Snapshot"; "Health"; "Leases" ]
        match prologue.Head with
        | Welcome welcome ->
          welcome
          |> Expect.equal "protocol, daemon version and start time, once" {
            BridgeWelcome.ProtocolVersion = ProtocolVersion
            DaemonVersion = "0.6.834-test"
            StartedAtMs = 1759420800123L
          }
        | other -> failtestf "unexpected %A" other
        socket.Hangup()
        do! settle running
        hub.ConnectionCount |> Expect.equal "the hub forgets a closed page" 0
        socket.WasClosed |> Expect.isTrue "the bridge closes its end"
      }

      testTask "every command frame is answered exactly once with its correlation" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let hub = hubOf composer stopping.Token unpaused (ClockGate())
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! _ = readPrologue socket
        socket.Deliver(frame 1 (OpenProject "/p/Main.fidproj"))
        socket.Deliver(frame 2 (Reserve { ReserveEdit.Target = target; Label = "edit" }))
        socket.Deliver(frame 3 (Run { RunCurrent.Target = target; Arguments = [| "--fail" |] }))
        socket.Deliver(frame 4 (Cancel target))
        socket.Deliver(frame 5 RequestSnapshot)
        socket.Deliver(frame 6 (Build { BuildReserved.Target = target; Reservation = "rsv" }))
        socket.Deliver(frame 7 (CloseSession target))
        socket.Deliver(frame 8 (ObserveNodeResources { Node = "unknown"; Seconds = 1 }))
        socket.Deliver(Encoding.UTF8.GetBytes """{"correlation":9,"command":"requestSnapshot"}""")
        socket.DeliverUnusable("frame_too_large", "frame: too large")
        let answered (seen: Event list) = seen |> List.choose correlationOf |> List.length
        let! (seen: Event list) = readUntil socket (fun seen -> answered seen = 10)
        socket.Hangup()
        do! settle running
        let outcomes = seen |> List.choose correlationOf |> List.countBy id |> Map.ofList
        for correlation in 1..8 do
          outcomes |> Map.tryFind correlation |> Expect.equal (sprintf "command %d has one outcome" correlation) (Some 1)
        outcomes |> Map.tryFind 0 |> Expect.equal "two frames with no readable correlation" (Some 2)
        let refusedCode correlation =
          seen |> List.tryPick (function Refused r when r.Correlation = correlation -> Some r.Code | _ -> None)
        refusedCode 4 |> Expect.equal "Composer's refusal keeps its code" (Some "busy")
        refusedCode 7 |> Expect.equal "a failing operation is refused, not dropped" (Some "provider_unavailable")
        refusedCode 8 |> Expect.equal "an unavailable node observation keeps its correlation" (Some "observation_unavailable")
        let index predicate = seen |> List.findIndex predicate
        let runOutput = index (function RunOutput r -> r.Correlation = 3 | _ -> false)
        let runAccepted = index (function Accepted a -> a.Correlation = 3 | _ -> false)
        runAccepted |> Expect.equal "a run's transcript directly precedes its outcome" (runOutput + 1)
        let snapshotSent = index (function Accepted a -> a.Correlation = 5 | _ -> false)
        seen[snapshotSent - 3 .. snapshotSent - 1] |> List.map (fun e -> caseName (box e) typeof<Event>)
        |> Expect.equal "RequestSnapshot pushes fresh state before it is answered" [ "Snapshot"; "Health"; "Leases" ]
      }

      testTask "closing the page neither cancels a build nor lets anything be written after close" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer(HoldBuilds = true)
        let hub = hubOf composer stopping.Token unpaused (ClockGate())
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! _ = readPrologue socket
        socket.Deliver(frame 1 (Build { BuildReserved.Target = target; Reservation = "rsv" }))
        let! (token: CancellationToken) = composer.BuildEntered.WaitAsync(TimeSpan.FromSeconds 5.)
        socket.Hangup()
        do! settle running
        token.IsCancellationRequested |> Expect.isFalse "the page closing does not cancel its build"
        composer.ReleaseBuild()
        // The completed build's outcome must be dropped, never written to the
        // closed socket. Give the dispatch time to finish.
        do! Task.Delay 300
        socket.LateWrites |> Expect.equal "nothing written after close" 0
        socket.Waiting |> Expect.equal "the build's outcome went nowhere" 0
        stopping.Cancel()
      }

      testTask "each supervisor change pushes exactly one snapshot" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let gate = PauseGate()
        let hub = hubOf composer stopping.Token gate.Pause (ClockGate())
        hub.Start()
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! _ = readPrologue socket
        for revision in [ 5L; 6L ] do
          composer.Revision <- revision
          composer.Change()
          // The hub has pushed and is held in its pause: nothing more can come.
          let! (pause: TaskCompletionSource<unit>) = gate.Entered()
          let! (pushed: Event) = socket.Next()
          match pushed with
          | Snapshot snapshot -> snapshot.Revision |> Expect.equal "the change's revision" revision
          | other -> failtestf "a change pushed %A" other
          socket.Waiting |> Expect.equal "one push for one change" 0
          pause.SetResult()
        socket.Hangup()
        do! settle running
        stopping.Cancel()
      }

      testTask "a memory level change reaches every page as one Health push" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let hub = hubOf composer stopping.Token unpaused (ClockGate())
        hub.Start()
        composer.PublishLevel Bozzetto.MemoryPressure.Tight
        composer.Refreshes |> Expect.equal "no page, no ask of the health owner" 0
        let first, second = FakeSocket(), FakeSocket()
        let firstRun = hub.RunAsync(first, CancellationToken.None)
        let secondRun = hub.RunAsync(second, CancellationToken.None)
        let! _ = readPrologue first
        let! _ = readPrologue second
        composer.PublishLevel Bozzetto.MemoryPressure.Normal
        let! one = first.Next()
        let! two = second.Next()
        one |> Expect.equal "push, not a RequestSnapshot response: the plane's projection of the reading" (Health composer.CurrentHealth)
        two |> Expect.equal "the same health reaches the second page" one
        (match one with Health h -> h.MemoryPressure, h.Revision | other -> failtestf "not health: %A" other)
        |> Expect.equal "the moved level, stamped one on from the two readings before it" ("Normal", 3L)
        composer.Refreshes |> Expect.equal "one ask per page at connect, none for the change" 2
        first.Hangup()
        second.Hangup()
        do! settle firstRun
        do! settle secondRun
        stopping.Cancel()
      }

      testTask "an older reading announced after a newer one never reaches a page, which ends on the newer Health" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let hub = hubOf composer stopping.Token unpaused (ClockGate())
        hub.Start()
        let page = FakeSocket()
        let run = hub.RunAsync(page, CancellationToken.None)
        let! _ = readPrologue page
        // Two readings heard out of order, as owners announcing outside their gates can be.
        composer.Graph.Tick(Readings.tick 3L Bozzetto.MemoryPressure.Tight) |> Expect.equal "the newer is admitted" (Ok())
        composer.Graph.Tick(Readings.tick 2L Bozzetto.MemoryPressure.Normal) |> Expect.equal "the older is dropped" (Error Facts.Refusal.Older)
        let! first = page.Next()
        first |> Expect.equal "the one frame is the newer Health" (Health composer.CurrentHealth)
        do! Task.Delay 50
        page.Waiting |> Expect.equal "nothing stale follows" 0
        page.Hangup()
        do! settle run
        stopping.Cancel()
      }

      testTask "lease owner changes reach every page without refresh and detach on shutdown" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let hub = hubOf composer stopping.Token unpaused (ClockGate())
        hub.Start()
        composer.PublishPool([], [])
        composer.Refreshes |> Expect.equal "no page, no ask of the health owner" 0
        let first, second = FakeSocket(), FakeSocket()
        let firstRun = hub.RunAsync(first, CancellationToken.None)
        let secondRun = hub.RunAsync(second, CancellationToken.None)
        let! _ = readPrologue first
        let! _ = readPrologue second
        let grant : Bozzetto.ExpensiveWorkLease.GrantView =
          { Holder = "pi · /repo/clef"; Kind = "full_build"
            GrantedAt = DateTimeOffset.FromUnixTimeMilliseconds 1L; ExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds 2L }
        composer.PublishPool([ grant ], [])
        let! one = first.Next()
        let! two = second.Next()
        one |> Expect.equal "push, not a RequestSnapshot response" (Leases composer.CurrentLeases)
        two |> Expect.equal "the same board reaches the second page" one
        (match one with Leases l -> l.Active |> Array.map (fun g -> g.Holder, g.Kind) | other -> failtestf "not leases: %A" other)
        |> Expect.equal "the grant as the pool published it" [| "pi · /repo/clef", "full_build" |]
        composer.Refreshes |> Expect.equal "lease updates never ask the health owner" 2
        first.Hangup()
        do! settle firstRun
        composer.PublishPool([], [])
        let! released = second.Next()
        released |> Expect.equal "release clears the live board" (Leases composer.CurrentLeases)
        first.LateWrites |> Expect.equal "closed page receives nothing" 0
        second.Hangup()
        do! settle secondRun
        stopping.Cancel()
        composer.PublishPool([ grant ], [])
        second.LateWrites |> Expect.equal "a closed page's registration is gone" 0
        stopping.Dispose()
      }

      testTask "without an owner change, the health owner is asked only at the connect and the pull, and the facts come from the plane" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let gate = PauseGate()
        let hub = hubOf composer stopping.Token gate.Pause (ClockGate())
        hub.Start()
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! _ = readPrologue socket
        composer.Refreshes |> Expect.equal "the connect asked once" 1
        let names (events: Event list) = events |> List.map (fun e -> caseName (box e) typeof<Event>)
        // A supervisor change and a command: a snapshot and an outcome, and
        // neither health nor leases is asked for or pushed.
        composer.Change()
        let! (pause: TaskCompletionSource<unit>) = gate.Entered()
        socket.Deliver(frame 1 (Reserve { ReserveEdit.Target = target; Label = "edit" }))
        let! (changed: Event list) = readUntil socket (fun seen -> seen |> List.exists (fun e -> correlationOf e = Some 1))
        names changed |> Expect.equal "the change's snapshot, then the command's outcome" [ "Snapshot"; "Accepted" ]
        composer.Refreshes |> Expect.equal "no ask since the connect" 1
        // The explicit pull asks once more and answers with the plane's current values.
        socket.Deliver(frame 2 RequestSnapshot)
        let! (pulled: Event list) = readUntil socket (fun seen -> seen |> List.exists (fun e -> correlationOf e = Some 2))
        names pulled |> Expect.equal "fresh state, then the acknowledgement" [ "Snapshot"; "Health"; "Leases"; "Accepted" ]
        composer.Refreshes |> Expect.equal "the pull asked once more" 2
        socket.Waiting |> Expect.equal "nothing else was written" 0
        pause.SetResult()
        socket.Hangup()
        do! settle running
        stopping.Cancel()
      }

      testTask "a burst of changes is pushed as one leading and one trailing snapshot, never as an older revision" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let gate = PauseGate()
        let hub = hubOf composer stopping.Token gate.Pause (ClockGate())
        hub.Start()
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! _ = readPrologue socket
        let revisionOf event = match event with Snapshot s -> Some s.Revision | _ -> None
        // Leading edge: the first change is pushed at once.
        composer.Revision <- 7L
        composer.Change()
        let! (leading: TaskCompletionSource<unit>) = gate.Entered()
        let! (first: Event) = socket.Next()
        // Nine more inside the spacing collapse into one trailing push.
        for revision in 8L .. 16L do
          composer.Revision <- revision
          composer.Change()
        leading.SetResult()
        let! (trailing: TaskCompletionSource<unit>) = gate.Entered()
        let! (second: Event) = socket.Next()
        [ first; second ] |> List.map revisionOf |> Expect.equal "ten changes, two pushes: leading and trailing" [ Some 7L; Some 16L ]
        socket.Waiting |> Expect.equal "and nothing else while the hub is held" 0
        // A revision older than the page's is read and pushed, but never written.
        composer.Revision <- 9L
        composer.Change()
        trailing.SetResult()
        let! (stale: TaskCompletionSource<unit>) = gate.Entered()
        composer.Revision <- 17L
        composer.Change()
        stale.SetResult()
        let! (last: TaskCompletionSource<unit>) = gate.Entered()
        let! (next: Event) = socket.Next()
        revisionOf next |> Expect.equal "9 was held back; the next frame is 17" (Some 17L)
        socket.Waiting |> Expect.equal "nothing else was written" 0
        last.SetResult()
        socket.Hangup()
        do! settle running
        stopping.Cancel()
      }
    ]

    testList "uptime clock" [
      testTask "no clock runs while no page is connected, before the first page or after the last" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let gate = PauseGate()
        let clock = ClockGate()
        let hub = hubOf composer stopping.Token gate.Pause clock
        hub.Start()
        // The hub is running and has handled a change, with no page.
        composer.Change()
        let! (pause: TaskCompletionSource<unit>) = gate.Entered()
        (clock.Started, clock.Pending) |> Expect.equal "no clock period begins without a page" (0, 0)
        pause.SetResult()
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! _ = readPrologue socket
        let! (period: ClockPeriod) = clock.Entered()
        period.Sequence |> Expect.equal "the first page starts the clock" 1
        socket.Hangup()
        do! settle running
        do! period.Stopped.WaitAsync(TimeSpan.FromSeconds 5.)
        (clock.Started, clock.Pending) |> Expect.equal "the last page closing stops it; nothing begins after" (1, 0)
        socket.Waiting |> Expect.equal "an unticked clock pushes nothing" 0
        stopping.Cancel()
      }

      testTask "one tick pushes exactly one Uptime to every connected page" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let clock = ClockGate()
        let hub = hubOf composer stopping.Token unpaused clock
        let first, second = FakeSocket(), FakeSocket()
        let runningFirst = hub.RunAsync(first, CancellationToken.None)
        let runningSecond = hub.RunAsync(second, CancellationToken.None)
        let! _ = readPrologue first
        let! _ = readPrologue second
        let! (period: ClockPeriod) = clock.Entered()
        period.Tick.SetResult 93784005L
        // The clock goes on to its next period, which the test holds.
        let! (next: ClockPeriod) = clock.Entered()
        (period.Sequence, next.Sequence) |> Expect.equal "one clock for both pages" (1, 2)
        let! (toFirst: Event) = first.Next()
        let! (toSecond: Event) = second.Next()
        [ uptimeOf toFirst; uptimeOf toSecond ] |> Expect.equal "each page is pushed the tick's uptime" [ Some 93784005L; Some 93784005L ]
        (first.Waiting, second.Waiting) |> Expect.equal "one Uptime per page per tick" (0, 0)
        clock.Pending |> Expect.equal "and no second clock" 0
        first.Hangup()
        second.Hangup()
        do! settle runningFirst
        do! settle runningSecond
        do! next.Stopped.WaitAsync(TimeSpan.FromSeconds 5.)
        stopping.Cancel()
      }

      testTask "the clock stops after the last page closes and starts again when a page connects" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let clock = ClockGate()
        let hub = hubOf composer stopping.Token unpaused clock
        let leaving, staying = FakeSocket(), FakeSocket()
        let runningLeaving = hub.RunAsync(leaving, CancellationToken.None)
        let! _ = readPrologue leaving
        let! (firstPeriod: ClockPeriod) = clock.Entered()
        let runningStaying = hub.RunAsync(staying, CancellationToken.None)
        let! _ = readPrologue staying
        // One page of two closes: the clock keeps running for the other.
        leaving.Hangup()
        do! settle runningLeaving
        firstPeriod.Tick.SetResult 2000L
        let! (toStaying: Event) = staying.Next()
        uptimeOf toStaying |> Expect.equal "the remaining page is still ticked" (Some 2000L)
        (leaving.Waiting, leaving.LateWrites) |> Expect.equal "the closed page is not" (0, 0)
        // The last page closes: the clock stops in the middle of its period.
        let! (secondPeriod: ClockPeriod) = clock.Entered()
        staying.Hangup()
        do! settle runningStaying
        do! secondPeriod.Stopped.WaitAsync(TimeSpan.FromSeconds 5.)
        (clock.Started, clock.Pending) |> Expect.equal "stopped, and nothing begins with no page" (2, 0)
        // A late tick of the stopped period is never pushed.
        secondPeriod.Tick.TrySetResult 3000L |> ignore
        let returning = FakeSocket()
        let runningReturning = hub.RunAsync(returning, CancellationToken.None)
        let! _ = readPrologue returning
        let! (thirdPeriod: ClockPeriod) = clock.Entered()
        thirdPeriod.Sequence |> Expect.equal "a page connecting starts the clock again" 3
        thirdPeriod.Tick.SetResult 4000L
        let! (toReturning: Event) = returning.Next()
        uptimeOf toReturning |> Expect.equal "its first Uptime is the new clock's" (Some 4000L)
        let! (fourthPeriod: ClockPeriod) = clock.Entered()
        returning.Waiting |> Expect.equal "one tick, one Uptime" 0
        returning.Hangup()
        do! settle runningReturning
        do! fourthPeriod.Stopped.WaitAsync(TimeSpan.FromSeconds 5.)
        stopping.Cancel()
      }
    ]
  ]

/// A board is sequenced by the publication, not by the owner's report count:
/// presence moves with leases and membership while the report count stands.
[<Tests>]
let boardSequenceTests =
  testList "UiBridge.Project.agentBoard — sequenced by the publication" [
    testCase "two publications of one unchanged report state with different presence are ordered by the publication" <| fun () ->
      let state = { Bozzetto.AgentWork.empty with Incarnation = "daemon-x"; Sequence = 5L }
      let held = [| { AgentPresence.Name = "pi"; Source = "lease"; Member = "pi · /repo"; Project = "/repo"; Detail = "holds full_build"; SinceMs = 1L } |]
      let before = Project.agentBoard 10L state [||]
      let after = Project.agentBoard 11L state held
      (after.Sequence > before.Sequence) |> Expect.isTrue "the later publication carries the later sequence although no report changed"
      after.Incarnation |> Expect.equal "the incarnation is still the owner's" "daemon-x"
      after.Presence |> Expect.equal "the later publication carries the presence it saw" held
  ]

/// Presence is derived from facts the daemon owns; these controls pin what each
/// row may claim and the mutations a derivation could drift into.
[<Tests>]
let presenceTests =
  let at (seconds: int) = DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero).AddSeconds(float seconds)
  let grant holder kind seconds : Bozzetto.ExpensiveWorkLease.GrantView =
    { Holder = holder; Kind = kind; GrantedAt = at seconds; ExpiresAt = at (seconds + 600) }
  let ask holder kind seconds : Bozzetto.ExpensiveWorkLease.QueueView = { Holder = holder; Kind = kind; RequestedAt = at seconds }
  let renewal seconds = DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc).AddSeconds(float seconds)
  let present id seconds = id, Bozzetto.Cohort.JoinableRole.Observer, Bozzetto.Cohort.SeatState.Present, renewal seconds
  let departed id seconds = id, Bozzetto.Cohort.JoinableRole.Observer, Bozzetto.Cohort.SeatState.Departed DateTime.UtcNow, renewal seconds
  let report name project status sequence : Bozzetto.AgentWork.Report =
    { Run = "run-" + string sequence; Session = "s"; Sequence = sequence; Name = name; Model = "m"; Endpoint = "e"
      Project = project; Focus = "f"; Operation = "o"; Status = status; Usage = None }
  let run who (r: Bozzetto.AgentWork.Report) (seconds: int) : Bozzetto.AgentWork.Run =
    { Member = who; Role = Bozzetto.Cohort.JoinableRole.Observer; Report = r
      UpdatedAt = DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc).AddSeconds(float seconds); Activity = [ r.Operation ]; OmittedActivity = 0L }
  testList "UiBridge.Project.presence — derived from leases and the cohort frame" [
    testCase "a lease holder spelled agent · project becomes that agent at that project, held or queued, since its grant or ask" <| fun () ->
      let rows = Project.presence [ grant "pi · /repo/clef" "full_build" 10 ] [ ask "pi · /repo/clef" "test_suite_run" 20 ] [] Map.empty
      rows |> Array.map (fun p -> p.Member) |> Expect.equal "the whole holder is the row's identity" [| "pi · /repo/clef"; "pi · /repo/clef" |]
      rows |> Array.map (fun p -> p.Name, p.Project, p.Source, p.Detail, p.SinceMs)
      |> Expect.equal "one row per lease fact"
           [| "pi", "/repo/clef", "lease", "holds full_build", (at 10).ToUnixTimeMilliseconds()
              "pi", "/repo/clef", "lease", "queued for test_suite_run", (at 20).ToUnixTimeMilliseconds() |]
    testCase "a holder without the separator is the whole name and claims no project" <| fun () ->
      let rows = Project.presence [ grant "gate-command-1" "full_build" 0 ] [] [] Map.empty
      rows |> Array.map (fun p -> p.Name, p.Project) |> Expect.equal "no invented project" [| "gate-command-1", "" |]
    testCase "a present member is named by its latest report and shows that report's status; a departed member is not shown" <| fun () ->
      let here = Bozzetto.MemberTable.MemberId.Mcp "here"
      let gone = Bozzetto.MemberTable.MemberId.Mcp "gone"
      let runs =
        Map.ofList [
          "run-1", run here (report "old-name" "/old" "completed" 1L) 5
          "run-2", run here (report "claude-primary" "/repo/Bozzetto" "running" 2L) 9
          "run-3", run gone (report "pi" "/repo" "running" 1L) 7 ]
      let rows = Project.presence [] [] [ present here 30; departed gone 31 ] runs
      rows |> Array.map (fun p -> p.Name, p.Project, p.Source, p.Detail, p.SinceMs)
      |> Expect.equal "the latest report names the member; the departed one is absent"
           [| "claude-primary", "/repo/Bozzetto", "member", "Observer member · last report running", (at 30).ToUnixTimeMilliseconds() |]
      rows[0].Member |> Expect.equal "the connection identity, not the reported name, is the row's identity" "mcp:here"
    testCase "a present member with no report is shown by its connection identity and says so" <| fun () ->
      let quiet = Bozzetto.MemberTable.MemberId.Mcp "quiet"
      let rows = Project.presence [] [] [ present quiet 0 ] Map.empty
      rows |> Array.map (fun p -> p.Name, p.Project, p.Detail)
      |> Expect.equal "no name is invented for a silent member" [| "mcp:quiet", "", "Observer member · no report yet" |]
    testCase "a member's time is its latest renewal, the fact the daemon retains" <| fun () ->
      let m = Bozzetto.MemberTable.MemberId.Mcp "m"
      let rows = Project.presence [] [] [ present m 45 ] Map.empty
      rows[0].SinceMs |> Expect.equal "renewal time, not a join time the daemon does not keep" ((at 45).ToUnixTimeMilliseconds())
  ]

/// Source admission extends the existing fact plane; no remote enrollment or
/// browser wire is involved in these controls.
[<Tests>]
let observationTests =
  let a : Facts.ObservationSource = { Node = "A"; Incarnation = "A-current" }
  let b : Facts.ObservationSource = { Node = "B"; Incarnation = "B-current" }
  let producer source : Facts.ObservationProducer = { Source = source; Correlation = Guid.NewGuid() }
  let pa, pb = producer a, producer b
  let consumerProducer (binding: Facts.ObservationBinding) : Facts.ObservationProducer =
    { Source = binding.Source; Correlation = binding.Subscription }
  let graph () = Facts.Graph({ McpPort = 47759; Version = "test"; ProcessId = 99 }, "daemon-o", "", observationOwners = [ pa; pb ])
  let register (graph: Facts.Graph) source sink =
    graph.RegisterObservation(source, sink) |> Result.defaultWith (fun error -> failtestf "registration refused: %A" error)
  let sample sequence note = { Bozzetto.Server.ResourceMonitor.empty "observing" with Sequence = sequence; SampledAtMs = sequence * 1000L; Note = note }
  let workGraph () =
    Facts.Graph({ McpPort = 47759; Version = "test"; ProcessId = 99 }, "daemon-o", "",
      observationOwners = [ pa ], localNode = a.Node, peerNodes = [ b.Node ])
  let workValue sequence : WorkSnapshot =
    { TranscriptsOmitted = false
      Sequence = sequence; SampledAtMs = sequence * 1000L
      Snapshot = { Revision = sequence; Worker = Unconfigured; Sessions = [||] }
      Leases = { Revision = sequence; Active = [||]; Queued = [||] }
      Operations = { Revision = sequence; Incarnation = "journal"; StorageError = None; Entries = [||] } }
  let workNode (graph: Facts.Graph) node =
    graph.Current() |> List.pick (function WorkNodes board -> board.Nodes |> Array.tryFind (fun value -> value.Source.Node = node) | _ -> None)
  testList "UiBridge.Facts scoped observation" [
    testCase "configured work slots stay unavailable until authenticated source publication" <| fun () ->
      let graph = workGraph ()
      let absent = workNode graph b.Node
      (absent.Source.Incarnation, absent.Local, absent.Status, absent.Value)
      |> Expect.equal "configuration supplies neither incarnation nor a fabricated snapshot" ("", false, "unavailable", None)
      (workNode graph a.Node).Local |> Expect.isTrue "locality comes from this controller"
      graph.PeerConnectionUnavailable(b.Node, "Peer authentication failed.") |> Expect.equal "failed Hello is visible" (Ok())
      (workNode graph b.Node).Note |> Expect.equal "sanitized failure is retained" "Peer authentication failed."
      graph.PeerConnectionUnavailable("unknown", "missing") |> Expect.equal "failure cannot enroll a node" (Error Facts.Refusal.UnknownSource)
      let peer = graph.BindPeer b
      graph.PeerConnectionUnavailable(b.Node, "late Hello failure") |> Expect.equal "unbound report cannot overwrite a bound connection" (Error Facts.Refusal.ForeignProducer)
      graph.PublishWork(peer, workValue 5L) |> Expect.equal "authenticated source is admitted" (Ok())
      graph.PublishWork({ peer with Correlation = Guid.NewGuid() }, workValue 6L) |> Expect.equal "identity alone grants no publication" (Error Facts.Refusal.ForeignProducer)
      (workNode graph b.Node).Value |> Expect.equal "the current snapshot names its real observation time" (Some(workValue 5L))
      graph.Stop()

    testCase "work sequence and unavailable status cannot overwrite or freshen a retained snapshot" <| fun () ->
      let graph = workGraph ()
      let peer = graph.BindPeer b
      let value = workValue 5L
      graph.PublishWork(peer, value) |> Expect.equal "first work snapshot admitted" (Ok())
      graph.PublishWork(peer, value) |> Expect.equal "identical work is idempotent" (Error Facts.Refusal.Duplicate)
      graph.PublishWork(peer, { value with SampledAtMs = 999L }) |> Expect.equal "same sequence with different facts is refused" (Error Facts.Refusal.Conflict)
      graph.PublishWork(peer, workValue 4L) |> Expect.equal "older snapshot refused" (Error Facts.Refusal.Older)
      for _ in 1 .. 7 do graph.PeerUnavailable(peer, "disconnected") |> Expect.equal "connection status has its own publication order" (Ok())
      let unavailable = workNode graph b.Node
      (unavailable.Status, unavailable.Value) |> Expect.equal "disconnect retains the exact old timestamp and facts" ("unavailable", Some value)
      graph.PublishWork(peer, value) |> Expect.equal "same-incarnation retained snapshot can recover without inventing sequence" (Ok())
      (workNode graph b.Node).Status |> Expect.equal "source is available again" "available"
      graph.Stop()

    testCase "same-incarnation reconnect fences the old connection and new incarnation clears retained work" <| fun () ->
      let graph = workGraph ()
      let first = graph.BindPeer b
      graph.PublishWork(first, workValue 5L) |> ignore
      let oldFocus, demand = register graph b ignore
      graph.PublishPeerResources(first, sample 8L "old") |> ignore
      let next = graph.BindPeer b
      graph.ObservationDemanded b |> Expect.equal "replacement releases old scoped demand in Foundation" (Ok false)
      graph.PublishWork(first, workValue 99L) |> Expect.equal "late same-incarnation connection refused" (Error Facts.Refusal.ForeignProducer)
      graph.PublishPeerResources(first, sample 99L "late") |> Expect.equal "late resource callback refused too" (Error Facts.Refusal.ForeignProducer)
      graph.PeerUnavailable(first, "late disconnect") |> Expect.equal "old connection cannot mark the replacement down" (Error Facts.Refusal.ForeignProducer)
      graph.PublishWork(next, workValue 5L) |> Expect.equal "replacement can repeat the retained snapshot" (Ok())
      let newestSource = { b with Incarnation = "B-next" }
      let newest = graph.BindPeer newestSource
      (workNode graph b.Node).Value |> Expect.equal "a new process owns no old process snapshot" None
      graph.PublishWork(next, workValue 999L) |> Expect.equal "old incarnation remains fenced" (Error Facts.Refusal.ForeignIncarnation)
      graph.PublishWork(newest, workValue 0L) |> Expect.equal "new process starts its own sequence" (Ok())
      demand.Dispose()
      graph.Stop()

    testCase "resource availability has its own stamp and conflicting equal samples remain refused" <| fun () ->
      let graph = workGraph ()
      let peer = graph.BindPeer b
      let pushed = ResizeArray<Facts.Observation>()
      let _, demand = register graph b pushed.Add
      use _demand = demand
      let value = sample 5L "sample"
      graph.PublishPeerResources(peer, value) |> Expect.equal "sample admitted" (Ok())
      graph.PublishPeerResources(peer, value) |> Expect.equal "duplicate sample idempotent" (Error Facts.Refusal.Duplicate)
      graph.PublishPeerResources(peer, { value with Note = "changed" }) |> Expect.equal "note-only change is not a new sample" (Error Facts.Refusal.Conflict)
      graph.PublishPeerResources(peer, sample 4L "old") |> Expect.equal "older sample refused" (Error Facts.Refusal.Older)
      graph.PeerResourcesUnavailable(peer, "stream unavailable") |> Expect.equal "unavailability does not fabricate a sample sequence" (Ok())
      pushed[pushed.Count - 1].Sequence |> Expect.equal "last real sample sequence remains" (Some 5L)
      graph.PublishPeerResources(peer, value) |> Expect.equal "same retained sample can recover after unavailable" (Ok())
      (workNode graph b.Node).Value |> Expect.equal "resource observations never create work snapshots" None
      graph.Stop()

    testCase "work summaries explicitly omit run transcripts without changing the local journal projection" <| fun () ->
      let operations = events |> List.pick (function Operations value when value.Entries |> Array.exists (fun entry -> entry.Run.IsSome) -> Some value | _ -> None)
      let original = { workValue 1L with Operations = operations }
      let summary = Project.workSummary original
      summary.TranscriptsOmitted |> Expect.isTrue "omission belongs to the work projection"
      summary.Operations.Entries |> Array.choose _.Run |> Array.forall (fun run -> run.StandardOutput = "" && run.StandardError = "")
      |> Expect.isTrue "live summary excludes transcript bytes"
      summary.Operations.Entries |> Array.choose _.Run |> Array.map _.Dropped
      |> Expect.equal "source loss is unchanged by summary omission" (original.Operations.Entries |> Array.choose _.Run |> Array.map _.Dropped)
      original.Operations |> Expect.equal "projection did not alter the source-owned local operations" operations
      summary.Snapshot |> Expect.equal "session/work facts are preserved" original.Snapshot
      summary.Leases |> Expect.equal "actual lease facts are preserved" original.Leases

    testCase "encoded work budget admits its exact edge and refuses oversize with retained stale facts" <| fun () ->
      let graph = workGraph ()
      let peer = graph.BindPeer b
      let withProject length =
        let value = workValue 1L
        { value with Snapshot = { value.Snapshot with Sessions = [| { bareSession with Project = String.replicate length "x" } |] } }
      let initialLength = MaxWorkSnapshotBytes - 1024
      let initial = withProject initialLength
      let initialSize = Bozzetto.Web.Shared.BinaryCodec.encodePeerFrame(PeerWork initial) |> Array.length
      let edge = withProject (initialLength + MaxWorkSnapshotBytes - initialSize)
      Bozzetto.Web.Shared.BinaryCodec.encodePeerFrame(PeerWork edge) |> Array.length |> Expect.equal "real encoded edge, including the peer envelope" MaxWorkSnapshotBytes
      graph.PublishWork(peer, edge) |> Expect.equal "exact budget admitted" (Ok())
      let oversized = { withProject (initialLength + MaxWorkSnapshotBytes - initialSize + 1) with Sequence = 2L }
      graph.PublishWork(peer, oversized) |> Expect.equal "one byte over refused specifically" (Error Facts.Refusal.Oversized)
      let current = workNode graph b.Node
      (current.Status, current.Note, current.Value)
      |> Expect.equal "oversize is visible and preserves the earlier timestamp and busy/lease facts"
        ("unavailable", "Work snapshot exceeds the encoded observation budget.", Some edge)
      graph.PublishWork(peer, edge) |> Expect.equal "a valid source can recover from the refused update" (Ok())
      graph.PublishWork(peer, { oversized with Sequence = 0L }) |> Expect.equal "an older oversized payload stays an ordering refusal" (Error Facts.Refusal.Older)
      (workNode graph b.Node).Status |> Expect.equal "late bad payload cannot invalidate the current source" "available"
      graph.Stop()

    testTask "one bounded resource window completing releases only its own focus and never a build" {
      let stopping = new CancellationTokenSource()
      let composer = FakeComposer(HoldBuilds = true)
      let graph = workGraph ()
      let peer = graph.BindPeer b
      let requested = Channel.CreateUnbounded<(string -> unit) * TaskCompletionSource<unit>>()
      let observePeer _ _ finished = async {
        let disposed = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        requested.Writer.TryWrite(finished, disposed) |> ignore
        return { new IDisposable with member _.Dispose() = disposed.TrySetResult() |> ignore }
      }
      let sources = { composer.Sources with Facts = { Register = graph.Register; Current = graph.Current; Refresh = ignore } }
      let hub = Hub(sources, quiet, stopping.Token, unpaused, (ClockGate()).Tick, observation = (graph, pa, observePeer))
      let first, second = FakeSocket(), FakeSocket()
      let runningFirst = hub.RunAsync(first, CancellationToken.None)
      let runningSecond = hub.RunAsync(second, CancellationToken.None)
      try
        let! _ = readUntil first (List.exists (function WorkNodes _ -> true | _ -> false))
        let! _ = readUntil second (List.exists (function WorkNodes _ -> true | _ -> false))
        first.Deliver(frame 1 (Build { Target = target; Reservation = "rsv" }))
        let! (buildToken: CancellationToken) = composer.BuildEntered.WaitAsync(TimeSpan.FromSeconds 5.)
        first.Deliver(frame 2 (ObserveNodeResources { Node = b.Node; Seconds = 1 }))
        let! ((finishFirst, disposedFirst): (string -> unit) * TaskCompletionSource<unit>) = requested.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds 5.)
        second.Deliver(frame 3 (ObserveNodeResources { Node = b.Node; Seconds = 120 }))
        let! ((finishSecond, disposedSecond): (string -> unit) * TaskCompletionSource<unit>) = requested.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds 5.)
        graph.PublishPeerResources(peer, sample 5L "both") |> Expect.equal "one source feeds independent demands" (Ok())
        let hasSample correlation sequence = List.exists (function NodeResources value -> value.Correlation = correlation && value.Value.Sequence = sequence && value.Value.Status = "observing" | _ -> false)
        let! _ = readUntil first (hasSample 2 5L)
        let! _ = readUntil second (hasSample 3 5L)
        finishFirst "complete"
        do! disposedFirst.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        let! _ = readUntil first (List.exists (function NodeResources value -> value.Correlation = 2 && value.Value.Status = "complete" && value.Value.SampledAtMs = 5000L | _ -> false))
        graph.ObservationDemanded b |> Expect.equal "other window keeps its Foundation demand" (Ok true)
        graph.PublishPeerResources(peer, sample 6L "remaining") |> Expect.equal "source stays live" (Ok())
        let! _ = readUntil second (hasSample 3 6L)
        first.Waiting |> Expect.equal "expired focus does not receive another window's sample" 0
        buildToken.IsCancellationRequested |> Expect.isFalse "resource focus never cancels the held build"
        second.Deliver(frame 4 (ObserveNodeResources { Node = b.Node; Seconds = 0 }))
        let! stopFrames = readUntil second (List.exists (function Accepted value -> value.Correlation = 4 | _ -> false))
        disposedSecond.Task.IsCompleted |> Expect.isTrue "stop acknowledgment follows the owned stream's disposal and join"
        stopFrames |> List.exists (function NodeResources value -> value.Correlation = 4 | _ -> false)
        |> Expect.isFalse "stop acknowledges the command without fabricating a source sample"
        graph.ObservationDemanded b |> Expect.equal "last window releases the actual Foundation demand" (Ok false)
        let current, retained = register graph b ignore
        current.Value |> Expect.equal "one window stopping does not change the shared source fact" (Ok(sample 6L "remaining"))
        retained.Dispose()
        finishSecond "complete"
        buildToken.IsCancellationRequested |> Expect.isFalse "stop only releases its observation demand"
      finally
        composer.ReleaseBuild()
        first.Hangup()
        second.Hangup()
      do! settle runningFirst
      do! settle runningSecond
      stopping.Cancel()
      graph.Stop()
      stopping.Dispose()
    }

    testTask "headless HTTP work and transcript reads bind their node and join active streams on close" {
      do! task {
        use stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let workerGraph = Facts.Graph({ McpPort = 47759; Version = "test"; ProcessId = 99 }, "daemon-o", "", observationOwners = [pb], localNode = b.Node)
        workerGraph.PublishWork(pb, workValue 7L) |> Expect.equal "worker has source-owned work before a subscriber" (Ok())
        let transcriptReads = ResizeArray<TranscriptReadRequest>()
        let readTranscript (request: TranscriptReadRequest) =
          transcriptReads.Add request
          TranscriptPage { Journal = (if request.Sequence = 8L then "wrong-journal" else request.Journal)
                           Sequence = request.Sequence; Stream = request.Stream; Offset = request.Offset
                           NextOffset = None; TotalBytes = 3L; Bytes = [|0uy;128uy;255uy|]; Truncated = false }
        let makeHub (graph: Facts.Graph) (owner: Facts.ObservationProducer) (peers: string -> int -> (string -> unit) -> Async<IDisposable>) =
          let sources = { composer.Sources with Facts = { Register = graph.Register; Current = graph.Current; Refresh = ignore } }
          Hub(sources, quiet, stopping.Token, unpaused, (ClockGate()).Tick,
            observation = (graph, owner, peers), localTranscript = readTranscript)
        let noResources _ _ _ = async { return invalidOp "This control does not request resource acquisition." }
        let workerHub = makeHub workerGraph pb noResources
        let aborted = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let requestVersions = System.Collections.Concurrent.ConcurrentQueue<byte>()
        let! server, url = Hosting.start (fun app ->
          Bozzetto.Server.McpServer.useOriginGuard
            (Bozzetto.Server.HttpOriginGuard.OwnOrigins.ofPorts [47749; 47750]) app
          Microsoft.AspNetCore.Builder.UseExtensions.Use(app,
            Func<Microsoft.AspNetCore.Http.HttpContext, Func<Task>, Task>(fun ctx next -> task {
              use _aborted =
                if string ctx.Request.Headers.Authorization = "Bearer fixture-token" then
                  ctx.RequestAborted.Register(fun () -> aborted.TrySetResult() |> ignore)
                else Unchecked.defaultof<CancellationTokenRegistration>
              if string ctx.Request.Headers.Authorization = "Bearer fixture-token" then
                use buffered = new System.IO.MemoryStream()
                do! ctx.Request.Body.CopyToAsync buffered
                let payload = buffered.ToArray()
                if payload.Length > 4 then requestVersions.Enqueue payload[4]
                buffered.Position <- 0L
                let original = ctx.Request.Body
                ctx.Request.Body <- buffered
                try do! next.Invoke()
                finally ctx.Request.Body <- original
              else
                do! next.Invoke()
            })) |> ignore
          mapRoutes true app (fun () -> failwith "headless must not map browser routes")
          Observe.mapRoutes app workerHub)
        let directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/work-observation-controls", Guid.NewGuid().ToString("N")))
        System.IO.Directory.CreateDirectory directory |> ignore
        let tokenFile = System.IO.Path.Combine(directory, "test.token")
        System.IO.File.WriteAllText(tokenFile, "fixture-token")
        let graph = workGraph ()
        let available = TaskCompletionSource<NodeWork>(TaskCreationOptions.RunContinuationsAsynchronously)
        let unavailable = TaskCompletionSource<NodeWork>(TaskCreationOptions.RunContinuationsAsynchronously)
        let disconnected = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, registration = graph.Register(fun _ event ->
          match event with
          | WorkNodes board ->
            board.Nodes |> Array.tryFind (fun node -> node.Source.Node = b.Node) |> Option.iter (fun node ->
              if node.Status = "available" then available.TrySetResult node |> ignore
              elif node.Note = "Worker snapshot is unavailable." then unavailable.TrySetResult node |> ignore
              elif node.Note = "Disconnected; retained work is not current." then disconnected.TrySetResult() |> ignore)
          | _ -> ())
        use _registration = registration
        let hub = makeHub graph pa noResources
        let controller = new Observe.Controller(hub, [ { Node = b.Node; Endpoint = Uri(url + "/api/work/observe"); TokenFile = tokenFile } ], stopping.Token)
        try
          use client = new Net.Http.HttpClient(Timeout = TimeSpan.FromSeconds 5.)
          let payload =
            Bozzetto.Web.Shared.BinaryCodec.encodePeerObserveRequest
              { ProtocolVersion = ProtocolVersion; Node = b.Node; Topic = "work"; Seconds = 0 }
          let request (methodName: string) (path: string) (mediaType: string) (origin: string option) (host: string option) =
            let message = new Net.Http.HttpRequestMessage(Net.Http.HttpMethod(methodName), url + path)
            message.Content <- new Net.Http.ByteArrayContent(payload)
            message.Content.Headers.ContentType <- Net.Http.Headers.MediaTypeHeaderValue(mediaType)
            origin |> Option.iter (fun value -> message.Headers.Add("Origin", value))
            host |> Option.iter (fun value -> message.Headers.Host <- value)
            message
          use admitted = request "POST" Observe.Route Observe.MediaType None None
          use! admittedResponse = client.SendAsync(admitted, Net.Http.HttpCompletionOption.ResponseHeadersRead)
          int admittedResponse.StatusCode |> Expect.equal "real origin middleware admits the declared binary observation route" 200
          string admittedResponse.Content.Headers.ContentType |> Expect.equal "successful observation stays binary" Observe.MediaType
          use! admittedBody = admittedResponse.Content.ReadAsStreamAsync()
          let! hello = Observe.Frames.read admittedBody CancellationToken.None |> Async.StartAsTask
          hello |> Option.map Bozzetto.Web.Shared.BinaryCodec.decodePeerFrame
          |> Expect.equal "headless handler returns its actual node after origin admission" (Some(Ok(PeerHello b)))
          admittedResponse.Dispose()
          for methodName, path, mediaType, origin, host, status, reason in [
            "POST", "/api/not-observe", Observe.MediaType, None, None, 415, "not application/json"
            "PUT", Observe.Route, Observe.MediaType, None, None, 415, "not application/json"
            "POST", Observe.Route, "text/plain", None, None, 415, "not " + Observe.MediaType
            "POST", Observe.Route, "application/json", None, None, 415, "not " + Observe.MediaType
            "POST", Observe.Route, Observe.MediaType, Some "https://outside.example", None, 403, "not one of this server's own origins"
            "POST", Observe.Route, Observe.MediaType, None, Some "outside.example", 403, "not the loopback interface"
            "POST", Observe.Route, Observe.MediaType, Some "http://localhost:47750", None, 200, "" ] do
            use rejected = request methodName path mediaType origin host
            use! response = client.SendAsync(rejected, Net.Http.HttpCompletionOption.ResponseHeadersRead)
            int response.StatusCode |> Expect.equal (sprintf "specific origin/media verdict for %s %s %s" methodName path mediaType) status
            if status <> 200 then
              let! body = response.Content.ReadAsStringAsync()
              body |> Expect.stringContains "the owning guard names the refusal" reason
          use oversized = request "POST" Observe.Route Observe.MediaType None None
          oversized.Content.Dispose()
          oversized.Content <- new Net.Http.ByteArrayContent(Array.zeroCreate<byte> (Observe.MaxRequestBytes + 1))
          oversized.Content.Headers.ContentType <- Net.Http.Headers.MediaTypeHeaderValue(Observe.MediaType)
          use! oversizedResponse = client.SendAsync oversized
          int oversizedResponse.StatusCode |> Expect.equal "origin admission does not bypass the observation body bound" 400
          string oversizedResponse.Content.Headers.ContentType |> Expect.equal "handler refusal stays binary" Observe.MediaType
          use! oversizedBody = oversizedResponse.Content.ReadAsStreamAsync()
          let! oversize = Observe.Frames.read oversizedBody CancellationToken.None |> Async.StartAsTask
          oversize |> Option.map Bozzetto.Web.Shared.BinaryCodec.decodePeerFrame
          |> Expect.equal "the route owns its exact bounded-body refusal" (Some(Ok(PeerUnavailable "request_too_large")))
          use! page = client.GetAsync(url + "/work")
          int page.StatusCode |> Expect.equal "headless HTTP does not expose the browser" 404
          let! (initial: NodeWork) = available.Task.WaitAsync(TimeSpan.FromSeconds 5.)
          (initial.Source, initial.Local, initial.Value) |> Expect.equal "binary Hello and atomic current work identify the actual worker" (b, false, Some(workValue 7L))
          let read = { Source = b; Journal = "persisted-journal"; Sequence = 7L; Stream = Stdout; Offset = 0L; MaxBytes = 3 }
          let! transcript = controller.ReadTranscript(read, CancellationToken.None) |> Async.StartAsTask
          transcript |> Expect.equal "selected output arrives as exact binary bytes from the named journal"
            (TranscriptPage { Journal = read.Journal; Sequence = 7L; Stream = Stdout; Offset = 0L; NextOffset = None; TotalBytes = 3L; Bytes = [|0uy;128uy;255uy|]; Truncated = false })
          let! wrongPage = controller.ReadTranscript({ read with Sequence = 8L }, CancellationToken.None) |> Async.StartAsTask
          wrongPage |> Expect.equal "a node cannot substitute another journal's page"
            (TranscriptRefused("invalid_transcript", "The node returned a mismatched transcript page."))
          let! retired = controller.ReadTranscript({ read with Source = { b with Incarnation = "retired" } }, CancellationToken.None) |> Async.StartAsTask
          retired |> Expect.equal "a retired browser reference cannot issue a node read"
            (TranscriptRefused("node_unavailable", "Select a node with a current authenticated work connection."))
          transcriptReads.Count |> Expect.equal "only the two admitted reads reached storage" 2
          use forged = new Net.Http.ByteArrayContent(Bozzetto.Web.Shared.BinaryCodec.encodePeerTranscriptRequest
            { ProtocolVersion = ProtocolVersion; Request = { read with Source = a } })
          forged.Headers.ContentType <- Net.Http.Headers.MediaTypeHeaderValue Observe.MediaType
          use! rejected = client.PostAsync(url + "/api/work/observe", forged)
          int rejected.StatusCode |> Expect.equal "worker also rejects a crossed identity" 400
          transcriptReads.Count |> Expect.equal "worker identity checked before storage" 2
          let versions = requestVersions.ToArray()
          versions.Length >= 3 |> Expect.isTrue "work and readback went through the actual controller HTTP client"
          versions |> Array.forall ((=) (byte LegacyProtocolVersion))
          |> Expect.isTrue "unchanged requests retain v9 envelopes for deployed workers"
          workerGraph.PeerUnavailable(pb, "Worker snapshot is unavailable.") |> Expect.equal "source publishes unavailable without inventing work" (Ok())
          let! (lost: NodeWork) = unavailable.Task.WaitAsync(TimeSpan.FromSeconds 5.)
          lost.Value |> Expect.equal "HTTP status propagation retains the old snapshot and timestamp" (Some(workValue 7L))
          do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
          do! disconnected.Task.WaitAsync(TimeSpan.FromSeconds 5.)
          do! aborted.Task.WaitAsync(TimeSpan.FromSeconds 5.)
          (workNode graph b.Node).Status |> Expect.equal "a joined connection never leaves an available worker" "unavailable"
        finally
          stopping.Cancel()
          try (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.).GetAwaiter().GetResult()
          with _ -> () // Preserve the assertion failure while still attempting bounded owned cleanup.
          workerGraph.Stop()
          graph.Stop()
          Hosting.stop server
          System.IO.Directory.Delete(directory, true)
      }
    }

    testCase "consumer registration and sink correlations cannot publish as the source" <| fun () ->
      let graph = graph ()
      let pushed = ResizeArray<Facts.Observation>()
      let current, demand = register graph a pushed.Add
      use _demand = demand
      graph.PublishObservation(consumerProducer current.Binding, sample 99L "forged")
      |> Expect.equal "a registered consumer has no producer correlation" (Error Facts.Refusal.ForeignProducer)
      graph.PublishObservation(pa, sample 1L "owner") |> Expect.equal "the setup-bound producer can publish" (Ok())
      let fromSink = consumerProducer pushed[0].Binding
      graph.PublishObservation(fromSink, sample 100L "forged sink")
      |> Expect.equal "a sink-derived correlation cannot publish" (Error Facts.Refusal.ForeignProducer)
      graph.UnavailableObservation(fromSink, 101L, "forged unavailable")
      |> Expect.equal "a consumer cannot mark the source unavailable either" (Error Facts.Refusal.ForeignProducer)
      pushed |> Seq.map _.Value |> Seq.toList |> Expect.equal "only the producer's value arrived" [ Ok(sample 1L "owner") ]
      graph.Stop()

    testCase "producer publication before and after focus does not create observation demand" <| fun () ->
      let graph = graph ()
      graph.PublishObservation(pa, sample 1L "before focus") |> Expect.equal "a producer needs no consumer handle" (Ok())
      graph.ObservationDemanded a |> Expect.equal "publication alone does not demand execution" (Ok false)
      let pushed = ResizeArray<Facts.Observation>()
      let current, demand = register graph a pushed.Add
      current.Value |> Expect.equal "first focus sees the prior source fact" (Ok(sample 1L "before focus"))
      graph.PublishObservation(pa, sample 2L "during focus") |> Expect.equal "live focus receives producer updates" (Ok())
      demand.Dispose()
      graph.PublishObservation(pa, sample 3L "after focus") |> Expect.equal "producer remains bound after the last focus detaches" (Ok())
      graph.ObservationDemanded a |> Expect.equal "post-detach publication does not revive demand" (Ok false)
      pushed |> Seq.map _.Sequence |> Seq.toList |> Expect.equal "the detached sink receives no later update" [ Some 2L ]
      let next, nextDemand = register graph a ignore
      use _nextDemand = nextDemand
      next.Value |> Expect.equal "new focus sees the latest independently published source fact" (Ok(sample 3L "after focus"))
      graph.Stop()

    testCase "current and later delivery name the same subscription without replaying current" <| fun () ->
      let graph = graph ()
      let first, firstDemand = register graph a ignore
      use _firstDemand = firstDemand
      first.Sequence |> Expect.equal "no fabricated initial sequence" None
      first.Value |> Expect.equal "absence is explicit" (Error "Source has not published an observation.")
      graph.PublishObservation(pa, sample 1L "A") |> Expect.equal "source admitted" (Ok())
      let pushed = ResizeArray<Facts.Observation>()
      let current, demand = register graph a pushed.Add
      use _demand = demand
      current.Value |> Expect.equal "registration sees the existing fact" (Ok(sample 1L "A"))
      pushed.Count |> Expect.equal "current was not also pushed" 0
      graph.PublishObservation(pa, sample 2L "new A") |> Expect.equal "new source fact admitted" (Ok())
      pushed |> Seq.toList |> Expect.equal "one later push bound to this subscriber"
        [ { current with Sequence = Some 2L; Value = Ok(sample 2L "new A") } ]
      graph.Stop()

    testCase "A to B to A refuses a late old subscription and preserves current A" <| fun () ->
      let graph = graph ()
      let first, firstDemand = register graph a ignore
      graph.PublishObservation(pa, sample 1L "current A") |> Expect.equal "first A admitted" (Ok())
      graph.ObservationDemanded a |> Expect.equal "A is effectively demanded" (Ok true)
      firstDemand.Dispose()
      graph.ObservationDemanded a |> Expect.equal "last A subscription releases its Foundation demand" (Ok false)
      (graph.StoredResults <= 1) |> Expect.isTrue "a retained cache is not a demand census"
      let middle, middleDemand = register graph b ignore
      graph.PublishObservation(pb, sample 1L "current B") |> Expect.equal "B has its own serial" (Ok())
      middleDemand.Dispose()
      graph.ObservationDemanded b |> Expect.equal "last B subscription also releases its Foundation demand" (Ok false)
      let pushed = ResizeArray<Facts.Observation>()
      let current, demand = register graph a pushed.Add
      use _demand = demand
      current.Value |> Expect.equal "new A subscription sees the retained A fact" (Ok(sample 1L "current A"))
      (current.Binding.Subscription <> first.Binding.Subscription) |> Expect.isTrue "focus identity is new despite the same source"
      graph.PublishObservation(consumerProducer first.Binding, sample 99L "late old A") |> Expect.equal "disposed focus has no producer correlation" (Error Facts.Refusal.ForeignProducer)
      pushed.Count |> Expect.equal "late delivery did not reach the new focus" 0
      graph.PublishObservation(pa, sample 2L "new A") |> Expect.equal "new A remains live" (Ok())
      pushed.Count |> Expect.equal "only the new subscription's delivery arrived" 1
      graph.Stop()

    testCase "source incarnation differs from subscription and unknown nodes have no fallback" <| fun () ->
      let graph = graph ()
      let current, demand = register graph a ignore
      use _demand = demand
      let oldSource = { a with Incarnation = "A-old" }
      graph.PublishObservation({ pa with Source = oldSource }, sample 999L "old owner")
      |> Expect.equal "a larger serial cannot replace the bound incarnation" (Error Facts.Refusal.ForeignIncarnation)
      graph.RegisterObservation(oldSource, ignore) |> Result.map ignore
      |> Expect.equal "registration also refuses the old incarnation" (Error Facts.Refusal.ForeignIncarnation)
      let unknown = { a with Node = "unknown" }
      graph.RegisterObservation(unknown, ignore) |> Result.map ignore
      |> Expect.equal "an unbound node does not become local" (Error Facts.Refusal.UnknownSource)
      graph.UnavailableObservation({ pa with Source = unknown }, 1000L, "disconnected")
      |> Expect.equal "unavailability cannot enroll a node" (Error Facts.Refusal.UnknownSource)
      let faults = graph.Faults
      graph.ObservationDemanded oldSource |> Expect.equal "query refuses a foreign incarnation" (Error Facts.Refusal.ForeignIncarnation)
      graph.ObservationDemanded unknown |> Expect.equal "query refuses an unknown source" (Error Facts.Refusal.UnknownSource)
      graph.Faults |> Expect.equal "read-only demand queries do not count protocol faults" faults
      graph.PublishObservation(pa, sample 1L "current owner") |> Expect.equal "current source still works" (Ok())
      graph.Stop()

    testCase "duplicate conflicting and older source serials cannot change current" <| fun () ->
      let graph = graph ()
      let pushed = ResizeArray<Facts.Observation>()
      let current, demand = register graph a pushed.Add
      use _demand = demand
      graph.PublishObservation(pa, sample 5L "current") |> Expect.equal "new serial admitted" (Ok())
      graph.PublishObservation(pa, sample 5L "current") |> Expect.equal "exact duplicate is idempotent" (Error Facts.Refusal.Duplicate)
      graph.PublishObservation(pa, sample 5L "conflict") |> Expect.equal "same serial cannot change content" (Error Facts.Refusal.Conflict)
      graph.PublishObservation(pa, sample 4L "older") |> Expect.equal "older content refused" (Error Facts.Refusal.Older)
      pushed |> Seq.map _.Value |> Seq.toList |> Expect.equal "no rejected fact was delivered" [ Ok(sample 5L "current") ]
      graph.Stop()

    testCase "unavailable is a current fact that the same live owner can recover" <| fun () ->
      let graph = graph ()
      let pushed = ResizeArray<Facts.Observation>()
      let current, demand = register graph a pushed.Add
      use _demand = demand
      graph.UnavailableObservation(pa, 1L, "source disconnected") |> Expect.equal "known source can be unavailable" (Ok())
      let unavailable, otherDemand = register graph a ignore
      use _otherDemand = otherDemand
      unavailable.Value |> Expect.equal "no local sample substitutes for unavailable" (Error "source disconnected")
      unavailable.Sequence |> Expect.equal "unavailability retains the source serial" (Some 1L)
      graph.PublishObservation(pa, sample 2L "recovered") |> Expect.equal "same binding can recover" (Ok())
      pushed |> Seq.map _.Value |> Seq.toList |> Expect.equal "unavailability and recovery are both pushed"
        [ Error "source disconnected"; Ok(sample 2L "recovered") ]
      graph.Stop()

    testTask "detaching one focus and recycling leaves another focus globals and held build intact" {
      let stopping = new CancellationTokenSource()
      let composer = FakeComposer(observationOwners = [ pa; pb ], HoldBuilds = true)
      let graph = composer.Graph
      let hub = hubOf composer stopping.Token unpaused (ClockGate())
      let socket = FakeSocket()
      let running = hub.RunAsync(socket, CancellationToken.None)
      let! _ = readPrologue socket
      socket.Deliver(frame 1 (Build { Target = target; Reservation = "rsv" }))
      let! (token: CancellationToken) = composer.BuildEntered.WaitAsync(TimeSpan.FromSeconds 5.)
      let globalBefore = graph.Current()
      let first, firstDemand = register graph a ignore
      let pushed = ResizeArray<Facts.Observation>()
      let second, secondDemand = register graph a pushed.Add
      firstDemand.Dispose()
      graph.ObservationDemanded a |> Expect.equal "the independent subscription still demands A" (Ok true)
      for sequence in 1L .. int64 Facts.recycleBound do
        graph.PublishObservation(pa, sample sequence "A") |> Expect.equal "live demand remains admitted" (Ok())
      (graph.Epoch > 1UL) |> Expect.isTrue "the real core epoch recycled"
      pushed.Count |> Expect.equal "handover neither drops nor duplicates publications" Facts.recycleBound
      graph.Current() |> Expect.equal "global facts and their stamps are unchanged" globalBefore
      graph.StoredResults |> Expect.equal "only the two globals and one observation remain" 3
      graph.PublishObservation(consumerProducer first.Binding, sample (int64 Facts.recycleBound + 1L) "late")
      |> Expect.equal "epoch handover does not turn a disposed focus into a producer" (Error Facts.Refusal.ForeignProducer)
      token.IsCancellationRequested |> Expect.isFalse "focus release did not cancel the held build"
      composer.ReleaseBuild()
      socket.Hangup()
      do! settle running
      stopping.Cancel()
      secondDemand.Dispose()
      graph.ObservationDemanded a |> Expect.equal "last subscription releases the restored epoch's demand" (Ok false)
      graph.Stop()
      stopping.Dispose()
    }

    testCase "a callback detaching another focus prevents its queued delivery" <| fun () ->
      let graph = graph ()
      let mutable detach = ignore
      let first, firstDemand = register graph a (fun _ -> detach ())
      use _firstDemand = firstDemand
      let pushed = ResizeArray<Facts.Observation>()
      let _, secondDemand = register graph a pushed.Add
      detach <- secondDemand.Dispose
      graph.PublishObservation(pa, sample 1L "A") |> Expect.equal "publication admitted" (Ok())
      pushed.Count |> Expect.equal "a callback disposed before its turn is not invoked" 0
      graph.Stop()

    testCase "a reentrant newer publication prevents older delivery to remaining focuses" <| fun () ->
      let graph = graph ()
      let mutable publishNewer = ignore
      let first, firstDemand = register graph a (fun observation -> if observation.Sequence = Some 1L then publishNewer ())
      use _firstDemand = firstDemand
      let pushed = ResizeArray<Facts.Observation>()
      let _, secondDemand = register graph a pushed.Add
      use _secondDemand = secondDemand
      publishNewer <- fun () -> graph.PublishObservation(pa, sample 2L "newer") |> Expect.equal "nested publication admitted" (Ok())
      graph.PublishObservation(pa, sample 1L "older") |> Expect.equal "original publication admitted" (Ok())
      pushed |> Seq.map _.Sequence |> Seq.toList |> Expect.equal "remaining focus never rolls back after the newer value" [ Some 2L ]
      graph.Stop()

    testCase "graph shutdown permanently retires all source bindings" <| fun () ->
      let graph = graph ()
      let current, demand = register graph a ignore
      graph.Stop()
      demand.Dispose()
      graph.Drained |> Expect.isTrue "shutdown drains the foundation epoch"
      graph.PublishObservation(pa, sample 1L "late") |> Expect.equal "retired graph refuses publication" (Error Facts.Refusal.Retired)
      graph.RegisterObservation(a, ignore) |> Result.map ignore |> Expect.equal "retired graph cannot create a new focus" (Error Facts.Refusal.Retired)

    testCase "constructor bindings require one named incarnation per distinct node" <| fun () ->
      let create owners = Facts.Graph({ McpPort = 47759; Version = "test"; ProcessId = 99 }, "daemon-o", "", observationOwners = owners) |> ignore
      (fun () -> create [ pa; producer { a with Incarnation = "other" } ]) |> Expect.throwsT<ArgumentException> "two incarnations cannot be preadmitted for one node"
      (fun () -> create [ producer { a with Incarnation = "" } ]) |> Expect.throwsT<ArgumentException> "an absent incarnation cannot be bound"
      (fun () -> create [ { pa with Correlation = Guid.Empty } ]) |> Expect.throwsT<ArgumentException> "an absent producer correlation cannot be bound"
  ]
