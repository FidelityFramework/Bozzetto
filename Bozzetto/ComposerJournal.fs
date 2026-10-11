/// The daemon's record of settled Composer operations: who asked for what, what
/// came of it, and a run's output. Every caller's operation settles in the
/// supervisor, which owns this journal and publishes it whole; the page reads
/// it through the bridge's fact plane. Bounded three ways so a replacement
/// snapshot stays a page fact: at most `maxEntries` entries, at most
/// `streamLimit` characters per transcript stream, at most `aggregateLimit`
/// transcript characters over the whole journal (the oldest transcripts go
/// first; their entries stay, marked). Memory only; status reads are never
/// journaled.
module Bozzetto.ComposerIntegration.ComposerJournal

open Bozzetto.Providers
open Bozzetto.Composer.Protocol

/// A run's output as journaled. `Dropped` marks a transcript given up for space.
type Transcript = {
  ExitCode: int
  Generation: int64
  SourceVersion: string
  StandardOutput: string
  StandardError: string
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
type Journal = { Incarnation: string; Sequence: int64; Entries: Entry list }

let maxEntries = 200
/// Characters (UTF-16 code units) kept per stream: the end of the stream, which carries the result.
let streamLimit = 16384
/// Transcript characters over the whole journal.
let aggregateLimit = 512 * 1024
/// Characters kept of a caller label or session identity, and of a project path or a message.
let labelLimit = 128
let textLimit = 512

let empty incarnation = { Incarnation = incarnation; Sequence = 0L; Entries = [] }

/// A stream longer than the limit keeps its end, with one marker line in front
/// that is not counted against the limit. A stream already cut is left as it is.
let cut (text: string) =
  if text.Length <= streamLimit then text
  elif text.StartsWith "[cut: " && text.Length <= streamLimit + 64 then text
  else sprintf "[cut: %d characters omitted]\n%s" (text.Length - streamLimit) (text.Substring(text.Length - streamLimit))

let transcript (run: RunResult) =
  { ExitCode = run.ExitCode; Generation = run.Generation; SourceVersion = run.SourceVersion
    StandardOutput = cut run.StandardOutput; StandardError = cut run.StandardError; Dropped = false }

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
          else { t with SourceVersion = clip labelLimit t.SourceVersion; StandardOutput = cut t.StandardOutput; StandardError = cut t.StandardError }) }

/// Record one settled operation: the newest entry first, the count bounded,
/// and the aggregate transcript budget kept by giving up transcripts oldest
/// first until the rest fit; their entries stay, marked.
let append (entry: Entry) (journal: Journal) =
  let sequence = journal.Sequence + 1L
  let entries = { bound entry with Sequence = sequence } :: journal.Entries |> List.truncate maxEntries
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
  { journal with Sequence = sequence; Entries = evicted }

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
