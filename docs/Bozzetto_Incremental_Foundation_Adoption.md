# Fidelity.FSharp.Incremental adoption contract

## Purpose and evidence boundary

Fidelity.FSharp.Incremental owns explicit dependency bookkeeping and the lifecycle protocol for demanded work. Bozzetto consumes that foundation to coordinate observations and owned work without becoming a compiler authority. Composer is a differential compiler. The term incremental refers here to the foundation and its values, not to a claim that every compiler operation reuses prior results.

Three components have distinct responsibilities:

| Component | Responsibility | What it does not establish |
| --- | --- | --- |
| Fidelity.PSG | Compiler-owned semantic and proof evidence, delivered under source authority. | Host admission or physical process drain. |
| Fidelity.FSharp.Incremental | Declared reads, shared demand, invalidation, attempts and result eligibility. | Completeness of compiler premises, proof validity or artifact acceptance. |
| Ranvier | The signal/memo/effect reference shape for reactive presentation in one mutation domain. | Foundation lifecycle certificates or compiler authority. |

This contract follows `AGENTS.md`, especially Architecture, Language and stack, and Validation posture. The owner's wire ruling is stricter than the existing general allowance for unavoidable JSON: direct interchange must be typed binary. See [component contracts](Bozzetto_Fidelity_Component_Contracts.md), [reactive push](reactive-push.md) and [architecture](architecture.md).

The implementation claims below rest on direct local reads of `src/Bozzetto/ResourceGraph.fs`, `src/Bozzetto/UiBridge.fs`, and the foundation's `src/Fidelity.FSharp.Incremental/API.fs` and `Core.fs`. Foundation paths refer to the Fidelity.FSharp.Incremental repository. No fresh retrieval snapshot, revision pin, executed test result or installed-daemon identity was obtained for this rewrite. Code inspection establishes implementation shape, not integration acceptance or deployment.

## Shared machinery, explicit owners

The portable core accepts an immutable `Command`, returns replacement state and ordered `Effect` values, and performs no evaluation or payload comparison. The caller serializes commands, commits successful state, then interprets effects in order. A rejected command supplies no new state or effects (`Core.fs`, `step`).

Each owner keeps its own graph. Using the same foundation does not imply a shared mutable graph across daemon and compiler processes. Bozzetto must not reconstruct the compiler's semantic graph from paths, diagnostics or artifacts.

| Owner | Obligations retained outside the foundation |
| --- | --- |
| Compiler, including Clef/CCS and Baker | Source meaning, complete premises, proof discharge and permission to reuse evidence. |
| Composer | Pipeline ordering, current-generation acceptance, artifact correspondence and final execution admission. |
| Bozzetto | Client and lease admission, checkout/session routing, worker fences, external process ownership and faithful presentation. |
| Fact producer | Immutable snapshot content, publication sequence, incarnation and acquisition policy. |
| Execution host | Fresh evaluator invocation, cancellation delivery, child ownership and physical cleanup. |

An `Offer` says that the declared dependency and lifecycle conditions hold. It cannot authorize PSG publication, proof reuse, native artifact acceptance or execution. Those decisions remain at their existing compiler-owned boundaries. Bozzetto presents accepted evidence; it does not manufacture it.

## Identities and immutable payloads

The public identity model is explicit (`API.fs`):

| Identity | Meaning |
| --- | --- |
| `EpochId` | One caller-owned core lifetime. |
| `ScopeId`, `RevisionId` | A closable definition owner and its reserved/open definition set. |
| `WorkId`, `DefinitionStamp` | Stable work lookup and a particular definition. |
| `InputId`, `InputStamp` | A source and a particular publication of that source. |
| `ReadSlotId` | One declared read occurrence, not a deduplicated source. |
| `DemandId` | One consumer's interest in work. |
| `AttemptId` | One execution attempt. |
| `EligibilityId` | One currently valid offer of a result. |
| `ValueToken` | An opaque reference to immutable owner-held payload. |
| `StepId`, `SuspensionId` | A workflow step and an exact one-use checkpoint. |

Numeric tokens have no semantic interpretation and are not authentication capabilities. They do not establish a worker's identity, distribution provenance or authorization. Compiler generations, daemon publication sequences and foundation revisions must remain distinct. External identities must survive local graph recycling without pretending that epoch-local handles remain valid.

The payload owner supplies a typed immutable store and a release policy. Tokens must not hide mutable compiler state, tasks or unowned native resources. A `ResultHandle` includes epoch, scope, revision, work, definition, attempt, eligibility and value. Admission must validate the complete handle, not just `WorkId` or equal output bytes (`Core.fs`, `isEligible`).

