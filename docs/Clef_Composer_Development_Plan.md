# Clef / Composer development plan

September 30, 2026. This is the implementation plan for making Bozzetto useful for Clef and Composer development, including a compiler-focused MCP interface. It incorporates the [provider handoff](Clef_Composer_Provider_Handoff.md), which remains the acceptance specification for the first provider milestone. See the [implementation checkpoint](Bozzetto_Provider_Checkpoint_2026-09-30.md) for delivered code, measured results and the independent audit boundary. Later milestones remain planned.

## Outcome and ownership

The destination is a Clef/Composer REPL backed by LLVM ORC JIT. Incremental compilation and explicit session authority establish the foundation for that experience. F#/.NET development can use the separate SageFS daemon and MCP source on its own ports; expanding Bozzetto’s inherited F# REPL is not a delivery priority.

An editor user and an agent attached to the same session must operate on the same compiler generation and observe the same diagnostics, build results, cancellations and execution refusals. Composer must also be usable through MCP without requiring an editor or Bozzetto's dashboard.

| Owner | Responsibility |
|---|---|
| Clef / CCS / Baker | Source semantics, checking, settlement and publication of PSG facts |
| Fidelity.PSG | The published semantic contract passed to Alex |
| Alex | Passive witnessing of settled facts; no replacement source analysis or graph repair |
| Composer | Compilation, proof discharge, accepted artifacts and the `ProjectSession` execution gate |
| Provider worker | Session-bound tickets, request routing, cancellation, compiler epochs and calls to Composer |
| Bozzetto | Supervision, client coordination and shared session views across F# and Clef providers |
| MCP adapters | Expose compiler/session operations through the protocol; no independent compilation state |
| MCP SDK | Protocol types and transport implementation, consumed as a normal package dependency |

MCP stays an integral Bozzetto interface. A standalone Composer MCP host exposes the same provider contract and implementation. It does not introduce a second compiler or a second interpretation of artifact validity.

## Inspected baseline

The current commits match the handoff: Bozzetto `4f36b847`, Composer `dc833dc23d13e119d1db0d2d1d53469220608968`, Clef `20fdab2`, Fidelity.PSG `61c0019`, Alex `6a252dd`. These are inspection references, not instructions to reset working trees. Composer, Clef and Alex contain active compiler/test edits; Bozzetto contains documentation and test-cache relocation edits. Preserve them and capture their exact diffs when producing acceptance evidence.

Verified implementation seams:

- Composer's `src/Core/CompilationOrchestrator.fs` exposes `ProjectSession`, `Reserve`, both `BuildAsync` overloads, `Current`, `ManifestPath`, `RunCurrentAsync` and disposal. Its project targets .NET 10. The first provider must use this API.
- [SessionProjectTarget](../Bozzetto.Core/SessionProjectTarget.fs), [SessionManager](../Bozzetto.Core/SessionManager.fs), [SessionMode](../Bozzetto.Core/SessionMode.fs) and [WorkerProtocol](../Bozzetto.Core/WorkerProtocol.fs) currently describe F# sessions. Accepting `.fidproj` in the existing FSI loader is not integration.
- [DaemonMode](../Bozzetto/DaemonMode.fs) supplies MCP with the shared session operations, model and event sources. [McpResources](../Bozzetto/McpResources.fs) already reuses session/cohort read models. Extend this arrangement for Clef.
- The removed nested `mcp-sdk` clone built `0.9.0-preview.3`, but the current restore resolves `ModelContextProtocol`, `.Core` and `.AspNetCore` at `1.0.0-rc.1` from nuget.org. The clone's historical SSE fix is not established as part of the running dependency closure.
- No REPL MCP tools are connected in this agent session; the bounded daemon probe returned connection refused. The .NET fallback was reported explicitly. The new provider has real native acceptance evidence; daemon integration remains later work.

## Delivery sequence

### 0. Simplify the MCP dependency build

