# FFI correction: later steps 2 to 8

October 3, 2026. This record is the plan for Steps 2 to 8 of the FFI boundary correction, which the research
sources call T2 to T8. Every item in it is plan. None of these steps has started, no repository has changed for
them, and every gate named here is unrun. The [implementor guide](FFI_Correction_Implementor_Guide_2026-10-03.md) places
these steps in the full sequence, the [tracker](FFI_Correction_Tracker_2026-10-03.md) carries their live checklist
and sign-offs, and the [rulings record](FFI_Correction_Rulings_2026-10-03.md) holds the owner's rulings and
principles that govern them. Where this record and the
[Step 1 plan](FFI_Correction_Step1_Plan_2026-10-03.md) disagree about what Step 1 delivers, the Step 1 plan wins.

The step sections follow the corrected execution order: 2, 3, 4, 6, native tables (6-T), 5, 7 and 8.

## Conventions and evidence

Raw evidence lives under `/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/`, written here as
`evidence:`. It is local and non-portable. The main sources are:

| Source | Path |
|---|---|
| Gap map and first tranche plan | `evidence:research/gap-map.md` (§3.1 graph, §3.3 tranches, §4 questions) |
| Adversarial review of the gap map | `evidence:research/critique.md` (M1, M2, M3, M9, M13, minors 10, 13, 14, 17) |
| Auditor review, forwarded by the owner | `evidence:t1/AUDITOR-REVIEW-2.md` |
| Step 1 plan, revised | `evidence:t1/T1-PLAN.md` (§4 schedules the later steps, §5 lists decisions D1 to D6) |
| Step 1 designs | `evidence:t1/records.md`, `evidence:t1/obligations.md` |
| Spec issue list S1 to S26 | `evidence:research/spec-contract.md` §3 |
| Baseline triage | `evidence:baseline-repair/triage-composer.md`, `triage-ccs8403.md`, `triage-ccs8011.md` |

Code citations take the form `Repo:path:line` at the heads below. `ffi Lnn` is line nn of
[`clef-lang-spec/spec/ffi-boundary.md`](../../clef-lang-spec/spec/ffi-boundary.md). Every load-bearing claim carries
a grade:

| Grade | How the claim is known |
|---|---|
| **[X]** | Executed in the unfiltered baseline run of October 3 |
| **[C\*]** | Read in code at the pinned head while this record was written |
| **[C]** | Read at the pinned head by a design or lens agent and cited in its report, not re-read here |
| **[S]** | Spec text at clef-lang-spec `232482b` |
| **[H]** | Installed system header on the owner's machine (glibc 2.44, wayland 1.26.0), read while writing |
| **[P]** | Scratch MLIR probe by a design agent with `mlir-opt` 22.1.8 (`evidence:t1/mlircheck/`) |
| **[A]** | One agent's report |
| **[D]** | Documentation prose |
| **[I]** | Inferred and unchecked, including general C platform knowledge |

**Pinned heads.** Rechecked read-only while this record was written. Every head below matched the research pin
except Bozzetto, which moved from `d2dc5c8f` to `1f716471` with the owner's documentation commit. Farscape carries
the owner's three uncommitted notes files, and Bozzetto carries the modified v1 guide.

| Repository | Head | Repository | Head |
|---|---|---|---|
| clef-lang-spec | `232482b` | Farscape | `5ec1954` |
| BAREWire | `571ff31` | Fidelity.Platform | `d7cc3b7` |
| Fidelity.PSG | `f153c75` | Fidelity.Gtk3 | `5cc6d1d` |
| clef | `a6614b5` | Fidelity.GObject | `541f580` |
| Alex | `4e1f859` | Fidelity.WebKit | `b274f13` |
| Composer | `fcd68d6` | Calque | `d0f6143` |
| Bozzetto | `1f716471` | | |

## Entry conditions

Steps 2 to 8 start from the state the earlier phases leave. Each item is a precondition the implementer re-verifies
at entry. None of them holds today.

- **Phase B, the clean baseline.** Every suite in the guide's current-state table passes unfiltered, and the result
  is pinned. Under the owner's decision D6(b), Phase B also delivers the callable-aggregate foundation: one protocol
  with code, environment and lifetime evidence for `FnPtr` record fields, ordinary function fields and callable union
  payloads, with the Fidelity.PSG schema change that carries it (rulings record, "Owner decisions on the Step 1
  plan").
- **Step 0.** Diagnostic codes are allocated in `error-handling.md` and used. BAREWire `Transfer` has the case
  `Undeclared`, the `Function.cdecl` default no longer invents `Borrowed`, and Farscape no longer emits `Borrowed`
  for undeclared ownership (rulings R5). Farscape refuses an unannotated or `_Null_unspecified` callback slot before
  writing output unless explicit evidence narrows it (rulings R6). Explicit `Borrowed`, `CallerOwns` and `CalleeOwns`
  literals in other repositories stay as they are (`evidence:t0/T0-BRIEF.md`, "Fixed vocabulary").
- **Step 1 and Step 1-B.** The spec prelude has landed (`evidence:t1/T1-PLAN.md` §2):
  - ffi §3.6, records holding `FnPtr`;
  - ffi §3.7, code lifetime by loading contract;
  - the ffi §5.6 paragraph on external execution hypotheses;
  - the corrected ffi §4.2 note on `Option<FnPtr>`;
  - the entry and invocation mapping with unit erasure, and compiler-owned contracts in ffi §3.4;
  - ffi §5.7, native tables, as refusal text only.

  Native entry, address and indirect-call rows exist. Composer's LLVM pathway runs the realization check (Edge 0,
  Edge A, Edge B and the ELF stage). `FnPtr.ofExtern` has replaced `fromSymbol`.
- **Hypothesis kinds without producers.** Step 1 ships the `ExecutionHypothesis` row kinds
  `CallbackInvocationContract`, `DeclaredNonnull` and `DeclaredLibraryPromise` with no producer, and integrity rule
  H7 treats any `CallbackInvocationContract` row in Step 1 as a defect (`evidence:t1/obligations.md` §3.2, §3.4 [A]).
  Steps 2 and 3 add the first producers.
- **The daemon is off the critical path.** The owner directed that compiler and FFI phases are accepted on component
  gates only. The Bozzetto daemon and worker track (T1-D) is independent, and a switch of the shared daemon is an
  operational step the owner triggers and the implementer reports separately. No step below waits for it.

## Dependency graph

```text
Phase B (clean baseline + callable-aggregate foundation, PSG schema change)
  └─> Step 0 ─> Step 1 (T1-S, T1-A) ─> Step 1-B (FnPtr.ofExtern)
                  │
                  ├─> Step 2   C-invoked closed entries
                  ├─> Step 3   handles, Option<CHandle>, resource contracts, release, retirement
                  │     ├─> Step 4-D  Option<FnPtr> boundary-conversion design (spec ruling) ─> Step 4
                  │     └─> Step 6    spatial admission, record references, output cells
                  │
 Steps 2 + 3 + 4 + 6 ─> Step 6-T  native table projection (ffi §5.7)
 Steps 2 + 3 + 4 + 6 ─> Step 5    captured callback environments
 Steps 1 to 6        ─> Step 7    link/load beyond libc, loading contracts L3 and L4, target vocabulary
 Steps 3 to 7        ─> Step 8    Fidelity.Platform regeneration and per-library acceptance

 Bozzetto T1-D (daemon and worker seam): independent track, never on this graph
```

Changes against the gap map's graph (`evidence:research/gap-map.md` §3.1):

1. Step 6 precedes Step 5. Step 5's gates need Step 6's output cells, which the gap map's own Step 5 text says but
   its graph omitted (critique minor 10; `evidence:t1/T1-PLAN.md` §4.1).
2. Step 4 splits into a spec design step, 4-D, and the implementation (critique M3; auditor review).
3. Native table projection is its own step, 6-T, after Steps 2, 3, 4 and 6 (`evidence:t1/records.md` §4,
   "Deferred").
4. The Wayland listener gate needs Step 6-T and Step 7, because `wl_proxy_add_listener` lives in `wayland-client`
   and Baker admits only library `c` (`clef:src/Compiler/Baker/Recipes/BoundaryRecipes.fs:432` [C\*];
   `evidence:t1/T1-PLAN.md` §1.3 DC-12).
5. Step 1-B is no prerequisite here. The ffi §6 examples it rewrites use the absence and resource conversions of
   Steps 3 and 4, so they become acceptance examples only after those steps.
6. Phase B owns the callable-aggregate foundation and its schema change (D6(b)). Steps 2 to 6 each add row kinds,
   so each is a further PSG contract change.

