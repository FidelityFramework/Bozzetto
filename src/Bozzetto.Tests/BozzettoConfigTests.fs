module Bozzetto.Tests.BozzettoConfigTests

open Expecto
open Expecto.Flip
open System
open Bozzetto

[<Tests>]
let bozzettoConfigTests =
  testList "BozzettoConfig" [

    testCase "DefaultMcpPort literal is 47749" <| fun _ ->
      BozzettoConfig.DefaultMcpPort
      |> Expect.equal "default MCP port" 47749

    testCase "DefaultDashboardPort is DefaultMcpPort + 1" <| fun _ ->
      BozzettoConfig.DefaultDashboardPort
      |> Expect.equal "dashboard port = mcp + 1" (BozzettoConfig.DefaultMcpPort + 1)

    testCase "DefaultDashboardPort is 47750" <| fun _ ->
      BozzettoConfig.DefaultDashboardPort
      |> Expect.equal "dashboard port" 47750

    testCase "WorkerStartupTimeoutMs is positive" <| fun _ ->
      (BozzettoConfig.WorkerStartupTimeoutMs, 0)
      |> Expect.isGreaterThan "must be positive"

    testCase "WorkerStartupTimeoutMs default is at least 30 seconds" <| fun _ ->
      (BozzettoConfig.WorkerStartupTimeoutMs, 30_000)
      |> Expect.isGreaterThan "at least 30s"

    testCase "RestartCount is non-negative" <| fun _ ->
      (BozzettoConfig.RestartCount, 0)
      |> Expect.isGreaterThanOrEqual "restart count must be non-negative"

    testCase "LoopbackHost.parse accepts exactly the loopback forms" <| fun _ ->
      for raw, expected in [ "", BozzettoConfig.LoopbackHost.Localhost
                             "localhost", BozzettoConfig.LoopbackHost.Localhost
                             " LOCALHOST ", BozzettoConfig.LoopbackHost.Localhost
                             "127.0.0.1", BozzettoConfig.LoopbackHost.Ipv4
                             "::1", BozzettoConfig.LoopbackHost.Ipv6
                             "[::1]", BozzettoConfig.LoopbackHost.Ipv6 ] do
        BozzettoConfig.LoopbackHost.parse raw
        |> Expect.equal (sprintf "%A must parse as loopback" raw) (Ok expected)

    testCase "LoopbackHost.parse refuses a LAN bind and says what to do instead" <| fun _ ->
      for raw in [ "0.0.0.0"; "192.168.1.20"; "::"; "*"; "+"; "bozzetto.local"; "localhost.evil.com" ] do
        match BozzettoConfig.LoopbackHost.parse raw with
        | Ok host -> failtestf "%s must be refused, parsed as %A" raw host
        | Error message ->
          message |> Expect.stringContains "the error names the variable" "BOZZETTO_BIND_HOST"
          message |> Expect.stringContains "the error explains why" "no authentication"
          message |> Expect.stringContains "the error gives the container alternative" "forwardPorts"

    testCase "every LoopbackHost round-trips through its listen URL" <| fun _ ->
      for host in [ BozzettoConfig.LoopbackHost.Localhost; BozzettoConfig.LoopbackHost.Ipv4; BozzettoConfig.LoopbackHost.Ipv6 ] do
        let url = BozzettoConfig.LoopbackHost.listenUrl host 47749
        Bozzetto.Server.HttpOriginGuard.isLoopbackOrigin url
        |> Expect.isTrue (sprintf "%s must be a loopback origin" url)

    testCase "OtelProtocol has a default value" <| fun _ ->
      BozzettoConfig.OtelProtocol
      |> Expect.isNotEmpty "otel protocol must not be empty"

    testCase "OtelServiceName has a default value" <| fun _ ->
      BozzettoConfig.OtelServiceName
      |> Expect.isNotEmpty "otel service name must not be empty"

    testCase "McpPortFromEnv exposes a valid default port even under arbitrary environment state" <| fun _ ->
      let value = BozzettoConfig.McpPortFromEnv
      (value > 0 && value < 65536)
      |> Expect.isTrue "MCP port value should remain in a valid range"
  ]
