module Bozzetto.Composer.Program

open System
open System.IO
open System.IO.Pipes
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Bozzetto.Providers

let private jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
let private element value = JsonSerializer.SerializeToElement(value, jsonOptions)
let private empty = element {| |}

let private text name (value: JsonElement) =
  match if value.ValueKind = JsonValueKind.Object then value.TryGetProperty(name: string) else false, Unchecked.defaultof<JsonElement> with
  | true, property when property.ValueKind = JsonValueKind.String -> property.GetString() |> Option.ofObj |> Option.defaultValue ""
  | _ -> ""

let private authority (value: Authority) =
  {| host = value.Host; session = value.Session; provider = ProviderIdentity.name value.Provider
     epoch = value.Epoch; generation = value.Generation |}

let private response requestId (identity: Authority) outcome =
  match outcome with
  | Result.Ok payload ->
    element {| protocolVersion = 1; requestId = requestId; authority = authority identity
               success = true; result = payload; error = empty |}
  | Result.Error (error: Refusal) ->
    element {| protocolVersion = 1; requestId = requestId; authority = authority identity
               success = false; result = empty; error = element error |}

let private convert requestId map (reply: Reply<'a>) =
  response requestId reply.Authority (reply.Outcome |> Result.map map)

let private cacheRoot () =
  let root =
    match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" with
    | path when not (String.IsNullOrWhiteSpace path) && Path.IsPathFullyQualified path -> path
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
  Path.Combine(root, "bozzetto", "provider-sessions")

/// This worker owns compiler sessions. A public MCP host uses this same private
/// protocol; it does not obtain or reconstruct Composer tickets.
type Worker(root: string) =
  let gate = obj ()
  let host = Guid.NewGuid().ToString("N")
  let epoch = Guid.NewGuid().ToString("N")
  let sessions = Collections.Generic.Dictionary<string, ProviderSession>()
  let opening = Collections.Generic.Dictionary<string, Task<Result<unit, Refusal>>>()
  let mutable retired = false
  let hostIdentity =
    { Host = host; Session = ""; Epoch = epoch
      Provider = ProviderIdentity.ClefComposer; Generation = 0L }

  let reject id code message = response id hostIdentity (Result.Error { Code = code; Message = message })

  member _.RejectFrame(id, code, message) = reject id code message

  member private _.BeginRetirement() =
    lock gate (fun () ->
      retired <- true
      let owned = sessions.Values |> Seq.toArray
      // Revoke EVERY session before attempting any fallible cleanup.
      for session in owned do session.BeginClose() |> ignore
      owned, opening.Values |> Seq.toArray)

  member this.RetireAsync() = task {
    let owned, pendingOpens = this.BeginRetirement()
    let cleanup =
      owned |> Array.map (fun session -> task {
        try
          let! reply = session.CloseAsync()
          return reply.Outcome |> Result.mapError (fun error ->
            { error with Message = session.Identity.Session + ": " + error.Message })
        with error ->
          return Result.Error { Code = "cleanup_failed"; Message = session.Identity.Session + ": " + error.Message }
      })
    let! results = Task.WhenAll(Array.append cleanup pendingOpens)
    let failures = results |> Array.choose (function Result.Error error -> Some error.Message | _ -> None)
    if failures.Length = 0 then return Result.Ok ()
    else return Result.Error {
      Code = "cleanup_failed"
      Message = "All sessions are retired; cleanup failed: " + String.concat "; " failures }
  }

  member this.Handle(request: JsonElement) = task {
    let id = text "requestId" request
    let op = text "operation" request
    let mutable resolvedAuthority = None
    try
      let version =
        match request.TryGetProperty "protocolVersion" with
        | true, value when value.ValueKind = JsonValueKind.Number -> value.TryGetInt32()
        | _ -> false, 0
      if id = "" then return reject id "invalid_request" "requestId is required."
      elif version <> (true, 1) then return reject id "protocol_version" "This worker requires protocolVersion 1."
      elif op = "hello" then
        let compiler = typeof<Core.CompilationOrchestrator.ProjectSession>.Assembly
        let digest = File.ReadAllBytes compiler.Location |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
        return response id hostIdentity (Result.Ok(element {|
          compilerAssembly = compiler.Location; compilerSha256 = digest
          compilerVersion = compiler.GetName().Version.ToString()
          operations = [| "open"; "reserve"; "build"; "status"; "run"; "cancel"; "close"; "prepare_compiler_change" |]
          inMemoryPatchAllowed = false |}))
      elif text "host" request <> host || text "epoch" request <> epoch then
        return reject id "wrong_authority" "Use the host and compiler epoch returned by hello."
      elif text "provider" request <> "clef-composer" then
        return reject id "wrong_provider" "This worker only accepts the clef-composer provider; F# operations belong to FSI."
      elif op = "prepare_compiler_change" then
        let! result = this.RetireAsync()
        return response id hostIdentity (result |> Result.map (fun () ->
          element {| restartRequired = true; inMemoryPatchAllowed = false |}))
      elif op = "open" then
        let project = text "project" request
        if String.IsNullOrWhiteSpace project || not (Path.IsPathFullyQualified project) then
          return reject id "invalid_project" "An absolute .fidproj path is required."
        elif not (project.EndsWith(".fidproj", StringComparison.OrdinalIgnoreCase)) || not (File.Exists project) then
          return reject id "invalid_project" "Open requires an existing .fidproj file."
        elif Path.GetFullPath(root) = Path.GetDirectoryName(Path.GetFullPath project) || Path.GetFullPath(root).StartsWith(Path.GetDirectoryName(Path.GetFullPath project) + string Path.DirectorySeparatorChar, StringComparison.Ordinal) then
          return reject id "invalid_cache" "Provider scratch must reside outside the project directory."
        else
          let sessionId = Guid.NewGuid().ToString("N")
          let completion = TaskCompletionSource<Result<unit, Refusal>>(TaskCreationOptions.RunContinuationsAsynchronously)
          let admitted = lock gate (fun () ->
            if retired then false
            else
              opening.Add(sessionId, completion.Task)
              true)
          if not admitted then
            return reject id "compiler_retired" "Start a fresh worker before accepting compiler work."
          else
            let mutable cleanup = Result.Ok ()
            try
              try
                let directory = Path.Combine(root, host, sessionId, epoch)
                // Construction can touch disk; it must not hold the host gate.
                let backend = ComposerAdapter.create (Path.GetFullPath project) directory
                let session = new ProviderSession(host, sessionId, epoch, Path.GetFullPath project, backend)
                let installed = lock gate (fun () ->
                  if retired then false
                  else
                    sessions.Add(sessionId, session)
                    true)
                if installed then
                  return session.Status() |> convert id (fun status ->
                    element {| project = status.Project; manifestPath = status.ManifestPath |})
                else
                  session.BeginClose() |> ignore
                  let! closed = session.CloseAsync()
                  cleanup <- closed.Outcome
                  return reject id "compiler_retired" "Compiler retirement overtook this project open."
              with error ->
                cleanup <- Result.Error { Code = "cleanup_failed"; Message = "Opening " + sessionId + ": " + error.Message }
                return reject id "request_refused" error.Message
            finally
              completion.TrySetResult cleanup |> ignore
              // An overtaken open never enters sessions. Retain its failed
              // cleanup result so a later fence cannot forget that failure.
              match cleanup with
              | Result.Ok () -> lock gate (fun () -> opening.Remove sessionId |> ignore)
              | Result.Error _ -> ()
      else
        let found, isRetired =
          lock gate (fun () ->
            let found =
              match sessions.TryGetValue(text "session" request) with
              | true, session -> Some session
              | _ -> None
            found, retired)
        match found with
        | None -> return reject id "unknown_session" "The session is not owned by this worker."
        | Some session ->
          resolvedAuthority <- Some session.Identity
          if isRetired && Set.contains op (set [ "reserve"; "build"; "run"; "cancel" ]) then
            return response id session.Identity (Result.Error { Code = "closed"; Message = "The compiler worker is retired." })
          else
            match op with
            | "reserve" ->
              let! result = session.ReserveAsync(text "label" request)
              return result |> convert id (fun token -> element {| reservation = token |})
            | "build" ->
              let! result = session.BuildAsync(text "reservation" request, CancellationToken.None)
              return convert id element result
            | "run" ->
              let arguments =
                match request.TryGetProperty "arguments" with
                | true, value when value.ValueKind = JsonValueKind.Array ->
                  value.EnumerateArray()
                  |> Seq.map (fun arg ->
                    if arg.ValueKind <> JsonValueKind.String then invalidArg "arguments" "Run arguments must be strings."
                    arg.GetString() |> Option.ofObj |> Option.defaultValue "")
                  |> Seq.toList
                | false, _ -> []
                | _ -> invalidArg "arguments" "Run arguments must be an array of strings."
              let! result = session.RunAsync(arguments, CancellationToken.None)
              return convert id element result
            | "status" ->
              return session.Status() |> convert id (fun status ->
                element {| project = status.Project; manifestPath = status.ManifestPath
                           closed = status.Closed; busy = status.Busy
                           revocationPending = status.RevocationPending; backendError = (status.BackendError |> Option.toObj)
                           cleanupPending = status.CleanupPending; cleanupError = (status.CleanupError |> Option.toObj)
                           current = status.Current |> Option.map element |> Option.defaultValue (element (null: obj))
                           executionRequiresRevalidation = true |})
            | "cancel" -> return session.Cancel() |> convert id (fun () -> empty)
            | "close" ->
              session.BeginClose() |> ignore
              let closing = session.CloseAsync()
              if closing.IsCompleted then
                let! result = closing
                return result |> convert id (fun () -> element {| closed = true; cleanupPending = false |})
              else
                return session.Status() |> convert id (fun status ->
                  element {| closed = status.Closed; cleanupPending = status.CleanupPending; cleanupError = (status.CleanupError |> Option.toObj) |})
            | _ -> return response id session.Identity (Result.Error {
                Code = "unsupported_operation"; Message = "The operation is not supported by the Clef/Composer provider." })
    with error ->
      return response id (resolvedAuthority |> Option.defaultValue hostIdentity)
        (Result.Error { Code = "request_refused"; Message = error.Message })
  }

  interface IDisposable with
    // The owner explicitly awaits RetireAsync within its shutdown deadline.
    // Disposing this scope must not start a second unbounded cleanup retry.
    member this.Dispose() = this.BeginRetirement() |> ignore

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
      use worker = new Worker(cacheRoot ())
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
                Task.Run(Func<Task>(fun () -> (task {
                  try
                    let! response = worker.Handle request
                    emit response
                  finally
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
