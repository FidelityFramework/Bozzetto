module Bozzetto.Tests.HostCoreAdoptionReloadTests

open System
open System.ComponentModel
open System.Reflection
open Expecto
open Expecto.Flip
open Bozzetto.Server.McpTools
open Bozzetto.McpTools

/// F5b Phase 2 (design: f5b-self-hosting-design.md §4) — a blessed,
/// discoverable "reload the self-host build" path so an agent developing
/// Bozzetto.Core inside a Bozzetto.Core session can rebuild + respawn +
/// re-adopt the fresh build in one call, and see confirmation it landed.
///
/// The design's own §3-4 conclusion — confirmed here by reading
/// Bozzetto/McpTools.fs's hard_reset_fsi_session description,
/// Bozzetto/Mcp.fs's hard_reset handler, and
/// Bozzetto.Core/HostCoreAdoption.fs's resolveLaunchRoot before writing a
/// single line of this file — is that the mechanism already exists
/// end-to-end: hard_reset_fsi_session rebuild=true drives
/// SessionManager's restart path (SessionManager.fs:313, NOT modified by
/// this island), which calls HostCoreAdoption.resolveLaunchRoot (also NOT
/// modified by this island) to rebuild, respawn, and re-adopt the
/// session project's own Bozzetto.Core build. F5b Phase 2 is therefore
/// documentation + confirmation UX layered over that proven path, not new
/// isolation machinery — the design explicitly rejects in-process ALC
/// isolation for Bozzetto.Core (§3: the native TPA binder wins before any
/// managed ALC identity check runs, and Bozzetto.Core's FSI-exchanged types
/// can't be split across ALCs without breaking the live-object
/// FSI<->plumbing boundary).
///
/// CONCLUSION: hard_reset_fsi_session rebuild=true is confirmed
/// SUFFICIENT for the self-host reload — this island only had to make it
/// DISCOVERABLE (the tool description now names the self-hosting use
/// case explicitly) and CONFIRMED (RebuildOutcome.describe's Succeeded
/// line — surfaced via get_fsi_status, Mcp.fs:2101-2105 — now names the
/// loaded Bozzetto.Core version so an agent can see the adopted build
/// actually changed).
///
/// SCOPE: this is a CONTRACT test, not the full rebuild-mid-test
/// integration test the design's own RED spec calls for (§4 Phase 2:
/// build Bozzetto.Core at vN, start a session, assert loaded=vN; rebuild at
/// vN+1 with a bumped marker, invoke the reload, assert loaded=vN+1 —
/// mirroring DogfoodReplTests.fs:102-113's "bozzetto-host-adopt-" assertion
/// pattern via SessionManager.create + AwaitReady, with any poll loop
/// written ITERATIVELY, not as `task { return! self }` recursion, per
/// [[project_task_recursion_not_stacksafe]]). That full integration test
/// is intentionally deferred — too slow/complex for this island; the
/// coordinating agent owns adding it during integration.
let private hardResetDescription : string =
  typeof<BozzettoTools>.GetMethods(BindingFlags.Instance ||| BindingFlags.Public)
  |> Array.find (fun m -> m.Name = "hard_reset_fsi_session")
  |> fun m -> m.GetCustomAttribute<DescriptionAttribute>().Description

[<Tests>]
let tests =
  testList "HostCoreAdoption self-host reload (F5b Phase 2)" [
    test "WHY — hard_reset_fsi_session's description names the self-host reload path, because a self-hosting agent must discover the ONE blessed call without ever reading HostCoreAdoption.fs" {
      hardResetDescription
      |> Expect.stringContains "the description calls out the self-hosting Bozzetto.Core use case" "SELF-HOSTING Bozzetto.Core"
      hardResetDescription
      |> Expect.stringContains "the description states rebuild=true IS the reload mechanism, not a separate tool" "rebuild=true rebuilds"
      hardResetDescription
      |> Expect.stringContains "the description promises a post-reload version confirmation, matching RebuildOutcome.describe's Succeeded line" "confirms which"
    }

    test "WHY — a successful rebuild's get_fsi_status line names the WORKER's actually-loaded Bozzetto.Core version, not the daemon's own, because those two can legitimately differ and conflating them sent an agent comparing versions down the wrong path" {
      let finishedAt = DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc)
      // A session's worker can be running a DIFFERENT Bozzetto.Core build than
      // the daemon process itself — a `rebuild=true` respawns and rebuilds
      // the WORKER, not the daemon, so right after a rebuild the worker is
      // routinely ahead of a not-yet-redeployed daemon (confirmed live:
      // daemon reported 0.6.782.0 while the session's worker had already
      // loaded 0.6.789+). This is deliberately a MADE-UP version, distinct
      // from whatever this test process's own Bozzetto.Core assembly version
      // happens to be, so the assertion cannot pass by accident if the code
      // regresses to reporting the daemon's own version again.
      let workerVersion = "9.9.999-worker-under-test"
      let line = RebuildOutcome.describe (finishedAt.AddSeconds 1.0) (Some workerVersion) (RebuildOutcome.Succeeded finishedAt)
      line |> Expect.stringContains "the confirmation names the WORKER's loaded Bozzetto.Core version" (sprintf "Bozzetto.Core %s" workerVersion)
      line |> Expect.stringContains "the confirmation states the build is now loaded" "now loaded"
      // The daemon's own version is named too, separately and unambiguously
      // — never silently substituted for the worker's, never omitted.
      let daemonVersion = typeof<Bozzetto.BozzettoError>.Assembly.GetName().Version.ToString()
      line |> Expect.stringContains "the daemon's own Bozzetto.Core version is named separately" daemonVersion
      (line.Contains workerVersion && line.Contains daemonVersion)
      |> Expect.isTrue "both versions are present and distinguishable, never conflated into one unlabeled number"
    }

    test "WHY — a rebuild still InProgress never claims a loaded version, because nothing was adopted yet and claiming otherwise would lie to the agent polling get_fsi_status" {
      let startedAt = DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc)
      let line = RebuildOutcome.describe (startedAt.AddSeconds 2.0) None (RebuildOutcome.InProgress startedAt)
      line.Contains "now loaded" |> Expect.isFalse "an in-progress rebuild must not claim a version is already loaded"
    }
  ]
