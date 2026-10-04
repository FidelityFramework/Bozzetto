# Callable aggregate follow-up

October 4, 2026. This record implements R1 through R4 from the
[auditor return](FFI_Correction_Auditor_Return_2026-10-04.md), in that order.
Those labels refer to the return's follow-up items, rather than the numbered
owner rulings. Phase B remains governed by the
[implementor guide](FFI_Correction_Implementor_Guide_2026-10-03.md) and
[Phase 0 record](FFI_Correction_Phase0_Audit_2026-10-03.md).

## Starting evidence

**A/E, trusted auditor execution.** The auditor independently rebuilt and ran
the four suites from the pushed heads. Clef executed 2,430 cases, with 2,349
passing and the same 81 baseline failures. PSG passed 376/376 and Alex passed
287/287. Composer executed 402 cases, with 401 passing and its one baseline
failure. The retained results are the starting comparison evidence for this
batch. No baseline rerun is planned.

**R.** The code checkouts are clean at the reviewed heads recorded in the
return. Bozzetto `4864bd0b` already contains both the auditor return and the
workstation findings. Both records are pushed. The workstation recommendations
remain a separate Bozzetto/editor task.

**E, service observation.** Retrieval generation 37 reports a fresh snapshot
`a378207c7f5bbb4ad9135481d83bbc4f077ad2c44cdb1dad97dff3cabd4cdef0`.
The scoped queries use the accepted pushed heads. Requests and responses are
retained under
`~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/auditor-followup-2026-10-04/lan/`.

## Acceptance

Each correction gets a discriminating control in its default test suite.
Focused runs establish the inner-loop exit. The batch then runs the unfiltered
suites of every touched component and its consumers under Bozzetto leases
covering each complete run. The final artifact hashes will come from builds
of the committed heads, following the auditor's evidence-method correction.
Edits stop throughout a running gate's dependency closure.

The retained comparison inputs are
`auditor-reproduction-2026-10-04/results/auditor-{clef,psg,alex,composer}.trx`
under `~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/`. Every new case must execute. Existing passing
cases stay passing, and remaining failures must belong to the trusted baseline.

Native execution, foreign lifetime closure and a shared daemon switch are
outside this follow-up batch.

## R1 placement

**R/S.** Aggregate work is prepared once from the current origin reading.
An ordering dependency exists when a stored callable's receiving parameter or
result contains another aggregate type. Baker processes ready aggregate types
together through the existing ingredient/recipe fold. Slots of one type are
processed together so placement sees the complete type. The remaining type
inventory decreases at each step. Cyclic receiving dependencies retain an
explicit source residual.

The representation owner admits only that step's type and occurrence changes.
Callable receiving contracts renew at the changed signature sites. Dependent
slots then use those renewed identities. Accounts must be current before
`Placement.callableComponent` reads them. Later account validation continues
to refuse stale semantic rows. This placement follows Closure Representation
§2.4 and Baker Saturation Architecture §3. The implementation controls include
the auditor's source program, a deeper dependency chain and a no-op fold.

**E, focused execution.** The R1 build succeeded and `CallableAggregateCases`
passed 13/13, with no skipped cases. Whole-run leases:
`5e3abb68700b4ba5aa1bb9cce14c01fc` (build) and
`bfe2d551585846ceba9beecd7e938c5d` (focused suite). Commands, logs, exit codes,
timestamps and release receipts are `r1-build-*` and `r1-focus-*` under the
follow-up evidence directory; TRX is `inner-results/r1-focus.trx`. This is an
inner-loop result, before the batch's unfiltered acceptance.

## R2 symbol evidence, schema 19 design

**R.** `CallableAggregateDependencyAccount` currently retains source incidences
and callable receiving contracts. It has no field for a symbol-only
implementation's `CallableSymbolName`. The PSG reader admits that implementation
from the symbol table, so symbol changes currently fall outside its account.
The existing symbol observer is in `CallableEmission.declarations`, called by
source-owned `WitnessEmission.prepare` before passive publication.

Before changing the schema, the selected correction is:

1. Add `Symbols: (NodeId * CallableSymbolName) list` to the dependency account.
   Keep exact typed symbol facts for the implementations referenced by its
   carriers and `CallableImplementation` participants, in deterministic order.
2. Extract the existing binding/lambda name observation into a Baker ingredient.
   Both aggregate account authorship and callable emission use that observation.
   Observation reads the current graph and creates no declaration identities.
