// Zero-poll SSE observer and wait-on-signal freshness barrier for Fanion
// retrieval readiness. Facts publish monotonically through UiBridge.Facts.Graph
// and local status/await adapters; no polling timers or retry loops exist here.
module Bozzetto.Server.RetrievalObservation

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Threading
open System.Threading.Tasks
open Fidelity.Data.JSON
open Bozzetto.Utils
open Bozzetto.Web.Shared.Protocol

[<Literal>]
let MaxConfigBytes = 131072L

[<Literal>]
let MaxEventBytes = 512 * 1024

[<Literal>]
let MaxTokenLength = 4096

[<Literal>]
let MaxReasonLength = 512

type Config = {
  Endpoint: Uri
  TokenFile: string
}

type BranchInfo = {
  BranchName: string
  OnMain: bool
  Repository: string option
  HeadRevision: string option
}

type FreshState = {
  Generation: int64
  SnapshotId: string
  PublishedAtMs: int64
  Revisions: RetrievalRevision array
}

type IndexingState = {
  Generation: int64
  SnapshotId: string
  ChangedAtMs: int64 option
  QueuedRetrievals: int32
  PendingSources: string array
  StaleSources: string array
  Reasons: string array
}

type CrossingHeldState = {
  Generation: int64
  SnapshotId: string
  ChangedAtMs: int64 option
  HeldUntilMs: int64 option
  QueuedRetrievals: int32
  ExpectedSources: string array
  PendingSources: string array
  StaleSources: string array
  Reasons: string array
}

[<RequireQualifiedAccess>]
type Readiness =
  | Fresh of FreshState
  | Indexing of IndexingState
  | CrossingHeld of CrossingHeldState
  | Unavailable of reason: string
  | Unconfigured

type Observation = {
  Serial: int64
  ObservedAtMs: int64
  Branch: BranchInfo
  Readiness: Readiness
  LastFreshSources: string array
}

[<RequireQualifiedAccess>]
type AwaitOutcome =
  | Fresh of Observation
  | Timeout of current: Observation
  | Unconfigured of current: Observation
  | Unavailable of current: Observation
  | Cancelled of current: Observation
  | Closed of current: Observation
  | InvalidFilter of reason: string

let private readTokenFile (tokenPath: string) : string =
  let info = FileInfo(tokenPath)
  if not info.Exists || info.Length > int64 MaxTokenLength + 64L then
    raise (InvalidDataException "retrieval_credential_invalid")
  File.ReadAllText tokenPath

let private nowMs () = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

let private boundReason (reason: string) =
  if isNull reason then ""
  elif reason.Length <= MaxReasonLength then reason
  else reason.Substring(0, MaxReasonLength)

let private expandPath (path: string) =
  if path.StartsWith("~/", StringComparison.Ordinal) then
    Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, path.Substring 2)
  else path

let private jsonField (key: string) = function
  | JsonValue.Object fields -> fields |> List.tryFind (fst >> (=) key) |> Option.map snd
  | _ -> None

let parseConfig (text: string) : Result<Config option, string> =
  if isNull text then Error "Retrieval configuration is not valid JSON."
  else
    match Json.parse text with
    | Error _ -> Error "Retrieval configuration is not valid JSON."
    | Ok root ->
      match root with
      | JsonValue.Object _ ->
        match jsonField "retrieval_endpoint" root with
        | None | Some JsonValue.Null -> Ok None
        | Some(JsonValue.String endpoint) when String.IsNullOrWhiteSpace endpoint -> Ok None
        | Some(JsonValue.String endpoint) ->
          match Uri.TryCreate(endpoint.Trim(), UriKind.Absolute) with
          | true, uri when (uri.Scheme = "https" || uri.Scheme = "http") && uri.UserInfo = "" && uri.Query = "" && uri.Fragment = "" ->
            if uri.Scheme = "http" && not uri.IsLoopback then
              Error "Non-loopback retrieval endpoint must use HTTPS."
            else
              match jsonField "token_file" root with
              | Some(JsonValue.String tokenFile) when not (String.IsNullOrWhiteSpace tokenFile) ->
                let fullTokenPath = expandPath (tokenFile.Trim())
                if not (Path.IsPathFullyQualified fullTokenPath) then
                  Error "Retrieval token_file must be absolute."
                else
                  let rootUri = uri.AbsoluteUri.TrimEnd('/') + "/"
                  Ok(Some { Endpoint = Uri(rootUri + "v1/retrieval/observe"); TokenFile = fullTokenPath })
              | _ -> Error "Retrieval configuration requires a non-empty absolute token_file."
          | _ -> Error "Retrieval endpoint must be HTTP(S), without credentials, query or fragment."
        | Some _ -> Error "Retrieval endpoint must be a string."
      | _ -> Error "Retrieval configuration must be a JSON object."

let private loadConfigFile (path: string) : Result<Config option, string> =
  let fullPath = expandPath (path.Trim())
  if not (Path.IsPathFullyQualified fullPath) then
    Error "Retrieval configuration path must be absolute."
  else
    try
      if not (File.Exists fullPath) then
        Error "Retrieval configuration file does not exist."
      elif FileInfo(fullPath).Length > MaxConfigBytes then
        Error "Retrieval configuration exceeds 128 KiB."
      else
        File.ReadAllText fullPath |> parseConfig
    with _ -> Error "Retrieval configuration could not be read."

let loadConfig (path: string option) : Result<Config option, string> =
  let explicit =
    match path with
    | Some p when not (String.IsNullOrWhiteSpace p) -> Some p
    | _ ->
      let envRetrieval = Environment.GetEnvironmentVariable "BOZZETTO_RETRIEVAL_CONFIG"
      if not (String.IsNullOrWhiteSpace envRetrieval) then Some envRetrieval
      else
        let envPeers = Environment.GetEnvironmentVariable "BOZZETTO_WORK_PEERS_FILE"
        if not (String.IsNullOrWhiteSpace envPeers) then Some envPeers
        else None
  match explicit with
  | Some p -> loadConfigFile p
  | None ->
    let defaultPath = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".pi", "agent", "lab.json")
    try
      if File.Exists defaultPath then loadConfigFile defaultPath
      else Ok None
    with _ -> Ok None

