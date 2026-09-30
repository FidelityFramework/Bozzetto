# Bozzetto provider and identity checkpoint — 2026-09-30

This is the first implementation checkpoint for the external auditor. It is not an audit approval or a claim that Clef is already available through the dashboard/MCP. Please write the assessment as `docs/Bozzetto_Provider_Assessment_2026-09-30.md`, referencing concrete files, observations and required corrections. The user will coordinate that review.

The [provider handoff](Clef_Composer_Provider_Handoff.md) is the acceptance specification; the [larger plan](Clef_Composer_Development_Plan.md) includes daemon integration and a standalone Composer MCP host using the same session authority.

## Delivered changes

- **Generated tier storage:** the former sibling `~/repos/Bozzetto.tiers` was 4.6 GB of CI scratch clones, caches and logs, including failed-tier evidence. It is now `~/.cache/bozzetto/tiers/Bozzetto-9c6966335ce4754f`. The original sibling is gone. The move retained all entries and verified 19 top-level file hashes. `ci-pipeline.fsx` chooses an absolute XDG cache (or `~/.cache`) plus checkout identity, rejects scratch inside the checkout, and probes reflinks between the checkout and cache. Agent instructions describe the same placement so the old path is not recreated.
- **MCP remains integral:** the nested SDK clone built an unused 0.9 preview, while the application resolves official 1.0.0-rc.1 packages. Removed redundant clone/pack stages and local feed. Preserved the clean clone and packages under `~/.cache/bozzetto/dependencies/`. This removes accidental source-tree segmentation; it does not remove MCP from the daemon or introduce a second application state model.
- **Hard-fork identity:** source namespaces, project paths, state files, editor commands, package/tool names and agent instructions now use Bozzetto and CLI `boz`. Defaults are **47749 MCP / 47750 dashboard**, separate from upstream 37749/37750. Historical attribution remains truthful. The live `SageFs.Harmony` dependency is a removal/isolation target on the self-hosting path, not a permanent architectural exception; see the [identity inventory](Bozzetto_Identity_Migration_Inventory.md). No compatibility with upstream state is promised and no product version was bumped.
- **Small Composer worker:** [contracts](../Bozzetto.Composer/ProviderContracts.fs), [authority/lifetime adapter](../Bozzetto.Composer/ProviderSession.fs), [Composer API adapter](../Bozzetto.Composer/ComposerAdapter.fs), and [process host](../Bozzetto.Composer/Program.fs). The compiler is isolated from FSI and only called through `CompilationOrchestrator.ProjectSession`.
- **Tests:** eleven deterministic session tests and three process/native tests in [Bozzetto.Composer.Tests](../Bozzetto.Composer.Tests). Pure tests join the default suite. The native suite is registered structurally under `--integration-composer`; that dedicated gate includes all fourteen tests. `ci-pipeline.fsx -- composer` opts into the compiler prerequisite/build/gate and records its trust row. Missing compiler or fixture configuration fails explicitly.

## Contract and deployment decisions

The worker targets .NET 10. Build it with an absolute `ComposerDistribution` directory containing the already-built Composer DLL closure; it copies those DLLs without rebuilding or guessing a sibling compiler checkout. It has no new NuGet dependency. It is intentionally an optional project outside the ordinary F# solution build, because that build must not require a Composer development checkout.

The private protocol is version 1 JSON lines, request-correlated, over inherited anonymous pipes or explicit `--stdio`. It is **not MCP**. Compiler console output goes to stderr; the client must drain it. Request frames are bounded to 1 MiB. Long operations run off the reader loop. The handshake reports compiler path/version/hash, operations and `inMemoryPatchAllowed=false`. A packaged distribution descriptor and closure validation remain follow-up work.

Established-session responses carry provider, host, session, epoch and generation. Reservation GUIDs are local handles for opaque Composer tickets; foreign, consumed and superseded handles fail. Reserve before editing files. Status returns metadata with `executionRequiresRevalidation=true`; execution exclusively calls Composer's `RunCurrentAsync`.

