# Architecture

Bozzetto is a development controller for Clef/Composer work. It admits and routes workspace operations, supervises a Composer worker, coordinates agents and leased work, and presents observations. It does not check the language, manufacture proofs, or grant native execution authority independently of the compiler.

Three components define the architecture together:

- **Fidelity.PSG** carries compiler-published semantic facts and their identities. Compiler owners decide what those facts establish.
- **Fidelity.FSharp.Incremental** owns declared dependency bookkeeping, demand, result eligibility and explicitly started work lifetimes. Eligibility is not compiler proof or permission to execute.
- **Ranvier** supplies the signal/memo/effect reference shape for the reactive push plane: admitted immutable inputs, derived views and effects within one mutation domain. This is a design reference, not a claim that Bozzetto runs Ranvier or inherits all its semantics. The owner's direction is that this shape and Fidelity.FSharp.Incremental pervade every Fidelity tool, Clef Auto Complete and Lattice included, so that design-time re-evaluation reacts to graph changes as narrowly as the compiler's own differential compilation. Bozzetto's fact plane (`src/Bozzetto/UiBridge.fs`, module `Facts`) is the first implementation of that direction; for the other tools it is direction, not delivery.

The governing rules are in `AGENTS.md`, particularly Architecture, Language and stack, Clef and Composer work, and Daemon lifecycle. The owner's wire ruling requires typed binary direct interchange. It supersedes broader allowances for external JSON in older hosting documents.

## Capability and evidence boundaries

“Implemented” below means present in the inspected source, not verified in an installed daemon. No service probe, build or test was executed for this document. The retrieval service could not be called with the available read/write tools; the explicitly named local sources were read directly without a fresh retrieval snapshot or pinned revision.

| Concern | Code-backed current state | Requirement or remaining boundary |
| --- | --- | --- |
| Local controller | `src/Bozzetto/DaemonMode.fs` composes one Composer supervisor and separate main and control listeners. | Installed identity and readiness require operational verification. |
| Worker transport | `src/Bozzetto/ComposerWorkerClient.fs` uses a local Unix-domain stream socket and the closed binary protocol. | Local transport does not establish remote-node support or delivery of published PSG revisions. |
| Foundation adoption | `src/Bozzetto/UiBridge.fs`, `Facts.Graph`, uses the foundation core for health, lease and agent-board projections. | This bounded use does not establish adoption across every worker, compiler or host lifetime. |
| Browser push | `src/Bozzetto/UiBridge.fs` implements change-driven WebSocket push and atomic fact subscription/current-value registration. | Its actual wire is JSON text and rejects binary frames. Typed binary browser interchange remains required. |
| Native migration | The inspected controller and protocol remain .NET-hosted; the protocol targets `net10.0`. | Clef-only hosting and LLVM ORC JIT are planned, not implemented capabilities. |
| Distribution | Worker addresses include host and epoch identities. | Authentication, remote fencing, durable reconciliation and real lab-node acceptance remain requirements. |

## Processes and listeners

The current topology is local. Sessions are workspace identities within the supervised Composer service, not a promise of a separate FSI process for every client session.

```text
Browser, editor and agent adapters
                 |
                 v
Bozzetto daemon, main listener 47749
  MCP, HTTP operations, /work, /dashboard, /ui/bridge
  workspace routing, lease admission, observations
                 |
                 | typed binary, local Unix-domain stream socket
                 v
Supervised Composer worker
  compiler-owned workspaces and acceptance gates
  accepted artifacts and owned execution

Bozzetto daemon, separate control listener 47750
  discovery, shutdown and browser redirects

Required independent diagnostic sink process
  binary PSG capture -> bounded persistence -> optional JSON export
```

The two listeners are separate ASP.NET Core applications in the same daemon process, not two daemons. `src/Bozzetto/DaemonMode.fs` starts the main listener on the selected MCP port and the control listener on that port plus one. The standard ports are 47749 and 47750. It checks for early listener completion before announcing readiness; that startup check does not replace a readiness probe.

`src/Bozzetto/ControlListener.fs` serves `/api/daemon-info` and `/api/shutdown` and redirects browser routes to the main listener. Its separate application allows control discovery to report main-listener failure. It is not an independent state authority or a guarantee of availability if the daemon process itself fails.

`src/Bozzetto/DaemonMode.fs` creates the Composer supervisor from configuration. `src/Bozzetto/ComposerSupervisor.fs` starts the worker lazily for an Open operation, using `BOZZETTO_COMPOSER_WORKER`; absent configuration produces an explicit unavailable result. Existing operations must address the active worker's host, epoch and provider. There is no implicit worker launch to satisfy an unrelated status or run request.

