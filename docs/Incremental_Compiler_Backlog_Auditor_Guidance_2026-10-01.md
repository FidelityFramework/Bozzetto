# Incremental compiler backlog — auditor guidance, 2026-10-01

The purpose of the integration is a more responsive, dependable foundation for
correcting the compiler and strengthening its architecture. Full language parity
is not a prerequisite for recognizing that improvement. It remains necessary to
repair defects that let stale authority survive or lose ownership of work.

This accompanies the [cross-repository assessment](Incremental_Cross_Repo_Fallback_Auditor_Assessment_2026-10-01.md).
The auditor proposes fixes and supplies controls; implementation remains with
the component owners. Recommendations refer to the checkpoint's exact source
pins, not a claim that these changes are already implemented.

## Recommended sequence

| Order | Work and owner | Why it belongs here | Completion criterion |
| --- | --- | --- | --- |
| 1 | Clef prepared-publication authority fence, A1 | A newly added authority root can be withdrawn without invalidating cached publication. | Receipt-only withdrawal refuses both passive reading and final publication; retained original still works. |
| 2, parallel small repairs | Composer proof fault classification and failed-construction cleanup; Bozzetto unsent cancellation bookkeeping | These directly affect reliable ownership and continued use of the integration. | Exact fault survives retirement; immediate retry after failed construction works; never-written cancellations do not accumulate late-reply tombstones. |
| 2, parallel validation repair | Composer program-lifetime fixture contract | A stale fixture can conceal usable compiler capability and prevent later editor groups from running. | Keep comparison and every existing public projection assertion; correct declared target facts and retain missing-byte refusal. |
| 3 | Callable `Result` payload transport, Baker/PSG/Alex | Unlocks the existing end-to-end branch/proof invalidation oracle, which currently stops at cold build. | Both native outputs and the later stale-proof rejection actually execute. |
| 3, independent language increment | Branch-local ordinary lazy demand, Baker | Restores common local source forms without changing first-demand semantics. | Selected-arm activation, post-write first demand and subsequent sharing work in source and native tests. |
| 4 | Bozzetto monitor exit/recheck | Prevents progress reporting from stalling after a narrow exit race. | Background observation resumes without a client read concealing the missed wakeup. |
| Later, demand-backed | Larger mapped-pull proofs and broader activation/transport forms | Current bounded refusal is safe; broader proof contracts cost more than a cap change. | Explicit scalable certificate, negative mutations, measured proof cost and native oracle. |

The two language increments can proceed independently. I favor callable
`Result` first when selecting one because it restores a particularly useful
cross-revision acceptance journey. Do not couple either to a scheduler rewrite
unless an actual shared-library contract prevents the repair.

## Program-lifetime: establish the target contract before changing borrow logic

The shared fixture in
[Composer/tests/Fixtures/ProgramLifetime](../../Composer/tests/Fixtures/ProgramLifetime)
compares strings, selects FPGA and declares program memory-space roles. Those
roles do not establish a byte representation. The corresponding Clef fixture
was changed before this integration to an explicit CPU target with Pointer and
Register widths, uint8 and signed64 offers. Its undeclared-byte negative test
requires refusal.

[StringBytes.fs:41](../../clef/src/Compiler/Baker/Ingredients/StringBytes.fs)
requires a unique declared unsigned eight-bit representation covering 0…255.
The current generic borrow/bounds diagnostic is therefore not enough to conclude
that a new lifetime algorithm is needed. Boundary settlement currently loses
the underlying memory-publication reason through `Result.toOption`; preserve
that reason in diagnostics as a separate usability improvement.

External controls preserve the original comparison, names, declaration mutations,
unsaved edits, retained snapshots and startup source. They vary only the explicit
platform declarations. The initial control exposed another incomplete declaration:
the selected manifest claimed runtime `bare`, but the copied direct compiler
fixture supplied no source runtime. The FPGA control also needed an explicit
clock and depth calibration. The refined controls declare those facts rather
than weakening validation. Synthetic FPGA clock/calibration values are test
inputs, not measurements or a supported physical target claim.

The refined CPU and synthetic FPGA candidates both passed the unchanged complete
`ProgramLifetimeProjection.run`; the no-byte CPU control still refused. Exact
inputs and receipts are in the assessment. The implementor can repair the shared
fixture with this evidence, retaining all existing assertions. These source/editor
results do not establish native FPGA execution. If a memory
premise still fails after platform alignment, trace it in producer order:

