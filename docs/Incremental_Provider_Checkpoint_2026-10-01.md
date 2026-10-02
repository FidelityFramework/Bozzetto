# Cross-project incremental integration — 2026-10-01

This is the primary audit entry for the integration across
**Fidelity.FSharp.Incremental, Clef/CCS, Fidelity.PSG, Composer, Bozzetto and Calque**.
The auditor's remit covers these repositories and their ownership boundaries,
including Alex as a reader of the changed PSG contract. The retained
filename preserves links to the original provider checkpoint; detailed compiler
contracts remain in the linked sibling documents. This records source integration
and separately identified gate results. Deployment identities are recorded
explicitly; earlier source anchors do not establish installed runtime behavior.

The current [binary PSG transport handback](PSG_Transport_Integration_Auditor_Checkpoint_2026-10-01.md)
records the still-unintegrated service boundary and the coordinated repair gates.

**October 2 audit correction:** Forgejo `main` contains unreleased source.
The audited `3d9ff09a` integration head reached `main` at version `0.6.834`
without the release bump or complete local-gate receipt; the auditor assessment
then reached `main` as `7d7fc308`. The checkout had no installed pre-push hook.
Source publication is confirmed, but it does not establish a release or replace
the installed daemon. `scripts/install-hooks` now provides explicit checkout
setup; follow-up recovery commits use `integration/audit-followup-20261002`.
The next release still requires `scripts/ship` and its complete gate. See the
[independent assessment](Incremental_Pipeline_Auditor_Assessment_2026-10-02.md).

## October 2 audit follow-up: cooperative formatting and owned retirement

### Subsequent review: bounded evidence and worker supervision

The [follow-up review](Incremental_Pipeline_Auditor_Followup_2026-10-02.md)
identified unbounded formatter evidence and an overstatement about document
capacity. Formatter evidence now retains recent entries within **16 KiB UTF-8**,
including a persistent truncation marker. Whole older entries are evicted;
oversized entries are clipped at Unicode scalar boundaries before concatenation.
Malformed surrogate text is normalized for strict wire encoding. Reservation,
cancellation and close preserve the bounded evidence.

The precise capacity contract is: existing documents and new previews with spare
capacity can proceed during unrelated retirement; at **32 live/closing handles**,
replacement and other new-document requests receive `busy` until a physical close
releases a slot. The user selected whole-worker retirement when that cleanup
stalls. A **30-second monotonic deadline** produces sticky
`WorkerRetirementRequired` status, while `FormatterCleanupPending` continues to
describe actual ownership. Timeout does not reclaim a live handle or certify a
successful close. Late cleanup cannot rescind an issued retirement.

The daemon's existing status monitor observes cleanup even while the triggering
request remains in flight; its normal monitoring window renews while owned work
still requires supervision. On an exact-worker retirement signal, the supervisor
withdraws every session's authority and invokes the existing process-stop path:
socket shutdown, up to five seconds of graceful cleanup, then process-tree
termination and an exit check. Replacement requires actual process and transport
cleanup to join. Failed termination leaves the worker fenced and its terminal
status observable. The deadline path does not spend another ten seconds asking
an already-stalled worker to prepare for retirement.

The binary status schema now has digest
`F641CBC43607354163EACAE34D8A2A7BA904FCFBFC35295BFBEB73943C6CA9BC`;
daemon and worker must be deployed as a matching pair. Public JSON exposes
`formatterCleanupPending` and `workerRetirementRequired`. Physical formatter close
starts independently of compiler draining and has one retained observation, so
repeated provider close does not re-run that formatter join.

Calque's additional tests exercise Merge-phase interruption and last-demand
release during the actual Parse, Print and Merge pipelines. These are lifecycle
checks, not performance claims. Ranvier remains an architectural oracle: the
author's threading correction is preserved in the independent follow-up. Its
cross-thread settlements marshal through `Dispatch` into owned graph mutation,
which corresponds to the mailbox boundary. No dependency or benchmark campaign
was added. Content-keyed owner tokens, retained drained successes and owner-tree
scopes remain separate experiments with proof identity and physical drain as
acceptance requirements.

Source recovery points are Bozzetto `df74a727` on
`integration/audit-followup-20261002` and Calque `a5e7355` on `main`, both pushed
to Forgejo. The Bozzetto candidate was built from that committed source. Calque's
new commit changes tests only; the worker retains the already-reviewed production
formatter closure from `19099b5`.

Acceptance evidence is under
`~/.codex/work/bozzetto-resumption-2026-10-02/validation/audit-release-fixes/`:

| Gate | Executed result and evidence |
|---|---|
| Solution and worker builds | **Zero warnings/errors** (`provider-build.log`, `worker-build.log`); matching daemon/worker publications complete. |
| Calque full suite | **99/99 passed**, zero skipped/failed (`calque-tests.log`, `calque-results/calque-audit.trx`). Includes Merge-phase stops and last-demand release inside Parse, Print and Merge. |
| Bozzetto unfiltered default suite | **8,977 registered/ran; 8,974 passed, three existing ignores, zero failed/errored; Trusted** (`bozzetto-default-final.log`, `trust.jsonl`). Includes deadline escalation, failed-stop fencing, monitor renewal, the 32-handle boundary and strict-wire diagnostic bounds. |
| Complete Composer tier | **46/46 passed**, zero ignored/failed/errored; **Trusted** (`bozzetto-native.log`, `trust.jsonl`). Includes real worker and public-interface preview/reserve/apply/build/run journeys. |
| Worker closure and artifact integrity | **34** declared runtime assets, zero missing and zero test dependencies (`worker-assets.log`); daemon/worker protocol DLLs match. All candidate files pass the final SHA-256 check (`candidate-before.sha256`, `candidate-integrity.log`). |

`acceptance-receipt.json` records the source commit, exact artifact identities and
both TRUST rows. The candidate directories are
`~/.codex/work/bozzetto-resumption-2026-10-02/candidate/{daemon-audit-release-fixes,worker-audit-release-fixes}`.
Daemon SHA-256:
`d0bb50bb572b38cdc39e4a1860b39ee1deebcf403e8fcda13db0918eaa032bb6`;
worker SHA-256:
`a85b419ade24f7ba57c68c269545676cd156ccc19531213e9bf3fb41427fd08d`.

Two initial default runs stalled at an unprotected nested Expecto spinner in
`IntegrationRegistryTests`. Its child case completed while the parent and console
logging stopped. Applying the existing `No_Spinner` convention resolved the stall.
Those runs emitted no TRUST row (`bozzetto-default.log`,
`bozzetto-default-traced.log`, and their incomplete-run JSON records). Subsequent
whole-suite runs rejected two new test fixtures because their scratch was inside
the project directory. The fixtures now use separate sibling directories; both
whole suites were rerun successfully. The failed receipts remain under
`before-fixture-fix-` names.

All build/test leases are released. Installed release
`2026-10-02-calque-7d3557971d26-66c0b1bce8fe` and shared daemon PID **99247** remain
unchanged. Release publication still requires the complete release gate; these
receipts establish the pushed integration checkpoint and its candidate.

### Earlier repair receipt

Source recovery points are Calque `19099b5` on `main`, Bozzetto `15d3e022`
on `integration/audit-followup-20261002` (following workflow repair `371ce4b8`),
and Lattice `3ddea31` on `fidelity`. Each is pushed to Forgejo. The candidate
assemblies below were built from those final source trees before their commits;
the file hashes identify the executed artifacts independently of Git metadata.

The follow-up keeps the existing foundation's demand, cancellation and physical
drain contract. Calque now checks carried `WorkCancellation` within parser token
delivery, Oak/trivia/dialect walks, printing and output/conditional merging.
Source preparation and the initial parse are cold. Withdrawing the last demand
unwinds at the next checkpoint; started conditional branches still join, and an
independent fault remains a fault. Replacement of the same work follows physical
drain. A peer demand keeps shared evaluation alive. This introduces no scheduler
or actor library; the ownership contract remains portable toward native hosting.
Single lexer tokens and intervening atomic helpers are still cooperative latency
limits. Full-document formatting remains in place; incremental syntax reuse and
measured editor latency are separate work.

