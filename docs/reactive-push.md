# Reactive push

Bozzetto's push plane presents owner-published facts without creating a second authority for them. Ranvier supplies the signal, memo and effect reference shape. Fidelity.FSharp.Incremental supplies the explicit dependency and work-lifetime machinery used by the implemented fact plane. Fidelity.PSG supplies compiler-owned evidence, which a page may display but cannot manufacture or authorize.

The daemon serves `/dashboard`, `/composer` and the WebSocket endpoint `/ui/bridge` on port 47749. Port 47750 is the control listener, not a second UI pipeline. The browser is Partas.Solid; Ranvier is a reference for reactive consistency here, not a claim that the daemon's fact plane executes Ranvier nodes. See [architecture.md](architecture.md) and [Bozzetto_Fidelity_Component_Contracts.md](Bozzetto_Fidelity_Component_Contracts.md).

## Delivery boundary

The required direct daemon-browser contract is typed binary. The same rule applies to daemon-worker and agent-owned direct interchange. JSON is not an alternative wire, even when encoded with Fidelity.Data. Fidelity.Data belongs at export edges and the independent, MailboxProcessor-driven diagnostic sink process. MCP's externally standardized JSON-RPC boundary is the sole external exception; it does not license JSON inside the push plane. See [mcp-tools.md](mcp-tools.md), [binary-format-spec.md](binary-format-spec.md) and [Bozzetto_Diagnostic_Sink.md](Bozzetto_Diagnostic_Sink.md).

Implementation status must be kept separate from this requirement:

| Area | Read-code evidence | Status |
| --- | --- | --- |
| Shared browser vocabulary | `bozzetto-web/src/Shared/Protocol.fs` defines typed commands, events and payloads; `ProtocolVersion` is 7 | Implemented vocabulary, including Health acquisition times and Leases revision |
| Fact derivation | `Bozzetto/UiBridge.fs`, module `Facts`, uses Fidelity.FSharp.Incremental | Implemented foundation-backed projections |
| Browser transport | `Bozzetto/UiBridge.fs`, `Outbound`, `IBridgeSocket` and `WebSocketTransport`, sends text through `UiBridgeCodec` and explicitly refuses binary frames | Interim JSON text wire; the browser's text wire is drift to remove |
| Passive resource endpoint | `Bozzetto/UiBridge.fs`, `mapRoutes`, writes `/api/resources` as `application/json` | Direct-interchange drift, not a permitted export contract |
| Reconnect subscription | `Facts.Graph.Register` captures current facts and installs a sink under one gate | Implemented backend register-and-snapshot |
| Atomic browser reconciliation | The prologue sends separate events; no single reconciliation envelope is defined in `Protocol.fs` | Requirement, not demonstrated end-to-end atomic installation |

Replacing the JSON serializer does not close the transport gap. Binary encoding, decoding, compatibility checks and receiving installation need their own acceptance evidence. Preserve the typed vocabulary and ownership seams while removing the text wire. Protocol 7 carries Health's `revision`, `sampledAtMs` and `recordAtMs`, and Leases' `revision` on the interim wire; these additions do not make that wire binary.

SSE is not the browser pipeline. FSI evaluation, REPL bindings, warmup, F# session hot reload, Datastar and runtime patching events are not current product contracts. Retained source or old endpoint descriptions do not restore those capabilities.

## Ranvier's reference shape and its limits

The Ranvier repository's `README.md` describes signals as settable sources, memos as values recomputed on read after dependency changes, and effects as scheduled side effects. The source-based library study, `/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/increment-2026-10-10/sol-audit/2026-10-11-pm/library-study.md`, §1–2, further distinguishes tracked reads from `Peek`, dynamic dependency discovery from explicit read declarations, and effect flushing from asynchronous settlement. Its Ranvier source citation root is `~/repos/Ranvier/src/Ranvier/`; the study supplies source-based evidence, not a pinned revision or executed acceptance.

A graph has one mutation consistency domain. Async acquisition may run outside it, but completion must enter the domain before it changes graph state. Ranvier's guarded or serialized affinity modes are ways to enforce that discipline; unchecked affinity is not protection. `Flush` drains effects, while `Pump` admits inbox work and requests a flush. Neither operation proves that all external work has joined (the same library study, §1, Ranvier source root `~/repos/Ranvier/src/Ranvier/`).

For Bozzetto, the corresponding shape is an immutable fact, a pure projection and eligible publication. It is a correspondence, not semantic equivalence:

