/// What Bozzetto keeps under its data dir, how big it is, how long it's kept,
/// and a way to clear it. The MCP tool `manage_local_data` and the daemon's
/// `/api/local-data` endpoints both go through here so they can't disagree.
module Bozzetto.LocalData

open System
open System.IO
open Bozzetto.Features
open Fidelity.Data.JSON
open Bozzetto.Features.FrictionSqlite
open Bozzetto.ComposerIntegration

let frictionFileName = "friction.db"
let cohortLedgerFileName = "cohort.ledger.db"
let operationsFileName = "operations.db"

let frictionPath (dataDir: string) = Path.Combine(dataDir, frictionFileName)
let cohortLedgerPath (dataDir: string) = Path.Combine(dataDir, cohortLedgerFileName)
let operationsPath (dataDir: string) = Path.Combine(dataDir, operationsFileName)

[<RequireQualifiedAccess>]
type FrictionReport =
  /// The daemon started without a friction store (init failed or it's off).
  | NotConfigured
  | ReadFailed of reason: string
  | Stored of StoreUsage

type Report = {
  DataDir: string
  Friction: FrictionReport
  CohortLedger: Result<CohortLedgerSqlite.Sqlite.LedgerUsage, string>
  Operations: Result<ComposerJournal.StoreUsage, string>
  /// The live cohort, as the running owner sees it right now.
  Cohort: LocalDataRetention.CohortActivity
  Policy: LocalDataRetention.FrictionPolicy
  CohortLedgerRetention: TimeSpan
  PruneInterval: TimeSpan
}

let report (frictionStore: FrictionStore option) (cohort: LocalDataRetention.CohortActivity) (dataDir: string) : Report =
  let friction =
    match frictionStore with
    | None -> FrictionReport.NotConfigured
    | Some store ->
      match store.Usage () with
      | Ok usage -> FrictionReport.Stored usage
      | Error reason -> FrictionReport.ReadFailed reason
  let ledger =
    try Ok (CohortLedgerSqlite.Sqlite.usage (cohortLedgerPath dataDir))
    with ex -> Error ex.Message
  { DataDir = dataDir
    Friction = friction
    CohortLedger = ledger
    Operations =
      if File.Exists(operationsPath dataDir) then
        ComposerJournal.openStore (operationsPath dataDir) |> Result.bind (fun store -> store.Usage())
      else Ok { Bytes = 0L; Rows = 0L; OldestMs = None }
    Cohort = cohort
    Policy = LocalDataRetention.defaultPolicy ()
    CohortLedgerRetention = DataRetention.cohortLedgerRetention
    PruneInterval = DataRetention.pruneInterval }

