# Harmony and provider repair follow-up — September 30, 2026

The Harmony ownership migration is sound in the reviewed checkpoint, with two
import-tooling corrections requested below. The provider repairs address the
previous R1–R4 findings in source review. Their revised native gate and the live
shared MCP/human-interface workflow remain separate, unfinished acceptance steps.
This assessment does not approve those workflows from process-test evidence.

Reviewed inputs are the [Harmony checkpoint](Bozzetto_Harmony_Checkpoint_2026-09-30.md),
[provider repair checkpoint](Bozzetto_Provider_Repair_Checkpoint_2026-09-30.md),
their source and external evidence. The original
[provider assessment](Bozzetto_Provider_Assessment_2026-09-30.md) remains intact.

## H1 — Preserve package provenance on identical-byte reimport (P2)

`build/HarmonyPackage.fs:116` reads the selected checkout's current HEAD.
`inspectPackage` checks package/version and CLR identities, but does not bind
that HEAD to the package's source commit. Lines 134–143 allow an identical-byte
reimport; lines 144–165 then replace the manifest's source commit, dirty state
and submodule record with the current checkout's values. `validate` at lines
73–87 does not inspect source provenance. The direct import entry point is
`scripts/harmony-package.fsx:8`.

Consequently, reimporting the existing package built at `c52cd5a` while selecting
the now-advanced clean fork at `3d3da55` would preserve the package bytes but
rewrite their claimed build origin. This follows from the reviewed code; no
mutating reproduction was run. The current vendored package manifest and its
embedded nuspec correctly identify `c52cd5a`: **no corruption of the current
artifact was found**.

Keep established provenance unchanged on an exact-byte reimport, or refuse an
inconsistent source selection. A first import should bind its source attribution
to package/build evidence, with dirty-source attribution explicit. Add a
regression using the same package after the selected checkout advances: the
original attribution must survive or the import must refuse. Also reject a new
package whose embedded/build source identity disagrees with the asserted clean
source. Retain the existing immutable-version checks.

## H2 — Canonicalize the consumer checkout before excluding scratch (P3)

`scripts/update-harmony:11` uses logical `pwd` for the consumer checkout, while
line 22 canonicalizes the cache with `realpath`. Invoking the script through a
checkout-directory symlink with a physical in-checkout `XDG_CACHE_HOME` makes
line 24 compare unlike paths. It can then create scratch inside the checkout.
Normalize the consumer root with `pwd -P` or `realpath` before this guard.
Test the symlinked invocation with a harmless pack stub and require refusal
before either scratch creation or child invocation. This is a static path-guard
finding; no reproduction was executed and no current evidence run is alleged
to have written misplaced scratch. The selected Harmony-fork guard already
compares canonical paths.

## Provider repairs and Composer handoff

Source review found no new concrete regression in the revised R1–R4 paths:

- Worker retirement fences every session before independent cleanup and retains
  failed cleanup from an open overtaken by retirement.
- Cancellation registration is inside operation removal's `finally`; callbacks
  withdraw logical authority without performing fallible compiler reservation
  inline.
- Resolved-session refusals retain authority, and reservation failures retain
  the attempted revision even when the compiler advanced before throwing.
- A separate invocation gate orders compiler prefixes and successful edit
  permission. Status reads cached authority; logical cancellation and closure
  do not wait on compiler I/O under that authority lock.

The overtaken-open cleanup path still deserves a discriminating concurrent
open/retirement test; the supplied evidence establishes source-load/handshake,
not that race's execution. The run-prefix ordering assumption remains documented
and must be revisited if Composer moves selection/launch behind an early await.

Composer's requested lease correction is complete. Its
[response](/home/hhh/repos/Composer/docs/Bozzetto_Lease_Release_Response_2026-09-30.md)
identifies implementation commit `2688fba1206c82a9b24d2221a754eaf6d2c5bac5`,
11/11 focused passes, all four new cases passing in the full suite, the exact
validated distribution and its complete 40-file hash manifest. Rebuild the
worker against that selected distribution and run the entire registered
`--integration-composer` tier: 20 unit plus four process/native cases. Preserve
the new worker, test-runner and dependency hashes, actual wire responses, object
reuse and lease-acquisition evidence. A passing tier does not by itself finish
shared MCP, dashboard or editor acceptance.

## Evidence checked and limits

The 1,700-file provider source manifest matches the current checkout; the
41-file Harmony manifest also matches. The retained default-suite log reports
9,784 accounted, 9,780 passed, four existing ignores, zero failures/errors and
`Trusted`; all 20 provider unit cases appear. The prior low-memory failures stay
recorded separately. No guard was changed and no service was stopped for this
review.

The current Harmony package and manifest retain the recorded hash and build
origin. The host-manifest correction guards the outer multi-target build;
framework manifests belong under their concrete output paths, without a new
ignore rule concealing misplaced output. The recorded patch/unpatch probes and
dependency-lock comparisons support the bounded ownership migration.

The supplied source manifests do not pin `Bozzetto.Tests.dll` and its complete
executed dependency closure. The Harmony consumer-closure record pins Harmony
DLLs, not that whole test closure. Thus this review corroborates the saved test
log and matching source; it does not assert that any subsequently selected test
binary is the exact one that produced it. Capture those hashes with the next gate.

No Bozzetto MCP tools were exposed in this session; an HTTP probe of local port
47750 refused connection. After the user's access clarification, executable
fallback checks were held. This is a static follow-up plus inspection of retained
evidence, not a fresh Bozzetto runtime or native acceptance run. The peer is
finishing the shared interfaces so they can be exercised directly.

Audit receipts are outside the repositories under
`/home/hhh/.local/state/bozzetto/checkpoints/2026-09-30-followup-audit/` and
`/home/hhh/.codex/work/clef-2026-09-30-lazy/provider-repair-review/`.
No peer implementation files were changed by this assessment.
