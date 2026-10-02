# Adopting Fidelity.FSharp.Incremental

October 1, 2026. Fidelity.FSharp.Incremental is the selected shared foundation for
incremental dependency bookkeeping and explicitly started work during Fidelity's
interim .NET hosting. Bozzetto and Calque are consumers alongside Clef, CCS, Baker
and Composer's coordinated compilation pipeline. This is first-horizon engineering
work toward self-hosting, independent of the exploratory cross-target horizons.

The direction is established; consumer integration and acceptance are in progress.
The accepted functional API baseline is
`613e2600c1f1696eb0a363b18d8ecdd7c9b2764b`, with implementation and preview.4
packages pinned to `d3239c26cf4de5e06babd541f576d9466e7a1986`. The
[functional assessment](../../Fidelity.FSharp.Incremental/docs/Functional_Async_Auditor_Assessment_2026-10-01.md)
accepts the typed async surface for integration work after a fresh Release build,
99 passing tests, eight lifetime controls and an independent fault-probe repeat.
The [original assessment](../../Fidelity.FSharp.Incremental/docs/Mailbox_Auditor_Assessment_2026-10-01.md)
and [F1 follow-up](../../Fidelity.FSharp.Incremental/docs/Mailbox_Auditor_Followup_2026-10-01.md)
remain preserved historical evidence.

The additive preview.5 observation API at
`87c77d91ab24c8e4d065e4e726f8c06abc3180c4` has a separate
[independent assessment](../../Fidelity.FSharp.Incremental/docs/Consumer_Observation_Auditor_Assessment_2026-10-01.md):
102 Release tests and three package-only observation controls passed. The
`613e260` assessment remains unchanged. Bozzetto's source adoption and its
remaining consumer/deployment gates are tracked in the
[provider checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md).

The local [README](../../Fidelity.FSharp.Incremental/README.md) and
[architecture](../../Fidelity.FSharp.Incremental/docs/Architecture.md) describe its
current contract. These sibling-checkout links are inspection references, not a
published dependency pin. The library draws on lessons from FSharp.Data.Adaptive
and IcedTasks and has its own implementation; it does not depend on or wrap those
libraries. Its API names below describe the initial source and may evolve before
adoption.

## Shared machinery, explicit owners

The reusable [core](../../Fidelity.FSharp.Incremental/src/Fidelity.FSharp.Incremental/Core.fs)
processes commands over explicit identities and dependency reads, returning new
state and ordered effects. The separate [.NET hosting layer](../../Fidelity.FSharp.Incremental/src/Fidelity.FSharp.Incremental.Hosting/Host.fs)
owns evaluation, cancellation and draining. That separation provides a concrete
porting boundary: a later Clef/native implementation should preserve the protocol
and its observable behavior while replacing the host machinery.

| Consumer | Intended use | Authority that remains with the consumer |
|---|---|---|
| Clef / CCS | Track demanded checking work and dependencies across identified source revisions. | Source meaning, checking rules and the compiler's serialized state boundary. |
| Baker | Track the dependency and lifetime conditions of semantic/proof work. | Complete premises, settlement, proof validity and permission to reuse evidence. |
| Composer | Coordinate demanded stages, invalidation, reuse candidates and owned execution lifetimes. | Pipeline ordering, proof discharge, artifact correspondence, accepted publication and execution admission. |
| Bozzetto | Coordinate workspace demand from clients, revision-bound observations, queued operations and supervised worker lifetimes. | Client/lease admission, routing, compiler epoch fences, external process ownership and faithful presentation of compiler evidence. |
| Calque | Coordinate demanded formatting over immutable document incarnations, source revisions and configuration. | Source/trivia/layout preservation, supported-syntax refusals and exact preview identity. Formatting never grants source mutation, proof, artifact or execution authority. |

Using one library does not mean sharing a mutable graph across process boundaries.
Each owner must identify the state it owns and exchange versioned observations or
requests with its peers. Bozzetto must not rebuild the compiler's semantic graph
from paths, diagnostics or emitted artifacts. Application runtime scheduling keeps
the ownership described in the [component contracts](Bozzetto_Fidelity_Component_Contracts.md).

