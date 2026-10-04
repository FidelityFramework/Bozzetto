# Callable aggregate correction: auditor return

Return for the [implementer handoff](FFI_Correction_Auditor_Handoff_2026-10-04.md),
October 4, 2026. Grades: **E** executed in this audit, **R** source read at the
pinned heads, **S** normative spec, **A** agent report, **I** inference.

## Verdict

**Accepted with bounded follow-up.** The batch's gates reproduce exactly from
the pushed commits, with no regression against either baseline. W1-W4 do what
the handoff says. Four follow-up items (R1-R4) are required before the next
tranche builds on this foundation: before multi-alternative selectors,
environment-carrying components, absent-union publication, or native lowering
are claimed. None is reachable by source the producers emit today. R1 refuses
positive programs and fails closed. R2 and R3 are checks that pass malformed
rows the producers do not currently emit.

**Reviewed heads (all pushed, all clean):**

| Repository | Head |
| --- | --- |
| Fidelity.PSG | `3d2fa758294a67ab96bcd102ff83da59dd7fb8f8` |
| clef | `46d31a905ebb216c63e6d769b27acb682fb0a7c5` |
| Alex | `9644c1c5aec2dca497a5e9d04d9d2707c1e2be8a` |
| Composer | `beb794234fadb3f236c213ccf7eb09702d19c59a` |
| clef-lang-spec | `fb7541db51842f957fc51ceb83b89ef93285f4fb` |

Audit evidence is under
`/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/auditor-reproduction-2026-10-04/`
(below, `R/`). The implementer's evidence `E/` is unchanged.

## Executed reproduction (E)

The auditor rebuilt all four test projects from the pushed commits under a
full-build lease. Each suite then ran unfiltered under its own test lease. All
leases were released.

| Suite | Auditor result | Versus implementer | Versus baseline |
| --- | --- | --- | --- |
| Clef | 2,430 run: 2,349 passed, 81 failed | identical outcome multiset | zero missing, 14 added, zero new failures |
| Fidelity.PSG | 376/376 | identical | n/a |
| Alex | 287/287 | identical | n/a |
| Composer | 402 run: 401 passed, 1 failed | identical | zero missing, 1 added, zero new failures |

TRX files: `R/results/auditor-{clef,psg,alex,composer}.trx`. Comparisons:
`R/compare-*.json`. The implementer's source manifests verify against the
committed heads, and every file changed between the tested base and the commit
is covered.

**Evidence-method note (R/E).** Rebuilt binaries embed the commit hash in their
informational version (`+46d31a90…`). The implementer's artifact hashes were
taken before the commit, so they cannot verify after any rebuild. The auditor's
run tested binaries built from the committed heads, which is the stronger
binding. Future handoffs should hash artifacts built from the committed head.

## Accepted

- **Spec amendment (S/R).** FFI §3.4 at `fb7541d` agrees with ruling Q1. It
  allows a portable interior convention from declared target capabilities. It
  requires agreement across every origin and indirect invocation, explicit
  identity conversions, and code-lifetime evidence naming the declaration and
  its program-image residence. Foreign crossings stay refused until a foreign
  contract and native ABI exist.
- **W1 census (R, E).** Admission still requires a capture-free lambda and
  excludes implementations with an environment formal
  (`CallableCarriers.fs:281-294`). Formations do not merge. The recipe keeps its
  occurrence/formation pairing check with no fallback
  (`CallableAggregateRecipes.fs:44-50`). The record and `Result` positive
  programs compile clean in an auditor probe (`R/probes/aggregate-probes.log`).
- **W2 support and renewal (R, E).** `validate` allocates nothing. Accounts
  re-observe `sourcePremisesCurrent`. Witness preparation re-derives carriers.
  **Positive control executed:** disabling the premise-currency check
  (`CallableAggregateAccounts.fs:82-84`) makes three tests fail: both
  `BoundaryValueCases.Same identity native residence corruption…` cases and
  `CallableAggregateCases.Changed numeric source support…`
  (`R/results/mut-E1b-clef-premise-currency.trx`).
- **Correction to the previous return.** Its W2 hypothesis (dormant contracts)
  was wrong. The implementer's probe found non-executable numeric premises, and
  the repair follows the probe.
