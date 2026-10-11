/// Explicit workflows for retained F# implementation tooling.
module Bozzetto.WorkflowTypes

open System

// ─── Web markers — the single source of truth ───────────────

/// Every token that means "this project serves HTTP", in ONE list.
///
/// Why one list: this used to be two — `ProjectKind.classify`'s private
/// `webPackages` and `WorkflowDetection.suggest`'s own private near-copy. Two
/// lists that had to agree, with nothing forcing them to, and they had already
/// drifted. Both paths now read this module, and `ProjectClassificationTests`
/// iterates `all` so a marker added here is automatically required to behave
/// identically on both paths.
module WebMarkers =

  /// F# / ASP.NET Core server-side web frameworks.
  ///
  /// `Microsoft.AspNetCore` covers both an explicit `Microsoft.AspNetCore.*`
  /// package AND the `Microsoft.AspNetCore.App` FrameworkReference marker that
  /// `ProjectFileMarkers` contributes, by substring.
  let serverFrameworks =
    [ "Falco"; "Giraffe"; "Saturn"; "Oxpecker"; "Microsoft.AspNetCore" ]

  /// Markers that come from the `.fsproj` XML rather than any package — see
  /// `ProjectFileMarkers`. A modern ASP.NET Core / Minimal API project reaches
  /// ASP.NET through the Web SDK and a FrameworkReference and carries no web
  /// `<PackageReference>` at all, so without these it classified as Console.
  let projectFile = [ "Microsoft.NET.Sdk.Web" ]

  /// The whole vocabulary. Order is irrelevant — matching is substring.
  let all = serverFrameworks @ projectFile |> List.distinct

  /// Substring match, case-INSENSITIVE. Package ids are not case-normalised by
  /// NuGet in a way anyone should depend on, and a case-sensitive version of
  /// this check once silently missed a real package id.
  let private containsCI (needle: string) (haystack: string) =
    haystack.Contains(needle, StringComparison.OrdinalIgnoreCase)

  /// The references that matched any of `markers`.
  let findMatches (markers: string list) (refs: string list) =
    refs |> List.filter (fun r -> markers |> List.exists (fun m -> containsCI m r))

  /// Whether any reference matched any of `markers`.
  let matchesAny (markers: string list) (refs: string list) =
    refs |> List.exists (fun r -> markers |> List.exists (fun m -> containsCI m r))

// ─── Fable (browser) markers ────────────────────────────────

