# Bozzetto shared provider checkpoint — 2026-09-30

**In progress: final shared-interface acceptance is running.** The earlier
[provider repair checkpoint](Bozzetto_Provider_Repair_Checkpoint_2026-09-30.md)
records 24/24 passing native/unit cases against Composer's approved distribution.
The [Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md) records the H1/H2
import corrections. This document will record the final deployed closure and live
results before handoff; do not infer completion from the presence of this file.

## Architecture delivered for the live gate

One daemon-owned Composer supervisor supplies both MCP and the browser/API. Its
worker protocol carries explicit host, session, provider, epoch and revision.
The daemon never reconstructs a Composer reservation or launches a cached artifact
directly. Nine `composer_*` tools and the `composer://sessions` resource expose the
same operations and state as `/composer` and `/api/composer/*`.

Clef sessions are opened explicitly with a `.fidproj`; they do not pass through
FSI. The intended F#/.NET workflow uses a separate SageFS daemon and MCP source
(default MCP 37749, dashboard 37750). Bozzetto retains inherited F# code, but this
checkpoint does not expand or require its F# REPL. Composer’s incremental sessions
are the foundation for a future Clef REPL backed by LLVM ORC JIT. Human reservations, cancellation
and retirement withdraw the authority visible to agents. Cancellation of a single
MCP request targets that worker request's identifier, never an unrelated newer
operation. Retirement fences every session and observes owned-process exit before
allowing a replacement epoch.

This delivers the shared daemon interface milestone. A standalone Composer MCP
host and editor save/build integration remain separate work in the
[development plan](Clef_Composer_Development_Plan.md).

## Auditor connection prerequisites

A checkout, compiled DLL, installed CLI and connected MCP server are four different
states. The earlier auditor had neither a running daemon on port 47750 nor
Bozzetto MCP tools in its session. That was a deployment/connection gap, not a
failed MCP call against the inherited implementation.

The intended local endpoints are:

- Dashboard: `http://127.0.0.1:47750/`
- Shared Composer page: `http://127.0.0.1:47749/composer` (also linked from the dashboard)
- Streamable HTTP MCP: `http://127.0.0.1:47749/`
- Daemon identity: `http://127.0.0.1:47750/api/daemon-info`
- Shared Composer state: `http://127.0.0.1:47749/api/composer/sessions`

Use bounded probes, for example:

```sh
curl --fail --max-time 3 http://127.0.0.1:47750/api/daemon-info
curl --fail --max-time 3 http://127.0.0.1:47749/api/composer/sessions
```

The reviewed launcher is `/home/hhh/.local/bin/boz`. It pins the daemon and worker
under `/home/hhh/.local/share/bozzetto/releases/2026-09-30-141a32481cec-b269cdb888a6/`
and the matching SDK/runtime under
`/home/hhh/.local/share/bozzetto/runtimes/11.0.100-rc.1.26425.128/`.
It exports `BOZZETTO_COMPOSER_WORKER`, `DOTNET_HOST_PATH`, `DOTNET_ROOT` and the
matching SDK path. Neither runtime deployment depends on repository build outputs
or disposable validation caches. Do not substitute a public NuGet package or
reuse a changing compiler build tree.

If the identity probe refuses connection and neither Bozzetto port is occupied,
start the shared daemon once:

```sh
mkdir -p "$HOME/.local/state/bozzetto"
nohup /home/hhh/.local/bin/boz --no-resume \
  >"$HOME/.local/state/bozzetto/daemon.log" 2>&1 </dev/null &
curl --fail --max-time 3 http://127.0.0.1:47750/api/daemon-info
```

Startup is asynchronous; an initial refused probe may precede readiness. Use a
bounded follow-up probe, not a wait for the daemon to exit. The daemon persists
until explicitly stopped. Use the HTTP MCP connection below for shared access;
starting it independently avoids making its lifetime depend on one client's
stdio bridge. CLI `--help` still describes the inherited F# command surface;
Composer operations are the `composer_*` MCP tools and the Composer browser page.
Do not stop a daemon serving another agent merely to perform the audit.

For a local Codex CLI/IDE auditor, add the HTTP connection while the shared daemon
is running:

```sh
codex mcp add bozzetto --url http://127.0.0.1:47749/
codex mcp list
```

Set an adequate native-build timeout in the existing server table in
`~/.codex/config.toml` (do not create a duplicate table):

```toml
[mcp_servers.bozzetto]
url = "http://127.0.0.1:47749/"
startup_timeout_sec = 30
tool_timeout_sec = 600
```

Reconnect/restart the agent client as appropriate and confirm that it actually
exposes `get_daemon_status`, `composer_list_sessions` and `composer_open_project`.
A configuration listing is not proof of a live tool connection. These CLI/config
instructions follow the [official MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).
A hosted ChatGPT Work client does not consume local Codex config files; its MCP
connection must be provided by that client's environment/plugin mechanism.

## Auditor workflow

