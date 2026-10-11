# Composer workflow

Composer is a differential compiler. Bozzetto coordinates explicit project work and owned process lifetimes; it does not decide source meaning, proof validity or artifact eligibility. Use the `/composer` page on daemon port 47749 or the `composer_*` MCP operations. The control listener on 47750 is not an alternate execution path.

The governing rules are in repository-root `AGENTS.md`, especially **Clef and Composer work**, **Architecture**, **Language and stack**, and **Validation posture**. This document distinguishes inspected implementation from requirements. Source inspection is not evidence that the installed daemon matches the checkout, that a workflow passed, or that native hosting is delivered. Check installed identity and readiness using [operations.md](operations.md).

## Ownership and capability boundaries

The workflow brings three components together without merging their authority:

| Component | Responsibility in this workflow | What it cannot establish |
|---|---|---|
| Fidelity.PSG | Compiler-owned semantic and proof evidence, with identities and premises preserved through publication and reuse. | A host status card, transport checksum or display count cannot create PSG authority. |
| Fidelity.FSharp.Incremental | Declared dependency bookkeeping, explicit demand, admission, withdrawal, eligibility and joined work lifetime. | An eligible result or `Offer` is not permission to reuse proof, accept an artifact or execute it. |
| Ranvier | Reference shape for signal/memo/effect presentation, with one mutation domain and immutable admitted observations. | Equal display values cannot erase compiler proof identity; effect cleanup cannot certify physical drain. |

The current provider uses the foundation's `AsyncMailbox` to own build/run producers and demand in `src/Bozzetto.Composer/ProviderSession.fs`. Its producer definitions declare `Reads = []`; this adapter is not discovering or reconstructing the compiler's semantic dependencies. Complete compiler premises remain the compiler's responsibility. See [Bozzetto_Incremental_Foundation_Adoption.md](Bozzetto_Incremental_Foundation_Adoption.md).

The browser vocabulary and projections expose observations, not an execution capability. Ranvier-shaped settlement is a requirement, not a claim of Ranvier adoption merely because the page has reactive stores. The inspected supervisor still reconciles pending activity with timed status reads (`src/Bozzetto/ComposerSupervisor.fs`). That mechanism is migration work, not the foundation-driven push contract. See [reactive-push.md](reactive-push.md).

Embedded F# sessions, REPL evaluation, warmup, Interactive/LiveTesting modes and runtime method patching are not Composer workflows. Do not recover a refusal through those surfaces. LLVM ORC JIT execution or replacement is planned, not implemented by file watching, worker replacement or these operation names. See [architecture.md](architecture.md).

## The explicit project loop

Use an existing absolute `.fidproj` path. Do not infer a project from the current directory or reuse an address from another worker. Human and agent clients use the same supervisor and compiler owner (`src/Bozzetto/ComposerTools.fs`, `src/Bozzetto/ComposerRoutes.fs`, `src/Bozzetto/UiBridge.fs`).

| Step | MCP operation and arguments | Required handling |
|---|---|---|
| Open | `composer_open_project(project)` | Retain the returned host, session and epoch. Opening does not build or grant execution permission. |
| Observe | `composer_session_status(host, session, epoch)` or `composer_list_sessions()` | Read freshness, pending withdrawal, cleanup and errors. Status is an observation only. |
| Reserve | `composer_reserve_edit(host, session, epoch, label)` | Wait for success and retain the opaque reservation before any source or dependency write. |
| Edit | Client-owned source mutation | Apply only the intended edit after reservation acknowledgement. The label is descriptive, not a path lock or a proof of scope. |
| Build | `composer_build(host, session, epoch, reservation)` | Use the token returned for this session and epoch. Preserve compiler diagnostics and specific refusals. |
| Run | `composer_run_current(host, session, epoch, arguments)` | Pass individual argument strings. This is the only execution path. |
| Close | `composer_close_session(host, session, epoch)` | Logical closure begins cleanup. Inspect pending cleanup and failures; closure is not a physical-drain receipt. |

The operation signatures above are implemented in `src/Bozzetto/ComposerTools.fs`. Browser `OpenProject`, `Reserve`, `Build`, `Run`, `Cancel`, `CloseSession` and `RetireWorker` commands map to the same typed requests in `src/Bozzetto/UiBridge.fs`. Detailed arguments and refusal vocabulary belong in [mcp-tools.md](mcp-tools.md), not a second schema here.

`src/Bozzetto.Composer/Worker.fs` checks that the project path is absolute, exists and ends in `.fidproj`. It rejects provider scratch inside the project directory. An open creates a new session and backend; a closed session cannot be reopened. Opening again is a new authority, not restoration of old tickets. Do not assume retained results survive close, retirement or restart without separate compiler-owned persistence evidence.

