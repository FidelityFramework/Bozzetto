# Bozzetto — Coding Agent Guidelines

**October 1 source transition:** embedded production FSI hosting is retired in
this checkout. Bozzetto on 47749/47750 is the only daemon surface and serves
Composer; no separate F# REPL service is part of any workflow. Older installed
releases may still contain the inherited host. See the
[Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md).
Clefx/ORC execution is not implemented by this removal. The daemon lifecycle
rules below still apply.

## STOP — Read This Before Anything Else

**The Bozzetto daemon is a long-running process.** It hosts Composer coordination, the MCP server and dashboard on ports 47749/47750. You will be tempted to wait for it. DO NOT.

**The cardinal rule, stated three times because it is the only thing you keep getting wrong:**

1. **NEVER narrate a wait to the user.** After starting a daemon, do not write "verifying" or "checking" or "started" and then stop. The next thing you produce must be a tool result — specifically a screenshot, an HTTP probe with a hard timeout, or the next concrete step. A chat message that is not a result or a question is the failure mode.

2. **NEVER call a command that blocks on the daemon's lifetime.** `Wait-Process`, `Start-Process -Wait`, waiting for a process to exit, `taskkill /T /F` on the daemon while also awaiting the result — all of these will hang forever because the daemon is not supposed to exit.

3. **NEVER treat `Start-Sleep` as "wait for the daemon to be ready" without a follow-up tool call in the same turn.** `Start-Sleep 3` followed by a chat message is the same hang, just shorter. `Start-Sleep 3` followed by a screenshot is fine. The sleep is not the problem. The text after the sleep is the problem.

**Concrete patterns:**

- Starting the daemon: one `Start-Process ... -WindowStyle Hidden` (no `-Wait`), one `Start-Sleep -Seconds 3` for warmup, then the next tool call is the screenshot. Nothing in between.
- Verifying the daemon is up: `Invoke-WebRequest -TimeoutSec 3` with a hard timeout. If it returns, great. If it throws a timeout exception, kill the request and report the state. Do not retry indefinitely.
- Killing the daemon: `Get-Process -Name "Bozzetto" | Stop-Process -Force` returns immediately. Never combine that with a `Wait-Process`.
- The "is it up" check is the screenshot. Not a chat message, not a sleep, not a status probe. The screenshot.

**If you catch yourself writing a sentence that contains "waiting", "let me check", "verifying", "starting up", or "should be ready"** between starting a process and your next tool call, stop. Skip the sentence. Make the tool call.

**You have failed this rule on the very first turn of this session, and on the turn immediately after being told about it, and on multiple turns after that. The next failure is a refusal to do the work, not a sentence of acknowledgment.**

## Choose the provider before the inner loop

Clef/Composer work uses Bozzetto's `composer_*` tools and `/composer` browser
page on 47749. Open an explicit `.fidproj`, reserve before editing, and execute
only through `composer_run_current`. F# changes to Bozzetto's own code are
validated with `dotnet build` and the unfiltered test suite under the work
leases below; do not make any F# REPL session a prerequisite for Composer
work. LLVM ORC JIT is a later Composer execution backend.

Start the shared Bozzetto daemon with `scripts/start-shared-daemon` and the
reviewed installed `boz`. Its working directory is the dedicated external
`$XDG_DATA_HOME/bozzetto/workspace` (default `~/.local/share/bozzetto/workspace`).
Do not launch it from home or the repositories parent: the inherited recursive
watcher would scan that directory. Logs belong under external state storage.
Preserve an existing daemon and follow the live checkpoint to connect MCP.

### Retained F# implementation work

Load and follow [`skills/bozzetto/SKILL.md`](skills/bozzetto/SKILL.md) before
touching F#. The short version: no F# REPL service is part of the loop. F#
changes to Bozzetto's own code are validated with `dotnet build` and the
unfiltered test suite, started only after `acquire_full_build_lease` /
`acquire_test_suite_lease` and ended with `release_work_lease`; a filtered
test run is never the acceptance check. If you brief a sub-agent, put the
loop in the brief. Sub-agents don't inherit it.