The browser is the Partas.Solid application in `src/bozzetto-web/`. `src/Bozzetto/UiBridge.fs` serves one welded bundle at `/work` and `/dashboard`, with one `/ui/bridge` WebSocket route. Both layouts use the same owner facts and supervisor. Build/validation leases and compiler work belong on Work; the Dashboard presents agents and machine resources. See [operations](operations.md) for deployment, health and accounting.

## Ownership, not a second compiler

The portable model is the ordering, authority and lifetime of operations. Hosting libraries and transport mechanisms implement that model but do not define Clef semantics.

| Owner | Responsibility |
| --- | --- |
| Clef / CCS / Baker | Source meaning, checking and settlement, proof premises, ABI facts and invalidation scope. |
| Fidelity.PSG | Identified published semantic graph facts and relationships under its owner-maintained schema. Structural integrity alone grants no proof or execution authority. |
| Alex | Passive composition of patterns and witnesses from published facts, without inventing semantic settlement. |
| Composer | Differential compilation, lowering, proof-dependent artifact acceptance, valid region/object reuse and admission of accepted execution. |
| Fidelity.FSharp.Incremental | Complete declared-read bookkeeping, shared demand, withdrawals, Offers and explicit completion/drain protocol. It cannot infer missing compiler premises. |
| Bozzetto | Client and lease admission, edit coordination, routing and epoch fences, worker supervision, owned cleanup and faithful observation. |
| Browser and editor adapters | Source interaction and presentation of owner-issued evidence, without a parallel semantic checker or execution authority. |
| Diagnostic sink | Independent capture and bounded export. It has no compiler or execution authority. |

These are ownership requirements, not proof that every consumer integration has passed. The durable seams are specified in [component contracts](Bozzetto_Fidelity_Component_Contracts.md).

Compiler tickets remain process-local. `src/Bozzetto.Composer/ProviderContracts.fs` requires the backend to create and consume them; the provider must not serialize or reconstruct them. A host/session/epoch address identifies a destination, not an accepted artifact or permission to run it.

Identity layers must remain distinct. `src/Bozzetto.Composer.Protocol/ProviderData.fs` distinguishes the adapter generation in `Authority` from the compiler generation in artifact/run payloads. Observation serials, foundation epochs and UI revisions are further separate identities. Equal numbers or equal display values do not collapse these domains.

## Operation ordering and work lifetime

Clients open an explicit `.fidproj`, reserve before editing, build against the reservation and execute only through `composer_run_current` or its equivalent typed operation. Reading diagnostics or status must not start a hidden build. See [Composer workflow](composer-workflow.md) for the client sequence, conflicts and evidence interpretation.

The required ordering is:

1. Admit an explicit request without starting its expensive effects while it is merely queued.
2. Commit reservation and withdraw affected publication and execution authority before acknowledging permission to write source.
3. Start demanded work through the shared foundation, using complete owner-declared reads.
4. Keep compiler acceptance separate from foundation eligibility.
5. Revalidate authority at actual artifact launch or commit, serialized against reservation and retirement.
6. Retain cleanup ownership until physical children and callbacks join, even after publication is withdrawn or a caller stops waiting.

`src/Bozzetto.Composer/ProviderContracts.fs` documents a current synchronous launch-prefix fence: `RunCurrentAsync` selects and launches before returning its task, ordered with Reserve by the adapter. Moving to cold work must preserve that ordering at the actual launch boundary, not merely validate a queued description. This interface requirement alone is not an executed race test.

The foundation does not discover reads dynamically. Consumers declare every relevant source, configuration, target and proof premise, including versioned evidence for negative facts. An Offer establishes eligibility under those declared dependencies and lifecycle conditions; Baker and Composer still decide proof reuse, publication and execution. See [foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md).

The supervisor's observed directory is not compiler authority. In `src/Bozzetto/ComposerSupervisor.fs`, stale status projections remove `Current`; retired-worker projections mark the worker unavailable. Responses from older generations or retired workers are refused rather than installed as current. Browser artifact rows derive witness and object states from worker metadata in `src/Bozzetto/UiBridge.fs`, `Project.artifactSummary`; the host does not generate those witnesses.

Cancellation and retirement have different scopes. `src/Bozzetto/ComposerWorkerClient.fs` targets caller withdrawal at the exact request identity for Build, Run and Format rather than silently substituting a session-wide Cancel. A page disconnect does not cancel a build or run in `src/Bozzetto/UiBridge.fs`; the explicit Cancel command changes shared work.

