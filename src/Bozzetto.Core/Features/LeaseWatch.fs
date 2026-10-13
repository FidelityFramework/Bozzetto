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

  let internal nextExpiry now active =
    active
    |> List.map (fun (grant: ExpensiveWorkLease.GrantView) -> grant.ExpiresAt)
    |> List.filter (fun expires -> expires > now)
    |> List.sort
    |> List.tryHead

  /// Under the gate: the next published pool, one serial on from the last.
  let private advance () =
    let live = ExpensiveWorkLease.snapshot DateTimeOffset.UtcNow pool
    latest <- { Serial = latest.Serial + 1L; Active = live.Active; Queue = live.Queue }
    latest

  /// A single one-shot deadline belongs to the pool, not to any browser. Each
  /// mutation replaces it; an already-queued callback is fenced by generation.
  let rec private schedule () =
    generation <- generation + 1L
    let run = generation
    deadline |> Option.iter _.Dispose()
    deadline <- None
    let now = DateTimeOffset.UtcNow
    let live = ExpensiveWorkLease.snapshot now pool
    match nextExpiry now live.Active with
    | None -> ()
    | Some expires ->
      let callback _ =
        let change = lock gate (fun () ->
          if generation <> run then None
          else
            schedule ()
            Some(advance ()))
        change |> Option.iter notify
      deadline <- Some(new Timer(TimerCallback callback, null, expires - now + TimeSpan.FromMilliseconds 1., Timeout.InfiniteTimeSpan))

  let private update transition =
    let result, change = lock gate (fun () ->
      let now = DateTimeOffset.UtcNow
      let before = ExpensiveWorkLease.snapshot now pool
      let next, result = transition pool
      pool <- next
      if before <> ExpensiveWorkLease.snapshot now pool then
        schedule ()
        result, Some(advance ())
      else result, None)
    change |> Option.iter notify
    result

  /// `holder` identifies the caller. Queueing and admission rules are unchanged.
  let request (holder: string) (kind: ExpensiveWorkLease.Kind) : ExpensiveWorkLease.Decision =
    update (fun state ->
      ExpensiveWorkLease.request DateTimeOffset.UtcNow (MemoryPressureWatch.currentLevel ()) state holder kind)

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
      pool <- ExpensiveWorkLease.empty
      schedule ()
      advance ())
    notify change
