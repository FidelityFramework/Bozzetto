/// Assembly test discovery and execution for the retained testing adapter.
/// This agent does not rewrite methods, watch value reads or alter user code.
module Bozzetto.HostAgent

open System
open System.IO
open System.Reflection
open Bozzetto.Utils
open Bozzetto.Features.LiveTesting

[<RequireQualifiedAccess>]
type DiscoveryPolicy = WhenChanged | Forced

type AfterEval = { Discovery: DiscoveryPolicy }

type AfterEvalReport =
  { LiveTest: LiveTestHookResultDto
    AssemblyLoadErrors: AssemblyLoadError list }

type Discovery =
  { Tests: TestCase array
    Providers: ProviderDescription list }

type AgentInit =
  { Projects: string list
    ResolveFrom: string list }

type AgentStarted =
  { LoadedProjects: string list
    AssemblyLoadErrors: AssemblyLoadError list }

type AgentReply<'a> =
  | AgentAnswered of 'a
  | AgentUnavailable of reason: string

type CoverageReading =
  | NoCoverage
  | CoverageTaken of count: int * words: string

type AssemblySources =
  { Dynamic: unit -> Assembly[]
    Loaded: unit -> Assembly[] }

let currentProcess (dynamic: unit -> Assembly[]) : AssemblySources =
  { Dynamic = dynamic
    Loaded = fun () -> AppDomain.CurrentDomain.GetAssemblies() }

let testFrameworkMarkers =
  [| "Expecto"; "xunit.core"; "xunit.v3.core"; "nunit.framework"
     "Microsoft.VisualStudio.TestPlatform.TestFramework"; "TUnit.Core" |]

type private Runner = TestCase -> Async<TestResult>

let private firstAnswer (runners: Runner list) : Runner =
  fun test ->
    let rec loop remaining = async {
      match remaining with
      | [] -> return TestResult.NotRun
      | run :: rest ->
        match! run test with
        | TestResult.NotRun -> return! loop rest
        | answered -> return answered
    }
    loop runners

type State =
  { ProjectAssemblies: Assembly list
    AssemblyLoadErrors: AssemblyLoadError list
    LastAssembly: Assembly option
    InitialDiscoveryComplete: bool }

let startState (init: AgentInit) : State =
  let results = init.Projects |> List.map AssemblyLoadError.loadAssembly
  let errors = results |> List.choose (function Ok _ -> None | Error error -> Some error)
  errors |> List.iter (AssemblyLoadError.describe >> Log.logWarn)
  { ProjectAssemblies = results |> List.choose (function Ok assembly -> Some assembly | Error _ -> None)
    AssemblyLoadErrors = errors
    LastAssembly = None
    InitialDiscoveryComplete = false }

type StepResult = { State: State; Hook: LiveTestHookResult }

let private mergeHooks (results: LiveTestHookResult list) : LiveTestHookResult =
  { LiveTestHookResult.empty with
      DetectedProviders =
        results |> List.collect _.DetectedProviders
        |> List.distinctBy (function ProviderDescription.AttributeBased p -> p.Name | ProviderDescription.Custom p -> p.Name)
      DiscoveredTests = results |> List.map _.DiscoveredTests |> Array.concat |> Array.distinctBy _.Id
      AffectedTestIds = results |> List.map _.AffectedTestIds |> Array.concat |> Array.distinct
      RunTest = results |> List.map _.RunTest |> firstAnswer }

/// A newly emitted assembly is a new discovery input. No method identity or
/// runtime patch is used as a dependency or execution-authority claim.
let afterEvalStep (executors: TestExecutor list) (state: State) (assembly: Assembly) (request: AfterEval) : StepResult =
  let changed = state.LastAssembly |> Option.forall (fun previous -> not (obj.ReferenceEquals(previous, assembly)))
  let initial = not state.InitialDiscoveryComplete
  if not changed && not initial && request.Discovery <> DiscoveryPolicy.Forced then
    { State = state; Hook = LiveTestHookResult.empty }
  else
    let discover assembly =
      let found = LiveTestingHook.afterReload executors assembly []
      { found with AffectedTestIds = found.DiscoveredTests |> Array.map _.Id }
    let results =
      discover assembly ::
        (if initial then state.ProjectAssemblies |> List.map discover else [])
    { State = { state with LastAssembly = Some assembly; InitialDiscoveryComplete = true }
      Hook = mergeHooks results }

