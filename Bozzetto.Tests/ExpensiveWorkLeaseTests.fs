/// Five agents each rebuilding/warming up against one daemon, all at once,
/// is how a night's incident happened — nobody misbehaved, nothing
/// coordinated. `ExpensiveWorkLease` is the coordination point: ask before
/// spending memory, get back Granted/Wait/Refused. These tests pin the
/// named shapes from the brief (a quiet machine, four leases out at Tight,
/// an expired lease being reclaimed, one member holding the cap while
/// another waits), plus the fairness and expiry guarantees.
module Bozzetto.Tests.ExpensiveWorkLeaseTests

open System
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.ExpensiveWorkLease

let private epoch = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)

[<Tests>]
let kindWireTests =
  testList "ExpensiveWorkLease.Kind wire tokens" [
    testCase "every Kind round-trips through its wire token" <| fun () ->
      Kind.all
      |> List.iter (fun k -> Kind.tryParse (Kind.toToken k) |> Expect.equal (sprintf "%A round-trips" k) (Some k))

    testCase "an unrecognized token parses to None, never a guess" <| fun () ->
      Kind.tryParse "not_a_real_kind" |> Expect.isNone "closed set — no silent fallback"
  ]

[<Tests>]
let namedShapeTests =
  testList "ExpensiveWorkLease.request — named shapes from the brief" [

    testCase "a quiet machine grants immediately" <| fun () ->
      let state', decision = request epoch MemoryPressure.Normal empty "agent-a" Kind.Rebuild
      match decision with
      | Decision.Granted(_, expiresAt) ->
        (expiresAt > epoch) |> Expect.isTrue "expiry is in the future"
        state'.Active |> Expect.hasLength "one lease now active" 1
      | other -> failtestf "expected Granted on a quiet machine, got %A" other

    testCase "four leases out at Tight: cap is 1, a fifth agent waits" <| fun () ->
      // Four leases already active (perhaps granted back when pressure was
      // Normal) — pressure has since risen to Tight, whose cap is 1.
      let fourActive =
        [ for i in 1 .. 4 ->
            { Id = LeaseId.create ()
              Kind = Kind.SessionCreateOrWarmup
              Holder = sprintf "agent-%d" i
              GrantedAt = epoch
              ExpiresAt = epoch.AddMinutes 5.0 } ]
      let state = { empty with Active = fourActive }
      let _, decision = request epoch MemoryPressure.Tight state "agent-5" Kind.Rebuild
      match decision with
      | Decision.Wait(retryAfter, reason) ->
        (retryAfter > TimeSpan.Zero) |> Expect.isTrue "a real, positive retry-after"
        reason |> Expect.stringContains "names how many are out" "4 lease(s) active"
        reason |> Expect.stringContains "names the pressure" "tight"
      | other -> failtestf "expected Wait — the pool is already over Tight's cap of 1, got %A" other

    testCase "an expired lease is reclaimed, freeing the slot for the next request" <| fun () ->
      let expired =
        { Id = LeaseId.create (); Kind = Kind.Rebuild; Holder = "agent-a"; GrantedAt = epoch.AddMinutes -20.0; ExpiresAt = epoch.AddMinutes -10.0 }
      let state = { empty with Active = [ expired ] }
      let state', decision = request epoch MemoryPressure.Tight state "agent-b" Kind.Rebuild
      match decision with
      | Decision.Granted _ ->
        state'.Active |> List.exists (fun l -> l.Holder = "agent-a") |> Expect.isFalse "the expired lease is gone"
        state'.Active |> List.exists (fun l -> l.Holder = "agent-b") |> Expect.isTrue "the new one is granted"
      | other -> failtestf "expected the expired lease to be reclaimed and the new request Granted, got %A" other

    testCase "one member holding the per-holder cap is Refused; another member still waits normally" <| fun () ->
      let holderALeases =
        [ for _ in 1 .. maxPerHolder ->
            { Id = LeaseId.create (); Kind = Kind.Rebuild; Holder = "agent-a"; GrantedAt = epoch; ExpiresAt = epoch.AddMinutes 5.0 } ]
      let state = { empty with Active = holderALeases }
      let _, decisionA = request epoch MemoryPressure.Normal state "agent-a" Kind.Rebuild
      match decisionA with
      | Decision.Refused reason -> reason |> Expect.stringContains "names the cap" (sprintf "%d/%d" maxPerHolder maxPerHolder)
      | other -> failtestf "expected agent-a (already at its per-holder cap) to be Refused, got %A" other
      let _, decisionB = request epoch MemoryPressure.Normal state "agent-b" Kind.Rebuild
      match decisionB with
      | Decision.Granted _ -> ()
      | other -> failtestf "agent-b holds nothing and Normal has spare capacity — expected Granted, got %A" other
  ]

