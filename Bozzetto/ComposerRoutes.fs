module Bozzetto.Server.ComposerRoutes

open System
open System.IO
open Fidelity.Data.JSON
open System.Threading.Channels
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Bozzetto.ComposerIntegration

let private framingError = ComposerClientJson.framingError

let private writeJson (ctx: HttpContext) status (response: JsonValue) = task {
  ctx.Response.StatusCode <- status
  ctx.Response.ContentType <- "application/json; charset=utf-8"
  do! ctx.Response.WriteAsync(Json.serialize response, ctx.RequestAborted)
}

let internal readBody (ctx: HttpContext) = task {
  let maximum = 1024 * 1024
  if ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value > int64 maximum then
    raise (InvalidDataException "Composer request exceeds 1 MiB.")
  use content = new MemoryStream()
  let buffer = Array.zeroCreate<byte> 16384
  let mutable reading = true
  while reading do
    let! count = ctx.Request.Body.ReadAsync(buffer.AsMemory(), ctx.RequestAborted)
    if count = 0 then reading <- false
    elif content.Length + int64 count > int64 maximum then
      raise (InvalidDataException "Composer request exceeds 1 MiB.")
    else content.Write(buffer, 0, count)
  try
    let utf8 = System.Text.UTF8Encoding(false, true)
    return content.ToArray() |> utf8.GetString |> Json.parse
  with :? System.Text.DecoderFallbackException as error -> return Result.Error error.Message
}

/// Composer's HTTP API. Browser entry points share UiBridge's live page.
let mapRoutes (app: WebApplication) (supervisor: ComposerSupervisor) =
  app.MapGet("/api/composer/sessions", RequestDelegate(fun (ctx: HttpContext) -> task {
    let! response = supervisor.SessionsAsync ctx.RequestAborted
    do! writeJson ctx 200 (ComposerClientJson.directory response)
  })) |> ignore

  app.MapPost("/api/composer/{operation}", RequestDelegate(fun (ctx: HttpContext) -> task {
    try
      let! parsed = readBody ctx
      match parsed with
      | Result.Error message -> do! writeJson ctx 400 (framingError "invalid_json" message)
      | Result.Ok body ->
        match ComposerClientJson.request (string ctx.Request.RouteValues["operation"]) body with
        | Result.Error(code, message) -> do! writeJson ctx 400 (framingError (ComposerClientJson.refusalCode code) message)
        | Result.Ok request ->
          let! response = supervisor.ExecuteAsync(request, ctx.RequestAborted)
          do! writeJson ctx 200 (ComposerClientJson.reply response)
    with
    | :? InvalidDataException as error -> do! writeJson ctx 413 (framingError "request_too_large" error.Message)
  })) |> ignore

  // The browser and MCP resource subscriptions observe the same change signal.
  // Events are invalidation hints; the shared status query supplies authority.
  app.MapGet("/api/composer/events", RequestDelegate(fun (ctx: HttpContext) -> task {
    ctx.Response.ContentType <- "text/event-stream"
    ctx.Response.Headers["Cache-Control"] <- "no-cache"
    let options = BoundedChannelOptions(1, FullMode = BoundedChannelFullMode.DropOldest)
    let events = Channel.CreateBounded<unit> options
    use subscription = supervisor.Changed.Subscribe(fun () -> events.Writer.TryWrite() |> ignore)
    events.Writer.TryWrite() |> ignore
    try
      let mutable listening = true
      while listening do
        let! available = events.Reader.WaitToReadAsync(ctx.RequestAborted)
        if not available then listening <- false
        else
          let mutable ignored = ()
          while events.Reader.TryRead(&ignored) do ()
          do! ctx.Response.WriteAsync("data: changed\n\n", ctx.RequestAborted)
          do! ctx.Response.Body.FlushAsync(ctx.RequestAborted)
    with :? OperationCanceledException -> ()
  })) |> ignore
