# FFI correction and clean baseline: implementor guide

October 3, 2026, version 1. The owner has handed implementation of the clean-baseline repair and the FFI boundary
correction to an incoming agent. The agent that ran the research session becomes the auditor. Nothing from that
session is committed except what the owner chooses to commit. Phases 0 and B below are fully specified by the
material this guide cites. Documents for the later steps are being finished and will appear in this folder with
the `FFI_Correction_` prefix, together with a tracker that supersedes the checklists here.

## Roles and authority

| Role | Holder | Responsibility |
|---|---|---|
| Owner | The framework's author | Decisions, commits, every switch of the shared daemon |
| Implementer | The incoming agent | Phases 0 onward, evidence records for every gate |
| Auditor | The outgoing research agent | Audits each phase against the evidence records |

The [rulings record](FFI_Correction_Rulings_2026-10-03.md) holds the owner's rulings and principles in full. Read it
before anything else in this guide. Authority runs in this order:

1. The owner's rulings, for intent and direction.
2. The normative spec, [`clef-lang-spec/spec/ffi-boundary.md`](../../clef-lang-spec/spec/ffi-boundary.md) and the
   chapters it cites. The spec is primary but not infallible: legacy C realities can justify an amendment, recorded
   with the API evidence.
3. Code and executed evidence, for what is true today.
4. Agent reports, including this guide. Treat each as a graded claim to verify.

## Evidence and tools

Raw evidence from the research session lives at `~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/`. It is local
and non-portable, like the evidence paths in earlier checkpoints.

| Folder | Contents |
|---|---|
| `research/` | Requirement map R1 to R83 and corrections C1 to C15 to the previous FFI checkpoint (`gap-map.md`), the first adversarial review (`critique.md`), seven lens reports |
| `t0/` | Pre-edit baseline records with the exact command for every suite (`baseline/<repo>.md`), the Step 0 brief (`T0-BRIEF.md`) |
| `baseline-repair/` | Inventory (`INVENTORY.md`), triage reports, the draft repair plan (`REPAIR-PLAN.md`) and its review |
| `t1/` | Step 1 designs (`records.md`, `deployment.md`, `obligations.md`) and the reconciled plan (`T1-PLAN.md`) |
| `notes/` | The report on the owner's finished Farscape notes (`notes-final.md`) |
| `tools/` | Helper scripts with a README: retrieval service, public hybrid search, Bozzetto MCP calls, LAN workers |

## Current state

Every suite ran unfiltered under Bozzetto leases on October 3 at the heads listed. These counts are executed
evidence.

| Repository and suite | Head | Result |
|---|---|---|
| BAREWire harness | `571ff31` | 619 of 619 |
| Fidelity.Data | `7cc19d6` | 424 passed, 1 pending test ignored, 0 failing |
| Fidelity.PSG | `f153c75` | 308 of 308 |
| clef, `Clef.Compiler.Service.Tests` | `a6614b5` | 2,260 of 2,354, **94 failing** (76 first-coded CCS8011, 12 CCS8403, 6 assertions) |
| Alex | `4e1f859` | 278 of 278 |
| Composer, `tests/Alex.Tests` | `fcd68d6` | 400 of 401, **1 failing** |
| Composer, NativeCallbacks runner | `fcd68d6` | **0 of 29**, every case fails at compile time |
| Composer, regression runner | `fcd68d6` | **3 of 53 compile** (45 failures, 5 timeouts), 3 of 3 execute |
| Farscape tests and BoundaryConformance | `5ec1954` | 649 of 649, 17 of 17 gates |
| Calque | `d0f6143` | 149 of 149 |
| Bozzetto default tier | `d2dc5c8f` | Debug run aborts at discovery (stale `obj/Debug` reference assemblies). Release run: 8,401 passed, 3 ignored, 1 errored, `TestsFailed`. With `DOTNET_ROOT` set: 8,402 passed, `Trusted` |
| Fidelity.FSharp.Incremental | `d476aea` | 108 of 108 and 3 samples |

The previous FFI handoff reported 61 clef failures. That figure came from a run that matches the
`Category=Compiler.Service` subset exactly, 1,723 of 2,354 tests. All 61 still fail. The other 33 sit in 28 classes
that run never included.

