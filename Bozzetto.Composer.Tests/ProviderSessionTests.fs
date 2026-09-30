module Bozzetto.Composer.Tests.ProviderSessionTests

open System
open System.Threading
open System.Threading.Tasks
open Bozzetto.Providers
open Expecto
open Expecto.Flip

type private Ticket(generation: int64) =
  member _.Generation = generation

let private artifact generation : AcceptedArtifact = {
  Generation = generation
  SourceVersion = sprintf "source-%d" generation
  ArtifactPath = sprintf "/artifacts/%d/program" generation
  ArtifactSha256 = sprintf "digest-%d" generation
  ObjectManifest = "/artifacts/objects.json"
  ChangedWitnesses = [||]
  RetainedWitnesses = [||]
  RetiredWitnesses = [||]
  WitnessVisits = [||]
  CompiledObjects = [||]
  ReusedObjects = [||]
  RetiredObjects = [||]
}

/// Ignores cancellation deliberately: the provider must reject late successful
/// replies even when a compiler or process cannot stop immediately.
type private Backend() =
  let gate = obj ()
  let buildEntered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let buildRelease = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let runEntered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let runRelease = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let mutable generation = 0L
  let mutable current: AcceptedArtifact option = None
  let mutable ticket: Ticket option = None
  let mutable disposed = false
  let mutable builds = 0
  let mutable runs = 0
  let mutable disposals = 0
  let mutable currentReads = 0
  let reserveLabels = Collections.Concurrent.ConcurrentQueue<string>()
  let mutable buildCancellation = CancellationToken.None
  member val HoldBuild = false with get, set
  member val HoldRun = false with get, set
  member val AfterReserve: string -> unit = ignore with get, set
  member val BeforeRun: unit -> unit = ignore with get, set
  member val BeforePublish: unit -> unit = ignore with get, set
  member val BeforeDispose: unit -> unit = ignore with get, set
  member _.BuildEntered = buildEntered.Task
  member _.RunEntered = runEntered.Task
  member _.ReleaseBuild() = buildRelease.TrySetResult() |> ignore
  member _.ReleaseRun() = runRelease.TrySetResult() |> ignore
  member _.BuildCount = builds
  member _.RunCount = runs
  member _.DisposeCount = disposals
  member _.CurrentReads = currentReads
  member _.ReserveLabels = reserveLabels.ToArray()
  member _.BuildCancellation = buildCancellation
  member _.Disposed = disposed

  interface IProjectBackend with
    member _.ManifestPath = "/project/program.fidproj"
    member _.Current = lock gate (fun () -> currentReads <- currentReads + 1; current)
    member this.Reserve label =
      lock gate (fun () ->
        reserveLabels.Enqueue label
        generation <- generation + 1L
        current <- None
        let next = Ticket generation
        ticket <- Some next
        this.AfterReserve label
        box next)
    member this.BuildAsync(value, cancellation) = task {
      let selected = value :?> Ticket
      builds <- builds + 1
      buildCancellation <- cancellation
      buildEntered.TrySetResult() |> ignore
      if this.HoldBuild then do! buildRelease.Task
      let result = artifact selected.Generation
      lock gate (fun () ->
        this.BeforePublish()
        if not disposed && (ticket |> Option.exists (fun latest -> obj.ReferenceEquals(latest, selected))) then
          current <- Some result)
      return Result.Ok result
    }
    member this.RunCurrentAsync(_, _) = task {
      let selected = lock gate (fun () -> this.BeforeRun(); runs <- runs + 1; current.Value)
      runEntered.TrySetResult() |> ignore
      if this.HoldRun then do! runRelease.Task
      return Result.Ok {
        Generation = selected.Generation; SourceVersion = selected.SourceVersion
        ExitCode = 0; StandardOutput = "hello"; StandardError = ""
      }
    }
    member this.Dispose() =
      lock gate (fun () ->
        disposals <- disposals + 1
        disposed <- true
        current <- None
        this.BeforeDispose())

