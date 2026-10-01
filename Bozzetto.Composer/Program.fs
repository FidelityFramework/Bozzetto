module Bozzetto.Composer.Program

open System
open System.IO
open System.IO.Pipes
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Bozzetto.Composer.WorkerProtocol

let private cacheRoot () =
  let root =
    match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" |> Option.ofObj with
    | Some path when not (String.IsNullOrWhiteSpace path) && Path.IsPathFullyQualified path -> path
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
  Path.Combine(root, "bozzetto", "provider-sessions")

let private describeCompiler () =
  let compiler = typeof<Core.CompilationOrchestrator.ProjectSession>.Assembly
  { AssemblyPath = compiler.Location
    Sha256 = File.ReadAllBytes compiler.Location |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
    Version = compiler.GetName().Version.ToString() }

let private readLine (reader: TextReader) =
  let line = StringBuilder()
  let mutable finished = false
  let mutable eof = false
  while not finished do
    let next = reader.Read()
    if next = -1 then
      eof <- true
      finished <- true
    elif next = int '\n' then finished <- true
    elif line.Length >= 1024 * 1024 then raise (InvalidDataException "Provider request exceeds 1 MiB.")
    else line.Append(char next) |> ignore
  if eof && line.Length = 0 then None else Some(line.ToString())

[<EntryPoint>]
let main argv =
  if argv = [| "--help" |] then
    printfn "Bozzetto Composer worker: --stdio or --read-handle HANDLE --write-handle HANDLE"
    printfn "Versioned JSON lines. Start with {\"protocolVersion\":1,\"requestId\":\"hello\",\"operation\":\"hello\"}."
    0
  else
    try
      use input: Stream =
        match argv with
        | [| "--stdio" |] -> Console.OpenStandardInput()
        | [| "--read-handle"; readHandle; "--write-handle"; _ |] -> new AnonymousPipeClientStream(PipeDirection.In, readHandle)
        | _ -> invalidArg "arguments" "Use --stdio or --read-handle HANDLE --write-handle HANDLE."
      use output: Stream =
        match argv with
        | [| "--stdio" |] -> Console.OpenStandardOutput()
        | [| "--read-handle"; _; "--write-handle"; writeHandle |] -> new AnonymousPipeClientStream(PipeDirection.Out, writeHandle)
        | _ -> failwith "Invalid transport arguments"
      use reader = new StreamReader(input, Encoding.UTF8)
      use writer = new StreamWriter(output, UTF8Encoding(false), AutoFlush = true)
      Console.SetOut(Console.Error)
      let requestCancellation = Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>()
      let cancelRequest requestId =
        match requestCancellation.TryGetValue requestId with
        | true, source ->
          try source.Cancel(); true
          with :? ObjectDisposedException -> false
        | false, _ -> false
      use worker = new Worker<Core.IncrementalBuild.Ticket>(cacheRoot (), ComposerAdapter.create, describeCompiler, cancelRequest = cancelRequest)
      let writes = obj ()
      let running = Collections.Concurrent.ConcurrentDictionary<string, Task>()
      let emit value = lock writes (fun () -> writer.WriteLine(JsonSerializer.Serialize(value, jsonOptions)))
      let mutable reading = true
      let mutable cleanupFailed = false
      try
        while reading do
          match readLine reader with
          | None -> reading <- false
          | Some line ->
            try
              use doc = JsonDocument.Parse line
              let request = doc.RootElement.Clone()
              let requestId = text "requestId" request
              let completion = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
              if not (running.TryAdd(requestId, completion.Task)) then
                emit (worker.RejectFrame(requestId, "duplicate_request", "This requestId is already in flight."))
              else
                // The input reader installs cancellation before dispatch, so
                // even a queued build can be canceled by the next wire frame.
                let cancellation = new CancellationTokenSource()
                requestCancellation[requestId] <- cancellation
                Task.Run(Func<Task>(fun () -> (task {
                  try
                    let! response = worker.Handle(request, cancellation = cancellation.Token)
                    emit response
                  finally
                    // Remove the old token before permitting request-id reuse.
                    requestCancellation.TryRemove requestId |> ignore
                    cancellation.Dispose()
                    running.TryRemove requestId |> ignore
                    completion.SetResult()
                } :> Task))) |> ignore
            with :? JsonException as error ->
              emit (worker.RejectFrame("", "invalid_json", error.Message))
      finally
        // Include retirement (lock acquisition and disposal) in the deadline.
        let retirement = Task.Run(Func<Task>(fun () -> (task {
          let! result = worker.RetireAsync()
          match result with
          | Result.Ok () -> ()
          | Result.Error error ->
            cleanupFailed <- true
            eprintfn "%s: %s" error.Code error.Message
        } :> Task)))
        let pending = Task.WhenAll(Array.append [| retirement |] (running.Values |> Seq.toArray))
        if not (pending.Wait(TimeSpan.FromSeconds 5.0)) then
          eprintfn "Provider shutdown exceeded five seconds; terminating its owned process tree."
          Diagnostics.Process.GetCurrentProcess().Kill(true)
      if cleanupFailed then 1 else 0
    with error ->
      eprintfn "Composer worker refused: %s" error.Message
      1
