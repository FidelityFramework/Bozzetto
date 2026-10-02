namespace Bozzetto.Features

open System
open System.IO
open Bozzetto
open Bozzetto.Cohort
open Bozzetto.MemberTable

/// Portable JSONL export/import of the cohort ledger. CohortJson owns the
/// explicit, version-compatible command/event schema shared with SQLite.
/// Import refuses a malformed line without silently dropping recorded work.
module CohortLedgerExport =

  /// One `LedgerEntry<MemberId>` per line, `\n`-separated. No file IO here —
  /// `exportToFile` below is the thin wrapper for that.
  let toJsonl (entries: LedgerEntry<MemberId> list) : string =
    entries
    |> List.map CohortJson.entryToJson
    |> String.concat "\n"

  /// Fail closed (never silently drops a bad line): splits on `\n`, skips
  /// blank lines (including a trailing one — a file written by `toJsonl` plus
  /// an editor's final newline is still valid), and deserializes every
  /// non-blank line in order. The first line that fails to parse aborts the
  /// whole import and names its 1-based line number and the reason — a
  /// corrupt tail must never produce a silently-truncated ledger. Never
  /// throws: every deserialization failure is caught and turned into `Error`.
  let fromJsonl (jsonl: string) : Result<LedgerEntry<MemberId> list, string> =
    if String.IsNullOrEmpty jsonl then
      Ok []
    else
      jsonl.Split '\n'
      |> Array.mapi (fun i line -> i + 1, line)
      |> Array.filter (fun (_, line) -> not (String.IsNullOrWhiteSpace line))
      |> Array.fold
        (fun acc (lineNo, line) ->
          match acc with
          | Error _ -> acc
          | Ok entries ->
            match CohortJson.entryFromJson (line.Trim()) with
            | Ok entry -> Ok(entry :: entries)
            | Error reason -> Error(sprintf "line %d: %s" lineNo reason))
        (Ok [])
      |> Result.map List.rev

  /// Convenience wrapper — the codec itself (`toJsonl`/`fromJsonl`) stays pure
  /// string<->entries; this just owns the file write.
  let exportToFile (path: string) (entries: LedgerEntry<MemberId> list) : unit =
    File.WriteAllText(path, toJsonl entries)

  /// Convenience wrapper — see `exportToFile`.
  let importFromFile (path: string) : Result<LedgerEntry<MemberId> list, string> =
    File.ReadAllText path |> fromJsonl
