# Callable aggregate follow-up: auditor handoff

October 4, 2026. This checkpoint implements R1 through R4 in the
[auditor return](FFI_Correction_Auditor_Return_2026-10-04.md).
The [evidence record](FFI_Correction_Followup_2026-10-04.md) contains the
placement decisions, spec quotations, focused failures and subsequent exits.

## Acceptance

The final build and four unfiltered suites ran from the committed heads under
Bozzetto leases covering each complete run. All leases were released.

| Suite | Passed | Failed | Added cases, all passed |
| --- | ---: | ---: | ---: |
| Clef | 2,360 | 81 | 11 |
| Fidelity.PSG | 395 | 0 | 19 |
| Alex | 305 | 0 | 18 |
| Composer | 405 | 1 | 4 |

All 52 added cases passed. Comparison with the trusted auditor TRX files found
no regressions, missing cases or resolved baseline failures. There were no
skips. The baseline was not rerun. Source and artifact hashes still matched
after the suites. These results accept the follow-up increment for review,
while the 81 Clef and one Composer baseline failures keep Phase B open.

| Repository | Pushed head on main |
| --- | --- |
| Fidelity.PSG | `bb062376fa4f13763246997feeb86ca48fe71989` |
| clef | `f23e33657fc73c4b99e29df01851bb80f6e0137d` |
| Alex | `efd1a008ce5d34a8cf4585f98faa269de112234a` |
| Composer | `0caa67d85533d69b23acf7f2b2fb2ab9fb71dd8d` |
| clef-lang-spec | `54ef3690c840327eed3fe832b6886520d4a33663` |

Direct remote reads match these heads. The code and spec checkouts are clean.
Push and remote-head receipts are retained with the evidence.

## Corrections

| Item | Change | Discriminating evidence |
| --- | --- | --- |
| R1 | Baker processes aggregate receiving dependencies through finite forward type frontiers. Each representation step renews the receiving signatures it changes before dependent slots read them. | The auditor's source program and a deeper dependency chain publish. Direct renewal controls preserve unrelated identities and unchanged signatures. A no-op fold preserves support. |
| R2 | PSG schema 19 retains symbol facts in aggregate dependency accounts. The reader checks actual operands, selected carriers, support ownership, component layout and exact closure implementation/environment pairing. | Malformed-row controls require their specific structural rule and reason after renewing the account. Unproved adapters remain refused. |
| R3 | Alex binds each aggregate row to the field, case, aggregate, operand and tag it witnesses. It checks every alternative and the final selector arm. | Removing native refusal fails four controls. Storing selector zero fails two. Reversing comparison ordinals fails four. Swapping actual arms while retaining ordinals fails four. Valid fixtures pass full public-image integrity first. |
| R4 | Scalar segmentation retains implementation-owned receiving contracts. Native entries remain in Common, outside independent scalar reuse. | A support-only change keeps the local fingerprint while current support is still required. An interface-only change alters it. Omitting receiving contracts makes the latter control fail. |

The R3 source controls retain the auditor's record and `Result` programs.
Their emitted portable output passes `mlir-opt --verify-each`. Source checking
eta-expands `first`, so the oracle checks the exact published eta formation
and its forwarding call to `StoredCallable.first`. It also checks that no
function value is stored through a data slot. The evidence record quotes the
spec clause supporting that oracle correction.

For `Result`, Baker distinguishes physical branch alternatives from logical
selected-origin observations. Both physical branch regions remain. Alex
composes their published code and associated environment together. The source
control changes the unselected body and requires stale publication to refuse.
Review the source-owned physical/logical distinction in
`CallableBranchSettlement.fs` and `CallableEmission.fs` alongside the passive
composition in Alex's callable patterns and control-flow witness.

## Evidence locations

External evidence is retained under:

```text
/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/auditor-followup-2026-10-04/
```

`final-source-manifest.json` and its checksum companion cover all 39 changed
paths, with full commit/tree identities. `final-artifacts.sha256` covers 24
built DLLs and copies. `final-artifact-binding.md` explains copy agreement.
These artifacts were built after the source commits. Exact build/test commands,
timestamps and lease acquisition/release receipts use the `final-*` prefix.
The accepted auditor TRX files remain the comparison inputs.

Mutation patches and restoration transactions are under
`r3-staged/mutations/` and `r4-proposal/`. Their leased executions and TRX
files are retained as `r3-M*`, `r4-mutation-*` and `inner-results/`.
All mutations were restored before the committed rebuild.

The four LAN/local workers were used for scoped research and retrieval.
Requests and responses are under `lan/`, with source-checked findings in
`lan/research-notes.md`. Retrieval snapshots preceded these new commits, so local
source observations are recorded separately from indexed evidence.

## Scope and next review

The **callable receiving contract** describes arguments, results and the
receiving convention. **Proof evidence** establishes the relevant obligations.
The **dependency account** retains the facts on which a published selection
depends. A **foreign resource contract** describes ownership, lifetime and
release obligations. A local code fingerprint authorizes none of those proofs
by itself.

A same-implementation conditional may remain an `Exact` carrier without a
flow-owned operand transport. Strict formation/environment checks may refuse
it. This case has no executed acceptance claim and needs a scoped control
before extending physical callable coverage.

Native aggregate reconstruction still refuses with AX4002, including when its
receiving contract and code-lifetime evidence are complete. Captured multi-arm
Alex fixtures establish consumption of public rows. They do not establish
Baker source admission for captured aggregate storage. Inherited callable
copy/update and unsupported DU initialization require typed source relations.

The remaining baseline failures keep Phase B open. The earlier auditor's
other residuals also remain visible: non-unique callable signatures, the
`FnPtr.invoke` receiving/layout disagreement, numeric claim reminting and the
Composer proof-receipt assertion blocked by its baseline failure. This batch
does not claim native ABI completion, foreign lifetime closure or regenerated
desktop bindings. The shared daemon/compiler distribution was not switched.
