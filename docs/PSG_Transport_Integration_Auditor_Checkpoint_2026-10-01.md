# Binary PSG integration: auditor handback — 2026-10-01

**Not integrated.** The running Bozzetto daemon talks to `Bozzetto.Composer`
using JSON over stdio. The BAREWire control protocol and Fidelity.PSG revision
codec are committed drafts, excluded from the production build. Neither the
purge tests nor the installed native workflow exercised those drafts.

This is the current transport audit entry. It supplements the
[purge checkpoint](Bozzetto_Minimal_Host_Auditor_Checkpoint_2026-10-01.md),
[Bozzetto draft checkpoint](PSG_Binary_Transport_Draft_Checkpoint_2026-10-01.md)
and [PSG draft checkpoint](../../Fidelity.PSG/docs/Binary_Transport_Draft_Checkpoint_2026-10-01.md).
Their evidence remains scoped to the source and binaries they actually tested.
This handback changes documentation only; it does not promote a compiler or daemon.

## Implementation accountability

The implementor preserved runtime patching when the requested architecture
required its removal, deferred the shared transport boundary, and initially
omitted draft files from the synchronized source checkpoint. Later removal
repaired the patching decision, but did not complete transport. Leaving the JSON
client, worker and fixtures in place increased the later migration work.
These were implementation and sequencing errors, not an owner-approved design.

Resource use also fell short of the owner's repeated direction. A returned worker
answer is not useful evidence unless its repository, source revision and claims
match the task. This handback records fresh retrieval and review outcomes below;
the implementor remains responsible for integration and repair. The auditor is
asked to challenge the boundaries and tests, not to assume implementation ownership.

## Verified source and installed boundary

Before this documentation edit, all ten checkouts below were clean and their
heads matched the named branch returned by `git ls-remote origin`. This is a
source synchronization check, not a compilation or deployment check.

| Repository | Branch / source anchor | Relevant state |
| --- | --- | --- |
| Bozzetto | main / `93f012d3` | Draft preserved after deployed purge `e971e3fa`; production transport remains JSON. |
| Fidelity.PSG | main / `63b8df4` | Schema 12; binary source exists outside the compiled project. |
| Clef | main / `ba694e1` | Owns publication of settled revisions. |
| Composer | main / `723e900` | Implementation `c1e3ff6`; receives the revision in process. |
| Alex | main / `9d53b9e` | Passive reader; transport is not its responsibility. |
| Fidelity.FSharp.Incremental | main / `d476aea` | Demand, invalidation and owned lifetime; no compiler authority. |
| BAREWire | main / `61b0bf7` | Shared encoding/framing foundation. |
| Thuja | fidelity / `e1b855d` | No change in this handback. |
| Fidelity.Data | main / `213918a` | No change in this handback. |
| lattice-vscode | fidelity / `1e60988` | No editor integration change in this handback. |

`boz status` still identifies daemon PID 215237. Its child PID 216737 was observed
running `Bozzetto.Composer.dll --stdio`. The installed daemon, worker and Composer
DLL hashes still match the three hashes in the purge checkpoint. `/health`
returned HTTP 200, API 4, and reported tight memory/degraded overall status.
This observation neither reran the native workflow nor established transport
acceptance. No service was restarted for this audit.

| Boundary | Current source evidence | Missing work |
| --- | --- | --- |
| Daemon → worker | [ComposerWorkerClient.fs](../Bozzetto/ComposerWorkerClient.fs) exposes `JsonElement`, operation strings and boxed parameters; writes serialized requests to child stdin. [Program.fs](../Bozzetto.Composer/Program.fs) reads JSON lines and emits serialized replies. | Replace both ends and their internal API with the shared typed socket protocol. Remove stdio/pipe JSON command modes. |
| Worker dispatch | [Worker.fs](../Bozzetto.Composer/Worker.fs) accepts `JsonElement` and checks protocol 1. | Typed dispatch, explicit incompatible-peer refusal and retained authority/lifetime checks. |
| Shared protocol | [Protocol.fs](../Bozzetto.Composer.Protocol/Protocol.fs) defines ten control operations. `PsgIdentity` carries only schema and assembly hash. The project and [draft tests](../Bozzetto.Tests/ComposerBinaryProtocolTests.fs) are unreferenced. | Consolidate duplicated provider data; compile one shared contract; add actual revision delivery. A hello digest is not graph transport. |
| Revision codec | [Fidelity.PSG.fsproj](../../Fidelity.PSG/src/Fidelity.PSG/Fidelity.PSG.fsproj) excludes `Binary*.fs`. [Binary.fs](../../Fidelity.PSG/src/Fidelity.PSG/Binary.fs) is draft encode/decode source. | Compiled codec, reproducible generation, independent format controls and real published-revision coverage. |
| Accepted revision | [IncrementalBuild.fs](../../Composer/src/Core/IncrementalBuild.fs) carries a revision in its build request; accepted state exposes artifact metadata. | Compiler-owned, revocable access to the exact revision, bound to session/generation and build evidence. No reconstruction from artifact paths or a status cache. |

