namespace Bozzetto

open System.Threading.Tasks
open Bozzetto.Server
open Bozzetto.McpTools
open Bozzetto.McpSessionRouting

// ── Integration ref/worktree (item 14c) ─────────────────────────────────────
//
// `Cohort.CohortState.IntegrationHead` (the git sha) is the only piece of this
// daemon's cohort-integration configuration that lives in the replayable
// ledger. The worktree path, branch, and daemon-owned session id are process-
// local handles and intentionally do not.
module McpCohortIntegration =

  [<RequireQualifiedAccess>]
  type IntegrationSession =
    | Started of sessionId: string
    | Failed of reason: string
    | Pending

  type CohortIntegrationBinding = {
    WorktreePath: string
    Branch: string
    Session: IntegrationSession
  }

  /// The one process-global integration binding consumed by the landing
  /// performer and the MCP status/setup tools.
  let cohortIntegrationRef : CohortIntegrationBinding option ref = ref None

  /// This inherited operation provisions an F# integration session. Refuse
  /// before changing a worktree, cohort head or binding, or starting a build.
  let setIntegrationRef (_ctx: McpContext) (_agentName: string) (_integrationRef: string) : Task<Result<string, BozzettoError>> =
    Task.FromResult (
      ExternalFSharpService.refuse ())

  /// Strict agent reporting shares this tool feature's cohort owner and the
  /// ambient transport binding. Unlike retained APIs it has NO name fallback.
  let reportAgentWork (ctx: McpContext) (report: AgentWork.Report) : Async<Result<string, BozzettoError>> = async {
    match currentTransportSessionId.Value, requireCohortOwner ctx with
    | Some transport, Ok owner when not (System.String.IsNullOrWhiteSpace transport) ->
      let! result = owner.ReportWork(MemberTable.MemberId.Mcp transport, report)
      return result |> Result.map (fun sequence -> sprintf "{\"sequence\":\"%d\"}" sequence)
                    |> Result.mapError (fun error -> BozzettoError.CohortActionFailed(AgentWork.code error, "Correct the report on this same bound connection; reconcile publication with get_agent_work."))
    | _, Error error -> return Error error
    | _ -> return Error(BozzettoError.CohortActionFailed("bound_connection_required", "Use the participant's enrolled MCP connection."))
  }

  /// Reconciliation is serialized by the owner and shares report admission.
  /// No polling: one demand, one attempt, then a typed success or loud refusal.
  let getAgentWork (ctx: McpContext) : Task<Result<string, BozzettoError>> = task {
    match requireCohortOwner ctx with
    | Error error -> return Error error
    | Ok owner ->
      let! result = owner.ReconcileWork() |> Async.StartAsTask
      return result |> Result.map (fun _ -> AgentWork.json (owner.ReadWork()))
                    |> Result.mapError (fun error -> BozzettoError.CohortActionFailed(AgentWork.code error, "Owner state is retained; retry get_agent_work after fixing publication."))
  }

  let getCohortStatus (ctx: McpContext) : Task<Result<string, BozzettoError>> =
    task {
      match requireCohortOwner ctx with
      | Error e -> return Error e
      | Ok owner ->
        let integration =
          match cohortIntegrationRef.Value with
          | None -> "Integration session: unavailable; embedded F# integration hosting is retired. Composer integration is not implemented."
          | Some b ->
            match b.Session with
            | IntegrationSession.Started sid -> sprintf "Integration session: %s (started)" sid
            | IntegrationSession.Failed reason -> sprintf "Integration session: FAILED to start — %s" reason
            | IntegrationSession.Pending -> "Integration session: pending"
        return Ok (renderCohortFrame (owner.ReadFrame()) + integration + "\n")
    }