3. The public reader compares these retained symbol facts with the published
   symbol table. Changing a symbol withdraws the previous account. Symbol facts
   stay distinct from executable source incidences and type identities.
4. Advance PSG schema 18 to 19 with the mirrored CCS type and generated mapping.
   Regenerate binary/JSON readers and the structural reference inventory.
   Update the independent binary header oracle and obsolete-schema negative.

Other R2 checks compare stored fields: selected carrier versus written value,
the live constructor operand, exact participant ownership, component data and
closure formation. Adapters remain refused pending a typed source-authored
relation. No layout choice or source analysis moves into PSG.

**R, correction to the auditor's numeric-premise minor.** `Edges.fs` can encode
`NumericDomain.Premises`, but production publication does not expose that
private numeric graph. `WitnessLiveRowsRecipes` retains complete relations in
the prepared source graph and publishes `Edges = []`; the publication recipe
copies that selection. Scanning public edges would establish nothing about
production currency. Baker retains its source-premise comparison. This batch
does not publish private numeric support to manufacture a second comparison.

**E, focused public-reader execution.** `CallableAggregateTests` and
`BinaryTests` passed 114/114 with zero skips under lease
`424568bf71d448b5b3664b363c2c013a`. Retained result:
`inner-results/r2-psg-focus.trx`, with command, log and release receipt under
`r2-psg-focus-*`. Controls include C1 through C7, erased closure formation,
conflicting environment extents/alignment, overlap and a construction naming
another aggregate. The dependency account is renewed in malformed-row tests,
so stale-account refusal cannot substitute for the named structural reason.

Schema generation and test compilation precede that result. The first build
reported 42 fixture syntax diagnostics; the second reported two ambiguous
assertion overloads. Those fixture errors were corrected without changing the
assertions. The third compiled PSG tests, then exposed a tuple-list syntax
error in the external CCS bootstrap project writer. Its source ordering is
unchanged by the syntax repair. The three build logs and lease releases are
retained as `r2-schema-build{,-2,-3}*`. No tests ran in those build commands.

**E, producer execution.** The first type-only CCS bootstrap omitted the
production `EmbeddedText` inputs and failed because generated `FSComp` was
absent. The corrected writer retains those inputs and their SDK generation
order. The next run built the type assembly, regenerated 237 structural
mappers and built Clef's test project. Its log and whole-run lease release are
`r2-producer-build-2*` (lease `b9d4e9c521964ba1bce9cc4e6cf84681`).

`CallableAggregateCases` and `BoundaryValueCases` then passed 55/55 with zero
skips under lease `a46cf6d909b24d27b162f5f21d3fe4a7`. The result is
`inner-results/r2-clef-focus.trx`; command, log and release are
`r2-clef-focus-*`. These source controls read the regenerated schema-19 PSG,
including the new account symbol facts. R1 and R2 have their focused exits;
unfiltered batch acceptance remains outstanding.

Terminology in this record distinguishes the **callable receiving contract**
(argument, result and receiving-convention facts) from a **foreign resource
contract** (ownership, lifetime and release obligations). Proof evidence and
the dependency account are separate authorities and are named separately.

## R3 diagnostic allocation

**S/R.** Spec commit `54ef369` allocates AX4002 for unsupported native callable
component reconstruction when its receiving contract and code-lifetime
evidence are published. The refusal identifies the operation and resolved slot
and retains a source location when available. Its witness emits no operations
and binds no result. The allocation precedes the Alex change.

## R3 operation binding

**R/S.** The record and union witnesses retain the complete observed operation
when asking the callable-component pattern to consume published rows. The
pattern compares the resolved field or case, aggregate and operand with that
operation before emitting anything. Union reads also require the retained
constructor and tag relationship. These are readings of published facts; Alex
does not choose a representation or reconstruct source evidence.

All alternatives are checked before either a write or a read. A stored
selector's final arm now requires its own equality comparison and portable
assertion before yielding a function value. Native reconstruction produces
AX4002 even when the receiving contract and program-image residence are valid.
A missing row cannot bypass these checks through ordinary data handling: the
witness also reads the published occurrence/type representation. Copy/update
requires explicit replacement rows for every callable component until a typed
inherited-component relation exists.

Full public-image integrity is checked once at `Alex.Generation.admit`, before
ordinary or selective witnessing. The per-operation checks do not repeat a
global reader walk. Positive direct-pattern fixtures first pass the complete
`Integrity.check`; negative fixtures then disturb one named fact. Two-arm
fixtures use distinct code symbols and environment offsets. They exercise
consumption of public values, without claiming Baker currently admits captured
aggregate storage from source.