Worker retirement first fences observations and then stops the owned process. `src/Bozzetto/ComposerSupervisor.fs` requires observed process exit before releasing the worker slot, joins its monitors and retirement work, and retains failed-retirement evidence. `src/Bozzetto/ComposerWorkerClient.fs` joins socket work and stdout/stderr drains after exit. `src/Bozzetto/DaemonMode.fs`, `completeShutdown`, still attempts manifest and session cleanup if Composer termination fails. A deadline or cancellation acknowledgment is not proof of physical drain.

## Typed boundaries and wire drift

Direct daemon-worker, daemon-browser, control-client and agent-owned interchange must use versioned typed binary contracts. JSON is not an alternative encoding or compatibility fallback on those boundaries. Changing the JSON serializer to Fidelity.Data does not satisfy this requirement.

| Boundary | Inspected implementation | Architectural disposition |
| --- | --- | --- |
| Daemon to Composer worker | `src/Bozzetto.Composer.Protocol/Protocol.fs`, `BAREWireCodec.fs` and `StreamFrames.fs`; used by `src/Bozzetto/ComposerWorkerClient.fs`. | Implemented typed binary request/reply profile with bounded framing and explicit failures. No JSON fallback. |
| Daemon to browser | `src/Bozzetto/UiBridge.fs`, `WebSocketTransport`, sends UTF-8 text and explicitly rejects binary messages. | JSON text wire is drift to remove. Preserve typed commands, correlation, ordering and push ownership when replacing it with binary frames. |
| Control listener to local tooling | `src/Bozzetto/ControlListener.fs` encodes discovery and shutdown replies with Fidelity.Data JSON. | Direct-interchange drift, not an export exception. Replace the wire at its owning contract. |
| Client projections | `src/Bozzetto/ComposerClientJson.fs` projects replies into a shared MCP/browser JSON representation; `UiBridge.mapRoutes` also exposes JSON resources. | The MCP representation must remain confined to MCP. Browser and direct API use must move to binary. |
| MCP external protocol | JSON-RPC is required by the external MCP standard. | The sole external protocol exception. Decode/encode at the adapter; preserve typed internal ownership. See [MCP tools](mcp-tools.md). |
| Diagnostic/export edge | Independent MailboxProcessor-driven sink process is required. | Binary capture and bounded persistence; Fidelity.Data JSON only as optional export. Sink deployment is not established by the inspected supervisor logs. |

The worker profile checks the protocol version before interpreting its union, requires exact payload consumption and bounds announced lengths before allocation (`src/Bozzetto.Composer.Protocol/BAREWireCodec.fs` and `StreamFrames.fs`). The transport owner, not the framing functions, serializes writers and joins admitted I/O.

Worker compatibility and semantic identity are separate checks. `src/Bozzetto/ComposerWorkerClient.fs` validates the negotiated binary agreement and PSG schema, format version and contract fingerprint. `Protocol.fs` also carries compiler and PSG assembly identities. The hello's `PsgIdentity` describes the loaded semantic contract; it is not a published graph revision. Matching this handshake does not prove compiler parity, scoped PSG transfer or artifact validity.

Source-authorized, demand-scoped PSG delivery remains a separate contract: exact-base deltas, reauthorization and receiver-installation fencing must be demonstrated independently. Do not infer delivery from a successful control handshake or copy a full retained graph to work around a missing scoped transport. PSG and BAREWire schema authority stays with their owners; see [component contracts](Bozzetto_Fidelity_Component_Contracts.md) and [binary formats](binary-format-spec.md).

The `.bozzettofm` manifest is durable controller state, not a live transport or permission to resume execution. `src/Bozzetto/DaemonMode.fs` routes writes through `Features.ManifestOwner` and reports unreadable bases rather than overwriting history. CRC and structural validation remain distinct from semantic authority.

## Foundation-backed, Ranvier-shaped push

The intended reactive shape is owner input, derived projection, then delivery effect within a single mutation domain. Ranvier is the reference for this separation, not an additional compiler authority or dependency graph competing with the foundation.

A bounded implementation exists in `src/Bozzetto/UiBridge.fs`, `Facts.Graph`:

