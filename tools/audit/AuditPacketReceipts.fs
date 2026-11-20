namespace Bozzetto.Audit

open System
open System.Globalization
open System.Text.RegularExpressions
open Fidelity.Data.JSON

// Pure Zone A Part 3 & Part 6 receipt parsers using Fidelity.Data.JSON:
// SARIF + AXD000 provenance, strict test compile receipt, revision-bound
// lab_retrieval snapshot freshness, and clef-search Spec Ballast.
[<AutoOpen>]
module AuditPacketReceipts =

  // ---------------------------------------------------------------------------
  // Independent Analyzer SARIF + AXD000 provenance parser
  // ---------------------------------------------------------------------------

  let private parseAxd000JsonObject
      (parseText: string -> string -> Axd000Digest list * bool)
      (sourceLabel: string)
      (objVal: JsonValue)
      : Axd000Digest list * bool =
    let directMessage = tryProp "message" objVal |> Option.bind (tryNonBlankStringProp [ "text" ])
    match directMessage with
    | Some msgText -> parseText sourceLabel msgText
    | None ->
      let hDigs, hBad =
        match tryProp "hashes" objVal with
        | Some(JsonValue.Object pairs) ->
          pairs
          |> List.fold (fun (digs, bad) (k, v) ->
            match tryNonBlankString v with
            | Some s when isHexString 64 64 s ->
              digs @ [ { Path = k; Sha256 = s.ToUpperInvariant() } ], bad
            | _ -> digs, true) ([], false)
        | Some _ -> [], true
        | None -> [], false
      let sDigs, sBad =
        match tryNonBlankStringProp [ "hash"; "sha256"; "digest"; "axd000" ] objVal with
        | Some v when isHexString 64 64 v -> [ { Path = sourceLabel; Sha256 = v.ToUpperInvariant() } ], false
        | Some _ -> [], true
        | None -> [], false
      hDigs @ sDigs, hBad || sBad

  let rec parseAxd000PayloadText (sourceLabel: string) (text: string) : Axd000Digest list * bool =
    if String.IsNullOrWhiteSpace text then [], true
    else
      let trimmed = text.Trim()
      if trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal) then
        match parseJsonValue "AXD000 payload" trimmed with
        | Error _ -> [], true
        | Ok root ->
          let rec collect (v: JsonValue) : Axd000Digest list * bool =
            match v with
            | JsonValue.Array items ->
              items
              |> List.fold (fun (digs, bad) item ->
                let d1, b1 = collect item
                digs @ d1, bad || b1) ([], false)
            | JsonValue.Object _ -> parseAxd000JsonObject parseAxd000PayloadText sourceLabel v
            | _ -> [], true
          collect root
      else
        trimmed.Replace("\r\n", "\n").Split('\n')
        |> Array.fold (fun (digs, bad) (rawLine: string) ->
          let line = rawLine.Trim()
          if String.IsNullOrEmpty line then digs, bad
          elif line.Contains("MISSING", StringComparison.OrdinalIgnoreCase) then digs, true
          else
            let kvMatch = Regex.Match(line, @"^(?:AXD000|sha256|hash)\s*[:=]\s*([0-9a-fA-F]{64})$", RegexOptions.IgnoreCase)
            let sumMatch = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$")
            if kvMatch.Success then
              digs @ [ { Path = sourceLabel; Sha256 = kvMatch.Groups[1].Value.ToUpperInvariant() } ], bad
            elif sumMatch.Success then
              digs @ [ { Path = sumMatch.Groups[2].Value.Trim(); Sha256 = sumMatch.Groups[1].Value.ToUpperInvariant() } ], bad
            elif isHexString 64 64 line then
              digs @ [ { Path = sourceLabel; Sha256 = line.ToUpperInvariant() } ], bad
            else
              digs, true) ([], false)

  let private indexRuleArray (rulesValOpt: JsonValue option) : Map<string, string> * Map<int, string * string> =
    match rulesValOpt with
    | Some(JsonValue.Array rules) ->
      rules |> List.indexed |> List.fold (fun (byId, byIdx) (idx, rule) ->
        let lvl = tryProp "defaultConfiguration" rule |> Option.bind (tryNonBlankStringProp [ "level" ]) |> Option.defaultValue "warning" |> _.ToLowerInvariant()
        match tryNonBlankStringProp [ "id" ] rule with
        | Some id -> Map.add id lvl byId, Map.add idx (id, lvl) byIdx
        | None -> byId, byIdx) (Map.empty, Map.empty)
    | _ -> Map.empty, Map.empty

  let private extractSarifRuleMeta (runObj: JsonValue) =
    let toolObj = tryProp "tool" runObj
    let driverRules = toolObj |> Option.bind (tryProp "driver") |> Option.bind (tryProp "rules") |> indexRuleArray
    let extByName, extByIdx =
      match toolObj |> Option.bind (tryProp "extensions") with
      | Some(JsonValue.Array exts) ->
        exts |> List.indexed |> List.fold (fun (byName, byIdx) (idx, ext) ->
          let meta = indexRuleArray (tryProp "rules" ext)
          let byName' = match tryNonBlankStringProp [ "name"; "guid" ] ext with Some n -> Map.add (n.ToLowerInvariant()) meta byName | None -> byName
          byName', Map.add idx meta byIdx) (Map.empty, Map.empty)
      | _ -> Map.empty, Map.empty
    driverRules, extByName, extByIdx

  let private parseSarifResult
      (driverRules: Map<string, string> * Map<int, string * string>)
      (extByName: Map<string, Map<string, string> * Map<int, string * string>>)
      (extByIdx: Map<int, Map<string, string> * Map<int, string * string>>)
      (res: JsonValue)
      : Result<AnalyzerDiagnostic * Axd000Digest list * bool, PacketParseError> =
    match res with
    | JsonValue.Object _ ->
      let ruleObj = tryProp "rule" res
      let toolCompObj = ruleObj |> Option.bind (tryProp "toolComponent")
      let targetRulesRes =
        match toolCompObj with
        | None | Some JsonValue.Null -> Ok driverRules
        | Some comp ->
          let byName = tryNonBlankStringProp [ "name"; "guid" ] comp |> Option.bind (fun n -> Map.tryFind (n.ToLowerInvariant()) extByName)
          let byIdx = tryInt32Prop [ "index" ] comp |> Option.bind (fun i -> Map.tryFind i extByIdx)
          match byName |> Option.orElse byIdx with
          | Some m -> Ok m
          | None -> Error(PacketParseError.InvalidJsonField("Analyzer SARIF run", "results", "result references unresolved SARIF toolComponent extension"))
      targetRulesRes
      |> Result.bind (fun (ruleLevels, rulesByIndex) ->
        let ruleIndexOpt = tryInt32Prop [ "ruleIndex" ] res |> Option.orElseWith (fun () -> ruleObj |> Option.bind (tryInt32Prop [ "index" ]))
        let indexedMeta = ruleIndexOpt |> Option.bind (fun idx -> Map.tryFind idx rulesByIndex)
        let ruleId =
          tryNonBlankStringProp [ "ruleId" ] res
          |> Option.orElseWith (fun () -> ruleObj |> Option.bind (tryNonBlankStringProp [ "id" ]))
          |> Option.orElseWith (fun () -> indexedMeta |> Option.map fst)
          |> Option.defaultValue ""
        let levelRes =
          match tryNonBlankStringProp [ "level" ] res with
          | Some l -> Ok(l.ToLowerInvariant())
          | None ->
            match Map.tryFind ruleId ruleLevels |> Option.orElseWith (fun () -> indexedMeta |> Option.map snd) with
            | Some l -> Ok l
            | None when ruleIndexOpt.IsSome || toolCompObj.IsSome ->
              Error(PacketParseError.InvalidJsonField("Analyzer SARIF run", "results", "result omits level and references unresolved ruleIndex/toolComponent"))
            | None -> Ok "warning"
        levelRes |> Result.map (fun level ->
          let message = tryProp "message" res |> Option.bind (tryStringProp [ "text"; "markdown" ]) |> Option.defaultValue ""
          let locFile, locLine =
            match tryProp "locations" res with
            | Some(JsonValue.Array (firstLoc :: _)) ->
              let phys = tryProp "physicalLocation" firstLoc
              let file = phys |> Option.bind (tryProp "artifactLocation") |> Option.bind (tryNonBlankStringProp [ "uri" ]) |> Option.defaultValue ""
              let line = phys |> Option.bind (tryProp "region") |> Option.bind (tryInt32Prop [ "startLine" ])
              file, line
            | _ -> "", None
          let diag = { RuleId = ruleId; Level = level; File = locFile; Line = locLine; Message = message; IsTargetRedRule = level = "error" }
          if String.Equals(ruleId, "AXD000", StringComparison.OrdinalIgnoreCase) then
            let label = if String.IsNullOrWhiteSpace locFile then "AXD000" else locFile
            let digs, bad = parseAxd000PayloadText label message
            diag, digs, bad
          else diag, [], false))
    | _ -> Error(PacketParseError.InvalidJsonField("Analyzer SARIF run", "results", "each result entry must be a JSON object"))

  let private checkSarifInvocations (toolName: string) (runObj: JsonValue) : Result<AnalyzerDiagnostic list, PacketParseError> =
    let mkRed id msg =
      { RuleId = id; Level = "error"; File = toolName; Line = None; Message = msg; IsTargetRedRule = true }
    match tryProp "invocations" runObj with
    | None | Some JsonValue.Null -> Ok []
    | Some(JsonValue.Array invs) ->
      let rec loop acc remaining =
        match remaining with
        | [] -> Ok(List.rev acc)
        | (JsonValue.Object _ as inv) :: rest ->
          let failedDiag =
            match tryBoolProp [ "executionSuccessful" ] inv with
            | Some false -> [ mkRed "SARIF_INVOCATION_FAILED" (sprintf "SARIF run for '%s' reported executionSuccessful=false." toolName) ]
            | _ -> []
          let notifDiags =
            match tryProp "toolExecutionNotifications" inv with
            | Some(JsonValue.Array notes) ->
              notes
              |> List.choose (fun n ->
                let lvl = tryNonBlankStringProp [ "level" ] n |> Option.map _.ToLowerInvariant() |> Option.defaultValue "warning"
                if lvl = "error" then
                  let msg = tryProp "message" n |> Option.bind (tryStringProp [ "text"; "markdown" ]) |> Option.defaultValue "SARIF tool execution notification error"
                  Some(mkRed "SARIF_EXECUTION_ERROR" msg)
                else None)
            | _ -> []
          loop (notifDiags @ failedDiag @ acc) rest
        | _ :: _ ->
          Error(PacketParseError.InvalidJsonField("Analyzer SARIF run", "invocations", "each invocation must be a JSON object"))
      loop [] invs
    | Some _ ->
      Error(PacketParseError.InvalidJsonField("Analyzer SARIF run", "invocations", "expected JSON array"))

  let parseSarifAndAxd000
      (sarifJsonOpt: string option)
      (axd000TextOpt: string option)
      : Result<AnalyzerReceipt option, PacketParseError> =
    match sarifJsonOpt, axd000TextOpt with
    | None, None -> Ok None
    | None, Some _ -> Error PacketParseError.MissingAxd000SarifPartner
    | Some sarifJson, _ ->
      parseJsonValue "Analyzer SARIF" sarifJson
      |> Result.bind (fun root ->
        match root with
        | JsonValue.Object _ ->
          match tryProp "runs" root with
          | None -> Error(PacketParseError.MissingJsonField("Analyzer SARIF", "runs"))
          | Some(JsonValue.Array []) ->
            Error(PacketParseError.InvalidJsonField("Analyzer SARIF", "runs", "must contain at least one completed analyzer run"))
          | Some(JsonValue.Array runsList) ->
            let rec parseRuns toolsAcc diagsAcc digsAcc revsAcc badAcc remainingRuns =
              match remainingRuns with
              | [] -> Ok(List.rev toolsAcc |> List.distinct, List.rev diagsAcc, List.rev digsAcc, List.rev revsAcc |> List.distinct, badAcc)
              | run :: restRuns ->
                match run with
                | JsonValue.Object _ ->
                  match tryProp "tool" run |> Option.bind (tryProp "driver") |> Option.bind (tryNonBlankStringProp [ "name" ]) with
                  | None -> Error(PacketParseError.MissingJsonField("Analyzer SARIF run", "tool.driver.name"))
                  | Some toolName ->
                    let runRevs =
                      match tryProp "versionControlProvenance" run with
                      | Some(JsonValue.Array vcs) -> vcs |> List.choose (tryNonBlankStringProp [ "revisionId"; "revision" ])
                      | _ -> []
                    match checkSarifInvocations toolName run with
                    | Error err -> Error err
                    | Ok invDiags ->
                      let driverRules, extByName, extByIdx = extractSarifRuleMeta run
                      match tryProp "results" run with
                      | None -> Error(PacketParseError.MissingJsonField("Analyzer SARIF run", "results"))
                      | Some(JsonValue.Array resultItems) ->
                        let rec parseResults dAcc axAcc bAcc remRes =
                          match remRes with
                          | [] -> Ok(dAcc, axAcc, bAcc)
                          | rItem :: rTail ->
                            match parseSarifResult driverRules extByName extByIdx rItem with
                            | Error err -> Error err
                            | Ok(diag, digs, bad) -> parseResults (diag :: dAcc) (List.rev digs @ axAcc) (bAcc || bad) rTail
                        match parseResults (invDiags @ diagsAcc) digsAcc badAcc resultItems with
                        | Error err -> Error err
                        | Ok(dAcc', axAcc', bAcc') -> parseRuns (toolName :: toolsAcc) dAcc' axAcc' (List.rev runRevs @ revsAcc) bAcc' restRuns
                      | Some _ -> Error(PacketParseError.InvalidJsonField("Analyzer SARIF run", "results", "expected JSON array"))
                | _ -> Error(PacketParseError.InvalidJsonField("Analyzer SARIF", "runs", "each run must be a JSON object"))
            parseRuns [] [] [] [] false runsList
            |> Result.map (fun (tools, diagnostics, sarifAxdDigests, revIds, sarifInvalidAxd) ->
              let fileDigests, fileInvalidAxd =
                match axd000TextOpt with
                | None -> [], false
                | Some raw -> parseAxd000PayloadText "axd000-receipt" raw
              let disagreement =
                if not (List.isEmpty sarifAxdDigests) && not (List.isEmpty fileDigests) then
                  let sarifShas = sarifAxdDigests |> List.map _.Sha256 |> Set.ofList
                  let fileShas = fileDigests |> List.map _.Sha256 |> Set.ofList
                  if sarifShas <> fileShas then Some "SARIF AXD000 digest set disagrees with standalone --axd000 receipt."
                  else None
                else None
              let allDigests = (sarifAxdDigests @ fileDigests) |> List.distinctBy (fun x -> x.Path, x.Sha256)
              let hasInvalid = sarifInvalidAxd || fileInvalidAxd || disagreement.IsSome
              let axdValid = (not (List.isEmpty allDigests)) && not hasInvalid
              let errorCount = diagnostics |> List.filter (fun x -> x.Level = "error") |> List.length
              let warningCount = diagnostics |> List.filter (fun x -> x.Level = "warning") |> List.length
              let noteCount = diagnostics |> List.filter (fun x -> x.Level = "note" || x.Level = "none") |> List.length
              Some {
                Tools = tools; TotalDiagnostics = diagnostics.Length; ErrorCount = errorCount
                WarningCount = warningCount; NoteCount = noteCount
                RedTargetDiagnostics = diagnostics |> List.filter _.IsTargetRedRule
                AllDiagnostics = diagnostics; Axd000Digests = allDigests
                RevisionIds = revIds; Axd000Disagreement = disagreement
                HasInvalidAxd000Entry = hasInvalid; Axd000Valid = axdValid; RawSarif = sarifJson
              })
          | Some _ ->
            Error(PacketParseError.InvalidJsonField("Analyzer SARIF", "runs", "expected JSON array"))
        | _ -> Error(PacketParseError.ExpectedJsonObject "Analyzer SARIF"))

  // ---------------------------------------------------------------------------
  // Strict Test Compilation Receipt parser
  // ---------------------------------------------------------------------------

  let private isNothingRanVerdict (vOpt: string option) : bool =
    match vOpt with
    | Some v ->
      let lower = v.Trim().ToLowerInvariant()
      lower = "nothingran" || lower = "nothing_ran" || lower = "notests" || lower = "skipped"
    | None -> false

  let private parseTestReceiptJson (raw: string) : Result<TestReceipt, PacketParseError> =
    parseJsonValue "Test receipt JSON" raw
    |> Result.bind (fun root ->
      match root with
      | JsonValue.Object _ ->
        let explicitOk = tryBoolProp [ "compileSucceeded"; "compile_succeeded"; "compileOk"; "compile_ok"; "buildSucceeded"; "build_succeeded" ] root
        let explicitFailed = tryBoolProp [ "compileFailed"; "compile_failed"; "buildFailed"; "build_failed" ] root
        let compileExitCode = tryInt32Prop [ "compileExitCode"; "compile_exit_code" ] root
        let genericExitCode = tryInt32Prop [ "exitCode"; "exit_code"; "ExitCode" ] root
        let exitCode = compileExitCode |> Option.orElse genericExitCode
        let verdict = tryNonBlankStringProp [ "verdict"; "Verdict"; "status" ] root
        let passed = tryInt32Prop [ "passed"; "Passed" ] root
        let failed = tryInt32Prop [ "failed"; "Failed" ] root
        let errored = tryInt32Prop [ "errored"; "Errored" ] root
        let ran = tryInt32Prop [ "ran"; "Ran" ] root
        let totalExecuted =
          match ran with
          | Some r -> r
          | None ->
            (passed |> Option.defaultValue 0)
            + (failed |> Option.defaultValue 0)
            + (errored |> Option.defaultValue 0)
        let summary =
          tryNonBlankStringProp [ "summary"; "error"; "message"; "detail" ] root
          |> Option.defaultWith (fun () ->
            sprintf "verdict=%s exitCode=%s"
              (verdict |> Option.defaultValue "none")
              (exitCode |> Option.map string |> Option.defaultValue "none"))
        let compileBadByVerdict =
          match verdict with
          | Some v ->
            let lower = v.ToLowerInvariant()
            lower.Contains("compilefail")
            || lower.Contains("buildfail")
            || lower = "compile_failed"
            || lower = "build_failed"
          | None -> false
        let hasCompileErrorText =
          Regex.IsMatch(summary, @"(Build FAILED|\berror\s+(?:FS|CS|MSB)\d{4}\b|compilation failed)", RegexOptions.IgnoreCase)
        let mkReceipt okFlag =
          Ok {
            CompileSucceeded = okFlag
            ExitCode = exitCode
            Verdict = verdict
            Passed = passed
            Failed = failed
            Errored = errored
            Summary = summary
          }
        let compileExitNonZero =
          match compileExitCode with
          | Some c when c <> 0 -> true
          | _ -> false
        if explicitOk = Some false || explicitFailed = Some true || compileExitNonZero || compileBadByVerdict || hasCompileErrorText then
          mkReceipt false
        elif isNothingRanVerdict verdict then
          Error(PacketParseError.InvalidTestReceipt "Test receipt reports NothingRan without explicit compile-success or test-execution evidence.")
        else
          match genericExitCode with
          | Some c when c <> 0 ->
            match verdict with
            | Some v when String.Equals(v, "TestsFailed", StringComparison.OrdinalIgnoreCase) && totalExecuted > 0 ->
              mkReceipt true
            | _ -> mkReceipt false
          | _ ->
            let trustedVerdictOk =
              match verdict with
              | Some v ->
                let lower = v.Trim().ToLowerInvariant()
                lower = "trusted"
                || lower = "narrowedrun"
                || lower = "narrowed_run"
                || (lower = "testsfailed" && totalExecuted > 0)
              | None -> false
            let hasExplicitSuccessEvidence =
              explicitOk = Some true
              || compileExitCode = Some 0
              || trustedVerdictOk
            if hasExplicitSuccessEvidence then mkReceipt true
            else
              Error(PacketParseError.InvalidTestReceipt "Test receipt JSON lacks explicit compile-success, compile-failure, or TRUST verdict evidence.")
      | _ -> Error(PacketParseError.ExpectedJsonObject "Test receipt JSON"))

  let private parseIntField (pattern: string) (line: string) : int option =
    let m = Regex.Match(line, pattern)
    if m.Success then
      match Int32.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
      | true, n -> Some n
      | _ -> None
    else None

  let private parseAllIntFields (pattern: string) (text: string) : int list =
    Regex.Matches(text, pattern)
    |> Seq.cast<Match>
    |> Seq.choose (fun m ->
      match Int32.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
      | true, n -> Some n
      | _ -> None)
    |> Seq.toList

  let private parseLastIntField (pattern: string) (text: string) : int option =
    parseAllIntFields pattern text |> List.tryLast

  let parseTestReceipt (raw: string) : Result<TestReceipt, PacketParseError> =
    if String.IsNullOrWhiteSpace raw then
      Error(PacketParseError.EmptyInput "Test receipt")
    else
      let trimmed = raw.TrimStart()
      if trimmed.StartsWith("{", StringComparison.Ordinal) then
        parseTestReceiptJson raw
      else
        let cleanLines =
          raw.Replace("\r\n", "\n").Split('\n')
          |> Array.map (fun (l: string) -> Regex.Replace(l, @"\x1B\[[0-9;]*[A-Za-z]", "").Trim())
        let cleanText = String.concat "\n" cleanLines
        let allCompileExitCodes = parseAllIntFields @"\bcompileExitCode=(-?\d+)\b" cleanText
        let compileExitCode =
          allCompileExitCodes
          |> List.tryFind (fun c -> c <> 0)
          |> Option.orElseWith (fun () -> List.tryLast allCompileExitCodes)
        let genericExitCode = parseLastIntField @"\bexitCode=(-?\d+)\b" cleanText
        let exitCode = compileExitCode |> Option.orElse genericExitCode
        let trustLineOpt =
          cleanLines
          |> Array.tryFindBack (fun (l: string) -> Regex.IsMatch(l, @"^TRUST\s+tier=\S+"))
        let verdict =
          trustLineOpt
          |> Option.bind (fun line ->
            let m = Regex.Match(line, @"\bverdict=([^\s(]+)")
            if m.Success then Some m.Groups[1].Value else None)
        let trustRegistered = trustLineOpt |> Option.bind (parseIntField @"\bregistered=(\d+)\b")
        let trustRan = trustLineOpt |> Option.bind (parseIntField @"\bran=(\d+)\b")
        let trustPassed = trustLineOpt |> Option.bind (parseIntField @"\bpassed=(\d+)\b")
        let trustFailed = trustLineOpt |> Option.bind (parseIntField @"\bfailed=(\d+)\b")
        let trustErrored = trustLineOpt |> Option.bind (parseIntField @"\berrored=(\d+)\b")
        let hasValidTrustSummary =
          match verdict, trustRegistered, trustRan, trustPassed, trustFailed, trustErrored with
          | Some v, Some reg, Some ran, Some _, Some _, Some _ when reg > 0 && ran > 0 ->
            let lower = v.Trim().ToLowerInvariant()
            lower = "trusted" || lower = "narrowedrun" || lower = "narrowed_run" || lower = "testsfailed"
          | _ -> false
        let hasBuildSucceeded =
          cleanLines |> Array.exists (fun (l: string) -> Regex.IsMatch(l, @"^Build succeeded\.", RegexOptions.IgnoreCase))
        let hasHardBuildFailed =
          cleanLines
          |> Array.exists (fun (l: string) ->
            Regex.IsMatch(l, @"^(?:Build FAILED\.?|The project .* failed to build|compilation failed)", RegexOptions.IgnoreCase))
        let expectoRan =
          cleanLines
          |> Array.exists (fun (l: string) ->
            let m = Regex.Match(l, @"^(?:\[[^\]]+\]\s*)?EXPECTO!\s+.*?(\d+)\s+tests?\s+run", RegexOptions.IgnoreCase)
            if m.Success then
              match Int32.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
              | true, n -> n > 0
              | _ -> false
            else false)
        let dotnetTestRan =
          cleanLines
          |> Array.exists (fun (l: string) ->
            let m = Regex.Match(l, @"^(?:Passed|Failed)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+)", RegexOptions.IgnoreCase)
            if m.Success then
              match Int32.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    Int32.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
              | (true, f), (true, p) -> (f + p) > 0
              | _ -> false
            else false)
        let hasBuildError =
          hasHardBuildFailed
          || Regex.IsMatch(cleanText, @"\berror\s+(?:FS|CS|MSB)\d{4}\b", RegexOptions.IgnoreCase)
        let failSummary () =
          cleanLines
          |> Array.tryFind (fun (l: string) ->
            l.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(l, @"\berror\s+(?:FS|CS|MSB)\d{4}\b", RegexOptions.IgnoreCase))
          |> Option.defaultValue "Test compilation failed"
        let compileExitNonZero =
          match compileExitCode with
          | Some c when c <> 0 -> true
          | _ -> false
        if compileExitNonZero || hasHardBuildFailed then
          Ok { CompileSucceeded = false; ExitCode = exitCode; Verdict = verdict; Passed = trustPassed; Failed = trustFailed; Errored = trustErrored; Summary = failSummary () }
        elif hasValidTrustSummary then
          Ok { CompileSucceeded = true; ExitCode = exitCode; Verdict = verdict; Passed = trustPassed; Failed = trustFailed; Errored = trustErrored; Summary = verdict |> Option.defaultValue "TRUST summary verified" }
        elif hasBuildError then
          Ok { CompileSucceeded = false; ExitCode = exitCode; Verdict = verdict; Passed = trustPassed; Failed = trustFailed; Errored = trustErrored; Summary = failSummary () }
        elif isNothingRanVerdict verdict || Regex.IsMatch(cleanText, @"\bNothingRan\b") then
          Error(PacketParseError.InvalidTestReceipt "Test receipt reports NothingRan without explicit compile-success or test-execution evidence.")
        else
          match genericExitCode with
          | Some c when c <> 0 ->
            Ok { CompileSucceeded = false; ExitCode = exitCode; Verdict = verdict; Passed = trustPassed; Failed = trustFailed; Errored = trustErrored; Summary = failSummary () }
          | _ ->
            if hasBuildSucceeded || expectoRan || dotnetTestRan then
              let summary =
                if hasBuildSucceeded then "Build succeeded" else "Test execution receipt verified"
              Ok { CompileSucceeded = true; ExitCode = exitCode; Verdict = verdict; Passed = trustPassed; Failed = trustFailed; Errored = trustErrored; Summary = summary }
            else
              Error(PacketParseError.InvalidTestReceipt "Test receipt text lacks terminal Build succeeded., compile failure, or TRUST/test-execution evidence.")

  // ---------------------------------------------------------------------------
  // Retrieval snapshot & clef-search Spec Ballast parsers
  // ---------------------------------------------------------------------------

  let private parseRevisions (elOpt: JsonValue option) : RetrievalRevisionEntry list =
    match elOpt with
    | Some(JsonValue.Object pairs) ->
      pairs
      |> List.map (fun (source, value) -> { Source = source; Revision = tryNonBlankString value })
      |> List.sortBy _.Source
    | _ -> []

  let private matchesRevisionSource (expectedRepo: string) (sourceKey: string) : bool =
    let normalize (source: string) =
      let name = source.Trim().ToLowerInvariant()
      if name.EndsWith(".git", StringComparison.Ordinal) && name.Length > 4 then name.Substring(0, name.Length - 4)
      else name
    let bare (name: string) = name.Substring(name.LastIndexOf('/') + 1)
    let req, key = normalize expectedRepo, normalize sourceKey
    req <> "" && key <> "" && (req = key || bare req = key || bare key = req)

  let private matchesRevisionSha (expectedSha: string) (actualOpt: string option) : bool =
    match actualOpt with
    | None -> false
    | Some actual ->
      let exp = expectedSha.Trim()
      let act = actual.Trim()
      if not (isHexString 7 40 exp) || not (isHexString 7 40 act) then
        false
      elif exp.Length = 40 then
        act.Length = 40 && String.Equals(exp, act, StringComparison.OrdinalIgnoreCase)
      else
        act.Length >= exp.Length && act.StartsWith(exp, StringComparison.OrdinalIgnoreCase)

  let private checkRevisionCoverage
      (expectedRepoOpt: string option)
      (expectedShaOpt: string option)
      (revisions: RetrievalRevisionEntry list)
      : string option =
    match expectedShaOpt with
    | None -> None
    | Some expSha ->
      let covered =
        match expectedRepoOpt with
        | Some repo ->
          revisions |> List.exists (fun r -> matchesRevisionSource repo r.Source && matchesRevisionSha expSha r.Revision)
        | None ->
          revisions |> List.exists (fun r -> matchesRevisionSha expSha r.Revision)
      if covered then None
      else
        let repoDesc = expectedRepoOpt |> Option.defaultValue "<any>"
        Some(sprintf "Retrieval snapshot revisions do not cover expected repo='%s' sha='%s'." repoDesc expSha)

  let private validateSnapshotFreshness
      (provenance: RetrievalProvenance)
      (freshFlag: bool)
      (state: string)
      (snapId: string)
      (curSnapId: string option)
      (revisions: RetrievalRevisionEntry list)
      : string option =
    if not freshFlag then
      Some(sprintf "Retrieval snapshot is not fresh (state='%s')." (if String.IsNullOrEmpty state then "unknown" else state))
    elif String.IsNullOrWhiteSpace snapId then
      Some "Retrieval snapshot is missing a pinned snapshot_id."
    else
      match provenance with
      | RetrievalProvenance.SnapshotFile(_, expectedRepoOpt, expectedShaOpt) ->
        match curSnapId with
        | None ->
          Some "Retrieval file snapshot is missing current_snapshot_id to verify snapshot_id is current."
        | Some cur when not (String.Equals(snapId, cur, StringComparison.Ordinal)) ->
          Some(sprintf "Snapshot ID mismatch: snapshot_id='%s' <> current_snapshot_id='%s'" snapId cur)
        | Some _ ->
          checkRevisionCoverage expectedRepoOpt expectedShaOpt revisions
      | RetrievalProvenance.LiveAwait(_, expectedRepoOpt, expectedShaOpt) ->
        match curSnapId with
        | Some cur when not (String.Equals(snapId, cur, StringComparison.Ordinal)) ->
          Some(sprintf "Snapshot ID mismatch: snapshot_id='%s' <> current_snapshot_id='%s'" snapId cur)
        | _ ->
          checkRevisionCoverage expectedRepoOpt expectedShaOpt revisions

  let parseRetrievalSnapshotWithProvenance (provenance: RetrievalProvenance) (raw: string) : Result<RetrievalSnapshot, PacketParseError> =
    parseJsonValue "Retrieval snapshot JSON" raw
    |> Result.bind (fun root ->
      match root with
      | JsonValue.Object _ ->
        match tryProp "freshness" root with
        | Some(JsonValue.Object _ as freshnessEl) ->
          let freshFlag = tryBoolProp [ "fresh" ] freshnessEl |> Option.defaultValue false
          let snapId =
            tryNonBlankStringProp [ "snapshot_id"; "snapshotId" ] freshnessEl
            |> Option.orElseWith (fun () -> tryNonBlankStringProp [ "snapshot_id"; "snapshotId" ] root)
            |> Option.defaultValue ""
          let curSnapId =
            tryNonBlankStringProp [ "current_snapshot_id"; "currentSnapshotId" ] freshnessEl
            |> Option.orElseWith (fun () -> tryNonBlankStringProp [ "current_snapshot_id"; "currentSnapshotId" ] root)
          let state = tryNonBlankStringProp [ "state" ] root |> Option.defaultValue (if freshFlag then "fresh" else "stale")
          let revisions =
            tryProp "revisions" root
            |> Option.orElseWith (fun () -> tryProp "revisions" freshnessEl)
            |> parseRevisions
          let invalidReason = validateSnapshotFreshness provenance freshFlag state snapId curSnapId revisions
          Ok {
            Fresh = freshFlag
            State = state
            SnapshotId = snapId
            CurrentSnapshotId = curSnapId
            Generation = tryInt64Prop [ "generation" ] root
            BranchName = tryNonBlankStringProp [ "branch"; "branchName" ] root
            BranchStatus = tryNonBlankStringProp [ "branchStatus"; "branch_status" ] root
            OnMain = tryBoolProp [ "onMain"; "on_main" ] root
            Revisions = revisions
            Note = tryNonBlankStringProp [ "note"; "warning" ] root
            IsValidFresh = invalidReason.IsNone
            InvalidReason = invalidReason
            Provenance = provenance
          }
        | _ ->
          let statusObj =
            match tryProp "status" root with
            | Some(JsonValue.Object _ as inner) -> inner
            | _ -> root
          let okEnvelope = tryBoolProp [ "ok" ] root
          let state =
            tryNonBlankStringProp [ "state" ] statusObj
            |> Option.orElseWith (fun () ->
              match tryBoolProp [ "fresh" ] statusObj with
              | Some true -> Some "fresh"
              | Some false -> Some "stale"
              | None -> None)
            |> Option.defaultValue ""
          let freshFlag =
            match tryBoolProp [ "fresh" ] statusObj with
            | Some b -> b && (okEnvelope <> Some false)
            | None -> String.Equals(state, "fresh", StringComparison.OrdinalIgnoreCase) && (okEnvelope <> Some false)
          let snapId = tryNonBlankStringProp [ "snapshotId"; "snapshot_id" ] statusObj |> Option.defaultValue ""
          let curSnapId = tryNonBlankStringProp [ "currentSnapshotId"; "current_snapshot_id" ] statusObj
          if String.IsNullOrWhiteSpace state && String.IsNullOrWhiteSpace snapId && okEnvelope.IsNone then
            Error(PacketParseError.MissingJsonField("Retrieval snapshot JSON", "freshness / status / state"))
          else
            let resolvedState = if String.IsNullOrEmpty state then (if freshFlag then "fresh" else "stale") else state
            let revisions =
              tryProp "revisions" statusObj
              |> Option.orElseWith (fun () -> tryProp "revisions" root)
              |> parseRevisions
            let invalidReason = validateSnapshotFreshness provenance freshFlag resolvedState snapId curSnapId revisions
            Ok {
              Fresh = freshFlag
              State = resolvedState
              SnapshotId = snapId
              CurrentSnapshotId = curSnapId
              Generation = tryInt64Prop [ "generation" ] statusObj
              BranchName = tryNonBlankStringProp [ "branch"; "branchName" ] statusObj
              BranchStatus = tryNonBlankStringProp [ "branchStatus"; "branch_status" ] statusObj
              OnMain = tryBoolProp [ "onMain"; "on_main" ] statusObj
              Revisions = revisions
              Note =
                tryNonBlankStringProp [ "note" ] statusObj
                |> Option.orElseWith (fun () -> tryNonBlankStringProp [ "warning"; "error" ] root)
              IsValidFresh = invalidReason.IsNone
              InvalidReason = invalidReason
              Provenance = provenance
            }
      | _ -> Error(PacketParseError.ExpectedJsonObject "Retrieval snapshot JSON"))

  let parseRetrievalSnapshot (raw: string) : Result<RetrievalSnapshot, PacketParseError> =
    parseRetrievalSnapshotWithProvenance (RetrievalProvenance.SnapshotFile("<inline>", None, None)) raw

  let private publicSpecVocabulary = [
    "nanopass"; "psg"; "baker"; "alex"; "mlir"; "barewire"; "farscape"; "ntu"
    "range"; "proof"; "cvc5"; "witness"; "zipper"; "arena"; "escape"; "region"
    "liveness"; "ffi"; "extern"; "marshal"; "differential"; "incremental"
    "saturation"; "clef"; "fidproj"; "bozzetto"; "fanion"; "sarif"; "canary"
    "watchdog"; "retrieval"; "cohort"; "ranvier"; "zone a"; "zone b"; "memory"
    "numeric"; "spatial"; "llvm"; "gpu"; "npu"; "fpga"; "jsir"; "javascript"
  ]

  let deriveSpecBallastQuery (issueOpt: IssueAuthority option) (modifiedPaths: string list) : string =
    let haystack =
      let issueText = match issueOpt with Some i -> i.Title + " " + i.Body | None -> ""
      (issueText + " " + String.concat " " modifiedPaths).ToLowerInvariant()
    let matched =
      publicSpecVocabulary
      |> List.filter (fun term -> Regex.IsMatch(haystack, sprintf @"\b%s\b" (Regex.Escape term)))
      |> List.truncate 8
    match matched with
    | [] -> "Clef language design and Zone A pure core compiler specification"
    | terms -> sprintf "Clef language specification and compiler invariants: %s" (String.concat " " terms)

  let private isCanonicalClefLangOrigin (parsed: ParsedHttpUrl) : bool =
    parsed.Scheme = "https"
    && (String.Equals(parsed.Origin, "https://clef-lang.com", StringComparison.OrdinalIgnoreCase)
        || (parsed.Host.EndsWith(".clef-lang.com", StringComparison.OrdinalIgnoreCase)
            && String.Equals(parsed.Origin, "https://" + parsed.Host.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))

  let private tryNormalizeClefLangUrl (rawPageUrl: string) : string option =
    match nonEmptyOpt rawPageUrl with
    | None -> None
    | Some trimmed ->
      if trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
         || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
        match tryParseHttpUrl trimmed with
        | Some parsed when isCanonicalClefLangOrigin parsed && not (List.isEmpty parsed.PathSegments) ->
          Some trimmed
        | _ -> None
      elif trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.Contains("://", StringComparison.Ordinal) then
        None
      else
        let candidate = "https://clef-lang.com/" + trimmed.TrimStart('/')
        match tryParseHttpUrl candidate with
        | Some parsed when isCanonicalClefLangOrigin parsed && not (List.isEmpty parsed.PathSegments) ->
          Some candidate
        | _ -> None

  let internal isValidSpecBallastHit (hit: SpecBallastHit) : bool =
    not (String.IsNullOrWhiteSpace hit.Snippet)
    && (match tryParseHttpUrl hit.Url with
        | Some parsed -> isCanonicalClefLangOrigin parsed && not (List.isEmpty parsed.PathSegments)
        | None -> false)

  let parseClefSearchResponse (query: string) (raw: string) : Result<SpecBallastStatus, PacketParseError> =
    parseJsonValue "clef-search response JSON" raw
    |> Result.bind (fun root ->
      match root with
      | JsonValue.Object _ ->
        match tryProp "results" root with
        | Some(JsonValue.Array items) ->
          let fusion = tryNonBlankStringProp [ "fusionMethod"; "fusion" ] root |> Option.defaultValue "hybrid"
          let truncate (maxLen: int) (s: string) =
            if s.Length <= maxLen then s else s.Substring(0, maxLen) + "..."
          let rec parseHits acc remaining =
            match remaining with
            | [] -> Ok(List.rev acc |> List.truncate 5)
            | row :: rest ->
              match row with
              | JsonValue.Object _ ->
                match tryNonBlankStringProp [ "pageUrl"; "url" ] row |> Option.bind tryNormalizeClefLangUrl,
                      tryNonBlankStringProp [ "snippet"; "excerpt" ] row with
                | Some url, Some rawSnippet ->
                  let title = tryNonBlankStringProp [ "pageTitle"; "title" ] row |> Option.defaultValue "Untitled"
                  let sec = tryNonBlankStringProp [ "sectionTitle"; "section" ] row |> Option.defaultValue ""
                  let contentType = tryNonBlankStringProp [ "contentType"; "content_type" ] row |> Option.defaultValue ""
                  let hit = {
                    Title = title
                    Section = sec
                    Url = url
                    ContentType = contentType
                    Snippet = truncate 500 rawSnippet
                  }
                  parseHits (hit :: acc) rest
                | None, _ ->
                  Error(PacketParseError.InvalidJsonField("clef-search response JSON", "results", "each hit must have a valid https://clef-lang.com page URL"))
                | _, None ->
                  Error(PacketParseError.InvalidJsonField("clef-search response JSON", "results", "each hit must have a non-blank snippet/excerpt"))
              | _ ->
                Error(PacketParseError.InvalidJsonField("clef-search response JSON", "results", "each hit must be a JSON object"))
          parseHits [] items
          |> Result.map (fun hits -> SpecBallastStatus.Loaded(query, fusion, hits))
        | _ -> Error(PacketParseError.MissingJsonField("clef-search response JSON", "results"))
      | _ -> Error(PacketParseError.ExpectedJsonObject "clef-search response JSON"))
