# Bozzetto — Visual Studio Extension

A Visual Studio extension for [Bozzetto](../Readme.md) — the live F# development server. Evaluate F# code, see inline results, stream diagnostics into the Error List, get completions, manage sessions, control hot reload, and monitor live test status — all from within Visual Studio.

## Requirements

- Visual Studio 2022 17.14+ (the extension uses the [new out-of-process Extensibility SDK](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/))
- .NET SDK 10.0+
- Bozzetto CLI: `dotnet tool install --global Bozzetto`
- Windows only (amd64 or arm64)

## ⚡ Quick Start

1. **Install Bozzetto CLI**: `dotnet tool install --global Bozzetto`
2. **Install the VSIX**: there is no published Bozzetto VSIX — this extension is deprecated and is not built or published, so use the retained [Installing from Source](#installing-from-source) steps below. Anything on the upstream SageFs [Releases](https://github.com/WillEhrendreich/SageFs/releases) page is an upstream build, not a Bozzetto release.
3. **Open an F# project** in Visual Studio 2022
4. **Press `Alt+Enter`** on any expression — the daemon starts automatically
5. **Enable live testing, then save a file** — test state and gutter markers update

The extension connects to the Bozzetto daemon automatically. No configuration needed for eval and session features; live testing itself is still an explicit toggle.

> **Important**: Bozzetto must be installed as a **global** .NET tool (`dotnet tool install --global Bozzetto`). Local tool installs are not supported by the Visual Studio extension. If `boz` is not found in your PATH, the extension will show "⚠ Bozzetto daemon is not running" in the Bozzetto Output channel.

## Installing from Source

```bash
cd bozzetto-vs/Bozzetto.VisualStudio
dotnet build
```

Then load the extension in Visual Studio's experimental instance, or install the generated VSIX.

## Features

| Feature | Status |
|---------|--------|
| Code evaluation (selection, file, block) | ✅ |
| Inline eval adornments | ✅ |
| CodeLens ("▶ Eval" per function/type/module) | ✅ |
| Error/warning squiggles (Error List integration) | ✅ |
| F# completions (working_directory context, 14 kind mappings) | ✅ |
| Gutter test status markers (populated on startup) | ✅ |
| TypeExplorer (auto-refreshes on session warmup) | ✅ |
| Live test status panel with Run Policy picker | ✅ |
| Daemon health check on startup (Output channel notification) | ✅ |
| Session management (create, switch, reset, hard reset) | ✅ |
| Hot reload file watching | ✅ |
| Coverage gutter signs | ✅ CoverageGlyphTagger renders green/red/gray bars |
| Test source navigation | ✅ TestStateTracker maps test names to file/line |
| Failure narratives | ✅ Inline failure adornments enriched with causal analysis |
| Themes | ❌ VS SDK doesn't expose color contribution API |
| Call graph viewer | ❌ Available in VS Code and Neovim |
| History browser | ❌ Available in VS Code and Neovim |

## ⌨️ Keybindings

| Action | Binding |
|--------|---------|
| Evaluate selection | `Alt+Enter` |
| Evaluate file | `Shift+Alt+Enter` |
| Toggle live testing | Command: Bozzetto Enable/Disable Live Testing |
| Mark all stale | `Ctrl+Shift+S` |
| Open test panel | View → Other Windows → Bozzetto Live Tests |

### Code Evaluation

| Command | Keybinding | Description |
|---------|-----------|-------------|
| Bozzetto: Evaluate Selection | `Alt+Enter` | Evaluate selected text, or the block around the cursor if nothing is selected |
| Bozzetto: Evaluate File | `Shift+Alt+Enter` | Evaluate the entire file |
| Bozzetto: Evaluate Code Block | *(no keybinding)* | Evaluate the block around the cursor (accessible via Extensions menu) |

Results appear inline as adornments and in the **Bozzetto Output** window.

### CodeLens

"▶ Eval" buttons appear above every F# function, type, and module. Click to evaluate. Live test CodeLens shows test status when live testing is enabled.

### Completions

F# completions are powered by Bozzetto's FSI session. The extension passes `working_directory` context and maps all 14 completion kind variants to their native VS equivalents. Completions have a 3-second timeout to keep the IDE responsive.

### Error List Integration

Bozzetto diagnostics (type errors, warnings) stream into the native VS Error List via SSE — real-time feedback as you code.

### Gutter Test Status Markers

Pass/fail/stale icons appear in the editor margin next to each test. Markers are seeded from daemon state on extension load (`InitialStatePoll`) so the gutter is populated immediately, even before the next test run.

### Session Management

| Command | Description |
|---------|-------------|
| Bozzetto: Create Session | Create a new isolated FSI session |
| Bozzetto: Configure Warmup Auto-Open | Create or open `.bozzetto/config.fsx` and disable warmup namespace auto-open |
| Bozzetto: Switch Session | Switch to a different session |
| Bozzetto: Stop Session | Stop the active session |
| Bozzetto: Reset Session | Soft reset (clear definitions, keep DLLs) |
| Bozzetto: Hard Reset | Full rebuild and reload |
| Bozzetto: Session Context | Show loaded assemblies, namespaces, warmup details |

### Daemon Lifecycle

| Command | Description |
|---------|-------------|
| Bozzetto: Start Daemon | Start the Bozzetto daemon (auto-detects project/solution) |
| Bozzetto: Stop Daemon | Stop the running daemon |
| Bozzetto: Open Dashboard | Open the web dashboard in your browser |

On extension load, a startup health check writes the daemon status to the **Bozzetto Output** channel so you know immediately whether the daemon is reachable.

### Hot Reload

| Command | Description |
|---------|-------------|
| Bozzetto: Toggle Hot Reload for File | Toggle watching for the active file |
| Bozzetto: Toggle Hot Reload for Directory | Toggle watching for a directory |
| Bozzetto: Watch All Files | Enable watching for all project files |
| Bozzetto: Unwatch All Files | Disable all file watching |
| Bozzetto: Refresh Hot Reload | Refresh the hot reload file list |

### Live Testing

| Command | Description |
|---------|-------------|
| Bozzetto: Enable/Disable Live Testing | Toggle the live test pipeline |
| Bozzetto: Run All Tests | Execute all tests now |
| Bozzetto: Set Run Policy | Configure which test categories auto-run |
| Bozzetto: Live Testing Dashboard | Open a tool window with test summary and results |
| Bozzetto: Show Recent Events | Display recent pipeline events |

The **Set Run Policy** command opens an interactive picker. Select a category and policy, then click **Apply** to update the daemon.

### Tool Windows

- **Session Context** — Connection status, loaded assemblies, opened namespaces, warmup details
- **Hot Reload Files** — Project files with watch status
- **Live Testing** — Test summary, toggle, run-all, per-category run policy, and results text
- **Type Explorer** — Browse .NET types and namespaces; auto-refreshes when a new FSI session warms up

## 🎨 Understanding What You See

### Gutter Glyphs (left margin)

| Glyph | Color | Meaning |
|-------|-------|---------|
| ● | Green | Test passing |
| ● | Red | Test failing |
| █ (bar) | Green | Coverage healthy — all tests pass |
| █ (bar) | Red | Coverage degraded — some tests failing |
| █ (bar) | Gray | No test coverage |

### Inline Adornments

| What you see | Meaning |
|-------------|---------|
| Gray text after expression | Evaluation result |
| Red text with `⊘` | Test failure with Expected/Actual diff |
| Narrative tooltip on hover | Failure context: what changed, time since last pass |

### Status Bar

| Item | Meaning |
|------|---------|
| `Bozzetto: Connected` | Daemon running, SSE stream active |
| `Bozzetto: Disconnected` | Connection lost — will retry |
| `Tests: 42 ✓ 2 ✗` | Test summary |

## Architecture

The extension uses a two-layer architecture:

- **`Bozzetto.VisualStudio`** (C#) — Thin shim using the VS Extensibility SDK. Defines commands, CodeLens providers, tool windows, and menu items. Targets `net8.0-windows8.0`.
- **`Bozzetto.VisualStudio.Core`** (F#) — All HTTP client logic, daemon management, SSE subscriptions, and domain types. This is where the real work happens.

Communication with Bozzetto uses the same HTTP + SSE protocol as all other frontends. The daemon runs on port 47749 (MCP) and 47750 (dashboard/API) by default.

## 🔧 Troubleshooting

### Extension not loading

- Requires Visual Studio 2022 17.14+ and .NET 8.0+
- Check Extensions → Manage Extensions → Bozzetto is enabled
- Look in Output → Bozzetto for error messages

### Bozzetto not found on PATH

Install Bozzetto: `dotnet tool install --global Bozzetto`. Make sure the .NET tools directory is on your PATH. Local tool installs (`dotnet tool install --local`) are **not** supported; only global installs work with the extension.

### Commands do nothing / no feedback

Check the Bozzetto Output window (`View → Output → Bozzetto`). The extension logs errors there. If the daemon isn't running, start it with "Bozzetto: Start Daemon".

### Error List not updating

Verify the SSE connection is active. The extension subscribes to `/events` on the dashboard port (47750). Firewall or proxy issues can block this.

### No gutter markers

1. Ensure Bozzetto daemon is running (check status bar)
2. Ensure your project has Expecto tests
3. Try: Extensions → Bozzetto → Mark All Tests Stale

### Kill Switches

If a feature is causing problems, you can disable it by creating an empty file at the specified path. Delete the file to re-enable the feature (a VS reload may be required).

| File | What it disables |
|------|-----------------|
| `%LOCALAPPDATA%\Bozzetto\disable-glyphs.flag` | All gutter test status markers (also acts as a master switch — disables squiggles and inline hints too) |
| `%LOCALAPPDATA%\Bozzetto\disable-squiggles.flag` | Error/warning squiggles from diagnostics |
| `%LOCALAPPDATA%\Bozzetto\disable-inline-hints.flag` | Inline failure adornments |
| `.bozzetto/disable-inline-hints.flag` | Disables inline eval adornments (per-solution) |
| `.bozzetto/disable-test-glyphs.flag` | Disables test gutter markers (per-solution) |
| `.bozzetto/disable-coverage-glyphs.flag` | Disables coverage gutter bars (per-solution) |

To create a kill switch from PowerShell:
```powershell
New-Item "$env:LOCALAPPDATA\Bozzetto\disable-glyphs.flag" -Force
```

To remove a kill switch:
```powershell
Remove-Item "$env:LOCALAPPDATA\Bozzetto\disable-glyphs.flag"
```

## Development

```bash
cd bozzetto-vs/Bozzetto.VisualStudio
dotnet build
```

Use Visual Studio's experimental instance (F5) to test. The C# shim project references the F# core via project reference.
