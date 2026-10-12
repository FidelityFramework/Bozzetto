# MCP tools and resources

Bozzetto's MCP adapter exposes the daemon's Composer, coordination and work-admission owners. The daemon listens on port **47749** for MCP, HTTP and browser access; **47750** is the control listener, not the MCP endpoint. Composer is a differential compiler. Open an explicit `.fidproj`, reserve before editing, and execute only through `composer_run_current`.

This is the supported-remit reference, not a reproduction of every inherited tool advertised by `tools/list`. Embedded production F# session hosting is retired. Remaining registrations and descriptions for that surface are drift to remove, not instructions for a working REPL, warmup or runtime-patching service.

**Evidence:** this reference is based on checkout source reads, not an executed daemon probe or acceptance run. Registration is in `src/Bozzetto/McpServer.fs`, `configureMcpProtocolWithComposer`; attributed methods are in `src/Bozzetto/McpTools.fs`, `src/Bozzetto/ComposerTools.fs` and `src/Bozzetto/McpResources.fs`. An installed release can differ from the checkout. Retrieval was unavailable to this audit's read/write-only tool surface; no fresh index snapshot or pinned source revision was established.

## Transport and ownership boundary

Connect an MCP client using Streamable HTTP at `http://127.0.0.1:47749/`. Complete the client's MCP initialization, then inspect `tools/list` and `resources/list`. A configured URL is not evidence of a live connection, configured compiler worker or available operation. Use `get_daemon_status` and `composer_list_sessions` to inspect those facts. Connection and deployment procedures belong in [operations.md](operations.md).

**MCP's external JSON-RPC standard is the one external exception to the no-JSON direct-interchange rule.** MCP arguments, structured results and resource content below are representations at that adapter only. They do not define the daemon-worker, daemon-browser or agent-owned direct protocol. Do not copy these representations into a second internal wire contract.

| Boundary | Contract and observed status |
| --- | --- |
| External MCP | JSON-RPC through the MCP SDK. `ComposerTools.fs` constructs typed provider requests and projects replies into MCP content. `McpServer.fs` registers the HTTP transport. |
| Daemon and Composer worker | `AGENTS.md` requires the matching typed binary contract in `Bozzetto.Composer.Protocol`. The tools use its request types; this source audit does not independently establish transport conformance or the installed pair's compatibility. |
| Direct HTTP, browser and agent-owned interchange | Typed binary is required. `McpServer.fs` still reads and writes JSON for direct lease HTTP operations. `ComposerClientJson.fs` also contains a direct client request parser, and `UiBridgeCodec.fs` explicitly implements an interim JSON browser wire. These are current implementation drift to remove, not additional exceptions. |
| Diagnostic export | Fidelity.Data JSON export belongs at the independent, MailboxProcessor-driven sink process. The local MCP friction tools are not evidence that this process boundary has been delivered. See [Bozzetto_Diagnostic_Sink.md](Bozzetto_Diagnostic_Sink.md). |

Changing the serializer does not satisfy the typed-binary requirement. Existing use of Fidelity.Data at a direct JSON wire remains drift.

The three centerpiece contracts constrain every adapter:

- **Fidelity.PSG** supplies compiler-owned evidence. A displayed witness, object manifest or accepted-artifact summary is not host-created proof or execution permission.
- **Fidelity.FSharp.Incremental** owns declared dependency bookkeeping and explicitly started work. An Offer is eligibility, not permission to reuse proof, publish PSG or execute an artifact. Complete declared reads, shared demand and physical drain remain distinct obligations.
- **Ranvier** supplies the signal/memo/effect reference shape for reactive presentation under one mutation domain. It is not a delivery claim for these MCP handlers. Equal display values cannot erase compiler proof identity, and effect cleanup cannot certify physical drain. The foundation does not dynamically discover reads.

See [Bozzetto_Fidelity_Component_Contracts.md](Bozzetto_Fidelity_Component_Contracts.md), [Bozzetto_Incremental_Foundation_Adoption.md](Bozzetto_Incremental_Foundation_Adoption.md) and [reactive-push.md](reactive-push.md).

