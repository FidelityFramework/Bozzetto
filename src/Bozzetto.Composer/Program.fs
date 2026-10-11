module Bozzetto.Composer.Program

open System
open System.IO
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Bozzetto.Composer.Protocol
open Bozzetto.Composer.WorkerProtocol

let private cacheRoot () =
  let root =
    match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" |> Option.ofObj with
    | Some path when not (String.IsNullOrWhiteSpace path) && Path.IsPathFullyQualified path -> path
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
  Path.Combine(root, "bozzetto", "provider-sessions")

let private describeCompiler () : CompilerIdentity =
  let compiler = typeof<Core.CompilationOrchestrator.ProjectSession>.Assembly
  { AssemblyPath = compiler.Location
    Sha256 = File.ReadAllBytes compiler.Location |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
    Version = compiler.GetName().Version.ToString() }

[<EntryPoint>]
let main argv =
  if argv = [| "--help" |] then
    printfn "Composer workspace host (Bozzetto.Composer): --socket ABSOLUTE_UNIX_SOCKET_PATH"
    printfn "Explicit binary protocol2/BAREWire agreement required; stdout/stderr are diagnostics only."
    0
  else
    try
      let path =
        match argv with
        | [| "--socket"; path |] when Path.IsPathFullyQualified path -> path
        | _ -> invalidArg "arguments" "Use --socket ABSOLUTE_UNIX_SOCKET_PATH. No other transport is supported."
      use socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
      socket.ConnectAsync(UnixDomainSocketEndPoint path).WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
      use stream = new NetworkStream(socket, ownsSocket = false)
      let requestCancellation = Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>()
      let cancelRequest requestId =
        match requestCancellation.TryGetValue requestId with
        | true, source ->
          try source.Cancel(); true
          with :? ObjectDisposedException -> false
        | false, _ -> false
      let tools = global.Composer.Hosting.ManagedTools.create()
      use worker = new Worker<Core.IncrementalBuild.Ticket>(cacheRoot (), ComposerAdapter.create tools, describeCompiler, cancelRequest = cancelRequest)
      use writes = new SemaphoreSlim(1, 1)
      let running = Collections.Concurrent.ConcurrentDictionary<string, Task>()
      let mutable cleanupFailed = false
      let emit (reply: Reply) = task {
        // Encode before writer admission; exactly one complete frame owns output.
        let bytes =
          match StreamFrames.encodeReply reply with
          | Result.Ok bytes -> bytes
          | Result.Error error ->
            let failure: Refusal = {
              Code = (match error with CodecFailure.FrameTooLarge -> RefusalCode.FrameTooLarge | _ -> RefusalCode.MalformedPayload)
              Message = sprintf "The reply has no admitted binary representation: %A" error }
            let refusal = { reply with Outcome = Result.Error failure }
            match StreamFrames.encodeReply refusal with
            | Result.Ok bytes -> bytes
            | Result.Error failure -> raise (InvalidDataException(sprintf "Cannot encode reply refusal: %A" failure))
        do! writes.WaitAsync()
        try
          do! stream.WriteAsync(bytes.AsMemory(), CancellationToken.None)
          do! stream.FlushAsync CancellationToken.None
        finally writes.Release() |> ignore
      }
      try
        let mutable reading = true
        while reading do
          match (StreamFrames.readRequestAsync stream CancellationToken.None).GetAwaiter().GetResult() with
          | Result.Error failure -> raise (InvalidDataException(sprintf "Binary request refused: %A" failure))
          | Result.Ok None -> reading <- false
          | Result.Ok(Some request) ->
            let completion = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            if not (running.TryAdd(request.RequestId, completion.Task)) then
              (emit (worker.RejectFrame(request.RequestId, RefusalCode.DuplicateRequest, "This requestId is already in flight."))).GetAwaiter().GetResult()
            else
              // Install cancellation before cold scheduling; a following frame
              // can cancel even work whose evaluator has not started.
              let cancellation = new CancellationTokenSource()
              requestCancellation[request.RequestId] <- cancellation
              Task.Run(Func<Task>(fun () -> task {
                try
                  try
                    let! reply = worker.Handle(request, cancellation = cancellation.Token)
                    do! emit reply
                  with error ->
                    eprintfn "Composer request failed: %s" error.Message
                    cleanupFailed <- true
                    // A partially written frame has no recoverable interpretation.
                    try socket.Shutdown(SocketShutdown.Both) with _ -> ()
                finally
                  requestCancellation.TryRemove request.RequestId |> ignore
                  cancellation.Dispose()
                  running.TryRemove request.RequestId |> ignore
                  completion.SetResult()
              })) |> ignore
      finally
        // Seal actual native launches before retiring compiler sessions. Native
        // tools that do not observe compiler cancellation still have this owner.
        tools.Seal()
        let nativeRetirement = Async.StartAsTask(tools.RetireAsync(), cancellationToken = CancellationToken.None)
        let retirement = Task.Run(Func<Task>(fun () -> task {
          let! result = worker.RetireAsync()
          match result with
          | Result.Ok () -> ()
          | Result.Error error -> cleanupFailed <- true; eprintfn "%s: %s" error.Code error.Message
        }))
        let pending = Task.WhenAll(Array.append [| retirement; nativeRetirement :> Task |] (running.Values |> Seq.toArray))
        let joined = pending.Wait(TimeSpan.FromSeconds 5.)
        // RetireAsync completes only after native process exit and redirected
        // I/O join, including on failure. A deadline cannot authorize orphaning
        // those children when the daemon is no longer present to supervise us.
        match nativeRetirement.GetAwaiter().GetResult() with
        | Result.Ok () -> ()
        | Result.Error error -> cleanupFailed <- true; eprintfn "Native tool cleanup failed: %s" error
        if not joined then
          eprintfn "Provider socket shutdown exceeded five seconds; native children joined, terminating the managed worker."
          Environment.Exit 1
      if cleanupFailed then 1 else 0
    with error ->
      eprintfn "Composer worker refused: %s" error.Message
      1
