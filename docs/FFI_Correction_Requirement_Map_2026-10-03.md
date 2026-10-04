# FFI correction: requirement map

October 3, 2026. The governing contract for the FFI boundary correction,
[`clef-lang-spec/spec/ffi-boundary.md`](../../clef-lang-spec/spec/ffi-boundary.md) with the chapters it cites,
places 83 obligations on the compiler pipeline and the binding generator. This map gives each obligation its clause,
its status at the pinned heads, the evidence for that status with a grade, the remaining gap with the step that
carries it, and the owning component. It also records the corrections to the previous FFI checkpoint, the
disposition of every finding of the two reviews, and the spec amendments the work needs.

The [rulings record](FFI_Correction_Rulings_2026-10-03.md) governs where a ruling and this map differ. The
[tracker](FFI_Correction_Tracker_2026-10-03.md) is the live checklist. The [implementor
guide](FFI_Correction_Implementor_Guide_2026-10-03.md) describes the phases.

## Conventions

The requirement inventory R1-R83, with its clause citations, comes from the research session's spec lens. The
statuses come from that session's gap map. Both were re-graded here against the unfiltered baseline of October 3 and
a re-read of the decisive citations. Where this map and the gap map differ, the [differences
section](#differences-from-the-gap-map) says why.

Raw evidence lives under `/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/`, written `EV/` below. It is
local and non-portable.

| Source | Evidence path |
|---|---|
| Requirement inventory, clauses, spec ambiguities S1-S26 | `EV/research/spec-contract.md` |
| Gap map: statuses (§1), checkpoint corrections (§2), tranche plan (§3) | `EV/research/gap-map.md` |
| Lens reports | `EV/research/{ccs-baker,psg-alex,composer-farscape,history,live-daemon,design-notes}.md` |
| First adversarial review | `EV/research/critique.md` |
| Auditor review, forwarded by the owner | `EV/t1/AUDITOR-REVIEW-2.md` |
| Research-session Step 1 plan, with its own reconciliation table (§1) | `EV/t1/T1-PLAN.md` |
| Unfiltered baseline records and failing lists | `EV/t0/baseline/`, `EV/baseline-repair/INVENTORY.md` |
| Baseline triage | `EV/baseline-repair/triage-{ccs8011,ccs8403,composer}.md` |

### Heads

Every citation holds at these heads. All of them were unchanged when this map was written. Farscape carries three
uncommitted owner files, so Farscape citations read committed content (`git show HEAD:`). Bozzetto's working tree
holds only catalog documents in progress, and every other tree was clean.

| Repository | Head | Repository | Head |
|---|---|---|---|
| clef-lang-spec | `232482b` | Farscape | `5ec1954` |
| clef | `a6614b5` | BAREWire | `571ff31` |
| Fidelity.PSG | `f153c75` | Calque | `d0f6143` |
| Alex | `4e1f859` | Fidelity.Platform | `d7cc3b7` |
| Composer | `fcd68d6` | Bozzetto | `d2dc5c8f` (the later `1f716471` adds two docs only) |

### Status vocabulary

| Status | Meaning |
|---|---|
| IMPLEMENTED+TESTED | Implemented, and a named test passes in the unfiltered run at the pin |
| IMPLEMENTED-UNTESTED | Implemented, with no covering test, or with its covering test red at the pin |
| METADATA-ONLY | The facts are declared or generated, and nothing settles, publishes or checks them |
| REFUSED | Rejected with a diagnostic, cited by code and raise site |
| ABSENT | No implementation |
| UNKNOWN | Not established; the row says what would resolve it |

A row gives one status per path when the paths differ, for example an extern call against an `FnPtr` invocation.

### Evidence grades

| Grade | How the claim is known |
|---|---|
| [X] | Executed in the unfiltered run at the pin (`EV/t0/baseline/`) |
| [C\*] | Read in code or spec at the pinned head while writing this map |
| [C] | Read at the pinned head by a research agent and cited, not re-read here |
| [S] | Spec text at `232482b` |
| [L] | Observed live on the shared daemon; time-bound |
| [A] | One agent's report, for example a triage verdict |
| [D] | Documentation prose |
| [I] | Inferred |

### Citation form and owners

Code is cited as `Repo:path:line`. A bare file name refers to the file cited in full earlier in the same row, or to
the only file of that name in the checkouts above. `ffi Lnn` is `clef-lang-spec/spec/ffi-boundary.md` line nn, and
`bla` is `backend-lowering-architecture.md`.

Owners: Fs Farscape, BW BAREWire, Bk CCS and Baker (clef), PSG Fidelity.PSG, Ax Alex, Cp Composer and its target
pathway, Pl Fidelity.Platform, Cq Calque, Bz Bozzetto, In Fidelity.FSharp.Incremental, Sp the spec owner.

### Steps

| Name | Content | Document |
|---|---|---|
| Phase B | Clean baseline, including the callable-aggregate foundation (owner decision D6(b)) and the Fidelity.PSG schema change it carries | `FFI_Correction_Baseline_2026-10-03.md` |
| Step 0 (T0) | Codes, located refusals, the rulings written into the spec, `Transfer.Undeclared`, callback-slot refusal | `FFI_Correction_Step0_Plan_2026-10-03.md` |
| Step 1: T1-S, T1-A, T1-B | Spec prelude, native ABI settlement on the foundation, `FnPtr.ofExtern` | `FFI_Correction_Step1_Plan_2026-10-03.md` |
| Bozzetto track (T1-D) | Worker replacement without a daemon restart, independent of the compiler phases | Same Step 1 document |
| Steps 2-8 (T2-T8, with T4-D and T6-tables) | Later tranches | `FFI_Correction_Later_Tranches_2026-10-03.md` |

Section numbers such as "§2.3" below refer to the research-session Step 1 plan, `EV/t1/T1-PLAN.md`, so they remain
traceable if the catalog documents renumber.

## Requirement status

### A. Published ABI and adaptation facts

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R1 | Symbol identity with declaration provenance (ffi L127, L287) | Extern: IMPLEMENTED+TESTED. `ofFunction`: METADATA-ONLY. `fromSymbol`: REFUSED (CCS8096) | `BoundaryImport.Symbol` and `DeclarationPath`, `Fidelity.PSG:src/Fidelity.PSG/Settled.fs:94-107` [C\*]. clef "Scalar publication preserves descriptor identity module scope and argument order" passes [X]. `FunctionPointerPlan.Address` holds a symbol string and a lambda (`Codata.fs:307-310`) [C\*]. `fromSymbol` reaches the catch-all at `clef:src/Compiler/PSGSaturation/SemanticGraph/FunctionPointers.fs:68`, coded by the helper at :27-30 [C\*] | Entry and indirect-call rows: Step 1, T1-A. `fromSymbol` retired for `FnPtr.ofExtern` (ruling R2): Step 1, T1-B | Bk, PSG |
| R2 | Library and link identity; symbol availability recorded as an execution hypothesis (ffi L10-12, L129, L337) | Library `c` only: IMPLEMENTED+TESTED. Hypothesis record: ABSENT | `clef:src/Compiler/Baker/Recipes/BoundaryRecipes.fs:432` requires `library = "c"`, and :418 requires `TargetCore.Runtime libc` [C\*]. BoundaryConformance passes 17 of 17 [X]. The gap map names gate `generated-foreign-scalar` as the covering case [A] | `ExecutionHypothesis` rows: Step 1, T1-A. Link and load beyond libc: Step 7 | Bk, Cp |
| R3 | Calling convention of the selected target (ffi L289) | METADATA-ONLY | `CallingConvention: string` at `Fidelity.PSG:src/Fidelity.PSG/Settled.fs:100`, checked as `"CDecl"` by `Composer:src/BackEnd/LLVM/BoundaryAdmission.fs:29` [C\*]. `BoundaryPlatformPremise` (`Settled.fs:187-199`) has no C ABI field [C\*]. `cAbiOfGraph` (`clef:src/Compiler/PSGSaturation/SemanticGraph/PlatformResolution.fs:1318`) has one reader, `MappedSpans.fs:12` [C\*] | Publish the C ABI identity with the rows (G1-inv): Step 1, T1-A. Per-target closed convention set: Step 7 | Pl, Bk, PSG |
| R4 | Per-position native representation (ffi L242, L289) | Integer direct: IMPLEMENTED+TESTED, from hand-built rows. Pointer, Float, Named: REFUSED (CCS8403). Bool, i8, i16 at Composer: REFUSED. Entries and `invoke`: ABSENT | `BoundaryRecipes.fs:74-80` [C\*]. `Composer:src/BackEnd/LLVM/BoundaryAdmission.fs:18-20` admits 32- and 64-bit integers only [C\*]. `Composer:tests/Alex.Tests/BoundaryAbiTests.fs:94` passes [X], but it builds `BoundaryImport` rows and Alex operations by hand (:95-107) [C], so no Clef source reaches native code through Baker | Carriers beyond integer and boolean, and a source-through-native case (`ScalarImportCall`): Step 1, T1-A | Bk, PSG, Cp |
| R5 | Coverage at every ABI position; adaptations settled by Baker (width-inference L95; numeric-selection §3.3, §5) | Direct calls: IMPLEMENTED+TESTED. `invoke` arguments: IMPLEMENTED-UNTESTED (meets only) | QF_LIA coverage tests at `clef:tests/Clef.Compiler.Service.Tests/BoundaryEmissionCases.fs:431, :484` [C\*]. The class passes 38 of 38 [X]. Meets at `Meets.fs:155-178` with no coverage obligation and no result adaptation [C] | Entry and invocation coverage rows: Step 1, T1-A | Bk |
| R6 | Passing mode and result convention (ffi L242) | `PassBy.Value` direct: IMPLEMENTED+TESTED. Reference: REFUSED (CCS8403). C `void` entry: IMPLEMENTED-UNTESTED inside Alex, a seam violation | `BoundaryRecipes.fs:120-121` [C\*], test `BoundaryEmissionCases.fs:347` [C]. Alex builds the C-void thunk at `Alex:src/Alex/Witnesses/LambdaWitness.fs:339-357` [C\*] | Baker-owned C-void and unit-erasure adapters (MAP rules, §2.4): Step 1, T1-S and T1-A. By-value aggregates and variadics are unspecified (S23): Step 7 | Bk, Ax |
| R7 | Absence conversion per position (ffi L81-98, L211-232, L269-283) | ABSENT at every position. REFUSED where reachable | Extern positions: CCS8403 from `BoundaryRecipes.fs:74-80` [C\*]. Callback declarations: findings at `CallbackDeclarations.fs:122, 138` [C\*], raised as CCS8207 through `PlatformDeclaration.fs:146` [C\*] | `Option<CHandle>`: Step 3. `Option<FnPtr>`: T4-D design, then Step 4. Step 1 refuses `Option` at `FnPtr.invoke` with a located code | Bk, PSG, Ax |
| R8 | Code lifetime (ffi L37, L77, L329) | ABSENT | No producer or row [C] | Code lifetime from the loading contract (new ffi §3.7): Step 1, T1-S and T1-A. Plugins and JIT output stay refused. Declared host loading contracts: Step 7 | Bk, Cp, Bz |
| R9 | Target selection with provenance and invalidation (ntu-dimensional §4.2-4.3) | METADATA-ONLY | Callback pointer width is checked against the platform (`CallbackDeclarations.fs:36-52`) [C\*]. Dimensions sit in `BoundaryPlatformPremise` [C\*]. Composer re-derives its profile from the triple (`BoundaryAdmission.fs:17`) [C\*]. The Farscape CLI selects Linux LP64 (checkpoint L59) [C\*] | Published C ABI identity and per-target invalidation tests (G1-inv, G2-inv): Step 1, T1-A | Pl, Bk, PSG |
| R10 | Publication before witnessing; Alex never infers or casts (PSG §2.2 L59-68) | Direct calls: IMPLEMENTED+TESTED. Callables: Alex derives entry types itself, then refuses | `Alex:src/Alex/Witnesses/FunctionPointerWitness.fs:24-27` maps types from the interior declaration [C\*]. The refusal casts nothing (`Alex:src/Alex/Patterns/FunctionPointerPatterns.fs:10-14`) [C\*] | Rows first, Alex reads rows only: Step 1, T1-A | Bk, PSG, Ax |
| R11 | Admission record per witness form (bla §2.1.1) | Extern call: piecewise evidence. Address and indirect call: ABSENT | Composer `ForeignDeclarationTests.fs:143, 295` and `BoundaryAbiTests.fs:94` pass [X] | `Composer:docs/Native_Callable_Admission.md` with tool versions: Step 1, T1-A | Ax, Cp |

### B. `FnPtr` operations

Ruling R2 retires `fromSymbol`. R13 and R14 therefore describe a retired surface, and their obligations pass to
`FnPtr.ofExtern`.

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R12 | `'F` is a function type (ffi L115) | IMPLEMENTED+TESTED, as CCS8096 | `clef:src/Compiler/NativeTypedTree/Expressions/Types.fs:1203-1209` [C\*]. "FnPtr rejects a concrete payload without a function signature" (5 rows) and "Concrete instantiation cannot hide a non-function native signature" (3 rows) pass [X] | CCS8096 is tabled for closed adapters only (S3). Code allocation: Step 0 | Bk, Sp |
| R13 | `fromSymbol` literal, link-time resolution (ffi L123-129) | REFUSED (CCS8096), untested | Catch-all at `FunctionPointers.fs:68` [C\*]. No test names `fromSymbol` (retrieval search) [A] | Superseded: `fromSymbol` refused with a located code naming `ofExtern`: Step 1, T1-B | Bk |
| R14 | Extern `func.func` plus `func.constant`, no cast (ffi L139-140) | ABSENT | Alex has only the interior `pFuncConstant` [C] | The same code generation for `ofExtern` (new §3.2 text, §2.9): Step 1, T1-B | Ax |
| R15 | Pathway realizes `llvm.mlir.addressof` (ffi L140) | Interior closures: IMPLEMENTED-UNTESTED at the pin. Native entries: ABSENT | Stock `convert-func-to-llvm` (`Composer:src/BackEnd/LLVM/Lowering.fs:42-49`) [C]. The NativeCallbacks runner passes 0 of 29, every case at compile, `ResultCases` included [X]. The three regression samples that run at the pin were not checked for function values [I] | Native-entry admission and the realization check (Edges 0, A, B): Step 1, T1-A. Interior native evidence returns with the Phase B runner repair | Cp |
| R16 | `invoke` calls through the entry with marshalling (ffi L142-163) | REFUSED | `FunctionPointerPatterns.fs:13-14` [C\*]. The unfiltered runner reproduces it for NativeCallbacks (`EV/baseline-repair/nc/structural.txt`) [X]. No test asserts the refusal [C] | Scalar invocation: Step 1, T1-A. `Option` operands: Steps 3 and 4 | Bk, Ax |
| R17 | `ofFunction` takes a module-level binding only (ffi L171-177) | IMPLEMENTED+TESTED | `FunctionPointers.fs:55-59` [C\*]. `FunctionPointerCases.fs:68-84`, xUnit class `FunctionPointerTests`, 6 of 6 pass [X] | None | Bk |
| R18 | Complete native representation for the entry (ffi L178-180) | REFUSED at Alex. ABSENT in Baker | `FunctionPointerPatterns.fs:10-11` [C\*]. `FnPtr` settles as a pointer word (`clef:src/Compiler/PSGSaturation/SemanticGraph/Placement.fs:147`, `clef:src/Compiler/Baker/Ingredients/ValueRepresentations.fs:66`) [C\*], which Alex spells `index` (`Alex:src/Alex/Dialects/Core/Types.fs:65`) [C\*] | `FnPtr` as a function value with a Baker-settled contract (ruling R1): Step 1, T1-A, on the Phase B foundation | Bk, PSG, Ax |
| R19 | `Option<FnPtr>` represents absence (ffi L197-207) | ABSENT. Callback parameters: REFUSED (CCS8207) | As R7 [C\*] | Boundary conversion: T4-D, then Step 4. Owner-forwarded D6(b) review settles the interior case: Phase B uses the common union-payload protocol, with no callable payload under `None`. C-boundary absence conversion is independent. | Bk |
| R20 | Provenance through aliases, records and transport (ffi L178, L287) | Records, aliases, repeated addresses: IMPLEMENTED+TESTED at graph level. Unannotated `FnPtr` parameters: ABSENT. Target sets: computed, unpublished | "Declared callback ABI follows another address and its indirect scalar results" and "Native address retains its named declaration and full invocation" pass [X]. `Composer:tests/NativeCallbacks/README.md:237-243` records the transport gap [C\*]. `NativeCalls` target sets at `CallableOrigins.fs:47-49` [C] | Published target sets, and contracts preserved through aliases, branches and records (§3.6 item 2): Step 1, T1-A, on the Phase B foundation | Bk, PSG |

### C. Closed entries and `CallbackDescriptor`

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R21 | A descriptor for every C-invoked entry, including `void` (ffi L287, L417) | METADATA-ONLY: generated and read, never published | Generated at `Farscape:src/Farscape.Core/FidelityCodeGenerator.fs:571-597` [C]. Read by `CallbackDeclarations.fs` [C\*]. "Listener descriptors govern native callback entry scalar width and void result" fails at the pin [X]: the fixture reads declarations with no platform (`ClosureValueCases.fs:120-121`, with the platform attached only at :129) [A, triage RC8 of an executed failure] | Fixture repair: Phase B. Publication: Step 1, T1-A | Fs, Bk |
| R22 | Complete C arity and result convention preserved (ffi L333) | UNKNOWN | Unused-formal omission appears limited to direct-use code (`clef:src/Compiler/PSGSaturation/SemanticGraph/OrdinaryDemand.fs:107-114`) [C\*, I]. No test | MAP rule "a native entry never omits a formal", tested on ListenerEntry's unused `_data`: Step 1, T1-S and T1-A | Bk |
| R23 | A closed entry has no environment; a no-op release proves nothing (ffi L335) | IMPLEMENTED+TESTED | `clef:src/Compiler/Nanopass/ClosedCallbacks.fs` [C]. xUnit class `ClosedCallbackTests` (file `ClosedCallbackCases.fs`), 16 of 16 pass [X]. `clef:docs/fidelity/Closed_Native_Callback_Adapters.md:50-52` [D] | None | Bk |
| R24 | Entry inputs need admission; an unconvertible contract is rejected (ffi L281, L283, L321) | Farscape optional defaults: IMPLEMENTED+TESTED. CCS: REFUSED (CCS8207) | Farscape 649 of 649 [X]. CCS8207 as R7 [C\*] | Conversions: Step 4 | Fs, Bk |
| R25 | A narrower nonnull slot is documented (ffi L321) | Narrowing without evidence: IMPLEMENTED+TESTED in Farscape, contrary to ruling R6. Documentation: generator doc only | Tests assert bare `FnPtr` for unannotated and `_Null_unspecified` slots (`Farscape:tests/Farscape.Tests/FidelityCodeGeneratorTests.fs:300-319`) [C\*]. `Farscape:docs/08_Nullable_Pointer_Architecture.md:84-89` [C\*] | Ruling R6 replaces the gap map's per-binding comment: such slots become `Option<FnPtr>`, and the binding is diagnosed before output until Step 4. Explicit evidence narrows: a header annotation, or a binding declaration with provenance recorded as a hypothesis. Step 0 | Fs |
| R26 | Closed-adapter rules (error-handling L356) | IMPLEMENTED+TESTED | `ClosedCallbackTests` 16 of 16 [X]. The spec defines the mechanism only in the error table (S1, S21) [S] | Spec text for the mechanism: unscheduled (see [Unscheduled spec items](#unscheduled-spec-items)) | Bk |

### D. Absence carriers

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R27-R30 | One-word carrier, explicit conversion, outgoing and incoming directions, portable middle end (ffi L43-49, L91-98, L211-232) | ABSENT. REFUSED where reachable | Interior options are tagged (`clef:src/Compiler/Baker/Ingredients/Options.fs`) [C]. CCS8403 and CCS8207 as R7 [C\*] | `Option<CHandle>` with the data-handle pathway commitment (S11) and the option-operations conflict (S5): Step 3. `Option<FnPtr>`: T4-D, Step 4 | Bk, PSG, Ax, Sp |
| R31 | Nonnull needs evidence or a checked conversion; reliance recorded as a hypothesis (ffi L265-267, L279) | Farscape evidence handling: IMPLEMENTED+TESTED. Hypothesis record and checked conversion: ABSENT | Contradiction refusal at `Farscape:src/Farscape.Core/FidelityCodeGenerator.fs:492-493` [C\*]. Farscape 649 of 649 [X] | Hypothesis row kind: Step 1, T1-A. Nonnull hypotheses and the checked conversion: Step 3 | Fs, Bk |
| R32 | `CHandle` and `FnPtr` never null; no forging (ffi L22-26, L79) | IMPLEMENTED+TESTED, as CCS8010 | `clef:src/Compiler/PSGSaturation/SemanticGraph/BoundaryValues.fs:148-152` [C\*]. "Zero initialization cannot forge a demanded non-null boundary value", 19 rows, pass [X] | CCS8010 is tabled as the `null` keyword only (`error-handling.md:496`) [S]: code allocation in Step 0. Artifact-level NULL confinement (S24): unscheduled | Bk, Sp |
| R33 | No pointer operations on handles (ffi L14, L79, L331) | Pointer operations: IMPLEMENTED+TESTED. `CHandle` equality: admitted, against ffi L79 | CCS8401 [C]. `Calque:src/Calque.Core/SourceDialect.fs:26-29` [C\*]. "raw pointers and unchecked construction are refused in their syntax roles" (`FormattingTests.fs:119`) passes [X]. Equality admitted as `OpaqueReference` (`clef:src/Compiler/Baker/Recipes/NumericOperationRecipes.fs:80-84`) [C\*] | Decision D4: `CHandle` equality settled with its resource contract in Step 3. `FnPtr` equality refused now. Whether any path admits it today is UNKNOWN (slot `[NATIVE-ENTRY-EQUALITY]`, Step 1) | Bk, Sp |

### E. Resources

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R34 | Owned, borrowed from a named owner, or static (ffi L327) | METADATA-ONLY | `OwnershipTransfer`, `CallerOwns` and `CalleeOwns` have 0 matches in `clef/src` [C\*]. BAREWire's `Transfer` has no static class and no named owner (`BAREWire:src/Descriptors/Bindings.fs:48-51`) [C\*] | `Transfer.Undeclared`: Step 0. Additive resource descriptor bound by exact declaration identity: Step 3 | BW, Fs, Bk |
| R35 | Owned acquisition paired with release, including failure cleanup (ffi L327, L379) | ABSENT. MappedReturn: METADATA-ONLY | `MappedBindings.fs:55-161`, `ObligationElaboration.fs:356-368` [C]. Alex refuses mapped calls (`Alex:src/Alex/Patterns/MappedViewPatterns.fs:10-11`) [C\*]. Pthread `mallocContext` declares `CallerOwns` with no release pairing (`Fidelity.Platform:Environments/Linux/x86_64/Bindings/Pthread/Storage/Storage.clef:70-86`) [C\*] | Step 3 | Bk, PSG, Ax |
| R36, R37, R39 | Borrowed owner outlives uses; static lifetime established; release retires every alias (ffi L327-329) | ABSENT | No rows [C] | Step 3 | Bk |
| R38 | Missing ownership is never reported as borrowed (ffi L327) | ABSENT, and generated metadata contradicts it | Farscape's `Borrowed` default at `FidelityCodeGenerator.fs:419-421` [C\*]. BAREWire's `Function.cdecl` default at `BAREWire:src/Descriptors/Bindings.fs:157-159` [C\*]. Callback descriptors hard-code `Borrowed` (`FidelityCodeGenerator.fs:596`) [C\*]. Inert today, because nothing reads it | Ruling R5, both defaults removed and `Undeclared` added: Step 0. Admission blocked on `Undeclared`: Step 3 | BW, Fs |
| R40 | No source ownership annotations (ffi L329) | No such syntax: IMPLEMENTED-UNTESTED. Graph obligations: ABSENT | [C] | Step 3 | Bk |
| R41 | Application-facing operations establish obligations first (ffi L291) | ABSENT. Farscape's refusal of unsupported listener builders and wrappers: IMPLEMENTED+TESTED | BoundaryConformance gates 3-8 pass [X] | Steps 3 to 5 | Fs, Bk |
| R42 | Ownership evidence is not duplicated (conformance §6.1 item 3) | ABSENT | No ownership evidence exists [C] | Step 3 | Bk |

### F. Callback environments

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R43, R45-R49 | Userdata association, retention, destroy hook exactly once, undo on failed registration, explicit retirement (ffi L333-335, L417) | ABSENT. Captured handlers: REFUSED (CCS8096) | `FunctionPointers.fs:57, 59` [C\*]. `ClosedCallbacks.fs:113, 236` [C] | Step 5 | Bk, PSG, Fs |
| R44 | No integer retyping of userdata (ffi L333) | IMPLEMENTED+TESTED | Farscape "legacy integer-address callbacks cannot emit Clef source" (`Farscape:tests/Farscape.Tests/CallbackTests.fs:410`) and its forbidden-token check (:361) [C\*], and the suite passes 649 of 649 [X]. `nativeint` is not denotable (CCS8706, clef `DimensionalCases.fs`) [C] | None | Fs, Cq, Bk |
| R50 | Native context stays inside the binding (ffi L419) | ABSENT beyond GObject signals | [C] | Step 5 | Fs |
| R51 | One joint constraint (program-hypergraph L45) | ABSENT | [C] | Step 5 | Bk |

### G. Spatial admission

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R52-R55 | Extent, alignment, access, retention; length related to the bound; NUL inside the extent (ffi L331) | `Sys.write` intrinsic: IMPLEMENTED. Extern buffers: REFUSED (CCS8403) | `BoundaryByteView`, `BoundaryStringExtent` and `IntrinsicWriteProof` rows in `Settled.fs` [C]. `BoundaryRecipes.fs:120-121` [C\*] | Step 6 | Bk, PSG |
| R56 | A mapped result becomes `Ptr` only through an admitted projection (ffi L331) | MappedReturn: METADATA-ONLY. Borrowed views: IMPLEMENTED-UNTESTED (tests red at the pin) | 21 of 23 BorrowedViewTests fail [X]. 18 fail first with CCS8011 on a fixture defect, the shared prefix's `NativeDefault.zeroed ()` integer result at `BorrowedViewCases.fs:17`. 3 fail first with CCS8403 [A, triage of executed failures] | Fixture repair: Phase B. The gap map's T6 assignment for these tests is withdrawn. Mapped projection: Step 6 | Bk |
| R57 | Access-kind invariance (access-kinds L137-143) | UNKNOWN | No lens assessed it | Read the CCS8020-8022 tests: Step 6 | Bk |

### H. Settlement and diagnosis

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R58 | Obligations with actual participants (ffi L337) | Direct calls: IMPLEMENTED, with untyped participants. Other forms: ABSENT | `Participants: Set<NodeId>` in `BoundaryImport` (`Fidelity.PSG:src/Fidelity.PSG/Settled.fs:104`) [C\*], listed as a legacy untyped set in `Fidelity.PSG:tests/Fidelity.PSG.Tests/GeneratedTests.fs:39-46` [C\*] | New rows use typed `Participant` lists: Phase B schema change and Step 1 | Bk, PSG |
| R59 | Unresolved premises retained; nothing emitted while pending (ffi L337) | Direct boundary: IMPLEMENTED+TESTED. `FnPtr`, closed-callback and callback-declaration findings: outside the publication gate | CCS8403 via `clef:src/Compiler/PSGSaturation/SemanticGraph/WitnessEmission.fs:38-45` [C\*]. `sourceAdmitted` (`clef:src/Compiler/NativeTypedTree/NativeService.fs:1250-1254`) omits `functionPointerDiagnostics` (computed at :1292), `closedCallbackDiagnostics` (:1008) and `declarationDiagnostics` (:1427, after `WitnessEmission.prepare` at :1420-1425). They join only the final list at :1445 [C\*]. Composer's error stop guards emission (`Composer:src/Core/CompilationOrchestrator.fs:89-93`) [C\*], and the gap map reports no other guard [C] | Gate publication at :1420, with those analyses moved before it: Step 1, T1-A | Bk |
| R60 | A declaration is not its proof (ffi L337) | IMPLEMENTED-UNTESTED: respected in code and documentation | `BAREWire:src/Descriptors/Bindings.fs:3-16` [C] | Review gate in every step | all |
| R61 | Located diagnosis at the commitment boundary (ffi L337) | CCS refusals: IMPLEMENTED, with overloaded or untabled codes. Alex root refusals: IMPLEMENTED, uncoded and unlocated | `Alex:src/Alex/Witnesses/FunctionPointerWitness.fs:16` uses `WitnessOutput.error`, while :18 shows the coded form [C\*]. The pin's NativeCallbacks log has 3 root lines and 9 cascades (`EV/baseline-repair/nc/structural.txt`) [X] | Code allocation and coded, located refusals: Step 0 | Bk, Ax, Sp |
| R62 | Lowering preserves or rechecks discharged properties (ffi L337; conformance L67-71) | ABSENT, for FFI and for direct calls | `Alex:src/Alex/Traversal/CoverageValidation.fs` checks that source occurrences and body-free import scopes were visited (:1-6, :29-36, :67-71) [C\*]. That check measures traversal coverage, and the auditor review places preservation in the target pathway. Composer checks the scalar carrier set before lowering (`BoundaryAdmission.fs:13-35`) [C\*] | Pathway realization recheck (Edges 0, A, B and the ELF stage), owned by Composer. The import path gets its first recheck: Step 1, T1-A | Cp |
| R63 | Hypotheses distinct from evidence (ffi L267, L337) | ABSENT | [C] | `ExecutionHypothesis` rows and a separate receipt category: Step 1, T1-A. Nonnull reliance: Step 3 | Bk, Cp |
| R64 | Node-local codata; the witness does not query hyperedges (program-hypergraph L55-66) | Direct calls: IMPLEMENTED. Callables: ABSENT | [C] | Step 1, T1-A | PSG, Ax |
| R65 | Revision authorization and invalidation (program-hypergraph L68-116) | `Integrity.check` before witnessing: IMPLEMENTED+TESTED. Invalidation of boundary rows on a descriptor or target change: UNKNOWN | `Alex:src/Alex/Generation.fs:134-147` [C\*]. `Alex:tests/Alex.Tests/Tests/TraversalOccurrenceTests.fs:109` [C\*], Alex 278 of 278 [X] | Invalidation tests (G1-inv, G2-inv): Step 1, T1-A | Bk, PSG, In |
| R66 | Residual foreign undefined behavior named (behavior-classification L47) | ABSENT in the spec | S14 [S] | UB list: unscheduled. Terminology half of S14: Step 1, T1-S | Sp |

### I. Farscape generation contract

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R67 | Annotation mapping, including `FnPtr` slots; contradictions diagnosed (ffi L250-265) | Clang and attribute paths: IMPLEMENTED+TESTED. SAL and GCC paths: UNKNOWN | Farscape 649 of 649 [X]. The CLI is Linux-only [C] | SAL and GCC: with Step 7 targets | Fs |
| R68 | Unannotated pointers are optional (ffi L269-283) | Data pointers: IMPLEMENTED+TESTED. Function-pointer slots: narrowed to `FnPtr` | "pointer return type emits as option by default" [C], 649 of 649 [X]. Slots as R25 [C\*] | Ruling R6: Step 0 | Fs |
| R69 | Declare symbol, ABI, absence and resource contract first (ffi L287) | Symbol and scalar ABI: IMPLEMENTED+TESTED. Resource contract: METADATA-ONLY, with an invented `Borrowed` default | As R38 [C\*] | Defaults removed: Step 0. Resource descriptor: Step 3 | Fs, BW |
| R70 | Value kinds in source, widths in descriptors, nominal markers (ffi L289) | IMPLEMENTED+TESTED. Typed Wayland uses `CHandle<unit>` for every proxy | `CodeRenderer.fs:152` and "opaque typedef does not expose null or address constructors" [C], 649 of 649 [X]. `TypedProtocolGenerator.fs:12-13, 167-169` [C] | Per-interface markers: unscheduled | Fs |
| R71 | Hints with provenance; admitted subset stated (ffi L293-301) | Unknown-key diagnostic: ABSENT | `opaque_handles` and `transitive_headers` are never read [C]. The conformance pilot sets `opaque_handles = true` (`tests/BoundaryConformance/boundary.pilot.toml:12`) [C] | A warning first, or the pilots and docs changed with the diagnostic (critique minor 6): Step 0 | Fs |
| R72 | `FnPtr` and `Option<FnPtr>` callback parameters (ffi L313-321) | `FnPtr`: IMPLEMENTED+TESTED. `Option<FnPtr>`: ABSENT. Slots are narrowed instead | As R25 [C\*] | Refusal: Step 0. Admission: Step 4 | Fs |
| R73 | Fail closed before writes (ffi L283, L430) | IMPLEMENTED+TESTED | BoundaryConformance gates 3-8 with output sentinels, 17 of 17 [X] | None | Fs |
| R74 | Descriptor and range evidence stay distinct participants (ntu-dimensional L262-264) | Direct calls: IMPLEMENTED+TESTED. Callbacks: ABSENT (unpublished) | `DeclarationFacts` against coverage obligations [C]. `BoundaryEmissionCases` 38 of 38 [X] | Step 1, T1-A | Bk |
| R75 | Descriptors are never referenced by executed code (CCS8066) | UNKNOWN | Not assessed. BoundaryConformance gate 11 exempts placeholder bodies from CCS8010 [C] | A T1-B test that `ofExtern` never turns a descriptor quotation into executed code: Step 1 | Bk |

### Cross-chapter requirements

| R | Requirement and clause | Status | Evidence | Gap and landing | Owner |
|---|---|---|---|---|---|
| R76 | A function value is two SSA values; no address held as data (closure-representation L180, L278; bla L112, L197) | Interior closures: IMPLEMENTED-UNTESTED at the pin. `FnPtr`: a pointer word, contrary to the clause | `Alex:src/Alex/Patterns/CallablePatterns.fs:132-151` emits `func.constant` with the settled signature [C\*]. Runner and pointer word as R15 and R18 [X], [C\*] | `FnPtr` record fields on the Phase B foundation. Native entries: Step 1, T1-A | Bk, Ax |
| R77 | Closure transfer between memory spaces (closure-representation L139) | UNKNOWN | Not assessed | `FnPtr` transfer out of the process refused (new §3.7 item 5): Step 1 | Bk, Cp |
| R78 | No silent substitution (conformance L65, L121) | IMPLEMENTED+TESTED: Alex refuses rather than casting, Farscape fails closed | `FunctionPointerPatterns.fs:10-14` [C\*]. Gates 3-8 [X] | Review gate in every step | all |
| R79 | Interactive sessions keep FFI requirements (interactive-development L167-170) | ABSENT: no Clefx host | `Bozzetto/docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md` [D] | Outside this correction | Cp, Bz |
| R80 | Platform intrinsic codes CCS8030-8033 (platform-bindings L368-387) | UNKNOWN | Not assessed | Outside the FFI correction, recorded | Bk |
| R81 | No `DllImport`, `MarshalAs`, `[<In>]`/`[<Out>]`, `UnmanagedFunctionPointer` (special-attributes L139, L185-192) | `DllImport`: IMPLEMENTED+TESTED. Others: UNKNOWN | `Calque:src/Calque.Core/SourceDialect.fs:252-254` and `FormattingTests.fs:130-131` [C\*], 149 of 149 [X] | Others: unscheduled | Bk, Cq |
| R82 | No bit-cast (intrinsics-cryptography-bits L189-219) | UNKNOWN | Not assessed | No-cast oracles and the realization check: Step 1, T1-A | Bk |
| R83 | BAREWire union contract (discriminated-union-representation L453-463) | UNKNOWN | Outside FFI scope | None | Bk, BW |

## Differences from the gap map

| R | Gap map | This map | Basis |
|---|---|---|---|
| R3 | Cited `Alex docs/04_Debt.md:48` for the unpublished C ABI | Cites `Settled.fs:187-199` and `cAbiOfGraph`'s single reader | `04_Debt.md:48` records only that the selected target is a field of Alex's request (critique minor 2) [C\*] |
| R4 | Native run implied end-to-end | The native test builds rows by hand | Critique minor 5 [C] |
| R7 | CCS8207 cited at `CallbackDeclarations.fs:122, 138` | Same lines, with the code assignment at `PlatformDeclaration.fs:146` | Those lines carry the finding text only [C\*] |
| R13, R14 | Open design gap (Q2) | Superseded by ruling R2 | Rulings record |
| R15, R76 | Interior closures IMPLEMENTED+TESTED natively, citing `ResultCases` | IMPLEMENTED-UNTESTED at the pin | Runner 0 of 29 at compile, `ResultCases` included [X]. The earlier native passes are README history |
| R19 | Interior `Option<FnPtr>` refused until T4 | Interior protocol belongs to Phase B; C-boundary absence conversion stays T4 | Owner-forwarded callable-aggregate design review |
| R20 | "PARTIAL", README only | Graph-level tests named | Two passing clef tests [X] |
| R21 | Fixture fix in T0 | Phase B | Triage RC8 places it in the baseline repair [A] |
| R23, R17 | Class names `ClosedCallbackCases`, `FunctionPointerCases` | xUnit classes `ClosedCallbackTests`, `FunctionPointerTests` | The file and class names differ in the TRX [X] |
| R25, R68, R72 | Remedy: a per-binding generated comment | Remedy: `Option<FnPtr>` and refusal before output | Ruling R6 |
| R31 | Contradiction refusal at `FidelityCodeGenerator.fs:488-490` | `:492-493` | Lines 488-489 hold the `nonnull_callbacks` requirement (critique minor 1) [C\*] |
| R44 | IMPLEMENTED, no test named | IMPLEMENTED+TESTED | `CallbackTests.fs:361, 410` [C\*], suite green [X] |
| R56 | 21 of 23 fail with CCS8011; unblocked in T6 | 18 CCS8011-first and 3 CCS8403-first, a fixture defect, repaired in Phase B | CCS8011 triage §0 item 4 [A] on executed failures [X] |
| R62 | Direct calls IMPLEMENTED+TESTED through `CoverageValidation.fs:31-36` | ABSENT for preservation | `CoverageValidation` measures traversal coverage (auditor review) [C\*] |

## Citation check

While writing this map, more than 60 citations from the gap map, the critique and the auditor review were re-read at
the pinned heads. These were wrong or weak:

| Where | Citation | Finding |
|---|---|---|
| Critique B1 | `Bozzetto/Program.fs:461` for the worker environment read | The read is at `Bozzetto/Bozzetto/ComposerSupervisor.fs:461`. `Program.fs:461` parses JSON [C\*] |
| Gap map R31 | `FidelityCodeGenerator.fs:488-490` | `:492-493` [C\*] |
| Gap map R3 | `Alex docs/04_Debt.md:48` | Does not mention `CAbiDescriptor` [C\*] |
| Gap map R62 | `CoverageValidation.fs:31-36` as preservation evidence | Traversal coverage of import scopes [C\*] |
| Gap map R15, R76 | NativeCallbacks interior cases as passing native evidence | 0 of 29 at the pin [X] |
| Gap map C10 | 17_ExternCall expected, by code reading, to hit CCS8403 or the descriptor refusal | It fails on CCS8706 (`unativeint`, `nativeint`) and CCS8018 (`64un`) (`EV/baseline-repair/triage-composer.md`) [A on X] |
| Gap map C6 | The "61" attributed to the checkpoint | The figure comes from the previous session's summary to the owner (`EV/research/main_assistant_msgs.txt:157, 293`), not the checkpoint document [C\*] |

The other re-read citations held, including `Settled.fs:10, 94-107, 187-199`, `Codata.fs:307-310`,
`Placement.fs:147`, `ValueRepresentations.fs:43-49, 66, 123`, `FunctionPointers.fs:55-59, 68`,
`BoundaryRecipes.fs:74-80, 118, 120-121, 138-143, 418, 432`, `NumericOperationRecipes.fs:80-84`,
`NativeService.fs:1250-1254, 1292, 1420-1427`, Alex `Dialects/Core/Types.fs:65`, `FunctionPointerWitness.fs:16,
24-27, 32`, `FunctionPointerPatterns.fs:10-14`, `LambdaWitness.fs:339-357`, `BoundaryAdmission.fs:13-35`,
`BoundaryAbiTests.fs:94`, `FidelityCodeGenerator.fs:419-421, 494-495, 592-596`, BAREWire `Bindings.fs:26-51, 63-69,
74-78, 157-159`, `CallbackDeclarations.fs:36-52, 98-99`, `ComposerWorkerClient.fs:146-151`,
`LiveProviderTests.fs:146`, `ntu-types.md:229`, `error-handling.md:356, 496`,
`namespace-and-module-signatures.md:11-14`, `Design_Supersession_Register.md:83, 128` and the checkpoint lines cited
in C1-C5 and C14.

## Checkpoint corrections

The previous checkpoint is `Farscape:docs/handoff/2026-10-03-ffi-boundary-checkpoint.md`, cited as CP Lnn. Its lines
were re-read at `5ec1954` [C\*]. The gap map's corrections (`EV/research/gap-map.md` §2) are restated here with the
baseline results that arrived after them.

| C | Kind | Claim | Finding | Grade | Resolution |
|---|---|---|---|---|---|
| C1 | Understated | CP L51, L64-66: the repair belongs to Baker's settled ABI and adaptation rows and their PSG publication, and Alex must consume them | Six gaps across four seams. No Baker recipe settles an entry or indirect-call ABI. No PSG row can carry one (`Settled.fs:10`, `Codata.fs:307-310`). `FnPtr` settles as a pointer word that Alex spells `index` (`Placement.fs:147`, `ValueRepresentations.fs:66`, `Alex:src/Alex/Dialects/Core/Types.fs:65`), against ffi L139-140. Alex builds the C-void entry body itself (`LambdaWitness.fs:339-357`). Alex's two patterns are unconditional stubs (`FunctionPointerPatterns.fs:10-14`), present since Alex `85f071e` on September 27. Composer admits only `BoundaryFuncDecl` scalar imports (`BoundaryAdmission.fs:13-35`). Publication is not gated on `FnPtr` findings (`NativeService.fs:1250-1254`) | [C\*] | Phase B foundation, Step 1 (T1-A) |
| C2 | Overstated | CP L52: owned, borrowed and static lifetimes have "Metadata and obligations … retained" | No ownership obligation exists. `OwnershipTransfer`, `CallerOwns` and `CalleeOwns` have 0 matches in `clef/src`. The field survives only as uninterpreted `DeclarationFacts` of admitted scalar imports (`BoundaryRecipes.fs:40-63`). The only release pairing is MappedReturn's declaration with a span obligation, and Alex refuses that call (`MappedViewPatterns.fs:10-11`). Accurate wording: metadata only, unread, no obligations | [C\*] | Steps 0 and 3 |
| C3 | Missing, spec conflict | CP L33: ownership metadata remains an explicit contract | Undeclared ownership is emitted as `Borrowed`, with the note "Borrowed is the inferred default" (`FidelityCodeGenerator.fs:419-421`). BAREWire's `Function.cdecl` defaults to `Borrowed` (`BAREWire:src/Descriptors/Bindings.fs:157-159`). Callback descriptors hard-code `CDecl` and `Borrowed` (`FidelityCodeGenerator.fs:595-596`). ffi L327: "Missing ownership information SHALL NOT be reported as proof that a resource is borrowed" | [C\*], [S] | Ruling R5: Step 0. `CDecl` hard-coding: Step 7 |
| C4 | Overstated | CP L27-29: complete native signature descriptors use the selected ABI model | The vocabulary has no per-level nullability, environment or destroy-hook association, static class, named owner, release or variadic flag. Its convention names are x86-32 names (`BAREWire:src/Descriptors/Bindings.fs:26-51`). "Selected ABI model" is the LP64 data model, and the convention is always `CDecl` | [C\*] | `Undeclared`: Step 0. Entry `TypeRef`: Step 2. Resource descriptor: Step 3. Retention: Step 6. `CallConv`: Step 7 |
| C5 | Misleading | CP L20-22, unannotated data pointers default to option; CP L58, nested slots refused | Top-level unannotated and `_Null_unspecified` function-pointer parameters are narrowed to bare `FnPtr` (`FidelityCodeGeneratorTests.fs:300-319`). Nullable wrapping applies to data pointers only (`FidelityCodeGenerator.fs:494-495`). The narrowing is documented in `docs/08_Nullable_Pointer_Architecture.md:84-89` and nowhere in generated output (ffi L299 SHOULD, L321 SHALL) | [C\*] | Ruling R6 replaces documentation with refusal: Step 0. Admission: Step 4 |
| C6 | Understated | The previous session's summary: 111 focused checks passed, and the full suite "retains 61 baseline failures", none introduced | The counts were accurate for their run, which held 1,723 of 2,354 results, exactly the `Category=Compiler.Service` subset. The unfiltered suite at `a6614b5` has 94 failures. All 61 still fail, and the other 33 sit in 28 classes the prior run never included. The 61 were themselves a subset of 66 earlier failures, 5 of which `a6614b5` repaired. 23 of the 61 are FFI-adjacent: 21 BorrowedViewTests and 2 ClosureValueTests. "Listener descriptors govern native callback entry scalar width and void result" fails on its fixture, so callback width and void settlement is not test-established | [X] `EV/t0/baseline/clef.md`; [A] triage | Phase B |
| C7 | Understated | ffi L16: "`FnPtr.fromSymbol` currently produces `CCS8096`" | True through the catch-all at `FunctionPointers.fs:68`, and no test covers it. CCS8096 is tabled for closed-adapter errors (error-handling L356). The spec's spelling `fromSymbol<'F>` may raise CCS8092 first (UNKNOWN). `fromSymbol` also has no ABI source, because nothing associates it with a descriptor (S1, S2, S10) | [C\*], [S] | Ruling R2 retires it: Step 1, T1-B. L16 is updated at each step's acceptance |
| C8 | Missing | Diagnosis quality | `FnPtr`, closed-callback and callback-declaration diagnostics do not gate publication (`NativeService.fs:1250-1254`, :1445). Alex's root refusals are uncoded and carry no node or range (`FunctionPointerWitness.fs:16`). In the pin's NativeCallbacks log, nine cascade lines (AX4001, `pFunctionDef` and unwitnessed results) bury the three root lines (`EV/baseline-repair/nc/structural.txt`) | [C\*], [X] | Codes and located refusals: Step 0. Publication gate: Step 1, T1-A |
| C9 | Missing, operations | None | The live worker ran from runtime snapshot `dashboard-work-20261003-lTyXpZ`, which matched the heads by hash and file time. The installed `boz` release `2026-10-02-calque-7d3557971d26-66c0b1bce8fe` lags (Composer `72cb4ff`, clef `2593630`, PSG `b1f0088`). Bozzetto's start path uses `boz` (`Bozzetto:docs/Bozzetto_Live_Provider_Checkpoint_2026-09-30.md:68-73`), so a restart through it would drop the `a6614b5` guards | [L] `EV/research/live-daemon.md`, [C\*] | Standing rule: never restart through `boz`. Bozzetto track |
| C10 | Missing | Native evidence elsewhere | The Composer README records native passes that do not hold at the pin. IgnoreValues and ListenerEntry fail at compile on the `BoundaryRecipes.fs:80` and `:124` refusals [X], and their oracles assume strict evaluation, which default demand (spec `586010e`) makes false [A]. 17_ExternCall fails on raw `unativeint` and `nativeint` bindings (CCS8706) and a `64un` literal (CCS8018) [A on X]. ForeignReferences, PointerCells and ForeignScalarArrays were not run in the baseline (UNKNOWN). No test carries Farscape-generated `abs` to native execution. CP L49 claims publication only, which is accurate | [X], [A] | Fixture rewrites: Phase B. Gates: Step 3. `ScalarImportCall`: Step 1 |
| C11 | Missing, spec contradiction | None | `ntu-types.md:229` maps `NTUfnptr` to `index`. clef's `docs/fidelity/phg/Design_Supersession_Register.md:83, 128` already supersedes that mapping ("function value is a `func` value"). The spec text is unchanged | [C\*] | Ruling R4: Step 0, or T1-S if Step 0 has not written it |
| C12 | Missing | Handle equality | Baker admits `CHandle` equality as `OpaqueReference` (`NumericOperationRecipes.fs:80-84`). ffi L79: "Its only role is to be handed back to another C binding". After a release, address reuse makes equality misleading | [C\*], [S] | Decision D4: Step 3 |
| C13 | Confirmed | CP L61-66: NativeCallbacks reaches source checking without errors and is refused at native witnessing | Source checking finished with 0 errors. cvc5 proved 62 numeric obligations, none of them ABI. The refusal is at Alex. Re-observed live, and reproduced in the unfiltered runner at the pin: two address refusals and one call refusal (`EV/baseline-repair/nc/structural.txt`) | [L], [X] | None |
| C14 | Confirmed | CP L116-122: 649 of 649, 17 of 17, and the driver "executes no C or native Clef program" | Rerun unfiltered at the pin: 649 of 649 and 17 of 17. The driver does no Alex or native work | [X], [C] | None |
| C15 | Missing | Untested refusals | No test covers the non-CDecl import (`BoundaryRecipes.fs:118`), the non-CDecl callback (`CallbackDeclarations.fs:98-99`), `FunctionPointers.fs:58, 67, 68`, the Alex invoke refusal, Alex AX4001 "callback declaration", or Alex ForeignCalls without a row (`Alex:src/Alex/Witnesses/PlatformWitness.fs:28-29`) | [C\*], [C] | Step 0 |

## Review dispositions

Two reviews examined the research plan: the first adversarial review (`EV/research/critique.md`) and the auditor
review the owner forwarded (`EV/t1/AUDITOR-REVIEW-2.md`). The research-session Step 1 plan carries its own
reconciliation table (`EV/t1/T1-PLAN.md` §1), which also covers the review of its first revision. That table
predates three owner decisions recorded in the rulings record and the implementor guide: D6(b), which moves the
callable-aggregate foundation into Phase B; D1(a) with approval (iii); and the direction that compiler and FFI
phases are accepted on component gates alone, with the Bozzetto track independent. The landings below apply those
decisions.

### First review

| ID | Finding | Disposition | Lands in |
|---|---|---|---|
| B1 | The daemon refuses a schema-17 worker at hello (`Bozzetto/Bozzetto/ComposerWorkerClient.fs:146-151`), so a daemon-path gate needs a matched pair and a restart. Bozzetto is missing from the touched repositories and its acceptance from the protocol | Adopted, then reshaped. Compiler and FFI phases are accepted on component gates: unfiltered suites, Composer runners, Farscape conformance. The daemon-worker seam is the Bozzetto track (T1-D) under D1(a): bootstrap once, then replace workers without a daemon restart while the wire contract stays compatible. The isolated pair test in `Bozzetto.Composer.Tests/LiveProviderTests.fs` belongs to that track, with the unfiltered `Trusted` Bozzetto suite. The shared switch is an operational step the owner triggers (D2), reported separately, never through the installed `boz`, and it never blocks a compiler phase. The schema change now arrives in Phase B | Step 1 document, Bozzetto track; guide, component boundaries |
| M1 | Step 3's gate needs Fidelity.Platform edits scheduled for Step 8, and the vocabulary change breaks every descriptor literal | Adopted. An additive resource descriptor, bound by exact declaration identity and carrying no default. `OwnershipTransfer` is never read as a resource contract. Pthread Storage regeneration and the ListenerEntry and IgnoreValues fixtures move into Step 3. Both fixtures are triaged as test defects under default demand, so Phase B rewrites them first [A] | Later tranches, Step 3; Phase B for the rewrites |
| M2 | Descriptor vocabulary changes lack the spec amendments they need (S1, S2) | Adopted for S1. Each vocabulary change carries its `platform-bindings.md` text in its own step: entry `TypeRef` (Step 2), resource descriptor reconciled with `TypeDescriptor.Destructor` and `OwnershipKind` (Step 3), retention (Step 6), `CallConv` (Step 7). T1-S adopts the existing `CallbackDescriptor`, `ClosedCallbackDescriptor` and `StructDescriptor`. The S2 half, the extern declaration form, has no landing yet | Step 1, T1-S; later tranches; S2 under [Unresolved items](#unresolved-items) |
| M3 | No portable carrier exists for a nullable function word | Adopted with the auditor's correction. T1-S corrects the ffi §4.2 note. A boundary-conversion design (T4-D) precedes Step 4. Ordinary `Option<FnPtr>` semantics stay unchanged inside Clef, and boundary uses stay refused until then | Step 1, T1-S; later tranches, T4-D and Step 4 |
| M4 | External execution hypotheses are mapped but never scheduled | Adopted. The `ExecutionHypothesis` row kind ships in T1-A, with producers for every admitted libc import, load-time residence, environment contracts, image loading and runtime startup. Later producers: T1-B, Step 2, Step 3. A `CallbackDescriptor` that governs a Clef-only entry counts as a declared contract, because no foreign invoker exists | Step 1, T1-S (§2.6) and T1-A |
| M5 | T1-A cites ffi L77 but leaves code lifetime to Step 2 | Adopted with the auditor's correction: code lifetime follows the loading contract. Executable images and load-time libraries are admitted. Plugins and JIT output are refused. A shared library alone causes no refusal | Step 1, T1-S (§2.5) and T1-A; host contracts in Step 7 |
| M6 | Admission records and lowering preservation are unscheduled | Adopted with the auditor's correction. Composer owns a realization table keyed by the published C ABI identity, Edges 0, A, B and an ELF stage, receipt evidence, and `Composer:docs/Native_Callable_Admission.md`. Extending `CoverageValidation` is rejected | Step 1, T1-S (§2.8) and T1-A |
| M7 | Invalidation of the new rows is untested | Adopted: G1-inv and G2-inv, including the equal-physical-signature case | Step 1, T1-A |
| M8 | Composer's regression runner is missing from acceptance | Adopted. The full runner is a common gate, run with `--timeout 360` and compared with the post-repair pin | Phase B pin; Step 1 common gates |
| M9 | The Step 2 `atexit` gate contradicts the arity rule | Adopted. The MAP rules admit exactly one justified erasure: a sole `unit` formal through a Baker adapter, or a sole `unit` actual at an invocation. The gate stands, with a regenerated Libc Process (stale raw-pointer generation today) | Step 1, T1-S (§2.4); Step 2 |
| M10 | Q1(a) needs a spec amendment | Adopted through ruling R1, which requires the amendment | Step 0, or T1-S (§2.3) if Step 0 has not written it |
| M11 | T1-B leaves the spec's `fromSymbol` examples non-conforming | Superseded by ruling R2. T1-B's spec change covers ffi §3.2, §3.3, §5.3, §6, §7 and L16. The CCS8092 typing question becomes moot | Step 1, T1-B (§2.9) |
| M12 | Q4(a) would refuse every CPU record that holds an `FnPtr` | Adopted with the auditor's correction: two forms. Interior callable components are part of the Phase B callable-aggregate foundation (D6(b)), one protocol for `FnPtr` record fields, ordinary function fields and callable union payloads. Native tables are specified in T1-S and implemented in Step 6. Premise correction: `ValueRepresentations.fs:123` refuses `TFun` values [C\*], so no working function-field path existed to follow, and the FunctionFields case fails at the pin on a lifetime refusal at `FunctionFields.clef:7-8` [X] | Phase B foundation; Step 1, T1-S (§2.2); Step 6 |
| M13 | Desktop binding repositories consume the vocabulary Steps 2 and 3 change | Adopted. Inventory done (§4.4). Decision D3(a): Fidelity.Gtk3, Fidelity.GObject and Fidelity.WebKit are frozen as legacy, and maintained bindings are regenerated into Fidelity.Platform | Later tranches, Steps 7 and 8 |
| minor 1 | Wrong citation for R31 | Adopted | This map, R31 |
| minor 2 | Weak citation for R3 | Adopted | This map, R3; Step 1, T1-A (G1-inv) |
| minor 3 | "A `.clefi` cannot carry nullability" overstates | Adopted as wording. A type surface lacks per-level nullability and its provenance | No work |
| minor 4 | "The only open interface case" overstates | Adopted as wording. Baker-demanded scopes and BAREWire inter-section contracts are open, interface-adjacent cases | Q11, an open owner decision in the tracker |
| minor 5 | The direct scalar path has no source-through-native test | Adopted: runner case `ScalarImportCall`, and Edge B's first recheck of the import path | Step 1, T1-A; this map, R4 |
| minor 6 | An unknown-pilot-key diagnostic would break the conformance pilot | Adopted: warn first, or change the pilots and docs with it | Step 0 |
| minor 7 | The `sourceAdmitted` gate site is wrong | Adopted. The gate goes at `NativeService.fs:1420`, with `FunctionPointers.settle` and `PlatformDeclaration.check` moved before `WitnessEmission.prepare` | Step 1, T1-A (clef) |
| minor 8 | CCS8010 is overloaded | Adopted | Step 0 code allocation |
| minor 9 | No agent edits the owner's three Farscape files | Done. The notes workflow finished them at the owner's request. They are uncommitted, Phase 0 audits them, and the owner commits | Phase 0 |
| minor 10 | The dependency graph omits Step 6 before Step 5 | Adopted | Later tranches, dependency graph |
| minor 11 | The build order omits BAREWire and Bozzetto, and the BAREWire harness is missing | Adopted. BAREWire and its harness join the build order. Bozzetto's suite applies on the Bozzetto track and to any phase that edits Bozzetto | Step 1 common gates |
| minor 12 | Alex's repository test rules are not reflected | Adopted. Alex tests are contract-row fixtures. A refusal test asserts the text, that no operation is emitted and that no operand is bound. Tests compiled from source live in Composer | Step 1, T1-A tests |
| minor 13 | No gate covers `Option` marshalling at `FnPtr.invoke` | Adopted as scheduling. T1-A refuses `Option` operands and results at `invoke` with a located code, on the basis of T1-S's §3.3 edit. Positive gates follow | Step 1, T1-A; Steps 3 and 4 |
| minor 14 | Static-libc applicability is unassigned | Moved, with Q13 and S9 | Step 7 |
| minor 15 | `FuncOp.NativeEntryDecl` duplicates PSG rows in Alex output | Adopted: dropped. Composer reads the rows from the revision it holds | Step 1, T1-A |
| minor 16 | `sig` keyword precision; R57, R75, R77, R80, R82 have no step | Adopted. R57: Step 6. R75: a T1-B test. R77: refused by new §3.7 item 5. R80: outside the FFI correction. R82: T1-A oracles and the realization check. `sig`: Q11 | As listed |
| minor 17 | No pinned per-case runner baseline | Resolved by the pin: 0 of 29, every case at compile [X]. The runner gains expected exits and expected refusals. The post-repair pin is the reference | Phase B pin; Step 1 |
| Not defects: seams | "No seam violations found" | Qualified by the auditor: the statement describes the intended division of responsibilities and does not establish that the representations and gates work | Step 1 revisions |
| Not defects: Q9 | "FsCheck appears nowhere" | Corrected. Bozzetto uses Expecto with FsCheck (`Bozzetto/AGENTS.md:91, :122`; `Bozzetto.Tests/Bozzetto.Tests.fsproj:542-544`) [C\*], and its acceptance needs a `Trusted` verdict | Guide, working rules |
| Not defects: C9, C11-C13 | Confirmed | See the corrections table | None |

### Auditor review

The labels AU-1 to AU-13 are those the research-session Step 1 plan assigned (`EV/t1/T1-PLAN.md` §1.2).

| ID | Point | Disposition | Lands in |
|---|---|---|---|
| AU-1 | The critique's main findings are sound, and the plan needs revision before Step 1 | Adopted. The Step 1 plan was revised twice in the research session. D6(b) re-sequences it again | Baseline document; Step 1 document |
| AU-2 | B1 is real. Build and validate a matching daemon-worker snapshot, keep the compatibility checks, avoid the stale `boz` | Adopted and reshaped by D1(a). The daemon drops its unneeded dependency on the PSG contract and keeps exact checks on the wire contract it interprets. Worker-loaded PSG identity and distribution bytes stay checked against the selected manifest. Approval is the owner's explicit go-ahead tied to the reviewed manifest, with an audit record of the selection and the permitted rollback (D1(iii)). No file-based approval is presented as owner-only, because agent sessions here have unrestricted filesystem access. A worker replacement ends compiler sessions, so the shared project list is preserved and fresh sessions reopened | Step 1 document, Bozzetto track |
| AU-3 | Use the isolated daemon harness rather than "component runners only". Report isolated and shared acceptance separately | Adopted. The isolated pair test belongs to the Bozzetto track. Shared-dashboard acceptance is reported separately and needs the owner's go-ahead (D2) | Bozzetto track |
| AU-4 | Two record forms: interior callable components, and native tables with declared layout, function-address fields and retention. Baker establishes the projection | Adopted. Interior components: Phase B foundation. Native tables: T1-S text, Step 6 implementation | Phase B; Step 1, T1-S; Step 6 |
| AU-5 | Step 1 obligations from the first native call: code lifetime and hypotheses, complete mappings with justified `unit` erasure, invalidation, source-through-native tests, preservation evidence | Adopted, each as an acceptance requirement with named evidence | Step 1, T1-A acceptance |
| AU-6 | `CoverageValidation` cannot check ABI preservation. The target pathway compares the lowered operation with the target realization of Baker's contract | Adopted | Step 1, T1-S (§2.8) and T1-A (Composer) |
| AU-7 | `Option<FnPtr>` needs a real boundary design; §4.2 conflates native-word comparison with function-value rules. Refuse until it exists | Adopted | Step 1, T1-S (§2.7); T4-D |
| AU-8 | Keep Q1 as an explicit spec amendment | Adopted (ruling R1) | Step 0 or T1-S (§2.3) |
| AU-9 | Replace the `fromSymbol` tranche with `FnPtr.ofExtern` | Adopted (ruling R2) | Step 1, T1-B |
| AU-10 | Schedule vocabulary changes with their Fidelity.Platform fixtures and generated consumers. An additive resource descriptor keeps exact declaration identity and no `Borrowed` default | Adopted | Later tranches, Steps 2, 3, 6, 7, 8 |
| AU-11 | Code lifetime follows the actual loading contract | Adopted | Step 1, T1-S (§2.5) |
| AU-12 | Correct "FsCheck appears nowhere" | Adopted | Guide, working rules |
| AU-13 | Reconcile, specify the two record forms, and switch the shared dashboard only after the matched snapshot and its evidence | Adopted. T1-S specifies both forms. The shared switch is last, owner-triggered and outside compiler acceptance | Step 1, T1-S; Bozzetto track |

## Spec amendments

### Scheduled amendments

"Drafted" means paste-ready text exists in `EV/t1/T1-PLAN.md` or the designs it cites (`EV/t1/records.md`,
`EV/t1/obligations.md`). The owner-forwarded D6(b) review moves the interior
callable-component prelude into Phase B before its schema batch. Rows below
distinguish that normative text from implementation and from remaining drafts.
This supersedes historical T1-S placement of the interior-record, callable
payload, NTU function-value and interior/native optional-entry distinction in
the review tables above; native table and NULL-carrier implementation schedules
are unchanged.

| Amendment | Spec location | Basis | Carried by | Text |
|---|---|---|---|---|
| `NTUfnptr` is a portable function value, not `index` | `ntu-types.md` §2.3 and §8; `native-type-universe.md` §3.2 | Ruling R4; S26; C11 | Phase B spec prelude | Written in the spec; compiler realization still requires the foundation and Step 1 |
| Code allocation: `FnPtr` misuse, `fromSymbol` retirement, §5.6 commitment refusals; tabling CCS8402-8405 and CCS8500; CCS8096 and CCS8010 overloading | `error-handling.md` | S3, S4; critique minor 8 | Step 0. Step 1 slots in T1-S (§2.10) | Slots listed, numbers unallocated |
| Callable aggregate and ordinary-demand commitment codes CCS8410–CCS8415 | `error-handling.md`, callable aggregate diagnostic subsection | Owner-forwarded D6(b) review | Phase B spec prelude | Allocated; compiler producers and paired tests belong to the schema batch |
| Compiler-owned entry contracts | ffi §3.4 after L180 | Ruling R1; M10; AU-8 | Step 0, or T1-S | Drafted (§2.3) |
| `FnPtr.ofExtern`; `fromSymbol` retired | ffi §3.2 (L117-140), §3.3, §5.3 L287, §6.1-6.2 examples (L349-394), §7 item 3, L16 | Ruling R2; M11; AU-9 | Step 1, T1-B | Drafted (§2.9) |
| Contracts carry more than the ABI | ffi §5.6 | Ruling R3 | T1-S, through §3.6 item 2 | Drafted (§2.2) |
| `Transfer.Undeclared`; no ownership default | `platform-bindings.md:285-291` (ffi L327 is already normative) | Ruling R5 | Step 0, or T1-S | Not drafted |
| No comment-narrowing; evidence with provenance | ffi §5.2, §5.4, §5.5 | Ruling R6; legacy-C principle | Step 0, or T1-S | Not drafted |
| Interior logical records holding `FnPtr`; callable components | ffi §3.6; `closure-representation.md` §2.4, requirements 6, 13, 14; `discriminated-union-representation.md` §9.1 and requirement 12; backend §3.2, §4.2, §7 | M12; AU-4; AU-13; owner-forwarded D6(b) review | Phase B text first, then common schema foundation | Written in the spec with full row dependency accounting; positive/negative matrix in the design; implementation pending |
| Native tables | ffi new §5.7, L281, §7 items 10-11; `platform-bindings.md` adoption of `CallbackDescriptor`, `ClosedCallbackDescriptor`, `StructDescriptor`; `memory-regions.md` after L191 | AU-4; S1 in part | T1-S text; implementation in Step 6 | Drafted (§2.2) |
| Entry and invocation mapping; unit erasure | ffi §3.4, with a §4.3 cross-reference | M9; AU-5 | T1-S | Drafted (§2.4) |
| Code lifetime from the loading contract | ffi §2.1 L77; new §3.7 | M5; AU-11 | T1-S. Host contracts (L3 escapes, L4): Step 7 | Drafted (§2.5) |
| External execution hypotheses and their definition | ffi §5.6 after L337; `terms-and-definitions.md` | M4; S14 terminology | T1-S | Drafted (§2.6) |
| `Option<FnPtr>` interior/native distinction; marshalling at `invoke` | ffi §3.5, §3.6, §4.2; §3.3 | M3; AU-7; minor 13; D6(b) review | Phase B distinction; remaining invocation mapping in T1-S | Interior union protocol and function-value/null distinction written; native carrier still T4-D and Step 4 |
| `Option<FnPtr>` boundary conversion as a pathway commitment | ffi §4; bla §2.1.1 | M3; AU-7 | T4-D, before Step 4 | Design questions open (§4.5) |
| Pathway realization recheck | bla §2.1.1 after L65 | M6; AU-6 | T1-S | Drafted (§2.8) |
| Carried-property index rows | `program-semantic-graph.md` §14.3.7 (L630-642) | S12 | T1-S | Drafted (§2.8) |
| Entry `TypeRef`, `Repr.Entry`, `CAbiDescriptor` to `AbiProfile` | `platform-bindings.md` | M2; Q5b | Step 2 | Not drafted |
| Additive resource descriptor, reconciled with `TypeDescriptor.Destructor` and `OwnershipKind` (`platform-bindings.md:269-281`) | `platform-bindings.md` | M1; M2; AU-10 | Step 3 | Constraints only (§4.3) |
| Option carrier against option-operations | `option-operations-representation.md` L409; `native-type-mappings.md` L473-488; `native-type-universe.md` L653-676 | S5 | Step 3 | Not drafted |
| Data-handle pathway commitment at the extern boundary | bla §3.2 (L71-75); `native-type-mappings.md` MLIR table | S11 | Step 3 | Not drafted |
| `RetainedReferenceDescriptor` and retention vocabulary | `platform-bindings.md`; ffi §5.7 | M2; records design A.7 | Step 6 | Drafted in the records design |
| Per-target `CallConv`; library link and load identity; C linked without libc; static libc | `platform-bindings.md`; ffi §3.7 L4; ffi L8-12 | M2; S9; Q13; minor 14 | Step 7 | Not drafted |
| Complete C ABI facts: data models, variadics, struct by value, bitfields, `_Bool`, `char` signedness, enum width, `errno` and TLS, `long double`, unwinding, callback thread context | ffi L287 | S23. D5 defers integer promotion | Step 7 | Not drafted |
| Site and design-notes drift D1-D8 | clef-lang-site pages (`EV/research/spec-contract.md` §4) | Spec lens | Step 8 | Not drafted |

### Unscheduled spec items

No plan carries these yet. The candidate step is a proposal [I], for the owner and the spec agent to confirm.

| Item | Spec location | Note | Candidate step |
|---|---|---|---|
| S2, the extern declaration form | `platform-bindings.md` L195-198, L340-352; `lexical-analysis.md` L155 | The implementation uses `[<FidelityExtern(library, symbol)>]` with an `Unchecked.defaultof` placeholder body, which `platform-bindings.md` L340-352 calls "WRONG" and CCS8083 rejects. Critique M2 asked for it, and only the S1 half was adopted | Step 0 or T1-S, because `ofExtern` requires "a compiler-recognized extern declaration" |
| S21, `NativeDefault.zeroed`, and R26's closed-adapter mechanism | `error-handling.md` L356, L496 | The adapter rule cites an "exact zeroed placeholder" the spec never defines | With S2 |
| S6, S7, S17, S18, S20, width spellings and the width source | `ffi-boundary.md` L109, L111, L134, L386; `memory-regions.md` L120, L161; `platform-bindings.md` L43-61, L399; bla L170-198 | Three incompatible rules for width-named types, and two for `word_size` | Unassigned |
| S8, closure casts | `closure-representation.md` L298 | Contradicts L180 and bla L112 | T1-S, which already edits these chapters |
| S13, name-based recognition | `platform-bindings.md` L206, L231-262 | `ofExtern` and the resource descriptor's identity rule answer it for their own surfaces | Step 3 or Step 7 |
| S14, residual foreign undefined behavior (R66) | `behavior-classification.md` L47, L61 | T1-S closes the terminology half | Unassigned |
| S15, captured callbacks | ffi L319, L427 against L335, L417 | Contradictory instructions | Step 5 |
| S16, examples without nonnull evidence | ffi L353 (`gtk_window_new`), §3.3 L153-154 | T1-B rewrites §6.1 and §6.2. The rewrite must follow ruling R6 for unannotated GTK returns | Step 1, T1-B |
| S19, BAREWire link drift | `intrinsics-cryptography-bits.md` L35, L212, L362, L380; `native-type-universe.md` L728 | Links point at a chapter that never mentions BAREWire | Unassigned |
| S22, `typar : null` | `types-and-type-constraints.md` L317 against `error-handling.md` L376 | CCS8010 against CCS8710 | Step 0, with code allocation |
| S24, NULL confinement in artifacts | None exists | The JavaScript analog is `javascript-boundary.md` L214-224 | Steps 3 and 4 |
| S25, intrinsic argument types | `platform-bindings.md` L373 | Omits `FnPtr` and optional handles | Unassigned |

### Open design items

These come from two Rust references the owner raised on October 3: the "Beyond the &" comparison and Seacord's
"Unsafe Rust" talk (RustConf 2026). The later-tranche document and the tracker carry them.

| Item | Current spec state | Step |
|---|---|---|
| Address stability for memory registered with foreign code, such as a `wl_listener` that must not relocate while registered | No obligation exists. The only relocation text concerns closure transfer (`closure-representation.md:139`) [C\*] | Step 1 native-table retention text (§5.7 item 4), then Steps 3 and 5 |
| Foreign aliasing: state who else may touch foreign memory (C threads, re-entrant callbacks) as a graph obligation, beside the access capability | Access kinds express capability only: read-only, write-only or read-write | Steps 3 and 6 |
| Output cells against "no value is uninitialised" (`error-handling.md:496`) [C\*] | The cell stays inside the binding adapter. The value is admitted after the call under its success convention, as a recorded hypothesis | Step 6 |
| Matching on foreign structures: a "same place, not mutated between reads" hypothesis, or a snapshot read | None | Unassigned; Step 6 is the likely home [I] |
| Operation checklist for Baker's native-table projection: read, write, project, borrow with access, pointer hop, discriminant read, wrapper preservation, each supported or refused with a code | None | Step 6 [I] |
| `FnPtr` identity under multiple instantiation and deduplication | D4 refuses `FnPtr` equality. If ever admitted, it is defined by declared-entry identity, never by address | Step 1 refusal; `CHandle` equality in Step 3 |
| Target caveats: byte stores that are not atomic on some MCUs, 128-bit integer availability, relaxed atomics, ABI discrepancies | None. Each is a target fact that admission checks | Step 7 |

## Unresolved items

Interior `Option<FnPtr>` is resolved by the owner-forwarded D6(b) review: it
belongs to Phase B's union-payload protocol. Only the C-boundary absence carrier remains
in Step 4; it is not a prerequisite for interior construction or matching.

| Item | State | How it resolves |
|---|---|---|
| The extern declaration form (S2) | No plan carries it | Owner or spec agent assigns a step; T1-B depends on it |
| A second Fidelity.PSG schema change in Step 1 | Owner direction: none unless the foundation's schema cannot carry the native rows | The Phase B schema design shows whether it can |
| Whether any path admits `FnPtr` equality today (U2) | UNKNOWN | A probe or code read, which decides whether `[NATIVE-ENTRY-EQUALITY]` has a producer |
| `fromSymbol<'F>` and CCS8092 | UNKNOWN; moot once T1-B retires `fromSymbol` | None needed after T1-B |
| R22, R57, R65 (invalidation), R67 (SAL, GCC), R75, R77, R80-R83 | UNKNOWN | The landing column of each row |
| Composer ForeignReferences, PointerCells, ForeignScalarArrays and about 25 further Composer entry points | Not run in the baseline | The Phase B pin runs them or records a reason for each exclusion |
| Farscape `tests/native-callbacks/run.py` | Not run before edits. No pre-edit result is obtainable | Run after Step 0 and pin |
| Triage verdicts (test defect, compiler defect, unimplemented) | One agent's report each [A] | Phase 0 audit, against spec text for every test-defect verdict |
