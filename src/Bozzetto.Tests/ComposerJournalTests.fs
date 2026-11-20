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

    testCaseAsync "reconciling running leased commands distinguishes live, exited, PID-reused, and inaccessible/unknown owners, honors live-lease grace, and preserves late settlement" (async {
      let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/journal-controls", Guid.NewGuid().ToString("N")))
      Directory.CreateDirectory directory |> ignore
      let path = Path.Combine(directory, "operations.db")
      let store = ComposerJournal.openStore path |> ok
      let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "no compiler process needed")
      let supervisor = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok store)
      let! outcome = async {
        try
          let liveCmd: ComposerJournal.LeasedCommand =
            { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-live"; LeaseDisplayId = "disp-live"; RunId = "run-live"
              Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 5001; StartedAtMs = 1_000L }
          let deadCmd: ComposerJournal.LeasedCommand =
            { Kind = ComposerJournal.LeasedKind.Test; LeaseId = "lease-dead"; LeaseDisplayId = "disp-dead"; RunId = "run-dead"
              Executable = "/tool/dotnet"; Arguments = [ "vstest" ]; ProcessId = 5002; StartedAtMs = 1_000L }
          let reusedCmd: ComposerJournal.LeasedCommand =
            { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-reused"; LeaseDisplayId = "disp-reused"; RunId = "run-reused"
              Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 5003; StartedAtMs = 1_000L }
          let unknownCmd: ComposerJournal.LeasedCommand =
            { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-unknown"; LeaseDisplayId = "disp-unknown"; RunId = "run-unknown"
              Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 5004; StartedAtMs = 1_000L }
          let graceCmd: ComposerJournal.LeasedCommand =
            { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-grace"; LeaseDisplayId = "disp-grace"; RunId = "run-grace"
              Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 5005; StartedAtMs = 1_000L }
          supervisor.BeginLeasedCommand("holder-live", "/project-a", liveCmd) |> ok |> ignore
          supervisor.BeginLeasedCommand("holder-dead", "/project-b", deadCmd) |> ok |> ignore
          supervisor.BeginLeasedCommand("holder-reused", "/project-c", reusedCmd) |> ok |> ignore
          supervisor.BeginLeasedCommand("holder-unknown", "/project-d", unknownCmd) |> ok |> ignore
          supervisor.BeginLeasedCommand("holder-grace", "/project-e", graceCmd) |> ok |> ignore
          let mutable notifications = 0
          use _sub = supervisor.JournalChanged.Subscribe(fun () -> notifications <- notifications + 1)
          let released = ResizeArray<string>()
          let mutable unknownNowAlive = false
          let probe pid _startedAt =
            match pid with
            | 5001 -> Bozzetto.ProcessWatchdog.ProcessLiveness.Alive
            | 5002 -> Bozzetto.ProcessWatchdog.ProcessLiveness.Exited
            | 5003 -> Bozzetto.ProcessWatchdog.ProcessLiveness.PidReused 8_500L
            | 5004 ->
              if unknownNowAlive then Bozzetto.ProcessWatchdog.ProcessLiveness.Alive
              else Bozzetto.ProcessWatchdog.ProcessLiveness.Unknown "stat inaccessible"
            | 5005 -> Bozzetto.ProcessWatchdog.ProcessLiveness.Exited
            | _ -> Bozzetto.ProcessWatchdog.ProcessLiveness.Exited
          let hasLiveLease leaseId = leaseId = "lease-live" || leaseId = "lease-grace" || leaseId = "lease-unknown"
          let abandoned, unknown, verifiedAlive =
            match supervisor.ReconcileLeasedCommands(probe, hasLiveLease, 5_000L, 9_000L, fun leaseId -> released.Add leaseId) with
            | ComposerJournal.ReconcileResult.Reconciled(_, items, unk, alive) -> items, unk, alive
            | ComposerJournal.ReconcileResult.StorageFailure reason -> failtestf "Expected reconciliation success: %s" reason
          abandoned |> List.map _.RunId |> Expect.equal "dead (no live lease) and PID-reused entries are reconciled; live-lease exited entry is deferred inside grace" [ "run-dead"; "run-reused" ]
          unknown |> List.map (fst >> _.RunId) |> Expect.equal "inaccessible entry is reported unknown" [ "run-unknown" ]
          verifiedAlive |> Expect.equal "live process is confirmed alive" [ "run-live" ]
          supervisor.LastLivenessVerifiedAtMs "run-live" |> Expect.equal "owner liveness heartbeat tracked separately from entry AtMs" (Some 9_000L)
          Seq.toList released |> Expect.equal "only proven abandoned leases are released" [ "lease-dead"; "lease-reused" ]
          notifications |> Expect.equal "reconciliation publishes one journal change" 1
          let board = Bozzetto.Server.UiBridge.Project.operations 7L supervisor.Journal
          let entryFor runId =
            board.Entries
            |> Array.pick (fun e ->
              match e.Lease with
              | Some l when l.RunId = runId -> Some e
              | _ -> None)
          let liveEntry = entryFor "run-live"
          (liveEntry.Code, liveEntry.Ok, liveEntry.AtMs) |> Expect.equal "live process stays running without fabricated AtMs" ("running", false, 1_000L)
          let graceEntry = entryFor "run-grace"
          graceEntry.Code |> Expect.equal "exited process with live lease stays running within settle grace window" "running"
          let deadEntry = entryFor "run-dead"
          deadEntry.Code |> Expect.equal "dead command projects as abandoned" "abandoned"
          deadEntry.Ok |> Expect.isFalse "abandoned command is not ok"
          deadEntry.Run |> Expect.equal "abandoned command does not invent an exit result" None
          deadEntry.Message |> Expect.stringContains "abandoned message records dead pid" "Process 5002"
          let reusedEntry = entryFor "run-reused"
          reusedEntry.Code |> Expect.equal "PID-reused command projects as abandoned" "abandoned"
          reusedEntry.Message |> Expect.stringContains "reused message records observed start" "8500"
          let unknownEntry = entryFor "run-unknown"
          unknownEntry.Code |> Expect.equal "inaccessible process projects as unknown" "unknown"
          unknownEntry.Run |> Expect.equal "unknown command does not invent an exit result" None
          unknownEntry.AtMs |> Expect.equal "unknown transition does not forge a heartbeat timestamp" 1_000L
          // Second pass inside grace window is idempotent: no further releases or journal updates.
          let secondAbandoned, secondUnknown, _ =
            match supervisor.ReconcileLeasedCommands(probe, hasLiveLease, 5_000L, 10_000L, fun leaseId -> released.Add leaseId) with
            | ComposerJournal.ReconcileResult.Reconciled(_, items, unk, alive) -> items, unk, alive
            | ComposerJournal.ReconcileResult.StorageFailure reason -> failtestf "Expected idempotent reconciliation: %s" reason
          secondAbandoned |> Expect.isEmpty "already abandoned rows and in-grace row are not re-abandoned"
          secondUnknown |> List.map (fst >> _.RunId) |> Expect.equal "unknown row remains reported without churn" [ "run-unknown" ]
          notifications |> Expect.equal "idempotent reconciliation does not re-publish" 1
          // Once settle grace elapses (>= 5000ms past 9000L) AND unknown regains access (Unknown -> Alive -> Running):
          unknownNowAlive <- true
          let thirdAbandoned, _, thirdAlive =
            match supervisor.ReconcileLeasedCommands(probe, hasLiveLease, 5_000L, 14_500L, fun leaseId -> released.Add leaseId) with
            | ComposerJournal.ReconcileResult.Reconciled(_, items, unk, alive) -> items, unk, alive
            | ComposerJournal.ReconcileResult.StorageFailure reason -> failtestf "Expected post-grace reconciliation: %s" reason
          thirdAbandoned |> List.map _.RunId |> Expect.equal "exited command past live-lease grace is abandoned" [ "run-grace" ]
          thirdAlive |> List.sort |> Expect.equal "both live and recovered unknown process verified alive" [ "run-live"; "run-unknown" ]
          // Reopening durable SQLite proves reconciled states (including Unknown -> Running recovery) persisted durably.
          let reopened = ComposerJournal.openStore path |> ok
          let persisted = reopened.Read() |> ok
          let persistedOutcome runId =
            persisted.Entries
            |> List.pick (fun e ->
              match e.Context with
              | ComposerJournal.Context.LeasedCommand c when c.RunId = runId -> Some e.Outcome
              | _ -> None)
          persistedOutcome "run-live" |> Expect.equal "live row persisted" ComposerJournal.Outcome.Running
          persistedOutcome "run-unknown" |> Expect.equal "unknown row recovered to Running durably" ComposerJournal.Outcome.Running
          (match persistedOutcome "run-dead" with ComposerJournal.Outcome.Abandoned _ -> true | _ -> false)
          |> Expect.isTrue "dead row durably abandoned"
          // Late authoritative CommandFinished after Abandoned settles truthfully without HTTP 409,
          // while a subsequent conflicting real settlement is still refused.
          let lateSettled =
            supervisor.SettleLeasedCommand(
              "holder-grace",
              "/project-e",
              graceCmd,
              { FinishedAtMs = 15_000L; ExitCode = 0; StandardOutput = "passed\n"; StandardError = ""; OutputTruncated = false; ErrorTruncated = false })
            |> ok
          lateSettled.Outcome |> Expect.equal "late authoritative settlement supersedes Abandoned" ComposerJournal.Outcome.Succeeded
          let conflicting =
            supervisor.SettleLeasedCommand(
              "holder-grace",
              "/project-e",
              graceCmd,
              { FinishedAtMs = 15_500L; ExitCode = 1; StandardOutput = ""; StandardError = "conflict\n"; OutputTruncated = false; ErrorTruncated = false })
          Result.isError conflicting |> Expect.isTrue "conflicting settlement after real terminal digest is still refused"
          return Ok ()
        with ex -> return Error ex
      }
      do! supervisor.StopAsync() |> Async.AwaitTask
      if Directory.Exists directory then Directory.Delete(directory, true)
      match outcome with
      | Ok () -> ()
      | Error ex -> return raise ex
    })

    testCaseAsync "workSummary retains freshly abandoned older rows ahead of older settled entries (#21)" (async {
      let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/journal-controls", Guid.NewGuid().ToString("N")))
      Directory.CreateDirectory directory |> ignore
      let path = Path.Combine(directory, "operations.db")
      let store = ComposerJournal.openStore path |> ok
      let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "no compiler process needed")
      let supervisor = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok store)
      let! outcome = async {
        try
          // 8 week-old running rows at seq 1..8, followed by 25 newer settled rows at seq 9..33.
          for i in 1 .. 8 do
            let cmd: ComposerJournal.LeasedCommand =
              { Kind = ComposerJournal.LeasedKind.Build; LeaseId = sprintf "lease-old-%d" i; LeaseDisplayId = sprintf "disp-old-%d" i
                RunId = sprintf "run-old-%d" i; Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 7000 + i; StartedAtMs = 1_000L + int64 i }
            supervisor.BeginLeasedCommand("old-holder", sprintf "/old-%d" i, cmd) |> ok |> ignore
          for i in 1 .. 25 do
            let cmd: ComposerJournal.LeasedCommand =
              { Kind = ComposerJournal.LeasedKind.Build; LeaseId = sprintf "lease-new-%d" i; LeaseDisplayId = sprintf "disp-new-%d" i
                RunId = sprintf "run-new-%d" i; Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 8000 + i; StartedAtMs = 5_000L + int64 i }
            supervisor.BeginLeasedCommand("new-holder", sprintf "/new-%d" i, cmd) |> ok |> ignore
            supervisor.SettleLeasedCommand(
              "new-holder", sprintf "/new-%d" i, cmd,
              { FinishedAtMs = 6_000L + int64 i; ExitCode = 0; StandardOutput = ""; StandardError = ""; OutputTruncated = false; ErrorTruncated = false })
            |> ok |> ignore
          let observedNow = 100_000L
          match supervisor.ReconcileLeasedCommands((fun _ _ -> Bozzetto.ProcessWatchdog.ProcessLiveness.Exited), observedNow, ignore) with
          | ComposerJournal.ReconcileResult.Reconciled(_, abandoned, _, _) ->
            abandoned.Length |> Expect.equal "all 8 stale running rows reconciled" 8
          | ComposerJournal.ReconcileResult.StorageFailure reason -> failtestf "Expected reconciliation: %s" reason
          let ops = Bozzetto.Server.UiBridge.Project.operations 1L supervisor.Journal
          let snap: Bozzetto.Web.Shared.Protocol.WorkSnapshot = {
            Sequence = 1L
            SampledAtMs = 200_000L
            Snapshot = { Revision = 0L; Worker = Bozzetto.Web.Shared.Protocol.WorkerState.Idle; Sessions = [||] }
            Leases = { Revision = 0L; Active = [||]; Queued = [||] }
            Operations = ops
            TranscriptsOmitted = false
          }
          let summary = Bozzetto.Server.UiBridge.Project.workSummary snap
          let abandonedIds =
            summary.Operations.Entries
            |> Array.choose (fun e ->
              match e.Code, e.Lease with
              | "abandoned", Some l -> Some l.RunId
              | _ -> None)
            |> Array.sort
          abandonedIds |> Expect.equal "all 8 freshly abandoned older rows remain visible in WorkSnapshot" [| for i in 1 .. 8 -> sprintf "run-old-%d" i |]
          return Ok ()
        with ex -> return Error ex
      }
      do! supervisor.StopAsync() |> Async.AwaitTask
      if Directory.Exists directory then Directory.Delete(directory, true)
      match outcome with
      | Ok () -> ()
      | Error ex -> return raise ex
    })

    testSequencedGroup "lease-watch-global"
    <| testCaseAsync "fault injection: real OS process live, PID-reuse (leaving live process untouched), and killed reconciliation release real LeaseWatch grants" (async {
      if OperatingSystem.IsLinux() then
        let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/journal-controls", Guid.NewGuid().ToString("N")))
        Directory.CreateDirectory directory |> ignore
        let path = Path.Combine(directory, "operations.db")
        let store = ComposerJournal.openStore path |> ok
        let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "no compiler process needed")
        let supervisor = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok store)
        Bozzetto.Features.LeaseWatch.reset ()
        let! outcome = async {
          try
            let psi = System.Diagnostics.ProcessStartInfo("/bin/sleep")
            psi.ArgumentList.Add "60"
            psi.UseShellExecute <- false
            use proc = System.Diagnostics.Process.Start psi
            try
              let pid = proc.Id
              let actualStartMs = DateTimeOffset(proc.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds()
              // 1. Real-OS PidReused control: record stale start time (60s earlier) against the live process.
              let reusedLeaseId =
                match Bozzetto.Features.LeaseWatch.request "journal-reused-holder" Bozzetto.ExpensiveWorkLease.Kind.FullBuild with
                | Bozzetto.ExpensiveWorkLease.Decision.Granted(lid, _) -> Bozzetto.ExpensiveWorkLease.LeaseId.value lid
                | other -> failtestf "Expected real LeaseWatch grant for reused test, got %A" other
              let reusedCmd: ComposerJournal.LeasedCommand =
                { Kind = ComposerJournal.LeasedKind.Build; LeaseId = reusedLeaseId; LeaseDisplayId = "disp-real-reused"; RunId = "run-real-reused"
                  Executable = "/bin/sleep"; Arguments = [ "60" ]; ProcessId = pid; StartedAtMs = actualStartMs - 60_000L }
              supervisor.BeginLeasedCommand("journal-reused-holder", "/project-reused", reusedCmd) |> ok |> ignore
              let releaseFromWatch (id: string) =
                Bozzetto.Features.LeaseWatch.release (Bozzetto.ExpensiveWorkLease.LeaseId.ofWire id) |> ignore
              let hasLiveFromWatch (id: string) =
                Bozzetto.Features.LeaseWatch.hasLiveLease (Bozzetto.ExpensiveWorkLease.LeaseId.ofWire id)
              match supervisor.ReconcileLeasedCommands(Bozzetto.Server.LinuxProcessEdge.probeProcessLiveness, hasLiveFromWatch, 5_000L, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), releaseFromWatch) with
              | ComposerJournal.ReconcileResult.Reconciled(_, items, _, _) ->
                items |> List.map _.RunId |> Expect.equal "PID-reused OS process is abandoned" [ "run-real-reused" ]
              | ComposerJournal.ReconcileResult.StorageFailure reason -> failtestf "Expected PidReused reconciliation: %s" reason
              hasLiveFromWatch reusedLeaseId |> Expect.isFalse "PID-reused reconciliation released real LeaseWatch grant"
              proc.HasExited |> Expect.isFalse "PID-reused reconciliation must never kill the unrelated live process"

              // 2. Real-OS Live + Killed controls with real LeaseWatch grant.
              let liveLeaseId =
                match Bozzetto.Features.LeaseWatch.request "journal-real-holder" Bozzetto.ExpensiveWorkLease.Kind.FullBuild with
                | Bozzetto.ExpensiveWorkLease.Decision.Granted(lid, _) -> Bozzetto.ExpensiveWorkLease.LeaseId.value lid
                | other -> failtestf "Expected real LeaseWatch grant for live test, got %A" other
              let cmd: ComposerJournal.LeasedCommand =
                { Kind = ComposerJournal.LeasedKind.Build; LeaseId = liveLeaseId; LeaseDisplayId = "disp-real-dead"; RunId = "run-real-dead"
                  Executable = "/bin/sleep"; Arguments = [ "60" ]; ProcessId = pid; StartedAtMs = actualStartMs }
              supervisor.BeginLeasedCommand("journal-real-holder", "/project-real", cmd) |> ok |> ignore
              let aliveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
              match supervisor.ReconcileLeasedCommands(Bozzetto.Server.LinuxProcessEdge.probeProcessLiveness, hasLiveFromWatch, 0L, aliveAt, releaseFromWatch) with
              | ComposerJournal.ReconcileResult.Reconciled(_, items, _, alive) ->
                items |> Expect.isEmpty "live process is not abandoned"
                alive |> Expect.equal "live process confirmed alive" [ "run-real-dead" ]
              | ComposerJournal.ReconcileResult.StorageFailure reason -> failtestf "Expected live probe success: %s" reason
              hasLiveFromWatch liveLeaseId |> Expect.isTrue "live process keeps its real LeaseWatch grant"
              supervisor.LastLivenessVerifiedAtMs "run-real-dead" |> Expect.equal "live probe recorded verification time" (Some aliveAt)
              // Negative control: kill process without settling its journal record.
              proc.Kill(true)
              do! proc.WaitForExitAsync() |> Async.AwaitTask
              match supervisor.ReconcileLeasedCommands(Bozzetto.Server.LinuxProcessEdge.probeProcessLiveness, hasLiveFromWatch, 0L, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), releaseFromWatch) with
              | ComposerJournal.ReconcileResult.Reconciled(_, items, _, _) ->
                items |> List.map _.RunId |> Expect.equal "killed OS process is abandoned" [ "run-real-dead" ]
              | ComposerJournal.ReconcileResult.StorageFailure reason -> failtestf "Expected real process reconciliation: %s" reason
              hasLiveFromWatch liveLeaseId |> Expect.isFalse "killed OS process released its real LeaseWatch grant"
            finally
              try if not proc.HasExited then proc.Kill(true) with _ -> ()
            return Ok ()
          with ex -> return Error ex
        }
        Bozzetto.Features.LeaseWatch.reset ()
        do! supervisor.StopAsync() |> Async.AwaitTask
        if Directory.Exists directory then Directory.Delete(directory, true)
        match outcome with
        | Ok () -> ()
        | Error ex -> return raise ex
    })

    testCaseAsync "concurrent reconciliation and Begin/Settle transactions never hit SQLITE_BUSY or latch StorageError, and Unknown clears settle grace" (async {
      let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/journal-controls", Guid.NewGuid().ToString("N")))
      Directory.CreateDirectory directory |> ignore
      let path = Path.Combine(directory, "operations.db")
      let store = ComposerJournal.openStore path |> ok
      let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "no compiler process needed")
      let supervisor = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok store)
      let! outcome = async {
        try
          // 1. Unknown clears firstAbsentObservedAtMs so Exited -> Unknown -> Exited honors full settle grace.
        let flapCmd: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-flap"; LeaseDisplayId = "disp-flap"; RunId = "run-flap"
            Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 7101; StartedAtMs = 1_000L }
        supervisor.BeginLeasedCommand("flap-holder", "/proj-flap", flapCmd) |> ok |> ignore
        let mutable phase = 0
        let flapProbe _ _ =
          match phase with
          | 0 -> Bozzetto.ProcessWatchdog.ProcessLiveness.Exited
          | 1 -> Bozzetto.ProcessWatchdog.ProcessLiveness.Unknown "transient /proc error"
          | _ -> Bozzetto.ProcessWatchdog.ProcessLiveness.Exited
        let liveLease _ = true
        // t=2000: first Exited records firstAbsentObservedAtMs = 2000 (deferred).
        match supervisor.ReconcileLeasedCommands(flapProbe, liveLease, 5_000L, 2_000L, ignore) with
        | ComposerJournal.ReconcileResult.Reconciled(_, ab, _, _) -> ab |> Expect.isEmpty "first Exited deferred inside grace"
        | ComposerJournal.ReconcileResult.StorageFailure err -> failtest err
        // t=4000: Unknown clears firstAbsentObservedAtMs.
        phase <- 1
        match supervisor.ReconcileLeasedCommands(flapProbe, liveLease, 5_000L, 4_000L, ignore) with
        | ComposerJournal.ReconcileResult.Reconciled(_, ab, unk, _) ->
          ab |> Expect.isEmpty "Unknown never abandons"
          unk |> List.length |> Expect.equal "Unknown reported" 1
        | ComposerJournal.ReconcileResult.StorageFailure err -> failtest err
        // t=8000 (6000ms after first Exited, 0ms after post-Unknown Exited): must restart grace window!
        phase <- 2
        match supervisor.ReconcileLeasedCommands(flapProbe, liveLease, 5_000L, 8_000L, ignore) with
        | ComposerJournal.ReconcileResult.Reconciled(_, ab, _, _) ->
          ab |> Expect.isEmpty "Exited after Unknown must restart grace instead of reusing stale firstAbsentObservedAtMs"
        | ComposerJournal.ReconcileResult.StorageFailure err -> failtest err
        // t=13500 (>= 5000ms after t=8000): now abandons truthfully.
        match supervisor.ReconcileLeasedCommands(flapProbe, liveLease, 5_000L, 13_500L, ignore) with
        | ComposerJournal.ReconcileResult.Reconciled(_, ab, _, _) ->
          ab |> List.map _.RunId |> Expect.equal "abandons only once post-Unknown grace elapses" [ "run-flap" ]
        | ComposerJournal.ReconcileResult.StorageFailure err -> failtest err

        // 2. Concurrent Begin/Settle + ReconcileLeasedCommands across parallel tasks never hits SQLITE_BUSY.
        let writerTask = Task.Run(fun () ->
          for i in 1 .. 25 do
            let cmd: ComposerJournal.LeasedCommand =
              { Kind = ComposerJournal.LeasedKind.Build; LeaseId = sprintf "lease-c-%d" i; LeaseDisplayId = sprintf "disp-c-%d" i
                RunId = sprintf "run-c-%d" i; Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 8000 + i; StartedAtMs = 20_000L + int64 i }
            supervisor.BeginLeasedCommand("concurrent-holder", "/proj-c", cmd) |> ok |> ignore
            let res: ComposerJournal.CommandResult =
              { FinishedAtMs = 20_500L + int64 i; ExitCode = 0; StandardOutput = "ok"; StandardError = ""; OutputTruncated = false; ErrorTruncated = false }
            supervisor.SettleLeasedCommand("concurrent-holder", "/proj-c", cmd, res) |> ok |> ignore)
        let reconcilerTask = Task.Run(fun () ->
          for i in 1 .. 25 do
            match supervisor.ReconcileLeasedCommands((fun _ _ -> Bozzetto.ProcessWatchdog.ProcessLiveness.Alive), (fun _ -> true), 5_000L, 21_000L + int64 i, ignore) with
            | ComposerJournal.ReconcileResult.Reconciled _ -> ()
            | ComposerJournal.ReconcileResult.StorageFailure err -> failtestf "Concurrent reconciliation hit storage failure: %s" err)
        do! Task.WhenAll(writerTask, reconcilerTask) |> Async.AwaitTask
        supervisor.Journal.StorageError |> Expect.isNone "concurrent SQLite writes and reconciliation never latch StorageError"
        return Ok ()
        with ex -> return Error ex
      }
      do! supervisor.StopAsync() |> Async.AwaitTask
      if Directory.Exists directory then Directory.Delete(directory, true)
      match outcome with
      | Ok () -> ()
      | Error ex -> return raise ex
    })

    testCaseAsync "owner heartbeat vs transport snapshot age, reconnection/reconciliation over UiBridge.Facts.Graph, and workSummary min-(AtMs, Sequence) eviction" (async {
      let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../nupkg/journal-controls", Guid.NewGuid().ToString("N")))
      Directory.CreateDirectory directory |> ignore
      let path = Path.Combine(directory, "operations.db")
      let store = ComposerJournal.openStore path |> ok
      let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "no compiler process needed")
      let supervisor = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok store)
      let! outcome = async {
        try
        let liveCmd: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Build; LeaseId = "lease-keep"; LeaseDisplayId = "disp-keep"; RunId = "run-keep"
            Executable = "/tool/dotnet"; Arguments = [ "build" ]; ProcessId = 9001; StartedAtMs = 10_000L }
        let vanishCmd: ComposerJournal.LeasedCommand =
          { Kind = ComposerJournal.LeasedKind.Test; LeaseId = "lease-vanish"; LeaseDisplayId = "disp-vanish"; RunId = "run-vanish"
            Executable = "/tool/dotnet"; Arguments = [ "test" ]; ProcessId = 9002; StartedAtMs = 10_000L }
        supervisor.BeginLeasedCommand("holder-keep", "/proj-keep", liveCmd) |> ok |> ignore
        supervisor.BeginLeasedCommand("holder-vanish", "/proj-vanish", vanishCmd) |> ok |> ignore
        let verifiedAt = 15_000L
        match supervisor.ReconcileLeasedCommands((fun _ _ -> Bozzetto.ProcessWatchdog.ProcessLiveness.Alive), (fun _ -> true), 5_000L, verifiedAt, ignore) with
        | ComposerJournal.ReconcileResult.Reconciled(_, _, _, alive) ->
          alive |> List.sort |> Expect.equal "both initially verified alive" [ "run-keep"; "run-vanish" ]
        | ComposerJournal.ReconcileResult.StorageFailure err -> failtest err

        // 1. DaemonStatusPayload exposes owner liveness verification separately from transport snapshot age.
        let livenessRows =
          supervisor.LeasedCommandLivenessSnapshot (fun lid ->
            if lid = "lease-keep" then Some(DateTimeOffset.FromUnixTimeMilliseconds 90_000L) else None)
        let statusJson = Bozzetto.DaemonStatusPayload.serializeWithLiveness 47749 None None livenessRows
        statusJson |> Expect.stringContains "status exposes leasedCommands array" "\"leasedCommands\":"
        statusJson |> Expect.stringContains "status exposes exact lastLivenessVerifiedAtMs" "\"lastLivenessVerifiedAtMs\":15000"
        statusJson |> Expect.stringContains "status distinguishes live lease heartbeat" "\"leaseLive\":true"
        statusJson |> Expect.stringContains "status distinguishes missing lease heartbeat" "\"leaseLive\":false"

        // 2. UiBridge.Facts.Graph reactive liveness & reconnection/reconciliation:
        //    liveness is a proper Fidelity.FSharp.Incremental InputId; confirming Alive pushes
        //    updated Operations without a Journal write or PublishLocalWork; refreshing
        //    WorkSnapshot.SampledAtMs never forges a newer owner liveness verification timestamp.
        let producer: Bozzetto.Server.UiBridge.Facts.ObservationProducer =
          { Source = { Node = "z13"; Incarnation = "inc-1" }; Correlation = Guid.NewGuid() }
        let graphConstants : Bozzetto.Server.UiBridge.Facts.Constants =
          { McpPort = 47749; Version = "test"; ProcessId = 1 }
        let graph =
          Bozzetto.Server.UiBridge.Facts.Graph(
            graphConstants,
            "",
            supervisor.Journal.Incarnation,
            observationOwners = [ producer ],
            localNode = "z13")
        let tickEvt = Event<Bozzetto.Server.HealthTick.Tick>()
        let poolEvt = Event<Bozzetto.Features.LeaseWatch.Pool>()
        let poolNow : Bozzetto.Features.LeaseWatch.Pool = { Serial = 1L; Active = []; Queue = [] }
        use _subs =
          graph.Start {
            Tick = tickEvt.Publish, (fun () -> None)
            Pool = poolEvt.Publish, (fun () -> poolNow)
            Agents = None
            Membership = None
            Journal = supervisor.JournalChanged, (fun () -> supervisor.Journal)
            Liveness = Some(supervisor.LivenessChanged, supervisor.CurrentLivenessSnapshot)
            Retrieval = None
          }
        let pushedOps = ResizeArray<Bozzetto.Web.Shared.Protocol.OperationsBoard>()
        let _, sinkReg =
          graph.Register(fun _ ev ->
            match ev with
            | Bozzetto.Web.Shared.Protocol.Operations board -> lock pushedOps (fun () -> pushedOps.Add board)
            | _ -> ())
        use _sinkReg = sinkReg
        let emptySnapshot: Bozzetto.Web.Shared.Protocol.ComposerSnapshot =
          { Revision = 1L; Worker = Bozzetto.Web.Shared.Protocol.WorkerState.Idle; Sessions = [||] }
        // Confirm Alive at 30_000L WITHOUT changing Journal or calling PublishLocalWork:
        // LivenessChanged must push an updated Operations frame automatically through Facts.Graph!
        let secondVerifiedAt = 30_000L
        match supervisor.ReconcileLeasedCommands((fun _ _ -> Bozzetto.ProcessWatchdog.ProcessLiveness.Alive), (fun _ -> true), 5_000L, secondVerifiedAt, ignore) with
        | ComposerJournal.ReconcileResult.Reconciled(_, _, _, alive) ->
          alive |> List.sort |> Expect.equal "second Alive check confirmed both" [ "run-keep"; "run-vanish" ]
        | ComposerJournal.ReconcileResult.StorageFailure err -> failtest err
        let expectedSecondIso = DateTimeOffset.FromUnixTimeMilliseconds(secondVerifiedAt).ToString("O")
        let autoPushed = lock pushedOps (fun () -> pushedOps |> Seq.toList)
        (autoPushed.Length >= 1) |> Expect.isTrue "Alive-only reconciliation automatically pushed Operations via Facts.Graph livenessInput"
        let autoKeep =
          (List.last autoPushed).Entries
          |> Array.find (fun e -> e.Lease |> Option.exists (fun l -> l.RunId = "run-keep"))
        autoKeep.Message |> Expect.stringContains "pushed Operations reflects 30_000L owner liveness" expectedSecondIso

        // First publish at SampledAtMs = 40_000L, then transport refresh at SampledAtMs = 80_000L (40s later).
        graph.PublishLocalWork(producer, emptySnapshot, 40_000L) |> Expect.isOk "first local work publish succeeds"
        graph.PublishLocalWork(producer, emptySnapshot, 80_000L) |> Expect.isOk "transport refresh publish succeeds"
        let extractLocalWork () =
          graph.Current()
          |> List.pick (function Bozzetto.Web.Shared.Protocol.WorkNodes board -> board.Nodes |> Array.tryFind (fun n -> n.Source.Node = "z13") |> Option.bind _.Value | _ -> None)
        let refreshedWork = extractLocalWork ()
        refreshedWork.SampledAtMs |> Expect.equal "transport snapshot age moved to 80_000" 80_000L
        let keepEntry =
          refreshedWork.Operations.Entries
          |> Array.find (fun e -> e.Lease |> Option.exists (fun l -> l.RunId = "run-keep"))
        keepEntry.Message |> Expect.stringContains "owner liveness message retains 30_000 verification time, distinct from SampledAtMs 80_000" expectedSecondIso

        // Now vanishCmd exits and reconciles to abandoned; transport reconnect at 95_000L must not revive it.
        match supervisor.ReconcileLeasedCommands(
                (fun pid _ -> if pid = 9001 then Bozzetto.ProcessWatchdog.ProcessLiveness.Alive else Bozzetto.ProcessWatchdog.ProcessLiveness.Exited),
                (fun lid -> lid = "lease-keep"),
                0L,
                90_000L,
                ignore) with
        | ComposerJournal.ReconcileResult.Reconciled(_, ab, _, alive) ->
          ab |> List.map _.RunId |> Expect.equal "vanishCmd abandoned" [ "run-vanish" ]
          alive |> Expect.equal "unrelated active run-keep preserved" [ "run-keep" ]
        | ComposerJournal.ReconcileResult.StorageFailure err -> failtest err
        graph.PublishLocalWork(producer, emptySnapshot, 95_000L) |> Expect.isOk "post-reconciliation reconnect publish succeeds"
        let afterReconnect = extractLocalWork ()
        afterReconnect.SampledAtMs |> Expect.equal "reconnected snapshot stamped 95_000" 95_000L
        let vanishedEntry = afterReconnect.Operations.Entries |> Array.find (fun e -> e.Lease |> Option.exists (fun l -> l.RunId = "run-vanish"))
        vanishedEntry.Code |> Expect.equal "abandoned entry stays abandoned across transport reconnect" "abandoned"
        let keptEntry = afterReconnect.Operations.Entries |> Array.find (fun e -> e.Lease |> Option.exists (fun l -> l.RunId = "run-keep"))
        keptEntry.Code |> Expect.equal "unrelated live entry stays running" "running"
        keptEntry.Message |> Expect.stringContains "kept entry updated owner liveness to 90_000" (DateTimeOffset.FromUnixTimeMilliseconds(90_000L).ToString("O"))

        // Settling liveCmd prunes its liveness entry from LastLivenessVerifiedAtMs and CurrentLivenessSnapshot.
        let finishPayload: ComposerJournal.CommandResult =
          { FinishedAtMs = 96_000L; ExitCode = 0; StandardOutput = ""; StandardError = ""; OutputTruncated = false; ErrorTruncated = false }
        supervisor.SettleLeasedCommand("holder-keep", "/proj-keep", liveCmd, finishPayload) |> ok |> ignore
        supervisor.LastLivenessVerifiedAtMs "run-keep" |> Expect.isNone "settled runId pruned from LastLivenessVerifiedAtMs"
        Map.containsKey "run-keep" (supervisor.CurrentLivenessSnapshot().VerifiedAtMs)
        |> Expect.isFalse "settled runId pruned from CurrentLivenessSnapshot"
        graph.Stop()

        // 3. UiBridge.Project.workSummary fit eviction removes min (AtMs, Sequence), preserving
        //    freshly abandoned low-Sequence rows over older high-Sequence rows.
        let bigMsg = String('x', 24_000)
        let mkEntry seq atMs code : Bozzetto.Web.Shared.Protocol.OperationEntry =
          { Sequence = seq; AtMs = atMs; CallerLabel = "caller"; Operation = "full_build"; Session = ""; Project = "/p"
            Lease = None; Ok = (code = ""); Code = code; Message = bigMsg; Run = None }
        let oversized: Bozzetto.Web.Shared.Protocol.WorkSnapshot =
          { Sequence = 1L; SampledAtMs = 100_000L; Snapshot = emptySnapshot
            Leases = { Revision = 1L; Active = [||]; Queued = [||] }
            Operations =
              { Revision = 20L; Incarnation = "inc"; StorageError = None
                Entries =
                  [| // Sequence 20..12 are older completed entries (AtMs = 5_000 + seq)
                     for seq in 20L .. -1L .. 12L do yield mkEntry seq (5_000L + seq) ""
                     // Sequence 1 is a freshly abandoned low-Sequence entry (AtMs = 99_000L)
                     yield mkEntry 1L 99_000L "abandoned" |] }
            TranscriptsOmitted = false }
        let fitted = Bozzetto.Server.UiBridge.Project.workSummary oversized
        (fitted.Operations.Entries.Length < oversized.Operations.Entries.Length)
        |> Expect.isTrue "oversized snapshot required fit eviction"
        fitted.Operations.Entries |> Array.exists (fun e -> e.Sequence = 1L && e.Code = "abandoned")
        |> Expect.isTrue "fit eviction preserved freshly abandoned low-Sequence row (AtMs=99_000) instead of evicting back of array"
        return Ok ()
        with ex -> return Error ex
      }
      do! supervisor.StopAsync() |> Async.AwaitTask
      if Directory.Exists directory then Directory.Delete(directory, true)
      match outcome with
      | Ok () -> ()
      | Error ex -> return raise ex
    })
  ]
