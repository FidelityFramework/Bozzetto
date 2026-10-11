module Bozzetto.Tests.DaemonIntegrationTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Threading
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Server
open Bozzetto.WorkerProtocol
open Bozzetto.Tests.SharedGenerators

module Integration = Bozzetto.Tests.TestInfrastructure.Integration

// ─── Helpers ───────────────────────────────────────────────────────

let testProjectDir =
  Path.GetFullPath(
    Path.Combine(__SOURCE_DIRECTORY__, "..", "Bozzetto.Tests"))

let BozzettoExe = Bozzetto.Tests.TestInfrastructure.BozzettoBinary.path ()

/// A fresh, throwaway BOZZETTO_DATA_DIR so CLI subcommands and daemons spawned
/// by these tests never read or write the real ~/.bozzetto.
let isolatedDataDir () =
  Path.Combine(Path.GetTempPath(), "bozzetto-test", Guid.NewGuid().ToString("N"))

/// Kill a process by PID, swallowing errors.
let tryKill (pid: int) =
  try
    let p = Process.GetProcessById(pid)
    p.Kill()
    p.WaitForExit(3000) |> ignore
  with _ -> ()

// ─── SessionManager: ManagerState pure functions ───────────────────

[<Tests>]
let managerStateTests =
  testList "SessionManager.ManagerState" [
    testCase "empty has no sessions" <| fun _ ->
      let state = Bozzetto.SessionManager.ManagerState.empty
      Bozzetto.SessionManager.ManagerState.allInfos state
      |> List.length
      |> Expect.equal "no sessions" 0

    testCase "addSession then tryGetSession finds it" <| fun _ ->
      let sid = testSessionId "aaaa0001"
      let info : SessionInfo = {
        Id = sid
        Name = None
        Projects = ["Foo.fsproj"]
        WorkingDirectory = @"C:\test"
        SolutionRoot = None
        CreatedAt = DateTime.UtcNow
        LastActivity = DateTime.UtcNow
        Status = SessionLifecycleStatus.Ready { Pid = 1234; Port = None }
        Workflow = WorkflowTypes.SessionWorkflow.Interactive
        ActiveProject = None

        ProjectRoles = []

        App = Bozzetto.AppRun.AppRunState.NotRunning

      }
      let session : Bozzetto.SessionManager.ManagedSession = {
        Info = info
        Process = new Process()
        Proxy = fun _ -> async { return WorkerResponse.WorkerError (BozzettoError.Unexpected (exn "mock")) }
        WorkerBaseUrl = ""
        Targets = [ Bozzetto.SessionProjectTarget.Project "Foo.fsproj" ]
        WorkingDir = @"C:\test"
        AutoOpenNamespaces = true
        Workflow = WorkflowTypes.SessionWorkflow.Interactive
        RestartState = Bozzetto.RestartPolicy.emptyState
        AppGeneration = Bozzetto.AppRun.AppSlot.initial.Generation
        AdoptedCore = None

        ProjectRoles = []


      }
      let state =
        Bozzetto.SessionManager.ManagerState.empty
        |> Bozzetto.SessionManager.ManagerState.addSession sid session
      Bozzetto.SessionManager.ManagerState.tryGetSession sid state
      |> Expect.isSome "should find session"

    testCase "removeSession then tryGetSession returns None" <| fun _ ->
      let sid = testSessionId "aaaa0002"
      let info : SessionInfo = {
        Id = sid
        Name = None
        Projects = []
        WorkingDirectory = @"C:\test"
        SolutionRoot = None
        CreatedAt = DateTime.UtcNow
        LastActivity = DateTime.UtcNow
        Status = SessionLifecycleStatus.Ready { Pid = 1; Port = None }
        Workflow = WorkflowTypes.SessionWorkflow.Interactive
        ActiveProject = None

        ProjectRoles = []

        App = Bozzetto.AppRun.AppRunState.NotRunning

      }
      let session : Bozzetto.SessionManager.ManagedSession = {
        Info = info
        Process = new Process()
        Proxy = fun _ -> async { return WorkerResponse.WorkerError (BozzettoError.Unexpected (exn "mock")) }
        WorkerBaseUrl = ""
        Targets = [ Bozzetto.SessionProjectTarget.Bare ]
        WorkingDir = @"C:\test"
        AutoOpenNamespaces = true
        Workflow = WorkflowTypes.SessionWorkflow.Interactive
        RestartState = Bozzetto.RestartPolicy.emptyState
        AppGeneration = Bozzetto.AppRun.AppSlot.initial.Generation
        AdoptedCore = None

        ProjectRoles = []


      }
      let state =
        Bozzetto.SessionManager.ManagerState.empty
        |> Bozzetto.SessionManager.ManagerState.addSession sid session
        |> Bozzetto.SessionManager.ManagerState.removeSession sid
      Bozzetto.SessionManager.ManagerState.tryGetSession sid state
      |> Expect.isNone "should not find removed session"

    testCase "allInfos returns all session infos" <| fun _ ->
      let mkSession (sid: SessionId) : Bozzetto.SessionManager.ManagedSession =
        let info : SessionInfo = {
          Id = sid
          Name = None
          Projects = []
          WorkingDirectory = @"C:\test"
          SolutionRoot = None
          CreatedAt = DateTime.UtcNow
          LastActivity = DateTime.UtcNow
          Status = SessionLifecycleStatus.Ready { Pid = 1; Port = None }
          Workflow = WorkflowTypes.SessionWorkflow.Interactive
          ActiveProject = None

          ProjectRoles = []

          App = Bozzetto.AppRun.AppRunState.NotRunning

        }
        { Info = info
          Process = new Process()
          Proxy = fun _ -> async { return WorkerResponse.WorkerError (BozzettoError.Unexpected (exn "mock")) }
          WorkerBaseUrl = ""
          Targets = [ Bozzetto.SessionProjectTarget.Bare ]
          WorkingDir = @"C:\test"
          AutoOpenNamespaces = true
          Workflow = WorkflowTypes.SessionWorkflow.Interactive
          RestartState = Bozzetto.RestartPolicy.emptyState
          AppGeneration = Bozzetto.AppRun.AppSlot.initial.Generation
          ProjectRoles = []
          AdoptedCore = None }

      let sidA = testSessionId "aa000011"
      let sidB = testSessionId "bb000011"
      let sidC = testSessionId "cc000011"
      let state =
        Bozzetto.SessionManager.ManagerState.empty
        |> Bozzetto.SessionManager.ManagerState.addSession sidA (mkSession sidA)
        |> Bozzetto.SessionManager.ManagerState.addSession sidB (mkSession sidB)
        |> Bozzetto.SessionManager.ManagerState.addSession sidC (mkSession sidC)

      Bozzetto.SessionManager.ManagerState.allInfos state
      |> List.length
      |> Expect.equal "3 sessions" 3
  ]

