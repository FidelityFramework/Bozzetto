# Clef / Composer provider handoff

September 30, 2026. This is an implementation assignment for a peer agent; the coordinating compiler agent audits the resulting patch and evidence. Deliver a small .NET provider host for opening, building, inspecting and running a Clef project. This document does not announce an implemented provider.

The [Clef / Composer development plan](Clef_Composer_Development_Plan.md) places this assignment in the larger delivery sequence: MCP dependency cleanup, this bounded provider and its audit, shared Bozzetto integration, a standalone Composer MCP host, and the human development loop. The standalone host must reuse the same provider operations and session authority rules. This handoff remains the first provider milestone's acceptance specification; the broader MCP work does not bypass its audit boundary.

## Starting point and ownership

The reviewed Bozzetto baseline is `4f36b847`; compiler references are Composer `dc833dc23d13e119d1db0d2d1d53469220608968`, Clef `20fdab2`, Fidelity.PSG `61c0019` (schema 7), and Alex `6a252dd`. Recheck HEADs and preserve existing dirty work before implementation. These pins describe the inspected contracts, not a requirement to discard newer fixes.

Bozzetto currently serves F#. [SessionProjectTarget](../Bozzetto.Core/SessionProjectTarget.fs) accepts `.fsproj`, `.sln`, `.slnx` and explicit bare sessions; its `.fidproj` refusal is correct. [FsiSession](../Bozzetto.Core/FsiSession.fs) is an FSI abstraction, not a generic language provider. Merely accepting another suffix would route Clef into the wrong evaluator.

Baker owns source semantics, settlement and proof premises. It publishes `Fidelity.PSG`; Alex passively witnesses those facts. Bozzetto must preserve compiler refusals, without repairing graphs, choosing widths or converting pending proofs into acceptance. .NET hosts the tools; it does not define Clef semantics. Native live-state HMR, ORC, self-hosting, proof reuse and complete language parity are outside this milestone.

Read [AGENTS.md](../AGENTS.md) and the [Bozzetto skill](../skills/bozzetto/SKILL.md). Use its REPL loop when available, report concrete unavailable/stale-tool friction, and retain the prescribed unfiltered test-tier acceptance. Follow two-space F#, central package versions and existing version policy.

For delegated reconnaissance and review, the local access guide is `/home/hhh/repos/speakez-lab/platform/agent-workers/HERMES-OPERATIONS.md`; retrieval access is described in `platform/retrieval/README.md` and the newer `docs/CURRENT-OPERATIONS.md` in that repository. Worker bases are `https://c-proxy.spkez.dev/workers/one`, `/workers/two` and `/workers/three`; retrieval is `POST https://duckdb-fidelity-pgq.spkez.dev/v1/retrieval`. Each worker accepts one assignment at a time. Supply a bounded task and exact source revisions, use configured private token files for authentication, and verify returned claims against source and tests. Workers do not inherit this conversation or local working-tree edits.

## Existing API to wrap

