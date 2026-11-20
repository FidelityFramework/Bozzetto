namespace Bozzetto.Audit

open System
open System.Text.RegularExpressions

// Pure Zone A F# lexical stripper and anti-pattern token counter.
// Strips line comments, nested block comments (preserving the (*) operator),
// regular/verbatim/triple-quoted strings, and char literals while preserving
// code inside interpolated string holes. Zero I/O, zero mutable state.
[<AutoOpen>]
module AuditPacketLex =

  let isProductionSrcPath (path: string) : bool =
    let lower = path.ToLowerInvariant()
    let isFSharpSource =
      lower.EndsWith(".fs", StringComparison.Ordinal)
      || lower.EndsWith(".fsi", StringComparison.Ordinal)
      || lower.EndsWith(".fsx", StringComparison.Ordinal)
    let segments = path.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
    let inTestProject =
      segments
      |> Array.exists (fun (seg: string) ->
        seg.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
        || seg.EndsWith(".Test", StringComparison.OrdinalIgnoreCase))
    path.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
    && isFSharpSource
    && not inTestProject

  let internal isNativeServicePath (path: string) : bool =
    let baseName =
      match path.LastIndexOf('/') with
      | idx when idx >= 0 -> path.Substring(idx + 1)
      | _ -> path
    String.Equals(baseName, "NativeService.fs", StringComparison.OrdinalIgnoreCase)

  let private isAsciiHex (c: char) =
    (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

  let private isAsciiDigit (c: char) = c >= '0' && c <= '9'

  let private tryCharLiteralEnd (src: string) (i: int) : int option =
    let len = src.Length
    if i >= len || src[i] <> '\'' then None
    elif i + 7 < len
         && src[i + 1] = '\\'
         && src[i + 2] = 'u'
         && isAsciiHex src[i + 3]
         && isAsciiHex src[i + 4]
         && isAsciiHex src[i + 5]
         && isAsciiHex src[i + 6]
         && src[i + 7] = '\'' then
      Some(i + 8)
    elif i + 5 < len
         && src[i + 1] = '\\'
         && src[i + 2] = 'x'
         && isAsciiHex src[i + 3]
         && isAsciiHex src[i + 4]
         && src[i + 5] = '\'' then
      Some(i + 6)
    elif i + 5 < len
         && src[i + 1] = '\\'
         && isAsciiDigit src[i + 2]
         && isAsciiDigit src[i + 3]
         && isAsciiDigit src[i + 4]
         && src[i + 5] = '\'' then
      Some(i + 6)
    elif i + 3 < len && src[i + 1] = '\\' && src[i + 3] = '\'' then
      Some(i + 4)
    elif i + 2 < len
         && src[i + 1] <> '\\'
         && src[i + 1] <> '\''
         && src[i + 1] <> '\n'
         && src[i + 1] <> '\r'
         && src[i + 2] = '\'' then
      Some(i + 3)
    else None

  let stripFSharpStringsAndComments (src: string) : string =
    if String.IsNullOrEmpty src then ""
    else
      let len = src.Length
      let flush (startIdx: int) (endIdx: int) (acc: string list) =
        if endIdx > startIdx then src.Substring(startIdx, endIdx - startIdx) :: acc
        else acc
      let countChar (ch: char) (startIdx: int) =
        let rec loop idx acc =
          if idx < len && src[idx] = ch then loop (idx + 1) (acc + 1)
          else acc
        loop startIdx 0
      let hasRun (ch: char) (count: int) (startIdx: int) =
        let rec check k =
          if k >= count then true
          elif startIdx + k < len && src[startIdx + k] = ch then check (k + 1)
          else false
        count > 0 && check 0
      let rec skipLineComment (i: int) =
        if i >= len || src[i] = '\n' then i
        else skipLineComment (i + 1)
      let rec skipStringInComment (i: int) =
        if i >= len then len
        elif src[i] = '\\' then skipStringInComment (min len (i + 2))
        elif src[i] = '"' then i + 1
        else skipStringInComment (i + 1)
      let rec skipVerbatimInComment (i: int) =
        if i >= len then len
        elif src[i] = '"' then
          if i + 1 < len && src[i + 1] = '"' then skipVerbatimInComment (i + 2)
          else i + 1
        else skipVerbatimInComment (i + 1)
      let rec skipTripleInComment (i: int) =
        if i >= len then len
        elif i + 2 < len && src[i] = '"' && src[i + 1] = '"' && src[i + 2] = '"' then i + 3
        else skipTripleInComment (i + 1)
      let rec skipBlockComment (i: int) (depth: int) =
        if i >= len || depth <= 0 then i
        elif src[i] = '\'' then
          match tryCharLiteralEnd src i with
          | Some nextI -> skipBlockComment nextI depth
          | None -> skipBlockComment (i + 1) depth
        elif src[i] = '"' && i + 2 < len && src[i + 1] = '"' && src[i + 2] = '"' then
          skipBlockComment (skipTripleInComment (i + 3)) depth
        elif src[i] = '@' && i + 1 < len && src[i + 1] = '"' then
          skipBlockComment (skipVerbatimInComment (i + 2)) depth
        elif src[i] = '"' then
          skipBlockComment (skipStringInComment (i + 1)) depth
        elif src[i] = '(' && i + 1 < len && src[i + 1] = '*' && not (i + 2 < len && src[i + 2] = ')') then
          skipBlockComment (i + 2) (depth + 1)
        elif src[i] = '*' && i + 1 < len && src[i + 1] = ')' then
          skipBlockComment (i + 2) (depth - 1)
        else
          skipBlockComment (i + 1) depth
      let rec scanCode (i: int) (codeStart: int) (braceDepth: int) (closeBraceCount: int) (acc: string list) : int * string list =
        if i >= len then
          len, flush codeStart len acc
        else
          let c = src[i]
          if closeBraceCount > 0 && braceDepth = 0 && hasRun '}' closeBraceCount i then
            i + closeBraceCount, flush codeStart i acc
          elif closeBraceCount > 0 && c = '{' then
            scanCode (i + 1) codeStart (braceDepth + 1) closeBraceCount acc
          elif closeBraceCount > 0 && c = '}' && braceDepth > 0 then
            scanCode (i + 1) codeStart (braceDepth - 1) closeBraceCount acc
          elif c = '/' && i + 1 < len && src[i + 1] = '/' then
            let nextI = skipLineComment (i + 2)
            scanCode nextI nextI braceDepth closeBraceCount (flush codeStart i acc)
          elif c = '(' && i + 1 < len && src[i + 1] = '*' && not (i + 2 < len && src[i + 2] = ')') then
            let nextI = skipBlockComment (i + 2) 1
            scanCode nextI nextI braceDepth closeBraceCount (flush codeStart i acc)
          elif c = '$' then
            let dollarCount = countChar '$' i
            let afterDollars = i + dollarCount
            if afterDollars + 2 < len && src[afterDollars] = '"' && src[afterDollars + 1] = '"' && src[afterDollars + 2] = '"' then
              let nextI, acc' = scanTripleString (afterDollars + 3) dollarCount (flush codeStart i acc)
              scanCode nextI nextI braceDepth closeBraceCount acc'
            elif afterDollars + 1 < len && src[afterDollars] = '@' && src[afterDollars + 1] = '"' then
              let nextI, acc' = scanVerbatimString (afterDollars + 2) dollarCount (flush codeStart i acc)
              scanCode nextI nextI braceDepth closeBraceCount acc'
            elif afterDollars < len && src[afterDollars] = '"' then
              let nextI, acc' = scanRegularString (afterDollars + 1) dollarCount (flush codeStart i acc)
              scanCode nextI nextI braceDepth closeBraceCount acc'
            else
              scanCode afterDollars codeStart braceDepth closeBraceCount acc
          elif c = '@' then
            let dollarCount = countChar '$' (i + 1)
            let afterPrefix = i + 1 + dollarCount
            if afterPrefix < len && src[afterPrefix] = '"' then
              let nextI, acc' = scanVerbatimString (afterPrefix + 1) dollarCount (flush codeStart i acc)
              scanCode nextI nextI braceDepth closeBraceCount acc'
            else
              scanCode (i + 1) codeStart braceDepth closeBraceCount acc
          elif c = '"' && i + 2 < len && src[i + 1] = '"' && src[i + 2] = '"' then
            let nextI, acc' = scanTripleString (i + 3) 0 (flush codeStart i acc)
            scanCode nextI nextI braceDepth closeBraceCount acc'
          elif c = '"' then
            let nextI, acc' = scanRegularString (i + 1) 0 (flush codeStart i acc)
            scanCode nextI nextI braceDepth closeBraceCount acc'
          elif c = '\'' then
            match tryCharLiteralEnd src i with
            | Some nextI -> scanCode nextI nextI braceDepth closeBraceCount (flush codeStart i acc)
            | None -> scanCode (i + 1) codeStart braceDepth closeBraceCount acc
          else
            scanCode (i + 1) codeStart braceDepth closeBraceCount acc
      and scanInterpOpen (i: int) (dollarCount: int) (acc: string list) : int * string list =
        if dollarCount = 1 then
          if i + 1 < len && src[i + 1] = '{' then i + 2, acc
          else scanCode (i + 1) (i + 1) 0 1 acc
        else
          let run = countChar '{' i
          if run < dollarCount then i + run, acc
          else scanCode (i + run) (i + run) 0 dollarCount acc
      and scanRegularString (i: int) (dollarCount: int) (acc: string list) : int * string list =
        if i >= len then len, acc
        else
          let c = src[i]
          if c = '\\' then scanRegularString (min len (i + 2)) dollarCount acc
          elif c = '"' then i + 1, acc
          elif dollarCount > 0 && c = '{' then
            let nextI, acc' = scanInterpOpen i dollarCount acc
            scanRegularString nextI dollarCount acc'
          elif dollarCount = 1 && c = '}' && i + 1 < len && src[i + 1] = '}' then
            scanRegularString (i + 2) dollarCount acc
          else scanRegularString (i + 1) dollarCount acc
      and scanVerbatimString (i: int) (dollarCount: int) (acc: string list) : int * string list =
        if i >= len then len, acc
        else
          let c = src[i]
          if c = '"' then
            if i + 1 < len && src[i + 1] = '"' then scanVerbatimString (i + 2) dollarCount acc
            else i + 1, acc
          elif dollarCount > 0 && c = '{' then
            let nextI, acc' = scanInterpOpen i dollarCount acc
            scanVerbatimString nextI dollarCount acc'
          elif dollarCount = 1 && c = '}' && i + 1 < len && src[i + 1] = '}' then
            scanVerbatimString (i + 2) dollarCount acc
          else scanVerbatimString (i + 1) dollarCount acc
      and scanTripleString (i: int) (dollarCount: int) (acc: string list) : int * string list =
        if i >= len then len, acc
        elif i + 2 < len && src[i] = '"' && src[i + 1] = '"' && src[i + 2] = '"' then
          if i + 3 < len && src[i + 3] = '"' then scanTripleString (i + 1) dollarCount acc
          else i + 3, acc
        elif dollarCount > 0 && src[i] = '{' then
          let nextI, acc' = scanInterpOpen i dollarCount acc
          scanTripleString nextI dollarCount acc'
        elif dollarCount = 1 && src[i] = '}' && i + 1 < len && src[i + 1] = '}' then
          scanTripleString (i + 2) dollarCount acc
        else scanTripleString (i + 1) dollarCount acc

      let _, parts = scanCode 0 0 0 0 []
      parts |> List.rev |> String.concat " "

  let private rxCwt = Regex(@"\bConditionalWeakTable\b", RegexOptions.Compiled)
  let private rxConcurrent = Regex(@"\bSystem\.Collections\.Concurrent(?:\.[A-Za-z0-9_]+)*\b|\bConcurrent[A-Za-z0-9_]*\b", RegexOptions.Compiled)
  let private rxSysReflection =
    Regex(
      @"\bSystem\.Reflection(?:\.[A-Za-z0-9_]+)*\b|\b(?:PropertyInfo|MethodInfo|FieldInfo|ConstructorInfo|BindingFlags)\b|\bActivator\.CreateInstance\b|\.GetType\s*\(\s*\)\s*\.\s*Get(?:Property|Properties|Method|Methods|Field|Fields|Constructor|Constructors|Member|Members)\b",
      RegexOptions.Compiled)
  let private rxTypeofOpen = Regex(@"\btypeof\s*<", RegexOptions.Compiled)
  let private rxDotGetMember = Regex(@"^\s*\.\s*Get(?:Property|Properties|Method|Methods|Field|Fields|Constructor|Constructors|Member|Members)\b", RegexOptions.Compiled)
  let private rxFsReflection =
    Regex(@"\b(?:(?:Microsoft\.)?FSharp\.Reflection|FSharpValue|FSharpType)(?:\.[A-Za-z0-9_]+)*\b|\b(?:PreComputeRecordReader|PreComputeUnionReader|PreComputeUnionTagReader|GetUnionFields|MakeUnion)\b", RegexOptions.Compiled)
  let private rxBclMutable = Regex(@"\b(?:Dictionary|HashSet|ResizeArray|Queue|Stack|SortedDictionary|SortedSet|LinkedList|Hashtable|ArrayList|System\.Collections\.Generic\.List)\b", RegexOptions.Compiled)
  let private rxLetMutable = Regex(@"\blet\s+mutable\b", RegexOptions.Compiled)
  let private rxNormalize = Regex(@"\.normalize\b", RegexOptions.Compiled)

  let private countTypeofGetReflection (sanitized: string) : int =
    let len = sanitized.Length
    let rec findCloseAngle (i: int) (depth: int) : int option =
      if i >= len then None
      elif sanitized[i] = '<' then findCloseAngle (i + 1) (depth + 1)
      elif sanitized[i] = '>' then
        if depth = 1 then Some(i + 1)
        else findCloseAngle (i + 1) (depth - 1)
      elif sanitized[i] = '\r' || sanitized[i] = '\n' then None
      else findCloseAngle (i + 1) depth
    rxTypeofOpen.Matches(sanitized)
    |> Seq.cast<Match>
    |> Seq.sumBy (fun m ->
      match findCloseAngle (m.Index + m.Length) 1 with
      | Some afterAngle when afterAngle < len ->
        let tail = sanitized.Substring(afterAngle, min 64 (len - afterAngle))
        if rxDotGetMember.IsMatch tail then 1 else 0
      | _ -> 0)

  type SourcePatternCounts = {
    ConditionalWeakTable: int
    Concurrent: int
    SystemReflection: int
    FSharpReflection: int
    BclMutable: int
    LetMutable: int
    NativeServiceNormalize: int
  }

  let internal zeroCounts : SourcePatternCounts = {
    ConditionalWeakTable = 0
    Concurrent = 0
    SystemReflection = 0
    FSharpReflection = 0
    BclMutable = 0
    LetMutable = 0
    NativeServiceNormalize = 0
  }

  let countAntiPatternsInSource (isNativeService: bool) (rawSource: string) : SourcePatternCounts =
    let sanitized = stripFSharpStringsAndComments rawSource
    if String.IsNullOrWhiteSpace sanitized then zeroCounts
    else
      { ConditionalWeakTable = rxCwt.Matches(sanitized).Count
        Concurrent = rxConcurrent.Matches(sanitized).Count
        SystemReflection = rxSysReflection.Matches(sanitized).Count + countTypeofGetReflection sanitized
        FSharpReflection = rxFsReflection.Matches(sanitized).Count
        BclMutable = rxBclMutable.Matches(sanitized).Count
        LetMutable = rxLetMutable.Matches(sanitized).Count
        NativeServiceNormalize = if isNativeService then rxNormalize.Matches(sanitized).Count else 0 }
