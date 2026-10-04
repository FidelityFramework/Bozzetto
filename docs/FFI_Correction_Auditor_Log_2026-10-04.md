# FFI correction: auditor log at the October 4 notch point

October 4, 2026. Auditor's record of the implementer's
[recovery checkpoint](FFI_Correction_Recovery_Checkpoint_2026-10-04.md), held until work resumes. The owner stopped
work for token budget. Nothing below is an acceptance claim. Per the checkpoint's instruction, the auditor verified
the binding and reran no trusted gate.

Grades: **E** executed by the auditor at this notch, **R** read at the stated commit, **A** reported by the
implementer and not re-executed here.

## Binding verification

| Repository | Pushed head | State at the notch (E) |
|---|---|---|
| clef | `7ad1daf` | Clean, level with `origin/main` |
| Fidelity.PSG | `d76b5a3` | Clean, level with `origin/main` |
| Alex | `99409b5` | Clean, level with `origin/main` |
| Composer | `0caa67d` | Clean, level with `origin/main` |
| clef-lang-spec | `4d5c445` | Clean, level with `origin/main` |
| Bozzetto | `29a1616a` | Clean, level with `origin/main` |
| Farscape | `5ec1954` | The owner's three uncommitted note files only |

- E: `sha256sum -c recovery-checkpoint-manifest.sha256` passes for all 39 entries in the notch evidence directory
  `~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/notch-2026-10-04/`.
- E: no gate is running and the checkout heads match the checkpoint's table.

## Status as recorded

- A: the last unfiltered gate ran on clef `a1e1768`, before the final repair. clef: 2,474 total, 2,399 passed, 75
  failed. Against the trusted pin it added 33 tests, resolved 28 failures and introduced 22 new failures.
  Fidelity.PSG 398 of 398, Alex 305 of 305, Composer `tests/Alex.Tests` 405 of 406 (its one trusted failure).
- A: the final head `7ad1daf` has a focused check only: 122 cases, 116 passed, 6 failed. The 16 stale-graph
  regression cases pass. The unfiltered state of the final head is unknown.

## Findings

1. **Unfiltered state of the pushed clef head (first item on resume).** The last unfiltered gate showed 22 new
   failures against the trusted pin, and the final head has had focused checks only. Resume with one unfiltered
   clef run at `7ad1daf` under a lease that covers the whole run, compared with the trusted pin. Any remaining new
   failure is the first work, ahead of the six restart fixtures.
2. **Contract discipline.** Fidelity.PSG's `Revision.Schema` moved from 16 to 21 across five commits that were not
   accepted (R: `Revision.fs:199`, `git log f153c75..d76b5a3`). The callable-aggregate design called for one bump
   for the foundation. No operational harm follows while the shared daemon stays on its snapshot and Bozzetto's
   contract switch remains an independent track. On resume, make no further bump before acceptance. At acceptance,
   record schema 21 and its fingerprint as the foundation contract, and let Step 1's schema follow from it.
3. **Design additions confirmed.** The auditor's five additions to the
   [callable-aggregate design](FFI_Correction_Callable_Aggregates_Design_2026-10-03.md) are now present: integrity
   rules, interior `Option<FnPtr>`, dependency accounts and spec-first sequencing. CCS8411 to CCS8414 and AX4002 are
   tabled with prose in `clef-lang-spec/spec/error-handling.md` (R, lines 375 to 429). The spec gained 11 lines in
   `4d5c445` for typed source paths of inherited callable components. On resume, confirm that the interior-record and
   callable-component text for ffi §3.6, closure representation and union payloads is complete. Otherwise land it
   before the remaining aggregate code.
4. **Placement held.** The checkpoint adds no production file and changes only existing owning passes, readers and
   tests. It records retrieval-first discipline with fresh snapshot identities. Both match the architecture review
   recorded in the [Phase 0 audit](FFI_Correction_Phase0_Audit_2026-10-03.md).
5. **Restart caution endorsed.** Restart point 2 is correct. The library-context probe shows `DeclarationRoots`
   holding only the entry point even with `output_kind=library`, so switching the six opacity fixtures to library
   mode would not establish activation. Library and public activation authority belong to their owners first. This
   connects to triage cause R10, declared ranges for exported library parameters.

## Pending on resume

| Owner | Item |
|---|---|
| Implementer | Finding 1, then restart points 1 to 4 of the checkpoint, then unfiltered acceptance of Phase B and the affected Composer validation |
| Implementer | Findings 2 and 3 |
| Auditor | Verify the resumed gates against this log. Complete the two outstanding catalog documents: the tracker and the Step 1 plan re-sequenced for D6(b), with the callable-aggregate foundation in Phase B, native ABI settlement in Step 1 and Bozzetto on its own track |
| Owner | Commit or set aside the Farscape note files |

## Terminology

The owner is moving to "differential compiler" for Composer's scoped recompilation. "Incremental" stays reserved for
`Incremental<'T>`, Fidelity.FSharp.Incremental and incremental computation. Later records use the terms in those
senses.
