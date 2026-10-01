# Clef / Composer development plan

September 30, 2026; scope clarified October 1, 2026. This is the concrete first-horizon implementation plan for Clef/Composer integration within Bozzetto's broader Fidelity development remit. The [development horizons](Bozzetto_Development_Horizons.md) define that remit and the later heterogeneous and federated work. The [Fidelity component contracts](Bozzetto_Fidelity_Component_Contracts.md) describe proposed interfaces and acceptance requirements for agreement with the component owners; they do not establish delivered APIs.

This plan incorporates the [provider handoff](Clef_Composer_Provider_Handoff.md), which remains the acceptance specification for the first provider milestone. The [initial implementation checkpoint](Bozzetto_Provider_Checkpoint_2026-09-30.md) records the first worker slice; the [live checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md) records shared MCP/browser integration and deployment. The [independent incremental assessment](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md), including its [October 1 promoted-distribution repeat](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md#october-1-promoted-distribution-repeat), establishes a bounded native cold/unchanged/edited workflow. The [editor workspace direction](Bozzetto_Editor_Workspace_Direction_2026-09-30.md) makes shared editor/compiler authority the next integration priority. Standalone MCP packaging, editor integration and the CPU ORC REPL remain planned first-horizon work.

## Outcome and ownership

The [October 1 Clefx host transition](Bozzetto_Clefx_Host_Transition_2026-10-01.md) removes embedded production FSI hosting from the checkout. Earlier milestone descriptions below preserve their historical acceptance scope; they do not promise continued embedded F# execution. Clefx/ORC implementation and deployment remain separate gates.

Bozzetto's broader destination is coordinated Fidelity development across application code, compilers, runtimes and target devices. This plan delivers its first-horizon compiler and workspace foundation, including a Clef/Composer CPU REPL backed by LLVM ORC JIT. Incremental compilation and explicit session authority establish the foundation for that experience. F#/.NET development can use the separate SageFS daemon and MCP connection on ports 37749/37750, alongside Bozzetto on 47749/47750; expanding Bozzetto’s inherited F# REPL is not a delivery priority.

An editor user and an agent attached to the same session must operate on the same compiler generation and observe the same diagnostics, build results, cancellations and execution refusals. Composer must also be usable through MCP without requiring an editor or Bozzetto's dashboard.

The ownership below governs this provider integration. Broader host, device and runtime coordination requires the additional component agreements in the [proposed contracts](Bozzetto_Fidelity_Component_Contracts.md).

| Owner | Responsibility |
|---|---|
| Clef / CCS / Baker | Source semantics, checking, settlement and publication of PSG facts |
| Fidelity.PSG | The published semantic contract passed to Alex |
| Alex | Passive witnessing of settled facts; no replacement source analysis or graph repair |
| Composer | Compilation, proof discharge, accepted artifacts and the `ProjectSession` execution gate |
| Provider worker | Session-bound tickets, request routing, cancellation, compiler epochs and calls to Composer |
| Bozzetto | Compiler-worker supervision and shared Clef/Composer session views for human and MCP clients |
| MCP adapters | Expose compiler/session operations through the protocol; no independent compilation state |
| MCP SDK | Protocol types and transport implementation, consumed as a normal package dependency |

MCP stays an integral Bozzetto interface. The planned standalone Composer MCP host will expose the same provider contract and implementation. It does not introduce a second compiler or a second interpretation of artifact validity.

Compiler parity across Clef samples, oracles and unit tests remains the compiler owners' prerequisite for broader application coverage. Those owners also validate and promote each exact compiler distribution; Bozzetto validates the adapter and deployment integration against that distribution. The [October 1 promotion and repeat](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md#october-1-promoted-distribution-repeat) completed that promotion for the recorded closure and bounded scalar workflow. It does not establish full language parity or resolve the recorded Lazy/Result boundaries.

Integration and efficient self-hosting guide the next phase. Ionide is vestigial context; Lattice scaffolding and Atelier's earlier topology are not constraints. Composer should own one authoritative workspace per selected session, sharing checking, proof scheduling, input snapshots and publication across Bozzetto's editor, MCP and browser adapters. Keep LSP as an interoperability surface and contracts independent of .NET/editor frameworks. Shared workspace integration takes priority over standalone packaging or a particular graph renderer.

## Historical inspection baseline

At the initial handoff inspection, the source references were Bozzetto `4f36b847`, Composer `dc833dc23d13e119d1db0d2d1d53469220608968`, Clef `20fdab2`, Fidelity.PSG `61c0019`, Alex `6a252dd`. These are historical inspection references, not current HEADs or instructions to reset working trees. The compiler repositories and Bozzetto had active edits during that inspection. Deployment and compiler-closure identities belong to the dated checkpoints and retained manifests, including the [October 1 promotion record](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md#october-1-promoted-distribution-repeat); preserve ongoing work and capture the exact source state for each acceptance run.

Implementation seams identified during that inspection (subsequent integration is recorded below):

- Composer's `src/Core/CompilationOrchestrator.fs` exposes `ProjectSession`, `Reserve`, both `BuildAsync` overloads, `Current`, `ManifestPath`, `RunCurrentAsync` and disposal. Its project targets .NET 10. The delivered provider uses this API.
- [SessionProjectTarget](../Bozzetto.Core/SessionProjectTarget.fs), [SessionManager](../Bozzetto.Core/SessionManager.fs), [SessionMode](../Bozzetto.Core/SessionMode.fs) and [WorkerProtocol](../Bozzetto.Core/WorkerProtocol.fs) describe inherited F# sessions. Clef project opening now uses its explicit Composer route; it does not add `.fidproj` to the FSI loader.
- [DaemonMode](../Bozzetto/DaemonMode.fs) supplies MCP with the shared session operations, model and event sources. [McpResources](../Bozzetto/McpResources.fs) reuses session/cohort read models. Composer now has one daemon-owned supervisor shared by its MCP tools/resource and browser/API.
- The removed nested `mcp-sdk` clone built `0.9.0-preview.3`, but the current restore resolves `ModelContextProtocol`, `.Core` and `.AspNetCore` at `1.0.0-rc.1` from nuget.org. The clone's historical SSE fix is not established as part of the running dependency closure.
- During the initial inspection, no REPL MCP tools were connected and the bounded daemon probe returned connection refused; the isolated .NET fallback was reported explicitly. That was a historical connection limitation. The live checkpoint now provides the installed launcher and daemon/MCP endpoints, with executed shared-interface native acceptance. Each auditor still needs to connect its own MCP client.

## Delivery sequence

### 0. Simplify the MCP dependency build

Delivered: the redundant clone/build path and local feed were removed; the normal SDK packages remain. The requirements below describe that change and its acceptance boundary.

Remove the unused SDK clone and pack steps from [ci-pipeline.fsx](../ci-pipeline.fsx), [build.fsx](../build.fsx), and the main/Copilot setup workflows. Remove the `mcp-sdk-fork` local NuGet source and its tracked placeholder, and correct contributor instructions and current README claims. Preserve historical validation records as history.

Keep the currently resolved package versions for this change. A package upgrade and any outstanding SSE connection-lifecycle repair require their own evidence; neither is implied by removing an unused source tree. Do not vendor the protocol implementation merely because MCP is important to Bozzetto.

After checking the nested clone for local work, move it and generated packages outside project repositories or remove them if they are wholly reproducible. No build entry point may recreate them inside Bozzetto. Test-tier scratch already moved under the external Bozzetto cache; retain that policy for provider fixtures, outputs and evidence.

Acceptance: a clean, locked restore and build succeed without the clone or local MCP package feed; the resolved package identities remain unchanged. Exercise MCP initialization, tool/resource discovery, subscription notifications, connection cleanup and reconnect behavior. Retain the prescribed unfiltered test acceptance and report any uncovered lifecycle issue rather than assuming the official package includes the fork's fix.

### 1. Deliver the bounded provider worker, then audit

Delivered, with the R1–R4 corrections recorded in the [repair checkpoint](Bozzetto_Provider_Repair_Checkpoint_2026-09-30.md). Composer supplied the lease-release repair and approved distribution used by the native gate. The requirements below remain the provider contract.

Implement the [handoff's first slice](Clef_Composer_Provider_Handoff.md#first-implementation-slice): explicit open, reserve, build, status, run and close, with responsive cancellation. Use typed `FSharp` and `ClefComposer` provider identities and reject operations for the wrong provider.

Keep Composer in a dedicated .NET worker, separate from FSI and the daemon's assembly-loading context. The adapter should be small and independently testable. Initial Composer access is exclusively through `ProjectSession`; do not reflect or replace its private `checkerGate`, introduce a second semantic pipeline, or cohost unrelated direct CCS callers.

Implementation choices, subject to independent review against the recorded patch and evidence:

- **Private worker transport:** versioned JSON request/response messages over parent-owned anonymous pipes, with request IDs and bounded messages. Keep compiler stdout/stderr on drained diagnostic streams. Dispatch long work off the reader/supervisor loop; serialize response writes while allowing status, reserve and cancellation requests to proceed. The private transport is separate from the public MCP transport.
- **Compiler deployment:** an explicitly configured compiler distribution identifies the worker launch command, Composer and dependency closure, runtime requirements and assembly identities. The first worker targets the inspected .NET 10 compiler closure. The worker build requires an absolute `ComposerDistribution` path and copies its DLL closure. The startup handshake reports Composer assembly path, version and SHA-256; a packaged distribution descriptor/closure manifest remains follow-up work. There is no hardcoded `/home/hhh` or assumed sibling source checkout in the worker. Missing artifacts fail the build.
- **Identity:** every established-session response carries protocol version, provider, owning host/session, compiler epoch and applicable generation/source identity. Pre-session failures carry request/provider identity without inventing a valid session. Transport reservation IDs map privately to Composer tickets and are bound to one session and epoch; each ticket is consumed once.
- **Compatibility:** introduce a versioned provider contract alongside the existing F# worker contract. This hard fork owns its namespaces, CLI and storage identity. It does not promise compatibility with SageFs state. Preserve the meaning of current Bozzetto F# operations; any unified session projection needs explicit decoding/version rules. Unknown providers must never default to F#.
- **Output placement:** private session/epoch directories under the external Bozzetto cache. Persist audit evidence separately under external state storage when it must survive cache cleanup. Each live directory has one owner; cleanup must not remove an active session's outputs. No generated fixture copies or dependency clones among project repositories.
- **Epoch fence:** introduce a provider-supervisor operation for replacing the compiler. It marks affected sessions unavailable, withdraws handles, cancels outstanding work and disposes/retires the old worker before accepting replacement work. Late responses and run completions are rejected by epoch/session identity. In-memory patching of an active compiler worker is unsupported in this milestone and must be refused before mutation; a file hash alone is not a patch fence.

Reservations happen before editor writes. A watcher can detect changes but cannot substitute for that ordering. Status describes recorded state and must not promise that an executable remains valid; execution always goes through `RunCurrentAsync`, including its post-execution generation check. Cancellation revokes acceptance without claiming immediate termination of every compiler subprocess or undoing native-program side effects.

The first assessment requires logical authority changes to be independent of
compiler filesystem work. Status reports cached observations; cancel and logical
close revoke immediately. Physical withdrawal and cleanup expose pending/error
state. A successful reserve still waits for the actual Composer reservation and
is the editor's permission to write. Run selection/launch and reservation remain
ordered through the backend invocation boundary. Adapter revisions and Composer
generations are distinct and must not be compared as if they were one counter.
Retirement fences every session before attempting any cleanup, aggregates every
failure, and preserves errors across repeated retirement attempts.

The full handoff acceptance cases are mandatory: simultaneous independent sessions; cold/unchanged/edited scalar compilation and actual retained object hashes; invalid, stale and corrupted inputs/artifacts; barrier-controlled cancellation and supersession; and compiler epoch replacement during work and after acceptance. Clef identities must never execute through inherited FSI routes. F#/.NET work may use the separate SageFS MCP service.

**Audit boundary:** deliver the bounded worker, contracts, tests and reproducible evidence to the coordinating compiler agent before proceeding with broad daemon/UI integration. Record findings and fixes against the exact patch. An implementation self-check is not the independent audit.

### 2. Integrate explicit provider sessions into Bozzetto

Delivered: nine `composer_*` MCP tools, `composer://sessions`, `/composer` and `/api/composer/*` share one daemon-owned supervisor. The unfiltered Composer integration tier passed **29/29 (Trusted)**, including the real MCP/HTTP journey through native build/run, human edit invalidation, exact-request cancellation, independent sessions, notifications, reconnect and worker replacement. See the [live checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md) for gate/deployment history and the [independent incremental assessment](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md) for the auditor-owned native journey and its narrower limits. Passing this tier is not audit approval. The requirements below define the delivered boundary.

After the first-slice audit, connect the provider worker to daemon ownership and supervision. Extend session operations and projections deliberately; do not rewrite `FsiSession` as a speculative universal evaluator. Initially require an explicit Clef project open. Keep `.fidproj` out of FSI routes.

Expose compiler operations through Bozzetto MCP using the same application handlers used by the human-facing API. Add a provider-aware session listing/status representation and resource change notifications. Preserve per-client selection, explicit session targeting, wrong-provider refusals and checkout boundaries.

Acceptance must demonstrate both directions: an agent reservation/build changes the state the human client observes, and a human reservation/cancel/replacement invalidates the state and execution authority visible to the agent. Compare session, epoch, generation and source identities, not just rendered messages. Include reconnect/resubscribe behavior so a client cannot revive an old accepted result after missing events.

### 3. Integrate the editor and compiler workspace

Partially delivered: the browser supports explicit open, reservation, build, status/evidence, cancel, run, close and worker retirement. Automatic editor save/build integration and compiler-stage progress streaming remain planned; status and completed-operation notifications are not a progress stream. Lattice currently checks through its own editor session, separately from the Bozzetto worker. Sharing project paths or displaying both outputs does not establish one compiler authority.

Follow the [integrated editor workspace checkpoint](Bozzetto_Editor_Workspace_Direction_2026-09-30.md): consolidate compiler-owned checking, proof scheduling and immutable observations behind the supervised workspace service; attach VS Code and Neovim through thin adapters and let Atelier consume the same contracts when implemented. Add a shared compiler artifact inventory and exact-byte evidence views within this integration. Preserve useful source grammar and freshness handling while replacing inherited Ionide assumptions wherever they obstruct the shared service or native hosting.

Add editor/dashboard actions for explicit Clef project open, reservation before save, build progress, diagnostics, evidence inspection, cancellation and gated run. Present provider and current generation clearly. Automatic rebuilds must preserve reservation, supersession and epoch rules; a filesystem change notification alone cannot authorize execution.

Show source diagnostics, proof outcomes and artifact admission as separate states. Preserve refusals and diagnostic provenance. Use the separate SageFS MCP service for F#/.NET compiler-host investigation. Apply compiler replacement through the provider epoch fence, with an observable withdrawal/restart for every affected session.

Acceptance: one reproducible editor-plus-agent workflow opens the real scalar fixture, builds/runs it, changes `changeable`, observes reuse and updated native output, then demonstrates invalid-edit and compiler-replacement refusals without stale execution being offered by either interface.

Begin with reserve/save/build. Unsaved-buffer checking and execution additionally require compiler-owned overlay transactions that identify the same checked and built inputs; LSP document versions alone cannot supply that guarantee. Measure duplicate work, cold/warm response latency and bounded resource use under rapid edits and multiple clients. Graph/proof/IR views consume compiler-authored snapshots, correspondence and events. Detailed animation, selective proof reuse and ORC remain separate acceptance boundaries, as specified in the editor checkpoint.

### 4. Deliver the standalone Composer MCP host

Planned. The integrated daemon MCP interface is delivered; an independently deployable Composer MCP host is a separate milestone.

Provide an independently launchable compiler MCP host using the same worker client, typed operation contracts, projections and refusal paths as Bozzetto. Initially use MCP stdio for local agent/automation clients; the worker remains on its separate private pipes. HTTP deployment can follow only when needed.

The compiler MCP vocabulary belongs to the Composer integration: project open, reserve edit, build, cancel, status, inspect accepted artifact/proof metadata, run current and close. Inspection reports available compiler evidence; it does not invent semantic queries or promote pending proofs to acceptance. Build/run operations preserve the handoff's identities and full proof requirements.

Standalone mode owns its explicitly created sessions. Bozzetto mode forwards to Bozzetto-owned sessions. An attach operation, if provided, must identify the owning host and route there; it must never silently reopen a project in another worker and present it as the same session. Independent sessions for the same source tree must be explicit and have separate outputs and authority.

Keep the initial reusable implementation together with the provider work; choose the final package/repository ownership with the auditor before packaging the standalone host. Do not copy a second adapter into Composer. Standalone usability must not require a Bozzetto source checkout, dashboard, editor or live Bozzetto daemon.

Acceptance: real MCP clients exercise both deployments against the same operation contract. Check tool/resource schemas, structured refusals, cancellation, disconnect cleanup and clean process shutdown. In integrated mode, both human and MCP clients demonstrably share the same authority. A standalone session makes its distinct ownership explicit.

## Acceptance evidence and exclusions

Use the handoff's real `04d_IncrementalScalarRegions` fixture in an external temporary copy. Record exact source revisions and dirty patches, compiler/runtime/native-tool identities, executed test counts, accepted source/artifact hashes, retained object paths/hashes, native stdout/exit status and every refusal. Retain full proof checks on every generation; neither object reuse nor successful solver dispatch alone proves artifact admission.

Storage capture with no known source identity must be refused by Baker. A passing identity check alone does not establish capture/lifetime safety; keep a regression that distinguishes those outcomes. Borrow records support full source revalidation, but do not by themselves establish eligibility for selective proof reuse. Before enabling that reuse, represent explicit dependencies for complete-use checks and prove invalidation when the relevant use set changes. Composer/Baker own these proof rules; Bozzetto carries their evidence and refusals without implementing a second proof checker.

Register tests structurally in the existing test infrastructure or in an explicit whole-suite provider entry point invoked by CI and represented in its trust ledger. Filtered runs are for development only. Run the required unfiltered gates at each implementation milestone; report failures, ignored cases and unrun gates explicitly. In Composer-owned work use .NET tooling, following its repository instructions.

The existing auditor owns the independent review. Do not treat this plan, handoff receipt, a successful build, or a mock-only provider test as audit approval or native acceptance.

## Workstream and resource coordination

Use Codex for implementation and focused review workstreams by default. The coordinating compiler agent remains the auditor. Parallelize bounded reads or disjoint changes only when their ownership is clear; preserve the other agent's active compiler and test edits.

LAN Ornith workers, local Lemonade/Nemotron workers and LAN retrieval are optional shared resources, not prerequisites for delivery. Use them only for a concrete need, accounting for the auditor's use and queue availability. Do not assume an endpoint, model capability or free worker. Coordinate expensive native compilation and unfiltered test runs; avoid starting competing builds against shared compiler outputs. Collect evidence outside repositories.

## Reduce .NET coupling on the path to self-hosting

### Shared incremental foundation

Adopt **Fidelity.FSharp.Incremental** as the shared dependency and work-lifetime
foundation for Bozzetto's interim .NET hosting, in coordination with its intended
Clef/CCS/Baker/Composer consumers. The [adoption contract](Bozzetto_Incremental_Foundation_Adoption.md)
records current source status, owner handoffs and the first integration gate.
The library is still pre-integration and locally developed; establish an immutable
dependency identity and executed evidence before promoting a consuming closure.

Begin with one compiler-owned workspace workload and Bozzetto client demand,
then extend through the intended consumers with separate acceptance records.
Preserve reserve-before-write, complete proof premises, fresh artifact receipts,
shared-demand cancellation and actual child-resource draining. Deferring a task
must preserve the existing launch/reservation ordering at its new execution
boundary. Use portable command/effect fixtures to carry these guarantees into a
native host. This work belongs alongside shared workspace integration, rather
than being deferred until device or remote-site horizons.

The functional coordinator and scoped suspension/resumption have standalone
[audit evidence](../../Fidelity.FSharp.Incremental/docs/Functional_Async_Auditor_Assessment_2026-10-01.md)
at checkpoint `613e260`, with 99 passing tests and the F1 cleanup repair preserved.
Audit its [acknowledgement and lifetime contract](Bozzetto_Incremental_Foundation_Adoption.md#workspace-coordinator-and-scoped-resumptions)
against the actual Bozzetto workspace journey before adopting it. Require tests
for reservation winning after scheduling but before launch, lost replies,
stale/duplicate resumptions, shared demand and cleanup under queue pressure.
The .NET mailbox implementation stays behind the portable protocol boundary.
Use the accepted `AsyncMailbox` functional API and F# async workflows for new
integration, keeping required CLR interop at explicit boundaries. Retain exact
operation handles before observing replies; preserve admission and owned cleanup
through the real backend. See the
[authoring contract](Bozzetto_Incremental_Foundation_Adoption.md#functional-async-authoring-and-native-execution).

The user relayed Clef-agent findings of separate Composer/editor locks around
shared compiler state and early result retirement before Bozzetto cleanup joins.
The agent is addressing shared whole-project checking and joined work lifetimes.
Treat those as reported integration blockers until the actual workspace path
demonstrates shared serialization, prompt logical withdrawal and completed
physical cleanup with proof/artifact gates intact.

### Host dependency boundaries

Self-hosting is a near-term design constraint. F#/.NET workflows can remain on the separate SageFS service; expanding Bozzetto's inherited engine is not required. Add no FSI, Harmony, FCS, MSBuild or reflection requirement to Clef session contracts. A package with an upstream name is a current implementation dependency to evaluate for removal, not a retained architectural requirement.

| Current dependency | Why present now | Exit boundary / acceptance |
|---|---|---|
| Fidelity.FSharp.Incremental and its .NET host (selected; not yet referenced) | Planned common incremental bookkeeping and work-lifetime foundation | Preserve the portable protocol and consumer admission rules while replacing .NET hosting; replay and real lifecycle conformance must pass on the native host. |
| FSharp.Compiler.Service / FSI, Fantomas, Ionide project loading, Harmony, Cecil | Existing F# evaluation, project resolution and patching in Bozzetto.Core | Isolate and remove from the Clef-only host, which must not reference or load these assemblies. Hybrid projects can use a separate SageFS service for F# support. |
| Composer managed DLL closure and .NET worker | Today's compiler is managed | Replace worker implementation with a native/self-hosted compiler process using the same versioned JSON authority contract; native build/run/refusal tests must pass without dotnet installed. |
| ModelContextProtocol .NET SDK / ASP.NET Core | Current MCP transport/hosting | Keep protocol schemas independent of SDK types. A native transport implementation must pass the same tool/resource and lifecycle conformance tests; SDK-specific code remains at the hosting edge. |
| Falco, adaptive state and .NET logging/telemetry/storage bindings | Current dashboard and daemon implementation | Extract session state transitions and projections from framework types before replacing host infrastructure; prove human/MCP views retain identical authority. |
| Expecto / FsCheck / managed build tools | Current implementation validation | Reuse language-neutral wire fixtures and process tests across managed and native hosts. Managed test tooling can remain a development aid while native deployment loses its runtime dependency. |

The current source has not yet achieved these separations: Core directly references FCS and Harmony and the daemon references Core. The new Composer worker already avoids a reference to Bozzetto.Core and serializes explicit data, not CLR type names or opaque compiler objects. Its build currently copies the compiler distribution's full DLL directory; narrow that to a compiler-owned deployment manifest rather than maintaining a guessed allowlist in Bozzetto.

Make the reusable worker boundary language-neutral and introduce a selectable native worker launcher as the compiler becomes ready, alongside shared workspace integration. Native migration does not depend on completing standalone MCP packaging. Both integrated and standalone hosts must launch the compiler without loading the F# host; run the same native acceptance suite against each delivered implementation. A Clef-only installation with no .NET runtime is the exit criterion; a separate SageFS service may still serve the F#/Fable side of hybrid projects.

Historical attribution and actual upstream URLs remain accurate even after runtime dependencies disappear. The formerly inherited `SageFs.Harmony` package is now replaced by the source-built, owned `Bozzetto.Harmony` fork; see the [Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md). This removes the old package/build identity. Its Harmony/MonoMod functionality still requires .NET until isolated or replaced.

## CPU REPL within the first horizon

LLVM ORC JIT is the intended first-horizon CPU execution backend once the compiler and shared-workspace prerequisites are met. Composer should own JIT materialization, publication and retirement, preserving proof/admission gates, revision and epoch fences, and refusal of obsolete execution. That REPL milestone requires its own native acceptance evidence; the current implementation executes accepted native binaries.

The later cross-target evaluation research gate does not add a prerequisite to this CPU-native REPL milestone.

## Later scope

The [development horizons](Bozzetto_Development_Horizons.md) own the broader roadmap. Horizon 2 proposes REPL selection from arbitrary existing application code and physical-device/LAN coordination for heterogeneous hot reload. Its cross-target evaluation tranche, especially CPU execution that preserves another target's arithmetic, is demand-gated research. Implementation commitment requires concrete customer, developer or engineering demand, a bounded feasibility assessment, and an estimate of proof and hardware-capability obligations and cost. CPU-native and target arithmetic can require substantially different proof contexts; successful CPU execution cannot stand in for target admission.

The research scope includes existing code without requiring it to move into a Common module or be restricted to pure functions. Any supported evaluation must identify its numeric behavior and admitted state, effects and resources; unsupported requirements remain explicit refusals. Horizon 3 proposes federated site nodes and remote specialized processors. These directions and the [component contracts](Bozzetto_Fidelity_Component_Contracts.md) require agreement with their owners and do not commit implementation of the cross-target evaluation tranche.

Proof caching and shared-memory PSG distribution remain separate compiler work; neither is implied by object reuse or a REPL. The self-hosting migration follows the explicit dependency boundaries above. This plan establishes the first-horizon integration and makes no claim that later capabilities already exist.

## Status

- Delivered: external artifact placement, redundant MCP SDK removal, product identity migration, separate default ports, the bounded Composer worker and shared daemon supervision behind MCP and the browser/API.
- Assessment corrections: [R1–R4](Bozzetto_Provider_Assessment_2026-09-30.md) and their [repair checkpoint](Bozzetto_Provider_Repair_Checkpoint_2026-09-30.md) record retirement, cancellation, identity and responsiveness changes. Composer supplied the separate lease-release repair and approved compiler distribution.
- Executed shared-interface/native acceptance: **29 registered, 29 passed, Trusted**. The [live checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md) records the September 30 deployment identities, separate default-gate disposition and independent auditor handoff. The [October 1 promotion record](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md#october-1-promoted-distribution-repeat) records the newer validated compiler/worker closure, coordinated retirement/restart and another unfiltered **29/29 Trusted** provider tier. The default gate was not rerun for that promotion; an earlier checkpoint's passing default gate does not establish a newer result.
- Harmony ownership and H1/H2 import corrections are recorded in the [Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md); managed dependencies remain explicit self-hosting work.
- Audit follow-up: the [independent read-only assessment](Bozzetto_Live_Provider_Auditor_Assessment_2026-09-30.md) corroborates shared visibility and recorded results. Its three usability findings are addressed in the deployed [correction response](Bozzetto_Live_Provider_Audit_Response_2026-09-30.md), with full gates rerun.
- Independent native workflow: the [incremental assessment](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md) records cold/unchanged/one-function-edit builds, exact object reuse, human reservation followed by MCP refusal before source mutation, successful updated execution and owned-session cleanup. Its October 1 repeat corroborates that bounded workflow against the promoted compiler. Successful retirement and provider-tier cancellation/cleanup controls do not establish every live race, arbitrary application coverage, unsaved-buffer authority or ORC execution; Lazy/Result boundaries remain explicit.
- Next integration: shared editor/compiler workspace authority, save/build and overlay contracts, measured responsiveness and evidence views in the [editor direction checkpoint](Bozzetto_Editor_Workspace_Direction_2026-09-30.md). Ionide/Lattice inheritance does not determine the architecture.
- Shared incremental foundation: adopt Fidelity.FSharp.Incremental across the intended compiler and Bozzetto consumers under the [adoption contract](Bozzetto_Incremental_Foundation_Adoption.md). Initial library source exists locally; dependency pinning, Bozzetto integration and cross-component acceptance remain work.
- Remaining first-horizon work: uncovered cancellation/recovery and retirement races, shared editor workspace and progress streaming (milestone 3), standalone Composer MCP packaging (milestone 4), CPU LLVM ORC JIT and the native/.NET-free hosting boundary. Compiler owners continue parity work and promote further distributions only with their own validated identities and acceptance; Bozzetto integration must validate each adopted closure.
- Broader direction: the [development horizons](Bozzetto_Development_Horizons.md) and [proposed component contracts](Bozzetto_Fidelity_Component_Contracts.md) describe later application-code REPL selection, heterogeneous reload and federated development. Cross-target evaluation remains research pending demonstrated demand, feasibility and proof-cost evidence; this gate does not delay first-horizon CPU ORC work. These directions are not delivered acceptance.

## Product identity and coexistence

Bozzetto is a hard fork with CLI `boz`, namespaces/packages `Bozzetto`, state under `.bozzetto`, and loopback defaults **47749 (MCP)** / **47750 (dashboard)**. These deliberately differ from SageFs 37749/37750. Explicit port overrides remain available. Product references are migrated throughout sources, project paths, editor commands, scripts and agent instructions. Real upstream URLs, legal credit and third-party package identities remain truthful; see the [migration inventory](Bozzetto_Identity_Migration_Inventory.md).

## Reference findings

Fable.SageFs at `53ed66275a267d35bee2044a30550bbb5b2ffd6c` demonstrates deliberate compiler assembly isolation, a persistent checker, and baseline/edit/revert output checks. Its watcher and project-configuration limits are relevant: edits outside the root and changed configuration must not be assumed to reload correctly. Its daemon discovery reads a state file, reinforcing separate Bozzetto state and explicit endpoint selection. These are lessons, not Clef implementation or proof that its live-patching path can safely patch an active Composer worker.

LAN retrieval provided a source map from Composer ProjectSession through CCS checking and revision publication to Fidelity.PSG and Alex generation/admission. The indexed compiler heads matched the inspected local heads, but local dirty changes required source inspection. Shared inference workers were not needed.
