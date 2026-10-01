# Preparing the Clefx execution boundary

October 1, 2026. This records a source transition, not a compiler promotion or a deployment record. The installed Bozzetto daemon continues to use its previously reviewed release until separately replaced.

## What changes now

Bozzetto's embedded `Bozzetto.FsiHost` is removed. It was an F# Interactive executable built from embedded source for a selected .NET SDK; changing its name to `ClefxHost` would not make it a Clef execution backend.

The daemon no longer builds, packages or launches that host. Requests to create, resume or rebuild an inherited F# session are refused with directions to the independent SageFS service. The retained `Bozzetto.Host` executable also refuses startup; it is no longer part of the daemon's tool package. Existing `.bozzetto/config.fsx` files are preserved, but Bozzetto neither evaluates them nor writes replacements through its former auto-open controls. The legacy Jupyter command reports the same provider boundary.

The inherited `set_integration_ref` operation also depends on creating an F# session. It refuses before changing git worktrees, the cohort head or integration bindings, or launching a build. General cohort coordination remains; a future Composer integration journey needs its own compiler-owned acceptance contract.

F#/.NET implementation work uses real upstream SageFS on MCP port `37749` and dashboard port `37750`. Bozzetto remains on `47749`/`47750`. Connections, sessions and lifecycle belong to the selected service; Bozzetto does not silently proxy F# requests or migrate config scripts to SageFS. The separate service was exercised with a bare REPL during this transition.

The existing Composer supervisor and isolated compiler worker continue to provide explicit `.fidproj` sessions, edit reservations, incremental native builds, accepted-artifact evidence and execution through `composer_run_current`. Compiler distribution promotion remains a separate operation owned by the compiler workflow.

## What remains inherited

This is the retirement of embedded production FSI hosting, not the completion of native self-hosting. Bozzetto is still implemented in F# on .NET. Its in-process FSI adapter, FCS/Harmony dependencies and associated component tests remain while shared daemon, editor and testing code is separated further. Generic lifecycle models with injected runtimes remain testable. Historical F# documentation, application samples and upstream credits remain available.

Tests whose purpose was to prove a real embedded F# session are explicitly recorded as retired coverage. Their retirement must not appear as passing Clef coverage. Generic daemon, MCP, coordination, persistence, browser and Composer behavior keeps its own executable gates; replacement refusal tests cover the absence of evaluation, config writes, rebuilds and worker creation at the retired boundary. The test runner reports the retirement inventory separately from each active tier's trust result.

Five test files for the removed host protocol, builder and adapters were removed with that implementation. The remaining historical product journeys stay compiled and explicitly registered as retired. The [quality matrix](../quality/definition-of-done.json) preserves their prior CI or external receipts under `historicalEvidence`, rather than claiming present verification of F# hot reload, live testing or future Clef capabilities.

The legacy dashboard's session panels also depended on a selected F# session. Its browser friction panel is unavailable in the no-session view; the corresponding old browser evidence is historical. Provider-neutral MCP friction telemetry and the browser shell remain supported. This preparation does not replace every inherited editor or dashboard interaction with a Composer equivalent.

## What ClefxHost must mean

`ClefxHost` is the intended name for a future Clef interactive execution host. No executable or F# adapter is renamed to claim that capability in this transition. Its implementation follows compiler parity and agreement on the [first-horizon contracts](Bozzetto_Fidelity_Component_Contracts.md), particularly:

- Composer owns source snapshots, checking, proof evidence and execution admission. The host accepts identified compiler output; it does not maintain a second semantic authority.
- Interactive requests name the workspace, session, compiler epoch, source revision, target and accepted execution plan. Revocation and compiler replacement invalidate old execution authority.
- LLVM ORC execution defines code and data lifetime, callbacks, outstanding work, state preservation, cancellation and teardown before offering a persistent REPL.
- CPU execution has an explicit arithmetic and capability context. Cross-target numeric emulation and changed proof obligations belong to the demand-gated second horizon.
- The contract supports a later native host without exposing CLR or FSI types as the public execution model.

