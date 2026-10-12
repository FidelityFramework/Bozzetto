/// Mock adapter uses the identical CLR/Fable binary owner.
module Bozzetto.Web.Mock.Codec

open Bozzetto.Web.Shared.Protocol

let noTarget = { SessionTarget.Worker = { WorkerTarget.Host = ""; Epoch = "" }; Session = "" }
let decodeCommand bytes = Bozzetto.Web.Shared.BinaryCodec.decodeCommand bytes
let encodeEvent = Bozzetto.Web.Shared.BinaryCodec.encodeEvent
