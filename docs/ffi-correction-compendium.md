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

Fable 5.1 is the primary implementer. Codex coordinates the work; Opus 5.5 is
the separate auditor for Fable-authored increments. Z13, ONE, TWO and THREE are
peers for scoped work. Use their existing primary checkouts, preserve local
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

## Resumption

1. Review the Farscape observation independently and settle the smallest
   provenance correction in the existing owner, including real pilot behavior,
   positive and exact-refusal controls, and a discriminating mutation.
2. Resume the analyzer candidate from its verified patch only when ready; bind
   its v8 controls and required real-project gate, and resolve the LAN gate scope
   with the auditor. Preserve the stated red findings and coverage limits.
3. Continue cross-project FFI closure by capability: implementation, proof and
   differential dependencies, and synchronized consumers each need evidence.
   Do not infer completion from a commit, a single gate or a percentage estimate.

Other historic owner decisions and residuals remain in the preserved prior
compendium and their governing specifications. Reconcile them with current
source and rulings before scheduling work; this shorter record does not declare
them closed.
