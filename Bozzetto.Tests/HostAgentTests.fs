module Bozzetto.Tests.HostAgentTests

open System
open System.IO
open System.Reflection
open Expecto
open Expecto.Flip
open Bozzetto.Features.LiveTesting
open Bozzetto.HostAgent

let private noAssemblies () : System.Reflection.Assembly[] = [||]

let private nothingLoaded : AssemblySources = { Dynamic = noAssemblies; Loaded = noAssemblies }

let private emptyInit : AgentInit = { Projects = []; ResolveFrom = [] }

let private request (discovery: DiscoveryPolicy) : AfterEval =
  { Discovery = discovery }

let private missingProject =
  Path.Combine(Path.GetTempPath(), "bozzetto-host-agent-no-such-dir", "Missing.dll")

let private unknownTest : TestCase =
  { Id = TestId.create "no.such.test" TestFramework.Expecto
    FullName = "no.such.test"
    DisplayName = "no.such.test"
    Origin = TestOrigin.ReflectionOnly
    Labels = []
    Framework = TestFramework.Expecto
    Category = TestCategory.Unit }

[<Tests>]
let tests =
  testList "HostAgent" [
    testList "start" [
      testCase "a project that is not on disk is a typed load error, never an exception" <| fun _ ->
        use agent = new Agent({ Projects = [ missingProject ]; ResolveFrom = [] }, nothingLoaded)
        match (agent.Started).AssemblyLoadErrors with
        | [ AssemblyLoadError.FileNotFound(path, _) ] -> Expect.equal "names the file" missingProject path
        | other -> failtestf "expected one FileNotFound, got %A" other

      testCase "a session with no projects starts clean" <| fun _ ->
        use agent = new Agent(emptyInit, nothingLoaded)
        Expect.isEmpty "no errors" agent.Started.AssemblyLoadErrors

    ]

    testList "AfterEval" [
      testCase "with nothing emitted yet discovery has nothing to report" <| fun _ ->
        use agent = new Agent(emptyInit, nothingLoaded)
        for discovery in [ DiscoveryPolicy.WhenChanged; DiscoveryPolicy.Forced ] do
          let report = agent.AfterEval(request discovery)
          Expect.isEmpty "no tests" report.LiveTest.DiscoveredTests
          Expect.isEmpty "no load errors" report.AssemblyLoadErrors

      testProperty "discovery reports are repeatable when nothing is emitted"
      <| fun forced ->
        use agent = new Agent(emptyInit, nothingLoaded)
        let policy = if forced then DiscoveryPolicy.Forced else DiscoveryPolicy.WhenChanged
        agent.AfterEval(request policy) = agent.AfterEval(request policy)
    ]

    testList "RunTest" [
      testAsync "a test the agent never discovered is NotRun, not an error" {
        use agent = new Agent(emptyInit, nothingLoaded)
        let! result = agent.RunTest unknownTest
        Expect.equal "not run" TestResult.NotRun result
      }
    ]

    testList "TakeCoverage" [
      testCase "a process with nothing instrumented has no coverage to take" <| fun _ ->
        use agent = new Agent(emptyInit, nothingLoaded)
        Expect.equal "none" NoCoverage (agent.TakeCoverage())

      testCase "assemblies without the coverage tracker report none, and taking twice changes nothing" <| fun _ ->
        use agent = new Agent(emptyInit, { nothingLoaded with Loaded = fun () -> [| typeof<string>.Assembly |] })
        Expect.equal "none" NoCoverage (agent.TakeCoverage())
        Expect.equal "still none" NoCoverage (agent.TakeCoverage())
    ]

    testList "LoadedAssemblyNames" [
      testCase "names exactly the assemblies the process has loaded, sorted and without duplicates" <| fun _ ->
        let assemblies = [| typeof<string>.Assembly; typeof<Expecto.TestCode>.Assembly; typeof<string>.Assembly |]
        use agent = new Agent(emptyInit, { nothingLoaded with Loaded = fun () -> assemblies })
        let names = agent.LoadedAssemblyNames()
        Expect.contains "Expecto is named" "Expecto" names
        Expect.equal "sorted" (List.sort names) names
        Expect.equal "distinct" (List.distinct names) names

      testCase "a process that has loaded nothing names nothing" <| fun _ ->
        use agent = new Agent(emptyInit, nothingLoaded)
        Expect.isEmpty "none" (agent.LoadedAssemblyNames())
    ]

    testList "DiscoverLoaded" [
      testCase "a process with no test framework loaded discovers nothing" <| fun _ ->
        use agent = new Agent(emptyInit, nothingLoaded)
        let discovery = agent.DiscoverLoaded()
        Expect.isEmpty "no tests" discovery.Tests
        Expect.isEmpty "no providers" discovery.Providers
    ]
  ]
