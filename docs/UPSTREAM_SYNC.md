# SageFs update: 27 September 2026

Bozzetto integrates SageFs **v0.6.834** while retaining its documentation identity and Fidelity Framework direction. This update does not implement the planned executable/package rename or Clef provider.

## Exact inputs and history

| Input | Commit |
|---|---|
| Bozzetto before integration | `5a0356edef9f219f5b856bdd293705ea9d738218` |
| Original SageFs fork point, 23 February 2026 | `5b685fb5ce3f5a90db595b457dee6d239634ba33` |
| Corresponding commit in rewritten upstream history | `20300df21547d1f4057d24b0a499df68f0147a8e` |
| Updated upstream, 25 September 2026 | `c86c3402460543849e771e527aa75b5892770ea0` (`v0.6.834`) |

The checkout was clean before work began. `upstream` points to `https://github.com/WillEhrendreich/SageFs.git`; its push URL remains `DISABLED`. Bozzetto's origin remains the FidelityFramework repository on forge.spkez.dev.

Upstream rewrote its history: `git merge-base` found no common ancestor. The original and rewritten fork-point snapshots have identical code; their six differences are documentation changes/deletions and removal of the old demo GIF. Bozzetto's three local commits changed only ten Markdown files.

The integration tree was computed with Git's three-way merge using the **original fork snapshot as the explicit base**:

```bash
git merge-tree --write-tree \
  --merge-base=5b685fb5ce3f5a90db595b457dee6d239634ba33 \
  5a0356e c86c3402460543849e771e527aa75b5892770ea0
```

All conflicts were documentation conflicts. A pending two-parent merge was established with `git merge --allow-unrelated-histories --strategy=ours --no-commit upstream/master`, then its entire index/worktree was replaced with that computed three-way tree and the documentation conflicts resolved. The final tree is **not** the result of the `ours` strategy: all executable source, projects, tests, package pins, native libraries, and build scripts match the new upstream snapshot. Both real histories are retained as merge parents; there are no replace refs or fabricated ancestors. Future upstream merges can use the integrated upstream commit as a normal common ancestor.

## Resolution decisions

- Preserved the name explanation, SageFs and Fable.SageFs credits, CCS/PSG semantic authority, Lattice/Atelier/Bozzetto roles, and all six hosting waypoints.
- Kept `SageFs.*`, `sagefs`, editor command/settings IDs, state paths, package IDs, and ports literal and compatible. `Bozzetto`, `boz`, `~/.bozzetto`, and independent ports remain the planned coordinated identity change.
- Updated the main and VS Code READMEs from current upstream behavior. Installation uses the fork's local build; upstream Marketplace/NuGet packages are not described as published Bozzetto releases.
- Retained upstream's current hot-reload limitations, workflow choices, session-scoped interfaces, live testing contracts, and binary session/test persistence. SQLite/DuckDB remain the fork's future storage direction; PostgreSQL is no longer required by the inherited engine.
- Preserved the upstream changelog as labeled upstream history, separate from Bozzetto's unreleased changes. Historical test counts are not local validation claims.
- Followed upstream's move of `IMPROVEMENT_PLAN.md` and `COMPLETED_IMPROVEMENTS.md` to `docs/internal/`, retaining Bozzetto naming and marking their status as historical.
- Preserved the owner's versions of three documents deleted upstream in `docs/internal/fork-history/`: `HOT_RELOAD_STATUS.md`, `ci-cd-plan.md`, and `keymap-architecture.md`. Their bodies are unchanged, with a historical notice prepended.
- Kept inherited technical/reference documents in upstream terminology. Current product interfaces are VS Code, Neovim, dashboard, and MCP; built-in TUI, GUI, and Visual Studio integration are deprecated upstream.

## Validation

Validation uses the SDK pinned by upstream's `global.json`, `11.0.100-rc.1.26425.128`, installed in `/home/hhh/.cache/bozzetto-validation/dotnet`. Existing .NET 10.0.401 SDK and 10.0.12 runtimes are made available within that isolated installation. Xvfb 21.1.24 was extracted into `/home/hhh/.cache/bozzetto-validation/xvfb`, without installing a system package. The system SDK selection and global tool installation were not changed.

The inherited [SageFs development skill](../skills/sagefs/SKILL.md) was read. No SageFs MCP tools were connected and the three-second probe of `http://localhost:37750/api/daemon-info` returned curl exit 7 (connection refused). Validation therefore uses the final .NET gates; no REPL results are claimed.

Command:

```bash
DOTNET_ROOT=/home/hhh/.cache/bozzetto-validation/dotnet \
PATH=/home/hhh/.cache/bozzetto-validation/dotnet:$PATH \
dotnet fsi ci-pipeline.fsx
```

The initial pipeline exited **1**: all build/editor stages passed, but two host shards failed because of missing test prerequisites. The full transcript is `/home/hhh/.cache/bozzetto-validation/pipeline.log`; per-tier logs and original ledgers were created in `/home/hhh/repos/Bozzetto.tiers/` and relocated on 30 September 2026 to `/home/hhh/.cache/bozzetto/tiers/Bozzetto-9c6966335ce4754f/`. The pipeline now places tier artifacts under `${XDG_CACHE_HOME:-$HOME/.cache}/bozzetto/tiers/<checkout-name>-<path-hash>/` automatically, outside project repositories.

