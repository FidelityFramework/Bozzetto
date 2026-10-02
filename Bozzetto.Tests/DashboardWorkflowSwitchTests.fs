/// Picker values are external browser input; workflow dispatch is a typed owner call.
module Bozzetto.Tests.DashboardWorkflowSwitchTests

open Expecto
open Expecto.Flip
open Bozzetto.WorkflowTypes
open Bozzetto.Server.DashboardTypes

[<Tests>]
let optionsTests =
  testList "WorkflowSwitch.options" [
    testCase "WHY — the picker offers exactly the two supported workflows, in a stable order" <| fun _ ->
      WorkflowSwitch.options
      |> List.map SessionWorkflow.label
      |> Expect.equal "Interactive, LiveTesting — in that order" [ "REPL"; "Live Testing" ]
  ]
[<Tests>]
let requestValueTests =
  testList "WorkflowSwitch.requestValue" [
    testCase "WHY — every option's wire value round-trips through the SAME parser the real endpoint uses (SessionWorkflow.tryOfString), so the dashboard can never send a value the server rejects" <| fun _ ->
      for w in WorkflowSwitch.options do
        let value = WorkflowSwitch.requestValue w
        match SessionWorkflow.tryOfString value with
        | None -> failtestf "requestValue %s ('%s') does not round-trip through tryOfString" (SessionWorkflow.label w) value
        | Some parsed ->
          parsed
          |> SessionWorkflow.label
          |> Expect.equal (sprintf "'%s' should round-trip to the same label" value) (SessionWorkflow.label w)

    testCase "WHY — a deliberately-wrong wire value would be caught by the round-trip check above (mutation-proofing the table)" <| fun _ ->
      // A broken requestValue that always returned "interactive" would still
      // "round trip" for the Interactive case but would fail LiveTesting —
      // this check documents that the values are
      // pairwise distinct, so no case can silently alias another.
      WorkflowSwitch.options
      |> List.map WorkflowSwitch.requestValue
      |> List.distinct
      |> List.length
      |> Expect.equal "two distinct wire values for two supported workflows" 2
  ]
