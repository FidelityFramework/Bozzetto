/// Backend codec for the browser UI bridge: decodes the UI's CommandFrames and
/// encodes Events in the interim JSON wire format documented in the header of
/// bozzetto-web/src/Shared/Protocol.fs. It is the .NET twin of
/// bozzetto-web/src/Mock/Codec.fs and agrees with it, field for field and
/// spelling for spelling, so the page cannot tell them apart. Only this module
/// knows the wire: when BAREWire frames replace JSON, it is the one file that
/// changes, and Protocol.fs, routing and the UI stay as they are.
module Bozzetto.Server.UiBridgeCodec

open System
open System.Globalization
open Fidelity.Data.JSON
open Bozzetto.Web.Shared.Protocol

// ── Decoding (UI -> backend) ───────────────────────────────────────────────

type private DecodeBuilder() =
  member _.Bind(result: Result<'a, string>, next: 'a -> Result<'b, string>) = Result.bind next result
  member _.Return(value: 'a) : Result<'a, string> = Ok value

let private decode = DecodeBuilder()

/// The last occurrence of a key wins, as JSON.parse reads a duplicated key.
let private field (name: string) (value: JsonValue) : JsonValue option =
  match value with
  | JsonValue.Object pairs -> pairs |> List.fold (fun found (key, item) -> if key = name then Some item else found) None
  | _ -> None

let private text (name: string) (o: JsonValue) : Result<string, string> =
  match field name o with
  | Some(JsonValue.String value) -> Ok value
  | _ -> Error(name + ": expected a string")

let private strings (name: string) (o: JsonValue) : Result<string array, string> =
  match field name o with
  | Some(JsonValue.Array items) ->
    let rec collect index acc remaining =
      match remaining with
      | [] -> Ok(acc |> List.rev |> List.toArray)
      | JsonValue.String value :: rest -> collect (index + 1) (value :: acc) rest
      | _ :: _ -> Error(sprintf "%s[%d]: expected a string" name index)
    collect 0 [] items
  | _ -> Error(name + ": expected an array")

let private nested (name: string) (inner: JsonValue -> Result<'a, string>) (o: JsonValue) : Result<'a, string> =
  match field name o with
  | Some(JsonValue.Object _ as child) -> inner child |> Result.mapError (fun message -> name + "." + message)
  | _ -> Error(name + ": expected an object")

/// JSON.parse semantics: any number whose value is an integer in int32 range,
/// however it is spelled ("7", "7.0", "7e0").
let private int32Of (value: JsonValue) : int32 option =
  match JsonValue.asNumber value with
  | Some number when Double.IsFinite number && Math.Floor number = number && number >= -2147483648.0 && number <= 2147483647.0 ->
    Some(int32 number)
  | _ -> None

let private positiveCorrelation (root: JsonValue) : int32 option =
  field "correlation" root |> Option.bind int32Of |> Option.filter (fun correlation -> correlation > 0)

let private workerTarget (o: JsonValue) = decode {
  let! host = text "host" o
  let! epoch = text "epoch" o
  return { WorkerTarget.Host = host; Epoch = epoch }
}

let private sessionTarget (o: JsonValue) = decode {
  let! worker = workerTarget o
  let! session = text "session" o
  return { SessionTarget.Worker = worker; Session = session }
}

let private command (o: JsonValue) : Result<Command, string> =
  text "command" o
  |> Result.bind (fun tag ->
    match tag with
    | "request_snapshot" -> Ok RequestSnapshot
    | "open_project" -> text "project" o |> Result.map OpenProject
    | "reserve" ->
      decode {
        let! target = nested "target" sessionTarget o
        let! label = text "label" o
        return Reserve { ReserveEdit.Target = target; Label = label }
      }
    | "build" ->
      decode {
        let! target = nested "target" sessionTarget o
        let! reservation = text "reservation" o
        return Build { BuildReserved.Target = target; Reservation = reservation }
      }
    | "run" ->
      decode {
        let! target = nested "target" sessionTarget o
        let! arguments = strings "arguments" o
        return Run { RunCurrent.Target = target; Arguments = arguments }
      }
    | "cancel" -> nested "target" sessionTarget o |> Result.map Cancel
    | "close_session" -> nested "target" sessionTarget o |> Result.map CloseSession
    | "retire_worker" -> nested "worker" workerTarget o |> Result.map RetireWorker
    | other -> Error("command: unknown command " + other))

/// The address a refusal carries when a frame names none that can be trusted.
let noTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }

/// A refusal for a frame the backend cannot act on. Nothing in such a frame is
/// trusted as an address, so the target is empty.
let frameRefusal (correlation: int32) (code: string) (message: string) : CommandRefusal =
  { CommandRefusal.Correlation = correlation; Target = noTarget; Code = code; Message = message }

/// Decode one UI frame. A frame that cannot be decoded becomes its refusal,
/// echoing the correlation whenever one can be read (0 otherwise); the
/// message names the offending field.
let decodeCommand (payload: string) : Result<CommandFrame, CommandRefusal> =
  match Json.parse payload with
  | Error reason -> Error(frameRefusal 0 "invalid_request" ("frame: not JSON (" + reason + ")"))
  | Ok(JsonValue.Object _ as root) ->
    match positiveCorrelation root with
    | None -> Error(frameRefusal 0 "invalid_request" "correlation: expected a positive int32")
    | Some correlation ->
      command root
      |> Result.map (fun decoded -> { CommandFrame.Correlation = correlation; Command = decoded })
      |> Result.mapError (frameRefusal correlation "invalid_request")
  | Ok _ -> Error(frameRefusal 0 "invalid_request" "frame: expected a JSON object")

// ── Encoding (backend -> UI) ───────────────────────────────────────────────

/// The spelling JSON.stringify gives a finite double (ECMAScript
/// Number::toString): shortest round-trip digits, in exponent form only below
/// 1e-6 or from 1e21. Fidelity.Data's float case writes G17
/// ("0.10000000000000001"), so floats travel as exact number literals instead.
/// None for NaN and the infinities, which JSON.stringify writes as null.
let jsNumberText (value: float) : string option =
  if not (Double.IsFinite value) then None
  elif value = 0.0 then Some "0"
  else
    let shortest = value.ToString("R", CultureInfo.InvariantCulture)
    let negative = shortest.StartsWith '-'
    let unsigned = if negative then shortest.Substring 1 else shortest
    let mantissa, exponent =
      match unsigned.IndexOf 'E' with
      | -1 -> unsigned, 0
      | at -> unsigned.Substring(0, at), Int32.Parse(unsigned.Substring(at + 1), CultureInfo.InvariantCulture)
    let whole, fraction =
      match mantissa.IndexOf '.' with
      | -1 -> mantissa, ""
      | at -> mantissa.Substring(0, at), mantissa.Substring(at + 1)
    let raw = whole + fraction
    let leadingZeros = raw.Length - raw.TrimStart('0').Length
    let digits = raw.Trim('0')
    // value = digits × 10^(n - k), in the notation of Number::toString.
    let n = whole.Length + exponent - leadingZeros
    let k = digits.Length
    let body =
      if k <= n && n <= 21 then digits + String('0', n - k)
      elif 0 < n && n <= 21 then digits.Substring(0, n) + "." + digits.Substring n
      elif -6 < n && n <= 0 then "0." + String('0', -n) + digits
      else
        let power = n - 1
        let significand = if k = 1 then digits else digits.Substring(0, 1) + "." + digits.Substring 1
        significand + "e" + (if power < 0 then "-" else "+") + string (abs power)
    Some(if negative then "-" + body else body)

/// A lone surrogate cannot be encoded as UTF-8; it travels as U+FFFD, so the
/// frame is well formed whatever framing carries it.
let private wellFormed (value: string) : string =
  let lone index =
    let c = value[index]
    if Char.IsHighSurrogate c then not (index + 1 < value.Length && Char.IsLowSurrogate value[index + 1])
    elif Char.IsLowSurrogate c then not (index > 0 && Char.IsHighSurrogate value[index - 1])
    else false
  let mutable clean = true
  let mutable index = 0
  while clean && index < value.Length do
    if lone index then clean <- false
    index <- index + 1
  if clean then value
  else String(Array.init value.Length (fun i -> if lone i then '�' else value[i]))

let private str (value: string) = JsonValue.String(wellFormed value)

let private nullable (value: string option) =
  match value with
  | Some text -> str text
  | None -> JsonValue.Null

let private whole (value: int32) = JsonValue.ofInt64 (int64 value)

/// int64 travels as a decimal string: JavaScript numbers lose integers above 2^53.
let private wide (value: int64) = JsonValue.String(value.ToString(CultureInfo.InvariantCulture))

let private real (value: float) =
  jsNumberText value
  |> Option.bind (fun spelling ->
    match JsonNumberLiteral.tryCreate spelling with
    | Ok literal -> Some(JsonValue.NumberLiteral literal)
    | Error _ -> None)
  |> Option.defaultValue JsonValue.Null

let private targetFields (target: SessionTarget) =
  [ "host", str target.Worker.Host; "epoch", str target.Worker.Epoch; "session", str target.Session ]

let private artifactJson (a: ArtifactSummary) =
  JsonValue.Object [
    "generation", wide a.Generation
    "sourceVersion", str a.SourceVersion
    "artifactPath", str a.ArtifactPath
    "artifactSha256", str a.ArtifactSha256
    "compiledObjects", whole a.CompiledObjects
    "reusedObjects", whole a.ReusedObjects
    "retiredObjects", whole a.RetiredObjects
    "changedWitnesses", whole a.ChangedWitnesses
  ]

let private sessionJson (s: SessionStatus) =
  JsonValue.Object(
    targetFields s.Target
    @ [
      "generation", wide s.Generation
      "project", str s.Project
      "closed", JsonValue.Bool s.Closed
      "busy", JsonValue.Bool s.Busy
      "statusFresh", JsonValue.Bool s.StatusFresh
      "revocationPending", JsonValue.Bool s.RevocationPending
      "cleanupPending", JsonValue.Bool s.CleanupPending
      "formatterCleanupPending", JsonValue.Bool s.FormatterCleanupPending
      "current", (match s.Current with Some artifact -> artifactJson artifact | None -> JsonValue.Null)
      "backendError", nullable s.BackendError
      "formatterError", nullable s.FormatterError
      "cleanupError", nullable s.CleanupError
      "workerRetirementRequired", nullable s.WorkerRetirementRequired
      "statusError", nullable s.StatusError
      "workerError", nullable s.WorkerError
    ])

let private workerJson (worker: WorkerState) =
  match worker with
  | Unconfigured -> JsonValue.Object [ "state", JsonValue.String "unconfigured" ]
  | Idle -> JsonValue.Object [ "state", JsonValue.String "idle" ]
  | Running info ->
    JsonValue.Object [
      "state", JsonValue.String "running"
      "host", str info.Target.Host
      "epoch", str info.Target.Epoch
      "compilerVersion", str info.CompilerVersion
      "processId", (match info.ProcessId with Some pid -> whole pid | None -> JsonValue.Null)
    ]

let private completionJson (completion: Completion) =
  let kind (name: string) = "kind", JsonValue.String name
  match completion with
  | SessionOpened -> JsonValue.Object [ kind "session_opened" ]
  | EditReserved reservation -> JsonValue.Object [ kind "edit_reserved"; "reservation", str reservation ]
  | ReservationBuilt artifact -> JsonValue.Object [ kind "reservation_built"; "artifact", artifactJson artifact ]
  | RunFinished exitCode -> JsonValue.Object [ kind "run_finished"; "exitCode", whole exitCode ]
  | WorkCanceled -> JsonValue.Object [ kind "work_canceled" ]
  | SessionClosed -> JsonValue.Object [ kind "session_closed" ]
  | WorkerRetired -> JsonValue.Object [ kind "worker_retired" ]
  | SnapshotSent -> JsonValue.Object [ kind "snapshot_sent" ]

let private eventJson (event: Event) =
  let tag (name: string) = "event", JsonValue.String name
  match event with
  | Welcome welcome ->
    JsonValue.Object [
      tag "welcome"
      "protocolVersion", whole welcome.ProtocolVersion
      "daemonVersion", str welcome.DaemonVersion
      "startedAtMs", wide welcome.StartedAtMs
    ]
  | Snapshot snapshot ->
    JsonValue.Object [
      tag "snapshot"
      "revision", wide snapshot.Revision
      "worker", workerJson snapshot.Worker
      "sessions", JsonValue.Array(snapshot.Sessions |> Array.toList |> List.map sessionJson)
    ]
  | Accepted accepted ->
    JsonValue.Object [
      tag "accepted"
      "correlation", whole accepted.Correlation
      "target", JsonValue.Object(targetFields accepted.Target)
      "completion", completionJson accepted.Completion
    ]
  | Refused refused ->
    JsonValue.Object [
      tag "refused"
      "correlation", whole refused.Correlation
      "target", JsonValue.Object(targetFields refused.Target)
      "code", str refused.Code
      "message", str refused.Message
    ]
  | RunOutput run ->
    JsonValue.Object [
      tag "run_output"
      "correlation", whole run.Correlation
      "target", JsonValue.Object(targetFields run.Target)
      "generation", wide run.Generation
      "sourceVersion", str run.SourceVersion
      "exitCode", whole run.ExitCode
      "standardOutput", str run.StandardOutput
      "standardError", str run.StandardError
    ]
  | Health health ->
    JsonValue.Object [
      tag "health"
      "version", str health.Version
      "processId", whole health.ProcessId
      "mcpPort", whole health.McpPort
      "overall", str health.Overall
      "memoryPressure", str health.MemoryPressure
      "residentBytes", wide health.ResidentBytes
      "machineAvailableBytes", wide health.MachineAvailableBytes
      "machineTotalBytes", wide health.MachineTotalBytes
      "cpuPercent", real health.CpuPercent
      "processes",
      JsonValue.Array [
        for p in health.Processes ->
          JsonValue.Object [
            "processId", whole p.ProcessId
            "role", str p.Role
            "residentBytes", wide p.ResidentBytes
            "cpuPercent", real p.CpuPercent
          ]
      ]
      "alarms",
      JsonValue.Array [
        for alarm in health.Alarms ->
          JsonValue.Object [ "signal", str alarm.Signal; "state", str alarm.State; "message", str alarm.Message ]
      ]
    ]
  | Leases board ->
    JsonValue.Object [
      tag "leases"
      "active",
      JsonValue.Array [
        for grant in board.Active ->
          JsonValue.Object [
            "id", str grant.Id
            "kind", str grant.Kind
            "holder", str grant.Holder
            "grantedAtMs", wide grant.GrantedAtMs
            "expiresAtMs", wide grant.ExpiresAtMs
          ]
      ]
      "queued",
      JsonValue.Array [
        for wait in board.Queued ->
          JsonValue.Object [ "kind", str wait.Kind; "holder", str wait.Holder; "requestedAtMs", wide wait.RequestedAtMs ]
      ]
    ]
  | Uptime tick -> JsonValue.Object [ tag "uptime"; "uptimeMs", wide tick.UptimeMs ]

/// Encode one backend frame.
let encodeEvent (event: Event) : string = eventJson event |> Json.serialize