[<Tests>]
let fairnessTests =
  testList "ExpensiveWorkLease — fairness and expiry" [

    testCase "queue is FIFO by arrival: the first to ask is the first granted when a slot frees" <| fun () ->
      // Tight caps at 1. agent-a gets the only slot; agent-b then agent-c ask
      // and both wait, in that order.
      let s0 = empty
      let s1, dA = request epoch MemoryPressure.Tight s0 "agent-a" Kind.Rebuild
      match dA with Decision.Granted _ -> () | other -> failtestf "expected agent-a granted, got %A" other
      let s2, dB = request epoch MemoryPressure.Tight s1 "agent-b" Kind.Rebuild
      match dB with Decision.Wait _ -> () | other -> failtestf "expected agent-b to wait, got %A" other
      let s3, dC = request epoch MemoryPressure.Tight s2 "agent-c" Kind.Rebuild
      match dC with Decision.Wait _ -> () | other -> failtestf "expected agent-c to wait, got %A" other
      // agent-a releases. Now agent-b (asked first) must be the one granted
      // next, not agent-c.
      let leaseA = match dA with Decision.Granted(id, _) -> id | _ -> failtest "unreachable"
      let s4, releaseOutcome = release leaseA s3
      releaseOutcome |> Expect.equal "agent-a's lease was live" ReleaseOutcome.Released
      let s5, dBRetry = request (epoch.AddSeconds 1.0) MemoryPressure.Tight s4 "agent-b" Kind.Rebuild
      match dBRetry with Decision.Granted _ -> () | other -> failtestf "agent-b asked first — expected Granted, got %A" other
      let _, dCRetry = request (epoch.AddSeconds 1.0) MemoryPressure.Tight s5 "agent-c" Kind.Rebuild
      match dCRetry with
      | Decision.Wait _ -> ()
      | other -> failtestf "agent-c asked second and the slot just went to agent-b — expected Wait, got %A" other

    testCase "a retrying holder does not create a duplicate queue entry" <| fun () ->
      let s0 = empty
      let s1, _ = request epoch MemoryPressure.Critical s0 "agent-a" Kind.Rebuild // Wait, enqueued
      let s2, _ = request (epoch.AddSeconds 5.0) MemoryPressure.Critical s1 "agent-a" Kind.Rebuild // retry, same ask
      s2.Queue |> Expect.hasLength "still one queue entry for agent-a" 1

    testCase "release makes the lease immediately unavailable" <| fun () ->
      let state, decision = request epoch MemoryPressure.Normal empty "agent-a" Kind.FullBuild
      let leaseId = match decision with Decision.Granted(id, _) -> id | _ -> failtest "expected Granted"
      let released, outcome1 = release leaseId state
      outcome1 |> Expect.equal "the lease was live" ReleaseOutcome.Released
      released.Active |> Expect.isEmpty "released lease is gone"
      // Releasing an already-released (or never-existing) id is a no-op,
      // not an error — but it DOES say AlreadyGone, the fencing signal.
      let releasedAgain, outcome2 = release leaseId released
      outcome2 |> Expect.equal "a second release of the same id finds nothing left to release" ReleaseOutcome.AlreadyGone
      releasedAgain.Active |> Expect.isEmpty "idempotent"

    testCase "Critical pressure admits nothing new but does not touch already-active leases" <| fun () ->
      let active = { Id = LeaseId.create (); Kind = Kind.RunApp; Holder = "agent-a"; GrantedAt = epoch; ExpiresAt = epoch.AddHours 1.0 }
      let state = { empty with Active = [ active ] }
      let state', decision = request epoch MemoryPressure.Critical state "agent-b" Kind.Rebuild
      match decision with
      | Decision.Wait _ -> ()
      | other -> failtestf "Critical's cap is 0 — expected Wait, got %A" other
      state'.Active |> Expect.contains "agent-a's already-granted lease is untouched" active
  ]

