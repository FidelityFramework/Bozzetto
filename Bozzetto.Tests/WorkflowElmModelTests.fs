module Bozzetto.Tests.WorkflowElmModelTests

open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.WorkflowTypes

// ─── Helpers ────────────────────────────────────────────────

let private defaultModel = BozzettoModel.initial ()

/// WHY: Before any session exists, the default workflow applies.
/// The projection function must handle the None case gracefully —
/// every UI component that reads the workflow must get a sensible default.
let currentWorkflowDefaultsToInteractive =
  testCase
    "currentWorkflow defaults to Interactive when no session context — safe fallback for UI" <| fun _ ->
    let model = { defaultModel with SessionContext = None }

    BozzettoModel.currentWorkflow model
    |> Expect.equal "should default to Interactive when no session exists"
        SessionWorkflow.Interactive

/// WHY: When a session IS active, the projection reads from the actual session config.
/// This proves the projection doesn't always return the default — it responds to real state.
let currentWorkflowReflectsSessionContext =
  testCase
    "currentWorkflow reflects actual session workflow when session context exists" <| fun _ ->
    let ctx : SessionContext = {
      SessionId = "test-session"
      ProjectNames = ["TestProj"]
      WorkingDir = "/code"
      Status = "Ready"
      Warmup = WarmupContext.empty
      FileStatuses = []
      Workflow = SessionWorkflow.LiveTesting
      AutoOpenNamespaces = true
    }
    let model = { defaultModel with SessionContext = Some ctx }

    BozzettoModel.currentWorkflow model
    |> Expect.equal "should reflect LiveTesting from session context"
        (SessionWorkflow.LiveTesting)

// ─── Test list ──────────────────────────────────────────────

[<Tests>]
let tests = testList "Workflow Elm Model" [
  testList "Projection — currentWorkflow" [
    currentWorkflowDefaultsToInteractive
    currentWorkflowReflectsSessionContext
  ]
]
