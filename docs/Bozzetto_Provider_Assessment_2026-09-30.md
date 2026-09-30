# Bozzetto provider assessment — 2026-09-30

**The direction is sound, and the worker is already useful. Changes are requested before signing off the first provider checkpoint.** On normal paths, the adapter preserves Composer's authority and provides a practical way to exercise native Clef builds. Fault injection exposed retirement and error-handling defects that should be corrected before shared daemon integration.

This reviews the [checkpoint](Bozzetto_Provider_Checkpoint_2026-09-30.md) against the [handoff](Clef_Composer_Provider_Handoff.md). It is feedback for the implementing agent, not a new implementation assignment or approval of the entire identity migration.

## Scope and evidence

All **1,695 files** in the final source manifest matched the reviewed checkout. All **15 DLLs** in the Release worker closure matched their recorded hashes. The provider is uncommitted work over Bozzetto `4f36b847dad6798be4ff837067f4fc9642e424ce`; HEAD alone does not identify this checkpoint.

- Worker SHA-256: `DC95BC1C1D1333EB3387A6C68C30A1356FBB37542FB877CAAD9804853252EEA4`.
- Supplied Composer SHA-256: `04F1CDF91336FD699AD0C1A53F333940B22DF5B0FE9EF1CDFDC4AC2DD853169D`.
- Recorded default gate: **9,775 accounted, 9,771 passed, four ignored, zero failures/errors, Trusted**. All eleven provider unit tests appear in the log.
- Recorded dedicated Composer gate: **14/14 passed**, comprising eleven adapter tests and three process/native tests. Registration and exclusion of native tests from the default suite are consistent.

Those full-suite results were inspected, not rerun during this audit. Independently, the auditor rehashed the nine object-manifest entries across cold/unchanged/edit, confirmed retained paths and hashes, and confirmed the edited callable's digest changes. The recorded **3/1/2 compiled and 0/2/1 reused** progression is substantiated.

New exercise used the existing compiled worker: one real native build and execution, two-session retirement fault injection over stdio, malformed-argument handling, two cancellation-registration probes, and four synchronous-prefix blocking probes. Both probe scripts completed with exit 0, meaning the stated defects were reproduced—not that the provider passed those missing regressions.

The [Bozzetto skill](../skills/bozzetto/SKILL.md) was applied. No REPL MCP tools were connected, and the bounded `127.0.0.1:47750/api/daemon-info` probe returned connection refused. The isolated pinned .NET fallback was reported explicitly. No daemon was started/replaced, no production source was changed, and no shared binaries were rebuilt. This exercised the private Composer worker, not a live `boz` MCP/REPL journey.

## What should be preserved

[ComposerAdapter](../Bozzetto.Composer/ComposerAdapter.fs) calls `ProjectSession` directly, retains actual opaque tickets, and routes execution through `RunCurrentAsync`. There is no source repair, proof weakening, direct cached-executable launch, or semantic inference in the adapter. Baker → Fidelity.PSG → Alex → Composer ownership remains intact.

Normal-path generation checks, single-use reservations, immutable session epochs and refusal of late completions are well chosen. The existing native tests discriminate source/dependency drift and missing/corrupt executables. Status correctly says execution needs revalidation. Separate worker processes and explicit compiler distribution inputs are appropriate for this milestone; active compiler patching should remain unsupported.

## Required corrections

### R1 — High priority: a disposal failure leaves worker retirement partial

**Locations:** [Program.fs](../Bozzetto.Composer/Program.fs), lines 60–63 and 109–143; [ProviderSession.fs](../Bozzetto.Composer/ProviderSession.fs), lines 136–143. The underlying lease-cleanup defect is in Composer `src/Core/IncrementalBuild.fs`, lines 264–273.

The real-worker reproducer opens A and B, builds B, and verifies `stable\nbefore\n`, exit 0. It then replaces A's `current.json` with a directory to force a deterministic persistence failure. `prepare_compiler_change` sets `retired=true`, but A's disposal throws and prevents B from being closed. Subsequent B status reports `closed=false` and an accepted artifact; **B runs successfully again and accepts another reservation**. Only new opens consult the worker's retired flag.

After repairing A's path, retrying `close` reports success, but A's exclusive directory lease remains held. Provider and Composer both mark themselves closed/disposed before fallible status persistence; the retry skips cleanup, and Composer never reaches `lease.Dispose()`.

The failed fence returns an error: this is not a demonstrated false-success patch authorization, and a caller must not mutate the compiler after that error. Nevertheless, retirement must reliably withdraw every session's authority even when cleanup fails.

**Correction:** separate logical revocation from fallible cleanup. Fence existing operations as retirement begins, retire every session even if one fails, aggregate cleanup failures, and make cleanup completion truthful and retryable where needed. Coordinate the unconditional lease-release repair with the Composer owner; wrapping its failure in Bozzetto alone does not fix the owned resource.

**Regression:** two sessions, one accepted native artifact, injected close-status failure. All old-session run/build/reserve attempts must refuse; no late completion may restore authority; every lease must release; repeated close must accurately report cleanup status.

### R2 — Medium priority: cancellation registration can strand `Busy=true`

**Location:** [ProviderSession.fs](../Bozzetto.Composer/ProviderSession.fs), lines 84–100 and 110–131.

