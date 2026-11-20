#r "../../src/Bozzetto/bin/Release/net10.0/XParsec.dll"
#r "../../src/Bozzetto/bin/Release/net10.0/Fidelity.Data.dll"
#load "../../src/Bozzetto.Core/OwnerMonitor.fs"
#load "../../src/Bozzetto.Core/ProcessWatchdog.fs"
#load "../../src/Bozzetto/LinuxProcessEdge.fs"
#load "AuditPacketTypes.fs"
#load "AuditPacketAuthority.fs"
#load "AuditPacketLex.fs"
#load "AuditPacketDiff.fs"
#load "AuditPacketCanary.fs"
#load "AuditPacketReceipts.fs"
#load "AuditPacketEval.fs"
#load "AuditPacketRender.fs"

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Threading
open Fidelity.Data.JSON
open Bozzetto.Server
open Bozzetto.Audit

let private usageText =
  String.concat "\n" [
    "usage: dotnet fsi tools/audit/make-packet.fsx <repo> <base-sha> <issue-url> [options]"
    ""
    "options:"
    "  --out <path>                  Write Markdown packet to file (default: stdout)"
    "  --candidate <ref-or-sha>      Candidate git ref or SHA (default: HEAD)"
    "  --issue-file <path>           Read Forgejo issue JSON (with comments) from file"
    "  --wiki-file <url>=<path>      Supply offline Forgejo wiki ruling file for <url> (repeatable)"
    "  --sarif <path>                Read analyzer SARIF JSON report"
    "  --axd000 <path>               Read standalone AXD000 SHA-256 receipt"
    "  --canary-base <path>          Read base .fidproj canary receipt"
    "  --canary-candidate <path>     Read candidate .fidproj canary receipt"
    "  --retrieval-snapshot <path>   Read pinned lab_retrieval snapshot JSON"
    "  --test-receipt <path>         Read test compilation / TRUST receipt (required)"
    "  --spec-ballast-file <path>    Read offline clef-search JSON Spec Ballast receipt"
    "  --spec-ballast-query <query>  Explicit clef-search query (1..512 chars)"
    "  --bozzetto-port <port>        Bozzetto HTTP port (default: BOZZETTO_MCP_PORT or 47749)"
    "  --skip-retrieval-await        Do not call POST /api/retrieval/await"
    "  --skip-clef-search            Omit clef-search Spec Ballast (yields partial exit 3 packet)"
    "  --outside-index               Mark repository outside lab_retrieval index"
  ]

let private envOpt (name: string) : string option =
  Environment.GetEnvironmentVariable name |> Option.ofObj |> Option.bind nonEmptyOpt

let private setsidPathOpt =
  if OperatingSystem.IsLinux() then LinuxProcessEdge.tryResolveSetsid () else None

