# Incremental pipeline auditor follow-up — 2026-10-02

Independent review of the repairs made in response to the
[first assessment](Incremental_Pipeline_Auditor_Assessment_2026-10-02.md), plus a
requested comparison with the Ranvier reactive graph. The review changed no
repository files and did not touch the installed daemon.

## Reviewed identities

| Repository | Head | Branch |
| --- | --- | --- |
| Bozzetto | `ec73390a` (source `15d3e022`, hook `371ce4b8`) | `integration/audit-followup-20261002`, three commits ahead of `main` `7d7fc308` |
| Calque | `19099b5` | `main` |
| Lattice | `3ddea31` | `fidelity` |
| Installed daemon | PID 99247, release `2026-10-02-calque-7d3557971d26-66c0b1bce8fe`, unchanged | — |

All nine repositories were clean and matched their remotes at review time.

## Evidence verified

Every receipt under `~/.codex/work/bozzetto-resumption-2026-10-02/validation/audit-followup/`
says what the checkpoint says. Both TRUST lines read `Trusted`: the default tier at
8,970 registered, 8,967 passed, three ignored, and the Composer tier at 44 of 44.
Calque's first run at 93 of 95 and corrected run at 95 of 95 are both retained.
Lattice's unit suite reports 103 of 103 with a passing VS Code 1.139.1 receipt.
The hook refusal log shows the pre-push rule now firing on `main`. The worker
asset check reports 34 declared, zero missing, zero test dependencies. Both
candidate hashes match the document. Calque's suite was re-executed during this
review at 95 of 95.

## Verdict on the follow-up

The repairs do what they claim. Findings 2, 3, 7, 8 and 9 of the first assessment
are fixed in source with discriminating tests, and finding 1 is materially
narrowed. One new defect was introduced and one documented claim overstates the
behaviour at capacity. Nothing blocks the branch; the two items below should be
corrected before it is released.

### New — significant: the formatter error field grows without bound

`ProviderSession.recordFormatterError` appends every formatter diagnostic and every
`backend_failed` preview with a separator and nothing ever clears it
(`Bozzetto.Composer/ProviderSession.fs:60-65,490`). Every `Status` reply ships the
whole string. The binary frame limit is 1 MiB (`BAREWireCodec.fs:13`); an
unencodable reply is replaced by a `FrameTooLarge` refusal
(`Bozzetto.Composer/Program.fs:53-61`). A session with a persistently faulting
document therefore eventually loses `Status` and `Close` observability. The
previous overwrite defect has been traded for unbounded retention. Keep the
last N entries or a byte bound with a truncation marker, and add a repeated-fault
test.

### New — significant: the "no delay for unrelated previews" claim fails at capacity

