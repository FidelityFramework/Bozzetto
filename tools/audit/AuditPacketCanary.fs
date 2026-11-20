namespace Bozzetto.Audit

open System
open System.Globalization
open System.Text.RegularExpressions
open Fidelity.Data.JSON

// Pure Zone A Part 5 live .fidproj canary receipt parser (JSON + WATCHDOG text)
// and scaling-slope / allocation regression comparator.
[<AutoOpen>]
module AuditPacketCanary =

  let private tryPairListFloat (value: JsonValue) : (string * float) list =
    match value with
    | JsonValue.Object pairs ->
      pairs |> List.choose (fun (k, v) -> tryFloat v |> Option.map (fun n -> k, n))
    | _ -> []

  let private tryPairListInt64 (value: JsonValue) : (string * int64) list =
    match value with
    | JsonValue.Object pairs ->
      pairs |> List.choose (fun (k, v) -> tryInt64 v |> Option.map (fun n -> k, n))
    | _ -> []

  let private parseOptionalNonNegInt64Prop (keys: string list) (propDesc: string) (objVal: JsonValue) : Result<int64 option, PacketParseError> =
    match tryPropAny keys objVal with
    | None -> Ok None
    | Some rawVal ->
      match tryInt64 rawVal with
      | Some n when n >= 0L -> Ok(Some n)
      | _ ->
        Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", sprintf "malformed %s (expected non-negative integer)" propDesc))

  let private parseOptionalNonNegFloatProp (keys: string list) (propDesc: string) (objVal: JsonValue) : Result<float option, PacketParseError> =
    match tryPropAny keys objVal with
    | None -> Ok None
    | Some rawVal ->
      match tryFloat rawVal with
      | Some n when n >= 0.0 -> Ok(Some n)
      | _ ->
        Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", sprintf "malformed %s (expected non-negative finite number)" propDesc))

  let private parseScalingPassesStrict (elOpt: JsonValue option) : Result<ScalingPassMetric list, PacketParseError> =
    match elOpt with
    | None | Some JsonValue.Null -> Ok []
    | Some(JsonValue.Array items) ->
      let rec loop acc remaining =
        match remaining with
        | [] -> Ok(List.rev acc)
        | item :: rest ->
          match item with
          | JsonValue.Object _ ->
            match tryNonBlankStringProp [ "pass"; "passName"; "pass_name"; "name" ] item with
            | None ->
              Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", "scaling pass entry missing pass name"))
            | Some name ->
              let a1Res = parseOptionalNonNegInt64Prop [ "alloc1xBytes"; "alloc_1x_bytes"; "scale1xAllocBytes"; "scale_1x_alloc_bytes"; "alloc1x" ] "alloc1x" item
              let a4Res = parseOptionalNonNegInt64Prop [ "alloc4xBytes"; "alloc_4x_bytes"; "scale4xAllocBytes"; "scale_4x_alloc_bytes"; "alloc4x" ] "alloc4x" item
              let slopeRes = parseOptionalNonNegFloatProp [ "slope"; "ratio" ] "slope" item
              match a1Res, a4Res, slopeRes with
              | Error err, _, _ -> Error err
              | _, Error err, _ -> Error err
              | _, _, Error err -> Error err
              | Ok(Some a1), Ok(Some a4), _ ->
                let slope =
                  if a1 > 0L then float a4 / float a1
                  elif a4 > 0L then Double.PositiveInfinity
                  else 0.0
                loop ({ PassName = name; Alloc1xBytes = a1; Alloc4xBytes = a4; Slope = slope } :: acc) rest
              | Ok None, Ok None, Ok(Some s) ->
                loop ({ PassName = name; Alloc1xBytes = 0L; Alloc4xBytes = 0L; Slope = s } :: acc) rest
              | _ ->
                Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", sprintf "pass '%s' has malformed or missing alloc1xBytes/alloc4xBytes/slope" name))
          | _ ->
            Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", "each scaling pass entry must be a JSON object"))
      loop [] items
    | Some _ ->
      Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", "expected JSON array"))

  let private msFromMsOrSecs (msNames: string list) (secNames: string list) (objVal: JsonValue) : float option =
    match tryFloatProp msNames objVal with
    | Some ms -> Some ms
    | None -> tryFloatProp secNames objVal |> Option.map (fun s -> s * 1000.0)

  let private bytesFromBytesOrMb (byteNames: string list) (mbNames: string list) (objVal: JsonValue) : int64 option =
    match tryInt64Prop byteNames objVal with
    | Some b -> Some b
    | None -> tryFloatProp mbNames objVal |> Option.map (fun mb -> int64 (mb * 1_048_576.0))

  let private normalizeOptionalNoneToken (rawOpt: string option) : string option =
    rawOpt
    |> Option.bind nonEmptyOpt
    |> Option.filter (fun s -> not (String.Equals(s, "none", StringComparison.OrdinalIgnoreCase)))

  let private normalizeWatchdogSurvivors (rawOpt: string option) : string option =
    rawOpt
    |> Option.bind nonEmptyOpt
    |> Option.filter (fun s ->
      if String.Equals(s, "none", StringComparison.OrdinalIgnoreCase) then false
      else
        match Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture) with
        | true, 0 -> false
        | _ -> true)

  let private parseWatchdogSurvivorsJson (root: JsonValue) : Result<string option * bool, PacketParseError> =
    match tryPropAny [ "watchdogSurvivors"; "watchdog_survivors"; "survivors" ] root with
    | None | Some JsonValue.Null -> Ok(None, false)
    | Some(JsonValue.String s) ->
      match normalizeWatchdogSurvivors (Some s) with
      | None -> Ok(None, true)
      | Some v -> Ok(Some v, false)
    | Some(JsonValue.Array []) -> Ok(None, true)
    | Some(JsonValue.Array items) ->
      let summary =
        items
        |> List.map (fun item ->
          match tryInt32 item, tryNonBlankString item with
          | Some n, _ -> string n
          | None, Some s -> s
          | None, None -> Json.serialize item)
        |> String.concat ","
      Ok(Some(sprintf "array(%s)" summary), false)
    | Some(JsonValue.Object _) ->
      Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "watchdogSurvivors", "unexpected JSON object for watchdogSurvivors"))
    | Some v ->
      match tryInt32 v with
      | Some 0 -> Ok(None, true)
      | Some n when n > 0 -> Ok(Some(string n), false)
      | _ ->
        Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "watchdogSurvivors", "expected non-negative integer, string, or array"))

  let private parseStrictOptStringProp (fieldLabel: string) (names: string list) (obj: JsonValue) : Result<string option, PacketParseError> =
    match tryPropAny names obj with
    | None | Some JsonValue.Null -> Ok None
    | Some(JsonValue.String s) -> Ok(normalizeOptionalNoneToken (Some s))
    | Some _ -> Error(PacketParseError.InvalidJsonField("Canary receipt JSON", fieldLabel, "expected string or null"))

  let private parseStrictOptBoolProp (fieldLabel: string) (names: string list) (obj: JsonValue) : Result<bool option, PacketParseError> =
    match tryPropAny names obj with
    | None | Some JsonValue.Null -> Ok None
    | Some(JsonValue.Bool b) -> Ok(Some b)
    | Some _ -> Error(PacketParseError.InvalidJsonField("Canary receipt JSON", fieldLabel, "expected boolean or null"))

  let private parseStrictOptIntProp (fieldLabel: string) (names: string list) (obj: JsonValue) : Result<int option, PacketParseError> =
    match tryPropAny names obj with
    | None | Some JsonValue.Null -> Ok None
    | Some v ->
      match tryInt32 v with
      | Some n -> Ok(Some n)
      | None -> Error(PacketParseError.InvalidJsonField("Canary receipt JSON", fieldLabel, "expected integer or null"))

  let private parseCanaryJson (raw: string) : Result<CanaryReceipt, PacketParseError> =
    parseJsonValue "Canary receipt JSON" raw
    |> Result.bind (fun root ->
      match root with
      | JsonValue.Object _ ->
        let countersObj = tryPropAny [ "counters"; "projections"; "budget" ] root |> Option.defaultValue root
        let scalingObj = tryProp "scaling" root |> Option.defaultValue root
        let scale1xObj = tryPropAny [ "scale1x"; "scale_1x"; "Scale_1x" ] scalingObj
        let scale4xObj = tryPropAny [ "scale4x"; "scale_4x"; "Scale_4x" ] scalingObj
        parseScalingPassesStrict (tryPropAny [ "scalingPasses"; "scaling_passes"; "passes" ] scalingObj)
        |> Result.bind (fun scalingPasses ->
          parseWatchdogSurvivorsJson root
          |> Result.bind (fun (watchdogSurvivors, survCleared) ->
            parseStrictOptStringProp "watchdogBreach" [ "watchdogBreach"; "watchdog_breach"; "breach" ] root
            |> Result.bind (fun watchdogBreach ->
              parseStrictOptStringProp "watchdogScanError" [ "watchdogScanError"; "watchdog_scan_error"; "scanError"; "scan_error" ] root
              |> Result.bind (fun watchdogScanError ->
                parseStrictOptBoolProp "failedTestCompile" [ "failedTestCompile"; "failed_test_compile" ] root
                |> Result.bind (fun failedTestCompile ->
                  parseStrictOptIntProp "compileExitCode" [ "compileExitCode"; "compile_exit_code" ] root
                  |> Result.bind (fun compileExitCode ->
                    parseStrictOptBoolProp "watchdogCleared" [ "watchdogCleared"; "watchdog_cleared" ] root
                    |> Result.map (fun explicitCleared ->
                      let hwModules = tryInt32Prop [ "hardwareModules"; "hardware_modules"; "hardwareModuleCount"; "hardware_module_count" ] countersObj
                      let krModules = tryInt32Prop [ "kernelModules"; "kernel_modules"; "kernelModuleCount"; "kernel_module_count" ] countersObj
                      let combinedHwKr =
                        match tryInt32Prop [ "hardwareAndKernelModuleCount"; "hardware_and_kernel_module_count" ] countersObj with
                        | Some n -> Some n
                        | None ->
                          match hwModules, krModules with
                          | Some h, Some k -> Some(h + k)
                          | Some h, None -> Some h
                          | None, Some k -> Some k
                          | None, None -> None
                      let scale1xAlloc =
                        tryInt64Prop [ "scale1xAllocBytes"; "scale_1x_alloc_bytes" ] scalingObj
                        |> Option.orElseWith (fun () -> scale1xObj |> Option.bind (tryInt64Prop [ "allocBytes"; "alloc_bytes"; "gcAllocatedBytes" ]))
                      let scale4xAlloc =
                        tryInt64Prop [ "scale4xAllocBytes"; "scale_4x_alloc_bytes" ] scalingObj
                        |> Option.orElseWith (fun () -> scale4xObj |> Option.bind (tryInt64Prop [ "allocBytes"; "alloc_bytes"; "gcAllocatedBytes" ]))
                      let scale1xWall =
                        msFromMsOrSecs [ "scale1xWallMs"; "scale_1x_wall_ms" ] [ "scale1xWallSeconds"; "scale_1x_wall_seconds" ] scalingObj
                        |> Option.orElseWith (fun () -> scale1xObj |> Option.bind (msFromMsOrSecs [ "wallMs"; "wall_ms" ] [ "wallSeconds"; "wall_seconds" ]))
                      let scale4xWall =
                        msFromMsOrSecs [ "scale4xWallMs"; "scale_4x_wall_ms" ] [ "scale4xWallSeconds"; "scale_4x_wall_seconds" ] scalingObj
                        |> Option.orElseWith (fun () -> scale4xObj |> Option.bind (msFromMsOrSecs [ "wallMs"; "wall_ms" ] [ "wallSeconds"; "wall_seconds" ]))
                      let phaseTimings =
                        tryPropAny [ "phaseTimingsMs"; "phase_timings_ms"; "phaseTimings"; "phase_timings" ] root
                        |> Option.map tryPairListFloat
                        |> Option.defaultValue []
                      let phaseAllocs =
                        tryPropAny [ "phaseAllocBytes"; "phase_alloc_bytes"; "phaseAllocations"; "phase_allocations" ] root
                        |> Option.map tryPairListInt64
                        |> Option.defaultValue []
                      let watchdogCleared =
                        (survCleared || explicitCleared = Some true)
                        && watchdogBreach.IsNone
                        && watchdogSurvivors.IsNone
                        && watchdogScanError.IsNone
                      { Target = tryNonBlankStringProp [ "target"; "canary"; "project" ] root |> Option.defaultValue ""
                        FrontEndMs = msFromMsOrSecs [ "frontEndMs"; "front_end_ms" ] [ "frontEndSeconds"; "front_end_seconds" ] root
                        FrontEndLimitMs = msFromMsOrSecs [ "frontEndLimitMs"; "front_end_limit_ms" ] [ "frontEndLimitSeconds"; "front_end_limit_seconds" ] root
                        ProofDischargeMs = msFromMsOrSecs [ "proofDischargeMs"; "proof_discharge_ms" ] [ "proofDischargeSeconds"; "proof_discharge_seconds" ] root
                        ProofDischargeLimitMs = msFromMsOrSecs [ "proofDischargeLimitMs"; "proof_discharge_limit_ms" ] [ "proofDischargeLimitSeconds"; "proof_discharge_limit_seconds" ] root
                        TotalWallMs = msFromMsOrSecs [ "totalWallMs"; "total_wall_ms"; "wallMs"; "wall_ms"; "elapsedMs" ] [ "wallSeconds"; "wall_seconds"; "elapsedSeconds" ] root
                        WallLimitMs = msFromMsOrSecs [ "wallLimitMs"; "wall_limit_ms" ] [ "wallLimitSeconds"; "wall_limit_seconds" ] root
                        PeakRssBytes = bytesFromBytesOrMb [ "peakRssBytes"; "peak_rss_bytes" ] [ "peakRssMb"; "peakRssMB"; "peak_rss_mb" ] root
                        RssLimitBytes = bytesFromBytesOrMb [ "rssLimitBytes"; "rss_limit_bytes" ] [ "rssLimitMb"; "rssLimitMB"; "rss_limit_mb" ] root
                        NativeExitCode = tryInt32Prop [ "nativeExitCode"; "native_exit_code"; "exitCode"; "exit_code" ] root
                        CompileExitCode = compileExitCode
                        WatchdogBreach = watchdogBreach
                        WatchdogSurvivors = watchdogSurvivors
                        WatchdogScanError = watchdogScanError
                        WatchdogCleared = watchdogCleared
                        TotalAllocBytes = tryInt64Prop [ "totalAllocBytes"; "total_alloc_bytes"; "gcAllocatedBytes"; "gc_allocated_bytes" ] root
                        PhaseTimingsMs = phaseTimings
                        PhaseAllocBytes = phaseAllocs
                        PlatformResolutionReads = tryInt32Prop [ "platformResolutionReads"; "platform_resolution_reads"; "platformResolutionReadCalls" ] countersObj
                        NumericProjectionCalls = tryInt32Prop [ "numericProjectionCalls"; "numeric_projection_calls"; "numericPublicationProjectCalls" ] countersObj
                        MemoryProjectionCalls = tryInt32Prop [ "memoryProjectionCalls"; "memory_projection_calls"; "memoryPublicationProjectCalls" ] countersObj
                        SpatialProjectionCalls = tryInt32Prop [ "spatialProjectionCalls"; "spatial_projection_calls"; "spatialPublicationProjectCalls" ] countersObj
                        SpatialExecuted = tryBoolProp [ "spatialExecuted"; "spatial_executed" ] countersObj
                        HardwareAndKernelModuleCount = combinedHwKr
                        Cvc5Spawns = tryInt32Prop [ "cvc5Spawns"; "cvc5_spawns"; "cvc5Count"; "cvc5_count" ] countersObj
                        UniqueObligations = tryInt32Prop [ "uniqueObligations"; "unique_obligations" ] countersObj
                        Scale1xAllocBytes = scale1xAlloc
                        Scale4xAllocBytes = scale4xAlloc
                        Scale1xWallMs = scale1xWall
                        Scale4xWallMs = scale4xWall
                        ExplicitScalingSlope = tryFloatProp [ "scalingSlope"; "scaling_slope"; "allocSlope"; "alloc_slope" ] scalingObj
                        ScalingPasses = scalingPasses
                        FailedTestCompile = failedTestCompile })))))))
      | _ -> Error(PacketParseError.ExpectedJsonObject "Canary receipt JSON"))

  let private parseCanaryText (raw: string) : Result<CanaryReceipt, PacketParseError> =
    let normalized = raw.Replace("\r\n", "\n")
    let watchdogLineOpt =
      normalized.Split('\n')
      |> Array.tryFind (fun (l: string) -> l.TrimStart().StartsWith("WATCHDOG ", StringComparison.Ordinal))
    let feMatch = Regex.Match(normalized, @"\[FrontEnd:1\]\s+([\d.]+)\s*(s|ms)")
    let pdMatch = Regex.Match(normalized, @"\[FrontEnd\.ProofDischarge:3\]\s+([\d.]+)\s*(s|ms)")
    if watchdogLineOpt.IsNone && not feMatch.Success && not pdMatch.Success then
      Error PacketParseError.UnrecognizedCanaryFormat
    else
      let toMs (m: Match) =
        if not m.Success then None
        else
          match Double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture) with
          | true, v -> if m.Groups[2].Value = "ms" then Some v else Some(v * 1000.0)
          | _ -> None
      let totalMs, rssBytes, breach, exitCode, survivors, scanError =
        match watchdogLineOpt with
        | None -> None, None, None, None, None, None
        | Some wdLine ->
          let rxVal pat =
            let m = Regex.Match(wdLine, pat)
            if m.Success then Some m.Groups[1].Value else None
          let ms =
            rxVal @"\belapsedMs=(\d+(?:\.\d+)?)\b"
            |> Option.bind (fun s -> match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with true, v -> Some v | _ -> None)
          let rss =
            rxVal @"\bpeakRssBytes=(\d+)\b"
            |> Option.bind (fun s -> match Int64.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture) with true, v -> Some v | _ -> None)
          let br = rxVal @"\bbreach=(\S+)" |> normalizeOptionalNoneToken
          let ex =
            rxVal @"\bexitCode=(-?\d+)\b"
            |> Option.bind (fun s -> match Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture) with true, v -> Some v | _ -> None)
          let surv = rxVal @"\bsurvivors=(.+?)(?:\s+scanError=|$)" |> normalizeWatchdogSurvivors
          let scanErr = rxVal @"\bscanError=(.+)$" |> normalizeOptionalNoneToken
          ms, rss, br, ex, surv, scanErr
      let targetOpt =
        let m = Regex.Match(normalized, @"(?:^|\s)(?:target|canary|project)\s*[:=]\s*(\S+)", RegexOptions.IgnoreCase)
        if m.Success then nonEmptyOpt m.Groups[1].Value else None
      let phaseTimings =
        Regex.Matches(normalized, @"\[CCS phase\]\s+(\S+)\s+([\d.]+)s")
        |> Seq.cast<Match>
        |> Seq.choose (fun (m: Match) ->
          match Double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture) with
          | true, secs -> Some(m.Groups[1].Value, secs * 1000.0)
          | _ -> None)
        |> Seq.toList
      Ok {
        Target = targetOpt |> Option.defaultValue ""
        FrontEndMs = toMs feMatch
        FrontEndLimitMs = None
        ProofDischargeMs = toMs pdMatch
        ProofDischargeLimitMs = None
        TotalWallMs = totalMs
        WallLimitMs = None
        PeakRssBytes = rssBytes
        RssLimitBytes = None
        NativeExitCode = exitCode
        CompileExitCode = None
        WatchdogBreach = breach
        WatchdogSurvivors = survivors
        WatchdogScanError = scanError
        WatchdogCleared =
          watchdogLineOpt |> Option.exists (fun l -> l.Contains("survivors="))
          && breach.IsNone && survivors.IsNone && scanError.IsNone
        TotalAllocBytes = None
        PhaseTimingsMs = phaseTimings
        PhaseAllocBytes = []
        PlatformResolutionReads = None
        NumericProjectionCalls = None
        MemoryProjectionCalls = None
        SpatialProjectionCalls = None
        SpatialExecuted = None
        HardwareAndKernelModuleCount = None
        Cvc5Spawns = None
        UniqueObligations = None
        Scale1xAllocBytes = None
        Scale4xAllocBytes = None
        Scale1xWallMs = None
        Scale4xWallMs = None
        ExplicitScalingSlope = None
        ScalingPasses = []
        FailedTestCompile = None
      }

  let parseCanaryReceipt (raw: string) : Result<CanaryReceipt, PacketParseError> =
    if String.IsNullOrWhiteSpace raw then Error(PacketParseError.EmptyInput "Canary receipt")
    elif raw.TrimStart().StartsWith("{", StringComparison.Ordinal) then parseCanaryJson raw
    else parseCanaryText raw

  let computeOverallScalingSlope (c: CanaryReceipt) : float option =
    match c.Scale1xAllocBytes, c.Scale4xAllocBytes with
    | Some a1, Some a4 ->
      if a1 > 0L then Some(float a4 / float a1)
      elif a4 > 0L then Some Double.PositiveInfinity
      else Some 0.0
    | _ -> c.ExplicitScalingSlope

  let findPhaseAllocRegressions (baseReceipt: CanaryReceipt) (candidateReceipt: CanaryReceipt) : (string * int64 * int64 * float) list =
    let basePhaseMap = Map.ofList baseReceipt.PhaseAllocBytes
    candidateReceipt.PhaseAllocBytes
    |> List.choose (fun (phase, candBytes) ->
      match Map.tryFind phase basePhaseMap with
      | Some 0L when candBytes > 0L ->
        Some(phase, 0L, candBytes, Double.PositiveInfinity)
      | Some baseBytes when baseBytes > 0L ->
        let pct = ((float candBytes - float baseBytes) / float baseBytes) * 100.0
        if pct > 15.0 then Some(phase, baseBytes, candBytes, pct)
        else None
      | _ -> None)

  let compareCanaries (baseReceipt: CanaryReceipt) (candidateReceipt: CanaryReceipt) : CanaryComparison =
    let phaseRegressions = findPhaseAllocRegressions baseReceipt candidateReceipt
    let totalWarning =
      match baseReceipt.TotalAllocBytes, candidateReceipt.TotalAllocBytes with
      | Some 0L, Some c when c > 0L ->
        [ sprintf "Total GC allocation grew from 0 -> %d bytes (> +15%% baseline budget)" c ]
      | Some b, Some c when b > 0L ->
        let pct = ((float c - float b) / float b) * 100.0
        if pct > 15.0 then
          [ sprintf "Total GC allocation grew +%.1f%% (%d -> %d bytes, > +15%% baseline budget)" pct b c ]
        else []
      | _ -> []
    { Base = baseReceipt
      Candidate = candidateReceipt
      OverallScalingSlope = computeOverallScalingSlope candidateReceipt
      PhaseAllocRegressions = phaseRegressions
      AllocRegressionWarnings = totalWarning }
