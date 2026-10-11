namespace Bozzetto

open System
open System.IO
open Bozzetto.Utils

[<RequireQualifiedAccess>]
type AutoOpenNamespacesOptOutResult =
  | Created of path: string
  | AlreadyDisabled of path: string
  | RequiresManualEdit of path: string

[<RequireQualifiedAccess>]
type AutoOpenNamespacesOptInResult =
  | Enabled of path: string
  | AlreadyEnabled
  | RequiresManualEdit of path: string

/// Compatibility entry points for retired .bozzetto/config.fsx evaluation.
/// Existing files are preserved; Bozzetto neither evaluates them nor writes replacements.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DirectoryConfig =
  let empty = DirectoryConfigDefaults.empty

  let configDir (workingDir: string) =
    Path.Combine(workingDir, ".bozzetto")

  let configPath (workingDir: string) =
    Path.Combine(configDir workingDir, "config.fsx")

  /// Historical template kept for callers that display legacy configuration.
  /// Bozzetto no longer evaluates or writes it.
  let autoOpenNamespacesOptOutTemplate =
    "{ DirectoryConfig.empty with AutoOpenNamespaces = false }"

  /// Historical default expression; no executable configuration path remains.
  let autoOpenNamespacesOptInTemplate =
    "DirectoryConfig.empty"

  /// Refuse the former F# evaluation path without executing supplied code.
  let evaluateIn (_workingDir: string) (_content: string) : Result<DirectoryConfig, string> =
    Error ExternalFSharpService.message

  let evaluate (content: string) : Result<DirectoryConfig, string> =
    evaluateIn Environment.CurrentDirectory content

  let load (workingDir: string) : DirectoryConfig option =
    let path = configPath workingDir
    if File.Exists path then
      Log.warn "Config %s was preserved but was not evaluated: %s" path ExternalFSharpService.message
    None

  let autoOpenNamespacesForDirectory (workingDir: string) =
    load workingDir
    |> Option.map (fun cfg -> cfg.AutoOpenNamespaces)
    |> Option.defaultValue true

  /// Preserve any legacy config file and direct configuration to its F# provider.
  let ensureAutoOpenNamespacesOptOut (_workingDir: string) : Result<AutoOpenNamespacesOptOutResult, string> =
    Error ExternalFSharpService.message

  let ensureAutoOpenNamespacesOptIn (_workingDir: string) : Result<AutoOpenNamespacesOptInResult, string> =
    Error ExternalFSharpService.message