The triage agents classified every failure (`baseline-repair/INVENTORY.md`). Their verdicts are one agent's report
each and are the first thing Phase 0 audits.

| Classification | clef | Composer |
|---|---|---|
| Regression | 0 | 1, introduced by clef `bba4025` |
| Test defect (the test is wrong against the spec) | 55 | 45 |
| Compiler defect | 28 | 5 |
| Unimplemented capability | 11 | 24 |
| Environment | 0 | 5 timeouts |

## Component boundaries

| Component | Responsibility | Never |
|---|---|---|
| CCS and Baker (clef) | Elaboration, saturation, derivation and settlement of ABI, absence, resource, spatial and callback-environment obligations | Defers an obligation to Alex or invents a missing fact |
| Fidelity.PSG | Immutable source-authored rows over BAREWire, structural integrity | References the compiler |
| Alex | Passive witness of published rows | Infers an ABI, builds an adapter, reads source |
| Composer | Orchestration, proof discharge, artifact acceptance, native launch | Skips proof discharge |
| Target pathway | Realizes native representation from settled facts | Moves a commitment into the middle end |
| Fidelity.FSharp.Incremental | Demand, invalidation, eligibility | Holds proof, publication or launch authority |
| Farscape | Bindings from clang-parsed headers and pilot contracts | Invents ownership or nonnull evidence |
| Calque | Source dialect refusals before formatting | Admits unsafe or managed-only mechanisms |
| BAREWire | Declaration vocabulary and encodings | Supplies an ownership default |
| Bozzetto | Development controller outside the compiler pipeline: client demand, delivery, supervision, process and lease lifetime | Gates compiler acceptance |

Bozzetto has latitude precisely because it sits outside the pipeline. Compiler and FFI work is accepted on its own
component gates: the unfiltered suites, the Composer runners and Farscape conformance. Adapting Bozzetto to a new
compiler contract is an independent track. Switching the shared daemon is an operational step the owner triggers,
reported separately, and it never blocks a compiler phase.

## Working rules

- Work on `main` in every repository. No branches and no worktrees.
- The owner reviews and commits. Propose one commit line per repository, `type(scope): what`.
- Read each repository's `AGENTS.md` or `CLAUDE.md` before editing it.
- Take Bozzetto leases for every caller-owned build and suite run. The MCP server is at `127.0.0.1:47749`.
  `acquire_full_build_lease` and `acquire_test_suite_lease` take no arguments. `release_work_lease` takes the
  `lease_id`. `tools/boz.sh` wraps these calls. Follow each decision. On a memory refusal, measure RAM, swap, process
  RSS and cgroup usage and report them. Never poll.
- A detached test driver outlives the agent that started it and keeps its lease. Release that lease yourself when the
  driver ends.
- Acceptance is the unfiltered suite of every touched repository, using the commands in `t0/baseline/<repo>.md`. A
  filtered run serves the inner loop only.
- Test frameworks follow each repository: xUnit in clef, Alex, Fidelity.PSG, Composer `tests/Alex.Tests` and
  Farscape; NUnit in Calque and Fidelity.FSharp.Incremental; BAREWire's own harness; Expecto with FsCheck in Bozzetto
  and Fidelity.Data. Register every new test so the unfiltered run includes it.
- Bozzetto gates run in Release with `DOTNET_ROOT` and `DOTNET_HOST_PATH` set and the system `TMPDIR`, judged by the
  `TRUST` line. Rebuild the stale Debug outputs before any Debug run.
- Never stop or restart the shared daemon without the owner's go-ahead. Never use the installed `boz`: it predates clef
  `a6614b5` and would silently drop that commit's guards.
- Never manufacture evidence. Missing ownership stays unresolved, a comment never establishes nonnull evidence, and a
  test is called defective only when the spec shows it wrong.
- SageFS is out of scope. Clef admits no .NET `task` and has no raw pointer type.
- Documentation prose follows the owner's avoid-ai-prose skill in its documentation register
  (`/home/hhh/repos/clef-lang-site/.claude/skills/avoid-ai-prose/SKILL.md`).

## Phase 0: incoming audit

