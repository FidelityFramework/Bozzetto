module Bozzetto.SseWriter

open System.IO
open System.Text
open Fidelity.Data.JSON
open WireJson
open System.Threading.Tasks

/// Pure: format an SSE retry hint per the EventSource spec.
/// Tells the client how many milliseconds to wait before reconnecting.
let formatRetryHint (retryMs: int) : string =
  sprintf "retry: %d\n\n" retryMs

/// Pure: format an SSE event string.
/// Handles data containing newlines per SSE spec (each line as separate data: field).
let formatSseEvent (eventType: string) (data: string) : string =
  match data.Contains("\n") with
  | true ->
    let dataLines = data.Split('\n') |> Array.map (sprintf "data: %s") |> String.concat "\n"
    sprintf "event: %s\n%s\n\n" eventType dataLines
  | false ->
    sprintf "event: %s\ndata: %s\n\n" eventType data

/// Pure: format an SSE event string with a sequence ID for Last-Event-Id replay support.
/// The `id:` field is prepended per the EventSource spec, enabling reconnection.
let formatSseEventWithId (seqId: int64) (eventType: string) (data: string) : string =
  let frame = formatSseEvent eventType data
  sprintf "id: %d\n%s" seqId frame

/// Pure: format SSE event with multiline data
let formatSseEventMultiline (eventType: string) (lines: string list) : string =
  match lines with
  | [] -> sprintf "event: %s\n\n" eventType
  | _ ->
    let dataLines = lines |> List.map (sprintf "data: %s") |> String.concat "\n"
    sprintf "event: %s\n%s\n\n" eventType dataLines

/// Safely write bytes to a stream, returning Result instead of throwing
let trySendBytes (stream: Stream) (bytes: byte[]) : Task<Result<unit, string>> =
  task {
    try
      do! stream.WriteAsync(bytes)
      do! stream.FlushAsync()
      return Ok ()
    with ex ->
      return Error (sprintf "SSE write failed: %s" ex.Message)
  }

/// Format + send an SSE event, returning Result instead of throwing
let trySendSseEvent (stream: Stream) (eventType: string) (data: string) : Task<Result<unit, string>> =
  let text = formatSseEvent eventType data
  let bytes = Encoding.UTF8.GetBytes(text)
  trySendBytes stream bytes

/// Inject the session identity through the JSON encoder so quotes and control
/// characters cannot escape the field. Non-object payloads are unchanged.
let injectSessionId (sessionId: string option) (json: string) : string =
  match sessionId with
  | None -> json
  | Some sid ->
    match Json.parse json with
    | Ok (JsonValue.Object properties) ->
      JsonValue.Object (("SessionId", text sid) :: (properties |> List.filter (fun (key, _) -> key <> "SessionId")))
      |> serialize
    | _ -> json

let private emit eventType sessionId value =
  value |> serialize |> injectSessionId sessionId |> formatSseEvent eventType

let private extend casing properties value =
  match value, objectValue casing properties with
  | JsonValue.Object existing, JsonValue.Object additional -> JsonValue.Object (existing @ additional)
  | _ -> invalidArg (nameof value) "Expected an explicitly constructed JSON object."

let private deriveWarmupPhase step total =
  if total > 5 then "opening_namespaces"
  else
    match step with
    | 1 -> "creating_fsi"
    | 2 -> "scanning_sources"
    | 3 -> "loading_assemblies"
    | _ -> "finalizing"

let formatWarmupProgressEvent casing sessionId step total message =
  let progress = if total = 0 then 0.0 else System.Math.Round(float step / float total, 3)
  objectValue casing [
    "Step", integer step; "Total", integer total; "Message", text message
    "Progress", number progress; "Phase", text (deriveWarmupPhase step total)
  ] |> emit "warmup_progress" sessionId

let formatTestSummaryEvent casing sessionId (summary: Features.LiveTesting.TestSummary) lastDecision =
  LiveTestingJson.summaryValue casing summary
  |> extend casing ["LastDecision", optional (LiveTestingJson.decisionValue casing) lastDecision]
  |> emit "test_summary" sessionId