**S/R, fixture correction.** Closure Representation §2.4 requires: “Union
payload selection additionally requires current constructor and tag evidence.”
It also states: “Publication and the witness consume source-settled
relationships; they do not reconstruct missing semantics.” The old positive
fixture used `DUInitialize` as its tag constructor without a published
initialization relation. Its positive absent-callable case now uses a real
`DUConstruct` carrying the other case's scalar payload. A separate negative
requires the unsupported initialization form to refuse. This changes the
fixture's authority and adds an explicit refusal; it does not grant a new
initialization pathway.

**E, build diagnostic.** The first R3 Alex build stopped with 11 F# compilation
diagnostics: an unqualified `Error` collided with diagnostic severity, and a
diagnostic constructor lacked its final argument. No tests executed. The
whole-run lease `0cf1461e4f3e441e976fd15dff167f84` was released; `r3-alex-build-*`
retains the command, log and receipt.

**E, focused failures retained.** Subsequent build attempts found eight fixture
indentation diagnostics and one tuple-list separator error. After those fixes,
`r3-build-4` built both Alex and Composer. `r3-alex-focus` ran 27 cases; all
stopped at the newly required full fixture-integrity check, which found missing
callable support/value-shape and occurrence-representation inventory. These
are fixture failures, not executed operation-binding or mutation evidence.

`r3-composer-focus` ran 47 cases: 44 passed and three failed. The record source
control reached an unmaterialized eta lambda; the `Result` control reached
ordinary data mapping for a callable-valued occurrence. The new kernel probe
also failed its demand precondition because only the kernel declaration root
was retained under the selected NPU context. These results remain explicit
until corrected; no R3 focused exit is claimed. Both suites ran under separate
whole-run leases and released them. Acquisition receipts, commands, logs and
TRX files are retained beside the earlier focused evidence.

**R/S, conditional placement.** The `Result` source control exposes a missing
callable-valued conditional path in `ControlFlowWitness`: its scalar path asks
for a data representation of the selected function. The correction belongs to
Alex's existing callable patterns and control-flow witness. They must compose
already published branch operands as a function value and optional environment
selected together. Closure Representation §2.4 states: “At a read, portable
control flow selects the function value and its associated environment
together.” This does not authorize Alex to recover origins, settle receiving
contracts or convert functions into data.

**E/R, closed-lambda correction.** The retained publication probes show a
capture-free eta lambda with an exact ordinary carrier, settled receiving
contract and physical declaration, alongside an older zero-capture closure
layout entry. `LambdaWitness` rejected that older entry before reading the
settled carrier. It now requires exact carrier/declaration/formal/body agreement
and explicit absence of an environment, then emits the closed expression's
function value after defining its body. Captured and native forms retain their
refusals. `r3-alex-focus-3` passes 27/27, including all full-integrity fixture
preconditions. The kernel source control also passes in Composer's focused run.

**S/E, source oracle correction.** The record now witnesses successfully.
Source checking eta-expands `first`, so its actual stored implementation is the
published eta lambda. The initial new test expected a constant named
`StoredCallable.first`, which would require an extra eta-reduction that the
source has not performed. Closure Representation §2.4 says: “Each alternative
retains its actual formation and environment instance.” The corrected oracle
requires the constant for the exact selected construction carrier, and requires
that implementation's emitted body to pass its own argument to
`StoredCallable.first` and return that call's result. The prohibition on stored
function values remains. This preserves the auditor's behavioral check while
also checking the actual published formation.

**R/E, physical alternatives.** Probe v3 shows both conditional arms reachable
and explicitly `EnterLocal` in source traversal, alongside a logical known-`Ok`
selection. `CallableBranchObservation` explicitly supplies no reachability or
effect-erasure authority. The existing final source fold now uses conservative
origin resolution for physical carriers and flows; logical calls and branch
authority retain their refined reading. Callable publication independently
checks both readings against the current graph. It adds physical operand
transport from the held flow's authored direct dependencies, with callable
shape and source-type correspondence. No graph rewrite, second exclusion
solver, cross-pass snapshot or relaxed Alex correspondence is introduced.

The new source control checks both physical leaves, exact arm transport and
forwarded memberships while retaining the selected logical arm. It also
disturbs the unselected lambda body and requires stale publication to fail.
Execution of that correction is pending at this entry.

