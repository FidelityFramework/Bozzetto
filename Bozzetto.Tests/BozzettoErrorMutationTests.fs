/// ## BozzettoError Mutation Tests
///
/// Proves the test suite catches mutations in `Bozzetto.BozzettoError` functions.
/// Focuses on classification functions (`toLogLevel`, `toHttpStatus`, `isClientError`,
/// `isServerError`, `isGatewayError`, `isInfraError`) and agent-facing output.
///
/// Each case asserts EXACT equality against the correct value (not merely
/// inequality with one hand-picked wrong value) so a mutant that returns any
/// other wrong value is killed too.
module BozzettoErrorMutationTests

open Expecto
open Expecto.Flip
open Bozzetto

// ── Test Fixtures ──────────────────────────────────────────────────────────

let sessionNotFound = BozzettoError.SessionNotFound "abc"
let evalFailed = BozzettoError.EvalFailed "bad code"
let portInUse = BozzettoError.PortInUse 47749
let workerTimeout = BozzettoError.WorkerTimeout("s1", "eval", 30.0)
let noActiveSessions = BozzettoError.NoActiveSessions
let unexpected = BozzettoError.Unexpected (System.Exception("boom"))
let daemonNotRunning = BozzettoError.DaemonNotRunning
let toolNotAvailable = BozzettoError.ToolNotAvailable("send", SessionState.WarmingUp, ["get_status"])
let daemonStartFailed = BozzettoError.DaemonStartFailed "port bound"
let restartLimitExceeded = BozzettoError.RestartLimitExceeded(10, 5.0)
let workerSpawnFailed = BozzettoError.WorkerSpawnFailed "SDK missing"
let sessionCreationFailed = BozzettoError.SessionCreationFailed "bad path"

// ── Mutation Tests ─────────────────────────────────────────────────────────

