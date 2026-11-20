module Bozzetto.Tests.DaemonHealthTests

open System
open Expecto
open Expecto.Flip
open Bozzetto.Features

[<Tests>]
let healthSnapshotTests =
  testList "DaemonHealth snapshot" [

    testCase "healthy daemon with ready sessions" <| fun _ ->
      let snapshot = {
        DaemonPid = 1234
        DaemonPort = 47749
        Uptime = TimeSpan.FromHours 2.5
        Version = "0.5.761"
        SessionSummaries = [
          { SessionId = "abc123"; ProjectName = "MyLib"; Status = SessionHealthStatus.Ready; EvalCount = 42; LastActivity = DateTimeOffset.UtcNow }
        ]
        LiveTestingSummary = Some { TotalTests = 100; Passed = 98; Failed = 2; Running = 0 }
        MemoryMB = 256
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      let health = DaemonHealth.overallStatus snapshot
      health |> Expect.equal "should be healthy" OverallHealth.Healthy

    testCase "degraded when any session is faulted" <| fun _ ->
      let snapshot = {
        DaemonPid = 1234
        DaemonPort = 47749
        Uptime = TimeSpan.FromMinutes 5.0
        Version = "0.5.761"
        SessionSummaries = [
          { SessionId = "abc"; ProjectName = "Good"; Status = SessionHealthStatus.Ready; EvalCount = 10; LastActivity = DateTimeOffset.UtcNow }
          { SessionId = "def"; ProjectName = "Bad"; Status = SessionHealthStatus.Faulted; EvalCount = 0; LastActivity = DateTimeOffset.UtcNow }
        ]
        LiveTestingSummary = None
        MemoryMB = 128
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      let health = DaemonHealth.overallStatus snapshot
      health |> Expect.equal "should be degraded" OverallHealth.Degraded

    testCase "healthy when no sessions exist (idle daemon)" <| fun _ ->
      let snapshot = {
        DaemonPid = 1234
        DaemonPort = 47749
        Uptime = TimeSpan.FromSeconds 2.0
        Version = "0.5.761"
        SessionSummaries = []
        LiveTestingSummary = None
        MemoryMB = 64
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      let health = DaemonHealth.overallStatus snapshot
      health |> Expect.equal "no sessions = healthy (idle)" OverallHealth.Healthy

    testCase "WHY — overallStatus — critical machine memory pressure is Unhealthy even with anomalies: [] and every session Ready, because a smooth RSS ramp never trips the shape-based detector" <| fun _ ->
      let snapshot = {
        DaemonPid = 1234
        DaemonPort = 47749
        Uptime = TimeSpan.FromHours 3.0
        Version = "0.6.820"
        SessionSummaries = [
          { SessionId = "abc123"; ProjectName = "MyLib"; Status = SessionHealthStatus.Ready; EvalCount = 42; LastActivity = DateTimeOffset.UtcNow }
        ]
        LiveTestingSummary = None
        MemoryMB = 55_000
        // Empty — the EWMA/CUSUM detector's own learned baseline rose right
        // along with a smooth ramp, exactly like both real incidents. This
        // snapshot has to be judged unhealthy some other way.
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Critical
      }
      let health = DaemonHealth.overallStatus snapshot
      health |> Expect.equal "the machine is almost out of memory, whatever the shape detector says" OverallHealth.Unhealthy

    testCase "WHY — overallStatus — tight machine memory pressure degrades, the same as a faulted session, even with anomalies: []" <| fun _ ->
      let snapshot = {
        DaemonPid = 1234
        DaemonPort = 47749
        Uptime = TimeSpan.FromHours 1.0
        Version = "0.6.820"
        SessionSummaries = [
          { SessionId = "abc123"; ProjectName = "MyLib"; Status = SessionHealthStatus.Ready; EvalCount = 10; LastActivity = DateTimeOffset.UtcNow }
        ]
        LiveTestingSummary = None
        MemoryMB = 40_000
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Tight
      }
      let health = DaemonHealth.overallStatus snapshot
      health |> Expect.equal "the machine is short on memory — worth degrading for, not staying silent about" OverallHealth.Degraded

    testCase "WHY — overallStatus — normal machine memory pressure changes nothing for an otherwise-healthy daemon" <| fun _ ->
      let snapshot = {
        DaemonPid = 1234
        DaemonPort = 47749
        Uptime = TimeSpan.FromMinutes 10.0
        Version = "0.6.820"
        SessionSummaries = [
          { SessionId = "abc123"; ProjectName = "MyLib"; Status = SessionHealthStatus.Ready; EvalCount = 5; LastActivity = DateTimeOffset.UtcNow }
        ]
        LiveTestingSummary = None
        MemoryMB = 300
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      let health = DaemonHealth.overallStatus snapshot
      health |> Expect.equal "plenty of machine memory, nothing anomalous, nothing faulted" OverallHealth.Healthy
  ]

[<Tests>]
let healthFormatTests =
  testList "DaemonHealth formatting" [

    testCase "summary includes key metrics" <| fun _ ->
      let snapshot = {
        DaemonPid = 5678
        DaemonPort = 47749
        Uptime = TimeSpan.FromHours 1.5
        Version = "0.5.761"
        SessionSummaries = [
          { SessionId = "s1"; ProjectName = "Lib"; Status = SessionHealthStatus.Ready; EvalCount = 25; LastActivity = DateTimeOffset.UtcNow }
        ]
        LiveTestingSummary = Some { TotalTests = 50; Passed = 48; Failed = 2; Running = 0 }
        MemoryMB = 200
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      let text = DaemonHealth.formatSummary snapshot
      text |> Expect.stringContains "has pid" "5678"
      text |> Expect.stringContains "has version" "0.5.761"
      text |> Expect.stringContains "has session" "Lib"

    testCase "emoji health indicator" <| fun _ ->
      DaemonHealth.healthEmoji OverallHealth.Healthy |> Expect.equal "green" "🟢"
      DaemonHealth.healthEmoji OverallHealth.Degraded |> Expect.equal "yellow" "🟡"
      DaemonHealth.healthEmoji OverallHealth.Unhealthy |> Expect.equal "red" "🔴"

    testCase "health label" <| fun _ ->
      DaemonHealth.healthLabel OverallHealth.Healthy |> Expect.equal "healthy" "Healthy"
      DaemonHealth.healthLabel OverallHealth.Degraded |> Expect.equal "degraded" "Degraded"
      DaemonHealth.healthLabel OverallHealth.Unhealthy |> Expect.equal "unhealthy" "Unhealthy"

    testCase "diagnostic summary explains missing sessions" <| fun _ ->
      let snapshot = {
        DaemonPid = 4321
        DaemonPort = 47749
        Uptime = TimeSpan.FromMinutes 1.0
        Version = "0.5.761"
        SessionSummaries = []
        LiveTestingSummary = None
        MemoryMB = 64
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      let text = DaemonHealth.diagnosticSummary snapshot
      text |> Expect.equal "zero is explicitly limited to the F# provider" "No F# sessions registered with the daemon."

    testCase "diagnostic summary includes faulted projects and status breakdown" <| fun _ ->
      let snapshot = {
        DaemonPid = 4321
        DaemonPort = 47749
        Uptime = TimeSpan.FromMinutes 10.0
        Version = "0.5.761"
        SessionSummaries = [
          { SessionId = "ready"; ProjectName = "ReadyProject"; Status = SessionHealthStatus.Ready; EvalCount = 5; LastActivity = DateTimeOffset.UtcNow }
          { SessionId = "faulted"; ProjectName = "FaultedProject"; Status = SessionHealthStatus.Faulted; EvalCount = 0; LastActivity = DateTimeOffset.UtcNow }
          { SessionId = "warming"; ProjectName = "WarmProject"; Status = SessionHealthStatus.WarmingUp; EvalCount = 0; LastActivity = DateTimeOffset.UtcNow }
        ]
        LiveTestingSummary = None
        MemoryMB = 64
        Anomalies = []
        GcDumpOutcome = None
        MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      let text = DaemonHealth.diagnosticSummary snapshot
      text |> Expect.stringContains "diagnostics label the provider for nonzero counts too" "3 F# session(s)"
      text |> Expect.stringContains "should mention the faulted project" "FaultedProject"
      text |> Expect.stringContains "should include the ready count" "Ready=1"
      text |> Expect.stringContains "should include the faulted count" "Faulted=1"
      text |> Expect.stringContains "should include the warming up count" "Warming Up=1"

    testCase "primary session status prefers ready over warming sessions" <| fun _ ->
      let sessions = [
        { SessionId = "warming"; ProjectName = "WarmProject"; Status = SessionHealthStatus.WarmingUp; EvalCount = 0; LastActivity = DateTimeOffset.UtcNow }
        { SessionId = "ready"; ProjectName = "ReadyProject"; Status = SessionHealthStatus.Ready; EvalCount = 3; LastActivity = DateTimeOffset.UtcNow }
      ]

      DaemonHealth.primarySessionStatus sessions
      |> Expect.equal "ready should win over warming" (Some SessionHealthStatus.Ready)

      DaemonHealth.primarySessionStatusLabel sessions
      |> Expect.equal "label should reflect ready state" "Ready"
  ]

[<Tests>]
let sessionHealthTests =
  testList "DaemonHealth session status" [

    testCase "session status labels" <| fun _ ->
      DaemonHealth.sessionStatusLabel SessionHealthStatus.Ready |> Expect.equal "ready" "Ready"
      DaemonHealth.sessionStatusLabel SessionHealthStatus.Evaluating |> Expect.equal "eval" "Evaluating"
      DaemonHealth.sessionStatusLabel SessionHealthStatus.WarmingUp |> Expect.equal "warmup" "Warming Up"
      DaemonHealth.sessionStatusLabel SessionHealthStatus.Faulted |> Expect.equal "faulted" "Faulted"
      DaemonHealth.sessionStatusLabel SessionHealthStatus.Stopped |> Expect.equal "stopped" "Stopped"

    testCase "session status emoji" <| fun _ ->
      DaemonHealth.sessionStatusEmoji SessionHealthStatus.Ready |> Expect.equal "ready" "✅"
      DaemonHealth.sessionStatusEmoji SessionHealthStatus.Faulted |> Expect.equal "faulted" "❌"
      DaemonHealth.sessionStatusEmoji SessionHealthStatus.WarmingUp |> Expect.equal "warmup" "⏳"

    testCase "uptime formatting" <| fun _ ->
      DaemonHealth.formatUptime (TimeSpan.FromMinutes 45.0)
      |> Expect.equal "minutes" "45m"
      DaemonHealth.formatUptime (TimeSpan.FromHours 2.5)
      |> Expect.equal "hours" "2h 30m"
      DaemonHealth.formatUptime (TimeSpan.FromDays 1.5)
      |> Expect.equal "days" "1d 12h"
  ]

[<Tests>]
let structuredErrorTests =
  testList "DaemonHealth structured error" [

    testCase "no error for a ready session" <| fun _ ->
      DaemonHealth.structuredErrorForFault "Ready" (Some "nope")
      |> Expect.equal "ready session carries no error" None

    testCase "faulted session with reason surfaces structured message" <| fun _ ->
      match DaemonHealth.structuredErrorForFault "Faulted" (Some "worker crashed") with
      | Some err ->
        let props =
          err.GetType().GetProperties()
          |> Array.map (fun p -> p.Name, p.GetValue(err))
          |> Map.ofArray
        props.TryFind "message" |> Expect.isSome "message field present"
        props.TryFind "suggestedAction" |> Expect.isSome "suggestedAction field present"
        props.TryFind "message" |> Option.bind (fun v -> v :?> string |> Some) |> Expect.equal "message carries the reason" (Some "worker crashed")
      | None -> failwith "expected a structured error for the faulted session"

    testCase "faulted session without reason carries no error" <| fun _ ->
      DaemonHealth.structuredErrorForFault "Faulted" None
      |> Expect.equal "no reason means no error object" None

    testCase "stopped session with reason surfaces structured message" <| fun _ ->
      DaemonHealth.structuredErrorForFault "Stopped" (Some "session ended")
      |> Expect.isSome "stopped session with reason should surface an error"

    testCase "warming session with reason carries no error" <| fun _ ->
      DaemonHealth.structuredErrorForFault "Warming Up" (Some "still warming")
      |> Expect.equal "warming is not a fault" None
  ]

[<Tests>]
let processWatchdogAndUnleasedAnomalyTests =
  let makeProc pid ppid pgid state startTicks rssBytes comm (argv: string list) : Bozzetto.ProcessWatchdog.ProcRecord =
    { Pid = pid; Ppid = ppid; Pgid = pgid; State = state; StartTimeTicks = startTicks
      ResidentBytes = rssBytes; Comm = comm; Argv = argv; Cmdline = String.concat " " argv
      StartTimeMsOpt = Some(1_700_000_000_000L + (startTicks * 1000L) / 100L) }

  let startEphemeralLeaseStub () =
    let probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
    probe.Start()
    let port = (probe.LocalEndpoint :?> System.Net.IPEndPoint).Port
    probe.Stop()
    let listener = new System.Net.HttpListener()
    listener.Prefixes.Add(sprintf "http://127.0.0.1:%d/" port)
    listener.Start()
    let gate = obj ()
    let mutable startCount = 0
    let finishedResults = ResizeArray<Bozzetto.Web.Shared.Protocol.CommandObservationResult>()
    let cts = new System.Threading.CancellationTokenSource()
    let loopTask =
      System.Threading.Tasks.Task.Run(fun () ->
        task {
          while not cts.IsCancellationRequested do
            try
              let! ctx = listener.GetContextAsync().WaitAsync(cts.Token)
              let path = ctx.Request.Url.AbsolutePath
              use ms = new System.IO.MemoryStream()
              do! ctx.Request.InputStream.CopyToAsync(ms)
              let reqBytes = ms.ToArray()
              if path = "/api/lease/request" then
                let expires = DateTimeOffset.UtcNow.AddMinutes(5.0).ToString("O")
                let json = sprintf """{"success":true,"protocolVersion":%d,"decision":"granted","leaseId":"stub-lease-1","expiresAt":"%s"}""" Bozzetto.Web.Shared.Protocol.ProtocolVersion expires
                let out = System.Text.Encoding.UTF8.GetBytes json
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- "application/json"
                do! ctx.Response.OutputStream.WriteAsync(out, 0, out.Length)
              elif path = "/api/lease/renew" then
                let expires = DateTimeOffset.UtcNow.AddMinutes(5.0).ToString("O")
                let json = sprintf """{"success":true,"outcome":"renewed","expiresAt":"%s"}""" expires
                let out = System.Text.Encoding.UTF8.GetBytes json
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- "application/json"
                do! ctx.Response.OutputStream.WriteAsync(out, 0, out.Length)
              elif path = "/api/lease/release" then
                let out = System.Text.Encoding.UTF8.GetBytes """{"success":true,"outcome":"released"}"""
                ctx.Response.StatusCode <- 200
                ctx.Response.ContentType <- "application/json"
                do! ctx.Response.OutputStream.WriteAsync(out, 0, out.Length)
              elif path = "/api/lease/command/start" || path = "/api/lease/command/finish" then
                match Bozzetto.Web.Shared.BinaryCodec.decodeCommandObservationRequest reqBytes with
                | Ok req ->
                  let replyBytes, status =
                    match path, req.Observation with
                    | "/api/lease/command/start", Bozzetto.Web.Shared.Protocol.CommandStarted ->
                      lock gate (fun () -> startCount <- startCount + 1)
                      Bozzetto.Web.Shared.BinaryCodec.encodeCommandObservationReply (Bozzetto.Web.Shared.Protocol.CommandObservationAccepted 1L), 200
                    | "/api/lease/command/finish", Bozzetto.Web.Shared.Protocol.CommandFinished result ->
                      if result.StandardOutput.Length > 16384 + 64 || result.StandardError.Length > 16384 + 64 then
                        Bozzetto.Web.Shared.BinaryCodec.encodeCommandObservationReply (Bozzetto.Web.Shared.Protocol.CommandObservationRefused "stderr/stdout exceeded wire limit"), 400
                      else
                        lock gate (fun () -> finishedResults.Add result)
                        Bozzetto.Web.Shared.BinaryCodec.encodeCommandObservationReply (Bozzetto.Web.Shared.Protocol.CommandObservationAccepted 2L), 200
                    | _ ->
                      Bozzetto.Web.Shared.BinaryCodec.encodeCommandObservationReply (Bozzetto.Web.Shared.Protocol.CommandObservationRefused "unexpected phase"), 400
                  ctx.Response.StatusCode <- status
                  ctx.Response.ContentType <- Bozzetto.Web.Shared.Protocol.CommandObservationMediaType
                  do! ctx.Response.OutputStream.WriteAsync(replyBytes, 0, replyBytes.Length)
                | Error err ->
                  let replyBytes = Bozzetto.Web.Shared.BinaryCodec.encodeCommandObservationReply (Bozzetto.Web.Shared.Protocol.CommandObservationRefused err)
                  ctx.Response.StatusCode <- 400
                  do! ctx.Response.OutputStream.WriteAsync(replyBytes, 0, replyBytes.Length)
              else
                ctx.Response.StatusCode <- 404
              ctx.Response.OutputStream.Close()
            with _ -> ()
        } :> System.Threading.Tasks.Task)
    let stopAsync () = async {
      cts.Cancel()
      try listener.Stop() with _ -> ()
      try listener.Close() with _ -> ()
      try do! loopTask.WaitAsync(TimeSpan.FromSeconds 1.0) |> Async.AwaitTask with _ -> ()
      cts.Dispose()
    }
    port, (fun () -> lock gate (fun () -> startCount)), (fun () -> lock gate (fun () -> Seq.toList finishedResults)), stopAsync

  testSequencedGroup "lease-watch-global" (testList "Process watchdog fuses and unleased subprocess telemetry" [

    testCase "parseProcStat handles spaces and parentheses, shares parser with LinuxResources, and parses NUL argv tokens" <| fun _ ->
      let stat = "4242 (dotnet (worker) 1) S 100 4242 4242 0 -1 4194304 0 0 0 0 10 5 0 0 20 0 1 0 123456 0 512"
      let bootMs = 1_700_000_000_000L
      let parsed = Bozzetto.ProcessWatchdog.parseProcStat 4096L (Some bootMs) 100L stat
      parsed |> Expect.isSome "stat line should parse"
      let p = parsed.Value
      p.Comm |> Expect.equal "comm preserves inner parens and spaces" "dotnet (worker) 1"
      p.Ppid |> Expect.equal "ppid" 100
      p.Pgid |> Expect.equal "pgid" 4242
      p.State |> Expect.equal "state" 'S'
      p.StartTimeTicks |> Expect.equal "starttime ticks" 123456L
      p.ResidentBytes |> Expect.equal "rss pages * pageSize" (512L * 4096L)
      p.StartTimeMsOpt |> Expect.equal "computed start ms" (Some(bootMs + 1_234_560L))
      let lr = Bozzetto.Server.LinuxResources.parseProcess 4096 stat |> Option.get
      lr.Name |> Expect.equal "LinuxResources shares exact comm parse" p.Comm
      lr.Resident |> Expect.equal "LinuxResources shares exact rss parse" p.ResidentBytes
      lr.Ticks |> Expect.equal "LinuxResources extracts utime+stime" 15L
      Bozzetto.ProcessWatchdog.parseCmdlineArgv "dotnet\u0000build\u0000Foo.fsproj\u0000"
      |> Expect.equal "argv splits on NUL" [ "dotnet"; "build"; "Foo.fsproj" ]

    testCase "evaluateProcessLiveness matches OwnerMonitor tolerance and distinguishes Alive, Exited, PidReused, and Unknown" <| fun _ ->
      Bozzetto.ProcessWatchdog.startTimeToleranceMs
      |> Expect.equal "tolerance aligns with OwnerMonitor" (Bozzetto.OwnerMonitor.startTimeToleranceTicks / TimeSpan.TicksPerMillisecond)
      let bootMs = 1_700_000_000_000L
      let live = makeProc 99 1 99 'S' 5000L 4096L "dotnet" [ "dotnet"; "build" ]
      let expectedMs = bootMs + 50_000L
      Bozzetto.ProcessWatchdog.evaluateProcessLiveness expectedMs (Bozzetto.ProcessWatchdog.ProcProbeObservation.Present live)
      |> Expect.equal "matching start time is Alive" Bozzetto.ProcessWatchdog.ProcessLiveness.Alive
      Bozzetto.ProcessWatchdog.evaluateProcessLiveness expectedMs Bozzetto.ProcessWatchdog.ProcProbeObservation.Absent
      |> Expect.equal "missing proc is Exited" Bozzetto.ProcessWatchdog.ProcessLiveness.Exited
      let zombie = { live with State = 'Z' }
      Bozzetto.ProcessWatchdog.evaluateProcessLiveness expectedMs (Bozzetto.ProcessWatchdog.ProcProbeObservation.Present zombie)
      |> Expect.equal "zombie is Exited" Bozzetto.ProcessWatchdog.ProcessLiveness.Exited
      let reused = { live with StartTimeTicks = 99999L; StartTimeMsOpt = Some(bootMs + 999_990L) }
      Bozzetto.ProcessWatchdog.evaluateProcessLiveness expectedMs (Bozzetto.ProcessWatchdog.ProcProbeObservation.Present reused)
      |> Expect.equal "reused PID reports observed start ms" (Bozzetto.ProcessWatchdog.ProcessLiveness.PidReused(bootMs + 999_990L))
      Bozzetto.ProcessWatchdog.evaluateProcessLiveness expectedMs (Bozzetto.ProcessWatchdog.ProcProbeObservation.Inaccessible "EACCES")
      |> Expect.equal "inaccessible proc is Unknown" (Bozzetto.ProcessWatchdog.ProcessLiveness.Unknown "EACCES")
      match Bozzetto.ProcessWatchdog.evaluateProcessLiveness expectedMs (Bozzetto.ProcessWatchdog.ProcProbeObservation.Present { live with StartTimeMsOpt = None }) with
      | Bozzetto.ProcessWatchdog.ProcessLiveness.Unknown _ -> ()
      | other -> failtestf "expected Unknown when start time cannot be verified, got %A" other

    testCase "safeTargetPgid fails closed when selfPgid is None or equals targetPgid" <| fun _ ->
      Bozzetto.ProcessWatchdog.safeTargetPgid (Some 4242) None
      |> Expect.equal "unknown selfPgid fails closed" None
      Bozzetto.ProcessWatchdog.safeTargetPgid (Some 4242) (Some 4242)
      |> Expect.equal "matching selfPgid fails closed" None
      Bozzetto.ProcessWatchdog.safeTargetPgid (Some 0) (Some 100)
      |> Expect.equal "non-positive targetPgid fails closed" None
      Bozzetto.ProcessWatchdog.safeTargetPgid (Some 4242) (Some 100)
      |> Expect.equal "distinct positive targetPgid is admitted" (Some 4242)

    testCase "WatchdogCli enforces Layer B canary presets, flag-order precedence, MB overflow guards, strict env validation, and bounded stderr receipts" <| fun _ ->
      let limits: Bozzetto.ProcessWatchdog.FuseLimits =
        { WallLimitSeconds = Some 30.0; RssLimitBytes = Some (512L * 1_048_576L) }
      Bozzetto.ProcessWatchdog.evaluateFuse limits 10.0 (200L * 1_048_576L)
      |> Expect.equal "within both limits" None
      Bozzetto.ProcessWatchdog.evaluateFuse limits 30.5 (200L * 1_048_576L)
      |> Expect.equal "wall breach reported" (Some(Bozzetto.ProcessWatchdog.FuseBreach.WallLimitExceeded(30.5, 30.0)))
      Bozzetto.ProcessWatchdog.evaluateFuse limits 12.0 (600L * 1_048_576L)
      |> Expect.equal "rss breach reported" (Some(Bozzetto.ProcessWatchdog.FuseBreach.RssLimitExceeded(600L * 1_048_576L, 512L * 1_048_576L)))
      Bozzetto.Build.WatchdogCli.tryCanaryPreset "hello"
      |> Expect.equal "Canary 1 Phase 1 preset" (Some { WallLimitSeconds = Some 15.0; RssLimitBytes = Some(512L * 1_048_576L) })
      Bozzetto.Build.WatchdogCli.tryCanaryPreset "hello-p3"
      |> Expect.equal "Canary 1 Phase 3 preset" (Some { WallLimitSeconds = Some 5.0; RssLimitBytes = Some(384L * 1_048_576L) })
      Bozzetto.Build.WatchdogCli.tryCanaryPreset "sqlite"
      |> Expect.equal "Canary 2 preset" (Some { WallLimitSeconds = Some 15.0; RssLimitBytes = Some(768L * 1_048_576L) })
      Bozzetto.Build.WatchdogCli.tryCanaryPreset "challenge"
      |> Expect.equal "Canary 3 preset" (Some { WallLimitSeconds = Some 30.0; RssLimitBytes = Some(1228L * 1_048_576L) })
      Bozzetto.Build.WatchdogCli.tryCanaryPreset "challenge-p3"
      |> Expect.equal "Canary 3 Phase 3 preset" (Some { WallLimitSeconds = Some 15.0; RssLimitBytes = Some(1024L * 1_048_576L) })
      Bozzetto.Build.WatchdogCli.tryCanaryPreset "unknown-canary"
      |> Expect.equal "unknown canary preset rejected" None

      // Explicit CLI limit precedence regardless of argument order (--wall-limit-secs BEFORE --canary).
      let envClean = Bozzetto.Build.WatchdogCli.parseWatchdogEnv None None None None |> Result.defaultWith (fun e -> failtestf "%A" e)
      match Bozzetto.Build.WatchdogCli.parseWatchdogArgs envClean [ "--wall-limit-secs"; "1"; "--canary"; "hello"; "full_build"; "dotnet" ] with
      | Ok(opts, rest) ->
        opts.Limits.WallLimitSeconds |> Expect.equal "explicit --wall-limit-secs beats later --canary" (Some 1.0)
        opts.Limits.RssLimitBytes |> Expect.equal "--canary still supplies unspecified RSS ceiling" (Some(512L * 1_048_576L))
        rest |> Expect.equal "remaining command tokens preserved" [ "full_build"; "dotnet" ]
      | Error err -> failtestf "Expected valid parse, got %A" err

      // Negative controls: invalid env vars and int64 MB overflow refuse loudly.
      Bozzetto.Build.WatchdogCli.parseWatchdogEnv (Some "bad-canary") None None None
      |> Expect.equal "invalid BOZZETTO_CANARY refused" (Error(Bozzetto.Build.WatchdogCli.WatchdogOptionError.UnknownCanaryPreset "bad-canary"))
      Bozzetto.Build.WatchdogCli.WatchdogOptionError.describe (Bozzetto.Build.WatchdogCli.WatchdogOptionError.UnknownCanaryPreset "bad-canary")
      |> Expect.stringContains "unknown canary error lists challenge-p3" "challenge-p3"
      Bozzetto.Build.WatchdogCli.parseWatchdogEnv None (Some "not-a-number") None None
      |> Expect.equal "invalid BOZZETTO_WALL_LIMIT_SECS refused" (Error(Bozzetto.Build.WatchdogCli.WatchdogOptionError.InvalidPositiveFloat("BOZZETTO_WALL_LIMIT_SECS", "not-a-number")))
      Bozzetto.Build.WatchdogCli.parseWatchdogEnv None None (Some "-5") None
      |> Expect.equal "negative BOZZETTO_RSS_LIMIT_MB refused" (Error(Bozzetto.Build.WatchdogCli.WatchdogOptionError.InvalidPositiveInt64("BOZZETTO_RSS_LIMIT_MB", "-5")))
      Bozzetto.Build.WatchdogCli.parseWatchdogArgs envClean [ "--rss-limit-mb"; "9000000000000"; "full_build"; "dotnet" ]
      |> Expect.equal "int64 MB overflow refused" (Error(Bozzetto.Build.WatchdogCli.WatchdogOptionError.InvalidPositiveInt64("--rss-limit-mb", "9000000000000")))

      // Bounded stderr receipt control: 16,422-char stderr + 160-char receipt stays inside McpServer's 16,384 + 64 cap.
      let fullTail = "[cut: earlier command output omitted]\n" + String('e', 16384)
      let receiptLine = "WATCHDOG rootPid=999 pgid=999 elapsedMs=1200 peakRssMB=42.0 peakRssBytes=44040192 breach=wall_limit_exceeded(1.20s>=1.00s) exitCode=124 survivors=none"
      let bounded, truncated = Bozzetto.Build.WatchdogCli.appendBoundedReceipt 16384 fullTail receiptLine true
      (bounded.Length <= 16384 + 64) |> Expect.isTrue (sprintf "bounded stderr length %d must fit within wire limit" bounded.Length)
      truncated |> Expect.isTrue "truncated flag preserved"
      bounded.EndsWith(receiptLine, StringComparison.Ordinal) |> Expect.isTrue "WATCHDOG receipt line preserved intact at tail"

    testCase "classifyWatchedSubprocess, exact lease attribution, and OperationalFault wire shape handle dotnet exec/flags, RunApp leases, and orphaned subtrees under MCP leases" <| fun _ ->
      // Exact argv classification controls:
      Bozzetto.ProcessWatchdog.classifyWatchedSubprocess "dotnet" [ "dotnet"; "fsi"; "tools/build/lease.fsx"; "full_build"; "dotnet"; "build"; "Foo.fsproj" ]
      |> Expect.equal "lease.fsx running dotnet build never self-flags" None
      Bozzetto.ProcessWatchdog.classifyWatchedSubprocess "dotnet" [ "dotnet"; "fsi"; "tools/build/lease.fsx"; "full_build"; "dotnet"; "Composer.dll"; "compile" ]
      |> Expect.equal "lease.fsx running Composer.dll never self-flags" None
      Bozzetto.ProcessWatchdog.classifyWatchedSubprocess "git" [ "git"; "-C"; "/home/hhh/repos/Composer"; "status" ]
      |> Expect.equal "git referencing Composer checkout never false-positives" None
      Bozzetto.ProcessWatchdog.classifyWatchedSubprocess "bash" [ "bash"; "-c"; "dotnet build Foo.fsproj" ]
      |> Expect.equal "shell wrapper never false-positives" None
      Bozzetto.ProcessWatchdog.classifyWatchedSubprocess "dotnet" [ "dotnet"; "/usr/share/dotnet/sdk/10.0/MSBuild.dll"; "/nodemode:1" ]
      |> Expect.equal "MSBuild worker node is classified" (Some Bozzetto.ProcessWatchdog.WatchedProcessRole.DotnetBuildOrTest)
      Bozzetto.ProcessWatchdog.classifyWatchedSubprocess "dotnet" [ "dotnet"; "exec"; "/home/hhh/repos/Composer/bin/Composer.dll"; "compile"; "Foo.fidproj" ]
      |> Expect.equal "dotnet exec Composer.dll is classified" (Some Bozzetto.ProcessWatchdog.WatchedProcessRole.Composer)
      Bozzetto.ProcessWatchdog.classifyWatchedSubprocess "dotnet" [ "dotnet"; "--roll-forward"; "Major"; "--fx-version"; "10.0.0"; "/home/hhh/repos/Composer/bin/Composer.dll"; "compile" ]
      |> Expect.equal "dotnet with two-token host flags is classified" (Some Bozzetto.ProcessWatchdog.WatchedProcessRole.Composer)

      let procs = [
        makeProc 1 0 1 'S' 1L (10L * 1_048_576L) "systemd" [ "/sbin/init" ]
        makeProc 5 1 5 'S' 5L (20L * 1_048_576L) "systemd" [ "/usr/lib/systemd/systemd"; "--user" ]
        makeProc 10 5 10 'S' 10L (100L * 1_048_576L) "boz" [ "dotnet"; "Bozzetto.dll" ]
        makeProc 11 10 11 'S' 20L (200L * 1_048_576L) "dotnet" [ "dotnet"; "Bozzetto.Composer.dll" ]
        makeProc 19 5 19 'S' 29L (80L * 1_048_576L) "dotnet" [ "dotnet"; "fsi"; "tools/build/lease.fsx"; "full_build"; "dotnet"; "build" ]
        makeProc 20 19 20 'S' 30L (50L * 1_048_576L) "setsid" [ "setsid"; "--wait"; "dotnet"; "build" ]
        makeProc 21 20 20 'S' 31L (400L * 1_048_576L) "dotnet" [ "dotnet"; "build"; "Composer.sln" ]
        makeProc 22 21 20 'S' 32L (120L * 1_048_576L) "cvc5" [ "/usr/bin/cvc5"; "--lang"; "smt2" ]
        makeProc 30 5 30 'S' 40L (300L * 1_048_576L) "dotnet" [ "dotnet"; "ClefAutoComplete.dll" ]
        // Live non-orphaned MCP build (parent = node pid 35, not init/systemd --user)
        makeProc 35 5 35 'S' 45L (60L * 1_048_576L) "node" [ "node"; "mcp-lease-run.mjs"; "full_build"; "dotnet"; "build" ]
        makeProc 36 35 36 'S' 46L (320L * 1_048_576L) "dotnet" [ "dotnet"; "build"; "Bozzetto.fsproj" ]
        // Detached orphaned Composer (reparented to systemd --user pid 5) + child cvc5 (pid 41 -> ppid 40)
        makeProc 40 5 40 'S' 50L (1400L * 1_048_576L) "Composer" [ "/home/hhh/repos/Composer/bin/Composer"; "compile"; "Foo.fidproj" ]
        makeProc 41 40 40 'S' 51L (250L * 1_048_576L) "cvc5" [ "/usr/bin/cvc5"; "--incremental" ]
        // Orphaned MSBuild sharing daemon PGID 10 (reparented to PID 1)
        makeProc 42 1 10 'S' 52L (180L * 1_048_576L) "dotnet" [ "dotnet"; "/sdk/MSBuild.dll"; "/nodemode:1" ]
        // Detached shell wrapper chain: systemd --user (5) -> bash (50, pgid 50) -> dotnet Composer.dll (51, pgid 50) -> cvc5 (52, pgid 52)
        makeProc 50 5 50 'S' 60L (8L * 1_048_576L) "bash" [ "bash"; "-c"; "dotnet Composer.dll compile" ]
        makeProc 51 50 50 'S' 61L (500L * 1_048_576L) "dotnet" [ "dotnet"; "/home/hhh/repos/Composer/bin/Composer.dll"; "compile" ]
        makeProc 52 51 52 'S' 62L (180L * 1_048_576L) "cvc5" [ "/usr/bin/cvc5"; "--lang"; "smt2" ]
        // Orphaned MSBuild (61) sharing a live external agent shell's PGID (60, owned by live parent 59)
        makeProc 59 5 59 'S' 70L (40L * 1_048_576L) "node" [ "node"; "agent.mjs" ]
        makeProc 60 59 60 'S' 71L (12L * 1_048_576L) "bash" [ "bash" ]
        makeProc 61 1 60 'S' 72L (140L * 1_048_576L) "dotnet" [ "dotnet"; "/sdk/MSBuild.dll"; "/nodemode:1" ]
        // Reparented MSBuild (37) sharing active MCP FullBuild PGID 35 (node 35 -> dotnet build 36)
        makeProc 37 1 35 'S' 47L (110L * 1_048_576L) "dotnet" [ "dotnet"; "/sdk/MSBuild.dll"; "/nodemode:1" ]
      ]
      // Positive control: owned + leased + editor LS only -> clean.
      let cleanProcs = procs |> List.filter (fun p -> p.Pid < 35)
      Bozzetto.Server.DaemonTelemetry.detectUnleasedSubprocesses (Set.ofList [ 10; 11 ]) (Set.ofList [ 20 ]) 0 cleanProcs
      |> Expect.isEmpty "lease.fsx, leased subtree, owned worker, and language server are clean"

      // MCP lease control: active untracked FullBuild lease spares live non-orphaned MCP build (pid 36),
      // while STILL catching reparented orphaned Composer (40), its child cvc5 (41), orphaned MSBuild (42, 37, 61),
      // shell-wrapped detached bash->Composer.dll (51), AND its own-PGID child cvc5 (52), both resolving OrphanRootPid = Some 50!
      let underMcpLease = Bozzetto.Server.DaemonTelemetry.detectUnleasedSubprocesses (Set.ofList [ 10; 11 ]) (Set.ofList [ 20 ]) 1 procs
      underMcpLease |> List.map (fun p -> p.Pid, p.Role, p.IsOrphaned, p.OrphanRootPid)
      |> Expect.equal "MCP lease spares non-orphaned node->dotnet build while still catching reparented orphans and bash->Composer->cvc5 chains"
           [ 37, Bozzetto.ProcessWatchdog.WatchedProcessRole.DotnetBuildOrTest, true, Some 37
             40, Bozzetto.ProcessWatchdog.WatchedProcessRole.Composer, true, Some 40
             41, Bozzetto.ProcessWatchdog.WatchedProcessRole.SolverCvc5, true, Some 40
             42, Bozzetto.ProcessWatchdog.WatchedProcessRole.DotnetBuildOrTest, true, Some 42
             51, Bozzetto.ProcessWatchdog.WatchedProcessRole.Composer, true, Some 50
             52, Bozzetto.ProcessWatchdog.WatchedProcessRole.SolverCvc5, true, Some 50
             61, Bozzetto.ProcessWatchdog.WatchedProcessRole.DotnetBuildOrTest, true, Some 61 ]

      // Exclusive PGID blast-radius controls:
      // 1. Dedicated orphan root bash(50)->Composer(51) in PGID 50 -> admits Some 50.
      Bozzetto.ProcessWatchdog.exclusiveOrphanPgid 50 50 (Set.ofList [ 50; 51 ]) procs
      |> Expect.equal "exclusive orphan wrapper PGID 50 is safe for -pgid" (Some 50)
      // 2. Orphaned MSBuild(61) sharing live external shell PGID 60 -> MUST return None (shell survives!).
      Bozzetto.ProcessWatchdog.exclusiveOrphanPgid 61 60 (Set.ofList [ 61 ]) procs
      |> Expect.equal "shared external shell PGID 60 is refused for -pgid" None
      // 3. Reparented MSBuild(37) sharing active MCP FullBuild PGID 35 -> MUST return None (MCP build survives!).
      Bozzetto.ProcessWatchdog.exclusiveOrphanPgid 37 35 (Set.ofList [ 37 ]) procs
      |> Expect.equal "shared active MCP FullBuild PGID 35 is refused for -pgid" None

      // LeaseWatch.untrackedBuildOrTestLeaseCount: RunApp never suppresses build/compiler detection.
      Bozzetto.Features.LeaseWatch.reset ()
      try
        match Bozzetto.Features.LeaseWatch.request "runapp-holder" Bozzetto.ExpensiveWorkLease.Kind.RunApp with
        | Bozzetto.ExpensiveWorkLease.Decision.Granted _ -> ()
        | other -> failtestf "Expected RunApp grant, got %A" other
        Bozzetto.Features.LeaseWatch.untrackedBuildOrTestLeaseCount Set.empty
        |> Expect.equal "RunApp lease never counts as untracked build/test lease" 0
        let buildLeaseId =
          match Bozzetto.Features.LeaseWatch.request "mcp-holder" Bozzetto.ExpensiveWorkLease.Kind.FullBuild with
          | Bozzetto.ExpensiveWorkLease.Decision.Granted(lid, _) -> Bozzetto.ExpensiveWorkLease.LeaseId.value lid
          | other -> failtestf "Expected FullBuild grant, got %A" other
        // Stale/dead journal leaseId ("dead-lease-99") NEVER subtracts from live MCP lease!
        Bozzetto.Features.LeaseWatch.untrackedBuildOrTestLeaseCount (Set.ofList [ "dead-lease-99" ])
        |> Expect.equal "stale journal lease id does not erase live untracked MCP FullBuild lease" 1
        Bozzetto.Features.LeaseWatch.untrackedBuildOrTestLeaseCount (Set.ofList [ buildLeaseId ])
        |> Expect.equal "matching live tracked lease id brings untracked count to 0" 0
      finally
        Bozzetto.Features.LeaseWatch.reset ()

      // OperationalFault serialization carries zero fabricated EWMA/CUSUM statistical fields.
      let verdict = Bozzetto.Server.DaemonTelemetry.unleasedAnomalyVerdict DateTimeOffset.UtcNow underMcpLease
      verdict |> Expect.isSome "unleased subprocesses produce an OperationalFault verdict"
      Bozzetto.Features.HealthAnomaly.evidenceOf verdict.Value |> Expect.isNone "no fabricated statistical SignalEvidence"
      let snap = {
        DaemonPid = 10; DaemonPort = 47749; Uptime = TimeSpan.FromMinutes 2.0; Version = "0.1.0"
        SessionSummaries = []; LiveTestingSummary = None; MemoryMB = 200
        Anomalies = Option.toList verdict; GcDumpOutcome = None; MemoryPressure = Bozzetto.MemoryPressure.Normal
      }
      DaemonHealth.overallStatus snap |> Expect.equal "OperationalFault degrades overall health to Unhealthy" OverallHealth.Unhealthy
      let json = Bozzetto.DaemonStatusPayload.serializeWithLiveness 47749 (Some snap) None []
      json |> Expect.stringContains "status payload surfaces stable signal" "\"signal\":\"unleased_subprocess\""
      json |> Expect.stringContains "status payload marks operational kind" "\"kind\":\"operational\""
      json |> Expect.stringContains "status payload names escaped Composer pid" "composer(pid=40"
      (json.Contains "\"baselineMean\"") |> Expect.isFalse "OperationalFault must not serialize fabricated baselineMean"
      (json.Contains "\"deviationInSigmas\"") |> Expect.isFalse "OperationalFault must not serialize fabricated deviationInSigmas"

    testCaseAsync "fault injection (#35): real sh->Composer->sleep detached orphan surfaces under active MCP lease, non-exclusive PGID spares live sibling, and O(1) disconnect wakeup leaves zero /proc survivors" (async {
      if OperatingSystem.IsLinux() then
        match Bozzetto.Server.LinuxProcessEdge.tryResolveSetsid () with
        | None -> failtest "setsid must be available on Linux test host"
        | Some setsidPath ->
          Bozzetto.Features.LeaseWatch.reset ()
          Bozzetto.Server.DaemonTelemetry.resetUnleasedState ()
          let tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boz-orphan-composer-" + Guid.NewGuid().ToString("N"))
          System.IO.Directory.CreateDirectory tempDir |> ignore
          let fakeComposer = System.IO.Path.Combine(tempDir, "Composer")
          let composerPidFile = System.IO.Path.Combine(tempDir, "composer.pid")
          let trapChildPidFile = System.IO.Path.Combine(tempDir, "trap-child.pid")
          let setsidTrapPidFile = System.IO.Path.Combine(tempDir, "setsid-trap.pid")
          let sharedOrphanPidFile = System.IO.Path.Combine(tempDir, "shared-orphan.pid")
          let sharedTrapChildPidFile = System.IO.Path.Combine(tempDir, "shared-trap-child.pid")
          let sharedSiblingPidFile = System.IO.Path.Combine(tempDir, "shared-sibling.pid")
          let fakeComposerBody =
            "#!/bin/sh\n"
            + "if [ -n \"$BOZ_TRAP_PID_FILE\" ]; then\n"
            + "  /bin/sh -c 'trap \"\" TERM; echo $$ > \"'$BOZ_TRAP_PID_FILE'\"; exec /bin/sleep 60' </dev/null >/dev/null 2>&1 &\n"
            + "fi\n"
            + "if [ -n \"$BOZ_SETSID_TRAP_PID_FILE\" ]; then\n"
            + "  " + setsidPath + " /bin/sh -c 'trap \"\" TERM; echo $$ > \"'$BOZ_SETSID_TRAP_PID_FILE'\"; exec /bin/sleep 60' </dev/null >/dev/null 2>&1 &\n"
            + "fi\n"
            + "/bin/sleep 60 &\n"
            + "wait\n"
          System.IO.File.WriteAllText(fakeComposer, fakeComposerBody)
          System.IO.File.SetUnixFileMode(
            fakeComposer,
            System.IO.UnixFileMode.UserRead ||| System.IO.UnixFileMode.UserWrite ||| System.IO.UnixFileMode.UserExecute)
          // Verify self-PGID guard protects unrelated sibling in caller's own PGID.
          let selfPgid = Bozzetto.Server.LinuxProcessEdge.readLinuxProc Environment.ProcessId |> Option.map _.Pgid
          let sibPsi = System.Diagnostics.ProcessStartInfo("/bin/sleep")
          sibPsi.ArgumentList.Add "60"
          sibPsi.UseShellExecute <- false
          use unrelatedSibling = System.Diagnostics.Process.Start sibPsi
          let mutable wrapperPgid = 0
          let mutable trapChildPid = 0
          let mutable setsidTrapPid = 0
          let mutable sharedGroupLeaderPid = 0
          let mutable sharedOrphanPid = 0
          let mutable sharedTrapChildPid = 0
          let mutable sharedLiveSiblingPid = 0
          try
            let! guardOutcome = Bozzetto.Server.LinuxProcessEdge.terminateProcessGroupAsync 999999999 selfPgid 100
            guardOutcome |> Expect.equal "non-existent root with caller PGID touches nothing" Bozzetto.ProcessWatchdog.TerminationOutcome.Cleared
            unrelatedSibling.HasExited |> Expect.isFalse "unrelated sibling in caller PGID must stay alive"

            // PID-reuse StartTimeTicks negative controls: neither terminateFencedProcessGroupAsync nor
            // terminateProcessGroupWithFenceAsync may signal a live process with mismatched StartTimeTicks.
            let sibActualTicks =
              Bozzetto.Server.LinuxProcessEdge.readLinuxProc unrelatedSibling.Id
              |> Option.map _.StartTimeTicks
              |> Option.defaultWith (fun () -> failtest "expected /proc stat for live unrelatedSibling")
            let! fencedMismatch =
              Bozzetto.Server.LinuxProcessEdge.terminateFencedProcessGroupAsync
                unrelatedSibling.Id (Some unrelatedSibling.Id) (sibActualTicks + 99999L) 100
            fencedMismatch |> Expect.equal "terminateFencedProcessGroupAsync refuses mismatched StartTimeTicks" Bozzetto.ProcessWatchdog.TerminationOutcome.Cleared
            unrelatedSibling.HasExited |> Expect.isFalse "mismatched terminateFencedProcessGroupAsync leaves unrelated live process alive"
            let! groupFenceMismatch =
              Bozzetto.Server.LinuxProcessEdge.terminateProcessGroupWithFenceAsync
                unrelatedSibling.Id (Some unrelatedSibling.Id) (Some(sibActualTicks + 99999L)) 100
            groupFenceMismatch |> Expect.equal "terminateProcessGroupWithFenceAsync refuses mismatched StartTimeTicks" Bozzetto.ProcessWatchdog.TerminationOutcome.Cleared
            unrelatedSibling.HasExited |> Expect.isFalse "mismatched terminateProcessGroupWithFenceAsync leaves unrelated live process alive"

            // Acquire an active untracked MCP FullBuild lease to prove it does NOT blind orphan detection.
            match Bozzetto.Features.LeaseWatch.request "mcp:active-other-agent" Bozzetto.ExpensiveWorkLease.Kind.FullBuild with
            | Bozzetto.ExpensiveWorkLease.Decision.Granted _ -> ()
            | other -> failtestf "Expected active MCP FullBuild lease, got %A" other

            // 1. Spawn detached `setsid /bin/sh -c "<fakeComposer>"` -> real systemd --user -> sh -> Composer ->
            //    { sleep 60, SIGTERM-ignoring child, AND SIGTERM-ignoring setsid grandchild in its OWN PGID }!
            let launcherPsi = System.Diagnostics.ProcessStartInfo("/bin/sh")
            launcherPsi.ArgumentList.Add "-c"
            launcherPsi.ArgumentList.Add(
              sprintf "%s /bin/sh -c 'BOZ_TRAP_PID_FILE=\"%s\" BOZ_SETSID_TRAP_PID_FILE=\"%s\" %s & echo $! > \"%s\"; wait' </dev/null >/dev/null 2>&1 & echo $!"
                setsidPath trapChildPidFile setsidTrapPidFile fakeComposer composerPidFile)
            launcherPsi.RedirectStandardOutput <- true
            launcherPsi.UseShellExecute <- false
            use launcher = System.Diagnostics.Process.Start launcherPsi
            let! pidText = launcher.StandardOutput.ReadToEndAsync() |> Async.AwaitTask
            do! launcher.WaitForExitAsync() |> Async.AwaitTask
            wrapperPgid <- Int32.Parse(pidText.Trim())

            // 2. Spawn a SEPARATE non-exclusive PGID containing BOTH a live non-watched sibling (/bin/sleep 60)
            //    AND a reparented orphaned fakeComposer (which spawns its own SIGTERM-ignoring child!) sharing that PGID.
            //    Cleanup MUST kill the orphaned fakeComposer AND escalate SIGKILL to its reparented SIGTERM-ignoring child
            //    while sparing the live non-watched sibling!
            let sharedPsi = System.Diagnostics.ProcessStartInfo(setsidPath)
            sharedPsi.ArgumentList.Add "/bin/sh"
            sharedPsi.ArgumentList.Add "-c"
            sharedPsi.ArgumentList.Add(
              sprintf "/bin/sleep 60 & echo $! > \"%s\"; (/bin/sh -c 'BOZ_TRAP_PID_FILE=\"%s\" %s </dev/null >/dev/null 2>&1 & echo $! > \"%s\"'); wait"
                sharedSiblingPidFile sharedTrapChildPidFile fakeComposer sharedOrphanPidFile)
            sharedPsi.UseShellExecute <- false
            use sharedGroupProc = System.Diagnostics.Process.Start sharedPsi
            sharedGroupLeaderPid <- sharedGroupProc.Id

            let tryReadPid path =
              if System.IO.File.Exists path then
                match Int32.TryParse(System.IO.File.ReadAllText(path).Trim()) with
                | true, v when v > 0 -> v
                | _ -> 0
              else 0

            // Wait for wrapper chain + all SIGTERM-ignoring descendants + shared orphan/sibling PID files.
            let mutable subtree = Set.empty
            let mutable composerPid = 0
            let mutable setsidGrandchildPgid = 0
            let deadline = DateTimeOffset.UtcNow.AddSeconds 5.0
            while (Set.count subtree < 5 || composerPid = 0 || trapChildPid = 0 || setsidTrapPid = 0 || setsidGrandchildPgid = 0 || sharedOrphanPid = 0 || sharedTrapChildPid = 0 || sharedLiveSiblingPid = 0) && DateTimeOffset.UtcNow < deadline do
              do! Async.Sleep 50
              if composerPid = 0 then composerPid <- tryReadPid composerPidFile
              if trapChildPid = 0 then trapChildPid <- tryReadPid trapChildPidFile
              if setsidTrapPid = 0 then setsidTrapPid <- tryReadPid setsidTrapPidFile
              if sharedOrphanPid = 0 then sharedOrphanPid <- tryReadPid sharedOrphanPidFile
              if sharedTrapChildPid = 0 then sharedTrapChildPid <- tryReadPid sharedTrapChildPidFile
              if sharedLiveSiblingPid = 0 then sharedLiveSiblingPid <- tryReadPid sharedSiblingPidFile
              match Bozzetto.Server.LinuxProcessEdge.readLinuxProcesses false with
              | Ok procs ->
                subtree <- Bozzetto.ProcessWatchdog.subtreePids wrapperPgid (Some wrapperPgid) procs
                if setsidTrapPid > 0 then
                  procs
                  |> List.tryFind (fun p -> p.Pid = setsidTrapPid && Bozzetto.ProcessWatchdog.isLiveRecord p)
                  |> Option.iter (fun p -> setsidGrandchildPgid <- p.Pgid)
              | Error err -> failtestf "Unexpected /proc scan error: %A" err
            (Set.count subtree >= 5 && composerPid > 0 && trapChildPid > 0 && setsidTrapPid > 0 && sharedOrphanPid > 0 && sharedTrapChildPid > 0 && sharedLiveSiblingPid > 0)
            |> Expect.isTrue (sprintf "expected wrapper subtree (>=5, composer=%d, trap=%d, setsidTrap=%d) and shared group (orphan=%d, sharedTrap=%d, sibling=%d), saw %A"
                                composerPid trapChildPid setsidTrapPid sharedOrphanPid sharedTrapChildPid sharedLiveSiblingPid subtree)
            (setsidGrandchildPgid > 0 && setsidGrandchildPgid <> wrapperPgid)
            |> Expect.isTrue (sprintf "setsid grandchild pid %d must occupy its own PGID distinct from wrapperPgid %d (saw %d)" setsidTrapPid wrapperPgid setsidGrandchildPgid)

            let getInputs () =
              let owned = Set.ofList [ Environment.ProcessId ]
              let untracked = Bozzetto.Features.LeaseWatch.untrackedBuildOrTestLeaseCount Set.empty
              owned, Set.empty, untracked

            // Step 1: Background unleased scan surfaces the sh->Composer orphan in get_daemon_status.anomalies.
            let! obs, _ = Bozzetto.Server.DaemonTelemetry.refreshUnleasedAsync getInputs
            match obs.Result with
            | Error err -> failtestf "Unexpected scan error: %A" err
            | Ok items ->
              items |> List.exists (fun p ->
                p.Pid = composerPid
                && p.Role = Bozzetto.ProcessWatchdog.WatchedProcessRole.Composer
                && p.IsOrphaned
                && p.OrphanRootPid = Some wrapperPgid)
              |> Expect.isTrue (sprintf "wrapper-chained Composer pid %d (root=%d) must be detected under active MCP lease; saw %A" composerPid wrapperPgid items)
            let anomalyBefore = Bozzetto.Server.DaemonTelemetry.currentUnleasedVerdict ()
            anomalyBefore |> Expect.isSome "currentUnleasedVerdict surfaces orphaned Composer"
            let snapBefore = {
              DaemonPid = Environment.ProcessId; DaemonPort = 47749; Uptime = TimeSpan.FromMinutes 1.0; Version = "0.1.0"
              SessionSummaries = []; LiveTestingSummary = None; MemoryMB = 100
              Anomalies = Option.toList anomalyBefore; GcDumpOutcome = None; MemoryPressure = Bozzetto.MemoryPressure.Normal
            }
            let statusBefore = Bozzetto.DaemonStatusPayload.serializeWithLiveness 47749 (Some snapBefore) None []
            statusBefore |> Expect.stringContains "get_daemon_status surfaces unleased_subprocess" "\"signal\":\"unleased_subprocess\""
            statusBefore |> Expect.stringContains "get_daemon_status names exact Composer pid" (sprintf "composer(pid=%d" composerPid)
            statusBefore |> Expect.stringContains "get_daemon_status flips overall Unhealthy" "\"overall\":\"Unhealthy\""

            // Step 2: Exercise the exact O(1) wakeup path used by McpServer/DaemonMode on session disconnect.
            use wakeGate = new System.Threading.SemaphoreSlim(0, 1)
            let cleanupResult = System.Threading.Tasks.TaskCompletionSource<Bozzetto.Server.DaemonTelemetry.OrphanCleanupReport>()
            Bozzetto.Server.DaemonTelemetry.installSessionDisconnectWakeup (fun () ->
              if wakeGate.CurrentCount = 0 then wakeGate.Release() |> ignore)
            let bgMonitor =
              Async.StartAsTask(async {
                let! _ = wakeGate.WaitAsync(TimeSpan.FromSeconds 5.0) |> Async.AwaitTask
                let! rep, _ =
                  Bozzetto.Server.DaemonTelemetry.cleanupOrphanedSubprocessesWithCancellationAsync
                    getInputs 0L 500 System.Threading.CancellationToken.None
                cleanupResult.TrySetResult rep |> ignore
              })
            Bozzetto.Server.DaemonTelemetry.notifySessionDisconnected ()
            let! report = cleanupResult.Task.WaitAsync(TimeSpan.FromSeconds 10.0) |> Async.AwaitTask
            do! bgMonitor.WaitAsync(TimeSpan.FromSeconds 5.0) |> Async.AwaitTask
            report.ScanError |> Expect.isNone "cleanup /proc scan succeeded"
            report.Targets |> List.exists (fun t -> t.Pid = wrapperPgid && t.Outcome = Bozzetto.ProcessWatchdog.TerminationOutcome.Cleared)
            |> Expect.isTrue (sprintf "cleanup must terminate wrapper root %d with Cleared; targets=%A" wrapperPgid report.Targets)
            report.Targets |> List.exists (fun t -> t.Pid = sharedOrphanPid && t.Outcome = Bozzetto.ProcessWatchdog.TerminationOutcome.Cleared)
            |> Expect.isTrue (sprintf "cleanup must terminate non-exclusive shared-PGID orphan %d with Cleared; targets=%A" sharedOrphanPid report.Targets)

            // Step 3: Verify independently in /proc BY EXACT CHILD PID that EVERY SIGTERM-ignoring
            // reparented descendant (same-PGID, child-setsid PGID, AND non-exclusive shared-PGID) was
            // escalated to SIGKILL and killed, while the unrelated shared-PGID live sibling SURVIVED!
            let afterProcs =
              match Bozzetto.Server.LinuxProcessEdge.readLinuxProcesses false with
              | Ok procs -> procs
              | Error err -> failtestf "Unexpected post-cleanup /proc error: %A" err
            let isPidAlive pid = afterProcs |> List.exists (fun p -> p.Pid = pid && Bozzetto.ProcessWatchdog.isLiveRecord p)
            Bozzetto.ProcessWatchdog.subtreePids wrapperPgid (Some wrapperPgid) afterProcs
            |> Set.isEmpty
            |> Expect.isTrue "post-cleanup /proc scan proves zero surviving sh/Composer/grandchild descendants"
            isPidAlive trapChildPid |> Expect.isFalse (sprintf "SIGTERM-ignoring wrapper child pid %d must be SIGKILL-escalated and gone" trapChildPid)
            isPidAlive setsidTrapPid |> Expect.isFalse (sprintf "SIGTERM-ignoring setsid grandchild pid %d (own PGID %d) must stay tracked across parent exit and be SIGKILL-escalated" setsidTrapPid setsidGrandchildPgid)
            isPidAlive sharedOrphanPid |> Expect.isFalse (sprintf "non-exclusive shared-PGID orphan Composer pid %d must be gone" sharedOrphanPid)
            isPidAlive sharedTrapChildPid |> Expect.isFalse (sprintf "SIGTERM-ignoring child pid %d reparented out of non-exclusive shared-PGID orphan must stay tracked and be SIGKILL-escalated" sharedTrapChildPid)
            isPidAlive sharedLiveSiblingPid
            |> Expect.isTrue (sprintf "live non-watched sibling pid %d sharing non-exclusive PGID %d MUST survive cleanup" sharedLiveSiblingPid sharedGroupLeaderPid)
            let anomalyAfter = Bozzetto.Server.DaemonTelemetry.currentUnleasedVerdict ()
            match anomalyAfter with
            | Some(HealthAnomaly.Verdict.OperationalFault f) when f.Detail.Contains(sprintf "pid=%d" composerPid) ->
              failtestf "cleaned Composer pid %d must not remain in anomalies: %s" composerPid f.Detail
            | _ -> ()
            unrelatedSibling.HasExited |> Expect.isFalse "unrelated non-watched sibling in caller PGID remained untouched"
          finally
            for pid in [ trapChildPid; setsidTrapPid; sharedTrapChildPid ] do
              if pid > 0 then
                try Bozzetto.Server.LinuxProcessEdge.sendSignalChecked pid Bozzetto.ProcessWatchdog.SigKill |> ignore with _ -> ()
            if wrapperPgid > 0 then
              try Bozzetto.Server.LinuxProcessEdge.signalProcessGroupFast (Some wrapperPgid) Bozzetto.ProcessWatchdog.SigKill |> ignore with _ -> ()
            if sharedGroupLeaderPid > 0 then
              try Bozzetto.Server.LinuxProcessEdge.signalProcessGroupFast (Some sharedGroupLeaderPid) Bozzetto.ProcessWatchdog.SigKill |> ignore with _ -> ()
            try if not unrelatedSibling.HasExited then unrelatedSibling.Kill(true) with _ -> ()
            Bozzetto.Server.DaemonTelemetry.resetUnleasedState ()
            Bozzetto.Features.LeaseWatch.reset ()
            if System.IO.Directory.Exists tempDir then System.IO.Directory.Delete(tempDir, true)
    })

    testCaseAsync "fault injection (#39): lease.fsx enforces bad-env refusal (64), wall fuse + un-truncated receipt + auto CLEF_PHASE_TIMING=1 (124), multi-process RSS fuse (137), and SIGTERM cancellation with independent /proc zero-survivor proofs" (async {
      if OperatingSystem.IsLinux() then
        let repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
        let leaseScript = System.IO.Path.Combine(repoRoot, "tools", "build", "lease.fsx")
        let tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boz-lease-fi-" + Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory tempDir |> ignore
        let port, getStartCount, getFinished, stopStub = startEphemeralLeaseStub ()
        let parseWatchdogIds (text: string) =
          let m = System.Text.RegularExpressions.Regex.Match(text, @"WATCHDOG rootPid=(\d+) pgid=(\d+|none)")
          m.Success |> Expect.isTrue (sprintf "expected WATCHDOG rootPid/pgid line in: %s" text)
          let rootPid = Int32.Parse(m.Groups[1].Value)
          let pgid =
            match Int32.TryParse(m.Groups[2].Value) with
            | true, v -> Some v
            | _ -> None
          rootPid, pgid
        let verifyNoProcSurvivors (label: string) (rootPid: int) (pgid: int option) (marker: string) =
          match Bozzetto.Server.LinuxProcessEdge.readLinuxProcesses true with
          | Error err -> failtestf "%s: post-exit /proc scan failed: %A" label err
          | Ok procs ->
            Bozzetto.ProcessWatchdog.subtreePids rootPid pgid procs
            |> Expect.equal (sprintf "%s: independent /proc scan proves zero subtree/PGID survivors" label) Set.empty
            procs
            |> List.filter (fun p -> Bozzetto.ProcessWatchdog.isLiveRecord p && p.Cmdline.Contains marker)
            |> Expect.isEmpty (sprintf "%s: independent /proc scan proves zero live processes carrying marker %s" label marker)
        let runLease (envExtras: (string * string) list) (args: string list) (sigTermWhenReady: (string * int) option) = async {
          let psi = System.Diagnostics.ProcessStartInfo("dotnet")
          psi.ArgumentList.Add "fsi"
          psi.ArgumentList.Add leaseScript
          for a in args do psi.ArgumentList.Add a
          psi.WorkingDirectory <- repoRoot
          psi.RedirectStandardOutput <- true
          psi.RedirectStandardError <- true
          psi.UseShellExecute <- false
          psi.Environment["BOZZETTO_MCP_PORT"] <- string port
          psi.Environment["BOZZETTO_AGENT"] <- "expecto-watchdog-test"
          psi.Environment["BOZZETTO_PROJECT"] <- repoRoot
          psi.Environment.Remove "CLEF_PHASE_TIMING" |> ignore
          psi.Environment.Remove "CLEF_BUDGET_GATE" |> ignore
          for k, v in envExtras do psi.Environment[k] <- v
          use proc = System.Diagnostics.Process.Start psi
          let readyMarker = sigTermWhenReady |> Option.map fst
          let markerSeen = System.Threading.Tasks.TaskCompletionSource<unit>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously)
          let stdoutTask =
            task {
              let sb = System.Text.StringBuilder()
              let mutable reading = true
              while reading do
                let! line = proc.StandardOutput.ReadLineAsync()
                if isNull line then
                  reading <- false
                else
                  sb.AppendLine line |> ignore
                  match readyMarker with
                  | Some m when line.Contains m -> markerSeen.TrySetResult() |> ignore
                  | _ -> ()
              return sb.ToString()
            }
          let stderrTask = proc.StandardError.ReadToEndAsync()
          match sigTermWhenReady with
          | Some(_, expectedStarts) ->
            do! markerSeen.Task.WaitAsync(TimeSpan.FromSeconds 25.0) |> Async.AwaitTask
            let startDeadline = DateTimeOffset.UtcNow.AddSeconds 5.0
            while getStartCount () < expectedStarts && DateTimeOffset.UtcNow < startDeadline do
              do! Async.Sleep 25
            if not proc.HasExited then
              Bozzetto.Server.LinuxProcessEdge.sendSignalChecked proc.Id Bozzetto.ProcessWatchdog.SigTerm |> ignore
          | None -> ()
          use waitCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds 45.0)
          do! proc.WaitForExitAsync(waitCts.Token) |> Async.AwaitTask
          let! outText = stdoutTask |> Async.AwaitTask
          let! errText = stderrTask |> Async.AwaitTask
          return proc.ExitCode, outText, errText
        }
        let! outcome = async {
          try
            // Control A: malformed BOZZETTO_WALL_LIMIT_SECS exits 64 loudly.
            let! codeA, _, errA = runLease [ "BOZZETTO_WALL_LIMIT_SECS", "bogus" ] [ "test_suite_run"; "/bin/true" ] None
            codeA |> Expect.equal "invalid BOZZETTO_WALL_LIMIT_SECS refuses with exit 64" 64
            errA |> Expect.stringContains "exit 64 explains invalid env var" "BOZZETTO_WALL_LIMIT_SECS"

            // Control B: wall-clock fuse breach + auto CLEF_PHASE_TIMING=1 + nested grandchild + 20KB stderr ->
            // exit 124, bounded 16 KiB wire stderr, UN-TRUNCATED receipt file, independent /proc zero-survivor proof.
            let wallReceipt = System.IO.Path.Combine(tempDir, "wall-receipt.txt")
            let wallMarker = "boz-fi-wall-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            let wallCmd = sprintf "printf 'CLEF_PHASE_TIMING_ENV=%%s CLEF_BUDGET_GATE_ENV=%%s %%s\\n' \"$CLEF_PHASE_TIMING\" \"$CLEF_BUDGET_GATE\" '%s'; printf 'STDERR_HEAD_%s\\n' >&2; head -c 20000 /dev/zero | tr '\\0' 'e' >&2; echo >&2; /bin/sh -c 'sleep 41' _ %s & /bin/sleep 41" wallMarker wallMarker wallMarker
            let! codeB, _, _ =
              runLease [] [ "--canary"; "hello"; "--wall-limit-secs"; "1"; "--receipt-path"; wallReceipt; "test_suite_run"; "/bin/sh"; "-c"; wallCmd ] None
            codeB |> Expect.equal "wall fuse breach exits 124" 124
            let receiptText = System.IO.File.ReadAllText wallReceipt
            receiptText |> Expect.stringContains "wall receipt records wall breach" "breach=wall_limit_exceeded"
            receiptText |> Expect.stringContains "wall receipt records zero survivors" "survivors=none"
            receiptText |> Expect.stringContains "canary preset auto-injected CLEF_PHASE_TIMING=1 and CLEF_BUDGET_GATE=1" ("CLEF_PHASE_TIMING_ENV=1 CLEF_BUDGET_GATE_ENV=1 " + wallMarker)
            receiptText |> Expect.stringContains "receipt file preserves un-truncated stderr head beyond 16 KiB wire tail" ("STDERR_HEAD_" + wallMarker)
            let wallRoot, wallPgid = parseWatchdogIds receiptText
            verifyNoProcSurvivors "Control B (wall fuse)" wallRoot wallPgid wallMarker
            let finishedAfterB = getFinished ()
            finishedAfterB |> List.length |> Expect.equal "stub accepted bounded CommandFinished after 20KB stderr" 1
            (finishedAfterB.Head.StandardError.Length <= 16384 + 64) |> Expect.isTrue "CommandFinished stderr respected wire cap"
            finishedAfterB.Head.StandardError |> Expect.stringContains "CommandFinished stderr carries WATCHDOG receipt" "WATCHDOG "

            // Control C: multi-process RSS fuse breach (nested dotnet fsi allocating 96 MiB + nested /bin/sleep sibling vs 64 MiB ceiling)
            // -> exit 137, independent /proc proof that BOTH allocator and nested sleep sibling are gone.
            let rssReceipt = System.IO.Path.Combine(tempDir, "rss-receipt.txt")
            let rssMarker = "boz-fi-rss-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            let rssScript = System.IO.Path.Combine(tempDir, "alloc.fsx")
            System.IO.File.WriteAllText(rssScript, "let b = Array.create (96 * 1024 * 1024) 1uy\nprintfn \"RSS_ALLOC_OK %d\" b.Length\nuse mre = new System.Threading.ManualResetEventSlim(false)\nmre.WaitHandle.WaitOne(30000) |> ignore\n")
            let rssCmd = sprintf "/bin/sleep 42 & dotnet fsi \"%s\" %s & wait" rssScript rssMarker
            let! codeC, _, _ =
              runLease [] [ "--rss-limit-mb"; "64"; "--wall-limit-secs"; "20"; "--receipt-path"; rssReceipt; "test_suite_run"; "/bin/sh"; "-c"; rssCmd ] None
            codeC |> Expect.equal "multi-process RSS fuse breach exits 137" 137
            let rssText = System.IO.File.ReadAllText rssReceipt
            rssText |> Expect.stringContains "RSS receipt records rss breach" "breach=rss_limit_exceeded"
            rssText |> Expect.stringContains "RSS receipt records zero survivors" "survivors=none"
            let rssRoot, rssPgid = parseWatchdogIds rssText
            verifyNoProcSurvivors "Control C (multi-process RSS fuse)" rssRoot rssPgid rssMarker

            // Control D: SIGTERM cancellation WITHOUT --receipt-path still emits WATCHDOG receipt to stderr & CommandFinished
            // and leaves zero /proc survivors. Synchronizes deterministically on child stdout + CommandStarted before signaling.
            let sigMarker = "boz-fi-sig-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            let expectedStarts = getStartCount () + 1
            let sigCmd = sprintf "/bin/sh -c 'sleep 43' _ %s & /bin/sleep 43 & printf '%%s\\n' '%s'; wait" sigMarker sigMarker
            let! codeD, _, errD =
              runLease [] [ "--canary"; "hello"; "test_suite_run"; "/bin/sh"; "-c"; sigCmd ] (Some(sigMarker, expectedStarts))
            (codeD <> 0) |> Expect.isTrue (sprintf "SIGTERM cancellation must exit non-zero, got %d" codeD)
            errD |> Expect.stringContains "SIGTERM cancellation without --receipt-path still emits WATCHDOG line" "WATCHDOG "
            errD |> Expect.stringContains "SIGTERM cancellation confirms zero survivors" "survivors=none"
            let sigRoot, sigPgid = parseWatchdogIds errD
            verifyNoProcSurvivors "Control D (SIGTERM cancellation)" sigRoot sigPgid sigMarker
            let finishedAfterD = getFinished ()
            (finishedAfterD.Length >= 3) |> Expect.isTrue "CommandFinished recorded for cancelled run"
            (List.last finishedAfterD).StandardError |> Expect.stringContains "CommandFinished carries WATCHDOG receipt on cancellation" "WATCHDOG "
            return Ok ()
          with ex -> return Error ex
        }
        do! stopStub ()
        if System.IO.Directory.Exists tempDir then System.IO.Directory.Delete(tempDir, true)
        match outcome with
        | Ok () -> ()
        | Error ex -> return raise ex
    })
  ])
