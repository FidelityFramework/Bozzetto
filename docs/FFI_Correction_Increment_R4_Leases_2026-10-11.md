# Handoff: R4 opacity fixtures, inactive generator bodies, and the lease plane — 2026-10-11

Grades: **E** executed, **R** source read, **S** spec, **I** inference.

**Scope:** Phase B, R4 (the six opacity/activity fixtures named in the
[recovery checkpoint](FFI_Correction_Recovery_Checkpoint_2026-10-04.md) §32), plus the
Bozzetto lease plane the owner ruled on the same day: leases renew on a heartbeat,
queued asks withdraw and age, and builds/suites show on the Composer page.

**Heads** (pushed to main, clean):

| Repository | Head |
| --- | --- |
| `clef` | `e8d54312e8e2b39b2a4f61196eb610ac687d8ffa` |
| `Bozzetto` | `15f68ad5` (lease plane); the Composer-page change lands with this record |
| `speakez-lab` | `03406ad` (Pi worker edge: turn budget follows the context window) |

**Gates** (unfiltered, leased, released):

```text
clef Clef.Compiler.Service.Tests at 2ac56b2 (baseline, E): total=2476 passed=2417 failed=59
clef Clef.Compiler.Service.Tests at e8d54312 (E): total=2482 passed=2430 failed=52 | vs baseline 2ac56b2: new-failures=0 resolved=7 (the six R4 cases and CallableApplicationTests "A known callable beside an opaque input cannot establish a bounded result", resolved by the same census change)
Bozzetto default suite at 15f68ad5 (E): TRUST tier=default registered=8447 ran=8447 passed=8443 failed=0 errored=1 ignored=3 verdict=TestsFailed (the one errored case is the pre-existing `Public release version.CLI version prints the release rather than a four-part CLR stamp`)
bozzetto-web after the Composer-page change (E): verify 145.5 KiB self-contained bundle; codec round trip 203 checks 0 failures; page checks passed (leases beside the compiler worker on /composer, absent from /dashboard)
Bozzetto default suite after the Composer-page change (E): TRUST tier=default registered=8447 ran=8447 passed=8443 failed=0 errored=1 ignored=3 verdict=TestsFailed (0 failed, 1 errored) (the one errored case is the pre-existing `Public release version.CLI version prints the release rather than a four-part CLR stamp`)
Composer tests/regression at Composer head (E): 53 samples, --jobs 8 --timeout 120, 357 s; compile 4/53, execute 4/4 (baseline; serial run was ~24 min)
  first-coded failure per sample (E, regression/results3-first-codes.txt): 14 CCS8403 BoundaryEmission "Sys.write requires…", 12 CCS8706 non-Clef source types (uint64 6, int64 4, unativeint/uint32/byte 1 each), 4 CCS8403 NumericPublication required integer, 3 CCS8403 completed partial application, 2 CCS8403 finite-cell, 2 CCS8403 callable declaration phase, 2 CCS8414, 4 with no code in the logs
```

**Binding:** `~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/increment-2026-10-10/`
holds `clef/` (build and suite logs, `results/clef.trx`, `failing.txt`, `failing-2ac56b2.txt`,
`binaries.sha256`), `regression/` (`results3/…/summary.json`), `bozzetto/` (release build, suite,
deployment status), `probes/` (the three executed probes and outputs), and `r4-six-ruling.md`
(the spec ruling with its executed revision).

**Rule ledger.**

