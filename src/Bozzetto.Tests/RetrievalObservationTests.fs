// Positive, exact-reason negative, and fault-injection tests for zero-poll
// Fanion retrieval observation, branch clarity, wait-on-signal barriers,
// UiBridge.Facts.Graph propagation, DaemonStatusPayload, and HTTP routes.
module Bozzetto.Tests.RetrievalObservationTests

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Fidelity.Data.JSON
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Expecto
open Expecto.Flip
open Bozzetto.Server
open Bozzetto.Server.RetrievalObservation
open Bozzetto.Server.UiBridge
open Bozzetto.Web.Shared.Protocol

type private ChunkItem =
  | Data of byte array
  | Fault of exn

// In-memory async byte stream that delivers SSE chunks on demand and honors
// CancellationToken, clean EOF, and injected IOException drops deterministically.
type private StreamingSseStream() =
  inherit Stream()
  let channel = Channel.CreateUnbounded<ChunkItem>(UnboundedChannelOptions(SingleReader = true, SingleWriter = false))
  let mutable pending: byte array = [||]
  let mutable pendingOffset = 0

  member _.Push(text: string) =
    let bytes = Encoding.UTF8.GetBytes text
    channel.Writer.TryWrite(Data bytes) |> ignore

  member _.Complete() = channel.Writer.TryComplete() |> ignore

  member _.Fail(error: exn) =
    channel.Writer.TryWrite(Fault error) |> ignore
    channel.Writer.TryComplete() |> ignore

  override _.CanRead = true
  override _.CanSeek = false
  override _.CanWrite = false
  override _.Length = raise (NotSupportedException())
  override _.Position
    with get() = raise (NotSupportedException())
    and set _ = raise (NotSupportedException())
  override _.Flush() = ()
  override _.Seek(_, _) = raise (NotSupportedException())
  override _.SetLength _ = raise (NotSupportedException())
  override _.Write(_, _, _) = raise (NotSupportedException())
  override _.Read(_, _, _) = raise (NotSupportedException())

  override _.ReadAsync(buffer: Memory<byte>, ct: CancellationToken) : ValueTask<int> =
    let copyFromPending () =
      let avail = pending.Length - pendingOffset
      let take = min avail buffer.Length
      pending.AsMemory(pendingOffset, take).CopyTo(buffer)
      pendingOffset <- pendingOffset + take
      if pendingOffset >= pending.Length then
        pending <- [||]
        pendingOffset <- 0
      take

    if pendingOffset < pending.Length then
      ValueTask<int>(copyFromPending ())
    else
      ValueTask<int>(task {
        let mutable result = -1
        while result < 0 do
          let! hasMore = channel.Reader.WaitToReadAsync(ct).AsTask()
          if not hasMore then
            result <- 0
          else
            match channel.Reader.TryRead() with
            | true, Data bytes when bytes.Length > 0 ->
              pending <- bytes
              pendingOffset <- 0
              result <- copyFromPending ()
            | true, Data _ -> ()
            | true, Fault error -> raise error
            | false, _ -> ()
        return result
      })

type private SseTestHandler(onConnect: int -> HttpRequestMessage -> StreamingSseStream -> HttpResponseMessage) =
  inherit HttpMessageHandler()
  let mutable attempts = 0
  let streams = ConcurrentQueue<StreamingSseStream>()
  let connected = ConcurrentQueue<TaskCompletionSource<StreamingSseStream>>()

  member _.AttemptCount = Volatile.Read(&attempts)

  member _.NextStreamAsync(?timeoutMs: int) : Task<StreamingSseStream> =
    let ms = defaultArg timeoutMs 5000
    match streams.TryDequeue() with
    | true, s -> Task.FromResult s
    | false, _ ->
      let tcs = TaskCompletionSource<StreamingSseStream>(TaskCreationOptions.RunContinuationsAsynchronously)
      connected.Enqueue tcs
      match streams.TryDequeue() with
      | true, s ->
        tcs.TrySetResult s |> ignore
        tcs.Task
      | false, _ -> tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(float ms))

  override _.SendAsync(request: HttpRequestMessage, _ct: CancellationToken) =
    let attempt = Interlocked.Increment(&attempts)
    let stream = new StreamingSseStream()
    let response = onConnect attempt request stream
    let mutable delivered = false
    while not delivered do
      match connected.TryDequeue() with
      | true, waiter ->
        if waiter.TrySetResult stream then delivered <- true
      | false, _ ->
        streams.Enqueue stream
        delivered <- true
    Task.FromResult response

let private okSseResponse (stream: StreamingSseStream) =
  let response = new HttpResponseMessage(HttpStatusCode.OK)
  response.Content <- new StreamContent(stream)
  response.Content.Headers.ContentType <- MediaTypeHeaderValue("text/event-stream")
  response

let private sseEvent (eventName: string) (json: string) =
  sprintf "event: %s\ndata: %s\n\n" eventName json

let private testConfig : Config = {
  Endpoint = Uri("https://retrieval.example/v1/retrieval/observe")
  TokenFile = "/private/retrieval.token"
}

let private freshJson gen snapId pubMs (revs: (string * string option) list) =
  let revPairs =
    revs
    |> List.map (fun (k, v) ->
      let encoded = match v with Some s -> sprintf "\"%s\"" s | None -> "null"
      sprintf "\"%s\":%s" k encoded)
    |> String.concat ","
  sprintf """{"state":"fresh","generation":%d,"snapshot_id":"%s","published_at_ms":%d,"revisions":{%s}}""" gen snapId pubMs revPairs

let private indexingJson gen snapId pending stale reasons =
  let arr xs = xs |> List.map (sprintf "\"%s\"") |> String.concat ","
  sprintf """{"state":"indexing","generation":%d,"snapshot_id":"%s","changed_at_ms":1700000001000,"queued_retrievals":2,"pending_sources":[%s],"stale_sources":[%s],"reasons":[%s]}"""
    gen snapId (arr pending) (arr stale) (arr reasons)

let private crossingHeldJson gen snapId expected pending stale reasons =
  let arr xs = xs |> List.map (sprintf "\"%s\"") |> String.concat ","
  sprintf """{"state":"crossing_held","generation":%d,"snapshot_id":"%s","changed_at_ms":1700000002000,"held_until_ms":1700000032000,"queued_retrievals":4,"expected_sources":[%s],"pending_sources":[%s],"stale_sources":[%s],"reasons":[%s]}"""
    gen snapId (arr expected) (arr pending) (arr stale) (arr reasons)

let private withTempDir (action: string -> unit) =
  let root = Path.Combine(Path.GetTempPath(), "boz-retrieval-test-" + Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory root |> ignore
  try action root
  finally
    try Directory.Delete(root, true) with _ -> ()

let private waitForObservation (controller: Controller) (predicate: Observation -> bool) (timeoutMs: int) : Task<Observation> =
  task {
    let tcs = TaskCompletionSource<Observation>(TaskCreationOptions.RunContinuationsAsynchronously)
    use _sub = controller.Changed.Subscribe(fun obs -> if predicate obs then tcs.TrySetResult obs |> ignore)
    let cur = controller.Current()
    if predicate cur then
      tcs.TrySetResult cur |> ignore
    return! tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(float timeoutMs))
  }

