# Fidelity component contracts

Bozzetto is the development controller for Clef/Composer. It routes client demand, supervises the Composer worker and owns host admission and process lifetimes. Composer is a differential compiler. Bozzetto remains outside the compiler pipeline and does not establish language meaning, proof validity or native execution authority.

Three components define the integration:

- **Fidelity.PSG** carries immutable, source-authorized compiler facts and their identities. It is not a host-generated semantic graph.
- **Fidelity.FSharp.Incremental** owns declared dependency bookkeeping, demand, invalidation and explicitly started work. Its eligibility results do not grant compiler authority.
- **Ranvier** supplies the signal/memo/effect reference shape for reactive presentation under one mutation owner. It does not replace compiler evidence or the foundation's work-lifetime protocol.

The governing host rules are in `AGENTS.md`, especially Architecture, Clef and Composer work, Language and stack, and Validation posture. The owner’s direct-interchange ruling is stricter than that file's general allowance for unavoidable text serialization: direct interchange must be typed binary.

## Evidence and capability boundary

Implementation statements below are based on local source reads, not executed acceptance or installed-daemon inspection. The available tools could not request a fresh retrieval schema or determine a checkout revision. Directory reads failed; project files supplied the bounded source roster. No claim here establishes a promoted distribution, running identity or whole-pipeline conformance.

| Boundary | What the inspected code establishes | What it does not establish |
| --- | --- | --- |
| Worker control transport | Closed protocol, explicit BAREWire codecs and Unix-domain socket transport in `src/Bozzetto.Composer.Protocol/Protocol.fs`, `BAREWireCodec.fs`, `StreamFrames.fs` and `src/Bozzetto.Composer/Program.fs`. | Scoped PSG delivery or receiving proof/native acceptance. |
| Foundation use | `src/Bozzetto.Composer/ProviderSession.fs` uses `AsyncMailbox`, explicit scopes, demands, release, eligibility checks and close joins. `src/Bozzetto.Composer/Bozzetto.Composer.fsproj` requires the selected distribution's core and Hosting assemblies. | Complete adoption across every owner, portable native conformance or installed assembly equality. |
| Compiler handoff | `src/Bozzetto.Composer/ComposerAdapter.fs` delegates reserve/build/run to Composer's `ProjectSession` and copies accepted witness/object receipts. | Independent verification of Composer internals or arbitrary source-region execution. |
| Browser/client wire | `src/Bozzetto/UiBridgeCodec.fs` uses Fidelity.Data JSON; `src/Bozzetto/ComposerRoutes.fs` accepts and returns JSON and retains an SSE change endpoint. | Compliance with typed-binary direct interchange. These paths are drift to remove. |
| Worker compatibility | `src/Bozzetto.Composer/Worker.fs` checks the wire agreement and reports loaded compiler/PSG identities. `src/Bozzetto/ComposerWorkerClient.fs` also compares PSG against daemon-loaded constants. | D1 separation or manifest-bound distribution selection. The daemon comparison remains drift. |
| Native hosting and distribution | The inspected provider is a .NET host; worker and protocol projects target `net10.0`. | LLVM ORC JIT, a self-hosted Clef controller or authenticated distributed lab-node authority. These remain planned or required. |

See [architecture](architecture.md) for process boundaries and [operations](operations.md) for installed identity and deployment. Historical package pins and gate counts are not current capability evidence.

## One owner for each fact

