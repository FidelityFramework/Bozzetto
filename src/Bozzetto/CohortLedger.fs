module Bozzetto.Features.CohortLedgerSqlite

open Microsoft.Data.Sqlite
open Bozzetto
open Bozzetto.Cohort
open Bozzetto.MemberTable
open Bozzetto.Features.CohortLedger

/// SQLite-backed cohort ledger. CohortJson supplies the same explicit
/// command/event schema as the portable JSONL export; SQLite owns only IO.
module Sqlite =

  let private schemaVersion = 1L

  let private openConnection (dbPath: string) =
    let connection = new SqliteConnection(sprintf "Data Source=%s" dbPath)
    connection.Open()
    connection

  let private ensureSchema (connection: SqliteConnection) =
    use command = connection.CreateCommand()
    command.CommandText <- "
CREATE TABLE IF NOT EXISTS cohort_ledger (
  seq INTEGER PRIMARY KEY,
  clock_ticks INTEGER NOT NULL,
  entropy BLOB NOT NULL,
  command_json TEXT NOT NULL,
  events_json TEXT NOT NULL
);"
    command.ExecuteNonQuery() |> ignore
    use pragma = connection.CreateCommand()
    pragma.CommandText <- sprintf "PRAGMA user_version = %d;" schemaVersion
    pragma.ExecuteNonQuery() |> ignore

  // Open or create the ledger. Each batch or read owns one connection.
  let create (dbPath: string) : LedgerPort<MemberId> =
    use init = openConnection dbPath
    ensureSchema init

    let appendBatch (entries: LedgerEntry<MemberId> list) =
      try
        use connection = openConnection dbPath
        use transaction = connection.BeginTransaction()
        use command = connection.CreateCommand()
        command.Transaction <- transaction
        command.CommandText <- "
INSERT INTO cohort_ledger (seq, clock_ticks, entropy, command_json, events_json)
VALUES ($seq, $clock_ticks, $entropy, $command_json, $events_json);"
        let seq = command.Parameters.Add("$seq", SqliteType.Integer)
        let clock = command.Parameters.Add("$clock_ticks", SqliteType.Integer)
        let entropy = command.Parameters.Add("$entropy", SqliteType.Blob)
        let cmd = command.Parameters.Add("$command_json", SqliteType.Text)
        let events = command.Parameters.Add("$events_json", SqliteType.Text)
        for entry in entries do
          seq.Value <- int64 entry.Seq
          clock.Value <- entry.Clock.Ticks
          entropy.Value <- entry.Entropy
          cmd.Value <- CohortJson.commandToJson entry.Command
          events.Value <- CohortJson.eventsToJson entry.Events
          command.ExecuteNonQuery() |> ignore
        transaction.Commit()
        Ok ()
      with
      | :? SqliteException as ex when ex.SqliteErrorCode = 19 ->
        Error(LedgerWriteError.ConstraintViolation ex.Message)
      | :? SqliteException as ex
          when ex.SqliteErrorCode = 5 || ex.SqliteErrorCode = 6 || ex.SqliteErrorCode = 8
               || ex.SqliteErrorCode = 10 || ex.SqliteErrorCode = 13 || ex.SqliteErrorCode = 14
               || ex.SqliteErrorCode = 26 ->
        Error(LedgerWriteError.StorageUnavailable ex.Message)
      | ex -> Error(LedgerWriteError.WriteFailed ex.Message)

    let readAll () : LedgerEntry<MemberId> list =
      use connection = openConnection dbPath
      use command = connection.CreateCommand()
      command.CommandText <- "SELECT seq, clock_ticks, entropy, command_json, events_json FROM cohort_ledger ORDER BY seq;"
      use reader = command.ExecuteReader()
      let entries = ResizeArray()
      while reader.Read() do
        let seq = LanguagePrimitives.Int64WithMeasure<Measures.ledgerSeq> (reader.GetInt64 0)
        let clock = System.DateTime(reader.GetInt64 1, System.DateTimeKind.Utc)
        let entropy = reader.GetFieldValue<byte[]>(2)
        let require = function Ok value -> value | Error reason -> invalidOp ("Invalid stored cohort ledger: " + reason)
        let cmd = CohortJson.commandFromJson (reader.GetString 3) |> require
        let events = CohortJson.eventsFromJson (reader.GetString 4) |> require
        entries.Add { Seq = seq; Clock = clock; Entropy = entropy; Command = cmd; Events = events }
      List.ofSeq entries

    { AppendBatch = appendBatch; ReadAll = readAll }

  /// The ledger's footprint for the "what's stored" view.
  type LedgerUsage = {
    Bytes: int64
    Rows: int64
    /// Clock of the oldest entry, `NoRows` when the ledger is empty.
    Oldest: Bozzetto.Features.FrictionSqlite.OldestRow
  }

  let usage (dbPath: string) : LedgerUsage =
    use connection = openConnection dbPath
    ensureSchema connection
    let scalar (sql: string) =
      use command = connection.CreateCommand()
      command.CommandText <- sql
      command.ExecuteScalar()
    let bytes = System.Convert.ToInt64(scalar "PRAGMA page_count;") * System.Convert.ToInt64(scalar "PRAGMA page_size;")
    let rows = System.Convert.ToInt64(scalar "SELECT count(*) FROM cohort_ledger;")
    let oldest =
      match scalar "SELECT min(clock_ticks) FROM cohort_ledger;" with
      | :? int64 as ticks ->
        Bozzetto.Features.FrictionSqlite.OldestRow.WrittenAt(System.DateTimeOffset(System.DateTime(ticks, System.DateTimeKind.Utc)))
      | _ -> Bozzetto.Features.FrictionSqlite.OldestRow.NoRows
    { Bytes = bytes; Rows = rows; Oldest = oldest }

  /// Delete every ledger row and give the space back. Callers decide whether
  /// that's allowed (`LocalDataRetention.decideLedger`); this just does it.
  let clear (dbPath: string) : int64 =
    use connection = openConnection dbPath
    ensureSchema connection
    let deleted =
      use command = connection.CreateCommand()
      command.CommandText <- "DELETE FROM cohort_ledger;"
      int64 (command.ExecuteNonQuery())
    use vacuum = connection.CreateCommand()
    vacuum.CommandText <- "VACUUM;"
    vacuum.ExecuteNonQuery() |> ignore
    deleted

  /// Run on daemon start, BEFORE the cohort owner replays the ledger: clear it
  /// when the cohort it holds finished longer ago than `retention`. Doing it
  /// before the replay means the owner's state and the ledger on disk never
  /// disagree. An active cohort's rows are never touched.
  let pruneFinished (retention: System.TimeSpan) (now: System.DateTime) (dbPath: string) : Bozzetto.Features.LocalDataRetention.LedgerDecision =
    let port = create dbPath
    let decision = Bozzetto.Features.LocalDataRetention.decideLedger retention now (port.ReadAll ())
    match decision with
    | Bozzetto.Features.LocalDataRetention.LedgerDecision.Clear _ -> clear dbPath |> ignore
    | Bozzetto.Features.LocalDataRetention.LedgerDecision.NothingStored
    | Bozzetto.Features.LocalDataRetention.LedgerDecision.KeepActive _
    | Bozzetto.Features.LocalDataRetention.LedgerDecision.KeepRecent _ -> ()
    decision
