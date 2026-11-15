module Bozzetto.Tests.LiveTestingGoldenFixtureTests

open System.IO
open Fidelity.Data.JSON
open Expecto
open Expecto.Flip

let private fixturePath name =
  Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "LiveTesting", name)

let private readFixture name = File.ReadAllText(fixturePath name)

let private prop name v = JsonValue.prop name v |> Option.defaultValue JsonValue.Null
let private asStr v = JsonValue.asString v |> Option.defaultValue ""
let private asInt32 v = JsonValue.asInt v |> Option.map int |> Option.defaultValue 0
let private arrLen v = match v with JsonValue.Array items -> items.Length | _ -> 0

[<Tests>]
let tests =
  testList "Live testing golden fixtures" [
    testCase "summary fixture preserves the latest fallback decision contract for downstream clients" <| fun _ ->
      let json = readFixture "summary-with-fallback-decision.json"
      let root = Json.parseOrFail json
      root |> prop "Total" |> asInt32 |> Expect.equal "total should stay stable" 5
      let decision = root |> prop "LastDecision"
      decision |> prop "Precision" |> asStr |> Expect.equal "precision should stay stable" "conservative_fallback"
      decision |> prop "Trust" |> asStr |> Expect.equal "trust should stay stable" "fresh_approximate"
      decision |> prop "Reason" |> asStr |> Expect.equal "reason should stay stable" "fallback rebuild"

    testCase "results batch fixture preserves the latest coverage-widened decision contract for downstream clients" <| fun _ ->
      let json = readFixture "results-batch-with-coverage-decision.json"
      let root = Json.parseOrFail json
      root |> prop "Entries" |> arrLen |> Expect.equal "entry count should stay stable" 2
      let decision = root |> prop "LastDecision"
      decision |> prop "Cause" |> asStr |> Expect.equal "cause should stay stable" "keystroke_buffered"
      decision |> prop "Precision" |> asStr |> Expect.equal "precision should stay stable" "coverage_approximation"
      decision |> prop "SelectedTests" |> arrLen |> Expect.equal "selected tests should stay stable" 2

    testCase "summary fixture preserves the latest suppressed-by-policy decision contract for downstream clients" <| fun _ ->
      let json = readFixture "summary-with-suppressed-decision.json"
      let root = Json.parseOrFail json
      root |> prop "Stale" |> asInt32 |> Expect.equal "stale count should stay stable" 2
      let decision = root |> prop "LastDecision"
      decision |> prop "Precision" |> asStr |> Expect.equal "precision should stay stable" "suppressed_by_policy"
      decision |> prop "Trust" |> asStr |> Expect.equal "trust should stay stable" "suppressed"
      decision |> prop "DeferredTests" |> arrLen |> Expect.equal "deferred test count should stay stable" 1
  ]