- **W3 compiler-owned entries (R).** `ProgramImageCodeLifetime` names module,
  binding and implementation (`Types.fs:2312`). Only `FnPtr.ofFunction` of an
  immutable, capture-free, non-extern module binding qualifies
  (`CompilerOwnedEntries.fs:31-47, 84`). It is structural evidence, not an SMT
  lifetime or ownership default. Zero component bytes and the standalone
  `PlatformWord` assertion both hold. Negative controls cover missing, forged and
  extern lifetimes and membership corruption.
- **Hardware/kernel shells (R).** They are excluded by `DeclRoot` source role,
  not by type name (`CallableOrigins.fs:765-773`). Ordinary references still
  require origins.
- **W4 consumer fixes (R).** The removed numeric replay was a test-only repair.
  Production runs the numeric owner once (`NativeService.fs:1394`).
- **PSG reader (R).** These hold:
  - the Startup member-prefix and attached-suffix decoding, with negatives for
    attached-only, membership, parent and capture mutations;
  - the embedded program-carrier rule and its seven controls;
  - I4 kind, signature and representation checks;
  - strict body/formal presence and exact premise ownership;
  - I6 full account recomputation and totality.
- **Alex (R).** No code value is stored as data. The selector domain is exact.
  Code and environment come from the same arm. Reconstruction is row-only, and
  absent payloads have no selector.
- **Dependency identity (R).** The public account compares full rows
  structurally. The private scalar fingerprint strips only premises, and only on
  local copies. Relation identities are admitted only through their validated
  tables. Reuse authorizes code objects only; source, MLIR and artifact proofs
  stay fresh.

## Required follow-up

### R1. Contract renewal is not folded back into aggregate rows (significant, E)

- **Owner:** `clef/src/Compiler/Nanopass/CallableAggregateSettlement.fs` with
  `Baker/Recipes/CallableContractRecipes.fs`.
- **Clause:** Closure §2.4, "Every representation rewrite preserves or
  recomputes the support it changes"; handoff priority 4.
- **What happens (R):**
  - Aggregate rows are computed from pre-renewal contracts (`:24-28`).
  - `renewForSites` then runs (`:39-47`) and gives any changed signature a
    fresh contract identity (`CallableContractRecipes.fs:24`).
  - `renewAccounts` (`:68-73`) refuses every row that still cites the old
    identity.
- **Reproduction (E),** `R/probes/aggregate-probes.log`:

  ```fsharp
  type Holder = { Apply: bool -> bool }
  type Outer = { Run: Holder -> bool }
  let first (v: bool) = v
  let run (h: Holder) = h.Apply true
  let main _ = let outer = { Run = run } in if outer.Run { Apply = first } then 0 else 1
  ```

  - This yields 3× CCS8413 and 4× CCS8414: "Callable aggregate formation or
    contract changed after placement; its owning source fold must settle it
    again."
  - The same `run` passed directly, not stored, compiles clean.
  - This is distinct from the known nested-path residual: a record nested in a
    record gives "The nested callable path still requires its source-owned
    aggregate formation protocol", which remains scheduled work.
- **Correction:**
  - After `renewForSites`, re-fold the aggregate recipe for exactly the slots
    whose carriers' contract identity changed, reusing the previous slot
    identities.
  - Repeat to a fixpoint bounded by aggregate-type dependency depth.
  - Do not loosen `renewAccounts`.
- **Exit:**
  - The source above becomes a positive Clef control that publishes with no
    errors, with a stable `Outer` slot identity and unchanged unrelated contract
    identities.
  - Add a direct `renewForSites` control: an unchanged signature in the change
    set keeps its identity, a changed one gets a fresh identity, and unrelated
    contract and carrier rows are byte-identical.

### R2. The PSG reader accepts malformed aggregate rows (significant, E)

- **Owner:** `Fidelity.PSG/src/Fidelity.PSG/CallableAggregateIntegrity.fs`.
- **Reproduction (E).** The auditor added each control to a scratch clone,
  never committed (`R/probes/psg-controls.patch`, `R/probes/psg-controls.log`).
  Each produced **zero violations**, so the reader accepted it.

