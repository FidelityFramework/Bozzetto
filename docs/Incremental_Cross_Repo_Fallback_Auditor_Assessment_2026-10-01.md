# Cross-repository fallback audit — 2026-10-01

This assessment reviews the [synchronized checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md)
at Bozzetto `3440fe9b`. It preserves the earlier independent assessments and the
fallback point. The auditor changed no implementation, package, compiler
distribution in service, or shared daemon. External controls and fresh builds
were used to test the actual contracts rather than infer acceptance from test
presence.

**Keep this checkpoint as a useful integration fallback. Do not yet treat it as
complete compiler parity or promotion acceptance.** The original C1–C5 repairs
address their demonstrated problems. This audit additionally reproduced a new
publication-authority omission, a proof-owner fault-classification defect, and two
inherited host reliability defects. One further monitor scheduling issue is
source-confirmed but has not been reproduced dynamically.

The known compiler failures are not being counted as integration regressions.
The [companion backlog guidance](Incremental_Compiler_Backlog_Auditor_Guidance_2026-10-01.md)
gives repair ownership, sequence, discriminating tests and shortcuts to avoid.

## Scope and identities

| Repository | Audited revision | Role |
| --- | --- | --- |
| Bozzetto | `3440fe9b`, implementation `fd10e283` | Provider sharing, transport cancellation, status, invocation and retirement |
| Fidelity.FSharp.Incremental | `d476aea`, implementation `3b86e2dac96ad55cb965341bfc04395061d09c46` | preview.6 mailbox, shared demand, observation and owned cleanup |
| Clef / CCS | `6a5c836` | Immutable capture, serialized checking/projection, Baker settlement and publication |
| Fidelity.PSG | `e2effe3` | Schema 12 sequence-pull correspondence |
| Composer | `6440afd36531f0203a72857b1599a040fa80a0bf` | Reservation, proof/artifact authority, native processes, editor and solver lifetime |
| Alex | `9d53b9e` | Passive consumer of the published compiler contract |

Initial status and full HEAD receipts, including BAREWire and Fidelity.Data, are
in `/home/hhh/.cache/bozzetto/audits/cross-repo-fallback-2026-10-01/` below,
abbreviated **audit evidence**. Prior assessment hashes are recorded in
`earlier-assessments.sha256`. Each consumer's vendored preview.6 archives match:

- Core package: `cc017440e0fadd177b3ba8cc2ab0f809aa754728b52f45310d998271af9562f9`.
- Hosting package: `1afaa709250d578b14bea8fa15a59ed8a227f86f003e3653017106db775522e5`.

The current library is within audit scope. Earlier acceptance is evidence for
that earlier revision, not a prohibition on improving an owning shared contract.

## Additional findings

### A1 — Withdrawing sequence authority does not invalidate passive publication

**High priority; new with the sequence-authority addition; independently
reproduced.** [WitnessEmission.fs:85](../../clef/src/Compiler/PSGSaturation/SemanticGraph/WitnessEmission.fs)
compares the prepared graph's retained roots, but omits the newly added
`SequenceRangeProvenance`. This contradicts the method's all-roots contract.

A checked mapped-sequence fixture has a valid sequence-range receipt and passes
numeric validation, passive witness reading and final revision publication.
Constructing `{ graph with SequenceRangeProvenance = None }` preserves the nodes,
edges and cached projection. The resulting graph is refused by
[NumericPublication.fs:16](../../clef/src/Compiler/PSGSaturation/SemanticGraph/NumericPublication.fs),
but is still accepted by `WitnessEmission.tryRead` and
`RevisionPublication.publish`. The retained original graph continues to publish.

The external `probes/SequenceAuthorityProbe.fsx` and
`sequence-authority-probe.log` record this exact counterexample. This proves a
revocation bypass at the passive publication boundary. It does not demonstrate
an ordinary user edit reaching that state or an incorrectly executing native
artifact; those would be additional claims.

