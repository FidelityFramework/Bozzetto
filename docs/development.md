# Development and acceptance

Bozzetto controls development work; it does not decide compiler meaning, proof validity or artifact authority. Implement changes at the component that owns the fact, then validate the consumers of that fact. The governing engineering rules are in `AGENTS.md`, especially Working principles, Validation posture, Finding code and Architecture. Load `tools/skills/bozzetto/SKILL.md` before changing F#.

This procedure applies to controller, compiler integration, foundation and reactive-push increments. It replaces an FFI-specific handoff with one reusable rule ledger and binding review. It does not authorize a new feature, dependency or scheduling policy.

## Protect the three centerpiece contracts

| Centerpiece | Implementation obligation | Discriminating check |
|---|---|---|
| Fidelity.PSG | Preserve compiler-owned, immutable, revision-bound evidence and source-authorized scoped delivery. Bozzetto routes evidence, not reconstructed semantic authority. | Change a premise or delivery scope and require the affected authority to be withdrawn or refused, even if the displayed result is unchanged. |
| Fidelity.FSharp.Incremental | Use the shared owner for complete declared reads, dependency bookkeeping, explicit demand, cold admission, Offers, withdrawal and joined lifetimes. | Withhold admission, remove one of two demands, change a declared read and delay cleanup. Observe each distinct transition. |
| Ranvier | Use its signal/memo/effect shape as the reactive reference: one mutation domain, immutable admitted inputs and coherent pushed views. | Interleave updates and reconnects; require atomic installation of each fact and daemon-incarnation/connection-generation authority fencing before accepting newer publications. Health revision, Leases revision, board sequence, Resources sequence and Snapshot revision are independent stamps, not one cross-fact version. |

These are requirements, not blanket claims of delivered integration. Follow [component contracts](Bozzetto_Fidelity_Component_Contracts.md), [foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md) and [reactive push](reactive-push.md) for their owning definitions.

Keep three distinctions explicit:

- An Offer establishes foundation eligibility under declared dependencies. Baker and Composer still establish proof, artifact correspondence and execution admission.
- A memo's equal display value cannot erase a changed proof identity, source revision, compiler epoch or authority handle.
- Effect cleanup, cancellation selection and observer completion cannot certify physical drain. The owner must account for children, callbacks, processes and resources. The foundation does not dynamically discover reads.

Direct daemon-worker, daemon-browser and agent-owned interchange must be typed binary. Replacing a JSON serializer is not compliance. MCP's externally standardized JSON-RPC boundary is the sole external exception; see [MCP tools](mcp-tools.md). JSON is permitted only as Fidelity.Data export, with the diagnostic sink running in an independent MailboxProcessor-driven process. Neither export nor diagnostics may grant compilation authority; see [diagnostic sink](Bozzetto_Diagnostic_Sink.md) and [binary contracts](binary-format-spec.md).

## Ground and bound the increment

Before implementation, identify the governing clause, owning component, affected consumers and exact acceptance boundary. Ask the owner yes/no questions for unsettled scope, schedule, dependency or policy choices. If a decision appears mid-increment, stop that item rather than silently choosing a policy or blocking unrelated settled work.

Use retrieval first for enrolled repositories:

1. Request `schema`. Require `freshness.fresh = true` and `snapshot_id = current_snapshot_id`; inspect current `tracked_sources` rather than an old repository list.
2. Use `find`, `pgq` and `sources`, pinned to that snapshot. Cite repository, revision, path and returned lines.
3. Push useful checkpoints before querying new work: the index covers pushed heads only.
4. Record the allowed direct-read case: repository absent from the fresh schema, uncommitted diff, stale index, or bounded confirmation of a located line. Do not grep an entire repository.

If retrieval is unavailable, report that limitation. An unavailable service does not establish an outside-index exception. Public hybrid search takes public language terms only, never private repository material.

Prefer extending or deleting at the owner. Each new file, dependency, abstraction or duplicated producer/reader rule needs a reason. No second authority, inferred replay or fallback may conceal a missing owner contract. Delegate bulk first-pass reading where workers are available; include retrieval and lease rules in every brief. Use one reviewer and one checklist.

