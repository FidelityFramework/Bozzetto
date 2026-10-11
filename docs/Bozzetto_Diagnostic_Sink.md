# Diagnostic sink

## Purpose and authority

The diagnostic sink preserves source-owned Fidelity.PSG occurrence evidence in an
independent process. It is not a compiler service, an execution backend, or a
second authority for source meaning. Composer is a differential compiler;
diagnostic capture observes compiler-selected evidence without changing its
proof, artifact or execution decisions.

The required design has three distinct responsibilities:

- **Fidelity.PSG** owns the occurrence and scoped-publication vocabulary. The
  sink validates and preserves a bounded capture, not a reconstructed compiler
  graph.
- **Fidelity.FSharp.Incremental** owns declared dependency bookkeeping and
  explicitly started work in its consumers. Diagnostic transport must preserve
  the owning operation's admission and cleanup obligations, not introduce an
  alternative invalidation or lifetime authority.
- **Ranvier** supplies the signal/memo/effect reference shape for presenting
  observations. Presentation does not establish capture integrity, compiler
  eligibility or physical drain. No Ranvier integration is demonstrated by the
  sink sources inspected here.

These boundaries follow `AGENTS.md`, particularly **Architecture**, **Language
and stack**, **Clef and Composer work**, and **Validation posture**, together
with the owner's stricter export-only JSON ruling. See
[component contracts](Bozzetto_Fidelity_Component_Contracts.md),
[foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md) and
[reactive push](reactive-push.md).

## Implementation status

“Implemented” below means present in the inspected source, not verified in an
installed release or by an executed test in this audit.

| Capability | Status and code evidence |
|---|---|
| Separate executable targeting `net10.0` | Implemented in `src/Bozzetto.Diagnostics/Bozzetto.Diagnostics.fsproj`. It imports the PSG/BAREWire closure selection in `tools/build/ComposerContract.props` and references Fidelity.Data. It does not reference a compiler project. |
| Source-owned occurrence capture and structural validation | Implemented in `src/Bozzetto.Diagnostics/Capture.fs` and `CaptureCodec.fs`, using Fidelity.PSG types and its occurrence codec. |
| Binary local request and receipt transport | Implemented in `src/Bozzetto.Diagnostics/ManagedHost.fs` and `Client.fs`, using managed named pipes and BAREWire framing. |
| Bounded binary persistence and Fidelity.Data JSON descriptor export | Implemented in `src/Bozzetto.Diagnostics/ManagedHost.fs`; CLI defaults are in `Program.fs`. |
| Optional worker capture callback | Implemented in `src/Bozzetto.Composer/DiagnosticCapture.fs`, attached to `ProjectSession` by `ComposerAdapter.fs`. |
| MailboxProcessor-driven sink process | **Required, not implemented in the inspected sink.** `ManagedHost.run` uses a serial async receive loop, not a `MailboxProcessor`. A separate executable alone does not satisfy this requirement. |
| Complete semantic PSG capture, diagnostic deltas and proof transcripts | Planned integration, not capabilities established by this capture stage. |
| Exact compiler handoff timing and joined compiler-close behavior | Not established by the worker adapter alone. These require compiler-owner source and execution evidence. |

The inspected sink process is independent in its launch contract. The client
connects to an existing endpoint; it neither starts nor stops the sink
(`src/Bozzetto.Diagnostics/Client.fs`). The worker references the diagnostics project
for the callback and client code (`src/Bozzetto.Composer/Bozzetto.Composer.fsproj`),
which is not evidence that it launches the sink executable.

The similarly named `src/Bozzetto.Core/Features/Diagnostics.fs` and
`DiagnosticsStore.fs` are not this sink. They contain retained FCS/FSI diagnostic
extraction and an in-memory map keyed by a code hash. They supply neither PSG
capture nor durable independent-process storage. Embedded F# hosting is retired;
these modules are not a prerequisite or a current workflow to restore.

## Process and mailbox contract

**Requirement:** the sink must run in a separate process driven by an F#
`MailboxProcessor`. An in-daemon mailbox, a compiler callback that writes JSON
instead of submitting typed binary diagnostic capture,
or the current serial async loop does not satisfy the combined requirement.
The sink's lifetime must be independent of the daemon and supervised Composer
worker, so already persisted evidence survives either process exiting.

The mailbox must own typed admission, capture correlation, persistence completion
and shutdown transitions. Posting a message is not a persistence acknowledgement.
Transport and filesystem effects need explicit owned lifetimes; blocking storage
must not hide outstanding work or make shutdown claim that writes have drained.
Queue admission, byte ownership and overload behavior must be bounded, not just
the number of actively executing writes. Exact queue policy is an owner decision,
not a default inferred from `MailboxProcessor`.