Working on Bozzetto itself has two extra catches:
- **Inherited F# session tools refuse here.** Requests to create, resume or
  rebuild an inherited F# session are refused at the retired provider boundary
  in this checkout (see the
  [Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md));
  that refusal is the documented boundary, not a bug to work around. If a
  Bozzetto project-loading check still reports "Not all DLLs are found" after a
  successful build, treat it as a Bozzetto bug and report the paths it names.
  Mixed-framework project references must resolve to the consumer's target,
  not the first target listed in the referenced project.
- **Self-hosting skew.** A worktree's `Bozzetto.Core` can be newer than the
  installed daemon (the daemon is whatever was last published). The daemon can
  then refuse with a version mismatch, or a "type not found, Version=..."
  error. That's a known Bozzetto bug. Report the exact error from the
  installed daemon; do not restart it, and never work around it silently.

## Project Overview

Bozzetto accelerates Clef/Composer development through shared MCP and browser interfaces over incremental compiler sessions. LLVM ORC JIT is the intended future Clef REPL backend. Bozzetto on 47749/47750 is the only daemon surface; no separate F# REPL service is part of any workflow or MCP connection. The in-process F# engine and editor integrations remain implementation/compatibility code; embedded production FSI hosting is retired. Read the current deployment and acceptance instructions in `docs/Bozzetto_Live_Provider_Checkpoint_2026-09-30.md` before assuming an agent is connected.

The built-in SageTUI client, legacy TUI, and `Bozzetto.Gui` Raylib frontend are deprecated. Do not treat them as current product surfaces or add new product documentation for them. Preserve Raylib application and game demos because they demonstrate Bozzetto support for game projects and are independent of the deprecated GUI frontend.

The Visual Studio extension (`bozzetto-vs/`) is deprecated and no longer built, tested, or published — do not treat it as a current product surface, do not add new product documentation for it, and do not route new engineering effort into it.

## Language & Stack

- **Primary language**: F# (functional programming)
- **Target framework**: `net10.0` throughout the hosted delivery; `global.json` selects the stable .NET 10 SDK.
- **Solution format**: `.slnx` (not `.sln`)
- **Web framework**: Falco (functional web framework for ASP.NET Core)
- **HTML rendering**: Falco.Markup
- **Real-time UI**: Falco.Datastar (SSE-based)
- **Testing**: Expecto (behavior-driven, property-based with FsCheck)
- **Snapshot testing**: Verify
- **Persistence**: Binary manifest format (.bozzettofm) for session and test state
- **Package management**: Central package management via `Directory.Packages.props`

## Critical Coding Standards

### Indentation
- **ALWAYS use 2 spaces**, never 4 spaces — this is non-negotiable across the entire codebase.

### Package References
- **NEVER** include `Version` attributes in `<PackageReference>` elements in `.fsproj` files.
- All versions are defined centrally in `Directory.Packages.props` at the repo root.

### Commit Messages
- Use **Conventional Commits** format: `type(scope): description`
- Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `style`, `ci`, `build`

### F# Style
- Favor immutable types, discriminated unions, pattern matching, and pipeline operators (`|>`)
- Use `Result<'T, 'TError>` for operations that can fail
- Keep domain logic pure — side effects only at system edges
- Small, composable functions with clear intent

### Testing
- Tests use Expecto with `Expecto.Flip` — **message is always the first argument**:
  ```fsharp
  actual |> Expect.equal "should be 42" 42
  actual |> Expect.isTrue "should be true"
  ```
- Run the unfiltered Bozzetto.Tests suite (`dotnet {testDll} --summary`) under `acquire_test_suite_lease` / `release_work_lease`; `dotnet test` is CI-only
- Property-based tests (FsCheck) are preferred over example-based tests

#### Filters: `--filter-test-list` matches LISTS, `--filter-test-case` matches LEAVES

Expecto exposes three filter flags and they do **not** mean the same thing:

| Flag | Matches against |
|---|---|
| `--filter <path>` | a slash-separated hierarchy prefix |
| `--filter-test-list <substring>` | **`testList` names only** |
| `--filter-test-case <substring>` | **leaf case names only** |

