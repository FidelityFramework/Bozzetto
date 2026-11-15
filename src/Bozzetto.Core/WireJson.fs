namespace Bozzetto

open System
open System.Globalization
open System.Text
open Fidelity.Data.JSON
open Fidelity.Data.XML

[<RequireQualifiedAccess>]
type JsonCasing = PascalCase | CamelCase

/// Pure XML query helpers over Fidelity.Data.XML trees.
module XmlQuery =
  let parseText (xmlText: string) : Result<XmlDocument, string> =
    let trimmed =
      if isNull xmlText then ""
      else xmlText.TrimStart('\uFEFF')
    Xml.parse trimmed

  let localName (name: string) : string =
    let idx = name.LastIndexOf ':'
    if idx >= 0 then name.Substring(idx + 1) else name

  let rec descendants (node: XmlNode) : XmlNode list =
    match node with
    | XmlNode.Element (_, _, children) ->
      node :: (children |> List.collect descendants)
    | _ -> []

  let descendantsNamed (targetLocalName: string) (node: XmlNode) : XmlNode list =
    descendants node
    |> List.filter (fun n ->
      match XmlNode.name n with
      | Some nm -> localName nm = targetLocalName
      | None -> false)

  let childElementsNamed (targetLocalName: string) (node: XmlNode) : XmlNode list =
    XmlNode.elements node
    |> List.filter (fun n ->
      match XmlNode.name n with
      | Some nm -> localName nm = targetLocalName
      | None -> false)

  let attr (attrName: string) (node: XmlNode) : string option =
    XmlNode.attr attrName node

  let innerText (node: XmlNode) : string =
    XmlNode.innerText node

/// Explicit JSON construction for hosted wire schemas. Callers name every
/// field; domain records and unions are never inspected at runtime.
module WireJson =
  let parseRelaxed (json: string) : Result<JsonValue, string> =
    if isNull json then Error "JSON input is null"
    else
      let s = json.TrimStart('\uFEFF')
      let sb1 = StringBuilder(s.Length)
      let mutable i = 0
      let mutable inStr = false
      let mutable esc = false
      let mutable commentErr = None
      while i < s.Length && commentErr.IsNone do
        let c = s[i]
        if inStr then
          sb1.Append(c) |> ignore
          if esc then esc <- false
          elif c = '\\' then esc <- true
          elif c = '"' then inStr <- false
          i <- i + 1
        elif c = '"' then
          inStr <- true
          sb1.Append(c) |> ignore
          i <- i + 1
        elif i + 1 < s.Length && c = '/' && s[i + 1] = '/' then
          i <- i + 2
          while i < s.Length && s[i] <> '\n' && s[i] <> '\r' do
            i <- i + 1
        elif i + 1 < s.Length && c = '/' && s[i + 1] = '*' then
          i <- i + 2
          let mutable closed = false
          while i + 1 < s.Length && not closed do
            if s[i] = '*' && s[i + 1] = '/' then
              closed <- true
              i <- i + 2
            else
              i <- i + 1
          if not closed then
            commentErr <- Some "Unterminated block comment in JSON"
        else
          sb1.Append(c) |> ignore
          i <- i + 1
      match commentErr with
      | Some err -> Error err
      | None ->
        let s1 = sb1.ToString()
        let sb2 = StringBuilder(s1.Length)
        let mutable j = 0
        inStr <- false
        esc <- false
        while j < s1.Length do
          let c = s1[j]
          if inStr then
            sb2.Append(c) |> ignore
            if esc then esc <- false
            elif c = '\\' then esc <- true
            elif c = '"' then inStr <- false
            j <- j + 1
          elif c = '"' then
            inStr <- true
            sb2.Append(c) |> ignore
            j <- j + 1
          elif c = ',' then
            let mutable k = j + 1
            while k < s1.Length && Char.IsWhiteSpace(s1[k]) do
              k <- k + 1
            if k < s1.Length && (s1[k] = '}' || s1[k] = ']') then
              j <- j + 1
            else
              sb2.Append(c) |> ignore
              j <- j + 1
          else
            sb2.Append(c) |> ignore
            j <- j + 1
        Json.parse (sb2.ToString())
  let text value = if isNull value then JsonValue.Null else JsonValue.String value
  let integer (value: int) = JsonValue.ofInt64 (int64 value)
  let signed value = JsonValue.ofInt64 value
  let unsigned value = JsonValue.ofUInt64 value
  let boolean = JsonValue.Bool
  let number value =
    if Double.IsFinite value then JsonValue.Number value
    else invalidArg (nameof value) "JSON numbers must be finite."
  let optional encode value = value |> Option.map encode |> Option.defaultValue JsonValue.Null
  let array encode values = values |> Seq.map encode |> Seq.toList |> JsonValue.Array
  let objectValue casing properties =
    properties |> List.map (fun (name: string, value) ->
      let key =
        match casing with
        | JsonCasing.PascalCase -> name
        | JsonCasing.CamelCase when name.Length > 0 -> string (Char.ToLowerInvariant name[0]) + name.Substring 1
        | JsonCasing.CamelCase -> name
      key, value)
    |> JsonValue.Object
  let union name fields =
    JsonValue.Object (["Case", text name] @ (if List.isEmpty fields then [] else ["Fields", JsonValue.Array fields]))
  let private trimFraction (value: string) =
    let start = value.IndexOf '.'
    if start < 0 then value
    else
      let mutable finish = start + 1
      while finish < value.Length && Char.IsAsciiDigit value[finish] do finish <- finish + 1
      let mutable significant = finish
      while significant > start + 1 && value[significant - 1] = '0' do significant <- significant - 1
      if significant = start + 1 then value[..start - 1] + value[finish..]
      else value[..significant - 1] + value[finish..]
  let date (value: DateTime) = value.ToString("O", CultureInfo.InvariantCulture) |> trimFraction |> text
  let timestamp (value: DateTimeOffset) = value.ToString("O", CultureInfo.InvariantCulture) |> trimFraction |> text
  let duration (value: TimeSpan) = value.ToString("c", CultureInfo.InvariantCulture) |> text
  let serialize = Json.serialize