This extends the existing sink owner rather than creating a second store or
compiler coordinator. Its acceptance must distinguish receipt, validation,
persistence and reply delivery. Once accepted persistence is owned by the sink,
a disconnected client or cancelled observation cannot detach it. Shutdown must
seal admission and account for accepted writes before reporting completion.
The current `ManagedHost.serve` already lets persistence continue after complete
request bytes arrive and awaits it before returning; the mailbox migration must
preserve that behavior.

Foundation eligibility remains separate from compiler proof. The foundation uses
complete declared reads; it does not discover compiler dependencies dynamically.
A Ranvier memo's equal display value cannot erase a changed proof identity, and
an effect cleanup cannot certify that a filesystem operation or child process
has physically drained. See [architecture](architecture.md) and
[foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md).

## Capture scope and validation

The implemented stage is `occurrence-structure-v1`
(`src/Bozzetto.Diagnostics/Capture.fs`). An `OccurrenceCapture` contains demand and
source-version metadata, expected scope stamps, a PSG `OccurrenceDelivery`,
sparse source-context headers and explicit context roots. The checked revision
is carried by the delivery transaction.

Validation requires a standalone first delivery with an empty subscription base,
matching base and target revisions, only added scopes, no withdrawals, and an
exact account of demanded scope stamps. It invokes Fidelity.PSG occurrence and
scoped-publication validation. It also checks duplicate bodies and headers,
context paths, declaration positions and node-reference accounts.

A referenced node must be accounted for by a captured body, a declaration header
or the applicable source-declared omitted-child handle. Headers cannot stand in
for live structural bodies. Unknown references, imported traversal and required
imported boundary facts unavailable in this stage are refused. The sink must not
fetch inactive bodies or expand capture into the retained compiler graph to make
those refusals disappear.

A complete narrow occurrence account is not complete semantic PSG integrity.
`Capture.validate` explicitly excludes support truth, complete semantic tables,
proof and current-source eligibility. Persisting a capture establishes none of
those facts. A worker that dies before handing over valid bytes cannot provide a
postmortem capture through this protocol.

PSG and BAREWire schemas remain with their owners. `CaptureCodec.fs` wraps the
owner codec rather than defining an alternative PSG schema. See
[binary format and schema ownership](binary-format-spec.md) and
[component contracts](Bozzetto_Fidelity_Component_Contracts.md).

## Binary interchange and export-only JSON

**JSON is prohibited in direct daemon-worker, daemon-browser and agent-owned
interchange.** Diagnostic requests and receipts are typed binary too. Replacing
a serializer with Fidelity.Data does not make a JSON wire compliant.

The sink's implemented local transport uses one request and one reply per
connection. A BAREWire `u32` length prefixes a bounded envelope. The receiver
checks the length before allocating its body, then checks checksum, magic,
schema, stage, complete decoding and capture structure
(`src/Bozzetto.Diagnostics/ManagedHost.fs`, `CaptureCodec.fs`). Truncated or
trailing-byte envelopes are refused, not committed snapshots. The checksum is
SHA-256; it detects content mismatch but does not authenticate compiler
provenance or establish authority.

**Permitted JSON uses:** Fidelity.Data export at export edges anywhere, and the
diagnostic sink, which is required to run as its own MailboxProcessor-driven
process and export through Fidelity.Data. An export is a file or stream read by
a human or external tool, never a message between daemon, worker, browser or
agents. General exports need not pass through the sink; sink isolation remains
mandatory for diagnostic capture. Neither use permits direct JSON interchange.

The current sink export is a small descriptor, not a JSON serialization of the
complete PSG. `src/Bozzetto.Diagnostics/ManagedHost.fs`, `ManagedHost.descriptor`, uses
`Fidelity.Data.JSON`, preserves integer identities with explicit integer values,
and emits `compilerAuthority: false`. The complete received capture remains in
the binary file. Neither the descriptor nor an exported report may be fed back
as a command, compiler premise, proof token or execution permission.

MCP's externally standardized JSON-RPC boundary is the single external protocol
exception, documented in [MCP tools](mcp-tools.md). It does not permit JSON inside
owned protocols or at this diagnostic pipe.

Current direct-wire JSON is drift to remove. For example,
`src/Bozzetto/UiBridgeCodec.fs` explicitly implements an interim JSON browser codec,
and `src/Bozzetto/ComposerClientJson.fs` projects MCP/browser values. Its browser use
is not legitimized by Fidelity.Data or by the MCP exception. Migration belongs
at the owning wire seam, not in diagnostic storage. See
[reactive push](reactive-push.md).

## Persistence, bounds and receipts