Use `Core.CompilationOrchestrator.ProjectSession`, documented in [Incremental project sessions](https://forge.spkez.dev/FidelityFramework/Composer/src/commit/dc833dc23d13e119d1db0d2d1d53469220608968/docs/Incremental_Project_Sessions.md). The implementation is [CompilationOrchestrator.fs](https://forge.spkez.dev/FidelityFramework/Composer/src/commit/dc833dc23d13e119d1db0d2d1d53469220608968/src/Core/CompilationOrchestrator.fs); the minimal existing caller is [IncrementalProject.fsx](https://forge.spkez.dev/FidelityFramework/Composer/src/commit/dc833dc23d13e119d1db0d2d1d53469220608968/tools/IncrementalProject.fsx).

| Existing member | Contract for the adapter |
|---|---|
| `ProjectSession(options, directory)` | One explicit CPU `.fidproj` and private session directory. Use `CompilationOptions`; deployment and partial LLVM/MLIR-only output are refused. |
| `Reserve(editLabel)` | Returns an opaque `Core.IncrementalBuild.Ticket`; immediately withdraws the current executable. |
| `BuildAsync(ticket, cancellation)` | Returns `Task<Result<Accepted,string>>`. The overload taking only cancellation reserves immediately before reading disk. |
| `Current`, `ManifestPath` | Accepted metadata option and atomic status-file path. Reading status does not revalidate disk. |
| `RunCurrentAsync(arguments, cancellation)` | Returns `Task<Result<Run,string>>`; checks current inputs and executable bytes before execution and validates again afterward. |
| `IDisposable.Dispose()` | Withdraws authority and prevents old work becoming current. |

`Accepted` contains generation, source version, artifact path/hash, object-manifest path, witness statistics and compiled/reused/retired object identities. `Run` carries generation, source version, exit code, stdout and stderr. Preserve these identities in responses. There is no existing `InvalidateCompiler` or generic provider API; any adapter types introduced below are new work.

## First implementation slice

Define typed provider identity, distinguishing the existing F# provider from `ClefComposer`, and typed operations/results. Associate each response with provider, session, compiler epoch and generation. Keep F# evaluation unavailable for Clef sessions. Support explicit project-open, reserve, build, status, run and close before adding automatic discovery or dashboard controls.

Start with a dedicated .NET provider worker using the existing Composer API, so it need not share compiler globals with FSI. Keep the adapter independently testable; proposed new `ClefComposerProvider` and provider-contract files should be small. Existing integration seams are [SessionManager](../Bozzetto.Core/SessionManager.fs) for ownership/supervision, [WorkerProtocol](../Bozzetto.Core/WorkerProtocol.fs) for session identity and responses, and [McpTools](../Bozzetto/McpTools.fs) for explicit operations. [DashboardTypes](../Bozzetto/DashboardTypes.fs) exposes current session actions for a later UI adapter. These presently assume F# workers: inspect their callers and serializers before extending them. Do not rewrite the FSI interface or silently change persisted session meaning. Register new files in the relevant project and tests in the existing test infrastructure.

Reserve **before** an editor writes source or dependency files. Keep the opaque ticket within its owning provider session; bind any transport reservation ID to that ticket, session and epoch. A build uses the ticket once. Pending, invalid, canceled, superseded or corrupt generations expose no runnable current artifact. Never launch a cached `ArtifactPath` directly: call `RunCurrentAsync`. The host already captures manifests, dependencies, source ordering and checker-read texts, then validates fresh disk receipts. Watcher notifications and timestamps cannot replace those checks. Status can describe last accepted metadata, but cannot promise it remains executable without the run gate.

Keep reservations and cancellation responsive while builds run. Do not block the session supervisor mailbox awaiting compilation; post completion tagged with its authority and reject late results. Each project needs its own output directory and lifetime; Composer already leases the directory and serializes its backend worker. A canceled caller must not acquire another session's result. Cancellation revokes acceptance; immediate termination of every compiler or external tool is not promised.

Composer's private `checkerGate` already serializes its source-check/publication path. Do not reflect it, replace it, or hold a monitor across an awaited `BuildAsync`. The first worker should call CCS only through `ProjectSession`. If direct CCS or CCS.Editor calls are later cohosted, all such entry points need one coordinated asynchronous scheduling boundary; an adapter-only lock does not serialize outsiders. Avoid nested scheduling waits and prove responsiveness with concurrent-session tests.

Add an explicit compiler epoch at the adapter boundary. Before applying or adopting a compiler change, withdraw affected handles, cancel old attempts, and retire/dispose their sessions and retained state. Compare epochs on completion and before run; old work must never restore current status. For this milestone, recreate the provider worker against the selected compiler binaries before accepting new work. Disk assembly hashes do not detect Harmony patches in memory: a patch path must trigger the same fence before mutation, or be unsupported for an active compiler host. This is an adapter protocol to implement, not an existing Composer guarantee.

## Acceptance and audit

Use real Composer compilation for native acceptance, with temporary fixture copies outside repositories. Existing [ProjectSessionTests](https://forge.spkez.dev/FidelityFramework/Composer/src/commit/dc833dc23d13e119d1db0d2d1d53469220608968/tests/Alex.Tests/ProjectSessionTests.fs) and adjacent `IncrementalBuildTests.fs` provide concrete controls.

1. Open F# and Clef sessions explicitly; reject wrong-provider operations and cross-session tickets. Two simultaneous projects must keep status, cancellation, outputs and object caches separate.
2. Run `04d_IncrementalScalarRegions`: cold output `stable\nbefore\n`, exit 0; unchanged rebuild retains both scalar objects; changing only `changeable` from false to true yields `stable\nafter\n`. Verify actual retained `.o` paths **and SHA-256**, not counters alone. At the reviewed baseline cold/unchanged/edit compile 3/1/2 objects and retain 0/2/1; the common object rebuilds. Explain any changed counts against the current compiler contract.
3. Invalid edits, unannounced disk edits, dependency changes and missing/corrupt executables refuse execution and withdraw current authority. Source diagnostics, proof dispatch and artifact admission remain distinct outcomes.
4. Use explicit barriers to cancel or supersede a check/build; release old work afterward and prove it cannot restore current status. A valid surviving session must still build and run. Verify run results are refused if their generation changes during execution.
5. Change the compiler epoch during a held build and after acceptance. Both old completion and old execution must be refused; fresh-host compilation is required. Include the in-memory patch event, not only changed DLL bytes.

First deliver the small provider host and these discriminating tests. Then the coordinating agent audits the exact diff, API routing, concurrency, receipts and refusal paths before broader integration. Record source revisions/dirty patches, fixture and tool identities, executed test counts, outcomes, object hashes and native output in external evidence; summarize the result here without importing raw logs. Filtered or zero-test runs are not final acceptance. Full source/MLIR/artifact proof remains mandatory on every generation; dispatch-plan materialization and successful solver responses alone do not grant execution authority.

Choose and document the minimal worker transport, deployment-independent compiler assembly resolution, provider identity serialization compatibility, and the concrete pre-patch epoch event. Use the bounded worker design above to make a reviewable first implementation; the auditor will assess these choices with the patch and tests. Keep the decisions explicit; do not invent an existing API to hide them.
