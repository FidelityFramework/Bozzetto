# Workstation findings: Ionide memory and Bozzetto telemetry

October 4, 2026. Recorded during the callable aggregate audit, a multi-repository
orchestrated build and test run across Fidelity.PSG, clef, Alex and Composer.
Two separate problems surfaced. This note captures both so the next orchestrated
run does not rediscover them. Grades: **E** observed or executed, **R** read in
source or configuration, **I** inference.

## Summary

- **The 10 GB, 325% CPU process was VS Code's Ionide language server for the
  clef window, not Bozzetto (E).** Large reloads of a compiler-sized F#
  workspace, triggered by build-output changes, compound with Ionide's default
  memory settings.
- **Bozzetto did not cause it, but its dashboard cannot show it (E/R).** The
  daemon's CPU figure covers only the daemon and its Composer worker. Leased
  builds and tests are invisible. The health surface still advertises inherited
  F# features that do nothing in this checkout.

## Ionide: what happened

**Observed (E):**

| Fact | Evidence |
| --- | --- |
| Two `fsautocomplete` processes from Ionide 7.31.1, one per VS Code window | `ps`, PIDs 2009752 and 1993792 |
| The clef window's server held 10.2 GB resident and used 325% CPU; the Bozzetto window's held 0.8 GB, idle | `ps -o rss,pcpu` at 05:28 |
| Each server had an idle `vstest.console` child, roughly 6 h old | Ionide test discovery |
| The clef server activated on `onLanguage:fsharp` at 23:12:53 | `~/.config/Code/logs/20261003T060731/window2/exthost/exthost.log` |
| It began serving before projects had loaded ("Couldn't find … in LoadedProjects") | `…/output_logging_20261003T231252/8-F#.log` |
| It re-evaluated the clef workspace at 23:12, 23:47 and 00:16, each logged as an MSBuild pass | same F# log |
| The clef window's extension host restarted eight times on October 3 | `…/window2/exthost/output_logging_*` folders |
| Killing all four processes returned about 10 GB; nothing respawned until an F# file was focused again | `free`, `pgrep` |

**Contributing factors (R):**

- clef contains the Clef compiler service, a very large F# codebase. A full type
  check of it is inherently multi-gigabyte.
- Build outputs change during orchestrated runs: `obj/project.assets.json`,
  `artifacts/` and generated assembly info. Each change can trigger a workspace
  re-evaluation and a fresh round of type checks.
- Ionide 7.31.1 defaults amplify memory:

  | Setting | Default |
  | --- | --- |
  | `FSharp.fsac.gc.server` | `true` (per-core heaps) |
  | `FSharp.fsac.conserveMemory` | `false` |
  | `FSharp.fsac.cachedTypeCheckCount` | `200` |
  | `FSharp.TestExplorer.AutoDiscoverTestsOnLoad` | `true` (spawns `vstest.console` over a 2,430-case suite) |
  | `FSharp.workspaceModePeekDeepLevel` | `4` |
  | `FSharp.excludeProjectDirectories` | omits `artifacts`, `bin` and `obj` |

- One server runs per VS Code window. Several Fidelity repositories are
  routinely open at once (Bozzetto, clef, Composer, Calque, Fidelity.PSG,
  clef-lang-spec, clef-lang-site), so each can carry its own server.

**Not established (I):** whether the 10 GB was retained cache growth across
repeated reloads or a true leak. That needs a heap snapshot taken before the
kill, and none was taken.

## Ionide: recommendations for the owner

These are not applied. Each is a configuration choice for you.

1. **For clef, or user-wide**, add to `.vscode/settings.json`:

   ```json
   {
     "FSharp.fsac.gc.server": false,
     "FSharp.fsac.gc.conserveMemory": 7,
     "FSharp.fsac.conserveMemory": true,
     "FSharp.fsac.cachedTypeCheckCount": 30,
     "FSharp.TestExplorer.AutoDiscoverTestsOnLoad": false,
     "FSharp.workspaceModePeekDeepLevel": 2,
     "FSharp.excludeProjectDirectories": [".git", "paket-files", ".fable", "packages", "node_modules", "artifacts", "bin", "obj", ".deps"]
   }
   ```

