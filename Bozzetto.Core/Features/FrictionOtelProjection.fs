module Bozzetto.Features.FrictionOtelProjection

open System.Diagnostics
open Bozzetto
open Bozzetto.Features.FrictionTelemetryTypes

module Projection =
  let tags (event: FrictionEvent) =
    let baseTags = [
      "bozzetto.mcp.tool_name", box (ToolName.value event.Tool)
      "bozzetto.mcp.intent_kind", box (string event.Intent)
      "bozzetto.mcp.outcome_kind", box (string (FrictionEvent.outcomeKind event))
      "bozzetto.session.id", box (SessionRef.value event.Session)
      "bozzetto.mcp.context_cost", box (string event.ContextCost)
      "bozzetto.mcp.duration_ms", box (DurationMs.value event.Duration)
    ]
    match event.Outcome with
    | FrictionOutcome.EncounteredBlocker blocker ->
      ("bozzetto.mcp.blocker_kind", box (string blocker)) :: baseTags
    | FrictionOutcome.RecoveredVia (ResolutionKind.SolvedWithDifferentTool tool) ->
      ("bozzetto.mcp.resolution_kind", box "SolvedWithDifferentTool")
      :: ("bozzetto.mcp.resolution_tool", box (ToolName.value tool))
      :: baseTags
    | FrictionOutcome.RecoveredVia resolution ->
      ("bozzetto.mcp.resolution_kind", box (string resolution)) :: baseTags
    | _ -> baseTags

  let emit (event: FrictionEvent) =
    use span = Instrumentation.startSpan Instrumentation.mcpSource "mcp.friction" (tags event)
    Instrumentation.succeedSpan span