[<Tests>]
let unitTests =
  testList "Retrieval observation config, parsing, and branch clarity" [

    testCase "parseConfig accepts valid lab.json authority and expands home token_file" <| fun () ->
      let json = """{"retrieval_endpoint":"https://duckdb-fidelity-pgq.spkez.dev/","token_file":"~/.pi/agent/lab-retrieval.token"}"""
      match parseConfig json with
      | Error reason -> failtest reason
      | Ok None -> failtest "expected configured endpoint"
      | Ok(Some cfg) ->
        cfg.Endpoint.AbsoluteUri |> Expect.equal "observe path appended cleanly" "https://duckdb-fidelity-pgq.spkez.dev/v1/retrieval/observe"
        Path.IsPathFullyQualified cfg.TokenFile |> Expect.isTrue "tilde token_file expanded to absolute path"
        cfg.TokenFile.EndsWith(".pi/agent/lab-retrieval.token", StringComparison.Ordinal)
        |> Expect.isTrue "expanded path keeps relative tail"

    testCase "parseConfig treats absent or empty retrieval_endpoint as Unconfigured" <| fun () ->
      parseConfig """{"workers":[]}""" |> Expect.equal "absent endpoint is unconfigured" (Ok None)
      parseConfig """{"retrieval_endpoint":""}""" |> Expect.equal "empty endpoint is unconfigured" (Ok None)
      parseConfig """{"retrieval_endpoint":null}""" |> Expect.equal "null endpoint is unconfigured" (Ok None)

    testCase "parseConfig rejects malformed JSON, invalid URIs, non-loopback HTTP, and relative or missing token_file with exact errors" <| fun () ->
      parseConfig "{bad json" |> Result.isError |> Expect.isTrue "malformed JSON rejected"
      parseConfig "[]" |> Result.isError |> Expect.isTrue "non-object JSON rejected"
      for badUri in [ "file:///etc/passwd"; "https://user:pass@retrieval.example"; "https://retrieval.example/?q=1"; "https://retrieval.example/#frag" ] do
        let text = sprintf """{"retrieval_endpoint":"%s","token_file":"/abs/token"}""" badUri
        match parseConfig text with
        | Error reason -> reason |> Expect.stringContains "invalid URI names HTTP(S) constraint" "HTTP(S)"
        | Ok _ -> failtestf "should reject bad URI %s" badUri
      for remoteHttp in [ "http://remote.example/v1/retrieval/observe"; "http://remote.example:8080" ] do
        let text = sprintf """{"retrieval_endpoint":"%s","token_file":"/abs/token"}""" remoteHttp
        match parseConfig text with
        | Error reason -> reason |> Expect.stringContains "non-loopback HTTP requires HTTPS" "HTTPS"
        | Ok _ -> failtestf "should reject non-loopback HTTP %s" remoteHttp
      match parseConfig """{"retrieval_endpoint":"http://127.0.0.1:8080","token_file":"/abs/token"}""" with
      | Ok(Some cfg) -> cfg.Endpoint.AbsoluteUri |> Expect.equal "loopback HTTP accepted" "http://127.0.0.1:8080/v1/retrieval/observe"
      | other -> failtestf "expected loopback HTTP to be accepted, got %A" other
      match parseConfig """{"retrieval_endpoint":"https://retrieval.example","token_file":"relative.token"}""" with
      | Error reason -> reason |> Expect.stringContains "relative token path refused" "absolute"
      | Ok _ -> failtest "should reject relative token_file"
      match parseConfig """{"retrieval_endpoint":"https://retrieval.example"}""" with
      | Error reason -> reason |> Expect.stringContains "missing token_file refused" "token_file"
      | Ok _ -> failtest "should reject missing token_file"

    testCase "loadConfig loads explicit file and refuses oversized or relative paths" <| fun () ->
      withTempDir (fun dir ->
        let cfgPath = Path.Combine(dir, "lab.json")
        File.WriteAllText(cfgPath, """{"retrieval_endpoint":"http://127.0.0.1:9000","token_file":"/tmp/tok"}""")
        match loadConfig (Some cfgPath) with
        | Ok(Some cfg) -> cfg.Endpoint.AbsoluteUri |> Expect.equal "loaded from explicit path" "http://127.0.0.1:9000/v1/retrieval/observe"
        | other -> failtestf "expected loaded config, got %A" other

        let bigPath = Path.Combine(dir, "huge.json")
        File.WriteAllText(bigPath, String(' ', int MaxConfigBytes + 64))
        match loadConfig (Some bigPath) with
        | Error reason -> reason |> Expect.stringContains "oversized config refused" "128 KiB"
        | Ok _ -> failtest "should reject oversized config"

        match loadConfig (Some "relative/lab.json") with
        | Error reason -> reason |> Expect.stringContains "relative config path refused" "absolute"
        | Ok _ -> failtest "should reject relative config path")

    testCase "branch clarity reads .git/HEAD, worktree gitdir pointers, detached HEAD, and classifies quarantined branches" <| fun () ->
      withTempDir (fun dir ->
        let parse json = match parseReadinessJson json with Ok readiness -> readiness | Error reason -> failtest reason
        let fresh = freshJson 7L "snap-7" 1700000000000L [ "clef", Some "abcdef1234567"; "FidelityFramework/psg", None ] |> parse
        let indexing = indexingJson 8L "snap-7" [ "clef" ] [ "psg" ] [] |> parse
        let held = crossingHeldJson 8L "snap-7" [ "clef" ] [] [] [] |> parse
        let branchStatus workingDir readiness lastFreshSources =
          { Serial = 1L; ObservedAtMs = 0L; Branch = readCheckoutBranch workingDir
            Readiness = readiness; LastFreshSources = lastFreshSources }
          |> toWireStatus
          |> _.BranchStatus

        // Regular repo on main
        let mainRepo = Path.Combine(dir, "clef")
        let subDir = Path.Combine(mainRepo, "src", "nested")
        Directory.CreateDirectory(Path.Combine(mainRepo, ".git")) |> ignore
        Directory.CreateDirectory subDir |> ignore
        File.WriteAllText(Path.Combine(mainRepo, ".git", "HEAD"), "ref: refs/heads/main\n")
        let mainBranch = readCheckoutBranch subDir
        (mainBranch.BranchName, mainBranch.OnMain) |> Expect.equal "nested dir resolves main" ("main", true)
        branchStatus subDir fresh [||] |> Expect.equal "managed main is indexed" "on_main_indexed"
        branchStatus subDir indexing [||] |> Expect.equal "pending source is managed" "indexing_pending"
        branchStatus subDir held [||] |> Expect.equal "expected source is managed" "indexing_pending"

        let unmanaged = Path.Combine(dir, "Bozzetto")
        Directory.CreateDirectory(Path.Combine(unmanaged, ".git")) |> ignore
        File.WriteAllText(Path.Combine(unmanaged, ".git", "HEAD"), "ref: refs/heads/main\n")
        branchStatus unmanaged fresh [| "Bozzetto" |] |> Expect.equal "fresh requires current source evidence" "unmanaged_checkout"
        branchStatus unmanaged indexing [| "clef" |] |> Expect.equal "unmanaged main while indexing" "unmanaged_checkout"
        branchStatus unmanaged held [||] |> Expect.equal "unmanaged main while crossing is held" "unmanaged_checkout"
        let empty = freshJson 8L "snap-8" 1700000001000L [] |> parse
        branchStatus unmanaged empty [||] |> Expect.equal "empty revisions cannot establish coverage" "unmanaged_checkout"

        // Regular repo on master (trunk/default branch per owner ruling)
        let masterRepo = Path.Combine(dir, "psg")
        Directory.CreateDirectory(Path.Combine(masterRepo, ".git")) |> ignore
        File.WriteAllText(Path.Combine(masterRepo, ".git", "HEAD"), "ref: refs/heads/master\n")
        let masterBranch = readCheckoutBranch masterRepo
        (masterBranch.BranchName, masterBranch.OnMain) |> Expect.equal "master trunk is treated as OnMain" ("master", true)
        branchStatus masterRepo fresh [||] |> Expect.equal "qualified source key matches root name" "on_main_indexed"
        branchStatus masterRepo indexing [||] |> Expect.equal "stale source is managed" "indexing_pending"
        branchStatus masterRepo held [| "FidelityFramework/PSG.git" |] |> Expect.equal "previously observed source is managed" "indexing_pending"

        // Non-main feature branch (quarantined)
        let featRepo = Path.Combine(dir, "feat-repo")
        Directory.CreateDirectory(Path.Combine(featRepo, ".git")) |> ignore
        File.WriteAllText(Path.Combine(featRepo, ".git", "HEAD"), "ref: refs/heads/quarantine-branch\n")
        let featBranch = readCheckoutBranch featRepo
        (featBranch.BranchName, featBranch.OnMain) |> Expect.equal "feature branch parsed" ("quarantine-branch", false)
        branchStatus featRepo fresh [||] |> Expect.equal "non-main fresh is quarantined" "quarantined_branch"
        branchStatus featRepo indexing [||] |> Expect.equal "non-main indexing is quarantined" "quarantined_branch"

        // Worktree .git file pointing via relative gitdir
        let wtDir = Path.Combine(dir, "wt-checkout")
        let wtMeta = Path.Combine(dir, "bare-meta", "worktrees", "wt1")
        Directory.CreateDirectory wtDir |> ignore
        Directory.CreateDirectory wtMeta |> ignore
        File.WriteAllText(Path.Combine(wtMeta, "HEAD"), "ref: refs/heads/feat/worktree-lane\n")
        File.WriteAllText(Path.Combine(wtDir, ".git"), "gitdir: ../bare-meta/worktrees/wt1\n")
        let wtBranch = readCheckoutBranch wtDir
        (wtBranch.BranchName, wtBranch.OnMain) |> Expect.equal "worktree gitdir resolved" ("feat/worktree-lane", false)

        // Detached HEAD
        let detRepo = Path.Combine(dir, "detached-repo")
        Directory.CreateDirectory(Path.Combine(detRepo, ".git")) |> ignore
        File.WriteAllText(Path.Combine(detRepo, ".git", "HEAD"), "0123456789abcdef0123456789abcdef01234567\n")
        let detBranch = readCheckoutBranch detRepo
        (detBranch.BranchName, detBranch.OnMain) |> Expect.equal "detached HEAD shortened" ("detached:0123456789ab", false)
        branchStatus detRepo fresh [||] |> Expect.equal "detached HEAD is quarantined" "quarantined_branch"

        // Non-git directory -> honest branch_unknown (never on_main_indexed or indexing_pending)
        let plainDir = Path.Combine(dir, "plain")
        Directory.CreateDirectory plainDir |> ignore
        resolveGitHeadDirectory plainDir |> Expect.equal "non-git dir resolves no .git HEAD dir" None
        let plainBranch = readCheckoutBranch plainDir
        (plainBranch.BranchName, plainBranch.OnMain) |> Expect.equal "non-git dir has empty branch" ("", false)
        branchStatus plainDir fresh [||] |> Expect.equal "non-git fresh is branch_unknown" "branch_unknown"
        branchStatus plainDir indexing [||] |> Expect.equal "non-git indexing is branch_unknown" "branch_unknown"
        branchStatus plainDir held [||] |> Expect.equal "non-git crossing_held is branch_unknown" "branch_unknown"
        branchStatus plainDir Readiness.Unconfigured [||] |> Expect.equal "unconfigured stays unconfigured" "unconfigured"
        branchStatus plainDir (Readiness.Unavailable "stream_disconnected") [||] |> Expect.equal "unavailable stays unavailable" "unavailable")

    testCase "parseReadinessJson parses fresh, indexing, crossing_held, and unavailable strictly and rejects malformed frames" <| fun () ->
      match parseReadinessJson (freshJson 7L "snap-7" 1700000000000L [ "FidelityFramework/Farscape", Some "abcdef1234567"; "FidelityFramework/Bozzetto", None ]) with
      | Ok(Readiness.Fresh f) ->
        (f.Generation, f.SnapshotId, f.PublishedAtMs, f.Revisions.Length) |> Expect.equal "fresh parsed" (7L, "snap-7", 1700000000000L, 2)
      | other -> failtestf "expected Fresh, got %A" other

      match parseReadinessJson (indexingJson 8L "snap-7" [ "Farscape" ] [ "Composer" ] [ "push_event" ]) with
      | Ok(Readiness.Indexing idx) ->
        (idx.Generation, idx.QueuedRetrievals, idx.PendingSources, idx.StaleSources)
        |> Expect.equal "indexing parsed" (8L, 2, [| "Farscape" |], [| "Composer" |])
      | other -> failtestf "expected Indexing, got %A" other

      match parseReadinessJson (crossingHeldJson 9L "snap-8" [ "Farscape"; "Composer" ] [ "Composer" ] [ "Farscape" ] [ "crossing_window" ]) with
      | Ok(Readiness.CrossingHeld held) ->
        (held.Generation, held.HeldUntilMs, held.QueuedRetrievals, held.ExpectedSources)
        |> Expect.equal "crossing_held parsed" (9L, Some 1700000032000L, 4, [| "Farscape"; "Composer" |])
      | other -> failtestf "expected CrossingHeld, got %A" other

      match parseReadinessJson """{"state":"unavailable","reason":"duckdb_rebuilding"}""" with
      | Ok(Readiness.Unavailable reason) -> reason |> Expect.equal "unavailable reason" "duckdb_rebuilding"
      | other -> failtestf "expected Unavailable, got %A" other

      // Strict negative controls
      for bad in [
        "not json"
        "{}"
        """{"state":"unknown_future_state"}"""
        """{"state":"fresh","generation":1,"snapshot_id":"s","published_at_ms":1}"""
        """{"state":"fresh","generation":"1","snapshot_id":"s","published_at_ms":1,"revisions":{}}"""
        """{"state":"fresh","generation":1,"snapshot_id":"s","published_at_ms":1,"revisions":{"a":123}}"""
        """{"state":"indexing","generation":1,"snapshot_id":"s","pending_sources":[1],"stale_sources":[],"reasons":[]}"""
        """{"state":"unavailable","reason":""}"""
      ] do
        parseReadinessJson bad |> Result.isError |> Expect.isTrue (sprintf "must reject invalid frame: %s" bad)

    testCase "DaemonStatusPayload.serializeWithStatus emits retrieval object with exact fields" <| fun () ->
      let status : RetrievalStatus = {
        Revision = 3L; State = "indexing"; BranchStatus = "indexing_pending"; BranchName = "main"; OnMain = true
        Generation = 12L; SnapshotId = "snap-12"; PublishedAtMs = 1700000000000L
        ChangedAtMs = Some 1700000001000L; HeldUntilMs = None; QueuedRetrievals = 2
        ExpectedSources = [||]; PendingSources = [| "Farscape" |]; StaleSources = [| "Composer" |]
        Reasons = [| "push" |]; Revisions = [||]; Note = ""
      }
      let json = Bozzetto.DaemonStatusPayload.serializeWithStatus 47749 None None [] status
      match Json.parse json with
      | Error e -> failtest e
      | Ok root ->
        match JsonValue.prop "retrieval" root with
        | None -> failtest "missing retrieval object in DaemonStatusPayload"
        | Some r ->
          JsonValue.prop "state" r |> Option.bind JsonValue.asString |> Expect.equal "state" (Some "indexing")
          JsonValue.prop "branchStatus" r |> Option.bind JsonValue.asString |> Expect.equal "branchStatus" (Some "indexing_pending")
          JsonValue.prop "branch" r |> Option.bind JsonValue.asString |> Expect.equal "branch" (Some "main")
          JsonValue.prop "onMain" r |> Option.bind JsonValue.asBool |> Expect.equal "onMain" (Some true)
          JsonValue.prop "generation" r |> Option.bind JsonValue.tryAsInt64 |> Expect.equal "generation" (Some 12L)
          JsonValue.prop "snapshotId" r |> Option.bind JsonValue.asString |> Expect.equal "snapshotId" (Some "snap-12")
          JsonValue.prop "publishedAtMs" r |> Option.bind JsonValue.tryAsInt64 |> Expect.equal "publishedAtMs" (Some 1700000000000L)
          JsonValue.prop "queuedRetrievals" r |> Option.bind JsonValue.tryAsInt64 |> Expect.equal "queuedRetrievals" (Some 2L)
          JsonValue.prop "pendingSources" r |> Option.map JsonValue.items |> Option.defaultValue [] |> List.choose JsonValue.asString
          |> Expect.equal "pendingSources" [ "Farscape" ]
          JsonValue.prop "staleSources" r |> Option.map JsonValue.items |> Option.defaultValue [] |> List.choose JsonValue.asString
          |> Expect.equal "staleSources" [ "Composer" ]
          JsonValue.prop "reasons" r |> Option.map JsonValue.items |> Option.defaultValue [] |> List.choose JsonValue.asString
          |> Expect.equal "reasons" [ "push" ]
          JsonValue.prop "note" r |> Option.bind JsonValue.asString |> Expect.equal "note" (Some "")
  ]

