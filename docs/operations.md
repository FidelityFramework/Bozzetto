# Operations

Bozzetto operates one development controller for Clef/Composer work. Port 47749 serves MCP, HTTP and the browser pages; port 47750 serves control. Composer is a differential compiler. The interim host targets `net10.0`; embedded F# session hosting is retired. Native hosting, LLVM ORC execution and distributed lab-node operation are requirements or planned capabilities, not operational delivery claims here.

The governing lifecycle, lease and policy rules are in `AGENTS.md`. This document consolidates operational guidance without granting new scheduling or termination authority. See [architecture.md](architecture.md) for process ownership and [development.md](development.md) for validation and promotion.

## Operational authorities and delivery boundaries

| Centerpiece | Operational responsibility | Boundary |
| --- | --- | --- |
| Fidelity.PSG | Carries compiler-owned evidence and identity through the selected compiler closure. | Health, available memory and a lease do not establish proof validity or execution permission. |
| Fidelity.FSharp.Incremental | Owns declared dependency bookkeeping, shared demand, withdrawal and explicitly started work lifetimes. | Resource observations must integrate through its publication boundary, not a parallel invalidation engine. |
| Ranvier | Supplies the signal/memo/effect reference shape and one-mutation-domain discipline for reactive presentation. | It is not compiler proof authority, the foundation's declared-read protocol or evidence of physical drain. |

The following evidence distinctions matter when operating or promoting a release:

- **Code-backed today:** `tools/build/WorkLease.fs` implements request, heartbeat renewal, release and withdrawal for `ci-pipeline.fsx` and `tools/build/lease.fsx`; `src/Bozzetto.Core/ExpensiveWorkLease.fs` implements queue, renewal and reclamation rules.
- **Code-backed today:** `src/Bozzetto/HealthTick.fs` sequences immutable health observations using a lock and an observable event. It does not itself demonstrate Fidelity.FSharp.Incremental adoption or a delivered Ranvier engine.
- **Code-backed protocol support:** `src/Bozzetto.Composer.Protocol/StreamFrames.fs` encodes typed BAREWire request/reply frames and rejects malformed framing. This establishes framing support, not acceptance of every deployed transport seam.
- **Requirements still needing acceptance:** shared whole-host/device acquisition, UMA-aware attribution, calibrated findings, native/platform parity and approved recovery. A design document or a healthy listener cannot prove these gates passed.

Direct daemon-worker, daemon-browser and agent-owned interchange must be typed binary. JSON is not a direct API contract, even if serialized with Fidelity.Data. MCP's external JSON-RPC standard is the sole external exception. Fidelity.Data JSON belongs only at export edges, including an independent MailboxProcessor-driven diagnostic sink process. See [mcp-tools.md](mcp-tools.md), [binary-format-spec.md](binary-format-spec.md) and [Bozzetto_Diagnostic_Sink.md](Bozzetto_Diagnostic_Sink.md).

There is current wire drift: `tools/build/WorkLease.fs` sends and parses JSON HTTP bodies. These are inspected implementation facts to remove, not a second permitted interchange contract. Do not copy their request shapes into new integrations or substitute a serializer and call the migration complete. Browser binary transport and reconnect acceptance require separate evidence in [reactive-push.md](reactive-push.md).

## Installed identity and bounded readiness

A checkout is not the running release. Record the reviewed installed launcher, resolved release path, daemon PID/start identity, worker identity and selected compiler closure before diagnosing version skew or claiming delivery. A shared version number alone cannot distinguish builds with different bytes.

Probe first and leave a running daemon in place. Otherwise launch the installed `boz` detached from the dedicated workspace, then probe once:

```bash
ws=${XDG_DATA_HOME:-$HOME/.local/share}/bozzetto/workspace
st=${XDG_STATE_HOME:-$HOME/.local/state}/bozzetto
mkdir -p "$ws" "$st" && (cd "$ws" && setsid boz --no-resume </dev/null >>"$st/daemon.log" 2>&1 &)
curl -s -m 3 http://127.0.0.1:47749/health
```

The probe is the next action after startup: one bounded probe, then act on its result. Do not put the daemon in the foreground, wait for its lifetime or add an outer readiness retry loop.

The command runs the installed `boz` with `--no-resume` from `$XDG_DATA_HOME/bozzetto/workspace` (default `~/.local/share/bozzetto/workspace`) and appends to `daemon.log` under `$XDG_STATE_HOME/bozzetto` (default `~/.local/state/bozzetto`). Keep both roots outside the repositories folder. After the probe, check control identity at `http://127.0.0.1:47750/api/daemon-info` and `/api/composer/sessions`, each once: the listener's PID must be the launched process, health must report no component failures, and Composer must be configured.

