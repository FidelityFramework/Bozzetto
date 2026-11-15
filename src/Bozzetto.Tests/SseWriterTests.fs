module Bozzetto.Tests.SseWriterTests

open System.IO
open System.Text
open Fidelity.Data.JSON
open Expecto
open Expecto.Flip
open Bozzetto.SseWriter
open Bozzetto
open Bozzetto.Features.LiveTesting

[<Tests>]
let sseTests = testList "SSE Writer" [
  testList "formatSseEvent" [
    testCase "formats single-line event" <| fun () ->
      formatSseEvent "test_summary" """{"total":5}"""
      |> Expect.equal "should format correctly"
           "event: test_summary\ndata: {\"total\":5}\n\n"

    testCase "handles empty data" <| fun () ->
      formatSseEvent "ping" ""
      |> Expect.equal "should format with empty data"
           "event: ping\ndata: \n\n"
  ]

  testList "formatSseEventMultiline" [
    testCase "formats multiline event" <| fun () ->
      formatSseEventMultiline "update" [ "line1"; "line2"; "line3" ]
      |> Expect.equal "should format each line with data:"
           "event: update\ndata: line1\ndata: line2\ndata: line3\n\n"

    testCase "handles empty lines list" <| fun () ->
      formatSseEventMultiline "empty" []
      |> Expect.equal "should format with no data lines"
           "event: empty\n\n"

    testCase "handles single line" <| fun () ->
      formatSseEventMultiline "single" [ "only" ]
      |> Expect.equal "should format single line"
           "event: single\ndata: only\n\n"
  ]

  testList "trySendBytes" [
    testTask "writes bytes to stream successfully" {
      use ms = new MemoryStream()
      let bytes = Encoding.UTF8.GetBytes("hello")
      let! result = trySendBytes ms bytes
      result |> Expect.isOk "should succeed"
      ms.ToArray() |> Encoding.UTF8.GetString
      |> Expect.equal "should have written content" "hello"
    }

    testTask "returns Error on disposed stream" {
      let ms = new MemoryStream()
      ms.Dispose()
      let bytes = Encoding.UTF8.GetBytes("hello")
      let! result = trySendBytes ms bytes
      result |> Expect.isError "should fail on disposed stream"
    }
  ]

  testList "trySendSseEvent" [
    testTask "sends formatted SSE event to stream" {
      use ms = new MemoryStream()
      let! result = trySendSseEvent ms "test" "data"
      result |> Expect.isOk "should succeed"
      ms.ToArray() |> Encoding.UTF8.GetString
      |> Expect.equal "should have formatted SSE" "event: test\ndata: data\n\n"
    }
  ]

  testList "formatTestSummaryEvent" [
    testCase "serializes TestSummary to SSE with PascalCase" <| fun () ->
      // Production SSE uses the explicit PascalCase Fidelity.Data schema.
      // NOT camelCase — see McpServer.fs sseJsonOpts
      let opts = Bozzetto.JsonCasing.PascalCase
      let summary: Bozzetto.Features.LiveTesting.TestSummary = {
        Total = 10; Passed = 8; Failed = 1; Stale = 1; Running = 0; Disabled = 0; Enabled = true
      }
      let result = formatTestSummaryEvent opts None summary None
      result |> Expect.stringContains "should contain event type" "event: test_summary"
      result |> Expect.stringContains "should contain PascalCase Total" "\"Total\":10"
      result |> Expect.stringContains "should contain PascalCase Passed" "\"Passed\":8"
      result |> Expect.stringContains "should end with double newline" "\n\n"
  ]

  testList "formatTestSummaryEventWithDiscovery" [
    testCase "zero-test discovery completion is observable on the wire" <| fun () ->
      // Phase 3 RED: a completed zero-test discovery must reach clients as
      // ready_zero_tests with a discovery generation — not silence.
      let opts = Bozzetto.JsonCasing.PascalCase
      let summary: Bozzetto.Features.LiveTesting.TestSummary = {
        Total = 0; Passed = 0; Failed = 0; Stale = 0; Running = 0; Disabled = 0; Enabled = true
      }
      let result =
        formatTestSummaryEventWithDiscovery opts (Some "sess-1") summary None
          Bozzetto.Features.LiveTesting.LiveTestDiscoveryState.ReadyZeroTests 7L
          (Bozzetto.Features.LiveTestActivity.LiveTestActivity.NoTestsFound [])
      result |> Expect.stringContains "should be a test_summary event" "event: test_summary"
      result |> Expect.stringContains "should carry session id" "\"SessionId\":\"sess-1\""
      result |> Expect.stringContains "zero-test discovery must be observable" "ready_zero_tests"
      result |> Expect.stringContains "should carry the discovery generation" "\"DiscoveryGeneration\":7"

    testCase "ready_with_tests surfaces the discovered count on the wire" <| fun () ->
      let opts = Bozzetto.JsonCasing.PascalCase
      let summary: Bozzetto.Features.LiveTesting.TestSummary = {
        Total = 3; Passed = 0; Failed = 0; Stale = 0; Running = 0; Disabled = 0; Enabled = true
      }
      let result =
        formatTestSummaryEventWithDiscovery opts None summary None
          (Bozzetto.Features.LiveTesting.LiveTestDiscoveryState.ReadyWithTests 3) 2L
          (Bozzetto.Features.LiveTestActivity.LiveTestActivity.Settled { Bozzetto.Features.LiveTestActivity.TestTally.empty with NotYetRun = 3 })
      result |> Expect.stringContains "non-zero discovery should surface ready_with_tests" "ready_with_tests"
      result |> Expect.stringContains "should carry the discovery generation" "\"DiscoveryGeneration\":2"

    testCase "WHY — the summary carries the session's activity so every client shows the same words" <| fun () ->
      let opts = Bozzetto.JsonCasing.PascalCase
      let summary: Bozzetto.Features.LiveTesting.TestSummary = {
        Total = 12; Passed = 0; Failed = 0; Stale = 0; Running = 0; Disabled = 0; Enabled = true
      }
      let activity =
        Bozzetto.Features.LiveTestActivity.LiveTestActivity.Settled { Bozzetto.Features.LiveTestActivity.TestTally.empty with NotYetRun = 12 }
      let result =
        formatTestSummaryEventWithDiscovery opts None summary None
          (Bozzetto.Features.LiveTesting.LiveTestDiscoveryState.ReadyWithTests 12) 3L activity
      result |> Expect.stringContains "names the activity for clients to switch on" "\"Activity\":\"settled\""
      result |> Expect.stringContains "carries the full wording" "\"ActivityText\":\"12 not yet run\""
      result |> Expect.stringContains "carries the status-bar wording" "\"ActivityShort\":\"12 not yet run\""
      result |> Expect.stringContains "counts tests that never ran" "\"NotYetRun\":12"
  ]
]