Cancellation withdraws acceptance and rejects late completions even if the backend ignores cancellation. It does not promise to stop every native subprocess immediately. `prepare_compiler_change` closes all sessions and forbids further opens in the old worker; a fresh worker gets a fresh host/epoch. Active in-memory compiler patching is unsupported. A patching path must fence and retire before mutation, not inspect disk DLL hashes afterward.

Build awaits occur outside the authority lock. Run validation and launch execute synchronously through the Composer call's first await under the lock to order selection with reservations; the process lifetime is awaited outside it. Close invokes Composer disposal under the lock. Please inspect those short synchronous API assumptions against the compiler implementation and potential blocking I/O.

Outputs use `~/.cache/bozzetto/provider-sessions/<host>/<session>/<epoch>` (or absolute XDG cache). They are never generated in fixture/source projects. The current host retires on transport closure and bounds shutdown, killing only its owned process tree if outstanding operations do not settle.

## Evidence and validation

The preliminary unfiltered harness runs passed **11/11 authority tests** and **3/3 process/native tests**, both with `Trusted` rows. The native run took 131 seconds and used the real scalar fixture in an external copy. The compiled, integrated acceptance results are recorded below when final validation completes.

Native evidence includes cold/unchanged/edited object manifests, actual `.o` paths and SHA-256 verification, request/response logs, compiler stderr and native stdout. Cold/unchanged/edit compiled **3/1/2** objects and reused **0/2/1**. The unchanged scalar objects retain their actual paths and hashes. Editing only `changeable` retains `stable` with object SHA-256 `0C1B496BE0C3C51CAFE686F62CFCB89B0B306A40589209C1CBE673C0A01075FD`. Native stdout changes from `stable\nbefore\n` to `stable\nafter\n`, exit 0.

The process tests also cover wrong provider/host/epoch, cross-session tickets, simultaneous status calls, close/replacement, invalid and unannounced source changes, root/dependency manifest changes, deleted/corrupted executable bytes, and rebuilding in a fresh worker. Deterministic backend barriers exercise cancellation, supersession, retirement while held, and a generation change during execution; these are adapter concurrency tests, not a claim of a barrier inserted into the real compiler.

Evidence working directory: `~/.cache/bozzetto/validation/provider-checkpoint/`. Durable checkpoint directory: `~/.local/state/bozzetto/checkpoints/2026-09-30-provider/`. Source heads/dirty diffs, SDK details and compiler closure hashes are captured externally. The initial compiler DLL SHA-256 is `04F1CDF91336FD699AD0C1A53F333940B22DF5B0FE9EF1CDFDC4AC2DD853169D` (Composer assembly 0.0.2.0). Local compiler repositories contain the other agent's work and were neither reset nor rebuilt here.

The required REPL skill was followed by checking tool availability and a bounded daemon probe. No REPL MCP tools were connected and the probe was refused; the .NET fallback was explicitly reported. Do not treat the bootstrap harness's older test-helper DLL as acceptance of the renamed application: the compiled gate below provides that distinction.

## Reproduce

Use the SDK pinned by `global.json` (locally installed at `~/.cache/bozzetto-validation/dotnet/dotnet`). Supply an existing compiler distribution and the `04d_IncrementalScalarRegions/IncrementalScalarRegions.fidproj` fixture explicitly:

```sh
export BOZZETTO_COMPOSER_DISTRIBUTION=/absolute/compiler/distribution
export BOZZETTO_COMPOSER_FIXTURE=/absolute/04d_IncrementalScalarRegions/IncrementalScalarRegions.fidproj
export DOTNET_HOST_PATH=/absolute/dotnet
export PATH="$(dirname "$DOTNET_HOST_PATH"):$PATH"
"$DOTNET_HOST_PATH" build Bozzetto.slnx -c Release
"$DOTNET_HOST_PATH" build Bozzetto.Composer/Bozzetto.Composer.fsproj -c Release "-p:ComposerDistribution=$BOZZETTO_COMPOSER_DISTRIBUTION"
export BOZZETTO_COMPOSER_WORKER="$PWD/Bozzetto.Composer/bin/Release/net10.0/Bozzetto.Composer.dll"
"$DOTNET_HOST_PATH" Bozzetto.Tests/bin/Release/net11.0/Bozzetto.Tests.dll --summary
"$DOTNET_HOST_PATH" Bozzetto.Tests/bin/Release/net11.0/Bozzetto.Tests.dll --integration-composer --summary
```

