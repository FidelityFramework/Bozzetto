module Bozzetto.Host.Program

/// Retained executable entry point for callers of the retired F# worker.
[<EntryPoint>]
let main _args =
  eprintfn "%s" Bozzetto.ExternalFSharpService.message
  2
