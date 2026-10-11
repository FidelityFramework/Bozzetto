namespace Bozzetto.Features

open Bozzetto
open Bozzetto.MemorySupervisor

/// The one place that remembers the daemon's current `MemoryPressure` across
/// calls — `MemorySupervisor.step` itself is pure and takes the level as an
/// explicit parameter; this is the stateful wrapper around it, the same
/// split `HealthWatch` is over `HealthAnomaly` and `GcDumpWatch` is over
/// `GcDumpCapture`. Without persisting the level between calls, the
/// hysteresis in `nextLevel` (the whole point: no flapping at a boundary)
/// would never actually apply — each call would start back at `Normal`
/// instead of remembering it was already `SheddingIdle`.
module MemoryPressureWatch =

  /// One settled move of the level: the level moved to and its revision, which
  /// increases by one per move under the gate. Announcements are made outside
  /// the gate, so two moves may be heard out of order; the revision orders them.
  type Move = { Revision: int64; Level: MemoryPressure }

  let mutable private level = MemoryPressure.Normal
  let mutable private revision = 0L
  let private gate = obj ()
  let private changed = Event<Move>()

  /// Fires once each time the retained level moves, after that level is
  /// readable through `currentLevel`. The bridge pushes Health on it, so a
  /// dashboard learns of a level change when it happens rather than on the
  /// next unrelated push. A subscriber that wants the current level reads
  /// `currentLevel`; the carried move is the change itself.
  let Changed : System.IObservable<Move> = changed.Publish

  /// One transition, computed and committed under the same hold of the gate:
  /// two concurrent readings cannot both decide from the same stale level.
  let private transition (decide: MemoryPressure -> MemoryPressure) : MemoryPressure =
    let settled, move =
      lock gate (fun () ->
        let next = decide level
        if level = next then next, None
        else
          level <- next
          revision <- revision + 1L
          next, Some { Revision = revision; Level = next })
    move |> Option.iter changed.Trigger
    settled

  /// Fold one fresh reading of the machine's memory into the retained level.
  /// Every reading reaches here through the daemon's one sequencer
  /// (HealthTick), which every acquisition point feeds — a lease decision, a
  /// health read, the resource observation a page drives — so the level is as
  /// current as the newest reading and no sampling loop is needed.
  let observe (t: Thresholds) (m: MachineMemory.Stats) : MemoryPressure =
    let availableFrac = if m.TotalBytes > 0L then float m.AvailableBytes / float m.TotalBytes else 1.0
    transition (fun current -> nextLevel t current availableFrac)

  /// Evaluate the policy against the given machine/session snapshot,
  /// persist the level it produces, and return the full `Decision`. Called
  /// from the admission check (before creating a session) and from the
  /// periodic sweep (to shed idle sessions even when nothing is trying to
  /// create a new one).
  let evaluate (t: Thresholds) (m: MachineStats) (sessions: SessionSnapshot list) : Decision =
    let decision = ref Unchecked.defaultof<Decision>
    transition (fun current ->
      decision.Value <- step t current m sessions
      decision.Value.Level) |> ignore
    decision.Value

  let currentLevel () : MemoryPressure = lock gate (fun () -> level)

  /// For tests, and for a daemon that wants to start clean.
  let reset () : unit = transition (fun _ -> MemoryPressure.Normal) |> ignore