The full pipeline option is `dotnet fsi ci-pipeline.fsx -- composer` with the same environment. It also runs the existing host/editor stages and therefore needs their prerequisites. No filter is an acceptance gate.

## .NET dependency reduction

The user explicitly prioritizes self-hosting over the next few months. The [plan](Clef_Composer_Development_Plan.md#reduce-net-coupling-on-the-path-to-self-hosting) now inventories current managed dependencies and their replacement boundaries. The worker does not reference Bozzetto.Core, FSI or Harmony directly. The current daemon/Core still do; provider isolation is not yet complete. Preserve language-neutral schemas and native process acceptance so a native compiler worker and eventually a Clef-only host can run without dotnet. Accurate attribution is separate from runtime dependency retention.

## Audit scope and next work

Review the adapter's routing, opaque ticket ownership, locking, cancellation/epoch fences, refusal metadata, compiler distribution, private transport and native receipts. Review the broad identity diff separately using the rename inventory and preserved original snapshot.

Still outstanding: daemon/provider supervision and selection; real F# and Clef sessions together through common human/MCP handlers; standalone Composer MCP packaging; UI/editor actions and reservation-before-save; integrated reconnect/notification/cleanup evidence; provider cache retention policy and durable distribution identity. Native proof here does not claim completion of those milestones or the entire handoff acceptance matrix.

Fable.SageFs supplied useful assembly isolation and baseline/edit/revert testing lessons, plus limitations around external-source watching. It is not evidence that active Composer compiler hot-patching is supported. LAN retrieval helped confirm the CCS → Fidelity.PSG → Alex → Composer relationship; no shared inference worker was needed.

## Final compiled validation

- Release solution build: **passed**, zero warnings/errors; both net10/net11 shipped tool targets compiled.
- Release Composer worker build: **passed**, zero warnings/errors, explicit compiler distribution.
- Default suite: **9,775 registered / 9,775 accounted for; 9,771 passed, zero failures/errors, four existing ignores; Trusted**. The ignores are three pending performance budgets and one existing Harmony patch case. An initial run had one child-process SDK-discovery error; setting PATH to the pinned SDK resolved it, and the entire suite was rerun.
- Dedicated Composer gate: **14 registered / 14 passed / zero ignored, failures or errors; Trusted**, 124 seconds. This is the final compiled runner and final Release worker, not the bootstrap harness.
- Package: `Bozzetto.0.6.834.nupkg` produced; isolated offline tool installation succeeded and the installed `boz --help` reports the new identity and default MCP port. No global installed tool was changed.
- Protocol smoke: malformed JSON, a non-object frame and hello received three authority-bearing replies; transport closure exited within the hard timeout. Retirement is included in the worker's five-second shutdown deadline.
- Full copied CI script compiled and listed the optional `composer` stage; original trust ledger was preserved. XML/reference/version/binary checks and `git diff --check` passed.
- **Not run:** full host/browser/mutation tiers, editor compilation/runtime journeys, simultaneous live SageFs/Bozzetto daemons, or complete CI. Free/default-port and package checks do not prove all coexistence behavior. The new private worker is not yet a public MCP host.

The reviewable final source snapshot and SHA-256 manifest are stored as `source-final.tar.gz` and `source-final-manifest.json` in the durable checkpoint directory. The workspace remains uncommitted, preserving the user's and compiler agent's existing edits. The source snapshot complements the rename map because ordinary unstaged `git diff` does not include new/renamed untracked files.
