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

After Composer close joins CCS, the adapter drains retained compiler diagnostics
to worker stderr with attempt, code and message, including failures from withdrawn
checks. Reporting cannot replace an original close exception. These checker
diagnostics are currently stderr-only; the provider status schema does not expose
them, and they are not reclassified as physical cleanup failures. Keep the worker
stderr evidence with the validation or deployed worker logs.

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

## Bounded audit map

Start with the sibling [CCS workspace contract](../../clef/docs/Incremental_Project_Workspace_2026-10-01.md)
and [Composer adoption contract](../../Composer/docs/incremental-compilation/2026-10-01-workspace-adoption.md).
The independent [preview.5 observation assessment at deabc97](https://forge.spkez.dev/FidelityFramework/Fidelity.FSharp.Incremental/src/commit/deabc979ccc21c975b47e2e16d8722085b71d82e/docs/Consumer_Observation_Auditor_Assessment_2026-10-01.md)
accepts library implementation `87c77d9`; its 102 repository tests and three
additional observation controls do not substitute for consumer acceptance.

These are source locations and discriminating assertions, not new passing-run
claims. CCS tests use actual checking with controlled barriers; provider unit
tests inject a backend. Rows marked native execute real processes or artifacts.

| Boundary | Test and location | Evidence to inspect |
| --- | --- | --- |
| Shared demand | CCS `Same generation shares a complete check and one withdrawn consumer preserves another` ([ProjectWorkspaceTests.fs:201](../../clef/tests/Clef.Compiler.Service.Tests/ProjectWorkspaceTests.fs)); provider `canceling one client detaches its demand while another keeps the shared producer` ([ProviderSessionTests.fs:273](../Bozzetto.Composer.Tests/ProviderSessionTests.fs)). | One check/build; withdrawing one demand preserves the other. Provider coverage is an injected-backend unit test. |
| Process-global state serialization | CCS `Two real workspaces share process ownership through complete projection` and `Close joins noninterruptible checking and restores process configuration` ([ProjectWorkspaceTests.fs:63,320](../../clef/tests/Clef.Compiler.Service.Tests/ProjectWorkspaceTests.fs)). | A second evaluator demonstrably waits for the shared CCS gate; projection remains inside ownership; configuration and withdrawn failure diagnostics survive projector failure. This is within one process. |
| Reservation before deferred launch | Provider `reservation bypasses occupied evaluator slots and prevents a deferred native launch` ([ProviderSessionTests.fs:433](../Bozzetto.Composer.Tests/ProviderSessionTests.fs)); Composer `reservation before deferred run invocation refuses the old artifact before launch validation` ([IncrementalBuildTests.fs:294](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Unit barriers prove edit permission bypasses dispatch saturation. The Composer case first builds a real artifact, then proves the obsolete deferred call never reaches launch validation. |
| Actual launch before reservation | Composer `a real launch that wins reservation remains owned and its old generation is withdrawn` ([IncrementalBuildTests.fs:355](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Native `Process.Start` occurs once before the barrier. Later reservation withdraws current authority without pretending the already launched process never ran. |
| Physical capture and callback joins | CCS `Owned capture shares demand permits reservation and remains joined by close` ([ProjectWorkspaceTests.fs:99](../../clef/tests/Clef.Compiler.Service.Tests/ProjectWorkspaceTests.fs)); provider `close retains ownership after evaluator return until cancellation callback exits` ([ProviderSessionTests.fs:456](../Bozzetto.Composer.Tests/ProviderSessionTests.fs)); Composer `close joins a caller cancellation callback after the operation body has completed` ([IncrementalBuildTests.fs:247](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Held capture and callbacks keep close incomplete. Composer also retains the directory lease; provider prevents backend disposal. Callback barriers alone are not native execution evidence. |
| Actual process and output joins | Composer `canceling a process joins its actual exit and both output streams` and `canceled process collection retains each independently held output cleanup` ([IncrementalBuildTests.fs:185,209](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Real child processes exit, stdout and stderr settle independently, and cancellation cannot detach either cleanup. |
| Stale input/proof refusal | `project receipts reject changed manifests and checker text even after disk restoration` ([ProjectSessionTests.fs:48](../../Composer/tests/Alex.Tests/ProjectSessionTests.fs)); `Result branch authority rebuilds its whole scope and rejects another revisions proof receipt` ([IncrementalBuildTests.fs:518](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Actual consumed-text receipts are checked; the native branch case produces fresh proofs/artifacts and explicitly refuses a prior revision's proof. |
| Same native receipt and retained object | `two wire clients of one reservation share the same native build receipt` and `native builds retain real objects and execution gates reject changed inputs and artifacts` ([NativeProviderTests.fs:379,405](../Bozzetto.Composer.Tests/NativeProviderTests.fs)). | Real worker replies share artifact path/hash and object manifest; replay does not rebuild. Source edits preserve the unaffected object's actual path/hash and change native output. Deterministic overlap is established separately by provider unit barriers. |

Final results for the rebuilt consumer closure remain pending the coordinating
validation run; attach exact logs/TRX or TrustSignal rows rather than inferring
success from this map.

| Final gate | Current-closure receipt |
| --- | --- |
| Clef unfiltered compiler-service suite and baseline comparison | Pending |
| Composer native, project/editor and RPC checks | Pending |
| Bozzetto unfiltered default tier | Pending |
| Entire `--integration-composer` tier, including owned live daemon | Pending |

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