let private isHexChar (c: char) =
  (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

let internal tryNormalizeSha40 (value: string) : string option =
  if isNull value then None
  else
    let trimmed = value.Trim()
    if trimmed.Length = 40 && (trimmed |> Seq.forall isHexChar) then
      Some(trimmed.ToLowerInvariant())
    else None

let private readSharedText (path: string) : string option =
  try
    if not (File.Exists path) then None
    else
      use fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
      use reader = new StreamReader(fs, Encoding.UTF8)
      Some(reader.ReadToEnd())
  with _ -> None

let private resolveCommonGitDir (gitDir: string) : string =
  match readSharedText (Path.Combine(gitDir, "commondir")) with
  | Some raw when not (String.IsNullOrWhiteSpace raw) ->
    let rel = raw.Trim()
    if Path.IsPathFullyQualified rel then Path.GetFullPath rel
    else Path.GetFullPath(Path.Combine(gitDir, rel))
  | _ -> gitDir

let private isSafeGitRefPath (target: string) : bool =
  not (String.IsNullOrWhiteSpace target)
  && target.StartsWith("refs/", StringComparison.Ordinal)
  && not (target.Contains('\\'))
  && not (target.Contains(".."))
  && not (Path.IsPathRooted target)

let private tryResolvePackedRef (packedRefsPath: string) (targetRef: string) : string option =
  match readSharedText packedRefsPath with
  | None -> None
  | Some content ->
    content.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.tryPick (fun rawLine ->
      let line = rawLine.Trim()
      if line.Length = 0 || line[0] = '#' || line[0] = '^' then None
      else
        let sep = line.IndexOfAny([| ' '; '\t' |])
        if sep <= 0 || sep + 1 >= line.Length then None
        else
          let shaPart = line.Substring(0, sep)
          let refPart = line.Substring(sep + 1).Trim()
          if refPart = targetRef then tryNormalizeSha40 shaPart else None)

type private LooseRefRead =
  | LoosePresent of string option
  | LooseAbsent
  | LooseUnreadable

let private readLooseRefFile (path: string) : LooseRefRead =
  try
    use fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
    use reader = new StreamReader(fs, Encoding.UTF8)
    LoosePresent(tryNormalizeSha40 (reader.ReadToEnd()))
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> LooseAbsent
  | _ -> LooseUnreadable

let private resolveRefRevision (gitDir: string) (targetRef: string) : string option =
  if not (isSafeGitRefPath targetRef) then None
  else
    let commonDir = resolveCommonGitDir gitDir
    let parts = targetRef.Split('/')
    let readLoose baseDir =
      let refPath = Path.Combine([| yield baseDir; yield! parts |])
      readLooseRefFile refPath
    match readLoose gitDir with
    | LoosePresent rev -> rev
    | LooseUnreadable -> None
    | LooseAbsent ->
      match if commonDir <> gitDir then readLoose commonDir else LooseAbsent with
      | LoosePresent rev -> rev
      | LooseUnreadable -> None
      | LooseAbsent ->
        tryResolvePackedRef (Path.Combine(commonDir, "packed-refs")) targetRef
        |> Option.orElseWith (fun () -> if commonDir <> gitDir then tryResolvePackedRef (Path.Combine(gitDir, "packed-refs")) targetRef else None)

let private unknownBranch = { BranchName = ""; OnMain = false; Repository = None; HeadRevision = None }

let private parseHeadContent (gitDir: string option) (repository: string) (content: string) : BranchInfo =
  let trimmed = if isNull content then "" else content.Trim()
  if trimmed.Length = 0 then
    unknownBranch
  elif trimmed.StartsWith("ref:", StringComparison.Ordinal) then
    let target = trimmed.Substring(4).Trim()
    let headRev = gitDir |> Option.bind (fun dir -> resolveRefRevision dir target)
    if target.StartsWith("refs/heads/", StringComparison.Ordinal) then
      let name = target.Substring("refs/heads/".Length)
      if String.IsNullOrWhiteSpace name then unknownBranch
      else { BranchName = name; OnMain = (name = "main" || name = "master"); Repository = Some repository; HeadRevision = headRev }
    elif target.Length > 0 then
      { BranchName = target; OnMain = false; Repository = Some repository; HeadRevision = headRev }
    else
      unknownBranch
  else
    let shortSha = if trimmed.Length > 12 then trimmed.Substring(0, 12) else trimmed
    { BranchName = "detached:" + shortSha; OnMain = false; Repository = Some repository; HeadRevision = tryNormalizeSha40 trimmed }

let private normalizeWorkingDir (workingDir: string) : string =
  if String.IsNullOrWhiteSpace workingDir then ""
  else
    try
      let full = Path.GetFullPath(workingDir.Trim())
      let root = Path.GetPathRoot full
      if not (isNull root) && full.Length > root.Length then
        full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
      else full
    with _ -> ""

type private Checkout = { GitDirectory: string; Repository: string }

let private resolveCheckout (workingDir: string) : Checkout option =
  let normalized = normalizeWorkingDir workingDir
  if normalized.Length = 0 then None
  else
    try
      let rec walk (dir: string) =
        let dotGit = Path.Combine(dir, ".git")
        if Directory.Exists dotGit && File.Exists(Path.Combine(dotGit, "HEAD")) then
          Some { GitDirectory = dotGit; Repository = Path.GetFileName dir }
        elif File.Exists dotGit then
          let line =
            use fs = new FileStream(dotGit, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
            use reader = new StreamReader(fs, Encoding.UTF8)
            reader.ReadLine()
          if not (isNull line) && line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase) then
            let rawGitDir = line.Substring(7).Trim()
            if String.IsNullOrWhiteSpace rawGitDir then None
            else
              let resolvedGitDir =
                if Path.IsPathFullyQualified rawGitDir then Path.GetFullPath rawGitDir
                else Path.GetFullPath(Path.Combine(dir, rawGitDir))
              if Directory.Exists resolvedGitDir && File.Exists(Path.Combine(resolvedGitDir, "HEAD")) then
                Some { GitDirectory = resolvedGitDir; Repository = Path.GetFileName dir }
              else None
          else None
        else
          match Directory.GetParent dir with
          | null -> None
          | parent -> walk parent.FullName
      if Directory.Exists normalized then walk normalized
      else None
    with _ -> None

let resolveGitHeadDirectory workingDir =
  resolveCheckout workingDir |> Option.map _.GitDirectory

let private readBranchInCheckout (checkout: Checkout) : BranchInfo =
  let headPath = Path.Combine(checkout.GitDirectory, "HEAD")
  let rec readAttempt remaining =
    if not (File.Exists headPath) then
      unknownBranch
    else
      try
        let content =
          use fs = new FileStream(headPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
          use reader = new StreamReader(fs, Encoding.UTF8)
          reader.ReadToEnd()
        if String.IsNullOrWhiteSpace content && remaining > 0 then
          Thread.Yield() |> ignore
          readAttempt (remaining - 1)
        else
          parseHeadContent (Some checkout.GitDirectory) checkout.Repository content
      with
      | :? IOException when remaining > 0 ->
        Thread.Yield() |> ignore
        readAttempt (remaining - 1)
      | _ -> unknownBranch
  try readAttempt 2
  with _ -> unknownBranch

let readCheckoutBranch (workingDir: string) : BranchInfo =
  match resolveCheckout workingDir with
  | Some checkout -> readBranchInCheckout checkout
  | None -> unknownBranch

let private matchesSource (expectedRepo: string) (sourceKey: string) =
  let normalize (source: string) =
    let name = source.Trim().ToLowerInvariant()
    if name.EndsWith(".git", StringComparison.Ordinal) then name.Substring(0, name.Length - 4) else name
  let bare (name: string) = name.Substring(name.LastIndexOf('/') + 1)
  let req, key = normalize expectedRepo, normalize sourceKey
  req <> "" && key <> "" && (req = key || bare req = key || bare key = req)

let private classifyBranch (branch: BranchInfo) (obs: Observation) : string =
  let pendingStatus matches sources =
    if Array.exists matches obs.LastFreshSources || List.exists (Array.exists matches) sources then
      "indexing_pending"
    else "unmanaged_checkout"
  match branch.Repository, obs.Readiness with
  | _, _ when not branch.OnMain && not (String.IsNullOrWhiteSpace branch.BranchName) -> "quarantined_branch"
  | _, Readiness.Unconfigured -> "unconfigured"
  | _, Readiness.Unavailable _ -> "unavailable"
  | None, _ -> "branch_unknown"
  | Some repo, Readiness.Fresh fresh ->
    if fresh.Revisions |> Array.exists (fun revision -> matchesSource repo revision.Source) then "on_main_indexed"
    else "unmanaged_checkout"
  | Some repo, Readiness.Indexing indexing ->
    pendingStatus (matchesSource repo) [ indexing.PendingSources; indexing.StaleSources ]
  | Some repo, Readiness.CrossingHeld held ->
    pendingStatus (matchesSource repo) [ held.ExpectedSources; held.PendingSources; held.StaleSources ]

let private reqString (key: string) (obj: JsonValue) : Result<string, string> =
  match jsonField key obj with
  | Some(JsonValue.String s) -> Ok s
  | _ -> Error(sprintf "Missing or invalid string field '%s'." key)

let private reqNonNegativeInt64 (key: string) (obj: JsonValue) : Result<int64, string> =
  match jsonField key obj |> Option.bind JsonValue.tryAsInt64 with
  | Some value when value >= 0L -> Ok value
  | _ -> Error(sprintf "Missing or invalid non-negative integer field '%s'." key)

let private optNonNegativeInt64 (key: string) (obj: JsonValue) : Result<int64 option, string> =
  match jsonField key obj with
  | None | Some JsonValue.Null -> Ok None
  | Some v ->
    match JsonValue.tryAsInt64 v with
    | Some value when value >= 0L -> Ok(Some value)
    | _ -> Error(sprintf "Invalid non-negative integer field '%s'." key)

let private optNonNegativeInt32 (key: string) (obj: JsonValue) : Result<int32, string> =
  match jsonField key obj with
  | None | Some JsonValue.Null -> Ok 0
  | Some v ->
    match JsonValue.tryAsInt64 v with
    | Some value when value >= 0L && value <= int64 Int32.MaxValue -> Ok(int32 value)
    | _ -> Error(sprintf "Invalid non-negative int32 field '%s'." key)

let private reqStringArray (key: string) (obj: JsonValue) : Result<string array, string> =
  match jsonField key obj with
  | Some(JsonValue.Array items) ->
    let result = ResizeArray<string>(items.Length)
    let mutable bad = false
    for item in items do
      match item with
      | JsonValue.String s -> result.Add s
      | _ -> bad <- true
    if bad then Error(sprintf "Field '%s' must be an array of strings." key)
    else Ok(result.ToArray())
  | _ -> Error(sprintf "Missing or invalid string array field '%s'." key)

let private reqRevisions (key: string) (obj: JsonValue) : Result<RetrievalRevision array, string> =
  match jsonField key obj with
  | Some(JsonValue.Object pairs) ->
    let result = ResizeArray<RetrievalRevision>(pairs.Length)
    let mutable bad = false
    for source, value in pairs do
      match value with
      | JsonValue.String rev -> result.Add { Source = source; Revision = Some rev }
      | JsonValue.Null -> result.Add { Source = source; Revision = None }
      | _ -> bad <- true
    if bad then Error(sprintf "Field '%s' must map source names to strings or null." key)
    else
      let arr = result.ToArray()
      Array.sortInPlaceWith (fun a b -> StringComparer.Ordinal.Compare(a.Source, b.Source)) arr
      Ok arr
  | _ -> Error(sprintf "Missing or invalid revisions map field '%s'." key)

let parseReadinessJson (text: string) : Result<Readiness, string> =
  if isNull text then Error "Readiness frame is not valid JSON."
  else
    match Json.parse text with
    | Error _ -> Error "Readiness frame is not valid JSON."
    | Ok(JsonValue.Object _ as root) ->
      match reqString "state" root with
      | Error err -> Error err
      | Ok "fresh" ->
        match reqNonNegativeInt64 "generation" root,
              reqString "snapshot_id" root,
              reqNonNegativeInt64 "published_at_ms" root,
              reqRevisions "revisions" root with
        | Ok gen, Ok snapshotId, Ok publishedAtMs, Ok revisions ->
          Ok(Readiness.Fresh {
            Generation = gen
            SnapshotId = snapshotId
            PublishedAtMs = publishedAtMs
            Revisions = revisions
          })
        | Error e, _, _, _ | _, Error e, _, _ | _, _, Error e, _ | _, _, _, Error e -> Error e
      | Ok "indexing" ->
        match reqNonNegativeInt64 "generation" root,
              reqString "snapshot_id" root,
              reqStringArray "pending_sources" root,
              reqStringArray "stale_sources" root,
              reqStringArray "reasons" root,
              optNonNegativeInt64 "changed_at_ms" root,
              optNonNegativeInt32 "queued_retrievals" root with
        | Ok gen, Ok snapshotId, Ok pending, Ok stale, Ok reasons, Ok changedAtMs, Ok queued ->
          Ok(Readiness.Indexing {
            Generation = gen
            SnapshotId = snapshotId
            ChangedAtMs = changedAtMs
            QueuedRetrievals = queued
            PendingSources = pending
            StaleSources = stale
            Reasons = reasons
          })
        | Error e, _, _, _, _, _, _
        | _, Error e, _, _, _, _, _
        | _, _, Error e, _, _, _, _
        | _, _, _, Error e, _, _, _
        | _, _, _, _, Error e, _, _
        | _, _, _, _, _, Error e, _
        | _, _, _, _, _, _, Error e -> Error e
      | Ok "crossing_held" ->
        match reqNonNegativeInt64 "generation" root,
              reqString "snapshot_id" root,
              reqStringArray "expected_sources" root,
              reqStringArray "pending_sources" root,
              reqStringArray "stale_sources" root,
              reqStringArray "reasons" root,
              optNonNegativeInt64 "held_until_ms" root,
              optNonNegativeInt32 "queued_retrievals" root,
              optNonNegativeInt64 "changed_at_ms" root with
        | Ok gen, Ok snapshotId, Ok expected, Ok pending, Ok stale, Ok reasons, Ok heldUntilMs, Ok queued, Ok changedAtMs ->
          Ok(Readiness.CrossingHeld {
            Generation = gen
            SnapshotId = snapshotId
            ChangedAtMs = changedAtMs
            HeldUntilMs = heldUntilMs
            QueuedRetrievals = queued
            ExpectedSources = expected
            PendingSources = pending
            StaleSources = stale
            Reasons = reasons
          })
        | Error e, _, _, _, _, _, _, _, _
        | _, Error e, _, _, _, _, _, _, _
        | _, _, Error e, _, _, _, _, _, _
        | _, _, _, Error e, _, _, _, _, _
        | _, _, _, _, Error e, _, _, _, _
        | _, _, _, _, _, Error e, _, _, _
        | _, _, _, _, _, _, Error e, _, _
        | _, _, _, _, _, _, _, Error e, _
        | _, _, _, _, _, _, _, _, Error e -> Error e
      | Ok "unavailable" ->
        match reqString "reason" root with
        | Ok reason when not (String.IsNullOrWhiteSpace reason) ->
          Ok(Readiness.Unavailable(boundReason reason))
        | _ -> Error "Unavailable readiness frame requires a non-empty reason."
      | Ok other -> Error(sprintf "Unknown readiness state '%s'." other)
    | Ok _ -> Error "Readiness frame must be a JSON object."

let stateName = function
  | Readiness.Fresh _ -> "fresh"
  | Readiness.CrossingHeld _ -> "crossing_held"
  | Readiness.Indexing _ -> "indexing"
  | Readiness.Unavailable _ -> "unavailable"
  | Readiness.Unconfigured -> "unconfigured"

let toWireStatusForBranch (branch: BranchInfo) (obs: Observation) : RetrievalStatus =
  let state = stateName obs.Readiness
  let branchStatus = classifyBranch branch obs
  match obs.Readiness with
  | Readiness.Fresh f ->
    { Revision = 0L
      State = state
      BranchStatus = branchStatus
      BranchName = branch.BranchName
      OnMain = branch.OnMain
      Generation = f.Generation
      SnapshotId = f.SnapshotId
      PublishedAtMs = f.PublishedAtMs
      ChangedAtMs = None
      HeldUntilMs = None
      QueuedRetrievals = 0
      ExpectedSources = [||]
      PendingSources = [||]
      StaleSources = [||]
      Reasons = [||]
      Revisions = f.Revisions
      Note = "" }
  | Readiness.Indexing idx ->
    { Revision = 0L
      State = state
      BranchStatus = branchStatus
      BranchName = branch.BranchName
      OnMain = branch.OnMain
      Generation = idx.Generation
      SnapshotId = idx.SnapshotId
      PublishedAtMs = 0L
      ChangedAtMs = idx.ChangedAtMs
      HeldUntilMs = None
      QueuedRetrievals = idx.QueuedRetrievals
      ExpectedSources = [||]
      PendingSources = idx.PendingSources
      StaleSources = idx.StaleSources
      Reasons = idx.Reasons
      Revisions = [||]
      Note = "" }
  | Readiness.CrossingHeld held ->
    { Revision = 0L
      State = state
      BranchStatus = branchStatus
      BranchName = branch.BranchName
      OnMain = branch.OnMain
      Generation = held.Generation
      SnapshotId = held.SnapshotId
      PublishedAtMs = 0L
      ChangedAtMs = held.ChangedAtMs
      HeldUntilMs = held.HeldUntilMs
      QueuedRetrievals = held.QueuedRetrievals
      ExpectedSources = held.ExpectedSources
      PendingSources = held.PendingSources
      StaleSources = held.StaleSources
      Reasons = held.Reasons
      Revisions = [||]
      Note = "" }
  | Readiness.Unavailable reason ->
    { Revision = 0L
      State = state
      BranchStatus = branchStatus
      BranchName = branch.BranchName
      OnMain = branch.OnMain
      Generation = 0L
      SnapshotId = ""
      PublishedAtMs = 0L
      ChangedAtMs = None
      HeldUntilMs = None
      QueuedRetrievals = 0
      ExpectedSources = [||]
      PendingSources = [||]
      StaleSources = [||]
      Reasons = [||]
      Revisions = [||]
      Note = boundReason reason }
  | Readiness.Unconfigured ->
    { Revision = 0L
      State = state
      BranchStatus = branchStatus
      BranchName = branch.BranchName
      OnMain = branch.OnMain
      Generation = 0L
      SnapshotId = ""
      PublishedAtMs = 0L
      ChangedAtMs = None
      HeldUntilMs = None
      QueuedRetrievals = 0
      ExpectedSources = [||]
      PendingSources = [||]
      StaleSources = [||]
      Reasons = [||]
      Revisions = [||]
      Note = "Retrieval observation is not configured." }

let toWireStatus (obs: Observation) : RetrievalStatus =
  toWireStatusForBranch obs.Branch obs

let statusForWorkingDir (workingDir: string option) (obs: Observation) : RetrievalStatus =
  match workingDir with
  | Some wd when not (String.IsNullOrWhiteSpace wd) ->
    toWireStatusForBranch (readCheckoutBranch wd) obs
  | _ -> toWireStatus obs

let checkoutSyncedForBranch (branch: BranchInfo) (status: RetrievalStatus) : bool option =
  if status.BranchStatus = "quarantined_branch"
     || status.BranchStatus = "indexing_pending"
     || status.State = "crossing_held"
     || status.State = "indexing" then
    Some false
  elif status.BranchStatus = "on_main_indexed" && status.State = "fresh" then
    match branch.Repository, branch.HeadRevision |> Option.bind tryNormalizeSha40 with
    | Some repo, Some localSha ->
      let matchingShas =
        status.Revisions
        |> Array.choose (fun r ->
          if matchesSource repo r.Source then r.Revision |> Option.bind tryNormalizeSha40
          else None)
      if Array.isEmpty matchingShas then None
      elif matchingShas |> Array.exists (fun indexedSha -> String.Equals(localSha, indexedSha, StringComparison.OrdinalIgnoreCase)) then
        Some true
      else
        Some false
    | _ -> None
  else
    None

let checkoutSynced (obs: Observation) : bool option =
  toWireStatus obs |> checkoutSyncedForBranch obs.Branch

let statusToJson (status: RetrievalStatus) : JsonValue =
  let strings (arr: string array) = arr |> Array.map JsonValue.String |> Array.toList |> JsonValue.Array
  let optSigned = function Some v -> JsonValue.ofInt64 v | None -> JsonValue.Null
  let revisionsObj =
    status.Revisions
    |> Array.map (fun r -> r.Source, match r.Revision with Some rev -> JsonValue.String rev | None -> JsonValue.Null)
    |> Array.toList
    |> JsonValue.Object
  JsonValue.Object [
    "state", JsonValue.String status.State
    "branchStatus", JsonValue.String status.BranchStatus
    "branch", JsonValue.String status.BranchName
    "onMain", JsonValue.Bool status.OnMain
    "generation", JsonValue.ofInt64 status.Generation
    "snapshotId", JsonValue.String status.SnapshotId
    "publishedAtMs", JsonValue.ofInt64 status.PublishedAtMs
    "changedAtMs", optSigned status.ChangedAtMs
    "heldUntilMs", optSigned status.HeldUntilMs
    "queuedRetrievals", JsonValue.ofInt64 (int64 status.QueuedRetrievals)
    "expectedSources", strings status.ExpectedSources
    "pendingSources", strings status.PendingSources
    "staleSources", strings status.StaleSources
    "reasons", strings status.Reasons
    "revisions", revisionsObj
    "note", JsonValue.String status.Note
  ]

let daemonStatusJson (status: RetrievalStatus) : JsonValue =
  let strings (arr: string array) = arr |> Array.map JsonValue.String |> Array.toList |> JsonValue.Array
  JsonValue.Object [
    "state", JsonValue.String status.State
    "branchStatus", JsonValue.String status.BranchStatus
    "branch", JsonValue.String status.BranchName
    "onMain", JsonValue.Bool status.OnMain
    "generation", JsonValue.ofInt64 status.Generation
    "snapshotId", JsonValue.String status.SnapshotId
    "publishedAtMs", JsonValue.ofInt64 status.PublishedAtMs
    "pendingSources", strings status.PendingSources
    "staleSources", strings status.StaleSources
    "reasons", strings status.Reasons
    "queuedRetrievals", JsonValue.ofInt64 (int64 status.QueuedRetrievals)
    "note", JsonValue.String status.Note
  ]

type private ValidatedFilter = {
  MinGeneration: int64 option
  ExpectedRepo: string option
  ExpectedSha: string option
  Timeout: TimeSpan
}

let private validateFilter
    (minGeneration: int64 option)
    (expectedRepo: string option)
    (expectedSha: string option)
    (timeout: TimeSpan) : Result<ValidatedFilter, string> =
  match minGeneration with
  | Some g when g < 0L -> Error "minGeneration must be non-negative."
  | _ ->
    if timeout < TimeSpan.Zero || timeout > TimeSpan.FromHours 24. then
      Error "timeout must be between 0 and 24 hours."
    else
      let validatedSha =
        match expectedSha with
        | None -> Ok None
        | Some sha ->
          let trimmed = if isNull sha then "" else sha.Trim()
          if trimmed.Length < 7 || trimmed.Length > 64 || not (trimmed |> Seq.forall isHexChar) then
            Error "expectedSha must be 7 to 64 hexadecimal characters."
          else Ok(Some trimmed)
      let validatedRepo =
        match expectedRepo with
        | None -> Ok None
        | Some repo ->
          let trimmed = if isNull repo then "" else repo.Trim()
          if trimmed.Length = 0 then Error "expectedRepo must not be empty."
          else Ok(Some trimmed)
      match validatedRepo, validatedSha with
      | Error e, _ | _, Error e -> Error e
      | Ok (Some _), Ok None -> Error "expectedRepo requires expectedSha."
      | Ok repoOpt, Ok shaOpt ->
        Ok {
          MinGeneration = minGeneration
          ExpectedRepo = repoOpt
          ExpectedSha = shaOpt
          Timeout = timeout
        }

let private matchesRevision (expectedSha: string) (rev: string option) =
  match rev with
  | Some actual -> actual.StartsWith(expectedSha, StringComparison.OrdinalIgnoreCase)
  | None -> false

let private matchesFilter (filter: ValidatedFilter) (fresh: FreshState) =
  let genOk =
    match filter.MinGeneration with
    | None -> true
    | Some minGen -> fresh.Generation >= minGen
  let revOk =
    match filter.ExpectedSha with
    | None -> true
    | Some sha ->
      match filter.ExpectedRepo with
      | Some repo ->
        fresh.Revisions |> Array.exists (fun r -> matchesSource repo r.Source && matchesRevision sha r.Revision)
      | None ->
        fresh.Revisions |> Array.exists (fun r -> matchesRevision sha r.Revision)
  genOk && revOk

let private utf8CharByteBound (c: char) =
  if c <= '\u007f' then 1
  elif c <= '\u07ff' then 2
  elif Char.IsSurrogate c then 2
  else 3

let private readSseStream (stream: Stream) (onReadiness: Readiness -> unit) (ct: CancellationToken) : Async<unit> = async {
  use reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks = false, bufferSize = 4096, leaveOpen = true)
  let buffer = Array.zeroCreate<char> 4096
  let lineBuf = StringBuilder()
  let mutable lineBytes = 0
  let dataLines = ResizeArray<string>()
  let mutable dataBytes = 0
  let mutable eventType: string option = None
  let mutable pendingCr = false

  let processLine (line: string) =
    if line.Length = 0 then
      if dataLines.Count > 0 then
        let evtName = defaultArg eventType "message"
        let payload = String.concat "\n" dataLines
        eventType <- None
        dataLines.Clear()
        dataBytes <- 0
        if evtName = "readiness" || evtName = "message" then
          match parseReadinessJson payload with
          | Ok readiness -> onReadiness readiness
          | Error _ -> raise (InvalidDataException "invalid_readiness_frame")
      else
        eventType <- None
    elif line.StartsWith(":", StringComparison.Ordinal) then
      ()
    else
      let colon = line.IndexOf(':')
      let fieldName, fieldValue =
        if colon < 0 then line, ""
        else
          let raw = line.Substring(colon + 1)
          let value = if raw.StartsWith(" ", StringComparison.Ordinal) then raw.Substring 1 else raw
          line.Substring(0, colon), value
      match fieldName with
      | "event" -> eventType <- Some fieldValue
      | "data" ->
        dataBytes <- dataBytes + Encoding.UTF8.GetByteCount(fieldValue) + 1
        if dataBytes > MaxEventBytes then
          raise (InvalidDataException "invalid_readiness_frame")
        dataLines.Add fieldValue
      | _ -> ()

  let mutable eof = false
  while not eof && not ct.IsCancellationRequested do
    let! count = reader.ReadAsync(Memory<char>(buffer), ct).AsTask() |> Async.AwaitTask
    if count = 0 then
      eof <- true
    else
      for i in 0 .. count - 1 do
        let c = buffer[i]
        if c = '\r' then
          let line = lineBuf.ToString()
          lineBuf.Clear() |> ignore
          lineBytes <- 0
          pendingCr <- true
          processLine line
        elif c = '\n' then
          if pendingCr then
            pendingCr <- false
          else
            let line = lineBuf.ToString()
            lineBuf.Clear() |> ignore
            lineBytes <- 0
            processLine line
        else
          pendingCr <- false
          let charBytes = utf8CharByteBound c
          if lineBytes + charBytes > MaxEventBytes then
            raise (InvalidDataException "invalid_readiness_frame")
          lineBytes <- lineBytes + charBytes
          lineBuf.Append c |> ignore
}

type private Waiter = {
  Match: FreshState -> bool
  Tcs: TaskCompletionSource<AwaitOutcome>
}

let private observationSignal () =
  let subGate = obj ()
  let mutable nextSubId = 0L
  let mutable subscribers : (int64 * IObserver<Observation>) list = []
  let observable =
    { new IObservable<Observation> with
        member _.Subscribe(observer) =
          let id =
            lock subGate (fun () ->
              nextSubId <- nextSubId + 1L
              let id = nextSubId
              subscribers <- subscribers @ [ id, observer ]
              id)
          { new IDisposable with
              member _.Dispose() =
                lock subGate (fun () ->
                  subscribers <- subscribers |> List.filter (fun (sid, _) -> sid <> id)) } }
  let trigger obs =
    let snapshot = lock subGate (fun () -> subscribers)
    for _, observer in snapshot do
      try observer.OnNext obs
      with ex -> Log.warn "[retrieval-observation] subscriber error: %s" ex.Message
  observable, trigger

// Persistent zero-poll SSE observer and event-driven waiter barrier.
// Never uses polling timers; wakes waiters and fires Changed only on real transitions.
type Controller private (
    config: Config option,
    configError: string option,
    workingDir: string,
    stopping: CancellationToken,
    injectedHandler: HttpMessageHandler option,
    readToken: string -> string,
    delayAsync: int -> CancellationToken -> Task,
    beforeCompletingFreshWaiters: unit -> unit) =
  let gate = obj ()
  let changedObservable, triggerChanged = observationSignal ()

  let lifetime = CancellationTokenSource.CreateLinkedTokenSource stopping
  let controllerCancellation = lifetime.Token
  let ownedHandler =
    match injectedHandler with
    | Some _ -> None
    | None -> Some(new SocketsHttpHandler(AllowAutoRedirect = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds 10.))
  let client =
    match injectedHandler, ownedHandler with
    | Some h, _ -> new HttpClient(h, disposeHandler = false, Timeout = Timeout.InfiniteTimeSpan)
    | None, Some h -> new HttpClient(h, disposeHandler = true, Timeout = Timeout.InfiniteTimeSpan)
    | None, None -> invalidOp "RetrievalObservation: missing HTTP handler."

  let initialReadiness =
    match config, configError with
    | _, Some err -> Readiness.Unavailable(boundReason err)
    | None, None -> Readiness.Unconfigured
    | Some _, None -> Readiness.Unavailable "connecting"

  let mutable activeWorkingDir = normalizeWorkingDir workingDir
  let mutable activeCheckout : Checkout option = None
  let mutable headWatchers : FileSystemWatcher list = []
  let mutable watcherEpoch = 0L
  let mutable currentObs : Observation = {
    Serial = 1L
    ObservedAtMs = nowMs ()
    Branch = readCheckoutBranch activeWorkingDir
    Readiness = initialReadiness
    LastFreshSources = [||]
  }
  let waiters = Dictionary<int64, Waiter>()
  let mutable nextWaiterId = 0L
  let mutable waiterWatchers : (int * TaskCompletionSource<unit>) list = []
  let mutable closed = false
  let closedSignal = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let mutable watchJob: Task<unit> option = None

  let drainSatisfiedWaiterWatchersLocked () =
    if List.isEmpty waiterWatchers then []
    else
      let count = waiters.Count
      let ready, pending = waiterWatchers |> List.partition (fun (target, _) -> count = target)
      waiterWatchers <- pending
      ready |> List.map snd

  let notifyWaiterWatchers (ready: TaskCompletionSource<unit> list) =
    for tcs in ready do
      tcs.TrySetResult() |> ignore

  let disposeWatchers (watchers: FileSystemWatcher list) =
    for w in watchers do
      try
        w.EnableRaisingEvents <- false
        w.Dispose()
      with _ -> ()

  // Checkout selection and HEAD reads share the observation commit gate.
  let readActiveBranchLocked () =
    match activeCheckout with
    | Some checkout -> readBranchInCheckout checkout
    | None -> readCheckoutBranch activeWorkingDir

  let refreshBranchLocked () =
    let branch = readActiveBranchLocked ()
    if currentObs.Branch = branch then None
    else
      let next =
        { currentObs with Serial = currentObs.Serial + 1L; ObservedAtMs = nowMs (); Branch = branch }
      currentObs <- next
      Some next

  let mutable watchingRefsDir = false

  let isRelevantGitDirEntry (name: string) =
    if String.IsNullOrWhiteSpace name then true
    else
      String.Equals(name, "HEAD", StringComparison.Ordinal)
      || String.Equals(name, "HEAD.lock", StringComparison.Ordinal)
      || String.Equals(name, "packed-refs", StringComparison.Ordinal)
      || String.Equals(name, "packed-refs.lock", StringComparison.Ordinal)
      || String.Equals(name, "commondir", StringComparison.Ordinal)
      || String.Equals(name, "refs", StringComparison.Ordinal)

  let rec createWatcher (dir: string) (filter: string) (includeSubdirs: bool) (nameFilter: (string -> bool) option) (epoch: int64) : FileSystemWatcher option =
    try
      let watcher = new FileSystemWatcher(dir, filter)
      watcher.IncludeSubdirectories <- includeSubdirs
      watcher.NotifyFilter <-
        NotifyFilters.FileName
        ||| NotifyFilters.DirectoryName
        ||| NotifyFilters.LastWrite
        ||| NotifyFilters.CreationTime
        ||| NotifyFilters.Size
      let scheduleRefresh (name: string) =
        let relevant =
          match nameFilter with
          | None -> true
          | Some pred -> pred name
        if relevant then
          let rebuildRefs =
            String.IsNullOrWhiteSpace name
            || String.Equals(name, "refs", StringComparison.Ordinal)
          ThreadPool.QueueUserWorkItem(fun _ -> refreshBranchForEpoch epoch rebuildRefs) |> ignore
      watcher.Changed.Add(fun e -> scheduleRefresh e.Name)
      watcher.Created.Add(fun e -> scheduleRefresh e.Name)
      watcher.Deleted.Add(fun e -> scheduleRefresh e.Name)
      watcher.Renamed.Add(fun e -> scheduleRefresh e.Name; scheduleRefresh e.OldName)
      watcher.Error.Add(fun args ->
        let ex = args.GetException()
        if not (isNull ex) then
          Log.warn "[retrieval-observation] HEAD watcher error for %s: %s" dir ex.Message
        scheduleRefresh "")
      try
        watcher.EnableRaisingEvents <- true
        Some watcher
      with _ ->
        disposeWatchers [ watcher ]
        reraise ()
    with ex ->
      Log.warn "[retrieval-observation] could not watch HEAD in %s: %s" dir ex.Message
      None

  and createCheckoutWatchers (gitDir: string) (epoch: int64) : FileSystemWatcher list * bool =
    let commonDir = resolveCommonGitDir gitDir
    let refsDir = Path.Combine(commonDir, "refs")
    let refsWatcher =
      if Directory.Exists refsDir then
        createWatcher refsDir "*" true None epoch
      else None
    let watchers =
      [ if Directory.Exists gitDir then
          yield! createWatcher gitDir "*" false (Some isRelevantGitDirEntry) epoch |> Option.toList
        if commonDir <> gitDir && Directory.Exists commonDir then
          yield! createWatcher commonDir "*" false (Some isRelevantGitDirEntry) epoch |> Option.toList
        yield! refsWatcher |> Option.toList ]
    watchers, refsWatcher.IsSome

  and refreshBranchForEpoch (expectedEpoch: int64) (rebuildRefs: bool) =
    let oldWatchers, published =
      lock gate (fun () ->
        if closed || watcherEpoch <> expectedEpoch then [], None
        else
          let oldWatchers =
            match activeCheckout with
            | Some checkout when rebuildRefs || not watchingRefsDir ->
              let refsDir = Path.Combine(resolveCommonGitDir checkout.GitDirectory, "refs")
              if rebuildRefs || Directory.Exists refsDir then
                let prev = headWatchers
                watcherEpoch <- watcherEpoch + 1L
                let nextWatchers, hasRefs = createCheckoutWatchers checkout.GitDirectory watcherEpoch
                headWatchers <- nextWatchers
                watchingRefsDir <- hasRefs
                prev
              else []
            | _ -> []
          oldWatchers, refreshBranchLocked ())
    disposeWatchers oldWatchers
    published |> Option.iter triggerChanged

  let applyWorkingDir (resolveDirectory: unit -> string option) =
    let oldWatchers, published =
      lock gate (fun () ->
        if closed then [], None
        else
          let targetDir = normalizeWorkingDir (resolveDirectory () |> Option.defaultValue "")
          let checkout = resolveCheckout targetDir
          let oldWatchers =
            if checkout = activeCheckout && (checkout.IsNone || not (List.isEmpty headWatchers)) then []
            else
              let previous = headWatchers
              watcherEpoch <- watcherEpoch + 1L
              let nextWatchers, hasRefs =
                match checkout with
                | Some value -> createCheckoutWatchers value.GitDirectory watcherEpoch
                | None -> [], false
              headWatchers <- nextWatchers
              watchingRefsDir <- hasRefs
              previous
          activeWorkingDir <- targetDir
          activeCheckout <- checkout
          oldWatchers, refreshBranchLocked ())
    disposeWatchers oldWatchers
    published |> Option.iter triggerChanged

  let publish (readiness: Readiness) =
    let decision =
      lock gate (fun () ->
        if closed then None
        else
          let branch = readActiveBranchLocked ()
          if currentObs.Readiness = readiness && currentObs.Branch = branch then None
          else
            let lastFreshSources =
              match readiness with
              | Readiness.Fresh fresh -> fresh.Revisions |> Array.map _.Source
              | _ -> currentObs.LastFreshSources
            let next = {
              Serial = currentObs.Serial + 1L
              ObservedAtMs = nowMs ()
              Branch = branch
              Readiness = readiness
              LastFreshSources = lastFreshSources
            }
            currentObs <- next
            let satisfied, readyWatchers =
              match readiness with
              | Readiness.Fresh fresh ->
                let matched = ResizeArray<int64 * TaskCompletionSource<AwaitOutcome>>()
                for KeyValue(id, waiter) in waiters do
                  if waiter.Match fresh then matched.Add(id, waiter.Tcs)
                for id, _ in matched do waiters.Remove id |> ignore
                let ready = if matched.Count > 0 then drainSatisfiedWaiterWatchersLocked () else []
                (matched |> Seq.map snd |> Seq.toList), ready
              | _ -> [], []
            Some(next, satisfied, readyWatchers))
    match decision with
    | None -> ()
    | Some(next, satisfied, readyWatchers) ->
      if not (List.isEmpty satisfied) then beforeCompletingFreshWaiters ()
      triggerChanged next
      for tcs in satisfied do
        tcs.TrySetResult(AwaitOutcome.Fresh next) |> ignore
      notifyWaiterWatchers readyWatchers

  let openStream (cfg: Config) (ct: CancellationToken) = async {
    let token =
      try readToken cfg.TokenFile
      with _ -> raise (InvalidDataException "retrieval_credential_unreadable")
    let trimmed = if isNull token then "" else token.Trim()
    if trimmed.Length = 0 || trimmed.Length > MaxTokenLength || (trimmed |> Seq.exists Char.IsWhiteSpace) then
      return raise (InvalidDataException "retrieval_credential_invalid")
    else
      use request = new HttpRequestMessage(HttpMethod.Get, cfg.Endpoint)
      request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", trimmed)
      request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("text/event-stream"))
      use headerDeadline = CancellationTokenSource.CreateLinkedTokenSource ct
      headerDeadline.CancelAfter(TimeSpan.FromSeconds 15.)
      let! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerDeadline.Token) |> Async.AwaitTask
      let contentType =
        if isNull response.Content || isNull response.Content.Headers || isNull response.Content.Headers.ContentType then ""
        else response.Content.Headers.ContentType.MediaType
      if not response.IsSuccessStatusCode then
        let code = int response.StatusCode
        response.Dispose()
        return raise (InvalidDataException(sprintf "retrieval_endpoint_http_%d" code))
      elif not (String.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase)) then
        response.Dispose()
        return raise (InvalidDataException "retrieval_endpoint_invalid_content_type")
      else
        return response
  }

  let watch (cfg: Config) = async {
    let mutable retrySeconds = 1
    while not lifetime.IsCancellationRequested do
      try
        use! response = openStream cfg controllerCancellation
        use! stream = response.Content.ReadAsStreamAsync(controllerCancellation) |> Async.AwaitTask
        do! readSseStream stream (fun readiness ->
          retrySeconds <- 1
          publish readiness) controllerCancellation
        if not lifetime.IsCancellationRequested then
          publish (Readiness.Unavailable "stream_disconnected")
      with
      | ex ->
        let baseEx = ex.GetBaseException()
        match baseEx with
        | :? OperationCanceledException when lifetime.IsCancellationRequested -> ()
        | :? InvalidDataException as ide ->
          if not lifetime.IsCancellationRequested then
            let reason =
              if String.IsNullOrWhiteSpace ide.Message then "invalid_readiness_frame"
              else boundReason ide.Message
            Log.warn "[retrieval-observation] stream fault: %s" reason
            publish (Readiness.Unavailable reason)
        | _ ->
          if not lifetime.IsCancellationRequested then
            let reason =
              match baseEx with
              | :? IOException -> "stream_disconnected"
              | :? HttpRequestException -> "endpoint_unreachable"
              | :? OperationCanceledException -> "connect_timeout"
              | _ -> "stream_unavailable"
            Log.warn "[retrieval-observation] connection error (%s): %s" reason (baseEx.GetType().Name)
            publish (Readiness.Unavailable reason)
      if not lifetime.IsCancellationRequested then
        try do! delayAsync retrySeconds controllerCancellation |> Async.AwaitTask
        with ex when lifetime.IsCancellationRequested && (ex.GetBaseException() :? OperationCanceledException) -> ()
        retrySeconds <- min 30 (retrySeconds * 2)
  }

  do
    applyWorkingDir (fun () -> Some activeWorkingDir)
    match config, configError with
    | Some cfg, None ->
      watchJob <- Some(Async.StartAsTask(watch cfg, cancellationToken = CancellationToken.None))
    | _ -> ()

  new(config: Config option, workingDir: string, stopping: CancellationToken) =
    new Controller(config, None, workingDir, stopping, None, readTokenFile, (fun seconds ct -> Task.Delay(seconds * 1000, ct)), ignore)

  static member FromConfigResult(configResult: Result<Config option, string>, workingDir: string, stopping: CancellationToken) =
    match configResult with
    | Ok cfgOpt -> new Controller(cfgOpt, None, workingDir, stopping, None, readTokenFile, (fun seconds ct -> Task.Delay(seconds * 1000, ct)), ignore)
    | Error err ->
      Log.warn "[retrieval-observation] invalid retrieval configuration: %s" err
      new Controller(None, Some err, workingDir, stopping, None, readTokenFile, (fun seconds ct -> Task.Delay(seconds * 1000, ct)), ignore)

  static member internal CreateForTest(
      config: Config option,
      workingDir: string,
      stopping: CancellationToken,
      handler: HttpMessageHandler,
      ?readToken: string -> string,
      ?delayAsync: int -> CancellationToken -> Task,
      ?configError: string,
      ?beforeCompletingFreshWaiters: unit -> unit) =
    new Controller(
      config,
      configError,
      workingDir,
      stopping,
      Some handler,
      defaultArg readToken readTokenFile,
      defaultArg delayAsync (fun seconds ct -> Task.Delay(seconds * 1000, ct)),
      defaultArg beforeCompletingFreshWaiters ignore)

  member _.Changed : IObservable<Observation> = changedObservable

  member _.Current() : Observation = lock gate (fun () -> currentObs)

  member _.ActiveWaiterCount : int = lock gate (fun () -> waiters.Count)

  member _.SetWorkingDirectory(dir: string option) : unit =
    applyWorkingDir (fun () -> dir)

  member _.RefreshCheckout(resolveDirectory: unit -> string option) : unit =
    applyWorkingDir resolveDirectory

  member internal _.WaitForActiveWaitersAsync(expectedCount: int, timeoutMs: int) : Task =
    let pending =
      lock gate (fun () ->
        if closed then Choice1Of2(Task.FromCanceled(CancellationToken(true)))
        elif waiters.Count = expectedCount then Choice1Of2 Task.CompletedTask
        else
          let tcs = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
          waiterWatchers <- waiterWatchers @ [ expectedCount, tcs ]
          Choice2Of2 tcs)
    match pending with
    | Choice1Of2 readyTask -> readyTask
    | Choice2Of2 tcs ->
      task {
        try
          do! tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(float timeoutMs))
        finally
          lock gate (fun () ->
            waiterWatchers <- waiterWatchers |> List.filter (fun (_, w) -> not (obj.ReferenceEquals(w, tcs))))
      }

  member _.AwaitFreshAsync(
      minGeneration: int64 option,
      expectedRepo: string option,
      expectedSha: string option,
      timeout: TimeSpan,
      ct: CancellationToken) : Task<AwaitOutcome> =
    match validateFilter minGeneration expectedRepo expectedSha timeout with
    | Error reason -> Task.FromResult(AwaitOutcome.InvalidFilter reason)
    | Ok filter ->
      let decision =
        lock gate (fun () ->
          if closed then
            Choice1Of2(AwaitOutcome.Closed currentObs)
          else
            match currentObs.Readiness with
            | Readiness.Unconfigured -> Choice1Of2(AwaitOutcome.Unconfigured currentObs)
            | Readiness.Unavailable _ when watchJob.IsNone -> Choice1Of2(AwaitOutcome.Unavailable currentObs)
            | Readiness.Fresh fresh when matchesFilter filter fresh ->
              Choice1Of2(AwaitOutcome.Fresh currentObs)
            | _ when ct.IsCancellationRequested ->
              Choice1Of2(AwaitOutcome.Cancelled currentObs)
            | _ when filter.Timeout = TimeSpan.Zero ->
              Choice1Of2(AwaitOutcome.Timeout currentObs)
            | _ ->
              nextWaiterId <- nextWaiterId + 1L
              let id = nextWaiterId
              let tcs = TaskCompletionSource<AwaitOutcome>(TaskCreationOptions.RunContinuationsAsynchronously)
              waiters[id] <- { Match = matchesFilter filter; Tcs = tcs }
              let ready = drainSatisfiedWaiterWatchersLocked ()
              Choice2Of2(id, tcs, ready))
      match decision with
      | Choice1Of2 outcome -> Task.FromResult outcome
      | Choice2Of2(id, tcs, readyWatchers) ->
        notifyWaiterWatchers readyWatchers
        task {
          let tryClaimWaiter () : Observation option =
            let snap, ready =
              lock gate (fun () ->
                if waiters.Remove id then Some currentObs, drainSatisfiedWaiterWatchersLocked ()
                else None, [])
            notifyWaiterWatchers ready
            snap
          use timeoutCts = new CancellationTokenSource(filter.Timeout)
          use _timeoutReg =
            timeoutCts.Token.Register(fun () ->
              tryClaimWaiter ()
              |> Option.iter (fun snap -> tcs.TrySetResult(AwaitOutcome.Timeout snap) |> ignore))
          use _callerReg =
            ct.Register(fun () ->
              tryClaimWaiter ()
              |> Option.iter (fun snap -> tcs.TrySetResult(AwaitOutcome.Cancelled snap) |> ignore))
          try
            return! tcs.Task
          finally
            let ready =
              lock gate (fun () ->
                if waiters.Remove id then drainSatisfiedWaiterWatchersLocked ()
                else [])
            notifyWaiterWatchers ready
        }

  member _.Close() : Async<unit> = async {
    let pending =
      lock gate (fun () ->
        if closed then None
        else
          closed <- true
          watcherEpoch <- watcherEpoch + 1L
          let oldWatchers = headWatchers
          headWatchers <- []
          activeCheckout <- None
          let snap = currentObs
          let parked = waiters.Values |> Seq.map _.Tcs |> Seq.toList
          waiters.Clear()
          let remainingWatchers = waiterWatchers |> List.map snd
          waiterWatchers <- []
          lifetime.Cancel()
          Some(snap, parked, remainingWatchers, oldWatchers, watchJob))
    match pending with
    | None -> do! closedSignal.Task |> Async.AwaitTask
    | Some(snap, parked, remainingWatchers, oldWatchers, job) ->
      disposeWatchers oldWatchers
      for tcs in parked do
        tcs.TrySetResult(AwaitOutcome.Closed snap) |> ignore
      for w in remainingWatchers do
        w.TrySetCanceled() |> ignore
      try
        match job with
        | Some taskJob ->
          try do! taskJob |> Async.AwaitTask
          with ex when (ex.GetBaseException() :? OperationCanceledException) -> ()
        | None -> ()
      finally
        client.Dispose()
        lifetime.Dispose()
        closedSignal.TrySetResult () |> ignore
  }

  interface IDisposable with
    member this.Dispose() = this.Close() |> Async.RunSynchronously

