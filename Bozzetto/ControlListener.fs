/// The daemon's control listener on MCP port + 1: discovery, graceful
/// shutdown, and the Composer redirect, as ASP.NET Core minimal APIs.
///
/// It is deliberately a separate WebApplication from the MCP server. `boz
/// status`/`boz stop`, the MCP stdio bridge, the VS Code client and
/// scripts/start-shared-daemon all discover and stop the daemon here, so this
/// listener has to keep answering (and report the failure) when the MCP
/// server's own startup never got far enough to serve anything at all.
module Bozzetto.Server.ControlListener

open System
open System.Threading.Tasks
open Fidelity.Data.JSON
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Bozzetto

/// The `/api/daemon-info` payload. `DashboardPort` keeps its historical wire
/// name (`dashboardPort`): clients and the persisted daemon records read the
/// control listener's port under that key.
type DaemonInfoContract = {
  Pid: int
  Version: string
  StartedAt: string
  WorkingDirectory: string
  McpPort: int
  DashboardPort: int
  ApiVersion: int
  SessionCount: int
  // A failed component (the MCP server itself, a session's file watcher)
  // reported into Bozzetto.Features.ComponentWatch — one line per failure,
  // "component: reason — hint". This listener is its own WebApplication,
  // separate from the MCP server's, so it keeps answering (and can carry
  // this) even when the MCP server never started — that split is exactly
  // how a daemon with a fully dead MCP server used to still read as
  // "Bozzetto daemon running" from `boz status`, which probes this endpoint,
  // not the MCP server's own `/health`.
  ComponentFailures: string list
}

[<RequireQualifiedAccess>]
module DaemonInfoContract =
  let toJsonValue (value: DaemonInfoContract) =
    JsonValue.Object [
      "pid", HttpJson.integer value.Pid
      "version", JsonValue.String value.Version
      "startedAt", JsonValue.String value.StartedAt
      "workingDirectory", JsonValue.String value.WorkingDirectory
      "mcpPort", HttpJson.integer value.McpPort
      "dashboardPort", HttpJson.integer value.DashboardPort
      "apiVersion", HttpJson.integer value.ApiVersion
      "sessionCount", HttpJson.integer value.SessionCount
      "componentFailures", HttpJson.strings value.ComponentFailures
    ]

  let create pid version startedAt workingDirectory mcpPort sessionCount : DaemonInfoContract =
    { Pid = pid
      Version = version
      StartedAt = startedAt
      WorkingDirectory = workingDirectory
      McpPort = mcpPort
      DashboardPort = mcpPort + 1
      ApiVersion = EndpointContracts.apiVersion
      SessionCount = sessionCount
      ComponentFailures =
        Bozzetto.Features.ComponentWatch.current ()
        |> List.map (fun f -> sprintf "%s: %s — %s" f.Component f.Reason f.Hint) }

/// What the control routes read from, and do to, the running daemon.
type ControlDeps = {
  Version: string
  McpPort: int
  GetSessionCount: unit -> Task<int>
  Shutdown: unit -> unit
}

/// Map the control routes onto `app`.
let mapRoutes (deps: ControlDeps) (app: WebApplication) : unit =
  // Browsers probe /favicon.ico unconditionally; answer it with the Clef
  // logo the page's own tab shows, instead of a 404.
  app.MapGet("/favicon.ico", fun (ctx: HttpContext) -> UiBridge.Icons.write UiBridge.Icons.ico.Value ctx) |> ignore
  // The Composer page is served by the MCP listener, which owns the daemon's
  // Composer coordination; a bookmark on this port lands there.
  app.MapGet("/composer", fun (ctx: HttpContext) ->
    let target = UriBuilder("http", ctx.Request.Host.Host, deps.McpPort, "/composer")
    ctx.Response.Redirect(target.Uri.AbsoluteUri)
    Task.CompletedTask
  ) |> ignore
  // Client discovery (replaces daemon.json).
  app.MapGet("/api/daemon-info", fun (ctx: HttpContext) ->
    task {
      let startedAt =
        let proc = System.Diagnostics.Process.GetCurrentProcess()
        proc.StartTime.ToUniversalTime()
      let! sessionCount = deps.GetSessionCount()
      let data =
        DaemonInfoContract.create
          Environment.ProcessId
          deps.Version
          (startedAt.ToString("o"))
          Environment.CurrentDirectory
          deps.McpPort
          sessionCount
      do! HttpJson.write ctx (DaemonInfoContract.toJsonValue data)
    } :> Task
  ) |> ignore
  // Graceful shutdown: answer first, then cancel the daemon.
  app.MapPost("/api/shutdown", fun (ctx: HttpContext) ->
    task {
      do! HttpJson.write ctx (JsonValue.Object [ "status", JsonValue.String "shutting_down" ])
      deps.Shutdown ()
    } :> Task
  ) |> ignore

/// Run the control listener until the daemon's `stopping` token is cancelled.
let start
  (log: ILogger)
  (bindHost: Bozzetto.BozzettoConfig.LoopbackHost)
  (ownOrigins: Bozzetto.Server.HttpOriginGuard.OwnOrigins)
  (port: int)
  (deps: ControlDeps)
  (stopping: System.Threading.CancellationToken) = task {
  try
    let builder = WebApplication.CreateBuilder()
    builder.Logging
      .AddFilter("Microsoft.AspNetCore", LogLevel.Warning)
      .AddFilter("Microsoft.Hosting", LogLevel.Warning)
    |> ignore
    let app = builder.Build()
    app.Urls.Add(Bozzetto.BozzettoConfig.LoopbackHost.listenUrl bindHost port)
    // Origin/CSRF gate (see HttpOriginGuard): only this daemon's own pages and
    // local tooling reach the shutdown endpoint.
    McpServer.useOriginGuard ownOrigins app
    mapRoutes deps app
    log.LogInformation("Control listener available at http://localhost:{Port}/api/daemon-info", port)
    do! McpServer.runUntilCancelled app stopping
  with ex ->
    log.LogWarning("Control listener failed to start: {Error}", ex.Message)
}
