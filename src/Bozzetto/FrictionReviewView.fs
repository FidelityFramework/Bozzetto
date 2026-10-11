module Bozzetto.Features.FrictionReviewView

/// Pure view-model for the dashboard friction review drawer. Converts the
/// in-memory FrictionReport + local send history into the exact shape the
/// drawer renders and the send handler POSTs — kept free of IO and DOM so
/// the contract is unit-testable.
///
/// Privacy boundary: this view is built from the LOCAL SQLite store only.
/// It never reads remote reports; the owner reads those through the
/// receiver's owner-gated endpoints, outside the dashboard.

open Bozzetto.Features.FrictionTelemetry
open Bozzetto.Features.FrictionTelemetryTypes
open Bozzetto.Features.FrictionSanitize
open Bozzetto.Features.FrictionSqlite
open Bozzetto.Features.ObservedFrictionTypes

type FrictionReviewSnapshot = {
  /// The canonical in-memory report (source of truth for re-deriving the
  /// outgoing payload after the user edits reasons).
  Report: FrictionReport
  /// The sanitized outbound report (what the user reviews before sending).
  Outgoing: OutgoingReport
  /// Raw counts shown in the header.
  EventCount: int
  FeedbackCount: int
  /// Local send history (receipts recorded after successful sends).
  SentReports: SentReport list
  /// Whether the store is empty (nothing to review or send).
  IsEmpty: bool
  /// Passively-detected (observed) friction signals over the same event
  /// stream the report was built from (observed-friction-plan.md §B8).
  /// Callers compute this via `ObservedFriction.detectAll
  /// DetectorConfig.defaults events` — the exact invocation Brief B7 uses
  /// in `McpFrictionRecorder.reportDirect` — and pass the result in here,
  /// rather than this view recomputing the detector pass itself: the
  /// daemon-side `FrictionReportWithSignals` bundle (B7) already carries
  /// it, so re-running `detectAll` per dashboard render would duplicate an
  /// O(events) fold on every SSE push for no new information.
  ObservedSignals: DetectedSignal list
}

/// Build the drawer view model from the canonical report + observed
/// signals (see `FrictionReviewSnapshot.ObservedSignals`) + send history.
let build (report: FrictionReport) (observedSignals: DetectedSignal list) (sentReports: SentReport list) : FrictionReviewSnapshot =
  {
    Report = report
    Outgoing = toOutgoing (BozzettoVersion.current ()) report Map.empty
    EventCount = report.TotalEvents
    FeedbackCount = report.TotalFeedbackItems
    SentReports = sentReports |> List.sortByDescending (fun s -> s.SentAtUtc)
    IsEmpty = report.TotalEvents = 0 && report.TotalFeedbackItems = 0
    ObservedSignals = observedSignals
  }

/// Re-derive the outgoing payload with user edits applied per (tool, kind).
/// `edits` maps (tool, kind) -> edited reason text; toOutgoing sanitizes
/// each edited value so a user cannot push raw secrets out.
let withEdits
  (edits: Map<string * string, string>)
  (snap: FrictionReviewSnapshot)
  : FrictionReviewSnapshot =
  { snap with Outgoing = toOutgoing (BozzettoVersion.current ()) snap.Report edits }

/// The default edit map the drawer binds its textareas to (tool, kind ->
/// latest raw reason).
let defaultEdits (report: FrictionReport) : Map<string * string, string> =
  report.RecentFeedback
  |> List.map (fun f ->
    (FrictionTelemetryTypes.ToolName.value f.Tool, string f.Kind),
    f.LatestReason)
  |> Map.ofList

