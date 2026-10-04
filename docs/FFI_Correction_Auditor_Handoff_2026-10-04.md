# Callable aggregate correction: auditor handoff

October 4, 2026. **The W1–W4 correction meets the auditor return's regression gates:** Clef has 2,349 passing cases and the same 81 baseline failures; Composer has 401 passing cases and its one baseline failure. PSG passes 376/376 and Alex passes 287/287. All added cases pass, with no missing occurrences or additional failures in either baseline comparison. This handoff covers the repairs requested in the [auditor return](FFI_Correction_Auditor_Return_2026-10-03.md), without closing Phase B or the full FFI program.

The governing evidence and grade definitions remain in the [Phase 0 record](FFI_Correction_Phase0_Audit_2026-10-03.md). Placement decisions are in the [callable aggregate design](FFI_Correction_Callable_Aggregates_Design_2026-10-03.md) and [compiler-owned entry design](FFI_Correction_CompilerOwned_Entry_Design_2026-10-03.md). The trusted baseline was not rerun.

`E/` below denotes `/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/phase-b-regression/`. The evidence grade **E** denotes retained execution; the directory shorthand does not assign a grade.

## What changed

**W1 — carrier census (R/E).** The probe found a capture-free eta lambda with a surviving expression marker, not a missing reference alias. The census now admits that physical lambda through its existing ingress, formal and environment checks. It still excludes materialized implementations with an environment formal. The aggregate recipe still requires the actual occurrence/formation pairing; no recipe-side carrier fallback was added. Both branch cases and all three previously added aggregate controls pass. Inspect `clef/.../SemanticGraph/CallableCarriers.fs` and `CallableEmissionCases.fs`.

**W2 — exact immutable support and owning renewal (R/E).** The scalar probe disproved the earlier dormant-contract hypothesis: the implementation, body and formals were live; missing participants were non-executable numeric source premises. Each receiving contract now retains its exact `SourcePremises`. Public presence checks recognize only that contract's source account, and published symbols can supply symbol-only implementations. Body and formal rules remain strict; dormant executable nodes are not imported to satisfy presence checks.

Callable validation reads current local support without invoking the recipe or allocating identities. Full numeric-domain freshness remains with its owner and `NumericPublication`. When aggregate placement changes occurrence representations, the owning fold sends its exact changed-site set to `CallableContractRecipes.renewForSites`; only affected receiving signatures renew. It preserves unrelated rows and identities and does not replay the pipeline. Controls demonstrate unchanged numeric results with changed published dependencies, retired earlier proof claims, and rejection of stale source/account replay. Inspect `CallableContracts.fs`, `CallableContractRecipes.fs`, `NumericRepresentationRecipes.fs`, `CallableAggregateSettlement.fs`, `CallableEmission.fs` and `CallableAggregateAccounts.fs`.

**W3 — compiler-owned portable entries (S/R/E).** The spec amendment landed first at **clef-lang-spec `fb7541d`**, FFI §3.4. A `NativeEntry` retains explicit scalar identity conversions, the selected target, environment absence and typed `ProgramImageCodeLifetime` naming its module, immutable binding and capture-free implementation. Residence is structural evidence; no fictitious SMT lifetime claim or foreign ownership default was introduced. The source owner reobserves exact declaration incidence. A closed singleton occupies zero aggregate component bytes; the standalone `PlatformWord` assertion remains.

The real source control exposed one reader error that the initial synthetic fixture missed: Startup clears executable module children but preserves lexical members. The public reader now validates the source-premise encoding's attached-children suffix and reads membership from its member prefix. Independent controls cover both Startup forms and reject an attached-only child as false membership. Residence-only declaration support remains metadata and does not demand otherwise dormant executable bodies; independent demand may authorize code occurrences. Inspect `CallableAggregateIntegrity.fs` and its independent `CallableAggregateTests.fs`. Native source controls are in `BoundaryValueCases.fs`; ordinary closure layout controls are in `ClosureValueCases.fs`.

**W4 — consumer integration (R/E).** The consumer gate exposed three further causes. First, Composer's test helper replayed numeric settlement after the complete source pipeline, retiring exact support still held by callable contracts. An executed probe distinguished the valid compiler result from the stale replayed result. The helper now publishes settled graphs without semantic repair; handwritten data and callable components explicitly invoke their initial owners. A new control proves support preservation and refusal after deliberate retirement. Existing programs and assertions remain intact.