| Owner | Authority retained there | Bozzetto's obligation |
| --- | --- | --- |
| Clef specification and CCS/Baker | Language meaning, lookup support, reachability, semantic closure, complete premises, settlement and reuse justification. | Carry current facts and explicit refusals; never reconstruct them from paths, output or display state. |
| Fidelity.PSG over BAREWire | Immutable scoped representations, authorization/delta declarations and structural validation. | Preserve source identity and typed reference roles; do not turn structural validity into semantic truth. |
| Alex | Passive witnessing of source-authorized occurrences and settled boundaries. | Present its receipts without moving pruning, adapter construction or dependency inference into Alex or the host. |
| Composer | Source capture, proof discharge, artifact correspondence, accepted publication and actual execution admission. | Delegate compiler operations and preserve its ordering and refusal boundaries. |
| Fidelity.FSharp.Incremental | Explicit reads, demand, invalidation, work eligibility and hosted work-lifetime machinery. | Map owner identities and retain actual cleanup responsibility at external resource edges. |
| Ranvier-shaped presentation owner | Signals, derived display state and subscription effects in one mutation domain. | Admit immutable owner observations and push typed views without minting compiler authority. |
| Compiler/toolchain distribution owner | Reviewed immutable dependency closure and compatibility evidence. | Select exact bytes, fence replacement and verify the serving worker against the selected manifest. |
| Fidelity.Platform, BAREWire and native binding owners | Target capabilities, layout/extent/ABI facts and resource obligations. | Route only compatible authorized operations; do not infer capability from reachability or ownership from missing metadata. |
| Runtime and device owners | Application scheduling, actual completion, live resource ownership and safe retirement. | Coordinate development operations without creating a competing application scheduler. |
| Bozzetto | Client routing, leases, supervised processes and faithful observations. | Keep host admission distinct from compiler acceptance and resource-owner authorization. |

Shared foundation use does not imply a shared mutable graph across processes. Each owner retains its state and exchanges versioned typed requests or immutable observations. A diagnostic sink, browser or agent is a consumer, not a second compiler authority.

## Identity, proof and eligibility

The following distinctions are requirements for component handoffs, not a claim that every field is present in today's worker wire:

| Identity | Required distinction |
| --- | --- |
| Workspace/input snapshot | Explicit project, configuration, dependency closure, ordered source bytes and overlays. A pathname is not a snapshot. |
| Compiler distribution/worker incarnation | Exact loaded closure versus the live host/epoch. Rebuilding source does not replace a worker. |
| Adapter/compiler generation | Host coordination revision versus compiler generation and source version. |
| PSG observation | Checked revision, scope content version, occurrence/traversal identity and subscription cursor remain separate. |
| Proof context | Complete source premises, target capabilities, arithmetic/effect policy, lowering assumptions and checker/discharge identity. |
| Artifact/composition | Exact accepted bytes, producer, dependencies and compatible artifact set. |
| Execution/resource | Actual invocation, active artifact set, resource grant, device/reset incarnation and physical lifetime. |
| Delivery | Exact resident base, proposed change, current authorization and verified installed base. |

Today's `Authority` and `AcceptedArtifact` are defined in `src/Bozzetto.Composer.Protocol/ProviderData.fs`. The authority generation is explicitly distinct from compiler generations in artifact/run payloads. `SessionSnapshot.Observation` orders actual status captures; it grants no compiler permission. `Protocol.fs` likewise identifies a worker address as a host/epoch address, not artifact or run authority.

An `Offer` or eligible foundation result means only that declared dependency and lifecycle conditions hold. Compiler owners must still establish complete premises, current proof, accepted publication and lawful execution. A proposition, its discharge receipt, an Alex witness and an accepted object are different evidence. Equal source text, ABI shape, output value or content hash cannot substitute for those checks.

Complete declared reads include negative support: lookup absence, collection membership, rule/declaration identity and joint participants where applicable. The foundation does not dynamically discover these premises. Until precise support exists, conservative source-owner revalidation is valid; a consumer must not invent a narrower dependency account.

Compilation, transfer, installation, activation and observed execution are separate facts. Packaging or signing may change bytes and must retain its derivation from the accepted artifact. Compiler epochs and execution lifetimes also differ: an edit can forbid new invocations while old frames or device jobs still retain code and buffers.

See [foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md) for reads, Offers and demand, and [Composer workflow](composer-workflow.md) for per-region witness/object validation.

## Source-authorized scoped PSG delivery

This section is the required delivery contract. The current worker operation union in `src/Bozzetto.Composer.Protocol/Protocol.fs` contains project control and formatting operations, not scoped PSG attachment or delta subscriptions. Binary project control must not be described as delivery of this contract.

### Source selection and representation

Full retained-graph transmission is prohibited on initial attachment, update and reconnect. Soft-deleted node bodies and source-private analytical hyperedges must not cross the service boundary. Increasing a frame cap, filtering a complete `Revision` at the receiver or debouncing whole-graph sends is not compliance.

