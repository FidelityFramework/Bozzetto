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
  let buildReturned = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
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
  let registrations = ResizeArray<CancellationTokenRegistration>()
  member val HoldBuild = false with get, set
  member val BuildFailure: string option = None with get, set
  member val HoldRun = false with get, set
  member val RefuseRun = false with get, set
  member val WithdrawOnRunRefusal = false with get, set
  member val AfterReserve: string -> unit = ignore with get, set
  member val BeforeRun: unit -> unit = ignore with get, set
  member val BeforePublish: unit -> unit = ignore with get, set
  member val BeforeDispose: unit -> unit = ignore with get, set
  member val OnCancellation: unit -> unit = ignore with get, set
  member _.BuildEntered = buildEntered.Task
  member _.BuildReturned = buildReturned.Task
  member _.RunEntered = runEntered.Task
  member _.ReleaseBuild() = buildRelease.TrySetResult() |> ignore
  member _.ReleaseRun() = runRelease.TrySetResult() |> ignore
  member _.BuildCount = builds
  member _.RunCount = lock gate (fun () -> runs)
  member _.DisposeCount = disposals
  member _.CurrentReads = currentReads
  member _.ReserveLabels = reserveLabels.ToArray()
  member _.BuildCancellation = buildCancellation
  member _.Disposed = disposed

  interface IProjectBackend<Ticket> with
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
        next)
    member this.BuildAsync(value, cancellation) = task {
      let selected = value
      let failure = this.BuildFailure
      builds <- builds + 1
      buildCancellation <- cancellation
      let registration = cancellation.Register(fun () -> this.OnCancellation())
      lock registrations (fun () -> registrations.Add registration)
      buildEntered.TrySetResult() |> ignore
      if this.HoldBuild then do! buildRelease.Task
      let result =
        match failure with
        | Some message -> Result.Error message
        | None ->
          let accepted = artifact selected.Generation
          lock gate (fun () ->
            this.BeforePublish()
            if not disposed && (ticket |> Option.exists (fun latest -> obj.ReferenceEquals(latest, selected))) then
              current <- Some accepted)
          Result.Ok accepted
      buildReturned.TrySetResult() |> ignore
      return result
    }
    member this.RunCurrentAsync(_, _) = task {
      let selected = lock gate (fun () -> this.BeforeRun(); runs <- runs + 1; current.Value)
      runEntered.TrySetResult() |> ignore
      if this.HoldRun then do! runRelease.Task
      if this.RefuseRun then
        lock gate (fun () ->
          if this.WithdrawOnRunRefusal && current = Some selected then current <- None)
        return Result.Error "The compiler refused this run."
      else
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
      lock registrations (fun () ->
        for registration in registrations do registration.Dispose()
        registrations.Clear())

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
  new ProviderSession<Ticket>("test-host", id, epoch, "/project/program.fidproj", backend)

let private build (session: ProviderSession<Ticket>) label =
  session.BuildAsync(session.Reserve(label) |> success, CancellationToken.None)

let private settledStatus (owner: ProviderSession<Ticket>) = task {
  let deadline = Diagnostics.Stopwatch.StartNew()
  let mutable status = owner.Status() |> success
  while status.Busy do
    if deadline.Elapsed > TimeSpan.FromSeconds 5. then failtest "The provider did not physically settle its build."
    do! Task.Yield()
    status <- owner.Status() |> success
  return status
}

let private compilerDiagnostic = "NativeCallbacks.clef:12:7 CCS8403: BoundaryEmission requires settled ownership.\nComposer exited with 26 compilation errors."

let private preCanceledRequest runOperation () = task {
  let backend = new Backend()
  use owner = session "one" "epoch-a" backend
  let token = owner.Reserve "initial" |> success
  if runOperation then
    let! accepted = owner.BuildAsync(token, CancellationToken.None)
    accepted |> success |> ignore
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
  response |> refuses "canceled"
  let status = owner.Status() |> success
  status.Busy |> Expect.isFalse "pre-canceled registration leaves no active operation"
  status.Current.IsSome |> Expect.equal "pre-canceled observation leaves existing artifact authority intact" runOperation
  owner.Identity.Generation |> Expect.equal "pre-canceled request never revokes generation" 1L
  backend.ReserveLabels |> Expect.equal "caller cancellation is not a compiler reservation" [| "initial" |]
  backend.RunCount |> Expect.equal "pre-canceled run never reaches native launch" 0
  backend.BuildCount |> Expect.equal "pre-canceled build never reaches compiler" (if runOperation then 1 else 0)
  let! accepted = owner.BuildAsync(token, CancellationToken.None)
  accepted |> success |> ignore
  let! run = owner.RunAsync([], CancellationToken.None)
  run |> success |> ignore
  (owner.Status() |> success).BackendError |> Expect.isNone "no backend mutation failed"
}

[<Tests>]
let tests =
  testList "Composer provider session authority" [
    testCase "producer failure evidence records exact diagnostic and owning revision without global console capture" <| fun _ ->
      use writer = new System.IO.StringWriter()
      let authority: Authority =
        { Host = "host-a"; Session = "session-b"; Provider = ProviderIdentity.ClefComposer
          Epoch = "epoch-c"; Generation = 7L }
      ProviderDiagnostics.reportFailure writer authority (Fidelity.FSharp.Incremental.ScopeId 11UL) (Fidelity.FSharp.Incremental.WorkId 13UL) compilerDiagnostic
      let evidence = writer.ToString()
      evidence |> Expect.stringContains "failure retains the exact diagnostic text" compilerDiagnostic
      evidence |> Expect.stringContains "evidence names the exact producer rather than current session state" "host=host-a session=session-b epoch=epoch-c generation=7 scope=11 work=13 compiler_refused: "
      evidence.Split([| "compiler_refused: " |], StringSplitOptions.None).Length |> Expect.equal "one report emits one scoped failure" 2

    testCase "provider parsing refuses unknown identities instead of selecting FSharp" <| fun _ ->
      ProviderIdentity.parse "fsharp" |> Expect.equal "explicit FSharp" (Result.Ok ProviderIdentity.FSharp)
      ProviderIdentity.parse "clef-composer" |> Expect.equal "explicit Composer" (Result.Ok ProviderIdentity.ClefComposer)
      for unknown in [ ""; "composer"; "unknown"; "FSharp"; null ] do
        ProviderIdentity.parse unknown |> Result.isError |> Expect.isTrue "unknown identity is refused"

    taskCase "status observations order actual capture despite reversed caller dispatch" <| fun () -> task {
      let backend = new Backend()
      use owner = session "observation-order" "epoch-a" backend
      let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      let earlierCaller = task {
        entered.TrySetResult() |> ignore
        do! release.Task
        return owner.Status()
      }
      do! entered.Task
      let first = owner.Status()
      release.TrySetResult() |> ignore
      let! second = earlierCaller
      first.Authority |> Expect.equal "same compiler authority throughout both reads" second.Authority
      (success first).Observation |> Expect.equal "first actual capture starts the sequence" 1UL
      (success second).Observation |> Expect.equal "earlier caller captures later and receives later sequence" 2UL
      let! captures = [| for _ in 1 .. 32 -> Task.Run(fun () -> owner.Status() |> success) |] |> Task.WhenAll
      captures |> Array.toList |> List.map _.Observation |> List.sort
      |> Expect.equal "concurrent captures have distinct contiguous ordering" [ 3UL .. 34UL ]
      owner.Identity.Generation |> Expect.equal "observing never advances compiler generation" 0L
    }

    taskCase "same-generation busy and accepted snapshots retain their own observation order" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "observation-state" "epoch-a" backend
      let reservation = owner.Reserve "initial" |> success
      let before = owner.Status()
      let building = owner.BuildAsync(reservation, CancellationToken.None)
      do! backend.BuildEntered
      let during = owner.Status()
      backend.ReleaseBuild()
      let! accepted = building
      accepted |> success |> ignore
      let after = owner.Status()
      before.Authority |> Expect.equal "build start does not alter reservation authority" during.Authority
      during.Authority |> Expect.equal "build completion keeps reservation authority" after.Authority
      (success before).Busy |> Expect.isFalse "before dispatch is idle"
      (success during).Busy |> Expect.isTrue "held compiler remains active"
      (success after).Current.IsSome |> Expect.isTrue "latest observation contains accepted artifact"
      [ success before; success during; success after ] |> List.map _.Observation
      |> Expect.equal "state captures advance independently of compiler generations" [ 1UL; 2UL; 3UL ]
      (success before).Current |> Expect.isNone "retained earlier snapshot is immutable"
    }

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

    taskCase "compiler run withdrawal invalidates current and completed build replay" <| fun () -> task {
      let backend = new Backend(RefuseRun = true, WithdrawOnRunRefusal = true)
      use owner = session "compiler-withdrawal" "epoch-a" backend
      let ticket = owner.Reserve "initial" |> success
      let! built = owner.BuildAsync(ticket, CancellationToken.None)
      built |> success |> ignore
      let! refused = owner.RunAsync([], CancellationToken.None)
      refused |> refuses "compiler_refused"
      (owner.Status() |> success).Current |> Expect.isNone "compiler withdrawal reaches the provider before the refusal"
      let! replay = owner.BuildAsync(ticket, CancellationToken.None)
      replay |> refuses "invalid_reservation"
      let! run = owner.RunAsync([], CancellationToken.None)
      run |> refuses "not_accepted"
      backend.BuildCount |> Expect.equal "withdrawn replay never restarts a consumed compiler ticket" 1
      backend.RefuseRun <- false
      let! restored = build owner "fresh reservation"
      restored |> success |> ignore
      let! running = owner.RunAsync([], CancellationToken.None)
      running |> success |> ignore
    }

    taskCase "run refusal with retained compiler authority preserves completed build replay" <| fun () -> task {
      let backend = new Backend(RefuseRun = true)
      use owner = session "retained-authority" "epoch-a" backend
      let ticket = owner.Reserve "initial" |> success
      let! built = owner.BuildAsync(ticket, CancellationToken.None)
      let accepted = built |> success
      let! refused = owner.RunAsync([], CancellationToken.None)
      refused |> refuses "compiler_refused"
      (owner.Status() |> success).Current |> Expect.equal "error text alone cannot withdraw accepted authority" (Some accepted)
      let! replay = owner.BuildAsync(ticket, CancellationToken.None)
      replay |> success |> Expect.equal "the still-current ticket remains observable" accepted
      backend.BuildCount |> Expect.equal "retained observation does not rebuild" 1
    }

    taskCase "late refused run cannot withdraw a newer accepted reservation" <| fun () -> task {
      let backend = new Backend(HoldRun = true, RefuseRun = true, WithdrawOnRunRefusal = true)
      use owner = session "late-refusal" "epoch-a" backend
      let! first = build owner "initial"
      first |> success |> ignore
      let running = owner.RunAsync([], CancellationToken.None)
      do! backend.RunEntered
      let ticket = owner.Reserve "replacement" |> success
      let! next = owner.BuildAsync(ticket, CancellationToken.None)
      let accepted = next |> success
      backend.ReleaseRun()
      let! old = running
      old |> refuses "superseded"
      (owner.Status() |> success).Current |> Expect.equal "late old refusal preserves the new artifact" (Some accepted)
      let! replay = owner.BuildAsync(ticket, CancellationToken.None)
      replay |> success |> Expect.equal "new reservation retains its own result authority" accepted
    }

    taskCase "canceling one run observer preserves another demand and accepted build authority" <| fun () -> task {
      let backend = new Backend(HoldRun = true, RefuseRun = true)
      use owner = session "detached-run" "epoch-a" backend
      let ticket = owner.Reserve "initial" |> success
      let! built = owner.BuildAsync(ticket, CancellationToken.None)
      let accepted = built |> success
      use cancellation = new CancellationTokenSource()
      let detached = owner.RunAsync([], cancellation.Token)
      do! backend.RunEntered
      let surviving = owner.RunAsync([], CancellationToken.None)
      cancellation.Cancel()
      let! canceled = detached
      canceled |> refuses "canceled"
      surviving.IsCompleted |> Expect.isFalse "another caller keeps its own run demand"
      (owner.Status() |> success).Current |> Expect.equal "detaching an observer does not withdraw compiler authority" (Some accepted)
      backend.ReleaseRun()
      let! refused = surviving
      refused |> refuses "compiler_refused"
      let! replay = owner.BuildAsync(ticket, CancellationToken.None)
      replay |> success |> Expect.equal "retained compiler authority survives cleanup of both runs" accepted
      backend.ReserveLabels |> Expect.equal "observer detach never inserts a compiler reservation" [| "initial" |]
    }

    taskCase "failed build evidence survives fresh status observers and replay until a new build succeeds" <| fun () -> task {
      let backend = new Backend(BuildFailure = Some compilerDiagnostic)
      use owner = session "shared-failure" "epoch-a" backend
      let token = owner.Reserve "failed revision" |> success
      let! failed = owner.BuildAsync(token, CancellationToken.None)
      failed |> refuses "compiler_refused"
      match failed.Outcome with
      | Result.Error refusal -> refusal.Message |> Expect.equal "original compiler evidence is retained exactly" compilerDiagnostic
      | Result.Ok _ -> failtest "The rejected build produced an artifact."
      let first = owner.Status() |> success
      let fresh = owner.Status() |> success
      first.BackendError |> Expect.equal "a new status observer sees the compiler refusal" (Some compilerDiagnostic)
      fresh.BackendError |> Expect.equal "the evidence belongs to shared session state" first.BackendError
      fresh.Current |> Expect.isNone "failed compilation has no accepted artifact"
      fresh.Busy |> Expect.isFalse "published failure follows physical build settlement"
      let! replay = owner.BuildAsync(token, CancellationToken.None)
      replay |> Expect.equal "re-observing a failed reservation retains its exact reply" failed
      backend.BuildCount |> Expect.equal "status and replay do not repeat the failed compilation" 1
      backend.BuildFailure <- None
      let! repaired = build owner "corrected revision"
      let accepted = repaired |> success
      let recovered = owner.Status() |> success
      recovered.Current |> Expect.equal "the corrected build publishes its artifact" (Some accepted)
      recovered.BackendError |> Expect.isNone "new successful build clears obsolete refusal evidence"
    }

    taskCase "late obsolete build failure cannot replace a newer accepted artifact or its clean status" <| fun () -> task {
      let backend = new Backend(HoldBuild = true, BuildFailure = Some compilerDiagnostic)
      use owner = session "superseded-failure" "epoch-a" backend
      let pending = build owner "old failing revision"
      try
        do! bounded backend.BuildEntered
        let token = owner.Reserve "new revision" |> success
        backend.BuildFailure <- None
        backend.HoldBuild <- false
        let! built = owner.BuildAsync(token, CancellationToken.None)
        let accepted = built |> success
        backend.ReleaseBuild()
        let! old = bounded pending
        old |> refuses "superseded"
        let fresh = owner.Status() |> success
        fresh.Current |> Expect.equal "late failure cannot revoke the new artifact" (Some accepted)
        fresh.BackendError |> Expect.isNone "unfenced compiler diagnostics cannot contaminate the new revision"
        let! replay = owner.BuildAsync(token, CancellationToken.None)
        replay |> success |> Expect.equal "new revision remains replayable" accepted
        (owner.Status() |> success).BackendError |> Expect.isNone "later observers retain the clean current revision"
      finally backend.ReleaseBuild()
    }

    taskCase "explicit cancellation prevents a late compiler refusal from becoming current evidence" <| fun () -> task {
      let backend = new Backend(HoldBuild = true, BuildFailure = Some compilerDiagnostic)
      use owner = session "canceled-failure" "epoch-a" backend
      let pending = build owner "failing revision"
      try
        do! bounded backend.BuildEntered
        owner.Cancel() |> success |> ignore
        backend.ReleaseBuild()
        let! old = bounded pending
        old |> refuses "superseded"
        let! fresh = settledStatus owner
        fresh.Current |> Expect.isNone "cancellation retains no artifact"
        fresh.BackendError |> Expect.isNone "withdrawn failure cannot publish through the generic diagnostic drain"
      finally backend.ReleaseBuild()
    }

    taskCase "closed sessions do not publish a late build refusal as current compiler evidence" <| fun () -> task {
      let backend = new Backend(HoldBuild = true, BuildFailure = Some compilerDiagnostic)
      use owner = session "closed-failure" "epoch-a" backend
      let pending = build owner "failing revision"
      try
        do! bounded backend.BuildEntered
        let closing = owner.CloseAsync()
        backend.ReleaseBuild()
        let! old = bounded pending
        old |> refuses "closed"
        let! cleaned = bounded closing
        cleaned |> success |> ignore
        let fresh = owner.Status() |> success
        fresh.Closed |> Expect.isTrue "logical retirement remains visible"
        fresh.Current |> Expect.isNone "closed session has no artifact"
        fresh.BackendError |> Expect.isNone "retired producer cannot publish a current refusal"
      finally backend.ReleaseBuild()
    }

    taskCase "a build abandoned by its last observer cannot publish a later compiler refusal" <| fun () -> task {
      let backend = new Backend(HoldBuild = true, BuildFailure = Some compilerDiagnostic)
      use owner = session "abandoned-failure" "epoch-a" backend
      use cancellation = new CancellationTokenSource()
      let token = owner.Reserve "failing revision" |> success
      let pending = owner.BuildAsync(token, cancellation.Token)
      try
        do! bounded backend.BuildEntered
        cancellation.Cancel()
        let! detached = bounded pending
        detached |> refuses "canceled"
        backend.ReleaseBuild()
        let! fresh = settledStatus owner
        fresh.Current |> Expect.isNone "abandoned build has no accepted artifact"
        fresh.BackendError |> Expect.isNone "failure without a surviving demand cannot become current evidence"
        let! replay = owner.BuildAsync(token, CancellationToken.None)
        replay |> refuses "canceled"
      finally backend.ReleaseBuild()
    }

    taskCase "a surviving shared build demand retains exact failure after a peer detaches" <| fun () -> task {
      let backend = new Backend(HoldBuild = true, BuildFailure = Some compilerDiagnostic)
      use owner = session "surviving-failure" "epoch-a" backend
      use cancellation = new CancellationTokenSource()
      let token = owner.Reserve "failing revision" |> success
      let pending = owner.BuildAsync(token, cancellation.Token)
      try
        do! bounded backend.BuildEntered
        let surviving = owner.BuildAsync(token, CancellationToken.None)
        cancellation.Cancel()
        let! detached = bounded pending
        detached |> refuses "canceled"
        backend.ReleaseBuild()
        let! failed = bounded surviving
        failed |> refuses "compiler_refused"
        let fresh = owner.Status() |> success
        fresh.BackendError |> Expect.equal "the surviving observer publishes shared compiler evidence" (Some compilerDiagnostic)
        fresh.Current |> Expect.isNone "the refusal cannot be treated as successful artifact authority"
        backend.BuildCount |> Expect.equal "both clients shared one compiler attempt" 1
      finally backend.ReleaseBuild()
    }

    taskCase "two clients share one producer for the same session reservation" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      let token = owner.Reserve "edit" |> success
      let pending = owner.BuildAsync(token, CancellationToken.None)
      do! bounded backend.BuildEntered
      let duplicate = owner.BuildAsync(token, CancellationToken.None)
      duplicate.IsCompleted |> Expect.isFalse "the second client observes the same held producer"
      backend.BuildCount |> Expect.equal "only original build reaches compiler" 1
      backend.ReleaseBuild()
      let! accepted = bounded pending
      accepted |> success |> ignore
      let! shared = bounded duplicate
      shared |> Expect.equal "both clients receive the same compiler artifact" accepted
      let! replay = owner.BuildAsync(token, CancellationToken.None)
      replay |> Expect.equal "current reservation re-observes its completed producer" accepted
      backend.BuildCount |> Expect.equal "neither sharing nor replay invokes compiler twice" 1
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

    taskCase "canceling one client detaches its demand while another keeps the shared producer" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      use cancellation = new CancellationTokenSource()
      let token = owner.Reserve "edit" |> success
      let pending = owner.BuildAsync(token, cancellation.Token)
      do! bounded backend.BuildEntered
      let retained = owner.BuildAsync(token, CancellationToken.None)
      cancellation.Cancel()
      let! response = bounded pending
      response |> refuses "canceled"
      owner.Identity.Generation |> Expect.equal "caller cancellation preserves generation" 1L
      backend.BuildCancellation.IsCancellationRequested |> Expect.isFalse "another demand keeps the producer live"
      retained.IsCompleted |> Expect.isFalse "surviving client still owns its held observation"
      backend.ReleaseBuild()
      let! accepted = bounded retained
      accepted |> success |> ignore
      backend.BuildCount |> Expect.equal "clients share one compiler call" 1
      let! run = owner.RunAsync([], CancellationToken.None)
      run |> success |> ignore
    }

    taskCase "closing a session retires held work and permanently refuses new operations" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      let reserved = owner.Reserve "edit" |> success
      let pending = owner.BuildAsync(reserved, CancellationToken.None)
      do! bounded backend.BuildEntered
      let cleanup = owner.CloseAsync()
      cleanup.IsCompleted |> Expect.isFalse "close retains physical ownership of held work"
      backend.Disposed |> Expect.isFalse "backend is not disposed beneath held compiler work"
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
      let! cleaned = bounded cleanup
      cleaned |> success |> ignore
      backend.Disposed |> Expect.isTrue "backend retires only after physical work joins"
    }

    taskCase "compiler replacement cannot lend a new epoch's authority to old work" <| fun () -> task {
      let oldBackend = new Backend(HoldBuild = true)
      use oldSession = session "project" "retired-epoch" oldBackend
      let oldToken = oldSession.Reserve "old edit" |> success
      let pending = oldSession.BuildAsync(oldToken, CancellationToken.None)
      do! bounded oldBackend.BuildEntered
      let cleanup = oldSession.CloseAsync()
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
      let! cleaned = bounded cleanup
      cleaned |> success |> ignore
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

    taskCase "pre-canceled build leaves its reservation available without entering compiler" (preCanceledRequest false)

    taskCase "pre-canceled run leaves accepted artifact authority intact without launching" (preCanceledRequest true)

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

    taskCase "reservation bypasses occupied evaluator slots and prevents a deferred native launch" <| fun () -> task {
      let backend = new Backend(HoldRun = true)
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      let running = [| for _ in 1..4 -> owner.RunAsync([], CancellationToken.None) |]
      try
        let deadline = Diagnostics.Stopwatch.StartNew()
        while backend.RunCount < 4 do
          if deadline.Elapsed > TimeSpan.FromSeconds 5. then failtest "The four native run slots did not become occupied"
          do! Task.Yield()
        let deferred = owner.RunAsync([], CancellationToken.None)
        let! reserved = owner.ReserveAsync "edited while dispatch is full" |> bounded
        reserved |> success |> ignore
        backend.ReserveLabels |> Expect.equal "edit permission requires real synchronous Composer reservation" [| "initial"; "edited while dispatch is full" |]
        backend.RunCount |> Expect.equal "queued run cannot launch behind reservation receipt" 4
        backend.ReleaseRun()
        let! results = Task.WhenAll(Array.append running [| deferred |]) |> bounded
        for result in results do result |> refuses "superseded"
        backend.RunCount |> Expect.equal "withdrawn deferred run never enters the backend" 4
      finally backend.ReleaseRun()
    }

    taskCase "close retains ownership after evaluator return until cancellation callback exits" <| fun () -> task {
      let backend = new Backend(HoldBuild = true)
      use owner = session "one" "epoch-a" backend
      use callback = new PrefixBarrier()
      backend.OnCancellation <- callback.Block
      let pending = build owner "held callback"
      try
        do! bounded backend.BuildEntered
        let closing = owner.CloseAsync()
        do! bounded callback.Entered
        backend.ReleaseBuild()
        do! bounded backend.BuildReturned
        closing.IsCompleted |> Expect.isFalse "callback exit is part of physical close"
        pending.IsCompleted |> Expect.isFalse "reply does not falsely certify physical settlement"
        backend.Disposed |> Expect.isFalse "disposal cannot overtake an owned callback"
        callback.Release()
        let! cleaned = bounded closing
        cleaned |> success |> ignore
        let! response = bounded pending
        response |> refuses "closed"
        backend.Disposed |> Expect.isTrue "joined close can dispose compiler ownership"
      finally
        backend.ReleaseBuild()
        callback.Release()
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

    taskCase "control admission is bounded and a refused reservation grants no new authority" <| fun () -> task {
      let backend = new Backend()
      use owner = session "one" "epoch-a" backend
      let! accepted = build owner "initial"
      accepted |> success |> ignore
      use prefix = new PrefixBarrier()
      backend.BeforeRun <- prefix.Block
      let oldRun = owner.RunAsync([], CancellationToken.None)
      try
        do! bounded prefix.Entered
        let admitted = [| for index in 1..128 -> owner.ReserveAsync(sprintf "queued-%d" index) |]
        let before = owner.Identity
        let! refused = owner.ReserveAsync "beyond capacity" |> bounded
        refused |> refuses "busy"
        owner.Identity |> Expect.equal "refused admission does not fabricate a new reservation" before
        prefix.Release()
        let! responses = Task.WhenAll admitted |> bounded
        responses[responses.Length - 1] |> success |> ignore
        for response in responses[0..responses.Length - 2] do response |> refuses "superseded"
        backend.ReserveLabels |> Expect.equal "only surviving control reaches the compiler" [| "initial"; "queued-128" |]
        let! old = bounded oldRun
        old |> refuses "superseded"
      finally prefix.Release()
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
