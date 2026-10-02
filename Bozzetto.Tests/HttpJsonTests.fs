module Bozzetto.Tests.HttpJsonTests

open System.IO
open System.Text
open Expecto
open Expecto.Flip
open Fidelity.Data.JSON
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Bozzetto.Server

let private signalsContext () =
  let ctx = DefaultHttpContext()
  let mutable maximum = System.Nullable<int64>()
  ctx.Features.Set<IHttpMaxRequestBodySizeFeature>(
    { new IHttpMaxRequestBodySizeFeature with
        member _.IsReadOnly = false
        member _.MaxRequestBodySize with get () = maximum and set value = maximum <- value })
  ctx

[<Tests>]
let tests =
  testList "Hosted JSON boundary" [
    testCase "HTTP numeric aliases preserve exact integer spelling and reject overflow" <| fun () ->
      let read text = McpServer.tryGetJsonIntAliases (HttpJson.parse text) [ "position" ]
      read "{\"position\":2147483647}" |> Expect.equal "largest position survives" (Some 2147483647)
      for number in [ "2147483648"; "9007199254740993"; "1.0"; "1e0" ] do
        read ("{\"position\":" + number + "}") |> Expect.isNone "unsupported integer spelling cannot round into a position"
      read "{\"position\":1,\"position\":2}" |> Expect.equal "last duplicate field retains HTTP compatibility" (Some 2)

    testTask "Datastar POST preserves escaped signals and exact value tokens" {
      let ctx = signalsContext ()
      ctx.Request.Method <- "POST"
      let bytes = Encoding.UTF8.GetBytes "{\"code\":\"quoted \\\"name\\\"\\nλ\",\"revision\":9007199254740993}"
      use body = new MemoryStream(bytes)
      ctx.Request.Body <- body
      ctx.Request.ContentLength <- int64 bytes.Length
      let! signals = DashboardTypes.readSignalsJsonSized ctx
      DashboardTypes.getSignalString signals "code" "code-text"
      |> Expect.equal "source text reaches the handler unchanged" "quoted \"name\"\nλ"
      HttpJson.property "revision" signals |> Option.bind JsonValue.tryAsInt64
      |> Expect.equal "large token survives signal parsing" (Some 9007199254740993L)
    }

    testTask "Datastar GET reads its query envelope without consuming a request body" {
      let ctx = signalsContext ()
      ctx.Request.Method <- "GET"
      ctx.Request.QueryString <- QueryString("?datastar=%7B%22code-text%22%3A%22hello%22%7D")
      let! signals = DashboardTypes.readSignalsJsonSized ctx
      DashboardTypes.getSignalString signals "code" "code-text"
      |> Expect.equal "kebab-case alias works from query signals" "hello"
    }
  ]
