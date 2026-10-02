namespace Bozzetto.Providers

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open Bozzetto.Composer.Protocol
open Calque.Core
open Calque.Incremental

type FormatterDiagnostic = {
  Document: DocumentIdentity
  Diagnostic: Fidelity.FSharp.Incremental.Hosting.HostDiagnostic
}

/// One accepted demand on the shared document computation. Observation is
/// cold; withdrawal only posts its release to the existing document mailbox.
/// The session owns accepted demands even when nobody observes them.
/// One resident owner runs Observe without ambient cancellation and joins its
/// release control. Additional consumers request their own demand handles;
/// Observe is an owner workflow, not a repeatable factory for new consumers.
[<NoEquality; NoComparison>]
type FormatDemand = {
  Observe: Async<Result<FormatPreview, Bozzetto.Providers.Refusal>>
  Withdraw: unit -> unit
}

/// Injectable immutable projection boundary. It carries no compiler ticket,
/// file read/write permission, artifact acceptance or execution authority.
type IFormatBackend =
  abstract RequestPreview: FormatBuffer -> Result<FormatDemand, Bozzetto.Providers.Refusal>
  /// Transfer each newly retired child once to the provider's owned controls.
  /// RequestPreview adds at most one child. The provider drains after each
  /// request; these cold joins are independent of the preview observation.
  abstract DrainRetirements: unit -> Async<Result<unit, string>> list
  abstract BeginClose: unit -> unit
  abstract CloseAsync: unit -> Async<Result<unit, string>>
  abstract DrainDiagnostics: unit -> FormatterDiagnostic list

module FormatPolicy =
  [<Literal>]
  let Configuration = "clef-two-space-lf-v1"
  let sourceSha256 source = UTF8Encoding(false, true).GetBytes(source: string) |> SHA256.HashData |> Convert.ToHexString
  let config = { FormatConfig.Default with IndentSize = 2; EndOfLine = EndOfLineStyle.LF }

