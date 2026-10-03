/// The Neovim client's scenarios (demo-actors-plan.md §1.3/§2.2): the
/// hot-reload scenarios `hr-neovim-raylib` and `hr-neovim-console`, joint
/// with the App co-actor. Built as plain `Scenario` values — a
/// caption/action/expectation/dwell per step — using `Target.NvimCommandLine`/
/// `Expectation.NvimBufferContains` (the Neovim-specific `Domain` cases) plus
/// the actor-agnostic `Target.EditorPosition`/`Action.Await`/`Action.Chord`.
///
/// STATUS: `Runtime.fs`'s `wireStepOf`/`CellAgent.fs`'s `assembleActors` are
/// genuinely wired for the Neovim actor — every step below resolves to a real
/// click/type/observe against a real kitty+nvim. Session creation goes
/// through the pinned plugin's own non-interactive
/// `:SageFsCreateSession <project>` (`ClientCommand.CreateSession`,
/// `Runtime.Neovim.fs`'s pinned commit 90bc3f41) rather than typing the bare
/// command, which opens an interactive picker synthetic keystrokes cannot
/// answer. Session readiness is observed through the Neovim actor's own RPC
/// channel (`Expectation.SessionReady` — the plugin's active session), the
/// running app and its changed output through the App co-actor, and typed
/// text/saves through nvim's own buffer state.
///
/// The REPL (`repl-neovim`), live-testing (`lt-neovim`) and web hot-reload
/// scenarios were removed with the web dashboard: their readiness, scan and
/// live-testing checks were observable only on the dashboard narrator pane
/// (that daemon-side status text is never written into nvim's own buffer),
/// and the REPL/web ones evaluated or edited the Datastar web sample.
module Bozzetto.Demos.Scenarios.Neovim

open Bozzetto.Demos.Domain

let private raylibProgram = { Sample = Sample.RaylibGame; RelativePath = "Program.fs" }
let private consoleTicker = { Sample = Sample.ConsoleTicker; RelativePath = "Ticker.fs" }

/// The hot-reload scenarios (§6's matrix), joint with the App co-actor —
/// genuinely wired (`Actors/App.fs`/`Runtime.App.fs`, `CellAgent.fs`'s lazy
/// launch right after a real `"run-app"` dispatch): real edit-save narrative,
/// a real App window, a real `Expectation.AppOutputChanged` observation
/// through it.
///
/// Typing and running a command are deliberately SEPARATE steps, never one
/// `Text` containing both: `Cadence.charToKey` only maps `'\n'`/`'\t'` inside
/// typed text to `Key.Return`/`Key.Tab` — there is no keysym for a raw ASCII
/// ESC byte embedded in a string, so `Keymap.resolve` would silently drop it
/// (`resolve` returns `[]` for an unmapped keysym, not a loud failure) and
/// the following `:w` text would land as literal buffer content instead of
/// running, while still in insert mode. Leaving insert mode is its own
/// `Action.Chord [ Key.Escape ]` step.
let private hotReloadScenario (appKind: AppKind) (sample: Sample) (file: SampleFile) (knobText: string) (expectText: string) : Scenario =
  { Id = ScenarioId.derive Capability.HotReload Client.Neovim appKind
    Capability = Capability.HotReload
    Client = Client.Neovim
    App = appKind
    Sample = sample
    Layout = LayoutTemplate.EditorLeft
    Steps =
      [ { Caption = Caption.mk "1/6 · Create a session for the real project"
          // `:SageFsCreateSession <project>` (pinned commit 90bc3f41) — the
          // non-interactive path; the bare command would open a
          // `vim.ui.select` picker no scripted actor can answer.
          Action = Action.Setup(ClientCommand.CreateSession sample)
          Expect = Expectation.SessionReady
          Dwell = Dwell.medium }
        { Caption = Caption.mk "2/6 · Run the app"
          Action = Action.Setup ClientCommand.RunApp
          Expect = Expectation.AppState AppRunStateCase.Running
          Dwell = Dwell.medium }
        { Caption = Caption.mk "3/6 · Edit a knob"
          Action = Action.Type(Target.EditorPosition(file, 1, 1), Text.mk knobText, CadenceSeed.ofId "hr-neovim-edit")
          Expect = Expectation.NvimBufferContains(Text.mk knobText)
          Dwell = Dwell.short }
        { Caption = Caption.mk "4/6 · Leave insert mode"
          // Escape is its own step, never folded into the typed `Text` above
          // (this helper's own doc).
          Action = Action.Chord [ Key.Escape ]
          Expect = Expectation.NvimBufferContains(Text.mk knobText)
          Dwell = Dwell.short }
        { Caption = Caption.mk "5/6 · Save"
          Action = Action.Type(Target.NvimCommandLine, Text.mk ":w\n", CadenceSeed.ofId "hr-neovim-save")
          Expect = Expectation.EditorSaved file
          Dwell = Dwell.short }
        { Caption = Caption.mk "6/6 · Watch it hot-reload"
          Action = Action.Await Signal.hotReloadApplied
          Expect = Expectation.AppOutputChanged(Region.appHeading)
          Dwell = Dwell.long } ]
    Cost = (match appKind with AppKind.Raylib -> CostClass.raylib | _ -> CostClass.console)
    Masks = [] }

let hrNeovimRaylib: Scenario = hotReloadScenario AppKind.Raylib Sample.RaylibGame raylibProgram "let starMaxSpeed  = 400.0f" "400.0f"
let hrNeovimConsole: Scenario = hotReloadScenario AppKind.Console Sample.ConsoleTicker consoleTicker "// hot-reloaded" "hot-reloaded"

/// Every scenario filmed through the Neovim client. `Scenarios.All.fs`
/// (Island F) aggregates this with every other client's own list.
let scenarios: Scenario list = [ hrNeovimRaylib; hrNeovimConsole ]
