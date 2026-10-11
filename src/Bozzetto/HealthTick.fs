/// The one sequencer of the daemon's observations of itself and its machine.
///
/// Every acquisition point the daemon already has — a lease request or
/// renewal, the resource sample a page drives, a health request, the health
/// record DaemonMode builds — hands its reading here. The tick folds the
/// machine reading into the memory level (MemoryPressureWatch owns the level
/// and its hysteresis), stamps one serial per publication and publishes the
/// whole reading as one immutable value, so Health derives from one fact with
/// one age instead of three samplers racing. No timer runs: the daemon reports
/// as often as it is asked and says when it last looked.
module Bozzetto.Server.HealthTick

open System
open Bozzetto
open Bozzetto.Features
open Bozzetto.Utils

/// One published reading. The machine figures and the level are as of
/// `SampledAtMs`; `Record` and `Telemetry` were taken together, as of
/// `RecordAtMs`, and are `None` with `RecordAtMs = 0` before the first record.
type Tick = {
  /// One per publication, increasing; the admission order downstream.
  Serial: int64
  SampledAtMs: int64
  Machine: MachineMemory.Stats
  Level: MemoryPressure
  Record: HealthSnapshot option
  Telemetry: DaemonTelemetry.Snapshot option
  RecordAtMs: int64
}

let private gate = obj ()
let private changed = Event<Tick>()
let mutable private latest : Tick option = None
let mutable private reader : (unit -> HealthSnapshot option) option = None
/// Refreshes in progress. A reading taken during one updates the value but is
/// not published on its own: the refresh publishes it with the fresh record.
let mutable private refreshing = 0
/// Refreshes started, and the one whose record is installed: a refresh that
/// finishes after a later-started one has already installed its record keeps
/// that newer record, so older accounting never arrives as newer news.
let mutable private started = 0L
let mutable private installed = 0L

/// Fires once per publication, after `current` reflects it. Announcements are
/// made outside the gate, so two may be heard out of order; `Serial` orders them.
let Changed : IObservable<Tick> = changed.Publish

let current () : Tick option = lock gate (fun () -> latest)

/// The daemon's health record reader (DaemonMode's), installed once at start.
let install (record: unit -> HealthSnapshot option) : unit = lock gate (fun () -> reader <- Some record)

let private now () = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

let private empty =
  { Serial = 0L; SampledAtMs = 0L; Machine = { MachineMemory.Stats.TotalBytes = 0L; AvailableBytes = 0L }
    Level = MemoryPressure.Normal; Record = None; Telemetry = None; RecordAtMs = 0L }

/// Under the gate: fold one machine reading into the level and retain it.
let private fold (machine: MachineMemory.Stats) =
  let level = MemoryPressureWatch.observe MemorySupervisor.defaultThresholds machine
  let previous = latest |> Option.defaultValue empty
  let moved = latest |> Option.exists (fun earlier -> earlier.Level <> level)
  let tick = { previous with Serial = previous.Serial + 1L; SampledAtMs = now (); Machine = machine; Level = level }
  latest <- Some tick
  tick, moved

/// Fold one machine reading into the level and publish the reading. A level
/// move is news about the daemon too, so its record follows, once.
let rec observe (machine: MachineMemory.Stats) : Tick =
  let tick, moved, publish = lock gate (fun () -> let tick, moved = fold machine in tick, moved, refreshing = 0)
  if publish then changed.Trigger tick
  if moved && publish then refresh () |> ignore
  tick

/// Ask the daemon for its health record and publish the reading with it. The
/// record is read outside the gate: it is the daemon's own accounting and may
/// take its time, while lease decisions keep handing readings in. A record
/// that fails, or that a later-started refresh has already superseded, leaves
/// the installed record and its time as they were; the reading is still
/// published, with its own time.
and refresh () : Tick =
  let read, ordinal =
    lock gate (fun () ->
      refreshing <- refreshing + 1
      started <- started + 1L
      reader, started)
  // A record obtained, or nothing: a reader that fails or has no record to give
  // leaves the installed record, its telemetry and its time untouched.
  let obtained =
    read |> Option.bind (fun read ->
      try read ()
      with error ->
        Log.warn "[health-tick] the health record failed: %s" error.Message
        None)
  let tick =
    lock gate (fun () ->
      refreshing <- refreshing - 1
      if latest.IsNone then fold (MachineMemory.current ()) |> ignore
      let previous = latest |> Option.defaultValue empty
      let record, telemetry, recordAt =
        match obtained with
        | Some record when ordinal > installed ->
          installed <- ordinal
          Some record, DaemonTelemetry.current (), now ()
        | _ -> previous.Record, previous.Telemetry, previous.RecordAtMs
      let tick =
        { previous with
            Serial = previous.Serial + 1L
            Record = record
            Telemetry = telemetry
            RecordAtMs = recordAt }
      latest <- Some tick
      tick)
  changed.Trigger tick
  tick

/// One fresh reading of this machine, folded in and published: what a lease
/// decision or a heartbeat does before it reports the level.
let sample () : Tick = observe (MachineMemory.current ())

/// For tests, and for a daemon that wants to start clean.
let reset () : unit =
  lock gate (fun () ->
    latest <- None
    reader <- None
    refreshing <- 0
    started <- 0L
    installed <- 0L)
