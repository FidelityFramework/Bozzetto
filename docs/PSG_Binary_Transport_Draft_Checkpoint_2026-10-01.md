# Binary transport — unfinished source checkpoint

October 1, 2026. This follow-up preserves work omitted from the source checkpoint.
The validated runtime-patching purge remains the separate commit **e971e3fa**.
Its [auditor checkpoint](Bozzetto_Minimal_Host_Auditor_Checkpoint_2026-10-01.md)
and installed binaries retain their original scope and identities.

The subsequent [integration auditor handback](PSG_Transport_Integration_Auditor_Checkpoint_2026-10-01.md)
records verified source/runtime state and the required cross-project repair order.
This draft checkpoint remains evidence of source preservation, not integration.

## Seven files being preserved

Six files in `Bozzetto.Composer.Protocol/` contain the proposed shared provider
data, typed protocol, BAREWire codec, stream framing, wire schema and project
file. `Bozzetto.Tests/ComposerBinaryProtocolTests.fs` contains proposed roundtrip,
golden, malformed-input and fragmented-stream checks. They were left untracked
when attention shifted to the purge. That omission made the source-control
anchor incomplete; this commit records them without claiming integration.

The protocol project is not referenced by the solution or production projects.
The test source is not included in the test project. **None of these draft tests
ran in the 8,954-test default gate or the 40-test native provider gate.**

The draft describes binary worker protocol 2 with bounded framing and explicit
version/contract agreement. Its PSG hello field identifies the loaded contract;
it does not carry an actual published revision. The current services still use
worker protocol 1, JSON over stdio. No socket transport is installed by this
checkpoint, and no automatic encoding substitution is intended.

## Coordinated source anchor

| Repository | Source identity at this checkpoint | Scope |
| --- | --- | --- |
| Bozzetto | This transport-draft commit; deployed purge `e971e3fa` | Draft codec/protocol/tests saved; production wiring remains absent. |
| Fidelity.PSG | `63b8df4` | Six codec/generator files saved; schema 12 and compiled project unchanged. |
| Alex | `9d53b9e` | Clean checkout; remote main matches; passive reader included in validated native closure. |
| Clef | `ba694e1` | Source-admission repair; 2,156/2,255 pass, same 99 failures, no missing or new failures. |
| Composer | `723e900`, implementation `c1e3ff6` | 385/386 pass, same callable-Result transport failure; five added ownership checks pass. |
| Fidelity.FSharp.Incremental | `d476aea`, preview.6 implementation `3b86e2d` | Shared library unchanged at the purge checkpoint. |

The [PSG draft checkpoint](../../Fidelity.PSG/docs/Binary_Transport_Draft_Checkpoint_2026-10-01.md)
records its narrow Sage evidence and unrun acceptance work. No new compiler,
Alex or library test result is claimed by this source-only check-in. The installed
daemon, worker and compiler remain the purge checkpoint's validated closure.

## Remaining acceptance work

- Compile and test both codecs with explicit BAREWire/compiler identities,
  complete wire inventories and malformed/oversized/incompatible input controls.
- Consolidate provider data ownership instead of linking duplicate definitions
  into one process; wire both endpoints to the same contract.
- Transport actual PSG revisions, including exact session/revision authority,
  structural validation and refusal of stale or withdrawn publication.
- Put stream/socket IO behind explicit work admission. Preserve reservation
  before writes and actual launches, shared-demand cancellation, response identity,
  bounded admission and physical process/stream cleanup.
- Exercise decoded revisions through passive Alex and native lowering, proving
  retained-region reuse and revocation behavior through the real service path.

The retained editor source-only CCS8011 failure and callable-Result transport gap
remain identified in the purge checkpoint. Saving this draft changes neither
their status nor the acceptance of the installed compiler.
