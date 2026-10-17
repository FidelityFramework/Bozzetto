# FFI implementation handoff

Owner-directed transition, October 17, 2026: **Claude Code Fable 5.1 implements;
Codex GPT-6.1 Sol audits.** This is the incoming implementer's operating brief,
not another requirements record. Maintain capability decisions and acceptance
in the [FFI compendium](ffi-correction-compendium.md).

## Mandate and starting assessment

Continue the designed FFI correction across the owning projects. Complete
bounded capabilities through generation, source semantics, published graph,
lowering, artifact checks and affected consumers. Do not stop at source tests
or silently grow a general compiler-maturation project.

The carried 90% estimate was withdrawn at Bozzetto `cc968d30`. Eight of ten
roadmap families have accepted bounded profiles; this is not eight completed
families or an overall completion percentage. Four of six Phase B questions
have source-scope acceptance; Q2 and Q3 remain partial. Use compendium §1 and
§11.1, and later accepted increments rather than superseded historical tables.

| Roadmap family | Starting position and remaining boundary |
| --- | --- |
| T0 ownership/migration | Bounded provenance/refusal controls accepted; reconcile the migration checklist against later acceptances. |
| T1 scalar/extern entries | Selected ABI and declared-extern native profiles accepted; broader carriers and export/loading profiles require their own evidence. |
| T2 callbacks | Closed callbacks, selected mixed signatures and explicit/implicit normal-exit profiles accepted; spatial, retained and captured cases are not generally admitted. |
| T3 resources | Owned nullable result, immutable alias and exact release accepted; broader transport and operations are not covered by that acceptance. |
| T4 optional entries | Direct Some/None, aliases, multiple parameters and one closed helper-result profile accepted; branch selection is unfinished below. |
| T5 captured user data | No delivered profile located; environment association, registration failure and retirement require implementation. |
| T6 buffers/text | Bounded borrows/writes, relational read results, prefixes and checked/returned text accepted; public checked Console input is the next deliverable. Output cells and retention need scoped reconciliation. |
| T6-T native callback tables | No delivered profile located; per-field contracts, layout, read/write and lifetime accounts remain. |
| T7 loading/ABI | Selected program-image and exact static-archive inputs accepted; do not infer general ABI, unloading or library-export admission. |
| T8 regeneration | Actual generator outputs and selected platform bindings accepted; six Pthread modules have regeneration/source-check evidence, not native Pthread acceptance. |

At the first checkpoint, reconcile each remaining designed profile to one
existing requirement, owning pass, affected consumers and a finite acceptance
matrix. This is an update of §11.1, not a new design survey. Distinguish
unfinished designed work from explicitly deferred scope. Do not add every ABI,
desktop library, C++ template mapping, Tier 3 library or general D5/SSA work to
the finish line. Do not substitute file counts or equal-weight family counts
for completion. Keep moving on the settled next deliverable during accounting.

## Preserve the accepted baseline

Primary-checkout heads at transition preparation:

| Repository under `/home/hhh/repos/` | Pinned head |
| --- | --- |
| Bozzetto | `cc968d3048c40ed84f08a25060108f704ab186e9` before this documentation checkpoint |
| clef | `1dfc73def1c413fd30ac82ff99a19c6399fdc749` |
| Fidelity.PSG | `393a48c1ef29a862a43520a197296a9583fdc92d` |
| Alex | `7ae1535d4d3526145d0cf1870d1ceba090f9e9e1` |
| Composer | `ee0e581706549ac1881b0dfd8015cb9221312dea` |
| Farscape | `23edcf0792fd80a91f000d60ec6ddd5e554bbb89` |
| Fidelity.Platform | `8452124a5af4fb0d5d289d6f16ab47d23a07ed10` |
| ClefAutoComplete | `2bdce35d0300077577e7ed27ff472eb5a1b5b402` |

The last accepted installed workstation pair is
`~/.local/share/bozzetto/releases/20261017-ffi-checked-guard-pair`:
PSG schema45/format2, wire2, 326 payloads. Compiler SHA-256 is
`C7896B9D723BAF13AC4C5712728742ECF0999D05D5BF0A2D93E2F0EAF145A2D2`.
The preceding pair remains available for rollback. Check current readiness
before using or replacing the installation; this paragraph is a retained
acceptance identity, not a live health observation or a LAN deployment claim.