let private oldestText (oldest: OldestRow) =
  match oldest with
  | OldestRow.NoRows -> "empty"
  | OldestRow.WrittenAt at -> sprintf "oldest %s" (at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'"))

let private kib (bytes: int64) = sprintf "%.1f KiB" (float bytes / 1024.0)

let private cohortText (activity: LocalDataRetention.CohortActivity) =
  match activity with
  | LocalDataRetention.CohortActivity.Finished -> "no cohort running"
  | LocalDataRetention.CohortActivity.Active(present, held, landings) ->
    sprintf "cohort running: %d present, %d claims held, %d landings in flight" present held landings

/// The plain-text view the MCP tool returns.
let render (r: Report) : string =
  let lines = ResizeArray<string>()
  lines.Add(sprintf "Local data in %s" r.DataDir)
  lines.Add ""
  match r.Friction with
  | FrictionReport.NotConfigured -> lines.Add(sprintf "%s: not configured (this daemon isn't recording friction)" frictionFileName)
  | FrictionReport.ReadFailed reason -> lines.Add(sprintf "%s: couldn't read it: %s" frictionFileName reason)
  | FrictionReport.Stored usage ->
    lines.Add(sprintf "%s: %s" frictionFileName (kib usage.Bytes))
    for t in usage.Tables do
      lines.Add(sprintf "  %s: %d rows, %s" t.Table t.Rows (oldestText t.Oldest))
  match r.CohortLedger with
  | Ok usage -> lines.Add(sprintf "%s: %s, %d rows, %s (%s)" cohortLedgerFileName (kib usage.Bytes) usage.Rows (oldestText usage.Oldest) (cohortText r.Cohort))
  | Error reason -> lines.Add(sprintf "%s: couldn't read it: %s" cohortLedgerFileName reason)
  match r.Operations with
  | Ok usage -> lines.Add(sprintf "%s: %s, %d retained operations" operationsFileName (kib usage.Bytes) usage.Rows)
  | Error reason -> lines.Add(sprintf "%s: couldn't read it: %s" operationsFileName reason)
  lines.Add ""
  lines.Add "Kept for:"
  lines.Add(sprintf "  friction: only the running Bozzetto version's rows (%s). Other versions are rolled into a count per kind." (FrictionTelemetryTypes.BozzettoVersion.current ()))
  lines.Add(sprintf "  friction: rows older than %g days go (%s)" r.Policy.MaxAge.TotalDays DataRetention.frictionMaxAgeEnvVar)
  lines.Add(sprintf "  friction: at most %d rows per table, newest win (%s)" r.Policy.MaxRows DataRetention.frictionMaxRowsEnvVar)
  lines.Add(sprintf "  friction: counts kept for the %d most recent versions (%s)" r.Policy.MaxAggregateVersions DataRetention.frictionMaxAggregateVersionsEnvVar)
  lines.Add(sprintf "  friction: pruned on daemon start and every %g minutes (%s)" r.PruneInterval.TotalMinutes DataRetention.pruneIntervalEnvVar)
  lines.Add(sprintf "  cohort ledger: cleared on daemon start once the cohort finished more than %g days ago; a running cohort is never touched (%s)" r.CohortLedgerRetention.TotalDays DataRetention.cohortLedgerRetentionEnvVar)
  lines.Add(sprintf "  operations: newest %d settled entries; each output stream keeps at most %d UTF-16 units plus its truncation marker. Live transcript eviction does not delete stored output." ComposerJournal.maxEntries ComposerJournal.streamLimit)
  lines.Add ""
  lines.Add "Clear it with manage_local_data action=clear store=friction|cohort|operations|all."
  String.Join("\n", lines)

/// The explicit JSON schema returned by /api/local-data.
let toJsonValue (report: Report) : JsonValue =
  let object = JsonValue.Object
  let text = WireJson.text
  let oldest = function
    | OldestRow.NoRows -> JsonValue.Null
    | OldestRow.WrittenAt at -> text (at.ToUniversalTime().ToString("O"))
  let friction =
    match report.Friction with
    | FrictionReport.NotConfigured -> object ["state", text "notConfigured"]
    | FrictionReport.ReadFailed reason -> object ["state", text "readFailed"; "reason", text reason]
    | FrictionReport.Stored usage -> object [
        "state", text "stored"
        "path", text (frictionPath report.DataDir)
        "bytes", WireJson.signed usage.Bytes
        "tables", usage.Tables |> WireJson.array (fun table -> object [
          "table", text table.Table; "rows", WireJson.signed table.Rows; "oldest", oldest table.Oldest ]) ]
  let ledger =
    match report.CohortLedger with
    | Error reason -> object ["state", text "readFailed"; "reason", text reason]
    | Ok usage -> object [
        "state", text "stored"
        "path", text (cohortLedgerPath report.DataDir)
        "bytes", WireJson.signed usage.Bytes
        "rows", WireJson.signed usage.Rows
        "oldest", oldest usage.Oldest
        "cohort", text (cohortText report.Cohort) ]
  let operations =
    match report.Operations with
    | Error reason -> object [ "state", text "readFailed"; "reason", text reason ]
    | Ok usage -> object [
        "state", text "stored"; "path", text (operationsPath report.DataDir)
        "bytes", WireJson.signed usage.Bytes; "rows", WireJson.signed usage.Rows
        "oldestMs", usage.OldestMs |> Option.map WireJson.signed |> Option.defaultValue JsonValue.Null ]
  object [
    "dataDir", text report.DataDir
    "friction", friction
    "cohortLedger", ledger
    "operations", operations
    "retention", object [
      "frictionCurrentVersionOnly", text (FrictionTelemetryTypes.BozzettoVersion.current ())
      "frictionMaxAgeDays", WireJson.number report.Policy.MaxAge.TotalDays
      "frictionMaxRows", WireJson.integer report.Policy.MaxRows
      "frictionMaxAggregateVersions", WireJson.integer report.Policy.MaxAggregateVersions
      "pruneIntervalMinutes", WireJson.number report.PruneInterval.TotalMinutes
      "cohortLedgerRetentionDays", WireJson.number report.CohortLedgerRetention.TotalDays ] ]

[<RequireQualifiedAccess>]
type ClearTarget =
  | Friction
  | CohortLedger
  | Operations
  | All

module ClearTarget =
  let parse (text: string) : Result<ClearTarget, string> =
    match (if isNull text then "" else text.Trim().ToLowerInvariant()) with
    | "" | "all" -> Ok ClearTarget.All
    | "friction" -> Ok ClearTarget.Friction
    | "cohort" | "cohort-ledger" -> Ok ClearTarget.CohortLedger
    | "operations" -> Ok ClearTarget.Operations
    | other -> Error (sprintf "Unknown store '%s'. Use friction, cohort, operations or all." other)

[<RequireQualifiedAccess>]
type ClearOutcome =
  | Cleared of rows: int64
  | NotConfigured
  /// The cohort is still running; its ledger is never cleared out from under it.
  | RefusedActiveCohort of LocalDataRetention.CohortActivity
  | Failed of reason: string

[<RequireQualifiedAccess>]
type LocalStore =
  | Friction
  | CohortLedger
  | Operations

module LocalStore =
  let fileName (store: LocalStore) =
    match store with
    | LocalStore.Friction -> frictionFileName
    | LocalStore.CohortLedger -> cohortLedgerFileName
    | LocalStore.Operations -> operationsFileName

/// One line per store the clear touched.
type ClearReport = (LocalStore * ClearOutcome) list

let clear (target: ClearTarget) (frictionStore: FrictionStore option) (cohort: LocalDataRetention.CohortActivity) (dataDir: string) : ClearReport =
  let clearFriction () =
    match frictionStore with
    | None -> ClearOutcome.NotConfigured
    | Some store ->
      match store.Clear () with
      | Ok rows -> ClearOutcome.Cleared rows
      | Error reason -> ClearOutcome.Failed reason
  let clearLedger () =
    match cohort with
    | LocalDataRetention.CohortActivity.Active _ -> ClearOutcome.RefusedActiveCohort cohort
    | LocalDataRetention.CohortActivity.Finished ->
      // The running owner keeps its in-memory state; only what's left of a
      // finished cohort (departed seats, settled claims and landings, the
      // conductor binding) goes from disk. The next daemon start replays
      // from whatever gets appended after this, which is a fresh cohort.
      try ClearOutcome.Cleared (CohortLedgerSqlite.Sqlite.clear (cohortLedgerPath dataDir))
      with ex -> ClearOutcome.Failed ex.Message
  let clearOperations () =
    if not (File.Exists(operationsPath dataDir)) then ClearOutcome.Cleared 0L
    else
      match ComposerJournal.openStore (operationsPath dataDir) |> Result.bind (fun store -> store.Clear()) with
      | Ok rows -> ClearOutcome.Cleared rows
      | Error reason -> ClearOutcome.Failed reason
  match target with
  | ClearTarget.Friction -> [ LocalStore.Friction, clearFriction () ]
  | ClearTarget.CohortLedger -> [ LocalStore.CohortLedger, clearLedger () ]
  | ClearTarget.Operations -> [ LocalStore.Operations, clearOperations () ]
  | ClearTarget.All -> [ LocalStore.Friction, clearFriction (); LocalStore.CohortLedger, clearLedger (); LocalStore.Operations, clearOperations () ]

let private outcomeText (store: LocalStore) (outcome: ClearOutcome) =
  let name = LocalStore.fileName store
  match outcome with
  | ClearOutcome.Cleared rows -> sprintf "%s: cleared %d rows" name rows
  | ClearOutcome.NotConfigured -> sprintf "%s: not configured, nothing to clear" name
  | ClearOutcome.RefusedActiveCohort activity -> sprintf "%s: left alone, %s" name (cohortText activity)
  | ClearOutcome.Failed reason -> sprintf "%s: clear failed: %s" name reason

let renderClear (r: ClearReport) : string =
  r |> List.map (fun (store, outcome) -> outcomeText store outcome) |> String.concat "\n"

/// The live cohort's activity, from the running owner. No owner (a daemon
/// without cohorts wired) means nothing is running.
let liveCohort (owner: CohortOwner.Handle option) : LocalDataRetention.CohortActivity =
  match owner with
  | Some handle -> LocalDataRetention.cohortActivity (handle.ReadCohortState ())
  | None -> LocalDataRetention.CohortActivity.Finished

[<RequireQualifiedAccess>]
type LocalDataAction =
  | Status
  | Clear

module LocalDataAction =
  let parse (text: string) : Result<LocalDataAction, string> =
    match (if isNull text then "" else text.Trim().ToLowerInvariant()) with
    | "" | "status" -> Ok LocalDataAction.Status
    | "clear" -> Ok LocalDataAction.Clear
    | other -> Error (sprintf "Unknown action '%s'. Use status or clear." other)