If either port is already occupied, leave the listener in place and check its identity, health and Composer state before using it. Do not kill an unidentified port occupant or launch another daemon to evade the conflict.

A health response proves only the facts it reports at their observation times. Composer configured is not proof that a project is open, its artifacts are current or native execution is admitted. Verify session/work state through the Composer surface and [composer-workflow.md](composer-workflow.md). A successful MCP initialization establishes a connection, not a compiler lease or project authority. These are the active connection checks intended to replace the dated live-provider checkpoint navigation; integration must update the still-governing `AGENTS.md` reference without deleting historical receipts.

## Matching deployment and rollback

Development restarts are authorized by `AGENTS.md`; asking for permission for every ordinary restart is unnecessary. That authorization does not permit interrupting another caller's active work or deploying unrelated compiler changes.

Use this sequence:

1. Identify the changed owner and validate it with its consumers. Retain the integration TRUST ledger and exact source/package identities; follow [development.md](development.md).
2. Inspect active and queued leases, Composer operations and pending cleanup. Arrange an owner-directed drain without abandoning another caller's work. A queue entry is not a running process, and an empty queue is not proof of physical drain.
3. Preserve the previous installed release and its matching worker/compiler closure. Record the intended new launcher, daemon, worker, protocol, PSG and foundation identities before switching.
4. **Current rule:** replace the installed daemon and worker as a matching pair from one selected closure through the reviewed release procedure. Verify the PSG contract fingerprint and a Composer open against the serving pair (`src/Bozzetto/ComposerWorkerClient.fs`, `src/Bozzetto.Composer/Worker.fs`). Do not deploy compiler/worker changes merely because the Bozzetto checkout was rebuilt. **Required D1 destination:** compatible worker-only replacement, with wire compatibility checked separately from manifest-bound worker distribution identity; this is not today's operating path. See [component contracts](Bozzetto_Fidelity_Component_Contracts.md#wire-compatibility-and-distribution-identity).
5. Start the installed pair from the dedicated workspace as shown above, then perform the bounded health probe. Verify the actual process identity, worker epoch and selected closure, not just the files in the checkout.
6. Reconnect clients and verify served browser bytes and authoritative observations. Establish a fresh baseline for the new incarnation; do not present the predecessor's resource samples as current.
7. If readiness or compatibility fails, report the exact failure and restore the previous matching release after accounting for new work. Repeat identity/readiness checks and reconnect reconciliation. Keep the failed release's logs and validation receipts.

The protocol project targets `net10.0` and imports `tools/build/ComposerContract.props`. That build owner selects PSG and BAREWire together, using local projects or an absolute `ComposerDistribution` containing both assemblies. It checks required files, not deployment provenance or live assembly equality. Confirm the selected closure and actual loaded identities separately.

Daemon wire compatibility and worker-loaded PSG/distribution identity are distinct checks. Do not solve a worker closure mismatch by ignoring its refusal or loading an arbitrary assembly that happens to satisfy a type name. Preserve compiler-owned authorization and epoch fencing during replacement. See [Bozzetto_Fidelity_Component_Contracts.md](Bozzetto_Fidelity_Component_Contracts.md).

Rollback restores executable compatibility, not authority from the previous epoch. Outstanding acknowledgements, demands and artifacts must be reconciled with their owners; an old process or browser snapshot cannot revive revoked permission. A second daemon is allowed only for testing daemon code the running instance cannot execute, with an explicit owner and time limit.

## Work view and recent history

The Work page keeps controller compiler sessions and the compiler worker above
the collapsible node listing. Each node reports its actual open compiler
projects, worker state, active leases and queue. A leased command's project
directory is recent activity; it does not create a compiler session.

Completed operations start collapsed, with individually expandable summaries.
The browser shows at most 20 completed entries per node within the last 24 hours.
Retire controls hide entries in that browser only, with a restore control;
they do not delete the node's SQLite evidence. Running work and current
sessions or leases do not age out of the view. Historical search is not provided.

The observation projection retains all current operations and at most 20
completed summaries, shedding the oldest completed summaries further if needed
to fit the existing encoded byte budget. Source journal retention is unchanged.
If current work alone exceeds that budget, the existing explicit oversize
refusal remains; availability is never fabricated by hiding active work.

## Leases: admission, heartbeat and queues

Every caller-owned build or test suite requires a lease. The ordinary client is:

```bash
dotnet fsi tools/build/lease.fsx full_build dotnet build
dotnet fsi tools/build/lease.fsx test_suite_run dotnet <testDll> --summary
```