| Rule (owner file:line) | Positive source control | Negative source control | Mutant killed (test name) |
| --- | --- | --- | --- |
| `clef src/Compiler/PSGSaturation/SemanticGraph/OrdinaryDemand.fs:243-255` — an inactive implementation's exclusion follows execution ownership through nested owners (a self-owning sequence generator or closure continues at its unique structural parent), so an uninvoked body commits no integer (width-inference.md:78, :80) | `LoopRangeCases.An uninvoked unknown trip bound owes neither a proof nor a carrier` (cells excluded, no CCS8011, no residual, no obligation) | `LoopRangeCases.An invoked unknown trip bound refuses every committed cell` (five located CCS8011 sites, `MissingBound` residual) | direct-ownership census (2ac56b2) → the passive case fails its no-errors gate with CCS8011 at `total`, `i`, `total + i` (`probes/r4-probe.out` vs `probes/r4-probe3.out`) |
| Opaque alternatives stay unobservable and refuse only where a reachable integer is committed (width-inference.md:78, :80; test-only re-expectation) | `UnionPayloadRangeCases` T1/T2, `RecordPayloadRangeCases` T3, `SequenceAccumulationCases` T4: payload/current `Unbounded`, no errors | the four active twins: every error CCS8011, one located at the binding that names the payload (`refusedAtPayload`), `rejected` sites for the sequence twin | an inactive body that refused (the 2ac56b2 expectation) fails the passive cases; an active body that stopped refusing fails the twins |

**Shape matrix** (R4 forms; inactive → clean, invoked → refused CCS8011 at the located sites, both intended):

| Form | Result |
| --- | --- |
| union alternative (`IntVal 7` beside an opaque `input`) | clean inactive / refused active |
| record alternative (`{ Payload = IntVal 7 }` beside an opaque holder) | clean inactive / refused active |
| recursive producer / function-value producer | clean inactive / refused active |
| sequence accumulation over an unresolved origin | clean inactive (unknown-origin edge, no certificate) / refused active (resolved origin, unobservable element) |
| loop generator with an unknown trip bound | clean inactive (no residual owed) / refused active (`MissingBound` residual) |

**Growth** (`git diff --shortstat`):

- `clef`: 4 files, +151/−22 (one compiler file, three test files; one new fixture helper each in
  UnionPayload and LoopRange).
- `Bozzetto` 15f68ad5: 9 files, +333/−14 (renew/withdraw/aging in the pure pool, two HTTP routes,
  runner heartbeat, tests, simulation, docs). Composer-page change: 4 files, +24/−10, no protocol change.
- `speakez-lab` 03406ad: edge default `max_turns` 500 (bound 5000); tests aligned.

**Lease-plane rule ledger (auditor finding 4; owner `Bozzetto.Core/ExpensiveWorkLease.fs`, tests
`Bozzetto.Tests/ExpensiveWorkLeaseTests.fs`, HTTP routes `Bozzetto/McpServer.fs:2770` renew and `:2790` withdraw):**

| Rule (owner file:line) | Positive control | Negative control | Reproduction / mutation |
| --- | --- | --- | --- |
| `renew` (:365): only the holder renews a live lease, by its kind's TTL | "the holder renews a live lease and its expiry moves forward by the kind's TTL" (:170) | "another holder cannot keep a lease alive" (:186); "an unknown lease id renews nothing" (:193); "an expired lease is reclaimed, never resurrected by a late renewal" (:199) | property "renewal never changes the active count, the holder set or the queue order" (:207) |
| `withdraw` (:344): a caller leaves the line for one kind only | "a waiting caller that withdraws leaves the line" (:233) | "withdraw touches only the holder's own ask for that kind" (:244) | — |
| queued-ask aging (:201-210, `queuedAskLifetime` 2 min; retry refreshes `LastAskedAt` :256) | "a ghost ask whose caller never retried is aged out of the line" (:254) | "a live retrier is never aged out: each retry refreshes the ask" (:264) | "REPRODUCED — without aging, two abandoned asks block a later caller" (:288): the defect reproduced inside the ask lifetime and released after it |
| same-scope supersession (:256-259) | "a newer ask from the same scope supersedes its older queued ask instead of lining up behind it" (:274); "a retrying holder does not create a duplicate queue entry" (:112) | — | — |

**Owner rulings applied (2026-10-10):** builds and test suites over Clef/Composer code are Composer
work; Work leases render on the Composer page; development daemon restarts are authorized; one
stable lease holder per agent and project; never 50-turn sessions on 131K-context LAN workers.

**Deviations from the written ruling (`r4-six-ruling.md`, executed revision):** the sequence twin
drives a resolved origin with an unobservable element because an unresolved origin cannot be reached
from an entry; the uninvoked loop fixture owes no `MissingBound` residual because the loop-range
recipe runs only for committed cells (RangeAnalysis.fs:2982), so the residual is asserted by the twin.