| Ranvier reference | Foundation-backed Bozzetto rule |
| --- | --- |
| Signal with a typed value | An owner publishes an immutable payload, identified by an input, stamp and opaque value token |
| Memo with dynamically tracked getters | A definition declares every read occurrence in advance; execution consumes frozen `StartRequest.Reads` |
| Effect after reactive settlement | The adapter interprets an eligible `Offer`; demand activates work but does not itself broadcast a value |
| Equality suppresses propagation | Identity and resolved read receipts govern foundation reuse; equal display values do not erase compiler proof or attempt identity |
| Owner cleanup before rerun or disposal | `Drained` certifies joined execution and resources; a cleanup callback alone cannot certify physical drain |

A Ranvier `Ready` value or an empty effect queue is not compiler authority. A foundation `Offer` is eligibility under current declared premises, not a PSG proof or authorization to execute an artifact. The host must retain source-issued compiler identities even when their display projections compare equal.

See [Bozzetto_Incremental_Foundation_Adoption.md](Bozzetto_Incremental_Foundation_Adoption.md) for complete reads, cold admission, demand, withdrawal and drain. The fact plane's synchronous projections do not establish conformance for every other hosted work lifetime.

## Facts and their owners

`Bozzetto/UiBridge.fs`, `Facts.Owners`, names four inputs. Each admission replaces the complete prior snapshot. These are not deltas, so discarding an older serial is sound within the bound owner lifetime.

| Input | Owning publication | Owner order | Contents consumed by projections |
| --- | --- | --- | --- |
| Tick | `Bozzetto/HealthTick.fs` | `Tick.Serial` | Machine memory, settled pressure level, optional daemon health record and telemetry, with acquisition times |
| Pool | `Bozzetto.Core/Features/LeaseWatch.fs` | `Pool.Serial` | Active lease views and queued asks |
| Agents | `AgentWork.Publication`, exposed by `CohortOwner.Handle.ReadWork` and `WorkChanged` | Bound `State.Incarnation` and `State.Sequence` | Complete bounded client-reported work state |
| Membership | `CohortOwner.Handle.ReadFrame` after cohort events | `CohortFrame.Version` | Member identities, roles, seats and last renewals |

The health tick is one sequencer, not an independent memory-policy authority. `HealthTick.fold` passes the sample to `MemoryPressureWatch.observe`, retains the returned level with that sample, and advances the tick serial under its own gate. `Bozzetto.Core/Features/MemoryPressureWatch.fs` owns the retained level and hysteresis around the pure memory policy. The bridge does not derive its own pressure level or subscribe to level changes as a reason to reread arbitrary owners.

`HealthTick.refresh` takes an ordinal under the tick gate when it starts, then obtains the daemon record outside that gate. Machine readings arriving during refresh update the retained tick but are not announced separately; refresh publishes the retained reading with the installed record. A successful record is installed only when its start ordinal exceeds the last installed ordinal. An earlier-started refresh finishing after a later-started refresh has installed its record therefore keeps the newer record, telemetry and record time, while still advancing the publication serial. The ordinal orders record acquisition; `Tick.Serial` orders downstream publications.

Machine and record acquisition may have different ages: `SampledAtMs` dates the machine figures and settled level; `RecordAtMs` dates the installed record and telemetry. A successful installation takes telemetry and advances the record time together. A thrown reader is logged; a failure, absent reader or `None` result leaves the record, telemetry and `RecordAtMs` unchanged. Before the first successful record, record and telemetry are absent and `RecordAtMs` is zero. A new publication is not proof that every field was freshly acquired, and a failed refresh does not make retained accounting look fresh.

The HTTP `/health` route in `Bozzetto/McpServer.fs`, `mapHealthRoutes`, asks for the daemon's record through `HealthTick.refresh ()` and uses the returned `tick.Record`. It overlays the route's own per-session summaries and the tick's settled memory level. When no record is available, it builds the local response from the same tick reading; this supports the pre-reader startup case rather than introducing a parallel live daemon-record authority. The pressure explanation uses `tick.Machine` with that level. HTTP and the page thus consume the same daemon record source, though route-specific session detail and later acquisitions need not be identical.

`LeaseWatch.update` commits pool changes and constructs their serial-bearing snapshots under the lease gate, then announces outside it. Value and serial never come apart, but announcements may be heard out of order: the plane admits by serial and drops the older whole snapshot. This is the stated owner contract, not a requirement to announce under the pool gate; doing so would invert lock order against the plane's subscribe-and-current section. Lease expiry uses a single owner-held, one-shot deadline fenced by generation, not a page-local scan. Lease views omit the release capability. `Project.leaseDisplayId` derives a display identifier only; receiving it grants no ability to release a lease.

