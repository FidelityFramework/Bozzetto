# Bozzetto.Harmony ownership checkpoint — 2026-09-30

The source fork and consumer migration are ready for review. The Release build,
locked restore, package creation and runtime patch probes pass. The latest full
default suite is **Trusted: 9,784 accounted, 9,780 passed, four existing ignores,
zero failures/errors**. Two earlier runs hit the production low-memory guard;
their evidence remains recorded below. The separate provider assessment still
requires native validation against the Composer owner's revised distribution.

## Owned source and integration

The controlled Git repository is `/home/hhh/repos/Bozzetto.Harmony`, branch
`bozzetto`, at `3d3da555f6372ec92cb45b4cba142b70e553b433`. The imported package was
built at `c52cd5a3ad4578e3362f032ae95be017fb4fe7fd`; the subsequent commit only
hardens packaging paths and updates their documentation. It is a source project,
not a generated artifact sibling. It preserves the original history and
licenses from upstream base `ffb6e9cabd1b83d4a51ef02fcaa914bf1269e51d`.
The reference remote is `upstream`, with pushes disabled. Nothing was published.

The package and CLR assembly are now **Bozzetto.Harmony**, version
**2.4.2-bozzetto.1**; the compatible API namespace remains `HarmonyLib`.
Version-reporting code was corrected for the new assembly identity. The
injected FSI host keeps its intentionally separate `Bozzetto.HostHarmony`
identity. A coexistence test's wording was corrected: it loads the owned
package beside that isolated copy, rather than claiming an upstream 0Harmony
fixture it no longer supplies.

Bozzetto consumes the reviewed artifact in `vendor/Bozzetto.Harmony/`.
The old `harmony-nupkg/SageFs.Harmony.2.4.2-sagefs.1.nupkg` was preserved in the
external evidence directory and removed from the checkout. CI no longer clones,
checks out or builds the upstream repository. The old package/build identity is
absent from active consumer configuration and all dependency locks. Earlier
checkpoint documents remain historical records of their exact audited state.

## One refresh and verification path

```sh
DOTNET_HOST_PATH=/absolute/dotnet scripts/update-harmony /absolute/Bozzetto.Harmony
```

The script invokes the owned fork's `scripts/pack-bozzetto`, stages output in an
external cache, validates the new package, imports it and regenerates locks.
Relative XDG cache settings fall back to `~/.cache`; the normalized path is
passed to the child. Both scripts reject scratch within their source checkout,
including symlink paths, and the consumer also guards the selected Harmony fork.
It never guesses a sibling checkout, resets source, clones, pushes or publishes.
The fork script reads its declared version rather than overriding it with a
hardcoded build number.

`build/HarmonyPackage.fs` is shared by the import tool and CI. It checks the
central package version, nuspec identity, package SHA-256 and the net8/net10 DLL
identities by reading CLR metadata without loading them. The manifest records
source commit, dirty state and recursive submodule pins. Reimporting different
bytes under the same version is refused: increment the fork's `BozzettoBuild`
and Bozzetto's central package pin together. This avoids stale NuGet cache
content. The guard also checks retained package files: importing v2 and later
returning to v1 cannot overwrite the retained v1 with different bytes.
`AGENTS.md` and contributor instructions describe this same path.

The vendored artifact is an intentionally versioned dependency input, not
scratch output. Build staging, logs and probe copies reside outside repos.
The artifact review also corrected `Bozzetto.Host`'s outer multi-target build:
manifest generation now runs only with a concrete framework/output directory.
Both net10/net11 manifests remain in `bin/Release/<framework>/`; no stray
`Bozzetto.Host/host-manifest.json` is created. A fresh host build passes with
zero warnings/errors and both output manifests validate. Existing `bin/` and
`obj/` ignore rules suffice; no ignore rule conceals a misplaced manifest.

## Evidence

Durable evidence: `~/.local/state/bozzetto/checkpoints/2026-09-30-harmony/`.
The package and manifest are also present in the source tree for review.