**Repair:** add this authority root to the prepared-input identity fence. Keep
the reader passive; do not repair it by rerunning numeric settlement from Alex
or publication. Cover unchanged receipt, removed receipt, replaced receipt and
the retained original graph through both passive reading and final publication.
Add a maintenance guard requiring an explicit identity/invalidation decision
whenever a semantic graph root is added. The existing freshness tests exercise
numeric validation; they did not cover this distinct cached publication path.

### A2 — The proof owner converts a faulted cancellation exception into cancellation

**Medium priority; defect in the new ownership wrapper; independently
reproduced.** [Server.fs:35](../../Composer/src/Lattice.Server/Server.fs) classifies
every `OperationCanceledException` as cancellation, using the exception type
after the shared CLR bridge has preserved the original failure.

The probe supplies `Task.FromException<int>(oce)`. That task is **faulted**, not
canceled. `ProofTaskOwnership.Own` returns a canceled observation and omits the
exception from retained failures; after retirement, sealing and joining,
`JoinAsync` succeeds. Controls establish that an ordinary fault is retained and
a genuinely canceled task is treated as cancellation. See
`probes/proof-owner-fault-kind.fsx` and `sage/proof-owner-probe-final.json`.

This is an internal fault-reporting/ownership defect. The original physical
join repair remains valuable: the probe does not show an early process exit,
false proof acceptance, or a naturally occurring cvc5 trigger.

**Repair:** preserve the task's terminal classification at the CLR boundary.
Exception type, token identity, and whether a token was requested are not
substitutes for that classification. A typed completion result at the explicit
interop seam is one suitable design; retaining the exact task's terminal state
is another. Keep the functional owner API quiet and typed. Add controls for a
faulted OCE with both requested and unrequested tokens, a synchronous factory
fault, genuine cancellation, and a retired observer whose fault must still reach
close. If a generally useful shared bridge is required, change and version the
library rather than reimplementing ambiguous semantics in each consumer.

### A3 — Never-written cancellations consume abandoned-request capacity forever

**Medium priority; inherited availability defect; independently reproduced.**
[ComposerWorkerClient.fs:254](../Bozzetto/ComposerWorkerClient.fs) adds a removed
request to `abandoned` even when both `Sent` and `Writing` are false. The sender
then sees no pending request and skips it. No wire reply can arrive to remove
that tombstone.

The probe cancels through a public serialization getter, after the initial
cancellation check and before registration. After 4,097 such requests the client
exceeds its 4,096 abandoned-request bound, terminates the healthy worker, and
faults an unrelated held request. All 4,097 canceled observations complete as
canceled. A control with 4,097 requests canceled before entry preserves the
worker and does not invoke serialization. The timing is deterministic; no private
state was injected. The real .NET child is cleaned up by the probe.

See `probes/UnsentCancellationProbe.fsx`, `sage/unsent-cancellation.json` and
`probes/evidence/unsent-cancellation-63c4715d9f6449babc882b601310122f/result.json`.
The observed peer error is `Composer worker exceeded its abandoned-request
limit.` This is not evidence that a typical editing session generates that burst.

**Repair:** retain late-reply tombstones only for requests that may have reached
the transport, deciding that atomically with the writer state. Preserve the
possibly-writing case and C2's reserved cancellation slot. Test pre-entry,
serialization-time, queued-before-write, writing and sent cancellation separately;
the first three must not leave unreachable tombstones. Do not simply raise the
capacity or discard possible late replies.

### A4 — Failed construction can retain the exclusive directory lease until GC

**Medium priority; inherited availability/lifetime defect; independently
reproduced.** [IncrementalBuild.fs:79](../../Composer/src/Core/IncrementalBuild.fs)
acquires `.session.lock` before the initial `writeStatus` at line 164. If that
initialization throws, no reachable session exists to dispose the lease.

Precreating `current.json` as a directory makes construction throw. Removing
that obstruction and immediately retrying still fails because the exclusive
lease remains held. Explicit finalization releases it. Positive controls show
that a live owner correctly excludes a second owner and normal disposal permits
immediate reuse. The test suppresses incidental GC during the failure/retry
interval; this makes a real lifetime defect deterministic rather than relying
on collection timing. See `probes/failed-construction-lease.fsx` and
`sage/failed-construction.json`.

