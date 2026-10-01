# MCP Tools Reference

Bozzetto's MCP server exposes Clef/Composer project sessions on port **47749**.
The `composer_*` tools and [Composer browser page](http://127.0.0.1:47749/composer)
share one daemon-owned supervisor, session authority and compiler worker.
Open an explicit `.fidproj`, reserve before editing, build the reservation and
run through `composer_run_current`.

For F#/.NET work, use the separate SageFS service on ports **37749/37750**.
Bozzetto's [retained F# compatibility tools](#retained-f-compatibility-tools)
are documented below; Composer does not require an FSI session.

`tools/list` advertises the registered catalog. Calls validate availability and
authority at execution time, so a listed tool can still refuse an operation.
Use `get_daemon_status` for daemon health and `composer_list_sessions` for Composer
state. The daemon's HTTP endpoints (`/api/...`) are separate from MCP tools.

## Connect

For shared work, use the reviewed installed `boz` and attach to the independently
running daemon over **Streamable HTTP**:

```json
{ "mcpServers": { "bozzetto": { "url": "http://127.0.0.1:47749/" } } }
```

Follow the [live provider checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md)
for the installed compiler/runtime identities and connection setup. If the daemon
is absent, `scripts/start-shared-daemon` starts it from its dedicated external
workspace and preserves existing listeners. The daemon is long-running; inspect
its identity, health and Composer configuration with bounded requests:

```sh
boz status
curl --fail --max-time 3 http://127.0.0.1:47749/health
curl --fail --max-time 3 http://127.0.0.1:47749/api/composer/sessions
```

The dashboard is on **47750**; the Composer page is on **47749**. SSE compatibility
is available at `http://127.0.0.1:47749/sse`. For individual client workflows,
`boz mcp` remains a stdio bridge:

```json
{ "mcpServers": { "bozzetto": { "command": "boz", "args": [ "mcp" ] } } }
```

If tools are missing, confirm that the daemon responds, that the client targets
port 47749 and that the client has a live MCP connection. Reconnect the client
after correcting its configuration. A configured URL alone does not establish
that tools are connected or the compiler worker is configured.

## Clef/Composer workflow

1. Call `composer_open_project` with an absolute `.fidproj` path. Retain the
   returned `host`, `session` and `epoch` for subsequent calls.
2. Call `composer_reserve_edit` and await a successful response before changing
   source or dependency files. Retain its opaque, single-use `reservation`.
3. Make the edit, then call `composer_build` with that reservation.
4. Call `composer_run_current` after an accepted build, supplying `arguments` as
   an array of individual strings (`[]` for none). Composer revalidates inputs
   and executable bytes; never launch an artifact path directly.
5. Close the session when finished and inspect pending cleanup or errors.

Every session operation below takes `host`, `session` and `epoch`, except
`composer_open_project`, `composer_list_sessions` and `composer_retire_worker`.
These handles belong to the returned worker epoch; do not substitute F# session
ids or reuse them after worker replacement.

| Tool | Additional arguments and behavior |
|:---|:---|
| `composer_open_project` | `project`: absolute `.fidproj` path. Returns shared session and worker authority. |
| `composer_list_sessions` | No arguments. Lists shared Composer sessions, worker identity and cleanup failures. |
| `composer_reserve_edit` | `label`: edit description. Withdraws old run authority and returns a reservation; only a successful response authorizes the source write. |
| `composer_build` | `reservation`: opaque token from the successful reservation. Builds that revision and preserves compiler diagnostics and proof refusals. |
| `composer_session_status` | No additional arguments. Reports accepted metadata, pending revocation and cleanup errors. Reading status grants no execution authority. |
| `composer_run_current` | `arguments`: string array. Runs the current accepted artifact through Composer's revalidation gates. |
| `composer_cancel` | No additional arguments. Withdraws authority and cancels outstanding work; physical withdrawal can remain pending or fail. |
| `composer_close_session` | No additional arguments. Closes the session and begins cleanup; inspect `cleanupPending` and `cleanupError`. |
| `composer_retire_worker` | `host`, `epoch`. Retires every session and stops the worker before compiler replacement. Coordinate with other owners; cleanup errors remain explicit refusals, and replacement requires observed process exit and a new epoch. |

The `composer://sessions` MCP resource exposes the same session directory as
`composer_list_sessions` and the browser. Subscribe for updates after shared
changes. Cached accepted metadata always requires execution revalidation.

Active compiler patching is unsupported. A newer compiler needs a validated,
explicit distribution and its own provider acceptance; changing a compiler
checkout does not update the running worker. See the
[development plan](Clef_Composer_Development_Plan.md) for remaining editor,
standalone-host and LLVM ORC JIT work.

## Daemon status and external work leases

These operations do not require a Composer or F# session. Acquire the appropriate
lease before caller-owned expensive work and release it afterward. Built-in
operations account for their own work.

| Tool | What it does |
|:---|:---|
| `get_daemon_status` | Version, health, memory pressure, process telemetry, F# session counts and lease summaries. Composer sessions are listed separately. |
| `acquire_full_build_lease` | Requests a lease for an external full build. |
| `acquire_test_suite_lease` | Requests a lease for an external test-suite run. |
| `acquire_run_app_lease` | Requests a lease for an external application process. |
| `release_work_lease` | Releases the caller-owned `lease_id` returned by a granted acquisition. |

## Cohort and multi-agent coordination

For running several agents against one repo at once. One implicit cohort per
daemon; the first agent to join becomes its conductor. Every tool resolves
the caller's identity from the MCP connection, not the `agentName` argument.
Two connections that pass the same name are still two different members.

| Tool | What it does |
|:---|:---|
| `join_cohort` | Join the daemon's shared coordination session. The first joiner becomes conductor. |
| `leave_cohort` | Leave. Any claims you still hold are orphaned (the conductor must reassign them). |
| `get_cohort_status` | Members, claims and fences, the test matrix, and the landing queue. Wait-free: reads a published snapshot. Also available on the `cohort://status` MCP resource for subscription. |
| `acquire_claim` | Take an exclusive claim over a file or project (`file:<path>` or `project:<path>`) so others know it's yours to edit. |
| `release_claim` | Release a claim you hold. The presented fence must match the current one. |
| `reassign_claim` | Conductor-only: reassign an orphaned claim to a present member. |
| `request_landing` | Queue a landing: your commits are rebased onto the integration head, verified against affected tests, and fast-forwarded in. Landings are strictly serial (one FIFO queue). |
| `set_integration_ref` | Conductor-only: configure the git ref that landings rebase onto, in a dedicated integration worktree. |

## Friction telemetry (local only)

These tools read or write local diagnostic data.

| Tool | What it does |
|:---|:---|
| `get_friction_summary` | Compact summary of recorded MCP friction. |
| `get_friction_report` | Structured JSON report of MCP pain points. |
| `report_friction` | Record structured feedback about a confusing tool call. |
| `manage_local_data` | See what Bozzetto stores under its data dir (rows, bytes, oldest row, retention rules), or clear it. |

## Retained F# compatibility tools

These tools apply to Bozzetto's inherited F# host. New F#/.NET work uses separate
SageFS; Clef/Composer uses the workflow above. `get_session_status` reports which
retained tools apply to the selected F# session's lifecycle. Listing a tool does
not bypass that state check.

<details>
<summary>Expand the retained F# tool reference</summary>

### Execution and status

| Tool | What it does |
|:---|:---|
| `send_fsharp_code` | Evaluate F# code in the session. Each `;;` is a transaction boundary: a failure discards that one statement and keeps everything before it. |
| `check_fsharp_code` | Type-check a snippet without running it, in the current FSI context. Earlier `send_fsharp_code` definitions are in scope, but namespaces still need an explicit `open`. A "not defined" error here almost always just means you forgot the `open`, nothing more sinister. |
| `cancel_eval` | Cancel a running evaluation. |
| `get_session_status` | The selected session's lifecycle, loaded projects, progress, and tools available in the current state. |
| `get_recent_fsi_events` | Recent evals, errors, and loads with timestamps. |

### Sessions and lifecycle

| Tool | What it does |
|:---|:---|
| `create_project_session` | Create an isolated session for one explicit `.fsproj`. Missing generated build state is rebuilt before the session is registered. |
| `create_solution_session` | Create an isolated session for one explicit `.sln` or `.slnx`. |
| `create_bare_session` | Create an isolated project-free REPL. It never auto-discovers. |
| `list_sessions` | List retained F# sessions; use `composer_list_sessions` for Composer. |
| `switch_session` | Change which session your calls route to. |
| `stop_session` | Stop a session by id. MCP-bound sessions can only be stopped by the connection that created them. |
| `reset_fsi_session` | Soft reset: clears definitions, keeps loaded DLLs. |
| `hard_reset_fsi_session` | Full reset: rebuilds the project, reloads, starts fresh. Needed after `.fsproj` or package changes. |
| `get_available_projects` | Discover `.fsproj` / `.sln` / `.slnx` files under a directory. |
| `list_runnable_projects` | List the session's projects and which ones `run_app` can run (`OutputType=Exe`). |
| `switch_workflow` | Switch the session's workflow: `repl` (Interactive), `livetesting` (Live Testing), or `live` (Hot Reload, aliases `hotreload`/`weblive`/`web`, kept for backward compatibility). Creates a new session in the target workflow and stops the old one; VS Code and the dashboard's own `POST /api/sessions/{sid}/workflow` route restart the same session id in place instead. |

### Hot reload and running apps

| Tool | What it does |
|:---|:---|
| `enable_hot_reload` | Turn on file watching and hot reload for the session. |
| `disable_hot_reload` | Turn it off. |
| `reset_hot_reload_state` | List the live state a save kept when you edited its initializer (binding, kept value, waiting initializer), or pass a binding to run only that initializer in the running app. |
| `set_reflection_read_mode` | Show how hot reload watches values read through reflection (the mode, whether the watch is on, and any question a hot reflective loop raised), or pass `exact-every-read`, `mark-on-reflect` or `probe-callers` to switch the running app. No restart. See [Hot Reload](hot-reload.md#reflection-reads). |
| `run_app` | Run the session's executable project the way `dotnet run` would, with hot reload. Applies `launchSettings.json` (first "Project" profile) and picks a free loopback port when the project sets no URL. Restarts an Interactive session into the Hot Reload workflow first, so REPL bindings are lost. Saving source then hot-patches the running app, including a route table built once at startup. |
| `stop_app` | Stop the app started by `run_app`. Its web host stops and frees its port; the session keeps running. |

### Testing and verification

| Tool | What it does |
|:---|:---|
| `list_tests` | List discovered tests, grouped by file with source locations. A compiled-project session's tests are ReflectionOnly and carry no file/line, so those come back under a separate `WithoutSourceLocation` field instead of being dropped. Optional pattern or file filter. |
| `targeted_verify` | Plan a trustworthy verification pass for one changed behavior. Refuses to claim green when session trust is ambiguous or loaded code is stale. It doesn't run tests itself. It returns the next trustworthy move. |
| `explain_test_failure` | Enriched failure context for a test that recently went from passing to failing. |

F# live-testing runs are driven by file saves or editor/dashboard run controls;
agents read results through `list_tests`, `explain_test_failure` and `diagnose`.

### Analysis and diagnostics

| Tool | What it does |
|:---|:---|
| `diagnose` | Full diagnostic report: test failures, cell staleness, ripple plan, suggestions. |
| `coverage_intel` | Coverage-quality analysis: blind spots, correlated failures. |
| `impact_forecast` | Forecast the performance impact and downstream blast radius of cells. |
| `suggest_next_action` | Prioritized "what next" queue combining coverage, impact, and staleness. |
| `suggest_next_cell` | Type-directed suggestions for what to evaluate next, from the bindings in scope. |
| `suggest_repair` | Given a failing test, trace causal changes and suggest the symbol to fix. |
| `plan_ripple` | Plan cascade re-evaluation for changed cells using the live dependency graph. |
| `preview_what_if` | Preview what would change if a binding had a different value, without executing. |
| `decompose_pipeline` | Break an F# pipeline into stages, each classified pure / effectful / unknown. |
| `get_cell_dependencies` | The cell dependency graph with staleness annotations. |
| `discover_features` | Context-aware feature discovery, ranked by relevance to the session state. |

### Export and history

| Tool | What it does |
|:---|:---|
| `export_notebook` | Export the session as a notebook-style `.fsx` with cell metadata. |
| `export_session_transcript` | Export the session as a clean, topologically sorted `.fsx` transcript. |
| `get_session_filmstrip` | Visual history of evaluations: each a frame with code, bindings, and duration. |
| `get_eval_timeline` | Eval-duration sparkline and P50/P95/P99 statistics. |
| `get_eval_diff` | Before/after diff of recent evaluation outputs. |
| `get_message_journal` | Audit log of eval events, filterable by severity and source. |
| `manage_scratch_pad` | View, export, or promote ephemeral snippets from session history. |

</details>
