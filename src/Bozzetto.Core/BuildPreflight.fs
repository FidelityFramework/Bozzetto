namespace Bozzetto

open System
open System.IO

[<RequireQualifiedAccess>]
type BuildPreflight =
  | Ready
  | NeedsRebuild of missing: string list
  | Unknown of reason: string

module BuildPreflight =
  let private hasCustomOutputPath (projectPath: string) (readText: string -> string) =
    try
      match XmlQuery.parseText (readText projectPath) with
      | Ok doc ->
        XmlQuery.descendantsNamed "OutputPath" doc.Root
        |> List.exists (fun element -> not (String.IsNullOrWhiteSpace (XmlQuery.innerText element)))
      | Error _ -> false
    with _ -> false

  let private conventionalGeneratedPaths (projectPath: string) =
    let dir = Path.GetDirectoryName(Path.GetFullPath projectPath)
    let name = Path.GetFileNameWithoutExtension projectPath
    [ Path.Combine(dir, "obj", "project.assets.json")
      Path.Combine(dir, "obj", name + ".fsproj.nuget.g.props") ]

  let private arcadeGeneratedPaths (projectPath: string) =
    let dir = Path.GetDirectoryName(Path.GetFullPath projectPath)
    let name = Path.GetFileNameWithoutExtension projectPath
    [ Path.Combine(dir, "artifacts", "obj", name, "project.assets.json")
      Path.Combine(dir, "artifacts", "obj", name, name + ".fsproj.nuget.g.props") ]

  let private preferredGeneratedPaths (projectPath: string) (fileExists: string -> bool) =
    let conventional = conventionalGeneratedPaths projectPath
    let arcade = arcadeGeneratedPaths projectPath
    match conventional @ arcade |> List.filter fileExists with
    | [] -> conventional
    | present ->
      if conventional |> List.exists (fun path -> List.contains path present) then conventional
      else arcade

  let check
    (fileExists: string -> bool)
    (readText: string -> string)
    (target: SessionProjectTarget)
    : BuildPreflight =
    match target with
    | SessionProjectTarget.Bare -> BuildPreflight.Ready
    | SessionProjectTarget.Solution _ -> BuildPreflight.Unknown "solution preflight is not classified yet"
    | SessionProjectTarget.Project projectPath ->
      match fileExists projectPath with
      | false -> BuildPreflight.NeedsRebuild [ Path.GetFullPath projectPath ]
      | true when hasCustomOutputPath projectPath readText -> BuildPreflight.Unknown "the project declares a custom OutputPath"
      | true ->
        let required = preferredGeneratedPaths projectPath fileExists
        let missing =
          required
          |> List.filter (fileExists >> not)
          |> List.distinct
        match missing with
        | [] -> BuildPreflight.Ready
        | missing -> BuildPreflight.NeedsRebuild missing

  let checkIn
    (fileExists: string -> bool)
    (readText: string -> string)
    (workingDir: string)
    (target: SessionProjectTarget)
    : BuildPreflight =
    match target with
    | SessionProjectTarget.Project path when not (Path.IsPathRooted path) ->
      check fileExists readText (SessionProjectTarget.Project (Path.Combine(workingDir, path)))
    | _ -> check fileExists readText target

  let describe = function
    | BuildPreflight.Ready -> "ready"
    | BuildPreflight.NeedsRebuild missing -> sprintf "needs rebuild: %s" (String.concat ", " missing)
    | BuildPreflight.Unknown reason -> sprintf "build state unknown: %s" reason