## Identity, discovery and refusals

Caller identity is bound to the MCP transport session. `agentName` is a label, not an authority credential. Two connections supplying the same label are different members and lease holders. Reconnecting does not transfer the previous connection's claims or leases. `src/Bozzetto/Mcp.fs`, `memberIdFor` and `resolvedKey`, implement that binding; `McpServer.fs`, `createServerCaptureFilter`, supplies it before a tool body runs.

Composer uses explicit `host`, `session` and `epoch` handles. These are not inherited F# session IDs and do not depend on a connection's active-session selection. Checkout-aware routing for retained session operations is a separate concern. In particular, a worktree without its own session must not fall back to the main checkout, and creating an inherited F# session is not a remedy for a Composer failure. See `AGENTS.md`, **Multi-agent and worktree sessions**, and [agents.md](agents.md).

Registration does not guarantee admission:

- The call filter checks declared affordances before execution. Unknown declarations fail closed. Composer tools pass the generic session-state gate but still enforce provider authority through the supervisor.
- Cohort actions additionally check the caller's current published role, followed by the cohort owner's command validation. A client-declared role or holder name cannot bypass that owner.
- Provider absence, stale authority, resource pressure, capacity and cleanup failure remain refusals. Do not route around them with another process or direct artifact launch.

Sources: `src/Bozzetto/Mcp.fs`, `enforceToolCallGate`; `src/Bozzetto.Core/Affordances.fs`; `src/Bozzetto/ComposerTools.fs`.

Read both MCP error state and the operation's domain result. They are not interchangeable:

| Response family | How to interpret it |
| --- | --- |
| Composer tool | `structuredContent` and a text block contain the same projection. `success=false` sets MCP `isError=true`; inspect `error.code` and `error.message`. |
| Classified host/cohort failure | `withEchoOutcome` raises the typed `BozzettoError`; the filter returns `isError=true` with `case`, `message` and `suggestedAction`. |
| Gate rejection or unexpected exception | The filter returns `isError=true`, but may provide only a text block. Do not assume every error has structured content. |
| Lease acquisition | `granted`, `wait` and `refused` are domain decisions in a returned payload. A completed MCP call does not imply a grant. |
| Local diagnostic tools | Some failures remain ordinary text results. Inspect the text; success at the RPC layer does not prove persistence or clearing succeeded. |

Sources: `src/Bozzetto/ComposerTools.fs`, `toolResult`; `src/Bozzetto/McpTools.fs`, `withEchoOutcome` and diagnostic methods; `src/Bozzetto/McpServer.fs`, `structuredToolErrorResult` and the call filter.

## Composer tool arguments

All tools below are registered in `src/Bozzetto/ComposerTools.fs`. Arguments are required unless stated otherwise. SDK-supplied cancellation tokens are not client arguments. Strings carrying handles and reservations are opaque; do not manufacture them.

For the session operations marked **address**, supply `host: string`, `session: string` and `epoch: string` exactly as returned in the response's `authority`. `composer_retire_worker` takes only the worker handles.

| Tool | Arguments | Result or obligation |
| --- | --- | --- |
| `composer_open_project` | `project: string`, absolute `.fidproj` path | Opens a shared project session. Retain returned authority. |
| `composer_list_sessions` | None | Shared directory, configuration, worker identity and session observations. |
| `composer_reserve_edit` | **address**, `label: string` | Withdraws old artifact authority and returns an edit reservation. Await success before writing source or dependencies. |
| `composer_build` | **address**, `reservation: string` | Builds the reserved revision. Reservation is bound to the session and epoch; concurrent observers share its admitted build. |
| `composer_session_status` | **address** | Observes status and accepted metadata. Grants no build or execution authority. |
| `composer_format_preview` | **address**, `generation: int64`, `document: string`, `incarnation: string`, `revision: uint64`, `source: string`, `configuration: string` | Formats an immutable buffer snapshot without file reads or writes. See the preview rules below. |
| `composer_run_current` | **address**, `arguments: string[]` | Revalidates inputs and executable bytes, then runs the accepted artifact. Supply individual strings, including an empty array when there are none. |
| `composer_cancel` | **address** | Withdraws current artifact authority and cancels outstanding work. Physical withdrawal can remain pending. |
| `composer_close_session` | **address** | Logically closes the session and begins cleanup. The session cannot be reopened. |
| `composer_retire_worker` | `host: string`, `epoch: string` | Retires every session in the worker before compiler replacement. Coordinate with other owners and inspect cleanup failures. |