let private runProcessAsync
    (exe: string)
    (workingDir: string)
    (clearGitExtEnv: bool)
    (args: string list)
    (timeoutMs: int)
    : Async<Result<string * string * int, SourceLoadError>> =
  async {
    try
      let psi =
        match setsidPathOpt with
        | Some setsid ->
          let p = ProcessStartInfo(setsid)
          p.ArgumentList.Add "--wait"
          p.ArgumentList.Add exe
          p
        | None -> ProcessStartInfo(exe)
      psi.WorkingDirectory <- workingDir
      psi.UseShellExecute <- false
      psi.RedirectStandardOutput <- true
      psi.RedirectStandardError <- true
      psi.StandardOutputEncoding <- Encoding.UTF8
      psi.StandardErrorEncoding <- Encoding.UTF8
      if clearGitExtEnv then
        psi.Environment.Remove("GIT_EXTERNAL_DIFF") |> ignore
        psi.Environment.Remove("GIT_DIFF_OPTS") |> ignore
      args |> List.iter psi.ArgumentList.Add
      match Option.ofObj (Process.Start psi) with
      | None -> return Error(SourceLoadError.ProcessStartFailed(exe, "Process.Start returned null"))
      | Some proc ->
        use p = proc
        let targetPgid = if setsidPathOpt.IsSome then Some p.Id else None
        let rootStartTicks =
          if OperatingSystem.IsLinux() then LinuxProcessEdge.readLinuxProc p.Id |> Option.map _.StartTimeTicks
          else None
        use cts = new CancellationTokenSource(timeoutMs)
        let stdoutTask = p.StandardOutput.ReadToEndAsync(cts.Token)
        let stderrTask = p.StandardError.ReadToEndAsync(cts.Token)
        let killAndCleanup () =
          async {
            if OperatingSystem.IsLinux() then
              try
                let! _ = LinuxProcessEdge.terminateProcessGroupWithFenceAsync p.Id targetPgid rootStartTicks 250
                ()
              with _ -> ()
            try p.Kill(entireProcessTree = true) with _ -> ()
            try
              use cleanupCts = new CancellationTokenSource(2000)
              do! p.WaitForExitAsync(cleanupCts.Token) |> Async.AwaitTask
            with _ -> ()
            try ignore stdoutTask.Exception with _ -> ()
            try ignore stderrTask.Exception with _ -> ()
          }
        try
          do! p.WaitForExitAsync(cts.Token) |> Async.AwaitTask
          let! outText = stdoutTask |> Async.AwaitTask
          let! errText = stderrTask |> Async.AwaitTask
          return Ok(outText, errText, p.ExitCode)
        with
        | :? OperationCanceledException ->
          do! killAndCleanup ()
          return Error(SourceLoadError.ProcessTimedOut(sprintf "%s %s" exe (String.concat " " args), timeoutMs))
        | ex ->
          do! killAndCleanup ()
          return Error(SourceLoadError.ProcessStartFailed(sprintf "%s %s" exe (String.concat " " args), ex.Message))
    with ex ->
      return Error(SourceLoadError.ProcessStartFailed(exe, ex.Message))
  }

let private gitSafePrefix = [ "-c"; "diff.external="; "-c"; "core.quotepath=false"; "-c"; "diff.renames=true" ]

let private gitDiffSafeArgs = [ "diff"; "--no-ext-diff"; "--no-textconv"; "--no-color"; "--src-prefix=a/"; "--dst-prefix=b/" ]

let private runGitAsync (repoPath: string) (args: string list) (timeoutMs: int) : Async<Result<string * string * int, SourceLoadError>> =
  runProcessAsync "git" repoPath true (gitSafePrefix @ args) timeoutMs

let private runGitChecked (repoPath: string) (args: string list) : Async<Result<string, SourceLoadError>> =
  async {
    match! runGitAsync repoPath args 60_000 with
    | Error err -> return Error err
    | Ok(stdout, stderr, code) ->
      if code = 0 then return Ok stdout
      else
        let detail = if String.IsNullOrWhiteSpace stderr then stdout.Trim() else stderr.Trim()
        return Error(SourceLoadError.GitFailed(String.concat " " args, code, detail))
  }

let private countTextLines (text: string) : int =
  if String.IsNullOrEmpty text then 0
  else
    let newlines = text |> Seq.filter (fun c -> c = '\n') |> Seq.length
    if text.EndsWith("\n", StringComparison.Ordinal) then newlines else newlines + 1

let private gitCatBlob (repoPath: string) (rev: string) (filePath: string) : Async<Result<string, SourceLoadError>> =
  async {
    let spec = sprintf "%s:%s" rev filePath
    match! runGitAsync repoPath [ "cat-file"; "blob"; spec ] 30_000 with
    | Error err -> return Error err
    | Ok(content, _, 0) -> return Ok content
    | Ok(stdout, stderr, code) ->
      let detail = if String.IsNullOrWhiteSpace stderr then stdout.Trim() else stderr.Trim()
      return Error(SourceLoadError.GitFailed(sprintf "cat-file blob %s" spec, code, detail))
  }

