module SageFs.Tests.DrawAndRectPrimitivesTests

open Expecto
open Expecto.Flip
open FsCheck
open SageFs

// ───────────────────────────────────────────────────────────
// Rect module tests
// ───────────────────────────────────────────────────────────

let rectCreateTests = testList "Rect.create" [
  test "creates rect with given values" {
    let r = Rect.create 1 2 10 20
    r.Row |> Expect.equal "row" 1
    r.Col |> Expect.equal "col" 2
    r.Width |> Expect.equal "width" 10
    r.Height |> Expect.equal "height" 20
  }
  test "clamps negative width to zero" {
    let r = Rect.create 0 0 -5 10
    r.Width |> Expect.equal "width clamped" 0
  }
  test "clamps negative height to zero" {
    let r = Rect.create 0 0 10 -5
    r.Height |> Expect.equal "height clamped" 0
  }
]

let rectEdgeTests = testList "Rect edges" [
  test "right returns col + width" {
    let r = Rect.create 0 5 10 20
    Rect.right r |> Expect.equal "right" 15
  }
  test "bottom returns row + height" {
    let r = Rect.create 3 0 10 20
    Rect.bottom r |> Expect.equal "bottom" 23
  }
  test "isEmpty is true for zero width" {
    Rect.create 0 0 0 10 |> Rect.isEmpty |> Expect.isTrue "zero width is empty"
  }
  test "isEmpty is true for zero height" {
    Rect.create 0 0 10 0 |> Rect.isEmpty |> Expect.isTrue "zero height is empty"
  }
  test "isEmpty is false for positive dimensions" {
    Rect.create 0 0 1 1 |> Rect.isEmpty |> Expect.isFalse "1x1 not empty"
  }
]

let rectInsetTests = testList "Rect.inset" [
  test "inset shrinks by margin on all sides" {
    let r = Rect.create 0 0 20 10
    let i = Rect.inset 2 r
    i.Row |> Expect.equal "row" 2
    i.Col |> Expect.equal "col" 2
    i.Width |> Expect.equal "width" 16
    i.Height |> Expect.equal "height" 6
  }
  test "inset with zero margin is identity" {
    let r = Rect.create 5 10 20 15
    Rect.inset 0 r |> Expect.equal "same rect" r
  }
  test "large inset clamps to zero dimensions" {
    let r = Rect.create 0 0 4 4
    let i = Rect.inset 10 r
    i.Width |> Expect.equal "width clamped" 0
    i.Height |> Expect.equal "height clamped" 0
  }
]

let rectSplitTests = testList "Rect.split" [
  test "splitH divides into top and bottom" {
    let r = Rect.create 0 0 20 10
    let top, bot = Rect.splitH 3 r
    top.Row |> Expect.equal "top row" 0
    top.Height |> Expect.equal "top height" 3
    bot.Row |> Expect.equal "bot row" 3
    bot.Height |> Expect.equal "bot height" 7
    top.Width |> Expect.equal "top width" 20
    bot.Width |> Expect.equal "bot width" 20
  }
  test "splitV divides into left and right" {
    let r = Rect.create 0 0 20 10
    let left, right = Rect.splitV 8 r
    left.Col |> Expect.equal "left col" 0
    left.Width |> Expect.equal "left width" 8
    right.Col |> Expect.equal "right col" 8
    right.Width |> Expect.equal "right width" 12
    left.Height |> Expect.equal "left height" 10
    right.Height |> Expect.equal "right height" 10
  }
  test "splitHProp by 0.5 gives equal halves" {
    let r = Rect.create 0 0 20 10
    let top, bot = Rect.splitHProp 0.5 r
    top.Height |> Expect.equal "top half" 5
    bot.Height |> Expect.equal "bot half" 5
  }
  test "splitVProp by 0.3 gives 30/70 split" {
    let r = Rect.create 0 0 20 10
    let left, right = Rect.splitVProp 0.3 r
    left.Width |> Expect.equal "left 30%" 6
    right.Width |> Expect.equal "right 70%" 14
  }
]

