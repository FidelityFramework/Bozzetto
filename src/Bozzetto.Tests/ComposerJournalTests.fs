/// The operations journal's bounds: count, per-stream cut, aggregate transcript
/// budget. Each rule has a positive control, a negative control asserting the
/// reason, and a named mutation (in the comments) the test would catch.
module Bozzetto.Tests.ComposerJournalTests

open Expecto
open Expecto.Flip
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration
open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks

let private entry (caller: string) (operation: Operation) (run: ComposerJournal.Transcript option) : ComposerJournal.Entry =
  { Sequence = 0L; AtMs = 1L; Caller = caller; Context = ComposerJournal.Context.Composer(operation, "s"); Project = "/p/P.fidproj"
    Outcome = ComposerJournal.Outcome.Succeeded; Run = run }

let private transcriptOf (chars: int) : ComposerJournal.Transcript =
  { ExitCode = 0; Compiler = Some { Generation = 1L; SourceVersion = "v" }; StandardOutput = System.String('o', chars); StandardError = ""
    OutputTruncated = false; ErrorTruncated = false; Dropped = false }

let private ok = function Ok value -> value | Error reason -> failtestf "Expected success: %A" reason

let private withStore action =
  let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/journal-controls", Guid.NewGuid().ToString("N")))
  Directory.CreateDirectory directory |> ignore
  let path = Path.Combine(directory, "operations.db")
  try action path (ComposerJournal.openStore path |> ok)
  finally Directory.Delete(directory, true)

let private request (journal: ComposerJournal.Journal) sequence : ComposerJournal.TranscriptRequest =
  { Incarnation = journal.Incarnation; Sequence = sequence; Stream = ComposerJournal.TranscriptStream.Stdout; Offset = 0L; MaxBytes = 32768 }

