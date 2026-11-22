namespace Bozzetto.Audit

open System

// Pure Zone A packet completeness checker, governing canary policy cap
// enforcer, and mechanical veto evaluator. Zero I/O, zero mutable state.
[<AutoOpen>]
module AuditPacketEval =

  // Governing Layer A & Layer B canary budgets from Composer wiki:
  // https://forge.spkez.dev/FidelityFramework/Composer/wiki/Pipeline-Guardrails-and-Canary-Budgets
  // and Farscape wiki Course-Correction-2026-11-19 (Phase 0).
  type internal GoverningCanaryPolicy = {
    WallCapMs: float option
    FrontEndCapMs: float option
    ProofDischargeCapMs: float option
    RssCapBytes: int64 option
    FixedCvc5Cap: int option
    Label: string
  }

  // Governing Layer A / Layer B canary budgets from Composer wiki
  // `Pipeline-Guardrails-and-Canary-Budgets`.
  let internal tryGoverningPolicyFor (target: string) : GoverningCanaryPolicy option =
    let t = target.Trim()
    let baseName = fileNameOf t
    let lower = baseName.ToLowerInvariant()
    if lower = "hello-p3" || lower = "canary1-p3" then
      Some {
        WallCapMs = Some 5000.0
        FrontEndCapMs = Some 3000.0
        ProofDischargeCapMs = Some 500.0
        RssCapBytes = Some(384L * 1_048_576L)
        FixedCvc5Cap = Some 4
        Label = "hello-p3"
      }
    elif lower = "01_helloworlddirect"
         || lower = "01_helloworlddirect.fidproj"
         || lower = "hello"
         || lower = "canary1" then
      Some {
        WallCapMs = Some 15000.0
        FrontEndCapMs = Some 6500.0
        ProofDischargeCapMs = Some 1000.0
        RssCapBytes = Some(512L * 1_048_576L)
        FixedCvc5Cap = Some 4
        Label = "01_HelloWorldDirect"
      }
    elif lower = "sqlite"
         || lower = "canary2"
         || lower = "fidelity.sqlite"
         || lower = "fidelity.sqlite.fidproj" then
      Some {
        WallCapMs = Some 15000.0
        FrontEndCapMs = None
        ProofDischargeCapMs = None
        RssCapBytes = Some(768L * 1_048_576L)
        FixedCvc5Cap = None
        Label = "sqlite"
      }
    elif lower = "challenge-p3" || lower = "canary3-p3" then
      Some {
        WallCapMs = Some 15000.0
        FrontEndCapMs = Some 15000.0
        ProofDischargeCapMs = None
        RssCapBytes = Some(1024L * 1_048_576L)
        FixedCvc5Cap = None
        Label = "challenge-p3"
      }
    elif lower = "challenge"
         || lower = "canary3"
         || lower = "challengeserver"
         || lower = "challengeserver.fidproj" then
      Some {
        WallCapMs = Some 30000.0
        FrontEndCapMs = None
        ProofDischargeCapMs = None
        RssCapBytes = Some(1228L * 1_048_576L)
        FixedCvc5Cap = None
        Label = "challenge"
      }
    else
      None

  let private effectiveFloatCap (policyCap: float option) (receiptLimit: float option) : float option =
    match policyCap, receiptLimit with
    | Some p, Some r when Double.IsFinite r && r > 0.0 -> Some(min p r)
    | Some p, _ -> Some p
    | None, Some r when Double.IsFinite r && r > 0.0 -> Some r
    | None, _ -> None

  let private effectiveInt64Cap (policyCap: int64 option) (receiptLimit: int64 option) : int64 option =
    match policyCap, receiptLimit with
    | Some p, Some r when r > 0L -> Some(min p r)
    | Some p, _ -> Some p
    | None, Some r when r > 0L -> Some r
    | None, _ -> None

  let missingCanaryMetrics (c: CanaryReceipt) : string list =
    let policyOpt = tryGoverningPolicyFor c.Target
    [ if String.IsNullOrWhiteSpace c.Target then
        yield "target"
      elif policyOpt.IsNone then
        yield sprintf "target (unrecognized governing canary '%s')" (c.Target.Trim())
      match c.TotalWallMs with
      | None -> yield "totalWallMs"
      | Some v when not (Double.IsFinite v) || v < 0.0 -> yield "totalWallMs (must be >= 0)"
      | Some _ -> ()
      match c.PeakRssBytes with
      | None -> yield "peakRssBytes"
      | Some v when v < 0L -> yield "peakRssBytes (must be >= 0)"
      | Some _ -> ()
      if c.NativeExitCode.IsNone then yield "nativeExitCode"
      match c.TotalAllocBytes with
      | None -> yield "totalAllocBytes"
      | Some v when v < 0L -> yield "totalAllocBytes (must be >= 0)"
      | Some _ -> ()
      if List.isEmpty c.PhaseTimingsMs then
        yield "phaseTimingsMs"
      elif c.PhaseTimingsMs |> List.exists (fun (_, v) -> not (Double.IsFinite v) || v < 0.0) then
        yield "phaseTimingsMs (must be >= 0)"
      if List.isEmpty c.PhaseAllocBytes then
        yield "phaseAllocBytes"
      elif c.PhaseAllocBytes |> List.exists (fun (_, b) -> b < 0L) then
        yield "phaseAllocBytes (must be >= 0)"
      match c.PlatformResolutionReads with
      | None -> yield "platformResolutionReads"
      | Some v when v < 0 -> yield "platformResolutionReads (must be >= 0)"
      | Some _ -> ()
      match c.NumericProjectionCalls with
      | None -> yield "numericProjectionCalls"
      | Some v when v < 0 -> yield "numericProjectionCalls (must be >= 0)"
      | Some _ -> ()
      match c.MemoryProjectionCalls with
      | None -> yield "memoryProjectionCalls"
      | Some v when v < 0 -> yield "memoryProjectionCalls (must be >= 0)"
      | Some _ -> ()
      match c.SpatialProjectionCalls with
      | None -> yield "spatialProjectionCalls"
      | Some v when v < 0 -> yield "spatialProjectionCalls (must be >= 0)"
      | Some _ -> ()
      match c.HardwareAndKernelModuleCount with
      | None -> yield "hardwareAndKernelModuleCount"
      | Some v when v < 0 -> yield "hardwareAndKernelModuleCount (must be >= 0)"
      | Some 0 when c.SpatialExecuted.IsNone -> yield "spatialExecuted"
      | Some _ -> ()
      match c.Cvc5Spawns with
      | None -> yield "cvc5Spawns"
      | Some v when v < 0 -> yield "cvc5Spawns (must be >= 0)"
      | Some _ -> ()
      match c.Scale1xAllocBytes, c.Scale4xAllocBytes with
      | None, _ | _, None -> yield "scaling (scale1xAllocBytes and scale4xAllocBytes required)"
      | Some a1, _ when a1 < 0L -> yield "scale1xAllocBytes (must be >= 0)"
      | _, Some a4 when a4 < 0L -> yield "scale4xAllocBytes (must be >= 0)"
      | _ -> ()
      match c.Scale1xWallMs, c.Scale4xWallMs with
      | None, _ | _, None -> yield "scaling (scale1xWallMs and scale4xWallMs required)"
      | Some t1, _ when not (Double.IsFinite t1) || t1 < 0.0 -> yield "scale1xWallMs (must be >= 0)"
      | _, Some t4 when not (Double.IsFinite t4) || t4 < 0.0 -> yield "scale4xWallMs (must be >= 0)"
      | _ -> ()
      if List.isEmpty c.ScalingPasses then
        yield "scalingPasses (per-pass scaling allocations required)"
      elif c.ScalingPasses |> List.exists (fun p -> p.Alloc1xBytes < 0L || p.Alloc4xBytes < 0L) then
        yield "scalingPasses (allocations must be >= 0)"
      let hasWatchdogOutcome =
        c.WatchdogCleared || c.WatchdogBreach.IsSome || c.WatchdogSurvivors.IsSome || c.WatchdogScanError.IsSome
      if not hasWatchdogOutcome then
        yield "watchdogClearance (watchdogSurvivors=0/none or explicit watchdog outcome required)"
      match policyOpt with
      | Some policy ->
        match policy.FrontEndCapMs, c.FrontEndMs with
        | Some _, None -> yield "frontEndMs"
        | _, Some v when not (Double.IsFinite v) || v < 0.0 -> yield "frontEndMs (must be >= 0)"
        | _ -> ()
        match policy.ProofDischargeCapMs, c.ProofDischargeMs with
        | Some _, None -> yield "proofDischargeMs"
        | _, Some v when not (Double.IsFinite v) || v < 0.0 -> yield "proofDischargeMs (must be >= 0)"
        | _ -> ()
        match policy.FixedCvc5Cap, c.UniqueObligations with
        | None, None -> yield "uniqueObligations"
        | _, Some u when u < 0 -> yield "uniqueObligations (must be >= 0)"
        | _ -> ()
      | None -> () ]

  let private unretrievedSameHostWikiUrls (issueUrl: string) (issue: IssueAuthority) : string list =
    match tryParseHttpUrl issueUrl with
    | None -> []
    | Some parsed ->
      let expectedRefs = extractSameHostWikiRefs parsed.Origin issue.ReferencedUrls
      let loadedKeys =
        issue.WikiRulings
        |> List.choose (fun r -> parseWikiUrl r.Url |> Option.map wikiRefKey)
        |> Set.ofList
      let failedKeys =
        issue.WikiRulingFailures
        |> List.choose (fun f -> parseWikiUrl f.Url |> Option.map wikiRefKey)
        |> Set.ofList
      expectedRefs
      |> List.filter (fun w ->
        let k = wikiRefKey w
        not (Set.contains k loadedKeys) && not (Set.contains k failedKeys))
      |> List.map _.CanonicalUrl

  let checkCompleteness (input: BlindAuditPacketInput) : PacketCompleteness =
    let missing =
      [ match input.Issue with
        | Error err -> yield sprintf "Part 1 (Verbatim Authority): %s" (SourceLoadError.describe err)
        | Ok issue ->
          if not issue.CommentsVerified then
            yield "Part 1 (Issue Comments): issue source lacks verified comment inventory (use live Forgejo API or JSON export with comments)"
          let distinctCommentCount =
            let withIds = issue.Comments |> List.choose _.Id
            if List.isEmpty withIds then issue.Comments.Length
            else (withIds |> List.distinct |> List.length) + (issue.Comments.Length - withIds.Length)
          match issue.ExpectedCommentCount with
          | Some expected when expected > distinctCommentCount ->
            yield sprintf "Part 1 (Issue Comments): issue declares %d comment(s) but only %d distinct comment(s) were loaded" expected distinctCommentCount
          | _ -> ()
          for f in issue.WikiRulingFailures do
            yield sprintf "Part 1 (Referenced Wiki Ruling %s): %s" f.Url (SourceLoadError.describe f.Error)
          for unretrieved in unretrievedSameHostWikiUrls input.IssueUrl issue do
            yield sprintf "Part 1 (Referenced Wiki Ruling %s): referenced same-host wiki ruling was not fetched" unretrieved

        match input.CodeDelta with
        | Error err ->
          yield sprintf "Part 2 (Exact Code Delta): %s" (SourceLoadError.describe err)
          yield "Part 4 (Automated Anti-Pattern Delta): requires Part 2 diff"
        | Ok delta ->
          for path, reason in delta.UnaccountedSrcFiles do
            yield sprintf "Part 2/4 (Unreadable diff file %s): %s" path reason

        match input.Analyzer with
        | Error err -> yield sprintf "Part 3 (Independent Analyzer Receipt): %s" (SourceLoadError.describe err)
        | Ok None -> yield "Part 3 (Independent Analyzer Receipt): SARIF receipt missing"
        | Ok(Some _) -> ()

        match input.TestReceipt with
        | Error err -> yield sprintf "Part 3 (Test Compilation Receipt): %s" (SourceLoadError.describe err)
        | Ok None -> yield "Part 3 (Test Compilation Receipt): test compilation receipt missing (--test-receipt required)"
        | Ok(Some _) -> ()

        match input.CanaryBase with
        | Error err -> yield sprintf "Part 5 (Live Canary Base Receipt): %s" (SourceLoadError.describe err)
        | Ok None -> yield "Part 5 (Live Canary Base Receipt): base canary receipt missing"
        | Ok(Some c) ->
          match missingCanaryMetrics c with
          | [] -> ()
          | missingFields ->
            yield sprintf "Part 5 (Live Canary Base Receipt): missing required metrics: %s" (String.concat ", " missingFields)

        match input.CanaryCandidate with
        | Error err -> yield sprintf "Part 5 (Live Canary Candidate Receipt): %s" (SourceLoadError.describe err)
        | Ok None -> yield "Part 5 (Live Canary Candidate Receipt): candidate canary receipt missing"
        | Ok(Some c) ->
          match missingCanaryMetrics c with
          | [] -> ()
          | missingFields ->
            yield sprintf "Part 5 (Live Canary Candidate Receipt): missing required metrics: %s" (String.concat ", " missingFields)

        match input.CanaryBase, input.CanaryCandidate with
        | Ok(Some b), Ok(Some c) ->
          match tryGoverningPolicyFor b.Target, tryGoverningPolicyFor c.Target with
          | Some bp, Some cp when bp.Label <> cp.Label ->
            yield sprintf "Part 5 (Live Canary Target Mismatch): base target '%s' (%s) <> candidate target '%s' (%s)" b.Target bp.Label c.Target cp.Label
          | Some bp, Some _ when bp.Label <> "01_HelloWorldDirect" && bp.Label <> "hello-p3" ->
            yield sprintf "Part 5 (Live Canary Baseline): required 01_HelloWorldDirect/hello-p3 baseline canary pair missing (got '%s')" bp.Label
          | _ -> ()
          let candPhaseKeys = c.PhaseAllocBytes |> List.map fst |> Set.ofList
          let missingPhases =
            b.PhaseAllocBytes
            |> List.map fst
            |> List.filter (fun phase -> not (Set.contains phase candPhaseKeys))
          if not (List.isEmpty missingPhases) then
            yield sprintf "Part 5 (Live Canary Candidate Receipt): missing base phaseAllocBytes key(s): %s" (String.concat ", " missingPhases)
          let candPassNames = c.ScalingPasses |> List.map _.PassName |> Set.ofList
          let missingScalingPasses =
            b.ScalingPasses
            |> List.map _.PassName
            |> List.filter (fun pass -> not (Set.contains pass candPassNames))
          if not (List.isEmpty missingScalingPasses) then
            yield sprintf "Part 5 (Live Canary Candidate Receipt): missing base scalingPasses key(s): %s" (String.concat ", " missingScalingPasses)
        | _ -> ()

        if not input.OutsideIndex then
          match input.Retrieval with
          | Error err -> yield sprintf "Part 6 (Pinned lab_retrieval Snapshot): %s" (SourceLoadError.describe err)
          | Ok None -> yield "Part 6 (Pinned lab_retrieval Snapshot): retrieval snapshot missing (pass --outside-index if repo is outside index)"
          | Ok(Some _) -> ()
        else
          match input.Retrieval with
          | Error err -> yield sprintf "Part 6 (Pinned lab_retrieval Snapshot): %s" (SourceLoadError.describe err)
          | Ok _ -> ()

        match input.SpecBallast with
        | SpecBallastStatus.Skipped reason ->
          yield sprintf "Part 6 (clef-search Spec Ballast): omitted (%s; Spec Ballast excerpts are required by #11094)" reason
        | SpecBallastStatus.Loaded(query, _, hits) ->
          if not (hits |> List.exists isValidSpecBallastHit) then
            yield sprintf "Part 6 (clef-search Spec Ballast): query '%s' returned 0 valid clef-lang.com hits with non-blank snippets" query
        | SpecBallastStatus.Unavailable(query, err) ->
          yield sprintf "Part 6 (clef-search Spec Ballast): query '%s' failed: %s" query (SourceLoadError.describe err) ]
    { IsComplete = List.isEmpty missing; MissingParts = missing }

  let private evaluateCanaryVetoes (baseCanaryOpt: CanaryReceipt option) (cand: CanaryReceipt) : MechanicalVeto list =
    let policyOpt = tryGoverningPolicyFor cand.Target

    let projVetoes =
      [ match cand.PlatformResolutionReads with
        | Some n when n > 1 -> yield MechanicalVeto.ProjectionCountExceeded("PlatformResolution.read*", n)
        | _ -> ()
        match cand.NumericProjectionCalls with
        | Some n when n > 1 -> yield MechanicalVeto.ProjectionCountExceeded("NumericPublication.project", n)
        | _ -> ()
        match cand.MemoryProjectionCalls with
        | Some n when n > 1 -> yield MechanicalVeto.ProjectionCountExceeded("MemoryPublication.project", n)
        | _ -> ()
        match cand.SpatialProjectionCalls with
        | Some n when n > 1 -> yield MechanicalVeto.ProjectionCountExceeded("SpatialPublication.project", n)
        | _ -> () ]

    let spatialZeroVeto =
      match cand.HardwareAndKernelModuleCount with
      | Some 0 ->
        let calls = cand.SpatialProjectionCalls |> Option.defaultValue 0
        if calls > 0 || cand.SpatialExecuted = Some true then
          [ MechanicalVeto.SpatialExecutedWithZeroKernelModules(max 1 calls) ]
        else []
      | _ -> []

    let slopeVetoes =
      let overall =
        match computeOverallScalingSlope cand with
        | Some s when Double.IsNaN s || Double.IsInfinity s || s > 6.0 ->
          [ MechanicalVeto.ScalingSlopeExceeded("Scale_4x/Scale_1x", s, 6.0) ]
        | _ -> []
      let perPass =
        cand.ScalingPasses
        |> List.choose (fun p ->
          if Double.IsNaN p.Slope || Double.IsInfinity p.Slope || p.Slope > 6.0 then
            Some(MechanicalVeto.ScalingSlopeExceeded(p.PassName, p.Slope, 6.0))
          else None)
      overall @ perPass

    let cvc5Veto =
      match cand.Cvc5Spawns with
      | None -> []
      | Some spawns ->
        let capOpt =
          match policyOpt |> Option.bind (fun p -> p.FixedCvc5Cap |> Option.map (fun cap -> cap, p.Label)) with
          | Some(fixedCap, label) ->
            Some(fixedCap, sprintf "%s governing cap <= %d" label fixedCap)
          | None ->
            match cand.UniqueObligations with
            | Some u ->
              let c = int (Math.Ceiling(float (max 0 u) / 64.0)) * 2
              Some(c, sprintf "ceil(%d/64)*2 budget" u)
            | None -> None
        match capOpt with
        | Some(cap, reason) when spawns > cap -> [ MechanicalVeto.Cvc5SpawnSpike(spawns, cap, reason) ]
        | _ -> []

    let exitVetoes =
      [ match cand.CommandExitCode with
        | Some code when code <> 0 -> yield MechanicalVeto.CanaryExitBreach("command", code)
        | _ -> ()
        match cand.CompileExitCode with
        | Some code when code <> 0 -> yield MechanicalVeto.CanaryExitBreach("compile", code)
        | _ -> ()
        match cand.NativeExitCode with
        | Some code when code <> 0 -> yield MechanicalVeto.CanaryExitBreach("native", code)
        | _ -> () ]

    let watchdogVetoes =
      [ match cand.WatchdogBreach with
        | Some b when not (String.IsNullOrWhiteSpace b)
                      && not (String.Equals(b.Trim(), "none", StringComparison.OrdinalIgnoreCase)) ->
          yield MechanicalVeto.CanaryWatchdogBreach(b.Trim())
        | _ -> ()
        match cand.WatchdogSurvivors with
        | Some s ->
          let trimmed = s.Trim()
          if not (String.IsNullOrEmpty trimmed)
             && not (String.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
             && trimmed <> "0" then
            yield MechanicalVeto.CanaryWatchdogBreach(sprintf "survivors=%s" trimmed)
        | None -> ()
        match cand.WatchdogScanError with
        | Some e ->
          let trimmed = e.Trim()
          if not (String.IsNullOrEmpty trimmed)
             && not (String.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)) then
            yield MechanicalVeto.CanaryWatchdogBreach(sprintf "scanError=%s" trimmed)
        | None -> () ]

    let wallVetoes =
      let effWallLimit = effectiveFloatCap (policyOpt |> Option.bind _.WallCapMs) cand.WallLimitMs
      let effFeLimit = effectiveFloatCap (policyOpt |> Option.bind _.FrontEndCapMs) cand.FrontEndLimitMs
      let effPdLimit = effectiveFloatCap (policyOpt |> Option.bind _.ProofDischargeCapMs) cand.ProofDischargeLimitMs
      [ match cand.TotalWallMs, effWallLimit with
        | Some actual, Some limit when actual > limit ->
          yield MechanicalVeto.CanaryWallBreach(actual, limit)
        | _ -> ()
        match cand.FrontEndMs, effFeLimit with
        | Some actual, Some limit when actual > limit ->
          yield MechanicalVeto.CanaryPhaseWallBreach("FrontEnd", actual, limit)
        | _ -> ()
        match cand.ProofDischargeMs, effPdLimit with
        | Some actual, Some limit when actual > limit ->
          yield MechanicalVeto.CanaryPhaseWallBreach("ProofDischarge", actual, limit)
        | _ -> () ]

    let phaseAllocVetoes =
      match baseCanaryOpt with
      | None -> []
      | Some baseCanary ->
        findPhaseAllocRegressions baseCanary cand
        |> List.map (fun (phase, baseBytes, candBytes, pct) ->
          MechanicalVeto.CanaryPhaseAllocRegression(phase, baseBytes, candBytes, pct))

    let rssVeto =
      let effRssLimit = effectiveInt64Cap (policyOpt |> Option.bind _.RssCapBytes) cand.RssLimitBytes
      match cand.PeakRssBytes, effRssLimit with
      | Some actual, Some limit when actual > limit -> [ MechanicalVeto.CanaryRssBreach(actual, limit) ]
      | _ -> []

    let canaryTestCompileVeto =
      match cand.FailedTestCompile with
      | Some true -> [ MechanicalVeto.FailedTestCompile "Canary receipt flagged test compilation failure." ]
      | _ -> []

    projVetoes
    @ spatialZeroVeto
    @ slopeVetoes
    @ cvc5Veto
    @ exitVetoes
    @ watchdogVetoes
    @ wallVetoes
    @ phaseAllocVetoes
    @ rssVeto
    @ canaryTestCompileVeto

  let evaluateVetoes (input: BlindAuditPacketInput) : EvaluatedPacket =
    let completeness = checkCompleteness input
    let baseCanaryOpt = match input.CanaryBase with Ok opt -> opt | Error _ -> None
    let candCanaryOpt = match input.CanaryCandidate with Ok opt -> opt | Error _ -> None
    let comparison =
      match baseCanaryOpt, candCanaryOpt with
      | Some b, Some c -> Some(compareCanaries b c)
      | _ -> None

    let analyzerVetoes =
      match input.Analyzer with
      | Ok(Some a) ->
        let diagVetoes =
          a.RedTargetDiagnostics
          |> List.map (fun d -> MechanicalVeto.RedAnalyzerDiagnostic(d.RuleId, d.File, d.Line, d.Message))
        let revMismatchVeto =
          match input.CodeDelta with
          | Ok delta when not (List.isEmpty a.RevisionIds) ->
            let matchesCandidate =
              a.RevisionIds
              |> List.exists (fun rev -> String.Equals(rev.Trim(), delta.CandidateSha.Trim(), StringComparison.OrdinalIgnoreCase))
            if matchesCandidate then []
            else
              [ MechanicalVeto.MissingAxd000Hash(sprintf "Analyzer SARIF versionControlProvenance revision (%s) does not match candidate SHA %s." (String.concat ", " a.RevisionIds) delta.CandidateSha) ]
          | _ -> []
        let axdVeto =
          match a.Axd000Disagreement with
          | Some msg -> [ MechanicalVeto.MissingAxd000Hash msg ]
          | None ->
            if a.Axd000Valid then []
            elif a.HasInvalidAxd000Entry then
              [ MechanicalVeto.MissingAxd000Hash "AXD000 receipt contains MISSING or malformed SHA-256 hash." ]
            else
              [ MechanicalVeto.MissingAxd000Hash "SARIF and --axd000 receipt contain no valid AXD000 SHA-256 hash." ]
        diagVetoes @ revMismatchVeto @ axdVeto
      | _ -> []

    let antiPatternVetoes =
      match input.CodeDelta with
      | Ok delta ->
        let ap = delta.AntiPatterns
        let maxUpward (agg: CounterDelta) (pick: FileAntiPatternHit -> CounterDelta) =
          let filePos = ap.PerFileHits |> List.sumBy (fun h -> max 0 (pick h).Delta)
          max agg.Delta filePos
        [ let cwtUp = maxUpward ap.ConditionalWeakTable _.ConditionalWeakTable
          if cwtUp > 0 then
            yield MechanicalVeto.AntiPatternIncrease("ΔConditionalWeakTable", cwtUp)
          let concUp = maxUpward ap.Concurrent _.Concurrent
          if concUp > 0 then
            yield MechanicalVeto.AntiPatternIncrease("ΔConcurrent*", concUp)
          let sysRefUp = maxUpward ap.SystemReflection _.SystemReflection
          if sysRefUp > 0 then
            yield MechanicalVeto.AntiPatternIncrease("ΔSystem.Reflection", sysRefUp)
          let fsRefUp = maxUpward ap.FSharpReflection _.FSharpReflection
          if fsRefUp > 0 then
            yield MechanicalVeto.AntiPatternIncrease("ΔMicrosoft.FSharp.Reflection", fsRefUp)
          let bclUp = maxUpward ap.BclMutable _.BclMutable
          if bclUp > 0 then
            yield MechanicalVeto.AntiPatternIncrease("ΔBCL_Mutable", bclUp)
          let letMutUp = maxUpward ap.LetMutable _.LetMutable
          if letMutUp > 0 then
            yield MechanicalVeto.AntiPatternIncrease("Δlet_mutable", letMutUp)
          let normUp = maxUpward ap.NativeServiceNormalize _.NativeServiceNormalize
          if normUp > 0 then
            yield MechanicalVeto.AntiPatternIncrease("ΔNativeService_normalize", normUp)
          if not (List.isEmpty ap.OverBudgetFileGrowth) then
            let maxOverGrowth =
              ap.OverBudgetFileGrowth
              |> List.map (fun (_, beforeLines, afterLines) -> afterLines - beforeLines)
              |> List.max
            yield MechanicalVeto.AntiPatternIncrease("Δmax_file_lines", maxOverGrowth) ]
      | Error _ -> []

    let canaryVetoes =
      match candCanaryOpt with
      | None -> []
      | Some cand -> evaluateCanaryVetoes baseCanaryOpt cand

    let testCompileVeto =
      match input.TestReceipt with
      | Ok(Some tr) when not tr.CompileSucceeded -> [ MechanicalVeto.FailedTestCompile tr.Summary ]
      | _ -> []

    let retrievalVetoes =
      match input.Retrieval with
      | Ok(Some snap) when not snap.IsValidFresh ->
        [ MechanicalVeto.StaleOrMissingRetrievalSnapshot(snap.InvalidReason |> Option.defaultValue "Retrieval snapshot is stale.") ]
      | Ok(Some _) -> []
      | Ok None ->
        if input.OutsideIndex then []
        else [ MechanicalVeto.StaleOrMissingRetrievalSnapshot "Retrieval snapshot is missing and --outside-index was not set." ]
      | Error err ->
        [ MechanicalVeto.StaleOrMissingRetrievalSnapshot(SourceLoadError.describe err) ]

    let completenessVeto =
      if completeness.IsComplete then []
      else [ MechanicalVeto.IncompletePacket completeness.MissingParts ]

    { Input = input
      Completeness = completeness
      CanaryComparison = comparison
      Vetoes =
        analyzerVetoes
        @ antiPatternVetoes
        @ canaryVetoes
        @ testCompileVeto
        @ retrievalVetoes
        @ completenessVeto }