`Bozzetto.Core/AgentWork.fs` owns report validation and bounded state, including report ordering and usage checks. `Bozzetto.Core/Features/CohortOwner.fs` serializes report commands, verifies present membership and publishes committed work through the injected publication seam. It retains pending publication failures for explicit reconciliation. Cohort frame publication follows ledger append, and events follow publication of the frame. The bridge reads the published frame, not the owner's mutable command state.

`Facts.Graph.Start` subscribes to owners and takes their current snapshots while holding the graph gate. Tick and Pool notifications carry values. Agent and membership notifications trigger reads of their published snapshots at this admission seam. Those reads are not allowed inside reusable projection bodies. Without a cohort owner, Agents and Membership are absent and no Board result is fabricated.

## Admission and one mutation domain

The gate in `Facts.Graph` covers admission, `Core.step`, committing its returned state, interpreting effects and handing publications to sinks. It also covers registration, current-value capture, epoch handover and shutdown retirement. A lock around `Core.step` alone would not establish this ordering.

The foundation does not authenticate owner identity or interpret stamps as increasing serials. The adapter makes these decisions before `SetInputs`:

| Publication | Adapter outcome |
| --- | --- |
| Same input, serial greater than last admitted | Admit the complete replacement snapshot |
| Same serial, identical fact | `Refusal.Duplicate`, no update |
| Serial less than last admitted | `Refusal.Older`, no update |
| Same serial, different fact | `Refusal.Conflict`, logged and counted as an owner protocol fault |
| Agent state with an incarnation other than the startup-bound incarnation | `Refusal.ForeignIncarnation`, logged and counted; never resets the serial window |
| Publication reaching serial admission after shutdown seals the plane | `Refusal.Retired`, no update or delivery |

Health, lease and cohort owners are bound by the daemon lifetime. The agent publication incarnation is captured when `UiBridge.create` constructs the graph. A payload cannot authorize an incarnation transition. A new daemon constructs a new plane; delayed publications from another agent-owner incarnation cannot roll the board back. The foreign-incarnation check precedes serial admission, so a foreign agent publication remains a `ForeignIncarnation` fault even after shutdown; otherwise late publications are refused as `Retired`.

Announcements may arrive out of order because owners announce outside their own gates. Admission handles that ordering explicitly; it is not an intrinsic property of graph scheduling. Do not reuse this policy for deltas, whose missing predecessors cannot safely be discarded.

Value tokens index immutable payloads in the plane's store. They are not owner serials, wire revisions, proof identities or capabilities. The adapter issues fresh foundation input stamps independently of the owner's publication order. These rules are implemented in `Bozzetto/UiBridge.fs`, `Facts.Graph.admit` and `Agents`.

## Declared works and eligible publication

`Facts.Graph.define` reserves and replaces one stable scope per epoch, then acquires daemon-lifetime demand for three works:

| Work | Complete declared reads | Projection |
| --- | --- | --- |
| Health | Tick | Daemon health, using the tick's machine figures, settled level and acquisition times |
| Leases | Pool | Granted and queued display rows |
| Board | Agents, Pool, Membership | Reports plus lease and cohort presence |

Health reads one batched Tick. There is no separate machine input or daemon-record input. Projection bodies consume the payload tokens from frozen read receipts; they do not sample telemetry, request worker status or reread mutable owners.

`Facts.Graph.apply` commits each `Core.step` result before interpreting its effects in returned order. On `Start`, it computes synchronously, stores the result and assigns a work publication serial, then submits `Finished(Succeeded token)` followed by `Drained`. Publication occurs only on the resulting `Offer`. `Withdraw` releases the superseded result payload; `Cancel` records cancellation and drain; a `Drain` effect is acknowledged for these resource-free synchronous projections. `StoredResults` exposes the number of retained projection results, permitting controls that superseded values are released rather than accumulated.

This immediate drain acknowledgement is justified only by that execution shape. Introducing async projection work or retained resources requires actual joined cleanup before `Drained`. `Finished` alone is never publication authority. Late or superseded success must not restore eligibility.

Projection exceptions are logged, counted as faults, recorded as failed completions and drained. Failed works are retried on a later admitted fact. Missing or failed results are omitted from `Current`; the page must represent absence, not invent a healthy row.

