# Fidelity.Data migration handoff — 2026-10-02

**Paused at the user's request for resumption next week. This is an unfinished
source recovery checkpoint, not a release or a passing final gate.** The broader
[integration checkpoint](Incremental_Provider_Checkpoint_2026-10-01.md) records
the diagnostic sink and earlier compiler integration separately.

## Scope and corrections

The only current task is replacing foreign JSON implementations with Fidelity.Data
and removing demonstrably unused JSON codecs. Preserve behavior, wire schemas,
exact numeric literals and regression coverage. Do not redesign persistence,
scheduling, telemetry or other subsystems as part of this migration.

The friction recorder's unused JSON storage codec was removed; its SQLite store,
tools, hooks, review UI and reporting remain. Removing that subsystem was never
authorized. An earlier scope expansion also removed retained updater, UI stream,
Aspire, warmup cache and live-value behavior. Those removals were reversed before
this checkpoint. Some restored paths intentionally still use their old JSON
implementation and are explicit remaining migration work below.

Jupyter/NetMQ pruning remains: the CLI already refused that retired provider.
Unrelated CLI tests were retained in `CliFirstRunTests.fs`. The unused compiled
`DaemonClient.fs` moved intact to the deprecated GUI project; current product
surfaces do not use it. Do not expand work on deprecated frontends.

## Source anchors

All entries use integration branches; no release version was changed.

| Repository | Branch | Recovery anchor |
| --- | --- | --- |
| Bozzetto | `integration/audit-followup-20261002` | Commit containing this handoff; prior sink/listener anchor `18054f37` |
| Composer | `integration/hosted-boundaries-20261002` | `1718e4d` unfinished editor/driver migration; `7c9da09` tested Core/Alex migration |
| clef | `integration/diagnostic-sink-20261002` | `b0e01c5` JSON migration |
| Fidelity.Data | `integration/hosted-boundaries-20261002` | `7cc19d6` exact JSON numeric literals |
| Fidelity.PSG | `integration/hosted-boundaries-20261002` | `f153c75` generated diagnostic reference accounts |
| Calque | `integration/fidelity-json-20261002` | `06537e2` documentation example; no active foreign JSON dependency found |
| Fidelity.FSharp.Incremental | `main` | `d476aea`; unchanged by this migration |
| Alex | `main` | `4e1f859d`; unchanged by this migration |

## Work saved in Bozzetto

Explicit Fidelity.Data schemas now cover daemon state/ownership/settings,
error/status projections, MCP JSON-RPC parsing, MCP tools, HTTP/dashboard/SSE
payloads, theme/environment readers, cohort export/import and friction report
views. New helpers are `WireJson`, `CohortJson`, `LiveTestingJson`, `HttpJson`,
`DaemonStatusPayload` and `McpProtocolJson`. Related tests cover exact large
integers, malformed schemas, error cases and payload compatibility.

`McpProtocolJson` is the narrow adapter to the MCP SDK's required
`System.Text.Json.JsonElement`. It is an acknowledged hosted boundary, not a
claim that the SDK or the whole process has no System.Text.Json dependency.
Test-only serializer use as an independent compatibility oracle must be
distinguished from production serialization during the remaining inventory.

These broad Bozzetto edits **have not passed a full build or test suite**. An
earlier Core-only build succeeded before the final SSE/HTTP changes and scope
restoration. It does not validate this checkpoint. `git diff --check` passes.

Remaining production JSON implementations at pause:

- `Bozzetto/UpdateCheckService.fs`: updater payload/cache.
- `Bozzetto.Core/AspireSetup.fs`: configuration reader.
- `Bozzetto.Core/WarmupReplayCache.fs`: replay-plan storage.
- `Bozzetto.Core/WorkerProtocol.fs`: generic codec and retained consumers,
  including WarmupContext, LiveValueSnapshot, LiveTestHookResultDto and assembly
  load errors. Replace actual schemas explicitly; do not delete behavior to
  eliminate its serializer.
