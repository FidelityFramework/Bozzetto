# FFI correction: current standing record

This is the current handoff for the Fidelity Framework's foreign-function
interface work. It records the active scope, accepted boundaries and open work;
it is not a chronological log or a substitute for the language specification.

The previous, detailed compendium is preserved in Git at Bozzetto commit
`408d72bc4f70562a1b745b10727f217942156aed`. To read it:

```sh
git show 408d72bc4f70562a1b745b10727f217942156aed:docs/ffi-correction-compendium.md
```

Historical rulings, owner questions, checkpoint details and evidence references
remain there. Their age and status must be checked before treating them as
current. The governing specifications and the relevant component designs take
precedence over this summary.

## Remit and working agreements

The work is to make C and C++ binding generation and boundary marshaling sound
across the affected projects. Farscape must not present a declaration, measured
layout or behavioral assumption as a proof it does not have. Unsupported or
unsettled obligations remain explicit errors or `Pending`; they are not repaired
by a fallback. The boundary protects Clef's native values, storage, ownership and
lifetimes. It does not judge whether a C algorithm computes the correct answer.
A check on a returned value cannot undo a write that native code has already
performed; the guarantees depend on the actual access and execution contract.

Fable 5.1 (Claude Code) is the primary implementer and coordinates the work from
Z13; Codex running GPT-6.1 Terra at reasoning effort `ultra`, read-only, is the
independent auditor for each increment (owner direction, October 27). The owner
reviews and commits every changeset. Z13, ONE, TWO and THREE are peers for
scoped work. Use their existing primary checkouts, preserve local
drafts, and route earned builds to available machines. Push useful reviewed
checkpoints so that the workers and retrieval index have a shared revision.

Follow [AGENTS.md](../AGENTS.md) and the
[development guide](development.md): retrieval first for indexed repositories;
no secondary worktrees or clones; renewing Bozzetto leases for builds and
suites; and one independent auditor for each increment. Extend the existing
owner and justify any new file, owner, dependency or duplicated fact. A test
result, a source checkpoint and a full capability acceptance are distinct.

## Ownership

| Component | Boundary responsibility |
| --- | --- |
| Farscape | Parse native declarations, measure representations and generate bindings with explicit provenance and refusals. |
| BAREWire and Fidelity.Platform | Carry declared descriptor vocabulary and target facts; declarations are not proof of foreign behavior. |
| Clef / CCS / Baker | Elaborate and settle boundary representations, conversions, obligations and their dependencies at their owning passes. |
| Fidelity.PSG | Publish the compiler-authored facts and enforce the graph's structural integrity. |
| Alex | Consume and witness published facts; it must not invent missing source facts or silently choose boundary semantics. |
| Composer | Preserve the required facts and proof dependencies through lowering, artifacts and differential compilation. |
| Bozzetto | Coordinate work, leases and evidence; it is outside the compiler pipeline. |

The normative boundary contract is in `clef-lang-spec/spec/ffi-boundary.md`,
particularly sections 3–5. The specification distinguishes measured
representation, declared foreign hypotheses, and checks on actual values.
SSA assignment, source graph identity and native execution are separate facts.

## Stopped checkpoint — October 27, 2026

All active FFI implementation and review runs were stopped for this handoff.
Fable's bounded Farscape observation completed; the follow-up Opus analyzer
routing review was interrupted and has **no verdict**. No FFI build is running.
The Fable Claude Code conversation is `ffi-fable-primary`, session
`68f516ff-e7cd-4ba3-8537-6708a1bedb16`, under the Farscape project.

### Farscape measurement provenance

Farscape `c155a09d128f905cf9f3225fb777e0d1a68d4dea` is pushed and independently
accepted for a bounded parser correction: marked clang layout blocks that are
empty, malformed or outside the supported bit range now return an error instead
of being omitted. Its focused gate passed 246/246, its unfiltered Farscape suite
passed 1149/1149, and seven discriminating mutations were caught. This is not a
claim of native ABI execution or complete FFI safety.

A subsequent synthetic observation exposed a separate **unreviewed** issue. For
one multi-header input, the public generator APIs paired a two-`int` declaration
from the second header with a two-`long` layout measured through the first, and
emitted a mixed descriptor. Reversing the header order produced the consistent
layout. The full pilot path was not executed, and no native code or runtime
corruption was demonstrated. Evidence and its limits are in Farscape
`artifacts/measured-bit-range-20261027/multiheader-provenance-fixture/` and the
Fable session return. Independent Opus review and an owning fix remain to be
done. Anonymous-record identity, multi-header merging and C++ layout remain
separate open questions.

### Optional callbacks and SSA

