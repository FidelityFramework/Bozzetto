# Bozzetto — VS Code Extension

> **Live eval works today. Live testing and coverage are available, but still being stabilized.**

Bozzetto brings live evaluation, instant test feedback, and coverage visualization to VS Code. No configuration needed — just press `Alt+Enter`.

## ✨ What You Get

| Feature | What it does |
|---------|-------------|
| **Inline Results** | Expression values appear next to your code as you evaluate |
| **Live Testing** | When live testing is enabled, save-triggered test updates can feed green ✓ / red ✗ gutter state (still stabilizing) |
| **Coverage Gutters** | Colored bars show which lines are covered by tests |
| **Failure Details** | Inline `⊘` markers show Expected vs Actual diffs |
| **Failure Narratives** | Rich context: what changed, when it last passed, causal analysis |
| **Test Source Jump** | Test Explorer items link to their source location automatically |
| **Eval Performance** | Status bar sparkline with P50/P95/P99 eval latencies |

> This is the Bozzetto fork's extension source. Package IDs, settings, and command-palette labels still use Bozzetto. The published Bozzetto Marketplace/Open VSX extension is upstream, not a Bozzetto release.

---

## 🚀 Quick Start

1. **Build and install Bozzetto CLI**: Follow the [local installation instructions](../Readme.md#installation).
2. **Build and install this extension**: Follow [Installing](#installing) below.
3. **Open an F# project** in VS Code
4. **Press `Alt+Enter`** on any expression — the daemon starts automatically and results appear inline

That's it for evaluation. Live testing is available, but it does not auto-enable yet — use `Bozzetto: Enable Live Testing` once your test session is loaded.

---

## ⌨️ Keybindings

| Action | Keybinding | Command Palette |
|--------|-----------|-----------------|
| Evaluate selection / `;;` block | `Alt+Enter` | Bozzetto: Evaluate Selection / Line |
| Evaluate entire file | `Alt+Shift+Enter` | Bozzetto: Evaluate Entire File |
| Evaluate & advance to next block | `Shift+Enter` | Bozzetto: Evaluate & Advance |
| Evaluate all `;;` blocks | `Ctrl+Alt+Enter` | Bozzetto: Evaluate All Blocks |
| Cancel running evaluation | `Ctrl+Shift+C` | Bozzetto: Cancel Evaluation |
| Next `;;` code block | `Ctrl+Down` | Bozzetto: Next Code Block |
| Previous `;;` code block | `Ctrl+Up` | Bozzetto: Previous Code Block |
| Next failing test | `Alt+Shift+]` | Bozzetto: Next Failing Test |
| Previous failing test | `Alt+Shift+[` | Bozzetto: Previous Failing Test |
| Clear inline results | — | Bozzetto: Clear Inline Results |
| Run all tests | — | Bozzetto: Run All Tests |
| Toggle live testing | — | Bozzetto: Enable / Disable Live Testing |
| Session picker | — | Bozzetto: Switch Session |
| Reset session | — | Bozzetto: Hard Reset (Rebuild) |
| Load current script | — | Bozzetto: Load Current Script |

> 💡 All keybindings are scoped to F# files only. `Shift+Enter` is deactivated when a notebook is focused to avoid conflicts with Jupyter.

---

## 🎨 Understanding What You See

### Gutter Icons

| Icon | Meaning |
|------|---------|
| ✓ (green) | All tests passing for this line |
| ✗ (red) | A test covering this line has failed |
| ⊘ (gray) | Test skipped or disabled by policy |
| ● | Test status marker on test definition lines |

### Coverage Bars (left gutter)

| Marker | Meaning |
|--------|---------|
| ▸ (green) | Line is covered — all covering tests pass |
| ▸ (red) | Line is covered — some covering tests are failing |
| ○ (gray) | Line has no test coverage |

### Inline Decorations

| Decoration | Meaning |
|------------|---------|
| `= 42` (gray text) | Evaluation result from `Alt+Enter` |
| `⊘ testName — Expected: 5  Actual: 3` | Test failure with Expected/Actual diff |
| `⊘ testName — exception message` | Test failure from unhandled exception |
| `⊘ testName — Timed out` | Test exceeded its timeout |
| `ℹ️ narrative summary` | Failure narrative: what changed, when it last passed |

> 💡 **Hover** over any decoration for more details, including failure narratives with causal analysis.

### Status Bar

The status bar (bottom of VS Code) shows three items:

| Item | Example | Meaning |
|------|---------|---------|
| **Daemon status** | `⚡ Bozzetto: MyProject [3]` | Connected, project name, eval count |
| **Test summary** | `🧪 42/42 ✓` | All tests passing |
| | `🧪 40/42 ✗ 2` | 40 passing, 2 failing (red background) |
| | `🧪 ⟳ Running 5/42` | Tests currently executing (spinner) |
| | `🧪 ⚠ 10/42 stale` | Tests need re-run — code changed (yellow background) |
| | `🧪 No tests` | No tests discovered yet |
| **Eval performance** | `P50: 12ms P95: 45ms` | Eval latency percentiles with sparkline |

Click the daemon status item to open the dashboard.

---

## Features

### Code Evaluation
- **Alt+Enter** — Evaluate the current selection or `;;`-delimited code block. Results appear as inline decorations.
- **Alt+Shift+Enter** — Evaluate the entire file
- **Shift+Enter** — Evaluate current block and advance cursor to the next
- **Ctrl+Alt+Enter** — Evaluate all `;;` blocks in the file
- **CodeLens** — Clickable "▶ Eval" buttons above every `;;` block
- **Density cycling** — Toggle between Full / Normal / Minimal inline result display

### Live Unit Testing
- **Inline test decorations** — ✓/✗/● markers on test lines, updated in real-time via SSE
- **Native Test Explorer** — Tests appear in VS Code's built-in Test Explorer via a `TestController` adapter
- **Test result CodeLens** — "✓ Passed" / "✗ Failed" above every test function
- **Failure diagnostics** — Failed tests appear as native VS Code squiggles
- **Coverage gutter bars** — Per-line coverage health (AllPassing / SomeFailing / NoCoverage) shown in the gutter
- **Inline failure details** — `⊘` markers with Expected/Actual diffs, exception messages, and timeouts
- **Failure narratives** — Enriched failure context: summary, time since last pass, causal changes
- **Test source locations** — Test Explorer items automatically link to their source file and line
- **Test policy controls** — Enable/disable live testing, run all tests, or configure run policies from the command palette
- **Call graph viewer** — Visualize test dependency graphs
- **Test trace** — Browse test cycle events

### Live Diagnostics
- F# type errors and warnings stream in via SSE as you edit, appearing as native VS Code squiggles

### Session Management
- **Session Context sidebar** — Loaded assemblies, opened namespaces, failed opens, warmup details
- **Sessions sidebar** — View all sessions with inline switch/stop/reset actions
- **Multi-session** — Create, switch, and manage multiple sessions from the command palette
- **Export session** — Save current session state as a `.fsx` script
- **Session menu** — Quick-access menu for all session operations

### More
- **Type Explorer sidebar** — Browse .NET types and namespaces interactively from the activity bar
- **Event history** — Browse recent pipeline events via QuickPick
- **FSI bindings browser** — View all current FSI bindings
- **Dashboard webview** — Open the Bozzetto dashboard directly inside VS Code
- **Status bar** — Active project, eval count, test summary, eval performance sparkline. Click to open dashboard.
- **Auto-start** — Detects `.fsproj`/`.sln`/`.slnx` files and offers to start Bozzetto automatically
- **Ionide integration** — Hijacks Ionide's `FSI: Send Selection` commands so Alt+Enter routes through Bozzetto
- **7 custom theme colors** — Inline result colors respect your VS Code theme

---

## Requirements

- [Bozzetto built from this checkout](../Readme.md#installation), using the inherited `boz` command
- An F# project (`.fsproj` or `.sln`) in your workspace

## Installing

Build the extension from this checkout. There is no published Bozzetto extension yet; installing `willehrendreich.bozzetto` installs upstream.

```bash
cd bozzetto-vscode
dotnet tool restore
npm ci
npm run compile
npx @vscode/vsce package
code --install-extension bozzetto-0.6.834.vsix
```

The extension still shares upstream's identifier and command labels. See the [fork status](../Readme.md#fork-status) before installing it alongside Bozzetto.

---

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `bozzetto.mcpPort` | `47749` | Bozzetto MCP server port |
| `bozzetto.dashboardPort` | `47750` | Legacy fallback dashboard port. Bozzetto normally discovers the daemon and derives the dashboard port from the MCP port. |
| `bozzetto.autoStart` | `true` | Automatically start Bozzetto when opening F# projects |
| `bozzetto.projectPath` | `""` | Explicit `.fsproj` path (auto-detect if empty) |
| `bozzetto.logLevel` | `"info"` | Output channel verbosity (`debug`, `info`, `error` — no `warn` level) |
| `bozzetto.inlineResultTimeout` | `30000` | How long (ms) inline eval results stay visible before auto-clearing. `0` keeps them forever. |
| `bozzetto.cellHighlight` | `true` | Highlight the current code cell (block) the cursor is in |
| `bozzetto.density` | `"full"` | Visual annotation level: `full` (all decorations), `normal` (inline results and test signs), `minimal` (inline results only) |
| `bozzetto.typeExplorerRoot` | `""` | Root namespace for the Type Explorer tree (empty shows all top-level namespaces) |

---

## Commands

### Evaluation

| Command | Keybinding | Description |
|---------|-----------|-------------|
| Bozzetto: Evaluate Selection / Line | `Alt+Enter` | Evaluate selection or `;;` block |
| Bozzetto: Evaluate Entire File | `Alt+Shift+Enter` | Evaluate full file |
| Bozzetto: Evaluate & Advance | `Shift+Enter` | Evaluate current block, move cursor to next |
| Bozzetto: Evaluate All Blocks | `Ctrl+Alt+Enter` | Evaluate every `;;` block in the file |
| Bozzetto: Evaluate Code Block | — | Evaluate the `;;`-delimited block at cursor |
| Bozzetto: Cancel Evaluation | `Ctrl+Shift+C` | Cancel a running evaluation |
| Bozzetto: Next Code Block | `Ctrl+Down` | Jump cursor to next `;;` block |
| Bozzetto: Previous Code Block | `Ctrl+Up` | Jump cursor to previous `;;` block |
| Bozzetto: Clear Inline Results | — | Remove all inline result decorations |
| Bozzetto: Cycle Density (Full → Normal → Minimal) | — | Toggle Full → Normal → Minimal inline display |
| Bozzetto: Load Current Script | — | Load the active `.fsx` file into the session |

### Daemon & Session

| Command | Keybinding | Description |
|---------|-----------|-------------|
| Bozzetto: Start Daemon | — | Start the Bozzetto daemon |
| Bozzetto: Stop Daemon | — | Stop the Bozzetto daemon |
| Bozzetto: Restart Daemon | — | Restart the Bozzetto daemon |
| Bozzetto: Open Dashboard | — | Open web dashboard in VS Code |
| Bozzetto: Check Health | — | Run the extension's health check |
| Bozzetto: Create Session | — | Create a new FSI session |
| Bozzetto: Switch Session | — | Switch to a different session |
| Bozzetto: Switch Project | — | Change which `.fsproj`/`.sln` the session loads |
| Bozzetto: Browse for Project | — | Pick a project file from a file dialog |
| Bozzetto: Switch Workflow | — | Switch the session between REPL and Live Testing |
| Bozzetto: Stop Session | — | Stop the active session |
| Bozzetto: Reset Session | — | Soft reset (clear definitions, keep session) |
| Bozzetto: Hard Reset (Rebuild) | — | Full rebuild and reload |
| Bozzetto: Session Menu | — | Quick-access menu for all session operations |
| Bozzetto: Export Session as .fsx | — | Save session state to a script file |
| Bozzetto: Show FSI Bindings | — | Browse current FSI bindings |
| Bozzetto: Configure Warmup Auto-Open | — | Create or open `.bozzetto/config.fsx` |
| Bozzetto: Refresh Sessions | — | Refresh the Sessions sidebar |
| Bozzetto: Refresh Session Context | — | Refresh the Session Context sidebar |
| Bozzetto: Open Getting Started Sample | — | Open the bundled getting-started `.fsx` |

The Sessions sidebar also has inline per-row actions (Switch To, Stop, Reset) — click the icons on a session row instead of going through the command palette.

### Live Testing

| Command | Keybinding | Description |
|---------|-----------|-------------|
| Bozzetto: Enable Live Testing | — | Turn on live test execution |
| Bozzetto: Disable Live Testing | — | Turn off live test execution |
| Bozzetto: Run All Tests | — | Execute all tests now |
| Bozzetto: Set Test Run Policy | — | Configure per-category run policies |
| Bozzetto: Show Test Call Graph | — | Visualize test dependency graph |
| Bozzetto: Show Test Trace | — | Browse test cycle events |
| Bozzetto: Show Recent Events | — | Browse pipeline event history |
| Bozzetto: Explain Test Failure | — | Enriched failure context for the test at cursor |
| Bozzetto: Suggest Repair for Failed Test | — | Trace causal changes and suggest a fix |
| Bozzetto: Next Failing Test | `Alt+Shift+]` | Jump to the next failing test |
| Bozzetto: Previous Failing Test | `Alt+Shift+[` | Jump to the previous failing test |

### Sidebar Views

| View | Location | Description |
|------|----------|-------------|
| Session Context | Activity Bar | Assemblies, namespaces, warmup details |
| Sessions | Activity Bar | All sessions with inline switch/stop/reset |
| API Browser | Activity Bar | Browse .NET types and namespaces (the Type Explorer) |

---

## 🔧 Troubleshooting

### "Bozzetto: offline" in status bar
The daemon isn't running. Fix:
1. Run `dotnet tool install --global Bozzetto` if you haven't installed the CLI
2. Click the status bar item, or run Command Palette → "Bozzetto: Start Daemon"
3. The extension auto-starts the daemon when it detects F# projects — if this isn't happening, check `bozzetto.autoStart` is `true`

### No inline results after Alt+Enter
1. Check the Output panel (View → Output → select "Bozzetto" from the dropdown) for errors
2. Verify the daemon is running: the status bar should show `⚡ Bozzetto: YourProject`
3. Try restarting: Command Palette → "Bozzetto: Hard Reset (Rebuild)"
4. Make sure your cursor is in an F# file (keybindings only activate for `fsharp` language)

### Tests not appearing in gutter
1. Bozzetto discovers [Expecto](https://github.com/haf/expecto), xUnit (including v3), NUnit, MSTest, and TUnit tests. Expecto is the best-covered path today, so start there if you can.
2. Enable live testing: Command Palette → "Bozzetto: Enable Live Testing"
3. Check that the daemon discovered tests: status bar should show a test count (e.g., `🧪 42/42 ✓`)
4. Check the Output panel for errors

### Status bar shows no connection
The SSE connection to the daemon dropped. It will reconnect automatically. If it persists:
1. Check if the daemon process is still running (look for `Bozzetto` in your task manager)
2. Restart the daemon: Command Palette → "Bozzetto: Restart Daemon"
3. Check Output panel (select "Bozzetto") for connection errors

### "No .fsproj or .sln found"
Open a folder containing an F# project. The extension auto-detects projects. For non-standard layouts, set `bozzetto.projectPath` in VS Code settings to the explicit path.

### Daemon crashes or restarts unexpectedly
The extension detects daemon crashes and shows a restart prompt. If it keeps crashing:
1. Check the Bozzetto output in your terminal for stack traces
2. Try starting Bozzetto manually from a terminal: `boz`
3. Report fork issues in [Bozzetto](https://forge.spkez.dev/FidelityFramework/Bozzetto). Include the upstream baseline from the [update record](../docs/UPSTREAM_SYNC.md).

---

## Architecture

This extension is written entirely in F# using [Fable](https://fable.io/) — no TypeScript. The F# source compiles to JavaScript, giving you type-safe extension code with the same language as your project.

Key architectural decisions:
- **SSE (Server-Sent Events)** for all real-time data: test results, diagnostics, coverage, eval results
- **POST commands** for all actions: eval, reset, start/stop — responses are acknowledgments only
- **Native VS Code APIs** via Fable bindings: TestController, CodeLens, TreeView, Diagnostics, Decorations

## Development

```bash
cd bozzetto-vscode
npm install
npm run compile
```

Press **F5** in VS Code to launch the Extension Development Host.
