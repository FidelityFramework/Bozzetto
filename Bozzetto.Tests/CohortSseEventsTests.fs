module Bozzetto.Tests.CohortSseEventsTests

/// Round-trip coverage for item 15a's three cohort SSE wire rows
/// (`cohort_matrix` / `claim_changed` / `landing_changed`, SseWriter.fs) plus
/// the claim early-warning row (`save_observed`, multi-agent vision §5.1):
/// the `formatSaveObservedEvent` wire round-trip, the pure
/// `McpServer.saveObservedRow` event→row mapping, and the pure
/// `DaemonMode.resolveSaveObserver` save→session→member resolver — all unit
/// tested here rather than through a full daemon spawn.
/// Mirrors SseContractComplianceTests.fs's "JSON shape contracts" style —
/// format, parse the JSON back, assert VALUES (not just property presence).
/// Fully qualifies every `Bozzetto.Cohort`/`Bozzetto.MemberTable` reference:
/// `Bozzetto.Cohort.TestId` would otherwise collide with
/// `Bozzetto.Features.LiveTesting.TestId` if this file ever opens both.

open System
open System.Text.Json
open Expecto
open Expecto.Flip

// ── Scenario builder ──

let private jsonOpts =
  let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
  opts.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())
  opts

let private clock = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
let private noEntropy : byte[] = [||]
let private alice = Bozzetto.MemberTable.MemberId.Minted "alice"
let private bob = Bozzetto.MemberTable.MemberId.Minted "bob"

let private applyOk state cmd =
  match Bozzetto.Cohort.decide clock noEntropy state cmd with
  | Ok(s, _, _) -> s
  | Error e -> failwithf "unexpected cohort decide error: %A" e

let private joinAndClaim () =
  Bozzetto.Cohort.CohortState.empty ()
  |> fun s -> applyOk s (Bozzetto.Cohort.CohortCommand.Join(alice, Bozzetto.Cohort.JoinableRole.Implementer, Some "sess-1"))
  |> fun s -> applyOk s (Bozzetto.Cohort.CohortCommand.AcquireClaim(alice, Bozzetto.Cohort.ClaimScope.File "src/Foo.fs", "testing"))

/// `joinAndClaim` plus a second member (`bob`, bound to `"sess-2"`) who
/// holds no claim of his own — the saver whose `ObserveSave` should land
/// inside alice's claim.
let private joinTwoAndClaim () =
  joinAndClaim ()
  |> fun s -> applyOk s (Bozzetto.Cohort.CohortCommand.Join(bob, Bozzetto.Cohort.JoinableRole.Implementer, Some "sess-2"))

let private getProp (name: string) (el: JsonElement) = el.GetProperty(name)

/// Minimal `WorkerProtocol.SessionInfo` for `resolveSaveObserver` tests —
/// only `Id`/`WorkingDirectory` matter to that resolver; every other field
/// is a harmless placeholder, mirroring DashboardParsingTests.fs's
/// `SidebarCards.info` builder.
let private mkSessionInfo (id: Bozzetto.WorkerProtocol.SessionId) (workingDirectory: string) : Bozzetto.WorkerProtocol.SessionInfo =
  { Id = id
    Name = None
    Projects = []
    WorkingDirectory = workingDirectory
    SolutionRoot = None
    CreatedAt = clock
    LastActivity = clock
    Status = Bozzetto.WorkerProtocol.SessionLifecycleStatus.Stopped
    Workflow = Bozzetto.WorkflowTypes.SessionWorkflow.Interactive
    ActiveProject = None
    ProjectRoles = []
    App = Bozzetto.AppRun.AppRunState.NotRunning }

/// A `MemberRecord` for `alice`, bound to `sid` — the `resolveSaveObserver`
/// tests' cohort membership fixture.
let private mkAliceMember (sid: Bozzetto.WorkerProtocol.SessionId) : Bozzetto.Cohort.MemberRecord =
  { Role = Bozzetto.Cohort.JoinableRole.Implementer
    Presence = Bozzetto.Cohort.MemberPresence.Present
    LastRenewal = clock
    Session = Some(Bozzetto.WorkerProtocol.SessionId.value sid) }

