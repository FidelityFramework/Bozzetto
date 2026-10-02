namespace Bozzetto.Server

open Fidelity.Data.JSON

/// The MCP SDK exposes JsonElement. Keep that foreign representation at this
/// adapter; Fidelity.Data owns the payload schema, encoding and inspection.
module McpProtocolJson =
  let toElement value =
    use document = System.Text.Json.JsonDocument.Parse(Json.serialize value)
    document.RootElement.Clone()

  let parseElement text = Json.parseOrFail text |> toElement

  let ofElement (value: System.Text.Json.JsonElement) =
    Json.parseOrFail (value.GetRawText())