At 32 live handles a replacement request retires the old incarnation and refuses
itself `busy`; every other new document is also refused `busy` while any close is
pending (`FormatterSession.fs:147-154`). The branch's own test asserts this
(`FormatPreviewTests.fs`, "a retired document occupies its slot until physical
close joins", the `buffer://unrelated/33` refusal), which contradicts the
checkpoint's statement that retirement does not delay unrelated previews. If a
document close never returns, the session refuses every new document forever with
a retry hint that never succeeds; there is no deadline or escalation. Document the
retry-after-`busy` contract for clients and bound the wait.

### Calque cooperative checkpoints — clean

The stop is one per-evaluation exception instance identified by reference
equality; it is mapped to `Cancelled` only when the carried cancellation was
actually requested, so a foreign or spurious stop becomes a host fault with a
diagnostic (`DocumentFormatter.fs:181-187`). Every started conditional branch
joins and a sibling fault wins over a stop (`CodeFormatterImpl.fs:13-20`). The
lexer wrapper is unwrapped only for this invocation's recorded instance
(`Parse.fs:1076-1096`). Checkpoints are placed per token, per Oak node, through
trivia, dialect, every printer event and merge. Public `FormatDocumentAsync`
behaviour is unchanged apart from source preparation now being deferred into the
workflow. Minor gaps: no stop test for the Merge phase or inside the parallel
define-combination parses; "withdrawing the last demand unwinds" is tested only
with stub evaluators, the real-pipeline tests use `cancelCurrent` or
release-with-peer; no measurement accompanies the per-node checkpoint cost.

### Remaining minor items

- The mid-flight cancellation test proves the Bozzetto boundary (withdraw called
  after admission, peer untouched, reply after physical join). It would pass on
  `main`, which is expected for a coverage gap. The vacuous `Busy=false` assertion
  in the pre-cancelled format test remains; the meaningful assertion is now the
  zero admission count.
- `cleanupFailures` is still never cleared; a faulted document close is re-joined
  and re-recorded on every later `CloseAsync`.
- Daemon-side rejection of an old-digest worker Hello has no test; the worker side
  is tested.
- `scripts/install-hooks` exits when `core.hooksPath` is already set, and from a
  git worktree it links the shared hooks directory to that worktree's script, so
  removing the worktree leaves a dangling symlink that git silently skips.
- Lattice now reports shared-session withdrawal with a retry hint and keeps one
  document id per file with a fresh incarnation on reopen; the new tests are
  discriminating.
- Sticky `Cancelled` on a same-snapshot re-request after the last demand is
  released is unchanged, and is now reached sooner because the stop lands earlier.

### Status of the first assessment's findings

| # | Finding | Status |
| --- | --- | --- |
| 1 | Superseded format runs to completion | Narrowed: stops at next checkpoint; full-document granularity remains |
| 2 | Main pushed without release rule | Fixed: hook installed, state corrected in checkpoint; release still pending |
| 3 | Formatter diagnostics conflated with compiler error | Fixed, with the new unbounded-growth defect above |
| 4 | Tombstone bound is consumer policy | Contract wording corrected; library unchanged |
| 5 | Stale-proof refusal untested | Open |
| 6 | Clef config-restore assertion non-discriminating | Open |
| 7 | Lattice silences foreign supersession | Fixed |
| 8 | Formatter handles never released | Fixed for replaced incarnations; closing an editor view still does not retire shared state |
| 9 | Mid-flight preview cancellation untested | Fixed |

## Ranvier comparison

Ranvier (`ac96be3`) is a fine-grained signal, memo and effect graph. Its headline
instruction counts measure one shape: 1,000 signals each read by one effect
(`bench/Ranvier.Counters/Scenarios.fs:79-97`). `cutoff` is the `Signal.set`
early-out on an equal value, not memo cutoff. The method counts retired
instructions per operation under a no-GC region with tiering off, so it includes
scheduling and allocation and excludes GC and wall time. None of that is
comparable to the pipeline's unit of work, which is a whole-project check or
whole-document format. The foundation's per-step bookkeeping is quadratic over
`Map`/`Set` but runs over a handful of works per scope today; its shape will
matter before its constants, and only once graphs reach thousands of nodes.

Three ideas are worth testing. None requires taking Ranvier as a dependency.

1. **Content-keyed cutoff.** The foundation puts the attempt in a read's identity
   (`Core.fs:118,249`), so a producer that recomputes to an equal result never
   matches a cached read. Calque and Clef mint fresh tokens per generation.
   Keying the cutoff on an owner-issued value token, never on payload equality,
   would let an already-formatted document or a comment-only edit yield fresh
   eligibility without execution. This is the `createMemo` equality option in
   SolidJS terms. The authority constraint is that an equal display value must
   not hide a changed proof or artifact identity.
2. **Keep a drained success regardless of demand.** The core caches a drained
   result only if still demanded (`Core.fs:494`) and obsoletes undemanded
   attempts (`:219-231`), which is why cancel then re-request recomputes.
   Retaining the payload until scope close, with eligibility still revalidated,
   is how a signal keeps its value after its last subscriber leaves.
3. **Owner-tree scopes.** Ranvier's owner list gives O(1) unlink and
   reverse-order teardown. The foundation's flat scopes make Bozzetto
   hand-maintain live scopes, controls, observations and formatter handles.
   A parent scope that closes its children first, with `ScopeClosed` still tied to
   zero pending work, would replace that bookkeeping while preserving physical
   drain. This is `createRoot` with `onCleanup`; WrenHello uses only
   `createSignal` today.

Two smaller items: naming the supersession policy explicitly (cancel-previous,
which exists, versus keep-latest at concurrency two, which bounds latency to one
format time at the cost of CPU on the superseded run), and replacing the four
growing tombstone maps with per-key high-water stamps plus a bounded completion
ring, since every consumer already supplies monotonic revisions.

Anti-lessons, where Ranvier's model would be wrong here: implicit dependency
tracking cannot express census inputs, configuration or proof premises; effects
as side effects in a flush, with cleanups recorded rather than joined, are the
opposite of the launch recheck under the invocation fence and `awaitClose`;
per-flight cancellation tokens inside the graph are ambient cancellation, which
the foundation deliberately avoids; and payload equality policies would let an
authority change vanish behind an equal value.

Threading is a correspondence rather than an anti-lesson, after correction by
Ranvier's author. A graph is not single-threaded: one thread owns graph mutation
under the default `Guarded` affinity, while async bodies run wherever their task
runs and settle through the graph inbox (`AsyncSource.Settle` posts through
`Graph.Dispatch`, `src/Ranvier/Core.fs:2559`), `Serialised` affinity admits
varying threads under one synchronisation context, and graphs owned by different
threads update in parallel (`tests/Ranvier.Tests/Threading.fs:382,643,699`).
That is the same shape as the foundation's `AsyncMailbox`: a single coordinator
interprets commands while evaluators run on the pool within `MaxConcurrency`.
The difference is that Ranvier's inbox needs a dispatcher or an explicit pump,
whereas the mailbox drives itself. Ranvier is pre-release, so these contracts
should be confirmed by experiment before any of the ideas above are built on them.

Suggested order: content-keyed cutoff first, since it is the smallest change with
the largest expected effect on repeated previews; then retained drained results;
then high-water stamps; then the named supersession policy; then scope parenting;
and a `Core.step` micro-baseline at 1, 100 and 1,000 works to decide when the
quadratic bookkeeping would block incremental syntax reuse.

## Review of the bounded-cleanup checkpoint (798dade9)

Independent review of Bozzetto `798dade9` (source `1d878ecd`, `df74a727`; docs-only
`f7921e70` landed during the review) and Calque `a5e7355`, both pushed. Method:
five dimension reviewers (bounded evidence, cleanup deadline, supervisor
escalation, wire/docs/Calque, evidence), three adversarial lenses per finding
with majority survival, then a completeness critic; 123 agents, read-only on
the repositories, no Bozzetto build or test run, installed daemon untouched. Of
39 reviewer findings, 30 survived and 9 were refuted. The three highest-impact
items below were re-read in source by the auditor.

### Verdict

Both defects from the previous section are fixed and the checkpoint's claims
hold: formatter evidence is bounded at exactly 16 KiB with exact byte accounting,
paired surrogates are never split, lone surrogates become U+FFFD, the bound
survives reservation, cancellation and close, and the test fills past the frame
limit and roundtrips every status through the strict codec. The capacity
contract is now stated correctly and tested. The 30-second deadline is observed
without client traffic because the daemon monitor polls status on a 100 ms
timer while cleanup is pending and renews its window. Escalation withdraws every
session and runs the stop path. Calque's new tests exercise release of the last
demand and a Merge-phase stop against the real pipeline. All gate receipts,
candidate hashes, the protocol DLL pair, the worker closure and the corrected
integrity receipt verify. No blocker. Two significant items and one pre-existing
significant defect found by the critic should be fixed before release.

### Significant

1. **Worker self-termination cannot kill its process tree** (pre-existing,
   `Bozzetto.Composer/Program.fs:110`). The bounded-shutdown fallback calls
   `Process.GetCurrentProcess().Kill(true)`. .NET refuses a tree kill whose tree
   contains the calling process and throws `InvalidOperationException` (resource
   `KillEntireProcessTree_DisallowedBecauseTreeContainsCallingProcess`, present in
   the installed runtime). The exception reaches the outer handler, the worker
   exits with code 1, and any native tool still running under a Build or Run is
   orphaned. In daemon-driven escalation the daemon's own kill usually covers the
   child first, so the stop-path claim holds in the common case but not
   deterministically, and not at all when the socket closes because the daemon
   died. No test retires a worker while a native child is live. Fix: enumerate
   and kill children explicitly, or make the daemon-side kill the only authority
   and have the worker exit plainly; add one native test that retires with a
   sleeping artifact running.
2. **A failed termination poisons daemon shutdown** (`Bozzetto/ComposerSupervisor.fs:417`,
   `Bozzetto/ComposerWorkerClient.fs:331-334`, `Bozzetto/DaemonMode.fs:3618-3622`).
   After a failed stop the supervisor keeps the connection as the current worker
   and the client memoises its faulted stop task. The only recovery is a daemon
   restart, and on that restart the supervisor's stop re-awaits the same faulted
   task and throws; the daemon wraps the Composer stop and
   `performGracefulShutdown` in one handler, so graceful shutdown is skipped with a
   warning. Separate the two steps and treat an already-faulted stop as settled.
3. **The new status fields and the retirement policy are undocumented for
   clients** (`docs/mcp-tools.md:83,99-101`, `Bozzetto/ComposerTools.fs:63`). The
   `composer_session_status` row and tool description still say "pending
   revocation and cleanup errors"; neither names `formatterError`,
   `formatterCleanupPending`, `workerRetirementRequired`, the busy-retry contract
   or the autonomous whole-worker retirement. The capacity sentence at lines
   99-101 still says capacity refuses rather than evicting, which now conflicts
   with replacement-at-capacity retiring the previous incarnation.

### Minor

- `FormatterSession.cleanupFailures` is still append-only and is concatenated
  into `CleanupError`, which ships in every Status and Close reply. A session
  whose demand releases persistently fail can push those replies past the frame
  limit, the same failure shape the formatter field just escaped
  (`FormatterSession.fs:61`, `ProviderSession.fs:618`). Apply the same bound.
- The fence after a failed stop is permanent for the daemon's lifetime and is
  reported with the same `provider_unavailable` "retiring" text as a transient
  retirement, so an agent may retry forever. The fence also engages on any
  exception from the stop's cleanup (for example a socket directory already
  removed), not only on a process that did not exit
  (`ComposerSupervisor.fs:132`, `ComposerWorkerClient.fs:320-328`).
