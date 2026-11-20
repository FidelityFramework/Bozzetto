module Bozzetto.Tests.WorkObservationTests

open System
open System.Collections.Concurrent
open System.IO
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Bozzetto.Server.UiBridge
open Bozzetto.Server.WorkObservation
open Bozzetto.Web.Shared.Protocol

module Codec = Bozzetto.Web.Shared.BinaryCodec
module Hosting = UiBridgeTests.Hosting

let private node = { Node = "one"; Incarnation = "process-1" }
let private request topic seconds = { ProtocolVersion = ProtocolVersion; Node = "one"; Topic = topic; Seconds = seconds }
let private worker id endpoint token =
  sprintf "{\"id\":\"%s\",\"base_url\":\"%s\",\"token_file\":\"%s\"}" id endpoint token
let private config rows = "{\"workers\":[" + String.concat "," rows + "]}"

let private transportedCapacity : NodeCapacity =
  { LeaseCap = Some 1; PidVerifiedLeases = 1; UnverifiedLeases = 1; StalePidLeases = 1
    MemoryPressure = "Tight"; MachineAvailableBytes = 8_000_000_000L; MachineTotalBytes = 64_000_000_000L
    ResidentBytes = 140_000_000L; PeakResidentBytes = 350_000_000L; CheckoutSynced = Some true }

let private work : WorkSnapshot =
  let grants =
    [| for i in 1 .. 3 ->
         { Id = $"lease-{i}"; Holder = $"agent-{i}"; Kind = "full_build"; GrantedAtMs = 1L; ExpiresAtMs = 600_000L } |]
  { Sequence = 1L; SampledAtMs = 1000L; Snapshot = { Revision = 1L; Worker = Idle; Sessions = [||] }
    Leases = { Revision = 1L; Active = grants; Queued = [||] }
    Operations = { Revision = 1L; Incarnation = "journal"; StorageError = None; Entries = [||] }
    TranscriptsOmitted = true; Capacity = Some transportedCapacity }

let private constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 99 }

let private owner (source: NodeIdentity) : Facts.ObservationProducer =
  { Source = source; Correlation = Guid.NewGuid() }

let private observePeer map (completed: NodeWork -> 'a option) = task {
  use stopping = new CancellationTokenSource()
  let local = owner { Node = "controller"; Incarnation = "controller-1" }
  let graph = Facts.Graph(constants, "controller-1", "journal", observationOwners = [local], localNode = local.Source.Node, peerNodes = [node.Node])
  let result = TaskCompletionSource<'a>(TaskCreationOptions.RunContinuationsAsynchronously)
  let _, subscription = graph.Register(fun _ event ->
    match event with
    | WorkNodes board ->
      board.Nodes
      |> Array.tryFind (fun value -> value.Source.Node = node.Node)
      |> Option.bind completed
      |> Option.iter (fun value -> result.TrySetResult value |> ignore)
    | _ -> ())
  use _subscription = subscription
  let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../test-results/scratch/work-observation", Guid.NewGuid().ToString("N")))
  Directory.CreateDirectory directory |> ignore
  try
    let tokenFile = Path.Combine(directory, "peer.token")
    File.WriteAllText(tokenFile, "fixture-token")
    let! server, url = Hosting.start map
    use _server = server :> IAsyncDisposable
    let hub = UiBridgeTests.observationHub graph local stopping.Token None
    let controller = new Controller(hub, [ { Node = node.Node; Endpoint = Uri(url + Route); TokenFile = tokenFile } ], stopping.Token)
    let! outcome = task {
      try
        let! value = result.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        return Ok value
      with error -> return Error error
    }
    stopping.Cancel()
    do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
    match outcome with
    | Ok value -> return value
    | Error error -> return raise error
  finally
    graph.Stop()
    Directory.Delete(directory, true)
}

