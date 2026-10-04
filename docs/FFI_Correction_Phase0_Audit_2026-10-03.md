# FFI correction: Phase 0 evidence

October 3, 2026. Implementer record. The owner reviews and commits repository changes. The separate auditor reviews this record.

Owner direction after this record's initial publication: trust the supplied test counts; do not rerun the baseline. Those counts are accepted as the starting point. Subsequent execution is limited to validating authorized repairs and the discriminating regression probe. All source changes use the existing `main` checkouts.

The current [implementor guide](FFI_Correction_Implementor_Guide_2026-10-03.md) and [owner rulings](FFI_Correction_Rulings_2026-10-03.md) govern this work. Fresh captures are under `/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/implementer-phase0-20261003-BPhJf0/`. Inherited evidence is under the parent directory.

## Evidence grades

| Grade | Meaning |
|---|---|
| E | Executed by this implementer, with the command and result retained. A live observation applies at its capture time. |
| R | Read in source at the stated commit. A working-tree observation is identified separately. |
| S | Stated by the normative spec at the stated commit. |
| A | One agent's report, including inherited test summaries until checked against raw evidence. |
| I | Inferred from the cited evidence. Requires the stated check before use as an implementation premise. |

## Repository inputs

The following heads were observed with `git log -1` and `git status --short` (E). Every checkout is on `main`.

| Repository | Head | Working-tree state before this record |
|---|---|---|
| BAREWire | `571ff31da1993b0cf3bad65934c382ad81f131f8` | Clean |
| Fidelity.Data | `7cc19d6b818124b252766a2caa8ecf0c8e24ae96` | Clean |
| Fidelity.PSG | `f153c751ff35ef0b54f6cc042fb38c5756f95740` | Clean |
| clef | `a6614b539e4ef47b67b75d89f3e304b2b8cdb625` | Clean |
| Alex | `4e1f859df080f487af5bac278d9ebe1e58516d2c` | Clean |
| Composer | `fcd68d6a067f4eac9412436646b3173bdedf041f` | Clean |
| Farscape | `5ec1954f1096b0616964c00581d5e4bef8bf1255` | Owner's two documentation files and `ProtocolParser.fs` modified |
| Calque | `d0f614329aa6b54eb54a9d9b080fc8fd9f23e794` | Clean |
| Bozzetto | `1f7164714a17dd29ee6ee7fe8c16d1b5c9f78b0c` | Owner's implementor-guide update modified |
| Fidelity.FSharp.Incremental | `d476aea234b70a6e4770153c77b6ad4a7fbde568` | Clean |
| Fidelity.Platform | `d7cc3b77f20f80720cea6e79c2bed1e6ee756be1` | Clean |
| clef-lang-spec | `232482bfccaf9e7f49d8ca4e61861d5350d94d33` | Clean |
| clef-lang-site | `480e85f25cd34a0f5e64938b95cfd8bd995f1672` | Owner's untracked `hugo/content/blog/retrofitting-rust.md` |

Bozzetto's new head adds the guide and rulings. Its implementation remains at the prior checkpoint (R, `git log -3`). Owner changes remain outside this implementer's edits.

## Findings

| ID | Grade | Finding and evidence | Consequence |
|---|---|---|---|
| P0-01 | E | `sha256sum -c SHA256SUMS --quiet` returned 0 in the inherited evidence root. The manifest has 222 entries. | Integrity is established for those listed files. This check does not establish the correctness of their claims or cover unlisted later reports. |
| P0-02 | E | The clef compiler DLL SHA-256 is `27b59f138f56f30a695a9311c5f2434c21aff7bb08fabdecf5c7775e969757ba`. The test DLL is `b6a3ff56314ec271029100d192c041f0ebb37fa4b30d9ca821a1fd4c86be8942`. Both equal the inherited `t0/baseline/clef.md` values. The checkout is unchanged. | The fresh unfiltered `--no-build` run uses the pinned compiler and test bytes. |
| P0-03 | E | At 19:27 UTC, the shared daemon reported PID 548253, release `0.1.0`, healthy status, zero active leases and zero queued leases. The worker PID was 549122. | Use this daemon for coordination. Its status does not establish acceptance of a new compiler. |
| P0-04 | R, working tree | The guide changed during intake. Its Phase B section now says: "Make no repository edits for Phase B until `FFI_Correction_Baseline_2026-10-03.md` appears in this folder." It requires checking the repair review's blocker first. | Phase 0 execution and evidence recording continue. Phase B source changes await that document. |
| P0-05 | R | The rulings at Bozzetto `1f716471` record D6(b), D1(a)/(iii), legacy desktop repositories, temporary `FnPtr` equality refusal and deferred unsupported integer carriers. These match the adopted directions available in this conversation. | Preserve positive callable-aggregate obligations. Exclude the superseded refusal-baseline and protected-approval-file proposals. |
| P0-06 | R | The Farscape `ProtocolParser.fs` diff changes the comment above `requireTypeOnly`. It removes the expectation that signature loading could restore integer-address handle records. | Preserve the owner's changes. The documentation diff and its remaining claims are still being checked. |
| P0-07 | A | The inherited classifications of 55 clef and 45 Composer test defects are reported in the guide. The separate repair review disputes several classifications and the regression's proposed mechanism. | Each root-cause group requires a source/spec check. These totals do not authorize changing test expectations. |
| P0-08 | E | The fresh clef run returned 1: 2,260 passed, 94 failed, 0 skipped, 2,354 total. `CompareTrx.fsx` compares failing test-name multiplicities in both raw TRX files: 94 before, 94 after, no differences. | The inherited failing census is reproduced. This establishes neither its proposed classifications nor its proposed fixes. |
| P0-09 | R | `RangeAnalysis.runValidated` and `sourceFacts` use the same `readProgram`/`fixpoint`. The transfer at `RangeAnalysis.fs:2039–2066` already applies loop, sequence, linear, finite-cell and lazy bounds. The four postpasses add proof structure, not value ranges. | Review D-01 is confirmed. RP-FRESH's extra-saturation proposal cannot repair the regression. |
| P0-10 | R/I | `NativeService.fs:1268–1287` rewrites sequences, closure environments and lazy values before numeric settlement at `:1395`. `SequenceMachineRecipes.fs:237` copies held ranges into generated nodes; `:334` emits frame reads. RangeAnalysis has no frame-read transfer case. | Recomputing after rewriting is a concrete causal hypothesis, not an executed diagnosis. The probe must compare held/fresh facts, node kind, owner currency and rewrite provenance. Preserve all eight FiniteCellAuthority mutation/withdrawal cases. |
| P0-11 | R | Baker's required scalar set in `NumericCarrierRecipes.fs:77–78` is independent of RangeAnalysis's commitment set. Integer selection requires a nonempty bounded range at `:108`; Empty falls through to refusal at `:124`. | Review D-02 is confirmed. An Empty-range change alone cannot repair native compilation of inactive bodies. |
| P0-12 | A, checked against retained driver records | The old NativeCallbacks run lasted 13:06:21–13:20:03 UTC; its lease expired at 13:11:14. Regression lasted 13:20:03–13:43:53; its lease expired at 13:35:03. `test-all.sh` only reacquires between suites. | The guide's blanket lease-coverage statement is incorrect. Retain these outputs as historical observations, not evidence of lease-compliant acceptance. Fresh runs require full coverage. |
| P0-13 | R | The owner Farscape diff was read in full against `notes/notes-final.md`. It separates inferred module views from boundary descriptors, keeps source elaboration in CCS/Baker, and withdraws integer-address handle records. It labels native format libraries, ORC hosting and schema derivation as future work. | Preserve all three owner files. Notes are design documentation, not evidence that those capabilities execute. The report's preservation counts remain A until a line-by-line count is independently reproduced. |
| P0-14 | R | `t0/baseline/` has no Composer repository Markdown record. Its commands are retained in `test-all.sh`, with separate raw logs, failing sets and evidence JSON. | Use those actual command records; do not cite a nonexistent `Composer.md`. |

## Fresh gates

| Gate | Command record | Lease | Result |
|---|---|---|---|
| clef, unfiltered | `clef-command.txt`, `clef-test.log`, `clef-results/clef.trx` in the fresh capture directory | `1eb8b4b0fd104783b1828805d25d4f28`, released | 2,260 passed / 94 failed / 0 skipped. Same failing multiset as the pin. |
| Composer NativeCallbacks, unfiltered | `nativecallbacks-command.txt`, `nativecallbacks-test.log`, `nativecallbacks.evidence.json` | `154a00090b7b4d62862d8135bf81bfd8`, released | Completed 19:36:46–19:50:44 UTC within lease. All 29 failed at compilation; none executed. Outcomes match the supplied record. |
| Composer regression runner | Supplied command and results accepted | No new lease | Not rerun, per owner direction. |

## Triage checked against source and spec

The tables cover every root-cause group in the three inherited triage reports. Read-only implementation helpers checked pinned source and spec; the implementer retains responsibility for the resulting repair. Their checks executed no compiler or test. R/S grades below apply to the cited mechanism or requirement; projected test outcomes remain I until a repair runs. Source paths are relative to clef unless another repository is named. Spec paths are under `clef-lang-spec/spec/` at the pin above.

### CCS8011 groups

