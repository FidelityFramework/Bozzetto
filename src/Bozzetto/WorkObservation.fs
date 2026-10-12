/// Read-only binary observation transport. Facts and demand remain owned by
/// UiBridge; this adapter authenticates configured peers and joins its I/O.
module Bozzetto.Server.WorkObservation

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Fidelity.Data.JSON
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Web.Shared.BinaryCodec

[<Literal>]
let MediaType = "application/vnd.bozzetto.observe"
[<Literal>]
let Route = "/api/work/observe"
[<Literal>]
let MaxRequestBytes = 4096
[<Literal>]
let MaxFrameBytes = 4 * 1024 * 1024

type Peer = { Node: string; Endpoint: Uri; TokenFile: string }

let validNode (node: string) =
  not (String.IsNullOrWhiteSpace node) && node.Length <= 64
  && (node |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '-' || c = '_'))

let private expandPath (path: string) =
  if path.StartsWith("~/", StringComparison.Ordinal) then
    Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, path.Substring 2)
  else path

/// Consume the native Pi configuration's existing authority, not a parallel
/// destination or credential store. Local configuration is not a wire format.
let parsePeers (localNode: string) (text: string) : Result<Peer list, string> =
  let field key = function
    | JsonValue.Object fields -> fields |> List.tryFind (fst >> (=) key) |> Option.map snd
    | _ -> None
  let textField key value = field key value |> Option.bind JsonValue.asString
  let parsePeer value =
    match textField "id" value, textField "base_url" value, textField "token_file" value with
    | Some node, Some endpoint, Some tokenFile when validNode node && node <> localNode ->
      match Uri.TryCreate(endpoint, UriKind.Absolute) with
      | true, uri when (uri.Scheme = "https" || uri.Scheme = "http") && uri.UserInfo = "" && uri.Query = "" && uri.Fragment = "" ->
        let path = expandPath tokenFile
        if not (Path.IsPathFullyQualified path) then Error "Worker token_file must be absolute."
        else
          let root = uri.AbsoluteUri.TrimEnd('/') + "/"
          Ok { Node = node; Endpoint = Uri(root + "v1/bozzetto/observe"); TokenFile = path }
      | _ -> Error "Worker base_url must be HTTP(S), without credentials, query or fragment."
    | _ -> Error "Each worker requires a distinct node id, base_url and token_file."
  match Json.parse text with
  | Error _ -> Error "Work peer configuration is not valid JSON."
  | Ok root ->
    match field "workers" root with
    | Some(JsonValue.Array workers) when workers.Length <= MaxObservedPeers ->
      let parsed = workers |> List.fold (fun acc value ->
        match acc, parsePeer value with
        | Ok peers, Ok peer -> Ok(peer :: peers)
        | Error reason, _ | _, Error reason -> Error reason) (Ok [])
      parsed |> Result.bind (fun peers ->
        if peers |> List.distinctBy _.Node |> List.length <> peers.Length then Error "Duplicate work node id."
        else Ok(List.rev peers))
    | _ -> Error "Work peer configuration requires at most 16 workers."

let loadPeers localNode path =
  if not (validNode localNode) then Error "BOZZETTO_NODE_ID must contain 1–64 ASCII letters, digits, '_' or '-'."
  elif String.IsNullOrWhiteSpace path then Ok []
  else
    let path = expandPath path
    if not (Path.IsPathFullyQualified path) then Error "BOZZETTO_WORK_PEERS_FILE must be absolute."
    else
      try
        if FileInfo(path).Length > 131072L then Error "Work peer configuration exceeds 128 KiB."
        else File.ReadAllText path |> parsePeers localNode
      with _ -> Error "Work peer configuration could not be read."