/// Packages whose real runtime is a browser, not the CLR.
///
/// Two jobs. First, precision for `WebMarkers`: `Oxpecker.Solid` is a
/// Fable/Solid.js CLIENT library (its nuspec reads "F# web framework built on
/// top of Solid.js" and it depends on Fable.Core and Fable.Browser.Dom), so
/// substring-matching `Oxpecker` would advertise browser hot reload for a
/// project that has no ASP.NET server in it at all. Second, this is the list
/// `ProjectCompatibility` uses to tell a user, truthfully, what Bozzetto can and
/// cannot do with a Fable client project.
///
/// Every entry below was checked against the real package: `dotnet build`
/// SUCCEEDS for all of them (they ship a real `lib/netstandard2.0/*.dll`), so
/// there is nothing to detect at build time — the stubs only throw when
/// EVALUATED.
module FableMarkers =

  /// Package-id prefixes whose members are browser/JS stubs on .NET.
  let jsOnly =
    [ "Fable.Core"           // JS/JsInterop: "You've hit dummy code used for Fable bindings"
      "Fable.Browser."       // Browser.Dom etc: "JS only"
      "Feliz"                // React view builders: InvalidCastException
      "Fable.React"
      "Fable.Elmish.React"
      "Fable.Elmish.Browser"
      "Fable.Elmish.HMR"
      "Fable.Remoting.Client"
      "Fable.Promise"
      "Fable.Fetch"
      "Fable.Lit"
      "Sutil"
      "Oxpecker.Solid"       // Solid.js client, NOT the Oxpecker ASP.NET server
      "Partas.Solid" ]

  /// Exact package ids that LOOK like the prefixes above but are ordinary .NET
  /// libraries, so they must never be treated as browser-only. Measured, not
  /// assumed: `Fable.Elmish`'s MVU core genuinely runs on .NET —
  /// `Program.mkSimple ... |> Program.run` executes its update/view loop in a
  /// plain console app. `Feliz.ViewEngine` renders HTML server-side. Checked
  /// BEFORE `jsOnly`, and by exact id, so the longer `Fable.Elmish.React`
  /// prefix still matches.
  let dotNetDespiteName =
    [ "Fable.Elmish"
      "Feliz.ViewEngine"
      "Fable.Remoting.Server"
      "Fable.Remoting.Giraffe"
      "Fable.Remoting.Suave"
      "Fable.Remoting.AspNetCore"
      "Fable.Remoting.Json" ]

  /// Whether ONE reference is a browser-only Fable package.
  let isJsOnly (reference: string) =
    let r = reference.Trim()
    let isAllowed =
      dotNetDespiteName
      |> List.exists (fun allowed -> String.Equals(r, allowed, StringComparison.OrdinalIgnoreCase))
    match isAllowed with
    | true -> false
    | false ->
      jsOnly
      |> List.exists (fun prefix -> r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

  /// The references that are browser-only Fable packages.
  let findMatches (refs: string list) = refs |> List.filter isJsOnly

// ─── Project-file markers ───────────────────────────────────

/// Classification markers that live in the `.fsproj` XML itself rather than in
/// any `<PackageReference>`: the `Sdk` attribute on `<Project>`, and
/// `<FrameworkReference Include="..." />`.
///
/// This exists because package references alone cannot see a plain ASP.NET Core
/// or Minimal API project. Verified against this repo's own
/// `Bozzetto.Tests/fixtures/WebAppFixture/WebAppFixture.fsproj`: `Sdk =
/// "Microsoft.NET.Sdk.Web"`, zero `<PackageReference>` elements, and the live
/// daemon reports it as `PackageRefs: []`. Every such project — plain ASP.NET,
/// Minimal API, Oxpecker, Giraffe-via-framework-ref — classified as Console and
/// was therefore never offered the hot-reload workflow.
///
/// Markers are raw strings so they compose with package references in the
/// single `string list` both classification paths already take — the same
/// pattern `ProjectLoading.activeUiPropertyMarkers` uses for `UseWPF`.
module ProjectFileMarkers =

  open System.Xml.Linq

  /// The default SDK. It is what a project says when it has nothing to say, so
  /// it is not a marker — emitting it would put `Microsoft.NET.Sdk` in the
  /// user-visible `PackageRefs` of every project in the repo for no
  /// information. Every OTHER SDK (`.Web`, `.Razor`, `.BlazorWebAssembly`, …)
  /// genuinely narrows what the project is.
  let private defaultSdk = "Microsoft.NET.Sdk"

  /// Parse markers out of raw `.fsproj` XML. Pure — no IO.
  ///
  /// Best-effort by construction: malformed or empty XML yields `[]` rather
  /// than throwing. A marker we cannot read costs at most a workflow
  /// suggestion; it must never cost the session.
  let parse (fsprojXml: string) : string list =
    match String.IsNullOrWhiteSpace fsprojXml with
    | true -> []
    | false ->
      try
        let doc = XDocument.Parse fsprojXml
        let sdkAttr =
          doc.Root
          |> Option.ofObj
          |> Option.bind (fun root -> root.Attribute(XName.Get "Sdk") |> Option.ofObj)
          |> Option.map (fun a -> a.Value.Trim())
          |> Option.filter (fun v ->
            v <> "" && not (String.Equals(v, defaultSdk, StringComparison.OrdinalIgnoreCase)))
          |> Option.toList
        let frameworkRefs =
          doc.Descendants(XName.Get "FrameworkReference")
          |> Seq.choose (fun el ->
            el.Attribute(XName.Get "Include")
            |> Option.ofObj
            |> Option.map (fun a -> a.Value.Trim()))
          |> Seq.filter (fun v -> v <> "")
          |> Seq.toList
        sdkAttr @ frameworkRefs |> List.distinct
      with _ -> []

  /// The one IO edge. Any failure to read the file yields `[]`, never an
  /// exception — see `parse`.
  let read (projPath: string) : string list =
    try
      parse (IO.File.ReadAllText projPath)
    with _ -> []

// ─── Paket references ───────────────────────────────────────

/// A Paket-managed project lists its packages in a sibling `paket.references`
/// file and carries NO `<PackageReference>` in the .fsproj at all.
///
/// This matters more than it looks: the SAFE stack template — by far the most
/// common Fable/full-stack F# layout — is Paket-managed, so a
/// `<PackageReference>`-only reader sees an empty package list for its Client
/// project and can say nothing useful about it. Ionide's resolved view does see
/// them, but the MCP `create_session` hint runs before any worker has loaded
/// anything and has only the files on disk.
module PaketReferences =

  /// Parse package ids out of `paket.references` text. One id per line;
  /// `group <name>` headers, comments and framework-restriction suffixes are
  /// dropped. Pure — no IO.
  let parse (text: string) : string list =
    match String.IsNullOrWhiteSpace text with
    | true -> []
    | false ->
      text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
      |> Array.map (fun line -> line.Trim())
      |> Array.filter (fun line ->
        line <> ""
        && not (line.StartsWith("#", StringComparison.Ordinal))
        && not (line.StartsWith("//", StringComparison.Ordinal))
        && not (line.StartsWith("group ", StringComparison.OrdinalIgnoreCase)))
      // `Foo.Bar framework: net10.0` → `Foo.Bar`
      |> Array.map (fun line -> line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries).[0])
      |> Array.distinct
      |> Array.toList

  /// Read the `paket.references` sitting next to a project file, if any. The
  /// one IO edge; a missing or unreadable file yields `[]`, never an exception.
  let readForProject (projPath: string) : string list =
    try
      let dir = IO.Path.GetDirectoryName(projPath: string)
      match String.IsNullOrEmpty dir with
      | true -> []
      | false ->
        let path = IO.Path.Combine(dir, "paket.references")
        match IO.File.Exists path with
        | false -> []
        | true -> parse (IO.File.ReadAllText path)
    with _ -> []