let private testCaseTask name (body: unit -> Task<unit>) =
  testCaseAsync name (async { do! body () |> Async.AwaitTask })

[<Tests>]
let streamingAndBarrierTests =
  testList "Retrieval observation zero-poll SSE stream, barriers, and fault injection" [

    testCaseTask "positive control: SSE Indexing -> Fresh wakes parked waiters immediately and pushes Event.Retrieval through UiBridge.Facts.Graph" <| fun () -> task {
      let mutable seenAuth = ""
      let mutable seenAccept = ""
      use handler = new SseTestHandler(fun _ req stream ->
        seenAuth <- if isNull req.Headers.Authorization then "" else req.Headers.Authorization.ToString()
        seenAccept <- req.Headers.Accept.ToString()
        okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "secret-lab-token-12345"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))
      let graph = Facts.Graph({ McpPort = 47749; Version = "test"; ProcessId = 1 }, "inc-1")
      let pushed = ResizeArray<Push * Event>()
      use _owners =
        graph.Start
          { Facts.Owners.Tick = (Event<HealthTick.Tick>()).Publish, (fun () -> None)
            Pool = (Event<Bozzetto.Features.LeaseWatch.Pool>()).Publish, (fun () -> { Serial = 1L; Active = []; Queue = [] })
            Agents = None
            Membership = None
            Journal = (Event<unit>()).Publish, (fun () -> Bozzetto.ComposerIntegration.ComposerJournal.empty "")
            Liveness = None
            Retrieval = Some(controller.Changed, controller.Current) }
      let _, sinkReg = graph.Register(fun kind ev -> lock pushed (fun () -> pushed.Add((kind, ev))))
      use _sinkReg = sinkReg

      let! stream = handler.NextStreamAsync()
      seenAuth |> Expect.equal "Bearer auth header sent" "Bearer secret-lab-token-12345"
      seenAccept |> Expect.stringContains "SSE accept header sent" "text/event-stream"

      // Send keepalive comment + Indexing frame
      stream.Push(": keepalive\n\n")
      let indexingWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Indexing _ -> true | _ -> false) 5000
      stream.Push(sseEvent "readiness" (indexingJson 5L "snap-4" [ "Farscape" ] [] [ "push" ]))
      let! _ = indexingWait

      // Park waiter while Indexing
      let waiterTask = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 5., CancellationToken.None)
      waiterTask.IsCompleted |> Expect.isFalse "waiter stays parked while indexing"
      controller.ActiveWaiterCount |> Expect.equal "one active parked waiter" 1

      // Push Fresh(gen 5) on SSE stream -> immediate event-driven wakeup!
      stream.Push(sseEvent "readiness" (freshJson 5L "snap-5" 1700000005000L [ "FidelityFramework/Farscape", Some "a1b2c3d4e5f60" ]))
      let! outcome = waiterTask.WaitAsync(TimeSpan.FromSeconds 5.)
      match outcome with
      | AwaitOutcome.Fresh obs ->
        match obs.Readiness with
        | Readiness.Fresh f -> (f.Generation, f.SnapshotId) |> Expect.equal "woken on Fresh gen 5" (5L, "snap-5")
        | other -> failtestf "expected Fresh readiness, got %A" other
      | other -> failtestf "expected Fresh outcome, got %A" other
      controller.ActiveWaiterCount |> Expect.equal "waiter unregistered upon wakeup" 0

      let retrievalPushes =
        lock pushed (fun () ->
          pushed |> Seq.choose (function Push.Retrieval, Retrieval s -> Some(s.State, s.Generation) | _ -> None) |> Seq.toList)
      retrievalPushes |> Expect.equal "UiBridge sink received Indexing then Fresh pushes" [ "indexing", 5L; "fresh", 5L ]

      do! controller.Close() |> Async.StartAsTask
      graph.Stop()
    }

    testCaseTask "positive control: minGeneration and expectedRepo+expectedSha barriers wake only on matching fresh snapshot" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))
      let! stream = handler.NextStreamAsync()

      let gen5Ready = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh f when f.Generation = 5L -> true | _ -> false) 5000
      stream.Push(sseEvent "readiness" (freshJson 5L "snap-5" 1700000005000L [ "FidelityFramework/Farscape", Some "1111111abcdef" ]))
      let! _ = gen5Ready

      // Immediate satisfaction when gen 5 matches minGeneration=5
      let! immediate = controller.AwaitFreshAsync(Some 5L, Some "Farscape", Some "1111111", TimeSpan.FromSeconds 2., CancellationToken.None)
      match immediate with
      | AwaitOutcome.Fresh _ -> ()
      | other -> failtestf "expected immediate Fresh, got %A" other

      // Park on minGeneration = 6 AND on expectedRepo + expectedSha
      let minGenWaiter = controller.AwaitFreshAsync(Some 6L, None, None, TimeSpan.FromSeconds 5., CancellationToken.None)
      let repoShaWaiter = controller.AwaitFreshAsync(None, Some "Farscape", Some "deadbeef99", TimeSpan.FromSeconds 5., CancellationToken.None)
      let anyRepoShaWaiter = controller.AwaitFreshAsync(None, None, Some "CAFEBABE1", TimeSpan.FromSeconds 5., CancellationToken.None)
      controller.ActiveWaiterCount |> Expect.equal "three waiters parked" 3

      // CrossingHeld must NOT satisfy any waiter
      let heldReady = waitForObservation controller (fun o -> match o.Readiness with Readiness.CrossingHeld _ -> true | _ -> false) 5000
      stream.Push(sseEvent "readiness" (crossingHeldJson 6L "snap-5" [ "Farscape"; "Composer" ] [ "Composer" ] [] [ "crossing_window" ]))
      let! _ = heldReady
      minGenWaiter.IsCompleted |> Expect.isFalse "crossing_held does not wake minGen waiter"
      repoShaWaiter.IsCompleted |> Expect.isFalse "crossing_held does not wake repo+sha waiter"

      // Fresh gen 6 with Composer SHA wakes minGenWaiter + anyRepoShaWaiter, NOT repoShaWaiter (Farscape still old SHA)
      stream.Push(sseEvent "readiness" (freshJson 6L "snap-6" 1700000006000L [
        "FidelityFramework/Farscape", Some "1111111abcdef"
        "FidelityFramework/Composer", Some "cafebabe12345678"
      ]))
      let! minGenOutcome = minGenWaiter.WaitAsync(TimeSpan.FromSeconds 5.)
      let! anyShaOutcome = anyRepoShaWaiter.WaitAsync(TimeSpan.FromSeconds 5.)
      match minGenOutcome, anyShaOutcome with
      | AwaitOutcome.Fresh _, AwaitOutcome.Fresh _ -> ()
      | other -> failtestf "expected Fresh outcomes, got %A" other
      repoShaWaiter.IsCompleted |> Expect.isFalse "repo+sha waiter stays parked until Farscape matches"
      controller.ActiveWaiterCount |> Expect.equal "only repo+sha waiter remains" 1

      // Fresh gen 7 with matching Farscape SHA wakes repoShaWaiter
      stream.Push(sseEvent "readiness" (freshJson 7L "snap-7" 1700000007000L [
        "FidelityFramework/Farscape", Some "DEADBEEF99112233"
        "FidelityFramework/Composer", Some "cafebabe12345678"
      ]))
      let! repoShaOutcome = repoShaWaiter.WaitAsync(TimeSpan.FromSeconds 5.)
      match repoShaOutcome with
      | AwaitOutcome.Fresh obs ->
        match obs.Readiness with
        | Readiness.Fresh f -> f.Generation |> Expect.equal "woken on gen 7" 7L
        | other -> failtestf "expected Fresh, got %A" other
      | other -> failtestf "expected Fresh, got %A" other
      controller.ActiveWaiterCount |> Expect.equal "all waiters cleared" 0
      do! controller.Close() |> Async.StartAsTask
    }

    testCaseTask "Fresh keeps a parked waiter it claimed before caller cancellation" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use caller = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig, "", CancellationToken.None, handler,
          readToken = (fun _ -> "tok"),
          beforeCompletingFreshWaiters = caller.Cancel)
      let! stream = handler.NextStreamAsync()
      let waiter = controller.AwaitFreshAsync(Some 1L, None, None, TimeSpan.FromSeconds 5., caller.Token)
      controller.ActiveWaiterCount |> Expect.equal "waiter is parked" 1
      stream.Push(sseEvent "readiness" (freshJson 1L "snap-1" 1700000001000L []))
      let! outcome = waiter.WaitAsync(TimeSpan.FromSeconds 5.)
      caller.IsCancellationRequested |> Expect.isTrue "cancellation ran before the publisher completed the waiter"
      outcome |> Expect.equal "Fresh claimed the waiter first" (AwaitOutcome.Fresh(controller.Current()))
      controller.ActiveWaiterCount |> Expect.equal "claim removed the waiter" 0
    }

    testCaseTask "exact-reason negative controls: timeout, invalid filters, and unconfigured return explicit outcomes with zero waiter leaks" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))
      let! stream = handler.NextStreamAsync()
      let idxReady = waitForObservation controller (fun o -> match o.Readiness with Readiness.Indexing _ -> true | _ -> false) 5000
      stream.Push(sseEvent "readiness" (indexingJson 2L "snap-1" [ "clef" ] [] [ "reindexing" ]))
      let! _ = idxReady

      // Timeout preserves exact current state and leaves ActiveWaiterCount = 0
      let! timedOut = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromMilliseconds 40., CancellationToken.None)
      match timedOut with
      | AwaitOutcome.Timeout obs -> stateName obs.Readiness |> Expect.equal "timeout carries exact indexing state" "indexing"
      | other -> failtestf "expected Timeout, got %A" other
      controller.ActiveWaiterCount |> Expect.equal "zero waiters after timeout" 0

      // Invalid expectedSha / expectedRepo / minGeneration / timeout
      for minGen, repo, sha, timeout, expectedSubstr in [
        None, None, Some "", TimeSpan.FromSeconds 1., "7 to 64"
        None, None, Some "abc123", TimeSpan.FromSeconds 1., "7 to 64"
        None, None, Some "zzzzzzz", TimeSpan.FromSeconds 1., "hexadecimal"
        None, Some "Farscape", None, TimeSpan.FromSeconds 1., "requires expectedSha"
        None, Some "   ", Some "abcdef1", TimeSpan.FromSeconds 1., "expectedRepo"
        Some -1L, None, None, TimeSpan.FromSeconds 1., "minGeneration"
        None, None, None, TimeSpan.FromMilliseconds -1., "timeout"
      ] do
        let! outcome = controller.AwaitFreshAsync(minGen, repo, sha, timeout, CancellationToken.None)
        match outcome with
        | AwaitOutcome.InvalidFilter reason -> reason |> Expect.stringContains "exact invalid filter reason" expectedSubstr
        | other -> failtestf "expected InvalidFilter, got %A" other
      controller.ActiveWaiterCount |> Expect.equal "invalid filters never register waiters" 0

      // Unconfigured controller returns immediately without parking
      use unconfigured = new Controller(None, "", stopping.Token)
      let! uncfgOutcome = unconfigured.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 5., CancellationToken.None)
      match uncfgOutcome with
      | AwaitOutcome.Unconfigured obs -> stateName obs.Readiness |> Expect.equal "unconfigured state" "unconfigured"
      | other -> failtestf "expected Unconfigured, got %A" other

      do! controller.Close() |> Async.StartAsTask
    }

    testCaseTask "fault injection: mid-stream disconnect clears stale Fresh to Unavailable, reconnects, and reconciles cleanly" <| fun () -> task {
      let backoffs = ConcurrentQueue<int>()
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun secs ct -> backoffs.Enqueue secs; Task.Delay(10, ct)))

      let! stream1 = handler.NextStreamAsync()
      let gen5Ready = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh f when f.Generation = 5L -> true | _ -> false) 5000
      stream1.Push(sseEvent "readiness" (freshJson 5L "snap-5" 1700000005000L [ "FidelityFramework/Farscape", Some "1111111222222" ]))
      let! _ = gen5Ready

      // Inject hard disconnect (IOException) -> must transition to Unavailable immediately!
      let disconnected = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable _ -> true | _ -> false) 5000
      stream1.Fail(IOException "simulated TCP reset")
      let! discObs = disconnected
      match discObs.Readiness with
      | Readiness.Unavailable reason -> reason |> Expect.equal "disconnect reason surfaced" "stream_disconnected"
      | other -> failtestf "expected Unavailable after disconnect, got %A" other

      // Waiter arriving while disconnected MUST park (never falsely satisfied by stale gen 5!)
      let waiter = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 5., CancellationToken.None)
      waiter.IsCompleted |> Expect.isFalse "disconnected observer must not falsely satisfy waiter"

      // Reconnect delivers Fresh gen 6 -> reconciles and wakes waiter!
      let! stream2 = handler.NextStreamAsync()
      stream2.Push(sseEvent "readiness" (freshJson 6L "snap-6" 1700000006000L [ "FidelityFramework/Farscape", Some "2222222333333" ]))
      let! reconciled = waiter.WaitAsync(TimeSpan.FromSeconds 5.)
      match reconciled with
      | AwaitOutcome.Fresh obs ->
        match obs.Readiness with
        | Readiness.Fresh f -> f.Generation |> Expect.equal "reconciled on new Fresh gen 6" 6L
        | other -> failtestf "expected Fresh, got %A" other
      | other -> failtestf "expected Fresh after reconnect, got %A" other
      (backoffs |> Seq.toList |> List.head) |> Expect.equal "first backoff starts at 1s" 1
      controller.ActiveWaiterCount |> Expect.equal "waiter cleared after reconciliation" 0
      do! controller.Close() |> Async.StartAsTask
    }

    testCaseTask "fault injection: malformed SSE JSON frame triggers Unavailable(invalid_readiness_frame) and reconnects cleanly" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))

      let! stream1 = handler.NextStreamAsync()
      let faulted = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "invalid_readiness_frame" -> true | _ -> false) 5000
      stream1.Push("event: readiness\ndata: {corrupt-json\n\n")
      let! _ = faulted

      let waiter = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 5., CancellationToken.None)
      let! stream2 = handler.NextStreamAsync()
      stream2.Push(sseEvent "readiness" (freshJson 3L "snap-3" 1700000003000L []))
      let! recovered = waiter.WaitAsync(TimeSpan.FromSeconds 5.)
      match recovered with
      | AwaitOutcome.Fresh obs ->
        match obs.Readiness with
        | Readiness.Fresh f -> f.Generation |> Expect.equal "recovered after malformed frame" 3L
        | other -> failtestf "expected Fresh, got %A" other
      | other -> failtestf "expected Fresh recovery, got %A" other
      do! controller.Close() |> Async.StartAsTask
    }

    testCaseTask "fault injection: caller cancellation unregisters only that waiter without disturbing concurrent waiters" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))
      let! stream = handler.NextStreamAsync()
      let idxReady = waitForObservation controller (fun o -> match o.Readiness with Readiness.Indexing _ -> true | _ -> false) 5000
      stream.Push(sseEvent "readiness" (indexingJson 1L "snap-0" [ "Farscape" ] [] [ "push" ]))
      let! _ = idxReady

      use caller1Cts = new CancellationTokenSource()
      let waiter1 = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 10., caller1Cts.Token)
      let waiter2 = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 10., CancellationToken.None)
      controller.ActiveWaiterCount |> Expect.equal "two waiters parked" 2

      caller1Cts.Cancel()
      let! outcome1 = waiter1.WaitAsync(TimeSpan.FromSeconds 5.)
      match outcome1 with
      | AwaitOutcome.Cancelled _ -> ()
      | other -> failtestf "expected Cancelled, got %A" other
      controller.ActiveWaiterCount |> Expect.equal "only waiter2 remains registered" 1
      waiter2.IsCompleted |> Expect.isFalse "waiter2 undisturbed by waiter1 cancellation"

      stream.Push(sseEvent "readiness" (freshJson 2L "snap-2" 1700000002000L []))
      let! outcome2 = waiter2.WaitAsync(TimeSpan.FromSeconds 5.)
      match outcome2 with
      | AwaitOutcome.Fresh _ -> ()
      | other -> failtestf "expected waiter2 Fresh, got %A" other
      controller.ActiveWaiterCount |> Expect.equal "zero waiters remain" 0
      do! controller.Close() |> Async.StartAsTask
    }

    testCaseTask "fault injection: Controller.Close joins background I/O and wakes parked waiters with Closed" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      let controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))
      let! _stream = handler.NextStreamAsync()
      let waiter = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 30., CancellationToken.None)
      controller.ActiveWaiterCount |> Expect.equal "one waiter parked before Close" 1

      do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
      let! outcome = waiter.WaitAsync(TimeSpan.FromSeconds 5.)
      match outcome with
      | AwaitOutcome.Closed _ -> ()
      | other -> failtestf "expected Closed on shutdown, got %A" other
      controller.ActiveWaiterCount |> Expect.equal "zero waiters after Close" 0

      // Idempotent second Close and post-close AwaitFreshAsync
      do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
      let! postClose = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 1., CancellationToken.None)
      match postClose with
      | AwaitOutcome.Closed _ -> ()
      | other -> failtestf "expected Closed after close, got %A" other
    }

    testCaseTask "fault injection: credential error never leaks secret content into status note" <| fun () -> task {
      let secret = "SUPER_SECRET_TOKEN_DO_NOT_LEAK"
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> secret + " with whitespace"),
          delayAsync = (fun _ ct -> Task.Delay(50, ct)))
      let! obs = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "retrieval_credential_invalid" -> true | _ -> false) 5000
      let wire = toWireStatus obs
      wire.Note.Contains secret |> Expect.isFalse "secret never appears in status note"
      wire.Note |> Expect.equal "fixed credential fault code" "retrieval_credential_invalid"
      do! controller.Close() |> Async.StartAsTask
    }

    testCaseTask "pinned fanion@1d5c8fd wire contract: verbatim captured SSE frames parse cleanly through StreamingSseStream and Controller" <| fun () -> task {
      let tempRoot = Path.Combine(Path.GetTempPath(), "boz-fanion-contract-" + Guid.NewGuid().ToString("N"))
      let checkout = Path.Combine(tempRoot, "clef")
      Directory.CreateDirectory(Path.Combine(checkout, ".git")) |> ignore
      try
        File.WriteAllText(Path.Combine(checkout, ".git", "HEAD"), "ref: refs/heads/main\n")
        use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
        use stopping = new CancellationTokenSource()
        use controller =
          Controller.CreateForTest(
            Some testConfig,
            checkout,
            stopping.Token,
            handler,
            readToken = (fun _ -> "tok"),
            delayAsync = (fun _ ct -> Task.Delay(10, ct)))
        let! stream = handler.NextStreamAsync()

        // Verbatim frames captured from retrieval-native.dll @ 1d5c8fda051ec37458ea67f22f9615f891acd6ac
        let frameFresh =
          "event: readiness\ndata: {\"state\":\"fresh\",\"generation\":7,\"snapshot_id\":\"snap-fanion-0007\",\"published_at_ms\":1763620000123,\"revisions\":{\"clef\":\"b4ef0bc1234567890abcdef1234567890abcdef1\",\"psg\":null}}\n\n"
        let frameIndexing =
          "event: readiness\ndata: {\"state\":\"indexing\",\"generation\":7,\"snapshot_id\":\"snap-fanion-0007\",\"pending_sources\":[\"clef\",\"psg\"],\"stale_sources\":[\"psg\"],\"reasons\":[\"indexing_pending\",\"observation_stale_or_unknown\"],\"changed_at_ms\":1763620005000}\n\n"
        let frameIndexingNullChanged =
          "event: readiness\ndata: {\"state\":\"indexing\",\"generation\":0,\"snapshot_id\":\"\",\"pending_sources\":[\"clef\"],\"stale_sources\":[],\"reasons\":[\"unknown_snapshot\",\"authoritative_observation_required\"],\"changed_at_ms\":null}\n\n"
        let frameUnavailable =
          "event: readiness\ndata: {\"state\":\"unavailable\",\"reason\":\"observer_lagged\"}\n\n"

        let w1 = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh f when f.Generation = 7L -> true | _ -> false) 5000
        stream.Push frameFresh
        let! obs1 = w1
        match obs1.Readiness with
        | Readiness.Fresh f ->
          f.Generation |> Expect.equal "fresh generation" 7L
          f.SnapshotId |> Expect.equal "fresh snapshot_id" "snap-fanion-0007"
          f.PublishedAtMs |> Expect.equal "fresh published_at_ms" 1763620000123L
          f.Revisions |> Expect.equal "fresh revisions sorted with null preserved" [|
            { Source = "clef"; Revision = Some "b4ef0bc1234567890abcdef1234567890abcdef1" }
            { Source = "psg"; Revision = None }
          |]
        | other -> failtestf "expected Fresh, got %A" other
        (toWireStatus obs1).BranchStatus |> Expect.equal "main fresh branchStatus" "on_main_indexed"

        for repo in [ "FidelityFramework/clef"; "  FIDELITYFRAMEWORK/CLEF.git  " ] do
          let! matched = controller.AwaitFreshAsync(None, Some repo, Some "b4ef0bc", TimeSpan.Zero, CancellationToken.None)
          matched |> Expect.equal "qualified repo immediately matches the captured bare source" (AwaitOutcome.Fresh obs1)
        let! wrongRepo =
          controller.AwaitFreshAsync(None, Some "FidelityFramework/Composer", Some "b4ef0bc", TimeSpan.Zero, CancellationToken.None)
        wrongRepo |> Expect.equal "same SHA in another repository does not satisfy the filter" (AwaitOutcome.Timeout obs1)

        let w2 = waitForObservation controller (fun o -> match o.Readiness with Readiness.Indexing i when i.Generation = 7L -> true | _ -> false) 5000
        stream.Push frameIndexing
        let! obs2 = w2
        match obs2.Readiness with
        | Readiness.Indexing i ->
          i.Generation |> Expect.equal "indexing generation" 7L
          i.SnapshotId |> Expect.equal "indexing snapshot_id" "snap-fanion-0007"
          i.PendingSources |> Expect.equal "indexing pending_sources" [| "clef"; "psg" |]
          i.StaleSources |> Expect.equal "indexing stale_sources" [| "psg" |]
          i.Reasons |> Expect.equal "indexing reasons" [| "indexing_pending"; "observation_stale_or_unknown" |]
          i.ChangedAtMs |> Expect.equal "indexing changed_at_ms" (Some 1763620005000L)
          i.QueuedRetrievals |> Expect.equal "omitted queued_retrievals defaults to 0" 0
        | other -> failtestf "expected Indexing, got %A" other
        (toWireStatus obs2).BranchStatus |> Expect.equal "main indexing branchStatus" "indexing_pending"

        let w3 = waitForObservation controller (fun o -> match o.Readiness with Readiness.Indexing i when i.Generation = 0L -> true | _ -> false) 5000
        stream.Push frameIndexingNullChanged
        let! obs3 = w3
        obs3.LastFreshSources |> Expect.equal "fresh source membership survives indexing frames" [| "clef"; "psg" |]
        match obs3.Readiness with
        | Readiness.Indexing i ->
          i.Generation |> Expect.equal "null-changed indexing generation" 0L
          i.SnapshotId |> Expect.equal "null-changed indexing snapshot_id" ""
          i.PendingSources |> Expect.equal "null-changed pending_sources" [| "clef" |]
          i.StaleSources |> Expect.equal "null-changed stale_sources" [||]
          i.Reasons |> Expect.equal "null-changed reasons" [| "unknown_snapshot"; "authoritative_observation_required" |]
          i.ChangedAtMs |> Expect.equal "null changed_at_ms maps to None" None
          i.QueuedRetrievals |> Expect.equal "omitted queued_retrievals defaults to 0" 0
        | other -> failtestf "expected Indexing gen 0, got %A" other

        let w4 = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "observer_lagged" -> true | _ -> false) 5000
        stream.Push frameUnavailable
        let! obs4 = w4
        match obs4.Readiness with
        | Readiness.Unavailable reason -> reason |> Expect.equal "unavailable reason" "observer_lagged"
        | other -> failtestf "expected Unavailable, got %A" other
        let wire4 = toWireStatus obs4
        wire4.State |> Expect.equal "wire state" "unavailable"
        wire4.Note |> Expect.equal "wire note" "observer_lagged"

        do! controller.Close() |> Async.StartAsTask
      finally
        try Directory.Delete(tempRoot, true) with _ -> ()
    }

    testCaseTask "branch clarity: non-git Controller reports branch_unknown (never on_main_indexed)" <| fun () -> task {
      let plainDir = Path.Combine(Path.GetTempPath(), "boz-nongit-" + Guid.NewGuid().ToString("N"))
      Directory.CreateDirectory plainDir |> ignore
      try
        use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
        use stopping = new CancellationTokenSource()
        use controller =
          Controller.CreateForTest(
            Some testConfig,
            plainDir,
            stopping.Token,
            handler,
            readToken = (fun _ -> "tok"),
            delayAsync = (fun _ ct -> Task.Delay(10, ct)))
        let! stream = handler.NextStreamAsync()
        let freshWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh _ -> true | _ -> false) 5000
        stream.Push(sseEvent "readiness" (freshJson 4L "snap-4" 1700000004000L []))
        let! obs = freshWait
        let wire = toWireStatus obs
        wire.BranchName |> Expect.equal "empty branch name" ""
        wire.OnMain |> Expect.isFalse "OnMain is false"
        wire.BranchStatus |> Expect.equal "non-git fresh is branch_unknown" "branch_unknown"
        do! controller.Close() |> Async.StartAsTask
      finally
        try Directory.Delete(plainDir, true) with _ -> ()
    }

    testCaseTask "event-driven .git/HEAD FileSystemWatcher pushes branch changes through Controller and Facts.Graph with zero SSE frames" <| fun () -> task {
      let root = Path.Combine(Path.GetTempPath(), "boz-head-watch-" + Guid.NewGuid().ToString("N"))
      let repoRoot = Path.Combine(root, "clef")
      let dotGit = Path.Combine(repoRoot, ".git")
      let headPath = Path.Combine(dotGit, "HEAD")
      Directory.CreateDirectory dotGit |> ignore
      try
        File.WriteAllText(headPath, "ref: refs/heads/main\n")
        use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
        use stopping = new CancellationTokenSource()
        use controller =
          Controller.CreateForTest(
            Some testConfig,
            repoRoot,
            stopping.Token,
            handler,
            readToken = (fun _ -> "tok"),
            delayAsync = (fun _ ct -> Task.Delay(10, ct)))
        let graph = Facts.Graph({ McpPort = 47749; Version = "test"; ProcessId = 1 }, "inc-head-watch")
        use _owners =
          graph.Start
            { Facts.Owners.Tick = (Event<HealthTick.Tick>()).Publish, (fun () -> None)
              Pool = (Event<Bozzetto.Features.LeaseWatch.Pool>()).Publish, (fun () -> { Serial = 1L; Active = []; Queue = [] })
              Agents = None
              Membership = None
              Journal = (Event<unit>()).Publish, (fun () -> Bozzetto.ComposerIntegration.ComposerJournal.empty "")
              Liveness = None
              Retrieval = Some(controller.Changed, controller.Current) }
        let pushed = ConcurrentQueue<RetrievalStatus>()
        let mutable nextWaiter: ((RetrievalStatus -> bool) * TaskCompletionSource<RetrievalStatus>) option = None
        let waitGraph (pred: RetrievalStatus -> bool) =
          let tcs = TaskCompletionSource<RetrievalStatus>(TaskCreationOptions.RunContinuationsAsynchronously)
          lock pushed (fun () ->
            match pushed |> Seq.tryFind pred with
            | Some existing -> tcs.TrySetResult existing |> ignore
            | None -> nextWaiter <- Some(pred, tcs))
          tcs.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        let _, sinkReg =
          graph.Register(fun kind ev ->
            match kind, ev with
            | Push.Retrieval, Retrieval status ->
              lock pushed (fun () ->
                pushed.Enqueue status
                match nextWaiter with
                | Some(pred, tcs) when pred status ->
                  nextWaiter <- None
                  tcs.TrySetResult status |> ignore
                | _ -> ())
            | _ -> ())
        use _sinkReg = sinkReg

        let! stream = handler.NextStreamAsync()
        let freshWait = waitGraph (fun s -> s.State = "fresh" && s.BranchStatus = "on_main_indexed")
        stream.Push(sseEvent "readiness" (freshJson 10L "snap-10" 1700000010000L [ "clef", Some "abcdef1234567" ]))
        let! initialFresh = freshWait
        initialFresh.BranchName |> Expect.equal "initially on main" "main"
        initialFresh.OnMain |> Expect.isTrue "initially OnMain"

        // Rewrite .git/HEAD directly without sending any SSE frame
        let quarantinedWait = waitGraph (fun s -> s.BranchStatus = "quarantined_branch" && s.BranchName = "quarantine-test")
        File.WriteAllText(headPath, "ref: refs/heads/quarantine-test\n")
        let! qStatus = quarantinedWait
        qStatus.State |> Expect.equal "readiness preserved across HEAD switch" "fresh"
        qStatus.Generation |> Expect.equal "generation preserved across HEAD switch" 10L
        qStatus.OnMain |> Expect.isFalse "quarantined branch has OnMain=false"

        // Atomic HEAD.lock -> HEAD rename back to main (git checkout style) without SSE frame
        let backMainWait = waitGraph (fun s -> s.Revision > qStatus.Revision && s.BranchStatus = "on_main_indexed" && s.BranchName = "main")
        let lockPath = Path.Combine(dotGit, "HEAD.lock")
        File.WriteAllText(lockPath, "ref: refs/heads/main\n")
        File.Move(lockPath, headPath, true)
        let! backStatus = backMainWait
        backStatus.OnMain |> Expect.isTrue "atomic rename back to main restores OnMain"
        backStatus.Generation |> Expect.equal "generation still preserved" 10L

        do! controller.Close() |> Async.StartAsTask
        graph.Stop()
      finally
        try Directory.Delete(root, true) with _ -> ()
    }

    testCaseTask "SetWorkingDirectory switches between main, side-branch, and non-git checkouts idempotently and cleans old watchers" <| fun () -> task {
      let root = Path.Combine(Path.GetTempPath(), "boz-switch-dir-" + Guid.NewGuid().ToString("N"))
      let mainRepo = Path.Combine(root, "main-repo")
      let sideRepo = Path.Combine(root, "side-repo")
      let plainDir = Path.Combine(root, "plain-dir")
      Directory.CreateDirectory(Path.Combine(mainRepo, ".git")) |> ignore
      Directory.CreateDirectory(Path.Combine(sideRepo, ".git")) |> ignore
      Directory.CreateDirectory plainDir |> ignore
      try
        File.WriteAllText(Path.Combine(mainRepo, ".git", "HEAD"), "ref: refs/heads/main\n")
        File.WriteAllText(Path.Combine(sideRepo, ".git", "HEAD"), "ref: refs/heads/feat/side-branch\n")
        use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
        use stopping = new CancellationTokenSource()
        use controller =
          Controller.CreateForTest(
            Some testConfig,
            mainRepo,
            stopping.Token,
            handler,
            readToken = (fun _ -> "tok"),
            delayAsync = (fun _ ct -> Task.Delay(10, ct)))
        let! stream = handler.NextStreamAsync()
        let freshWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh f when f.Generation = 4L -> true | _ -> false) 5000
        stream.Push(sseEvent "readiness" (freshJson 4L "snap-4" 1700000004000L [ "main-repo", Some "abcdef1234567"; "side-repo", Some "abcdef1234567" ]))
        let! mainObs = freshWait
        (toWireStatus mainObs).BranchStatus |> Expect.equal "initial main checkout" "on_main_indexed"

        // Switch to side-branch checkout (simulates opening Composer session on side branch)
        let sideWait = waitForObservation controller (fun o -> o.Branch.BranchName = "feat/side-branch") 5000
        controller.SetWorkingDirectory(Some sideRepo)
        let! sideObs = sideWait
        let sideWire = toWireStatus sideObs
        sideWire.BranchStatus |> Expect.equal "side repo is quarantined" "quarantined_branch"
        sideWire.OnMain |> Expect.isFalse "side repo OnMain=false"
        sideWire.State |> Expect.equal "readiness stays fresh" "fresh"

        // Idempotent repeat does not emit a new serial
        let serialBeforeRepeat = controller.Current().Serial
        controller.SetWorkingDirectory(Some sideRepo)
        controller.Current().Serial |> Expect.equal "idempotent SetWorkingDirectory does not bump serial" serialBeforeRepeat

        // Rewrite mainRepo HEAD while watching sideRepo -> controller stays on side-branch
        File.WriteAllText(Path.Combine(mainRepo, ".git", "HEAD"), "ref: refs/heads/stale-old-repo\n")
        controller.Current().Branch.BranchName |> Expect.equal "old watcher detached" "feat/side-branch"

        // Rewriting sideRepo HEAD to master is watched live and treated as trunk (OnMain = true)
        let masterWait = waitForObservation controller (fun o -> o.Branch.BranchName = "master" && o.Branch.OnMain) 5000
        File.WriteAllText(Path.Combine(sideRepo, ".git", "HEAD"), "ref: refs/heads/master\n")
        let! masterObs = masterWait
        (toWireStatus masterObs).BranchStatus |> Expect.equal "master trunk is on_main_indexed" "on_main_indexed"

        // Switch to non-git directory -> branch_unknown
        let plainWait = waitForObservation controller (fun o -> o.Branch.BranchName = "" && not o.Branch.OnMain) 5000
        controller.SetWorkingDirectory(Some plainDir)
        let! plainObs = plainWait
        (toWireStatus plainObs).BranchStatus |> Expect.equal "plain dir is branch_unknown" "branch_unknown"

        // Switch back to mainRepo (now on stale-old-repo) -> quarantined_branch
        let backWait = waitForObservation controller (fun o -> o.Branch.BranchName = "stale-old-repo") 5000
        controller.SetWorkingDirectory(Some mainRepo)
        let! backObs = backWait
        (toWireStatus backObs).BranchStatus |> Expect.equal "re-attaching reads current HEAD" "quarantined_branch"

        do! controller.Close() |> Async.StartAsTask
      finally
        try Directory.Delete(root, true) with _ -> ()
    }

    testCase "overlapping checkout refreshes serialize resolution through the branch commit" <| fun () ->
      withTempDir (fun root ->
        let writeHead repo branch =
          let git = Path.Combine(repo, ".git")
          Directory.CreateDirectory git |> ignore
          File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/" + branch + "\n")
        let earlier = Path.Combine(root, "earlier")
        let later = Path.Combine(root, "later")
        writeHead earlier "old-branch"
        writeHead later "new-branch"
        use controller = new Controller(None, root, CancellationToken.None)
        use firstResolving = new ManualResetEventSlim(false)
        use releaseFirst = new ManualResetEventSlim(false)
        use secondStarted = new ManualResetEventSlim(false)
        use secondResolved = new ManualResetEventSlim(false)
        let bound = TimeSpan.FromSeconds 5.
        let start action =
          Task.Factory.StartNew(Action action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
        let first = start (fun () ->
          controller.RefreshCheckout(fun () ->
            firstResolving.Set()
            if not (releaseFirst.Wait bound) then failtest "first resolver was not released"
            Some earlier))
        try
          firstResolving.Wait bound |> Expect.isTrue "first callback entered the resolver"
          let second = start (fun () ->
            secondStarted.Set()
            controller.RefreshCheckout(fun () ->
              secondResolved.Set()
              controller.Current().Branch.BranchName |> Expect.equal "first checkout committed before the next resolution" "old-branch"
              Some later))
          let resolvedEarly =
            try
              secondStarted.Wait bound |> Expect.isTrue "second callback arrived while the first was still resolving"
              secondResolved.Wait(TimeSpan.FromMilliseconds 250.)
            finally
              releaseFirst.Set()
          Task.WaitAll([| first; second |], bound) |> Expect.isTrue "both refreshes complete"
          resolvedEarly |> Expect.isFalse "second callback could not sample checkout state ahead of the first commit"
          controller.Current().Branch.BranchName |> Expect.equal "latest selection wins" "new-branch"
        finally
          releaseFirst.Set())

    testCaseTask "fault injection: throwing Changed subscriber never kills watch loop, blocks siblings, or breaks Close" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))
      use _badSub = controller.Changed.Subscribe(fun _ -> invalidOp "simulated subscriber throw")

      let! stream1 = handler.NextStreamAsync()
      let idxWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Indexing _ -> true | _ -> false) 5000
      stream1.Push(sseEvent "readiness" (indexingJson 1L "snap-1" [ "clef" ] [] [ "push" ]))
      let! _ = idxWait

      // Trigger fault path (throwing subscriber also runs during Unavailable publish!)
      let unavailWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "stream_disconnected" -> true | _ -> false) 5000
      stream1.Fail(IOException "drop while bad subscriber attached")
      let! _ = unavailWait

      let waiter = controller.AwaitFreshAsync(None, None, None, TimeSpan.FromSeconds 5., CancellationToken.None)
      let! stream2 = handler.NextStreamAsync()
      stream2.Push(sseEvent "readiness" (freshJson 2L "snap-2" 1700000002000L []))
      let! outcome = waiter.WaitAsync(TimeSpan.FromSeconds 5.)
      match outcome with
      | AwaitOutcome.Fresh obs ->
        match obs.Readiness with
        | Readiness.Fresh f -> f.Generation |> Expect.equal "watch loop survived throwing subscriber" 2L
        | other -> failtestf "expected Fresh, got %A" other
      | other -> failtestf "expected Fresh outcome, got %A" other

      do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
    }

    testCaseTask "SSE framing: CRLF split across chunks, multi-line data concatenation, and UTF-8 MaxEventBytes enforcement" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct -> Task.Delay(10, ct)))

      let! stream1 = handler.NextStreamAsync()
      let multiWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh f when f.Generation = 9L -> true | _ -> false) 5000
      // Split CRLF across chunks AND concatenate multi-line data:
      stream1.Push("event: readiness\r")
      stream1.Push("\ndata: {\r")
      stream1.Push("\ndata:   \"state\":\"fresh\",\r\n")
      stream1.Push("data:   \"generation\":9,\n")
      stream1.Push("data:   \"snapshot_id\":\"snap-crlf\",\r")
      stream1.Push("\ndata:   \"published_at_ms\":1700000009000,\r\ndata:   \"revisions\":{\"clef\":\"abcdef1234567\"}\r\n")
      stream1.Push("data: }\r")
      stream1.Push("\n\r")
      stream1.Push("\n")
      let! multiObs = multiWait
      match multiObs.Readiness with
      | Readiness.Fresh f ->
        f.Generation |> Expect.equal "multi-line CRLF fresh generation" 9L
        f.SnapshotId |> Expect.equal "multi-line CRLF snapshotId" "snap-crlf"
      | other -> failtestf "expected Fresh gen 9, got %A" other

      // Multi-byte UTF-8 single line: 262,144 '€' chars = 262,144 UTF-16 chars (< MaxEventBytes),
      // but 786,432 UTF-8 bytes (> MaxEventBytes) -> must fail with invalid_readiness_frame!
      let fault1 = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "invalid_readiness_frame" -> true | _ -> false) 5000
      let euroBurst = String('\u20AC', MaxEventBytes / 2)
      stream1.Push("event: readiness\ndata: " + euroBurst + "\n\n")
      let! _ = fault1

      // Multi-line UTF-8 dataLines total > MaxEventBytes UTF-8 bytes (each line < MaxEventBytes):
      let! stream2 = handler.NextStreamAsync()
      let freshResetWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh f when f.Generation = 10L -> true | _ -> false) 5000
      stream2.Push(sseEvent "readiness" (freshJson 10L "snap-10" 1700000010000L []))
      let! _ = freshResetWait

      let fault2 = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "invalid_readiness_frame" -> true | _ -> false) 5000
      let chunkEacute = String('\u00E9', 160_000) // 160,000 chars = 320,000 UTF-8 bytes per line; 2 lines = 640,002 UTF-8 bytes > 524,288
      stream2.Push("event: readiness\ndata: " + chunkEacute + "\ndata: " + chunkEacute + "\n\n")
      let! _ = fault2

      do! controller.Close() |> Async.StartAsTask
    }

    testCaseTask "fault injection: non-2xx HTTP responses (401/503) and wrong Content-Type (application/json) surface explicit faults and recover" <| fun () -> task {
      let gateLock = obj ()
      let mutable currentGate = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      let advanceAttempt () =
        let prev =
          lock gateLock (fun () ->
            let p = currentGate
            currentGate <- TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
            p)
        prev.TrySetResult() |> ignore
      let stream4Tcs = TaskCompletionSource<StreamingSseStream>(TaskCreationOptions.RunContinuationsAsynchronously)
      use handler =
        new SseTestHandler(fun n _ stream ->
          match n with
          | 1 -> new HttpResponseMessage(HttpStatusCode.Unauthorized)
          | 2 -> new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
          | 3 ->
            let resp = new HttpResponseMessage(HttpStatusCode.OK)
            resp.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
            resp
          | _ ->
            stream4Tcs.TrySetResult stream |> ignore
            okSseResponse stream)
      use stopping = new CancellationTokenSource()
      use controller =
        Controller.CreateForTest(
          Some testConfig,
          "",
          stopping.Token,
          handler,
          readToken = (fun _ -> "tok"),
          delayAsync = (fun _ ct ->
            let g = lock gateLock (fun () -> currentGate.Task)
            g.WaitAsync(ct)))

      let! obs401 = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "retrieval_endpoint_http_401" -> true | _ -> false) 5000
      (toWireStatus obs401).Note |> Expect.equal "401 reason" "retrieval_endpoint_http_401"

      let obs503Wait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "retrieval_endpoint_http_503" -> true | _ -> false) 5000
      advanceAttempt ()
      let! obs503 = obs503Wait
      (toWireStatus obs503).Note |> Expect.equal "503 reason" "retrieval_endpoint_http_503"

      let obsWrongCtWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Unavailable r when r = "retrieval_endpoint_invalid_content_type" -> true | _ -> false) 5000
      advanceAttempt ()
      let! obsWrongCt = obsWrongCtWait
      (toWireStatus obsWrongCt).Note |> Expect.equal "wrong Content-Type reason" "retrieval_endpoint_invalid_content_type"

      advanceAttempt ()
      let! stream4 = stream4Tcs.Task.WaitAsync(TimeSpan.FromSeconds 5.)
      let freshWait = waitForObservation controller (fun o -> match o.Readiness with Readiness.Fresh f when f.Generation = 15L -> true | _ -> false) 5000
      stream4.Push(sseEvent "readiness" (freshJson 15L "snap-15" 1700000015000L []))
      let! recovered = freshWait
      match recovered.Readiness with
      | Readiness.Fresh f -> f.Generation |> Expect.equal "recovered after HTTP/content-type faults" 15L
      | other -> failtestf "expected Fresh, got %A" other
      do! controller.Close() |> Async.StartAsTask
    }
  ]

