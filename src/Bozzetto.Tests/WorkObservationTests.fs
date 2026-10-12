module Bozzetto.Tests.WorkObservationTests

open System
open System.IO
open System.Threading
open Expecto
open Expecto.Flip
open Bozzetto.Server.WorkObservation
open Bozzetto.Web.Shared.Protocol

let private node = { Node = "one"; Incarnation = "process-1" }
let private request topic seconds = { ProtocolVersion = ProtocolVersion; Node = "one"; Topic = topic; Seconds = seconds }
let private worker id endpoint token =
  sprintf "{\"id\":\"%s\",\"base_url\":\"%s\",\"token_file\":\"%s\"}" id endpoint token
let private config rows = "{\"workers\":[" + String.concat "," rows + "]}"

[<Tests>]
let tests =
  testList "Work observation transport" [
    testCase "existing Pi authority supplies only fixed observe destinations" <| fun () ->
      let value = config [worker "one" "https://proxy.example/workers/one/" "/private/one.token"; worker "two" "http://10.0.0.2:8787" "/private/two.token"]
      match parsePeers "controller" value with
      | Error reason -> failtest reason
      | Ok peers ->
        peers |> List.map _.Node |> Expect.equal "configured node ids" ["one"; "two"]
        peers[0].Endpoint.AbsoluteUri |> Expect.equal "fixed route under configured prefix" "https://proxy.example/workers/one/v1/bozzetto/observe"
        peers[1].Endpoint.AbsoluteUri |> Expect.equal "fixed route on bare origin" "http://10.0.0.2:8787/v1/bozzetto/observe"
    testCase "invalid destinations and duplicate nodes are refused" <| fun () ->
      for endpoint in ["file:///private"; "https://user:secret@proxy.example"; "https://proxy.example/?target=other"; "https://proxy.example/#fragment"] do
        parsePeers "controller" (config [worker "one" endpoint "/private/token"])
        |> Result.isError |> Expect.isTrue "endpoint cannot add authority"
      parsePeers "controller" (config [worker "one" "https://one.example" "/private/token";worker "one" "https://two.example" "/private/token"])
      |> Expect.equal "duplicate identity has a specific refusal" (Error "Duplicate work node id.")
      parsePeers "controller" (config [worker "one" "https://one.example" "relative.token"])
      |> Expect.equal "credential reference stays absolute" (Error "Worker token_file must be absolute.")
    testCase "work and bounded resource scopes are read-only" <| fun () ->
      validateRequest node (request "work" 0) |> Expect.equal "work admitted" (Ok ())
      for seconds in [1;120] do
        validateRequest node (request "resources" seconds) |> Expect.equal "bounded resources admitted" (Ok ())
      for topic, seconds in ["build",0;"work",1;"resources",0;"resources",121] do
        validateRequest node (request topic seconds) |> Expect.equal "execution and invalid windows refused" (Error "invalid_observation_scope")
    testCase "node and protocol are checked before scope admission" <| fun () ->
      validateRequest node { request "work" 0 with Node = "two" }
      |> Expect.equal "cannot observe another node by alias" (Error "node_mismatch")
      validateRequest node { request "work" 0 with ProtocolVersion = ProtocolVersion - 1 }
      |> Expect.equal "no protocol fallback" (Error "protocol_mismatch")
    testCase "transcript reads bind the node incarnation and bounded cursor" <| fun () ->
      let read = { PeerTranscriptRequest.ProtocolVersion = ProtocolVersion
                   Request = { Source = node; Journal = "journal"; Sequence = 1L; Stream = Stdout; Offset = 0L; MaxBytes = MaxTranscriptPageBytes } }
      validateTranscriptRequest node read |> Expect.equal "bounded read admitted" (Ok())
      validateTranscriptRequest node { read with Request = { read.Request with Source = { node with Incarnation = "retired" } } }
      |> Expect.equal "same node name is insufficient" (Error "node_mismatch")
      validateTranscriptRequest node { read with ProtocolVersion = ProtocolVersion - 1 }
      |> Expect.equal "version mismatch has no fallback" (Error "protocol_mismatch")
      for limit in [0; MaxTranscriptPageBytes + 1] do
        validateTranscriptRequest node { read with Request = { read.Request with MaxBytes = limit } }
        |> Expect.equal "exact byte bound enforced" (Error "invalid_limit")
      for cursor in [{ read.Request with Sequence = 0L }; { read.Request with Offset = -1L }; { read.Request with Journal = "" }] do
        validateTranscriptRequest node { read with Request = cursor }
        |> Expect.equal "invalid record cursor refused before storage" (Error "invalid_cursor")
    testCaseAsync "binary payloads retain boundaries across consecutive frames" <| async {
      use stream = new MemoryStream()
      let payloads = [| [|0uy|]; [| for n in 0..255 -> byte n |]; [|255uy;0uy;128uy|] |]
      for value in payloads do do! Frames.write stream value CancellationToken.None
      stream.Position <- 0L
      for expected in payloads do
        let! actual = Frames.read stream CancellationToken.None
        actual |> Expect.equal "one frame consumed exactly" (Some expected)
      let! ended = Frames.read stream CancellationToken.None
      ended |> Expect.equal "clean EOF is distinct" None
    }
    testCaseAsync "announced oversized length is refused before payload read" <| async {
      use stream = new MemoryStream([|127uy;255uy;255uy;255uy|])
      let! observed = async {
        try
          let! _ = Frames.read stream CancellationToken.None
          return false
        with :? InvalidDataException -> return true
      }
      observed |> Expect.isTrue "length cap rejects allocation"
      stream.Position |> Expect.equal "only header read" 4L
    }
    testCaseAsync "partial headers and payloads never become clean EOF" <| async {
      for bytes in [ [|0uy|]; [|0uy;0uy;0uy;2uy;99uy|] ] do
        use stream = new MemoryStream(bytes)
        let! refused = async {
          try
            let! _ = Frames.read stream CancellationToken.None
            return false
          with error when (error.GetBaseException() :? EndOfStreamException) -> return true
        }
        refused |> Expect.isTrue "truncated observation is unavailable"
    }
    testCaseAsync "cancelled reads remain cancelled" <| async {
      use stream = new MemoryStream([|0uy;0uy;0uy;1uy;8uy|])
      use cancellation = new CancellationTokenSource()
      cancellation.Cancel()
      let! cancelled = async {
        try
          let! _ = Frames.read stream cancellation.Token
          return false
        with :? OperationCanceledException -> return true
      }
      cancelled |> Expect.isTrue "no retained frame after cancellation"
    }
  ]