| Group | Grade | Finding and implementation consequence |
|---|---|---|
| R1 zeroed placeholder | R/S/I | `BorrowedViewCases.fs:17` returns an untabled `zeroed`; RangeAnalysis supplies no finite result. However changing it to `0` can stop demanding `work`. `expressions.md:2878` requires a separate foreign activation contract. Metadata parsing alone does not prove the replacement preserves the test. Settle scoped callback demand first. |
| R2 platform-free length | R/S | `BorrowedViews.fs:98` needs Pointer width. `width-inference.md:78` permits unresolved platform obligations before commitment. Supplying platform evidence can isolate the fixture, but partial-check rejection is not established as normative policy. |
| R3 mutable updates | R/S/I | Mutable joins and captured-cell exclusions are present (`RangeAnalysis.fs:1998`, `FiniteCellRanges.fs:273`). Several fixtures have one finite invocation or one update between yields. `width-inference.md:39` does not make those programs erroneous. Split finite activation gaps from unbounded recurrence; no blanket modulo rewrite. |
| R4 uncalled closures | R/S/I | No suppliers fall back to Unbounded (`RangeAnalysis.fs:1983`). `width-inference.md:78` distinguishes unreachable from unobserved. Do not infer unknown callers from module-level residence or change positive fixtures to evade that heuristic. Demand and Baker commitment must agree; the guide records an owner ruling pending. |
| R5 dead alternatives | R/S | Known other-tag projections become empty, then unknown seeding turns them opaque (`CallableOrigins.fs:399,473`). Preserve proved-empty versus unresolved alternatives with validated constructor/tag identity. This follows the same width-spec distinction. |
| R6 inequality | R/S | `Relation.Ne -> r` at `RangeAnalysis.fs:919` loses branch polarity required by `width-inference.md:33`. A fix may exclude an established interval endpoint or singleton; it must not infer positivity from arbitrary inequality. |
| R7 finite `twice` | R/S/I | `ClosureEnvironmentCases.fs:464` is finite composition. The context-insensitive feedback does not prove infinite execution; `ntu-types.md:79` specifies call-site joins, not this identity collapse. Preserve its positive oracle. |
| R8 Option aliases | R/S/I | Literal alias/record producers are closed, but payload/origin fallback can lose them (`OptionValueCases.fs:22`, `OptionAlternativesCases.fs:240`, `RangeAnalysis.fs:1873`). Trace the losing participant before choosing the patch. |
| R9 finite folds | R/S | `SequenceConsumerCases.fs:52` folds literal finite collections. `SequenceAccumulationRecipes.fs:412` recognizes direct addition, not folder calls. `width-inference.md:25` allows termination bounds. Preserve the finite-fold tests and fix correspondence rather than substitute modulo-only oracles. |
| R10 library inputs | R/S/I | `ProjectChecker.fs:45` lacks library context; root discovery at `RangeAnalysis.fs:1182` follows a direct lambda. `width-inference.md:95` requires declared export boundaries. Distinguish pending checking from concrete export commitment; replacing int with bool evades the issue. |
| R11 lazy sequence | R/S | `SequenceOrigins.fs:82` lacks a validated force/instance origin rule; lazy settlement retains relevant correspondence. Extend that rule with owner and proof participants, preserving deferred facts (`width-inference.md:39`). |
| R12 intentional unknowns | R/S | Five cases use never-invoked functions; three use a literal callable array. Neither demonstrates foreign opacity. Do not pin current CCS8011 output as language truth. Use genuinely opaque declarations or analysis-only graphs for negative coverage. |
| R13 Register fallback | R/S | `ClosureValueCases.fs:484` requires an Unbounded interior integer to gain 64 bits, contrary to `width-inference.md:80`. This is a confirmed test defect. Assert declared width only at the real callable boundary and retain absence of fabricated interior width. |

### CCS8403 and assertion groups

| Group | Grade | Finding and implementation consequence |
|---|---|---|
| RC1 unused binding | R/S | `MemoryValues.fs:8` lacks the needed demand exclusion. `expressions.md:2862` forbids forcing discarded ordinary bindings. Preserve the positive test. |
| RC2 activation | R/S/I | `ProgramActivation.fs:135` requires single-target calls and nonempty invocation evidence. Scoped callbacks need declared activation/demand, not only reordered startup. |
| RC3 placeholder / RC3b initializer | R/S/I | Same demand ambiguity as R1, and same unsupplied-parameter problem as R4. Changing the placeholder is not independently ready. |
| RC4 snapshots | R/S | The mutable Array.init fixture does not demand the write before observing state (`CallEffectRangeCases.fs:268`; `expressions.md:2199`). The two branch-local immutable cases are valid; `BindingDemand.fs:113` lacks their scope. Do not rewrite all three to eager. |
| RC5 mutable callable | R/S | `RangeAnalysis.fs:285` selects the initializer lambda without checking mutability, although CallableOrigins records replacements. Fix the compiler to join current callable origins (`ntu-types.md:79`). |
| RC6 inequality | R/S/I | Same no-op inequality refinement as R6; exact repaired outcomes await execution. |
| RC7 recursive accumulators | R/S/I | `LoopRangeRecipes.fs:401` covers mutable loops, not these recursive forms. Separate tail-parameter and non-tail-result recurrences. Finite termination is not evidence of an invalid fixture. |
| RC8 listener platform | R/S | `ClosureValueCases.fs:120` reads declarations before attaching Pointer dimensions, contrary to `ntu-types.md:47`. Attach platform to the valid graph and both negative graphs; otherwise their assertions are nondiscriminating. Confirmed fixture repair. |
| RC9 Option payload | R/S | `Placement.fs:267` starts the payload at Unbounded; `AggregateValues.fs:13` requires layout. Settle all producers with evidence; never add a machine-width default (`width-inference.md:80`). |
| RC10 doctored graph | R/S/I | `CallEffectRangeCases.fs:294` changes the callee while retaining old demand evidence and discarding refusal diagnostics. Reestablish freshness per `conformance.md:86`; the exact stale-row trigger remains inferred. |

### Composer groups

| Group | Grade | Finding and implementation consequence |
|---|---|---|
| Regression / RP-FRESH | R/A/I | The new refusal originates in `bba4025`; the proposed mechanism is refuted by P0-09. Historical raw diagnostics show modulus-bounded sites as well as an already-unobservable `taken` accumulator. Probe a discriminating bounded site; do not weaken currency checks. |
| X1 ordinary demand | R/S | IgnoreValues, consumeUnit, listener snapshots and other discarded bindings assume strict evaluation. `expressions.md:657–716,2861–2879` specifies deferred ordinary operands and local, shallow eager. Correct only identified strict oracles; valid deferred forms still need compiler support. |
| X2 accumulators | R/S | Blanket TEST-DEFECT is unsupported. Finite foreach and finite activation bounds are allowed by `width-inference.md:24–25,39`. A huge conservative enclosure alone does not prove the program requires that width. Preserve unresolved coverage. |
| X3 range defects | R/S | Inequality, guarded loops, zero-trip bodies and uncalled lambdas are distinct mechanisms. Baker's independent bounded-carrier requirement confirms that an Empty-only change is incomplete. |
| X4 structural gaps | R/S/A | View proof, environment schema handling, native callable ABI, callable union transport and GenericRecords publication remain real implementation obligations. D6(b) requires one aggregate protocol and passing original positive cases; deliberate refusal alone proves neither correctness nor absence of regression. |
| X5 environment | R/A | The pinned manifest uses a 30-second default, with 60-, 120- and 180-second overrides. The timed-out cases in the old run hit four 30-second limits and one 60-second limit; another census used 360 seconds. Their outcomes need a fresh run. Prior lease coverage is defective (P0-12). |
| X6 coverage | R | The three Composer entry points are not whole-repository acceptance. `Composer/docs/C_F_Completion_Ledger.md:369–381` includes editor, source-admission, platform-format and BAREWire native consumers. Inventory and rerun affected consumers in Phase B. |
| Dimension application / U6 | R/S/A | Retained `0006/intermediates/06b_obligations.smt2:532–550` compares generalized expected `'u` with occurrence units and produces constant-false equalities for `__closure_environment_impl_5681`. `units-of-measure.md:210–218` requires occurrence substitution. This identifies the defect in obligation/occurrence settlement; do not weaken the solver. |
| Legacy raw FFI | R/S | `nativeint`/`unativeint` malloc carriers and `64un` conflict with `ffi-boundary.md:14` and width §7/§10.6. Their refusal remains tested. Replacing them with generated CHandle contracts also requires acquisition/release implementation. |

## Repair-plan corrections

The review was read in full. Its central source claims D-01, D-02, D-03, D-04, D-09 and D-11 are substantiated above. Its unsupported expected-green counts remain A, not an acceptance target. File ownership and gate scheduling must be rebuilt around the corrected plan: several recognizers need `RangeAnalysis.fs`, and no source in a gate's dependency closure may change during that gate.

The following draft locations conflict with adopted D6(b) (R, compared with the rulings):

| Draft location | Superseded instruction |
|---|---|
| `REPAIR-PLAN.md:443–455` | Add a callable-union refusal, turn the positive test into P-PIN, defer its transport. |
| `:617–638` | Leave callable/closure-related positive capabilities in the pending refusal ledger. Their exact coverage must be assigned to the shared foundation and its consumers. |
| `:696–698` | Give PSG and Alex no baseline work by default. |
| `:747,774` | C8/P4 change the callable union positive test into a refusal gate. |
| `:858` | Defer the shared schema change until after T1-A. |
| `:894–908` | Accept the hybrid refusal/skip baseline in place of completing the adopted callable-aggregate work. |

