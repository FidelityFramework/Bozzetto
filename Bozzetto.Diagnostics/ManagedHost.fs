namespace Bozzetto.Diagnostics

open System
open System.Diagnostics
open System.IO
open System.IO.Pipes
open System.Security.Cryptography
open System.Text
open System.Threading
open BAREWire.Encoding
open Fidelity.Data.JSON

/// The managed stream boundary uses a BARE u32 length followed by one complete
/// versioned capture or receipt. A connection carries one request and one reply.
module Transport =
  let private readExactly (stream: Stream) (buffer: byte array) cancellation = async {
    let mutable offset = 0
    let mutable ended = false
    while offset < buffer.Length && not ended do
      let! count = stream.ReadAsync(buffer.AsMemory(offset), cancellation).AsTask() |> Async.AwaitTask
      if count = 0 then ended <- true
      else offset <- offset + count
    return offset = buffer.Length
  }

  let read (stream: Stream) (cancellation: CancellationToken) = async {
    try
      let prefix = Array.zeroCreate<byte> 4
      let! prefixComplete = readExactly stream prefix cancellation
      if not prefixComplete then
        return Result.Error "The diagnostic frame ended before its length prefix."
      else
        let length, next = Decoder.readU32 prefix 0
        if Cursor.isFault next || length = 0u || length > uint32 CaptureCodec.MaximumBody then
          return Result.Error "The diagnostic frame length is outside the permitted bound."
        else
          let body = Array.zeroCreate<byte> (int length)
          let! complete = readExactly stream body cancellation
          return
            if complete then Result.Ok body
            else Result.Error "The diagnostic frame ended before its complete body."
    with
    | :? OperationCanceledException -> return Result.Error "The diagnostic frame read exceeded its deadline or was cancelled."
    | error -> return Result.Error ("The diagnostic frame could not be read: " + error.Message)
  }

  let write (stream: Stream) (body: byte array) (cancellation: CancellationToken) = async {
    if body.Length = 0 || body.Length > CaptureCodec.MaximumBody then
      return Result.Error "The diagnostic frame length is outside the permitted bound."
    else
      try
        let prefix = Array.zeroCreate<byte> 4
        Encoder.writeU32 prefix 0 (uint32 body.Length) |> ignore
        do! stream.WriteAsync(prefix.AsMemory(), cancellation).AsTask() |> Async.AwaitTask
        do! stream.WriteAsync(body.AsMemory(), cancellation).AsTask() |> Async.AwaitTask
        do! stream.FlushAsync(cancellation) |> Async.AwaitTask
        return Result.Ok ()
      with
      | :? OperationCanceledException -> return Result.Error "The diagnostic frame write exceeded its deadline or was cancelled."
      | error -> return Result.Error ("The diagnostic frame could not be written: " + error.Message)
  }

type HostOptions = {
  PipeName: string
  StoreDirectory: string
  IdleSeconds: int
  IoTimeoutMilliseconds: int
  MaxStoreBytes: int64
  MaxCaptures: int
}