[<Sealed>]
type Agent(init: AgentInit, sources: AssemblySources, executors: TestExecutor list) =
  let gate = obj ()
  // Resolve only an exact declared assembly identity. Do not replace versions
  // or bypass the loader through raw bytes when an identity cannot be loaded.
  let searchPaths =
    init.Projects @ init.ResolveFrom
    |> List.map Path.GetDirectoryName
    |> List.filter (String.IsNullOrWhiteSpace >> not)
    |> List.distinct
  let resolver = ResolveEventHandler(fun _ args ->
    let requested = AssemblyName args.Name
    searchPaths
    |> List.tryPick (fun directory ->
      let candidate = Path.Combine(directory, requested.Name + ".dll")
      if not (File.Exists candidate) then None
      else
        try
          let identity = AssemblyName.GetAssemblyName candidate
          if identity.FullName = requested.FullName then Some(Assembly.LoadFrom candidate) else None
        with error ->
          Log.warn "[HostAgent] cannot load declared dependency %s: %s" candidate error.Message
          None)
    |> Option.toObj)
  do AppDomain.CurrentDomain.add_AssemblyResolve resolver
  let mutable state = startState init
  let started =
    { LoadedProjects = state.ProjectAssemblies |> List.map _.Location
      AssemblyLoadErrors = state.AssemblyLoadErrors }
  let mutable dynamicRunner : Runner option = None
  let mutable projectRunner : Runner option = None

  new(init, sources) = new Agent(init, sources, BuiltInExecutors.builtIn)

  member _.Started = started

  member _.AfterEval(request: AfterEval) : AfterEvalReport =
    lock gate (fun () ->
      match sources.Dynamic() |> Array.tryLast with
      | None ->
        { LiveTest = LiveTestHookResultDto.fromResult LiveTestHookResult.empty
          AssemblyLoadErrors = state.AssemblyLoadErrors }
      | Some assembly ->
        let step = afterEvalStep executors state assembly request
        state <- step.State
        if step.Hook.DiscoveredTests.Length > 0 || not step.Hook.DetectedProviders.IsEmpty then
          dynamicRunner <- Some step.Hook.RunTest
        { LiveTest = LiveTestHookResultDto.fromResult step.Hook
          AssemblyLoadErrors = state.AssemblyLoadErrors })

  interface IDisposable with
    member _.Dispose() = AppDomain.CurrentDomain.remove_AssemblyResolve resolver
  /// Take the coverage the instrumented assemblies recorded, and reset it for the next run. Coverage lives in the process
  /// that ran the tests, so only its agent can read it.
  member _.TakeCoverage() : CoverageReading =
    let instrumentable =
      sources.Loaded()
      |> Array.filter (fun a -> try not a.IsDynamic && not (isNull a.Location) && a.Location <> "" with _ -> false)
    match CoverageProbes.discoverAndCollectHits instrumentable with
    | None -> NoCoverage
    | Some hits ->
      let bitmap = CoverageBitmap.ofBoolArray hits
      CoverageProbes.discoverAndResetHits instrumentable
      CoverageTaken(bitmap.Count, CoverageBitmap.toBase64 bitmap)

  /// The simple names of every assembly the process has loaded, sorted and distinct: what a warmup check asks, since only
  /// the process the user's code runs in knows.
  member _.LoadedAssemblyNames() : string list =
    sources.Loaded()
    |> Array.choose (fun a -> try Some(a.GetName().Name) with _ -> None)
    |> Array.toList
    |> List.distinct
    |> List.sort

  /// Scan the assemblies the process has loaded (the project's own, referencing a test framework) for tests, and keep
  /// the runner for them.
  member _.DiscoverLoaded() : Discovery =
    lock gate (fun () ->
      let referencesFramework (a: Assembly) =
        try a.GetReferencedAssemblies() |> Array.exists (fun r -> testFrameworkMarkers |> Array.contains r.Name)
        with ex ->
          Log.warn "[HostAgent] framework check failed for %s: %s" a.FullName ex.Message
          false
      let results =
        sources.Loaded()
        |> Array.filter referencesFramework
        |> Array.choose (fun asm ->
          try
            let hook = LiveTestingHook.afterReload executors asm []
            match hook.DiscoveredTests.Length > 0 with
            | true -> Some hook
            | false -> None
          with ex ->
            Log.error "[HostAgent] discovery failed for %s: %s" asm.FullName ex.Message
            None)
      projectRunner <-
        (match results with
         | [||] -> None
         | found -> Some(found |> Array.toList |> List.map (fun r -> r.RunTest) |> firstAnswer))
      { Tests = results |> Array.collect (fun r -> r.DiscoveredTests) |> Array.distinctBy (fun t -> t.Id)
        Providers =
          results
          |> Array.collect (fun r -> List.toArray r.DetectedProviders)
          |> Array.distinctBy (function
            | ProviderDescription.AttributeBased d -> d.Name
            | ProviderDescription.Custom d -> d.Name)
          |> Array.toList })

  /// Run one test: interactively defined tests first, then the project's.
  member _.RunTest(test: TestCase) : Async<TestResult> =
    let runners =
      lock gate (fun () -> [ yield! Option.toList dynamicRunner; yield! Option.toList projectRunner ])
    firstAnswer runners test