`src/Bozzetto.Diagnostics/ManagedHost.fs` implements an exclusive open store lock
and a single serial receiver. It writes `<SHA256>.bare`, then exports
`<SHA256>.json`. Temporary files are flushed, closed and renamed; the descriptor
is published last. Before a successful receipt, it rechecks the stored payload's
length, digest and decoding, and compares the descriptor bytes with the expected
export. A filename or descriptor alone is not integrity evidence.

| Bound | Implemented value or policy |
|---|---|
| Transport body | At most 32 MiB, checked before body allocation. |
| Default store | 128 `.bare` captures and 256 MiB of store files. |
| Default connection I/O timeout | 10 seconds; configurable from 100 to 60,000 ms. |
| Idle lifetime | Required, from 1 to 86,400 seconds. Successful persistence renews it; invalid or idle clients do not. |
| Capacity exhaustion | Refuse new captures and retain existing evidence. No silent eviction. |

These values come from `CaptureCodec.fs`, `ManagedHost.fs` and `Program.fs`.
The PSG decoder also receives explicit collection, depth, string and value
limits in `CaptureCodec.fs`; the frame bound is not the only validation bound.
Store byte accounting includes files already present. Orphaned payloads count
against quota, and replay can verify and complete an interrupted publication.
Changing retention or deletion policy requires owner approval.

Replaying identical bytes revalidates the existing payload and export instead of
duplicating evidence. A lost receipt means **persistence unconfirmed**, not
“nothing was written.” The client checks a successful receipt's digest, byte
count and stage against its submitted payload (`Client.fs`, `Program.fs`).

The I/O timeout and idle lifetime are not filesystem cancellation guarantees.
Accepted persistence is awaited and can outlast them if storage stalls. Flush
and rename do not establish power-loss durability of directory metadata.
Do not report a deadline, disconnected client or stopped observer as physical
drain.

## Running the implemented executable

These commands describe the current serial-loop implementation, not completion
of the MailboxProcessor requirement. Publish the `net10.0` diagnostics project
with the selected PSG/BAREWire closure used by the worker. Closure selection is
in `tools/build/ComposerContract.props`; a matching selection is not proof that two
installed deployments contain identical bytes. Deployment and rollback checks
belong in [operations](operations.md).

Start the sink independently, with an absolute external state directory:

```sh
dotnet /absolute/sink/Bozzetto.Diagnostics.dll \
  --pipe fidelity-diagnostics \
  --store /absolute/external-state/bozzetto/diagnostics \
  --idle-seconds 3600
```

`Program.fs` also accepts `--max-captures`, `--max-store-bytes` and
`--io-timeout-ms`. `ManagedHost.run` restricts the named pipe to the current user
and prints `DIAGNOSTIC_SINK_READY pipe=...` after listener creation. The open
`sink.lock` handle prevents another sink from writing that store even through a
different pipe name. A remaining lock filename alone does not mean a sink lives.

Configure `BOZZETTO_DIAGNOSTIC_PIPE=fidelity-diagnostics` in the worker's launch
environment. `DiagnosticCapture.fromEnvironment` returns no callback for an
unset or blank value. When set, the callback converts compiler-supplied
`BuildCapture` values into the occurrence envelope, uses a ten-second client
exchange deadline, and reports source refusal, transport failure or a receipt to
worker stderr. Encoding occurs before that exchange deadline (`Client.fs`).
The callback returns no compiler-authority value and does not launch the sink.

A finite replay uses an existing binary capture:

```sh
dotnet /absolute/sink/Bozzetto.Diagnostics.dll submit \
  --pipe fidelity-diagnostics \
  --capture /absolute/capture.bare \
  --timeout-ms 10000
```

Do not claim that sink readiness establishes worker capture activation or a
successful compiler handoff. Verify those separately. Capture placement before
proof dispatch and joined ownership through compiler close require evidence
from the compiler owner, not assumptions about this adapter.

## Acceptance and evidence limits

The remaining mailbox implementation needs positive and specific negative
controls plus a mutation caught by a test for each new rule. At integration,
retain one unfiltered TRUST acceptance run as required by `AGENTS.md` and
[development](development.md). Acceptance should include malformed/truncated
frames, unavailable boundary facts, quota refusal without eviction, duplicate
replay, corrupted stored bytes, lost receipts, disconnect during persistence,
store exclusion, saturated admission and shutdown with a delayed write. Check
that sink failure cannot grant or revoke compiler proof or execution authority.

This audit read the cited local source files and project manifests. Directory
reads were unsupported, so project manifests supplied source locations. The
harness provided only file read/write tools: no fresh retrieval schema, revision
verification, build, test or installed-process probe was available. No
outside-index exemption or deployed acceptance is inferred from that limitation.
The source evidence confirms binary capture and bounded export storage; it also
confirms the outstanding MailboxProcessor requirement. Compiler-internal timing,
full lifecycle integration, native hosting and distribution remain separate
claims requiring their own evidence.