Second, PSG's generic named-reference inventory flattened an embedded program carrier's contract identity into an executable-node requirement. The moved-factory probe confirmed that the contract existed and the embedded carrier equalled its canonical row. The reader now requires that exact canonical carrier and its own settled contract table entry. It refuses stale, missing and pending contracts; executable nodes cannot substitute for contract rows. Seven independent controls cover this distinction, without changing the schema or generated inventory.

Third, the source aggregate census treated typed hardware declaration shells as runtime aggregate values even though spatial plans consume them. The source correction distinguishes those declaration shells from runtime occurrences. Ordinary value uses still require source origins; spatial-plan validation remains strict. Both new hardware/kernel shell-versus-ordinary-use controls pass in the seven-case Clef aggregate group, and the eight original Composer hardware controls pass. The final unfiltered suites include these controls.

Two later fixture corrections preserve those checks. PSG's old residence fixture now supplies the canonical carriers and receiving contract required of real source. Composer's foreign-graph negative now uses a second independently checked revision instead of reversing the ordered finite-cell relation premises. It asserts distinct graph objects and Alex's exact context/zipper mismatch refusal, retaining every no-emission assertion. Its six-case class passed before the final unfiltered Composer run. Neither correction changes production behavior.

## Contract and limits

The coordinated public contract is **schema 18**, fingerprint `6F9021CC426417F8F26F1840BEEB0F5A3D5FAECFA51910AD7D8C513283384E78`. Generated binary/JSON readers and the Clef mapper were regenerated. The independent binary header oracle names the schema and digest explicitly.

These results establish source settlement, aggregate placement and publication integrity. They do **not** establish native execution, native address realization, foreign ABI admission or foreign lifetime closure. **Alex's native aggregate reconstruction itself remains refused**, as do its native-boundary operations. The baseline unobserved-callback CCS8011 case remains for R4. No shared daemon/compiler deployment was performed.

## Validation evidence

All artifact paths below are relative to `E/`; grades E refer to the main implementer's retained executions, trusted by this handoff writer without rerunning them.

| Status (grade E unless pending) | Scope and result | Retained record |
| --- | --- | --- |
| Focused exit established | W2 Clef 100/101 and W3 Clef 73/74; their sole failure is the trusted baseline unobserved-callback case. W3 PSG 61/61. | `inner-results/w2-renewal-focused.trx`, `inner-results/w3-residence-clef-focused.trx`, `inner-results/w3-residence-psg-focused.trx` |
| Focused correction accepted | Composer 111/111, including the real program-closure and moved-factory controls. | `inner-results/composer-owner-fixes-focused.trx` |
| Focused correction accepted | Corrected PSG positive fixture and matching embedded-carrier controls: 6/6. The pending-contract negative also passed in the earlier 168/169 group. | `inner-results/aggregate-hardware-exit-psg.trx`; earlier `inner-results/composer-owner-fixes-psg.trx` |
| Focused correction accepted | Clef aggregate 7/7, including both new hardware/kernel shell-versus-ordinary-use controls; original Composer hardware 8/8. | `inner-results/aggregate-hardware-exit-clef.trx`, `inner-results/aggregate-hardware-exit-composer.trx` |
| Final build accepted | Final dependent projects built successfully under lease `d1eb7a0d9af54443be47c7d046dda99f`. The hardware/reader focused exit group used lease `d14bc7e996814b2ca5ed5e108c6e3430`. | `aggregate-final-build*`, `aggregate-hardware-exit*` |
| Intermediate unfiltered pass against baseline | Clef 2,428 total: 2,347 passed, 81 failed, zero skipped. Comparison: zero missing, 12 added, zero extra failures, zero failed-to-passed. All added cases pass. Lease `f87c92c674d24a47ae8c2abb89ea3531`. | `unfiltered-results/aggregate-correction-clef-full.trx`, `aggregate-correction-clef-comparison.json` |
| Intermediate unfiltered passes | PSG 369/369, Alex 287/287, zero skipped. Leases `aeffc38e10724404a0e3a5bb97e4afca` and `4098d48450d54141848edc5d76aaae13`. These precede W4 corrections. | `unfiltered-results/aggregate-correction-psg-final.trx`, `unfiltered-results/aggregate-correction-alex-final.trx` |
| Intermediate failed gates, corrected in focus | First PSG 368/369 exposed the obsolete-schema fixture; first Composer 401 total, 282 passed, 119 failed exposed the W4 issues. The later PSG 168/169 group exposed its malformed positive fixture. None is an accepted gate. | Full records and causes in the [Phase 0 record](FFI_Correction_Phase0_Audit_2026-10-03.md); Composer TRX `unfiltered-results/aggregate-correction-composer-final.trx` |
| Final Clef accepted against retained baseline | 2,430 executed: 2,349 passed, 81 failed, zero skipped. Zero missing; 14 added passing; zero additional failures. Whole-run lease `22af5b0ae5774ee8be51710cc7c78a8b`, released. | `unfiltered-results/aggregate-accepted-clef.trx`, `aggregate-accepted-clef-comparison.json` |
| Final PSG and Alex accepted | PSG 376/376; Alex 287/287, zero skipped. The old residence fixture first passed its 37-case focused family. Whole-run lease `eef24beae77940d5adb07919e6d49e14`, released. | `unfiltered-results/aggregate-final-psg.trx`, `unfiltered-results/aggregate-accepted-alex.trx`, `inner-results/aggregate-residence-fixture-exit.trx` |
| Later intermediate failures, corrected as fixtures | PSG 375/376 first exposed the old pending residence fixture. Composer 402 total, 400 passed, two failed then exposed the foreign-graph fixture in addition to its baseline failure. | `unfiltered-results/aggregate-accepted-psg.trx`, `unfiltered-results/aggregate-accepted-composer.trx`, `aggregate-accepted-composer-comparison.json` |
| Final Composer accepted against retained baseline | 402 executed: 401 passed, one baseline failure, zero skipped. Zero missing; one added passing; zero additional failures. Focused graph-mismatch class 6/6 preceded the full run. Whole-run lease `0c5f622dd6f44a2e8017fa2d98905765`, released. Fixture build lease `c3ad84000c7643339512805d0cc27d2f`, released. | `unfiltered-results/aggregate-final-composer.trx`, `aggregate-final-composer-comparison.json`, `inner-results/aggregate-foreign-graph-exit.trx` |

