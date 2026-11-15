module Bozzetto.Tests.McpResourcesTests

/// Item 12 of bozzetto-multiagent-vision.md's Phase 1 (§10): "Build MCP
/// resource registration + `resources/subscribe`". This file covers the
/// PURE half of that item — the resource content projections
/// (`CohortFrame -> JSON`, `SessionInfo list -> JSON`) and the
/// `resources/updated` no-change gate (`McpResourceGate`) — all unit
/// testable without a live MCP server, per the task's own requirement.
/// The SDK wiring itself (`Bozzetto/McpResources.fs`: `[<McpServerResource>]`
/// registration, `resources/subscribe`/`unsubscribe` handlers, and the
/// `notifications/resources/updated` push) is exercised indirectly by
/// constructing `BozzettoResources` directly against a real `McpContext`
/// (`TestInfrastructure.sharedCtxWith`) and calling its resource methods —
/// the same "call the underlying function directly" style
/// McpServerIntegrationTests.fs already uses for MCP tools.
///
/// Style mirrors CohortSseEventsTests.fs: format, parse the JSON back,
/// assert VALUES.

open System
open Fidelity.Data.JSON
open Expecto
open Expecto.Flip
open Bozzetto.Tests.TestInfrastructure

let private jsonOpts = Bozzetto.JsonCasing.CamelCase

let private clock = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
let private noEntropy : byte[] = [||]
let private alice = Bozzetto.MemberTable.MemberId.Minted "alice"

let private applyOk state cmd =
  match Bozzetto.Cohort.decide clock noEntropy state cmd with
  | Ok(s, _, _) -> s
  | Error e -> failwithf "unexpected cohort decide error: %A" e

let private joinAndClaim () =
  Bozzetto.Cohort.CohortState.empty ()
  |> fun s -> applyOk s (Bozzetto.Cohort.CohortCommand.Join(alice, Bozzetto.Cohort.JoinableRole.Implementer, Some "sess-1"))
  |> fun s -> applyOk s (Bozzetto.Cohort.CohortCommand.AcquireClaim(alice, Bozzetto.Cohort.ClaimScope.File "src/Foo.fs", "testing"))

let private getProp (name: string) (el: JsonValue) =
  match JsonValue.prop name el with
  | Some p -> p
  | None -> failtestf "missing property '%s' in %s" name (Json.serialize el)

let private getArr (name: string) (el: JsonValue) =
  match getProp name el with
  | JsonValue.Array items -> items
  | _ -> failtestf "property '%s' is not an array" name

let private getStr (name: string) (el: JsonValue) =
  match getProp name el |> JsonValue.asString with
  | Some s -> s
  | None -> failtestf "property '%s' is not a string" name

let private getInt64 (name: string) (el: JsonValue) =
  match getProp name el |> JsonValue.asInt with
  | Some n -> n
  | None -> failtestf "property '%s' is not an int64" name

let private mkSessionInfo (name: string) (workingDirectory: string) : Bozzetto.WorkerProtocol.SessionInfo =
  { Id = Bozzetto.WorkerProtocol.SessionId.newId ()
    Name = Some name
    Projects = [ "Foo.fsproj" ]
    WorkingDirectory = workingDirectory
    SolutionRoot = None
    CreatedAt = clock
    LastActivity = clock
    Status = Bozzetto.WorkerProtocol.SessionLifecycleStatus.Ready { Pid = 1; Port = None }
    Workflow = Bozzetto.WorkflowTypes.SessionWorkflow.Interactive
    ActiveProject = None
    ProjectRoles = []
    App = Bozzetto.AppRun.AppRunState.NotRunning }

