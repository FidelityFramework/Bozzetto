module Bozzetto.Tests.ComposerBinaryProtocolTests

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open BAREWire.Framing
open Bozzetto.Providers
open Bozzetto.Composer.Protocol

let private mustSucceed = function
  | Result.Ok value -> value
  | Result.Error error -> failtestf "Unexpected refusal: %A" error

let private worker: WorkerAddress = { Host = "host"; Epoch = "epoch"; Provider = ProviderIdentity.ClefComposer }
let private session: SessionAddress = { Worker = worker; Session = "session" }
let private authority: Authority = {
  Host = worker.Host; Session = session.Session; Provider = worker.Provider
  Epoch = worker.Epoch; Generation = 17L
}
let private artifact: AcceptedArtifact = {
  Generation = 23L; SourceVersion = "source-version"; ArtifactPath = "/tmp/compiled program"
  ArtifactSha256 = "artifact-hash"; ObjectManifest = "/tmp/objects.json"
  ChangedWitnesses = [| "changed" |]; RetainedWitnesses = [| "retained"; "retained" |]
  RetiredWitnesses = [| "retired" |]; WitnessVisits = [| "changed", 1; "retained", 0 |]
  CompiledObjects = [| "new.o" |]; ReusedObjects = [| "held.o" |]; RetiredObjects = [| "old.o" |]
}
let private status: SessionSnapshot = {
  Observation = 19UL; Project = "/tmp/project.fidproj"; ManifestPath = "/tmp/current.json"
  Closed = true; Busy = true; Current = Some artifact; RevocationPending = true
  BackendError = Some "backend error"; FormatterError = Some "formatter host error"
  CleanupPending = true; CleanupError = Some "cleanup error"
}
let private request body: Request = { ProtocolVersion = 2us; RequestId = "id/α/\000/not-u32"; Body = body }
let private reply body: Reply = { ProtocolVersion = 2us; RequestId = "same-full-id"; Authority = authority; Outcome = Result.Ok body }
let private requests = [
  Hello BAREWireCodec.agreement; Open(worker, "/tmp/with spaces/项目.fidproj")
  Reserve(session, "before write"); Build(session, "opaque-reservation")
  Status session; RequestBody.Run(session, [| ""; "a\000b"; "λ" |]); Cancel session
  CancelRequest(worker, "full-target-id"); Close session; PrepareCompilerChange worker
  Format(session, 17L, {
    Document = "buffer://α"; Incarnation = "00000000-0000-0000-0000-000000000001"
    Revision = UInt64.MaxValue; Source = "module Sample\nlet value=1\n"; Configuration = "clef-two-space-lf-v1"
  })
]
let private replies = [
  HelloAccepted {
    Agreement = BAREWireCodec.agreement
    Compiler = { AssemblyPath = "/compiler/Composer.dll"; Sha256 = "compiler-hash"; Version = "1" }
    Psg = { Schema = Fidelity.PSG.Revision.Schema; AssemblySha256 = "psg-hash"
            FormatVersion = Fidelity.PSG.Binary.FormatVersion; ContractFingerprint = Fidelity.PSG.Binary.ContractFingerprint }
    Operations = [| Operation.Hello; Operation.Open; Operation.Reserve; Operation.Build; Operation.Status
                    Operation.Run; Operation.Cancel; Operation.CancelRequest; Operation.Close; Operation.PrepareCompilerChange; Operation.Format |]
    InMemoryPatchAllowed = false
  }
  Opened { Observation = 1UL; Project = "/project.fidproj"; ManifestPath = "/current.json" }
  Reserved { Reservation = "opaque" }; Built artifact; Observed status
  Ran { Generation = 23L; SourceVersion = "source-version"; ExitCode = -7; StandardOutput = "α\n\000"; StandardError = "stderr" }
  Canceled; RequestCanceled { TargetRequestId = "old/exact/id"; CancellationRequested = false }
  Closed { Observation = 20UL; Closed = true; CleanupPending = true; CleanupError = Some "held" }
  CompilerRetired { RestartRequired = true; InMemoryPatchAllowed = false }
  Formatted {
    Document = "buffer://α"; Incarnation = "00000000-0000-0000-0000-000000000001"; Revision = UInt64.MaxValue
    SourceSha256 = "base-digest"; Configuration = "clef-two-space-lf-v1"; FormatterIdentity = "pinned-closure"; Formatted = "module Sample\n\nlet value = 1\n"
  }
]
let private refusals = [
  RefusalCode.InvalidRequest; RefusalCode.ProtocolVersion; RefusalCode.EncodingMismatch; RefusalCode.ContractMismatch
  RefusalCode.WrongAuthority; RefusalCode.WrongProvider; RefusalCode.InvalidProject; RefusalCode.InvalidCache
  RefusalCode.CompilerRetired; RefusalCode.UnknownSession; RefusalCode.Closed; RefusalCode.UnsupportedOperation
  RefusalCode.RequestRefused; RefusalCode.DuplicateRequest; RefusalCode.CleanupFailed; RefusalCode.Superseded
  RefusalCode.CompilerRefused; RefusalCode.Canceled; RefusalCode.ObservationCapacity; RefusalCode.Busy
  RefusalCode.SessionCapacity; RefusalCode.BackendFailed; RefusalCode.InvalidReservation; RefusalCode.NotAccepted
  RefusalCode.FrameTooLarge; RefusalCode.MalformedPayload
  RefusalCode.ProviderRetiring; RefusalCode.ProviderUnavailable; RefusalCode.RetirementFailed; RefusalCode.Timeout
]

