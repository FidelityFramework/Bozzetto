module Bozzetto.Tests.McpFrictionSummaryToolTests

open System
open Fidelity.Data.JSON
open Expecto
open Expecto.Flip
open FsCheck
open Microsoft.Extensions.Logging.Abstractions
open Bozzetto.Server.McpTools
open Bozzetto.Tests.TestInfrastructure
open Bozzetto.Features.FrictionTelemetryTypes
open Bozzetto.Features.ObservedFrictionTypes

let private getProp (name: string) (v: JsonValue) =
  match JsonValue.prop name v with
  | Some p -> p
  | None -> failtestf "missing property '%s' in %s" name (Json.serialize v)

let private getStr (name: string) (v: JsonValue) =
  match getProp name v |> JsonValue.asString with
  | Some s -> s
  | None -> failtestf "property '%s' is not a string" name

let private getInt (name: string) (v: JsonValue) =
  match getProp name v |> JsonValue.asInt with
  | Some n -> int n
  | None -> failtestf "property '%s' is not an int" name

let private getArr (name: string) (v: JsonValue) =
  match getProp name v with
  | JsonValue.Array items -> items
  | _ -> failtestf "property '%s' is not an array" name

let private ok = function
  | Ok value -> value
  | Error err -> failwith err

/// Local event builder mirroring ObservedFrictionTypesTests.event. NOT
/// shared with that module: Bozzetto.Tests.fsproj compiles this file before
/// ObservedFrictionTypesTests.fs, so `open`ing it here is a compile-order
/// landmine (the same class of hazard as this brief's Core-side finding) —
/// duplicating this tiny builder avoids it without reordering the fsproj.
let private baseTimeUtc = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)

let private mkEvent (tool: string) (outcome: FrictionOutcome) (atUtcOffsetSeconds: float) : FrictionEvent =
  { OccurredAtUtc = baseTimeUtc.AddSeconds atUtcOffsetSeconds
    Session = SessionRef.create "mcp" |> ok
    Tool = ToolName.create tool |> ok
    Intent = IntentKind.ExploreCode
    Outcome = outcome
    Duration = DurationMs.create 1 |> ok
    FollowUp = FollowUp.NoFollowUpYet
    ContextCost = ContextCost.Focused
    BozzettoVersion = ""; AgentKey = ""; ErrorSignature = "" }

/// The tool the recorded harvest burst was captured under, read from the
/// product's retired-tool vocabulary so this copy cannot name something the
/// other replay tests do not. `reportDirect` uses the LIVE default, so the two
/// B7 tests below pass the matching config explicitly via `reportDirectWith`.
let private historicalPollingTool =
  Bozzetto.Affordances.RetiredTool.toToolName Bozzetto.Affordances.RetiredTool.GetFsiStatus

let private harvestReplayConfig : DetectorConfig =
  { DetectorConfig.defaults with PollingTools = Set.ofList [ historicalPollingTool ] }

/// The harvest replay pattern (observed-friction-plan.md §e /
/// ObservedFrictionAcceptanceTests.fs): 4x "unknown (missing argument)"
/// unattributed failures + a 72x polling burst under the tool name the
/// harvest was recorded under. Reused here
/// to prove the SAME signals reach the daemon's `reportDirect` read model
/// (Brief B7), not just the pure `detectAll` engine.
let private harvestReplayEvents : FrictionEvent list =
  let unattributed =
    List.init 4 (fun i ->
      mkEvent "unknown (missing argument)" (FrictionOutcome.EncounteredBlocker BlockerKind.InvalidRequest) (float i))
  let polling =
    List.init 72 (fun i -> mkEvent historicalPollingTool FrictionOutcome.CompletedCleanly (10.0 + float i))
  unattributed @ polling

let private seedStore (store: Bozzetto.Features.FrictionSqlite.FrictionStore) (events: FrictionEvent list) = task {
  for e in events do
    let! (result: Result<unit, string>) = Bozzetto.Features.McpFrictionRecorder.Recorder.appendEventDirect store e
    match result with
    | Ok () -> ()
    | Error err -> failwithf "failed to seed friction store: %s" err
}