**Open after this increment:** `work-lease run` still relies on the pi-lab `observeLeasedCommand`
side channel for Agent Work rows (the Composer leases panel is the daemon-observed view); the three
LAN guests' `worker.json` still pin `max_turns` 50 until the operator rollout; Sol audit of this
increment; the 52 remaining clef failures by test class (E, `clef/results/clef.trx`): BorrowedViewTests 21
(17 refuse at `the result of 'zeroed'` in the shared `withView` helper, 2 at `the result of 'length'`, 3 CCS8403
initialization-order settlement), SequenceAggregateCases 5, ResultOperationCases 3, CallableApplicationTests 3,
CallEffectRangeCases 3, SequenceConsumerCases 2, SequencePullEffectCases 2, DirectCaptureCarrierTests 2,
OptionFoldCases 2, ResultEliminationCases 2, and one each in ClosureEnvironment, Dimensional, SequenceConsumption,
SequenceFactoryResults, RecursiveInstantiation, SequenceApplication and ArrayConstruction.

---

## Handoff: borrowed-view group (zeroed default and view length), 2026-10-11

**Scope:** Phase B, the 21 `BorrowedViewTests` failures; 18 resolved by tabling two intrinsic results and by two fixture
corrections; 3 left as an owner question (below).

**Heads:** `clef` `d2e6cf64347b446d3ac60d847f4844ed7786e2ff` (pushed, clean).

**Gates** (unfiltered, leased, released):

```text
clef Clef.Compiler.Service.Tests at d2e6cf64 (E): total=2482 passed=2448 failed=34 | vs e8d5431: new-failures=0 resolved=18
```

**Rule ledger.**

