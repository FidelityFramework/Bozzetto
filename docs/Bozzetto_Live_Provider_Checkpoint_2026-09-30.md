# Bozzetto shared provider checkpoint — 2026-09-30

**Recorded live checkpoint; independent read-only assessment received.**
See the [auditor assessment](Bozzetto_Live_Provider_Auditor_Assessment_2026-09-30.md)
and [correction response](Bozzetto_Live_Provider_Audit_Response_2026-09-30.md) for
subsequent status/launcher corrections and their deployment identity. The earlier
[provider repair checkpoint](Bozzetto_Provider_Repair_Checkpoint_2026-09-30.md)
records 24/24 passing native/unit cases against Composer's approved distribution.
The [Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md) records the H1/H2
import corrections. The daemon, MCP and browser are running from the recorded deployment below.
The auditor independently confirmed evidence and shared MCP visibility; the
auditor has not repeated the mutating native workflow.

## Architecture delivered for the live gate

One daemon-owned Composer supervisor supplies both MCP and the browser/API. Its
worker protocol carries explicit host, session, provider, epoch and revision.
The daemon never reconstructs a Composer reservation or launches a cached artifact
directly. Nine `composer_*` tools and the `composer://sessions` resource expose the
same operations and state as `/composer` and `/api/composer/*`.

Clef sessions are opened explicitly with a `.fidproj`; they do not pass through
FSI. F# changes to Bozzetto's own code are validated with `dotnet build` and the
unfiltered test suite under work leases. Bozzetto retains inherited F# code, but this
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
- Health: `http://127.0.0.1:47749/health`
- Daemon identity: `http://127.0.0.1:47750/api/daemon-info`
- Shared Composer state: `http://127.0.0.1:47749/api/composer/sessions`

Use bounded probes, for example:

```sh
curl --fail --max-time 3 http://127.0.0.1:47750/api/daemon-info
curl --fail --max-time 3 http://127.0.0.1:47749/health
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
/home/hhh/repos/Bozzetto/scripts/start-shared-daemon
```

The helper preserves existing listeners. That path reports
`readinessChecked=false`: exit zero means both ports and a parseable identity
were found, not that health or Composer readiness was checked. Run the separate
bounded health and session probes above. On a fresh start it uses the installed
`boz`, launches detached from `$XDG_DATA_HOME/bozzetto/workspace` (default
`~/.local/share/bozzetto/workspace`), writes logs/PID under external state storage,
and probes identity, health and Composer configuration within a bounded readiness
loop (20-second loop deadline, three-second per-request timeouts). Requests
already in progress may finish after the loop deadline; this is not a strict
20-second wall-clock cap. Workspace/log/PID
symlinks are refused, so a redirected child cannot recreate the home scan or
write artifacts into a repository.
It never waits for the daemon's lifetime or changes operating-system limits.

The first shared launch (PID 653222) used home as its working directory. The
inherited recursive watcher exhausted the inotify watch limit while scanning
home. That operational error is preserved under `home-watcher-finding/` and in
the original browser evidence. It was corrected by gracefully stopping that
owned daemon and launching PID **669866** at **2026-09-30T15:05:00Z** from the dedicated
external workspace. Identity and health probes now report no component failures;
the browser completed native build/run again on the replacement daemon. Do not
launch a shared daemon from home or the repositories parent. The daemon persists
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

Bozzetto on ports 47749/47750 is the only daemon and MCP source for this review;
do not register a second MCP source for F#/.NET work. F# changes to Bozzetto's
own code are validated with `dotnet build` and the unfiltered test suite under
work leases. This checkpoint does not alter the user's global MCP config.

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
old-session operations refuse and reopening yields a new epoch. No F#/.NET hosting is part of this review;
the Bozzetto daemon on ports 47749/47750 is its only MCP connection.
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
- Final unfiltered default gate: **9,805 accounted, 9,801 passed, four existing
  ignores, zero failures/errors; Trusted**. The test-only Release rebuild had
  zero warnings/errors. Both build and test ran under granted MCP work leases,
  released afterward; all production assembly/dependency hashes were unchanged.
- Earlier default failures are preserved. The first run exposed two stale MCP
  catalog assertions; they now include the nine Composer tools with strict
  equality. The next run exposed a provider-test publication race (corrected
  with a bounded barrier before recovery) and a pre-existing health-anomaly
  property failure. The latter reproduces against byte-identical baseline
  `1bb1e7c` source: noisy inputs can jump directly to `Broken` after the detector's
  sustain gate, contrary to that property's ordering assumption. No health
  detector or threshold was changed. Its concrete baseline reproducer remains
  under `health-anomaly-baseline/`; a passing final run does not repair that
  existing flaky property.
