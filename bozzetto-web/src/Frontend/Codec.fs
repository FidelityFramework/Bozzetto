/// Frontend-side codec for the shared protocol: encodes CommandFrames and
/// decodes Events in the interim JSON wire format documented in
/// Shared/Protocol.fs, written in Fable idioms over the browser's own JSON.
///
/// Why not Fidelity.Data.JSON: under Fable 5.0.0-alpha.14 its JsonParser
/// fails to emit (lone-surrogate char literals '\uD800'..'\uDFFF' cannot be
/// written to the JavaScript output), and its Toml/Xml parsers fail FS0748.
/// Every int64 travels as a decimal string, so JSON.parse loses nothing.
/// When BAREWire compiles under Fable and Composer, this module becomes the
/// binary codec; Protocol.fs and the UI do not change.
module Bozzetto.Web.Frontend.Codec

open Fable.Core
open Fable.Core.JsInterop
open Bozzetto.Web.Shared.Protocol

// ── JSON shape tests (the browser's JSON values, untyped) ──────────────────

[<Emit("typeof $0 === 'string'")>]
let private isString (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'boolean'")>]
let private isBool (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'number' && Number.isFinite($0)")>]
let private isNumber (value: obj) : bool = jsNative

[<Emit("Number.isInteger($0) && $0 >= -2147483648 && $0 <= 2147483647")>]
let private isInt32 (value: obj) : bool = jsNative

[<Emit("Array.isArray($0)")>]
let private isArray (value: obj) : bool = jsNative

[<Emit("$0 !== null && typeof $0 === 'object' && !Array.isArray($0)")>]
let private isObject (value: obj) : bool = jsNative

[<Emit("$0 === null || $0 === undefined")>]
let private isAbsent (value: obj) : bool = jsNative

/// Own properties only: a frame cannot smuggle prototype members.
[<Emit("Object.prototype.hasOwnProperty.call($0, $1) ? $0[$1] : undefined")>]
let private field (value: obj) (name: string) : obj = jsNative

[<Emit("(function(t){try{return {ok:true,value:JSON.parse(t)}}catch(e){return {ok:false,value:String(e)}}})($0)")>]
let private parseJson (text: string) : obj = jsNative

// ── Decoding combinators ───────────────────────────────────────────────────

type private DecodeBuilder() =
  member _.Bind(result: Result<'a, string>, next: 'a -> Result<'b, string>) = Result.bind next result
  member _.Return(value: 'a) : Result<'a, string> = Ok value
  member _.ReturnFrom(result: Result<'a, string>) = result

let private decode = DecodeBuilder()

let private text (o: obj) (name: string) : Result<string, string> =
  let v = field o name
  if isString v then Ok(unbox<string> v) else Error(name + ": expected a string")

let private optionalText (o: obj) (name: string) : Result<string option, string> =
  let v = field o name
  if isAbsent v then Ok None
  elif isString v then Ok(Some(unbox<string> v))
  else Error(name + ": expected a string or null")

let private flag (o: obj) (name: string) : Result<bool, string> =
  let v = field o name
  if isBool v then Ok(unbox<bool> v) else Error(name + ": expected a boolean")

let private real (o: obj) (name: string) : Result<float, string> =
  let v = field o name
  if isNumber v then Ok(unbox<float> v) else Error(name + ": expected a number")

let private optionalReal (o: obj) (name: string) =
  if isAbsent (field o name) then Ok None else real o name |> Result.map Some

let private int32Value (name: string) (v: obj) : Result<int32, string> =
  if isNumber v && isInt32 v then Ok(unbox<int32> v) else Error(name + ": expected an int32")

let private whole (o: obj) (name: string) : Result<int32, string> = int32Value name (field o name)

/// int64 travels as a decimal string: optional '-', digits only.
let private int64Text (name: string) (raw: string) : Result<int64, string> =
  let digitsFrom = if raw.StartsWith "-" then 1 else 0
  let canonical =
    raw.Length > digitsFrom && raw.Length <= 20
    && (raw.Substring digitsFrom |> Seq.forall (fun c -> c >= '0' && c <= '9'))
  match System.Int64.TryParse raw with
  | true, value when canonical -> Ok value
  | _ -> Error(name + ": expected an int64 decimal string")

let private wide (o: obj) (name: string) : Result<int64, string> =
  let v = field o name
  if isString v then int64Text name (unbox<string> v) else Error(name + ": expected an int64 decimal string")

let private child (o: obj) (name: string) : Result<obj, string> =
  let v = field o name
  if isObject v then Ok v else Error(name + ": expected an object")

let private items (o: obj) (name: string) (item: obj -> Result<'a, string>) : Result<'a array, string> =
  let v = field o name
  if not (isArray v) then Error(name + ": expected an array")
  else
    let source = unbox<obj array> v
    let decoded = ResizeArray<'a>()
    let mutable failure = None
    let mutable index = 0
    while failure.IsNone && index < source.Length do
      match item source[index] with
      | Ok value -> decoded.Add value
      | Error message -> failure <- Some(name + "[" + string index + "]." + message)
      index <- index + 1
    match failure with
    | Some message -> Error message
    | None -> Ok(decoded.ToArray())

let private optionalChild (o: obj) (name: string) (inner: obj -> Result<'a, string>) : Result<'a option, string> =
  let v = field o name
  if isAbsent v then Ok None
  elif isObject v then inner v |> Result.map Some |> Result.mapError (fun m -> name + "." + m)
  else Error(name + ": expected an object or null")

let private nested (o: obj) (name: string) (inner: obj -> Result<'a, string>) : Result<'a, string> =
  child o name |> Result.bind (fun v -> inner v |> Result.mapError (fun m -> name + "." + m))

// ── Event payloads ─────────────────────────────────────────────────────────

let private sessionTarget (o: obj) = decode {
  let! host = text o "host"
  let! epoch = text o "epoch"
  let! session = text o "session"
  return { SessionTarget.Worker = { WorkerTarget.Host = host; Epoch = epoch }; Session = session }
}

let private artifact (o: obj) = decode {
  let! generation = wide o "generation"
  let! sourceVersion = text o "sourceVersion"
  let! artifactPath = text o "artifactPath"
  let! artifactSha256 = text o "artifactSha256"
  let! compiled = whole o "compiledObjects"
  let! reused = whole o "reusedObjects"
  let! retired = whole o "retiredObjects"
  let! changed = whole o "changedWitnesses"
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

let private sessionStatus (o: obj) = decode {
  let! target = sessionTarget o
  let! generation = wide o "generation"
  let! project = text o "project"
  let! closed = flag o "closed"
  let! busy = flag o "busy"
  let! statusFresh = flag o "statusFresh"
  let! revocationPending = flag o "revocationPending"
  let! cleanupPending = flag o "cleanupPending"
  let! formatterCleanupPending = flag o "formatterCleanupPending"
  let! current = optionalChild o "current" artifact
  let! backendError = optionalText o "backendError"
  let! formatterError = optionalText o "formatterError"
  let! cleanupError = optionalText o "cleanupError"
  let! workerRetirementRequired = optionalText o "workerRetirementRequired"
  let! statusError = optionalText o "statusError"
  let! workerError = optionalText o "workerError"
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

let private workerState (o: obj) : Result<WorkerState, string> =
  text o "state"
  |> Result.bind (fun state ->
    match state with
    | "unconfigured" -> Ok Unconfigured
    | "idle" -> Ok Idle
    | "running" ->
      decode {
        let! host = text o "host"
        let! epoch = text o "epoch"
        let! compilerVersion = text o "compilerVersion"
        let processId = field o "processId"
        let! pid = if isAbsent processId then Ok None else int32Value "processId" processId |> Result.map Some
        return Running {
          WorkerInfo.Target = { WorkerTarget.Host = host; Epoch = epoch }
          CompilerVersion = compilerVersion
          ProcessId = pid
        }
      }
    | other -> Error("state: unknown worker state " + other))

let private completion (o: obj) : Result<Completion, string> =
  text o "kind"
  |> Result.bind (fun kind ->
    match kind with
    | "session_opened" -> Ok SessionOpened
    | "edit_reserved" -> text o "reservation" |> Result.map EditReserved
    | "reservation_built" -> nested o "artifact" artifact |> Result.map ReservationBuilt
    | "run_finished" -> whole o "exitCode" |> Result.map RunFinished
    | "work_canceled" -> Ok WorkCanceled
    | "session_closed" -> Ok SessionClosed
    | "worker_retired" -> Ok WorkerRetired
    | "snapshot_sent" -> Ok SnapshotSent
    | other -> Error("kind: unknown completion " + other))

let private processSample (o: obj) = decode {
  let! processId = whole o "processId"
  let! role = text o "role"
  let! residentBytes = wide o "residentBytes"
  let! cpuPercent = real o "cpuPercent"
  return { ProcessSample.ProcessId = processId; Role = role; ResidentBytes = residentBytes; CpuPercent = cpuPercent }
}

let private alarm (o: obj) = decode {
  let! signal = text o "signal"
  let! state = text o "state"
  let! message = text o "message"
  return { HealthAlarm.Signal = signal; State = state; Message = message }
}

let private health (o: obj) = decode {
  let! version = text o "version"
  let! processId = whole o "processId"
  let! mcpPort = whole o "mcpPort"
  let! overall = text o "overall"
  let! memoryPressure = text o "memoryPressure"
  let! residentBytes = wide o "residentBytes"
  let! machineAvailableBytes = wide o "machineAvailableBytes"
  let! machineTotalBytes = wide o "machineTotalBytes"
  let! cpuPercent = real o "cpuPercent"
  let! processes = items o "processes" processSample
  let! alarms = items o "alarms" alarm
  return {
    DaemonHealth.Version = version
    ProcessId = processId
    McpPort = mcpPort
    Overall = overall
    MemoryPressure = memoryPressure
    ResidentBytes = residentBytes
    MachineAvailableBytes = machineAvailableBytes
    MachineTotalBytes = machineTotalBytes
    CpuPercent = cpuPercent
    Processes = processes
    Alarms = alarms
  }
}

let private leaseGrant (o: obj) = decode {
  let! id = text o "id"
  let! kind = text o "kind"
  let! holder = text o "holder"
  let! grantedAtMs = wide o "grantedAtMs"
  let! expiresAtMs = wide o "expiresAtMs"
  return { LeaseGrant.Id = id; Kind = kind; Holder = holder; GrantedAtMs = grantedAtMs; ExpiresAtMs = expiresAtMs }
}

let private leaseWait (o: obj) = decode {
  let! kind = text o "kind"
  let! holder = text o "holder"
  let! requestedAtMs = wide o "requestedAtMs"
  return { LeaseWait.Kind = kind; Holder = holder; RequestedAtMs = requestedAtMs }
}

let private resourceMetric o = decode {
  let! name = text o "name"
  let! value = optionalReal o "value"
  let! unit = text o "unit"
  return { ResourceMetric.Name = name; Value = value; Unit = unit }
}

let private resourceProcess o = decode {
  let! pid = whole o "processId"
  let! parent = whole o "parentId"
  let! start = text o "startTicks"
  let! name = text o "name"
  let! context = text o "context"
  let! cpu = optionalReal o "cpuPercent"
  let! rss = real o "residentBytes"
  let! threads = whole o "threads"
  return { ResourceProcess.ProcessId = pid; ParentId = parent; StartTicks = start; Name = name; Context = context; CpuPercent = cpu; ResidentBytes = rss; Threads = threads }
}

let private resources o = decode {
  let! sequence = wide o "sequence"
  let! status = text o "status"
  let! at = wide o "sampledAtMs"
  let! interval = real o "intervalMs"
  let! cost = real o "sampleCostMs"
  let! metrics = items o "metrics" resourceMetric
  let! processes = items o "processes" resourceProcess
  let! count = whole o "processCount"
  let! unreadable = whole o "unreadableCount"
  let! omitted = whole o "omittedCount"
  let! note = text o "note"
  return { HostResources.Sequence = sequence; Status = status; SampledAtMs = at; IntervalMs = interval; SampleCostMs = cost
           Metrics = metrics; Processes = processes; ProcessCount = count; UnreadableCount = unreadable; OmittedCount = omitted; Note = note }
}

let private agentUsage o = decode {
  let! input = wide o "input"
  let! output = wide o "output"
  let! cacheRead = wide o "cacheRead"
  let! cacheWrite = wide o "cacheWrite"
  let! total = wide o "total"
  let! estimate = optionalReal o "estimateUsd"
  return { AgentUsage.Input = input; Output = output; CacheRead = cacheRead; CacheWrite = cacheWrite; Total = total; EstimateUsd = estimate }
}
let private agentRun o = decode {
  let! id = text o "id"
  let! memberId = text o "member"
  let! role = text o "role"
  let! name = text o "name"
  let! session = text o "session"
  let! sequence = wide o "reportSequence"
  let! model = text o "model"
  let! endpoint = text o "endpoint"
  let! project = text o "project"
  let! focus = text o "focus"
  let! operation = text o "operation"
  let! status = text o "status"
  let! at = wide o "updatedAtMs"
  let! omitted = wide o "omittedActivity"
  let! activity = items o "activity" (fun v -> if isString v then Ok(unbox<string> v) else Error "expected string")
  let! usage = optionalChild o "usage" agentUsage
  return { AgentRun.Id = id; Member = memberId; Role = role; Name = name; Session = session; ReportSequence = sequence
           Model = model; Endpoint = endpoint; Project = project; Focus = focus; Operation = operation; Status = status
           UpdatedAtMs = at; OmittedActivity = omitted; Activity = activity; Usage = usage }
}
let private agentWork o = decode {
  let! incarnation = text o "incarnation"
  let! sequence = wide o "sequence"
  let! host = text o "executionHost"
  let! capacity = whole o "capacity"
  let! runs = items o "runs" agentRun
  return { AgentWorkBoard.Incarnation = incarnation; Sequence = sequence; ExecutionHost = host; Capacity = capacity; Runs = runs }
}

let private eventOf (o: obj) : Result<Event, string> =
  text o "event"
  |> Result.bind (fun tag ->
    match tag with
    | "welcome" ->
      decode {
        let! version = whole o "protocolVersion"
        let! daemonVersion = text o "daemonVersion"
        let! startedAtMs = wide o "startedAtMs"
        return Welcome { BridgeWelcome.ProtocolVersion = version; DaemonVersion = daemonVersion; StartedAtMs = startedAtMs }
      }
    | "snapshot" ->
      decode {
        let! revision = wide o "revision"
        let! worker = nested o "worker" workerState
        let! sessions = items o "sessions" sessionStatus
        return Snapshot { ComposerSnapshot.Revision = revision; Worker = worker; Sessions = sessions }
      }
    | "accepted" ->
      decode {
        let! correlation = whole o "correlation"
        let! target = nested o "target" sessionTarget
        let! done' = nested o "completion" completion
        return Accepted { CommandAcceptance.Correlation = correlation; Target = target; Completion = done' }
      }
    | "refused" ->
      decode {
        let! correlation = whole o "correlation"
        let! target = nested o "target" sessionTarget
        let! code = text o "code"
        let! message = text o "message"
        return Refused { CommandRefusal.Correlation = correlation; Target = target; Code = code; Message = message }
      }
    | "run_output" ->
      decode {
        let! correlation = whole o "correlation"
        let! target = nested o "target" sessionTarget
        let! generation = wide o "generation"
        let! sourceVersion = text o "sourceVersion"
        let! exitCode = whole o "exitCode"
        let! standardOutput = text o "standardOutput"
        let! standardError = text o "standardError"
        return RunOutput {
          RunTranscript.Correlation = correlation
          Target = target
          Generation = generation
          SourceVersion = sourceVersion
          ExitCode = exitCode
          StandardOutput = standardOutput
          StandardError = standardError
        }
      }
    | "agent_work" -> agentWork o |> Result.map AgentWork
    | "resources" -> resources o |> Result.map Resources
    | "health" -> health o |> Result.map Health
    | "leases" ->
      decode {
        let! active = items o "active" leaseGrant
        let! queued = items o "queued" leaseWait
        return Leases { LeaseBoard.Active = active; Queued = queued }
      }
    | "uptime" -> wide o "uptimeMs" |> Result.map (fun uptimeMs -> Uptime { UptimeTick.UptimeMs = uptimeMs })
    | other -> Error("event: unknown tag " + other))

/// Decode one backend frame. Errors name the offending field.
let decodeEvent (payload: string) : Result<Event, string> =
  let parsed = parseJson payload
  if not (unbox<bool> parsed?ok) then Error("not JSON: " + unbox<string> parsed?value)
  else
    let root: obj = parsed?value
    if isObject root then eventOf root else Error "frame: expected a JSON object"

// ── Command encoding ───────────────────────────────────────────────────────

let private sessionTargetJson (target: SessionTarget) =
  createObj [ "host" ==> target.Worker.Host; "epoch" ==> target.Worker.Epoch; "session" ==> target.Session ]

/// Encode one UI frame.
let encodeCommand (frame: CommandFrame) : string =
  let fields =
    match frame.Command with
    | RequestSnapshot -> [ "command" ==> "request_snapshot" ]
    | ObserveResources seconds -> [ "command" ==> "observe_resources"; "seconds" ==> seconds ]
    | OpenProject project -> [ "command" ==> "open_project"; "project" ==> project ]
    | Reserve edit ->
      [ "command" ==> "reserve"; "target" ==> sessionTargetJson edit.Target; "label" ==> edit.Label ]
    | Build build ->
      [ "command" ==> "build"; "target" ==> sessionTargetJson build.Target; "reservation" ==> build.Reservation ]
    | Run run ->
      [ "command" ==> "run"; "target" ==> sessionTargetJson run.Target; "arguments" ==> run.Arguments ]
    | Cancel target -> [ "command" ==> "cancel"; "target" ==> sessionTargetJson target ]
    | CloseSession target -> [ "command" ==> "close_session"; "target" ==> sessionTargetJson target ]
    | RetireWorker worker ->
      [ "command" ==> "retire_worker"; "worker" ==> createObj [ "host" ==> worker.Host; "epoch" ==> worker.Epoch ] ]
  JS.JSON.stringify(createObj (("correlation" ==> frame.Correlation) :: fields))
