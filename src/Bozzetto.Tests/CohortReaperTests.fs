module Bozzetto.Tests.CohortReaperTests

open System
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Cohort
open Bozzetto.Server

// roast-7 §5: the daemon's cohort lease reaper. The pure DECIDE logic (Tick /
// RenewLease) lives in Cohort.fs; the daemon wires a 60s timer that renews the
// lease of each active Present member, then posts Tick to depart the silent
// ones. These tests guard BOTH halves:
//   1. DaemonMode.cohortMembersToRenew — the selection (Present ∧ active).
//   2. The renew-then-reap SEQUENCE against the real Cohort.decide — the exact
//      property the earlier (reverted) attempt broke: an ACTIVE member must
//      never be reaped, however long the daemon runs.

let private entropy : Cohort.Entropy = [| 1uy |]

/// Apply one command through the real decide, ignoring refusals (a RenewLease
/// of a non-member is a harmless no-op, exactly as in production).
let inline private stepWith (seed: byte) (clock: DateTime) (st: CohortState<'m>) (cmd: CohortCommand<'m>) : CohortState<'m> =
  match Cohort.decide clock [| seed |] st cmd with
  | Result.Ok(st', _, _) -> st'
  | Result.Error err -> failtestf "expected setup command %A to succeed, got %A" cmd err

let inline private step (clock: DateTime) (st: CohortState<'m>) (cmd: CohortCommand<'m>) : CohortState<'m> =
  match Cohort.decide clock entropy st cmd with
  | Result.Ok(st', _, _) -> st'
  | Result.Error _ -> st

/// The daemon's reaper tick as a pure sequence over the real decide: renew the
/// active Present members, then Tick. Mirrors DaemonMode.cohortReaperCallback.
let private reaperTick (isActive: string -> bool) (clock: DateTime) (st: CohortState<string>) : CohortState<string> =
  let renewed =
    st.Members
    |> Map.toList
    |> List.choose (fun (m, r) -> match r.Presence with | MemberPresence.Present when isActive m -> Some m | _ -> None)
    |> List.fold (fun s who -> step clock s (CohortCommand.RenewLease who)) st
  step clock renewed CohortCommand.Tick

let private presenceOf who (st: CohortState<string>) =
  match Map.tryFind who st.Members with
  | Some r -> Some r.Presence
  | None -> None

let private heldClaims (st: CohortState<string>) =
  st.Claims |> Map.toList |> List.filter (fun (_, c) -> match c.State with | ClaimState.Held _ -> true | _ -> false) |> List.length

[<Tests>]
let tests =
  testList "Cohort lease reaper (roast-7 §5)" [

    testCase "cohortMembersToRenew selects exactly the Present, active members"
    <| fun _ ->
      // Build a cohort with a Present active member, a Present idle member, and
      // a Departed one — only the Present+active member should be renewed.
      let t0 = DateTime(2026, 1, 1, 12, 0, 0)
      let s =
        (CohortState.empty () : CohortState<MemberTable.MemberId>)
        |> fun s -> step t0 s (CohortCommand.Join(MemberTable.MemberId.Minted "active", JoinableRole.Implementer, None))
        |> fun s -> step t0 s (CohortCommand.Join(MemberTable.MemberId.Minted "idle", JoinableRole.Implementer, None))
        |> fun s -> step t0 s (CohortCommand.Join(MemberTable.MemberId.Minted "left", JoinableRole.Implementer, None))
        // "left" departs (its own tick past the lease, in isolation)
        |> fun s -> step (t0 + Cohort.leaseWindow + TimeSpan.FromMinutes 1.0) s CohortCommand.Tick
      // After that Tick everyone silent is Departed. Re-join active + idle fresh.
      let now = t0 + Cohort.leaseWindow + TimeSpan.FromMinutes 2.0
      let s2 =
        s
        |> fun s -> step now s (CohortCommand.Join(MemberTable.MemberId.Minted "active", JoinableRole.Implementer, None))
        |> fun s -> step now s (CohortCommand.Join(MemberTable.MemberId.Minted "idle", JoinableRole.Implementer, None))
      let isActive (m: MemberTable.MemberId) = MemberTable.MemberId.display m = "active"
      DaemonMode.cohortMembersToRenew isActive s2.Members
      |> Expect.equal "only the Present, active member is renewed" [ MemberTable.MemberId.Minted "active" ]

    testCase "an ACTIVE member is never reaped, however long the daemon runs — the property the earlier attempt broke"
    <| fun _ ->
      let t0 = DateTime(2026, 1, 1, 12, 0, 0)
      let start =
        (CohortState.empty () : CohortState<string>)
        |> fun s -> step t0 s (CohortCommand.Join("busy", JoinableRole.Implementer, Some "sA"))
        |> fun s -> step t0 s (CohortCommand.Join("idle", JoinableRole.Implementer, Some "sB"))
        |> fun s -> stepWith 1uy t0 s (CohortCommand.AcquireClaim("busy", ClaimScope.File "/repo/a.fs", "editing"))
        |> fun s -> stepWith 2uy t0 s (CohortCommand.AcquireClaim("idle", ClaimScope.File "/repo/b.fs", "editing"))
      // 90 minutes of 60s ticks (3x the 30-min lease). "busy" active every tick.
      let final =
        [ 1 .. 90 ]
        |> List.fold (fun st minute -> reaperTick (fun who -> who = "busy") (t0.AddMinutes(float minute)) st) start
      presenceOf "busy" final |> Expect.equal "busy stays Present — renewed every tick" (Some MemberPresence.Present)
      // idle departs at the lease window (minute 30). Its orphaned claim keeps
      // blocking until reassigned or reclaimed (Bozzetto #24), so neither the
      // claim nor the seat it names ages out.
      match presenceOf "idle" final with
      | Some (MemberPresence.Departed _) -> ()
      | other -> failtestf "idle's seat stays Departed while its orphan names it, was %A" other
      heldClaims final |> Expect.equal "only busy's claim is still Held" 1
      final.Claims |> Map.count |> Expect.equal "idle's orphaned claim is still in state, blocking" 2
      // Midway (minute 45 — departed at 30, inside the retention window) the
      // seat is still visible as Departed, so the conductor can still act on it.
      let midway =
        [ 1 .. 45 ]
        |> List.fold (fun st minute -> reaperTick (fun who -> who = "busy") (t0.AddMinutes(float minute)) st) start
      match presenceOf "idle" midway with
      | Some (MemberPresence.Departed _) -> ()
      | other -> failtestf "idle should still be Departed inside the retention window, was %A" other

    testCase "an idle member is reaped exactly at the lease window, not before"
    <| fun _ ->
      let t0 = DateTime(2026, 1, 1, 12, 0, 0)
      let start = step t0 (CohortState.empty () : CohortState<string>) (CohortCommand.Join("idle", JoinableRole.Implementer, None))
      // One minute BEFORE the lease elapses: still Present.
      let before = reaperTick (fun _ -> false) (t0 + Cohort.leaseWindow - TimeSpan.FromMinutes 1.0) start
      presenceOf "idle" before |> Expect.equal "still Present just before the lease" (Some MemberPresence.Present)
      // At the lease window: reaped.
      let at = reaperTick (fun _ -> false) (t0 + Cohort.leaseWindow) start
      match presenceOf "idle" at with
      | Some (MemberPresence.Departed _) -> ()
      | other -> failtestf "idle should be Departed at the lease window, was %A" other
  ]


// ── Bozzetto #24: lease waits are presence; orphaned claims block ─────────

let private mcpMember (sid: string) = MemberTable.MemberId.Mcp sid

/// The daemon's reaper tick over MemberIds, with presence read from the REAL
/// activity tracker exactly as DaemonMode.cohortReaperCallback reads it.
let private trackedReaperTick (tracker: AgentActivityTracker.Tracker) (now: DateTime) (st: CohortState<MemberTable.MemberId>) =
  let fresh =
    AgentActivityTracker.getActivePresences tracker None (TimeSpan.FromMinutes 2.0) now
    |> List.map (fun p -> p.AgentName)
    |> Set.ofList
  let isActive (m: MemberTable.MemberId) = Set.contains (MemberTable.MemberId.display m) fresh
  let renewed =
    DaemonMode.cohortMembersToRenew isActive st.Members
    |> List.fold (fun s who -> step now s (CohortCommand.RenewLease who)) st
  step now renewed CohortCommand.Tick

let private claimStateOf (cid: ClaimId) (st: CohortState<'m>) = st.Claims |> Map.tryFind cid |> Option.map _.State

let private acquireOk (clock: DateTime) (n: byte) (st: CohortState<'m>) (who: 'm) (scope: ClaimScope) =
  match Cohort.decide clock [| n |] st (CohortCommand.AcquireClaim(who, scope, "editing")) with
  | Result.Ok(st', [ CohortEvent.ClaimAcquired(cid, _, _, _) ], _) -> st', cid
  | other -> failtestf "expected the claim to be granted, got %A" other

[<Tests>]
let leaseWaitPresenceTests =
  testList "Cohort membership during a lease wait (Bozzetto #24)" [

    testCase "a member that only re-polls a lease for longer than the membership TTL keeps its claims" <| fun _ ->
      let t0 = DateTime(2026, 11, 8, 1, 0, 0, DateTimeKind.Utc)
      let waiter = mcpMember "waiting-session"
      let silent = mcpMember "silent-session"
      let tracker = AgentActivityTracker.create ()
      let start =
        (CohortState.empty () : CohortState<MemberTable.MemberId>)
        |> fun s -> step t0 s (CohortCommand.Join(waiter, JoinableRole.Implementer, None))
        |> fun s -> step t0 s (CohortCommand.Join(silent, JoinableRole.Implementer, None))
      let s1, waiterClaim = acquireOk t0 1uy start waiter (ClaimScope.Project "Bozzetto/src/Bozzetto.Core/Bozzetto.Core.fsproj")
      let s2, silentClaim = acquireOk t0 2uy s1 silent (ClaimScope.Project "Farscape/src/Farscape.Core/Farscape.Core.fsproj")
      // 45 minutes (1.5x the membership TTL): the waiter's ONLY traffic is a
      // test-suite lease poll every 30s; the reaper runs every 60s.
      let final =
        [ 1 .. 90 ]
        |> List.fold (fun st half ->
          let now = t0.AddSeconds(float half * 30.0)
          Bozzetto.McpTools.recordLeasePoll tracker waiter ExpensiveWorkLease.Kind.TestSuiteRun now
          if half % 2 = 0 then trackedReaperTick tracker now st else st) s2
      final.Members.[waiter].Presence |> Expect.equal "the polling member stays present past the TTL" MemberPresence.Present
      claimStateOf waiterClaim final |> Expect.equal "its claim is still held by it" (Some (ClaimState.Held waiter))
      // Negative control: the same reaper departs a member with no polls.
      match claimStateOf silentClaim final with
      | Some (ClaimState.Orphaned(previous, _)) -> previous |> Expect.equal "the silent member's claim orphans" silent
      | other -> failtestf "the silent member's claim should be orphaned, got %A" other
  ]

[<Tests>]
let orphanedClaimTests =
  testList "Orphaned claims block until reassigned or reclaimed (Bozzetto #24)" [

    let t0 = DateTime(2026, 11, 8, 1, 0, 0, DateTimeKind.Utc)
    let scope = ClaimScope.Project "Bozzetto/src/Bozzetto.Core/Bozzetto.Core.fsproj"
    // conductor joins first; holder acquires, then departs (its claim orphans).
    let orphanedScene () =
      let s0 =
        (CohortState.empty () : CohortState<string>)
        |> fun s -> step t0 s (CohortCommand.Join("conductor", JoinableRole.Implementer, None))
        |> fun s -> step t0 s (CohortCommand.Join("holder", JoinableRole.Implementer, None))
        |> fun s -> step t0 s (CohortCommand.Join("other", JoinableRole.Implementer, None))
      let s1, cid = acquireOk t0 7uy s0 "holder" scope
      let s2 = step t0 s1 (CohortCommand.Depart "holder")
      match claimStateOf cid s2 with
      | Some (ClaimState.Orphaned("holder", _)) -> s2, cid
      | other -> failtestf "scene should orphan the holder's claim, got %A" other

    testCase "an orphaned claim blocks another member's acquire, naming the orphan" <| fun _ ->
      let s, cid = orphanedScene ()
      match Cohort.decide t0 [| 9uy |] s (CohortCommand.AcquireClaim("other", ClaimScope.File "Bozzetto/src/Bozzetto.Core/Cohort.fs", "editing")) with
      | Result.Error(CohortError.OrphanedClaimConflict(_, blocking, previous)) ->
        blocking |> Expect.equal "the refusal names the orphaned claim" cid
        previous |> Expect.equal "and the member that last held it" "holder"
      | other -> failtestf "an overlapping acquire must be refused by the orphan, got %A" other

    testCase "the original holder reclaims with the claim's last fence, and is refused with a wrong one" <| fun _ ->
      let s, cid = orphanedScene ()
      let lastFence = s.Claims.[cid].Fence
      match Cohort.decide t0 [| 9uy |] s (CohortCommand.ReclaimClaim("holder", cid, lastFence)) with
      | Result.Error(CohortError.MemberNotPresent "holder") -> ()
      | other -> failtestf "a departed holder must rejoin before reclaiming, got %A" other
      let rejoined = step t0 s (CohortCommand.Join("holder", JoinableRole.Implementer, None))
      match Cohort.decide t0 [| 9uy |] rejoined (CohortCommand.ReclaimClaim("holder", cid, lastFence - 1L<Bozzetto.Measures.fence>)) with
      | Result.Error(CohortError.StaleClaimFence(c, presented, current)) ->
        (c, presented, current) |> Expect.equal "a wrong fence is refused with both fences named" (cid, lastFence - 1L<Bozzetto.Measures.fence>, lastFence)
      | other -> failtestf "a wrong fence must be refused, got %A" other
      match Cohort.decide t0 [| 9uy |] rejoined (CohortCommand.ReclaimClaim("holder", cid, lastFence)) with
      | Result.Ok(after, [ CohortEvent.ClaimReassigned(c, "holder", fence) ], _) ->
        c |> Expect.equal "the same claim comes back" cid
        claimStateOf cid after |> Expect.equal "Held by its original holder again" (Some (ClaimState.Held "holder"))
        (fence > lastFence) |> Expect.isTrue "with a newer fence"
      | other -> failtestf "the original holder's reclaim must succeed, got %A" other

    testCase "anyone other than the original holder is refused a reclaim, even with the right fence" <| fun _ ->
      let s, cid = orphanedScene ()
      for who in [ "other"; "conductor" ] do
        match Cohort.decide t0 [| 9uy |] s (CohortCommand.ReclaimClaim(who, cid, s.Claims.[cid].Fence)) with
        | Result.Error(CohortError.NotClaimHolder(c, requester)) ->
          (c, requester) |> Expect.equal "refused as not the claim's holder" (cid, who)
        | other -> failtestf "%s must not reclaim another member's orphan, got %A" who other

    testCase "conductor reassignment of an orphaned claim still works" <| fun _ ->
      let s, cid = orphanedScene ()
      match Cohort.decide t0 [| 9uy |] s (CohortCommand.ReassignClaim("conductor", cid, "other")) with
      | Result.Ok(after, [ CohortEvent.ClaimReassigned(c, "other", _) ], _) ->
        c |> Expect.equal "the orphan is reassigned" cid
        claimStateOf cid after |> Expect.equal "Held by the recipient" (Some (ClaimState.Held "other"))
        let rejoined = step t0 after (CohortCommand.Join("holder", JoinableRole.Implementer, None))
        match Cohort.decide t0 [| 9uy |] rejoined (CohortCommand.ReclaimClaim("holder", cid, after.Claims.[cid].Fence)) with
        | Result.Error(CohortError.ClaimNotOrphaned c') -> c' |> Expect.equal "a reassigned claim is no longer reclaimable" cid
        | other -> failtestf "reclaiming a reassigned claim must be refused, got %A" other
      | other -> failtestf "the conductor's reassignment must succeed, got %A" other
  ]

// ── #34/#13 promotion, #12 named claims, #13 dead-but-open, #34 orphan expiry ──

let private silentLogger =
  { new Bozzetto.Utils.ILogger with
      member _.LogInfo _ = ()
      member _.LogDebug _ = ()
      member _.LogWarning _ = ()
      member _.LogError _ = () }

let private ledgerSeq (n: int64) = LanguagePrimitives.Int64WithMeasure<Bozzetto.Measures.ledgerSeq> n
let private fenceOf (n: int64) = LanguagePrimitives.Int64WithMeasure<Bozzetto.Measures.fence> n

// Re-decide every recorded entry in order: each must be accepted and must
// re-derive exactly its recorded events. Returns the first divergence, if any,
// so a ledger can never be "replayed" by silently skipping an entry.
let private replayDivergence
    (decideWith: DateTime -> Cohort.Entropy -> CohortState<'m> -> CohortCommand<'m> -> Result<CohortState<'m> * CohortEvent<'m> list * CohortEffect<'m> list, CohortError<'m>>)
    (entries: LedgerEntry<'m> list) =
  ((CohortState.empty (), None), entries)
  ||> List.fold (fun (state, divergence) entry ->
    match divergence with
    | Some _ -> state, divergence
    | None ->
      match decideWith entry.Clock entry.Entropy state entry.Command with
      | Result.Ok(next, events, _) when events = entry.Events -> next, None
      | Result.Ok(_, events, _) -> state, Some(entry.Seq, sprintf "events %A <> recorded %A" events entry.Events)
      | Result.Error err -> state, Some(entry.Seq, sprintf "refused %A" err))
  |> snd

// The pre-#34 ledger the v2 audit named, with the events base 08844e71 recorded
// for it: a departed-but-bound conductor reassigns its own orphan to b; then the
// conductor rejoins while b is present and sets the integration head.
let private legacyLedger () : LedgerEntry<string> list =
  let t0 = DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc)
  let scope = ClaimScope.File "src/Legacy.fs"
  let cid = ClaimId "c-03"
  let at s = t0.AddSeconds(float s)
  let entry seq secs entropy cmd events = { Seq = ledgerSeq seq; Clock = at secs; Entropy = [| entropy |]; Command = cmd; Events = events }
  [ entry 0L 0 0uy (CohortCommand.Join("a", JoinableRole.Implementer, None)) [ CohortEvent.MemberJoined("a", JoinableRole.Implementer, None); CohortEvent.ConductorBound "a" ]
    entry 1L 1 1uy (CohortCommand.Join("b", JoinableRole.Implementer, None)) [ CohortEvent.MemberJoined("b", JoinableRole.Implementer, None) ]
    entry 2L 2 3uy (CohortCommand.AcquireClaim("a", scope, "legacy")) [ CohortEvent.ClaimAcquired(cid, scope, "a", fenceOf 1L) ]
    entry 3L 3 4uy (CohortCommand.Depart "a") [ CohortEvent.MemberDeparted("a", at 3); CohortEvent.ClaimOrphaned(cid, "a", fenceOf 2L) ]
    entry 4L 4 5uy (CohortCommand.ReassignClaim("a", cid, "b")) [ CohortEvent.ClaimReassigned(cid, "b", fenceOf 3L) ]
    entry 5L 5 6uy (CohortCommand.RenewLease "b") [ CohortEvent.LeaseRenewed "b" ]
    entry 6L 6 7uy CohortCommand.Tick []
    entry 7L 7 8uy (CohortCommand.Join("a", JoinableRole.Implementer, None)) [ CohortEvent.MemberJoined("a", JoinableRole.Implementer, None) ]
    entry 8L 8 9uy (CohortCommand.SetIntegrationHead("a", "sha-legacy")) [ CohortEvent.IntegrationConfigured "sha-legacy" ] ]

// The v2 shape the audits rejected, frozen as a fault-injection twin: decide
// itself promotes after every Depart. Never wired into a product path.
let private implicitPromotionTwin clock entropy (state: CohortState<'m>) (cmd: CohortCommand<'m>) =
  match Cohort.decide clock entropy state cmd, cmd with
  | Result.Ok(next, events, effects), CohortCommand.Depart _ ->
    match Cohort.decide clock entropy next CohortCommand.PromoteConductor with
    | Result.Ok(promoted, promotionEvents, _) -> Result.Ok(promoted, events @ promotionEvents, effects)
    | Result.Error _ -> Result.Ok(next, events, effects)
  | result, _ -> result

let private startOwner (ledger: Bozzetto.Features.CohortLedger.LedgerPort<MemberTable.MemberId>) (clock: DateTime ref) =
  let counter = ref 0
  let entropy () = counter.Value <- counter.Value + 1; BitConverter.GetBytes counter.Value
  Bozzetto.Features.CohortOwner.start silentLogger ledger (fun () -> clock.Value) entropy (fun _ -> ([], [], [], 0L))

let private committed (result: Result<CohortEvent<'m> list * CohortEffect<'m> list, CohortError<'m>>) =
  match result with
  | Result.Ok(events, _) -> events
  | Result.Error err -> failtestf "expected the command to be accepted, got %A" err

[<Tests>]
let conductorPromotionIdentityAndExpiryTests =
  testList "Cohort conductor promotion, identity-bound claims, dead-but-open holders, and orphan expiry" [

    testCase "a pre-promotion ledger replays exactly as base decided it: every entry accepted with its recorded events" <| fun _ ->
      let ledger = legacyLedger ()
      replayDivergence Cohort.decide ledger |> Expect.isNone "no entry is refused or re-derives different events"
      let state = Cohort.replay ledger
      state.Claims.[ClaimId "c-03"].State |> Expect.equal "the departed-but-bound conductor's reassignment still lands" (ClaimState.Held "b")
      state.Conductor |> Expect.equal "decide never moves the seat by itself" (Some "a")
      state.IntegrationHead |> Expect.equal "the rejoined conductor's head change is not lost" "sha-legacy"

    testCase "fault injection: implicit promotion inside decide is caught as a legacy replay divergence" <| fun _ ->
      match replayDivergence implicitPromotionTwin (legacyLedger ()) with
      | Some(seq, reason) ->
        seq |> Expect.equal "the twin diverges at the departing conductor's own entry" (ledgerSeq 3L)
        reason |> Expect.stringContains "the divergence names the extra event" "ConductorPromoted"
      | None -> failtest "the implicit-promotion twin must be detected"

    testCase "Join, Depart, RenewLease and Tick never promote; PromoteConductor alone moves a departed seat" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let s0 =
        (CohortState.empty () : CohortState<string>)
        |> fun s -> step t0 s (CohortCommand.Join("c0", JoinableRole.Implementer, None))
        |> fun s -> step (t0.AddMinutes 1.0) s (CohortCommand.Join("b", JoinableRole.Implementer, None))
      for cmd in [ CohortCommand.Depart "c0"; CohortCommand.RenewLease "b"; CohortCommand.Join("x", JoinableRole.Implementer, None); CohortCommand.Tick ] do
        match Cohort.decide (t0.AddMinutes 2.0) entropy (step (t0.AddMinutes 2.0) s0 (CohortCommand.Depart "c0")) cmd with
        | Result.Ok(next, events, _) ->
          next.Conductor |> Expect.equal (sprintf "%A leaves the departed seat bound" cmd) (Some "c0")
          events |> List.exists (function CohortEvent.ConductorPromoted _ -> true | _ -> false) |> Expect.isFalse "and emits no promotion"
        | Result.Error(CohortError.MemberNotPresent "c0") -> ()
        | other -> failtestf "unexpected %A for %A" other cmd

    testCase "PromoteConductor takes the oldest Present member and breaks equal join times by member ID" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let state =
        (CohortState.empty () : CohortState<string>)
        |> fun s -> step t0 s (CohortCommand.Join("c0", JoinableRole.Implementer, None))
        |> fun s -> step (t0.AddMinutes 1.0) s (CohortCommand.Join("a-observer", JoinableRole.Observer, None))
        |> fun s -> step (t0.AddMinutes 1.0) s (CohortCommand.Join("z-verifier", JoinableRole.Verifier, None))
        |> fun s -> step (t0.AddMinutes 2.0) s (CohortCommand.Join("0-implementer", JoinableRole.Implementer, None))
        |> fun s -> step (t0.AddMinutes 3.0) s (CohortCommand.Depart "c0")
      match Cohort.decide (t0.AddMinutes 4.0) entropy state CohortCommand.PromoteConductor with
      | Result.Ok(promoted, events, effects) ->
        events |> Expect.equal "the older observer wins the tie with the verifier" [ CohortEvent.ConductorPromoted("c0", "a-observer") ]
        effects |> Expect.isEmpty "promotion has no shell effects"
        Authority.present "a-observer" promoted |> Expect.equal "the observer has conductor authority" (Authority.Conductor "a-observer")
      | other -> failtestf "expected PromoteConductor to fill the vacant seat, got %A" other

    testCase "PromoteConductor is refused with ConductorNotPromotable when the seat is unbound, held by a present member, or has no present candidate" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let present = step t0 (CohortState.empty () : CohortState<string>) (CohortCommand.Join("c0", JoinableRole.Implementer, None))
      let alone = step (t0.AddMinutes 1.0) present (CohortCommand.Depart "c0")
      for state, expected in [ CohortState.empty (), None; present, Some "c0"; alone, Some "c0" ] do
        match Cohort.decide (t0.AddMinutes 2.0) entropy state CohortCommand.PromoteConductor with
        | Result.Error(CohortError.ConductorNotPromotable conductor) -> conductor |> Expect.equal "the refusal names the seat as it stands" expected
        | other -> failtestf "expected ConductorNotPromotable, got %A" other

    testTask "the owner records PromoteConductor as its own ledger entry after a conductor departs, and the reply carries it" {
      let clock = ref (DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc))
      let ledger = Bozzetto.Features.CohortLedger.InMemory.create<MemberTable.MemberId> ()
      use owner = startOwner ledger clock
      let a, b = mcpMember "a", mcpMember "b"
      let! _ = owner.Commit(CohortCommand.Join(a, JoinableRole.Implementer, None))
      clock.Value <- clock.Value.AddMinutes 1.0
      let! joinedB = owner.Commit(CohortCommand.JoinNamed(b, JoinableRole.Implementer, None, "beta"))
      committed joinedB |> List.exists (function CohortEvent.ConductorPromoted _ -> true | _ -> false) |> Expect.isFalse "a present conductor is never replaced"
      ledger.ReadAll () |> List.length |> Expect.equal "negative control: no promotion entry while the seat is held" 2
      let! departed = owner.Commit(CohortCommand.Depart a)
      committed departed |> List.last |> Expect.equal "the caller sees the promotion" (CohortEvent.ConductorPromoted(a, b))
      let entries = ledger.ReadAll ()
      entries |> List.map (fun e -> e.Command) |> List.skip 2 |> Expect.equal "Depart and PromoteConductor are separate entries" [ CohortCommand.Depart a; CohortCommand.PromoteConductor ]
      entries.[2].Events |> List.exists (function CohortEvent.ConductorPromoted _ -> true | _ -> false) |> Expect.isFalse "the Depart entry records only what Depart decided"
      entries |> List.map (fun e -> e.Seq) |> Expect.equal "seq stays dense" [ ledgerSeq 0L; ledgerSeq 1L; ledgerSeq 2L; ledgerSeq 3L ]
      replayDivergence Cohort.decide entries |> Expect.isNone "the owner's ledger replays without divergence"
      (Cohort.replay entries).Conductor |> Expect.equal "replay reconstructs the promoted seat" (Some b)
      owner.ReadCohortState().Conductor |> Expect.equal "the live owner agrees" (Some b)
    }

    testTask "the owner promotes the next joiner when nobody was present, and promotes after a legacy ledger replays unchanged" {
      let clock = ref (DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc))
      let first, second, third = mcpMember "first", mcpMember "second", mcpMember "third"
      // A ledger written before promotion existed: the conductor departed while another member stayed.
      let ledger = Bozzetto.Features.CohortLedger.InMemory.create<MemberTable.MemberId> ()
      [ CohortCommand.Join(first, JoinableRole.Implementer, None); CohortCommand.Join(second, JoinableRole.Observer, None); CohortCommand.Depart first ]
      |> List.iteri (fun i cmd ->
        match Cohort.decide clock.Value [| byte i |] (Cohort.replay (ledger.ReadAll ())) cmd with
        | Result.Ok(_, events, _) -> ledger.AppendBatch [ { Seq = ledgerSeq (int64 i); Clock = clock.Value; Entropy = [| byte i |]; Command = cmd; Events = events } ] |> Expect.equal "legacy seed was appended" (Ok ())
        | Result.Error err -> failtestf "legacy seed refused %A" err)
      use owner = startOwner ledger clock
      owner.ReadCohortState().Conductor |> Expect.equal "startup replay leaves the legacy seat exactly as recorded" (Some first)
      let! ticked = owner.Commit CohortCommand.Tick
      committed ticked |> Expect.equal "the next accepted entry is followed by the promotion" [ CohortEvent.ConductorPromoted(first, second) ]
      // Sole member departs: no candidate, so no promotion entry; the next joiner takes the seat.
      let! _ = owner.Commit(CohortCommand.Depart second)
      let countBefore = ledger.ReadAll () |> List.length
      clock.Value <- clock.Value.AddMinutes 1.0
      let! joined = owner.Commit(CohortCommand.JoinNamed(third, JoinableRole.Implementer, None, "gamma"))
      committed joined |> Expect.equal "the joiner is promoted in the same step" [ CohortEvent.MemberJoined(third, JoinableRole.Implementer, None); CohortEvent.ConductorPromoted(second, third) ]
      ledger.ReadAll () |> List.length |> Expect.equal "join plus its promotion entry" (countBefore + 2)
      replayDivergence Cohort.decide (ledger.ReadAll ()) |> Expect.isNone "the mixed legacy/new ledger replays without divergence"
    }

    testCase "a six-entry legacy ledger still reclaims its orphan after Tick at 25h" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let scope = ClaimScope.File "src/Legacy.fs"
      let cid = ClaimId "c-03"
      let departAt = t0.AddMinutes 1.0
      let tickAt = departAt.AddHours 25.0
      let entry seq clock entropy command events =
        { Seq = ledgerSeq seq; Clock = clock; Entropy = [| entropy |]; Command = command; Events = events }
      let entries =
        [ entry 0L t0 0uy (CohortCommand.Join("a", JoinableRole.Implementer, None))
            [ CohortEvent.MemberJoined("a", JoinableRole.Implementer, None); CohortEvent.ConductorBound "a" ]
          entry 1L t0 3uy (CohortCommand.AcquireClaim("a", scope, "legacy"))
            [ CohortEvent.ClaimAcquired(cid, scope, "a", fenceOf 1L) ]
          entry 2L departAt 4uy (CohortCommand.Depart "a")
            [ CohortEvent.MemberDeparted("a", departAt); CohortEvent.ClaimOrphaned(cid, "a", fenceOf 2L) ]
          entry 3L tickAt 5uy CohortCommand.Tick []
          entry 4L tickAt 6uy (CohortCommand.Join("a", JoinableRole.Implementer, None))
            [ CohortEvent.MemberJoined("a", JoinableRole.Implementer, None) ]
          entry 5L tickAt 7uy (CohortCommand.ReclaimClaim("a", cid, fenceOf 2L))
            [ CohortEvent.ClaimReassigned(cid, "a", fenceOf 3L) ] ]
      replayDivergence Cohort.decide entries |> Expect.isNone "all six commands reproduce their base events"
      let state = Cohort.replay entries
      state.Conductor |> Expect.equal "the original conductor is bound" (Some "a")
      state.Claims[cid].State |> Expect.equal "the claim ends Held" (ClaimState.Held "a")
      state.NextFence |> Expect.equal "the final fence includes the reclaim" (fenceOf 3L)

    testTask "the owner expires at 24h and preserves a departed holder's claim while its name is Present" {
      let clock = ref (DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc))
      let ledger = Bozzetto.Features.CohortLedger.InMemory.create<MemberTable.MemberId> ()
      use owner = startOwner ledger clock
      let lead, gone, former, returning = mcpMember "lead", mcpMember "gone", mcpMember "former", mcpMember "returning"
      let apply cmd = task {
        let! result = owner.Commit cmd
        return committed result
      }
      let! _ = apply (CohortCommand.JoinNamed(lead, JoinableRole.Implementer, None, "lead"))
      let! _ = apply (CohortCommand.JoinNamed(gone, JoinableRole.Implementer, None, "gone"))
      let! _ = apply (CohortCommand.JoinNamed(former, JoinableRole.Implementer, None, "protected"))
      let! _ = apply (CohortCommand.AcquireClaim(gone, ClaimScope.File "src/Stale.fs", "stale"))
      let! _ = apply (CohortCommand.AcquireClaim(former, ClaimScope.File "src/Protected.fs", "protected"))
      clock.Value <- clock.Value.AddMinutes 1.0
      let departure = clock.Value
      let! _ = apply (CohortCommand.Depart gone)
      let! _ = apply (CohortCommand.Depart former)
      clock.Value <- departure + Cohort.orphanClaimTtl - TimeSpan.FromMinutes 1.0
      let! _ = apply (CohortCommand.RenewLease lead)
      let! _ = apply (CohortCommand.JoinNamed(returning, JoinableRole.Implementer, None, "protected"))
      let! _ = apply CohortCommand.Tick
      owner.ReadCohortState().Claims.Count |> Expect.equal "both orphans survive at 23h59m" 2
      let atTtl = departure + Cohort.orphanClaimTtl
      clock.Value <- atTtl
      let! events = apply CohortCommand.Tick
      let entries = ledger.ReadAll ()
      entries |> List.rev |> List.take 2 |> List.rev |> List.map (fun e -> e.Command)
      |> Expect.equal "Tick and expiry are separate, adjacent rows" [ CohortCommand.Tick; CohortCommand.ExpireOrphanedClaims ]
      entries |> List.rev |> List.item 1 |> fun tick -> tick.Events |> Expect.isEmpty "legacy Tick emits no orphan expiry"
      let state = owner.ReadCohortState()
      state.Members |> Map.containsKey gone |> Expect.isFalse "expiry purges the departed holder when its last claim is removed"
      state.Claims |> Map.toList |> List.map (fun (_, claim) -> claim.HolderName)
      |> Expect.equal "a Present member with the same name protects the other orphan" [ Some "protected" ]
      events |> List.choose (function CohortEvent.ClaimPruned(_, reason) -> Some reason | _ -> None)
      |> Expect.equal "the owner reply includes the exact age-based prune reason" [ ClaimPruneReason.OrphanedPastRetention departure ]
      let! refused = owner.Commit CohortCommand.ExpireOrphanedClaims
      refused |> Expect.equal "manual expiry refuses with its exact reason when nothing is eligible" (Error CohortError.NoExpiredOrphanedClaims)
      ledger.ReadAll () |> Expect.equal "the refusal produces no row" entries
      let! _ = apply (CohortCommand.Depart returning)
      owner.ReadCohortState().Claims |> Map.isEmpty |> Expect.isTrue "the next accepted command expires the unprotected claim"
      replayDivergence Cohort.decide (ledger.ReadAll ()) |> Expect.isNone "explicit expiry reconstructs from the ledger"
    }

    testCase "the original member ID cannot reclaim after rejoining under a different name" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let holder = mcpMember "same-connection"
      let state = step t0 (CohortState.empty ()) (CohortCommand.JoinNamed(holder, JoinableRole.Implementer, None, "Agent"))
      let held, cid = acquireOk t0 53uy state holder (ClaimScope.File "src/Name.fs")
      let departed = step t0 held (CohortCommand.Depart holder)
      let renamed = step t0 departed (CohortCommand.JoinNamed(holder, JoinableRole.Implementer, None, "agent"))
      Cohort.decide t0 entropy renamed (CohortCommand.ReclaimClaim(holder, cid, renamed.Claims[cid].Fence))
      |> Expect.equal "the recorded HolderName is case-sensitive even for the same member ID" (Error(CohortError.NotClaimHolder(cid, holder)))

    testCase "claim bound to joined agent name can be released and reclaimed from a new MCP connection with exact fence" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let conn1 = mcpMember "conn-1"
      let conn2 = mcpMember "conn-2"
      let otherConn = mcpMember "conn-other"
      let departedSame = mcpMember "conn-dep"
      let s0 =
        (CohortState.empty () : CohortState<MemberTable.MemberId>)
        |> fun s -> step t0 s (CohortCommand.JoinNamed(conn1, JoinableRole.Implementer, None, "antigravity"))
        |> fun s -> step (t0.AddSeconds 1.0) s (CohortCommand.JoinNamed(otherConn, JoinableRole.Implementer, None, "codex"))
        |> fun s -> step (t0.AddSeconds 2.0) s (CohortCommand.JoinNamed(departedSame, JoinableRole.Implementer, None, "antigravity"))
        |> fun s -> step (t0.AddSeconds 3.0) s (CohortCommand.Depart departedSame)
      let s1, cid1 = acquireOk t0 31uy s0 conn1 (ClaimScope.File "src/Clef/RangeAnalysis.fs")
      let s2, cid2 = acquireOk t0 32uy s1 conn1 (ClaimScope.File "src/Clef/BakerSaturation.fs")
      let fence1 = s2.Claims.[cid1].Fence
      // Status text surfaces both connection id and joined agent name while preserving held-by=mcp:.
      let frame = Cohort.project { Seq = 1L<Bozzetto.Measures.ledgerSeq>; State = s2 } [||]
      let rendered = Bozzetto.Features.CohortStatusText.render frame
      rendered |> Expect.stringContains "status keeps held-by=mcp:" "held-by=mcp:conn-1 (antigravity)"
      // Agent resumes on a new MCP connection while conn-1 stays open (zombie helper process).
      let s3 = step (t0.AddMinutes 1.0) s2 (CohortCommand.JoinNamed(conn2, JoinableRole.Implementer, None, "antigravity"))
      // Negative control 1: stale fence on resumed connection is refused.
      match Cohort.decide (t0.AddMinutes 1.0) [| 33uy |] s3 (CohortCommand.ReleaseClaim(conn2, cid1, fence1 - 1L<Bozzetto.Measures.fence>)) with
      | Result.Error(CohortError.StaleClaimFence(c, _, _)) -> c |> Expect.equal "stale fence refused" cid1
      | otherRes -> failtestf "expected StaleClaimFence, got %A" otherRes
      // Negative control 2: different agent name with exact fence is refused.
      match Cohort.decide (t0.AddMinutes 1.0) [| 34uy |] s3 (CohortCommand.ReleaseClaim(otherConn, cid1, fence1)) with
      | Result.Error(CohortError.NotClaimHolder(c, who)) -> (c, who) |> Expect.equal "unrelated agent refused" (cid1, otherConn)
      | otherRes -> failtestf "expected NotClaimHolder for unrelated agent, got %A" otherRes
      // Negative control 3: departed connection sharing the same agent name is refused.
      match Cohort.decide (t0.AddMinutes 1.0) [| 35uy |] s3 (CohortCommand.ReleaseClaim(departedSame, cid1, fence1)) with
      | Result.Error(CohortError.NotClaimHolder(c, who)) -> (c, who) |> Expect.equal "departed same-name connection refused" (cid1, departedSame)
      | otherRes -> failtestf "expected NotClaimHolder for departed connection, got %A" otherRes
      // Negative control 4 (#12 is release/reclaim only): landing still requires the holding connection.
      match Cohort.decide (t0.AddMinutes 1.0) [| 39uy |] s3 (CohortCommand.RequestLanding(conn2, [ cid2, s3.Claims.[cid2].Fence ], [ "sha" ], "land")) with
      | Result.Error(CohortError.NotClaimHolder(c, who)) -> (c, who) |> Expect.equal "same-name connection cannot land another connection's claim" (cid2, conn2)
      | otherRes -> failtestf "expected NotClaimHolder for a cross-connection landing, got %A" otherRes
      // ObserveSave from conn2 for same agent name does not emit a false self-violation.
      match Cohort.decide (t0.AddMinutes 1.0) [| 36uy |] s3 (CohortCommand.ObserveSave(conn2, "src/Clef/RangeAnalysis.fs")) with
      | Result.Ok(_, evs, _) -> evs |> Expect.isEmpty "same agent on new connection does not trigger self-violation"
      | otherRes -> failtestf "unexpected ObserveSave result %A" otherRes
      // Positive control: resumed connection releases earlier claim with exact fence while conn1 is still open.
      match Cohort.decide (t0.AddMinutes 1.0) [| 37uy |] s3 (CohortCommand.ReleaseClaim(conn2, cid1, fence1)) with
      | Result.Ok(s4, [ CohortEvent.ClaimReleased(c, by, _) ], _) ->
        c |> Expect.equal "released earlier claim" cid1
        by |> Expect.equal "release records acting connection" conn2
        match claimStateOf cid1 s4 with
        | Some(ClaimState.Released _) -> ()
        | otherState -> failtestf "expected Released state, got %A" otherState
        // Also verify post-departure ReclaimClaim across connections when conn1 departs and orphans cid2.
        let s5 = step (t0.AddMinutes 2.0) s4 (CohortCommand.Depart conn1)
        let orphanFence2 = s5.Claims.[cid2].Fence
        match Cohort.decide (t0.AddMinutes 3.0) [| 38uy |] s5 (CohortCommand.ReclaimClaim(conn2, cid2, orphanFence2)) with
        | Result.Ok(s6, [ CohortEvent.ClaimReassigned(c, toM, _) ], _) ->
          (c, toM) |> Expect.equal "reclaimed across connections" (cid2, conn2)
          claimStateOf cid2 s6 |> Expect.equal "reclaimed claim held by resumed connection" (Some (ClaimState.Held conn2))
        | otherRes -> failtestf "expected ReclaimClaim across connections to succeed, got %A" otherRes
      | otherRes -> failtestf "expected cross-connection ReleaseClaim to succeed, got %A" otherRes

    testCase "dead-but-open holder and zombie same-name connection depart while active WorkLease holder stays present" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let deadOpen = mcpMember "dead-open"
      let zombieOld = mcpMember "building-old-zombie"
      let leaseHolder = mcpMember "lease-holder"
      let s0 =
        (CohortState.empty () : CohortState<MemberTable.MemberId>)
        |> fun s -> step t0 s (CohortCommand.JoinNamed(deadOpen, JoinableRole.Implementer, None, "finished-agent"))
        |> fun s -> step (t0.AddSeconds 2.0) s (CohortCommand.JoinNamed(zombieOld, JoinableRole.Implementer, None, "building-agent"))
        |> fun s -> step (t0.AddSeconds 5.0) s (CohortCommand.JoinNamed(leaseHolder, JoinableRole.Implementer, None, "building-agent"))
      let s1, deadClaim = acquireOk t0 41uy s0 deadOpen (ClaimScope.File "src/DeadOpen.fs")
      let s2, zombieClaim = acquireOk t0 42uy s1 zombieOld (ClaimScope.File "src/ZombieOld.fs")
      let s3, liveClaim = acquireOk t0 43uy s2 leaseHolder (ClaimScope.File "src/Building.fs")
      // WorkLease.holder format ("<agent> · <project>") matches building-agent and renews ONLY
      // the newest present connection (leaseHolder), letting zombieOld and deadOpen depart cleanly.
      let liveLeaseHolders = Set.ofList [ "building-agent · Bozzetto" ]
      let tickWithLeases (now: DateTime) (st: CohortState<MemberTable.MemberId>) =
        let toRenew = DaemonMode.cohortMembersToRenewWithLeases Set.empty liveLeaseHolders st.Members
        let renewed = toRenew |> List.fold (fun acc who -> step now acc (CohortCommand.RenewLease who)) st
        // CohortOwner follows a seat-vacating Tick with PromoteConductor; `step` ignores its refusal otherwise.
        step now renewed CohortCommand.Tick |> fun s -> step now s CohortCommand.PromoteConductor
      let final =
        [ 1 .. 35 ]
        |> List.fold (fun st m -> tickWithLeases (t0.AddMinutes(float m)) st) s3
      match final.Members.[deadOpen].Presence with
      | MemberPresence.Departed _ -> ()
      | other -> failtestf "dead-but-open idle holder must be marked Departed, got %A" other
      match final.Members.[zombieOld].Presence with
      | MemberPresence.Departed _ -> ()
      | other -> failtestf "older zombie connection sharing AgentName must still depart, got %A" other
      match claimStateOf deadClaim final with
      | Some(ClaimState.Orphaned(prev, _)) -> prev |> Expect.equal "dead-but-open holder's claim is orphaned" deadOpen
      | other -> failtestf "dead-but-open holder's claim must become Orphaned, got %A" other
      match claimStateOf zombieClaim final with
      | Some(ClaimState.Orphaned(prev, _)) -> prev |> Expect.equal "zombie connection's claim is orphaned" zombieOld
      | other -> failtestf "zombie connection's claim must become Orphaned, got %A" other
      final.Members.[leaseHolder].Presence |> Expect.equal "newest connection with live WorkLease stays Present" MemberPresence.Present
      claimStateOf liveClaim final |> Expect.equal "live lease holder keeps its claim Held" (Some (ClaimState.Held leaseHolder))
      final.Conductor |> Expect.equal "the vacated seat is promoted to the active lease holder" (Some leaseHolder)

    testCase "a name-only lease renews the most recently JOINED same-name connection, never a zombie with a later renewal" <| fun _ ->
      let t0 = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let zombie, live = mcpMember "zombie-old", mcpMember "live-new"
      let record joinedAt lastRenewal : MemberRecord =
        { Role = JoinableRole.Implementer; Presence = MemberPresence.Present; JoinedAt = joinedAt; LastRenewal = lastRenewal; Session = None; AgentName = Some "building-agent" }
      // The zombie joined first but a stale tool call renewed it AFTER the live connection joined.
      let members = Map.ofList [ zombie, record t0 (t0.AddMinutes 10.0); live, record (t0.AddMinutes 5.0) (t0.AddMinutes 5.0) ]
      let lease = Set.ofList [ "building-agent · Bozzetto" ]
      DaemonMode.cohortMembersToRenewWithLeases Set.empty lease members |> Expect.equal "only the live, newer connection is renewed" [ live ]
      DaemonMode.cohortMembersToRenewWithLeases Set.empty Set.empty members |> Expect.isEmpty "negative control: without a lease or activity nobody is renewed"
      DaemonMode.cohortMembersToRenewWithLeases (Set.ofList [ MemberTable.MemberId.display zombie ]) lease members
      |> List.sort |> Expect.equal "a connection's own fresh activity still renews it" (List.sort [ zombie; live ])
      // Fault injection: the rejected LastRenewal-first rank picks the zombie, which this test rejects.
      let lastRenewalFirst =
        members |> Map.toList |> List.maxBy (fun (m, r) -> r.LastRenewal, r.JoinedAt, m) |> fst
      lastRenewalFirst |> Expect.equal "the old rank would have renewed the zombie" zombie
      (lastRenewalFirst = live) |> Expect.isFalse "so the shipped rank is not the old one"

    testCase "every tool call on a bound connection records presence that keeps the member renewed" <| fun _ ->
      let tracker = AgentActivityTracker.create ()
      let now = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
      let who = mcpMember "tool-caller"
      AgentActivityTracker.recordMemberActivity tracker who "checkout-1" None None (now.AddMinutes -1.0)
      McpServer.recordToolCallActivity tracker "tool-caller" (Some "get_cohort_status") now
      AgentActivityTracker.getPresence tracker (MemberTable.MemberId.display who)
      |> Option.map (fun presence -> presence.SessionId)
      |> Expect.equal "generic get_cohort_status preserves existing session attribution" (Some "checkout-1")
      McpServer.recordToolCallActivity tracker "" (Some "get_cohort_status") now
      McpServer.recordToolCallActivity tracker "   " (Some "list_sessions") now
      let fresh at =
        AgentActivityTracker.getActivePresences tracker None (TimeSpan.FromMinutes 2.0) at |> List.map (fun p -> p.AgentName) |> Set.ofList
      fresh now |> Expect.equal "only the bound connection is recorded" (Set.ofList [ MemberTable.MemberId.display who ])
      (AgentActivityTracker.getPresence tracker (MemberTable.MemberId.display who)) |> Option.bind (fun p -> p.Intent)
      |> Expect.equal "the call names its tool" (Some "get_cohort_status")
      let members = Map.ofList [ who, { Role = JoinableRole.Implementer; Presence = MemberPresence.Present; JoinedAt = now; LastRenewal = now; Session = None; AgentName = None } ]
      DaemonMode.cohortMembersToRenewWithLeases (fresh (now.AddMinutes 1.0)) Set.empty members |> Expect.equal "a recent tool call renews the member" [ who ]
      DaemonMode.cohortMembersToRenewWithLeases (fresh (now.AddMinutes 3.0)) Set.empty members |> Expect.isEmpty "a stale tool call no longer does"

    testCase "CohortJson round-trips named joins, owner follow-ups and promotion events" <| fun _ ->
      let m1 = mcpMember "m1"
      let m2 = mcpMember "m2"
      for cmd in [ CohortCommand.JoinNamed(m1, JoinableRole.Implementer, Some "s1", "antigravity"); CohortCommand.PromoteConductor; CohortCommand.ExpireOrphanedClaims ] do
        CohortJson.commandFromJson (CohortJson.commandToJson cmd) |> Expect.equal "command JSON round-trip" (Ok cmd)
      let evs = [ CohortEvent.ConductorPromoted(m1, m2) ]
      CohortJson.eventsFromJson (CohortJson.eventsToJson evs) |> Expect.equal "ConductorPromoted JSON round-trip" (Ok evs)
  ]
