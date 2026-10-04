# Callable aggregate follow-up: auditor return

Return for the [follow-up handoff](FFI_Correction_Followup_Handoff_2026-10-04.md),
October 4, 2026. Grades: **E** executed in this audit, **R** source read at the
pinned heads, **S** spec, **I** inference.

## Verdict

**Changes required: one focused correction, then accept.**

- R1, R3 and R4 meet their exit conditions. R2 meets its controls but
  introduced a regression. The new public write rule refuses valid source
  programs that store a function in a tuple or keep one through a record
  copy-update (B1, executed).
- Every suite reproduces exactly, so no existing test exposes this.
- The remaining findings are latent reader gaps, weak controls and
  documentation. They are listed for the next increment and do not block it.

**Reviewed heads (pushed, clean):**

| Repository | Head |
| --- | --- |
| Fidelity.PSG | `bb062376fa4f13763246997feeb86ca48fe71989` |
| clef | `f23e33657fc73c4b99e29df01851bb80f6e0137d` |
| Alex | `efd1a008ce5d34a8cf4585f98faa269de112234a` |
| Composer | `0caa67d85533d69b23acf7f2b2fb2ab9fb71dd8d` |
| clef-lang-spec | `54ef3690c840327eed3fe832b6886520d4a33663` |

Audit evidence:
`/home/hhh/.cache/bozzetto/evidence/ffi-correction-2026-10-03/auditor-followup-reproduction-2026-10-04/`
(below, `R/`). No repository was edited and no clone was made. Every probe ran
as a script against binaries built from the pushed heads.

## Executed reproduction (E)

The auditor rebuilt all four test projects from the pushed heads under a
full-build lease, then ran each suite unfiltered under its own test lease. All
leases were released. No Ionide language server was running before or after.

| Suite | Result | Versus implementer | Versus previous audit |
| --- | --- | --- | --- |
| Clef | 2,441 run: 2,360 passed, 81 failed | identical | 11 added, 0 missing, 0 new failures, 0 resolved |
| Fidelity.PSG | 395/395 | identical | 19 added, 0 missing |
| Alex | 305/305 | identical | 18 added, 0 missing |
| Composer | 406 run: 405 passed, 1 failed | identical | 4 added, 0 missing, 0 new failures |

TRX files: `R/results/`. Comparisons: `R/compare-*.json`.

## Exit conditions (E)

Every source probe ran through `checkParsedInputsWithPlatform` with the clef
fixture platform. Where a probe published, it ran
`RevisionPublication.publish` and the public `Integrity.check`.

| Item | Probe | Result |
| --- | --- | --- |
| R1 | Prior return's nested source (`Outer.Run : Holder -> bool`) | Clean. **Exit met.** |
| R1 | Three-level chain A/B/C | Clean |
| R1 | Self-recursive `type T = { F: T -> bool; Flag: bool }`; mutual `P`/`Q` | Refused: CCS8414 "receiving types form a cycle"; terminates. Residual N1. |
| R1 | Record nested in record | Refused: the known nested-path residual |
| R2 | `(first, true)` read via `fst`; via tuple pattern; record copy-update `{ h with Count = 1 }` | Check clean and publish, then **public integrity refuses with I5 "The callable write differs from its live constructor operand"**. B1. |
| R2 | Record and `Result` controls through the same pipeline | Publish with zero integrity violations |
| R3 | Mutations M1-M3 | Reported killed (4/2/4/4) with restored hashes. The Alex reviewer confirmed the mutation text matches the prior return's. Not re-executed. |
| R3 | Stored `Result.defaultValue …` and stored `if c then f else g` | Refused: CCS8411. Source cannot yet produce multi-alternative stored functions. |
| R3 | Same-implementation conditional, called directly | Clean at source. Alex output unverified (handoff residual). |

Probe logs: `R/probes/*.log`.

## Blocker

### B1. The R2 write rule refuses tuple and copy-update programs (E)

- **Where:** Fidelity.PSG `src/Fidelity.PSG/CallableAggregateIntegrity.fs`.
  `liveWrite` (`:162-181`) recognises `RecordExpr`, `FieldSet`, `UnionCase` and
  `DUConstruct` only. The refusal fires at `:312-314`.
- **Why tuples matter:** Clef treats tuples as callable aggregates
  (`CallableAggregateTypes.fs:44-46`, `CallableOrigins.fs:625, 725`,
  `Placement.fs:402-403`). So a `TupleExpr` construction row finds no live
  operand and is refused.
- **Why copy-update matters:** a copy-update inherits the stored function
  through `CallableOrigins.fs:691-693`. Baker publishes it as a Construct whose
  value is not an operand of the copy expression.
- **Reproduction:** `R/probes/tuple-publication-probe.log`. All three programs
  check with zero errors and publish, then fail public integrity. The record and
  `Result` controls pass the same pipeline.
- **Clause:** §2.4, "CCS/Baker settles the family over every construction, copy
  and write"; correction 6 of the prior return asked for live-operand
  comparison, not refusal of forms the reader does not model.
