/// Proves the Wave-1 domain model actually models the plan (demo-gif-plan.md
/// §5, §6, §6.1): the worked hot-reload scenario constructs, the derived ids
/// match the matrix naming, the caption smart constructor is total, and the
/// matrix comprehension shape from §6 type-checks. None of these tests call
/// a planner stub (`Motion`, `Cadence`, …) — those are Wave 2.
module Bozzetto.Demos.Tests.DomainTests

open Expecto
open Expecto.Flip
open Bozzetto.Demos.Domain

/// `hr-vscode-console`: edit the console ticker's message in VS Code, save,
/// watch the running ticker's next line change. The plan's §6.1 worked
/// example had the same five-step shape against the since-removed Datastar
/// web sample (its "Run" click landed on the retired web dashboard); this is
/// that example repointed to a sample and a click target that still exist.
let hrVsCodeConsole : Scenario =
  { Id = ScenarioId.derive Capability.HotReload Client.VsCode AppKind.Console
    Capability = Capability.HotReload
    Client = Client.VsCode
    App = AppKind.Console
    Sample = Sample.ConsoleTicker
    Layout = LayoutTemplate.EditorLeft
    Cost = CostClass.console
    Masks = [ Region.clock ]
    Steps =
      [ { Caption = Caption.mk "Open the ticker"
          Action = Action.Setup (ClientCommand.OpenFile SampleFile.ticker)
          Expect = Expectation.EditorSaved SampleFile.ticker
          Dwell = Dwell.short }
        { Caption = Caption.mk "1/4 · Run the app from the command palette"
          Action = Action.Click (Target.PaletteItem VsCodeCommand.BozzettoRunApp)
          Expect = Expectation.AppState AppRunStateCase.Running
          Dwell = Dwell.medium }
        { Caption = Caption.mk "2/4 · Change the message"
          Action =
            Action.Type (
              Target.EditorPosition (SampleFile.ticker, 24, 17),
              Text.mk "Bozzetto is live",
              CadenceSeed.ofId "hr-vscode-console"
            )
          Expect = Expectation.EditorSaved SampleFile.ticker
          Dwell = Dwell.short }
        { Caption = Caption.mk "3/4 · Save"
          Action = Action.Chord [ Key.Ctrl; Key.S ]
          Expect = Expectation.EditorSaved SampleFile.ticker
          Dwell = Dwell.short }
        { Caption = Caption.mk "4/4 · The running ticker updates — no restart"
          Action = Action.Await Signal.appOutputChanged
          Expect = Expectation.AppOutputChanged Region.appHeading
          Dwell = Dwell.long } ] }

[<Tests>]
let tests =
  testList "Domain" [

    testCase "the worked hero scenario constructs with 5 steps" <| fun _ ->
      hrVsCodeConsole.Steps.Length |> Expect.equal "should have 5 steps" 5

    testCase "ScenarioId.derive matches the worked example" <| fun _ ->
      ScenarioId.derive Capability.HotReload Client.VsCode AppKind.Console
      |> ScenarioId.value
      |> Expect.equal "hr-vscode-console is derived from HotReload/VsCode/Console" "hr-vscode-console"

    testCase "ScenarioId.derive matches every matrix id shape" <| fun _ ->
      let cases =
        [ (Capability.HotReload, Client.VsCode, AppKind.Raylib), "hr-vscode-raylib"
          (Capability.HotReload, Client.Neovim, AppKind.Raylib), "hr-neovim-raylib"
          (Capability.HotReload, Client.VsCode, AppKind.Console), "hr-vscode-console"
          (Capability.HotReload, Client.Neovim, AppKind.NoApp), "hr-neovim"
          (Capability.LiveTesting, Client.VsCode, AppKind.NoApp), "lt-vscode"
          (Capability.Repl, Client.Neovim, AppKind.NoApp), "repl-neovim"
          (Capability.Sessions, Client.VsCode, AppKind.Console), "sessions-vscode" ]
      for (capability, client, appKind), expected in cases do
        ScenarioId.derive capability client appKind
        |> ScenarioId.value
        |> Expect.equal (sprintf "%A/%A/%A should derive %s" capability client appKind expected) expected

    testCase "Caption.mk is total: a caption at the 70-char limit is unchanged" <| fun _ ->
      let exactly70 = String.replicate 70 "x"
      exactly70
      |> Caption.mk
      |> Caption.value
      |> Expect.equal "70-char caption round-trips unchanged" exactly70

    testCase "Caption.mk truncates a caption over the 70-char limit (§9)" <| fun _ ->
      let tooLong = String.replicate 100 "x"
      let result = tooLong |> Caption.mk |> Caption.value
      result.Length |> Expect.equal "truncated caption is exactly 70 chars" 70
      result |> Expect.equal "truncated caption is the first 70 chars of the input" (tooLong.Substring(0, 70))

    testCase "the §6 matrix comprehension shape type-checks over Client.all × Sample.runnable" <| fun _ ->
      // `Client.Agent` (`Scenario.Client` is a required field, and
      // `agent-mcp` is filmed through no editor at all) is deliberately NOT
      // in `Client.all` — that list drives the hot-reload comprehension
      // below, and the Agent actor authors no hot-reload scenarios
      // (demo-actors-plan.md §2.4). `Client.all` is exactly the editors.
      Client.all |> Expect.equal "two hot-reload-eligible editor clients" [ Client.VsCode; Client.Neovim ]
      Sample.runnable
      |> Expect.equal
        "two runnable samples (FromCSharp is a test project)"
        [ Sample.RaylibGame; Sample.ConsoleTicker ]
      let hotReloadPairs = [ for client in Client.all do for sample in Sample.runnable -> client, sample ]
      hotReloadPairs.Length
      |> Expect.equal "2 clients × 2 runnable samples = 4 hot-reload scenarios" 4

    testCase "every hot-reload scenario registered for record is one of the Client.all × Sample.runnable pairs, each at most once" <| fun _ ->
      let hotReload =
        Bozzetto.Demos.Scenarios.All.all
        |> List.filter (fun s -> s.Capability = Capability.HotReload)
      let pairs = hotReload |> List.map (fun s -> s.Client, s.Sample)
      let matrix = [ for client in Client.all do for sample in Sample.runnable -> client, sample ]
      for pair in pairs do
        matrix |> List.contains pair |> Expect.isTrue (sprintf "%A is a matrix pair" pair)
      pairs |> List.distinct |> List.length |> Expect.equal "no matrix pair registered twice" pairs.Length
      hotReload
      |> List.map (fun s -> ScenarioId.value s.Id)
      |> List.filter (fun id -> id.Contains "dashboard")
      |> Expect.isEmpty "no registered id still names the removed dashboard narrator"
  ]