// ─── DaemonState + CLI subcommand integration ──────────────────────
//
// THE CLAIM this used to be three real-process tests for: `status`/`stop`
// exit non-zero with no daemon, and `--help` advertises current flags.
//
// Split:
//   * The DECISION ("no daemon found" -> print "No daemon running", exit 1)
//     is identical machinery for both subcommands (`DaemonState.readOnPort`
//     returning `None`) and is exhaustively pure-tested with every daemon
//     interaction injected: CliStopExitCodeTests.fs (`stopCommand`) and
//     CliStatusExitCodeTests.fs (`statusCommand`).
//   * Which subcommand a bare CLI arg routes to (`"status"`/`"stop"` ->
//     `CliCommand.Status`/`.Stop`) is a pure function of args, unit-tested
//     directly in CliStatusExitCodeTests.fs
//     ("Program.CliCommand.parse routes daemon subcommands").
//   * `--help`'s content (including that it never advertises the legacy
//     `--proj`/`--sln` flags) never touches daemon state, so it is proven
//     in-process via `Program.main` directly in CliFirstRunTests.fs
//     ("boz --help documents check and stop exit codes...") — no real
//     process needed to prove pure stdout content.
//
// What NONE of the above proves: that the actual compiled, spawned
// `Bozzetto.exe`/`Bozzetto.dll` binary parses real OS argv, reaches
// `DaemonState.readOnPort`'s REAL network probe, and propagates the exit
// code back across a genuine process boundary. That is a wire claim, and
// `status`'s and `stop`'s no-daemon paths exercise IDENTICAL wiring (both
// call only `readOnPort`; `stop`'s extra injected functions are never
// reached when there is no daemon) — so ONE retained smoke proves it for
// both.
[<Tests>]
let daemonCliTests =
  Integration.hostList "Daemon CLI subcommands" [

    testCase "Bozzetto status returns 1 when no daemon running (real binary, real argv, real exit code)" <| fun _ ->
      let psi = ProcessStartInfo()
      psi.FileName <- BozzettoExe
      psi.Arguments <- "status --mcp-port 39990"
      psi.UseShellExecute <- false
      psi.RedirectStandardOutput <- true
      psi.CreateNoWindow <- true
      psi.Environment.["BOZZETTO_DATA_DIR"] <- isolatedDataDir ()

      use proc = Process.Start(psi)
      let output = proc.StandardOutput.ReadToEnd()
      proc.WaitForExit(5000) |> ignore

      proc.ExitCode |> Expect.equal "exit code 1" 1
      output |> Expect.stringContains "says no daemon" "No daemon running"
  ]

