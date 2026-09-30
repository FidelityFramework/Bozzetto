# Bozzetto — Coding Agent Guidelines

## STOP — Read This Before Anything Else

**The Bozzetto daemon is a long-running process that never exits on its own.** It hosts the FSI session, MCP server, and dashboard on ports 47749/47750. You will be tempted to wait for it. DO NOT.

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
only through `composer_run_current`. F#/.NET development uses the separate
SageFS MCP service on 37749; do not make a Bozzetto FSI session a prerequisite
for Composer work. LLVM ORC JIT is a later Composer execution backend.

Start the shared Bozzetto daemon with `scripts/start-shared-daemon` and the
reviewed installed `boz`. Its working directory is the dedicated external
`$XDG_DATA_HOME/bozzetto/workspace` (default `~/.local/share/bozzetto/workspace`).
Do not launch it from home or the repositories parent: the inherited recursive
watcher would scan that directory. Logs belong under external state storage.
Preserve an existing daemon and follow the live checkpoint to connect MCP.

### Retained F# implementation work

Load and follow [`skills/bozzetto/SKILL.md`](skills/bozzetto/SKILL.md) before
touching F#. The short version: an available F# REPL (normally separate SageFS)
is the inner loop for F# implementation work, and
`dotnet build` / `dotnet test` is the final gate only. If you brief a
sub-agent, put the loop in the brief. Sub-agents don't inherit it.

Working on Bozzetto itself has two extra catches:
- **Build before `create_session`.** A session loads compiled output. If it
  still fails with "Not all DLLs are found" after a build, treat it as a Bozzetto
  bug and report the paths it names. Bozzetto.Core, Bozzetto.Host and Bozzetto
  multi-target net10.0;net11.0, and 0.6.782 resolved those references wrong,
  so a session on Bozzetto.Tests faulted even when everything was built.
- **Self-hosting skew.** A worktree's `Bozzetto.Core` can be newer than the
  installed daemon (the daemon is whatever was last published). A
  session that loads Core can then refuse with a version mismatch, or a
  "type not found, Version=..." error. That's a known Bozzetto bug. Report the
  exact error, work in a session that doesn't load Core if you can (pure files
  can be `#load`ed into any session), and only then fall back to `dotnet` for
  that step. Never silently.

## Project Overview

Bozzetto accelerates Clef/Composer development through shared MCP and browser interfaces over incremental compiler sessions. LLVM ORC JIT is the intended future Clef REPL backend. F#/.NET work can use a separate SageFS daemon/MCP connection on ports 37749/37750; Bozzetto uses 47749/47750. The inherited F# host and editor integrations remain implementation/compatibility code, not a priority for new REPL work. Read the current deployment and acceptance instructions in `docs/Bozzetto_Live_Provider_Checkpoint_2026-09-30.md` before assuming an agent is connected.

The built-in SageTUI client, legacy TUI, and `Bozzetto.Gui` Raylib frontend are deprecated. Do not treat them as current product surfaces or add new product documentation for them. Preserve Raylib application and game demos because they demonstrate Bozzetto support for game projects and are independent of the deprecated GUI frontend.

The Visual Studio extension (`bozzetto-vs/`) is deprecated and no longer built, tested, or published — do not treat it as a current product surface, do not add new product documentation for it, and do not route new engineering effort into it.

## Language & Stack

- **Primary language**: F# (functional programming)
- **Target framework**: `net11.0` (the shipped tool closure — Bozzetto, Bozzetto.Core, Bozzetto.Host — multi-targets `net10.0;net11.0`; see `Directory.Build.props`)
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
- Run tests via the Bozzetto REPL, not `dotnet test`
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

The Neovim plugin lives in a separate repo: `WillEhrendreich/sagefs.nvim`.

## Build & Test

```bash
dotnet build           # Build all projects
dotnet test            # Run tests (CI only — prefer Bozzetto REPL locally)
dotnet pack Bozzetto -o nupkg  # Package the CLI tool
```

