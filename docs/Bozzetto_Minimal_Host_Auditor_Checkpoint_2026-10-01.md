# Minimal host purge: auditor checkpoint — 2026-10-01

This checkpoint explains the removal of inherited runtime patching and records
the compiled evidence separately from deployment. **The purge is deployed on
stable .NET 10, and the installed native workflow passed.** Compiler-wide failures
remain: Clef's comparison retains 99, Composer retains one callable-Result
transport failure, and the editor suite reaches an unresolved source-only
encoding check. These are recorded below rather than counted as acceptance.

Start with the [minimal hosting architecture](Minimal_Hosting_Architecture.md)
and [cross-project integration checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md).
The [consumer assessment](Incremental_Consumer_Integration_Auditor_Assessment_2026-10-01.md),
[fallback assessment](Incremental_Cross_Repo_Fallback_Auditor_Assessment_2026-10-01.md)
and [compiler backlog guidance](Incremental_Compiler_Backlog_Auditor_Guidance_2026-10-01.md)
remain unchanged. Their findings and revision scope are evidence, not a permanent
freeze on the shared library or a blanket acceptance of its consumers.

For the subsequent transport audit, use the [binary PSG integration handback](PSG_Transport_Integration_Auditor_Checkpoint_2026-10-01.md).
It verifies the remaining JSON worker boundary and separates the committed drafts
from this checkpoint's deployed, tested closure.

## Why the scope changed

The earlier implementation work preserved the inherited Harmony integration and
even ported its owned package to .NET 10 to keep its patch/unpatch gate running.
That was the wrong architectural choice. Passing that gate demonstrated that the
inherited mechanism still worked; it did not establish a role for runtime method
replacement in Bozzetto's compiler-owned execution model. The earlier receipt is
retained as history, not as a delivery requirement or a fallback recommendation.

The correction removes the mechanism and its callers. An optional patching mode
would retain a second application-update path, with its own method identity,
injection and state-retention rules outside Composer's source, proof and artifact
authority. It would also retain the dependency and deployment obligations. There
is no disabled compatibility patcher, successful no-op substitute or automatic
return to the old host. Future native replacement and ORC execution must satisfy
the same compiler authority and physical lifetime boundaries.

## Source, package and client boundary

The staged purge measured **280 changed files, 1,114 inserted lines and 34,047
deleted lines** using `git diff --cached --shortstat`, before this document was
added. These are textual diff counts, including tests, documentation and lockfiles;
they are not production-code-only counts. The two deleted binary NuGet packages
have no textual line count. Final commit statistics may also include this report.

The removal covers the retired `Bozzetto.Host` project; Harmony/MonoMod method
detours; reflection/value-read interception; stable-identity and patch-carrying
source injection; kept-state/reload planning; browser `devreload.js` injection;
and the associated host, daemon, dashboard and client entry points. Package pins,
vendored Harmony packages, refresh/packaging scripts, project references and locks
were updated together. Patch-only fixtures, simulations, CI tiers and acceptance
claims were deleted with the feature.

The seven removed MCP tools are `enable_hot_reload`, `disable_hot_reload`,
`reset_hot_reload_state`, `set_reflection_read_mode`, `run_app`, `stop_app` and
`list_runnable_projects`. Managed run/stop UI and patch controls were removed from
the active VS Code source, contributions, tests and daemon routes. Native execution
continues through `composer_run_current`; the generic external-process work lease
does not restore the deleted managed application runner.

The daemon contract is **API 4** in [EndpointContracts.fs](../Bozzetto.Core/EndpointContracts.fs),
and [BozzettoClient.fs](../bozzetto-vscode/src/BozzettoClient.fs) requires API 4.
The endpoint inventory checks now assert 32 declarations, 15 Neovim contract rows
and 13 VS Code rows, retaining missing-endpoint and client/source equality checks.
The external Neovim client has not been executed against API 4. Source inventory
compatibility is not a claim that an older installed client remains compatible.

