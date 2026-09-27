module SageFs.Tests.AlgebraicPropertyTests

open Expecto
open Expecto.Flip
open FsCheck
open SageFs
open SageFs.Features.LiveTesting

let private cfg = { FsCheckConfig.defaultConfig with maxTest = 200 }

// ── CellGrid: set/get roundtrip, bounds safety, clone independence ──

let cellGridPropertyTests =
  testList "CellGrid algebraic properties" [
    testPropertyWithConfig cfg "set/get roundtrip for in-bounds coords" <|
      fun (PositiveInt rows) (PositiveInt cols) (NonNegativeInt r) (NonNegativeInt c) ->
        let rows = min rows 100
        let cols = min cols 100
        let row = r % rows
        let col = c % cols
        let cell = Cell.create 'X' 0xFF0000u 0x00FF00u CellAttrs.Bold
        let grid = CellGrid.create rows cols
        CellGrid.set grid row col cell
        CellGrid.get grid row col = cell

    testPropertyWithConfig cfg "get returns Cell.empty for out-of-bounds" <|
      fun (PositiveInt rows) (PositiveInt cols) ->
        let rows = min rows 100
        let cols = min cols 100
        let grid = CellGrid.create rows cols
        CellGrid.get grid -1 0 = Cell.empty
        && CellGrid.get grid 0 -1 = Cell.empty
        && CellGrid.get grid rows 0 = Cell.empty
        && CellGrid.get grid 0 cols = Cell.empty

    testPropertyWithConfig cfg "set out-of-bounds is silent no-op" <|
      fun (PositiveInt rows) (PositiveInt cols) ->
        let rows = min rows 100
        let cols = min cols 100
        let grid = CellGrid.create rows cols
        let cell = Cell.create 'Z' 0u 0u CellAttrs.Dim
        CellGrid.set grid -1 0 cell
        CellGrid.set grid 0 -1 cell
        CellGrid.set grid rows 0 cell
        CellGrid.set grid 0 cols cell
        [| for r in 0 .. rows - 1 do
             for c in 0 .. cols - 1 do
               yield CellGrid.get grid r c = Cell.empty |]
        |> Array.forall id

    testPropertyWithConfig cfg "clone produces independent copy" <|
      fun (PositiveInt rows) (PositiveInt cols) (NonNegativeInt r) (NonNegativeInt c) ->
        let rows = min rows 50
        let cols = min cols 50
        let row = r % rows
        let col = c % cols
        let grid = CellGrid.create rows cols
        let cloned = CellGrid.clone grid
        let cell = Cell.create 'Y' 0xFFu 0xAAu CellAttrs.Inverse
        CellGrid.set cloned row col cell
        CellGrid.get grid row col = Cell.empty
        && CellGrid.get cloned row col = cell
  ]

// ── CoverageBitmap: monoid laws, lattice absorption, popCount ──
// Binary ops require same-Count bitmaps. We use FsCheck-generated bool arrays
// and derive same-sized second/third bitmaps deterministically.

let coverageBitmapPropertyTests =
  testList "CoverageBitmap algebraic properties" [
    testPropertyWithConfig cfg "ofBoolArray/toBoolArray roundtrip" <|
      fun (hits: bool array) ->
        let bm = CoverageBitmap.ofBoolArray hits
        CoverageBitmap.toBoolArray bm = hits

    testPropertyWithConfig cfg "union is commutative" <|
      fun (hits1: bool array) ->
        let n = hits1.Length
        let hits2 = Array.init n (fun i -> i % 3 = 0 || hits1.[i])
        let a = CoverageBitmap.ofBoolArray hits1
        let b = CoverageBitmap.ofBoolArray hits2
        CoverageBitmap.equivalent (CoverageBitmap.union a b) (CoverageBitmap.union b a)

    testPropertyWithConfig cfg "union is associative" <|
      fun (hits1: bool array) ->
        let n = hits1.Length
        let hits2 = Array.init n (fun i -> i % 2 = 0)
        let hits3 = Array.init n (fun i -> i % 3 = 0)
        let a = CoverageBitmap.ofBoolArray hits1
        let b = CoverageBitmap.ofBoolArray hits2
        let c = CoverageBitmap.ofBoolArray hits3
        CoverageBitmap.equivalent
          (CoverageBitmap.union (CoverageBitmap.union a b) c)
          (CoverageBitmap.union a (CoverageBitmap.union b c))

    testPropertyWithConfig cfg "union with all-false is identity" <|
      fun (hits: bool array) ->
        let a = CoverageBitmap.ofBoolArray hits
        let zeros = CoverageBitmap.ofBoolArray (Array.zeroCreate<bool> hits.Length)
        CoverageBitmap.equivalent (CoverageBitmap.union a zeros) a

    testPropertyWithConfig cfg "union is idempotent" <|
      fun (hits: bool array) ->
        let a = CoverageBitmap.ofBoolArray hits
        CoverageBitmap.equivalent (CoverageBitmap.union a a) a

    testPropertyWithConfig cfg "intersect is commutative" <|
      fun (hits1: bool array) ->
        let n = hits1.Length
        let hits2 = Array.init n (fun i -> i % 2 = 0 || hits1.[i])
        let a = CoverageBitmap.ofBoolArray hits1
        let b = CoverageBitmap.ofBoolArray hits2
        CoverageBitmap.equivalent (CoverageBitmap.intersect a b) (CoverageBitmap.intersect b a)

    testPropertyWithConfig cfg "intersect is associative" <|
      fun (hits1: bool array) ->
        let n = hits1.Length
        let hits2 = Array.init n (fun i -> i % 2 = 0)
        let hits3 = Array.init n (fun i -> i % 3 = 0)
        let a = CoverageBitmap.ofBoolArray hits1
        let b = CoverageBitmap.ofBoolArray hits2
        let c = CoverageBitmap.ofBoolArray hits3
        CoverageBitmap.equivalent
          (CoverageBitmap.intersect (CoverageBitmap.intersect a b) c)
          (CoverageBitmap.intersect a (CoverageBitmap.intersect b c))

    testPropertyWithConfig cfg "absorption: intersect a (union a b) = a" <|
      fun (hits1: bool array) ->
        let n = hits1.Length
        let hits2 = Array.init n (fun i -> i % 2 = 0)
        let a = CoverageBitmap.ofBoolArray hits1
        let b = CoverageBitmap.ofBoolArray hits2
        CoverageBitmap.equivalent
          (CoverageBitmap.intersect a (CoverageBitmap.union a b))
          a

    testPropertyWithConfig cfg "popCount monotonicity: popCount(union a b) >= max(popCount a, popCount b)" <|
      fun (hits1: bool array) ->
        let n = hits1.Length
        let hits2 = Array.init n (fun i -> i % 2 = 0)
        let a = CoverageBitmap.ofBoolArray hits1
        let b = CoverageBitmap.ofBoolArray hits2
        let unionPop = CoverageBitmap.popCount (CoverageBitmap.union a b)
        let maxPop = max (CoverageBitmap.popCount a) (CoverageBitmap.popCount b)
        unionPop >= maxPop

    testPropertyWithConfig cfg "popCount matches count of true values" <|
      fun (hits: bool array) ->
        let bm = CoverageBitmap.ofBoolArray hits
        let expected = hits |> Array.filter id |> Array.length
        CoverageBitmap.popCount bm = expected
  ]

[<Tests>]
let allAlgebraicPropertyTests =
  testList "Algebraic property tests" [
    cellGridPropertyTests
    coverageBitmapPropertyTests
  ]