The owner's 2026-10-11 authorization lets the implementer coordinate agents and carry out settled implementation work without asking again for routine delegation. The [implementation handoff](ffi-implementation-handoff.md) records the current FFI role assignment. Preserve earlier acceptance evidence at its recorded scope while auditing its architectural assumptions against current code and governing clauses. LAN research and local design reviews supply leads and implementation checks; the auditor owns the acceptance verdict and the decision to widen regression. Unsettled owner decisions retain the boundary above.

### Authoring conventions

Target `net10.0` and the `.slnx` solution. Use two-space indentation, immutable data, discriminated unions, pattern matching, small pure functions and `Result` for expected failures. Prefer cold `async`; isolate required CLR interoperability at execution edges. Package versions belong in `Directory.Packages.props`; adding a package requires owner agreement. Change the shared release version only for an owner-requested release.

Tests use Expecto with FsCheck where appropriate and `Expecto.Flip`, with the assertion message first. Tracking IDs do not belong in `testList` names. Commit useful checkpoints on `main` with Conventional Commits and push them promptly. Preserve exact dependency closure identities rather than relying on package names alone.

Composer is a differential compiler. Reserve “incremental” for Fidelity.FSharp.Incremental and its `Incremental<'T>` values.

## Leased implementation and validation

Bozzetto's own F# work needs no Composer project session. Every caller-owned build or suite needs a lease. Existing local commands are:

```bash
export BOZZETTO_AGENT=<stable-agent-label>
export BOZZETTO_PROJECT=<stable-project-label>
dotnet fsi tools/build/lease.fsx full_build dotnet build
dotnet fsi tools/build/lease.fsx test_suite_run dotnet src/Bozzetto.Tests/bin/Debug/net10.0/Bozzetto.Tests.dll --summary
```