module Frames =
  /// Four-byte network-order length, checked before allocation. EOF midway
  /// through a frame is a failed observation, never an empty snapshot.
  let write (stream: Stream) (payload: byte array) (ct: CancellationToken) = async {
    if payload.Length = 0 || payload.Length > MaxFrameBytes then invalidArg "payload" "Observation frame length is invalid."
    let n = uint32 payload.Length
    let header = [| byte(n >>> 24); byte(n >>> 16); byte(n >>> 8); byte n |]
    do! stream.WriteAsync(ReadOnlyMemory header, ct).AsTask() |> Async.AwaitTask
    do! stream.WriteAsync(ReadOnlyMemory payload, ct).AsTask() |> Async.AwaitTask
    do! stream.FlushAsync ct |> Async.AwaitTask
  }

  let read (stream: Stream) (ct: CancellationToken) = async {
    let header = Array.zeroCreate<byte> 4
    let! first = stream.ReadAsync(Memory(header, 0, 1), ct).AsTask() |> Async.AwaitTask
    if first = 0 then return None
    else
      do! stream.ReadExactlyAsync(Memory(header, 1, 3), ct).AsTask() |> Async.AwaitTask
      let size = (uint32 header[0] <<< 24) ||| (uint32 header[1] <<< 16) ||| (uint32 header[2] <<< 8) ||| uint32 header[3]
      if size = 0u || size > uint32 MaxFrameBytes then return raise (InvalidDataException "Observation frame length is invalid.")
      else
        let payload = Array.zeroCreate<byte> (int size)
        do! stream.ReadExactlyAsync(Memory payload, ct).AsTask() |> Async.AwaitTask
        return Some payload
  }

let validateRequest (identity: NodeIdentity) (request: PeerObserveRequest) =
  if request.ProtocolVersion <> ProtocolVersion then Error "protocol_mismatch"
  elif request.Node <> identity.Node then Error "node_mismatch"
  else
    match request.Topic, request.Seconds with
    | "work", 0 -> Ok ()
    | "resources", seconds when seconds >= 1 && seconds <= 120 -> Ok ()
    | _ -> Error "invalid_observation_scope"

type private ReadRequest = Observe of PeerObserveRequest | Transcript of PeerTranscriptRequest

let validateTranscriptRequest (identity: NodeIdentity) (request: PeerTranscriptRequest) =
  if request.ProtocolVersion <> ProtocolVersion then Error "protocol_mismatch"
  elif request.Request.Source <> identity then Error "node_mismatch"
  elif request.Request.MaxBytes < 1 || request.Request.MaxBytes > MaxTranscriptPageBytes then Error "invalid_limit"
  elif request.Request.Offset < 0L || request.Request.Sequence < 1L || String.IsNullOrWhiteSpace request.Request.Journal then Error "invalid_cursor"
  else Ok ()

let private requestBody (ctx: HttpContext) = async {
  let buffer = Array.zeroCreate<byte> (MaxRequestBytes + 1)
  let mutable size = 0
  let mutable finished = false
  while not finished && size <= MaxRequestBytes do
    let! read = ctx.Request.Body.ReadAsync(Memory(buffer, size, buffer.Length - size), ctx.RequestAborted).AsTask() |> Async.AwaitTask
    if read = 0 then finished <- true else size <- size + read
  if size > MaxRequestBytes then return Error "request_too_large"
  else
    let bytes = buffer[..size - 1]
    match decodePeerObserveRequest bytes with
    | Ok request -> return Ok(Observe request)
    | Error _ -> return decodePeerTranscriptRequest bytes |> Result.map Transcript
}

let private sendFrame stream ct frame = Frames.write stream (encodePeerFrame frame) ct

