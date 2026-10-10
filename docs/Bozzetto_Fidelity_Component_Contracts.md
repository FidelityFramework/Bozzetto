# Bozzetto contracts with Fidelity components

October 1, 2026. These are Bozzetto's required integration contracts for the
[three development horizons](Bozzetto_Development_Horizons.md). They describe what
Bozzetto must supply and what it needs from peer components. New interfaces and
ownership extensions below are proposals for implementation with those owners;
this document does not claim that another component has delivered or accepted
them. Existing language and compiler contracts remain authoritative.

For exploratory horizons these are conditional acceptance requirements, not a
funded implementation backlog. CPU execution preserving another target's numeric
and hardware semantics must first pass the roadmap's
[demand and feasibility gate](Bozzetto_Development_Horizons.md#demand-and-feasibility-before-expansion).
Ordinary CPU REPL delivery is independent of that research tranche.

The current baseline is the [provider handoff](Clef_Composer_Provider_Handoff.md),
[provider data types](../Bozzetto.Composer/ProviderContracts.fs),
[shared workspace direction](Bozzetto_Editor_Workspace_Direction_2026-09-30.md)
and [native workflow evidence](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md).
Today's provider exposes local project build/run operations and one accepted
artifact receipt. The richer evaluation, application manifest, target lifecycle
and remote operation contracts below remain development work.

## Ownership and required handoffs

| Component or owner | Contract Bozzetto needs | Bozzetto responsibility |
|---|---|---|
| Fidelity.FSharp.Incremental | Explicit dependency, demand, invalidation and execution-lifetime protocol; interim .NET hosting and portable conformance requirements. | Adopt the shared foundation for workspace coordination, map identities and resource ownership, and preserve compiler admission beyond library eligibility. |
| Clef specification, CCS and Baker | Source admission, binding/redefinition, initialization, effects, capture/lifetime rules, numeric selection and evaluation-context requirements. | Preserve compiler authority and refusals; never reconstruct language meaning in an adapter. |
| Fidelity.PSG | Immutable revision-bound observations, dependency facts and source/occurrence correspondence needed by consumers. | Route and retain observations with identity; distinguish compiler facts from presentation and user annotations. |
| Composer and Alex | Shared workspace operations, proof/evidence publication, witnessed lowering, artifact manifests, compatible compositions and execution admission. Alex remains a passive witness of settled facts. | Supervise workers, order requests and expose the same accepted state to all clients. |
| Composer native execution and ORC integration | Callable materialization, invocation, redefinition, typed results, initializer policy, live references and retirement. | Manage session/execution lifetime and display state; do not infer that a replaced symbol is safe to unload. |
| Compiler and toolchain distribution owners | Immutable dependency closure, tool/runtime identities, compatibility information and parity/acceptance evidence. | Install/select reviewed distributions, fence replacement and repeat provider acceptance against their exact identities. |
| Fidelity.Platform | Selected hardware/environment/profile facts, available numeric operations, device access and deployment constraints; adapters need explicit conformance to those facts. | Discover operational availability and route to compatible authorized hosts. Reachability does not establish semantic capability. |
| BAREWire | Layout, extent, ABI and boundary declarations for memory, values and transfers, with ownership/visibility obligations connected to compiler evidence. | Carry declared data and resource identities; avoid serializing raw local pointers as remote values. |
| Farscape and native binding owners | Versioned ABI/calling-convention facts, callback/resource ownership and supported native entry points. | Report missing bindings or incompatible runtime versions and preserve native-handle lifetime. |
| Olivier, Prospero and Ariel | Application effect/actor semantics, supervision/placement and dispatch, including valid quiescence and resource-release observations where supplied. | Coordinate development operations around these runtime contracts; do not create a competing application scheduler. |
| Lattice, Atelier, browser and MCP adapters | Workspace attachment, versioned editing and execution requests, observation subscriptions and explicit presentation of context. | Provide one owner and operation model across interfaces; clients keep presentation state, not independent semantic state. |
| Target adapters and future Bozzetto site nodes | Device/runtime identity, scoped resource ownership, stage/activate/observe/retire operations, cancellation and recovery evidence. | Coordinate application transitions, host selection, admission and reconciliation across participants. |

The [Composer workbench](../../Composer/docs/Interactive_Compiler_Workbench.md),
[numeric selection](../../clef-lang-spec/spec/numeric-selection.md),
[platform predicates](../../clef-lang-spec/spec/platform-predicates.md),
[platform structure](../../Fidelity.Platform/README.md),
[BAREWire declarations](../../BAREWire/README.md) and
[scheduler contract](../../clef-lang-spec/spec/scheduler-contract.md) supply the
existing ownership basis. Their design requirements and reference packages must
not be reported as implemented Bozzetto integrations.

## Host observation and UMA coordination

The [resource observation and UMA contract](Bozzetto_Resource_Observation_And_UMA.md)
is first-horizon infrastructure for FFI, Farscape and compiler-component work.
Bozzetto owns whole-host observation and correlation with admitted operations;
its existing lease owner retains admission. Fidelity.Platform and Farscape/native
binding owners supply platform capability and ABI boundaries, not a second
scheduler. Lemonade remains an independent workload with explicitly observed
configuration and performance.

Physical memory domains, device budgets, shared backing, process attribution and
actual pressure must remain distinct. Neither managed-process RSS nor a GPU
allocation counter is a universal physical-usage total. Portable, versioned
observations carry scope, provenance, interval, coverage and freshness; Linux,
Windows and Apple-silicon adapters establish their own evidence. Native Clef
collection replaces the acquisition edge, not the accounting contract. The design
specifies staged delivery and coexistence experiments; no unimplemented collector
or sibling-project handoff is claimed complete.

## Identity and evidence

Each operation must identify its owning context. The following are conceptual
fields to refine into a versioned schema; they are not new MCP tools or wire
fields in the current provider.

| Identity | Required distinction |
|---|---|
| Workspace and input snapshot | Owner/session, project/configuration/dependency identities, ordered source bytes and overlays. A path or editor document version alone is insufficient. |
| Compiler distribution and epoch | Compiler/toolchain closure digest and live worker incarnation. Rebuilding a checkout does not replace a deployed worker. |
| Target and arithmetic context | Hardware/environment/profile, ABI, selected numeric contract, execution mode and any model or substitutions. |
| Proof context | Source premises, semantic target, actual execution capabilities, representation/lowering policy, assumptions and correspondence obligations. Different realizations cannot inherit acceptance solely from shared source. |
| Selection and evaluation context | Source span plus compiler-resolved entity/occurrence in its snapshot, dependencies, arguments, captures, initialization and resource bindings. A line number is not stable semantic identity. |
| Artifact and application composition | Exact bytes, producer, dependencies, interfaces and the compatible set of artifacts selected for activation. |
| Host and device | Authenticated node/host identity, device identity, attachment route, runtime/reset incarnation and scoped authority. A route change need not change the device; a reset can invalidate its resources. |
| Operation and active execution | Stable request identity, expected state, ownership fence, activation/run identity and actual observed artifact/resource set. |
| Evidence | Source and target scope, producer/checker identity, verdict, original bytes/digest, observation time and any missing history or unverified premise. |

Compilation success, transfer completion, installation, activation and observed
execution are separate facts. Packaging or signing can change artifact bytes;
record the derivation from compiled artifacts to the final installed package.
Never label a requested revision as running merely because its upload succeeded.

Compiler epochs and execution epochs are distinct. An edit can revoke admission
for new invocations while already-admitted frames or device jobs still hold the
previous code and buffers. Their continued existence is observable state, not
permission to submit more obsolete work. Preserve both identities until lifetime
rules permit retirement.

## First horizon workspace and native execution

### Shared incremental foundation

Fidelity.FSharp.Incremental is the selected foundation for incremental dependency
bookkeeping and explicitly started work across Bozzetto's interim .NET host and
Clef/CCS/Baker/Composer. The [adoption contract](Bozzetto_Incremental_Foundation_Adoption.md)
records the initial library source, ownership boundaries and integration gates.
Adoption is planned; the local pre-integration library is not yet a pinned
Bozzetto dependency.

Its core command/effect protocol and replaceable hosting layer should carry the
same lifetime rules into self-hosting. Library eligibility cannot replace Baker's
complete premises or Composer's proof, artifact and execution gates. Agree identity
mapping, shared consumer cancellation, actual child-resource draining, queue and
retention limits, and portable conformance before declaring a consumer integrated.
Cold execution must preserve authority at the actual start/commit point, including
Bozzetto's ordering of artifact launch against edit reservation.

The implemented mailbox coordinator and scoped suspension protocol have a separate
[Bozzetto audit contract](Bozzetto_Incremental_Foundation_Adoption.md#workspace-coordinator-and-scoped-resumptions):
reservation replies acknowledge committed authority changes; resumptions retain
explicit ownership; control and cleanup remain serviceable under queue pressure.
Posting a request, selecting a cancellation path or stopping a mailbox supplies
none of those guarantees by itself.

The [functional assessment](../../Fidelity.FSharp.Incremental/docs/Functional_Async_Auditor_Assessment_2026-10-01.md)
accepts `AsyncMailbox` and preserves F1's repair; consumer integration remains
open. Typed module operations and F# async now provide the preferred hosted
surface, with necessary CLR interop at explicit boundaries. Retained operation
handles support local re-observation after cancellation; adapters still own
transport correlation and restart recovery. Native lowering must preserve the
ownership protocol without requiring CLR Task objects or boxed payloads in
its semantics; see the [authoring contract](Bozzetto_Incremental_Foundation_Adoption.md#functional-async-authoring-and-native-execution).

### Workspace service

Composer must expose one service that owns input snapshots, checking/proof
scheduling, publication and execution admission. Bozzetto attaches editor, MCP and
browser demand to that owner. Opening the same path in another checker does not
attach to the same workspace.

The contract needs ordered edit reservations and overlay transactions with
expected-base identity; shared demand and bounded queues; structured diagnostics
and refusals; immutable artifact/proof observations; explicit cancellation scope;
and stale-publication rejection. Observation subscriptions need resynchronization
after event gaps. Compiler-owned correspondence connects source, graph, lowered
operations and exact artifact bytes.

Concurrency must be specified rather than inherited accidentally from CLR thread
pool behavior. Define which compiler state is process-global, which operations
serialize, what solver work consumes immutable inputs outside that section, and
the limits for processes, threads, queues and cancellation. Native hosting must
preserve these decisions, with measured changes made explicitly.

### Interactive semantics and execution

The compiler must specify binding and redefinition, initialization frequency,
effects, captured mutable state, closures, callbacks, exceptions/failures and
target access. Composer's execution service must carry these rules through actual
materialization and invocation. A source proof or symbol-compatible replacement
alone does not establish that live state is compatible.

Result inspection needs compiler-described types, layouts and bounded value
access. Formatters produce labelled presentations of those values. Inspection
must not silently force lazy computations, rerun initializers or traverse invalid
native handles. An effectful inspection is a separate admitted operation.

Retirement requires evidence that no live frame, closure, callback or external
resource can still reach the retired code/data. If state cannot be preserved,
the contract returns a supported migration, restart requirement or refusal. The
host must not hide state loss behind successful evaluation output.

### Distribution promotion

The compiler owner supplies a validated immutable closure and identifies the
parity scope, native tool dependencies and known gaps. Bozzetto coordinates
withdrawal of the old worker, physical retirement and activation of its
replacement, then exercises the provider journey against that exact closure.
CLI deployment is independent. Neither installing new CLI help nor building new
compiler source proves a changed compiler is serving requests.

## Second horizon evaluation contexts

This section defines the required behavior if the demand-gated capability is
selected. Feasibility and proof cost must be established for a bounded set of
realizations before promising general availability.

The request is to execute selected application code in an identified context.
The selection may be a function, expression or supported region in the existing
project. Acceptance must include code with actual application dependencies and
state; a required `Common` directory or pure-helper rewrite would narrow the
product objective incorrectly.

The compiler's proposed evaluation-plan response must supply:

1. The selected source identity and valid entry/control-flow boundary, including
   resolved private helpers, captures, generic specialization and dependencies.
2. Required arguments, initialization, mutable state and resource bindings, with
   a lawful snapshot/capture boundary and lifetime requirements.
3. Available execution modes: CPU realization, CPU execution preserving selected
   target semantics, named emulator/model, or actual target execution. Missing
   capabilities and unsupported operations remain explicit.
4. The chosen arithmetic and effect contracts, including any supplied inputs,
   models, substitutions or remote operations. A substitute is never implicit.
5. Admitted callable artifacts and interfaces, evidence prerequisites, invocation
   limits, result observation and disposal/retirement requirements.

Bozzetto presents unresolved inputs, binds authorized resources and dispatches
the admitted plan. Composer revalidates the plan against current source, target
and execution context before invocation. Native pointers, device handles and
captured references are not portable merely because their surrounding function
can compile for a CPU.

The evaluation plan must preserve the selected code's semantics or state exactly
which explicit alternative context is being evaluated. Rerunning initialization
to reconstruct state can have effects and cannot substitute silently for the
original state. Arbitrary source selection remains a goal even when execution
requires a particular device; a CPU-only interpretation is not mandatory.

### Numeric and effect models

CCS/Baker and the target description own representation choice and validity.
Required arithmetic observations include widths, scaling, rounding order,
overflow behavior, FMA/reassociation permissions, special values, accumulator
capacity and transfer representation where applicable. Use the
[numeric acceptance cases](../../Composer/docs/PRDs/Numeric_Validation_Cases.md)
to discriminate a faithful implementation from a plausible result.

Reference calculations, target-mode CPU execution and physical execution retain
different evidence scopes. Comparison can require exact bits, a specified error
bound or another compiler/application-declared relation; Bozzetto must not pick
an arbitrary tolerance to turn a mismatch green. Model coverage is explicit for
device effects, interrupts, synchronization and timing. Full processor emulation
is separate work, not an implied dependency of every preview.

### Proof contexts across realizations

The compiler must distinguish three cases: a CPU-native realization with its own
admitted numeric choices; a physical-target realization; and CPU execution that
implements specified target semantics. Each carries its own proof and artifact
context. Changing the target is not just choosing a different code generator
after all semantic and proof work is complete.

Differences can change range/precision obligations, intermediate representations,
operation order, layouts, ABI, resource access, memory visibility, atomicity and
concurrency premises. New obligations may appear, others may no longer apply,
and apparently identical queries may have different surrounding assumptions.
Bozzetto must display the verdict for the selected realization rather than carry
forward a previous green result.

For execution preserving target semantics on a CPU, record both the semantic
contract being modeled and the actual CPU execution environment. The compiler
must establish the relevant correspondence between the admitted target operations
and their CPU implementation. It cannot claim that the CPU acquired a hardware
capability merely because the model names it. Unsupported device effects or
unestablished correspondence remain explicit limitations or refusals.

CCS/Baker and Composer own obligation construction, invalidation and any permitted
reuse, following the [proof composition contract](../../Composer/docs/Proof_Composition_Architecture.md).
Reuse requires complete applicable dependencies: source and captures, selected
platform facts, arithmetic/effect policies, resource assumptions and the relevant
lowering/model implementation. Shared source text, matching solver input or a
successful CPU result alone is insufficient. Conservative rechecking remains
valid until a narrower reuse claim is demonstrated.

The feasibility record must compare the proof work and latency across proposed
realizations as well as their execution results. An implementation that produces
correct answers but multiplies checking cost beyond the workflow's needs may not
justify the investment. This is a product and engineering decision before the
tranche is committed, not a late optimization exercise.

## Second horizon application transitions

### Composition and target capabilities

Composer supplies the dependency and interface manifest for the application's
CPU code, accelerator artifacts and other deployable parts. Fidelity.Platform,
BAREWire and binding owners supply the target/layout/resource requirements. The
target adapter supplies actual availability and supported transition operations.
The participating owners must establish agreement before Bozzetto activates a set.

Each adapter must report supported update modes, required safe boundaries,
state capture/migration capabilities, resource acquisition/release, completion
observations and recovery limits. An NPU program/model reload, MCU flash and FPGA
reprogram are not assumed to have the same semantics as CPU code replacement.
Unsupported transitions must still permit an accurately labelled supported
restart/reprogram workflow where available.

### Transition lifecycle

The coordinator requests a plan against the currently observed execution set.
The plan identifies compatible retained artifacts, replaced artifacts, affected
state/resources and each participant's activation boundary. The required lifecycle
is:

1. Validate the new composition and obtain current resource authority.
2. Prepare and transfer verified artifacts; stage them where the target permits.
3. Stop admitting incompatible new work and reach the declared safe boundary.
4. Preserve, migrate or recreate state according to the admitted plan.
5. Activate the compatible set and collect per-participant execution receipts.
6. Retire previous code/resources only after their final users complete.

Targets unable to stage before stopping report that interruption explicitly.
Global atomic activation is not assumed. Where mixed activation cannot preserve
compatibility, use a declared stop/start boundary or refuse the transition.
Failed stages leave an observable state with permitted recovery actions. Rollback
needs a real target capability and a valid state strategy; prior external effects
are not undone by restoring an older binary.

### Memory and resource handoffs

Shared memory needs layout, extent, access, ownership, visibility and completion
contracts. The application must not replace a CPU buffer layout while an older
GPU/NPU job can access it. Keep old allocations alive, drain work, or use an
admitted migration. Shared physical backing does not create device mappings or
remove cache/synchronization obligations. Consult the
[platform handoff design](../../Fidelity.Platform/docs/ADMISSION_AND_SIDECARS.md)
and [BAREWire platform declarations](../../BAREWire/docs/11%20Platform%20Description.md).

For distributed resources, identical bytes do not imply interchangeable live
handles. Transfers carry declared layouts and resource ownership; the receiving
adapter establishes local handles and reports their lifetime. Device reset,
runtime replacement or lease fencing invalidates affected handles explicitly.

## Second and third horizon host coordination

### Hosts and destinations

Model the workstation, compiler owner, build/package host, execution host, device
and transport route independently. A Mac may build/sign an iOS package while a
Linux host installs it on a locally attached phone. A remote GPU host may compile
or execute while datasets remain in its own site. These are proposed arrangements,
with support contingent on validated adapters and platform requirements.

Discovery reports capabilities and operational readiness separately: reachable,
paired, authorized, toolchain-compatible, busy and ready are different states.
Authentication and project/device authorization govern use. Signing credentials
remain with their authorized owner. Discovery does not grant device control.

The existing [loopback configuration](../Bozzetto.Core/BozzettoConfig.fs) rejects
LAN binding because the current service lacks authentication. Remote coordination
requires an authenticated boundary and explicit host enrollment; changing the
bind address is not its implementation. A narrowly scoped authenticated tunnel
can support an initial experiment, but does not supply device ownership, remote
operation durability or the federation contract by itself.

### Remote authority and operations

Each operation needs a stable identity and an expected target/execution state.
The executing host persists enough status to reconcile a missing reply or node
restart. Duplicate delivery returns the known outcome or enters reconciliation;
operations with uncertain physical effects are not blindly repeated. A transport
timeout is not a definitive device-operation failure.

Host/device leases must be scoped and enforced at the executing resource owner.
A fencing token identifies the current grant; an older token cannot authorize
queued activation after ownership changes. Define the physical effect boundary,
including what can finish after expiry. The policy for a disconnected node is
explicit: which already-admitted operations can finish, which new work is refused,
and how authority is renewed. The current local expensive-work lease pool does
not establish these distributed guarantees.

Cancellation requested, cancellation acknowledged, process stopped, device work
drained and resources released are separate observable states. Reconnect queries
the owner and reconciles them with the client's request history. Compiler epoch,
node restart and device reset fences all apply independently. Cross-site failover
must establish a new owner and fence the old one rather than silently presenting
two mutable workspaces as one.

### Scheduling and observations

Bozzetto schedules build/test/deploy/inspect operations with bounded admission and
declared priorities. Compiler services retain compiler/proof scheduling; runtime
owners retain application scheduling. Nodes exchange operations and evidence
across latency domains, not actor-turn dispatch.

Expose queue time, compiler work, artifact transfer, preparation, activation,
interruption and target execution separately. Correlate events by operation and
artifact identity with clock provenance; wall-clock timestamps from different
hosts alone do not establish order. Bound event buffers and mark lost history;
after reconnect provide a current snapshot and the available operation history.
Bulk data can use a suitable direct transport with verified endpoints and bytes,
without making the workstation the mandatory relay.

## Acceptance and delivery responsibilities

| Boundary | Required discriminating evidence |
|---|---|
| Shared workspace | Human/editor/MCP requests use the same admitted snapshot; conflicts, cancellation, supersession and epoch changes cannot publish stale success. |
| Native REPL | Actual materialization/invocation and retained state/callback behavior, with failed replacement and final-reference retirement. Host simulations alone are insufficient. |
| Arbitrary application selection | A function/region in existing application structure executes with resolved dependencies and explicit state/effects, without extraction into a shared module. Missing context is actionable. |
| Numeric and proof context | Cases that distinguish rounding, representation, ordering and overflow; change target capabilities and require affected obligations/verdicts to change, with explicit reuse evidence and unsupported-model refusals. |
| Heterogeneous update | Real work holds an old artifact/resource while a new compatible set is prepared; activation and retirement preserve declared lifetimes and expose partial failure. |
| Independent hosts | Build/package and device hosts differ; exact final bytes and actual running identity are observable on the physical destination. |
| Remote recovery | Lost acknowledgment, duplicate delivery, node restart, device reset and stale ownership cannot cause an unobserved second effect or obsolete activation. |
| Native host | The same portable contracts and required lifecycle gates pass without the .NET runtime in the deployed Clef host. |

The component that owns a semantic or target boundary supplies its implementation
and conformance evidence. Bozzetto owns adapter, coordination and complete user
journey acceptance. Independent audit retains its own scope and evidence.
Register the relevant tests in whole-suite acceptance tiers, record execution
counts and preserve actual hardware/toolchain identities. Passing a fixture for
one target or transition does not close another target's positive requirement.

For implementation, agree a versioned bounded handoff with the named owners at
each selected tranche. Exploratory second- and third-horizon work first requires
its demand and feasibility decision; this adds no gate to first-horizon CPU REPL
work. Refine these conceptual fields into schemas and conformance cases,
then update this document with links to delivered interfaces and acceptance.
No repository outside Bozzetto is changed or assigned completed work by this
design record.