[<Tests>]
let neverExpiresTwinTests =
  testList "ExpensiveWorkLease.requestNeverExpiresTwin — reproduces the deadlock" [

    testCase "REPRODUCED — an abandoned lease under the twin blocks the pool forever" <| fun () ->
      // Tight caps concurrency at 1, so a single abandoned lease consumes
      // the WHOLE pool — the cleanest reproduction of the deadlock.
      let state, decision = requestNeverExpiresTwin epoch MemoryPressure.Tight empty "crashed-agent" Kind.Rebuild
      match decision with Decision.Granted _ -> () | other -> failtestf "expected the crashed agent granted first, got %A" other
      // Time passes well beyond the lease's TTL — under the real `request`
      // this would be reclaimed; under the twin it never is.
      let farFuture = epoch.AddHours 100.0
      let _, laterDecision = requestNeverExpiresTwin farFuture MemoryPressure.Tight state "agent-b" Kind.Rebuild
      match laterDecision with
      | Decision.Wait _ -> () // the deadlock: agent-b waits forever, since nothing ever reclaims the crashed lease
      | other -> failtestf "expected the twin to deadlock agent-b behind the abandoned lease, got %A" other

    testCase "the REAL request reclaims the identical abandoned lease instead" <| fun () ->
      let state, decision = request epoch MemoryPressure.Tight empty "crashed-agent" Kind.Rebuild
      match decision with Decision.Granted _ -> () | other -> failtestf "expected granted first, got %A" other
      let farFuture = epoch.AddHours 100.0
      let _, laterDecision = request farFuture MemoryPressure.Tight state "agent-b" Kind.Rebuild
      match laterDecision with
      | Decision.Granted _ -> ()
      | other -> failtestf "the real request must reclaim the expired lease and grant agent-b, got %A" other
  ]

[<Tests>]
let renewalTests =
  testList "ExpensiveWorkLease.renew — a live run outlasts a fixed TTL" [
    testCase "the holder renews a live lease and its expiry moves forward by the kind's TTL" <| fun () ->
      let state, decision = request epoch MemoryPressure.Normal empty "agent-a" Kind.TestSuiteRun
      let leaseId, firstExpiry = match decision with Decision.Granted(id, at) -> id, at | _ -> failtest "expected Granted"
      let later = epoch.AddMinutes 12.0
      let renewed, outcome = renew later "agent-a" leaseId state
      outcome |> Expect.equal "renewed from the renewal instant, not the grant" (RenewOutcome.Renewed(later + Kind.defaultTtl Kind.TestSuiteRun))
      let lease = renewed.Active |> List.exactlyOne
      (lease.ExpiresAt > firstExpiry) |> Expect.isTrue "the lease now lives past its original expiry"
      lease.GrantedAt |> Expect.equal "renewal is not a new grant" epoch
      renewed.Queue |> Expect.equal "the queue is untouched" state.Queue
      // The renewed lease still counts: a second holder at Tight (cap 1) still waits.
      let _, second = request later MemoryPressure.Tight renewed "agent-b" Kind.FullBuild
      match second with
      | Decision.Wait _ -> ()
      | other -> failtestf "a renewed lease must still occupy its slot, got %A" other

    testCase "another holder cannot keep a lease alive" <| fun () ->
      let state, decision = request epoch MemoryPressure.Normal empty "agent-a" Kind.FullBuild
      let leaseId = match decision with Decision.Granted(id, _) -> id | _ -> failtest "expected Granted"
      let after, outcome = renew (epoch.AddMinutes 1.0) "agent-b" leaseId state
      outcome |> Expect.equal "wrong holder is refused" RenewOutcome.AlreadyGone
      (after.Active |> List.exactlyOne).ExpiresAt |> Expect.equal "the expiry is unchanged" (epoch + Kind.defaultTtl Kind.FullBuild)

    testCase "an unknown lease id renews nothing" <| fun () ->
      let state, _ = request epoch MemoryPressure.Normal empty "agent-a" Kind.FullBuild
      let after, outcome = renew (epoch.AddMinutes 1.0) "agent-a" (LeaseId.create ()) state
      outcome |> Expect.equal "fabricated id" RenewOutcome.AlreadyGone
      after.Active |> Expect.equal "nothing changed" state.Active

    testCase "an expired lease is reclaimed, never resurrected by a late renewal" <| fun () ->
      let expired =
        { Id = LeaseId.create (); Kind = Kind.TestSuiteRun; Holder = "agent-a"; GrantedAt = epoch.AddMinutes -30.0; ExpiresAt = epoch.AddMinutes -1.0 }
      let state = { empty with Active = [ expired ] }
      let after, outcome = renew epoch "agent-a" expired.Id state
      outcome |> Expect.equal "too late: the crashed-holder guarantee holds" RenewOutcome.AlreadyGone
      after.Active |> Expect.isEmpty "the expired lease was reaped on the way"

    testProperty "renewal never changes the active count, the holder set or the queue order" <| fun (seconds: uint8) ->
      // Inside the queued-ask lifetime, so the only reap a renewal can trigger
      // is the holder's own expired lease; queued asks are all still live.
      let now = epoch.AddSeconds(float seconds % queuedAskLifetime.TotalSeconds)
      let s1, d1 = request epoch MemoryPressure.Normal empty "agent-a" Kind.Rebuild
      let s2, _ = request epoch MemoryPressure.Normal s1 "agent-b" Kind.FullBuild
      // Fill the Normal cap so later askers queue, giving the property a queue to protect.
      let s3, _ = request epoch MemoryPressure.Normal s2 "agent-c" Kind.TestSuiteRun
      let s4, _ = request epoch MemoryPressure.Normal s3 "agent-d" Kind.Rebuild
      let s5, _ = request epoch MemoryPressure.Normal s4 "agent-e" Kind.FullBuild
      let leaseId = match d1 with Decision.Granted(id, _) -> id | _ -> failtest "expected Granted"
      let after, _ = renew now "agent-a" leaseId s5
      let holders (s: PoolState) = s.Active |> List.map (fun l -> l.Holder) |> List.sort
      List.length after.Active |> Expect.equal "an in-time renewal changes no count" (List.length s5.Active)
      holders after |> Expect.equal "the holder set is preserved" (holders s5)
      after.Queue |> Expect.equal "the queue is never reordered or shortened by a renewal" s5.Queue
  ]

