namespace Bozzetto

#nowarn "3511"

open System
open System.IO
open Fidelity.Data.JSON
open System.Threading.Tasks
open System.Xml.Linq
open Bozzetto.AppState
open Bozzetto.WarmUp
open Bozzetto.Features.CellDependenciesReport
open Bozzetto.Utils

/// Explicit JSON primitives shared by MCP projections.
module internal McpJson =
  let text = WireJson.text
  let integer = WireJson.integer
  let signed = WireJson.signed
  let real = WireJson.number
  let boolean = WireJson.boolean
  let array = WireJson.array
  let strings values = array text values
  let optional = WireJson.optional
  let object = JsonValue.Object
  let render = Fidelity.Data.JSON.Json.serialize
  let pretty = Fidelity.Data.JSON.Json.serializePretty
  let timestamp = WireJson.timestamp

/// Pure functions for MCP adapter (formatting responses)
module McpAdapter =

  open McpJson

  let isSolutionFile (path: string) =
    path.EndsWith(".sln", System.StringComparison.Ordinal) || path.EndsWith(".slnx", System.StringComparison.Ordinal)

  let isProjectFile (path: string) =
    path.EndsWith(".fsproj", System.StringComparison.Ordinal)

  /// Directory-name segments never worth surfacing as "available projects":
  /// build output, VCS/tooling metadata, restored packages, and — the one that
  /// actually bit us while dogfooding — the per-agent worktrees under
  /// `.claude/worktrees`, each a full repo checkout that multiplies every
  /// `.fsproj`. A recursive scan that does not prune these returned 1,600+ paths
  /// and overflowed the calling agent's context.
  ///
  /// One ignore list for the whole product — defined next to `discoverProjects`
  /// in DashboardTypes, re-exported here so MCP and the dashboard can never
  /// disagree about what counts as a project.
  let projectNoiseSegments : Set<string> = Bozzetto.Server.DashboardTypes.projectNoiseSegments

  /// True if any path segment is build/worktree/tooling noise.
  let isNoiseProjectPath (path: string) : bool = Bozzetto.Server.DashboardTypes.isNoiseProjectPath path

  /// Bound the available-projects list for an agent's context window: drop noise
  /// paths, sort deterministically, and cap. Returns the shown paths plus the
  /// TOTAL real project count (post-noise-filter) so the caller can honestly say
  /// "showing X of N" instead of dumping everything. Pure and testable.
  let selectProjectsForDisplay (cap: int) (relativePaths: string seq) : string[] * int =
    let real =
      relativePaths
      |> Seq.filter (isNoiseProjectPath >> not)
      |> Seq.sort
      |> Seq.toArray
    (real |> Array.truncate (max 0 cap)), real.Length

  /// Parse the `projects` argument of create_session tolerantly. The MCP tool
  /// contract historically took a COMMA-SEPARATED string, but every hint the
  /// daemon emits (the NoSession message, the tool description) tells agents to
  /// pass a JSON array — `projects=["a.fsproj"]`. An agent that followed the
  /// docs got the whole JSON-array TEXT taken as one bogus project path
  /// (`.Split(',')` finds no comma), so no project loaded and the session still
  /// reached a vacuous Ready. This accepts BOTH forms — JSON array (the
  /// documented shape), comma-separated (the legacy shape), or a single path —
  /// and drops empties so `[]`/`""` mean "no EXPLICIT project requested" —
  /// NOT a guaranteed-empty REPL: the worker still auto-discovers whatever
  /// project/solution sits directly in the working directory when the
  /// caller names none (bozzetto-roast.md Finding #2; verified live). Pure
  /// and testable. Found by dogfooding: self-hosting a Bozzetto.Core session.
  let parseProjectsArg (raw: string) : string list =
    match System.String.IsNullOrWhiteSpace raw with
    | true -> []
    | false ->
      let trimmed = raw.Trim()
      let fromJsonArray () =
        match Fidelity.Data.JSON.Json.parse trimmed with
        | Ok (JsonValue.Array values) -> Some (values |> List.choose JsonValue.asString)
        | _ -> None
      let entries =
        match trimmed.StartsWith("[", System.StringComparison.Ordinal) with
        | true ->
          // Looks like the documented JSON-array form. Parse it; if it is
          // malformed, fall back to comma-split so a stray bracket never
          // swallows the paths whole.
          match fromJsonArray () with
          | Some xs -> xs
          | None -> trimmed.Split(',') |> Array.toList
        | false -> trimmed.Split(',') |> Array.toList
      entries
      |> List.map (fun s -> s.Trim())
      |> List.filter (System.String.IsNullOrWhiteSpace >> not)

  let formatAvailableProjects (workingDir: string) (projects: string array) (solutions: string array) (moreCount: int) =
    let projectList =
      match Array.isEmpty projects with
      | true -> "  (none found)"
      | false -> projects |> Array.map (sprintf "  - %s") |> String.concat "\n"
    let moreNote =
      match moreCount with
      | n when n > 0 -> sprintf "\n  …and %d more (pass working_directory to narrow the search)" n
      | _ -> ""
    let solutionList =
      match Array.isEmpty solutions with
      | true -> "  (none found)"
      | false -> solutions |> Array.map (sprintf "  - %s") |> String.concat "\n"
    // NOTE: no longer says "Start the daemon with: Bozzetto" — that hint can
    // never be true at the moment this text is printed: it is a tool call
    // the running daemon just served (bozzetto-roast.md Finding #5).
    sprintf "Available Projects/Solutions in %s:\n\n📦 F# Projects (.fsproj):\n%s%s\n\n📂 Solutions (.sln/.slnx):\n%s\n\n💡 Pass one .fsproj to create_project_session, one .sln/.slnx to create_solution_session, or choose create_bare_session explicitly.\n💡 Sessions can also be created from connected editors or the dashboard" workingDir projectList moreNote solutionList

  let formatStartupBanner (version: string) (mcpPort: int option) =
    match mcpPort with
    | Some port -> sprintf "Bozzetto v%s | MCP on port %d" version port
    | None -> sprintf "Bozzetto v%s" version

  let formatEvalResult (_workflow: WorkflowTypes.SessionWorkflow) (result: EvalResponse) : string =
    let stdout = 
      match result.Metadata.TryFind "stdout" with
      | Some (s: obj) -> s.ToString()
      | None -> ""
    
    let diagnosticsSection =
      match Array.isEmpty result.Diagnostics with
      | true -> ""
      | false ->
        let items =
          result.Diagnostics
          |> Array.map (fun d ->
            sprintf "  [%s] %s" (Features.Diagnostics.DiagnosticSeverity.label d.Severity) d.Message)
          |> String.concat "\n"
        sprintf "\nDiagnostics:\n%s" items

    let output =
      match result.EvaluationResult with
      | Ok output -> sprintf "Result: %s" output
      | Error ex ->
          match ex with
          | :? BozzettoErrorException as se ->
            // BozzettoErrorException is exactly how a BozzettoError travels
            // through this exception-typed channel (see its own doc
            // comment) — the algebra already knows what happened and what
            // to do about it, so use its own suggestedAction instead of
            // re-deriving one via fragile substring matching over free-form
            // text (ErrorMessages.categorize).
            let errText = BozzettoError.describe se.Error
            let suggestion = BozzettoError.suggestedAction se.Error
            sprintf "Error: %s\n%s%s" errText suggestion diagnosticsSection
          | _ ->
            let suggestion = ex.Message |> ErrorMessages.categorize |> ErrorMessages.getSuggestion
            sprintf "Error: %s\n%s%s" ex.Message suggestion diagnosticsSection
    
    match String.IsNullOrEmpty(stdout) with
    | true -> output
    | false -> sprintf "%s\n%s" stdout output

  let private diagnosticValue (diagnostic: Features.Diagnostics.Diagnostic) =
    object [
      "severity", text (Features.Diagnostics.DiagnosticSeverity.label diagnostic.Severity)
      "message", text diagnostic.Message
      "startLine", integer diagnostic.Range.StartLine
      "startColumn", integer diagnostic.Range.StartColumn
      "endLine", integer diagnostic.Range.EndLine
      "endColumn", integer diagnostic.Range.EndColumn ]

  let formatEvalResultJson (response: EvalResponse) : string =
    let stdout =
      response.Metadata.TryFind "stdout"
      |> Option.map (fun value -> value.ToString())
      |> Option.filter (String.IsNullOrEmpty >> not)
      |> Option.map (fun value -> "stdout", text value)
      |> Option.toList
    let result =
      match response.EvaluationResult with
      | Ok output -> ["success", boolean true; "result", text output]
      | Error error -> ["success", boolean false; "error", text error.Message]
    object (result @ stdout @ [
      "diagnostics", array diagnosticValue response.Diagnostics
      "code", text response.EvaluatedCode ])
    |> render

  /// Full warmup detail for LLM startup info — shows loaded assemblies,
  /// opened namespaces/modules, failures. Included in get_startup_info only.
  let formatWarmupDetailForLlm (ctx: SessionContext) =
    let w = ctx.Warmup
    let opened = WarmupContext.totalOpenedCount w
    let failed = WarmupContext.totalFailedCount w
    let asmCount = w.AssembliesLoaded.Length
    let lines = Collections.Generic.List<string>()

    lines.Add(
      sprintf "🔧 Warmup: %d assemblies, %d/%d namespaces opened, %dms"
        asmCount opened (opened + failed) (WarmupContext.totalDurationMs w))

    match asmCount > 0 with
    | true ->
      lines.Add(sprintf "  Assemblies (%d):" asmCount)
      for a in w.AssembliesLoaded do
        lines.Add(sprintf "    📦 %s (%d ns, %d modules)" a.Name a.NamespaceCount a.ModuleCount)
      lines.Add("  ⚠️ Do NOT '#r' any of the assemblies listed above — they are already loaded via the project graph.")
      lines.Add("     Using '#r' on them creates a second .NET load context causing TypeLoadException on ALL subsequent evals.")
      lines.Add("     Reference project types directly without '#r'. They are already in scope.")
    | false -> ()

    // Phase timing breakdown
    let t = w.PhaseTiming
    lines.Add(sprintf "  Timing: scan=%dms, asm=%dms, open=%dms, total=%dms"
      t.ScanSourceFilesMs t.ScanAssembliesMs t.OpenNamespacesMs t.TotalMs)

    match w.NamespacesOpened.Length > 0 with
    | true ->
      lines.Add(sprintf "  Opened (%d):" w.NamespacesOpened.Length)
      for b in w.NamespacesOpened do
        let kind = OpenableKind.label b.Kind
        lines.Add(sprintf "    open %s // %s (%.1fms)" b.Name kind b.DurationMs)
    | false -> ()

    match w.FailedOpens.Length > 0 with
    | true ->
      lines.Add(sprintf "  ⚠ Failed opens (%d):" w.FailedOpens.Length)
      for f in w.FailedOpens do
        let kind = OpenableKind.label f.Kind
        lines.Add(sprintf "    ✖ %s (%s) — %s" f.Name kind f.ErrorMessage)
        for d in f.Diagnostics do
          let loc =
            match d.FileName with
            | Some fn -> sprintf "%s:%d:%d" fn d.StartLine d.StartColumn
            | None -> "unknown"
          lines.Add(sprintf "      FS%04d %s — %s" d.ErrorNumber loc d.Message)
    | false -> ()

    let files = ctx.FileStatuses
    match files.Length > 0 with
    | true ->
      let loaded = files |> List.filter (fun f -> f.Readiness = Loaded) |> List.length
      lines.Add(sprintf "  Files (%d/%d loaded):" loaded files.Length)
      for f in files do
        lines.Add(sprintf "    %s %s" (FileReadiness.icon f.Readiness) f.Path)
    | false -> ()

    lines |> Seq.toList |> String.concat "\n"

  let splitStatements (code: string) : string list =
    let mutable i = 0
    let len = code.Length
    let statements = ResizeArray<string>()
    let current = Text.StringBuilder()
    let inline peek offset = match i + offset < len with | true -> code.[i + offset] | false -> '\000'
    while i < len do
      let c = code.[i]
      match c with
      | '"' when peek 1 = '"' && peek 2 = '"' ->
        current.Append("\"\"\"") |> ignore
        i <- i + 3
        let mutable inTriple = true
        while inTriple && i < len do
          match code.[i] = '"' && peek 1 = '"' && peek 2 = '"' with
          | true ->
            current.Append("\"\"\"") |> ignore
            i <- i + 3
            inTriple <- false
          | false ->
            current.Append(code.[i]) |> ignore
            i <- i + 1
      | '@' when peek 1 = '"' ->
        current.Append("@\"") |> ignore
        i <- i + 2
        let mutable inVerbatim = true
        while inVerbatim && i < len do
          match code.[i] = '"' && peek 1 = '"', code.[i] = '"' with
          | true, _ ->
            current.Append("\"\"") |> ignore
            i <- i + 2
          | _, true ->
            current.Append('"') |> ignore
            i <- i + 1
            inVerbatim <- false
          | _ ->
            current.Append(code.[i]) |> ignore
            i <- i + 1
      | '"' ->
        current.Append('"') |> ignore
        i <- i + 1
        let mutable inStr = true
        while inStr && i < len do
          match code.[i] = '\\', code.[i] = '"' with
          | true, _ ->
            current.Append(code.[i]) |> ignore
            i <- i + 1
            match i < len with
            | true ->
              current.Append(code.[i]) |> ignore
              i <- i + 1
            | false -> ()
          | _, true ->
            current.Append('"') |> ignore
            i <- i + 1
            inStr <- false
          | _ ->
            current.Append(code.[i]) |> ignore
            i <- i + 1
      | '/' when peek 1 = '/' ->
        while i < len && code.[i] <> '\n' do
          current.Append(code.[i]) |> ignore
          i <- i + 1
      | '(' when peek 1 = '*' ->
        current.Append("(*") |> ignore
        i <- i + 2
        let mutable depth = 1
        while depth > 0 && i < len do
          match code.[i] = '(' && peek 1 = '*', code.[i] = '*' && peek 1 = ')' with
          | true, _ ->
            current.Append("(*") |> ignore
            i <- i + 2
            depth <- depth + 1
          | _, true ->
            current.Append("*)") |> ignore
            i <- i + 2
            depth <- depth - 1
          | _ ->
            current.Append(code.[i]) |> ignore
            i <- i + 1
      | ';' when peek 1 = ';' ->
        let stmt = current.ToString().Trim()
        match stmt.Length > 0 with
        | true -> statements.Add(stmt + ";;")
        | false -> ()
        current.Clear() |> ignore
        i <- i + 2
      | _ ->
        current.Append(c) |> ignore
        i <- i + 1
    let trailing = current.ToString().Trim()
    match trailing.Length > 0 with
    | true -> statements.Add(trailing)
    | false -> ()
    statements |> Seq.toList

  let echoStatement (writer: TextWriter) (statement: string) =
    let code =
      match statement.EndsWith(";;", System.StringComparison.Ordinal) with
      | true -> statement.[.. statement.Length - 3]
      | false -> statement
    writer.WriteLine()
    writer.WriteLine(">")
    let lines = code.TrimEnd().Split([| '\n' |])
    for line in lines do
      writer.WriteLine(line.TrimEnd('\r'))

  let formatEvents (events: list<DateTime * string * string>) : string =
    events
    |> List.map (fun (timestamp, source, text) -> $"[{timestamp:O}] %s{source}: %s{text}")
    |> String.concat "\n"

  /// Kept for callers that need a string fragment; escaping belongs to Fidelity.Data.
  let escapeJson (value: string) =
    let encoded = JsonValue.String value |> render
    encoded.Substring(1, encoded.Length - 2)

  let formatEventsJson (events: list<DateTime * string * string>) : string =
    object [
      "events", array (fun (timestamp, source, value) -> object [
        "timestamp", text (timestamp.ToString("O"))
        "source", text source
        "text", text value ]) events
      "count", integer events.Length ]
    |> render

  let parseScriptFile (filePath: string) : Result<list<string>, exn> =
    try
      let content = File.ReadAllText(filePath)
      Ok(splitStatements content)
    with ex ->
      Error ex

  let formatStatus (sessionId: string) (eventCount: int) (state: SessionState) (evalStats: Affordances.EvalStats option) : string =
    let tools = Affordances.availableTools state |> String.concat ", "
    let base' = sprintf "Session: %s | Events: %d | State: %s" sessionId eventCount (SessionState.label state)
    let statsLine =
      match evalStats with
      | Some s when s.EvalCount > 0 ->
        let avg = Affordances.EvalStats.averageDuration s
        sprintf "\nEvals: %d | Avg: %dms | Min: %dms | Max: %dms"
          s.EvalCount (int avg.TotalMilliseconds) (int s.MinDuration.TotalMilliseconds) (int s.MaxDuration.TotalMilliseconds)
      | _ -> ""
    sprintf "%s%s\nAvailable: %s" base' statsLine tools

  let private evalStatsFields (stats: Affordances.EvalStats option) =
    match stats with
    | Some value when value.EvalCount > 0 ->
      [ "evalStats", object [
          "count", integer value.EvalCount
          "avgMs", integer (int (Affordances.EvalStats.averageDuration value).TotalMilliseconds)
          "minMs", integer (int value.MinDuration.TotalMilliseconds)
          "maxMs", integer (int value.MaxDuration.TotalMilliseconds) ] ]
    | _ -> []

  let private statusFields sessionId eventCount state = [
    "sessionId", text sessionId
    "eventCount", integer eventCount
    "state", text (SessionState.label state)
    "tools", strings (Affordances.availableTools state) ]

  let formatStatusJson (sessionId: string) (eventCount: int) (state: SessionState) (evalStats: Affordances.EvalStats option) : string =
    object (statusFields sessionId eventCount state @ evalStatsFields evalStats) |> render

  let formatCompletions (items: Features.AutoCompletion.CompletionItem list) : string =
    match items with
    | [] -> "No completions found."
    | items ->
      items
      |> List.map (fun item -> sprintf "%s (%s)" item.DisplayText (Features.AutoCompletion.CompletionKind.label item.Kind))
      |> String.concat "\n"

  let formatCompletionsJson (items: Features.AutoCompletion.CompletionItem list) : string =
    let values = items |> List.map (fun item ->
      let detail =
        match item.GetDescription with
        | Some getDescription ->
          try
            let description = getDescription () |> Array.map (fun tag -> tag.Text) |> String.concat ""
            if description.Length = 0 then [] else ["detail", text description]
          with
          | :? System.OperationCanceledException -> reraise()
          | _ -> []
        | None -> []
      object ([
        "label", text item.DisplayText
        "kind", text (Features.AutoCompletion.CompletionKind.label item.Kind)
        "insertText", text item.ReplacementText ] @ detail))
    object ["completions", JsonValue.Array values; "count", integer items.Length] |> render

  let formatExplorationResult (qualifiedName: string) (items: Features.AutoCompletion.CompletionItem list) : string =
    match items with
    | [] -> sprintf "No items found in '%s'." qualifiedName
    | items ->
      let grouped =
        items
        |> List.groupBy (fun item -> Features.AutoCompletion.CompletionKind.label item.Kind)
        |> List.sortBy fst
      let sections =
        grouped
        |> List.map (fun (kind, members) ->
          let memberLines =
            members
            |> List.map (fun m -> sprintf "  %s" m.DisplayText)
            |> String.concat "\n"
          sprintf "### %s\n%s" kind memberLines)
        |> String.concat "\n\n"
      sprintf "## %s\n\n%s" qualifiedName sections

  let formatExplorationResultJson (qualifiedName: string) (items: Features.AutoCompletion.CompletionItem list) : string =
    let groups =
      items
      |> List.groupBy (fun item -> Features.AutoCompletion.CompletionKind.label item.Kind)
      |> List.sortBy fst
      |> List.map (fun (kind, members) -> object [
        "kind", text kind
        "members", strings (members |> List.map (fun memberItem -> memberItem.DisplayText))
        "count", integer members.Length ])
    object ["name", text qualifiedName; "groups", JsonValue.Array groups; "totalCount", integer items.Length]
    |> render

  let formatStartupInfo (config: AppState.StartupConfig) : string =
    // Filter out verbose -r: assembly references from args display
    let importantArgs = 
      config.CommandLineArgs 
      |> Array.filter (fun arg -> not (arg.StartsWith("-r:", System.StringComparison.Ordinal) || arg.StartsWith("--reference:", System.StringComparison.Ordinal)))
    let argsStr = 
      match importantArgs.Length = 0 with
      | true -> "(none)"
      | false -> String.concat " " importantArgs
    
    let projectsStr = 
      match config.LoadedProjects.IsEmpty with
      | true -> "None"
      | false -> String.concat ", " config.LoadedProjects
    let aspireStr = match config.AspireDetected with | true -> "Yes ✓" | false -> "No"
    let timestamp = config.StartupTimestamp.ToString("yyyy-MM-dd HH:mm:ss")
    
    // Count assembly references for info
    let assemblyCount = 
      config.CommandLineArgs 
      |> Array.filter (fun arg -> arg.StartsWith("-r:", System.StringComparison.Ordinal) || arg.StartsWith("--reference:", System.StringComparison.Ordinal))
      |> Array.length
    
    let profileStr =
      match config.StartupProfileLoaded with
      | Some path -> sprintf "Loaded (%s)" path
      | None -> "None"

    $"""Bozzetto Startup Information:

Args: %s{argsStr}
Working Directory: %s{config.WorkingDirectory}
Loaded Projects: %s{projectsStr}
Assemblies Loaded: %d{assemblyCount}
Aspire Detected: %s{aspireStr}
Startup Profile: %s{profileStr}
Started: %s{timestamp} UTC"""

  let formatStartupInfoJson (config: AppState.StartupConfig) : string =
    object [
      "commandLineArgs", strings config.CommandLineArgs
      "loadedProjects", strings config.LoadedProjects
      "workingDirectory", text config.WorkingDirectory
      "aspireDetected", boolean config.AspireDetected
      "startupProfileLoaded", optional text config.StartupProfileLoaded
      "startupTimestamp", text (config.StartupTimestamp.ToString("O")) ]
    |> pretty

  let formatDiagnosticsResult (diagnostics: Features.Diagnostics.Diagnostic array) : string =
    match Array.isEmpty diagnostics with
    | true -> "No issues found."
    | false ->
      diagnostics
      |> Array.map (fun d ->
        let sev = Features.Diagnostics.DiagnosticSeverity.label d.Severity
        sprintf "(%d,%d): [%s] %s" d.Range.StartLine d.Range.StartColumn sev d.Message)
      |> String.concat "\n"

  let formatDiagnosticsResultJson (diagnostics: Features.Diagnostics.Diagnostic array) : string =
    object ["diagnostics", array diagnosticValue diagnostics; "count", integer diagnostics.Length] |> render

  let formatDiagnosticsStoreAsJson (store: Features.DiagnosticsStore.T) : string =
    Features.DiagnosticsStore.all store
    |> array (fun (codeHash, diagnostics) -> object [
      "codeHash", text codeHash
      "diagnostics", array (fun (diagnostic: Features.Diagnostics.Diagnostic) -> object [
        "message", text diagnostic.Message
        "severity", text (Features.Diagnostics.DiagnosticSeverity.label diagnostic.Severity)
        "range", object [
          "startLine", integer diagnostic.Range.StartLine
          "startColumn", integer diagnostic.Range.StartColumn
          "endLine", integer diagnostic.Range.EndLine
          "endColumn", integer diagnostic.Range.EndColumn ] ]) diagnostics ])
    |> render

  let formatEnhancedStatus(sessionId: string) (eventCount: int) (state: SessionState) (evalStats: Affordances.EvalStats option) (startupConfig: AppState.StartupConfig option) : string =
    let projectsStr = 
      match startupConfig with
      | None -> "Unknown"
      | Some config -> 
          match config.LoadedProjects.IsEmpty with
          | true -> "None"
          | false -> String.concat ", " (config.LoadedProjects |> List.map Path.GetFileName)
    
    let startupSection =
      match startupConfig with
      | None -> ""
      | Some config ->
          let aspire = match config.AspireDetected with | true -> "✅" | false -> "❌"
          sprintf """

📋 Startup Information:
- Working Directory: %s
- Aspire: %s""" config.WorkingDirectory aspire

    let statsSection =
      match evalStats with
      | Some s when s.EvalCount > 0 ->
        let avg = Affordances.EvalStats.averageDuration s
        sprintf "\nEvals: %d | Avg: %dms | Min: %dms | Max: %dms"
          s.EvalCount (int avg.TotalMilliseconds) (int s.MinDuration.TotalMilliseconds) (int s.MaxDuration.TotalMilliseconds)
      | _ -> ""

    let tools = Affordances.availableTools state |> String.concat ", "
    sprintf """Session: %s | Events: %d | State: %s | Projects: %s
Available: %s%s%s""" sessionId eventCount (SessionState.label state) projectsStr tools statsSection startupSection

  let formatEnhancedStatusJson
    (sessionId: string)
    (eventCount: int)
    (state: SessionState)
    (evalStats: Affordances.EvalStats option)
    (startupConfig: AppState.StartupConfig option)
    : string =
    let projects = startupConfig |> Option.map (fun config -> config.LoadedProjects |> List.map Path.GetFileName) |> Option.defaultValue []
    let startup = startupConfig |> Option.map (fun config ->
      "startup", object [
        "workingDirectory", text config.WorkingDirectory
        "aspireDetected", boolean config.AspireDetected
        "workflow", text (sprintf "%A" config.Workflow)
        "workflowLabel", text (WorkflowTypes.SessionWorkflow.label config.Workflow) ]) |> Option.toList
    object (statusFields sessionId eventCount state @ ["projects", strings projects] @ evalStatsFields evalStats @ startup)
    |> render

  /// What the worker ACTUALLY resolved and loaded, as opposed to what a
  /// session was declared with — the two can legitimately differ, most
  /// visibly with `projects=[]`, which still auto-discovers whatever
  /// solution/project sits directly in the working directory
  /// (bozzetto-roast.md Finding #2/#3). `formatProxyStatus`'s `Projects:`
  /// field reports the DECLARED list; this reports what `ProjectRoles` says
  /// was actually loaded, so "my project silently didn't load" — or "an
  /// unrequested project silently DID load" — is visible at the call site
  /// instead of requiring a separate investigation.
  let formatLoadedProjectsLine (roles: Bozzetto.ProjectLoading.ClassifiedProject list) : string =
    match roles with
    | [] -> "(none resolved yet — session may still be warming up, or nothing was found to load)"
    | rs -> rs |> List.map (fun p -> Path.GetFileName p.Path) |> String.concat ", "

  /// Format status from a worker proxy's StatusSnapshot + SessionInfo.
  let formatProxyStatus
    (sessionId: string)
    (eventCount: int)
    (snapshot: WorkerProtocol.WorkerStatusSnapshot)
    (info: WorkerProtocol.SessionInfo)
    (mcpPort: int)
    : string =
    let state = WorkerProtocol.SessionStatus.toSessionState snapshot.Status
    let projectsStr =
      match info.Projects.IsEmpty with
      | true -> "None"
      | false -> String.concat ", " (info.Projects |> List.map Path.GetFileName)
    let loadedStr = formatLoadedProjectsLine info.ProjectRoles
    let statsSection =
      match snapshot.EvalCount > 0 with
      | true ->
        sprintf "\nEvals: %d | Avg: %dms | Min: %dms | Max: %dms"
          snapshot.EvalCount snapshot.AvgDurationMs snapshot.MinDurationMs snapshot.MaxDurationMs
      | false -> ""
    let tools = Affordances.availableTools state |> String.concat ", "
    let modeLabel = WorkflowTypes.SessionWorkflow.label info.Workflow
    sprintf """Session: %s | Mode: %s | Events: %d | State: %s | Projects: %s | Loaded: %s
Available: %s%s

📋 Startup Information:
- Working Directory: %s
- MCP Port: %d""" sessionId modeLabel eventCount (SessionState.label state) projectsStr loadedStr tools statsSection info.WorkingDirectory mcpPort

  let private workerDiagnosticValue (diagnostic: WorkerProtocol.WorkerDiagnostic) =
    object [
      "severity", text (Features.Diagnostics.DiagnosticSeverity.label diagnostic.Severity)
      "message", text diagnostic.Message
      "startLine", integer diagnostic.StartLine
      "startColumn", integer diagnostic.StartColumn
      "endLine", integer diagnostic.EndLine
      "endColumn", integer diagnostic.EndColumn ]

  let formatWorkerEvalResultJson (response: WorkerProtocol.WorkerResponse) : string =
    let result, diagnostics =
      match response with
      | WorkerProtocol.WorkerResponse.EvalResult(_, result, diagnostics, _) ->
        let fields =
          match result with
          | Ok output -> ["success", boolean true; "result", text (stripAnsi output)]
          | Error error -> ["success", boolean false; "error", text (BozzettoError.describeForAgent error)]
        fields, diagnostics
      | WorkerProtocol.WorkerResponse.WorkerError error ->
        ["success", boolean false; "error", text (BozzettoError.describeForAgent error)], []
      | other ->
        ["success", boolean false; "error", text (sprintf "Unexpected response: %A" other)], []
    object (result @ ["diagnostics", array workerDiagnosticValue diagnostics]) |> render

  let formatEvalStructuredSuccess (result: string) (diagnostics: WorkerProtocol.WorkerDiagnostic list) : string =
    object [
      "success", boolean true
      "result", text result
      "diagnostics", array workerDiagnosticValue diagnostics ]
    |> render

  let formatEvalStructuredError (error: BozzettoError) : string =
    BozzettoError.toJsonValue error |> render

  /// Outbound size cap for MCP tool text results (roast-7 §13/§16 item 13).
  /// The only enforced limit before this was INBOUND — 4 MiB on the request
  /// body (McpServer.maxRequestBodyBytes). A verbose eval result had no
  /// ceiling before landing in an agent's context window. 256 KiB is
  /// generous for a legitimate eval reply (large printed tables, full
  /// stack traces) while bounding runaway output (an accidental print loop,
  /// a megabyte-sized dump) before it eats the whole context budget.
  [<Literal>]
  let MaxOutboundResultBytes = 262_144

  /// Truncate `text` to at most `maxBytes` UTF-8 bytes, appending a marker
  /// naming how many bytes were cut. Never silently drops data without
  /// saying so (a bare cut would look like Bozzetto is just producing short
  /// output). A partial multi-byte sequence at the cut point decodes to the
  /// Unicode replacement character rather than throwing — acceptable at a
  /// boundary that only exists to protect an agent's context window.
  let truncateForOutbound (maxBytes: int) (text: string) : string =
    let totalBytes = System.Text.Encoding.UTF8.GetByteCount(text)
    match totalBytes <= maxBytes with
    | true -> text
    | false ->
      let marker = sprintf "…[truncated %d bytes]" (totalBytes - maxBytes)
      let markerBytes = System.Text.Encoding.UTF8.GetByteCount(marker)
      let budget = max 0 (maxBytes - markerBytes)
      let allBytes = System.Text.Encoding.UTF8.GetBytes(text)
      let cut = min budget allBytes.Length
      let kept = System.Text.Encoding.UTF8.GetString(allBytes, 0, cut)
      kept + marker

  /// A compact, single-line preview of `text` for event-log summaries:
  /// newlines collapsed to spaces, truncated to `maxChars` with an
  /// ellipsis. Distinct from `truncateForOutbound` (byte-budgeted, for a
  /// whole tool reply) — this is char-budgeted, for one line of many in a
  /// list.
  let previewLine (maxChars: int) (text: string) : string =
    let collapsed = text.Replace("\r\n", " ").Replace("\n", " ").Trim()
    match collapsed.Length <= maxChars with
    | true -> collapsed
    | false -> collapsed.Substring(0, maxChars) + "…"