| Control | Malformed row | Reader lines | Clause |
| --- | --- | --- | --- |
| C1 (record and union) | `SelectedAlternative` names carrier 31 while the row writes carrier 30 | 275-284 | §2.4 pairing; I2/I3 |
| C2 | A present `Some f` payload published with zero alternatives and a payload-less tag | 256-258, 341-345 | §2.4 "A missing union payload has neither an alternative nor a selector", which applies only to a genuinely absent payload |
| C3 | Contract 100 cites contract 102's source premise | 32-41, 403-412 | I5 ownership; I6 then misses 102's changes |
| C4 | Closed singleton with 8 bytes of `InlineBytes(8,8)` component data | 120-126, 239-244; `Adaptation` unread at 309-322 | §2.4 "occupies no component storage"; I1 |
| C5 | An unrelated carrier with the slot's contract accepted as an adapter | 300-308 | I4; adapters are Baker's and not yet emitted |
| C6 | Row value 30 while the live `RecordExpr` field holds 31 | no comparison | withdrawal; §2.4 |

- **Not reachable today.** The producers refuse or never emit these shapes
  (`CallableAggregateRecipes.fs:68-69, 90-91, 131-139`). But the reader is the
  independent public authority, and the next tranche emits more of these forms.
- **Latent, not executed:** the environment value is never checked against the
  live `ClosureValue` formation (C7).
- **C9 was inconclusive:** that fixture fails the full `Integrity.check` for
  unrelated synthetic-fixture reasons.
- **Correction:**
  1. Require `Value = Alternatives[SelectedAlternative].Carrier` and a selection
     on every Construct or Assign row with alternatives.
  2. Admit zero alternatives only for a union path whose tag names a different
     case, compared against the live constructor's payload presence; forbid a
     tag on record paths.
  3. Require contract-held participants to be grouped under that contract.
  4. Recompute the expected component data from selector and placements, with
     exact equality and `Adaptation = None`.
  5. Refuse `Adapter = Some _` until a typed adapter relation exists.
  6. Compare row values with live constructor operands.
- **Exit:**
  - Each control in the patch, inverted to assert refusal, fails with its
    specific rule and reason.
  - Tighten the test helper `failure` so it asserts the reason, not only the
    rule family (`CallableAggregateTests.fs:10-11`).

### R3. Alex does not bind rows to the operation, and its tests do not discriminate (significant, E)

- **Owner:** Alex, `src/Alex/Patterns/CallableAggregatePatterns.fs` (CAP) and
  the record/union witnesses.
- **Mutations (E).** Each mutation below left all **287/287** Alex tests passing
  with an outcome multiset identical to the clean run
  (`R/results/mut-M{1,2,3}-*.trx`):
  - **M1:** remove the native-entry refusal (CAP:66-67);
  - **M2:** always store selector `0` (CAP, `pConstI (name 3)`);
  - **M3:** swap selection arms in `pSelect`.
- **Operation binding (R; reachability I).** The witness checks `row.Aggregate`
  but never the field, case or operand:
  - `RecordWitness.fs:113` discards the field name;
  - `RecordWitness.fs:95` discards the value;
  - `DUWitness.fs:59` discards the case index;
  - `pReadComponent` never reads `row.Tag` (CAP:283-358).

  Swapped slot/value rows, or a mismatched eliminated case, would emit the
  wrong implementation. This violates §2.4 "Union payload selection
  additionally requires current constructor and tag evidence" and the ruling
  "Alex witnesses the settled operation."
- **Native refusal (R).**
  - The refusal is uncoded for `RecordExpr`/`DUConstruct` (`DUWitness.fs:112`).
  - Its message claims a missing contract that schema 18 now publishes.
  - The write path validates only the selected alternative (CAP:195).
- **Fixtures (R).**
  - The two alternatives share an implementation and an offset
    (`CallableAggregatePatternTests.fs:174-273`).
  - Fixtures never pass `Integrity.check`; line 326 uses a `DUInitialize` node
    as tag constructor.
- **Correction:**
  - Pass node operands into the patterns. Require the slot path to name the
    witnessed field or case, the row value to equal the operand, and the tag to
    agree with the constructor and case index.
  - Validate every alternative on write.
  - Give native refusal its own code and an accurate message.
- **Exit:**
  - M1-M3 each make at least one test fail.
  - Negative fixtures cover each binding mismatch, refused with no ops emitted.
  - Fixtures pass `Integrity.check`.
  - A Composer source control compiles the record and `Result` aggregate
    programs through Alex and asserts `func.constant @first` and no stored
    function value.

