// Test script for state change handler extraction
// Load via SageFs: #load "scripts/test-state-handlers.fsx"

open SageFs.McpPushNotifications
open SageFs.Features.Diagnostics
open SageFs.Features.LiveTesting
open Expecto
open Expecto.Flip

// ─── Pure state machine types ───

/// Immutable state for the model-change handler pipeline.
/// Replaces 7 mutable locals in startMcpServer.
type ModelChangeState = {
  LastDiagCount: int
  LastOutputCount: int
  LastTestSsePushTicks: int64
  LastTestTraceJson: string
  TestSseThrottleMs: int64
}

module ModelChangeState =
  let empty = {
    LastDiagCount = 0
    LastOutputCount = 0
    LastTestSsePushTicks = 0L
    LastTestTraceJson = ""
    TestSseThrottleMs = 250L
  }

/// Effects produced by processing a model change.
type ModelChangeEffect =
  | AccumulatePush of PushEvent
  | BroadcastTestSse of string

// ─── Pure helper: extract error diagnostics ───

let extractDiagErrors (diagnostics: Map<string, Diagnostic list>) : (string * int * string) list =
  diagnostics
  |> Map.toList
  |> List.collect (fun (_, diags) ->
    diags
    |> List.filter (fun d -> d.Severity = DiagnosticSeverity.Error)
    |> List.map (fun d -> ("fsi", d.Range.StartLine, d.Message)))

// ─── Pure helper: diagnostics dirty check ───

let processDiagnosticsChange
  (diagCount: int)
  (diagnostics: Map<string, Diagnostic list>)
  (state: ModelChangeState)
  : ModelChangeState * ModelChangeEffect list =
  match diagCount <> state.LastDiagCount with
  | true ->
    let errors = extractDiagErrors diagnostics
    { state with LastDiagCount = diagCount },
    [ AccumulatePush (PushEvent.DiagnosticsChanged errors) ]
  | false ->
    state, []

// ─── Pure helper: throttle decision ───

/// Decide whether a test summary SSE push should go through.
/// Returns true when elapsed >= throttleMs OR run is complete.
let shouldPushTestSummary (nowTicks: int64) (lastPushTicks: int64) (throttleMs: int64) (isRunComplete: bool) : bool =
  let elapsedMs = (nowTicks - lastPushTicks) * 1000L / System.Diagnostics.Stopwatch.Frequency
  elapsedMs >= throttleMs || isRunComplete

// ─── RED Tests ───

let mkDiag severity line msg : Diagnostic =
  { Severity = severity
    Range = { StartLine = line; StartColumn = 0; EndLine = line; EndColumn = 0 }
    Subcategory = ""
    Message = msg }

let extractDiagErrorsTests = testList "extractDiagErrors" [
  test "empty diagnostics yields empty list" {
    extractDiagErrors Map.empty
    |> Expect.isEmpty "should be empty"
  }

  test "filters out warnings, keeps errors" {
    let diags = Map.ofList [
      "file1.fs", [
        mkDiag DiagnosticSeverity.Warning 1 "warn1"
        mkDiag DiagnosticSeverity.Error 2 "err1"
        mkDiag DiagnosticSeverity.Info 3 "info1"
      ]
    ]
    let errors = extractDiagErrors diags
    errors |> Expect.hasLength "should have 1 error" 1
    errors.[0] |> Expect.equal "should be err1" ("fsi", 2, "err1")
  }

  test "collects errors across multiple files" {
    let diags = Map.ofList [
      "a.fs", [ mkDiag DiagnosticSeverity.Error 1 "errA" ]
      "b.fs", [ mkDiag DiagnosticSeverity.Error 5 "errB" ]
    ]
    let errors = extractDiagErrors diags
    errors |> Expect.hasLength "should have 2 errors" 2
  }
]

let processDiagnosticsChangeTests = testList "processDiagnosticsChange" [
  test "no change when diagCount unchanged" {
    let state = { ModelChangeState.empty with LastDiagCount = 3 }
    let state', effects = processDiagnosticsChange 3 Map.empty state
    effects |> Expect.isEmpty "no effects when count unchanged"
    state'.LastDiagCount |> Expect.equal "state unchanged" 3
  }

  test "produces AccumulatePush when diagCount changes" {
    let state = ModelChangeState.empty
    let diags = Map.ofList [
      "f.fs", [ mkDiag DiagnosticSeverity.Error 10 "type mismatch" ]
    ]
    let state', effects = processDiagnosticsChange 1 diags state
    state'.LastDiagCount |> Expect.equal "updated count" 1
    effects |> Expect.hasLength "one effect" 1
    match effects.[0] with
    | AccumulatePush (PushEvent.DiagnosticsChanged errors) ->
      errors |> Expect.hasLength "one error" 1
    | other -> failwith (sprintf "unexpected effect: %A" other)
  }

  test "count change with no errors still produces event" {
    let state = ModelChangeState.empty
    let state', effects = processDiagnosticsChange 5 Map.empty state
    state'.LastDiagCount |> Expect.equal "updated to 5" 5
    effects |> Expect.hasLength "one effect" 1
    match effects.[0] with
    | AccumulatePush (PushEvent.DiagnosticsChanged errors) ->
      errors |> Expect.isEmpty "empty error list"
    | other -> failwith (sprintf "unexpected effect: %A" other)
  }
]

let throttleTests = testList "shouldPushTestSummary" [
  test "push when elapsed exceeds throttle" {
    let freq = System.Diagnostics.Stopwatch.Frequency
    let lastPush = 0L
    let now = freq // 1 second later
    shouldPushTestSummary now lastPush 250L false
    |> Expect.isTrue "1s > 250ms should push"
  }

  test "suppress when within throttle window" {
    let freq = System.Diagnostics.Stopwatch.Frequency
    let lastPush = 0L
    let now = freq / 10L // 100ms later
    shouldPushTestSummary now lastPush 250L false
    |> Expect.isFalse "100ms < 250ms should suppress"
  }

  test "always push when run is complete" {
    let lastPush = 0L
    let now = 1L // basically no time elapsed
    shouldPushTestSummary now lastPush 250L true
    |> Expect.isTrue "run complete always pushes"
  }
]

let allTests = testList "StateChangeHandler" [
  extractDiagErrorsTests
  processDiagnosticsChangeTests
  throttleTests
]

// Run
Expecto.Tests.runTestsWithCLIArgs [] [||] allTests
