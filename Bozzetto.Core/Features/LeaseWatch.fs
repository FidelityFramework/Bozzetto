namespace Bozzetto.Features

open System
open System.Threading
open Bozzetto

/// The daemon's one live lease pool. Admission still belongs to the pure
/// ExpensiveWorkLease rules and MemoryPressureWatch; observers gain no authority.
module LeaseWatch =

  let mutable private pool = ExpensiveWorkLease.empty
  let private gate = obj ()
  let private changedEvent = Event<unit>()
  let mutable private deadline: Timer option = None
  let mutable private generation = 0L

  /// Owner changes only: request, release, reset, or the next known expiry.
  /// No periodic scan or subscriber-local clock is involved.
  let changed = changedEvent.Publish

  let private notify () =
    try changedEvent.Trigger()
    with _ -> () // An observer cannot fail the admission or release operation.

  let internal nextExpiry now active =
    active
    |> List.map (fun (grant: ExpensiveWorkLease.GrantView) -> grant.ExpiresAt)
    |> List.filter (fun expires -> expires > now)
    |> List.sort
    |> List.tryHead

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
        let current = lock gate (fun () ->
          if generation <> run then false
          else
            schedule ()
            true)
        if current then notify ()
      deadline <- Some(new Timer(TimerCallback callback, null, expires - now + TimeSpan.FromMilliseconds 1., Timeout.InfiniteTimeSpan))

  let private update transition =
    let result, didChange = lock gate (fun () ->
      let now = DateTimeOffset.UtcNow
      let before = ExpensiveWorkLease.snapshot now pool
      let next, result = transition pool
      pool <- next
      let didChange = before <> ExpensiveWorkLease.snapshot now pool
      if didChange then schedule ()
      result, didChange)
    if didChange then notify ()
    result

  /// `holder` identifies the caller. Queueing and admission rules are unchanged.
  let request (holder: string) (kind: ExpensiveWorkLease.Kind) : ExpensiveWorkLease.Decision =
    update (fun state ->
      ExpensiveWorkLease.request DateTimeOffset.UtcNow (MemoryPressureWatch.currentLevel ()) state holder kind)

  let release (leaseId: ExpensiveWorkLease.LeaseId) : ExpensiveWorkLease.ReleaseOutcome =
    update (ExpensiveWorkLease.release leaseId)

  let releaseOwned (holder: string) (leaseId: ExpensiveWorkLease.LeaseId) : ExpensiveWorkLease.ReleaseOutcome =
    update (ExpensiveWorkLease.releaseOwned holder leaseId)

  /// Safe observation: the release capability never appears in this snapshot.
  let snapshot () = lock gate (fun () -> ExpensiveWorkLease.snapshot DateTimeOffset.UtcNow pool)

  let reset () : unit =
    lock gate (fun () ->
      pool <- ExpensiveWorkLease.empty
      schedule ())
    notify ()