An `open` changes lookup visibility. It neither demands every library member nor authorizes eager mapping of that library. A visibility change can alter binding selection or an absence judgment, which the source owner must reconsider. Actual references demand only their necessary semantic closure; unrelated settled content stays resident.

CCS/Baker owns scope closure and accepted-revision authorization, including all relevant support. Publication copies source-settled facts; it must not become a second place that computes semantics. Fidelity.PSG validates the structural declarations over BAREWire without a compiler reference or semantic inference. Alex follows source-authored paths and settled facts passively.

A payload must distinguish:

- Live executable occurrence bodies.
- Settled imported boundary facts and module context headers.
- Current propositions and their separate discharge receipts.
- Historical identity references that do not require a delivered body.

Definition, storage and activation owners need complete replacement and retirement accounts, including splits and merges. Source-private history and whole-domain premise support may remain retained at the source without being emitted. Public consumers must use source-authored typed accounts, not request analytical edges back to reconstruct missing facts.

The normative source ownership basis is the clef-lang-spec repository, `spec/program-hypergraph.md`, §5–5.1, at the revision selected for the contract. The stronger ban on full retained service transfer is an owner requirement. This audit did not independently read or pin that external specification; no unspecified transport checkpoint serves as current evidence.

### Attachment, delta and reauthorization

1. **Attach by demand.** Obtain only requested, source-authorized scopes. A fresh attachment is not a full-corpus recovery path.
2. **Identify the exact base.** Each update names the receiver's resident delivery base and the affected additions, replacements, withdrawals and retirements.
3. **Refuse incompatible continuity.** Missing/foreign bases, reversed updates and foreign session, epoch or subscription identities produce explicit diagnostics with no partial installation. Recovery remains demand-scoped.
4. **Reauthorize retained content.** Unchanged bytes require explicit current source authorization after complete support validates. A hash, schema, producer name or structurally valid catalog cannot grant that permission.
5. **Account for installation.** An acknowledgement advances only a verified installed delivery base. It grants neither proof nor execution permission and cannot restore withdrawn authority.

Checked revision, content version, traversal identity and delivery cursor must not be collapsed. Source-owned correspondence must preserve unrelated identities; equal encoded sizes or body counts do not prove identity continuity or unchanged-body reuse.

### Reservation and installation fences

Resident bytes, a pending packet and current source eligibility are distinct states. Edit reservation withdraws affected eligibility before source-write permission. Retained bytes may remain as an ineligible base for a later exact-base delta.

Authorization must be checked at staging and at receiver installation, with defined ordering against reservation and retirement. Encoding while authorized and waiting later for writer admission is insufficient: reservation may overtake the packet. A late packet or acknowledgement cannot reintroduce authority withdrawn by a newer reservation. Composer separately fences actual execution.

Edits must not wait for arbitrary browser acknowledgements. Observation cancellation does not certify physical join. Close and retirement retain ownership until admitted work, callbacks, socket activity, process exit and required diagnostic drains join; cleanup failures remain observable.

[Binary formats](binary-format-spec.md) identifies the owning PSG/BAREWire schemas. This document defines seams, not a duplicate codec or schema authority.

## Foundation lifetimes and Ranvier-shaped push

The foundation carries cold work descriptions through explicit admission and start. Constructing or queueing work must not start compiler, filesystem, solver or device effects. Withdrawal removes eligibility before cancellation is requested. Releasing one consumer's demand must preserve a producer still needed by another.

`src/Bozzetto.Composer/ProviderSession.fs` implements provider-local scopes and demands through `AsyncMailbox`. Build/run definitions use `Reads = []` because the provider delegates the compiler operation; this is not evidence that compiler proof dependencies are empty. Publication checks both provider generation and foundation eligibility. Reservation observes committed withdrawals before returning its token, and the invocation semaphore orders backend reserve against actual run invocation.

`src/Bozzetto.Composer/ProviderContracts.fs` keeps compiler tickets process-local and requires `RunCurrentAsync` to select and launch synchronously before returning its task. `ComposerAdapter.fs` delegates that boundary to Composer. Moving to a cold native host must preserve the actual start/commit fence, not merely recheck a queued description.