[<Tests>]
let tests =
  testList "MCP friction summary tool" [
    testCaseTask "get_friction_summary reports compact local counts" <| fun () -> task {
      let baseCtx = sharedCtx ()
      let tools = BozzettoTools(baseCtx, NullLogger<BozzettoTools>.Instance)

      let! _ = tools.report_friction("run_tests", "output_too_large", "Too much output to inspect.", "")
      let! summary = tools.get_friction_summary()

      summary |> Expect.stringContains "should count blocker families" "Top blockers:"
      summary |> Expect.stringContains "should count tracked tools" "Tracked tools:"
      summary |> Expect.stringContains "should count explicit feedback" "Explicit feedback items:"
    }

    testCaseTask "get_friction_report returns ranked actionable local friction as JSON" <| fun () -> task {
      let baseCtx = sharedCtx ()
      let tools = BozzettoTools(baseCtx, NullLogger<BozzettoTools>.Instance)

      let! _ = tools.report_friction("run_tests", "needed_another_tool", "Exact test name was not obvious.", "list_tests")
      let! reportJson = tools.get_friction_report()

      let root = Json.parseOrFail reportJson
      getInt "TotalFeedbackItems" root
      |> Expect.equal "report should expose feedback count" 1

      let topTools = getArr "HighestPriorityTools" root
      (topTools.Length, 0)
      |> Expect.isGreaterThan "report should expose at least one actionable tool"

      let top = topTools[0]
      getStr "Tool" top
      |> Expect.equal "feedback-heavy complained-about tool should rise to the top" "run_tests"
      getStr "SuggestedFixTarget" top
      |> Expect.stringContains "top remediation target should reflect the resolving follow-up" "list_tests"

      let recentFeedback = getArr "RecentFeedback" root
      (recentFeedback.Length, 0)
      |> Expect.isGreaterThan "report should expose recent explicit complaints"

      let firstFeedback = recentFeedback[0]
      getStr "Tool" firstFeedback
      |> Expect.equal "recent feedback should preserve the complained-about tool" "run_tests"
      getStr "LatestAlternative" firstFeedback
      |> Expect.equal "recent feedback should preserve the resolving alternative" "list_tests"

      let recommended = getArr "RecommendedWorkItems" root
      (recommended.Length, 0)
      |> Expect.isGreaterThan "report should expose recommended work items"
      let firstWorkItem = recommended[0]
      getStr "TargetTool" firstWorkItem
      |> Expect.equal "first recommended work item should point at the complained-about tool" "run_tests"
      getStr "LikelyFixType" firstWorkItem
      |> Expect.equal "recommended work item should classify the fix shape" "workflow-linking"
      getStr "SuggestedAction" firstWorkItem
      |> Expect.stringContains "recommended action should preserve the resolving alternative" "list_tests"
    }

    testCase "friction JSON preserves null alternatives, escaped feedback and every signal's evidence" <| fun _ ->
      let tool = ToolName.create "run_tests" |> ok
      let escaped = "quoted \"feedback\"\nnext\\line\tλ"
      let report: Bozzetto.Features.FrictionTelemetry.FrictionReport =
        { TotalEvents = Int32.MaxValue
          TotalFeedbackItems = 1
          HighestPriorityTools =
            [ { Tool = tool; TotalInvocations = 3; BlockedCount = 2; AbandonedCount = 1
                ExplicitFeedbackCount = 1; MostCommonBlocker = None; MostCommonFollowUp = None
                MostCommonAlternative = None; SuggestedFixTarget = "Clarify the tool" } ]
          TopBlockers = []
          FrequentTransitions = []
          RecentFeedback =
            [ { Tool = tool; Kind = ExplicitFeedbackKind.ToolIntentWasUnclear; Count = 1
                LatestReason = escaped; LatestAlternative = None } ]
          RecommendedWorkItems =
            [ { Title = escaped; TargetTool = None; LikelyFixType = "clarity"
                Reason = escaped; SuggestedAction = escaped } ] }
      let window = { FirstAtUtc = baseTimeUtc; LastAtUtc = baseTimeUtc; EventCount = 13 }
      let duration = DurationMs.create Int32.MaxValue |> ok
      let cases =
        [ FrictionSignal.ExcessivePolling(tool, 11, 7, window), "Calls", 11
          FrictionSignal.ResetThrash(9, window), "Resets", 9
          FrictionSignal.HardResetAfterCreate duration, "GapMs", Int32.MaxValue
          FrictionSignal.UnattributedFailure(escaped, 8), "Count", 8
          FrictionSignal.InvalidStateCall(tool, 6), "Blocked", 6
          FrictionSignal.RepeatedSameError(BlockerKind.InvalidRequest, 5), "Run", 5
          FrictionSignal.RetryLoop(tool, 4), "Attempts", 4
          FrictionSignal.SlowTimeToFirstSuccess(duration, 3), "FailedBefore", 3 ]
      let signal value =
        { Signal = value; Scope = SignalScope.Agent escaped; Confidence = SignalConfidence.Strong }
      let signals = List.map (fun (value, _, _) -> signal value) cases
      let signals = signals @ [ signal (FrictionSignal.Abandonment(tool, BlockerKind.InvalidRequest)) ]
      let root = Json.parseOrFail (frictionReportJson report signals)
      getInt "TotalEvents" root
      |> Expect.equal "Counts retain their exact integer value." Int32.MaxValue
      let top = (getArr "HighestPriorityTools" root)[0]
      for field in [ "MostCommonBlocker"; "MostCommonFollowUp"; "MostCommonAlternative" ] do
        getProp field top |> Expect.equal "Absent choices are explicit JSON null." JsonValue.Null
      let feedback = (getArr "RecentFeedback" root)[0]
      getStr "LatestReason" feedback
      |> Expect.equal "Feedback is escaped by the JSON library." escaped
      getProp "LatestAlternative" feedback
      |> Expect.equal "Missing feedback alternatives remain null." JsonValue.Null
      let workItem = (getArr "RecommendedWorkItems" root)[0]
      getProp "TargetTool" workItem
      |> Expect.equal "A work item may apply to no particular tool." JsonValue.Null
      let observed = getArr "ObservedSignals" root
      observed.Length |> Expect.equal "All nine signal cases have a schema." 9
      for index, (value, field, expected) in List.indexed cases do
        getStr "Id" observed[index]
        |> Expect.equal "Signal identity stays stable." (FrictionSignal.id value)
        getInt field observed[index] |> Expect.equal "Evidence is not stringified." expected
        getStr "Scope" observed[index]
        |> Expect.equal "Scope text is escaped verbatim." ("Agent:" + escaped)
      getInt "EventCount" observed[0] |> Expect.equal "Window evidence is retained." 13
      getStr "RawTool" observed[3] |> Expect.equal "Unattributed names are escaped." escaped
      getStr "LastBlocker" observed[8]
      |> Expect.equal "Abandonment retains its blocker." "InvalidRequest"

    // ── B7 — observed signals surfaced through the daemon read model ──

    testCaseTask "reportDirect surfaces observed signals from the harvest replay pattern (B7)" <| fun () -> task {
      let store = tempFrictionStore ()
      do! seedStore store harvestReplayEvents

      let! bundleResult = Bozzetto.Features.McpFrictionRecorder.Recorder.reportDirectWith store None harvestReplayConfig
      match bundleResult with
      | Error err -> failwithf "reportDirect failed: %s" err
      | Ok bundle ->
        bundle.ObservedSignals
        |> List.exists (fun d ->
          match d.Signal with
          | FrictionSignal.UnattributedFailure("unknown (missing argument)", 4) -> true
          | _ -> false)
        |> Expect.isTrue "reportDirect's read model should carry the UnattributedFailure(4) observed signal"

        bundle.ObservedSignals
        |> List.exists (fun d ->
          match d.Signal with
          | FrictionSignal.ExcessivePolling(tool, 72, _, _) -> ToolName.value tool = historicalPollingTool
          | _ -> false)
        |> Expect.isTrue "reportDirect's read model should carry the ExcessivePolling(72) observed signal"
    }

    testCaseTask "frictionReportJson emits an ObservedSignals array with the expected stable ids (B7)" <| fun () -> task {
      let store = tempFrictionStore ()
      do! seedStore store harvestReplayEvents

      let! bundleResult = Bozzetto.Features.McpFrictionRecorder.Recorder.reportDirectWith store None harvestReplayConfig
      match bundleResult with
      | Error err -> failwithf "reportDirect failed: %s" err
      | Ok bundle ->
        let json = frictionReportJson bundle.Report bundle.ObservedSignals
        let root = Json.parseOrFail json
        let observed = getArr "ObservedSignals" root
        let ids =
          [ for item in observed -> getStr "Id" item ]

        ids
        |> Expect.containsAll
          "report JSON should carry both observed-signal stable ids"
          [ "observed.unattributed-failure"; "observed.excessive-polling" ]
    }

    testCaseTask "reportDirect yields no observed signals for an empty store — no phantom signals (B7)" <| fun () -> task {
      let store = tempFrictionStore ()

      // Live defaults on purpose: this is not a harvest replay, it is the
      // production path with nothing in it.
      let! bundleResult = Bozzetto.Features.McpFrictionRecorder.Recorder.reportDirect store None
      match bundleResult with
      | Error err -> failwithf "reportDirect failed: %s" err
      | Ok bundle ->
        bundle.ObservedSignals |> Expect.isEmpty "an empty friction store must yield zero observed signals"
    }

    testProperty "detectAll never fabricates a signal from an empty event stream, for any threshold config (B7)" <|
      fun (pollingMinCalls: PositiveInt) (resetThrashCount: PositiveInt) (repeatedErrorRun: PositiveInt) ->
        let cfg =
          { DetectorConfig.defaults with
              PollingMinCalls = pollingMinCalls.Get
              ResetThrashCount = resetThrashCount.Get
              RepeatedErrorRun = repeatedErrorRun.Get }
        Bozzetto.Features.ObservedFriction.detectAll cfg [] |> List.isEmpty
  ]