1. Source byte/numeric representation authority.
2. String-comparison traversal and the exact byte buffer/index/extent.
3. Each lower/upper bound guard and its always-active failure continuation.
4. Borrow lineage, read-only use census and lifetime boundary.

Relevant owners are `StringComparisonRecipes.fs:255`,
`MemoryAccessRecipes.fs:114` and `:215`, and `BoundaryRecipes.fs:368` in Clef
Baker. Do not invent byte offers, add unchecked loads, infer safety in Alex, or
admit every literal-backed Bytes view. Additional producer controls should cover
zero/unequal extents, multibyte UTF-8, redirected buffer/index, bypassed guard,
guard copied from another access, and escaping or mutable extra view use.

The separately executed `StringEncodingChecks` exposes another incomplete
fixture: it declares read-only image storage but asks for a dynamic array/string
snapshot with no writable stack. `ArrayMemoryRecipes.fs:313` requires that stack;
`StringViewRecipes.fs:115` then needs the resulting allocation. Add the explicit
stack declaration already used by Clef's `StringEncodingCases`, and rerun all
existing valid/invalid byte, diagnostic-span, repair and immutable-snapshot
assertions. An external two-line candidate is supplied under the audit evidence's
`probes/string-encoding/`; it is **unexecuted**, so remaining allocation premises
must still be checked. Do not suppress either diagnostic or change `String.fromBytes`
to a different operation just to bypass the fixture's missing authority.

## Triage stable failures by the first failed contract

The fresh Clef suite retains exactly the same 99 failing identities. A useful
next pass should group their first failed phase, not attempt 99 unrelated fixes.
The audit found first diagnostics of CCS8011 in 81 cases and CCS8403 in 13;
five stop at direct assertions without those codes. The largest named family is
BorrowedView (21), followed by ResultElimination (8), ResultOperation (6),
SequenceAggregate (5), and OptionDefaultWith (5).

Prioritize these investigations alongside the named language increments:

1. **Restore BorrowedView fixture admission.** All 21 cases abort in shared
   `BorrowedViewCases.check` / `DimensionalCases.noErrors` before the scoped
   callback, escape, access or revocation assertion. Trace the common earliest
   numeric/ordinary-demand premise first. Preserve those negative assertions;
   stable failures do not establish that the intended safety behavior ran.
2. **Examine SequenceAggregate residence/refusal rows.** These five cases reach
   snapshot preparation/runtime assertions. Three expected refusal rows are
   absent, one expected allocation is absent, and an Option frame field is
   unsupported. Distinguish changed representation from missing source refusal
   evidence. Repair source settlement/residence before broadening Alex's runtime
   frame acceptance. They do not all follow from the mapped-pull state cap.
3. **Trace callable Option/Result prerequisites before backend changes.** Use
   the first missing ordinary-demand/carrier/transport fact from each failing
   case. Similar test-family names do not prove a shared root cause. The native
   callable `Result` seam below is a concrete starting point, not a claim that
   one fix resolves every Option/Result failure.

Add a positive setup control next to a negative safety test when a fixture can
fail before its intended assertion. That preserves a useful red signal while
making the reason for it clear. Do not change expected results wholesale or
count a setup refusal as proof of the later safety property.

## Callable Result: publish complete payload transport

The unchanged test at
[IncrementalBuildTests.fs:584](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)
checks branch authority before its initial native build. It constructs a
`Result<bool -> bool, bool>`, selects a function and invokes it. Cold build
refuses occurrence 218 because a callable has no ordinary scalar representation.
The subsequent changed-output and stale-proof assertions therefore do not run.
Keep the test intact; reaching those assertions is the objective.

Whole-revision branch authority already exists. The missing native transport
must preserve callable components and environment identity across union
construction, storage, extraction and later application. Baker owns these facts;
PSG publishes typed correspondence; Alex consumes it.

The concrete source seam is:

- [ValueRepresentations.fs:123](../../clef/src/Compiler/Baker/Ingredients/ValueRepresentations.fs)
  correctly refuses treating a function as an ordinary scalar.
