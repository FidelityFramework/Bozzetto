/// Backend adapter for the single shared typed binary codec.
module Bozzetto.Server.UiBridgeCodec

open Bozzetto.Web.Shared.Protocol

let noTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }
let frameRefusal correlation code message =
  { CommandRefusal.Correlation = correlation; Target = noTarget; Code = code; Message = message }
let decodeCommand bytes =
  Bozzetto.Web.Shared.BinaryCodec.decodeCommand bytes
  |> Result.mapError (fun reason ->
    let code =
      if reason.Contains("unsupported version", System.StringComparison.OrdinalIgnoreCase) then
        "protocol_mismatch"
      else
        "invalid_request"
    frameRefusal 0 code reason)
let encodeEvent = Bozzetto.Web.Shared.BinaryCodec.encodeEvent