Bozzetto now retains formatter faults in `FormatterError`, separately from
compiler `BackendError`, through reservation, cancellation and close. Public JSON
uses `formatterError`. The binary status layout changes its exact schema digest
to `6A52AB4179B0E1880AD4BB21667A83D3070F8CFF67CA380B24EB23CD1E873239`.
Daemon and worker must be deployed together; their mandatory Hello agreement
rejects the earlier schema despite retaining framing version 2.

Replaced document incarnations become owned cleanup children. The provider
registers them under its existing control boundary; previews that fit within the
document limit do not wait for unrelated cleanup. At capacity the retry contract
and subsequent deadline policy above apply. A handle is reclaimed only after its physical close
returns, with faults retained and an identity tombstone preventing resurrection.
Failed starts follow the same cleanup path and can be retried after joining.
Lattice keeps one document ID per client/file while assigning a fresh incarnation
on reopening. Current previews withdrawn by another session operation now show a
retry hint; locally obsolete observations remain quiet.

The [adoption contract](Bozzetto_Incremental_Foundation_Adoption.md#functional-async-authoring-and-native-execution)
records HelloWayland's existing native Ariel region as ownership evidence and
keeps a hosted actor/scheduler library deferred. Closing an editor view does not
yet retire shared document state: that requires explicit consumer ownership.
The 32 **distinct live document** limit remains; this repair reclaims replaced
incarnations. The 8,192 admitted incarnation limit bounds formatter identity
tombstones. Shared Incremental history within a long-lived document still needs
an epoch retirement policy; no library compaction or general idle eviction was
added.

Acceptance evidence is under
`~/.codex/work/bozzetto-resumption-2026-10-02/validation/audit-followup/`:

| Gate | Executed result and evidence |
|---|---|
| Calque build and full suite | **95/95 passed**, zero failed/skipped (`calque-build-final.log`, `calque-tests.log`, `calque-results/calque-audit.trx`). The build retains four inherited parser serialization warnings. New controls interrupt inside all five major traversal phases, withdraw actual parser/printer work, preserve peer demand, join replacement and retain independent faults. |
| Bozzetto full build and default suite | Build: **zero warnings/errors**. **8,970 registered/ran; 8,967 passed, three existing performance-budget ignores, zero failed/errored; Trusted** (`provider-build.log`, `bozzetto-default.log`, final default row in `trust.jsonl`). Includes document-slot reclamation after close, failed-start recovery, unrelated preview progress during held cleanup and protocol/status controls. |
| Complete Composer tier | **44/44 passed**, zero ignored/failed/errored; **Trusted** (`bozzetto-native.log`, final Composer row in `trust.jsonl`). Includes executing mid-flight Format-token withdrawal, peer observation survival, retained formatter diagnostics and the real preview/reserve/exact-base-apply/build/run journey through worker and public MCP/HTTP surfaces. |
| Lattice client | **103/103 passed**, zero skipped (`editor-unit.log`). Real VS Code **1.139.1** acceptance passed against the preserved installed daemon (`editor-host.log`, `~/.cache/lattice-vscode/composer-host-vpd4Se/result.json`); the runner's `closed-status-0.json` records joined session cleanup. This editor receipt does not claim the new worker was installed. |
| Publication guard and worker closure | Installed pre-push hook refuses the unchanged `main` version (`main-hook-refusal.log`); integration recovery pushes succeeded. All **34** declared worker assets are present, with **zero** test dependencies (`worker-assets.log`). Daemon/worker protocol DLLs match. All candidate files passed the final SHA-256 check (`candidate-before.sha256`, `candidate-integrity.log`). |

The first Calque run was **93/95**: two new parser controls exposed the inherited
lexer's `WrappedError` around the exact checkpoint exception. The correction
unwraps only that invocation's recorded inner exception. An unrelated exception
of the same type still becomes a host diagnostic. The failed log/TRX and earlier
successful provider gates are preserved with `before-parser-fix-` names (the TRX
is `calque-results/before-parser-fix.trx`). Both provider tiers were rerun against
the corrected closure. The initial queue-name compile error is retained in
`provider-build-initial-failure.log`; its correction explicitly selects the CLR
queue type rather than the repository's persistent queue.

The final candidate lives at
`~/.codex/work/bozzetto-resumption-2026-10-02/candidate/{daemon-audit,worker-audit}`.
Daemon SHA-256:
`27adde9ce2ea886aa3c6bae9d11e1ecf1743b08882532ad23655d319dec1ad56`;
worker SHA-256:
`3d2ed2edff1c445add392770f444ce2bd99c5cefac6814f90fce618cd572d1ba`.
The manifest records the complete closure, including corrected Calque DLLs.
This candidate has **not** replaced installed release
`2026-10-02-calque-7d3557971d26-66c0b1bce8fe`; shared daemon PID **99247**
remains unchanged. All build/test leases were released. Release publication and
installation remain separate from these pushed source recovery points.

This addresses audit findings 2, 3, 7 and 9; narrows formatting latency in 1;
corrects the contract wording in 4; and partially repairs 8. The Composer
stale-proof test gap (5), Clef config-restore assertion (6), general document
retirement policy and the remaining minor findings stay open. The independent
assessment remains unchanged as evidence for its audited revisions.

## Earlier October 2 resident formatting and editor preview

Bozzetto `dcbe57a7`, pushed to `integration/calque-incremental-20261002`, removes
the formatter adapter's reliance on a task's eager prefix. `RequestPreview`
accepts an exact demand under the provider generation lock; its owner then runs
a cold F# `Async` observation. New generations withdraw registered demands.
Transport cancellation now reaches the exact worker-side Format request, using
the same cancellation path as Build and Run. It does not cancel a peer request
for the same buffer. Release controls and physical document work remain owned
through close, including demands never observed by their caller.

The shared foundation already implements publication withdrawal, carried
`WorkCancellation`, peer-demand preservation and evaluator/callback joins.
Current Incremental `d476aea2` differs from the pinned preview.6 implementation
`3b86e2d` only in documentation. A fresh stable-SDK build and unfiltered NUnit run
passed **108/108**, zero skipped. This confirms the library contract, not
cooperative interruption inside every consumer: at that checkpoint Calque's
active full-document parse/print ran to completion after its output was withdrawn.
The later audit follow-up above adds cooperative checkpoints.

Lattice `5066006`, pushed to its `fidelity` branch, adds **Select Composer Session**
and **Preview Calque Formatting**. An explicitly demanded preview captures the
unsaved buffer, validates the full response identity and source digest, and
presents immutable original/formatted documents. Source edits, editor changes,
session changes and cancellation discard pending presentations. Expected
supersession is quiet; genuine backend failures remain visible. The full client
suite passed **90/90**, zero skipped. Real VS Code **1.139.1** acceptance against
the installed 13:48 daemon passed unsaved formatting, delayed stale-response
rejection and unchanged disk checks; disposal preserved the shared session, and
the runner subsequently closed its own session with completed cleanup.

The new Bozzetto candidate built without warnings/errors. Its complete Composer
tier passed **42/42**, zero ignored/failed/errored, `Trusted`, including native
build/run and owned live MCP/HTTP checks. The candidate daemon SHA-256 is
`c6fcef3dca57cfaf2be38ad048dc1c27559dce257232b316b8cebc73cbcfdb82`;
the worker is
`f7939720ffee5be079bd01640b338f0cb431ebbe2fcdae7873c7409cd2cecace`.
All candidate files remained unchanged through acceptance. This candidate has
**not** replaced the shared daemon recorded below.

The final unfiltered default suite ran all **8,962** registered cases:
**8,959 passed**, three existing performance-budget ignores, zero failed/errored,
`Trusted` (`bozzetto-resident-default-final.log`). The first run is preserved in
`bozzetto-resident-default.log`: one unchanged cache test compared a single cold
parse (0.474 ms) with a single hit (2.935 ms) during parallel execution and failed.
Its test-only correction retains timing diagnostics and requires exact parsed
object and immutable-cache reuse instead of one-sample wall-clock ordering.
The test project was rebuilt without rebuilding dependencies, then the whole
default suite ran again; the native candidate and its tested implementation
remained byte-identical. Both the failure and the subsequent `Trusted` receipt
remain in the ledger, with `resident-default-final-runner.sha256` identifying
the corrected runner.

Evidence is under `~/.codex/work/bozzetto-resumption-2026-10-02/validation/`:
`resident-incremental-tests.log`, `resident-editor-unit.log`,
`resident-editor-host-cleanup.log`, `bozzetto-resident-native.log` and
`resident-provider-trust.jsonl`. The real editor receipt and joined session close
are under `~/.cache/lattice-vscode/composer-host-dGKaEs/`. The native tier's exact
test assembly identity is `resident-native-runner.sha256`; the later default
runner differs only by the parser-cache test correction recorded with its run.

The editor still uses a separate Lattice CCS connection for proof checking.
Explicit Format/Save/Build remains the agreed next editor operation, with
competing saves allowed to invalidate the build. This checkpoint adds preview
and repairs its work ownership; it does not implement that command, automatic
format-on-save or a compiler-owned unsaved-buffer save transaction. The
[adoption contract](Bozzetto_Incremental_Foundation_Adoption.md#cold-work-and-incremental-reuse)
and Calque's [design note](../../Calque/docs/design.md#incremental-formatting)
record the cancellation boundary and the later cooperative checkpoint repair.

## October 2 source integration: Calque and artifact accounts

The resumed .NET-hosted path now includes `Calque.Incremental` and the public
`composer_format_preview` operation on the existing versioned binary provider
protocol. Calque retains exact document incarnation, revision, source and policy
inputs through the shared preview.6 functional mailbox. Formatting is immutable
preview data: it does not reserve an edit, withdraw an accepted artifact or grant
proof/execution authority. Clients reserve successfully before saving/applying
source, compare current source with the exact buffer supplied for preview and
verify its digest. The client retains that immutable base text; the wire result
returns its digest rather than another copy of the source.

Provider generation checks now cover both actual formatter invocation and reply
publication. A controlled regression holds a queued generation-0 revision-4
preview, reserves generation 1 and admits revision 3, then proves the obsolete
request never enters Calque or displaces the valid lower revision. Provider close
seals admission, joins retained preview tasks and document hosts, and preserves
late diagnostics. The native and public MCP journeys exercise preview, successful
reservation, exact-base apply, build and `run_current`; their gate receipts below
distinguish compiled tests from executed acceptance.

The PSG artifact-account draft is now generated as **schema 16**, binary format 2,
with 251 named types and fingerprint
`7D92113F623A60402739AE33757DD07F6CB54191BBE9B4A6231A1DF3D7FF1638`.
Reservation, factory-result, residence and initialization-order maps are carried
through final Clef codata and read passively by Composer. Integrity validates the
canonical residence and its alias participants against their owning published
instances. These typed source facts do not contain controller/runtime handles.
Full PSG transport across the service boundary and controller realization remain
the separately identified work in the transport handback.

Calque's implementation root is `48c77a607f111e7ec698ee5668f2d06e029fde4f`;
documentation cleanup is published as `07f6276` on `main`. Other published source heads are
PSG `e99010e631d196d23aa7d303a22bf06e10466a35`,
Clef `f1dc69ac7a711c4dc3eeb81e9034e07ddf465eb3` and
Composer `0287b744d3dd8af4dc67a6f101dd0d9bf50d6f84` on Forgejo `main`.
Calque starts with one parentless commit of its thin retained implementation,
at the owner's request. License and upstream attribution remain; the former
local Git metadata is archived outside the repository under the evidence root's
`publication/calque-upstream-metadata/fantomas.git`. Its canonical remote is
`ssh://hhh@forge.spkez.dev:2222/FidelityFramework/Calque.git`.
Bozzetto source was initially published from base `da1f3604` on
`integration/calque-incremental-20261002`. The earlier `92e54246` checkpoint
included worker metadata and shipping/lease repairs. Its successors through
`3d9ff09a` later reached `main` without the release gate, as corrected above.
Fresh builds and the default suite passed after the machine reboot; the Composer
tier and installed replay are tracked below. Unchanged Alex and Incremental heads are
`4e1f859d` and `d476aea2`.
The original pre-publication candidate predates Calque's repository-metadata
correction. Its replacement under `candidate/daemon-corrected` and
`candidate/worker-corrected` was built with stable SDK **10.0.401**, Bozzetto
`89fcad86` and Calque's published implementation root. Composer's existing product
DLLs were retained and tested, rather than claimed as newly rebuilt compiler
sources. Both selected closures contain foundation core SHA-256
`27b7a4d6449387529d0f415d13d3e656ef598697261868751f893de9dce44c81`
and Hosting SHA-256
`179f724c381241e4a102a32c92d5984ba60835b921a4c90c66f5f8263c528c11`.
The build rejects unequal foundation bytes.

Evidence is external under
`~/.codex/work/bozzetto-resumption-2026-10-02/validation/`:

| Whole gate | Executed result |
| --- | --- |
| PSG schema-16 Release suite | **307/307 passed**, including 37 new artifact-account controls (`psg-schema16-tests.log`). |
| Clef official mapper generation check | **218 structural mappers checked** against the fresh compiler and schema-16 contract (`clef-schema16-mappers.log`). |
| Calque Release build and unfiltered suite | **83/83 passed**, zero skipped, against the published implementation (`calque-published-build.log`, `calque-published-tests.log`). Build: four inherited serialization warnings, zero errors. |
| Bozzetto solution and optional worker Release builds | **Zero warnings/errors**, using Composer's product distribution and freshly published Calque closure (`bozzetto-corrected-build.log`, `worker-corrected-build.log`, `corrected-host-build-final.log`). |
| Bozzetto default suite | **8,950 registered/ran, 8,947 passed, three performance-budget ignores, zero failed/errored; Trusted** (`bozzetto-corrected-default.log`, `corrected-provider-trust.jsonl`). All seven preview controls and the bounded retired CLI refusal ran. The wrapper's anchored text search failed on an ANSI colour prefix after the suite passed; the structured trust ledger records exit zero. |
| Composer schema-16 Release suite | **385/386 passed**, zero skipped (`composer-schema16-tests.log`). Exact failed-case comparison recovered eleven environment failures and introduced none. Callable `Result` transport at occurrence 218 remains refused; its exact node kind is not yet established. |
| Whole Bozzetto Composer tier | **41/41 passed, zero ignored/failed/errored; Trusted**, repeated against the corrected published closure (`bozzetto-corrected-native.log`, `corrected-provider-trust.jsonl`). The real worker journey retained the unchanged callable's actual object path/digest and witness with zero visits, compiled the changed callable and ran changed output. The public MCP/HTTP journey exercised formatting, reservation, exact-base apply, cancellation/recovery and native execution. |
| Unchanged Alex reader Release suite over schema 16 | **278/278 passed**, zero skipped (`alex-schema16-tests.log`). |
| Clef final unfiltered Release suite | **2,212/2,311 passed, 99 failed, zero skipped** (`clef-schema16-final-tests.log`). The class/display/outcome comparison preserves all 2,304 baseline cases with their multiplicities and the same 99 failed identities; all seven additions passed, zero missing cases/new failures/recoveries (`clef-schema16-final-case-preservation.log`, `clef-schema16-final-comparison.log`). Full compiler acceptance remains red. The first run's added alias-use test had an incorrect premise-owner oracle; it was corrected to require exactly the removed and added target owners. |

The external report wrappers initially exited after successful tests: the default
wrapper's anchored search missed an ANSI prefix, and the native wrapper selected
`composer` instead of the recorded tier `--integration-composer`. Both checks were
corrected; the structured ledger and `corrected-gate-postchecks.log` confirm the
actual test results and unchanged candidate/installed bytes. Tests were not rerun
solely to repair report parsing. `ci-pipeline-compile-help.log` also confirms the
shipping script compiles after the `92e54246` syntax correction; it is not a full
release-pipeline receipt.

The original candidate is beside `validation/`, under
`~/.codex/work/bozzetto-resumption-2026-10-02/candidate/daemon` and
`candidate/worker-product`; the latter contains the compiler product closure,
Calque and the shared foundation, with no test-runner assemblies in that
directory. A subsequent deployment audit found that its dependency metadata
still named 18 absent test runtime assemblies and 65 absent localized resources
inherited from the earlier selected test distribution. The SDK omitted external reference selection from its
incremental dependency-file inputs. `RecordSelectedWorkerClosure` now records
the selected distribution paths and resolved-reference hashes as an explicit
input. The replacement's distribution-switch regression passed under a build
lease: test distribution to older product DLLs in the same output directories,
then an unchanged product rebuild. All **34** declared published runtime/resource
assets exist, with **zero** test dependencies; unchanged selection preserved both
stamp and dependency-file timestamps (`worker-closure-regression.log`). The old
candidate must not be installed. Publication must follow a successful
build against those same selected distributions: ordinary `--no-build` publish
copies the build dependency file rather than regenerating it. The original candidate's exact
files are recorded in `calque-candidate-before.sha256`; all **211 files** matched
after the complete Composer tier (`calque-candidate-after.log`). Live public
evidence is in
`~/.cache/bozzetto/live-provider-tests/25657804b4f14d14b0cbbefb9d9aad7e/`, including
`017-mcp-composer_format_preview.json` and the subsequent native run replies.
The owned live daemon was PID **1813858**, API 4, with a ten-minute TTL and
reserved ports; it exited after the test. All build/test leases were released.
The shared daemon replacement is recorded below. PID 215237 belongs to the
previous boot; PID 62482 was the restored old deployment, stopped gracefully
after confirming no sessions, worker or active work leases.
Full-document parsing remains current; incremental syntax/trivia reuse,
markup, cooperative parser interruption and measured editor latency are open.

Installed primary assembly SHA-256 values:

| Assembly | SHA-256 |
| --- | --- |
| Bozzetto | `7d3557971d264b0ace288d6098ea035b90d6cdc7ca1a6f0e654bf682f5aef1fb` |
| Bozzetto.Composer | `66c0b1bce8fe606fe3abd9f9e05a684a76ff94bc8b8d9ef0301195075c44380f` |
| Composer | `7437b5ac7f55e90763e22b981c843d5b256a227a380ede8106e21baaa5774df4` |
| Clef.Compiler.Service | `6c16e09c9e6fc9b5c03dba0bcbf4c3200bef79f5c7f20ae61a257b28a9698e34` |
| Fidelity.PSG | `3ade77a33fa6b5466d575d5803de735d0860922a35f586f2b55fb016bdf80c52` |
| Calque.Incremental | `4b5cc96c068014192b4da3922d3f329397811fbfd46bf3596ef650d6b10966a6` |
| Calque.Core | `abbcbfc9389e0626555391e97783c76aaf3733ba9affc6786c0323765f3fcbdd` |
| Calque.Syntax | `7893d30f67684577abaf94c9c2355e7830a5b778ff0756bb912e5de0031bb27d` |

The daemon and worker's protocol assembly also matches byte-for-byte:
`b4432e696f97c56591ebff105280cbfaab852be8e0c892de301eb7c24e1493e7`.

### Installed Calque provider — October 2, 13:48 UTC

The previously approved shared replacement now runs as **PID 99247**, started
**2026-10-02T13:48:52.8185111Z**, API **4**, on 47749/47750 from the dedicated
external workspace. Bounded identity, health and Composer configuration probes
passed. `/home/hhh/.local/bin/boz` selects the immutable release directory
`~/.local/share/bozzetto/releases/2026-10-02-calque-7d3557971d26-66c0b1bce8fe/`.
It contains both exact tested closures, `acceptance.jsonl`, `provenance.json`, the
previous launcher for rollback and `manifest.sha256` (SHA-256
`d128d37bbc12a20330aa75a40a9c02ae98f10cce2968528a88a619bc08e2967c`).
Staging compared every file to the tested candidate. Startup evidence is
`replacement-start.json`. The public MCP replay in
`installed-format-replay.qKATkcyr/summary.json` passed preview, reservation,
exact-buffer comparison, save, build and `composer_run_current` for both initial
and edited source. Output changed from `stable\nbefore\n` to `stable\nafter\n`.
The unchanged callable retained its actual object path and SHA-256, its witness
was retained with **zero visits**, and the changed callable was compiled. The
replay's session closed with no pending cleanup/error; its test-suite lease was
released. `installed-after-sessions.json` records the live worker's `format`
operation and PSG schema-16 contract; `installed-composer-page.png` records the
browser page with the replay session closed. This deployment does not establish clean
full-language acceptance or completion of the full release pipeline.

The next editor step remains distinct: Lattice currently checks unsaved buffers
in its own CCS session while Bozzetto builds saved source. The existing
[editor direction](Bozzetto_Editor_Workspace_Direction_2026-09-30.md) specifies
explicit reserve/save/build before shared compiler-owned overlays. A formatting
preview can be added independently; applying it must coordinate source versions
with that save/build workflow. No shared unsaved-buffer compiler session or
automatic format-on-save integration is claimed by this installed replay.

Retrieval configuration has been published in speakez-lab
`0b5dde1cd44195f00b483fd749159e122302ed45`. Its scoped seventeen-source rollout
adds Calque and Bozzetto within the unchanged 32 MiB budget; 97 checks passed.
The live existing fifteen-source corpus already observes the published PSG,
Clef and Composer heads with exact-commit citations. Seventeen-source activation
still requires the reviewed passworded operator step and a refreshed rollout
check against the now-published Bozzetto source. The staged offline proof uses
the prior Bozzetto `da1f3604` head, not this integration branch.

The next callable `Result` observation plan is external under
`validation/callable-result-observation-plan.md`. Source review identifies
callable DU payload extraction and conditional transport candidates, but the
actual kind and witness caller for occurrence 218 remain unobserved. The next
probe must retain that exact source/published occurrence and the direct selective
witness exception; a generic pointer representation is not a repair.

## Minimal host and aligned compiler checkpoint

The dedicated [auditor checkpoint](Bozzetto_Minimal_Host_Auditor_Checkpoint_2026-10-01.md)
records the architectural correction, deletion scope, retained controls and
reproduction guidance.

The [minimal hosting architecture](Minimal_Hosting_Architecture.md) defines the
remaining host responsibilities and the obligations a native implementation
must preserve. Runtime method patching, its owned fork, package, production
host, browser injection, client controls and patch-only tests are removed.
There is no optional patching mode. F# implementation work uses `dotnet build` and
the unfiltered test suite; Composer retains native artifact and launch authority.

The default gate registered and ran **8,957 tests: 8,954 passed, three ignored,
zero failed or errored**, with a `Trusted` receipt. The three ignores are
unimplemented performance-budget checks, not ignored correctness failures.
The count decreased because tests for the removed feature were deleted; it is
not a claim that the two suites have identical coverage. The first purge run's
13 failures and two errors, then their corrected contract assertions, remain
in the external evidence. Watcher, session, authority, cancellation and joined
cleanup checks remain. The compiled dependency guard rejects transitive
Harmony/MonoMod references.

The daemon/test Release build passed with zero warnings or errors. VS Code's
Fable compilation, bundle and golden checks passed. The public daemon API is
version 4; the active VS Code adapter checks that version. The external Neovim
client has not been validated against it.

Running the entire Composer tier against the previously installed worker
(`4229c332`) produced **38/40 passes**, one failure and one error. Its first
request consumed the reservation, preventing shared build observation; it
also omitted the status observation sequence now required by the daemon.
Saved resource evidence explicitly names that missing sequence. Neither
regression was weakened. The fresh, aligned Release compiler and worker then
passed the entire tier: **40/40, zero failures, errors or ignores; Trusted**.
That includes real native object reuse and shared MCP/HTTP authority. The
source is unchanged between the failed old-worker run and the aligned run.

The standalone shared-daemon launcher now uses Bash. Thirteen controlled
launcher cases passed, covering dedicated workspace placement, existing
listeners, exact process identity, startup failure and bounded readiness.

The installed daemon now runs API 4 on **CoreCLR 10.0.12**. The fresh installed
MCP/HTTP workflow passed cold, unchanged and one-function-edit native runs;
reservation refused the old artifact before writing, and the stable function
retained its object with zero witness visits. Owned cleanup and lease release
completed. Exact assembly/closure hashes and the bounded replay results are in
the dedicated auditor checkpoint. Twenty-two retired binary/cache roots were
removed after shutdown; no patching assemblies remain in the checkout or the
installed Bozzetto releases.

Clef `ba694e1` passed **2,156/2,255**, retaining the identical 99 failures with
one passing addition and no missing cases. Composer `723e900` over implementation
`c1e3ff6` passed **385/386**, the same callable-Result failure with all five added
ownership checks passing. The compiled editor run passed 30 checks, including
all six failure-classification controls, then stopped at the source-only encoding
check with two CCS8011 range diagnostics. That last check remains unresolved;
the preceding selected encoding refusal/repair controls executed successfully.

The published contract remains **Fidelity.PSG schema 12** (`e2effe3`), with
Alex `9d53b9e` and Incremental Hosting **preview.6**. The unintegrated binary
codec/control-channel drafts are not part of this compiled closure. Actual
PSG revision transport remains open work; a handshake is not graph transport.

The bounded [compiler authority review](../../Composer/docs/Compiler_Authority_Boundaries_2026-10-01.md)
also identifies MCU image resolvers reading CCS structures and project options
after checking. Baker-published image facts and captured backend inputs are
the required repair. The exercised incremental project-session path admits
CPU targets and retains capture validation; this review is not MCU acceptance.

Receipts remain outside Git under
`~/.codex/work/incremental-audit-repairs-2026-10-01/validation/`:
`bozzetto-purge-tests-build-r6.log`, `bozzetto-purge-default-r2.log`,
`bozzetto-purge-trust-r2.jsonl`, `bozzetto-purge-vscode-golden.log`,
`bozzetto-purge-provider.log`, `purge-aligned-provider.log`,
`purge-aligned-provider-trust.jsonl`, and `aligned-compiler-publish.log`.

## Earlier audit repairs and stable runtime source checkpoint

This commit records the next source-control anchor. Compiler and transport
contracts remain fail-closed: a Git checkpoint is not runtime fallback behavior.
The two [audit findings](Incremental_Cross_Repo_Fallback_Auditor_Assessment_2026-10-01.md)
and [backlog guidance](Incremental_Compiler_Backlog_Auditor_Guidance_2026-10-01.md)
remain unchanged as independent evidence.

| Source | Change and executed evidence |
| --- | --- |
| Clef `794b302` | Cached publication now includes the exact sequence-authority root. Full compiled suite: **2,155 passed / 2,254**, the identical **99 failures**, all seven additions passed, no missing cases or new failures. |
| Composer `c1e3ff6` | Exact Task terminal classification retains faulted cancellation exceptions. Failed native/session construction releases ownership before GC; outer construction joins cleanup. Sage controls: **6/6** classification and **5/5** construction. Compiled regression comparison remains pending. |
| Bozzetto, this commit | Never-written withdrawals consume no late-reply slot; idle-monitor exit rechecks pending demand. Persisted-source Sage controls **32/32**. Session-status serialization uses the existing explicit F# wire codec; three constructor/path controls passed. |
| Runtime delivery | Stable SDK **10.0.401**, `net10.0` targets, test paths, packaging checks and regenerated locks. Bozzetto compiled with **zero warnings/errors**. First default run: **9,789 passed, five status-serialization errors, four ignores**; the five errors are repaired in source, final default/provider runs remain pending. |
| Harmony `528579dd` | .NET 10 patch/unpatch probe passed. Immutable package `2.4.2-bozzetto.2`, SHA-256 `b988bfafc3e75e660c16b60757563a7fe337975ca99ee01378b4766ad28a8b46`, imported through the controlled refresh script. No publishing remote is configured for this owned source fork. |

Program-lifetime fixture declarations now state the required representations and
synthetic FPGA timing facts; its complete source-loaded projection passed. The
string fixture now declares writable storage and admits valid input. Its invalid
byte test still exposes two downstream CCS8403 diagnostics alongside CCS8404;
the exact-one assertion is retained and the source-admission repair is in progress.
PSG schema 12 and Incremental preview.6 are unchanged at this anchor. Binary
worker/PSG transport work is separate and is not included or claimed validated.
The installed daemon is still the previous runtime; promotion requires the
reviewed compiled closure and a fresh live-interface check.

Raw receipts are outside the repositories under
`~/.codex/work/incremental-audit-repairs-2026-10-01/`; `validation/clef-comparison.json`
contains the exact comparison. Historical runtime receipts below retain their
original identities. The recipe uses the current stable runtime.

## Earlier integration anchor

The common library is `Fidelity.FSharp.Incremental.Hosting`
**0.1.0-preview.6**. The table below pins a pushed source checkpoint across all
affected repositories. Full regression comparison and the final compiler/provider
closure remain open validation work; they do not postpone this Git checkpoint.
The repaired Bozzetto default and entire Composer integration tiers are now
`Trusted` against the frozen `compiler-preview6-c1-c4` closure. That closure
predates the Baker repair and PSG schema 12; final aligned-closure validation
remains separate.

## Cross-project scope and identities

| Repository | Recorded integration identity | Ownership |
| --- | --- | --- |
| [Fidelity.FSharp.Incremental](../../Fidelity.FSharp.Incremental/docs/Consumer_Integration_Checkpoint_2026-10-01.md) | `d476aea`; preview.6 implementation `3b86e2d` | Pure dependency/eligibility transitions, exact admitted-operation observation, owned evaluator/cancellation-callback lifetime and physical close. No compiler semantics, proof or launch authority. |
| [Clef / CCS](../../clef/docs/Incremental_Project_Workspace_2026-10-01.md) | `6a5c836` | Immutable captured project inputs, shared whole-project checking, process-global checker-state ownership through projection, Baker semantics and settled PSG publication. Each reserved generation still gets fresh checking. |
| [Fidelity.PSG](../../Fidelity.PSG/AGENTS.md) | `e2effe3`, schema 12 | Immutable typed correspondence for bounded sequence-pull composition. No source inference, proof derivation or scheduling. |
| [Composer](../../Composer/docs/incremental-compilation/2026-10-01-workspace-adoption.md) | `6440afd` | Source/proof/target/input receipts, compiler reservation, native artifacts and reuse, actual process launch, process/output/lease cleanup; editor and CLI consumers join CCS ownership. |
| Bozzetto | `fd10e283` implementation; this document records the synchronized anchor. | Explicit session and wire authority, same-ticket shared demand, caller detach, reservation acknowledgement, compiler replacement and worker supervision. One mailbox per provider session; separate opens stay separate. |

Checkpoint evidence: library **108/108**; PSG **103/103**; the compiler's focused
finite-sequence/loop/freshness cohort **52/52**; Bozzetto default **9,788 passed,
four ignored** and its entire Composer tier **40/40** against the named earlier
compiler closure. Canonical generation exactly reproduces the integrity traversal
and all 206 publication mappers. The final editor run passes 23 groups, then
stops at program-lifetime CCS8403: missing `string.Bytes` borrow lifetime and
guarded-comparison memory-access/bounds evidence. Later editor groups are unrun.
Ordinary branch-local lazy-demand activation and callable-`Result` native
transport remain named compiler gaps. Their presence is not full compiler/editor
acceptance, nor a reason to leave the integrated source unpushed.

These identities distinguish implementation, documentation and validation inputs.
Further validation must identify the actual compiler/provider binary manifests.
Independent audit is evidence for an identified revision and scope, not a frozen
library contract. All affected repositories remain within the auditor's remit. If
integration exposes a defect in the shared contract, repair it in
Fidelity.FSharp.Incremental and validate its consumers against the resulting
version; do not preserve a weaker contract merely because it was audited before.

The coordinating runner owns dependency alignment across Clef's package reference,
Composer's compiler/distribution closure and Bozzetto's central package version,
vendored packages and lock files. Any changed shared package requires a new
immutable version, matching hashes and renewed library and consumer receipts.

## Auditor priorities

Read each repository's owner instructions and inspect these boundaries across
callers and owners, using the concrete tests in the audit map below:

1. **Reserve before writing:** require acknowledgement of the exact reservation
   before source/editor mutation. Withdrawal must not wait behind evaluator
   saturation, and a notification or status refresh is not that acknowledgement.
2. **Actual launch:** follow eligibility and input/artifact revalidation through
   the owning reservation gate to `Process.Start`. Check both reservation-first
   and launch-first orderings; queued invocation alone grants no launch authority.
3. **Shared checker state:** prove all relevant CCS paths share process ownership
   through checking and projection, restore configuration on failure, and preserve
   a second demand when the first consumer withdraws. This is not cross-process
   or cross-session sharing.
4. **Proof and result authority:** require the matching library result token and
   eligibility plus the compiler's current source, proof and artifact receipts.
   Baker/Publication/Alex responsibilities remain unchanged; stale-proof refusal
   must actually execute before it can count as a passing assertion.
5. **Immutable capture:** inspect the exact metadata/source/platform bytes used
   by checking, normalized override conflicts and same-generation re-observation.
   A later disk read must not silently change an already captured generation.
6. **Physical drain:** hold capture, checking, cancellation callbacks, process
   exit, stdout and stderr independently. Close must retain every owner and lease
   until real completion, while preserving failures from withdrawn work and
   distinguishing checker diagnostics from cleanup failures.

Report counterexamples and failures at the owning source contract. Record actual
executed tests, evidence and closure identities; do not infer full acceptance
from the library audit, focused passes, test presence or a baseline comparison.

## Dependency identity

The library implementation commit is
`3b86e2dac96ad55cb965341bfc04395061d09c46`. Both preview.6 package nuspecs record
that full source commit. Clef, Composer and Bozzetto carry identical archives;
the central Bozzetto package pin and Clef reference select preview.6. Explicit
Composer distribution checks and retained packages under
[`vendor/Fidelity.FSharp.Incremental`](../vendor/Fidelity.FSharp.Incremental/README.md)
identify the dependency. Exact archives are listed in
[`SHA256SUMS`](../vendor/Fidelity.FSharp.Incremental/SHA256SUMS):

- Core: `cc017440e0fadd177b3ba8cc2ab0f809aa754728b52f45310d998271af9562f9`.
- Hosting: `1afaa709250d578b14bea8fa15a59ed8a227f86f003e3653017106db775522e5`.

Preview.5 introduced `AsyncMailbox.watch`: a published snapshot and its next change
notification are captured together. Observers cannot lose a change between
reading state and subscribing. A notification itself grants no result or effect
authority. The provider checks the matching result token and `isEligible` before
publishing successful producer metadata.

Preview.6 adds the shared `ClrInterop.fromUncancelledTask` boundary needed by
the Lattice proof owner. With an uncancelled owner workflow, its cold factory
joins the exact returned task, preserves the original fault and permits cleanup
after task cancellation. The task still owns its children and physical cleanup;
cancellable observers remain separate. All **108 library tests passed**,
including six new bridge controls. Integration required this shared boundary, so
the package was revised and validated again; the earlier independent audit still
applies to its recorded revision.

## Implemented provider contract

- `IProjectBackend<'Ticket>`, `ProviderSession<'Ticket>` and `Worker<'Ticket>` keep
  compiler tickets typed and process-local. JSON reservation strings identify
  provider-owned tickets; clients cannot reconstruct compiler authority.
- Concurrent builds for one session and reservation share one compiler producer.
  Each pending caller owns a separate library demand. A completed current ticket
  can be observed again without another compiler invocation. Separate `open`
  requests still create independent sessions, even for the same project path.
- Caller cancellation, including private `cancel_request`, detaches that caller's
  observation and releases its demand. Another demand keeps the producer alive.
  A pre-canceled request never enters the compiler or revokes the generation.
  Releasing the last pending demand abandons that ticket; retry requires a new
  reservation. Explicit `cancel` continues to revoke the generation.
- A refused run reconciles the compiler's typed current-artifact state outside
  the provider gate, under the invocation fence. If the matching artifact was
  withdrawn, the provider clears current metadata and its retained build ticket,
  committing library scope withdrawal before returning the refusal. A retained
  successful observation rechecks eligibility. An ordinary refusal with compiler
  authority still intact preserves that authority; late failures cannot withdraw
  a newer reservation.
- Reservation first withdraws provider authority and admits library scope
  invalidation. Its receipt waits for committed invalidation and the synchronous
  Composer `Reserve` call. Compiler reservation is independent of evaluator slot
  availability. The shared invocation fence serializes reservation with native
  artifact selection and launch; Composer remains the final artifact authority.
- Each producer has a scope identifying its physical drain. The provider awaits
  evaluator completion and cancellation callbacks before settling its outcome.
  `BeginClose` immediately retires authority. `CloseAsync` joins the mailbox,
  outstanding control operations and outcome projections before backend disposal.
  Provider `IDisposable.Dispose` synchronously joins this close. Worker shutdown
  uses its separately awaited `RetireAsync` and existing process deadline.
- State is bounded per session: at most 1,024 producer scopes, 8,192 demand IDs,
  8,192 control admissions and 128 simultaneous control operations. Epoch limits
  return `session_capacity` and require a fresh session; control saturation returns
  `busy` without granting another reservation. Completed evaluator closures are
  removed; the core's protocol tombstones remain bounded by these limits.

`cancel` acknowledges logical withdrawal. `RevocationPending`, `CleanupPending`
and their error fields continue to distinguish pending physical work and failure
from an acknowledged request.

After Composer close joins CCS, the adapter drains retained compiler diagnostics
to worker stderr with attempt, code and message, including failures from withdrawn
checks. Reporting cannot replace an original close exception. These checker
diagnostics are currently stderr-only; the provider status schema does not expose
them, and they are not reclassified as physical cleanup failures. Keep the worker
stderr evidence with the validation or deployed worker logs.

## Evidence and remaining gates

Separate SageFS session `6fd7fa1e` on port 37749 supplied the F# inner loop. A RED
probe showed the old implementation refusing a second same-ticket request with
`invalid_reservation`. The updated provider and worker suites ran **27 tests:
27 passed, 0 ignored, 0 failed, 0 errored**. They cover shared demand and detach,
pre-canceled requests, held evaluator and cancellation callback cleanup, deferred
native launch invalidation, control saturation, compiler-prefix fences, disposal
failure and overtaken session construction. This is focused REPL evidence, not
whole-consumer acceptance.

External transcripts are under
`/home/hhh/.codex/work/incremental-adoption-2026-10-01/sage/`:
`boz-red.messages.json`, `boz-tests-load.messages.json`, and
`boz-run.messages.json`. `NativeProviderTests.fs`, including the new real wire
sharing case, type-checked in SageFS using the actual Integration module extracted
from the test harness. Native test bodies were not executed by that syntax check.
The later shared-interop exact-fault probe passed; its separate cancellation
probe timed out and is not passing evidence. The task-owned bare session was
then stopped; the SageFS daemon was preserved. Compiled bridge and editor receipts
below supply the subsequent execution evidence.

The earlier Bozzetto Release build and unfiltered default receipts are summarized
below. Final source-built consumer and native/provider gates are still being
completed. Installed daemon/MCP/browser
acceptance must identify the deployed closure separately.

Exact reconciliation after a lost wire reply remains a follow-up. The mailbox
retains exact admitted-operation receipts internally, and current build tickets
can re-observe their producer. Protocol v1 still lacks a retained request receipt
and explicit acknowledgement/forget protocol for reconnecting clients. A status
refresh does not prove which lost request committed. No durable actor inbox,
cross-session producer sharing, persistent actor lifecycle or Fable runtime is
implemented by this provider change.

## Response to the consumer audit

The [original C1–C4 assessment](Incremental_Consumer_Integration_Auditor_Assessment_2026-10-01.md)
is retained unchanged. Its findings and the subsequent native C5 failure prompted
the following controlled regressions and repairs. These receipts do not yet close the cross-project audit;
final build, complete consumer suites, native/provider execution and exact pins
remain pending. Paths below are relative to the external evidence workspace
`/home/hhh/.codex/work/incremental-adoption-2026-10-01/`.

| Finding and owner | Repair and discriminating evidence | Current limit |
| --- | --- | --- |
| **C1 — Composer reservation pairing** | `concurrent reservations keep the final native and checker pair buildable` in [ProjectSessionTests.fs](../../Composer/tests/Alex.Tests/ProjectSessionTests.fs) reproduced **one failure, zero passes**: `Project checking refused: InvalidRevision` (`validation/reservation-red.log` / `.trx`). Native and CCS immediate admissions now share an ordering boundary; notification and acknowledgement waits remain outside it. | The repaired full suite on preview.5 passed **380/381**, including this regression, with the same known callable-`Result` transport failure (`validation/composer-full-repaired.log` / `.trx`). That run predates the final preview.6 pin; it is not a preview.6 full-suite receipt. |
| **C2 — Bozzetto transport withdrawal capacity** | A controlled Sage transport probe with a shell child reproduced slot theft and healthy-worker termination. Atomic reservation/transfer of the abandoned request's slot preserves the 256-request bound. The green probe observed capacity refusal, exact cancellation completion and a surviving worker (`sage/boz-c2-red.messages.json`, `boz-c2-green.messages.json`). | Both real .NET child-process build/run cases in [ComposerWorkerClientTests.fs](../Bozzetto.Tests/ComposerWorkerClientTests.fs) **passed in the unfiltered default gate** (`validation/bozzetto-default-c5.log`). They hold control admission, refuse an ordinary caller, complete the other 255 requests and accept the abandoned late reply. |
| **C3 — Bozzetto same-generation status ordering** | Four controlled Supervisor regressions failed before repair (`sage/c3-red-result.messages.json`). Repair combines provider capture order with daemon activity fencing; malformed sequences invalidate freshness and observation counters do not emit visible-state changes. Native validation then exposed overly strict rejection of stable active reads. Those reads now report actual busy progress with `statusFresh=false` and `current=null`; reads crossing activity boundaries remain refused. | Seven Supervisor and three provider/wire regressions **passed in the unfiltered default gate** (`validation/bozzetto-default-c5.log`). The positive progress test retains both authority-denial assertions. Live MCP/browser cancellation progress then passed in the **40/40 entire Composer tier** (`validation/bozzetto-native-c5.log`), on the intermediate compiler closure. Earlier Sage emission errors and its zero-test command remain historical failed attempts. |
| **C4 — Composer editor solver lifetime** | Shared process collection joins exit and retained input/output tasks; Lattice retains proof ownership across invalidation and joins it during retirement. Four shared process controls passed (`validation/process-cleanup-r2.log` / `.trx`). The earlier editor run exposed retained-fault wrapping (`validation/editor-proof-lifetime-r2.log`); preview.6's shared CLR bridge repaired that boundary. | **Actual cvc5 controls and all four lifecycle checks now pass on preview.6**, including retained fault identity and actual solver input ownership (`validation/editor-proof-lifetime-preview6.log`; [LifetimeChecks.fs](../../Composer/tests/CCS.Editor.Tests/LifetimeChecks.fs)). Complete editor/RPC and final Composer gates remain separate requirements. |
| **C5 — Bozzetto retained artifact after compiler withdrawal** | The real native test refused a run after an unreserved source edit, but provider status still held `current` (`validation/bozzetto-native-preview6.log`). Reconciliation now reads typed backend authority outside the provider gate, withdraws only matching metadata/tickets and commits library invalidation before the refusal. Retained successful replies also recheck eligibility. | Four regressions in [ProviderSessionTests.fs](../Bozzetto.Composer.Tests/ProviderSessionTests.fs) cover withdrawal, identical refusal text with authority retained, a newer reservation and independent observer cancellation. All **29 provider tests passed in fresh Sage** (`sage/provider-authority-suite.messages.json`), then in the unfiltered default gate. The **unchanged native oracle passed** in the 40/40 entire Composer tier (`validation/bozzetto-native-c5.log`), on the intermediate closure. |

## Bounded audit map

Start with the sibling [CCS workspace contract](../../clef/docs/Incremental_Project_Workspace_2026-10-01.md)
and [Composer adoption contract](../../Composer/docs/incremental-compilation/2026-10-01-workspace-adoption.md).
The independent [preview.5 observation assessment at deabc97](https://forge.spkez.dev/FidelityFramework/Fidelity.FSharp.Incremental/src/commit/deabc979ccc21c975b47e2e16d8722085b71d82e/docs/Consumer_Observation_Auditor_Assessment_2026-10-01.md)
records acceptance of library implementation `87c77d9` within that audit's scope;
its 102 repository tests and three additional observation controls do not
substitute for consumer acceptance or preclude a later shared-contract repair.

These are source locations and discriminating assertions, not new passing-run
claims. CCS tests use actual checking with controlled barriers; provider unit
tests inject a backend. Rows marked native execute real processes or artifacts.
Line numbers identify the inspected integration source; use the exact test names
if the ongoing cleanup repair moves those locations.

| Boundary | Test and location | Evidence to inspect |
| --- | --- | --- |
| Shared demand | CCS `Same generation shares a complete check and one withdrawn consumer preserves another` ([ProjectWorkspaceTests.fs:201](../../clef/tests/Clef.Compiler.Service.Tests/ProjectWorkspaceTests.fs)); provider `canceling one client detaches its demand while another keeps the shared producer` ([ProviderSessionTests.fs:273](../Bozzetto.Composer.Tests/ProviderSessionTests.fs)). | One check/build; withdrawing one demand preserves the other. Provider coverage is an injected-backend unit test. |
| Process-global state serialization | CCS `Two real workspaces share process ownership through complete projection` and `Close joins noninterruptible checking and restores process configuration` ([ProjectWorkspaceTests.fs:63,320](../../clef/tests/Clef.Compiler.Service.Tests/ProjectWorkspaceTests.fs)). | A second evaluator demonstrably waits for the shared CCS gate; projection remains inside ownership; configuration and withdrawn failure diagnostics survive projector failure. This is within one process. |
| Reservation before deferred launch | Provider `reservation bypasses occupied evaluator slots and prevents a deferred native launch` ([ProviderSessionTests.fs:433](../Bozzetto.Composer.Tests/ProviderSessionTests.fs)); Composer `reservation before deferred run invocation refuses the old artifact before launch validation` ([IncrementalBuildTests.fs:294](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Unit barriers prove edit permission bypasses dispatch saturation. The Composer case first builds a real artifact, then proves the obsolete deferred call never reaches launch validation. |
| Actual launch before reservation | Composer `a real launch that wins reservation remains owned and its old generation is withdrawn` ([IncrementalBuildTests.fs:355](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Native `Process.Start` occurs once before the barrier. Later reservation withdraws current authority without pretending the already launched process never ran. |
| Physical capture and callback joins | CCS `Owned capture shares demand permits reservation and remains joined by close` ([ProjectWorkspaceTests.fs:99](../../clef/tests/Clef.Compiler.Service.Tests/ProjectWorkspaceTests.fs)); provider `close retains ownership after evaluator return until cancellation callback exits` ([ProviderSessionTests.fs:456](../Bozzetto.Composer.Tests/ProviderSessionTests.fs)); Composer `close joins a caller cancellation callback after the operation body has completed` ([IncrementalBuildTests.fs:247](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Held capture and callbacks keep close incomplete. Composer also retains the directory lease; provider prevents backend disposal. Callback barriers alone are not native execution evidence. |
| Actual process and output joins | Composer `canceling a process joins its actual exit and both output streams` and `canceled process collection retains each independently held output cleanup` ([IncrementalBuildTests.fs:185,209](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | Real child processes exit, stdout and stderr settle independently, and cancellation cannot detach either cleanup. |
| Stale input/proof refusal | `project receipts reject changed manifests and checker text even after disk restoration` ([ProjectSessionTests.fs:48](../../Composer/tests/Alex.Tests/ProjectSessionTests.fs)); `Result branch authority rebuilds its whole scope and rejects another revisions proof receipt` ([IncrementalBuildTests.fs:518](../../Composer/tests/Alex.Tests/IncrementalBuildTests.fs)). | The receipt test checks actual consumed text. The native case defines fresh-proof and stale-proof assertions, but its current recorded run stops at the initial build; the later assertions remain unexercised. |
| Same native receipt and retained object | `two wire clients of one reservation share the same native build receipt` and `native builds retain real objects and execution gates reject changed inputs and artifacts` ([NativeProviderTests.fs:379,405](../Bozzetto.Composer.Tests/NativeProviderTests.fs)). | Real worker replies share artifact path/hash and object manifest; replay does not rebuild. Source edits preserve the unaffected object's actual path/hash and change native output. Deterministic overlap is established separately by provider unit barriers. |

## Cross-project gates and current evidence

Evidence is retained under
`/home/hhh/.codex/work/incremental-adoption-2026-10-01/validation/`.
The table preserves failures and separates the first Composer run from its
subsequent process-cleanup repair. Final native receipts and closure pins remain
pending the coordinating run.

| Gate | Recorded receipt and remaining limit |
| --- | --- |
| Library preview.5 independent audit | **102/102 passed**, plus three additional observation controls, recorded at `deabc97` for implementation `87c77d9`. This revision-scoped evidence does not certify the consumers or freeze the library contract. |
| Library preview.6 full suite at `3b86e2d` | **108/108 passed**, zero failed/skipped, including six new CLR bridge controls (`incremental-preview6.log` / `.trx`). Package identities are recorded above; this is subsequent author validation, not the earlier independent audit. |
| Clef focused and unfiltered compiler-service suites at `7927b3f`, preview.5 | **22/22 focused passed**. Full: **2,125 passed / 2,224 total, 99 failed, zero skipped** (`clef-full-final.log` / `.trx`). `clef-exact-comparison-final.json` preserves all baseline cases and the same 99 failures; all fifteen additions passed, with no missing cases, new failures or recoveries. Full compiler acceptance remains red; the final preview.6 receipt is pending. |
| First Composer full comparison at integration base `0c9dc71` | **377 passed / 378 total, one failed, zero skipped** (`composer-full.log` / `.trx`). `composer-exact-comparison.json` preserves all 369 baseline cases plus nine passing additions, with no missing cases, new failures or recoveries. The existing callable-`Result` transport case fails at its initial native build, so its later stale-proof assertions remain unexercised. This run predates the reservation and cleanup repairs. |
| Repaired Composer full suite, preview.5 | **380 passed / 381 total, one known failure, zero skipped** (`composer-full-repaired.log` / `.trx`), including the repaired C1 reservation regression. This predates the last package pin; no preview.6 full-suite result is implied. |
| First Bozzetto unfiltered default tier, preview.5 before C2/C3 | **9,772 passed, four ignored, zero failed/errored; 9,776 registered and accounted for; Trusted** (`bozzetto-default.log`, `bozzetto-trust.jsonl`). The comparison baseline is the peer's FSI-retirement checkpoint: **9,769 passed plus four ignores**, not the older 9,802 count. See the [host transition validation](Bozzetto_Clefx_Host_Transition_2026-10-01.md#validation). |
| Editor proof/lifetime checks, preview.6 | **Actual cvc5 controls and all four added lifecycle checks passed** (`editor-proof-lifetime-preview6.log`). This includes both cases that the earlier retained-fault failure prevented from passing. |
| Complete editor default run, preview.6 | Stopped after **18 passing groups** at `closureEnvironmentChecks`: fixture diagnostic `CCS8011` reports unobservable ranges for `total` and the `+` result (`editor-default-preview6.log`). Later checks did not execute. No editor baseline comparison has established whether this failure predates the integration. The full editor gate remains red. |
| Bozzetto default after C2/C3/C5 repairs, preview.6 | **9,788 passed, four ignored, zero failed/errored; 9,792 registered and accounted for; Trusted** (`bozzetto-default-c5.log`). Includes the real .NET transport fixture, status ordering/progress and retained-authority regressions. This records the current provider build; the final compiler closure after Baker repair still requires alignment and revalidation. |
| Composer final preview.6 process/native and RPC checks | Pending after an explicit restore: an earlier no-restore build retained preview.5 assets. The final binary closure must match preview.6 hashes; aligned source pins alone do not establish that result. Preserve the earlier full-suite receipts and separate editor results above. |
| First entire Bozzetto `--integration-composer` tier, preview.6 | **34 passed, one failed, one errored; 36 registered/executed; TestsFailed** (`bozzetto-native-preview6.log`). The native failure exposed C5; the owned live case exposed overly strict active-status refusal under C3. |
| Entire Bozzetto tier after C3/C5 repairs | **40/40 passed, zero ignored/failed/errored; Trusted** (`bozzetto-native-c5.log`). Includes unchanged stale-current, real object/receipt reuse and live MCP/browser cancellation-progress assertions. The provider was built against frozen `compiler-preview6-c1-c4`, before the Baker repair and PSG schema 12. This is an intermediate receipt; the final aligned closure still requires a rerun. |

## Validation and promotion recipe

Use the existing Bozzetto build/test work leases. Set these inputs to the reviewed
closure and an external evidence directory; the fixture must be the real
`IncrementalScalarRegions.fidproj` with its explicit absolute platform dependency.
The commands below are a recipe, not additional recorded results.

```bash
export DOTNET_HOST_PATH=/absolute/path/to/reviewed/dotnet
export COMPOSER_DISTRIBUTION=/absolute/path/to/rebuilt/Composer/distribution
export CALQUE_DISTRIBUTION=/absolute/path/to/reviewed/Calque/publish/closure
export BOZZETTO_COMPOSER_FIXTURE=/absolute/path/to/04d_IncrementalScalarRegions/IncrementalScalarRegions.fidproj
export BOZZETTO_COMPOSER_EVIDENCE=/absolute/external/evidence/native-provider
export BOZZETTO_TRUST_LEDGER=/absolute/external/evidence/trust.jsonl

"$DOTNET_HOST_PATH" build Bozzetto.Composer/Bozzetto.Composer.fsproj -c Release \
  -p:ComposerDistribution="$COMPOSER_DISTRIBUTION" \
  -p:CalqueDistribution="$CALQUE_DISTRIBUTION"
"$DOTNET_HOST_PATH" build Bozzetto.Tests/Bozzetto.Tests.fsproj -c Release \
  -p:ComposerDistribution="$COMPOSER_DISTRIBUTION" \
  -p:CalqueDistribution="$CALQUE_DISTRIBUTION"

export BOZZETTO_COMPOSER_WORKER="$PWD/Bozzetto.Composer/bin/Release/net10.0/Bozzetto.Composer.dll"
export BOZZETTO_DAEMON_DLL="$PWD/Bozzetto/bin/Release/net10.0/Bozzetto.dll"

"$DOTNET_HOST_PATH" Bozzetto.Tests/bin/Release/net10.0/Bozzetto.Tests.dll --summary
```

The first test command is the unfiltered default gate and must report `Trusted`.
The formatter closure must include Calque.Incremental, Core and Syntax and the
reviewed foundation assemblies. The build compares both foundation assembly
hashes with the selected compiler closure. A library build directory can omit
package dependencies; use the complete `dotnet publish` output for deployed
selection. Filtered runs are inner-loop evidence only.

Once the rebuilt daemon closure is available, run the entire dedicated tier:

```bash
"$DOTNET_HOST_PATH" Bozzetto.Tests/bin/Release/net10.0/Bozzetto.Tests.dll \
  --integration-composer --summary
```

That tier includes provider units, worker protocol tests, native process tests
and `Live Composer shared interfaces`. The live test starts and cleans up its own
daemon on reserved ports, with an external working/data directory and ten-minute
TTL; it requires the complete daemon closure and MCP dependencies. Accept the tier
only with an unfiltered `Trusted` row and retained evidence. Neither command
updates the existing shared daemon on 47749/47750. Promotion of that service must
record the reviewed deployed closure and repeat its MCP/browser checkpoint
separately; a passing owned test daemon does not identify the installed service.