**Repair:** make acquisition and initialization exception-safe. Validate paths
before acquisition where possible and release every acquired resource on failed
initialization. Apply the same ownership reasoning to
[CompilationOrchestrator.fs:408](../../Composer/src/Core/CompilationOrchestrator.fs),
where a native session is created before later project/workspace construction.
The outer case is source risk, not a second executed reproduction. Keep ordinary
close joined; do not use forced GC as recovery or weaken exclusivity.

### A5 — A monitor can miss work while it is deregistering

**Medium priority; inherited; source-confirmed only.**
[ComposerSupervisor.fs:279](../Bozzetto/ComposerSupervisor.fs) can set `polling`
false, after which another operation makes reconciliation necessary. That
operation's `reconcile` sees the old key still registered at line 260 and does
not start a monitor. The old task then removes the key at line 283 without a
recheck. Future explicit reads can restart monitoring, but none is guaranteed;
the change-notification subscriber does not itself refresh status.

The consequence is stalled background progress reporting, not demonstrated
stale execution authority. No dynamic counterexample was run in this audit.

**Repair:** coordinate normal monitor exit and deregistration with a recheck or
explicit handoff to a successor, preserving retirement and timeout bounds. Hold
a test barrier between `needed=false` and removal; admit another cancellation
whose cleanup remains busy, let it settle, then release the exiting monitor.
Require a background refresh and settled notification without a `SessionsAsync`
call that would conceal the missed wakeup.

## Original C1–C5 repair assessment

| Earlier finding | Assessment of the repaired contract |
| --- | --- |
| C1: mismatched native/checker reservations | The immediate reservations share one ordering gate; acknowledgement waits occur outside it. The recorded red control discriminated the original race. |
| C2: cancellation loses its transport slot | Atomic reservation/transfer prevents ordinary callers taking the cancellation slot and retains the 256-request bound. Real child-process controls cover build and run. A3 concerns never-written requests, a separate case. |
| C3: delayed same-generation status overwrites newer state | Provider capture sequence and daemon activity fencing address the race. Stable active observations may report progress while explicitly denying fresh/current artifact authority. A5 concerns monitor retirement, not snapshot ordering. |
| C4: solver cancellation does not join exit/output | Retained proof ownership, uncanceled physical exit and I/O joins, and cancellation-callback joins address the original early-retirement defect. A2 narrows a remaining failure-classification issue. |
| C5: refused run leaves cached current artifact | Typed backend authority is inspected outside the provider gate; withdrawal affects only matching state and commits library invalidation before refusal. Retained successful replies recheck eligibility. Newer reservations and independent observers have controls. |

The author recorded an unfiltered default `Trusted` row (9,788 passed, four
ignored, 9,792 registered/executed) and an entire Composer tier `Trusted` row
(40/40) after these repairs. Those native receipts used the explicitly named
intermediate `compiler-preview6-c1-c4` distribution, before Clef `6a5c836` and
PSG schema 12. They cannot identify the final aligned binary closure.

## Independent validation

The following are audit-owned runs, distinct from the author's receipts.

| Gate | Result and interpretation |
| --- | --- |
| Incremental library | Fresh isolated Release build; **108/108 pass**, unfiltered. |
| Fidelity.PSG | Fresh Debug build; **103/103 pass**, unfiltered. |
| Clef | Fresh Debug build; **2,148 pass, 99 fail, 2,247 total**, zero skipped. |
| Clef identity comparison | All 2,224 prior cases retained, **23 additional cases pass**, no missing cases, exactly the same 99 failing identities. |
| Composer and editor builds | Both pass with SDK **10.0.401**, targeting net10.0. Existing build warnings remain recorded. |
| Composer | **380 pass / 381 total**, one known callable-`Result` failure, zero skipped, on preview.6 and the final aligned compiler/PSG source. Exact identity/outcome comparison with the earlier repaired suite is unchanged. |
| Editor default | **23 groups pass**, then the original program-lifetime fixture fails CCS8403. This fail-fast executable does not run later groups. |
| Editor string encoding, run separately | Fails initial valid-input admission with CCS8403: no validated source snapshot allocation and no declared writable stack space. This closes an observation gap; no before/after result establishes it as a new integration regression. |
| External program-lifetime controls | Original refuses; corrected CPU and synthetic FPGA declarations pass the **unchanged complete projection gate**. Removing only the CPU byte offer refuses. |
| Bozzetto | Reviewed the author's unfiltered default and 40/40 native receipts and independently exercised A3 against the transport with a real child. **The final aligned default/native tiers were not rerun by this audit.** |