let rectPropertyTests = testList "Rect properties" [
  testProperty "create always produces non-negative dimensions" <| fun (r: int) (c: int) (w: int) (h: int) ->
    let rect = Rect.create r c w h
    rect.Width >= 0 && rect.Height >= 0

  testProperty "right = col + width" <| fun (c: NonNegativeInt) (w: NonNegativeInt) ->
    let rect = Rect.create 0 c.Get w.Get 1
    Rect.right rect = rect.Col + rect.Width

  testProperty "bottom = row + height" <| fun (r: NonNegativeInt) (h: NonNegativeInt) ->
    let rect = Rect.create r.Get 0 1 h.Get
    Rect.bottom rect = rect.Row + rect.Height

  testProperty "splitH top + bottom heights = total height" <| fun (h: int) (splitAt: int) ->
    let h' = abs h % 1000 + 1
    let s = (abs splitAt) % h'
    let rect = Rect.create 0 0 10 h'
    let top, bot = Rect.splitH s rect
    top.Height + bot.Height = h'

  testProperty "splitV left + right widths = total width" <| fun (w: int) (splitAt: int) ->
    let w' = abs w % 1000 + 1
    let s = (abs splitAt) % w'
    let rect = Rect.create 0 0 w' 10
    let left, right = Rect.splitV s rect
    left.Width + right.Width = w'

  testProperty "inset shrinks dimensions by 2*margin" <| fun (w: int) (h: int) (m: int) ->
    let w' = abs w % 1000
    let h' = abs h % 1000
    let m' = abs m % 100
    let rect = Rect.create 0 0 w' h'
    let insetted = Rect.inset m' rect
    insetted.Width = max 0 (w' - 2 * m') && insetted.Height = max 0 (h' - 2 * m')

  testProperty "isEmpty iff width or height is zero" <| fun (w: int) (h: int) ->
    let w' = abs w % 100
    let h' = abs h % 100
    let rect = Rect.create 0 0 w' h'
    Rect.isEmpty rect = (w' = 0 || h' = 0)
]

// ───────────────────────────────────────────────────────────
// DrawTarget module tests
// ───────────────────────────────────────────────────────────

let drawTargetTests = testList "DrawTarget" [
  test "create wraps grid and clip" {
    let grid = CellGrid.create 10 20
    let clip = Rect.create 0 0 20 10
    let dt = DrawTarget.create grid clip
    dt.Grid |> Expect.equal "grid" grid
    dt.Clip |> Expect.equal "clip" clip
  }
  test "sub computes intersection of parent clip and child rect" {
    let grid = CellGrid.create 15 30
    let parent = DrawTarget.create grid (Rect.create 2 3 28 13)
    let child = DrawTarget.sub parent (Rect.create 1 1 10 5)
    // intersection: row=max(2,1)=2, col=max(3,1)=3
    // r2=min(2+28,1+10)=min(30,11)=11, b2=min(2+13,1+5)=min(15,6)=6
    // w=11-3=8, h=6-2=4
    child.Clip.Row |> Expect.equal "row" 2
    child.Clip.Col |> Expect.equal "col" 3
    child.Clip.Width |> Expect.equal "width" 8
    child.Clip.Height |> Expect.equal "height" 4
  }
  test "sub with non-overlapping rect produces empty clip" {
    let grid = CellGrid.create 10 10
    let parent = DrawTarget.create grid (Rect.create 0 0 5 5)
    let child = DrawTarget.sub parent (Rect.create 6 6 5 5)
    Rect.isEmpty child.Clip |> Expect.isTrue "no overlap"
  }
]

// ───────────────────────────────────────────────────────────
// Draw module tests
// ───────────────────────────────────────────────────────────

