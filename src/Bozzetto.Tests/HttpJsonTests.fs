module Bozzetto.Tests.HttpJsonTests

open Expecto
open Expecto.Flip
open Bozzetto.Server

[<Tests>]
let tests =
  testList "Hosted JSON boundary" [
    testCase "HTTP numeric aliases preserve exact integer spelling and reject overflow" <| fun () ->
      let read text = McpServer.tryGetJsonIntAliases (HttpJson.parse text) [ "position" ]
      read "{\"position\":2147483647}" |> Expect.equal "largest position survives" (Some 2147483647)
      for number in [ "2147483648"; "9007199254740993"; "1.0"; "1e0" ] do
        read ("{\"position\":" + number + "}") |> Expect.isNone "unsupported integer spelling cannot round into a position"
      read "{\"position\":1,\"position\":2}" |> Expect.equal "last duplicate field retains HTTP compatibility" (Some 2)
  ]
