/// The daemon's record of settled Composer operations: who asked for what, what
/// came of it, and a run's output. Every caller's operation settles in the
/// supervisor, which owns this journal and publishes it whole; the page reads
/// it through the bridge's fact plane. Bounded three ways so a replacement
/// snapshot stays a page fact: at most `maxEntries` entries, at most
/// `streamLimit` characters per transcript stream, at most `aggregateLimit`
/// transcript characters over the whole journal (the oldest transcripts go
/// first; their entries stay, marked). SQLite retains bounded source entries;
/// aggregate eviction is only a live projection. Status reads are not journaled.
module Bozzetto.ComposerIntegration.ComposerJournal

open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open System
open System.IO
open System.Text
open Microsoft.Data.Sqlite
open Microsoft.FSharp.Reflection

/// A run's output as journaled. `Dropped` marks a transcript given up for space.
type Transcript = {
  ExitCode: int
  Generation: int64
  SourceVersion: string
  StandardOutput: string
  StandardError: string
  OutputTruncated: bool
  ErrorTruncated: bool
  Dropped: bool
}

[<RequireQualifiedAccess>]
type Outcome =
  | Succeeded
  | Refused of RefusalCode * string
  /// An exception escaped the supervisor: journaled and rethrown.
  | Failed of string

type Entry = {
  /// The journal's serial, one per entry, increasing.
  Sequence: int64
  AtMs: int64
  /// The adapter's report of who asked ("mcp:<transport>", "http", "browser"): a label, not an identity.
  Caller: string
  Operation: Operation
  Session: string
  Project: string
  Outcome: Outcome
  Run: Transcript option
}

/// Newest first.
type Journal = { Incarnation: string; Sequence: int64; Entries: Entry list; StorageError: string option }

[<RequireQualifiedAccess>]
type TranscriptStream = Stdout | Stderr

type TranscriptRequest = {
  Incarnation: string; Sequence: int64; Stream: TranscriptStream; Offset: int64; MaxBytes: int
}

type TranscriptPage = {
  Incarnation: string; Sequence: int64; Stream: TranscriptStream; Offset: int64
  NextOffset: int64 option; TotalBytes: int64; Bytes: byte array; Truncated: bool
}

[<RequireQualifiedAccess>]
type TranscriptRefusal =
  | ForeignIncarnation | MissingEntry | NoTranscript | InvalidCursor | InvalidLimit
  | StorageFailure of string

type StoreUsage = { Bytes: int64; Rows: int64; OldestMs: int64 option }

type Store = {
  Read: unit -> Result<Journal, string>
  Append: Entry -> Result<Journal, string>
  ReadTranscript: TranscriptRequest -> Result<TranscriptPage, TranscriptRefusal>
  Usage: unit -> Result<StoreUsage, string>
  Clear: unit -> Result<int64, string>
}

let maxEntries = 200
/// Characters (UTF-16 code units) kept per stream: the end of the stream, which carries the result.
let streamLimit = 16384
/// Transcript characters over the whole journal.
let aggregateLimit = 512 * 1024
/// Characters kept of a caller label or session identity, and of a project path or a message.
let labelLimit = 128
let textLimit = 512

let empty incarnation = { Incarnation = incarnation; Sequence = 0L; Entries = []; StorageError = None }

/// A stream longer than the limit keeps its end, with one marker line in front
/// that is not counted against the limit. A stream already cut is left as it is.
let cut (text: string) =
  if text.Length <= streamLimit then text
  elif text.StartsWith "[cut: " && text.Length <= streamLimit + 64 then text
  else sprintf "[cut: %d characters omitted]\n%s" (text.Length - streamLimit) (text.Substring(text.Length - streamLimit))

let transcript (run: RunResult) =
  let output, error = cut run.StandardOutput, cut run.StandardError
  { ExitCode = run.ExitCode; Generation = run.Generation; SourceVersion = run.SourceVersion
    StandardOutput = output; StandardError = error
    OutputTruncated = output <> run.StandardOutput; ErrorTruncated = error <> run.StandardError
    Dropped = false }

let private size (t: Transcript) = t.StandardOutput.Length + t.StandardError.Length
let private live (e: Entry) = match e.Run with Some t when not t.Dropped -> size t | _ -> 0
let private clip limit (text: string) = if text.Length <= limit then text else text.Substring(0, limit)

