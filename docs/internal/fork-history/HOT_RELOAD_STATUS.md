> Historical Bozzetto fork document, preserved from `5a0356e`. Superseded by the v0.6.834 integration. See [the update record](../../UPSTREAM_SYNC.md) and current guides under `docs/`. Paths and feature claims below describe the old snapshot.

# Bozzetto Hot Reload Status

## ✅ What Works

### 6. Browser Auto-Refresh via DevReload Middleware (NEW)
- **`Bozzetto.DevReload`** — pure broadcaster in `Bozzetto.Core` with zero ASP.NET dependency
- **`Bozzetto.DevReloadMiddleware`** — ASP.NET Core middleware in `Bozzetto` project
- Injects a tiny `<script>` before `</body>` in all `text/html` responses
- Script opens SSE connection to `/__bozzetto__/reload`
- When Harmony detours fire after a hot reload, `DevReload.triggerReload()` signals all connected browsers
- Browser auto-refreshes — **no manual F5 needed**

#### Usage in a Falco app
```fsharp
open Bozzetto.DevReloadMiddleware

webHost [||] {
  use_middleware middleware
  // your routes...
}
```
Or with `IApplicationBuilder` directly:
```fsharp
app.Use(Bozzetto.DevReloadMiddleware.middleware) |> ignore
```

### 1. Automatic File Watching (NEW in 0.4.18)
- **Worker processes automatically watch project directories** for `.fs`, `.fsx`, `.fsproj` changes
- On `.fs`/`.fsx` change: debounced `#load` + Harmony method detouring — live-patches running code
- On `.fsproj` change: triggers soft reset to pick up new references
- Configurable via `--no-watch` flag to disable
- 500ms debounce prevents thrashing on rapid saves

### 2. Hot Reload with Harmony Method Detouring
- **PROVEN WORKING** with `test-hot-reload.fsx` example
- Handlers can be updated in real-time — no restart needed
- File-change-triggered `#load` now carries `hotReload=true` in Args
- This ensures the Harmony detouring middleware fires on both:
  - REPL-typed code (interactive)
  - File-change-triggered reloads (automatic)
- Changes appear instantly in browser

### 3. FSI Compatibility Middleware
- Automatically rewrites `use` → `let` for indented use statements
- Applies to interactively-sent code via MCP
- Handles FSI incompatibilities transparently
- Located in `Bozzetto/FsiRewrite.fs` and `Bozzetto/Middleware/FsiCompatibility.fs`

### 4. Multi-line Code Submission
- Fixed in `Bozzetto/Mcp.fs` sendFsharpCode
- Splits code by `;;` delimiter
- Executes each statement sequentially
- Returns all results concatenated

### 5. Enhanced Error Reporting
- Shows full exception details including:
  - Exception type
  - Message
  - Stack trace
  - Inner exceptions (recursively)
- Located in `Bozzetto/Mcp.fs` formatEvalResult

## 🔥 How Hot Reload Works End-to-End

1. **File change detected** → FileWatcher debounces (500ms)
2. **Action decided** → `fileChangeAction` routes `.fs` → Reload, `.fsproj` → SoftReset
3. **Code sent to FSI** → `#load @"path/to/file.fs"` with `hotReload=true` in Args
4. **FSI evaluates** → generates new dynamic assembly with updated method bodies
5. **Harmony middleware fires** → fuzzy-matches new methods against existing project methods
6. **Method detour applied** → old method pointers patched to call new implementations
7. **No restart needed** → next HTTP request uses the new code automatically

## ⚠️ Known Limitations

### Project Loading with `--proj`
When using `Bozzetto --proj MyProject.fsproj`:
- Bozzetto loads **compiled DLLs**, not source code
- The FSI compatibility rewrite only affects:
  - Files loaded with `--use` flag (`.fsx` scripts)
  - Code sent interactively via MCP
  - Files reloaded via file watcher `#load`
- Already-compiled DLL code is NOT rewritten

### Console I/O — Resolved
- PrettyPrompt has been **removed** from Bozzetto. The daemon-first architecture runs headless; `Bozzetto connect` provides the REPL client.

## 🎯 How to Use Hot Reload

### Automatic (File Watcher — Recommended)
```powershell
# Start Bozzetto with your project — file watching is ON by default
Bozzetto --proj HarmonyServer/HarmonyServer.fsproj

# Start your web server from the REPL, then just edit .fs files
# Changes are picked up automatically!
# Look for 🔥 or 📄 messages in the Bozzetto console
```

### Manual (REPL — For Experimentation)
```powershell
cd C:\Code\Repos\Bozzetto
Bozzetto --use test-hot-reload.fsx
```

Wait for "Starting web server..." message, then:
1. Open browser to http://localhost:5555
2. In FSI, send updated handler code
3. Refresh browser → see changes instantly!

### Disabling File Watching
```powershell
Bozzetto --proj MyProject.fsproj --no-watch
```

## 📁 Key Files

| File | Purpose |
|------|---------|
| `Bozzetto/DevReloadMiddleware.fs` | ASP.NET middleware: SSE endpoint + HTML script injection |
| `Bozzetto.Core/DevReload.fs` | Pure broadcaster: `triggerReload()`, `registerClient`, `unregisterClient` |
| `Bozzetto/WorkerMain.fs` | Starts file watcher, routes changes to FSI |
| `Bozzetto.Core/FileWatcher.fs` | Pure file watching with debounce |
| `Bozzetto.Core/Middleware/HotReloading.fs` | Harmony method detouring |
| `Bozzetto.Core/ActorCreation.fs` | Registers middleware pipeline |
| `Bozzetto.Tests/HotReloadTests.fs` | 21 integration tests |
| `Bozzetto.Tests/FileWatcherTests.fs` | Pure function tests |

## ✨ Summary

**Hot reload is fully wired and working!** The system:
- Watches project directories for `.fs`/`.fsx`/`.fsproj` changes
- Debounces (500ms) to avoid thrashing
- Sends `#load` with `hotReload=true` to FSI
- Harmony library detours method pointers at runtime
- No restart, no manual intervention — just edit and save
