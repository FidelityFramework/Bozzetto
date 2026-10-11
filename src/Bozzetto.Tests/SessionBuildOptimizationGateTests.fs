/// Controlled restarts preserve the project's own compiler optimization settings.
module Bozzetto.Tests.SessionBuildOptimizationGateTests

open Expecto
open Expecto.Flip
open Bozzetto

[<Tests>]
let fastGateTests =
  testList "SessionBuild preserves compiler settings" [
    testCase "fast rebuild preserves project options and skips restore" <| fun _ ->
      SessionBuild.buildArguments false "/src/Web/Web.fsproj"
      |> Expect.equal "no optimization override" [ "build"; "/src/Web/Web.fsproj"; "--no-restore" ]

    testCase "restore rebuild preserves project options" <| fun _ ->
      SessionBuild.buildArguments true "/src/Web/Web.fsproj"
      |> Expect.equal "no optimization override" [ "build"; "/src/Web/Web.fsproj" ]
  ]
