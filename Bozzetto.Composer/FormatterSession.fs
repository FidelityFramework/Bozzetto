namespace Bozzetto.Providers

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Bozzetto.Composer.Protocol
open Calque.Core
open Calque.Incremental

type FormatterDiagnostic = {
  Document: DocumentIdentity
  Diagnostic: Fidelity.FSharp.Incremental.Hosting.HostDiagnostic
}

/// Injectable immutable projection boundary. It carries no compiler ticket,
/// file read/write permission, artifact acceptance or execution authority.
type IFormatBackend =
  abstract PreviewAsync: FormatBuffer * CancellationToken -> Task<Result<FormatPreview, Bozzetto.Providers.Refusal>>
  abstract BeginClose: unit -> unit
  abstract CloseAsync: unit -> Task<Result<unit, string>>
  abstract DrainDiagnostics: unit -> FormatterDiagnostic list

module FormatPolicy =
  [<Literal>]
  let Configuration = "clef-two-space-lf-v1"
  let sourceSha256 source = UTF8Encoding(false, true).GetBytes(source: string) |> SHA256.HashData |> Convert.ToHexString
  let config = { FormatConfig.Default with IndentSize = 2; EndOfLine = EndOfLineStyle.LF }

/// Bounded handles and incarnation tombstones last for this provider epoch.
/// Document labels are never interpreted as paths or opened by this boundary.
type FormatterSession() =
  let gate = obj ()
  let handles = Dictionary<DocumentIdentity, DocumentFormatter.Handle>()
  let current = Dictionary<string, DocumentIdentity>(StringComparer.Ordinal)
  let latest = Dictionary<DocumentIdentity, SourceSnapshot>()
  let closing = Dictionary<DocumentIdentity, DocumentFormatter.CloseOperation>()
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
        | true, _ -> refuse "superseded" "This document incarnation was retired."
        | _ when handles.Count >= 32 -> refuse "session_capacity" "The provider retains at most 32 document incarnations; open a fresh session."
        | _ ->
          match DocumentFormatter.create { Epoch = 1UL; CommandCapacity = 256; MaxDemands = 128 } document with
          | Error failure -> error failure
          | Ok handle ->
            match DocumentFormatter.start handle with
            | Error failure -> closing.Add(document, DocumentFormatter.beginClose handle); handles.Add(document, handle); error failure
            | Ok () ->
              match current.TryGetValue document.Uri with
              | true, previous -> closing[previous] <- DocumentFormatter.beginClose handles[previous]
              | _ -> ()
              handles.Add(document, handle)
              current[document.Uri] <- document
              Ok handle
      selected |> Result.bind (fun handle ->
        let snapshot: SourceSnapshot = {
          Document = document; Revision = buffer.Revision; ConfigurationRevision = 1UL
          IsSignature = false; Source = buffer.Source; Config = FormatPolicy.config
        }
        DocumentFormatter.request snapshot handle |> Result.map (fun request -> latest[document] <- snapshot; snapshot, handle, request) |> Result.mapError refusalOfError))
  member _.PreviewAsync(buffer: FormatBuffer, cancellation: CancellationToken) = task {
    if cancellation.IsCancellationRequested then return refuse "canceled" "The formatting request was canceled before admission."
    else
      match admit buffer with
      | Error refusal -> return Error refusal
      | Ok(snapshot, handle, request) ->
        let observation = Async.StartAsTask(DocumentFormatter.observe request handle, cancellationToken = CancellationToken.None)
        let! result = task {
          try
            let! observed = observation.WaitAsync cancellation
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
          | :? OperationCanceledException -> return refuse "canceled" "The formatting demand was canceled."
          | failure -> return refuse "backend_failed" failure.Message
        }
        let! released = task {
          try
            match DocumentFormatter.release request handle with
            | Ok control ->
              let! acknowledged = Async.StartAsTask(DocumentFormatter.observeControl control, cancellationToken = CancellationToken.None)
              return acknowledged |> Result.mapError (sprintf "%A")
            | Error FormatterError.Closed -> return Ok () // session close owns the physical join
            | Error failure -> return Error(sprintf "%A" failure)
          with failure -> return Error failure.Message
        }
        let! joined = task {
          // WaitAsync cancels only its projection. Release/close wakes the
          // retained observation; join it before completing this owned task.
          try let! _ = observation in return Ok ()
          with failure -> return Error failure.Message
        }
        let controlFailure =
          match released, joined with
          | Error failure, _ -> Some("Formatter demand release failed: " + failure)
          | _, Error message -> Some("Formatter observation join failed: " + message)
          | _ -> None
        controlFailure |> Option.iter (fun message -> lock gate (fun () -> cleanupFailures.Add message))
        return lock gate (fun () ->
          if closed then refuse "closed" "The provider formatter is closed."
          elif current.TryGetValue snapshot.Document.Uri <> (true, snapshot.Document) || latest.TryGetValue snapshot.Document <> (true, snapshot) then
            refuse "superseded" "A newer document snapshot withdrew this preview."
          else match controlFailure with Some message -> refuse "backend_failed" message | None -> result)
  }
  member _.BeginClose() = lock gate (fun () ->
    closed <- true
    for KeyValue(document, handle) in handles do
      if not (closing.ContainsKey document) then closing.Add(document, DocumentFormatter.beginClose handle))
  member this.CloseAsync() = task {
    this.BeginClose()
    let owned = lock gate (fun () -> closing.Values |> Seq.toArray)
    let! results = owned |> Array.map (fun operation -> Async.StartAsTask(DocumentFormatter.awaitClose operation, cancellationToken = CancellationToken.None)) |> Task.WhenAll
    let closeFailures = results |> Array.choose (function Error failure -> Some(sprintf "%A" failure) | _ -> None)
    let releaseFailures = lock gate (fun () -> cleanupFailures.ToArray())
    let failures = Array.append closeFailures releaseFailures
    return if failures.Length = 0 then Ok () else Error(String.concat "; " failures)
  }
  member _.DrainDiagnostics() =
    let owned = lock gate (fun () -> handles |> Seq.map (fun pair -> pair.Key, pair.Value) |> Seq.toArray)
    owned |> Array.collect (fun (document, handle) ->
      DocumentFormatter.drainDiagnostics handle
      |> List.map (fun diagnostic -> { Document = document; Diagnostic = diagnostic })
      |> List.toArray) |> Array.toList
  interface IFormatBackend with
    member this.PreviewAsync(buffer, cancellation) = this.PreviewAsync(buffer, cancellation)
    member this.BeginClose() = this.BeginClose()
    member this.CloseAsync() = this.CloseAsync()
    member this.DrainDiagnostics() = this.DrainDiagnostics()