| Rule (owner file:line) | Positive source control | Negative source control | Mutant killed (test name) |
| --- | --- | --- | --- |
| `clef src/Compiler/NativeTypedTree/Expressions/Intrinsics.fs` `RangeSources.intrinsic`: `NativeDefault.zeroed` of an integer kind is the point 0 (zero-initialized storage is the compiler's own fact); `BorrowedView.length` is a length, bounded by the platform's Pointer width like the other lengths | the 16 `withView`-stub fixtures check clean (E, `clef/results-bv/bv-targeted.trx`, second run) | `BorrowedViewTests.Borrowed view cannot be manufactured with a default value`: the view's origin finding stays ("no source constructor") while no integer is committed | a `zeroed` row matching an empty argument list (first run) leaves all 16 refused: `NativeDefault.zeroed ()` applies to a unit argument |

**Domain of the two rows (auditor finding 2):** `RangeSources.intrinsic` is consulted from `transfer`, which ranges
only integer, boolean and char nodes (RangeAnalysis.fs:2163-2176), so the `zeroed` row's point 0 applies to those kinds
alone (false for a boolean, NUL for a char); `lengthRange` is `[0, 2^(Pointer-1) - 1]` on a context declaring a Pointer
width and unobservable otherwise (Intrinsics.fs:1304-1308), which is why a platform-less commitment of a view's length
still refuses.

**Fixture corrections:** the two manufactured-view cases `ignore` the view and return 0 instead of returning
`BorrowedView.length view`; a committed length is unobservable on a platform-less check (`RangeSources.lengthRange`), which
is not what those cases guard (E, `probes/bv-probe2.out`: the finding is produced without the commitment).

**Growth:** `clef` 2 files, +11/−5 (one table row pair, two fixture bodies).

**Open, owner decision needed (yes/no):** the three remaining `BorrowedViewTests` cases refuse with CCS8403
"Complete ordinary invocation scope is unavailable" because `ProgramActivation.coverage` covers a lambda only through
callers that lead back to the entry, while the fixture's `viewScope` descriptor declares the callback's invocation to
`ScopedCallbacks` only. Should a declared scoped callback count as a covered activation (invoked within its declaring
boundary's call) for initialization-order settlement? If no, the three fixtures expect CCS8403 until platform boundaries
declare invocation. Details: evidence `borrowed-view-notes.md`.

**Further owner decisions surfaced while scoping the remaining 34 (evidence: `borrowed-view-notes.md`, `probes/`):**

- The eight `calls`-counter fixtures (ResultOperation 3, OptionFold 2, ResultElimination 2, SequenceApplication 1)
  snapshot a lambda stored in `let mutable callback` through a partial `Result.map`/`Option.fold`/`Seq.fold`, reassign
  the slot, and invoke the snapshot once. closure-representation.md:118-120 and Callable_Aggregates_Design rule 4 say the
  retained snapshot keeps its pair; today `CallableOrigins` resolves the snapshot's call to every writer and
  `CallableIngress` builds no family for a lambda stored in a mutable cell, so `FiniteCellRanges` cannot bound `calls`.
  Yes/no: extend the ingress and finite-cell owners to admit a mutable callable cell's complete exact write set as the
  finite family (MutableCallableStorage.fs:19, "the first finite family") in this engagement, or re-expect the eight?
- The three `CallEffectRangeCases` fixtures place a scalar snapshot (`let observed = state`) inside a branch;
  `BindingDemand` (Baker ingredient) declares such schedules pending by design (BindingDemand.fs:200-204) and
  `failures` turns pending into CCS8403 at settlement. Yes/no: should a pending binding-demand schedule leave the snapshot
  eager at its binding instead of refusing the program?

**Auditor return (Sol, 2026-10-11, `sol-audit/sol-return.md`):** six findings, all acted on.

1. Gate binding → `manifest.md` binds each gate to its tree, commit, binaries and log; `clef/run.sh` now records
   `tree.txt`.
2. Row domains → stated above (`transfer` ranges integer/bool/char nodes; `lengthRange` needs a Pointer width).
3. Length-row controls → **the finding was right in substance.** The executed mutant (`clef/mut-status.log`, lengthrow:
   the row forgets borrowed views) left a platform-aware positive control passing: a borrowed view's length is bounded by
   `BorrowedViews.numericBoundary` (BorrowedViews.fs:95-101, the declared pointer-extent boundary), so the row I added was a
   second authority for a fact that owner already holds. **Withdrawn** (`fix(ranges): …` on clef main); the two
   manufactured-view fixtures were resolved by their fixture change alone. The two new `BorrowedViewTests` controls now
   guard `numericBoundary`: positive (declared extent `[0, 2^63-1]` under Pointer 64) and negative (no platform: CCS8011 at
   a length the entry commits).
4. Lease-plane ledger → stated above.
5. R4 active-exclusion mutant → executed (`clef/mut2-status.log`, genexcl: RangeAnalysis defers every node a
   SeqGenerator lambda owns, i.e. excludes active generators): `LoopRangeCases.An invoked unknown trip bound refuses every
   committed cell` and `SequenceAccumulationCases.An invoked sequence with an unobservable element refuses every committed
   cell` fail, the passive cases pass. A first mutant (dropping the census's activation guard) did not reach the twins
   because an invoked implementation's formals keep active suppliers (`suppliersInactive`); recorded as such.
6. Contradictory join sentence → struck through and superseded in `r4-six-ruling.md`.

**Re-expectations landed after the audit brief (clef `068c5246`, E, unfiltered, leased): total=2482 passed=2453 failed=29 |
vs d2e6cf6: new-failures=0 resolved=5.** The uninvoked pull-effect pair asserts the counter's exclusion under the
inactivity proof instead of an unobservable range (the recipe runs only for committed cells); the three invoked
opaque-aggregate cases expect the located CCS8011 at `first` while keeping their structural assertions.

**Final clef gate of this record (clef `b0ef172d`, E, unfiltered, leased): total=2484 passed=2455 failed=29 | vs the committed
29: new-failures=0 resolved=0; the two new boundary controls pass.** Heads at close: clef `b0ef172d1eef6afd244020a03b3bd3c49b9dbcb0`, Bozzetto (this record's
commit), Composer `0caa67d8`, speakez-lab `03406ad`, clef-lang-spec `4d5c445`.