/// Test/build-time reflection only. The production codec contains no discovery.
/// Include every reachable declared record field and union case.
let contractShape () =
  let rows = Collections.Generic.SortedDictionary<string, string>(StringComparer.Ordinal)
  let rec shape (kind: Type) =
    if kind.IsArray then "array<" + shape (kind.GetElementType()) + ">"
    elif Microsoft.FSharp.Reflection.FSharpType.IsTuple kind then
      "tuple<" + (Microsoft.FSharp.Reflection.FSharpType.GetTupleElements kind |> Array.map shape |> String.concat ",") + ">"
    elif kind.IsGenericType then
      let definition = kind.GetGenericTypeDefinition()
      let arguments = kind.GetGenericArguments() |> Array.map shape |> String.concat ","
      definition.FullName + "<" + arguments + ">"
    elif kind.FullName.Contains("Bozzetto.", StringComparison.Ordinal) then
      let name = kind.FullName.Substring(kind.FullName.IndexOf("Bozzetto.", StringComparison.Ordinal))
      if not (rows.ContainsKey name) then
        rows.Add(name, "")
        let field (value: Reflection.PropertyInfo) = value.Name + ":" + shape value.PropertyType
        let body =
          if Microsoft.FSharp.Reflection.FSharpType.IsRecord kind then
            "record " + (Microsoft.FSharp.Reflection.FSharpType.GetRecordFields kind |> Array.map field |> String.concat ";")
          elif Microsoft.FSharp.Reflection.FSharpType.IsUnion kind then
            Microsoft.FSharp.Reflection.FSharpType.GetUnionCases kind
            |> Array.map (fun case -> sprintf "%d:%s(%s)" case.Tag case.Name (case.GetFields() |> Array.map field |> String.concat ";"))
            |> String.concat "|"
          else failtestf "Wire contract contains unsupported CLR shape %O" kind
        rows[name] <- name + "=" + body
      name
    else kind.FullName
  shape typeof<Request> |> ignore
  shape typeof<Reply> |> ignore
  (rows.Values |> String.concat "\n") + "\n"

/// Force real fragmented Stream.ReadAsync without timer-based readiness claims.
type private FragmentedStream(bytes: byte array) =
  inherit MemoryStream(bytes, false)
  override this.ReadAsync(buffer: Memory<byte>, cancellation: CancellationToken): ValueTask<int> =
    base.ReadAsync(buffer.Slice(0, min 1 buffer.Length), cancellation)

