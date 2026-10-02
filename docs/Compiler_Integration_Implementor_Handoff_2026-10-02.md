# Compiler integration: implementor handoff

October 2, 2026. The owner has transferred implementation to another agent and
assigned the outgoing implementor to audit. Implementation is stopped. Preserve
the current working changes; they are unfinished work, not an accepted release.
No compiler or Bozzetto deployment was performed in this tranche.

The incoming implementation resumed on October 2 and added Calque to the
.NET-hosted provider path. Its generated schema-16 artifact accounts, aligned
candidate, unfiltered gates and remaining boundaries are recorded in the
[current cross-project checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md#october-2-source-integration-calque-and-artifact-accounts).
The stopped draft inventory and failure observations below remain the outgoing
handoff's historical evidence; they do not describe the newly gated candidate.
Scoped PSG service delivery remains unfinished and no shared runtime is promoted.

## Governing direction

The remit is one coherent incremental compiler architecture across
Fidelity.FSharp.Incremental, Clef/CCS/Baker, Fidelity.PSG, Alex, Composer and
Bozzetto. Read each repository's `AGENTS.md` before editing.

- **Fidelity.FSharp.Incremental remains the shared foundation.** Use its typed
  functional APIs for dependency invalidation, demand, explicitly started work,
  sharing, cancellation and physical cleanup. Integration may require changing
  the library; its current implementation is not immutable design authority.
- **Cold, lazy and incremental are distinct contracts.** Deferral does not imply
  caching; receipt of a packet does not imply demand. Stale demanded work needs
  re-evaluation. Invalidation and authority withdrawal can happen before demand
  starts replacement work.
- **Baker owns source semantics and complete support.** This includes demand,
  lookup/absence and collection membership, joint premises, layout, declaration
  and ABI settlement, intermediate rewrites and current correspondence. Alex is
  a passive witness of stored source facts. Neither Alex nor an MLIR pass repairs
  missing semantics or assigns replacement semantic identities.
- **One graph exit:** Clef's
  `src/Compiler/PSGSaturation/Publication/RevisionPublication.fs` copies leaf rows
  Baker prepared. Derivation at publication remains debt to an owning recipe.
  Readers reference Fidelity.PSG, never the publication assembly.
- **Service delivery is demanded scopes and affected changes.** Never send the
  full retained graph, inactive or soft-deleted bodies, including attachment and
  reconnect. An `open` changes lookup visibility; it does not demand a library.
  A missing/foreign base produces a diagnostic, not a complete-graph recovery.
- **Graph codata and realization of coeffects are distinct.** The owner's latest
  clarification is that the codata *in* the graph must be distinct from the
  realization of coeffects *of* the graph transmitted *to* Bozzetto in its role
  as incremental compilation controller. Semantic facts and contextual
  requirements remain source-owned. The controller's demanded work, supplied
  context, attempts, completion and resource lifetime need separate typed
  accounts tied to those facts. Process handles, callbacks, mutable scheduler
  state and CLR cancellation objects do not enter the semantic revision.
- **Authority is separate from retained content and work eligibility.** Baker
  establishes semantic scope; Composer owns proof discharge, artifact acceptance
  and actual execution admission. Incremental eligibility alone grants none of
  those permissions. Reserve before source writes; preserve the actual-launch
  fence and join owned work on retirement.
- BAREWire realizes the shared binary format. Replacing JSON with arbitrary
  binary control messages does not complete PSG integration. Private services
  have no JSON alternative. Public MCP/browser adapters are separate interfaces.
- Preserve the path to self-hosting. Do not restore runtime patching, Harmony,
  detours, custom MLIR plugins or .NET 11. Use .NET tooling, not Python.

The existing [transport checkpoint](PSG_Transport_Integration_Auditor_Checkpoint_2026-10-01.md)
and [incremental adoption contract](Bozzetto_Incremental_Foundation_Adoption.md)
provide the wider audit history. README files are general introductions, not
checkpoint ledgers.

## Last pushed anchor

All eleven repositories below were checked clean with local heads matching their
remote branch heads before the current artifact-account edits. Full receipt:
`~/.codex/work/psg-scope-retool-2026-10-01/boundary-anchor-roster.json`.

| Repository | Pushed head | State at handoff |
| --- | --- | --- |
| BAREWire | `571ff31` | No implementation changes |
| Fidelity.PSG | `b1f0088` | Four uncommitted files described below |
| Clef | `2593630` | Nine uncommitted files described below |
| Alex | `4e1f859` | Clean; separate body-free import coverage |
| Composer | `72cb4ff` | Two uncommitted files described below |
| Bozzetto | `6672ddd0` before this handoff document | No new runtime implementation |
| Fidelity.FSharp.Incremental | `d476aea` | Clean; consumers pin Hosting preview.6 |
| Fidelity.CloudEdge.Actor | `d4a013b` | No changes |
| Lattice (`lattice-vscode`, branch `fidelity`) | `1e60988` | No changes |
| Fidelity.Data | `213918a` | No changes |
| Thuja (branch `fidelity`) | `e1b855d` | No changes |

The [anchor narrative](PSG_Scoped_Integration_Interim_2026-10-01.md) records:

| Gate | Actual completed result |
| --- | --- |
| Fidelity.PSG | 270/270; 162 focused Integrity cases |
| Alex | 278/278, including body-free import and selective confinement controls |
| Clef | 2,205 passed, 99 failed, 2,304 total; focused 228/228 |
| Composer | 374 passed, 12 failed, 386 total; zero skips |

Composer recovered four exact identities from the preceding 370/16 anchor;
there were no previously passing regressions, added or removed cases. Eleven
remaining cases stop at `Allocation lacks one current reservation relation`.
They cover environment artifact correspondence, native execution, substitutions
and selective closure reuse. The twelfth is callable `Result` native transport
at occurrence 218. Clef's 99 failed identities remained unchanged; they are real
unresolved semantic/fixture failures, not an exemption from investigation.

An earlier local header implementation put body-free headers in executable
coverage, changing the already-failing selective closure case to a common-region
escape. The pushed repair uses a separate successful-import receipt and retains
exact body membership. The completed whole run reaches the original reservation
failure again. Compare failure messages as well as counts and identities.

The completed Composer receipt is
`composer-boundary-resumed-full/composer-boundary-resumed-full.trx` under the
external evidence root. `composer-boundary-comparison.{json,log}` compares exact
identities. `composer-boundary-component-dll.sha256` records matching producer
and copied consumer assemblies. An interrupted preceding run produced no TRX and
is not evidence. These results do not apply to the dirty Schema 16 work below.

## Uncommitted implementation: 15 files, not gated

The next repair replaces Composer's four private analytical-edge reads with
Baker-authored immutable artifact accounts. The implementation recommendation is
`~/.codex/work/psg-scope-retool-2026-10-01/artifact-account-design/PSG_Artifact_Account_Schema_Recommendation.md`.
It specifies ownership, exact fields, cross-checks and discriminating controls.

**Fidelity.PSG — four files:**

`src/Fidelity.PSG/{Codata,Empty,Integrity,Revision}.fs`

Four records/maps were added to `StorageWitnessProjection`:
`EnvironmentReservations`, `EnvironmentFactoryResults`,
`EnvironmentResidences`, `ProgramInitializationOrders`. `Revision.Schema` is
changed from 15 to 16. Structural validators have been drafted. Generated binary
codec, exhaustive Integrity inventory and optional local JSON inspection codec
have **not** been refreshed. New structural/round-trip tests are not yet in main.
Do not assume the current checkout compiles or that its generated contract agrees.

**Clef — nine files:**

- `src/Compiler/PSGSaturation/SemanticGraph/{Types,StorageWitness}.fs`
- `src/Compiler/Baker/Ingredients/PublishedFactRows.fs`
- `src/Compiler/Baker/Recipes/{ContinuationObligationRecipes,WitnessLiveRowsRecipes}.fs`
- `src/Compiler/Nanopass/{ClosureEnvironmentSettlement,EnvironmentFactoryResults,ProgramInitializationOrder}.fs`
- `src/Compiler/NativeTypedTree/NativeService.fs`

These draft changes author accounts at owning recipes, validate complete current
premises, select demanded rows, and carry them through NativeService's final
Codata replacement. Bootstrap publication mappings were added manually; reconcile
them with the repository's generator after the shared schema builds. New source
regressions have not yet been added or run.

**Composer — two files:**

- `src/BackEnd/LLVM/EnvironmentArtifacts.fs`
- `tests/Alex.Tests/EnvironmentArtifactTests.fs`

The observer now reads the four typed account maps. It selects an admitted
residence's exact reservation claim, checks the full ordered premise vector and
factory correspondence, and retains supported-inventory refusal. The test edit
only updates failure diagnostics to print the new reservation map. No completed
build or test receipt exists for this draft reader.

Important details to preserve while finishing this tranche:

1. Reservation keys are fresh claim NodeIds, not display names or allocations.
   Several proposals for one allocation must survive; admitted residence is a
   separate result. An unresolved proposal cannot disappear to obtain acceptance.
2. Factory-result relation ordinal is distinct from destination formal insertion
   index. Current fixtures have result ordinal zero and can insert at index one.
3. Residence `LayoutClaims` equals the ordered layout obligations. `BindingPath`
   is the allocation-to-initializer path. Preserve named authority/declaration
   inputs and complete role/group/ordinal multiplicity.
4. Initialization binding role has ordinal zero; value/spine/entry roles use the
   initializer ordinal. Do not normalize all of them to zero.
5. Closure settlement has an unforceable-Codata fixture. It must return explicit
   account results for final assembly rather than force early Codata.
6. Participant identities are provenance, not automatic executable-body demand.
   Source owner revalidation covers membership, absence, new reads, capacity,
   declarations and intermediate rewrite correspondence.

External preparation is in `artifact-psg-implementation/`, `artifact-baker/`,
`artifact-account-design/`. Do not copy the external scratch tree wholesale;
it includes old build outputs. Main checkout diffs are the current implementation.

## What is actually integrated, and what is not

Fidelity.FSharp.Incremental is already used in Clef
`src/Compiler/Project/ProjectWorkspace.fs` through the functional `AsyncMailbox`
API. Composer `ProjectSession` uses that workspace. Bozzetto
`Bozzetto.Composer/ProviderSession.fs` uses the same API for producers, demand,
eligibility and joined cleanup. The source checker is still scheduled as a
conservative whole-project unit; selective Baker recipe scheduling is unfinished.

Fidelity.PSG is the current compiler-to-Alex semantic contract. Bozzetto's source
implements a Unix socket BAREWire control protocol and identifies the PSG schema,
assembly digest and format fingerprint in its handshake. **It does not yet
transport actual PSG fact scopes/deltas into the controller.** Current operations
are project/build/control operations. The installed deployment remains the prior
validated distribution, with its prior private transport; it was not promoted.

The missing transport work is substantive:

- Stable source-owned cross-revision identities and row versions. Increasing an
  unused library from one to 64 declarations currently changes an unchanged
  entry ID from 117 to 684. Equal body counts/byte lengths are not zero resend.
- Typed fact ownership, replacement and retirement; complete demanded facts
  alongside occurrence/context accounts. `ScopedPublication.apply` presently
  checks metadata; the occurrence delivery does not implement general no-resend
  typed fact updates. The external roughly 150-leaf prototype is not main code.
- Private candidate receiving state and atomic install against the exact base,
  source revision and current edit authority, with acknowledgements and joined
  resource retirement. Retaining bytes is distinct from authorizing their use.
- A separate typed controller realization account, reflecting the owner's latest
  codata/coeffect distinction. Receiving source facts must not eagerly activate
  compiler work or silently grant execution permission.

Read the external
`scoped-fact-design/Receiving_Fact_Delivery_Implementation_Handoff.md` and
`identity-ledger-plan/Source_Identity_Ledger_Plan.md`. They are proposals to review
against the normative spec, not completed components. One earlier adapter proposal
would have introduced `ReceiveScopedAsync` into ProjectSession before defining
the source-producer direction. Do not implement that proposal blindly: trace
Baker publication → Bozzetto subscription/controller → authorized compiler work,
including which process owns each resident view. A client cannot invent semantic
authority by submitting an apparently valid packet.

The planned `PSG_Codata_and_Compiler_Realization_2026-10-02.md` was not created
before handoff. Complete the focused design account before expanding wire APIs.
The four artifact accounts above are semantic source facts; they do not settle
the controller realization protocol.

## Suggested resumption and audit gates

1. Review the 15-file draft, finish schema generation/mapping and add the specified
   source and structural controls. Run PSG, source and aligned consumer gates
   with exact identities. Preserve existing native operand, copied field,
   destination, startup order and no-release tests. Record any changed failure
   cause instead of describing every surviving failed identity as unchanged.
2. Capture a synchronized anchor across affected repositories. The owner permits
   useful interim commits/pushes with named failures; no need for every language
   test to pass. Keep messages salient and documentation concise.
3. Establish the codata/realization contract and source-owned identity/version
   model, then implement actual typed scope delivery using Incremental's existing
   demand/lifetime foundation. Do not grow a second scheduler or transport-only
   approximation of source dependency closure.
4. Exercise actual packets: unused library/open delivers no extra bodies; a real
   demand adds only necessary facts; unchanged work retains resident content;
   edits revoke authority before writing; late/wrong-base results cannot install;
   retirement joins work; changed premises re-enter their owning source analysis.
   Feed the received facts through proof, passive witnessing and native execution.
5. Return matched commit identities, exact gate receipts and limitations to the
   auditor in this docs directory. A component codec or direct local revision
   build does not establish receiving-path acceptance.

## Tools, resources and ownership at handoff

There is no active build/test/generator process and no active implementation
subagent. The last Bozzetto build lease `efffebdbe31e4cf3a8d2834addf1c9e5` was
explicitly released; receipt is `boz-coordination/artifact-handoff-release.messages.json`.
No daemon was stopped/restarted, compiler promoted or memory guard weakened.

Use .NET SDK `/home/hhh/.local/share/mise/installs/dotnet/10.0.401/dotnet` and set
`DOTNET_ROOT` and `DOTNET_HOST_PATH` consistently. Serialize expensive builds,
tests and probes through the existing Bozzetto leases. The external
`boz-coordination/call.sh` is a working MCP helper; `release_work_lease` takes
`lease_id`. Inspect the returned decision before starting work.

Use local and LAN inference and retrieval throughout the continuation:

- Retrieval helper:
  `/home/hhh/.codex/work/clef-2026-09-28-part2/lan/retrieve.sh REQUESTFILE`.
  Use schema → find → PGQ → sources with a pinned snapshot. It keeps authentication
  out of output. Hybrid search/spec and repository graph queries are both useful.
- LAN helper:
  `/home/hhh/.codex/work/clef-2026-09-29-closure-lifetime/lan/worker.sh` with
  `one|two|three`, `submit|status` and an external request prefix. Supply current
  uncommitted snippets directly when useful; do not pretend they are indexed.
- Local Lemonade: `http://127.0.0.1:13305/api/v1/chat/completions`, model
  `NVIDIA-Nemotron-3.5-Lightning-30B-A3B-GGUF`; observed context 32K, one inference
  slot. Use bounded tasks; do not reconfigure it just to avoid using it.
- Bozzetto remains on 47749/47750. Separate SageFS probes on 37749/37750 were
  refused, as earlier .NET probe records note; no separate F# REPL service is
  part of the continuation, so validate Bozzetto F# changes with
  `dotnet build` and its unfiltered test suite under the Bozzetto leases.
  Preserve the shared deployment and read its skill/provider instructions
  before using it.

Fresh retrieval generation 21, snapshot
`43ca44172cb94e31012327c192c89f471959cbf04f8ccc394cfafbe07f5a4883`, confirmed
the pushed Alex/Composer heads and Incremental `d476aea`. Normative spec is
`44fd9890e4332c4e60e84f93031cbd757cca0d6c`; site docs are `f5efe4b`. Read PHG §5/5.1
and incremental-computation §1/6. The site's `coeffects-and-codata.md` gives
historical design context; it does not replace normative rules. Bozzetto docs
are not in this retrieval corpus.

Artifact source review `run_8b0fd43fe43b483880b2728d1bbc67fa` was submitted to LAN
ONE, but no final reviewed result is claimed here. The PSG draft review run-id
file currently contains `null`. Earlier null, timed-out, incomplete and unrelated
model responses were excluded from evidence. Model review does not replace
executed gates or owning-source validation.

The outgoing agent will now audit the incoming implementor's checkpoint. It will
not continue modifying production code while acting in that role.
