module TestWorkspace.Program

open Expecto

[<EntryPoint>]
let main args =
  runTestsWithCLIArgs [] args TestWorkspace.SampleTests.sampleTests