The draft's proposed first-refusal pins also depend on diagnostic ordering (`NativeService.fs:1445`). Preserve root cause, code and location with explicit cascade handling; do not treat the first printed error as an invariant.

## Step 1 orientation

Read-only helpers read `t1/T1-PLAN.md`, `deployment.md`, `records.md` and `obligations.md` in full (R, retained design documents). Their valid requirements preserve complete callable contracts, Baker-owned adapters, immutable PSG transport, passive Alex witnessing and target realization checks. `FnPtr.ofExtern` demands the actual import through declaration identity. Interior records and native C tables need distinct admission and projection contracts.

The following instructions are superseded by the owner rulings: fixed schema 16→17 sequencing in T1-A; deferral of ordinary callable fields and callable union payloads; blanket refusal of interior Option<FnPtr>; protected approval-file consumption; and Bozzetto gates as prerequisites to compiler acceptance. D6(b) moves the shared callable foundation into Phase B. Native absence conversion remains a separate boundary obligation. D1(iii) uses explicit owner go-ahead tied to the reviewed distribution and rollback record. Worker wire/loaded-identity checks, cleanup fencing and rollback remain required on the independent controller track.

## Follow-through

The completed runs' failing sets have been compared without rerunning tests. The remaining supplied counts are accepted under the owner's direction. Step 1 orientation is complete; the corrected baseline document governs implementation sequencing when it arrives.

The updated guide authorizes the ordered Ready now set: Bozzetto output repair and Release validation, two Clef fixture corrections, then the temporary regression probe. Other Phase B edits await the corrected baseline document. Each result is recorded below as it completes.

## Ready now: environment repair

E: `dotnet clean -c Debug` completed. The following parallel build hit two writes to Calque's generated `obj/Debug/net10.0/FSComp.fs`. Completing the build with `-m:1` succeeded, followed by a successful Release build. This changed generated outputs only. No shared daemon or worker was restarted.

E: The authorized Release check, with `DOTNET_ROOT` and `DOTNET_HOST_PATH` pointing to the mise 10.0.401 installation and system temporary storage, returned:

```text
TRUST tier=default registered=8405 ran=8405 passed=8402 failed=0 errored=0 ignored=3 verdict=Trusted
```

Commands, logs and exit records are in `ready-now/` beneath the fresh evidence directory. Build leases: `a32b9854b2d143e385f3e92d456f5c15` (initial clean/build; manually released after driver interruption) and `38aaecf64e204fd384008c66f355628f` (serialized builds; released). Test lease: `4b4ba468840b400a83c51428aa7ddf61` (released). The run uses private data and trust-ledger paths. This is the requested environment-repair check, not a revalidation of the supplied baseline counts.

## Ready now: Clef fixture corrections

R: Changed only `ClosureValueCases.fs` on `main`. RC8 attaches `ClosureValues.context` before reading the valid callback descriptor and both invalid variants. R13 now asserts that an Unbounded interior value has no held width and names that obligation explicitly. Its source and `noErrors` guard are preserved.

E: The fixture build passed under lease `dd8f22e0c4b941199deb3151d289fdf9`. The requested unfiltered post-change gate ran under lease `adc03043ff6f4e7a9cb65918a9c418c3`, finished in 4 minutes 35 seconds, and released the lease: 2,261 passed, 93 failed, 0 skipped, 2,354 total. The listener descriptor test now passes. Comparison with the supplied TRX shows no other failure changes except the R13 test's rename. R13 still stops at the separate, already-known CCS8011 error before reaching its corrected assertion; it is not reported as passing.

Evidence: `ready-now/clef-fixtures-{build,test}*`, `clef-fixtures-results/clef-fixtures.trx`, and `clef-fixtures-comparison.json`. The owner subsequently clarified validation scope: focused checks during implementation, full suites at major integration gates. Do not repeat full runs for each small edit.

## Ready now: discriminating range probe

E: P0-10's representation-rewrite hypothesis is now demonstrated for both requested fixtures. Temporary, read-only logging captured the graph before representation preparation, after sequence/environment rewriting and demand renewal, and inside numeric settlement using its actual `freshFacts` map. `ProjectChecker.checkProject` checked the existing `16g_SequenceStartup` and `16a_SequenceOperations` projects. No native executable was launched.

| Fixture | Before rewriting: sites whose fresh facts do not fit held bounded ranges | After sequence/environment rewriting | At numeric settlement | Freshness diagnostic occurrences |
|---|---:|---:|---:|---:|
| 16g | 0 | 56 | 56 | 51 |
| 16a | 0 | 448 | 448 | 404 |

These are different measures: node comparisons and emitted diagnostic occurrences. The emitted freshness messages total 455. Demand is valid at all three capture points in both projects; an invalid-demand empty map does not explain these runs.

E: In 16g, finite-cell evidence remains current, and sequence-range evidence is current after rewriting and at numeric settlement. Thirteen generated `FrameRead` sites retain bounded annotations but recompute as `Unbounded`. For example:

```text
NodeId 6502: FrameRead (NodeId 6501, NodeId 5749)
held:  Some (Bounded (0, 99))
fresh: Some Unbounded
source: SequenceStartup.clef, line 9, column 8
ContinuationValue: source NodeId 5749 -> target NodeId 6502
ContinuationSlotAccess: sources 5738, 5773, 6482, 5749, 6515, 6513 -> target 6502
```

Before rewriting, source node 5749 is an Application whose held and fresh ranges both equal `Bounded (0, 99)`. The generated read's provenance points back to that source node, while fresh range recomputation loses the bound.

The other mismatches occur in applications, bindings, sequences and references. R: the pinned RangeAnalysis transfer has no FrameRead rule; its unmatched integer-value fallback produces Unbounded. Thus the generated read is a demonstrated loss of range correspondence, even while the captured proof-currency checks succeed. This is not missing post-fixpoint saturation or evidence that the source arithmetic itself is unbounded.

E: In 16a, 260 of the 448 mismatches are FrameRead sites. Finite-cell evidence changes from current before rewriting to non-current afterward. This fixture has an additional currency problem; repairing FrameRead handling alone is not established as sufficient. I: the baseline repair must preserve validated source-to-generated-value correspondence and renew the owning premises across representation rewrites. Disabling freshness checks or accepting held annotations as their own proof would not establish that repair.

Evidence is under `ready-now/probe/`: `instrumentation.patch`, `RunProbe.fsx`, per-fixture JSONL captures, diagnostic JSON, summary JSON and command/lease records. The JSONL uses F# diagnostic rendering for metadata and incidence; large rendered collections can be abbreviated. It is a diagnostic capture, not a complete serialized PSG or proof certificate. The specific continuation edges quoted above are complete in the capture.

Build lease: `b8dd2d1ba5c141e589e43b636a80d7ab`. Successful probe lease: `f82856f65c2e46b08d03b38f8ed71db4`. Two earlier driver attempts stopped before project checking because FSI had not loaded XParsec; their logs and released leases (`1575e3a2a3a646e591fa190f01bce932`, `be9e40080c6a4b7fa8881edec002a1c5`) are retained. All leases were released.

E: Both temporarily instrumented source files were restored byte-for-byte against their saved hashes. The normal compiler was rebuilt under released lease `12b92a848b7f4a6196ba6ae492c263fe`; its SHA-256 is again `27b59f138f56f30a695a9311c5f2434c21aff7bb08fabdecf5c7775e969757ba`, identical to the original compiler. No instrumentation remains in repository source or compiler output. Clef's retained diff consists solely of the two fixture corrections. The regression itself has not been repaired; this probe supplies its executed basis to the corrected baseline plan.

## Phase B: representation-range repair placement

R: The owner lifted the Phase B hold. This record governs the repairs; no separate baseline document or repeated baseline run is required. The current implementation batch addresses the executed P0-10 regression and the source-checked R5, R6, R11 and RC5 findings. Supplied baseline counts remain trusted.

R: The owner-forwarded placement review accepted the frame-read transfer, endpoint-only inequality refinement and immutable-only direct-lambda lookup, and required removal of the prepared `RepresentationPremises` snapshot carried between passes by `NativeService`. Adopted placement: each representation-rewriting nanopass must preserve or recompute the finite-cell support disturbed by its own rewrite. Its Baker recipe reads that pass's input graph and proposed graph change, retains an independently established unchanged recurrence, and emits only the affected relation enrichment. No handoff snapshot remains in `NativeService`, and no pipeline retry or recursive compiler invocation is introduced. This follows Baker_Saturation_Architecture §3 and §3.1: algorithms remain in ingredients/recipes; the owning nanopass folds the changed relation projection.

R: Use `ObligationElaboration.retireAnchors` and the existing enrichment fold for anchor consequences. Preserve the obligation identity when its proposition is unchanged, while changing its dependency account and invalidating downstream reuse when support changes. An explicit regression must exercise unchanged numeric results with changed support and demonstrate rejection of the earlier published account. These are implementation requirements, not yet executed acceptance claims.