If the token you are filtering on lives in the `testList` name and not in any leaf
case name, `--filter-test-case` matches **nothing** — and the run still prints
`Failed: 0, Errored: 0` and **exits 0**. This has already happened here: an agent ran
`--filter-test-case "roast-8"` against a list literally named for `roast-8` whose eight
cases all begin `"WHY — "`, read the green result, and concluded the behaviour was
covered. Nothing had executed. Exit 0 means *nothing that ran failed*; it says nothing
about what was excluded.

**Now enforced, not just documented.** Every tier (default, `--integration-host`,
each dedicated entry point, `--mutation-score`) runs through
`TestInfrastructure.TrustSignal.run`, which reads Expecto's own summary and prints
one `TRUST tier=… registered=N ran=N … verdict=…` line:

| Verdict | Meaning | Exit |
|---|---|---|
| `Trusted` | unfiltered, everything registered ran, nothing failed | 0 |
| `NarrowedRun` | passed, but a filter/`--run`/`--stress` narrowed it — inner loop only | 0 |
| `TestsFailed` | something failed or errored | 1/2 |
| `NothingRan` | zero tests executed (the trap above) — filtered or not | **3** |
| `CountMismatch` | unfiltered, but ran ≠ registered | **3** |

CI runs every test stage even after one goes red, collects each tier's row in a
ledger (`BOZZETTO_TRUST_LEDGER`), and its final `trust report` stage prints one
table and fails on any tier that is not Trusted — including a tier whose process
died before reporting (`NoReport`). `TrustSignalTests` fails the fast suite if a
registered tier is not invoked by `ci-pipeline.fsx`, or if a test run there
bypasses the ledgered `testTier` step. Read the table, not a stage colour.

Consequences, in order of importance:

1. **A filtered run is never the acceptance check.** A gate is done when its own test
   name appears in the output of a real, **unfiltered** run — `dotnet {testDll} --summary`,
   or the relevant whole-suite entry point. Filters are for the inner loop only.
2. **Never add a gate that is only reachable by a name filter.** Put it in the default
   suite as a plain `[<Tests>]` value, or select it structurally through the
   `Integration` registry in `TestInfrastructure.fs` (reference-based exclusion, which a
   typo cannot defeat). CI itself uses no filter expressions — every stage runs a whole
   suite. Keep it that way.
3. **Do not put tracking tokens (`roast-N`, bug ids) in `testList` names.** Put them on
   the leaf cases where a case filter can see them, or leave them out entirely.

## Project Structure

```
Bozzetto.Core/       — Shared engine, session, testing, persistence, and protocol logic
Bozzetto/            — CLI tool, daemon, MCP server, dashboard, and retained deprecated TUI source
Bozzetto.Gui/        — Deprecated Raylib product frontend retained as legacy source
Bozzetto.Tests/      — Expecto test project
bozzetto-vscode/     — VS Code extension (Fable F#→JS)
bozzetto-vs/         — Deprecated Visual Studio extension (C# + F#), retained as legacy source
docs/              — GitHub Pages site
```

The separate upstream Neovim plugin, `WillEhrendreich/sagefs.nvim`, is not part of this repository; it is a client of the inherited F# session contracts, whose embedded production hosting the [Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md) retires in this checkout.

## Build & Test

```bash
dotnet build           # Build all projects
dotnet test            # CI only — locally run the built test DLL unfiltered under a test-suite lease
dotnet pack Bozzetto -o nupkg  # Package the CLI tool
```

If the scheduler blocks a build for memory, immediately investigate the consumers
with `btop`, `ps`, or equivalent tools instead of repeatedly polling for a lease.
Capture available RAM and swap, process RSS and parent processes, cgroup memory
usage and limits including file cache, and GPU use of shared system memory where
applicable. Distinguish the daemon, compiler workers, and unrelated builds. Report
the measurements in plain software engineering terms; a scheduling threshold is
not a measurement of Bozzetto's memory usage. Raise architectural questions early,
and ask before changing the scheduling policy or stopping processes you do not own.

Commit and push useful checkpoints promptly so work has a remote recovery point.
If release checks are still pending, use an integration branch and record the
outstanding checks; release checks must not block these checkpoint pushes.

## Multi-agent / worktree sessions

