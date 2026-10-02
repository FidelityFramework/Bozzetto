# Incremental pipeline auditor assessment — 2026-10-02

Independent audit of the integrated incremental pipeline recorded in the
[cross-project checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md),
section "October 2 resident formatting and editor preview". The audit covered
source and evidence; it changed no files in any repository and did not touch the
installed daemon beyond one bounded `/health` probe.

## Audited identities

| Repository | Head audited | Branch |
| --- | --- | --- |
| Bozzetto | `3d9ff09a` | `main` = `origin/main` = `origin/integration/calque-incremental-20261002` |
| Fidelity.FSharp.Incremental | `d476aea2` (preview.6 implementation `3b86e2d`) | `main` |
| Calque | `c4be5e1` (implementation root `48c77a6`; `3cfbc72` plus one test whitespace commit) | `main` |
| Composer | `0287b74` | `main` |
| Clef / CCS | `f1dc69a` | `main` |
| Lattice (`lattice-vscode`) | `5066006` | `fidelity` |
| Installed daemon | PID 99247, release `2026-10-02-calque-7d3557971d26-66c0b1bce8fe`, API 4, version 0.6.834 | — |

All four .NET consumers (Bozzetto, Clef, Composer, Calque) vendor byte-identical
preview.6 archives: core `cc017440…9562f9`, hosting `1afaa709…5522e5`. Both
nuspecs record implementation commit `3b86e2dac96ad55cb965341bfc04395061d09c46`.

## Verdict

The checkpoint's claims are supported by the evidence it cites and by the
source. Every named log, ledger row, hash and manifest was located and verified;
no evidence file is missing and no count or hash disagrees with the document.
Two suites were re-executed during the audit: Fidelity.FSharp.Incremental
(108/108) and Calque (83/83); Lattice's unit suite also re-ran at 90/90. The
Bozzetto, Composer and Clef suites were not re-run (lease-governed); their
recorded TRUST lines and test logs were read instead.

No blocker was found. The findings below are ranked by impact. The first is an
operational latency cost the checkpoint already names; the next two are process
and contract-wording defects that should be corrected before the next release.

## Ranked findings

### 1. Significant — superseded formatting runs to completion and serialises the next revision

Calque `DocumentFormatter.create` discards the carried `WorkCancellation`
(`src/Calque.Incremental/DocumentFormatter.fs:177`) and `CodeFormatter.FormatDocumentAsync`
takes no token. The signal is checked only before evaluation, before publication
and before dispatch. Because the document mailbox runs with `MaxConcurrency = 1`
and the core refuses to start a new attempt for the same work while one is
pending, each new revision waits for the full-document format of a buffer that
has already been withdrawn. Correctness is intact: the stale result is dropped
and `isCurrent` refuses it. The cost is latency, up to twice the format time per
revision under rapid edits, and a cancel followed by a same-revision re-request
pays a full recompute (library `advance` obsoletes a completed-but-draining
attempt, Core.fs:219-231). Today the editor sends one explicit preview per
command, so the cost is bounded; it becomes a product defect the moment previews
are triggered by edits.

Remedies, in order of cost: client-side debounce once previews become
edit-triggered; raising the per-document mailbox concurrency so a new revision
starts immediately (bounds latency to one format time, wastes CPU on the
superseded run); cooperative phase checkpoints between parse, Oak, trivia,
dialect, print and merge (the documented next step, and the real fix);
incremental syntax/trivia reuse so a keystroke reformats a region rather than the
document.

### 2. Significant — main received seven commits without the release rule

`scripts/pre-push` requires a push to `main` to raise `<Version>` and to carry a
local-gate pass under `~/.local/share/bozzetto-gate/passed/<sha>/ok`. Commits
`da1f3604` through `3d9ff09a` reached `origin/main` at `0.6.834` with no bump; the
gate directory does not exist; no `core.hooksPath` or `.git/hooks/pre-push` is
installed, so the rule was never enforced. The checkpoint's statements that
`main` publication "remains separate" and is "pending" are inaccurate as written:
`origin/main` already equals the integration branch. Either run the release
pipeline against `3d9ff09a` and bump, or record explicitly that `main` carries
unreleased source.

### 3. Significant — formatter evidence is conflated with the compiler error field

`ProviderSession.recordFormatterDiagnostics` appends Calque host faults to
`backendError` (`Bozzetto.Composer/ProviderSession.fs:59-62`). A successful
`Reserve` sets it to `None` (`:350`), `Cancel` replaces it (`:376`) and
`drainObservations` overwrites it (`:99`). A formatter host fault observed through
`Status` is therefore erased by the next reservation and was never labelled as a
formatter fault. The provider contract section says checker diagnostics are
stderr-only, yet formatter diagnostics are published through a compiler-named
status field. Either add a separate `FormatterError` field or document the
conflation and its lifetime. No test pins the current loss.

