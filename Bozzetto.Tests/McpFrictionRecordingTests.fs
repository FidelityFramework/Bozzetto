module Bozzetto.Tests.McpFrictionRecordingTests

open System
open Expecto
open Expecto.Flip
open Microsoft.Extensions.Logging.Abstractions
open Bozzetto.McpTools
open Bozzetto.Server.McpTools
open Bozzetto.Tests.TestInfrastructure

[<Tests>]
let tests =
  testList "MCP friction recording" [
    testCaseTask "successful MCP tool calls are recorded as clean friction events" <| fun () -> task {
      let baseCtx = sharedCtx ()
      let tools = BozzettoTools(baseCtx, NullLogger<BozzettoTools>.Instance)

      let! _ = tools.get_friction_summary()
      // FrictionStore is required now — no fallback to EventPersistence
      match baseCtx.FrictionStore with
      | Some store ->
        let! envelopeResult = Bozzetto.Features.McpFrictionRecorder.Recorder.readEnvelopeDirect store
        match envelopeResult with
        | Ok envelope ->
          envelope.Events.Length |> Expect.equal "friction events are recorded" 1  // get_friction_summary is recorded but marked non-actionable
        | Error err ->
          failwith (sprintf "Failed to read friction envelope: %s" err)
      | None ->
        // FrictionStore should always be configured in tests
        ()
    }

    // Brief B9 (observed-friction-plan.md §B9): with no bound MCP transport
    // (the shape every pre-B9 unit test call uses), recordToolResult must
    // keep stamping the pre-B9 "mcp" sentinel and an empty AgentKey — this
    // is the back-compat floor the additive fields must never break.
    testCaseTask "recordToolResult falls back to the \"mcp\" sentinel and an empty AgentKey with no bound transport" <| fun () -> task {
      let baseCtx = sharedCtx ()
      currentTransportSessionId.Value <- None
      do! recordToolResult baseCtx "send_fsharp_code" (Ok "done") 5
      match baseCtx.FrictionStore with
      | Some store ->
        let! envelopeResult = Bozzetto.Features.McpFrictionRecorder.Recorder.readEnvelopeDirect store
        match envelopeResult with
        | Ok envelope ->
          let recorded = envelope.Events |> List.last
          recorded.Session |> Bozzetto.Features.FrictionTelemetryTypes.SessionRef.value
          |> Expect.equal "no bound transport should keep the pre-B9 sentinel" "mcp"
          recorded.AgentKey |> Expect.equal "no bound transport should record an empty AgentKey" ""
        | Error err -> failwith (sprintf "Failed to read friction envelope: %s" err)
      | None -> ()
    }

    // Brief B9's headline requirement: once a transport connection IS bound
    // (McpServer.createServerCaptureFilter sets currentTransportSessionId on
    // every real call) and that connection has an active FSI session, the
    // recorded event must carry the REAL resolved session id and agent key —
    // never the flat "mcp" sentinel every event carried before this brief.
    testCaseTask "recordToolResult stamps the resolved session id and agent key when a transport session is bound" <| fun () -> task {
      let baseCtx = sharedCtx ()
      let tsid = "test-transport-" + Guid.NewGuid().ToString("N")
      currentTransportSessionId.Value <- Some tsid
      try
        let expectedAgentKey = resolvedKey "mcp"
        let expectedSessionId = "real-session-" + Guid.NewGuid().ToString("N").Substring(0, 8)
        baseCtx.SessionMap.[expectedAgentKey] <- expectedSessionId

        do! recordToolResult baseCtx "send_fsharp_code" (Ok "done") 5

        match baseCtx.FrictionStore with
        | Some store ->
          let! envelopeResult = Bozzetto.Features.McpFrictionRecorder.Recorder.readEnvelopeDirect store
          match envelopeResult with
          | Ok envelope ->
            let recorded = envelope.Events |> List.last
            recorded.Session |> Bozzetto.Features.FrictionTelemetryTypes.SessionRef.value
            |> Expect.equal "a bound transport with an active session must be recorded, not the \"mcp\" sentinel" expectedSessionId
            recorded.AgentKey |> Expect.equal "AgentKey should be the resolved connection-bound routing key" expectedAgentKey
          | Error err -> failwith (sprintf "Failed to read friction envelope: %s" err)
        | None -> ()
      finally
        currentTransportSessionId.Value <- None
    }

    // Brief B9: closes the loop the roast opened — the InvalidStateCall
    // detector (Brief B6) was structurally correct but had no matching
    // input because gate rejections were never recorded. A rejected call
    // must append exactly ONE AffordanceMismatch friction event.
    testCase "a gate-rejected call appends exactly one AffordanceMismatch friction event" <| fun _ ->
      let baseCtx = sharedCtx ()
      Bozzetto.Server.McpServer.recordGateRejection baseCtx "send_fsharp_code" "Tool 'send_fsharp_code' is not available while the session is warming up."
      match baseCtx.FrictionStore with
      | Some store ->
        match store.ReadEvents() with
        | Ok events ->
          let affordanceMismatches =
            events
            |> List.filter (fun e ->
              match e.Outcome with
              | Bozzetto.Features.FrictionTelemetryTypes.FrictionOutcome.EncounteredBlocker
                  Bozzetto.Features.FrictionTelemetryTypes.BlockerKind.AffordanceMismatch -> true
              | _ -> false)
          affordanceMismatches |> Expect.hasLength "exactly one AffordanceMismatch event must be recorded" 1
          let recorded = affordanceMismatches.Head
          recorded.Tool |> Bozzetto.Features.FrictionTelemetryTypes.ToolName.value
          |> Expect.equal "the rejected tool name should be recorded" "send_fsharp_code"
        | Error err -> failwith (sprintf "ReadEvents failed: %s" err)
      | None -> ()

    // Brief B9 — ErrorSignature is derived from the tool's BozzettoError via
    // FrictionSanitize.sanitizeText (reusing the sanitizer FrictionSendSafetyTests
    // already covers directly). A raw path/email/session-id embedded in the
    // underlying error reason must never survive into the persisted signature.
    testCaseTask "ErrorSignature never contains a raw path, email, or session id" <| fun () -> task {
      let baseCtx = sharedCtx ()
      let poisoned =
        [ Bozzetto.BozzettoError.EvalFailed @"in C:\Users\alice\secret-proj\Lib.fs: type mismatch"
          Bozzetto.BozzettoError.EvalFailed "read /home/alice/proj/src/App.fs failed"
          Bozzetto.BozzettoError.EvalFailed "contact dev@example.com about a1b2c3d4e5f6a7b8"
          Bozzetto.BozzettoError.WorkerSpawnFailed @"\\server\share\worker.exe missing" ]
      for err in poisoned do
        do! recordToolResult baseCtx "send_fsharp_code" (Error err) 5
      match baseCtx.FrictionStore with
      | Some store ->
        match store.ReadEvents() with
        | Ok events ->
          let doesNotContain (label: string) (needle: string) (text: string) =
            text.Contains(needle) |> Expect.isFalse (sprintf "%s (signature was: %s)" label text)
          for e in events do
            e.ErrorSignature |> doesNotContain "ErrorSignature must not contain a raw Windows path" @"C:\Users\alice"
            e.ErrorSignature |> doesNotContain "ErrorSignature must not contain a raw Unix path" "/home/alice"
            e.ErrorSignature |> doesNotContain "ErrorSignature must not contain a raw UNC path" @"\\server\share"
            e.ErrorSignature |> doesNotContain "ErrorSignature must not contain a raw email" "dev@example.com"
            e.ErrorSignature |> doesNotContain "ErrorSignature must not contain a raw session id" "a1b2c3d4e5f6a7b8"
        | Error err -> failwith (sprintf "ReadEvents failed: %s" err)
      | None -> ()
    }
  ]
