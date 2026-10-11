module Bozzetto.Tests.DiagnosticSinkTests

open System
open System.Diagnostics
open System.IO
open System.IO.Pipes
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto.Diagnostics

type private FixtureMarker = class end

let private take = function Result.Ok value -> value | Result.Error reason -> failtest reason

let private start arguments =
  let executable =
    Environment.GetEnvironmentVariable "DOTNET_HOST_PATH"
    |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.defaultValue "dotnet"
  let runner = typeof<FixtureMarker>.Assembly.Location
  let info = ProcessStartInfo(executable)
  info.UseShellExecute <- false
  info.RedirectStandardOutput <- true
  info.RedirectStandardError <- true
  for argument in [ "exec"; "--runtimeconfig"; Path.ChangeExtension(runner, ".runtimeconfig.json")
                    "--depsfile"; Path.ChangeExtension(runner, ".deps.json")
                    typeof<CaptureMetadata>.Assembly.Location ] @ arguments do
    info.ArgumentList.Add argument
  let child = new Process(StartInfo = info)
  if not (child.Start()) then failtest "Diagnostic fixture process did not start."
  child

let private withStore work = async {
  let directory = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData,
                               "bozzetto", "diagnostic-tests", Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory directory |> ignore
  try return! work directory
  finally Directory.Delete(directory, true)
}

let internal withSink directory maximum idleSeconds work = async {
  let pipe = "boz-diagnostics-test-" + Guid.NewGuid().ToString("N")
  use child = start [ "--pipe"; pipe; "--store"; directory; "--idle-seconds"; string idleSeconds
                      "--io-timeout-ms"; "2000"; "--max-captures"; string maximum ]
  let errors = child.StandardError.ReadToEndAsync()
  let mutable result = None
  let mutable failure: Runtime.ExceptionServices.ExceptionDispatchInfo option = None
  try
    let! ready = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds 15.) |> Async.AwaitTask
    ready |> Expect.equal "real sink reports its listening endpoint" ("DIAGNOSTIC_SINK_READY pipe=" + pipe)
    let! value = work pipe child
    result <- Some value
  with error -> failure <- Some(Runtime.ExceptionServices.ExceptionDispatchInfo.Capture error)
  if not child.HasExited then child.Kill(true)
  do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.) |> Async.AwaitTask
  let! stderr = errors |> Async.AwaitTask
  failure |> Option.iter (fun original ->
    if stderr <> "" then eprintfn "Diagnostic sink stderr: %s" stderr
    original.Throw())
  return result.Value
}