### 4. Significant (library contract wording) — "bounded tombstones" is consumer policy

In Fidelity.FSharp.Incremental, `TerminalCompletions`, `ReleasedDemands`,
`InputStamps` and `DefinitionStamps` only grow for the life of an epoch
(`Core.fs:355,387,487,523,554`). Bozzetto is bounded because every producer gets a
fresh scope and scopes are capped at 1,024. Clef, Calque and Lattice epochs that
take repeated inputs or reservations on one long-lived epoch grow linearly until
`Retire`. The adoption contract should state the bound as a consumer obligation.
No library test covers growth.

### 5. Significant (test gap) — stale-proof refusal still has no passing test

Composer `IncrementalBuild.fs:240-241` refuses a proof whose revision is not the
request's. Its only coverage is inside `Result branch authority rebuilds its whole
scope…` (`IncrementalBuildTests.fs:627-676`), which still fails at its initial
build on the callable `Result` occurrence 218. The checkpoint already admits this;
it remains true. The refusal is live code with zero passing coverage.

### 6. Significant (test gap) — Clef config-restore assertion cannot discriminate

`ProjectWorkspaceTests.fs:320-345` compares the restored config with a captured
default. The workspace is built with the default `ArtifactConfig`, so a
`CheckExecution.run` that never restored would also leave the default in place.
The join and diagnostic assertions in the same test do discriminate. Pass a
non-default config to the workspace.

### 7. Medium — Lattice silences daemon-side supersession that is not its own

`client/composer-format.cjs:55` drops every `superseded`/`canceled` refusal. The
daemon also returns `superseded` for a wrong provider generation
(`ProviderSession.fs:421`) when another agent reserves on the shared session
between the client's `status` and `format` calls, and `canceled` can come from
another agent's session-wide cancel. The user presses Preview and gets silence.
Suppress only when the job was aborted or is no longer current; otherwise report
"withdrawn by a newer session operation; retry". Tests at
`composer-format.test.cjs:265-274` currently lock the broad behaviour in.

### 8. Medium — formatter incarnation handles are never released

`FormatterSession` never removes entries from `handles`; retired incarnations stay
counted against the 32-handle cap (`FormatterSession.fs:102`), and a `start`
failure also burns an incarnation (`:108`). Lattice mints a fresh
`buffer://lattice/<client>/<uuid>` document id and incarnation per `TextDocument`
(`composer-format.cjs:110`), so reopening a file never retires an incarnation. A
long-lived shared session refuses `session_capacity` permanently after 32 distinct
previewed documents, and the shared session is not the editor's to recreate. Fix
on both sides: remove closed handles after `awaitClose` completes, and use a stable
per-file document id with a per-`TextDocument` incarnation.

### 9. Medium (test gap) — mid-flight preview cancellation has no executing regression

`ProviderSession.FormatAsync` registers `demand.Withdraw()` on the CLR token
(`ProviderSession.fs:448-450`); no test drives that path. `FormatPreviewTests.fs:170`
calls `Withdraw` directly, `WorkerCancellationTests.fs:180` is pre-cancelled only,
and `canceledOperation Operation.Format` (`:80-119`) asserts `Busy=false`, which
formats never set. An implementation that admitted the demand into Calque and then
replied "canceled" would pass. Add an admission counter to that fixture.

### 10. Minor items

- `Reserve` in Composer waits on artifact and source hashing under the publication
  gate (`IncrementalBuild.fs:312-339`); withdrawal latency scales with project
  size. Authority is correct.
- A CCS admission failure after `session.Reserve` consumes the native generation
  and throws; the old artifact is gone with no ticket (`CompilationOrchestrator.fs:445-453`).
  Fail-closed; document it.
- `ProcessLifetime.fs:34-39` folds a cancellation plus a reader fault into an
  `AggregateException`, losing cancellation identity. Untested edge.
- Calque `release` marks a demand released before admission; a `QueueFull`
  refusal leaks one `MaxDemands` slot until close (`DocumentFormatter.fs:314-316`).
  A failed shared demand never restarts without `Action.Retry` (`:226,:257`).
- Previews share the 128-control budget with Reserve and Cancel; 128 in-flight
  previews make `Reserve` return `busy`.