/// At most 32 live/closing handles and 8192 admitted identities per epoch.
/// Document labels are never interpreted as paths or opened by this boundary.
type FormatterSession internal (
  startDocument: DocumentFormatter.Handle -> Result<unit, FormatterError>,
  awaitDocumentClose: DocumentFormatter.CloseOperation -> Async<Result<unit, FormatterError>>) =
  let gate = obj ()
  let handles = Dictionary<DocumentIdentity, DocumentFormatter.Handle>()
  let current = Dictionary<string, DocumentIdentity>(StringComparer.Ordinal)
  let latest = Dictionary<DocumentIdentity, SourceSnapshot>()
  let closing = Dictionary<DocumentIdentity, DocumentFormatter.CloseOperation>()
  let pendingRetirements = System.Collections.Generic.Queue<DocumentIdentity * DocumentFormatter.CloseOperation>()
  let admittedIdentities = HashSet<DocumentIdentity>()
  let diagnostics = ResizeArray<FormatterDiagnostic>()
  let releaseControls = HashSet<DocumentFormatter.ControlOperation>(HashIdentity.Reference)
  let cleanupFailures = ResizeArray<string>()
  let mutable closed = false
  // Capture the deployment file snapshot once at owner creation. Development
  // files can be replaced independently of loaded assemblies; this identity
  // describes those captured files, never claims a late hash proves loaded IL.
  let identity =
    [ typeof<DocumentIdentity>.Assembly; typeof<FormatConfig>.Assembly; typeof<Calque.Syntax.Text.pos>.Assembly
      typeof<Fidelity.FSharp.Incremental.EpochId>.Assembly; typeof<Fidelity.FSharp.Incremental.Hosting.MailboxError>.Assembly ]
    |> List.map (fun assembly -> assembly.GetName().Name + ":" + assembly.Location + ":" + (File.ReadAllBytes assembly.Location |> SHA256.HashData |> Convert.ToHexString))
    |> String.concat ";"
    |> fun captured -> "owner-start-file-snapshots:" + captured
  let refuse code message = Error ({ Code = code; Message = message }: Bozzetto.Providers.Refusal)
  let refusalOfError failure : Bozzetto.Providers.Refusal =
    let code, message =
      match failure with
      | FormatterError.Superseded | FormatterError.ConflictingSnapshot | FormatterError.RevisionNotIncreasing -> "superseded", "Document revision or immutable snapshot identity is stale or conflicting."
      | FormatterError.Closed -> "closed", "The formatter document is closed."
      | FormatterError.Cancelled | FormatterError.Released -> "canceled", "The formatting demand was released."
      | FormatterError.DemandCapacity -> "busy", "The formatter demand capacity is full."
      | FormatterError.HostFailure failure -> "backend_failed", sprintf "Formatter host failed: %A" failure
      | other -> "request_refused", sprintf "Formatter refused: %A" other
    { Code = code; Message = message }
  let error failure = Error(refusalOfError failure)
  let joinClose (document, operation) = async {
    let! returned, result = async {
      try
        let! joined = awaitDocumentClose operation
        return true, joined |> Result.mapError (sprintf "%A")
      with failure -> return false, Error failure.Message
    }
    lock gate (fun () ->
      match closing.TryGetValue document with
      | true, retained when returned && obj.ReferenceEquals(retained, operation) ->
        let handle = handles[document]
        for diagnostic in DocumentFormatter.drainDiagnostics handle do
          diagnostics.Add { Document = document; Diagnostic = diagnostic }
        closing.Remove document |> ignore
        handles.Remove document |> ignore
        latest.Remove document |> ignore
        if current.TryGetValue document.Uri = (true, document) then current.Remove document.Uri |> ignore
        match result with
        | Error failure -> cleanupFailures.Add("Formatter document close failed: " + failure)
        | Ok () -> ()
      | true, _ when not returned ->
        match result with
        | Error failure -> cleanupFailures.Add("Formatter document close did not join: " + failure)
        | Ok () -> ()
      | _ -> ())
    return result
  }
  let retire document handle =
    if not (closing.ContainsKey document) then
      let operation = DocumentFormatter.beginClose handle
      closing.Add(document, operation)
      pendingRetirements.Enqueue(document, operation)
  let joinRelease control = async {
    let! result = async {
      try
        let! acknowledged = DocumentFormatter.observeControl control
        return acknowledged |> Result.mapError (sprintf "%A")
      with failure -> return Error failure.Message
    }
    lock gate (fun () ->
      if releaseControls.Remove control then
        match result with
        | Error failure -> cleanupFailures.Add("Formatter demand release failed: " + failure)
        | Ok () -> ())
    return result
  }
  let admit (buffer: FormatBuffer) = lock gate (fun () ->
    let mutable incarnation = Guid.Empty
    if closed then refuse "closed" "The provider formatter is closed."
    elif String.IsNullOrWhiteSpace buffer.Document || not (Guid.TryParse(buffer.Incarnation, &incarnation)) || incarnation = Guid.Empty then
      refuse "invalid_request" "A document identity and nonempty GUID incarnation are required."
    elif buffer.Configuration <> FormatPolicy.Configuration then refuse "invalid_request" "Use configuration clef-two-space-lf-v1."
    elif isNull buffer.Source then refuse "invalid_request" "An immutable source buffer is required."
    else
      let document = { Uri = buffer.Document; Incarnation = incarnation }
      let selected =
        match handles.TryGetValue document with
        | true, handle when current.TryGetValue document.Uri = (true, document) -> Ok handle
        | true, _ when admittedIdentities.Contains document -> refuse "superseded" "This document incarnation was retired."
        | true, _ -> refuse "busy" "The failed formatter start is still being joined."
        | _ when admittedIdentities.Contains document -> refuse "superseded" "This document incarnation was retired."
        | _ when admittedIdentities.Count >= 8192 -> refuse "session_capacity" "The provider formatter has reached its incarnation identity limit; open a fresh session."
        | _ ->
          if handles.Count >= 32 then
            match current.TryGetValue document.Uri with
            | true, previous ->
              retire previous handles[previous]
              current.Remove document.Uri |> ignore
            | _ -> ()
            if closing.Count > 0 then refuse "busy" "Retired formatter work must finish closing before another document can start."
            else refuse "session_capacity" "The provider retains at most 32 live document incarnations; open a fresh session."
          else
            match DocumentFormatter.create { Epoch = 1UL; CommandCapacity = 256; MaxDemands = 128 } document with
            | Error failure -> error failure
            | Ok handle ->
              handles.Add(document, handle)
              match startDocument handle with
              | Error failure -> retire document handle; error failure
              | Ok () ->
                match current.TryGetValue document.Uri with
                | true, previous -> retire previous handles[previous]
                | _ -> ()
                admittedIdentities.Add document |> ignore
                current[document.Uri] <- document
                Ok handle
      selected |> Result.bind (fun handle ->
        let snapshot: SourceSnapshot = {
          Document = document; Revision = buffer.Revision; ConfigurationRevision = 1UL
          IsSignature = false; Source = buffer.Source; Config = FormatPolicy.config
        }
        DocumentFormatter.request snapshot handle |> Result.map (fun request -> latest[document] <- snapshot; snapshot, handle, request) |> Result.mapError refusalOfError))
  new () = FormatterSession(DocumentFormatter.start, DocumentFormatter.awaitClose)
  member _.DrainRetirements() = lock gate (fun () ->
    let owned = pendingRetirements.ToArray()
    pendingRetirements.Clear()
    owned |> Array.map joinClose |> Array.toList)
  member private _.JoinRetiredAsync() =
    let owned = lock gate (fun () -> closing |> Seq.map (fun pair -> pair.Key, pair.Value) |> Seq.toArray)
    async {
      let! results = owned |> Array.map joinClose |> Async.Parallel
      let failures = results |> Array.choose (function Error failure -> Some failure | _ -> None)
      return if failures.Length = 0 then Ok () else Error(String.concat "; " failures)
    }
  member _.RequestPreview(buffer: FormatBuffer) =
    admit buffer |> Result.map (fun (snapshot, handle, request) ->
      // The exact command handle survives cancellation and observation. A
      // transport callback can withdraw this demand without starting a second
      // workflow or releasing another consumer of the same snapshot.
      let releaseGate = obj ()
      let mutable release = None
      let withdraw () = lock releaseGate (fun () ->
        match release with
        | Some operation -> operation
        | None ->
          let operation = lock gate (fun () ->
            let accepted =
              try
                match DocumentFormatter.release request handle with
                | Ok control ->
                  releaseControls.Add control |> ignore
                  Ok(Some control)
                | Error FormatterError.Closed -> Ok None // session close owns the physical join
                | Error failure -> Error(sprintf "%A" failure)
              with failure -> Error failure.Message
            match accepted with
            | Error failure -> cleanupFailures.Add("Formatter demand release failed: " + failure)
            | Ok _ -> ()
            accepted)
          release <- Some operation
          operation)
      let observe = async {
        let! result = async {
          try
            let! observed = DocumentFormatter.observe request handle
            return observed |> Result.mapError refusalOfError |> Result.bind (fun preview ->
              if not (DocumentFormatter.isCurrent snapshot handle) then refuse "superseded" "A newer buffer withdrew this preview."
              else
                match preview.Outcome with
                | FormatOutcome.Refused diagnostic -> refuse "request_refused" (diagnostic.Code + ": " + diagnostic.Message)
                | FormatOutcome.Formatted formatted ->
                  let preview: FormatPreview = {
                    Document = buffer.Document; Incarnation = buffer.Incarnation; Revision = buffer.Revision
                    SourceSha256 = FormatPolicy.sourceSha256 buffer.Source; Configuration = buffer.Configuration
                    FormatterIdentity = identity; Formatted = formatted.Code
                  }
                  Ok preview)
          with
          | failure -> return refuse "backend_failed" failure.Message
        }
        let! released = async {
          try
            match withdraw () with
            | Ok(Some control) -> return! joinRelease control
            | Ok None -> return Ok ()
            | Error failure -> return Error failure
          with failure -> return Error failure.Message
        }
        let controlFailure =
          match released with
          | Error failure -> Some("Formatter demand release failed: " + failure)
          | Ok () -> None
        return lock gate (fun () ->
          if closed then refuse "closed" "The provider formatter is closed."
          elif current.TryGetValue snapshot.Document.Uri <> (true, snapshot.Document) || latest.TryGetValue snapshot.Document <> (true, snapshot) then
            refuse "superseded" "A newer document snapshot withdrew this preview."
          else match controlFailure with Some message -> refuse "backend_failed" message | None -> result)
      }
      { Observe = observe; Withdraw = fun () -> withdraw () |> ignore })
  member _.BeginClose() = lock gate (fun () ->
    closed <- true
    pendingRetirements.Clear()
    for KeyValue(document, handle) in handles do
      if not (closing.ContainsKey document) then closing.Add(document, DocumentFormatter.beginClose handle))
  member this.CloseAsync() =
    this.BeginClose()
    let releases = lock gate (fun () -> releaseControls |> Seq.toArray)
    async {
      let! _ = releases |> Array.map joinRelease |> Async.Parallel
      let! _ = this.JoinRetiredAsync()
      let failures = lock gate (fun () -> cleanupFailures.ToArray())
      return if failures.Length = 0 then Ok () else Error(String.concat "; " failures)
    }
  member _.DrainDiagnostics() =
    lock gate (fun () ->
      for KeyValue(document, handle) in handles do
        for diagnostic in DocumentFormatter.drainDiagnostics handle do
          diagnostics.Add { Document = document; Diagnostic = diagnostic }
      let retained = diagnostics |> Seq.toList
      diagnostics.Clear()
      retained)
  interface IFormatBackend with
    member this.RequestPreview buffer = this.RequestPreview buffer
    member this.DrainRetirements() = this.DrainRetirements()
    member this.BeginClose() = this.BeginClose()
    member this.CloseAsync() = this.CloseAsync()
    member this.DrainDiagnostics() = this.DrainDiagnostics()