The October 2 integration adds Calque's explicitly started .NET-hosted document
formatter and `composer_format_preview` on the existing provider protocol. A
preview identifies its exact input and policy; applying it requires the client's
current-buffer check and a successful Composer reservation before source mutation.
Calque and Composer select the reviewed preview.6 foundation. Deployed formatter
and compiler closures must contain identical foundation assembly bytes. Their
owners remain separate, and Calque still parses and formats whole documents;
incremental syntax/trivia reuse and editor latency budgets remain open work.
Executed gates and deployment status belong to the
[cross-project checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md).

An `Offer` means that the library's declared dependency and lifecycle conditions
hold. It does not authorize proof reuse, PSG publication, native artifact
acceptance or execution. Baker and Composer still decide those outcomes, including
the completeness of dependencies that the library cannot infer.

## Cold work and incremental reuse

Here, **cold work** means an operation description or evaluator that has not yet
started. It differs from a **cold build**, which starts without reusable compiler
results. A cold operation can eventually consume retained results; constructing
it or holding it in an unadmitted queue must not start compiler, solver,
filesystem or device effects.

The preferred `AsyncMailbox` host accepts a fresh evaluator invocation of
`StepInvocation -> WorkCancellation -> Async<StepOutcome>`. Its explicit stop
request preserves owned cleanup independently of observer cancellation. The
host already interprets lifecycle effects: observing `Start` must not launch the
same work a second time. Reusing an eligible result is also distinct from reusing
an executing task or repeating an effectful operation.

Supersession is implemented in the shared foundation. Input changes and scope
reservations withdraw affected publication before requesting cancellation.
Releasing one demand preserves a producer still needed by another consumer.
The host joins the evaluator and its cancellation callbacks before acknowledging
`Drained`; another attempt for the same `WorkId` cannot start before that join.
Independent work can still run within the configured concurrency limit.

`WorkCancellation` carries this request explicitly. The host runs owned workflows
without ambient F# cancellation so that cleanup and its failures cannot be
detached by a canceled observer. Evaluators must cooperate with the carried
request and include their children and cleanup in completion. Currently Calque's
full-document parser/printer does not inspect that signal mid-computation: its
adapter withdraws obsolete output immediately and owns the parse/print until it
returns. This establishes safe supersession, not prompt interruption inside every
compiler or formatter phase.

Bozzetto's formatter adapter accepts `RequestPreview` under the provider's
generation lock and returns a demand with a cold `Async` owner workflow and an
exact withdrawal operation. It no longer relies on a task's eager prefix for
ordering. A new compiler generation withdraws registered formatting demands;
request cancellation withdraws only that request. Accepted demands and their
release controls remain owned through session close. Each observing consumer
gets its own demand handle while Calque shares the underlying snapshot work.

There is a specific migration hazard in Bozzetto's current
[backend contract](../Bozzetto.Composer/ProviderContracts.fs): `RunCurrentAsync`
selects and launches its artifact synchronously before returning its task, so
reservation can be ordered against that invocation. Deferring invocation changes
where that ordering must hold. The adapter must recheck authority at the actual
launch/commit boundary and preserve serialization with reservation; validating a
queued description is insufficient. Do not replace task syntax mechanically and
assume the old race guarantees still apply.

The intended lifecycle is explicit demand, admission, start, completion, resource
draining, then eligibility. In the current protocol `Finished` precedes `Drained`,
and a fresh attempt's success requires current demand and unchanged premises when
draining completes. Retaining an already drained cached result can issue fresh
eligibility without new execution or a new demand. The .NET host owns completion
and drain acknowledgements. Its evaluator must include owned child work and
cleanup before finishing; a wrapper task completing cannot certify that a compiler
subprocess, callback or native resource has stopped. Bozzetto's physical retirement
evidence and cleanup failures must remain observable.

## Functional async authoring and native execution

Prefer a quiet API of small module operations, typed data and pure F# `Async`
workflows during interim .NET hosting. Necessary CLR interoperability belongs at
explicit execution boundaries. Do not use boxed payloads, `:> obj`, unchecked
casts, reflection or object hierarchies to conceal missing semantic or lifetime
contracts. Owner-issued value tokens should refer to typed immutable stores or
explicit payload unions, including suspension environments.

The accepted `AsyncMailbox` surface now follows this direction using an F# async
coordinator and typed settlement cells. `create` is cold, `start` explicitly
starts coordination, and `admit` immediately returns an exact operation handle.
`observe` is cold and repeatable; it does not admit the command again.
`beginClose` immediately seals admission, while `close` does so when its workflow
executes. Cleanup remains owned until children and callbacks join, even after
observer cancellation. `ClrInterop` names the required Task/token boundaries;
`MailboxHost` is the CLR compatibility adapter and the separate `Host` remains
legacy. Select the functional surface for the new integration.