Steps 2 and 3 both depend only on Step 1. Both extend `NativeCarrier`, `BoundaryRecipes.fs` and the Fidelity.PSG
schema, so running them concurrently would contend for the same files and schema number. The recommended order is
Step 2 first, as the smaller change, then Step 3. A single schema change covering both is an acceptable alternative
if the implementer records it in the tracker [I].

## Common acceptance

Every step closes on component gates. Each gate is unfiltered and run under Bozzetto leases
(`acquire_full_build_lease` for builds, `acquire_test_suite_lease` for suites, `release_work_lease` after each).
The exact command for each suite is the one recorded in the
[baseline record](FFI_Correction_Baseline_2026-10-03.md). Each gate runs entirely inside a held lease. The baseline
driver reacquired leases only between suites, and two suites ran past their lease's expiry (Phase 0 audit P0-12,
`Bozzetto:docs/FFI_Correction_Phase0_Audit_2026-10-03.md`, an in-progress working-tree record [A]).

1. **Suites.** Build in the order BAREWire, Fidelity.PSG, clef, Alex, Composer, Farscape, Bozzetto
   (`evidence:t1/T1-PLAN.md` §0.5). Then run, unfiltered, the suites of every touched repository:
   - the BAREWire harness;
   - Fidelity.PSG, `Clef.Compiler.Service.Tests` and Alex;
   - Composer `tests/Alex.Tests` and every Composer entry point in the post-repair pin;
   - the NativeCallbacks runner with no case names;
   - the regression runner with `--timeout 360` and no `--sample`;
   - Farscape's tests and BoundaryConformance, with no gate names;
   - the Bozzetto default tier when Bozzetto is touched, which must print `TRUST … verdict=Trusted`.
2. **Pins.** The failing set of each suite is a subset of the post-repair pin. Every new test name appears in the
   unfiltered output. A gate reachable only by a name filter is never added.
3. **Tests in each repository's framework.** xUnit in clef, Alex, Fidelity.PSG, Composer and Farscape. NUnit in
   Calque and Fidelity.FSharp.Incremental. BAREWire's own harness. Expecto with FsCheck in Bozzetto. An Alex refusal
   test asserts the refusal text, no emitted operation and no bound operand, and a test compiled from Clef source
   belongs in Composer (`Alex/AGENTS.md`, Tests).
4. **PSG contract changes** follow the gap map's procedure (`evidence:research/gap-map.md` §3.2):
   - bump `Revision.Schema`;
   - use typed `Participant` lists, never a new `Set<NodeId>` participant;
   - regenerate `IntegrityNamed.fs`, `BinaryGenerated.fs` and `JsonGenerated.fs`, each with its drift test;
   - run `tools/PSGContract/GenerateMappers.fsx --check` in clef;
   - pass the `build/Contract.targets` gates PSG001 to PSG003.
5. **Obligations inherited from Step 1.** Every new form gets the evidence Step 1 established for native entries
   (auditor review; `evidence:t1/T1-PLAN.md` §3.A.8):
   - the pathway realization check extended to the new form, comparing the lowered operation with the target
     realization of Baker's contract (never a portable-versus-native text comparison);
   - an external execution hypothesis, with origin and provenance, for every new reliance on foreign code;
   - an invalidation test: changing the descriptor or target fact a row depends on invalidates the row and its
     evidence;
   - a source-through-native case paired with a contract-row test;
   - located refusals whose codes are allocated in the spec before code uses them.
6. **Descriptor vocabulary rule.** A step that changes the descriptor vocabulary, or how the compiler reads it,
   carries all of its consumers in the same step (see Descriptor-vocabulary migration).
7. **The ffi L16 implementation checkpoint** changes only at a step's acceptance, so the spec never claims
   unimplemented behavior (`evidence:t1/T1-PLAN.md` §2).
8. **Evidence record.** Each step files the guide's evidence record in the tracker: heads and working-tree state,
   commands and lease ids, totals against the pin, `TRUST` lines, new test names, graded claims and residual risks.

A daemon-path observation (`composer_run_current` on a matching worker) is reported as its own record when the owner
has switched the shared daemon. It never blocks a step.

## Step 2: C-invoked closed entries (T2)

**Goal.** An extern parameter may carry a nonnull `FnPtr<'F>` whose contract a `CallbackDescriptor` declares, and C
invokes the closed Clef entry through it. The narrowest native gate is libc `atexit` with a handler that ends the
process through `_exit` (`evidence:research/gap-map.md` §3.3 T2).

**Depends on.** Step 1: native entry rows, the mapping rules with the single justified unit erasure
(`evidence:t1/obligations.md` §5.2 and §5.3, the MAP rules of the Step 1 plan), the loading contract and the
realization check. Critique M9 (the `void (*)(void)` handler against an arity rule) is resolved there: a sole `unit`
formal maps to an empty native parameter list, realized only through a Baker adapter.

**Spec first.**

- `platform-bindings.md` Quotation Structure Requirements (`:269-291` [S]): an entry `TypeRef` case that names its
  `CallbackDescriptor`. A new case keeps code and data pointers distinct, which `Named` would blur
  (`evidence:research/gap-map.md` §4 Q5). The same amendment adds `Repr.Entry` for code-address fields and has the
  platform's `CAbiDescriptor` name its `AbiProfile` (`evidence:t1/T1-PLAN.md` §4.2; `evidence:t1/records.md` §3.7).
- ffi §3.7: an entry that reaches foreign code from a compiler-produced shared object escapes its load unit, and
  stays refused until Step 7 supplies a declared host loading contract.
- The Step 1 hypothesis paragraph in ffi §5.6 already names "a foreign invoker calls an entry only under the
  entry's declared contract". No new text is needed if Step 1 landed it.
- The activation of an entry that foreign control invokes, and the operands it demands (design item (h)).

**Work by repository.**

| Repository | Work |
|---|---|
| BAREWire | Add the entry `TypeRef` case beside `Integer`, `Float`, `Pointer`, `Bool`, `Void` and `Named` (`src/Descriptors/Bindings.fs:26-32` [C\*]). Add `Repr.Entry` to the representation set, which today ends at `Pointer` (`src/Hardware/Descriptors.fs:90-98` [C\*]). Add a code-pointer size to `AbiProfile`, which carries only data-pointer and scalar alignment facts (`src/Hardware/Abi.fs:8-15` [C\*]). Harness tests for each. |
| Farscape | Emit the entry `TypeRef` instead of `Pointer 64` for function-pointer parameters (gap-map T2 [A]). Narrow a slot to `FnPtr` only on header or pilot evidence. glibc declares `atexit (void (*__func) (void)) __THROW __nonnull ((1))` (`/usr/include/stdlib.h:756` [H]). The generator's nonnull set (`src/Farscape.Core/FidelityCodeGenerator.fs:490` [C\*]) is the path such evidence must reach. Whether the GCC `nonnull` attribute form reaches it is unknown (gap-map R67 [A]), and a generator test settles it. |
| clef | `PlatformResolution.readTypeRef` (`src/Compiler/PSGSaturation/SemanticGraph/PlatformResolution.fs:968` [C\*]) reads the new case. `BoundaryRecipes` admits an entry carrier only when the actual's settled contract is the declared callback contract. Today any non-scalar `TypeRef` is refused there (`BoundaryRecipes.fs:74-80` [C\*]). Contract identity is compared, never the machine signature (rulings R3). Baker produces the first `CallbackInvocationContract(entry, invoker)` hypotheses (`evidence:t1/obligations.md` §3.3 [A]). Narrow carriers (`i8`, `i16`, `bool`) in C-invoked entries stay refused with a located code (owner decision D5). |
| Fidelity.PSG | `NativeCarrier.Entry of signature`, with an integrity rule that the operand's entry contract equals the declared one. H7 becomes satisfiable, because an import operand now carries the entry. Schema change per Common acceptance item 4. |
| Alex | `pBoundaryCall` (`src/Alex/Patterns/PlatformPatterns.fs:112` [C\*]) accepts a function-typed operand read from the row. Alex builds no adapter. |
| Composer | Admission of function-typed extern parameters on SysV AMD64 beside the scalar rule (`src/BackEnd/LLVM/BoundaryAdmission.fs:13-35` [C\*]). The realization check covers the address operand of an extern call. The receipt lists `CallbackInvocationContract` in the hypotheses category. |
| Fidelity.Platform | Regenerate `Libc/Process` into a review directory under the checkpoint procedure (`Farscape:docs/handoff/2026-10-03-ffi-boundary-checkpoint.md:89-95` [C\*]). The package is a stale raw-pointer generation without descriptors, for example `let atexit (func: nativeint) : int` (`Environments/Linux/x86_64/Bindings/Libc/Process/Process.clef:102` [C\*]). |

**Tests.**

