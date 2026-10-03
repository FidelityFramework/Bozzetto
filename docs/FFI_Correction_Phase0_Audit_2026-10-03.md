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
