namespace Bozzetto.Audit

open System

// Pure Zone A deterministic 6-part Markdown Blind Audit Packet renderer
// (zero implementer narrative, zero I/O, zero mutable state).
[<AutoOpen>]
module AuditPacketRender =

  let private escapeCell (s: string) : string =
    s.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ")

  let private fmtOptFloat (unitSuffix: string) (v: float option) : string =
    match v with Some x -> sprintf "%.2f%s" x unitSuffix | None -> "n/a"

  let private fmtOptInt (v: int option) : string =
    match v with Some x -> string x | None -> "n/a"

  let private fmtOptBytesMb (v: int64 option) : string =
    match v with Some b -> sprintf "%.2f MiB (%d)" (float b / 1_048_576.0) b | None -> "n/a"

  let private fmtOptBytes (v: int64 option) : string =
    match v with Some b -> string b | None -> "n/a"

  let private maxConsecutiveBackticks (content: string) : int =
    let len = content.Length
    let rec loop i cur best =
      if i >= len then max cur best
      elif content[i] = '`' then loop (i + 1) (cur + 1) best
      else loop (i + 1) 0 (max cur best)
    loop 0 0 0

  let private fenceFor (lang: string) (content: string) : string =
    let fenceLen = max 3 (maxConsecutiveBackticks content + 1)
    let fence = String('`', fenceLen)
    if String.IsNullOrEmpty content then
      sprintf "%s%s\n%s" fence lang fence
    elif content.EndsWith("\n", StringComparison.Ordinal) then
      sprintf "%s%s\n%s%s" fence lang content fence
    else
      sprintf "%s%s\n%s\n%s" fence lang content fence

  let private describeAuthorityProvenance =
    function
    | AuthorityProvenance.LiveFetch endpoint -> sprintf "live fetch (`%s`)" endpoint
    | AuthorityProvenance.LocalFile path -> sprintf "local file (`--issue-file` / `--wiki-file`: `%s`)" path

  let private describeRetrievalProvenance =
    function
    | RetrievalProvenance.LiveAwait(endpoint, repoOpt, shaOpt) ->
      sprintf "live fetch (`POST %s`, expectedRepo=`%s`, expectedSha=`%s`)"
        endpoint
        (repoOpt |> Option.defaultValue "none")
        (shaOpt |> Option.defaultValue "none")
    | RetrievalProvenance.SnapshotFile(path, repoOpt, shaOpt) ->
      match repoOpt, shaOpt with
      | None, None -> sprintf "local file (`--retrieval-snapshot %s`)" path
      | _ ->
        sprintf "local file (`--retrieval-snapshot %s`) [expectedRepo=`%s`, expectedSha=`%s`]"
          path
          (repoOpt |> Option.defaultValue "none")
          (shaOpt |> Option.defaultValue "none")

  let private vetoGateRow (gateName: string) (matching: MechanicalVeto list) (cleanStatus: string) (cleanDetail: string) : string =
    match matching with
    | [] -> sprintf "| %s | %s | %s |" gateName cleanStatus (escapeCell cleanDetail)
    | xs ->
      let detail = xs |> List.map MechanicalVeto.describe |> String.concat "; "
      sprintf "| %s | VETO | %s |" gateName (escapeCell detail)

  let private renderVetoSummaryRows (input: BlindAuditPacketInput) (vetoes: MechanicalVeto list) : string list =
    let byPred pred = vetoes |> List.filter pred
    let hasCompleteCand =
      match input.CanaryCandidate with
      | Ok(Some c) -> List.isEmpty (missingCanaryMetrics c)
      | _ -> false
    let candStatus = if hasCompleteCand then "PASS" else "MISSING"
    let retrievalCleanStatus, retrievalCleanDetail =
      match input.Retrieval with
      | Ok(Some r) -> "PASS", sprintf "snapshot_id=%s fresh=%b" r.SnapshotId r.Fresh
      | Ok None when input.OutsideIndex -> "EXEMPT (--outside-index)", "Repository explicitly marked outside retrieval index"
      | _ -> "MISSING", "Missing retrieval snapshot"
    [ vetoGateRow "6-Part Packet Completeness"
        (byPred (function MechanicalVeto.IncompletePacket _ -> true | _ -> false))
        "COMPLETE" "All 6 mandatory evidence sections present"
      vetoGateRow "Analyzer Diagnostics (any `level=error`)"
        (byPred (function MechanicalVeto.RedAnalyzerDiagnostic _ -> true | _ -> false))
        (match input.Analyzer with Ok(Some _) -> "PASS" | _ -> "MISSING")
        (match input.Analyzer with Ok(Some a) -> sprintf "0 red diagnostics (errors=%d, warnings=%d, notes=%d)" a.ErrorCount a.WarningCount a.NoteCount | _ -> "No SARIF provided")
      vetoGateRow "`AXD000` Context Hash"
        (byPred (function MechanicalVeto.MissingAxd000Hash _ -> true | _ -> false))
        (match input.Analyzer with Ok(Some a) when a.Axd000Valid -> "PASS" | _ -> "MISSING")
        (match input.Analyzer with Ok(Some a) when a.Axd000Valid -> sprintf "%d verified SHA-256 digest(s)" a.Axd000Digests.Length | _ -> "No AXD000 digest")
      vetoGateRow "Anti-Pattern Upward Delta (`src/`)"
        (byPred (function MechanicalVeto.AntiPatternIncrease _ -> true | _ -> false))
        (match input.CodeDelta with Ok _ -> "PASS" | _ -> "MISSING")
        "Zero upward delta in CWT/Concurrent*/Reflection/BCL_Mutable/let_mutable/NativeService.normalize"
      vetoGateRow "Projection Call Counts (`<= 1`)"
        (byPred (function MechanicalVeto.ProjectionCountExceeded _ -> true | _ -> false))
        candStatus "No projection exceeded 1 call"
      vetoGateRow "`Spatial*` at 0 Kernel Modules"
        (byPred (function MechanicalVeto.SpatialExecutedWithZeroKernelModules _ -> true | _ -> false))
        candStatus "Short-circuited cleanly or kernel modules > 0"
      vetoGateRow "Scaling Slope (`alloc 4x/1x <= 6.0`)"
        (byPred (function MechanicalVeto.ScalingSlopeExceeded _ -> true | _ -> false))
        candStatus
        (match input.CanaryCandidate |> Result.toOption |> Option.flatten |> Option.bind computeOverallScalingSlope with
         | Some s -> sprintf "Overall slope %.2f" s
         | None -> "No scaling slope metric reported")
      vetoGateRow "`cvc5` Spawn Cap"
        (byPred (function MechanicalVeto.Cvc5SpawnSpike _ -> true | _ -> false))
        candStatus
        (match input.CanaryCandidate with Ok(Some c) -> sprintf "cvc5=%s" (fmtOptInt c.Cvc5Spawns) | _ -> "Missing canary")
      vetoGateRow "Canary Phase Allocation (`<= +15%` vs Base)"
        (byPred (function MechanicalVeto.CanaryPhaseAllocRegression _ -> true | _ -> false))
        candStatus "No phase heap allocation grew > +15% vs base"
      vetoGateRow "Canary Wall / RSS / Exit"
        (byPred (function
          | MechanicalVeto.CanaryWallBreach _
          | MechanicalVeto.CanaryPhaseWallBreach _
          | MechanicalVeto.CanaryRssBreach _
          | MechanicalVeto.CanaryExitBreach _
          | MechanicalVeto.CanaryWatchdogBreach _ -> true
          | _ -> false))
        candStatus "Within wall, phase, RSS, watchdog, and exit=0 limits"
      vetoGateRow "Test Project Compilation"
        (byPred (function MechanicalVeto.FailedTestCompile _ -> true | _ -> false))
        (match input.TestReceipt with Ok(Some _) -> "PASS" | _ -> "MISSING")
        (match input.TestReceipt with Ok(Some t) -> t.Summary | _ -> "Missing --test-receipt")
      vetoGateRow "Pinned `lab_retrieval` Snapshot (`fresh=true`)"
        (byPred (function MechanicalVeto.StaleOrMissingRetrievalSnapshot _ -> true | _ -> false))
        retrievalCleanStatus retrievalCleanDetail ]

  let private renderPart1 (issueRes: Result<IssueAuthority, SourceLoadError>) : string list =
    match issueRes with
    | Error err ->
      [ "## Part 1 — Verbatim Authority"
        ""
        sprintf "BLOCKED: Missing Part 1 (Verbatim Authority): %s" (SourceLoadError.describe err) ]
    | Ok issue ->
      let labelsText = if List.isEmpty issue.Labels then "none" else String.concat ", " issue.Labels
      let milestoneText = issue.Milestone |> Option.defaultValue "none"
      let urlsSection =
        if List.isEmpty issue.ReferencedUrls then [ "- Referenced Ledger / Spec / Sibling URLs: none" ]
        else
          [ "- Referenced Ledger / Spec / Sibling URLs:" ]
          @ (issue.ReferencedUrls |> List.map (fun u -> sprintf "  - %s" u))
      let commentsSection =
        if List.isEmpty issue.Comments then []
        else
          [ ""; "### Verbatim Issue Comments" ]
          @ (issue.Comments
             |> List.collect (fun c ->
               [ ""; sprintf "#### Comment by `%s` (%s)" c.Author c.HtmlUrl; ""
                 fenceFor "markdown" c.Body ]))
      let wikiSection =
        if List.isEmpty issue.WikiRulings && List.isEmpty issue.WikiRulingFailures then []
        else
          let loaded =
            issue.WikiRulings
            |> List.collect (fun r ->
              [ ""
                sprintf "#### Wiki Ruling: %s (%s)" r.Title r.Url
                sprintf "- Provenance: %s" (describeAuthorityProvenance r.Provenance)
                ""
                fenceFor "markdown" r.Content ])
          let failed =
            issue.WikiRulingFailures
            |> List.collect (fun f ->
              [ ""
                sprintf "#### Wiki Ruling Fetch Failed: %s" f.Url
                sprintf "BLOCKED: %s" (SourceLoadError.describe f.Error) ])
          [ ""; "### Verbatim Referenced Wiki Rulings" ] @ loaded @ failed
      [ "## Part 1 — Verbatim Authority"
        ""
        sprintf "- Provenance: %s" (describeAuthorityProvenance issue.Provenance)
        sprintf "- Issue: #%d — %s" issue.Number issue.Title
        sprintf "- URL: %s" issue.HtmlUrl
        sprintf "- State: `%s` | Milestone: `%s` | Labels: `%s`" issue.State milestoneText labelsText ]
      @ urlsSection
      @ [ ""; "### Verbatim Issue Body"; ""; fenceFor "markdown" issue.Body ]
      @ commentsSection
      @ wikiSection

  let private renderPart2 (deltaRes: Result<CodeDeltaReport, SourceLoadError>) : string list =
    match deltaRes with
    | Error err ->
      [ "## Part 2 — Exact Code Delta"
        ""
        sprintf "BLOCKED: Missing Part 2 (Exact Code Delta): %s" (SourceLoadError.describe err) ]
    | Ok delta ->
      let quarantineNote =
        if delta.IsQuarantinedBranch then
          [ sprintf "> **Quarantined Branch Notice:** Candidate branch `%s` is an intentionally quarantined candidate tree outside the `main` retrieval index."
              (delta.CandidateBranch |> Option.defaultValue delta.CandidateRef)
            "" ]
        else []
      let unaccountedNote =
        if List.isEmpty delta.UnaccountedSrcFiles then []
        else
          [ ""; "### Unreadable / Unaccounted Diff Files"; "" ]
          @ (delta.UnaccountedSrcFiles |> List.map (fun (p, err) -> sprintf "- **BLOCKED:** `%s` — %s" p err))
      let fileTable =
        if List.isEmpty delta.Files then [ "No modified files in diff." ]
        else
          [ "| File | Before Lines | After Lines | Δ Lines | +Added | -Removed | Prod `src/` | Cohesion |"
            "| :--- | ---: | ---: | ---: | ---: | ---: | :---: | :--- |" ]
          @ (delta.Files
             |> List.map (fun f ->
               let bStr = if f.IsBinary then "bin" else f.BeforeLines |> Option.map string |> Option.defaultValue "new"
               let aStr = if f.IsBinary then "bin" else f.AfterLines |> Option.map string |> Option.defaultValue "del"
               let dStr = if f.IsBinary then "bin" else sprintf "%+d" f.DeltaLines
               let cohesion = if f.ExceedsCohesionLimit then sprintf "OVER (>%d)" CohesionLineLimit else "ok"
               sprintf "| `%s` | %s | %s | %s | %d | %d | %b | %s |"
                 (escapeCell f.Path) bStr aStr dStr f.AddedLines f.RemovedLines f.IsProductionSrc cohesion))
      let rawNumstatText =
        if String.IsNullOrWhiteSpace delta.RawNumstat then "(empty numstat)" else delta.RawNumstat
      [ "## Part 2 — Exact Code Delta"; "" ]
      @ quarantineNote
      @ [ sprintf "- Range: `%s..%s` (resolved candidate `%s`)" delta.BaseSha delta.CandidateRef delta.CandidateSha
          sprintf "- `Δmax_file_lines`: `%+d` (max before: `%s`, max after: `%s`, max single-file Δ: `%+d`)"
            delta.AntiPatterns.DeltaMaxFileLines
            (fmtOptInt delta.AntiPatterns.MaxFileLinesBefore)
            (fmtOptInt delta.AntiPatterns.MaxFileLinesAfter)
            delta.AntiPatterns.MaxSingleFileDelta
          ""
          "### Verbatim `git diff --numstat`"
          ""
          fenceFor "text" rawNumstatText
          ""
          "### Per-File Line-Count Deltas"
          "" ]
      @ fileTable
      @ unaccountedNote
      @ [ ""
          "### Raw Unified Diff"
          ""
          fenceFor "diff" (if String.IsNullOrWhiteSpace delta.RawDiff then "(empty diff)" else delta.RawDiff) ]

  let private renderPart3
      (analyzerRes: Result<AnalyzerReceipt option, SourceLoadError>)
      (testRes: Result<TestReceipt option, SourceLoadError>)
      : string list =
    let analyzerSection =
      match analyzerRes with
      | Error err -> [ sprintf "BLOCKED: Missing Part 3 (Independent Analyzer Receipt): %s" (SourceLoadError.describe err) ]
      | Ok None -> [ "BLOCKED: Missing Part 3 (Independent Analyzer Receipt): SARIF receipt not provided." ]
      | Ok(Some a) ->
        let toolsText = if List.isEmpty a.Tools then "unspecified" else String.concat ", " a.Tools
        let axdRows =
          if List.isEmpty a.Axd000Digests then [ "- No valid `AXD000` SHA-256 digests found." ]
          else
            [ "| File / Source | `AXD000` SHA-256 |"; "| :--- | :--- |" ]
            @ (a.Axd000Digests |> List.map (fun d -> sprintf "| `%s` | `%s` |" (escapeCell d.Path) d.Sha256))
        let redRows =
          if List.isEmpty a.RedTargetDiagnostics then [ "- Red Analyzer Diagnostics (`level=error`): none." ]
          else
            [ "| RuleId | Level | File | Line | Message |"
              "| :--- | :--- | :--- | ---: | :--- |" ]
            @ (a.RedTargetDiagnostics |> List.map (fun d ->
              sprintf "| `%s` | `%s` | `%s` | %s | %s |"
                (escapeCell d.RuleId) (escapeCell d.Level) (escapeCell d.File) (fmtOptInt d.Line) (escapeCell d.Message)))
        let rawSarifBlock =
          if String.IsNullOrWhiteSpace a.RawSarif then []
          else
            [ ""; "### Raw Analyzer SARIF (Verbatim)"; ""; fenceFor "json" a.RawSarif ]
        [ sprintf "- Tools: %s" toolsText
          sprintf "- Totals: `%d` diagnostic(s) (`error=%d`, `warning=%d`, `note=%d`)" a.TotalDiagnostics a.ErrorCount a.WarningCount a.NoteCount
          sprintf "- `AXD000` Status: `%s` (invalidEntries=%b)" (if a.Axd000Valid then "VALID" else "MISSING_OR_INVALID") a.HasInvalidAxd000Entry
          ""
          "### `AXD000` Provenance Digests"
          "" ]
        @ axdRows
        @ [ ""; "### Red Analyzer Diagnostics"; "" ]
        @ redRows
        @ rawSarifBlock
    let testSection =
      match testRes with
      | Error err -> [ ""; "### Test Compilation Receipt"; ""; sprintf "BLOCKED: Invalid Test Compilation Receipt: %s" (SourceLoadError.describe err) ]
      | Ok None -> [ ""; "### Test Compilation Receipt"; ""; "BLOCKED: Missing Part 3 (Test Compilation Receipt): --test-receipt is required." ]
      | Ok(Some t) ->
        [ ""; "### Test Compilation Receipt"; ""
          sprintf "- Compile Succeeded: `%b` | Exit Code: `%s` | Verdict: `%s`"
            t.CompileSucceeded (fmtOptInt t.ExitCode) (t.Verdict |> Option.defaultValue "n/a")
          sprintf "- Summary: %s" t.Summary ]
    [ "## Part 3 — Independent Analyzer Receipt (SARIF + `AXD000`)"; "" ] @ analyzerSection @ testSection

  let private renderPart4 (deltaRes: Result<CodeDeltaReport, SourceLoadError>) : string list =
    match deltaRes with
    | Error _ ->
      [ "## Part 4 — Automated Anti-Pattern Delta (`src/`)"
        ""
        "BLOCKED: Missing Part 4 (requires Part 2 code delta)." ]
    | Ok delta ->
      let ap = delta.AntiPatterns
      let row label (c: CounterDelta) (pick: FileAntiPatternHit -> CounterDelta) =
        let fileUp = ap.PerFileHits |> List.sumBy (fun h -> max 0 (pick h).Delta)
        let threshold = "`<= 0` (VETO if any file `> 0`)"
        let status = if max c.Delta fileUp > 0 then "VETO" else "ok"
        sprintf "| %s | +%d | -%d | %+d | %s | %s |" label c.Added c.Removed c.Delta threshold status
      let perFileSection =
        if List.isEmpty ap.PerFileHits then []
        else
          [ ""; "### Per-File `src/` Anti-Pattern Hits"; ""
            "| File | `ΔCWT` | `ΔConcurrent*` | `ΔSys.Refl` | `ΔFS.Refl` | `ΔBCL_Mut` | `Δlet_mut` | `ΔNS.norm` |"
            "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |" ]
          @ (ap.PerFileHits |> List.map (fun h ->
            sprintf "| `%s` | %+d | %+d | %+d | %+d | %+d | %+d | %+d |"
              (escapeCell h.Path)
              h.ConditionalWeakTable.Delta
              h.Concurrent.Delta
              h.SystemReflection.Delta
              h.FSharpReflection.Delta
              h.BclMutable.Delta
              h.LetMutable.Delta
              h.NativeServiceNormalize.Delta))
      let maxLinesStatus =
        if not (List.isEmpty ap.OverBudgetFileGrowth) then "VETO"
        elif ap.DeltaMaxFileLines > 0 then "warn"
        else "ok"
      [ "## Part 4 — Automated Anti-Pattern Delta (`src/`)"
        ""
        sprintf "- Scanned production `src/` F# files: `%d`" ap.ScannedSrcFiles.Length
        ""
        "| Metric | Added | Removed | Net Δ | Veto Threshold | Status |"
        "| :--- | ---: | ---: | ---: | :--- | :--- |"
        row "`ΔConditionalWeakTable`" ap.ConditionalWeakTable _.ConditionalWeakTable
        row "`ΔConcurrent*`" ap.Concurrent _.Concurrent
        row "`ΔSystem.Reflection`" ap.SystemReflection _.SystemReflection
        row "`ΔMicrosoft.FSharp.Reflection`" ap.FSharpReflection _.FSharpReflection
        row "`ΔBCL_Mutable`" ap.BclMutable _.BclMutable
        row "`Δlet_mutable`" ap.LetMutable _.LetMutable
        row "`ΔNativeService_normalize`" ap.NativeServiceNormalize _.NativeServiceNormalize
        sprintf "| `Δmax_file_lines` | - | - | %+d | `<= 750` lines in `src/` (VETO on over-budget growth) | %s |"
          ap.DeltaMaxFileLines
          maxLinesStatus ]
      @ perFileSection

  let private renderPart5 (input: BlindAuditPacketInput) (cmpOpt: CanaryComparison option) : string list =
    match cmpOpt with
    | None ->
      let bMsg =
        match input.CanaryBase with
        | Ok None -> "base canary missing"
        | Error e -> SourceLoadError.describe e
        | Ok(Some _) -> "ok"
      let cMsg =
        match input.CanaryCandidate with
        | Ok None -> "candidate canary missing"
        | Error e -> SourceLoadError.describe e
        | Ok(Some _) -> "ok"
      [ "## Part 5 — Live `.fidproj` Canary Receipt (Side-by-Side vs. `<base>`)"
        ""
        sprintf "BLOCKED: Missing Part 5 (base=%s; candidate=%s)" bMsg cMsg ]
    | Some cmp ->
      let b = cmp.Base
      let c = cmp.Candidate
      let deltaFloatStr (bOpt: float option) (cOpt: float option) (suffix: string) =
        match bOpt, cOpt with Some bv, Some cv -> sprintf "%+.2f%s" (cv - bv) suffix | _ -> "n/a"
      let deltaIntStr (bOpt: int option) (cOpt: int option) =
        match bOpt, cOpt with Some bv, Some cv -> sprintf "%+d" (cv - bv) | _ -> "n/a"
      let deltaInt64Str (bOpt: int64 option) (cOpt: int64 option) =
        match bOpt, cOpt with
        | Some bv, Some cv ->
          if bv > 0L then sprintf "%+d (%+.1f%%)" (cv - bv) ((float (cv - bv) / float bv) * 100.0)
          else sprintf "%+d" (cv - bv)
        | _ -> "n/a"
      let baseSlope = computeOverallScalingSlope b
      let candSlope = cmp.OverallScalingSlope
      let basePhaseMap = Map.ofList b.PhaseTimingsMs
      let candPhaseMap = Map.ofList c.PhaseTimingsMs
      let baseAllocMap = Map.ofList b.PhaseAllocBytes
      let candAllocMap = Map.ofList c.PhaseAllocBytes
      let allPhases =
        ((b.PhaseTimingsMs |> List.map fst)
         @ (c.PhaseTimingsMs |> List.map fst)
         @ (b.PhaseAllocBytes |> List.map fst)
         @ (c.PhaseAllocBytes |> List.map fst))
        |> List.distinct
      let phaseTable =
        if List.isEmpty allPhases then []
        else
          [ ""; "### Per-Phase Breakdown (`CLEF_PHASE_TIMING=1` / `GC.GetAllocatedBytes`)"; ""
            "| Phase | Base ms | Candidate ms | Base Alloc (B) | Candidate Alloc (B) |"
            "| :--- | ---: | ---: | ---: | ---: |" ]
          @ (allPhases |> List.map (fun ph ->
            sprintf "| `%s` | %s | %s | %s | %s |"
              (escapeCell ph)
              (fmtOptFloat " ms" (Map.tryFind ph basePhaseMap))
              (fmtOptFloat " ms" (Map.tryFind ph candPhaseMap))
              (fmtOptBytes (Map.tryFind ph baseAllocMap))
              (fmtOptBytes (Map.tryFind ph candAllocMap))))
      let scalingTable =
        if List.isEmpty c.ScalingPasses then []
        else
          [ ""; "### Scaling Canary Per-Pass Slopes (`Scale_4x` / `Scale_1x`)"; ""
            "| Pass | `Alloc(1x)` | `Alloc(4x)` | Slope `4x/1x` | Status |"
            "| :--- | ---: | ---: | ---: | :--- |" ]
          @ (c.ScalingPasses |> List.map (fun p ->
            sprintf "| `%s` | %d | %d | %.2f | %s |"
              (escapeCell p.PassName) p.Alloc1xBytes p.Alloc4xBytes p.Slope
              (if Double.IsNaN p.Slope || Double.IsInfinity p.Slope || p.Slope > 6.0 then "VETO" else "ok")))
      let phaseRegressionLines =
        if List.isEmpty cmp.PhaseAllocRegressions then []
        else
          [ ""; "### Phase Allocation Regressions (`> +15%` vs Base — VETO)" ]
          @ (cmp.PhaseAllocRegressions
             |> List.map (fun (phase, baseBytes, candBytes, pct) ->
               sprintf "- **VETO:** Phase '%s' heap allocation grew +%.1f%% (%d -> %d bytes, > +15.0%% baseline budget)"
                 phase pct baseBytes candBytes))
      let warningsLines =
        if List.isEmpty cmp.AllocRegressionWarnings then []
        else
          [ ""; "### Total Allocation Warnings (`> +15%` vs Base)" ]
          @ (cmp.AllocRegressionWarnings |> List.map (fun w -> sprintf "- **WARN:** %s" w))
      let missingLines =
        let mb = missingCanaryMetrics b
        let mc = missingCanaryMetrics c
        let targetMismatch =
          match tryGoverningPolicyFor b.Target, tryGoverningPolicyFor c.Target with
          | Some bp, Some cp when bp.Label <> cp.Label ->
            [ sprintf "- **BLOCKED (Target policy mismatch):** base `%s` (%s) <> candidate `%s` (%s)" b.Target bp.Label c.Target cp.Label ]
          | _ -> []
        [ if not (List.isEmpty mb) then
            yield sprintf "- **BLOCKED (Base missing required metrics):** `%s`" (String.concat ", " mb)
          if not (List.isEmpty mc) then
            yield sprintf "- **BLOCKED (Candidate missing required metrics):** `%s`" (String.concat ", " mc) ]
        @ targetMismatch
      let watchdogRows =
        [ if b.WatchdogBreach.IsSome || c.WatchdogBreach.IsSome then
            yield sprintf "| Watchdog breach | %s | %s | - |"
              (b.WatchdogBreach |> Option.defaultValue "none" |> escapeCell)
              (c.WatchdogBreach |> Option.defaultValue "none" |> escapeCell)
          if b.WatchdogSurvivors.IsSome || c.WatchdogSurvivors.IsSome then
            yield sprintf "| Watchdog survivors | %s | %s | - |"
              (b.WatchdogSurvivors |> Option.defaultValue "none" |> escapeCell)
              (c.WatchdogSurvivors |> Option.defaultValue "none" |> escapeCell)
          if b.WatchdogScanError.IsSome || c.WatchdogScanError.IsSome then
            yield sprintf "| Watchdog scanError | %s | %s | - |"
              (b.WatchdogScanError |> Option.defaultValue "none" |> escapeCell)
              (c.WatchdogScanError |> Option.defaultValue "none" |> escapeCell) ]
      let baseTarget = if String.IsNullOrWhiteSpace b.Target then "<missing>" else b.Target
      let candTarget = if String.IsNullOrWhiteSpace c.Target then "<missing>" else c.Target
      [ "## Part 5 — Live `.fidproj` Canary Receipt (Side-by-Side vs. `<base>`)"
        ""
        sprintf "- Target: `%s` (base) vs `%s` (candidate)" baseTarget candTarget ]
      @ missingLines
      @ [ ""
          "| Metric | Base | Candidate | Δ (Candidate - Base) |"
          "| :--- | ---: | ---: | ---: |"
          sprintf "| `[FrontEnd:1]` wall | %s | %s | %s |" (fmtOptFloat " ms" b.FrontEndMs) (fmtOptFloat " ms" c.FrontEndMs) (deltaFloatStr b.FrontEndMs c.FrontEndMs " ms")
          sprintf "| `[FrontEnd.ProofDischarge:3]` wall | %s | %s | %s |" (fmtOptFloat " ms" b.ProofDischargeMs) (fmtOptFloat " ms" c.ProofDischargeMs) (deltaFloatStr b.ProofDischargeMs c.ProofDischargeMs " ms")
          sprintf "| Total wall | %s | %s | %s |" (fmtOptFloat " ms" b.TotalWallMs) (fmtOptFloat " ms" c.TotalWallMs) (deltaFloatStr b.TotalWallMs c.TotalWallMs " ms")
          sprintf "| Peak RSS | %s | %s | %s |" (fmtOptBytesMb b.PeakRssBytes) (fmtOptBytesMb c.PeakRssBytes) (deltaInt64Str b.PeakRssBytes c.PeakRssBytes)
          sprintf "| Native exit code | %s | %s | %s |" (fmtOptInt b.NativeExitCode) (fmtOptInt c.NativeExitCode) (deltaIntStr b.NativeExitCode c.NativeExitCode)
          sprintf "| Compile exit code | %s | %s | %s |" (fmtOptInt b.CompileExitCode) (fmtOptInt c.CompileExitCode) (deltaIntStr b.CompileExitCode c.CompileExitCode)
          sprintf "| Total `GC.GetAllocatedBytes` | %s | %s | %s |" (fmtOptBytes b.TotalAllocBytes) (fmtOptBytes c.TotalAllocBytes) (deltaInt64Str b.TotalAllocBytes c.TotalAllocBytes)
          sprintf "| `PlatformResolution.read*` | %s | %s | %s |" (fmtOptInt b.PlatformResolutionReads) (fmtOptInt c.PlatformResolutionReads) (deltaIntStr b.PlatformResolutionReads c.PlatformResolutionReads)
          sprintf "| `NumericPublication.project` | %s | %s | %s |" (fmtOptInt b.NumericProjectionCalls) (fmtOptInt c.NumericProjectionCalls) (deltaIntStr b.NumericProjectionCalls c.NumericProjectionCalls)
          sprintf "| `MemoryPublication.project` | %s | %s | %s |" (fmtOptInt b.MemoryProjectionCalls) (fmtOptInt c.MemoryProjectionCalls) (deltaIntStr b.MemoryProjectionCalls c.MemoryProjectionCalls)
          sprintf "| `SpatialPublication.project` | %s | %s | %s |" (fmtOptInt b.SpatialProjectionCalls) (fmtOptInt c.SpatialProjectionCalls) (deltaIntStr b.SpatialProjectionCalls c.SpatialProjectionCalls)
          sprintf "| Hardware/Kernel module count | %s | %s | %s |" (fmtOptInt b.HardwareAndKernelModuleCount) (fmtOptInt c.HardwareAndKernelModuleCount) (deltaIntStr b.HardwareAndKernelModuleCount c.HardwareAndKernelModuleCount)
          sprintf "| `cvc5` spawns | %s | %s | %s |" (fmtOptInt b.Cvc5Spawns) (fmtOptInt c.Cvc5Spawns) (deltaIntStr b.Cvc5Spawns c.Cvc5Spawns)
          sprintf "| `Scale_1x` alloc (B) | %s | %s | %s |" (fmtOptBytes b.Scale1xAllocBytes) (fmtOptBytes c.Scale1xAllocBytes) (deltaInt64Str b.Scale1xAllocBytes c.Scale1xAllocBytes)
          sprintf "| `Scale_4x` alloc (B) | %s | %s | %s |" (fmtOptBytes b.Scale4xAllocBytes) (fmtOptBytes c.Scale4xAllocBytes) (deltaInt64Str b.Scale4xAllocBytes c.Scale4xAllocBytes)
          sprintf "| `Scale_1x` wall | %s | %s | %s |" (fmtOptFloat " ms" b.Scale1xWallMs) (fmtOptFloat " ms" c.Scale1xWallMs) (deltaFloatStr b.Scale1xWallMs c.Scale1xWallMs " ms")
          sprintf "| `Scale_4x` wall | %s | %s | %s |" (fmtOptFloat " ms" b.Scale4xWallMs) (fmtOptFloat " ms" c.Scale4xWallMs) (deltaFloatStr b.Scale4xWallMs c.Scale4xWallMs " ms")
          sprintf "| Scaling slope `alloc(4x)/alloc(1x)` | %s | %s | %s |" (fmtOptFloat "" baseSlope) (fmtOptFloat "" candSlope) (deltaFloatStr baseSlope candSlope "") ]
      @ watchdogRows
      @ phaseTable
      @ scalingTable
      @ phaseRegressionLines
      @ warningsLines

  let private renderPart6 (input: BlindAuditPacketInput) : string list =
    let retrievalLines =
      match input.Retrieval with
      | Error err ->
        [ "### (a) Pinned `lab_retrieval` Snapshot"
          ""
          sprintf "BLOCKED: Missing Part 6 (Pinned lab_retrieval Snapshot): %s" (SourceLoadError.describe err) ]
      | Ok None when input.OutsideIndex ->
        [ "### (a) Pinned `lab_retrieval` Snapshot"
          ""
          "- Provenance: `--outside-index` exemption"
          "- Status: `EXEMPT (--outside-index)` (repository outside the `lab_retrieval` index per `fidelity-lookup` §3)." ]
      | Ok None ->
        [ "### (a) Pinned `lab_retrieval` Snapshot"
          ""
          "BLOCKED: Missing Part 6 (Pinned lab_retrieval Snapshot): no snapshot provided and --outside-index not set." ]
      | Ok(Some snap) ->
        let revRows =
          if List.isEmpty snap.Revisions then []
          else
            [ ""; "| Source | Indexed Revision |"; "| :--- | :--- |" ]
            @ (snap.Revisions |> List.map (fun r ->
              sprintf "| `%s` | `%s` |" (escapeCell r.Source) (r.Revision |> Option.defaultValue "null")))
        [ "### (a) Pinned `lab_retrieval` Snapshot"
          ""
          sprintf "- Provenance: %s" (describeRetrievalProvenance snap.Provenance)
          sprintf "- `freshness.fresh`: `%b` (validFresh=%b)" snap.Fresh snap.IsValidFresh
          sprintf "- `snapshot_id`: `%s`" (if String.IsNullOrEmpty snap.SnapshotId then "<missing>" else snap.SnapshotId)
          sprintf "- `current_snapshot_id`: `%s`" (snap.CurrentSnapshotId |> Option.defaultValue "n/a")
          sprintf "- `generation`: `%s` | `state`: `%s`" (snap.Generation |> Option.map string |> Option.defaultValue "n/a") snap.State
          sprintf "- `branch`: `%s` | `branchStatus`: `%s` | `onMain`: `%s`"
            (snap.BranchName |> Option.defaultValue "n/a")
            (snap.BranchStatus |> Option.defaultValue "n/a")
            (snap.OnMain |> Option.map string |> Option.defaultValue "n/a") ]
        @ (if input.OutsideIndex then [ "- Note: `--outside-index` was set, but an explicit retrieval snapshot was supplied and validated." ] else [])
        @ (match snap.InvalidReason with Some r -> [ sprintf "- Invalid reason: %s" r ] | None -> [])
        @ (match snap.Note with Some n -> [ sprintf "- Note: %s" n ] | None -> [])
        @ revRows

    let ballastLines =
      match input.SpecBallast with
      | SpecBallastStatus.Skipped reason ->
        [ ""; "### (b) `clef-search` Spec Ballast Addendum"; ""
          sprintf "- Provenance: explicitly skipped (%s)" reason ]
      | SpecBallastStatus.Unavailable(query, reason) ->
        [ ""; "### (b) `clef-search` Spec Ballast Addendum"; ""
          sprintf "- Query: `%s`" query
          sprintf "- BLOCKED: unavailable (%s)" (SourceLoadError.describe reason) ]
      | SpecBallastStatus.Loaded(query, fusion, hits) ->
        let hitLines =
          if List.isEmpty hits then [ "- BLOCKED: No hits returned." ]
          else
            hits
            |> List.collect (fun h ->
              [ sprintf "- **[%s](%s)** (%s / `%s`)" h.Title h.Url h.Section h.ContentType
                sprintf "  > %s" (h.Snippet.Replace("\r", "").Replace("\n", " ")) ])
        [ ""; "### (b) `clef-search` Spec Ballast Addendum"; ""
          "- Provenance: live fetch (`clef-search`)"
          sprintf "- Query: `%s` | Fusion: `%s` | Hits: `%d`" query fusion hits.Length
          "" ]
        @ hitLines

    [ "## Part 6 — Pinned `lab_retrieval` Snapshot & `clef-search` Spec Ballast"; "" ]
    @ retrievalLines
    @ ballastLines

  let renderPacket (evaluated: EvaluatedPacket) : string =
    let input = evaluated.Input
    let vetoes = evaluated.Vetoes
    let comp = evaluated.Completeness
    let candidateDisplay =
      match input.CodeDelta with
      | Ok d ->
        match d.CandidateBranch with
        | Some b -> sprintf "%s (`%s`, branch `%s`)" input.CandidateRef d.CandidateSha b
        | None -> sprintf "%s (`%s`)" input.CandidateRef d.CandidateSha
      | Error _ -> input.CandidateRef

    let statusBanner =
      if not comp.IsComplete then
        "> **PACKET STATUS: BLOCKED: incomplete packet**"
      elif not (List.isEmpty vetoes) then
        sprintf "> **MECHANICAL VERDICT: REJECT (%d automatic mechanical veto(es) triggered)**" vetoes.Length
      else
        "> **MECHANICAL VERDICT: PASS (0 mechanical vetoes — ready for independent /ffi-implementation-audit and /ffi-drift-review)**"

    let triggeredVetoLines =
      if List.isEmpty vetoes then [ "- None." ]
      else vetoes |> List.map (fun v -> sprintf "- **VETO:** %s" (MechanicalVeto.describe v))

    let sections =
      [ [ "# Blind Audit Packet"
          ""
          statusBanner
          ""
          "| Field | Value |"
          "| :--- | :--- |"
          sprintf "| Repository | `%s` |" (escapeCell input.RepoPath)
          sprintf "| Base SHA | `%s` |" (escapeCell input.BaseSha)
          sprintf "| Candidate | %s |" candidateDisplay
          sprintf "| Issue URL | %s |" input.IssueUrl
          sprintf "| Outside Index | `%b` |" input.OutsideIndex
          "| Assembly Mode | deterministic machine-assembled (zero implementer narrative) |"
          ""
          "## Mechanical Veto Summary"
          ""
          "| Gate | Status | Detail |"
          "| :--- | :--- | :--- |" ]
        @ renderVetoSummaryRows input vetoes
        @ [ ""; "### Triggered Mechanical Vetoes"; "" ]
        @ triggeredVetoLines
        @ [ "" ]
        @ renderPart1 input.Issue
        @ [ "" ]
        @ renderPart2 input.CodeDelta
        @ [ "" ]
        @ renderPart3 input.Analyzer input.TestReceipt
        @ [ "" ]
        @ renderPart4 input.CodeDelta
        @ [ "" ]
        @ renderPart5 input evaluated.CanaryComparison
        @ [ "" ]
        @ renderPart6 input ]

    sections |> List.concat |> String.concat "\n" |> fun s -> s + "\n"