[<Tests>]
let composerJournalTests =
  testList "Composer operations journal" [
    testCase "entries are stamped one on, newest first, and the count is bounded by dropping the oldest" <| fun () ->
      // Mutation caught: skip the truncation and the journal would hold 201 entries.
      let journal =
        (ComposerJournal.empty "j", [ 1 .. ComposerJournal.maxEntries + 1 ])
        ||> List.fold (fun j i -> ComposerJournal.append (entry ("c" + string i) Operation.Build None) j)
      journal.Sequence |> Expect.equal "one stamp per appended entry" (int64 (ComposerJournal.maxEntries + 1))
      journal.Entries.Length |> Expect.equal "the count is bounded" ComposerJournal.maxEntries
      (journal.Entries.Head.Sequence, journal.Entries.Head.Caller) |> Expect.equal "newest first" (int64 (ComposerJournal.maxEntries + 1), "c" + string (ComposerJournal.maxEntries + 1))
      (journal.Entries |> List.last).Caller |> Expect.equal "the oldest entry (c1) is gone" "c2"
      journal.Entries |> List.map _.Sequence |> List.pairwise |> List.forall (fun (a, b) -> a > b) |> Expect.isTrue "sequences strictly decrease down the list"

    testCase "a stream longer than the limit keeps its end behind one marker line; a short stream is unchanged" <| fun () ->
      // Mutation caught: keep the head instead of the end and the result line at the end would be lost.
      let short = "a short stream\n"
      ComposerJournal.cut short |> Expect.equal "unchanged" short
      let long = System.String('x', ComposerJournal.streamLimit) + "RESULT"
      let kept = ComposerJournal.cut long
      kept.StartsWith "[cut: 6 characters omitted]\n" |> Expect.isTrue "the marker names what was omitted"
      kept.EndsWith "RESULT" |> Expect.isTrue "the end of the stream is kept"
      (kept.Length - "[cut: 6 characters omitted]\n".Length) |> Expect.equal "exactly the limit is kept behind the marker" ComposerJournal.streamLimit

    testCase "a run's transcript is cut on both streams as it is journaled" <| fun () ->
      let result : Bozzetto.Providers.RunResult =
        { Generation = 3L; SourceVersion = "src"; ExitCode = 2
          StandardOutput = System.String('o', ComposerJournal.streamLimit + 1); StandardError = System.String('e', ComposerJournal.streamLimit + 2) }
      let t = ComposerJournal.transcript result
      (t.StandardOutput.StartsWith "[cut: 1 characters omitted]", t.StandardError.StartsWith "[cut: 2 characters omitted]") |> Expect.equal "both streams cut" (true, true)
      (t.ExitCode, t.Compiler, t.Dropped)
      |> Expect.equal
        "the rest carried"
        (2,
         Some {
           Generation = 3L
           SourceVersion = "src"
         },
         false)

    testCase "the aggregate transcript budget gives up the oldest transcripts first and keeps their entries, marked" <| fun () ->
      // Mutation caught: skip the budget and every entry would keep its transcript past the aggregate limit; evict by fit
      // instead of by age and a newer large transcript would go while an older small one stayed.
      let each = ComposerJournal.streamLimit
      let fits = ComposerJournal.aggregateLimit / each
      let journal =
        (ComposerJournal.empty "j", [ 1 .. fits + 1 ])
        ||> List.fold (fun j i -> ComposerJournal.append (entry ("c" + string i) Operation.Run (Some(transcriptOf each))) j)
      journal.Entries.Length |> Expect.equal "every entry is kept" (fits + 1)
      let dropped = journal.Entries |> List.filter (fun e -> e.Run |> Option.exists _.Dropped)
      dropped |> List.map _.Caller |> Expect.equal "the oldest transcript was given up" [ "c1" ]
      (dropped.Head.Run.Value.StandardOutput, dropped.Head.Run.Value.StandardError) |> Expect.equal "its streams are empty" ("", "")
      journal.Entries |> List.sumBy (fun e -> e.Run |> Option.map (fun t -> t.StandardOutput.Length + t.StandardError.Length) |> Option.defaultValue 0)
      |> Expect.equal "the kept transcripts fill the budget exactly" (fits * each)
      // Age, not fit: a newer transcript twice the size of the others arrives; the two oldest go, the newer large one stays.
      let after = ComposerJournal.append (entry "big" Operation.Run (Some(transcriptOf (2 * each)))) journal
      after.Entries |> List.filter (fun e -> e.Run |> Option.exists _.Dropped) |> List.map _.Caller |> List.sort
      |> Expect.equal "the two oldest live transcripts were given up for the newer large one" [ "c1"; "c2"; "c3" ]
      (after.Entries.Head.Caller, after.Entries.Head.Run.Value.Dropped) |> Expect.equal "the newest, largest transcript is kept" ("big", false)

    testCase "every text of an entry is bounded, whatever the producer sent" <| fun () ->
      // Mutation caught: skip the bound and a caller label or message of any length would stand in the snapshot.
      let huge = System.String('h', 10_000)
      let wide = System.String('w', 3 * ComposerJournal.streamLimit)
      let e : ComposerJournal.Entry =
        { Sequence = 0L; AtMs = 1L; Caller = huge; Context = ComposerJournal.Context.Composer(Operation.Build, huge); Project = huge
          Outcome = ComposerJournal.Outcome.Refused(RefusalCode.InvalidReservation, huge)
          Run = Some { ExitCode = 0; Compiler = Some { Generation = 1L; SourceVersion = huge }; StandardOutput = wide; StandardError = wide
                       OutputTruncated = false; ErrorTruncated = false; Dropped = false } }
      let kept = (ComposerJournal.append e (ComposerJournal.empty "j")).Entries.Head
      (kept.Caller.Length,
       (match kept.Context with
        | ComposerJournal.Context.Composer(_, session) -> session.Length
        | _ -> -1),
       kept.Project.Length)
      |> Expect.equal "labels and texts clipped" (ComposerJournal.labelLimit, ComposerJournal.labelLimit, ComposerJournal.textLimit)
      (match kept.Outcome with ComposerJournal.Outcome.Refused(_, m) -> m.Length | _ -> -1) |> Expect.equal "the refusal message clipped" ComposerJournal.textLimit
      let run = kept.Run.Value
      run.Compiler.Value.SourceVersion.Length
      |> Expect.equal "the source version clipped" ComposerJournal.labelLimit
      (run.StandardOutput.Length <= ComposerJournal.streamLimit + 64, run.StandardError.StartsWith "[cut: ") |> Expect.equal "supplied streams are cut like journaled ones" (true, true)
      // A stream already cut is left as it is: a second bound does not eat its marker.
      let again = (ComposerJournal.append kept (ComposerJournal.empty "j")).Entries.Head
      again.Run.Value.StandardOutput |> Expect.equal "idempotent" run.StandardOutput
      // A transcript supplied as dropped carries no streams: it weighs what it counts against the budget.
      let dropped = { e with Run = Some { e.Run.Value with Dropped = true } }
      let keptDropped = (ComposerJournal.append dropped (ComposerJournal.empty "j")).Entries.Head
      (keptDropped.Run.Value.StandardOutput, keptDropped.Run.Value.StandardError, keptDropped.Run.Value.Dropped) |> Expect.equal "dropped means empty" ("", "", true)

    testCase "settlement: a reply's outcome, a refusal, a cancellation and an escaped exception each journal as themselves" <| fun () ->
      // Mutation caught: journal an escaped exception as a provider refusal and the two would be indistinguishable on the page.
      let settle = ComposerJournal.settle "http" Operation.Build "s" "/p" 5L
      (settle (Result.Ok(Result.Ok Canceled))).Outcome |> Expect.equal "a reply succeeds" ComposerJournal.Outcome.Succeeded
      (settle (Result.Ok(Result.Error { Code = RefusalCode.InvalidReservation; Message = "m" }))).Outcome
      |> Expect.equal "a refusal keeps its code" (ComposerJournal.Outcome.Refused(RefusalCode.InvalidReservation, "m"))
      (settle (Result.Error(System.OperationCanceledException() :> exn))).Outcome
      |> Expect.equal "a cancellation is the Canceled refusal" (ComposerJournal.Outcome.Refused(RefusalCode.Canceled, "The caller canceled this operation."))
      (settle (Result.Error(System.InvalidOperationException "boom" :> exn))).Outcome |> Expect.equal "an escaped exception is Failed" (ComposerJournal.Outcome.Failed "boom")
      let ran = settle (Result.Ok(Result.Ok(Ran { Generation = 2L; SourceVersion = "v"; ExitCode = 1; StandardOutput = "o"; StandardError = "e" })))
      (ran.Run |> Option.map (fun t -> t.ExitCode, t.StandardOutput)) |> Expect.equal "a run carries its transcript" (Some(1, "o"))

    testCase "SQLite restart retains journal identity sequence immutable entries and byte cursors" <| fun () ->
      withStore (fun path store ->
        let text = "xλ🙂z"
        let transcript = { transcriptOf 0 with StandardOutput = text }
        let first = store.Append(entry "one" Operation.Run (Some transcript)) |> ok
        let page = store.ReadTranscript { request first 1L with MaxBytes = 2 } |> ok
        (page.Offset, page.NextOffset, page.TotalBytes, page.Truncated) |> Expect.equal "cursor describes exact UTF8 bytes" (0L, Some 2L, int64 (Encoding.UTF8.GetByteCount text), false)
        let rest = store.ReadTranscript { request first 1L with Offset = page.NextOffset.Value } |> ok
        Encoding.UTF8.GetString(Array.append page.Bytes rest.Bytes) |> Expect.equal "split codepoints are reconstructed from typed bytes" text
        let reopened = ComposerJournal.openStore path |> ok
        reopened.Read() |> ok |> Expect.equal "restart keeps the same incarnation and contents" first
        let next = reopened.Append(entry "two" Operation.Build None) |> ok
        (next.Incarnation, next.Sequence) |> Expect.equal "persistent owner allocates the next identity" (first.Incarnation, 2L)
        reopened.ReadTranscript { request first 1L with MaxBytes = 2 } |> Expect.equal "later entries never rewrite earlier transcript bytes" (Ok page))

    testCase "SQLite readback refuses foreign missing absent and invalid bounded requests specifically" <| fun () ->
      withStore (fun _ store ->
        let first = store.Append(entry "run" Operation.Run (Some(transcriptOf 3))) |> ok
        let query = request first 1L
        store.ReadTranscript { query with Incarnation = "foreign" } |> Expect.equal "no other journal alias" (Error ComposerJournal.TranscriptRefusal.ForeignIncarnation)
        store.ReadTranscript { query with Sequence = 999L } |> Expect.equal "missing is not empty output" (Error ComposerJournal.TranscriptRefusal.MissingEntry)
        let _ = store.Append(entry "build" Operation.Build None) |> ok
        store.ReadTranscript { query with Sequence = 2L } |> Expect.equal "an operation without a run has no transcript" (Error ComposerJournal.TranscriptRefusal.NoTranscript)
        for offset in [ -1L; 4L ] do
          store.ReadTranscript { query with Offset = offset } |> Expect.equal "out-of-range cursor refused" (Error ComposerJournal.TranscriptRefusal.InvalidCursor)
        for size in [ 0; Bozzetto.Web.Shared.Protocol.MaxTranscriptPageBytes + 1 ] do
          store.ReadTranscript { query with MaxBytes = size } |> Expect.equal "invalid page budget refused" (Error ComposerJournal.TranscriptRefusal.InvalidLimit)
        let eof = store.ReadTranscript { query with Offset = 3L; MaxBytes = 1 } |> ok
        (eof.Bytes, eof.NextOffset) |> Expect.equal "exact end is a valid empty final page" ([||], None)
        let empty = store.ReadTranscript { query with Stream = ComposerJournal.TranscriptStream.Stderr } |> ok
        empty.TotalBytes |> Expect.equal "present empty stderr differs from no transcript" 0L)

    testCase "SQLite retains source-cut output after live aggregate eviction and prunes oldest whole entries" <| fun () ->
      withStore (fun path store ->
        let run = { transcriptOf (ComposerJournal.streamLimit + 1) with StandardError = "small" }
        let first = store.Append(entry "first" Operation.Run (Some run)) |> ok
        for i in 2 .. ComposerJournal.aggregateLimit / ComposerJournal.streamLimit + 2 do
          store.Append(entry (string i) Operation.Run (Some(transcriptOf ComposerJournal.streamLimit))) |> ok |> ignore
        let current = store.Read() |> ok
        current.Entries |> List.find (fun e -> e.Sequence = 1L) |> _.Run |> Option.get |> _.Dropped
        |> Expect.isTrue "the live aggregate budget still evicts its oldest transcript"
        let stored = store.ReadTranscript(request first 1L) |> ok
        stored.Truncated |> Expect.isTrue "per-stream source cut is explicit"
        Encoding.UTF8.GetString stored.Bytes |> Expect.equal "live eviction never rewrites the persisted bounded source" (ComposerJournal.cut run.StandardOutput)
        let error = store.ReadTranscript { request first 1L with Stream = ComposerJournal.TranscriptStream.Stderr } |> ok
        error.Truncated |> Expect.isFalse "truncation belongs to the selected stream"
        for i in current.Sequence + 1L .. int64 ComposerJournal.maxEntries + 1L do
          store.Append(entry (string i) Operation.Build None) |> ok |> ignore
        let reopened = ComposerJournal.openStore path |> ok
        let retained = reopened.Read() |> ok
        (retained.Sequence, retained.Entries.Length, retained.Entries |> List.last |> _.Sequence)
        |> Expect.equal "restart restores newest200 only" (201L, ComposerJournal.maxEntries, 2L)
        reopened.ReadTranscript(request first 1L) |> Expect.equal "pruned output is explicitly missing" (Error ComposerJournal.TranscriptRefusal.MissingEntry))

    testCase "SQLite clearing never recycles a retained entry identity" <| fun () ->
      withStore (fun _ store ->
        let first = store.Append(entry "first" Operation.Run (Some(transcriptOf 3))) |> ok
        store.Clear() |> ok |> Expect.equal "one settled entry cleared" 1L
        (store.Usage() |> ok).Rows |> Expect.equal "usage sees the same store" 0L
        let second = store.Append(entry "second" Operation.Run (Some(transcriptOf 4))) |> ok
        (second.Incarnation, second.Sequence) |> Expect.equal "clear preserves identity high-water" (first.Incarnation, 2L)
        store.ReadTranscript(request first 1L) |> Expect.equal "old identity cannot read the replacement" (Error ComposerJournal.TranscriptRefusal.MissingEntry))

    testCase "storage failure remains visible without changing compiler outcomes or retrying durable publication" <| fun () ->
      withStore (fun _ actual ->
        let seed = actual.Append(entry "stored" Operation.Build None) |> ok
        let mutable attempts = 0
        let broken = { actual with Append = fun _ -> attempts <- attempts + 1; Error "disk refused" }
        let supervisor = ComposerSupervisor((fun () -> Task.FromException<IComposerWorker>(InvalidOperationException "unused")), false,
          (fun () -> Task.CompletedTask), journalStore = Ok broken)
        let target = { WorkerAddress.Host = ""; Epoch = ""; Provider = Bozzetto.Providers.ProviderIdentity.ClefComposer }
        for _ in 1 .. 2 do
          let result = supervisor.ExecuteAsync(Open(target, "/project.fidproj"), "test", CancellationToken.None).GetAwaiter().GetResult()
          match result.Reply.Outcome with
          | Error refusal -> refusal.Code |> Expect.equal "the actual compiler refusal is unchanged" RefusalCode.ProviderUnavailable
          | _ -> failtest "disabled compiler must remain refused"
        attempts |> Expect.equal "failed storage is latched until restart" 1
        let observed = supervisor.Journal
        (observed.StorageError, observed.Sequence, observed.Entries.Length) |> Expect.equal "newest settled operations remain visible as non-durable" (Some "disk refused", seed.Sequence + 2L, 3)
        supervisor.ReadTranscript(request seed 1L) |> Expect.equal "readback cannot imply durable success after the failure" (Error(ComposerJournal.TranscriptRefusal.StorageFailure "disk refused"))
        (actual.Read() |> ok).Sequence |> Expect.equal "failed appends never changed durable authority" seed.Sequence
        supervisor.StopAsync().GetAwaiter().GetResult())

    testCase "leased commands settle one durable cursor and retain actual command context without compiler provenance" <| fun () ->
      withStore (fun path store ->
        let command: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-one"; LeaseDisplayId = "display-lease-one"; RunId = "run-one"
            Executable = "/tool/dotnet"; Arguments = [ "build"; "project with spaces.fsproj" ]
            ProcessId = 321; StartedAtMs = 100L }
        let first, started = store.BeginCommand "holder" "/project" command |> ok
        (started.Sequence, started.Outcome, started.Run) |> Expect.equal "actual process begins without a terminal fiction" (1L, ComposerJournal.Outcome.Running, None)
        let completed: ComposerJournal.CommandResult =
          { FinishedAtMs = 200L; ExitCode = 0; StandardOutput = "built λ"; StandardError = ""
            OutputTruncated = false; ErrorTruncated = false }
        let settled, terminal = store.SettleCommand "holder" "/project" command completed |> ok
        (settled.Entries.Length, terminal.Sequence, settled.Sequence) |> Expect.equal "one stable cursor and a new journal revision" (1, 1L, 2L)
        (terminal.Context, terminal.Run.Value.Compiler) |> Expect.equal "no fabricated compiler operation or generation" (ComposerJournal.Context.LeasedCommand command, None)
        let reopened = ComposerJournal.openStore path |> ok
        reopened.Read() |> ok |> Expect.equal "terminal record survives connection and daemon restart" settled
        let page = reopened.ReadTranscript(request first started.Sequence) |> ok
        Encoding.UTF8.GetString page.Bytes |> Expect.equal "same node-local readback owner" completed.StandardOutput
        let next = reopened.Append(entry "compiler" Operation.Build None) |> ok
        next.Entries.Head.Sequence |> Expect.equal "next append is above the settlement high-water" 3L)

    testCase "leased nonzero outcomes retain both bounded streams and explicit source truncation" <| fun () ->
      withStore (fun _ store ->
        let command: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Test; LeaseId = "lease-test"; LeaseDisplayId = "display-lease-test"; RunId = "run-test"
            Executable = "/tool/dotnet"; Arguments = [ "vstest" ]; ProcessId = 322; StartedAtMs = 100L }
        let first, _ = store.BeginCommand "holder" "/project" command |> ok
        let result: ComposerJournal.CommandResult =
          { FinishedAtMs = 200L; ExitCode = 7; StandardOutput = "[cut: earlier command output omitted]\ntail"
            StandardError = "assertion failed"; OutputTruncated = true; ErrorTruncated = false }
        let _, terminal = store.SettleCommand "holder" "/project" command result |> ok
        terminal.Outcome |> Expect.equal "nonzero is a command failure" (ComposerJournal.Outcome.Failed "Command exited with code 7.")
        (terminal.Run.Value.ExitCode, terminal.Run.Value.Compiler) |> Expect.equal "actual exit without compiler metadata" (7, None)
        (store.ReadTranscript(request first 1L) |> ok).Truncated |> Expect.isTrue "client source cut stays explicit"
        let error = store.ReadTranscript { request first 1L with Stream = ComposerJournal.TranscriptStream.Stderr } |> ok
        (Encoding.UTF8.GetString error.Bytes, error.Truncated) |> Expect.equal "independent stderr fact" ("assertion failed", false))

    testCase "leased settlement refuses missing records foreign owners and changed immutable process context" <| fun () ->
      withStore (fun _ store ->
        let command: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-owned"; LeaseDisplayId = "display-lease-owned"; RunId = "run-owned"
            Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 323; StartedAtMs = 100L }
        let result: ComposerJournal.CommandResult =
          { FinishedAtMs = 200L; ExitCode = 0; StandardOutput = "ok"; StandardError = ""
            OutputTruncated = false; ErrorTruncated = false }
        store.SettleCommand "holder" "/project" command result
        |> Expect.equal "no invented start" (Error "The leased command was not begun in this journal.")
        store.BeginCommand "holder" "/project" command |> ok |> ignore
        for caller, project, changed in
          [ "other", "/project", command; "holder", "/other", command
            "holder", "/project", { command with LeaseId = "foreign" }
            "holder", "/project", { command with LeaseDisplayId = "foreign-display" }
            "holder", "/project", { command with ProcessId = 999 }
            "holder", "/project", { command with Arguments = [ "test" ] } ] do
          store.SettleCommand caller project changed result
          |> Expect.equal "issued owner and immutable context must match" (Error "The leased command owner or immutable context changed.")
        store.FindCommand "foreign" command.RunId |> Expect.equal "lookup does not cross leases" (Error "The leased command belongs to another lease.")
        (store.Read() |> ok).Sequence |> Expect.equal "rejected reports do not advance the journal" 1L)

    testCase "exact leased retries are idempotent while a changed terminal report is refused even beyond the stored tail" <| fun () ->
      withStore (fun _ store ->
        let command: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-repeat"; LeaseDisplayId = "display-lease-repeat"; RunId = "run-repeat"
            Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 324; StartedAtMs = 100L }
        let first = store.BeginCommand "holder" "/project" command |> ok
        store.BeginCommand "holder" "/project" command |> ok |> Expect.equal "lost start ack can retry the exact issued command" first
        let tail = String('x', ComposerJournal.streamLimit)
        let result: ComposerJournal.CommandResult =
          { FinishedAtMs = 200L; ExitCode = 0; StandardOutput = "a" + tail; StandardError = ""
            OutputTruncated = false; ErrorTruncated = false }
        let settled = store.SettleCommand "holder" "/project" command result |> ok
        store.SettleCommand "holder" "/project" command result |> ok |> Expect.equal "lost finish ack does not create a new cursor or revision" settled
        for changed in [ { result with ExitCode = 1 }; { result with StandardOutput = "b" + tail } ] do
          store.SettleCommand "holder" "/project" command changed
          |> Expect.equal "terminal identity binds the complete reported result before stream cutting" (Error "The leased command already has a different terminal result.")
        (store.Read() |> ok).Sequence |> Expect.equal "only begin and first finish advanced" 2L)

    testCase "running command records survive retention and clearing without granting a live lease" <| fun () ->
      withStore (fun _ store ->
        let command: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Test; LeaseId = "lease-retained"; LeaseDisplayId = "display-lease-retained"; RunId = "run-retained"
            Executable = "/tool/dotnet"; Arguments = [ "vstest" ]; ProcessId = 325; StartedAtMs = 100L }
        store.BeginCommand "holder" "/project" command |> ok |> ignore
        for i in 1 .. ComposerJournal.maxEntries do
          store.Append(entry (string i) Operation.Build None) |> ok |> ignore
        let retained = store.FindCommand command.LeaseId command.RunId |> ok
        retained.Sequence |> Expect.equal "unrelated settled operations cannot evict an unfinished record" 1L
        store.Clear() |> ok |> ignore
        (store.Read() |> ok).Entries |> Expect.equal "output clearing preserves the unfinished observation, not a claim of liveness" [ retained ]
        let result: ComposerJournal.CommandResult =
          { FinishedAtMs = 200L; ExitCode = 0; StandardOutput = "done"; StandardError = ""
            OutputTruncated = false; ErrorTruncated = false }
        let settled, terminal = store.SettleCommand "holder" "/project" command result |> ok
        (terminal.Sequence, settled.Sequence) |> Expect.equal "cursor stays issued and version remains above all prior entries" (1L, 202L))

    testCase "legacy SQLite compiler rows migrate without changing compiler provenance or cursor identity" <| fun () ->
      let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/journal-controls", Guid.NewGuid().ToString("N")))
      Directory.CreateDirectory directory |> ignore
      let path = Path.Combine(directory, "legacy.db")
      try
        do
          use connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False")
          connection.Open()
          use create = connection.CreateCommand()
          create.CommandText <- "
