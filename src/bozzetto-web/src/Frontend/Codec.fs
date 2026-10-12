/// Browser adapter for the shared binary wire owner.
module Bozzetto.Web.Frontend.Codec

let encodeCommand = Bozzetto.Web.Shared.BinaryCodec.encodeCommand
let decodeEvent = Bozzetto.Web.Shared.BinaryCodec.decodeEvent