[<Tests>]
let httpRouteTests =
  testSequenced <| testList "Retrieval observation ambient dispatch and local HTTP routes" [
    testCaseTask "fault injection: a throwing ambient subscriber cannot block any transition for the next subscriber" <| fun () -> task {
      use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
      use controller = Controller.CreateForTest(Some testConfig, "", CancellationToken.None, handler, readToken = (fun _ -> "tok"))
      let attempted = ConcurrentQueue<int64>()
      let received = ConcurrentQueue<Observation>()
      let fresh = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      use _throwing =
        RetrievalObservation.Changed.Subscribe(fun obs ->
          attempted.Enqueue obs.Serial
          invalidOp "simulated ambient subscriber failure")
      use _healthy =
        RetrievalObservation.Changed.Subscribe(fun obs ->
          received.Enqueue obs
          match obs.Readiness with
          | Readiness.Fresh _ -> fresh.TrySetResult() |> ignore
          | _ -> ())
      use _installed = RetrievalObservation.install controller
      let! stream = handler.NextStreamAsync()
      stream.Push(sseEvent "readiness" (indexingJson 1L "snap-0" [ "clef" ] [] [ "push" ]))
      stream.Push(sseEvent "readiness" """{"state":"unavailable","reason":"observer_lagged"}""")
      stream.Push(sseEvent "readiness" (freshJson 1L "snap-1" 1700000001000L [ "clef", Some "abcdef1234567" ]))
      do! fresh.Task.WaitAsync(TimeSpan.FromSeconds 5.)
      let observations = received.ToArray()
      observations |> Array.map (fun obs -> stateName obs.Readiness)
      |> Expect.equal "healthy subscriber received initial state and each pushed transition" [| "unavailable"; "indexing"; "unavailable"; "fresh" |]
      observations |> Array.map _.Serial |> Expect.equal "throwing subscriber was invoked before each healthy delivery" (attempted.ToArray())
    }

    testCaseTask "GET /api/retrieval/status and POST /api/retrieval/await enforce origin guard, status codes, and branch clarity" <| fun () -> task {
      RetrievalObservation.reset ()
      let builder = WebApplication.CreateBuilder()
      builder.Logging.ClearProviders() |> ignore
      builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
      let app = builder.Build()
      let ownOrigins = HttpOriginGuard.OwnOrigins.ofPorts [ 47749; 47750 ]
      McpServer.useOriginGuard ownOrigins app
      McpServer.mapRetrievalRoutes app
      do! app.StartAsync()
      let! runResult =
        task {
          try
            let address =
              app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses
              |> Seq.head
            use http = new HttpClient(BaseAddress = Uri address)

            // 1. Unconfigured returns 200 on status and 503 on await
            let! statusResp = http.GetAsync("/api/retrieval/status")
            statusResp.StatusCode |> Expect.equal "status returns 200" HttpStatusCode.OK
            let! statusBody = statusResp.Content.ReadAsStringAsync()
            statusBody |> Expect.stringContains "unconfigured state" "\"state\":\"unconfigured\""

            let! uncfgAwait = http.PostAsync("/api/retrieval/await", new StringContent("{}", Encoding.UTF8, "application/json"))
            uncfgAwait.StatusCode |> Expect.equal "unconfigured await returns 503" HttpStatusCode.ServiceUnavailable

            // 2. Origin guard rejects foreign browser origin on POST /api/retrieval/await
            use foreignReq = new HttpRequestMessage(HttpMethod.Post, "/api/retrieval/await")
            foreignReq.Headers.Add("Origin", "https://evil.example")
            foreignReq.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
            let! foreignResp = http.SendAsync(foreignReq)
            foreignResp.StatusCode |> Expect.equal "foreign origin rejected with 403" HttpStatusCode.Forbidden

            // 3. Invalid params return 400
            let! badShaResp = http.PostAsync("/api/retrieval/await", new StringContent("""{"expectedSha":"short"}""", Encoding.UTF8, "application/json"))
            badShaResp.StatusCode |> Expect.equal "invalid expectedSha returns 400" HttpStatusCode.BadRequest
            let! badJsonResp = http.PostAsync("/api/retrieval/await", new StringContent("{bad", Encoding.UTF8, "application/json"))
            badJsonResp.StatusCode |> Expect.equal "malformed JSON returns 400" HttpStatusCode.BadRequest

            // 4. Live SSE controller installed -> 408 on timeout, then 200 on Fresh with quarantined_branch clarity
            use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
            use stopping = new CancellationTokenSource()
            use controller =
              Controller.CreateForTest(
                Some testConfig,
                "",
                stopping.Token,
                handler,
                readToken = (fun _ -> "tok"),
                delayAsync = (fun _ ct -> Task.Delay(10, ct)))
            use _installed = RetrievalObservation.install controller
            let! stream = handler.NextStreamAsync()
            let idxReady = waitForObservation controller (fun o -> match o.Readiness with Readiness.Indexing _ -> true | _ -> false) 5000
            stream.Push(sseEvent "readiness" (indexingJson 10L "snap-9" [ "Farscape" ] [] [ "webhook" ]))
            let! _ = idxReady

            let! timeoutResp = http.PostAsync("/api/retrieval/await", new StringContent("""{"timeoutSeconds":1}""", Encoding.UTF8, "application/json"))
            timeoutResp.StatusCode |> Expect.equal "timeout returns 408" HttpStatusCode.RequestTimeout

            let tempRepo = Path.Combine(Path.GetTempPath(), "boz-http-branch-" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(Path.Combine(tempRepo, ".git")) |> ignore
            try
              File.WriteAllText(Path.Combine(tempRepo, ".git", "HEAD"), "ref: refs/heads/feat/quarantined-check\n")
              let escapedDir = tempRepo.Replace("\\", "\\\\")
              let waiterParked = controller.WaitForActiveWaitersAsync(1, 5000)
              let awaitReq = http.PostAsync("/api/retrieval/await", new StringContent(sprintf """{"minGeneration":11,"timeoutSeconds":5,"workingDirectory":"%s"}""" escapedDir, Encoding.UTF8, "application/json"))
              do! waiterParked
              controller.ActiveWaiterCount |> Expect.equal "HTTP request parked on controller waiter table" 1
              awaitReq.IsCompleted |> Expect.isFalse "HTTP POST stays parked until Fresh SSE arrives"
              // Push Fresh gen 11 while HTTP request is genuinely parked
              stream.Push(sseEvent "readiness" (freshJson 11L "snap-11" 1700000011000L [ "FidelityFramework/Farscape", Some "abcdef1234567" ]))
              let! okResp = awaitReq.WaitAsync(TimeSpan.FromSeconds 5.)
              okResp.StatusCode |> Expect.equal "fresh wakeup returns 200" HttpStatusCode.OK
              let! okBody = okResp.Content.ReadAsStringAsync()
              okBody |> Expect.stringContains "fresh outcome" "\"outcome\":\"fresh\""
              okBody |> Expect.stringContains "quarantined branch status reported" "\"branchStatus\":\"quarantined_branch\""
              okBody |> Expect.stringContains "quarantined warning flag" "\"quarantined\":true"

              let! wdStatusResp = http.GetAsync("/api/retrieval/status?workingDirectory=" + Uri.EscapeDataString tempRepo)
              wdStatusResp.StatusCode |> Expect.equal "workingDirectory status returns 200" HttpStatusCode.OK
              let! wdStatusBody = wdStatusResp.Content.ReadAsStringAsync()
              wdStatusBody |> Expect.stringContains "status reflects live fresh state" "\"state\":\"fresh\""
              wdStatusBody |> Expect.stringContains "status reflects quarantined branch" "\"branchStatus\":\"quarantined_branch\""
              wdStatusBody |> Expect.stringContains "status names branch" "\"branch\":\"feat/quarantined-check\""
            finally
              try Directory.Delete(tempRepo, true) with _ -> ()
            do! controller.Close() |> Async.StartAsTask
            return Ok ()
          with ex -> return Error ex
        }
      RetrievalObservation.reset ()
      do! app.StopAsync()
      do! app.DisposeAsync().AsTask()
      match runResult with
      | Ok () -> ()
      | Error ex -> raise ex
    }

    testCaseTask "checkout ref watcher catches packed-to-loose transition when refs/ is absent at startup and subsequent loose updates" <| fun () -> task {
      let shaA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
      let shaB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
      let shaC = "cccccccccccccccccccccccccccccccccccccccc"
      let repoDir = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../test-results/scratch", "boz-packed-to-loose-" + Guid.NewGuid().ToString("N")))
      Directory.CreateDirectory(Path.Combine(repoDir, ".git")) |> ignore
      File.WriteAllText(Path.Combine(repoDir, ".git", "HEAD"), "ref: refs/heads/main\n")
      File.WriteAllText(Path.Combine(repoDir, ".git", "packed-refs"), "# pack-refs\n" + shaB + " refs/heads/main\n")
      try
        use handler = new SseTestHandler(fun _ _ stream -> okSseResponse stream)
        use stopping = new CancellationTokenSource()
        use controller =
          Controller.CreateForTest(
            Some testConfig,
            repoDir,
            stopping.Token,
            handler,
            readToken = (fun _ -> "tok"),
            delayAsync = (fun _ ct -> Task.Delay(10, ct)))
        let! outcome = task {
          try
            (controller.Current()).Branch.HeadRevision
            |> Expect.equal "starts on packed-refs SHA when refs/ does not yet exist" (Some shaB)

            let sawLooseA = waitForObservation controller (fun o -> o.Branch.HeadRevision = Some shaA) 5000
            let headsDir = Path.Combine(repoDir, ".git", "refs", "heads")
            Directory.CreateDirectory headsDir |> ignore
            File.WriteAllText(Path.Combine(headsDir, "main"), shaA + "\n")
            let! obsA = sawLooseA
            obsA.Branch.HeadRevision |> Expect.equal "packed-to-loose creation pushes new HeadRevision" (Some shaA)

            let sawLooseC = waitForObservation controller (fun o -> o.Branch.HeadRevision = Some shaC) 5000
            File.WriteAllText(Path.Combine(headsDir, "main"), shaC + "\n")
            let! obsC = sawLooseC
            obsC.Branch.HeadRevision |> Expect.equal "newly attached refs/ watcher pushes subsequent loose ref update" (Some shaC)
            return Ok ()
          with ex -> return Error ex
        }
        stopping.Cancel()
        do! (controller.Close() |> Async.StartAsTask).WaitAsync(TimeSpan.FromSeconds 5.)
        match outcome with
        | Ok () -> ()
        | Error ex -> raise ex
      finally
        if Directory.Exists repoDir then Directory.Delete(repoDir, true)
    }
  ]
