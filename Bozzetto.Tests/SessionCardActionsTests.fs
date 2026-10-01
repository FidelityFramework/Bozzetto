module Bozzetto.Tests.SessionCardActionsTests

open Expecto
open Expecto.Flip
open Falco.Markup
open Bozzetto
open Bozzetto.Server.DashboardTypes
open Bozzetto.Server.DashboardFragments

let private mkCardSession (id: string) (projects: (string * ProjectLoading.ProjectRole) list) : ParsedSession =
  let sid = WorkerProtocol.SessionId.validate id |> Result.defaultValue (WorkerProtocol.SessionId.newId ())
  { Id = sid; Status = SessionDisplayStatus.Running; StatusMessage = None
    ProjectsText = "(A.fsproj)"; EvalCount = 1
    Uptime = "1m"; WorkingDir = "/a"; LastActivity = "A"
    TestSummary = None; CoverageSummary = None; TestTreemapEntries = [||]; CoverageTreemap = None
    BindingEntries = [||]; AgentBadges = []; GuidanceCssClass = ""
    ActiveProject = None
    ProjectRoles =
      projects
      |> List.map (fun (path, role) -> { ProjectLoading.ClassifiedProject.Path = path; Role = role; PackageRefs = [] })
    App = AppRun.AppRunState.NotRunning
    WorkerRssBytes = None; SelfHostStaleness = None; Health = SessionHealth.Healthy }

let private render viewing sessions =
  renderSessionsForSession viewing sessions false |> renderNode

[<Tests>]
let tests = testList "Session card actions" [
  test "WHY — session switching — cards and the switch button POST to /dashboard/session/switch because GET /dashboard ignores ?session= and every card reloaded onto the first session" {
    let html = render "0a2b3c4e" [ mkCardSession "0a2b3c4d" []; mkCardSession "0a2b3c4e" [] ]
    html.Contains "?session=" |> Expect.isFalse "no URL-driven navigation remains"
    html |> Expect.stringContains "a non-viewed card switches via the signal-synced POST" "@post('/dashboard/session/switch/0a2b3c4d')"
  }

  test "WHY — session switching — the viewed card has no switch handler because clicking the session you are already viewing must do nothing" {
    let html = render "0a2b3c4e" [ mkCardSession "0a2b3c4e" [] ]
    html.Contains "/dashboard/session/switch/0a2b3c4e" |> Expect.isFalse "the viewed card must not switch to itself"
  }

  test "WHY — session switching — the card ignores clicks that land on its own buttons and links because Stop/Run clicks bubbled into a switch" {
    let html = render "0a2b3c4e" [ mkCardSession "0a2b3c4d" [] ]
    html |> Expect.stringContains "card click guard" "evt.target.closest('button, a') ||"
  }
]