// ─── Browser refresh configuration ──────────────────────────

/// Configuration for the browser hot-reload pipeline.
/// Only meaningful when the workflow uses SaveDriven feedback.
type BrowserRefreshConfig = {
  /// File patterns that trigger a browser refresh on save.
  WatchPatterns: string list
}

module BrowserRefreshConfig =
  let defaults = { WatchPatterns = [ "*.fs"; "*.fsx" ] }

// ─── Project kind ───────────────────────────────────────────

/// Classifies project runtime shape for tooling and presentation.
[<RequireQualifiedAccess>]
type ProjectKind =
  /// A web application and its browser refresh configuration.
  | Web of BrowserRefreshConfig
  /// A console or headless application.
  | Console
  /// A native windowed application.
  | NativeGui

module ProjectKind =

  /// Native game AND desktop-UI markers — both run a native window with a
  /// render/event loop and no WebApplication. Some desktop frameworks are
  /// enabled by an MSBuild PROPERTY, not a package (WPF/WinForms have no
  /// package at all — see ProjectLoading.classifyProject, which surfaces the
  /// active `Use*` properties as classification markers).
  let private nativeGuiPackages =
    [ "Raylib"; "SDL2"; "Silk.NET"; "MonoGame"; "SFML"          // game packages
      "Avalonia"; "Microsoft.Maui"; "Microsoft.WindowsAppSDK"   // desktop-UI packages
      "Microsoft.WinUI"; "Uno.UI"; "Uno.WinUI"
      "UseWPF"; "UseWindowsForms"; "UseMaui"; "UseWinUI" ]       // desktop-UI MSBuild property markers

  /// Classify a project by its package references. NativeGui wins over Web wins
  /// over Console: a native game/desktop-UI library dominates the runtime shape,
  /// then a web framework, else a plain console/headless app.
  ///
  /// `packageRefs` is the project's package references PLUS any non-package
  /// classification markers the loader surfaced for it — `UseWPF` and friends
  /// from `ProjectLoading.activeUiPropertyMarkers`, and the Web SDK /
  /// FrameworkReference markers from `ProjectFileMarkers`.
  let classify (packageRefs: string list) : ProjectKind =
    let hasNativeGui =
      packageRefs |> List.exists (fun ref -> nativeGuiPackages |> List.exists ref.Contains)
    // A browser-only Fable package can never be the evidence that a project is
    // an ASP.NET web app. Without this, `Oxpecker.Solid` — a Fable/Solid.js
    // CLIENT library — would match the `Oxpecker` server marker by substring
    // and a pure browser project would be offered browser hot reload it cannot
    // possibly use.
    let webEvidence = packageRefs |> List.filter (FableMarkers.isJsOnly >> not)
    if hasNativeGui then ProjectKind.NativeGui
    elif WebMarkers.matchesAny WebMarkers.all webEvidence then
      ProjectKind.Web BrowserRefreshConfig.defaults
    else ProjectKind.Console

  /// Short user-facing label.
  let label = function
    | ProjectKind.Web _     -> "web"
    | ProjectKind.Console   -> "console"
    | ProjectKind.NativeGui -> "native-gui"