/// Production-equivalent SSE serialization options (must match McpServer.fs sseJsonOpts)
let productionSseOpts = Bozzetto.JsonCasing.PascalCase

let extractSseData (sseEvent: string) =
  sseEvent.Split('\n')
  |> Array.tryFind (fun l -> l.StartsWith("data: "))
  |> Option.map (fun l -> l.Substring(6))

[<Tests>]
let wireProtocolTests = testList "Wire Protocol Contract" [

  testList "TestSummary shape" [
    testCase "has all expected PascalCase fields" <| fun () ->
      let summary: Bozzetto.Features.LiveTesting.TestSummary = {
        Total = 10; Passed = 7; Failed = 1; Stale = 1; Running = 0; Disabled = 1; Enabled = true
      }
      let json = LiveTestingJson.summaryValue productionSseOpts summary |> WireJson.serialize
      let root = Json.parseOrFail json
      root |> JsonValue.prop "Total" |> Option.isSome |> Expect.isTrue "has Total"
      root |> JsonValue.prop "Passed" |> Option.isSome |> Expect.isTrue "has Passed"
      root |> JsonValue.prop "Failed" |> Option.isSome |> Expect.isTrue "has Failed"
      root |> JsonValue.prop "Stale" |> Option.isSome |> Expect.isTrue "has Stale"
      root |> JsonValue.prop "Running" |> Option.isSome |> Expect.isTrue "has Running"
      root |> JsonValue.prop "Disabled" |> Option.isSome |> Expect.isTrue "has Disabled"

    testCase "round-trip values are correct" <| fun () ->
      let summary: Bozzetto.Features.LiveTesting.TestSummary = {
        Total = 47; Passed = 40; Failed = 3; Stale = 2; Running = 1; Disabled = 1; Enabled = true
      }
      let sse = formatTestSummaryEvent productionSseOpts None summary None
      let data = extractSseData sse |> Option.get
      let root = Json.parseOrFail data
      root |> JsonValue.prop "Total" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "Total" 47
      root |> JsonValue.prop "Passed" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "Passed" 40
      root |> JsonValue.prop "Failed" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "Failed" 3
      root |> JsonValue.prop "Stale" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "Stale" 2
      root |> JsonValue.prop "Disabled" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "Disabled" 1

    testCase "no camelCase properties in output" <| fun () ->
      let summary: Bozzetto.Features.LiveTesting.TestSummary = {
        Total = 10; Passed = 8; Failed = 1; Stale = 1; Running = 0; Disabled = 0; Enabled = true
      }
      let sse = formatTestSummaryEvent productionSseOpts None summary None
      let data = extractSseData sse |> Option.get
      data.Contains("\"total\"") |> Expect.isFalse "no camelCase total"
      data.Contains("\"passed\"") |> Expect.isFalse "no camelCase passed"
  ]

  testList "ResultFreshness DU shape" [
    testCase "Fresh serializes as Case/Fields" <| fun () ->
      let json = LiveTestingJson.freshnessValue productionSseOpts Bozzetto.Features.LiveTesting.ResultFreshness.Fresh |> WireJson.serialize
      let doc = Json.parseOrFail json
      doc |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "Fresh"

    testCase "StaleCodeEdited serializes as Case/Fields" <| fun () ->
      let json = LiveTestingJson.freshnessValue productionSseOpts Bozzetto.Features.LiveTesting.ResultFreshness.StaleCodeEdited |> WireJson.serialize
      let doc = Json.parseOrFail json
      doc |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "StaleCodeEdited"

    testCase "StaleWrongGeneration serializes as Case/Fields" <| fun () ->
      let json = LiveTestingJson.freshnessValue productionSseOpts Bozzetto.Features.LiveTesting.ResultFreshness.StaleWrongGeneration |> WireJson.serialize
      let doc = Json.parseOrFail json
      doc |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "StaleWrongGeneration"
  ]

  testList "TestRunStatus DU shape" [
    testCase "Stale has Case:Stale" <| fun () ->
      let json = LiveTestingJson.statusValue productionSseOpts (Bozzetto.Features.LiveTesting.TestRunStatus.Stale) |> WireJson.serialize
      let doc = Json.parseOrFail json
      doc |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "Stale"

    testCase "PolicyDisabled has Case:PolicyDisabled" <| fun () ->
      let json = LiveTestingJson.statusValue productionSseOpts (Bozzetto.Features.LiveTesting.TestRunStatus.PolicyDisabled) |> WireJson.serialize
      let doc = Json.parseOrFail json
      doc |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "PolicyDisabled"

    testCase "Passed carries duration in Fields" <| fun () ->
      let status = Bozzetto.Features.LiveTesting.TestRunStatus.Passed (System.TimeSpan.FromMilliseconds(42.5))
      let json = LiveTestingJson.statusValue productionSseOpts status |> WireJson.serialize
      let root = Json.parseOrFail json
      root |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "Passed"
      let fields =
        match JsonValue.prop "Fields" root with
        | Some (JsonValue.Array items) -> items
        | _ -> []
      fields.Length |> Expect.equal "1 field" 1
      fields.[0] |> JsonValue.asString |> Option.isSome |> Expect.isTrue "field is string"

    testCase "Failed carries failure+duration in Fields" <| fun () ->
      let failure = Bozzetto.Features.LiveTesting.TestFailure.AssertionFailed "oops"
      let dur = System.TimeSpan.FromMilliseconds(100.0)
      let status = Bozzetto.Features.LiveTesting.TestRunStatus.Failed(failure, dur)
      let json = LiveTestingJson.statusValue productionSseOpts status |> WireJson.serialize
      let root = Json.parseOrFail json
      root |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "Failed"
      let fields =
        match JsonValue.prop "Fields" root with
        | Some (JsonValue.Array items) -> items
        | _ -> []
      fields.Length |> Expect.equal "2 fields" 2

    testCase "Skipped carries reason in Fields" <| fun () ->
      let json = LiveTestingJson.statusValue productionSseOpts (Bozzetto.Features.LiveTesting.TestRunStatus.Skipped "not applicable") |> WireJson.serialize
      let root = Json.parseOrFail json
      root |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "Skipped"
      let fields =
        match JsonValue.prop "Fields" root with
        | Some (JsonValue.Array items) -> items
        | _ -> []
      fields.[0] |> JsonValue.asString |> Option.defaultValue "" |> Expect.equal "reason" "not applicable"
  ]

  testList "TestResultsBatchPayload shape" [
    testCase "has Freshness, Entries, Summary, Generation, Completion" <| fun () ->
      let payload: Bozzetto.Features.LiveTesting.TestResultsBatchPayload = {
        Generation = Bozzetto.Features.LiveTesting.RunGeneration 1
        Freshness = Bozzetto.Features.LiveTesting.ResultFreshness.StaleCodeEdited
        Completion = Bozzetto.Features.LiveTesting.BatchCompletion.Superseded
        Entries = [||]
        Summary = { Total = 0; Passed = 0; Failed = 0; Stale = 0; Running = 0; Disabled = 0; Enabled = true }
        LastDecision = None
      }
      let json = LiveTestingJson.batchValue productionSseOpts payload |> WireJson.serialize
      let root = Json.parseOrFail json
      root |> JsonValue.prop "Freshness" |> Option.isSome |> Expect.isTrue "has Freshness"
      root |> JsonValue.prop "Entries" |> Option.isSome |> Expect.isTrue "has Entries"
      root |> JsonValue.prop "Summary" |> Option.isSome |> Expect.isTrue "has Summary"
      root |> JsonValue.prop "Generation" |> Option.isSome |> Expect.isTrue "has Generation"
      root |> JsonValue.prop "Completion" |> Option.isSome |> Expect.isTrue "has Completion"

    testCase "Freshness is nested DU object" <| fun () ->
      let payload: Bozzetto.Features.LiveTesting.TestResultsBatchPayload = {
        Generation = Bozzetto.Features.LiveTesting.RunGeneration 1
        Freshness = Bozzetto.Features.LiveTesting.ResultFreshness.StaleCodeEdited
        Completion = Bozzetto.Features.LiveTesting.BatchCompletion.Complete(5, 5)
        Entries = [||]
        Summary = { Total = 5; Passed = 5; Failed = 0; Stale = 0; Running = 0; Disabled = 0; Enabled = true }
        LastDecision = None
      }
      let json = LiveTestingJson.batchValue productionSseOpts payload |> WireJson.serialize
      let doc = Json.parseOrFail json
      let freshEl = doc |> JsonValue.prop "Freshness" |> Option.defaultValue JsonValue.Null
      let isObj = match freshEl with JsonValue.Object _ -> true | _ -> false
      isObj |> Expect.isTrue "is object"
      freshEl |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "StaleCodeEdited"
  ]
]

