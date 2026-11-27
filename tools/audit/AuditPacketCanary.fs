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

  let internal computeAllocSlope (a1: int64) (a4: int64) : float =
    if a1 > 0L then float a4 / float a1
    elif a4 > 0L then Double.PositiveInfinity
    else 0.0

  let internal effectiveScalingPassSlope (p: ScalingPassMetric) : float =
    if p.Alloc1xBytes > 0L || p.Alloc4xBytes > 0L then
      let computed = computeAllocSlope p.Alloc1xBytes p.Alloc4xBytes
      if Double.IsNaN p.Slope then p.Slope
      else max p.Slope computed
    else
      p.Slope

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
                let slope = computeAllocSlope a1 a4
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

  // ---------------------------------------------------------------------------
  // Strict `schemaVersion: 1` canary receipt contract (Bozzetto #39 / Composer
  // #192 producer-consumer schema aligned with #12617).
  // - Only absent `schemaVersion` takes the unversioned legacy path; explicit
  //   `0`, `>= 2`, `< 0`, non-integer/null values, duplicate `schemaVersion`,
  //   and version aliases (`schema_version`, `schemaVersionMajor`) are refused.
  // - Unversioned legacy receipts refuse v1-only blocks (`telemetry`,
  //   `projectionCoverage`) so stripping `schemaVersion` cannot hide violations.
  // - Strict `schemaVersion: 1` refuses duplicate JSON keys in every object,
  //   refuses legacy aliases before checking missing fields, refuses unknown
  //   keys, computes per-pass scaling ratios from `alloc4xBytes / alloc1xBytes`
  //   and cross-checks declared slopes, and parses `projectionCoverage.legacySites`
  //   into `CanaryReceipt.LegacyProjectionSites` so the full receipt stays intact
  //   for diagnosis while `AuditPacketEval` fires `ProjectionCoverageLegacySites`.
  // ---------------------------------------------------------------------------
  let private slopeTolerance = 1e-3

  type private StrictFieldContract = {
    Canonical: string
    LegacyAliases: string list
  }

  type private StrictRootValues = {
    Target: string
    CompileExitCode: int
    NativeExitCode: int option
    TotalAllocBytes: int64
    PhaseTimingsMs: (string * float) list
    PhaseAllocBytes: (string * int64) list
    TotalWallMs: float option
    WallLimitMs: float option
    FrontEndMs: float option
    FrontEndLimitMs: float option
    ProofDischargeMs: float option
    ProofDischargeLimitMs: float option
    PeakRssBytes: int64 option
    RssLimitBytes: int64 option
  }

  type private StrictCounterValues = {
    PlatformResolutionReads: int
    NumericProjectionCalls: int
    MemoryProjectionCalls: int
    SpatialProjectionCalls: int
    SpatialExecuted: bool
    HardwareModules: int
    KernelModules: int
    ProjectionScope: string
    LegacyProjectionSites: int
    Cvc5Spawns: int
    UniqueObligations: int
    Cvc5SpawnsByPhase: (string * int) list
    Cvc5ToolIdentity: string option
  }

  type private StrictScalingValues = {
    Scale1xAllocBytes: int64
    Scale4xAllocBytes: int64
    ScalingSlope: float
    ScalingPasses: ScalingPassMetric list
    Scale1xWallMs: float option
    Scale4xWallMs: float option
  }

  type private StrictTelemetryValues = {
    Transport: string
    Dropped: int64
    Drain: string
    Observations: int64 option
    ProducerOverheadBytes: int64 option
    ProducerOverheadTicks: int64 option
  }

  type private StrictBudgetValues = {
    Accepted: bool
    Breaches: string list
  }

  type private StrictWatchdogValues = {
    Survivors: string option
    SurvivorsCleared: bool
    Breach: string option
    ScanError: string option
    ExplicitCleared: bool option
    CommandExitCode: int option
  }

  // Pure Result computation expression: keeps the strict contract readable
  // without nesting thirty levels of Result.bind.
  type private StrictResultBuilder() =
    member _.Bind (value: Result<'a, PacketParseError>, f: 'a -> Result<'b, PacketParseError>) : Result<'b, PacketParseError> =
      match value with
      | Ok value -> f value
      | Error err -> Error err
    member _.Return (value: 'a) : Result<'a, PacketParseError> = Ok value

  let private strictResult = StrictResultBuilder()

  let private strictRootContract: StrictFieldContract list = [
    { Canonical = "schemaVersion"; LegacyAliases = [ "schema_version"; "schemaVersionMajor" ] }
    { Canonical = "target"; LegacyAliases = [ "canary"; "project" ] }
    { Canonical = "compileExitCode"; LegacyAliases = [ "compile_exit_code" ] }
    { Canonical = "nativeExitCode"; LegacyAliases = [ "native_exit_code"; "exitCode"; "exit_code" ] }
    { Canonical = "totalAllocBytes"; LegacyAliases = [ "total_alloc_bytes"; "gcAllocatedBytes"; "gc_allocated_bytes" ] }
    { Canonical = "phaseTimingsMs"; LegacyAliases = [ "phase_timings_ms"; "phaseTimings"; "phase_timings" ] }
    { Canonical = "phaseAllocBytes"; LegacyAliases = [ "phase_alloc_bytes"; "phaseAllocations"; "phase_allocations" ] }
    { Canonical = "counters"; LegacyAliases = [ "projections" ] }
    { Canonical = "scaling"; LegacyAliases = [] }
    { Canonical = "telemetry"; LegacyAliases = [] }
    { Canonical = "budget"; LegacyAliases = [ "budgetResult"; "budget_result" ] }
    { Canonical = "totalWallMs"; LegacyAliases = [ "total_wall_ms"; "wallMs"; "wall_ms"; "elapsedMs" ] }
    { Canonical = "wallLimitMs"; LegacyAliases = [ "wall_limit_ms" ] }
    { Canonical = "frontEndMs"; LegacyAliases = [ "front_end_ms" ] }
    { Canonical = "frontEndLimitMs"; LegacyAliases = [ "front_end_limit_ms" ] }
    { Canonical = "proofDischargeMs"; LegacyAliases = [ "proof_discharge_ms" ] }
    { Canonical = "proofDischargeLimitMs"; LegacyAliases = [ "proof_discharge_limit_ms" ] }
    { Canonical = "peakRssBytes"; LegacyAliases = [ "peak_rss_bytes"; "peakRssMb"; "peakRssMB"; "peak_rss_mb" ] }
    { Canonical = "rssLimitBytes"; LegacyAliases = [ "rss_limit_bytes"; "rssLimitMb"; "rssLimitMB"; "rss_limit_mb" ] }
    { Canonical = "watchdogBreach"; LegacyAliases = [ "watchdog_breach"; "breach" ] }
    { Canonical = "watchdogSurvivors"; LegacyAliases = [ "watchdog_survivors"; "survivors" ] }
    { Canonical = "watchdogScanError"; LegacyAliases = [ "watchdog_scan_error"; "scanError"; "scan_error" ] }
    { Canonical = "watchdogCleared"; LegacyAliases = [ "watchdog_cleared" ] }
    { Canonical = "commandExitCode"; LegacyAliases = [ "command_exit_code" ] }
  ]

  let private strictCounterContract: StrictFieldContract list = [
    { Canonical = "platformResolutionReads"; LegacyAliases = [ "platform_resolution_reads"; "platformResolutionReadCalls" ] }
    { Canonical = "numericProjectionCalls"; LegacyAliases = [ "numeric_projection_calls"; "numericPublicationProjectCalls" ] }
    { Canonical = "memoryProjectionCalls"; LegacyAliases = [ "memory_projection_calls"; "memoryPublicationProjectCalls" ] }
    { Canonical = "spatialProjectionCalls"; LegacyAliases = [ "spatial_projection_calls"; "spatialPublicationProjectCalls" ] }
    { Canonical = "spatialExecuted"; LegacyAliases = [ "spatial_executed" ] }
    { Canonical = "hardwareModules"; LegacyAliases = [ "hardware_modules"; "hardwareModuleCount"; "hardware_module_count"; "hardwareAndKernelModuleCount"; "hardware_and_kernel_module_count" ] }
    { Canonical = "kernelModules"; LegacyAliases = [ "kernel_modules"; "kernelModuleCount"; "kernel_module_count" ] }
    { Canonical = "projectionCoverage"; LegacyAliases = [ "projection_coverage"; "projectionCensus" ] }
    { Canonical = "cvc5Spawns"; LegacyAliases = [ "cvc5_spawns"; "cvc5Count"; "cvc5_count" ] }
    { Canonical = "uniqueObligations"; LegacyAliases = [ "unique_obligations" ] }
    { Canonical = "cvc5SpawnsByPhase"; LegacyAliases = [ "cvc5_spawns_by_phase" ] }
    { Canonical = "cvc5ToolIdentity"; LegacyAliases = [ "cvc5_tool_identity"; "toolIdentity"; "tool_identity" ] }
  ]

  let private strictScalingContract: StrictFieldContract list = [
    { Canonical = "scale1xAllocBytes"; LegacyAliases = [ "scale1x"; "scale_1x"; "Scale_1x"; "scale_1x_alloc_bytes" ] }
    { Canonical = "scale4xAllocBytes"; LegacyAliases = [ "scale4x"; "scale_4x"; "Scale_4x"; "scale_4x_alloc_bytes" ] }
    { Canonical = "scalingSlope"; LegacyAliases = [ "scaling_slope"; "allocSlope"; "alloc_slope"; "scalingRatio" ] }
    { Canonical = "scalingPasses"; LegacyAliases = [ "scaling_passes"; "passes" ] }
    { Canonical = "scale1xWallMs"; LegacyAliases = [ "scale_1x_wall_ms" ] }
    { Canonical = "scale4xWallMs"; LegacyAliases = [ "scale_4x_wall_ms" ] }
  ]

  let private strictScalingPassContract: StrictFieldContract list = [
    { Canonical = "pass"; LegacyAliases = [ "passName"; "pass_name"; "name" ] }
    { Canonical = "alloc1xBytes"; LegacyAliases = [ "alloc_1x_bytes"; "scale1xAllocBytes"; "scale_1x_alloc_bytes"; "alloc1x" ] }
    { Canonical = "alloc4xBytes"; LegacyAliases = [ "alloc_4x_bytes"; "scale4xAllocBytes"; "scale_4x_alloc_bytes"; "alloc4x" ] }
    { Canonical = "slope"; LegacyAliases = [ "ratio"; "slopeRatio" ] }
  ]

  let private strictCoverageContract: StrictFieldContract list = [
    { Canonical = "scope"; LegacyAliases = [ "projectionScope"; "coverageScope" ] }
    { Canonical = "legacySites"; LegacyAliases = [ "legacy_sites"; "legacyCount"; "legacy_count" ] }
  ]

  let private strictTelemetryContract: StrictFieldContract list = [
    { Canonical = "transport"; LegacyAliases = [ "sink"; "sinkTransport" ] }
    { Canonical = "dropped"; LegacyAliases = [ "droppedRecords"; "dropped_count" ] }
    { Canonical = "drain"; LegacyAliases = [ "drainBudget"; "drain_budget" ] }
    { Canonical = "observations"; LegacyAliases = [ "observationCount"; "observation_count" ] }
    { Canonical = "producerOverheadBytes"; LegacyAliases = [ "producer_overhead_bytes" ] }
    { Canonical = "producerOverheadTicks"; LegacyAliases = [ "producer_overhead_ticks" ] }
  ]

  let private strictBudgetContract: StrictFieldContract list = [
    { Canonical = "accepted"; LegacyAliases = [ "budgetAccepted"; "budget_accepted"; "passed" ] }
    { Canonical = "breaches"; LegacyAliases = [ "budgetBreaches"; "budget_breaches"; "violations" ] }
  ]

  let private strictRootCanonicalNames = strictRootContract |> List.map _.Canonical
  let private strictCounterCanonicalNames = strictCounterContract |> List.map _.Canonical
  let private strictScalingCanonicalNames = strictScalingContract |> List.map _.Canonical
  let private strictScalingPassCanonicalNames = strictScalingPassContract |> List.map _.Canonical
  let private strictCoverageCanonicalNames = strictCoverageContract |> List.map _.Canonical
  let private strictTelemetryCanonicalNames = strictTelemetryContract |> List.map _.Canonical
  let private strictBudgetCanonicalNames = strictBudgetContract |> List.map _.Canonical

  let private rejectDuplicateKeys (label: string) (obj: JsonValue) : Result<unit, PacketParseError> =
    match tryObjectPairs obj with
    | Some pairs ->
      let rec loop (seen: Set<string>) remaining =
        match remaining with
        | [] -> Ok ()
        | (key, _) :: rest ->
          if Set.contains key seen then
            Error(PacketParseError.InvalidJsonField(
              label, key,
              sprintf "duplicate JSON key '%s' is refused; repeated observations cannot select a convenient value" key))
          else
            loop (Set.add key seen) rest
      loop Set.empty pairs
    | None -> Ok ()

  let private rejectLegacyAliases (label: string) (contract: StrictFieldContract list) (obj: JsonValue) : Result<unit, PacketParseError> =
    List.fold (fun acc spec ->
      match acc with
      | Error err -> Error err
      | Ok () ->
        match spec.LegacyAliases |> List.tryFind (fun alias -> tryProp alias obj |> Option.isSome) with
        | Some alias ->
          Error(PacketParseError.InvalidJsonField(
            label, alias,
            sprintf "strict schemaVersion 1 receipts accept only the canonical spelling '%s'; legacy alias '%s' is refused instead of being read as absent or 0"
              spec.Canonical alias))
        | None -> Ok()) (Ok()) contract

  let private rejectUnknownKeys (label: string) (canonical: string list) (obj: JsonValue) : Result<unit, PacketParseError> =
    match tryObjectPairs obj with
    | Some pairs ->
      List.fold (fun acc (key, _) ->
        match acc with
        | Error err -> Error err
        | Ok () ->
          if canonical |> List.exists (fun c -> String.Equals(c, key, StringComparison.Ordinal)) then Ok()
          else
            Error(PacketParseError.InvalidJsonField(
              label, key,
              sprintf "strict schemaVersion 1 receipts do not define key '%s'; expected only %s" key (String.concat ", " canonical)))) (Ok()) pairs
    | None -> Ok()

  let private validateStrictObjectKeys (label: string) (contract: StrictFieldContract list) (canonical: string list) (obj: JsonValue) : Result<unit, PacketParseError> =
    strictResult {
      do! rejectDuplicateKeys label obj
      do! rejectLegacyAliases label contract obj
      do! rejectUnknownKeys label canonical obj
      return ()
    }

  let private strictObjectField (label: string) (name: string) (obj: JsonValue) : Result<JsonValue, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some(JsonValue.Object _ as subObj) -> Ok subObj
    | Some _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a JSON object"))

  let private strictAnyInt (label: string) (name: string) (obj: JsonValue) : Result<int, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some value ->
      match tryInt32 value with
      | Some n -> Ok n
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected an integer"))

  let private strictOptionalAnyInt (label: string) (name: string) (obj: JsonValue) : Result<int option, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Ok None
    | Some value ->
      match tryInt32 value with
      | Some n -> Ok(Some n)
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected an integer or null"))

  let private strictNonNegInt (label: string) (name: string) (obj: JsonValue) : Result<int, PacketParseError> =
    strictAnyInt label name obj
    |> Result.bind (fun n ->
      if n >= 0 then Ok n
      else Error(PacketParseError.InvalidJsonField(label, name, "expected a non-negative integer")))

  let private strictNonNegInt64 (label: string) (name: string) (obj: JsonValue) : Result<int64, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some value ->
      match tryInt64 value with
      | Some n when n >= 0L -> Ok n
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a non-negative integer"))

  let private strictOptionalNonNegInt64 (label: string) (name: string) (obj: JsonValue) : Result<int64 option, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Ok None
    | Some value ->
      match tryInt64 value with
      | Some n when n >= 0L -> Ok(Some n)
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a non-negative integer"))

  let private strictOptionalNonNegFloat (label: string) (name: string) (obj: JsonValue) : Result<float option, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Ok None
    | Some value ->
      match tryFloat value with
      | Some n when n >= 0.0 -> Ok(Some n)
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a non-negative finite number"))

  let private strictNonNegFloat (label: string) (name: string) (obj: JsonValue) : Result<float, PacketParseError> =
    strictOptionalNonNegFloat label name obj
    |> Result.bind (fun opt ->
      match opt with
      | Some n -> Ok n
      | None -> Error(PacketParseError.MissingJsonField(label, name)))

  let private strictNonEmptyString (label: string) (name: string) (obj: JsonValue) : Result<string, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some value ->
      match tryNonBlankString value with
      | Some s -> Ok s
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a non-empty string"))

  let private strictOptionalNonEmptyString (label: string) (name: string) (obj: JsonValue) : Result<string option, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Ok None
    | Some value ->
      match tryNonBlankString value with
      | Some s -> Ok(Some s)
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a non-empty string or null"))

  let private strictBool (label: string) (name: string) (obj: JsonValue) : Result<bool, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some value ->
      match tryBool value with
      | Some b -> Ok b
      | _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a boolean"))

  let private strictNonEmptyStringList (label: string) (name: string) (obj: JsonValue) : Result<string list, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some(JsonValue.Array items) ->
      items
      |> List.fold (fun acc item ->
        match acc with
        | Error err -> Error err
        | Ok acc' ->
          match tryNonBlankString item with
          | Some s -> Ok(s :: acc')
          | None -> Error(PacketParseError.InvalidJsonField(label, name, "each entry must be a non-empty string"))) (Ok [])
      |> Result.map List.rev
    | Some _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a JSON array of strings"))

  let private strictPairListFloat (label: string) (name: string) (obj: JsonValue) : Result<(string * float) list, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some(JsonValue.Object pairs as mapObj) ->
      if List.isEmpty pairs then
        Error(PacketParseError.InvalidJsonField(label, name, "expected at least one phase entry"))
      else
        rejectDuplicateKeys (sprintf "%s.%s" label name) mapObj
        |> Result.bind (fun () ->
          pairs
          |> List.fold (fun acc (key, value) ->
            match acc with
            | Error err -> Error err
            | Ok acc' ->
              if String.IsNullOrWhiteSpace key then
                Error(PacketParseError.InvalidJsonField(label, name, "phase key must not be blank"))
              else
                match tryFloat value with
                | Some n when n >= 0.0 -> Ok((key, n) :: acc')
                | _ -> Error(PacketParseError.InvalidJsonField(label, name, sprintf "phase entry '%s' is not a non-negative finite number" key))) (Ok [])
          |> Result.map List.rev)
    | Some _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a JSON object of phase -> value"))

  let private strictPairListInt64 (label: string) (name: string) (obj: JsonValue) : Result<(string * int64) list, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, name))
    | Some(JsonValue.Object pairs as mapObj) ->
      if List.isEmpty pairs then
        Error(PacketParseError.InvalidJsonField(label, name, "expected at least one phase entry"))
      else
        rejectDuplicateKeys (sprintf "%s.%s" label name) mapObj
        |> Result.bind (fun () ->
          pairs
          |> List.fold (fun acc (key, value) ->
            match acc with
            | Error err -> Error err
            | Ok acc' ->
              if String.IsNullOrWhiteSpace key then
                Error(PacketParseError.InvalidJsonField(label, name, "phase key must not be blank"))
              else
                match tryInt64 value with
                | Some n when n >= 0L -> Ok((key, n) :: acc')
                | _ -> Error(PacketParseError.InvalidJsonField(label, name, sprintf "phase entry '%s' is not a non-negative integer" key))) (Ok [])
          |> Result.map List.rev)
    | Some _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a JSON object of phase -> value"))

  let private strictOptionalPairListNonNegInt (label: string) (name: string) (obj: JsonValue) : Result<(string * int) list, PacketParseError> =
    match tryProp name obj with
    | None | Some JsonValue.Null -> Ok []
    | Some(JsonValue.Object pairs as mapObj) ->
      rejectDuplicateKeys (sprintf "%s.%s" label name) mapObj
      |> Result.bind (fun () ->
        pairs
        |> List.fold (fun acc (key, value) ->
          match acc with
          | Error err -> Error err
          | Ok acc' ->
            if String.IsNullOrWhiteSpace key then
              Error(PacketParseError.InvalidJsonField(label, name, "phase key must not be blank"))
            else
              match tryInt32 value with
              | Some n when n >= 0 -> Ok((key, n) :: acc')
              | _ -> Error(PacketParseError.InvalidJsonField(label, name, sprintf "phase entry '%s' is not a non-negative integer" key))) (Ok [])
        |> Result.map List.rev)
    | Some _ -> Error(PacketParseError.InvalidJsonField(label, name, "expected a JSON object of phase -> non-negative integer"))

  // Detect declared schemaVersion before choosing strict vs legacy parsing.
  // Only absent `schemaVersion` (with no version aliases) takes the unversioned
  // legacy path; only exact integer `schemaVersion = 1` takes the strict path.
  let private detectStrictSchemaVersion (root: JsonValue) : Result<bool, PacketParseError> =
    let label = "strict schemaVersion 1 canary receipt"
    let aliasKeys = [ "schema_version"; "schemaVersionMajor" ]
    match tryObjectPairs root with
    | None -> Error(PacketParseError.ExpectedJsonObject "Canary receipt JSON")
    | Some pairs ->
      let versionValues =
        pairs
        |> List.choose (fun (k, v) ->
          if String.Equals(k, "schemaVersion", StringComparison.Ordinal) then Some v
          else None)
      if versionValues.Length > 1 then
        Error(PacketParseError.InvalidJsonField(
          label, "schemaVersion",
          "duplicate JSON key 'schemaVersion' is refused; repeated observations cannot select a convenient value"))
      else
        match aliasKeys |> List.tryFind (fun alias -> pairs |> List.exists (fun (k, _) -> String.Equals(k, alias, StringComparison.Ordinal))) with
        | Some alias ->
          Error(PacketParseError.InvalidJsonField(
            label, alias,
            "strict schemaVersion 1 receipts require the canonical key 'schemaVersion'; the legacy alias is refused"))
        | None ->
          match versionValues with
          | [] -> Ok false
          | [ value ] ->
            match tryInt32 value with
            | Some 1 -> Ok true
            | Some 0 ->
              Error(PacketParseError.InvalidJsonField(
                label, "schemaVersion",
                "explicit schemaVersion 0 is unsupported; omit schemaVersion for unversioned legacy receipts or declare schemaVersion 1"))
            | Some n when n >= 2 ->
              Error(PacketParseError.InvalidJsonField(
                label, "schemaVersion",
                sprintf "unsupported schemaVersion %d; only schemaVersion 1 is supported" n))
            | Some n ->
              Error(PacketParseError.InvalidJsonField(
                label, "schemaVersion",
                sprintf "invalid schemaVersion %d; expected integer 1" n))
            | None ->
              Error(PacketParseError.InvalidJsonField(
                label, "schemaVersion",
                "expected integer schemaVersion 1 (non-integer or null values are refused)"))
          | _ ->
            Error(PacketParseError.InvalidJsonField(
              label, "schemaVersion",
              "duplicate JSON key 'schemaVersion' is refused; repeated observations cannot select a convenient value"))

  let private parseStrictScalingPasses (scalingObj: JsonValue) : Result<ScalingPassMetric list, PacketParseError> =
    let label = "strict schemaVersion 1 scaling block"
    let passLabel = "strict schemaVersion 1 scaling pass"
    match tryProp "scalingPasses" scalingObj with
    | None | Some JsonValue.Null -> Error(PacketParseError.MissingJsonField(label, "scalingPasses"))
    | Some(JsonValue.Array items) ->
      if List.isEmpty items then
        Error(PacketParseError.InvalidJsonField(label, "scalingPasses", "expected at least one scaling pass"))
      else
        items
        |> List.fold (fun acc item ->
          match acc with
          | Error err -> Error err
          | Ok(seenNames, passes) ->
            match item with
            | JsonValue.Object _ ->
              strictResult {
                do! validateStrictObjectKeys passLabel strictScalingPassContract strictScalingPassCanonicalNames item
                let! name = strictNonEmptyString passLabel "pass" item
                do!
                  if Set.contains name seenNames then
                    Error(PacketParseError.InvalidJsonField(passLabel, "pass", sprintf "duplicate scaling pass '%s' is refused" name))
                  else
                    Ok ()
                let! a1 = strictNonNegInt64 passLabel "alloc1xBytes" item
                let! a4 = strictNonNegInt64 passLabel "alloc4xBytes" item
                let! declaredSlope = strictNonNegFloat passLabel "slope" item
                let computedSlope = computeAllocSlope a1 a4
                do!
                  if Double.IsInfinity computedSlope then
                    Error(PacketParseError.InvalidJsonField(
                      passLabel, "slope",
                      sprintf "pass '%s' declares finite slope %.4f for zero-baseline allocation growth (%d -> %d bytes, computed slope +Infinity)"
                        name declaredSlope a1 a4))
                  elif abs (declaredSlope - computedSlope) > slopeTolerance then
                    Error(PacketParseError.InvalidJsonField(
                      passLabel, "slope",
                      sprintf "pass '%s' declares slope %.4f which disagrees with computed alloc4xBytes/alloc1xBytes ratio %.4f (%d/%d, tolerance %.3f)"
                        name declaredSlope computedSlope a4 a1 slopeTolerance))
                  else
                    Ok ()
                let entry = { PassName = name; Alloc1xBytes = a1; Alloc4xBytes = a4; Slope = computedSlope }
                return (Set.add name seenNames, entry :: passes)
              }
            | _ ->
              Error(PacketParseError.InvalidJsonField(label, "scalingPasses", "each scaling pass entry must be a JSON object"))) (Ok(Set.empty, []))
        |> Result.map (snd >> List.rev)
    | Some _ -> Error(PacketParseError.InvalidJsonField(label, "scalingPasses", "expected a JSON array"))

  let private parseStrictRootValues (root: JsonValue) : Result<StrictRootValues, PacketParseError> =
    let label = "strict schemaVersion 1 canary receipt"
    strictResult {
      let! target = strictNonEmptyString label "target" root
      let! compileExitCode = strictAnyInt label "compileExitCode" root
      let! nativeExitCode = strictOptionalAnyInt label "nativeExitCode" root
      let! totalAllocBytes = strictNonNegInt64 label "totalAllocBytes" root
      let! phaseTimingsMs = strictPairListFloat label "phaseTimingsMs" root
      let! phaseAllocBytes = strictPairListInt64 label "phaseAllocBytes" root
      let! totalWallMs = strictOptionalNonNegFloat label "totalWallMs" root
      let! wallLimitMs = strictOptionalNonNegFloat label "wallLimitMs" root
      let! frontEndMs = strictOptionalNonNegFloat label "frontEndMs" root
      let! frontEndLimitMs = strictOptionalNonNegFloat label "frontEndLimitMs" root
      let! proofDischargeMs = strictOptionalNonNegFloat label "proofDischargeMs" root
      let! proofDischargeLimitMs = strictOptionalNonNegFloat label "proofDischargeLimitMs" root
      let! peakRssBytes = strictOptionalNonNegInt64 label "peakRssBytes" root
      let! rssLimitBytes = strictOptionalNonNegInt64 label "rssLimitBytes" root
      return {
        Target = target
        CompileExitCode = compileExitCode
        NativeExitCode = nativeExitCode
        TotalAllocBytes = totalAllocBytes
        PhaseTimingsMs = phaseTimingsMs
        PhaseAllocBytes = phaseAllocBytes
        TotalWallMs = totalWallMs
        WallLimitMs = wallLimitMs
        FrontEndMs = frontEndMs
        FrontEndLimitMs = frontEndLimitMs
        ProofDischargeMs = proofDischargeMs
        ProofDischargeLimitMs = proofDischargeLimitMs
        PeakRssBytes = peakRssBytes
        RssLimitBytes = rssLimitBytes
      }
    }

  let private validateProjectionScope (label: string) (scope: string) : Result<string, PacketParseError> =
    if String.Equals(scope, "driver-path", StringComparison.Ordinal)
       || String.Equals(scope, "owner-site", StringComparison.Ordinal) then
      Ok scope
    else
      Error(PacketParseError.InvalidJsonField(
        label, "scope",
        sprintf "expected 'driver-path' or 'owner-site', found '%s'" scope))

  let private validateTelemetryTransport (label: string) (transport: string) : Result<string, PacketParseError> =
    if String.Equals(transport, "synchronous", StringComparison.Ordinal)
       || String.Equals(transport, "async-two-lane", StringComparison.Ordinal) then
      Ok transport
    else
      Error(PacketParseError.InvalidJsonField(
        label, "transport",
        sprintf "expected 'synchronous' or 'async-two-lane', found '%s'" transport))

  let private parseStrictCounterValues (countersObj: JsonValue) (coverageObj: JsonValue) : Result<StrictCounterValues, PacketParseError> =
    let countersLabel = "strict schemaVersion 1 counters block"
    let coverageLabel = "strict schemaVersion 1 projectionCoverage block"
    strictResult {
      let! platformResolutionReads = strictNonNegInt countersLabel "platformResolutionReads" countersObj
      let! numericProjectionCalls = strictNonNegInt countersLabel "numericProjectionCalls" countersObj
      let! memoryProjectionCalls = strictNonNegInt countersLabel "memoryProjectionCalls" countersObj
      let! spatialProjectionCalls = strictNonNegInt countersLabel "spatialProjectionCalls" countersObj
      let! spatialExecuted = strictBool countersLabel "spatialExecuted" countersObj
      let! hardwareModules = strictNonNegInt countersLabel "hardwareModules" countersObj
      let! kernelModules = strictNonNegInt countersLabel "kernelModules" countersObj
      let! rawScope = strictNonEmptyString coverageLabel "scope" coverageObj
      let! projectionScope = validateProjectionScope coverageLabel rawScope
      let! legacySites = strictNonNegInt coverageLabel "legacySites" coverageObj
      let! cvc5Spawns = strictNonNegInt countersLabel "cvc5Spawns" countersObj
      let! uniqueObligations = strictNonNegInt countersLabel "uniqueObligations" countersObj
      let! cvc5SpawnsByPhase = strictOptionalPairListNonNegInt countersLabel "cvc5SpawnsByPhase" countersObj
      let! cvc5ToolIdentity = strictOptionalNonEmptyString countersLabel "cvc5ToolIdentity" countersObj
      return {
        PlatformResolutionReads = platformResolutionReads
        NumericProjectionCalls = numericProjectionCalls
        MemoryProjectionCalls = memoryProjectionCalls
        SpatialProjectionCalls = spatialProjectionCalls
        SpatialExecuted = spatialExecuted
        HardwareModules = hardwareModules
        KernelModules = kernelModules
        ProjectionScope = projectionScope
        LegacyProjectionSites = legacySites
        Cvc5Spawns = cvc5Spawns
        UniqueObligations = uniqueObligations
        Cvc5SpawnsByPhase = cvc5SpawnsByPhase
        Cvc5ToolIdentity = cvc5ToolIdentity
      }
    }

  let private parseStrictScalingValues (scalingObj: JsonValue) : Result<StrictScalingValues, PacketParseError> =
    let label = "strict schemaVersion 1 scaling block"
    strictResult {
      let! scale1xAllocBytes = strictNonNegInt64 label "scale1xAllocBytes" scalingObj
      let! scale4xAllocBytes = strictNonNegInt64 label "scale4xAllocBytes" scalingObj
      let! scalingSlope = strictNonNegFloat label "scalingSlope" scalingObj
      let! scalingPasses = parseStrictScalingPasses scalingObj
      let overallSlope = computeAllocSlope scale1xAllocBytes scale4xAllocBytes
      let maxPassSlope = scalingPasses |> List.map _.Slope |> List.max
      do!
        if Double.IsInfinity overallSlope then
          Error(PacketParseError.InvalidJsonField(
            label, "scalingSlope",
            sprintf "scaling declares finite scalingSlope %.4f for zero-baseline allocation growth (%d -> %d bytes, computed ratio +Infinity)"
              scalingSlope scale1xAllocBytes scale4xAllocBytes))
        else
          let matchesOverall = abs (scalingSlope - overallSlope) <= slopeTolerance
          let matchesMaxPass = not (Double.IsInfinity maxPassSlope) && abs (scalingSlope - maxPassSlope) <= slopeTolerance
          if matchesOverall || matchesMaxPass then
            Ok ()
          else
            Error(PacketParseError.InvalidJsonField(
              label, "scalingSlope",
              sprintf "declared scalingSlope %.4f disagrees with both overall alloc ratio %.4f (%d/%d) and max pass slope %.4f (tolerance %.3f)"
                scalingSlope overallSlope scale4xAllocBytes scale1xAllocBytes maxPassSlope slopeTolerance))
      let! scale1xWallMs = strictOptionalNonNegFloat label "scale1xWallMs" scalingObj
      let! scale4xWallMs = strictOptionalNonNegFloat label "scale4xWallMs" scalingObj
      return {
        Scale1xAllocBytes = scale1xAllocBytes
        Scale4xAllocBytes = scale4xAllocBytes
        ScalingSlope = scalingSlope
        ScalingPasses = scalingPasses
        Scale1xWallMs = scale1xWallMs
        Scale4xWallMs = scale4xWallMs
      }
    }

  let private parseStrictTelemetryValues (telemetryObj: JsonValue) : Result<StrictTelemetryValues, PacketParseError> =
    let label = "strict schemaVersion 1 telemetry block"
    strictResult {
      let! rawTransport = strictNonEmptyString label "transport" telemetryObj
      let! transport = validateTelemetryTransport label rawTransport
      let! dropped = strictNonNegInt64 label "dropped" telemetryObj
      let! drain = strictNonEmptyString label "drain" telemetryObj
      let! observations = strictOptionalNonNegInt64 label "observations" telemetryObj
      let! producerOverheadBytes = strictOptionalNonNegInt64 label "producerOverheadBytes" telemetryObj
      let! producerOverheadTicks = strictOptionalNonNegInt64 label "producerOverheadTicks" telemetryObj
      return {
        Transport = transport
        Dropped = dropped
        Drain = drain
        Observations = observations
        ProducerOverheadBytes = producerOverheadBytes
        ProducerOverheadTicks = producerOverheadTicks
      }
    }

  let private parseStrictBudgetValues (root: JsonValue) : Result<StrictBudgetValues option, PacketParseError> =
    let rootLabel = "strict schemaVersion 1 canary receipt"
    let budgetLabel = "strict schemaVersion 1 budget block"
    match tryProp "budget" root with
    | None | Some JsonValue.Null -> Ok None
    | Some(JsonValue.Object _ as budgetObj) ->
      strictResult {
        do! validateStrictObjectKeys budgetLabel strictBudgetContract strictBudgetCanonicalNames budgetObj
        let! accepted = strictBool budgetLabel "accepted" budgetObj
        let! breaches = strictNonEmptyStringList budgetLabel "breaches" budgetObj
        return Some { Accepted = accepted; Breaches = breaches }
      }
    | Some _ -> Error(PacketParseError.InvalidJsonField(rootLabel, "budget", "expected a JSON object or null"))

  let private parseStrictWatchdogValues (root: JsonValue) : Result<StrictWatchdogValues, PacketParseError> =
    strictResult {
      let! (survivors, cleared) = parseWatchdogSurvivorsJson root
      let! breach = parseStrictOptStringProp "watchdogBreach" [ "watchdogBreach" ] root
      let! scanError = parseStrictOptStringProp "watchdogScanError" [ "watchdogScanError" ] root
      let! explicitCleared = parseStrictOptBoolProp "watchdogCleared" [ "watchdogCleared" ] root
      let! commandExitCode = parseStrictOptIntProp "commandExitCode" [ "commandExitCode" ] root
      return {
        Survivors = survivors
        SurvivorsCleared = cleared
        Breach = breach
        ScanError = scanError
        ExplicitCleared = explicitCleared
        CommandExitCode = commandExitCode
      }
    }

  let private parseStrictCanaryReceipt (root: JsonValue) : Result<CanaryReceipt, PacketParseError> =
    let rootLabel = "strict schemaVersion 1 canary receipt"
    let countersLabel = "strict schemaVersion 1 counters block"
    let scalingLabel = "strict schemaVersion 1 scaling block"
    let coverageLabel = "strict schemaVersion 1 projectionCoverage block"
    let telemetryLabel = "strict schemaVersion 1 telemetry block"
    strictResult {
      // Check duplicate keys, legacy aliases, and unknown keys BEFORE missing-field
      // lookups so alias spellings (e.g. `projections`) report the alias explicitly.
      do! validateStrictObjectKeys rootLabel strictRootContract strictRootCanonicalNames root
      let! countersObj = strictObjectField rootLabel "counters" root
      do! validateStrictObjectKeys countersLabel strictCounterContract strictCounterCanonicalNames countersObj
      let! coverageObj = strictObjectField countersLabel "projectionCoverage" countersObj
      do! validateStrictObjectKeys coverageLabel strictCoverageContract strictCoverageCanonicalNames coverageObj
      let! scalingObj = strictObjectField rootLabel "scaling" root
      do! validateStrictObjectKeys scalingLabel strictScalingContract strictScalingCanonicalNames scalingObj
      let! telemetryObj = strictObjectField rootLabel "telemetry" root
      do! validateStrictObjectKeys telemetryLabel strictTelemetryContract strictTelemetryCanonicalNames telemetryObj
      let! r = parseStrictRootValues root
      let! c = parseStrictCounterValues countersObj coverageObj
      let! s = parseStrictScalingValues scalingObj
      let! t = parseStrictTelemetryValues telemetryObj
      let! bOpt = parseStrictBudgetValues root
      let! w = parseStrictWatchdogValues root
      let watchdogCleared =
        (w.SurvivorsCleared || w.ExplicitCleared = Some true)
        && w.Breach.IsNone
        && w.Survivors.IsNone
        && w.ScanError.IsNone
        && (w.CommandExitCode |> Option.forall ((=) 0))
      return {
        Target = r.Target
        FrontEndMs = r.FrontEndMs
        FrontEndLimitMs = r.FrontEndLimitMs
        ProofDischargeMs = r.ProofDischargeMs
        ProofDischargeLimitMs = r.ProofDischargeLimitMs
        TotalWallMs = r.TotalWallMs
        WallLimitMs = r.WallLimitMs
        PeakRssBytes = r.PeakRssBytes
        RssLimitBytes = r.RssLimitBytes
        NativeExitCode = r.NativeExitCode
        CompileExitCode = Some r.CompileExitCode
        CommandExitCode = w.CommandExitCode
        WatchdogBreach = w.Breach
        WatchdogSurvivors = w.Survivors
        WatchdogScanError = w.ScanError
        WatchdogCleared = watchdogCleared
        TotalAllocBytes = Some r.TotalAllocBytes
        PhaseTimingsMs = r.PhaseTimingsMs
        PhaseAllocBytes = r.PhaseAllocBytes
        PlatformResolutionReads = Some c.PlatformResolutionReads
        NumericProjectionCalls = Some c.NumericProjectionCalls
        MemoryProjectionCalls = Some c.MemoryProjectionCalls
        SpatialProjectionCalls = Some c.SpatialProjectionCalls
        SpatialExecuted = Some c.SpatialExecuted
        HardwareAndKernelModuleCount = Some(c.HardwareModules + c.KernelModules)
        Cvc5Spawns = Some c.Cvc5Spawns
        UniqueObligations = Some c.UniqueObligations
        Scale1xAllocBytes = Some s.Scale1xAllocBytes
        Scale4xAllocBytes = Some s.Scale4xAllocBytes
        Scale1xWallMs = s.Scale1xWallMs
        Scale4xWallMs = s.Scale4xWallMs
        ExplicitScalingSlope = Some s.ScalingSlope
        ScalingPasses = s.ScalingPasses
        FailedTestCompile = None
        SchemaVersion = Some 1
        ProjectionScope = Some c.ProjectionScope
        LegacyProjectionSites = Some c.LegacyProjectionSites
        Cvc5SpawnsByPhase = c.Cvc5SpawnsByPhase
        Cvc5ToolIdentity = c.Cvc5ToolIdentity
        TelemetryTransport = Some t.Transport
        TelemetryDropped = Some t.Dropped
        TelemetryDrain = Some t.Drain
        TelemetryObservations = t.Observations
        TelemetryProducerOverheadBytes = t.ProducerOverheadBytes
        TelemetryProducerOverheadTicks = t.ProducerOverheadTicks
        BudgetAccepted = bOpt |> Option.map _.Accepted
        BudgetBreaches = bOpt |> Option.map _.Breaches |> Option.defaultValue []
      }
    }

  let private rejectV1OnlyBlocksInLegacy (root: JsonValue) : Result<unit, PacketParseError> =
    let label = "Canary receipt JSON"
    if tryProp "telemetry" root |> Option.isSome then
      Error(PacketParseError.InvalidJsonField(
        label, "telemetry",
        "v1-only block 'telemetry' requires explicit schemaVersion 1; unversioned legacy receipts cannot carry v1-only blocks"))
    else
      let coverageKeys = [ "projectionCoverage"; "projection_coverage"; "projectionCensus" ]
      let containers =
        root
        :: ([ "counters"; "projections"; "budget" ] |> List.choose (fun k -> tryProp k root))
      match coverageKeys |> List.tryFind (fun k -> containers |> List.exists (fun c -> tryProp k c |> Option.isSome)) with
      | Some found ->
        Error(PacketParseError.InvalidJsonField(
          label, found,
          "v1-only block 'projectionCoverage' requires explicit schemaVersion 1; unversioned legacy receipts cannot strip schemaVersion to bypass projectionCoverage enforcement"))
      | None -> Ok ()

  let private parseLegacyCanaryReceipt (root: JsonValue) : Result<CanaryReceipt, PacketParseError> =
    rejectV1OnlyBlocksInLegacy root
    |> Result.bind (fun () ->
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
                    let commandExitCode = tryInt32Prop [ "commandExitCode"; "command_exit_code" ] root
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
                      CommandExitCode = commandExitCode
                      WatchdogBreach = watchdogBreach
                      WatchdogSurvivors = watchdogSurvivors
                      WatchdogScanError = watchdogScanError
                      WatchdogCleared = watchdogCleared && (commandExitCode |> Option.forall ((=) 0))
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
                      FailedTestCompile = failedTestCompile
                      SchemaVersion = None
                      ProjectionScope = None
                      LegacyProjectionSites = None
                      Cvc5SpawnsByPhase = []
                      Cvc5ToolIdentity = None
                      TelemetryTransport = None
                      TelemetryDropped = None
                      TelemetryDrain = None
                      TelemetryObservations = None
                      TelemetryProducerOverheadBytes = None
                      TelemetryProducerOverheadTicks = None
                      BudgetAccepted = None
                      BudgetBreaches = [] }))))))))

  let private parseCanaryJson (raw: string) : Result<CanaryReceipt, PacketParseError> =
    parseJsonValue "Canary receipt JSON" raw
    |> Result.bind (fun root ->
      match root with
      | JsonValue.Object _ ->
        detectStrictSchemaVersion root
        |> Result.bind (fun strict ->
          if strict then parseStrictCanaryReceipt root
          else parseLegacyCanaryReceipt root)
      | _ -> Error(PacketParseError.ExpectedJsonObject "Canary receipt JSON"))

  let private parseCanaryText (raw: string) : Result<CanaryReceipt, PacketParseError> =
    let normalized = raw.Replace("\r\n", "\n")
    let only = function [| value |] -> Some value | _ -> None
    let watchdogLines =
      normalized.Split('\n')
      |> Array.filter (fun l -> l.TrimStart().StartsWith("WATCHDOG ", StringComparison.Ordinal))
    let watchdogLineOpt = only watchdogLines
    let uniqueMatch pattern =
      Regex.Matches(normalized, pattern) |> Seq.cast<Match> |> Seq.truncate 2 |> Seq.toArray |> only
    let phaseCompletion name =
      let elapsed = @"(?<elapsed>\d+(?:\.\d+)?)[ \t]*(?<unit>ms|s)"
      uniqueMatch (@"(?m)^[ \t]*\[" + Regex.Escape(name) + @":\d+\][ \t]+(?:Done \(" + elapsed + @"\)|" + elapsed + @")[ \t]*$")
    let feMatch = phaseCompletion "FrontEnd"
    let pdMatch = phaseCompletion "FrontEnd.ProofDischarge"
    if watchdogLines.Length = 0 && feMatch.IsNone && pdMatch.IsNone then
      Error PacketParseError.UnrecognizedCanaryFormat
    else
      let toMs completion =
        completion |> Option.bind (fun (m: Match) ->
          match Double.TryParse(m.Groups["elapsed"].Value, NumberStyles.Float, CultureInfo.InvariantCulture) with
          | true, v -> if m.Groups["unit"].Value = "ms" then Some v else Some(v * 1000.0)
          | _ -> None)
      let tryExitCode (text: string) =
        match Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture) with
        | true, code -> Some code
        | _ -> None
      let readExitCode name =
        uniqueMatch (@"(?m)^[ \t]*" + name + @"=([^\n]*)$")
        |> Option.bind (fun m -> tryExitCode (m.Groups[1].Value.Trim()))
      let totalMs, rssBytes, breach, commandExitCode, survivors, scanError =
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
          let commandExit = rxVal @"\bexitCode=(\S+)" |> Option.bind tryExitCode
          let surv = rxVal @"\bsurvivors=(.+?)(?:\s+scanError=|$)" |> normalizeWatchdogSurvivors
          let scanErr = rxVal @"\bscanError=(.+)$" |> normalizeOptionalNoneToken
          ms, rss, br, commandExit, surv, scanErr
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
        NativeExitCode = readExitCode "nativeExitCode"
        CompileExitCode = readExitCode "compileExitCode"
        CommandExitCode = commandExitCode
        WatchdogBreach = breach
        WatchdogSurvivors = survivors
        WatchdogScanError = scanError
        WatchdogCleared =
          watchdogLineOpt |> Option.exists (fun l -> l.Contains("survivors="))
          && commandExitCode = Some 0 && breach.IsNone && survivors.IsNone && scanError.IsNone
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
        SchemaVersion = None
        ProjectionScope = None
        LegacyProjectionSites = None
        Cvc5SpawnsByPhase = []
        Cvc5ToolIdentity = None
        TelemetryTransport = None
        TelemetryDropped = None
        TelemetryDrain = None
        TelemetryObservations = None
        TelemetryProducerOverheadBytes = None
        TelemetryProducerOverheadTicks = None
        BudgetAccepted = None
        BudgetBreaches = []
      }

  let parseCanaryReceipt (raw: string) : Result<CanaryReceipt, PacketParseError> =
    if String.IsNullOrWhiteSpace raw then Error(PacketParseError.EmptyInput "Canary receipt")
    elif raw.TrimStart().StartsWith("{", StringComparison.Ordinal) then parseCanaryJson raw
    else parseCanaryText raw

  let computeOverallScalingSlope (c: CanaryReceipt) : float option =
    match c.Scale1xAllocBytes, c.Scale4xAllocBytes with
    | Some a1, Some a4 -> Some(computeAllocSlope a1 a4)
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