For a build without edits, still obtain an explicit reservation and then build. This permits an unchanged-input control without implying a status read or page refresh starts compilation. `src/Bozzetto.Composer/ProviderSession.fs` defines the producer during reservation and attaches demand through `BuildAsync`; observation alone does not admit a build.

Caller-owned builds and test suites require work leases under `AGENTS.md`. A lease governs resource admission, not compiler proof or edit permission. Follow grant, wait and refused decisions, renew while work runs, and release on completion or failure. See [agents.md](agents.md) and [operations.md](operations.md). Do not route Clef/Composer execution around the provider by launching an artifact or invoking a compiler independently.

## Operations journal

The supervisor owns a memory-only journal of settled Composer calls from all adapters, not just commands issued by the viewing page. `src/Bozzetto/ComposerSupervisor.fs`, `ExecuteAsync` and `Record`, records every non-status call that returns or throws, including refused operations. This includes open, reserve, build, run, cancel, close, compiler retirement and format preview. Status reads are never journaled, whether successful or refused; directory observation and internal status reconciliation do not append entries. Commands handled by the bridge without a supervisor operation, such as snapshot requests and resource observation, are not Composer journal entries.

Each entry carries a journal sequence, settlement time, caller label, operation, session, available project text and outcome. A returned reply records its outcome; an escaping exception records `Outcome.Failed` and is rethrown. Cancellation records a `Canceled` refusal; if cancellation escapes the wrapper it is rethrown after recording. A returned `Ran` outcome also carries exit code, compiler generation, `sourceVersion`, standard output and standard error (`src/Bozzetto/ComposerJournal.fs`, `settle`). A nonzero program exit code is still a successfully returned run operation, not a compiler refusal. Project text comes from the open request or the available session observation; it can be empty and is not recovered by rereading a finished session (`ComposerSupervisor.Record`).

The caller is an **adapter-reported label**, not an authenticated identity or proof of who performed the work. The journal vocabulary gives examples `mcp:<transport>`, `http` and `browser`; the two-argument supervisor overload reports `unknown`. `src/Bozzetto/UiBridge.fs`, `Project.operations`, carries this as `CallerLabel` in `OperationsBoard`. `src/bozzetto-web/src/Frontend/App.fs`, `OutputPane`, marks the caller's provenance in its title text. The requirement is to preserve this distinction, not infer identity or authorization from a label.

`src/Bozzetto/ComposerJournal.fs` bounds the journal three ways:

| Bound | Implemented behavior |
| --- | --- |
| Entry count | At most 200 entries, newest first; appending the 201st drops the oldest entry. |
| Each transcript stream | At most 16,384 UTF-16 code units of stream content. A longer stream keeps its end, prefixed by `[cut: N characters omitted]`; that bounded marker line is additional to the content limit. |
| Aggregate transcripts | At most 512 × 1024 UTF-16 code units across retained standard-output and standard-error strings, including cut markers. Oldest live transcripts are cleared first until the budget fits; their entries and run metadata remain with `Dropped = true`. |

Other entry text is bounded too: caller labels, session identities and run source versions to 128 code units, project paths and refusal/exception messages to 512. These are character limits, not UTF-8 byte limits or an encoded-frame size guarantee. Nothing is persisted by this journal, and it does not enlist the independent diagnostic sink.

The supervisor publishes the complete immutable journal with a fixed incarnation and increasing entry sequence through `Journal` and `JournalChanged`. The fact plane derives the stamped `operations` event on protocol 8 and supplies current state on connect and resync (`src/Bozzetto/UiBridge.fs`, `Facts`; `src/bozzetto-web/src/Shared/Protocol.fs`, `OperationsBoard`). Push coalescing may skip intermediate snapshots, and the bounds mean this is not a durable audit history or missed-event replay.

On the Composer page, Activity lists the journal newest first. Run output offers journal entries with run metadata, defaults to the newest journal run when no retained selection matches, and displays `[transcript dropped for space]` when output was evicted. The page's own command activity and run output remain separate, labelled `this page`; a browser operation can therefore appear in both sources (`src/bozzetto-web/src/Frontend/App.fs`, `OutputPane`; `src/bozzetto-web/src/Frontend/Model.fs`, `apply`). A journal outcome or retained transcript grants no current execution or physical-drain authority.

## Reserve before write

A successful reservation means that the owning provider has committed the required withdrawal and invoked the compiler's reservation boundary. A queued request, transport receipt, pending browser button or timeout is not permission to edit.

