namespace Bozzetto.Diagnostics

open System
open System.Globalization
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Threading

module Program =
  let private usage =
    "Bozzetto.Diagnostics --pipe NAME --store ABSOLUTE_DIRECTORY --idle-seconds 1..86400 "
    + "[--io-timeout-ms 100..60000] [--max-store-bytes BYTES] [--max-captures COUNT]"
    + Environment.NewLine
    + "Bozzetto.Diagnostics submit --pipe NAME --capture ABSOLUTE_FILE --timeout-ms 100..60000"

  let private collectArguments keys (arguments: string array) =
    let rec collect values remaining =
      match remaining with
      | [] -> Result.Ok values
      | key :: value :: rest when List.contains key keys ->
        if Map.containsKey key values then Result.Error ("Duplicate argument: " + key)
        else collect (Map.add key value values) rest
      | argument :: _ -> Result.Error ("Unknown argument or missing value: " + argument)
    collect Map.empty (Array.toList arguments)

  let private integer key fallback (values: Map<string, string>) =
    match Map.tryFind key values with
    | None -> fallback
    | Some value ->
      match Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture) with
      | true, number -> Result.Ok number
      | false, _ -> Result.Error (key + " requires an unsigned decimal integer.")

  let private narrow key value =
    if value > int64 Int32.MaxValue then Result.Error (key + " exceeds its permitted integer range.")
    else Result.Ok (int value)

  let parseArguments (arguments: string array) =
    match collectArguments [ "--pipe"; "--store"; "--idle-seconds"; "--io-timeout-ms"; "--max-store-bytes"; "--max-captures" ] arguments with
    | Result.Error reason -> Result.Error reason
    | Result.Ok values ->
      match Map.tryFind "--pipe" values, Map.tryFind "--store" values with
      | Some pipe, Some store ->
        integer "--idle-seconds" (Result.Error "--idle-seconds is required.") values
        |> Result.bind (narrow "--idle-seconds")
        |> Result.bind (fun idle ->
          integer "--io-timeout-ms" (Result.Ok 10000L) values
          |> Result.bind (narrow "--io-timeout-ms")
          |> Result.bind (fun timeout ->
            integer "--max-store-bytes" (Result.Ok 268435456L) values
            |> Result.bind (fun bytes ->
              integer "--max-captures" (Result.Ok 128L) values
              |> Result.bind (narrow "--max-captures")
              |> Result.bind (fun captures ->
                let options = {
                  PipeName = pipe; StoreDirectory = store; IdleSeconds = idle
                  IoTimeoutMilliseconds = timeout; MaxStoreBytes = bytes; MaxCaptures = captures
                }
                ManagedHost.validateOptions options |> Result.map (fun () -> options)))))
      | _ -> Result.Error "--pipe and --store are required."

  let private submit pipeName capturePath (timeoutMilliseconds: int) = async {
    try
      let timer = Stopwatch.StartNew()
      use deadline = new CancellationTokenSource(timeoutMilliseconds)
      use input = new FileStream(capturePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                 65536, FileOptions.Asynchronous)
      if input.Length <= 0L || input.Length > int64 CaptureCodec.MaximumBody then
        return Result.Error "The capture file is outside the diagnostic frame bound."
      else
        let body = Array.zeroCreate<byte> (int input.Length)
        do! input.ReadExactlyAsync(body.AsMemory(), deadline.Token).AsTask() |> Async.AwaitTask
        let remaining = int64 timeoutMilliseconds - timer.ElapsedMilliseconds
        if remaining <= 0L then return Result.Error "The diagnostic submit deadline expired while reading its capture."
        else
          let! response = Client.exchange pipeName (int remaining) body
          return
            response
            |> Result.bind CaptureCodec.decodeResponse
            |> Result.bind id
            |> Result.bind (fun receipt ->
              if receipt.Digest <> Convert.ToHexString(SHA256.HashData body)
                 || receipt.PayloadBytes <> int64 body.Length || receipt.Stage <> Capture.Stage then
                Result.Error "The persisted receipt does not identify the submitted capture."
              else Result.Ok (Some ("Diagnostic capture persisted digest=" + receipt.Digest)))
    with error -> return Result.Error ("Diagnostic submit failed: " + error.Message)
  }

  let private submission arguments =
    collectArguments [ "--pipe"; "--capture"; "--timeout-ms" ] arguments
    |> Result.bind (fun values ->
      match Map.tryFind "--pipe" values, Map.tryFind "--capture" values with
      | Some pipe, Some capture when not (String.IsNullOrWhiteSpace pipe) && Path.IsPathFullyQualified capture ->
        integer "--timeout-ms" (Result.Error "--timeout-ms is required.") values
        |> Result.bind (narrow "--timeout-ms")
        |> Result.bind (fun timeout ->
          if timeout < 100 || timeout > 60000 then Result.Error "--timeout-ms must be between 100 and 60000."
          else Result.Ok (submit pipe capture timeout))
      | _ -> Result.Error "submit requires --pipe and an absolute --capture file path.")

  let run arguments =
    if arguments = [| "--help" |] then
      Console.Out.WriteLine usage
      0
    else
      let command =
        match Array.toList arguments with
        | "submit" :: rest -> submission (Array.ofList rest)
        | _ ->
          parseArguments arguments
          |> Result.map (fun options -> async {
            let! result = ManagedHost.run options
            return result |> Result.map (fun () -> None)
          })
      match command with
      | Result.Error reason ->
        Console.Error.WriteLine reason
        Console.Error.WriteLine usage
        2
      | Result.Ok workflow ->
        match workflow |> Async.RunSynchronously with
        | Result.Ok output ->
          output |> Option.iter Console.Out.WriteLine
          0
        | Result.Error reason ->
          Console.Error.WriteLine reason
          1

  [<EntryPoint>]
  let main arguments = run arguments
