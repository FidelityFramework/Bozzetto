# Callable aggregate foundation: auditor handoff and implementer return

Handoff: October 3, 2026, 23:17 EDT. The owner requested a stop for auditor
review, then a return to this implementer with the findings. No further build
or test gates are to be started for this handoff.

**Status: reviewable working-tree checkpoint, not an accepted D6(b) batch.**
PSG and Alex pass their structural/passive gates. Clef's completed integration
gate is **2,323 passed / 96 failed / 0 skipped**, including **12 regressions
in existing tests and three failing added tests** against the trusted repair
checkpoint. The current increment has **not been committed or pushed**.

This checkpoint introduces the shared callable-aggregate contract and its source
and passive-consumer integration. Phase B and D6(b) remain open. The
[evidence record](FFI_Correction_Phase0_Audit_2026-10-03.md#d6b-contract-integration-first-gate)
retains the failed intermediate runs as well as the final results.

## Review scope

Fidelity.PSG schema 17 carries receiving contracts, exact formations and
component placement. Dependency accounts include current claims and typed
declaration facts. Contract and slot identities remain distinct from executable
graph nodes. The generated fingerprint is
`E7DD6A92B7F79FF75CD39D384C16CCEB3E2B931878F94441649581DA797ADA3C`.

Baker owns the source recipes and their forward folds. Aggregate layout renewal
retains checked scalar evidence. Program storage inventory follows the component
layout. Checked aliases preserve formation identity, while distinct factory
calls preserve their actual environment instances. Source publication copies the
settled rows. Alex reconstructs portable function values from those rows and
keeps selectors and environments in data storage.

The differential compiler's reusable code fingerprints include the settled
contract interface. Full proof dependencies remain in source support and public
accounts. A reusable object does not authorize a previous revision's proof.

## Remaining work

1. The three new Clef source tests still fail before publication: the closed
   ingress census does not yet admit the aggregate-retained callable formation.
   Complete that source-owned transport obligation before claiming the record
   or Result path accepted. Preserve the new positive assertions.
2. Four older placement expectations need coordinated fixture and producer
   repair. `ClosureValueCases` expects five pointer words for a closed singleton.
   Closure Representation §2.4 states: “A slot without an environment and with
   one alternative occupies no component storage.” `BoundaryValueCases` expects
   a pointer word for an interior native-entry field. FFI §3.6.1 states:
   “A single-entry family needs no code storage.” Preserve their positive source
   programs and establish the required contracts. The old pointer representation
   must not return. These expectations remain unchanged in this checkpoint.
3. Captured aggregate environments need retained residence evidence. Multiple
   formations, nested paths and mutable snapshots need the common component
   protocol completed. Current source residuals identify those gaps.
4. R4 inactivity and unused-binding demand require their planned source proofs.
   Native ABI settlement, foreign ownership/lifetimes and C-boundary absence
   conversion remain in their scheduled tranches.
5. Composer's original Result native journey remains an acceptance obligation.
   Contract-only fixtures do not establish native execution or downstream proof
   renewal for aggregate source programs.

The [design note](FFI_Correction_Callable_Aggregates_Design_2026-10-03.md) describes
placement and I1–I6. The [rulings](FFI_Correction_Rulings_2026-10-03.md) remain the
authority for scope. The checkpoint does not switch the shared daemon.

## Validation

Evidence grades follow the [Phase 0 record](FFI_Correction_Phase0_Audit_2026-10-03.md#evidence-grades):
E = executed/retained result, R = source observation, S = normative spec,
A = agent report, I = inference. Architectural descriptions above are R;
quoted layout requirements are S. Source acceptance remains unestablished.

All filenames below are relative to the retained evidence directory:

`/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/phase-b-regression/`

| Grade | Gate | Result and limit | Evidence |
|---|---|---|---|
| E | Fidelity.PSG, unfiltered | 343/343 passed, zero skipped; includes 35 added cases. Structural integrity acceptance only. | `unfiltered-results/d6-psg-final.trx`, `d6-psg-final-suite.log` |
| E | Alex, unfiltered | 287/287 passed, zero skipped; includes nine added cases. Passive synthetic-row acceptance only. | `unfiltered-results/d6-alex-final.trx`, `d6-alex-final-suite.log` |
| E | Clef, final unfiltered integration | 2,419 executed: 2,323 passed, 96 failed, zero skipped. Failed gate. | `unfiltered-results/d6-clef-final.trx`, `d6-clef-final-suite.log` |
| E | Clef, first integrated gate | 2,419 executed: 1,760 passed, 659 failed. Retained as failed intermediate evidence; subsequent fixes do not erase it. | `unfiltered-results/d6-clef-checkpoint.trx`, `d6-clef-checkpoint-comparison.json` |
| E | Clef, intermediate focused check | 68 executed: 54 passed, 14 failed. Diagnostic check only, superseded by the final unfiltered result. | `inner-results/d6-clef-smoke.trx` |
| E/R | Compiler build | Clef and Alex production/tests built; Composer production built. Composer test build failed on eight missing `Inputs.Contracts` fields. Those eight fixture initializers were then repaired, but have **not been rebuilt or tested**. | `d6-checkpoint-compiler-build2*`; Composer's three modified test files |

The trusted preceding Clef result is 2,335 passed / 81 failed / 0 skipped,
2,416 total at `3795a179f7ac9c6eee2febc72c0344638ecc8259`. It was not rerun
for this handoff. `d6-clef-final-comparison.json` compares the retained TRX
display-name outcome multisets, preserving duplicate-name multiplicities:
zero missing occurrences, three added cases, 15 additional failures, and zero
failure-to-pass transitions. Twelve additional failures belong to existing
cases; all three added cases fail. These are observed deltas, not an accepted
reclassification of failures as expected refusals.

The 12 existing-case regressions are:

- Four layout cases: `BoundaryValueCases.Native entry records use the declared
  platform pointer width` and `ClosureValueTests.Aggregate function fields
  retain the entire settled view descriptor`, each at 32 and 64 bits.
- Two `CallableBranchPublicationCases`: withdrawn callable authority and
  whole-revision callable authority/passive publication.
- `LazyCallablePublicationCases.Generic lazy history remains source resident
  without entering the live revision`.
- Four `PublicationContractCases` structural-contract cases: `functions`,
  `lazy`, `record`, and `scalar`.
- `WitnessPreparedScopeTests.Unused open and dormant library growth do not
  enlarge published body or fact inventories`.

The added failures are the two closed-callable aggregate publication cases
(record and union) and the same-identity declaration-change invalidation case.
Their failure before publication means the intended downstream invalidation
assertions have **not** been demonstrated by these source tests. Full names and
failure text are retained in the comparison JSON and TRX.

E/A: Reading the retained failure messages groups the additional failures into
six existing cases with I5, `A participant lacks its exact published row or
source occurrence`; two existing plus three added cases with CCS8414, missing
actual carrier formation; and the four layout assertions. The I5 failures are
the four structural `PublicationContractCases`, the lazy-history case, and the
unused/dormant-library scope case. The fixture settlement-order repair did not
close published participant integrity. Repair that source/publication closure
alongside carrier admission before claiming integration acceptance. All 81
preceding failures remain.

PSG/Alex shared test lease `82e751b630b0430dabc3ab73522384a8` was released.
The final Clef test had already started before the owner's stop instruction;
its child process completed at approximately 23:13 EDT, within test lease
`38404b93fdea433e849979ed8f663e8c` (23:06:55–23:21:55 EDT). The interrupted
tool parent did not retain a shell exit code or release the lease automatically.
Completion is established by the final TRX, log summary and absence of the
owned test processes. The lease was explicitly released after completion;
`d6-clef-final-suite-release.txt` records `released`. No test was restarted.

The `d6-psg-final-source.sha256`, `d6-alex-final-source.sha256`, and
`d6-clef-final-source.sha256` manifests were checked unchanged after their
runs; corresponding `*-source-verified.txt` files retain the results.
The final Clef pin also includes HEAD, status, patch, artifact hashes, command
and start records under `d6-clef-final*`. No shared daemon switch occurred.

## Repository recovery state

All affected checkouts remain on `main`. These are the base commits, **not
commits containing this increment**. Review the working-tree diffs and untracked
source files together.

| Repository | Base HEAD | Current checkpoint state |
|---|---|---|
| Fidelity.PSG | `f153c751ff35ef0b54f6cc042fb38c5756f95740` | Uncommitted schema, generators, integrity and tests |
| clef | `73c32e3772f14720d3ea7b577b1a426054b7a4a9` | Uncommitted source recipes, folds, mirror/publication and tests |
| Alex | `4e1f859df080f487af5bac278d9ebe1e58516d2c` | Uncommitted passive aggregate patterns and tests |
| Composer | `703c5647c44e9ed66d8104eab89d24daac571b23` | Uncommitted fixture fixes in `CallableOperandTests.fs`, `LazyOperandTests.fs`, `PublicationRefusalTests.fs` under `tests/Alex.Tests/` |
| Bozzetto | `9baf9dd93b6a07d6673d7a8d539d2347b9ecac79` | Uncommitted evidence/design/rulings/requirement-map updates and this handoff |
| clef-lang-spec | `82f0077f614167e3523bc6380ee470e1180f5fb0` | Spec-first text already committed/pushed; clean at handoff |

Bozzetto also has concurrent changes to `FFI_Correction_Later_Tranches_2026-10-03.md`
and `FFI_Correction_Step0_Plan_2026-10-03.md` from another actor. They are not
this implementer's changes. Preserve them and the owner's Farscape changes;
do not sweep them into a checkpoint commit. The current increment has no remote
recovery commit yet; preceding repair/spec checkpoints remain the pushed recovery
points.

## Auditor review and return

Review the existing evidence and source; the owner explicitly requested **no
more big gates now**. Trust the retained baseline and supplied results. Do not
rerun them to establish the starting point. This is a review handoff, not a
claim that Phase B or the FFI correction is complete.

Please return findings in `docs/FFI_Correction_Auditor_Return_2026-10-03.md`
(or provide the path of your equivalent record), then hand that record back to
this implementer. For each finding give severity, evidence grade, concrete
file/symbol or test, violated spec/architecture clause, and required correction.
Separate accepted portions from blockers and distinguish executed evidence
from source reasoning. Carry forward the exact result counts above.

Review priorities:

1. Source ownership and nanopass placement: each owner maintains the facts it
   disturbs; no pipeline replay, private snapshot, or Alex repair/inference.
2. PSG I1–I6 and dependency accounts: exact declaration/contract identity,
   code/environment pairing, selector extent, stale formation/tag rejection,
   and invalidation before witnessing. Synthetic passes do not establish the
   failing source mutation test.
3. Witness segmentation: relation identities must be admitted only through
   their validated domains. Reusable code fingerprints must not authorize
   stale proofs or omit current published support.
4. The 12 existing-test regressions and three added failures, including the
   four layout cases whose old expectations conflict with the quoted spec.
   Preserve positive programs; changing an expectation requires the governing
   spec clause. Do not replace missing proof with a permissive fallback.
5. The boundary between completed structural foundation and outstanding
   source/native execution, R4, ABI and foreign-lifetime work.

On return, the implementer should read the auditor's findings and this record,
resolve the new integration regressions and source admission first, and finish
the outstanding Composer fixture build at the next appropriate validation
point. Continue on `main`, preserve concurrent edits, and use Bozzetto whole-run
leases for later gates. Do not broaden into later FFI tranches or regenerate
desktop bindings on the strength of the current failing source gate. Record
the eventual checkpoint commits and pushes explicitly; do not infer them from
this document's title.
