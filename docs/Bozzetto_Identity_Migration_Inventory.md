# Bozzetto identity migration checkpoint

Checkpoint: 30 September 2026. The hard fork now has its own product identity. This is an audit record of the source migration, not a claim that publication or every runtime gate has passed.

## Implemented names

| Surface | Bozzetto identity |
|---|---|
| Package / namespaces / assemblies | `Bozzetto`, `Bozzetto.*` |
| CLI executable / .NET tool command | `boz` |
| Solution / project directories | `Bozzetto.slnx`, `Bozzetto*` |
| VS Code extension / command IDs | `bozzetto-vscode`, `bozzetto.*` |
| VS Code publisher metadata | `fidelityframework` (publication ownership not validated) |
| Environment variables / process and IPC identities | `BOZZETTO_*` / `Bozzetto` or `bozzetto` as appropriate |
| State / per-project config | `~/.bozzetto`, `.bozzetto/config.fsx` |
| Startup profile alternatives | `.bozzetto/init.fsx`, `.Bozzettorc` |
| MCP / dashboard defaults | `47749` / `47750` |
| Binary persistence filenames | `.bozzettofm`, `.bozzettotc`; legacy replay suffix `.bozzetto` |
| Agent skill | `skills/bozzetto/SKILL.md` |

The migration changed namespaces, source/project/solution paths, embedded resource names, include paths, snapshot filenames and contents, editor configuration, scripts, documentation and tests together. CLI launch sites use `boz`; package installation uses `Bozzetto`. Source-only lower-camel identifiers use `bozzetto`. The binary manifest/cache layouts and version fields were not changed merely to rename their filename suffixes. No state import or SageFs compatibility layer was added. Existing SageFs state and installed tools were not modified. Test-specific dynamic port allocation was preserved.

Of 1,681 original tracked paths, the migration checkpoint has 1,617 changed paths, including 1,427 path renames and 1,502 text changes. These counts include the root agent's concurrent additions to released integration files, and exclude new untracked provider source and this checkpoint. `SageFs.Demos/Fingerprint.fs` is valid UTF-8 F# containing a literal NUL; it was migrated as source, not excluded as binary.

## Retained identities and dependency boundaries

There are two different reasons for retaining an upstream name:

- **Provenance and historical evidence.** Legal attribution, upstream repository/issue/immutable source URLs, `UPSTREAM_HERITAGE.md`, historical changelogs and `docs/UPSTREAM_SYNC.md` retain truthful SageFs names. `Fable.SageFs` remains the name of Shayan Habibi's separate project. These references do not impose runtime dependencies.
- **Actual external contracts.** `SageFs.Harmony` version `2.4.2-sagefs.1` remains a real package dependency. Its NuGet filename, lockfile identity and source-build property `SageFsBuild` must remain accurate until that dependency is replaced or deliberately repackaged. This is a remaining runtime dependency, not merely attribution or an unavoidable long-term requirement.

The current F# engine uses FSharp.Compiler.Service / FSI and Harmony-backed managed method patching. Those are part of the existing F# execution path. The CLI, daemon, dashboard and MCP server also currently run on .NET; renaming them does not remove their runtime hosting dependency. The separate Composer worker is likewise a .NET host today. Moving the Clef execution path toward accepted native artifacts and eventually self-hosted Clef must keep the provider boundary explicit so FCS/FSI/Harmony do not become requirements for a future native provider. Dependency removal, host replacement and storage changes require separate implementation and acceptance evidence; no new dependencies were added by this migration.

`sagefs.nvim` is a separate upstream plugin, absent from this repository. Its real Lua module (`require('sagefs')`), commands (`:SageFs…`) and repository paths remain truthful external contracts. The retained demo adapter explicitly passes Bozzetto's MCP/dashboard ports. This is not a second Bozzetto plugin or a compatibility alias inside the daemon. A future independent plugin must be implemented before new Lua module/command names can be advertised. The workflow step that wrote version tags to the upstream plugin repository was removed entirely.

## Preservation and validation

The source migration saved all original tracked contents, existing dirty diff/status and the complete rename map outside the repository at:

`~/.cache/bozzetto/validation/identity-migration/`

The directory includes `tracked-before.tar`, `preexisting.diff`, `rename-map.json`, `final-manifest.json`, `final-remaining-occurrences.json` and static-check receipts. This preserves prior user/root edits. No staging or commit was performed.

- All 46 current tracked XML solution/project/build files parsed. Every concrete source, project and embedded-resource path resolved. The 18 deliberately absent `Bozzetto.FsiHost` template sources each match a `Bozzetto.Core` embedded resource.
- All 32 original binary assets retained identical bytes. Binary screenshots/icons may still visually depict upstream branding; they were not rewritten as text.
- Central package versions and `Directory.Build.props` product version are unchanged.
- Remaining SageFs source occurrences were reviewed for external Harmony/plugin contracts or actual upstream links. Default `37749`/`37750` references remain only as history or explicit upstream coexistence documentation.
- Old source-name directories contained 3,106 generated/cache files after tracked paths moved. They were preserved intact under the external checkpoint's `ignored-before/` directory. No original nonignored untracked files were present in those directories. Generated outputs in the new Bozzetto directories are owned by the current build.

The source migration agent ran no build, test suite or daemon. No connected REPL was available; the root agent coordinates the final build and runtime evidence separately. Source parsing/reference checks do not replace that gate.

## Remaining operational work

- Root-agent build, unfiltered test tiers, packaged `boz` smoke checks and editor build/runtime checks must establish the renamed product's behavior. No published Bozzetto NuGet or editor release is claimed.
- The new editor publisher metadata and self-hosted runner label `bozzetto-local` require corresponding external account/runner configuration before publishing/CI can use them.
- Inherited publishing and ship/pre-push automation still selects `master`; this checkout's branch is `main`. Branch/release policy needs an explicit decision before using those release scripts.
- Renamed cloud worker/bucket identifiers are source configuration only; no cloud resources were created or deployed.
- External SageFs.Harmony and the separate sagefs.nvim plugin remain specific dependencies/contracts to replace deliberately, while upstream provenance should remain accurate after replacement.
