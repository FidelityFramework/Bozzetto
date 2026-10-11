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

- `ClosureEnvironmentCases.Complete higher order invocation borrows the actual environment…` (E, `probes/ce-probe.out`):
  `twice f x = f (f x)` makes the callback's formal range feed its own result, so the per-formal fixpoint widens and the
  commitment refuses CCS8011 even for `twice (fun v -> v * 2) 3`. Yes/no: is that refusal the intended answer (re-expect
  the fixture as an active refusal), or should complete higher-order invocations specialize the callback per call site?

---

## Bozzetto: the memory-pressure level now explains itself (2026-10-11, `738ac64f`)

The owner saw "memory Tight" with 74% of RAM occupied. The classifier (`Bozzetto.Core/MemoryPressure.fs`) works on the
fraction of RAM *available* with hysteresis: Tight is entered at or below 20% available and left only at or above 30%
(`defaultThresholds`; the lease pool caps expensive work at one lease while Tight). At 15.0 of 58.5 GiB the machine sits at
25.6% available, above the entry threshold and below the exit threshold, so it stayed Tight from an earlier dip. The level
is unchanged; what changed is that the health event, the dashboard chip (tooltip and a visible line while not Normal) and
the status note now carry the observed fraction and both thresholds (`MemoryPressure.explainAt`; bridge protocol 3 → 4,
so open tabs must reload). Gate: TRUST registered=8450 ran=8450 passed=8446 failed=0 errored=1 ignored=3 (the one errored
case is the pre-existing CLI-version check); codec round trip 203/0; page checks passed. Whether 20%/30% are the right
thresholds for a 58.5 GiB workstation is a policy choice for the owner; the numbers are now visible where the decision is
felt.

---

## Follow-through from the spec sweep (2026-10-11, later)

- Q5 is decided by the spec (lazy by default; a branch demands its condition and selected arm; definitions never force
  at their binding; conditional demand stays pending). clef's `BindingDemand` ingredient now opens one activation per
  `Sequential` arm of a reached decision, with the conditional entry path as proof premises, admits a decision's
  condition (including the lowered `&&`/`||`) and a terminal reference as unconditional first demands, and requires the
  cell to be established in the activation or a dominating prefix. The defective Array.init fixture (Phase 0 RC4) now
  demands the write before observing; two controls were added (the 257/3 sharing control and an arm-swap retraction).
- The spec was amended at clef-lang-spec `9947c9e` (expressions.md: selected-arm demand and first-demand read time over
  mutable storage; width-inference.md §2.2: the relevant demand is the first demand; error-handling.md: `CCS8403` tabled
  as an unestablished settlement). **Owner review requested**: the wording follows the verified sweep text.
- Q1, Q6, Q2, Q4 and Q3 follow in that order; see `docs/Spec_Findings_Phase_B_Questions_2026-10-11.md`.
- Bozzetto: `composer_open_project` refused "Composer worker contract does not match the daemon" on every release since
  the daemon alone was rebuilt: `Fidelity.PSG.Binary.ContractFingerprint` is inlined into Bozzetto.dll at compile time
  (`458DFDB8…` from the current PSG head) while the retained worker closure carries `7D92113F…` (Oct 4). The next deploy
  ships daemon and worker from one build.

---

## Daemon pair, differential acceptance and the liveness increment (2026-10-11, afternoon)

### The matching pair, deployed

