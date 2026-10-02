# Using Bozzetto with AI agents

Use Bozzetto's `composer_*` tools for Clef/Composer development. The tools and the human-facing Composer page share one daemon-owned supervisor, so both observe the same session, compiler epoch, revision and accepted-artifact evidence.

The [October 1 host transition](Bozzetto_Clefx_Host_Transition_2026-10-01.md) removes embedded production FSI hosting from the checkout. Bozzetto on `47749`/`47750` is the only daemon surface; no separate F# REPL service is part of any workflow, and Clefx/ORC remains future work. Check the installed release separately from source status.

## Connect to the shared provider

Use the reviewed installed `boz` and follow the [live provider checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md), including its linked deployment corrections. From this checkout, `scripts/start-shared-daemon` preserves an existing listener or launches the daemon in its dedicated external workspace. Keep that shared daemon independent of any individual client's lifetime.

Configure your MCP client to use Streamable HTTP at `http://127.0.0.1:47749/`. The [MCP reference](mcp-tools.md#connect) and [checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md#auditor-connection-prerequisites) cover client configuration. Use a timeout suitable for native builds; the checkpoint uses 600 seconds for tool calls.

Confirm that the connected client exposes `get_daemon_status`, `composer_list_sessions` and `composer_open_project`. A server entry in a config file is not evidence that tools are available. The human view is `http://127.0.0.1:47749/composer`; `composer://sessions` exposes the same session directory as an MCP resource.

## The Composer loop

1. Read `composer_list_sessions` and open your explicit absolute `.fidproj` path with `composer_open_project`.
2. Retain the returned host, session and compiler epoch. Pass those identities to subsequent session operations.
3. Call `composer_reserve_edit` before changing source or dependency files. Receive a successful response before writing, and keep its opaque single-use reservation.
4. Make the edit and pass that reservation to `composer_build`.
5. Inspect the compiler result and run through `composer_run_current`. Composer revalidates inputs and executable bytes before execution.
6. Read `composer_session_status` for shared state, diagnostics and cleanup status. Close your session with `composer_close_session` when finished.

An edit reservation withdraws the old artifact's run authority. Human reservations and session cancellation affect agents too. A status response or cached artifact path does not grant execution permission; all runs go through `composer_run_current`.

Before a coordinated compiler-distribution replacement, `composer_retire_worker` retires every session in that worker. Complete cleanup must succeed before a fresh epoch can serve work. Compiler checkout changes alone do not change the deployed worker, and in-place patching of an active compiler is unsupported.

Today's backend runs accepted native binaries. LLVM ORC JIT and automatic editor save/build integration remain planned; do not assume that an editor save builds a Composer revision.

## Repository guidance for agents

A project can include this in its `AGENTS.md`:

```markdown
## Clef development with Bozzetto

- Connect to the shared Bozzetto MCP provider at http://127.0.0.1:47749/.
- Open an explicit absolute .fidproj with composer_open_project and retain
  the returned host, session and compiler epoch.
- Reserve with composer_reserve_edit and receive success before writing
  source or dependencies. Build with its single-use reservation.
- Execute only through composer_run_current; status and artifact paths
  do not authorize direct execution.
- Preserve other users' sessions and the shared daemon. Coordinate worker
  retirement before changing the installed compiler distribution.
```

## F#/.NET implementation work

Bozzetto no longer hosts F# sessions or evaluates F# configuration, and no separate F# REPL service is part of its workflow. The retained F# tools in its MCP catalog are inherited host code, not a product surface; creation, resume and rebuild requests refuse. A Composer project does not need an FSI session.

Contributors changing Bozzetto's retained F# implementation must read the repository's [agent guidelines](../AGENTS.md) and [F# implementation skill](../skills/bozzetto/SKILL.md). Validate those changes with `dotnet build` and the unfiltered test suite, started only after the matching `acquire_full_build_lease` or `acquire_test_suite_lease` and ended with `release_work_lease`; a filtered test run is never the acceptance check. Report exact tool errors and self-hosting version skew instead of shelling around the daemon's lease accounting. That guidance applies to F# implementation work, while Clef projects use the Composer loop above.