// Daemon-wide ambient retrieval owner (mirrors HealthTick's single-sequencer pattern).
let private ambientGate = obj ()
let private ambientChanged, triggerAmbient = observationSignal ()
let mutable private ambientController: Controller option = None
let mutable private ambientSub: IDisposable option = None
let mutable private ambientSerial = 1L
let mutable private ambientLastSourceSerial = 0L
let mutable private ambientLatest: Observation = {
  Serial = 1L
  ObservedAtMs = nowMs ()
  Branch = unknownBranch
  Readiness = Readiness.Unconfigured
  LastFreshSources = [||]
}

let Changed : IObservable<Observation> = ambientChanged

let current () : Observation = lock ambientGate (fun () -> ambientLatest)

let currentForStatus (workingDir: string option) : RetrievalStatus =
  current () |> statusForWorkingDir workingDir

let install (controller: Controller) : IDisposable =
  let isCurrentController () =
    match ambientController with
    | Some current -> obj.ReferenceEquals(current, controller)
    | None -> false
  let publishFrom (obs: Observation) =
    let published =
      lock ambientGate (fun () ->
        if not (isCurrentController ()) || obs.Serial <= ambientLastSourceSerial then
          None
        else
          ambientLastSourceSerial <- obs.Serial
          ambientSerial <- ambientSerial + 1L
          let stamped = { obs with Serial = ambientSerial }
          ambientLatest <- stamped
          Some stamped)
    published |> Option.iter triggerAmbient

  let initial =
    lock ambientGate (fun () ->
      ambientSub |> Option.iter _.Dispose()
      ambientController <- Some controller
      ambientLastSourceSerial <- 0L
      let sub = controller.Changed.Subscribe publishFrom
      ambientSub <- Some sub
      let snap = controller.Current()
      if snap.Serial > ambientLastSourceSerial then
        ambientLastSourceSerial <- snap.Serial
        ambientSerial <- ambientSerial + 1L
        let stamped = { snap with Serial = ambientSerial }
        ambientLatest <- stamped
        Some stamped
      else None)
  initial |> Option.iter triggerAmbient
  { new IDisposable with
      member _.Dispose() =
        lock ambientGate (fun () ->
          if isCurrentController () then
            ambientSub |> Option.iter _.Dispose()
            ambientSub <- None
            ambientController <- None) }