`composer_open_project` had refused every release since the region-rows daemon was built alone against the source
`Fidelity.PSG` (daemon fingerprint `458DFDB8…`, retained worker closure `7D92113F…`). The daemon was rebuilt with
`-p:ComposerDistribution=<release>/compiler` so the inlined `Fidelity.PSG.Binary.ContractFingerprint` is the closure's
(`7D92113F…` on daemon, worker and compiler, checked by `bozzetto/stage-dist-aligned.sh` before staging). The suite built
the same way read TRUST registered=8450 ran=8450 passed=8447 failed=0 errored=0 ignored=3 verdict=Trusted once the
runner exported `DOTNET_ROOT`: the one case that had errored on every earlier run ("CLI version prints the release…")
launches the apphost, which cannot find the mise-managed runtime without it (reproduced by hand: exit 150, "You must
install or update .NET"). Release `20261011-dist-aligned`, pid 1512763 from 11:50:44Z.

### Differential compilation, observed end to end

`differential/acceptance/run-04d.sh` on `04d_IncrementalScalarRegions` against the installed pair: the cold build
compiled all three regions (`callable:…:stable`, `callable:…:changeable`, `common`); the unchanged rebuild re-witnessed
only `common` and reused both callables' objects; editing `changeable` recompiled it and `common` and reused `stable`
(Alex visits 3 / 0 / 67). The Composer page listed the same three region rows for generation 3 with their witness and
object states, which is what the owner was fighting to see. `common` is re-witnessed on every build: that is the
seam named in `docs/Differential_Compilation_Seams_2026-10-11.md`, not a defect of the display.

### Liveness: the memory level and agent presence

The owner saw "Degraded" and "memory Tight" at 70% occupied, and "No active reporting agents" while an agent was
visibly working. Both were liveness defects, not measurement defects:

- The level lived in a mutable that a five-second sweep updated and nothing pushed. Now every reader folds a fresh
  reading into the level first (`MemoryPressureWatch.observe`/`sample`: lease request and renew, `/health`, the
  bridge's health read, `get_daemon_status`, and every resource sample a page drives), the level announces each move
  (`MemoryPressureWatch.Changed`), and the bridge pushes Health on it (`BridgeSources.HealthChanged`). The dashboard
  also folds the newest resource sample into the health strip's machine figure, so one fact has one freshest value.
  No new timer; the sweep remains only for the retired session shedding.
- The Agent Work board showed self-reports only. It now carries presence derived from facts the daemon owns: every
  lease held or queued (holder `agent · project`, kind, since) and every present cohort member (name and project from
  its latest report when it has one, else its connection identity). Lease and membership changes re-publish the
  board. Bridge protocol 5 → 6 (`AgentPresence`, `AgentWorkBoard.Presence`); open tabs must reload.
- The agent half of the "re-wiring on resumption" problem is mine: `tools/report.sh resume` re-initializes the MCP
  session, re-joins and posts a running row, and every call re-wires once on `member_not_present`/`wrong_member`;
  the memory note `rewire-presence-on-resumption` makes it the first action after a compaction or a daemon swap.
- Policy, not changed: Tight at ≤20% available on a 61 GB machine (12 GB free) serialises all expensive work to one
  lease, which is what slowed this afternoon's chains. Kernel PSI read 0 stall throughout. Whether the level should
  follow PSI or an absolute floor is the owner's call; the question is on the table.

### Findings while doing this

- The Composer worker spawned for the acceptance session held 4.3 GB RSS after three builds of a one-file sample and
  was the largest process on the machine; the dashboard's resource panel showed it as `dotnet` under the daemon.
  Closing the session released nothing; `composer_retire_worker` (host + epoch) did, and available memory rose from
  8.9 GB to 12.9 GB at once. The worker's retention across generations is a Composer/worker owner question, recorded
  here, not changed.
- One deferred lease ask (clef `test_suite_run`, 12:09:47Z) stayed queued after the script's automatic withdraw,
  and then blocked the same holder's next build ask ("outstanding request queued for a different kind"). A manual
  `withdraw` removed it, and a probe (`acquire` → wait → `withdraw`) removed its ask cleanly, so the cause is not
  reproduced. The chain scripts now follow a Wait decision by retrying the same ask instead of exiting 75.
- The cohort reaper renews only members the activity tracker has seen within two minutes, and the tracker was fed
  by F# session tools alone (retired) and session-status reads; a member that only joined, reported and read the
  board was departed on schedule and every row it posted turned "disconnected", which is what the owner saw as an
  empty panel while four chains were running. `join_cohort`, `report_agent_work` and `get_agent_work` now record
  activity under the member's own key.
- Agent Work rows are bound to the MCP connection that posted them (identity is the connection, by design). My
  reporting helper re-wired its session on transient failures, so one afternoon left rows under five member ids,
  each un-updatable by the next session (`wrong_member`) and shown as "running" until the old member is reaped. The
  helper now re-wires only on a definite session or membership error and continues under a fresh run on
  `wrong_member`. The seam itself is a question for the owner: whether a reconnecting client may continue a run it
  started on an earlier connection when it presents the same client session key, or whether a reconnect is always a
  new member (as today).
- `check-pages.js` and the other hand-written JavaScript in `bozzetto-web/` are scaffolding by the owner's ruling,
  to be replaced by Fable F# or Clef later; the page check's assertions were made structural (presence rows carry
  their own class) so F# UI work stops snagging on JavaScript text literals.

### Audits (2026-10-11, 12:30Z): two reject verdicts, acted on before any commit or swap

The owner asked why no ChatGPT usage was showing: none of the day's work had gone to the auditor. Two one-shot briefs
went to `openai/gpt-6.1-sol` through Pi (`sol-audit/2026-10-11-pm/{q1q5,liveness}-{brief.md,return.md,usage.json}`;
15.2k + 12.8k input tokens, $0.040 + $0.041), and both rows sit on the Agent Work board with model and usage.

| Finding (auditor) | Severity | Action |
| --- | --- | --- |
| `MemoryPressureWatch.observe`/`evaluate` compute the transition under one lock and commit under another | blocking | one `transition` under a single hold of the gate; the announcement carries the level moved to |
| The bridge's `Changed` subscriber reads Health synchronously, and that read samples, so a newer Health can be announced and broadcast before the older one | blocking | Health pushes go through a bounded, latest-wins channel and one loop (`healthPushes`), like Snapshot pushes; a move announced during a read is pushed after it |
| Announcements carry no level | should-fix | `Changed: IObservable<MemoryPressure>` |
| `currentLevel` still allows unsampled reads | should-fix | every reader established: lease request/renew (HTTP and MCP), `/health`, `GetDaemonHealth` (bridge and `get_daemon_status`), resource samples; `currentLevel` remains for `LeaseWatch.request`, whose callers sample first |
| Member `SinceMs` is the latest renewal, not a join time | should-fix | the row says "renewed", not "since"; the daemon keeps no join time |
| "No agents present" claims absence before a board was received | should-fix | the fallback distinguishes "no agent board received" from an empty board |
| Tests miss atomic transitions, callback-visible state, re-entry, derivation controls | should-fix | added: announced level equals the settled level inside the callback; 64 concurrent readings never announce the same level twice and end on the retained level; a Health read that announces is pushed again after, never nested; `Project.presence` controls for holder parsing, latest-report naming, departed exclusion, silent members and renewal time |
| Spec: "bounded by the call that supplies it" is unsound when the same callable is reached by several calls | blocking | reworded: each invocation is bounded by the call through which it is reached; a proof accounts every path separately and lets no path's bound stand for another |
| The incomplete-coverage fallback discards the scoped formals and declarations it relies on | blocking | `ProgramActivation.invocations` returns the premises; `ProgramValueUses` records them as `InitializationPremise` participants |
| The prefix-dominance premise in `BindingDemand` is not recorded as schedule evidence | blocking | the cell and every statement after it up to the decision are recorded as `DemandPremise` rows, so a cell moved past the decision retracts the retained proof |
| The scoped-parameter account has not yet demonstrated the positive control | blocking | demonstrated after three probe facts: the call to a view boundary is absent from the resolver's call table (its body is an intrinsic), so the boundary's own formals are read from the declaration, as `ScopedCallbacks` does; closure settlement lifts the callback to a module-level code binding, so its formation sites are the references to that binding, not the binding's module parent; and the receiving formal is itself an alias of the callback, whose parameter edge is benign only when the formal is a scoped parameter (declared or inferred), so lazy and sequence residence, which also consume coverage, do not widen. The three named BorrowedView cases and "A callable stored by a startup initializer after its slot is ordered by its formation phase" pass; the escaping-parameter negative control stays pending with both reasons |
| "Partially applied view getter cannot be retained" now fails only on `CCS8011` for the entry's own parameter (`_arg1` in `__clef_program_entry`: "no reachable call supplies it") | note | not Q1: the program entry's parameter is the runtime's inbound contract, the same question as library exports (Q2) and the RC5 family (Q4); recorded for that increment |
| Arm-swap control checks a generic reason; no `&&`-right-operand or nested-deferred negatives; fallbacks not isolated | should-fix | added: the retained edge names the decision and guard and the swap retracts for the relation-differs reason; `&&` right-operand pending with the first-demand reason and its left-operand mirror scheduled; a captured snapshot pending with the carrier reason; an undeclared boundary reports both reasons; a formed-but-never-applied callable is ordered by its formation |

#### Second pass (12:55Z): reject again, three findings, all acted on

| Finding (auditor) | Severity | Action |
| --- | --- | --- |
| `healthPushes` serializes only change-triggered broadcasts; connect and `RequestSnapshot` still read and send Health independently, so an older read can arrive after a newer broadcast with no revision to reject it | blocking | every health read is stamped with an increasing `Revision` at the daemon (`readHealth`); the page keeps the newest and drops an older one, as it already does for the resource and agent-work sequences; wire field on `DaemonHealth`, both codecs, mock, round-trip and bridge fixtures |
| Presence reconciliation keys hold only display fields, so two members with the same name, project and status collide | should-fix | `AgentPresence.Member` carries the holder string or the connection identity; the page keys rows by source, member and detail |
| The re-entry test asserts three reads before the queued read may have run, and does not show that no stale frame follows | should-fix | the test waits on the announced read's completion, then checks that every later frame is the newer Health |

Usage: 19.4k input, 0.9k output tokens, $0.048 (`sol-audit/2026-10-11-pm/liveness2-*`). A first attempt of this pass
produced no output for nine minutes and was stopped; its row on the board is closed as failed.

#### Third pass (14:47Z): reject, two ordering findings, both acted on

| Finding (auditor) | Severity | Action |
| --- | --- | --- |
| `readHealth` stamps the revision after collecting, so a read that began earlier but finished later can take the higher revision and overwrite a newer Health | blocking | the revision is taken when the read begins; a control holds one read inside its collection while a later read completes and asserts the earlier read carries the lower revision |
| Presence changes reuse the owner's report sequence, so two board reads differing only in presence can reach the page out of order with the same sequence | blocking | the board is sequenced by a read counter taken when the read begins (`Project.agentBoard`); a control shows two reads of one unchanged report state ordered by the read |

Validation for the third pass: suite `TRUST registered=8464 ran=8464 passed=8461 failed=0 errored=0 ignored=3 verdict=Trusted`;
the Release daemon was rebuilt from those sources and staged as `20261011-liveness`; the pass's first launch stalled
at start (0 bytes) like two earlier ones, so `tools/audit.sh` now kills a silent launch after 90 s and retries. Usage:
`sol-audit/2026-10-11-pm/liveness3-usage.json`.

#### A void pass (14:50Z)

The first fourth-pass brief was the third pass's text with only its suite line replaced, so its diff predated both
fixes, and its TRUST line was read from a stale log after the test build had failed on a control's call shape. The
auditor repeated the two findings against that diff; the pass is void and recorded as such
(`sol-audit/2026-10-11-pm/liveness4-*`, $0.055). The chain now deletes the suite log before a run and the brief is
generated from the working tree each time.

#### Fourth pass on the current diff (14:52Z): reject, one finding, acted on

Suite `TRUST registered=8466 ran=8466 passed=8463 failed=0 errored=0 ignored=3 verdict=Trusted` (the two ordering
controls included). The auditor accepted both ordering fixes implicitly and found the same class once more in the
board path: merged `AgentWorkChanged` callbacks read and broadcast concurrently, so an older board can replace a newer
one in the latest-wins outbox before either reaches the page. Action: board pushes (and lease pushes, for the same
reason) go through a bounded latest-wins channel and one loop each (`pushesOf`), exactly as Health pushes do; control
`boardPushOrderTests` blocks the first change-triggered read while a second change arrives and asserts the page never
sees a newer board overtaken, which the callback design fails. Usage `liveness5-usage.json` ($0.058). The brief is
now generated from the working tree at the moment of sending (`sol-audit/2026-10-11-pm/make-liveness-brief.sh`).

### Course correction (15:00Z): the push plane becomes a derived graph on the foundation

The owner, on the serialized loops: "why is that necessary if you're using Fidelity.FSharp.Incremental? What
happened to the call by value advantage?" — and the direction: Ranvier's shape (signals, memos, effects; one owner
of mutation; "I love Ranvier and consider it to have been inspired by my work with signals in Clef and Fidelity
Framework"), built on Fidelity.FSharp.Incremental "as much as possible as Bozzetto is targeting self-hosting along
with Alex, Clef, and eventually composer". The loops, the two revision counters and the level-carrying event are
not deployed and not committed; the presence derivation, cohort activity, sampling at readers and the wire fields
stay. Drift accounting given to the owner: contained in the uncommitted Bozzetto tree; committed clef and spec
heads audited and accepted.

A design brief went to the auditor before any code (`sol-audit/2026-10-11-pm/bridge-graph-design-*`, $0.011):
**reject**, four blocking (the machine-sample input would have two publishers; serial filtering needs atomic
value-with-serial capture for every input, which "members read with the frame version" is not; `Core.step` under
one gate does not by itself order asynchronous completions or hub publication; connect-time replay and live
pushes need one ordering boundary) and six should-fix (derived-value store ownership and token retention; one
atomic `SetInputs` for telemetry and snapshot; startup subscribe-and-snapshot; demand persistence after `Offer`;
accepted-input versus settled-output versus latest-wins delivery; hazard tests). At the owner's instruction the
auditor is making its own study of both libraries with a read tool over the checkouts
(`sol-audit/2026-10-11-pm/library-study-*`); design v2 follows that study and a second design pass precedes code.

#### clef, second pass (13:12Z): the code stands, one spec sentence rejected

The second-pass brief carried the final clef diff, the amended spec and the backstop (2492/2467/25, four resolved,
none new). The auditor raised no finding on the code or the controls and one blocking finding on the amendment: "a
callable constructed in the entry's body, or in code the entry reaches, is not invoked during startup" wrongly
excludes a callable formed in a helper reached from both a startup initializer and the entry. The sentence now limits
the claim to code reached only through the entry call and states the shared case path by path (the invocations
reached through the initializer are bounded by its phase, only those reached through the entry by the entry's),
which is what `ProgramValueUses.combine` already computes. A third pass on the wording alone follows. Usage of the
second pass: `sol-audit/2026-10-11-pm/q1q5-2-usage.json`. The third pass (wording only, 891 input tokens, $0.003) returned **accept**: "the revised
wording resolves the prior blocking finding by treating shared-helper invocations separately under their initializer
and entry paths." Committed: clef `d0df2e0` (`feat(baker): branch-local demand schedules and formation-bounded
callback phases`, five files by explicit path) and clef-lang-spec `5983f0e` (`docs(spec): bound callable invocations
per path and declare scoped callbacks (owner review)`), both pushed to main.
