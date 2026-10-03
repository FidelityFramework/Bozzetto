/// The VS Code client's scenarios (demo-actors-plan.md §1.3/§2.1): the
/// hot-reload scenarios `hr-vscode-raylib` and `hr-vscode-console`, joint
/// with the App co-actor.
///
/// INTEGRATION STATUS (read before wiring `record` against these):
/// `Runtime.fs`'s `vsCodeTargetSelector` flattens `Target.EditorPosition`/
/// `WindowCenter ActorId.VsCode` for real (routed through the extension
/// host's `bozzetto.debug.rectFor` — `Actors/VsCode.fs`), so every
/// `Type`/`Chord` step below drives a genuine click/type. Each beat is
/// observed through the actor that can genuinely prove it: session readiness
/// through the VS Code actor's own daemon `/health` poll
/// (`Expectation.SessionReady`), the running app and its changed output
/// through the App co-actor. `Expectation.EditorSaved` has no VS Code
/// observation channel yet (`Runtime.fs`'s `expectationWire`), so that beat
/// records as Skipped (honestly unverified), never as a pass.
///
/// The live-testing (`lt-vscode`), REPL (`repl-vscode`) and web hot-reload
/// scenarios were removed with the web dashboard: they proved their story
/// only through the dashboard narrator pane (its output panel, session badge
/// and live-testing panel) or drove the Datastar web sample.
module Bozzetto.Demos.Scenarios.VsCode

open Bozzetto.Demos.Domain

/// The `hr-vscode-*` hot-reload scenarios (§6's matrix; `-console` is a
/// hero) — JOINT with the App co-actor (`Actors/App.fs`/`Runtime.App.fs`,
/// `CellAgent.fs`'s lazy launch right after a real `"run-app"` dispatch).
/// Each creates a real session and waits until it is ready, runs the app,
/// edits a real, already-documented hot-reload knob in the sample's own
/// source (`starMinSpeed`/`starMaxSpeed` for Raylib, the message knob for
/// Console) at a real `EditorPosition`, saves with the real `Ctrl+S` chord
/// (`Key` module's own worked example), and awaits the daemon's real
/// `AppOutputChanged` signal through the App actor's own real window.
let private hotReloadVsCode (appKind: AppKind) (sample: Sample) (relativePath: string) (line: int) (knobText: string) (region: Region) : Scenario =
  let file = { Sample = sample; RelativePath = relativePath }

  { Id = ScenarioId.derive Capability.HotReload Client.VsCode appKind
    Capability = Capability.HotReload
    Client = Client.VsCode
    App = appKind
    Sample = sample
    Layout = LayoutTemplate.EditorLeft
    Steps =
      [ // The extension's own session-creation paths are interactive
        // (`Actors/VsCode.fs`'s `command` doc), so this creates the session
        // through the cell daemon's own API, then waits until the daemon
        // reports it Ready. One step: the separate "Scanned N source files"
        // warmup beat that used to precede readiness was only observable on
        // the retired dashboard's output panel.
        { Caption = Caption.mk "1/4 · Create a session — it warms up and goes green"
          Action = Action.Setup(ClientCommand.CreateSession sample)
          Expect = Expectation.SessionReady
          Dwell = Dwell.medium }
        // Without this step the App co-actor never launches (`CellAgent.fs`'s
        // lazy launch fires only right after a real "run-app" dispatch).
        { Caption = Caption.mk "2/4 · Run the app"
          Action = Action.Setup ClientCommand.RunApp
          Expect = Expectation.AppState AppRunStateCase.Running
          Dwell = Dwell.medium }
        { Caption = Caption.mk "3/4 · Change a real knob in the editor"
          Action = Action.Type(Target.EditorPosition(file, line, 0), Text.mk knobText, CadenceSeed.ofId (sprintf "hr-vscode-%s-knob" relativePath))
          Expect = Expectation.EditorSaved file
          Dwell = Dwell.short }
        { Caption = Caption.mk "4/4 · Save it — the running app updates live"
          Action = Action.Chord [ Key.Ctrl; Key.S ]
          Expect = Expectation.AppOutputChanged region
          Dwell = Dwell.long } ]
    Cost = (match appKind with AppKind.Raylib -> CostClass.raylib | _ -> CostClass.console)
    Masks = [] }

let hrVsCodeRaylib: Scenario =
  hotReloadVsCode AppKind.Raylib Sample.RaylibGame "Program.fs" 1 "let starMinSpeed, starMaxSpeed = 4.0f, 9.0f" Region.appHeading

/// ★ hero (§3).
let hrVsCodeConsole: Scenario =
  hotReloadVsCode AppKind.Console Sample.ConsoleTicker "Ticker.fs" 1 "let message = \"hot-reloaded live\"" Region.appHeading

let scenarios: Scenario list = [ hrVsCodeRaylib; hrVsCodeConsole ]
