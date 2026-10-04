# Bozzetto: agent guidelines

Bozzetto is a development controller for Clef/Composer work. It runs as one
daemon on ports 47749 (MCP, HTTP API, browser UI) and 47750 (control listener),
supervises a Composer worker, and grants leases for builds and test runs. It
sits outside the compiler pipeline: compiler work is accepted on component
gates, and Bozzetto adapts on its own track.

Embedded F# session hosting is retired in this checkout
([Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md)).
No F# REPL service is part of any workflow. LLVM ORC JIT is the intended future
Clef execution backend and is not implemented.

## Working principles

These replace reflexes with judgment. Each one exists because its opposite cost
this project real time or code.

1. **Ground before building.** Read the governing document or spec clause
   first and cite it. Do not invent requirements or build ahead of the plan.
2. **Extend the owner.** Every fact has one owning component. Fix a problem
   where it is owned. Do not add a parallel mechanism, a fallback, or a second
   authority for a fact another component already holds.
3. **Fix friction at the seam.** No component's current mechanism is fixed,
   Bozzetto's included. When a boundary causes friction, change the component
   that causes it; do not route around it. Each component keeps its
   responsibility and its refusals stay loud.
4. **Grade evidence.** An executed unfiltered result outranks code read at a
   pinned revision, which outranks spec text, which outranks an agent's report.
   State how you know a load-bearing claim. Report failures as failures.
5. **Justify growth.** Prefer deleting or extending to adding. A new file,
   dependency, owner or abstraction needs a stated reason. Unneeded code has
   already been removed from this repository in bulk; do not add it back.
6. **Spend tokens deliberately.** Locate code through the retrieval service
   before reading files (see "Finding code"). Delegate bulk reading. Keep
   handoffs to one page.
7. **Stop and ask on owner decisions.** Scope, schedule, dependency and policy
   choices belong to the owner. Surface them early as yes/no questions.

## Daemon lifecycle

The daemon is long-running and is not supposed to exit.

- **Start:** `scripts/start-shared-daemon`. It launches the reviewed installed
  `boz` from the dedicated workspace (`$XDG_DATA_HOME/bozzetto/workspace`,
  default `~/.local/share/bozzetto/workspace`) with bounded readiness checks.
  Never launch from home or the repositories parent.
- **Check:** one bounded probe, then act on the result.

  ```bash
  curl -s -m 3 http://127.0.0.1:47749/health
  ```

- **Never block on the daemon's lifetime.** Do not run it in the foreground,
  `wait` on it, or retry a probe in a loop. After starting it, the next action
  is the bounded probe, not a status message.
- **Preserve a running daemon.** Stopping, restarting or switching the shared
  daemon is an operational step the owner triggers. A worktree's code can be
  newer than the installed daemon. If the daemon refuses with a version or
  "type not found" error, report the exact error. Do not restart it or work
  around it.
- **Second daemons** are only for testing daemon code the running one cannot
  execute. Give one an explicit owner and time limit.
- **The dashboard's CPU figure covers the daemon and its worker only.** It does
  not show leased builds or tests
  ([findings](docs/Workstation_Findings_Ionide_And_Bozzetto_Telemetry_2026-10-04.md)).

Read `docs/Bozzetto_Live_Provider_Checkpoint_2026-09-30.md` before assuming an
agent is connected.

## Clef and Composer work

Use the `composer_*` tools and the `/composer` page on 47749. Open an explicit
`.fidproj`, reserve before editing, and execute only through
`composer_run_current`.

Requests to create, resume or rebuild an inherited F# session are refused at
the retired provider boundary. That refusal is the documented behaviour, not a
bug to work around.

## Building and testing Bozzetto

Load [`skills/bozzetto/SKILL.md`](skills/bozzetto/SKILL.md) before changing F#.

```bash
scripts/work-lease run full_build dotnet build
scripts/work-lease run test_suite_run dotnet <testDll> --summary
dotnet fsi ci-pipeline.fsx     # full pipeline; `-- ci`, `-- composer`, `-- pack` add stages
```

- **Leases.** Every caller-owned build or suite runs under a lease:
  `scripts/work-lease`, or the MCP tools `acquire_full_build_lease`,
  `acquire_test_suite_lease` and `release_work_lease`. Follow wait and refused
  decisions. Always release.
- **`dotnet test` is for CI.** Locally, run the built test DLL.
- **Memory pressure.** If a lease is deferred for memory, investigate instead
  of polling. Measure available RAM and swap, process RSS with parents, and
  cgroup usage. Distinguish the daemon, compiler workers, unrelated builds and
  editor language servers. Before and after multi-repository builds, check for
  an oversized F# language server (`pgrep -af fsautocomplete`). Ask before
  changing scheduling policy or stopping processes you do not own.