The edit/build/run loop is:

1. Open the explicit project and retain its authority.
2. Reserve and await a successful acknowledgement.
3. Edit, then build with the returned reservation.
4. Execute only through `composer_run_current` after an accepted build.
5. Close and inspect pending cleanup or failures.

A timed-out or cancelled observer is not proof that an operation never committed. Do not authorize a source write from mere enqueue receipt or blindly replay a lost reservation request. Request reconciliation across a transport failure needs an explicit contract; the existence of `requestId` in a reply does not establish durable retry semantics. See [composer-workflow.md](composer-workflow.md).

### Composer replies and evidence

`src/Bozzetto/ComposerClientJson.fs` defines the current MCP projection:

- Operation envelope: `protocolVersion`, `requestId`, `authority`, `success`, `result`, `error`.
- Authority: `host`, `session`, `epoch`, `provider`, `generation`.
- Directory: `protocolVersion`, `provider`, `configured`, `revision`, `worker`, `sessions`.
- Worker hello: compiler path/hash/version, supported `operations`, binary transport label, `workerProtocolVersion`, `contractDigest`, and separate PSG schema, assembly hash, format version and contract fingerprint.
- Accepted artifact: generation/source identity, artifact path/hash, object manifest, changed/retained/retired witnesses, witness visits and compiled/reused/retired objects.

The public projection's `protocolVersion=1` is not the worker agreement version or the PSG schema version. Keep all three identities distinct. A returned transport label does not independently validate deployed bytes. An artifact path is evidence to inspect, never a command to launch directly.

Status includes `current`, `closed`, `busy`, `revocationPending`, `backendError`, `formatterError`, `formatterCleanupPending`, `workerRetirementRequired`, `cleanupPending`, `cleanupError`, `statusFresh`, `statusError`, `workerAvailable` and `workerError`. Cached metadata can remain visible when the observation or worker is unavailable. `executionRequiresRevalidation` is true.

The refusal projection includes authority/provider errors (`wrong_authority`, `wrong_provider`, `unknown_session`, `closed`, `compiler_retired`), admission errors (`busy`, `observation_capacity`, `session_capacity`, `invalid_reservation`, `not_accepted`), compatibility/framing errors and compiler/backend/cleanup/retirement failures. Use the exact `error.code` returned, rather than interpreting a status row as permission. The exhaustive mapping is `ComposerClientJson.refusalCode`; this document does not assign undocumented recovery semantics to every code.

### Formatting and cleanup

Use a stable opaque `document` label, a GUID `incarnation` for that buffer opening, an unsigned revision, the current generation and `configuration=clef-two-space-lf-v1`. A new incarnation retires its predecessor. The preview returns `formatted`, exact base `sourceSha256` and `formatterIdentity`, together with document/incarnation/revision/configuration. It grants neither edit nor execution permission.

Before applying a preview, obtain a new successful reservation and compare the current buffer with the preview's exact immutable base and hash. Save only if the base still matches, then build with that reservation. A superseded preview is not a patch to merge automatically.

The registered tool description specifies at most 32 live/closing formatter handles. At capacity, replacement that initiates cleanup returns `busy`; retry after capacity is freed using the current generation and buffer. A distinct label at capacity with no pending close returns `session_capacity`, requiring a fresh session. There is no registered per-buffer close tool.

