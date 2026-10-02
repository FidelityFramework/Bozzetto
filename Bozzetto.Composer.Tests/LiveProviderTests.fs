module Bozzetto.Composer.Tests.LiveProviderTests

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open ModelContextProtocol
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol
open Bozzetto.Tests.TestInfrastructure

let private field name (value: JsonElement) = value.GetProperty(name: string)
let private text name value = (field name value).GetString() |> Option.ofObj |> Option.defaultValue ""
let private parse (value: string) =
  use document = JsonDocument.Parse value
  document.RootElement.Clone()
let private success response =
  (field "success" response).GetBoolean() |> Expect.isTrue (response.GetRawText())
  field "result" response
let private authority response = field "authority" response
let private arguments identity = [ "host", box (text "host" identity); "session", box (text "session" identity); "epoch", box (text "epoch" identity) ]
let private required name =
  match Environment.GetEnvironmentVariable name with
  | value when not (String.IsNullOrWhiteSpace value) -> value
  | _ -> failtestf "%s is required for live provider acceptance" name
let private providerResult (result: CallToolResult) =
  result.StructuredContent.HasValue |> Expect.isTrue "Composer MCP result retains structured worker authority"
  result.StructuredContent.Value

type private Evidence(directory: string) =
  let gate = obj ()
  let mutable index = 0
  member _.Write(label: string, content: string) =
    lock gate (fun () ->
      index <- index + 1
      File.WriteAllText(Path.Combine(directory, sprintf "%03d-%s.json" index label), content))

let private httpJson (http: HttpClient) (evidence: Evidence) (methodName: string) (path: string) payload = task {
  use request = new HttpRequestMessage(HttpMethod(methodName), path)
  payload |> Option.iter (fun value -> request.Content <- new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"))
  use deadline = new CancellationTokenSource(if methodName = "GET" then TimeSpan.FromSeconds 3. else TimeSpan.FromMinutes 8.)
  use! response = http.SendAsync(request, deadline.Token)
  let! body = response.Content.ReadAsStringAsync()
  evidence.Write("http-" + path.Replace('/', '_'), body)
  return int response.StatusCode, parse body
}

let private providerHttp http evidence operation identity extra = task {
  let payload = dict (arguments identity @ extra)
  let! status, body = httpJson http evidence "POST" ("/api/composer/" + operation) (Some payload)
  status |> Expect.equal "provider HTTP transport completed" 200
  return body
}

let private call (mcp: McpClient) (evidence: Evidence) name args cancellation = task {
  let! result = mcp.CallToolAsync(name, readOnlyDict args, null, null, cancellation)
  evidence.Write("mcp-" + name, JsonSerializer.Serialize result)
  return result
}

let private composer mcp evidence name args = task {
  let! result = call mcp evidence name args CancellationToken.None
  return providerResult result
}

let private ownAsync (value: IAsyncDisposable) =
  { new IDisposable with
      member _.Dispose() = value.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds 5.).GetAwaiter().GetResult() }

let private connect port =
  let transport = new HttpClientTransport(HttpClientTransportOptions(Endpoint = Uri(sprintf "http://127.0.0.1:%d/" port)), null)
  McpClient.CreateAsync(transport, null, null, CancellationToken.None)

let private cliStatus dotnet daemonDll port directory = task {
  let info = ProcessStartInfo(dotnet, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true)
  for argument in [ daemonDll; "status"; "--mcp-port"; string port ] do
    info.ArgumentList.Add argument
  info.WorkingDirectory <- directory
  info.Environment["BOZZETTO_DATA_DIR"] <- Path.Combine(directory, "daemon-data")
  use statusProcess = Process.Start info
  let stdout = statusProcess.StandardOutput.ReadToEndAsync()
  let stderr = statusProcess.StandardError.ReadToEndAsync()
  try
    do! statusProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
    let! output = stdout.WaitAsync(TimeSpan.FromSeconds 3.)
    let! errors = stderr.WaitAsync(TimeSpan.FromSeconds 3.)
    statusProcess.ExitCode |> Expect.equal ("CLI status completed: " + errors) 0
    return output
  finally
    // This owns only the short-lived status command, never the shared daemon.
    if not statusProcess.HasExited then statusProcess.Kill true
}

let private readResource (mcp: McpClient) (evidence: Evidence) = task {
  let! resource = mcp.ReadResourceAsync("composer://sessions", (null: RequestOptions), CancellationToken.None)
  let json =
    resource.Contents
    |> Seq.choose (function :? TextResourceContents as content -> Some content.Text | _ -> None)
    |> Seq.exactlyOne
    |> parse
  evidence.Write("composer-resource", json.GetRawText())
  return json
}

