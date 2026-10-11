# Bozzetto documentation

Bozzetto is the development controller for Clef/Composer work, outside the compiler pipeline. It coordinates human and agent sessions, supervises the Composer worker, grants build and test leases, and presents compiler-owned evidence without creating proof or execution authority. Composer is a **differential compiler**: it recompiles what a change affects. The forward remit brings **Fidelity.PSG**, **Fidelity.FSharp.Incremental** and **Ranvier** together as the evidence, dependency/work-lifetime and reactive-presentation centerpieces, respectively. The governing operating rules are in [`AGENTS.md`](../AGENTS.md); the target is `net10.0`, with one daemon on 47749 for MCP, HTTP and browser UI and 47750 for control.

## Navigation

These twelve documents form the target documentation set. A link names the durable destination, not a claim that its rewrite or the capability it describes has been delivered.

| Document | Purpose |
| --- | --- |
| [README.md](README.md) | Remit, navigation, the three centerpieces and current-versus-planned boundaries. |
| [architecture.md](architecture.md) | Daemon, control listener, supervised worker, ownership and process boundaries, with native migration and distributed lab-node requirements. |
| [Bozzetto_Fidelity_Component_Contracts.md](Bozzetto_Fidelity_Component_Contracts.md) | Compiler/host seams, source-authorized scoped PSG delivery, proof versus eligibility, compatibility and distributed authority. |
| [Bozzetto_Incremental_Foundation_Adoption.md](Bozzetto_Incremental_Foundation_Adoption.md) | Foundation identities, complete declared reads, cold admission, shared demand, Offers, withdrawal, drain and self-hosting conformance. |
| [reactive-push.md](reactive-push.md) | Ranvier's reference shape, one mutation domain, immutable input admission, typed browser push and atomic reconnect reconciliation. |
| [composer-workflow.md](composer-workflow.md) | Explicit `.fidproj` open/reserve/edit/build/run/close, conflicts, epochs and per-region witness/object evidence. |
| [agents.md](agents.md) | Connection and checkout/session identity, cohort claims, agent-work board, human coordination and leased work. |
| [mcp-tools.md](mcp-tools.md) | Tool/resource arguments, refusals and subscriptions, containing MCP's external JSON-RPC exception. |
| [operations.md](operations.md) | Installed identity, readiness, matching deployment, rollback, lease heartbeat/queues, resource accounting and troubleshooting. |
| [binary-format-spec.md](binary-format-spec.md) | Durable `.bozzettofm` validation and typed direct interchange, linking owner-maintained PSG/BAREWire schemas rather than copying their authority. |
| [Bozzetto_Diagnostic_Sink.md](Bozzetto_Diagnostic_Sink.md) | Independent MailboxProcessor-driven sink, binary PSG capture, bounded persistence and Fidelity.Data JSON export only. |
| [development.md](development.md) | Owner-focused validation, differential controls, mutation checks, unfiltered TRUST acceptance and the one-page handoff. |

## Current versus planned

“Current” below means code inspected in this checkout, not verified installed behavior or executed acceptance. Paths identify the source evidence; a requirement is not a delivery claim. No build, test or live-service check was run for this entry point. Fresh retrieval and revision verification were unavailable in this read/write-only audit, so the bounded direct reads are not pinned-revision evidence.