/// Four holders take the Normal cap so a fifth asker is told to Wait.
let private fillNormalCap state =
  [ "h1"; "h2"; "h3"; "h4" ]
  |> List.fold (fun s h -> fst (request epoch MemoryPressure.Normal s h Kind.Rebuild)) state

[<Tests>]
let queuedAskTests =
  testList "ExpensiveWorkLease — a queued ask is a claim held by a live caller" [
    testCase "a waiting caller that withdraws leaves the line" <| fun () ->
      let full = fillNormalCap empty
      let queued, decision = request epoch MemoryPressure.Normal full "late" Kind.FullBuild
      match decision with Decision.Wait _ -> () | other -> failtestf "expected Wait, got %A" other
      queued.Queue |> List.map (fun q -> q.Holder) |> Expect.equal "the ask is queued" [ "late" ]
      let after, outcome = withdraw "late" Kind.FullBuild queued
      outcome |> Expect.equal "withdrawn" WithdrawOutcome.Withdrawn
      after.Queue |> Expect.isEmpty "the line closed up"
      after.Active |> Expect.equal "active leases untouched" queued.Active
      snd (withdraw "late" Kind.FullBuild after) |> Expect.equal "withdrawing twice finds nothing" WithdrawOutcome.NotQueued

    testCase "withdraw touches only the holder's own ask for that kind" <| fun () ->
      let full = fillNormalCap empty
      let s1, _ = request epoch MemoryPressure.Normal full "late-a" Kind.FullBuild
      let s2, _ = request (epoch.AddSeconds 1.0) MemoryPressure.Normal s1 "late-b" Kind.TestSuiteRun
      let after, outcome = withdraw "late-a" Kind.TestSuiteRun s2
      outcome |> Expect.equal "late-a never queued that kind" WithdrawOutcome.NotQueued
      after.Queue |> Expect.equal "nothing changed" s2.Queue
      let after', _ = withdraw "late-a" Kind.FullBuild s2
      after'.Queue |> List.map (fun q -> q.Holder) |> Expect.equal "only late-a left; late-b keeps its place" [ "late-b" ]

    testCase "a ghost ask whose caller never retried is aged out of the line" <| fun () ->
      let full = fillNormalCap empty
      let queued, _ = request epoch MemoryPressure.Normal full "ghost" Kind.FullBuild
      let later = epoch + queuedAskLifetime + TimeSpan.FromSeconds 1.0
      // Someone else asking later triggers the reap; the ghost is gone and
      // the newcomer is at the front, not behind a claim nobody holds.
      let after, decision = request later MemoryPressure.Normal queued "live" Kind.FullBuild
      after.Queue |> List.map (fun q -> q.Holder) |> Expect.equal "ghost reaped, live asker queued" [ "live" ]
      match decision with Decision.Wait _ -> () | other -> failtestf "cap still full, so Wait, got %A" other

    testCase "a live retrier is never aged out: each retry refreshes the ask" <| fun () ->
      let full = fillNormalCap empty
      let queued, _ = request epoch MemoryPressure.Normal full "patient" Kind.FullBuild
      let halfway = epoch + TimeSpan.FromTicks(queuedAskLifetime.Ticks / 2L)
      let retried, _ = request halfway MemoryPressure.Normal queued "patient" Kind.FullBuild
      let nearlyTwice = halfway + queuedAskLifetime - TimeSpan.FromSeconds 1.0
      let after, _ = request nearlyTwice MemoryPressure.Normal retried "other" Kind.FullBuild
      after.Queue |> List.map (fun q -> q.Holder) |> Expect.equal "patient kept its place by retrying" [ "patient"; "other" ]
      (after.Queue |> List.head).Seq |> Expect.equal "and its original position" (queued.Queue |> List.head).Seq

    testCase "a newer ask from the same scope supersedes its older queued ask instead of lining up behind it" <| fun () ->
      // Code changed; the same agent/project asks to build again. The pool
      // must hold ONE claim for that scope, at its original position, and
      // the stale earlier ask must not be served first.
      let full = fillNormalCap empty
      let s1, d1 = request epoch MemoryPressure.Normal full "claude · /repos/Bozzetto" Kind.FullBuild
      match d1 with Decision.Wait _ -> () | other -> failtestf "expected Wait, got %A" other
      let s2, _ = request (epoch.AddSeconds 5.0) MemoryPressure.Normal s1 "other-agent" Kind.FullBuild
      let s3, d3 = request (epoch.AddSeconds 20.0) MemoryPressure.Normal s2 "claude · /repos/Bozzetto" Kind.FullBuild
      match d3 with Decision.Wait _ -> () | other -> failtestf "still waiting, got %A" other
      s3.Queue |> List.map (fun q -> q.Holder) |> Expect.equal "one claim per scope, position kept" [ "claude · /repos/Bozzetto"; "other-agent" ]
      (s3.Queue |> List.head).LastAskedAt |> Expect.equal "the claim now represents the newest ask" (epoch.AddSeconds 20.0)
      (s3.Queue |> List.head).FirstAskedAt |> Expect.equal "arrival order is unchanged" epoch

    testCase "REPRODUCED — without aging, two abandoned asks block a later caller" <| fun () ->
      // The shape from the dashboard: two one-shot callers were told to Wait,
      // exited, and never retried. Under the real pool they age out.
      let full = fillNormalCap empty
      let s1, _ = request epoch MemoryPressure.Normal full "gate-1" Kind.FullBuild
      let s2, _ = request epoch MemoryPressure.Normal s1 "gate-2" Kind.FullBuild
      let freed = [ "h1"; "h2"; "h3"; "h4" ] |> List.fold (fun (s: PoolState) h ->
        let id = (s.Active |> List.find (fun l -> l.Holder = h)).Id
        fst (release id s)) s2
      let soon = epoch.AddSeconds 30.0
      let _, blocked = request soon MemoryPressure.Normal freed "newcomer" Kind.FullBuild
      match blocked with Decision.Wait _ -> () | other -> failtestf "within the lifetime the ghosts still hold the line, got %A" other
      let later = epoch + queuedAskLifetime + TimeSpan.FromSeconds 1.0
      let _, unblocked = request later MemoryPressure.Normal freed "newcomer" Kind.FullBuild
      match unblocked with Decision.Granted _ -> () | other -> failtestf "after the lifetime the ghosts are reaped and the newcomer is granted, got %A" other
  ]

[<Tests>]
let observationDeadlineTests =
  testList "Lease observation deadlines" [
    test "the owner arms only the earliest future expiry" {
      let grant expires : GrantView =
        { Holder = "agent"; Kind = "full_build"; GrantedAt = epoch; ExpiresAt = expires }
      let first, last = epoch.AddSeconds 2., epoch.AddMinutes 1.
      let active = [ grant last; grant epoch; grant first; grant (epoch.AddSeconds -1.) ]
      Features.LeaseWatch.nextExpiry epoch active |> Expect.equal "earliest future, not the last grant" (Some first)
      Features.LeaseWatch.nextExpiry last active |> Expect.equal "expired grants never rearm a timer" None
      Features.LeaseWatch.nextExpiry epoch [] |> Expect.equal "empty pool owns no deadline" None
      let latestInstead = active |> List.map _.ExpiresAt |> List.max
      latestInstead |> Expect.notEqual "latest-expiry mutant would miss the first removal" first
    }
  ]
