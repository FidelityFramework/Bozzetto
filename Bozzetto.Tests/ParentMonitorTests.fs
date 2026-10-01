module Bozzetto.Tests.ParentMonitorTests

open System
open System.Diagnostics
open System.Threading
open Expecto
open Expecto.Flip
open Bozzetto

/// Tests for the worker parent-death watchdog (issue #126).
/// Workers self-exit when their daemon process dies, so hard kills
/// (Task Manager, taskkill /F, crash, OS shutdown) don't orphan workers.

[<Tests>]
let parentMonitorAliveTests = testList "Parent lifetime through OwnerMonitor.isAlive" [

  testCase "live pid reports alive" <| fun _ ->
    use self = Process.GetCurrentProcess()
    OwnerMonitor.isAlive (fun _ -> Some self) (OwnerMonitor.Owner.ofPid self.Id)
    |> Expect.isTrue "current process should be alive"

  testCase "missing pid reports dead" <| fun _ ->
    OwnerMonitor.isAlive (fun _ -> None) (OwnerMonitor.Owner.ofPid 999999)
    |> Expect.isFalse "none lookup should be dead"

  testCase "exited process reports dead" <| fun _ ->
    // Spawn a throwaway process and let it exit, then check HasExited.
    let psi = ProcessStartInfo(
      FileName = Environment.ProcessPath,
      RedirectStandardOutput = true,
      UseShellExecute = false,
      CreateNoWindow = true)
    psi.ArgumentList.Add("--help")
    use p = Process.Start(psi)
    p.WaitForExit(10_000) |> ignore
    OwnerMonitor.isAlive (fun _ -> Some p) (OwnerMonitor.Owner.ofPid p.Id)
    |> Expect.isFalse "exited process should be dead"

  testCase "lookup exception reports dead" <| fun _ ->
    OwnerMonitor.isAlive (fun _ -> failwith "boom") (OwnerMonitor.Owner.ofPid 123)
    |> Expect.isFalse "throwing lookup should be treated as dead"
]

[<Tests>]
let parentMonitorRunTests = testList "Parent lifetime through OwnerMonitor.run" [

  testTask "cancels cts when daemon pid disappears" {
    let cts = new CancellationTokenSource()
    let logLines = ref []
    let running =
      OwnerMonitor.run (fun _ -> None) (OwnerMonitor.Owner.ofPid 999999) cts (fun msg -> logLines.Value <- msg :: logLines.Value)
      |> Async.StartAsTask
    try
      // Always report dead — monitor should cancel promptly.
      let! _ = Tasks.Task.WhenAny(running :> Tasks.Task, Tasks.Task.Delay 10_000)
      running.IsCompleted |> Expect.isTrue "monitor terminates within the deadline"
      cts.IsCancellationRequested
      |> Expect.isTrue "cts should be cancelled after daemon death detected"
      (not (List.isEmpty logLines.Value))
      |> Expect.isTrue "should log that the daemon died"
    finally
      cts.Cancel()
      running.GetAwaiter().GetResult()
      cts.Dispose()
  }

  testTask "does not cancel while daemon alive" {
    let cts = new CancellationTokenSource()
    let self = Process.GetCurrentProcess()
    let running =
      OwnerMonitor.run (fun _ -> Some self) (OwnerMonitor.Owner.ofPid self.Id) cts ignore
      |> Async.StartAsTask
    try
      // Give it a few poll cycles while the daemon (us) stays alive.
      do! Tasks.Task.Delay(OwnerMonitor.pollIntervalMs * 3)
      cts.IsCancellationRequested
      |> Expect.isFalse "should not cancel while daemon alive"
    finally
      // Join even when an assertion fails; the monitor owns this CTS until exit.
      cts.Cancel()
      running.GetAwaiter().GetResult()
      cts.Dispose()
      self.Dispose()
  }
]