The final increment is bound by `aggregate-accepted-{clef,alex}-source.sha256` and `aggregate-final-{psg,composer}-source.sha256`, with corresponding `-artifacts.sha256`, `.patch`, `-head.txt` and `-status.txt` records. These filenames do not themselves confer acceptance. Source manifests cover changed/untracked files; heads and complete patches identify the base. They are not whole-tree manifests. Earlier gate captures remain under `aggregate-correction-*` and the named focused prefixes. The final PSG capture additionally includes the old residence fixture's explicit canonical carriers and receiving contract; production code is unchanged from Clef acceptance. The final Composer capture includes the foreign-graph fixture correction. Comparison uses display-name/outcome multisets and records duplicate name groups; it does not claim distinct parameter identities where the runner truncates names.

Composer's remaining failure is `IncrementalBuildTests.Result branch authority rebuilds its whole scope and rejects another revisions proof receipt`: CCS8414 requires source inactivity evidence for an absent callable union payload. It is the same failure in the retained 401-case baseline. The suite exits nonzero for that failure; acceptance here means the explicitly authorized regression condition, not a completely green compiler.

The obsolete-schema fixture now presents 17 and expects `SchemaMismatch(18, 17)`, quoting the transport contract: “A schema, format or fingerprint mismatch is refused.” No production schema rule was weakened. No edits occur inside a running gate's dependency closure. Every failed build/gate, the diagnostic probes, their corrections and exact leases remain in the [Phase 0 record](FFI_Correction_Phase0_Audit_2026-10-03.md); intermediate failures are retained rather than counted as acceptance.

Other actors' `FFI_Correction_Later_Tranches_2026-10-03.md` and `FFI_Correction_Step0_Plan_2026-10-03.md` edits are excluded. The owner's Farscape changes are preserved. The main implementer owns final evidence updates and checkpoint commit/push actions.

## Checkpoint on main

The following heads are pushed and match `origin`'s `refs/heads/main`. All four code checkouts are clean. Their final source and artifact manifests still verify after committing: no tested source changed between acceptance and these commits. The spec amendment preceded implementation.

| Repository | Accepted checkpoint | Scope |
| --- | --- | --- |
| clef-lang-spec | `fb7541db51842f957fc51ceb83b89ef93285f4fb` | Compiler-owned portable entry convention, FFI §3.4 |
| Fidelity.PSG | `3d2fa758294a67ab96bcd102ff83da59dd7fb8f8` | Schema 18, exact contract support, typed program-carrier integrity and independent controls |
| clef | `46d31a905ebb216c63e6d769b27acb682fb0a7c5` | Owning carrier/contract/representation corrections and source controls |
| Alex | `9644c1c5aec2dca497a5e9d04d9d2707c1e2be8a` | Independent aggregate fixture supplies the extended contract fields |
| Composer | `beb794234fadb3f236c213ccf7eb09702d19c59a` | Explicit source-owner fixture preparation and graph-mismatch control |
| Bozzetto | The documentation commit containing this handoff | Phase 0 evidence, both design notes and this review entry point only |