// ─── Daemon startup fails closed on a bind failure ──────────────────
//
// THE FLAKE THIS PROVES FIXED: ci-pipeline.fsx runs several test tiers
// concurrently, all sharing one network namespace. Every harness reserves a
// daemon's ports by binding them, reading them back, then RELEASING them
// before the daemon itself binds — a window a concurrently-running tier's
// daemon can win. When that happened here, the daemon logged "Failed to bind
// to address ...: address already in use", then "Dashboard failed to start"
// — and then logged "Bozzetto daemon ready" anyway, before eventually exiting
// 0. Every test that had spawned it burned its whole readiness timeout
// polling a port nobody would ever answer on.
//
// TestPorts.reservePair (see Bozzetto.Tests.TestInfrastructure) makes the
// cross-tier race structurally impossible (disjoint per-tier port ranges).
// This is the OTHER half of the fix: even when a bind loses a race, the
// daemon itself must fail fast and say why, rather than announce readiness
// over a listener nobody can reach.
let private startupBindFailureCeiling = TimeSpan.FromSeconds 20.0

[<Tests>]
let daemonStartupFailsClosedTests =
  Integration.hostList "Daemon startup fails closed" [

    testCase "a taken control port makes the daemon exit non-zero instead of announcing ready" <| fun _ ->
      let mcpPort, dashboardPort =
        Bozzetto.Tests.TestInfrastructure.TestPorts.reservePair ()

      // Hold the control listener's own port for the whole test — exactly what a
      // losing tier's daemon does to the winner in the real flake.
      use occupyDashboard = new TcpListener(IPAddress.Loopback, dashboardPort)
      occupyDashboard.Start()

      let psi = ProcessStartInfo()
      psi.FileName <- BozzettoExe
      psi.UseShellExecute <- false
      psi.CreateNoWindow <- true
      psi.WorkingDirectory <- testProjectDir
      psi.ArgumentList.Add "--mcp-port"
      psi.ArgumentList.Add(string mcpPort)
      psi.ArgumentList.Add("--owner-pid")
      psi.ArgumentList.Add(string (System.Diagnostics.Process.GetCurrentProcess().Id))
      psi.ArgumentList.Add "--no-resume"
      psi.Environment["BOZZETTO_DATA_DIR"] <- isolatedDataDir ()
      // Redirected to FILES, never undrained pipes: a full pipe deadlocks the
      // child before it can even log the failure.
      let dataDirForLogs = Path.Combine(Path.GetTempPath(), "bozzetto-test", Guid.NewGuid().ToString("N"))
      Directory.CreateDirectory dataDirForLogs |> ignore
      let outLog = Path.Combine(dataDirForLogs, "stdout.log")
      psi.RedirectStandardOutput <- true
      psi.RedirectStandardError <- true

      use daemonProc = new Process(StartInfo = psi)
      use logWriter = new StreamWriter(outLog, append = false)
      let logLock = obj ()
      let writeLine (line: string) =
        if not (isNull line) then lock logLock (fun () -> logWriter.WriteLine line)
      daemonProc.OutputDataReceived.Add(fun e -> writeLine e.Data)
      daemonProc.ErrorDataReceived.Add(fun e -> writeLine e.Data)
      daemonProc.Start() |> ignore
      daemonProc.BeginOutputReadLine()
      daemonProc.BeginErrorReadLine()

      try
        let exited =
          Bozzetto.Tests.TestInfrastructure.waitFor
            (int startupBindFailureCeiling.TotalMilliseconds)
            (fun () -> daemonProc.HasExited)
        exited
        |> Expect.isTrue
             "the daemon must exit on its own once its control bind fails, instead of hanging around claiming to be ready"

        daemonProc.WaitForExit(1000) |> ignore // flush the async readers
        lock logLock (fun () -> logWriter.Flush())

        daemonProc.ExitCode
        |> Expect.notEqual "a failed required bind must be a non-zero exit, not a quiet success" 0

        let logged = File.ReadAllText outLog
        logged
        |> Expect.stringContains "the failure is explained, not silent" "did not stay up"
        logged.Contains "Bozzetto daemon ready"
        |> Expect.isFalse "the daemon must never announce ready once a required listener failed to bind"
      finally
        try
          if not daemonProc.HasExited then
            daemonProc.Kill()
            daemonProc.WaitForExit(3000) |> ignore
        with _ -> ()
        try Directory.Delete(dataDirForLogs, true) with _ -> ()
  ]

