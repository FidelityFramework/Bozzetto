namespace Bozzetto.Audit

open System
open System.Globalization
open Fidelity.Data.JSON

// Pure Zone A domain types, closed error algebras, pure URL helpers,
// and Fidelity.Data.JSON query helpers for Blind Audit Packet assembly.
// Pure algebraic F# with closed error DUs, zero I/O, zero mutable state,
// zero reflection, and zero CLR exception handling.
[<AutoOpen>]
module AuditPacketTypes =

  [<RequireQualifiedAccess>]
  type CliError =
    | HelpRequested
    | UnknownFlag of flag: string
    | MissingFlagValue of flag: string
    | BlankValue of source: string
    | InvalidPort of raw: string
    | InvalidEnvPort of envName: string * raw: string
    | InvalidBaseSha of raw: string
    | InvalidCandidateRef of raw: string
    | InvalidIssueUrl of raw: string
    | InvalidWikiFileMapping of raw: string
    | SpecBallastQueryTooLong of length: int
    | MissingPositionalArgs of expected: int * actual: int
    | UnexpectedPositionalArg of arg: string

  module CliError =
    let describe =
      function
      | CliError.HelpRequested ->
        "usage: dotnet fsi tools/audit/make-packet.fsx <repo> <base-sha> <issue-url> [options]"
      | CliError.UnknownFlag flag -> sprintf "unknown flag '%s'" flag
      | CliError.MissingFlagValue flag -> sprintf "missing value for %s" flag
      | CliError.BlankValue source -> sprintf "blank value for %s" source
      | CliError.InvalidPort raw ->
        sprintf "invalid port for --bozzetto-port: '%s' (expected integer 1..65535)" raw
      | CliError.InvalidEnvPort(envName, raw) ->
        sprintf "invalid port in %s: '%s' (expected integer 1..65535)" envName raw
      | CliError.InvalidBaseSha raw ->
        sprintf "malformed <base-sha> '%s' (expected 7..40 hex characters)" raw
      | CliError.InvalidCandidateRef raw -> sprintf "malformed --candidate ref '%s'" raw
      | CliError.InvalidIssueUrl raw ->
        sprintf "malformed Forgejo <issue-url> '%s' (expected http(s)://<host>/<owner>/<repo>/issues/<number>)" raw
      | CliError.InvalidWikiFileMapping raw ->
        sprintf "malformed --wiki-file '%s' (expected <http(s)://host/owner/repo/wiki/page>=<path>)" raw
      | CliError.SpecBallastQueryTooLong len ->
        sprintf "--spec-ballast-query length %d exceeds 512 character bound" len
      | CliError.MissingPositionalArgs(expected, actual) ->
        sprintf "expected %d positional arguments (<repo> <base-sha> <issue-url>), got %d" expected actual
      | CliError.UnexpectedPositionalArg arg ->
        sprintf "unexpected positional argument '%s' (expected <repo> <base-sha> <issue-url>)" arg

  [<RequireQualifiedAccess>]
  type PacketParseError =
    | EmptyInput of source: string
    | InvalidJson of source: string * detail: string
    | ExpectedJsonObject of source: string
    | MissingJsonField of source: string * field: string
    | InvalidJsonField of source: string * field: string * detail: string
    | InvalidTestReceipt of detail: string
    | InvalidWikiPayload of source: string * detail: string
    | InvalidGitStatus of detail: string
    | MissingAxd000SarifPartner
    | UnrecognizedCanaryFormat

  module PacketParseError =
    let describe =
      function
      | PacketParseError.EmptyInput source -> sprintf "%s is empty." source
      | PacketParseError.InvalidJson(source, detail) -> sprintf "%s is invalid JSON: %s" source detail
      | PacketParseError.ExpectedJsonObject source -> sprintf "%s must be a JSON object." source
      | PacketParseError.MissingJsonField(source, field) -> sprintf "%s missing required field '%s'." source field
      | PacketParseError.InvalidJsonField(source, field, detail) ->
        sprintf "%s has invalid field '%s': %s" source field detail
      | PacketParseError.InvalidTestReceipt detail -> sprintf "Invalid or incomplete test receipt: %s" detail
      | PacketParseError.InvalidWikiPayload(source, detail) ->
        sprintf "Invalid wiki ruling payload for %s: %s" source detail
      | PacketParseError.InvalidGitStatus detail -> sprintf "Invalid git --name-status -z output: %s" detail
      | PacketParseError.MissingAxd000SarifPartner -> "AXD000 receipt provided without SARIF analyzer report."
      | PacketParseError.UnrecognizedCanaryFormat ->
        "Canary receipt is neither valid JSON nor a recognized WATCHDOG/CLEF_PHASE_TIMING receipt."

  [<RequireQualifiedAccess>]
  type SourceLoadError =
    | MissingDirectory of path: string
    | MissingFile of label: string * path: string
    | FileReadFailed of label: string * path: string * detail: string
    | ProcessStartFailed of executable: string * detail: string
    | ProcessTimedOut of executable: string * timeoutMs: int
    | GitFailed of args: string * exitCode: int * detail: string
    | HttpRequestFailed of target: string * detail: string
    | HttpStatusError of target: string * statusCode: int * detail: string
    | RetrievalServiceStatus of state: string * detail: string
    | ParseFailure of PacketParseError

  module SourceLoadError =
    let describe =
      function
      | SourceLoadError.MissingDirectory path -> sprintf "Repository directory does not exist: %s" path
      | SourceLoadError.MissingFile(label, path) -> sprintf "%s file not found: %s" label path
      | SourceLoadError.FileReadFailed(label, path, detail) ->
        sprintf "Failed reading %s file '%s': %s" label path detail
      | SourceLoadError.ProcessStartFailed(exe, detail) -> sprintf "Failed starting '%s': %s" exe detail
      | SourceLoadError.ProcessTimedOut(exe, timeoutMs) ->
        sprintf "Command '%s' timed out after %d ms." exe timeoutMs
      | SourceLoadError.GitFailed(args, exitCode, detail) ->
        sprintf "git %s failed (exit %d): %s" args exitCode detail
      | SourceLoadError.HttpRequestFailed(target, detail) -> sprintf "HTTP request to %s failed: %s" target detail
      | SourceLoadError.HttpStatusError(target, statusCode, detail) ->
        sprintf "HTTP %d from %s: %s" statusCode target detail
      | SourceLoadError.RetrievalServiceStatus(state, detail) ->
        sprintf "Retrieval service status '%s': %s" state detail
      | SourceLoadError.ParseFailure err -> PacketParseError.describe err

  type ForgejoIssueRef = {
    BaseUri: string
    Owner: string
    Repo: string
    Number: int
  }

  type ForgejoWikiRef = {
    BaseUri: string
    Owner: string
    Repo: string
    PageName: string
    CanonicalUrl: string
  }

  type CliOptions = {
    RepoPath: string
    BaseSha: string
    IssueUrl: string
    IssueRef: ForgejoIssueRef
    OutPath: string option
    CandidateRef: string
    IssueFile: string option
    WikiFiles: (string * string) list
    SarifPath: string option
    Axd000Path: string option
    CanaryBasePath: string option
    CanaryCandidatePath: string option
    RetrievalSnapshotPath: string option
    TestReceiptPath: string option
    SpecBallastFilePath: string option
    SpecBallastQuery: string option
    BozzettoPort: int option
    SkipRetrievalAwait: bool
    SkipClefSearch: bool
    OutsideIndex: bool
  }

  [<RequireQualifiedAccess>]
  type AuthorityProvenance =
    | LiveFetch of endpoint: string
    | LocalFile of path: string

  type WikiRuling = {
    Url: string
    Title: string
    PageName: string
    Content: string
    Provenance: AuthorityProvenance
  }

  type WikiRulingFailure = {
    Url: string
    Error: SourceLoadError
  }

  type IssueComment = {
    Id: int64 option
    Author: string
    Body: string
    HtmlUrl: string
    CreatedAt: string
  }

  type IssueAuthority = {
    Number: int
    Title: string
    State: string
    HtmlUrl: string
    Milestone: string option
    Labels: string list
    Body: string
    Comments: IssueComment list
    ExpectedCommentCount: int option
    CommentsVerified: bool
    ReferencedUrls: string list
    Provenance: AuthorityProvenance
    WikiRulings: WikiRuling list
    WikiRulingFailures: WikiRulingFailure list
  }

  [<RequireQualifiedAccess>]
  type GitChangeKind =
    | Added
    | Deleted
    | Modified
    | TypeChanged
    | Renamed of score: int
    | Copied of score: int

  type GitFileChange = {
    Kind: GitChangeKind
    OldPath: string option
    NewPath: string
    BeforeBlob: string option
    AfterBlob: string option
    ReadError: string option
  }

  type NumstatEntry = {
    Path: string
    OldPath: string option
    AddedLines: int option
    RemovedLines: int option
    IsBinary: bool
  }

  type FileLineDelta = {
    Path: string
    BeforeLines: int option
    AfterLines: int option
    AddedLines: int
    RemovedLines: int
    DeltaLines: int
    IsBinary: bool
    IsProductionSrc: bool
    ExceedsCohesionLimit: bool
  }

  type CounterDelta = {
    Added: int
    Removed: int
    Delta: int
  }

  type FileAntiPatternHit = {
    Path: string
    ConditionalWeakTable: CounterDelta
    Concurrent: CounterDelta
    SystemReflection: CounterDelta
    FSharpReflection: CounterDelta
    BclMutable: CounterDelta
    LetMutable: CounterDelta
    NativeServiceNormalize: CounterDelta
  }

  type AntiPatternDeltaReport = {
    ScannedSrcFiles: string list
    ConditionalWeakTable: CounterDelta
    Concurrent: CounterDelta
    SystemReflection: CounterDelta
    FSharpReflection: CounterDelta
    BclMutable: CounterDelta
    LetMutable: CounterDelta
    NativeServiceNormalize: CounterDelta
    MaxFileLinesBefore: int option
    MaxFileLinesAfter: int option
    DeltaMaxFileLines: int
    MaxSingleFileDelta: int
    OverBudgetFileGrowth: (string * int * int) list
    PerFileHits: FileAntiPatternHit list
  }

  type CodeDeltaReport = {
    BaseSha: string
    CandidateRef: string
    CandidateSha: string
    CandidateBranch: string option
    IsQuarantinedBranch: bool
    RawNumstat: string
    Numstat: NumstatEntry list
    Files: FileLineDelta list
    RawDiff: string
    AntiPatterns: AntiPatternDeltaReport
    UnaccountedSrcFiles: (string * string) list
  }

  type AnalyzerDiagnostic = {
    RuleId: string
    Level: string
    File: string
    Line: int option
    Message: string
    IsTargetRedRule: bool
  }

  type Axd000Digest = {
    Path: string
    Sha256: string
  }

  type AnalyzerReceipt = {
    Tools: string list
    TotalDiagnostics: int
    ErrorCount: int
    WarningCount: int
    NoteCount: int
    RedTargetDiagnostics: AnalyzerDiagnostic list
    AllDiagnostics: AnalyzerDiagnostic list
    Axd000Digests: Axd000Digest list
    RevisionIds: string list
    Axd000Disagreement: string option
    HasInvalidAxd000Entry: bool
    Axd000Valid: bool
    RawSarif: string
  }

  type TestReceipt = {
    CompileSucceeded: bool
    ExitCode: int option
    Verdict: string option
    Passed: int option
    Failed: int option
    Errored: int option
    Summary: string
  }

  type ScalingPassMetric = {
    PassName: string
    Alloc1xBytes: int64
    Alloc4xBytes: int64
    Slope: float
  }

  type CanaryReceipt = {
    Target: string
    FrontEndMs: float option
    FrontEndLimitMs: float option
    ProofDischargeMs: float option
    ProofDischargeLimitMs: float option
    TotalWallMs: float option
    WallLimitMs: float option
    PeakRssBytes: int64 option
    RssLimitBytes: int64 option
    NativeExitCode: int option
    CompileExitCode: int option
    WatchdogBreach: string option
    WatchdogSurvivors: string option
    WatchdogScanError: string option
    WatchdogCleared: bool
    TotalAllocBytes: int64 option
    PhaseTimingsMs: (string * float) list
    PhaseAllocBytes: (string * int64) list
    PlatformResolutionReads: int option
    NumericProjectionCalls: int option
    MemoryProjectionCalls: int option
    SpatialProjectionCalls: int option
    SpatialExecuted: bool option
    HardwareAndKernelModuleCount: int option
    Cvc5Spawns: int option
    UniqueObligations: int option
    Scale1xAllocBytes: int64 option
    Scale4xAllocBytes: int64 option
    Scale1xWallMs: float option
    Scale4xWallMs: float option
    ExplicitScalingSlope: float option
    ScalingPasses: ScalingPassMetric list
    FailedTestCompile: bool option
  }

  type CanaryComparison = {
    Base: CanaryReceipt
    Candidate: CanaryReceipt
    OverallScalingSlope: float option
    PhaseAllocRegressions: (string * int64 * int64 * float) list
    AllocRegressionWarnings: string list
  }

  [<RequireQualifiedAccess>]
  type RetrievalProvenance =
    | LiveAwait of endpoint: string * expectedRepo: string option * expectedSha: string option
    | SnapshotFile of path: string * expectedRepo: string option * expectedSha: string option

  type RetrievalRevisionEntry = {
    Source: string
    Revision: string option
  }

  type RetrievalSnapshot = {
    Fresh: bool
    State: string
    SnapshotId: string
    CurrentSnapshotId: string option
    Generation: int64 option
    BranchName: string option
    BranchStatus: string option
    OnMain: bool option
    Revisions: RetrievalRevisionEntry list
    Note: string option
    IsValidFresh: bool
    InvalidReason: string option
    Provenance: RetrievalProvenance
  }

  type SpecBallastHit = {
    Title: string
    Section: string
    Url: string
    ContentType: string
    Snippet: string
  }

  [<RequireQualifiedAccess>]
  type SpecBallastStatus =
    | Loaded of query: string * fusionMethod: string * hits: SpecBallastHit list
    | Skipped of reason: string
    | Unavailable of query: string * reason: SourceLoadError

  [<RequireQualifiedAccess>]
  type MechanicalVeto =
    | RedAnalyzerDiagnostic of code: string * file: string * line: int option * message: string
    | MissingAxd000Hash of reason: string
    | AntiPatternIncrease of metric: string * delta: int
    | ProjectionCountExceeded of projection: string * count: int
    | SpatialExecutedWithZeroKernelModules of spatialCalls: int
    | ScalingSlopeExceeded of passName: string * slope: float * limit: float
    | Cvc5SpawnSpike of spawns: int * cap: int * reason: string
    | CanaryWallBreach of actualMs: float * limitMs: float
    | CanaryPhaseWallBreach of phase: string * actualMs: float * limitMs: float
    | CanaryPhaseAllocRegression of phaseName: string * baseBytes: int64 * candBytes: int64 * pctIncrease: float
    | CanaryRssBreach of actualBytes: int64 * limitBytes: int64
    | CanaryExitBreach of stage: string * exitCode: int
    | CanaryWatchdogBreach of detail: string
    | FailedTestCompile of reason: string
    | StaleOrMissingRetrievalSnapshot of reason: string
    | IncompletePacket of missingParts: string list

  module MechanicalVeto =
    let describe =
      function
      | MechanicalVeto.RedAnalyzerDiagnostic(code, file, lineOpt, message) ->
        let codeText = if String.IsNullOrWhiteSpace code then "<unspecified-rule>" else code
        let loc =
          match lineOpt with
          | Some ln -> sprintf "%s:%d" file ln
          | None -> if String.IsNullOrWhiteSpace file then "<unknown>" else file
        sprintf "Red analyzer diagnostic %s at %s: %s" codeText loc message
      | MechanicalVeto.MissingAxd000Hash reason -> sprintf "Missing/invalid AXD000 hash: %s" reason
      | MechanicalVeto.AntiPatternIncrease(metric, delta) ->
        sprintf "Upward anti-pattern delta %s = +%d (> 0)" metric delta
      | MechanicalVeto.ProjectionCountExceeded(projection, count) ->
        sprintf "Projection %s called %d times (> 1 limit)" projection count
      | MechanicalVeto.SpatialExecutedWithZeroKernelModules spatialCalls ->
        sprintf "Spatial* executed (%d call(s)) with 0 hardware/kernel modules" spatialCalls
      | MechanicalVeto.ScalingSlopeExceeded(passName, slope, limit) ->
        sprintf "Scaling slope alloc(4x)/alloc(1x) on %s is %.2f (> %.1f limit)" passName slope limit
      | MechanicalVeto.Cvc5SpawnSpike(spawns, cap, reason) ->
        sprintf "cvc5 spawn spike: %d spawns (> %d cap; %s)" spawns cap reason
      | MechanicalVeto.CanaryWallBreach(actualMs, limitMs) ->
        sprintf "Canary wall-clock breach: %.1f ms (> %.1f ms limit)" actualMs limitMs
      | MechanicalVeto.CanaryPhaseWallBreach(phase, actualMs, limitMs) ->
        sprintf "Canary %s wall-clock breach: %.1f ms (> %.1f ms limit)" phase actualMs limitMs
      | MechanicalVeto.CanaryPhaseAllocRegression(phaseName, baseBytes, candBytes, pctIncrease) ->
        if Double.IsPositiveInfinity pctIncrease then
          sprintf "Canary phase '%s' heap allocation regression: +inf%% (%d -> %d bytes, > +15.0%% budget)"
            phaseName baseBytes candBytes
        else
          sprintf "Canary phase '%s' heap allocation regression: +%.1f%% (%d -> %d bytes, > +15.0%% budget)"
            phaseName pctIncrease baseBytes candBytes
      | MechanicalVeto.CanaryRssBreach(actualBytes, limitBytes) ->
        sprintf "Canary RSS breach: %.1f MiB (%d bytes > %d bytes limit)"
          (float actualBytes / 1_048_576.0) actualBytes limitBytes
      | MechanicalVeto.CanaryExitBreach(stage, exitCode) ->
        sprintf "Canary %s exit code breach: exitCode=%d (<> 0)" stage exitCode
      | MechanicalVeto.CanaryWatchdogBreach detail -> sprintf "Canary watchdog breach: %s" detail
      | MechanicalVeto.FailedTestCompile reason -> sprintf "Failed test compile: %s" reason
      | MechanicalVeto.StaleOrMissingRetrievalSnapshot reason ->
        sprintf "Stale or missing retrieval snapshot: %s" reason
      | MechanicalVeto.IncompletePacket missingParts ->
        sprintf "BLOCKED: incomplete packet (%s)" (String.concat "; " missingParts)

  type BlindAuditPacketInput = {
    RepoPath: string
    BaseSha: string
    CandidateRef: string
    IssueUrl: string
    OutsideIndex: bool
    Issue: Result<IssueAuthority, SourceLoadError>
    CodeDelta: Result<CodeDeltaReport, SourceLoadError>
    Analyzer: Result<AnalyzerReceipt option, SourceLoadError>
    TestReceipt: Result<TestReceipt option, SourceLoadError>
    CanaryBase: Result<CanaryReceipt option, SourceLoadError>
    CanaryCandidate: Result<CanaryReceipt option, SourceLoadError>
    Retrieval: Result<RetrievalSnapshot option, SourceLoadError>
    SpecBallast: SpecBallastStatus
  }

  type PacketCompleteness = {
    IsComplete: bool
    MissingParts: string list
  }

  type EvaluatedPacket = {
    Input: BlindAuditPacketInput
    Completeness: PacketCompleteness
    CanaryComparison: CanaryComparison option
    Vetoes: MechanicalVeto list
  }

  // Pure string, path, and HTTP URL utilities (zero System.IO, zero System.Uri, zero Base64/UTF-8 decoding)

  let internal nonEmptyOpt (value: string) : string option =
    if String.IsNullOrWhiteSpace value then None
    else Some(value.Trim())

  let internal normalizeSlash (path: string) : string =
    path.Replace('\\', '/')

  let internal fileNameOf (path: string) : string =
    let norm = normalizeSlash path
    match norm.LastIndexOf('/') with
    | idx when idx >= 0 -> norm.Substring(idx + 1)
    | _ -> norm

  let internal stripDiffPrefix (path: string) : string =
    let norm = normalizeSlash (path.Trim())
    if norm.StartsWith("a/", StringComparison.Ordinal) || norm.StartsWith("b/", StringComparison.Ordinal) then
      norm.Substring 2
    else
      norm

  let internal containsNullChar (s: string) : bool =
    s.IndexOf('\000') >= 0

  let internal isHexString (minLen: int) (maxLen: int) (raw: string) : bool =
    let s = raw.Trim()
    s.Length >= minLen
    && s.Length <= maxLen
    && (s |> Seq.forall (fun c ->
      (c >= '0' && c <= '9')
      || (c >= 'a' && c <= 'f')
      || (c >= 'A' && c <= 'F')))

  type internal ParsedHttpUrl = {
    Scheme: string
    Host: string
    Port: int
    Origin: string
    PathSegments: string list
  }

  let private isValidHostChar (c: char) =
    Char.IsLetterOrDigit c || c = '.' || c = '-'

  let internal tryParseHttpUrl (raw: string) : ParsedHttpUrl option =
    match nonEmptyOpt raw with
    | None -> None
    | Some trimmed ->
      if trimmed |> Seq.exists (fun c -> c <= ' ' || c = '\u007f') then None
      else
        let schemeOpt =
          if trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
            Some("https", 443, trimmed.Substring 8)
          elif trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) then
            Some("http", 80, trimmed.Substring 7)
          else None
        match schemeOpt with
        | None -> None
        | Some(scheme, defaultPort, rest) ->
          let sepIdx = rest.IndexOfAny([| '/'; '?'; '#' |])
          let authority, tail =
            if sepIdx < 0 then rest, ""
            else rest.Substring(0, sepIdx), rest.Substring sepIdx
          if String.IsNullOrEmpty authority || authority.IndexOf('@') >= 0 then None
          else
            let hostPortOpt =
              if authority.StartsWith("[", StringComparison.Ordinal) then
                let closeIdx = authority.IndexOf(']')
                if closeIdx <= 1 then None
                else
                  let host = authority.Substring(0, closeIdx + 1).ToLowerInvariant()
                  let inner = host.Substring(1, host.Length - 2)
                  let innerOk = inner |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = ':' || c = '.')
                  if innerOk then Some(host, authority.Substring(closeIdx + 1)) else None
              else
                let colonIdx = authority.IndexOf(':')
                let host, portSuffix =
                  if colonIdx < 0 then authority.ToLowerInvariant(), ""
                  else authority.Substring(0, colonIdx).ToLowerInvariant(), authority.Substring colonIdx
                let hostOk =
                  not (String.IsNullOrEmpty host)
                  && not (host.StartsWith(".", StringComparison.Ordinal))
                  && not (host.EndsWith(".", StringComparison.Ordinal))
                  && (host |> Seq.forall isValidHostChar)
                if hostOk then Some(host, portSuffix) else None
            match hostPortOpt with
            | None -> None
            | Some(host, portSuffix) ->
              let portOpt =
                if String.IsNullOrEmpty portSuffix then Some defaultPort
                elif portSuffix.StartsWith(":", StringComparison.Ordinal) then
                  let digits = portSuffix.Substring 1
                  match Int32.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture) with
                  | true, p when p >= 1 && p <= 65535 -> Some p
                  | _ -> None
                else None
              match portOpt with
              | None -> None
              | Some port ->
                let qfIdx = tail.IndexOfAny([| '?'; '#' |])
                let pathOnly = if qfIdx < 0 then tail else tail.Substring(0, qfIdx)
                let segments =
                  pathOnly.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
                  |> Array.toList
                let portPart = if port = defaultPort then "" else sprintf ":%d" port
                Some {
                  Scheme = scheme
                  Host = host
                  Port = port
                  Origin = sprintf "%s://%s%s" scheme host portPart
                  PathSegments = segments
                }

  let internal isLoopbackHost (host: string) : bool =
    let h = host.Trim().ToLowerInvariant()
    if h = "localhost" || h = "127.0.0.1" || h = "[::1]" || h = "::1" then true
    else
      let parts = h.Split('.')
      if parts.Length = 4 && parts[0] = "127" then
        parts
        |> Array.forall (fun seg ->
          match Int32.TryParse(seg, NumberStyles.None, CultureInfo.InvariantCulture) with
          | true, n -> n >= 0 && n <= 255
          | _ -> false)
      else false

  // Pure Fidelity.Data.JSON query helpers (closed error DUs, zero throws)

  let internal parseJsonValue (source: string) (raw: string) : Result<JsonValue, PacketParseError> =
    if String.IsNullOrWhiteSpace raw then Error(PacketParseError.EmptyInput source)
    else
      let cleaned = raw.TrimStart('\uFEFF')
      match Json.parse cleaned with
      | Ok value -> Ok value
      | Error detail -> Error(PacketParseError.InvalidJson(source, detail))

  let internal tryProp (name: string) (value: JsonValue) : JsonValue option =
    match value with
    | JsonValue.Object pairs -> pairs |> List.tryFindBack (fun (k, _) -> k = name) |> Option.map snd
    | _ -> None

  let internal tryPropAny (names: string list) (value: JsonValue) : JsonValue option =
    names |> List.tryPick (fun name -> tryProp name value)

  let internal tryString (value: JsonValue) : string option =
    JsonValue.asString value

  let internal tryNonBlankString (value: JsonValue) : string option =
    tryString value |> Option.bind nonEmptyOpt

  let internal tryBool (value: JsonValue) : bool option =
    JsonValue.asBool value

  let internal tryInt64 (value: JsonValue) : int64 option =
    JsonValue.asInt value

  let internal tryInt32 (value: JsonValue) : int option =
    tryInt64 value
    |> Option.bind (fun n ->
      if n >= int64 Int32.MinValue && n <= int64 Int32.MaxValue then Some(int n)
      else None)

  let internal tryFloat (value: JsonValue) : float option =
    JsonValue.asNumber value |> Option.filter Double.IsFinite

  let internal tryArray (value: JsonValue) : JsonValue list option =
    match value with
    | JsonValue.Array items -> Some items
    | _ -> None

  let internal tryObjectPairs (value: JsonValue) : (string * JsonValue) list option =
    match value with
    | JsonValue.Object pairs -> Some pairs
    | _ -> None

  let internal tryStringProp (names: string list) (value: JsonValue) : string option =
    tryPropAny names value |> Option.bind tryString

  let internal tryNonBlankStringProp (names: string list) (value: JsonValue) : string option =
    tryPropAny names value |> Option.bind tryNonBlankString

  let internal tryBoolProp (names: string list) (value: JsonValue) : bool option =
    tryPropAny names value |> Option.bind tryBool

  let internal tryInt32Prop (names: string list) (value: JsonValue) : int option =
    tryPropAny names value |> Option.bind tryInt32

  let internal tryInt64Prop (names: string list) (value: JsonValue) : int64 option =
    tryPropAny names value |> Option.bind tryInt64

  let internal tryFloatProp (names: string list) (value: JsonValue) : float option =
    tryPropAny names value |> Option.bind tryFloat
