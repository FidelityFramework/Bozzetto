# FFI correction: Step 0 plan

October 3, 2026. Step 0, labelled T0 in the research sources, makes the refusals that exist
today stable, coded, located and tested. It writes the owner's native-callable rulings into the
spec, removes invented ownership from the descriptor vocabulary and the binding generator, and
corrects the checkpoint claims the research found overstated. It starts after the clean-baseline
repair closes, and its reference point is the pin that repair produces.

Step 0 changes no Fidelity.PSG contract: no schema bump, no fingerprint change and no new row or
edge. It changes no compiler admission decision. CCS, Baker, Alex and Composer accept and refuse
the same programs before and after Step 0, and only diagnostic codes, messages and locations
differ. Two components do change their output, by ruling. BAREWire's `Function.cdecl` stops
supplying `Borrowed`, and Farscape stops inventing ownership and stops narrowing unannotated
callback slots, so some bindings it generates today are refused before any output is written.
Neither change reaches a compiler decision today, because no source in clef, Alex, Composer or
Fidelity.PSG reads `OwnershipTransfer` [R: grep over each repository's `src` at the pinned heads,
zero hits].

The T0 workflow pinned its pre-edit baseline between 12:54 and 14:14 EDT and was stopped before
any edit agent ran. Its report folder (`t0/reports/` in the evidence folder) is empty and no code
allocation sheet exists [R]. No repository contains Step 0 work. The owner's Farscape notes, which
the T0 brief assigned to a `notes` agent, were finished by a separate pass and are reported in
`/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/notes/notes-final.md`.

The [rulings record](FFI_Correction_Rulings_2026-10-03.md) governs this plan. The
[tracker](FFI_Correction_Tracker_2026-10-03.md) carries its items, evidence fields and
sign-offs. The [implementor guide](FFI_Correction_Implementor_Guide_2026-10-03.md) holds the
working rules, lease mechanics and evidence record template that this plan does not repeat.

## Sources and evidence grades

Load-bearing claims carry one of these grades.

| Tag | How the claim is known |
|---|---|
| [X] | Executed in an unfiltered run. The log or record is cited. |
| [R] | Read in code or documentation at the pinned heads, cited as file:line. |
| [S] | Spec text at clef-lang-spec `232482b`. |
| [H] | Git history, commit cited. |
| [A] | One agent's report, not re-verified for this plan. |
| [I] | Inferred. |

Every file:line in this plan was re-read for it on October 3 at these heads, with every tree clean
except Farscape's three owner files: clef `a6614b5`, Alex `4e1f859`, Composer `fcd68d6`, Farscape
`5ec1954`, BAREWire `571ff31`, Fidelity.PSG `f153c75`, Fidelity.Platform `d7cc3b7`,
clef-lang-spec `232482b`. The clean-baseline repair edits several of the cited files, among them
`FunctionPointers.fs` under decision D6(b), Composer's NativeCallbacks runner and the spec's
`ntu-types.md`. Before starting, locate each citation again at the post-repair heads by its
content. The line numbers hold at the pinned heads only.

Raw evidence lives under `/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/`,
abbreviated `E/` from here on. It is local and non-portable.

| Source | Path | Used for |
|---|---|---|
| T0 brief | `E/t0/T0-BRIEF.md` | Rulings as first forwarded, the fixed `Undeclared` vocabulary, global rules |
| Stopped T0 workflow | `/home/hhh/.claude/projects/-home-hhh-repos-Bozzetto/e8bd60e6-43cb-40dd-a471-36b9394d9dd1/workflows/scripts/ffi-t0-baseline-edit-gate-wf_d744dafa-a76.js` (session storage, not in `E/`) | Per-repository task texts `SPEC`, `CLEF`, `ALEX`, `COMPOSER`, `FARSCAPE`, `BAREWIRE`. This plan reproduces and corrects them. |
| Gap map | `E/research/gap-map.md` §2 (corrections C1 to C15), §3.3 T0 | Checkpoint corrections, the original T0 scope |
| Critique | `E/research/critique.md`, minors 6, 7, 8 and 17 | Pilot-key warning, publication-gate site, CCS8010 overloading, runner pin |
| T0 baseline records | `E/t0/baseline/<repo>.md`, `build-all.sh`, `test-all.sh` | Exact build and test commands |
| Baseline inventory | `E/baseline-repair/INVENTORY.md` | Pinned totals and failing lists |
| Repair plan and review | `E/baseline-repair/REPAIR-PLAN.md` §1.2, §2.3 and §3.1, with `repair-review.md` D-06 and D-07 | Overlap with Phase B, gate rules |
| Composer triage | `E/baseline-repair/triage-composer.md` | Composer facts. The `COMPOSER` task cited `t0/baseline/composer.md`, which was never written. |
| Farscape notes report | `E/notes/notes-final.md`, "Tracker list" | Remaining vestiges in other Farscape docs |
| Step 1 plan draft | `E/t1/T1-PLAN.md` §2.1, §2.10 | What Step 1 expects Step 0 to deliver |

clef source files are cited by file name. Under `clef/src/Compiler/`, `FunctionPointers.fs`,
`BoundaryValues.fs` and `CallbackDeclarations.fs` sit in `PSGSaturation/SemanticGraph/`,
`BoundaryRecipes.fs` in `Baker/Recipes/`, and `NativeService.fs` and the `Expressions/` files in
`NativeTypedTree/`. clef tests sit in `tests/Clef.Compiler.Service.Tests/`.

The gap map's T0 section predates the rulings in two places, and the rulings take precedence.
It proposed keeping the narrowing of callback slots and documenting it in a generated comment
(gap-map §3.3). Ruling R6 replaces that with a refusal unless explicit evidence exists. It also
assigned the ClosureValue listener fixture to T0, which the repair plan has since taken over
(`REPAIR-PLAN.md` §1.2, RP-LISTENER: "The repair owns it, and T0 drops it").

## Scope and invariants

The T0 brief set five goals: pin baselines, make the current refusals stable, coded, located and
tested, stop manufacturing evidence, write the owner's rulings into the spec, and continue the
owner's Farscape notes. The baseline pin and the notes are done elsewhere. Step 0 keeps the middle
three and adds the checkpoint corrections from gap-map §2.

| Invariant | How the gate checks it |
|---|---|
| No Fidelity.PSG contract change | `git -C Fidelity.PSG status --short` is empty. `Revision.Schema` (`src/Fidelity.PSG/Revision.fs:191`, 16 at `f153c75`) and `BinaryGenerated.Fingerprint` (`src/Fidelity.PSG/BinaryGenerated.fs:10`) equal the post-repair pin. D6(b) moves the schema change into Phase B, so the expected schema is the pin's value, likely 17 [I]. |
| No compiler admission change | Each suite's failing set equals the pin's. Every refusal test still refuses. Composer per-sample diagnostic multisets equal the pin's after applying the code mapping in the allocation sheet. |
| Output changes only by ruling | BAREWire and Farscape output differs from the pin only through items S0-W1, S0-F1, S0-F2 and S0-F3. |
| Spec gains only ruled text | Every amendment traces to R1 to R4, the `fromSymbol` retirement or the fixed `Undeclared` vocabulary. Anything else becomes an open question. |