let private mockPeerRaw (versions: ConcurrentQueue<int>) (reply: int -> int * byte array list) (app: WebApplication) =
  app.MapPost(Route, RequestDelegate(fun ctx -> task {
    use buffer = new MemoryStream()
    do! ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted)
    let payload = buffer.ToArray()
    match Codec.decodePeerObserveRequest payload with
    | Error reason -> failtest reason
    | Ok request ->
      versions.Enqueue(int payload[4])
      let status, frames = reply request.ProtocolVersion
      ctx.Response.StatusCode <- status
      ctx.Response.ContentType <- MediaType
      for frame in frames do
        do! Frames.write ctx.Response.Body frame ctx.RequestAborted |> Async.StartAsTask
      if status = 200 then
        try do! Task.Delay(Timeout.Infinite, ctx.RequestAborted)
        with :? OperationCanceledException -> ()
  })) |> ignore

let private mockPeer (versions: ConcurrentQueue<int>) reply (app: WebApplication) =
  mockPeerRaw versions (fun version ->
    let status, frames = reply version
    status, frames |> List.map Codec.encodePeerFrame) app

let private available (value: NodeWork) = if value.Status = "available" then Some value else None
let private capacityOf (value: NodeWork) = value.Value |> Option.bind _.Capacity
let private hasTransportedCapacity value = capacityOf value = Some transportedCapacity