/// This route is present on headless nodes. The daemon remains loopback-only;
/// the native Pi gateway authenticates before relaying this one fixed route.
let mapRoutes (app: WebApplication) (hub: UiBridge.Hub) =
  let mutable nextResource = 0L
  app.MapPost(Route, RequestDelegate(fun ctx ->
    Async.StartAsTask(async {
      ctx.Response.ContentType <- MediaType
      ctx.Response.Headers.CacheControl <- "no-store"
      ctx.Response.Headers["X-Accel-Buffering"] <- "no"
      let! request = async {
        if ctx.Request.ContentType <> MediaType then return Error "unsupported_content_type"
        elif ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value > int64 MaxRequestBytes then return Error "request_too_large"
        else return! requestBody ctx
      }
      let accepted = request |> Result.bind (fun value ->
        let validity =
          match value with
          | Observe request -> validateRequest hub.LocalNode request
          | Transcript request -> validateTranscriptRequest hub.LocalNode request
        validity |> Result.map (fun () -> value))
      match accepted with
      | Error reason ->
        ctx.Response.StatusCode <- 400
        do! sendFrame ctx.Response.Body ctx.RequestAborted (PeerUnavailable reason)
      | Ok(Transcript request) ->
        let reply = { PeerTranscriptReply.Source = hub.LocalNode; Outcome = hub.ReadLocalTranscript request.Request }
        do! Frames.write ctx.Response.Body (encodePeerTranscriptReply reply) ctx.RequestAborted
      | Ok(Observe request) ->
        use lifetime = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, app.Lifetime.ApplicationStopping)
        let options = BoundedChannelOptions(16, SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait)
        let channel = Channel.CreateBounded<PeerFrame>(options)
        let enqueue frame =
          if not (channel.Writer.TryWrite frame) then
            channel.Writer.TryComplete(InvalidDataException "Observation consumer is too slow.") |> ignore
            lifetime.Cancel()
        do! sendFrame ctx.Response.Body lifetime.Token (PeerHello hub.LocalNode)
        let mutable subscription: IDisposable option = None
        let mutable resource: int64 option = None
        try
          if request.Topic = "work" then
            let frameOf (state: NodeWork) =
              match state.Status, state.Value with
              | "available", Some snapshot -> PeerWork snapshot
              | _ -> PeerUnavailable state.Note
            let current, joined = hub.RegisterLocalWork(frameOf >> enqueue)
            subscription <- Some joined
            match current with
            | Some state -> do! sendFrame ctx.Response.Body lifetime.Token (frameOf state)
            | None -> do! sendFrame ctx.Response.Body lifetime.Token (PeerUnavailable "Local work has not published.")
          else
            let id = Interlocked.Decrement(&nextResource)
            resource <- Some id
            do! hub.ObserveLocalResources(id, int request.Seconds, fun value ->
              enqueue (PeerResources value)
              if value.Status = "complete" || value.Status = "stopped" || value.Status = "error" then channel.Writer.TryComplete() |> ignore) |> Async.AwaitTask
          use heartbeat = hub.RegisterHeartbeat(PeerHeartbeat >> enqueue)
          let mutable reading = true
          while reading && not lifetime.IsCancellationRequested do
            let! more = channel.Reader.WaitToReadAsync(lifetime.Token).AsTask() |> Async.AwaitTask
            if not more then reading <- false
            else
              let mutable next = Unchecked.defaultof<PeerFrame>
              while channel.Reader.TryRead(&next) do
                do! sendFrame ctx.Response.Body lifetime.Token next
        finally
          subscription |> Option.iter _.Dispose()
          // Remove only this observer. It has no compiler/work cancellation authority.
          resource |> Option.iter (fun id -> hub.RemoveLocalResources(id).GetAwaiter().GetResult())
    }, cancellationToken = CancellationToken.None) :> Task)) |> ignore