- Harmony H1/H2: 12 provenance and four path checks passed. Current vendored
  package validation also passes, with package and manifest bytes unchanged.
- Real Chromium browser check on the installed replacement daemon: explicit
  project open, reserve, native build and run passed; output `stable` / `before`,
  exit zero. Browser and shared API retained identical authority and accepted
  artifact digest. No page errors; the only console warning was a favicon 404.
  The accepted external fixture session remains open for inspection:
  `bdb2233db4244816aa1e5a9080e1804f`, epoch
  `5cf5024f134b4c4dbe2cb246eb646ac4`, Composer generation 1.

### Health and memory interpretation

The final daemon reports `healthy=true` with no component failures. Its inherited
health endpoint counts F# sessions; use `/api/composer/sessions` or
`composer_list_sessions` for Composer session state. A zero legacy session count
does not mean the accepted Composer session is absent.

Machine memory is sampled from Linux `/proc/meminfo` (`MemAvailable`, including
reclaimable cache) on the daemon's five-second sweep, and also at F# session
admission. Work leases read that shared pressure state; it is not frozen at
startup. The inherited thresholds enter `tight` at 20% available and exit at 30%;
`critical` enters at 8% and exits at 15%. This hysteresis avoids threshold flapping.
On this 58.5 GiB machine, about 10 GiB available still means `tight`; returning
to `normal` requires about 17.5 GiB. The user's freed RAM is included in fresh
samples. `overall=Degraded` with no component failures can therefore reflect
memory pressure alone. No threshold, OS setting or admission guard was disabled. The installed Core
assembly thresholds and repeated kernel/HTTP samples are recorded under
`memory-live/`; these samples did not cross a recovery threshold, so this is not
a claim that a forced pressure-transition experiment was run.

All evidence is outside repositories at
`/home/hhh/.local/state/bozzetto/checkpoints/2026-09-30-live-provider/`.
The native gate lives under `attempt2/`, the final default gate under
`default-repaired/`, and final browser validation under `browser-final/`.
`default-final/` and `browser/` retain the earlier default run and initial browser check. The earlier failed
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

Each gate has `before/` and `after/` closure manifests. The native test-runner
closure contains 850 files; later default-runner closures contain 849 following
the test-project rebuild. These cover all runner dependencies, not just
`Bozzetto.Tests.dll`. Each executed runner is also
preserved as a complete `test-runner-closure/` copy. The native gate's runner
absolute-path manifest `attempt2/before/test-runner.sha256` (identical to its
`after/` counterpart) has SHA256
`f4da6c3cbe6e73f745f1e42cf6484dabf7e0a4ae9ee518a1923c5abbf600f021`.
The copied runner's relative-path `attempt2/test-runner-closure.sha256` instead
hashes to `c67073854bb906d0f4749d2b891f3c7798116b0013968f55c6de77d7c45775f7`;
the different path spelling does not represent binary drift.
After native acceptance, test catalog expectations and a provider-test
synchronization barrier changed; the daemon and worker closures stayed byte-identical.
The barrier waits for the deliberately failed withdrawal to publish its error
before the test starts recovery, removing a race in the assertion itself. The final default runner
has its own identity, so no later test binary is substituted for the native log.
Its relative closure-manifest SHA256 is
`33ef0d61a96a8a0297f53ee36b1859b69003aa67a4a13a930c7bfee941787c2d`.

The final default runner was built from user checkpoint commit
`5e37875ae41aedcdb104aa382985fc52686e39dd`. Subsequent changes in this checkpoint
are deployment/agent guidance and the detached startup helper, not compiler or
daemon implementation changes. `source-checkpoint/` retains the HEAD archive,
working-tree patch, untracked helper, source hashes and change inventory. The
startup helper's existing-listener behavior and workspace/log/PID symlink
refusals were checked without disrupting the shared daemon.

Standalone Composer MCP, ORC JIT execution, automatic editor save/build integration
and compiler-stage progress streaming remain planned. Accepted metadata is always
revalidated by Composer before execution. Human and agent views share state, but
status and completed-operation notifications are not a compiler progress stream.
The independent read-only assessment supports the evidence and visibility; an
auditor-owned mutating workflow repeat remains outstanding. Full host/browser/mutation
and upstream Harmony suites were not rerun for this checkpoint; the explicit
Composer/browser journey above is the executed live acceptance scope.