## Complete declared reads

Every definition supplies its complete ordered `Read` list. Multiple slots may read the same source. Deduplicating those occurrences changes the contract. `StartRequest.Reads` freezes the resolved receipts for that attempt; evaluation consumes those receipts rather than rereading mutable owners (`API.fs`, `Definition`, `ResolvedRead`, `StartRequest`).

The owner must declare source, configuration, target, policy and effect premises. Negative facts require an explicit versioned census or whole-revision input. For example, evidence that there are no additional uses cannot be represented solely by reads of the uses already found.

The foundation does not dynamically discover dependencies. Environment and response tokens for suspension do not register extra reads. Their semantic inputs must already be covered by declared reads and stamps. The registered work graph is acyclic; cycles need owner-defined decomposition or a separately agreed extension, not an implicit fixed-point mechanism (`Core.fs`, `validateReads`, `topological`, `replace`).

Input stamps are identity, not a monotonic freshness test. `SetInputs` treats an exact duplicate as unchanged and refuses reuse of a previous stamp for different input data; it does not reject every previously unseen smaller stamp (`Core.fs`, `setInputs`). Before admission, an adapter must validate owner sequence and incarnation under the same gate as state commitment. Dropping an older complete replacement snapshot is valid; dropping an older delta without reconciliation is not.

Correlated facts should be supplied as one immutable owner publication or one atomic `SetInputs` command. Pure projections must not sample telemetry, perform filesystem operations or reread owners. Acquisition belongs to the producer, which publishes a new immutable input or exposes the age of its last observation.

## Cold admission and shared demand

Cold work is an operation description that has not started. It is different from a cold build, which lacks reusable compiler results. Constructing a description or holding it in an unadmitted queue must not start compiler, solver, filesystem or device effects.

The core emits `Start` only for demanded work whose reads resolve, whose scope is open, and whose preceding attempts for the same work have drained. Producer dependencies require eligible results. Independent ready work is selected deterministically by identity, relative to the supplied command trace, not wall-clock completion order (`Core.fs`, `advance`).

Demand propagates through declared work dependencies. Each observing client needs its own demand identity. Releasing one demand cannot stop a producer still required by another direct or transitive consumer. Releasing the last demand can obsolete running work, but does not inherently evict an already eligible cached result. Retained, drained cache candidates can receive fresh eligibility without new execution or new demand (`Core.fs`, `demanded`, `advance`, `Release`).

Closing a view, cancelling an acknowledgement wait, cancelling shared work and closing an owning scope are different operations. Status and evidence reads do not implicitly request a build. Continuous daemon fact projections may keep daemon-lifetime demand; browser connections then observe them without owning that demand.

For asynchronous work, the preferred integration surface is the foundation's functional `AsyncMailbox`: cold creation, explicit start, retained admission operation, repeatable cold observation and explicit `WorkCancellation`. Its README documents that observer cancellation does not retract admission or excuse cleanup. This is integration guidance, not a claim that `ResourceGraph` or `Facts.Graph` uses that host: both interpret `Core` directly and settle pure work synchronously.

## Reservation, withdrawal and final authority

`ReserveScope` withdraws the scope's current results and affected reverse dependencies before requesting cancellation. `ReplaceScope` must match the reservation and supply complete membership. `Retain` preserves an existing definition; `Define` supplies a new definition stamp; omission removes work (`Core.fs`, `reserve`, `replace`, `invalidate`).

A drained cache candidate can be reoffered only when its ordered resolved reads match. Upstream attempt identity is part of those receipts. Revalidation produces a fresh eligibility handle at the current revision. Old handles stay revoked even if the displayed value is equal. Late pre-reservation completion cannot restore current authority.

For source edits, queue receipt is not permission. A successful acknowledgement must identify the exact request, workspace, epoch and revision, and confirm both host withdrawal/admission fences and the compiler-owned reservation. Physical cancellation may still be pending, with separate status. The client must reconcile a lost acknowledgement before writing. Cancelling its wait is not evidence that the reservation failed to commit.

Eligibility checking and an external effect's commit must have defined ordering with reservation and retirement. A check at description construction or queue admission is too early. Composer must serialize current-artifact validation and actual launch against reservation. An unprotected process start after `Core.isEligible` creates a race. See [Composer workflow](composer-workflow.md).

The required launch control pauses after scheduling, lets reservation commit, then releases the old operation: it must cause zero launches. The opposite ordering must preserve the joined lifetime of work that legitimately launched first. These are adapter and compiler-owner gates, not consequences of core bookkeeping alone.

