namespace Bozzetto.Composer.Protocol

open System
open System.IO
open System.Threading
open BAREWire.Encoding
open BAREWire.Framing

/// Framing only. The transport owner serializes writers and joins admitted IO;
/// these functions do not transfer cancellation or process-lifetime ownership.
module StreamFrames =
  let private frame kind payload =
    Envelope.encodeStream { Kind = kind; Correlation = 0u; Payload = payload }

  let encodeRequest request = BAREWireCodec.encodeRequest request |> Result.map (frame FrameKind.Ask)
  let encodeReply reply = BAREWireCodec.encodeReply reply |> Result.map (frame FrameKind.Reply)

  /// Clean EOF is represented explicitly by None.
  /// Check the announced extent before allocation. Do not rely on Envelope's
  /// generic MaxStreamBody, which is deliberately much larger than this profile.
  let readAsync expectedKind (input: Stream) (cancellation: CancellationToken) = task {
    let prefix = Array.zeroCreate<byte> Envelope.LengthPrefixSize
    let mutable read = 0
    let mutable ended = false
    while read < prefix.Length && not ended do
      let! count = input.ReadAsync(prefix.AsMemory(read, prefix.Length - read), cancellation)
      if count = 0 then ended <- true else read <- read + count
    if ended then
      if read = 0 then return Result.Ok None
      else return Result.Error FrameFailure.TruncatedPrefix
    else
      let length, next = Decoder.readU32 prefix 0
      if Cursor.isFault next || length < uint32 Envelope.HeaderSize || length > uint32 BAREWireCodec.MaximumBody then
        return Result.Error(FrameFailure.InvalidLength length)
      else
        let body = Array.zeroCreate<byte> (int length)
        read <- 0
        while read < body.Length && not ended do
          let! count = input.ReadAsync(body.AsMemory(read, body.Length - read), cancellation)
          if count = 0 then ended <- true else read <- read + count
        if ended then return Result.Error FrameFailure.TruncatedBody
        else
          let decoded, at = Envelope.decodeMessage body
          if Cursor.isFault at then return Result.Error FrameFailure.InvalidPayload
          elif decoded.Kind <> expectedKind then return Result.Error(FrameFailure.UnexpectedFrameKind decoded.Kind)
          elif decoded.Correlation <> 0u then return Result.Error(FrameFailure.InvalidCorrelation decoded.Correlation)
          else return Result.Ok(Some decoded.Payload)
  }