**E, mutation controls.** Each mutation compiled and ran the same 27-case Alex
focused suite under separate whole-run build and test leases. Source was
restored after each run and its SHA-256 matched the saved default. No branch,
worktree or clone was created.

| Mutation | Discriminating failure | Result |
| --- | --- | --- |
| M1: remove native refusal | Four native construction/projection controls receive output instead of AX4002 | 4 failed, 23 passed |
| M2: store selector zero | Both selected-one controls observe `0` instead of `1` at the selector store | 2 failed, 25 passed |
| M3: reverse compared ordinals | All four paired-arm controls observe comparison `1` instead of `0` | 4 failed, 23 passed |
| M3b: retain ordinals but swap arm operations/values | All four paired-arm controls observe `second` instead of `first` | 4 failed, 23 passed |

M3b checks the actual arm pairing separately from the comparison-literal check.
These are assertion failures after valid fixture construction, rather than
build or setup failures. Exact patches and restoration transactions are in
`r3-staged/mutations/`; commands, lease receipts and TRX are `r3-M*` and
`inner-results/r3-M*.trx`. Accepted outputs must be rebuilt from restored
source before the unfiltered gate.

**E, R3 focused exit.** Clef passes 35/35 (`r3-clef-focus-2`), Composer passes
47/47 (`r3-composer-focus-5`), and the restored Alex outputs pass 27/27
(`r3-alex-restored-focus`). Zero cases were skipped. Each run used and released
its own whole-run lease, with the exact acquisition receipt retained. The two
source-to-witness programs are unchanged from the auditor's record and `Result`
controls. Their output passes native `mlir-opt --verify-each`, including SSA
dominance. The `Result` control follows each yielded SSA to the correct distinct
implementation and retains both structured regions; eager callable arguments
may have their dominating definition before the conditional.

The additional Clef source control has explicit `selected` and `forwarded`
bindings, so its forwarding assertions require a real path. The first version
had required a forwarded row without writing a forwarding expression; that
fixture correction leaves every assertion intact. No previously accepted
expectation was changed. The earlier focused failures and probe logs remain
available as the explanation for these corrections.

**R/I, bounded remaining case.** A same-implementation conditional can remain
an exact carrier under conservative resolution and therefore has no flow-owned
branch transport in this correction. Its strict formation/environment checks
may still refuse. No execution or general acceptance is claimed for that case;
it requires a scoped control before extending physical callable coverage.

## R4 fingerprint placement

**R.** The existing scalar segmentation projector will retain all
implementation-owned receiving contracts while considering a reusable region.
A native receiving contract explicitly excludes that region from independent
reuse; its code stays in Common. Ordinary interface serialization is unchanged.
The controls distinguish an interface-only change from a support-only change,
and check native exclusion with and without a coexisting ordinary contract.
They deliberately do not claim that manually edited rows are fresh source
proofs. A separate mutation removes receiving contracts from fingerprint scope
and must be detected by the interface-only control.

**E, focused exit and mutation.** `ComparisonRegionCases` passes 21/21 in
`r4-focus`. Removing receiving contracts from the fingerprint scope makes
`A callable interface only edit changes its scalar fingerprint` fail because
the fingerprints remain equal; the other 20 controls pass. `r4-mutation-*`
retains both leases and the exact execution. The transaction restored
`WitnessSegmentation.fs` to SHA-256
`e9b5149e56c501e6bb8905aadff4c0d98f1b2d084d94c577c6f71d8f9de6e17d`.
The final build and unfiltered runs will use the restored committed source.

## Committed build and evidence binding

**E.** The restored source was committed on `main` before the final rebuild.
The build completed without errors under lease
`7eae3cec43d54b8aa3907241c99bfe1a`, from 07:25:42 to 07:27:44 EDT on October 4.
Its lease was released. `final-build.sh` rebuilds the four test projects with
`--no-restore --disable-build-servers -m:1 -t:Rebuild`. The driver sets
`DOTNET_ROOT=/home/hhh/.local/share/mise/installs/dotnet/10.0.401` and
`DOTNET_HOST_PATH=$DOTNET_ROOT/dotnet`. Exact commands, output and receipts are
retained as `final-build-*` in the follow-up evidence directory.