/// Discovery generations are exact integers; consumers reject older summaries.
let formatTestSummaryEventWithDiscovery casing sessionId (summary: Features.LiveTesting.TestSummary) lastDecision discoveryState discoveryGeneration activity =
  LiveTestingJson.summaryValue casing summary
  |> extend casing [
    "NotYetRun", integer (Features.LiveTestActivity.LiveTestActivity.tallyOf activity).NotYetRun
    "Activity", text (Features.LiveTestActivity.LiveTestActivity.wireKind activity)
    "ActivityText", text (Features.LiveTestActivity.LiveTestActivity.describe activity)
    "ActivityShort", text (Features.LiveTestActivity.LiveTestActivity.shortLabel activity)
    "DiscoveryState", text (Features.LiveTesting.LiveTestDiscoveryState.toWireValue discoveryState)
    "DiscoveryGeneration", signed discoveryGeneration
    "LastDecision", optional (LiveTestingJson.decisionValue casing) lastDecision
  ] |> emit "test_summary" sessionId

let formatTestResultsBatchEvent casing sessionId payload =
  LiveTestingJson.batchValue casing payload |> emit "test_results_batch" sessionId

let formatFileAnnotationsEvent casing sessionId annotations =
  LiveTestingJson.fileAnnotationsValue casing annotations |> emit "file_annotations" sessionId

let formatCoverageViewEvent casing sessionId generation view =
  LiveTestingJson.coverageViewValue casing view
  |> extend casing ["Generation", integer generation]
  |> emit "coverage_view" sessionId

let formatFailureNarrativesEvent casing sessionId (narratives: Map<Features.LiveTesting.TestId, Features.LiveTesting.FailureNarrative>) =
  narratives |> Map.toSeq |> array (fun (tid, n) ->
    let changeValue change =
      let kind, name =
        match change with
        | Features.LiveTesting.CausalChange.SymbolChanged s -> "symbol", s
        | Features.LiveTesting.CausalChange.FileChanged f -> "file", f
        | Features.LiveTesting.CausalChange.Unknown -> "unknown", ""
      objectValue casing ["Kind", text kind; "Name", text name]
    objectValue casing [
      "TestId", LiveTestingJson.testIdValue tid
      "LastPassedAt", optional timestamp n.LastPassedAt
      "TimeSinceLastPass", optional duration n.TimeSinceLastPass
      "CausalChanges", array changeValue n.CausalChanges
      "PropertyViolation", optional (LiveTestingJson.propertyViolationValue casing) n.PropertyViolation
      "Summary", text n.Summary
    ]) |> emit "failure_narratives" sessionId

let formatTestSourceLocationsEvent casing sessionId locations =
  objectValue casing ["Locations", array (LiveTestingJson.sourceLocationValue casing) locations]
  |> emit "test_source_locations" sessionId

/// A single FSI binding tracked server-side
type FsiBinding = {
  Name: string
  TypeSig: string
  Value: string option
  ShadowCount: int
}

// ── Binding parser: Option.bind pipeline (ROP) ──

/// Try to strip a prefix, returning the rest or None
let private tryStripPrefix (prefix: string) (s: string) =
  match s.Trim() with
  | t when t.StartsWith(prefix) -> Some (t.Substring(prefix.Length))
  | _ -> None

/// Strip "mutable " prefix if present (total function, always succeeds)
let private stripMutablePrefix (s: string) =
  match s with
  | t when t.StartsWith("mutable ") -> t.Substring(8)
  | t -> t

/// Split at first colon into (name, typeSig), or None
let private splitAtColon (s: string) =
  match s.IndexOf(':') with
  | i when i > 0 -> Some (s.Substring(0, i).Trim(), s.Substring(i + 1).Trim())
  | _ -> None

/// Split trailing "= value" from a type signature, returning (typeSig, valueOpt)
let private splitTypeSigAndValue (typeSig: string) =
  match typeSig.LastIndexOf('=') with
  | i when i > 0 ->
    let ts = typeSig.Substring(0, i).Trim()
    let v = typeSig.Substring(i + 1).Trim()
    match v with
    | "" -> (ts, None)
    | _ -> (ts, Some v)
  | _ -> (typeSig, None)

