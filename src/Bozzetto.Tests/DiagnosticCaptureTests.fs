module Bozzetto.Tests.DiagnosticCaptureTests

open System
open System.Text
open System.Security.Cryptography
open Expecto
open Expecto.Flip
open Fidelity.PSG
open Bozzetto.Diagnostics

let private take = function Ok value -> value | Error reason -> failtestf "%A" reason

let internal sampleCapture () : OccurrenceCapture =
  let revision : CheckedRevisionId = { Session = "source-session"; Ordinal = 9007199254740993UL }
  let stamp : ScopeContentStamp = { Identity = "source-demanded-scope"; Version = 18446744073709551615UL }
  let support = SupportKey.WholeOwningAnalysisRegion "source-selected-owner"
  let context : OccurrenceBreadcrumb =
    { Parent = NodeId 1; Port = OccurrencePort.StructuralChild; Ordinal = 0; Extent = 2; Stamp = "original-child-port" }
  let descriptor : ScopeDescriptor =
    { Content = stamp; LiveOccurrences = Set.ofList [NodeId 1; NodeId 2]
      ContextBreadcrumbs = Map.ofList [NodeId 1, [[]]; NodeId 2, [[context]]]
      RequiredBoundaries = Set.empty; RequiredSupports = Set.singleton support; OwnedArtifacts = Set.empty }
  let boolean = TypeIdentity.Application({ Declaration = { Module = []; Name = "bool" }; Parameters = []; NativeKind = Some NTUKind.NTUbool }, [])
  let node identity kind children parent : SemanticNode =
    { Id = NodeId identity; Kind = kind; Type = boolean; Children = List.map NodeId children; Parent = parent
      Range = { File = "capture-fixture.clef"; Start = { Line = 1; Column = 0 }; End = { Line = 1; Column = 1 } }
      IsReachable = true; EmissionStrategy = EmissionStrategy.Inline; ValueRange = None; ObligationAnchors = [] }
  let parent = node 1 (SemanticKind.Sequential [NodeId 2; NodeId 99]) [2; 99] None
  let child = node 2 (SemanticKind.Literal(NativeLiteral.Bool true)) [] (Some(NodeId 1))
  let section : LiveOccurrenceSection =
    { Descriptor = descriptor
      Occurrences = Map.ofList [
        NodeId 1, { Body = parent; OccurrenceContexts = [[]]
                    Children = [{ Ordinal = 0; Traversal = ChildTraversal.EnterLocal(NodeId 2) }
                                { Ordinal = 1; Traversal = ChildTraversal.SourceOmitted(support, NodeId 99) }] }
        NodeId 2, { Body = child; OccurrenceContexts = [[context]]; Children = [] } ] }
  { Metadata = { Demand = "demand-fixture"; SourceVersion = "source-content-identity"; ExpectedScopes = [stamp] }
    Delivery = {
      Transaction = {
        BaseCursor = { Subscription = "demand-fixture"; Ordinal = 0UL }; BaseRevision = revision
        TargetCursor = { Subscription = "demand-fixture"; Ordinal = 1UL }; TargetRevision = revision
        Changes = [ScopeChange.Add descriptor]
        Authorizations = [{ Content = stamp; CheckedRevision = revision; Boundaries = []; Supports = [{ Key = support; Version = 7UL }] }]
        Withdrawals = []; ArtifactTransitions = [] }
      Sections = [section] }
    ContextHeaders = []; ContextRoots = [NodeId 1] }

let private mapSection change capture =
  { capture with Delivery = { capture.Delivery with Sections = capture.Delivery.Sections |> List.map change } }

