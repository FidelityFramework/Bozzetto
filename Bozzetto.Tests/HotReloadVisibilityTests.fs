namespace Bozzetto.Tests

open Expecto
open Expecto.Flip
open Bozzetto

module HotReloadVisibilityTests =

  let private defaultPanes = LayoutConfig.defaults.VisiblePanes

  let statusHintTests = testList "StatusHints base visibility" [
    testCase "standard editor hints remain present" <| fun _ ->
      let result = StatusHints.build KeyMap.defaults PaneId.Editor defaultPanes 0 UiDensity.Normal
      result |> Expect.stringContains "quit still present" "quit"
      result |> Expect.stringContains "eval still present" "eval"

    testCase "empty keymap still returns empty string" <| fun _ ->
      let result = StatusHints.build Map.empty PaneId.Editor Set.empty 0 UiDensity.Normal
      result |> Expect.equal "empty hints" ""
  ]

  [<Tests>]
  let tests = testList "StatusHints visibility" [statusHintTests]