let bozzettoErrorMutationTests = testList "BozzettoError mutations" [

  // ── toLogLevel ────────────────────────────────────────────────────────────

  testCase "WHY — toLogLevel_DaemonStartFailed_is_Critical — critical errors must not be downgraded" <| fun () ->
    BozzettoError.toLogLevel daemonStartFailed
    |> Expect.equal "DaemonStartFailed must log at Critical" Microsoft.Extensions.Logging.LogLevel.Critical

  testCase "WHY — toLogLevel_PortInUse_is_Critical — port conflicts are critical" <| fun () ->
    BozzettoError.toLogLevel portInUse
    |> Expect.equal "PortInUse must log at Critical" Microsoft.Extensions.Logging.LogLevel.Critical

  testCase "WHY — toLogLevel_EvalFailed_is_Error — eval failures are errors, not warnings" <| fun () ->
    BozzettoError.toLogLevel evalFailed
    |> Expect.equal "EvalFailed must log at Error" Microsoft.Extensions.Logging.LogLevel.Error

  testCase "WHY — toLogLevel_SessionNotFound_is_Information — not-found is informational, not error" <| fun () ->
    BozzettoError.toLogLevel sessionNotFound
    |> Expect.equal "SessionNotFound must log at Information" Microsoft.Extensions.Logging.LogLevel.Information

  testCase "WHY — toLogLevel_RestartLimitExceeded_is_Critical — restart limit is critical" <| fun () ->
    BozzettoError.toLogLevel restartLimitExceeded
    |> Expect.equal "RestartLimitExceeded must log at Critical" Microsoft.Extensions.Logging.LogLevel.Critical

  // ── toHttpStatus ──────────────────────────────────────────────────────────

  testCase "WHY — toHttpStatus_SessionNotFound_is_404 — not-found must be 404" <| fun () ->
    BozzettoError.toHttpStatus sessionNotFound
    |> Expect.equal "SessionNotFound must be 404" 404

  testCase "WHY — toHttpStatus_PortInUse_is_409 — port conflict must be 409" <| fun () ->
    BozzettoError.toHttpStatus portInUse
    |> Expect.equal "PortInUse must be 409" 409

  testCase "WHY — toHttpStatus_WorkerTimeout_is_504 — timeout must be 504" <| fun () ->
    BozzettoError.toHttpStatus workerTimeout
    |> Expect.equal "WorkerTimeout must be 504" 504

  testCase "WHY — toHttpStatus_NoActiveSessions_is_404 — empty sessions must be 404" <| fun () ->
    BozzettoError.toHttpStatus noActiveSessions
    |> Expect.equal "NoActiveSessions must be 404" 404

  testCase "WHY — toHttpStatus_WorkerSpawnFailed_is_502 — spawn failure is a bad gateway" <| fun () ->
    BozzettoError.toHttpStatus workerSpawnFailed
    |> Expect.equal "WorkerSpawnFailed must be 502" 502

  // ── isClientError ─────────────────────────────────────────────────────────

  testCase "WHY — isClientError_SessionNotFound_is_true — 404s are client errors" <| fun () ->
    BozzettoError.isClientError sessionNotFound
    |> Expect.isTrue "SessionNotFound must be a client error"

  testCase "WHY — isClientError_EvalFailed_is_false — 500s are not client errors" <| fun () ->
    BozzettoError.isClientError evalFailed
    |> Expect.isFalse "EvalFailed must not be a client error"

  testCase "WHY — isClientError_DaemonNotRunning_is_true — daemon down is client-actionable" <| fun () ->
    BozzettoError.isClientError daemonNotRunning
    |> Expect.isTrue "DaemonNotRunning must be a client error"

  // ── isServerError ─────────────────────────────────────────────────────────

  testCase "WHY — isServerError_EvalFailed_is_true — eval failures are server errors" <| fun () ->
    BozzettoError.isServerError evalFailed
    |> Expect.isTrue "EvalFailed must be a server error"

  testCase "WHY — isServerError_SessionNotFound_is_false — 404s are not server errors" <| fun () ->
    BozzettoError.isServerError sessionNotFound
    |> Expect.isFalse "SessionNotFound must not be a server error"

  testCase "WHY — isServerError_DaemonStartFailed_is_true — daemon crashes are server errors" <| fun () ->
    BozzettoError.isServerError daemonStartFailed
    |> Expect.isTrue "DaemonStartFailed must be a server error"

  // ── isGatewayError ────────────────────────────────────────────────────────

  testCase "WHY — isGatewayError_WorkerTimeout_is_true — timeouts are gateway errors" <| fun () ->
    BozzettoError.isGatewayError workerTimeout
    |> Expect.isTrue "WorkerTimeout must be a gateway error"

  testCase "WHY — isGatewayError_EvalFailed_is_false — eval failures are not gateway errors" <| fun () ->
    BozzettoError.isGatewayError evalFailed
    |> Expect.isFalse "EvalFailed must not be a gateway error"

  testCase "WHY — isGatewayError_WorkerSpawnFailed_is_true — spawn failure is a gateway error" <| fun () ->
    BozzettoError.isGatewayError workerSpawnFailed
    |> Expect.isTrue "WorkerSpawnFailed must be a gateway error"

  // ── isInfraError ──────────────────────────────────────────────────────────

  testCase "WHY — isInfraError_PortInUse_is_true — port conflicts are infra errors" <| fun () ->
    BozzettoError.isInfraError portInUse
    |> Expect.isTrue "PortInUse must be an infra error"

  testCase "WHY — isInfraError_EvalFailed_is_false — eval failures are not infra errors" <| fun () ->
    BozzettoError.isInfraError evalFailed
    |> Expect.isFalse "EvalFailed must not be an infra error"

  testCase "WHY — isInfraError_RestartLimitExceeded_is_true — restart limit is an infra error" <| fun () ->
    BozzettoError.isInfraError restartLimitExceeded
    |> Expect.isTrue "RestartLimitExceeded must be an infra error"

  // ── describeForAgent ──────────────────────────────────────────────────────

  testCase "WHY — describeForAgent_composes_describe_and_suggestedAction — agents need next steps" <| fun () ->
    let expected = sprintf "%s → Next: %s" (BozzettoError.describe sessionNotFound) (BozzettoError.suggestedAction sessionNotFound)
    BozzettoError.describeForAgent sessionNotFound
    |> Expect.equal "describeForAgent must be \"<describe> → Next: <suggestedAction>\"" expected

  // ── Mutual exclusion: isClientError and isServerError ──────────────────────

  testCase "WHY — isClientError_and_isServerError_are_mutually_exclusive — classification must be consistent" <| fun () ->
    let allErrors = [sessionNotFound; evalFailed; portInUse; workerTimeout; noActiveSessions; unexpected; daemonNotRunning; daemonStartFailed; restartLimitExceeded]
    let violations = allErrors |> List.filter (fun e -> BozzettoError.isClientError e && BozzettoError.isServerError e)
    violations
    |> Expect.isEmpty "no error may be classified as both client and server"
]