The [development horizons](Bozzetto_Development_Horizons.md) cover heterogeneous HMR and site coordination. SageFS and [Fable.SageFs](https://github.com/shayanhabibi/Fable.SageFs) remain important foundations and inspiration, described in [upstream heritage](../UPSTREAM_HERITAGE.md); their contribution is independent of which execution host Bozzetto ships.

## Validation

Validated on October 1 with the pinned .NET 11 release-candidate SDK. The Release solution build and the separate demos build succeeded. The tool package includes both `net10.0` and `net11.0`; archive inspection found 310 files, no `Bozzetto.FsiHost`, `Bozzetto.Host` or `host/` payload, and a README identical to the restored checkout copy. CLI help exits successfully; the retired Jupyter entry point exits 2 with SageFS guidance before reading its supplied connection-file path.

Each active test tier below ran without a name filter. Registered counts include the runner's existing ignores, which are shown separately rather than counted as passes.

| Tier | Registered | Passed | Ignored | Trust verdict |
|---|---:|---:|---:|---|
| `default` | 9,773 | 9,769 | 4 | Trusted |
| `--integration-host` | 120 | 116 | 4 | Trusted |
| `--integration-composer` | 29 | 29 | 0 | Trusted |
| `--integration-browser` | 11 | 11 | 0 | Trusted |
| `--integration-disconnect` | 3 | 3 | 0 | Trusted |
| `--mutation-score` | 272 | 272 | 0 | Trusted |

Discovery reports 10,024 cases: 9,922 executable cases and 102 explicitly retired F# cases. The dedicated mutation tier re-exercises its registered mutant checks; it is not an additional 272 unique product tests. The default suite retains four existing performance/Harmony ignores. The host suite retains four existing managed-dependency/cross-submission ignores. No retired journey is counted as a current pass or ignore, and no filtered run supplies the acceptance result.

Composer's 29-case tier used the already promoted distribution identified in the [October 1 assessment](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md#october-1-promoted-distribution-repeat), including its native build/run/reuse and authority checks. This transition neither replaces that distribution nor establishes broader Clef parity. The browser tier covers the retained shell; the retired F# session panels do not acquire replacement coverage from that result.

Implementation checks used the independent upstream SageFS 0.6.834 service on `37749`/`37750`, including a config script sentinel proving no evaluation or file replacement. One attempt to run a loaded config suite in SageFS exceeded the evaluation timeout and remained `Evaluating` after cancellation; the exact refusal was `Cannot send_fsharp_code: session is Evaluating.` Only the owned session was stopped. Fresh bare sessions supported further checks; the full compiled tiers above provide final acceptance. SageFS remained separate from the unchanged installed Bozzetto daemon.

Intermediate failures are retained with the results: test-helper compilation errors were corrected; the default suite's three architecture failures were resolved without increasing its blocking/string-error budgets; stale generated Core reference assemblies were refreshed; and the required Playwright Chromium revision 1208 was installed. Two browser tests were corrected to click the real New Session accordion instead of matching explanatory text, while retaining the form and SSE persistence assertions. The SSE test triggers and observes an actual server update before testing preservation. Their whole tier was rerun.

The local evidence checkpoint is `/home/hhh/.local/state/bozzetto/checkpoints/2026-10-01-clefx-host-transition/`. It preserves gate commands and logs, the complete trust ledger including failed attempts, a separate accepted-tier table, SageFS receipts, Composer evidence, the local package and a manifest of the uncommitted source. Full build/test gates acquired and released Bozzetto work leases. Local documentation links and `git diff --check` also passed.

These are source and package checks, not a claim that the complete release pipeline ran. The installed Bozzetto CLI/daemon and its compiler distribution were preserved; no source changes in this transition were deployed or published. A real Clefx implementation and new acceptance evidence remain required for the intended Clef REPL.