Both paths add a cancellation source to `active`, then register the caller callback before entering the removal `try/finally`. A pre-cancelled token invokes that callback immediately. If its `backend.Reserve` throws, registration unwinds before `active.Remove`; the source is disposed but the active entry remains. Controlled backend probes reproduced this for build and run, including `Busy=true` after a later successful reservation.

**Correction:** put registration inside the active-operation cleanup lifetime and define how failed revocation is reported. Actual Composer reservation can advance its generation before persistence throws; do not assume an exception means no state changed. Add pre-cancelled build/run cases with one reservation failure, requiring cleared activity and usable subsequent operations. This concerns the public session API; current wire calls supply `CancellationToken.None`.

A rejected review hypothesis is worth preserving: on the probed runtime, disposed `CancelAsync()` returns a faulted task, rather than throwing synchronously. Later reserve/close did succeed. The demonstrated defect is the stranded activity entry, not a proven cascade of synchronous cancellation failures.

### R3 — Medium priority: established-session errors lose their authority identity

**Location:** [Program.fs](../Bozzetto.Composer/Program.fs), lines 123–147.

Sending `run` with `arguments:[1]` to accepted session B, generation 1, produced `request_refused` with **session `""`, generation 0**. The outer exception handler always uses host identity, even after resolving a valid session. Backend exceptions reaching that handler have the same problem. Request correlation remains intact, but the advertised established-session response contract does not.

**Correction:** preserve the resolved session/operation authority when producing a refusal. Keep host identity for requests that have not established a valid session. Add malformed-argument and injected-backend-exception wire tests that assert the complete response identity.

### R4 — Medium priority before interactive integration: synchronous work blocks supervision

**Locations:** [ProviderSession.fs](../Bozzetto.Composer/ProviderSession.fs), lines 55–73, 105–118 and 136–143; Composer `IncrementalBuild.fs`, lines 66–82, 179–205 and 220–250.

Four controlled barriers demonstrate that status, reserve, cancel and close cannot finish while `RunCurrentAsync` is blocked before returning its task. Actual Composer performs executable hashing, input recapture and process launch in that synchronous prefix; acceptance and status persistence also hold its gate during filesystem work, including `Flush(true)`. The existing tests suspend after selection/first await and therefore miss this window.

This establishes a blocking window, not a measured real-project latency budget or a deadlock. Current Composer does satisfy the adapter's synchronous launch-order assumption. **Do not simply move run outside the lock:** that could select a newer artifact for an older request.

Add pre-first-await and acceptance-gate coverage, specify the admitted responsiveness bound, and address long work at the owning scheduling/Composer boundary before advertising responsive editor cancellation. Keep artifact selection and reservation correctly ordered. Document that timing dependency in the backend contract so a future early await cannot silently invalidate it.

## Release and coverage follow-through

The MCP dependency cleanup retains official 1.0.0-rc.1 package resolution; inspected lockfile package rows and content hashes match the preserved predecessor. No second MCP state model was introduced. The remaining Harmony dependency is disclosed, as are distribution validation and cache-retention follow-ups.

One operational issue remains from the identity inventory: the checkout is on `main`, while [pre-push](../scripts/pre-push) line 30 gates only `master`, [main.yml](../.github/workflows/main.yml) line 7 schedules push CI for `master`, and [ship](../scripts/ship) line 35 pushes there. Resolve branch/release policy before publishing. This is not evidence that the recorded local gates were skipped.

The `composer` pipeline is deliberately opt-in; ordinary CI/local-gate does not establish native provider acceptance. Full host/browser/mutation tiers, editor journeys and simultaneous F#/Clef operation through shared handlers remain unproven, as the checkpoint states. A distribution descriptor and native/tool closure validation remain explicit follow-ups, not demonstrated failures of the supplied closure.

## Recommended next checkpoint

First fix R1–R3, add their discriminating regressions, and settle the R4 contract. Rerun the unfiltered default and dedicated Composer gates against the revised worker, recording exact source/closure identities. Keep the existing native receipt controls.

Then connect these same operations to Bozzetto's shared provider supervision and explicit MCP handlers. The useful compiler-development acceptance loop is: open a copied `04d` or `04e` project, reserve before edit, build, inspect refusal/current metadata, run through Composer, and retire/reopen around a compiler replacement. Exercise two independently compiling projects plus an F# session through those handlers. That would let the compiler agent use `boz` routinely without bypassing compiler authority. Live-state HMR and in-memory compiler patching need not enter this next slice.

## Evidence location

The implementation checkpoint remains at `~/.local/state/bozzetto/checkpoints/2026-09-30-provider/`. Audit scripts, exact wire responses, object/source/closure verification and independent-review dispositions are retained at `~/.local/state/bozzetto/checkpoints/2026-09-30-provider-audit/`; original exercise outputs are under `~/.cache/bozzetto/audit/2026-09-30-provider/`.

The main native reproducer is `worker-fault-probe.fsx`; its `worker-fault-1d9f0d9a6dfe4c8e8d3c6471ed0596ad/summary.json` records the partial retirement, successful post-failure native run, retained lease and lost refusal identity. `session-probes/probe.fsx` and `probe.log` hold the cancellation and blocking cases. These are bounded audit reproducers, not replacements for unfiltered acceptance. Source review was split across local reviewers and bounded LAN worker reviews; only findings checked against source or execution are included here. Raw logs remain outside the repository.