Run the built test DLL locally; `dotnet test` is for CI. MCP callers use the acquire/release tools described in [mcp-tools.md](mcp-tools.md). Renewal and withdrawal are not exposed as MCP tools; that connection-bound lifecycle gap must not be mistaken for HTTP holder compatibility. Do not nest a second lease under a held one or launch work before a grant.

| Decision | Required caller action |
| --- | --- |
| Granted | Start the identified work, renew while it lives and release on every terminal path. |
| Wait | Do not start. Read the reason and retry-after. Retain the same holder/kind if continuing; withdraw if giving up. Investigate memory pressure before retrying. |
| Refused | Do not start or blindly retry. Resolve the specific refusal at its owner. |

`src/Bozzetto.Core/ExpensiveWorkLease.fs` owns the current pool rules:

- Admission capacity is four active leases at Normal pressure, one at Tight and zero new grants at Critical. A pressure rise does not revoke already granted work.
- A holder may have one outstanding claim. A held lease or queued request for another kind causes refusal, not a second queued claim.
- Queue order uses a monotonic arrival sequence. Retrying the same holder/kind retains its place and refreshes `LastAskedAt`.
- Unretried queued asks age out after two minutes. Explicit withdrawal removes only that holder/kind's queued ask, not an active lease.
- Full-build lifetime is ten minutes; test-suite lifetime is fifteen minutes. Renewal extends a still-live lease from the renewal time by its kind's lifetime. Unknown, expired or differently owned leases are not resurrected.
- Reclamation occurs on pool operations, including requests, renewal and observation. The pure pool has no reclamation timer of its own.
- Observer grant views omit the lease ID because that ID is a release capability. MCP release ownership is supported by `releaseOwned`; possession of a display row is not a release authorization.

A lease lifetime bounds abandonment, not the duration of a healthy renewing run. `tools/build/lease.fsx` derives a stable holder from `BOZZETTO_AGENT` and `BOZZETTO_PROJECT` (default project: cwd), renews on a heartbeat and releases during cleanup. Set distinct agent labels: these are caller-reported context, not authenticated identity or test proof. MCP identity instead belongs to the connection.

`tools/build/lease.fsx` keeps a deferred ask in line, retrying it as the same holder and kind every 15 seconds, and withdraws it after ten minutes; it exits 69 when the lease is refused or the ask is withdrawn. An ask nobody retries within two minutes is reaped. Do not create fresh holder suffixes for each attempt. The client uses `/api/lease/request`, `/api/lease/renew`, `/api/lease/withdraw` and `/api/lease/release`; these holders are not the MCP connection binding.

If renewal reports the lease gone, the wrapper requests termination of its direct child. If renewal is unreachable it logs and retries; if the route is absent it warns that the old daemon leaves the TTL in charge. Neither case demonstrates current coordination. Fix installed skew rather than treating that compatibility behavior as the operating contract. Wrapper termination and release do not establish that all descendants or device work have physically drained.

`AGENTS.md` also calls for a newer ask from the same scope to supersede an older ask. The inspected core instead refuses a queued ask for a different kind. Do not claim cross-kind supersession is delivered by this module. Resolve or withdraw the old ask before changing kind; any adapter-level supersession needs its own code and acceptance evidence.

Caller-owned builds and tests run through `tools/build/lease.fsx`, which reports the actual project, command, PID and start under its live lease, then settles the same durable SQLite record with exit status and output before releasing admission. The new reporting routes use the shared typed binary command-observation contract; the older admission JSON drift remains separate. Work shows these observations per node alongside actual compiler sessions and worker state.

Granted and queued Clef/Composer build/test leases belong on `/work` beside compiler work. `/dashboard` keeps agents and machine resources. A lease records admission, not measured CPU/memory attribution, compiler proof or process-kill authority.

## Reading resource observations correctly

Keep four answers separate:

| Dimension | Evidence to inspect |
| --- | --- |
| Service health | Responsiveness, component failures and collector errors. |
| Utilization | Occupied/busy resources with units, scope, denominator and measurement interval. |
| Pressure/interference | Stalls, paging rates, allocation failures, queue delay and measured workload regression. |
| Admission headroom | Lease decision, current observations, effective limits and workload envelope. |

Low available RAM can reduce admission headroom without proving thrashing or a leak. A healthy daemon can coexist with sustained host pressure. Missing telemetry is unavailable, not healthy zero usage.

`src/Bozzetto/DaemonTelemetry.fs` samples an explicit process list. Its owned-process helper includes the daemon and Composer worker, with a retained parameter for retired session workers. It does not discover leased builds, tests, editor servers or inference workloads. The dashboard CPU figure therefore covers the daemon-owned tree only, not total workstation activity.