### R4. The scalar fingerprint's contract interface is untested and omits native entries (minor; required before native lowering; E)

- **Mutation (E).** Dropping contracts from the fingerprint scope
  (`WitnessSegmentation.fs:368`) left all 2,430 Clef outcomes identical
  (`R/results/mut-E2-clef-fingerprint-contracts.trx`).
- **Native entries (R).** Only `OrdinaryFlatClosure` contracts enter the
  fingerprint (`WitnessSegmentation.fs:330-336`), so other contracts are
  dropped silently. The handoff's claim that reusable code fingerprints
  include the settled contract interface does not hold for native entries.
- **No unsound reuse today (R):** Alex scalar emission does not read contracts.
- **Correction:** encode the native interface, or refuse the candidate.
- **Exit:** two controls. A support-only change keeps the fingerprint; an
  interface-only change alters it or removes the region.

## Minor items

| Item | Grade | Where | Action |
| --- | --- | --- | --- |
| The account subset check compares `expected` with itself, so it can never fail | R | `CallableAggregateSettlement.fs:66, 74` | Derive sites from held accounts, or delete the clause |
| Changed-support assertions pass because numeric nodes are re-minted on every renewal | R | `CallableAggregateCases.fs:157, 166, 172`; `NumericSettlement.fs:30-38` | Add a no-op renewal baseline |
| Kernel `Compute` bindings are not distinguished for program-image residence | I | `CompilerOwnedEntries.fs:31-47` | Probe a `FnPtr.ofFunction` of a kernel binding in a record; expect refusal |
| Contract premises are not compared with published `NumericDomain.Premises` | R | PSG `CallableAggregateIntegrity.fs:32-36, 369-372` | Compare per premise |
| Symbol-only implementations are absent from the account | R | PSG `:363-373` | Include the symbol fact |
| `settleCallableComponent` lacks the held-contract guard and replays owners | R | Composer `tests/Alex.Tests/Fixtures.fs:80`, `PublicationRefusalTests.fs:524` | Add the guard |
| Foreign-graph negative still reverses edges and asserts no reason | R | Composer `ProgramValuePatternTests.fs:260` | Mirror the `ProgramSequencePatternTests` fix |
| `FnPtr.invoke` never consults the contract; two layout authorities disagree | R | `FunctionPointers.fs:68`; `Placement.fs:150` vs `TypeLayout.layoutOf` | Resolve before native operations |
| Families without a unique signature are skipped silently | R | `CallableContracts.fs:233-247` | Refuse or record a residual |
| `normalize` re-mints every numeric node | R | `NumericSettlement.fs:30-31` | Constraint for any differential re-run |
| Revision-mismatch receipt guard is never exercised | R | Composer `IncrementalBuildTests.fs:634-682` fails at baseline CCS8414 first | Keep visible until that baseline is cleared |
| The selector's final arm is an unconditional else | R/S | CAP:256-281 | Compare the final arm; unreachable default |

## Order of work for the implementer

1. **R1** first. It refuses positive programs, and its fix touches the same
   owners as the renewal controls.
2. **R2** with the supplied patch as the starting controls.
3. **R3**, then the Composer source control through Alex.
4. **R4** before any native lowering claim.
5. **Gates.** Run the three suites unfiltered against the trusted baselines. Add
   the R1 source and the R2/R3 controls to the default suites, not as filtered
   gates.

## Audit conduct

- **No repository was edited, committed or pushed by the auditor.** Mutation
  and control probes ran in local clones under the audit evidence folder, which
  were deleted afterwards. Their source changes are retained only as patches,
  scripts and TRX files.
- **One side effect, repaired.** An early clone layout symlinked BAREWire and
  Fidelity.Data. Restoring through those links rewrote git-ignored restore
  metadata in the real repositories (`obj/project.assets.json`, the NuGet cache
  and dependency spec, and build file lists). The auditor restored both
  projects from their real paths and rebuilt from the real PSG checkout. No
  workspace path remains, and all BAREWire and Fidelity.Data DLL hashes are
  unchanged (`R/sibling-dlls-before-repair.sha256`, `R/sibling-repair.log`).
  The workspace was then rebuilt from genuine clones.
- **Leases.** Every build and test ran under a Bozzetto lease, and every lease
  was released (`R/*-lease-acquire.txt`, `R/*-release.txt`).
