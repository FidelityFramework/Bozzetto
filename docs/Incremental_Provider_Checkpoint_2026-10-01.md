# Incremental provider checkpoint — 2026-10-01

This checkout implements a first provider integration with
`Fidelity.FSharp.Incremental.Hosting` **0.1.0-preview.5**. The provider owns one
functional `AsyncMailbox` per explicitly opened Composer session. This is a
source implementation checkpoint; full consumer build, default TrustSignal and
native provider acceptance remain pending below. It does not describe an
updated installed daemon.

## Dependency identity

The library implementation commit is
`87c77d91ab24c8e4d065e4e726f8c06abc3180c4`. The central package pin, explicit
Composer distribution checks, and retained packages under
[`vendor/Fidelity.FSharp.Incremental`](../vendor/Fidelity.FSharp.Incremental/README.md)
identify the dependency. Exact archives are listed in
[`SHA256SUMS`](../vendor/Fidelity.FSharp.Incremental/SHA256SUMS):

- Core: `7f27688e98c2cee965b6ebf5f20073fb9d17b3f9d89e12511daf5e5a4eca3a88`.
- Hosting: `fb63ca9b638dde967958c032cce13e685dd78baf6b1e67766a764b00afa8e5e3`.

Preview.5 adds `AsyncMailbox.watch`: a published snapshot and its next change
notification are captured together. Observers cannot lose a change between
reading state and subscribing. A notification itself grants no result or effect
authority. The provider checks the matching result token and `isEligible` before
publishing successful producer metadata.

## Implemented provider contract

- `IProjectBackend<'Ticket>`, `ProviderSession<'Ticket>` and `Worker<'Ticket>` keep
  compiler tickets typed and process-local. JSON reservation strings identify
  provider-owned tickets; clients cannot reconstruct compiler authority.
- Concurrent builds for one session and reservation share one compiler producer.
  Each pending caller owns a separate library demand. A completed current ticket
  can be observed again without another compiler invocation. Separate `open`
  requests still create independent sessions, even for the same project path.
- Caller cancellation, including private `cancel_request`, detaches that caller's
  observation and releases its demand. Another demand keeps the producer alive.
  A pre-canceled request never enters the compiler or revokes the generation.
  Releasing the last pending demand abandons that ticket; retry requires a new
  reservation. Explicit `cancel` continues to revoke the generation.
- Reservation first withdraws provider authority and admits library scope
  invalidation. Its receipt waits for committed invalidation and the synchronous
  Composer `Reserve` call. Compiler reservation is independent of evaluator slot
  availability. The shared invocation fence serializes reservation with native
  artifact selection and launch; Composer remains the final artifact authority.
- Each producer has a scope identifying its physical drain. The provider awaits
  evaluator completion and cancellation callbacks before settling its outcome.
  `BeginClose` immediately retires authority. `CloseAsync` joins the mailbox,
  outstanding control operations and outcome projections before backend disposal.
  Provider `IDisposable.Dispose` synchronously joins this close. Worker shutdown
  uses its separately awaited `RetireAsync` and existing process deadline.
- State is bounded per session: at most 1,024 producer scopes, 8,192 demand IDs,
  8,192 control admissions and 128 simultaneous control operations. Epoch limits
  return `session_capacity` and require a fresh session; control saturation returns
  `busy` without granting another reservation. Completed evaluator closures are
  removed; the core's protocol tombstones remain bounded by these limits.

`cancel` acknowledges logical withdrawal. `RevocationPending`, `CleanupPending`
and their error fields continue to distinguish pending physical work and failure
from an acknowledged request.

## Evidence and remaining gates

Separate SageFS session `6fd7fa1e` on port 37749 supplied the F# inner loop. A RED
probe showed the old implementation refusing a second same-ticket request with
`invalid_reservation`. The updated provider and worker suites ran **27 tests:
27 passed, 0 ignored, 0 failed, 0 errored**. They cover shared demand and detach,
pre-canceled requests, held evaluator and cancellation callback cleanup, deferred
native launch invalidation, control saturation, compiler-prefix fences, disposal
failure and overtaken session construction. This is focused REPL evidence, not
whole-consumer acceptance.