- **Sessions are checkout-aware.** A session's working directory is classified against the filesystem (`Bozzetto.Checkout.classify`, no `git` subprocess): a plain repository, a git **worktree** (its own root and branch — worktrees have a `.git` FILE, not a directory, pointing at the main checkout's `.git/worktrees/<name>` admin dir), or not a git checkout at all. `list_sessions` and the dashboard show a worktree session's branch.
- **A git worktree is a routing boundary.** If you are working inside a worktree (e.g. `.claude/worktrees/agent-x`) and no session exists for it yet, tool calls resolve to `Gone` with a create hint — they never silently fall back to a session rooted at the main checkout, even though your directory is textually nested under it. Create a session for the worktree; do not assume the main checkout's session is yours to use.
- **For a project the running daemon already serves, create a session in it.** Only spawn a second daemon when you are testing daemon code itself (changes to `Bozzetto.Core`/`Bozzetto`) that the running daemon cannot execute because it predates your change — and then give that daemon an explicit owner/TTL rather than leaving it to leak.
- **Identity is bound to your MCP connection, not to the `agentName` you pass.** Two different connections that happen to declare the same `agentName` are tracked as two separate members — you cannot see or clear another connection's active session by reusing its name.

## Generated test artifacts

- `ci-pipeline.fsx` owns test-tier artifact placement. It uses `${XDG_CACHE_HOME:-$HOME/.cache}/bozzetto/tiers/<checkout-name>-<path-hash>/`; the hash isolates different checkouts and worktrees. Do not create `<checkout>.tiers` siblings in `~/repos` or put generated scratch among project repositories.
- Keep tier scratch outside the checkout and, for isolated runs, outside `/tmp`: private bind mounts replace both locations. Copy-on-write support must be probed from the checkout into the cache, since they may be on different filesystems.
- Tier checkout copies, temporary data, and build caches are disposable when no pipeline is using them. Preserve any logs or trust ledgers referenced by validation records before deleting artifacts, and update those records when moving them.

## Runtime ownership

- Bozzetto orchestrates compiler work and owned process lifetimes. File changes
  revoke affected work before recompilation or controlled process replacement.
- Runtime method patching and its dependencies are removed. Do not restore
  Harmony, MonoMod, detours, injection, an optional patching mode or a fallback.
- Native execution and future ORC replacement require compiler-owned authority;
  a host reload never substitutes for proof or artifact validation.

## Architecture Principles

- **Current clients**: VS Code, Neovim, the web dashboard, and MCP use session-scoped daemon contracts
- **Web dashboard**: Falco.Datastar and SSE provide browser-based session control and observability
- **Binary persistence**: Session/test state via CRC-validated binary manifest (.bozzettofm)
- **CQRS**: Separate read/write models
- **Vertical slices**: Features as single files for locality of behavior
- **Daemon architecture**: Long-running Composer supervision with shared MCP and browser contracts; embedded production FSI hosting is retired and Bozzetto is the only daemon surface

### Shared incremental foundation

Fidelity.FSharp.Incremental is the selected shared foundation for incremental
dependency bookkeeping and explicitly started work across Bozzetto and the
Clef/CCS/Baker/Composer pipeline. Follow the
[adoption contract](docs/Bozzetto_Incremental_Foundation_Adoption.md). Provider
sessions now use its functional Async mailbox through a pinned package reference.
Use the shared foundation for workspace coordination instead of growing separate
invalidation or work-lifetime mechanisms. Keep dependency identities aligned
across consumers and validate changes at their owning contract, including the
shared library when integration exposes a missing contract. Preserve compiler
proof/artifact authority, reservation/launch ordering, physical cleanup and
portable host contracts. Record exact identities and acceptance evidence in the
[cross-project checkpoint](docs/Incremental_Provider_Checkpoint_2026-10-01.md).

## Things to Avoid

- Do not introduce new NuGet dependencies without discussion
- Do not change the indentation style (2 spaces)
- Do not use `dotnet test` for local development — run the built test DLL unfiltered under `acquire_test_suite_lease`, and read the TRUST line
- Do not modify `Directory.Build.props` version numbers. Nothing bumps on commit: `scripts/ship` bumps once per push, and `scripts/pre-push` refuses a main push that doesn't raise the version
- Do not add Version attributes to PackageReference elements