- An accepted-then-withdrawn preview still advances Calque's revision mark, so a
  lower revision after reservation is refused `superseded`; benign with monotonic
  editor revisions.
- `FormatterSession.cleanupFailures` is never cleared, so one failed release makes
  every later `CloseAsync` fail and `cleanupComplete` unreachable for that session.
- The library's `interpret` marks cancellation before publishing state, so an
  evaluator observer can briefly see `isEligible = true` for a withdrawn handle;
  the reserving caller's acknowledgement is unaffected. Bozzetto rechecks after
  `PendingAttempts = 0`.
- Bozzetto turns every mailbox refusal into `invalidOp` (`ProviderSession.fs:83-88`);
  a late reservation racing close throws instead of refusing.
- `/health` still reports `sessionProvider: "fsharp"` and "No F# sessions
  registered" (`Bozzetto/McpServer.fs:2352`, `Bozzetto.Core/Features/DaemonHealth.fs:193`),
  pinned by tests. Add Composer state rather than deleting the compatibility fields.
- Audit-map line numbers have drifted: Composer `IncrementalBuildTests` 185/209/247/294/355
  are now 229/253/291/404/465; `ProjectSessionTests` 48 is now 136; Bozzetto
  `ProviderSessionTests` 273/433/456 are now 401/561/584; `NativeProviderTests`
  379/405 are now 503/529. Test names are exact.
- The live evidence file `017-mcp-composer_format_preview.json` records the
  original candidate's Calque closure, not the installed one; correct for its
  time, worth a note.
- The Lattice acceptance "delayed stale-response rejection" proves the client
  predicate rejects a completed valid reply after a later edit. It did not
  exercise a daemon `superseded` refusal or HTTP-abort propagation live.

## Claims verified by boundary

| Boundary | Verified | Partial | Evidence |
| --- | --- | --- | --- |
| Library: carried cancellation, peer demand, joins before Drained, `watch`, `fromUncancelledTask`, seal/join on close | yes | withdraw-before-cancel for evaluator observers; tombstone bound is consumer policy | 108/108 re-run; `AsyncMailbox.fs:178-219,459-461`; `Core.fs:89-97,219-246` |
| Bozzetto provider: exact demand under generation lock, generation withdraws previews, request cancel withdraws one, transport cancel reaches the worker Format request, close owns controls and documents, reservation bypasses evaluator saturation, run-refusal reconciliation, bounds | yes | diagnostics conflation (3); handle leak (8); cancellation test gap (9) | `ProviderSession.fs:110-111,434-456`; `ComposerWorkerClient.fs:207-265`; `FormatterSession.fs:127-201` |
| Calque: cold create, immutable snapshot, supersession, shared observe, exact release, seal/join, config in identity, exception mapping | yes | cancellation not inspected mid-pipeline (1); release/retry bookkeeping | 83/83 re-run; `DocumentFormatter.fs:154-177,200-244,289-339` |
| Composer/Clef: reserve pairing, actual launch ordering, immutable capture, physical drain | yes | shared checker config restore (6); stale-proof refusal (5) | `CompilationOrchestrator.fs:445-447`; `IncrementalBuild.fs:104-142,309-377`; `ProjectWorkspace.fs:85-173`; `ProjectChecker.fs:123-196` |
| Lattice: explicit preview only, identity and digest validation, immutable presentation, no disk writes, shared session preserved, separate CCS proof connection | yes | quiet-refusal set too broad (7); per-document ids (8) | 90/90 re-run; `composer-format.cjs:96-175,213-241`; receipt `composer-host-dGKaEs/result.json` |
| Evidence and deployment: all logs, TRUST lines, ledgers, candidate and installed hashes, manifest, worker closure | yes | main-push rule (2) | `~/.codex/work/bozzetto-resumption-2026-10-02/validation/`; release `manifest.sha256` 211/211 OK |

## Recommended order of work

1. Decide the `main` state: run the release pipeline and bump, or record that
   `main` carries unreleased source and reinstall the pre-push hook.
2. Separate formatter diagnostics from `BackendError` in the provider status
   schema, and add the mid-flight preview cancellation regression with an
   admission counter.
3. Release retired formatter handles and give Lattice stable per-file document
   ids; narrow Lattice's quiet-refusal set.
4. Implement Calque phase checkpoints for the carried cancellation; add client
   debounce before any edit-triggered preview ships.
5. Make the Clef config-restore test discriminating; keep the stale-proof test
   red and visible until occurrence 218 is observed.
6. State the tombstone bound as a consumer obligation in the adoption contract
   and refresh the audit-map line numbers.