Status descriptions specify bounded formatter evidence and autonomous whole-worker retirement when formatter cleanup reaches 30 seconds. `formatterCleanupPending` is separate from logical authority withdrawal; `workerRetirementRequired` is sticky for that epoch. A deadline, cancelled tool wait or closed session does not prove physical drain. Replacement requires observed process exit and a new epoch. These are exposed lifecycle obligations in `ComposerTools.fs`; this audit did not execute the deadline or capacity controls.

## Caller-owned work leases

These registered tools require no Composer session. They admit external work that the daemon does not run itself. They do not confer compiler proof or native execution authority.

| Tool | Arguments | Kind or outcome |
| --- | --- | --- |
| `get_daemon_status` | None | Daemon version, health, memory/process telemetry and safe lease summaries. Composer directory is separate. |
| `acquire_full_build_lease` | None | `full_build` |
| `acquire_test_suite_lease` | None | `test_suite_run` |
| `acquire_run_app_lease` | None | `run_app`; Composer execution still uses `composer_run_current`. |
| `release_work_lease` | `lease_id: string` | `released` or `already_gone_or_not_owned`. Empty input is an error. |

Acquisition returns `kind` and `decision`. `granted` carries `leaseId` and `expiresAt`; `wait` carries `retryAfterSeconds` and `reason`; `refused` carries `reason`. Only a grant admits work. Release on completion or failure using the same bound connection. A successful release RPC returning `already_gone_or_not_owned` is not evidence that the caller held the lease through completion.

Sources: registration in `src/Bozzetto/McpTools.fs`; `src/Bozzetto/Mcp.fs`, `leaseDecisionJson`, `acquireWorkLease` and `releaseWorkLease`.

A lease is a heartbeat, not a command deadline. Current default lifetimes are 10 minutes for a full build, 15 minutes for a suite and four hours for an external app. Renewal extends a live grant; an expired one cannot be renewed. Retries of the same holder/kind retain the queued ask, and abandoned asks are reaped after two minutes without retry. Source: `src/Bozzetto.Core/ExpensiveWorkLease.fs`; governing caller rules: `AGENTS.md`, **Building and testing Bozzetto**.

No renewal or withdrawal MCP tool is registered. Current direct HTTP renewal uses `holder` and `leaseId`; withdrawal uses `holder` and `kind`, at `/api/lease/renew` and `/api/lease/withdraw`. Their JSON bodies in `McpServer.fs` are drift requiring typed-binary replacement. A caller-supplied HTTP holder is not the MCP connection binding. Do not mix holders or claim that MCP acquisition alone supplies an automatic heartbeat. `scripts/work-lease run` manages renewal for caller-owned commands; its current direct wire has the same migration obligation.

Follow wait/refused decisions without bypassing accounting. Investigate memory deferrals rather than polling blindly or stopping another owner's processes. Lease grants and queued Clef/Composer work belong on `/work`; the Dashboard's CPU figure covers only the daemon and its worker, not external leased processes. See [operations.md](operations.md) and [development.md](development.md) for validation under `net10.0` and the unfiltered TRUST acceptance rule.

## Cohort coordination

One implicit cohort exists per daemon. The first joiner becomes conductor. Joining without a resolved retained checkout session can succeed, but supplies no per-session test-matrix binding. `join_cohort` does not open a Composer project or establish a Composer verification session. Sources: `src/Bozzetto/Mcp.fs`, `resolveJoinSession` and `joinCohort`.

Every tool below is attributed in `src/Bozzetto/McpTools.fs`. Required argument names retain their current spelling.