let drawTextTests = testList "Draw.text" [
  test "writes text to grid at specified position" {
    let grid = CellGrid.create 5 20
    let dt = DrawTarget.create grid (Rect.create 0 0 20 5)
    Draw.text dt 1 2 0xFFFFFFu 0x000000u CellAttrs.None "Hello"
    CellGrid.get grid 1 2 |> fun c -> c.Char |> Expect.equal "H" 'H'
    CellGrid.get grid 1 6 |> fun c -> c.Char |> Expect.equal "o" 'o'
  }
  test "clips text outside rect" {
    let grid = CellGrid.create 5 10
    let dt = DrawTarget.create grid (Rect.create 0 0 5 5)
    Draw.text dt 0 3 0xFFFFFFu 0x000000u CellAttrs.None "Hello"
    // Only "He" fits (cols 3,4 within width 5)
    CellGrid.get grid 0 3 |> fun c -> c.Char |> Expect.equal "H" 'H'
    CellGrid.get grid 0 4 |> fun c -> c.Char |> Expect.equal "e" 'e'
    CellGrid.get grid 0 5 |> fun c -> c.Char |> Expect.equal "outside" ' '
  }
  test "text outside clip row does nothing" {
    let grid = CellGrid.create 5 10
    let dt = DrawTarget.create grid (Rect.create 0 0 10 3)
    Draw.text dt 5 0 0xFFFFFFu 0x000000u CellAttrs.None "Hello"
    CellGrid.get grid 4 0 |> fun c -> c.Char |> Expect.equal "untouched" ' '
  }
]

let drawFillTests = testList "Draw.fill" [
  test "fill sets all cells in clip to bg color" {
    let grid = CellGrid.create 3 5
    let dt = DrawTarget.create grid (Rect.create 0 0 5 3)
    Draw.fill dt 0x123456u
    CellGrid.get grid 1 2 |> fun c -> c.Bg |> Expect.equal "bg" 0x123456u
  }
  test "fill only affects clip region" {
    let grid = CellGrid.create 10 10
    let dt = DrawTarget.create grid (Rect.create 2 2 3 3)
    Draw.fill dt 0xAABBCCu
    CellGrid.get grid 0 0 |> fun c -> c.Bg |> Expect.equal "outside" 0u
    CellGrid.get grid 3 3 |> fun c -> c.Bg |> Expect.equal "inside" 0xAABBCCu
  }
]

let drawLineTests = testList "Draw.lines" [
  test "hline draws across clip width" {
    let grid = CellGrid.create 5 10
    let dt = DrawTarget.create grid (Rect.create 0 0 10 5)
    Draw.hline dt 2 0xFFFFFFu 0x000000u '─'
    for c in 0..9 do
      CellGrid.get grid 2 c |> fun cell -> cell.Char |> Expect.equal (sprintf "col %d" c) '─'
  }
  test "vline draws down clip height" {
    let grid = CellGrid.create 5 10
    let dt = DrawTarget.create grid (Rect.create 0 0 10 5)
    Draw.vline dt 3 0xFFFFFFu 0x000000u '│'
    for r in 0..4 do
      CellGrid.get grid r 3 |> fun cell -> cell.Char |> Expect.equal (sprintf "row %d" r) '│'
  }
]

let drawBoxTests = testList "Draw.box" [
  test "box draws border characters" {
    let grid = CellGrid.create 5 10
    let dt = DrawTarget.create grid (Rect.create 0 0 10 5)
    let _inner = Draw.box dt "" 0xFFFFFFu 0x000000u
    CellGrid.get grid 0 0 |> fun c -> c.Char |> Expect.equal "top-left" '┌'
    CellGrid.get grid 0 9 |> fun c -> c.Char |> Expect.equal "top-right" '┐'
    CellGrid.get grid 4 0 |> fun c -> c.Char |> Expect.equal "bottom-left" '└'
    CellGrid.get grid 4 9 |> fun c -> c.Char |> Expect.equal "bottom-right" '┘'
  }
  test "box returns interior DrawTarget" {
    let grid = CellGrid.create 10 20
    let dt = DrawTarget.create grid (Rect.create 0 0 20 10)
    let inner = Draw.box dt "" 0xFFFFFFu 0x000000u
    inner.Clip.Row |> Expect.equal "inner row" 1
    inner.Clip.Col |> Expect.equal "inner col" 1
    inner.Clip.Width |> Expect.equal "inner width" 18
    inner.Clip.Height |> Expect.equal "inner height" 8
  }
  test "box with title draws title text" {
    let grid = CellGrid.create 5 20
    let dt = DrawTarget.create grid (Rect.create 0 0 20 5)
    let _ = Draw.box dt "My Title" 0xFFFFFFu 0x000000u
    // Format is: ┌─ My Title ───┐  so M starts at col 3
    CellGrid.get grid 0 3 |> fun c -> c.Char |> Expect.equal "title char" 'M'
  }
]