There is no display-equality cutoff on accepted changing inputs. A presence-only Pool or Membership change can recompute Board while `AgentWork.State.Sequence` is unchanged. `Project.agentBoard` uses the work's publication serial as the outgoing board sequence. Health carries its work serial as `DaemonHealth.Revision`; Leases now carries its work serial as `LeaseBoard.Revision`. These are projection publication identities, not the input owners' serials. Health also projects `Tick.SampledAtMs` and `Tick.RecordAtMs` unchanged onto protocol 7. Source serial provenance remains distinct from those wire revisions and ages.

## Connect, coalescing and reconnect reconciliation

`Facts.Graph.Register` installs the page sink and returns current offered values under the same gate. `Current` uses `Core.tryResult` and the stored result payloads. The foundation's `Core.fs` defines eligibility in terms of that current offered handle and epoch. Registration cannot interleave with withdrawal, recomputation and re-offer within the synchronous admission turn.

`Hub.RunAsync` refreshes the health owner before registration. It primes the outbox with Welcome, the cached Composer directory snapshot and the captured facts. Pushes wait until the prologue has been written, so a later push cannot precede that connection's initial facts. Welcome carries the protocol version, daemon version and process start time.

`Outbox` has one socket writer and one latest-wins slot for each push kind. It does not call back into the graph during transport delivery. Command answers retain their order within each answer, while concurrent commands may finish in different orders. A run's transcript precedes its terminal answer. A page closing does not cancel Composer work; the explicit Cancel command does (`Hub` uses the daemon stopping token for Composer operations).

The state-delivery contract is **coalescing**, not an event log:

- A slow page may skip intermediate values for any fact.
- Per fact, the intended visible sequence is increasing and ends at the latest settled publication while delivery remains live.
- The connect snapshot is the lower bound for later publications on that connection.
- There is no promise of every accepted fact appearing on the page, or of a single cross-fact version across separately published inputs.

`bozzetto-web/src/Frontend/Bridge.fs` reconnects with capped backoff and does not queue disconnected commands for replay. Each new connection obtains current state instead of a missed-event replay.

Atomic reconciliation means installing current state and its subscription as one consistency boundary, not merely reconnecting a socket. The backend fact registration provides that boundary. A complete binary browser contract must additionally fence daemon incarnation and connection generation, install the reconciliation state atomically, then accept only newer publications from that authority. Absent or pending works must be represented explicitly so old page state is not mistaken for a current value.

Two implementation limits remain in `Bozzetto/UiBridge.fs`: the prologue is multiple frames rather than an atomic browser installation, and `Outbox`'s revision-regression check applies only to Composer Snapshot. Explicit resync uses `Facts.Current` and ordered answer frames rather than the register-and-snapshot operation. Protocol 7 now supplies receiving order protection with Health and Leases revisions, alongside Board's incarnation and sequence; `bozzetto-web/src/Frontend/Model.fs`, `apply`, keeps older Health and Leases responses from overwriting newer values. That closes the missing lease-stamp gap, not the separate requirements for atomic receiving installation, authority fencing and complete reconnect/resync acceptance. Do not infer those guarantees from the existing text transport alone.

## Freshness is acquisition-driven

Continuous demand keeps projections current with admitted inputs. It does not acquire new telemetry. The freshness policy has no background health polling timer:

- While a page explicitly observes Resources, that acquisition cadence feeds machine readings into HealthTick. `Hub.observeMachine` passes usable RAM readings to the sequencer.
- Lease requests and renewals and health requests are acquisition points for the tick. `HealthTick.sample` folds a fresh machine sample before a caller uses the level; `/health` asks for the daemon record through `refresh`.
- A pressure-level move requests the daemon record once. Connect and explicit resync request a record refresh.
- With no page observation and no lease or health traffic, staleness is unbounded by design. Lease decisions must acquire their reading before deciding; a demanded projection alone is not a fresh sample.

This policy is reflected in `Bozzetto/HealthTick.fs` and `Bozzetto/UiBridge.fs`. It does not promise an idle sampling bound. Protocol 7 exposes the two acquisition times through Health; `bozzetto-web/src/Frontend/Model.fs`, `healthView`, preserves them, and `bozzetto-web/src/Frontend/App.fs`, `HealthStrip`, reports their ages as “read N ago · record N ago”. Failed or superseded record acquisition retains the installed record time; publishing the latest machine reading does not renew that time. A refresh that takes no new machine reading also retains `SampledAtMs`. Delivering ages is implemented on the interim wire; preserving ages and required source provenance in the typed binary contract remains an acceptance obligation.

