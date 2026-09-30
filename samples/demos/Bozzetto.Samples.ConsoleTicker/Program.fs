// ============================================================
//  ⏱️  Console Ticker Demo
//  Prints one line every `intervalMs`, forever. Built for the Bozzetto demo
//  recordings (demo-gif-plan.md §7 "G2") — a large-font-friendly console
//  app whose output keeps changing so a hot-reload edit always lands on a
//  visibly live line.
//
//  To run this demo:
//    1. Start Bozzetto:  bozzetto
//    2. Watch this project, run it, and watch the output
//    3. Edit `renderLine` in Ticker.fs (the message or the format), save —
//       the next tick shows the change, no restart.
//
//  Configuration:
//    BOZZETTO_TICKER_INTERVAL_MS — milliseconds between lines (default 1000)
// ============================================================
module Bozzetto.Samples.ConsoleTicker.Program

open System
open System.Threading
open Bozzetto.Samples.ConsoleTicker.Ticker

let private readEnv (name: string) : string option =
  match Environment.GetEnvironmentVariable name with
  | null -> None
  | value -> Some value

[<EntryPoint>]
let main _args =
  let intervalMs = readEnv "BOZZETTO_TICKER_INTERVAL_MS" |> parseIntervalMs
  let mutable n = 0
  while true do
    n <- n + 1
    Console.Out.WriteLine(renderLine n DateTime.Now)
    Console.Out.Flush()
    Thread.Sleep intervalMs
  0