| Repository | Tested commit |
| --- | --- |
| Fidelity.PSG | `bb062376fa4f13763246997feeb86ca48fe71989` |
| clef | `f23e33657fc73c4b99e29df01851bb80f6e0137d` |
| Alex | `efd1a008ce5d34a8cf4585f98faa269de112234a` |
| Composer | `0caa67d85533d69b23acf7f2b2fb2ab9fb71dd8d` |
| clef-lang-spec | `54ef3690c840327eed3fe832b6886520d4a33663` |

BAREWire remains at `571ff31da1993b0cf3bad65934c382ad81f131f8`.
`final-source-manifest.json` records the full commit and tree identities, Git
blobs, working-file hashes and changed paths against the auditor's accepted
heads. All 39 changed paths are covered. Each committed file matches its
working copy, and all five repositories were clean on `main` at capture.
The JSON's SHA-256 is
`363acd387f4517024300a6117162e2d668a90f6494d60ac7c0cb838f885e4f71`.
Its checksum companion also covers those 39 source files.

`final-artifacts.sha256` records 24 production/test DLLs and dependency copies
from this committed build. All compared copies of PSG, BAREWire, CCS, Alex,
PSG.Json and Composer agree. The two `Alex.Tests.dll` files are separate test
projects and retain distinct hashes. `final-artifact-binding.md` records the
paths and comparisons. The artifact manifest's SHA-256 is
`c0c1848c57d101aa20add85bf8f54bc887b57c0e5541c0d7d9e330d9cf039c2d`.

The source and artifact manifests bind this batch's executed evidence to its
committed build. They do not establish foreign ownership or lifetime proofs.

## Unfiltered acceptance

**E.** Each final suite ran once, unfiltered, from the committed build above.
Each acquired its own Bozzetto lease before execution and released it after
completion. No source changed within a running gate's dependency closure.

| Suite | Executed | Passed | Failed | Added cases, all passed |
| --- | ---: | ---: | ---: | ---: |
| Clef | 2,441 | 2,360 | 81 | 11 |
| Fidelity.PSG | 395 | 395 | 0 | 19 |
| Alex | 305 | 305 | 0 | 18 |
| Composer | 406 | 405 | 1 | 4 |

All 52 added cases executed and passed. There were no skips, missing cases,
new failures or resolved baseline failures. Clef's remaining 81 failures and
Composer's remaining failure match the trusted auditor result names and
outcome counts. The baseline was not rerun. The Composer case remains
`Result branch authority rebuilds its whole scope and rejects another revisions proof receipt`.
It stops at CCS8414 for an absent union payload before reaching its proof
receipt assertion. Phase B remains open.

| Gate | Lease | Start to finish, EDT | Exit |
| --- | --- | --- | ---: |
| Clef | `c445d9a617ee45ce8ac2b3a685404d2d` | 07:29:33 to 07:35:34 | 1 |
| PSG | `2c37f7b95ccb486e9cf1e2b6b1be3b59` | 07:35:49 to 07:35:56 | 0 |
| Alex | `3f3a2bd89c4844a9a3c36770efefdb60` | 07:36:13 to 07:36:14 | 0 |
| Composer | `970be5ae11ac42e495b2538b739e0aee` | 07:36:32 to 07:41:10 | 1 |

`final-gate-ledger.json` retains exact commands, timestamps and lease receipts.
The nonzero Clef/Composer exits report their retained baseline failures.
`final-results/final-{clef,psg,alex,composer}.trx` contains every executed case.
`compare-final-*.json` compares these with the auditor's retained TRX files
using `phase-b-regression/CompareTrxOccurrences.fsx`. The comparison uses
display-name outcome multisets. Clef has four duplicate-name groups, reported
explicitly, so individual transitions within such a group are not claimed.
`final-suite-summary.json` contains the compact counts.

After all suites, the source checksum companion verified 40/40 entries and
the artifact manifest verified 24/24. Results are retained in
`final-source-postgate-verification.txt` and
`final-artifact-postgate-verification.txt`. The four required follow-up items
have their executed exits and are ready for independent review. The
[handoff](FFI_Correction_Followup_Handoff_2026-10-04.md) identifies the remaining
scope limits and next controls.

**E, checkpoint push.** The four code commits above are pushed to `main`.
The spec commit was already pushed. Direct remote-head reads match all five
recorded identities, and the corresponding checkouts remain clean on `main`.
Receipts are `push-{psg,clef,alex,composer}.txt` and
`remote-{psg,clef,alex,composer,spec}.txt`. Other actors' Farscape changes were
excluded. No shared daemon or compiler distribution was switched.