- [DUWitness.fs:51](../../Alex/src/Alex/Witnesses/DUWitness.fs) requests ordinary
  type mapping during extraction; its construction path reads one ordinary
  payload. [DUPatterns.fs:52](../../Alex/src/Alex/Patterns/DUPatterns.fs) likewise
  expects one scalar payload.
- [MemoryPatterns.fs:248](../../Alex/src/Alex/Patterns/MemoryPatterns.fs) uses the
  same payload offset for each value in its list. Merely flattening code and
  environment into that list risks overlapping stores.

First identify occurrence 218's actual kind and inspect its published carrier,
constructor/arm path, environment component and residence. This audit establishes
the transport mismatch, not that node 218 must itself be a `DUEliminate`.
If source rows are missing, repair Baker. If complete rows exist but Alex asks
for a scalar, repair that consumer to read the rows. Publish exact per-case
component placement, size/alignment, environment ownership and
constructor/storage/copy/extraction correspondence. This is a contract demand,
not a preselected ABI or assumption of two pointers.

Minimum controls:

- Both Result alternatives, captureless and captured functions, and two closures
  sharing code but carrying different environments.
- No invocation from tag-only predicates; selecting a function and applying it
  remain distinct operations.
- Missing or changed code/environment/layout/owner/path rows refuse before
  binding/emitting an incomplete value.
- Nested aggregate transport requires explicit residence/copy evidence; do not
  silently broaden the scalar Option-copy rule.
- The original native journey reaches initial exit 0, reservation and immediate
  revocation, edited exit 1, whole-scope rebuild with no inappropriate retained
  objects, and refusal of the earlier revision's proof receipt.

Do not substitute a pointer for `TFun`, infer an environment from a code address,
or weaken the stale-proof assertion to get a green test.

## Branch-local lazy demand: add activation evidence

The bounded implementation in
[BindingDemand.fs](../../clef/src/Compiler/Baker/Ingredients/BindingDemand.fs)
has three distinct limits: region discovery only flattens `Sequential` inside
lambdas (line 82); the alias and mutable declaration must occupy the same flat
slot list (line 113); recognized first frontiers do not include the activated
arm's demanded terminal result (line 195). Adding branch discovery alone will
not fix the original returned-alias example.

Introduce a typed activation descriptor containing the owning lambda, region
root, ordered local slots and full conditional-entry path. Each entry identifies
guard, both arm roots, selected arm, parent and ordinal. Runtime branch selection
is an activation condition, not proof that the guard is statically known.

Prove that the mutable cell exists before entering the exact selected arm,
whether declared locally or in a dominating outer prefix. This does not freeze
its value before writes on the way to first demand. A sibling declaration,
later declaration, unrelated lambda or previous loop iteration does not qualify.
Initially confine the entire alias family and its uses/captures to one activation.
Preserve the existing complete-use census and freshness comparison.

Recognize a direct terminal reference, or a transparent wrapper whose value is
demanded as the activated arm's result, as a possible first frontier. An ordinary
deferred initializer or unused expression is not an unconditional frontier.
Move the shared family immediately before its first proven local frontier using
the existing `BindingDemandRecipes` machinery, within that arm's `Sequential`
parents. Preserve identity and invalidate ranges before renewed numeric analysis.
Add typed participant roles if existing rows cannot express the full path.

Discriminating tests must establish behavior, not just graph shape:

- In either selected arm: define `snapshot = current`, write 257, first-demand
  `snapshot`, write 3, and demand it again. Both observations return 257.
- A direct branch terminal `snapshot` observes the post-write value without an
  added eager marker.
- An effectful guard runs once; an unselected arm's effects or divergence do
  not run. Cover nested activation with an outer dominating cell.
- Change an arm role/ordinal, add an owner, redirect a same-typed alias, move
  cell initialization, or remove/duplicate entry premises: retained evidence
  must refuse before settlement can recreate it.
- Cross-arm escapes, deeper conditional-only demand and unsupported loop
  lifetimes stay explicitly unresolved until their carrier contract exists.

Never make ordinary `let` eager, flatten both arms, move the snapshot ahead of
the guard, or clone one shared alias into independent reads. The staged proposal
under `/home/hhh/.codex/work/incremental-adoption-2026-10-01/branch-local-demand/`
is useful implementation input; its presence is not an executed gate.

## Mapped pulls: keep bounded refusal, improve the proof when needed