/// Every text of an entry bounded, whatever its producer sent: labels,
/// identities, paths, messages, a run's source version and both streams.
let bound (entry: Entry) =
  { entry with
      Caller = clip labelLimit entry.Caller
      Session = clip labelLimit entry.Session
      Project = clip textLimit entry.Project
      Outcome =
        match entry.Outcome with
        | Outcome.Refused(code, message) -> Outcome.Refused(code, clip textLimit message)
        | Outcome.Failed message -> Outcome.Failed(clip textLimit message)
        | Outcome.Succeeded -> Outcome.Succeeded
      Run =
        entry.Run |> Option.map (fun t ->
          // A transcript marked dropped holds no streams, whatever the producer sent, so it weighs what it counts.
          if t.Dropped then { t with SourceVersion = clip labelLimit t.SourceVersion; StandardOutput = ""; StandardError = "" }
          else
            let output, error = cut t.StandardOutput, cut t.StandardError
            { t with SourceVersion = clip labelLimit t.SourceVersion
                     StandardOutput = output; StandardError = error
                     OutputTruncated = t.OutputTruncated || output <> t.StandardOutput
                     ErrorTruncated = t.ErrorTruncated || error <> t.StandardError }) }

/// Record one settled operation: the newest entry first, the count bounded,
/// and the aggregate transcript budget kept by giving up transcripts oldest
/// first until the rest fit; their entries stay, marked.
let private project (entries: Entry list) =
  let excess = (entries |> List.sumBy live) - aggregateLimit
  let evicted =
    if excess <= 0 then entries
    else
      // Walk from the oldest entry; prepending as we go rebuilds the list newest first.
      ((excess, []), List.rev entries)
      ||> List.fold (fun (remaining, acc) (e: Entry) ->
        if remaining > 0 && live e > 0 then
          let t = e.Run.Value
          remaining - size t, { e with Run = Some { t with StandardOutput = ""; StandardError = ""; Dropped = true } } :: acc
        else remaining, e :: acc)
      |> snd
  evicted

let append (entry: Entry) (journal: Journal) =
  let sequence = journal.Sequence + 1L
  let entries = { bound entry with Sequence = sequence } :: journal.Entries |> List.truncate maxEntries
  { journal with Sequence = sequence; Entries = project entries }

/// How one call settled, as the journal records it: a reply's outcome, a
/// refusal, or an exception that escaped (recorded and rethrown by the caller).
/// Cancellation is a refusal with the Canceled code.
let settle (caller: string) (operation: Operation) (session: string) (project: string) (atMs: int64)
           (settled: Result<Result<ReplyBody, Refusal>, exn>) : Entry =
  let outcome, run =
    match settled with
    | Result.Ok(Result.Ok(Ran result)) -> Outcome.Succeeded, Some(transcript result)
    | Result.Ok(Result.Ok _) -> Outcome.Succeeded, None
    | Result.Ok(Result.Error refusal) -> Outcome.Refused(refusal.Code, refusal.Message), None
    | Result.Error(:? System.OperationCanceledException) -> Outcome.Refused(RefusalCode.Canceled, "The caller canceled this operation."), None
    | Result.Error error -> Outcome.Failed error.Message, None
  { Sequence = 0L; AtMs = atMs; Caller = caller; Operation = operation; Session = session; Project = project; Outcome = outcome; Run = run }