[<Tests>]
let tests = testList "Composer typed binary worker protocol" [
  testCase "schema digest includes every declared nested contract field and union tag" <| fun _ ->
    let path = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Bozzetto.Composer.Protocol", "WireSchema.txt"))
    let bytes = File.ReadAllBytes path
    let source = Encoding.UTF8.GetString bytes
    let marker = "\n# Derived closed CLR contract shape\n"
    let index = source.IndexOf(marker, StringComparison.Ordinal)
    index >= 0 |> Expect.isTrue "schema includes the generated type-shape section"
    source.Substring(index + marker.Length) |> Expect.equal "schema shape must track actual declared fields and order" (contractShape ())
    bytes |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
    |> Expect.equal "negotiated digest covers schema bytes and actual type shape" BAREWireCodec.ContractDigest

  testCase "request and reply tags follow shared declaration order" <| fun _ ->
    for index, body in List.indexed requests do
      let case, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(body, typeof<RequestBody>)
      case.Tag |> Expect.equal "request declaration order" index
      let bytes = { request body with RequestId = "" } |> BAREWireCodec.encodeRequest |> mustSucceed
      bytes[3] |> Expect.equal "request encoded tag" (byte case.Tag)
    for index, body in List.indexed replies do
      let case, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(body, typeof<ReplyBody>)
      case.Tag |> Expect.equal "reply declaration order" index
      let operation, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(BAREWireCodec.replyOperation body, typeof<Operation>)
      operation.Tag |> Expect.equal "reply operation follows its matching request declaration" index
      let offset = (reply Canceled |> BAREWireCodec.encodeReply |> mustSucceed).Length - 1
      let bytes = reply body |> BAREWireCodec.encodeReply |> mustSucceed
      bytes[offset] |> Expect.equal "reply encoded tag" (byte case.Tag)

  testCase "every request case roundtrips full string identity and payload" <| fun _ ->
    requests.Length |> Expect.equal "all closed request cases covered" (Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<RequestBody>).Length)
    for body in requests do
      let original = request body
      original |> BAREWireCodec.encodeRequest |> mustSucceed |> BAREWireCodec.decodeRequest
      |> Expect.equal "request exact equality" (Result.Ok original)

  testCase "every reply case preserves authority independently from artifact generation" <| fun _ ->
    replies.Length |> Expect.equal "all closed reply cases covered" (Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<ReplyBody>).Length)
    for body in replies do
      let original = reply body
      original |> BAREWireCodec.encodeReply |> mustSucceed |> BAREWireCodec.decodeReply
      |> Expect.equal "reply exact equality" (Result.Ok original)

  testCase "public status labels formatter evidence separately from compiler failure" <| fun _ ->
    let projected = reply (Observed status) |> Bozzetto.ComposerIntegration.ComposerClientJson.wireReply
    let body = projected.GetProperty "result"
    body.GetProperty("backendError").GetString() |> Expect.equal "compiler evidence keeps its own label" "backend error"
    body.GetProperty("formatterError").GetString() |> Expect.equal "formatter evidence has its own label" "formatter host error"
    let clear = { status with BackendError = None; FormatterError = None }
    let projected = reply (Observed clear) |> Bozzetto.ComposerIntegration.ComposerClientJson.wireReply
    for field in [ "backendError"; "formatterError" ] do
      projected.GetProperty("result").GetProperty(field).ValueKind
      |> Expect.equal "absent diagnostic is a JSON null" System.Text.Json.JsonValueKind.Null
    reply (Observed clear) |> BAREWireCodec.encodeReply |> mustSucceed |> BAREWireCodec.decodeReply
    |> Expect.equal "empty independent fields roundtrip over binary" (Result.Ok(reply (Observed clear)))

  testCase "all refusal codes preserve typed identity and stable explicit tags" <| fun _ ->
    refusals.Length |> Expect.equal "all closed refusals covered" (Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<RefusalCode>).Length)
    refusals |> List.map BAREWireCodec.refusalTag |> Expect.equal "wire tags are an explicit contiguous inventory" [0 .. 29]
    for code in refusals do
      let original = { reply Canceled with Outcome = Result.Error { Code = code; Message = "specific α refusal" } }
      original |> BAREWireCodec.encodeReply |> mustSucceed |> BAREWireCodec.decodeReply
      |> Expect.equal "refusal exact equality" (Result.Ok original)

  testCase "golden cancel request fixes field order tag width and byte identity" <| fun _ ->
    let target = { Worker = { Host = "h"; Epoch = "e"; Provider = ProviderIdentity.ClefComposer }; Session = "s" }
    let original = { request (Cancel target) with RequestId = "r" }
    original |> BAREWireCodec.encodeRequest |> mustSucceed
    |> Expect.equal "explicit BARE schema bytes" [| 2uy; 0uy; 1uy; 114uy; 6uy; 1uy; 104uy; 1uy; 101uy; 1uy; 1uy; 115uy |]

  testCase "truncation unknown union tag and suffix never decode as accepted request" <| fun _ ->
    let bytes = request (Hello BAREWireCodec.agreement) |> BAREWireCodec.encodeRequest |> mustSucceed
    for length in 0 .. bytes.Length - 1 do
      BAREWireCodec.decodeRequest(bytes |> Array.take length) |> Result.isError |> Expect.isTrue "every proper prefix is refused"
    BAREWireCodec.decodeRequest(Array.append bytes [| 0uy |]) |> Result.isError |> Expect.isTrue "suffix is refused"
    let simple = { request (Cancel session) with RequestId = "r" } |> BAREWireCodec.encodeRequest |> mustSucceed
    simple[4] <- 127uy
    BAREWireCodec.decodeRequest simple |> Expect.equal "unknown request case" (Result.Error CodecFailure.InvalidPayload)

  testCase "invalid option tags and oversized collection declarations are refused" <| fun _ ->
    let bytes = reply Canceled |> BAREWireCodec.encodeReply |> mustSucceed
    bytes[bytes.Length - 2] <- 2uy
    BAREWireCodec.decodeReply bytes |> Expect.equal "result option tag is strict" (Result.Error CodecFailure.InvalidPayload)
    let run = request (RequestBody.Run(session, [||])) |> BAREWireCodec.encodeRequest |> mustSucceed
    run[run.Length - 1] <- 127uy
    BAREWireCodec.decodeRequest run |> Expect.equal "array count exceeds its actual extent" (Result.Error CodecFailure.InvalidPayload)

  testCase "invalid UTF8 and unpaired outbound surrogate cannot alias another identity" <| fun _ ->
    let original = { request (Cancel session) with RequestId = "r" }
    let bytes = BAREWireCodec.encodeRequest original |> mustSucceed
    bytes[3] <- 255uy
    BAREWireCodec.decodeRequest bytes |> Expect.equal "bad UTF8 is not repaired" (Result.Error CodecFailure.InvalidPayload)
    BAREWireCodec.encodeRequest { original with RequestId = String(char 0xD800, 1) }
    |> Expect.equal "bad UTF16 is not repaired" (Result.Error CodecFailure.InvalidPayload)

  testCase "one MiB envelope budget admits exact fit and refuses one more byte" <| fun _ ->
    let empty = request (Open(worker, ""))
    let baseline = BAREWireCodec.encodeRequest empty |> mustSucceed
    // For a large string the BARE length prefix grows from one to three bytes.
    let count = BAREWireCodec.MaximumPayload - baseline.Length - 2
    let exact = request (Open(worker, String('x', count)))
    let frame = StreamFrames.encodeRequest exact |> mustSucceed
    frame.Length |> Expect.equal "prefix is outside the body admission budget" (BAREWireCodec.MaximumBody + 4)
    StreamFrames.encodeRequest (request (Open(worker, String('x', count + 1))))
    |> Expect.equal "one byte over budget" (Result.Error CodecFailure.FrameTooLarge)

  testCase "version mismatch is explicit and never tries another encoding" <| fun _ ->
    let current = request (Hello BAREWireCodec.agreement)
    BAREWireCodec.encodeRequest { current with ProtocolVersion = 1us }
    |> Expect.equal "old version outbound is refused" (Result.Error(CodecFailure.UnsupportedVersion 1us))
    let bytes = BAREWireCodec.encodeRequest current |> mustSucceed
    bytes[0] <- 1uy
    BAREWireCodec.decodeRequest bytes |> Expect.equal "old version inbound is refused" (Result.Error(CodecFailure.UnsupportedVersion 1us))

  testCase "retired full-revision operation tags have no request or reply decoder" <| fun _ ->
    let requestBytes = { request (Status session) with RequestId = "" } |> BAREWireCodec.encodeRequest |> mustSucceed
    requestBytes[3] <- 10uy
    BAREWireCodec.decodeRequest requestBytes |> Expect.equal "retired request tag is refused" (Result.Error CodecFailure.InvalidPayload)
    let replyBytes = reply Canceled |> BAREWireCodec.encodeReply |> mustSucceed
    replyBytes[replyBytes.Length - 1] <- 10uy
    BAREWireCodec.decodeReply replyBytes |> Expect.equal "retired reply tag is refused" (Result.Error CodecFailure.InvalidPayload)

  testCaseAsync "reply prefix exceeding the control cap is refused before its body" <| async {
    let prefix = BitConverter.GetBytes(uint32 BAREWireCodec.MaximumBody + 1u)
    use stream = new MemoryStream(Array.append prefix [| 99uy |], false)
    let! result = StreamFrames.readReplyAsync stream CancellationToken.None |> Async.AwaitTask
    result |> Expect.equal "hard reply bound" (Result.Error(FrameFailure.InvalidLength(uint32 BAREWireCodec.MaximumBody + 1u)))
    stream.Position |> Expect.equal "no declared body consumed" 4L
  }

  testCaseAsync "fragmented binary stream preserves adjacent frames and clean EOF" <| async {
    let first = request (Status session)
    let second = request (CancelRequest(worker, "first"))
    let bytes = Array.append (StreamFrames.encodeRequest first |> mustSucceed) (StreamFrames.encodeRequest second |> mustSucceed)
    use stream = new FragmentedStream(bytes)
    let! a = StreamFrames.readAsync FrameKind.Ask stream CancellationToken.None |> Async.AwaitTask
    let! b = StreamFrames.readAsync FrameKind.Ask stream CancellationToken.None |> Async.AwaitTask
    let! ended = StreamFrames.readAsync FrameKind.Ask stream CancellationToken.None |> Async.AwaitTask
    a |> mustSucceed |> Option.get |> BAREWireCodec.decodeRequest |> Expect.equal "first" (Result.Ok first)
    b |> mustSucceed |> Option.get |> BAREWireCodec.decodeRequest |> Expect.equal "second" (Result.Ok second)
    ended |> Expect.equal "EOF only at frame boundary" (Result.Ok None)
  }

  testCaseAsync "oversize prefix is rejected before reading or allocating its declared body" <| async {
    use stream = new MemoryStream([| 1uy; 0uy; 16uy; 0uy; 99uy |], false)
    let! result = StreamFrames.readAsync FrameKind.Ask stream CancellationToken.None |> Async.AwaitTask
    result |> Expect.equal "one byte over limit" (Result.Error(FrameFailure.InvalidLength 1048577u))
    stream.Position |> Expect.equal "only prefix consumed" 4L
  }

  testCaseAsync "partial EOF wrong frame kind and nonzero envelope correlation refuse" <| async {
    use prefix = new MemoryStream([| 5uy; 0uy |], false)
    let! a = StreamFrames.readAsync FrameKind.Ask prefix CancellationToken.None |> Async.AwaitTask
    a |> Expect.equal "truncated prefix" (Result.Error FrameFailure.TruncatedPrefix)
    use body = new MemoryStream([| 5uy; 0uy; 0uy; 0uy; 1uy |], false)
    let! b = StreamFrames.readAsync FrameKind.Ask body CancellationToken.None |> Async.AwaitTask
    b |> Expect.equal "truncated body" (Result.Error FrameFailure.TruncatedBody)
    let bytes = StreamFrames.encodeRequest (request (Status session)) |> mustSucceed
    bytes[4] <- FrameKind.Reply
    use wrong = new MemoryStream(bytes, false)
    let! c = StreamFrames.readAsync FrameKind.Ask wrong CancellationToken.None |> Async.AwaitTask
    c |> Expect.equal "wrong direction" (Result.Error(FrameFailure.UnexpectedFrameKind FrameKind.Reply))
    bytes[4] <- FrameKind.Ask
    bytes[5] <- 1uy
    use correlation = new MemoryStream(bytes, false)
    let! d = StreamFrames.readAsync FrameKind.Ask correlation CancellationToken.None |> Async.AwaitTask
    d |> Expect.equal "no hidden u32 identity map" (Result.Error(FrameFailure.InvalidCorrelation 1u))
  }
]
