/// Access to assembly test discovery and coverage in the active session.
module Bozzetto.SessionAgent

open Bozzetto.Features
open Bozzetto.ProjectLoading

let agentInitOf (solution: Solution) : HostAgent.AgentInit =
  let referenced options =
    options
    |> List.filter (fun (value: string) -> value.StartsWith("-r:", System.StringComparison.Ordinal) && value.EndsWith(".dll", System.StringComparison.Ordinal))
    |> List.map (fun value -> value.Substring 3)
  { Projects = solution.Projects |> List.map _.TargetPath
    ResolveFrom =
      solution.Projects
      |> List.collect (fun project ->
        (project.PackageReferences |> List.map _.FullPath) @ referenced project.OtherOptions) }

type SessionAgent =
  { DiscoverLoaded: unit -> HostAgent.AgentReply<HostAgent.Discovery>
    TakeCoverage: unit -> HostAgent.AgentReply<HostAgent.CoverageReading>
    RunTest: LiveTesting.TestCase -> Async<HostAgent.AgentReply<LiveTesting.TestResult>> }

let ofCurrentSession (current: unit -> FsiSession.IFsiSession) : SessionAgent =
  let inactive = HostAgent.AgentUnavailable "the session is not active"
  let withSession ask whenInactive =
    match current () with
    | null -> whenInactive
    | session -> ask session
  { DiscoverLoaded = fun () -> withSession (fun session -> session.DiscoverLoaded()) inactive
    TakeCoverage = fun () -> withSession (fun session -> session.TakeCoverage()) inactive
    RunTest = fun test -> withSession (fun session -> session.RunTest test) (async { return inactive }) }
