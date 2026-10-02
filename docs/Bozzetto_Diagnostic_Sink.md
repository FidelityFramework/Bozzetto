# Diagnostic PSG capture

`Bozzetto.Diagnostics` is an independent .NET process that receives source-owned
PSG occurrence captures and keeps them after compiler-worker or Bozzetto exit.
It references Fidelity.PSG, BAREWire and Fidelity.Data; it loads no compiler.

The first capture stage is `occurrence-structure-v1`. CCS supplies its already
selected live occurrences and sparse declaration context through its publication
boundary. The capture includes the actual source demand, checked workspace
revision, source-input identity and expected scope stamps. It contains no retained
compiler graph, inactive node bodies or complete semantic/proof tables.

Every referenced identity must have a captured body, a declared context header,
or the source's explicit omitted-child account. Unknown references and imported
boundaries whose facts are unavailable are refused. A deliberately narrow,
complete occurrence capture is distinct from an incomplete transport: truncated
transfers never produce a committed snapshot. This stage does not establish
complete semantic PSG integrity or confer proof, artifact or execution authority.

## Run the sink

Publish `Bozzetto.Diagnostics/Bozzetto.Diagnostics.fsproj` with the same selected
PSG/BAREWire closure as the compiler worker. Start it independently of Bozzetto
and the worker's owned process tree:

```sh
dotnet /absolute/sink/Bozzetto.Diagnostics.dll \
  --pipe fidelity-diagnostics \
  --store /absolute/external-state/bozzetto/diagnostics \
  --idle-seconds 3600
```

The pipe uses the managed local named-pipe API with access restricted to the
current user. The sink prints `DIAGNOSTIC_SINK_READY pipe=...` after creating its
listener. An exclusive store lock prevents two sinks from writing the same store.
Use the host's normal service manager when a longer service lifetime is wanted.

Set `BOZZETTO_DIAGNOSTIC_PIPE=fidelity-diagnostics` in the compiler worker's
environment. Bozzetto's worker inherits its environment. This does not start or
stop the sink. Without that setting, Composer creates no diagnostic occurrence
reading and makes no sink connection.

The compiler hands over the immutable capture after binding the checked source
identity and before proof dispatch or native tools. The host uses a cold `Async`
operation with a ten-second observation deadline. Diagnostic refusal is reported
to worker stderr and does not change compiler authority. Project close still
seals and cancels native tools immediately; any admitted diagnostic handoff joins
as owned work. A lost acknowledgment leaves persistence unconfirmed, even if the
sink subsequently completes it.

## Persistence and limits

The binary envelope includes scope metadata and the existing occurrence codec,
with a checksum over the complete capture. The receiver checks length, checksum,
schema, occurrence structure and reference accounts before persistence. Frames
are bounded to 32 MiB before body allocation. JSON never carries the PSG between
processes.

The sink writes `<SHA256>.bare` and derives `<SHA256>.json` using Fidelity.Data.
The small JSON descriptor preserves exact integer identities and names the
capture stage. The binary file retains the complete received capture. Temporary
files are flushed and atomically renamed; the descriptor is published last.
A persistence acknowledgment is sent only after both files have been checked.
This is not a guarantee against power loss during directory metadata updates.

Replaying the same capture rechecks the stored bytes and descriptor. It neither
duplicates evidence nor treats a filename as proof of integrity. The finite
replay command is:

```sh
dotnet /absolute/sink/Bozzetto.Diagnostics.dll submit \
  --pipe fidelity-diagnostics --capture /absolute/capture.bare --timeout-ms 10000
```

Defaults are 128 captures, 256 MiB of store files, and a ten-second connection
I/O deadline. Set `--max-captures`, `--max-store-bytes` and `--io-timeout-ms` to
choose another policy. A full store refuses new captures and preserves existing
evidence. Nothing is silently evicted. Payloads left without a descriptor after
an interrupted write count against the quota and can be verified by replay.

`--idle-seconds` is required. Idle shutdown retains all stored evidence; malformed
or idle clients do not renew the idle period. An accepted filesystem write is
joined physically and can outlast that deadline if the filesystem stalls.

Complete semantic scopes, incremental diagnostic deltas, proof transcripts and
earlier failed-check captures remain subsequent integration work. A worker killed
before handing over a valid capture cannot supply a final dump after its death.
