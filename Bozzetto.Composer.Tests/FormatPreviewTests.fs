module Bozzetto.Composer.Tests.FormatPreviewTests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Bozzetto.ComposerIntegration

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
  let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let mutable closed = false
  let diagnostics = Collections.Concurrent.ConcurrentQueue<FormatterDiagnostic>()
  member _.Entered = entered.Task
  member _.Release() = release.TrySetResult() |> ignore
  member _.Closed = closed
  member _.QueueDiagnostic diagnostic = diagnostics.Enqueue diagnostic
  interface IFormatBackend with
    member _.PreviewAsync(buffer, _) = task {
      entered.TrySetResult() |> ignore
      do! release.Task
      return Ok {
        Document = buffer.Document; Incarnation = buffer.Incarnation; Revision = buffer.Revision
        SourceSha256 = FormatPolicy.sourceSha256 buffer.Source; Configuration = buffer.Configuration
        FormatterIdentity = "test-held"; Formatted = buffer.Source
      }
    }
    member _.BeginClose() = closed <- true
    member _.CloseAsync() = task {
      do! release.Task
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
    member _.PreviewAsync(buffer, cancellation) =
      admitted.Enqueue buffer
      formatter.PreviewAsync(buffer, cancellation)
    member _.BeginClose() = formatter.BeginClose()
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
      let! first = formatter.PreviewAsync(original, CancellationToken.None)
      let! same = formatter.PreviewAsync(original, CancellationToken.None)
      success same |> Expect.equal "same immutable snapshot has the same preview" (success first)
      let! conflict = formatter.PreviewAsync({ original with Source = "module Sample\nlet value=2\n" }, CancellationToken.None)
      conflict |> refusal "superseded"
      let! old = formatter.PreviewAsync({ original with Revision = 2UL }, CancellationToken.None)
      old |> refusal "superseded"
      let replacement = { original with Incarnation = Guid.NewGuid().ToString("D"); Revision = 0UL }
      let! newIncarnation = formatter.PreviewAsync(replacement, CancellationToken.None)
      success newIncarnation |> ignore
      let! retired = formatter.PreviewAsync(original, CancellationToken.None)
      retired |> refusal "superseded"
    finally formatter.BeginClose()
    let! closed = formatter.CloseAsync()
    success closed |> ignore
  }
  taskCase "pending preview completion after reservation refuses its original generation" <| fun () -> task {
    let backend = new Backend()
    let formatter = HeldFormatter()
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/not-read/project.fidproj", backend, formatter = formatter)
    try
      let preview = owner.FormatAsync(0L, buffer 1UL "module Sample\nlet value=1\n", CancellationToken.None)
      do! formatter.Entered
      let! reserved = owner.ReserveAsync "new edit"
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
      let diagnostic = (owner.Status() |> fun response -> success response.Outcome).BackendError |> Option.defaultWith (fun () -> failtest "stale formatter diagnostics were discarded")
      for evidence in [ "buffer://stale-attempt"; "77UL"; "withdrawn-attempt-failure"; "retained stale formatter fault" ] do
        diagnostic.Contains evidence |> Expect.isTrue "the stale attempt's complete diagnostic remains visible"
      formatter.QueueDiagnostic {
        Document = { Uri = "buffer://late-retired-attempt"; Incarnation = Guid.Parse("00000000-0000-0000-0000-000000000001") }
        Diagnostic = { Attempt = Fidelity.FSharp.Incremental.AttemptId 78UL
                       Failure = { Code = "late-retired-failure"; Message = "fault after projection settlement" } }
      }
      let late = (owner.Status() |> fun response -> success response.Outcome).BackendError |> Option.defaultValue ""
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
  taskCase "session close seals admission and joins retained formatter work" <| fun () -> task {
    let formatter = HeldFormatter()
    use owner = new ProviderSession<int64>("host", "session", "epoch", "/not-read/project.fidproj", new Backend(), formatter = formatter)
    try
      let pending = owner.FormatAsync(0L, buffer 1UL "module Sample\nlet value=1\n", CancellationToken.None)
      do! formatter.Entered
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
        let! preview = formatter.PreviewAsync(snapshot, CancellationToken.None)
        success preview |> ignore
      let! full = formatter.PreviewAsync({ buffer 0UL "module Sample\nlet value=1\n" with Document = "buffer://33" }, CancellationToken.None)
      full |> refusal "session_capacity"
      let! retained = formatter.PreviewAsync({ buffer 0UL "module Sample\nlet value=1\n" with Document = "buffer://1" }, CancellationToken.None)
      success retained |> ignore
      let! config = formatter.PreviewAsync({ buffer 0UL "" with Configuration = "unknown" }, CancellationToken.None)
      config |> refusal "invalid_request"
    finally formatter.BeginClose()
    let! closed = formatter.CloseAsync()
    success closed |> ignore
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