[<Tests>]
let mcpResourcesTests = testList "MCP resources (item 12)" [

  // ── cohort://status content projection (SseWriter.cohortFrameJson) ──

  testCase "cohortFrameJson round-trips members/claims/tests/rows (same shape as cohort_matrix)" <| fun () ->
    let state = joinAndClaim ()
    let head : Bozzetto.Cohort.LedgerHead<Bozzetto.MemberTable.MemberId> = { Seq = 5L<Bozzetto.Measures.ledgerSeq>; State = state }
    let snapshot : Bozzetto.Cohort.SessionSnapshot<Bozzetto.MemberTable.MemberId> =
      { Member = Some alice
        SessionId = "sess-1"
        Generation = 1L
        PassingTests = [ Bozzetto.Cohort.TestId "t1" ]
        FailingTests = []
        StaleTests = [] }
    let frame = Bozzetto.Cohort.project head [| snapshot |]
    let json = Bozzetto.SseWriter.cohortFrameJson jsonOpts frame
    let root = Json.parseOrFail json
    getInt64 "version" root |> Expect.equal "version should be the ledger seq the frame was projected from" 5L
    let members = getArr "members" root
    members |> List.length |> Expect.equal "one member joined" 1
    getStr "id" members.[0] |> Expect.equal "member id displayed verbatim" "alice"
    let claims = getArr "claims" root
    claims |> List.length |> Expect.equal "one claim acquired" 1

  testCase "cohortFrameJson is stable/empty for an empty cohort (no members ever joined)" <| fun () ->
    let head : Bozzetto.Cohort.LedgerHead<Bozzetto.MemberTable.MemberId> = { Seq = 0L<Bozzetto.Measures.ledgerSeq>; State = Bozzetto.Cohort.CohortState.empty () }
    let frame = Bozzetto.Cohort.project head [||]
    let json1 = Bozzetto.SseWriter.cohortFrameJson jsonOpts frame
    let json2 = Bozzetto.SseWriter.cohortFrameJson jsonOpts frame
    json1 |> Expect.equal "same input frame must always project the same JSON (property 10: frame identity is (Version, SessionGens))" json2
    let root = Json.parseOrFail json1
    getInt64 "version" root |> Expect.equal "version should be 0 for an untouched ledger" 0L
    (getArr "members" root).Length |> Expect.equal "no members yet" 0
    (getArr "claims" root).Length |> Expect.equal "no claims yet" 0
    (getArr "rows" root).Length |> Expect.equal "no session rows yet" 0

  testCase "formatCohortMatrixEvent's SSE data payload is byte-identical to cohortFrameJson" <| fun () ->
    // Proves the extraction changed nothing observable about the existing
    // cohort_matrix SSE wire row — both surfaces share the exact same
    // projection (§5.6 "one read model").
    let state = joinAndClaim ()
    let head : Bozzetto.Cohort.LedgerHead<Bozzetto.MemberTable.MemberId> = { Seq = 1L<Bozzetto.Measures.ledgerSeq>; State = state }
    let frame = Bozzetto.Cohort.project head [||]
    let sse = Bozzetto.SseWriter.formatCohortMatrixEvent jsonOpts frame
    let dataLine =
      sse.Split('\n') |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None) |> String.concat "\n"
    dataLine |> Expect.equal "SSE data must equal the pure projection" (Bozzetto.SseWriter.cohortFrameJson jsonOpts frame)

  // ── sessions://list content projection (SessionOperations.sessionsToJson) ──

  testCase "sessionsToJson round-trips id/name/workingDirectory/status/workflow/projects" <| fun () ->
    let info = mkSessionInfo "my-session" "/repo/checkout"
    let json = Bozzetto.SessionOperations.sessionsToJson jsonOpts [ info ]
    let root = Json.parseOrFail json
    let sessions = getArr "sessions" root
    sessions |> List.length |> Expect.equal "one session" 1
    let s0 = sessions.[0]
    getStr "id" s0 |> Expect.equal "id should be the session id" (Bozzetto.WorkerProtocol.SessionId.value info.Id)
    // SessionInfo.displayName derives from SolutionRoot/WorkingDirectory's
    // last path segment (WorkerProtocol.fs), NOT the `Name` field — the same
    // behavior formatSessionInfo/list_sessions already reports, so
    // sessionsToJson must match it exactly rather than surfacing `Name`.
    getStr "name" s0 |> Expect.equal "name should be the display name (last segment of workingDirectory, no SolutionRoot set)" "checkout"
    getStr "workingDirectory" s0 |> Expect.equal "workingDirectory should round-trip" "/repo/checkout"
    getStr "workflow" s0 |> Expect.equal "workflow should be the label for Interactive" "REPL"
    let projects = getArr "projects" s0 |> List.choose JsonValue.asString
    projects |> Expect.equal "projects should round-trip" [ "Foo.fsproj" ]

  testCase "sessionsToJson is stable/empty for no active sessions" <| fun () ->
    let json = Bozzetto.SessionOperations.sessionsToJson jsonOpts []
    let root = Json.parseOrFail json
    (getArr "sessions" root).Length |> Expect.equal "no sessions" 0

  // ── sessionsListVersion: the sessions://list McpResourceGate key ──

  testCase "sessionsListVersion is identical for the same sessions in a different order" <| fun () ->
    let a = mkSessionInfo "a" "/repo/a"
    let b = mkSessionInfo "b" "/repo/b"
    Bozzetto.SessionOperations.sessionsListVersion [ a; b ]
    |> Expect.equal "order must not matter (sorted internally)" (Bozzetto.SessionOperations.sessionsListVersion [ b; a ])

  testCase "sessionsListVersion changes when a session's status changes" <| fun () ->
    let ready = mkSessionInfo "a" "/repo/a"
    let stopped = { ready with Status = Bozzetto.WorkerProtocol.SessionLifecycleStatus.Stopped }
    Bozzetto.SessionOperations.sessionsListVersion [ ready ]
    |> Expect.notEqual "a status change must change the version" (Bozzetto.SessionOperations.sessionsListVersion [ stopped ])

  testCase "sessionsListVersion changes when a session is added or removed" <| fun () ->
    let a = mkSessionInfo "a" "/repo/a"
    let b = mkSessionInfo "b" "/repo/b"
    Bozzetto.SessionOperations.sessionsListVersion [ a ]
    |> Expect.notEqual "adding a session must change the version" (Bozzetto.SessionOperations.sessionsListVersion [ a; b ])

  // ── McpResourceGate: the resources/updated no-change guard ──

  testCase "McpResourceGate.observe notifies on the first observation" <| fun () ->
    let notify, _ = Bozzetto.McpResourceGate.observe 1L Bozzetto.McpResourceGate.initial
    notify |> Expect.isTrue "the very first version observed must always notify"

  testCase "McpResourceGate.observe does not notify twice for the same unchanged version" <| fun () ->
    let notify1, state1 = Bozzetto.McpResourceGate.observe 1L Bozzetto.McpResourceGate.initial
    let notify2, state2 = Bozzetto.McpResourceGate.observe 1L state1
    notify1 |> Expect.isTrue "first observation notifies"
    notify2 |> Expect.isFalse "an unchanged tick (same version again) must emit zero notifications"
    state2 |> Expect.equal "state is unchanged when nothing was notified" state1

  testCase "McpResourceGate.observe notifies exactly once per version change, across a run of ticks" <| fun () ->
    // Mirrors wireCohortEventSubscription's lastMatrixVersion guard: several
    // unchanged ticks between two real changes must fire nothing.
    let versions = [ 1L; 1L; 1L; 2L; 2L; 3L ]
    let notifications =
      versions
      |> List.fold
        (fun (fired, state) v ->
          let notify, state' = Bozzetto.McpResourceGate.observe v state
          (fired @ [ notify ]), state')
        ([], Bozzetto.McpResourceGate.initial)
      |> fst
    notifications |> Expect.equal "notify exactly on the version changes: 1(first), 1, 1, 2(changed), 2, 3(changed)"
      [ true; false; false; true; false; true ]

  testCase "McpResourceGate.observe notifies again when the version changes back to a previous value" <| fun () ->
    let _, s1 = Bozzetto.McpResourceGate.observe 1L Bozzetto.McpResourceGate.initial
    let notify2, s2 = Bozzetto.McpResourceGate.observe 2L s1
    let notify3, _ = Bozzetto.McpResourceGate.observe 1L s2
    notify2 |> Expect.isTrue "moving to a new version notifies"
    notify3 |> Expect.isTrue "the gate does not assume monotonicity — any change from the last-seen version notifies"

  // ── BozzettoResources: the actual MCP resource methods, called directly ──

  testTask "BozzettoResources.CohortStatus reads the cohort owner's current frame as JSON" {
    let sessionId = Bozzetto.WorkerProtocol.SessionId.newId ()
    let ctx = sharedCtxWith sessionId
    let ledger = Bozzetto.Features.CohortLedger.InMemory.create<Bozzetto.MemberTable.MemberId> ()
    use cohortOwner =
      Bozzetto.Features.CohortOwner.start (Bozzetto.Utils.Log.asILogger ()) ledger (fun () -> clock) (fun () -> noEntropy) (fun _ -> ([], [], [], 0L))
    let! _ = cohortOwner.Commit(Bozzetto.Cohort.CohortCommand.Join(alice, Bozzetto.Cohort.JoinableRole.Implementer, Some "sess-1"))
    let ctxWithCohort = { ctx with CohortOwner = Some cohortOwner }
    let resources = Bozzetto.Server.McpResources.BozzettoResources(ctxWithCohort)
    let json = resources.CohortStatus()
    let root = Json.parseOrFail json
    let members = getArr "members" root
    members |> List.length |> Expect.equal "alice should have joined the cohort" 1
  }

  testCase "BozzettoResources.CohortStatus reports an empty-but-valid frame when no cohort is wired" <| fun () ->
    let sessionId = Bozzetto.WorkerProtocol.SessionId.newId ()
    let ctx = sharedCtxWith sessionId
    let resources = Bozzetto.Server.McpResources.BozzettoResources(ctx)
    let json = resources.CohortStatus()
    let root = Json.parseOrFail json
    (getArr "members" root).Length |> Expect.equal "no cohort owner -> no members, not an error" 0

  testTask "BozzettoResources.SessionsList reads the session list as JSON" {
    let sessionId = Bozzetto.WorkerProtocol.SessionId.newId ()
    let ctx = sharedCtxWith sessionId
    let resources = Bozzetto.Server.McpResources.BozzettoResources(ctx)
    let! (json: string) = resources.SessionsList()
    let root = Json.parseOrFail json
    JsonValue.prop "sessions" root |> Option.isSome |> Expect.isTrue "sessions://list content must be a JSON object with a sessions array"
  }
]
