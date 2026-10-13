/// One explicit BARE primitive profile for CLR and Fable: little-endian i32,
/// i64/f64, canonical uint lengths/tags, UTF-8 strings, bool/optional 0 or 1.
/// The envelope binds magic, protocol version and message family. No reflection,
/// JSON fallback, implicit type discovery or host-endian wire representation.
module Bozzetto.Web.Shared.BinaryCodec

open System
open Bozzetto.Web.Shared.Protocol

[<Literal>]
let MaxFrameBytes = 4 * 1024 * 1024
[<Literal>]
let MaxRequestBytes = 4096
[<Literal>]
let private MaxItems = 65536
[<Literal>]
let private MaxStringBytes = 1024 * 1024

type private Reader = { Bytes: byte array; mutable Offset: int; mutable Version: int }
type private Writer = { Bytes: ResizeArray<byte>; Limit: int }
type private Codec<'T> = { Read: Reader -> 'T; Write: Writer -> 'T -> unit }

let private fail message = invalidOp ("binary: " + message)
let private put w value =
  if w.Bytes.Count >= w.Limit then fail "frame too large"
  w.Bytes.Add value
let private take r =
  if r.Offset >= r.Bytes.Length then fail "truncated frame"
  let value = r.Bytes[r.Offset]
  r.Offset <- r.Offset + 1
  value
let private uint = {
  Write = fun w (value: int) ->
    if value < 0 then fail "negative length/tag"
    let mutable rest = uint32 value
    while rest >= 128u do
      put w (byte ((rest &&& 127u) ||| 128u))
      rest <- rest >>> 7
    put w (byte rest)
  Read = fun r ->
    let mutable value = 0u
    let mutable shift = 0
    let mutable more = true
    while more do
      let b = take r
      if shift = 28 && b > 7uy then fail "length/tag overflow"
      value <- value ||| (uint32 (b &&& 127uy) <<< shift)
      more <- b >= 128uy
      if not more && shift > 0 && b = 0uy then fail "noncanonical uint"
      shift <- shift + 7
      if more && shift > 28 then fail "length/tag overflow"
    int value
}
let private whole : Codec<int32> = {
  Write = fun w value -> for shift in 0 .. 8 .. 24 do put w (byte ((value >>> shift) &&& 255))
  Read = fun r ->
    let mutable value = 0
    for shift in 0 .. 8 .. 24 do value <- value ||| (int (take r) <<< shift)
    value
}
let private wide : Codec<int64> = {
  Write = fun w value -> for shift in 0 .. 8 .. 56 do put w (byte ((value >>> shift) &&& 255L))
  Read = fun r ->
    let mutable value = 0L
    for shift in 0 .. 8 .. 56 do value <- value ||| (int64 (take r) <<< shift)
    value
}
let private real : Codec<float> = {
  Write = fun w value ->
    if Double.IsNaN value || Double.IsInfinity value then fail "nonfinite float"
    let native = BitConverter.GetBytes value
    let bytes = if BitConverter.IsLittleEndian then native else Array.rev native
    for b in bytes do put w b
  Read = fun r ->
    let wire = Array.init 8 (fun _ -> take r)
    let bytes = if BitConverter.IsLittleEndian then wire else Array.rev wire
    let value = BitConverter.ToDouble(bytes, 0)
    if Double.IsNaN value || Double.IsInfinity value then fail "nonfinite float"
    value
}
let private flag : Codec<bool> = {
  Write = fun w value -> put w (if value then 1uy else 0uy)
  Read = fun r -> match take r with 0uy -> false | 1uy -> true | _ -> fail "invalid bool/optional tag"
}

/// Strict scalar UTF-8 in both runtimes, including rejection of overlong forms,
/// surrogate scalars and incomplete sequences. No replacement-character repair.
let private utf8 (value: string) =
  if isNull value then fail "null string"
  let bytes = ResizeArray<byte>()
  let add n =
    if bytes.Count >= MaxStringBytes then fail "string too large"
    bytes.Add(byte n)
  let mutable i = 0
  while i < value.Length do
    let mutable scalar = int value[i]
    i <- i + 1
    if scalar >= 0xD800 && scalar <= 0xDBFF then
      if i = value.Length || int value[i] < 0xDC00 || int value[i] > 0xDFFF then fail "invalid UTF-16 source"
      scalar <- 0x10000 + ((scalar - 0xD800) <<< 10) + int value[i] - 0xDC00
      i <- i + 1
    elif scalar >= 0xDC00 && scalar <= 0xDFFF then fail "invalid UTF-16 source"
    if scalar < 0x80 then add scalar
    elif scalar < 0x800 then
      add (0xC0 ||| (scalar >>> 6)); add (0x80 ||| (scalar &&& 63))
    elif scalar < 0x10000 then
      add (0xE0 ||| (scalar >>> 12)); add (0x80 ||| ((scalar >>> 6) &&& 63)); add (0x80 ||| (scalar &&& 63))
    else
      add (0xF0 ||| (scalar >>> 18)); add (0x80 ||| ((scalar >>> 12) &&& 63))
      add (0x80 ||| ((scalar >>> 6) &&& 63)); add (0x80 ||| (scalar &&& 63))
  bytes.ToArray()

