/// Test discovery after an ordinary successful evaluation. This middleware
/// neither modifies the submitted code nor patches an existing method.
module Bozzetto.Middleware.TestDiscovery

open Bozzetto.AppState
open Bozzetto.Features.LiveTesting
open Bozzetto.HostAgent
open Bozzetto.Utils

let testDiscoveryMiddleware next (request, state: AppState) =
  let response, state = next (request, state)
  match response.EvaluationResult with
  | Error _ -> response, state
  | Ok _ when isNull (box state.Session) -> response, state
  | Ok _ ->
    let discovery =
      match Map.tryFind "liveTestRediscover" request.Args with
      | Some value when value = box true -> DiscoveryPolicy.Forced
      | _ -> DiscoveryPolicy.WhenChanged
    match state.Session.AfterEval { Discovery = discovery } with
    | AgentUnavailable reason ->
      Log.warn "[TestDiscovery] cannot scan tests: %s" reason
      response, state
    | AgentAnswered report ->
      let session = state.Session
      let runTest test = async {
        match! session.RunTest test with
        | AgentAnswered result -> return result
        | AgentUnavailable _ -> return TestResult.NotRun
      }
      let metadata =
        response.Metadata
          .Add("liveTestHookResult", report.LiveTest)
          .Add("liveTestRunTest", runTest)
      let metadata =
        if report.AssemblyLoadErrors.IsEmpty then metadata
        else metadata.Add("assemblyLoadErrors", report.AssemblyLoadErrors)
      { response with Metadata = metadata }, state
