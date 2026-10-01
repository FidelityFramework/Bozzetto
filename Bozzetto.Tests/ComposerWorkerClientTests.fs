module Bozzetto.Tests.ComposerWorkerClientTests

open System
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto.ComposerIntegration

type private FixtureMarker = class end

/// Runs only through the test executable's explicit child-fixture branch.
let runFixture () =
  let held = ResizeArray<JsonElement>()
  let reply (request: JsonElement) (result: obj) =
    let frame =
      {| protocolVersion = 1
         requestId = request.GetProperty("requestId").GetString()
         authority =
          {| host = "transport-fixture"; epoch = "transport-fixture-epoch"
             provider = "clef-composer"; generation = 0L
             session = request.GetProperty("session").GetString() |}
         success = true; result = result |}
    Console.Out.WriteLine(JsonSerializer.Serialize frame)
    Console.Out.Flush()
  let mutable reading = true
  while reading do
    match Console.ReadLine() with
    | null -> reading <- false
    | line ->
      use document = JsonDocument.Parse line
      let request = document.RootElement
      match request.GetProperty("operation").GetString() with
      | "hello" -> reply request (box {| ready = true |})
      | "barrier" -> reply request (box {| held = held.Count |})
      | "cancel_request" ->
        let result =
          {| targetRequestId = request.GetProperty("targetRequestId").GetString()
             cancellationRequested = true |}
        reply request (box result)
      | "release" ->
        let count = held.Count
        for item in held do reply item (box {| completed = true |})
        held.Clear()
        reply request (box {| released = count |})
      | "build" | "run" | "status" -> held.Add(request.Clone())
      | operation -> invalidOp ("Unexpected transport fixture operation: " + operation)
  0

let private bounded (work: Task<'T>) = work.WaitAsync(TimeSpan.FromSeconds 10.)

let private atCapacity operation = task {
  use entered = new ManualResetEventSlim()
  use release = new ManualResetEventSlim()
  use cancellation = new CancellationTokenSource()
  let beforeCancellationAdmission () =
    entered.Set()
    if not (release.Wait(TimeSpan.FromSeconds 10.)) then
      failwith "Cancellation admission barrier was not released."
  let cache =
    Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
    |> Option.ofObj
    |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.defaultWith (fun () ->
      Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache"))
  let dotnet =
    Environment.GetEnvironmentVariable "DOTNET_HOST_PATH"
    |> Option.ofObj
    |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.defaultValue "dotnet"
  let! client =
    ComposerWorkerClient.startWithCancellationBoundary beforeCancellationAdmission
      [ "--composer-worker-client-fixture" ]
      { WorkerPath = typeof<FixtureMarker>.Assembly.Location
        DotnetPath = dotnet
        EvidenceDirectory = Path.Combine(cache, "bozzetto", "worker-transport-tests", Guid.NewGuid().ToString("N")) }
  let mutable canceling: Task = Task.CompletedTask
  try
    let victim = client.RequestAsync(operation, "session", [], cancellation.Token)
    let! barrier = client.RequestAsync("barrier", "", [], CancellationToken.None) |> bounded
    barrier.GetProperty("result").GetProperty("held").GetInt32()
    |> Expect.equal "The original request reached the child before cancellation." 1
    let held =
      Array.init 255 (fun _ -> client.RequestAsync("status", "session", [], CancellationToken.None))
    canceling <- Task.Run(Action(fun () -> cancellation.Cancel()))
    entered.Wait(TimeSpan.FromSeconds 10.)
    |> Expect.isTrue "Cancellation reached the control-admission boundary."
    let competing = client.RequestAsync("status", "session", [], CancellationToken.None)
    let rejectedAtAdmission = competing.IsFaulted
    release.Set()
    do! canceling.WaitAsync(TimeSpan.FromSeconds 10.)
    rejectedAtAdmission |> Expect.isTrue "An ordinary request cannot steal the withdrawal's bounded slot."
    let! refused = task {
      try
        let! _ = competing |> bounded
        return false
      with :? InvalidOperationException as error ->
        return error.Message = "Too many pending Composer requests."
    }
    refused |> Expect.isTrue "The competing request receives the ordinary capacity refusal."
    let! detached = task {
      try
        let! _ = victim |> bounded
        return false
      with :? OperationCanceledException -> return true
    }
    detached |> Expect.isTrue "The original caller observes cancellation after the exact acknowledgement."
    client.IsAlive |> Expect.isTrue "Capacity pressure does not retire a healthy worker."
    let! released = client.RequestAsync("release", "", [], CancellationToken.None) |> bounded
    released.GetProperty("result").GetProperty("released").GetInt32()
    |> Expect.equal "Only the original and 255 admitted requests reached the child." 256
    let! survivors = Task.WhenAll held |> bounded
    survivors |> Array.forall (fun response -> response.GetProperty("success").GetBoolean())
    |> Expect.isTrue "Every other admitted request completes successfully."
    client.IsAlive |> Expect.isTrue "Late completion of the abandoned request leaves the connection usable."
  finally
    release.Set()
    try canceling.WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
    finally client.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
}

[<Tests>]
let tests =
  [ "build"; "run" ]
  |> List.map (fun operation ->
    testCaseAsync (operation + " cancellation retains its slot at transport capacity") (async {
      do! atCapacity operation |> Async.AwaitTask
    }))
  |> testList "Composer worker transport capacity"
