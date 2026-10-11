/// Headless codec round trip under node (`npm test`).
///
/// Every Command case goes Frontend.Codec.encodeCommand -> Mock.Codec.decodeCommand
/// and every Event case goes Mock.Codec.encodeEvent -> Frontend.Codec.decodeEvent;
/// each must come back structurally equal. Union coverage is checked by
/// reflection, so a new case without a sample fails the run. Malformed frames
/// must be refused, never half-decoded.
module Bozzetto.Web.Tests.RoundTrip

open Fable.Core
open Microsoft.FSharp.Reflection
open Bozzetto.Web.Shared.Protocol

module FrontendCodec = Bozzetto.Web.Frontend.Codec
module BackendCodec = Bozzetto.Web.Mock.Codec
module DiagnosticText = Bozzetto.Web.Frontend.DiagnosticText

[<Emit("process.exitCode = $0")>]
let private setExitCode (code: int) : unit = jsNative

let mutable private failures = 0
let mutable private checks = 0

let private check (label: string) (ok: bool) =
  checks <- checks + 1
  if not ok then
    failures <- failures + 1
    printfn "FAIL %s" label

let private caseName (value: obj) (unionType: System.Type) =
  let case, _ = FSharpValue.GetUnionFields(value, unionType)
  case.Name

let private covers (label: string) (unionType: System.Type) (seen: string list) =
  for case in FSharpType.GetUnionCases unionType do
    check (label + " sample covers " + case.Name) (seen |> List.contains case.Name)

// ── Samples ────────────────────────────────────────────────────────────────

let private worker = { WorkerTarget.Host = "host-α"; Epoch = "epoch-0001" }
let private target = { SessionTarget.Worker = worker; Session = "session-日本-🎼" }
let private emptyTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }

let private artifact = {
  ArtifactSummary.Generation = System.Int64.MaxValue
  SourceVersion = "src \"quoted\" \\ back"
  ArtifactPath = "/tmp/a b/naïve.out"
  ArtifactSha256 = String.replicate 64 "f"
  Regions = [|
    { RegionRow.Identity = "common"; Witness = "changed"; Visits = System.Int32.MaxValue; Object = "compiled" }
    { RegionRow.Identity = "callable:M:f \"q\""; Witness = "retained"; Visits = 0; Object = "" }
    { RegionRow.Identity = "o-retired"; Witness = ""; Visits = System.Int32.MinValue; Object = "retired" }
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
  BackendError = Some "Backend refused:\r\n\tline two <script>literal text</script>\n\t日本語 🎼"
  FormatterError = Some "formatter"
  CleanupError = Some "cleanup"
  WorkerRetirementRequired = Some "retire"
  StatusError = Some "stale"
  WorkerError = Some ""
}

let private bareSession = {
  fullSession with
    Generation = System.Int64.MinValue
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
  ObserveResources 120
  ObserveResources 0
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
  AgentWork { Incarnation = "daemon-a"; Sequence = 1L; ExecutionHost = "local"; Capacity = 256; Runs = [||]; Presence = [||] }
  AgentWork { Incarnation = "daemon-b"; Sequence = 9007199254740993L; ExecutionHost = "local"; Capacity = 256
              Presence = [| { Name = "pi ü"; Source = "lease"; Member = "pi ü · /repo/clef"; Project = "/repo/clef"; Detail = "holds full_build"; SinceMs = 9007199254740993L }
                            { Name = "mcp:two"; Source = "member"; Member = "mcp:two"; Project = ""; Detail = "Verifier member · no report yet"; SinceMs = -1L } |]
              Runs = [| { Id = "run"; Member = "mcp:one"; Role = "Observer"; Name = "same"; Session = "pi"
                          ReportSequence = 2L; Model = "reported"; Endpoint = "LAN"; Project = "/repo"; Focus = "review"
                          Operation = "read"; Status = "disconnected"; UpdatedAtMs = 123L; Activity = [|"read"|]; OmittedActivity = 4L; Usage = None }
                        { Id = "run2"; Member = "mcp:two"; Role = "Verifier"; Name = "same"; Session = "pi2"
                          ReportSequence = 3L; Model = "reported2"; Endpoint = "paid"; Project = "/repo"; Focus = "audit"
                          Operation = "settled"; Status = "completed"; UpdatedAtMs = 124L; Activity = [|"settled"|]; OmittedActivity = 0L
                          Usage = Some { Input = 0L; Output = 0L; CacheRead = 0L; CacheWrite = 0L; Total = 0L; EstimateUsd = Some 0. } } |] }
  Resources {
    Sequence = 9007199254740993L; Status = "observing"; SampledAtMs = 1759420800123L; IntervalMs = 2000.; SampleCostMs = 12.5
    Metrics = [| { Name = "GPU unavailable"; Value = None; Unit = "percent" }; { Name = "RAM used"; Value = Some 4294967296.; Unit = "bytes" } |]
    Processes = [| {
      ProcessId = 42; ParentId = 1; StartTicks = "9007199254740993"; Name = "worker 日本"; Context = "external"
      CpuPercent = None; ResidentBytes = 10000000.; Threads = 8 } |]
    ProcessCount = 400; UnreadableCount = 2; OmittedCount = 397; Note = "Overlapping views; no summation." }
  Welcome { BridgeWelcome.ProtocolVersion = ProtocolVersion; DaemonVersion = "0.6.834"; StartedAtMs = 1759420800123L }
  Welcome { BridgeWelcome.ProtocolVersion = System.Int32.MaxValue; DaemonVersion = ""; StartedAtMs = System.Int64.MinValue }
  Snapshot { ComposerSnapshot.Revision = 0L; Worker = Unconfigured; Sessions = [||] }
  Snapshot { ComposerSnapshot.Revision = 1L; Worker = Idle; Sessions = [| bareSession |] }
  Snapshot {
    ComposerSnapshot.Revision = System.Int64.MaxValue
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
  Refused { CommandRefusal.Correlation = System.Int32.MaxValue; Target = target; Code = "busy"; Message = "ünïcode ⚠" }
  RunOutput {
    RunTranscript.Correlation = 7
    Target = target
    Generation = System.Int64.MinValue
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
    MachineAvailableBytes = System.Int64.MaxValue
    MachineTotalBytes = 66571993088L
    CpuPercent = 1e-7
    SampledAtMs = 1759420800000L
    RecordAtMs = 0L
    Processes = [||]
    Alarms = [||]
  }
  Health {
    DaemonHealth.Revision = 9007199254740993L
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
    SampledAtMs = System.Int64.MaxValue
    RecordAtMs = -1L
    Processes = [|
      { ProcessSample.ProcessId = 1; Role = "daemon"; ResidentBytes = 1L; CpuPercent = 0.0 }
      { ProcessSample.ProcessId = 2; Role = "composer-worker"; ResidentBytes = System.Int64.MaxValue; CpuPercent = 12.5 }
    |]
    Alarms = [| { HealthAlarm.Signal = "WorkerRss"; State = "Broken"; Message = "RSS ↑" } |]
  }
  Leases { LeaseBoard.Revision = 0L; Active = [||]; Queued = [||] }
  Leases {
    LeaseBoard.Revision = 9007199254740993L
    Active = [|
      { LeaseGrant.Id = "lease-1"; Kind = "full_build"; Holder = "gate-command-1"; GrantedAtMs = 1759420800000L; ExpiresAtMs = System.Int64.MaxValue }
    |]
    Queued = [| { LeaseWait.Kind = "test_suite_run"; Holder = "agent-ü"; RequestedAtMs = -1L } |]
  }
  Operations { OperationsBoard.Revision = 0L; Incarnation = ""; Entries = [||] }
  Operations {
    OperationsBoard.Revision = 9007199254740993L
    Incarnation = "daemon-ü"
    Entries = [|
      { OperationEntry.Sequence = System.Int64.MaxValue; AtMs = -1L; CallerLabel = "mcp:3f9c 🎼"; Operation = "run"; Session = "s"; Project = "/p/Ü.fidproj"
        Ok = true; Code = ""; Message = ""
        Run = Some { OperationRun.ExitCode = -2147483648; Generation = 7L; SourceVersion = "src"; StandardOutput = "[cut: 5 characters omitted]\nout\u0000"; StandardError = "err — 日本"; Dropped = false } }
      { OperationEntry.Sequence = 1L; AtMs = 0L; CallerLabel = "http"; Operation = "build"; Session = ""; Project = ""; Ok = false; Code = "invalid_reservation"; Message = "⚠"; Run = None }
      { OperationEntry.Sequence = 2L; AtMs = 1L; CallerLabel = "browser"; Operation = "run"; Session = "s"; Project = "p"; Ok = true; Code = ""; Message = ""
        Run = Some { OperationRun.ExitCode = 0; Generation = 0L; SourceVersion = ""; StandardOutput = ""; StandardError = ""; Dropped = true } } |] }
  Uptime { UptimeTick.UptimeMs = 0L }
  Uptime { UptimeTick.UptimeMs = 93784005L }
  Uptime { UptimeTick.UptimeMs = System.Int64.MaxValue }
]

// ── Malformed frames ───────────────────────────────────────────────────────

let private badEvents = [
  """{"event":"agent_work","incarnation":"d","sequence":1,"executionHost":"local","capacity":256,"runs":[]}"""
  "not json"
  "[]"
  "null"
  "{}"
  """{"event":"nope"}"""
  """{"event":"operations","revision":"1","incarnation":"d","entries":[{"sequence":"1"}]}"""
  """{"event":"operations","revision":"1","incarnation":"d","entries":[{"sequence":"1","atMs":"0","caller":"c","operation":"run","session":"","project":"","ok":true,"code":"","message":"","run":5}]}"""
  """{"event":"welcome","protocolVersion":1.5,"daemonVersion":"x","startedAtMs":"0"}"""
  """{"event":"welcome","protocolVersion":2147483648,"daemonVersion":"x","startedAtMs":"0"}"""
  """{"event":"welcome","protocolVersion":2,"daemonVersion":"x"}"""
  """{"event":"welcome","protocolVersion":2,"daemonVersion":"x","startedAtMs":1759420800123}"""
  """{"event":"leases","active":[],"queued":[{"kind":"k","holder":"h","requestedAtMs":5}]}"""
  """{"event":"leases","active":[],"queued":[{"kind":"k","holder":"h","requestedAtMs":"9223372036854775808"}]}"""
  """{"event":"leases","active":[],"queued":[{"kind":"k","holder":"h","requestedAtMs":"+5"}]}"""
  """{"event":"leases","active":[],"queued":[{"kind":"k","holder":"h","requestedAtMs":" 5"}]}"""
  """{"event":"leases","active":[],"queued":[{"kind":"k","holder":"h","requestedAtMs":"5.0"}]}"""
  """{"event":"leases","active":[],"queued":[{"kind":"k","holder":"h","requestedAtMs":""}]}"""
  """{"event":"leases","active":[],"queued":[{"kind":"k","holder":"h","requestedAtMs":"-"}]}"""
  """{"event":"snapshot","revision":"1","worker":{"state":"sleeping"},"sessions":[]}"""
  """{"event":"accepted","correlation":1,"target":{"host":"h","epoch":"e"},"completion":{"kind":"session_opened"}}"""
  """{"event":"accepted","correlation":1,"target":{"host":"h","epoch":"e","session":"s"},"completion":{"kind":"later"}}"""
  """{"event":"uptime"}"""
  """{"event":"uptime","uptimeMs":93784005}"""
  """{"event":"uptime","uptimeMs":"9223372036854775808"}"""
  """{"event":"uptime","uptimeMs":"1.5"}"""
]

let private badCommands = [
  "not json"
  """{"command":"request_snapshot"}"""
  """{"correlation":0,"command":"request_snapshot"}"""
  """{"correlation":-4,"command":"request_snapshot"}"""
  """{"correlation":1.5,"command":"request_snapshot"}"""
  """{"correlation":1,"command":"explode"}"""
  """{"correlation":1,"command":"open_project","project":5}"""
  """{"correlation":1,"command":"run","target":{"host":"h","epoch":"e","session":"s"},"arguments":["a",1]}"""
  """{"correlation":1,"command":"reserve","target":{"host":"h","epoch":"e"},"label":"x"}"""
  """{"correlation":1,"command":"retire_worker","worker":{"host":"h"}}"""
]

// ── Run ────────────────────────────────────────────────────────────────────

let private run () =
  let diagnosticCases = [
    "", []
    "[ERROR] AX4001 callback refused\n", [ "error", "ERROR"; "error", "AX4001" ]
    "\t[WARN] compiler warning\r\n", [ "warning", "WARN" ]
    " [INFO] informational message", [ "info", "INFO" ]
    "error CCS8018: invalid suffix\n", [ "error", "error"; "error", "CCS8018" ]
    "/src/a.clef:12: warning CCS8019: width alias\r\n", [ "warning", "warning"; "warning", "CCS8019" ]
    "C:\\work\\b.clef:7: info CCS1001: unreachable", [ "info", "info"; "info", "CCS1001" ]
    "An error inside a message is ordinary text.\nThe [WARN] word is also ordinary.\n", []
    "[ERROR] Function <script>literal text</script>\n\tRegion = 'packet\n\t日本語 🎼", [ "error", "ERROR" ]
  ]
  for trace, expected in diagnosticCases do
    let segments = DiagnosticText.segments trace
    check ("diagnostic original text preserved " + trace) (segments |> Array.map (fun token -> token.text) |> String.concat "" = trace)
    let marked = segments |> Array.filter (fun token -> token.severity <> "") |> Array.map (fun token -> token.severity, token.text) |> Array.toList
    check ("only explicit diagnostic prefixes colored " + trace) (marked = expected)
  for newline in [ "\n"; "\r\n" ] do
    for indent in [ ""; " "; "\t"; "\t  " ] do
      for label, kind in [ "ERR", "error"; "WARNING", "warning"; "WRN", "warning"; "INF", "info"; "info", "info" ] do
        let trace = indent + "[" + label + "] <&> diagnostic" + newline + "\tplain error warning info" + newline
        let segments = DiagnosticText.segments trace
        check "severity aliases preserve every line/tab/text character" (segments |> Array.map (fun token -> token.text) |> String.concat "" = trace)
        check "severity aliases mark only their explicit prefix" (segments |> Array.filter (fun token -> token.severity <> "") |> Array.map (fun token -> token.severity, token.text) = [| kind, label |])

  commands |> List.iteri (fun i command ->
    let frame = { CommandFrame.Correlation = (if i = 0 then System.Int32.MaxValue else i + 1); Command = command }
    let wire = FrontendCodec.encodeCommand frame
    match BackendCodec.decodeCommand wire with
    | Ok decoded -> check ("command round trip " + wire) (decoded = frame)
    | Error message -> check ("command decodes " + wire + " (" + message + ")") false)
  covers "Command" typeof<Command> (commands |> List.map (fun c -> caseName (box c) typeof<Command>))

  for event in events do
    let wire = BackendCodec.encodeEvent event
    match FrontendCodec.decodeEvent wire with
    | Ok decoded -> check ("event round trip " + wire) (decoded = event)
    | Error message -> check ("event decodes " + wire + " (" + message + ")") false
  covers "Event" typeof<Event> (events |> List.map (fun e -> caseName (box e) typeof<Event>))
  covers "Completion" typeof<Completion> (completions |> List.map (fun c -> caseName (box c) typeof<Completion>))
  covers "WorkerState" typeof<WorkerState> [
    for event in events do
      match event with
      | Snapshot s -> caseName (box s.Worker) typeof<WorkerState>
      | _ -> ()
  ]

  for wire in badEvents do
    check ("malformed event refused " + wire) (FrontendCodec.decodeEvent wire |> Result.isError)
  for wire in badCommands do
    check ("malformed command refused " + wire) (BackendCodec.decodeCommand wire |> Result.isError)
  check "correlation echoed from an undecodable command" (BackendCodec.correlationOf """{"correlation":12,"command":"explode"}""" = 12)
  check "no correlation from non-JSON" (BackendCodec.correlationOf "nope" = 0)

  printfn "codec round trip: %d checks, %d failures (%d commands, %d events, %d malformed)" checks failures commands.Length events.Length (badEvents.Length + badCommands.Length)
  setExitCode (if failures = 0 && checks > 0 then 0 else 1)

run ()