2. **During orchestrated multi-repository runs**, either disable Ionide in the
   affected windows, or close F# editors in repositories that agents are
   building. Compiler work is accepted on component gates, not editor
   diagnostics.
3. **Next occurrence: capture before killing.** Run
   `dotnet-gcdump collect -p <pid>`, or `dotnet-counters ps` then
   `dotnet-counters monitor -p <pid>`. Keep the window's
   `exthost/output_logging_*/…F#.log`. That settles cache growth versus leak,
   and gives Ionide upstream something actionable.

## Bozzetto: what the dashboard does and does not show

**Observed (E/R):**

- **The CPU figure is scoped to the daemon's own process tree.** The status
  line's CPU is `AggregateCpuPercent` over `ownedProcesses`: the daemon and its
  Composer worker (`Bozzetto/DaemonTelemetry.fs`, `Bozzetto/UiBridge.fs:220`).
  During the audit both were idle while leased builds and tests ran at about
  330% CPU, so the dashboard showed `cpu 0.0%`.
- **Leases are granted but not accounted for.** Every build and test in the
  audit ran under `acquire_full_build_lease` or `acquire_test_suite_lease`. The
  daemon records the grant and release but never observes the leased processes.
- **The health surface advertises inherited F# features that do nothing.**
  `/health` reports `sessionProvider: "fsharp"` and
  `diagnosticSummary: "No F# sessions registered with the daemon."`. Its
  feature list is hard-coded in `Bozzetto/McpServer.fs:2324`: `live-testing`,
  `coverage-intel`, `impact-forecast`, `action-prioritizer`, `mark-all-stale`
  and `time-travel`. F# session creation is refused in this checkout
  (`Bozzetto.Core/ExternalFSharpService.fs`), so these features cannot run.
- **The project-loading and watcher code is present but dormant.** It includes
  Ionide.ProjInfo project loading (`Bozzetto.Core/ProjectLoading.fs`) and the
  live-test watchers (`Bozzetto.Core/FileWatcher.fs`, `LiveTestWatcherCore.fs`).
  The running daemon loads no MSBuild, ProjInfo or F# compiler assemblies and
  holds three file watches (E, `/proc/548253/maps` and `fdinfo`). It is not the
  source of editor or machine load.

**Owner's assessment, recorded:** Bozzetto is not yet doing what its UI says.
The evidence supports that: the UI implies a coordinator with visibility over
work it does not see, and it advertises capabilities that are inert.

## Bozzetto: recommended changes

These follow the push model: owners publish Fidelity.FSharp.Incremental
inputs, and the bridge pushes on change, with no polling. They belong on
Bozzetto's own track, not the compiler gates.

1. **Label honestly.** Rename the figure to "daemon CPU". Show machine load and
   available memory beside it.
2. **Make leases first-class.** List each active lease with holder, kind, start
   time and expiry. Let the acquiring caller register the root process of the
   leased work, so the daemon can measure that process tree's CPU and memory
   and report it under the lease.
3. **Retire inherited F# claims from the health surface.** Report
   `sessionProvider: "clef-composer"`. Drop the six inherited features, or mark
   them unavailable with the retirement reason. Remove or quarantine the
   dormant ProjInfo and live-test watcher paths per the
   [Clefx host transition](Bozzetto_Clefx_Host_Transition_2026-10-01.md).
4. **Surface editor-side pressure.** When a workstation process outside the
   daemon's tree holds more than a configurable threshold of memory or CPU
   during leased work, a `fsautocomplete` for example, show it as an advisory
   rather than leaving it to be found by hand.
