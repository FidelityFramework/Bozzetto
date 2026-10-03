/// Proves the one JSON contract that crosses the sandbox namespace wall
/// (demo-gif-plan.md §4.1): `ScenarioPlan`/`StepLog` round-trip through
/// `serialize*`/`deserialize*` unchanged — the exact mechanism the Phase-0
/// spike proved end to end, exercised here without ever spawning a cell.
module Bozzetto.Demos.Tests.WireTests

open Expecto
open Expecto.Flip
open Bozzetto.Demos.Wire

let private samplePlan: ScenarioPlan =
  { ScenarioId = "hr-vscode-console"
    ChromePath = "/chrome-bin/chrome"
    UserDataDir = "/home/demo/chrome-profile"
    OutDir = "/out"
    Steps =
      [ { Index = 0
          Caption = "1/1 · Change the message"
          PreClickSelector = None
          ClickSelector = Some "editor"
          TypeText = Some "let message = \"hot-reloaded live\""
          SubmitSelector = None
          ExpectSelector = Some "app-output-changed"
          DwellMs = 1500
          TargetActor = None
          ChordKeys = None
          SetupCommand = None
          ObserveActor = Some "app" } ]
    Client = "vscode"
    VsCode = Some { ExtensionDevPath = Some "/vscode-ext" }
    Nvim = None
    App = Some { Kind = Some "console" }
    ActorRects = [ { ActorToken = "vscode"; X = 0; Y = 0; W = 704; H = 720 }; { ActorToken = "app"; X = 712; Y = 0; W = 568; H = 720 } ]
    WorkspaceDir = Some "/repo/samples/demos/Bozzetto.Samples.ConsoleTicker"
    NvimOpenFilePath = None }

let private sampleStepLog: StepLog =
  { ScenarioId = "hr-vscode-console"
    Steps =
      [ { Index = 0
          Caption = "1/1 · Change the message"
          Segment = "/out/step-00.mkv"
          StartedMs = 0L
          EndedMs = 2140L
          PointerPath = [ [| 620; 300 |]; [| 630; 298 |] ]
          ObservedAtMs = 1980L
          Outcome = "Passed"
          Message = "'app-output-changed' appeared" } ] }

[<Tests>]
let tests =
  testList "Wire" [

    testCase "ScenarioPlan round-trips through serializePlan/deserializePlan unchanged" <| fun _ ->
      samplePlan |> serializePlan |> deserializePlan |> Expect.equal "round-tripped plan equals the original" samplePlan

    testCase "StepLog round-trips through serializeStepLog/deserializeStepLog unchanged" <| fun _ ->
      sampleStepLog
      |> serializeStepLog
      |> deserializeStepLog
      |> Expect.equal "round-tripped StepLog equals the original" sampleStepLog

    testCase "serializePlan produces one JSON line (the stdio pipe carries exactly one line, §4.1)" <| fun _ ->
      samplePlan |> serializePlan |> (fun s -> s.Contains "\n") |> Expect.isFalse "no embedded newline"

    testCase "a ScenarioPlan step with no click/type/expect (an Await step) still round-trips" <| fun _ ->
      let awaitOnly =
        { samplePlan with
            Steps =
              [ { Index = 0
                  Caption = "wait"
                  PreClickSelector = None
                  ClickSelector = None
                  TypeText = None
                  SubmitSelector = None
                  ExpectSelector = None
                  DwellMs = 500
                  TargetActor = None
                  ChordKeys = None
                  SetupCommand = None
                  ObserveActor = None } ] }

      awaitOnly |> serializePlan |> deserializePlan |> Expect.equal "round-trips with every optional field None" awaitOnly

    testCase "a TypeThenClick step's SubmitSelector round-trips (the chained 'type, then press submit' beat)" <| fun _ ->
      let typeThenClick =
        { samplePlan with
            Steps =
              [ { Index = 0
                  Caption = "3/3 · Type and submit"
                  PreClickSelector = None
                  ClickSelector = Some "commandline"
                  TypeText = Some ":w"
                  SubmitSelector = Some "window-center"
                  ExpectSelector = Some "saved"
                  DwellMs = 2000
                  TargetActor = None
                  ChordKeys = None
                  SetupCommand = None
                  ObserveActor = None } ] }

      typeThenClick |> serializePlan |> deserializePlan |> Expect.equal "round-trips with SubmitSelector populated" typeThenClick

    testCase "a ClickThenTypeThenClick step's PreClickSelector round-trips (the 'expand a panel, then type, then press submit' beat)" <| fun _ ->
      let clickThenTypeThenClick =
        { samplePlan with
            Steps =
              [ { Index = 0
                  Caption = "3/3 · Expand, type and submit"
                  PreClickSelector = Some "statusline"
                  ClickSelector = Some "commandline"
                  TypeText = Some ":w"
                  SubmitSelector = Some "window-center"
                  ExpectSelector = Some "saved"
                  DwellMs = 2000
                  TargetActor = None
                  ChordKeys = None
                  SetupCommand = None
                  ObserveActor = None } ] }

      clickThenTypeThenClick
      |> serializePlan
      |> deserializePlan
      |> Expect.equal "round-trips with PreClickSelector populated" clickThenTypeThenClick
  ]