/// Retained F# implementation workflows; neither rewrites running methods.
[<RequireQualifiedAccess>]
type SessionWorkflow =
  | Interactive
  | LiveTesting

module SessionWorkflow =
  let label = function
    | SessionWorkflow.Interactive -> "REPL"
    | SessionWorkflow.LiveTesting -> "Live Testing"

  let defaultWorkflow = SessionWorkflow.Interactive

  /// Explicitly reject unsupported modes, including retired method patching.
  let tryOfString (s: string) : SessionWorkflow option =
    match (s |> Option.ofObj |> Option.defaultValue "").Trim().ToLowerInvariant() with
    | "interactive" | "repl" | "normal" -> Some SessionWorkflow.Interactive
    | "livetesting" | "live-testing" | "testing" | "test" -> Some SessionWorkflow.LiveTesting
    | _ -> None

  let ofString (s: string) : SessionWorkflow =
    match tryOfString s with
    | Some workflow -> workflow
    | None -> invalidArg (nameof s) "Unsupported workflow. Use interactive or livetesting."

// ─── Transition cost ────────────────────────────────────────

/// What the user will lose when switching workflows.
/// Computed before the switch happens — the UI renders this for confirmation.
type TransitionCost = {
  /// Number of REPL let-bindings that will be cleared.
  DefinitionsLost: int
  /// Number of evaluated cells that will be lost.
  CellsLost: int
  /// Estimated time for the new session to warm up.
  EstimatedRestart: System.TimeSpan
}

module TransitionCost =
  let zero = {
    DefinitionsLost = 0
    CellsLost = 0
    EstimatedRestart = System.TimeSpan.Zero
  }

  /// Zero-cost switches skip confirmation.
  /// True when there's no REPL state to lose.
  let isZeroCost (cost: TransitionCost) =
    cost.DefinitionsLost = 0 && cost.CellsLost = 0

  /// Compute transition cost from observable session state.
  /// Every switch spawns a fresh session, so restart always reflects
  /// the cold-start estimate — there is no standby pool anymore.
  let compute (evalCount: int) (cellCount: int) = {
    DefinitionsLost = evalCount
    CellsLost = cellCount
    EstimatedRestart = System.TimeSpan.FromSeconds 15.0
  }