Out of scope for Step 0:

| Item | Where it belongs |
|---|---|
| Implementing `FnPtr.ofExtern` | Step 1-B. Step 0 specifies it and refuses `fromSymbol` with a code naming it. |
| Refusing `FnPtr` equality (owner decision D4) | Step 1 |
| Gating Clef publication on `FnPtr` findings (gap-map C8) | Step 1. Critique minor 7: `sourceAdmitted` (`clef/src/Compiler/NativeTypedTree/NativeService.fs:1250-1254`) is consumed before `FunctionPointers.settle` runs (`:1292`), so the gate belongs at the publication condition (`:1420`) with that settlement and `PlatformDeclaration.check` (`:1427`) moved ahead of it [R]. That changes which programs publish, an admission change. |
| The `Option<FnPtr>` boundary conversion | Step 4 |
| Descriptor vocabulary beyond `Undeclared`: `TypeDescriptor.Ownership` and `Destructor` (`spec/platform-bindings.md:279-281`), per-level nullability, a static class, variadics | Later steps, scheduled with Fidelity.Platform fixtures (rulings record, review directions) |
| Regenerating Fidelity.Platform bindings and pilots | Step 8 |
| ClosureValue listener fixture (`ClosureValueCases.fs:120`, platform attached only at `:129`) | Phase B, RP-LISTENER |
| Farscape `tests/native-callbacks/run.py` | Not a Step 0 gate. A Python driver whose .NET replacement the research recorded [A]. Its outcome is UNKNOWN (`E/t0/baseline/Farscape.md`, "Not run"). |
| The Bozzetto daemon and its workers | The independent Bozzetto track. No Step 0 gate depends on the daemon. |

## Entry criteria

