/// Proves `Dashboard.switchWorkflowViaApi` — the dashboard's ONLY network
/// call for the workflow switcher (bozzetto-ux-roast.md §4.1/§4.2/§11 Island B
/// item 4) — actually agrees with the real `POST /api/sessions/{sid}/workflow`
/// route it targets. Stands up the SAME kind of bare Kestrel host
/// `WorkflowRouteHttpTests.fs` uses for that route (a fake `SessionManagementOps`,
/// no real daemon, own ephemeral loopback port) and calls the dashboard's
/// glue function against it — the handler-logic tests
/// (`DashboardWorkflowSwitchHandlerTests.fs`) inject a fake network function,
/// so this is the one place that would catch a URL/body/field-name mismatch
/// between the two ends.
module Bozzetto.Tests.DashboardWorkflowSwitchApiWiringTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.McpTools
open Bozzetto.WorkerProtocol
open Bozzetto.Server.Dashboard

let private sid =
  match SessionId.validate "0a0b0c0d" with
  | Ok id -> id
  | Error e -> failwith e

let private unknownSid =
  match SessionId.validate "deadbeef" with
  | Ok id -> id
  | Error e -> failwith e

/// A fake SessionOps whose `SwitchWorkflow` only recognizes `sid` — matches
/// `WorkflowRouteHttpTests.fs`'s fake exactly (the real SessionManager
/// mailbox's `None` branch for an unknown session).
let private fakeOps : SessionManagementOps =
  { SessionManagementOps.stub with
      SwitchWorkflow = fun sidStr target ->
        match sidStr = SessionId.value sid with
        | true -> Task.FromResult(Ok (sprintf "Switching to %s" (WorkflowTypes.SessionWorkflow.label target)))
        | false -> Task.FromResult(Error (BozzettoError.SessionNotFound sidStr)) }

/// Stand up only `mapSessionRoutes` on a bare Kestrel host bound to an
/// ephemeral loopback port — this test's own spare port, never
/// 47749/47750.
let private startFakeApiServer (ops: SessionManagementOps) = task {
  let builder = WebApplication.CreateBuilder([||])
  builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
  builder.Logging.ClearProviders() |> ignore

  let config : Bozzetto.Server.McpServer.McpServerConfig =
    { DiagnosticsChanged = (Event<Bozzetto.Features.DiagnosticsStore.T>()).Publish
      StateChanged = None
      FrictionStore = None
      Port = 0
      BindHost = Bozzetto.BozzettoConfig.LoopbackHost.Localhost
      OwnOrigins = Bozzetto.Server.HttpOriginGuard.OwnOrigins.ofPorts [ 0 ]
      SessionOps = ops
      ElmRuntime = None
      GetWarmupContext = None
      GetHotReloadState = None
      SharedBindingScope = ref None
      SharedFeatureState = None
      ActivityTracker = Bozzetto.AgentActivityTracker.create ()
      LiveSnapshotSink = None
      CohortOwner = None
      GetDaemonHealth = fun () -> None
      Composer = None }

  let mcpContext : McpContext =
    { FrictionStore = None
      DiagnosticsChanged = config.DiagnosticsChanged
      StateChanged = None
      SessionOps = ops
      SessionMap = Collections.Concurrent.ConcurrentDictionary<string, string>()
      McpPort = 0
      Dispatch = None
      GetElmModel = None
      GetElmRegions = None
      GetWarmupContext = None
      GetFeatureState = None; RecordEval = None
      ActivityTracker = config.ActivityTracker
      LiveSnapshotSink = None
      CohortOwner = None
      GetDaemonHealth = fun () -> None
      GetProcessTelemetry = fun () -> None }

  let sseContext : Bozzetto.Server.McpServer.SseContext =
    { GetElmModel = None
      GetWarmupContext = None
      GetHotReloadState = None
      SseJsonOpts = Text.Json.JsonSerializerOptions()
      TestEventBroadcast = Event<string>()
      SessionEventBroadcast = Event<string>()
      ServerTracker = Bozzetto.Server.McpServer.McpServerTracker()
      CohortOwner = None }

  let rctx : Bozzetto.Server.McpServer.RouteContext =
    { Config = config
      McpContext = mcpContext
      SseContext = sseContext
      Dispatch = None
      GetElmRegions = None
      FsiBindings = ref Map.empty
      FeaturePushState = ref Bozzetto.Features.FeatureHooks.FeaturePushState.empty
      LastFeatureOutputCount = ref 0
      LastEvalContext = ref None }

  let app = builder.Build()
  Bozzetto.Server.McpServer.mapSessionRoutes app rctx
  do! app.StartAsync()
  let server = app.Services.GetRequiredService<IServer>()
  let port =
    server.Features.Get<IServerAddressesFeature>().Addresses
    |> Seq.head
    |> fun (addr: string) -> Uri(addr).Port
  return app, port
}

[<Tests>]
let apiWiringTests =
  testList "Dashboard.switchWorkflowViaApi — wired against the real route" [
    testTask "WHY — a recognized session + target resolves through the REAL route's success shape, not just a fake stub" {
      let! (app: WebApplication), port = startFakeApiServer fakeOps
      try
        let! result = switchWorkflowViaApi port sid WorkflowTypes.SessionWorkflow.Interactive
        result |> Expect.equal "the real route's message flows through unchanged" (Ok "Switching to REPL")
      finally (app :> IDisposable).Dispose()
    }

    testTask "WHY — an unknown session's REAL 404 BozzettoError body parses to an Error naming the real reason, not a swallowed success" {
      let! (app: WebApplication), port = startFakeApiServer fakeOps
      try
        let! result = switchWorkflowViaApi port unknownSid WorkflowTypes.SessionWorkflow.Interactive
        match result with
        | Error msg -> msg |> Expect.stringContains "names the unknown session" (SessionId.value unknownSid)
        | Ok m -> failtestf "an unknown session must not succeed, got Ok %s" m
      finally (app :> IDisposable).Dispose()
    }

    testTask "WHY — every dashboard-offered workflow round-trips through the real route" {
      let! (app: WebApplication), port = startFakeApiServer fakeOps
      try
        for w in Bozzetto.Server.DashboardTypes.WorkflowSwitch.options do
          let! result = switchWorkflowViaApi port sid w
          match result with
          | Ok _ -> ()
          | Error e -> failtestf "%s should switch successfully via the real route, got: %s" (WorkflowTypes.SessionWorkflow.label w) e
      finally (app :> IDisposable).Dispose()
    }
  ]
