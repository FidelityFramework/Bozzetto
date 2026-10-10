/// Backend-side codec for the shared protocol, in Fable idioms, for the mock
/// bridge and the codec round-trip tests. It decodes CommandFrames and
/// encodes Events in the interim JSON wire format documented in
/// Shared/Protocol.fs. Bozzetto's real backend codec is the .NET counterpart
/// of this module (over Fidelity.Data.JSON); both must agree with
/// Frontend/Codec.fs byte for byte on field names and value spellings.
module Bozzetto.Web.Mock.Codec

open Fable.Core
open Fable.Core.JsInterop
open Bozzetto.Web.Shared.Protocol

[<Emit("typeof $0 === 'string'")>]
let private isString (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'number' && Number.isInteger($0) && $0 >= -2147483648 && $0 <= 2147483647")>]
let private isInt32 (value: obj) : bool = jsNative

[<Emit("Array.isArray($0)")>]
let private isArray (value: obj) : bool = jsNative

[<Emit("$0 !== null && typeof $0 === 'object' && !Array.isArray($0)")>]
let private isObject (value: obj) : bool = jsNative

[<Emit("Object.prototype.hasOwnProperty.call($0, $1) ? $0[$1] : undefined")>]
let private field (value: obj) (name: string) : obj = jsNative

[<Emit("(function(t){try{return {ok:true,value:JSON.parse(t)}}catch(e){return {ok:false,value:String(e)}}})($0)")>]
let private parseJson (text: string) : obj = jsNative

let private text (o: obj) (name: string) : Result<string, string> =
  let v = field o name
  if isString v then Ok(unbox<string> v) else Error(name + " must be a string.")

let private target (o: obj) : Result<SessionTarget, string> =
  let t = field o "target"
  if not (isObject t) then Error "target must be an object."
  else
    match text t "host", text t "epoch", text t "session" with
    | Ok host, Ok epoch, Ok session ->
      Ok { SessionTarget.Worker = { WorkerTarget.Host = host; Epoch = epoch }; Session = session }
    | Error e, _, _ | _, Error e, _ | _, _, Error e -> Error("target." + e)

let private strings (o: obj) (name: string) : Result<string array, string> =
  let v = field o name
  if not (isArray v) then Error(name + " must be an array.")
  else
    let values = unbox<obj array> v
    if values |> Array.forall isString then Ok(values |> Array.map unbox<string>)
    else Error(name + " must contain strings.")

/// The correlation of a frame, when it carries a valid one (refusals echo it).
let correlationOf (payload: string) : int32 =
  let parsed = parseJson payload
  if not (unbox<bool> parsed?ok) then 0
  else
    let root: obj = parsed?value
    if isObject root then
      let c = field root "correlation"
      if isInt32 c && unbox<int32> c > 0 then unbox<int32> c else 0
    else 0

/// Decode one UI frame. The error text becomes a refusal message.
let decodeCommand (payload: string) : Result<CommandFrame, string> =
  let parsed = parseJson payload
  if not (unbox<bool> parsed?ok) then Error "Frame is not JSON."
  else
    let o: obj = parsed?value
    if not (isObject o) then Error "Frame must be a JSON object."
    else
      let c = field o "correlation"
      if not (isInt32 c) || unbox<int32> c <= 0 then Error "correlation must be a positive int32."
      else
        let command =
          text o "command"
          |> Result.bind (fun tag ->
            match tag with
            | "request_snapshot" -> Ok RequestSnapshot
            | "observe_resources" ->
              let n = field o "seconds"
              if isInt32 n && unbox<int32> n >= 0 && unbox<int32> n <= 120 then Ok(ObserveResources(unbox<int32> n))
              else Error "seconds: expected an integer from 0 to 120"
            | "open_project" -> text o "project" |> Result.map OpenProject
            | "reserve" ->
              match target o, text o "label" with
              | Ok t, Ok label -> Ok(Reserve { ReserveEdit.Target = t; Label = label })
              | Error e, _ | _, Error e -> Error e
            | "build" ->
              match target o, text o "reservation" with
              | Ok t, Ok reservation -> Ok(Build { BuildReserved.Target = t; Reservation = reservation })
              | Error e, _ | _, Error e -> Error e
            | "run" ->
              match target o, strings o "arguments" with
              | Ok t, Ok arguments -> Ok(Run { RunCurrent.Target = t; Arguments = arguments })
              | Error e, _ | _, Error e -> Error e
            | "cancel" -> target o |> Result.map Cancel
            | "close_session" -> target o |> Result.map CloseSession
            | "retire_worker" ->
              let w = field o "worker"
              if not (isObject w) then Error "worker must be an object."
              else
                match text w "host", text w "epoch" with
                | Ok host, Ok epoch -> Ok(RetireWorker { WorkerTarget.Host = host; Epoch = epoch })
                | Error e, _ | _, Error e -> Error("worker." + e)
            | other -> Error("Unknown command " + other + "."))
        command |> Result.map (fun cmd -> { CommandFrame.Correlation = unbox<int32> c; Command = cmd })

// ── Event encoding ─────────────────────────────────────────────────────────

let private wide (value: int64) : obj = box (string value)

let private nullable (value: string option) : obj =
  match value with
  | Some v -> box v
  | None -> null

let private sessionTargetJson (t: SessionTarget) =
  createObj [ "host" ==> t.Worker.Host; "epoch" ==> t.Worker.Epoch; "session" ==> t.Session ]

let private artifactJson (a: ArtifactSummary) =
  createObj [
    "generation" ==> wide a.Generation
    "sourceVersion" ==> a.SourceVersion
    "artifactPath" ==> a.ArtifactPath
    "artifactSha256" ==> a.ArtifactSha256
    "compiledObjects" ==> a.CompiledObjects
    "reusedObjects" ==> a.ReusedObjects
    "retiredObjects" ==> a.RetiredObjects
    "changedWitnesses" ==> a.ChangedWitnesses
  ]

let private sessionJson (s: SessionStatus) =
  createObj [
    "host" ==> s.Target.Worker.Host
    "epoch" ==> s.Target.Worker.Epoch
    "session" ==> s.Target.Session
    "generation" ==> wide s.Generation
    "project" ==> s.Project
    "closed" ==> s.Closed
    "busy" ==> s.Busy
    "statusFresh" ==> s.StatusFresh
    "revocationPending" ==> s.RevocationPending
    "cleanupPending" ==> s.CleanupPending
    "formatterCleanupPending" ==> s.FormatterCleanupPending
    "current" ==> (match s.Current with Some a -> artifactJson a | None -> null)
    "backendError" ==> nullable s.BackendError
    "formatterError" ==> nullable s.FormatterError
    "cleanupError" ==> nullable s.CleanupError
    "workerRetirementRequired" ==> nullable s.WorkerRetirementRequired
    "statusError" ==> nullable s.StatusError
    "workerError" ==> nullable s.WorkerError
  ]

let private workerJson (w: WorkerState) =
  match w with
  | Unconfigured -> createObj [ "state" ==> "unconfigured" ]
  | Idle -> createObj [ "state" ==> "idle" ]
  | Running info ->
    createObj [
      "state" ==> "running"
      "host" ==> info.Target.Host
      "epoch" ==> info.Target.Epoch
      "compilerVersion" ==> info.CompilerVersion
      "processId" ==> (match info.ProcessId with Some pid -> box pid | None -> null)
    ]

let private completionJson (c: Completion) =
  match c with
  | SessionOpened -> createObj [ "kind" ==> "session_opened" ]
  | EditReserved reservation -> createObj [ "kind" ==> "edit_reserved"; "reservation" ==> reservation ]
  | ReservationBuilt a -> createObj [ "kind" ==> "reservation_built"; "artifact" ==> artifactJson a ]
  | RunFinished exitCode -> createObj [ "kind" ==> "run_finished"; "exitCode" ==> exitCode ]
  | WorkCanceled -> createObj [ "kind" ==> "work_canceled" ]
  | SessionClosed -> createObj [ "kind" ==> "session_closed" ]
  | WorkerRetired -> createObj [ "kind" ==> "worker_retired" ]
  | SnapshotSent -> createObj [ "kind" ==> "snapshot_sent" ]

/// Encode one backend frame.
let encodeEvent (event: Event) : string =
  let frame =
    match event with
    | Welcome w ->
      createObj [
        "event" ==> "welcome"
        "protocolVersion" ==> w.ProtocolVersion
        "daemonVersion" ==> w.DaemonVersion
        "startedAtMs" ==> wide w.StartedAtMs
      ]
    | Snapshot s ->
      createObj [
        "event" ==> "snapshot"
        "revision" ==> wide s.Revision
        "worker" ==> workerJson s.Worker
        "sessions" ==> (s.Sessions |> Array.map sessionJson)
      ]
    | Accepted a ->
      createObj [
        "event" ==> "accepted"
        "correlation" ==> a.Correlation
        "target" ==> sessionTargetJson a.Target
        "completion" ==> completionJson a.Completion
      ]
    | Refused r ->
      createObj [
        "event" ==> "refused"
        "correlation" ==> r.Correlation
        "target" ==> sessionTargetJson r.Target
        "code" ==> r.Code
        "message" ==> r.Message
      ]
    | RunOutput r ->
      createObj [
        "event" ==> "run_output"
        "correlation" ==> r.Correlation
        "target" ==> sessionTargetJson r.Target
        "generation" ==> wide r.Generation
        "sourceVersion" ==> r.SourceVersion
        "exitCode" ==> r.ExitCode
        "standardOutput" ==> r.StandardOutput
        "standardError" ==> r.StandardError
      ]
    | Health h ->
      createObj [
        "event" ==> "health"
        "version" ==> h.Version
        "processId" ==> h.ProcessId
        "mcpPort" ==> h.McpPort
        "overall" ==> h.Overall
        "memoryPressure" ==> h.MemoryPressure
        "residentBytes" ==> wide h.ResidentBytes
        "machineAvailableBytes" ==> wide h.MachineAvailableBytes
        "machineTotalBytes" ==> wide h.MachineTotalBytes
        "cpuPercent" ==> h.CpuPercent
        "processes" ==> (h.Processes |> Array.map (fun p ->
          createObj [
            "processId" ==> p.ProcessId
            "role" ==> p.Role
            "residentBytes" ==> wide p.ResidentBytes
            "cpuPercent" ==> p.CpuPercent
          ]))
        "alarms" ==> (h.Alarms |> Array.map (fun a ->
          createObj [ "signal" ==> a.Signal; "state" ==> a.State; "message" ==> a.Message ]))
      ]
    | Leases board ->
      createObj [
        "event" ==> "leases"
        "active" ==> (board.Active |> Array.map (fun l ->
          createObj [
            "id" ==> l.Id
            "kind" ==> l.Kind
            "holder" ==> l.Holder
            "grantedAtMs" ==> wide l.GrantedAtMs
            "expiresAtMs" ==> wide l.ExpiresAtMs
          ]))
        "queued" ==> (board.Queued |> Array.map (fun q ->
          createObj [ "kind" ==> q.Kind; "holder" ==> q.Holder; "requestedAtMs" ==> wide q.RequestedAtMs ]))
      ]
    | Resources r ->
      let opt (v: float option) : obj = match v with Some n -> box n | None -> null
      createObj [
        "event" ==> "resources"; "sequence" ==> wide r.Sequence; "status" ==> r.Status
        "sampledAtMs" ==> wide r.SampledAtMs; "intervalMs" ==> r.IntervalMs; "sampleCostMs" ==> r.SampleCostMs
        "processCount" ==> r.ProcessCount; "unreadableCount" ==> r.UnreadableCount; "omittedCount" ==> r.OmittedCount; "note" ==> r.Note
        "metrics" ==> (r.Metrics |> Array.map (fun m -> createObj [ "name" ==> m.Name; "value" ==> opt m.Value; "unit" ==> m.Unit ]))
        "processes" ==> (r.Processes |> Array.map (fun p -> createObj [
          "processId" ==> p.ProcessId; "parentId" ==> p.ParentId; "startTicks" ==> p.StartTicks; "name" ==> p.Name
          "context" ==> p.Context; "cpuPercent" ==> opt p.CpuPercent; "residentBytes" ==> p.ResidentBytes; "threads" ==> p.Threads ]))
      ]
    | AgentWork board ->
      let usage = function
        | None -> null
        | Some (u: AgentUsage) ->
          createObj [
            "input" ==> wide u.Input; "output" ==> wide u.Output; "cacheRead" ==> wide u.CacheRead
            "cacheWrite" ==> wide u.CacheWrite; "total" ==> wide u.Total
            "estimateUsd" ==> (match u.EstimateUsd with Some cost -> box cost | None -> null)
          ]
      createObj [
        "event" ==> "agent_work"; "incarnation" ==> board.Incarnation; "sequence" ==> wide board.Sequence
        "executionHost" ==> board.ExecutionHost; "capacity" ==> board.Capacity
        "runs" ==> (board.Runs |> Array.map (fun r -> createObj [
          "id" ==> r.Id; "member" ==> r.Member; "role" ==> r.Role; "name" ==> r.Name; "session" ==> r.Session
          "reportSequence" ==> wide r.ReportSequence; "model" ==> r.Model; "endpoint" ==> r.Endpoint
          "project" ==> r.Project; "focus" ==> r.Focus; "operation" ==> r.Operation; "status" ==> r.Status
          "updatedAtMs" ==> wide r.UpdatedAtMs; "omittedActivity" ==> wide r.OmittedActivity; "activity" ==> r.Activity
          "usage" ==> usage r.Usage ])) ]
    | Uptime tick -> createObj [ "event" ==> "uptime"; "uptimeMs" ==> wide tick.UptimeMs ]
  JS.JSON.stringify frame
