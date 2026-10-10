# Host resource observation and UMA-aware work coordination

October 10, 2026. **Design requirement; implementation and platform acceptance
remain separate.** Linux is the first delivery target. Windows on the same AMD
workstation and macOS on Apple silicon (including M3 and later) are required
architectural cases, not capabilities claimed by this document.

This is first-horizon operational work supporting FFI and Farscape development
with Clef, Composer, CCS, Baker, PSG, Alex and their component projects. It is
not gated on heterogeneous application execution or remote-node research.
Bozzetto is becoming a native system daemon. The inherited SageFS/F# session
and managed-process frame is not its resource model.

## Decision and current evidence

A developer must be able to answer: **what consumes this machine, which work is
competing, and what measured change would improve the combined workflow?**
A healthy daemon with low CPU usage cannot answer that question.

The [October 4 findings](Workstation_Findings_Ionide_And_Bozzetto_Telemetry_2026-10-04.md#bozzetto-what-the-dashboard-does-and-does-not-show)
record external builds and editor processes invisible to owned-process telemetry.
The October 10 page split separates presentations, but does not establish host
CPU/GPU coverage. Health/lease snapshots can remain old while uptime advances.
The Linux resource implementation is in progress; no Windows, macOS or native
collector acceptance is asserted here.

The existing policy can classify approximately 9 GiB available out of 58.5 GiB
as tight because it is below the 20% entry threshold. That explains a policy
verdict; it does not prove thrashing, a leak, or an excessive Lemonade context.
Expected residency is not a fault. It still consumes capacity and cannot be
subtracted from physical usage merely because the owner intended it.

## Four independent answers, not one health badge

| Dimension | Question | Evidence and presentation |
|---|---|---|
| Service health | Are daemon, workers and collector functioning? | Component failures, responsiveness, collector errors; missing telemetry is not healthy zero usage. |
| Utilization | What resources are occupied or busy? | Host/device/process counters with explicit units, denominator, time window and coverage. |
| Pressure and interference | Is work being delayed or failing for resources? | Stall time, paging rates, allocation failures, queueing and measured latency/throughput changes. |
| Admission headroom | Should another identified workload start? | Existing lease decision and reason, effective limits, current observations, measured workload envelope and safety margin. |

A valid state is **service healthy / model resident / limited build headroom /
no observed memory stalls**. Another is **service healthy / sustained memory
stalls / builds deferred**. Do not collapse either into an unexplained degraded
badge. Preserve legacy API meanings until a versioned migration; new presentation
must identify a legacy aggregate verdict as such, not relabel it service health.

Pressure thresholds, hysteresis and lease serialization remain visible policy,
not kernel facts. Changing the display does not change admission. Policy tuning
requires a separate owner decision and recorded before/after evidence.

## Memory topology and accounting

Model physical backing domains and device access separately. A host can contain
UMA accelerators, discrete VRAM devices, firmware-reserved regions, multiple NUMA
nodes and process/container limits at once. Do not infer all of that from a GPU
vendor name or the word integrated.

Required distinctions:

- Installed physical capacity versus OS-managed capacity and known reservations.
  Firmware carve-outs can reduce OS-visible RAM. Report unknown relationships.
- OS-used/available memory, reclaimable cache, anonymous/file-backed residency,
  wired/pinned or nonpageable allocations, and compression where observable.
  Available is an OS estimate, not simply free; OS definitions differ.
- Swap capacity/occupancy versus swap-in/out rates. Existing swap occupancy is
  not proof of current thrashing; hard faults can also be file-backed I/O.
- Device-local capacity, shared host-backed allocations, aperture/address-space
  limits, driver budgets, committed allocations and actual resident backing.
  A reported GPU budget is not extra installed RAM or guaranteed free capacity.
- Mapping, residency, ownership and reclaimability are different relationships.
  A model mmap, process RSS and driver allocation can describe overlapping bytes.

**Never add process RSS, GPU shared-memory usage and host used memory to obtain
a total.** Host accounting is the physical-capacity view; process/device views
are attribution views. RSS sums are labeled overlapping. PSS, when available,
is proportional attribution, not exact ownership or a guaranteed GPU-inclusive
physical total. Unknown overlap remains unknown; no guessed residual called
"other" if its inputs are not a disjoint partition.

For Apple silicon, shared physical backing does not imply that every allocation
is equally reclaimable or that Metal allocation totals equal process footprint.
For AMD UMA, driver VRAM/GTT labels do not by themselves establish independent
physical pools. Windows dedicated/shared segment counters need adapter topology
and budget context, even on hardware also running Linux.

UMA shares more than capacity. CPU compilation and accelerator inference may
compete for memory bandwidth, cache, power and thermal headroom without exhausting
RAM. Report measured bandwidth/thermal limits where supported. GPU busy percent
alone is not bandwidth saturation, and correlation alone is not causation.

## Observation contract: portable data, replaceable collection

Separate four functions: acquire OS/device facts; normalize explicit semantics;
correlate with operations; project observations and advice. Lease admission remains
with Bozzetto's existing owner. Compiler scheduling and proof authority remain
with their components. No parallel scheduler or native execution authority is
introduced by resource monitoring.

The portable record requires:

| Record | Required fields/semantics |
|---|---|
| Observation envelope | Host/boot identity, collector version, sequence, wall time for display, monotonic interval for deltas, acquisition duration, per-source acquisition time/skew and coverage. |
| Metric | Stable key, value or explicit unavailable reason, unit, scope, gauge/counter/rate kind, denominator, source, quality and reset/discontinuity marker. |
| Memory domain | Identity, physical backing relationship (shared/distinct/unknown), capacity/budget/resident/committed semantics and overlap qualification. |
| Process/thread | PID/TID plus start identity, parent incarnation when known, name, CPU delta, resident footprint, thread count and attribution confidence. |
| Operation | Lease/operation identity, project/component, revision, tool configuration, registered process incarnation(s), phase and start/end/cancel/drain observations. |
| Inference workload | Server/backend/model identity, declared configuration, observed residency, actual request/context size, request phase, latency and throughput where exposed. |
| Coverage | Supported/unsupported/permission-denied/disappeared/reset/error/stale, scanned/unreadable/omitted counts, sampling gaps and retention truncation. |

CPU has two useful scales: host utilization (0–100% of observed host capacity)
and process CPU equivalents (100% = one logical CPU). Display both only with
labels; processor count, thread count and load average are not utilization.
Effective affinity/cgroup/job limits are separate from host capacity. Heterogeneous
CPU cores are not assumed to deliver equal work per busy second.

Counters use sufficiently wide integer representations and declared units;
transport must preserve exact byte counts and timestamps. First samples, PID reuse,
device resets, suspend/resume and counter regression invalidate affected deltas
rather than becoming idle zero or spikes. Missing GPU data never becomes 0%.

Acquisition is event/subscription-driven by default, with bounded top-CPU/top-memory
process views and selected-process thread inspection. Counter-only sources require
an explicitly declared, demand-scoped sampling capability; no default periodic
whole-host or process scan is authorized. Expensive proportional-memory or driver diagnostics are demand-driven
and measured. Bound scans, retries, payloads and retention; report omissions and
collector overhead. Do not read process environments or expose full command lines
by default. Project association uses explicit registration first; cwd/name ancestry
is labeled inference, not lease ownership. Process names alone cannot distinguish
concurrent Farscape, compiler and unrelated dotnet work.

**Event-driven acquisition, incrementals and observables are the default, end to
end.** No HTTP long polling, periodic snapshot fetching, scan-and-diff loop or
subscriber-local timer is an acceptable substitute. Native event sources feed
owner-held inputs; dependency changes drive derived observations and subscriptions.
Some utilization counters have no change notification and require timed reads to
form a rate. Declare that limitation per metric and use only explicitly enabled,
bounded, demand-scoped acquisition; never silently fall back to polling. Missing
support remains visible. Integrate through the shared foundation's publication
boundary; a missing library capability is an integration gap to fix there, not
permission to create a parallel invalidation engine.

Every surface reads the same published sequence: dashboard, CLI and MCP. Display
sample time, interval and stale state separately from live uptime. On reconnect,
replace old observations and identify the new daemon/boot incarnation. Two clients
may receive different sequences briefly; they must not display old data as current.
Staleness is explicit after three expected sample periods, including when the socket
is connected but the collector is stalled. Bounded history aligns utilization,
pressure, inference requests and tool operations without claiming cross-host clock
precision that has not been established.

## Incremental monitoring, subscriptions and calibration

**Owner direction:** monitoring itself is incremental and reactive, not merely a
periodic report with a reactive frontend. One owner maintains an observation and
derivation graph through Fidelity.FSharp.Incremental in the interim host; the
native implementation preserves that contract. No independent polling/watch
engine, anomaly authority or admission controller belongs in each client.

The dependency graph separates:

1. **Source inputs:** host counters, process/device lifecycle, effective limits,
   operation transitions, inference activity and configuration identities.
2. **Derived observations:** valid interval deltas, backing-domain accounting,
   operation attribution, pressure windows and data freshness.
3. **Calibrated expectations:** baseline identity, work-cycle envelope, expected
   release time, recovery residual and confidence for comparable workloads.
4. **Findings:** unexpected retention, suspected leaks, pressure/interference and
   stability risk, with the exact evidence and calibration revision used.
5. **Decisions:** advisory or authorized recovery plans at the existing admission
   and process-lifetime owners. A computed finding is not permission to kill.

Source changes invalidate only dependent derivations. Sampling an unrelated GPU
must not rebuild every process history. Avoid publishing a fresh timestamp as a
change to all semantic inputs: freshness has its own dependency. Window expiry,
operation deadlines and baseline expiry are explicit clock inputs and can change
a finding even when a counter is unchanged. New configuration, process incarnation
or incomplete evidence invalidates affected expectations rather than reusing an
apparently compatible old baseline. Bound graph cardinality and retire histories
when their retention scope ends; the monitoring graph must not become a leak.

### Partas.Solid presentation contract

The existing Partas.Solid frontend is intentional: MVU-style event flow with
Solid stores, not a replacement UI architecture. Backend owners publish typed,
versioned observations; bridge Events fold through the frontend Model into Solid
stores. Components render those stores and dispatch Commands. They do not collect
system facts, calculate competing pressure verdicts or own recovery policy.

Keep the existing store discipline: Fable-emitted plain anonymous records and
arrays, stable incarnation-aware keys, and keyed reconciliation of changed slices.
Do not replace the entire model for every observation, duplicate one mutable object
across store locations, or put opaque class/record instances into stores and expect
field-level tracking. Preserve unchanged rows and let Solid update only consumers
of changed fields. Read reactive state inside tracked expressions, not component
setup. Page-local controls stay local; authoritative observations stay backend-owned.

Monitoring subscriptions follow view scope and detail demand, with disposal on
scope exit and shared backend acquisition across clients. No component timer,
fetch loop, separate frontend event bus or Elmish-style whole-view rerender loop.
The bridge remains transport, not another resource owner. Reconnect reconciles a
versioned snapshot before subsequent updates; stale/disconnected coverage is an
explicit model state. Recovery buttons dispatch commands whose accepted/refused
outcomes come from the backend; an optimistic click never means a process exited.

Acceptance must show that one process-metric change updates its relevant store
slice without resetting unrelated rows, selection or controls, that leaving a
view releases its demand without dropping another subscriber's demand, and that
two pages converge on the same backend observation revision. This extends the
existing `App.fs` / `Model.fs` / shared-protocol design, not a second UI framework.

### Subscription and acquisition lifecycle

A subscription declares scope, requested observations/detail, maximum acceptable
age and retention. Host, device, process incarnation, operation and component are
separate scopes. Multiple consumers share acquisition and derived work. Releasing
one subscription cannot stop acquisition needed by another. The stability owner
holds its own baseline subscription for the daemon's lifetime; closing all browser
tabs does not disable pressure detection or recovery.

Use native lifecycle, pressure and device notifications as primary inputs.
Work-start/finish, configuration changes and pressure transitions trigger scoped
observation and calibration. Scheduled recalibration and window expiry use explicit
one-shot deadlines owned by the graph, not a perpetual wake-and-check loop.
Counter-only metrics may use an explicitly enabled acquisition window with a
specified interval, duration, demand owner and overhead budget. Record actual
measurement intervals; windows are duration-weighted. End acquisition when that
window or its final demand ends. The safety subscription retains event/deadline
coverage; it does not implicitly authorize a permanent periodic system scan.

Consumers receive an initial snapshot, then sequenced changes with explicit gap
and resynchronization semantics. Slow visual subscribers may coalesce current
values; recovery decisions and action receipts must remain in a bounded durable
audit trail with retention/gap accounting. Observer cancellation never cancels a
recovery already admitted by its owner. Source replacement and daemon shutdown
join owned acquisition before its resources are released.

The implementation gate must prove these subscription/lifetime properties with
the selected shared-foundation API. If its publication or window support is
insufficient, close that explicit library integration gap rather than calling a
new event bus an incremental implementation.

### Work-cycle calibration

Track `Settled → WindUp → Active → WindDown → Settled`, with separate
`Contended`, `Recovery` and `Unknown` observations. These are observed phases,
not a promise that every operation follows a clean isolated sequence. Overlapping
work records overlap; it must not be assigned a fabricated single-workload baseline.

Calibration is passive during ordinary operation. It establishes and periodically
revalidates expectations from repeated comparable cycles; synthetic stress or
unloading a resident model requires an explicitly admitted experiment. Calibration
identity includes host/boot, topology and limits, tool/model configurations,
operation class and scale, concurrency, cache state and relevant background load.

Record settled residency and variability, wind-up allocation rate, CPU/GPU demand,
peak memory, pressure/queue response, active plateau, wind-down release curve,
time to quiescence and post-cycle residual. Differentiate completed work from
physical process/device drain. Model load, prefill, decode and retained-cache
phases need different envelopes. Warm caches are not failed wind-down.

Reassess after configuration changes, device/driver replacement, suspend/resume,
large background-workload changes and at a bounded configured review interval.
Show calibration age, comparable cycle count, coverage and confidence. Retain a
versioned last-known-good reference alongside any rolling estimate so a growing
leak is not continually learned as normal. Freeze suspect intervals out of baseline
promotion; require sustained recovery or reviewed rebaselining. No fixed universal
wind-down deadline or leak slope is asserted without workload evidence.

### Leak-aware observations

A growing RSS reading is a lead, not a diagnosis. Distinguish working-set growth,
file mappings, reclaimable caches, allocator high-water retention, pinned/device
allocations, resource/handle leaks and externally retained work. Where supported,
compare private/proportional footprint and backing-domain observations at comparable
quiescent points across repeated cycles. Partial visibility weakens the finding.

Use explicit findings: `ExpectedRetention`, `UnexplainedGrowth`, `SuspectedLeak`
and `ConfirmedLeak` (the last requires diagnostic evidence, not a slope alone).
Findings record incarnation, baseline, growth/residual windows, recurrence,
confidence, alternative explanations and available diagnostics. Capture bounded
pre-action evidence when safe; expensive heap/device diagnostics require admission
and must not worsen an emergency. Keep leak suspicion separate from stability
risk: a leak may not yet threaten capacity; legitimate nonleaking work can still
require emergency recovery.

## Stability recovery and process termination

Bozzetto must be able to reclaim stability, including terminating processes, not
only display warnings. This is a new explicitly authorized recovery capability;
it is **not enabled by this design document**, and no running process is selected
for termination by this documentation change. It extends Bozzetto's existing
lease/process-lifetime authority, not a competing resource scheduler.

### Authority and protected workloads

Before automatic destructive recovery is enabled, install a versioned owner-approved
policy specifying eligible workloads, protection rules, priorities, trigger evidence,
dwell/deadline values, action limits and permitted graceful/forced operations.
Daemon-owned workers and leased external tools have different authority. A build
lease alone does not authorize killing its caller, editor or arbitrary descendants.
Explicit registration binds a process incarnation and controllable job boundary to
the permitted recovery actions. Name or cwd heuristics never grant kill authority.

Lemonade is protected by default until the owner explicitly grants a recovery
contract for its server/model lifecycle. Desktop, editor, storage/security services,
OS-critical processes and unrelated workloads are excluded by default. Protected
work cannot become eligible merely because a recovery action failed. Manual
termination requires an identified target and explicit confirmation unless already
covered by an approved automation policy. Unknown or stale identity means refuse
that action. Privilege elevation is never implicit.

OS-level permission is necessary but insufficient. Revalidate policy, live target
identity, operation ownership and current evidence at the execution boundary.
Prefer stable process handles (Linux pidfd or a verified equivalent, Windows process
handles/job boundaries, platform-appropriate macOS identity checks); naked PID reuse
must not redirect termination. If a race-safe target cannot be established, report
the limitation and refuse unattended destructive action. Group kills require a
proven, authorized group boundary; ancestry snapshots alone are insufficient.

### Recovery sequence

1. Detect sustained pressure, imminent capacity failure or severe workload
   interference using fresh observations and a configured policy. An emergency
   can bypass ordinary dwell only under a preapproved emergency rule. A low
   available-memory percentage or suspected leak alone is not a universal trigger.
2. Ask the existing lease owner to defer additional competing work. Report which
   work is waiting and why. Do not bypass its admission or compiler cancellation
   contracts to invent a second throttle.
3. Prefer authorized reversible relief: stop admitting inference requests, request
   cache release where the workload supports it, lower admitted concurrency or
   cancel disposable work through its owning tool. Each is a separate capability;
   do not assume pause/unload/reconfigure is supported or safe.
4. Select an eligible target by policy: protection and authority first, then expected
   *reclaimable physical backing*, recovery time, work loss and restart cost. Largest
   RSS is not necessarily the most useful victim on UMA. Show uncertainty and
   predicted consequences before nonemergency action.
5. Request graceful cancellation/shutdown with an action identity and deadline.
   Observe physical drain, including device work where visible. Escalate to forced
   termination only if that exact target and escalation are authorized. A graceful
   request rejected or timed out is not proof that the process stopped.
6. Verify process exit and observed pressure/headroom recovery. Driver allocations
   may outlive a process; exit is not proof of reclaimed GPU memory. Wait within a
   bounded recovery window, then reassess. Serialize recovery decisions to prevent
   several observers independently killing multiple workloads for one pressure event.
7. Apply cooldown and a restart budget, resume admission only after sustained
   recovery, and record a new baseline if justified. Do not automatically relaunch
   a failing workload into the same pressure or endlessly escalate victims.

Record trigger samples, policy revision, target identity, action/request/acknowledgment,
exit/drain evidence, observed reclaimed capacity, lost work, remaining uncertainty
and recovery outcome. If no eligible target exists or reclamation fails, report
`UnresolvedPressure` and keep the permitted admission restriction; do not claim
stability or silently broaden privileges. The kernel's OOM machinery remains a
separate last-resort authority, not something Bozzetto claims to replace.

On daemon restart, reconcile outstanding action receipts against current boot and
process identities. Never blindly replay an old kill. Roll out as observe-only,
then policy dry-run, then approved graceful recovery, then bounded forced recovery.
Dry-run recommendations and executed actions must be visibly different.

## Platform acquisition and the native path

The domain contract must not depend on System.Diagnostics.Process, CLR object
identity, WMI object graphs, managed performance-counter packages or reflection.
Interim F# can perform file reads and narrow native calls at the acquisition edge;
do not build a large .NET-specific monitoring subsystem to throw away later.
Pure parsers, delta calculations and portable fixtures survive migration. Native
handles, privileges, errors and cancellation stay explicit at that edge.

Candidate sources below require per-platform validation; they are not equivalent
counters or a guarantee that every driver exposes every metric.

| Platform | Initial acquisition boundary | Important limitations |
|---|---|---|
| Linux | `/proc/stat`, `/proc/meminfo`, `/proc/vmstat`, PSI, process/task stat and status; cgroup v2 limits/events; DRM/sysfs driver counters and capability discovery. | Process races, hidepid/permissions, kernel/driver availability and UMA VRAM/GTT overlap. Detailed smaps and GPU attribution can be costly or unavailable. |
| Windows | Narrow native system/process time and memory APIs; commit/available/paging observations; job limits; DXGI/D3DKMT or supported driver telemetry for adapter segments/budgets/activity. | No assumption of PSI equivalence; DXGI process budgets are not whole-host GPU usage. Engine percentages, residency, access rights and shared segments need explicit scope. |
| macOS / Apple silicon | Mach host/task and VM statistics, sysctl/process APIs, pressure/compression/swap observations; documented Metal/device telemetry where available. | Process access is restricted; Metal working-set guidance/allocation data is not universal system GPU utilization. Privileged diagnostics are optional, not a default dependency. |

Fidelity.Platform owns platform capability/access contracts; Farscape and binding
owners provide reviewed native ABI bindings where needed. Bozzetto owns collection,
normalization, operation correlation and user-visible evidence. These are requested
handoffs, not assignments claimed completed in sibling repositories. Native Clef
replacement must pass the same fixtures and real hardware gates without .NET.
A bounded diagnostic subprocess may support an experiment, but parsing a vendor
CLI indefinitely is not the core portable API. Never elevate privileges silently.

## Lemonade coexistence: measure before changing configuration

Treat Lemonade as an independently owned workload, not a Bozzetto child. Distinguish
model weights, runtime buffers, active KV cache, retained prompt caches, pinned
residency, configured context limit and actual occupied tokens. Configured context
alone does not establish allocated bytes; backend allocation strategy matters.
Changing context may also alter workload utility, recomputation and cache reuse.

Record current configuration through the owner's API/configuration and runtime
identity. Record what is declared versus actually observed. Never copy prompts,
credentials or source payloads into resource receipts. Model/config digests,
token counts and anonymized workload identifiers suffice.

### Controlled experiment

1. Agree representative tasks and acceptable latency/headroom before comparison:
   Farscape binding generation, Clef/Composer and component cold/warm builds,
   representative suites, and actual inference prefill/decode workloads. Pin
   revisions, tool identities, parallelism and input sizes. Lease every tool run.
2. Capture settled idle baseline with the chosen model resident, then build-only
   with model resident but no inference, inference-only, and concurrent work.
   Record editor/background activity and warm/cache state. Do not silently unload
   the model to manufacture an idle baseline.
3. Record queue delay separately from execution; build/test elapsed time and
   peaks; inference time-to-first-token, prefill rate, decode tokens/second,
   completion time and tail latency; memory stalls, paging rates, failures and
   device/host activity over the same intervals. Include collector cost.
4. Repeat matched trials in alternating order to reduce cache/thermal bias.
   Use at least three repeats for an initial comparison, disclose variance and
   increase repetitions if noisy. A small sample cannot establish a robust p95;
   report sample count and range rather than false tail precision.
5. Compare concurrent performance with the matching isolated baseline. State
   slowdown and throughput changes with uncertainty. Low available RAM without
   stalls or workload regression is not sufficient evidence to shrink context.
6. If interference is material against the agreed objective, test one reversible
   change at a time: separate prefill from compilation, reduce build concurrency,
   reduce retained cache or context, change inference parallelism, or select a
   smaller model. Owner approval precedes runtime/config changes. Record whether
   restart/reload is required and distinguish reload cost from settled behavior.
7. Confirm improvement and retained task capability; restore the baseline for
   a comparison where safe. Adopt only a demonstrated tradeoff, with rollback
   configuration and a new baseline receipt. Expected residency never suppresses
   genuine allocation failures, sustained stalls or unacceptable interference.

A recommendation states: affected operation/configuration, observation window,
coverage, measured regression, suspected bottleneck and confidence, proposed
change, expected tradeoff, and confirming experiment. If bandwidth or driver
visibility is absent, say so. "Reduce Lemonade memory because tight" is not an
acceptable recommendation. An optional owner-defined resident workload profile
records expected ranges, not a subtraction from usage or an exemption from limits.

## Delivery increments and acceptance

1. **Linux visibility:** host CPU, RAM/available/cache, swap occupancy/rates,
   pressure, supported GPU counters and external process attribution; shared
   pushed samples and explicit freshness. Daemon footprint is secondary. Unknown
   device fields remain unavailable. No lease policy change.
2. **Work correlation:** register leased root process incarnations with exact
   caller authority; track descendants/lifetime limitations, project/component
   phases, and bounded timelines alongside Lemonade request statistics. A granted
   lease is not proof that every child is accounted for. No false attribution of
   unrelated processes with similar names.
3. **Coexistence evidence:** execute the controlled matrix on this UMA workstation;
   publish receipts and justified recommendations before changing its settled
   Lemonade profile. Do not block useful visibility on completing all attribution.
4. **Reactive calibration and recovery:** establish shared subscriptions and
   incremental dependency invalidation; learn and revalidate work-cycle envelopes,
   publish leak-aware findings, and exercise policy dry-runs. Enable graceful and
   forced recovery only through separately approved policies and executed safety
   gates. Useful Linux monitoring does not wait for autonomous termination.
5. **Platform parity and native migration:** Windows on UMA, Apple-silicon macOS,
   and a discrete-GPU control establish domain/overlap semantics and honest gaps.
   Replay parser/delta fixtures in Clef and compare native observations with the
   same OS reference sources. Linux success is not cross-platform acceptance.

Required tests include stable residency without pressure, low available memory
with and without stalls, counter resets/PID reuse/disappearing processes, hidden
processes, unavailable GPU fields, shared versus discrete backing, cgroup/job
limits, collector overload, stale connected clients and reconnect/restart. Test
that shared allocation views are never summed into physical totals and that a
busy external workload appears while the daemon is idle. Each new rule needs
positive/negative controls and a mutation that the test catches.

Reactive-monitor acceptance additionally proves shared acquisition, last-subscriber
release with the safety subscription retained, selective recomputation, window
expiry without new counter values, bounded histories and calibration invalidation.
Use stable-cache and leaking-worker controls over repeated work cycles; prove a
rolling baseline cannot absorb the injected leak unnoticed.

Recovery acceptance uses disposable fixtures, never arbitrary workstation victims:
protected/unauthorized targets refused, PID reuse and group-boundary races refused,
gracious exit and forced escalation distinguished, cancellation during recovery,
concurrent triggers, delayed GPU reclamation, no effective reclamation, exhausted
action/restart budgets, stale sources and restart reconciliation. Mutation tests
must catch protection bypass and a false claim that process exit implies stability.
Real device reclamation needs platform evidence beyond simulated process tests.

Validate targeted owners/consumers during development and the repository's
unfiltered TRUST backstop at integration. Deployment is part of acceptance:
owner-directed drain, install, restart, reconnect and baseline; verify actual
served bytes, live observations, matching worker/compiler closure, and rollback.
Do not call a checkout build delivered when the browser still runs old code.

## Evidence provenance

Design was grounded through fresh retrieval `schema → find → pgq → sources` at
snapshot `0cbe94d87d33e4cd8d2f3f0437fa2b9860db04e798b93d6efa5d56ca09edcb97`,
Bozzetto revision `f7a742db01507d34dfd1d7d6d122be4cec5334c7`:

- `docs/Workstation_Findings_Ionide_And_Bozzetto_Telemetry_2026-10-04.md:91–139`
  establishes the owned-process/lease/external-work visibility gap.
- `docs/Bozzetto_Development_Horizons.md:40–105` establishes first-horizon
  local development, native hosting and resource-use acceptance.
- `docs/Bozzetto_Fidelity_Component_Contracts.md:23–48, 286–306, 351–390`
  establishes component ownership, shared-memory handoffs and scoped evidence.

The owner's October 10 direction establishes UMA support, Linux-first delivery,
Windows/macOS portability, native Clef intent and demonstrated rather than guessed
Lemonade tradeoffs. This design does not claim that those implementation gates
have passed or that current context size should change.