Start evidence review with these bounded local packets, not the whole history:

- `Composer/artifacts/ffi-utf8-20261016/checked-guard-fidelity/`:
  `commit-installed-native-binding-corrected.json`, `guardreview02-return.md`
  and `guardfinal01-return.md`. The last correction has 31 focused checks,
  four discriminating operand mutants and one native spot check, accepted by
  Opus. Low residuals concerning late refusal and receipt schema remain in
  that verdict; they are not permission for a new verification subsystem.
- `Bozzetto/nupkg/work-visibility/ffi-delivery-closeout/read-prefix-text-final/`:
  retained read-prefix/checked-text bindings and `gates/guard-valid/`.
- `Bozzetto/nupkg/work-visibility/ffi-assessment-20261017/`:
  `since-first-recorded-90.json` binds nine existing compiler files +4311/-695
  (+3105/-678 implementation; +1206/-17 tests). The UI's 68/+7244 comparison
  base remains unbound; do not claim this interval explains it fully.

Trust earlier accepted evidence at its recorded scope. Verify its binding;
do not spend a new baseline run merely to establish it again. These packets
contain local, ignored evidence; tracked decisions stay in the compendium.

## Exact unfinished checkout state

Clef has one uncommitted test-only change, **+76 lines** in
`tests/Clef.Compiler.Service.Tests/ClosedCallbackCases.fs`. It deliberately
exercises a real Boolean formal selecting Some/None before one during-call
foreign receiver. Current product code refuses with CCS8414/CCS8403. This is
an unfinished feature test, not an accepted positive or a new regression in
the accepted direct profile. Preserve it; do not silently commit it as green,
delete it, weaken its assertions or claim an unfiltered green suite around it.

Its packet is `clef/artifacts/ffi-reconcile-20261013/optional-branch-20261017/`:
`branchdesign02-return.md`, `source-account-amendment.md`, `g1-case03.log` and
the `g1-*03` bindings. The design is accepted with follow-ups, not implemented.
The latest source-only G1 probe observes both constructor placements at the
same slot with Bytes0/Alignment1. **Public revision Integrity admission is
not established**; do not report G1's full exit condition as passed.

Before resuming this feature, finish the existing-owner design: Baker owns
the exact Select condition/arms; the receiver must name that same selection
through its actual value and admitted immutable transport. Both arm
obligations are required. Dispatch uses the already-computed option tag, not
a re-evaluated predicate. Ordered edges come from source/formation incidences,
not enumeration of a Set. Existing SSAAssignment assigns source-authored
occurrences before Alex consumes them. Prove reversed arms, wrong-selection
refusal and exactly-once predicate evaluation. Keep nested/mutable/returned
selection beyond the agreed profile specifically refused. Reconcile the
existing internal conversion `Arms` records before adding public structure.

Farscape has three unrelated ambient edits (+392/-231 at capture):
`docs/11_Namespace_Scoped_PSG_Design.md`,
`docs/roadmap/07_type-provider-clef-fidelity.md`, and
`src/Farscape.Core/ProtocolParser.fs`. Do not overwrite, stage or claim them.
PSG's untracked `artifacts/` is retained evidence. Recheck Git status before
editing; do not assume this inventory remains live after another agent acts.

## Next bounded deliverable: checked Console input

Implement the approved graph-owned API:

```clef
Console.readln : unit -> Result<voption<string>, ConsoleInputError>
```

`Ok(ValueSome text)` carries a line, including an empty line. `Ok ValueNone`
means EOF before any bytes. Errors distinguish read failure, invalid UTF-8
and capacity exhaustion. Malformed UTF-8 is rejected. Exact public error-case
names/payloads have not been settled in a proposal; keep representation native
and raise any additional public policy choice concretely. Do not reopen the
approved outcome distinctions or introduce CLR/BCL option behavior.

