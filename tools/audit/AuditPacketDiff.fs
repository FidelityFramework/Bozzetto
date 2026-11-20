namespace Bozzetto.Audit

open System
open System.Globalization
open System.Text.RegularExpressions

// Pure Zone A code-delta and automated anti-pattern delta scanner over git
// --name-status -z, git --numstat, source blobs, and unified diffs.
[<AutoOpen>]
module AuditPacketDiff =

  [<Literal>]
  let CohesionLineLimit = 1000

  let private zeroDelta : CounterDelta = { Added = 0; Removed = 0; Delta = 0 }

  let private addDeltas (a: CounterDelta) (b: CounterDelta) : CounterDelta =
    { Added = a.Added + b.Added
      Removed = a.Removed + b.Removed
      Delta = (a.Added + b.Added) - (a.Removed + b.Removed) }

  let private deltaFromCounts (beforeCount: int) (afterCount: int) : CounterDelta =
    let diff = afterCount - beforeCount
    { Added = max 0 diff; Removed = max 0 (-diff); Delta = diff }

  let private emptyFileHit (path: string) : FileAntiPatternHit =
    { Path = path
      ConditionalWeakTable = zeroDelta
      Concurrent = zeroDelta
      SystemReflection = zeroDelta
      FSharpReflection = zeroDelta
      BclMutable = zeroDelta
      LetMutable = zeroDelta
      NativeServiceNormalize = zeroDelta }

  let private addFileHits (a: FileAntiPatternHit) (b: FileAntiPatternHit) : FileAntiPatternHit =
    { Path = a.Path
      ConditionalWeakTable = addDeltas a.ConditionalWeakTable b.ConditionalWeakTable
      Concurrent = addDeltas a.Concurrent b.Concurrent
      SystemReflection = addDeltas a.SystemReflection b.SystemReflection
      FSharpReflection = addDeltas a.FSharpReflection b.FSharpReflection
      BclMutable = addDeltas a.BclMutable b.BclMutable
      LetMutable = addDeltas a.LetMutable b.LetMutable
      NativeServiceNormalize = addDeltas a.NativeServiceNormalize b.NativeServiceNormalize }

  let private hasNonZeroHit (h: FileAntiPatternHit) : bool =
    h.ConditionalWeakTable.Added > 0 || h.ConditionalWeakTable.Removed > 0
    || h.Concurrent.Added > 0 || h.Concurrent.Removed > 0
    || h.SystemReflection.Added > 0 || h.SystemReflection.Removed > 0
    || h.FSharpReflection.Added > 0 || h.FSharpReflection.Removed > 0
    || h.BclMutable.Added > 0 || h.BclMutable.Removed > 0
    || h.LetMutable.Added > 0 || h.LetMutable.Removed > 0
    || h.NativeServiceNormalize.Added > 0 || h.NativeServiceNormalize.Removed > 0

  let parseNameStatusZ (raw: string) : Result<(GitChangeKind * string option * string) list, PacketParseError> =
    if String.IsNullOrEmpty raw then Ok []
    else
      let tokens = raw.Split('\000') |> Array.toList
      let parseScore (code: string) =
        let numPart = code.Substring 1
        if String.IsNullOrEmpty numPart then Some 100
        else
          match Int32.TryParse(numPart, NumberStyles.None, CultureInfo.InvariantCulture) with
          | true, s when s >= 0 && s <= 100 -> Some s
          | _ -> None
      let rec loop acc remaining =
        match remaining with
        | [] | [ "" ] -> Ok(List.rev acc)
        | "A" :: path :: rest when not (String.IsNullOrEmpty path) ->
          loop ((GitChangeKind.Added, None, path) :: acc) rest
        | "D" :: path :: rest when not (String.IsNullOrEmpty path) ->
          loop ((GitChangeKind.Deleted, Some path, path) :: acc) rest
        | "M" :: path :: rest when not (String.IsNullOrEmpty path) ->
          loop ((GitChangeKind.Modified, Some path, path) :: acc) rest
        | "T" :: path :: rest when not (String.IsNullOrEmpty path) ->
          loop ((GitChangeKind.TypeChanged, Some path, path) :: acc) rest
        | "U" :: path :: rest when not (String.IsNullOrEmpty path) ->
          loop ((GitChangeKind.Modified, Some path, path) :: acc) rest
        | code :: oldPath :: newPath :: rest
          when code.StartsWith("R", StringComparison.Ordinal)
               && not (String.IsNullOrEmpty oldPath)
               && not (String.IsNullOrEmpty newPath) ->
          match parseScore code with
          | Some score ->
            loop ((GitChangeKind.Renamed score, Some oldPath, newPath) :: acc) rest
          | None -> Error(PacketParseError.InvalidGitStatus(sprintf "invalid rename status '%s'" code))
        | code :: oldPath :: newPath :: rest
          when code.StartsWith("C", StringComparison.Ordinal)
               && not (String.IsNullOrEmpty oldPath)
               && not (String.IsNullOrEmpty newPath) ->
          match parseScore code with
          | Some score ->
            loop ((GitChangeKind.Copied score, Some oldPath, newPath) :: acc) rest
          | None -> Error(PacketParseError.InvalidGitStatus(sprintf "invalid copy status '%s'" code))
        | bad :: _ ->
          Error(PacketParseError.InvalidGitStatus(sprintf "unexpected status token '%s'" bad))
      loop [] tokens

  let private resolveRenamedPath (rawPath: string) : string * string option =
    let trimmed = normalizeSlash (rawPath.Trim())
    let braceMatch = Regex.Match(trimmed, @"^(.*)\{([^{}]*)\s*=>\s*([^{}]*)\}(.*)$")
    if braceMatch.Success then
      let prefix = braceMatch.Groups[1].Value
      let oldMid = braceMatch.Groups[2].Value
      let newMid = braceMatch.Groups[3].Value
      let suffix = braceMatch.Groups[4].Value
      let cleanJoin (a: string) (b: string) (c: string) = (a + b + c).Replace("//", "/")
      cleanJoin prefix newMid suffix, Some(cleanJoin prefix oldMid suffix)
    else
      let arrowIdx = trimmed.IndexOf(" => ", StringComparison.Ordinal)
      if arrowIdx >= 0 then
        let oldP = trimmed.Substring(0, arrowIdx).Trim()
        let newP = trimmed.Substring(arrowIdx + 4).Trim()
        newP, Some oldP
      else
        trimmed, None

  let private parseNumstatCounts (preserveExactPath: bool) (aRaw: string) (rRaw: string) (path: string) (oldPath: string option) : NumstatEntry option =
    let normPath = if preserveExactPath then path else stripDiffPrefix path
    let normOld = if preserveExactPath then oldPath else oldPath |> Option.map stripDiffPrefix
    if aRaw = "-" || rRaw = "-" then
      Some {
        Path = normPath
        OldPath = normOld
        AddedLines = None
        RemovedLines = None
        IsBinary = true
      }
    else
      match Int32.TryParse(aRaw, NumberStyles.Integer, CultureInfo.InvariantCulture),
            Int32.TryParse(rRaw, NumberStyles.Integer, CultureInfo.InvariantCulture) with
      | (true, added), (true, removed) ->
        Some {
          Path = normPath
          OldPath = normOld
          AddedLines = Some added
          RemovedLines = Some removed
          IsBinary = false
        }
      | _ -> None

  let private splitFirstTwoTabs (s: string) : (string * string * string) option =
    let t1 = s.IndexOf('\t')
    if t1 < 0 then None
    else
      let t2 = s.IndexOf('\t', t1 + 1)
      if t2 < 0 then None
      else Some(s.Substring(0, t1), s.Substring(t1 + 1, t2 - t1 - 1), s.Substring(t2 + 1))

  let parseNumstatZ (numstatZ: string) : NumstatEntry list =
    if String.IsNullOrEmpty numstatZ then []
    else
      let tokens = numstatZ.Split('\000') |> Array.toList
      let rec loop acc remaining =
        match remaining with
        | [] | [ "" ] -> List.rev acc
        | head :: tail ->
          match splitFirstTwoTabs head with
          | None -> loop acc tail
          | Some(aRaw, rRaw, restPath) ->
            if String.IsNullOrEmpty restPath then
              match tail with
              | oldP :: newP :: rest ->
                let acc' =
                  match parseNumstatCounts true aRaw rRaw newP (Some oldP) with
                  | Some e -> e :: acc
                  | None -> acc
                loop acc' rest
              | _ -> List.rev acc
            else
              let acc' =
                match parseNumstatCounts true aRaw rRaw restPath None with
                | Some e -> e :: acc
                | None -> acc
              loop acc' tail
      loop [] tokens

  let parseNumstat (numstatText: string) : NumstatEntry list =
    if String.IsNullOrWhiteSpace numstatText then []
    else
      numstatText.Replace("\r\n", "\n").Split('\n')
      |> Array.toList
      |> List.choose (fun (line: string) ->
        if String.IsNullOrWhiteSpace line then None
        else
          let parts = line.Split('\t')
          if parts.Length < 3 then None
          else
            let rawPath = parts |> Array.skip 2 |> String.concat "\t"
            let path, oldPath = resolveRenamedPath rawPath
            parseNumstatCounts false parts[0] parts[1] path oldPath)

  let private rxHunk = Regex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled)
  let private rxDiffGit = Regex(@"^diff --git a/(.+?) b/(.+)$", RegexOptions.Compiled)

  let private fileHitFromCounts (path: string) (beforeC: SourcePatternCounts) (afterC: SourcePatternCounts) : FileAntiPatternHit =
    { Path = path
      ConditionalWeakTable = deltaFromCounts beforeC.ConditionalWeakTable afterC.ConditionalWeakTable
      Concurrent = deltaFromCounts beforeC.Concurrent afterC.Concurrent
      SystemReflection = deltaFromCounts beforeC.SystemReflection afterC.SystemReflection
      FSharpReflection = deltaFromCounts beforeC.FSharpReflection afterC.FSharpReflection
      BclMutable = deltaFromCounts beforeC.BclMutable afterC.BclMutable
      LetMutable = deltaFromCounts beforeC.LetMutable afterC.LetMutable
      NativeServiceNormalize = deltaFromCounts beforeC.NativeServiceNormalize afterC.NativeServiceNormalize }

  type private DiffParseState = {
    OldPath: string option
    NewPath: string option
    CurrentPath: string option
    OldRemaining: int
    NewRemaining: int
    InHunk: bool
    BeforeHunkRev: string list
    AfterHunkRev: string list
    AddedCounts: Map<string, int>
    RemovedCounts: Map<string, int>
    BinaryPaths: Set<string>
    SeenOrder: string list
    Hits: Map<string, FileAntiPatternHit>
  }

  let private flushHunk (st: DiffParseState) : DiffParseState =
    if List.isEmpty st.BeforeHunkRev && List.isEmpty st.AfterHunkRev then st
    else
      let oldInProd = st.OldPath |> Option.map isProductionSrcPath |> Option.defaultValue false
      let newInProd = st.NewPath |> Option.map isProductionSrcPath |> Option.defaultValue false
      let targetKeyOpt =
        if newInProd then st.NewPath
        elif oldInProd then st.OldPath
        else None
      let hits' =
        match targetKeyOpt with
        | None -> st.Hits
        | Some keyPath ->
          let beforeCounts =
            if oldInProd then
              let oldPath = st.OldPath |> Option.defaultValue keyPath
              st.BeforeHunkRev |> List.rev |> String.concat "\n" |> countAntiPatternsInSource (isNativeServicePath oldPath)
            else zeroCounts
          let afterCounts =
            if newInProd then
              let newPath = st.NewPath |> Option.defaultValue keyPath
              st.AfterHunkRev |> List.rev |> String.concat "\n" |> countAntiPatternsInSource (isNativeServicePath newPath)
            else zeroCounts
          let hunkHit = fileHitFromCounts keyPath beforeCounts afterCounts
          let existing = Map.tryFind keyPath st.Hits |> Option.defaultValue (emptyFileHit keyPath)
          Map.add keyPath (addFileHits existing hunkHit) st.Hits
      { st with BeforeHunkRev = []; AfterHunkRev = []; Hits = hits' }

  let private ensureFileSeen (path: string) (st: DiffParseState) : DiffParseState =
    let clean = stripDiffPrefix path
    if clean = "/dev/null" || String.IsNullOrWhiteSpace clean then st
    else
      let order = if List.contains clean st.SeenOrder then st.SeenOrder else st.SeenOrder @ [ clean ]
      let hits = if Map.containsKey clean st.Hits then st.Hits else Map.add clean (emptyFileHit clean) st.Hits
      { st with CurrentPath = Some clean; SeenOrder = order; Hits = hits }

  let private parseHunkCounts (line: string) : (int * int) option =
    let m = rxHunk.Match line
    if not m.Success then None
    else
      let parseOptCount (idx: int) : int =
        let grp = m.Groups[idx]
        let rawVal = grp.Value
        if grp.Success && not (String.IsNullOrEmpty rawVal) then
          match Int32.TryParse(rawVal, NumberStyles.Integer, CultureInfo.InvariantCulture) with
          | true, n -> n
          | false, _ -> 1
        else 1
      Some(parseOptCount 2, parseOptCount 4)

  let private buildAntiPatternReport
      (fileDeltas: FileLineDelta list)
      (scannedSrcFiles: string list)
      (perFileHits: FileAntiPatternHit list)
      : AntiPatternDeltaReport =
    let sumField selector =
      perFileHits |> List.fold (fun acc h -> addDeltas acc (selector h)) zeroDelta
    let prodFiles = fileDeltas |> List.filter _.IsProductionSrc
    let beforeKnown = prodFiles |> List.choose _.BeforeLines
    let afterKnown = prodFiles |> List.choose _.AfterLines
    let maxBeforeOpt, maxAfterOpt, deltaMaxLines =
      if List.isEmpty beforeKnown && List.isEmpty afterKnown then
        let maxGrowth =
          match prodFiles with
          | [] -> 0
          | xs -> xs |> List.map _.DeltaLines |> List.max
        None, None, maxGrowth
      else
        let mb = if List.isEmpty beforeKnown then 0 else List.max beforeKnown
        let ma = if List.isEmpty afterKnown then 0 else List.max afterKnown
        Some mb, Some ma, ma - mb
    let maxSingleFileDelta =
      match prodFiles with
      | [] -> 0
      | xs -> xs |> List.map _.DeltaLines |> List.max
    let overBudgetGrowth =
      prodFiles
      |> List.choose (fun f ->
        match f.BeforeLines, f.AfterLines with
        | Some b, Some a when a > 750 && a > b -> Some(f.Path, b, a)
        | None, Some a when a > 750 -> Some(f.Path, 0, a)
        | _ -> None)
    { ScannedSrcFiles = scannedSrcFiles
      ConditionalWeakTable = sumField _.ConditionalWeakTable
      Concurrent = sumField _.Concurrent
      SystemReflection = sumField _.SystemReflection
      FSharpReflection = sumField _.FSharpReflection
      BclMutable = sumField _.BclMutable
      LetMutable = sumField _.LetMutable
      NativeServiceNormalize = sumField _.NativeServiceNormalize
      MaxFileLinesBefore = maxBeforeOpt
      MaxFileLinesAfter = maxAfterOpt
      DeltaMaxFileLines = deltaMaxLines
      MaxSingleFileDelta = maxSingleFileDelta
      OverBudgetFileGrowth = overBudgetGrowth
      PerFileHits = perFileHits }

  let private buildFileDeltas
      (allPaths: string list)
      (numstat: NumstatEntry list)
      (addedCounts: Map<string, int>)
      (removedCounts: Map<string, int>)
      (binaryPaths: Set<string>)
      (fileLineCounts: Map<string, int option * int option>)
      : FileLineDelta list =
    let numstatByPath = numstat |> List.map (fun e -> e.Path, e) |> Map.ofList
    allPaths
    |> List.map (fun path ->
      let ns = Map.tryFind path numstatByPath
      let added =
        ns |> Option.bind _.AddedLines
        |> Option.defaultWith (fun () -> Map.tryFind path addedCounts |> Option.defaultValue 0)
      let removed =
        ns |> Option.bind _.RemovedLines
        |> Option.defaultWith (fun () -> Map.tryFind path removedCounts |> Option.defaultValue 0)
      let isBinary =
        (ns |> Option.map _.IsBinary |> Option.defaultValue false)
        || Set.contains path binaryPaths
      let beforeLines, afterLines =
        match Map.tryFind path fileLineCounts with
        | Some(b, a) -> b, a
        | None -> None, None
      let deltaLines =
        match beforeLines, afterLines with
        | Some b, Some a -> a - b
        | None, Some a -> a
        | Some b, None -> -b
        | None, None -> added - removed
      let exceedsCohesion =
        match afterLines with
        | Some a -> a > CohesionLineLimit
        | None -> false
      { Path = path
        BeforeLines = beforeLines
        AfterLines = afterLines
        AddedLines = added
        RemovedLines = removed
        DeltaLines = deltaLines
        IsBinary = isBinary
        IsProductionSrc = isProductionSrcPath path
        ExceedsCohesionLimit = exceedsCohesion })

  let scanUnifiedDiff
      (rawDiff: string)
      (numstat: NumstatEntry list)
      (fileLineCounts: Map<string, int option * int option>)
      : FileLineDelta list * AntiPatternDeltaReport =
    let initial = {
      OldPath = None
      NewPath = None
      CurrentPath = None
      OldRemaining = 0
      NewRemaining = 0
      InHunk = false
      BeforeHunkRev = []
      AfterHunkRev = []
      AddedCounts = Map.empty
      RemovedCounts = Map.empty
      BinaryPaths = Set.empty
      SeenOrder = []
      Hits = Map.empty
    }
    let lines =
      if String.IsNullOrEmpty rawDiff then [||]
      else rawDiff.Replace("\r\n", "\n").Split('\n')

    let finalState =
      lines
      |> Array.fold (fun st (line: string) ->
        let diffMatch = rxDiffGit.Match line
        if diffMatch.Success && not (st.InHunk && (st.OldRemaining > 0 || st.NewRemaining > 0)) then
          let flushed = flushHunk st
          let aPath = diffMatch.Groups[1].Value |> stripDiffPrefix
          let bPath = diffMatch.Groups[2].Value |> stripDiffPrefix
          { (ensureFileSeen bPath { flushed with OldRemaining = 0; NewRemaining = 0; InHunk = false }) with
              OldPath = Some aPath
              NewPath = Some bPath
              CurrentPath = Some bPath }
        elif line.StartsWith("diff --git ", StringComparison.Ordinal) && not (st.InHunk && (st.OldRemaining > 0 || st.NewRemaining > 0)) then
          let flushed = flushHunk st
          let rest = line.Substring(11)
          let bIdx = rest.LastIndexOf(" b/", StringComparison.Ordinal)
          let aPath, bPath =
            if bIdx >= 0 then rest.Substring(0, bIdx) |> stripDiffPrefix, rest.Substring(bIdx + 3) |> stripDiffPrefix
            else rest |> stripDiffPrefix, rest |> stripDiffPrefix
          { (ensureFileSeen bPath { flushed with OldRemaining = 0; NewRemaining = 0; InHunk = false }) with
              OldPath = Some aPath
              NewPath = Some bPath
              CurrentPath = Some bPath }
        elif line.StartsWith("@@ ", StringComparison.Ordinal) then
          let flushed = flushHunk st
          match parseHunkCounts line with
          | Some(oldC, newC) -> { flushed with OldRemaining = oldC; NewRemaining = newC; InHunk = true }
          | None -> flushed
        elif not st.InHunk || (st.OldRemaining = 0 && st.NewRemaining = 0) then
          let flushed = flushHunk st
          if line.StartsWith("rename from ", StringComparison.Ordinal) then
            { flushed with OldPath = Some(line.Substring 12 |> stripDiffPrefix) }
          elif line.StartsWith("rename to ", StringComparison.Ordinal) then
            let np = line.Substring 10 |> stripDiffPrefix
            { (ensureFileSeen np flushed) with NewPath = Some np }
          elif line = "--- /dev/null" then
            { flushed with OldPath = None }
          elif line.StartsWith("--- a/", StringComparison.Ordinal) then
            { flushed with OldPath = Some(line.Substring 6 |> stripDiffPrefix) }
          elif line.StartsWith("--- ", StringComparison.Ordinal) then
            { flushed with OldPath = Some(line.Substring 4 |> stripDiffPrefix) }
          elif line = "+++ /dev/null" then
            { flushed with NewPath = None }
          elif line.StartsWith("+++ b/", StringComparison.Ordinal) then
            let np = line.Substring 6 |> stripDiffPrefix
            { (ensureFileSeen np flushed) with NewPath = Some np }
          elif line.StartsWith("+++ ", StringComparison.Ordinal) then
            let np = line.Substring 4 |> stripDiffPrefix
            { (ensureFileSeen np flushed) with NewPath = Some np }
          elif line.StartsWith("Binary files ", StringComparison.Ordinal) then
            match flushed.CurrentPath with
            | Some p -> { flushed with BinaryPaths = Set.add p flushed.BinaryPaths }
            | None -> flushed
          else flushed
        else
          match st.CurrentPath with
          | None -> st
          | Some path ->
            if line.StartsWith("+", StringComparison.Ordinal) then
              let content = line.Substring 1
              let prevAdded = Map.tryFind path st.AddedCounts |> Option.defaultValue 0
              { st with
                  NewRemaining = max 0 (st.NewRemaining - 1)
                  AddedCounts = Map.add path (prevAdded + 1) st.AddedCounts
                  AfterHunkRev = content :: st.AfterHunkRev }
            elif line.StartsWith("-", StringComparison.Ordinal) then
              let content = line.Substring 1
              let prevRemoved = Map.tryFind path st.RemovedCounts |> Option.defaultValue 0
              { st with
                  OldRemaining = max 0 (st.OldRemaining - 1)
                  RemovedCounts = Map.add path (prevRemoved + 1) st.RemovedCounts
                  BeforeHunkRev = content :: st.BeforeHunkRev }
            elif line.StartsWith(" ", StringComparison.Ordinal) then
              let content = line.Substring 1
              { st with
                  OldRemaining = max 0 (st.OldRemaining - 1)
                  NewRemaining = max 0 (st.NewRemaining - 1)
                  BeforeHunkRev = content :: st.BeforeHunkRev
                  AfterHunkRev = content :: st.AfterHunkRev }
            else st
      ) initial
      |> flushHunk

    let allPaths = (finalState.SeenOrder @ (numstat |> List.map _.Path)) |> List.distinct
    let fileDeltas = buildFileDeltas allPaths numstat finalState.AddedCounts finalState.RemovedCounts finalState.BinaryPaths fileLineCounts
    let scannedSrcFiles = allPaths |> List.filter isProductionSrcPath
    let perFileHits =
      finalState.Hits
      |> Map.toList
      |> List.map snd
      |> List.filter hasNonZeroHit
    fileDeltas, buildAntiPatternReport fileDeltas scannedSrcFiles perFileHits

  let private isQuarantinedBranchName (candidateBranchOpt: string option) : bool =
    match candidateBranchOpt with
    | Some b ->
      let trimmed = b.Trim()
      not (String.IsNullOrEmpty trimmed)
      && not (String.Equals(trimmed, "main", StringComparison.OrdinalIgnoreCase))
      && not (String.Equals(trimmed, "master", StringComparison.OrdinalIgnoreCase))
      && not (String.Equals(trimmed, "HEAD", StringComparison.OrdinalIgnoreCase))
    | None -> false

  let scanDiffWithChanges
      (baseSha: string)
      (candidateRef: string)
      (candidateSha: string)
      (candidateBranchOpt: string option)
      (rawDiff: string)
      (rawNumstatText: string)
      (numstatEntries: NumstatEntry list)
      (changes: GitFileChange list)
      (fileLineCounts: Map<string, int option * int option>)
      : CodeDeltaReport =
    let numstat =
      if List.isEmpty numstatEntries then parseNumstat rawNumstatText
      else numstatEntries
    let allPaths =
      ((changes |> List.map _.NewPath) @ (numstat |> List.map _.Path))
      |> List.filter (fun p -> not (String.IsNullOrEmpty p))
      |> List.distinct
    let fileDeltas = buildFileDeltas allPaths numstat Map.empty Map.empty Set.empty fileLineCounts

    let scannedAcc, hitsAcc, unaccountedAcc =
      changes
      |> List.fold (fun (scanned, hits, unacc) ch ->
        let oldInProd =
          match ch.Kind, ch.OldPath with
          | GitChangeKind.Copied _, _ -> false
          | _, Some p -> isProductionSrcPath p
          | _, None -> false
        let newInProd =
          match ch.Kind with
          | GitChangeKind.Deleted -> false
          | _ -> isProductionSrcPath ch.NewPath
        if not oldInProd && not newInProd then
          match ch.ReadError with
          | Some err -> scanned, hits, unacc @ [ (ch.NewPath, err) ]
          | None -> scanned, hits, unacc
        else
          let keyPath =
            if newInProd then ch.NewPath
            else ch.OldPath |> Option.defaultValue ch.NewPath
          let scanned' = if List.contains keyPath scanned then scanned else scanned @ [ keyPath ]
          match ch.ReadError with
          | Some err -> scanned', hits, unacc @ [ (keyPath, err) ]
          | None ->
            let beforeCounts =
              if oldInProd then
                let oldPath = ch.OldPath |> Option.defaultValue keyPath
                ch.BeforeBlob
                |> Option.map (countAntiPatternsInSource (isNativeServicePath oldPath))
                |> Option.defaultValue zeroCounts
              else zeroCounts
            let afterCounts =
              if newInProd then
                ch.AfterBlob
                |> Option.map (countAntiPatternsInSource (isNativeServicePath ch.NewPath))
                |> Option.defaultValue zeroCounts
              else zeroCounts
            let hit = fileHitFromCounts keyPath beforeCounts afterCounts
            let hits' = if hasNonZeroHit hit then hits @ [ hit ] else hits
            scanned', hits', unacc
      ) ([], [], [])

    { BaseSha = baseSha
      CandidateRef = candidateRef
      CandidateSha = candidateSha
      CandidateBranch = candidateBranchOpt
      IsQuarantinedBranch = isQuarantinedBranchName candidateBranchOpt
      RawNumstat = rawNumstatText
      Numstat = numstat
      Files = fileDeltas
      RawDiff = rawDiff
      AntiPatterns = buildAntiPatternReport fileDeltas scannedAcc hitsAcc
      UnaccountedSrcFiles = unaccountedAcc }

  let scanDiff
      (baseSha: string)
      (candidateRef: string)
      (candidateSha: string)
      (candidateBranchOpt: string option)
      (rawDiff: string)
      (numstatText: string)
      (fileLineCounts: Map<string, int option * int option>)
      : CodeDeltaReport =
    let numstat = parseNumstat numstatText
    let files, antiPatterns = scanUnifiedDiff rawDiff numstat fileLineCounts
    { BaseSha = baseSha
      CandidateRef = candidateRef
      CandidateSha = candidateSha
      CandidateBranch = candidateBranchOpt
      IsQuarantinedBranch = isQuarantinedBranchName candidateBranchOpt
      RawNumstat = numstatText
      Numstat = numstat
      Files = files
      RawDiff = rawDiff
      AntiPatterns = antiPatterns
      UnaccountedSrcFiles = [] }
