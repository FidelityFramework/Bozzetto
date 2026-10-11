namespace Bozzetto.Diagnostics

open System
open System.IO.Pipes
open System.Threading

/// Local transport mechanics. Creating a workflow neither connects nor starts
/// a sink. The caller owns the observation; the independent sink owns any
/// complete capture it has accepted, even if this connection disappears.
module Client =
  let exchange pipeName (timeoutMilliseconds: int) (payload: byte array) = async {
    if String.IsNullOrWhiteSpace pipeName then
      return Result.Error "A diagnostic sink pipe name is required."
    elif timeoutMilliseconds <= 0 then
      return Result.Error "The diagnostic sink request needs a positive deadline."
    elif payload.Length > CaptureCodec.MaximumBody then
      return Result.Error "The diagnostic capture exceeds the transport limit."
    else
      let! observation = Async.CancellationToken
      use deadline = CancellationTokenSource.CreateLinkedTokenSource observation
      deadline.CancelAfter timeoutMilliseconds
      use pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                                           PipeOptions.Asynchronous ||| PipeOptions.CurrentUserOnly)
      try
        do! pipe.ConnectAsync(deadline.Token) |> Async.AwaitTask
        let! written = Transport.write pipe payload deadline.Token
        match written with
        | Result.Error reason -> return Result.Error reason
        | Result.Ok () -> return! Transport.read pipe deadline.Token
      with
      | :? OperationCanceledException ->
        return Result.Error "Diagnostic capture acknowledgment was interrupted; persistence is unconfirmed."
      | error -> return Result.Error ("Diagnostic sink transport failed: " + error.Message)
  }

  let capture pipeName timeoutMilliseconds (value: OccurrenceCapture) = async {
    match CaptureCodec.encode value with
    | Result.Error reason -> return Result.Error reason
    | Result.Ok payload ->
      let! response = exchange pipeName timeoutMilliseconds payload
      return response |> Result.bind CaptureCodec.decodeReply |> Result.bind (fun receipt ->
        let expected = payload |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
        if receipt.Digest <> expected || receipt.PayloadBytes <> int64 payload.Length
           || receipt.Stage <> Capture.Stage then
          Result.Error "Diagnostic sink receipt does not identify the submitted capture."
        else Result.Ok receipt)
  }