let reset () : unit =
  lock ambientGate (fun () ->
    ambientSub |> Option.iter _.Dispose()
    ambientSub <- None
    ambientController <- None
    ambientLastSourceSerial <- 0L
    ambientSerial <- ambientSerial + 1L
    ambientLatest <- {
      Serial = ambientSerial
      ObservedAtMs = nowMs ()
      Branch = unknownBranch
      Readiness = Readiness.Unconfigured
      LastFreshSources = [||]
    })

let awaitFreshAsync
    (minGeneration: int64 option)
    (expectedRepo: string option)
    (expectedSha: string option)
    (timeout: TimeSpan)
    (ct: CancellationToken) : Task<AwaitOutcome> =
  let ctrlOpt, snap = lock ambientGate (fun () -> ambientController, ambientLatest)
  match ctrlOpt with
  | Some ctrl -> ctrl.AwaitFreshAsync(minGeneration, expectedRepo, expectedSha, timeout, ct)
  | None ->
    match validateFilter minGeneration expectedRepo expectedSha timeout with
    | Error reason -> Task.FromResult(AwaitOutcome.InvalidFilter reason)
    | Ok _ -> Task.FromResult(AwaitOutcome.Unconfigured snap)

let outcomeToHttpJson (workingDir: string option) (outcome: AwaitOutcome) : int * JsonValue =
  let attachStatus ok outcomeName errorOpt obs =
    let status = statusForWorkingDir workingDir obs
    let fields = [
      yield "ok", JsonValue.Bool ok
      yield "outcome", JsonValue.String outcomeName
      match errorOpt with
      | Some err -> yield "error", JsonValue.String err
      | None -> ()
      if status.BranchStatus = "quarantined_branch" then
        yield "quarantined", JsonValue.Bool true
        yield "warning", JsonValue.String(sprintf "Checkout is on non-main branch '%s'; lab retrieval indexes main only." status.BranchName)
      yield "status", statusToJson status
    ]
    JsonValue.Object fields
  match outcome with
  | AwaitOutcome.Fresh obs -> 200, attachStatus true "fresh" None obs
  | AwaitOutcome.Timeout obs -> 408, attachStatus false "timeout" (Some "Timed out waiting for retrieval index freshness.") obs
  | AwaitOutcome.Unconfigured obs -> 503, attachStatus false "unconfigured" (Some "Retrieval observation is not configured.") obs
  | AwaitOutcome.Unavailable obs -> 503, attachStatus false "unavailable" (Some "Retrieval observation is unavailable.") obs
  | AwaitOutcome.Cancelled obs -> 503, attachStatus false "cancelled" (Some "Wait for retrieval freshness was cancelled.") obs
  | AwaitOutcome.Closed obs -> 503, attachStatus false "closed" (Some "Retrieval observer is shutting down.") obs
  | AwaitOutcome.InvalidFilter reason ->
    400, JsonValue.Object [
      "ok", JsonValue.Bool false
      "outcome", JsonValue.String "invalid_filter"
      "error", JsonValue.String reason
    ]
