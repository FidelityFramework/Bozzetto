# Differential compilation through Alex and the LLVM back end: where the boundary holds and where it collapses — 2026-10-11

Owner direction (2026-10-11): differential compilation must stay visible in Bozzetto and be carried through the Alex middle
end and the LLVM back end. This record is the grounded map behind that work. It was produced by five read-only readers
(Composer, Alex, LLVM/link, spec and design records, Bozzetto's Composer surface) and one synthesis, all at the heads named
below; the per-stage reports with every `path:line` are in the evidence root under `increment-2026-10-10/differential/`
(`reports.json`). Nothing was executed; `[inf]` marks inference. Terminology: Composer is a differential compiler;
"incremental" is reserved for Fidelity.FSharp.Incremental.


**Headline.** The per-region path already exists end to end: Baker publishes `WitnessSegmentation`, Alex retains unchanged regions, Composer's LLVM back end lowers and links per region by content key, and the Bozzetto worker runs that path. The leverage is in Baker's narrow cohort and the always-fresh `Common` region, not in Alex or LLVM. Evidence: code read at Composer 0caa67d, Alex 99409b5, Fidelity.PSG d76b5a3, clef b0ef172, Bozzetto 1fac8ad9 plus the uncommitted tree; nothing executed; retrieval not used (outside-index exception). `[inf]` marks inference.

## 1. Pipeline today (worker path, `ProjectSession`)

| Stage | Unit | Reused on a small edit | Evidence |
|---|---|---|---|
| CCS/Baker check, segmentation | whole project | nothing | clef ProjectWorkspace.fs:59-64,122; WitnessEmission.fs:69,176 |
| Publish, source proof | whole revision | nothing | Composer CompilationOrchestrator.fs:568-569 |
| Alex `generateSelective` | `WitnessRegion`: one `ScalarCallable` per capture-free scalar leaf function, one `Common` | held `MLIROp` if fingerprint, `Dependencies`, coeffects equal; `Common` always re-witnessed | Alex SelectiveTraversal.fs:185-212; Fidelity.PSG Codata.fs:890-911 |
| MLIR proof discharge | whole module | nothing | Composer Types/Pipeline.fs:167-184 |
| LLVM per region (mlir-opt, mlir-translate, llc) | region | object if key(recipe, identity, fingerprint, content hash, text hash) equal and .o/.bc intact; `Common` recompiles after any fresh check | IncrementalRegions.fs:103-148; clef WitnessSegmentation.fs:411-417 |
| llvm-link, ld.lld, ELF rocq proof | whole program | nothing | IncrementalRegions.fs:149-168; ArtifactProof.fs:774-779 |
| `composer compile`, regression runner | whole program, stateless | nothing | CLI/Program.fs:126-127; RunnerCore.fsx:238-241; BackEnd/LLVM/Pipeline.fs:13-58 |
| Bozzetto wire, page | `AcceptedArtifact` identity arrays, `WitnessVisits` | shown as counts only | Bozzetto ProviderData.fs:27-40; UiBridge.fs:70-78; web Model.fs:465; App.fs:412 (working-tree lines) |

## 2. Where the boundary collapses to whole-program

1. **Cohort edge.** `supported` admits literals, member `VarRef`s, immutable bindings, `Sequential`/`IfThenElse`, admitted IEEE comparisons. Any call, capture, string, storage, real arithmetic or FFI leaf puts the function in `Common`; scalar `Dependencies = Set.empty`, so Alex's `dependentClosure` is latent (clef WitnessSegmentation.fs:140-151, 260-290, 390; SelectiveTraversal.fs:194-198).
2. **Branch observations.** Any `Callable.Branches.Observations` yields zero scalar regions (WitnessSegmentation.fs:397-399; IncrementalBuildTests.fs:658-673).
3. **`Common` churns by construction.** Its fingerprint hashes every node including `Id`; MLIR value names embed NodeIds; the comment states "Common remains freshly witnessed" (WitnessSegmentation.fs:411-417; Alex Values.fs:16-18; Serialize.fs:101). D5 stable identity is "Not built" (Alex docs/02_Revision_Contract.md:26-27).
4. **Whole-program guards.** A coeffect change drops the prior (SelectiveTraversal.fs:185); proof runs whole every generation (how-it-works.md:31-37).
5. **Lifetime.** Both states are mutable fields on `IncrementalBuild.Session` (84-86, 308-310). Each `Open` creates a fresh sessionId, backend and `ProjectSession` (Bozzetto Worker.fs:136-149; ComposerAdapter.fs:26); host and epoch are per worker process (Worker.fs:31); the supervisor reuses a live worker (ComposerSupervisor.fs:139-140). Reuse is therefore visible only across builds inside one open session; re-open, retirement or restart is cold. Nothing reads `accepted.json` back `[inf from absence]`.
6. **Visibility.** `ShowTiming = false` on the worker path (ComposerAdapter.fs:25); the bridge keeps `.Length` only (UiBridge.fs:75-78).

## 3. Seams, by leverage

| # | Seam | Owner | Publishes or keys | Governing clause |
|---|---|---|---|---|
| 1 | Admit ordinary scalar calls between module functions; emit non-empty `Dependencies` | clef Baker `WitnessSegmentation` | region identity, fingerprint, callee set | Alex AGENTS.md:5-8; SelectiveTraversal.fs:1-2; clef Types.fs:3080-3100. Downstream is ready: `scalarBody` accepts `func.call` (SelectiveTraversal.fs:108); `partition` emits typed imports (IncrementalRegions.fs:58-62, 88-96) |
| 2 | Widen `retainable`/`scalarBody` in step; bump segmentation `Version` | Alex | none | SelectiveTraversal.fs:105-120, 75-101 |
| 3 | End `Common` churn: D5 stable node identity, or split `Common` into published regions | clef | stable identities or more regions | Alex docs/03_Node_Identity.md:44-53; 04_Debt.md:13,17 |
| 4 | Show region identities | Bozzetto daemon, web | per-region rows | UiBridge.fs:70-78 |
| 5 | Phase spans on `Accepted` and the wire | Composer, then Protocol | per-stage timings | Timing.fs:23-55; ProviderData.fs:27-40; digest change, paired redeploy |
| 6 | Persist both states across restarts | Composer | content-addressed keys exist | Incremental_Project_Sessions.md:26 excludes it today |

## 4. What the Composer page must show per build

Generation and `sourceVersion`, then one row per region: identity, witness state (changed, retained, retired), `WitnessVisits`, object state (compiled, reused, retired); totals derived from rows. Owner chain: Composer `IncrementalBuild.Accepted` owns the fact (IncrementalBuild.fs:289-307); `ComposerAdapter` maps it whole (7-36); daemon `UiBridge.artifactSummary` must carry rows, not lengths; then `UiBridgeCodec`, web `Protocol`, `Codec`, `Model`. Timings wait on seam 5. Push stays `supervisor.Changed` (ComposerSupervisor.fs:301); no Fidelity.FSharp.Incremental input exists in UiBridge or ComposerSupervisor (grep confirmed) `[inf: the "owners publish Incremental inputs" rule is unmet here; flagged, not fixed]`.

## 5. First increment (one to two days, Bozzetto only)

Replace the four `int32` counts (web Protocol.fs:154-157) with region rows across UiBridge.fs:70-78, UiBridgeCodec.fs:212-215, Codec.fs:149-152, Model.fs:305-308,465, App.fs:403-412. The pending working-tree `ProtocolVersion` 3 to 4 bump carries it. No compiler or BAREWire change.

Acceptance: codec positive control (rows round-trip), negative control (a row lacking a state is refused with its reason), one mutation; under a lease, one open session on `Composer/samples/console/FidelityHelloWorld/04d_IncrementalScalarRegions` with three `composer_run_current` builds: cold (3 compiled), unchanged (`callable:…:first` and `second` retained and reused, `common` compiled), edit `second` (only `first` retained), matching 2026-09-30-snapshot.md:7-22; page rows equal `accepted.json` under `provider-sessions/<host>/<session>/<epoch>/generation-N-*/`; one unfiltered Bozzetto.Tests run with TRUST `Trusted`.

## 6. Owner questions

1. Next Baker construct: scalar calls (downstream ready), real arithmetic, or strings (pool is `Common`-only, IncrementalRegions.fs:144)?
2. `Common`: land D5 in clef, or split it in Baker?
3. Persist states across worker restart; who owns the store and eviction?
4. Timings and edit scope on the wire (paired redeploy), or read generation files from the session directory?
5. Does the CLI stay whole-program as the validation reference?
6. MCU as the second selective target (IncrementalBuild.fs:252-253 admits CPU only)?
7. Do the current samples carry `Callable.Branches.Observations`? Unmeasured; that alone zeroes reuse.

## Disposition (primary implementor, 2026-10-11)

- First increment (section 5) is Bozzetto-only and decision-free: region rows on the Composer page, acceptance on the
  `04d_IncrementalScalarRegions` sample under a lease. It lands on bridge protocol 5 (4 shipped the memory-pressure note).
- Section 6's questions 1–6 are the owner's; question 7 (do the samples carry branch observations?) is measured in the
  acceptance run rather than asked.