/// Helper to serialize and parse in one call
let private serAndParse encode value =
  let json = encode productionSseOpts value |> WireJson.serialize
  Json.parseOrFail json

[<Tests>]
let protocolSnapshotTests = testList "Protocol Snapshots" [

  testList "RunPolicy wire format" [
    testCase "OnEveryChange" <| fun () ->
      (serAndParse LiveTestingJson.policyValue RunPolicy.OnEveryChange) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "OnEveryChange"
    testCase "OnSaveOnly" <| fun () ->
      (serAndParse LiveTestingJson.policyValue RunPolicy.OnSaveOnly) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "OnSaveOnly"
    testCase "OnDemand" <| fun () ->
      (serAndParse LiveTestingJson.policyValue RunPolicy.OnDemand) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "OnDemand"
    testCase "Disabled" <| fun () ->
      (serAndParse LiveTestingJson.policyValue RunPolicy.Disabled) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Disabled"
  ]

  testList "TestCategory wire format" [
    testCase "Unit" <| fun () ->
      (serAndParse LiveTestingJson.categoryValue TestCategory.Unit) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Unit"
    testCase "Integration" <| fun () ->
      (serAndParse LiveTestingJson.categoryValue TestCategory.Integration) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Integration"
    testCase "Custom with value" <| fun () ->
      let root = serAndParse LiveTestingJson.categoryValue (TestCategory.Custom "smoke")
      root |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "Custom"
      let fields =
        match JsonValue.prop "Fields" root with
        | Some (JsonValue.Array items) -> items
        | _ -> []
      fields.[0] |> JsonValue.asString |> Option.defaultValue "" |> Expect.equal "value" "smoke"
  ]

  testList "CoverageHealth wire format" [
    testCase "AllPassing" <| fun () ->
      (serAndParse LiveTestingJson.healthValue CoverageHealth.AllPassing) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "AllPassing"
    testCase "SomeFailing" <| fun () ->
      (serAndParse LiveTestingJson.healthValue CoverageHealth.SomeFailing) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "SomeFailing"
  ]

  testList "CoverageStatus wire format" [
    testCase "Covered has testCount and health" <| fun () ->
      let root = serAndParse LiveTestingJson.coverageStatusValue (CoverageStatus.Covered(3, CoverageHealth.AllPassing))
      root |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "Covered"
      let fields =
        match JsonValue.prop "Fields" root with
        | Some (JsonValue.Array items) -> items
        | _ -> []
      fields.Length |> Expect.equal "2 fields" 2
      fields.[0] |> JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "testCount" 3
    testCase "NotCovered" <| fun () ->
      (serAndParse LiveTestingJson.coverageStatusValue CoverageStatus.NotCovered) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "NotCovered"
    testCase "Pending" <| fun () ->
      (serAndParse LiveTestingJson.coverageStatusValue CoverageStatus.Pending) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Pending"
  ]

  testList "LineCoverage wire format" [
    testCase "FullyCovered" <| fun () ->
      (serAndParse LiveTestingJson.lineCoverageValue LineCoverage.FullyCovered) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "FullyCovered"
    testCase "PartiallyCovered" <| fun () ->
      let root = serAndParse LiveTestingJson.lineCoverageValue (LineCoverage.PartiallyCovered(2, 5))
      root |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "case" "PartiallyCovered"
      let fields =
        match JsonValue.prop "Fields" root with
        | Some (JsonValue.Array items) -> items
        | _ -> []
      fields.[0] |> JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "covered" 2
    testCase "NotCovered" <| fun () ->
      (serAndParse LiveTestingJson.lineCoverageValue LineCoverage.NotCovered) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "NotCovered"
  ]

  testList "FileAnnotations wire format" [
    testCase "empty has all PascalCase fields" <| fun () ->
      let fa : FileAnnotations = {
        FilePath = "src/Foo.fs"; TestAnnotations = [||]
        CoverageAnnotations = [||]; InlineFailures = [||]; CodeLenses = [||]
        PerformanceAnnotations = [||]
      }
      let root = serAndParse LiveTestingJson.fileAnnotationsValue fa
      root |> JsonValue.prop "FilePath" |> Option.isSome |> Expect.isTrue "has FilePath"
      root |> JsonValue.prop "CoverageAnnotations" |> Option.isSome |> Expect.isTrue "has CoverageAnnotations"
      root |> JsonValue.prop "CodeLenses" |> Option.isSome |> Expect.isTrue "has CodeLenses"

    testCase "CoverageLineAnnotation shape" <| fun () ->
      let cla : CoverageLineAnnotation = {
        Line = 42; EndLine = 0; EndColumn = 0
        Detail = CoverageStatus.Covered(2, CoverageHealth.AllPassing)
        CoveringTestIds = [| TestId.TestId "t1"; TestId.TestId "t2" |]
        BranchCoverage = None
      }
      let root = serAndParse LiveTestingJson.coverageLineAnnotationValue cla
      root |> JsonValue.prop "Line" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "line" 42
      root |> JsonValue.prop "Detail" |> Option.bind (JsonValue.prop "Case") |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "detail" "Covered"
      let coveringIds =
        match JsonValue.prop "CoveringTestIds" root with
        | Some (JsonValue.Array items) -> items
        | _ -> []
      coveringIds.Length |> Expect.equal "ids" 2

    testCase "TestLineAnnotation shape" <| fun () ->
      let tla : TestLineAnnotation = {
        TestId = TestId.TestId "my-test"; DisplayName = "MyModule.should_work"
        Line = 10; Status = TestRunStatus.Passed(System.TimeSpan.FromMilliseconds(50.0))
        Freshness = AnnotationFreshness.Current
      }
      let root = serAndParse LiveTestingJson.testLineAnnotationValue tla
      root |> JsonValue.prop "Line" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0 |> Expect.equal "line" 10
      root |> JsonValue.prop "Status" |> Option.bind (JsonValue.prop "Case") |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "status" "Passed"

    testCase "TestCodeLens shape" <| fun () ->
      let cl : TestCodeLens = {
        TestId = TestId.TestId "my-test"; Label = "✓ Passed"
        Line = 5; Command = CodeLensCommand.RunTest
      }
      let root = serAndParse LiveTestingJson.codeLensValue cl
      root |> JsonValue.prop "Label" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.stringContains "label" "Passed"
      root |> JsonValue.prop "Command" |> Option.bind (JsonValue.prop "Case") |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "cmd" "RunTest"
  ]

  testList "BatchCompletion wire format" [
    testCase "Complete" <| fun () ->
      (serAndParse LiveTestingJson.completionValue (BatchCompletion.Complete(10, 10))) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Complete"
    testCase "Partial" <| fun () ->
      (serAndParse LiveTestingJson.completionValue (BatchCompletion.Partial(5, 10))) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Partial"
    testCase "Superseded" <| fun () ->
      (serAndParse LiveTestingJson.completionValue BatchCompletion.Superseded) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Superseded"
  ]

  testList "AnnotationFreshness wire format" [
    testCase "Current" <| fun () ->
      (serAndParse LiveTestingJson.annotationFreshnessValue AnnotationFreshness.Current) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Current"
    testCase "Stale" <| fun () ->
      (serAndParse LiveTestingJson.annotationFreshnessValue AnnotationFreshness.Stale) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Stale"
    testCase "Running" <| fun () ->
      (serAndParse LiveTestingJson.annotationFreshnessValue AnnotationFreshness.Running) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "Running"
  ]

  testList "CodeLensCommand wire format" [
    testCase "RunTest" <| fun () ->
      (serAndParse LiveTestingJson.lensCommandValue CodeLensCommand.RunTest) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "RunTest"
    testCase "DebugTest" <| fun () ->
      (serAndParse LiveTestingJson.lensCommandValue CodeLensCommand.DebugTest) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "DebugTest"
    testCase "ShowHistory" <| fun () ->
      (serAndParse LiveTestingJson.lensCommandValue CodeLensCommand.ShowHistory) |> JsonValue.prop "Case" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "case" "ShowHistory"
  ]
]

