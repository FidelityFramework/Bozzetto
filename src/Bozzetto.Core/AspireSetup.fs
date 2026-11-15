module Bozzetto.AspireSetup

open System
open System.IO
open System.Runtime.InteropServices
open Fidelity.Data.JSON
open Bozzetto.Utils

type LaunchProfile = {
  CommandName: string
  LaunchBrowser: bool
  EnvironmentVariables: Map<string, string>
  ApplicationUrl: string option
}

type LaunchSettings = {
  Profiles: Map<string, LaunchProfile>
}

let findLatestVersion (packagePath: string) =
  match Directory.Exists(packagePath) with
  | true ->
    Directory.GetDirectories(packagePath)
    |> Array.choose (fun d ->
      let v = Path.GetFileName d
      match v.Contains(".") with
      | true -> Some v
      | false -> None)
    |> Array.sortDescending
    |> Array.tryHead
  | false ->
    None

let getRidSuffix () =
  match RuntimeInformation.IsOSPlatform(OSPlatform.Windows) with
  | true -> "win-x64"
  | false ->
    match RuntimeInformation.IsOSPlatform(OSPlatform.Linux) with
    | true -> "linux-x64"
    | false ->
      match RuntimeInformation.IsOSPlatform(OSPlatform.OSX) with
      | true -> "osx-x64"
      | false -> "win-x64" // default

let getDcpExecutableName () =
  match RuntimeInformation.IsOSPlatform(OSPlatform.Windows) with
  | true -> "dcp.exe"
  | false -> "dcp"

let loadLaunchSettings (projectDir: string) : LaunchSettings option =
  try
    let launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json")
    match File.Exists(launchSettingsPath) with
    | true ->
      let json = File.ReadAllText(launchSettingsPath)
      match WireJson.parseRelaxed json with
      | Error _ -> None
      | Ok root ->
        let profiles =
          match JsonValue.prop "profiles" root with
          | Some (JsonValue.Object profileProps) ->
            profileProps
            |> List.map (fun (profileName, profile) ->
              let envVars =
                match JsonValue.prop "environmentVariables" profile with
                | Some (JsonValue.Object envProps) ->
                  envProps
                  |> List.choose (fun (k, v) -> JsonValue.asString v |> Option.map (fun s -> k, s))
                  |> Map.ofList
                | _ -> Map.empty
              let appUrl =
                JsonValue.prop "applicationUrl" profile
                |> Option.bind JsonValue.asString
              let commandName =
                JsonValue.prop "commandName" profile
                |> Option.bind JsonValue.asString
                |> Option.defaultValue "Project"
              let launchBrowser =
                JsonValue.prop "launchBrowser" profile
                |> Option.bind JsonValue.asBool
                |> Option.defaultValue false
              profileName, {
                CommandName = commandName
                LaunchBrowser = launchBrowser
                EnvironmentVariables = envVars
                ApplicationUrl = appUrl
              })
            |> Map.ofList
          | _ -> Map.empty
        Some { Profiles = profiles }
    | false ->
      None
  with _ ->
    None

let setupAspirePaths (logger: ILogger) =
  try
    let nugetPackages = 
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
    
    let rid = getRidSuffix()
    let dcpExeName = getDcpExecutableName()
    
    // Find DCP (Distributed Control Plane) path
    let dcpPackagePath = Path.Combine(nugetPackages, $"aspire.hosting.orchestration.%s{rid}")
    match findLatestVersion dcpPackagePath with
    | Some version ->
        let dcpExePath = Path.Combine(dcpPackagePath, version, "tools", dcpExeName)
        match File.Exists(dcpExePath) with
        | true ->
          Environment.SetEnvironmentVariable("DcpPublisherSettings__CliPath", dcpExePath)
          Environment.SetEnvironmentVariable("Bozzetto_ASPIRE_DCP_PATH", dcpExePath)
          logger.LogInfo $"Aspire DCP: %s{dcpExePath}"
        | false ->
          logger.LogDebug $"DCP executable not found at: %s{dcpExePath}"
    | None ->
        logger.LogDebug $"Aspire DCP package not found at: %s{dcpPackagePath}"
    
    // Find Dashboard path
    let dashboardPackagePath = Path.Combine(nugetPackages, $"aspire.dashboard.sdk.%s{rid}")
    match findLatestVersion dashboardPackagePath with
    | Some version ->
        // Set the path to the actual DLL, not just the tools directory
        let dashboardDllPath = Path.Combine(dashboardPackagePath, version, "tools", "Aspire.Dashboard.dll")
        match File.Exists(dashboardDllPath) with
        | true ->
          Environment.SetEnvironmentVariable("DcpPublisherSettings__DashboardPath", dashboardDllPath)
          Environment.SetEnvironmentVariable("Bozzetto_ASPIRE_DASHBOARD_PATH", dashboardDllPath)
          logger.LogInfo $"Aspire Dashboard: %s{dashboardDllPath}"
        | false ->
          logger.LogDebug $"Dashboard DLL not found at: %s{dashboardDllPath}"
    | None ->
        logger.LogDebug $"Aspire Dashboard package not found at: %s{dashboardPackagePath}"
    
    // Prevent duplicate endpoints by disabling launchSettings URL loading for child projects
    Environment.SetEnvironmentVariable("ASPNETCORE_SUPPRESS_LAUNCH_PROFILE_URLS", "true")
    logger.LogDebug "Suppressed launchSettings applicationUrl to prevent endpoint conflicts"
    
    // Enable hot reload for Aspire-launched projects via .NET Hot Reload
    Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "debug")
    logger.LogDebug "Enabled .NET Hot Reload for Aspire project resources"
    
  with ex ->
    logger.LogDebug $"Error setting up Aspire paths: %s{ex.Message}"

