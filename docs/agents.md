# Agents and coordinated work

Bozzetto coordinates agents and humans working on Clef/Composer projects. The shared daemon owns client admission, routing and supervised process lifetimes. Composer is a differential compiler. An agent's name, progress report, cohort claim or resource lease does not establish compiler proof or execution authority.

Three components define the forward contract:

| Component | Responsibility at the agent boundary |
| --- | --- |
| **Fidelity.PSG** | Compiler-owned evidence, delivered only within source-authorized scopes. Bozzetto presents evidence and refusals; an agent report cannot manufacture proof. |
| **Fidelity.FSharp.Incremental** | Declared dependency bookkeeping and explicitly started work, with shared demand, withdrawal and joined lifetimes. Bozzetto retains client admission and process supervision. |
| **Ranvier** | The signal/memo/effect reference shape for reactive observations, with one mutation owner and immutable input admission. This is not compiler settlement or physical-drain authority. |

See [component contracts](Bozzetto_Fidelity_Component_Contracts.md), [foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md) and [reactive push](reactive-push.md). These responsibilities are requirements, not a claim that every agent-facing path already implements them.

Implementation statements below are based on source reads, not a live connection or executed validation. The governing repository rules are in [`AGENTS.md`](../AGENTS.md), particularly **Daemon lifecycle**, **Clef and Composer work**, **Building and testing Bozzetto**, **Architecture** and **Multi-agent and worktree sessions**.

## Connect and establish current identity

Use the shared installed daemon, independently of any individual client's lifetime. Port **47749** serves MCP, HTTP and the browser UI; **47750** is the control listener. Configure an MCP client for Streamable HTTP at `http://127.0.0.1:47749/`. Connection arguments and resources belong in [mcp-tools.md](mcp-tools.md).

1. Make one bounded readiness probe:

   ```sh
   curl --fail --max-time 3 http://127.0.0.1:47749/health
   ```

2. If the daemon is absent, use `scripts/start-shared-daemon`, then make the bounded probe. Do not launch it in the foreground, wait for its lifetime or loop on health checks.
3. Confirm a live MCP connection exposes `get_daemon_status`, `composer_list_sessions` and `composer_open_project`. A configured server entry is not evidence of connection or availability.
4. Inspect the installed daemon and worker identities and their readiness. A checkout change does not replace a loaded compiler distribution. Report exact version, type and protocol errors.
5. Use `/composer` for the human view and `composer://sessions` for the MCP session directory. Subscribe to supported resources rather than adding status polling.

Current connection checks belong in [operations.md](operations.md), not dated deployment checkpoints. The root `AGENTS.md` still names `Bozzetto_Live_Provider_Checkpoint_2026-09-30.md`; its active navigation duty must move to operations when the owner integrates this set. Until that guidance is updated, this staged document does not silently repeal it. Current identity and readiness must be observed against the installed provider; dated PIDs, package paths and test counts cannot establish today's state. Deployment, matching daemon/worker replacement and rollback belong in [operations.md](operations.md).

Development restarts are authorized by `AGENTS.md`, but check active leases and Composer work first. Never interrupt another caller's active work or replace unrelated compiler components. Keep the previous installed release available for rollback.

## Keep the identities distinct

| Identity | Meaning and restriction |
| --- | --- |
| MCP transport connection | The enrolled participant identity. Two connections using the same display name are different members. |
| Checkout or worktree | The filesystem routing boundary. An unresolved worktree must not silently use the main checkout's session. |
| Composer host, session and epoch | Handles returned for the explicit project and worker incarnation. Retain them and do not reuse them after worker replacement. |
| Cohort claim and fence | Coordination ownership over a declared scope. Present the current fence when releasing or transferring ownership. |
| Agent-work run and report sequence | A participant's bounded, ordered progress facts. These are not Composer handles or proof identities. |
| Lease holder and lease ID | Resource admission for caller-owned work. The holder must remain stable across retries; the grant ID identifies one admitted lease. |

`AGENTS.md` specifies checkout-aware session routing and a `Gone` refusal with a create hint for a worktree without a session. That rule does not revive retained F# session creation. For Clef work, open the explicit `.fidproj` through Composer and retain its returned handles. If a client route still requires a retired session provider, report the refusal as a boundary defect rather than creating a REPL or falling back to another checkout.

