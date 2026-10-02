module Bozzetto.Tests.DashboardWorkflowSwitchApiWiringTests

open System
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.WorkerProtocol
open Bozzetto.Server.Dashboard

let private sid = SessionId.validate "0a0b0c0d" |> Result.defaultWith failwith

[<Tests>]
let apiWiringTests =
  testList "Dashboard shared workflow owner" [
    testTask "every offered workflow reaches the same typed session owner exactly once" {
      for target in Bozzetto.Server.DashboardTypes.WorkflowSwitch.options do
        let calls = ResizeArray<string * WorkflowTypes.SessionWorkflow>()
        let operations =
          { SessionManagementOps.stub with
              SwitchWorkflow = fun identity workflow ->
                calls.Add(identity, workflow)
                Task.FromResult(Ok "owner accepted") }
        let! response = switchWorkflow operations sid target
        response |> Expect.equal "owner response preserved" (Ok "owner accepted")
        calls |> Seq.toList |> Expect.equal "exact address and typed workflow" [ SessionId.value sid, target ]
    }
    testTask "owner refusal remains a refusal with its original diagnostic" {
      let error = BozzettoError.SessionNotFound(SessionId.value sid)
      let operations = { SessionManagementOps.stub with SwitchWorkflow = fun _ _ -> Task.FromResult(Error error) }
      let! response = switchWorkflow operations sid WorkflowTypes.SessionWorkflow.Interactive
      response |> Expect.equal "refusal preserved" (Error(BozzettoError.describe error))
    }
    testTask "owner failure is reported and is never retried through another provider" {
      let mutable calls = 0
      let operations =
        { SessionManagementOps.stub with
            SwitchWorkflow = fun _ _ ->
              calls <- calls + 1
              Task.FromException<Result<string, BozzettoError>>(InvalidOperationException "owner failed") }
      let! response = switchWorkflow operations sid WorkflowTypes.SessionWorkflow.Interactive
      calls |> Expect.equal "one invocation" 1
      response |> Expect.equal "failure diagnostic" (Error "Session workflow operation failed: owner failed")
    }
  ]
