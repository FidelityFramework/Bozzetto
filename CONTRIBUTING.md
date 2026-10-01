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

Use separate SageFS on `37749`/`37750` for the F# REPL implementation loop;
Bozzetto on `47749`/`47750` serves Clef/Composer. Embedded production FSI hosting
is retired by the [Clefx host transition](docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md).
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

# Use an editor integration, MCP client, or the dashboard
# Dashboard: http://localhost:47750/dashboard
```

## Project Structure

```
Bozzetto.Core/       — Shared engine, session, testing, persistence, and protocol logic (start here!)
Bozzetto/            — CLI tool, daemon, MCP server, dashboard, plus retained deprecated TUI source
Bozzetto.Gui/        — Deprecated Raylib product frontend retained as legacy source
Bozzetto.Tests/      — Expecto test project (thousands of tests; the README badge is auto-derived)
bozzetto-vscode/     — VS Code extension (F# via Fable → JavaScript)
bozzetto-vs/         — Deprecated Visual Studio extension (C# + F#), retained as legacy source; not built or published
docs/              — GitHub Pages documentation site
```

The Neovim plugin lives in a separate repo: [sagefs.nvim](https://github.com/WillEhrendreich/sagefs.nvim).

The built-in SageTUI client, legacy TUI, and `Bozzetto.Gui` Raylib frontend are deprecated. Do not extend them as current product surfaces. Raylib application and game demos remain valuable examples of Bozzetto game-project support and should be preserved.

**Good starting points for reading code:**
- `Bozzetto/DaemonMode.fs` — daemon composition and client routing
- `Bozzetto/Dashboard.fs` — current browser dashboard
- `Bozzetto/McpServer.fs` and `Bozzetto/McpTools.fs` — MCP transport and tools
- `Bozzetto.Tests/` — the test project shows how every module is exercised

## Debugging Bozzetto

This is the section your friend probably wants. Here's how to actually debug and develop Bozzetto day-to-day.

### The Development Loop

Bozzetto is its own development environment. The recommended workflow is:

```
1. Run Bozzetto against the test project
2. Use the live FSI session to iterate on code
3. Write tests in the REPL, see them fail, make them pass
4. Save proven code to .fs files
5. Rebuild and verify
```

### Step-by-Step: Your First Debugging Session

**1. Start Bozzetto against its own test project:**

```bash
boz
```

Then create a session for `Bozzetto.Tests/Bozzetto.Tests.fsproj` from your editor, MCP client, or the dashboard. That session loads the project into a live F# Interactive session with hot reload.

**2. Connect your editor.** Bozzetto exposes an MCP server at `http://localhost:47749/sse`. If you're using VS Code with the Bozzetto extension, it auto-connects. For other editors, see the [README](Readme.md) for setup.

**3. Edit a `.fs` file and save.** Bozzetto detects the change (~500ms debounce), reloads the file via `#load` (~100ms), and if you have live testing enabled, affected tests re-run automatically.

**4. Run tests from the Bozzetto REPL** (not `dotnet test`):

```fsharp
// Run a specific test module
Expecto.Tests.runTestsWithCLIArgs [] [||] Bozzetto.Tests.SomeModule.tests;;

// Run all tests
Expecto.Tests.runTestsWithCLIArgs [] [||] Bozzetto.Tests.AllTests.tests;;
```

> **Signature note:** `runTestsWithCLIArgs` takes `(cliArguments: string list, argv: string[], test: Test)` — the **third argument is a single `Test` value**, not an array. A `[<Tests>]` module binding like `SomeModule.tests` is already a single combined `Test`; do NOT wrap it in `[| ... |]`. Passing an array lands it in the `argv` slot and produces the confusing error `expected string but got Test`.

**5. Check test output** in the Bozzetto console window. Exit code 0 = all passed. Exit code 2 = passed but no TTY detected (cosmetic, ignore it). Exit code 1 = actual failures.

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
genuine hot path; do heavier one-off profiling ad hoc in the REPL.

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
- **Dashboard** — `http://localhost:47750/dashboard` shows session state, events, test results
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

For local development, prefer running tests inside Bozzetto's own REPL for instant feedback.

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
- **Conventional Commits** — `feat(core): add session routing`, `fix(dashboard): handle reconnect`, `docs: update contributing guide`
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
- Does it affect multiple current clients? (VS Code, Neovim, web dashboard, MCP)
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
                │  │ FSI Actor│  │  ← F# Interactive session
                │  └─────────┘  │
                │  ┌─────────┐  │
                │  │  File    │  │  ← watches .fs/.fsx changes
                │  │ Watcher  │  │
                │  └─────────┘  │
                │  ┌─────────┐  │
                │  │  MCP     │  │  ← AI + editor communication
                │  │ Server   │  │
                │  └─────────┘  │
                 └──┬──┬──┬──┬───┘
                    │  │  │  │
     ┌───────┐ ┌────┴──┐ ┌┴──────┐  ┌──────────┐
     │VS Code│ │Neovim │ │ Web   │  │MCP Client│
     └───────┘ └───────┘ │ Dash  │  └──────────┘
                          └───────┘
```

Key architectural concepts:
- **Thin clients** — editors, dashboard tabs, and MCP clients use the same session-scoped daemon contracts
- **Web dashboard** — browser operations and state updates use Falco.Datastar and SSE
- **Worker isolation** — each FSI session runs in an isolated sub-process (Erlang-style)
- **SSE for reads** — all state changes push to clients via Server-Sent Events
- **POST for commands** — write operations are POST-only, return acknowledgment only

## Questions?

- Open a [project issue](https://forge.spkez.dev/FidelityFramework/Bozzetto/issues) for general questions
- Open an [Issue](https://forge.spkez.dev/FidelityFramework/Bozzetto/issues) for bugs or feature requests
- PRs are always welcome — even small ones

Thank you for contributing! 🎉
