namespace Bozzetto

open System
open System.Threading
open System.Threading.Tasks

/// A "railway" effect: an async computation that either succeeds with a
/// value or fails with a classified `BozzettoError` — never a raw exception.
/// Every `BozzettoIO` value already carries structured failure information, so
/// nothing downstream needs to sniff a message string or catch `exn` to
/// learn why an operation failed; it pattern-matches a `BozzettoError` case.
type BozzettoIO<'A> = Async<Result<'A, BozzettoError>>

/// Combinators over `BozzettoIO`. Pure composition — no IO of its own beyond
/// the Async plumbing the type already implies. Nothing here swallows an
/// exception: `ofExn` is the only boundary that catches, and it forces the
/// caller to classify what it caught.
[<RequireQualifiedAccess>]
module BozzettoIO =

  /// Lift a plain value into a successful `BozzettoIO`.
  let ret (value: 'A) : BozzettoIO<'A> =
    async.Return(Ok value)

  /// Sequence two `BozzettoIO` computations, short-circuiting on the first Error.
  let bind (f: 'A -> BozzettoIO<'B>) (io: BozzettoIO<'A>) : BozzettoIO<'B> =
    async {
      let! result = io
      match result with
      | Ok value -> return! f value
      | Error err -> return Error err
    }

  /// Apply a pure function to a successful result, preserving errors.
  let map (f: 'A -> 'B) (io: BozzettoIO<'A>) : BozzettoIO<'B> =
    async {
      let! result = io
      return Result.map f result
    }

  /// Lift an already-computed `Result` into `BozzettoIO`.
  let ofResult (result: Result<'A, BozzettoError>) : BozzettoIO<'A> =
    async.Return(result)

  /// Lift a throwing async into `BozzettoIO`. `classify` is mandatory — it
  /// forces every call site to decide, at the boundary, which `BozzettoError`
  /// an exception from THIS specific operation means, instead of flattening
  /// every failure into the same `Unexpected`/string shape. A cancellation
  /// is never classified as a domain failure: it propagates as a
  /// cancellation, exactly as any other Async/Task caller already expects.
  let ofExn (classify: exn -> BozzettoError) (work: Async<'A>) : BozzettoIO<'A> =
    async {
      try
        let! value = work
        return Ok value
      with
      | :? OperationCanceledException as cancellation -> return raise cancellation
      | ex -> return Error (classify ex)
    }

  /// Run two `BozzettoIO` computations concurrently; the first to complete
  /// (success or failure) wins. The loser is cooperatively cancelled — it is
  /// never awaited, so a slow loser cannot delay the caller past the winner.
  let race (a: BozzettoIO<'A>) (b: BozzettoIO<'A>) : BozzettoIO<'A> =
    async {
      let! ct = Async.CancellationToken
      use cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
      let taskA = Async.StartAsTask(a, cancellationToken = cts.Token)
      let taskB = Async.StartAsTask(b, cancellationToken = cts.Token)
      let! (winner: Task<Result<'A, BozzettoError>>) =
        Task.WhenAny(taskA, taskB) |> Async.AwaitTask
      cts.Cancel()
      return! Async.AwaitTask winner
    }

  /// Race `io` against a deadline. `onTimeout` is the `BozzettoError` to fail
  /// with when the span elapses first — the caller decides what a timeout
  /// MEANS for this specific operation (`WorkerTimeout`, `SessionNotRoutable`,
  /// ...) rather than this module inventing a generic one.
  let timeout (span: TimeSpan) (onTimeout: BozzettoError) (io: BozzettoIO<'A>) : BozzettoIO<'A> =
    let clock : BozzettoIO<'A> =
      async {
        do! Async.Sleep(span)
        return Error onTimeout
      }
    race io clock

  /// Retry `io` until `isDone` accepts the latest result or `maxAttempts`
  /// attempts are spent — the final attempt's outcome (success or failure)
  /// is returned as-is, whichever way the loop ends.
  let rec retryUntil (isDone: Result<'A, BozzettoError> -> bool) (maxAttempts: int) (io: BozzettoIO<'A>) : BozzettoIO<'A> =
    async {
      let! result = io
      match isDone result || maxAttempts <= 1 with
      | true -> return result
      | false -> return! retryUntil isDone (maxAttempts - 1) io
    }

/// Computation-expression builder for `BozzettoIO`, so call sites read like
/// ordinary `async { ... }` code while every `let!`/`return!` still carries
/// the railway's short-circuit-on-Error semantics.
type SageIOBuilder() =
  member _.Bind(io: BozzettoIO<'A>, f: 'A -> BozzettoIO<'B>) : BozzettoIO<'B> =
    BozzettoIO.bind f io
  member _.Return(value: 'A) : BozzettoIO<'A> =
    BozzettoIO.ret value
  member _.ReturnFrom(io: BozzettoIO<'A>) : BozzettoIO<'A> =
    io
  member _.Zero() : BozzettoIO<unit> =
    BozzettoIO.ret ()
  member _.Delay(f: unit -> BozzettoIO<'A>) : BozzettoIO<'A> =
    async.Delay(f)
  member _.Combine(a: BozzettoIO<unit>, b: BozzettoIO<'A>) : BozzettoIO<'A> =
    BozzettoIO.bind (fun () -> b) a
  member _.TryWith(io: BozzettoIO<'A>, handler: exn -> BozzettoIO<'A>) : BozzettoIO<'A> =
    async.TryWith(io, handler)
  member _.TryFinally(io: BozzettoIO<'A>, compensation: unit -> unit) : BozzettoIO<'A> =
    async.TryFinally(io, compensation)
  member _.Using(resource: 'T, binder: 'T -> BozzettoIO<'A>) : BozzettoIO<'A> when 'T :> IDisposable =
    async.Using(resource, binder)

[<AutoOpen>]
module BozzettoIOBuilder =
  /// `sageIO { let! v = someIO ... }` — the railway CE for `BozzettoIO`.
  let sageIO = SageIOBuilder()