Provider close joins foundation close, owned controls/observations and formatting before backend disposal, retaining failures (`ProviderSession.fs`). Worker socket shutdown seals and retires native tools (`Program.fs`); daemon worker stop joins process exit, transport work and redirected output (`src/Bozzetto/ComposerWorkerClient.fs`). These are code-read mechanisms, not executed proof of every drain path.

Ranvier's signal/memo/effect shape applies to the observation plane:

- One mutation owner admits immutable inputs from fact owners.
- Memos derive display values; effects own subscriptions and typed push.
- Equal display values may suppress redundant rendering, but never erase changed proof, artifact or authorization identity.
- Effect cleanup may detach a subscription, but cannot certify compiler/process/device drain.
- Reconnect reconciles a coherent owner snapshot and cursors atomically before applying subsequent updates. It cannot restore old authority from cached display state.

Ranvier adoption and reactive settlement require their own code and acceptance evidence. No implementation claim follows from resemblance to its model. See [reactive push](reactive-push.md) for the presentation contract.

## Typed direct interchange and diagnostic isolation

| Boundary | Required contract | Current evidence or limit |
| --- | --- | --- |
| Daemon to worker | Closed versioned typed-binary requests/replies, exact agreement and authority fences; no JSON, stdio or anonymous-pipe fallback. | Implemented socket path in `ComposerWorkerClient.fs` and `src/Bozzetto.Composer/Program.fs`; explicit codec/framing in `src/Bozzetto.Composer.Protocol/`. |
| Daemon to browser | Typed-binary pushed observations and commands over the bridge, with coherent reconnect reconciliation. | JSON remains in `src/Bozzetto/UiBridgeCodec.fs`. This is drift even though it uses Fidelity.Data. |
| Agent-owned direct APIs | Typed-binary contracts and owner identities. | JSON Composer HTTP requests/replies remain in `src/Bozzetto/ComposerRoutes.fs`; its SSE endpoint is not the required UI pipeline. |
| MCP external boundary | JSON-RPC as required by the external MCP standard; translate to typed owner operations. | The sole external interchange exception. It does not permit JSON between internal owners or agent-owned direct peers. |
| Diagnostic capture/export | Binary source-owned PSG capture to an independent MailboxProcessor-driven sink process; Fidelity.Data JSON only as export. | `src/Bozzetto.Composer/DiagnosticCapture.fs` requests source-prepared occurrence capture and delegates to the diagnostic client. This read alone does not prove process isolation or sink conformance. |

Strings representing source, arguments or output inside a typed binary envelope do not make the envelope a JSON wire. Conversely, wrapping JSON in binary bytes or changing its serializer does not establish a typed contract. Opaque metadata must retain its owner's schema rather than become an alternate authority.

The sink has no compilation, proof, publication or launch authority. Capture refusal, persistence success and export success are diagnostic facts only. Bound retained data, queues and drains independently from compiler admission. See [diagnostic sink](Bozzetto_Diagnostic_Sink.md) and [MCP tools](mcp-tools.md).

## Wire compatibility and distribution identity

D1 requires separation between what the daemon interprets and what the worker loads:

- The daemon checks the exact worker wire protocol, encoding and contract digest it understands.
- Worker-loaded PSG schema, binary format, fingerprint and assembly bytes are checked against the selected reviewed distribution manifest, not against the daemon's own PSG assembly.
- The selected manifest binds the complete compiler/toolchain dependency closure. One compiler DLL hash or version string is not the closure.
- A content hash identifies bytes. It does not identify who approved them. In an environment where agents can write the filesystem, an approval file is not human-only authorization.

`src/Bozzetto.Composer/Worker.fs` already separates the wire `Agreement` from reported compiler and `PsgIdentity` data. However, `src/Bozzetto/ComposerWorkerClient.fs` still compares `hello.Psg.Schema`, `FormatVersion` and `ContractFingerprint` to daemon-loaded PSG constants. That coupling contradicts D1 and must be removed at its owner while replacing it with manifest-bound checks. Dropping checks without establishing the selected identity is not the repair.

**Current deployment rule:** replace daemon and worker as a matching pair from one selected closure, verify the PSG contract fingerprint and perform a Composer open. The existing coupling in `src/Bozzetto/ComposerWorkerClient.fs` is not compatible worker-only replacement; see [operations](operations.md) for the current procedure.

