# Binary persistence and interchange

## Scope and authority

Bozzetto persists a daemon session registry in `daemon.bozzettofm`. This is a
host manifest, not a compiler graph, proof cache, executable image or network
protocol. Disk persistence and direct interchange have distinct owners and
validation rules. Sharing binary primitives does not make their bytes
interchangeable.

The governing rules are `AGENTS.md`, especially **Architecture**, **Language and
stack**, **Clef and Composer work**, and **Validation posture**, together with
the owner's direct-interchange ruling of 2026-10-11. Direct daemon-worker,
daemon-browser and agent-owned interchange must be typed binary. JSON is allowed
only as an export format through Fidelity.Data and in the independent,
MailboxProcessor-driven diagnostic sink process. MCP's externally standardized
JSON-RPC boundary is the sole external protocol exception; it does not authorize
JSON on internal or other direct client boundaries.

Three responsibilities must remain distinct:

- **Fidelity.PSG** owns published semantic data and its structural contracts.
  Compiler owners supply source authorization, proof and artifact acceptance.
- **Fidelity.FSharp.Incremental** owns declared dependency bookkeeping and
  explicitly started work. Decoding bytes does not establish eligibility,
  commit an Offer, withdraw demand or prove physical drain.
- **Ranvier** supplies the signal/memo/effect reference shape for reactive
  presentation. Equal display values cannot collapse proof identities; effect
  cleanup cannot certify resource retirement.

See [component contracts](Bozzetto_Fidelity_Component_Contracts.md),
[foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md) and
[reactive push](reactive-push.md).

**Evidence scope:** the implementation descriptions below come from direct reads
of the named checkout files, not execution or installed-daemon inspection. This
read/write-only audit could not obtain a fresh retrieval snapshot or pin the
checkout revision. External schema references identify their owners, not a tested
integration. No build, test or transport acceptance is claimed here.

## Durable manifest: `.bozzettofm` v1

The implemented codec is
[`Bozzetto.Core/Features/ManifestPersistence.fs`](../Bozzetto.Core/Features/ManifestPersistence.fs).
Its domain values are session ID, project list, working directory, creation time,
optional stopped time and optional active session ID. The file is named
`daemon.bozzettofm` within the directory supplied to `ManifestFile`.

[`DaemonManifest.fs`](../Bozzetto.Core/Features/DaemonManifest.fs) owns the registry
state and mutations. A registry entry is not evidence of a live process or
permission to execute. In particular, persisted entries cannot restore the
retired embedded F# provider. Current Composer work still requires an explicit
`.fidproj` and compiler-owned admission; see
[Composer workflow](composer-workflow.md).

### Encoding and header

Manifest numeric fields are fixed-width little-endian values written with
`BinaryWriter`. A string is `u32 byte_length` followed by that many UTF-8 bytes.
An optional string uses `0xFFFFFFFF` for `None`; zero length means `Some ""`.
These are manifest primitives, not BARE's variable-length string encoding.
[`BinaryFormat.fs`](../Bozzetto.Core/BinaryFormat.fs) owns the primitives and CRC.

The writer emits a 64-byte header, one 16-byte directory entry and a SESS payload.

| Offset | Bytes | Field | Writer value or meaning |
| --- | ---: | --- | --- |
| 0 | 4 | Magic | ASCII `SFM1` |
| 4 | 2 | Format version | `1` |
| 6 | 2 | Minimum reader version | `1` |
| 8 | 4 | Section count | `1` |
| 12 | 4 | Flags | `0` |
| 16 | 8 | Created-at milliseconds | Signed Unix epoch milliseconds |
| 24 | 8 | Total file size | Unsigned exact byte length |
| 32 | 4 | Session count | Number of entries |
| 36 | 4 | File integrity CRC | Whole-file CRC with these four bytes zeroed |
| 40 | 4 + N | Active session ID | Optional length-prefixed string |
| 44 + N | Remaining header | Padding | Zero bytes through offset 63 |