/// Parse a client-supplied edits JSON object (keys "tool|kind") into the
/// (tool, kind) map. Unknown/malformed keys are dropped; values are
/// sanitized by toOutgoing on the server, so a client can never push raw
/// secrets out through an edit.
let parseEditsJson (json: string) : Map<string * string, string> =
  if System.String.IsNullOrWhiteSpace json then Map.empty
  else
    match Fidelity.Data.JSON.Json.parse json with
    | Ok (Fidelity.Data.JSON.JsonValue.Object properties) ->
      properties
      |> List.choose (fun (key, value) ->
        let separator = key.IndexOf '|'
        match value with
        | Fidelity.Data.JSON.JsonValue.String text when separator > 0 && separator < key.Length - 1 ->
          Some ((key.Substring(0, separator), key.Substring(separator + 1)), text)
        | _ -> None)
      |> Map.ofList
    | _ -> Map.empty

open Fidelity.Data.JSON

let private outgoingToolValue (value: OutgoingTool) =
  JsonValue.Object [
    "Tool", JsonValue.String value.Tool
    "Invocations", JsonValue.ofInt64 (int64 value.Invocations)
    "Blocked", JsonValue.ofInt64 (int64 value.Blocked)
    "Abandoned", JsonValue.ofInt64 (int64 value.Abandoned)
    "ExplicitFeedback", JsonValue.ofInt64 (int64 value.ExplicitFeedback)
    "SuggestedFix", JsonValue.String value.SuggestedFix
  ]

let private outgoingBlockerValue (value: OutgoingBlocker) =
  JsonValue.Object [
    "Blocker", JsonValue.String value.Blocker
    "Count", JsonValue.ofInt64 (int64 value.Count)
    "AffectedTools", JsonValue.Array (List.map JsonValue.String value.AffectedTools)
  ]

let private outgoingTransitionValue (value: OutgoingTransition) =
  JsonValue.Object [
    "From", JsonValue.String value.From
    "To", JsonValue.String value.To
    "Count", JsonValue.ofInt64 (int64 value.Count)
  ]

let private outgoingFeedbackValue (value: OutgoingFeedback) =
  JsonValue.Object [
    "Tool", JsonValue.String value.Tool
    "Kind", JsonValue.String value.Kind
    "Count", JsonValue.ofInt64 (int64 value.Count)
    "Reason", JsonValue.String value.Reason
    "Alternative", value.Alternative |> Option.map JsonValue.String |> Option.defaultValue JsonValue.Null
  ]

let private outgoingWorkItemValue (value: OutgoingWorkItem) =
  JsonValue.Object [
    "Title", JsonValue.String value.Title
    "TargetTool", value.TargetTool |> Option.map JsonValue.String |> Option.defaultValue JsonValue.Null
    "Reason", JsonValue.String value.Reason
    "SuggestedAction", JsonValue.String value.SuggestedAction
  ]

let private outgoingReportValue (value: OutgoingReport) =
  JsonValue.Object [
    "SchemaVersion", JsonValue.ofInt64 (int64 value.SchemaVersion)
    "BozzettoVersion", JsonValue.String value.BozzettoVersion
    "SubmittedAtUtc", JsonValue.String value.SubmittedAtUtc
    "TotalEvents", JsonValue.ofInt64 (int64 value.TotalEvents)
    "TotalFeedbackItems", JsonValue.ofInt64 (int64 value.TotalFeedbackItems)
    "ToolsWithFriction", JsonValue.Array (List.map outgoingToolValue value.ToolsWithFriction)
    "TopBlockers", JsonValue.Array (List.map outgoingBlockerValue value.TopBlockers)
    "FrequentTransitions", JsonValue.Array (List.map outgoingTransitionValue value.FrequentTransitions)
    "RecentFeedback", JsonValue.Array (List.map outgoingFeedbackValue value.RecentFeedback)
    "RecommendedWorkItems", JsonValue.Array (List.map outgoingWorkItemValue value.RecommendedWorkItems)
  ]

let outgoingJson value = outgoingReportValue value |> Json.serialize

/// One-shot: build the outgoing report for a send from the canonical report
/// + client-supplied edits JSON. Pure and sanitized — this is what the
/// server-authoritative send handler POSTs.
let buildOutgoingForSend (report: FrictionReport) (editsJson: string) : OutgoingReport =
  let edits = parseEditsJson editsJson
  toOutgoing (BozzettoVersion.current ()) report edits