[<Tests>]
let tests =
  testList "Work observation transport" [
    testTask "v14 observation route sends complete capacity including CheckoutSynced to the controller's WorkNodes" {
      let stopping = new CancellationTokenSource()
      let producer = owner node
      let graph = Facts.Graph(constants, node.Incarnation, "journal", observationOwners = [producer], localNode = node.Node)
      try
        graph.PublishWork(producer, work) |> Expect.isOk "worker capacity published before the request"
        let worker = UiBridgeTests.observationHub graph producer stopping.Token None
        let! (observed: NodeWork) = observePeer (fun app -> mapRoutes app worker) available
        observed.Source |> Expect.equal "capacity arrived from the authenticated node" node
        hasTransportedCapacity observed |> Expect.isTrue "v14 preserves cap, pressure, RAM, RSS, PID counts, and CheckoutSynced"
      finally
        stopping.Cancel()
        graph.Stop()
        stopping.Dispose()
    }
    testTask "strict exact-version parity: protocol rejection fails immediately with zero downgrade retries and surfaces version-skew diagnostic" {
      for refusal in ["binary: unsupported version"; "protocol_mismatch"; "binary: wrong message family"] do
        let versions = ConcurrentQueue<int>()
        let reply _ = 400, [PeerUnavailable refusal]
        let completed (value: NodeWork) =
          if value.Status = "available" || value.Note.Contains("version skew", StringComparison.OrdinalIgnoreCase) then
            Some (value.Status, value.Note, versions.ToArray())
          else None
        let! status, note, seen = observePeer (mockPeer versions reply) completed
        status |> Expect.equal $"HTTP 400 {refusal} marks peer unavailable" "unavailable"
        seen |> Expect.equal $"exactly one v14 attempt with zero retries on {refusal}" [|ProtocolVersion|]
        note |> Expect.stringContains $"names refusal {refusal}" refusal
        note |> Expect.stringContains "names version skew" "version skew"
    }
    testTask "strict exact-version parity: older-version peer refusal or stream frames are rejected immediately without fallback" {
      for badVersion in [ 9uy; 11uy; 12uy; 13uy; 15uy ] do
        let versions = ConcurrentQueue<int>()
        let reply _ =
          let badFrame = Codec.encodePeerFrame (PeerHello node)
          badFrame[4] <- badVersion
          200, [badFrame]
        let completed (value: NodeWork) =
          if value.Status = "available" || value.Note.Contains("version skew", StringComparison.OrdinalIgnoreCase) then
            Some (value.Status, value.Note, versions.ToArray())
          else None
        let! status, note, seen = observePeer (mockPeerRaw versions reply) completed
        status |> Expect.equal $"mismatched v{badVersion} stream marks peer unavailable" "unavailable"
        seen |> Expect.equal $"exactly one v14 attempt with zero retries against v{badVersion}" [|ProtocolVersion|]
        note |> Expect.stringContains "names protocol_mismatch" "protocol_mismatch"
    }
    testTask "peer that served v14, disconnects, and reconnects on v13 surfaces explicit protocol_mismatch version-skew note instead of generic disconnect" {
      let! () = task {
        use stopping = new CancellationTokenSource()
        let local = owner { Node = "controller"; Incarnation = "controller-1" }
        let graph = Facts.Graph(constants, "controller-1", "journal", observationOwners = [local], localNode = local.Source.Node, peerNodes = [node.Node])
        let availableSeen = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let skewSeen = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, sub = graph.Register(fun _ event ->
          match event with
          | WorkNodes board ->
            match board.Nodes |> Array.tryFind (fun value -> value.Source.Node = node.Node) with
            | Some value when value.Status = "available" -> availableSeen.TrySetResult() |> ignore
            | Some value when value.Status = "unavailable" && value.Note.Contains("protocol_mismatch", StringComparison.OrdinalIgnoreCase) ->
              skewSeen.TrySetResult value.Note |> ignore
            | _ -> ()
          | _ -> ())
        use _sub = sub
        let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../test-results/scratch/work-observation", Guid.NewGuid().ToString("N")))
        Directory.CreateDirectory directory |> ignore
        let mutable connectionCount = 0
        let mapSkewedReconnect (app: WebApplication) =
          app.MapPost(Route, RequestDelegate(fun ctx -> task {
            use buffer = new MemoryStream()
            do! ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted)
            match Codec.decodePeerObserveRequest (buffer.ToArray()) with
            | Error reason -> failtest reason
            | Ok _ ->
              let attempt = Interlocked.Increment(&connectionCount)
              ctx.Response.ContentType <- MediaType
              if attempt = 1 then
                ctx.Response.StatusCode <- 200
                do! Frames.write ctx.Response.Body (Codec.encodePeerFrame (PeerHello node)) ctx.RequestAborted |> Async.StartAsTask
                do! Frames.write ctx.Response.Body (Codec.encodePeerFrame (PeerWork work)) ctx.RequestAborted |> Async.StartAsTask
                do! Task.Delay(50, ctx.RequestAborted)
              elif attempt = 2 then
                ctx.Response.StatusCode <- 400
                do! Frames.write ctx.Response.Body (Codec.encodePeerFrame (PeerUnavailable "protocol_mismatch")) ctx.RequestAborted |> Async.StartAsTask
              else
                ctx.Response.StatusCode <- 503
          })) |> ignore
        try
          let tokenFile = Path.Combine(directory, "peer.token")
          File.WriteAllText(tokenFile, "fixture-token")
          let! server, url = Hosting.start mapSkewedReconnect
          use _server = server :> IAsyncDisposable
          let hub = UiBridgeTests.observationHub graph local stopping.Token None
          let controller = new Controller(hub, [ { Node = node.Node; Endpoint = Uri(url + Route); TokenFile = tokenFile } ], stopping.Token)
          let! outcome = task {
            try
              do! availableSeen.Task.WaitAsync(TimeSpan.FromSeconds 5.)
              let! skewNote = skewSeen.Task.WaitAsync(TimeSpan.FromSeconds 5.)
              skewNote |> Expect.stringContains "surfaces protocol_mismatch on v13 reconnect" "protocol_mismatch"
              skewNote |> Expect.stringContains "surfaces explicit version skew guidance" "version skew"
              let readReq : TranscriptReadRequest =
                { Source = node; Journal = "j"; Sequence = 1L; Stream = Stdout; Offset = 0L; MaxBytes = 1024 }
              let! disconnectedTr = controller.ReadTranscript(readReq, CancellationToken.None) |> Async.StartAsTask
              match disconnectedTr with
              | TranscriptRefused(code, msg) ->
                code |> Expect.equal "ReadTranscript preserves protocol_mismatch while peer is disconnected due to skew" "protocol_mismatch"
                msg |> Expect.stringContains "ReadTranscript preserves version skew message" "version skew"
              | other -> failtestf "expected TranscriptRefused(protocol_mismatch) after disconnect, got %A" other
              return Ok()
            with ex -> return Error ex
          }
          stopping.Cancel()
          do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
          match outcome with
          | Ok() -> ()
          | Error ex -> raise ex
        finally
          graph.Stop()
          Directory.Delete(directory, true)
      }
      ()
    }
    testTask "mapRoutes maps skewed v13 request to canonical protocol_mismatch and ReadTranscript surfaces TranscriptRefused(protocol_mismatch)" {
      let! () = task {
        use stopping = new CancellationTokenSource()
        let local = owner { Node = "controller"; Incarnation = "controller-1" }
        let graph = Facts.Graph(constants, "controller-1", "journal", observationOwners = [local], localNode = local.Source.Node, peerNodes = [node.Node])
        let availableSeen = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let skewRetiredSeen = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, sub = graph.Register(fun _ event ->
          match event with
          | WorkNodes board ->
            match board.Nodes |> Array.tryFind (fun value -> value.Source.Node = node.Node) with
            | Some value when value.Status = "available" -> availableSeen.TrySetResult() |> ignore
            | Some value when value.Status = "unavailable" && value.Note.Contains("protocol_mismatch", StringComparison.OrdinalIgnoreCase) ->
              skewRetiredSeen.TrySetResult value.Note |> ignore
            | _ -> ()
          | _ -> ())
        use _sub = sub
        let directory = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../test-results/scratch/work-observation", Guid.NewGuid().ToString("N")))
        Directory.CreateDirectory directory |> ignore
        let mutable transcriptCalls = 0
        let mapTranscriptSkew (app: WebApplication) =
          app.MapPost(Route, RequestDelegate(fun ctx -> task {
            use buffer = new MemoryStream()
            do! ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted)
            let bytes = buffer.ToArray()
            match Codec.decodePeerObserveRequest bytes with
            | Ok _ ->
              ctx.Response.StatusCode <- 200
              ctx.Response.ContentType <- MediaType
              do! Frames.write ctx.Response.Body (Codec.encodePeerFrame (PeerHello node)) ctx.RequestAborted |> Async.StartAsTask
              do! Frames.write ctx.Response.Body (Codec.encodePeerFrame (PeerWork work)) ctx.RequestAborted |> Async.StartAsTask
              try do! Task.Delay(Timeout.Infinite, ctx.RequestAborted)
              with :? OperationCanceledException -> ()
            | Error _ ->
              let n = Interlocked.Increment(&transcriptCalls)
              if n = 1 then
                ctx.Response.StatusCode <- 400
                ctx.Response.ContentType <- MediaType
                do! Frames.write ctx.Response.Body (Codec.encodePeerFrame (PeerUnavailable "binary: unsupported version")) ctx.RequestAborted |> Async.StartAsTask
              else
                ctx.Response.StatusCode <- 503
          })) |> ignore
        try
          let tokenFile = Path.Combine(directory, "peer.token")
          File.WriteAllText(tokenFile, "fixture-token")
          let! server, url = Hosting.start mapTranscriptSkew
          use _server = server :> IAsyncDisposable
          let hub = UiBridgeTests.observationHub graph local stopping.Token None
          let controller = new Controller(hub, [ { Node = node.Node; Endpoint = Uri(url + Route); TokenFile = tokenFile } ], stopping.Token)

          // Also test real mapRoutes inbound HTTP 400 refusal on skewed v13 request.
          let workerGraph = Facts.Graph(constants, node.Incarnation, "journal", observationOwners = [owner node], localNode = node.Node)
          let workerHub = UiBridgeTests.observationHub workerGraph (owner node) stopping.Token None
          let! workerServer, workerUrl = Hosting.start (fun app -> mapRoutes app workerHub)
          use _workerServer = workerServer :> IAsyncDisposable

          let! outcome = task {
            try
              do! availableSeen.Task.WaitAsync(TimeSpan.FromSeconds 5.)
              let readReq : TranscriptReadRequest =
                { Source = node; Journal = "j"; Sequence = 1L; Stream = Stdout; Offset = 0L; MaxBytes = 1024 }
              let! trOutcome = controller.ReadTranscript(readReq, CancellationToken.None) |> Async.StartAsTask
              match trOutcome with
              | TranscriptRefused(code, msg) ->
                code |> Expect.equal "ReadTranscript preserves protocol_mismatch code" "protocol_mismatch"
                msg |> Expect.stringContains "ReadTranscript preserves version-skew guidance" "version skew"
              | other -> failtestf "expected TranscriptRefused(protocol_mismatch), got %A" other

              // Recorded skew retires peer for placement AND takes precedence over live binding on subsequent reads (even if backend returns 503).
              let! retiredNote = skewRetiredSeen.Task.WaitAsync(TimeSpan.FromSeconds 5.)
              retiredNote |> Expect.stringContains "skewed transcript reply retires peer with protocol_mismatch" "protocol_mismatch"
              let! secondTrOutcome = controller.ReadTranscript(readReq, CancellationToken.None) |> Async.StartAsTask
              match secondTrOutcome with
              | TranscriptRefused(code, msg) ->
                code |> Expect.equal "subsequent ReadTranscript preserves protocol_mismatch precedence before fresh v14 hello" "protocol_mismatch"
                msg |> Expect.stringContains "subsequent ReadTranscript keeps version-skew guidance" "version skew"
              | other -> failtestf "expected TranscriptRefused(protocol_mismatch) on second call, got %A" other

              // Real mapRoutes inbound skewed v13 observe request -> HTTP 400 PeerUnavailable "protocol_mismatch"
              use http = new Net.Http.HttpClient()
              let skewedObs = Codec.encodePeerObserveRequest (request "work" 0)
              skewedObs[4] <- 13uy
              use content = new Net.Http.ByteArrayContent(skewedObs)
              content.Headers.ContentType <- Net.Http.Headers.MediaTypeHeaderValue MediaType
              use! resp = http.PostAsync(workerUrl + Route, content)
              int resp.StatusCode |> Expect.equal "skewed v13 observe request returns HTTP 400" 400
              use! stream = resp.Content.ReadAsStreamAsync()
              let! rawFrame = Frames.read stream CancellationToken.None |> Async.StartAsTask
              rawFrame |> Option.map Codec.decodePeerFrame
              |> Expect.equal "inbound skewed request maps to canonical protocol_mismatch" (Some(Ok(PeerUnavailable "protocol_mismatch")))
              return Ok()
            with ex -> return Error ex
          }
          stopping.Cancel()
          do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
          workerGraph.Stop()
          match outcome with
          | Ok() -> ()
          | Error ex -> raise ex
        finally
          graph.Stop()
          Directory.Delete(directory, true)
      }
      ()
    }
    testTask "explicit non-protocol refusals fail closed after exactly one v14 attempt" {
      for status, reason in [400, "node_mismatch"; 400, "invalid_observation_scope"; 403, "binary: unsupported version"] do
        let versions = ConcurrentQueue<int>()
        let reply _ = status, [PeerUnavailable reason]
        let completed (value: NodeWork) =
          if value.Status = "available" || value.Note = "Authenticated observation connection is unavailable." then
            Some (value.Status, versions.ToArray())
          else None
        let! verdict = observePeer (mockPeer versions reply) completed
        verdict |> Expect.equal $"HTTP {status} {reason} publishes unavailable after just v14" ("unavailable", [|ProtocolVersion|])
    }
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
    testCase "node and exact protocol version are checked before scope admission" <| fun () ->
      validateRequest node { request "work" 0 with Node = "two" }
      |> Expect.equal "cannot observe another node by alias" (Error "node_mismatch")
      validateRequest node { request "work" 0 with ProtocolVersion = ProtocolVersion }
      |> Expect.equal "exact ProtocolVersion admitted" (Ok ())
      for version in [9; 10; 11; 12; 13; 15] do
        validateRequest node { request "work" 0 with ProtocolVersion = version }
        |> Expect.equal $"mismatched peer version {version} refused" (Error "protocol_mismatch")
    testCase "transcript reads bind the node incarnation, exact protocol version, and bounded cursor" <| fun () ->
      let read = { PeerTranscriptRequest.ProtocolVersion = ProtocolVersion
                   Request = { Source = node; Journal = "journal"; Sequence = 1L; Stream = Stdout; Offset = 0L; MaxBytes = MaxTranscriptPageBytes } }
      validateTranscriptRequest node read
      |> Expect.equal "bounded read admitted on exact ProtocolVersion" (Ok ())
      for version in [9; 10; 11; 12; 13; 15] do
        validateTranscriptRequest node { read with ProtocolVersion = version }
        |> Expect.equal $"mismatched transcript version {version} refused" (Error "protocol_mismatch")
      validateTranscriptRequest node { read with Request = { read.Request with Source = { node with Incarnation = "retired" } } }
      |> Expect.equal "same node name is insufficient" (Error "node_mismatch")
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
