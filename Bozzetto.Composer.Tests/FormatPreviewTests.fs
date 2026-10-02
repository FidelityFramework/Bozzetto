module Bozzetto.Composer.Tests.FormatPreviewTests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration
open Fidelity.FSharp.Incremental.Hosting

let private buffer revision source : FormatBuffer = {
  Document = "buffer://not-a-file/quoted.clef"; Incarnation = "00000000-0000-0000-0000-000000000001"
  Revision = revision; Source = source; Configuration = FormatPolicy.Configuration
}
let private success = function
  | Ok value -> value
  | Error error -> failtestf "Unexpected refusal: %A" error
let private refusal code = function
  | Error (error: Bozzetto.Providers.Refusal) -> error.Code |> Expect.equal "refusal reason" code
  | Ok _ -> failtestf "Expected refusal %s" code
let private taskCase name (work: unit -> Task<unit>) = testCaseAsync name (async {
  do! (work ()).WaitAsync(TimeSpan.FromSeconds 15.) |> Async.AwaitTask
})
let private preview (formatter: FormatterSession) snapshot =
  task {
    let accepted = formatter.RequestPreview snapshot
    let retired = formatter.DrainRetirements() |> List.map (ClrInterop.toTask CancellationToken.None)
    let! result =
      match accepted with
      | Ok demand -> ClrInterop.toTask CancellationToken.None demand.Observe
      | Error refusal -> Task.FromResult(Error refusal)
    let! joined = Task.WhenAll retired
    joined |> Array.iter (fun result -> success result |> ignore)
    return result
  }
let private closeFormatter (formatter: FormatterSession) =
  formatter.CloseAsync() |> ClrInterop.toTask CancellationToken.None

type private Backend() =
  let mutable generation = 0L
  let mutable current = None
  member val Reservations = 0 with get, set
  interface IProjectBackend<int64> with
    member _.ManifestPath = "/no-file-reads/program.fidproj"
    member _.Current = current
    member this.Reserve _ =
      this.Reservations <- this.Reservations + 1
      generation <- generation + 1L
      current <- None
      generation
    member _.BuildAsync(ticket, _) =
      let artifact: AcceptedArtifact = {
        Generation = ticket; SourceVersion = "exact-base"; ArtifactPath = "/accepted/program"; ArtifactSha256 = "digest"
        ObjectManifest = "/accepted/objects"; ChangedWitnesses = [||]; RetainedWitnesses = [||]; RetiredWitnesses = [||]
        WitnessVisits = [||]; CompiledObjects = [||]; ReusedObjects = [||]; RetiredObjects = [||]
      }
      current <- Some artifact
      Task.FromResult(Ok artifact)
    member _.RunCurrentAsync(_, _) = Task.FromResult(Ok {
      Generation = generation; SourceVersion = "exact-base"; ExitCode = 0; StandardOutput = "native"; StandardError = ""
    })
    member _.Dispose() = current <- None

/// Deliberately ignores cancellation until physical release. The provider's
/// publication fence must refuse its old-generation successful completion.
type private HeldFormatter() =
  let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let withdrawn = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let mutable closed = false
  let diagnostics = Collections.Concurrent.ConcurrentQueue<FormatterDiagnostic>()
  member _.Entered = entered.Task
  member _.Withdrawn = withdrawn.Task
  member _.Release() = release.TrySetResult() |> ignore
  member _.Closed = closed
  member _.QueueDiagnostic diagnostic = diagnostics.Enqueue diagnostic
  interface IFormatBackend with
    member _.RequestPreview buffer = Ok {
      Observe = async {
        entered.TrySetResult() |> ignore
        do! release.Task |> Async.AwaitTask
        return Ok {
          Document = buffer.Document; Incarnation = buffer.Incarnation; Revision = buffer.Revision
          SourceSha256 = FormatPolicy.sourceSha256 buffer.Source; Configuration = buffer.Configuration
          FormatterIdentity = "test-held"; Formatted = buffer.Source
        }
      }
      Withdraw = fun () -> withdrawn.TrySetResult() |> ignore
    }
    member _.BeginClose() = closed <- true
    member _.DrainRetirements() = []
    member _.CloseAsync() = async {
      do! release.Task |> Async.AwaitTask
      return Ok ()
    }
    member _.DrainDiagnostics() =
      let captured = ResizeArray<FormatterDiagnostic>()
      let mutable diagnostic = Unchecked.defaultof<FormatterDiagnostic>
      while diagnostics.TryDequeue(&diagnostic) do captured.Add diagnostic
      captured |> Seq.toList