/// Validate a binding name: skip "it" (expression results) and tuple patterns
let private validateBindingName (name: string, typeSig: string, value: string option) =
  match name with
  | "it" -> None
  | n when n.Contains("(") -> None
  | _ -> Some (name, typeSig, value)

/// Parse a single FSI output line into (name, typeSig, value) via Option.bind pipeline
let private tryParseBinding (line: string) =
  line
  |> tryStripPrefix "val "
  |> Option.map stripMutablePrefix
  |> Option.bind splitAtColon
  |> Option.map (fun (name, ts) ->
    let (typeSig, value) = splitTypeSigAndValue ts
    (name, typeSig, value))
  |> Option.bind validateBindingName

/// Parse `val name : type = value` lines from FSI output.
/// Skips `val it` (expression results) and tuple patterns.
let parseBindingsFromOutput (output: string) : (string * string * string option) array =
  output.Split('\n') |> Array.choose tryParseBinding

/// Accumulate parsed bindings into a running map, tracking shadow counts.
let accumulateBindings
  (existing: Map<string, FsiBinding>)
  (parsed: (string * string * string option) array)
  : Map<string, FsiBinding> =
  parsed
  |> Array.fold (fun acc (name, typeSig, value) ->
    let count =
      match Map.tryFind name acc with
      | Some b -> b.ShadowCount + 1
      | None -> 1
    Map.add name { Name = name; TypeSig = typeSig; Value = value; ShadowCount = count } acc
  ) existing

/// The retained bindings read model is encoded explicitly, independently of
/// the retired embedded FSI producer.
let formatBindingsSnapshotEvent casing sessionId bindingValues blockStartLine filePath (bindings: FsiBinding array) =
  objectValue casing [
    "Bindings", array (fun b -> objectValue casing [
      "Name", text b.Name; "TypeSig", text b.TypeSig; "Value", optional text b.Value
      "ShadowCount", integer b.ShadowCount
    ]) bindings
    "BindingValues", array (LiveTestingJson.bindingValue casing) bindingValues
    "blockStartLine", integer blockStartLine
    "filePath", text (filePath |> Option.defaultValue "")
  ] |> emit "bindings_snapshot" sessionId

let formatLiveBindingsEvent casing sessionId snapshot =
  LiveTestingJson.liveSnapshotValue casing snapshot |> emit "live_bindings" sessionId

let formatTestTraceEvent sessionId traceJson =
  traceJson |> injectSessionId sessionId |> formatSseEvent "test_trace"

let formatEvalDiffEvent casing sessionId (summary: Features.EvalDiff.DiffSummary) =
  let lineValue line =
    let kind, current, previous =
      match line with
      | Features.EvalDiff.Added s -> "added", s, ""
      | Features.EvalDiff.Removed s -> "removed", "", s
      | Features.EvalDiff.Modified (o, n) -> "modified", n, o
      | Features.EvalDiff.Unchanged s -> "unchanged", s, ""
    objectValue casing ["Kind", text kind; "Text", text current; "OldText", text previous]
  objectValue casing [
    "Lines", array lineValue summary.Lines
    "Added", integer summary.AddedCount; "Removed", integer summary.RemovedCount
    "Modified", integer summary.ModifiedCount; "Unchanged", integer summary.UnchangedCount
  ] |> emit "eval_diff" sessionId

let formatEvalStartedEvent casing sessionId filePath blockStartLine =
  objectValue casing ["filePath", text filePath; "blockStartLine", integer blockStartLine]
  |> emit "eval_started" sessionId

let formatEvalHeartbeatEvent casing sessionId filePath blockStartLine elapsedMs =
  objectValue casing ["FilePath", text filePath; "BlockStartLine", integer blockStartLine; "ElapsedMs", signed elapsedMs]
  |> emit "eval_heartbeat" sessionId