/// Files, clocks, hashing and named pipes belong to this hosted adapter. No
/// compiler session, process parent or retained semantic graph owns this sink.
module ManagedHost =
  let validateOptions options =
    if String.IsNullOrWhiteSpace options.PipeName || options.PipeName.Length > 100
       || (options.PipeName |> Seq.exists (fun c -> not (Char.IsAsciiLetterOrDigit c || c = '-' || c = '_'))) then
      Result.Error "--pipe requires 1–100 ASCII letters, digits, hyphens or underscores."
    elif String.IsNullOrWhiteSpace options.StoreDirectory || not (Path.IsPathFullyQualified options.StoreDirectory) then
      Result.Error "--store must be an absolute directory path."
    elif options.IdleSeconds < 1 || options.IdleSeconds > 86400 then
      Result.Error "--idle-seconds must be between 1 and 86400."
    elif options.IoTimeoutMilliseconds < 100 || options.IoTimeoutMilliseconds > 60000 then
      Result.Error "--io-timeout-ms must be between 100 and 60000."
    elif options.MaxStoreBytes <= 0L || options.MaxCaptures <= 0 then
      Result.Error "The diagnostic store byte and capture bounds must be positive."
    else Result.Ok ()

  let private digest (body: byte array) =
    Convert.ToHexString(SHA256.HashData body)

  let private descriptor (capture: OccurrenceCapture) (receipt: CaptureReceipt) =
    let revision = capture.Delivery.Transaction.TargetRevision
    JsonValue.Object [
      "schemaVersion", JsonValue.ofInt64 1L
      "contentKind", JsonValue.String receipt.Stage
      "captureComplete", JsonValue.Bool true
      "digestAlgorithm", JsonValue.String "sha256"
      "digest", JsonValue.String receipt.Digest
      "payloadBytes", JsonValue.ofInt64 receipt.PayloadBytes
      "compilerAuthority", JsonValue.Bool false
      "demand", JsonValue.String capture.Metadata.Demand
      "sourceVersion", JsonValue.String capture.Metadata.SourceVersion
      "checkedRevision", JsonValue.Object [
        "session", JsonValue.String revision.Session
        "ordinal", JsonValue.ofUInt64 revision.Ordinal
      ]
      "expectedScopeCount", JsonValue.ofInt64 (int64 capture.Metadata.ExpectedScopes.Length)
      "sectionCount", JsonValue.ofInt64 (int64 capture.Delivery.Sections.Length)
      "contextHeaderCount", JsonValue.ofInt64 (int64 capture.ContextHeaders.Length)
      "contextRootCount", JsonValue.ofInt64 (int64 capture.ContextRoots.Length)
    ]
    |> Json.serializePretty
    |> Encoding.UTF8.GetBytes

  let private readStoredFile path expectedLength = async {
    use input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                               65536, FileOptions.Asynchronous)
    if expectedLength < 0L || expectedLength > int64 CaptureCodec.MaximumBody || input.Length <> expectedLength then
      return Result.Error "The stored diagnostic file has an unexpected length."
    else
      let bytes = Array.zeroCreate<byte> (int expectedLength)
      do! input.ReadExactlyAsync(bytes.AsMemory()).AsTask() |> Async.AwaitTask
      return Result.Ok bytes
  }

  let private validateStoredPayload path expectedDigest expectedLength = async {
    let! stored = readStoredFile path expectedLength
    match stored with
    | Result.Error reason -> return Result.Error reason
    | Result.Ok bytes ->
      if digest bytes <> expectedDigest then
        return Result.Error "The stored diagnostic payload does not match its digest."
      else
        return CaptureCodec.decode bytes |> Result.map ignore
  }

  let private publishFile path (bytes: byte array) = async {
    let temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
    try
      // Closing the owned stream precedes publication. A descriptor is the last
      // file published; an interrupted temporary file is never a commit marker.
      do!
        async {
          use output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                      65536, FileOptions.Asynchronous)
          do! output.WriteAsync(bytes.AsMemory()).AsTask() |> Async.AwaitTask
          do! output.FlushAsync() |> Async.AwaitTask
          output.Flush true
        }
      try File.Move(temporary, path, false)
      with :? IOException when File.Exists path -> ()
    finally
      if File.Exists temporary then File.Delete temporary
  }

  let private checkQuota options payloadPath descriptorPath bodyLength descriptorLength =
    let files = Directory.EnumerateFiles options.StoreDirectory |> Seq.map FileInfo |> Seq.toArray
    let currentBytes = files |> Array.sumBy _.Length
    let captures = files |> Array.filter (fun file -> file.Extension = ".bare") |> Array.length
    let newPayload = not (File.Exists payloadPath)
    let addition =
      (if newPayload then bodyLength else 0L)
      + (if File.Exists descriptorPath then 0L else int64 descriptorLength)
    if (newPayload && captures >= options.MaxCaptures)
       || currentBytes > options.MaxStoreBytes || addition > options.MaxStoreBytes - currentBytes then
      Result.Error (sprintf "Diagnostic store capacity reached at %s; preserve or remove captures before retrying." options.StoreDirectory)
    else Result.Ok ()

  /// Completion means both the validated binary body and its sink-generated
  /// descriptor were published. Losing a reply leaves the caller uncertain;
  /// replaying the same bytes verifies and recovers the same persisted capture.
  /// Calls belong to the host's single receiver and store ownership scope.
  let persist options (body: byte array) = async {
    match validateOptions options |> Result.bind (fun () -> CaptureCodec.decode body) with
    | Result.Error reason -> return Result.Error reason
    | Result.Ok capture ->
      try
        Directory.CreateDirectory options.StoreDirectory |> ignore
        let receipt = { Digest = digest body; PayloadBytes = int64 body.Length; Stage = Capture.Stage }
        let payloadPath = Path.Combine(options.StoreDirectory, receipt.Digest + ".bare")
        let descriptorPath = Path.Combine(options.StoreDirectory, receipt.Digest + ".json")
        let json = descriptor capture receipt
        match checkQuota options payloadPath descriptorPath receipt.PayloadBytes json.Length with
        | Result.Error reason -> return Result.Error reason
        | Result.Ok () ->
          if not (File.Exists payloadPath) then do! publishFile payloadPath body
          let! checkedPayload = validateStoredPayload payloadPath receipt.Digest receipt.PayloadBytes
          match checkedPayload with
          | Result.Error reason -> return Result.Error reason
          | Result.Ok () ->
            if not (File.Exists descriptorPath) then do! publishFile descriptorPath json
            let! checkedDescriptor = readStoredFile descriptorPath (int64 json.Length)
            match checkedDescriptor with
            | Result.Error reason -> return Result.Error reason
            | Result.Ok storedDescriptor ->
              if storedDescriptor <> json then
                return Result.Error "The diagnostic descriptor disagrees with its verified capture."
              else return Result.Ok receipt
      with error ->
        return Result.Error ("Diagnostic persistence failed: " + error.Message)
  }

  let private serve options (pipe: NamedPipeServerStream) = async {
    use deadline = new CancellationTokenSource(options.IoTimeoutMilliseconds)
    let! request = Transport.read pipe deadline.Token
    let! result =
      match request with
      | Result.Error reason -> async.Return (Result.Error reason)
      // Once all bytes arrive, ownership passes to this process. Client
      // disconnection and its observation deadline do not cancel persistence.
      | Result.Ok body -> persist options body
    match result with
    | Result.Error reason -> Console.Error.WriteLine("Diagnostic capture refused: " + reason)
    | Result.Ok _ -> ()
    match CaptureCodec.encodeResponse result with
    | Result.Error reason -> Console.Error.WriteLine("Diagnostic reply encoding failed: " + reason)
    | Result.Ok response ->
      let! written = Transport.write pipe response deadline.Token
      match written with
      | Result.Error reason -> Console.Error.WriteLine reason
      | Result.Ok () -> ()
    return Result.isOk result
  }

  /// A single serial receiver bounds simultaneous decoding and file ownership.
  /// Invalid requests and open-but-idle connections do not renew the idle lease.
  let run options = async {
    match validateOptions options with
    | Result.Error reason -> return Result.Error reason
    | Result.Ok () ->
      try
        Directory.CreateDirectory options.StoreDirectory |> ignore
        // One sink owns this store even when another pipe name is configured.
        // The empty file may remain after exit; the open handle is the lock.
        use ownership = new FileStream(Path.Combine(options.StoreDirectory, "sink.lock"),
                                       FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
        let timer = Stopwatch.StartNew()
        let mutable lastCapture = 0L
        let mutable running = true
        let mutable announced = false
        while running do
          let remaining = int64 options.IdleSeconds * 1000L - (timer.ElapsedMilliseconds - lastCapture)
          if remaining <= 0L then running <- false
          else
            use pipe = new NamedPipeServerStream(options.PipeName, PipeDirection.InOut, 1,
                                                 PipeTransmissionMode.Byte,
                                                 PipeOptions.Asynchronous ||| PipeOptions.CurrentUserOnly)
            if not announced then
              Console.Out.WriteLine("DIAGNOSTIC_SINK_READY pipe=" + options.PipeName)
              Console.Out.Flush()
              announced <- true
            use idle = new CancellationTokenSource(TimeSpan.FromMilliseconds(float remaining))
            let! connected = async {
              try
                do! pipe.WaitForConnectionAsync(idle.Token) |> Async.AwaitTask
                return true
              with :? OperationCanceledException -> return false
            }
            if not connected then running <- false
            else
              let! persisted = serve options pipe
              if persisted then lastCapture <- timer.ElapsedMilliseconds
        return Result.Ok ()
      with error ->
        return Result.Error ("Diagnostic sink stopped: " + error.Message)
  }
