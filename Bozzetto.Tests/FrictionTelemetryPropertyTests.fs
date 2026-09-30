module Bozzetto.Tests.FrictionTelemetryPropertyTests

open Expecto
open Bozzetto.Features.FrictionTelemetryTypes

let private ok = function
  | Ok value -> value
  | Error err -> failwith err

[<Tests>]
let tests =
  testList "Friction telemetry properties" [
    testProperty "clean completion always maps to succeeded outcome kind" <| fun () ->
      let event =
        { OccurredAtUtc = System.DateTimeOffset.UtcNow
          Session = SessionRef.create "session-1" |> ok
          Tool = ToolName.create "send_fsharp_code" |> ok
          Intent = IntentKind.ExploreCode
          Outcome = FrictionOutcome.CompletedCleanly
          Duration = DurationMs.create 1 |> ok
          FollowUp = FollowUp.SessionEnded
          ContextCost = ContextCost.Tiny
          BozzettoVersion = ""; AgentKey = ""; ErrorSignature = "" }
      FrictionEvent.outcomeKind event = OutcomeKind.Succeeded

    testProperty "blocked outcomes always map to blocked outcome kind" <| fun () ->
      let blockers = [
        BlockerKind.SessionAmbiguous
        BlockerKind.LoadedStateStale
        BlockerKind.ExactTestNotFound
      ]
      blockers
      |> List.forall (fun blocker ->
        let event =
          { OccurredAtUtc = System.DateTimeOffset.UtcNow
            Session = SessionRef.create "session-1" |> ok
            Tool = ToolName.create "run_tests" |> ok
            Intent = IntentKind.RunExactTest
            Outcome = FrictionOutcome.EncounteredBlocker blocker
            Duration = DurationMs.create 2 |> ok
            FollowUp = FollowUp.NoFollowUpYet
            ContextCost = ContextCost.Focused
            BozzettoVersion = ""; AgentKey = ""; ErrorSignature = "" }
        FrictionEvent.outcomeKind event = OutcomeKind.Blocked)
  ]
