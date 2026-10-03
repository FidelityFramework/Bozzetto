# Contributing to Bozzetto

Welcome! Bozzetto is an open-source project and we genuinely appreciate contributions — whether it's fixing a typo, improving docs, filing a bug, or building a whole new feature. If you're from the F# community and want to help, you're in the right place.

## Quick Links

| What | Where |
|:---|:---|
| Report a bug | [Issues](https://forge.spkez.dev/FidelityFramework/Bozzetto/issues) |
| Suggest a feature | [Issues](https://forge.spkez.dev/FidelityFramework/Bozzetto/issues) |
| Ask a question | [Project issues](https://forge.spkez.dev/FidelityFramework/Bozzetto/issues) or open an issue |
| Code standards | [AGENTS.md](AGENTS.md) |

## Getting Started

### Prerequisites

- The .NET SDK pinned in [`global.json`](global.json)
- Git
- An editor — VS Code with Ionide, Neovim, Rider, or your preference

Bozzetto on `47749`/`47750` is the only daemon surface; it serves Clef/Composer
through the `composer_*` tools and the `/composer` browser page. Embedded
production FSI hosting is retired by the [Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md),
and no separate F# REPL service is part of the workflow. Changes to Bozzetto's
own F# code are validated with `dotnet build` and the unfiltered test suite.
Follow [the implementation skill](skills/bozzetto/SKILL.md) and retain the
unfiltered build/test gates below.

### Clone and Build

```bash
git clone https://forge.spkez.dev/FidelityFramework/Bozzetto.git
cd Bozzetto
dotnet fsi build.fsx
```

The build script restores the packages pinned in `Directory.Packages.props` and builds the solution. The MCP SDK comes from nuget.org; no dependency checkout or local MCP package feed is required. Bozzetto orchestrates compiler work and controlled process lifetimes; it contains no runtime method-patching backend.

`dotnet build` also restores these dependencies directly. Use `dotnet fsi ci-pipeline.fsx` for the complete build and test gate.

### Build Script

```bash
dotnet fsi build.fsx              # restore pinned packages + build
dotnet fsi build.fsx -- test      # build + run tests
dotnet fsi build.fsx -- install   # build + pack + install as global tool
dotnet fsi build.fsx -- ext       # build + package + install editor extensions
dotnet fsi build.fsx -- all       # everything
```

### Install Your Local Build

```bash
dotnet pack Bozzetto -o nupkg
dotnet tool install --global Bozzetto --add-source ./nupkg --no-cache
```

Now `boz` on your PATH is your locally-built version.

### Run It

```bash
# Point Bozzetto at any F# project
boz

# Use an editor integration, MCP client, or the Composer browser page
# Composer: http://localhost:47749/composer
```

## Project Structure

```
Bozzetto.Core/       — Shared engine, session, testing, persistence, and protocol logic (start here!)
Bozzetto/            — CLI tool, daemon, MCP server, plus retained deprecated TUI source
Bozzetto.Gui/        — Deprecated Raylib product frontend retained as legacy source
Bozzetto.Tests/      — Expecto test project (thousands of tests; the README badge is auto-derived)
bozzetto-vscode/     — VS Code extension (F# via Fable → JavaScript)
bozzetto-vs/         — Deprecated Visual Studio extension (C# + F#), retained as legacy source; not built or published
docs/              — GitHub Pages documentation site
```

The separate upstream Neovim plugin, [sagefs.nvim](https://github.com/WillEhrendreich/sagefs.nvim), is not part of this repository; it is a client of the inherited F# session contracts, whose embedded production hosting the [Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md) retires in this checkout.

The built-in SageTUI client, legacy TUI, and `Bozzetto.Gui` Raylib frontend are deprecated. Do not extend them as current product surfaces. Raylib application and game demos remain valuable examples of Bozzetto game-project support and should be preserved.

**Good starting points for reading code:**
- `Bozzetto/DaemonMode.fs` — daemon composition and client routing
- `Bozzetto/ComposerRoutes.fs` — the `/composer` browser page and `/api/composer/*` routes
- `Bozzetto/McpServer.fs` and `Bozzetto/McpTools.fs` — MCP transport and tools
- `Bozzetto.Tests/` — the test project shows how every module is exercised

## Branches, versions and packages

Work lands on `main`; there are no side or integration branches. No hook,
script or pipeline stage bumps the version or gates a push.
`Directory.Build.props` owns the shared release version, starting at `0.1.0`.
Change it only for a deliberate owner-requested release. Builds, commits and
pushes retain that value; source revisions and artifact hashes identify builds.
Public version displays use the release version, while the SDK derives the
assembly/file versions. The VS Code manifest's explicit `sync-version` command
copies this shared value without incrementing it. Packages follow the project's
Forgejo package workflow.

Bozzetto is built from source and published to the project's own Forgejo
package registry; it is not published to NuGet.org. `dotnet fsi ci-pipeline.fsx -- pack`
packs the CLI tool into `nupkg/` after a trusted run and checks its payload.
Nothing in this checkout publishes the package.

The Composer provider tier runs with `dotnet fsi ci-pipeline.fsx -- composer`
and needs explicit, absolute paths to a built compiler closure and the native
fixture:

```bash
export BOZZETTO_COMPOSER_DISTRIBUTION=/absolute/path/to/reviewed/compiler/closure
export BOZZETTO_COMPOSER_FIXTURE=/absolute/path/to/IncrementalScalarRegions.fidproj
dotnet fsi ci-pipeline.fsx -- ci composer
```

Build commands and the parallel test stage take their matching work leases;
deferral or refusal stops the work. The current lease endpoint has no renewal
operation, so work ends before expiry and an overlong tier is reported as
incomplete.

## Debugging Bozzetto

This is the section your friend probably wants. Here's how to actually debug and develop Bozzetto day-to-day.

### The Development Loop

Bozzetto's daemon accounts for the machine memory that every contributor and agent on it spends, so the loop is lease-gated:

```
1. Write the failing Expecto test in Bozzetto.Tests
2. Make it pass in the .fs file
3. acquire_full_build_lease -> dotnet build -> release_work_lease
4. acquire_test_suite_lease -> unfiltered Bozzetto.Tests run -> release_work_lease
5. Read the TRUST line: only verdict=Trusted is acceptance
```

### Step-by-Step: Your First Debugging Session

**1. Connect to the shared daemon.** If none is running, start it with `scripts/start-shared-daemon`, which launches the reviewed installed `boz` from the dedicated external workspace (see [AGENTS.md](AGENTS.md) for the daemon lifecycle rules). Never stop or restart a daemon that other people or agents may be on. Bozzetto exposes an MCP server at `http://localhost:47749/sse`. If you're using VS Code with the Bozzetto extension, it auto-connects. For other editors, see the [README](Readme.md) for setup.

**2. Write the test first.** Add the failing case to the right module under `Bozzetto.Tests/` (Expecto.Flip, message first), then make it pass in the `.fs` file.

**3. Build under a lease.** Call `acquire_full_build_lease`, run `dotnet build` (warnings are errors), then call `release_work_lease` with the `leaseId` it returned.

**4. Run the suite unfiltered under a lease** (not `dotnet test`):

```bash
# acquire_test_suite_lease first; release_work_lease when it exits.
# <cfg> is Debug after a plain `dotnet build`; CI uses Release.
dotnet Bozzetto.Tests/bin/<cfg>/net10.0/Bozzetto.Tests.dll --summary
```

A filtered run (`--filter`, `--filter-test-list`, `--filter-test-case`) is fine while you iterate, but it narrows the run and is never the acceptance check. If a lease request answers `wait` or `refused`, the daemon is busy, not broken: wait the time it names and retry the identical request rather than building around its accounting.

**5. Read the TRUST line.** Every tier prints one `TRUST tier=… registered=N ran=N … verdict=…` line. `Trusted` is the only accepted verdict; `NarrowedRun` means a filter was on, and `NothingRan` or `CountMismatch` exit 3. Exit code 0 only means that nothing which ran failed.

The inherited F# session steps that used to live here — creating a session for `Bozzetto.Tests/Bozzetto.Tests.fsproj`, `#load`-based hot reload, and running `Expecto.Tests.runTestsWithCLIArgs` inside the session — describe retained host code that is not a product surface. This checkout refuses to create, resume or rebuild an inherited F# session at the retired provider boundary ([Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md)).

### Debugging with Breakpoints

For traditional breakpoint debugging:

```bash
# Build in Debug configuration (default)
dotnet build

# Attach your debugger to the Bozzetto process, or:
# Run the test project directly with a debugger attached
dotnet run --project Bozzetto.Tests -- --filter "test name"
```

VS Code: Use the built-in .NET debugger. Create a `launch.json` that targets `Bozzetto.Tests.dll`.

Visual Studio / Rider: Open `Bozzetto.slnx`, set `Bozzetto.Tests` as the startup project, and hit F5.

### Performance guards

Performance is guarded locally, in the normal test run — not by a separate CI
job on noisy shared runners. The heavyweight BenchmarkDotNet suite was removed:
it gated releases on microbenchmark variance for paths no user feels (the
deprecated TUI, features with no product consumer), while the one benchmark that
measured a real hot path — the per-eval binding-scope rebuild — was never in the
threshold gate at all.

In its place, `PerfTests.fs` carries lightweight guards that run every time you
run the suite. They assert **algorithmic scaling ratios** (`PerfBudget.fs`)
rather than absolute wall-clock budgets: the ratio of a large-workload cost to a
small one isolates the algorithm's growth and is independent of how fast or busy
the machine is, so the check is meaningful locally and never flakes. The current
guard proves `recordEval` stays sub-linear as the eval history grows (the O(n^2)
regression the roast flagged). Add a new guard the same way when you touch a
genuine hot path; do heavier one-off profiling ad hoc, outside the suite and
under the matching work lease.

### The Pack/Reinstall Cycle

When you change Bozzetto's own source code (anything in `Bozzetto/` or `Bozzetto.Core/`), you need to rebuild and reinstall:

```bash
# Stop the running instance, rebuild, repackage, reinstall
dotnet build && dotnet pack Bozzetto -o nupkg
dotnet tool update --global Bozzetto --add-source ./nupkg --no-cache
```

Then restart Bozzetto. If you only changed test code, a simpler rebuild is enough — no reinstall needed.

### Viewing Logs

- **Daemon console** — real-time output in the terminal where Bozzetto is running
- **Log files** — `bozzetto-stderr.log`, `bozzetto-trace.log` in the working directory
- **OpenTelemetry** — start with `start-bozzetto-otel.bat` for structured traces

## Running Tests

Bozzetto uses [Expecto](https://github.com/haf/expecto) for testing, with [FsCheck](https://github.com/fscheck/FsCheck) for property-based tests and [Verify](https://github.com/VerifyTests/Verify) for snapshot tests.

```bash
# Quick: run all tests via build script
dotnet fsi build.fsx -- test

# Direct: run the test project
dotnet run --project Bozzetto.Tests -- --summary

# Filter: run specific tests
dotnet run --project Bozzetto.Tests -- --filter "CellGrid"
```

For local development, run the built test DLL directly (`dotnet Bozzetto.Tests/bin/<cfg>/net10.0/Bozzetto.Tests.dll --summary`) under `acquire_test_suite_lease` / `release_work_lease`. Filters are for iterating only; the unfiltered run whose TRUST line says `Trusted` is the acceptance check.

### Test Categories

Tests are auto-categorized:
- **Unit** — pure logic, runs on every change
- **Integration** — needs external resources, runs on demand by default
- **Browser** — Playwright .NET tests, runs on demand
- **Property** — FsCheck generative tests
- **Benchmark** — performance tests

## Coding Standards

The full coding standards are in [AGENTS.md](AGENTS.md). Here are the essentials:

### The Non-Negotiables

- **2 spaces for indentation** — not 4, not tabs. The entire codebase uses 2 spaces.
- **Conventional Commits** — `feat(core): add session routing`, `fix(mcp): handle reconnect`, `docs: update contributing guide`
- **No `Version` in PackageReference** — all NuGet versions live in `Directory.Packages.props`

### F# Style

```fsharp
// ✅ Pattern matching, not if/else
match user.Role with
| Admin -> doAdmin()
| Regular -> doRegular()

// ✅ Result for errors, not Option or bool
let validate input : Result<ValidInput, ValidationError> = ...

// ✅ Immutable records with { with } for updates
let updated = { user with Name = newName }

// ✅ Pipeline operators
input |> validate |> Result.map transform |> Result.mapError formatError

// ✅ Small, composable functions in modules
module User =
  let create name email = { Name = name; Email = email }
  let rename newName user = { user with Name = newName }
```

### Testing Style (Expecto.Flip)

Message is **always the first argument**:

```fsharp
actual |> Expect.equal "should be 42" 42
actual |> Expect.isTrue "should be true"
list |> Expect.hasLength "should have 3 items" 3
```

## Making a Pull Request

### Before You Start

1. **Check existing issues** — someone may already be working on it
2. **Open an issue first** for large changes — let's discuss the approach before you invest time
3. **Small PRs are better** — easier to review, faster to merge

### PR Workflow

1. Fork the repo and create a branch: `git checkout -b feat/my-feature`
2. Make your changes with tests
3. Ensure `dotnet build` succeeds with no warnings (warnings are errors)
4. Run `dotnet fsi build.fsx -- test` to verify tests pass
5. Commit with conventional commit messages
6. Push and open a PR against `main`

### What Makes a Great PR

- **Tests included** — new features need tests, bug fixes need a regression test
- **Small and focused** — one logical change per PR
- **Clear description** — what changed, why, and how to verify
- **Passes CI** — green build, no new warnings

### What We'll Review

- Does it follow F# idioms? (pattern matching, immutability, composition)
- Does it have tests?
- Does it use 2-space indentation?
- Does it affect multiple current clients? (VS Code, Neovim, MCP)
- Are commit messages conventional?

## Good First Contributions

Not sure where to start? Here are some areas where help is especially welcome:

- **Documentation** — improve docs, add examples, fix typos
- **Test coverage** — add property-based tests, improve edge case coverage
- **Snapshot tests** — add Verify snapshot tests for rendered output
- **Bug fixes** — check the issue tracker for bugs labeled `good-first-issue`
- **Error messages** — make diagnostics clearer and more helpful
- **FSI quirks** — `Bozzetto.Core/FsiRewrite.fs` is ~25 lines and handles FSI edge cases — PRs welcome

## Architecture Overview

Bozzetto is **daemon-first** — one long-running server, many clients:

```
                ┌───────────────┐
                │  Bozzetto Daemon│
                │  ┌─────────┐  │
                │  │ Composer│  │  ← .fidproj sessions, reservations, build and run
                │  └─────────┘  │
                │  ┌─────────┐  │
                │  │  File    │  │
                │  │ Watcher  │  │  ← source changes revoke affected work
                │  └─────────┘  │
                │  ┌─────────┐  │
                │  │  MCP     │  │  ← AI + editor communication
                │  │ Server   │  │
                │  └─────────┘  │
                 └──┬──┬──┬──┬───┘
                    │  │  │  │
     ┌───────┐ ┌────┴──┐ ┌┴──────┐  ┌──────────┐
     │VS Code│ │Neovim │ │ Web   │  │MCP Client│
     └───────┘ └───────┘ │ UI    │  └──────────┘
                          └───────┘
```

Key architectural concepts:
- **Thin clients** — editors and MCP clients use the same session-scoped daemon contracts
- **Browser UI** — the plain-JS `/composer` page on 47749 uses `/api/composer/*` and SSE; a Partas.Solid UI in `bozzetto-web/` is in progress
- **Worker isolation** — Composer supervises an isolated compiler worker for native build and accepted-artifact execution
- **SSE for reads** — all state changes push to clients via Server-Sent Events
- **POST for commands** — write operations are POST-only, return acknowledgment only

## Questions?

- Open a [project issue](https://forge.spkez.dev/FidelityFramework/Bozzetto/issues) for general questions
- Open an [Issue](https://forge.spkez.dev/FidelityFramework/Bozzetto/issues) for bugs or feature requests
- PRs are always welcome — even small ones

Thank you for contributing! 🎉
