module Bozzetto.Composer.Tests.NativeProviderTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip

let private required name =
  match Environment.GetEnvironmentVariable name with
  | value when not (String.IsNullOrWhiteSpace value) -> value
  | _ -> failtestf "%s must be configured; native acceptance must not silently skip" name

let private evidenceRoot () =
  let root =
    match Environment.GetEnvironmentVariable "BOZZETTO_COMPOSER_EVIDENCE" with
    | value when not (String.IsNullOrWhiteSpace value) -> value
    | _ ->
      let cache =
        match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" with
        | value when not (String.IsNullOrWhiteSpace value) && Path.IsPathFullyQualified value -> value
        | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
      Path.Combine(cache, "bozzetto", "native-provider-tests")
  if not (Path.IsPathFullyQualified root) then failtest "Evidence directory must be absolute"
  let directory = Path.Combine(root, Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory directory |> ignore
  printfn "Native provider evidence: %s" directory
  directory

let private field name (value: JsonElement) = value.GetProperty(name: string)
let private stringField name value = (field name value).GetString()
let private succeeded response =
  (field "success" response).GetBoolean() |> Expect.isTrue ("request succeeded: " + response.GetRawText())
  field "result" response
let private refused code response =
  (field "success" response).GetBoolean() |> Expect.isFalse ("request refused: " + response.GetRawText())
  stringField "code" (field "error" response) |> Expect.equal "refusal classification" code

let private stringArray name value =
  (field name value).EnumerateArray() |> Seq.map (fun item -> item.GetString()) |> Seq.toList

/// Independent read/drain loops prevent compiler logging from blocking protocol
/// responses. Each request has its own correlation slot and bounded lifetime.
type private Client(directory: string, name: string) =
  let wireGate = obj ()
  let writeGate = obj ()
  let pending = ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>()
  let wire = new StreamWriter(Path.Combine(directory, name + "-wire.jsonl"), false, UTF8Encoding(false), AutoFlush = true)
  let stderr = new StreamWriter(Path.Combine(directory, name + "-stderr.log"), false, UTF8Encoding(false), AutoFlush = true)
  let mutable sequence = 0
  let mutable host = ""
  let mutable epoch = ""
  let info = ProcessStartInfo()
  do
    let worker = required "BOZZETTO_COMPOSER_WORKER"
    if not (Path.IsPathFullyQualified worker && File.Exists worker) then failtest "Worker must be an existing absolute path"
    if worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) then
      info.FileName <-
        match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
        | value when not (String.IsNullOrWhiteSpace value) -> value
        | _ -> "dotnet"
      info.ArgumentList.Add worker
    else info.FileName <- worker
    info.ArgumentList.Add "--stdio"
    info.UseShellExecute <- false
    info.RedirectStandardInput <- true
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    info.WorkingDirectory <- directory
  let workerProcess = Process.Start info
  let record direction (line: string) =
    lock wireGate (fun () ->
      wire.WriteLine(JsonSerializer.Serialize {| direction = direction; at = DateTimeOffset.UtcNow; payload = line |}))
  let failPending (error: exn) =
    for KeyValue(_, completion) in pending do completion.TrySetException error |> ignore
  let output = Task.Run(Func<Task>(fun () -> task {
    try
      let mutable reading = true
      while reading do
        let! line = workerProcess.StandardOutput.ReadLineAsync()
        if isNull line then
          reading <- false
          failPending (EndOfStreamException "Provider closed stdout")
        else
          record "response" line
          use document = JsonDocument.Parse line
          let response = document.RootElement.Clone()
          match pending.TryRemove(stringField "requestId" response) with
          | true, completion -> completion.TrySetResult response |> ignore
          | _ -> failPending (InvalidDataException("Uncorrelated response: " + line))
    with error -> failPending error
  }))
  let errors = Task.Run(Func<Task>(fun () -> task {
    let mutable reading = true
    while reading do
      let! line = workerProcess.StandardError.ReadLineAsync()
      if isNull line then reading <- false
      else do! stderr.WriteLineAsync line
  }))

  member _.Host = host
  member _.Epoch = epoch
  /// Test-only prediction used by a single-threaded self-cancellation probe.
  member _.NextRequestId = string (Volatile.Read(&sequence) + 1)
  member _.Call(operation: string, session: string, extra: (string * obj) list) =
    let id = string (Interlocked.Increment &sequence)
    let request = Dictionary<string, obj>()
    for key, value in [ "protocolVersion", box 1; "requestId", box id; "operation", box operation
                        "host", box host; "epoch", box epoch; "provider", box "clef-composer"; "session", box session ] @ extra do
      request[key] <- value
    let completion = TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously)
    pending[id] <- completion
    try
      let line = JsonSerializer.Serialize request
      record "request" line
      lock writeGate (fun () -> workerProcess.StandardInput.WriteLine line; workerProcess.StandardInput.Flush())
      let timeout = if operation = "build" then TimeSpan.FromMinutes 8. else TimeSpan.FromSeconds 30.
      completion.Task.WaitAsync(timeout).GetAwaiter().GetResult()
    finally
      pending.TryRemove id |> ignore

  member this.Hello() =
    let response = this.Call("hello", "", [])
    succeeded response |> ignore
    let authority = field "authority" response
    host <- stringField "host" authority
    epoch <- stringField "epoch" authority
    response

  interface IDisposable with
    member _.Dispose() =
      try
        workerProcess.StandardInput.Close()
        if not (workerProcess.WaitForExit 5000) then
          workerProcess.Kill true
          if not (workerProcess.WaitForExit 5000) then failtest "Owned provider process did not terminate after kill"
        if not (Task.WhenAll(output, errors).Wait 5000) then failtest "Provider log drains did not terminate"
      finally
        wire.Dispose()
        stderr.Dispose()
        workerProcess.Dispose()

