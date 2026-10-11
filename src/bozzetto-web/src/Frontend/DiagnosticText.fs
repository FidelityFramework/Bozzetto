/// Presentation of explicit compiler/log prefixes, never inferred severity.
/// Every segment is original text; concatenating them reconstructs the trace.
module Bozzetto.Web.Frontend.DiagnosticText

open System
open System.Text.RegularExpressions

type Segment = {| id: int; severity: string; text: string |}

// Composer: `path:line: error CCSnnnn: message` / `error CCSnnnn: message`.
// Alex/logs: `[ERROR] AXnnnn message` / `[WARN] message` / `[INFO] message`.
// A severity word inside an ordinary message is deliberately left unmarked.
let private prefix =
  Regex(
    @"^[ \t]*(?:\[(ERROR|ERR|WARNING|WARN|WRN|INFO|INF)\](?:[ \t]+((?:CCS|AX)[0-9]+)(?=[: \t\r\n]|$))?|(?:[^\r\n]+:[0-9]+:[ \t]*)?(error|warning|info)(?:[ \t]+(CCS[0-9]+))?[ \t]*:)",
    RegexOptions.Multiline ||| RegexOptions.IgnoreCase)

let private severity (label: string) =
  match label.ToLowerInvariant() with
  | "error" | "err" -> "error"
  | "warning" | "warn" | "wrn" -> "warning"
  | "info" | "inf" -> "info"
  | _ -> ""

let segments (text: string) : Segment array =
  let marked = ResizeArray<int * int * string>()
  for matched in prefix.Matches text do
    let label =
      if matched.Groups.[1].Success then matched.Groups.[1].Value
      else matched.Groups.[3].Value
    let kind = severity label
    marked.Add(matched.Index + matched.Value.LastIndexOf(label, StringComparison.Ordinal), label.Length, kind)
    let code =
      if matched.Groups.[2].Success then matched.Groups.[2].Value
      else matched.Groups.[4].Value
    if code <> "" then
      marked.Add(matched.Index + matched.Value.LastIndexOf(code, StringComparison.Ordinal), code.Length, kind)
  let result = ResizeArray<Segment>()
  let mutable offset = 0
  for start, length, kind in marked do
    if start > offset then
      result.Add {| id = offset; severity = ""; text = text.Substring(offset, start - offset) |}
    result.Add {| id = start; severity = kind; text = text.Substring(start, length) |}
    offset <- start + length
  if offset < text.Length then
    result.Add {| id = offset; severity = ""; text = text.Substring offset |}
  result.ToArray()