- clef: an entry argument publishes the declared signature. A missing or mismatched descriptor is refused. Two
  contracts with equal machine signatures from different declarations are refused at the meeting point. An entry
  passed to foreign code from library output is refused as an escape. A `unit -> unit` handler maps to
  `void (*)(void)` only through a Baker adapter.
- Fidelity.PSG: positive and negative integrity cases for `NativeCarrier.Entry`, and H7 with an invoker present.
- Composer `tests/Alex.Tests/BoundaryAbiTests.fs`: "C calls a Clef entry through a function pointer", as a
  contract-row test with a clang-compiled harness `int call_twice(int (*f)(int), int x)` (gap-map T2 [A]).
- NativeCallbacks case `ExitEntry`. `main` returns a nonzero status and the handler calls `_exit 0`, so exit status
  0 shows the handler ran and the runner's exit-0 requirement suffices (`Composer:tests/Infrastructure/Process.fs:23-26`,
  critique minor 17 [C]). The alternative is runner support for an expected nonzero status. Console output links
  `crt1.o` and enters at `_start` (`Composer:src/BackEnd/LLVM/Codegen.fs:77, 108` [C\*]), so a normal return from
  `main` reaches libc `exit` and its handlers [I].
- Farscape: `atexit`'s slot generates as `FnPtr` from the header attribute, with that provenance in the descriptor
  comment. An unannotated slot is still refused before output.
- BoundaryConformance: a positive publication gate `generated-callback-registration`.

**Acceptance.** Common acceptance, `ExitEntry` exits 0 as a fresh executable, the realization check reports the
address operand preserved, the receipt's hypotheses category names the invoker contract with a `Descriptor` origin,
and changing the `CallbackDescriptor` invalidates the entry rows.

**Risks and unknowns.**

- Narrow integers and `bool` crossing into C-invoked entries need extension attributes. D5 keeps them refused.
- Process code lifetime covers foreign holders only in an executable image (loading contract L1). Library output
  stays refused for entries passed out of the unit.
- Farscape does not remove stale files, so a regenerated `Libc` package can surface unrelated refusals. Regenerate
  `Libc/Process` alone, in a review directory.
- Float carriers remain refused here and in every later step as written. See open decision LT-15.

## Step 3: handles, absence carriers and resource contracts (T3)

**Goal.** Admit `CHandle` and `Option<CHandle>` at extern and callback positions with explicit one-word conversions
and a declared resource contract. Pair an owned acquisition with its release on every admitted exit, including
failure, and retire aliases on release (ffi L327, L329 [S]). This is the core of the previous checkpoint's second
blocker. That checkpoint described ownership as "metadata and obligations … retained", and the gap map found
neither in the compiler: `OwnershipTransfer`, `CallerOwns` and `CalleeOwns` occur nowhere in `clef/src` [C\*]
(gap-map C2).

**Depends on.** Step 1, and Step 2 if the recommended order is kept.

**Spec first.**

- **S5, the option carrier.** ffi L98 permits an interior single-word optional handle with established equivalence,
  while `option-operations-representation.md` L409 says `None` is never a null pointer
  (`evidence:research/spec-contract.md` S5 [A]). A ruling reconciles them (decision LT-2).
- **S11, data handles at the extern boundary.** `CHandle` is `index` in the middle end, and no pathway commitment
  states how that `index` becomes a target pointer at an extern declaration without a middle-end cast
  (`evidence:research/spec-contract.md` S11 [A]). The commitment goes into backend-lowering §3.2 (decision LT-3).
- **The resource descriptor.** `platform-bindings.md` gains the additive resource descriptor described under
  Descriptor-vocabulary migration. The amendment also settles the spec's unimplemented `TypeDescriptor` with
  `Ownership: OwnershipKind`, `RefCounted` and `Destructor` (`platform-bindings.md:275-282` [S]). Neither
  BAREWire, clef, Farscape nor Fidelity.Platform implements those fields [C\*] (decision LT-4).
- **`CHandle` equality.** Baker admits it today as `OpaqueReference`
  (`clef:src/Compiler/Baker/Recipes/NumericOperationRecipes.fs:80-84` [C\*]), while ffi L79 gives a handle no role
  beyond being handed back [S]. The owner deferred this to Step 3, to be settled with the resource contract (D4).
  See design item (f) and decision LT-1.
- **Foreign invalidation and invocation thread.** The resource descriptor states whether foreign code may release or
  invalidate the resource outside the program's own release operation, and the callback descriptor states on which
  thread the foreign side invokes an entry. See design item (b).
- **Release as a demanded effect.** A declared release runs on every admitted exit even when its `unit` result is
  discarded (design item (h)).

**Work by repository.**

| Repository | Work |
|---|---|
| BAREWire | The additive resource descriptor: classes `Owned(release binding, failure status)`, `BorrowedFrom(owner)` and `Static(lifetime basis)`, each with provenance. No constructor carries a default. A resource-bearing position without a descriptor is `Undeclared` (`evidence:t1/T1-PLAN.md` §4.3). Harness tests. |
| Farscape | Emit a resource descriptor only with provenance: pilot keys for the release pairing, named owner and static class next to the existing `BindingOwnership` (`src/Farscape.Core/PilotTypes.fs:97-113` [C\*]). Header deallocator attributes are a candidate evidence source, for example glibc's `__attr_dealloc_fclose` on `fopencookie` (`/usr/include/stdio.h:313-316` [H]). Whether Farscape reads them is unknown [I]. |
| clef | Read the descriptor through `PlatformResolution.readDescriptors` (`:1234` [C\*]). `BoundaryRecipes` admits handle and optional-word carriers only with a declared contract and refuses `Undeclared` at resource-bearing positions. A new `Baker/Recipes/ForeignResourceRecipes.fs` produces acquisition, release and retirement obligations with actual participants, patterned on `MappedBindings.fs:55-161` and `ObligationElaboration.fs:356-368` (gap-map T3 [C]). A checked nonnull conversion for results. Equivalence of the settled option representation (`Baker/Ingredients/Options.fs`) with the one-word carrier, per the S5 ruling. `DeclaredNonnull` hypotheses with `HeaderAnnotation` or `PilotDeclaration` origins (integrity rule H4). |
| Fidelity.PSG | `NativeCarrier.Handle` and `NativeCarrier.OptionalWord`. Rows `ForeignAcquisition`, `ForeignRelease` and `AliasRetirement` with integrity rules. Schema change. |
| Alex | Outgoing `None` as a zero word and `Some h` as `h`. Incoming null test through portable control flow into the settled option (ffi L231-232 [S]; pattern `src/Alex/Witnesses/OptionWitness.fs`, gap-map T3 [C]). The exact operations follow the S11 ruling. |
| Composer | Admit handle and optional-word carriers at the extern boundary on SysV AMD64, realized under the S11 commitment. The realization check covers them. |
| Fidelity.Platform | Regenerate `Pthread/Storage` with resource descriptors for `mallocContext` and `freeContext`, which declare `OwnershipTransfer = CallerOwns` with no release pairing today (`Environments/Linux/x86_64/Bindings/Pthread/Storage/Storage.clef:70-86` [C\*]). |
| Composer fixtures | ListenerEntry and IgnoreValues assume strict evaluation of discarded bindings, which the spec's demand rules do not grant (triage X1, `evidence:baseline-repair/triage-composer.md` §0 item 2 [A]). The Phase 0 audit confirmed the group at grade R/S and requires each strict oracle to be identified before it is corrected (audit, Composer group X1). If Phase B rewrote them and pinned them as refused for Step 3 reasons, Step 3 starts from that. The regression sample `17_ExternCall` becomes a Step 3 gate once rewritten off raw `nativeint` onto generated `CHandle` bindings with descriptors (same report, sample table [A]). |

**Tests.**

- clef: NULL to `None` and `None` to NULL. `Undeclared` ownership blocks admission. An owned acquisition releases
  exactly once on every admitted exit, and the `None` path releases nothing. Use after release is refused. A borrowed
  handle is refused after its owner is released. The negative test `BoundaryEmissionCases.fs:130` keeps its meaning
  without a contract and gains a positive twin (gap-map T3 [C]).
- `FnPtr.invoke` with `Option<CHandle>` operands and results, covering ffi L153-154 and the example at L162
  (`FnPtr.invoke gtk_init_ptr None None`) [S]. Critique minor 13 found no gate for this.
- Equality: tests that follow decision LT-1, admitted or refused, including after release.
- Fidelity.PSG: integrity cases for each new row kind.
- Composer `BoundaryAbiTests.fs`: "nullable handle results and arguments cross the C ABI as one word", with a C
  harness returning NULL and non-NULL.
- NativeCallbacks: ListenerEntry and IgnoreValues. Regression runner: rewritten `17_ExternCall`.
- BoundaryConformance: gate 11 splits into a refused `Undeclared` gate and an admitted declared pair.

