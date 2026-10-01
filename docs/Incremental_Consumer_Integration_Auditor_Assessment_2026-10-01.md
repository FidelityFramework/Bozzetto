# Incremental consumer integration auditor assessment — 2026-10-01

**Cross-consumer acceptance remains open.** The Clef workspace integration has
no confirmed defect in the inspected paths. Review found one new Composer
reservation race and three retained consumer gaps affecting cancellation,
status and process cleanup. These belong to the consumers; the independently
accepted Incremental library baseline is unchanged.

This is a source and recorded-evidence audit across **Clef/CCS, Composer and
Bozzetto** while the implementer continues validation. No implementation files,
dependencies or deployments were changed by the auditor. The findings below are
concrete source interleavings, not independently executed reproductions.

## Findings

### C1 — Concurrent Composer reservations can leave neither ticket usable

**New integration issue.** In
[CompilationOrchestrator.fs:432](../../Composer/src/Core/CompilationOrchestrator.fs),
`ProjectSession.ReserveAsync` separately calls native `session.Reserve` and
`workspace.BeginReserve`, without a shared ordering boundary.

The possible order is A/native generation 1, B/native generation 2 plus workspace
revision 1, then A/workspace revision 2. B holds the current native ticket but
its workspace demand is refused as obsolete. A holds the current workspace
revision but its native ticket is obsolete. Both reservations can acknowledge;
neither ticket can build without another reservation. This fails closed rather
than launching stale code, but breaks concurrent reservation usability.

Order the two immediate reservations together, leaving acknowledgement waits
outside that boundary. Add a controlled two-caller test that establishes this
interleaving and requires the final reservation to remain buildable.

### C2 — Transport saturation can turn one cancellation into worker termination

**Pre-existing, still relevant to independent caller cancellation.**
[ComposerWorkerClient.fs:240](../Bozzetto/ComposerWorkerClient.fs) removes the
abandoned build/run request, releases its lock, then submits `cancel_request`
through the ordinary 256-request admission path. Another caller can fill the
freed slot first. The cancellation request then fails local admission; the catch
at line 235 invalidates and kills the entire worker, failing unrelated callers
and their shared work.

Withdrawal needs bounded capacity that ordinary admissions cannot consume, or
an equivalent atomic transfer of the abandoned request's capacity. Cover this
at the transport boundary. Provider control-saturation and shared-demand unit
tests do not exercise this queue.

### C3 — Delayed status can overwrite newer state within one generation

**Pre-existing shared-status gap.**
[ComposerSupervisor.fs:114](../Bozzetto/ComposerSupervisor.fs) accepts snapshots
whose generation equals the cached generation without ordering their state.
Hold an idle status captured before a build; let the same-generation build
finish and publish its current artifact; then deliver the old status. It can
overwrite the artifact with `current=null, busy=false`, marked fresh. The
reconciliation predicate at line 70 sees no reason to refresh that state.

The same race can hide in-progress work. Actual execution still revalidates
compiler authority, so this is a status regression, not permission to run an
obsolete artifact. Order same-generation observations or invalidate and reconcile
them when a concurrent operation supersedes their observation. Existing delayed
status coverage exercises a newer generation, not this case.

### C4 — Joined CCS shutdown does not yet cover editor solver processes

**Pre-existing lifetime gap outside the narrower CCS workspace guarantee.**
[Server.fs:76](../../Composer/src/Lattice.Server/Server.fs) cancels and discards
proof tasks; shutdown and replacement join the editor workspace only. With the
checker idle and a proof solver active, that join can finish before the solver.
[ProofDispatch.fs:50](../../Composer/src/CCS.Editor/ProofDispatch.fs) requests
termination on cancellation/error without then joining actual exit and both
output readers; the readers also use the cancelled token.

Retain solver ownership through uncancelled cleanup and join it during server
retirement. A held solver/output test must discriminate physical cleanup from
the cancellation request. The new native-run process tests do not cover this
separate editor solver path. Do not describe the CCS join as complete editor
process cleanup until this boundary is covered.

## What the review supports

Clef's captured inputs, process-wide checking gate, projection under that gate,
configuration restoration, retained demands and joined close are coherent in
the inspected workspace paths. A reservation acknowledgement remains distinct
from admission, and observer cancellation remains distinct from demand release.
Captured source/metadata and platform selection remain compiler-owned.

Composer's admitted CPU project-session path uses immutable options and target
data after projection; no unsafe raw-graph read was established there. Its
non-CPU graph-reading branches remain excluded. Native artifact validation and
actual launch retain the reservation boundary. Bozzetto's new provider shares
one producer among independent demands and joins the mailbox, controls and
result projections before backend disposal. These source findings support the
direction but do not close C1–C4 or replace execution evidence.

## Evidence and remaining gates

| Consumer | Evidence inspected | Audit interpretation |
| --- | --- | --- |
| Clef/CCS | `7927b3f8dcc8528f8a76571ae1dc75c5e66e0cd0`; 22/22 focused tests. Full Debug run: 2,224 executed, 2,125 passed, 99 failed, zero skipped. | Independent comparison of both TRX files retains all 2,209 baseline identities, the identical 99 failing identities, and 15 passing additions. No new failure or missing test; full compiler parity remains incomplete. |
| Composer | Working integration over `9aa9143`; build succeeds. Latest focused Debug receipt: 21 executed, 20 passed, one failed. | The failed Result-branch proof test has the same identity and TypeMapping diagnostic in the earlier baseline receipt. It supplies no passing proof-revocation evidence. Editor/RPC and remaining acceptance receipts are still required. |
| Bozzetto | Provider/worker source at `a64010a8`, plus adapter changes committed during review in `62285f3e`. Recorded 27/27 focused REPL tests and successful Release test-project build. | Focused provider evidence is useful; unfiltered default and entire Composer integration tier receipts are still required. An owned test daemon will not identify the deployed service. |

The earlier Composer focused attempt stopped at compilation and ran no tests.
No suites or native journeys were rerun by this audit. The live Bozzetto worker
continues to use its previous compiler; no deployment acceptance is implied.

Author receipts are under
`/home/hhh/.codex/work/incremental-adoption-2026-10-01/validation/`.
The Clef comparison baseline is
`/home/hhh/.codex/work/clef-2026-10-01-source-106/local-cells/full-clef-r2/clef.trx`;
the corresponding `full-composer/composer.trx` preserves the earlier native
proof failure. Auditor source hashes, relevant source copies, working diffs and
independent test-identity comparison are retained under
`/home/hhh/.cache/bozzetto/audits/consumer-integration-2026-10-01/`.
Production files supporting these findings remained unchanged during review;
Composer test source continued to change as the implementer validated it.

Return the fixes with discriminating regressions and the final consumer
receipts. Review of those repairs and exact deployment promotion can then proceed
without reopening or rewriting the accepted Incremental library assessments.
