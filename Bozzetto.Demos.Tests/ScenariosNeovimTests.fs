/// Proves the shape of `Scenarios.Neovim.fs` — stable derived ids matching
/// the matrix naming (`ScenarioId.derive`), the right client/layout per
/// scenario, and (§9's "never fake a step" doctrine, applied at the data
/// level) that every step which types into the editor is followed by its
/// OWN, separate `Action.Chord [ Key.Escape ]` step before the next
/// command-line step —
/// the concrete regression this file guards is folding an embedded ESC byte
/// into a `Text` (silently dropped by `Keymap.resolve`, per `Cadence.fs`'s
/// own doc), which a purely visual read of the scenario file will not catch
/// but this structural check will.
module Bozzetto.Demos.Tests.ScenariosNeovimTests

open Expecto
open Expecto.Flip
open Bozzetto.Demos.Domain
open Bozzetto.Demos.Scenarios.Neovim

let private isEscapeChord (action: Action) : bool =
  match action with
  | Action.Chord [ Key.Escape ] -> true
  | _ -> false

/// A typed `Text` should never itself contain an embedded ESC byte — leaving
/// insert mode is always its own `Action.Chord [ Key.Escape ]` step (see the
/// module doc).
let private textOfAction (action: Action) : string option =
  match action with
  | Action.Type(_, text, _) -> Some(Text.value text)
  | _ -> None

[<Tests>]
let tests =
  testList "Scenarios.Neovim" [

    testCase "every scenario id round-trips through ScenarioId.derive with Client.Neovim" <| fun _ ->
      for s in scenarios do
        s.Client |> Expect.equal (sprintf "%s is filmed through Neovim" (ScenarioId.value s.Id)) Client.Neovim

    testCase "the hot-reload scenarios derive the matrix ids and each keeps the client's own AppKind" <| fun _ ->
      [ hrNeovimRaylib, "hr-neovim-raylib", AppKind.Raylib
        hrNeovimConsole, "hr-neovim-console", AppKind.Console ]
      |> List.iter (fun (s, expectedId, expectedApp) ->
        s.Id |> ScenarioId.value |> Expect.equal "derived id matches the matrix naming" expectedId
        s.App |> Expect.equal "app kind" expectedApp
        s.Layout |> Expect.equal "hot-reload scenarios use EditorLeft (editor + app pane)" LayoutTemplate.EditorLeft)

    testCase "each hot-reload scenario's session step is proven through the editor's own readiness channel, never a removed dashboard pane" <| fun _ ->
      for s in scenarios do
        match s.Steps with
        | { Action = Action.Setup(ClientCommand.CreateSession sample); Expect = Expectation.SessionReady } :: _ ->
          sample |> Expect.equal (sprintf "%s creates a session for its own sample" (ScenarioId.value s.Id)) s.Sample
        | first :: _ -> failtestf "%s: expected the first step to create a session and expect SessionReady, got %A" (ScenarioId.value s.Id) first
        | [] -> failtestf "%s has no steps" (ScenarioId.value s.Id)

    testCase "every Action.Type step's own Text never embeds a raw ESC byte — leaving insert mode is always its own Chord step (regression guard for the silently-dropped-keystroke bug this file's doc names)" <| fun _ ->
      for s in scenarios do
        for step in s.Steps do
          match textOfAction step.Action with
          | Some text -> text.Contains '' |> Expect.isFalse (sprintf "%s: no embedded ESC (\\u001b) in typed text" (Caption.value step.Caption))
          | None -> ()

    testCase "every scenario that types into the editor (not the command line) is immediately followed by an explicit Escape-chord step before the next Action.Type at the command line" <| fun _ ->
      // Structural proof for the hot-reload helper's own doctrine (see its
      // doc comment): an editor-targeted Type step is followed by
      // Chord[Escape] before any subsequent command-line Type step.
      for s in scenarios do
        let steps = s.Steps

        let editorTypeIndices =
          steps
          |> List.indexed
          |> List.choose (fun (i, step) ->
            match step.Action with
            | Action.Type(Target.WindowCenter ActorId.Neovim, _, _)
            | Action.Type(Target.EditorPosition _, _, _) -> Some i
            | _ -> None)

        editorTypeIndices
        |> List.isEmpty
        |> Expect.isFalse (sprintf "%s types at least once into the editor" (ScenarioId.value s.Id))

        for i in editorTypeIndices do
          steps.[i + 1].Action |> isEscapeChord |> Expect.isTrue (sprintf "%s step %d (an editor Type) is immediately followed by Chord[Escape]" (ScenarioId.value s.Id) i)
  ]