| Tool | Arguments | Behavior |
| --- | --- | --- |
| `join_cohort` | `agentName: string`, `role: string`, optional `working_directory: string = ""` | Roles: Implementer, Verifier or Observer, parsed case-insensitively. First join binds conductor. |
| `leave_cohort` | `agentName: string` | Departs; held claims become orphaned, not free. |
| `get_cohort_status` | None | Published members, roles, claims/fences, test matrix, ledger head and landing queue. |
| `acquire_claim` | `agentName: string`, `scope: string`, `purpose: string` | Exclusive `file:<path>` or `project:<path>` claim. Purpose is one line, at most 200 characters. Returns claim ID and fence. |
| `release_claim` | `agentName: string`, `claimId: string`, `fence: int64` | Only the current holder with the current fence may release. |
| `reassign_claim` | `agentName: string`, `claimId: string`, `toMember: string` | Conductor-only; orphaned claim to a present recipient named by its exact displayed identity. |
| `request_landing` | `agentName: string`, `claims: string`, `commits: string`, `statement: string` | Claims are comma-separated `claimId:fence` pairs, empty for none; commits are comma-separated SHAs. Statement is at most 1000 characters. Queue receipt is not verification or landing. |
| `set_integration_ref` | `agentName: string`, `integrationRef: string` | Retained conductor-gated setup operation. Refuses at the retired provider boundary before worktree, cohort-head, binding or build changes. |

The current role gate allows join/status to every caller, leave to present members, and claim/release/landing actions to Implementers and the conductor. Reassignment and integration setup require conductor authority. The cohort owner still checks membership, scope overlap, holder, fence, recipient and command state. Sources: `src/Bozzetto.Core/Affordances.fs`, `cohortTools`; `src/Bozzetto/Mcp.fs`, cohort command adapters and error mapping.

Claims coordinate edits; they do not replace `composer_reserve_edit`. Leaving without release keeps an orphaned claim for conductor action. Stale fences and conflicting scopes remain loud refusals.

**Landing verification is unavailable:** `request_landing` retains the queue contract, but its production verification journey depended on the retired F# integration provider. Composer integration verification is not implemented. `src/Bozzetto/McpCohortIntegration.fs` implements the setup refusal and reports this boundary in status. Do not describe a queued landing or matrix row as compiler acceptance.

## Agent-work reporting

`report_agent_work` and `get_agent_work` are registered in `src/Bozzetto/McpTools.fs`; admission and reconciliation use the same cohort owner in `src/Bozzetto/McpCohortIntegration.fs`. No Composer session is required. Reporting requires the participant's enrolled, bound MCP connection and present membership, not a claimed agent name.

`report_agent_work` requires every argument below. These are client-reported invocation snapshots, not prompts, thinking, transcripts or tool payloads.

| Arguments | Type and rule |
| --- | --- |
| `run`, `session` | Strings with stable globally unique keys, each at most 128 UTF-8 bytes. `session` here is the reporting client's session key, not a Composer handle. |
| `sequence` | int64, starts at 1 and increases; maximum 9007199254740991. Exact duplicate reports are idempotent. |
| `name`, `model` | Strings, at most 128 UTF-8 bytes each. Name and session cannot change within a run. Model is a label, not attestation. |
| `endpoint`, `project`, `focus`, `operation` | Strings, limits 256, 512, 256 and 128 UTF-8 bytes respectively. Endpoint rejects `@`, `?`, `#` and `=`; do not include credentials. |
| `status` | `running`, `completed`, `failed`, `aborted`, `disconnected` or `unknown`. Every non-running status is terminal. |
| `usageAvailable` | Boolean. False means unknown, not known zero. |
| `input`, `output`, `cacheRead`, `cacheWrite`, `total` | int64 cumulative finalized-message counters, nonnegative and at most 9007199254740991. Total must equal the sum of the four components. |
| `estimateUsd` | Floating-point USD estimate; `-1` means unavailable. Otherwise finite and nonnegative. Not billing. |

All label fields must be nonblank and contain no control characters. When usage is unavailable, supply zero placeholder counters and `estimateUsd=-1`; these placeholders are not recorded as known zero. Known counters and known estimates cannot regress or become unknown. A terminal run rejects newer reports, while its exact last duplicate remains idempotent.

`src/Bozzetto.Core/AgentWork.fs` defines validation and refusal tokens, including `wrong_member`, `stale_sequence`, `conflicting_duplicate`, `terminal_run`, `invalid_field:<field>`, `oversized_field:<field>`, `invalid_usage`, `regressing_usage` and capacity failures. State is bounded to 256 runs, 64 members, 16 recent activity labels per run and a retained-byte budget. Reuse neither another member's run key nor a terminal key for a new invocation.