1. **The clean baseline is closed and pinned.** Every suite in the gate command table passes at its
   documented command, unfiltered, apart from any exact pending ledger the baseline document
   accepts, and the results are recorded. Until
   [`FFI_Correction_Baseline_2026-10-03.md`](FFI_Correction_Baseline_2026-10-03.md) exists, the
   source is `E/baseline-repair/` (`REPAIR-PLAN.md` corrected by `repair-review.md`, and the
   guide's hold on Phase B edits). That pin replaces the October 3 T0 pin as Step 0's reference.
   The T0 numbers (clef 2,260 of 2,354, NativeCallbacks 0 of 29, regression 3 of 53 compiling)
   stay as history [X: `E/baseline-repair/INVENTORY.md`].
2. **The owner has committed or set aside the Farscape notes.** The three files are
   `docs/11_Namespace_Scoped_PSG_Design.md`, `docs/roadmap/07_type-provider-clef-fidelity.md` and
   `src/Farscape.Core/ProtocolParser.fs`. The last one compiles into both Farscape suites, and a
   gate's scope check stops on files outside its change set (`repair-review.md` D-06). Step 0's
   Farscape change set must also be reviewable and committable on its own. The notes report
   proposes one commit line (`E/notes/notes-final.md`, "Result").
3. **Phase 0 has audited this plan** and recorded its findings in the tracker.
4. **Every Step 0 repository is clean**, and its head is recorded in the Step 0 evidence record.
5. **No detached test driver holds a lease.** Confirm that no earlier driver is still running and
   that every lease such a driver held was released.

## Sequence and file ownership

1. The code allocation sheet (S0-S1) comes first. No repository uses a new number before the sheet
   is complete. The other spec items can proceed alongside the code work.
2. BAREWire (S0-W1) lands before Farscape's ownership item (S0-F1). Generated text that writes
   `OwnershipTransfer = Undeclared` resolves against BAREWire's Clef-side
   `src/BAREWire.BindingMetadata.fidproj`, which BoundaryConformance compiles
   (`E/t0/baseline/Farscape.md`).
3. clef (S0-C) and Alex (S0-A) follow the sheet.
4. Farscape follows the sheet and S0-W1. Its conformance re-pins (S0-F4) follow clef's code change
   (S0-C2).
5. Composer (S0-M) has no dependency on the others.
6. One serialized gate runs after every edit has stopped. Edits and gate runs never overlap.

| Repository | Files Step 0 may edit | Items |
|---|---|---|
| clef-lang-spec | `spec/error-handling.md`, `spec/ntu-types.md`, `spec/platform-bindings.md`, `spec/ffi-boundary.md` | S0-S1 to S0-S6 |
| BAREWire | `src/Descriptors/Bindings.fs`, a test module in `tests/` with its `tests/BAREWire.Tests.fsproj` and `tests/Program.fs` registration | S0-W1 to S0-W3 |
| clef | `FunctionPointers.fs`, `BoundaryValues.fs`, `Intrinsics.fs`, `Expressions/Types.fs` (`DiagnosticCodes`), and test files in `tests/Clef.Compiler.Service.Tests` | S0-C1 to S0-C5 |
| Alex | `src/Alex/Traversal/TransferTypes.fs`, `src/Alex/Witnesses/FunctionPointerWitness.fs`, `src/Alex/Witnesses/PlatformWitness.fs`, a new file under `tests/Alex.Tests/Tests/` | S0-A1 to S0-A3 |
| Composer | `tests/NativeCallbacks/Program.fs`, `tests/NativeCallbacks/README.md`, `tests/ForeignReferences/README.md`, `tests/ForeignScalarArrays/README.md` | S0-M1 to S0-M3 |
| Farscape | Generator sources under `src/Farscape.Core/` (except `ProtocolParser.fs`), `tests/Farscape.Tests/`, `tests/BoundaryConformance/`, docs other than the owner's two notes files | S0-F1 to S0-F6 |
| Fidelity.PSG, Fidelity.Platform, Calque, Bozzetto, Fidelity.Data, Fidelity.FSharp.Incremental | none | none |

## Work items

### clef-lang-spec

There is no spec test suite. `CONTRIBUTING.md:27-33` asks that intra-spec links work in the
sources. Acceptance for every spec item: each link and anchor the item touches resolves, the
normative voice is kept, the item adds no requirement beyond its ruling, and anything needed but
unruled goes to the open questions with the owner.

**S0-S1. Code allocation sheet.** Write the complete sheet before any other edit, and file it with
the Step 0 evidence record. For each code it gives the number, severity, meaning, raise site
(existing or planned) and message guidance. Allocate from free numbers in the fitting blocks of
`error-handling.md:305-311`:

- (a) `FnPtr.fromSymbol` is retired, and the message points to `FnPtr.ofExtern` on a described
  extern. Today `FunctionPointers.fs:68` refuses it under CCS8096 [R], whose tabled meaning is the
  closed callback adapter (`error-handling.md:356`) [S]. Step 1's draft names this slot
  `[FROMSYMBOL-RETIRED]` (`E/t1/T1-PLAN.md` §2.10). One allocation serves both steps.
- (b) A native function value or indirect invocation lacks settled native callable facts, the
  compiler-side refusal at the commitment boundary (ffi `:337`). No compiler site raises it today,
  since Alex refuses these forms instead. Its planned producer is Step 1, unless open question 2
  assigns it to the existing arms.
- (c) A fabricated nonnull boundary value. `BoundaryValues.fs:150` raises CCS8010 for it [R]. The
  spec gives CCS8010 one meaning, the `null` keyword, and calls it the only null diagnostic
  (`error-handling.md:329, 496`) [S]. Critique minor 8 records the overload.
- (d) A reserved family for ffi §5.6 resource refusals: ownership class, release pairing and alias
  retirement, marked reserved until implemented.
- (e) Alex's root refusals for a native function address, an indirect invocation and a callback
  entry. The spec tables no AX code [R: no `AX` in `error-handling.md`]. Alex owns its registry, a
  closed union at `src/Alex/Traversal/TransferTypes.fs:115-142` [R]. The sheet records the AX
  numbers and states that ownership.

The sheet also settles four existing codes or families.

- **The other `FunctionPointers.fs` arms.** Every refusal in that file uses one helper that sets
  CCS8096 (`:27-28`), including the `ofFunction` arms (`:56-59`) and the `invoke` arms (`:66-67`)
  [R]. Three tests pin CCS8096 for them (`FunctionPointerCases.fs:73, 83, 93`) [R]. The sheet
  either widens CCS8096's row to cover them or gives them code (b) (open question 2).
- **CCS8092.** The table says Warning (`error-handling.md:355`) [S]. The compiler raises it as an
  error: `addNativeError` (`Expressions/Types.fs:361`) sets `Severity = Error`, and both raise
  sites call it (`Expressions/Applications.fs:647`, `Expressions/Types.fs:1735`) [R]. Correcting the
  table keeps Step 0's invariant. Making the compiler warn would admit programs it refuses today,
  an admission change outside Step 0 (open question 3).
- **Codes raised but untabled.** CCS8402 (`Baker/Recipes/SequenceOwnershipRecipes.fs:16`), CCS8403
  (raised in 11 source files), CCS8404 (`Baker/Recipes/StringByteStorageRecipes.fs:29, 306`,
  `Nanopass/StringByteStorage.fs:29`), CCS8405 (`Nanopass/LazyRuntime.fs:135`) and CCS8500
  (`Expressions/Types.fs:115`, raised at `NativeService.fs:926`) [R]. Read each raise site and
  table its actual meaning. Where CCS8403 carries several meanings, record that rather than
  hiding it. The range table stops at CCS8499 (`:311`) while the code table already lists CCS8701
  to CCS8711 (`:374-377`), so CCS8500 also needs a decision on the range row.
- **The null constraint.** `types-and-type-constraints.md:317` says `typar : null` produces
  CCS8010, and `error-handling.md:376` tables CCS8710 for it [S]. clef defines
  `CCS8710_NullConstraint` (`Expressions/Types.fs:127`) and raises it nowhere [R: grep]. Which
  code the constraint draws today is UNKNOWN until a test runs it. The requirement map proposes
  reconciling this with the allocation (its item S22) [I].

**S0-S2. `error-handling.md` rows and prose.** Add the rows of the sheet. Reconcile the prose at
`:496` with code (c): the new code names fabricated evidence for a nonnull carrier, and the
statement that null-freedom has exactly one null diagnostic must stay true.

**S0-S3. The NTU function value (ruling R4).** `ntu-types.md:229` maps `NTUfnptr` to `index` [S].
It changes to a portable function value until target realization, consistent with
`backend-lowering-architecture.md` §4.1, §4.2 and §7 items 2 and 5, and with ffi `:140`. The §2.3
row at `:52` resolves `NTUfnptr` at the pointer dimension [S]. Whether that row needs a matching
note, placing the pointer width at target realization, is the spec agent's reading under the same
ruling, recorded with the item. clef's
`docs/fidelity/phg/Design_Supersession_Register.md:83, 128` already records this supersession [R].
`native-type-universe.md` and `native-type-mappings.md` carry no `NTUfnptr` or function-pointer
row [R: grep]. Phase B may also edit `ntu-types.md` (`REPAIR-PLAN.md` §3.1, decision D2 at `:79`),
so start from the post-repair text.

**S0-S4. `Transfer.Undeclared` in `platform-bindings.md`.** The quotation structure at `:290` lists
`CallerOwns | CalleeOwns | Borrowed` [S]. Add `Undeclared` with the T0 brief's fixed meaning: no
ownership evidence was supplied, and the value never denotes borrowed, owned or static. A
resource-bearing position with `Undeclared` ownership carries an unresolved obligation that
admission must diagnose. A scalar-only operation has no resource obligation, whatever this field
says. Leave the `TypeDescriptor` fields at `:279-281` as they are and record the deferred
reconciliation.

**S0-S5. `ffi-boundary.md` amendments for R1 to R3 and the `fromSymbol` retirement.** Keep section
numbers stable where possible. Draft wording exists in `E/t1/T1-PLAN.md` §2.3 (R1) and §2.9
(`ofExtern` and the retirement), written by one agent [A]. The §2.9 draft cites a §3.7 on code
lifetime that only Step 1 adds, so drop such forward references when reusing it.

| Section | Lines [S] | Change |
|---|---|---|
| §3.2 `FnPtr.fromSymbol` | `:117-140` | Retire `fromSymbol` as a declaration route, since a string and a Clef type carry no boundary contract. Specify `FnPtr.ofExtern` per R2: signature shape, type inferred from the binding, the import demanded and settled by taking the entry, the declared foreign entry named including any declared adapter and never an application-facing wrapper. Code generation stays an extern `func.func` with a `func.constant`, and address realization stays a pathway commitment (`:140`). |
| §3.3 `FnPtr.invoke` | `:142-163` | Its examples call through `fromSymbol` values. Rewrite them over `ofExtern`. Critique minor 13 notes that no gate covers `invoke`'s Option to NULL marshalling. The normative text stays, and the example at `:162` must not imply that the conversion is implemented. |
| §3.4 `FnPtr.ofFunction` | `:165-195` | Encode R1: compiler-owned families, one complete contract settled by Baker across every origin and invocation, adapters established by Baker, today's limits stated as diagnosed admission limits, register spills distinct from source-level storage. Keep `:178`'s three sources coherent: for a compiler-owned family the boundary contract is the one Baker settles. |
| §5.3 Generated binding structure | `:287` | The extern declaration with `FunctionDescriptor` and `CallbackDescriptor` entries is the declaration route. `ofExtern` takes an entry from such a declaration and `invoke` calls through it. Remove the `fromSymbol` alternative. |
| §5.6 Boundary proof obligations | `:323-337` | Add R3: aliases, branches, records and indirect invocation preserve the applicable contract. Two values of one type and one machine signature can carry different obligations. An unresolved combination is refused or explicitly adapted. |
| §6.1, §6.2 Examples | `:341-396` | Rewrite as extern declarations with descriptors, in the shape Farscape generates (`Farscape/src/Farscape.Core/FidelityCodeGenerator.fs:402-429, 571-597`, and a generated binding such as `Fidelity.Platform/Environments/Linux/x86_64/Bindings/Pthread/Storage/Storage.clef`). The body form waits for open question 9. Use `ofExtern` only where an address is needed, for example a release function passed as a destroy notifier. Keep the caveats about resource contracts (`:343`, `:379`). |
| §7 Normative summary | `:421-431` | Item 3 and any affected item: `ofExtern`, the retirement, the compiler-owned family rule. |

The spec defines no extern declaration form. Neither the `FidelityExtern` attribute nor the
`NativeDefault.zeroed ()` placeholder body that Farscape generates appears in any chapter [R: grep
of `spec/*.md`, and the generated `Storage.clef`]. `platform-bindings.md:340-352` rejects BCL
placeholder bodies, and its own example (`:196-198`) uses another body. The requirement map lists
this gap as S2 with no assigned step. Removing the `fromSymbol` route leaves the extern declaration
as the only route, so the §5.3 and §6 rewrites depend on it. Writing S2's text would add a
requirement no ruling covers, which open question 9 puts to the owner. Until then the §6 examples
name the declaration and its descriptor without fixing a body form.

Ruling R6 needs no new requirement. ffi `:283` and `:321` already require diagnosis before output
and refuse a bare `FnPtr` as a substitute [S]. The spec item records that reading, and Step 1's
draft defers any further R6 text to its own spec prelude (`E/t1/T1-PLAN.md` §2.1).

**S0-S6. The informative checkpoint at ffi `:16`.** Rewrite it to the state after Step 0 and claim
nothing Step 0 does not deliver. Ownership metadata exists, but nothing reads it and no ownership
obligation is implemented (gap-map C2, C3). `fromSymbol` is retired in favor of `ofExtern`, which
is not yet implemented (C7). Native callable ABI publication through Baker and Fidelity.PSG is not
implemented (C1, C8). Farscape refuses nullable callback slots it cannot convert (C5). Whether such
a paragraph belongs in a normative chapter at all is open question 7.

### BAREWire

**S0-W1. The vocabulary.** `src/Descriptors/Bindings.fs:48-51` defines `Transfer` and `:158-159`
makes `Function.cdecl` supply `Borrowed`, under the doc comment "A C-convention function whose
result is borrowed" (`:157`) [R]. Add `Undeclared` with a doc comment carrying the fixed meaning.
Append it as the last case, so existing case order and any tag numbering stay stable [I]. Make
`cdecl` supply `Undeclared` and correct its doc comment. `withTransfer` (`:162-163`) is the only
other ownership helper, and it supplies no default [R]. The file compiles into
`src/BAREWire.fsproj:37`, `src/BAREWire.Fable.fsproj:32` and `src/BAREWire.BindingMetadata.fidproj:12`
[R]. The gate builds the .NET project, BoundaryConformance compiles the Clef one, and
BAREWire's JavaScript gate compiles the Fable one when the post-repair pin includes that gate.

**S0-W2. Tests.** BAREWire's harness has no framework: `tests/Harness.fs` provides `check` and
`equal`, and `tests/Program.fs` calls each module's `run` [R]. A new module needs a
`Compile Include` in `tests/BAREWire.Tests.fsproj` before `Program.fs` and a `run ()` call in
`main`. Checks: `cdecl` yields `Undeclared`, `Undeclared` differs from `Borrowed`, and
`withTransfer` still overrides. The harness has no filter, so every run is unfiltered
(`E/t0/baseline/BAREWire.md`).

**S0-W3. Docs and the consumer survey.** No BAREWire doc describes the `Transfer` cases beyond the
type's comment [R: grep]. No F# code in clef, Alex, Composer, Fidelity.PSG, Bozzetto or Calque
matches on `Transfer`, and nothing outside BAREWire calls `Function.cdecl` [R: grep]. The only
match on `CallerOwns | CalleeOwns | Borrowed` is `Farscape/src/Farscape.Core/PilotSerializer.fs:144`,
over Farscape's own `BindingOwnership` (`PilotTypes.fs:98`), a different type. Repeat the survey at
the post-repair heads and list any hit. Explicit literals in Fidelity.Platform (20 `.clef` files)
and Composer test fixtures (14 lines) stay as they are, which leaves them to Step 8 [R].

### clef

**S0-C1. Codes for the function-pointer refusals.** Apply the sheet in
`src/Compiler/PSGSaturation/SemanticGraph/FunctionPointers.fs`. The `fromSymbol` arm (`:68`)
gets code (a) and a message that names the retirement and `FnPtr.ofExtern` as not yet
implemented. The other arms follow the sheet. `Expressions/Intrinsics.fs:387-391` keeps typing
`fromSymbol`, so admission does not change. Its fallback message at `:407` lists `fromSymbol` as
available and changes accordingly. Add named constants for the new codes to `DiagnosticCodes`
(`Expressions/Types.fs:45`). Change CCS8092's severity only if the sheet assigns the change to the
compiler, which the invariant rules out.

**S0-C2. The fabricated-value code.** `BoundaryValues.fs:150` raises code (c). The message keeps
its content. CCS8010 then remains only on the `null` keyword (`Expressions/Types.fs:453-455`).

**S0-C3. Function-pointer tests.** xUnit, in `tests/Clef.Compiler.Service.Tests/FunctionPointerCases.fs`
(compiled at `Clef.Compiler.Service.Tests.fsproj:36`), as plain `[<Fact>]` or `[<Theory>]` members.

- `fromSymbol` is refused with code (a), and no `FunctionPointerPlan` exists at the site.
- The spec's former spelling `FnPtr.fromSymbol<int -> int> "abs"` is pinned to whichever
  diagnostic fires. Explicit type arguments on a value that is not a type scheme raise CCS8092 at
  `Applications.fs:647` before settlement [R]. Whether the intrinsic's type counts as a scheme
  there is UNKNOWN. Settle it by reading, or assert the expected code and flag the case for the
  gate.
- The untested arms at `FunctionPointers.fs:58` (an `ofFunction` operand that resolves to something
  other than a module function binding) and `:67` (`invoke` on a value not typed `FnPtr`) each get
  a refusal test [R: gap-map C15].

**S0-C4. Calling-convention tests.** A non-CDecl import refusal for `BoundaryRecipes.fs:118`
("Calling convention '{convention}' is not admitted by the scalar C contract"), for example in
`BoundaryEmissionCases.fs`. A non-CDecl callback refusal for `CallbackDeclarations.fs:98-99`
("Native callback declarations currently require CDecl"), for example in `ClosedCallbackCases.fs`.
Neither has a covering test [R: gap-map C15].

**S0-C5. Re-pins.** `BoundaryValueCases.fs:72` pins CCS8010 for zero-initialized forgeries and
moves to code (c). `BoundaryEmissionCases.fs:141` asserts that CCS8010 is absent. After S0-C2 that
assertion can no longer fail, so it moves to code (c) as well. `MatchDecisionCases.fs:291` pins the
`null` pattern and stays at CCS8010 [R].

No `Transfer` match exists in clef [R]. The ClosureValue listener fixture belongs to Phase B.
Confirm that it passes at entry and leave it alone.

### Alex

Read `Alex/AGENTS.md` first. Its test rules apply: a test states a revision from contract values
and references no compiler (`:36`). A refusal test requires the refusal text, no emitted operation
and no bound operand (`:38`). A test whose input is Clef source belongs in Composer (`:39`).

**S0-A1. The registry.** Add the AX codes of sheet item (e) to `AlexErrorCode` and its `format`
function (`src/Alex/Traversal/TransferTypes.fs:115-142`).

**S0-A2. Coded, located refusals.** In `src/Alex/Witnesses/FunctionPointerWitness.fs`, `observe`
turns pattern failures into an uncoded `WitnessOutput.error` (`:16`). Those failures are the root
refusals of `pFunctionAddress` and `pFunctionPointerCall`
(`src/Alex/Patterns/FunctionPointerPatterns.fs:11, 14`).
`:34` and `:44` are further uncoded errors. Replace them with `WitnessOutput.errorCoded` carrying
the node, as `:18` does. The address, indirect-invocation and callback-entry refusals become
distinguishable by code. `:29` is already coded AX4001 with phase "callback declaration".
`src/Alex/Witnesses/PlatformWitness.fs:29` refuses a foreign call without its published row through
the uncoded `WitnessOutput.error` and gets a code too. The live log buried these roots under
AX4001 `pFunctionDef` cascades (`E/research/live-daemon.md`) [A]. No behavior change: the same
nodes refuse and nothing new is emitted.

**S0-A3. Tests.** xUnit, as contract-row tests in a new file under `tests/Alex.Tests/Tests/`.
`Alex.Tests.fsproj:26, 28` compiles every `Tests/*.fs` and `Fixtures/*.fs` when `OnlyTests` is
unset [R], so the unfiltered run includes the file. No function-pointer test class exists at
`4e1f859` (`E/t0/baseline/Alex.md`). Four refusals, each asserting the text, the code, no emitted
operation and no bound operand: the `invoke` refusal, the "callback declaration" refusal, the
foreign call without a row, and the function address. No `Transfer` match exists in Alex [R].

### Composer

**S0-M1. Negative oracles in the NativeCallbacks runner.** `tests/NativeCallbacks/Program.fs:92-93`
checks that each case's retained MLIR contains its expected operations, and `:95` runs
`mlir-opt --verify-each` [R]. Add, for every case in the default list (`:8-38`), that the same
text contains no `unrealized_conversion_cast` and no `llvm` dialect operation (ffi `:140`,
`backend-lowering-architecture.md` §7 items 2 and 5) [S]. The file read there,
`targets/intermediates/10_output.mlir`, is Alex's witnessed text (`src/Core/Witnessing.fs:73-74`)
[R], the right place for a middle-end oracle. Match operations, not every `llvm.` substring, so a
dialect attribute does not trip the oracle. Record a failure under its own phase in
`evidence.json`. If a case that passes at the pin fails the new oracle, the oracle has exposed a
defect: report it and keep the oracle. Alex's MMIO serialization emits `llvm.inttoptr` and
`llvm.load volatile` (`Alex/src/Alex/Dialects/Core/Serialize.fs:501-510`) [R]. No NativeCallbacks
case uses MMIO [I], so the oracle does not meet it there. Whether the spec admits those operations
is an auditor item, not a Step 0 change.

**S0-M2. Stale native-pass claims (gap-map C10).** Replace each with what the post-repair pin
executed, or mark it unverified at that pin.

| Claim | Location [R] | Executed fact |
|---|---|---|
| Listener gate passed, build 28 | `tests/NativeCallbacks/README.md:228` | ListenerEntry failed at compile at the T0 pin [X: `E/t0/baseline/composer-nativecallbacks.failing.txt`, `E/baseline-repair/INVENTORY.md`]. Use the post-repair result. |
| IgnoreValues passed, build 32 | `tests/NativeCallbacks/README.md:233` | IgnoreValues also failed at compile at the T0 pin [X: same sources]. Use the post-repair result. |
| PointerCells checks passed, build 32 | `tests/ForeignReferences/README.md:13` | Never run in any baseline. The driver is Python, and Composer forbids running Python drivers (`AGENTS.md:27`). Mark it unverified. |
| Both gates passed, build 33 | `tests/ForeignScalarArrays/README.md:6` | Never run in any baseline, for the same reason. Mark it unverified. |
| `17_ExternCall` as a Farscape binding sample | `tests/regression/Manifest.toml:992-1001` | Failed at the T0 pin [X: `E/t0/baseline/composer-regression.failing.tsv`]. The draft repair plan skips it until Step 3 (RP-RAWFFI). The manifest's description is not a pass claim and stays. |

Dated waypoint records such as `docs/Language_Coverage_Waypoints.md:2031, 2214` are history and
stay.

**S0-M3. Totality.** No F# code in Composer matches on `Transfer` [R]. Fixture literals stay.

### Farscape

Farscape has no `AGENTS.md` or `CLAUDE.md` [R]. Follow the existing style, and never edit the
owner's three notes files.

**S0-F1. No invented ownership (ruling R5).** `FidelityCodeGenerator.fs:418-421` emits `Borrowed`
with the note "Borrowed is the inferred default" when the pilot declares nothing, and the callback
descriptor at `:591-596` hard-codes `Borrowed` [R]. Both emit `Undeclared` unless a pilot declares
ownership, and the note says ownership is unresolved. Pilot-declared ownership (`:420`) stays.
`CallingConvention = CDecl` stays (gap-map C4 records the CDecl-only vocabulary as a later item).

R5 covers every constructor, and the `FARSCAPE` task named only those two sites. Other generators
also hard-code ownership [R]:

| Site | Literal | Reading |
|---|---|---|
| `ErrnoModuleGenerator.fs:99` (`__errno_location`) | `Borrowed` | Thread-lifetime storage. The vocabulary has no static class (C4), so `Borrowed` is invented and `Undeclared` is the only honest Step 0 value [I]. |
| `TypedSignalGenerator.fs:21` (every signal function record) | `Borrowed` | Undeclared unless a documented contract is cited [I]. |
| `TypedProtocolGenerator.fs:91-93` (`mempcpy` copies) | `Borrowed` | `mempcpy` returns a pointer just past the bytes it copied into its destination argument. Borrowed from that argument has a documented basis, which the generated comment must cite, or the value becomes `Undeclared` [I]. |
| `TypedProtocolGenerator.fs:151-152` (listener descriptor, `wl_proxy_add_listener` returning `int`) | `Borrowed` | The listener entry and an `int` result carry no declared ownership. Scalar-only, so `Undeclared` carries no obligation [I]. |
| `TypedProtocolGenerator.fs:183` (`wl_proxy_marshal_array_flags`) | `CallerOwns` for `new_id` constructors, `CalleeOwns` for destructors, `Borrowed` otherwise | The first two derive from the protocol XML and keep that provenance in a comment. The `Borrowed` fallback is invented [I]. |

The table's readings are inferences for the owner to confirm (open question 5). Tests that pin
the old literal: `tests/Farscape.Tests/ErrnoModuleGeneratorTests.fs:143`. Re-pin it to the new
output, and find any other pin by grep.

**S0-F2. Callback slots (ruling R6) with the legacy-C accommodation.** Today a top-level
function-pointer parameter becomes a bare `FnPtr` whether it is unannotated, `_Null_unspecified` or
`_Nonnull`, because `Option` wrapping applies to data pointers only
(`FidelityCodeGenerator.fs:494-495`). Tests pin that narrowing
(`FidelityCodeGeneratorTests.fs:300-319`) [R]. Results already follow the ruling: a
function-pointer result without explicit nonnull evidence is refused (`:509-511`) [R]. Parameters
follow the same rule.

- An unannotated or `_Null_unspecified` slot is `Option<FnPtr>` by ffi §5.2 (`:274`). The
  optional-entry conversion is unsupported, so the binding is refused before any output is written
  (`:283`, `:321`) [S]. The refusal uses the generator's existing pre-write path, where
  `BindingGenerator.generateFromProject` (`BindingGenerator.fs:900`) returns `Error`. The
  conformance helper `rejectedGeneration` (`tests/BoundaryConformance/Program.fs:72-88`) shows the
  sentinel check that existing output survives.
- Explicit evidence narrows to `FnPtr`: a clang `NonNullAttr` (`:443-446`), an outer `_Nonnull`
  (`:491`), a pilot `[annotations.nonnull]` index (`:448-452`) or a pilot `nonnull_callbacks`
  entry (`PilotTypes.fs:110`, read at `:462` and `:486-489`) [R].
- A pilot declaration taken from a documented contract is legitimate evidence, under the owner's
  principle on legacy C (rulings record, "The spec is primary"). The generated binding records its
  provenance (the pilot file, the key and the contract it cites) and labels the reliance as an
  external execution hypothesis (ffi §5.1 `:267`, §5.4 `:301`, `conformance.md` §6.1 item 5) [S].
- A comment never narrows, whether in the header or in generated output (R6). Narrowing to a
  supplied entry where C permits absence is a restriction that §5.5 `:321` requires the binding to
  document [S].

The pilot format has no field for the cited contract: `NonnullCallbacks` is an `int list`
(`PilotTypes.fs:110`) [R]. Adding one is a Farscape pilot-format change, and what happens to a
declaration without a citation is an owner decision (open question 4).

Tests, in xUnit in `FidelityCodeGeneratorTests.fs`: replace `:300-307` and the theory at `:309-319`.
Unannotated and `_Null_unspecified` slots are refused before output, `_Nonnull` narrows, a pilot
declaration narrows and its provenance appears in the output, and documentation text claiming
nonnull does not narrow.

BoundaryConformance: the `generated-opaque-contracts` gate (`Program.fs:294-313`) inspects
`token_visit`'s callback, which the conformance pilot declares nonnull (`boundary.pilot.toml:23-25`).
The fixture header states that the pilot supplies explicit assertions (`boundary.h:15`) [R]. The
gate keeps that evidence and also asserts the recorded provenance. Add an expected-refusal gate:
the same header without the `nonnull_callbacks` declaration is refused before writing output, by
`rejectedGeneration`. Never weaken a gate to keep it green, and record each gate change with its
reason.

Docs that state the old narrowing: `docs/08_Nullable_Pointer_Architecture.md:84-89`,
`docs/10_Boundary_Marshaling_Spec.md:138`, `docs/14_Binding_Generation_Gaps.md:494` (notes tracker
item 12) and the generator's own doc comment at `FidelityCodeGenerator.fs:432-434` [R].
Fidelity.Platform's `pthread.pilot.toml:23-24` declares `nonnull_callbacks = [2]` for
`pthread_create` with a comment. POSIX documents that argument as the thread's start routine, a
documented contract of the kind the accommodation admits [I]. It is regenerated in Step 8, not
edited here.

**S0-F3. Unknown pilot keys as warnings (critique minor 6).** `PilotSerializer.deserialize`
(`:689-691`) returns `Result<PilotProject, string>` over Fidelity.Data's TOML reader and has no
warning channel [R]. Add one that names the table and key of every unread key and leaves the
generation result unchanged. An error would break the conformance pilot, which sets
`opaque_handles = true` (`boundary.pilot.toml:12`). `docs/07_Pilot_Project_Setup.md:130-134` already
says that key was never read and asks that pilots drop it. Remove it from the conformance pilot in
the same change and record that fixture edit. `transitive_headers` is documented as a working key
(`docs/07_Pilot_Project_Setup.md:38, 48, 291-317`) although no source reads it [R]. Farscape
`4bb4567` (2026-03-04, "remove transitive headers; automate traversal/discovery") removed it [H].
Correct docs/07 to say so. Fidelity.Platform pilots that carry either key (`libgbm.pilot.toml:11`,
`libc.pilot.toml:35`, `wayland.pilot.toml:5, 25`, `libdrm.pilot.toml:5, 13`,
`wayland.native.pilot.toml:5`) will warn, and Step 8 cleans them [R]. Tests: an unknown key yields
one warning naming it, a pilot with only known keys yields none, and the generated output matches
the output without the key.

**S0-F4. Conformance re-pins for clef's code change.** Four gates pin CCS8010 for fabricated values
(`tests/BoundaryConformance/Program.fs:356-395`), and `:349` requires CCS8010's absence for a
declaration-only placeholder [R]. Move all five to code (c) with S0-C2. Missing `:349` would leave a
check that cannot fail.

**S0-F5. The checkpoint correction.** In `docs/handoff/2026-10-03-ffi-boundary-checkpoint.md`, add a
dated "Takeover correction (2026-10-03)" section near the top. It corrects the overstated lines per
gap-map §2 without rewriting the record [R: line numbers at `5ec1954`]:

| Lines | Claim | Correction |
|---|---|---|
| `:20-22`, `:58` | Unannotated pointers default to option, unknown nested slots refused | Top-level callback slots were narrowed to bare `FnPtr` (C5). Step 0 refuses them unless explicit evidence narrows them. |
| `:27-29` | Complete native signature descriptors under the selected ABI model | The vocabulary has no per-level nullability, environment, static class or variadic flag. The model is LP64 data and the convention is always CDecl (C4). |
| `:33` | Ownership metadata remains an explicit contract | Undeclared ownership was emitted as `Borrowed` (C3). Step 0 emits `Undeclared`. |
| `:51`, `:64-66` | The repair belongs to Baker rows and PSG publication | Correct, and understated: six gaps across four seams (C1). |
| `:52` | Metadata and obligations retained | Metadata only, unread, with no obligation (C2). |

Also state C8 (uncoded Alex refusals, publication not gated on `FnPtr` findings), C10 (stale
Composer claims), C15 (untested refusals) as they apply, and what Step 0 changed: `Undeclared`
ownership, the callback-slot refusal and the pilot-key warning. Accurate rows (C13, C14) stay as
written.

**S0-F6. The remaining vestiges (notes tracker list).** `E/notes/notes-final.md` lists 17 items
verified at `5ec1954`. Items 1 to 10 and 15 are documentation or comment corrections in the vestige
family of the owner's notes. Item 11 is a citation of Composer `C-01-Closures.md` "§10.8", a section
that does not exist at `fcd68d6` (`docs/10_Boundary_Marshaling_Spec.md:100`) [A]. Item 14 covers
raw-pointer vocabulary in 20 docs, each to be read before changing, since some describe forbidden
forms. These go in their own Farscape commit and change no code. Item 12 is part of S0-F2. Item 13
(`run.py`) waits for its .NET replacement. Item 16 belongs to Fidelity.Platform in Step 8. Item 17
applies only if the owner retires inline signatures. Open question 8 asks whether item 14 stays in
Step 0.

The same pass corrects `docs/14_Binding_Generation_Gaps.md:340-345`, which the tracker list
does not cite. It quotes a memory file's claim that callback fields use `FnPtr` "resolved via
`FnPtr.fromSymbol`" and answers that `FnPtr` is never emitted [R]. Both halves are stale: the
generator emits `FnPtr` entry records (`FidelityCodeGenerator.fs:585`), and ruling R2 retires
`fromSymbol`.

## Acceptance protocol

The gate follows the guide's working rules and `REPAIR-PLAN.md` §2.3 as corrected by
`repair-review.md`.

1. **Quiescence and scope.** No edit is in progress. `git status --short` in every chain
   repository shows only files from the ownership table. Fidelity.PSG, Fidelity.Platform, Calque,
   Bozzetto, Fidelity.Data and Fidelity.FSharp.Incremental show no change.
2. **Builds** under `acquire_full_build_lease`, in dependency order: BAREWire, Fidelity.PSG, clef,
   Alex, Composer, Farscape. Fidelity.PSG rebuilds because it references BAREWire
   (`Fidelity.PSG/Directory.Build.props:6`, `src/Fidelity.PSG/Fidelity.PSG.fsproj:52`) [R]. Release
   with `release_work_lease` when the builds end, also on failure. Record each artifact's sha256.
3. **Suites**, one `acquire_test_suite_lease` per suite. The observed lease lasted 15 minutes
   with no renewal [X: `E/t0/baseline/logs/lease.log`, granted 12:56:14 EDT, expiring 17:11:14 UTC].
   Never run past expiry. The T0 driver's suite 07 overran its lease by 8 min 50 s
   (`E/baseline-repair/INVENTORY.md`, hazards) [A]. A suite that cannot finish within one lease
   follows the rule the baseline document sets.
4. **Commands.** Use the exact commands recorded at the T0 pin (`E/t0/baseline/<repo>.md`,
   `build-all.sh`, `test-all.sh`) [X], listed in the gate command table after this list. Where
   the post-repair pin changed a command, use the pin's.
5. **Comparison with the post-repair pin.**
   - Each failing set equals the pin's, with no new names. The pin's failing sets hold only the
     pending entries the baseline document accepts, if it accepts any.
   - Each total equals the pin's plus the new tests, and every new test name appears in the
     unfiltered output: the TRX, the harness count, the runner's case list or the conformance gate
     list.
   - Every re-pinned test (S0-C5, S0-F1, S0-F4) passes on its new code or output.
   - NativeCallbacks per-case outcomes in `evidence.json` equal the pin's. A case that newly fails
     only on the S0-M1 oracle is an exposed defect, reported with its MLIR line.
   - Regression per-sample outcomes equal the pin's. Per-sample diagnostic multisets equal the
     pin's after the code mapping of the sheet.
   - BoundaryConformance passes the pin's gates, with the CCS8010 gates now on code (c), plus the
     new gates.
   - Fidelity.PSG's suite equals the pin, and `Revision.Schema` and `BinaryGenerated.Fingerprint`
     are unchanged.
6. **Record.** File the evidence record the guide defines: heads, commands, lease ids, artifact
   hashes, totals, failing sets compared with the pin, new test names as shown in the output,
   graded claims and residual risks. Each repository gets one proposed commit line.

The shared daemon's `composer_*` path is not a Step 0 gate. Bozzetto builds against a Composer
distribution named by an absolute path (`Bozzetto/build/ComposerContract.props:13-18`) [R], and
any daemon or worker change belongs to the Bozzetto track.

### Gate commands

Each command runs from its repository's root unless it changes directory first. `<dir>` is a
results folder filed with the Step 0 evidence record.

| Suite | Build | Test |
|---|---|---|
| BAREWire harness | `cd /home/hhh/repos/BAREWire && dotnet build tests/BAREWire.Tests.fsproj --disable-build-servers`. The JavaScript gate (Fable) runs too if the post-repair pin includes it. The T0 pin did not (`E/t0/baseline/BAREWire.md`). | `dotnet run --project tests/BAREWire.Tests.fsproj --no-build` |
| Fidelity.PSG | `dotnet build src/Fidelity.PSG/Fidelity.PSG.fsproj --disable-build-servers`, `dotnet build tests/Fidelity.PSG.Tests/Fidelity.PSG.Tests.fsproj --disable-build-servers` | `dotnet test tests/Fidelity.PSG.Tests/Fidelity.PSG.Tests.fsproj --no-build --logger "trx;LogFileName=psg.trx" --results-directory <dir>` |
| clef | `cd /home/hhh/repos/clef && dotnet build Clef.Compiler.Service.sln --disable-build-servers` | `dotnet test tests/Clef.Compiler.Service.Tests/Clef.Compiler.Service.Tests.fsproj --no-build --logger "trx;LogFileName=clef.trx" --results-directory <dir>` |
| Alex | `dotnet build src/Alex/Alex.fsproj --disable-build-servers`, `dotnet build tests/Alex.Tests/Alex.Tests.fsproj --disable-build-servers` | `dotnet test tests/Alex.Tests/Alex.Tests.fsproj --no-build --logger "trx;LogFileName=alex.trx" --results-directory <dir>`, never with `-p:OnlyTests` |
| Composer `tests/Alex.Tests` | `dotnet build src/Composer.fsproj --disable-build-servers`, `dotnet build tests/Alex.Tests/Alex.Tests.fsproj --disable-build-servers` | `dotnet test tests/Alex.Tests/Alex.Tests.fsproj --no-build --logger "trx;LogFileName=composer-alex.trx" --results-directory <dir>` |
| Composer NativeCallbacks | `dotnet build tests/NativeCallbacks/NativeCallbacks.Tests.fsproj --disable-build-servers` | `dotnet run --project tests/NativeCallbacks/NativeCallbacks.Tests.fsproj --no-build`, with no case names |
| Composer regression | Uses the Composer `src` build | `cd tests/regression && dotnet fsi Runner.fsx` at the T0 pin, with no `--sample`. `REPAIR-PLAN.md` §2.3 proposes `-- --jobs 2 --timeout 360` for its gates (RP-TIMEOUT). Use whatever the post-repair pin used. |
| Farscape tests | `cd /home/hhh/repos/Farscape && dotnet build tests/Farscape.Tests/Farscape.Tests.fsproj --disable-build-servers` | `dotnet test tests/Farscape.Tests/Farscape.Tests.fsproj --no-build --no-restore --logger "trx;LogFileName=farscape.trx" --results-directory <dir>` |
| Farscape BoundaryConformance | `dotnet build tests/BoundaryConformance/BoundaryConformance.fsproj --disable-build-servers` | `dotnet run --no-build --project tests/BoundaryConformance/BoundaryConformance.fsproj`, with no gate names |
| clef-lang-spec | none | Link and anchor check over the touched chapters |

## Risks

- **A code change crossing repositories.** S0-C2 changes a code that clef tests and Farscape gates
  both pin. Two of those pins assert absence (`BoundaryEmissionCases.fs:141`,
  `BoundaryConformance/Program.fs:349`). A missed re-pin leaves a check that passes without
  checking anything.
- **CCS8092.** Choosing the compiler side of the severity mismatch would change admission.
- **A new union case.** No F# match over `Transfer` exists at the pinned heads, but the repair
  changes many files. Repeat the grep. Appending the case keeps tags stable for the Fable and Clef
  compilations [I].
- **Generated consumers.** Generated `.clef` that writes `Undeclared` needs a BAREWire
  BindingMetadata that has the case. Fidelity.Platform's checked-in bindings keep explicit literals
  until Step 8.
- **Refusal breadth in Farscape.** Regenerating Fidelity.Platform pilots after S0-F2 will refuse
  every unannotated callback slot that lacks a pilot declaration. The count is UNKNOWN until a Step
  8 survey.
- **Oracle precision.** A substring check for `llvm.` would trip on dialect attributes. The oracle
  matches operations.
- **Spec ahead of code.** S0-S5 specifies `ofExtern` before Step 1-B implements it. S0-S6 states
  that, and the `fromSymbol` message names it as not yet implemented.
- **Shared files with Phase B.** `ntu-types.md`, `error-handling.md`, the NativeCallbacks runner and
  `FunctionPointers.fs` all change in Phase B first. Start from the post-repair text and
  re-locate every citation.
- **Leases.** A suite that overruns its 15-minute lease, or a detached driver that keeps one,
  blocks other callers. Never poll for a lease, and on a memory refusal measure and report
  (guide, working rules).
- **The filtered-run trap.** Alex's `-p:OnlyTests`, NativeCallbacks case names, the regression
  runner's `--sample`, conformance gate names and any `dotnet test --filter` all narrow a run. None
  of them is acceptance.

## Auditor checkpoint

The auditor signs Step 0 off in the tracker after checking each item against the evidence record.

1. The allocation sheet predates every use of a new number. Each code raised in clef and Alex
   matches its row. CCS8010 is raised only for the `null` keyword (grep of raise sites), and
   CCS8096's row covers every remaining raise.
2. Each spec amendment traces to R1 to R4, the retirement or the `Undeclared` vocabulary. Links
   resolve. The ffi `:16` paragraph claims nothing Step 0 did not deliver.
3. Fidelity.PSG is untouched, with schema and fingerprint equal to the pin.
4. Refusal sets and diagnostic multisets equal the pin's after the mapping.
5. Alex's new tests meet `AGENTS.md:38`, and none takes Clef source as input.
6. `Undeclared`'s doc comment carries the fixed meaning, `cdecl` supplies it, and the harness count
   includes the new checks.
7. No Farscape generator constructor invents ownership. Every remaining literal cites its source in
   the generated comment.
8. Farscape refuses unconvertible callback slots before output, with the sentinel preserved. Pilot
   narrowing records provenance as an external execution hypothesis, and no comment narrows.
9. Unknown pilot keys warn without changing output. Every conformance gate change carries its
   reason.
10. The checkpoint correction is dated, leaves the original text, and matches gap-map §2.
11. The NativeCallbacks oracles run on every default case. Each stale Composer claim is replaced by
    an executed fact or marked unverified.
12. Every new test name appears in an unfiltered run, and no new gate depends on a filter.
13. The owner's three Farscape notes files are absent from the Step 0 diff.
14. Alex's MMIO serialization emits `llvm` dialect operations (`Serialize.fs:501-510`). The auditor
    records whether `backend-lowering-architecture.md` §2.1.1 admits them, for a later step.

## Open questions for the owner

1. **When codes are allocated.** The repair plan forbids new codes in Phase B because T0 allocates
   them (`REPAIR-PLAN.md` §2.4). D6(b) enlarges Phase B with a callable-aggregate foundation that
   may need its own refusals. S0-S1 is spec-only and could run as a prelude before Phase B.
   Otherwise Phase B uses existing codes and Step 0 re-codes.
2. **The CCS8096 arms.** Widen CCS8096's row to cover the `ofFunction` and `invoke` refusals, or
   give them code (b).
3. **CCS8092.** Correct the table to Error, which preserves admission, or make the compiler warn,
   which is an admission change for a later step.
4. **Pilot provenance.** Should a `nonnull_callbacks` or `[annotations.nonnull]` declaration require
   a citation of its documented contract? Is a declaration without one accepted with a warning or
   refused?
5. **The other ownership literals.** Confirm the readings in the S0-F1 table, in particular whether
   the Wayland `new_id` and destructor derivations count as declared evidence.
6. **The spec change record.** `change-process-management.md` §5.1 requires an RFC to amend a
   normative section of a released specification [S]. Recent amendments were committed directly
   (for example `232482b`) [H]. Does the rulings record serve as the recorded step, or does the
   owner want an RFC?
7. **The ffi `:16` checkpoint.** `change-process-management.md` §2 places implementation milestones
   in implementation records, outside normative chapters [S]. Keep the corrected paragraph, or move
   it to an implementation record?
8. **Vestige scope.** Item 14 of the notes tracker covers 20 docs. Keep it in Step 0 as its own
   commit, or schedule it later?
9. **The extern declaration form (S2).** Specify the `[<FidelityExtern>]` declaration and its
   placeholder body in Step 0, so that §5.3 and the §6 examples rest on a defined form, or leave it
   to Step 1-B, which depends on it, and keep the Step 0 examples form-neutral?