Native lowering needs explicit suspension/resumption, captured state and settled
ownership; it does not inherently require a CLR `Task` object. F# async provides
the preferred authoring direction, while the compiler and native host must
establish the corresponding semantics and execution. Keep portable command/effect
traces and real ownership tests as conformance evidence. Merely using today's
F# async runtime does not demonstrate Clef compilation or self-hosting.

The [early concurrency post](https://clef-lang.com/blog/dotnet-to-fidelity-concurrency/)
is historical direction. Its illustrative APIs and component assignments are
not the current specification. The current requirement comes from the user's
explicit async preference and the reviewed ownership contracts.

## Contracts to settle with the library and compiler owners

- **Identity mapping:** map library epochs, scopes, revisions, attempts and demands
  to their owning compiler/workspace and client operations. Compiler generations
  and Bozzetto adapter revisions remain distinct. Library numeric tokens do not
  establish host identity, distribution provenance or authorization. Preserve
  public wire identities independently of process-local tokens and CLR objects.
- **Complete dependencies:** compiler owners supply ordered read occurrences,
  source/configuration/target inputs and the premises needed for proof reuse.
  Negative facts such as the absence of additional uses need a versioned census
  or whole-revision input. The initial library accepts acyclic work; compiler
  cycles need an explicit owner-defined decomposition or a future contract.
- **Reservation and publication:** reserve before source mutation, process
  withdrawals before granting edit permission, and reject obsolete completion.
  Retaining unchanged work issues fresh eligibility under the new revision;
  old handles stay revoked. Checking eligibility and committing an external
  effect must have defined ordering with concurrent reservation and retirement.
- **Shared demand:** closing one editor, cancelling one wait or releasing one
  client's interest must not cancel work another consumer still needs. Define
  the separate operation that cancels shared work or closes its owning scope.
  Distinguish demand lifetime from retention of an already eligible cached result.
  Status/evidence reads observe state; they do not implicitly request a build.
- **Capacity and cleanup:** specify queue admission, solver/process concurrency,
  overload/coalescing policy, cancellation and teardown separately. The current
  host's `maxConcurrency` bounds evaluator lifetimes; it does not establish bounds
  for queued demand, event/diagnostic buffers, payload storage or identity
  tombstones. Drain observations and define retention/epoch-recycling policies.
- **Payload and recovery ownership:** specify who stores immutable values behind
  tokens, when those values can be released, and which observations survive a
  disconnect or process restart. Idle is a momentary observation, not a global
  admission lock. Wire versioning and durable recovery are integration work.
- **Native conformance:** retain portable command/effect fixtures and observable
  lifetime requirements. Deterministic core replay can compare exact transitions;
  real host races must also test invariants under controlled interleavings.
  .NET `Task`, `CancellationToken`, locks and semaphores belong to the interim
  host. A native/pthread host supplies its own mechanisms and must pass the same
  ownership, publication and cleanup gates.

## Workspace coordinator and scoped resumptions

Hosting now provides `AsyncMailbox` with bounded external command admission and
separate active-step capacity. A `WorkflowEvaluator` returns completion or a
resource-free checkpoint; exact one-use `SuspensionHandle` values support
resumption under the original attempt and declared reads. `WorkStatus.AwaitingResume`
describes that held checkpoint. The older `WorkStatus.Suspended` still describes
a scope that is not open. The original non-step `Host` remains a separate API.

The audit accepts the bounded core/mailbox tranche for integration work. The
original `Host` cleanup defect, F1, is closed by the
[repair follow-up](../../Fidelity.FSharp.Incremental/docs/Mailbox_Auditor_Followup_2026-10-01.md):
both hosts passed the independent check that close joins evaluator and callback
ownership before reporting a protocol fault. The functional audit repeated that
check through the new async engine. Standalone acceptance does not settle the
adapter gates below or establish native compilation.

The audit includes an independent document-analysis probe using immutable owner
payloads, shared client demand, changed-input invalidation and independent reuse.
These contracts are intended to serve noncompiler consumers too. Application
authority, request reconciliation, overload policy and total-memory budgets
belong in explicit adapters; Bozzetto-specific identities should not enter the
portable core.

### Mailbox processing and acknowledgements

A workspace coordinator should own short state transitions and dispatch expensive
work outside its receive loop. Completion returns as a message. Awaiting the
whole build inside that loop would prevent it from handling reservation or
cancellation, even if the await releases an OS thread. Preserve a coordinator per
defined ownership boundary, rather than serializing unrelated workspaces behind
one global inbox.

F# distinguishes posting a queued message from asynchronously obtaining its
reply; the application defines what that reply means. This is the useful interim
hosting mechanism described by the [MailboxProcessor API](https://fsharp.github.io/fsharp-core-docs/reference/fsharp-control-fsharpmailboxprocessor-1.html).
The portable contract must specify commands, correlation and acknowledgement
state independently of `MailboxProcessor` or `AsyncReplyChannel` types.

Bozzetto requires these observable guarantees:

- **Receipt and permission are separate.** Enqueue acceptance grants no right to
  edit. A successful reservation acknowledgement identifies the request,
  workspace, epoch and revision, and confirms the required withdrawals and
  admission fences have committed, including the compiler-owned reservation.
  Physical cancellation and draining can remain pending with explicit status.
- **Lost replies can be reconciled.** A cancelled wait or timed-out reply is not
  evidence that the command never committed. Keep source writes unauthorized
  until the exact request is reconciled. Retain the exact `AsyncMailbox.Operation`
  before observing it; cancelled observations can be repeated without resubmission.
  That local guarantee does not supply a transport or durable request identity.
  Define duplicate-request handling and
  how coordinator failure or restart invalidates outstanding acknowledgements.
- **Launch has a final authority boundary.** Ordering a launch message before a
  reservation message does not establish physical launch order when workers run
  elsewhere. Composer must order actual artifact validation/launch against
  reservation. Pause after scheduling or a provisional launch permission, then
  let reservation win: the old operation must cause zero launches. Also test the
  opposite ordering and preserve the lifetime of work that already launched.
- **Control remains available under load.** Bound ordinary work admission and
  specify a delivery path for reservations, cancellation, completion and
  retirement under saturation. Terminal events must not be dropped or stranded
  behind a full work queue. Define fairness and any permitted reordering; measure
  control latency while an evaluator or cancellation callback is stalled.
- **Shutdown accounts for every accepted operation.** Stop new admission, settle
  or explicitly fail pending replies, invalidate resumptions and drain owned
  children. Stopping the receive loop alone cannot certify cleanup. User
  callbacks and potentially blocking work remain outside coordinator transitions.

### Suspension ownership and native representation

Scoped suspension is an additional execution capability. An ordinary await,
scope reservation or cancellation request does not prove that an attempt has
reached a safe suspension point. Define when suspension is acknowledged and
which resources remain owned while it is suspended.

A resumption needs its owning epoch, scope, attempt and suspension identity,
with the relevant revision/premises and typed captured state. Its authority is
single-use: competing resume/cancel/retire paths select one permitted transition.
Duplicate delivery must not execute a step or external effect twice. A later
suspension gets a new identity, and invoking a reusable workflow again creates
fresh execution ownership. Dependency changes or epoch replacement cannot let
an old resumption acquire current authority.

The [F# async paper](https://tomasp.net/academic/papers/async/async.pdf) provides a
reference model for suspension and separate success, exception and cancellation
paths. The public [Async.FromContinuations contract](https://fsharp.github.io/fsharp-core-docs/reference/fsharp-control-fsharpasync.html#FromContinuations)
requires one terminal choice. For this library, selecting cancellation still
leaves an obligation to account for callbacks, suspended captures and child
resources before `Drained`. Losing publication authority must not prevent the
cleanup path from running.

For a defined workflow vocabulary, a program-step identifier plus typed captured
state is a candidate native representation. This is a design requirement to
validate, not a claim that existing .NET closures can be serialized. Baker/PSG own
the capture, effect and lifetime settlement for arbitrary source workflows.
Native resource handles need explicit ownership and release rules; portability
does not imply durable restart or migration of a suspended process/device state.

The [Computation Expression Zoo](https://tomasp.net/academic/papers/computation-zoo/)
explains builder-defined execution models and deliberate handling of delayed
effects. A later computation builder should elaborate into the same explicit
operations. Its syntax must preserve declared dependency reads, fresh invocation
ownership, launch admission and cleanup, with equivalent traces to direct API
use. General multi-shot continuations remain outside this initial contract.

### Coordinator audit handoff

The owner should provide the precise source/package identity, protocol changes,
executed test receipts and a mapping from each claim to a test. Bozzetto's audit
adds the real workspace/worker boundary: reservation acknowledgement before a
source write; reservation winning between scheduling and launch; timeout after
commit; duplicate or stale resumption; invalidation while suspended; two clients
sharing demand when one leaves; and cancellation, coordinator failure and close
with callbacks or real child processes still outstanding. Include saturated
queues and a blocked evaluator while measuring control responsiveness.

Library tests establish the protocol implementation. Bozzetto's provider journey
must separately establish the adapter, compiler authority and real cleanup. A
future native host must replay the portable cases and satisfy the same lifecycle
requirements. The standalone
[functional assessment](../../Fidelity.FSharp.Incremental/docs/Functional_Async_Auditor_Assessment_2026-10-01.md)
records the completed library checks and any findings. The real Bozzetto/Composer
integration audit remains pending.

The user relayed two concrete Clef-agent findings during this audit: Composer and
editor paths use separate locks around shared compiler state, and a Bozzetto
cancellation path can retire a result before cleanup joins. The agent is working
on shared whole-project checking and joined lifetimes. These are reported
integration blockers, not independently reproduced library defects. Acceptance
must show one compiler-owned serialization boundary and separate observations
for immediate authority withdrawal versus completed physical cleanup. Preserve
the existing proof and artifact gates throughout those repairs.

## Adoption sequence and evidence

1. **Establish a reproducible library input.** The library owner records its own
   executed tests, limitations and an immutable source/package identity. Preserve
   it in the intended repository and agree the version consumed by each component.
   Local integration experiments can use a recorded source snapshot; they are not
   promoted dependency evidence. Include the adopted library and host in compiler
   distribution and Bozzetto package manifests as applicable.
2. **Deliver one bounded end-to-end use.** Coordinate a compiler-owned workspace
   workload with Bozzetto client demand, explicit source reservation, incremental
   build and accepted evidence. Exercise both a fresh build and selective reuse,
   and compare the resulting authority, output and refusals with the existing
   provider journey. Integrate through the actual library host and real compiler
   worker; isolated library examples cannot establish this boundary.
3. **Expand throughout the intended consumers.** Track adoption in Clef, CCS,
   Baker, Composer and Bozzetto with an owner and acceptance record for each
   boundary. Move new shared-workspace observations and coordination onto the
   common protocol, then retire superseded mechanisms after equivalent behavior
   is demonstrated. One Bozzetto adapter does not establish framework-wide use.
4. **Exercise the native replacement.** Reuse the core protocol and conformance
   fixtures against the native host, including delayed completion, cancellation,
   failed cleanup and compiler replacement. Deployment without .NET remains the
   self-hosting exit criterion; a portable F# core alone does not achieve it.

The first integration gate must distinguish no-work-before-admission, two
consumers sharing one producer, one consumer leaving, reserve-before-write,
unchanged-result retention with fresh handles, an omitted proof premise, late
completion after revision/epoch replacement, failure while draining, queue
pressure, and an uncooperative real child process. Existing build/run admission
and unfiltered provider tests remain acceptance requirements. Record the exact
library/compiler/host identities, execution counts and observations together.
Measure cold and reused builds separately, including duplicate work, queue delay,
latency and retained memory; reuse alone is not a performance measurement.

Bozzetto currently references FSharp.Data.Adaptive in the daemon and Core. Its
[live-bindings store](../Bozzetto.Core/Features/LiveBindingsAdaptive.fs) uses cells,
transactions and subscriptions for retained F# implementation. The new library
does not provide drop-in adaptive collections or automatic dependency discovery.
Inventory those uses and explicitly replace or retire them as their owning
features change. The first adoption should serve the current Composer/workspace
direction; rebuilding a retired F# product surface is not a prerequisite.

These records change documentation only; the library checks do not establish
Bozzetto adoption. Dependency references, runtime behavior, the installed daemon
and the promoted compiler distribution are unchanged. The
[development plan](Clef_Composer_Development_Plan.md) owns implementation ordering;
the [horizons](Bozzetto_Development_Horizons.md) retain the broader product goals.
