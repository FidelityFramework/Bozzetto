module Bozzetto.ProjectLoading

open System
open System.IO
open System.Xml.Linq

open FSharp.Compiler.CodeAnalysis
open Bozzetto.Utils

type FileName = string
type DllName = string
type DirName = string

/// Project role classification for session management.
/// Determines which projects are suitable for hot-reloading vs. testing.
type ProjectRole =
  | Executable    // Has OutputType = Exe and is meant to be run as a web app
  | Library       // Shared libraries, static assemblies
  | Test          // Test projects (contained in test packages or marked with IsTestProject)

/// A loaded project with its role. The entry point of an executable is not
/// stored here: the compiled assembly's Assembly.EntryPoint is authoritative.
and ClassifiedProject = {
  Path: string
  Role: ProjectRole
  PackageRefs: string list
}

/// Retained manual .fsproj parsing utility for component-test fixtures.
/// Named F# session construction is refused before this utility can run;
/// it does not replace the retired project evaluator.
module ManualProjectParse =

  let private xname (local: string) = XName.Get(local)

  let rec private collectSourceFiles (projPath: string) (visited: Set<string>) (acc: FileName list) =
    let full = Path.GetFullPath projPath
    match visited.Contains full with
    | true -> acc, visited
    | false ->
      let visited' = visited.Add full
      match File.Exists full with
      | false -> acc, visited'
      | true ->
        try
          let doc = XDocument.Load full
          let dir = Path.GetDirectoryName full
          let ns = doc.Root.Attribute(xname "xmlns") |> Option.ofObj |> Option.map (fun a -> XNamespace.Get a.Value) |> Option.defaultValue (XNamespace.None)
          let compileIncludes =
            doc.Descendants(ns + "Compile")
            |> Seq.choose (fun el -> el.Attribute(xname "Include") |> Option.ofObj |> Option.map (fun a -> a.Value))
            |> Seq.map (fun inc -> Path.GetFullPath(Path.Combine(dir, inc.Replace('\\', Path.DirectorySeparatorChar))))
            |> Seq.filter (fun p -> p.EndsWith(".fs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase))
            |> Seq.filter File.Exists
            |> Seq.toList
          // Recurse into ProjectReferences
          let refs =
            doc.Descendants(ns + "ProjectReference")
            |> Seq.choose (fun el -> el.Attribute(xname "Include") |> Option.ofObj |> Option.map (fun a -> a.Value))
            |> Seq.map (fun inc -> Path.GetFullPath(Path.Combine(dir, inc.Replace('\\', Path.DirectorySeparatorChar))))
            |> Seq.toList
          let recAcc, recVisited =
            refs |> List.fold (fun (a, v) r -> collectSourceFiles r v a) (acc, visited')
          recAcc @ compileIncludes, recVisited
        with _ ->
          acc, visited'

  /// Arcade-style repos (fsharp/fsharp-compiler-services, dotnet/runtime,
  /// dotnet/sdk, and other .NET-eng-templated repos) route ALL build output
  /// through `<repoRoot>/artifacts/bin/<ProjectName>/<Config>/<Tfm>/` and
  /// never create a `<projectDir>/bin/` at all — confirmed on
  /// fsharp-compiler-services: `src/Compiler/` has no `bin/` anywhere under
  /// it, real output lives at `artifacts/bin/FSharp.Compiler.Service/`. The
  /// conventional-layout probe below finds nothing there and reports "not
  /// built yet" on a project that IS built. Walk up from the project's own
  /// directory looking for an ancestor `artifacts/bin/<ProjectName>/` —
  /// bounded (8 levels) so a project with no Arcade layout anywhere in its
  /// ancestry (the common case) doesn't walk to the filesystem root.
  let arcadeBinDir (projDir: string) (projectFileName: string) : string option =
    let projectName = Path.GetFileNameWithoutExtension projectFileName
    let rec walk (dir: string) (depth: int) =
      match depth > 8 with
      | true -> None
      | false ->
        let candidate = Path.Combine(dir, "artifacts", "bin", projectName)
        match Directory.Exists candidate with
        | true -> Some candidate
        | false ->
          match Path.GetDirectoryName dir with
          | null
          | "" -> None
          | parent when parent = dir -> None
          | parent -> walk parent (depth + 1)
    walk projDir 0

  /// The directory the manual fallback should collect build output from for
  /// `projPath`: the conventional `<projectDir>/bin` when it exists, or the
  /// Arcade-style `artifacts/bin/<ProjectName>` found in an ancestor
  /// directory otherwise. `None` when neither layout has been built yet.
  let outputBinDir (projPath: string) : string option =
    let projDir = Path.GetDirectoryName (Path.GetFullPath projPath)
    let conventional = Path.Combine(projDir, "bin")
    match Directory.Exists conventional with
    | true -> Some conventional
    | false -> arcadeBinDir projDir projPath

  /// Collect the built assembly and its dependencies for a project that was
  /// already compiled (bin/<config>/<tfm>/, or the Arcade-style
  /// artifacts/bin/<ProjectName>/<config>/<tfm>/ layout — see `outputBinDir`).
  /// Used by the manual fallback so FSI still gets project + NuGet references
  /// even when MSBuild evaluation fails.
  let collectBinReferences (logger: ILogger) (projPaths: string list) : DllName list =
    projPaths
    |> List.collect (fun projPath ->
      match outputBinDir projPath with
      | None ->
        logger.LogWarning (sprintf "  No bin dir for %s — project may not be built yet" (Path.GetFileName projPath))
        []
      | Some binDir ->
        // Layout varies: some builds put DLLs in bin/<cfg>/ directly, others in
        // bin/<cfg>/<tfm>/. Collect from ONE config dir only (newest by write
        // time) — mixing Debug + Release DLLs produces duplicate assembly
        // versions that FSI rejects with 0x80131040.
        let cfgDirs = Directory.EnumerateDirectories binDir |> Seq.sortByDescending (fun d -> Directory.GetLastWriteTimeUtc d) |> Seq.toList
        match cfgDirs with
        | [] -> []
        | cfgDir :: _ ->
          let cfgRootDlls =
            Directory.EnumerateFiles(cfgDir, "*.dll", SearchOption.TopDirectoryOnly)
          let tfmSubDlls =
            Directory.EnumerateDirectories cfgDir
            |> Seq.collect (fun tfmDir ->
              Directory.EnumerateFiles(tfmDir, "*.dll", SearchOption.TopDirectoryOnly))
          Seq.append cfgRootDlls tfmSubDlls
          // Exclude satellite/resource assemblies (they live in culture subdirs
          // and would collide in the shadow dir) and native/non-managed DLLs that
          // FSI can't load via -r: (e.g. aspnetcorev2_inprocess.dll).
          |> Seq.filter (fun dll ->
            let name = Path.GetFileName dll
            not (name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            && not (name.Contains("aspnetcorev2", StringComparison.OrdinalIgnoreCase))
            && not (name.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase))
            && not (name.StartsWith("lib", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
          // Same-named DLLs can appear in MULTIPLE TFM subdirs (a project's bin
          // may hold orphans from old target layouts, e.g. a net10 copy of
          // Bozzetto.Core.dll left behind after the project targeted a different
          // TFM (an unrelated preview target, or a net9 orphan)). Passing
          // both to FSI lets the stale one shadow the fresh build, so the REPL
          // compiles against ancient metadata. Dedupe by file name, keeping the
          // NEWEST copy — an orphan can never shadow a fresh build.
          |> Seq.groupBy (fun dll -> Path.GetFileName dll)
          |> Seq.map (fun (_, group) ->
            group |> Seq.maxBy (fun dll -> File.GetLastWriteTimeUtc dll))
          |> Seq.toList
          |> fun dlls ->
          // ASP.NET Core framework DLLs (Microsoft.AspNetCore.*, etc.) are NOT in
          // bin/ — they come from the shared framework. MSBuild's FrameworkReference
          // normally adds them; the manual fallback must add them explicitly or FSI
          // fails with "type ... is defined in an assembly that is not referenced".
          let dotnetRoot =
            Environment.GetEnvironmentVariable("DOTNET_ROOT")
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
              // typeof<obj>.Assembly.Location = .../shared/Microsoft.NETCore.App/<ver>/System.Private.CoreLib.dll
              // ../../../ = dotnet root
              let runtimeDir = Path.GetDirectoryName(typeof<obj>.Assembly.Location)
              Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", "..")))
          // *.dll of the shared framework version that matches the runtime THIS process
          // is running on (see RuntimeCompat.selectFrameworkDir), or [] when that
          // framework is not installed. A FrameworkReference normally adds these; the
          // manual fallback must add them or FSI fails with "type ... is defined in an
          // assembly that is not referenced". Taking the "newest" directory instead
          // handed a net10 worker another runtime's reference assemblies on a box with both.
          let sharedFrameworkDlls (frameworkName: string) =
            let dir = Path.Combine(dotnetRoot, "shared", frameworkName)
            match Directory.Exists dir with
            | false -> []
            | true ->
              let versionDirs = Directory.EnumerateDirectories dir |> Seq.toList
              match RuntimeCompat.selectFrameworkDir Environment.Version (versionDirs |> List.map Path.GetFileName) with
              | Error _ -> []
              | Ok chosen ->
                Directory.EnumerateFiles(Path.Combine(dir, chosen), "*.dll", SearchOption.TopDirectoryOnly) |> Seq.toList
          let aspNetShared = sharedFrameworkDlls "Microsoft.AspNetCore.App"
          // WPF/WinForms assemblies come from the Microsoft.WindowsDesktop.App shared
          // framework (Windows only), exactly the way ASP.NET Core does. The directory
          // is absent on Linux/macOS, so this is a no-op there and present only on a
          // Windows box with the Desktop runtime installed.
          let windowsDesktopShared = sharedFrameworkDlls "Microsoft.WindowsDesktop.App"
          // Apply the same safety filter to BOTH lists (native DLLs like
          // aspnetcorev2_inprocess.dll exist in the shared framework and must
          // never be passed to FSI as -r: references).
          let isManagedRef (dll: string) =
            let name = Path.GetFileName dll
            not (name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            && not (name.Contains("aspnetcorev2", StringComparison.OrdinalIgnoreCase))
            && not (name.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase))
          // Shared-framework entries are only used when the app's own bin
          // doesn't already provide that assembly — the bin version is the
          // exact dependency the app was built against.
          let binNames = dlls |> List.map Path.GetFileName |> Set.ofList
          let combined =
            dlls
            |> List.append (aspNetShared |> List.filter (fun d -> not (binNames.Contains(Path.GetFileName d))))
            |> List.append (windowsDesktopShared |> List.filter (fun d -> not (binNames.Contains(Path.GetFileName d))))
            |> List.filter isManagedRef
            |> List.distinct
          logger.LogInfo (sprintf "  Collected %d reference DLL(s) from %s (%d ASP.NET + %d WindowsDesktop shared framework)" combined.Length binDir aspNetShared.Length windowsDesktopShared.Length)
          combined)

  /// Parse an .fsproj (and its project references) into FSharpProjectOptions.
  /// Returns None if the file doesn't exist or has no source files.
  let parseFsproj (logger: ILogger) (projPath: string) : FSharpProjectOptions list =
    let files, _ = collectSourceFiles projPath Set.empty []
    match files with
    | [] ->
      logger.LogWarning (sprintf "  Manual parse of %s found no source files" (Path.GetFileName projPath))
      []
    | _ ->
      logger.LogInfo (sprintf "  Manual parse of %s found %d source file(s)" (Path.GetFileName projPath) files.Length)
      [ { ProjectFileName = Path.GetFullPath projPath
          ProjectId = None
          SourceFiles = files |> List.toArray
          OtherOptions = [||]
          ReferencedProjects = [||]
          IsIncompleteTypeCheckEnvironment = false
          UseScriptResolutionRules = false
          LoadTime = DateTime.UtcNow
          OriginalLoadReferences = []
          UnresolvedReferences = None
          Stamp = None } ]

/// Inert project facts injected into retained component tests. This is not a
/// project evaluator: named F# project/session construction is retired.
type ProjectMetadata = {
  ProjectFileName: string
  TargetPath: string
  TargetFramework: string
  OtherOptions: string list
  ReferencedProjects: string list
  /// Package identity and assembly path are distinct injected facts.
  PackageReferences: (string * string) list
  AllProperties: Map<string, Set<string>>
}

type Solution = {
  FsProjects: FSharpProjectOptions list
  Projects: ProjectMetadata list
  StartupFiles: FileName list
  References: DllName list
  LibPaths: DirName list
  OtherArgs: string list
}

let emptySolution = {
  FsProjects = []
  Projects = []
  StartupFiles = []
  References = []
  LibPaths = []
  OtherArgs = []
}

/// If a build output does not exist, probe the same path under the sibling
/// configuration (Debug ↔ Release) and return it when it exists. The retired loader
/// evaluated projects with MSBuild's default Configuration (Debug), so after a
/// Release-only build every Debug path it reports is missing — the project's
/// own bin/<Config>/<TFM>/x.dll AND the referenced projects' reference
/// assemblies at obj/<Config>/<TFM>/ref/x.dll. The nearest Debug/Release
/// directory in the path is the configuration, whatever the layout.
/// The same build-output path under the OTHER configuration (Debug ↔ Release),
/// or None when the path contains no Debug/Release segment. The nearest such
/// segment (searched from the end) is the configuration, whatever the layout.
let siblingConfigPath (dllPath: string) : string option =
  let separators = [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |]
  let segments = dllPath.Split separators
  let isConfig (segment: string) =
    String.Equals(segment, "Debug", StringComparison.OrdinalIgnoreCase)
    || String.Equals(segment, "Release", StringComparison.OrdinalIgnoreCase)
  match segments |> Array.tryFindIndexBack isConfig with
  | None -> None
  | Some index ->
    let sibling =
      match String.Equals(segments.[index], "Debug", StringComparison.OrdinalIgnoreCase) with
      | true -> "Release"
      | false -> "Debug"
    segments
    |> Array.mapi (fun i segment -> match i = index with | true -> sibling | false -> segment)
    |> String.concat (string Path.DirectorySeparatorChar)
    |> Some

let resolveSiblingConfigOutput (dllPath: string) : string option =
  try
    match File.Exists dllPath with
    | true -> Some dllPath
    | false ->
      match siblingConfigPath dllPath with
      | Some candidate when File.Exists candidate -> Some candidate
      | _ -> None
  with _ -> None

/// The FRESHEST existing build output across the Debug/Release sibling
/// configurations. Historical regression: the retired loader evaluated projects
/// with MSBuild's default Configuration (Debug), so it reports a bin/Debug
/// TargetPath even when the user's real, current build is Release — and if a
/// STALE Debug output happens to exist, `resolveSiblingConfigOutput` (which
/// stops at the first path that exists) would load that stale assembly into the
/// REPL. That was a genuine dogfood failure: a session ran a project's OLD code
/// while the freshly-built Release output sat unused. Picking the newest write
/// time across configs means the code you built is the code the REPL runs,
/// regardless of the initial config. Pure: existence + write time are
/// injected so the selection is unit-testable without a filesystem.
let chooseFreshestConfigOutputWith
    (exists: string -> bool)
    (writeTimeUtc: string -> DateTime)
    (dllPath: string) : string option =
  let candidates =
    dllPath :: (siblingConfigPath dllPath |> Option.toList)
    |> List.filter exists
  match candidates with
  | [] -> None
  | xs -> xs |> List.maxBy writeTimeUtc |> Some

let resolveFreshestConfigOutput (dllPath: string) : string option =
  try chooseFreshestConfigOutputWith File.Exists File.GetLastWriteTimeUtc dllPath
  with _ -> None

/// Which target framework each referenced project has to be loaded at.
///
/// Historical regression: the retired loader evaluated every project independently; for a project with
/// `<TargetFrameworks>net9.0;net10.0</TargetFrameworks>` it just takes the
/// FIRST one. It never asks the project that references it. So a net10.0 test
/// project referencing a multi-targeted library got the library's net9.0
/// TargetPath. After a normal `dotnet build` of the test project only the
/// net10.0 output exists, and warmup died with "Not all DLLs are found" on a
/// project that was built. When the net9.0 output did exist, it was worse:
/// the session quietly loaded the wrong build.
///
/// MSBuild already worked out the right answer. During the consumer's
/// design-time build the SDK's `_GetProjectReferenceTargetFrameworkProperties`
/// target runs NuGet's nearest-framework pick for every ProjectReference and
/// stamps it on the `_MSBuildProjectReferenceExistent` item as
/// `NearestTargetFramework`. That's the TFM `dotnet build` actually builds the
/// reference at, so that's the one we load. No guessing from folder names.
module ReferenceFrameworks =

  /// One loaded project as far as TFM planning cares: where it lives, what TFM
  /// it was evaluated at, and the TFM MSBuild picked for each project it
  /// references (keyed by full project path).
  type Node = {
    ProjectFile: string
    EvaluatedAt: string
    ReferencesAt: Map<string, string>
  }

  let private normalize (path: string) = Path.GetFullPath path

  /// The `NearestTargetFramework` MSBuild resolved for each of a project's
  /// ProjectReferences, read from the design-time build's items. A reference
  /// without that metadata (a non-SDK project, a failed resolution) is left out,
  /// so it keeps whatever TFM it was loaded at.
  let referencesAtOf (projectFile: string) (allItems: Map<string, Set<string * Map<string, string>>>) : Map<string, string> =
    let dir = Path.GetDirectoryName(normalize projectFile)
    match allItems.TryFind "_MSBuildProjectReferenceExistent" with
    | None -> Map.empty
    | Some items ->
      items
      |> Seq.choose (fun (include', metadata) ->
        match metadata.TryFind "NearestTargetFramework" with
        | Some tfm when not (String.IsNullOrWhiteSpace tfm) ->
          let relative = include'.Replace('\\', Path.DirectorySeparatorChar)
          Some (normalize (Path.Combine(dir, relative)), tfm)
        | _ -> None)
      |> Map.ofSeq

  /// Projects nothing else in the closure references: the ones the user asked
  /// for. They keep the TFM they were loaded at.
  let roots (nodes: Node list) : Node list =
    let referenced = nodes |> Seq.collect (fun n -> n.ReferencesAt.Keys) |> Set.ofSeq
    nodes |> List.filter (fun n -> not (referenced.Contains n.ProjectFile))

  /// The TFM every reachable project should be loaded at, walking breadth-first
  /// from the roots. A project referenced by two consumers that want different
  /// TFMs gets the first one reached: FSI can only load one copy of an
  /// assembly, and the one closest to what the user asked for wins.
  ///
  /// The walk only goes THROUGH a project that's already loaded at the TFM it
  /// should be. A project loaded at the wrong TFM reports the references of
  /// that wrong build (a net10.0 Bozzetto.fsproj says "Core at net10.0"), so its
  /// children wait until it's been reloaded. `settle` does that.
  let plan (nodes: Node list) : Map<string, string> =
    let byPath = nodes |> List.map (fun n -> n.ProjectFile, n) |> Map.ofList
    let rec walk (queue: (string * string) list) (wanted: Map<string, string>) =
      match queue with
      | [] -> wanted
      | (path, _) :: rest when wanted.ContainsKey path -> walk rest wanted
      | (path, tfm) :: rest ->
        let children =
          match byPath.TryFind path with
          | Some node when node.EvaluatedAt = tfm -> node.ReferencesAt |> Map.toList
          | _ -> []
        walk (rest @ children) (wanted.Add(path, tfm))
    walk (roots nodes |> List.map (fun n -> n.ProjectFile, n.EvaluatedAt)) Map.empty

  /// Projects loaded at a TFM other than the one `plan` says, with the TFM
  /// they should be reloaded at.
  let mismatches (nodes: Node list) : (string * string) list =
    let wanted = plan nodes
    nodes
    |> List.choose (fun n ->
      match wanted.TryFind n.ProjectFile with
      | Some tfm when tfm <> n.EvaluatedAt -> Some (n.ProjectFile, tfm)
      | _ -> None)

  /// Reload mismatched projects until every project sits at the TFM its
  /// consumer needs. `reload tfm paths` evaluates those projects at that TFM
  /// (a real MSBuild evaluation, so TargetPath, compiler args and package
  /// references all come from the right build). Each (project, TFM) pair is
  /// tried once: when a reload can't produce it, the original stays and the
  /// missing-DLL check reports exactly where it looked. That bounds the loop
  /// at one attempt per pair.
  let settle (toNode: 'P -> Node) (reload: string -> string list -> 'P list) (projects: 'P list) : 'P list =
    let rec go (projects: 'P list) (attempted: Set<string * string>) =
      let pending =
        projects
        |> List.map toNode
        |> mismatches
        |> List.filter (attempted.Contains >> not)
      match pending with
      | [] -> projects
      | _ ->
        let pendingSet = Set.ofList pending
        let reloaded =
          pending
          |> List.groupBy snd
          |> List.collect (fun (tfm, group) -> reload tfm (group |> List.map fst))
          |> List.choose (fun p ->
            let node = toNode p
            match pendingSet.Contains (node.ProjectFile, node.EvaluatedAt) with
            | true -> Some (node.ProjectFile, p)
            | false -> None)
          |> Map.ofList
        let projects' =
          projects
          |> List.map (fun p ->
            reloaded.TryFind (toNode p).ProjectFile |> Option.defaultValue p)
        go projects' (Set.union attempted pendingSet)
    go projects Set.empty

/// Retained component-test progress fold over injected loading events.
/// It performs no project evaluation and carries no loader dependency.
module ProjectLoadProgress =
  /// The injected running project count; it only grows so step <= total.
  type State = { Completed: int; KnownTotal: int }

  let initial = { Completed = 0; KnownTotal = 0 }

  /// An injected project-evaluation event used by retained component tests.
  type Update =
    | Loading of projectFile: string
    | Loaded of projectFile: string * knownProjectCount: int
    | Failed of projectFile: string

  /// Fold one update through the state. Returns `None` for `Loading` — nothing
  /// has completed yet, so there is no honest `step` to report (a `0/N` line
  /// would violate the same "step must be positive" rule the wire format
  /// already enforces) — and `Some (step, total, message)` once a project
  /// finishes, one way or the other.
  let step (s: State) (update: Update) : State * (int * int * string) option =
    match update with
    | Loading _ ->
      let total = max s.KnownTotal (s.Completed + 1)
      { s with KnownTotal = total }, None
    | Loaded (file, knownCount) ->
      let completed = s.Completed + 1
      let total = max knownCount completed
      { Completed = completed; KnownTotal = total },
      Some(completed, total, sprintf "Loaded %s" (Path.GetFileName file))
    | Failed file ->
      let completed = s.Completed + 1
      let total = max s.KnownTotal completed
      { Completed = completed; KnownTotal = total },
      Some(completed, total, sprintf "Failed to load %s" (Path.GetFileName file))

/// Shared classification vocabulary for injected metadata and fixture XML.
let private isTestPackageName = TestProviderCatalog.isTestPackageName

/// Detect if a project is a test project via MSBuild property or package references.
let isTestProject (proj: ProjectMetadata) : bool =
  match proj.AllProperties.TryFind "IsTestProject" with
  | Some vals when vals |> Set.exists (fun v -> String.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) -> true
  | _ ->
    proj.PackageReferences
    |> List.exists (fun (name, _path) -> isTestPackageName name)

/// Filter a solution's projects to only test projects.
let discoverTestProjects (projects: ProjectMetadata list) : ProjectMetadata list =
  projects |> List.filter isTestProject

/// Desktop-UI frameworks are enabled by MSBuild properties, not packages
/// (WPF/WinForms have no package — they are `<UseWPF>`/`<UseWindowsForms>` on a
/// `-windows` TFM; MAUI/WinUI add `<UseMaui>`/`<UseWinUI>` alongside packages).
/// Surface the active ones as classification markers so ProjectKind can see a
/// desktop UI it could never detect from package references alone.
let private activeUiPropertyMarkers (proj: ProjectMetadata) : string list =
  [ "UseWPF"; "UseWindowsForms"; "UseMaui"; "UseWinUI" ]
  |> List.filter (fun prop ->
    match proj.AllProperties.TryFind prop with
    | Some vals -> vals |> Set.exists (fun v -> String.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
    | None -> false)

/// Classify a single project by its role (Executable, Library, or Test).
/// Uses MSBuild OutputType property and test package reference heuristics.
let classifyProject (proj: ProjectMetadata) : ClassifiedProject =
  let role =
    match proj.AllProperties.TryFind "OutputType" with
    | Some vals when vals |> Set.exists (fun v -> String.Equals(v, "Exe", StringComparison.OrdinalIgnoreCase)) -> ProjectRole.Executable
    | _ ->
      if isTestProject proj then ProjectRole.Test
      else ProjectRole.Library
  let packageRefs = proj.PackageReferences |> List.map fst
  { Path = proj.ProjectFileName
    Role = role
    // Package refs plus active desktop-UI property markers (UseWPF/…), so a
    // WPF/WinForms/MAUI/WinUI project — whose UI framework is a property, not a
    // package — still classifies as native-GUI; plus the .fsproj's own SDK and
    // FrameworkReference markers, so a plain ASP.NET Core / Minimal API project
    // — which reaches ASP.NET through `Sdk="Microsoft.NET.Sdk.Web"` and a
    // FrameworkReference, and carries NO web PackageReference — still
    // classifies as web instead of falling through to Console.
    PackageRefs =
      packageRefs
      @ activeUiPropertyMarkers proj
      @ WorkflowTypes.ProjectFileMarkers.read proj.ProjectFileName }

/// Classify all projects in a solution, returning a map of path to classification.
let classifyProjects (projects: ProjectMetadata list) : ClassifiedProject list =
  projects |> List.map classifyProject

/// Classification properties read from component-test fixture XML.
type private FallbackProjectProps = {
  OutputType: string option
  IsTestProject: bool option
  PackageRefs: string list
}

let private readFallbackProjectProps (projPath: string) : FallbackProjectProps =
  try
    let doc = XDocument.Load (Path.GetFullPath projPath)
    let propValue (name: string) =
      doc.Descendants(XName.Get name) |> Seq.tryHead |> Option.map (fun e -> e.Value.Trim())
    let outputType = propValue "OutputType"
    let isTestProjectProp =
      propValue "IsTestProject"
      |> Option.map (fun v -> String.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
    let packageRefs =
      doc.Descendants(XName.Get "PackageReference")
      |> Seq.choose (fun el -> el.Attribute(XName.Get "Include") |> Option.ofObj |> Option.map (fun a -> a.Value))
      |> Seq.toList
    // Same desktop-UI markers as activeUiPropertyMarkers, read straight from
    // the XML since there is no ProjectMetadata.AllProperties here.
    let uiMarkers =
      [ "UseWPF"; "UseWindowsForms"; "UseMaui"; "UseWinUI" ]
      |> List.filter (fun prop ->
        match propValue prop with
        | Some v -> String.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
        | None -> false)
    // Fixture SDK/FrameworkReference markers come from the same
    // single reader — so the fallback path cannot classify a Minimal API
    // project differently from the normal one.
    let projectFileMarkers = WorkflowTypes.ProjectFileMarkers.read projPath
    { OutputType = outputType
      IsTestProject = isTestProjectProp
      PackageRefs = packageRefs @ uiMarkers @ projectFileMarkers }
  with _ ->
    { OutputType = None; IsTestProject = None; PackageRefs = [] }

/// Classify retained FCS fixture options using the fixture XML properties.
let classifyFallbackProject (fp: FSharpProjectOptions) : ClassifiedProject =
  let props = readFallbackProjectProps fp.ProjectFileName
  let role =
    match props.OutputType with
    | Some v when String.Equals(v, "Exe", StringComparison.OrdinalIgnoreCase) -> ProjectRole.Executable
    | _ ->
      match props.IsTestProject with
      | Some true -> ProjectRole.Test
      | _ when props.PackageRefs |> List.exists isTestPackageName -> ProjectRole.Test
      | _ -> ProjectRole.Library
  { Path = fp.ProjectFileName
    Role = role
    PackageRefs = props.PackageRefs }

/// Classify injected metadata and retained FCS fixture options.
/// The owning seam deduplicates by path before reading fixture properties.
let classifiedProjectsOf (sln: Solution) : ClassifiedProject list =
  let normal = classifyProjects sln.Projects
  let normalPaths = normal |> List.map (fun cp -> cp.Path) |> Set.ofList
  let fallback =
    sln.FsProjects
    |> List.filter (fun fp -> not (normalPaths.Contains fp.ProjectFileName))
    |> List.map classifyFallbackProject
  normal @ fallback

/// Best-effort target assembly for a fallback project: the manual fallback
/// never builds, so there is no per-project TargetPath — only the flat DLL
/// list `ManualProjectParse.collectBinReferences` gathered from every
/// project's bin dir. Match by expected output filename (`<ProjectName>.dll`).
/// When no match exists, the project has no known target — omitted rather
/// than pointing `run_app` at a guessed, possibly wrong, assembly.
let private fallbackProjectTarget (references: DllName list) (fp: FSharpProjectOptions) : (string * string) option =
  let expectedName = Path.GetFileNameWithoutExtension(fp.ProjectFileName) + ".dll"
  references
  |> List.tryFind (fun dll -> String.Equals(Path.GetFileName dll, expectedName, StringComparison.OrdinalIgnoreCase))
  |> Option.map (fun target -> fp.ProjectFileName, target)

/// Assembly paths from injected metadata and retained FCS fixture options.
let projectTargetsOf (sln: Solution) : (string * string) list =
  let normal = sln.Projects |> List.map (fun po -> po.ProjectFileName, po.TargetPath)
  let normalPaths = normal |> List.map fst |> Set.ofList
  let fallback =
    sln.FsProjects
    |> List.filter (fun fp -> not (normalPaths.Contains fp.ProjectFileName))
    |> List.choose (fallbackProjectTarget sln.References)
  normal @ fallback

/// Orders projects so every project appears AFTER all of its own project
/// references (a dependency-first topological sort by `ReferencedProjects`).
///
/// Historical regression: the retired loader returned the explicitly-requested project(s)
/// first, followed by their transitive references in discovery order — NOT
/// dependency order. Feeding FSI `-r:` flags in that order (the dependent
/// project's assembly referenced BEFORE the assembly it depends on) makes FCS
/// resolve an ambiguous cross-assembly name incorrectly: verified live, when
/// `Bozzetto.Tests.dll` (declaring namespace `Bozzetto.Tests`) was referenced
/// before `Bozzetto.dll` (declaring `[<RequireQualifiedAccess>] PaneId` — a
/// union type living directly in namespace `Bozzetto`, with a case named
/// `Tests`), resolving `Bozzetto.Tests.EvalTimelineTests` failed with "the
/// union case 'Tests' ... requires the union type name ('PaneId')" instead of
/// finding the sibling namespace — a bogus ambiguity between the namespace
/// segment and the qualified-access union case. Referencing `Bozzetto.dll`
/// first (as a normal build's dependency order does) resolves it correctly.
/// (roast-4 #0, dogfood REPL gate.)
let private topoSortByProjectReferences (projects: ProjectMetadata list) : ProjectMetadata list =
  let byPath =
    projects
    |> List.map (fun p -> Path.GetFullPath p.ProjectFileName, p)
    |> Map.ofList
  let visited = System.Collections.Generic.HashSet<string>()
  let result = System.Collections.Generic.List<ProjectMetadata>()
  let rec visit (p: ProjectMetadata) =
    let key = Path.GetFullPath p.ProjectFileName
    match visited.Add(key) with
    | false -> ()
    | true ->
      for r in p.ReferencedProjects do
        match byPath.TryFind(Path.GetFullPath r) with
        | Some referenced -> visit referenced
        | None -> ()
      result.Add(p)
  for p in projects do
    visit p
  result |> List.ofSeq

/// Where a missing DLL was supposed to come from.
[<RequireQualifiedAccess>]
type MissingDllSource =
  /// The build output of a loaded project, at the TFM it was loaded at.
  | ProjectOutput of project: string * targetFramework: string
  /// A package or compiler reference (not something a project here builds).
  | Reference

/// A DLL warmup needed and couldn't find, and every path it tried.
type MissingDll = {
  Dll: string
  LookedIn: string list
  Source: MissingDllSource
}

/// The warmup error for missing DLLs. It used to say "this project isn't
/// built yet" no matter what, which was a lie when Bozzetto was looking in the
/// wrong TFM folder of a project that WAS built. So it names every path it
/// tried, with the project and TFM each one belongs to. Then you can check
/// the folder yourself and tell "not built" from "looked in the wrong place".
let describeMissingDlls (missing: MissingDll list) : string =
  let describe (m: MissingDll) =
    let owner =
      match m.Source with
      | MissingDllSource.ProjectOutput (project, tfm) ->
        sprintf "%s, the %s output of %s" (Path.GetFileName m.Dll) tfm (Path.GetFileName project)
      | MissingDllSource.Reference -> sprintf "%s (a referenced assembly)" (Path.GetFileName m.Dll)
    let paths = m.LookedIn |> List.map (sprintf "      %s") |> String.concat "\n"
    sprintf "  - %s. Looked in:\n%s" owner paths
  let frameworks =
    missing
    |> List.choose (fun m ->
      match m.Source with
      | MissingDllSource.ProjectOutput (_, tfm) -> Some tfm
      | MissingDllSource.Reference -> None)
    |> List.distinct
  let notBuiltHint =
    match frameworks with
    | [] -> "the project isn't built yet"
    | tfms -> sprintf "the project isn't built for %s yet" (String.concat "/" tfms)
  sprintf
    "Not all DLLs are found (%d missing). These are the exact paths Bozzetto checked:\n%s\n\
     If those files don't exist, %s. If the DLL is sitting in some other folder, Bozzetto looked in the wrong place, \
     and that's a Bozzetto bug worth reporting with this message.\n\
     Named F# project sessions are retired. Validate these component-test outputs with dotnet build."
    missing.Length
    (missing |> List.map describe |> String.concat "\n")
    notBuiltHint

let solutionToFsiArgs (logger: ILogger) (_useAsp: bool) sln =
  let orderedProjects = topoSortByProjectReferences sln.Projects
  let projectDlls = orderedProjects |> Seq.map _.TargetPath

  let nugetDlls =
    orderedProjects |> Seq.collect _.PackageReferences |> Seq.map snd

  let otherDlls = sln.References

  let allDlls =
    projectDlls
    |> Seq.append nugetDlls
    |> Seq.append otherDlls
    |> Seq.distinct
    |> List.ofSeq

  // The -r: references each project passes its compiler (framework assemblies
  // and referenced projects' reference assemblies), resolved to outputs that
  // exist — after a Release-only build they point into obj/Debug and would
  // otherwise kill FSI at startup with a bare StopProcessingExn.
  //
  // Retained fixture compiler options can carry
  // each project's OWN "-r:" flags as MSBuild originally emitted them —
  // including project-to-project references resolved to their COMPILE-TIME
  // "obj/<Config>/<TFM>/ref/X.dll" reference assemblies, which are NEVER
  // rewritten to the shadow-copy path the way projectDlls' TargetPath is.
  // Feeding FSI both "-r:<shadow>/X.dll" (allDlls, real IL, the assembly the
  // session actually executes) AND "-r:.../obj/.../ref/X.dll" (compilerRefs,
  // a distinct file with the SAME assembly identity) hands the compiler two
  // separate physical files for one logical assembly — a needless duplicate
  // reference (and, on a Release-only build, a stale/missing-DLL risk since
  // the ref/ path is never shadow-copied). allDlls already carries the one
  // copy FSI should execute against, so any compilerRefs entry naming the
  // same file is dropped.
  let allDllNames =
    allDlls
    |> Seq.map Path.GetFileName
    |> Set.ofSeq

  let compilerRefs =
    orderedProjects
    |> Seq.collect _.OtherOptions
    |> Seq.filter (fun s ->
      s.StartsWith("-r:", System.StringComparison.Ordinal)
      && s.EndsWith(".dll", System.StringComparison.Ordinal))
    |> Seq.map (fun s ->
      let path = s.Substring 3
      resolveSiblingConfigOutput path |> Option.defaultValue path)
    |> Seq.filter (fun path -> not (allDllNames.Contains(Path.GetFileName path)))
    |> Seq.distinct
    |> List.ofSeq

  match (allDlls @ compilerRefs) |> List.filter (File.Exists >> not) |> List.distinct with
  | [] -> ()
  | missing ->
    for dll in missing do
      logger.LogError (sprintf "Missing DLL: %s" dll)
    let producedBy =
      orderedProjects
      |> List.map (fun po -> po.TargetPath, MissingDllSource.ProjectOutput (po.ProjectFileName, po.TargetFramework))
      |> Map.ofList
    missing
    |> List.map (fun dll ->
      { Dll = dll
        LookedIn = dll :: (siblingConfigPath dll |> Option.toList)
        Source = producedBy.TryFind dll |> Option.defaultValue MissingDllSource.Reference })
    |> describeMissingDlls
    |> failwith
  // Flags from project OtherOptions that FSI should inherit for source-level
  // compatibility (e.g. --checknulls+ from <Nullable>enable</Nullable>).
  // We explicitly exclude --warnaserror (too strict for REPL) and --optimize
  // (irrelevant for interactive eval).
  let fsiSafeFlags =
    orderedProjects
    |> Seq.collect _.OtherOptions
    |> Seq.filter (fun s ->
      s.StartsWith("--checknulls", System.StringComparison.Ordinal)
      || s.StartsWith("--nowarn", System.StringComparison.Ordinal)
      || s.StartsWith("--langversion", System.StringComparison.Ordinal))
    |> Seq.distinct

  [|
    "fsi"
    // "--multiemit-" disables FSI multi-assembly mode, keeping all code in a single
    // assembly. This prevents the canonical F# pattern (type T + module T) from breaking
    // across submission boundaries. Always enable to ensure type references remain valid.
    "--multiemit-"
    yield! allDlls |> Seq.map (sprintf "-r:%s")
    yield! sln.LibPaths |> Seq.map (sprintf "--lib:%s")
    yield! sln.OtherArgs
    yield! fsiSafeFlags
    // Always include the projects' compiler references (framework assemblies
    // such as ASP.NET Core, and referenced projects) — resolved above.
    yield! compilerRefs |> Seq.map (sprintf "-r:%s")
  |]