[<Tests>]
let tests = testList "Diagnostic occurrence capture" [
  test "complete demanded occurrence account roundtrips with exact identities and omission handles" {
    let original = sampleCapture ()
    Capture.validate original |> Expect.isOk "source-authored standalone occurrence structure is complete"
    let received = original |> CaptureCodec.encode |> take |> CaptureCodec.decode |> take
    received |> Expect.equal "all source identity fields survive transport" original
    received.Delivery.Sections.Head.Occurrences.ContainsKey(NodeId 99)
    |> Expect.isFalse "source omission does not acquire an inactive body"
    Capture.Stage |> Expect.equal "receipt says which integrity boundary was checked" "occurrence-structure-v1"
  }

  test "missing body and undeclared semantic reference cannot claim complete capture" {
    let original = sampleCapture ()
    let missing = original |> mapSection (fun section -> { section with Occurrences = section.Occurrences.Remove(NodeId 2) })
    CaptureCodec.encode missing |> Expect.isError "missing selected body is incomplete"
    let dangling = original |> mapSection (fun section ->
      let child = section.Occurrences[NodeId 2]
      { section with Occurrences = section.Occurrences.Add(NodeId 2, { child with Body = { child.Body with Kind = SemanticKind.VarRef("missing", Some(NodeId 404)) } }) })
    CaptureCodec.encode dangling |> Expect.isError "kind references outside structural children must also resolve"
  }

  test "capture refuses scope substitution imported facts and nonempty resident bases" {
    let original = sampleCapture ()
    { original with Metadata = { original.Metadata with ExpectedScopes = [{ Identity = "unrequested"; Version = 1UL }] } }
    |> CaptureCodec.encode |> Expect.isError "scope must be the exact demand account"
    { original with Metadata = { original.Metadata with Demand = "another-demand" } }
    |> CaptureCodec.encode |> Expect.isError "metadata cannot relabel a different source demand"
    let later =
      { original.Delivery.Transaction with
          BaseCursor = { original.Delivery.Transaction.BaseCursor with Ordinal = 1UL }
          TargetCursor = { original.Delivery.Transaction.TargetCursor with Ordinal = 2UL } }
    { original with Delivery = { original.Delivery with Transaction = later } }
    |> CaptureCodec.encode |> Expect.isError "unstored base cannot be silently assumed"
    let imported = original |> mapSection (fun section ->
      let parent = section.Occurrences[NodeId 1]
      let boundary : BoundaryContractStamp = { Identity = "imported-boundary"; Version = 1UL }
      let children = [{ Ordinal = 0; Traversal = ChildTraversal.EnterImported({ Identity = "outside"; Version = 1UL }, boundary, NodeId 2) }; parent.Children[1]]
      { section with Occurrences = section.Occurrences.Add(NodeId 1, { parent with Children = children }) })
    CaptureCodec.encode imported |> Expect.isError "an imported stamp is not its boundary payload"
  }

  test "declaration context is explicit and sparse without a retained container body" {
    let original = sampleCapture ()
    let frame : OccurrenceBreadcrumb = { Parent = NodeId 100; Port = OccurrencePort.ModuleDeclaration; Ordinal = 5; Extent = 40; Stamp = "module-source-port" }
    let section = original.Delivery.Sections.Head
    let contexts = section.Descriptor.ContextBreadcrumbs |> Map.map (fun _ paths -> paths |> List.map (fun path -> path @ [frame]))
    let descriptor = { section.Descriptor with ContextBreadcrumbs = contexts }
    let occurrences = section.Occurrences |> Map.map (fun key occurrence ->
      { occurrence with OccurrenceContexts = contexts[key]
                        Body = if key = NodeId 1 then { occurrence.Body with Parent = Some(NodeId 100) } else occurrence.Body })
    let header : SourceContextHeader = { Identity = NodeId 100; Name = "SelectedModule"
                                         Ports = Map.ofList [OccurrencePort.ModuleDeclaration,
                                           { Extent = 40; Stamp = frame.Stamp; Positions = Map.ofList [5, NodeId 1] }] }
    let delivery = {
      original.Delivery with
        Sections = [{ Descriptor = descriptor; Occurrences = occurrences }]
        Transaction = { original.Delivery.Transaction with Changes = [ScopeChange.Add descriptor] } }
    let captured = { original with ContextHeaders = [header]; ContextRoots = [NodeId 100]; Delivery = delivery }
    let received = captured |> CaptureCodec.encode |> take |> CaptureCodec.decode |> take
    received |> Expect.equal "original sparse position and context survive" captured
    { captured with ContextHeaders = [] } |> CaptureCodec.encode |> Expect.isError "ancestor identity alone is insufficient"
    { captured with ContextRoots = [] } |> CaptureCodec.encode |> Expect.isError "root completion is source-declared"
  }

  test "every truncation appended byte and valid-value metadata corruption is refused" {
    let bytes = sampleCapture () |> CaptureCodec.encode |> take
    for length in 0 .. bytes.Length - 1 do
      CaptureCodec.decode (Array.take length bytes) |> Expect.isError (sprintf "incomplete frame %d cannot persist as complete" length)
    Array.append bytes [|0uy|] |> CaptureCodec.decode |> Expect.isError "trailing bytes change the envelope"
    let marker = Encoding.UTF8.GetBytes "demand-fixture"
    let offset = [0 .. bytes.Length - marker.Length] |> List.find (fun at -> bytes[at .. at + marker.Length - 1] = marker)
    let corrupt = Array.copy bytes
    corrupt[offset] <- byte 'e'
    match CaptureCodec.decode corrupt with
    | Error message -> message |> Expect.stringContains "valid UTF-8 corruption still fails transport integrity" "checksum"
    | Ok _ -> failtest "Corrupted demand identity was accepted"
  }

  test "contradictory source stamps for the same structural port are refused" {
    let original = sampleCapture ()
    let section = original.Delivery.Sections.Head
    let child = section.Occurrences[NodeId 2]
    let frame = child.OccurrenceContexts.Head.Head
    let contexts = [[frame]; [{ frame with Stamp = "different-source-port" }]]
    let descriptor = { section.Descriptor with ContextBreadcrumbs = section.Descriptor.ContextBreadcrumbs.Add(NodeId 2, contexts) }
    let changed = { section with Descriptor = descriptor; Occurrences = section.Occurrences.Add(NodeId 2, { child with OccurrenceContexts = contexts }) }
    let delivery = {
      original.Delivery with
        Sections = [changed]
        Transaction = { original.Delivery.Transaction with Changes = [ScopeChange.Add descriptor] } }
    let capture = { original with Delivery = delivery }
    OccurrenceDelivery.check capture.Delivery |> Expect.isEmpty "the occurrence-only codec does not compare shared port stamps"
    match Capture.validate capture with
    | Error reason -> reason |> Expect.stringContains "the self-contained account requires one source port identity" "stamps"
    | Ok () -> failtest "Conflicting structural port stamps were accepted"
  }

  test "full revision image has no diagnostic fallback and malformed checksum-valid schema is refused" {
    let bytes = sampleCapture () |> CaptureCodec.encode |> take
    let mutated = bytes[..bytes.Length - 33]
    BAREWire.Encoding.Encoder.writeU32 mutated 9 (CaptureCodec.Schema + 1u) |> ignore
    Array.append mutated (SHA256.HashData mutated) |> CaptureCodec.decode |> Expect.isError "schema negotiation is strict after checksum validation"
    let limits : BinaryLimits =
      { MaxBytes = 1048576; MaxCollectionLength = 10000; MaxDepth = 128
        MaxStringBytes = 65536; MaxBigIntegerBytes = 65536; MaxValues = 100000 }
    let complete = Binary.encode limits (Revision.empty "diagnostic-test") |> take
    match Array.append complete (SHA256.HashData complete) |> CaptureCodec.decode with
    | Error message -> message |> Expect.stringContains "even a valid whole revision has no alternate decoder" "magic"
    | Ok _ -> failtest "A complete Revision image entered the occurrence sink"
  }

  test "capture receipt and refusal responses roundtrip and protect their integrity" {
    let receipt = { Digest = String.replicate 64 "A"; PayloadBytes = 1234L; Stage = Capture.Stage }
    Ok receipt |> CaptureCodec.encodeResponse |> take |> CaptureCodec.decodeReply
    |> Expect.equal "receipt identity is retained" (Ok receipt)
    Error "source scope incomplete" |> CaptureCodec.encodeResponse |> take |> CaptureCodec.decodeReply
    |> Expect.equal "refusal stays distinguishable from acceptance" (Error "source scope incomplete")
    let bytes = Ok receipt |> CaptureCodec.encodeResponse |> take
    bytes[bytes.Length - 1] <- bytes[bytes.Length - 1] ^^^ 1uy
    CaptureCodec.decodeReply bytes |> Expect.isError "corrupted acknowledgement is not confirmed retention"
  }

  test "validation diagnostics stay bounded for many malformed references" {
    let original = sampleCapture ()
    let broken = original |> mapSection (fun section ->
      let child = section.Occurrences[NodeId 2]
      let body = { child.Body with Kind = SemanticKind.Sequential [for value in 1000 .. 3000 -> NodeId value] }
      { section with Occurrences = section.Occurrences.Add(NodeId 2, { child with Body = body }) })
    match Capture.validate broken with
    | Error message ->
      message.Length <= 8192 |> Expect.isTrue "persistent invalid input cannot inflate status indefinitely"
      message |> Expect.stringContains "bounded report records omission" "truncated"
    | Ok () -> failtest "Unknown semantic references were accepted"
  }

  test "oversized refusal is bounded before serialization without splitting Unicode scalars" {
    let reason = String.replicate 100000 "😀"
    let response = Error reason |> CaptureCodec.encodeResponse |> take |> CaptureCodec.decodeReply
    match response with
    | Error message ->
      message.Length <= 8192 |> Expect.isTrue "all host and codec refusals fit the response field"
      UTF8Encoding(false, true).GetBytes message |> ignore
      message |> Expect.stringContains "loss of excess diagnostic text is explicit" "truncated"
    | Ok _ -> failtest "A refusal was converted into an acceptance receipt"
    let malformed = "fault: " + string (char 0xD800) + " and " + string (char 0xDC00)
    match Error malformed |> CaptureCodec.encodeResponse |> take |> CaptureCodec.decodeReply with
    | Error message ->
      message |> Expect.equal "malformed host exception text is replaced at the transport boundary" "fault: � and �"
    | Ok _ -> failtest "Malformed refusal was converted into success"
  }
]
