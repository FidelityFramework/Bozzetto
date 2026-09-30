module Bozzetto.Tests.EvalActorStragglerTests

open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.AppState

/// The generation-supersession / no-resurrection decision this file used to
/// prove via a real FSI-warmup Integration harness (spawning a bare
/// eval-actor, submitting a straggler eval that ignores cancellation, then
/// resetting while it's in flight) is now the `no-resurrection` invariant in
/// Bozzetto.Simulation/EvalActorSim.fs + EvalActorInvariants.fs, folding the
/// REAL Bozzetto.EvalActorDecision.decide, with a generation-blind
/// twin proving the invariant has teeth. Asserted in
/// Bozzetto.Tests/EvalActorSimTests.fs — proven in milliseconds. See
/// EvalActorSimTests.fs "resetDuringEval: a straggler Finished for the
/// superseded generation is dropped" and "REPRODUCED — generation-blind
/// twin resurrects a superseded straggler".
///
/// `supersededAtWorkerBoundaryTests` below is UNCHANGED — it is the one real
/// process-boundary smoke worth keeping: it proves the structured
/// `BozzettoError.EvalSupersededByReset` case crosses the worker HTTP
/// boundary intact, which the pure DST above cannot exercise.
[<Tests>]
let evalActorStragglerTests = testList "Eval actor straggler" []

/// An actor that answers every Eval with a fixed response.
let private answeringActor (response: EvalResponse) : AppActor =
  MailboxProcessor.Start(fun inbox ->
    let rec loop () = async {
      match! inbox.Receive() with
      | Eval(_, _, reply) -> reply.Reply response
      | _ -> ()
      return! loop ()
    }
    loop ())

let private workerEval (actor: AppActor) =
  Bozzetto.Server.WorkerMain.handleMessage
    actor (fun () -> SessionState.Ready) (fun () -> Affordances.EvalStats.empty) (fun () -> None) []
    (fun () -> Bozzetto.Features.LiveTesting.LiveTestHookResult.noOp) (fun _ -> ()) (fun () -> [||], [])
    (fun _ _ -> async { return Result.Error (BozzettoError.EvalFailed "EvalLiveTestFile not available on this test worker") })
    Bozzetto.Server.WorkerMain.noAppRuns
    (WorkerProtocol.WorkerMessage.EvalCode("x", "r1"))
  |> Async.StartAsTask

[<Tests>]
let supersededAtWorkerBoundaryTests =
  testList "Eval superseded by reset at the worker boundary" [

    testTask "WHY — the worker forwards the superseded-by-reset case instead of a stack dump, because the agent must learn to re-run its code rather than fix it" {
      let response =
        { EvaluationResult = Error (BozzettoErrorException BozzettoError.EvalSupersededByReset :> exn)
          Diagnostics = [||]
          EvaluatedCode = "x"
          Metadata = Map.empty }
      let! resp = workerEval (answeringActor response)
      match resp with
      | WorkerProtocol.WorkerResponse.EvalResult(_, Error err, _, _) ->
        err |> Expect.equal "the structured case crosses the process boundary" BozzettoError.EvalSupersededByReset
      | other -> failtestf "expected an EvalResult error, got %A" other
    }
  ]