The host remains an F# implementation on stable SDK **10.0.401**, targeting
`net10.0`; see [global.json](../global.json), [Directory.Build.props](../Directory.Build.props)
and [central packages](../Directory.Packages.props). Retained FCS, project loading,
Fantomas and managed test discovery serve F# implementation tooling. The resolved
FCS/FSharp.Core preview closure supplies .NET Standard assets; the FSharp.Core
package's major version does not select a .NET 11 runtime. Locked package identity
and actual runtime identity must both be checked. Mono.Cecil remains for managed
test coverage in shadow workspaces, never for rewriting Composer artifacts.
Further separation of that tooling closure remains a packaging responsibility.

## Retained authority and lifetime obligations

| Owner | Boundary the purge must preserve |
| --- | --- |
| CCS/Baker and Fidelity.PSG | Baker settles source semantics, proof premises and dependencies; publication copies settled facts into the versioned revision. |
| Alex and Composer | Alex consumes published facts passively; Composer accepts artifacts and checks authority at actual native launch. |
| Fidelity.FSharp.Incremental | Shared dependency/demand bookkeeping, result eligibility and owned work/callback completion; no semantic or artifact certification. |
| Bozzetto provider and supervisor | Reserve before source writes; share one producer within one session/ticket; fence host/epoch/session identities and status observations. |
| Process and editor owners | Capture immutable inputs, serialize shared checker state, retain failures and join process, stream, lease and callback cleanup. |

Ordinary file watching, controlled process lifetimes, real assembly test discovery,
coverage and supported workflow transitions remain. Bozzetto's own F# code is
validated by `dotnet build` and the unfiltered test suite under Bozzetto work
leases; no separate F# REPL service is part of that loop. The earlier
[FSI host transition](Bozzetto_Clefx_Host_Transition_2026-10-01.md) describes that
distinct retirement. Native Clefx/ORC and actual binary PSG revision transport are
not implemented by this purge. The unintegrated codec/control-channel drafts are
outside the tested closure; a handshake alone cannot prove graph transport.

## Executed gates and their limits

Receipts below are under the external evidence root
`~/.codex/work/incremental-audit-repairs-2026-10-01/`, normally in `validation/`.

| Gate | Executed result and receipt |
| --- | --- |
| Release daemon/Core/tests build | Zero warnings and errors; `bozzetto-purge-tests-build-r6.log`. |
| Unfiltered default | **8,957 registered/ran; 8,954 passed, three ignored, zero failed/errored; Trusted**. `bozzetto-purge-default-r2.log` and `bozzetto-purge-trust-r2.jsonl`. |
| Whole aligned Composer provider tier | **40 registered/ran/passed, zero ignored/failed/errored; Trusted**. `purge-aligned-provider.log` and `purge-aligned-provider-trust.jsonl`. |
| VS Code | Fable compilation passed. Initial bundling failed because esbuild was absent; after dependency installation, bundle exit 0 and 17 golden checks passed. `bozzetto-purge-vscode-{build,install,bundle,golden}.log` and bundle `.exit`. |
| Shared-daemon launcher | **13/13 controls passed**; `launcher-tests/RESULT.txt`. Stubbed port/HTTP controls cover identity, existing listeners, workspace placement, failure and deadline; they did not deploy or contact a daemon. |
| Composer full aligned suite | **385/386 passed, one failed, zero skipped**; `aligned-composer-full.log`/`.trx`. Exact comparison: 381→386 cases, zero missing, zero new failures, the same one failure; all five ownership additions passed (`aligned-composer-comparison.json`). |
| Clef full aligned suite | **2,156/2,255 passed, the same 99 failures, zero skipped**; `aligned-clef-full.log`/`.trx`. Exact comparison: 2,254→2,255, zero missing or new failures; the added independent source-error control passed (`aligned-clef-comparison.json`). |
| Compiled editor suite | **30 PASS records, then exit 1** at the source-only encoding check: two CCS8011 range diagnostics for `String.length` and program entry. All six terminal-classification controls, real cvc5 cleanup, selected encoding refusal/repair and program-lifetime checks executed successfully before that stop. `aligned-editor-full.log`; build `aligned-editor-build.log`. |

