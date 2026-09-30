# Live provider audit response — 2026-09-30

Status: all three findings addressed, full gates passed, corrected deployment active.

The [auditor assessment](Bozzetto_Live_Provider_Auditor_Assessment_2026-09-30.md)
independently corroborated the recorded gate results and read-only HTTP/MCP
session identity. It did not repeat the mutating native workflow. This response
addresses its three actionable findings without changing compiler acceptance,
retirement, memory thresholds or the approved compiler distribution.

## Corrections

1. Existing F# session counts are explicitly identified as F# in CLI output,
   health and MCP daemon status. They are not presented as a total for every
   provider. Composer sessions remain available through their shared tool,
   resource and HTTP projection. Regression coverage exercises zero F# sessions
   alongside one open Composer session in the existing live journey.
2. CLI status lists the root Streamable HTTP MCP URL first. `/sse` is labeled
   as older-client compatibility; the audit did not establish that it was broken.
3. The startup helper's existing-listener path reports `readinessChecked=false`
   and supplies the two separate health/Composer URLs to probe. It preserves the
   listener and does not claim readiness from identity alone. The documented
   20-second loop deadline is not a strict wall-clock cap: requests already
   underway have individual three-second timeouts. Workspace/log/PID symlink
   refusals and external placement remain intact.

The original checkpoint now names the native absolute-path and copied relative
closure manifests explicitly, correcting the lookup ambiguity without changing
any recorded hashes or binaries. The auditor's original assessment and evidence
remain intact.

## Acceptance and deployment

The source-level CLI/health checks passed **28/28**, and the updated live journey
passed strict source typechecking. This is preparatory evidence, not the final
compiled acceptance gate. The helper was executed against the existing daemon:
it preserved PID 669866 and reported `readinessChecked=false`; separate bounded
health and Composer probes succeeded.

The full Release solution build passed with zero warnings/errors. The unfiltered
default suite accounted for **9,806 cases: 9,802 passed, four existing ignores,
zero failures/errors; Trusted**. The additional case covers the provider-specific
CLI zero and current MCP URL. The whole Composer tier passed **29/29**, with no ignores, failures or errors;
**Trusted**. Its updated live journey proves the zero-F#/one-accepted-Composer
case through CLI, health and MCP, alongside the existing native workflow. Each
gate used a separate granted MCP work lease, released afterward; both suites
used the same frozen runner. New evidence belongs under
`/home/hhh/.local/state/bozzetto/checkpoints/2026-09-30-live-provider-audit-response/`;
original checkpoint evidence is preserved separately. Neither the original
source snapshot nor its runner identities are relabeled as this correction.

The active daemon closure is
`/home/hhh/.local/share/bozzetto/releases/2026-09-30-7693f54e236e-b269cdb888a6`.
The compiler worker and approved Composer DLL are unchanged.

| Component | SHA256 |
|---|---|
| Corrected daemon | `7693f54e236e03ef9b142d50f33c9eb799ddfc6bfbc85ca9ca9c6c4535121b9e` |
| Compiler worker | `b269cdb888a628b64be4465742adb816cb97f28e84c253e4f89220c00145a056` |
| Approved Composer | `f2b2cedad8f753d279bef5bc6e6d69a9319227d076c27fe9a0108f99ec5f626b` |

`deployment.json`, the release's `closure.sha256`, and the gate's full dependency
manifests retain the exact identities. This is a build of HEAD `5e37875ae41aedcdb104aa382985fc52686e39dd`
plus the recorded correction patch, not a clean-HEAD claim.
The shared frozen test-runner manifest `gates/test-runner-closure.sha256` hashes
to `c339148c544bb839f5891f67c952cca71f4ed3a3728d0fef9a120b782b7855af`.
All five gate closure manifests stayed unchanged through both suites; every one
of the staged daemon's 388 files matches the executed gate's manifest. The owned
native/live artifacts, eight provider trees and two worker logs are retained;
`gates/owned-evidence.sha256` covers 693 archived evidence files.

After gates and lease release, the original owned daemon and idle demonstration
session were replaced. No other session existed. Both old daemon PID 669866 and
its worker PID 673154 exited. The installed `boz` now runs PID **765938**, started
**2026-09-30T15:50:39.2999387Z**, from the same dedicated external workspace.
Endpoints remain **47749 MCP/Composer** and **47750 dashboard**. Reconnect any MCP
client bound to the former daemon; old session/epoch handles are obsolete.

A fresh Chromium browser journey against the installed replacement completed
open, reserve, native build and run: output `stable` / `before`, exit zero.
The accepted external demo remains open: session
`65b24fea55f94ab090912924ab0adbd1`, host
`70373b83f656434d8488a4f6975182d2`, epoch
`0d19fc6eabf74004863a770d5c2f9365`, generation 1. Its accepted artifact digest is
`B645538FAA6512B4A37FFF6AB37D31D204FB2EDBF97508CA5FEF0687DB36A7FA`.
The installed CLI and HTTP health show the corrected F# labels, while the
Composer projection retains the accepted session. Browser receipts/screenshot
are under `browser/`; there were no JavaScript errors and only a favicon 404.

Final health reports `healthy=true`, no component failures, and `tight` memory
pressure as the sole reason for `overall=Degraded`. Startup's initial `normal`
reading preceded the first memory sweep and is not evidence of recovery. Memory
thresholds and the nominal five-second refresh policy are unchanged. Full host,
browser-matrix, mutation and upstream Harmony suites were not rerun; the explicit
browser journey and native tier above are the executed acceptance scope.

`source-checkpoint/` preserves the final HEAD archive, tracked patch, untracked
source files, full source hashes and status inventory. The auditor's original
assessment is included unchanged. Earlier failed runs and the baseline health
property reproducer remain in the original checkpoint, not erased by these
passing runs.

## Compiler and next audit boundary

The Composer agent's sequence repair and array-copy source-contract repair are
separate compiler work. The currently approved Composer DLL remains SHA256
`f2b2cedad8f753d279bef5bc6e6d69a9319227d076c27fe9a0108f99ec5f626b`.
Promoting newer compiler changes requires the compiler owner's validated
immutable distribution and a new recorded native gate, followed by worker
retirement/replacement. A source update alone does not update the running worker.

Compiler-owner follow-up, reported after this deployment: Baker now refuses a
storage capture whose source identity is unknown, with a regression showing that
the identity check alone still succeeds. This report is not evidence that the
pinned compiler above includes that repair. The [development plan](Clef_Composer_Development_Plan.md)
records the resulting boundary: borrow records support full source revalidation;
selective proof reuse additionally requires explicit dependencies for complete-use
checks and invalidation coverage. This documentation follow-up postdates the
deployment's preserved source snapshot; it does not revise its gate identities.

The next independent native acceptance should use the auditor's own external
fixture and session, preserving other sessions. Exercise open, reserve before
editing, build, run, human invalidation, exact-request cancellation, recovery and
stale-result refusal through the connected client/browser views. Close the owned
session afterward. Coordinate whole-worker retirement because it affects all
sessions. The recorded implementation gates do not substitute for that independent
repeat. Standalone MCP packaging, editor integration, ORC and removal of .NET
remain separate milestones.