Remove the unused SDK clone and pack steps from [ci-pipeline.fsx](../ci-pipeline.fsx), [build.fsx](../build.fsx), and the main/Copilot setup workflows. Remove the `mcp-sdk-fork` local NuGet source and its tracked placeholder, and correct contributor instructions and current README claims. Preserve historical validation records as history.

Keep the currently resolved package versions for this change. A package upgrade and any outstanding SSE connection-lifecycle repair require their own evidence; neither is implied by removing an unused source tree. Do not vendor the protocol implementation merely because MCP is important to Bozzetto.

After checking the nested clone for local work, move it and generated packages outside project repositories or remove them if they are wholly reproducible. No build entry point may recreate them inside Bozzetto. Test-tier scratch already moved under the external Bozzetto cache; retain that policy for provider fixtures, outputs and evidence.

Acceptance: a clean, locked restore and build succeed without the clone or local MCP package feed; the resolved package identities remain unchanged. Exercise MCP initialization, tool/resource discovery, subscription notifications, connection cleanup and reconnect behavior. Retain the prescribed unfiltered test acceptance and report any uncovered lifecycle issue rather than assuming the official package includes the fork's fix.

### 1. Deliver the bounded provider worker, then audit

Implement the [handoff's first slice](Clef_Composer_Provider_Handoff.md#first-implementation-slice): explicit open, reserve, build, status, run and close, with responsive cancellation. Use typed `FSharp` and `ClefComposer` provider identities and reject operations for the wrong provider.

Keep Composer in a dedicated .NET worker, separate from FSI and the daemon's assembly-loading context. The adapter should be small and independently testable. Initial Composer access is exclusively through `ProjectSession`; do not reflect or replace its private `checkerGate`, introduce a second semantic pipeline, or cohost unrelated direct CCS callers.

Initial implementation choices, to be checked against the actual patch by the auditor:

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

After the first-slice audit, connect the provider worker to daemon ownership and supervision. Extend session operations and projections deliberately; do not rewrite `FsiSession` as a speculative universal evaluator. Initially require an explicit Clef project open. Keep `.fidproj` out of FSI routes.

Expose compiler operations through Bozzetto MCP using the same application handlers used by the human-facing API. Add a provider-aware session listing/status representation and resource change notifications. Preserve per-client selection, explicit session targeting, wrong-provider refusals and checkout boundaries.

Acceptance must demonstrate both directions: an agent reservation/build changes the state the human client observes, and a human reservation/cancel/replacement invalidates the state and execution authority visible to the agent. Compare session, epoch, generation and source identities, not just rendered messages. Include reconnect/resubscribe behavior so a client cannot revive an old accepted result after missing events.

### 3. Deliver the standalone Composer MCP host

Provide an independently launchable compiler MCP host using the same worker client, typed operation contracts, projections and refusal paths as Bozzetto. Initially use MCP stdio for local agent/automation clients; the worker remains on its separate private pipes. HTTP deployment can follow only when needed.

The compiler MCP vocabulary belongs to the Composer integration: project open, reserve edit, build, cancel, status, inspect accepted artifact/proof metadata, run current and close. Inspection reports available compiler evidence; it does not invent semantic queries or promote pending proofs to acceptance. Build/run operations preserve the handoff's identities and full proof requirements.

Standalone mode owns its explicitly created sessions. Bozzetto mode forwards to Bozzetto-owned sessions. An attach operation, if provided, must identify the owning host and route there; it must never silently reopen a project in another worker and present it as the same session. Independent sessions for the same source tree must be explicit and have separate outputs and authority.

Keep the initial reusable implementation together with the provider work; choose the final package/repository ownership with the auditor before packaging the standalone host. Do not copy a second adapter into Composer. Standalone usability must not require a Bozzetto source checkout, dashboard, editor or live Bozzetto daemon.

Acceptance: real MCP clients exercise both deployments against the same operation contract. Check tool/resource schemas, structured refusals, cancellation, disconnect cleanup and clean process shutdown. In integrated mode, both human and MCP clients demonstrably share the same authority. A standalone session makes its distinct ownership explicit.

### 4. Complete the human development loop

Add editor/dashboard actions for explicit Clef project open, reservation before save, build progress, diagnostics, evidence inspection, cancellation and gated run. Present provider and current generation clearly. Automatic rebuilds must preserve reservation, supersession and epoch rules; a filesystem change notification alone cannot authorize execution.

Show source diagnostics, proof outcomes and artifact admission as separate states. Preserve refusals and diagnostic provenance. Use the separate SageFS MCP service for F#/.NET compiler-host investigation. Apply compiler replacement through the provider epoch fence, with an observable withdrawal/restart for every affected session.

Acceptance: one reproducible editor-plus-agent workflow opens the real scalar fixture, builds/runs it, changes `changeable`, observes reuse and updated native output, then demonstrates invalid-edit and compiler-replacement refusals without stale execution being offered by either interface.

## Acceptance evidence and exclusions

Use the handoff's real `04d_IncrementalScalarRegions` fixture in an external temporary copy. Record exact source revisions and dirty patches, compiler/runtime/native-tool identities, executed test counts, accepted source/artifact hashes, retained object paths/hashes, native stdout/exit status and every refusal. Retain full proof checks on every generation; neither object reuse nor successful solver dispatch alone proves artifact admission.

Register tests structurally in the existing test infrastructure or in an explicit whole-suite provider entry point invoked by CI and represented in its trust ledger. Filtered runs are for development only. Run the required unfiltered gates at each implementation milestone; report failures, ignored cases and unrun gates explicitly. In Composer-owned work use .NET tooling, following its repository instructions.

The existing auditor owns the independent review. Do not treat this plan, handoff receipt, a successful build, or a mock-only provider test as audit approval or native acceptance.

## Workstream and resource coordination

Use Codex for implementation and focused review workstreams by default. The coordinating compiler agent remains the auditor. Parallelize bounded reads or disjoint changes only when their ownership is clear; preserve the other agent's active compiler and test edits.

LAN Ornith workers, local Lemonade/Nemotron workers and LAN retrieval are optional shared resources, not prerequisites for delivery. Use them only for a concrete need, accounting for the auditor's use and queue availability. Do not assume an endpoint, model capability or free worker. Coordinate expensive native compilation and unfiltered test runs; avoid starting competing builds against shared compiler outputs. Collect evidence outside repositories.

## Reduce .NET coupling on the path to self-hosting

Self-hosting is a near-term design constraint, not an excuse to add another permanent .NET layer. Keep existing working F# support, but add no FSI, Harmony, FCS, MSBuild or reflection requirement to Clef session contracts. A package with an upstream name is a current implementation dependency to evaluate for removal, not a retained architectural requirement.

| Current dependency | Why present now | Exit boundary / acceptance |
|---|---|---|
| FSharp.Compiler.Service / FSI, Fantomas, Ionide project loading, Harmony, Cecil | Existing F# evaluation, project resolution and patching in Bozzetto.Core | Extract behind the F# provider; Clef-only host must not reference or load these assemblies. Keep optional F# support available for hybrid projects. |
| Composer managed DLL closure and .NET worker | Today's compiler is managed | Replace worker implementation with a native/self-hosted compiler process using the same versioned JSON authority contract; native build/run/refusal tests must pass without dotnet installed. |
| ModelContextProtocol .NET SDK / ASP.NET Core | Current MCP transport/hosting | Keep protocol schemas independent of SDK types. A native transport implementation must pass the same tool/resource and lifecycle conformance tests; SDK-specific code remains at the hosting edge. |
| Falco, adaptive state and .NET logging/telemetry/storage bindings | Current dashboard and daemon implementation | Extract session state transitions and projections from framework types before replacing host infrastructure; prove human/MCP views retain identical authority. |
| Expecto / FsCheck / managed build tools | Current implementation validation | Reuse language-neutral wire fixtures and process tests across managed and native hosts. Managed test tooling can remain a development aid while native deployment loses its runtime dependency. |

The current source has not yet achieved these separations: Core directly references FCS and Harmony and the daemon references Core. The new Composer worker already avoids a reference to Bozzetto.Core and serializes explicit data, not CLR type names or opaque compiler objects. Its build currently copies the compiler distribution's full DLL directory; narrow that to a compiler-owned deployment manifest rather than maintaining a guessed allowlist in Bozzetto.

After the first audit, make the reusable worker client/contract assembly-free at the protocol boundary and ensure standalone Composer MCP can launch the compiler without loading the F# host. Then introduce a selectable native worker launcher and run the same native acceptance suite against it. A Clef-only installation with no .NET runtime is the exit criterion; an optional managed F# provider may still serve hybrid Fable/F#/Clef projects.

Historical attribution and actual upstream URLs remain accurate even after runtime dependencies disappear. The formerly inherited `SageFs.Harmony` package is now replaced by the source-built, owned `Bozzetto.Harmony` fork; see the [Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md). This removes the old package/build identity. Its Harmony/MonoMod functionality still requires .NET until isolated or replaced.

## Later scope

LLVM ORC JIT is the intended next execution-backend direction once the incremental session contract is established. Composer should own JIT materialization, publication and retirement, preserving proof/admission gates, revision and epoch fences, and refusal of obsolete execution. That REPL milestone requires its own native acceptance evidence; the current implementation executes accepted native binaries.

Native live-state hot reload, proof caching, complete language parity and shared-memory PSG distribution remain later work. The self-hosting migration follows the explicit dependency boundaries above. This plan establishes safe, observable compilation sessions and MCP access first; it makes no claim that those later capabilities already exist.

## Status

- Delivered for audit: external artifact placement, redundant MCP SDK removal, product identity migration, separate default ports, small Composer worker, authority tests and native process tests.
- Assessment received: [R1–R4 corrections](Bozzetto_Provider_Assessment_2026-09-30.md) cover retirement failure, cancellation cleanup, refusal identity and responsiveness. The [repair checkpoint](Bozzetto_Provider_Repair_Checkpoint_2026-09-30.md) records implemented changes and the passing default gate; the Composer owner supplies the separate lease-release repair and validated distribution for native acceptance.
- Harmony ownership is ready for review in the [Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md); managed dependencies remain explicit self-hosting work.
- Next after audit: daemon ownership and common human/MCP session projection (milestone 2), then standalone Composer MCP (milestone 3) and editor actions (milestone 4).

## Product identity and coexistence

Bozzetto is a hard fork with CLI `boz`, namespaces/packages `Bozzetto`, state under `.bozzetto`, and loopback defaults **47749 (MCP)** / **47750 (dashboard)**. These deliberately differ from SageFs 37749/37750. Explicit port overrides remain available. Product references are migrated throughout sources, project paths, editor commands, scripts and agent instructions. Real upstream URLs, legal credit and third-party package identities remain truthful; see the [migration inventory](Bozzetto_Identity_Migration_Inventory.md).

## Reference findings

Fable.SageFs at `53ed66275a267d35bee2044a30550bbb5b2ffd6c` demonstrates deliberate compiler assembly isolation, a persistent checker, and baseline/edit/revert output checks. Its watcher and project-configuration limits are relevant: edits outside the root and changed configuration must not be assumed to reload correctly. Its daemon discovery reads a state file, reinforcing separate Bozzetto state and explicit endpoint selection. These are lessons, not Clef implementation or proof that its live-patching path can safely patch an active Composer worker.

LAN retrieval provided a source map from Composer ProjectSession through CCS checking and revision publication to Fidelity.PSG and Alex generation/admission. The indexed compiler heads matched the inspected local heads, but local dirty changes required source inspection. Shared inference workers were not needed.