- Owners publish complete immutable health, lease, agent-work and membership values with their own serials.
- The supervisor also publishes its bounded, memory-only journal of every caller's settled non-status Composer operations, including refusals and run output, as the fifth input; the fourth work derives protocol 8 `operations` for the Work page's Activity and Run output tabs, with adapter-reported caller labels rather than authenticated identities (`src/Bozzetto/ComposerJournal.fs`, `src/Bozzetto/ComposerSupervisor.fs`, `src/Bozzetto/UiBridge.fs`, `src/bozzetto-web/src/Shared/Protocol.fs`, `src/bozzetto-web/src/Frontend/App.fs`).
- Admission refuses older publications, distinguishes exact duplicates from conflicting equal serials, and fences foreign agent-board and journal incarnations.
- Foundation work definitions declare their reads explicitly. Pure synchronous projections report Finished and Drained and deliver through Offers.
- Sink registration and retrieval of current fact values occur under the same gate, preventing a gap between current-state delivery and later fact publications.
- Epoch recycling retires and checks foundation drain before rebuilding the bounded projection graph, preserving public projection stamps during handover.

This use can acknowledge synchronous projection drain because the work has no outstanding physical children. It does not justify doing the same for compiler processes or device work. A memo's equal rendered value cannot erase changed proof identity, and effect cleanup cannot certify physical drain.

`src/Bozzetto/HealthTick.fs` sequences machine and daemon observations into immutable publications with separate sample and record ages. It installs no timer of its own. The bridge derives health from that publication rather than racing independent samplers or inferring pressure from a display value.

`src/Bozzetto/UiBridge.fs` coalesces supervisor-change notifications and reads the cached directory for pushes. Each socket has one writer; command answers preserve order and pushed state uses latest-value slots. Initial fact registration is atomic, but the Composer directory and fact values are obtained separately. This is not evidence of atomic reconciliation across the whole reconnect state. Incarnation/version-aware atomic reconnect reconciliation remains a requirement in [reactive push](reactive-push.md).

Do not claim that the whole daemon is free of polling. `src/Bozzetto/ComposerSupervisor.fs` still polls status at 100 ms while reconciling pending work, and `src/Bozzetto/DaemonMode.fs` retains legacy status polling and timers. These are explicit adoption/removal seams, not the required UI pipeline. Replace observation polling at the owning publication boundary without losing cleanup escalation. An owner-driven uptime clock or requested resource acquisition is distinct from polling domain state for changes.

## Portable hosting and native migration

The current .NET 10 host provides process, socket, file, cancellation and ASP.NET APIs. Native migration must preserve portable contracts rather than translate CLR mechanisms into language requirements.

| Host facility | Contract the replacement must preserve |
| --- | --- |
| Async/task adapters and coordination primitives | Cold admission, explicit demand, one terminal completion classification, separate drain and joined cleanup. |
| Process and stream APIs | Actual launch fencing, process identity, bounded framing, owned diagnostic drains and visible termination failure. |
| File and watcher APIs | Reservation before source mutation, source identities and truthful lost-event/refusal reporting. |
| HTTP/MCP/browser adapters | The same typed operations and authority checks; MCP's external JSON-RPC exception must not leak into internal wires. |
| Diagnostic persistence | Bounded independent observation, export-only JSON and no semantic authority. |

F# implementation tooling is not a Clef semantic dependency. Native hosting must not depend on FSI, reflection, CLR object identity or managed runtime patching. Separate retained tooling at its owning packaging boundary instead of recreating it as a native compiler obligation.

LLVM ORC JIT is the intended future Clef execution backend and is not implemented, as governed by `AGENTS.md`. Native interactive execution, state/code retirement and target transitions each need compiler-owned settlement and real acceptance. A process restart is not proof-preserving hot replacement, and a native artifact launch is not a native REPL.

Clef-only deployment without .NET is the migration exit requirement. Portable command/effect replay must be supplemented by physical ownership tests for delayed children, callbacks, cancellation and failed drain on the native host. Native host migration and ORC delivery are separate gates; neither implies the other. See [development](development.md) for acceptance and differential controls.

## Distributed lab nodes: approved topology and delivery requirements

The owner has approved distributed FFI build work with the workstation's Bozzetto as controller and the existing Pi VMs on ONE, TWO and THREE as headless Bozzetto nodes. Each node hosts its canonical primary repositories, local admission and owned build processes alongside Pi. Manager retains Forgejo, retrieval and mesh/proxy/OpenNebula housekeeping; it is not a build node. ONE is the first resource and execution trial. TWO and THREE remain available at their current allocations for scoped agent work until ONE's acceptance supports rollout.

