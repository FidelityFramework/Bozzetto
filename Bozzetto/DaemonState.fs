namespace Bozzetto.Server

open Bozzetto

// The SSE state-change event vocabulary formerly defined here as
// `DaemonStateChange` now lives in SseEvent.fs, unified with the former
// SessionEvents.SessionEvent type into one `SseEvent` DU with one
// serializer (roast-5 §1). This file keeps only daemon info/probe state.

module DaemonInfo =
  let version = ReleaseVersion.current ()

  let otelConfigured =
    System.Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    |> Option.ofObj |> Option.isSome