In the current provider, reservation advances the adapter generation, clears the current artifact and previous token, closes affected foundation scopes, and withdraws admitted formatting demands. It then invokes `backend.Reserve` under the invocation fence, observes foundation withdrawals, defines the build producer and returns a fresh opaque token (`src/Bozzetto.Composer/ProviderSession.fs`). Compiler tickets remain process-local; clients cannot serialize or reconstruct them (`src/Bozzetto.Composer/ProviderContracts.fs`).

Logical withdrawal need not wait for every evaluator, callback or process to finish. Physical ownership remains with the existing work owner. This distinction permits edits after acknowledged reservation while keeping outstanding cleanup visible. It does not permit old work to publish or launch under obsolete authority.

Reserve before changing source, project configuration or dependency files that affect the build. File observation is not a replacement for reservation. Unreserved changes must never regain authority merely because the host has noticed them or restarted a process. Composer's next build/run gates remain authoritative.

### Formatting is a preview, not an edit

`composer_format_preview` operates on an immutable supplied buffer, with the current adapter generation, stable document label, GUID incarnation, revision and configuration. It returns exact base-source identity and formatted text; it grants no file-write, proof or execution authority (`src/Bozzetto/ComposerTools.fs`).

To apply a preview:

1. Obtain the preview for the intended immutable buffer.
2. Successfully reserve the edit.
3. Compare the live buffer with the exact preview base again.
4. If it differs, do not apply that preview. Obtain a fresh preview under current authority.
5. Apply the verified preview and build with the reservation.

Formatting failures and cleanup are separate from compiler failures. Inspect `formatterError`, `formatterCleanupPending` and `workerRetirementRequired`; a successful build does not erase formatter cleanup evidence (`src/Bozzetto.Composer/ProviderSession.fs`).

## Identities, conflicts and lost outcomes

Treat host, session, epoch and reservation as opaque owner-issued values. A worker epoch fences all sessions of that worker. A compiler replacement requires a fresh epoch, not a changed version label on an old address.

Keep these counters distinct:

| Identity or counter | Meaning |
|---|---|
| Host/session/epoch | Owning worker incarnation and explicit session address. |
| Adapter generation | Provider authority revision, advanced by reservation, cancellation and close. |
| Artifact generation and `sourceVersion` | Compiler-issued identity of an accepted build. |
| Status observation | Order of actual provider status captures within an address. |
| Supervisor snapshot revision | Local shared-directory change order, not compiler proof identity. |
| Journal sequence | Settled-entry order within one supervisor incarnation. |
| Operations board revision | Publication stamp of the plane's Operations work, not the journal sequence. |
| Browser correlation | Matches a command to its outcome, not an edit or execution capability. |

These distinctions are explicit in `src/Bozzetto.Composer.Protocol/ProviderData.fs`, `src/Bozzetto/ComposerSupervisor.fs`, `src/Bozzetto/ComposerJournal.fs`, `src/Bozzetto/UiBridge.fs` and `src/bozzetto-web/src/Shared/Protocol.fs`. Never substitute one counter for another or infer global ordering across epochs.

| Situation | Response |
|---|---|
| Another reservation wins | Old tokens and completions are obsolete. Reconcile current status and coordinate before reserving again. |
| Foreign or superseded token | Preserve `invalid_reservation`; do not fabricate a token or silently build another revision. |
| Old host or epoch | Preserve `wrong_authority`; recover an address from the current owner, not by rewriting fields. |
| Busy or bounded-capacity refusal | Respect the specific reason. Do not bypass admission; session-capacity exhaustion requires a fresh session. |
| No current accepted artifact | Reserve and build. Cached paths are not a fallback. |
| Timeout, cancellation or disconnect before the reply | The operation may already have committed. Stop writes and reconcile; do not assume it never ran. |
| Stale status or worker failure | Treat the projection as observation failure, not retained authority. |

`src/Bozzetto.Composer/ProviderSession.fs` shares one admitted build producer among observers of the same reservation. Detaching one request releases its demand without cancelling a peer's demand. Explicit `composer_cancel` is different: it revokes session authority and starts compiler withdrawal. The browser consumes its local token when sending a build (`src/bozzetto-web/src/Frontend/Model.fs`); this does not mean the provider blindly resubmits the build on a retry.

The supervisor rejects wrong worker addresses, obsolete generations and responses from retired workers. It clears current metadata from stale status projections (`src/Bozzetto/ComposerSupervisor.fs`). These fences do not establish general durable duplicate-request reconciliation across reconnects or daemon failure. That remains an explicit integration requirement. If a reservation outcome is unknown and no exact acknowledgement can be recovered, writes remain unauthorized; coordinate a new acknowledged reservation rather than guessing the lost token.