[SequenceAccumulationRecipes.fs:154](../../clef/src/Compiler/Baker/Recipes/SequenceAccumulationRecipes.fs)
uses `(control label, remaining inner pulls)` states with a 4,096-state cap.
This is proof construction, not runtime unrolling. The existing 80-yield case
deliberately exceeds the cap and requires refusal without a retained range.
That safe limit should not be treated as an authority bug.

First report proof-state exhaustion distinctly and measure workloads that need
more. A larger cap changes cost without addressing multiplicative proof size.
For the existing single-inner-loop cohort, a bounded symbolic loop certificate
is a plausible next increment: exact entry/exit and guard alternatives, fresh
iterator identity, inner finite budget, and acyclic prefix/iteration/suffix
suspension bounds. Prove that every admitted backedge consumes one successful
inner pull and that the zero-budget success alternative is unavailable.

Such an extension needs a typed native proof body, its PSG counterpart,
canonical mappings and source-authority freshness. Numeric renewal validates
the handoff; it must not invent the certificate. Alex performs only the
published arithmetic translation.

Retain refusal for extra MoveNext/reset/escape/reentry, missing guard alternatives,
unbudgeted cycles, changed bounds, duplicate correspondence and withdrawn
authority. Cover zero pulls, conditional yields, multiple suspensions and early
exit. Measure proof size and compile-resource growth; execute the native output
oracle. A finite successful-pull bound alone does not prove callback termination.

## Standardize interim hosting on .NET 10

**Recommendation: SDK/runtime 10, with no demonstrated reason to require 11.**
The repository's SDK 11 RC, default net11 target and preview-language setting
came from imported SageFS baseline `4f36b847`. They were not introduced to meet
a Composer requirement. The worker and compiler consumers target net10; CLI,
Core and Host already include net10. The owned Harmony checkpoint records the
same artifact working on .NET 10.0.12.

Distinguish three decisions: host SDK/runtime, F# compiler packages, and the
native target. The pinned FCS/FSharp.Core 11 packages have netstandard assets;
their names do not require CLR 11. Nullness checks also exist in the older
compiler generation. Neither observation proves that every retained Bozzetto
source/API already builds with SDK 10, so require a clean build and concrete
diagnostics rather than presume compatibility or necessity.

Migration scope:

1. Pin SDK 10.0.401 or an explicitly chosen .NET 10 servicing version. Remove
   prerelease/default net11 selection and make shipped targets net10-only.
   A default multi-target list containing net11 still requires a newer SDK.
2. Initially retain reviewed dependency pins. Downgrading FCS APIs is a separate
   change; assess any real SDK 10 incompatibility at its smallest boundary.
   Do not disable nullness or suppress NU1605 as a blanket remedy.
3. Update active CI/package/test paths together: `ci-pipeline.fsx` test DLL,
   required TFMs and install matrix; `TestInfrastructure.fs` daemon candidates;
   `LiveTestingTestHelpers.fs`; default-target assertions; active fixtures.
   Prevent old net11 outputs from satisfying a new test accidentally.
4. Regenerate and review locks under the selected SDK. Run actual unfiltered
   tiers, package inspection and isolated install/version/help smoke checks
   against the net10 payload. Preserve historical receipts as history.
5. Stage an immutable net10 release and compatible ASP.NET runtime. The current
   installed launcher selects a net11 release: changing PATH cannot change a
   running daemon's runtime. Coordinate its eventual replacement and repeat the
   provider journey using the exact staged compiler closure.

The imported baseline's runtime choice is upstream history. Bozzetto need not
inherit that choice. The short self-hosting horizon strengthens the case for a
stable host and explicit CLR seams, not for carrying an unnecessary preview
runtime migration.

## Keep the API aligned with self-hosting

Use typed messages, admitted-operation handles, scoped demand and explicit
completion/lifetime states as the portable contract. Keep CLR Task/process/IO
adapters at host boundaries. A2 is an example of why exception conventions are
not enough: the owner needs the exact completion meaning, not a guess based on
an OCE. A shared typed fix is preferable to consumers accumulating different
task-classification rules.

Avoid turning H1 repairs into a general actor framework or arbitrary continuation
system. Conversely, do not call queue bounds, observer cancellation or scope
withdrawal physical cleanup. Those distinctions are precisely what makes this
foundation useful for future compiler and device orchestration.