**Acceptance.** Common acceptance, with ListenerEntry, IgnoreValues and `17_ExternCall` running natively. The
receipt lists `DeclaredNonnull` reliance as hypotheses, and the acquisition and release obligations name their
actual participants.

**Risks and unknowns.**

- This is the largest step. If it splits, the ListenerEntry gate stays at the end, because a carrier without its
  resource contract would violate ffi L327 (gap-map T3 [A]).
- The S11 commitment risks reintroducing a cast under another name. The ruling has to place the conversion in the
  pathway, recorded in the form's admission record, and keep the middle end cast-free.
- The 106 generated `Borrowed` literals in Fidelity.Platform stay in place and must stay inert: nothing in Step 3
  reads `OwnershipTransfer` as a resource contract.

## Step 4: `Option<FnPtr>` boundary conversion (T4-D, then T4)

**Goal.** Specify, then implement, the admitted boundary conversion that realizes absence of a function entry as
native null, while `Option<FnPtr>` keeps ordinary option semantics inside Clef (auditor review on critique M3;
rulings, "Review directions"). Admit the optional forms that the callback-declaration refusals reject today, and
replace Step 0's generator refusal for unannotated slots with the `Option<FnPtr>` default.

**Step 4-D, the design (spec ruling, no code).** Owner: the spec agent with Composer's pathway owner. The fixed
constraints and questions are those of `evidence:t1/T1-PLAN.md` §4.5:

- the portable vocabulary has no null function constant and no null test of a function value, and the middle end
  never holds a function address as data (backend-lowering §7 requirement 5, `:197` [S]);
- absence becomes native null only by a pathway commitment at the extern boundary, under backend-lowering §2.1.1;
- no integer conversion, and no substituted nonnull or no-op entry, may realize absence;
- the open choice is the interior carrier of a foreign-origin optional entry: a tag with a function value valid only
  under `Some`, or a dedicated row carrier the pathway realizes as one word (decision LT-5). A Clef-origin optional
  entry can reuse the callable-component selector with an absence state.

**Depends on.** Step 3 (the one-word carrier and its conversions) and Step 4-D.

**Work by repository (Step 4).**

| Repository | Work |
|---|---|
| clef-lang-spec | The 4-D ruling text in ffi §4.1 and §4.2, replacing Step 1's deferral note. |
| clef | Remove the callback refusals that reject any parameter or result other than a scalar or handle (`src/Compiler/PSGSaturation/SemanticGraph/CallbackDeclarations.fs:122, 138` [C\*], raised as CCS8207 through `PlatformDeclaration.fs:146` [C\*]). Admit `Option<FnPtr>` at extern parameters and results, callback parameters and results, and `FnPtr.invoke` operands and results. Admit interior `Option<FnPtr>`, refused with `[REC-STORAGE]` in Step 1, through the callable-component protocol. Step 1 deferred it to this step as an interior limit separate from the boundary conversion (`evidence:t1/T1-PLAN.md` §2.7(c)). |
| Fidelity.PSG | Rows for the optional entry carrier chosen by 4-D. Schema change. |
| Alex | Witness the optional entry from rows only. |
| Composer | Realize the committed conversion, outgoing and incoming. The realization check extends to null comparisons and null address uses. Negative cases: an integer cast, and a substituted nonnull entry. |
| Farscape | Emit `Option<FnPtr>` by default for unannotated and `_Null_unspecified` slots (ffi L274, L278 [S]), replacing Step 0's refusal for the forms this step admits. |

**Gate.** `pthread_atfork(prepare, parent, child)`, whose three slots carry no nonnull attribute
(`/usr/include/pthread.h:1337-1339` [H]), passing `None` for two of them. Handlers run only on `fork`, so the case
calls `fork`, has the child end with `_exit`, and has the parent reap it with `waitpid(pid, None, 0)`, which needs
Step 3's `Option<CHandle>` [I]. Feasibility is unproven (decision LT-6).

**Tests.** Outgoing `None` and `Some`, incoming NULL and non-NULL, at each admitted position. `FnPtr.invoke` with
`Option<FnPtr>` operands (critique minor 13). The negative realization cases above. A Farscape test that an
unannotated slot now generates as `Option<FnPtr>`.

**Risks.** `fork` in a test executable interacts with the runner's process handling [I]. Native table code fields
that may be null wait for Step 6-T.

## Step 6: spatial admission (T6)

**Goal.** Admit reference, pointer-reference and record-reference parameters and buffers with element
representation, extent, alignment, access mode and permitted retention, relate native length arguments to the
actual Clef bound, and require a terminator inside the extent for NUL-terminated operations (ffi L331 [S]). Extend
the existing `BoundaryByteView`, `BoundaryStringExtent` and `IntrinsicWriteProof` rows (gap-map T6 [C]). Admit
output cells (design item (c)). Today every reference argument is refused before any spatial question arises
(`BoundaryRecipes.fs:120-121` [C\*]).

**Depends on.** Step 3 (handles and absence carriers for pointer-valued fields and nullable buffers).

**Spec first.**

- Retention vocabulary in `platform-bindings.md`: `RetainedReferenceDescriptor` with `Until` as call return, a named
  release, or process end (`evidence:t1/records.md` A.7). Step 1 deferred the retention vocabulary to its
  implementation step (`evidence:t1/T1-PLAN.md` §2.2, M2).
- Output cells (design item (c)), reconciled with "no value is uninitialised" (`error-handling.md:496` [S]).
- The foreign aliasing class of a crossing (design item (b)).
- Access-kind invariance, requirement R57, which no lens assessed (`evidence:research/gap-map.md` §1 (G)). The
  access kinds are capabilities over permitted loads and stores (`access-kinds.md:8-29` [S]).

**Work by repository.**

| Repository | Work |
|---|---|
| BAREWire | `RetainedReferenceDescriptor`, additive. An output declaration with its success convention, extending the existing compiler-owned output cells of `MappedValue.Output` (`src/Descriptors/Bindings.fs:102-106, 108-112` [C\*]). |
| Farscape | Emit spatial facts and retention only with provenance. Missing retention stays undeclared and refused. |
| clef | Spatial obligations with actual participants. Output cells kept inside the binding adapter. A located refusal for a buffer, byte view or region whose element type contains a callable component, which Step 1 assigned here (`evidence:t1/T1-PLAN.md` §4.6). Mapped results become `Ptr<'T, 'Region, 'Access>` only through an admitted projection (ffi L331). |
| Fidelity.PSG | Spatial and retention rows, output-cell rows. Schema change. |
| Alex | Witness the admitted views and cells from rows. Mapped calls are refused today (`src/Alex/Patterns/MappedViewPatterns.fs:10-11`, gap-map R35 [C]). |
| Composer | Realization and checks for reference carriers. |

**Gates.**

- Composer's `ForeignReferences/PointerCells` and `ForeignScalarArrays` entry points. Their README passes are stale,
  and the baseline never ran them, so their current state is unknown (`evidence:baseline-repair/triage-composer.md`
  X6 [A]; gap-map C10).
- `qsort` with a closed comparator, which combines Step 2's entry carrier with a buffer. glibc marks the base and the
  comparator nonnull (`/usr/include/stdlib.h:998-999` [H]). The comparator receives `const void *` element
  pointers, and Clef reads foreign memory only through admitted views, so the binding has to declare that each
  argument points to one element of `base`. That relation is new descriptor vocabulary, and the gate's feasibility
  is unknown until it is designed [I].
- An output-cell case such as `pipe`, whose two descriptors are written only on success [I].

**Correction to the gap map.** The gap map made the 21 `BorrowedViewTests` failures a Step 6 gate. The baseline
triage found that 18 of them fail on their fixture and the other three on causes that need no Step 6 feature, and
assigned them to Phase B (`evidence:baseline-repair/triage-ccs8011.md` §0, lines 44-47;
`evidence:baseline-repair/triage-ccs8403.md` §0 item 7 [A]). The Phase 0 audit qualifies that: the fixture change
for the shared placeholder is not independently ready, and scoped callbacks need a declared activation and demand
contract (audit groups R1, RC2 and RC3, grades R/S/I). Whether that contract is Phase B work or belongs with the
scoped-callback vocabulary of Step 5 is decision LT-18.

**Risks.** Spatial admission and the intrinsic `Sys.write` and `Sys.readline` gaps sit side by side in the
regression runner (`evidence:baseline-repair/triage-composer.md`, regression notes [A]). Keep the two scopes
separate in evidence records.

## Step 6-T: native table projection (T6, tables)

**Goal.** Implement ffi §5.7: a C structure holding function addresses crosses only through a Baker-established
projection from a Clef record type, with a declared layout checked against the selected target, a code-address
field per entry whose contract equals the field's declaration, and retention covering every interval in which
foreign code may read the table or call its entries (`evidence:t1/records.md` A.2). Step 1 specified the text and
refuses every crossing with `[REC-TABLE]`.

