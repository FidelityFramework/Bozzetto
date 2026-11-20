namespace Bozzetto.Audit

open System
open System.Globalization
open System.Text.RegularExpressions
open Fidelity.Data.JSON

// Pure Zone A strict CLI parser, Forgejo URL/origin policy, and Part 1
// Verbatim Authority (issue + comments + wiki ruling) parsers.
[<AutoOpen>]
module AuditPacketAuthority =

  [<Literal>]
  let DefaultBozzettoPort = 47749

  [<Literal>]
  let DefaultTrustedForgeOrigin = "https://forge.spkez.dev"

  let parseIssueUrl (raw: string) : Result<ForgejoIssueRef, CliError> =
    match tryParseHttpUrl raw with
    | None -> Error(CliError.InvalidIssueUrl raw)
    | Some parsed ->
      match List.rev parsed.PathSegments with
      | numStr :: "issues" :: repo :: owner :: _ ->
        match Int32.TryParse(numStr, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, num when num > 0 && not (String.IsNullOrWhiteSpace owner) && not (String.IsNullOrWhiteSpace repo) ->
          Ok { BaseUri = parsed.Origin; Owner = owner; Repo = repo; Number = num }
        | _ -> Error(CliError.InvalidIssueUrl raw)
      | _ -> Error(CliError.InvalidIssueUrl raw)

  let parseWikiUrl (raw: string) : ForgejoWikiRef option =
    match tryParseHttpUrl raw with
    | None -> None
    | Some parsed ->
      match parsed.PathSegments with
      | owner :: repo :: "wiki" :: firstPage :: restPages
        when not (String.IsNullOrWhiteSpace owner)
             && not (String.IsNullOrWhiteSpace repo)
             && not (String.IsNullOrWhiteSpace firstPage)
             && not (String.Equals(firstPage, "_pages", StringComparison.OrdinalIgnoreCase))
             && not (String.Equals(firstPage, "_new", StringComparison.OrdinalIgnoreCase)) ->
        let pageName = String.concat "/" (firstPage :: restPages)
        let canonical = sprintf "%s/%s/%s/wiki/%s" parsed.Origin owner repo pageName
        Some {
          BaseUri = parsed.Origin
          Owner = owner
          Repo = repo
          PageName = pageName
          CanonicalUrl = canonical
        }
      | _ -> None

  let wikiRefKey (w: ForgejoWikiRef) : string =
    sprintf "%s/%s/%s/%s"
      (w.BaseUri.ToLowerInvariant())
      (w.Owner.ToLowerInvariant())
      (w.Repo.ToLowerInvariant())
      w.PageName

  let extractSameHostWikiRefs (issueBaseUri: string) (urls: string list) : ForgejoWikiRef list =
    let normBase =
      match tryParseHttpUrl issueBaseUri with
      | Some p -> p.Origin
      | None -> issueBaseUri.Trim().TrimEnd('/').ToLowerInvariant()
    urls
    |> List.choose parseWikiUrl
    |> List.filter (fun w -> String.Equals(w.BaseUri, normBase, StringComparison.OrdinalIgnoreCase))
    |> List.distinctBy wikiRefKey

  let shouldSendForgeToken
      (targetOrigin: string)
      (trustedOriginEnv: string option)
      (allowTestOriginEnv: string option)
      : bool =
    match tryParseHttpUrl targetOrigin with
    | None -> false
    | Some target ->
      let matchesTrustedHttps =
        let trustedRaw =
          trustedOriginEnv
          |> Option.bind nonEmptyOpt
          |> Option.defaultValue DefaultTrustedForgeOrigin
        match tryParseHttpUrl trustedRaw with
        | Some trusted when trusted.Scheme = "https" ->
          String.Equals(target.Origin, trusted.Origin, StringComparison.OrdinalIgnoreCase)
        | _ -> false
      let matchesLoopbackTest =
        match allowTestOriginEnv |> Option.bind nonEmptyOpt |> Option.bind tryParseHttpUrl with
        | Some allowed when isLoopbackHost allowed.Host ->
          String.Equals(target.Origin, allowed.Origin, StringComparison.OrdinalIgnoreCase)
        | _ -> false
      matchesTrustedHttps || matchesLoopbackTest

  let resolveBozzettoPort (cliPort: int option) (envPortRaw: string option) : Result<int, CliError> =
    match cliPort with
    | Some p when p >= 1 && p <= 65535 -> Ok p
    | Some p -> Error(CliError.InvalidPort(string p))
    | None ->
      match envPortRaw |> Option.bind nonEmptyOpt with
      | None -> Ok DefaultBozzettoPort
      | Some raw ->
        match Int32.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, port when port >= 1 && port <= 65535 -> Ok port
        | _ -> Error(CliError.InvalidEnvPort("BOZZETTO_MCP_PORT", raw))

  let deriveExpectedRepo (repoPath: string) (originUrlOpt: string option) : string option =
    let stripGitSuffix (name: string) =
      let trimmed = name.Trim().TrimEnd('/', '\\')
      if trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4 then
        trimmed.Substring(0, trimmed.Length - 4)
      else trimmed
    let fromOrigin =
      originUrlOpt
      |> Option.bind nonEmptyOpt
      |> Option.bind (fun raw ->
        let cleaned = stripGitSuffix raw
        let slashIdx = cleaned.LastIndexOf('/')
        let colonIdx = cleaned.LastIndexOf(':')
        let sep = max slashIdx colonIdx
        let candidate = if sep >= 0 then cleaned.Substring(sep + 1) else cleaned
        nonEmptyOpt candidate)
    match fromOrigin with
    | Some repo -> Some repo
    | None ->
      let norm = normalizeSlash(repoPath.Trim()).TrimEnd('/')
      match norm.LastIndexOf('/') with
      | idx when idx >= 0 -> nonEmptyOpt (norm.Substring(idx + 1))
      | _ -> nonEmptyOpt norm

  let private validateCandidateRef (raw: string) : Result<string, CliError> =
    match nonEmptyOpt raw with
    | None -> Error(CliError.BlankValue "--candidate")
    | Some trimmed ->
      let validChars =
        trimmed |> Seq.forall (fun c ->
          Char.IsLetterOrDigit c || c = '.' || c = '_' || c = '/' || c = '-' || c = '@' || c = '^' || c = '{' || c = '}' || c = '~')
      if not validChars
         || trimmed.StartsWith("-", StringComparison.Ordinal)
         || trimmed.StartsWith("/", StringComparison.Ordinal)
         || trimmed.EndsWith("/", StringComparison.Ordinal)
         || trimmed.Contains("..", StringComparison.Ordinal)
         || trimmed.Contains("//", StringComparison.Ordinal)
         || containsNullChar trimmed then
        Error(CliError.InvalidCandidateRef raw)
      else Ok trimmed

  let private requireValue (flag: string) (rest: string list) : Result<string * string list, CliError> =
    match rest with
    | [] -> Error(CliError.MissingFlagValue flag)
    | value :: tail ->
      match nonEmptyOpt value with
      | None -> Error(CliError.BlankValue flag)
      | Some trimmed ->
        if trimmed.StartsWith("--", StringComparison.Ordinal) || containsNullChar trimmed then
          Error(CliError.MissingFlagValue flag)
        else Ok(trimmed, tail)

  let private parseWikiFileMapping (raw: string) : Result<string * string, CliError> =
    let eqIdx = raw.IndexOf('=')
    if eqIdx <= 0 || eqIdx >= raw.Length - 1 then
      Error(CliError.InvalidWikiFileMapping raw)
    else
      let urlPart = raw.Substring(0, eqIdx).Trim()
      let pathPart = raw.Substring(eqIdx + 1).Trim()
      if String.IsNullOrWhiteSpace urlPart || String.IsNullOrWhiteSpace pathPart then
        Error(CliError.InvalidWikiFileMapping raw)
      else
        match parseWikiUrl urlPart with
        | Some wikiRef -> Ok(wikiRef.CanonicalUrl, pathPart)
        | None -> Error(CliError.InvalidWikiFileMapping raw)

  type private PartialOpts = {
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
    Positionals: string list
  }

  let parseArgs (argv: string list) : Result<CliOptions, CliError> =
    let initial = {
      OutPath = None; CandidateRef = "HEAD"; IssueFile = None; WikiFiles = []
      SarifPath = None; Axd000Path = None; CanaryBasePath = None; CanaryCandidatePath = None
      RetrievalSnapshotPath = None; TestReceiptPath = None; SpecBallastFilePath = None; SpecBallastQuery = None
      BozzettoPort = None; SkipRetrievalAwait = false; SkipClefSearch = false
      OutsideIndex = false; Positionals = []
    }
    let validateCandidateOrErr (v: string, tail: string list) =
      validateCandidateRef v |> Result.map (fun valid -> valid, tail)
    let rec loop (acc: PartialOpts) (remaining: string list) : Result<PartialOpts, CliError> =
      match remaining with
      | [] -> Ok acc
      | ("--help" | "-h") :: _ -> Error CliError.HelpRequested
      | "--skip-retrieval-await" :: rest -> loop { acc with SkipRetrievalAwait = true } rest
      | "--skip-clef-search" :: rest -> loop { acc with SkipClefSearch = true } rest
      | "--outside-index" :: rest -> loop { acc with OutsideIndex = true } rest
      | "--out" :: rest -> requireValue "--out" rest |> Result.bind (fun (v, t) -> loop { acc with OutPath = Some v } t)
      | "--candidate" :: rest ->
        requireValue "--candidate" rest
        |> Result.bind validateCandidateOrErr
        |> Result.bind (fun (v, t) -> loop { acc with CandidateRef = v } t)
      | "--issue-file" :: rest ->
        requireValue "--issue-file" rest |> Result.bind (fun (v, t) -> loop { acc with IssueFile = Some v } t)
      | "--wiki-file" :: rest ->
        requireValue "--wiki-file" rest
        |> Result.bind (fun (v, t) ->
          parseWikiFileMapping v
          |> Result.bind (fun mapping -> loop { acc with WikiFiles = acc.WikiFiles @ [ mapping ] } t))
      | "--sarif" :: rest -> requireValue "--sarif" rest |> Result.bind (fun (v, t) -> loop { acc with SarifPath = Some v } t)
      | "--axd000" :: rest -> requireValue "--axd000" rest |> Result.bind (fun (v, t) -> loop { acc with Axd000Path = Some v } t)
      | "--canary-base" :: rest ->
        requireValue "--canary-base" rest |> Result.bind (fun (v, t) -> loop { acc with CanaryBasePath = Some v } t)
      | "--canary-candidate" :: rest ->
        requireValue "--canary-candidate" rest |> Result.bind (fun (v, t) -> loop { acc with CanaryCandidatePath = Some v } t)
      | "--retrieval-snapshot" :: rest ->
        requireValue "--retrieval-snapshot" rest |> Result.bind (fun (v, t) -> loop { acc with RetrievalSnapshotPath = Some v } t)
      | "--test-receipt" :: rest ->
        requireValue "--test-receipt" rest |> Result.bind (fun (v, t) -> loop { acc with TestReceiptPath = Some v } t)
      | "--spec-ballast-file" :: rest ->
        requireValue "--spec-ballast-file" rest |> Result.bind (fun (v, t) -> loop { acc with SpecBallastFilePath = Some v } t)
      | "--spec-ballast-query" :: rest ->
        requireValue "--spec-ballast-query" rest
        |> Result.bind (fun (v, t) ->
          if v.Length > 512 then Error(CliError.SpecBallastQueryTooLong v.Length)
          else loop { acc with SpecBallastQuery = Some v } t)
      | "--bozzetto-port" :: rest ->
        requireValue "--bozzetto-port" rest
        |> Result.bind (fun (v, t) ->
          match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
          | true, port when port >= 1 && port <= 65535 -> loop { acc with BozzettoPort = Some port } t
          | _ -> Error(CliError.InvalidPort v))
      | token :: rest ->
        let raw = match Option.ofObj token with Some s -> s | None -> ""
        if raw.StartsWith("-", StringComparison.Ordinal) then Error(CliError.UnknownFlag raw)
        elif String.IsNullOrWhiteSpace raw || containsNullChar raw then Error(CliError.BlankValue "positional argument")
        else loop { acc with Positionals = acc.Positionals @ [ raw.Trim() ] } rest

    loop initial argv
    |> Result.bind (fun parsed ->
      match parsed.Positionals with
      | [] | [ _ ] | [ _; _ ] -> Error(CliError.MissingPositionalArgs(3, parsed.Positionals.Length))
      | [ repoRaw; baseShaRaw; issueUrlRaw ] ->
        if String.IsNullOrWhiteSpace repoRaw || containsNullChar repoRaw then Error(CliError.BlankValue "<repo>")
        elif not (isHexString 7 40 baseShaRaw) then Error(CliError.InvalidBaseSha baseShaRaw)
        else
          parseIssueUrl issueUrlRaw
          |> Result.map (fun issueRef ->
            { RepoPath = repoRaw; BaseSha = baseShaRaw.Trim(); IssueUrl = issueUrlRaw.Trim(); IssueRef = issueRef
              OutPath = parsed.OutPath; CandidateRef = parsed.CandidateRef; IssueFile = parsed.IssueFile
              WikiFiles = parsed.WikiFiles; SarifPath = parsed.SarifPath; Axd000Path = parsed.Axd000Path
              CanaryBasePath = parsed.CanaryBasePath; CanaryCandidatePath = parsed.CanaryCandidatePath
              RetrievalSnapshotPath = parsed.RetrievalSnapshotPath; TestReceiptPath = parsed.TestReceiptPath
              SpecBallastFilePath = parsed.SpecBallastFilePath; SpecBallastQuery = parsed.SpecBallastQuery; BozzettoPort = parsed.BozzettoPort
              SkipRetrievalAwait = parsed.SkipRetrievalAwait; SkipClefSearch = parsed.SkipClefSearch
              OutsideIndex = parsed.OutsideIndex })
      | _ :: _ :: _ :: extra :: _ -> Error(CliError.UnexpectedPositionalArg extra))

  // Part 1: Verbatim Authority parsers (issue, comments, wiki rulings)

  let extractReferencedUrls (text: string) : string list =
    if String.IsNullOrWhiteSpace text then []
    else
      Regex.Matches(text, @"https?://[^\s\)\]>""'<>`]+")
      |> Seq.cast<Match>
      |> Seq.map (fun (m: Match) -> m.Value.TrimEnd('.', ',', ';', ':', ')', ']'))
      |> Seq.filter (fun (u: string) -> u.Length > 0)
      |> Seq.distinct
      |> Seq.toList

  let private parseCommentsArrayStrict (source: string) (arr: JsonValue) : Result<IssueComment list, PacketParseError> =
    match arr with
    | JsonValue.Array items ->
      let rec loop acc remaining =
        match remaining with
        | [] -> Ok(List.rev acc)
        | item :: rest ->
          match item with
          | JsonValue.Object _ ->
            match tryStringProp [ "body" ] item with
            | None ->
              Error(PacketParseError.MissingJsonField(source, "comments[].body"))
            | Some body ->
              let author =
                tryProp "user" item
                |> Option.bind (tryNonBlankStringProp [ "username"; "login" ])
                |> Option.defaultValue "unknown"
              let comment = {
                Id = tryInt64Prop [ "id" ] item
                Author = author
                Body = body
                HtmlUrl = tryStringProp [ "html_url" ] item |> Option.defaultValue ""
                CreatedAt = tryStringProp [ "created_at" ] item |> Option.defaultValue ""
              }
              loop (comment :: acc) rest
          | _ -> Error(PacketParseError.InvalidJsonField(source, "comments", "each comment must be a JSON object"))
      loop [] items
    | _ -> Error(PacketParseError.InvalidJsonField(source, "comments", "expected JSON array"))

  let parseIssuePayloadWithProvenance
      (provenance: AuthorityProvenance)
      (fallbackNumber: int)
      (fallbackUrl: string)
      (issuePayload: string)
      (commentsJsonOpt: string option)
      : Result<IssueAuthority, PacketParseError> =
    if String.IsNullOrWhiteSpace issuePayload then
      Error(PacketParseError.EmptyInput "Forgejo issue payload")
    else
      let trimmed = issuePayload.TrimStart()
      if trimmed.StartsWith("{", StringComparison.Ordinal) then
        parseJsonValue "Forgejo issue JSON" issuePayload
        |> Result.bind (fun root ->
          match root with
          | JsonValue.Object _ ->
            let issueObj =
              match tryProp "issue" root with
              | Some(JsonValue.Object _ as inner) -> inner
              | _ -> root
            let number = tryInt32Prop [ "number" ] issueObj |> Option.defaultValue fallbackNumber
            let title = tryNonBlankStringProp [ "title" ] issueObj |> Option.defaultValue ""
            let state = tryNonBlankStringProp [ "state" ] issueObj |> Option.defaultValue "unknown"
            let htmlUrl = tryNonBlankStringProp [ "html_url"; "url" ] issueObj |> Option.defaultValue fallbackUrl
            match tryStringProp [ "body" ] issueObj with
            | None ->
              Error(PacketParseError.MissingJsonField("Forgejo issue JSON", "body"))
            | Some body when String.IsNullOrWhiteSpace title && String.IsNullOrWhiteSpace body ->
              Error(PacketParseError.MissingJsonField("Forgejo issue JSON", "title/body"))
            | Some body ->
              let milestone = tryProp "milestone" issueObj |> Option.bind (tryNonBlankStringProp [ "title" ])
              let labels =
                match tryProp "labels" issueObj with
                | Some(JsonValue.Array items) ->
                  items
                  |> List.choose (fun l ->
                    match l with
                    | JsonValue.String _ -> tryNonBlankString l
                    | JsonValue.Object _ -> tryNonBlankStringProp [ "name" ] l
                    | _ -> None)
                | _ -> []
              let expectedCommentCountRes =
                match tryInt32Prop [ "comments" ] issueObj |> Option.orElseWith (fun () -> tryInt32Prop [ "comments" ] root) with
                | Some n when n < 0 ->
                  Error(PacketParseError.InvalidJsonField("Forgejo issue JSON", "comments", "comment count must be non-negative"))
                | opt -> Ok opt
              let commentsRes =
                match commentsJsonOpt with
                | Some rawComments ->
                  parseJsonValue "Forgejo comments JSON" rawComments
                  |> Result.bind (parseCommentsArrayStrict "Forgejo comments JSON")
                | None ->
                  match tryProp "comments" root with
                  | Some(JsonValue.Array _ as arr) -> parseCommentsArrayStrict "Forgejo issue JSON" arr
                  | Some(JsonValue.Number _) | Some(JsonValue.NumberLiteral _) | Some JsonValue.Null | None -> Ok []
                  | Some _ -> Error(PacketParseError.InvalidJsonField("Forgejo issue JSON", "comments", "expected JSON array or integer count"))
              let hasInlineCommentsArr =
                match tryProp "comments" root with
                | Some(JsonValue.Array _) -> true
                | _ -> false
              expectedCommentCountRes
              |> Result.bind (fun expectedCommentCount ->
                commentsRes
                |> Result.map (fun comments ->
                  let commentsVerified =
                    commentsJsonOpt.IsSome || hasInlineCommentsArr || expectedCommentCount = Some 0
                  let allText = (body :: (comments |> List.map _.Body)) |> String.concat "\n"
                  { Number = number; Title = title; State = state; HtmlUrl = htmlUrl
                    Milestone = milestone; Labels = labels; Body = body
                    Comments = comments; ExpectedCommentCount = expectedCommentCount
                    CommentsVerified = commentsVerified
                    ReferencedUrls = extractReferencedUrls allText
                    Provenance = provenance; WikiRulings = []; WikiRulingFailures = [] }))
          | _ -> Error(PacketParseError.ExpectedJsonObject "Forgejo issue JSON"))
      else
        match provenance with
        | AuthorityProvenance.LiveFetch _ ->
          Error(PacketParseError.ExpectedJsonObject "Forgejo issue JSON")
        | AuthorityProvenance.LocalFile _ ->
          let lines = issuePayload.Replace("\r\n", "\n").Split('\n')
          let title =
            lines
            |> Array.tryFind (fun (l: string) -> not (String.IsNullOrWhiteSpace l))
            |> Option.map (fun (l: string) -> l.TrimStart('#', ' ').Trim())
            |> Option.defaultValue (sprintf "Issue #%d" fallbackNumber)
          Ok {
            Number = fallbackNumber; Title = title; State = "file"; HtmlUrl = fallbackUrl
            Milestone = None; Labels = []; Body = issuePayload; Comments = []
            ExpectedCommentCount = None; CommentsVerified = false
            ReferencedUrls = extractReferencedUrls issuePayload
            Provenance = provenance; WikiRulings = []; WikiRulingFailures = []
          }

  let parseIssuePayload
      (fallbackNumber: int)
      (fallbackUrl: string)
      (issuePayload: string)
      (commentsJsonOpt: string option)
      : Result<IssueAuthority, PacketParseError> =
    let defaultProv =
      if not (String.IsNullOrWhiteSpace issuePayload) && issuePayload.TrimStart().StartsWith("{", StringComparison.Ordinal) then
        AuthorityProvenance.LiveFetch fallbackUrl
      else
        AuthorityProvenance.LocalFile "<inline>"
    parseIssuePayloadWithProvenance defaultProv fallbackNumber fallbackUrl issuePayload commentsJsonOpt

  let extractWikiBase64Field (sourceUrl: string) (raw: string) : Result<string option, PacketParseError> =
    if String.IsNullOrWhiteSpace raw then
      Error(PacketParseError.EmptyInput (sprintf "Wiki ruling %s" sourceUrl))
    else
      let trimmed = raw.TrimStart()
      if not (trimmed.StartsWith("{", StringComparison.Ordinal)) then
        Ok None
      else
        parseJsonValue (sprintf "Forgejo wiki page %s" sourceUrl) raw
        |> Result.bind (fun root ->
          match root with
          | JsonValue.Object _ ->
            match tryProp "content_base64" root with
            | Some(JsonValue.String b64) -> Ok(Some b64)
            | Some _ -> Error(PacketParseError.InvalidWikiPayload(sourceUrl, "content_base64 must be a string"))
            | None -> Ok None
          | _ -> Error(PacketParseError.ExpectedJsonObject (sprintf "Forgejo wiki page %s" sourceUrl)))

  let parseWikiPagePayload
      (provenance: AuthorityProvenance)
      (wikiRef: ForgejoWikiRef)
      (raw: string)
      (decodedBase64Opt: string option)
      : Result<WikiRuling, PacketParseError> =
    if String.IsNullOrWhiteSpace raw then
      Error(PacketParseError.EmptyInput (sprintf "Wiki ruling %s" wikiRef.CanonicalUrl))
    else
      let trimmed = raw.TrimStart()
      if trimmed.StartsWith("{", StringComparison.Ordinal) then
        parseJsonValue (sprintf "Forgejo wiki page %s" wikiRef.CanonicalUrl) raw
        |> Result.bind (fun root ->
          match root with
          | JsonValue.Object _ ->
            let title = tryNonBlankStringProp [ "title" ] root |> Option.defaultValue wikiRef.PageName
            let contentRes =
              match tryProp "content_base64" root with
              | Some(JsonValue.String _) ->
                match decodedBase64Opt with
                | Some decoded when not (String.IsNullOrWhiteSpace decoded) -> Ok decoded
                | Some _ -> Error(PacketParseError.EmptyInput (sprintf "Wiki ruling %s content_base64" wikiRef.CanonicalUrl))
                | None -> Error(PacketParseError.InvalidWikiPayload(wikiRef.CanonicalUrl, "content_base64 requires host-decoded UTF-8 markdown"))
              | Some _ -> Error(PacketParseError.InvalidWikiPayload(wikiRef.CanonicalUrl, "content_base64 must be a string"))
              | None ->
                match tryStringProp [ "content" ] root with
                | Some text when not (String.IsNullOrWhiteSpace text) -> Ok text
                | Some _ -> Error(PacketParseError.EmptyInput (sprintf "Wiki ruling %s content" wikiRef.CanonicalUrl))
                | None -> Error(PacketParseError.MissingJsonField(sprintf "Forgejo wiki page %s" wikiRef.CanonicalUrl, "content_base64/content"))
            contentRes
            |> Result.map (fun content ->
              { Url = wikiRef.CanonicalUrl
                Title = title
                PageName = wikiRef.PageName
                Content = content
                Provenance = provenance })
          | _ -> Error(PacketParseError.ExpectedJsonObject (sprintf "Forgejo wiki page %s" wikiRef.CanonicalUrl)))
      else
        match provenance with
        | AuthorityProvenance.LiveFetch _ ->
          Error(PacketParseError.InvalidWikiPayload(wikiRef.CanonicalUrl, "expected JSON response from Forgejo wiki API"))
        | AuthorityProvenance.LocalFile _ ->
          let lines = raw.Replace("\r\n", "\n").Split('\n')
          let title =
            lines
            |> Array.tryFind (fun (l: string) -> not (String.IsNullOrWhiteSpace l))
            |> Option.map (fun (l: string) -> l.TrimStart('#', ' ').Trim())
            |> Option.defaultValue wikiRef.PageName
          Ok {
            Url = wikiRef.CanonicalUrl
            Title = title
            PageName = wikiRef.PageName
            Content = raw
            Provenance = provenance
          }