R: The D6(b) design is recorded in `FFI_Correction_Callable_Aggregates_Design_2026-10-03.md` before any schema change. Its R4 choice uses a complete activation proof, shared by demand and Baker commitment, for inactive ordinary bodies. Aggregate transport and the typed inactivity relation belong to the callable-foundation schema batch; module residence supplies no unknown callers. No owner decision is pending for this choice.

### First correction checks (not batch acceptance)

E: The revised compiler and test project build succeeded (`phase-b-regression/build-7.log`, released build lease `3301236db5a540bc8dec507abb6b53f8`). Earlier builds caught source/test construction errors; their command, output and release records are retained beside it. These were implementation builds, not reruns of the supplied baseline.

E: Under whole-run lease `f81b99e0a8f4489a89511657d66e7a54`, source checks of 16g/16a and 56 focused cases finished at 21:16:42 UTC, before the 21:29:35 expiry. All work released the lease. The focused cases report 39 passed, 17 failed. `FiniteCellAuthorityCases.Identical numeric results with changed finite support require new downstream proofs` passed: it retains the numeric range and slot, changes the published finite relation account, rejects the prior passive projection and checks that old downstream numeric obligation IDs are absent after source-owned settlement. The finite mutation/withdrawal and endpoint-inequality checks also passed. This is preliminary evidence; the required unfiltered run remains outstanding.

E: The corrected 16a graph now reports current finite-cell evidence (previous probe: false); 16g remains current. Held/fresh bounded mismatches fell from 56 to 50 in 16g and from 448 to 348 in 16a. These are improvements, not closure. The samples retain the inherited unsupported legacy-type/suffix and other source diagnostics. The 17 new frame-read cases fail: positive fresh facts remain unbounded, and withdrawn correspondence can still pass numeric publication when no finite-cell certificate activated its relation census. R/I: keep the complete storage census separate from callable-value identity, and activate numeric dependency review for generated integer frame reads themselves. No freshness check will be disabled to make these tests pass.

### Owning folds and current storage evidence