A Composer reservation is not a repository-wide human/agent edit lock. Coordinate shared files and checkout/session identity through [agents.md](agents.md). Separate sessions addressing the same files do not make conflicting writes safe.

## Build evidence and per-region validation

Composer decides which evidence and objects remain valid. Bozzetto must present the accepted facts without inventing proof, reconstructing a semantic graph from diagnostics, or treating a successful command as proof of every pipeline stage.

The inspected adapter maps compiler `Accepted` metadata to generation, `sourceVersion`, artifact path and hash, object manifest, witness identity arrays, witness visits and object identity arrays (`src/Bozzetto.Composer/ComposerAdapter.fs`, `src/Bozzetto.Composer.Protocol/ProviderData.fs`). This is implemented evidence projection. It is not independent evidence of the compiler's retention algorithm or proof discharge.

For every accepted build, the presentation contract requires one row per region identity named in the accepted evidence, including retired regions. Witness and object states are separate:

| Field | Required meaning |
|---|---|
| Identity | Compiler-issued region identity, preserved exactly. |
| Witness | `changed`, `retained` or `retired`, as reported by the compiler. |
| Witness visits | Compiler-reported number of visited nodes for this region, not a host estimate. |
| Object | `compiled`, `reused` or `retired`, as reported by the backend. |

A region may have witness evidence without an object of its own, or an object without a witness row. Represent absence explicitly in the presentation; do not classify it as retained or reused. The current `RegionRow` vocabulary uses an empty string for an absent witness/object state (`src/bozzetto-web/src/Shared/Protocol.fs`). Unknown or contradictory states must not be silently converted into a favorable result.

`src/Bozzetto/UiBridge.fs`, function `Project.artifactSummary`, currently constructs rows from the union of all witness, visit and object identities, deduplicates and sorts identities, and maps the reported states. It defaults an absent visit entry to zero and chooses the first matching state when arrays overlap. Those projection choices are not compiler validation: distinguishing absent visit evidence from measured zero and refusing contradictory states require their own owner/codec controls.

The shared browser protocol carries these rows. `src/bozzetto-web/src/Frontend/Model.fs` derives its compiled, reused, retired-object and changed-witness counts from `Regions`, rather than separately supplied totals. This establishes those source-level projections, not an executed page acceptance result.

All displayed totals must be derived from the same accepted row set: count each witness/object state and sum reported visits only with their defined meaning. Never maintain an independent host counter or mix rows from different generations. If the available evidence is incomplete, label the limitation instead of reporting complete totals. A retained witness does not necessarily imply a reused object; a reused object does not establish fresh proof or whole-program acceptance. Whole-program validation and region reuse remain distinct compiler facts.

The UI must retain generation and `sourceVersion` alongside the rows and artifact identity. Equal totals, equal output text or an unchanged display value cannot suppress a change of evidence identity. Timings, widened region cohorts and persistence across restart are separate capabilities; do not infer them from this row vocabulary.

### PSG evidence delivery

PSG evidence belongs to the compiler and must be delivered under source-authorized scope. A region row is a summary of accepted metadata, not a PSG proof payload. Do not manufacture a proof handle from a filename, hash, diagnostic or foundation eligibility token.

Scoped attachments, exact-base deltas, reauthorization and receiver-installation fences are requirements governed by [Bozzetto_Fidelity_Component_Contracts.md](Bozzetto_Fidelity_Component_Contracts.md). Do not transfer a full retained graph simply to populate a page. Scope, source revision, epoch and proof identity must survive projection and reconnect. A receiver showing old evidence cannot authorize current execution.

## Run admission and execution lifetime

Run only through `composer_run_current`. Never execute `artifactPath` directly, even after an accepted build or fresh status read. Metadata can describe an artifact whose inputs or bytes have since changed.

The operation is exposed as input/executable revalidation in `src/Bozzetto/ComposerTools.fs`; `src/Bozzetto.Composer/ComposerAdapter.fs` delegates it to Composer's `ProjectSession.RunCurrentAsync`. The inspected Bozzetto code establishes delegation and fencing, not an independent audit of the compiler's internal byte-validation implementation.

The provider checks current adapter authority at actual backend invocation under the same fence used by reservation. Its backend contract requires artifact selection and launch synchronously before returning the task, with the returned work owning process cleanup (`src/Bozzetto.Composer/ProviderSession.fs`, `src/Bozzetto.Composer/ProviderContracts.fs`). Moving launch behind a deferred queue must preserve that final fence; validating only the queued description is insufficient.