**Depends on.** Step 2 (`Repr.Entry`, entry carriers, process code lifetime), Step 3 (handles, resource
vocabulary), Step 4 (nullable code fields) and Step 6 (record references, retention vocabulary).

**Spec first.** The retention vocabulary of §5.7 (from Step 6), the operation checklist of design item (e), the
snapshot rule of design item (d), and address stability, design item (a).

**Work by repository.**

| Repository | Work |
|---|---|
| clef | The projection: layout from the `StructDescriptor` validated against the `AbiProfile` the C ABI names, field correspondence, code-address fields bound to exactly one settled entry, retention obligations over storage, entries, crossings and the release. Codes `[REC-TABLE-LAYOUT]`, `[REC-TABLE-ENTRY]` and `[REC-RETENTION]` (`evidence:t1/T1-PLAN.md` §2.10). |
| Fidelity.PSG | Table rows referenced by storage symbol at each crossing. Schema change. |
| Alex | Witnesses the table's reference at the crossing only. |
| Composer | Realizes the table definition with code-address fields as relocations. A scratch probe showed `llvm.mlir.global` with `llvm.mlir.addressof` translating to `@ops_table = internal constant { ptr, i32 } { ptr @entry_apply, i32 10 }` (`evidence:t1/records.md` F8 [P]). |
| Farscape | Emit a `CallbackDescriptor` per code field of a value struct, which it omits today (`src/Farscape.Core/FidelityCodeGenerator.fs:657-675`, records F6 [C]). Replace the `Borrowed` emitted for listener registration (`src/Farscape.Core/TypedProtocolGenerator.fs:151-152` [C]) with retention declared from provenance, or leave it undeclared and refused. |
| Fidelity.Platform | The Wayland registry bridge already declares the full pattern: a record of two `FnPtr` fields, a 16-byte `StructDescriptor` with both fields as `Repr.Pointer`, one `CallbackDescriptor` per field, and `wl_proxy_add_listener` taking the record as `Named "WlRegistryListener"` with `ReadOnlyReference` and `OwnershipTransfer = Borrowed` (`Environments/Linux/x86_64/Bindings/WaylandNative/Bridge/Callbacks.clef:7-25` [C\*]). Its regeneration waits for Step 7. |

**Gates.**

- A contract-row test first, with a clang-compiled harness that calls through a table it receives, following the
  existing `BoundaryAbiTests` pattern [I].
- A source-through-native libc gate. Both candidates carry more than the projection itself [H]:
  - `sigaction` takes a structure whose handler is a C union of two function pointers selected by `SA_SIGINFO` in
    `sa_flags`, next to a `sa_restorer` entry (`/usr/include/bits/sigaction.h`). It needs a discriminant read and
    signal-context obligations (decision LT-8).
  - `fopencookie` takes `cookie_io_functions_t`, four function pointers, by value
    (`/usr/include/stdio.h:313-316`; `/usr/include/bits/types/cookie_io_functions_t.h:55-61`). It needs
    struct-by-value passing, which the spec leaves unaddressed (S23).
- The Wayland listener gate after Step 7.

**Risks.** Retention semantics of libwayland and GLib are external facts, unknown until each binding declares them
with provenance and never inferred (`evidence:t1/records.md` §6 U4). A table whose entry is chosen at run time needs
an admitted pathway store form for a function address and stays refused until one exists (records U8).

## Step 5: captured callback environments (T5)

**Goal.** Meet ffi L333-335 and L417 [S]:

- a `void*` userdata slot associated with one environment type and with its producer, entry and release;
- retention of the environment across registration and every possible invocation;
- a destroy hook that releases the retained environment exactly once after the last invocation;
- undo of any retention when registration fails;
- explicit retirement and release where no destroy hook exists.

The captured handlers refused today with CCS8096 become admissible (gap-map R43 to R51 [C]).

**Depends on.** Steps 2, 3, 4 and 6. Phase B's callable-aggregate foundation supplies the environment component
for function values (D6(b)). Step 5 extends it to native entries whose environment travels as userdata.

**Spec first.**

- **S15.** ffi L319 and §7 item 5 still say callbacks must be top-level functions, against L335 and L417, which
  specify captured-callback adapters (`evidence:research/spec-contract.md` S15 [A]). The stale text is removed.
- The joint constraint of `program-hypergraph` L45, a single constraint over environment, entry, registration and
  release (gap-map R51 [C]).
- Address stability of a registered environment (design item (a)) and its invocation thread (design item (b)).

**Gates.**

1. A synchronous scoped API: GNU `qsort_r`, whose comparator receives `void *__arg` for the call's duration
   (`/usr/include/stdlib.h:1001-1003` [H]). It also needs Step 6 for the buffer and the element-pointer relation
   noted in Step 6's `qsort` gate.
2. A retained API: `pthread_create` with release on `pthread_join`. The start routine and the thread output cell are
   declared `__nonnull ((1, 3))` (`/usr/include/pthread.h:202-205` [H]), and the `pthread_t *` output needs Step 6.
   The routine runs on a thread the program did not create through Clef (decision LT-9).
3. Optionally `on_exit`, whose environment must live to process exit (`/usr/include/stdlib.h:771-772` [H]).

GLib's `g_idle_add_full` with a destroy notify, the spec's own example (ffi L400-419), is a non-libc library and
becomes a Step 8 per-library gate.

**Risks.** This step has the largest proof surface (gap-map T5 [A]). Retention, exactly-once release and undo on
failure all depend on foreign behavior, so each reliance needs its hypothesis with provenance.

## Step 7: link/load contracts and target vocabulary (T7)

**Goal.** Admit libraries other than `c` with declared link and load identity, today refused at
`BoundaryRecipes.fs:432` [C\*]. Replace the x86-32 calling-convention names `CDecl | StdCall | FastCall`
(`BAREWire:src/Descriptors/Bindings.fs:42-45` [C\*]) with a closed per-target set tied to the selected C ABI.
Record target facts that admission checks (design item (g)). Prepare Win64 and Darwin, which are on the owner's
roadmap (gap-map T7 [A]).

**Depends on.** Steps 1 to 6.

**Scope.**

- **Loading contract L3 escapes.** An entry reachable from a compiler-produced shared object's exported interface is
  admitted with a declared host loading contract (`evidence:t1/T1-PLAN.md` §4.6, m-12).
- **Loading contract L4.** Explicitly loaded, unloadable code (`dlopen`, `dlclose`): a binding-owned library-handle
  resource with alias retirement over every derived code address before unload (`evidence:t1/obligations.md` §2.2
  [A]; ffi L329 [S]). Farscape refuses `dlsym` wrappers today (gap-map R44 [C]).
- **Static libc, L2′.** ffi L11 keeps the boundary for freestanding images with static libc [S]. Admission requires
  `TargetCore.Runtime libc` and library `c`, and no freestanding environment declares a `CAbiDescriptor`
  (critique minor 14; `evidence:t1/obligations.md` §2.2 [A]). Decided with Q13 and S9 (decision LT-14).
- **Variadic functions.** See the legacy-C section and decision LT-13.
- **Struct by value, data models, `char` signedness, `_Bool`, `long double`, unwinding.** The spec leaves these C
  ABI facts unaddressed, so the "complete C ABI" that ffi L287 requires names facts the spec never defines
  (`evidence:research/spec-contract.md` S23 [A]). Step 7 specifies the ones the admitted libraries need and keeps
  refusing the rest with located codes.

**Spec first.** `platform-bindings.md`: the per-target convention set and library link/load identity. ffi §3.7: L3
host loading contracts and the L4 retirement obligation. ffi §1 scope: S9 and Q13. The S23 items above, and the
target facts of design item (g).

**Breaking change.** The convention change touches every descriptor: 142 `CallingConvention =` lines in
Fidelity.Platform, 39 in the three desktop repositories, 110 lines across 14 files in Composer source and tests, 25
in 8 clef files, 8 in 6 Farscape files and 4 lines of `platform-bindings.md` [C\*]. Under D3(a) the desktop
repositories are frozen, so they stop compiling at Step 7 with a located diagnostic. Their `CDecl` literals are
never silently reinterpreted (`evidence:t1/T1-PLAN.md` §5, D3).

**Gates.** The Wayland registry listener (Step 6-T plus a non-libc link). A Wayland client needs a running
compositor, which CI may lack [I] (decision LT-17).

**Risks.** Every consumer changes at once. Run the descriptor-vocabulary rule in full, and stage the change in
review directories before replacing package output.

## Step 8: regeneration and per-library acceptance (T8)