The owner approved the bounded T4 source/public checkpoint with 28 reproduced
baseline failures retained. Its pushed heads are PSG
`2a0025ad2176df86acc829121dbfa10fad071548`, Clef
`6c574a39b76ba2978c8ed97cce663f93a270d5e2`, Alex
`adf6e3040f552abfb8447ddcfa0f34b7be0e1d09` and Composer
`f95f4fb487511bcba9397657cbe8acd5783b23f7`. Native conditional callback
conversion remains explicitly refused; the baseline failures are not passes.

A typed `DUGetTag` operation exists, but a boundary reference alone does not
establish its execution or ordering. The boundary pass currently recreates its
owned graph nodes, so stable graph identity, publication, SSA assignment and
native dispatch remain unresolved. The owner's direction is to retain SSA
assignments where graph structure is unchanged, advance newly allocated or
reassigned IDs within a full-pass lifetime, and cold-start on a full release
rebuild. Numeric IDs may recur in a new lifetime. This does not settle the
correspondence algorithm or the identity of a new projection.

### Alex analyzer candidate

The isolated analyzer tooling is approved. The next project-reference candidate
was **not accepted or tested at v8**. Its six-file change has been removed from
the working tree and preserved as the verified patch
`artifacts/compendium-cleanup-20261027/alex-analyzer-v8-paused.patch`; the adjacent
JSON records the base, file hashes and successful reapplication check. Reapply
only to its recorded base after checking the working tree. The earlier v7
controls and real-CLI fixture are evidence only for their recorded bytes; the
v8 focused controls, mutations and real-Alex wrapper remain outstanding. The
historical combined analyzer run exited 254 with 425 AXC002 and 27 AXC005
findings; those findings remain unadjudicated.

Analysis-time project-reference hashes do not prove bytes already mapped by
FCS. Non-project reference digest comparison and reverse coverage remain open.
The prior Fable v7 review is in
`artifacts/ffi-resume-20261020/closure-20261026/fable-f2-v7-review-20261027-return.md`;
the preliminary Opus routing review is in
`artifacts/ffi-resume-20261020/closure-20261026/opus-analyzer-route-review-return.md`.
The latter's follow-up was stopped before a verdict, so it does not authorize a
LAN gate by itself.

### Workspace and LAN readiness

Five unrelated Bozzetto drafts remain in `docs/ffi-implementation-handoff.md`,
`src/Bozzetto.Core/AgentWork.fs`, `src/Bozzetto.Core/Features/CohortOwner.fs`,
`src/Bozzetto/UiBridge.fs` and `src/bozzetto-web/src/Shared/Protocol.fs`.
Farscape's two documentation drafts and `src/Farscape.Core/ProtocolParser.fs`
remain untouched. Other repositories also have pre-existing drafts; inspect
status before changing them.

ONE, TWO and THREE were reachable with .NET 10.0.401 and local Bozzetto
controllers. Their relevant checkouts were behind pushed heads; ONE has
Farscape drafts, ONE and THREE have Alex drafts, and each PSG checkout has
untracked artifacts. Lease renewal compatibility was not established. Before a
needed LAN gate, check actual checkout, dependency and installed-controller
identities, advance only safely, and bind the granted/renewed/released lease and
results. Do not reset or overwrite drafts. Read-only probe receipts are in
`artifacts/ffi-resume-20261020/closure-20261026/lan-build-readiness/`.

## Resumed — October 27, 2026 (afternoon)

### Workstation controller and lease client

The workstation daemon was found stopped at 09:15 (last healthy probe 09:05,
PID 1065220; no implementer command stopped it) and was relaunched per the
AGENTS procedure at 09:20:56 (PID 4029319, release
`20261017-ffi-segmentation-restore-pair`, Composer configured, no component
failures). `tools/build/lease.fsx` could not compile at Bozzetto HEAD 447b27d8:
that commit removed `Capacity` from `AgentWorkBoard` and the AgentWork bounds
but left `src/bozzetto-web/src/Shared/BinaryCodec.fs` and four consumers
referencing them. The repair (5 existing files, +29/−31, no new owner) is in the
working tree: Bozzetto.Tests builds clean, "Agent work" 10/10 and "UiBridge"
42/42 (both `NarrowedRun`), the web codec round trip 270 checks/0 failures, and
the lease client admits and journals commands again (durable records in
`~/.bozzetto/operations.db`). Codex (terra01) returned **changes required** on
two seam findings that are owner decisions, recorded with code facts and exit
tests in `artifacts/codec-capacity-20261027/owner-decisions.md`: an accepted
board can now exceed the codec's 65,536-item/4 MiB limits, and the changed
AgentWork wire shape still carries protocol version 10. Do not fast-forward
ONE/TWO/THREE's Bozzetto checkouts (at a697e43f, where `lease.fsx` compiles)
until this repair lands.

### LAN readiness