type private RecordingFormatter() =
  let formatter = FormatterSession()
  let admitted = Collections.Concurrent.ConcurrentQueue<FormatBuffer>()
  member _.Admitted = admitted.ToArray()
  interface IFormatBackend with
    member _.RequestPreview buffer =
      admitted.Enqueue buffer
      formatter.RequestPreview buffer
    member _.BeginClose() = formatter.BeginClose()
    member _.DrainRetirements() = formatter.DrainRetirements()
    member _.CloseAsync() = formatter.CloseAsync()
    member _.DrainDiagnostics() = formatter.DrainDiagnostics()

[<Tests>]
let tests = testList "Composer Calque preview" [
  taskCase "immutable quoted buffer formats without changing accepted artifact authority" <| fun () -> task {
    let backend = new Backend()
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/not-read/project.fidproj", backend)
    let reservation = owner.Reserve "build base" |> fun response -> success response.Outcome
    let! built = owner.BuildAsync(reservation, CancellationToken.None)
    let accepted = success built.Outcome
    let snapshot = buffer 1UL "module Quoted\nlet law = <@ fun value->value % 7 @>\n"
    let! response = owner.FormatAsync(owner.Identity.Generation, snapshot, CancellationToken.None)
    let preview = success response.Outcome
    preview.Formatted.Contains "<@" |> Expect.isTrue "typed quotation is retained"
    preview.Formatted.Contains "% 7" |> Expect.isTrue "infix modulo stays native"
    preview.SourceSha256 |> Expect.equal "exact immutable base digest" (FormatPolicy.sourceSha256 snapshot.Source)
    preview.FormatterIdentity.Contains "Calque.Core:" |> Expect.isTrue "owner-start formatter deployment files are identified"
    response.Authority.Generation |> Expect.equal "preview does not reserve" built.Authority.Generation
    (owner.Status() |> fun response -> success response.Outcome).Current |> Expect.equal "accepted artifact remains available" (Some accepted)
    backend.Reservations |> Expect.equal "formatting makes no compiler reservation" 1
    let! ran = owner.RunAsync([], CancellationToken.None)
    success ran.Outcome |> ignore
    let! closed = owner.CloseAsync()
    success closed.Outcome |> ignore
  }
  taskCase "document revision conflicts and retired incarnations refuse while exact requests share" <| fun () -> task {
    let formatter = FormatterSession()
    try
      let original = buffer 3UL "module Sample\nlet value=1\n"
      let! first = preview formatter original
      let! same = preview formatter original
      success same |> Expect.equal "same immutable snapshot has the same preview" (success first)
      let! conflict = preview formatter { original with Source = "module Sample\nlet value=2\n" }
      conflict |> refusal "superseded"
      let! old = preview formatter { original with Revision = 2UL }
      old |> refusal "superseded"
      let replacement = { original with Incarnation = Guid.NewGuid().ToString("D"); Revision = 0UL }
      let! newIncarnation = preview formatter replacement
      success newIncarnation |> ignore
      let! retired = preview formatter original
      retired |> refusal "superseded"
    finally formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
  }
  taskCase "demand acceptance records the snapshot before its cold observation runs" <| fun () -> task {
    let formatter = FormatterSession()
    try
      let original = buffer 3UL "module Sample\nlet value=3\n"
      let first = formatter.RequestPreview original |> success
      let delayedObservation = first.Observe
      formatter.RequestPreview { original with Revision = 2UL }
      |> refusal "superseded"
      formatter.RequestPreview { original with Source = "module Sample\nlet value=99\n" }
      |> refusal "superseded"
      let current = buffer 4UL "module Sample\nlet value=4\n"
      let second = formatter.RequestPreview current |> success
      let! stale = ClrInterop.toTask CancellationToken.None delayedObservation
      stale |> refusal "superseded"
      let! fresh = ClrInterop.toTask CancellationToken.None second.Observe
      (success fresh).SourceSha256 |> Expect.equal "observation cannot reinstate the older input" (FormatPolicy.sourceSha256 current.Source)
    finally formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
  }
  taskCase "withdrawing one accepted formatter demand preserves its peer before either is observed" <| fun () -> task {
    let formatter = FormatterSession()
    try
      let snapshot = buffer 1UL "module Shared\nlet value=1\n"
      let canceled = formatter.RequestPreview snapshot |> success
      let peer = formatter.RequestPreview snapshot |> success
      canceled.Withdraw()
      canceled.Withdraw()
      let! results = [ canceled.Observe; peer.Observe ] |> Async.Parallel |> ClrInterop.toTask CancellationToken.None
      results[0] |> refusal "canceled"
      (success results[1]).SourceSha256 |> Expect.equal "the peer retains its exact immutable input" (FormatPolicy.sourceSha256 snapshot.Source)
      let! repeated = preview formatter snapshot
      success repeated |> Expect.equal "an independently released consumer preserves the shared preview" (success results[1])
    finally formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
  }
  taskCase "closing the formatter owns an accepted demand that was never observed" <| fun () -> task {
    let formatter = FormatterSession()
    let snapshot = buffer 1UL "module Unobserved\nlet value=1\n"
    let demand = formatter.RequestPreview snapshot |> success
    let withdrawn = formatter.RequestPreview snapshot |> success
    withdrawn.Withdraw()
    formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
    let! late = ClrInterop.toTask CancellationToken.None demand.Observe
    late |> refusal "closed"
    let! lateWithdrawal = ClrInterop.toTask CancellationToken.None withdrawn.Observe
    lateWithdrawal |> refusal "closed"
    formatter.RequestPreview (buffer 2UL "module Unobserved\nlet value=2\n") |> refusal "closed"
  }
  taskCase "reservation withdraws a pending preview before cleanup and refuses its original generation" <| fun () -> task {
    let backend = new Backend()
    let formatter = HeldFormatter()
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/not-read/project.fidproj", backend, formatter = formatter)
    try
      let preview = owner.FormatAsync(0L, buffer 1UL "module Sample\nlet value=1\n", CancellationToken.None)
      do! formatter.Entered
      let reservation = owner.ReserveAsync "new edit"
      formatter.Withdrawn.IsCompleted |> Expect.isTrue "reservation immediately withdraws accepted formatting without a replacement preview"
      preview.IsCompleted |> Expect.isFalse "withdrawal does not discard the held observation or its cleanup"
      let! reserved = reservation
      success reserved.Outcome |> ignore
      formatter.QueueDiagnostic {
        Document = { Uri = "buffer://stale-attempt"; Incarnation = Guid.Parse("00000000-0000-0000-0000-000000000001") }
        Diagnostic = { Attempt = Fidelity.FSharp.Incremental.AttemptId 77UL
                       Failure = { Code = "withdrawn-attempt-failure"; Message = "retained stale formatter fault" } }
      }
      formatter.Release()
      let! stale = preview
      stale.Outcome |> refusal "superseded"
      stale.Authority.Generation |> Expect.equal "refusal preserves admitted generation" 0L
      let diagnostic = (owner.Status() |> fun response -> success response.Outcome).FormatterError |> Option.defaultWith (fun () -> failtest "stale formatter diagnostics were discarded")
      for evidence in [ "buffer://stale-attempt"; "77UL"; "withdrawn-attempt-failure"; "retained stale formatter fault" ] do
        diagnostic.Contains evidence |> Expect.isTrue "the stale attempt's complete diagnostic remains visible"
      formatter.QueueDiagnostic {
        Document = { Uri = "buffer://late-retired-attempt"; Incarnation = Guid.Parse("00000000-0000-0000-0000-000000000001") }
        Diagnostic = { Attempt = Fidelity.FSharp.Incremental.AttemptId 78UL
                       Failure = { Code = "late-retired-failure"; Message = "fault after projection settlement" } }
      }
      let late = (owner.Status() |> fun response -> success response.Outcome).FormatterError |> Option.defaultValue ""
      late.Contains "late-retired-failure" |> Expect.isTrue "ordinary status captures faults after a preview has settled"
      late.Contains "withdrawn-attempt-failure" |> Expect.isTrue "later diagnostics preserve previous attempt evidence"
      let! rejected = owner.FormatAsync(0L, buffer 2UL "module Sample\nlet value=2\n", CancellationToken.None)
      rejected.Outcome |> refusal "superseded"
    finally formatter.Release()
    let! closed = owner.CloseAsync()
    success closed.Outcome |> ignore
  }
  taskCase "queued old-generation preview cannot advance document revision after reservation" <| fun () -> task {
    let formatter = RecordingFormatter()
    let scheduled = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable invocations = 0
    let checkpoint () =
      if Interlocked.Increment(&invocations) = 1 then
        scheduled.TrySetResult() |> ignore
        release.Task :> Task
      else Task.CompletedTask
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/not-read/project.fidproj", new Backend(), formatter = formatter, beforeFormatInvocation = checkpoint)
    try
      let old = buffer 4UL "module Sample\nlet value=4\n"
      let stale = owner.FormatAsync(0L, old, CancellationToken.None)
      do! scheduled.Task
      formatter.Admitted |> Expect.isEmpty "scheduled old request has not entered formatter admission"
      let! reserved = owner.ReserveAsync "new generation wins before formatting admission"
      success reserved.Outcome |> ignore
      let current = buffer 3UL "module Sample\nlet value=3\n"
      let! fresh = owner.FormatAsync(1L, current, CancellationToken.None)
      let accepted = success fresh.Outcome
      release.TrySetResult() |> ignore
      let! refused = stale
      refused.Outcome |> refusal "superseded"
      refused.Authority.Generation |> Expect.equal "queued refusal preserves original generation" 0L
      formatter.Admitted |> Expect.equal "obsolete queued snapshot never reaches the formatter" [| current |]
      let! repeated = owner.FormatAsync(1L, current, CancellationToken.None)
      success repeated.Outcome |> Expect.equal "new generation's lower document revision stays eligible" accepted
      formatter.Admitted |> Expect.equal "only exact current snapshots were admitted" [| current; current |]
    finally release.TrySetResult() |> ignore
    let! closed = owner.CloseAsync()
    success closed.Outcome |> ignore
  }
  taskCase "session close seals admission and joins withdrawn formatter work" <| fun () -> task {
    let formatter = HeldFormatter()
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/not-read/project.fidproj", new Backend(), formatter = formatter)
    try
      let pending = owner.FormatAsync(0L, buffer 1UL "module Sample\nlet value=1\n", CancellationToken.None)
      do! formatter.Entered
      let! reservation = owner.ReserveAsync "withdraw before closing"
      success reservation.Outcome |> ignore
      formatter.Withdrawn.IsCompleted |> Expect.isTrue "the retained formatter demand was withdrawn before close"
      let closing = owner.CloseAsync()
      formatter.Closed |> Expect.isTrue "close synchronously seals the formatting host"
      let! premature = Task.WhenAny(closing :> Task, Task.Delay 100)
      obj.ReferenceEquals(premature, closing) |> Expect.isFalse "cleanup must remain blocked while physical formatting work is held"
      let! refused = owner.FormatAsync(owner.Identity.Generation, buffer 2UL "module Sample\nlet value=2\n", CancellationToken.None)
      refused.Outcome |> refusal "closed"
      formatter.Release()
      let! stale = pending
      stale.Outcome |> refusal "closed"
      let! closed = closing
      success closed.Outcome |> ignore
    finally formatter.Release()
  }
  taskCase "formatter capacity refuses rather than evicting immutable identities" <| fun () -> task {
    let formatter = FormatterSession()
    try
      for index in 1 .. 32 do
        let snapshot = { buffer 0UL "module Sample\nlet value=1\n" with Document = "buffer://" + string index }
        let! formatted = preview formatter snapshot
        success formatted |> ignore
      let! full = preview formatter { buffer 0UL "module Sample\nlet value=1\n" with Document = "buffer://33" }
      full |> refusal "session_capacity"
      let! retained = preview formatter { buffer 0UL "module Sample\nlet value=1\n" with Document = "buffer://1" }
      success retained |> ignore
      let! config = preview formatter { buffer 0UL "" with Configuration = "unknown" }
      config |> refusal "invalid_request"
    finally formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
  }
  taskCase "joined retired incarnations release slots without resurrecting old identities" <| fun () -> task {
    let formatter = FormatterSession()
    let original = buffer 0UL "module Reopened\nlet value=1\n"
    try
      let! first = preview formatter original
      success first |> ignore
      for _ in 1 .. 40 do
        let snapshot = { original with Incarnation = Guid.NewGuid().ToString("D") }
        let! reopened = preview formatter snapshot
        success reopened |> ignore
      formatter.RequestPreview original |> refusal "superseded"
      formatter.DrainRetirements() |> Expect.isEmpty "each retired child transferred only once"
    finally formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
  }
  taskCase "a retired document occupies its slot until physical close joins" <| fun () -> task {
    let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let join operation = async {
      entered.TrySetResult() |> ignore
      do! release.Task |> Async.AwaitTask
      return! Calque.Incremental.DocumentFormatter.awaitClose operation
    }
    let formatter = FormatterSession(Calque.Incremental.DocumentFormatter.start, join)
    let original = { buffer 0UL "module Capacity\nlet value=1\n" with Document = "buffer://retiring" }
    try
      let! first = preview formatter original
      success first |> ignore
      for index in 1 .. 31 do
        let! retained = preview formatter { original with Document = "buffer://retained/" + string index }
        success retained |> ignore
      let replacement = { original with Incarnation = Guid.NewGuid().ToString("D") }
      formatter.RequestPreview replacement |> refusal "busy"
      let retirements = formatter.DrainRetirements()
      retirements.Length |> Expect.equal "one exact retired child was transferred" 1
      formatter.DrainRetirements() |> Expect.isEmpty "retirement is not scheduled twice"
      let joined = retirements.Head |> ClrInterop.toTask CancellationToken.None
      do! entered.Task
      formatter.RequestPreview { original with Document = "buffer://unrelated/33" } |> refusal "busy"
      formatter.RequestPreview original |> refusal "superseded"
      joined.IsCompleted |> Expect.isFalse "sealing did not reclaim the live physical slot"
      release.TrySetResult() |> ignore
      let! retired = joined
      success retired |> ignore
      let! reopened = preview formatter replacement
      success reopened |> ignore
      formatter.RequestPreview original |> refusal "superseded"
      let! retained = preview formatter { original with Document = "buffer://retained/1" }
      success retained |> ignore
    finally
      release.TrySetResult() |> ignore
      formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
  }
  taskCase "failed document starts are joined without consuming an incarnation identity" <| fun () -> task {
    let mutable failStart = true
    let start handle =
      if failStart then
        failStart <- false
        Error(Calque.Incremental.FormatterError.InvalidSettings "injected start failure")
      else Calque.Incremental.DocumentFormatter.start handle
    let formatter = FormatterSession(start, Calque.Incremental.DocumentFormatter.awaitClose)
    let original = buffer 0UL "module Retry\nlet value=1\n"
    try
      formatter.RequestPreview original |> refusal "request_refused"
      formatter.RequestPreview original |> refusal "busy"
      let retirements = formatter.DrainRetirements()
      retirements.Length |> Expect.equal "failed start retains one owned cleanup" 1
      let! joined = retirements.Head |> ClrInterop.toTask CancellationToken.None
      success joined |> ignore
      let! retried = preview formatter original
      success retried |> ignore
      for index in 1 .. 31 do
        let! retained = preview formatter { original with Document = "buffer://remaining/" + string index }
        success retained |> ignore
      formatter.RequestPreview { original with Document = "buffer://over-capacity" } |> refusal "session_capacity"
    finally formatter.BeginClose()
    let! closed = closeFormatter formatter
    success closed |> ignore
  }
  taskCase "provider owns retired document cleanup while spare capacity permits unrelated previews" <| fun () -> task {
    let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let join operation = async {
      entered.TrySetResult() |> ignore
      do! release.Task |> Async.AwaitTask
      return! Calque.Incremental.DocumentFormatter.awaitClose operation
    }
    let formatter = FormatterSession(Calque.Incremental.DocumentFormatter.start, join)
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/not-read/project.fidproj", new Backend(), formatter = formatter)
    try
      let original = buffer 0UL "module Owned\nlet value=1\n"
      let! first = owner.FormatAsync(0L, original, CancellationToken.None)
      success first.Outcome |> ignore
      let replacement = { original with Incarnation = Guid.NewGuid().ToString("D") }
      let! reopened = owner.FormatAsync(0L, replacement, CancellationToken.None)
      success reopened.Outcome |> ignore
      do! entered.Task
      let! unrelated = owner.FormatAsync(0L, { original with Document = "buffer://unrelated" }, CancellationToken.None)
      success unrelated.Outcome |> ignore
      let closing = owner.CloseAsync()
      let! premature = Task.WhenAny(closing :> Task, Task.Delay 50)
      obj.ReferenceEquals(premature, closing) |> Expect.isFalse "parent close still owns the physically held retired child"
      release.TrySetResult() |> ignore
      let! closed = closing
      success closed.Outcome |> ignore
    finally release.TrySetResult() |> ignore
  }
  taskCase "formatter capacity busy expires into sticky worker retirement without discarding cleanup" <| fun () -> task {
    let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable timestamp = 0L
    let join operation = async {
      entered.TrySetResult() |> ignore
      do! release.Task |> Async.AwaitTask
      return! Calque.Incremental.DocumentFormatter.awaitClose operation
    }
    let formatter = FormatterSession(Calque.Incremental.DocumentFormatter.start, join)
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/deadline/project.fidproj", new Backend(),
      formatter = formatter, formatterCleanupTimestamp = (fun () -> Interlocked.Read(&timestamp)))
    let status () = owner.Status().Outcome |> success
    let original = buffer 0UL "module Deadline\nlet value=1\n"
    try
      let! first = owner.FormatAsync(0L, original, CancellationToken.None)
      success first.Outcome |> ignore
      for index in 1 .. 31 do
        let! retained = owner.FormatAsync(0L, { original with Document = "buffer://capacity/" + string index }, CancellationToken.None)
        success retained.Outcome |> ignore
      let replacement = { original with Incarnation = Guid.NewGuid().ToString("D") }
      let! busy = owner.FormatAsync(0L, replacement, CancellationToken.None)
      refusal "busy" busy.Outcome
      do! entered.Task
      (status ()).FormatterCleanupPending |> Expect.isTrue "pending close keeps daemon supervision active"
      Interlocked.Exchange(&timestamp, 29L * Diagnostics.Stopwatch.Frequency) |> ignore
      (status ()).WorkerRetirementRequired |> Expect.isNone "the grace interval permits physical cleanup"
      let! existing = owner.FormatAsync(0L, { original with Document = "buffer://capacity/1" }, CancellationToken.None)
      success existing.Outcome |> ignore
      let! unrelated = owner.FormatAsync(0L, { original with Document = "buffer://capacity/33" }, CancellationToken.None)
      refusal "busy" unrelated.Outcome
      Interlocked.Exchange(&timestamp, 30L * Diagnostics.Stopwatch.Frequency) |> ignore
      let expired = status ()
      expired.WorkerRetirementRequired |> Expect.isSome "the exact deadline requires whole-worker supervision"
      expired.FormatterCleanupPending |> Expect.isTrue "a deadline cannot manufacture physical completion"
      let! refused = owner.FormatAsync(0L, replacement, CancellationToken.None)
      refusal "closed" refused.Outcome
      let closing = owner.CloseAsync()
      closing.IsCompleted |> Expect.isFalse "parent still owns the held child after escalation"
      release.TrySetResult() |> ignore
      let! closed = closing
      success closed.Outcome |> ignore
      let final = status ()
      final.FormatterCleanupPending |> Expect.isFalse "actual join clears physical cleanup state"
      final.WorkerRetirementRequired |> Expect.equal "late cleanup cannot undo a retirement already issued" expired.WorkerRetirementRequired
    finally release.TrySetResult() |> ignore
  }
  testCase "public JSON preserves full uint64 revisions and rejects absent immutable buffers" <| fun _ ->
    let snapshot = buffer UInt64.MaxValue "module Sample\nlet value=1\n"
    let body = ComposerClientJson.value {|
      host = "host"; session = "session"; epoch = "epoch"; generation = 17L
      document = snapshot.Document; incarnation = snapshot.Incarnation; revision = snapshot.Revision
      source = snapshot.Source; configuration = snapshot.Configuration
    |}
    let address = { Worker = { Host = "host"; Epoch = "epoch"; Provider = ProviderIdentity.ClefComposer }; Session = "session" }
    ComposerClientJson.request "format" body |> Expect.equal "exact buffer snapshot reaches the binary contract" (Ok(Format(address, 17L, snapshot)))
    ComposerClientJson.request "format" (ComposerClientJson.value {| generation = 17L; revision = 1UL |})
    |> Result.isError |> Expect.isTrue "missing source is refused at the external adapter"
]