Locate the existing owners first: Platform's Linux/x86_64 `Console.clef`, its
generated `Bindings/ConsoleInput/NativeInput/NativeInput.clef`, Clef's
`ClefIntrinsics.fs` and the current `Sys.readline` lowering. The last source
inventory found the public/platform path still string-only. The accepted
private read -> prefix -> checked-text profile is a dependency, not an
implementation of this public API. Move semantic operations into platform
source/graph rather than adding bounds to an opaque lowering witness.

Close a finite matrix covering a normal line, an empty line, EOF before bytes,
EOF after bytes under the stated line policy, foreign read failure, malformed
UTF-8, exact capacity and capacity exhaustion. Locate existing newline,
partial-read and capacity behavior; ask only for genuinely unsettled policy.
Every admitted copy must bind the actual read-count node and guarding path.
Keep allocation capacity, target ABI width and versioned foreign behavior
hypothesis separate. Check generated bindings, source/public graph, direct
Alex/Composer consumers and actual native behavior before claiming delivery.
Synchronize affected spec, Platform, samples and active editor consumers.

Then return to the bounded optional selection above and the reconciled
remaining T5/T6-T and other planned profiles. Each increment needs a useful
delivered capability; do not serially expand into all adjacent compiler issues.

## Architecture and proof conditions

- Extend the existing owning nanopass through Baker elaboration and completed
  saturation. PSG publishes immutable facts and validates them passively;
  Alex witnesses them. Neither backend nor reader repairs missing semantics.
- Farscape/BAREWire own generated binding contracts; Platform owns platform
  source behavior. Fidelity.FSharp.Incremental owns shared dependency/work
  bookkeeping; do not add a second invalidation mechanism or polling path.
  Reconcile actual CCS.Editor/Lattice.Server/ClefAutoComplete consumers:
  retrieval enrollment or source alignment does not prove deployed IDE coverage.
- No LLVM dialect or `llvm.ptr` in bindings or the middle end, on any target.
  Backend realization is separate. No generic SSA slots, backend-invented
  assignments, recursive compilation or global reassignment shortcut.
- Absent contracts/evidence remain Pending or refuse; distinguish Pending,
  refuted and discharged. Census obligations from the graph independently of
  emitted proof rows. Test omissions and uncovered reachable arms.
- Refutation-based discharge requires satisfiable premises and unsatisfiable
  premises-plus-negated-claim. Vacuous premises are not a proof. Query/code
  checks and audits are not solver certificates or general Rocq theorems.
  Current accepted profiles are Tier 1 plus conditional Tier 2; the runtime
  UTF-8 validator remains trusted. Do not promote its tests to a UTF-8 theorem.
- Foreign result relations over-approximate possible results per application;
  they are not functions. Descriptor behavior is a versioned external
  hypothesis. Never merge two effectful calls because arguments are equal.
- Record computation dependencies and proof premises separately, including
  descriptor/checker/lemma versions. Code that consumes a proof depends on
  its identity and validity. Test premise-only change and withdrawal as well
  as source change. Current evidence is NonReusable; stable provenance,
  deletion/incarnation persistence and general differential SSA remain explicit
  follow-ups, not delivered reuse or new obligations to solve in this tranche.

## Execution and auditor contract

Use the user's primary checkout on each machine; no worktrees, temporary
clones, copied repositories or unreviewed snapshots. Read each affected
repository's AGENTS before edits. Clef/PSG/Alex/Composer automation uses
.NET/F#/shell, not new Python drivers. Follow the owning formatter and style.

Use retrieval schema -> find -> pgq -> sources at one fresh/latest snapshot
with matching current identity; record repo/revision/path/returned lines.
Transition preparation used generation40,
`9c78307cf30295429c3e7f537048f468608c876a345961af30cba366159560be`.
It includes Bozzetto, BAREWire and Calque; old outside-index lists are stale.
Rediscover after pushes change the snapshot. Dirty diffs, ignored evidence
and bounded confirmation of located lines are the relevant direct-read
exceptions. Retrieval unavailability is reported, not replaced by broad scans.
Helpers and authentication handling are in
`~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/tools/README.md`;
its historical lease/auditor descriptions do not override current AGENTS.