// ── Tests ──

[<Tests>]
let cohortSseEventsTests = testList "Cohort SSE events (item 15a)" [

  testCase "claim_changed round-trips claimId/scope/holder/fence/kind" <| fun () ->
    let state = joinAndClaim ()
    let claimId, claim = state.Claims |> Map.toList |> List.exactlyOne
    let (Bozzetto.Cohort.ClaimId expectedClaimId) = claimId
    let payload =
      Bozzetto.SseWriter.formatClaimChangedEvent jsonOpts "acquired" claim
      |> fun sse ->
        sse.Split('\n')
        |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None)
        |> String.concat "\n"
    use doc = JsonDocument.Parse(payload)
    let root = doc.RootElement
    root |> getProp "claimId" |> fun p -> p.GetString()
    |> Expect.equal "claimId should match the acquired claim's id" expectedClaimId
    let scope = root |> getProp "scope"
    scope |> getProp "kind" |> fun p -> p.GetString() |> Expect.equal "scope kind should be file" "file"
    scope |> getProp "path" |> fun p -> p.GetString() |> Expect.equal "scope path should be the claimed file" "src/Foo.fs"
    root |> getProp "holder" |> fun p -> p.GetString()
    |> Expect.equal "holder should be the acquiring member, displayed verbatim (Minted)" "alice"
    root |> getProp "fence" |> fun p -> p.GetInt64()
    |> Expect.equal "fence should be the claim's current fence" (int64 claim.Fence)
    root |> getProp "kind" |> fun p -> p.GetString()
    |> Expect.equal "kind should be the event kind passed in" "acquired"

  testCase "claim_changed reports holder=null for a released claim" <| fun () ->
    let state0 = joinAndClaim ()
    let claimId, claim0 = state0.Claims |> Map.toList |> List.exactlyOne
    let state1 = applyOk state0 (Bozzetto.Cohort.CohortCommand.ReleaseClaim(alice, claimId, claim0.Fence))
    let released = state1.Claims |> Map.find claimId
    let payload =
      Bozzetto.SseWriter.formatClaimChangedEvent jsonOpts "released" released
      |> fun sse -> sse.Split('\n') |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None) |> String.concat "\n"
    use doc = JsonDocument.Parse(payload)
    doc.RootElement.GetProperty("holder").ValueKind
    |> Expect.equal "released claim should carry a null holder, not a stale one" JsonValueKind.Null

  testCase "landing_changed round-trips a Blocked landing's typed blocker + next action" <| fun () ->
    let state0 = joinAndClaim ()
    let claimId, claim = state0.Claims |> Map.toList |> List.exactlyOne
    let state1 = applyOk state0 (Bozzetto.Cohort.CohortCommand.RequestLanding(alice, [ claimId, claim.Fence ], [ "abc123" ], "land it"))
    let landingId = state1.Landings |> Map.toList |> List.exactlyOne |> fst
    let (Bozzetto.Cohort.LandingId expectedLandingId) = landingId
    let state2 = applyOk state1 (Bozzetto.Cohort.CohortCommand.RebaseCompleted(landingId, Ok "def456"))
    let state3 = applyOk state2 (Bozzetto.Cohort.CohortCommand.AffectedComputed(landingId, [ Bozzetto.Cohort.TestId "t1" ]))
    let state4 = applyOk state3 (Bozzetto.Cohort.CohortCommand.TestsCompleted(landingId, [ Bozzetto.Cohort.TestId "t1" ]))
    let landing = state4.Landings |> Map.find landingId
    let payload =
      Bozzetto.SseWriter.formatLandingChangedEvent jsonOpts landing
      |> fun sse -> sse.Split('\n') |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None) |> String.concat "\n"
    use doc = JsonDocument.Parse(payload)
    let root = doc.RootElement
    root |> getProp "landingId" |> fun p -> p.GetString() |> Expect.equal "landingId should match" expectedLandingId
    root |> getProp "requester" |> fun p -> p.GetString() |> Expect.equal "requester should be the landing's requester" "alice"
    root |> getProp "state" |> fun p -> p.GetString() |> Expect.equal "state should be blocked" "blocked"
    let blocker = root |> getProp "blocker"
    blocker |> getProp "kind" |> fun p -> p.GetString() |> Expect.equal "blocker kind should be failing_tests" "failing_tests"
    blocker |> getProp "tests" |> fun p -> p.EnumerateArray() |> Seq.map (fun t -> t.GetString()) |> Seq.toList
    |> Expect.equal "blocker tests should list the failing test" [ "t1" ]
    let nextAction = root |> getProp "nextAction"
    nextAction |> getProp "kind" |> fun p -> p.GetString() |> Expect.equal "nextAction kind should be fix_tests" "fix_tests"
    nextAction |> getProp "tests" |> fun p -> p.EnumerateArray() |> Seq.map (fun t -> t.GetString()) |> Seq.toList
    |> Expect.equal "nextAction tests should list the failing test" [ "t1" ]

  testCase "landing_changed carries no blocker/nextAction for a non-blocked state" <| fun () ->
    let state0 = joinAndClaim ()
    let claimId, claim = state0.Claims |> Map.toList |> List.exactlyOne
    let state1 = applyOk state0 (Bozzetto.Cohort.CohortCommand.RequestLanding(alice, [ claimId, claim.Fence ], [ "abc123" ], "land it"))
    let landingId = state1.Landings |> Map.toList |> List.exactlyOne |> fst
    let landing = state1.Landings |> Map.find landingId
    let payload =
      Bozzetto.SseWriter.formatLandingChangedEvent jsonOpts landing
      |> fun sse -> sse.Split('\n') |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None) |> String.concat "\n"
    use doc = JsonDocument.Parse(payload)
    doc.RootElement.GetProperty("state").GetString()
    |> Expect.equal "landing was queued then immediately rebased (queue was empty)" "rebasing"
    doc.RootElement.GetProperty("blocker").ValueKind
    |> Expect.equal "blocker should be null when not blocked" JsonValueKind.Null
    doc.RootElement.GetProperty("nextAction").ValueKind
    |> Expect.equal "nextAction should be null when not blocked" JsonValueKind.Null

  testCase "cohort_matrix round-trips members/claims/tests/rows" <| fun () ->
    let state = joinAndClaim ()
    let head : Bozzetto.Cohort.LedgerHead<Bozzetto.MemberTable.MemberId> = { Seq = 3L<Bozzetto.Measures.ledgerSeq>; State = state }
    let snapshot : Bozzetto.Cohort.SessionSnapshot<Bozzetto.MemberTable.MemberId> =
      { Member = Some alice
        SessionId = "sess-1"
        Generation = 2L
        PassingTests = [ Bozzetto.Cohort.TestId "t1" ]
        FailingTests = [ Bozzetto.Cohort.TestId "t2" ]
        StaleTests = [] }
    let frame = Bozzetto.Cohort.project head [| snapshot |]
    let payload =
      Bozzetto.SseWriter.formatCohortMatrixEvent jsonOpts frame
      |> fun sse -> sse.Split('\n') |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None) |> String.concat "\n"
    use doc = JsonDocument.Parse(payload)
    let root = doc.RootElement
    root |> getProp "version" |> fun p -> p.GetInt64() |> Expect.equal "version should be the ledger seq the frame was projected from" 3L
    let members = root |> getProp "members" |> fun p -> p.EnumerateArray() |> Seq.toList
    members |> List.length |> Expect.equal "one member joined" 1
    let m0 = members.[0]
    m0 |> getProp "id" |> fun p -> p.GetString() |> Expect.equal "member id should be displayed verbatim" "alice"
    m0 |> getProp "role" |> fun p -> p.GetString() |> Expect.equal "role should be Implementer" "Implementer"
    m0 |> getProp "conductor" |> fun p -> p.GetBoolean() |> Expect.isTrue "the first joiner becomes conductor"
    let claims = root |> getProp "claims" |> fun p -> p.EnumerateArray() |> Seq.toList
    claims |> List.length |> Expect.equal "one claim acquired" 1
    let c0 = claims.[0]
    c0 |> getProp "scope" |> getProp "path" |> fun p -> p.GetString() |> Expect.equal "claim scope path" "src/Foo.fs"
    c0 |> getProp "holder" |> fun p -> p.GetString() |> Expect.equal "claim holder should be alice" "alice"
    let tests = root |> getProp "tests" |> fun p -> p.EnumerateArray() |> Seq.map (fun t -> t.GetString()) |> Seq.toList
    tests |> Expect.equal "tests should be the union of pass/fail/stale, sorted" [ "t1"; "t2" ]
    let rows = root |> getProp "rows" |> fun p -> p.EnumerateArray() |> Seq.toList
    rows |> List.length |> Expect.equal "one session row (alice's)" 1
    let r0 = rows.[0]
    r0 |> getProp "generation" |> fun p -> p.GetInt64() |> Expect.equal "row generation should match the snapshot" 2L
    let passBits = r0 |> getProp "pass" |> fun p -> p.EnumerateArray() |> Seq.map (fun b -> b.GetBoolean()) |> Seq.toList
    passBits |> Expect.equal "pass bitplane aligned with tests [t1;t2]" [ true; false ]
    let failBits = r0 |> getProp "fail" |> fun p -> p.EnumerateArray() |> Seq.map (fun b -> b.GetBoolean()) |> Seq.toList
    failBits |> Expect.equal "fail bitplane aligned with tests [t1;t2]" [ false; true ]

  testCase "cohort_matrix has no members/claims/rows for an empty cohort" <| fun () ->
    let head : Bozzetto.Cohort.LedgerHead<Bozzetto.MemberTable.MemberId> = { Seq = 0L<Bozzetto.Measures.ledgerSeq>; State = Bozzetto.Cohort.CohortState.empty () }
    let frame = Bozzetto.Cohort.project head [||]
    let payload =
      Bozzetto.SseWriter.formatCohortMatrixEvent jsonOpts frame
      |> fun sse -> sse.Split('\n') |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None) |> String.concat "\n"
    use doc = JsonDocument.Parse(payload)
    doc.RootElement.GetProperty("members").GetArrayLength() |> Expect.equal "no members yet" 0
    doc.RootElement.GetProperty("claims").GetArrayLength() |> Expect.equal "no claims yet" 0
    doc.RootElement.GetProperty("rows").GetArrayLength() |> Expect.equal "no session rows yet" 0

  // ── save_observed (claim early-warning, multi-agent vision §5.1) ──

  testCase "save_observed round-trips claimId/observer/holder/scope/path" <| fun () ->
    let state0 = joinTwoAndClaim ()
    let claimId, claim = state0.Claims |> Map.toList |> List.exactlyOne
    let (Bozzetto.Cohort.ClaimId expectedClaimId) = claimId
    let events =
      match Bozzetto.Cohort.decide clock noEntropy state0 (Bozzetto.Cohort.CohortCommand.ObserveSave(bob, "src/Foo.fs")) with
      | Ok(_, evs, _) -> evs
      | Error e -> failwithf "unexpected cohort decide error: %A" e
    events
    |> Expect.equal "bob saving into alice's claim should produce exactly one violation"
      [ Bozzetto.Cohort.CohortEvent.ClaimViolationObserved(claimId, bob, alice, "src/Foo.fs") ]
    let payload =
      Bozzetto.SseWriter.formatSaveObservedEvent jsonOpts claim bob alice "src/Foo.fs"
      |> fun sse -> sse.Split('\n') |> Array.choose (fun l -> if l.StartsWith("data: ") then Some (l.Substring 6) else None) |> String.concat "\n"
    use doc = JsonDocument.Parse(payload)
    let root = doc.RootElement
    root |> getProp "claimId" |> fun p -> p.GetString() |> Expect.equal "claimId should be the violated claim's id" expectedClaimId
    root |> getProp "observer" |> fun p -> p.GetString() |> Expect.equal "observer should be the saving member, displayed verbatim (Minted)" "bob"
    root |> getProp "holder" |> fun p -> p.GetString() |> Expect.equal "holder should be the claim's holder" "alice"
    let scope = root |> getProp "scope"
    scope |> getProp "kind" |> fun p -> p.GetString() |> Expect.equal "scope kind should be file" "file"
    scope |> getProp "path" |> fun p -> p.GetString() |> Expect.equal "scope path should be the claimed file" "src/Foo.fs"
    root |> getProp "path" |> fun p -> p.GetString() |> Expect.equal "path should be the saved path" "src/Foo.fs"

  testCase "save_observed mapping (McpServer.saveObservedRow) resolves the current claim by the event's claimId" <| fun () ->
    let state0 = joinTwoAndClaim ()
    let claimId, _ = state0.Claims |> Map.toList |> List.exactlyOne
    let ev = Bozzetto.Cohort.CohortEvent.ClaimViolationObserved(claimId, bob, alice, "src/Foo.fs")
    match Bozzetto.Server.McpServer.saveObservedRow state0 ev with
    | Some(claim, observer, holder, path) ->
      claim.Id |> Expect.equal "should resolve the claim the event names" claimId
      observer |> Expect.equal "observer should pass through from the event" bob
      holder |> Expect.equal "holder should pass through from the event" alice
      path |> Expect.equal "path should pass through from the event" "src/Foo.fs"
    | None -> failwith "expected Some for a ClaimViolationObserved whose claim exists in state"

  testCase "save_observed mapping ignores every non-violation cohort event" <| fun () ->
    let state0 = joinAndClaim ()
    let claimId, claim = state0.Claims |> Map.toList |> List.exactlyOne
    Bozzetto.Server.McpServer.saveObservedRow state0 (Bozzetto.Cohort.CohortEvent.ClaimAcquired(claimId, claim.Scope, alice, claim.Fence))
    |> Expect.isNone "a ClaimAcquired event should never produce a save_observed row"

  testCase "resolveSaveObserver (DaemonMode) maps a save to the cohort member bound to the saving session" <| fun () ->
    let sid = Bozzetto.WorkerProtocol.SessionId.newId ()
    let sessions = [ mkSessionInfo sid "/repo/checkout" ]
    let members = Map.ofList [ alice, mkAliceMember sid ]
    Bozzetto.Server.DaemonMode.resolveSaveObserver sessions members sid "/repo/checkout/src/Foo.fs"
    |> Expect.equal "should resolve alice + the repo-relative path" (Some(alice, "src/Foo.fs"))

  testCase "resolveSaveObserver (DaemonMode) is None for a session bound to no cohort member" <| fun () ->
    let sid = Bozzetto.WorkerProtocol.SessionId.newId ()
    let sessions = [ mkSessionInfo sid "/repo/checkout" ]
    Bozzetto.Server.DaemonMode.resolveSaveObserver sessions Map.empty sid "/repo/checkout/src/Foo.fs"
    |> Expect.isNone "a solo/non-cohort session's save must not be attributed to any member"

  testCase "resolveSaveObserver (DaemonMode) is None when the path falls outside the session's working directory" <| fun () ->
    let sid = Bozzetto.WorkerProtocol.SessionId.newId ()
    let sessions = [ mkSessionInfo sid "/repo/checkout" ]
    let members = Map.ofList [ alice, mkAliceMember sid ]
    Bozzetto.Server.DaemonMode.resolveSaveObserver sessions members sid "/somewhere/else/Foo.fs"
    |> Expect.isNone "a path outside the session's working directory has no repo-relative form"
]
