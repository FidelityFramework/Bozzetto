# Callable aggregates: Phase B design

October 3, 2026. Implement [D6(b)](FFI_Correction_Rulings_2026-10-03.md#owner-decisions-on-the-step-1-plan-october-3-2026) through one aggregate protocol for `FnPtr` record fields, ordinary function fields and callable union payloads. Retain the affected original positive tests. Native ABI settlement in Step 1 will extend this foundation.

## Required order before the schema batch

The owner-forwarded auditor review approves the direction, placement and R4
activation-proof choice, subject to the following work preceding schema changes:

1. Land the normative interior-record text in FFI §3.6, callable components in
   Closure Representation §2.4, and DU callable payloads in §9.1 and requirement
   12. These portions of the Step 1 spec prelude move into Phase B. Include the
   corresponding backend and NTU consistency edits; native table realization
   remains Step 6.
2. Fix the integrity and test obligations enumerated below before adding rows.
3. Allocate the batch's diagnostics in `spec/error-handling.md` before producing
   them. CCS8410–CCS8415 are allocated for this batch; lifetime failures retain
   CCS8100–CCS8102. Allocation alone is not implementation evidence.
4. Admit interior `Option<FnPtr<'F>>` through this batch's union-payload protocol:
   `Some` holds a callable component and `None` has no payload. C-boundary absence
   conversion between Clef `None` and C's single-word NULL carrier stays in Step 4.
   This never introduces a null value or null comparison into Clef. The
   interior case does not depend on that conversion's implementation.
5. Include every callable aggregate row and its actual participants in the
   published dependency account, as specified below. The schema implementation
   and its tests follow these landed contracts.

The normative prelude and code allocations landed first in clef-lang-spec
[`82f0077`](https://forge.spkez.dev/FidelityFramework/clef-lang-spec/commit/82f0077f614167e3523bc6380ee470e1180f5fb0).
The following schema rows, diagnostic producers and paired tests remain
implementation work; this spec commit does not claim their acceptance.

## Contract extension

Fidelity.PSG at `f153c75` declares schema 16 in [Revision.fs](../../Fidelity.PSG/src/Fidelity.PSG/Revision.fs). Extend the existing [CallableCarrier and CallableEmissionProjection](../../Fidelity.PSG/src/Fidelity.PSG/Codata.fs), with these proposed records:

| Record | Proposed extension |
|---|---|
| `CallableCarrier` | A kind distinguishing ordinary flat closures from native entries. Retain the formation occurrence and implementation. Add the actual environment-value occurrence, where present, alongside the existing environment owner/formal relation. Carry typed lifetime and contract participants. |
| `CallableAggregateSlot` | The instantiated aggregate `TypeIdentity`, its declaration node and a resolved slot path. A path identifies a record field or a union case's payload ordinal, including nested aggregates. Field text alone supplies no identity. |
| `CallableAggregateValue` | The aggregate occurrence and slot, with alternatives referring to carrier occurrences. Construction, projection and assignment retain their actual source participants. Mutable reads identify their snapshot frontier. |

Add these aggregate rows to `CallableEmissionProjection`. Reuse `CallableFlow`, `CallableJoin` and the mutable-callable construction/selection machinery. Extend `ValueRepresentation` to describe data placement and callable components separately for records and unions. A callable payload receives the same component representation as a callable field.

Bump the contract once for this foundation, to 17 if schema 16 is still current. Regenerate binary, JSON and integrity files with the clef mirrors and consumer matches in the same checkpoint. Keep subsequent Step 1 schema numbers dependent on that accepted checkpoint.

## PSG integrity and paired tests

Each rule is enforced by source-owned settlement and by public PSG integrity
validation. An integrity refusal is not an opportunity for publication or Alex
to repair the graph. Source-facing commitment diagnostics use the allocated
codes; a standalone malformed-image check must not fabricate a source location.
The positive and negative cases below are required tests for the schema batch,
not tests claimed to exist already.

| Rule | Positive case | Negative case | Source diagnostic |
| --- | --- | --- | --- |
| I1: No code value in a data slot. Layout contains only data, environment views and admitted selectors; code remains a function value. | Record field and union payload reconstruct a portable callable from settled components. | Put a code value in a data field, byte extent, integer/index carrier or environment slot. Reject before witnessing. | CCS8410 |
| I2: Exact selector domain. For `n > 0` settled alternatives, the logical selector range is exactly `[0, n)` with one alternative per value. A singleton may elide storage; a missing payload has no selector. | Singleton and multi-alternative families cover every selectable member exactly once. | Extra or missing selector values, duplicate alternatives/ordinals, or treating `None` as a callable alternative. Wider physical storage does not authorize extra logical values. | CCS8411 |
| I3: Code and environment belong to the same formation. Closed environment absence is explicit. | Two formations of one implementation keep distinct environments; a copied shared capture retains its cell identity. | Cross the environments, reuse the wrong formation, or drop a required environment while keeping the implementation unchanged. | CCS8412 |
| I4: One receiving contract identity per callable slot. Every alternative conforms or passes through an explicitly settled adapter. | Alternatives carry the same receiving contract while retaining their own formation and lifetime participants. | Join distinct contracts with identical source types or ABI shapes, or omit the required contract identity. | CCS8413 |
| I5: Formation, selection and union tag evidence is current and complete. | Construction and elimination carry exact live formation and constructor/tag participants. | Keep cached annotations but remove, duplicate or change a required formation/tag row or its operand incidence. | CCS8414 |
| I6: Every aggregate row participates in dependency accounting. | Change support with the same selected implementation and output; publication records a different dependency account and downstream proof is renewed. | Replay the previous selection/proof receipt after changing formation, environment, tag, assignment or slot rows. Reject before witnessing. | CCS8414 |

Exercise each rule through both record and union transport where it applies,
including interior `Option<FnPtr>`. R4 and RC1 add positive inactive/unused
ordinary cases and negative controls that invalidate a *consumed* exclusion;
those controls use CCS8415. Absence of a valid exclusion normally retains demand
and the ordinary commitment diagnostics; it does not itself establish an error.

## Dependency account

The published account SHALL include every `CallableAggregateSlot` and
`CallableAggregateValue` row, their referred `CallableCarrier`, flow/join and
   selection rows, and all actual formation, environment, contract, lifetime,
construction, write/read-frontier and constructor/tag participants. Include the
typed relation identities, roles, ordinals and operand order/multiplicity;
hashing only selected code or a deduplicated set of node IDs is insufficient.

The producing recipe and owning fold establish and maintain this incidence.
Publication copies it, and public integrity validates that the account covers
the rows it claims. A changed, removed or duplicated row invalidates the
published selection and every dependent proof before Alex witnesses it, even
when the implementation, numeric result or observable output is unchanged.
Scope invalidation to the recorded dependents through the existing incremental
mechanism; no pipeline replay or separate premise snapshot is introduced.

## Formation and lifetime

An ordinary component retains its implementation together with the exact environment instance formed for it. Two closures with one implementation and different captured values retain distinct carrier occurrences. A native component retains its entry contract and code-lifetime premises. Environment absence is explicit for a closed component. An entry requiring an unresolved contract remains pending until its commitment boundary.

For each construction or copy, Baker must establish the destination's residence and the lifetime of every retained environment. Reuse the existing environment reservation, factory-result and residence accounts. Mutable captures retain shared-cell identity. A native entry's code lifetime remains separate from an ordinary environment's storage lifetime. Referenced declarations and obligation anchors preserve the premises. Their presence in a row establishes no discharge by itself.

A selected alternative must determine both its code and its environment. Store a finite selector with the associated environment components under one write/read relation. Baker settles selector width and storage layout. Reads reconstruct portable function values under the selected arm. Union elimination retains the constructor/tag evidence for its payload. A join preserves each formation's dependencies and conditional obligations, including alternatives with different environments. Unsupported combinations receive a located diagnostic before publication.

## Component ownership

| Owner | Proposed files and responsibility |
|---|---|
| CCS/Baker, clef | `CallableOrigins.fs`, `CallableCarriers.fs`, `CallableFlows.fs`, `ClosureEnvironmentSettlement.fs`, `Placement.fs` and `ValueRepresentations.fs`. Add one `CallableAggregateRecipes.fs` producer for slot resolution, paired transport and residence obligations. Keep declaration identity and source-to-generated correspondence through rewrites. |
| Fidelity.PSG | `Codata.fs`, `Settled.fs`, `Participant.fs`, `Empty.fs`, `Revision.fs` and `Integrity.fs`. Check references, pairing and complete participant incidence. Preserve unresolved premises as evidence requiring settlement. |
| Alex | `RecordWitness.fs`, `DUWitness.fs`, their patterns and `MutableCallablePatterns.fs`. Consume the published component rows, including paired selected environments. Read settled layouts and adaptations from rows. |
| Composer and target pathway | Discharge retained obligations before artifact acceptance. Preserve ordinary flat environments through lowering. Realize native addresses only at admitted target boundaries and retain the native ABI/code-lifetime checks of Step 1. |

Interior aggregates use Clef layouts. A C listener table requires the separately admitted native table projection and retention contract. This extension grants no raw pointer operation or implicit C layout.

## R4: demand and commitment

Phase B uses a separate proof of inactive ordinary execution. An empty parameter join requires a complete use census proving that the implementation receives no activated invocation. Baker excludes the body's scalar commitments using the same proof. Stored values retain their callable representation and environment residence. Exports and declared external activations retain their input obligations. Module-level residence alone supplies no caller. Taking a native entry still demands its ABI and code-lifetime settlement. This protocol is planned for the D6(b) schema batch, after acceptance of the representation-range regression repair.

`CallableIngress.tryClosedImplementation` currently requires its dependency family to connect to the program entry through callers. Add a separate activation reading over that complete typed census. Require a known entry and valid use and dependency participants before computing which implementations have a caller path from it. An ordinary implementation outside that set is inactive under the proof. Recursive calls inside an inactive family do not establish activation. Missing suppliers or an absent closed-ingress proof establish no such conclusion. Native entry uses, exports, opaque users and declared callback activation prevent exclusion unless their contracts establish otherwise.

Add a typed `OrdinaryInactiveBody` demand relation with the implementation, body, formals, entry, roots and complete use participants. `OrdinaryDemandRecipes` produces the row and its nanopass folds it into PSG. The reader validates exact current incidence, including multiplicity. Publish the validated body and formal exclusions in the ordinary demand projection. Retain the implementation, callable formation, environment initializers and residence obligations. A node shared with an active computation remains required.

`RangeAnalysis` uses those exclusions for `CommitmentSites` and publishes `Empty` only for the proved inactive parameter join. Calls inside inactive bodies must also leave the argument-supplier and effect census, so a dormant frontier cannot contaminate an active callback. `NumericCarrierRecipes` uses the same projection for scalar, operation and index commitments. Publication retains the demand relation as a proof dependency. Added callers, opaque uses, changed roots or altered rows invalidate the exclusion before witnessing.

Keep the proof's raw use/call census complete; filter execution suppliers and effects only after validating the inactivity reason. Inactive formal exclusions are distinct from `OrdinaryDemandProjection.Parameters`, whose existing unused-formal contract changes physical argument transport. Inactivity alone must not rewrite a retained callable signature.

The current ingress reader rejects callable transport through several aggregate paths. Most original R4 fixtures use Option, Result or sequence payloads, so their positive acceptance depends on D6(b)'s common transport protocol. Preserve those fixtures. Add a retained ordinary callable with an inactive body and verify that its environment remains represented. Negative controls add an invocation or opaque use, change the entry/export contract and withdraw or duplicate the inactivity row. Each must retract the exclusion. Native entries and declared callbacks keep their separate obligations.

## RC1: unused ordinary bindings in the same demand foundation

The normative definition rule in `clef-lang-spec/spec/expressions.md`, “Evaluating Definition Expressions,” says: “A simple named pattern does not force an ordinary initializer.” It separately requires a reached direct `eager` initializer to execute even when its bound value is unused. Preserve that distinction and the original unused-array positive test.

Add a sibling typed unused-binding reason to the shared ordinary-demand projection in this schema batch. Reuse `BindingDemandEvidence` participant accounting where applicable, but do not encode a missing first-demand frontier as a successful snapshot schedule or invent an `OrdinaryOmission` call site. The candidate must be a local immutable simple binding with an ordinary initializer, a complete use/capture census, exact initializer ownership and no independent demand. Missing or opaque uses prevent exclusion. Shared initializer descendants needed by an active computation remain required; retain graph citizens and their proof participants.

The validated reason contributes to the existing `OrdinaryDemand.DeferredOnly` projection read by memory, numeric commitment and passive emission. Formal-demand and static-string-layout consumers must also consume the appropriate shared deferral reason, rather than restricting exclusion to call-argument omissions. Added uses, captures, eager activation or changed ownership retract the exclusion. This remains a source-owned proof; no memory-only heuristic or passive-reader analysis is introduced.

This extends the already planned D6(b) demand contract once, alongside inactive callable bodies. It is a placement decision, not an implemented or accepted capability.

## Acceptance

Preserve the original Composer union-payload test and `FunctionFields`, `FunctionSnapshots` and `CapturedRecords`. Add source cases for two environments sharing one implementation, copy-and-update and alias transport. Cover mutable read snapshots followed by reassignment, nested callable payloads and a proved inactive ordinary body whose value is retained.

PSG negatives must reject crossed code/environment pairs and stale formation or tag evidence. Include missing residence premises and contract loss at a join. Alex fixtures assert the published selection and environment operands. Source-through-native cases distinguish captured values after storage and selection. Run the touched repositories' unfiltered suites and consumers under leases, preserving the no-raw-pointer and portable-dialect oracles. Compiler acceptance and the independent Bozzetto deployment track retain separate records.

## Implementation guidance from the representation repair

These are recommendations for the next tranche, not additional implemented
capabilities. Their grounding is clef `3795a17` and the
[recorded exact-head unfiltered gate](FFI_Correction_Phase0_Audit_2026-10-03.md#exact-pushed-head-unfiltered-gate-october-3-2110-edt):
2,416 executed, 2,335 passed, the same 81 failures, all eight final additions
passing and no missing cases. The zero-mismatch 16a probe establishes range
correspondence; it establishes neither aggregate lifetime safety nor native ABI
coverage.

1. **Assign maintenance of each new relation to its rewriting pass.** Before
   adding a schema row, name its producer recipe, owning fold, current-graph
   reader, dependency account and retirement path. Aggregate construction,
   projection and assignment must maintain the relations they change. Follow
   [Baker Saturation Architecture §3](../../clef/docs/fidelity/Baker_Saturation_Architecture.md#3-fan-out-and-fold-in)
   and the existing
   [representation fold](../../clef/src/Compiler/Nanopass/RepresentationRanges.fs):
   preserve or recompute disturbed support at that boundary and use
   `ObligationElaboration.retireAnchors` when retiring its anchors. Stable
   obligation identity does not authorize reuse after its premises change.
   Keep renewal reconstructible from the graph; no pipeline snapshot is needed.

2. **Preserve formation, environment instance and shared-cell identity separately.**
   The shared-cell repair needed stores from owners that never read the cell.
   For callable aggregates, test two formations of the same implementation with
   different environments, then copies that retain the same mutable capture.
   Require a complete writer/use account before claiming closed mutable storage.
   Missing writers, wrong capture sources or unaccounted writable escapes must
   withdraw that claim. Reuse the existing checked capture/borrow ingredients
   where their contracts match; scalar `SharedCell` admission does not by itself
   admit a callable or native entry.

3. **Test support changes with unchanged output.** Replace an environment,
   formation, tag or residence participant while retaining identical values and
   observable output. Assert a changed published dependency account and explicit
   rejection of the previous downstream proof. Withdraw the replacement premise
   and require a located refusal. Preserve the positive original fixtures. This
   extends the representation repair's proof-withdrawal tests to the new rows;
   a successful result alone cannot detect stale evidence.

4. **Distinguish a retained snapshot from the current contents of a field.** Read
   a callable field, reassign it, then invoke both the retained value and a new
   read. Each must select its own paired code/environment instance. Reassignment
   does not retroactively change the earlier snapshot. Separately corrupt the
   actual selector/tag correspondence while retaining cached annotations and
   require proof withdrawal. The guard repair had to distinguish operand
   observation, comparison availability and branch truth; a tag or branch label
   alone is likewise insufficient authority for an aggregate selection. R4's
   inactivity proof needs the same treatment when an invocation or opaque use is
   added to the current graph.

5. **Make graph-local indexing preserve all evidence.** The first final 16a probe
   was stopped after 231 seconds without a result; after indexing provenance and
   checked branch evidence it completed in 102 seconds. These are recorded runs,
   not a controlled benchmark. Build complete relation indexes once per immutable
   graph reading; retain duplicates so validation can reject them. A cache keyed
   only by implementation, field text or numeric result cannot justify reuse
   across revisions. Feed actual participant changes into the existing dependency
   mechanism instead of replaying the compiler pipeline.

6. **Integrate one common protocol through its consumers before widening it.**
   Start with one original failing aggregate fixture and carry its formation,
   selection, residence and proof dependencies through Baker, publication and
   passive Alex consumption. Extend that same protocol to records and union
   payloads. Keep ordinary flat closures and native entries distinct throughout;
   an unresolved native contract must stay explicit. Use focused checks while
   assembling this batch, then unfiltered touched-repository and consumer gates
   at the substantial checkpoint. Compare against the retained results; the
   remaining 81 failures have several owners and are not evidence that D6(b)
   alone will resolve them.
