# Callable aggregates: Phase B design

October 3, 2026. Implement [D6(b)](FFI_Correction_Rulings_2026-10-03.md#owner-decisions-on-the-step-1-plan-october-3-2026) through one aggregate protocol for `FnPtr` record fields, ordinary function fields and callable union payloads. Retain the affected original positive tests. Native ABI settlement in Step 1 will extend this foundation.

## Contract extension

Fidelity.PSG at `f153c75` declares schema 16 in [Revision.fs](../../Fidelity.PSG/src/Fidelity.PSG/Revision.fs). Extend the existing [CallableCarrier and CallableEmissionProjection](../../Fidelity.PSG/src/Fidelity.PSG/Codata.fs), with these proposed records:

| Record | Proposed extension |
|---|---|
| `CallableCarrier` | A kind distinguishing ordinary flat closures from native entries. Retain the formation occurrence and implementation. Add the actual environment-value occurrence, where present, alongside the existing environment owner/formal relation. Carry typed lifetime and contract participants. |
| `CallableAggregateSlot` | The instantiated aggregate `TypeIdentity`, its declaration node and a resolved slot path. A path identifies a record field or a union case's payload ordinal, including nested aggregates. Field text alone supplies no identity. |
| `CallableAggregateValue` | The aggregate occurrence and slot, with alternatives referring to carrier occurrences. Construction, projection and assignment retain their actual source participants. Mutable reads identify their snapshot frontier. |

Add these aggregate rows to `CallableEmissionProjection`. Reuse `CallableFlow`, `CallableJoin` and the mutable-callable construction/selection machinery. Extend `ValueRepresentation` to describe data placement and callable components separately for records and unions. A callable payload receives the same component representation as a callable field.

Bump the contract once for this foundation, to 17 if schema 16 is still current. Regenerate binary, JSON and integrity files with the clef mirrors and consumer matches in the same checkpoint. Keep subsequent Step 1 schema numbers dependent on that accepted checkpoint.

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
