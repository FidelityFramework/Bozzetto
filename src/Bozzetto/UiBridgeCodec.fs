/// Backend adapter for the single shared typed binary codec.
module Bozzetto.Server.UiBridgeCodec

open Bozzetto.Web.Shared.Protocol

let noTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }
let frameRefusal correlation code message =
  { CommandRefusal.Correlation = correlation; Target = noTarget; Code = code; Message = message }
let decodeCommand bytes =
  Bozzetto.Web.Shared.BinaryCodec.decodeCommand bytes
  |> Result.mapError (frameRefusal 0 "invalid_request")
let encodeEvent = Bozzetto.Web.Shared.BinaryCodec.encodeEvent