let drawScrolledLinesTests = testList "Draw.scrolledLines" [
  test "renders lines from scroll offset" {
    let grid = CellGrid.create 5 20
    let dt = DrawTarget.create grid (Rect.create 0 0 20 5)
    let lines = [ "line0"; "line1"; "line2"; "line3"; "line4"; "line5" ]
    Draw.scrolledLines dt lines 1 0xFFFFFFu 0x000000u
    // offset=1 means first visible line is "line1"
    CellGrid.get grid 0 0 |> fun c -> c.Char |> Expect.equal "first visible" 'l'
    CellGrid.get grid 0 4 |> fun c -> c.Char |> Expect.equal "fifth char" '1'
  }
  test "renders nothing when offset beyond lines" {
    let grid = CellGrid.create 5 20
    let dt = DrawTarget.create grid (Rect.create 0 0 20 5)
    let lines = [ "line0"; "line1" ]
    Draw.scrolledLines dt lines 10 0xFFFFFFu 0x000000u
    CellGrid.get grid 0 0 |> fun c -> c.Char |> Expect.equal "empty" ' '
  }
]

let drawStatusBarTests = testList "Draw.statusBar" [
  test "left text appears at left edge" {
    let grid = CellGrid.create 5 30
    let dt = DrawTarget.create grid (Rect.create 0 0 30 5)
    Draw.statusBar dt "LEFT" "RIGHT" 0xFFFFFFu 0x000000u
    let lastRow = 4
    CellGrid.get grid lastRow 0 |> fun c -> c.Char |> Expect.equal "L" 'L'
    CellGrid.get grid lastRow 3 |> fun c -> c.Char |> Expect.equal "T" 'T'
  }
  test "right text appears at right edge" {
    let grid = CellGrid.create 5 30
    let dt = DrawTarget.create grid (Rect.create 0 0 30 5)
    Draw.statusBar dt "L" "RIGHT" 0xFFFFFFu 0x000000u
    let lastRow = 4
    // RIGHT is 5 chars, so starts at col 30 - 5 = 25
    CellGrid.get grid lastRow 25 |> fun c -> c.Char |> Expect.equal "R" 'R'
    CellGrid.get grid lastRow 29 |> fun c -> c.Char |> Expect.equal "T" 'T'
  }
]

// ───────────────────────────────────────────────────────────
// Cell module tests
// ───────────────────────────────────────────────────────────

let cellTests = testList "Cell" [
  test "create sets all fields" {
    let c = Cell.create 'A' 0xFFu 0x00u CellAttrs.Bold
    c.Char |> Expect.equal "char" 'A'
    c.Fg |> Expect.equal "fg" 0xFFu
    c.Bg |> Expect.equal "bg" 0x00u
    c.Attrs |> Expect.equal "attrs" CellAttrs.Bold
  }
  test "empty is space with default colors" {
    let e = Cell.empty
    e.Char |> Expect.equal "char" ' '
    e.Fg |> Expect.equal "fg" 0xFFFFFFu
    e.Bg |> Expect.equal "bg" 0u
    e.Attrs |> Expect.equal "attrs" CellAttrs.None
  }
]

// ───────────────────────────────────────────────────────────
// SessionState module tests
// ───────────────────────────────────────────────────────────

let sessionStateTests = testList "SessionState.label" [
  test "all cases have labels" {
    let cases =
      [ SessionState.Uninitialized, "Uninitialized"
        SessionState.WarmingUp, "WarmingUp"
        SessionState.Ready, "Ready"
        SessionState.Evaluating, "Evaluating"
        SessionState.Faulted, "Faulted" ]
    for state, expected in cases do
      SessionState.label state |> Expect.equal (sprintf "%s label" expected) expected
  }
]

// ───────────────────────────────────────────────────────────
// Combined test list
// ───────────────────────────────────────────────────────────

[<Tests>]
let allTests = testList "Draw and Rect coverage" [
  testList "Rect" [
    rectCreateTests
    rectEdgeTests
    rectInsetTests
    rectSplitTests
    rectPropertyTests
  ]
  testList "Draw" [
    drawTargetTests
    drawTextTests
    drawFillTests
    drawLineTests
    drawBoxTests
    drawScrolledLinesTests
    drawStatusBarTests
  ]
  cellTests
  sessionStateTests
]