Composer denotes the compiler. Bozzetto's host adapters are compiler workers and their interchange is the worker contract; host monitoring is the Work view. The workstation's existing compiler/build/lease surface becomes that consolidated Work view; a separate Controller page is not planned. Remote rows must identify the executing node and its incarnation, distinguish connectivity from readiness and admission, show source/toolchain identities and worker-reported outcomes, and keep cancellation requested, acknowledged and physically drained distinct. Dashboard remains the agent and machine-resource view. Remote nodes do not serve a second monitoring UI. Disconnected observations remain visibly unavailable or stale, never an inferred successful cancellation or a free remote slot. A node selector changes the focused resource, lease and job observation while retaining the consolidated cross-node work summary. Each node owns a scoped Observe subscription: registration captures the current eligible snapshot and subsequent pushed changes under the same owner gate. Observation does not demand a build. Replies carry node identity, incarnation, publication sequence and observation age; reconnect reconciles those identities before presenting current values.

Each Pi agent connects to its own headless Bozzetto's MCP adapter and discovers the complete session- and role-scoped affordance set offered by that instance. The controller must not substitute a fixed tool subset or present another node's local capabilities as its own. Worker enrollment binds that MCP instance, local admission and observation to one node incarnation.

The worker owns its local lease pool and actual process lifetime. The controller requests named work and presents worker-issued observations; it cannot turn a workstation lease into remote capacity or accept an old node incarnation's result as current. Managed tooling builds and compiler-owned Composer operations retain their respective admission and acceptance boundaries. A generic build command must not impersonate a Composer Build or confer permission to execute a native artifact.

This extends the existing ownership model. The topology is an owner decision, not a capability established by local Unix sockets or a Host field in a reply. Authenticated typed binary control, durable operation reconciliation and consolidated remote presentation still require implementation and acceptance. LAN or Cloudflare mesh reachability grants no authority.

A bounded distributed workflow must establish:

1. **Enrollment and compatibility.** Authenticate nodes and callers, authorize named operations and negotiate typed binary contracts and compiler/PSG identities.
2. **One workspace compiler owner.** Distinguish forwarding to an owner from creating an independent workspace. Exchange identified source snapshots, artifacts and scoped evidence without presenting a second checker as the same session.
3. **Distinct destinations.** Identify build host, execution host, device and compilation target separately. Local paths, PIDs and symbols are not universal artifact identities.
4. **Effect-boundary fencing.** The executing host rejects obsolete ownership at actual launch or activation. Workstation lease expiry does not prove remote work stopped.
5. **Durable reconciliation.** Exact operation identities survive lost replies, retries, disconnects and node restarts. Reconnection recovers authoritative running and activation state without duplicating effects.
6. **Bounded admission and cleanup.** Ordinary queues cannot strand cancellation, completion or retirement. Report requested, acknowledged and physically completed cancellation separately.
7. **Resource locality and observations.** Keep resident datasets near execution resources where permitted; distinguish utilization, pressure and admission headroom. Preserve freshness and provenance in pushed views.
8. **Truthful target transitions.** Declare whether a target supports drain-and-replace, restart, reset, reprogramming or state migration. Partial activation and failed recovery remain explicit; rollback requires target evidence.

Bozzetto coordinates development jobs and application transitions. It must not become a WAN instruction scheduler or assume ownership of language/runtime scheduling. Shared memory does not establish addressability, coherence or permission across CPU, GPU or NPU participants.

Distribution requires an owner-approved workload and budget. Acceptance must use real owned sites and selected hardware, compare admitted behavior with fresh reference builds, and exercise lost acknowledgment, duplicate request, node restart, stale-owner activation and blocked cleanup. Two localhost processes or mock transport prove narrower properties only.

## Excluded surfaces and navigation

Embedded F# session hosting, FSI/REPL warmup and F# session hot reload are retired. `src/Bozzetto/DaemonMode.fs` refuses creation/restart through the retired provider boundary and disables previous-session resume. Retained warmup, live-testing, SSE and terminal-related plumbing in that file is removal work, not a supported workflow to recover.

Harmony, MonoMod, detours and injection are excluded. Datastar, the Raylib GUI, the Visual Studio extension, SageTUI and terminal rendering are not current product surfaces. Retained Raylib application/game demos are distinct from the removed GUI. SSE is not the browser UI pipeline; MCP transport compatibility does not reinstate it as an internal architecture.

Continue with [component contracts](Bozzetto_Fidelity_Component_Contracts.md), [foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md), [reactive push](reactive-push.md), [Composer workflow](composer-workflow.md), [agents](agents.md), [operations](operations.md) and the [independent diagnostic sink](Bozzetto_Diagnostic_Sink.md). These contracts must describe current implementation, explicit drift and requirements separately, without turning an architectural destination into a delivery claim.