The initial failing recovery checkpoints remain in history; these heads replace them as the subject of the next review. The final counts belong to the source/artifact captures listed above, not to the older recovery binaries. No daemon restart, compiler-worker replacement, desktop binding regeneration or native execution is claimed.

## Retrieval and LAN evidence

The requested services were used for the engineering questions: LAN endpoints at `gpu-one.spkez.dev:8000/v1` and `gpu-two.spkez.dev:8000/v1`, Lemonade at `127.0.0.1:13305/api/v1`, and retrieval at `https://duckdb-fidelity-pgq.spkez.dev/v1/retrieval`. Retained requests, responses, pinned snapshots, source excerpts and rejected model inferences are indexed in `/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/auditor-return/lan/README.md`; later W2/W3/reader and hardware follow-ups remain alongside it and in the Phase 0 chronology. Retrieval described pushed source, with local working changes distinguished explicitly. Advisory model output did not substitute for source or executed controls. The task-owned Lemonade model was unloaded after its bounded review when its memory use blocked a gate lease.

## Requested review priorities

The prior bounded review explicitly left these areas unreviewed; passing gates do not discharge their architectural review:

1. **I1–I4 and I6 in depth.** Check data-only component storage, exact selector coverage, formation/environment pairing, one receiving contract, and dependency-account invalidation. Inspect both ordinary and native/union cases, including unchanged numeric results with changed support. Keep body/formal presence strict.
2. **Alex aggregate patterns.** Review `CallableAggregatePatterns.fs` for row-only reconstruction and exact alternatives, selector and environment use. Check that no code value becomes ordinary stored data and that native aggregate reconstruction remains explicitly refused. Do not interpret source placement acceptance as native lowering acceptance.
3. **Dependency fingerprints and witness segmentation.** Review `CallableAggregateAccounts.fs` and `WitnessSegmentation.fs`: complete aggregate contracts, conventions and source support must affect selection/dependency identity. The private reusable scalar-code fingerprint's narrower interface must not erase support from the public aggregate account or allow an earlier proof receipt to survive changed formation, environment, tag or residence.
4. **Other Baker owners.** Review the remaining callable aggregate/branch ingredients and recipes, representation placement and affected-contract renewal. Each owner must maintain the facts it changes through its own forward fold, preserving unaffected identities. No reader may construct missing facts, allocate contract identities or replay the earlier pipeline. Check withdrawal behavior as carefully as admission.

Review W2 against its executed probe rather than the disproved dormant-contract inference in the earlier return. The source publication closure already selected live contracts; immutable proof-support facts were what the public presence domain lacked.

## Grounded guidance for the next tranche

- **Make the next native lowering claim require both source and reader controls.** The synthetic residence fixture initially passed while real source failed after Startup. Pair the next Alex native aggregate/entry case with a complete source pipeline and an independently constructed public row. W3 supplies the compiler-owned portable interior contract; the next interior work must reconstruct and use its settled rows passively. Function-to-address conversion and foreign invocation separately require ABI/loading and target realization authority. This batch's portable residence convention does not supply foreign ABI authority.
- **Keep evidence roles separate in every transported aggregate.** W2 found non-executable numeric premises; W4 found a contract identity incorrectly treated as an executable node. Extend the owning typed tables and dependency accounts when a consumer lacks a fact. Do not enlarge executable scope to satisfy a generic presence check.
- **Preserve exact support across differential changes.** Numeric replay in the consumer fixture invalidated otherwise unchanged signatures. Future representation/ownership rewrites should report their affected sites to the owning recipes, retaining unaffected identities. Keep the control where physical results stay equal but support changes; extend it when adding foreign release or loading obligations.
- **Use declaration context to determine whether a value is required.** The hardware/kernel correction excludes declaration shells only where their source role warrants it, while ordinary uses remain checked. Apply that distinction before adding broader native-table or desktop-binding cases, rather than accepting unknown origins based on type names.

## Auditor return template

- **Verdict:** accepted / accepted with bounded follow-up / changes required. State the exact reviewed repository heads and evidence pins.
- **Finding:** file and line, concrete invariant or observed behavior, evidence grade (E/R/S/A/I).
- **Placement:** governing spec clause and responsible producer/reader owner; explain any architectural mismatch.
- **Reproduction:** existing test or smallest discriminating control, expected versus observed result, retained log/TRX. Separate source inspection from executed evidence.
- **Return to implementer:** required correction and focused exit condition; distinguish this batch's defects from later-tranche work. Do not rerun the trusted baseline.