let private collectCodeDelta (opts: CliOptions) : Async<Result<CodeDeltaReport, SourceLoadError>> =
  async {
    if not (Directory.Exists opts.RepoPath) then
      return Error(SourceLoadError.MissingDirectory opts.RepoPath)
    else
      match! runGitChecked opts.RepoPath [ "rev-parse"; "--verify"; sprintf "%s^{commit}" opts.BaseSha ] with
      | Error err -> return Error err
      | Ok baseResolved ->
        let baseSha = baseResolved.Trim()
        match! runGitChecked opts.RepoPath [ "rev-parse"; "--verify"; sprintf "%s^{commit}" opts.CandidateRef ] with
        | Error err -> return Error err
        | Ok candResolved ->
          let candidateSha = candResolved.Trim()
          let! branchOpt =
            async {
              match! runGitAsync opts.RepoPath [ "rev-parse"; "--abbrev-ref"; opts.CandidateRef ] 15_000 with
              | Ok(branchOut, _, 0) ->
                let b = branchOut.Trim()
                if String.IsNullOrWhiteSpace b || b = "HEAD" then return None
                else return Some b
              | _ -> return None
            }
          let rangeSpec = sprintf "%s..%s" baseSha candidateSha
          match! runGitChecked opts.RepoPath (gitDiffSafeArgs @ [ "--unified=3"; rangeSpec ]) with
          | Error err -> return Error err
          | Ok rawDiff ->
            match! runGitChecked opts.RepoPath (gitDiffSafeArgs @ [ "--numstat"; rangeSpec ]) with
            | Error err -> return Error err
            | Ok rawNumstatText ->
              match! runGitChecked opts.RepoPath (gitDiffSafeArgs @ [ "--numstat"; "-z"; rangeSpec ]) with
              | Error err -> return Error err
              | Ok numstatZ ->
                match! runGitChecked opts.RepoPath (gitDiffSafeArgs @ [ "--name-status"; "-z"; rangeSpec ]) with
                | Error err -> return Error err
                | Ok nameStatusZ ->
                  match parseNameStatusZ nameStatusZ with
                  | Error parseErr -> return Error(SourceLoadError.ParseFailure parseErr)
                  | Ok statusEntries ->
                    let numstatEntries = parseNumstatZ numstatZ
                    let binaryPaths =
                      numstatEntries
                      |> List.filter _.IsBinary
                      |> List.map _.Path
                      |> Set.ofList
                    let changeTasks =
                      statusEntries
                      |> List.map (fun (kind, oldPathOpt, newPath) ->
                        async {
                          let isBin = Set.contains newPath binaryPaths
                          let oldInProd =
                            match kind, oldPathOpt with
                            | GitChangeKind.Copied _, _ -> false
                            | _, Some p -> isProductionSrcPath p
                            | _, None -> false
                          let newInProd =
                            match kind with
                            | GitChangeKind.Deleted -> false
                            | _ -> isProductionSrcPath newPath
                          let needBefore =
                            match kind, oldPathOpt with
                            | GitChangeKind.Added, _ -> false
                            | GitChangeKind.Copied _, _ -> false
                            | _, Some _ -> (not isBin) || oldInProd
                            | _, None -> false
                          let needAfter =
                            match kind with
                            | GitChangeKind.Deleted -> false
                            | _ -> (not isBin) || newInProd
                          let! beforeRes =
                            if needBefore then
                              match oldPathOpt with
                              | Some op ->
                                async {
                                  let! r = gitCatBlob opts.RepoPath baseSha op
                                  return Some r
                                }
                              | None -> async { return None }
                            else async { return None }
                          let! afterRes =
                            if needAfter then
                              async {
                                let! r = gitCatBlob opts.RepoPath candidateSha newPath
                                return Some r
                              }
                            else async { return None }
                          let beforeBlob, beforeErr =
                            match beforeRes with
                            | Some(Ok txt) -> Some txt, None
                            | Some(Error e) -> None, Some(SourceLoadError.describe e)
                            | None -> None, None
                          let afterBlob, afterErr =
                            match afterRes with
                            | Some(Ok txt) -> Some txt, None
                            | Some(Error e) -> None, Some(SourceLoadError.describe e)
                            | None -> None, None
                          let readError =
                            match beforeErr, afterErr with
                            | Some e1, Some e2 -> Some(e1 + "; " + e2)
                            | Some e, None -> Some e
                            | None, Some e -> Some e
                            | None, None -> None
                          let linePair =
                            if isBin then newPath, (None, None)
                            else newPath, (beforeBlob |> Option.map countTextLines, afterBlob |> Option.map countTextLines)
                          let change : GitFileChange = {
                            Kind = kind
                            OldPath = oldPathOpt
                            NewPath = newPath
                            BeforeBlob = beforeBlob
                            AfterBlob = afterBlob
                            ReadError = readError
                          }
                          return change, linePair
                        })
                    let! collected = Async.Sequential changeTasks
                    let changes = collected |> Array.map fst |> Array.toList
                    let lineCounts = collected |> Array.map snd |> Array.toList |> Map.ofList
                    return
                      Ok(
                        scanDiffWithChanges
                          baseSha opts.CandidateRef candidateSha branchOpt
                          rawDiff rawNumstatText numstatEntries changes lineCounts)
  }