Run the built test DLL locally; reserve `dotnet test` for CI. The labels are caller-reported context, not authenticated identity or acceptance proof (`tools/build/WorkLease.fs`). MCP acquire/release tools bind ownership to the connection: proceed only on grant and release the exact lease through that connection, including after failure. **Lifecycle gap:** MCP exposes neither renewal nor withdrawal tools, so it does not currently provide the complete connection-bound lifecycle for long-lived work or abandoned asks. Do not imply MCP renewal or use an HTTP holder to renew a connection-owned grant. The complete HTTP lifecycle, used by `tools/build/WorkLease.fs`, is `/api/lease/request`, `/api/lease/renew`, `/api/lease/withdraw` and `/api/lease/release`; use `tools/build/lease.fsx` for work needing that lifecycle. See [operations](operations.md#leases-admission-heartbeat-and-queues) and [MCP tools](mcp-tools.md).

A lease is a heartbeat, not a deadline for live work. Honor wait/refused decisions. Keep a stable holder per agent and project so a retry is the same queued ask; withdraw an abandoned ask. On memory deferral, investigate available RAM, swap, RSS with parents and cgroup usage rather than polling. Distinguish owned processes, unrelated builds and editor language servers. Ask before stopping processes not owned by the caller or changing admission policy.

Clef/Composer projects instead follow [Composer workflow](composer-workflow.md): open an explicit `.fidproj`, reserve before source mutation, build through the provider and execute only through `composer_run_current`. Component gates accept compiler work; a Bozzetto provider journey separately accepts its adapter and deployment seam.

Development daemon restarts are authorized, but never interrupt another caller's active work. Validate the changed owner, check leases and Composer work, preserve rollback, replace only the intended matching deployment and make one bounded identity/readiness check. The daemon serves MCP, HTTP and UI on 47749 and control on 47750. See [operations](operations.md) for deployment binding.

## Focused validation and earned regression

Select tests for the changed owner and its direct consumers. Record the selection so a filtered result proves only its declared scope:

| Flag | Selection |
|---|---|
| `--filter <path>` | Slash-separated hierarchy prefix |
| `--filter-test-list <substring>` | List names |
| `--filter-test-case <substring>` | Leaf case names |

Check what actually ran. A zero-match Expecto filter can otherwise look successful. `TrustSignal.scopeOf` also treats `--run` and `--stress` as narrowed (`src/Bozzetto.Tests/TestInfrastructure.fs`).

Register new gates as ordinary `[<Tests>]` values or through the structural `Integration` registry, never solely through a name filter. `Integration.hostList` and `hostCase` register self-contained host tests; dedicated gates need a real runner and pipeline selection. The registry separates retired F# journeys from executable acceptance and removes retired nested leaves from active host lists (`src/Bozzetto.Tests/TestInfrastructure.fs`, `Integration`). Retired counts are not passed or ignored evidence.

At integration, the auditor reviews the focused owner and consumer evidence, shape matrix and discriminating mutations, then decides whether full regression is earned. Record the decision, the risk being tested, exact tiers and expected wall-clock cost before dispatch; record elapsed time with the result. No full sweep runs automatically per increment. A default unfiltered tier is not evidence for omitted host, Composer or consumer tiers. Repeat a run only for a recorded cause such as changed acceptance inputs, a binding mismatch or an unexplained result. The auditor normally verifies retained evidence instead of rerunning suites.

Acceptance is the auditor's verdict against this declared evidence scope. It does not change a runner's verdict: `NarrowedRun` remains narrowed, and omitted cases remain unverified. Historical unfiltered gates and their accepted counts retain their original scope.

`ci-pipeline.fsx` builds the solution once in Release and declares default and sharded host tiers. `ci` adds the mutation-score tier; `composer` adds a worker build and provider tier using `BOZZETTO_COMPOSER_DISTRIBUTION` and `BOZZETTO_COMPOSER_FIXTURE`; `pack` adds package creation and payload checks after the trust report. These are implementation facts, not receipts of successful execution.

```bash
dotnet fsi ci-pipeline.fsx
dotnet fsi ci-pipeline.fsx -- ci composer
```

The script has its own lease acquisition; do not add nested leases to its tier children. Running `dotnet fsi` as build tooling does not restore an embedded F# product service.

## Read TRUST, not a green stage

`TrustSignal.runObserved` compares Expecto's summary with the registered tree, prints a `TRUST` line and optionally records a row through `BOZZETTO_TRUST_LEDGER` (`src/Bozzetto.Tests/TestInfrastructure.fs`). Preserve the verbatim line and the underlying report.

| Verdict | Meaning and acceptance action |
|---|---|
| `Trusted` | Unfiltered, nonzero execution count equals registered count, no failures or errors. Check ignored cases separately. |
| `NarrowedRun` | Passing narrowed run. Evidence only for the selected scope; never a whole-tier result. |
| `TestsFailed` | Failure or error. Report it as a failure. |
| `NothingRan` | Zero execution. Not acceptance; trust exit is 3. |
| `CountMismatch` | Unfiltered registered/ran mismatch. Not acceptance; trust exit is 3. |
| `SurvivorsUnderBar` | Mutation-score threshold met but mutants survived. Report survivors explicitly; this does not prove the increment's new rules. |

The implementation counts ignored cases in `Ran` and does not make a nonzero ignored count reject `Trusted`. Do not translate that verdict into “every case passed.” Record ignored cases and reasons; the handoff's executable acceptance gates require `skipped=0`. Any proposed exception needs an owner decision (`src/Bozzetto.Tests/TestInfrastructure.fs`, `TrustSignal.Tally` and `judge`).

The pipeline attempts every selected test tier despite another tier's failed exit, joins workers and merges their rows. Its trust report rejects missing rows (`NoReport`) and green verdicts paired with nonzero process exits (`ExitMismatch`). Earlier build or setup failure can still prevent the tier stage from being reached. Preserve that distinction (`ci-pipeline.fsx`, `runTiers` and `trustLines`).

Current code accepts both `Trusted` and `SurvivorsUnderBar` in the pipeline report. This qualifies `AGENTS.md`'s shorthand that every non-Trusted tier fails CI. A green mutation-score stage cannot replace a killed mutant for each new rule.

## Controls that discriminate the contract

Maintain a rule ledger for every new or changed check, refusal or invariant. A positive source control proves valid input remains accepted; a negative source control asserts the exact refusal code and reason. The implementer executes one mutant per new rule and records the test that kills it before handoff. A compile error, unrelated setup failure or unscheduled test is not a killed behavioral mutant.

Choose a bounded matrix from the actual diff, not a broad defensive sweep:

| Changed boundary | Required controls |
|---|---|
| Differential compilation | Fresh build, unchanged repeat and selective edit. Compare accepted output/authority and per-region witness/object states with a fresh reference; show unaffected reuse and affected withdrawal separately. |
| PSG delivery | Authorized scope, unauthorized scope, exact-base delta, wrong base, stale epoch and receiver-installation fence. Do not transfer a full retained graph as a shortcut. |
| Foundation admission | Zero work before admission; complete declared reads including versioned negative facts; shared demand with one client leaving; unchanged retention with fresh eligibility and revoked old handles. |
| Reservation and launch | Pause between scheduling and physical launch, let reservation win and assert zero obsolete launches. Test the opposite ordering and ownership of already launched work. |
| Withdrawal and drain | Late completion, failed cleanup, delayed callback or real child exit, close under pressure and epoch replacement. Observe authority withdrawal separately from physical drain. |
| Reactive presentation | Equal display value with changed proof identity; immutable input admission; one mutation owner; event gap, atomic per-fact reconciliation and authority fencing with independent fact stamps. |
| Direct wire or export | Binary round trip, bounds/version refusal and identity preservation. JSON export cannot be accepted as a direct-wire fixture or affect admission. |

These are required test selections when the boundary changes, not claims that these gates already exist. For compiler source shapes, cover every touched form, such as records, unions, tuples, copy-update, nested/recursive forms, conditionals and arrays. Check, publish and invoke the compiler owner's integrity check where that contract applies. Do not invent a host integrity authority. Mark each row clean, intentionally refused with code, or unexpected; any unexpected row blocks handoff.

Measure differential work separately from correctness: duplicate work, queue delay, cold/reused latency and retained memory. Reuse alone is not a performance claim. Use controlled interleavings and portable command/effect traces where appropriate, plus real process ownership tests at physical seams. Native hosting and distribution require their own conformance evidence; local .NET fixtures cannot establish either.

## Evidence binding and known drift

Grade claims as **E** executed, **R** source read, **S** specification/requirement or **I** inference. Attach revisions and paths to source claims, and commands, counts and retained artifacts to execution claims. An agent report is not execution evidence. The precedence is executed unfiltered result, pinned code read, specification, then report/inference.

Bind all changed paths to pushed, clean heads with a verifying digest manifest. Bind built artifacts and dependency closures to those heads, and bind ledgers to original logs and machine-readable reports, including TRX where produced. Do not claim every tier emits TRX without checking its runner. Record worker protocol compatibility separately from the loaded PSG schema and compiler distribution identity.

Current validation machinery contains drift that must not become the intended contract:

- `tools/build/WorkLease.fs` sends JSON HTTP lease requests and parses JSON replies. The target is typed-binary direct control and explicit installed-version mismatch handling, not serializer substitution or a compatibility fallback promoted into policy.
- `TrustSignal.record` writes JSON rows using `System.Text.Json`; the pipeline directly reads/merges JSON ledgers and reads/writes JSON timing maps. These are current files, not an approved interchange contract. The required replacement keeps typed binary authoritative evidence and uses Fidelity.Data JSON only at export edges; exports read by a human or external tool need not pass through the independent diagnostic sink, while direct pipeline/agent messages cannot be recast as exports.
- `DaemonIdentity.reportsPid` parses a JSON control reply, and `BozzettoBinary.path` selects the newest Debug/Release executable by timestamp (`src/Bozzetto.Tests/TestInfrastructure.fs`). Verify and record the actual selected binary digest; neither timestamp nor successful health response binds it to the claimed head.

Preserve existing receipts while removing these mechanisms at their owners. Do not hide failures or delete evidence to make the documentation agree with the remit. Binary transport, full foundation adoption, Ranvier-shaped settlement, native execution and authenticated distributed operation each need separate source and executed evidence before being called implemented. LLVM ORC JIT remains planned under `AGENTS.md`.

Tier scratch is keyed by checkout path under `${XDG_CACHE_HOME:-$HOME/.cache}/bozzetto/tiers/<checkout-name>-<path-hash>/` (`ci-pipeline.fsx`, `tierWork`). The merged ledger/report currently live in checkout `test-results/`; preserve them and external tier logs before cleanup. Keep new scratch out of the checkout and `~/repos`.

## One-page increment handoff

Keep the filled handoff to one page. Put rationale in the governing specification or code, and link bulky matrices and receipts. This format applies to all three centerpiece contracts, not just FFI.

This document holds the evidence binding and the handoff format; the dated checkpoints that once carried them are evidence outside the active documentation. The FFI correction, the one increment series still in progress, keeps its standing record in [ffi-correction-compendium.md](ffi-correction-compendium.md).

```text
Increment / date / implementer:
Scope: governing clause, owning component, changed items and consumers.
Heads: repository -> full pushed SHA, clean; base -> head.
Dependencies: exact package/assembly/distribution identities.
Gates: focused changed-owner/direct-consumer selection; auditor's full-regression
  decision with risk, exact scope and expected wall-clock cost; commands and
  filters; actual elapsed time; lease grant/renewal/release evidence;
  verbatim TRUST lines; counts including ignored/skipped; baseline delta
  (missing, added, new failures, resolved); original report/log paths.
Binding: manifest path and coverage of every changed path; built artifact
  digests and head/closure provenance; selected worker and protocol identities.
Rule ledger:
  owner file:line + rule | positive control | negative code/reason control |
  mutant -> existing killing test + executed receipt
Matrix: touched source forms or boundary states -> clean / intended refusal
  code / unexpected; detailed receipt link. No unexpected rows permitted.
Growth: per-repository diff shortstat; each new file/owner/dependency and reason.
Context: retrieval snapshot/revisions; each allowed direct-read case or limitation.
Residuals: unsupported/refused state -> code, tracking item and owner.
Decisions: none, or yes/no questions with governing clause.
```

The implementer supplies adversarial checks before transfer. Do not ask the reviewer to discover missing controls or mutants. Preserve logs, manifests and trust ledgers outside the active documentation tree when obsolete pages are removed; removals and inbound-link repairs require owner approval.

## Auditor binding review and return

Claude Code running Opus 5.5 checks binding first: heads equal the claimed remote heads; manifests verify; artifact provenance matches; ledger lines agree with original reports; every rule-ledger test exists and belongs to the declared executable gate. It records whether the proposed evidence scope addresses the change's risk and whether broader regression is earned. An unperformed check remains unverified, not grade E.

Review the diff with one checklist:

- Producer and reader agree; the reader does not reject valid source by adding its own authority.
- Every new rule has positive/negative controls and a killed mutant.
- Withdrawal is as explicit as admission; physical drain remains separately evidenced.
- PSG proof identity, foundation eligibility and Ranvier presentation remain distinct.
- Every owner maintains its facts; no replay, inferred authority or fallback substitutes for them.
- The touched-form/state matrix is complete and growth is justified.
- Direct interchange is binary; export and MCP exceptions stay at their boundaries.
- Retrieval context, heads, manifests, dependency closures and acceptance scope bind together.

Reproduce existing gates for a binding mismatch, ledger anomaly or owner-requested spot check; widening regression requires the auditor's recorded risk, scope and wall-clock rationale. Record the reason and whether reproduction agrees. Do not repeat an unfiltered suite as a review ritual.

Return findings only, most severe first: severity, E/R/S/I grade, file:line, defect, required fix and discriminating exit test. Conclude **accept**, **accept with follow-up** or **changes required**. A failed required gate or unexpected matrix row cannot be relabeled a harmless residual.

Do not restore embedded F# session hosting, REPL warmup/hot reload, runtime patching, Datastar, the Raylib GUI, the Visual Studio extension, SageTUI, terminal rendering or SSE as the UI pipeline. Retained component harnesses are not product capabilities. Keep current-versus-planned boundaries aligned with [architecture](architecture.md) and the [documentation index](README.md).