type internal Fixture = { Project: string; Source: string; Dependency: string; OriginalSource: string }

let internal fixture directory =
  let template = required "BOZZETTO_COMPOSER_FIXTURE"
  if not (Path.IsPathFullyQualified template && File.Exists template) then failtest "Fixture must be an existing absolute .fidproj"
  let sourceDirectory = Path.GetDirectoryName template
  let copyDirectory = Path.Combine(directory, "fixture")
  Directory.CreateDirectory copyDirectory |> ignore
  for file in Directory.EnumerateFiles sourceDirectory do
    if Path.GetExtension file = ".clef" || Path.GetExtension file = ".fidproj" then
      File.Copy(file, Path.Combine(copyDirectory, Path.GetFileName file))
  let project = Path.Combine(copyDirectory, Path.GetFileName template)
  let originalProject = File.ReadAllText project
  let platform = Regex.Match(originalProject, "platform\\s*=\\s*\\{\\s*path\\s*=\\s*\"([^\"]+)\"")
  if not platform.Success then failtest "Fixture must declare its platform dependency explicitly"
  let originalDependency = platform.Groups[1].Value
  if not (Path.IsPathFullyQualified originalDependency) then failtest "Fixture platform dependency must remain absolute"
  let dependency = Path.Combine(copyDirectory, "Fidelity.Platform.fidproj")
  let dependencyText = File.ReadAllText originalDependency
  let absoluteDependencies =
    Regex.Replace(dependencyText, "path\\s*=\\s*\"([^\"]+)\"", MatchEvaluator(fun matched ->
      let path = Path.GetFullPath(matched.Groups[1].Value, Path.GetDirectoryName originalDependency)
      "path = \"" + path + "\""))
  File.WriteAllText(dependency, absoluteDependencies)
  File.WriteAllText(project, originalProject.Replace(originalDependency, dependency))
  let source = Path.Combine(copyDirectory, "IncrementalScalarRegions.clef")
  let originalSource = File.ReadAllText source
  originalSource.Contains "let changeable () = false" |> Expect.isTrue "known scalar-region fixture"
  { Project = project; Source = source; Dependency = dependency; OriginalSource = originalSource }

let private openProject (client: Client) project =
  let response = client.Call("open", "", [ "project", box project ])
  succeeded response |> ignore
  stringField "session" (field "authority" response)

