/// The daemon's record of Composer operations and leased process observations:
/// who asked for what, what came of it, and a run's output. Composer operations settle in the
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

[<RequireQualifiedAccess>]
type LeasedKind = Build | Test

type LeasedCommand = {
  Kind: LeasedKind
  /// Private admission capability, never projected to the browser.
  LeaseId: string
  /// Existing lease owner projection identity, safe for Work correlation.
  LeaseDisplayId: string
  RunId: string
  Executable: string
  Arguments: string list
  ProcessId: int
  StartedAtMs: int64
}

[<RequireQualifiedAccess>]
type Context =
  | Composer of Operation * session: string
  | LeasedCommand of LeasedCommand

type CompilerTranscript = { Generation: int64; SourceVersion: string }

type CommandResult = {
  FinishedAtMs: int64
  ExitCode: int
  StandardOutput: string
  StandardError: string
  OutputTruncated: bool
  ErrorTruncated: bool
}

/// Compiler provenance is absent for an ordinary leased process.
/// Dropped marks a transcript given up for the live projection's space budget.
type Transcript = {
  ExitCode: int
  Compiler: CompilerTranscript option
  StandardOutput: string
  StandardError: string
  OutputTruncated: bool
  ErrorTruncated: bool
  Dropped: bool
}

[<RequireQualifiedAccess>]
type Outcome =
  | Running
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
  Context: Context
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
  BeginCommand: string -> string -> LeasedCommand -> Result<Journal * Entry, string>
  SettleCommand: string -> string -> LeasedCommand -> CommandResult -> Result<Journal * Entry, string>
  FindCommand: string -> string -> Result<Entry, string>
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
  { ExitCode = run.ExitCode; Compiler = Some { Generation = run.Generation; SourceVersion = run.SourceVersion }
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
      Context =
        match entry.Context with
        | Context.Composer(operation, session) -> Context.Composer(operation, clip labelLimit session)
        | Context.LeasedCommand _ -> entry.Context
      Project = clip textLimit entry.Project
      Outcome =
        match entry.Outcome with
        | Outcome.Refused(code, message) -> Outcome.Refused(code, clip textLimit message)
        | Outcome.Failed message -> Outcome.Failed(clip textLimit message)
        | Outcome.Running -> Outcome.Running
        | Outcome.Succeeded -> Outcome.Succeeded
      Run =
        entry.Run |> Option.map (fun t ->
          // A transcript marked dropped holds no streams, whatever the producer sent, so it weighs what it counts.
          if t.Dropped then { t with Compiler = t.Compiler |> Option.map (fun c -> { c with SourceVersion = clip labelLimit c.SourceVersion }); StandardOutput = ""; StandardError = "" }
          else
            let output, error = cut t.StandardOutput, cut t.StandardError
            { t with Compiler = t.Compiler |> Option.map (fun c -> { c with SourceVersion = clip labelLimit c.SourceVersion })
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
  {
    Sequence = 0L
    AtMs = atMs
    Caller = caller
    Context = Context.Composer(operation, session)
    Project = project
    Outcome = outcome
    Run = run
  }

// Stored case names come from the existing closed protocol types. Unknown
// future cases refuse the stored row; this is not another operation table.
let private caseOf<'a> name : 'a =
  FSharpType.GetUnionCases typeof<'a>
  |> Array.tryFind (fun case -> case.Name = name && case.GetFields().Length = 0)
  |> Option.map (fun case -> FSharpValue.MakeUnion(case, [||]) :?> 'a)
  |> Option.defaultWith (fun () -> invalidOp ("Unknown stored " + typeof<'a>.Name + " case: " + name))

/// One node-local store, using the existing SQLite dependency. The transaction
/// owns sequence allocation and pruning. A leased command settles its existing
/// row once; its transcript cursor stays stable while the journal version advances.
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
  let encodeArguments (arguments: string list) =
    use stream = new MemoryStream()
    use writer = new BinaryWriter(stream, Encoding.UTF8, true)
    writer.Write(List.length arguments)
    arguments |> List.iter (fun argument -> writer.Write(argument: string))
    writer.Flush()
    stream.ToArray()
  let decodeArguments bytes =
    use stream = new MemoryStream(bytes: byte array)
    use reader = new BinaryReader(stream, Encoding.UTF8, true)
    let count = reader.ReadInt32()
    if count < 0 || count > 256 then invalidOp "Invalid stored command arguments."
    let arguments = [ for _ in 1 .. count -> reader.ReadString() ]
    if stream.Position <> stream.Length then invalidOp "Trailing stored command arguments."
    arguments
  let resultIdentity (result: CommandResult) =
    use stream = new MemoryStream()
    use writer = new BinaryWriter(stream, Encoding.UTF8, true)
    writer.Write result.FinishedAtMs
    writer.Write result.ExitCode
    writer.Write result.StandardOutput
    writer.Write result.StandardError
    writer.Write result.OutputTruncated
    writer.Write result.ErrorTruncated
    writer.Flush()
    System.Security.Cryptography.SHA256.HashData(stream.ToArray()) |> Convert.ToHexString
  let columns = "seq,at_ms,caller,operation,session,project,outcome,refusal,message,exit_code,generation,source_version,stdout,stderr,stdout_truncated,stderr_truncated,dropped,command_kind,lease_id,run_id,executable,arguments,process_id,started_at_ms,terminal_digest,lease_display_id"
  let readEntry (row: SqliteDataReader) =
    let context =
      if row.IsDBNull 17 then
        Context.Composer(caseOf<Operation>(row.GetString 3), row.GetString 4)
      else
        Context.LeasedCommand {
          Kind = caseOf<LeasedKind>(row.GetString 17)
          LeaseId = row.GetString 18
          RunId = row.GetString 19
          LeaseDisplayId = row.GetString 25
          Executable = row.GetString 20
          Arguments = decodeArguments (row.GetFieldValue<byte[]>(21))
          ProcessId = row.GetInt32 22
          StartedAtMs = row.GetInt64 23
        }
    let outcome =
      match row.GetInt32 6 with
      | 0 -> Outcome.Succeeded
      | 1 -> Outcome.Refused(caseOf<RefusalCode> (row.GetString 7), row.GetString 8)
      | 2 -> Outcome.Failed(row.GetString 8)
      | 3 -> Outcome.Running
      | _ -> invalidOp "Unknown stored operations outcome."
    let run =
      if row.IsDBNull 9 then None
      else Some {
        ExitCode = row.GetInt32 9
        Compiler = if row.IsDBNull 10 && row.IsDBNull 11 then None
                   elif row.IsDBNull 10 || row.IsDBNull 11 then invalidOp "Incomplete stored compiler provenance."
                   else Some { Generation = row.GetInt64 10; SourceVersion = row.GetString 11 }
        StandardOutput = Encoding.UTF8.GetString(row.GetFieldValue<byte[]>(12))
        StandardError = Encoding.UTF8.GetString(row.GetFieldValue<byte[]>(13))
        OutputTruncated = row.GetBoolean 14; ErrorTruncated = row.GetBoolean 15; Dropped = row.GetBoolean 16 }
    { Sequence = row.GetInt64 0; AtMs = row.GetInt64 1; Caller = row.GetString 2
      Context = context; Project = row.GetString 5; Outcome = outcome; Run = run }
  let read connection transaction =
    let incarnation, sequence = metadata connection transaction
    use query =
      command connection transaction ("SELECT " + columns + " FROM operations_entries ORDER BY seq DESC LIMIT $limit;")
    add query "$limit" maxEntries
    use rows = query.ExecuteReader()
    let entries = [ while rows.Read() do yield readEntry rows ]
    { Incarnation = incarnation; Sequence = sequence; Entries = project entries; StorageError = None }
  let find connection transaction runId =
    use query =
      command connection transaction ("SELECT " + columns + " FROM operations_entries WHERE run_id=$run;")

    add query "$run" runId
    use row = query.ExecuteReader()
    if row.Read() then Some(readEntry row) else None
  let initialize () =
    let parent = Path.GetDirectoryName(Path.GetFullPath dbPath)
    Directory.CreateDirectory parent |> ignore
    use connection = connect ()
    use transaction = connection.BeginTransaction()
    use version = command connection transaction "PRAGMA user_version;"
    let previous = Convert.ToInt64(version.ExecuteScalar())
    match previous with
    | 0L | 1L | 2L -> ()
    | _ -> invalidOp "Unsupported operations journal schema."
    use schema = command connection transaction "
CREATE TABLE IF NOT EXISTS operations_meta (
  singleton INTEGER PRIMARY KEY CHECK(singleton=1), incarnation TEXT NOT NULL, sequence INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS operations_entries (
  seq INTEGER PRIMARY KEY, at_ms INTEGER NOT NULL, caller TEXT NOT NULL, operation TEXT NOT NULL,
  session TEXT NOT NULL, project TEXT NOT NULL, outcome INTEGER NOT NULL, refusal TEXT NOT NULL,
  message TEXT NOT NULL, exit_code INTEGER, generation INTEGER, source_version TEXT,
  stdout BLOB, stderr BLOB, stdout_truncated INTEGER, stderr_truncated INTEGER, dropped INTEGER);"
    schema.ExecuteNonQuery() |> ignore
    if previous < 2L then
      use migrate =
        command
          connection
          transaction
          "
    ALTER TABLE operations_entries ADD COLUMN command_kind TEXT;
    ALTER TABLE operations_entries ADD COLUMN lease_id TEXT;
    ALTER TABLE operations_entries ADD COLUMN run_id TEXT;
    ALTER TABLE operations_entries ADD COLUMN executable TEXT;
    ALTER TABLE operations_entries ADD COLUMN arguments BLOB;
    ALTER TABLE operations_entries ADD COLUMN process_id INTEGER;
    ALTER TABLE operations_entries ADD COLUMN started_at_ms INTEGER;
    ALTER TABLE operations_entries ADD COLUMN terminal_digest TEXT;
    ALTER TABLE operations_entries ADD COLUMN lease_display_id TEXT;
    CREATE UNIQUE INDEX operations_command_run ON operations_entries(run_id) WHERE run_id IS NOT NULL;
    PRAGMA user_version=2;"

      migrate.ExecuteNonQuery() |> ignore
    use seed = command connection transaction "INSERT OR IGNORE INTO operations_meta(singleton,incarnation,sequence) VALUES(1,$incarnation,0);"
    add seed "$incarnation" (Guid.NewGuid().ToString("N"))
    seed.ExecuteNonQuery() |> ignore
    transaction.Commit()
  let advance connection transaction =
    let _, previous = metadata connection transaction
    if previous = Int64.MaxValue then invalidOp "The operations journal sequence is exhausted."
    let next = previous + 1L
    use update = command connection transaction "UPDATE operations_meta SET sequence=$seq WHERE singleton=1;"
    add update "$seq" next
    update.ExecuteNonQuery() |> ignore
    next
  let write connection transaction updating (terminalDigest: string option) (entry: Entry) =
    let outcome, refusal, message =
      match entry.Outcome with
      | Outcome.Succeeded -> 0, "", ""
      | Outcome.Refused(code, reason) -> 1, string code, reason
      | Outcome.Failed reason -> 2, "", reason
      | Outcome.Running -> 3, "", ""
    let operation, session, leased =
      match entry.Context with
      | Context.Composer(operation, session) -> string operation, session, None
      | Context.LeasedCommand context -> "LeasedCommand", "", Some context
    let parameters = "$seq,$at,$caller,$operation,$session,$project,$outcome,$refusal,$message,$exit,$generation,$source,$stdout,$stderr,$outcut,$errcut,$dropped,$kind,$lease,$run,$executable,$arguments,$pid,$started,$terminal,$display"
    let sql =
      if updating then
        "UPDATE operations_entries SET at_ms=$at,caller=$caller,operation=$operation,session=$session,project=$project,outcome=$outcome,refusal=$refusal,message=$message,exit_code=$exit,generation=$generation,source_version=$source,stdout=$stdout,stderr=$stderr,stdout_truncated=$outcut,stderr_truncated=$errcut,dropped=$dropped,command_kind=$kind,lease_id=$lease,run_id=$run,executable=$executable,arguments=$arguments,process_id=$pid,started_at_ms=$started,terminal_digest=$terminal,lease_display_id=$display WHERE seq=$seq;"
      else "INSERT INTO operations_entries(" + columns + ") VALUES(" + parameters + ");"
    use save = command connection transaction sql
    [ "$seq", box entry.Sequence; "$at", box entry.AtMs; "$caller", box entry.Caller
      "$operation", box operation; "$session", box session; "$project", box entry.Project
      "$outcome", box outcome; "$refusal", box refusal; "$message", box message ]
    |> List.iter (fun (name, value) -> add save name value)
    let value select item = item |> Option.map select |> Option.defaultValue (box DBNull.Value)
    let compiler = entry.Run |> Option.bind _.Compiler
    [ "$exit", value (fun (run: Transcript) -> box run.ExitCode) entry.Run
      "$generation", value (fun (source: CompilerTranscript) -> box source.Generation) compiler
      "$source", value (fun (source: CompilerTranscript) -> box source.SourceVersion) compiler
      "$stdout", value (fun (run: Transcript) -> box (Encoding.UTF8.GetBytes run.StandardOutput)) entry.Run
      "$stderr", value (fun (run: Transcript) -> box (Encoding.UTF8.GetBytes run.StandardError)) entry.Run
      "$outcut", value (fun (run: Transcript) -> box run.OutputTruncated) entry.Run
      "$errcut", value (fun (run: Transcript) -> box run.ErrorTruncated) entry.Run
      "$dropped", value (fun (run: Transcript) -> box run.Dropped) entry.Run
      "$kind", value (fun (context: LeasedCommand) -> box (string context.Kind)) leased
      "$lease", value (fun (context: LeasedCommand) -> box context.LeaseId) leased
      "$display", value (fun (context: LeasedCommand) -> box context.LeaseDisplayId) leased
      "$run", value (fun (context: LeasedCommand) -> box context.RunId) leased
      "$executable", value (fun (context: LeasedCommand) -> box context.Executable) leased
      "$arguments", value (fun (context: LeasedCommand) -> box (encodeArguments context.Arguments)) leased
      "$pid", value (fun (context: LeasedCommand) -> box context.ProcessId) leased
      "$started", value (fun (context: LeasedCommand) -> box context.StartedAtMs) leased
      "$terminal", terminalDigest |> Option.map box |> Option.defaultValue (box DBNull.Value) ]
    |> List.iter (fun (name, item) -> add save name item)
    if save.ExecuteNonQuery() <> 1 then invalidOp "The journal row was not written."
    // A running command cannot lose its record to unrelated settled operations.
    use prune = command connection transaction "DELETE FROM operations_entries WHERE seq NOT IN (SELECT seq FROM operations_entries ORDER BY (outcome=3) DESC,seq DESC LIMIT $limit);"
    add prune "$limit" maxEntries
    prune.ExecuteNonQuery() |> ignore
  let validate caller project (context: LeasedCommand) =
    let bounded limit (text: string) = not (String.IsNullOrWhiteSpace text) && text.Length <= limit
    if not (bounded labelLimit caller && bounded textLimit project
            && bounded labelLimit context.LeaseId && bounded labelLimit context.LeaseDisplayId && bounded labelLimit context.RunId
            && bounded 4096 context.Executable && Path.IsPathRooted context.Executable)
       || context.ProcessId <= 0 || context.StartedAtMs <= 0L
       || context.Arguments.Length > 256
       || (context.Arguments |> List.exists isNull)
       || (context.Arguments |> List.sumBy (fun arg -> if isNull arg then 0 else Encoding.UTF8.GetByteCount arg)) > 32768 then
      invalidOp "Invalid leased command identity or bounded command metadata."
  let requireContext caller project context (entry: Entry) =
    if entry.Caller <> caller || entry.Project <> project || entry.Context <> Context.LeasedCommand context then
      invalidOp "The leased command owner or immutable context changed."
  protect initialize |> Result.map (fun () ->
    let readAll () = protect (fun () ->
      use connection = connect ()
      use transaction = connection.BeginTransaction()
      let current = read connection transaction
      transaction.Commit()
      current)
    let appendEntry entry = protect (fun () ->
      match entry.Context with
      | Context.LeasedCommand _ -> invalidOp "A leased command must use its begin and settle journal boundary."
      | Context.Composer _ -> ()
      use connection = connect ()
      use transaction = connection.BeginTransaction()
      let entry = { bound entry with Sequence = advance connection transaction }
      write connection transaction false None entry
      let current = read connection transaction
      transaction.Commit()
      current)
    let beginCommand caller project context = protect (fun () ->
      validate caller project context
      use connection = connect ()
      use transaction = connection.BeginTransaction()
      let entry =
        match find connection transaction context.RunId with
        | Some entry -> requireContext caller project context entry; entry
        | None ->
          use active = command connection transaction "SELECT count(*) FROM operations_entries WHERE outcome=3;"
          if Convert.ToInt64(active.ExecuteScalar()) >= int64 maxEntries then invalidOp "The journal has no room for another running command."
          let entry = { Sequence = advance connection transaction; AtMs = context.StartedAtMs; Caller = caller
                        Context = Context.LeasedCommand context; Project = project; Outcome = Outcome.Running; Run = None }
          write connection transaction false None entry
          entry
      let current = read connection transaction
      transaction.Commit()
      current, entry)
    let settleCommand caller project context (result: CommandResult) = protect (fun () ->
      validate caller project context
      if result.FinishedAtMs < context.StartedAtMs || isNull result.StandardOutput || isNull result.StandardError then
        invalidOp "Invalid leased command terminal result."
      use connection = connect ()
      use transaction = connection.BeginTransaction()
      let before = find connection transaction context.RunId |> Option.defaultWith (fun () -> invalidOp "The leased command was not begun in this journal.")
      requireContext caller project context before
      let after = bound { before with AtMs = result.FinishedAtMs
                                      Outcome = if result.ExitCode = 0 then Outcome.Succeeded else Outcome.Failed(sprintf "Command exited with code %d." result.ExitCode)
                                      Run = Some { ExitCode = result.ExitCode; Compiler = None; StandardOutput = result.StandardOutput
                                                   StandardError = result.StandardError; OutputTruncated = result.OutputTruncated; ErrorTruncated = result.ErrorTruncated; Dropped = false } }
      let digest = resultIdentity result
      match before.Outcome with
      | Outcome.Running ->
        advance connection transaction |> ignore
        write connection transaction true (Some digest) after
      | _ ->
        use prior = command connection transaction "SELECT terminal_digest FROM operations_entries WHERE seq=$seq;"
        add prior "$seq" before.Sequence
        let recorded = prior.ExecuteScalar()
        if before <> after || isNull recorded || recorded = box DBNull.Value || string recorded <> digest then
          invalidOp "The leased command already has a different terminal result."
      let current = read connection transaction
      transaction.Commit()
      current, after)
    let findCommand leaseId runId = protect (fun () ->
      use connection = connect ()
      let entry = find connection null runId |> Option.defaultWith (fun () -> invalidOp "The leased command is not retained in this journal.")
      match entry.Context with
      | Context.LeasedCommand context when context.LeaseId = leaseId -> entry
      | _ -> invalidOp "The leased command belongs to another lease.")
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
      use delete =
        command connection transaction "DELETE FROM operations_entries WHERE outcome<>3;"
      let rows = int64 (delete.ExecuteNonQuery())
      // Clearing retained output never recycles an already issued identity.
      transaction.Commit()
      rows)
    { Read = readAll; Append = appendEntry; BeginCommand = beginCommand; SettleCommand = settleCommand; FindCommand = findCommand
      ReadTranscript = readTranscript; Usage = usage; Clear = clear })