## Completion, drain and retirement

The fresh-attempt lifecycle is demand, admission, start, terminal completion, physical drain, then conditional eligibility. `Finished` records an outcome; `Drained` certifies joined execution and resources. Only a successful, still-current, demanded attempt with unchanged revision and reads can enter the cache and produce an `Offer` (`Core.fs`, `finish`, `drain`, `advance`).

The host must account for every `Start`, including cancellation before evaluator invocation and synchronous invocation failure. Evaluators include owned children, cancellation callbacks and cleanup in completion. A wrapper workflow finishing cannot certify that its subprocess, callback or device resource stopped. Deadlines, effect disposal and cancellation requests are not physical-drain evidence.

Withdrawal is immediate loss of publication authority, not completed cleanup. Replacement for the same `WorkId` waits for prior drain; unrelated work may continue within capacity. Failed or cancelled current work needs `Retry` or changed premises, rather than an automatic retry loop. Preserve cleanup failures as evidence, including when the graph itself faults.

`CloseScope` removes its definitions, invalidates dependants and cancels attempts. `ScopeClosed` follows their drain. `Retire` seals the core epoch against new work while allowing terminal acknowledgements; `EpochDrained` marks completed core retirement (`Core.fs`, `close`, `retire`, `finishClosures`). Host admission closure and core retirement are separate obligations. No adapter may manufacture `Drained` merely to unblock replacement.

For scoped suspension, `AwaitingResume` holds one logical attempt with frozen original reads. The exact `SuspensionHandle` is consumed once. A step must release physical resources before yielding a resource-free checkpoint; stale, duplicate or foreign resumptions cannot execute another step (`API.fs`; `Core.fs`, `suspend`, `resume`). General resource-bearing migration is not established by this API.

## Implemented use: ResourceGraph

`src/Bozzetto/ResourceGraph.fs` contains two direct-core consumers. They demonstrate bounded synchronous derivation, not the complete asynchronous workspace/worker contract.

- The RAM graph declares separate reads for used and total RAM, establishes one scope and demand, and batches changed inputs with `SetInputs`. Its evaluator reads frozen tokens and computes occupied percentage without I/O. It settles `Finished` and `Drained` synchronously and retains only current input payloads.
- `AgentPublication` establishes a demanded identity projection for the cohort-owned work state. It swaps the published state on `Offer`, emits a unit change signal and starts a new local epoch every 256 writes.

Their limitations matter. The RAM implementation sets its separate `Result` field during `Start` and does not derive the returned metric from an eligible `Offer`. The agent evaluator publishes a captured `value`, rather than resolving a typed snapshot from `StartRequest.Reads`; its event carries no payload. These are shortcuts to remove when extending the consumer, not a general pattern for authority-bearing publication.

The RAM state's acquisition-window bound depends on its caller; this file alone does not establish the window lifetime. `AgentPublication` resets its settled graph without an explicit retirement handshake. Neither implementation demonstrates cancellation of real owned child work or a native resource join. Synchronous pure settlement permits immediate drain only because those evaluators acquire no such resources.

## Implemented use: the bridge fact plane

`Facts.Graph` in `src/Bozzetto/UiBridge.fs` is a fuller synchronous fact adapter. It declares stable projections once per epoch:

| Work | Declared inputs |
| --- | --- |
| Health | One health tick containing record, telemetry, machine figures and settled pressure level. |
| Leases | One complete lease-pool snapshot. |
| Agent board | Agent-work snapshot, lease pool and cohort membership, in distinct read slots. |

The graph holds typed immutable facts and result events behind tokens. Under one gate it admits owner publications, commits core state and interprets effects in returned order. `Start` resolves frozen receipts, stores the result, and acknowledges completion and drain. Only `Offer` delivers the result. `Withdraw` removes its stored result. Projection exceptions become failed completions and increment the fault count; later admission explicitly retries failed work.

Admission rejects older complete snapshots, exact duplicates and conflicting content at the same owner serial. Agent-work admission additionally rejects a foreign incarnation. That check is not implemented uniformly for every input: tick, pool and membership rely on their daemon-owner lifetime and sequence. Extending owner replacement needs an explicit incarnation contract, not reliance on core stamps.

Health, leases and the board retain daemon-lifetime demand. `Register` captures eligible current events and installs the page sink under the same gate. `Current` reads eligible handles, not arbitrary retained payloads. Without agent and membership inputs, the board cannot settle. These code properties support fact-plane connection reconciliation; they do not prove atomic reconciliation with the separately cached Composer directory.

