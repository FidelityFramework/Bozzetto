module Bozzetto.Tests.HostCoreAdoptionReloadTests

open System
open System.ComponentModel
open System.Reflection
open Expecto
open Expecto.Flip
open Bozzetto.Server.McpTools
open Bozzetto.McpTools

/// The October 1 host transition retires production F# session rebuilds.
/// MCP guidance must expose that boundary while lifecycle formatters remain
/// testable through injected component runtimes.
let private hardResetMethod =
  typeof<BozzettoTools>.GetMethods(BindingFlags.Instance ||| BindingFlags.Public)
  |> Array.find (fun m -> m.Name = "hard_reset_fsi_session")

let private hardResetDescription : string =
  hardResetMethod.GetCustomAttribute<DescriptionAttribute>().Description

[<Tests>]
let tests =
  testList "HostCoreAdoption compatibility guidance and retained lifecycle" [
    test "WHY — hard reset guidance exposes provider retirement and leased validation because an agent cannot reload a production F# session in this checkout" {
      hardResetDescription
      |> Expect.stringContains "the description states the production provider is retired" "Embedded production FSI hosting is retired"
      hardResetDescription
      |> Expect.stringContains "session recreation and rebuild are refused" "refused at the retired provider boundary"
      for lease in [ "acquire_full_build_lease"; "acquire_test_suite_lease"; "release_work_lease" ] do
        hardResetDescription |> Expect.stringContains "validation uses the daemon's work leases" lease
      hardResetDescription
      |> Expect.stringContains "the acceptance command runs the compiled test DLL" "Bozzetto.Tests.dll --summary"
      hardResetDescription
      |> Expect.stringContains "the acceptance command requires trusted coverage" "TRUST verdict=Trusted"
      for step in [ "composer_open_project"; "composer_reserve_edit"; "composer_build"; "composer_run_current" ] do
        hardResetDescription |> Expect.stringContains "Composer uses its explicit provider contract" step
      for promise in [ "rebuild=true rebuilds"; "poll get_session_status"; "ONE blessed reload path" ] do
        hardResetDescription.Contains promise
        |> Expect.isFalse "the description must not promise a retired production reload"
    }

    test "WHY — the rebuild parameter describes the same retired provider boundary because parameter guidance must not advertise unavailable session rebuilds" {
      let parameterDescription =
        hardResetMethod.GetParameters()
        |> Array.find (fun parameter -> parameter.Name = "rebuild")
        |> fun parameter -> parameter.GetCustomAttribute<DescriptionAttribute>().Description
      parameterDescription
      |> Expect.stringContains "the compatibility flag cannot restore session rebuilds" "cannot rebuild an inherited F# session"
      parameterDescription
      |> Expect.stringContains "caller-owned builds must acquire their work lease" "acquire_full_build_lease"
      parameterDescription
      |> Expect.stringContains "caller-owned builds must release their work lease" "release_work_lease"
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
