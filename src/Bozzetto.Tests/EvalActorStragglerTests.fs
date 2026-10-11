module Bozzetto.Tests.EvalActorStragglerTests

open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.WorkerProtocol

/// The actual generation decision is covered by EvalActorSimTests, including
/// the generation-blind negative twin. This separate check proves that its
/// typed refusal survives the retained wire codec; no worker host is implied.
[<Tests>]
let supersededAtWorkerBoundaryTests =
  testList "Eval supersession wire identity" [
    testCase "supersession remains a typed refusal across serialization" <| fun _ ->
      let error = BozzettoErrorException BozzettoError.EvalSupersededByReset
      let response = WorkerResponse.EvalResult("r1", Error error.Error, [], Map.empty)
      let received = Serialization.serialize response |> Serialization.deserialize<WorkerResponse>
      match received with
      | WorkerResponse.EvalResult(replyId, Error failure, _, _) ->
        replyId |> Expect.equal "request identity survives" "r1"
        failure |> Expect.equal "the agent learns to rerun its code" BozzettoError.EvalSupersededByReset
      | other -> failtestf "expected a typed EvalResult refusal, got %A" other
  ]