**Goal.** Regenerate the Fidelity.Platform bindings, rewrite the refused pilots, and run per-library native ABI and
lifecycle gates. The packages are stale on two counts: a March raw-pointer generation (Libc, Wayland, WaylandBridge,
DRM, GBM, with 1,040 lines matching `nativeptr` or `nativeint` among them [C\*]) and a September generation that
predates Farscape `5ec1954` (gap-map T8 [A]).

**Depends on.** Steps 3 to 7.

**Work.**

- Regenerate each package into a review directory and reconcile the file set before replacing output, because
  Farscape does not remove stale files (`Farscape:docs/handoff/2026-10-03-ffi-boundary-checkpoint.md:89-95` [C\*]).
- Retire `OwnershipTransfer` from `FunctionDescriptor`. That is the one breaking resource change, scheduled here and
  nowhere earlier (`evidence:t1/T1-PLAN.md` §4.3 item 6; decision LT-16).
- Desktop bindings that remain needed are regenerated into Fidelity.Platform (D3(a)). The checkpoint already says
  "Future desktop bindings belong in `Fidelity.Platform` and must pass their own gates"
  (`Farscape:docs/handoff/2026-10-03-ffi-boundary-checkpoint.md:68-71` [C\*]). GObject-Introspection data is the
  evidence source for those libraries: Farscape's `IntrospectionParser` reads GIR nullability for signals today and
  no transfer, scope, closure or destroy attribute (`Farscape:src/Farscape.Core/IntrospectionParser.fs:23-24, 73-76`
  [C\*]). Reading `transfer-ownership`, `scope`, `closure` and `destroy` as declared evidence with provenance is
  Step 8 generator work [I].
- Per-library gates for Pthread, Wayland, GBM, DRM, Resvg and Display. Device-bound libraries need a render node
  or a compositor at test time (decision LT-17).
- Correct the design-notes and site drift listed in `evidence:research/gap-map.md` §5, and update the ffi L16
  checkpoint at acceptance.

**Risks.** Regeneration can surface refusals in pilots that were accepted under older generator behavior. Each
refusal is evidence about a contract the generator now declines to invent, and is resolved by declaring evidence
with provenance, never by restoring a default.

## Design items

Items (a) to (g) came from the owner's sidebars on Rust's "Beyond the &" comparison and on Seacord's "Unsafe Rust"
(RustConf 2026), recorded by the orchestrator (`evidence:catalog/ASSEMBLY-NOTES.md`). Item (h) comes from the spec's
demand rules and the Phase 0 audit. Each states the current spec and code position, the proposed obligation, and the
step that owns it. Every proposal here is plan text awaiting a spec ruling.

**(a) Address stability.** By a grep of `spec/`, the spec requires a stable address only for lazy memoization
(`lazy-representation.md:369-371` [S]) and mentions relocation only for closure transfer between memory spaces
(`closure-representation.md:139` [S]). No obligation keeps memory registered with foreign code at a fixed address.
Foreign code can hold such an address after the call returns. `wl_proxy_add_listener` receives its listener table as
`void (**implementation)(void)` (`/usr/include/wayland-client-core.h:191-192` [H]), and how long libwayland keeps it
is an external fact that no binding declares yet (`evidence:t1/records.md` §6 U4). libwayland-server links each
`wl_listener` into a list through its `link` field (`/usr/include/wayland-server-core.h:443-449` [H]).

- Proposed obligation: storage registered with foreign code for an interval longer than the call is a place with a
  fixed address for that interval. The program does not replace it by copy-and-update, move it in a transfer, or
  release or reset its region inside the interval. This extends the foreign retention ordering proposed for
  `memory-regions.md` (`evidence:t1/records.md` A.8).
- Placement: no pinning question arises for Step 1's interior records, which have no C layout
  (`evidence:t1/records.md` A.1 item 5), or for Step 3's handles, whose referents C allocates. It arises in
  Step 6 (retained buffers), Step 6-T (registered tables) and Step 5 (environments passed as userdata).

**(b) Aliasing and capability.** A Clef access kind is a capability over permitted operations: `ReadOnly`,
`WriteOnly` and `ReadWrite` (`access-kinds.md:8-29` [S]). It says nothing about who else may touch the memory, and
the ffi chapter has no thread or re-entrancy text (S23 [A], confirmed by a grep of `ffi-boundary.md` [S]).

- Proposed obligation: each crossing declares its foreign aliasing class. Foreign code either does not retain the
  memory, reads it during a declared retention, or may write it concurrently (another C thread, a re-entrant
  callback, a signal handler). The part the compiler can check, such as Clef not writing a table while foreign code
  may read it (ffi §5.7 retention item 4 as drafted), is a graph obligation. Reliance on foreign behavior, such as a
  `const` parameter never being written or a callback never arriving on another thread, is an external execution
  hypothesis.
- Placement: Step 3 for resources (foreign invalidation outside the program's release, invocation thread of an
  entry), Step 6 for memory (buffers, cells, views), Steps 6-T and 5 for tables and environments.

**(c) Output cells.** C output parameters (`pthread_t *`, a `pipe` descriptor pair, `sigaction`'s old-action
pointer) are written by the callee, and before the call the cell holds no Clef value. The spec says no value is
uninitialised (`error-handling.md:496` [S]).

- Proposed accommodation: the cell stays inside the binding adapter as compiler-owned storage, the pattern BAREWire
  already declares for mapped acquisitions ("The compiler owns output cells", `Bindings.fs:108-112` [C\*]). The
  program receives a value only after the call, under the API's declared success convention. Reliance on the callee
  writing the cell on success is a recorded hypothesis, and on failure the cell's contents are never admitted.
- Placement: Step 6, used by Step 5's `pthread_create` gate.

**(d) Matching on foreign structures.** A `match` over a structure foreign code may mutate reads several fields at
different moments, while the graph treats a matched value as one immutable value.

- Proposed rule: the projection reads a foreign structure once into a Clef snapshot, and matching operates on the
  snapshot. Matching in place requires a recorded "same place, not mutated between reads" hypothesis whose
  participants are the reads, admissible only when the crossing declares no concurrent foreign writer.
- Placement: Step 6-T (incoming tables) and Step 6 (projections into views).

**(e) Operation checklist for the native-table projection.** Each operation is supported or refused with its own
code before Step 6-T closes. The initial dispositions below are proposals [I].

| Operation | Proposed initial disposition |
|---|---|
| Read a field | Supported for incoming tables through the snapshot of item (d). Clef-written fields are never read back from the table. |
| Write a field | Initialization only, for a table foreign code retains. A later write is refused unless the crossing declares foreign readers and a write protocol. |
| Project record to table and back | Supported per ffi §5.7. |
| Borrow a field or the table with an access kind | Refused until Step 6's views cover it. |
| Pointer hop (follow a pointer field to another structure) | Refused. A pointer field crosses as a handle unless the target's layout and lifetime are declared. |
| Discriminant read (a C union selected by another field, as in `sigaction`) | Refused until a descriptor declares the discriminant field and its value set. |
| Wrapper preservation (nominal markers through the projection) | Required. Markers survive projection, as generated bindings already require (gap-map R70 [C]). |

**(f) Function pointer identity.** Equality of function pointers is unreliable under multiple instantiation and
deduplication. In Clef, one logical entry can be reached through Baker adapters and `func.constant` reconstructions,
and a linker's identical-code folding can merge distinct entries [I]. The owner refused `FnPtr` equality for now
(D4). If equality is ever admitted, it is defined by declared-entry identity (the settled entry row), never by
address. The same question extends to `CHandle` equality: after a release, address reuse makes equal words
misleading (gap-map C12 [A]). Step 3 settles it with the resource contract (decision LT-1).

**(g) Target caveats.** `AbiProfile` holds pointer size and alignment, 64-bit integer and float alignment, and maximum
alignment (`BAREWire:src/Hardware/Abi.fs:8-15` [C\*]). The representation set ends at 64-bit integers, 64-bit floats,
`bool` and `pointer` (`src/Hardware/Descriptors.fs:94-97` [C\*]), and no Win64 profile exists (`Abi.fs` [C\*]). The
following are target facts that admission checks, each recorded with provenance, and never assumptions [I]:

- byte stores that are not atomic on some microcontrollers, so a write to one field can rewrite an adjacent field
  that an interrupt handler or foreign thread writes;
- availability and alignment of 128-bit integers;
- what relaxed atomics guarantee, and the ordering a foreign side expects;
- data model (LP64 against Win64's LLP64), `char` signedness, the variadic convention (Darwin AArch64 passes variadic
  arguments on the stack), code-pointer size where it differs from data pointers.

A binding that relies on a fact the selected target does not declare is refused at admission. Placement: Step 7.

