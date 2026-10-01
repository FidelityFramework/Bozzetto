/// The product boundary for F# execution formerly hosted inside Bozzetto.
module Bozzetto.ExternalFSharpService

let message =
  "Bozzetto no longer hosts F# sessions or evaluates F# configuration. Use a separate SageFS daemon for F# work (MCP http://localhost:37749/; dashboard http://localhost:37750/dashboard). Use Composer for Clef projects; Clefx interactive execution is not implemented yet."

let refuse<'T> () : Result<'T, Bozzetto.BozzettoError> =
  Result.Error (Bozzetto.BozzettoError.SessionCreationFailed message)