Resources and Uptime are separate owner-driven pushes. `Protocol.fs` gives Resources its own sequence and sample time, with explicit bounded observation. `UptimeClock` runs only while pages are connected and pushes its own measured uptime; it rereads no fact owners. These acquisition and clock lifetimes are not permission to add a polling loop around Health, Leases or Board. See [operations.md](operations.md) for resource interpretation and [agents.md](agents.md) for report and presence semantics.

## Bounded epochs and shutdown

`Facts.recycleBound` is 4096 admitted sets. The adapter bounds foundation history by recycling under the same gate:

1. Suppress delivery and mark handover active.
2. Retire the old epoch and interpret its effects.
3. Require `Core.isDrained` before initializing the next epoch.
4. Recreate stable definitions and daemon-lifetime demands.
5. Install the retained current inputs with fresh foundation stamps.
6. Leave handover and resume ordinary delivery.

The admission table, retained input payloads and work publication counters survive the handover. Recomputed values during handover keep their publication serials and are not pushed as new facts. Registration cannot see the retirement gap. This is implemented in `Facts.Graph.recycle`, not a native worker or distributed-node handover guarantee.

Shutdown now has an explicit retirement path. `UiBridge.create` disposes owner subscriptions and calls `Facts.Graph.Stop()` from the stopping callback. Under the graph gate, Stop seals admission with `stopped <- true`, suppresses delivery while interpreting `Action.Retire`, and clears page sinks. Retirement's Withdraw effects release offered result payloads. Stop is idempotent, and `Drained` exposes `Core.isDrained`; the synchronous, resource-free projection shape permits the retired epoch to drain immediately. An owner callback already waiting on the gate cannot admit a fact after Stop: serial admission returns `Refusal.Retired`.

Hub separately halts the uptime clock and closes resource acquisition on stopping; linked connection lifetimes end socket service and outboxes close. This implemented fact-epoch retirement is not proof that arbitrary future async work or external resources have joined. Such work must establish physical drain before reporting `Drained`, and shutdown delivery/cleanup still needs boundary-specific acceptance evidence.

## Acceptance obligations

The prior documentation audit read the second-pass `Bozzetto/UiBridge.fs` fact plane and hub, `Bozzetto/HealthTick.fs`, `Bozzetto.Core/Features/LeaseWatch.fs`, `bozzetto-web/src/Shared/Protocol.fs`, and the `/health` route in `Bozzetto/McpServer.fs`. These source citations support implementation shape, not a pinned revision or executed acceptance. Failed, absent or superseded record reads retain telemetry as well as the record and its time. No build, suite or live transport check was executed for this documentation update.

The second-pass brief was reported as listing targeted runs for Facts (12), HealthTick (4), UI bridge (73), Project (6), Cohort affordances (12) and Cohort.project (4), all with `NarrowedRun` TRUST verdicts. The brief and original logs have no resolved file citation here, so these counts remain unbound reported inner-loop evidence, not acceptance. The reported transcript records an unfiltered acceptance start, not a completed TRUST verdict; require retained original receipts before crediting any run.

Acceptance must cover older and duplicate snapshot refusal, conflicting content and foreign incarnation faults, frozen-read projection, presence-only Board updates, subscribe-and-snapshot races, coalescing under a slow writer, absence after failed projection, and epoch handover without publication identity changes. The second-pass controls are source-read evidence in `Bozzetto.Tests/BridgeFactsTests.fs` for Withdraw/re-Offer, retained result counts, overlap with a publishing thread, shutdown retirement and late `Retired` refusal, concurrent HealthTick refresh ordering and retained failed-record time. `Bozzetto.Tests/UiBridgeTests.fs` supplies bridge transport/projection controls, including increasing lease-stamp behavior. Their presence is not an executed result. These controls do not replace the remaining acceptance obligations for binary compatibility, age/provenance delivery, stamped resync and reconnect races, atomic receiving installation and shutdown cleanup at the delivery boundary.

Every new refusal needs a positive control, a negative control asserting the specific reason and a mutation caught by a test. Control and caught-mutation descriptions in `Bozzetto.Tests/BridgeFactsTests.fs` and `Bozzetto.Tests/UiBridgeTests.fs` are not evidence that mutation testing was executed. Any future async projection must add discarded late success, withdrawal and delayed physical-drain controls; the synchronous interpreter's settlement observations do not transfer automatically. Use targeted validation for the changed owner and consumers, followed by one unfiltered TRUST acceptance at integration as specified in [development.md](development.md).