Audit this session's work before relying on any of it. Record findings in
`docs/FFI_Correction_Phase0_Audit_<date>.md`, with a grade for every finding: executed, read at a pinned commit,
spec text, one agent's report, or inferred. Disagreements with the rulings go to the owner.

1. Confirm the heads. Rerun at least the clef suite and both Composer runners under leases and compare the failing
   sets with `t0/baseline/`.
2. Audit the triage. Spot-check every root-cause group in `triage-ccs8011.md`, `triage-ccs8403.md` and
   `triage-composer.md`. Each test-defect verdict needs spec text showing the test wrong.
3. Audit the draft repair plan against its review, `baseline-repair/repair-review.md`. The plan predates the owner's
   decision D6(b), so record every place where it conflicts.
4. Check the rulings record against the owner's statements. The owner settles any difference.
5. Read the owner's finished Farscape notes (`notes/notes-final.md` and the Farscape working-tree diff). The owner
   reviews and commits them.
6. Read the Step 1 material in `t1/` for orientation only. It is re-sequenced for D6(b) before Step 1 begins.

## Phase B: clean baseline

The goal is every suite in the current-state table passing at its documented command, unfiltered. The owner's
decision D6(b) makes this phase larger: one callable-aggregate protocol, with code, environment and lifetime
evidence, covers `FnPtr` record fields, ordinary function fields and callable union payloads. Flat closures and
native entries stay distinct. The Fidelity.PSG schema change moves into this phase, and the affected positive tests
must pass rather than turn into refusals.

Work that can start as soon as Phase 0 confirms it:

- The regression introduced by clef `bba4025`, from `triage-composer.md`.
- The environment items: clean Bozzetto Debug outputs, the `DOTNET_ROOT` requirement and the five regression-runner
  timeouts.
- Compiler defects and confirmed test defects in clef and Composer, in the dependency order and file partition of
  `REPAIR-PLAN.md`, corrected by its review.

Work that waits for the re-sequenced baseline document: the callable-aggregate foundation and every unimplemented
item that depends on it.

The phase closes when every suite passes unfiltered and the results are pinned. That pin becomes the baseline for
Step 0.

## Later phases

| Phase | Content | Document |
|---|---|---|
| Step 0 | Coded and located refusals, the rulings written into the spec, `Transfer.Undeclared`, callback-slot refusal with the legacy-C accommodation, Farscape pilot-key warnings | `FFI_Correction_Step0_Plan_2026-10-03.md` |
| Step 1 | Native ABI settlement on the callable-aggregate foundation, then `FnPtr.ofExtern` replacing `fromSymbol` | `FFI_Correction_Step1_Plan_2026-10-03.md` |
| Bozzetto track | Worker replacement without a daemon restart, approved by the owner's explicit go-ahead with an audit record. Independent of the compiler phases | Same Step 1 document |
| Steps 2 to 8 | Entries invoked by C, resources and absence carriers, spatial admission, captured callbacks, target vocabulary, Fidelity.Platform regeneration | `FFI_Correction_Later_Tranches_2026-10-03.md` |

## Open owner decisions

- Whether inline `signature … end` blocks are retired along with separate signature files. Nothing in the current
  work depends on the answer.
- `CHandle` equality, decided with its resource contract in Step 3.
- Further decisions surface in the later documents as they land.

## Hazards

- The filtered-run trap. A green filtered run says nothing about what it excluded, as the 61 and 94 counts show.
- The stale installed `boz`, which would roll back clef `a6614b5`.
- Detached test drivers that keep leases after their agent ends.
- Bozzetto's exit code 3 also covers a run with both failures and errors, so read the `TRUST` line, not the code.
- Long `TMPDIR` paths in `ci-pipeline.fsx` tiers can break Bozzetto's socket tests.
- Overloaded diagnostic codes: CCS8096 and CCS8010 each carry more than one meaning today.
- Ownership defaults in Farscape and BAREWire's `Function.cdecl`, which Step 0 removes.

## Evidence record

File one record per gate:

- repository heads and working-tree state;
- exact commands and lease ids;
- totals, failing sets compared with the pin, and `TRUST` lines where they apply;
- every new test name, shown in the unfiltered output;
- graded claims and residual risks.