| Check | Result |
|---|---|
| Release solution build, including net10.0/net11.0 tool closure | Passed; 0 warnings, 0 errors |
| `dotnet format --verify-no-changes --verbosity minimal` | Passed |
| Integration sample builds | Passed |
| VS Code extension and test-electron host compilation | Passed |
| VS Code golden test and every `sagefs-vscode/tests/*.fsx` contract suite | Passed |
| Default suite | 9,760 passed, 4 ignored; 0 failed/errored; `Trusted` |
| Host shard 1/5, after environment repair | 45 passed; 0 failed/errored/ignored; `Trusted` |
| Host shard 2/5, after environment repair | 46 passed, 4 ignored; 0 failed/errored; `Trusted` |
| Host shard 3/5 | 39 passed, 1 ignored; 0 failed/errored; `Trusted` |
| Host shard 4/5 | 76 passed; 0 failed/errored/ignored; `Trusted` |
| Host shard 5/5 | 39 passed; 0 failed/errored/ignored; `Trusted` |
| `dotnet pack SageFs -c Release -o nupkg --no-build` | Passed; `SageFs.0.6.834.nupkg`, both target frameworks |
| Install from a local-only NuGet config to an isolated tool directory | Passed; no global tool replaced |
| Packaged CLI `--help`, .NET 11 and .NET 10 payloads | Passed; both exited 0 |
| All non-Markdown tracked files vs `upstream/master` | Identical (`git diff --exit-code`) |
| Current documentation whitespace/conflict-marker checks | Passed; archived bodies retain six pre-existing trailing-whitespace lines to preserve the originals |
| Local file links in main/extension README, heritage, and this record | Passed |
| Three archived document bodies vs original Bozzetto HEAD | Byte-for-byte text match |

Final default-plus-host totals: **10,005 passed, 9 ignored, 0 failed, 0 errored**. All six tier rows report `Trusted`. The original pipeline failure remains recorded; the final combined ledger, replacing only the two rerun shards, is `/home/hhh/.cache/bozzetto-validation/final-trust-ledger.jsonl`. Shard registration counts were checked against the initial run before combining results.

The nine ignored cases are not counted as passes: three pending cohort/latency budgets and a method-patcher test in the default suite; three real-sample managed-dependency checks and the net10 keep-tiering case in host shard 2; and the cross-submission record property in host shard 3. The keep-tiering test conditionally skips when the runtime does not recompile the watched entry points. No test definitions were changed for this integration.

The upstream pipeline bootstrapped its MCP SDK fork at `785713abdf8daa956a1df6eccb74e6135465139c`; Harmony came from the committed `SageFs.Harmony.2.4.2-sagefs.1.nupkg`.

### Initial failures and repairs

1. Host shard 1/5 reported `this host has no stable .NET SDK for the real #139 check`. The isolated installation initially contained only the pinned .NET 11 RC SDK. Exposing the existing .NET 10.0.401 SDK corrected this; the entire 45-test shard was rerun successfully.
2. Host shard 2/5 initially reported 14 errors: 13 net10 hot-reload state cases needed a .NET 10 SDK, and the real VS Code command proof needed `Xvfb` on PATH. Both prerequisites were supplied in the isolated setup.
3. The first shard-2 retry then revealed `ELECTRON_RUN_AS_NODE=1` inherited from the invoking editor. Electron tried to execute the sample directory as a Node module and reported `Cannot find module '/home/hhh/repos/Bozzetto/samples/from-csharp/SageFs.Samples.FromCSharp'`. That retry was stopped (exit 137, no acceptance result), and the whole shard was restarted with `env -u ELECTRON_RUN_AS_NODE`. No source or tests were weakened to address these environment failures.

Retries use the same Release assemblies and whole structural shard entry points, not name filters:

```bash
dotnet SageFs.Tests/bin/Release/net11.0/SageFs.Tests.dll --integration-host --shard 1/5 --summary
dotnet SageFs.Tests/bin/Release/net11.0/SageFs.Tests.dll --integration-host --shard 2/5 --summary
```

They retain the pipeline's private checkout and `/tmp` bind mounts, isolated data directories, shared prebuilt FSI-host cache, and disjoint test port ranges. `SAGEFS_SUITE_DURATIONS` points to an absent file to retain the initial run's empty timing history and therefore the **same shard membership**. Retry transcripts/ledgers are `retry1.log`/`retry1.jsonl` and `retry2-clean.log`/`retry2-clean.jsonl` under `/home/hhh/.cache/bozzetto-validation/`. The abandoned Electron retry remains in `retry2.log`.

The `ci`-only mutation-score and four browser tiers were **not run**. Neither were release promotion, Windows/macOS execution, or a GUI application demo. This record does not claim complete CI/release acceptance, Clef/Fidelity F/C acceptance, or implementation of the planned Clef provider.