let private reserve (client: Client) session label =
  client.Call("reserve", session, [ "label", box label ]) |> succeeded |> stringField "reservation"

let private buildReserved (client: Client) session token =
  client.Call("build", session, [ "reservation", box token ])

let private build client session label = buildReserved client session (reserve client session label) |> succeeded

let private run (client: Client) session expected =
  let result = client.Call("run", session, []) |> succeeded
  (field "exitCode" result).GetInt32() |> Expect.equal "native exit code" 0
  stringField "standardOutput" result |> Expect.equal "native stdout" expected

let private noCurrent (client: Client) session =
  let status = client.Call("status", session, []) |> succeeded
  (field "current" status).ValueKind |> Expect.equal "refusal withdrew accepted metadata" JsonValueKind.Null

let private expectAuthority (client: Client) session generation response =
  let identity = field "authority" response
  stringField "host" identity |> Expect.equal "response preserves owning host" client.Host
  stringField "session" identity |> Expect.equal "response preserves owning session" session
  stringField "provider" identity |> Expect.equal "response preserves provider" "clef-composer"
  stringField "epoch" identity |> Expect.equal "response preserves compiler epoch" client.Epoch
  (field "generation" identity).GetInt64() |> Expect.equal "response preserves operation revision" generation

let private leaseHeld path =
  try
    use lease = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
    false
  with :? IOException -> true

let private expectCleanupFailureStatus (client: Client) session =
  let status = client.Call("status", session, []) |> succeeded
  (field "closed" status).GetBoolean() |> Expect.isTrue "cleanup failure leaves session logically retired"
  (field "current" status).ValueKind |> Expect.equal "retired session has no accepted artifact" JsonValueKind.Null
  (field "cleanupPending" status).GetBoolean() |> Expect.isTrue "failed cleanup is never reported complete"
  (field "cleanupError" status).ValueKind |> Expect.equal "original cleanup failure remains visible" JsonValueKind.String
  stringField "cleanupError" status |> String.IsNullOrWhiteSpace |> Expect.isFalse "cleanup failure has an explanation"
  status

let private objects directory label accepted =
  let manifestPath = stringField "objectManifest" accepted
  let content = File.ReadAllText manifestPath
  File.WriteAllText(Path.Combine(directory, label + "-objects.json"), content)
  use document = JsonDocument.Parse content
  field "linkedObjects" document.RootElement
  |> fun rows -> rows.EnumerateArray()
  |> Seq.map (fun row ->
    let identity = stringField "Identity" row
    let path = stringField "ObjectPath" row
    let expected = stringField "ObjectSha256" row
    let actual = File.ReadAllBytes path |> SHA256.HashData |> Convert.ToHexString
    String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) |> Expect.isTrue ("actual object digest: " + identity)
    identity, (path, actual))
  |> Map.ofSeq

