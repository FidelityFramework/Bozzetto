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

1. **Compiler-owner promotion:** provide a validated immutable compiler closure
   and then repeat the owned reserve/build/run/reuse journey against its explicit
   digest. Current checkout test results do not update the deployed worker. This
   is pending compiler integration work, not a newly found Bozzetto defect.
2. **CLI discoverability:** deployed `boz --help` still leads with “F# Interactive
   daemon,” `fsi` checks and F# session instructions, and omits the Composer
   browser/provider entry path. Align help with the already documented provider
   choice. Keep the accurate provider-specific status labels. A subsequent
   checkout inspection found the peer's pending help-text repair in `Program.fs`
   and related documentation. This audit has not built or deployed those edits;
   they remain outside this documentation checkpoint.
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