`get_agent_work` takes no arguments. It attempts owner publication reconciliation once and returns the compact published board or a loud refusal such as `publication_failed`; retained owner state is not silently discarded. Subscribe to `agents://work` for changes. The board includes incarnation and publication sequence; several wide integer values are strings in the current MCP projection. Completion status never establishes test acceptance or compiler proof.

## Resources and subscriptions

Resources are direct URIs without template parameters. `resources/read`, `resources/subscribe` and `resources/unsubscribe` take `uri`. All four currently advertise `application/json` at the external MCP adapter.

| URI | Resource name | Owning observation |
| --- | --- | --- |
| `composer://sessions` | `composer_sessions` | Composer supervisor directory, same owner as `composer_list_sessions`. |
| `cohort://status` | `cohort_status` | Published cohort frame. With no wired owner, reports an empty cohort frame. |
| `agents://work` | `agent_work` | Published agent reports, or empty board when no owner is wired. |
| `sessions://list` | `sessions_list` | Retained session registry, not the Composer directory. Compatibility observation only. |

Registrations: `src/Bozzetto/ComposerTools.fs`, `ComposerResources`; `src/Bozzetto/McpResources.fs`, `BozzettoResources`.

Subscribe on the live connection, read the current resource, and reread after `notifications/resources/updated`. The notification identifies the URI; it is not the new contents, a lossless event journal or execution authority. Unsubscribe when finished. Restore subscriptions and current observations after reconnect instead of treating old handles or cached state as current.

`McpServer.fs` tracks per-connection URI subscriptions. Unsubscribe is idempotent. The subscribe handler records the requested URI without checking it against the resource catalog; subscription acknowledgement does not prove a resource exists. Notifications go only to subscribed connections, with a 500 ms per-send bound; failed sends remove the tracked connection and its subscriptions.

Composer updates follow `supervisor.Changed`, and agent-work updates follow `owner.WorkChanged`. Cohort and retained-session updates use version-gated owner/model observations in `McpServer.fs`, `wireCohortEventSubscription` and `wireSessionsResourceSubscription`. These are implemented push hooks, not evidence of atomic reconnect reconciliation, universal foundation adoption or Ranvier settlement conformance. Some hooks still share retained SSE-oriented implementation code; that is not an endorsement of SSE as the browser pipeline.

The current tracker registers a server in the tool-call filter. A resource-only client that has never called a tool must not assume subscription alone made it a tracked notification recipient. Make an initial status tool call, then subscribe/read. Repairing this subscription seam belongs to the MCP owner, not to a new polling mechanism.

## Local friction and data management

These four tools are registered in `src/Bozzetto/McpTools.fs` and operate on local diagnostics. They do not supervise a compiler or grant acceptance.

| Tool | Arguments | Behavior |
| --- | --- | --- |
| `get_friction_summary` | None | Compact summary for the running version, or explicit absence/read failure. |
| `get_friction_report` | None | Structured local report of blockers, tool transitions, feedback and observed signals. No store currently returns an empty object. |
| `report_friction` | `tool_name: string`, `feedback_kind: string`, `short_reason: string`, optional `alternative_tool: string = ""` | Records local feedback if a store exists. |
| `manage_local_data` | Optional `action: string = "status"`, `store: string = "all"` | Status reports storage/retention. Clear targets friction, cohort or all; cohort clearing is refused while a cohort is running. Inspect the returned result. |

Supported feedback tokens are `output_too_large`, `intent_unclear`, `name_misleading`, `needed_another_tool` and `trust_not_established`. The current handler maps an unrecognized token to the last category rather than rejecting it. Use a listed token; do not rely on that permissive behavior as a new contract.

Diagnostic recording and JSON-shaped reports at MCP do not establish the separate-process sink requirement. That delivery and the remaining direct-wire migrations need owner-specific evidence. Retired F# catalog descriptions, runtime patching and internal SSE/JSON paths must be removed at their owning boundaries, not preserved here as alternate workflows.