let private text : Codec<string> = {
  Write = fun w value ->
    let bytes = utf8 value
    uint.Write w bytes.Length
    for b in bytes do put w b
  Read = fun r ->
    let length = uint.Read r
    if length > MaxStringBytes || length > r.Bytes.Length - r.Offset then fail "invalid string length"
    let finish = r.Offset + length
    let chars = ResizeArray<char>()
    while r.Offset < finish do
      let lead = int (take r)
      let count, minimum, initial =
        if lead < 0x80 then 0, 0, lead
        elif lead >= 0xC2 && lead <= 0xDF then 1, 0x80, lead &&& 31
        elif lead >= 0xE0 && lead <= 0xEF then 2, 0x800, lead &&& 15
        elif lead >= 0xF0 && lead <= 0xF4 then 3, 0x10000, lead &&& 7
        else fail "invalid UTF-8 lead"
      if count > finish - r.Offset then fail "truncated UTF-8"
      let mutable scalar = initial
      for _ in 1 .. count do
        let b = int (take r)
        if b < 0x80 || b > 0xBF then fail "invalid UTF-8 continuation"
        scalar <- (scalar <<< 6) ||| (b &&& 63)
      if scalar < minimum || scalar > 0x10FFFF || (scalar >= 0xD800 && scalar <= 0xDFFF) then fail "invalid UTF-8 scalar"
      if scalar < 0x10000 then chars.Add(char scalar)
      else
        chars.Add(char (0xD800 + ((scalar - 0x10000) >>> 10)))
        chars.Add(char (0xDC00 + ((scalar - 0x10000) &&& 1023)))
    String(chars.ToArray())
}
let private optional (codec: Codec<'T>) : Codec<'T option> = {
  Write = fun w value ->
    flag.Write w value.IsSome
    value |> Option.iter (codec.Write w)
  Read = fun r -> if flag.Read r then Some(codec.Read r) else None
}
let private array (codec: Codec<'T>) : Codec<'T array> = {
  Write = fun w values ->
    if isNull values || values.Length > MaxItems then fail "array too large"
    uint.Write w values.Length
    for value in values do codec.Write w value
  Read = fun r ->
    let count = uint.Read r
    if count > MaxItems || count > r.Bytes.Length - r.Offset then fail "invalid array length"
    Array.init count (fun _ -> codec.Read r)
}

// Explicit declaration-order fields and stable union tags; changes require a version change.

let private workerTarget : Codec<WorkerTarget> = {
  Write = fun w value ->
    text.Write w value.Host
    text.Write w value.Epoch
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    { WorkerTarget.Host = f0; Epoch = f1 }
}

let private sessionTarget : Codec<SessionTarget> = {
  Write = fun w value ->
    workerTarget.Write w value.Worker
    text.Write w value.Session
  Read = fun r ->
    let f0 = workerTarget.Read r
    let f1 = text.Read r
    { SessionTarget.Worker = f0; Session = f1 }
}

let private reserveEdit : Codec<ReserveEdit> = {
  Write = fun w value ->
    sessionTarget.Write w value.Target
    text.Write w value.Label
  Read = fun r ->
    let f0 = sessionTarget.Read r
    let f1 = text.Read r
    { ReserveEdit.Target = f0; Label = f1 }
}

let private buildReserved : Codec<BuildReserved> = {
  Write = fun w value ->
    sessionTarget.Write w value.Target
    text.Write w value.Reservation
  Read = fun r ->
    let f0 = sessionTarget.Read r
    let f1 = text.Read r
    { BuildReserved.Target = f0; Reservation = f1 }
}

let private runCurrent : Codec<RunCurrent> = {
  Write = fun w value ->
    sessionTarget.Write w value.Target
    (array text).Write w value.Arguments
  Read = fun r ->
    let f0 = sessionTarget.Read r
    let f1 = (array text).Read r
    { RunCurrent.Target = f0; Arguments = f1 }
}

let private nodeIdentity : Codec<NodeIdentity> = {
  Write = fun w value ->
    text.Write w value.Node
    text.Write w value.Incarnation
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    { NodeIdentity.Node = f0; Incarnation = f1 }
}

let private nodeObservationRequest : Codec<NodeObservationRequest> = {
  Write = fun w value ->
    text.Write w value.Node
    whole.Write w value.Seconds
  Read = fun r ->
    let f0 = text.Read r
    let f1 = whole.Read r
    { NodeObservationRequest.Node = f0; Seconds = f1 }
}

let private transcriptStream : Codec<TranscriptStream> = {
  Write = fun w value -> uint.Write w (match value with Stdout -> 0 | Stderr -> 1)
  Read = fun r -> match uint.Read r with 0 -> Stdout | 1 -> Stderr | _ -> fail "unknown transcript stream"
}

let private transcriptReadRequest : Codec<TranscriptReadRequest> = {
  Write = fun w value ->
    nodeIdentity.Write w value.Source
    text.Write w value.Journal
    wide.Write w value.Sequence
    transcriptStream.Write w value.Stream
    wide.Write w value.Offset
    whole.Write w value.MaxBytes
  Read = fun r ->
    let source = nodeIdentity.Read r
    let journal = text.Read r
    let sequence = wide.Read r
    let stream = transcriptStream.Read r
    let offset = wide.Read r
    let limit = whole.Read r
    { Source = source; Journal = journal; Sequence = sequence; Stream = stream; Offset = offset; MaxBytes = limit }
}

let private transcriptPage : Codec<TranscriptPage> = {
  Write = fun w value ->
    if value.Bytes.Length > MaxTranscriptPageBytes then fail "transcript page too large"
    text.Write w value.Journal
    wide.Write w value.Sequence
    transcriptStream.Write w value.Stream
    wide.Write w value.Offset
    (optional wide).Write w value.NextOffset
    wide.Write w value.TotalBytes
    uint.Write w value.Bytes.Length
    for b in value.Bytes do put w b
    flag.Write w value.Truncated
  Read = fun r ->
    let journal = text.Read r
    let sequence = wide.Read r
    let stream = transcriptStream.Read r
    let offset = wide.Read r
    let next = (optional wide).Read r
    let total = wide.Read r
    let length = uint.Read r
    if length > MaxTranscriptPageBytes || length > r.Bytes.Length - r.Offset then fail "invalid transcript byte length"
    let bytes = Array.init length (fun _ -> take r)
    let truncated = flag.Read r
    { Journal = journal; Sequence = sequence; Stream = stream; Offset = offset; NextOffset = next; TotalBytes = total; Bytes = bytes; Truncated = truncated }
}

let private transcriptOutcome : Codec<TranscriptReadOutcome> = {
  Write = fun w value ->
    match value with
    | TranscriptPage page -> uint.Write w 0; transcriptPage.Write w page
    | TranscriptRefused(code, message) -> uint.Write w 1; text.Write w code; text.Write w message
  Read = fun r ->
    match uint.Read r with
    | 0 -> TranscriptPage(transcriptPage.Read r)
    | 1 -> let code = text.Read r in TranscriptRefused(code, text.Read r)
    | _ -> fail "unknown transcript outcome"
}

let private nodeTranscript : Codec<NodeTranscript> = {
  Write = fun w value -> nodeIdentity.Write w value.Source; whole.Write w value.Correlation; transcriptOutcome.Write w value.Outcome
  Read = fun r ->
    let source = nodeIdentity.Read r
    let correlation = whole.Read r
    { Source = source; Correlation = correlation; Outcome = transcriptOutcome.Read r }
}

let private peerTranscriptRequest : Codec<PeerTranscriptRequest> = {
  Write = fun w value -> whole.Write w value.ProtocolVersion; transcriptReadRequest.Write w value.Request
  Read = fun r ->
    let version = whole.Read r
    { ProtocolVersion = version; Request = transcriptReadRequest.Read r }
}
let private peerTranscriptReply : Codec<PeerTranscriptReply> = {
  Write = fun w value -> nodeIdentity.Write w value.Source; transcriptOutcome.Write w value.Outcome
  Read = fun r ->
    let source = nodeIdentity.Read r
    { Source = source; Outcome = transcriptOutcome.Read r }
}

let private command : Codec<Command> = {
  Write = fun w value ->
    match value with
    | RequestSnapshot -> uint.Write w 0
    | ObserveResources item -> uint.Write w 1; whole.Write w item
    | ObserveNodeResources item -> uint.Write w 2; nodeObservationRequest.Write w item
    | OpenProject item -> uint.Write w 3; text.Write w item
    | Reserve item -> uint.Write w 4; reserveEdit.Write w item
    | Build item -> uint.Write w 5; buildReserved.Write w item
    | Run item -> uint.Write w 6; runCurrent.Write w item
    | Cancel item -> uint.Write w 7; sessionTarget.Write w item
    | CloseSession item -> uint.Write w 8; sessionTarget.Write w item
    | RetireWorker item -> uint.Write w 9; workerTarget.Write w item
    | ReadNodeTranscript item -> uint.Write w 10; transcriptReadRequest.Write w item
  Read = fun r ->
    match uint.Read r with
    | 0 -> RequestSnapshot
    | 1 -> ObserveResources(whole.Read r)
    | 2 -> ObserveNodeResources(nodeObservationRequest.Read r)
    | 3 -> OpenProject(text.Read r)
    | 4 -> Reserve(reserveEdit.Read r)
    | 5 -> Build(buildReserved.Read r)
    | 6 -> Run(runCurrent.Read r)
    | 7 -> Cancel(sessionTarget.Read r)
    | 8 -> CloseSession(sessionTarget.Read r)
    | 9 -> RetireWorker(workerTarget.Read r)
    | 10 -> ReadNodeTranscript(transcriptReadRequest.Read r)
    | _ -> fail "unknown Command tag"
}

let private commandFrame : Codec<CommandFrame> = {
  Write = fun w value ->
    whole.Write w value.Correlation
    command.Write w value.Command
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = command.Read r
    { CommandFrame.Correlation = f0; Command = f1 }
}

let private regionRow : Codec<RegionRow> = {
  Write = fun w value ->
    text.Write w value.Identity
    text.Write w value.Witness
    whole.Write w value.Visits
    text.Write w value.Object
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    let f2 = whole.Read r
    let f3 = text.Read r
    { RegionRow.Identity = f0; Witness = f1; Visits = f2; Object = f3 }
}

let private artifactSummary : Codec<ArtifactSummary> = {
  Write = fun w value ->
    wide.Write w value.Generation
    text.Write w value.SourceVersion
    text.Write w value.ArtifactPath
    text.Write w value.ArtifactSha256
    (array regionRow).Write w value.Regions
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = text.Read r
    let f2 = text.Read r
    let f3 = text.Read r
    let f4 = (array regionRow).Read r
    { ArtifactSummary.Generation = f0; SourceVersion = f1; ArtifactPath = f2; ArtifactSha256 = f3; Regions = f4 }
}

let private sessionStatus : Codec<SessionStatus> = {
  Write = fun w value ->
    sessionTarget.Write w value.Target
    wide.Write w value.Generation
    text.Write w value.Project
    flag.Write w value.Closed
    flag.Write w value.Busy
    flag.Write w value.StatusFresh
    flag.Write w value.RevocationPending
    flag.Write w value.CleanupPending
    flag.Write w value.FormatterCleanupPending
    (optional artifactSummary).Write w value.Current
    (optional text).Write w value.BackendError
    (optional text).Write w value.FormatterError
    (optional text).Write w value.CleanupError
    (optional text).Write w value.WorkerRetirementRequired
    (optional text).Write w value.StatusError
    (optional text).Write w value.WorkerError
  Read = fun r ->
    let f0 = sessionTarget.Read r
    let f1 = wide.Read r
    let f2 = text.Read r
    let f3 = flag.Read r
    let f4 = flag.Read r
    let f5 = flag.Read r
    let f6 = flag.Read r
    let f7 = flag.Read r
    let f8 = flag.Read r
    let f9 = (optional artifactSummary).Read r
    let f10 = (optional text).Read r
    let f11 = (optional text).Read r
    let f12 = (optional text).Read r
    let f13 = (optional text).Read r
    let f14 = (optional text).Read r
    let f15 = (optional text).Read r
    { SessionStatus.Target = f0; Generation = f1; Project = f2; Closed = f3; Busy = f4; StatusFresh = f5; RevocationPending = f6; CleanupPending = f7; FormatterCleanupPending = f8; Current = f9; BackendError = f10; FormatterError = f11; CleanupError = f12; WorkerRetirementRequired = f13; StatusError = f14; WorkerError = f15 }
}

let private workerInfo : Codec<WorkerInfo> = {
  Write = fun w value ->
    workerTarget.Write w value.Target
    text.Write w value.CompilerVersion
    (optional whole).Write w value.ProcessId
  Read = fun r ->
    let f0 = workerTarget.Read r
    let f1 = text.Read r
    let f2 = (optional whole).Read r
    { WorkerInfo.Target = f0; CompilerVersion = f1; ProcessId = f2 }
}

let private workerState : Codec<WorkerState> = {
  Write = fun w value ->
    match value with
    | Unconfigured -> uint.Write w 0
    | Idle -> uint.Write w 1
    | Running item -> uint.Write w 2; workerInfo.Write w item
  Read = fun r ->
    match uint.Read r with
    | 0 -> Unconfigured
    | 1 -> Idle
    | 2 -> Running(workerInfo.Read r)
    | _ -> fail "unknown WorkerState tag"
}

let private composerSnapshot : Codec<ComposerSnapshot> = {
  Write = fun w value ->
    wide.Write w value.Revision
    workerState.Write w value.Worker
    (array sessionStatus).Write w value.Sessions
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = workerState.Read r
    let f2 = (array sessionStatus).Read r
    { ComposerSnapshot.Revision = f0; Worker = f1; Sessions = f2 }
}

let private completion : Codec<Completion> = {
  Write = fun w value ->
    match value with
    | SessionOpened -> uint.Write w 0
    | EditReserved item -> uint.Write w 1; text.Write w item
    | ReservationBuilt item -> uint.Write w 2; artifactSummary.Write w item
    | RunFinished item -> uint.Write w 3; whole.Write w item
    | WorkCanceled -> uint.Write w 4
    | SessionClosed -> uint.Write w 5
    | WorkerRetired -> uint.Write w 6
    | SnapshotSent -> uint.Write w 7
  Read = fun r ->
    match uint.Read r with
    | 0 -> SessionOpened
    | 1 -> EditReserved(text.Read r)
    | 2 -> ReservationBuilt(artifactSummary.Read r)
    | 3 -> RunFinished(whole.Read r)
    | 4 -> WorkCanceled
    | 5 -> SessionClosed
    | 6 -> WorkerRetired
    | 7 -> SnapshotSent
    | _ -> fail "unknown Completion tag"
}

let private commandAcceptance : Codec<CommandAcceptance> = {
  Write = fun w value ->
    whole.Write w value.Correlation
    sessionTarget.Write w value.Target
    completion.Write w value.Completion
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = sessionTarget.Read r
    let f2 = completion.Read r
    { CommandAcceptance.Correlation = f0; Target = f1; Completion = f2 }
}

let private commandRefusal : Codec<CommandRefusal> = {
  Write = fun w value ->
    whole.Write w value.Correlation
    sessionTarget.Write w value.Target
    text.Write w value.Code
    text.Write w value.Message
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = sessionTarget.Read r
    let f2 = text.Read r
    let f3 = text.Read r
    { CommandRefusal.Correlation = f0; Target = f1; Code = f2; Message = f3 }
}

let private runTranscript : Codec<RunTranscript> = {
  Write = fun w value ->
    whole.Write w value.Correlation
    sessionTarget.Write w value.Target
    wide.Write w value.Generation
    text.Write w value.SourceVersion
    whole.Write w value.ExitCode
    text.Write w value.StandardOutput
    text.Write w value.StandardError
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = sessionTarget.Read r
    let f2 = wide.Read r
    let f3 = text.Read r
    let f4 = whole.Read r
    let f5 = text.Read r
    let f6 = text.Read r
    { RunTranscript.Correlation = f0; Target = f1; Generation = f2; SourceVersion = f3; ExitCode = f4; StandardOutput = f5; StandardError = f6 }
}

let private processSample : Codec<ProcessSample> = {
  Write = fun w value ->
    whole.Write w value.ProcessId
    text.Write w value.Role
    wide.Write w value.ResidentBytes
    real.Write w value.CpuPercent
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = text.Read r
    let f2 = wide.Read r
    let f3 = real.Read r
    { ProcessSample.ProcessId = f0; Role = f1; ResidentBytes = f2; CpuPercent = f3 }
}

let private healthAlarm : Codec<HealthAlarm> = {
  Write = fun w value ->
    text.Write w value.Signal
    text.Write w value.State
    text.Write w value.Message
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    let f2 = text.Read r
    { HealthAlarm.Signal = f0; State = f1; Message = f2 }
}

let private daemonHealth : Codec<DaemonHealth> = {
  Write = fun w value ->
    wide.Write w value.Revision
    text.Write w value.Version
    whole.Write w value.ProcessId
    whole.Write w value.McpPort
    text.Write w value.Overall
    text.Write w value.MemoryPressure
    text.Write w value.MemoryPressureNote
    wide.Write w value.ResidentBytes
    wide.Write w value.MachineAvailableBytes
    wide.Write w value.MachineTotalBytes
    real.Write w value.CpuPercent
    (array processSample).Write w value.Processes
    (array healthAlarm).Write w value.Alarms
    wide.Write w value.SampledAtMs
    wide.Write w value.RecordAtMs
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = text.Read r
    let f2 = whole.Read r
    let f3 = whole.Read r
    let f4 = text.Read r
    let f5 = text.Read r
    let f6 = text.Read r
    let f7 = wide.Read r
    let f8 = wide.Read r
    let f9 = wide.Read r
    let f10 = real.Read r
    let f11 = (array processSample).Read r
    let f12 = (array healthAlarm).Read r
    let f13 = wide.Read r
    let f14 = wide.Read r
    { DaemonHealth.Revision = f0; Version = f1; ProcessId = f2; McpPort = f3; Overall = f4; MemoryPressure = f5; MemoryPressureNote = f6; ResidentBytes = f7; MachineAvailableBytes = f8; MachineTotalBytes = f9; CpuPercent = f10; Processes = f11; Alarms = f12; SampledAtMs = f13; RecordAtMs = f14 }
}

let private leaseGrant : Codec<LeaseGrant> = {
  Write = fun w value ->
    text.Write w value.Id
    text.Write w value.Kind
    text.Write w value.Holder
    wide.Write w value.GrantedAtMs
    wide.Write w value.ExpiresAtMs
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    let f2 = text.Read r
    let f3 = wide.Read r
    let f4 = wide.Read r
    { LeaseGrant.Id = f0; Kind = f1; Holder = f2; GrantedAtMs = f3; ExpiresAtMs = f4 }
}

let private leaseWait : Codec<LeaseWait> = {
  Write = fun w value ->
    text.Write w value.Kind
    text.Write w value.Holder
    wide.Write w value.RequestedAtMs
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    let f2 = wide.Read r
    { LeaseWait.Kind = f0; Holder = f1; RequestedAtMs = f2 }
}

let private leaseBoard : Codec<LeaseBoard> = {
  Write = fun w value ->
    wide.Write w value.Revision
    (array leaseGrant).Write w value.Active
    (array leaseWait).Write w value.Queued
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = (array leaseGrant).Read r
    let f2 = (array leaseWait).Read r
    { LeaseBoard.Revision = f0; Active = f1; Queued = f2 }
}

let private bridgeWelcome : Codec<BridgeWelcome> = {
  Write = fun w value ->
    whole.Write w value.ProtocolVersion
    text.Write w value.DaemonVersion
    wide.Write w value.StartedAtMs
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = text.Read r
    let f2 = wide.Read r
    { BridgeWelcome.ProtocolVersion = f0; DaemonVersion = f1; StartedAtMs = f2 }
}

let private uptimeTick : Codec<UptimeTick> = {
  Write = fun w value ->
    wide.Write w value.UptimeMs
  Read = fun r ->
    let f0 = wide.Read r
    { UptimeTick.UptimeMs = f0 }
}

let private resourceMetric : Codec<ResourceMetric> = {
  Write = fun w value ->
    text.Write w value.Name
    (optional real).Write w value.Value
    text.Write w value.Unit
  Read = fun r ->
    let f0 = text.Read r
    let f1 = (optional real).Read r
    let f2 = text.Read r
    { ResourceMetric.Name = f0; Value = f1; Unit = f2 }
}

let private resourceProcess : Codec<ResourceProcess> = {
  Write = fun w value ->
    whole.Write w value.ProcessId
    whole.Write w value.ParentId
    text.Write w value.StartTicks
    text.Write w value.Name
    text.Write w value.Context
    (optional real).Write w value.CpuPercent
    real.Write w value.ResidentBytes
    whole.Write w value.Threads
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = whole.Read r
    let f2 = text.Read r
    let f3 = text.Read r
    let f4 = text.Read r
    let f5 = (optional real).Read r
    let f6 = real.Read r
    let f7 = whole.Read r
    { ResourceProcess.ProcessId = f0; ParentId = f1; StartTicks = f2; Name = f3; Context = f4; CpuPercent = f5; ResidentBytes = f6; Threads = f7 }
}

let private hostResources : Codec<HostResources> = {
  Write = fun w value ->
    wide.Write w value.Sequence
    text.Write w value.Status
    wide.Write w value.SampledAtMs
    real.Write w value.IntervalMs
    real.Write w value.SampleCostMs
    (array resourceMetric).Write w value.Metrics
    (array resourceProcess).Write w value.Processes
    whole.Write w value.ProcessCount
    whole.Write w value.UnreadableCount
    whole.Write w value.OmittedCount
    text.Write w value.Note
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = text.Read r
    let f2 = wide.Read r
    let f3 = real.Read r
    let f4 = real.Read r
    let f5 = (array resourceMetric).Read r
    let f6 = (array resourceProcess).Read r
    let f7 = whole.Read r
    let f8 = whole.Read r
    let f9 = whole.Read r
    let f10 = text.Read r
    { HostResources.Sequence = f0; Status = f1; SampledAtMs = f2; IntervalMs = f3; SampleCostMs = f4; Metrics = f5; Processes = f6; ProcessCount = f7; UnreadableCount = f8; OmittedCount = f9; Note = f10 }
}

let private agentUsage : Codec<AgentUsage> = {
  Write = fun w value ->
    wide.Write w value.Input
    wide.Write w value.Output
    wide.Write w value.CacheRead
    wide.Write w value.CacheWrite
    wide.Write w value.Total
    (optional real).Write w value.EstimateUsd
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = wide.Read r
    let f2 = wide.Read r
    let f3 = wide.Read r
    let f4 = wide.Read r
    let f5 = (optional real).Read r
    { AgentUsage.Input = f0; Output = f1; CacheRead = f2; CacheWrite = f3; Total = f4; EstimateUsd = f5 }
}

let private agentRun : Codec<AgentRun> = {
  Write = fun w value ->
    text.Write w value.Id
    text.Write w value.Member
    text.Write w value.Role
    text.Write w value.Name
    text.Write w value.Session
    wide.Write w value.ReportSequence
    text.Write w value.Model
    text.Write w value.Endpoint
    text.Write w value.Project
    text.Write w value.Focus
    text.Write w value.Operation
    text.Write w value.Status
    wide.Write w value.UpdatedAtMs
    (array text).Write w value.Activity
    wide.Write w value.OmittedActivity
    (optional agentUsage).Write w value.Usage
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    let f2 = text.Read r
    let f3 = text.Read r
    let f4 = text.Read r
    let f5 = wide.Read r
    let f6 = text.Read r
    let f7 = text.Read r
    let f8 = text.Read r
    let f9 = text.Read r
    let f10 = text.Read r
    let f11 = text.Read r
    let f12 = wide.Read r
    let f13 = (array text).Read r
    let f14 = wide.Read r
    let f15 = (optional agentUsage).Read r
    { AgentRun.Id = f0; Member = f1; Role = f2; Name = f3; Session = f4; ReportSequence = f5; Model = f6; Endpoint = f7; Project = f8; Focus = f9; Operation = f10; Status = f11; UpdatedAtMs = f12; Activity = f13; OmittedActivity = f14; Usage = f15 }
}

let private agentPresence : Codec<AgentPresence> = {
  Write = fun w value ->
    text.Write w value.Name
    text.Write w value.Source
    text.Write w value.Member
    text.Write w value.Project
    text.Write w value.Detail
    wide.Write w value.SinceMs
  Read = fun r ->
    let f0 = text.Read r
    let f1 = text.Read r
    let f2 = text.Read r
    let f3 = text.Read r
    let f4 = text.Read r
    let f5 = wide.Read r
    { AgentPresence.Name = f0; Source = f1; Member = f2; Project = f3; Detail = f4; SinceMs = f5 }
}

let private agentWorkBoard : Codec<AgentWorkBoard> = {
  Write = fun w value ->
    text.Write w value.Incarnation
    wide.Write w value.Sequence
    text.Write w value.ExecutionHost
    whole.Write w value.Capacity
    (array agentRun).Write w value.Runs
    (array agentPresence).Write w value.Presence
  Read = fun r ->
    let f0 = text.Read r
    let f1 = wide.Read r
    let f2 = text.Read r
    let f3 = whole.Read r
    let f4 = (array agentRun).Read r
    let f5 = (array agentPresence).Read r
    { AgentWorkBoard.Incarnation = f0; Sequence = f1; ExecutionHost = f2; Capacity = f3; Runs = f4; Presence = f5 }
}

let private operationCompiler : Codec<OperationCompiler> = {
  Write = fun w value -> wide.Write w value.Generation; text.Write w value.SourceVersion
  Read = fun r ->
    let generation = wide.Read r
    let source = text.Read r
    { Generation = generation; SourceVersion = source }
}

let private leasedCommand : Codec<LeasedCommandInfo> = {
  Write = fun w value ->
    text.Write w value.Kind
    text.Write w value.LeaseId
    text.Write w value.RunId
    text.Write w value.Executable
    (array text).Write w value.Arguments
    whole.Write w value.ProcessId
    wide.Write w value.StartedAtMs
  Read = fun r ->
    let kind = text.Read r
    let lease = text.Read r
    let run = text.Read r
    let executable = text.Read r
    let arguments = (array text).Read r
    let pid = whole.Read r
    let started = wide.Read r
    { Kind = kind; LeaseId = lease; RunId = run; Executable = executable
      Arguments = arguments; ProcessId = pid; StartedAtMs = started }
}

let private operationRun : Codec<OperationRun> = {
  Write = fun w value ->
    whole.Write w value.ExitCode
    (optional operationCompiler).Write w value.Compiler
    text.Write w value.StandardOutput
    text.Write w value.StandardError
    flag.Write w value.Dropped
  Read = fun r ->
    let f0 = whole.Read r
    let compiler =
      if r.Version = LegacyProtocolVersion then Some(operationCompiler.Read r)
      else (optional operationCompiler).Read r
    let f3 = text.Read r
    let f4 = text.Read r
    let f5 = flag.Read r
    { OperationRun.ExitCode = f0; Compiler = compiler; StandardOutput = f3; StandardError = f4; Dropped = f5 }
}

let private operationEntry : Codec<OperationEntry> = {
  Write = fun w value ->
    wide.Write w value.Sequence
    wide.Write w value.AtMs
    text.Write w value.CallerLabel
    text.Write w value.Operation
    text.Write w value.Session
    text.Write w value.Project
    flag.Write w value.Ok
    text.Write w value.Code
    text.Write w value.Message
    (optional operationRun).Write w value.Run
    (optional leasedCommand).Write w value.Lease
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = wide.Read r
    let f2 = text.Read r
    let f3 = text.Read r
    let f4 = text.Read r
    let f5 = text.Read r
    let f6 = flag.Read r
    let f7 = text.Read r
    let f8 = text.Read r
    let f9 = (optional operationRun).Read r
    let lease = if r.Version = LegacyProtocolVersion then None else (optional leasedCommand).Read r
    { OperationEntry.Sequence = f0; AtMs = f1; CallerLabel = f2; Operation = f3; Session = f4; Project = f5; Ok = f6; Code = f7; Message = f8; Run = f9; Lease = lease }
}

let private operationsBoard : Codec<OperationsBoard> = {
  Write = fun w value ->
    wide.Write w value.Revision
    text.Write w value.Incarnation
    (array operationEntry).Write w value.Entries
    (optional text).Write w value.StorageError
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = text.Read r
    let f2 = (array operationEntry).Read r
    { OperationsBoard.Revision = f0; Incarnation = f1; Entries = f2; StorageError = (optional text).Read r }
}

let private workSnapshot : Codec<WorkSnapshot> = {
  Write = fun w value ->
    wide.Write w value.Sequence
    wide.Write w value.SampledAtMs
    composerSnapshot.Write w value.Snapshot
    leaseBoard.Write w value.Leases
    operationsBoard.Write w value.Operations
    flag.Write w value.TranscriptsOmitted
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = wide.Read r
    let f2 = composerSnapshot.Read r
    let f3 = leaseBoard.Read r
    let f4 = operationsBoard.Read r
    { WorkSnapshot.Sequence = f0; SampledAtMs = f1; Snapshot = f2; Leases = f3; Operations = f4; TranscriptsOmitted = flag.Read r }
}

let private nodeWork : Codec<NodeWork> = {
  Write = fun w value ->
    nodeIdentity.Write w value.Source
    flag.Write w value.Local
    text.Write w value.Status
    text.Write w value.Note
    (optional workSnapshot).Write w value.Value
  Read = fun r ->
    let f0 = nodeIdentity.Read r
    let f1 = flag.Read r
    let f2 = text.Read r
    let f3 = text.Read r
    let f4 = (optional workSnapshot).Read r
    { NodeWork.Source = f0; Local = f1; Status = f2; Note = f3; Value = f4 }
}

let private workBoard : Codec<WorkBoard> = {
  Write = fun w value ->
    wide.Write w value.Revision
    (array nodeWork).Write w value.Nodes
  Read = fun r ->
    let f0 = wide.Read r
    let f1 = (array nodeWork).Read r
    { WorkBoard.Revision = f0; Nodes = f1 }
}

let private nodeResources : Codec<NodeResources> = {
  Write = fun w value ->
    nodeIdentity.Write w value.Source
    whole.Write w value.Correlation
    hostResources.Write w value.Value
  Read = fun r ->
    let f0 = nodeIdentity.Read r
    let f1 = whole.Read r
    let f2 = hostResources.Read r
    { NodeResources.Source = f0; Correlation = f1; Value = f2 }
}

let private peerObserveRequest : Codec<PeerObserveRequest> = {
  Write = fun w value ->
    whole.Write w value.ProtocolVersion
    text.Write w value.Node
    text.Write w value.Topic
    whole.Write w value.Seconds
  Read = fun r ->
    let f0 = whole.Read r
    let f1 = text.Read r
    let f2 = text.Read r
    let f3 = whole.Read r
    { PeerObserveRequest.ProtocolVersion = f0; Node = f1; Topic = f2; Seconds = f3 }
}

let private peerFrame : Codec<PeerFrame> = {
  Write = fun w value ->
    match value with
    | PeerHello item -> uint.Write w 0; nodeIdentity.Write w item
    | PeerWork item -> uint.Write w 1; workSnapshot.Write w item
    | PeerResources item -> uint.Write w 2; hostResources.Write w item
    | PeerUnavailable item -> uint.Write w 3; text.Write w item
    | PeerHeartbeat item -> uint.Write w 4; uptimeTick.Write w item
  Read = fun r ->
    match uint.Read r with
    | 0 -> PeerHello(nodeIdentity.Read r)
    | 1 -> PeerWork(workSnapshot.Read r)
    | 2 -> PeerResources(hostResources.Read r)
    | 3 -> PeerUnavailable(text.Read r)
    | 4 -> PeerHeartbeat(uptimeTick.Read r)
    | _ -> fail "unknown PeerFrame tag"
}

let private event : Codec<Event> = {
  Write = fun w value ->
    match value with
    | Welcome item -> uint.Write w 0; bridgeWelcome.Write w item
    | Snapshot item -> uint.Write w 1; composerSnapshot.Write w item
    | Accepted item -> uint.Write w 2; commandAcceptance.Write w item
    | Refused item -> uint.Write w 3; commandRefusal.Write w item
    | RunOutput item -> uint.Write w 4; runTranscript.Write w item
    | Health item -> uint.Write w 5; daemonHealth.Write w item
    | Leases item -> uint.Write w 6; leaseBoard.Write w item
    | Uptime item -> uint.Write w 7; uptimeTick.Write w item
    | Resources item -> uint.Write w 8; hostResources.Write w item
    | AgentWork item -> uint.Write w 9; agentWorkBoard.Write w item
    | Operations item -> uint.Write w 10; operationsBoard.Write w item
    | WorkNodes item -> uint.Write w 11; workBoard.Write w item
    | NodeResources item -> uint.Write w 12; nodeResources.Write w item
    | NodeTranscript item -> uint.Write w 13; nodeTranscript.Write w item
  Read = fun r ->
    match uint.Read r with
    | 0 -> Welcome(bridgeWelcome.Read r)
    | 1 -> Snapshot(composerSnapshot.Read r)
    | 2 -> Accepted(commandAcceptance.Read r)
    | 3 -> Refused(commandRefusal.Read r)
    | 4 -> RunOutput(runTranscript.Read r)
    | 5 -> Health(daemonHealth.Read r)
    | 6 -> Leases(leaseBoard.Read r)
    | 7 -> Uptime(uptimeTick.Read r)
    | 8 -> Resources(hostResources.Read r)
    | 9 -> AgentWork(agentWorkBoard.Read r)
    | 10 -> Operations(operationsBoard.Read r)
    | 11 -> WorkNodes(workBoard.Read r)
    | 12 -> NodeResources(nodeResources.Read r)
    | 13 -> NodeTranscript(nodeTranscript.Read r)
    | _ -> fail "unknown Event tag"
}

let private commandObservationResult : Codec<CommandObservationResult> = {
  Write = fun w value ->
    wide.Write w value.FinishedAtMs
    whole.Write w value.ExitCode
    text.Write w value.StandardOutput
    text.Write w value.StandardError
    flag.Write w value.OutputTruncated
    flag.Write w value.ErrorTruncated
  Read = fun r ->
    let finished = wide.Read r
    let exitCode = whole.Read r
    let output = text.Read r
    let error = text.Read r
    let outputTruncated = flag.Read r
    let errorTruncated = flag.Read r
    { FinishedAtMs = finished; ExitCode = exitCode; StandardOutput = output; StandardError = error
      OutputTruncated = outputTruncated; ErrorTruncated = errorTruncated }
}

let private commandObservationRequest : Codec<CommandObservationRequest> = {
  Write = fun w value ->
    text.Write w value.Holder
    text.Write w value.Project
    text.Write w value.Kind
    text.Write w value.LeaseId
    text.Write w value.RunId
    text.Write w value.Executable
    (array text).Write w value.Arguments
    whole.Write w value.ProcessId
    wide.Write w value.StartedAtMs
    match value.Observation with
    | CommandStarted -> uint.Write w 0
    | CommandFinished result -> uint.Write w 1; commandObservationResult.Write w result
  Read = fun r ->
    let holder = text.Read r
    let project = text.Read r
    let kind = text.Read r
    let lease = text.Read r
    let run = text.Read r
    let executable = text.Read r
    let arguments = (array text).Read r
    let pid = whole.Read r
    let started = wide.Read r
    let observation =
      match uint.Read r with
      | 0 -> CommandStarted
      | 1 -> CommandFinished(commandObservationResult.Read r)
      | _ -> fail "unknown CommandObservation tag"
    { Holder = holder; Project = project; Kind = kind; LeaseId = lease; RunId = run
      Executable = executable; Arguments = arguments; ProcessId = pid; StartedAtMs = started; Observation = observation }
}

let private commandObservationReply : Codec<CommandObservationReply> = {
  Write = fun w value ->
    match value with
    | CommandObservationAccepted sequence -> uint.Write w 0; wide.Write w sequence
    | CommandObservationRefused reason -> uint.Write w 1; text.Write w reason
  Read = fun r ->
    match uint.Read r with
    | 0 -> CommandObservationAccepted(wide.Read r)
    | 1 -> CommandObservationRefused(text.Read r)
    | _ -> fail "unknown CommandObservationReply tag"
}

let private encodeVersion version family limit codec value =
  let writer = { Writer.Bytes = ResizeArray<byte>(); Limit = limit }
  for b in [| 0x42uy; 0x5Auy; 0x57uy; 0x31uy |] do put writer b
  uint.Write writer version
  uint.Write writer family
  codec.Write writer value
  writer.Bytes.ToArray()

let private encode family limit codec value = encodeVersion ProtocolVersion family limit codec value

let private decode family limit codec (bytes: byte array) =
  try
    if isNull bytes || bytes.Length > limit then fail "frame too large"
    let reader = { Reader.Bytes = bytes; Offset = 0; Version = 0 }
    for b in [| 0x42uy; 0x5Auy; 0x57uy; 0x31uy |] do
      if take reader <> b then fail "wrong magic"
    reader.Version <- uint.Read reader
    let legacyFamily = family >= 1 && family <= 6
    if reader.Version <> ProtocolVersion && (not legacyFamily || reader.Version <> LegacyProtocolVersion) then fail "unsupported version"
    if uint.Read reader <> family then fail "wrong message family"
    let value = codec.Read reader
    if reader.Offset <> bytes.Length then fail "trailing bytes"
    Ok value
  with error -> Error error.Message

let private validateTranscript (request: TranscriptReadRequest) =
  if String.IsNullOrWhiteSpace request.Source.Node || String.IsNullOrWhiteSpace request.Source.Incarnation || String.IsNullOrWhiteSpace request.Journal then Error "transcript identity: required"
  elif request.Sequence <= 0L || request.Offset < 0L then Error "transcript cursor: invalid"
  elif request.MaxBytes < 1 || request.MaxBytes > MaxTranscriptPageBytes then Error "transcript limit: expected 1 through 32768"
  else Ok request

let private validateCommand (frame: CommandFrame) =
  if frame.Correlation <= 0 then Error "correlation: expected a positive int32"
  else
    match frame.Command with
    | ObserveResources seconds | ObserveNodeResources { Seconds = seconds } when seconds < 0 || seconds > 120 ->
      Error "seconds: expected 0 through 120"
    | ObserveNodeResources request when String.IsNullOrWhiteSpace request.Node -> Error "node: required"
    | ReadNodeTranscript request -> validateTranscript request |> Result.map (fun _ -> frame)
    | _ -> Ok frame

let private validatePeer (request: PeerObserveRequest) =
  if request.ProtocolVersion <> ProtocolVersion && request.ProtocolVersion <> LegacyProtocolVersion then Error "unsupported peer version"
  elif String.IsNullOrWhiteSpace request.Node then Error "node: required"
  elif request.Topic <> "work" && request.Topic <> "resources" then Error "unknown observation topic"
  elif request.Seconds < 0 || request.Seconds > 120 then Error "seconds: expected 0 through 120"
  else Ok request

let private require = function Ok value -> value | Error reason -> fail reason
let encodeCommand value = encode 1 MaxFrameBytes commandFrame (validateCommand value |> require)
let decodeCommand bytes = decode 1 MaxFrameBytes commandFrame bytes |> Result.bind validateCommand
let encodeEvent value = encode 2 MaxFrameBytes event value
let decodeEvent bytes = decode 2 MaxFrameBytes event bytes
let encodePeerObserveRequest (value: PeerObserveRequest) = encodeVersion value.ProtocolVersion 3 MaxRequestBytes peerObserveRequest (validatePeer value |> require)
let decodePeerObserveRequest bytes = decode 3 MaxRequestBytes peerObserveRequest bytes |> Result.bind validatePeer
let encodePeerFrame value = encode 4 MaxFrameBytes peerFrame value
let decodePeerFrame bytes = decode 4 MaxFrameBytes peerFrame bytes

let private validatePeerTranscript (request: PeerTranscriptRequest) =
  if request.ProtocolVersion <> ProtocolVersion && request.ProtocolVersion <> LegacyProtocolVersion then Error "unsupported peer version"
  else validateTranscript request.Request |> Result.map (fun _ -> request)
let encodePeerTranscriptRequest (value: PeerTranscriptRequest) = encodeVersion value.ProtocolVersion 5 MaxRequestBytes peerTranscriptRequest (validatePeerTranscript value |> require)
let decodePeerTranscriptRequest bytes = decode 5 MaxRequestBytes peerTranscriptRequest bytes |> Result.bind validatePeerTranscript
let encodePeerTranscriptReply value = encode 6 MaxTranscriptBytes peerTranscriptReply value
let decodePeerTranscriptReply bytes = decode 6 MaxTranscriptBytes peerTranscriptReply bytes

// These new private reporting families have no legacy JSON or v9 representation.
let encodeCommandObservationRequest value = encode 7 MaxCommandObservationBytes commandObservationRequest value
let decodeCommandObservationRequest bytes = decode 7 MaxCommandObservationBytes commandObservationRequest bytes
let encodeCommandObservationReply value = encode 8 MaxCommandObservationReplyBytes commandObservationReply value
let decodeCommandObservationReply bytes = decode 8 MaxCommandObservationReplyBytes commandObservationReply bytes