- **Sub-agents do not inherit this file's context.** Put the lease loop and the
  retrieval rule in every brief.

### Validation posture

Broad defensive test sweeps are a habit to re-examine, not a virtue. Validate
as narrowly as the evidence allows, and keep one honest backstop.

1. **Inner loop: targeted.** Run the tests for the owner you changed and for
   the components that consume its facts. Filters are fine here.
2. **Prove each new rule.** A new check or refusal needs a positive control, a
   negative control that asserts the specific reason, and one mutation that a
   test catches.
3. **Acceptance: one unfiltered run per increment,** by the implementer, at
   integration. A filtered run is never the acceptance check. Do not repeat the
   full run without cause; a reviewer verifies heads, manifests and the TRUST
   ledger instead.
4. **Blind spots remain.** Host code is not yet covered by the compiler's own
   guarantees, so the unfiltered backstop stays until targeted selection has
   predicted the full run's failures over several increments.

Cross-repository increments use the one-page
[handoff template](docs/FFI_Increment_Handoff_Template.md).

### The TRUST line

Every tier runs through `TestInfrastructure.TrustSignal.run` and prints one
`TRUST tier=… registered=N ran=N … verdict=…` line. Read it, not the exit code
or a stage colour.

| Verdict | Meaning | Exit |
| --- | --- | --- |
| `Trusted` | Unfiltered, everything registered ran, nothing failed | 0 |
| `NarrowedRun` | Passed, but a filter narrowed it; inner loop only | 0 |
| `TestsFailed` | Something failed or errored | 1/2 |
| `NothingRan` | Zero tests executed | 3 |
| `CountMismatch` | Unfiltered, but ran ≠ registered | 3 |

CI runs every stage even after one fails, records each tier in the ledger
(`BOZZETTO_TRUST_LEDGER`) and fails on any tier that is not `Trusted`.

### Expecto filters

| Flag | Matches |
| --- | --- |
| `--filter <path>` | A slash-separated hierarchy prefix |
| `--filter-test-list <substring>` | `testList` names only |
| `--filter-test-case <substring>` | Leaf case names only |

A filter that matches nothing still exits 0, which is why the TRUST line
exists. Two rules follow:

- Never add a gate reachable only by a name filter. Register it as a plain
  `[<Tests>]` value, or select it through the `Integration` registry in
  `TestInfrastructure.fs`.
- Keep tracking tokens (bug ids) out of `testList` names.

### Test artifacts

`ci-pipeline.fsx` places tier artifacts under
`${XDG_CACHE_HOME:-$HOME/.cache}/bozzetto/tiers/<checkout-name>-<path-hash>/`.
Keep generated scratch out of the checkout and out of `~/repos`. Preserve logs
and trust ledgers that validation records cite before deleting artifacts.

## Finding code

- **Repositories listed by the fresh retrieval schema** use retrieval first.
  Check `tracked_sources` for coverage, including Bozzetto when its enrollment
  is active; do not infer coverage from an older repository list.
  - Start with `schema` and require a fresh snapshot.
  - Then `find`, `pgq` and `sources`, each pinned to that snapshot.
  - Cite repo, revision, path and the returned lines.
  - The index covers pushed heads only, so push before asking about new work.
  - Helpers and request shapes:
    `~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/tools/README.md`.
- **Repositories absent from the fresh schema** may be read directly, with
  bounded searches. Record that outside-index exception for the current
  snapshot.
- **Direct reads elsewhere** are for uncommitted diffs, a stale index, or
  confirming a line retrieval already located. Do not grep a repository
  wholesale.
- **Spec and site questions** go through the public hybrid search, with public
  language terms only.

## Language and stack

- **F#**, functional first. Imperative and object-oriented vestiges remain
  from the code's origins; retire them when touched, and do not extend them.
- **Target:** `net10.0`; `global.json` selects the stable .NET 10 SDK.
  Solution format is `.slnx`.
- **Daemon HTTP:** ASP.NET Core minimal APIs.
- **Browser UI:** Partas.Solid in `bozzetto-web/`, served at `/composer` and
  `/dashboard` over `/ui/bridge`. `bozzetto-web/theme.js` is the only colour
  and font source. Change reusable treatments in
  `bozzetto-web/src/Frontend/styles.css`, then rebuild, verify and weld the
  bundle under a lease. Never edit generated `Bozzetto/WebAssets.fs` by hand.
- **Updates are pushed, not polled.** Owners publish Fidelity.FSharp.Incremental
  inputs and the bridge pushes on change. Do not add timers or polling loops.
