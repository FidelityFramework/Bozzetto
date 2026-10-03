# 🏗️ Architecture

One Bozzetto daemon runs per machine. It starts with no project loaded, and creates sessions on demand. I didn't
want a daemon that assumes it knows what you're working on before you've told it. Each session is a separate OS
worker process with its own FSI, loaded project assemblies, and file watcher. VS Code, Neovim, and MCP
clients all talk to the daemon through session-scoped HTTP and SSE contracts. See the
[architecture diagram](../Readme.md#-one-daemon-every-client) for how clients connect.

The daemon listens on port 47749 for MCP (streamable HTTP at `/`, legacy SSE at `/sse`) and the editor state
stream (`/events`). Port 47750 carries only a minimal control listener (`/api/daemon-info`, `/api/shutdown`),
kept separate from the MCP listener so `boz status` and `boz stop` still get answers if it dies. Target framework is net10.0; the
solution file is `Bozzetto.slnx`.

The test suite uses Expecto unit tests, FsCheck property-based state-machine tests, Verify snapshots, and
binary-persistence property tests. The README's test-count badge and property-test count are derived from
source, never hand-typed, but restamping is an explicit step (`dotnet run --project Bozzetto.Tests -- --update-badge`,
see `Bozzetto.Tests/TestCountBadge.fs`), and CI or a normal test run doesn't do it for you, so the
numbers can lag between restamps. If you spot a stale number, that's why.

## Project Structure

```
Bozzetto.Core/       — Shared engine, session, testing, persistence, and protocol logic
Bozzetto/            — CLI tool, daemon, and MCP server
Bozzetto.Tests/      — Expecto test project
bozzetto-vscode/     — VS Code extension (Fable F#→JS)
docs/              — GitHub Pages site
```

The separate upstream Neovim plugin, [sagefs.nvim](https://github.com/WillEhrendreich/sagefs.nvim), is not part of this repository; it is a client of the inherited F# session contracts, whose embedded production hosting the [Clefx host transition](Bozzetto_Clefx_Host_Transition_2026-10-01.md) retires in this checkout.

## Client Pipeline

Current clients use the daemon as the source of truth:

```
Editor / MCP command
  → session-scoped daemon endpoint
    → isolated FSI worker
      → structured result and SSE state updates
```

The Raylib application and game demos are there because they prove Bozzetto works for real projects
outside web dev too.

## Session Lifecycle

1. Daemon starts with no project and no session loaded
2. A client creates a session with a project path
3. The daemon spawns a worker sub-process, loads the project, starts watching files
4. Clients send code, read diagnostics, and run tests, all through the daemon
5. Multiple clients can connect to the same session simultaneously

## FSI Quirks & Rewrites

Bozzetto automatically rewrites `use` to `let` inside nested scopes (functions, computation expressions) because
FSI doesn't support `use` there. That means disposables aren't automatically disposed in the REPL. Fine for
quick experiments; worth remembering if you're leaning on a `use` binding to clean something up in a long
session.

Other FSI behaviors worth knowing: redefining a binding shadows it instead of erroring, each `;;` boundary is
its own transaction, there's no `[<EntryPoint>]`, and assembly loading is scoped to the session.

Rewrite logic: [`Bozzetto.Core/FsiRewrite.fs`](../Bozzetto.Core/FsiRewrite.fs) (26 lines, genuinely small).
PRs welcome.