The current CPU calculation divides processor-time delta by elapsed wall time and `Environment.ProcessorCount`. It is host-count-normalized, not the process-equivalent convention where 100% means one logical CPU. Its first sample returns zero; inaccessible processes are omitted; prior samples are keyed by PID. These are limitations to correct under the richer observation contract, not evidence of idle external work or incarnation-safe attribution.

`src/Bozzetto/HealthTick.fs` publishes a serial, machine sample time and separate record time. Publications can be announced out of order outside its lock; consumers must respect the serial. A failed record refresh preserves the old record/time. Advancing uptime or a connected socket does not make those retained observations fresh. Its request-driven sampling is not a delivered whole-host acquisition service.

`src/Bozzetto.Core/MemorySupervisor.fs` uses shared memory-pressure hysteresis: Tight entry/exit at 20%/30% available, Critical entry/exit at 8%/15%. These are policy thresholds, not kernel pressure facts. The module is pure and returns actions; it does not execute them. Its retained idle-session shedding model must not be presented as a current F# session workflow or authorization to terminate unrelated processes. A zero total is treated as full availability by this module, another reason not to label unknown telemetry as measured headroom.

## UMA accounting and shared acquisition requirements

The resource observation contract must distinguish physical backing domains from device access. UMA does not create an extra pool of RAM. Firmware reservations, OS-managed memory, device-local memory, shared allocations, driver budgets and process/container limits need separate semantics.

Never sum process RSS, GPU shared-memory usage and host-used memory into a physical total. Host counters provide the capacity view; process/device counters provide overlapping attribution views. PSS is proportional attribution, not exact ownership or a guaranteed GPU-inclusive total. Unknown overlap stays unknown, not a guessed residual called "other".

Report available memory separately from free memory, reclaimable cache, pinned/wired allocations and compression where supported. Swap occupancy is not a paging rate or proof of present thrashing. AMD VRAM/GTT labels, Windows shared/dedicated segments and Apple Metal allocations require topology and backing qualifications, not assumed equivalence.

UMA workloads can compete for bandwidth, cache, power or thermal headroom without exhausting capacity. GPU busy percent alone does not establish bandwidth saturation. Expected model residency still consumes physical capacity; it cannot be subtracted because the owner intended it.

The required portable observation envelope includes host/boot and collector identities, sequence, acquisition times/skew, monotonic delta interval and coverage. Metrics need explicit units, source, scope, denominator, quality and reset markers. Processes need PID plus start identity; operations need explicit registration and lifetime observations. First samples, PID reuse, suspend/resume and counter regression invalidate deltas. Missing GPU support remains unavailable.

Acquisition must be shared and demand-scoped through Fidelity.FSharp.Incremental. Native notifications feed owner-held immutable inputs; derived observations declare complete reads. Counter-only metrics may use explicitly enabled bounded sampling windows with a demand owner, interval, duration and overhead budget. There is no default whole-host scan, subscriber timer, HTTP polling or silent polling fallback. Expensive proportional-memory and driver diagnostics require measured, bounded demand.

Closing one view withdraws only its demand. Acquisition needed by another subscriber remains live. An approved stability owner needs its own retained demand; browser closure must not disable required safety coverage. Source replacement and shutdown must join acquisition resources before release. These are acceptance requirements, not claims established by `HealthTick.fs`.

Ranvier-shaped push projects the same sequenced backend observations into Partas.Solid stores. The frontend renders and dispatches commands; it does not collect facts or recompute competing admission policy. Reconnect must atomically reconcile an incarnation/versioned snapshot before subsequent updates, with explicit gap, stale and unavailable states. See [reactive-push.md](reactive-push.md).

Equal display values cannot erase changed PSG proof identity. Effect cleanup cannot certify process/device drain. The foundation does not dynamically discover compiler premises. An Offer supplies dependency/lifecycle eligibility, not proof reuse, PSG publication or execution permission. See [Bozzetto_Incremental_Foundation_Adoption.md](Bozzetto_Incremental_Foundation_Adoption.md).

## Troubleshooting without bypassing ownership

