module Bozzetto.Tests.FileCoverageTests

open Expecto
open Expecto.Flip
open Fidelity.Data.JSON
open Bozzetto.Features.LiveTesting
open Bozzetto.McpTools

let private prop name v =
  match JsonValue.prop name v with
  | Some p -> p
  | None -> failtestf "Missing property '%s' in %s" name (Json.serialize v)

let private asStr v = JsonValue.asString v |> Option.defaultValue ""
let private asInt32 v = JsonValue.asInt v |> Option.map int |> Option.defaultValue 0
let private asFloat v =
  match v with
  | JsonValue.Number n -> n
  | JsonValue.NumberLiteral _ ->
    match System.Double.TryParse(Json.serialize v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
    | true, n -> n
    | false, _ -> 0.0
  | _ -> 0.0
let private asBool v = JsonValue.asBool v |> Option.defaultValue false
let private asArr v = match v with JsonValue.Array items -> items | _ -> []

let private mkCovAnn line endLine endCol status testIds branchCov : CoverageLineAnnotation =
  { Line = line; EndLine = endLine; EndColumn = endCol
    Detail = status; CoveringTestIds = testIds; BranchCoverage = branchCov }

let private mkFileAnns path covAnns : FileAnnotations =
  { FilePath = path; TestAnnotations = [||]; CoverageAnnotations = covAnns
    InlineFailures = [||]; CodeLenses = [||]; PerformanceAnnotations = [||] }

[<Tests>]
let tests =
  testList "getFileCoverage formatting" [

    test "empty annotations produce zero-line JSON" {
      let json = formatFileCoverageResponse (mkFileAnns "test.fs" [||]) LiveTestState.empty
      let root = Json.parseOrFail json
      root |> prop "FilePath" |> asStr
        |> Expect.equal "filePath" "test.fs"
      (root |> prop "Lines" |> asArr).Length
        |> Expect.equal "no lines" 0
      root |> prop "Summary" |> prop "TotalLines" |> asInt32
        |> Expect.equal "total 0" 0
      root |> prop "Summary" |> prop "CoveragePercent" |> asFloat
        |> Expect.equal "pct 0" 0.0
    }

    test "covered line has correct structure" {
      let tid = TestId.create "MyTest" TestFramework.Expecto
      let cov =
        mkCovAnn 10 10 30
          (CoverageStatus.Covered (1, CoverageHealth.AllPassing))
          [| tid |]
          (Some LineCoverage.FullyCovered)
      let json = formatFileCoverageResponse (mkFileAnns "Foo.fs" [| cov |]) LiveTestState.empty
      let root = Json.parseOrFail json
      let line0 = root |> prop "Lines" |> asArr |> List.head
      line0 |> prop "Line" |> asInt32
        |> Expect.equal "line 10" 10
      line0 |> prop "Covered" |> asBool
        |> Expect.isTrue "is covered"
      line0 |> prop "TestCount" |> asInt32
        |> Expect.equal "1 test" 1
      line0 |> prop "Health" |> asStr
        |> Expect.equal "all passing" "AllPassing"
      let bc = line0 |> prop "BranchCoverage"
      bc |> prop "Case" |> asStr
        |> Expect.equal "fully covered" "FullyCovered"
    }

    test "not-covered line has correct structure" {
      let cov = mkCovAnn 5 5 20 CoverageStatus.NotCovered [||] None
      let json = formatFileCoverageResponse (mkFileAnns "Bar.fs" [| cov |]) LiveTestState.empty
      let root = Json.parseOrFail json
      let line0 = root |> prop "Lines" |> asArr |> List.head
      line0 |> prop "Covered" |> asBool
        |> Expect.isFalse "not covered"
      line0 |> prop "TestCount" |> asInt32
        |> Expect.equal "0 tests" 0
      line0 |> prop "Health" |> asStr
        |> Expect.equal "not covered" "NotCovered"
      let bc = line0 |> prop "BranchCoverage"
      bc |> prop "Case" |> asStr
        |> Expect.equal "unknown branch" "Unknown"
    }

    test "summary computes correct coverage percent" {
      let covered =
        mkCovAnn 1 1 10
          (CoverageStatus.Covered (1, CoverageHealth.AllPassing)) [||] None
      let notCov = mkCovAnn 2 2 10 CoverageStatus.NotCovered [||] None
      let notCov2 = mkCovAnn 3 3 10 CoverageStatus.NotCovered [||] None
      let json =
        formatFileCoverageResponse
          (mkFileAnns "Mix.fs" [| covered; notCov; notCov2 |])
          LiveTestState.empty
      let root = Json.parseOrFail json
      let summary = root |> prop "Summary"
      summary |> prop "CoveredLines" |> asInt32
        |> Expect.equal "1 covered" 1
      summary |> prop "TotalLines" |> asInt32
        |> Expect.equal "3 total" 3
      summary |> prop "CoveragePercent" |> asFloat
        |> Expect.equal "33.3%" 33.3
    }

    test "partial branch coverage formatted correctly" {
      let cov =
        mkCovAnn 7 7 25
          (CoverageStatus.Covered (2, CoverageHealth.SomeFailing))
          [||]
          (Some (LineCoverage.PartiallyCovered (3, 5)))
      let json = formatFileCoverageResponse (mkFileAnns "Branch.fs" [| cov |]) LiveTestState.empty
      let root = Json.parseOrFail json
      let line0 = root |> prop "Lines" |> asArr |> List.head
      line0 |> prop "Health" |> asStr
        |> Expect.equal "some failing" "SomeFailing"
      let bc = line0 |> prop "BranchCoverage"
      bc |> prop "Case" |> asStr
        |> Expect.equal "partial case" "PartiallyCovered"
      bc |> prop "Covered" |> asInt32
        |> Expect.equal "3 covered" 3
      bc |> prop "Total" |> asInt32
        |> Expect.equal "5 total" 5
    }

    test "covering test names resolved from discovered tests" {
      let tid = TestId.create "MyModule.myTest" TestFramework.Expecto
      let tc : TestCase = {
        Id = tid
        FullName = "MyModule.myTest"
        DisplayName = "my test"
        Origin = TestOrigin.ReflectionOnly
        Labels = []
        Framework = TestFramework.Expecto
        Category = TestCategory.Unit
      }
      let testState = { LiveTestState.empty with DiscoveredTests = [| tc |] }
      let cov =
        mkCovAnn 1 1 10
          (CoverageStatus.Covered (1, CoverageHealth.AllPassing))
          [| tid |] None
      let json = formatFileCoverageResponse (mkFileAnns "Named.fs" [| cov |]) testState
      let root = Json.parseOrFail json
      let tests =
        root |> prop "Lines" |> asArr |> List.head |> prop "CoveringTests" |> asArr
      tests
        |> List.head
        |> asStr
        |> Expect.equal "resolved name" "my test"
    }

    test "covering test falls back to TestId when not discovered" {
      let tid = TestId.create "Unknown.test" TestFramework.Expecto
      let cov =
        mkCovAnn 1 1 10
          (CoverageStatus.Covered (1, CoverageHealth.AllPassing))
          [| tid |] None
      let json = formatFileCoverageResponse (mkFileAnns "Fallback.fs" [| cov |]) LiveTestState.empty
      let root = Json.parseOrFail json
      let tests =
        root |> prop "Lines" |> asArr |> List.head |> prop "CoveringTests" |> asArr
      let testName = tests |> List.head |> asStr
      // Falls back to the raw TestId value (hex hash)
      testName |> Expect.isNonEmpty "has fallback name"
    }
  ]