Evidence includes `library.trx`, `psg.trx`, `clef.trx`, the exact comparison TSVs,
build logs, exits and tested-binary SHA-256 manifests. The Clef test binary
manifest was checked before and after execution. A pass-count comparison alone
would not have established the absence of removed or newly failing cases.

The four reproduced findings have isolated controls; they are not additional
official suite failures. Earlier failed probe submissions remain historical
attempts, with the final successful diagnostic receipts identified above.

### Editor fixture assessment

`program-lifetime-original.log` records the original refusal. First-round
declared-carrier controls exposed missing runtime agreement (and, for FPGA,
clock/calibration), so those attempted positives are retained as failures.
Refined controls add the complete explicit contract:

- `program-lifetime-representation-aligned-cpu-r2.log`: zero compiler errors;
  numeric and memory projections admit.
- `program-lifetime-no-byte-cpu-r2.log`: compiler-owned refusal remains.
- `program-lifetime-representation-aligned-fpga-r2.log`: zero compiler errors;
  numeric and memory projections admit with declared synthetic timing inputs.
- `program-lifetime-full-control-r2.log` and
  `program-lifetime-full-fpga-r2.log`: the existing `ProgramLifetimeProjection.run`
  passes all declaration, exact-span, source-definition, unsaved edit/repair,
  retained snapshot and startup assertions for each candidate.

These controls establish an actionable **fixture repair**, without changing
compiler logic or reducing an assertion. They do not establish native FPGA
execution. Inputs, diffs, hashes and public projection evidence are under
`probes/program-lifetime/`. The main fixture was left unchanged for its owner.

For the separately executed string-encoding group, the fixture's sole memory
space is read-only image storage. `ArrayMemoryRecipes` requires a writable stack
for its dynamic allocation, which is in turn required by the string snapshot.
The owner should first declare the intended stack and rerun every existing
assertion. An external candidate and exact diff are in `probes/string-encoding/`;
**that proposed repair was not executed or applied**. Further allocation/lifetime
failures may remain after it. See `editor-string-encoding.log` for the observed
failure, rather than treating the proposed candidate as green evidence.

### Interpreting the 99 retained compiler failures

The failures are stable by identity, not necessarily independent root causes.
Their first reported diagnostics are CCS8011 in 81 cases, CCS8403 in 13, and
direct assertion failures without those diagnostic codes in five. In particular,
all 21 BorrowedView cases stop in the shared fixture's `noErrors` admission before
their specific escape/access/revocation assertions. They therefore neither prove
those protections were exercised nor demonstrate unsafe acceptance. Restore the
fixture's earliest owning premise and rerun the original negative assertions.

SequenceAggregate's five failures need separate attention: assertions reach
snapshot preparation/runtime construction, including three missing expected
refusal rows, one missing expected allocation and an unsupported Option frame
field. Those are more informative than a setup failure and should be diagnosed
at source settlement/residence before widening backend acceptance. The companion
guidance gives a triage order. Baseline equality is regression evidence, not
semantic correctness or permission to ignore failing safety controls.

## Scope that is now useful, and scope still open

The shared library/CCS/provider integration has useful concrete behavior:
same-revision work can be shared by independent demands; reservations bypass
occupied evaluation slots; cancellation of an observer does not itself revoke
another observer's demand; and ownership continues through evaluator and
cancellation-callback completion. Checking and immutable projection share the
compiler's process-wide gate in the inspected consumer paths. Composer remains
the final proof, artifact and actual-launch authority.