R: The placement implements [Baker Saturation Architecture §3](../../clef/docs/fidelity/Baker_Saturation_Architecture.md#3-fan-out-and-fold-in): “A pass scheduled after range or placement must preserve or recompute the appropriate facts explicitly.” Section 3.1 assigns relation enrichment to a dedicated owning fold. `EnvironmentFactoryResults`, `LazyFactoryResults`, `SequenceFactoryResults` and `SequenceAggregateValues` each preserve support at their own returned rewrite. `SequenceRuntime` does so immediately after its empty-consumer rewrite and after its machine rewrite, before family/program-storage settlement. The shared ingredient compares the two graphs of that single rewrite and requires unchanged recurrence, writer identities and obligation inventory. It does not carry a preparation record across passes. `RepresentationRanges.foldIn` delegates anchor retirement to `ObligationElaboration.retireAnchors`; its recipe emits only the changed relation projection. `NativeService` has no representation-premise snapshot or renewal loop.

E: Build 8 passed (0 errors; lease `24e2ee0f8c3943e990728ee6fb4bf8ba`). Focused `inner-2` ran 57 cases under lease `d4737ae4139a4c179295f2a70c6b94e1`: 40 passed and 17 failed. The changed-support proof regression and added Option/Result absent-payload controls passed. The 17 representation cases now fail during initial source publication, before their individual assertions, because activating dependency review exposed other generated integer facts. This is not acceptance.

E: External `DebugFrame.fsx` checked the same source without the test helper's early diagnostic assertion, under lease `debb276d79724ea4b3e58a791496de93`; no repository instrumentation or test expectation changed. Every generated source-value frame read in this fixture now has validated storage inputs and matching fresh bounds. Eleven held/fresh mismatches remain: ten sequence-current applications and propagated bindings/references, plus the generated state read. R: the element reader still requires retired source `Yield` payloads to be executable; the state read has no ordinary source-value correspondence. The owning machine rewrite must publish usable current correspondence for these cases. Records are `phase-b-regression/{build-8,inner-2,debug-frame}*`; all three leases were released.

R: `SequenceContinuationEvidence` now records each scalar source cut's actual generated current writer. `ContinuationCurrentInputs` reads the current cut/resume/initial-state protocol. `CallableIngress` supplies its shared complete storage census to state/current slots and ordinary frame reads. The numeric solver joins current writer operands, with initial zero admitted only by the validated initial-state contract. Empty sequences have no fabricated current slot. Typed dependency snapshots include the cut/resume labels, writer correspondence, successful-current admission and state-proof support. These receipts are internal source records, so this batch changes no Fidelity.PSG wire schema. No held range supplies its own proof.

E: Build 13 succeeded. Focused `inner-4` ran 94 cases, all passing, from 21:52:06 to 21:52:38 UTC under released lease `517c57d340624256bf95fe6424a67045`. This includes all 25 representation-range cases, the changed-support/new-downstream-proof case, finite-support withdrawal controls, existing continuation evidence cases, and the callable/lazy/inequality cases selected in its command record. Earlier integration builds and checks remain recorded, including the reader correction for the different enumeration orders of dispatch `Children` and typed structural edges. The reader compares the complete operand multiset while the typed edges retain operand roles. The unfiltered acceptance run follows this check, without source changes during the gate.

### First unfiltered correction gate

E: The unfiltered clef suite and 16g/16a source checks ran under lease `6010b61cca8d4d2fa0692f5b1188de14`, from 21:53:26 to 22:02:13 UTC, before its 22:08:10 expiry. The lease was released. The suite reports **2,290 passed, 109 failed, 0 skipped, 2,399 total**. XML comparison against the trusted fixture pin identifies 12 resolved failures, 81 continuing failures and 28 previously passing cases now failing. All **45 added cases pass**, including `FiniteCellAuthorityCases.Identical numeric results with changed finite support require new downstream proofs` and all eight state/current support mutations. The batch is not accepted while those 28 regressions remain.

E: The 28 regressions report CCS8403 in source publication: CallableInstanceContinuation (10), MappedSequenceComposition (1), PublicationContract (6), SequenceAccumulation (3), and SequenceNumericPublication (8). The inventory retains each exact name, outcome and diagnostic. R/I: their source fixtures expose remaining correspondence or accumulator-support losses when fresh numeric review becomes active for frame reads. Preserve their positive expectations and repair the owning support.

E: In 16g, all 13 frame reads now have checked storage input and no frame-read held/fresh mismatch remains. Total mismatches fell from the original 56 to 16; the remaining mismatches include the summation cell and propagated results. In 16a, finite-cell evidence is current and total mismatches fell from 448 to 243, of which 132 are frame reads. Fourteen of its 388 frame reads lack checked storage access. Both samples retain source diagnostics, so neither is accepted or reported as executable.

Evidence: `phase-b-regression/clef-acceptance-1-*`, `accept-clef.sh`, `clef-batch-1.log`, `unfiltered-results/clef-batch-1.trx`, `clef-batch-1-comparison.json`, `clef-batch-1-inventory.json` and `check-3.log`. `accepted-input-{head,status}.txt`, `accepted-input.patch` and `accepted-input.sha256` pin the source state and compiler/test bytes. No baseline was rerun and no test expectation changed in this batch.

### Discriminating checks of the new publication refusals

E: External `phase-b-regression/DebugCases.fsx` checked the measured mapper with the original `LazyResidenceFixture` authority and source, and the module sequence summation with `SequenceProgramFixture` authority. It ran under released lease `70ad3ad4fefa4085941f1ea3f80792cd`; `debug-cases-2.log` records the result. The first script invocation failed FSI type inference before checking source (`debug-cases.log`, released lease `e1d0591022b44179bf9c0b68950efe0a`); the corrected invocation completed. No repository instrumentation or existing assertion changed.

E/R: Both mapper continuation protocols validate, and its frame reads have current storage inputs. The first missing element transfer is generated `Seq.current` application 424: its source annotation is `Bounded (3, 4)` and its fresh fact is `Unbounded`. That propagates through the mapper argument, formal and result. `SequenceRuntime` carries the source successful-current certificate to the generated call, but `RangeAnalysis` reads only the original three-source admission and original element rows. The summation's protocol also validates; its accumulator 109 retains `Bounded (0, 40)` but derives `Above 0`. Fresh accumulation recognition rejects the retired source evaluation body after the generator acquires its machine body.

R: The repair stays with the owning readers and recipes. Guarded-current transfer must validate retained source admission and its actual machine correspondence. Accumulation recognition must derive its finite model from typed source evaluation/control rows and current machine cuts. The existing in-graph sequence handoff receipt may establish correspondence currency; its cached scalar annotations cannot supply the result. Added numeric dependency observations cover typed evaluation and sequence-capture formation/initializer rows consumed by these readers. These source edits await the next correction gate.

### Guarded current and recipe ownership corrections

E: Build 18 passed. The next focused integration check ran 158 cases under released lease `1361ca486607438a93d13b76c56b2279`: **140 passed, 18 failed**. All ten `CallableInstanceContinuationCases` and all seven new `MappedRepresentationRangeCases` pass, including the case which swaps actual guard branches while keeping cached scalar ranges and the cut/storage protocol unchanged. `debug-cases-3.log` reports no mapper diagnostics or held/fresh mismatches. All 18 remaining failures are accumulation/publication fixture setup refusals. This is focused evidence, not unfiltered batch acceptance.

R: `Baker/Ingredients/SequenceCurrentCorrespondence.fs` validates the retained source prefix with `SequenceCurrentRecipes`, exact generated operands with `CallableIngress`, and the actual machine's successful-pull ordering. Its finite action worklist starts each resume without pull authority, grants current access only after the checked fresh pull's true branch, and consumes that authority at current. Per-action produced-value reuse respects the generated DAG; it does not carry availability across another dispatch visit. The range solver consumes the checked correspondence and actual current writer operands. No compiler pass or saturation pipeline is replayed.

R: The remaining summation loss has a separate ownership cause: `LoopRanges.isOurs` previously matched every `AdditiveLoopInvariant` body, including the sequence recipe's `sequence-additive-invariant`. It deleted that sequence-owned claim before fresh sequence recognition, invalidating the protected handoff. Retirement now identifies the loop recipe's exact claim kinds. The new ownership regression requires the sequence claim's node identity, dependency-account currency and passive publication to survive loop recognition.

R: The two touched early folds, `LoopRanges` and `SequenceAccumulations`, now call `ObligationRecipes.retireAnchors`, the single implementation exported by the later `ObligationElaboration.retireAnchors` alias. Their compile position precedes the elaboration orchestrator. Neither duplicates anchor retirement. The representation-rewrite folds continue to call `ObligationElaboration.retireAnchors` directly. This follows the same Baker §3 ownership rule: a pass preserves other owners' facts and retires only its own consequences.

E: Build 19 passed after these ownership changes. Build 14–17 failures were integration construction errors (F# indentation, an updated recipe call signature and an xUnit overload); their exact command, output and release records remain under `phase-b-regression/`. Build 19's released lease is `d056ade9af514f51b543b404ea716e43`. The subsequent focused ownership gate is running; no acceptance is claimed yet.

### Machine effects and iterator storage correspondence

E: The ownership-focused check completed under released lease `0fcf3c37f3694b379664bcfc0acf57c6`: **56 passed, 19 failed, 75 total** (`inner-6` artifacts). The 18 accumulation/publication setup refusals persisted, and the new ownership case encountered the same setup refusal before its assertions. The mapper remained clean in `debug-cases-4.log`.

E: External `DebugAccumulation.fsx` completed under released lease `87491e0ca2cf481ca8fcb2aafb81db5b` (`debug-accumulation-3.log`). The retained source evaluation has exactly 93 expected and actual rows; the independently validated machine cuts agree, and fresh source control derives two pulls. Sequence account currency now survives loop recognition. The remaining rejection is the actual pull guard's unknown effect. Its stored pull-effect row names the retired source body, whereas its generator now names the realized machine body. Two earlier probe invocations stopped at reflection errors; their records remain. No production instrumentation or existing expectation changed.

R: The owning `SequenceRuntime` fold now refreshes `SequenceEffects` after installing machine correspondence and reachability, before renewing finite support and recording the sequence account. The existing origin ingredient follows iterator `FrameRead` values only through `CallableIngress`'s complete actual storage census. RangeAnalysis uses that same census for actual `FrameWrite` effects: mutable source slots retain their logical cell writes; admitted private slots do not fabricate source-cell effects; missing or contradictory evidence stays unknown. The successful-current ingredient supplies both numeric and effect readers, avoiding a second guard algorithm. Numeric dependency accounts include the effect rows consumed here. This follows Baker §3's requirement that each rewrite maintain the facts it disturbs.

E: Build 23 passed under released lease `d91e1711153d4f598a730b4543c753da`. Builds 20–22 caught construction errors (indentation and set type inference); all command/output/release records are retained. The next focused integration run is under lease `965a1fe916194503bbde3445745b72f4`; its outcome is not yet claimed.

R/E: Bozzetto coordinates these host-side CCS builds and tests through its work leases. A read-only check of the live service found no host-test log/TRX ingestion tool; `/dashboard` presents the leases and the existing installed-compiler NativeCallbacks session. That session has no accepted artifact. Native acceptance will use the shared Composer session after the separately authorized worker switch. Neither the daemon nor its installed worker was replaced during these checks.

E: That focused run completed and released its lease: **145 passed, 15 failed, 160 total** (`inner-7.trx`, `inner-current-effects-*`). Both external mapper and standalone summation checks now have no diagnostics or held/fresh mismatch (`debug-cases-5.log`). The sequence-proof ownership test passes. Remaining failures are the mapped-sequence composition case and mapped-accumulation publication fixture setup, not the standalone accumulation cases.

E: `DebugMappedAccumulation.fsx` completed under released lease `1ab0ae6df5e148b68fd3b5ec1e1d0028`. The mapped fixture's effect map has no unknown entries. Both inner and outer machine correspondences validate; retained evaluation accounts match exactly. Fresh inner control derives one pull, but the outer mapped finite model remains absent. This isolates the remaining refusal in finite product recognition, rather than the repaired storage/effect readers. This diagnostic script remains outside repository source.

E/R: A second external observation under released lease `b710ef6515db4f018ad3bb8fb80bbe39` identifies the exact product rejection (`debug-mapped-accumulation-2.log`). The immutable input path is already valid (149→147→90); no expanded retired-node origin census is needed. The source use check rejects canonical `FrameSlot` incidence at generated writer/read sites 329, 382 and 413. The repair shares the existing complete storage census between capture checks and this use check. It admits only a checked actual `FrameRead` or an exact validated writer/value pair for the enumerator. Scope membership alone does not admit an access; borrow, environment and unknown accesses retain their refusal.

E: Build 24 passed under released lease `50ac36ca6ed4449ea1bfeb1a696159d4`. The integrated focused run then passed **all 160 cases**, with no skips, under released lease `ad819d47a2314b4d8ccfe375e28350af` (`inner-product-*`, `inner-8.trx`). Both external mapper and summation checks are clear (`debug-cases-6.log`). The strengthened origin/effect withdrawal case first asserts a known origin and a closed pull-body account, then checks that removing storage evidence withdraws that account and emits unknown effect evidence. Its final publication refusal is additional coverage, not the sole evidence of effect retraction.

E: `representation-batch-2-input-{head,status}.txt`, `.patch`, `-source.sha256`, `-artifacts.sha256`, and the dependency head/artifact records pin this implementation, including untracked new files. The second unfiltered correction gate started under lease `d5ad8bc604514f33bf7d9876a4c72667`, expiring at 23:26:06 UTC; the driver bounds all work to 840 seconds. Source remains frozen for the gate. This is acceptance testing of changed code, not a rerun of the trusted baseline.

### Representation correction: review checkpoint

E: The second unfiltered gate completed from **23:11:17 to 23:17:58 UTC**, within that lease, and released it. The suite reports **2,327 passed, 81 failed, 0 skipped, 2,408 total**. The retained XML comparison against the trusted fixture pin shows **54 new cases, all passing; 12 prior failures resolved; no newly failing cases**. All 28 regressions exposed by the first correction gate are gone. Source and compiler/test artifact hashes still match the pin after the gate. Evidence: `clef-acceptance-2-*`, `unfiltered-results/clef-batch-2.trx`, `clef-batch-2-inventory.json`, `check-4.log` and `representation-batch-2-*` under `phase-b-regression/`.

E: The original 16g source probe now has **zero** held/fresh bounded mismatches (originally 56), all 13 frame reads admitted, and current finite-cell evidence. The original 16a has **137** mismatches (originally 448): 93 frame reads, 40 applications, two references, one binding and one pattern binding. Fourteen of 388 frame reads still lack storage evidence; finite-cell evidence is current. Both samples still have legacy surface and numeric diagnostics, so neither source nor native acceptance is claimed for them.

R: This is a reviewable representation-support increment, not complete Phase B or FFI acceptance. The owning rewrite folds, exact stable-obligation renewal, current storage and guarded-current readers, finite product repair and changed-support/no-proof-reuse regression can now be reviewed against executed evidence. `NativeService` carries no snapshot or pipeline replay. Composer consumer rebuild/validation and the remaining 16a/Phase B causes continue without a checkpoint pause. The native ABI and shared-worker deployment steps remain separate.

### Consumer validation and remaining 16a discrimination

E: Composer and its unfiltered Alex consumer tests rebuilt with project references enabled. The complete editor test project exposed two existing F# list-separator errors in JSON evidence/request construction (`ProgramLifetimeProjection.fs:123`, `LiveCaptureChecks.fs:109`). Added only the missing separators; no assertion or expected value changed. The second consumer build passed for Composer/Alex.Tests, CCS.Editor.Tests, NativeSequences and Farscape BoundaryConformance. Both build leases (`53840487a7f64f9fa2cf7bf2764c97dd`, `2752a3126ef94a19b56bae70abefc473`) were released. `build-consumers*` and `consumer-{composer-head,composer.patch,artifacts.sha256}` retain the commands, changes and binaries. Composer's unfiltered Alex consumer gate started under lease `bfc19ea8c2404932a275f6bdf2f003b5`.

R: The remaining 16a log contains a counted-range current losing its upper bound and a trace arithmetic chain losing its finite/positive inputs. Its per-mismatch `access` field reports callable-identity access, not storage access. Do not equate its `None` entries with refused storage. Only the separate complete `FRAME-STORAGE` census establishes fourteen refusals. The next discriminating probe must print exact storage accesses and their writer operands, distinguishing those refusals from admitted storage whose fresh inputs already lack a range.

E: Composer's unfiltered Alex consumer suite completed **400 passed, one failed, zero skipped, 401 total**, matching the trusted failing test name exactly: `IncrementalBuildTests.Result branch authority rebuilds its whole scope and rejects another revisions proof receipt`. Its refusal remains the callable component representation gap, owned by the planned D6(b) foundation. The lease was released; evidence is `consumer-composer-alex-*` and `consumer-results/composer-alex-representation.trx`. No baseline was rerun to establish that comparison.

E: The full default editor command returned 1 after eleven seconds under released lease `5b596844b4794960acce54c93a501c99`. `JsonRpcChecks.run` timed out at its request-entry wait (`JsonRpcChecks.fs:87`) before the compiler check groups ran. The main handler caught and printed `TaskCanceledException`; this was a failed gate, not an unhandled process crash. Evidence is `consumer-editor-*`. No editor acceptance or compiler-case count is claimed. Investigate the actual request fault/dispatch before changing any timeout or cancellation expectation.

E: Farscape's rebuilt `BoundaryConformance` consumer passed **17/17** gates under released lease `14ff51b279fb418aadb0ee31844aed73`. This includes actual header generation plus current-CCS source/publication checks and explicit rejection of raw-address formation and fabricated handles/native entries. It does not establish native execution or foreign lifetime safety. Evidence is `consumer-boundary-*` and the retained `boundary-conformance-artifacts/` copy.

E: The original 16g NativeSequences gate returned 1 under released lease `a4faf0852b53496b9550294de1dfb4f3`, before MLIR verification or native execution. Its compiler emitted seventeen effective errors: unbounded `firstSum`/`secondSum` and dependent arithmetic carriers, followed by absent memory/spatial settlement. The legacy platform type/suffix notices are unreachable informational diagnostics in this native run, not its effective failure. Evidence is `consumer-native-16g-*` and `native-16g-artifacts/`, including the harness's compiler/source hashes and original output oracle.

E: The original 16a NativeSequences gate also stopped at compilation under released lease `dbcfe2d049134be68beb3883b8a1b184`: 235 effective errors and 87 informational notices, before MLIR verification or execution. The source/native limits agree with the pending numeric settlement work. Evidence is `consumer-native-16a-*` and `native-16a-artifacts/`.

R/E: The editor transport check now races remote progress against request and connection completion, preserving the original ten-second bound and requiring the exact remote payload plus carried cancellation. It cannot satisfy the remote-cancellation oracle with local wait cancellation. After a passing build under released lease `34a1b2479267497ba842a3e68d2a76d8`, the full editor command immediately exposed `RemoteMethodNotFoundException: No method by the name 'hold' is found` under released lease `2806edf4eb3047438f23d84a9b7e5023`. That localizes the earlier timeout without weakening its oracle. The formatter has not been loosened. `build-editor-fault-*` and `consumer-editor-fault-*` retain the evidence; registration/dispatch repair continues.

E: Making the test RPC target publicly discoverable and asserting its actual method registration resolves that transport failure. Build lease `71dee5b94ca64568a5db0326b618cb6f` and editor run lease `6a47fde710f947b5871040a1fa112255` were released. The actual remote cancellation check and 31 compiler check groups pass; the command then stops in `StringEncodingChecks` on source-only CCS8011 (`length` and the entry result lack an observable range without a target). No positive expectation or timeout was changed, and the formatter remains unchanged. Evidence: `build-editor-registration-*`, `consumer-editor-registration-*`. This is a harness repair with a remaining compiler refusal, not complete editor acceptance.

E: `ProbeRemaining16a.fsx` (SHA-256 `1ed0e1bdc4e0cea3f8392dd8873f4a0924fcf928618a2eb7c8cecbc8a86fc0e4`) completed under released lease `568036514cb64e1289ec494952000497`. Its exact commands, loaded assembly identities and output are retained as `remaining-16a-probe-*`. All fourteen refused storage reads are mutable captured integer cells: trace, empty-effects, predicate-pulls and demand counters. A second sequence owner writes the shared trace cell. Other admitted reads lose guard-derived bounds after continuation realization. The probe remains outside repository source.

R: The next repair preserves shared-cell identity through the existing storage reader, including write-only owners, and requires a complete current store census across owners before transferring a shared-cell range. Guard restoration belongs in a Baker recipe over retained typed control and checked actual continuation transitions; RangeAnalysis consumes its exact read/guard correspondence through the existing arithmetic refinement rules. No held numeric annotation establishes its own proof. This continues Baker Saturation Architecture §3's ownership requirement and introduces no pipeline replay or NativeService snapshot.

E/R: The owner's retrieval instruction is now applied to code discovery. The authenticated natural-language request uses `action: ask` with the required `text` field. `retrieval-demand-discovery.{request,response,schema}.json` records HTTP 200, fresh generation 29 and clef revision `98bf2efb95e87420e2ca01ccc911f156eed8393b`, matching local HEAD. The response provides revision-pinned source locations; its four hydrated excerpts are incomplete and serve as navigation, not proof of uncommitted implementation. Working-file reads establish those changes. Earlier malformed requests returned HTTP 400 and establish nothing about source behavior.

R: At the owner's direction, validation expansion stops here. The trusted baseline remains accepted. The shared-cell and guard repair is one implementation batch, with focused checks while integrating and an unfiltered acceptance run only at its substantial checkpoint. No new full regression cycle is currently running.

### Shared-cell and generated-guard implementation

R: `CallableIngress.StorageAccess.SharedCell` distinguishes a checked mutable cell from immutable formation values. RangeAnalysis reads its fresh logical-cell enclosure only after accounting for all live frame stores, including write-only sequence owners, and for closure/lazy stores through their existing checked readers. Unsupported writable borrows withdraw admission. The address-exposure predicate is shared with the existing finite-cell reader through `Baker/Ingredients/SourceStorageBorrows.fs`; no second address rule or carrier default is introduced. Callable identity explicitly refuses the shared mutable path.

R: `Baker/Recipes/ContinuationGuardRecipes.fs` establishes exact read/guard correspondence from typed source control and the actual dispatch, PC stores, resume actions and storage census. It tracks operand observation, comparison availability and branch truth separately, so a branch cannot revive a predicate invalidated by a write. Unknown effects and suspension preserve facts only for checked private storage with a closed use account. RangeAnalysis supplies current action effects, treats missing effects as unknown, and applies its existing arithmetic refinement rules at the exact admitted read. The current numeric dependency account already observes these source/control/storage relations. No cached numeric annotation, pipeline replay, NativeService snapshot or public schema change is involved.

E: The compiler and test project build successfully (`build-shared-guard-6`, released lease `c7d1a06fe3ba4b3ea6beed0d784d636a`). Earlier `build-shared-guard-1` through `-5` retain construction-error logs and released leases; indentation, imports, a numeric annotation and test module-abbreviation qualification were corrected without changing test expectations.

E: Focused run `shared-guard-1`, released lease `a29bbe4e5fdc4a349550fa6df54b5fa8`, passed all three generated-guard cases: counted and take bounds ignore poisoned cached ranges, and swapping actual counted branches retracts authority with held ranges unchanged. Its five shared-cell cases stopped in fixture setup because module bindings produced no captures. Moving that new fixture's declarations inside the entry function creates the intended two capturing sequence owners; its expected outcomes are unchanged. This corrects new test construction, not an existing baseline expectation.

E: The corrected fixture rebuilt successfully (`build-shared-guard-7`, released lease `0cce68fe90ea433482f2389130c77985`). Run `shared-cell-2`, released lease `a35bffd62a35455e80c022b269a27e4c`, passed all five shared-cell cases: complete cross-owner stores, two missing/corrupt writer-identity controls, writable address exposure and an unproved lazy store. The already passing guard cases were not rerun for this test-only edit. `focused-results/{shared-guard-1,shared-cell-2}.trx` and `shared-guard-input-{1,2}-*` preserve outcomes and exact source/binary inputs, including untracked files. These are focused integration results; no unfiltered batch acceptance is claimed yet.

E/R: The first original-16a probe of this implementation was stopped with TERM after 231 seconds (20:19:25–20:23:16 EDT), before producing a semantic result. Its previous execution took 76 seconds. `remaining-16a-shared-guard-*` records exit 143 and release of lease `b2e8b901d29943f696e60718dfec5433`; it establishes no new mismatch count. Source inspection identified repeated complete provenance scans inside guard clone discovery and repeated branch-evidence construction. The reader now indexes complete provenance row lists once per graph and caches validated clones and branch evidence within the current immutable reading. Duplicate rejection and every type/operand check remain intact; no cached result crosses graph revisions. Build `build-shared-guard-8` passed under released lease `ff17d03136224c1dba0c2e0ce6bb1d19`. The affected three guard cases and the original probe follow under one whole-run lease, with the probe bounded to 300 seconds.

E: `guard-index-1` completed under released whole-run lease `5e703c8964824b32a118d5592bd6f5d1`. The three affected guard cases pass again. The original 16a probe then completed in **102 seconds**, reporting **zero held/fresh mismatches, 388 frame reads, zero refused storage reads, and current finite-cell evidence**. This resolves the 137 remaining mismatches at the preceding checkpoint (448 at the original probe). `remaining-16a-indexed.{log}`, its start/end/exit files, `focused-results/guard-index-1.trx`, `check-guard-index.sh`, the command/release records and `shared-guard-input-3-*` retain the evidence. Source hashes still match the pin after execution; `git diff --check` passes.

E/R: Separate 16a diagnostics remain: 83 CCS8403 numeric settlement diagnostics, 11 CCS8011 unobservable-range diagnostics, and the same 87 legacy source-surface notices (57 CCS8706, 22 CCS8018, 8 CCS8009). The probe is source-only and establishes neither native execution nor unfiltered Phase B acceptance. Its zero mismatch result establishes repaired representation correspondence and current finite-cell currency for this fixture; the remaining range/demand and callable-foundation causes stay in Phase B. No supplied baseline or broad regression suite was rerun in this increment.

### Owner-authorized checkpoint publication

E: The owner explicitly directed committing and pushing this checkpoint. Source
hashes matched `shared-guard-input-3-source.sha256` before staging; the additional
clef checkpoint document summarizes the same recorded evidence. These commits
were pushed to the existing `main` branches without a version change:

- clef [`3795a179f7ac9c6eee2febc72c0344638ecc8259`](https://forge.spkez.dev/FidelityFramework/clef/commit/3795a179f7ac9c6eee2febc72c0344638ecc8259):
  representation support, current storage/guard correspondence, proof withdrawal
  regressions and the repository-local checkpoint note.
- Composer [`703c5647c44e9ed66d8104eab89d24daac571b23`](https://forge.spkez.dev/FidelityFramework/Composer/commit/703c5647c44e9ed66d8104eab89d24daac571b23):
  editor RPC registration/cancellation checks and the two JSON-list repairs.

R: This is a remote recovery and review checkpoint. Phase B acceptance, the
callable-aggregate schema work and native FFI closure remain open under the
recorded conditions. The latest increment's validation remains the focused
checks and original-fixture probe above; publication did not rerun the trusted
baseline. The Bozzetto documentation commit includes the owner's hold-lift
instructions, this evidence record and the proposed callable-aggregate design.
The existing separate Farscape edits remain with their owner. No compiler worker
or shared daemon was replaced.

### Exact pushed-head unfiltered gate, October 3, 21:10 EDT

E: At the owner's direction, the next action after the recovery checkpoint was
the unfiltered clef suite on exact pushed head
`3795a179f7ac9c6eee2febc72c0344638ecc8259`, before further implementation.
The working tree was clean throughout. Existing source and compiler/test DLL
hashes matched `shared-guard-input-3-*` before execution; no rebuild was needed.
The complete command was:

```sh
dotnet test tests/Clef.Compiler.Service.Tests/Clef.Compiler.Service.Tests.fsproj \
  --no-build --no-restore --disable-build-servers -m:1 \
  --logger 'trx;LogFileName=checkpoint-3795a17.trx' \
  --results-directory /home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/phase-b-regression/unfiltered-results
```

E: Bozzetto granted test-suite lease `757e2fc31efa42359ef791064c66f698`,
expiring at 21:18:38 EDT. The gate ran **21:03:42–21:10:18 EDT**, bounded to
840 seconds, and the driver released the lease on completion. It exited 1 with
**2,416 executed, 2,335 passed, 81 failed, zero skipped**, with no aborted or
timed-out cases. All eight newly added shared-cell/guard cases passed in this
unfiltered run. Relative to the trusted preceding increment, there are **zero
new failures, zero resolved failures and zero missing cases**. The same 81
failures remain. This closes the final increment's missing unfiltered regression
evidence; it does not close Phase B.

E: `BatchTestInventory.fsx` compares the preceding retained TRX with this run;
`CompareTrxOccurrences.fsx` additionally compares display-name outcome multisets
and checks result counts against TRX totals. The four pre-existing duplicated
display-name groups retain their full passing multiplicities. No previous suite
or trusted baseline was rerun. The eight passing additions, under
`Clef.Compiler.Service.Tests`, are:

| Class | Test name and parameters |
| --- | --- |
| `SharedMutableRepresentationRangeCases` | `Shared cell reads include stores from a second sequence owner that never reads the cell` |
| `SharedMutableRepresentationRangeCases` | `Shared cell range authority withdraws when a write only owner loses its checked storage identity(defect: "missing-capture")` |
| `SharedMutableRepresentationRangeCases` | `Shared cell range authority withdraws when a write only owner loses its checked storage identity(defect: "wrong-capture-source")` |
| `SharedMutableRepresentationRangeCases` | `Unsupported writable cell exposures withdraw shared read authority without changing the known stores(defect: "cell-address")` |
| `SharedMutableRepresentationRangeCases` | `Unsupported writable cell exposures withdraw shared read authority without changing the known stores(defect: "unknown-lazy-store")` |
| `ContinuationGuardRangeCases` | `Generated guarded reads derive finite counted and take bounds without cached ranges(prefix: "__for_counter", upper: 10)` |
| `ContinuationGuardRangeCases` | `Generated guarded reads derive finite counted and take bounds without cached ranges(prefix: "__seq_remaining", upper: 2)` |
| `ContinuationGuardRangeCases` | `Swapping a generated counted branch retracts guard authority despite unchanged cached bounds` |

E: Source, runtime DLL, dependency and runtime-configuration hashes still match
after execution. The broad artifact inventory also included
`unused/05_psg2.json`, a generated final-PSG dump whose timestamp and content were
rewritten by the suite; that output is not a runtime-input hash. Both the broad
inventory and the explicit runtime-input inventory are retained. No source,
test expectation, schema, compiler worker or daemon changed during this gate.

E: Evidence under `phase-b-regression/`: `checkpoint-3795a17-{head,status-before,status-after,lease,command,start,end,exit,release,inputs-verified}.txt`,
`checkpoint-3795a17.log`, `checkpoint-3795a17-{artifacts,runtime-inputs}.sha256`,
`checkpoint-3795a17-{inventory,occurrences}.json`, and
`unfiltered-results/checkpoint-3795a17.trx` (SHA-256
`1e036b7ac9e0558cf52710d621b23cd52be041596d76c48869d553d6d8559bb2`).
The owning clef checkpoint note is updated with this result. The D6(b) design
remains available for the separate auditor's review; this gate introduced no
new implementation breadth.

R: At the owner's request, the callable-aggregate design now includes
[implementation guidance from the representation repair](FFI_Correction_Callable_Aggregates_Design_2026-10-03.md#implementation-guidance-from-the-representation-repair).
It carries forward pass-owned support maintenance, complete writer/escape
accounting, unchanged-result proof invalidation, paired callable snapshots,
graph-local indexing and scoped integration gates. The advice is grounded in
the recorded repair and Baker Saturation Architecture §3; proposed aggregate
tests remain proposals. No source, schema, expectation or owner ruling changes
with these notes.

### D6(b) spec-first prerequisites

S: The owner-forwarded auditor review requires the interior callable-component
spec text, integrity rules, diagnostic allocations, interior optional-entry
distinction and complete dependency coverage before the schema batch. The
normative prelude is committed and pushed on clef-lang-spec `main` at
[`82f0077f614167e3523bc6380ee470e1180f5fb0`](https://forge.spkez.dev/FidelityFramework/clef-lang-spec/commit/82f0077f614167e3523bc6380ee470e1180f5fb0).
It adopts FFI §3.6, Closure Representation §2.4 and requirement 14, and DU §9.1
and requirement 12 from the Step 1 draft, extended to the approved common
record/union protocol. Backend and NTU clauses now agree that code remains a
portable function value and interior layout is a Clef layout. The rationale
records the impact and compiler implementation sequence.

S/R: `error-handling.md` allocates CCS8410–CCS8415 for code/data placement,
selector/family, formation/environment pairing, slot contract identity,
aggregate evidence and consumed ordinary-demand exclusion failures.
CCS8100–CCS8102 retain residence/lifetime failures. Missing inactivity evidence
normally retains demand; it is not itself an error or a reason to force an
ordinary initializer. Standalone PSG integrity refusal identifies participants
without manufacturing a source span.

R: The design now enumerates I1–I6 with a positive and negative case for each,
including changed dependency support with unchanged output and rejection of
the earlier downstream receipt. Every callable aggregate row and its typed
incidence must enter the published account. These are requirements for the
forthcoming implementation and tests, not executed test results. The requirement
map and later-tranche plan remove the obsolete deferral of interior
`Option<FnPtr>` to Step 4.

S: At the owner's terminology query, FFI §4 explicitly calls the separate work
**C-boundary absence conversion**: Clef `None` corresponds to C `NULL`, with
`Option` on the Clef side throughout. It introduces no null value, null literal
or raw pointer comparison into Clef. Interior `Some`/`None` construction and
matching use the common union protocol in this batch. The foreign single-word
carrier and its conversion remain Step 4; native tables remain Step 6.

E/A: `git diff --check` passed for the spec and design changes. A bounded peer
source review confirmed the diagnostic anchors and found the remaining
unconditional `NTUfnptr` pointer-width entry in NTU §2.3; that entry was corrected
before committing. Retrieval schema capture was fresh, but the first request
exceeded its source-character limit (HTTP 400) and the corrected natural-language
request returned `no_seed_candidates` (HTTP 422). Neither supplied source
evidence; the named local chapters and retained Step 1 drafts were read directly.
No compiler source or PSG schema changed, no test expectations changed, and no
compiler/baseline suite was rerun for this normative prelude.

### D6(b) contract integration, first gate

E: Fidelity.PSG's unfiltered suite passed **335/335**, zero skipped, on the
schema-17 working tree based on `f153c751ff35ef0b54f6cc042fb38c5756f95740`.
The trusted preceding result is 308/308. All 27 additional cases passed.
The run occupied 22:19:48–22:19:56 EDT on October 3 under test-suite lease
`020b022e66bc42f4a31bdc3eb5b25e1b`, released at completion. No baseline was rerun.

The exact command, timestamps and release are in `phase-b-regression/d6-psg-suite-*`
under the evidence root. The unfiltered TRX is
`phase-b-regression/unfiltered-results/d6-psg.trx`, SHA-256
`2b19de5993c8da86c37f58f21da770c667f34b87f5fe29a9865ba13678acbc79`.
`d6-psg-inputs.sha256` inventories the contract, generated codecs, test and tool
sources. Its entries were verified unchanged after the run. This is working-tree
evidence pending the coordinated repository checkpoint.

E/R: The model bootstrap and complete binary, JSON and integrity generators ran
under build leases. The generated schema fingerprint is
`5CB8CC3E341BC4252B41A4659944E97AF04516B2835C4B64861CD71589A894B0`.
The independent binary header oracle changed for schema 17 under the repository's
contract-version rule and Closure Representation §2.4. It still checks exact
bytes. Initial compilation exposed an effect-guard false positive on a record
field, missing type annotations and fixture syntax errors. Those were corrected
without weakening the effect guard, compiler flags or test expectations.

E: The new tests exercise I1–I6 through structural PSG readings, including changed
claim support with unchanged numeric output and rejection of the earlier account.
They distinguish actual environment instances sharing code, physical convention
differences despite equal source signatures, and absent callable slots with no
invented contract. These results do not establish source-to-native execution or
Composer receipt rejection. Clef and Alex integration is still undergoing its
first compilation and consumer validation.

R: Ordinary contracts now preserve omitted parameter ordinals and the actual
parameter/result representations. Native contracts remain pending. An all-absent
union slot can retain a pending receiving contract, while every present callable
requires an established identity. Inactive callable slots retain the actual tag
and any scalar payload of the other case, including `Result.Error false`.

R: The draft source fold settles component data placement after scalar selection
and before memory/spatial/boundary consumers. Its numeric owner checks the current
numeric account before updating aggregate representations, retaining scalar and
finite-range evidence unchanged. It performs no range pass or pipeline replay.
This placement follows the owning-fold requirement in
[Baker Saturation Architecture §3](../../clef/docs/fidelity/Baker_Saturation_Architecture.md#3-fan-out-and-fold-in).
The implementation remains unaccepted until the compiler and consumer gates run.

### D6(b) declaration and alias integration

R: Nominal declarations remain outside executable witness scopes. Canonical
callable slots now carry immutable declaration facts with exact type identities
and field or case definitions. The source account reader reconstructs those
facts from the current instantiated path. Public integrity checks their ordered
slot incidence and rejects conflicting facts for the same declaration identity
across slots. Account snapshots include the canonical slots. This corrects the
earlier attempt to require an executable node for a declaration participant.

R: The declaration definition uses a dedicated immutable DU on both sides of
publication. Reusing CCS's `TypeDefKind` would have retained mutable type-checker
cells. The structural mapper copies the settled type identities. Metadata facts
authorize only their declaration role and never authorize executable values.

R: Review found that occurrence-local formation identities would reject valid
ordinary callable aliases in Alex. The producer now uses the existing checked
value-identity reader in `CallableIngress`. Transparent aliases preserve the
formation and its environment. Distinct factory-call and read frontiers retain
distinct identities. The existing callable-emission tests gained assertions for
both cases. Alex's paired identity checks remain in place.

R: Program storage inventory now follows callable component placement, before
memory settlement. Its owning recipe therefore reads the final aggregate extent.
Numeric account renewal checks scalar and range support without rerunning range
analysis. Failed or incomplete aggregate-account renewal invalidates witness
state and returns a residual rather than retaining a publication receipt.

E: The updated PSG model and all three generators completed under build lease
`2abe0db0477f4ea9b01c253153c73ed1`. The declaration extension changes the pending
schema-17 fingerprint to
`E7DD6A92B7F79FF75CD39D384C16CCEB3E2B931878F94441649581DA797ADA3C`.
The CCS structural mapper generator completed under build lease
`19885ebf5f3544e599a7af751d42de31`, emitting 233 structural mappings.
Both leases were released. The earlier 335/335 gate predates this extension.

R: The source recipe currently implements direct singleton closed callable
components and absent union slots. End-to-end source admission remains pending.
Captured aggregate environments, multiple
formations, nested paths and mutable snapshots remain explicit residuals.
Ordinary inactivity and unused-binding demand still need their planned source
proofs. Native ABI settlement and C-boundary absence conversion remain later
tranches. Synthetic PSG and Alex fixtures describe more cases than the current
source producer admits. This checkpoint must retain that distinction.

E: The first integrated clef gate executed 2,419 cases: 1,760 passed and 659
failed, with no skipped cases. Against the trusted `3795a17` result, the
display-name occurrence comparison found 578 additional failures, three added
cases, no resolved failures and no missing occurrences. Test-suite lease
`0d39e862d9274a39ada5402081cf0f4c` covered the run and was released.
The command and release records use prefix `d6-clef-checkpoint-suite`.
Results are in `unfiltered-results/d6-clef-checkpoint.trx`, with the comparison
in `d6-clef-checkpoint-comparison.json`. This is a failed integration gate.

R/E: Repeated failures report `Common witness input WitnessEmission: Region has
source dependency … outside its admitted interface`. The common witness encoder
enumerated only graph-node identities, while the new source-owned contract and
slot rows have separate relation identities. Their complete rows need explicit
identity admission in that encoder. Scalar regions also need the exact contract
interface in their fingerprints. Unknown identities must continue to fail, and
full published proof accounts must remain current. The repair and its subsequent
checks are recorded separately from this failing run.

E/R: After typed relation-identity admission, the focused integration check ran
68 cases: 54 passed and 14 failed. The three added source cases fail with
CCS8414, `The actual callable input has no settled carrier formation`. The
remaining failures are in proof-mutation fixtures whose manual settlement
sequence replaced numeric proof identities but retained the preceding callable
contract participants. Their setup now invokes the callable owner after numeric
settlement, matching `NativeService`. Original assertions are unchanged.
The run used lease `4541f584fcb4499584c166d2ab692df6`, released at completion.
Its TRX is `inner-results/d6-clef-smoke.trx`. This focused result establishes
neither unfiltered acceptance nor source aggregate publication.

E: The final contract/consumer gate passed **343/343 Fidelity.PSG** and
**287/287 Alex**, unfiltered with no skipped cases. Lease
`82e751b630b0430dabc3ab73522384a8` covered both runs and was released.
The exact commands are in `checkpoint-contract-tests.sh`, with the outer gate
record `d6-contract-final-suites`. TRX files are
`unfiltered-results/d6-psg-final.trx` and `unfiltered-results/d6-alex-final.trx`.
Source manifests `d6-psg-final-source.sha256` and `d6-alex-final-source.sha256`
were verified unchanged after execution. Counts include all 35 added PSG cases
and all nine added Alex cases against the trusted 308 and 278 results.

E: An intermediate PSG run was 342/343. The new shared-declaration fixture
included two canonical slots but accounted for only the first slot's
participants. The I6 validator rejected it. The fixture now includes both
ordered participant lists, with the validator and substantive assertions intact.

### D6(b) bedtime handoff: final executed result

E: The already-running final unfiltered Clef gate completed with **2,419 total,
2,323 passed, 96 failed, zero skipped**. It was not restarted after the owner
requested no more big gates. `unfiltered-results/d6-clef-final.trx` and
`d6-clef-final-suite.log` retain the result. The retained-TRX comparison
`d6-clef-final-comparison.json` reports zero missing occurrences, three added
cases, 15 additional failures (12 existing-case regressions and three failing
added cases), and zero failure-to-pass transitions against trusted `3795a17`.
The comparison preserves display-name outcome multiplicities; it does not
establish identity within duplicate display-name groups.

E: The test finished within lease `38404b93fdea433e849979ed8f663e8c`.
The interrupted tool parent lost the shell exit record and automatic cleanup;
the child completed, produced its final TRX, and no owned test process remained.
The lease was explicitly released afterward, with `released` retained in
`d6-clef-final-suite-release.txt`. The final source manifest checked unchanged;
see `d6-clef-final-source-verified.txt`. No new build/test gate or daemon switch
was started for the handoff.

E/R: The current aggregate implementation remains uncommitted on `main` and is
not accepted as a completed D6(b) batch. PSG 343/343 and Alex 287/287 establish
their structural/passive gates only. Composer's eight fixture field additions
follow its failed test build and remain unbuilt/untested. The
[auditor handoff and return instructions](FFI_Correction_Checkpoint_2026-10-03.md)
record exact repository heads, evidence paths, the remaining regressions, and
the requested return to this implementer. No test expectation was changed to
obtain these results.