Through the existing lab adapter, ONE, TWO and THREE answered read-only
readiness delegations at 09:33–09:36 (`artifacts/codec-capacity-20261027/lan-readiness-20261027/`):
each has SDK 10.0.401, a healthy local controller (PIDs 2387/2373/2374, started
October 19), `lease.fsx` present, and checkouts behind the pushed heads
(Farscape 23edcf07, clef 9aa171ba, PSG e50bd74a, Alex 832fbb3e, Composer
ee0e5817, Bozzetto a697e43f); ONE's Farscape has 7 dirty files and ONE/THREE's
Alex 25. No LAN gate was needed for this increment.

### Farscape measurement provenance — corrected, second audit recorded

The unreviewed multi-header observation is corrected at its owner.
`BindingGenerator.measurementUnits` measures each ABI-critical entry's record in
the unit of every pilot header whose own filtered list defines it (and, for a
`typedef` entry, declares that typedef), requires the merged declaration of that
record and of every record nested in it by value to match the measuring
header's own declarations, requires every typedef their field spellings depend
on to be declared there with the merged spelling, and `agreeMeasurements`
requires one effective layout across declaring units; undeclared entries are
not probed. The former refusal
"requires a measured layout; header probe did not provide it" for a storage
declared only by a later header was a misattributed cause and that case is now
admitted; the October 26 `required-layout-later*` receipts recorded that refusal
as intended. Candidate (uncommitted, base c155a09): 3 existing files
+518/−8, no new owner. Gates on Z13 under renewing leases (chain script
`run-gate-chain.sh` and `gate-chain-v6.log` in
`Farscape/artifacts/layout-provenance-20261027/`): focused 45/45, twelve
mutations each killed with byte-exact restoration (`mutations/kill-map.txt`;
the first-hop-only, tags-as-typedefs, entry-first-hop and candidates-only
mutants are killed solely by their dedicated controls while every neighbouring
control passes under them), unfiltered Farscape.Tests 1171/1171 (baseline
1149/1149, +22 rows), a 67-entry strict-checked binding manifest
(`binding-v6.sha256`; earlier rounds archived with strict-checked manifests and
documented in `EVIDENCE-NOTE.md`), and the consumer gate regenerating the four
maintained pilots requesting `abi_critical_structs` with baseline and candidate
CLIs: pthread, wayland and resvg byte-identical (7, 13 and 3 files); the
experimental pthread pilot refused identically by both (pre-existing
`experimental_native_pointer_surface` refusal); Fidelity.Platform clean before
and after by repository-identified receipts. Codex terra01 returned changes
required on five findings (nested records, typedef-bound records, pack widths, a
missing discriminating control, the earned consumer gate), terra02 on four
(field typedef dependencies, the manifest, the addendum, a stale growth figure),
terra03 on three (multi-hop typedef chains with the emitted representation, the
platform receipt, file counts), terra04 on two (tag names mistaken for typedef
dependencies, the overwritten v3 diff) and terra05 on three (the requested
typedef's chain validated only at its first spelling, headers declaring the
typedef without the record escaping validation, the archived v4 manifest); all
are answered in v6, which also records an internal adversarial panel's result.
The terra06 verdict is in
`Farscape/artifacts/layout-provenance-20261027/audit/terra06-result.md`.
Residuals: no declaration origin file (identical shapes in different files are
one declaration, now fenced by effective-layout equality); descriptor
documentation names the stratum, not the unit; tag-versus-typedef layout keys.
An independent read-only audit of the same defect class recorded four further
owner items: inferred enum widths, the profile scalar table consulted before the
unit's typedef chain, C++-detected headers probed as C, and macros merged by
bare name across units.

## Resumption

1. Farscape provenance correction: implemented and gated (above); owner commit
   of the Farscape working tree after the terra02 verdict, then push so the index
   and the LAN checkouts can follow. Owner decisions D1 (carry the declaring
   file on `StructDecl`) and D2 (name the measured unit in descriptor
   documentation) remain open; the four further leak-audit items need owners.
2. Resume the analyzer candidate from its verified patch only when ready; bind
   its v8 controls and required real-project gate, and resolve the LAN gate scope
   with the auditor. Preserve the stated red findings and coverage limits.
3. Continue cross-project FFI closure by capability: implementation, proof and
   differential dependencies, and synchronized consumers each need evidence.
   Do not infer completion from a commit, a single gate or a percentage estimate.
4. Bozzetto: owner decisions B1 (agent-board encoding bound) and B2 (protocol
   version for the changed AgentWork shape, coupled to the next matching
   daemon/worker deployment), then commit the lease-client repair.

Other historic owner decisions and residuals remain in the preserved prior
compendium and their governing specifications. Reconcile them with current
source and rulings before scheduling work; this shorter record does not declare
them closed.