Two identity gaps require explicit treatment. `RevisionHeader` currently contains
schema and producer, not a unique revision identifier. Composer's build gate uses
`Object.ReferenceEquals(request.Revision, request.SourceProof.Revision)` to reject
a foreign proof receipt. Serialization creates a new object: do not copy that
receipt, deserialize it as trusted, or weaken the gate to a hash comparison.
Passive observation of a revision does not require another solver run. For a new
compilation using decoded data, Composer must obtain a fresh `SourceProofReceipt`
for that exact decoded object through its existing discharge path, and separately
establish source provenance and current-generation authority. CCS/Baker owns the
premises; Composer performs external discharge. Solver success and transport
success alone establish neither provenance nor execution permission.

The broader source inventory also found:

- [Dashboard.fs](../Bozzetto/Dashboard.fs) sends a loopback JSON workflow request
  to the daemon. Calling that endpoint public does not exempt an internal
  server-to-server hop: use the shared typed operation in process, or the binary
  contract when crossing a process boundary.
- [HttpWorkerClient.fs](../Bozzetto.Core/HttpWorkerClient.fs) retains compiled
  legacy F# HTTP/JSON adapters. Production worker spawning currently refuses in
  [SessionManager.fs](../Bozzetto.Core/SessionManager.fs); this is not an active
  Composer fallback. Audit removal of unused production adapters and their
  dedicated fixtures instead of treating refusal as permanent architecture.
- [McpStdioBridge.fs](../Bozzetto/McpStdioBridge.fs) carries external MCP JSON-RPC.
  CLI health/shutdown and browser requests are client protocols. A user-triggered
  friction-report POST also sends JSON externally. These are named boundaries,
  not evidence that all JSON network traffic has been eliminated. The source
  inventory does not substitute for a complete runtime traffic audit.

## Required ownership and data path

CCS/Baker owns semantic settlement, SSA, ABI, proof premises and dependency scope.
Its single publication boundary copies those facts into `Fidelity.PSG.Revision`.
Composer owns build acceptance and execution permission. Alex reads the revision
passively; it must not gain compiler references, inference, repair or socket code.

The current [publication implementation](../../clef/src/Compiler/PSGSaturation/Publication/RevisionPublication.fs)
itself records remaining Baker-recipe debt in borrowed views, mapped spans and
bindings, MMIO, explicit demand and implied structural edges. That existing debt
must not migrate into a decoder or reader. This transport handback does not claim
that publication has already become a pure copy for every field.

Fidelity.PSG owns immutable graph data and its pure BAREWire encoding/decoding.
It must not open sockets, retain compiler sessions or contain deferred callbacks.
Bozzetto's shared protocol owns typed orchestration envelopes. Explicit host
adapters own socket/process IO and cleanup. Incremental owns demand, invalidation
and result eligibility; none of those responsibilities certifies a proof.

The required service path carries **typed control messages and complete published
PSG revisions over sockets using BAREWire**. In-process consumers can receive the
same revision directly; serialization is required where a service boundary is
crossed, not between every assembly. Do not add an encode/decode roundtrip inside
one process merely to manufacture a transport demonstration.

The delivery contract must bind the exact revision and observed evidence across
two authority domains: Bozzetto's provider host/epoch/session and adapter generation,
and Composer's compiler generation/source identity and build acceptance. These
counters are not interchangeable. Define their binding explicitly before wiring
callers. Structural integrity, content hashes
and schema agreement cannot grant freshness or execution permission. Reservation
withdraws affected current authority before source mutation; a deferred read or
late reply cannot restore it. Recheck at observation/acceptance and actual launch.
Preserve dependency-backed reuse in unaffected settled regions.

There is no JSON compatibility mode or automatic encoding substitution on private
compiler service links. Decode/version/limit failures produce an error and
diagnostic, never a partial revision or inferred missing facts. External MCP and
browser encodings are distinct client boundaries; their JSON must not be forwarded
as an internal worker protocol. Persistence and diagnostic files are not service
transport. These distinctions must be established by call sites, not filenames.