For strict agent-work reporting, `Bozzetto/McpCohortIntegration.fs` requires a nonempty bound MCP transport identity and has no name fallback. The cohort owner then requires that member to be present. Changing the report's name or session label cannot transfer a run to another connection.

## The agent Composer loop

The prescribed workflow in `AGENTS.md` and the current Composer tool reference is:

1. Read `composer_list_sessions`. Open an explicit absolute `.fidproj` with `composer_open_project`.
2. Retain the returned host, session and compiler epoch for subsequent operations.
3. Coordinate overlapping edits with the cohort. Call `composer_reserve_edit` and receive success before changing source or dependency files. Keep the opaque reservation; its admitted-build and shared-observer semantics belong in [composer-workflow.md](composer-workflow.md) and [mcp-tools.md](mcp-tools.md), not a provider-wide single-use rule here.
4. Make the edit, then pass that reservation to `composer_build`.
5. Inspect compiler diagnostics, refusals and accepted evidence. Execute only through `composer_run_current`, passing arguments as individual strings.
6. Read `composer_session_status` for pending withdrawal, cleanup and errors. Close your session with `composer_close_session` when finished, retaining responsibility for unresolved cleanup.

A cohort claim does not replace the Composer reservation. Enqueue acceptance, a timed-out reply, a status response and a cached artifact path do not grant permission to write or execute. Reconcile uncertain reservation outcomes before writing. The required lifecycle withdraws affected authority before edit permission, while physical cancellation and drain can remain pending and observable.

Human reservations, cancellation and worker retirement affect agents too. Coordinate `composer_retire_worker` before replacing a compiler distribution. A new epoch needs new session handles and reservations; cleanup failure is not permission to reuse the old ones.

See [composer-workflow.md](composer-workflow.md) for conflicts, epochs and per-region witness/object validation. Fidelity.PSG evidence must retain compiler-owned scope and identity. Equal display summaries or unchanged row counts cannot prove that evidence remains valid.

No embedded F# session hosting, REPL, FSI warmup or runtime patching belongs in this loop. LLVM ORC JIT remains planned. Retained F# tools in a catalog do not establish an execution service, and editor saves must not be assumed to build or run a Composer revision.

## Cohort ownership and claims

The current MCP reference describes one implicit cohort per daemon, with the first joiner becoming conductor. Members join with `join_cohort`, inspect `get_cohort_status` or subscribe to `cohort://status`, and leave with `leave_cohort`. Connection identity, not `agentName`, owns membership.

The implementation owner is `Bozzetto.Core/Features/CohortOwner.fs`:

- Commands are serialized through one mailbox and applied through `Cohort.decide`.
- Successful cohort transitions append their ledger entry before the reply. The ledger is replayed to recover cohort state.
- Published frames and state are read without posting a new mailbox command. Events are emitted after publication.
- Landing effects run outside the mailbox and return typed completion commands to the same owner. An unavailable affected-test set or verifier result becomes inconclusive, not an empty passing result.

The implemented claim vocabulary in `Bozzetto.Core/Cohort.fs` is file or project scope, exclusive only. Project overlap is directory-based: the project file's containing directory defines its scope, including nested paths. Normalize and agree repository-relative paths before claiming work; do not imply symbol-level coordination or filesystem enforcement.

| Operation | Coordination rule |
| --- | --- |
| `acquire_claim` | Acquire the relevant `file:<path>` or `project:<path>` scope before editing shared territory. Keep the returned identity and fence. |
| `release_claim` | Release your own claim with its current fence. A stale fence must not be treated as success. |
| `leave_cohort` | Remaining held claims become orphaned. Departure does not silently transfer ownership. |
| `reassign_claim` | The conductor reassigns an orphaned claim to a present member. Reconcile the new fence before continuing. |

These tool rules are described in [mcp-tools.md](mcp-tools.md); claim state and fence types are in `Bozzetto.Core/Cohort.fs`. Claims coordinate people and agents, not compiler proofs, work admission or physical process lifetime.

