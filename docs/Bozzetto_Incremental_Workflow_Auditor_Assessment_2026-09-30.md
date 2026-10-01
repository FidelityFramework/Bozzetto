# Incremental workflow auditor assessment — 2026-09-30

An independent auditor completed a native cold build, unchanged rebuild and one-function edit through the deployed Bozzetto Composer provider. The saved receipts demonstrate a useful incremental development loop now: unchanged callable regions retain their objects, an edited callable is rebuilt, and reservation withdraws run authority before source changes. This advances the earlier [read-only live assessment](Bozzetto_Live_Provider_Auditor_Assessment_2026-09-30.md).

## Scope and identity

The auditor used an owned external `IncrementalScalarRegions` project and session. Build and run operations used actual HTTP MCP requests; the edit reservation used the human-facing HTTP API. First-class tools were unavailable in the auditor's tool catalog. No MCP configuration was changed.

The worker reported Composer SHA-256 `F2B2CEDAD8F753D279BEF5BC6E6D69A9319227D076C27FE9A0108F99EC5F626B`, version `0.0.2.0`, from the deployed release `2026-09-30-7693f54e236e-b269cdb888a6`. Its `inMemoryPatchAllowed` flag was false. This assessment applies to that pinned compiler, not subsequent compiler working-tree repairs.

All three phases used host `70373b83f656434d8488a4f6975182d2`, session `24702e576d03434583a55b123982d8af`, epoch `0d19fc6eabf74004863a770d5c2f9365` and provider `clef-composer`.

## Observed results

| Phase | Generation | Objects compiled / reused | Witness visits: stable / changeable / common | Native stdout | Exit |
|---|---:|---:|---:|---|---:|
| Cold | 1 | 3 / 0 | 3 / 3 / 68 | `stable\nbefore\n` | 0 |
| Unchanged | 2 | 1 / 2 | 0 / 0 / 68 | `stable\nbefore\n` | 0 |
| One-function edit | 3 | 2 / 1 | 0 / 3 / 68 | `stable\nafter\n` | 0 |

All native runs reported empty stderr. The unchanged build retained both callable regions; the common region was rebuilt. After changing only `changeable` from false to true, `stable` still had zero witness visits and retained exactly the same object and bitcode paths and hashes as the cold build. Its object SHA-256 was `0C1B496BE0C3C51CAFE686F62CFCB89B0B306A40589209C1CBE673C0A01075FD`. The saved object-check receipts report successful file-hash checks in each phase.

The cold and unchanged builds shared source version `6D2FBCA82A9542BF3450F796E1A72AB6B81018FE68B5E624D203131BA45DDAB1` and artifact SHA-256 `B645538FAA6512B4A37FFF6AB37D31D204FB2EDBF97508CA5FEF0687DB36A7FA`. The edit produced source version `E2F99D276C7513710AE5D32C8E58E7E72BA05D4C538E7C82C963EB9B10D6DF64` and artifact SHA-256 `DF847F1B7257D7F7BAD88A9640B4B1585B9B490506C111F410C4C7C0DDC7A307`. Each run returned its corresponding build's source version.

## Authority, shared state and cleanup

The critical edit sequence was **successful human HTTP reservation → MCP run refused with `not_accepted` → assertion of that refusal → source write → build → successful MCP run**. The refusal message was “No accepted current artifact; reserve and build first.” This tested withdrawal before any source write, rather than relying on detecting an already-modified file. The saved response timestamp and edited file timestamp corroborate the recorded command order. The earlier unchanged rebuild separately demonstrated retention after reservation and successful rebuilding without a source change.

The human HTTP session projection and MCP status agreed on the owned session's authority and current accepted artifact. Status continued to declare `executionRequiresRevalidation=true`; an object-reuse receipt was not treated as independent run authority.

Closing the owned session advanced it to generation 4. The immediate close response reported cleanup pending; the subsequent status reported `closed=true`, `current=null`, `cleanupPending=false`, `cleanupError=null`, `busy=false` and `revocationPending=false`. The peer session `65b24fea55f94ab090912924ab0adbd1` retained the same authority, current artifact and open state in the before/after snapshots.

## Evidence and remaining acceptance

