namespace Bozzetto

open System
open System.Globalization
open Fidelity.Data.JSON

[<RequireQualifiedAccess>]
type JsonCasing = PascalCase | CamelCase

/// Explicit JSON construction for hosted wire schemas. Callers name every
/// field; domain records and unions are never inspected at runtime.
module WireJson =
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
