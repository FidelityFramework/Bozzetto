namespace Bozzetto

/// Specifies how projects/solutions should be loaded for a session.
type LoadStrategy =
  /// Load a specific solution file (.sln/.slnx)
  | Solution of path: string
  /// Load specific project files (.fsproj)
  | Projects of paths: string list
  /// Auto-detect projects/solutions from the directory (default)
  | AutoDetect
  /// Bare FSI session — no project loading
  | NoLoad

/// Per-directory configuration via .bozzetto/config.fsx: load strategy, init script, default args.
///
/// Retained data model for inherited F# UI and component tests. Embedded
/// config.fsx evaluation is retired; separate SageFS owns F# execution.
type DirectoryConfig =
  { Load: LoadStrategy
    InitScript: string option
    DefaultArgs: string list
    AutoOpenNamespaces: bool
    /// When true, treat this directory as a session root — don't walk up to git/solution root.
    /// Use for monorepos where each subdirectory is an independent project.
    IsRoot: bool
    /// Optional friendly name for auto-created sessions. Defaults to the directory name.
    SessionName: string option }

/// The historical default value shared by inherited UI and component tests.
module DirectoryConfigDefaults =
  let empty : DirectoryConfig =
    { Load = AutoDetect
      InitScript = None
      DefaultArgs = []
      AutoOpenNamespaces = true
      IsRoot = false
      SessionName = None }
