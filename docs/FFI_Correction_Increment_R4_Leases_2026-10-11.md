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
Bozzetto default suite at 15f68ad5 (E): TRUST tier=default registered=8447 ran=8447 passed=8443 failed=0 errored=1 ignored=3 verdict=TestsFailed (the one errored case is the pre-existing CLI-version check)
bozzetto-web after the Composer-page change (E): verify 145.5 KiB self-contained bundle; codec round trip 203 checks 0 failures; page checks passed (leases beside the compiler worker on /composer, absent from /dashboard)
Bozzetto default suite after the Composer-page change (E): TRUST tier=default registered=8447 ran=8447 passed=8443 failed=0 errored=1 ignored=3 verdict=TestsFailed (0 failed, 1 errored) (the one errored case is the pre-existing CLI-version check)
Composer tests/regression at Composer head (E): 53 samples, --jobs 8 --timeout 120, 357 s; compile 4/53, execute 4/4 (baseline; serial run was ~24 min)
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
