/// ## BozzettoIO Tests
///
/// The railway effect type over `BozzettoError`: monad laws (so `bind`/`ret`
/// compose the way every other `bind`/`ret` in the codebase already does),
/// plus behavior tests for the boundary combinators (`ofExn`, `timeout`,
/// `race`, `retryUntil`) that route exceptions and time budgets through the
/// algebra instead of a raw exception or a bare bool.
module BozzettoIOTests

open System
open Expecto
open Expecto.Flip
open Bozzetto

// ── Fixtures ────────────────────────────────────────────────────────────

/// Build a `Result<int, BozzettoError>` from cheap FsCheck-generated inputs
/// (`bool`/`int`) rather than deriving an `Arbitrary<BozzettoError>` for the
/// whole 30-case DU (several cases carry `exn`, which has no meaningful
/// generator) — two SessionNotFound variants distinguished by payload is
/// enough to exercise both the Ok and Error rails.
let private mkResult (isOk: bool) (n: int) : Result<int, BozzettoError> =
  match isOk with
  | true -> Ok n
  | false -> BozzettoError.SessionNotFound (string n) |> Error

let private runIO (io: BozzettoIO<'A>) : Result<'A, BozzettoError> =
  Async.RunSynchronously(io, timeout = 5000)

// ── Monad laws ──────────────────────────────────────────────────────────

[<Tests>]
let monadLawTests =
  testList "BozzettoIO monad laws" [

    testProperty "WHY — left identity: bind f (ret a) = f a, or bind couldn't be trusted to sequence a lifted value" <|
      fun (a: int) (fIsOk: bool) ->
        let f (x: int) : BozzettoIO<int> = BozzettoIO.ofResult (mkResult fIsOk (x + 1))
        let lhs = BozzettoIO.bind f (BozzettoIO.ret a) |> runIO
        let rhs = f a |> runIO
        lhs |> Expect.equal "bind f (ret a) = f a" rhs

    testProperty "WHY — right identity: bind ret m = m, or bind couldn't be trusted to leave an unchanged computation alone" <|
      fun (mIsOk: bool) (n: int) ->
        let m : BozzettoIO<int> = BozzettoIO.ofResult (mkResult mIsOk n)
        let lhs = BozzettoIO.bind BozzettoIO.ret m |> runIO
        let rhs = m |> runIO
        lhs |> Expect.equal "bind ret m = m" rhs

    testProperty "WHY — associativity: bind h (bind g m) = bind (fun x -> bind h (g x)) m, or chained binds could reassociate into a different result" <|
      fun (mIsOk: bool) (n: int) (gIsOk: bool) (hIsOk: bool) ->
        let m : BozzettoIO<int> = BozzettoIO.ofResult (mkResult mIsOk n)
        let g (x: int) : BozzettoIO<int> = BozzettoIO.ofResult (mkResult gIsOk (x + 1))
        let h (x: int) : BozzettoIO<int> = BozzettoIO.ofResult (mkResult hIsOk (x * 2))
        let lhs = BozzettoIO.bind h (BozzettoIO.bind g m) |> runIO
        let rhs = BozzettoIO.bind (fun x -> BozzettoIO.bind h (g x)) m |> runIO
        lhs |> Expect.equal "bind h (bind g m) = bind (fun x -> bind h (g x)) m" rhs
  ]

// ── ofExn ───────────────────────────────────────────────────────────────

[<Tests>]
let ofExnTests =
  testList "BozzettoIO.ofExn" [

    testCase "WHY — a throwing async becomes Error (classify ex), never a raw exception" <| fun _ ->
      let boom : Async<int> = async { return failwith "boom" }
      let classify (ex: exn) = BozzettoError.EvalFailed ex.Message
      let result = BozzettoIO.ofExn classify boom |> runIO
      result |> Expect.equal "classified error" (Error (BozzettoError.EvalFailed "boom"))

    testCase "WHY — the exact classifier passed in is the one applied, not some fixed default" <| fun _ ->
      let boom : Async<int> = async { return raise (InvalidOperationException "nope") }
      let classify (ex: exn) = BozzettoError.CheckFailed (sprintf "custom: %s" ex.Message)
      let result = BozzettoIO.ofExn classify boom |> runIO
      result |> Expect.equal "custom classification applied" (Error (BozzettoError.CheckFailed "custom: nope"))

    testCase "WHY — a succeeding async still becomes Ok, unaffected by the presence of a classifier" <| fun _ ->
      let succeed : Async<int> = async { return 42 }
      let classify (_: exn) = BozzettoError.Unexpected (Exception "should never run")
      let result = BozzettoIO.ofExn classify succeed |> runIO
      result |> Expect.equal "Ok on success" (Ok 42)

    testCase "WHY — a cancellation is never classified as a domain failure; it propagates as a cancellation" <| fun _ ->
      use cts = new Threading.CancellationTokenSource()
      let neverCompletes : Async<int> =
        async {
          do! Async.Sleep(60000)
          return 0
        }
      let classify (_: exn) = BozzettoError.Unexpected (Exception "must not classify a cancellation")
      let io = BozzettoIO.ofExn classify neverCompletes
      cts.CancelAfter(50)
      let mutable cancelled = false
      try
        Async.RunSynchronously(io, cancellationToken = cts.Token) |> ignore
      with :? OperationCanceledException -> cancelled <- true
      cancelled |> Expect.isTrue "cancellation propagated instead of being classified"
  ]

// ── timeout ─────────────────────────────────────────────────────────────

[<Tests>]
let timeoutTests =
  testList "BozzettoIO.timeout" [

    testCase "WHY — an async that exceeds the span yields Error <the passed error>" <| fun _ ->
      let slow : BozzettoIO<int> =
        async {
          do! Async.Sleep(2000)
          return Ok 1
        }
      let onTimeout = BozzettoError.WorkerTimeout ("s1", "eval", 0.05)
      let result = BozzettoIO.timeout (TimeSpan.FromMilliseconds 50.0) onTimeout slow |> runIO
      result |> Expect.equal "times out with the given error" (Error onTimeout)

    testCase "WHY — an async that finishes in time yields its own result, not the timeout error" <| fun _ ->
      let fast : BozzettoIO<int> = BozzettoIO.ret 7
      let onTimeout = BozzettoError.WorkerTimeout ("s1", "eval", 5.0)
      let result = BozzettoIO.timeout (TimeSpan.FromSeconds 5.0) onTimeout fast |> runIO
      result |> Expect.equal "finishes with its own value" (Ok 7)

    testCase "WHY — a fast failure still beats the deadline with its own error, not the timeout error" <| fun _ ->
      let fastFailure : BozzettoIO<int> = BozzettoIO.ofResult (Error (BozzettoError.EvalFailed "fast failure"))
      let onTimeout = BozzettoError.WorkerTimeout ("s1", "eval", 5.0)
      let result = BozzettoIO.timeout (TimeSpan.FromSeconds 5.0) onTimeout fastFailure |> runIO
      result |> Expect.equal "own failure wins over the deadline" (Error (BozzettoError.EvalFailed "fast failure"))
  ]

// ── race ────────────────────────────────────────────────────────────────

[<Tests>]
let raceTests =
  testList "BozzettoIO.race" [

    testCase "WHY — race returns the faster side's result" <| fun _ ->
      let slow : BozzettoIO<string> =
        async {
          do! Async.Sleep(2000)
          return Ok "slow"
        }
      let fast : BozzettoIO<string> =
        async {
          do! Async.Sleep(10)
          return Ok "fast"
        }
      let result = BozzettoIO.race slow fast |> runIO
      result |> Expect.equal "fast side wins" (Ok "fast")

    testCase "WHY — race also returns a fast failure over a slow success" <| fun _ ->
      let slowSuccess : BozzettoIO<string> =
        async {
          do! Async.Sleep(2000)
          return Ok "slow"
        }
      let fastFailure : BozzettoIO<string> =
        async {
          do! Async.Sleep(10)
          return Error (BozzettoError.EvalFailed "fast failure")
        }
      let result = BozzettoIO.race slowSuccess fastFailure |> runIO
      result |> Expect.equal "fast failure wins" (Error (BozzettoError.EvalFailed "fast failure"))
  ]

// ── retryUntil ──────────────────────────────────────────────────────────

[<Tests>]
let retryUntilTests =
  testList "BozzettoIO.retryUntil" [

    testCase "WHY — retries until the predicate holds, then stops" <| fun _ ->
      let mutable attempts = 0
      let io : BozzettoIO<int> =
        async {
          attempts <- attempts + 1
          return
            match attempts >= 3 with
            | true -> Ok attempts
            | false -> Error (BozzettoError.EvalFailed "not yet")
        }
      let result = BozzettoIO.retryUntil Result.isOk 10 io |> runIO
      result |> Expect.equal "stopped once Ok" (Ok 3)
      attempts |> Expect.equal "attempted exactly 3 times" 3

    testCase "WHY — gives up after maxAttempts, returning the last (failing) outcome" <| fun _ ->
      let mutable attempts = 0
      let io : BozzettoIO<int> =
        async {
          attempts <- attempts + 1
          return Error (BozzettoError.EvalFailed "always fails")
        }
      let result = BozzettoIO.retryUntil Result.isOk 4 io |> runIO
      result |> Expect.equal "last failing outcome returned" (Error (BozzettoError.EvalFailed "always fails"))
      attempts |> Expect.equal "attempted exactly maxAttempts times" 4

    testCase "WHY — a predicate that holds immediately runs the effect exactly once" <| fun _ ->
      let mutable attempts = 0
      let io : BozzettoIO<int> =
        async {
          attempts <- attempts + 1
          return Ok attempts
        }
      let result = BozzettoIO.retryUntil Result.isOk 10 io |> runIO
      result |> Expect.equal "Ok on first attempt" (Ok 1)
      attempts |> Expect.equal "only one attempt needed" 1
  ]

// ── sageIO computation expression ──────────────────────────────────────

[<Tests>]
let sageIOBuilderTests =
  testList "sageIO builder" [

    testCase "WHY — sageIO { let! } short-circuits on the first Error, like bind does" <| fun _ ->
      let io =
        sageIO {
          let! a = BozzettoIO.ret 1
          let! b = BozzettoIO.ofResult (Error (BozzettoError.EvalFailed "stop here") : Result<int, BozzettoError>)
          let! c = BozzettoIO.ret (a + b)
          return c
        }
      io |> runIO |> Expect.equal "short-circuited" (Error (BozzettoError.EvalFailed "stop here"))

    testCase "WHY — sageIO { let! } composes successes through to the final return" <| fun _ ->
      let io =
        sageIO {
          let! a = BozzettoIO.ret 1
          let! b = BozzettoIO.ret 2
          return a + b
        }
      io |> runIO |> Expect.equal "composed successfully" (Ok 3)
  ]