These statements do not imply cross-process shared compiler state, cross-session
producer deduplication, arbitrary source-level incremental reuse, native
continuation lowering, or H2/H3 distributed/HMR acceptance. Each reserved CCS
generation still receives fresh whole-project checking. Separate provider opens
remain separate sessions.

Additional boundaries to keep explicit:

- Protocol v1 lacks exact retained wire-request reconciliation after a lost
  reply. Status is not an acknowledgement of a particular reservation. Define
  retained request identity, reconnect re-observation and bounded forget/expiry
  semantics before promising network-transparent recovery.
- Two solver dispatch slots bound active solvers, not all queued requests or
  proof-cache memory. Specify queue admission and demand policy before claiming
  bounded end-to-end scheduling. Keep short control transitions responsive.
- `ProcessLifetime` joins its started process and supplied I/O. This is not a
  blanket guarantee about arbitrary detached descendants.
- ProofDispatch converts some non-cancellation process errors into domain
  `ProofResult` errors. Decide explicitly which failures must also survive
  cache retirement as physical-cleanup failures; thrown-fault retention alone
  does not establish that broader guarantee. No additional runtime counterexample
  is claimed here.
- The current configuration-restoration control uses insufficiently distinct
  values to prove isolation by itself. Add A→B→A configuration controls, a
  throwing projector and a competing configuration. Source restoration is
  present; this is stronger discrimination, not a demonstrated leak.
- Legacy public `NativeService.createFreshTypeEnv` remains outside the new
  serialization boundary. The inspected current consumers do not call it;
  describe the supported entry points rather than all historical APIs as safe.
- Publication still calls `Baker.Closures.structuralIncidence` while publishing
  a revision. This predates the integration. Move that semantic computation
  under compiler ownership and publish materialized rows in a subsequent
  architecture cleanup; do not delete required incidence to make the reader
  look passive.

## Toolchain and deployment

The user has explicitly selected **.NET 10** unless a concrete need justifies
otherwise. SDK/runtime 11 should not remain the default merely because it came
from the SageFS baseline. A first SDK 11 Composer restore failed NU1605 through
implicit FSharp.Core 11 dependencies; selecting SDK 10.0.401 built the same
Composer/editor source successfully. The failed restore is retained in
`composer-build.log` / `editor-build.log`; successful SDK 10 receipts have
`-sdk10` in their names.

Keep host SDK/runtime, F# language/compiler packages and native compiler targets
as separate choices. The inherited FCS/FSharp.Core package pins do not themselves
require CLR 11. The companion guidance records the .NET 10 migration scope.
No migration was applied to the main checkout or installed runtime.

An isolated detached worktree was used for an SDK 10 experiment. The worker
built successfully against the freshly captured Composer closure. Command-line
target overrides did not cover every referenced project; temporary changes to
the external copy's SDK/default target and web fixture allowed Core, Host, CLI,
Simulation and fixtures to produce net10 outputs. The full test-project build
was stopped when the user reaffirmed the auditor-only scope. It has **no complete
acceptance receipt**; its partial output and exit value must not be counted as
a passing build or test tier. That worktree and its experimental edits were
removed, and the lease released. Only the diagnostic logs and experiment diff
remain as history. The implementor owns the migration and its complete checks.

The shared Bozzetto daemon and its previous compiler were preserved. Fresh
compiler/provider validation, packaging and service promotion are separate
operations. Before promotion, retain the exact package/closure manifest and
repeat reserve/build/run/reuse plus revocation through the deployed MCP/browser
service. A passing owned test daemon does not identify the installed worker.

The audit's SageFS probes used an owned bare session, `1bc46351`. The editor
fixture probe stalled in that session; it was canceled and the session stopped.
Standalone bounded FSI execution was used for the blocked compiler controls,
with the exception disclosed. This is an audit-harness limitation, not a passing
product test or a demonstrated editor deadlock. Shared build/test leases were
used for the full validation runs.