- `Bozzetto.Core/RuntimeCompat.fs` and `AppRun.fs`: runtime configuration and
  launch-settings readers. Preserve accepted comments/trailing commas where the
  existing reader supports them.
- Central/project `FSharp.SystemTextJson` references remain needed by the above.
  Remove them only after the callers have migrated and the build proves it.

## Evidence and its limits

Evidence root:
`/home/hhh/.codex/work/bozzetto-resumption-2026-10-02/validation/`.
Keep these external receipts and candidate directories for the next session.

| Check | Actual result |
| --- | --- |
| Composer migrated Core/Alex | Build passed; full suite **400/401**, existing occurrence-218 failure. Three new JSON tests passed. `fidelity-json/composer-build.log`, `composer-tests.log`, `composer-results/*.trx`. |
| Clef migration | Build passed; full suite **2,215/2,314**, 99 failures. Fresh identity comparison gives **99 baseline, 99 current, zero new/resolved**. `fidelity-json/clef-{build,tests}.log`, `clef-results/*.trx`, `clef-baseline-comparison.log`. |
| Lattice/editor migration | Last build failed at `FidelityJsonFormatter.fs:98` with FS0691. The syntax correction is committed but **not rebuilt**. New formatter cancellation/live-LSP tests have not executed. `fidelity-json/editor-build.log`. |
| Earlier Bozzetto Core subset | Build passed, not the final checkpoint. `fidelity-json/core-build.log`. |
| Earlier diagnostic sink candidate | PSG **308/308**; native Composer tier **48/48 Trusted**. Bozzetto default **9,005 ran: 9,001 passed, one failed, three ignored**. Listener fix in `18054f37` still needs its unfiltered rerun. `diagnostic-sink/{psg-tests,native-tests,bozzetto-tests}.log`, `trust.jsonl`. |
| Earlier sink artifact integrity | Explicit successful checks for **223 manifest entries**; worker closure **36 declared, zero missing/test dependencies**. `diagnostic-sink/candidate-manifest.sha256`, `candidate-integrity.log`, `worker-closure.log`. These receipts cover that candidate, not the later JSON source. |

The sink candidate remains under `candidate/daemon-diagnostic-sink/` and
`candidate/worker-diagnostic-sink/` beneath the same work root. It has not replaced
the installed daemon. The sink's `occurrence-structure-v1` validation is scoped
structural evidence, not full semantic/proof validation; retain that distinction.

## Resume in this order

1. Read this handoff, local AGENTS instructions and `skills/bozzetto/SKILL.md`.
   Preserve the shared installed daemon and sessions. No inherited FSI service
   is needed. Acquire/release work leases for builds and unfiltered test suites.
2. Build the current Bozzetto test project and Composer editor project before
   extending migration. Fix integration/compile errors; do not weaken tests or
   remove callers to achieve a clean dependency search.
3. Finish the explicitly listed Bozzetto JSON readers/codecs. Preserve exact
   integer spellings, option/null/casing/union shapes and input validation.
   Recheck production references and package closure afterward.
4. Follow the [Composer handoff](../../Composer/docs/JSON_Migration_Checkpoint_2026-10-02.md):
   run compiled editor proof/cancellation and live stdio scenarios, build the six
   standalone driver projects, and run their applicable whole suites.
5. Run the unfiltered Bozzetto default suite and complete Composer integration
   tier against freshly published matching candidates. Read the TRUST rows;
   the sink partial-frame regression must execute and pass. Retain existing
   compiler/editor baseline failures separately from new regressions.
6. Record exact source and artifact identities and push useful integration
   checkpoints. Release acceptance, versioning and installed-daemon promotion
   remain separate; do not infer them from these source pushes.

The build lease `24705ffc8e70445fb1f98f582b89845a` was explicitly released at
pause. No new daemon or long-running build/test was left running by this tie-off.