// ─── Workflow switch outcome ────────────────────────────────

/// Outcome of a switch_workflow call — makes impossible states unrepresentable.
///
/// The old record type allowed `Switched=true, NewSessionId=None` (switched
/// but no session?) and `Switched=false, NewSessionId=Some x` (didn't switch
/// but got a session?). This DU eliminates those impossible states:
/// - AlreadyActive structurally cannot carry a sessionId
/// - DryRunPreview structurally cannot carry a sessionId
/// - Executed always carries a sessionId (non-optional)
[<RequireQualifiedAccess>]
type WorkflowSwitchOutcome =
  /// Target = current workflow — nothing happened, zero side effects.
  | AlreadyActive of cost: TransitionCost * message: string
  /// Dry-run preview — shows cost without executing.
  | DryRunPreview of cost: TransitionCost * message: string
  /// Switch executed — new session created, old session stopped.
  | Executed of
      previous: SessionWorkflow *
      target: SessionWorkflow *
      cost: TransitionCost *
      sessionId: string *
      message: string

module WorkflowSwitchOutcome =

  /// Create a no-op outcome when target = current workflow.
  let alreadyInWorkflow (workflow: SessionWorkflow) (cost: TransitionCost) =
    WorkflowSwitchOutcome.AlreadyActive (
      cost,
      sprintf "Already in %s workflow — no switch needed"
        (SessionWorkflow.label workflow))

  /// Create a dry-run preview outcome.
  let preview
    (current: SessionWorkflow)
    (target: SessionWorkflow)
    (cost: TransitionCost) =
    WorkflowSwitchOutcome.DryRunPreview (
      cost,
      sprintf "Preview: switching from %s to %s would lose %d definitions and %d cells"
        (SessionWorkflow.label current)
        (SessionWorkflow.label target)
        cost.DefinitionsLost
        cost.CellsLost)

  /// Create a successful switch outcome.
  let switched
    (previous: SessionWorkflow)
    (target: SessionWorkflow)
    (cost: TransitionCost)
    (newSessionId: string) =
    WorkflowSwitchOutcome.Executed (
      previous, target, cost, newSessionId,
      sprintf "Switched from %s to %s (new session: %s)"
        (SessionWorkflow.label previous)
        (SessionWorkflow.label target)
        newSessionId)

  /// Extract cost from any outcome.
  let cost = function
    | WorkflowSwitchOutcome.AlreadyActive (c, _) -> c
    | WorkflowSwitchOutcome.DryRunPreview (c, _) -> c
    | WorkflowSwitchOutcome.Executed (_, _, c, _, _) -> c

  /// Extract human-readable message from any outcome.
  let message = function
    | WorkflowSwitchOutcome.AlreadyActive (_, m) -> m
    | WorkflowSwitchOutcome.DryRunPreview (_, m) -> m
    | WorkflowSwitchOutcome.Executed (_, _, _, _, m) -> m

  /// Extract session ID (only present for Executed outcomes).
  let sessionId = function
    | WorkflowSwitchOutcome.Executed (_, _, _, sid, _) -> Some sid
    | _ -> None

  /// Whether the outcome represents an actual switch execution.
  let wasExecuted = function
    | WorkflowSwitchOutcome.Executed _ -> true
    | _ -> false

// ─── Workflow suggestion (project detection) ────────────────

module WorkflowDetection =

  // ── Package extraction (pure) ─────────────────────────────

  /// Package names that indicate a test project.
  let isTestPackageSet (packages: string list) =
    packages |> List.exists TestProviderCatalog.isTestPackageName

  /// Extract package reference names from grouped per-project packages,
  /// filtering out test projects. Returns a distinct union of all names.
  let extractPackageNames (projectPackages: string list list) : string list =
    projectPackages
    |> List.filter (isTestPackageSet >> not)
    |> List.concat
    |> List.distinct