CREATE TABLE operations_meta(singleton INTEGER PRIMARY KEY,incarnation TEXT NOT NULL,sequence INTEGER NOT NULL);
CREATE TABLE operations_entries(seq INTEGER PRIMARY KEY,at_ms INTEGER NOT NULL,caller TEXT NOT NULL,operation TEXT NOT NULL,session TEXT NOT NULL,project TEXT NOT NULL,outcome INTEGER NOT NULL,refusal TEXT NOT NULL,message TEXT NOT NULL,exit_code INTEGER,generation INTEGER,source_version TEXT,stdout BLOB,stderr BLOB,stdout_truncated INTEGER,stderr_truncated INTEGER,dropped INTEGER);
INSERT INTO operations_meta VALUES(1,'legacy',5);
INSERT INTO operations_entries VALUES(5,100,'compiler','Run','session','/project',0,'','',0,4,'source',X'6869',X'',0,0,0);
PRAGMA user_version=1;"
          create.ExecuteNonQuery() |> ignore
        let journal = (ComposerJournal.openStore path |> ok).Read() |> ok
        (journal.Incarnation, journal.Sequence, journal.Entries.Head.Sequence) |> Expect.equal "no cold identity reset during schema migration" ("legacy", 5L, 5L)
        journal.Entries.Head.Context |> Expect.equal "stored protocol operation remains a compiler operation" (ComposerJournal.Context.Composer(Operation.Run, "session"))
        journal.Entries.Head.Run.Value.Compiler |> Expect.equal "actual compiler provenance retained" (Some { Generation = 4L; SourceVersion = "source" })
      finally Directory.Delete(directory, true)

    testCase "supervisor publishes persisted leased transitions and refuses unavailable storage without accepting memory fallback" <| fun () ->
      withStore (fun _ actual ->
        let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "no compiler process needed")
        let supervisor = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok actual)
        let mutable notifications = 0
        use subscription = supervisor.JournalChanged.Subscribe(fun () -> notifications <- notifications + 1)
        let command: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-observed"; LeaseDisplayId = "display-lease-observed"; RunId = "run-observed"
            Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 326; StartedAtMs = 100L }
        supervisor.BeginLeasedCommand("holder", "/project", command) |> ok |> ignore
        let result: ComposerJournal.CommandResult =
          { FinishedAtMs = 200L; ExitCode = 0; StandardOutput = "done"; StandardError = ""
            OutputTruncated = false; ErrorTruncated = false }
        supervisor.SettleLeasedCommand("holder", "/project", command, result) |> ok |> ignore
        supervisor.SettleLeasedCommand("holder", "/project", command, result) |> ok |> ignore
        notifications |> Expect.equal "begin and settle publish, exact retry does not" 2
        let broken = { actual with BeginCommand = fun _ _ _ -> Error "disk refused" }
        let refusing = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok broken)
        refusing.BeginLeasedCommand("holder", "/project", { command with RunId = "second" })
        |> Expect.equal "durability failure cannot be accepted" (Error "disk refused")
        refusing.Journal |> Expect.equal "failed publication never invents a running row" supervisor.Journal
        let absent = ComposerSupervisor(factory, false)
        absent.BeginLeasedCommand("holder", "/project", command)
        |> Expect.equal "no transient substitute for the store" (Error "Leased command observation requires a durable operations journal.")
        for owner in [ supervisor; refusing; absent ] do owner.StopAsync().GetAwaiter().GetResult())
  ]