- **Daemon and worker wire:** the typed binary contract in
  `Bozzetto.Composer.Protocol`. Daemon and worker deploy as a matching pair.
- **Serialization edges:** where JSON or another text format is unavoidable,
  prefer Fidelity.Data over `System.Text.Json`. Adding the reference is still a
  dependency decision for the owner.
- **Asynchrony:** prefer cold `async` over hot `Task` in host code.
- **Testing:** Expecto with FsCheck; Verify for snapshots.
- **Persistence:** CRC-validated binary manifest (`.bozzettofm`).

### Terms

Composer is a **differential compiler**: it recompiles what a change affects.
**Incremental** is reserved for Fidelity.FSharp.Incremental and its
`Incremental<'T>` values.

## Coding standards

- **Indentation:** 2 spaces, always.
- **Packages:** no `Version` attributes on `<PackageReference>`. Versions live
  in `Directory.Packages.props`. Do not add NuGet dependencies without
  discussion.
- **Style:** immutable types, discriminated unions, pattern matching and
  pipelines. Use `Result<'T, 'TError>` for operations that can fail. Keep domain
  logic pure, with effects at the edges. Prefer small, composable functions.
- **Tests:** `Expecto.Flip`, so the message is the first argument:

  ```fsharp
  actual |> Expect.equal "should be 42" 42
  ```

  Prefer property-based tests to examples.
- **Commits:** Conventional Commits, `type(scope): description`, with types
  `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `style`, `ci`,
  `build`.
- **Version:** `Directory.Build.props` holds the shared release version
  (`0.1.0`). Change it only for an owner-requested release.
- **Branches:** work lands on `main`. Commit and push useful checkpoints
  promptly so work has a remote recovery point. Packages go to the project's
  Forgejo registry; nothing here publishes to NuGet.org.

## Architecture

- **Runtime ownership.** Bozzetto orchestrates compiler work and owned process
  lifetimes. File changes revoke affected work before recompilation or process
  replacement. Native execution requires compiler-owned authority; a host
  reload never substitutes for proof or artifact validation.
- **No runtime patching.** Harmony, MonoMod, detours and injection are removed.
  Do not restore them in any form.
- **Shared foundation.** Fidelity.FSharp.Incremental owns dependency
  bookkeeping and explicitly started work across Bozzetto and the
  Clef/CCS/Baker/Composer pipeline. Follow the
  [adoption contract](docs/Bozzetto_Incremental_Foundation_Adoption.md). Do not
  grow separate invalidation or work-lifetime mechanisms. Record identities and
  acceptance evidence in the
  [cross-project checkpoint](docs/Incremental_Provider_Checkpoint_2026-10-01.md).
- **Shape.** CQRS read and write models; features as vertical slices in single
  files; clients (VS Code, Neovim, MCP, browser) use session-scoped contracts,
  with `/api/composer/*` as the HTTP client contract.

## Project structure

| Path | Role |
| --- | --- |
| `Bozzetto.Core/` | Shared engine, session, persistence and protocol logic |
| `Bozzetto/` | CLI, daemon, MCP server and browser bridge |
| `Bozzetto.Composer/` | Composer worker process |
| `Bozzetto.Composer.Protocol/` | Typed binary daemon and worker contract |
| `Bozzetto.Tests/`, `Bozzetto.Composer.Tests/` | Expecto suites |
| `bozzetto-web/` | Partas.Solid browser UI |
| `bozzetto-vscode/` | VS Code extension (Fable) |
| `docs/` | Design records, checkpoints and the documentation site |

The disconnected `Bozzetto.Gui` Raylib frontend, Visual Studio extension
(`bozzetto-vs/`) and SageTUI proof of concept (`samples/sagetui-poc/`) have been
removed. Do not restore these retired clients. Deprecated terminal rendering
support remains compiled into `Bozzetto/`; prune it at its owning dependency
boundary and do not treat it as a current product surface. Keep the Raylib
application and game demos; they show support for game projects and do not
depend on the removed GUI.

Other top-level folders are supporting or retained code. Read their project
file before treating one as a product surface.

## Multi-agent and worktree sessions

- **Sessions are checkout-aware.** A session's directory is classified from
  the filesystem as a repository, a git worktree, or neither. `list_sessions`
  shows a worktree session's branch.
- **A worktree is a routing boundary.** Inside a worktree with no session,
  tool calls resolve to `Gone` with a create hint. They never fall back to the
  main checkout's session. Create a session for the worktree.
- **For a project the daemon already serves, create a session in it.**
- **Identity is bound to the MCP connection,** not to the `agentName` passed.
  Two connections with the same name are two members.
