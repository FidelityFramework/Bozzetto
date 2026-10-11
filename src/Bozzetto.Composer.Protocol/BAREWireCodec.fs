namespace Bozzetto.Composer.Protocol

open System
open System.Text
open BAREWire.Encoding
open Bozzetto.Providers

/// Explicit schema codecs: no reflection, JSON projection, or encoding fallback.
module BAREWireCodec =
  [<Literal>]
  let ProtocolVersion = 2us
  [<Literal>]
  let MaximumBody = 1048576
  [<Literal>]
  let MaximumPayload = MaximumBody - 5
  [<Literal>]
  let ContractDigest = "F641CBC43607354163EACAE34D8A2A7BA904FCFBFC35295BFBEB73943C6CA9BC"

  let agreement = { ProtocolVersion = ProtocolVersion; Encoding = EncodingId.BAREWire1; ContractDigest = ContractDigest }
  let private utf8 = UTF8Encoding(false, true)

  let private uintLength (value: uint64) =
    let mutable value = value
    let mutable length = 1
    while value >= 128UL do
      value <- value >>> 7
      length <- length + 1
    length

  type private Writer(maximum: int) =
    let mutable bytes = Array.zeroCreate<byte> (min maximum 4096)
    let mutable at = 0
    let ensure count =
      if Cursor.isOk at then
        if count < 0 || count > maximum - at then at <- Cursor.Fault
        elif at + count > bytes.Length then
          Array.Resize(&bytes, min maximum (max (at + count) (bytes.Length * 2)))
    member _.U16(value) = ensure 2; at <- Encoder.writeU16 bytes at value
    member _.U32(value) = ensure 4; at <- Encoder.writeU32 bytes at value
    member _.I32(value) = ensure 4; at <- Encoder.writeI32 bytes at value
    member _.I64(value) = ensure 8; at <- Encoder.writeI64 bytes at value
    member _.U64(value) = ensure 8; at <- Encoder.writeU64 bytes at value
    member _.Bool(value) = ensure 1; at <- Encoder.writeBool bytes at value
    member _.Tag(value) = ensure (uintLength (uint64 value)); at <- Encoder.writeTag bytes at value
    member _.Data(value: byte array) =
      ensure (uintLength (uint64 value.Length) + value.Length)
      at <- Encoder.writeData bytes at value
    member this.String(value: string) =
      let length = utf8.GetByteCount value
      if length > MaximumPayload then at <- Cursor.Fault
      elif Cursor.isOk at then this.Data(utf8.GetBytes value)
    member this.Optional(write, value) =
      match value with
      | None -> this.Bool false
      | Some item -> this.Bool true; write item
    member _.Array(write, values: 'T array) =
      if values.Length > MaximumPayload then at <- Cursor.Fault
      else
        ensure (uintLength (uint64 values.Length))
        at <- Encoder.writeUInt bytes at (uint64 values.Length)
        let mutable index = 0
        while Cursor.isOk at && index < values.Length do
          write values[index]
          index <- index + 1
    member _.Finish() =
      if Cursor.isFault at then Result.Error CodecFailure.FrameTooLarge
      else Result.Ok (Array.sub bytes 0 at)

  type private Reader(bytes: byte array) =
    let mutable at = 0
    member _.U16() = let value, next = Decoder.readU16 bytes at in at <- next; value
    member _.U32() = let value, next = Decoder.readU32 bytes at in at <- next; value
    member _.I32() = let value, next = Decoder.readI32 bytes at in at <- next; value
    member _.I64() = let value, next = Decoder.readI64 bytes at in at <- next; value
    member _.U64() = let value, next = Decoder.readU64 bytes at in at <- next; value
    member _.Bool() = let value, next = Decoder.readBool bytes at in at <- next; value
    member _.Tag() = let value, next = Decoder.readTag bytes at in at <- next; value
    member _.Invalid<'T>() : 'T = at <- Cursor.Fault; Unchecked.defaultof<'T>
    member _.String() =
      let value, next = Decoder.readData bytes at
      at <- next
      if Cursor.isFault at then ""
      elif value.Length > MaximumPayload then at <- Cursor.Fault; ""
      else utf8.GetString value
    member this.Optional(read) =
      if this.Bool() then Some(read()) else None
    member this.Array(read: unit -> 'T) =
      let count, next = Decoder.readUInt bytes at
      at <- next
      // Every element in this schema has at least one encoded byte. Bound
      // the allocation by the actual remaining extent, before converting.
      if Cursor.isFault at || count > uint64 MaximumPayload || count > uint64 (Cursor.remaining bytes at) then
        this.Invalid<'T array>()
      else
        let values = Array.zeroCreate<'T> (int count)
        let mutable index = 0
        while Cursor.isOk at && index < values.Length do
          values[index] <- read()
          index <- index + 1
        values
    member _.Exact = Cursor.isOk at && at = bytes.Length

  let private providerTag = function ProviderIdentity.FSharp -> 0 | ProviderIdentity.ClefComposer -> 1
  let private readProvider (r: Reader) =
    match r.Tag() with
    | 0 -> ProviderIdentity.FSharp
    | 1 -> ProviderIdentity.ClefComposer
    | _ -> r.Invalid()

  let operationTag = function
    | Operation.Hello -> 0 | Operation.Open -> 1 | Operation.Reserve -> 2
    | Operation.Build -> 3 | Operation.Status -> 4 | Operation.Run -> 5
    | Operation.Cancel -> 6 | Operation.CancelRequest -> 7 | Operation.Close -> 8
    | Operation.PrepareCompilerChange -> 9 | Operation.Format -> 10

  let private readOperation (r: Reader) =
    match r.Tag() with
    | 0 -> Operation.Hello | 1 -> Operation.Open | 2 -> Operation.Reserve
    | 3 -> Operation.Build | 4 -> Operation.Status | 5 -> Operation.Run
    | 6 -> Operation.Cancel | 7 -> Operation.CancelRequest | 8 -> Operation.Close
    | 9 -> Operation.PrepareCompilerChange | 10 -> Operation.Format | _ -> r.Invalid()

  let refusalTag = function
    | RefusalCode.InvalidRequest -> 0 | RefusalCode.ProtocolVersion -> 1
    | RefusalCode.EncodingMismatch -> 2 | RefusalCode.ContractMismatch -> 3
    | RefusalCode.WrongAuthority -> 4 | RefusalCode.WrongProvider -> 5
    | RefusalCode.InvalidProject -> 6 | RefusalCode.InvalidCache -> 7
    | RefusalCode.CompilerRetired -> 8 | RefusalCode.UnknownSession -> 9
    | RefusalCode.Closed -> 10 | RefusalCode.UnsupportedOperation -> 11
    | RefusalCode.RequestRefused -> 12 | RefusalCode.DuplicateRequest -> 13
    | RefusalCode.CleanupFailed -> 14 | RefusalCode.Superseded -> 15
    | RefusalCode.CompilerRefused -> 16 | RefusalCode.Canceled -> 17
    | RefusalCode.ObservationCapacity -> 18 | RefusalCode.Busy -> 19
    | RefusalCode.SessionCapacity -> 20 | RefusalCode.BackendFailed -> 21
    | RefusalCode.InvalidReservation -> 22 | RefusalCode.NotAccepted -> 23
    | RefusalCode.FrameTooLarge -> 24 | RefusalCode.MalformedPayload -> 25
    | RefusalCode.ProviderRetiring -> 26 | RefusalCode.ProviderUnavailable -> 27
    | RefusalCode.RetirementFailed -> 28 | RefusalCode.Timeout -> 29

  let private readRefusalCode (r: Reader) =
    match r.Tag() with
    | 0 -> RefusalCode.InvalidRequest | 1 -> RefusalCode.ProtocolVersion
    | 2 -> RefusalCode.EncodingMismatch | 3 -> RefusalCode.ContractMismatch
    | 4 -> RefusalCode.WrongAuthority | 5 -> RefusalCode.WrongProvider
    | 6 -> RefusalCode.InvalidProject | 7 -> RefusalCode.InvalidCache
    | 8 -> RefusalCode.CompilerRetired | 9 -> RefusalCode.UnknownSession
    | 10 -> RefusalCode.Closed | 11 -> RefusalCode.UnsupportedOperation
    | 12 -> RefusalCode.RequestRefused | 13 -> RefusalCode.DuplicateRequest
    | 14 -> RefusalCode.CleanupFailed | 15 -> RefusalCode.Superseded
    | 16 -> RefusalCode.CompilerRefused | 17 -> RefusalCode.Canceled
    | 18 -> RefusalCode.ObservationCapacity | 19 -> RefusalCode.Busy
    | 20 -> RefusalCode.SessionCapacity | 21 -> RefusalCode.BackendFailed
    | 22 -> RefusalCode.InvalidReservation | 23 -> RefusalCode.NotAccepted
    | 24 -> RefusalCode.FrameTooLarge | 25 -> RefusalCode.MalformedPayload
    | 26 -> RefusalCode.ProviderRetiring | 27 -> RefusalCode.ProviderUnavailable
    | 28 -> RefusalCode.RetirementFailed | 29 -> RefusalCode.Timeout
    | _ -> r.Invalid()

  let private writeWorker (w: Writer) (v: WorkerAddress) =
    w.String v.Host; w.String v.Epoch; w.Tag(providerTag v.Provider)
  let private readWorker (r: Reader) : WorkerAddress =
    let host = r.String()
    let epoch = r.String()
    let provider = readProvider r
    { Host = host; Epoch = epoch; Provider = provider }
  let private writeSession (w: Writer) (v: SessionAddress) = writeWorker w v.Worker; w.String v.Session
  let private readSession (r: Reader) : SessionAddress =
    let worker = readWorker r
    let session = r.String()
    { Worker = worker; Session = session }
  let private writeAuthority (w: Writer) (v: Authority) =
    w.String v.Host; w.String v.Session; w.Tag(providerTag v.Provider); w.String v.Epoch; w.I64 v.Generation
  let private readAuthority (r: Reader) : Authority =
    let host = r.String()
    let session = r.String()
    let provider = readProvider r
    let epoch = r.String()
    let generation = r.I64()
    { Host = host; Session = session; Provider = provider; Epoch = epoch; Generation = generation }
  let private writeAgreement (w: Writer) (v: Agreement) =
    w.U16 v.ProtocolVersion
    match v.Encoding with EncodingId.BAREWire1 -> w.Tag 0
    w.String v.ContractDigest
  let private readAgreement (r: Reader) : Agreement =
    let version = r.U16()
    let encoding = match r.Tag() with 0 -> EncodingId.BAREWire1 | _ -> r.Invalid()
    let digest = r.String()
    { ProtocolVersion = version; Encoding = encoding; ContractDigest = digest }

  let requestOperation = function
    | Hello _ -> Operation.Hello | Open _ -> Operation.Open | Reserve _ -> Operation.Reserve
    | Build _ -> Operation.Build | Status _ -> Operation.Status | Run _ -> Operation.Run
    | Cancel _ -> Operation.Cancel | CancelRequest _ -> Operation.CancelRequest
    | Close _ -> Operation.Close | PrepareCompilerChange _ -> Operation.PrepareCompilerChange
    | Format _ -> Operation.Format
  let replyOperation = function
    | HelloAccepted _ -> Operation.Hello | Opened _ -> Operation.Open | Reserved _ -> Operation.Reserve
    | Built _ -> Operation.Build | Observed _ -> Operation.Status | Ran _ -> Operation.Run
    | Canceled -> Operation.Cancel | RequestCanceled _ -> Operation.CancelRequest
    | Closed _ -> Operation.Close | CompilerRetired _ -> Operation.PrepareCompilerChange
    | Formatted _ -> Operation.Format

  let private writeRequestBody (w: Writer) body =
    w.Tag(operationTag (requestOperation body))
    match body with
    | Hello value -> writeAgreement w value
    | Open(target, project) -> writeWorker w target; w.String project
    | Reserve(target, label) -> writeSession w target; w.String label
    | Build(target, reservation) -> writeSession w target; w.String reservation
    | Status target | Cancel target | Close target -> writeSession w target
    | Run(target, arguments) -> writeSession w target; w.Array(w.String, arguments)
    | CancelRequest(target, id) -> writeWorker w target; w.String id
    | PrepareCompilerChange target -> writeWorker w target
    | Format(target, generation, buffer) ->
      writeSession w target; w.I64 generation
      w.String buffer.Document; w.String buffer.Incarnation; w.U64 buffer.Revision
      w.String buffer.Source; w.String buffer.Configuration
  let private readRequestBody (r: Reader) =
    match readOperation r with
    | Operation.Hello -> Hello(readAgreement r)
    | Operation.Open -> let target = readWorker r in Open(target, r.String())
    | Operation.Reserve -> let target = readSession r in Reserve(target, r.String())
    | Operation.Build -> let target = readSession r in Build(target, r.String())
    | Operation.Status -> Status(readSession r)
    | Operation.Run -> let target = readSession r in Run(target, r.Array r.String)
    | Operation.Cancel -> Cancel(readSession r)
    | Operation.CancelRequest -> let target = readWorker r in CancelRequest(target, r.String())
    | Operation.Close -> Close(readSession r)
    | Operation.PrepareCompilerChange -> PrepareCompilerChange(readWorker r)
    | Operation.Format ->
      let target = readSession r
      let generation = r.I64()
      let document = r.String()
      let incarnation = r.String()
      let revision = r.U64()
      let source = r.String()
      let configuration = r.String()
      let buffer: FormatBuffer = {
        Document = document; Incarnation = incarnation; Revision = revision
        Source = source; Configuration = configuration
      }
      Format(target, generation, buffer)

  let private writeArtifact (w: Writer) (v: AcceptedArtifact) =
    w.I64 v.Generation; w.String v.SourceVersion; w.String v.ArtifactPath; w.String v.ArtifactSha256
    w.String v.ObjectManifest
    w.Array(w.String, v.ChangedWitnesses); w.Array(w.String, v.RetainedWitnesses); w.Array(w.String, v.RetiredWitnesses)
    w.Array((fun (key, count) -> w.String key; w.I32 count), v.WitnessVisits)
    w.Array(w.String, v.CompiledObjects); w.Array(w.String, v.ReusedObjects); w.Array(w.String, v.RetiredObjects)
  let private readArtifact (r: Reader) : AcceptedArtifact =
    let generation = r.I64()
    let source = r.String()
    let path = r.String()
    let hash = r.String()
    let manifest = r.String()
    let changed = r.Array r.String
    let retained = r.Array r.String
    let retired = r.Array r.String
    let visits = r.Array(fun () -> let key = r.String() in key, r.I32())
    let compiled = r.Array r.String
    let reused = r.Array r.String
    let retiredObjects = r.Array r.String
    { Generation = generation; SourceVersion = source; ArtifactPath = path; ArtifactSha256 = hash
      ObjectManifest = manifest; ChangedWitnesses = changed; RetainedWitnesses = retained; RetiredWitnesses = retired
      WitnessVisits = visits; CompiledObjects = compiled; ReusedObjects = reused; RetiredObjects = retiredObjects }
  let private writeStatus (w: Writer) (v: SessionSnapshot) =
    w.U64 v.Observation; w.String v.Project; w.String v.ManifestPath; w.Bool v.Closed; w.Bool v.Busy
    w.Optional(writeArtifact w, v.Current); w.Bool v.RevocationPending
    w.Optional(w.String, v.BackendError); w.Optional(w.String, v.FormatterError)
    w.Bool v.FormatterCleanupPending; w.Optional(w.String, v.WorkerRetirementRequired)
    w.Bool v.CleanupPending; w.Optional(w.String, v.CleanupError)
  let private readStatus (r: Reader) : SessionSnapshot =
    let observation = r.U64()
    let project = r.String()
    let manifest = r.String()
    let closed = r.Bool()
    let busy = r.Bool()
    let current = r.Optional(fun () -> readArtifact r)
    let revocation = r.Bool()
    let backend = r.Optional r.String
    let formatter = r.Optional r.String
    let formatterCleanup = r.Bool()
    let retirement = r.Optional r.String
    let cleanup = r.Bool()
    let cleanupError = r.Optional r.String
    { Observation = observation; Project = project; ManifestPath = manifest; Closed = closed; Busy = busy
      Current = current; RevocationPending = revocation; BackendError = backend; FormatterError = formatter
      FormatterCleanupPending = formatterCleanup; WorkerRetirementRequired = retirement
      CleanupPending = cleanup; CleanupError = cleanupError }

  let private writeReplyBody (w: Writer) body =
    w.Tag(operationTag (replyOperation body))
    match body with
    | HelloAccepted v ->
      writeAgreement w v.Agreement
      w.String v.Compiler.AssemblyPath; w.String v.Compiler.Sha256; w.String v.Compiler.Version
      w.I32 v.Psg.Schema; w.String v.Psg.AssemblySha256
      w.U32 v.Psg.FormatVersion; w.String v.Psg.ContractFingerprint
      w.Array((operationTag >> w.Tag), v.Operations); w.Bool v.InMemoryPatchAllowed
    | Opened v -> w.U64 v.Observation; w.String v.Project; w.String v.ManifestPath
    | Reserved v -> w.String v.Reservation
    | Built v -> writeArtifact w v
    | Observed v -> writeStatus w v
    | Ran v -> w.I64 v.Generation; w.String v.SourceVersion; w.I32 v.ExitCode; w.String v.StandardOutput; w.String v.StandardError
    | Canceled -> ()
    | RequestCanceled v -> w.String v.TargetRequestId; w.Bool v.CancellationRequested
    | Closed v -> w.U64 v.Observation; w.Bool v.Closed; w.Bool v.CleanupPending; w.Optional(w.String, v.CleanupError)
    | CompilerRetired v -> w.Bool v.RestartRequired; w.Bool v.InMemoryPatchAllowed
    | Formatted v ->
      w.String v.Document; w.String v.Incarnation; w.U64 v.Revision
      w.String v.SourceSha256; w.String v.Configuration; w.String v.FormatterIdentity; w.String v.Formatted
  let private readReplyBody (r: Reader) =
    match readOperation r with
    | Operation.Hello ->
      let agreed = readAgreement r
      let path = r.String()
      let hash = r.String()
      let version = r.String()
      let schema = r.I32()
      let psgHash = r.String()
      let format = r.U32()
      let fingerprint = r.String()
      let operations = r.Array(fun () -> readOperation r)
      let patch = r.Bool()
      HelloAccepted { Agreement = agreed; Compiler = { AssemblyPath = path; Sha256 = hash; Version = version }
                      Psg = { Schema = schema; AssemblySha256 = psgHash; FormatVersion = format; ContractFingerprint = fingerprint }
                      Operations = operations; InMemoryPatchAllowed = patch }
    | Operation.Open ->
      let observation = r.U64()
      let project = r.String()
      let manifest = r.String()
      Opened { Observation = observation; Project = project; ManifestPath = manifest }
    | Operation.Reserve -> Reserved { Reservation = r.String() }
    | Operation.Build -> Built(readArtifact r)
    | Operation.Status -> Observed(readStatus r)
    | Operation.Run ->
      let generation = r.I64()
      let source = r.String()
      let code = r.I32()
      let output = r.String()
      let error = r.String()
      Ran { Generation = generation; SourceVersion = source; ExitCode = code; StandardOutput = output; StandardError = error }
    | Operation.Cancel -> Canceled
    | Operation.CancelRequest ->
      let target = r.String()
      RequestCanceled { TargetRequestId = target; CancellationRequested = r.Bool() }
    | Operation.Close ->
      let observation = r.U64()
      let closed = r.Bool()
      let pending = r.Bool()
      let error = r.Optional r.String
      Closed { Observation = observation; Closed = closed; CleanupPending = pending; CleanupError = error }
    | Operation.PrepareCompilerChange ->
      let restart = r.Bool()
      CompilerRetired { RestartRequired = restart; InMemoryPatchAllowed = r.Bool() }
    | Operation.Format ->
      let document = r.String()
      let incarnation = r.String()
      let revision = r.U64()
      let source = r.String()
      let configuration = r.String()
      let formatter = r.String()
      let formatted = r.String()
      let preview: FormatPreview = {
        Document = document; Incarnation = incarnation; Revision = revision
        SourceSha256 = source; Configuration = configuration
        FormatterIdentity = formatter; Formatted = formatted
      }
      Formatted preview

  let private encode maximum version write =
    if version <> ProtocolVersion then Result.Error(CodecFailure.UnsupportedVersion version)
    else
      try
        let writer = Writer(maximum)
        writer.U16 version
        write writer
        writer.Finish()
      with
      | :? ArgumentException -> Result.Error CodecFailure.InvalidPayload
      | :? NullReferenceException -> Result.Error CodecFailure.InvalidPayload

  let private decode maximum read (bytes: byte array) =
    if isNull bytes then Result.Error CodecFailure.InvalidPayload
    elif bytes.Length > maximum then Result.Error CodecFailure.FrameTooLarge
    else
      try
        let reader = Reader bytes
        let version = reader.U16()
        if version <> ProtocolVersion then Result.Error(CodecFailure.UnsupportedVersion version)
        else
          let value = read reader
          if reader.Exact then Result.Ok value else Result.Error CodecFailure.InvalidPayload
      with
      | :? ArgumentException -> Result.Error CodecFailure.InvalidPayload
      | :? NullReferenceException -> Result.Error CodecFailure.InvalidPayload
      | :? MatchFailureException -> Result.Error CodecFailure.InvalidPayload

  let encodeRequest (request: Request) =
    encode MaximumPayload request.ProtocolVersion (fun w -> w.String request.RequestId; writeRequestBody w request.Body)
  let decodeRequest bytes =
    decode MaximumPayload (fun r ->
      let id = r.String()
      let body = readRequestBody r
      { ProtocolVersion = ProtocolVersion; RequestId = id; Body = body }) bytes
  let encodeReply (reply: Reply) =
    encode MaximumPayload reply.ProtocolVersion (fun w ->
      w.String reply.RequestId
      writeAuthority w reply.Authority
      match reply.Outcome with
      | Result.Ok body -> w.Bool false; writeReplyBody w body
      | Result.Error refusal -> w.Bool true; w.Tag(refusalTag refusal.Code); w.String refusal.Message)
  let decodeReply bytes =
    decode MaximumPayload (fun r ->
      let id = r.String()
      let authority = readAuthority r
      let outcome: Result<ReplyBody, Bozzetto.Composer.Protocol.Refusal> =
        if r.Bool() then
          let code = readRefusalCode r
          Result.Error { Code = code; Message = r.String() }
        else Result.Ok(readReplyBody r)
      { ProtocolVersion = ProtocolVersion; RequestId = id; Authority = authority; Outcome = outcome }) bytes
