/// The operations journal's bounds: count, per-stream cut, aggregate transcript
/// budget. Each rule has a positive control, a negative control asserting the
/// reason, and a named mutation (in the comments) the test would catch.
module Bozzetto.Tests.ComposerJournalTests

open Expecto
open Expecto.Flip
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration

let private entry (caller: string) (operation: Operation) (run: ComposerJournal.Transcript option) : ComposerJournal.Entry =
  { Sequence = 0L; AtMs = 1L; Caller = caller; Operation = operation; Session = "s"; Project = "/p/P.fidproj"
    Outcome = ComposerJournal.Outcome.Succeeded; Run = run }

let private transcriptOf (chars: int) : ComposerJournal.Transcript =
  { ExitCode = 0; Generation = 1L; SourceVersion = "v"; StandardOutput = System.String('o', chars); StandardError = ""; Dropped = false }

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
      (t.ExitCode, t.Generation, t.SourceVersion, t.Dropped) |> Expect.equal "the rest carried" (2, 3L, "src", false)

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
        { Sequence = 0L; AtMs = 1L; Caller = huge; Operation = Operation.Build; Session = huge; Project = huge
          Outcome = ComposerJournal.Outcome.Refused(RefusalCode.InvalidReservation, huge)
          Run = Some { ExitCode = 0; Generation = 1L; SourceVersion = huge; StandardOutput = wide; StandardError = wide; Dropped = false } }
      let kept = (ComposerJournal.append e (ComposerJournal.empty "j")).Entries.Head
      (kept.Caller.Length, kept.Session.Length, kept.Project.Length) |> Expect.equal "labels and texts clipped" (ComposerJournal.labelLimit, ComposerJournal.labelLimit, ComposerJournal.textLimit)
      (match kept.Outcome with ComposerJournal.Outcome.Refused(_, m) -> m.Length | _ -> -1) |> Expect.equal "the refusal message clipped" ComposerJournal.textLimit
      let run = kept.Run.Value
      run.SourceVersion.Length |> Expect.equal "the source version clipped" ComposerJournal.labelLimit
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
  ]