## Repair order and acceptance

1. **PSG + BAREWire:** settle format/version/fingerprint and resource limits;
   wire the codec into the project. Cover every supported shape, duplicate keys,
   strict text, exact extent, malformed input and generator drift. The draft's
   1 MiB control-frame limit is not automatically a suitable graph limit; define
   bounded revision delivery and refuse excess before allocation or publication.
2. **Composer:** retain and expose the authoritative revision with an explicit
   lifetime. Test refusal after reservation, failed build, withdrawal and retirement.
   Keep source/proof admission in its existing owners; a transport API is not a
   second publication or acceptance path. When decoded data enters compilation,
   exercise the proof-receipt boundary above, including refusal of the original
   object's receipt; passive observation must not demand unnecessary proof dispatch.
3. **Bozzetto:** integrate the single typed contract, socket client/server,
   supervisor and fixtures together. Preserve full request identity, monotonic
   observations, reserved control capacity, cold admission and joined cleanup.
   Remove the old JSON worker modes rather than retaining a selectable path.
   Include the dashboard loopback and retained private adapters in that inventory;
   migrating the worker alone does not establish all-service conformance.
4. **Cross-project gate:** send an actual nonempty compiler-published revision
   through the service socket. Feed the decoded revision into the existing
   Composer/Alex path in the acceptance fixture and compare witnessing, native
   output, proof scope and unchanged-region/object reuse with the direct path.
   Bozzetto does not become a second compiler to perform this check.
5. **Delivery:** run the whole registered suites and native provider tier against
   one matched distribution. Repeat live cold/unchanged/edited builds, prewrite
   revocation and cleanup against recorded DLL/contract hashes. Commit and push
   all changed producer/contract/consumer repositories at each usable anchor;
   source anchors may retain explicitly named failures, but are not runtime acceptance.

The discriminating controls must include:

- Hold an old revision reply, acknowledge edit reservation while the source is
  still unchanged, then release the reply. It must not become current or authorize
  execution. A new generation can proceed; stale data must not revoke that generation.
- Transfer a structurally valid old or foreign revision with a correct content
  digest. Decoding may succeed; the current-authority gate must refuse its use.
  Separately change a proof premise at its owning scope and require reanalysis
  before affected output is accepted, while justified unrelated reuse survives.
- Cancel one observer of shared work and cancel a never-written request. The peer
  must complete, admission capacity must recover, and close must join held work,
  callbacks, socket IO and the owned process. Inject cleanup failure and retain
  its failure classification and diagnostic.
- Fragment frames, truncate every boundary, supply unknown tags/version/digest,
  exceed limits and break the socket during delivery. No partial graph becomes
  visible, no alternate encoding is attempted, and cleanup remains bounded and joined.

The earlier default **8,954 passes / three ignores** and native **40/40** results
apply to the purge distribution only. Compiler failures and their precise scope
remain in the linked checkpoint. No new compiler test results are claimed here.

## Resource evidence and auditor return

Fresh retrieval snapshot
`c697e553e252667ca1bf9ce73706847d2699e6ed80cdb386c1afe2f98bc206fe`
(generation 16) contains the Clef, Composer, PSG, Alex and Incremental source
anchors above. `find → sources` retrieved the PSG codec; a PGQ query returned
project-reference relationships. Such edges do not demonstrate that the codec
is compiled or called. The repository list does **not** contain Bozzetto or
BAREWire, so their current evidence comes from local source, not an assumed index.

LAN worker two reviewed the supplied, explicitly scoped boundary description and
returned useful stale-delivery and shared-demand test suggestions. These inform
the controls above; they are not independently executed tests. Lemonade/Nemotron
was asked for one delayed-delivery race review but returned no bytes within the
45-second deadline; no finding is attributed to that attempt. Local source
reviews independently checked producer and host call sites.

Receipts remain outside the repositories at
`~/.codex/work/incremental-audit-repairs-2026-10-01/transport-audit-handback/`:
`*.source.json`, `installed.sha256`, `boz-status.txt`, `live-health.json`, retrieval
requests/responses, `worker-review.status.json` and `local-outcome.json`.
This document contains the interpretation; raw logs are not added to the docs tree.

Please return findings in `docs/PSG_Transport_Integration_Auditor_Assessment_2026-10-01.md`.
For each finding, identify the repository and revision, owning boundary,
reproduction or source evidence, and a discriminating acceptance condition.
Prioritize missing authority/lifetime guarantees and any surviving private JSON
service path. Assess a future implementation against its matched binaries;
this documentation checkpoint is not a claim that those repairs have landed.