The landing state machine and effect-dispatch seam exist, but do not advertise an available Composer landing verifier. `Bozzetto/McpCohortIntegration.fs` refuses `set_integration_ref` before changing worktrees, cohort heads or builds because its F# integration provider is retired. Its status text explicitly says Composer integration is not implemented. `CohortOwner.start` also supplies a nonperforming default landing performer. A queue entry is not evidence that a verification journey can complete.

## Agent-work board

The board holds bounded client-reported facts, not transcripts, tool payloads, process supervision or a durable execution journal. Its pure rules live in `Bozzetto.Core/AgentWork.fs`; admission and publication belong to `Bozzetto.Core/Features/CohortOwner.fs`. MCP reporting and reconciliation are in `Bozzetto/McpCohortIntegration.fs`.

A report identifies a run, client session label, increasing report sequence, name, model, endpoint, project, focus, operation and status, with optional cumulative usage. Status is one of `running`, `completed`, `failed`, `aborted`, `disconnected` or `unknown`. Every status other than `running` is terminal in the current implementation.

Use `report_agent_work` on the participant's enrolled connection. Read or reconcile with `get_agent_work`. Consult [mcp-tools.md](mcp-tools.md) for the current advertised argument shape; do not infer API availability solely from documentation.

The implemented admission rules include:

- A run belongs to its first reporting member. A different member receives `wrong_member`.
- An exact duplicate report is idempotent. Lower sequences refuse as `stale_sequence`; a different report at the same sequence refuses as `conflicting_duplicate`.
- After a terminal report, a higher-sequence update refuses as `terminal_run`. Start a fresh run for new work.
- Session and name remain fixed for an existing run. Usage counters cannot regress or disappear after being supplied.
- Text fields are nonempty, free of control characters and bounded by UTF-8 byte count. Endpoint labels reject `@`, `?`, `#` and `=`. Send a safe endpoint label, never credentials, query strings or private request content.
- Usage counters are nonnegative and bounded; total must equal input, output, cache-read and cache-write counters. An optional cost estimate must be finite and nonnegative. These remain client estimates, not billing authority.

The state retains at most 256 runs from at most 64 distinct members and a conservative 2 MiB byte budget. Each run retains at most 16 recent operation labels and counts omitted activity. Admission allows at most 32 outstanding reporting or reconciliation commands. Capacity exhaustion refuses; no automatic run eviction is implemented in this owner.

Publication failure is not rollback. `CohortOwner` retains the newly admitted work state, replies `publication_failed` and marks publication pending. `get_agent_work` makes one owner-serialized reconciliation attempt and then reads the publication. Correct the failure and retry explicitly; do not create a second writer or poll for settlement. Reporting slots are released when the owner settles the command, not when an observing caller cancels its wait.

Reports and board reads on a bound connection record participant activity in `McpCohortIntegration.fs`. Member departure changes that member's `running` rows to `disconnected`; it does not claim that an associated process physically stopped. A board heartbeat does not renew an expensive-work lease, and a completed row does not certify compiler acceptance or cleanup.

## Leased caller-owned work

Every caller-owned build and suite requires a lease. Composer operations own their internal accounting; do not add a second external grant around them without an owner-defined reason. For Bozzetto implementation work, use the existing helper:

```sh
BOZZETTO_AGENT=implementer BOZZETTO_PROJECT="$PWD" \
  scripts/work-lease run full_build dotnet build
BOZZETTO_AGENT=implementer BOZZETTO_PROJECT="$PWD" \
  scripts/work-lease run test_suite_run dotnet <testDll> --summary
```

