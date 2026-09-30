# Live provider auditor assessment — 2026-09-30

The [shared provider checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md) is supported by its recorded **29/29 Composer integration** and **9,801 passed plus four ignored default** results. Fresh independent read-only observations also reached the deployed daemon and its Composer state through HTTP MCP. This is a limited assessment of evidence, deployment visibility and guidance; the auditor has **not independently repeated the native workflow**.

## Scope and source identity

Repository instructions and the Bozzetto skill were read. The reviewed Bozzetto HEAD is `5e37875ae41aedcdb104aa382985fc52686e39dd`; the separate Harmony checkout is `3d3da555f6372ec92cb45b4cba142b70e553b433`. Before this assessment was added, all **1,711 entries** in the checkpoint's `source-checkpoint/working-source.sha256` matched the current working files. That snapshot includes the five modified guidance/documents and untracked `scripts/start-shared-daemon`; those files are not represented by HEAD alone.

Recorded gate evidence lives at `/home/hhh/.local/state/bozzetto/checkpoints/2026-09-30-live-provider/`. Its `deployment.json` retains base source HEAD `1bb1e7c8898c80c6c160e775923490ce2ba6cd8a`; the final default runner was rebuilt at `5e37875…`. The preserved source snapshot, patches and binary manifests distinguish these stages. This assessment does not relabel the earlier deployed binary as a clean build of the later commit.

| Recorded gate, independently inspected | Result and identity |
|---|---|
| `attempt2/composer.log` and trust ledger | 29 registered/ran/passed; no ignores, failures or errors; **Trusted**. This comprises 20 authority cases, four worker-lifetime cases, four process/native cases and one shared-interface journey—not 29 native executions. |
| `default-repaired/default.log` and trust ledger | 9,805 registered/accounted; 9,801 passed; four existing ignores; no failures/errors; **Trusted**. The recorded command is unfiltered, with `--summary --parallel-workers 4`. |
| Distinct frozen runners | Native `Bozzetto.Tests.dll` SHA-256 `b16bb8202d05c5c3a5467bd4d20164b6bdf77629032f66a6612fb981e6ccedb9`; final default `9023284b7ac3c033cb03cf365f06439718575acede585c5ce8d6e6ec7e82e60a`. Both files matched their copied manifests. Closure-comparison receipts report unchanged production assemblies through the test-only rebuild. |

The four ignores are three existing performance-budget cases and `method patcher tests.after patch using Harmony`. Earlier failing runs and the known stochastic health-property limitation remain disclosed. No test suite was rerun for this assessment, and the auditor did not rehash every large dependency file.

For precise manifest lookup, the documented native hash `f4da6c…` names `attempt2/before/test-runner.sha256` and its identical `after` manifest. The relative copied-closure manifest instead hashes to `c67073854bb906d0f4749d2b891f3c7798116b0013968f55c6de77d7c45775f7`. The final default relative manifest matches the documented `33ef0d…`. This is a path-naming distinction, not observed binary drift.

## Fresh live observations

Independent receipts are retained at `/home/hhh/.local/state/bozzetto/checkpoints/2026-09-30-auditor-mcp/checkpoint2/`. `boz status` exited zero and reported PID **669866**, version **0.6.834.0**, start time **2026-09-30T15:05:00.7197464Z**, and working directory `/home/hhh/.local/share/bozzetto/workspace`.

HTTP identity and health observations agreed on the daemon. Health reported `healthy=true`, empty component failures and anomalies, but `overall=Degraded` with `memoryPressure=tight`. This is consistent with the documented memory policy, rather than evidence of a component failure. The source samples `MemAvailable` and rearms its sweep five seconds after completion; the 20%/30% tight hysteresis is unchanged. No forced pressure-transition test was performed.

First-class Bozzetto tools were unavailable in the auditor's active tool catalog. The auditor therefore made explicit read-only **HTTP MCP** calls, without changing client configuration: `get_daemon_status`, `composer_list_sessions`, and `resources/read` for `composer://sessions` all succeeded. These were actual MCP requests, not substitutes inferred from an ordinary health response.

The HTTP session endpoint, MCP tool and MCP resource reported the same existing peer-owned session `bdb2233db4244816aa1e5a9080e1804f`, host `2e423ebc93df40de8abaa886d0979f37`, epoch `5cf5024f134b4c4dbe2cb246eb646ac4`, generation **1**. Its current artifact digest was `B645538FAA6512B4A37FFF6AB37D31D204FB2EDBF97508CA5FEF0687DB36A7FA`; `executionRequiresRevalidation=true` remained explicit. The worker reported the approved Composer digest `F2B2CEDAD8F753D279BEF5BC6E6D69A9319227D076C27FE9A0108F99EC5F626B`. These observations establish shared visible identity, not fresh execution authority or a new native run.

## Actionable feedback

1. **Label session counts by provider.** [`Program.fs:405–408`](../Bozzetto/Program.fs) prints `Sessions: 0 active` while the Composer projection contains one open accepted session. Health similarly says “No sessions registered.” The checkpoint already explains that these counters describe inherited F# sessions; the user-facing responses should say so or show separate F# and Composer counts. Test the zero-F#/one-Composer case across CLI, health and MCP status.
2. **Prefer the current MCP endpoint in status output.** The same CLI status lists only `/sse`; the main help already documents `/` as Streamable HTTP and `/sse` as older-client compatibility (`Program.fs:519–520`). Align status with that guidance. This review did not establish that the compatibility endpoint is broken.
3. **Describe launcher guarantees narrowly.** The new helper preserves occupied listeners and refuses direct workspace/log/PID symlinks. Its existing-listener path checks ports and parseable identity, not health/Composer readiness (`scripts/start-shared-daemon:43–50`). Its 20-second loop deadline can also be exceeded by requests already in progress (`:68–82`). Keep the separate bounded health/session checks; do not describe preserve-existing success as a complete readiness check or the loop deadline as a strict wall-clock cap. The helper is Python and was only inspected here, never executed by the auditor.

## Conditions for the next workflow acceptance

Use an auditor-owned external fixture and session, preserving the peer's session. Record one connected-client/browser journey through open, reserve-before-edit, build and `composer_run_current`, with matching authority and current source/artifact hashes. Then demonstrate human invalidation, exact-request cancellation, recovery and stale-result refusal on that owned session; compare the shared views and clean it up. Retirement/replacement requires coordinated ownership because it affects other sessions.

None of those mutating operations—open, reserve, build, run, cancel, close or retire—was performed by the auditor in this checkpoint. The peer's recorded 29-case gate substantiates its claimed execution; it is not claimed as the auditor's own repeat. Standalone MCP packaging, editor save/build integration, progress streaming, ORC live-state reload and removal of .NET remain separate work. Existing peer files, services and sessions were preserved.