let private readOptionalFile (label: string) (pathOpt: string option) : Result<string option, SourceLoadError> =
  match pathOpt with
  | None -> Ok None
  | Some path ->
    if not (File.Exists path) then Error(SourceLoadError.MissingFile(label, path))
    else
      try Ok(Some(File.ReadAllText(path, Encoding.UTF8)))
      with ex -> Error(SourceLoadError.FileReadFailed(label, path, ex.Message))

let private tryDecodeBase64Utf8 (encoded: string) : string option =
  if String.IsNullOrEmpty encoded then Some ""
  else
    try
      let bytes = Convert.FromBase64String encoded
      Some(Encoding.UTF8.GetString bytes)
    with _ -> None

let private decodeAndParseWikiPayload
    (provenance: AuthorityProvenance)
    (wikiRef: ForgejoWikiRef)
    (raw: string)
    : Result<WikiRuling, PacketParseError> =
  extractWikiBase64Field wikiRef.CanonicalUrl raw
  |> Result.bind (fun b64Opt ->
    match b64Opt with
    | None -> parseWikiPagePayload provenance wikiRef raw None
    | Some b64 ->
      match tryDecodeBase64Utf8 b64 with
      | Some decoded -> parseWikiPagePayload provenance wikiRef raw (Some decoded)
      | None -> Error(PacketParseError.InvalidWikiPayload(wikiRef.CanonicalUrl, "invalid Base64 in content_base64")))

let private clientForUrl (http: HttpClient) (loopbackHttp: HttpClient) (url: string) : HttpClient =
  match tryParseHttpUrl url with
  | Some parsed when isLoopbackHost parsed.Host -> loopbackHttp
  | _ -> http

let private sendForgeGetAsync
    (http: HttpClient)
    (loopbackHttp: HttpClient)
    (tokenOpt: string option)
    (trustedOriginEnv: string option)
    (allowTestOriginEnv: string option)
    (targetOrigin: string)
    (url: string)
    : Async<Result<int * string, SourceLoadError>> =
  async {
    try
      let client = clientForUrl http loopbackHttp url
      use req = new HttpRequestMessage(HttpMethod.Get, url)
      req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json"))
      req.Headers.UserAgent.ParseAdd("BozzettoAuditPacket/1.0")
      if shouldSendForgeToken targetOrigin trustedOriginEnv allowTestOriginEnv then
        tokenOpt |> Option.iter (fun tok -> req.Headers.TryAddWithoutValidation("Authorization", "token " + tok) |> ignore)
      use! resp = client.SendAsync req |> Async.AwaitTask
      let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
      return Ok(int resp.StatusCode, body)
    with ex ->
      return Error(SourceLoadError.HttpRequestFailed(url, ex.Message))
  }