/// Stops inside a synchronous compiler prefix, before it can return its task.
type private PrefixBarrier() =
  let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let released = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  member _.Entered = entered.Task
  member _.Block() =
    entered.TrySetResult() |> ignore
    released.Task.GetAwaiter().GetResult()
  member _.Release() = released.TrySetResult() |> ignore
  interface IDisposable with
    member this.Dispose() = this.Release()

let private success (response: Reply<'a>) =
  match response.Outcome with
  | Result.Ok value -> value
  | Result.Error refusal -> failtestf "Unexpected refusal %s: %s" refusal.Code refusal.Message

let private refuses code (response: Reply<'a>) =
  match response.Outcome with
  | Result.Error refusal -> refusal.Code |> Expect.equal "refusal reason" code
  | Result.Ok _ -> failtestf "Expected %s refusal, got success" code

let private bounded (work: Task<'a>) = work.WaitAsync(TimeSpan.FromSeconds 5.)

let private taskCase name work =
  testCaseAsync name (async { do! work () |> bounded |> Async.AwaitTask })

let private session id epoch backend =
  new ProviderSession("test-host", id, epoch, "/project/program.fidproj", backend)

let private build (session: ProviderSession) label =
  session.BuildAsync(session.Reserve(label) |> success, CancellationToken.None)

let private preCanceledFailure runOperation () = task {
  let backend = new Backend()
  use owner = session "one" "epoch-a" backend
  let token = owner.Reserve "initial" |> success
  if runOperation then
    let! accepted = owner.BuildAsync(token, CancellationToken.None)
    accepted |> success |> ignore
  let withdrawalEntered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  use recovery = new PrefixBarrier()
  backend.AfterReserve <- fun label ->
    if label = "canceled" then
      withdrawalEntered.TrySetResult() |> ignore
      failwith "injected persistence failure after compiler mutation"
    elif label = "recovery" then recovery.Block()
  use cancellation = new CancellationTokenSource()
  cancellation.Cancel()
  let withoutValue (response: Reply<'a>) = { Authority = response.Authority; Outcome = response.Outcome |> Result.map ignore }
  let! response =
    if runOperation then task {
      let! response = owner.RunAsync([], cancellation.Token)
      return withoutValue response
    }
    else task {
      let! response = owner.BuildAsync(token, cancellation.Token)
      return withoutValue response
    }
  response |> refuses "superseded"
  do! bounded withdrawalEntered.Task
  let repaired = owner.ReserveAsync "recovery"
  do! bounded recovery.Entered
  let status = owner.Status() |> success
  status.Busy |> Expect.isFalse "pre-canceled registration leaves no active operation"
  status.Current |> Expect.isNone "failed backend withdrawal grants no artifact authority"
  status.BackendError.IsSome |> Expect.isTrue "physical withdrawal failure remains visible"
  backend.RunCount |> Expect.equal "pre-canceled run never reaches native launch" 0
  backend.BuildCount |> Expect.equal "pre-canceled build never reaches compiler" (if runOperation then 1 else 0)
  recovery.Release()
  let! reservation = bounded repaired
  let! accepted = owner.BuildAsync(success reservation, CancellationToken.None)
  accepted |> success |> ignore
  let! run = owner.RunAsync([], CancellationToken.None)
  run |> success |> ignore
  (owner.Status() |> success).BackendError |> Expect.isNone "successful reservation clears prior withdrawal error"
}

[<Tests>]
let tests =
  testList "Composer provider session authority" [
    testCase "provider parsing refuses unknown identities instead of selecting FSharp" <| fun _ ->
      ProviderIdentity.parse "fsharp" |> Expect.equal "explicit FSharp" (Result.Ok ProviderIdentity.FSharp)
      ProviderIdentity.parse "clef-composer" |> Expect.equal "explicit Composer" (Result.Ok ProviderIdentity.ClefComposer)
      for unknown in [ ""; "composer"; "unknown"; "FSharp"; null ] do
        ProviderIdentity.parse unknown |> Result.isError |> Expect.isTrue "unknown identity is refused"

    taskCase "reserving an edit immediately withdraws a previously runnable artifact" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      (owner.Status() |> success).Current.IsSome |> Expect.isTrue "initial build is accepted"
      let reserved = owner.Reserve "edited"
      reserved |> success |> ignore
      reserved.Authority.Generation |> Expect.equal "new edit advances authority" 2L
      (owner.Status() |> success).Current |> Expect.isNone "edit withdraws current"
      let! run = owner.RunAsync([], CancellationToken.None)
      run |> refuses "not_accepted"
    }

    taskCase "foreign reservation cannot consume the owner's valid ticket" <| fun () -> task {
      let firstBackend = new Backend()
      let secondBackend = new Backend()
      use first = session "first" "epoch-a" firstBackend
      use second = session "second" "epoch-a" secondBackend
      let firstToken = first.Reserve "first edit" |> success
      let secondToken = second.Reserve "second edit" |> success
      let! refused = second.BuildAsync(firstToken, CancellationToken.None)
      refused |> refuses "invalid_reservation"
      secondBackend.BuildCount |> Expect.equal "foreign token never enters compiler" 0
      let! accepted = second.BuildAsync(secondToken, CancellationToken.None)
      accepted |> success |> ignore
      accepted.Authority.Session |> Expect.equal "right session remains usable" "second"
    }

    taskCase "a reservation is single use even while its first build is pending" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      let token = owner.Reserve "edit" |> success
      let pending = owner.BuildAsync(token, CancellationToken.None)
      do! bounded backend.BuildEntered
      let! duplicate = owner.BuildAsync(token, CancellationToken.None)
      duplicate |> refuses "invalid_reservation"
      backend.BuildCount |> Expect.equal "only original build reaches compiler" 1
      backend.ReleaseBuild()
      let! accepted = bounded pending
      accepted |> success |> ignore
      let! replay = owner.BuildAsync(token, CancellationToken.None)
      replay |> refuses "invalid_reservation"
    }

    taskCase "pending build permits status and replacement reservation and rejects old success" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      let pending = build owner "old edit"
      do! bounded backend.BuildEntered
      (owner.Status() |> success).Busy |> Expect.isTrue "compiler work is visible"
      let replacement = owner.Reserve "new edit"
      replacement |> success |> ignore
      pending.IsCompleted |> Expect.isFalse "replacement does not wait for old compiler work"
      backend.ReleaseBuild()
      let! refused = bounded pending
      refused |> refuses "superseded"
      refused.Authority.Generation |> Expect.equal "late reply retains originating authority" 1L
      owner.Identity.Generation |> Expect.equal "session retains replacement authority" 2L
      (owner.Status() |> success).Current |> Expect.isNone "old success cannot revive an artifact"
      let! accepted = owner.BuildAsync(success replacement, CancellationToken.None)
      (accepted |> success).Generation |> Expect.equal "replacement remains buildable" 2L
    }

    taskCase "cancel withdraws authority while uncooperative build remains pending" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      let pending = build owner "edit"
      do! bounded backend.BuildEntered
      owner.Cancel() |> success |> ignore
      owner.Identity.Generation |> Expect.equal "cancel invalidates generation" 2L
      pending.IsCompleted |> Expect.isFalse "cancel does not await compiler completion"
      (owner.Status() |> success).Current |> Expect.isNone "canceled authority has no artifact"
      backend.ReleaseBuild()
      let! response = bounded pending
      response |> refuses "superseded"
      (owner.Status() |> success).Busy |> Expect.isFalse "completed work leaves active set"
    }

    taskCase "caller cancellation withdraws artifact authority before held work completes" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      use cancellation = new CancellationTokenSource()
      let pending = owner.BuildAsync(owner.Reserve "edit" |> success, cancellation.Token)
      do! bounded backend.BuildEntered
      cancellation.Cancel()
      owner.Identity.Generation |> Expect.equal "caller cancellation advances authority" 2L
      backend.BuildCancellation.IsCancellationRequested |> Expect.isTrue "compiler receives cancellation"
      (owner.Status() |> success).Current |> Expect.isNone "authority is withdrawn immediately"
      backend.ReleaseBuild()
      let! response = bounded pending
      response |> refuses "superseded"
      let! run = owner.RunAsync([], CancellationToken.None)
      run |> refuses "not_accepted"
    }

    taskCase "closing a session retires held work and permanently refuses new operations" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      let reserved = owner.Reserve "edit" |> success
      let pending = owner.BuildAsync(reserved, CancellationToken.None)
      do! bounded backend.BuildEntered
      owner.Close() |> success |> ignore
      backend.Disposed |> Expect.isTrue "backend retires with session"
      let snapshot = owner.Status() |> success
      snapshot.Closed |> Expect.isTrue "closed state is visible before completion"
      snapshot.Current |> Expect.isNone "closed session has no current artifact"
      owner.Reserve "late edit" |> refuses "closed"
      owner.Cancel() |> refuses "closed"
      let! lateBuild = owner.BuildAsync(reserved, CancellationToken.None)
      lateBuild |> refuses "closed"
      let! lateRun = owner.RunAsync([], CancellationToken.None)
      lateRun |> refuses "closed"
      backend.ReleaseBuild()
      let! response = bounded pending
      response |> refuses "closed"
    }

    taskCase "compiler replacement cannot lend a new epoch's authority to old work" <| fun () -> task {
      let oldBackend = new Backend(HoldBuild = true)
      use oldSession = session "project" "retired-epoch" oldBackend
      let oldToken = oldSession.Reserve "old edit" |> success
      let pending = oldSession.BuildAsync(oldToken, CancellationToken.None)
      do! bounded oldBackend.BuildEntered
      oldSession.Close() |> success |> ignore
      let freshBackend = new Backend()
      use fresh = session "project" "replacement-epoch" freshBackend
      let! foreign = fresh.BuildAsync(oldToken, CancellationToken.None)
      foreign |> refuses "invalid_reservation"
      let! freshBuild = build fresh "fresh edit"
      freshBuild |> success |> ignore
      freshBuild.Authority.Epoch |> Expect.equal "fresh result carries new compiler epoch" "replacement-epoch"
      oldBackend.ReleaseBuild()
      let! oldResult = bounded pending
      oldResult |> refuses "closed"
      oldResult.Authority.Epoch |> Expect.equal "old completion remains attributable to retired compiler" "retired-epoch"
      (fresh.Status() |> success).Current.IsSome |> Expect.isTrue "late old work leaves fresh session intact"
    }

    taskCase "a running artifact's result is refused if a newer edit supersedes it" <| fun () -> task {
      let backend = new Backend(HoldRun = true)
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      let pending = owner.RunAsync([ "argument" ], CancellationToken.None)
      do! bounded backend.RunEntered
      owner.Reserve "replacement" |> success |> ignore
      backend.ReleaseRun()
      let! response = bounded pending
      response |> refuses "superseded"
      response.Authority.Generation |> Expect.equal "execution result keeps old generation" 1L
      (owner.Status() |> success).Current |> Expect.isNone "old execution does not restore current"
    }

    taskCase "canceling one project leaves another project's artifact executable" <| fun () -> task {
      let firstBackend = new Backend(HoldBuild = true)
      let secondBackend = new Backend()
      use first = session "first" "epoch-a" firstBackend
      use second = session "second" "epoch-a" secondBackend
      let firstBuild = build first "first edit"
      do! bounded firstBackend.BuildEntered
      let! accepted = build second "second edit"
      accepted |> success |> ignore
      first.Cancel() |> success |> ignore
      let! run = second.RunAsync([], CancellationToken.None)
      (run |> success).StandardOutput |> Expect.equal "unrelated accepted artifact runs" "hello"
      second.Identity.Generation |> Expect.equal "unrelated authority is stable" 1L
      firstBackend.ReleaseBuild()
      let! canceled = bounded firstBuild
      canceled |> refuses "superseded"
    }

    taskCase "pre-canceled build clears activity when compiler withdrawal fails after mutation" (preCanceledFailure false)

    taskCase "pre-canceled run clears activity without launching when compiler withdrawal fails" (preCanceledFailure true)

    taskCase "synchronous run prefix cannot block status cancellation or logical retirement" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      use prefix = new PrefixBarrier()
      backend.BeforeRun <- prefix.Block
      let pending = owner.RunAsync([], CancellationToken.None)
      do! bounded prefix.Entered
      let! status = Task.Run(fun () -> owner.Status()) |> bounded
      (status |> success).Busy |> Expect.isTrue "held synchronous prefix is active"
      let! canceled = Task.Run(fun () -> owner.Cancel()) |> bounded
      canceled |> success |> ignore
      let! closed = Task.Run(fun () -> owner.BeginClose()) |> bounded
      closed |> success |> ignore
      let snapshot = owner.Status() |> success
      snapshot.Closed |> Expect.isTrue "logical retirement is immediate"
      snapshot.Current |> Expect.isNone "logical retirement withdraws current"
      snapshot.CleanupPending |> Expect.isTrue "physical cleanup is not misreported complete"
      let cleanup = owner.CloseAsync()
      cleanup.IsCompleted |> Expect.isFalse "cleanup honestly remains pending behind compiler prefix"
      prefix.Release()
      let! result = bounded pending
      result |> refuses "closed"
      let! cleaned = bounded cleanup
      cleaned |> success |> ignore
      (owner.Status() |> success).CleanupPending |> Expect.isFalse "cleanup completion is observable"
      backend.CurrentReads |> Expect.equal "supervision never contends on compiler Current" 0
    }

    taskCase "pending reservation grants no edit permission before old run selection completes" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      use prefix = new PrefixBarrier()
      backend.BeforeRun <- prefix.Block
      let oldRun = owner.RunAsync([], CancellationToken.None)
      do! bounded prefix.Entered
      let next = owner.ReserveAsync "new edit"
      next.IsCompleted |> Expect.isFalse "backend reservation must complete before editing is authorized"
      (owner.Status() |> success).Current |> Expect.isNone "pending reservation revokes local authority"
      prefix.Release()
      let! oldResult = bounded oldRun
      oldResult |> refuses "superseded"
      let! nextToken = bounded next
      let! rebuilt = owner.BuildAsync(success nextToken, CancellationToken.None)
      (rebuilt |> success).Generation |> Expect.equal "new compiler generation remains independently buildable" 2L
      let! newRun = owner.RunAsync([], CancellationToken.None)
      (newRun |> success).Generation |> Expect.equal "later execution selects later artifact" 2L
    }

    taskCase "obsolete queued withdrawals cannot invalidate a newer reservation" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      use prefix = new PrefixBarrier()
      backend.BeforeRun <- prefix.Block
      let oldRun = owner.RunAsync([], CancellationToken.None)
      do! bounded prefix.Entered
      owner.Cancel() |> success |> ignore
      owner.Cancel() |> success |> ignore
      let next = owner.ReserveAsync "surviving edit"
      prefix.Release()
      let! token = bounded next
      token.Authority.Generation |> Expect.equal "logical revisions include both cancellations" 4L
      let! accepted = owner.BuildAsync(success token, CancellationToken.None)
      (accepted |> success).Generation |> Expect.equal "compiler generation is preserved without fabricated logical numbering" 2L
      accepted.Authority.Generation |> Expect.equal "response retains independent logical authority" 4L
      backend.ReserveLabels |> Expect.equal "obsolete queued compiler mutations are skipped" [| "initial"; "surviving edit" |]
      let! refused = bounded oldRun
      refused |> refuses "superseded"
      let! result = owner.RunAsync([], CancellationToken.None)
      (result |> success).Generation |> Expect.equal "new reservation survives old cancellation queue" 2L
    }

    taskCase "compiler publication gate cannot block adapter status or cancellation" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      use publication = new PrefixBarrier()
      backend.BeforePublish <- publication.Block
      let pending = build owner "held publication"
      do! bounded publication.Entered
      let! status = Task.Run(fun () -> owner.Status()) |> bounded
      (status |> success).Current |> Expect.isNone "unobserved compiler acceptance is not provider acceptance"
      let! canceled = Task.Run(fun () -> owner.Cancel()) |> bounded
      canceled |> success |> ignore
      publication.Release()
      let! result = bounded pending
      result |> refuses "superseded"
      (owner.Status() |> success).Current |> Expect.isNone "late compiler publication cannot restore adapter authority"
      backend.CurrentReads |> Expect.equal "status never reads blocking compiler Current" 0
    }

    taskCase "failed reservation revokes cached artifact even when compiler mutated before throwing" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      backend.AfterReserve <- fun label -> if label = "broken edit" then failwith "status persistence failed"
      let! failure = owner.ReserveAsync "broken edit"
      failure |> refuses "backend_failed"
      failure.Authority.Generation |> Expect.equal "failed mutation still advances logical authority" 2L
      (owner.Status() |> success).Current |> Expect.isNone "failure does not resurrect previous artifact"
      let! refused = owner.RunAsync([], CancellationToken.None)
      refused |> refuses "not_accepted"
      let! repaired = build owner "repaired edit"
      repaired |> success |> ignore
      let! runnable = owner.RunAsync([], CancellationToken.None)
      runnable |> success |> ignore
    }

    taskCase "physical cleanup failure remains visible and is retried after logical retirement" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      backend.BeforeDispose <- fun () -> failwith "closed manifest cannot be persisted"
      let! first = owner.CloseAsync()
      first |> refuses "cleanup_failed"
      let failed = owner.Status() |> success
      failed.Closed |> Expect.isTrue "cleanup failure cannot restore logical session"
      failed.CleanupPending |> Expect.isTrue "failed cleanup remains incomplete"
      failed.CleanupError.IsSome |> Expect.isTrue "cleanup error remains visible"
      failed.Current |> Expect.isNone "retirement revokes artifact despite cleanup failure"
      let! refused = owner.RunAsync([], CancellationToken.None)
      refused |> refuses "closed"
      let! repeated = owner.CloseAsync()
      repeated |> refuses "cleanup_failed"
      backend.DisposeCount |> Expect.equal "repeated close attempts cleanup instead of reporting false success" 2
      backend.BeforeDispose <- ignore
      let! repaired = owner.CloseAsync()
      repaired |> success |> ignore
      let cleaned = owner.Status() |> success
      cleaned.CleanupPending |> Expect.isFalse "successful retry completes cleanup"
      cleaned.CleanupError |> Expect.isNone "successful retry clears cleanup failure"
    }

    taskCase "held physical disposal cannot block status or another session's logical retirement" <| fun () -> task {
      let firstBackend = new Backend()
      let secondBackend = new Backend()
      use first = session "first" "epoch-a" firstBackend
      use second = session "second" "epoch-a" secondBackend
      let! accepted = build second "surviving artifact"
      accepted |> success |> ignore
      use cleanup = new PrefixBarrier()
      firstBackend.BeforeDispose <- cleanup.Block
      let pending = first.CloseAsync()
      do! bounded cleanup.Entered
      let! status = Task.Run(fun () -> first.Status()) |> bounded
      (status |> success).CleanupPending |> Expect.isTrue "blocked cleanup remains visible"
      let! retired = Task.Run(fun () -> second.BeginClose()) |> bounded
      retired |> success |> ignore
      let! refused = second.RunAsync([], CancellationToken.None)
      refused |> refuses "closed"
      cleanup.Release()
      let! cleaned = bounded pending
      cleaned |> success |> ignore
    }
  ]
