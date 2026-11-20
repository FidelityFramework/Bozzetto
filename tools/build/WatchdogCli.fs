namespace Bozzetto.Build

open System
open System.Globalization
open Bozzetto

// CLI argument, environment, canary-preset, and receipt formatting rules for
// tools/build/lease.fsx. Lives under tools/build/ so Bozzetto.Core stays free of
// harness/CLI concerns while Bozzetto.Tests compiles and verifies every rule.
module WatchdogCli =

  let private maxRssLimitMb : int64 = Int64.MaxValue / 1_048_576L

  // Named canary presets aligned with Composer wiki Pipeline Guardrails and Canary Budgets (Layer B).
  let tryCanaryPreset (name: string) : ProcessWatchdog.FuseLimits option =
    if String.IsNullOrWhiteSpace name then None
    else
      match name.Trim().ToLowerInvariant() with
      | "hello" | "canary1" | "01_helloworlddirect" ->
        Some { WallLimitSeconds = Some 15.0; RssLimitBytes = Some(512L * 1_048_576L) }
      | "hello-p3" | "canary1-p3" ->
        Some { WallLimitSeconds = Some 5.0; RssLimitBytes = Some(384L * 1_048_576L) }
      | "sqlite" | "canary2" | "fidelity.sqlite" ->
        Some { WallLimitSeconds = Some 15.0; RssLimitBytes = Some(768L * 1_048_576L) }
      | "challenge-p3" | "canary3-p3" ->
        Some { WallLimitSeconds = Some 15.0; RssLimitBytes = Some(1024L * 1_048_576L) }
      | "challenge" | "canary3" | "challengeserver" ->
        Some { WallLimitSeconds = Some 30.0; RssLimitBytes = Some(1228L * 1_048_576L) }
      | _ -> None

  [<RequireQualifiedAccess>]
  type WatchdogOptionError =
    | MissingValue of flagName: string
    | UnknownCanaryPreset of name: string
    | InvalidPositiveFloat of source: string * raw: string
    | InvalidPositiveInt64 of source: string * raw: string
    | InvalidBoolean of source: string * raw: string
    | EmptyReceiptPath of source: string

  module WatchdogOptionError =
    let describe =
      function
      | WatchdogOptionError.MissingValue flag -> sprintf "missing value for %s" flag
      | WatchdogOptionError.UnknownCanaryPreset name ->
        sprintf "unknown canary preset '%s' (expected hello|hello-p3|sqlite|challenge|challenge-p3)" name
      | WatchdogOptionError.InvalidPositiveFloat(source, raw) ->
        sprintf "invalid positive number for %s: '%s'" source raw
      | WatchdogOptionError.InvalidPositiveInt64(source, raw) ->
        sprintf "invalid positive integer MB for %s: '%s'" source raw
      | WatchdogOptionError.InvalidBoolean(source, raw) ->
        sprintf "invalid boolean for %s: '%s' (expected 1|0|true|false|yes|no|on|off)" source raw
      | WatchdogOptionError.EmptyReceiptPath source ->
        sprintf "empty receipt path for %s" source

  type WatchdogOptions = {
    CanaryPreset: string option
    Limits: ProcessWatchdog.FuseLimits
    ReceiptPath: string option
    PinZ13: bool
  }

  let parsePinZ13Env (rawOpt: string option) : Result<bool, WatchdogOptionError> =
    match rawOpt with
    | None -> Ok false
    | Some s when String.IsNullOrWhiteSpace s -> Ok false
    | Some s ->
      match s.Trim().ToLowerInvariant() with
      | "1" | "true" | "yes" | "on" -> Ok true
      | "0" | "false" | "no" | "off" -> Ok false
      | _ -> Error(WatchdogOptionError.InvalidBoolean("BOZZETTO_PIN_Z13", s))

  let private parsePositiveFloat (source: string) (raw: string) : Result<float, WatchdogOptionError> =
    match Double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, v when Double.IsFinite v && v > 0.0 -> Ok v
    | _ -> Error(WatchdogOptionError.InvalidPositiveFloat(source, raw))

  let private parsePositiveRssBytes (source: string) (raw: string) : Result<int64, WatchdogOptionError> =
    match Int64.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture) with
    | true, mb when mb > 0L && mb <= maxRssLimitMb -> Ok(mb * 1_048_576L)
    | _ -> Error(WatchdogOptionError.InvalidPositiveInt64(source, raw))

  // Parse watchdog environment variables strictly (never silently ignoring malformed values).
  let parseWatchdogEnv
      (canaryEnv: string option)
      (wallSecsEnv: string option)
      (rssMbEnv: string option)
      (receiptPathEnv: string option)
      : Result<WatchdogOptions, WatchdogOptionError> =
    let nonEmpty opt = opt |> Option.filter (fun s -> not (String.IsNullOrWhiteSpace s))
    let canaryRes =
      match nonEmpty canaryEnv with
      | None -> Ok(None, ProcessWatchdog.noLimits)
      | Some name ->
        match tryCanaryPreset name with
        | Some preset -> Ok(Some(name.Trim()), preset)
        | None -> Error(WatchdogOptionError.UnknownCanaryPreset name)
    canaryRes
    |> Result.bind (fun (presetName, baseLimits) ->
      match nonEmpty wallSecsEnv with
      | None -> Ok(presetName, baseLimits)
      | Some raw ->
        parsePositiveFloat "BOZZETTO_WALL_LIMIT_SECS" raw
        |> Result.map (fun secs -> presetName, { baseLimits with WallLimitSeconds = Some secs }))
    |> Result.bind (fun (presetName, limits) ->
      match nonEmpty rssMbEnv with
      | None -> Ok(presetName, limits)
      | Some raw ->
        parsePositiveRssBytes "BOZZETTO_RSS_LIMIT_MB" raw
        |> Result.map (fun bytes -> presetName, { limits with RssLimitBytes = Some bytes }))
    |> Result.map (fun (presetName, limits) ->
      { CanaryPreset = presetName
        Limits = limits
        ReceiptPath = nonEmpty receiptPathEnv |> Option.map _.Trim()
        PinZ13 = false })

  // Parse leading lease.fsx watchdog flags before <kind> <command> [args...].
  // Explicit --wall-limit-secs / --rss-limit-mb always override --canary regardless
  // of flag order on the command line.
  let parseWatchdogArgs (defaults: WatchdogOptions) (argv: string list) : Result<WatchdogOptions * string list, WatchdogOptionError> =
    let rec loop
        (presetName: string option)
        (presetLimits: ProcessWatchdog.FuseLimits option)
        (explicitWall: float option)
        (explicitRss: int64 option)
        (receiptPath: string option)
        (pinZ13: bool)
        (remaining: string list) =
      match remaining with
      | "--canary" :: value :: rest ->
        match tryCanaryPreset value with
        | Some preset -> loop (Some(value.Trim())) (Some preset) explicitWall explicitRss receiptPath pinZ13 rest
        | None -> Error(WatchdogOptionError.UnknownCanaryPreset value)
      | [ "--canary" ] -> Error(WatchdogOptionError.MissingValue "--canary")
      | "--wall-limit-secs" :: value :: rest ->
        match parsePositiveFloat "--wall-limit-secs" value with
        | Ok secs -> loop presetName presetLimits (Some secs) explicitRss receiptPath pinZ13 rest
        | Error err -> Error err
      | [ "--wall-limit-secs" ] -> Error(WatchdogOptionError.MissingValue "--wall-limit-secs")
      | "--rss-limit-mb" :: value :: rest ->
        match parsePositiveRssBytes "--rss-limit-mb" value with
        | Ok bytes -> loop presetName presetLimits explicitWall (Some bytes) receiptPath pinZ13 rest
        | Error err -> Error err
      | [ "--rss-limit-mb" ] -> Error(WatchdogOptionError.MissingValue "--rss-limit-mb")
      | "--receipt-path" :: value :: rest ->
        if String.IsNullOrWhiteSpace value then Error(WatchdogOptionError.EmptyReceiptPath "--receipt-path")
        else loop presetName presetLimits explicitWall explicitRss (Some(value.Trim())) pinZ13 rest
      | [ "--receipt-path" ] -> Error(WatchdogOptionError.MissingValue "--receipt-path")
      | "--pin-z13" :: rest ->
        loop presetName presetLimits explicitWall explicitRss receiptPath true rest
      | rest ->
        let baseLimits = presetLimits |> Option.defaultValue defaults.Limits
        let resolvedLimits : ProcessWatchdog.FuseLimits = {
          WallLimitSeconds = explicitWall |> Option.orElse baseLimits.WallLimitSeconds
          RssLimitBytes = explicitRss |> Option.orElse baseLimits.RssLimitBytes
        }
        Ok(
          { CanaryPreset = presetName
            Limits = resolvedLimits
            ReceiptPath = receiptPath
            PinZ13 = pinZ13 },
          rest)
    loop defaults.CanaryPreset None None None defaults.ReceiptPath defaults.PinZ13 argv

  type WatchdogReceipt = {
    RootPid: int
    TargetPgid: int option
    WallElapsedMs: int64
    PeakRssBytes: int64
    Breach: ProcessWatchdog.FuseBreach option
    ScanError: ProcessWatchdog.ProcScanError option
    ExitCode: int
    Termination: ProcessWatchdog.TerminationOutcome
  }

  let formatReceipt (receipt: WatchdogReceipt) : string =
    let pgidText =
      match receipt.TargetPgid with
      | Some pgid -> string pgid
      | None -> "none"
    let survivorsText =
      match receipt.Termination with
      | ProcessWatchdog.TerminationOutcome.Cleared -> "none"
      | ProcessWatchdog.TerminationOutcome.Survivors(pids, errs) ->
        sprintf "pids=%s;errors=%s"
          (pids |> List.map string |> String.concat ",")
          (errs |> String.concat "|")
      | ProcessWatchdog.TerminationOutcome.Unverified err ->
        sprintf "unverified(%s)" (ProcessWatchdog.describeProcScanError err)
    let scanSuffix =
      match receipt.ScanError with
      | None -> ""
      | Some err -> sprintf " scanError=%s" (ProcessWatchdog.describeProcScanError err)
    sprintf "WATCHDOG rootPid=%d pgid=%s elapsedMs=%d peakRssMB=%.1f peakRssBytes=%d breach=%s exitCode=%d survivors=%s%s"
      receipt.RootPid
      pgidText
      receipt.WallElapsedMs
      (float receipt.PeakRssBytes / 1_048_576.0)
      receipt.PeakRssBytes
      (ProcessWatchdog.describeBreach receipt.Breach)
      receipt.ExitCode
      survivorsText
      scanSuffix

  // Append a watchdog receipt line to a bounded stderr tail without exceeding
  // McpServer's CommandFinished.StandardError wire limit (streamLimit + 64).
  let appendBoundedReceipt (streamLimit: int) (stderrTail: string) (receiptLine: string) (alreadyTruncated: bool) : string * bool =
    let prefix = "[cut: earlier command output omitted]\n"
    let suffix =
      if String.IsNullOrEmpty stderrTail then receiptLine
      elif stderrTail.EndsWith("\n", StringComparison.Ordinal) then receiptLine
      else "\n" + receiptLine
    let combined = stderrTail + suffix
    let maxWireLength = streamLimit + 64
    if combined.Length <= maxWireLength then
      combined, alreadyTruncated
    else
      let keepBudget = max 0 (streamLimit - suffix.Length)
      let trimmedBody =
        if keepBudget = 0 then ""
        elif stderrTail.Length <= keepBudget then stderrTail
        else stderrTail.Substring(stderrTail.Length - keepBudget)
      let result =
        let candidate = prefix + trimmedBody + suffix
        if candidate.Length <= maxWireLength then candidate
        else receiptLine.Substring(0, min receiptLine.Length maxWireLength)
      result, true
