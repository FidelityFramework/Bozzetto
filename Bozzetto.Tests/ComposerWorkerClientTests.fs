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

type CancelWhenSerialized(source: CancellationTokenSource, observed: unit -> unit) =
  member _.Value =
    observed ()
    source.Cancel()
    "canceled-before-send"

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

let private canceled (work: Task<JsonElement>) = task {
  try
    let! _ = bounded work
    return false
  with :? OperationCanceledException -> return true
}

let private cancellationPhase phase = task {
  let entered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let beforeWrite _ session =
    if session = "writer" then
      entered.TrySetResult() |> ignore
      release.Task :> Task
    else Task.CompletedTask
  let cache =
    Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
    |> Option.ofObj
    |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.defaultWith (fun () -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache"))
  let dotnet =
    Environment.GetEnvironmentVariable "DOTNET_HOST_PATH"
    |> Option.ofObj
    |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.defaultValue "dotnet"
  let! client = ComposerWorkerClient.startWithBoundaries ignore beforeWrite
                  [ "--composer-worker-client-fixture" ]
                  { WorkerPath = typeof<FixtureMarker>.Assembly.Location; DotnetPath = dotnet
                    EvidenceDirectory = Path.Combine(cache, "bozzetto", "worker-transport-tests", Guid.NewGuid().ToString("N")) }
  try
    let peer = client.RequestAsync("status", "peer", [], CancellationToken.None)
    let! initial = client.RequestAsync("barrier", "", [], CancellationToken.None) |> bounded
    initial.GetProperty("result").GetProperty("held").GetInt32()
    |> Expect.equal "The unrelated peer is physically retained by the child." 1
    let mutable serialized = 0
    if phase = "writing" || phase = "sent" then
      use cancellation = new CancellationTokenSource()
      let victim = client.RequestAsync("build", (if phase = "writing" then "writer" else "victim"), [], cancellation.Token)
      if phase = "writing" then
        do! entered.Task |> bounded
        cancellation.Cancel()
        victim.IsCompleted |> Expect.isFalse "A possibly-written build waits for targeted cancellation acknowledgement."
        release.TrySetResult() |> ignore
      else
        let! barrier = client.RequestAsync("barrier", "", [], CancellationToken.None) |> bounded
        barrier.GetProperty("result").GetProperty("held").GetInt32()
        |> Expect.equal "The victim was sent before cancellation." 2
        cancellation.Cancel()
      let! detached = canceled victim
      detached |> Expect.isTrue "Targeted cancellation acknowledges the exact build."
      let! response = client.RequestAsync("release", "", [], CancellationToken.None) |> bounded
      response.GetProperty("result").GetProperty("released").GetInt32()
      |> Expect.equal "Both possibly-written requests retain valid late-reply correlation." 2
    else
      let writer =
        if phase = "queued" then Some(client.RequestAsync("status", "writer", [], CancellationToken.None)) else None
      if writer.IsSome then do! entered.Task |> bounded
      // The production bound is part of the discriminator: a healthy peer
      // must survive more never-written cancellations than late-reply slots.
      for _ in 1 .. 4097 do
        use cancellation = new CancellationTokenSource()
        if phase = "initial" then cancellation.Cancel()
        let parameters =
          if phase = "initial" || phase = "serialization" then
            [ "probe", box (CancelWhenSerialized(cancellation, fun () -> serialized <- serialized + 1)) ]
          else []
        let beforeSerialization = serialized
        let victim = client.RequestAsync("status", "victim", parameters, cancellation.Token)
        if phase = "serialization" then
          serialized |> Expect.equal "The public payload getter canceled during serialization." (beforeSerialization + 1)
        if phase = "queued" then cancellation.Cancel()
        let! detached = canceled victim
        detached |> Expect.isTrue "Each unsent caller observes cancellation."
      serialized |> Expect.equal "Only serialization-time cancellation executes the getter."
        (if phase = "serialization" then 4097 else 0)
      client.IsAlive |> Expect.isTrue "Never-written requests do not exhaust late-reply capacity."
      peer.IsCompleted |> Expect.isFalse "The unrelated held request remains owned."
      release.TrySetResult() |> ignore
      let! response = client.RequestAsync("release", "", [], CancellationToken.None) |> bounded
      response.GetProperty("result").GetProperty("released").GetInt32()
      |> Expect.equal "No definitively unsent victim reached the child." (if writer.IsSome then 2 else 1)
      match writer with
      | Some work ->
        let! reply = bounded work
        reply.GetProperty("success").GetBoolean() |> Expect.isTrue "The writer survives queued cancellations."
      | None -> ()
    let! peerReply = peer |> bounded
    peerReply.GetProperty("success").GetBoolean() |> Expect.isTrue "The independent peer completes successfully."
    let! final = client.RequestAsync("barrier", "", [], CancellationToken.None) |> bounded
    final.GetProperty("result").GetProperty("held").GetInt32() |> Expect.equal "All physical replies were drained." 0
    client.IsAlive |> Expect.isTrue "The protocol remains usable after late replies."
  finally
    release.TrySetResult() |> ignore
    client.StopAsync().WaitAsync(TimeSpan.FromSeconds 10.).GetAwaiter().GetResult()
}

[<Tests>]
let tests =
  let capacity =
    [ "build"; "run" ]
    |> List.map (fun operation ->
      testCaseAsync (operation + " cancellation retains its slot at transport capacity") (async {
        do! atCapacity operation |> Async.AwaitTask
      }))
  let phases =
    [ "initial"; "serialization"; "queued"; "writing"; "sent" ]
    |> List.map (fun phase ->
      testCaseAsync (phase + " cancellation preserves transport ownership and peer progress") (async {
        do! cancellationPhase phase |> Async.AwaitTask
      }))
  capacity @ phases |> testList "Composer worker transport capacity"