let formatEvalResultEvent casing sessionId filePath blockStartLine output success durationMs =
  objectValue casing [
    "filePath", text filePath; "blockStartLine", integer blockStartLine
    "output", text output; "success", boolean success; "durationMs", number durationMs
  ] |> emit "eval_result" sessionId

let formatCellDependenciesEvent casing sessionId (graph: Features.CellDependencyGraph.CellGraph) =
  objectValue casing [
    "Nodes", graph.Cells |> Map.values |> array (fun c -> objectValue casing [
      "Id", integer c.Id; "Produces", array text c.Produces; "Consumes", array text c.Consumes
    ])
    "Edges", graph.Edges |> array (fun (f, t) -> objectValue casing ["From", integer f; "To", integer t])
  ] |> emit "cell_dependencies" sessionId

let formatBindingScopeMapEvent casing sessionId (snapshot: Features.BindingExplorer.BindingScopeSnapshot) =
  objectValue casing [
    "Bindings", snapshot.Bindings |> array (fun b -> objectValue casing [
      "Name", text b.Name; "TypeSig", text b.TypeSig; "CellIndex", integer b.CellIndex
      "ShadowedBy", array integer b.ShadowedBy; "ReferencedIn", array integer b.ReferencedIn
    ])
    "ActiveCount", integer snapshot.ActiveBindings.Count
    "ShadowedCount", integer snapshot.ShadowedBindings.Length
  ] |> emit "binding_scope_map" sessionId

let formatEvalTimelineEvent casing sessionId (stats: Features.EvalTimeline.TimelineStats) =
  objectValue casing [
    "Count", integer stats.Count; "P50Ms", optional number stats.P50Ms
    "P95Ms", optional number stats.P95Ms; "P99Ms", optional number stats.P99Ms
    "MeanMs", optional number stats.MeanMs; "Sparkline", text stats.Sparkline
  ] |> emit "eval_timeline" sessionId

let formatDiagnosisReadyEvent casing sessionId (report: Features.Diagnostician.DiagnosticReport) =
  let staleness = function
    | Features.EvalProvenance.Staleness.Fresh -> union "Fresh" []
    | Features.EvalProvenance.Staleness.StaleUpstream ids -> union "StaleUpstream" [array integer ids]
  let severity =
    match report.Severity with
    | Features.Diagnostician.DiagnosticSeverity.Info -> "Info"
    | Features.Diagnostician.DiagnosticSeverity.Warning -> "Warning"
    | Features.Diagnostician.DiagnosticSeverity.Critical -> "Critical"
  objectValue casing [
    "Severity", text severity
    "FailureCount", integer report.Failures.Length
    "AffectedCells", report.AffectedCells |> array (fun (cell, stale) -> JsonValue.Array [integer cell; staleness stale])
    "SuggestionCount", integer report.SuggestedFixes.Length
    "TopSuggestions", report.SuggestedFixes |> List.truncate 3 |> array (fun s -> objectValue casing [
      "Code", text s.Code; "Explanation", text s.Explanation; "Confidence", number s.Confidence
    ])
    "Failures", report.Failures |> array (fun f -> objectValue casing [
      "TestName", text f.TestName
      "CausalSymbols", f.Narrative.CausalChanges |> List.choose (function
        | Features.LiveTesting.CausalChange.SymbolChanged s -> Some s
        | _ -> None) |> array text
    ])
    "Performance", report.PerformanceContext |> optional (fun s -> objectValue casing [
      "Sparkline", text s.Sparkline; "P50Ms", optional number s.P50Ms; "P95Ms", optional number s.P95Ms
    ])
    "Summary", text report.Summary
  ] |> emit "diagnosis_ready" sessionId

// Cohort events are daemon-scoped and intentionally have no SessionId.
let private displayMember = MemberTable.MemberId.display

let private claimScopeToWire casing scope =
  let kind, path =
    match scope with
    | Cohort.ClaimScope.File p -> "file", p
    | Cohort.ClaimScope.Project p -> "project", p
  objectValue casing ["Kind", text kind; "Path", text path]