Use an external copy of the real scalar fixture, preserving source projects.
Open it with `composer_open_project`; retain the returned authority. Reserve an
edit before writing source, build that reservation, and run through
`composer_run_current`. Compare the browser/resource state with the exact MCP
host/session/epoch/revision and accepted source/artifact hashes. Exercise a human
reservation and confirm that the agent no longer sees a runnable current artifact.

Use `composer_retire_worker` before changing compiler binaries; then confirm
old-session operations refuse and reopening yields a new epoch. Use the separate SageFS MCP connection if the review needs F#/.NET hosting.
Its endpoint and lifecycle are independent of Bozzetto.
The independent assessment belongs in a new document under `docs/`; preserve prior
assessments and evidence.

## MCP cancellation distinction

Canceling an HTTP response and canceling MCP work are different operations. The
shipped .NET MCP client SDK (`1.0.0-rc.1`) aborts an in-flight Streamable HTTP
`CallToolAsync` when its caller token is canceled without sending
`notifications/cancelled`. An isolated probe against the exact NuGet DLLs captured
that behavior: the server's tool token stayed active. Sending the explicit MCP
notification for that same request ID canceled the server token.

The live acceptance test therefore sends and records an explicit
`notifications/cancelled` targeting its build request, proves shared authority is
withdrawn, and only then closes the abandoned HTTP response. The daemon translates
that cancellation into the worker's exact-request cancellation, preserving other
requests. `composer_cancel` is a separate, explicit session-wide revocation.
A transient transport disconnect is not silently promoted into session cancellation.

The failed first gate and the discriminating SDK probe remain recorded under the
external live checkpoint's `attempt1/` and `mcp-cancellation-probe/` directories.
That attempt accounted for all 29 cases: 28 passed, one live cancellation assertion
errored, `TestsFailed`. The final gate below supersedes that test assumption; it
does not erase the failed evidence or claim the SDK behavior was changed.

## Executed acceptance and identities

- Full Release solution build: zero warnings/errors. Optional Composer worker
  build against the approved distribution: zero warnings/errors.
- Full `--integration-composer` tier: **29 registered, 29 passed, no ignores,
  failures or errors; Trusted**. It includes 20 session authority cases, four
  deterministic worker lifetime cases, four process/native cases, and the real
  shared MCP/HTTP journey. The journey takes both interfaces through native
  build/run, human edit invalidation, exact-request MCP cancellation, recovery,
  independent projects, resource/SSE notifications, reconnect, worker retirement
  and a fresh epoch. This is executed evidence, not source-review approval.
- Default gate: final catalog-corrected rerun pending. Its first run accounted for
  9,805 cases, with 9,799 passing, four existing ignores and two stale catalog
  assertions failing. The assertions now include the nine Composer tools while
  retaining strict equality; both entire affected lists passed (15/15).
- Harmony H1/H2: 12 provenance and four path checks passed. Current vendored
  package validation also passes, with package and manifest bytes unchanged.

All evidence is outside repositories at
`/home/hhh/.local/state/bozzetto/checkpoints/2026-09-30-live-provider/`.
The native gate lives under `attempt2/`, the final default gate under
`default-final/`, and browser validation under `browser/`. The earlier failed
attempts remain intact. Native outputs, object receipts, lease evidence, 93 live
HTTP/MCP records and copies of eight provider artifact trees are retained.

| Deployed component | SHA256 |
|---|---|
| Bozzetto daemon (net11) | `141a32481cec326c6c4505fd90b865945d8f26846891946d833eee75bcd49696` |
| Bozzetto Composer worker (net10) | `b269cdb888a628b64be4465742adb816cb97f28e84c253e4f89220c00145a056` |
| Approved Composer DLL | `f2b2cedad8f753d279bef5bc6e6d69a9319227d076c27fe9a0108f99ec5f626b` |

`deployment.json` and the installed release's `closure.sha256` identify the full
application deployment. The SDK/runtime copy is pinned in
`runtime-installed.sha256`. Compiler provenance and the approved 40-file manifest
remain in Composer's
[lease-release response](/home/hhh/repos/Composer/docs/Bozzetto_Lease_Release_Response_2026-09-30.md).
The worker deployment contains 41 files, including its compiler dependencies.

Each gate has `before/` and `after/` closure manifests. The test-runner closure
contains 850 files, not just `Bozzetto.Tests.dll`. Each executed runner is also
preserved as a complete `test-runner-closure/` copy. The native gate's runner
manifest SHA256 is
`f4da6c3cbe6e73f745f1e42cf6484dabf7e0a4ae9ee518a1923c5abbf600f021`.
After native acceptance, only test catalog expectations and whitespace changed;
the daemon and worker closures stayed byte-identical. The final default runner
has its own identity, so no later test binary is substituted for the native log.

Standalone Composer MCP, ORC JIT execution, automatic editor save/build integration
and compiler-stage progress streaming remain planned. Accepted metadata is always
revalidated by Composer before execution. Human and agent views share state, but
status and completed-operation notifications are not a compiler progress stream.
Independent auditor approval remains outstanding.
