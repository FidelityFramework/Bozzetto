namespace Bozzetto.Server

open Fidelity.Data.JSON
open Microsoft.AspNetCore.Http

/// JSON is a boundary value with an explicit schema, never a reflected object.
module HttpJson =
  exception InvalidJson of string

  let parse text =
    match Json.parse text with
    | Ok value -> value
    | Error reason -> raise (InvalidJson reason)

  /// Preserve the last-property semantics of the existing HTTP contracts.
  let property name = function
    | JsonValue.Object fields -> fields |> List.tryFindBack (fst >> (=) name) |> Option.map snd
    | _ -> None

  let stringProperty name value = property name value |> Option.bind JsonValue.asString
  let textProperty name value = stringProperty name value |> Option.defaultValue ""
  let requiredStringProperty name value =
    match stringProperty name value with
    | Some text -> text
    | None -> raise (InvalidJson (sprintf "Missing or invalid string field '%s'." name))
  let integer value = JsonValue.ofInt64 (int64 value)
  let optional encode = Option.map encode >> Option.defaultValue JsonValue.Null
  let strings = List.map JsonValue.String >> JsonValue.Array

  let write (ctx: HttpContext) value = task {
    ctx.Response.ContentType <- "application/json; charset=utf-8"
    do! ctx.Response.WriteAsync(Json.serialize value, ctx.RequestAborted)
  }

  let error message = JsonValue.Object [ "error", JsonValue.String message ]
  let result success key message =
    JsonValue.Object [ "success", JsonValue.Bool success; key, JsonValue.String message ]
