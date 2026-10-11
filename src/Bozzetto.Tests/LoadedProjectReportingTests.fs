module Bozzetto.Tests.LoadedProjectReportingTests

// Retained regression coverage: injected project metadata and FCS fixture
// options must both contribute to project roles and targets.

open System
open System.IO
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.ProjectLoading
open FSharp.Compiler.CodeAnalysis

let private quietLogger =
  { new Utils.ILogger with
      member _.LogInfo _ = ()
      member _.LogDebug _ = ()
      member _.LogWarning _ = ()
      member _.LogError _ = () }

/// An injected ProjectMetadata fixture. Mirrors
/// ActorCreationTests.mkProject — kept local so this file has no
/// cross-file test dependency.
let private mkProject (fileName: string) (outputType: string option) (isTestProject: bool) : ProjectMetadata =
  let allProps =
    [ match outputType with
      | Some v -> yield "OutputType", Set.singleton v
      | None -> ()
      yield "IsTestProject", Set.singleton (string isTestProject) ]
    |> Map.ofList
  { ProjectFileName = fileName
    TargetFramework = "net10.0"
    OtherOptions = []
    ReferencedProjects = []
    PackageReferences = []
    TargetPath = fileName + ".dll"
    AllProperties = allProps }

let private tempDir () =
  let dir = Path.Combine(Path.GetTempPath(), "bozzetto-loaded-project-reporting-" + Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory dir |> ignore
  dir

/// Writes a minimal buildable-looking fsproj + one source file, then parses
/// it the same way the manual-parse fallback does, producing a real
/// FSharpProjectOptions with no injected ProjectMetadata counterpart — exactly
/// the shape `sln.FsProjects` has on the fallback path.
let private manualParseFallback (dir: string) (fsprojXml: string) : FSharpProjectOptions =
  let projPath = Path.Combine(dir, "App.fsproj")
  File.WriteAllText(Path.Combine(dir, "Program.fs"), "module Program\nlet x = 1\n")
  File.WriteAllText(projPath, fsprojXml)
  match ManualProjectParse.parseFsproj quietLogger projPath with
  | [ options ] -> options
  | other -> failwithf "expected exactly one FSharpProjectOptions, got %d" other.Length

let private exeFsproj =
  "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
  "  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
  "  <ItemGroup><Compile Include=\"Program.fs\" /></ItemGroup>\n" +
  "</Project>\n"

let private libFsproj =
  "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
  "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
  "  <ItemGroup><Compile Include=\"Program.fs\" /></ItemGroup>\n" +
  "</Project>\n"

let private testFsproj =
  "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
  "  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>\n" +
  "  <ItemGroup>\n" +
  "    <PackageReference Include=\"Expecto\" Version=\"11.0.0-alpha8\" />\n" +
  "    <Compile Include=\"Program.fs\" />\n" +
  "  </ItemGroup>\n" +
  "</Project>\n"

[<Tests>]
let tests =
  testList "LoadedProjectReporting" [

    testCase "test-package identity survives an unrelated assembly filename" (fun () ->
      let project =
        { mkProject "/repo/App/App.fsproj" None false with
            AllProperties = Map.empty
            PackageReferences = [ "Expecto", "/assemblies/unrelated.dll" ] }
      isTestProject project |> Expect.isTrue "test discovery must use the supplied package identity"
      let classified = classifyProject project
      classified.Role |> Expect.equal "a test package classifies the project as Test" ProjectRole.Test
      classified.PackageRefs |> Expect.contains "reported package facts keep the supplied identity" "Expecto")

    testCase "a test-like assembly filename does not invent a test-package identity" (fun () ->
      let project =
        { mkProject "/repo/App/App.fsproj" None false with
            AllProperties = Map.empty
            PackageReferences = [ "Ordinary.Package", "/assemblies/Expecto.dll" ] }
      isTestProject project |> Expect.isFalse "test-package identity must not be inferred from an assembly filename"
      let classified = classifyProject project
      classified.Role |> Expect.equal "ordinary package metadata remains Library" ProjectRole.Library
      classified.PackageRefs |> Expect.equal "reported facts must not invent an Expecto package" [ "Ordinary.Package" ])

    testCase "package identity survives an unrelated assembly filename" (fun () ->
      let project =
        { mkProject "/repo/App/App.fsproj" None false with
            PackageReferences = [ "Aspire.Hosting", "/assemblies/unrelated.dll" ] }
      let solution = { emptySolution with Projects = [ project ] }
      AspireSetup.hasAspireReferences solution
      |> Expect.isTrue "Aspire detection must use the supplied package identity"
      (SessionAgent.agentInitOf solution).ResolveFrom
      |> Expect.equal "assembly resolution must use the separate supplied path"
           [ "/assemblies/unrelated.dll" ])

    testCase "an Aspire-like assembly filename does not invent a package identity" (fun () ->
      let project =
        { mkProject "/repo/App/App.fsproj" None false with
            PackageReferences = [ "Ordinary.Package", "/assemblies/Aspire.Hosting.dll" ] }
      AspireSetup.hasAspireReferences { emptySolution with Projects = [ project ] }
      |> Expect.isFalse "package identity must not be inferred from an assembly filename")

    testCase "empty solution classifies to nothing" (fun () ->
      classifiedProjectsOf emptySolution |> Expect.isEmpty "no projects at all")

    testCase "injected metadata is preserved by classifiedProjectsOf" (fun () ->
      let exeProj = mkProject "/repo/App/App.fsproj" (Some "Exe") false
      let libProj = mkProject "/repo/Lib/Lib.fsproj" None false
      let sln = { emptySolution with Projects = [ exeProj; libProj ] }
      let viaUnified = classifiedProjectsOf sln
      let viaDirect = classifyProjects sln.Projects
      viaUnified |> Expect.equal "classifiedProjectsOf must not change the normal-path result" viaDirect
      viaUnified |> List.map (fun cp -> cp.Role)
      |> Expect.equal "roles read straight from AllProperties" [ ProjectRole.Executable; ProjectRole.Library ])

    testCase "fallback-only solution (Projects empty, FsProjects populated) is classified — the regression" (fun () ->
      let dir = tempDir ()
      try
        let fp = manualParseFallback dir exeFsproj
        let sln = { emptySolution with Projects = []; FsProjects = [ fp ] }
        let result = classifiedProjectsOf sln
        result |> Expect.hasLength "the fallback project must be reported, not dropped" 1
        result.Head.Path |> Expect.equal "path comes from the FSharpProjectOptions" fp.ProjectFileName
        result.Head.Role |> Expect.equal "OutputType=Exe in the fsproj XML" ProjectRole.Executable
      finally
        Directory.Delete(dir, true))

    testCase "fallback project with no OutputType is classified Library, never guessed Executable" (fun () ->
      let dir = tempDir ()
      try
        let fp = manualParseFallback dir libFsproj
        let sln = { emptySolution with Projects = []; FsProjects = [ fp ] }
        let result = classifiedProjectsOf sln
        result |> Expect.hasLength "one fallback project" 1
        result.Head.Role |> Expect.equal "unknown OutputType must fall back to Library, not a guessed Run button" ProjectRole.Library
      finally
        Directory.Delete(dir, true))

    testCase "fallback project with IsTestProject/Expecto is classified Test" (fun () ->
      let dir = tempDir ()
      try
        let fp = manualParseFallback dir testFsproj
        let sln = { emptySolution with Projects = []; FsProjects = [ fp ] }
        let result = classifiedProjectsOf sln
        result.Head.Role |> Expect.equal "IsTestProject + Expecto package" ProjectRole.Test
        result.Head.PackageRefs |> Expect.contains "package refs come from raw fsproj PackageReference Include" "Expecto"
      finally
        Directory.Delete(dir, true))

    testCase "both populated: no duplicates, normal data wins for the shared path" (fun () ->
      let dir = tempDir ()
      try
        let fp = manualParseFallback dir exeFsproj
        // Same path as the fallback entry, but via the (richer) normal path,
        // classified Library so the test can tell which source won.
        let normalProj = mkProject fp.ProjectFileName None false
        let otherFallback = manualParseFallback (tempDir ()) libFsproj
        let sln = { emptySolution with Projects = [ normalProj ]; FsProjects = [ fp; otherFallback ] }
        let result = classifiedProjectsOf sln
        result |> Expect.hasLength "one deduped shared-path entry + one fallback-only entry" 2
        let shared = result |> List.find (fun cp -> cp.Path = fp.ProjectFileName)
        shared.Role |> Expect.equal "the normal-path classification must win for a path present in both" ProjectRole.Library
      finally
        Directory.Delete(dir, true))

    testCase "normal path projectTargetsOf is unchanged" (fun () ->
      let exeProj = mkProject "/repo/App/App.fsproj" (Some "Exe") false
      let sln = { emptySolution with Projects = [ exeProj ] }
      projectTargetsOf sln
      |> Expect.equal "unchanged: (ProjectFileName, TargetPath) straight from Projects" [ exeProj.ProjectFileName, exeProj.TargetPath ]
      )

    testCase "fallback projectTargetsOf finds the built assembly by expected file name" (fun () ->
      let dir = tempDir ()
      try
        let fp = manualParseFallback dir exeFsproj
        let expectedDll = Path.Combine(dir, "bin", "Debug", "net10.0", "App.dll")
        Directory.CreateDirectory(Path.GetDirectoryName expectedDll) |> ignore
        File.WriteAllBytes(expectedDll, [| 0uy |])
        let sln = { emptySolution with Projects = []; FsProjects = [ fp ]; References = [ expectedDll ] }
        projectTargetsOf sln
        |> Expect.equal "matched by App.dll among the collected reference DLLs" [ fp.ProjectFileName, expectedDll ]
      finally
        Directory.Delete(dir, true))

    testCase "fallback projectTargetsOf omits a project with no matching built assembly, rather than guessing" (fun () ->
      let dir = tempDir ()
      try
        let fp = manualParseFallback dir exeFsproj
        let sln = { emptySolution with Projects = []; FsProjects = [ fp ]; References = [ "/some/other/Unrelated.dll" ] }
        projectTargetsOf sln |> Expect.isEmpty "no known target — omit, never guess a wrong one"
      finally
        Directory.Delete(dir, true))
  ]