let private loadReferencedWikiRulings
    (http: HttpClient)
    (loopbackHttp: HttpClient)
    (tokenOpt: string option)
    (trustedOriginEnv: string option)
    (allowTestOriginEnv: string option)
    (opts: CliOptions)
    (issue: IssueAuthority)
    : Async<IssueAuthority> =
  async {
    let wikiFileMap =
      opts.WikiFiles
      |> List.choose (fun (urlStr, filePath) ->
        parseWikiUrl urlStr |> Option.map (fun w -> wikiRefKey w, (w, filePath)))
      |> Map.ofList
    let referencedRefs = extractSameHostWikiRefs opts.IssueRef.BaseUri issue.ReferencedUrls
    let explicitExtraRefs =
      opts.WikiFiles
      |> List.choose (fun (urlStr, _) -> parseWikiUrl urlStr)
      |> List.filter (fun w -> not (referencedRefs |> List.exists (fun r -> wikiRefKey r = wikiRefKey w)))
    let allWikiRefs = (referencedRefs @ explicitExtraRefs) |> List.distinctBy wikiRefKey
    let fetchTasks =
      allWikiRefs
      |> List.map (fun w ->
        async {
          match Map.tryFind (wikiRefKey w) wikiFileMap with
          | Some(_, filePath) ->
            match readOptionalFile (sprintf "Wiki ruling (%s)" w.CanonicalUrl) (Some filePath) with
            | Error err -> return Choice2Of2 { Url = w.CanonicalUrl; Error = err }
            | Ok None -> return Choice2Of2 { Url = w.CanonicalUrl; Error = SourceLoadError.MissingFile("Wiki ruling", filePath) }
            | Ok(Some raw) ->
              match decodeAndParseWikiPayload (AuthorityProvenance.LocalFile filePath) w raw with
              | Ok ruling -> return Choice1Of2 ruling
              | Error parseErr -> return Choice2Of2 { Url = w.CanonicalUrl; Error = SourceLoadError.ParseFailure parseErr }
          | None ->
            let wikiApi = sprintf "%s/api/v1/repos/%s/%s/wiki/page/%s" w.BaseUri w.Owner w.Repo w.PageName
            match! sendForgeGetAsync http loopbackHttp tokenOpt trustedOriginEnv allowTestOriginEnv w.BaseUri wikiApi with
            | Error err -> return Choice2Of2 { Url = w.CanonicalUrl; Error = err }
            | Ok(status, body) when status < 200 || status >= 300 ->
              return Choice2Of2 { Url = w.CanonicalUrl; Error = SourceLoadError.HttpStatusError(wikiApi, status, body) }
            | Ok(_, body) ->
              match decodeAndParseWikiPayload (AuthorityProvenance.LiveFetch wikiApi) w body with
              | Ok ruling -> return Choice1Of2 ruling
              | Error parseErr -> return Choice2Of2 { Url = w.CanonicalUrl; Error = SourceLoadError.ParseFailure parseErr }
        })
    let! outcomes = Async.Sequential fetchTasks
    let rulings =
      outcomes
      |> Array.choose (function Choice1Of2 r -> Some r | Choice2Of2 _ -> None)
      |> Array.toList
    let failures =
      outcomes
      |> Array.choose (function Choice2Of2 f -> Some f | Choice1Of2 _ -> None)
      |> Array.toList
    return { issue with WikiRulings = rulings; WikiRulingFailures = failures }
  }

let private fetchIssueAuthority (http: HttpClient) (loopbackHttp: HttpClient) (opts: CliOptions) : Async<Result<IssueAuthority, SourceLoadError>> =
  async {
    let tokenOpt =
      [ "FORGEJO_TOKEN"; "GITEA_TOKEN" ]
      |> List.tryPick envOpt
    let trustedOriginEnv = envOpt "BOZZETTO_FORGE_ORIGIN"
    let allowTestOriginEnv = envOpt "BOZZETTO_ALLOW_TEST_TOKEN_ORIGIN"
    let! baseIssueRes =
      async {
        match opts.IssueFile with
        | Some path ->
          match readOptionalFile "Issue" (Some path) with
          | Error err -> return Error err
          | Ok None -> return Error(SourceLoadError.MissingFile("Issue", path))
          | Ok(Some raw) ->
            return
              parseIssuePayloadWithProvenance (AuthorityProvenance.LocalFile path) opts.IssueRef.Number opts.IssueUrl raw None
              |> Result.mapError SourceLoadError.ParseFailure
        | None ->
          let issueApi =
            sprintf "%s/api/v1/repos/%s/%s/issues/%d"
              opts.IssueRef.BaseUri opts.IssueRef.Owner opts.IssueRef.Repo opts.IssueRef.Number
          let commentsApi =
            sprintf "%s/api/v1/repos/%s/%s/issues/%d/comments"
              opts.IssueRef.BaseUri opts.IssueRef.Owner opts.IssueRef.Repo opts.IssueRef.Number
          match! sendForgeGetAsync http loopbackHttp tokenOpt trustedOriginEnv allowTestOriginEnv opts.IssueRef.BaseUri issueApi with
          | Error err -> return Error err
          | Ok(status, issueBody) when status < 200 || status >= 300 ->
            return Error(SourceLoadError.HttpStatusError(issueApi, status, issueBody))
          | Ok(_, issueBody) ->
            let rec fetchCommentPages (page: int) (acc: JsonValue list) =
              async {
                let pagedApi = sprintf "%s?page=%d&limit=50" commentsApi page
                match! sendForgeGetAsync http loopbackHttp tokenOpt trustedOriginEnv allowTestOriginEnv opts.IssueRef.BaseUri pagedApi with
                | Error err -> return Error err
                | Ok(cStatus, pageBody) when cStatus < 200 || cStatus >= 300 ->
                  return Error(SourceLoadError.HttpStatusError(pagedApi, cStatus, pageBody))
                | Ok(_, pageBody) ->
                  match parseJsonValue "Forgejo comments JSON" pageBody with
                  | Error pErr -> return Error(SourceLoadError.ParseFailure pErr)
                  | Ok(JsonValue.Array items) ->
                    let combined = acc @ items
                    if items.Length < 50 then
                      return Ok(Json.serialize (JsonValue.Array combined))
                    elif page >= 20 then
                      return Error(SourceLoadError.HttpRequestFailed(pagedApi, "Forgejo comments pagination reached 20-page limit without completing (possible truncation)"))
                    else
                      return! fetchCommentPages (page + 1) combined
                  | Ok _ ->
                    return Error(SourceLoadError.ParseFailure(PacketParseError.InvalidJsonField("Forgejo comments JSON", "comments", "expected JSON array")))
              }
            match! fetchCommentPages 1 [] with
            | Error err -> return Error err
            | Ok commentsBody ->
              return
                parseIssuePayloadWithProvenance (AuthorityProvenance.LiveFetch issueApi) opts.IssueRef.Number opts.IssueUrl issueBody (Some commentsBody)
                |> Result.mapError SourceLoadError.ParseFailure
      }
    match baseIssueRes with
    | Error err -> return Error err
    | Ok issue ->
      let! enriched = loadReferencedWikiRulings http loopbackHttp tokenOpt trustedOriginEnv allowTestOriginEnv opts issue
      return Ok enriched
  }