| Symptom | Investigation and action |
| --- | --- |
| Startup fails or a port is occupied | Read launcher output, control identity and the state-directory log. Preserve unidentified listeners. Take the bounded health probe; do not start a competing daemon. |
| Type/assembly/version error | Capture the exact error, installed launcher/release, PID/start identity and worker/compiler closure. Compare actual loaded identities with the intended deployment; replace the matching pair after safe drain. |
| Health succeeds but Composer refuses work | Check explicit `.fidproj`, checkout/session routing, epoch and the specific refusal. Health is not project or execution authority. |
| Build/test is deferred | Read lease reason, capacity and queue state. Measure memory consumers before retrying; do not bypass admission. |
| A lease expires during a long run | Check renewal route, renewal outcomes, holder stability and daemon restart. Do not resurrect an expired token or trust work that continued without coordinated admission. |
| Dashboard CPU is low while the machine is busy | Inspect external builds, tests, editor servers and inference separately. The daemon-tree figure is not whole-host load. |
| Values remain old on a connected page | Inspect sample/record times, serials, incarnation and collector coverage. Reconcile at the owning push boundary; do not add a frontend fetch loop. |
| A retired F# request is refused | Accept the retirement boundary. Use Composer; do not recreate REPL, warmup, hard-reset or patching workflows. |

For memory investigation, use bounded measurements of available RAM/swap, process RSS with parents and cgroup usage. Useful manual probes include:

```bash
free -h
ps -eo pid,ppid,rss,pcpu,comm --sort=-rss
pgrep -af fsautocomplete
```

Check `fsautocomplete` before and after multi-repository builds. Distinguish each editor window's language server from the daemon and compiler workers. Capture evidence before any approved termination; growth alone does not distinguish cache retention from a leak. Diagnostic capture must itself fit available headroom. Do not expose command lines, process environments, prompts or credentials in published receipts.

Treat Lemonade as independently owned. Compare model-resident idle, build-only, inference-only and concurrent work using pinned configurations, leased tool runs and aligned observation windows. Separate queue delay, execution time, paging/stalls and inference throughput. Obtain owner approval before changing context, cache, concurrency or model residency. One reversible change needs repeated comparable trials, measured benefit, retained task utility and rollback configuration. "Memory is tight" alone is not a justification.

Keep the daemon local; do not widen its bind exposure to solve connectivity. Container/remote access needs reviewed forwarding and security boundaries, not unauthenticated remote execution. Broader distributed identity, authentication and fencing remain requirements in [architecture.md](architecture.md).

## Policy and recovery boundaries

Observation does not authorize destructive recovery. Current lease admission, daemon-owned supervision and compiler cancellation retain their existing owners. A build lease does not authorize killing its caller, editor, inference server or arbitrary descendants. Name, cwd and ancestry heuristics do not grant ownership.

Automatic resource recovery is a separate, unenabled capability until a versioned owner-approved policy and executed safety gates exist. Its requirements include:

- Eligible workload/incarnation and controllable group registration; explicit protection rules and action authority. Lemonade, desktop/editor and unrelated services are protected by default.
- Fresh trigger evidence, limits, dwell/deadlines and restart/action budgets. Low available percentage or suspected leakage alone is not a universal termination trigger.
- Race-safe target identity and execution-boundary revalidation. Unknown identity, stale evidence or an unproven group boundary must refuse unattended destructive action.
- Approved reversible relief before graceful cancellation, with forced escalation only when specifically authorized.
- Separate request, acknowledgement, exit and physical drain evidence. Process exit does not prove reclaimed GPU allocations or restored stability.
- Serialized recovery decisions, bounded audit receipts and restart reconciliation. Never replay an old kill blindly or broaden target eligibility after a failed action.

Where no permitted action restores headroom, report unresolved pressure and retain only authorized admission restrictions. Kernel OOM handling remains a separate authority. Do not silently elevate privileges, stop unowned processes or tune scheduling policy.

Linux visibility is the first delivery target. Windows UMA, Apple-silicon macOS, discrete-GPU controls and a native collector each require platform acceptance. Foundation-driven shared acquisition, calibration and leak-aware findings also need their own gates. Observe-only and policy dry-run results must stay distinct from executed recovery. Native portability and successful .NET tests do not prove self-hosting.

## Evidence and maintenance

Code statements here come from direct reads of the named Bozzetto checkout files, not live execution or a pinned retrieval result. No service, build, platform collector or recovery action was exercised for this rewrite. A fresh retrieval schema was unavailable through the read/write-only tooling; coverage and revision were not verified. This limitation does not establish an outside-index exemption or a deployed release identity.

Operational acceptance must record exact installed/loaded identities, readiness, matching closure, served browser bytes, observation freshness and rollback evidence. Preserve cited logs and TRUST ledgers outside the checkout. Retire obsolete installation, FSI warmup, runtime patching, inherited feature badges and JSON-body operating instructions from the active guidance, without deleting their retained audit evidence. The owner's export-only JSON ruling supersedes the broader serializer allowance in existing guidance.
