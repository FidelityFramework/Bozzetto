module Bozzetto.Samples.ConsoleTicker.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
  runTestsWithCLIArgs [] argv Bozzetto.Samples.ConsoleTicker.Tests.TickerTests.tests