External transcripts are under
`/home/hhh/.codex/work/incremental-adoption-2026-10-01/sage/`:
`boz-red.messages.json`, `boz-tests-load.messages.json`, and
`boz-run.messages.json`. `NativeProviderTests.fs`, including the new real wire
sharing case, type-checked in SageFS using the actual Integration module extracted
from the test harness. Native test bodies were not executed by that syntax check.

Pending acceptance: rebuild the reviewed Composer closure; build the provider
and Bozzetto tests; run the unfiltered default TrustSignal tier; run the dedicated
native provider tier against that rebuilt worker. Installed daemon/MCP/browser
acceptance must identify the deployed closure separately.

Exact reconciliation after a lost wire reply remains a follow-up. The mailbox
retains exact admitted-operation receipts internally, and current build tickets
can re-observe their producer. Protocol v1 still lacks a retained request receipt
and explicit acknowledgement/forget protocol for reconnecting clients. A status
refresh does not prove which lost request committed. No durable actor inbox,
cross-session producer sharing, persistent actor lifecycle or Fable runtime is
implemented by this provider change.

## Validation and promotion recipe

Use the existing SageFS build/test leases. Set these inputs to the reviewed
closure and an external evidence directory; the fixture must be the real
`IncrementalScalarRegions.fidproj` with its explicit absolute platform dependency.
The commands below are a recipe, not additional recorded results.

```bash
export DOTNET_HOST_PATH=/absolute/path/to/reviewed/dotnet
export COMPOSER_DISTRIBUTION=/absolute/path/to/rebuilt/Composer/distribution
export BOZZETTO_COMPOSER_FIXTURE=/absolute/path/to/04d_IncrementalScalarRegions/IncrementalScalarRegions.fidproj
export BOZZETTO_COMPOSER_EVIDENCE=/absolute/external/evidence/native-provider
export BOZZETTO_TRUST_LEDGER=/absolute/external/evidence/trust.jsonl

"$DOTNET_HOST_PATH" build Bozzetto.Composer/Bozzetto.Composer.fsproj -c Release \
  -p:ComposerDistribution="$COMPOSER_DISTRIBUTION"
"$DOTNET_HOST_PATH" build Bozzetto.Tests/Bozzetto.Tests.fsproj -c Release

export BOZZETTO_COMPOSER_WORKER="$PWD/Bozzetto.Composer/bin/Release/net10.0/Bozzetto.Composer.dll"
export BOZZETTO_DAEMON_DLL="$PWD/Bozzetto/bin/Release/net11.0/Bozzetto.dll"

"$DOTNET_HOST_PATH" Bozzetto.Tests/bin/Release/net11.0/Bozzetto.Tests.dll --summary
"$DOTNET_HOST_PATH" Bozzetto.Tests/bin/Release/net11.0/Bozzetto.Tests.dll \
  --integration-composer --filter-test-list 'Composer native provider process' --summary
```

The first test command is the unfiltered default gate and must report `Trusted`.
The second selects the complete native process test list, launches only its owned
workers, and reports `NarrowedRun`. It is useful focused evidence for the rebuilt
worker, including real artifact receipts, shared-ticket replay and native output;
it does not complete the dedicated Composer tier.

Once the rebuilt daemon closure is available, run the entire dedicated tier:

```bash
"$DOTNET_HOST_PATH" Bozzetto.Tests/bin/Release/net11.0/Bozzetto.Tests.dll \
  --integration-composer --summary
```

That tier includes provider units, worker protocol tests, native process tests
and `Live Composer shared interfaces`. The live test starts and cleans up its own
daemon on reserved ports, with an external working/data directory and ten-minute
TTL; it requires the complete daemon closure and MCP dependencies. Accept the tier
only with an unfiltered `Trusted` row and retained evidence. Neither command
updates the existing shared daemon on 47749/47750. Promotion of that service must
record the reviewed deployed closure and repeat its MCP/browser checkpoint
separately; a passing owned test daemon does not identify the installed service.