After 4096 admitted sets, the graph retires the old epoch, checks `Core.isDrained`, and rebuilds from current inputs in the same critical section. Handover suppresses delivery and preserves work publication stamps. This bounds epoch history for these synchronous projections, not all daemon memory or arbitrary asynchronous workloads.

Remaining seams are explicit. Sink callbacks run under the graph gate; extensions must prevent reentrant mutation and keep callbacks short. In `src/Bozzetto/UiBridge.fs`, the stopping callback in `UiBridge.create` disposes owner subscriptions and calls `Facts.Graph.Stop()`. Stop seals admission, suppresses delivery, interprets `Action.Retire` and clears page sinks under the gate; Withdraw releases offered results, and late serial admission returns `Refusal.Retired`. Stop is idempotent and `Drained` exposes `Core.isDrained`. Immediate final-epoch drain is justified only by the current synchronous, resource-free projections. A future asynchronous evaluator must join its actual work and resources before reporting drain; this retirement path is not proof of arbitrary external cleanup.

## Ranvier boundary and wire drift

Ranvier's signal/memo/effect shape is useful for presentation settlement, but it is not interchangeable with this protocol. Ranvier tracks reads dynamically; the foundation requires complete declarations. Display equality cannot erase compiler proof identity or an upstream attempt receipt. An effect queue flush or cleanup callback cannot certify joined physical drain. See [reactive push](reactive-push.md) for the mutation-domain and settlement contract.

Neither inspected Bozzetto file establishes a Ranvier-backed implementation. The fact plane instead uses the explicit core as described above. It must not be documented as delivering every async-settlement guarantee of a reactive graph.

The required daemon-worker, daemon-browser and agent-owned interchange is typed binary. MCP JSON-RPC is the sole external-standard exception. Fidelity.Data belongs at export edges and the independent, MailboxProcessor-driven diagnostic sink, not as a replacement serializer on direct wires. See [binary formats](binary-format-spec.md), [MCP tools](mcp-tools.md) and [diagnostic sink](Bozzetto_Diagnostic_Sink.md).

Current browser transport is drift: `src/Bozzetto/UiBridge.fs` stores string frames, sends WebSocket text, and explicitly refuses binary frames while asking for JSON text. Its `/api/resources` response also uses `application/json`. Typed in-process events and foundation adoption do not make that wire binary. Binary replacement remains required, independently of the implemented fact-plane settlement.

## Capacity, coordination and self-hosting conformance

The coordinator owns short state transitions, not a whole build awaited inside its receive loop. Expensive work runs outside that loop and returns terminal observations. Ordinary admission must be bounded without starving reservation, cancellation, completion or retirement. Shutdown seals admission, settles or explicitly fails every accepted operation, invalidates resumptions and joins owned children.

Define separate limits for queued demand, active execution, closing children, payloads, diagnostics and identity history. Closing work still occupies physical capacity until joined. The core retains stamp history, released demand IDs and terminal completions for an epoch and rejects identifier exhaustion (`Core.fs`, `State`, `advance`, `suspend`). A bounded queue alone does not bound that history. Epoch replacement needs sealed admission, physical drain and atomic handover with preserved external identity.

Self-hosting is planned. A portable command processor, F# async authoring or .NET execution does not demonstrate Clef/native hosting. A replacement host must preserve exact command/effect traces and observable ownership while replacing CLR mechanisms. Native execution and distributed authority require their own acceptance evidence; library tokens do not supply authentication, placement, fencing or restart recovery.

Acceptance must cover the changed owner and its consumers, with positive and specific negative controls plus a caught mutation for each new rule. Required cases include:

- No effects before admission; complete reads including a changed census premise.
- Two consumers sharing a producer, with one leaving and the other continuing.
- Reservation before write; retained results with fresh handles and revoked old handles.
- Reservation winning between scheduling and actual launch, plus the opposite ordering.
- Late success after revision/epoch replacement; failure and delayed physical drain.
- Duplicate/stale resumption, changed inputs while suspended and close with outstanding children.
- Saturated admission with responsive control and terminal delivery.
- Older, conflicting and foreign owner publications; eligible snapshot plus subscription capture.
- Epoch recycling, retained payload bounds and a noncooperative real child process.

Portable replay tests are necessary but insufficient for host races. Controlled interleavings and real cleanup establish the execution boundary. Keep exact source/package/worker identities and evidence receipts in the handoff. Run targeted checks in the inner loop and one unfiltered TRUST acceptance per integration increment, under the required work lease. Only `Trusted` is the unfiltered acceptance verdict. See [development](development.md) and [operations](operations.md).