- Imported package SHA-256:
  `7ddc863b8e0e4ecb8ab43cbbd9e6259a0662d12e7fdef020daf44fe4c4c03e08`.
- Imported net10 DLL SHA-256:
  `30eb1266c72c1883654f07cab856d4e73015d4340acbbde4f3355a767cafe57b`.
- Owned source builds net8/net10 successfully. Real patch/unpatch, assembly
  identity and version-reporting probes against the **exact imported DLL** pass
  on .NET 10.0.12 and .NET 11 RC1: patched method returns 99, unpatched returns 1.
  A rebuilt fat package can differ byte-for-byte, which is why the imported
  hashes and immutable-version rule matter.
- Bozzetto Release solution build: zero warnings/errors. Locked restore and
  `dotnet pack --no-build` succeed. Both target graphs for Bozzetto, Core and Host
  use the owned package; the executable output directories contain the new DLL
  and no `0Harmony.dll`.
- Compared all 22 source lockfiles with the preserved provider checkpoint:
  exactly five Harmony consumer locks changed; all other package versions and
  content hashes remained unchanged. The main product version is unchanged.
- Import probes pass: initial import, same-byte reimport, changed-byte rejection
  and preservation of the original artifact after refusal. Final package
  verification, copied CI-script compilation and shell syntax checks pass.
- Final tooling review added seven version-history import checks and eight
  isolated packaging-path cases, including relative XDG settings and symlink
  refusals. All pass. These tooling fixes leave the imported package unchanged.
- Two unfiltered default runs each accounted for 9,775 tests: 9,767 passed,
  four errored, four existing ignores, zero failures. The errors are all
  `BuildPreflight session-create boundary` cases receiving `MemoryPressureRefused`
  at 6.5–7.0% available memory, below the 8% floor. Reducing concurrency to four
  workers did not remove that condition. No guard was disabled and no user
  service was stopped. Those two whole-suite verdicts were `TestsFailed`.
- After memory availability recovered, the full Release solution and unfiltered
  default suite were rerun with the provider repair's nine additional unit tests:
  **9,784 accounted, 9,780 passed, four existing ignores, zero failures/errors,
  Trusted**. The four memory-sensitive boundary cases passed. Logs are in the
  adjacent external `2026-09-30-provider-repair/` evidence directory; this is
  also a passing default gate for the final Harmony consumer closure.

Not run: full upstream NUnit suite, full host/browser/mutation tiers, net8 runtime
probe, or simultaneous upstream/Bozzetto daemon operation.

## Remaining dependency work and provider audit

The fork still includes pinned MonoMod (`1c2740546ca6c3a5851cf10c3b9ba1d45d78e280`),
iced (`c50f29b7bc305696895c075f3fc7719751426b12`) and Cecil implementation code in
its merged assembly. It remains a .NET managed patching backend. Source builds
retain existing NuGet/build dependencies, including warnings for build-only
`Microsoft.Build.Tasks.Git` 8.0.0 (NU1902) and redundant System.Text.Json references.
Those pins were not silently upgraded. SourceLink's misleading upstream source
URLs were removed in favor of embedded owned sources. Consolidation and pruning
can now happen in the owned repository with explicit package revisions.

The [provider assessment](Bozzetto_Provider_Assessment_2026-09-30.md) is a separate
workstream. Bozzetto's R1–R4 repairs and remaining native validation are described
in the [repair checkpoint](Bozzetto_Provider_Repair_Checkpoint_2026-09-30.md).
This ownership migration alone does not resolve them. The user assigned the compiler-side lease fix to the Composer
agent, with a handoff in Composer's
`docs/Bozzetto_Lease_Release_Handoff_2026-09-30.md`. Bozzetto retains ownership of
logical retirement, cancellation cleanup, refusal identity and responsive
supervision. Native acceptance must use the rebuilt compiler supplied by that
agent, not silently reuse the previously audited DLL.
