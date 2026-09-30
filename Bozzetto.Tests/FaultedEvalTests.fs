module Bozzetto.Tests.FaultedEvalTests

open System.IO
open System.Threading
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.AppState

let private quietLogger = Bozzetto.Tests.TestInfrastructure.quietLogger

let private createFaultedActorResult () : System.Threading.Tasks.Task<Bozzetto.ActorCreation.ActorResult> =
  let args =
    { Bozzetto.ActorCreation.mkCommonActorArgs quietLogger false ignore Bozzetto.Args.ProjectLoadConfig.empty with
        OutStream = null :> TextWriter }
  Bozzetto.ActorCreation.createActor args

[<Tests>]
let faultedEvalTests =
  testList "Faulted eval" [
    testTask "faulted session rejects eval without losing faulted state" {
      let! (result: Bozzetto.ActorCreation.ActorResult) = createFaultedActorResult ()
      let! becameFaulted =
        Bozzetto.Tests.TestInfrastructure.awaitCondition 5000 (fun () -> result.GetSessionState() = SessionState.Faulted)
      becameFaulted
      |> Expect.isTrue "actor should enter faulted state when warmup fails"

      let request = { Code = """printfn "hello world";;"""; Args = Map.empty }
      let! (response: EvalResponse) =
        result.Actor.PostAndAsyncReply(fun reply -> Eval(request, CancellationToken.None, reply))
        |> Async.StartAsTask

      match response.EvaluationResult with
      | Error ex ->
        ex.Message
        |> Expect.stringContains "faulted eval should tell the user how to recover" "hard_reset_fsi_session"
      | Ok output ->
        failtestf "expected eval to fail for a faulted session, but got: %s" output

      result.GetSessionState()
      |> Expect.equal "faulted eval should not transition the session back to ready" SessionState.Faulted
    }

    testTask "faulted session rejects enablestdout without crashing" {
      let! (result: Bozzetto.ActorCreation.ActorResult) = createFaultedActorResult ()
      let! becameFaulted =
        Bozzetto.Tests.TestInfrastructure.awaitCondition 5000 (fun () -> result.GetSessionState() = SessionState.Faulted)
      becameFaulted
      |> Expect.isTrue "actor should enter faulted state when warmup fails"

      try
        result.Actor.Post(EnableStdout)
        // The actor handles its mailbox in order, so the reply to a later
        // message proves EnableStdout has been processed.
        let! _ =
          result.Actor.PostAndAsyncReply(fun reply -> Eval({ Code = "();;"; Args = Map.empty }, CancellationToken.None, reply))
          |> Async.StartAsTask
        result.GetSessionState()
        |> Expect.equal "enablestdout on faulted should not transition session" SessionState.Faulted
      with ex ->
        failtestf "enablestdout on faulted session should not throw, but got: %s" ex.Message
    }
   ]