A reservation that wins before invocation must prevent the old launch. If a run already launched, withdrawing publication authority does not erase its physical lifetime. The owner must cancel and join its processes and callbacks, retain failures, and prevent obsolete completion from becoming current evidence. A host process restart cannot bypass compiler checks or certify cleanup.

Run results carry compiler generation, `sourceVersion`, exit code and output. Program success is not compiler acceptance evidence. Inspect both operation refusals and the run exit code without conflating them.

## Cancel, close and compiler replacement

`composer_cancel` withdraws the current artifact and outstanding session work. `revocationPending` records unfinished physical withdrawal. Request cancellation merely detaches that request's demand; it is not the explicit session-cancel operation.

`composer_close_session` seals admission, withdraws current authority and starts joining owned work. Keep `cleanupPending`, `cleanupError`, formatter cleanup and worker errors visible until settled. A closed session, completed wrapper task or elapsed timeout does not prove physical drain. These distinctions are implemented in `src/Bozzetto.Composer/ProviderSession.fs` and projected by `src/Bozzetto/ComposerSupervisor.fs`.

Before compiler replacement, use `composer_retire_worker(host, epoch)`. It affects every session in that worker, not just the caller's project. Coordinate active work before requesting retirement. The worker closes its sessions; the supervisor fences their projections and requires observed process exit before releasing ownership (`src/Bozzetto.Composer/Worker.fs`, `src/Bozzetto/ComposerSupervisor.fs`). Cleanup failures remain failures. Replacement uses a fresh epoch and new project sessions; in-memory compiler patching is unsupported.

The inspected formatter cleanup deadline triggers sticky worker retirement rather than declaring drain complete. This is an escalation policy, not permission to disregard a surviving process. Matching deployment, rollback and readiness belong in [operations.md](operations.md).

## Transport contract and current drift

Direct daemon-worker, daemon-browser and agent-owned interchange must be typed binary. Changing a JSON serializer is not compliance. MCP alone retains JSON-RPC at its externally standardized boundary; its tool/resource projection does not authorize JSON on internal or other direct wires.

| Boundary | Inspected state and required disposition |
|---|---|
| Daemon-worker | `src/Bozzetto.Composer/Worker.fs` requires the exact BAREWire protocol/encoding/contract handshake and reports worker-loaded PSG identity. Preserve that typed boundary and matching deployment. Wire agreement does not prove artifact eligibility. |
| Composer HTTP clients | `src/Bozzetto/ComposerRoutes.fs` parses and emits JSON through Fidelity.Data. This direct JSON API is drift to remove, not the required client contract. |
| Daemon-browser | `src/bozzetto-web/src/Shared/Protocol.fs` documents interim JSON WebSocket text frames on `/ui/bridge`. Typed records alone do not establish binary transport; replace the text wire under its owning codecs. |
| Legacy events route | `src/Bozzetto/ComposerRoutes.fs` still maps `/api/composer/events` as SSE invalidation hints. This is vestigial code, not the current UI pipeline or an authority source. |
| MCP | `src/Bozzetto/ComposerTools.fs` projects typed replies into standard MCP results and resource content. This is the contained external JSON-RPC exception. |

Fidelity.Data JSON belongs only at export edges and in the independent MailboxProcessor-driven diagnostic sink process. The sink may capture binary PSG evidence and export it; it cannot reserve edits, grant proof validity or authorize execution. See [Bozzetto_Diagnostic_Sink.md](Bozzetto_Diagnostic_Sink.md) and [binary-format-spec.md](binary-format-spec.md).

## Validating a workflow change

Validate the changed owner and its consumers, with one unfiltered TRUST acceptance run at integration. Preserve exact component identities and receipts using [development.md](development.md).

For differential presentation, exercise a cold build, an unchanged build and a controlled edit inside one explicit session. Compare accepted compiler evidence with projected rows and row-derived totals for each generation. Do not prescribe reuse counts independent of the compiler's admitted region cohort and premises.

Positive controls must show accepted rows and valid current execution. Negative controls must assert specific refusals for obsolete reservations/epochs, changed inputs or corrupt artifact bytes, and malformed or contradictory region evidence at its owning boundary. Include a mutation that the tests catch. Exercise reservation winning before launch, one observer detaching while a peer remains, late completion after withdrawal, and close with delayed or failed physical cleanup.

These are acceptance requirements, not reports of completed runs. Region display, binary delivery, foundation adoption, reactive settlement, native execution and distributed placement each need their own evidence. None is established by a successful page render or a green build command alone.
