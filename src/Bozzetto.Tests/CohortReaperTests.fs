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
        |> fun s -> step t0 s (CohortCommand.AcquireClaim("busy", ClaimScope.File "/repo/a.fs", "editing"))
        |> fun s -> step t0 s (CohortCommand.AcquireClaim("idle", ClaimScope.File "/repo/b.fs", "editing"))
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
