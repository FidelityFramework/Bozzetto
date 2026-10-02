namespace Bozzetto

open Fidelity.Data.JSON
open Bozzetto.Features.LiveTesting
open WireJson

/// Shared explicit schema for the live-testing HTTP, SSE and MCP read models.
module LiveTestingJson =
  let testIdValue value = TestId.value value |> text

  let frameworkValue (_casing: JsonCasing) (value: TestFramework) =
    match value with
    | TestFramework.Expecto -> union "Expecto" []
    | TestFramework.XUnit -> union "XUnit" []
    | TestFramework.NUnit -> union "NUnit" []
    | TestFramework.MSTest -> union "MSTest" []
    | TestFramework.TUnit -> union "TUnit" []
    | TestFramework.Unknown a -> union "Unknown" [text a]

  let categoryValue (_casing: JsonCasing) (value: TestCategory) =
    match value with
    | TestCategory.Unit -> union "Unit" []
    | TestCategory.Integration -> union "Integration" []
    | TestCategory.Browser -> union "Browser" []
    | TestCategory.Benchmark -> union "Benchmark" []
    | TestCategory.Architecture -> union "Architecture" []
    | TestCategory.Property -> union "Property" []
    | TestCategory.Custom a -> union "Custom" [text a]

  let policyValue (_casing: JsonCasing) (value: RunPolicy) =
    match value with
    | RunPolicy.OnEveryChange -> union "OnEveryChange" []
    | RunPolicy.OnSaveOnly -> union "OnSaveOnly" []
    | RunPolicy.OnDemand -> union "OnDemand" []
    | RunPolicy.Disabled -> union "Disabled" []

  let originValue (_casing: JsonCasing) (value: TestOrigin) =
    match value with
    | TestOrigin.SourceMapped (a, b) -> union "SourceMapped" [text a; integer b]
    | TestOrigin.ReflectionOnly -> union "ReflectionOnly" []

  let failureValue (_casing: JsonCasing) (value: TestFailure) =
    match value with
    | TestFailure.AssertionFailed a -> union "AssertionFailed" [text a]
    | TestFailure.ExceptionThrown (a, b) -> union "ExceptionThrown" [text a; text b]
    | TestFailure.TimedOut a -> union "TimedOut" [duration a]

  let statusValue (_casing: JsonCasing) (value: TestRunStatus) =
    match value with
    | TestRunStatus.Detected -> union "Detected" []
    | TestRunStatus.Queued -> union "Queued" []
    | TestRunStatus.Running -> union "Running" []
    | TestRunStatus.Passed a -> union "Passed" [duration a]
    | TestRunStatus.Failed (a, b) -> union "Failed" [failureValue _casing a; duration b]
    | TestRunStatus.Skipped a -> union "Skipped" [text a]
    | TestRunStatus.Stale -> union "Stale" []
    | TestRunStatus.PolicyDisabled -> union "PolicyDisabled" []

  let historyValue (_casing: JsonCasing) (value: RunHistory) =
    match value with
    | RunHistory.NeverRun -> union "NeverRun" []
    | RunHistory.PreviousRun a -> union "PreviousRun" [duration a]

  let freshnessValue (_casing: JsonCasing) (value: ResultFreshness) =
    match value with
    | ResultFreshness.Fresh -> union "Fresh" []
    | ResultFreshness.StaleCodeEdited -> union "StaleCodeEdited" []
    | ResultFreshness.StaleWrongGeneration -> union "StaleWrongGeneration" []

  let completionValue (_casing: JsonCasing) (value: BatchCompletion) =
    match value with
    | BatchCompletion.Complete (a, b) -> union "Complete" [integer a; integer b]
    | BatchCompletion.Partial (a, b) -> union "Partial" [integer a; integer b]
    | BatchCompletion.Superseded -> union "Superseded" []

  let annotationFreshnessValue (_casing: JsonCasing) (value: AnnotationFreshness) =
    match value with
    | AnnotationFreshness.Current -> union "Current" []
    | AnnotationFreshness.Stale -> union "Stale" []
    | AnnotationFreshness.Running -> union "Running" []

  let healthValue (_casing: JsonCasing) (value: CoverageHealth) =
    match value with
    | CoverageHealth.AllPassing -> union "AllPassing" []
    | CoverageHealth.SomeFailing -> union "SomeFailing" []

  let coverageStatusValue (_casing: JsonCasing) (value: CoverageStatus) =
    match value with
    | CoverageStatus.Covered (a, b) -> union "Covered" [integer a; healthValue _casing b]
    | CoverageStatus.NotCovered -> union "NotCovered" []
    | CoverageStatus.Pending -> union "Pending" []

  let lineCoverageValue (_casing: JsonCasing) (value: LineCoverage) =
    match value with
    | LineCoverage.FullyCovered -> union "FullyCovered" []
    | LineCoverage.PartiallyCovered (a, b) -> union "PartiallyCovered" [integer a; integer b]
    | LineCoverage.NotCovered -> union "NotCovered" []

  let failurePresentationValue (_casing: JsonCasing) (value: FailurePresentation) =
    match value with
    | FailurePresentation.AssertionDiff (a, b) -> union "AssertionDiff" [text a; text b]
    | FailurePresentation.ExceptionMessage (a, b) -> union "ExceptionMessage" [text a; text b]
    | FailurePresentation.Timeout a -> union "Timeout" [duration a]
    | FailurePresentation.RawMessage a -> union "RawMessage" [text a]

  let lensCommandValue (_casing: JsonCasing) (value: CodeLensCommand) =
    match value with
    | CodeLensCommand.RunTest -> union "RunTest" []
    | CodeLensCommand.DebugTest -> union "DebugTest" []
    | CodeLensCommand.ShowHistory -> union "ShowHistory" []

  let overflowValue (_casing: JsonCasing) (value: Overflow) =
    match value with
    | Overflow.Within -> union "Within" []
    | Overflow.Overflow a -> union "Overflow" [integer a]

  let viewStateValue (_casing: JsonCasing) (value: CoverageViewState) =
    match value with
    | CoverageViewState.Passing -> union "Passing" []
    | CoverageViewState.Failing -> union "Failing" []
    | CoverageViewState.Running -> union "Running" []
    | CoverageViewState.Stale -> union "Stale" []
    | CoverageViewState.Skipped -> union "Skipped" []
    | CoverageViewState.Absent -> union "Absent" []

  let summaryValue casing (value: TestSummary) =
    objectValue casing [
      "Total", integer value.Total
      "Passed", integer value.Passed
      "Failed", integer value.Failed
      "Stale", integer value.Stale
      "Running", integer value.Running
      "Disabled", integer value.Disabled
      "Enabled", boolean value.Enabled
    ]

  let statusEntryValue casing (value: TestStatusEntry) =
    objectValue casing [
      "TestId", testIdValue value.TestId
      "DisplayName", text value.DisplayName
      "FullName", text value.FullName
      "Origin", originValue casing value.Origin
      "Framework", frameworkValue casing value.Framework
      "Category", categoryValue casing value.Category
      "CurrentPolicy", policyValue casing value.CurrentPolicy
      "Status", statusValue casing value.Status
      "PreviousStatus", statusValue casing value.PreviousStatus
    ]

  let sourceLocationValue casing (value: TestSourceLocation) =
    objectValue casing [
      "CellId", integer value.CellId
      "TestName", text value.TestName
      "FilePath", text value.FilePath
      "StartLine", integer value.StartLine
      "EndLine", integer value.EndLine
    ]

  let propertyViolationValue casing (value: PropertyViolationDetail) =
    objectValue casing [
      "PropertyName", optional text value.PropertyName
      "ShrunkCounterexample", text value.ShrunkCounterexample
      "AlgebraicCategory", optional text value.AlgebraicCategory
    ]

  let testLineAnnotationValue casing (value: TestLineAnnotation) =
    objectValue casing [
      "Line", integer value.Line
      "TestId", testIdValue value.TestId
      "DisplayName", text value.DisplayName
      "Status", statusValue casing value.Status
      "Freshness", annotationFreshnessValue casing value.Freshness
    ]

  let coverageLineAnnotationValue casing (value: CoverageLineAnnotation) =
    objectValue casing [
      "Line", integer value.Line
      "EndLine", integer value.EndLine
      "EndColumn", integer value.EndColumn
      "Detail", coverageStatusValue casing value.Detail
      "CoveringTestIds", array testIdValue value.CoveringTestIds
      "BranchCoverage", optional (lineCoverageValue casing) value.BranchCoverage
    ]

  let inlineFailureValue casing (value: InlineFailure) =
    objectValue casing [
      "Line", integer value.Line
      "TestId", testIdValue value.TestId
      "TestName", text value.TestName
      "Failure", failurePresentationValue casing value.Failure
      "Duration", duration value.Duration
    ]

  let codeLensValue casing (value: TestCodeLens) =
    objectValue casing [
      "Line", integer value.Line
      "Label", text value.Label
      "TestId", testIdValue value.TestId
      "Command", lensCommandValue casing value.Command
    ]

  let performanceAnnotationValue casing (value: PerformanceAnnotation) =
    objectValue casing [
      "Line", integer value.Line
      "CellIndex", integer value.CellIndex
      "DurationsMs", array number value.DurationsMs
      "Sparkline", text value.Sparkline
      "P50Ms", number value.P50Ms
      "P95Ms", number value.P95Ms
    ]

  let fileAnnotationsValue casing (value: FileAnnotations) =
    objectValue casing [
      "FilePath", text value.FilePath
      "TestAnnotations", array (testLineAnnotationValue casing) value.TestAnnotations
      "CoverageAnnotations", array (coverageLineAnnotationValue casing) value.CoverageAnnotations
      "InlineFailures", array (inlineFailureValue casing) value.InlineFailures
      "CodeLenses", array (codeLensValue casing) value.CodeLenses
      "PerformanceAnnotations", array (performanceAnnotationValue casing) value.PerformanceAnnotations
    ]

  let coverageViewValue casing (value: CoverageView) =
    objectValue casing [
      "Symbol", text value.Symbol
      "FilePath", text value.FilePath
      "DefinitionLine", integer value.DefinitionLine
      "TotalCount", integer value.TotalCount
      "Overflow", overflowValue casing value.Overflow
      "InlineBadgeText", text value.InlineBadgeText
      "Health", viewStateValue casing value.Health
    ]

  let decisionValue casing (decision: LiveTestingDecision) =
    let value = LiveTestingDecision.toWireModel decision
    objectValue casing [
      "Cause", text value.Cause
      "ChangedSymbols", array text value.ChangedSymbols
      "DeferredTests", array text value.DeferredTests
      "FilePath", text value.FilePath
      "Precision", text value.Precision
      "Reason", text value.Reason
      "SelectedTests", array text value.SelectedTests
      "Trust", text value.Trust
    ]

  let batchValue casing (value: TestResultsBatchPayload) =
    objectValue casing [
      "Completion", completionValue casing value.Completion
      "Entries", array (statusEntryValue casing) value.Entries
      "Freshness", freshnessValue casing value.Freshness
      "Generation", integer (RunGeneration.value value.Generation)
      "LastDecision", optional (decisionValue casing) value.LastDecision
      "Summary", summaryValue casing value.Summary
    ]

  let bindingValue casing (value: Features.FsiOutputParser.BindingValue) =
    objectValue casing [
      "Name", text value.Name
      "TypeSig", text value.TypeSig
      "DisplayValue", text value.DisplayValue
      "IsTruncated", boolean value.IsTruncated
      "IsFunctionValue", boolean value.IsFunctionValue
      "CellIndex", integer value.CellIndex
      "EvalDurationMs", number value.EvalDurationMs
      "SourceLine", integer value.SourceLine
    ]

  let liveSnapshotValue casing (value: Features.LiveValueTree.LiveValueSnapshot) =
    let kindName = function
      | Features.LiveValueTree.NodeKind.Leaf -> "Leaf"
      | Features.LiveValueTree.NodeKind.Record -> "Record"
      | Features.LiveValueTree.NodeKind.List -> "List"
      | Features.LiveValueTree.NodeKind.Map -> "Map"
      | Features.LiveValueTree.NodeKind.Option -> "Option"
      | Features.LiveValueTree.NodeKind.Union -> "Union"
      | Features.LiveValueTree.NodeKind.Tuple -> "Tuple"
      | Features.LiveValueTree.NodeKind.Array -> "Array"
      | Features.LiveValueTree.NodeKind.Class -> "Class"
      | Features.LiveValueTree.NodeKind.Closure -> "Closure"
      | Features.LiveValueTree.NodeKind.Cycle -> "Cycle"
      | Features.LiveValueTree.NodeKind.Truncated -> "Truncated"
    let rec node (value: Features.LiveValueTree.LiveValueNode) =
      objectValue casing [
        "Label", text value.Label
        "TypeName", text value.TypeName
        "Preview", text value.Preview
        "Kind", union (kindName value.Kind) []
        "Children", array node value.Children
        "BestEffort", boolean value.BestEffort
        "Depth", integer value.Depth
      ]
    objectValue casing [
      "SessionId", text value.SessionId
      "Generation", signed value.Generation
      "Bindings", array (fun (binding: Features.LiveValueTree.LiveBindingValue) -> objectValue casing [
        "Name", text binding.Name
        "TypeSignature", text binding.TypeSignature
        "Root", node binding.Root
      ]) value.Bindings
      "Truncated", boolean value.Truncated
      "CapturedAt", timestamp value.CapturedAt
    ]