- **Correction:**
  - Add tuple element matching (`TupleExpr` by ordinal) to `liveWrite`.
  - For copy-update, Baker either authors a typed inherited-copy relation that
    the reader can verify, or refuses with a coded source diagnostic at
    commitment. The public reader must not be where a valid program first fails.
- **Exit:**
  - PSG positives for a tuple construction and a copy-update row.
  - Clef source controls for the three probe programs, each publishing with
    zero `Integrity.check` violations.

## Significant, latent: reader binding gaps

Not reachable by today's producers, but required before multi-alternative or
environment-carrying components.

- **N2. Tag constructor not bound to the row (R).**
  `CallableAggregateIntegrity.fs:290-291, 326-329, 390-414`. An absent row at a
  live `Some f` occurrence can cite another node's `None` constructor and pass.
  Clef always sets them equal (`CallableAggregateRecipes.fs:145, 284-287`).
  - **Correction:** Construct rows require `tag.Constructor = row.Occurrence`;
    Project rows require `tag.Constructor = FormationInputs.Head`.
- **N3. Project rows unbound (R).** `:345-354, 450-455`. A Project row is never
  compared with its construction row or with the live `FieldGet`, `TupleGet` or
  `DUEliminate`. It may name another carrier with the same contract, or have
  empty formation inputs.
  - **Correction:** require a Construct row for the same slot; require equal
    alternatives, selector, tag and extents; require the live projection
    subject and path to match.
- **N4. Assignment binding untested (R).** Alex `CallableAggregatePatterns.fs:117-120`
  and `RecordWitness.fs:96`. No fixture has an Assign row. The aggregate, field
  and value checks there could be deleted with every test passing.
  - **Correction:** add a positive Assign fixture that passes
    `Integrity.check`, one negative each for aggregate, field and value, and an
    AX4002 native-assignment case.

## Minor and residual

| # | Item | Grade | Where | Action |
| --- | --- | --- | --- | --- |
| N1 | Recursive receiving types refused as cycles, also when the recursion is only through a parameter; not listed in the handoff's residuals | E/R | clef `CallableAggregateSettlement.fs:97-104`; the existing control at `CallableAggregateCases.fs:211-230` is synthetic | Record as a tracked residual with a source control, or implement a bounded re-fold |
| N5 | Stale unselected-body control asserts only `Result.isError` | R | clef `CallableAggregateCases.fs:111-129` | Pin the stale-carrier reason |
| N6 | R4 interface-only control passes vacuously if the region disappears | R | clef `ComparisonRegionCases.fs:219-221` | Assert the region is present |
| N7 | Native-exclusion case `keepOrdinary=true` passes through an "ambiguous" throw, not the native rule | R/I | clef `WitnessSegmentation.fs:346, 392` | Distinct implementations, or surface the reason |
| N8 | Wrapper records do not order layout after re-placement | R | clef `CallableAggregateSettlement.fs:48-58`, `NumericRepresentationRecipes.fs:24-34` | Precondition for admitting nested paths |
| N9 | A renewal failure with no current rows is dropped | R | clef `CallableAggregateSettlement.fs:125` | Report it |
| N10 | Alex write-side environment offset unpinned | R | Alex CAP:383-384, test `:544-547` | Assert offset 8 or 48 for the selected arm |
| N11 | Unlowered `UnionCase` rows admitted then skipped with no error | I | Alex CAP:124-125, `DUWitness.fs:114`; PSG `:171` | Refuse rows on unlowered `UnionCase` |
| N12 | Composer record oracle satisfied by the construction-time constant; stored-code check misses integer casts of code | R/I | Composer `CallableAggregateSourceTests.fs:78-81, 134-139` | Follow the read's callee to its constant; forbid `ptrtoint` and `unrealized_conversion_cast` |
| N13 | `pCallableConditional` has no direct test; its native refusal is uncoded | R | Alex `CallablePatterns.fs:78-119` | Pin it |
| N14 | Absent-row data compared with an empty record although placement uses present rows only | R | PSG `:305-308` vs `Placement.fs:240-247` | Spec ruling on absent-row extents |
| N15 | A participant grouped under a sibling contract still resolves its premise from rows outside contracts | R | PSG `:32-45` | Admit only within the owning or account-held contracts |
| N16 | Reader duplicates placement's unspecified component encoding | R | PSG `:134-156` | One authority, with spec text |
| N17 | Alias carriers with a non-closure formation would be refused once environments are admitted | R | PSG `:228-234`; clef `CallableCarriers.fs:248-254` | Ruling before environment-carrying components |

**Terminology (S/R).**

- The handoff separates a "foreign resource contract" from the "callable
  receiving contract". FFI §3.6 item 2 (`ffi-boundary.md:250-255`) places
  code-lifetime premises and resource obligations inside the native-entry
  receiving contract, and the code follows the spec (`CallableContracts.fs:54-72`).
  Align the handoff wording with §3.6, or amend the spec explicitly.
- "Foreign resource contract" and "proof evidence" are not spec terms.
- Spec `54ef3690` adds AX4002 normatively; the AX family itself, and the AX4001
  that Alex raises 46 times, are unallocated in the spec.

