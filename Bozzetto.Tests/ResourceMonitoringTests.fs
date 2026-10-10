module Bozzetto.Tests.ResourceMonitoringTests

open System
open System.Diagnostics
open System.Threading
open Expecto
open Expecto.Flip
open Bozzetto.Server
open Bozzetto.Web.Shared.Protocol

let private counter start ticks : LinuxResources.ProcessCounter =
  { Pid = 42; Parent = 1; Start = start; Name = "outside-build"; Ticks = ticks; Resident = 4096L; Threads = 7 }
let private raw () : LinuxResources.Raw =
  { At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); Clock = Stopwatch.GetTimestamp()
    Cpu = Some { Total = 1000L; Idle = 600L; Wait = 20L; Count = 4 }
    Processes = [|counter 10L 100L|]; Metrics = [||]; SwapIn = None; SwapOut = None
    Count = 1; Unreadable = 0; Truncated = false }
let private metrics used total : ResourceMetric array =
  [| { Name = "RAM used (total − available)"; Unit = "bytes"; Value = Some used }
     { Name = "RAM total (OS managed)"; Unit = "bytes"; Value = Some total } |]

[<Tests>]
let tests = testList "Resource monitoring" [
  testCase "host CPU excludes double-counted guest time and I/O wait" <| fun _ ->
    let parse text = LinuxResources.parseCpu text |> Option.get
    let a = parse "cpu 10 2 3 40 5 1 1 0 1000 1000\ncpu0 1\ncpu1 1\n"
    a.Total |> Expect.equal "guest not counted twice" 62L
    let b = { a with Total = a.Total + 100L; Idle = a.Idle + 60L; Wait = a.Wait + 10L }
    LinuxResources.cpuDelta a b |> Expect.equal "busy and wait have separate meanings" (Some(30.,10.,100L))
    LinuxResources.cpuDelta b a |> Expect.isNone "counter reset invalidates interval"
    LinuxResources.cpuDelta a { b with Count = 3 } |> Expect.isNone "hotplug invalidates interval"

  testCase "process CPU is core equivalents and PID reuse is unavailable" <| fun _ ->
    let p = counter 10L 100L
    LinuxResources.processPercent 400L 4 (Some p) { p with Ticks = 200L }
    |> Expect.equal "one busy CPU on four CPUs is 100 not 25" (Some 100.)
    LinuxResources.processPercent 400L 4 (Some p) { p with Start = 11L; Ticks = 200L }
    |> Expect.isNone "new incarnation cannot inherit old CPU"
    LinuxResources.processPercent 400L 4 (Some p) { p with Ticks = 10L }
    |> Expect.isNone "regressing counter isn't idle"
    LinuxResources.processPercent 400L 4 None p |> Expect.isNone "first observation isn't idle"

  testCase "proc stat handles parentheses in comm and scales RSS pages" <| fun _ ->
    let fields = Array.create 22 "0"
    fields[0] <- "S"; fields[1] <- "1"; fields[11] <- "20"; fields[12] <- "30"
    fields[17] <- "7"; fields[19] <- "123"; fields[21] <- "2"
    let parsed = LinuxResources.parseProcess 4096 ("42 (worker ) test) " + String.concat " " fields) |> Option.get
    parsed.Name |> Expect.equal "use last delimiter" "worker ) test"
    parsed.Ticks |> Expect.equal "own user plus system only" 50L
    parsed.Resident |> Expect.equal "bytes not pages" 8192L
    LinuxResources.parseProcess 4096 "disappeared" |> Expect.isNone "malformed/disappearing process"

  testCase "external process visible without daemon ownership and no RSS host total" <| fun _ ->
    let sample = LinuxResources.project None (raw())
    sample.Processes[0].Name |> Expect.equal "outside workload retained" "outside-build"
    sample.Processes[0].CpuPercent |> Expect.isNone "no baseline is not zero"
    sample.Metrics |> Array.exists (fun m -> m.Name = "Host CPU busy" && m.Value.IsNone)
    |> Expect.isTrue "missing delta explicit"
    sample.Metrics |> Array.exists (fun m -> m.Name.Contains "RSS") |> Expect.isFalse "no overlapping RSS total"

  testCase "incremental graph reuses unchanged inputs and keeps untouched payloads" <| fun _ ->
    let a, m = ResourceGraph.update (metrics 25. 100.) (ResourceGraph.create())
    m.Value |> Expect.equal "derived occupancy" (Some 25.)
    let b, _ = ResourceGraph.update (metrics 25. 100.) a
    ResourceGraph.evaluations b |> Expect.equal "same semantic inputs do not reevaluate" (ResourceGraph.evaluations a)
    let c = ([26..40], b) ||> fun values initial ->
      (initial, values) ||> List.fold (fun s n -> ResourceGraph.update (metrics (float n) 100.) s |> fst)
    let d, final = ResourceGraph.update (metrics 40. 200.) c
    final.Value |> Expect.equal "unchanged other input remains retained" (Some 20.)
    ResourceGraph.evaluations d |> Expect.equal "only changed sources triggered work" 17

  testCase "resource command bounds and resource JSON encode explicitly" <| fun _ ->
    UiBridgeCodec.decodeCommand "{\"correlation\":1,\"command\":\"observe_resources\",\"seconds\":120}"
    |> Expect.equal "bounded request accepted" (Ok { Correlation = 1; Command = ObserveResources 120 })
    match UiBridgeCodec.decodeCommand "{\"correlation\":1,\"command\":\"observe_resources\",\"seconds\":121}" with
    | Error r -> r.Message |> Expect.stringContains "specific bounds refusal" "0 to 120"
    | Ok _ -> failtest "unbounded request accepted"
    let json = UiBridgeCodec.encodeEvent(Resources(LinuxResources.project None (raw())))
    json |> Expect.stringContains "missing CPU survives transport" "\"cpuPercent\":null"
    json |> Expect.stringContains "event identity" "\"event\":\"resources\""

  testCaseAsync "acquisition is cold, shared, and stops after final subscriber leaves" <| async {
    let mutable reads = 0
    let acquire () = Interlocked.Increment(&reads) |> ignore; raw()
    let monitor = new ResourceMonitor.Monitor(acquire, TimeSpan.FromMilliseconds 25.)
    do! Async.Sleep 40
    reads |> Expect.equal "zero default scans" 0
    let first = System.Threading.Tasks.TaskCompletionSource<unit>()
    let second = System.Threading.Tasks.TaskCompletionSource<unit>()
    do! monitor.Observe(1L, 1, fun r -> if r.Status = "observing" then first.TrySetResult() |> ignore)
    do! first.Task.WaitAsync(TimeSpan.FromSeconds 2.) |> Async.AwaitTask
    do! monitor.Observe(2L, 1, fun r -> if r.Sequence >= 2L then second.TrySetResult() |> ignore)
    do! monitor.Remove 1L
    do! second.Task.WaitAsync(TimeSpan.FromSeconds 2.) |> Async.AwaitTask
    (reads > 1) |> Expect.isTrue "other subscriber keeps acquisition alive"
    do! monitor.Remove 2L
    let stopped = reads
    do! Async.Sleep 80
    reads |> Expect.equal "no reads after final release" stopped
    do! monitor.Close()
  }

  testCaseAsync "explicit window expiry ends acquisition without a browser timer" <| async {
    let mutable reads = 0
    let monitor = new ResourceMonitor.Monitor((fun () -> Interlocked.Increment(&reads) |> ignore; raw()), TimeSpan.FromMilliseconds 25.)
    let finished = System.Threading.Tasks.TaskCompletionSource<unit>()
    do! monitor.Observe(1L, 1, fun r -> if r.Status = "complete" then finished.TrySetResult() |> ignore)
    do! finished.Task.WaitAsync(TimeSpan.FromSeconds 3.) |> Async.AwaitTask
    let stopped = reads
    do! Async.Sleep 80
    reads |> Expect.equal "expired window never silently renews" stopped
    do! monitor.Close()
  }
]
