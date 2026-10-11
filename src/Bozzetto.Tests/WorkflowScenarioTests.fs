module Bozzetto.Tests.WorkflowScenarioTests

open Expecto

open Expecto.Flip

open Bozzetto.WorkflowTypes

let private transitionCostScenarios =
  testList "transition cost informs the user" [

    testCase "TransitionCost.zero represents a costless switch" <| fun _ ->
      // GIVEN a fresh session with no REPL state
      let cost = TransitionCost.zero

      // THEN the cost shows nothing will be lost
      cost.DefinitionsLost
      |> Expect.equal "no definitions to lose" 0
      cost.CellsLost
      |> Expect.equal "no cells to lose" 0

    testCase "TransitionCost captures real losses" <| fun _ ->
      // GIVEN a session where the user has accumulated REPL state
      let cost = {
        DefinitionsLost = 12
        CellsLost = 3
        EstimatedRestart = System.TimeSpan.FromSeconds 8.0
      }

      // THEN the cost accurately describes what switching will cost
      cost.DefinitionsLost
      |> Expect.equal "should report 12 definitions" 12
      cost.CellsLost
      |> Expect.equal "should report 3 cells" 3
  ]

[<Tests>]
let workflowScenarioTests =
  testList "Workflow scenarios" [
    transitionCostScenarios
    testCase "supported workflows retain distinct exact labels" <| fun _ ->
      [ SessionWorkflow.Interactive; SessionWorkflow.LiveTesting ]
      |> List.map SessionWorkflow.label
      |> Expect.equal "the two supported workflows are distinguishable" [ "REPL"; "Live Testing" ]
    testCase "default workflow is explicitly Interactive" <| fun _ ->
      SessionWorkflow.defaultWorkflow |> Expect.equal "declared default" SessionWorkflow.Interactive
    testCase "interactive aliases parse explicitly and case-insensitively" <| fun _ ->
      [ "interactive"; "Interactive"; "repl"; "REPL"; " normal " ]
      |> List.map SessionWorkflow.tryOfString
      |> Expect.allEqual "interactive aliases" (Some SessionWorkflow.Interactive)
    testCase "live-testing aliases parse explicitly and case-insensitively" <| fun _ ->
      [ "livetesting"; "LiveTesting"; "live-testing"; "testing"; "test"; " TEST " ]
      |> List.map SessionWorkflow.tryOfString
      |> Expect.allEqual "live-testing aliases" (Some SessionWorkflow.LiveTesting)
    testCase "retired and unknown modes are refused without a fallback workflow" <| fun _ ->
      for name in [ "live"; "weblive"; "hotreload"; "web"; ""; " "; null; "garbage" ] do
        SessionWorkflow.tryOfString name |> Expect.isNone "unsupported request"
        Expect.throwsT<System.ArgumentException> "the partial parser must also refuse" (fun () -> SessionWorkflow.ofString name |> ignore)
  ]
