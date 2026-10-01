module Bozzetto.Tests.DaemonStateChangeContractTests

/// Contract tests for SseEvent event payloads.
///
/// File reloads carry their owning session identity, including when two
/// sessions share one working directory. Consumers can reject mismatched
/// snapshots without consulting a global active-session pointer.

open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Server

/// 8-char lowercase hex, matching WorkerProtocol.SessionId.validate.
let private sid (raw: string) =
  match WorkerProtocol.SessionId.validate raw with
  | Ok s -> s
  | Error e -> failwithf "test session id %s invalid: %s" raw e

[<Tests>]
let daemonStateChangeContractTests =
  testList "SseEvent event contract" [

    testCase "FileReloaded serializes with the owning session ID and path" <| fun _ ->
      let s = sid "deadbeef"
      let json = SseEvent.toJson (SseEvent.FileReloaded (s, "C:\\proj\\src\\Lib.fs"))
      json
      |> Expect.stringContains "payload should carry the session id" "\"sessionId\":\"deadbeef\""
      json
      |> Expect.stringContains "payload should carry the file path" "\"fileReloaded\":\"C:\\\\proj\\\\src\\\\Lib.fs\""

    testCase "FileReloaded for shared working dir distinguishes owning sessions" <| fun _ ->
      // Two sessions can share one working dir; the watcher manager attributes
      // a reloaded path to each owning session. The payloads must differ by
      // session even for the identical path.
      let sharedPath = "C:\\shared\\src\\Lib.fs"
      let forA = SseEvent.toJson (SseEvent.FileReloaded (sid "aaaa1111", sharedPath))
      let forB = SseEvent.toJson (SseEvent.FileReloaded (sid "bbbb2222", sharedPath))
      forA
      |> Expect.stringContains "session A payload should name A" "\"sessionId\":\"aaaa1111\""
      forB
      |> Expect.stringContains "session B payload should name B" "\"sessionId\":\"bbbb2222\""
  ]

// LiveTestWatcherStaleGuard and its tests were deleted (roast-9 #10): the
// epoch/generation guard is now structurally impossible to need. A single
// mailbox owner processes messages FIFO, so a FileSaved posted by a watcher
// that is later torn down always sits behind the RemoveDirectory/StopWatch
// that tore it down — by the time DebounceElapsed drains it, the resolved
// session claim is already gone and the path is dropped. See
// Bozzetto.Core/LiveTestWatcherCore.fs and Bozzetto.Tests/LiveTestWatcherCoreTests.fs.


// ── Behavioral: LiveTestWatcherManager session attribution ──────────────
// Two real-FileSystemWatcher Integration tests previously lived here ("file
// save in a shared dir fires FileReloaded for every owning session" and
// "removing one session's claim stops only its FileReloaded events") —
// each spun up a real watcher, wrote probe files, and polled up to 10s.
// Both are superseded by a DST harness that folds the REAL
// LiveTestWatcherCore.apply/isUnderWatchedDir/sessionsForPath (the exact
// pure functions the watcher shell calls) through the identical claim/save/
// drain scenarios in milliseconds, with two twin routers proving the
// invariants have teeth. See Bozzetto.Simulation/FileReloadRoutingSim.fs and
// Bozzetto.Tests/FileReloadRoutingSimTests.fs. The pure serialization test
// above ("FileReloaded for shared working dir distinguishes owning
// sessions") stays — it pins the wire format, which the DST does not
// exercise.