Use a stable, distinct holder per agent/project; start only on grant, honor Wait/refusal, renew owned work, release on every terminal path and explicitly withdraw only when abandoning a queued ask. The detailed pool, wrapper and HTTP lifecycle contract belongs in [operations.md, Leases: admission, heartbeat and queues](operations.md#leases-admission-heartbeat-and-queues), with MCP lifecycle limits in [mcp-tools.md](mcp-tools.md). The current `scripts/work-lease` run case leaves a deferred ask queued on exit 75; the previous `set -e` withdrawal warning is obsolete, not a remaining helper gap.

If memory defers work, investigate available RAM and swap, process RSS with parents, and cgroup usage. Separate daemon and worker usage from leased builds, unrelated work and language servers. Ask before stopping processes you do not own or changing scheduling policy. Granted and queued compiler-related leases belong beside the worker on `/composer`; the Dashboard keeps agents and machine resources, and its CPU figure covers only daemon and worker (`AGENTS.md`).

## Foundation lifetimes and human coordination

The foundation adoption contract requires complete declared reads and cold work before admission. It does not dynamically discover compiler premises. An `Offer` establishes declared dependency and lifetime eligibility, not PSG proof validity, artifact acceptance or execution permission.

Keep these acknowledgements separate:

- **Claim committed:** coordination ownership changed.
- **Reservation acknowledged:** the required authority withdrawals committed and the source write is permitted.
- **Lease granted:** resource admission exists for caller-owned work.
- **Report admitted or published:** bounded progress facts changed or became visible.
- **Authority withdrawn:** obsolete work cannot publish or launch under old authority.
- **Physically drained:** owned children, callbacks and cleanup have joined.

The current cohort mailbox, reporting semaphore and pure lease pool demonstrate specific local ownership rules, not complete foundation-host adoption. `AgentWork.Publication` supplies an injected publish/read/change seam; its comment describes production settlement through the shared core, while its in-memory implementation uses a reference and event. The inspected files alone do not establish production foundation wiring or Ranvier deployment.

The required reactive plane admits immutable snapshots through one mutation domain and pushes typed changes. A memo's equal display value cannot erase changed proof identity. Effect cleanup, mailbox disposal, observer cancellation and a deadline cannot certify physical drain. Withdrawal of one client's demand must preserve a producer still needed by another consumer; shared cancellation and owner retirement need distinct operations.

Before a handoff, record checkout, project, connection/member context, host/session/epoch, claim/fence, outstanding reservation, lease or queued ask, and pending cleanup. Do not transfer a connection-bound capability by copying a display label. Release your claims and leases, withdraw abandoned asks, report terminal work honestly and identify anything still owned. Scope, scheduling, dependency and policy decisions remain with the owner.

Delegated briefs must include the retrieval rule and lease lifecycle because sub-agents do not inherit `AGENTS.md`. Validation guidance and the one-page evidence handoff belong in [development.md](development.md): targeted inner-loop runs plus one unfiltered TRUST acceptance per increment. A board status or cohort matrix does not replace that ledger.

## Binary protocol boundary and remaining drift

Direct daemon-worker, daemon-browser and agent-owned interchange must use **typed binary contracts**. Domain types alone are not evidence of binary transport. Wire identities must preserve epoch, request, scope and sequence distinctions; reconnect must reconcile authoritative snapshots without reviving stale permissions. See [binary-format-spec.md](binary-format-spec.md) and [reactive-push.md](reactive-push.md).

MCP's externally standardized JSON-RPC transport is the sole external exception. JSON-shaped responses inside the MCP adapter do not authorize a second direct JSON API. Fidelity.Data belongs at JSON export edges and the independent, separate-process MailboxProcessor-driven [diagnostic sink](Bozzetto_Diagnostic_Sink.md), not as a serializer substitution for forbidden direct interchange.

The inspected source still exposes drift:

| Source evidence | Classification |
| --- | --- |
| `scripts/work-lease` constructs and parses JSON HTTP request/reply bodies using `jq`. | Current direct agent/daemon wire drift. Preserve lease semantics while replacing the wire with a typed binary boundary. |
| `Bozzetto.Core/AgentWork.fs` builds a Fidelity.Data JSON projection explicitly shared by MCP and the bridge. | The shared browser/direct-interchange projection violates the forward contract. Keep MCP adaptation at its external boundary and remove JSON from direct delivery. |
| `Bozzetto/McpCohortIntegration.fs` returns JSON text for reporting and board reads. | Acceptable only as external MCP adaptation, not a reusable internal protocol authority. |
| `CohortOwner.fs` contains inherited SSE-oriented event comments. | A local typed event exists; the comments do not establish the required binary browser pipeline. SSE is not the current UI contract. |

Changing these serializers would not satisfy the remit. Binary agent transport, atomic reconnect reconciliation, complete foundation lifetime adoption and distributed agent authority require separate implementation and acceptance evidence. Do not describe native hosting, distributed lab-node execution or Composer landing verification as implemented on the strength of these coordination seams.
