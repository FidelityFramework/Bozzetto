module Bozzetto.Tests.SessionCreationUxTests

open System
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Tests.SharedGenerators

let mkSnap id = {
  SessionSnapshot.Id = id
  Name = None
  Status = SessionDisplayStatus.Starting
  Projects = ["test.fsproj"]
  EvalCount = 0
  UpSince = DateTime.UtcNow
  LastActivity = DateTime.UtcNow
  WorkingDirectory = "."
}

let creatingSessionGuardTests = testList "CreatingSession guard" [
  testCase "model starts with CreatingSession = false" <| fun () ->
    (BozzettoModel.initial()).CreatingSession
    |> Expect.isFalse "should start false"

  testCase "CreateSession action sets CreatingSession true" <| fun () ->
    let model', effects =
      BozzettoUpdate.update
        (BozzettoMsg.Editor (EditorAction.CreateSession ["test.fsproj"]))
        (BozzettoModel.initial())
    model'.CreatingSession
    |> Expect.isTrue "should be true after CreateSession"
    effects
    |> List.exists (function
      | BozzettoEffect.Editor (EditorEffect.RequestSessionCreate _) -> true
      | _ -> false)
    |> Expect.isTrue "should emit RequestSessionCreate effect"

  testCase "duplicate CreateSession is blocked when already creating" <| fun () ->
    let model = { (BozzettoModel.initial()) with CreatingSession = true }
    let _, effects =
      BozzettoUpdate.update
        (BozzettoMsg.Editor (EditorAction.CreateSession ["test.fsproj"]))
        model
    effects
    |> Expect.isEmpty "should emit no effects when already creating"

  testCase "SessionCreated event clears CreatingSession" <| fun () ->
    let model = { (BozzettoModel.initial()) with CreatingSession = true }
    let model', _ =
      BozzettoUpdate.update
        (BozzettoMsg.Event (TuiEvent.SessionCreated (mkSnap (testSessionId "aa001230"))))
        model
    model'.CreatingSession
    |> Expect.isFalse "should be false after SessionCreated"

  testCase "EvalFailed with 'Create failed' clears CreatingSession" <| fun () ->
    let model = { (BozzettoModel.initial()) with CreatingSession = true }
    let model', _ =
      BozzettoUpdate.update
        (BozzettoMsg.Event (TuiEvent.EvalFailed ("", "Create failed: something went wrong")))
        model
    model'.CreatingSession
    |> Expect.isFalse "should be false after create failure"

  testCase "EvalFailed without 'Create failed' keeps CreatingSession" <| fun () ->
    let model = { (BozzettoModel.initial()) with CreatingSession = true }
    let model', _ =
      BozzettoUpdate.update
        (BozzettoMsg.Event (TuiEvent.EvalFailed ("", "Some other error")))
        model
    model'.CreatingSession
    |> Expect.isTrue "should still be true for non-create errors"
]

let sessionsRenderTests = testList "Sessions panel creating indicator" [
  testCase "sessions region shows creating indicator when CreatingSession is true" <| fun () ->
    let model = { (BozzettoModel.initial()) with CreatingSession = true }
    let regions = BozzettoRender.render model
    let sessionsRegion = regions |> List.find (fun r -> r.Id = "sessions")
    sessionsRegion.Content
    |> Expect.stringContains "should contain creating text" "⏳ Creating session..."

  testCase "sessions region hides creating indicator when false" <| fun () ->
    let regions = BozzettoRender.render (BozzettoModel.initial())
    let sessionsRegion = regions |> List.find (fun r -> r.Id = "sessions")
    sessionsRegion.Content.Contains("⏳ Creating session...")
    |> Expect.isFalse "should not contain creating text"
]

let tests = testList "Session creation UX" [
  creatingSessionGuardTests
  sessionsRenderTests
]