[<Tests>]
let tests =
  testList "Independent diagnostic sink" [
    testCaseAsync "persisted capture survives producer exit and a new producer reconnects" <|
      withStore (fun root -> async {
        let store = Path.Combine(root, "store")
        let capture = DiagnosticCaptureTests.sampleCapture ()
        let bytes = CaptureCodec.encode capture |> take
        let input = Path.Combine(root, "capture.bare")
        File.WriteAllBytes(input, bytes)
        return! withSink store 4 120 (fun pipe sink -> async {
          use producer = start [ "submit"; "--pipe"; pipe; "--capture"; input; "--timeout-ms"; "5000" ]
          let output = producer.StandardOutput.ReadToEndAsync()
          let errors = producer.StandardError.ReadToEndAsync()
          do! producer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 15.) |> Async.AwaitTask
          let! stderr = errors |> Async.AwaitTask
          producer.ExitCode |> Expect.equal ("producer confirms sink persistence: " + stderr) 0
          let! acknowledgment = output |> Async.AwaitTask
          acknowledgment.Contains "persisted" |> Expect.isTrue "producer receives an explicit persistence receipt"
          sink.HasExited |> Expect.isFalse "producer lifetime does not own the sink"
          let! replay = Client.capture pipe 5000 capture
          let receipt = take replay
          let saved = File.ReadAllBytes(Path.Combine(store, receipt.Digest + ".bare"))
          saved |> Expect.equal "sink owns the exact lossless capture after producer exit" bytes
          CaptureCodec.decode saved |> take |> Expect.equal "scope and source identity survive persistence" capture
          Directory.GetFiles(store, "*.json").Length |> Expect.equal "replay preserves one committed capture" 1
        })
      })
    testCaseAsync "corrupt transfer never becomes a committed snapshot" <|
      withStore (fun store ->
        withSink store 4 120 (fun pipe _ -> async {
          let original = DiagnosticCaptureTests.sampleCapture () |> CaptureCodec.encode |> take
          let corrupted = Array.copy original
          corrupted[corrupted.Length - 1] <- corrupted[corrupted.Length - 1] ^^^ 1uy
          let! response = Client.exchange pipe 5000 corrupted
          response |> Result.bind CaptureCodec.decodeReply |> Result.isError
          |> Expect.isTrue "receiver refuses corruption before acknowledging capture"
          Directory.GetFiles(store, "*.json") |> Expect.isEmpty "no persistence marker for damaged transport"
          let! valid = Client.capture pipe 5000 (DiagnosticCaptureTests.sampleCapture ())
          valid |> Result.isOk |> Expect.isTrue "a bad producer does not kill the sink"
        }))
    testCaseAsync "producer disconnect during a frame leaves no capture and the sink accepts the next producer" <|
      withStore (fun store ->
        withSink store 4 120 (fun pipe _ -> async {
          let bytes = DiagnosticCaptureTests.sampleCapture () |> CaptureCodec.encode |> take
          do! async {
            use deadline = new CancellationTokenSource(5000)
            use producer = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                                                     PipeOptions.Asynchronous ||| PipeOptions.CurrentUserOnly)
            do! producer.ConnectAsync(deadline.Token) |> Async.AwaitTask
            let prefix = Array.zeroCreate<byte> 4
            BAREWire.Encoding.Encoder.writeU32 prefix 0 (uint32 bytes.Length) |> ignore
            do! producer.WriteAsync(prefix.AsMemory(), deadline.Token).AsTask() |> Async.AwaitTask
            do! producer.WriteAsync(bytes.AsMemory(0, bytes.Length / 2), deadline.Token).AsTask() |> Async.AwaitTask
            do! producer.FlushAsync(deadline.Token) |> Async.AwaitTask
          }
          let! accepted = Client.capture pipe 5000 (DiagnosticCaptureTests.sampleCapture ())
          let receipt = take accepted
          Directory.GetFiles(store, "*.json").Length |> Expect.equal "only the complete second producer commits a descriptor" 1
          Directory.GetFiles(store, "*.bare").Length |> Expect.equal "no partial binary is retained as a capture" 1
          File.ReadAllBytes(Path.Combine(store, receipt.Digest + ".bare"))
          |> Expect.equal "the committed body comes from the complete transfer" bytes
        }))
    testCaseAsync "an open idle producer cannot keep the independent sink alive" <|
      withStore (fun store ->
        withSink store 4 1 (fun pipe sink -> async {
          use deadline = new CancellationTokenSource(5000)
          use producer = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                                                   PipeOptions.Asynchronous ||| PipeOptions.CurrentUserOnly)
          do! producer.ConnectAsync(deadline.Token) |> Async.AwaitTask
          do! sink.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.) |> Async.AwaitTask
          sink.ExitCode |> Expect.equal "read deadline releases the pipe and expired idle lifetime ends cleanly" 0
          Directory.GetFiles(store, "*.json") |> Expect.isEmpty "an idle connection has no persisted receipt"
        }))
    testCaseAsync "quota refusal preserves accepted evidence and replay checks stored bytes" <|
      withStore (fun store ->
        withSink store 1 120 (fun pipe _ -> async {
          let first = DiagnosticCaptureTests.sampleCapture ()
          let! accepted = Client.capture pipe 5000 first
          let receipt = take accepted
          let transaction = {
            first.Delivery.Transaction with
              BaseCursor = { first.Delivery.Transaction.BaseCursor with Subscription = "another-demand" }
              TargetCursor = { first.Delivery.Transaction.TargetCursor with Subscription = "another-demand" } }
          let second = {
            first with
              Metadata = { first.Metadata with Demand = "another-demand" }
              Delivery = { first.Delivery with Transaction = transaction } }
          let! refused = Client.capture pipe 5000 second
          match refused with
          | Result.Error reason -> reason |> Expect.stringContains "valid new capture is refused by the store quota" "capacity"
          | Result.Ok _ -> failtest "A full store accepted a new capture."
          Directory.GetFiles(store, "*.bare").Length |> Expect.equal "first capture is retained" 1
          let path = Path.Combine(store, receipt.Digest + ".bare")
          let stored = File.ReadAllBytes path
          stored[stored.Length - 1] <- stored[stored.Length - 1] ^^^ 1uy
          File.WriteAllBytes(path, stored)
          let! replay = Client.capture pipe 5000 first
          replay |> Result.isError |> Expect.isTrue "existing marker cannot hide damaged evidence"
        }))
    testCaseAsync "idle shutdown retains evidence for the next independent sink lifetime" <|
      withStore (fun store -> async {
        let capture = DiagnosticCaptureTests.sampleCapture ()
        let! receipt = withSink store 4 2 (fun pipe sink -> async {
          let! accepted = Client.capture pipe 5000 capture
          let receipt = take accepted
          do! sink.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.) |> Async.AwaitTask
          sink.ExitCode |> Expect.equal "explicit idle lifetime ends cleanly" 0
          File.Exists(Path.Combine(store, receipt.Digest + ".json"))
          |> Expect.isTrue "idle disposal keeps the committed capture"
          return receipt
        })
        do! withSink store 4 120 (fun pipe _ -> async {
          let! recovered = Client.capture pipe 5000 capture
          take recovered |> Expect.equal "restarted sink verifies the same stored capture" receipt
        })
      })
  ]