**Required D1 destination:** while the wire stays compatible, permit worker replacement without daemon replacement. Compiler sessions still end. Preserve the shared project list and reopen fresh sessions so clients return to the same projects under new authority; never carry old reservations or accepted-run permission into the new epoch. Project-list restoration is a requirement here, not a verified current implementation.

If the interpreted wire changes, validate and deploy a matching daemon-worker pair. Compiler component acceptance and controller integration acceptance remain separate; a shared dashboard switch does not gate compiler acceptance. Use the current restart and active-work policy in [operations](operations.md) and `AGENTS.md`, not superseded restart prohibitions from correction records. Retain reviewed rollback bytes and report the exact serving identity after replacement.

## Distributed authority and native destination

Distributed lab-node operation is a requirement, not delivered capability established by these source reads. `src/Bozzetto.Core/BozzettoConfig.fs` restricts configured binding to loopback. Its retained F# execution wording is stale; it does not restore embedded F# hosting. Changing a bind address or adding a tunnel does not implement federation.

A distributed operation must distinguish workstation, compiler owner, build/package host, execution host, device and transport route. Required boundaries include:

- Authenticated enrollment, project/device authorization and separately reported reachability, compatibility, capacity and readiness.
- Stable operation identity and expected target state, with enough owner-retained status to reconcile lost replies, duplicate delivery and restarts.
- Resource-owner-enforced grants and fencing tokens. Stale tokens cannot authorize queued physical effects after ownership changes.
- Independent compiler epoch, node incarnation and device/reset fences, including invalidation of affected local handles.
- Explicit disconnected-node policy for already-admitted work, refused new work, renewal and recovery.
- Separate observations for cancellation request, acknowledgement, process exit, device drain and resource release.

A timeout is uncertainty, not proof that an effect failed. Do not blindly repeat uncertain activation or device work. Failover must establish a new owner and fence the old one. Local expensive-work leases do not establish distributed resource authority.

Remote values carry declared layouts, extents and ownership, not raw local pointers. Shared physical backing does not establish mappings, visibility or completion. Live frames, closures, callbacks and device jobs retain their code/resources until the appropriate owner establishes retirement. Unsupported state migration or activation boundaries must produce a supported restart requirement or an explicit refusal.

LLVM ORC JIT is the intended future Clef backend and is not implemented under the governing host rules. Embedded F# session hosting and runtime patching are retired, not fallback execution mechanisms. Native self-hosting must preserve the same owner contracts and demonstrate physical lifetimes without relying on CLR objects as semantic authority.

## Acceptance at the seams

Each owner supplies conformance evidence for its boundary. A local codec round trip, successful upload or filtered suite cannot close a receiving compiler/host contract.

| Seam | Discriminating acceptance required |
| --- | --- |
| Scoped demand | Unused library growth and unused `open` add no delivered/mapped bodies; actual references deliver only necessary scoped facts. |
| Support and reuse | Lookup/absence changes revoke affected judgments. Unchanged builds resend no settled bodies; a one-callable edit preserves another's facts, witness and object only after current source reauthorization. |
| Delivery fencing | Wrong base, reversed update and foreign session/epoch/subscription are refused atomically. Reservation overtakes held delivery and late acknowledgements without restoring authority. |
| Receiving compiler path | The actual received scope packet feeds independent proof discharge, passive witnessing and native artifact/execution checks. |
| Lifetime | Last-demand withdrawal, delayed callbacks, split/merge retirement, socket failure and cleanup faults retain owners through actual joins and expose failures. |
| Compatibility | A compatible wire admits a manifest-approved changed worker PSG; incompatible wire and wrong loaded distribution bytes are refused for their specific reasons. |
| Distributed recovery | Lost acknowledgement, duplicates, node restart, device reset and stale fencing cannot cause obsolete activation or an unobserved second effect. |
| Native hosting | Portable contract traces and physical ownership gates pass in the deployed native host; .NET code resemblance is insufficient. |

For each new rule, provide a positive control, a negative control asserting the specific refusal and a mutation the test catches. Integration requires one unfiltered acceptance run per increment and its `TRUST` evidence where the Bozzetto harness applies. Record matched producer/contract/consumer identities, actual packet/artifact receipts and installed identities separately. See [development](development.md) for the evidence ledger and handoff procedure.
