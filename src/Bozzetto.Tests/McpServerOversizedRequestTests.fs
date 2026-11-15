module Bozzetto.Tests.McpServerOversizedRequestTests

open System
open System.IO
open System.Text
open Fidelity.Data.JSON
open Expecto
open Expecto.Flip
open Microsoft.AspNetCore.Http
open Bozzetto.Server.McpServer

let private maxBodyBytes = 4_194_304L

let private oversizedContext (body: string) =
  let ctx = DefaultHttpContext()
  ctx.Request.ContentType <- "application/json"
  ctx.Request.ContentLength <- Nullable(maxBodyBytes + 1L)
  ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes(body))
  ctx.Response.Body <- new MemoryStream()
  ctx

let private responseBody (ctx: HttpContext) =
  let body = ctx.Response.Body :?> MemoryStream
  body.ToArray() |> Encoding.UTF8.GetString

let private expectTooLargeResponse (ctx: HttpContext) =
  ctx.Response.StatusCode
  |> Expect.equal "should return 413" 413

  ctx.Response.ContentType
  |> Expect.equal "should return JSON content type" "application/json"

  let root = Json.parseOrFail (responseBody ctx)

  match JsonValue.prop "success" root |> Option.bind JsonValue.asBool with
  | Some b -> b |> Expect.isFalse "should mark the request as unsuccessful"
  | None -> failtestf "Expected bool property 'success'"

  match JsonValue.prop "error" root |> Option.bind JsonValue.asString with
  | Some s -> s |> Expect.equal "should explain the body size failure" "Request body too large"
  | None -> failtestf "Expected string property 'error'"

[<Tests>]
let tests =
  testList "McpServer oversized request handling" [
    testTask "readJsonBody returns a consistent 413 JSON envelope" {
      let ctx = oversizedContext "{}"

      let! rejected =
        task {
          try
            let! _ = readJsonBody ctx
            return false
          with
          | RequestTooLarge -> return true
        }

      rejected |> Expect.isTrue "should reject oversized request bodies"
      expectTooLargeResponse ctx
    }

    testTask "readJsonProp returns a consistent 413 JSON envelope" {
      let ctx = oversizedContext """{"sessionId":"ignored"}"""

      let! rejected =
        task {
          try
            let! _ = readJsonProp ctx "sessionId"
            return false
          with
          | RequestTooLarge -> return true
        }

      rejected |> Expect.isTrue "should reject oversized request bodies"
      expectTooLargeResponse ctx
    }
  ]