## Multi-agent / worktree sessions

- **Sessions are checkout-aware.** A session's working directory is classified against the filesystem (`Bozzetto.Checkout.classify`, no `git` subprocess): a plain repository, a git **worktree** (its own root and branch — worktrees have a `.git` FILE, not a directory, pointing at the main checkout's `.git/worktrees/<name>` admin dir), or not a git checkout at all. `list_sessions` and the dashboard show a worktree session's branch.
- **A git worktree is a routing boundary.** If you are working inside a worktree (e.g. `.claude/worktrees/agent-x`) and no session exists for it yet, tool calls resolve to `Gone` with a create hint — they never silently fall back to a session rooted at the main checkout, even though your directory is textually nested under it. Create a session for the worktree; do not assume the main checkout's session is yours to use.
- **For a project the running daemon already serves, create a session in it.** Only spawn a second daemon when you are testing daemon code itself (changes to `Bozzetto.Core`/`Bozzetto`/`Bozzetto.Host`) that the running daemon cannot execute because it predates your change — and then give that daemon an explicit owner/TTL rather than leaving it to leak.
- **Identity is bound to your MCP connection, not to the `agentName` you pass.** Two different connections that happen to declare the same `agentName` are tracked as two separate members — you cannot see or clear another connection's active session by reusing its name.

## Generated test artifacts

- `ci-pipeline.fsx` owns test-tier artifact placement. It uses `${XDG_CACHE_HOME:-$HOME/.cache}/bozzetto/tiers/<checkout-name>-<path-hash>/`; the hash isolates different checkouts and worktrees. Do not create `<checkout>.tiers` siblings in `~/repos` or put generated scratch among project repositories.
- Keep tier scratch outside the checkout and, for isolated runs, outside `/tmp`: private bind mounts replace both locations. Copy-on-write support must be probed from the checkout into the cache, since they may be on different filesystems.
- Tier checkout copies, temporary data, and build caches are disposable when no pipeline is using them. Preserve any logs or trust ledgers referenced by validation records before deleting artifacts, and update those records when moving them.

## Owned Harmony dependency

- `Bozzetto.Harmony` is built from the separately controlled Harmony fork. Keep package and CLR assembly identity `Bozzetto.Harmony`; the compatible API namespace is still `HarmonyLib`. The isolated FSI host deliberately uses its own `Bozzetto.HostHarmony` identity.
- `vendor/Bozzetto.Harmony/` contains the reviewed package plus provenance/hash manifest. CI validates it; it must never silently clone or rebuild the former upstream dependency. Refresh only through `scripts/update-harmony /absolute/path/to/Bozzetto.Harmony` and review the artifact, manifest and regenerated locks together.
- Package versions are immutable. Increment the fork's `BozzettoBuild` and central package version before distributing changed package contents. Generated packaging output belongs in the external cache, not in a repository sibling. The controlled `Bozzetto.Harmony` Git checkout is source code, not a scratch cache.

## Architecture Principles

- **Current clients**: VS Code, Neovim, the web dashboard, and MCP use session-scoped daemon contracts
- **Web dashboard**: Falco.Datastar and SSE provide browser-based session control and observability
- **Binary persistence**: Session/test state via CRC-validated binary manifest (.bozzettofm)
- **CQRS**: Separate read/write models
- **Vertical slices**: Features as single files for locality of behavior
- **Daemon architecture**: Long-running FSI session with MCP server for editor communication

## Things to Avoid

- Do not introduce new NuGet dependencies without discussion
- Do not change the indentation style (2 spaces)
- Do not use `dotnet test` for local development — use the Bozzetto REPL
- Do not modify `Directory.Build.props` version numbers. Nothing bumps on commit: `scripts/ship` bumps once per push, and `scripts/pre-push` refuses a master push that doesn't raise the version
- Do not add Version attributes to PackageReference elements