// ─── Daemon lifecycle: start, status, stop ─────────────────────────

[<Tests>]
let daemonLifecycleTests =
  Integration.hostList "Daemon lifecycle" [

    testCase "start daemon, check status, stop" <| fun _ ->
      // Reserved via TestPorts (disjoint per-tier ranges) instead of a fixed
      // 37800+rand(100) span, which could collide with another
      // concurrently-running tier's daemon on the self-hosted runner.
      let port, _dashboardPort = Bozzetto.Tests.TestInfrastructure.TestPorts.reservePair ()
      let psi = ProcessStartInfo()
      psi.FileName <- BozzettoExe
      psi.Arguments <- sprintf "--mcp-port %d" port
      psi.UseShellExecute <- false
      psi.CreateNoWindow <- true
      psi.WorkingDirectory <- testProjectDir
      // Isolate persisted state so the daemon never resumes real ~/.bozzetto
      // sessions; status/stop below share the same data dir.
      let dataDir = isolatedDataDir ()
      psi.Environment.["BOZZETTO_DATA_DIR"] <- dataDir

      let daemonProc = Process.Start(psi)
      try
        // Wait for daemon to respond on HTTP — event-driven, no sleep-poll.
        let ready =
          Bozzetto.Tests.TestInfrastructure.waitFor
            (int Timeouts.integrationDaemonReady.TotalMilliseconds)
            (fun () -> (DaemonState.readOnPort port) |> Option.isSome)
        let info = if ready then DaemonState.readOnPort port else None

        info |> Expect.isSome "daemon should respond within the readiness ceiling"
        let di = info.Value
        di.Port |> Expect.equal "port matches" port
        di.Pid |> Expect.equal "PID matches" daemonProc.Id

        // Run Bozzetto status
        let statusPsi = ProcessStartInfo()
        statusPsi.FileName <- BozzettoExe
        statusPsi.Arguments <- sprintf "status --mcp-port %d" port
        statusPsi.UseShellExecute <- false
        statusPsi.RedirectStandardOutput <- true
        statusPsi.CreateNoWindow <- true
        statusPsi.Environment.["BOZZETTO_DATA_DIR"] <- dataDir

        use statusProc = Process.Start(statusPsi)
        let statusOutput = statusProc.StandardOutput.ReadToEnd()
        statusProc.WaitForExit(5000) |> ignore

        statusProc.ExitCode |> Expect.equal "status exits 0" 0
        statusOutput |> Expect.stringContains "shows running" "running"
        statusOutput
        |> Expect.stringContains "shows PID" (string daemonProc.Id)

        // Run Bozzetto stop
        let stopPsi = ProcessStartInfo()
        stopPsi.FileName <- BozzettoExe
        stopPsi.Arguments <- sprintf "stop --mcp-port %d" port
        stopPsi.UseShellExecute <- false
        stopPsi.RedirectStandardOutput <- true
        stopPsi.CreateNoWindow <- true
        stopPsi.Environment.["BOZZETTO_DATA_DIR"] <- dataDir

        use stopProc = Process.Start(stopPsi)
        let stopOutput = stopProc.StandardOutput.ReadToEnd()
        stopProc.WaitForExit(5000) |> ignore

        stopProc.ExitCode |> Expect.equal "stop exits 0" 0
        stopOutput |> Expect.stringContains "reports the stopped daemon's pid" (sprintf "Daemon stopped (PID %d)" daemonProc.Id)

        // Verify daemon process actually exited — event-driven, no sleep-poll.
        let exited =
          Bozzetto.Tests.TestInfrastructure.waitFor
            5000
            (fun () -> try daemonProc.HasExited with _ -> true)
        exited |> Expect.isTrue "daemon process should have exited"

        // Verify daemon is no longer responding
        DaemonState.readOnPort port
        |> Expect.isNone "daemon should no longer respond after stop"
      finally
        // Ensure cleanup even if test fails
        try
          if not daemonProc.HasExited then
            daemonProc.Kill()
            daemonProc.WaitForExit(3000) |> ignore
        with _ -> ()
  ]

// ─── SessionManager lifecycle: spawn, eval, stop ───────────────────

