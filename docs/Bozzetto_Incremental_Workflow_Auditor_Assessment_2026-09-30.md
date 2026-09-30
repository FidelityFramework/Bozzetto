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
