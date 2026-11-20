namespace Bozzetto.Features

open System
open System.Threading
open Bozzetto

/// The daemon's one live lease pool. Admission still belongs to the pure
/// ExpensiveWorkLease rules and MemoryPressureWatch; observers gain no authority.
module LeaseWatch =

  /// The pool as observers see it, with the serial of the change that produced
  /// it: one per published change, increasing. Published whole, under the
  /// pool's own gate, so a value and its serial never come apart.
  type Pool = { Serial: int64; Active: ExpensiveWorkLease.GrantView list; Queue: ExpensiveWorkLease.QueueView list }

  let mutable private pool = ExpensiveWorkLease.empty
  let private gate = obj ()
  let private changedEvent = Event<Pool>()
  let mutable private deadline: Timer option = None
  let mutable private generation = 0L
  let mutable private latest = { Serial = 0L; Active = []; Queue = [] }

  /// Owner changes only: request, release, withdraw, reset, or the next known
  /// expiry. Each carries the pool it produced, captured with its serial under
  /// the gate. The announcement itself is made outside the gate, so two may be
  /// heard out of order; the serial orders them, and a subscriber that admits
  /// by serial (the bridge's fact plane) drops the older. No periodic scan or
  /// subscriber-local clock is involved.
  let changed : IObservable<Pool> = changedEvent.Publish

  let private notify (change: Pool) =
    try changedEvent.Trigger change
    with _ -> () // An observer cannot fail the admission or release operation.

  let internal nextExpiryFromTimestamps (now: DateTimeOffset) (expiries: DateTimeOffset list) =
    expiries
    |> List.filter (fun expires -> expires > now)
    |> List.sort
    |> List.tryHead

  let internal nextExpiry now (active: ExpensiveWorkLease.GrantView list) =
    nextExpiryFromTimestamps now (active |> List.map (fun (grant: ExpensiveWorkLease.GrantView) -> grant.ExpiresAt))

  let internal nextPoolExpiry (now: DateTimeOffset) (state: ExpensiveWorkLease.PoolState) =
    let live = ExpensiveWorkLease.reapExpired now state
    (live.Active |> List.map _.ExpiresAt) @ (live.Queue |> List.map _.ExpiresAt)
    |> nextExpiryFromTimestamps now

  /// Under the gate: the next published pool, one serial on from the last.
  let private advanceAt (now: DateTimeOffset) =
    let live = ExpensiveWorkLease.snapshot now pool
    latest <- { Serial = latest.Serial + 1L; Active = live.Active; Queue = live.Queue }
    latest

  /// A single one-shot deadline belongs to the pool, not to any browser. Each
  /// mutation replaces it; an already-queued callback is fenced by generation.
  let rec private scheduleAt (now: DateTimeOffset) =
    generation <- generation + 1L
    let run = generation
    deadline |> Option.iter _.Dispose()
    deadline <- None
    match nextPoolExpiry now pool with
    | None -> ()
    | Some expires ->
      let maxDue = TimeSpan.FromDays 45.0
      let delay = min maxDue (max (TimeSpan.FromMilliseconds 1.) (expires - now + TimeSpan.FromMilliseconds 1.))
      let callback _ =
        let change = lock gate (fun () ->
          if generation <> run then None
          else
            let firedAt = DateTimeOffset.UtcNow
            pool <- ExpensiveWorkLease.reapExpired firedAt pool
            scheduleAt firedAt
            let after = ExpensiveWorkLease.snapshot firedAt pool
            if latest.Active <> after.Active || latest.Queue <> after.Queue then
              Some(advanceAt firedAt)
            else
              None)
        change |> Option.iter notify
      deadline <- Some(new Timer(TimerCallback callback, null, delay, Timeout.InfiniteTimeSpan))

  let private update transition =
    let result, change = lock gate (fun () ->
      let prior = pool
      let next, result = transition pool
      let now = DateTimeOffset.UtcNow
      pool <- ExpensiveWorkLease.reapExpired now next
      if pool <> prior then
        scheduleAt now
      let after = ExpensiveWorkLease.snapshot now pool
      if latest.Active <> after.Active || latest.Queue <> after.Queue then
        result, Some(advanceAt now)
      else result, None)
    change |> Option.iter notify
    result

  /// `holder` identifies the caller. Queueing and admission rules are unchanged.
  let request (holder: string) (kind: ExpensiveWorkLease.Kind) : ExpensiveWorkLease.Decision =
    update (fun state ->
      ExpensiveWorkLease.request DateTimeOffset.UtcNow (MemoryPressureWatch.currentLevel ()) state holder kind)

  // Atomic placement evaluation and admission inside the lease pool's own lock.
  // Reaps expired leases before evaluating placement; withdraws any stale queued
  // ask when throttled so a deferred caller never leaves a ghost queue entry.
  let requestWithPlacement
      (holder: string)
      (kind: ExpensiveWorkLease.Kind)
      (evaluatePlacement: ExpensiveWorkLease.PoolState -> MemoryPressure -> DateTimeOffset -> FleetBalance.PlacementDecision)
      : Result<ExpensiveWorkLease.Decision * FleetBalance.RebalanceAdvisory option, FleetBalance.RebalanceAdvisory> =
    let outcome, change =
      lock gate (fun () ->
        let prior = pool
        let now = DateTimeOffset.UtcNow
        let livePool = ExpensiveWorkLease.reapExpired now pool
        let pressure = MemoryPressureWatch.currentLevel ()
        let nextPool, result =
          match evaluatePlacement livePool pressure now with
          | FleetBalance.PlacementDecision.Throttled advisory ->
            let withdrawnPool, _ = ExpensiveWorkLease.withdraw holder kind livePool
            withdrawnPool, Error advisory
          | FleetBalance.PlacementDecision.Proceed advisoryOpt ->
            let grantedPool, decision = ExpensiveWorkLease.request now pressure livePool holder kind
            grantedPool, Ok(decision, advisoryOpt)
          | FleetBalance.PlacementDecision.BypassedPinned _ ->
            let grantedPool, decision = ExpensiveWorkLease.request now pressure livePool holder kind
            grantedPool, Ok(decision, None)
        pool <- ExpensiveWorkLease.reapExpired now nextPool
        if pool <> prior then
          scheduleAt now
        let after = ExpensiveWorkLease.snapshot now pool
        if latest.Active <> after.Active || latest.Queue <> after.Queue then
          result, Some(advanceAt now)
        else
          result, None)
    change |> Option.iter notify
    outcome

  let release (leaseId: ExpensiveWorkLease.LeaseId) : ExpensiveWorkLease.ReleaseOutcome =
    update (ExpensiveWorkLease.release leaseId)

  let releaseOwned (holder: string) (leaseId: ExpensiveWorkLease.LeaseId) : ExpensiveWorkLease.ReleaseOutcome =
    update (ExpensiveWorkLease.releaseOwned holder leaseId)

  /// Heartbeat from a live holder; the pool's expiry deadline moves with it.
  let renew (holder: string) (leaseId: ExpensiveWorkLease.LeaseId) : ExpensiveWorkLease.RenewOutcome =
    update (ExpensiveWorkLease.renew DateTimeOffset.UtcNow holder leaseId)

  /// A caller leaving the line says so, instead of leaving a ghost.
  let withdraw (holder: string) (kind: ExpensiveWorkLease.Kind) : ExpensiveWorkLease.WithdrawOutcome =
    update (ExpensiveWorkLease.withdraw holder kind)

  /// Safe observation: the release capability never appears in this snapshot.
  let snapshot () = lock gate (fun () -> ExpensiveWorkLease.snapshot DateTimeOffset.UtcNow pool)

  /// Command reporting proves the existing grant without publishing its capability
  /// or renewing it. The admission owner and its expiry remain unchanged.
  let private matchingAt now holder kind leaseId (state: ExpensiveWorkLease.PoolState) =
    state.Active |> List.tryFind (fun lease ->
      lease.Id = leaseId && lease.Holder = holder && lease.Kind = kind && lease.ExpiresAt > now)

  let internal matchesAt now holder kind leaseId state = matchingAt now holder kind leaseId state |> Option.isSome

  let matchesLive holder kind leaseId =
    lock gate (fun () -> matchesAt DateTimeOffset.UtcNow holder kind leaseId pool)

  // True when leaseId is currently held and unexpired in the live pool.
  let hasLiveLease (leaseId: ExpensiveWorkLease.LeaseId) : bool =
    lock gate (fun () ->
      let now = DateTimeOffset.UtcNow
      pool.Active |> List.exists (fun lease -> lease.Id = leaseId && lease.ExpiresAt > now))

  // Expiry of a live unexpired lease already known to the caller (never publishes unknown lease capabilities).
  let leaseExpiresAt (leaseId: ExpensiveWorkLease.LeaseId) : DateTimeOffset option =
    lock gate (fun () ->
      let now = DateTimeOffset.UtcNow
      pool.Active
      |> List.tryFind (fun lease -> lease.Id = leaseId && lease.ExpiresAt > now)
      |> Option.map _.ExpiresAt)

  // Count live unexpired build/test/rebuild leases whose capability id is not in trackedLeaseIds.
  // RunApp and SessionCreateOrWarmup leases never suppress unleased compiler/build detection.
  let untrackedBuildOrTestLeaseCount (trackedLeaseIds: Set<string>) : int =
    lock gate (fun () ->
      let now = DateTimeOffset.UtcNow
      pool.Active
      |> List.filter (fun lease ->
        lease.ExpiresAt > now
        && (match lease.Kind with
            | ExpensiveWorkLease.Kind.FullBuild
            | ExpensiveWorkLease.Kind.TestSuiteRun
            | ExpensiveWorkLease.Kind.Rebuild -> true
            | ExpensiveWorkLease.Kind.RunApp
            | ExpensiveWorkLease.Kind.SessionCreateOrWarmup -> false)
        && not (Set.contains (ExpensiveWorkLease.LeaseId.value lease.Id) trackedLeaseIds))
      |> List.length)

  let tryMatchingLive holder kind leaseId =
    lock gate (fun () ->
      matchingAt DateTimeOffset.UtcNow holder kind leaseId pool
      |> Option.map (fun lease ->
        let view: ExpensiveWorkLease.GrantView = {
          Holder = lease.Holder; Kind = ExpensiveWorkLease.Kind.toToken lease.Kind
          GrantedAt = lease.GrantedAt; ExpiresAt = lease.ExpiresAt }
        view))

  /// The pool as last published, with its serial: what a subscriber takes as
  /// its first value, so a change announced meanwhile is ordered after it.
  let current () : Pool = lock gate (fun () -> latest)

  let reset () : unit =
    let change = lock gate (fun () ->
      let now = DateTimeOffset.UtcNow
      pool <- ExpensiveWorkLease.empty
      scheduleAt now
      advanceAt now)
    notify change

  let internal seedPoolForTest (state: ExpensiveWorkLease.PoolState) : unit =
    let change = lock gate (fun () ->
      let now = DateTimeOffset.UtcNow
      pool <- state
      scheduleAt now
      advanceAt now)
    notify change