**Executed checks requested but not run** (exact mutation text is in the three
reviewer reports, retained in `R/` as `reviewer-mutations.md`). Run each in the
existing mutation harness as an exit:

- Alex X1 (remove Assign binding; predicted survives), X2 (environment offset),
  X4 (record oracle);
- clef E4 (logical/physical branch resolution), E5 (native exclusion),
  E6 (renewal identity reuse).

## Baseline failures: what this work teaches

The owner asked whether this batch's learnings help resolve the 81 Clef and one
Composer baseline failures. Short answer: **none is fixed by this batch alone,
but ten of them now have their named prerequisite in place.**

| Group | Count | Classification | Executed evidence |
| --- | ---: | --- | --- |
| Callable in an `Option`/`Result` payload never projected or invoked | 9 | **Unblocked**: needs R4 inactivity, whose design required this batch's transport | E: an uninvoked `int` payload gives the exact "stored as a value and no call through a value reaches it"; the `bool` twin and the invoked form do not |
| Composer absent `Ok` payload | 1 | **Unblocked**: R4 arm inactivity, not the "representation gap" Phase 0 named | E: `Error false` with an `Ok` projection is refused CCS8414; an explicit `match` is refused the same way; `isOk` without projection publishes |
| Borrowed-view `zeroed` and scoped callbacks | 19 | Scheduled: later-tranche scoped-callback activation | R |
| Mutable cells written by closures that run a finite number of times | 20 | Independent: captured-cell range exclusion (`FiniteCellRanges.fs:274`); three cases also need arm inactivity | E inconclusive: the auditor's probe captured without writing |
| Never-invoked callables outside the aggregate transport | 3 | Scheduled: R4 plus sequence owner | R |
| "Opaque" inputs that are really never-invoked functions | 5 | Scheduled: R4; **oracle conflict** (below) | R/I |
| "Opaque aggregate alternative" built from a literal array | 3 | Independent: array element origins, then the multi-construction selector | E: an array-held record gives CCS8414 "unresolved source origins" |
| Bare `Option` intrinsic aliases | 4 | Independent: likely `poisoningOf` (`RangeAnalysis.fs:445-466`) | I |
| Sequences | 8 | Independent: Phase 0 R9, R11, RC9 | R |
| Recursive accumulators | 3 | Independent: RC7 | R |
| Ordinary binding demand scope | 2 | Independent: RC4/RC10 | R |
| Unused array binding | 1 | Scheduled: RC1 | R/S |
| `twice` | 1 | Independent: R7 | R |
| Library export input | 1 | Independent: R10 | R/S |

**Corrections to the earlier triage.**

- Ranges are already joined over indirect invocations through
  `CallableOrigins.Calls` (`RangeAnalysis.fs:1382-1406`). An auditor probe with
  a stored, invoked `int -> int` compiles clean (E). The failing cases have zero
  invocations, so the receiving contract is not the missing ingredient.
- The Composer case is arm inactivity. The batch's physical/logical branch split
  also shows that a logical branch selection cannot serve as inactivity
  evidence.

**Guidance for R4 drawn from this batch.** The proven-inactive relation must
meet each of these:

- Built from a complete use census read from the same `CallableOrigins`
  aggregate reading that Baker's aggregate recipe consumes, not a second
  reachability algorithm.
- Refused when origins are unknown, a poison is present, or the value reaches an
  export, a native entry or a declared callback.
- Held in the aggregate dependency account, so that adding a projection or an
  invocation withdraws the exclusion. Add a negative control for that.
- Stable across numeric re-minting.
- Leaves receiving contracts and residence unchanged.

**Already settled by the approved R4 design (correction to an earlier draft of
this return, which asked for an owner decision).**

- The [callable aggregate design](FFI_Correction_Callable_Aggregates_Design_2026-10-03.md#r4-demand-and-commitment)
  publishes `Empty` "only for the proved inactive parameter join". That proof
  requires a complete, current use census.
- `ClosureValueTests.An unobserved callback interior does not acquire a register
  width` asserts `Unbounded`, and the five "opaque input" oracles assert open
  ranges. The implementer will check these six fixtures against that rule and
  cite the governing clause for any expectation change.

**Masking.** CCS8011 is reported once per enclosing binding, and aggregate
residuals surface later. Fixing one error may expose the next, so per-group
acceptance should be read from each test's full diagnostic list.

## Order of work for the implementer

1. **B1:** tuple matching in `liveWrite`, the copy-update relation or coded
   refusal, and the three source controls.
2. **N2-N4:** tag constructor binding, Project row binding, Assign fixtures.
3. Run the requested mutations (X1, X2, X4, E4-E6) as exits.
4. Record N1 and N8 as tracked residuals. Fix N5-N7 and N9-N13 with the next
   increment.
5. Align the handoff terminology with FFI §3.6, and allocate the AX family in the
   spec.
6. Refresh the FFI requirement map. It predates Phase B, and this batch moves
   several `FnPtr` and callback-environment rows.
7. Then R4, starting from the ten unblocked baseline failures, with the six
   oracle checks above.
