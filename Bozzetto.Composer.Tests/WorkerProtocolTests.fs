module Bozzetto.Composer.Tests.WorkerProtocolTests

open System
open System.IO
open System.Threading.Tasks
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Bozzetto.Composer.WorkerProtocol
open Expecto
open Expecto.Flip

let private request id body : Request = { ProtocolVersion = BAREWireCodec.ProtocolVersion; RequestId = id; Body = body }
let private success (reply: Bozzetto.Composer.Protocol.Reply) =
  match reply.Outcome with Result.Ok body -> body | Result.Error error -> failtestf "Unexpected refusal: %A" error
let private refused code (reply: Bozzetto.Composer.Protocol.Reply) =
  match reply.Outcome with
  | Result.Error error -> error.Code |> Expect.equal "exact refusal" code
  | Result.Ok body -> failtestf "Expected refusal, received %A" body
let private compiler () : CompilerIdentity = { AssemblyPath = "injected"; Sha256 = "compiler-sha"; Version = "test" }
let private target (identity: Authority) : WorkerAddress = { Host = identity.Host; Epoch = identity.Epoch; Provider = identity.Provider }
let private taskCase name run = testCaseAsync name (async { do! (run () : Task<unit>).WaitAsync(TimeSpan.FromSeconds 5.) |> Async.AwaitTask })

type private Backend() =
  let mutable generation = 100L
  let mutable current: AcceptedArtifact option = None
  interface IProjectBackend<int64> with
    member _.ManifestPath = "owner-manifest"
    member _.Current = current
    member _.Reserve _ = generation <- generation + 1L; current <- None; generation
    member _.BuildAsync(ticket, _) =
      let accepted = {
        Generation = ticket; SourceVersion = "exact-captured-inputs"; ArtifactPath = "artifact"
        ArtifactSha256 = "artifact-sha"; ObjectManifest = "objects"
        ChangedWitnesses = [||]; RetainedWitnesses = [||]; RetiredWitnesses = [||]
        WitnessVisits = [||]; CompiledObjects = [||]; ReusedObjects = [||]; RetiredObjects = [||] }
      current <- Some accepted
      Task.FromResult(Result.Ok accepted)
    member _.RunCurrentAsync(_, _) = Task.FromResult(Result.Error "Not used by the status fixture.")
    member _.Dispose() = current <- None

[<Tests>]
let tests = testList "Composer worker binary authority" [
  taskCase "failed handshake identity capture does not enable addressed requests" <| fun () -> task {
    use worker = new Worker<int64>(Path.GetTempPath(), (fun _ _ -> failwith "No backend should be opened"), (fun () -> failwith "identity capture failed"))
    let! failed = worker.Handle(request "hello-failed" (Hello BAREWireCodec.agreement))
    refused RefusalCode.RequestRefused failed
    let! disabled = worker.Handle(request "not-agreed" (CancelRequest(target failed.Authority, "target")))
    refused RefusalCode.ProtocolVersion disabled
  }

  taskCase "only the exact binary agreement enables addressed requests" <| fun () -> task {
    use worker = new Worker<int64>(Path.GetTempPath(), (fun _ _ -> failwith "No backend should be opened"), compiler)
    let! wrong = worker.Handle(request "wrong-contract" (Hello { BAREWireCodec.agreement with ContractDigest = "foreign" }))
    refused RefusalCode.ContractMismatch wrong
    let address = target wrong.Authority
    let! disabled = worker.Handle(request "not-agreed" (CancelRequest(address, "target")))
    refused RefusalCode.ProtocolVersion disabled
    let! accepted = worker.Handle(request "agreed" (Hello BAREWireCodec.agreement))
    let hello = match success accepted with HelloAccepted value -> value | other -> failtestf "Expected hello: %A" other
    hello.Psg.Schema |> Expect.equal "loaded source-owned schema" Fidelity.PSG.Revision.Schema
    hello.Psg.FormatVersion |> Expect.equal "shared PSG encoding" Fidelity.PSG.Binary.FormatVersion
    hello.Psg.ContractFingerprint |> Expect.equal "generated semantic shape" Fidelity.PSG.Binary.ContractFingerprint
    hello.Operations |> Expect.equal "only supported workspace operations are advertised"
      [| Operation.Hello; Operation.Open; Operation.Reserve; Operation.Build; Operation.Status
         Operation.Run; Operation.Cancel; Operation.CancelRequest; Operation.Close; Operation.PrepareCompilerChange |]
    let! cancellation = worker.Handle(request "enabled" (CancelRequest(target accepted.Authority, "unknown")))
    match success cancellation with
    | RequestCanceled result -> result.CancellationRequested |> Expect.isFalse "unknown target is not invented"
    | body -> failtestf "Expected cancellation response: %A" body
  }

  taskCase "status preserves compiler identity and reservation withdraws artifact authority" <| fun () -> task {
    let cache = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache", "bozzetto", "worker-status-tests", Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory cache |> ignore
    let project = Path.Combine(cache, "source", "test.fidproj")
    Directory.CreateDirectory(Path.GetDirectoryName project) |> ignore
    File.WriteAllText(project, "injected source owner; no compiler invocation")
    use worker = new Worker<int64>(Path.Combine(cache, "sessions"), (fun _ _ -> new Backend()), compiler)
    try
      let! hello = worker.Handle(request "hello" (Hello BAREWireCodec.agreement))
      success hello |> ignore
      let! opened = worker.Handle(request "open" (Open(target hello.Authority, project)))
      success opened |> ignore
      let address = { Worker = target opened.Authority; Session = opened.Authority.Session }
      let! absent = worker.Handle(request "absent" (Status address))
      match success absent with
      | Observed value -> value.Current |> Expect.isNone "unbuilt status contains no artifact"
      | body -> failtestf "Expected status: %A" body
      let! reserved = worker.Handle(request "reserve" (Reserve(address, "build-current")))
      let token = match success reserved with Reserved value -> value.Reservation | body -> failtestf "Expected reserve: %A" body
      let! built = worker.Handle(request "build" (Build(address, token)))
      let accepted = match success built with Built value -> value | body -> failtestf "Expected build: %A" body
      let! read = worker.Handle(request "status" (Status address))
      let current = match success read with Observed value -> value.Current.Value | body -> failtestf "Expected status: %A" body
      current.Generation |> Expect.equal "compiler generation is preserved" accepted.Generation
      current.Generation <> read.Authority.Generation |> Expect.isTrue "provider generation is not substituted for compiler generation"
      current.SourceVersion |> Expect.equal "exact accepted input stamp" accepted.SourceVersion
      let encoded = BAREWireCodec.encodeReply read |> Result.defaultWith (fun error -> failtestf "Encode: %A" error)
      BAREWireCodec.decodeReply encoded |> Expect.equal "the status reply uses the shared binary contract" (Result.Ok read)
      let! changed = worker.Handle(request "next-reserve" (Reserve(address, "before-source-write")))
      success changed |> ignore
      let! withdrawn = worker.Handle(request "withdrawn" (Status address))
      match success withdrawn with
      | Observed value -> value.Current |> Expect.isNone "reservation withdraws current artifact before source write"
      | body -> failtestf "Expected status: %A" body
      let! foreign = worker.Handle(request "foreign" (Status { address with Worker = { address.Worker with Epoch = "foreign" } }))
      refused RefusalCode.WrongAuthority foreign
    finally
      worker.RetireAsync().WaitAsync(TimeSpan.FromSeconds 5.).GetAwaiter().GetResult() |> ignore
      Directory.Delete(cache, true)
  }
]
