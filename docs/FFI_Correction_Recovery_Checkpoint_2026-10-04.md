# FFI correction — resumable recovery checkpoint, October 4

**NOT ACCEPTED. Owner requested a stop for token budget.** Phase B: B1 tuple/copy correspondence, returned-call ranges, finite captured cells, and R4 inactivity. No further full gate was run after that request. No daemon deployment or owner Farscape edits.

## Pushed main heads

| Repository | Head |
|---|---|
| clef | `7ad1daf233645b242633620b51c7901f14bbe3ed` |
| Fidelity.PSG | `d76b5a3c18fb46ba4e84cededd124d32029c5410` |
| Alex | `99409b57046b06dcbef7768503e7d2d25497e00d` |
| Composer | `0caa67d85533d69b23acf7f2b2fb2ab9fb71dd8d` |
| clef-lang-spec | `4d5c445331b34e68228a44e04a5e074430bc68fa` |

## Evidence and exact status

**E:** Last unfiltered gate was on Clef **a1e1768**, before the final repair. All eight whole-run build/test leases released; heads stayed clean and unchanged. Trusted auditor baselines were reused, not re-established.

```text
psg total=398 passed=398 failed=0 skipped=0 | vs trusted: missing=0 added=3 new-failures=0 resolved=0
alex total=305 passed=305 failed=0 skipped=0 | vs trusted: missing=0 added=0 new-failures=0 resolved=0
composer total=406 passed=405 failed=1 skipped=0 | vs trusted: missing=0 added=0 new-failures=0 resolved=0
clef total=2474 passed=2399 failed=75 skipped=0 | vs trusted: missing=0 added=33 new-failures=22 resolved=28
```

**E:** Final scoped check, after the opaque-supplier repair and four fixture-comparator corrections: **122 total,116 passed,6 failed**. All16 stale-graph regressions pass. This does **not** establish a final unfiltered failure count. The earlier gate had53 remaining trusted Clef failures plus22 regressions;28 trusted failures were resolved.

**E:** Final source bytes were built/tested before commit and match the committed tree. Build and scoped test leases released. Evidence directory **N** is `~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/notch-2026-10-04/`. Binding: `recovery-checkpoint-binding.json`, `recovery-checkpoint-manifest.sha256`; focused evidence: `checkpoint-focus-results/checkpoint-focus.trx`, `checkpoint-comparator-build-*`, `checkpoint-focus-*`. The earlier unfiltered artifact binding is separately labelled NOT ACCEPTED in `final-post-test-binding-callable-range-schema21-a1e1768.json`.

## Precise restart point

1. **Six opacity/activity fixtures remain:** UnionPayloadRange's opaque union, producer and recursive producer; RecordPayloadRange's opaque record; SequenceAccumulation's unresolved origin; LoopRange's unknown trip bound. They store/ignore helpers. Keep their unknown-origin/recurrence assertions and located CCS8011 checks. The numeric opacity defect is fixed: filtered-empty suppliers no longer erase an unknown aggregate alternative. Scalar Empty still requires the separate inactivity proof.
2. **Do not blindly switch these fixtures to library mode.** Executed `opacity-library-context-probe.log` shows all six helpers still unrooted/inactive; DeclarationRoots contains only EntryPoint even with output_kind=library. All unknown/residual properties hold. Establish the intended public/library activation authority through existing ProjectChecker/NativeService/OrdinaryDemand owners before changing those expectations. This probe is a failed control, not proof that library mode repairs the cases.
3. **Retain the completed withdrawal checks.** Four existing fixture helpers compare all non-range node fields, source incidence/roots/types/modules, absent range inventories and sourceFacts, and unchanged diagnostics. Spec error-handling.md415–421 requires withdrawal of unsupported exclusions while preserving remaining commitments. SemanticNode has reference equality: whole-record Assert.Equal was an invalid intermediate oracle, now corrected.
4. **Finish only the remaining controls, then acceptance.** The returned-call negative source probe `returned-call-vector-control.fsx` has its missing import corrected but has not executed successfully. Add the discriminating empty-supplier guard reversion; preserve its dormant opaque-range control. Use focused checks first, then an unfiltered Clef acceptance on the final clean head and affected Composer validation. Reuse unchanged PSG/Alex results; do not repeat the baseline or all completed mutations.

## Existing controls, growth and context

**E:** `mutation-ledger.json` binds14 completed semantic kills/restorations/releases; three earlier B1 reader mutants are separate. `b1-shape-matrix-4.log`:10 cases,8 categories,zero unexpected. These precede the final repair and do not certify its outstanding controls. Exact test names, reasons and owner lines: `handoff-growth-controls.md`, `lan/final-owner-locations.md`. Full progression and failures: `range-owner-notes.md`, `stale-graph-oracle-review.md`, `r4-opacity-disposition.md`. Preserve the two earlier spec-backed oracle corrections in `handoff-r4-residuals.md`.

| Repository | Base-to-head growth | New production files |
|---|---|---|
| clef | 22 files changed, 1160 insertions(+), 121 deletions(-) | none |
| Fidelity.PSG | 11 files changed, 471 insertions(+), 132 deletions(-) | none |
| Alex | 1 file changed, 5 insertions(+), 4 deletions(-) | none |
| Composer | unchanged | none |
| clef-lang-spec | 1 file changed, 11 insertions(+) | none |

Bases are pinned in the binding JSON. Existing owning passes/readers/tests only; no new semantic owner. This new document is the requested recovery evidence index. Bozzetto's additional change appends the phase evidence record.

**R:** Retrieval receipts retain fresh/latest snapshots including generation5 `2b015a2f…ca722d` and generation6 `1ad0d6b6…f36630`; subsequent site/source indexing windows were explicitly stale. Direct reads were bounded known-line confirmations, uncommitted diffs, or documented stale-index exceptions; older Bozzetto reads predated enrollment. LAN query preparation/receipts are retained. On resume, obtain fresh schema and rediscover seeds for the pushed heads, then pinned find→pgq→sources. Do not reuse an older snapshot as current.

**Residuals:** six current activity/CCS8011 cases above; trusted remaining groups in `failure-map.json` and the unfiltered comparison; Composer dead-arm projection CCS8414; unsupported aggregates CCS8411/13/14; native reconstruction AX4002. Resource obligations remain inside receiving contracts. No owner decision pending for this checkpoint. Auditor should verify binding and report findings, not rerun trusted gates.