let private claimStateToWire casing state =
  let kind, holder, since =
    match state with
    | Cohort.ClaimState.Held holder -> "held", holder, None
    | Cohort.ClaimState.Orphaned (holder, since) -> "orphaned", holder, Some since
    | Cohort.ClaimState.Released (holder, since) -> "released", holder, Some since
  objectValue casing ["Kind", text kind; "Holder", text (displayMember holder); "Since", optional date since]

let private landingBlockerToWire casing blocker =
  let kind, files, tests, claimId, fromHead, toHead, by, reason =
    match blocker with
    | Cohort.LandingBlocker.RebaseConflict files -> "rebase_conflict", files, [], "", "", "", "", ""
    | Cohort.LandingBlocker.FailingTests tests -> "failing_tests", [], tests |> List.map (fun (Cohort.TestId t) -> t), "", "", "", "", ""
    | Cohort.LandingBlocker.StaleClaimFence (Cohort.ClaimId cid) -> "stale_claim_fence", [], [], cid, "", "", "", ""
    | Cohort.LandingBlocker.HeadMoved (fromHead, toHead) -> "head_moved", [], [], "", fromHead, toHead, "", ""
    | Cohort.LandingBlocker.VetoedBy (by, reason) -> "vetoed_by", [], [], "", "", "", displayMember by, reason
    | Cohort.LandingBlocker.Inconclusive reason -> "inconclusive", [], [], "", "", "", "", reason
  objectValue casing [
    "Kind", text kind; "Files", array text files; "Tests", array text tests
    "ClaimId", text claimId; "From", text fromHead; "To", text toHead; "By", text by; "Reason", text reason
  ]

let private nextActionToWire casing action =
  let kind, tests =
    match action with
    | Cohort.NextAction.RebaseAndResubmit -> "rebase_and_resubmit", []
    | Cohort.NextAction.AwaitConductor -> "await_conductor", []
    | Cohort.NextAction.FixTests tests -> "fix_tests", tests |> List.map (fun (Cohort.TestId t) -> t)
    | Cohort.NextAction.Withdraw -> "withdraw", []
  objectValue casing ["Kind", text kind; "Tests", array text tests]

let private landingStateKind state =
  match state with
  | Cohort.LandingState.Queued -> "queued"
  | Cohort.LandingState.Rebasing _ -> "rebasing"
  | Cohort.LandingState.Verifying _ -> "verifying"
  | Cohort.LandingState.Blocked _ -> "blocked"
  | Cohort.LandingState.Landed _ -> "landed"
  | Cohort.LandingState.Withdrawn -> "withdrawn"

let private landingBlockerValues casing state =
  match state with
  | Cohort.LandingState.Blocked (blocker, action) -> landingBlockerToWire casing blocker, nextActionToWire casing action
  | _ -> JsonValue.Null, JsonValue.Null