For `None`, the optional field occupies four bytes. For `Some`, at most **20
UTF-8 bytes** fit in the header. This is a byte limit, not a character limit.
The writer throws on header overflow; `ManifestFile.save` converts that failure
to an error. This limit does not apply to IDs inside SESS.

### Directory and SESS payload

Each directory entry is:

| Relative offset | Bytes | Field |
| --- | ---: | --- |
| 0 | 4 | Tag, unsigned integer |
| 4 | 8 | Payload offset from file start |
| 12 | 4 | Payload CRC |

The writer uses numeric tag `0x53455353`, named SESS in the codec, and payload
offset `80`. Use the numeric value for byte inspection rather than assuming the
label's ASCII spelling is the on-disk byte sequence. There is no section-size
field. The reader derives SESS's end from the smallest directory offset greater
than its start, or from the actual file end if none exists.

The payload is a fixed-order sequence:

```text
u32 entry_count
repeat entry_count times:
  lp-string session_id
  u32 project_count
  repeat project_count times: lp-string project
  lp-string working_directory
  i64 created_at_ms
  i64 stopped_at_ms        (-1 means None)
```

There are no assembly blobs, captured bindings, interactions or evaluator
outputs in this manifest. Retired FSI snapshot formats are not part of this
specification. Retained test-cache helpers in
[`DaemonPersistence.fs`](../Bozzetto.Core/Features/DaemonPersistence.fs) do not
make those caches a Composer interchange or acceptance contract.

## Manifest validation and its limits

### Implemented reader checks

`ManifestReader.read` returns `Result<DaemonManifestData, string>` and converts
parse exceptions into errors. It performs the following checks in code:

1. Require at least 64 bytes and magic `SFM1`.
2. Reject format version zero or a version above the current writer version.
3. Compare the stored whole-file CRC with the computed CRC.
4. If declared total size is nonzero, require it to equal the actual length.
5. Bound section count by the directory capacity of the supplied byte array.
6. Read the optional active ID and enforce the minimum-reader version gate.
7. Require a SESS entry and require every directory offset to be below file end.
8. Derive the SESS slice and validate its CRC before parsing it.
9. Bound entry count by `(payload_length - 4) / 24`, and project count by the
   remaining payload divided by four.
10. Bound each SESS string by the remaining section bytes and `Int32.MaxValue`.
    Timestamp construction can fail and is covered by the parse-error handler.
11. If the header session count is nonzero, require it to equal the parsed count.

The entry-count divisor is the implemented conservative cap. The actual minimum
entry encoding is 28 bytes, including three prefixes/count fields and two
64-bit timestamps. Do not describe the cap as an exact layout size.

### What CRC does not validate

`Crc32` uses the reflected polynomial `0xEDB88320`, initial value `0xFFFFFFFF`
and final XOR `0xFFFFFFFF`. The file CRC covers every byte with offsets 36 to 39
zeroed; the SESS CRC covers the derived payload slice. These checks detect
accidental corruption. They are neither authentication nor structural validation.
A consistently malformed file can carry correct CRCs.

The current reader is more permissive than the writer in several respects:

| Boundary | Current behavior or gap |
| --- | --- |
| Declared size/count | Zero bypasses the corresponding equality check. |
| Header active ID | Bounded against the whole stream, not the 24-byte header remainder; reader does not enforce the writer's 20-byte limit. |
| Flags and padding | Flags are ignored; zero padding is not validated. |
| Directory structure | No explicit lower-bound check against header/directory end, duplicate-tag rejection or unique-offset check. First matching SESS is selected. |
| Unknown sections | Not parsed and their individual CRCs are not checked; whole-file CRC still covers their bytes. |
| Payload completion | No exact-consumption check after the declared entries. |
| UTF-8 | Manifest primitives use replacement decoding, not strict malformed-UTF-8 rejection. |
| Domain identity | No duplicate-session-ID or active-ID membership check in the codec. |
| Allocation budget | File loading reads the entire file before parsing; there is no configured maximum file size. |

These are limits to state honestly, not guarantees to infer from successful CRC
validation. Tightening the owner's parser requires discriminating controls and a
compatibility decision, not a second parser in an adapter. A conforming writer
continues to emit the exact v1 layout above.