- At 32 live handles with nothing closing, a 33rd distinct document receives
  `session_capacity`, not `busy`, and no close will ever free a slot; the
  checkpoint's "busy until a physical close releases a slot" covers only the
  replacement case (`FormatterSession.fs:154`).
- The adoption contract still says Calque does not inspect the cancellation
  signal mid-computation and says nothing about the 16 KiB bound or the 30 s
  retirement policy (`docs/Bozzetto_Incremental_Foundation_Adoption.md:100-104`).
- The oversized-entry test clips exactly on a repeat-unit boundary, so the
  pair-straddle guard is proven by reading, not by assertion
  (`WorkerCancellationTests.fs:363`). The 16 KiB bound is asserted as at most,
  never as exactly, and the 30 s value is a literal duplicated in message text.
- The daemon-side rejection of an old-digest worker Hello remains untested.
- Lattice shows `busy`, `closed` and `provider_unavailable` refusals verbatim as
  error dialogs with no retry-after-busy behaviour; no client version bump is
  needed since the fields are additive and ignored.
- Each retired worker leaves its scratch tree, lease file and evidence directory
  in place; replacement is clean by construction through fresh host and epoch
  identities, not by cleanup. With 10 Hz polling during cleanup the wire logs
  are not small.
- Evidence: the integrity log cited at 798dade9 was empty because the check ran
  quietly; the implementor's correction in `f7921e70` records 211 OK results,
  and the auditor's own re-check agrees. The two new Composer-tier cases also
  run in the default tier, so 8,977 and 46 are not disjoint counts. The
  checkpoint's "formatter closure unchanged" line is content-true since the
  worker's Calque DLLs are byte-identical to the previous candidate, but the
  stated commit is the test-only one. Two sentences, the user's selection of
  whole-worker retirement and the release of all leases, are not verifiable
  from artifacts.

### Refuted or confirmed benign

Nine reviewer findings were refuted on verification. The notable ones: in-worker
sealing consulting only the addressed session is covered within one monitor tick
by the daemon-side withdrawal; a document close that throws is retained with its
fault as designed; a cleanup finishing at or after 30 s sets sticky retirement by
design; the deadline applies only `current <- None` because every operation then
refuses `closed`; the unbounded final join in the stop path is reachable only
after the process has exited; the stalled first default runs are attributable to
the nested spinner from the traced log.

### Status of earlier findings

Finding 1 of this document (unbounded formatter evidence) is fixed. Finding 2
(capacity liveness) is fixed by the deadline and escalation, with the
documentation gaps above. The five items from the first assessment that were
open before this checkpoint remain open and untouched: Composer stale-proof
test, Clef config-restore assertion, `cleanupFailures` never cleared,
daemon-side Hello mismatch untested, `install-hooks` worktree edge.

### Serialization note

The new fields reach clients through `Bozzetto/ComposerClientJson.fs`, which
uses `System.Text.Json`. The user's standing preference is `Fidelity.Data` for
any unavoidable JSON handling; adopting it at this public edge is a deliberate
dependency change to raise separately, not part of this checkpoint.