[<Tests>]
let sessionScopingTests = testList "SSE Session Scoping" [
  testList "injectSessionId" [
    testCase "None returns json unchanged" <| fun () ->
      let json = """{"Total":5}"""
      injectSessionId None json
      |> Expect.equal "unchanged" """{"Total":5}"""

    testCase "Some prepends SessionId field" <| fun () ->
      let json = """{"Total":5}"""
      let result = injectSessionId (Some "sess-123") json
      result |> Expect.stringContains "has SessionId" "\"SessionId\":\"sess-123\""

    testCase "injected json is valid JSON" <| fun () ->
      let json = """{"Total":5,"Passed":3}"""
      let result = injectSessionId (Some "abc") json
      let doc = Json.parseOrFail result
      doc |> JsonValue.prop "SessionId" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "sessionId" "abc"
      doc |> JsonValue.prop "Total" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0
      |> Expect.equal "total preserved" 5

    testCase "session identity cannot inject JSON fields or SSE lines" <| fun () ->
      let sid = "quote\"\nslash\\"
      let doc = Json.parseOrFail (injectSessionId (Some sid) "{\"Total\":9007199254740993}")
      doc |> JsonValue.prop "SessionId" |> Option.bind JsonValue.asString |> Option.defaultValue "" |> Expect.equal "identity preserved" sid
      doc |> JsonValue.prop "Total" |> Option.bind JsonValue.asInt |> Option.defaultValue 0L |> Expect.equal "exact integer preserved" 9007199254740993L
      doc |> JsonValue.keys |> List.length |> Expect.equal "no injected properties" 2

    testCase "non-object json returned unchanged" <| fun () ->
      injectSessionId (Some "abc") "[1,2,3]"
      |> Expect.equal "unchanged" "[1,2,3]"
  ]

  testList "format functions with sessionId" [
    testCase "formatTestSummaryEvent None has no SessionId" <| fun () ->
      let summary: TestSummary = {
        Total = 10; Passed = 8; Failed = 1; Stale = 1; Running = 0; Disabled = 0; Enabled = true
      }
      let result = formatTestSummaryEvent productionSseOpts None summary None
      result.Contains("SessionId") |> Expect.isFalse "no SessionId"

    testCase "formatTestSummaryEvent Some injects SessionId" <| fun () ->
      let summary: TestSummary = {
        Total = 10; Passed = 8; Failed = 1; Stale = 1; Running = 0; Disabled = 0; Enabled = true
      }
      let result = formatTestSummaryEvent productionSseOpts (Some "sess-456") summary None
      let data = extractSseData result |> Option.get
      let doc = Json.parseOrFail data
      doc |> JsonValue.prop "SessionId" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "sessionId" "sess-456"
      doc |> JsonValue.prop "Total" |> Option.bind JsonValue.asInt |> Option.map int |> Option.defaultValue 0
      |> Expect.equal "total preserved" 10

    testCase "formatTestResultsBatchEvent Some injects SessionId" <| fun () ->
      let payload: TestResultsBatchPayload = {
        Generation = RunGeneration 1
        Freshness = ResultFreshness.Fresh
        Completion = BatchCompletion.Complete(5, 5)
        Entries = [||]
        Summary = { Total = 5; Passed = 5; Failed = 0; Stale = 0; Running = 0; Disabled = 0; Enabled = true }
        LastDecision = None
      }
      let result = formatTestResultsBatchEvent productionSseOpts (Some "sess-789") payload
      let data = extractSseData result |> Option.get
      let doc = Json.parseOrFail data
      doc |> JsonValue.prop "SessionId" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "sessionId" "sess-789"

    testCase "formatTestSummaryEvent includes last decision when provided" <| fun () ->
      let summary: TestSummary = {
        Total = 2; Passed = 1; Failed = 0; Stale = 1; Running = 0; Disabled = 0; Enabled = true
      }
      let decision =
        LiveTestingDecision.fromSelection
          (RerunCause.KeystrokeBuffered "src/Module.fs")
          SelectionPrecision.CoverageApproximation
          [ "Module.add" ]
          [| "Module.Tests.should_add"; "Module.Tests.should_guard_edges" |]
          [||]
          "coverage widened"
      let result = formatTestSummaryEvent productionSseOpts None summary (Some decision)
      let data = extractSseData result |> Option.get
      let doc = Json.parseOrFail data
      let lastDecision = doc |> JsonValue.prop "LastDecision" |> Option.defaultValue JsonValue.Null
      lastDecision |> JsonValue.prop "Precision" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "precision should be serialized" "coverage_approximation"
      lastDecision |> JsonValue.prop "Trust" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "trust should be serialized" "fresh_approximate"

    testCase "formatTestResultsBatchEvent includes last decision when provided" <| fun () ->
      let decision =
        LiveTestingDecision.fromSelection
          (RerunCause.FileSaved "src/Compiled.fs")
          SelectionPrecision.ConservativeFallback
          []
          [| "Compiled.Tests.should_build_a" |]
          [||]
          "fallback rebuild"
      let payload: TestResultsBatchPayload = {
        Generation = RunGeneration 1
        Freshness = ResultFreshness.Fresh
        Completion = BatchCompletion.Complete(1, 1)
        Entries = [||]
        Summary = { Total = 1; Passed = 1; Failed = 0; Stale = 0; Running = 0; Disabled = 0; Enabled = true }
        LastDecision = Some decision
      }
      let result = formatTestResultsBatchEvent productionSseOpts None payload
      let data = extractSseData result |> Option.get
      let doc = Json.parseOrFail data
      let lastDecision = doc |> JsonValue.prop "LastDecision" |> Option.defaultValue JsonValue.Null
      lastDecision |> JsonValue.prop "Cause" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "cause should be serialized" "file_saved"
      lastDecision |> JsonValue.prop "Precision" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "precision should be serialized" "conservative_fallback"

    testCase "formatFileAnnotationsEvent Some injects SessionId" <| fun () ->
      let fa : FileAnnotations = {
        FilePath = "src/Foo.fs"; TestAnnotations = [||]
        CoverageAnnotations = [||]; InlineFailures = [||]; CodeLenses = [||]
        PerformanceAnnotations = [||]
      }
      let result = formatFileAnnotationsEvent productionSseOpts (Some "sess-abc") fa
      let data = extractSseData result |> Option.get
      let doc = Json.parseOrFail data
      doc |> JsonValue.prop "SessionId" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "sessionId" "sess-abc"
      doc |> JsonValue.prop "FilePath" |> Option.bind JsonValue.asString |> Option.defaultValue ""
      |> Expect.equal "path preserved" "src/Foo.fs"
  ]
]