**(h) Activation and demand at foreign contracts.** Clef evaluates ordinary operands on demand, and the spec requires
"Entry, startup, resource, subscription and foreign-call contracts" to "specify their activation and demanded
operands separately" (`expressions.md:2878-2879` [S]). In the baseline, fixtures that assumed strict evaluation of
discarded bindings, ListenerEntry and IgnoreValues among them, assert outcomes the demand rules do not produce
(triage X1 [A], confirmed at grade R/S by the Phase 0 audit).

- Proposed obligation: every foreign contract a step admits states its activation and its demanded operands. An
  extern call demands the operands its descriptor lists. An entry passed to foreign code is activated by foreign
  control (at exit for `atexit`, during the call for a `qsort` comparator), and its body is an effect root under
  that activation. A declared release is demanded on every admitted exit, so a release whose `unit` result is
  discarded still runs. A scoped callback's activation is the scope its `ScopedCallbackDescriptor` promises.
- Placement: Step 2 (foreign-activated entries), Step 3 (acquisition and release), Step 5 (scoped and retained
  callbacks), and every source-through-native case, whose oracle must follow the demand rules.

## Descriptor-vocabulary migration

**The rule** (auditor review; `evidence:t1/T1-PLAN.md` §4.2). A step that changes the descriptor vocabulary, or how
the compiler reads it, also does all of the following in that step:

1. amends `platform-bindings.md`;
2. updates BAREWire, with a harness test;
3. updates the Farscape generator and its tests;
4. regenerates the affected Fidelity.Platform packages into review directories;
5. updates the Composer and clef fixtures;
6. records the effect on the desktop repositories.

**The additive resource descriptor** (Step 3; `evidence:t1/T1-PLAN.md` §4.3; auditor review).

- A separate quotation record beside the extern, so existing `FunctionDescriptor` literals keep compiling.
- Exact declaration identity: it carries no name field and resolves to exactly one extern declaration, by the same
  rule as `FunctionDescriptor`, a `<name>ResourceDescriptor` binding beside `<name>` in the same module occurrence,
  matched by body identity. It is never keyed by C symbol, because four Pthread Storage bindings share `malloc`
  (`Storage.clef:31, 51, 71, 171` [C\*]). The Step 1 plan counted three. A missing, duplicated or ambiguous
  association is refused.
- No default and no inheritance: `OwnershipTransfer = Borrowed` is never read as a resource contract.
- Scalar-only operations carry no resource obligation, whatever any descriptor says.

**Schedule.**

| Step | Vocabulary change | Spec | Fidelity.Platform in the same step | Composer and clef fixtures | Desktop repositories |
|---|---|---|---|---|---|
| 0 | `Transfer.Undeclared`, the `Function.cdecl` default, Farscape stops emitting `Borrowed` | `platform-bindings.md` `Transfer` | None. Explicit literals stay. | None | Untouched |
| 2 | Entry `TypeRef`, `Repr.Entry`, `CAbiDescriptor` names its `AbiProfile` | `platform-bindings.md` | `Libc/Process` | New `ExitEntry` case | Additive, no breakage |
| 3 | Additive resource descriptor | `platform-bindings.md`, settling `TypeDescriptor` ownership fields | `Pthread/Storage` | ListenerEntry, IgnoreValues, `17_ExternCall`, BoundaryConformance gate 11 | Additive. Their `Borrowed` literals become inert. |
| 4 | Optional entry rows | ffi §4 (4-D) | Packages with nullable callback slots | New `pthread_atfork` case | None |
| 6 | `RetainedReferenceDescriptor`, output declarations | `platform-bindings.md`, ffi §5.6 and §5.7 | Packages with buffers and output cells | PointerCells, ForeignScalarArrays | None |
| 6-T | Table code fields declared with `Repr.Entry` and a `CallbackDescriptor` each | ffi §5.7 | Wayland bridge, with Step 7 | Table cases | None |
| 7 | Per-target convention set, link/load identity, target facts | `platform-bindings.md`, ffi §1 and §3.7 | Every descriptor | Every descriptor literal | **Breaking.** Frozen per D3(a), they stop compiling with a located diagnostic. |
| 8 | `OwnershipTransfer` retired from `FunctionDescriptor` | `platform-bindings.md` | Full regeneration | Every descriptor literal | Per D3(a), maintained bindings move to Fidelity.Platform |

**Inventory at the pinned heads** (`git grep` line counts [C\*]).

Fidelity.Platform: 130 `Expr<FunctionDescriptor>` bindings in 20 files, 12 `Expr<CallbackDescriptor>`, 106
`= Borrowed`, 18 `= CallerOwns`, 18 `= CalleeOwns` and 142 `CallingConvention =` lines.

| Package | Function descriptors | Callback descriptors | `= Borrowed` | `CallingConvention =` |
|---|---|---|---|---|
| Pthread | 67 | 0 | 59 | 67 |
| WaylandNative | 42 | 12 | 33 | 54 |
| GBMNative | 9 | 0 | 5 | 9 |
| ResvgNative | 7 | 0 | 4 | 7 |
| DisplayNative | 5 | 0 | 5 | 5 |

Libc, Wayland, WaylandBridge, DRM and GBM carry no descriptors and belong to the March raw-pointer generation.

| Repository (head, date) | Function descriptors | Callback descriptors | `= Borrowed` | `= CallerOwns` | `= CalleeOwns` | `CallingConvention =` | `Pointer 64` |
|---|---|---|---|---|---|---|---|
| Fidelity.Gtk3 (`5cc6d1d`, 2026-09-14) | 14 | 2 | 16 | 0 | 0 | 16 | 15 |
| Fidelity.GObject (`541f580`) | 9 | 0 | 5 | 1 | 3 | 9 | 13 |
| Fidelity.WebKit (`b274f13`) | 11 | 3 | 11 | 3 | 0 | 14 | 32 |

The per-file breakdown is in `evidence:t1/T1-PLAN.md` §4.4, and its totals match these recounts. Fidelity.Gtk3
declares `OwnershipTransfer = Borrowed` on `gtk_widget_destroy`
(`CPU/Linux/x86_64/Bindings/Gtk3Native/Native/Native.clef:10, 23` [C\*]).

Fixture files with `OwnershipTransfer` lines: Composer `tests/Alex.Tests/ForeignDeclarationTests.fs` (2),
`tests/Alex.Tests/WitnessArtifactTests.fs` (2), `tests/ForeignReferences/PointerCells.clef` (2),
`tests/ForeignScalarArrays/Bindings.clef` (6), `tests/NativeCallbacks/ListenerEntry.clef` (2). clef
`tests/Clef.Compiler.Service.Tests/BoundaryEmissionCases.fs` (6), `ClosedCallbackCases.fs` (2),
`ClosureValueCases.fs` (4), `ForeignReferenceCases.fs` (6). Farscape has `OwnershipTransfer` or `Borrowed` lines in
seven source files and five test files [C\*]. The additive descriptor leaves their `OwnershipTransfer` lines valid
until Step 8, and their `CallingConvention` literals change at Step 7.

**Desktop repositories, scoped out.** The owner froze Fidelity.Gtk3, Fidelity.GObject and Fidelity.WebKit as legacy
(D3(a)). No step edits them. Steps 2 to 6 are additive and leave them compiling as they do today. Step 7's convention
change stops them with a located diagnostic, and Step 8 regenerates whatever desktop bindings remain needed into
Fidelity.Platform.

## Legacy C pressure points

The owner's principle: the spec is primary, legacy C realities can justify an accommodation or an amendment, and no
evidence is manufactured in either direction (rulings, "Owner working principles"). Each row gives the principled
accommodation this plan proposes and the step that owns it.