`ManifestMapping.toManifestState` removes entries stopped more than seven days
ago and builds a map by session ID. That transformation is separate from byte
validation; it can collapse duplicate IDs and leaves the active ID unchanged.
A successfully decoded file therefore does not by itself establish a coherent,
current registry or a resumable project.

## Persistence ownership and recovery

[`ManifestOwner.fs`](../Bozzetto.Core/Features/ManifestOwner.fs) holds one in-memory
registry and serializes mutations. Its write path reports `WrittenTo` after a
successful save, or `Unchanged` when the state is already on disk. A failed write
is reported as `WriteFailed` and retained as pending state for a later commit.
An unreadable base is refused rather than replaced by partial daemon knowledge.

`ManifestFile.save` implements this replacement sequence:

1. Create the destination directory and encode the manifest.
2. Write to a unique `daemon.bozzettofm.<guid>.tmp` file.
3. Best-effort copy the existing primary to `daemon.bozzettofm.bak`.
4. Move the temporary file over the primary with overwrite enabled.
5. Delete a remaining temporary file in `finally`, when possible.

This is a staged file replacement, not evidence of power-loss durability or a
cross-process locking protocol. The code does not issue an explicit disk flush,
and backup-copy failure does not fail the save.

`ManifestFile.load` tries the primary, then the backup after primary absence or
failure. It returns `Primary` or `Backup` with successful data; backup recovery is
logged. `DaemonPersistence.loadManifest` discards that source discriminator.
Failures distinguish `NotFound`, `IoError` and `CorruptData`. The quarantine helper
renames a corrupt primary to `daemon.bozzettofm.corrupt.<milliseconds>`; it is not
intended for transient I/O failures. See [operations](operations.md) for recovery
policy rather than treating a backup as new execution authority.

## Typed direct interchange

### Implemented daemon-worker profile

The shared closed contract lives in
[`Protocol.fs`](../Bozzetto.Composer.Protocol/Protocol.fs),
[`ProviderData.fs`](../Bozzetto.Composer.Protocol/ProviderData.fs),
[`BAREWireCodec.fs`](../Bozzetto.Composer.Protocol/BAREWireCodec.fs) and
[`WireSchema.txt`](../Bozzetto.Composer.Protocol/WireSchema.txt).
Consult those owner-maintained files for field order and union tags; this page
does not reproduce their schema tables.

The codec implements protocol version **2**, encoding `BAREWire1`, and an explicit
contract digest. It uses strict UTF-8, bounded BARE arrays/options/unions and
fixed-width declared integers. Request IDs retain their complete string values.
Unsupported versions are checked before decoding the request/reply body; unknown
closed tags and unconsumed trailing bytes are invalid payloads.

[`StreamFrames.fs`](../Bozzetto.Composer.Protocol/StreamFrames.fs) uses the BAREWire
stream envelope: a four-byte little-endian body length, then a kind byte,
four-byte correlation and typed payload. This profile requires correlation zero;
request correlation belongs to the full request ID in the payload.

| Bound or distinction | Implemented behavior |
| --- | --- |
| Frame body | 5 through 1,048,576 bytes; checked before body allocation. |
| Payload | At most 1,048,571 bytes, excluding the five-byte envelope header. |
| Strings and arrays | Profile caps apply; decoded array count is also bounded by actual remaining bytes before allocation. |
| EOF | Clean EOF is `None`; partial prefix and partial body are distinct failures. |
| Envelope | Wrong kind and nonzero correlation are refused. |
| Payload | Version, malformed content and size failures are typed codec failures. |

These limits belong to the control profile, not to PSG images or manifest files.
Text such as source buffers and diagnostics can be typed string fields inside a
binary message; that is not permission to carry a JSON command envelope.

[`ComposerWorkerClient.fs`](../Bozzetto/ComposerWorkerClient.fs) owns a socket-backed
connection and serializes writes. Framing functions explicitly leave transport
lifetime and admitted I/O joining to their caller. A decoded `WorkerAddress` is
an address, not artifact or run authority. Cancellation requested, cleanup pending
and actual retirement remain distinct states in `Protocol.fs`.