// Stored case names come from the existing closed protocol types. Unknown
// future cases refuse the stored row; this is not another operation table.
let private caseOf<'a> name : 'a =
  FSharpType.GetUnionCases typeof<'a>
  |> Array.tryFind (fun case -> case.Name = name && case.GetFields().Length = 0)
  |> Option.map (fun case -> FSharpValue.MakeUnion(case, [||]) :?> 'a)
  |> Option.defaultWith (fun () -> invalidOp ("Unknown stored " + typeof<'a>.Name + " case: " + name))

/// One node-local store, using the existing SQLite dependency. The transaction
/// owns sequence allocation and pruning. Entries are never updated in place.
let openStore (dbPath: string) : Result<Store, string> =
  let protect action = try Ok(action ()) with error -> Error error.Message
  let connect () =
    let options = SqliteConnectionStringBuilder()
    options.DataSource <- dbPath
    options.Pooling <- false
    let connection = new SqliteConnection(options.ToString())
    connection.Open()
    connection
  let command (connection: SqliteConnection) transaction sql =
    let command = connection.CreateCommand()
    command.Transaction <- transaction
    command.CommandText <- sql
    command
  let add (command: SqliteCommand) name (value: obj) = command.Parameters.AddWithValue(name, value) |> ignore
  let metadata connection transaction =
    use query = command connection transaction "SELECT incarnation, sequence FROM operations_meta WHERE singleton=1;"
    use row = query.ExecuteReader()
    if not (row.Read()) then invalidOp "The operations journal metadata is missing."
    row.GetString 0, row.GetInt64 1
  let readEntry (row: SqliteDataReader) =
    let outcome =
      match row.GetInt32 6 with
      | 0 -> Outcome.Succeeded
      | 1 -> Outcome.Refused(caseOf<RefusalCode> (row.GetString 7), row.GetString 8)
      | 2 -> Outcome.Failed(row.GetString 8)
      | _ -> invalidOp "Unknown stored operations outcome."
    let run =
      if row.IsDBNull 9 then None
      else Some {
        ExitCode = row.GetInt32 9; Generation = row.GetInt64 10; SourceVersion = row.GetString 11
        StandardOutput = Encoding.UTF8.GetString(row.GetFieldValue<byte[]>(12))
        StandardError = Encoding.UTF8.GetString(row.GetFieldValue<byte[]>(13))
        OutputTruncated = row.GetBoolean 14; ErrorTruncated = row.GetBoolean 15; Dropped = row.GetBoolean 16 }
    { Sequence = row.GetInt64 0; AtMs = row.GetInt64 1; Caller = row.GetString 2
      Operation = caseOf<Operation> (row.GetString 3); Session = row.GetString 4; Project = row.GetString 5
      Outcome = outcome; Run = run }
  let read connection transaction =
    let incarnation, sequence = metadata connection transaction
    use query = command connection transaction "SELECT seq,at_ms,caller,operation,session,project,outcome,refusal,message,exit_code,generation,source_version,stdout,stderr,stdout_truncated,stderr_truncated,dropped FROM operations_entries ORDER BY seq DESC LIMIT $limit;"
    add query "$limit" maxEntries
    use rows = query.ExecuteReader()
    let entries = [ while rows.Read() do yield readEntry rows ]
    { Incarnation = incarnation; Sequence = sequence; Entries = project entries; StorageError = None }
  let initialize () =
    let parent = Path.GetDirectoryName(Path.GetFullPath dbPath)
    Directory.CreateDirectory parent |> ignore
    use connection = connect ()
    use transaction = connection.BeginTransaction()
    use version = command connection transaction "PRAGMA user_version;"
    match Convert.ToInt64(version.ExecuteScalar()) with
    | 0L | 1L -> ()
    | _ -> invalidOp "Unsupported operations journal schema."
    use schema = command connection transaction "
CREATE TABLE IF NOT EXISTS operations_meta (
  singleton INTEGER PRIMARY KEY CHECK(singleton=1), incarnation TEXT NOT NULL, sequence INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS operations_entries (
  seq INTEGER PRIMARY KEY, at_ms INTEGER NOT NULL, caller TEXT NOT NULL, operation TEXT NOT NULL,
  session TEXT NOT NULL, project TEXT NOT NULL, outcome INTEGER NOT NULL, refusal TEXT NOT NULL,
  message TEXT NOT NULL, exit_code INTEGER, generation INTEGER, source_version TEXT,
  stdout BLOB, stderr BLOB, stdout_truncated INTEGER, stderr_truncated INTEGER, dropped INTEGER);
PRAGMA user_version=1;"
    schema.ExecuteNonQuery() |> ignore
    use seed = command connection transaction "INSERT OR IGNORE INTO operations_meta(singleton,incarnation,sequence) VALUES(1,$incarnation,0);"
    add seed "$incarnation" (Guid.NewGuid().ToString("N"))
    seed.ExecuteNonQuery() |> ignore
    transaction.Commit()
  protect initialize |> Result.map (fun () ->
    let readAll () = protect (fun () ->
      use connection = connect ()
      use transaction = connection.BeginTransaction()
      let current = read connection transaction
      transaction.Commit()
      current)
    let appendEntry entry = protect (fun () ->
      use connection = connect ()
      use transaction = connection.BeginTransaction()
      let _, previous = metadata connection transaction
      if previous = Int64.MaxValue then invalidOp "The operations journal sequence is exhausted."
      let entry = { bound entry with Sequence = previous + 1L }
      let outcome, refusal, message =
        match entry.Outcome with
        | Outcome.Succeeded -> 0, "", ""
        | Outcome.Refused(code, reason) -> 1, string code, reason
        | Outcome.Failed reason -> 2, "", reason
      use insert = command connection transaction "INSERT INTO operations_entries(seq,at_ms,caller,operation,session,project,outcome,refusal,message,exit_code,generation,source_version,stdout,stderr,stdout_truncated,stderr_truncated,dropped) VALUES($seq,$at,$caller,$operation,$session,$project,$outcome,$refusal,$message,$exit,$generation,$source,$stdout,$stderr,$outcut,$errcut,$dropped);"
      [ "$seq", box entry.Sequence; "$at", box entry.AtMs; "$caller", box entry.Caller
        "$operation", box (string entry.Operation); "$session", box entry.Session; "$project", box entry.Project
        "$outcome", box outcome; "$refusal", box refusal; "$message", box message ]
      |> List.iter (fun (name, value) -> add insert name value)
      let runValue project = entry.Run |> Option.map project |> Option.defaultValue (box DBNull.Value)
      [ "$exit", runValue (fun run -> box run.ExitCode); "$generation", runValue (fun run -> box run.Generation)
        "$source", runValue (fun run -> box run.SourceVersion)
        "$stdout", runValue (fun run -> box (Encoding.UTF8.GetBytes run.StandardOutput))
        "$stderr", runValue (fun run -> box (Encoding.UTF8.GetBytes run.StandardError))
        "$outcut", runValue (fun run -> box run.OutputTruncated); "$errcut", runValue (fun run -> box run.ErrorTruncated)
        "$dropped", runValue (fun run -> box run.Dropped) ]
      |> List.iter (fun (name, value) -> add insert name value)
      insert.ExecuteNonQuery() |> ignore
      use prune = command connection transaction "DELETE FROM operations_entries WHERE seq NOT IN (SELECT seq FROM operations_entries ORDER BY seq DESC LIMIT $limit);"
      add prune "$limit" maxEntries
      prune.ExecuteNonQuery() |> ignore
      use advance = command connection transaction "UPDATE operations_meta SET sequence=$seq WHERE singleton=1;"
      add advance "$seq" entry.Sequence
      advance.ExecuteNonQuery() |> ignore
      let current = read connection transaction
      transaction.Commit()
      current)
    let readTranscript (request: TranscriptRequest) =
      if request.MaxBytes < 1 || request.MaxBytes > Bozzetto.Web.Shared.Protocol.MaxTranscriptPageBytes then Error TranscriptRefusal.InvalidLimit
      elif request.Offset < 0L then Error TranscriptRefusal.InvalidCursor
      else
        protect (fun () ->
          use connection = connect ()
          use transaction = connection.BeginTransaction()
          let incarnation, _ = metadata connection transaction
          if request.Incarnation <> incarnation then Error TranscriptRefusal.ForeignIncarnation
          else
            use query = command connection transaction "SELECT exit_code,stdout,stderr,stdout_truncated,stderr_truncated,dropped FROM operations_entries WHERE seq=$seq;"
            add query "$seq" request.Sequence
            use row = query.ExecuteReader()
            if not (row.Read()) then Error TranscriptRefusal.MissingEntry
            elif row.IsDBNull 0 || row.GetBoolean 5 then Error TranscriptRefusal.NoTranscript
            else
              let column, truncated = match request.Stream with TranscriptStream.Stdout -> 1, 3 | TranscriptStream.Stderr -> 2, 4
              let bytes = row.GetFieldValue<byte[]>(column)
              if request.Offset > int64 bytes.Length then Error TranscriptRefusal.InvalidCursor
              else
                let count = min request.MaxBytes (bytes.Length - int request.Offset)
                let next = request.Offset + int64 count
                Ok { Incarnation = incarnation; Sequence = request.Sequence; Stream = request.Stream
                     Offset = request.Offset; NextOffset = if next < int64 bytes.Length then Some next else None
                     TotalBytes = int64 bytes.Length; Bytes = Array.sub bytes (int request.Offset) count
                     Truncated = row.GetBoolean truncated })
        |> Result.mapError TranscriptRefusal.StorageFailure
        |> Result.bind id
    let usage () = protect (fun () ->
      use connection = connect ()
      let scalar sql = use query = command connection null sql in query.ExecuteScalar()
      let bytes = Convert.ToInt64(scalar "PRAGMA page_count;") * Convert.ToInt64(scalar "PRAGMA page_size;")
      use query = command connection null "SELECT count(*),min(at_ms) FROM operations_entries;"
      use row = query.ExecuteReader()
      row.Read() |> ignore
      { Bytes = bytes; Rows = row.GetInt64 0; OldestMs = if row.IsDBNull 1 then None else Some(row.GetInt64 1) })
    let clear () = protect (fun () ->
      use connection = connect ()
      use transaction = connection.BeginTransaction()
      use delete = command connection transaction "DELETE FROM operations_entries;"
      let rows = int64 (delete.ExecuteNonQuery())
      // Clearing retained output never recycles an already issued identity.
      transaction.Commit()
      rows)
    { Read = readAll; Append = appendEntry; ReadTranscript = readTranscript; Usage = usage; Clear = clear })