The Composer failure remains `Result branch authority rebuilds its whole scope
and rejects another revisions proof receipt`: callable values lack separately
published code/environment representation at transport. It is a callable-Result
transport failure, not a generic native proof failure, and is not counted as green.

Clef `ba694e1` admits physical preparation only after byte-storage source
validation succeeds. Rejected encoding keeps its exact source diagnostic and
independent source errors; it supplies no fabricated snapshot or witness, and
publication still refuses it. The strengthened tests check those absences and
refusals. Composer `723e900` records the bounded authority review over implementation
`c1e3ff6`; PSG remains `e2effe3` (schema 12), Alex `9d53b9e`, and Incremental
`d476aea` (preview.6 implementation `3b86e2d`). Bozzetto's source identity is the
commit containing this completed checkpoint; deployed DLL hashes appear below.

The pre-purge default baseline was **9,801 registered/ran: 9,797 passed and four
ignored** (`bozzetto-trust.jsonl`). Its count cannot be compared as unchanged
coverage: tests for removed behavior were deleted. The remaining three ignores
are existing performance-budget placeholders, not newly ignored correctness
failures. The first purge run's 13 failures and two errors remain recorded in
`bozzetto-purge-default.log`; obsolete API/count/UI contracts were corrected while
preserving their meaningful equality, uniqueness and absence assertions.

## Negative controls and auditor reproduction

The old installed worker at Bozzetto `4229c332` gave **38/40 passes, one failure
and one error** in `bozzetto-purge-provider.log`. It consumed a reservation on the
first request, refusing the second client's shared build, and omitted the provider
observation sequence required by the current supervisor. Saved live resource
evidence explicitly reports the missing sequence. The native tests were unchanged
between that run and the aligned 40/40 run. No stale-authority assertion was removed
to accommodate the old binary. This is why source pins alone do not prove delivery.

Audit these concrete controls, separating deterministic unit barriers from real
native/process evidence:

- [ArchitectureTests.fs](../Bozzetto.Tests/ArchitectureTests.fs): `compiled delivery and test closure contains no managed patching dependency` recursively inspects local referenced assemblies from Core, CLI and tests for Harmony/MonoMod names. Also inspect publish contents, deps manifests and package locks; a reference check does not inventory unrelated stale files.
- [ProviderSessionTests.fs](../Bozzetto.Composer.Tests/ProviderSessionTests.fs): reserve bypasses occupied evaluator slots and prevents deferred launch; one canceled client leaves another demand alive; close remains pending until its held cancellation callback exits; compiler withdrawal invalidates current and completed replay while a late refusal cannot revoke a newer reservation.
- [ComposerWorkerClientTests.fs](../Bozzetto.Tests/ComposerWorkerClientTests.fs): build/run cancellation retains its slot at transport capacity; cancellation before writing, while writing and after sending preserves peer progress and drains replies through an actual child transport fixture.
- [ComposerSupervisorTests.fs](../Bozzetto.Tests/ComposerSupervisorTests.fs): reversed capture/delivery order cannot overwrite a newer snapshot; missing observation is refused; active progress may report busy only with `statusFresh=false` and `current=null`; old worker replies cannot lend authority to its replacement.
- [NativeProviderTests.fs](../Bozzetto.Composer.Tests/NativeProviderTests.fs) and [LiveProviderTests.fs](../Bozzetto.Composer.Tests/LiveProviderTests.fs): two wire clients receive the same native receipt/object manifest; changed source, proof/input/artifact and foreign authority are refused; MCP and HTTP share one authority; native execution and retirement join physical cleanup.