[<Tests>]
let tests =
  testSequenced <| testList "Composer native provider process" [
    testCase "wire authority rejects wrong provider, host, epoch and cross-session reservations" <| fun _ ->
      let directory = evidenceRoot ()
      let fixture = fixture directory
      use client = new Client(directory, "authority")
      let hello = client.Hello() |> succeeded
      stringArray "operations" hello |> List.contains "cancel_request" |> Expect.isTrue "private request cancellation is advertised"
      client.Call("cancel_request", "", []) |> refused "invalid_request"
      client.Call("cancel_request", "", [ "targetRequestId", box "missing"; "host", box "foreign-host" ]) |> refused "wrong_authority"
      client.Call("cancel_request", "", [ "targetRequestId", box "missing"; "provider", box "fsharp" ]) |> refused "wrong_provider"
      let selfTarget = client.NextRequestId
      let canceled = client.Call("cancel_request", "", [ "targetRequestId", box selfTarget ])
      expectAuthority client "" 0L canceled
      (canceled |> succeeded |> field "cancellationRequested").GetBoolean()
      |> Expect.isTrue "request cancellation is installed before the handler dispatches"
      let first = openProject client fixture.Project
      let second = openProject client fixture.Project
      client.Call("status", first, [ "provider", box "fsharp" ]) |> refused "wrong_provider"
      client.Call("prepare_compiler_change", "", [ "provider", box "fsharp" ]) |> refused "wrong_provider"
      client.Call("status", first, [ "host", box "foreign-host" ]) |> refused "wrong_authority"
      client.Call("status", first, [ "epoch", box "foreign-epoch" ]) |> refused "wrong_authority"
      client.Call("eval", first, []) |> refused "unsupported_operation"
      let token = reserve client first "first edit"
      let missing = client.Call("cancel_request", "", [ "targetRequestId", box "no-such-request" ]) |> succeeded
      (field "cancellationRequested" missing).GetBoolean() |> Expect.isFalse "missing target is an idempotent no-op"
      client.Call("status", first, []) |> expectAuthority client first 1L
      buildReserved client second token |> refused "invalid_reservation"
      let queries =
        [| Task.Run(fun () -> client.Call("status", first, []))
           Task.Run(fun () -> client.Call("status", second, [])) |]
      let statuses = Task.WhenAll(queries).WaitAsync(TimeSpan.FromSeconds 35.).GetAwaiter().GetResult()
      for index in 0 .. statuses.Length - 1 do
        succeeded statuses[index] |> ignore
        stringField "session" (field "authority" statuses[index])
        |> Expect.equal "concurrent replies correlate to their own request" ([| first; second |][index])
      client.Call("close", first, []) |> succeeded |> ignore
      client.Call("run", first, []) |> refused "closed"
      client.Call("reserve", second, [ "label", box "surviving session" ]) |> succeeded |> ignore
  
    testCase "pre-patch fence retires the process epoch and requires a fresh worker" <| fun _ ->
      let directory = evidenceRoot ()
      let fixture = fixture directory
      use oldWorker = new Client(directory, "retired")
      let hello = oldWorker.Hello() |> succeeded
      (field "inMemoryPatchAllowed" hello).GetBoolean() |> Expect.isFalse "in-memory patching is unsupported"
      let session = openProject oldWorker fixture.Project
      oldWorker.Call("prepare_compiler_change", "", []) |> succeeded |> ignore
      oldWorker.Call("reserve", session, [ "label", box "late edit" ]) |> refused "closed"
      oldWorker.Call("run", session, []) |> refused "closed"
      oldWorker.Call("open", "", [ "project", box fixture.Project ]) |> refused "compiler_retired"
      use fresh = new Client(directory, "fresh")
      fresh.Hello() |> ignore
      fresh.Host |> Expect.notEqual "fresh process owns distinct host" oldWorker.Host
      fresh.Epoch |> Expect.notEqual "fresh process owns distinct compiler epoch" oldWorker.Epoch
      let freshSession = openProject fresh fixture.Project
      fresh.Call("status", freshSession, [ "epoch", box oldWorker.Epoch ]) |> refused "wrong_authority"

    testCase "native retirement withdraws every session and releases leases despite failed status persistence" <| fun _ ->
      let directory = evidenceRoot ()
      let fixture = fixture directory
      use client = new Client(directory, "retirement-failure")
      client.Hello() |> ignore
      let first = openProject client fixture.Project
      let second = openProject client fixture.Project
      let firstToken = reserve client first "reserved before persistence failure"
      let firstStatus = client.Call("status", first, []) |> succeeded
      let manifest = stringField "manifestPath" firstStatus
      let leasePath = Path.Combine(Path.GetDirectoryName manifest, ".session.lock")
      leaseHeld leasePath |> Expect.isTrue "first session owns its exclusive directory lease before failure"
      build client second "accepted surviving project" |> ignore
      run client second "stable\nbefore\n"

      // Argument validation fails after session resolution, so its refusal must
      // retain the accepted session's complete authority rather than host-only identity.
      let malformed = client.Call("run", second, [ "arguments", box [| 1 |] ])
      malformed |> refused "request_refused"
      expectAuthority client second 1L malformed
      run client second "stable\nbefore\n"

      File.Delete manifest
      Directory.CreateDirectory manifest |> ignore
      try
        // Composer advances its reservation before persisting status. The
        // exception must neither erase response identity nor restore old authority.
        let failedReserve = client.Call("reserve", first, [ "label", box "injected persistence failure" ])
        failedReserve |> refused "backend_failed"
        expectAuthority client first 2L failedReserve
        noCurrent client first
        buildReserved client first firstToken |> refused "invalid_reservation"
        run client second "stable\nbefore\n"

        let retirement = client.Call("prepare_compiler_change", "", [])
        retirement |> refused "cleanup_failed"
        stringField "message" (field "error" retirement) |> fun message ->
          message.Contains first |> Expect.isTrue "aggregate cleanup error identifies its session"
        expectCleanupFailureStatus client first |> ignore
        for owned in [ first; second ] do
          let statusReply = client.Call("status", owned, [])
          let status = succeeded statusReply
          (field "closed" status).GetBoolean() |> Expect.isTrue "every session retires even when one disposal fails"
          (field "current" status).ValueKind |> Expect.equal "retirement withdraws every cached artifact" JsonValueKind.Null
          let revision = (field "generation" (field "authority" statusReply)).GetInt64()
          let refusedRun = client.Call("run", owned, [])
          refusedRun |> refused "closed"
          expectAuthority client owned revision refusedRun
          client.Call("reserve", owned, [ "label", box "late edit" ]) |> refused "closed"
          buildReserved client owned firstToken |> refused "closed"
        client.Call("open", "", [ "project", box fixture.Project ]) |> refused "compiler_retired"

        // Acquire the actual exclusive OS-backed FileStream lease; adapter
        // Closed=true alone cannot establish resource release.
        use released = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
        File.WriteAllText(Path.Combine(directory, "lease-release.json"),
          JsonSerializer.Serialize {| session = first; leasePath = leasePath; heldBeforeFailure = true; acquiredAfterFailedDisposal = true |})
      finally
        if Directory.Exists manifest then Directory.Delete manifest

      // Composer deliberately retains its first disposal failure rather than
      // writing into a directory which a replacement may now own. Repairing the
      // path must not turn repeated disposal into a false success.
      let closing = client.Call("close", first, [])
      if (field "success" closing).GetBoolean() then
        let status = succeeded closing
        (field "closed" status).GetBoolean() |> Expect.isTrue "close acknowledges logical retirement"
        (field "cleanupPending" status).GetBoolean() |> Expect.isTrue "asynchronous close does not promise successful cleanup"
        (field "cleanupError" status).ValueKind |> Expect.equal "pending close retains its existing error" JsonValueKind.String
      else closing |> refused "cleanup_failed"
      client.Call("prepare_compiler_change", "", []) |> refused "cleanup_failed"
      expectCleanupFailureStatus client first |> ignore
      let secondStatus = client.Call("status", second, []) |> succeeded
      (field "cleanupPending" secondStatus).GetBoolean() |> Expect.isFalse "unaffected session cleanup completed despite peer failure"
      (field "cleanupError" secondStatus).ValueKind |> Expect.equal "unaffected session has no fabricated cleanup error" JsonValueKind.Null
      use releasedAgain = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
      client.Call("run", second, []) |> refused "closed"
  
    testCase "native builds retain real objects and execution gates reject changed inputs and artifacts" <| fun _ ->
      let directory = evidenceRoot ()
      let fixture = fixture directory
      use client = new Client(directory, "native")
      client.Hello() |> ignore
      let session = openProject client fixture.Project
      let cold = build client session "cold"
      run client session "stable\nbefore\n"
      let coldObjects = objects directory "cold" cold
      let unchanged = build client session "unchanged"
      run client session "stable\nbefore\n"
      let unchangedObjects = objects directory "unchanged" unchanged
      for identity in [ "callable:IncrementalScalarRegions:stable"; "callable:IncrementalScalarRegions:changeable" ] do
        unchangedObjects[identity] |> Expect.equal ("unchanged path and digest: " + identity) coldObjects[identity]
        stringArray "reusedObjects" unchanged |> List.contains identity |> Expect.isTrue ("compiler reports reuse: " + identity)
      let edit = reserve client session "changeable edit"
      noCurrent client session
      let changedSource = fixture.OriginalSource.Replace("let changeable () = false", "let changeable () = true")
      File.WriteAllText(fixture.Source, changedSource)
      let edited = buildReserved client session edit |> succeeded
      run client session "stable\nafter\n"
      let editedObjects = objects directory "edited" edited
      editedObjects["callable:IncrementalScalarRegions:stable"]
      |> Expect.equal "stable object retains actual path and hash" coldObjects["callable:IncrementalScalarRegions:stable"]
      editedObjects["callable:IncrementalScalarRegions:changeable"]
      |> Expect.notEqual "changed object has a distinct artifact identity" coldObjects["callable:IncrementalScalarRegions:changeable"]
      stringArray "compiledObjects" edited |> List.contains "callable:IncrementalScalarRegions:changeable"
      |> Expect.isTrue "compiler reports changed callable compilation"
      stringArray "reusedObjects" edited |> List.contains "callable:IncrementalScalarRegions:stable"
      |> Expect.isTrue "compiler reports stable callable reuse"
      stringField "sourceVersion" edited |> Expect.notEqual "source receipt changes" (stringField "sourceVersion" cold)
  
      File.WriteAllText(fixture.Source, fixture.OriginalSource)
      client.Call("run", session, []) |> refused "compiler_refused"
      noCurrent client session
      let invalid = reserve client session "invalid source"
      File.WriteAllText(fixture.Source, fixture.OriginalSource.Replace("let changeable () = false", "let changeable () = missingName"))
      buildReserved client session invalid |> refused "compiler_refused"
      client.Call("run", session, []) |> refused "not_accepted"
      noCurrent client session
  
      let restore = reserve client session "restore valid source"
      File.WriteAllText(fixture.Source, changedSource)
      buildReserved client session restore |> succeeded |> ignore
      run client session "stable\nafter\n"
      let projectText = File.ReadAllText fixture.Project
      File.AppendAllText(fixture.Project, "\n# unannounced manifest change\n")
      client.Call("run", session, []) |> refused "compiler_refused"
      noCurrent client session
      let restore = reserve client session "restore manifest"
      File.WriteAllText(fixture.Project, projectText)
      buildReserved client session restore |> succeeded |> ignore
  
      let dependencyText = File.ReadAllText fixture.Dependency
      File.AppendAllText(fixture.Dependency, "\n# unannounced dependency receipt change\n")
      client.Call("run", session, []) |> refused "compiler_refused"
      noCurrent client session
      let restore = reserve client session "restore dependency"
      File.WriteAllText(fixture.Dependency, dependencyText)
      let restored = buildReserved client session restore |> succeeded
      run client session "stable\nafter\n"
  
      File.Delete(stringField "artifactPath" restored)
      client.Call("run", session, []) |> refused "compiler_refused"
      noCurrent client session
      let restored = build client session "restore missing executable"
      File.AppendAllText(stringField "artifactPath" restored, "corrupt executable bytes")
      client.Call("run", session, []) |> refused "compiler_refused"
      noCurrent client session
      build client session "restore corrupt executable" |> ignore
      run client session "stable\nafter\n"
  
      client.Call("prepare_compiler_change", "", []) |> succeeded |> ignore
      client.Call("run", session, []) |> refused "closed"
      use replacement = new Client(directory, "replacement-native")
      replacement.Hello() |> ignore
      replacement.Epoch |> Expect.notEqual "replacement compiler has fresh epoch" client.Epoch
      let freshSession = openProject replacement fixture.Project
      replacement.Call("run", freshSession, []) |> refused "not_accepted"
      build replacement freshSession "fresh compiler epoch" |> ignore
      run replacement freshSession "stable\nafter\n"
  ]
  |> Bozzetto.Tests.TestInfrastructure.Integration.register
    (Bozzetto.Tests.TestInfrastructure.Integration.Dedicated "--integration-composer")