/// The node binding is established only after an authenticated binary Hello.
/// A fresh connection gets a fresh producer correlation, including reconnects
/// to the same process, so late callbacks cannot revive a lost observation.
type Controller(hub: UiBridge.Hub, peers: Peer list, stopping: CancellationToken) =
  let gate = obj ()
  let lifetime = CancellationTokenSource.CreateLinkedTokenSource stopping
  let controllerCancellation = lifetime.Token
  let handler = new SocketsHttpHandler(AllowAutoRedirect = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds 10.)
  let client = new HttpClient(handler, Timeout = Timeout.InfiniteTimeSpan)
  let mutable bindings: Map<string, UiBridge.Facts.ObservationProducer> = Map.empty
  let mutable jobs: Task<unit> list = []
  let mutable resources: Map<Guid, CancellationTokenSource * Task<unit>> = Map.empty
  let mutable closed = false
  let closedSignal = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

  let openPayload (peer: Peer) (payload: byte array) (ct: CancellationToken) = async {
    // Never put credential contents in diagnostics or URI strings.
    let token = File.ReadAllText(peer.TokenFile).Trim()
    if token.Length = 0 || token.Length > 4096 || (token |> Seq.exists Char.IsWhiteSpace) then
      return raise (InvalidDataException "Worker credential is invalid.")
    else
      use request = new HttpRequestMessage(HttpMethod.Post, peer.Endpoint)
      request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
      request.Content <- new ByteArrayContent(payload)
      request.Content.Headers.ContentType <- MediaTypeHeaderValue MediaType
      use headerDeadline = CancellationTokenSource.CreateLinkedTokenSource ct
      headerDeadline.CancelAfter(TimeSpan.FromSeconds 15.)
      let! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerDeadline.Token) |> Async.AwaitTask
      if not response.IsSuccessStatusCode || isNull response.Content.Headers.ContentType || response.Content.Headers.ContentType.MediaType <> MediaType then
        let code = int response.StatusCode
        response.Dispose()
        return raise (InvalidDataException(sprintf "Observation endpoint refused HTTP %d." code))
      else return response
  }

  let openStream peer topic seconds ct =
    openPayload peer (encodePeerObserveRequest { ProtocolVersion = ProtocolVersion; Node = peer.Node; Topic = topic; Seconds = seconds }) ct

  let nextFrame (stream: Stream) (ct: CancellationToken) = async {
    use deadline = CancellationTokenSource.CreateLinkedTokenSource ct
    deadline.CancelAfter(TimeSpan.FromSeconds 45.)
    let! bytes = Frames.read stream deadline.Token
    match bytes with
    | None -> return raise (EndOfStreamException "Observation stream closed.")
    | Some bytes ->
      match decodePeerFrame bytes with
      | Ok frame -> return frame
      | Error _ -> return raise (InvalidDataException "Observation binary frame is invalid.")
  }

  let hello peer stream ct = async {
    let! first = nextFrame stream ct
    match first with
    | PeerHello identity when identity.Node = peer.Node && not (String.IsNullOrWhiteSpace identity.Incarnation) && identity.Incarnation.Length <= 128 -> return identity
    | _ -> return raise (InvalidDataException "Observation node identity mismatch.")
  }

  let unavailable producer reason = hub.PeerUnavailable(producer, reason) |> ignore

  let watch peer = async {
    let mutable retrySeconds = 1
    while not lifetime.IsCancellationRequested do
      let mutable admitted = None
      try
        use! response = openStream peer "work" 0 controllerCancellation
        use! stream = response.Content.ReadAsStreamAsync(controllerCancellation) |> Async.AwaitTask
        let! identity = hello peer stream controllerCancellation
        let producer = hub.BindPeer identity
        admitted <- Some producer
        lock gate (fun () -> bindings <- Map.add peer.Node producer bindings)
        let mutable haveState = false
        while not lifetime.IsCancellationRequested do
          let! frame = nextFrame stream controllerCancellation
          match frame with
          | PeerWork snapshot ->
            match hub.PublishPeerWork(producer, snapshot) with
            | Ok () -> haveState <- true; retrySeconds <- 1
            | Error UiBridge.Facts.Refusal.Duplicate when haveState -> ()
            | Error _ -> raise (InvalidDataException "Observation publication was refused.")
          | PeerHeartbeat _ when haveState -> ()
          | PeerUnavailable reason ->
            unavailable producer (if reason.Length <= 512 then reason else reason.Substring(0, 512))
            haveState <- true
          | _ -> raise (InvalidDataException "Unexpected observation frame.")
      with
      | :? OperationCanceledException when lifetime.IsCancellationRequested -> ()
      | _ ->
        match admitted with
        | Some producer -> unavailable producer "Connection lost; work state is unavailable."
        | None -> hub.PeerConnectionUnavailable(peer.Node, "Authenticated observation connection is unavailable.") |> ignore
      match admitted with
      | Some producer ->
        lock gate (fun () ->
          if Map.tryFind peer.Node bindings = Some producer then bindings <- Map.remove peer.Node bindings)
        unavailable producer "Disconnected; retained work is not current."
      | None -> ()
      if not lifetime.IsCancellationRequested then
        try do! Task.Delay(retrySeconds * 1000, controllerCancellation) |> Async.AwaitTask
        with _ when lifetime.IsCancellationRequested -> ()
        retrySeconds <- min 30 (retrySeconds * 2)
  }

  do
    jobs <- peers |> List.map (fun peer -> Async.StartAsTask(watch peer, cancellationToken = CancellationToken.None))

  member _.Observe(node: string, seconds: int, finish: string -> unit) : Async<IDisposable> = async {
    let request = { NodeObservationRequest.Node = node; Seconds = seconds }
    match peers |> List.tryFind (fun peer -> peer.Node = request.Node), lock gate (fun () -> if closed then None else Map.tryFind request.Node bindings) with
    | Some peer, Some producer when request.Seconds >= 1 && request.Seconds <= 120 ->
      let id = Guid.NewGuid()
      let cancellation = CancellationTokenSource.CreateLinkedTokenSource controllerCancellation
      let run = async {
        try
          use! response = openStream peer "resources" request.Seconds cancellation.Token
          use! stream = response.Content.ReadAsStreamAsync(cancellation.Token) |> Async.AwaitTask
          let! identity = hello peer stream cancellation.Token
          if identity.Node <> producer.Source.Node || identity.Incarnation <> producer.Source.Incarnation then
            raise (InvalidDataException "Resource observation belongs to another incarnation.")
          let mutable reading = true
          while reading && not cancellation.IsCancellationRequested do
            let! frame = nextFrame stream cancellation.Token
            match frame with
            | PeerResources value ->
              let active = lock gate (fun () -> Map.containsKey id resources && not cancellation.IsCancellationRequested)
              if not active then raise (OperationCanceledException cancellation.Token)
              if value.Status = "complete" || value.Status = "stopped" || value.Status = "error" then
                reading <- false
                if lock gate (fun () -> Map.tryFind peer.Node bindings = Some producer) then finish value.Status
              else
                match hub.PublishPeerResources(producer, value) with
                | Ok () | Error UiBridge.Facts.Refusal.Duplicate -> ()
                | Error _ -> raise (InvalidDataException "Resource observation was refused.")
            | PeerHeartbeat _ -> ()
            | _ -> raise (InvalidDataException "Unexpected resource frame.")
        with
        | :? OperationCanceledException when cancellation.IsCancellationRequested -> ()
        | _ ->
          if lock gate (fun () -> Map.containsKey id resources && not cancellation.IsCancellationRequested) then
            finish "unavailable"
      }
      let completed = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      let registered = lock gate (fun () ->
        if closed then false
        else
          resources <- Map.add id (cancellation, completed.Task) resources
          true)
      if registered then
        Async.Start(async {
          try
            do! run
          finally
            lock gate (fun () -> resources <- Map.remove id resources)
            cancellation.Dispose()
            completed.TrySetResult () |> ignore
        }, cancellationToken = CancellationToken.None)
        return
          { new IDisposable with
              member _.Dispose() =
                let pending = lock gate (fun () ->
                  let value = Map.tryFind id resources
                  value |> Option.iter (fun (source, _) -> source.Cancel())
                  value)
                // Hub invokes this outside the fact gate; join before focus changes.
                pending |> Option.iter (fun (_, completion) -> completion.GetAwaiter().GetResult()) }
      else
        cancellation.Dispose()
        return { new IDisposable with member _.Dispose() = () }
    | _ ->
      return raise (InvalidOperationException "This node has no current authenticated work observation.")
  }

  member _.ReadTranscript(request: TranscriptReadRequest, caller: CancellationToken) : Async<TranscriptReadOutcome> = async {
    let current () = lock gate (fun () -> if closed then None else Map.tryFind request.Source.Node bindings)
    match peers |> List.tryFind (fun peer -> peer.Node = request.Source.Node), current () with
    | Some peer, Some producer when producer.Source = request.Source ->
      let cancellation = CancellationTokenSource.CreateLinkedTokenSource(controllerCancellation, caller)
      cancellation.CancelAfter(TimeSpan.FromSeconds 15.)
      let id = Guid.NewGuid()
      let completed = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      let admitted = lock gate (fun () ->
        if closed then false
        else resources <- Map.add id (cancellation, completed.Task) resources; true)
      if not admitted then
        cancellation.Dispose()
        return TranscriptRefused("node_unavailable", "The controller is closing.")
      else
        try
          try
            use! response = openPayload peer (encodePeerTranscriptRequest { ProtocolVersion = ProtocolVersion; Request = request }) cancellation.Token
            use! stream = response.Content.ReadAsStreamAsync(cancellation.Token) |> Async.AwaitTask
            let! frame = Frames.read stream cancellation.Token
            match frame |> Option.map decodePeerTranscriptReply with
            | Some(Ok reply) when reply.Source = request.Source && current () = Some producer ->
              match reply.Outcome with
              | TranscriptPage page when
                  page.Journal = request.Journal && page.Sequence = request.Sequence && page.Stream = request.Stream
                  && page.Offset = request.Offset && page.Bytes.Length <= request.MaxBytes
                  && page.TotalBytes >= page.Offset + int64 page.Bytes.Length
                  && page.TotalBytes <= int64 MaxTranscriptBytes
                  && (match page.NextOffset with
                      | Some next -> next = page.Offset + int64 page.Bytes.Length && next > page.Offset && next < page.TotalBytes
                      | None -> page.Offset + int64 page.Bytes.Length = page.TotalBytes) -> return reply.Outcome
              | TranscriptRefused _ -> return reply.Outcome
              | _ -> return TranscriptRefused("invalid_transcript", "The node returned a mismatched transcript page.")
            | _ -> return TranscriptRefused("node_mismatch", "The transcript connection or node incarnation changed.")
          with _ -> return TranscriptRefused("node_unavailable", "The transcript request could not be completed.")
        finally
          lock gate (fun () -> resources <- Map.remove id resources)
          cancellation.Dispose()
          completed.TrySetResult() |> ignore
    | _ -> return TranscriptRefused("node_unavailable", "Select a node with a current authenticated work connection.")
  }

  member _.Close() = async {
    let pending = lock gate (fun () ->
      if closed then None
      else
        closed <- true
        lifetime.Cancel()
        for _, (cancellation, _) in Map.toSeq resources do cancellation.Cancel()
        Some(jobs @ (resources |> Map.toList |> List.map (fun (_, (_, job)) -> job))))
    match pending with
    | None -> do! closedSignal.Task |> Async.AwaitTask
    | Some pending ->
      try
        // Joined I/O is the boundary; cancellation alone is not drain.
        for job in pending do
          try do! job |> Async.AwaitTask with :? OperationCanceledException -> ()
      finally
        client.Dispose()
        lifetime.Dispose()
        closedSignal.TrySetResult () |> ignore
  }

  interface IDisposable with
    member this.Dispose() = this.Close() |> Async.RunSynchronously
