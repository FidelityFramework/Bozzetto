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

// ── The UI side, in .NET ───────────────────────────────────────────────────

/// The .NET twin of bozzetto-web/src/Frontend/Codec.fs, as Mock/Codec.fs is
/// the Fable twin of the backend codec: encodes commands and strictly decodes
/// events, so every case can make the round trip inside this suite.
module private Ui =

  type private Decoder() =
    member _.Bind(result: Result<'a, string>, next: 'a -> Result<'b, string>) = Result.bind next result
    member _.Return(value: 'a) : Result<'a, string> = Ok value

  let private decode = Decoder()

  let private targetFields (t: SessionTarget) =
    [ "host", JsonValue.String t.Worker.Host; "epoch", JsonValue.String t.Worker.Epoch; "session", JsonValue.String t.Session ]

  let encodeCommand (frame: CommandFrame) : string =
    let fields =
      match frame.Command with
      | RequestSnapshot -> [ "command", JsonValue.String "request_snapshot" ]
      | OpenProject project -> [ "command", JsonValue.String "open_project"; "project", JsonValue.String project ]
      | Reserve edit ->
        [ "command", JsonValue.String "reserve"; "target", JsonValue.Object(targetFields edit.Target); "label", JsonValue.String edit.Label ]
      | Build build ->
        [ "command", JsonValue.String "build"; "target", JsonValue.Object(targetFields build.Target)
          "reservation", JsonValue.String build.Reservation ]
      | Run run ->
        [ "command", JsonValue.String "run"; "target", JsonValue.Object(targetFields run.Target)
          "arguments", JsonValue.Array(run.Arguments |> Array.toList |> List.map JsonValue.String) ]
      | Cancel target -> [ "command", JsonValue.String "cancel"; "target", JsonValue.Object(targetFields target) ]
      | CloseSession target -> [ "command", JsonValue.String "close_session"; "target", JsonValue.Object(targetFields target) ]
      | RetireWorker worker ->
        [ "command", JsonValue.String "retire_worker"
          "worker", JsonValue.Object [ "host", JsonValue.String worker.Host; "epoch", JsonValue.String worker.Epoch ] ]
    JsonValue.Object(("correlation", JsonValue.ofInt64 (int64 frame.Correlation)) :: fields) |> Json.serialize

  let private field name o = JsonValue.prop name o

  let private text name o =
    match field name o with
    | Some(JsonValue.String value) -> Ok value
    | _ -> Error(name + ": expected a string")

  let private optionalText name o =
    match field name o with
    | Some JsonValue.Null -> Ok None
    | Some(JsonValue.String value) -> Ok(Some value)
    | _ -> Error(name + ": expected a string or null")

  let private flag name o =
    match field name o with
    | Some(JsonValue.Bool value) -> Ok value
    | _ -> Error(name + ": expected a boolean")

  let private whole name o =
    match field name o |> Option.bind JsonValue.tryAsInt64 with
    | Some value when value >= int64 Int32.MinValue && value <= int64 Int32.MaxValue -> Ok(int32 value)
    | _ -> Error(name + ": expected an int32")

  let private real name o =
    match field name o |> Option.bind JsonValue.asNumber with
    | Some value when Double.IsFinite value -> Ok value
    | _ -> Error(name + ": expected a finite number")

  let private wide name o =
    match field name o with
    | Some(JsonValue.String raw) ->
      let digits = if raw.StartsWith '-' then raw.Substring 1 else raw
      match Int64.TryParse(raw, Globalization.NumberStyles.AllowLeadingSign, Globalization.CultureInfo.InvariantCulture) with
      | true, value when digits.Length > 0 && digits |> Seq.forall Char.IsAsciiDigit && not (raw.StartsWith '+') -> Ok value
      | _ -> Error(name + ": expected an int64 decimal string")
    | _ -> Error(name + ": expected an int64 decimal string")

  let private items name (item: JsonValue -> Result<'a, string>) o =
    match field name o with
    | Some(JsonValue.Array values) ->
      List.foldBack (fun value acc -> Result.bind (fun rest -> item value |> Result.map (fun head -> head :: rest)) acc) values (Ok [])
      |> Result.map List.toArray
    | _ -> Error(name + ": expected an array")

  let private nested name (inner: JsonValue -> Result<'a, string>) o =
    match field name o with
    | Some(JsonValue.Object _ as child) -> inner child
    | _ -> Error(name + ": expected an object")

  let private sessionTarget o = decode {
    let! host = text "host" o
    let! epoch = text "epoch" o
    let! session = text "session" o
    return { SessionTarget.Worker = { WorkerTarget.Host = host; Epoch = epoch }; Session = session }
  }

  let private artifact o = decode {
    let! generation = wide "generation" o
    let! sourceVersion = text "sourceVersion" o
    let! artifactPath = text "artifactPath" o
    let! artifactSha256 = text "artifactSha256" o
    let! compiled = whole "compiledObjects" o
    let! reused = whole "reusedObjects" o
    let! retired = whole "retiredObjects" o
    let! changed = whole "changedWitnesses" o
    return {
      ArtifactSummary.Generation = generation
      SourceVersion = sourceVersion
      ArtifactPath = artifactPath
      ArtifactSha256 = artifactSha256
      CompiledObjects = compiled
      ReusedObjects = reused
      RetiredObjects = retired
      ChangedWitnesses = changed
    }
  }

  let private session o = decode {
    let! target = sessionTarget o
    let! generation = wide "generation" o
    let! project = text "project" o
    let! closed = flag "closed" o
    let! busy = flag "busy" o
    let! statusFresh = flag "statusFresh" o
    let! revocationPending = flag "revocationPending" o
    let! cleanupPending = flag "cleanupPending" o
    let! formatterCleanupPending = flag "formatterCleanupPending" o
    let! current =
      match field "current" o with
      | Some JsonValue.Null -> Ok None
      | Some(JsonValue.Object _ as value) -> artifact value |> Result.map Some
      | _ -> Error "current: expected an object or null"
    let! backendError = optionalText "backendError" o
    let! formatterError = optionalText "formatterError" o
    let! cleanupError = optionalText "cleanupError" o
    let! workerRetirementRequired = optionalText "workerRetirementRequired" o
    let! statusError = optionalText "statusError" o
    let! workerError = optionalText "workerError" o
    return {
      SessionStatus.Target = target
      Generation = generation
      Project = project
      Closed = closed
      Busy = busy
      StatusFresh = statusFresh
      RevocationPending = revocationPending
      CleanupPending = cleanupPending
      FormatterCleanupPending = formatterCleanupPending
      Current = current
      BackendError = backendError
      FormatterError = formatterError
      CleanupError = cleanupError
      WorkerRetirementRequired = workerRetirementRequired
      StatusError = statusError
      WorkerError = workerError
    }
  }

  let private worker o =
    text "state" o
    |> Result.bind (function
      | "unconfigured" -> Ok Unconfigured
      | "idle" -> Ok Idle
      | "running" ->
        decode {
          let! host = text "host" o
          let! epoch = text "epoch" o
          let! compilerVersion = text "compilerVersion" o
          let! pid =
            match field "processId" o with
            | Some JsonValue.Null -> Ok None
            | _ -> whole "processId" o |> Result.map Some
          return Running { WorkerInfo.Target = { WorkerTarget.Host = host; Epoch = epoch }; CompilerVersion = compilerVersion; ProcessId = pid }
        }
      | other -> Error("state: unknown " + other))

  let private completion o =
    text "kind" o
    |> Result.bind (function
      | "session_opened" -> Ok SessionOpened
      | "edit_reserved" -> text "reservation" o |> Result.map EditReserved
      | "reservation_built" -> nested "artifact" artifact o |> Result.map ReservationBuilt
      | "run_finished" -> whole "exitCode" o |> Result.map RunFinished
      | "work_canceled" -> Ok WorkCanceled
      | "session_closed" -> Ok SessionClosed
      | "worker_retired" -> Ok WorkerRetired
      | "snapshot_sent" -> Ok SnapshotSent
      | other -> Error("kind: unknown " + other))

  let private health o = decode {
    let! version = text "version" o
    let! processId = whole "processId" o
    let! mcpPort = whole "mcpPort" o
    let! overall = text "overall" o
    let! pressure = text "memoryPressure" o
    let! resident = wide "residentBytes" o
    let! available = wide "machineAvailableBytes" o
    let! total = wide "machineTotalBytes" o
    let! cpu = real "cpuPercent" o
    let! processes =
      items "processes" (fun p -> decode {
        let! pid = whole "processId" p
        let! role = text "role" p
        let! rss = wide "residentBytes" p
        let! cpu = real "cpuPercent" p
        return { ProcessSample.ProcessId = pid; Role = role; ResidentBytes = rss; CpuPercent = cpu }
      }) o
    let! alarms =
      items "alarms" (fun a -> decode {
        let! signal = text "signal" a
        let! state = text "state" a
        let! message = text "message" a
        return { HealthAlarm.Signal = signal; State = state; Message = message }
      }) o
    return {
      DaemonHealth.Version = version
      ProcessId = processId
      McpPort = mcpPort
      Overall = overall
      MemoryPressure = pressure
      ResidentBytes = resident
      MachineAvailableBytes = available
      MachineTotalBytes = total
      CpuPercent = cpu
      Processes = processes
      Alarms = alarms
    }
  }

  let private leases o = decode {
    let! active =
      items "active" (fun l -> decode {
        let! id = text "id" l
        let! kind = text "kind" l
        let! holder = text "holder" l
        let! granted = wide "grantedAtMs" l
        let! expires = wide "expiresAtMs" l
        return { LeaseGrant.Id = id; Kind = kind; Holder = holder; GrantedAtMs = granted; ExpiresAtMs = expires }
      }) o
    let! queued =
      items "queued" (fun q -> decode {
        let! kind = text "kind" q
        let! holder = text "holder" q
        let! requested = wide "requestedAtMs" q
        return { LeaseWait.Kind = kind; Holder = holder; RequestedAtMs = requested }
      }) o
    return { LeaseBoard.Active = active; Queued = queued }
  }

  let decodeEvent (payload: string) : Result<Event, string> =
    match Json.parse payload with
    | Error reason -> Error reason
    | Ok o ->
      text "event" o
      |> Result.bind (function
        | "welcome" ->
          decode {
            let! version = whole "protocolVersion" o
            let! daemon = text "daemonVersion" o
            let! startedAt = wide "startedAtMs" o
            return Welcome { BridgeWelcome.ProtocolVersion = version; DaemonVersion = daemon; StartedAtMs = startedAt }
          }
        | "snapshot" ->
          decode {
            let! revision = wide "revision" o
            let! state = nested "worker" worker o
            let! sessions = items "sessions" session o
            return Snapshot { ComposerSnapshot.Revision = revision; Worker = state; Sessions = sessions }
          }
        | "accepted" ->
          decode {
            let! correlation = whole "correlation" o
            let! target = nested "target" sessionTarget o
            let! completed = nested "completion" completion o
            return Accepted { CommandAcceptance.Correlation = correlation; Target = target; Completion = completed }
          }
        | "refused" ->
          decode {
            let! correlation = whole "correlation" o
            let! target = nested "target" sessionTarget o
            let! code = text "code" o
            let! message = text "message" o
            return Refused { CommandRefusal.Correlation = correlation; Target = target; Code = code; Message = message }
          }
        | "run_output" ->
          decode {
            let! correlation = whole "correlation" o
            let! target = nested "target" sessionTarget o
            let! generation = wide "generation" o
            let! sourceVersion = text "sourceVersion" o
            let! exitCode = whole "exitCode" o
            let! stdout = text "standardOutput" o
            let! stderr = text "standardError" o
            return RunOutput {
              RunTranscript.Correlation = correlation
              Target = target
              Generation = generation
              SourceVersion = sourceVersion
              ExitCode = exitCode
              StandardOutput = stdout
              StandardError = stderr
            }
          }
        | "health" -> health o |> Result.map Health
        | "leases" -> leases o |> Result.map Leases
        | "uptime" -> wide "uptimeMs" o |> Result.map (fun uptimeMs -> Uptime { UptimeTick.UptimeMs = uptimeMs })
        | other -> Error("event: unknown " + other))

// ── The samples of bozzetto-web/tests/RoundTrip.fs ─────────────────────────

let private worker = { WorkerTarget.Host = "host-α"; Epoch = "epoch-0001" }
let private target = { SessionTarget.Worker = worker; Session = "session-日本-🎼" }
let private emptyTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }

let private artifact = {
  ArtifactSummary.Generation = Int64.MaxValue
  SourceVersion = "src \"quoted\" \\ back"
  ArtifactPath = "/tmp/a b/naïve.out"
  ArtifactSha256 = String.replicate 64 "f"
  CompiledObjects = Int32.MaxValue
  ReusedObjects = 0
  RetiredObjects = Int32.MinValue
  ChangedWitnesses = 3
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
    DaemonHealth.Version = "0.6.834"
    ProcessId = 4242
    McpPort = 47749
    Overall = "Healthy"
    MemoryPressure = "Normal"
    ResidentBytes = 0L
    MachineAvailableBytes = Int64.MaxValue
    MachineTotalBytes = 66571993088L
    CpuPercent = 1e-7
    Processes = [||]
    Alarms = [||]
  }
  Health {
    DaemonHealth.Version = "v"
    ProcessId = 1
    McpPort = 47759
    Overall = "Degraded"
    MemoryPressure = "Critical"
    ResidentBytes = 55_000_000_000L
    MachineAvailableBytes = 1L
    MachineTotalBytes = 2L
    CpuPercent = 99.5
    Processes = [|
      { ProcessSample.ProcessId = 1; Role = "daemon"; ResidentBytes = 1L; CpuPercent = 0.0 }
      { ProcessSample.ProcessId = 2; Role = "composer-worker"; ResidentBytes = Int64.MaxValue; CpuPercent = 12.5 }
    |]
    Alarms = [| { HealthAlarm.Signal = "WorkerRss"; State = "Broken"; Message = "RSS ↑" } |]
  }
  Leases { LeaseBoard.Active = [||]; Queued = [||] }
  Leases {
    LeaseBoard.Active = [|
      { LeaseGrant.Id = "lease-1"; Kind = "full_build"; Holder = "gate-command-1"; GrantedAtMs = 1759420800000L; ExpiresAtMs = Int64.MaxValue }
    |]
    Queued = [| { LeaseWait.Kind = "test_suite_run"; Holder = "agent-ü"; RequestedAtMs = -1L } |]
  }
  Uptime { UptimeTick.UptimeMs = 0L }
  Uptime { UptimeTick.UptimeMs = 93784005L }
  Uptime { UptimeTick.UptimeMs = Int64.MaxValue }
]

/// The frames bozzetto-web's Fable codecs produce for the samples above:
/// Frontend.Codec.encodeCommand and Mock.Codec.encodeEvent, run under node
/// from bozzetto-web/output-test. Regenerate them there if a sample changes.
let private commandWires = [
  """{"correlation":2147483647,"command":"request_snapshot"}"""
  """{"correlation":2,"command":"open_project","project":"/abs/Ünï 🎼.fidproj"}"""
  """{"correlation":3,"command":"open_project","project":""}"""
  """{"correlation":4,"command":"reserve","target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"label":"edit: \"main\" — ✓"}"""
  """{"correlation":5,"command":"build","target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"reservation":"rsv-0123"}"""
  """{"correlation":6,"command":"run","target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"arguments":[]}"""
  """{"correlation":7,"command":"run","target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"arguments":["a b","\"q\"","🎼","","\\"]}"""
  """{"correlation":8,"command":"cancel","target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"}}"""
  """{"correlation":9,"command":"close_session","target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"}}"""
  """{"correlation":10,"command":"retire_worker","worker":{"host":"host-α","epoch":"epoch-0001"}}"""
]

let private eventWires = [
  """{"event":"welcome","protocolVersion":3,"daemonVersion":"0.6.834","startedAtMs":"1759420800123"}"""
  """{"event":"welcome","protocolVersion":2147483647,"daemonVersion":"","startedAtMs":"-9223372036854775808"}"""
  """{"event":"snapshot","revision":"0","worker":{"state":"unconfigured"},"sessions":[]}"""
  """{"event":"snapshot","revision":"1","worker":{"state":"idle"},"sessions":[{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼","generation":"-9223372036854775808","project":"/home/ü/Ünïcode.fidproj","closed":false,"busy":false,"statusFresh":true,"revocationPending":false,"cleanupPending":false,"formatterCleanupPending":false,"current":null,"backendError":null,"formatterError":null,"cleanupError":null,"workerRetirementRequired":null,"statusError":null,"workerError":null}]}"""
  """{"event":"snapshot","revision":"9223372036854775807","worker":{"state":"running","host":"host-α","epoch":"epoch-0001","compilerVersion":"0.0.2+abc","processId":31337},"sessions":[{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼","generation":"9007199254740993","project":"/home/ü/Ünïcode.fidproj","closed":true,"busy":true,"statusFresh":false,"revocationPending":true,"cleanupPending":true,"formatterCleanupPending":true,"current":{"generation":"9223372036854775807","sourceVersion":"src \"quoted\" \\ back","artifactPath":"/tmp/a b/naïve.out","artifactSha256":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff","compiledObjects":2147483647,"reusedObjects":0,"retiredObjects":-2147483648,"changedWitnesses":3},"backendError":"backend\nline two","formatterError":"formatter","cleanupError":"cleanup","workerRetirementRequired":"retire","statusError":"stale","workerError":""},{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼","generation":"-9223372036854775808","project":"/home/ü/Ünïcode.fidproj","closed":false,"busy":false,"statusFresh":true,"revocationPending":false,"cleanupPending":false,"formatterCleanupPending":false,"current":null,"backendError":null,"formatterError":null,"cleanupError":null,"workerRetirementRequired":null,"statusError":null,"workerError":null}]}"""
  """{"event":"snapshot","revision":"2","worker":{"state":"running","host":"host-α","epoch":"epoch-0001","compilerVersion":"","processId":null},"sessions":[]}"""
  """{"event":"accepted","correlation":1,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"session_opened"}}"""
  """{"event":"accepted","correlation":2,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"edit_reserved","reservation":"rsv-ü"}}"""
  """{"event":"accepted","correlation":3,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"reservation_built","artifact":{"generation":"9223372036854775807","sourceVersion":"src \"quoted\" \\ back","artifactPath":"/tmp/a b/naïve.out","artifactSha256":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff","compiledObjects":2147483647,"reusedObjects":0,"retiredObjects":-2147483648,"changedWitnesses":3}}}"""
  """{"event":"accepted","correlation":4,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"run_finished","exitCode":-1}}"""
  """{"event":"accepted","correlation":5,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"work_canceled"}}"""
  """{"event":"accepted","correlation":6,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"session_closed"}}"""
  """{"event":"accepted","correlation":7,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"worker_retired"}}"""
  """{"event":"accepted","correlation":8,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"completion":{"kind":"snapshot_sent"}}"""
  """{"event":"refused","correlation":0,"target":{"host":"","epoch":"","session":""},"code":"invalid_request","message":"Frame is not JSON."}"""
  """{"event":"refused","correlation":2147483647,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"code":"busy","message":"ünïcode ⚠"}"""
  """{"event":"run_output","correlation":7,"target":{"host":"host-α","epoch":"epoch-0001","session":"session-日本-🎼"},"generation":"-9223372036854775808","sourceVersion":"src-2","exitCode":-2147483648,"standardOutput":"line one\r\nline two\ttab — 日本語 🎼\u0000nul","standardError":""}"""
  """{"event":"health","version":"0.6.834","processId":4242,"mcpPort":47749,"overall":"Healthy","memoryPressure":"Normal","residentBytes":"0","machineAvailableBytes":"9223372036854775807","machineTotalBytes":"66571993088","cpuPercent":1e-7,"processes":[],"alarms":[]}"""
  """{"event":"health","version":"v","processId":1,"mcpPort":47759,"overall":"Degraded","memoryPressure":"Critical","residentBytes":"55000000000","machineAvailableBytes":"1","machineTotalBytes":"2","cpuPercent":99.5,"processes":[{"processId":1,"role":"daemon","residentBytes":"1","cpuPercent":0},{"processId":2,"role":"composer-worker","residentBytes":"9223372036854775807","cpuPercent":12.5}],"alarms":[{"signal":"WorkerRss","state":"Broken","message":"RSS ↑"}]}"""
  """{"event":"leases","active":[],"queued":[]}"""
  """{"event":"leases","active":[{"id":"lease-1","kind":"full_build","holder":"gate-command-1","grantedAtMs":"1759420800000","expiresAtMs":"9223372036854775807"}],"queued":[{"kind":"test_suite_run","holder":"agent-ü","requestedAtMs":"-1"}]}"""
  """{"event":"uptime","uptimeMs":"0"}"""
  """{"event":"uptime","uptimeMs":"93784005"}"""
  """{"event":"uptime","uptimeMs":"9223372036854775807"}"""
]

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

let private sampleHealth = {
  DaemonHealth.Version = "0.6.834-test"
  ProcessId = 99
  McpPort = 47759
  Overall = "Healthy"
  MemoryPressure = "Normal"
  ResidentBytes = 1L
  MachineAvailableBytes = 2L
  MachineTotalBytes = 3L
  CpuPercent = 0.5
  Processes = [||]
  Alarms = [||]
}

// ── Fakes for the connection contract ──────────────────────────────────────

/// A page at the other end of the socket. Records what the bridge wrote and
/// whether anything was written after the bridge closed the socket.
type private FakeSocket() =
  let inbound = Channel.CreateUnbounded<Inbound>()
  let written = Channel.CreateUnbounded<string>()
  let mutable closed = 0
  let mutable lateWrites = 0
  member _.Deliver(payload: string) = inbound.Writer.TryWrite(Inbound.Text payload) |> ignore
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
      | Error message -> failtestf "the page cannot decode %s: %s" text message
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

/// The supervisor as the bridge sees it. A build is held until the test
/// releases it, so a socket can close while the build is in flight. Health and
/// lease reads are counted: the bridge may read them only to answer a connect
/// or a RequestSnapshot.
type private FakeComposer() =
  let changed = Microsoft.FSharp.Control.Event<unit>()
  let buildEntered = TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously)
  let buildReleased = TaskCompletionSource<ComposerResponse>(TaskCreationOptions.RunContinuationsAsynchronously)
  let mutable revision = 1L
  let mutable healthReads = 0
  let mutable leaseReads = 0
  member val HoldBuilds = false with get, set
  member _.HealthReads = Volatile.Read &healthReads
  member _.LeaseReads = Volatile.Read &leaseReads
  member _.Revision
    with get () = Volatile.Read &revision
    and set value = Volatile.Write(&revision, value)
  member _.BuildEntered = buildEntered.Task
  member _.ReleaseBuild() = buildReleased.TrySetResult(response (Result.Ok(Built accepted))) |> ignore
  member _.Change() = changed.Trigger()
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
    Health =
      fun () ->
        Interlocked.Increment &healthReads |> ignore
        sampleHealth
    Leases =
      fun () ->
        Interlocked.Increment &leaseReads |> ignore
        { LeaseBoard.Active = [||]; Queued = [||] }
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

    testList "golden frames shared with bozzetto-web's Fable codecs" [
      for index, (command, wire) in List.zip commands commandWires |> List.indexed ->
        test (sprintf "command %d (%s) decodes from the frontend's exact bytes" index (caseName (box command) typeof<Command>)) {
          let expected = { CommandFrame.Correlation = (if index = 0 then Int32.MaxValue else index + 1); Command = command }
          Codec.decodeCommand wire |> Expect.equal "the frontend's frame decodes to the sample" (Ok expected)
        }
      for index, (event, wire) in List.zip events eventWires |> List.indexed ->
        test (sprintf "event %d (%s) encodes to the mock's exact bytes" index (caseName (box event) typeof<Event>)) {
          Codec.encodeEvent event |> Expect.equal "byte-identical to Mock.Codec.encodeEvent" wire
          Ui.decodeEvent wire |> Expect.equal "the page reads it back" (Ok event)
        }
      yield test "the samples cover every case of every wire union" {
        let covered (unionType: Type) (seen: string list) =
          for case in FSharpType.GetUnionCases unionType do
            seen |> List.contains case.Name |> Expect.isTrue (unionType.Name + " sample covers " + case.Name)
        covered typeof<Command> (commands |> List.map (fun c -> caseName (box c) typeof<Command>))
        covered typeof<Event> (events |> List.map (fun e -> caseName (box e) typeof<Event>))
        covered typeof<Completion> (completions |> List.map (fun c -> caseName (box c) typeof<Completion>))
        covered typeof<WorkerState> [
          for event in events do
            match event with
            | Snapshot s -> caseName (box s.Worker) typeof<WorkerState>
            | _ -> ()
        ]
      }
    ]

    testList "codec" [
      testPropertyWithConfig wireConfig "every command frame the page can send decodes to itself" <|
        fun (seed: int32) (command: Command) ->
          let correlation = if seed = Int32.MinValue then 1 else max 1 (abs seed)
          let frame = { CommandFrame.Correlation = correlation; Command = command }
          Codec.decodeCommand (Ui.encodeCommand frame) = Ok frame

      testPropertyWithConfig wireConfig "every event the backend can send reaches the page unchanged" <|
        fun (event: Event) -> Ui.decodeEvent (Codec.encodeEvent event) = Ok event

      testPropertyWithConfig wireConfig "a finite float travels as a JSON number that parses back to itself" <|
        fun (value: float) ->
          match Codec.jsNumberText value with
          | Some spelling ->
            Result.isOk (JsonNumberLiteral.tryCreate spelling)
            && Double.Parse(spelling, Globalization.CultureInfo.InvariantCulture) = value
          | None -> false

      test "floats are spelled as JSON.stringify spells them" {
        let cases = [
          0.0, "0"; -0.0, "0"; 1.0, "1"; -1.0, "-1"; 0.1, "0.1"; 1.0 / 3.0, "0.3333333333333333"
          2.0 / 3.0, "0.6666666666666666"; 100.0, "100"; 1e20, "100000000000000000000"; 1e21, "1e+21"
          1.5e21, "1.5e+21"; 123456789012345680000.0, "123456789012345680000"; 1e-6, "0.000001"; 1e-7, "1e-7"
          1.5e-7, "1.5e-7"; 0.000001234, "0.000001234"; 5e-324, "5e-324"; 1.7976931348623157e308, "1.7976931348623157e+308"
          -2.5e-10, "-2.5e-10"; 4.35, "4.35"; 0.30000000000000004, "0.30000000000000004"; 9007199254740993.0, "9007199254740992"
          1e15, "1000000000000000"; 1e16, "10000000000000000"; 12345600.0, "12345600" ]
        for value, spelling in cases do
          Codec.jsNumberText value |> Expect.equal (sprintf "spelling of %s" spelling) (Some spelling)
        for value in [ nan; infinity; -infinity ] do
          Codec.jsNumberText value |> Expect.isNone "JSON.stringify writes a non-finite number as null"
      }

      test "control characters travel escaped and arrive unchanged" {
        let text = String [| for c in 0 .. 31 -> char c |] + "\u007f  "
        let run = RunOutput { RunTranscript.Correlation = 1; Target = target; Generation = 0L; SourceVersion = ""; ExitCode = 0; StandardOutput = text; StandardError = "" }
        let wire = Codec.encodeEvent run
        wire |> Seq.exists (fun c -> c < ' ') |> Expect.isFalse "no raw control character on the wire"
        Ui.decodeEvent wire |> Expect.equal "the page reads the same text" (Ok run)
      }

      test "a lone surrogate cannot reach the wire; it travels as U+FFFD" {
        let run = RunOutput { RunTranscript.Correlation = 1; Target = target; Generation = 0L; SourceVersion = ""; ExitCode = 0; StandardOutput = "a\uD800b\uDC00"; StandardError = "🎼" }
        match Ui.decodeEvent (Codec.encodeEvent run) with
        | Ok(RunOutput transcript) ->
          transcript.StandardOutput |> Expect.equal "lone halves replaced" "a�b�"
          transcript.StandardError |> Expect.equal "a pair is kept" "🎼"
        | other -> failtestf "unexpected %A" other
      }

      test "an undecodable frame is refused, echoing its correlation when it has one" {
        let cases = [
          "not json", 0, "frame"
          """[]""", 0, "frame"
          """{"command":"request_snapshot"}""", 0, "correlation"
          """{"correlation":0,"command":"request_snapshot"}""", 0, "correlation"
          """{"correlation":-4,"command":"request_snapshot"}""", 0, "correlation"
          """{"correlation":1.5,"command":"request_snapshot"}""", 0, "correlation"
          """{"correlation":2147483648,"command":"request_snapshot"}""", 0, "correlation"
          """{"correlation":"1","command":"request_snapshot"}""", 0, "correlation"
          """{"correlation":12,"command":"explode"}""", 12, "command: unknown command explode"
          """{"correlation":1,"command":"open_project","project":5}""", 1, "project"
          """{"correlation":1,"command":"run","target":{"host":"h","epoch":"e","session":"s"},"arguments":["a",1]}""", 1, "arguments[1]"
          """{"correlation":1,"command":"reserve","target":{"host":"h","epoch":"e"},"label":"x"}""", 1, "target.session"
          """{"correlation":1,"command":"retire_worker","worker":{"host":"h"}}""", 1, "worker.epoch"
          """{"correlation":3,"command":"build","target":"h/e/s","reservation":"r"}""", 3, "target"
        ]
        for wire, correlation, names in cases do
          match Codec.decodeCommand wire with
          | Ok decoded -> failtestf "%s decoded to %A" wire decoded
          | Error refusal ->
            refusal.Correlation |> Expect.equal (wire + " echoes what can be read") correlation
            refusal.Target |> Expect.equal "nothing in the frame is trusted as an address" Codec.noTarget
            refusal.Code |> Expect.equal "Composer's spelling" "invalid_request"
            refusal.Message |> Expect.stringContains "the message names the field" names
      }

      test "integers and duplicate keys read as JSON.parse reads them" {
        Codec.decodeCommand """{"correlation":7.0,"command":"request_snapshot"}"""
        |> Expect.equal "7.0 is the integer 7" (Ok { CommandFrame.Correlation = 7; Command = RequestSnapshot })
        Codec.decodeCommand """{"correlation":1,"command":"explode","command":"request_snapshot"}"""
        |> Expect.equal "the last duplicate wins" (Ok { CommandFrame.Correlation = 1; Command = RequestSnapshot })
      }
    ]

    testList "projections" [
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
            CompiledObjects = 2; ReusedObjects = 1; RetiredObjects = 0; ChangedWitnesses = 2 }) ]
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
            Current = Some { ArtifactSummary.Generation = 3L; SourceVersion = "v3"; ArtifactPath = "/external/a.out"; ArtifactSha256 = "sha"; CompiledObjects = 2; ReusedObjects = 1; RetiredObjects = 0; ChangedWitnesses = 2 }
            BackendError = Some "backend"; FormatterError = Some "formatter"; CleanupError = Some "cleanup"
            WorkerRetirementRequired = Some "retire"; StatusError = Some "reading"; WorkerError = Some "gone" } |]
        let unobserved = Project.sessionStatus (response (Result.Ok(Reserved { Reservation = "rsv" })))
        unobserved.StatusFresh |> Expect.isFalse "an entry without an observation is never fresh"
        unobserved.StatusError |> Expect.isSome "and says why"
      }

      test "health reads the daemon's own observations, spelled as Protocol.fs spells them" {
        let sources = { HealthSources.McpPort = 47759; Version = "fallback"; ProcessId = 11; Health = None; Telemetry = None; Machine = Readings.machine }
        let cold = Project.health sources
        (cold.Version, cold.ProcessId, cold.McpPort, cold.Overall, cold.MemoryPressure)
        |> Expect.equal "before the first health tick" ("fallback", 11, 47759, "Unknown", "Normal")
        (cold.MachineAvailableBytes, cold.MachineTotalBytes) |> Expect.equal "machine memory" (32L, 64L)
        let warm = Project.health { sources with Health = Some Readings.health; Telemetry = Some Readings.telemetry }
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
        let board = Project.leaseBoard snapshot.Active snapshot.Queue
        let wire = Codec.encodeEvent (Leases board)
        for leaseId in leaseIds do
          wire.Contains leaseId |> Expect.isFalse "the release capability never leaves the daemon"
        board.Active |> Array.map _.Holder |> Expect.equal "grants" [| "agent-a"; "agent-b" |]
        board.Active |> Array.map _.GrantedAtMs |> Expect.equal "granted at" [| now.ToUnixTimeMilliseconds(); now.AddSeconds(1.).ToUnixTimeMilliseconds() |]
        board.Active |> Array.map _.Id |> Array.distinct |> Array.length |> Expect.equal "display ids are distinct" 2
        board.Active |> Array.forall (fun grant -> grant.Id.StartsWith "lease-") |> Expect.isTrue "display ids are labelled"
        Project.leaseBoard snapshot.Active snapshot.Queue |> Expect.equal "display ids are stable" board
        board.Queued |> Expect.equal "the queued request" [| { LeaseWait.Kind = "rebuild"; Holder = "agent-c"; RequestedAtMs = now.AddSeconds(2.).ToUnixTimeMilliseconds() } |]
      }
    ]

    testList "page" [
      testTask "Composer and dashboard bookmarks serve the same live Braidpoint page and policy" {
        do! task {
          use stopping = new CancellationTokenSource()
          let hub = hubOf (FakeComposer()) stopping.Token unpaused (ClockGate())
          let! app, url = Hosting.start (fun app -> mapRoutes app hub)
          use client = new Net.Http.HttpClient(Timeout = TimeSpan.FromSeconds 10.)
          try
            let mutable pages = []
            for route in [ "/composer"; "/dashboard" ] do
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
          finally
            stopping.Cancel()
            Hosting.stop app
        }
      }

      testTask "control-port Composer and old dashboard bookmarks redirect to the shared live view" {
        do! task {
          let deps: Bozzetto.Server.ControlListener.ControlDeps =
            { Version = "0.6.834-test"; McpPort = 47749; GetSessionCount = (fun () -> Task.FromResult 0); Shutdown = ignore }
          let! app, url = Hosting.start (Bozzetto.Server.ControlListener.mapRoutes deps)
          use handler = new Net.Http.HttpClientHandler(AllowAutoRedirect = false)
          use client = new Net.Http.HttpClient(handler, Timeout = TimeSpan.FromSeconds 10.)
          try
            for route in [ "/composer"; "/dashboard" ] do
              use! response = client.GetAsync(url + route)
              int response.StatusCode |> Expect.equal (route + " redirects") 302
              string response.Headers.Location |> Expect.equal (route + " keeps its entry point on the MCP listener") ("http://127.0.0.1:47749" + route)
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
          { Version = "0.6.834-test"; McpPort = 0; GetSessionCount = (fun () -> Task.FromResult 0); Shutdown = ignore }
        let! (mcp: Microsoft.AspNetCore.Builder.WebApplication), (mcpUrl: string) = Hosting.start (fun app -> mapRoutes app hub)
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
            BridgeWelcome.ProtocolVersion = 3
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
        socket.Deliver """{"correlation":8,"command":"explode"}"""
        socket.Deliver "not json"
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
        refusedCode 8 |> Expect.equal "an unknown command is refused with its correlation" (Some "invalid_request")
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

      testTask "without an owner change, health and leases are read only for the connect and the pull" {
        let stopping = new CancellationTokenSource()
        let composer = FakeComposer()
        let gate = PauseGate()
        let hub = hubOf composer stopping.Token gate.Pause (ClockGate())
        hub.Start()
        let socket = FakeSocket()
        let running = hub.RunAsync(socket, CancellationToken.None)
        let! _ = readPrologue socket
        (composer.HealthReads, composer.LeaseReads) |> Expect.equal "the prologue read each once" (1, 1)
        let names (events: Event list) = events |> List.map (fun e -> caseName (box e) typeof<Event>)
        // A supervisor change and a command: a snapshot and an outcome, and
        // neither health nor leases is read or pushed.
        composer.Change()
        let! (pause: TaskCompletionSource<unit>) = gate.Entered()
        socket.Deliver(frame 1 (Reserve { ReserveEdit.Target = target; Label = "edit" }))
        let! (changed: Event list) = readUntil socket (fun seen -> seen |> List.exists (fun e -> correlationOf e = Some 1))
        names changed |> Expect.equal "the change's snapshot, then the command's outcome" [ "Snapshot"; "Accepted" ]
        (composer.HealthReads, composer.LeaseReads) |> Expect.equal "neither read since the prologue" (1, 1)
        // The explicit pull answers with fresh state, reading each once more.
        socket.Deliver(frame 2 RequestSnapshot)
        let! (pulled: Event list) = readUntil socket (fun seen -> seen |> List.exists (fun e -> correlationOf e = Some 2))
        names pulled |> Expect.equal "fresh state, then the acknowledgement" [ "Snapshot"; "Health"; "Leases"; "Accepted" ]
        (composer.HealthReads, composer.LeaseReads) |> Expect.equal "the pull read each once" (2, 2)
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