let applyLaunchProfile (logger: ILogger) (profile: LaunchProfile) (projectDir: string) =
  logger.LogInfo "Applying Aspire launch profile configuration..."
  
  // Apply all environment variables from the profile
  for KeyValue(key, value) in profile.EnvironmentVariables do
    Environment.SetEnvironmentVariable(key, value)
    logger.LogDebug $"  %s{key}=%s{value}"
  
  // Set application URL if present
  match profile.ApplicationUrl with
  | Some url ->
      Environment.SetEnvironmentVariable("ASPNETCORE_URLS", url)
      logger.LogInfo $"Application URL: %s{url}"
  | None -> ()

let hasAspireReferences (projects: ProjectLoading.Solution) =
  // Check both command-line references and project package references
  let hasInCommandLineRefs =
    projects.References
    |> List.exists (fun ref -> ref.Contains("Aspire.Hosting", StringComparison.OrdinalIgnoreCase))
  
  let hasInProjectRefs =
    projects.Projects
    |> List.exists (fun proj -> 
      proj.PackageReferences
      |> Seq.exists (fun (name, _path) -> name.Contains("Aspire.Hosting", StringComparison.OrdinalIgnoreCase)))
  
  hasInCommandLineRefs || hasInProjectRefs

let configureAspireIfNeeded (logger: ILogger) (solution: ProjectLoading.Solution) =
  match hasAspireReferences solution with
  | true ->
    logger.LogWarning "⚠️  Aspire AppHost project detected"
    logger.LogWarning "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    logger.LogWarning "Hot reload will NOT work for Aspire-orchestrated services!"
    logger.LogWarning ""
    logger.LogInfo "Aspire services run as separate processes, not in the FSI session."
    logger.LogInfo "For hot reload, load your F# web project directly instead:"
    logger.LogInfo "  ✅ Create a session for YourWebProject.fsproj"
    logger.LogInfo "  ❌ Create a session for AppHost.fsproj (limited functionality)"
    logger.LogWarning "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    logger.LogInfo ""
    logger.LogInfo "🚀 Configuring Aspire AppHost (non-blocking execution enabled)..."
    
    // Setup DCP and Dashboard paths
    setupAspirePaths logger
    
    // Try to load and apply launch settings from the first project
    match solution.Projects |> List.tryHead with
    | Some primaryProject ->
        let projectDir = Path.GetDirectoryName(primaryProject.ProjectFileName)
        match loadLaunchSettings projectDir with
        | Some launchSettings ->
            // Prefer "http" profile for development, fallback to first profile
            let profile = 
              launchSettings.Profiles 
              |> Map.tryFind "http"
              |> Option.orElseWith (fun () -> launchSettings.Profiles |> Map.toSeq |> Seq.tryHead |> Option.map snd)
            
            match profile with
            | Some p ->
                applyLaunchProfile logger p projectDir
                logger.LogInfo "✓ Aspire configured successfully"
            | None ->
                logger.LogWarning "No launch profiles found in launchSettings.json"
        | None ->
            logger.LogDebug "No launchSettings.json found, using basic Aspire configuration"
    | None -> ()
  | false ->
    ()