| Area | Current source evidence | Requirement or planned boundary |
| --- | --- | --- |
| Controller and worker ownership | `Bozzetto/ComposerSupervisor.fs` owns the shared worker and session observations; `Bozzetto/ComposerWorkerClient.fs` distinguishes usable transport from physical process liveness. `Bozzetto/ControlListener.fs` provides a separate control application. | Deploy daemon and worker as a matching pair; reconcile observations against the current worker epoch. Ports and the one-daemon operating policy come from `AGENTS.md`, not a live probe here. |
| Composer workflow | `Bozzetto/ComposerTools.fs` routes explicit project open, reserve, build, run, cancel and close operations to the supervisor. Its tool contract requires reservation before source mutation and execution through `composer_run_current`. | Do not launch a returned artifact path directly or treat a status read as execution permission. Compiler-owned revalidation remains the admission boundary. |
| Daemon-worker wire | `Bozzetto.Composer.Protocol/BAREWireCodec.fs` implements explicit typed binary codecs; `StreamFrames.fs` bounds and frames requests/replies. `Bozzetto/ComposerWorkerClient.fs` reads binary replies and checks wire agreement, PSG identity, worker identity and correlation. | Preserve typed binary interchange and loud compatibility refusals. This transport evidence does not establish acceptance of every PSG delivery mode. |
| Scoped PSG delivery | The worker hello checks PSG schema, format version and contract fingerprint in `Bozzetto/ComposerWorkerClient.fs`. The diagnostic transport in `Bozzetto.Diagnostics/ManagedHost.fs` receives binary captures. | Source-authorized scope selection, exact-base deltas, reauthorization and receiver-installation fencing need their own evidence. Neither a hello nor diagnostic capture proves complete semantic/proof transport; full retained-graph transfer is not the contract. |
| Foundation adoption | `Bozzetto/ResourceGraph.fs` uses `Core.step`, declared reads, demand, `Finished` and `Drained` for pure resource derivations; its `AgentPublication` installs snapshots on foundation Offers. | These are concrete consumers, not proof of complete compiler/supervisor lifetime adoption or native self-hosting conformance. Complete premises and physical child cleanup remain owner obligations. |
| Browser push | `Bozzetto/UiBridge.fs` projects typed shared vocabulary, including per-region witness/object rows, and subscribes to owner changes. Its fifth fact input is the supervisor's bounded, memory-only operations journal; the fourth work derives protocol 8 `operations`, shown in the Composer page's Activity and Run output tabs with adapter-reported caller labels (`Bozzetto/ComposerJournal.fs`, `Bozzetto/ComposerSupervisor.fs`, `bozzetto-web/src/Shared/Protocol.fs`, `bozzetto-web/src/Frontend/App.fs`). `Bozzetto/UiBridgeCodec.fs` still encodes and decodes JSON, despite using Fidelity.Data. | Typed binary browser interchange is required. The current JSON wire is drift to remove, not an allowed interim contract. Ranvier-shaped settlement and atomic reconnect reconciliation require separate acceptance evidence. The journal is not authenticated identity, compiler authority or durable audit history. |
| Direct HTTP and agent-owned interchange | `Bozzetto/ComposerRoutes.fs` parses JSON requests, writes JSON responses and exposes an SSE invalidation endpoint. `Bozzetto/ControlListener.fs` constructs JSON discovery payloads. | Replace text/JSON direct interchange with typed binary. The retained SSE route is not the UI pipeline; changing serializer does not meet this requirement. MCP alone retains the external JSON-RPC exception. |
| Durable manifest | `Bozzetto.Core/Features/ManifestPersistence.fs` implements the `.bozzettofm` writer with binary sections and CRCs. | Persistence is not wire or compiler authority. Bounds, structure and identities must be validated as well as checksums; detailed format claims belong in the binary reference. |
| Diagnostic sink | `Bozzetto.Diagnostics/Bozzetto.Diagnostics.fsproj` defines a separate executable. `Bozzetto.Diagnostics/ManagedHost.fs` receives binary frames, persists bounded captures and generates JSON descriptors using Fidelity.Data. Its receiver is a serial async loop, not a MailboxProcessor. | The independent process must be MailboxProcessor-driven. That owner requirement remains an implementation gap; diagnostic persistence/export must never grant compilation, proof or execution authority. |
| Native hosting and distribution | The inspected controller/protocol code establishes a supervised worker boundary, not LLVM ORC hosting or authenticated distributed execution. | LLVM ORC JIT is planned, not implemented, per `AGENTS.md`. Native/self-hosted migration and lab-node identity, authentication, placement, fencing and reconciliation need explicit contracts and differential acceptance before delivery claims. |

## The three centerpieces

### Fidelity.PSG: compiler-owned evidence

Fidelity.PSG carries source-authorized compiler evidence whose scope, identity and proof meaning remain with the compiler owners; Bozzetto presents it and enforces host routing/epoch fences, rather than reconstructing a semantic graph from diagnostics or artifact paths. The PSG identity checks in `Bozzetto/ComposerWorkerClient.fs` are current code, but they do not establish proof validity or artifact acceptance. [Component contracts](Bozzetto_Fidelity_Component_Contracts.md) govern scoped delivery, while [Composer workflow](composer-workflow.md) governs its presentation and use.

### Fidelity.FSharp.Incremental: declared dependencies and owned work

Fidelity.FSharp.Incremental is the shared foundation for dependency bookkeeping and explicitly started work, with concrete Bozzetto consumers in `Bozzetto/ResourceGraph.fs`. Its adoption contract requires complete declared reads, cold admission, shared demand, withdrawal and physical drain; it does not dynamically discover dependencies, and an Offer establishes eligibility rather than compiler proof or permission to execute. See [foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md) for the lifetime and conformance requirements, including what must survive native migration.

### Ranvier: reactive presentation reference

Ranvier's signal/memo/effect shape and single mutation domain are the reference for coherent reactive presentation, not a claim that the controller already implements Ranvier or all its semantics. Immutable owner inputs must settle before typed push and reconnect reconciliation expose them, without creating another dependency or work-lifetime authority beside Fidelity.FSharp.Incremental. A memo's equal display value cannot erase compiler proof identity, and effect cleanup cannot certify physical drain; [reactive push](reactive-push.md) defines these boundaries.

## Interchange and retired surfaces

**No JSON in direct interchange.** Daemon-worker, daemon-browser and agent-owned direct contracts must be typed binary. JSON is an export format using Fidelity.Data at export edges and in the independent MailboxProcessor-driven diagnostic sink, not a substitute transport. MCP is JSON-RPC by external standard and is the sole external interchange exception; its adapter must preserve typed internal ownership contracts. The broader text/JSON allowance in `AGENTS.md` is superseded by this owner ruling for the documentation rewrite.

Embedded F# session hosting, FSI/REPL warmup and session hot reload are retired. Harmony, MonoMod, detours and injection are not supported; Datastar, the Raylib GUI, the Visual Studio extension, SageTUI, terminal rendering and SSE as the UI pipeline are not current product surfaces. Retained code or a stale page does not restore them: the compiled terminal modules listed in `Bozzetto/Bozzetto.fsproj` and the SSE endpoint in `Bozzetto/ComposerRoutes.fs` are drift to prune at their owning boundaries. Raylib application and game demos are separate from the retired GUI and remain permitted by `AGENTS.md`.

Use the durable references above for operating contracts. Dated checkpoints and audit receipts may support acceptance evidence, but cannot establish current installed identity, readiness or capability. This staged entry point does not certify completion of the other eleven rewrites or authorize deletion of their underlying evidence.