/// Shared projection used by SSE and the cohort MCP resource.
let cohortFrameJson casing (frame: Cohort.CohortFrame<MemberTable.MemberId>) =
  let memberAt index = if index = -1 then JsonValue.Null else text (displayMember frame.MemberIds[index])
  let members = Array.init frame.MemberIds.Length (fun i ->
    let role =
      match frame.MemberRole[i] with
      | Cohort.JoinableRole.Implementer -> "Implementer"
      | Cohort.JoinableRole.Verifier -> "Verifier"
      | Cohort.JoinableRole.Observer -> "Observer"
    let seat = match frame.MemberSeat[i] with Cohort.SeatState.Present -> "present" | Cohort.SeatState.Departed _ -> "departed"
    objectValue casing [
      "Id", text (displayMember frame.MemberIds[i]); "Role", text role; "Seat", text seat
      "Conductor", boolean (frame.Conductor = Some frame.MemberIds[i])
    ])
  let claims = Array.init frame.ClaimIds.Length (fun i ->
    let (Cohort.ClaimId cid) = frame.ClaimIds[i]
    objectValue casing [
      "Id", text cid; "Scope", claimScopeToWire casing frame.ClaimScope[i]
      "Holder", memberAt frame.ClaimHolderIndex[i]; "Fence", signed (int64 frame.ClaimFence[i])
      "State", claimStateToWire casing frame.ClaimState[i]
    ])
  let rows = Array.init frame.SessionGens.Length (fun i -> objectValue casing [
    "Generation", signed frame.SessionGens[i]
    "Pass", array boolean frame.Pass[i]; "Fail", array boolean frame.Fail[i]; "Stale", array boolean frame.Stale[i]
  ])
  let landings = Array.init frame.LandingIds.Length (fun i ->
    let (Cohort.LandingId lid) = frame.LandingIds[i]
    let blocker, nextAction = landingBlockerValues casing frame.LandingState[i]
    objectValue casing [
      "Id", text lid; "Requester", memberAt frame.LandingRequesterIndex[i]
      "Statement", text (Cohort.Statement.value frame.LandingStatement[i])
      "Commits", array text frame.LandingCommits[i]; "State", text (landingStateKind frame.LandingState[i])
      "QueuePosition", integer frame.LandingQueuePosition[i]; "Blocker", blocker; "NextAction", nextAction
    ])
  objectValue casing [
    "Version", signed (int64 frame.Version); "Members", array id members; "Claims", array id claims
    "Tests", array (fun (Cohort.TestId t) -> text t) frame.TestIds; "Rows", array id rows
    "IntegrationHead", text frame.IntegrationHead; "Landings", array id landings
  ] |> serialize

let formatCohortMatrixEvent casing frame =
  cohortFrameJson casing frame |> formatSseEvent "cohort_matrix"

let formatClaimChangedEvent casing kind (claim: Cohort.Claim<MemberTable.MemberId>) =
  let (Cohort.ClaimId cid) = claim.Id
  let holder = match claim.State with Cohort.ClaimState.Held holder -> Some (displayMember holder) | _ -> None
  objectValue casing [
    "ClaimId", text cid; "Scope", claimScopeToWire casing claim.Scope
    "Holder", optional text holder; "Fence", signed (int64 claim.Fence); "Kind", text kind
  ] |> emit "claim_changed" None

let formatLandingChangedEvent casing (landing: Cohort.LandingRequest<MemberTable.MemberId>) =
  let (Cohort.LandingId lid) = landing.Id
  let blocker, nextAction = landingBlockerValues casing landing.State
  objectValue casing [
    "LandingId", text lid; "Requester", text (displayMember landing.Requester)
    "State", text (landingStateKind landing.State); "Blocker", blocker; "NextAction", nextAction
  ] |> emit "landing_changed" None

let formatSaveObservedEvent casing (claim: Cohort.Claim<MemberTable.MemberId>) observer holder path =
  let (Cohort.ClaimId cid) = claim.Id
  objectValue casing [
    "ClaimId", text cid; "Observer", text (displayMember observer); "Holder", text (displayMember holder)
    "Scope", claimScopeToWire casing claim.Scope; "Path", text path
  ] |> emit "save_observed" None

// ── Authoritative SSE event type registry ──────────────────────────────────────────

/// Authoritative list of all SSE event type names emitted by SseWriter formatters.
/// Every event type emitted by the daemon that originates from SseWriter must appear here.
/// The `"state"` and `"session"` events are the two channels of the unified
/// Bozzetto.Server.SseEvent vocabulary (roast-5 §1) — one DU, one serializer,
/// classified by SseEvent.channel; their channel names are
/// SseEvent.sseEventTypeState / SseEvent.sseEventTypeSession.
let allSseEventTypes : string list = [
  "warmup_progress"
  "test_summary"
  "test_results_batch"
  "file_annotations"
  "failure_narratives"
  "test_source_locations"
  "bindings_snapshot"
  "live_bindings"
  "test_trace"
  "eval_diff"
  "eval_started"
  "eval_heartbeat"
  "eval_result"
  "cell_dependencies"
  "binding_scope_map"
  "eval_timeline"
  "diagnosis_ready"
  "coverage_view"
  "cohort_matrix"
  "claim_changed"
  "landing_changed"
  "save_observed"
]