let private mkSp file line col bid : SequencePoint =
  { File = file; Line = line; Column = col; EndLine = 0; EndColumn = 0; BranchId = bid }

[<Tests>]
let branchCoverageTests = testList "Branch Coverage Wiring" [
  testList "CoverageBitmap.union" [
    testCase "union of disjoint bitmaps" <| fun () ->
      let a = CoverageBitmap.ofBoolArray [| true; false; false; false |]
      let b = CoverageBitmap.ofBoolArray [| false; true; false; false |]
      CoverageBitmap.union a b |> CoverageBitmap.toBoolArray
      |> Expect.equal "combined" [| true; true; false; false |]

    testCase "union of empty" <| fun () ->
      CoverageBitmap.union CoverageBitmap.empty CoverageBitmap.empty
      |> fun r -> r.Count |> Expect.equal "count" 0
  ]

  testList "computeLineCoverageForFile" [
    testCase "partial coverage returns PartiallyCovered" <| fun () ->
      let map : InstrumentationMap = {
        Slots = [| mkSp "f.fs" 10 0 0; mkSp "f.fs" 10 10 1; mkSp "f.fs" 10 20 2 |]
        TotalProbes = 3; TrackerTypeName = "t"; HitsFieldName = "h"
      }
      let bm = CoverageBitmap.ofBoolArray [| true; false; true |]
      let bms = Map.ofList [ TestId.TestId "t1", bm ]
      CoverageBitmap.computeLineCoverageForFile "f.fs" [| map |] bms
      |> Map.tryFind 10
      |> Expect.equal "partial" (Some (LineCoverage.PartiallyCovered(2, 3)))

    testCase "all probes hit returns FullyCovered" <| fun () ->
      let map : InstrumentationMap = {
        Slots = [| mkSp "f.fs" 10 0 0; mkSp "f.fs" 10 10 1 |]
        TotalProbes = 2; TrackerTypeName = "t"; HitsFieldName = "h"
      }
      let bm = CoverageBitmap.ofBoolArray [| true; true |]
      let bms = Map.ofList [ TestId.TestId "t1", bm ]
      CoverageBitmap.computeLineCoverageForFile "f.fs" [| map |] bms
      |> Map.tryFind 10
      |> Expect.equal "full" (Some LineCoverage.FullyCovered)

    testCase "no probes hit returns NotCovered" <| fun () ->
      let map : InstrumentationMap = {
        Slots = [| mkSp "f.fs" 10 0 0; mkSp "f.fs" 10 10 1 |]
        TotalProbes = 2; TrackerTypeName = "t"; HitsFieldName = "h"
      }
      let bm = CoverageBitmap.ofBoolArray [| false; false |]
      let bms = Map.ofList [ TestId.TestId "t1", bm ]
      CoverageBitmap.computeLineCoverageForFile "f.fs" [| map |] bms
      |> Map.tryFind 10
      |> Expect.equal "not covered" (Some LineCoverage.NotCovered)

    testCase "multiple tests OR coverage together" <| fun () ->
      let map : InstrumentationMap = {
        Slots = [| mkSp "f.fs" 10 0 0; mkSp "f.fs" 10 10 1; mkSp "f.fs" 10 20 2 |]
        TotalProbes = 3; TrackerTypeName = "t"; HitsFieldName = "h"
      }
      let bm1 = CoverageBitmap.ofBoolArray [| true; false; false |]
      let bm2 = CoverageBitmap.ofBoolArray [| false; false; true |]
      let bms = Map.ofList [ TestId.TestId "t1", bm1; TestId.TestId "t2", bm2 ]
      CoverageBitmap.computeLineCoverageForFile "f.fs" [| map |] bms
      |> Map.tryFind 10
      |> Expect.equal "OR'd partial" (Some (LineCoverage.PartiallyCovered(2, 3)))

    testCase "empty maps returns empty" <| fun () ->
      CoverageBitmap.computeLineCoverageForFile "f.fs" [||] Map.empty
      |> Expect.isEmpty "empty"

    testCase "no matching bitmaps returns empty" <| fun () ->
      let map : InstrumentationMap = {
        Slots = [| mkSp "f.fs" 10 0 0 |]
        TotalProbes = 1; TrackerTypeName = "t"; HitsFieldName = "h"
      }
      CoverageBitmap.computeLineCoverageForFile "f.fs" [| map |] Map.empty
      |> Expect.isEmpty "empty"

    testCase "different file returns empty" <| fun () ->
      let map : InstrumentationMap = {
        Slots = [| mkSp "other.fs" 10 0 0 |]
        TotalProbes = 1; TrackerTypeName = "t"; HitsFieldName = "h"
      }
      let bm = CoverageBitmap.ofBoolArray [| true |]
      let bms = Map.ofList [ TestId.TestId "t1", bm ]
      CoverageBitmap.computeLineCoverageForFile "f.fs" [| map |] bms
      |> Expect.isEmpty "empty"
  ]

  testList "formatRetryHint" [
    testCase "formats retry hint per SSE spec" <| fun () ->
      formatRetryHint 3000
      |> Expect.equal "should be retry: N\\n\\n" "retry: 3000\n\n"

    testCase "formats 1ms retry hint" <| fun () ->
      formatRetryHint 1
      |> Expect.equal "minimum positive" "retry: 1\n\n"

    testCase "formats large retry value" <| fun () ->
      formatRetryHint 60000
      |> Expect.equal "60 second retry" "retry: 60000\n\n"
  ]
]
