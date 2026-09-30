// ============================================================
//  ⏱️  Console Ticker Demo — pure core
//  Continuous console output, sized for the Bozzetto demo recordings
//  (see demo-gif-plan.md §7 "G2"): a tiny app that never stops printing,
//  so a save-and-hot-reload during recording always lands on a live line.
//  This module holds every decision as a pure function; Program.fs is the
//  thin IO shell that calls it in a loop.
// ============================================================
module Bozzetto.Samples.ConsoleTicker.Ticker

open System

/// Tick interval used when BOZZETTO_TICKER_INTERVAL_MS is unset or invalid.
[<Literal>]
let DefaultIntervalMs = 1000

// ┌─ HOT RELOAD DEMO: edit the message below, save it, watch the next ──────┐
// │ printed line change. Both the message text and the line's shape live   │
// │ in this one function body, so Bozzetto's source hot-reload can patch it  │
// │ while the ticker keeps running — nothing else in the process needs to  │
// │ change for the new line to show up.                                    │
/// Renders one output line as `HH:mm:ss.f  #<n>  <message>`.
let renderLine (n: int) (now: DateTime) : string =
  let message = "Bozzetto keeps ticking"
  sprintf "%s  #%d  %s" (now.ToString "HH:mm:ss.f") n message
// └───────────────────────────────────────────────────────────────────────┘

/// Parses an interval override (e.g. read from an env var), falling back to
/// the default for anything missing, non-numeric, or non-positive. Pure —
/// the caller is responsible for actually reading the environment.
let parseIntervalMs (raw: string option) : int =
  match raw with
  | None -> DefaultIntervalMs
  | Some text ->
    match Int32.TryParse text with
    | true, parsed when parsed > 0 -> parsed
    | _ -> DefaultIntervalMs