let private tryReadGitOriginUrl (repoPath: string) : Async<string option> =
  async {
    if not (Directory.Exists repoPath) then return None
    else
      match! runGitAsync repoPath [ "remote"; "get-url"; "origin" ] 10_000 with
      | Ok(stdout, _, 0) -> return nonEmptyOpt stdout
      | _ -> return None
  }

let private fetchRetrievalSnapshot
    (loopbackHttp: HttpClient)
    (opts: CliOptions)
    (bozzettoPort: int)
    (resolvedBaseSha: string)
    : Async<Result<RetrievalSnapshot option, SourceLoadError>> =
  async {
    match opts.RetrievalSnapshotPath with
    | Some path ->
      match readOptionalFile "Retrieval snapshot" (Some path) with
      | Error err -> return Error err
      | Ok None -> return Ok None
      | Ok(Some raw) ->
        let! originOpt = tryReadGitOriginUrl opts.RepoPath
        let fullRepoPath = Path.GetFullPath opts.RepoPath
        let expectedRepoOpt = deriveExpectedRepo fullRepoPath originOpt
        let provenance = RetrievalProvenance.SnapshotFile(path, expectedRepoOpt, Some resolvedBaseSha)
        return
          parseRetrievalSnapshotWithProvenance provenance raw
          |> Result.map Some
          |> Result.mapError SourceLoadError.ParseFailure
    | None when opts.OutsideIndex ->
      return Ok None
    | None when opts.SkipRetrievalAwait ->
      return Ok None
    | None ->
      let url = sprintf "http://127.0.0.1:%d/api/retrieval/await" bozzettoPort
      let! originOpt = tryReadGitOriginUrl opts.RepoPath
      let fullRepoPath = Path.GetFullPath opts.RepoPath
      let expectedRepoOpt = deriveExpectedRepo fullRepoPath originOpt
      let provenance = RetrievalProvenance.LiveAwait(url, expectedRepoOpt, Some resolvedBaseSha)
      try
        let fields = [
          yield "timeoutSeconds", JsonValue.ofInt64 30L
          yield "workingDirectory", JsonValue.String fullRepoPath
          yield "expectedSha", JsonValue.String resolvedBaseSha
          match expectedRepoOpt with
          | Some repo -> yield "expectedRepo", JsonValue.String repo
          | None -> ()
        ]
        let payload = Json.serialize (JsonValue.Object fields)
        use content = new StringContent(payload, Encoding.UTF8, "application/json")
        use! resp = loopbackHttp.PostAsync(url, content) |> Async.AwaitTask
        let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
        let statusCode = int resp.StatusCode
        if statusCode = 503 then
          match parseRetrievalSnapshotWithProvenance provenance body with
          | Ok snap when snap.State = "unconfigured" || snap.State = "unavailable" ->
            return Error(SourceLoadError.RetrievalServiceStatus(snap.State, sprintf "HTTP 503 from %s" url))
          | Ok snap -> return Ok(Some snap)
          | Error err -> return Error(SourceLoadError.HttpStatusError(url, 503, PacketParseError.describe err))
        elif statusCode = 200 || statusCode = 408 then
          return
            parseRetrievalSnapshotWithProvenance provenance body
            |> Result.map Some
            |> Result.mapError SourceLoadError.ParseFailure
        else
          return Error(SourceLoadError.HttpStatusError(url, statusCode, body))
      with ex ->
        return Error(SourceLoadError.HttpRequestFailed(url, ex.Message))
  }

