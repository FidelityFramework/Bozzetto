# Bozzetto provider repair checkpoint — 2026-09-30

Bozzetto's R1–R4 repairs are implemented and the full default gate passes.
**Revised native acceptance and independent sign-off remain pending.** Composer's
owner has passed its lease-release tests, but its response still marks the
validated distribution selection pending. Do not use the previous worker DLL or
the changing shared compiler output as evidence that this repair passes natively.

This follows the [assessment](Bozzetto_Provider_Assessment_2026-09-30.md).
The original [provider checkpoint](Bozzetto_Provider_Checkpoint_2026-09-30.md) and
auditor's evidence remain intact. Harmony ownership and its passing build/runtime
checks are recorded separately in the
[Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md).

## Corrections

| Finding | Implemented behavior | Discriminating coverage |
|---|---|---|
| R1: partial retirement | Fence every session before any cleanup; independently attempt all disposals; aggregate errors. Worker retirement also rejects existing run/build/reserve/cancel requests. Retain failed cleanup from an open overtaken by retirement so a later fence cannot forget it. | Pure held-work/cleanup tests pass. Added real two-session wire regression with a failing `current.json` persistence path and an actual exclusive `.session.lock` acquisition; execution awaits the revised compiler distribution. |
| R2: stranded activity | Cancellation registration lives inside the operation's removal `try/finally`. Callbacks revoke logical authority and schedule physical withdrawal; they do not execute fallible compiler reservation inline. | Pre-canceled build/run with reservation failure after backend mutation, recovery, cleared activity and no unintended launch pass. |
| R3: refusal identity | The worker remembers resolved session authority for validation exceptions. Backend reservation failures carry the revision under which the attempt occurred. | Added malformed-run-arguments and failed-reservation wire assertions for host, session, provider, epoch and revision; native execution pending. |
| R4: blocked supervision | A short authority lock holds cached state only. A separate asynchronous invocation gate orders backend prefixes, reservations and disposal; compiler IO never runs under the authority lock. | Pre-first-await run barriers, publication barriers, blocked disposal, pending reservations and obsolete cancellation withdrawals pass. |

Composer owns the private lease-release correction. Its response at
`/home/hhh/repos/Composer/docs/Bozzetto_Lease_Release_Response_2026-09-30.md`
records **11/11 passing tests** across its complete `ProjectSessionTests` and
`IncrementalBuildTests` modules. Its first disposal releases the lease in a
`finally` and retains the first persistence/cleanup exception. Repeated disposal
continues to report that failure; it never overwrites a replacement owner's
status. Bozzetto preserves that outcome instead of treating a second call as
successful cleanup. Bozzetto has not changed Composer implementation files.

## Observable contract and responsiveness

`authority.generation` is the adapter's revision. Artifact and run payload
`generation` values remain Composer's real generation. These are independent
counters: canceled/failed attempts can make them differ.

Status returns cached accepted metadata and requires execution revalidation.
It never reads the backend's `Current` getter. Status adds `revocationPending`,
`backendError`, `cleanupPending` and `cleanupError`. A cached artifact is not a
claim that disk contents are still valid.

Cancel acknowledges logical withdrawal immediately; physical Composer
reservation can remain pending or fail, visible in status. Close likewise
acknowledges logical closure with explicit cleanup state when disposal remains
pending. `cleanupPending=true` means cleanup has not completed successfully; a
retained error can keep it true permanently. A successful logical-close response
does not authorize compiler replacement. `prepare_compiler_change` awaits every
cleanup and only succeeds if all succeeded. Any error requires retiring the old
worker and observing process exit before a supervisor adopts a replacement.
Active in-memory compiler patching remains unsupported.

The controlled regression bound is **five seconds while backend barriers remain
held**, for status, cancellation and logical close. This demonstrates independence
from the held compiler work, not a production latency guarantee under scheduler
starvation. Successful reservation and completed physical cleanup intentionally
wait for backend ordering. Editors must await a successful reservation before
writing source. Run selection and launch still occur in Composer's synchronous
prefix before its task is returned; `IProjectBackend` documents this dependency.
A future compiler early-await change must revise this contract together with its
ordering tests. Cancellation cannot undo native-program side effects.

## Evidence and exact limits

Evidence resides outside repositories at
`~/.local/state/bozzetto/checkpoints/2026-09-30-provider-repair/`.

- Isolated pure-source FSI gate: **20/20, Trusted**. Nine new regressions.
- Full Release solution build: **zero warnings/errors**. The optional Composer
  worker is built separately against its explicit compiler distribution.
- Full unfiltered default suite: **9,784 accounted; 9,780 passed; four existing
  ignores; zero failures/errors; Trusted**. All 20 provider unit cases ran.
- Worker sources loaded in FSI and their handshake passed using the previous
  compiler API for type checking. This does **not** establish revised worker
  binary or native acceptance. The subsequent retained-open-error change is
  included in the final source check.
- The added native case is compiled into the registered dedicated Composer
  suite. That suite now contains 20 unit and four process/native cases. It has
  not yet been rerun against a validated revised compiler closure.
- No daemon/REPL MCP connection was available. Validation used the explicitly
  reported pinned .NET fallback; no user daemon was replaced or service stopped.

The owner-tested Composer hash is
`f2b2cedad8f753d279bef5bc6e6d69a9319227d076c27fe9a0108f99ec5f626b`.
The shared output's CCS hash already differs from the owner's recorded test
closure, so that directory is deliberately not selected for native acceptance.
The compiler owner must identify the complete validated distribution, not just
the Composer DLL. Preserve its closure and source identities when running the
dedicated suite.

## Next audit step

Build the optional worker against that explicit distribution and run the entire
`--integration-composer` tier. Record its exact worker/closure hashes, wire
responses, native outputs, retained object hashes and lease evidence here.
Then request the auditor's reassessment of R1–R4 against the final snapshot.
Shared daemon/MCP integration and standalone Composer MCP remain the next
milestones in the [development plan](Clef_Composer_Development_Plan.md); this
checkpoint does not claim those user journeys are delivered.
