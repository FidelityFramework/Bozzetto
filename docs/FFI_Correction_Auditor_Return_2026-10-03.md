# Callable aggregate checkpoint: auditor return and work order

Return for the [implementer handoff](FFI_Correction_Checkpoint_2026-10-03.md),
written late on October 3, 2026. The owner asked for no broad tests, so this is
a **bounded review**: retained evidence, failure text in the retained TRX, the
governing spec clauses and targeted source reads at the tested working trees.
No build, test, gate or daemon action was taken by the auditor.

Grades follow the [Phase 0 record](FFI_Correction_Phase0_Audit_2026-10-03.md#evidence-grades):
E executed/retained result, R source observation, S normative spec, A agent
report, I inference. Every root cause below carries its grade; the I-graded ones
come with the single discriminating check that settles them before any edit.

## Carried-forward results

| Grade | Gate | Result |
| --- | --- | --- |
| E | Fidelity.PSG, unfiltered | 343/343 passed, zero skipped |
| E | Alex, unfiltered | 287/287 passed, zero skipped |
| E | Clef, final unfiltered | 2,419 executed: 2,323 passed, 96 failed, zero skipped |
| E | Clef, trusted baseline `3795a179` | 2,416 executed: 2,335 passed, 81 failed |
| E | Comparison | zero missing, 3 added (all fail), 15 additional failures, zero failure-to-pass; 12 regressions in existing cases |
| R | Composer fixture repair | eight `Inputs.Contracts` initializers edited, not rebuilt or tested |

## Accepted

- **Evidence binding (E).** TRX counters match the handoff. The three final
  source manifests verify inside their repositories (PSG 16/16, Alex 19/19,
  Clef 40/40), and every changed or untracked file is covered, so the reviewed
  trees are the tested source.
- **Spec quotes (S).** Closure Representation §2.4 and FFI Boundary §3.6.1
  item 1 are quoted correctly.
- **Status boundary (R).** The handoff claims no acceptance and keeps Phase B,
  R4, ABI, foreign lifetime and the Composer native journey open. Correct.
- **PSG and Alex gates (E).** Accepted as structural and passive only.

## Step 0: push, then use retrieval and the LAN for coding questions

Owner instruction, relayed by the auditor.

1. **Push the current increment as a recovery checkpoint now**, on `main`, in
   each affected repository: Fidelity.PSG, clef, Alex, Composer, and Bozzetto
   (handoff, evidence-record updates and this return). Commit only your own
   files. In Bozzetto, exclude the other actor's edits to
   `FFI_Correction_Later_Tranches_2026-10-03.md` and
   `FFI_Correction_Step0_Plan_2026-10-03.md`; leave the owner's Farscape changes
   alone. Say in each commit that it is a failing checkpoint (Clef 2,323 / 96)
   so nobody reads it as accepted.
2. **Why first:** the retrieval service indexes the FidelityFramework repos at
   their pushed heads. Until the increment is pushed, retrieval cannot see the
   code you are about to change.
3. **Then answer coding questions through retrieval, with the LAN building the
   query.** For each question below, send the question plus the retrieval
   `schema` result to a LAN endpoint and ask it to write the retrieval request
   JSON (`find`, `sources` or `pgq`, pinned by `snapshot_id`); run that request;
   paste the hits back to the LAN model or read them yourself.
   - LAN: direct llama.cpp, `http://gpu-one.spkez.dev:8000/v1` (also `gpu-two`,
     `gpu-three`). Not Hermes. Send `chat_template_kwargs: {enable_thinking:false}`
     for bounded answers, or `max_tokens` ≥ 4000. The model has no tools; paste
     what it needs.
   - Retrieval: `https://duckdb-fidelity-pgq.spkez.dev/v1/retrieval`, token in
     `~/.hermes/secrets/lab-retrieval.token`, helper
     `bash ~/.codex/work/clef-2026-09-28-part2/lan/retrieve.sh REQUEST.request.json`.
     Bozzetto docs are not in its corpus.
   - Spec questions: Cloudflare hybrid search with `"type":"spec"`, public
     language terms only.

## Work order

Do W1 to W3 in order. Each has an inner-loop check (a filtered run is inner-loop
evidence only) and an exit condition. Do not start the unfiltered gate until all
three exit conditions hold.

### W1. Closed function references stored in aggregates have no carrier (blocker)

**Covers:** both regressed `CallableBranchPublicationCases`, all three added
`CallableAggregateCases` (including the same-identity invalidation case), and
probably both `ClosureValueTests` layout cases.

- **Symptom (E).** CCS8414 "The actual callable input has no settled carrier
  formation." The stored values are plain references to a top-level function:
  `Ok first` in `let input () : Result<bool -> bool, bool> = Ok first`
  (`branch-publication.clef` 4:44-52) and `{ Apply = first }`
  (`callable-aggregate.clef` 6:17-34).
- **Where it is raised (R).** `clef/src/Compiler/Baker/Recipes/CallableAggregateRecipes.fs`,
  `closedCarrier`, lines 42-47: `inputs.Carriers.TryFind value` returns `None`.
  Line 48 also requires `carrier.Occurrence = value`.
- **Why the branch cases regressed (R).** D6(b) moves callable union payloads
  into the aggregate protocol, so `Ok first` now goes through this recipe instead
  of the old branch path. Same root as the added tests.
- **Likely root (I).** Carriers are keyed by occurrence in
  `PSGSaturation/SemanticGraph/CallableCarriers.fs`, `settleWithResolution`
  (lines 150-290). A reference to a closed top-level function is probably not a
  carrier occurrence at all, or its carrier is keyed at another node.
- **Discriminating check, before editing.** In an inner-loop run of
  `CallableAggregateCases` (record case), print for the stored value: its
  `SemanticKind`, `inputs.Carriers.ContainsKey value`, and every carrier whose
  `Formation` equals `CallableIngress.tryValueIdentity reading value`.
- **Correction, by outcome.**
  - *No carrier exists for that reference:* extend the census in
    `CallableCarriers.settleWithResolution` so a reference to a closed ordinary
    function yields an `OrdinaryFlatClosure` carrier keyed at the stored
    occurrence, with `Formation` from `tryValueIdentity`, `Environment = None`,
    `EnvironmentValue = None` and empty `Lifetime` (lifetime is only required
    with an environment or for a native entry, recipe line 62). Contract
    assignment then follows `CallableContracts.assign` as for other carriers.
  - *A carrier exists at another node:* author a carrier row for the stored
    occurrence in the census, carrying the shared formation. Do not loosen the
    `carrier.Occurrence <> value` pairing check at recipe line 48; Closure §2.4
    requires each alternative to retain its actual formation, and distinct
    factory calls must stay distinct.
  - Either way the fix lives in the census, which owns carriers. Do not add a
    recipe-side lookup that admits a value without a settled carrier.
- **Inner-loop check.** `CallableAggregateCases`, `CallableBranchPublicationCases`,
  `CallableEmissionCases`, `ClosureValueTests`.
- **Exit.** Both branch-publication cases pass with unchanged assertions. All
  three added cases pass, including the invalidation-before-witnessing
  assertions. Then rerun `ClosureValueTests`: if the `Work` field still shows
  `Opaque ... CCS8410` from `Placement.fs:308`, the recipe is still not
  authoring a slot row for that (aggregate type, path); trace that before W3.

### W2. The I5 participant check walks every contract (blocker)

**Covers:** `PublicationContractCases` (`functions`, `lazy`, `record`,
`scalar`), `LazyCallablePublicationCases` generic lazy history, and
`WitnessPreparedScopeTests` unused/dormant library growth.

- **Symptom (E).** `Emission.Callable.Aggregates.I5` "A participant lacks its
  exact published row or source occurrence", on programs that store no
  callables in aggregates.
- **What the code does (R).**
  - `Fidelity.PSG/src/Fidelity.PSG/CallableAggregateIntegrity.fs`, final loop:
    `for KeyValue(key, contract) in callable.Contracts do yield! vector key contract.Participants`
    checks **every** published contract.
  - The `vector` presence match (lines 20-33) has no case for
    `CallableImplementation`, `CalleeBody` or `CalleeParameter`; they fall to
    `revision.Nodes.ContainsKey`. By contrast `carrierChecks` (line 103) accepts
    an implementation from `callable.Symbols`.
  - `clef/src/Compiler/Baker/Recipes/CallableContractRecipes.fs` lines 20-45
    author a contract for every ordinary implementation, with implementation,
    body and formal participants, and `CallableCarriers.fs` line ~258 assigns
    contracts to every carrier.
- **Likely root (I, strong).** Contracts are now published for implementations
  that are not in the live revision (lazy history, dormant library, body-free
  imports), so their body and formal participants are absent from
  `revision.Nodes`. This matches all six failure themes.
- **Discriminating check.** For the `scalar` case (flagged NodeId 164), print
  the failing participants' `Role`, `Node` and whether that node is in
  `revision.Nodes`, `callable.Symbols` or neither.
- **Correction.**
  1. **Publication selects contracts by reachability.** Baker may keep settling
     a convention for every implementation. The source publication copier must
     publish only contracts reachable from the live revision's published
     `AggregateSlots.Contract` and `Carriers.Contract`. The existing
     `WitnessPreparedScopeTests` contract, that unused and dormant library
     growth does not enlarge published fact inventories, requires this.
  2. **Symbol-only implementations.** For a published contract whose
     implementation is body-free in this revision, add a
     `ParticipantRole.CallableImplementation` case to the `vector` presence
     match that accepts `callable.Symbols`, matching `carrierChecks`. Pair it
     with two PSG tests: symbol-only implementation accepted; implementation
     absent from both nodes and symbols refused.
  3. **Do not relax `CalleeBody` or `CalleeParameter`.** If a selected contract's
     body or formals are not published, the selection in step 1 is wrong; fix the
     selection, not the check.
- **Inner-loop check.** The six cases above plus `CallableAggregateTests` in
  Fidelity.PSG.
- **Exit.** All six pass with unchanged assertions, and the dormant-library
  inventory assertions are untouched.

### W3. Interior native-entry and closed-function fields must place with zero storage (blocker)

**Covers:** `BoundaryValueCases.Native entry records use the declared platform
pointer width` (32, 64) and, if W1 did not clear them, `ClosureValueTests.Aggregate
function fields retain the entire settled view descriptor` (32, 64).

- **What the code does (R).**
  - `Placement.fs` line 253: a `NativeEntry` contract hits
    `pending "native entry component realization remains pending (CCS8410)"`.
  - `CallableContractRecipes.fs` comment: "Native entries never enter this
    recipe."
  - `CallableAggregateRecipes.fs` line 62 refuses a native entry without
    lifetime evidence (CCS8101).
- **Scope (R, rulings).** D6(b) explicitly includes `FnPtr` record fields in the
  one callable-aggregate protocol. The fixture is an interior record,
  `type Listener = { Entry: FnPtr<bool -> bool> }`, built with
  `FnPtr.ofFunction callback` and invoked inside Clef; it never reaches C. This
  is in scope now. A native C listener-table projection is not.
- **Clauses (S).** FFI §3.6.1 item 1: a single-entry family needs no code
  storage; item 2: each inhabited slot has one receiving contract including
  code-lifetime premises. Closure §2.4 final paragraph: a native entry carries
  its contract and code-lifetime premises with explicit environment absence,
  and stays a distinct kind from a closed ordinary function.
- **Correction.**
  1. Baker authors a `NativeEntry` contract for the compiler-owned family made
     by `FnPtr.ofFunction`, per ruling Q1: complete parameter and result
     representations and conversions, explicit environment absence, and a
     code-lifetime premise for a program function that lives as long as the
     program image. Record that premise as lifetime participants. Do not invent
     resource obligations; a scalar-only signature carries none.
  2. The census produces a `NativeEntry` carrier for that occurrence.
  3. In `Placement.fs`, replace the line 253 pending for the single-entry,
     environment-absent case with zero-storage placement through the same path
     used for one-alternative ordinary components. Multi-entry native families
     use the selector path. Leave the C-table projection unimplemented.
- **Expectation rewrite, after the producer is fixed.** Cite FFI §3.6.1 items 1
  and 2 and Closure §2.4 in the test comments. Assert: the record is fully
  placed; the `Entry` component has zero bytes; the PSG keeps `NativeEntry`
  kind, its contract identity, explicit environment absence and the lifetime
  participant. **Keep** the final assertion that the standalone `FnPtr` type's
  layout is `TypeLayout.PlatformWord`: that is the boundary value layout, not
  the interior field. Add the paired negative: a native entry without lifetime
  evidence is refused with CCS8101. For `ClosureValueTests`, a closed singleton
  `Work` field asserts zero component bytes and a placed `Tail`.
- **Do not:** restore the pointer representation, accept `Opaque` as the new
  expectation, or add ABI or C-table work.
- **Exit.** All four layout cases pass with the rewritten expectations, and the
  new negative passes.

### W4. Composer fixtures

Rebuild Composer tests and run `tests/Alex.Tests` at the validation point below.

## Validation sequence

1. Inner loop per work item, filtered, recorded as inner evidence only. Use the
   retained command form with a `--filter` on the classes named above.
2. **Clef unfiltered** under a test lease, same command form as
   `d6-clef-final-suite-command.txt`. Compare against
   `checkpoint-3795a17.trx` with the existing comparison script.
   **Accept only if:** zero missing occurrences; the 12 regressions and 3 added
   cases pass; every remaining failure is one of the 81 baseline names; no new
   failure name appears.
3. **Fidelity.PSG unfiltered** (it changes in W2): 343 plus the new paired
   tests, all passing.
4. **Alex unfiltered** only if Alex changed.
5. **Composer** test build and `tests/Alex.Tests`.
6. Commit and push each repository with the counts in the message body of the
   evidence record, not the commit subject.

## Guardrails

- No permissive fallback: do not relax I5, do not let publication reconstruct
  missing rows, do not admit a value without a settled carrier.
- Fix at the owning pass: carriers in the census, contracts in Baker, selection
  in publication, presence rules in the PSG reader.
- Preserve every positive source program.
- No broadening into ABI settlement, C-table projection, foreign lifetimes, R4,
  or desktop binding regeneration. No shared daemon switch.
- Preserve concurrent edits. Use whole-run leases for gates.

## Not reviewed in this bounded pass

Not to be read as accepted: the I1-I4 and I6 rules in depth, Alex's
`CallableAggregatePatterns.fs`, the witness-segmentation and reusable-code
fingerprint changes, and Baker's other new recipe and ingredient files. Ask for
a fuller review of handoff priorities 2 and 3 at the next checkpoint.