let private fetchSpecBallast
    (http: HttpClient)
    (loopbackHttp: HttpClient)
    (opts: CliOptions)
    (issueRes: Result<IssueAuthority, SourceLoadError>)
    (codeRes: Result<CodeDeltaReport, SourceLoadError>)
    : Async<SpecBallastStatus> =
  async {
    let modifiedPaths =
      match codeRes with
      | Ok delta -> delta.Files |> List.map _.Path
      | Error _ -> []
    let query =
      opts.SpecBallastQuery
      |> Option.defaultWith (fun () -> deriveSpecBallastQuery (Result.toOption issueRes) modifiedPaths)
    match opts.SpecBallastFilePath with
    | Some path ->
      match readOptionalFile "Spec Ballast" (Some path) with
      | Error err -> return SpecBallastStatus.Unavailable(query, err)
      | Ok None -> return SpecBallastStatus.Unavailable(query, SourceLoadError.MissingFile("Spec Ballast", path))
      | Ok(Some raw) ->
        match parseClefSearchResponse query raw with
        | Ok status -> return status
        | Error err -> return SpecBallastStatus.Unavailable(query, SourceLoadError.ParseFailure err)
    | None when opts.SkipClefSearch ->
      return SpecBallastStatus.Skipped "--skip-clef-search flag set"
    | None ->
      let endpoint =
        envOpt "CLEF_SEARCH_URL"
        |> Option.defaultValue "https://clef-search.engineering-0c5.workers.dev/search/hybrid"
      try
        let client = clientForUrl http loopbackHttp endpoint
        let bodyJson =
          Json.serialize (
            JsonValue.Object [
              "query", JsonValue.String query
              "limit", JsonValue.ofInt64 3L
            ])
        use req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json"))
        req.Headers.UserAgent.ParseAdd("FidelitySwarm/1.0")
        req.Content <- new StringContent(bodyJson, Encoding.UTF8, "application/json")
        use! resp = client.SendAsync req |> Async.AwaitTask
        let! rawBody = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
        if resp.IsSuccessStatusCode then
          match parseClefSearchResponse query rawBody with
          | Ok status -> return status
          | Error err -> return SpecBallastStatus.Unavailable(query, SourceLoadError.ParseFailure err)
        else
          return SpecBallastStatus.Unavailable(query, SourceLoadError.HttpStatusError(endpoint, int resp.StatusCode, rawBody))
      with ex ->
        return SpecBallastStatus.Unavailable(query, SourceLoadError.HttpRequestFailed(endpoint, ex.Message))
  }

let private writePacketOutput (outPathOpt: string option) (rendered: string) : Result<unit, SourceLoadError> =
  try
    match outPathOpt with
    | Some outPath ->
      let parent = Path.GetDirectoryName outPath |> Option.ofObj |> Option.defaultValue ""
      if not (String.IsNullOrWhiteSpace parent) then
        Directory.CreateDirectory parent |> ignore
      File.WriteAllText(outPath, rendered, Encoding.UTF8)
      Ok ()
    | None ->
      Console.Out.Write rendered
      Ok ()
  with ex ->
    Error(SourceLoadError.FileReadFailed("Output packet", outPathOpt |> Option.defaultValue "<stdout>", ex.Message))

