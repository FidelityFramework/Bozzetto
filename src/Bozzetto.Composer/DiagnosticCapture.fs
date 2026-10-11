namespace Bozzetto.Composer

open System
open Bozzetto.Diagnostics
open Core.CompilationOrchestrator

/// Optional diagnostic demand at the worker edge. The compiler hands over
/// source-owned PSG values; transport and persistence never confer authority.
module DiagnosticCapture =
  let fromEnvironment () =
    match Environment.GetEnvironmentVariable "BOZZETTO_DIAGNOSTIC_PIPE" |> Option.ofObj with
    | None -> None
    | Some pipe when String.IsNullOrWhiteSpace pipe -> None
    | Some pipe ->
      Some (fun (prepared: Result<BuildCapture, string>) -> async {
        let report message = Console.Error.WriteLine("Diagnostic capture: " + message)
        match prepared with
        | Result.Error reason -> report ("source refused the requested occurrence capture: " + reason)
        | Result.Ok value ->
          let capture: OccurrenceCapture = {
            Metadata = {
              Demand = value.Demand
              SourceVersion = value.SourceVersion
              ExpectedScopes = value.Delivery.Sections |> List.map _.Descriptor.Content }
            Delivery = value.Delivery
            ContextHeaders = value.ContextHeaders
            ContextRoots = value.ContextRoots }
          let! result = Client.capture pipe 10000 capture
          match result with
          | Result.Ok receipt -> report (sprintf "persisted demand %s: %A" value.Demand receipt)
          | Result.Error reason -> report ("demand " + value.Demand + ": " + reason)
      })