let private waitUntil seconds description probe = task {
  let deadline = DateTimeOffset.UtcNow.AddSeconds seconds
  let mutable complete = false
  while not complete && DateTimeOffset.UtcNow < deadline do
    let! ready = probe ()
    complete <- ready
    if not ready then do! Task.Delay 100
  complete |> Expect.isTrue description
}

let private nextSse (reader: StreamReader) = task {
  use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 10.)
  let mutable received = false
  while not received do
    let! line = reader.ReadLineAsync(timeout.Token)
    if isNull line then failtest "Human event stream ended before shared change notification"
    else received <- line.StartsWith("data:", StringComparison.Ordinal)
}

[<Tests>]
let tests =
  testSequenced <| testList "Live Composer shared interfaces" [
    testTask "MCP agents and human HTTP share Clef authority" {
      return! task {
        let cache = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache", "bozzetto", "live-provider-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory cache |> ignore
        printfn "Live provider evidence: %s" cache
        let evidence = Evidence cache
        let firstDirectory = Path.Combine(cache, "first")
        let secondDirectory = Path.Combine(cache, "second")
        Directory.CreateDirectory firstDirectory |> ignore
        Directory.CreateDirectory secondDirectory |> ignore
        let firstFixture = NativeProviderTests.fixture firstDirectory
        let secondFixture = NativeProviderTests.fixture secondDirectory
        let daemonDll =
          match Environment.GetEnvironmentVariable "BOZZETTO_DAEMON_DLL" with
          | value when not (String.IsNullOrWhiteSpace value) -> value
          | _ -> Path.Combine(AppContext.BaseDirectory, "Bozzetto.dll")
        File.Exists daemonDll |> Expect.isTrue "built daemon closure must be available"
        let port, dashboardPort = TestPorts.reservePair ()
        let dotnet =
          match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
          | value when not (String.IsNullOrWhiteSpace value) -> value
          | _ -> "dotnet"
        let info = ProcessStartInfo(dotnet, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true)
        info.ArgumentList.Add daemonDll
        for argument in [ "--mcp-port"; string port; "--no-resume"; "--owner-pid"; string Environment.ProcessId
                          "--owner-start"; string (Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks); "--ttl"; "10m" ] do
          info.ArgumentList.Add argument
        info.WorkingDirectory <- cache
        info.Environment["BOZZETTO_DATA_DIR"] <- Path.Combine(cache, "daemon-data")
        info.Environment["BOZZETTO_COMPOSER_WORKER"] <- required "BOZZETTO_COMPOSER_WORKER"
        info.Environment["DOTNET_HOST_PATH"] <- dotnet
        use daemon = Process.Start info
        use stdout = File.Create(Path.Combine(cache, "daemon-stdout.log"))
        use stderr = File.Create(Path.Combine(cache, "daemon-stderr.log"))
        let stdoutDrain = daemon.StandardOutput.BaseStream.CopyToAsync stdout
        let stderrDrain = daemon.StandardError.BaseStream.CopyToAsync stderr
        use http = new HttpClient(BaseAddress = Uri(sprintf "http://127.0.0.1:%d" port), Timeout = TimeSpan.FromMinutes 8.)
        try
          do! waitUntil 45. "owned daemon answers its bounded identity probe" (fun () -> task {
            if daemon.HasExited then failtestf "Daemon exited early with %d; see %s" daemon.ExitCode cache
            use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 3.)
            try
              use! response = http.GetAsync(sprintf "http://127.0.0.1:%d/api/daemon-info" dashboardPort, timeout.Token)
              if response.IsSuccessStatusCode then
                let! body = response.Content.ReadAsStringAsync()
                evidence.Write("daemon-identity", body)
                let identity = parse body
                let pid =
                  match identity.TryGetProperty "pid" with
                  | true, value -> value.GetInt32()
                  | _ -> (field "Pid" identity).GetInt32()
                pid |> Expect.equal "probe belongs to the exact spawned daemon" daemon.Id
                use! health = http.GetAsync("/health", timeout.Token)
                return health.IsSuccessStatusCode
              else return false
            with :? HttpRequestException | :? OperationCanceledException -> return false
          })
          let! mcp = connect port
          use ownedMcp = ownAsync mcp
          let! discovered = mcp.ListToolsAsync((null: RequestOptions), CancellationToken.None)
          discovered |> Seq.exists (fun tool -> tool.Name = "composer_open_project") |> Expect.isTrue "Composer tools are discoverable"
          discovered |> Seq.exists (fun tool -> tool.Name = "composer_format_preview") |> Expect.isTrue "public formatting preview is discoverable"
          let! initialFsiStatus, initialFsi = httpJson http evidence "GET" "/api/sessions" None
          initialFsiStatus |> Expect.equal "FSI list is readable" 200
          (field "sessions" initialFsi).GetArrayLength() |> Expect.equal "Composer is callable before any FSI session exists" 0
          let! opened = composer mcp evidence "composer_open_project" [ "project", box firstFixture.Project ]
          success opened |> ignore
          let first = authority opened
          let notification = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
          let! subscription = mcp.SubscribeToResourceAsync("composer://sessions",
            Func<ResourceUpdatedNotificationParams, CancellationToken, ValueTask>(fun _ _ -> notification.TrySetResult() |> ignore; ValueTask.CompletedTask),
            null, CancellationToken.None)
          use ownedSubscription = ownAsync subscription
          let! before = readResource mcp evidence
          (field "sessions" before).GetArrayLength() |> Expect.equal "resource exposes the MCP-opened session" 1
          use! streamResponse = http.GetAsync("/api/composer/events", HttpCompletionOption.ResponseHeadersRead)
          use! stream = streamResponse.Content.ReadAsStreamAsync()
          use reader = new StreamReader(stream)
          do! nextSse reader
          let! humanPage = http.GetStringAsync "/composer"
          humanPage |> Expect.stringContains "human page explains reservation discipline" "Reserve before editing"

          let! reserved = composer mcp evidence "composer_reserve_edit" (arguments first @ [ "label", box "cold" ])
          let reservation = reserved |> success |> text "reservation"
          do! notification.Task.WaitAsync(TimeSpan.FromSeconds 10.)
          do! nextSse reader
          let! cold = providerHttp http evidence "build" first [ "reservation", box reservation ]
          let accepted = success cold
          let! status = composer mcp evidence "composer_session_status" (arguments first)
          let current = status |> success |> field "current"
          text "artifactSha256" current |> Expect.equal "MCP status sees HTTP build artifact" (text "artifactSha256" accepted)
          text "sourceVersion" current |> Expect.equal "both interfaces share the checked source receipt" (text "sourceVersion" accepted)
          // These are external JSON adapter refusals. Neither malformed
          // arguments nor a foreign operation can become a binary worker request.
          let! malformedCode, malformed = httpJson http evidence "POST" "/api/composer/run"
                                            (Some(dict (arguments first @ [ "arguments", box [| 1 |] ])))
          malformedCode |> Expect.equal "malformed run rejected at HTTP boundary" 400
          text "code" (field "error" malformed) |> Expect.equal "typed arguments required" "invalid_request"
          let! unsupportedCode, unsupported = httpJson http evidence "POST" "/api/composer/eval" (Some(dict (arguments first)))
          unsupportedCode |> Expect.equal "unsupported operation rejected at HTTP boundary" 400
          text "code" (field "error" unsupported) |> Expect.equal "no alternate evaluation provider" "unsupported_operation"
          let! retiredCode, retired = httpJson http evidence "POST" "/api/composer/read_revision" (Some(dict (arguments first)))
          retiredCode |> Expect.equal "retired graph delivery has no HTTP route" 400
          text "code" (field "error" retired) |> Expect.equal "full graph delivery is unsupported" "unsupported_operation"
          let! afterMalformed = composer mcp evidence "composer_session_status" (arguments first)
          text "artifactSha256" (afterMalformed |> success |> field "current")
          |> Expect.equal "adapter refusal preserves accepted authority" (text "artifactSha256" accepted)
          // The daemon has zero F# sessions and one accepted Composer session.
          // Legacy counts must name their provider rather than imply no work exists.
          let! providerState = readResource mcp evidence
          (field "sessions" providerState).GetArrayLength() |> Expect.equal "one accepted Composer session remains visible" 1
          let! healthCode, health = httpJson http evidence "GET" "/health" None
          healthCode |> Expect.equal "health transport completed" 200
          (field "sessionCount" health).GetInt32() |> Expect.equal "health retains the F# count" 0
          text "sessionProvider" health |> Expect.equal "health scopes inherited counts" "fsharp"
          text "diagnosticSummary" health |> Expect.equal "health does not claim Composer is empty" "No F# sessions registered with the daemon."
          let! daemonStatus = call mcp evidence "get_daemon_status" [] CancellationToken.None
          let daemonStatusJson =
            daemonStatus.Content
            |> Seq.choose (function :? TextContentBlock as content -> Some content.Text | _ -> None)
            |> Seq.exactlyOne
            |> parse
          let inheritedSessions = field "sessions" daemonStatusJson
          text "provider" inheritedSessions |> Expect.equal "MCP status scopes inherited counts" "fsharp"
          (field "total" inheritedSessions).GetInt32() |> Expect.equal "MCP retains the F# count" 0
          let! cliOutput = cliStatus dotnet daemonDll port cache
          evidence.Write("cli-status", JsonSerializer.Serialize {| output = cliOutput |})
          cliOutput |> Expect.stringContains "CLI zero is explicitly F#" "F# sessions: 0 active"
          cliOutput |> Expect.stringContains "CLI advertises the current MCP endpoint" (sprintf "MCP (Streamable HTTP): http://localhost:%d/" port)
          cliOutput |> Expect.stringContains "CLI labels SSE compatibility" (sprintf "MCP (SSE, older clients): http://localhost:%d/sse" port)
          let! beforeRun = providerHttp http evidence "run" first [ "arguments", box ([||]: string array) ]
          let result = success beforeRun
          text "standardOutput" result |> Expect.equal "HTTP execution uses accepted native artifact" "stable\nbefore\n"
          (field "exitCode" result).GetInt32() |> Expect.equal "native exit" 0

          let unchangedSource = File.ReadAllText firstFixture.Source
          let editedBuffer = unchangedSource.Replace("let changeable () = false", "let changeable () = true")
          editedBuffer.Contains "let changeable () = true" |> Expect.isTrue "the immutable edited buffer contains the known semantic change"
          let document = "buffer://live/IncrementalScalarRegions.clef"
          let incarnation = Guid.NewGuid().ToString("D")
          let policy = "clef-two-space-lf-v1"
          let baseDigest = Encoding.UTF8.GetBytes editedBuffer |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
          let! formatting = composer mcp evidence "composer_format_preview" (arguments first @ [
            "generation", box ((field "generation" (authority afterMalformed)).GetInt64())
            "document", box document; "incarnation", box incarnation; "revision", box 1UL
            "source", box editedBuffer; "configuration", box policy
          ])
          let preview = success formatting
          text "document" preview |> Expect.equal "public preview preserves the opaque document identity" document
          text "incarnation" preview |> Expect.equal "public preview preserves the document incarnation" incarnation
          (field "revision" preview).GetUInt64() |> Expect.equal "public preview preserves the immutable buffer revision" 1UL
          text "configuration" preview |> Expect.equal "public preview preserves the selected formatting policy" policy
          text "sourceSha256" preview |> Expect.equal "public preview identifies the exact supplied edited buffer" baseDigest
          File.ReadAllText firstFixture.Source |> Expect.equal "public preview performs no source write" unchangedSource
          (field "generation" (authority formatting)).GetInt64()
          |> Expect.equal "public preview does not reserve an edit" ((field "generation" (authority afterMalformed)).GetInt64())
          let! afterPreview = composer mcp evidence "composer_session_status" (arguments first)
          text "artifactSha256" (afterPreview |> success |> field "current")
          |> Expect.equal "public preview preserves accepted artifact authority" (text "artifactSha256" accepted)

          let! editing = providerHttp http evidence "reserve" first [ "label", box "save edited buffer and apply completed preview" ]
          let editReservation = editing |> success |> text "reservation"
          let! withdrawn = composer mcp evidence "composer_session_status" (arguments first)
          (withdrawn |> success |> field "current").ValueKind |> Expect.equal "human reservation immediately withdraws MCP current" JsonValueKind.Null
          // Saving and applying a completed preview use the new successful
          // reservation. The preview itself grants no source write authority.
          File.WriteAllText(firstFixture.Source, editedBuffer)
          File.ReadAllText firstFixture.Source |> Expect.equal "saved buffer is the exact preview base before formatting apply" editedBuffer
          File.ReadAllBytes firstFixture.Source |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
          |> Expect.equal "saved base bytes match the public preview digest before apply" baseDigest
          File.WriteAllText(firstFixture.Source, text "formatted" preview)
          // Streamable HTTP cancellation aborts a response stream; MCP work
          // cancellation is an explicit notification targeting its JSON-RPC id.
          let buildRequestId = "live-build-" + Guid.NewGuid().ToString("N")
          let buildParameters =
            JsonSerializer.SerializeToNode({| name = "composer_build"
                                              arguments = dict (arguments first @ [ "reservation", box editReservation ]) |})
          let buildRequest = JsonRpcRequest(Id = RequestId(buildRequestId), Method = "tools/call", Params = buildParameters)
          evidence.Write("mcp-build-request", JsonSerializer.Serialize buildRequest)
          use buildTransport = new CancellationTokenSource(TimeSpan.FromMinutes 8.)
          let pending = mcp.SendRequestAsync(buildRequest, buildTransport.Token)
          do! waitUntil 30. "real MCP build enters supervised work before cancellation" (fun () -> task {
            let! status = providerHttp http evidence "status" first []
            return (status |> success |> field "busy").GetBoolean()
          })
          let canceled = JsonRpcNotification(
            Method = NotificationMethods.CancelledNotification,
            Params = JsonSerializer.SerializeToNode({| requestId = buildRequestId; reason = "live shared-authority acceptance" |}))
          evidence.Write("mcp-cancellation-notification", JsonSerializer.Serialize canceled)
          use notificationDeadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.)
          do! mcp.SendMessageAsync(canceled, notificationDeadline.Token)
          do! waitUntil 30. "MCP cancellation notification withdraws shared artifact authority" (fun () -> task {
            let! status = providerHttp http evidence "status" first []
            let value = success status
            return not ((field "busy" value).GetBoolean()) && (field "current" value).ValueKind = JsonValueKind.Null
          })
          // Close the abandoned HTTP response only AFTER server-side authority
          // is withdrawn, so disconnect alone cannot make this assertion pass.
          buildTransport.Cancel()
          try
            let! response = pending.WaitAsync(TimeSpan.FromSeconds 10.)
            evidence.Write("mcp-canceled-build-response", JsonSerializer.Serialize response)
          with :? OperationCanceledException -> ()
          let! recovered = providerHttp http evidence "reserve" first [ "label", box "recover canceled edit" ]
          let! rebuilt = providerHttp http evidence "build" first [ "reservation", box (recovered |> success |> text "reservation") ]
          success rebuilt |> ignore
          let! afterRun = composer mcp evidence "composer_run_current" (arguments first @ [ "arguments", box ([||]: string array) ])
          text "standardOutput" (success afterRun) |> Expect.equal "MCP executes human-edited native artifact" "stable\nafter\n"

          let! secondOpen = composer mcp evidence "composer_open_project" [ "project", box secondFixture.Project ]
          success secondOpen |> ignore
          let second = authority secondOpen
          let! secondReserve = providerHttp http evidence "reserve" second [ "label", box "independent project" ]
          let! secondBuild = providerHttp http evidence "build" second [ "reservation", box (secondReserve |> success |> text "reservation") ]
          text "artifactPath" (success secondBuild) |> Expect.notEqual "projects own distinct output artifacts" (text "artifactPath" (success rebuilt))
          let! canceledFirst = composer mcp evidence "composer_cancel" (arguments first)
          success canceledFirst |> ignore
          let! independent = composer mcp evidence "composer_run_current" (arguments second @ [ "arguments", box ([||]: string array) ])
          text "standardOutput" (success independent) |> Expect.equal "canceling first project preserves second executable" "stable\nbefore\n"

          let! reconnected = connect port
          use ownedReconnect = ownAsync reconnected
          let! reconnectResource = readResource reconnected evidence
          let found = (field "sessions" reconnectResource).EnumerateArray() |> Seq.find (fun row -> text "session" (authority row) = text "session" second)
          text "host" (authority found) |> Expect.equal "reconnected observer sees same owner" (text "host" second)
          text "epoch" (authority found) |> Expect.equal "reconnection creates no compiler epoch" (text "epoch" second)
          let! retired = providerHttp http evidence "prepare_compiler_change" second []
          success retired |> ignore
          let! oldRun = composer reconnected evidence "composer_run_current" (arguments second @ [ "arguments", box ([||]: string array) ])
          (field "success" oldRun).GetBoolean() |> Expect.isFalse "human retirement revokes agent execution authority"
          let! freshOpen = composer reconnected evidence "composer_open_project" [ "project", box secondFixture.Project ]
          success freshOpen |> ignore
          text "epoch" (authority freshOpen) |> Expect.notEqual "reopening after replacement has a new compiler epoch" (text "epoch" second)
        finally
          try
            use shutdown = new HttpClient(Timeout = TimeSpan.FromSeconds 3.)
            use response = shutdown.PostAsync(sprintf "http://127.0.0.1:%d/api/shutdown" dashboardPort, new StringContent("{}")).GetAwaiter().GetResult()
            ignore response
          with _ -> ()
          if not (daemon.WaitForExit 5000) then
            daemon.Kill true
            daemon.WaitForExit 5000 |> ignore
          Task.WhenAll(stdoutDrain, stderrDrain).Wait(TimeSpan.FromSeconds 5.) |> ignore
      }
    }
  ]
  |> Integration.register (Integration.Dedicated "--integration-composer")
