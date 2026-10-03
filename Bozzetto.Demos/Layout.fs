/// Pure per-actor rect layout for a `LayoutTemplate` on a given `Screen`
/// (demo-gif-plan.md §5, §9). Tests: rects are multiples of 8, tile without
/// overlap, respect the 8 px gutter / 1 px separator rule.
module Bozzetto.Demos.Layout

open Bozzetto.Demos.Domain

/// The `Ground` gap between adjacent panes (§9: "8px Ground gutters"). The
/// 1px `Rule` separator §9 also names is drawn as a hairline centered inside
/// this gap by the storyboard/composer — it is a rendering detail, not an
/// extra span the layout has to reserve, which is why the plan's own numbers
/// tile exactly (e.g. 704 + 8 + 568 = 1280) with nothing left over for it.
[<Literal>]
let private Gutter = 8

/// Rounds `value` to the nearest multiple of 8, biasing up on an exact tie so
/// the result is deterministic (used where a proportional pane width does not
/// land on the grid by itself).
let private snapToGrid8 (value: int) : int =
  let lower = (value / 8) * 8
  let upper = lower + 8
  if value - lower <= upper - value then lower else upper

/// The placed rect for every actor in a scenario using `template`, on a
/// screen of the given size. Every template tiles the screen exactly: full
/// height panes separated by one 8px `Ground` gutter where there are two.
/// (The plan's original layouts also reserved a pane for the web dashboard
/// as a narrator beside the editor; that pane was removed with the
/// dashboard, and the remaining panes take its space.)
let rects (template: LayoutTemplate) (screen: Screen) : Map<ActorId, Rect> =
  match template with
  | LayoutTemplate.AgentOnly ->
    // The Agent/MCP viz page alone fills the whole screen (agent and cohort
    // scenarios: no editor or app pane).
    Map.ofList [ ActorId.Agent, { X = 0; Y = 0; W = screen.Width; H = screen.Height } ]

  | LayoutTemplate.EditorFull ->
    // The editor alone fills the whole screen (scenarios with no app pane).
    let editor = { X = 0; Y = 0; W = screen.Width; H = screen.Height }
    Map.ofList [ ActorId.VsCode, editor; ActorId.Neovim, editor ]

  | LayoutTemplate.EditorLeft ->
    // §9's editor column (704×720) on the left, the app it drives in the
    // full-height right column — the story reads left→right: cause, then
    // effect. One 8px gutter separates the two columns.
    let editorW = snapToGrid8 (screen.Width * 704 / 1280)
    let appW = screen.Width - Gutter - editorW
    let editor = { X = 0; Y = 0; W = editorW; H = screen.Height }
    let app = { X = editorW + Gutter; Y = 0; W = appW; H = screen.Height }
    Map.ofList [ ActorId.VsCode, editor; ActorId.Neovim, editor; ActorId.App, app ]