| Pressure point | Evidence | Principled accommodation | Step |
|---|---|---|---|
| Unannotated callback slots in libc and GLib | `pthread_atfork`'s three slots carry no nonnull attribute (`pthread.h:1337-1339` [H]). GObject declares signal handlers as `GCallback`, `void (*)(void)` (`Farscape:docs/07_Pilot_Project_Setup.md:232` [D]). | The slot is `Option<FnPtr>` (ffi §5.2). Header attributes (`atexit`, `qsort` and `pthread_create` carry `__nonnull` [H]) and GIR `nullable` attributes are evidence with provenance. A pilot nonnull declaration is admitted as a `DeclaredNonnull` hypothesis with a `PilotDeclaration` origin. A comment establishes nothing. Until Step 4, Step 0's refusal before output stands. | 0, 4 |
| Type-erased function tables | `wl_proxy_add_listener` takes `void (**implementation)(void)` (`wayland-client-core.h:191-192` [H]). | Per-field signatures come from the protocol XML or GIR as declared evidence, and the foreign side calling each slot with that signature is a `CallbackInvocationContract` hypothesis. | 6-T, 8 |
| Callbacks without userdata or destroy hook | `qsort` and `atexit` take entries with no userdata. Signal handlers run in signal context. `pthread_create` has userdata and no destroy hook. | Closed entries only where no userdata exists: `qsort` comparators (Step 2 with Step 6) and `atexit` handlers (Step 2). A captured comparator goes through `qsort_r`, an exit-time environment through `on_exit` (Step 5). `pthread_create` retires its environment at `pthread_join`, a declared release, and a detached thread stays refused. Signal handlers stay refused until the spec states signal-context obligations (decision LT-8). | 2, 5 |
| Pointer-identity comparisons | `CHandle` equality is admitted today (`NumericOperationRecipes.fs:80-84` [C\*]). Some C APIs compare function pointers, such as disconnecting a GObject handler by function [I]. | Equality only by declared identity, never by address (design item (f)). A binding prefers the API's registration token, for example the handler id a connect call returns, over a by-function lookup [I]. | 3 |
| Intrusive structures | `wl_listener` holds a `wl_list link` and a `notify` entry, and handlers recover their container with `wl_container_of`, pointer arithmetic from member to container (`wayland-util.h:424-426` [H]). | The listener is a native table embedded in a Baker-laid-out registration block. `link` is declared foreign-owned storage that Clef neither reads nor writes after initialization (ffi §5.7 field correspondence as drafted). The adapter recovers the block through a Baker-established projection from the layout declaration, with no source-level pointer arithmetic. This needs spec text beyond the drafted §5.7 [I]. | 5, 6-T, 7 |
| Variadic functions | glibc declares `open`, `openat`, `fcntl` and `ioctl` variadic (`fcntl.h:177, 209, 233`; `sys/ioctl.h:42` [H]). The generated Fidelity.Platform bindings declare them with fixed arity, for example `int open(const char * __file, int __oflag)` (`Libc/IO/IO.clef:93-101, 108-111`; `DisplayNative/Native/Native.clef:49-66` [C\*]). A grep of Farscape `src/` and of the spec finds no variadic handling [C\*]. | The generated fixed-arity form is a narrowed contract the header does not state. Calling a variadic function through a fixed prototype is outside the C contract, and conventions differ by target [I]. Proposed: Farscape diagnoses variadic declarations before output, then Step 7 adds a variadic descriptor with per-call-site argument representations and the target's variadic convention. No admission reaches these bindings today: the Libc package carries no descriptors, which Baker refuses (`BoundaryRecipes.fs:111-112` [C\*]), and the DisplayNative `open` descriptor has a pointer parameter, refused at `:74-80` [C\*]. The narrowing has no executed consequence yet. The first step that admits pointer carriers and regenerates these packages would expose it. | 0 or 7 (decision LT-13) |
| Output cells | `pthread_create`'s `pthread_t *`, `sigaction`'s old action. | Compiler-owned cell inside the adapter, value admitted after the call under its success convention (design item (c)). | 6 |
| Struct by value | `fopencookie` takes `cookie_io_functions_t` by value. | Specified per target with the S23 items, refused until then. | 7 |

## Open owner decisions

Decisions already made and binding on these steps: D3(a) freezes the desktop repositories, D4 refuses `FnPtr`
equality and settles `CHandle` equality in Step 3, D5 defers integer promotion and keeps narrow carriers refused, D2
requires the owner's go-ahead for each shared-daemon switch, and component gates alone accept these steps.

| ID | Decision | Options | Needed before |
|---|---|---|---|
| LT-1 | `CHandle` equality (Q7 with D4) | (a) Admit, restricted to aliases that are not retired, defined by resource identity. (b) Remove. Leaving it unspecified is excluded (gap-map Q7). | Step 3 |
| LT-2 | S5, the option carrier | Reconcile ffi L98 with `option-operations-representation.md` L409. | Step 3 |
| LT-3 | S11, data handles at the extern boundary | Specify the pathway commitment that realizes the `index` word as a target pointer. | Step 3 |
| LT-4 | The spec's `TypeDescriptor` ownership fields | Replace them with the additive resource descriptor, or keep both with a stated relationship. | Step 3 |
| LT-5 | Interior carrier of a foreign-origin `Option<FnPtr>` | Tag with a function value valid under `Some`, or a dedicated row carrier realized as one word. | Step 4 |
| LT-6 | Step 4 gate | `pthread_atfork` with `fork` and `waitpid`, or another API that 4-D finds. | Step 4 |
| LT-7 | Native table gate | Contract-row harness first, then `sigaction` (union and signal context) or `fopencookie` (struct by value) as the libc gate. | Step 6-T |
| LT-8 | Signal context | Specify signal-context obligations (S23), or refuse signal-handler registration as a documented limit. | Step 6-T if `sigaction` is chosen, and any signal-handler binding |
| LT-9 | Clef code on threads it did not create | Admit with stated obligations for environments and regions, or refuse `pthread_create`-style entries. | Step 5 |
| LT-10 | Address stability (design item (a)) | Adopt the place obligation in `memory-regions.md` and ffi §5.7, or another formulation. | Steps 6, 6-T, 5 |
| LT-11 | Foreign aliasing classes (design item (b)) | Adopt per-crossing declarations with the obligation and hypothesis split, or another formulation. | Step 6, resource part in Step 3 |
| LT-12 | Output cells (design item (c)) | Adopt the adapter-owned cell with a success convention, reconciled with `error-handling.md:496`. | Step 6 |
| LT-13 | Variadic functions | (a) Farscape refuses variadic declarations before output now, in Step 0, and Step 7 admits them with a variadic descriptor. (b) Leave generation as it is until Step 7. | Step 0 for (a). In any case before `Libc/IO` or `DisplayNative` is regenerated with admitted pointer carriers (Step 3 at the earliest) |
| LT-14 | Q13 and S9: C without libc, and static libc | Bring CMSIS-style C libraries and the static-libc image into ffi scope, or exclude them explicitly. | Step 7 |
| LT-15 | Float carriers | No tranche in the sources schedules `Float` carriers. The gap map refuses them with pointers and named types (R4), and the Step 1 plan assigns them to "T2 to T6" collectively (`evidence:t1/T1-PLAN.md` §3.A.3). Schedule them in Step 2 as a scalar extension, or as a separate step. | Step 2 |
| LT-16 | Retiring `OwnershipTransfer` | Retire it at Step 8 (breaking), or keep it as an inert, deprecated field. | Step 8 |
| LT-17 | Native gates that need a compositor or a device | Decide which per-library gates run in CI and which run only on the owner's machine, with the evidence record saying which. | Steps 7, 8 |
| LT-18 | The activation and demand contract for scoped callbacks (design item (h)) | Settle it in Phase B, where the `BorrowedViewTests` repair needs it, or with Step 5's scoped-callback vocabulary. | Phase B closure |

## Disposition of source findings

| Source finding | Disposition in this plan |
|---|---|
| Critique M1, resource vocabulary breaks descriptor literals | Additive resource descriptor in Step 3, `Pthread/Storage` regenerated in Step 3, `OwnershipTransfer` retired only at Step 8 |
| Critique M2, vocabulary changes without spec amendments | Each step's "Spec first" list and the migration rule |
| Critique M3, no portable carrier for a nullable entry | Step 4-D design before Step 4, per the auditor's direction |
| Critique M9, `atexit` handler against the arity rule | Resolved by Step 1's mapping rules, consumed by Step 2 |
| Critique M13, desktop repositories | Inventory above, frozen per D3(a), breaking only at Step 7 with a located diagnostic |
| Critique minor 10, Step 5 needs Step 6 | Dependency graph |
| Critique minor 13, `FnPtr.invoke` absence marshalling | Step 3 for `Option<CHandle>`, Step 4 for `Option<FnPtr>` |
| Critique minor 14, static libc | Step 7, with LT-14 |
| Critique minor 17, runner exit status | Step 2's `ExitEntry` design, or runner support |
| Auditor: vocabulary scheduled with fixtures and consumers | Migration rule and schedule |
| Auditor: additive descriptor with exact identity and no `Borrowed` default | Step 3 and the migration section |
| Auditor: `Option<FnPtr>` boundary conversion | Step 4-D |
| Auditor: code lifetime by loading contract | Step 1 for L1 to L3, Step 7 for L3 escapes and L4 |
| Gap map T6 gate on `BorrowedViewTests` | Superseded by the baseline triage, owned by Phase B |
| Gap map Q4 and the `fromSymbol` tranche | Replaced in Step 1 by the record forms and `FnPtr.ofExtern` |
| Assembly design items (a) to (g) | Design items section, placed in Steps 3, 5, 6, 6-T and 7 |
| Phase 0 audit P0-12, lease coverage | Common acceptance: each gate runs inside a held lease |
| Phase 0 audit groups X1, R1, RC2, RC3 | Design item (h), Step 3 fixtures, Step 6 correction, LT-18 |