let private runPacket (opts: CliOptions) (bozzettoPort: int) : Async<int> =
  async {
    use handler = new HttpClientHandler(AllowAutoRedirect = false)
    use http = new HttpClient(handler, false, Timeout = TimeSpan.FromSeconds 35.0)
    use loopbackHandler = new HttpClientHandler(AllowAutoRedirect = false, UseProxy = false)
    use loopbackHttp = new HttpClient(loopbackHandler, false, Timeout = TimeSpan.FromSeconds 35.0)
    let! issueRes = fetchIssueAuthority http loopbackHttp opts
    let! codeRes = collectCodeDelta opts

    let analyzerRes =
      match readOptionalFile "SARIF" opts.SarifPath, readOptionalFile "AXD000" opts.Axd000Path with
      | Error e, _ -> Error e
      | _, Error e -> Error e
      | Ok sarifOpt, Ok axdOpt -> parseSarifAndAxd000 sarifOpt axdOpt |> Result.mapError SourceLoadError.ParseFailure

    let testReceiptRes =
      match readOptionalFile "Test receipt" opts.TestReceiptPath with
      | Error e -> Error e
      | Ok None -> Ok None
      | Ok(Some raw) -> parseTestReceipt raw |> Result.map Some |> Result.mapError SourceLoadError.ParseFailure

    let canaryBaseRes =
      match readOptionalFile "Base canary receipt" opts.CanaryBasePath with
      | Error e -> Error e
      | Ok None -> Ok None
      | Ok(Some raw) -> parseCanaryReceipt raw |> Result.map Some |> Result.mapError SourceLoadError.ParseFailure

    let canaryCandidateRes =
      match readOptionalFile "Candidate canary receipt" opts.CanaryCandidatePath with
      | Error e -> Error e
      | Ok None -> Ok None
      | Ok(Some raw) -> parseCanaryReceipt raw |> Result.map Some |> Result.mapError SourceLoadError.ParseFailure

    let resolvedBaseSha =
      match codeRes with
      | Ok delta -> delta.BaseSha
      | Error _ -> opts.BaseSha

    let! retrievalRes = fetchRetrievalSnapshot loopbackHttp opts bozzettoPort resolvedBaseSha
    let! specBallast = fetchSpecBallast http loopbackHttp opts issueRes codeRes

    let packetInput : BlindAuditPacketInput = {
      RepoPath = opts.RepoPath
      BaseSha = opts.BaseSha
      CandidateRef = opts.CandidateRef
      IssueUrl = opts.IssueUrl
      OutsideIndex = opts.OutsideIndex
      Issue = issueRes
      CodeDelta = codeRes
      Analyzer = analyzerRes
      TestReceipt = testReceiptRes
      CanaryBase = canaryBaseRes
      CanaryCandidate = canaryCandidateRes
      Retrieval = retrievalRes
      SpecBallast = specBallast
    }

    let evaluated = evaluateVetoes packetInput
    let rendered = renderPacket evaluated

    match writePacketOutput opts.OutPath rendered with
    | Error writeErr ->
      eprintfn "BLOCKED: failed writing audit packet: %s" (SourceLoadError.describe writeErr)
      return 3
    | Ok () ->
      if not evaluated.Completeness.IsComplete then
        eprintfn "BLOCKED: incomplete packet (%s)" (String.concat "; " evaluated.Completeness.MissingParts)
        return 3
      elif not (List.isEmpty evaluated.Vetoes) then
        eprintfn "REJECT: %d mechanical veto(es) triggered" evaluated.Vetoes.Length
        return 2
      else
        return 0
  }

let argv = fsi.CommandLineArgs |> Array.toList |> List.tail
match parseArgs argv with
| Error CliError.HelpRequested ->
  printfn "%s" usageText
  exit 0
| Error err ->
  eprintfn "make-packet: %s" (CliError.describe err)
  eprintfn "%s" usageText
  exit 64
| Ok opts ->
  match resolveBozzettoPort opts.BozzettoPort (envOpt "BOZZETTO_MCP_PORT") with
  | Error err ->
    eprintfn "make-packet: %s" (CliError.describe err)
    exit 64
  | Ok port ->
    try
      let code = runPacket opts port |> Async.RunSynchronously
      exit code
    with ex ->
      eprintfn "BLOCKED: unexpected host error: %s" ex.Message
      exit 3