Keep workstation and ONE/TWO/THREE in use for independent first-pass reading
and scoped component gates. Native Pi workers have repo/tool access; the bare
`ornith.sh` inference endpoint does not. The existing lab adapter is
`speakez-lab/platform/agents/pi-lab/lab.py` (stdin JSON: discover/delegate/
collect); do not build a replacement. Discover current capability, preserve
run IDs and journal outcomes. ONE's retained SSA reading is a lead, not an
accepted architecture conclusion. Workers pull authorized checkpoints into
their existing primary repos; record exact heads/toolchains/dependency closure.
Do not run concurrent mutations in one checkout or let a pull change a gate.
Local Magnitude/Pi availability is to be discovered, not assumed.

Every build/suite runs under a renewing Bozzetto lease with stable agent and
project scope, frozen dependencies, wait/refusal honored and release/withdrawal.
Use `tools/build/lease.fsx`; locally run built test DLLs. Source project native
journeys use composer_open_project -> reserve -> build -> run_current -> close.
Publish useful AgentWork and lease context. No polling UI or infrastructure
redesign is required for this transition. Deploy matching worker/daemon
contracts only when necessary, without interrupting another caller's work.

Send **one small packet per increment to the GPT-6.1 Sol auditor in the existing
Codex audit conversation**, using an available verified transport or owner
relay. Do not assume a second session shares this conversation. The historical
`audit.sh` launches read-only Claude Code; it neither launches Fable as a
write-capable implementer nor routes to the new Codex auditor. Do not create
a new audit harness or silently keep Opus as a second required reviewer.

The packet follows [development.md](development.md): requirement and bounded
claim; base/head plus exact diff; producer/consumer fingerprints; shape matrix;
positive and exact-reason negative controls; caught mutations and exact
restoration; commands, counts, elapsed times and retained evidence; hypothesis,
proof tier and differential limits; coded residuals. Include per-repo
`git diff --shortstat <base>..<head>` and each new file/owner with its reason.
The separately named `FFI_Increment_Handoff_Template.md` is absent; use the
existing development format rather than creating another template.

GPT-6.1 Sol reviews binding, authority, vacuity/coverage, source-to-artifact
faithfulness and consumers; it returns a scoped verdict and any earned gate.
Fable executes selected gates under leases. Broader regression needs a named
risk, exact scope and expected wall-clock cost. No automatic whole sweep;
filtered results remain NarrowedRun. Repeat accepted gates only for changed
acceptance inputs, binding mismatch, unexplained results or owner-requested
spot checks. A spanning failure earns design reconsideration, not symptom fixes.

Report meaningful progress at least every minute during active work and a
substantive assessment at each checkpoint or roughly hourly: what capability
closed, which evidence supports it, remaining bounded work, growth, elapsed
time and blockers. Commit/push useful checkpoints promptly. Do not invent a
completion percentage or promise a date from the number of passing tests.

## Bounded crash follow-up

The owner's supplied triage concerns **Code renderer PID1136308, SIGILL at
06:56:51 EDT October17**, not a dotnet process. Transition review independently
confirmed executable `/usr/share/code/code`, renderer arguments, timestamp,
truncated system core and the matching retained Crashpad dump:
`~/.config/Code/Crashpad/completed/c9726a70-a485-48ff-acfd-a5a509c41804.dmp`.

The supplied analysis reports V8 allocation/reservation exhaustion during the
Codex extension's conversation serialization, an intentional fatal trap and
no adjacent kernel OOM/GPU reset. Those deeper findings remain inherited
triage evidence; this transition did not re-extract or re-symbolize a core.
Why the renderer accumulated that memory is unestablished. No dotnet/compiler
defect or invalidated FFI gate follows from this incident. If a separate
dotnet crash is raised, obtain its own PID/time/evidence rather than conflating
the two. No system changes or upstream report were made for this review.

The original triage attachment, hashes and independent metadata receipts are
referenced under `Bozzetto/nupkg/work-visibility/ffi-assessment-20261017/transition/`.
Keep crash dumps private; do not commit or publish them. Preserve this follow-up
without making it a new blocker for FFI delivery.

Growth justification: this one new document is owned by the development
workflow and supplies the explicitly requested implementer/auditor transition
directives; it creates no product owner or parallel requirements record.
