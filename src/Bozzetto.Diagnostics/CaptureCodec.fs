namespace Bozzetto.Diagnostics

open System
open System.Text
open System.Security.Cryptography
open BAREWire.Encoding
open Fidelity.PSG

/// A bounded BAREWire envelope around the occurrence-only PSG codec. Framing
/// belongs to the transport; a partial envelope is never a completed capture.
module CaptureCodec =
  [<Literal>]
  let MaximumBody = 33554432
  [<Literal>]
  let Schema = 1u
  let private magic = [| 66uy; 79uy; 90uy; 68uy; 73uy; 65uy; 71uy; 49uy |] // BOZDIAG1
  let private responseMagic = [| 66uy; 79uy; 90uy; 65uy; 67uy; 75uy; 48uy; 49uy |] // BOZACK01
  let private utf8 = UTF8Encoding(false, true)
  /// Bound one refusal before writing a receipt or logging it at a host edge.
  let boundDiagnostic (message: string) =
    let marker = "\n[diagnostic refusal truncated]"
    if isNull message then "Diagnostic refusal did not include a reason."
    else
      let maximum = 8192 - marker.Length
      let text = StringBuilder(min message.Length 8192)
      let mutable at = 0
      let mutable stopped = false
      while at < message.Length && text.Length < maximum && not stopped do
        let current = message[at]
        if current >= '\uD800' && current <= '\uDBFF' then
          if at + 1 < message.Length && message[at + 1] >= '\uDC00' && message[at + 1] <= '\uDFFF' then
            if text.Length + 2 > maximum then stopped <- true
            else
              text.Append(current).Append(message[at + 1]) |> ignore
              at <- at + 2
          else
            text.Append('\uFFFD') |> ignore
            at <- at + 1
        elif current >= '\uDC00' && current <= '\uDFFF' then
          text.Append('\uFFFD') |> ignore
          at <- at + 1
        else
          text.Append current |> ignore
          at <- at + 1
      if at < message.Length then text.Append marker |> ignore
      text.ToString()
  let private occurrenceError context error =
    let message =
      match error with
      | BinaryError.InvalidOccurrenceDelivery findings ->
        sprintf "%s (first findings): %A" context (List.truncate 4 findings)
      | BinaryError.InvalidRevision findings ->
        sprintf "%s (first findings): %A" context (List.truncate 4 findings)
      | other -> sprintf "%s: %A" context other
    boundDiagnostic message
  [<Literal>]
  let private ChecksumBytes = 32
  let private addChecksum (bytes: byte array) = Array.append bytes (SHA256.HashData bytes)
  let private verifyChecksum (bytes: byte array) =
    if isNull bytes || bytes.Length <= ChecksumBytes || bytes.Length > MaximumBody then
      Error "Diagnostic envelope length is invalid."
    else
      let payload = bytes[..bytes.Length - ChecksumBytes - 1]
      let checksum = bytes[bytes.Length - ChecksumBytes..]
      if SHA256.HashData payload <> checksum then Error "Diagnostic envelope content checksum does not match."
      else Ok payload
  let private limits : BinaryLimits =
    { MaxBytes = MaximumBody; MaxCollectionLength = 65536; MaxDepth = 128
      MaxStringBytes = 1048576; MaxBigIntegerBytes = 65536; MaxValues = 1000000 }

  type private Writer() =
    let mutable bytes = Array.zeroCreate<byte> 4096
    let mutable at = 0
    let ensure count =
      if Cursor.isOk at then
        if count < 0 || count > MaximumBody - ChecksumBytes - at then at <- Cursor.Fault
        elif at + count > bytes.Length then
          Array.Resize(&bytes, min MaximumBody (max (at + count) (bytes.Length * 2)))
    member _.U32 value = ensure 4; at <- Encoder.writeU32 bytes at value
    member _.I32 value = ensure 4; at <- Encoder.writeI32 bytes at value
    member _.U64 value = ensure 8; at <- Encoder.writeU64 bytes at value
    member _.I64 value = ensure 8; at <- Encoder.writeI64 bytes at value
    member _.Bool value = ensure 1; at <- Encoder.writeBool bytes at value
    member _.Data (value: byte array) =
      ensure (10 + value.Length)
      if Cursor.isOk at then at <- Encoder.writeData bytes at value
    member this.String (value: string) =
      if isNull value || utf8.GetByteCount value > 65536 then at <- Cursor.Fault
      elif Cursor.isOk at then this.Data(utf8.GetBytes value)
    member this.List(write, values: 'a list) =
      if values.Length > 65536 then at <- Cursor.Fault
      else
        this.U32(uint32 values.Length)
        for value in values do if Cursor.isOk at then write value
    member _.Finish() =
      if Cursor.isFault at then Error "Diagnostic envelope exceeds its byte or collection limit."
      else Ok (Array.sub bytes 0 at |> addChecksum)

  type private Reader(bytes: byte array) =
    let mutable at = 0
    member _.U32() = let value, next = Decoder.readU32 bytes at in at <- next; value
    member _.I32() = let value, next = Decoder.readI32 bytes at in at <- next; value
    member _.U64() = let value, next = Decoder.readU64 bytes at in at <- next; value
    member _.I64() = let value, next = Decoder.readI64 bytes at in at <- next; value
    member _.Bool() = let value, next = Decoder.readBool bytes at in at <- next; value
    member _.Data() = let value, next = Decoder.readData bytes at in at <- next; value
    member this.String() =
      let value = this.Data()
      if Cursor.isFault at then ""
      elif value.Length > 65536 then at <- Cursor.Fault; ""
      else utf8.GetString value
    member this.List(read: unit -> 'a) =
      let count = this.U32()
      if Cursor.isFault at || count > 65536u || uint64 count > uint64 (Cursor.remaining bytes at) then
        at <- Cursor.Fault
        []
      else
        let values = ResizeArray<'a>()
        let mutable remaining = count
        while Cursor.isOk at && remaining > 0u do
          values.Add(read())
          remaining <- remaining - 1u
        List.ofSeq values
    member _.Finished = Cursor.isOk at && at = bytes.Length

  let private writeNode (writer: Writer) node = writer.I32(NodeId.value node)
  let private readNode (reader: Reader) = NodeId(reader.I32())
  let private writeScope (writer: Writer) (stamp: ScopeContentStamp) =
    writer.String stamp.Identity
    writer.U64 stamp.Version
  let private readScope (reader: Reader) : ScopeContentStamp =
    { Identity = reader.String(); Version = reader.U64() }
  let private writeHeader (writer: Writer) (header: SourceContextHeader) =
    writeNode writer header.Identity
    writer.String header.Name
    writer.List((fun (kind, (port: SourcePortAccount)) ->
      writer.U32(match kind with OccurrencePort.StructuralChild -> 0u | OccurrencePort.ModuleDeclaration -> 1u)
      writer.I32 port.Extent
      writer.String port.Stamp
      writer.List((fun (ordinal, node) -> writer.I32 ordinal; writeNode writer node), Map.toList port.Positions)), Map.toList header.Ports)
  let private readHeader (reader: Reader) =
    let identity = readNode reader
    let name = reader.String()
    let ports = reader.List(fun () ->
      let kind =
        match reader.U32() with
        | 0u -> OccurrencePort.StructuralChild
        | 1u -> OccurrencePort.ModuleDeclaration
        | _ -> invalidOp "Unknown occurrence port tag."
      let extent = reader.I32()
      let stamp = reader.String()
      let positions = reader.List(fun () -> let ordinal = reader.I32() in ordinal, readNode reader)
      let mapped = Map.ofList positions
      if mapped.Count <> positions.Length then invalidOp "Repeated declaration port ordinal."
      kind, { Extent = extent; Stamp = stamp; Positions = mapped })
    let mapped = Map.ofList ports
    if mapped.Count <> ports.Length then invalidOp "Repeated declaration port kind."
    { Identity = identity; Name = name; Ports = mapped }

  let encode (capture: OccurrenceCapture) : Result<byte array, string> =
    Capture.validate capture |> Result.bind (fun () ->
      OccurrenceBinary.encode limits capture.Delivery
      |> Result.mapError (occurrenceError "Occurrence encoding refused")
      |> Result.bind (fun image ->
        try
          let writer = Writer()
          writer.Data magic
          writer.U32 Schema
          writer.String Capture.Stage
          writer.String capture.Metadata.Demand
          writer.String capture.Metadata.SourceVersion
          writer.List(writeScope writer, capture.Metadata.ExpectedScopes)
          writer.List(writeHeader writer, capture.ContextHeaders)
          writer.List(writeNode writer, capture.ContextRoots)
          writer.Data image
          writer.Finish()
        with error -> Error (boundDiagnostic ("Diagnostic encoding refused: " + error.Message))))

  let decode (bytes: byte array) : Result<OccurrenceCapture, string> =
    verifyChecksum bytes |> Result.bind (fun bytes ->
      try
        let reader = Reader bytes
        if reader.Data() <> magic then Error "Unknown diagnostic envelope magic."
        elif reader.U32() <> Schema then Error "Unsupported diagnostic envelope schema."
        elif reader.String() <> Capture.Stage then Error "Unsupported diagnostic capture stage."
        else
          let metadata =
            { Demand = reader.String(); SourceVersion = reader.String(); ExpectedScopes = reader.List(fun () -> readScope reader) }
          let headers = reader.List(fun () -> readHeader reader)
          let roots = reader.List(fun () -> readNode reader)
          let image = reader.Data()
          if not reader.Finished then Error "Diagnostic envelope is truncated, malformed or has trailing bytes."
          else
            OccurrenceBinary.decode limits image
            |> Result.mapError (occurrenceError "Occurrence decoding refused")
            |> Result.bind (fun delivery ->
              let capture = { Metadata = metadata; Delivery = delivery; ContextHeaders = headers; ContextRoots = roots }
              Capture.validate capture |> Result.map (fun () -> capture))
      with error -> Error (boundDiagnostic ("Diagnostic decoding refused: " + error.Message)))

  let encodeResponse (response: Result<CaptureReceipt, string>) : Result<byte array, string> =
    try
      let writer = Writer()
      writer.Data responseMagic
      writer.U32 Schema
      match response with
      | Ok receipt ->
        writer.Bool true
        writer.String receipt.Digest
        writer.I64 receipt.PayloadBytes
        writer.String receipt.Stage
      | Error reason -> writer.Bool false; writer.String (boundDiagnostic reason)
      writer.Finish()
    with error -> Error (boundDiagnostic ("Diagnostic response encoding refused: " + error.Message))

  let decodeResponse (bytes: byte array) : Result<Result<CaptureReceipt, string>, string> =
    verifyChecksum bytes |> Result.bind (fun bytes ->
      try
        let reader = Reader bytes
        if reader.Data() <> responseMagic then Error "Unknown diagnostic response magic."
        elif reader.U32() <> Schema then Error "Unsupported diagnostic response schema."
        else
          let response =
            if reader.Bool() then
              Ok { Digest = reader.String(); PayloadBytes = reader.I64(); Stage = reader.String() }
            else Error (reader.String() |> boundDiagnostic)
          if reader.Finished then Ok response else Error "Diagnostic response is truncated, malformed or has trailing bytes."
      with error -> Error (boundDiagnostic ("Diagnostic response decoding refused: " + error.Message)))

  /// Client convenience: both malformed replies and explicit refusal are errors.
  let decodeReply bytes = decodeResponse bytes |> Result.bind id