Reproduce against a single explicitly recorded compiler/worker/daemon closure.
Set `BOZZETTO_COMPOSER_WORKER` and `BOZZETTO_COMPOSER_FIXTURE` to absolute
validated paths, with `BOZZETTO_COMPOSER_EVIDENCE` naming an external evidence
directory. Preserve the worker handshake, hashes and native output. Run the
test DLL with `--summary`, then the entire `--integration-composer --summary`
tier. Require its `TRUST` row, nonzero executed count and complete registration;
filtered inner-loop passes cannot replace either acceptance gate. The live fixture
owns its test daemon; do not point destructive retirement checks at shared services.
Building the worker requires the explicit MSBuild property
`-p:ComposerDistribution=/absolute/validated/compiler`; there is no corresponding
test environment-variable shortcut. Use the pinned SDK 10 executable for these gates.

## Installed delivery and open acceptance work

The old worker and daemon exited through their explicit retirement/shutdown
operations. The installed `boz` launcher now selects daemon
`2026-10-01-minimal-host-52b60eb23540` and worker
`2026-10-01-worker-f71d1b7bd178-1673d060ba87`. Activation recorded daemon PID
215237 and the actual loaded **CoreCLR 10.0.12** from SDK 10.0.401. API 4 health
passed. Separate SageFS listeners retained their process identity.

| Installed assembly | SHA-256 |
| --- | --- |
| Bozzetto.dll | `52b60eb23540852f1638cdb1d7058639ab65eb071905c5f1a7d711ad0fe54f66` |
| Bozzetto.Composer.dll | `f71d1b7bd178b4a676e0f5be242405ac02ea72bdb263e125deed9a3aa8a5825b` |
| Composer.dll | `1673d060ba8721132f3381e0b00e4d1b659bd1f3af92c766858f19a70a24c544` |

The full closure-manifest hashes are daemon
`70bca09af3fd02c38ec92c1552986baa9c8399215c8311c3bf80ae04651d337e`,
worker `fdb30b1dc85e6eccd2253b2e4aa8287cd3681c12a1f1f90baf71310b262e1383`,
and compiler `5a6d9c2ed3e88551a14f11b459450286783852ec13c259df3e8f6acf5fc956d0`.
The binaries were built from the implementation later committed at this source
anchor; gate identities are these binaries and manifests, not an assumed rebuild.

Fresh MCP/HTTP execution of the official `04d_IncrementalScalarRegions` fixture
against the installed closure passed:

| Phase | Compiled objects | Reused objects | Stable-function witness visits | Native output |
| --- | ---: | ---: | ---: | --- |
| Cold | 3 | 0 | 3 | `stable`, `before` |
| Unchanged | 1 | 2 | 0 | `stable`, `before` |
| One-function edit | 2 | 1 | 0 | `stable`, `after` |

The HTTP edit reservation preceded an MCP run refusal while the source still
matched its original hash. Only then was the source written. The stable function
retained its object/bitcode identity; proof checking ran afresh. The owned session
closed with no current artifact, busy work, pending revocation or cleanup error,
and its work lease was released. There were zero peer sessions, so this replay
does not claim peer-preservation or cancellation-race coverage.

Executed receipts under the same external root: `purge/activation-01/activation.json`,
`purge/live-aligned/run-01/{summary.json,order.tsv,closed-status.result.json}` and
the complete inventories in `purge/retired-binaries/`. Twenty-two retired
binary/cache roots were removed, including all three old daemon closures,
generated Host/patching outputs and the owned Harmony package cache. Audit
documents, logs and manifest receipts remain. One old private SDK directory is
still mapped by **separate SageFS PID 3785110**; it was retained to preserve that
running service. Neither the Bozzetto launcher nor its daemon/worker uses it.

The editor's source-only CCS8011 finding remains open; this is the first recorded
run past the previously blocking fixture checks, so its age is not established
by the earlier stopped run. Actual binary/socketed PSG revision transport also
remains unintegrated: the running worker still uses JSON over stdio. Remaining
compiler authority work stays in the
[cross-project checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md) and its
linked owner reports; this purge is not complete compiler or native-host acceptance.