Raw requests, responses, headers, input hashes, object manifests and checks remain outside the repository at `/home/hhh/.local/state/bozzetto/checkpoints/2026-09-30-composer-auditor-incremental/`. The primary receipts are `summary.json`, the three `*-build.result.json` and `*-run.result.json` pairs, `*-object-manifest.json`, `*-objects.checked`, `edited-reserve.result.json`, `withdrawn-run.result.json`, `edited-status.result.json`, `sessions-edited.json`, `closed-status.result.json` and the peer snapshots. The object and shared-state comparisons were independently checked while preparing this assessment; raw records were not modified. No workflow was rerun solely to write this document.

This is one independently executed native incremental journey, not another execution of the peer's full test gate. It does not establish cancellation races, provider retirement/replacement, ORC live-state reload, full browser rendering, arbitrary-project coverage or proof-result reuse. Witness visits and object counts are observed work counters, not a wall-clock benchmark.

The next compiler repairs require a validated distribution promotion and a repeat against its explicit digest. Broader workflow acceptance should add exact-request cancellation/recovery and coordinated retirement checks while preserving peer ownership. The demonstrated pinned-provider loop is already suitable for the bounded scalar edit/build/run workflow recorded here.

## October 1 promoted distribution repeat

The compiler owner completed that promotion and independently repeated the live
journey. The complete compiler closure and rebuilt worker are installed under
`~/.local/share/bozzetto/releases/2026-10-01-worker-9bd8e40fe2e8-995047939b5f/`.
Composer SHA-256 is `995047939b5f228436e7b9a96409510eb661e8743c5225a369e951899b23f491`;
worker SHA-256 is `9bd8e40fe2e8b9f7a03db5575c8822336fbd030f866b59c2d4c6b1608100b56c`.
Every worker compiler DLL was compared with the sealed distribution. The source
vector, full file manifests and build commands are retained in the receipt below.
PSG schema 11 and worker wire protocol 1 are distinct identities.

Before activation, the new compiler passed native samples 01, 04a, 04d and 04e.
Lazy samples 14a/14b still refused artifact admission; no sample timed out or was
skipped. The rebuilt worker passed the complete Composer provider tier:
**29/29; zero failures, errors or ignores; Trusted**. This tier included isolated
native and shared-interface tests; the default suite was not rerun for promotion.

The daemon captures its worker path at startup. Successful HTTP retirement first
withdrew the old Composer epoch and completed cleanup; a graceful shutdown and
restart then selected the new immutable path. PID **3012680**, started at
`2026-10-01T10:12:57.4099824Z`, is healthy on the same ports and external workspace.
The CLI release `2026-10-01-cli-67d11d379c82` and runtime remain selected. Old
Composer session authority was intentionally retired, including the earlier
peer demo session. There were no F# sessions. MCP clients need to reconnect.

The fresh live audit used host `5b784f050a67466bbbfbc054c34cdabe`, epoch
`bea9fa6e83f149e481800f8e986a4ffb`, session `646eb415bdbe44519a3242fe7ba28ab3`.
Cold/unchanged/edited generations reproduced **3/0 → 1/2 → 2/1 compiled/reused
objects** and the outputs in the table above. Human HTTP reservation succeeded;
MCP run then returned `not_accepted`; source hashes were checked unchanged;
only then was the single source line edited. The stable callable had zero visits
after the edit and retained identical object/bitcode paths and bytes. Its object
hash remained `0C1B496BE0C3C51CAFE686F62CFCB89B0B306A40589209C1CBE673C0A01075FD`.

MCP status, its resource and the human HTTP projection agreed. Each generation
had a distinct proof invocation with **43 source + 43 MLIR** successful solver
checks and current artifact/Rocq evidence; retained code did not bypass proofs.
Owned cleanup finished with no pending work/error and no current artifact.
No peer session existed during this repeat, so it does not add another
peer-preservation test. The preceding provider tier separately exercised shared
sessions and retirement controls.

Evidence is external at
`~/.local/state/bozzetto/checkpoints/2026-10-01-compiler-promotion/`:
`deployment.json`, `provider-trust.jsonl`, `activation/`, and `live-replay/`.
Replay, offline verification and lease release each exited 0. These checks
establish the promoted bounded scalar workflow, not native Lazy/Result transport,
arbitrary language coverage, selective proof reuse, ORC or a performance gain.
