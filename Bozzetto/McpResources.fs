module Bozzetto.Server.McpResources

/// MCP resources for cohort/session state (Phase 1 item 12,
/// bozzetto-multiagent-vision.md §5.6/§8.2/§10): "Build MCP resource
/// registration + `resources/subscribe`". Agents read — and, once
/// subscribed (`configureMcpProtocol` in McpServer.fs wires
/// `resources/subscribe`/`unsubscribe` and pushes
/// `notifications/resources/updated`), watch — this instead of polling
/// `get_cohort_status`/`list_sessions`.
///
/// Content is produced by the same PURE read-model projections the existing
/// SSE wire rows already use (`SseWriter.cohortFrameJson`,
/// `SessionOperations.sessionsToJson`) — §5.6's "one read model, no new
/// channel": a resource is a second VIEW onto state that already has a
/// single source of truth, never a second copy of it.

open System.ComponentModel
open System.Threading.Tasks
open ModelContextProtocol.Server
open Bozzetto.McpTools

/// The daemon's single per-daemon cohort (cohort-integration-plan.md D1) —
/// a direct resource (no URI parameters), not a template.
[<Literal>]
let CohortStatusUri = "cohort://status"

[<Literal>]
let AgentWorkUri = "agents://work"

/// The daemon's current session list — a direct resource.
[<Literal>]
let SessionsListUri = "sessions://list"

/// Resources keep their existing camelCase wire schema.
let private jsonOpts = Bozzetto.JsonCasing.CamelCase

/// A `CohortFrame` for a cohort that has never had a command applied to it —
/// used only when no `CohortOwner` is wired (most daemons/tests). "No
/// cohort" is a legitimate, stable state to report, not a failure to read
/// the resource.
let private emptyCohortFrame () : Bozzetto.Cohort.CohortFrame<Bozzetto.MemberTable.MemberId> =
  let head : Bozzetto.Cohort.LedgerHead<Bozzetto.MemberTable.MemberId> =
    { Seq = 0L<Bozzetto.Measures.ledgerSeq>; State = Bozzetto.Cohort.CohortState.empty () }
  Bozzetto.Cohort.project head [||]

type BozzettoResources(ctx: McpContext) =

  [<McpServerResource(UriTemplate = AgentWorkUri, Name = "agent_work", MimeType = "application/json")>]
  [<Description("Bound, client-reported agent runs and cumulative usage; subscribed owner changes, no polling. Model labels and USD estimates are not attestation or billing.")>]
  member _.AgentWork() : string =
    match ctx.CohortOwner with
    | Some owner -> Bozzetto.AgentWork.json (owner.ReadWork())
    | None -> Bozzetto.AgentWork.json Bozzetto.AgentWork.empty

  [<McpServerResource(UriTemplate = CohortStatusUri, Name = "cohort_status", MimeType = "application/json")>]
  [<Description("The daemon's current cohort state (members, claims, test matrix) as JSON — the same read model get_cohort_status reports. Subscribe (resources/subscribe) to be pushed notifications/resources/updated whenever the cohort changes, instead of polling.")>]
  member _.CohortStatus() : string =
    match ctx.CohortOwner with
    | Some cohortOwner -> Bozzetto.SseWriter.cohortFrameJson jsonOpts (cohortOwner.ReadFrame())
    | None -> Bozzetto.SseWriter.cohortFrameJson jsonOpts (emptyCohortFrame ())

  [<McpServerResource(UriTemplate = SessionsListUri, Name = "sessions_list", MimeType = "application/json")>]
  [<Description("The daemon's current session list as JSON — the same read model list_sessions reports.")>]
  member _.SessionsList() : Task<string> =
    task {
      let! sessions = ctx.SessionOps.GetAllSessions()
      return Bozzetto.SessionOperations.sessionsToJson jsonOpts sessions
    }
