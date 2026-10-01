# Compiler continuation follow-up — September 30, 2026

Bozzetto is serving a useful coordination role during the current compiler repair
work. The auditor acquires and releases its MCP `test_suite_run` lease around the
serialized .NET build/test lane. Tests execute directly against the compiler
checkout; this use does not establish that Bozzetto's pinned worker contains those
repairs.

The earlier [independent incremental journey](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md)
remains the native workflow evidence: cold build, unchanged rebuild, one-function
edit, unchanged-region object retention, and run-authority withdrawal before a
source write. It was not rerun for this note.

## Fresh observations

Read-only checks on October 1 UTC (September 30 local) found:

- `boz status` returned the running daemon, PID 765938, version 0.6.834.0, the
  dedicated external workspace, current root MCP URL and explicitly labeled F#
  session count.
- `/health` reported `healthy=true`, `overall=Healthy`, normal memory pressure
  and no component failures. This is a momentary reading, not a sustained load
  measurement.
- `/api/composer/sessions` reported a configured `clef-composer` provider and
  the same pinned compiler digest
  `F2B2CEDAD8F753D279BEF5BC6E6D69A9319227D076C27FE9A0108F99EC5F626B`.
  `inMemoryPatchAllowed` remains false.
- MCP lease receipt 7287 recorded a granted test-suite lease. This corroborates
  the coordination use above; it is not a new native acceptance gate.

Small external receipts are under
`~/.local/state/bozzetto/checkpoints/2026-09-30-compiler-residence-continuation/followup-status/`;
lease receipts remain in its parent directory. No service or session was changed
by these read-only probes.

## Follow-up priorities

1. **Compiler-owner promotion — completed October 1:** the immutable compiler
   and rebuilt worker passed selected native validation and the complete 29-case
   Composer provider tier. Successful retirement and a coordinated restart
   activated the new distribution. A fresh owned reserve/build/run/reuse journey
   passed against Composer SHA-256 `995047939b5f228436e7b9a96409510eb661e8743c5225a369e951899b23f491`.
   The [auditor assessment](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md#october-1-promoted-distribution-repeat)
   records its evidence and remaining Lazy/Result boundaries.
2. **CLI discoverability — deployed and independently checked:**
   the peer reports that CLI help, the repository README, docs index, agent guide
   and MCP reference now lead with Composer and label retained F# features as
   compatibility material. Auditor readback confirms that provider order in
   `Bozzetto/Program.fs`. On October 1 the peer reported a passing build and
   **9,802 passed, four ignored, zero failures; Trusted**. Those test results
   are peer-reported, not independently rerun for this note. The installed CLI
   was subsequently redeployed in release `2026-10-01-cli-67d11d379c82`.
   Independent `boz --help` readback now shows Composer guidance first, and
   `boz status` confirms that this CLI-only deployment preserved daemon PID
   765938. The later compiler promotion separately replaced that daemon with
   PID 3012680; the CLI-only deployment did not do so. Its deployment receipt is
   `~/.local/state/bozzetto/checkpoints/2026-10-01-cli-help-deployment/deployment.json`;
   auditor readbacks are under the sibling `2026-10-01-compiler-promotion/`.
   The implementation edits remain outside this auditor's documentation commits.
3. **Shared workspace authority:** continue the
   [editor workspace audit](Bozzetto_Editor_Workspace_Audit_2026-09-30.md).
   Bozzetto coordinates sessions and execution; compiler-owned observations and
   complete proof dependencies must cross into Lattice's editing/artifact views.
4. **Deferred concurrency design:** the owner explicitly wants measured process
   and thread scheduling, rather than accidental reliance on CLR defaults.
   Specify compiler-state serialization, worker/process boundaries, solver
   concurrency, queue bounds and cancellation before tuning. Preserve the same
   authority contracts on the later native/pthread host. No concurrency tuning
   or throughput claim belongs to this checkpoint.

There is no new runtime failure demonstrated here. Exact-request cancellation,
coordinated worker retirement, shared unsaved buffers and ORC remain separate
acceptance work; the status probes do not close them.

The October 1 promotion subsequently exercised successful worker retirement and
the provider tier, including its cancellation/cleanup controls. That bounded
evidence does not establish every cancellation race, unsaved-buffer integration
or ORC. Existing MCP clients must reconnect after the coordinated restart.