### Browser and direct-client drift

The current browser vocabulary is shared source in
[`bozzetto-web/src/Shared/Protocol.fs`](../bozzetto-web/src/Shared/Protocol.fs), but
[`UiBridgeCodec.fs`](../Bozzetto/UiBridgeCodec.fs) still implements JSON commands
and events on WebSocket text frames at `/ui/bridge`. This is **drift to remove**,
not delivered binary transport. Its use of Fidelity.Data does not make it compliant.

[`ComposerRoutes.fs`](../Bozzetto/ComposerRoutes.fs) likewise reads and writes JSON
for `/api/composer/*`, using
[`ComposerClientJson.fs`](../Bozzetto/ComposerClientJson.fs), and retains an SSE
change-hint endpoint. Direct agent/client use of those JSON routes is also drift.
SSE is not the forward UI pipeline. Binary browser and direct-client contracts
must preserve identity, refusals, bounds and atomic reconnect reconciliation;
their delivery is not established by the worker codec. See
[reactive push](reactive-push.md), [agents](agents.md) and
[MCP tools](mcp-tools.md) for their separate boundaries.

JSON export must not become a request channel, compiler cache or fallback decoder.
The independent sink has no authority to admit compilation or execution; see
[diagnostic sink](Bozzetto_Diagnostic_Sink.md).

## PSG and BAREWire schema ownership

Use the selected dependency closure's contracts, not copied Bozzetto schemas.
Resolve each reference in its named owner repository at the revision selected
for that closure, not through a sibling-checkout filesystem layout:

- The Fidelity.PSG repository, `docs/Binary_Images.md`, owns indexed image
  layout, fingerprinting, explicit byte/collection/depth/value limits and
  partial-read versus complete-integrity boundaries.
- The Fidelity.PSG repository, `docs/Scoped_Publication.md`, owns checked
  revisions, delivery cursors, scope authorization, exact-base transactions
  and occurrence delivery. It explicitly distinguishes these from proof
  truth, complete executable facts and launch permission.
- The BAREWire repository, `README.md`, `docs/02 Encoding and Decoding Engine.md`
  and `docs/05 Network Protocol.md`, owns the shared representation and
  framing primitives.

A complete-Revision image codec does not authorize full retained-graph service
transfer or a full-graph recovery fallback. Source-authorized demanded scopes,
exact-base affected deltas and explicit reauthorization remain the service
requirement. Successful byte transfer, catalog installation, structural checks,
proof discharge and execution admission are separate facts. Owner documentation
describes remaining completeness and integration limits; do not promote it into
a Bozzetto delivery claim.

## Compatibility and acceptance

Manifest v1 has both format and minimum-reader versions, but its current reader
also rejects any format version above 1. Unknown directory tags do not establish
unrestricted forward compatibility. A changed field layout needs an explicit
owner-approved format evolution and reader behavior, not silent reinterpretation.

Worker wire compatibility is separate from loaded compiler/PSG identity.
`Agreement` carries protocol version, encoding and contract digest; `HelloResult`
separately carries compiler assembly identity and PSG schema, assembly hash,
format version and fingerprint. A compatible worker envelope does not prove that
its loaded semantic contract is the intended one.

[`build/ComposerContract.props`](../build/ComposerContract.props) selects shared
PSG/BAREWire project references or assemblies from an absolute
`ComposerDistribution` containing both DLLs. Matching daemon/worker deployment
and actual loaded identity must be verified separately, as described in
[operations](operations.md). Native hosting and distributed authenticated,
fenced interchange remain requirements until their own receiving and lifecycle
acceptance is demonstrated.

For parser or contract changes, test the changed owner and its consumers with
positive controls, specific negative refusals and a caught mutation. Include
malformed inputs with recomputed CRCs so structural checks are exercised rather
than hidden by checksum failure. Wire checks must separately cover bounds,
version/contract mismatch, malformed tags, truncated frames and stale authority.
One unfiltered TRUST acceptance run closes the increment; code reads and filtered
codec tests do not. See [development](development.md).
