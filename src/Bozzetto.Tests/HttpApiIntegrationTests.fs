module Bozzetto.Tests.HttpApiIntegrationTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto.Features

module Integration = Bozzetto.Tests.TestInfrastructure.Integration

// ─── Shared Helpers ───────────────────────────────────────────────

/// The repository root: the working directory the daemons these suites start run in.
let repoRoot =
  Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let bozzettoExe = Bozzetto.Tests.TestInfrastructure.BozzettoBinary.path ()

let private daemonStartupHealthPollInterval = TimeSpan.FromMilliseconds(100.0)

let private daemonStartupHealthTimeout =
  TimeSpan.FromSeconds(60.0)

/// Does a /api/daemon-info payload identify THIS pid? Kept outside the task
/// builder so parsing needs no `use` inside a resumable state machine.
let private reportsPid = Bozzetto.Tests.TestInfrastructure.DaemonIdentity.reportsPid

let private daemonStartupHealthMaxAttempts =
  int (Math.Ceiling(daemonStartupHealthTimeout.TotalMilliseconds / daemonStartupHealthPollInterval.TotalMilliseconds))

/// A daemon binds TWO ports: the MCP port it is given, and the control
/// listener at that port + 1. Both are released before the daemon starts — a
/// check-then-use window that cannot be closed without handing a bound
/// socket to another process. `startDaemonWithArgs` therefore does not trust
/// the port afterwards: it proves the daemon that answers is the one it
/// spawned.
///
/// Delegates to the ONE shared allocator (TestInfrastructure.TestPorts) so
/// this suite's daemons scan only this tier's assigned
/// BOZZETTO_TEST_PORT_RANGE when ci-pipeline.fsx runs several tiers
/// concurrently — a concurrently-running tier's daemon can never win the
/// reserve-then-bind race for the same pair. Every call site here used to
/// pass its own hardcoded "preferred" literal (`38700 + Random().Next 100`,
/// ...) purely to avoid the OTHER literals in this SAME file; the shared
/// allocator's real bind-and-verify makes that convention unnecessary.
let reserveLoopbackPort () =
  fst (Bozzetto.Tests.TestInfrastructure.TestPorts.reservePair ())

let runProcessExpectSuccess (fileName: string) (workingDir: string) (args: string list) =
  let psi = ProcessStartInfo()
  psi.FileName <- fileName
  psi.WorkingDirectory <- workingDir
  psi.UseShellExecute <- false
  psi.RedirectStandardOutput <- true
  psi.RedirectStandardError <- true

  for arg in args do
    psi.ArgumentList.Add(arg)

  use proc = Process.Start(psi)
  let stdout = proc.StandardOutput.ReadToEnd()
  let stderr = proc.StandardError.ReadToEnd()
  proc.WaitForExit()

  match proc.ExitCode with
  | 0 -> ()
  | code ->
      failwith (
        sprintf
          "Process failed: %s %s (exit %d)%s%s"
          fileName
          (String.concat " " args)
          code
          Environment.NewLine
          ((stdout + Environment.NewLine + stderr).Trim()))

/// Start a daemon on a given port, wait until /health responds, return (process, HttpClient).
let startDaemonWithArgs (port: int) (workingDir: string) (args: string list) = task {
  let psi = ProcessStartInfo()
  psi.FileName <- bozzettoExe
  psi.UseShellExecute <- false
  psi.CreateNoWindow <- true
  psi.WorkingDirectory <- workingDir
  psi.ArgumentList.Add("--mcp-port")
  psi.ArgumentList.Add(string port)
  psi.ArgumentList.Add("--owner-pid")
  psi.ArgumentList.Add(string (System.Diagnostics.Process.GetCurrentProcess().Id))
  for arg in args do
    psi.ArgumentList.Add(arg)

  // Ownership rule 3 (multi-agent vision §3.1/§3.3): this daemon is owned
  // by the test runner process — if the test process dies (crash, kill),
  // the spawned daemon's own OwnerMonitor watchdog exits it instead of
  // leaking. --ttl is belt-and-braces on top of the owner watchdog.
  let self = Process.GetCurrentProcess()
  psi.ArgumentList.Add("--owner-pid")
  psi.ArgumentList.Add(string self.Id)
  psi.ArgumentList.Add("--owner-start")
  psi.ArgumentList.Add(string (self.StartTime.ToUniversalTime().Ticks))
  psi.ArgumentList.Add("--ttl")
  psi.ArgumentList.Add("10m")

  // Isolate this daemon's persisted state so it never resumes (or pollutes)
  // sessions from the real ~/.bozzetto or from earlier test runs.
  let dataDir = Path.Combine(Path.GetTempPath(), "bozzetto-test", Guid.NewGuid().ToString("N"))
  psi.Environment["BOZZETTO_DATA_DIR"] <- dataDir

  let proc = Process.Start(psi)
  let client = new HttpClient()
  client.BaseAddress <- Uri(sprintf "http://localhost:%d" port)
  client.Timeout <- TimeSpan.FromSeconds(30.0)

  // Ready means OUR daemon answers — not "something answers on this port".
  //
  // This used to accept any /health response. The port is released before the
  // daemon binds it (see `TestInfrastructure.TestPorts.reservePair`), and
  // integration suites run concurrently, so a neighbouring suite's daemon could
  // take the port first: ours failed to bind and exited, the neighbour's /health
  // answered, this returned "ready", and the test failed seconds later with
  // `Connection refused` once the neighbour shut down — passing every time in
  // isolation and failing under full-suite load. The control listener's
  // /api/daemon-info reports the daemon's own pid, so readiness is proven
  // against the process this call spawned, and an early exit fails fast with a
  // message that says what happened instead of a misleading error later.
  use identity = new HttpClient()
  identity.BaseAddress <- Uri(sprintf "http://localhost:%d" (port + 1))
  identity.Timeout <- TimeSpan.FromSeconds(5.0)
  let mutable ready = false
  let mutable exitedEarly = false
  let mutable attempts = 0
  while not ready && not exitedEarly && attempts < daemonStartupHealthMaxAttempts do
    do! Threading.Tasks.Task.Delay(daemonStartupHealthPollInterval)
    match proc.HasExited with
    | true -> exitedEarly <- true
    | false ->
      try
        let! body = identity.GetStringAsync("/api/daemon-info")
        ready <- reportsPid body proc.Id
      with _ -> ()
    attempts <- attempts + 1

  match ready, exitedEarly with
  | true, _ -> ()
  | false, true ->
    let code = proc.ExitCode
    proc.Dispose()
    client.Dispose()
    failwith (
      sprintf
        "Daemon exited with code %d before becoming ready on port %d — most often the port (or its +1 control port) was taken between reservation and bind by a parallel suite's daemon."
        code port)
  | false, false ->
    try proc.Kill() with _ -> ()
    proc.Dispose()
    client.Dispose()
    failwith (sprintf "Daemon failed to start on port %d within %O" port daemonStartupHealthTimeout)

  return proc, client
}

let startDaemon (port: int) =
  startDaemonWithArgs port repoRoot []

/// POST JSON to a path, return (statusCode, body).
let postJson (client: HttpClient) (path: string) (payload: obj) = task {
  let json = JsonSerializer.Serialize(payload)
  use content = new StringContent(json, Encoding.UTF8, "application/json")
  let! resp = client.PostAsync(path, content)
  let! body = resp.Content.ReadAsStringAsync()
  return int resp.StatusCode, body
}

let utf8NoBom = new UTF8Encoding(false)

/// GET a path, return (statusCode, body).
let getJson (client: HttpClient) (path: string) = task {
  let! resp = client.GetAsync(path)
  let! body = resp.Content.ReadAsStringAsync()
  return int resp.StatusCode, body
}

let normalizeDir (path: string) =
  Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

let createSession (client: HttpClient) (projectPath: string) (workingDir: string) = task {
  let payload =
    {| projects = [| projectPath |]
       workingDirectory = workingDir |}
  let! status, body = postJson client "/api/sessions/create" payload
  return status, body
}

let waitForReadySession (client: HttpClient) (targetDir: string) (timeout: TimeSpan) = task {
  let started = DateTime.UtcNow
  let mutable ready = false
  let mutable lastBody = ""
  let expectedDir = normalizeDir targetDir

  while not ready && DateTime.UtcNow - started < timeout do
    do! Task.Delay(1000)
    let! status, body = getJson client "/api/sessions"
    lastBody <- body
    if status = 200 then
      use doc = JsonDocument.Parse(body)
      ready <-
        doc.RootElement.GetProperty("sessions").EnumerateArray()
        |> Seq.exists (fun session ->
          let sessionDir = session.GetProperty("workingDirectory").GetString() |> normalizeDir
          sessionDir = expectedDir
          && session.GetProperty("status").GetString() = "Ready")

  return ready, lastBody
}

/// Ensure a Ready session exists for targetDir, creating one if absent.
/// The daemon routes /exec by workingDirectory but does not auto-create,
/// so tests that eval against a directory depend on this.
let ensureSession (client: HttpClient) (projectPath: string) (targetDir: string) = task {
  let! ready, _ = waitForReadySession client targetDir (TimeSpan.FromSeconds 5.0)
  if not ready then
    let payload =
      {| projects = [| projectPath |]
         workingDirectory = targetDir |}
    let! status, body = postJson client "/api/sessions/create" payload
    if status <> 200 then
      failwith (sprintf "session create failed: %d %s" status body)
    let! ready, _ = waitForReadySession client targetDir LiveTestingBudgets.baselineRun
    if not ready then
      failwith "session did not reach Ready"
}

type LiveTestingStatusSnapshot = {
  DiscoveryState: string
  Total: int
  Passed: int
  Failed: int
  Stale: int
  Running: int
  FailedTests: string list
}

let getLiveTestingStatusSnapshot (client: HttpClient) (fileFilter: string option) = task {
  let path =
    match fileFilter with
    | Some file -> sprintf "/api/live-testing/status?file=%s" (Uri.EscapeDataString file)
    | None -> "/api/live-testing/status"

  let! status, body = getJson client path

  if status <> 200 then
    failwith (sprintf "Expected 200 from %s, got %d with body: %s" path status body)

  use doc = JsonDocument.Parse(body)
  let root = doc.RootElement
  let summary = root.GetProperty("Summary")
  let failedTests =
    match root.TryGetProperty("FailedTests") with
    | true, tests ->
      tests.EnumerateArray()
      |> Seq.map (fun entry -> entry.GetProperty("Name").GetString())
      |> Seq.toList
    | false, _ -> []

  let snapshot = {
    DiscoveryState = root.GetProperty("DiscoveryState").GetString()
    Total = summary.GetProperty("Total").GetInt32()
    Passed = summary.GetProperty("Passed").GetInt32()
    Failed = summary.GetProperty("Failed").GetInt32()
    Stale = summary.GetProperty("Stale").GetInt32()
    Running = summary.GetProperty("Running").GetInt32()
    FailedTests = failedTests
  }

  return snapshot, body
}

let waitForLiveTestingStatus
  (client: HttpClient)
  (fileFilter: string option)
  (timeout: TimeSpan)
  (predicate: LiveTestingStatusSnapshot -> bool)
  =
  task {
    let started = DateTime.UtcNow
    let mutable matched = false
    let mutable lastBody = ""
    let mutable lastSnapshot = {
      DiscoveryState = ""
      Total = 0
      Passed = 0
      Failed = 0
      Stale = 0
      Running = 0
      FailedTests = []
    }

    while not matched && DateTime.UtcNow - started < timeout do
      let! snapshot, body = getLiveTestingStatusSnapshot client fileFilter
      lastSnapshot <- snapshot
      lastBody <- body
      matched <- predicate snapshot

      if not matched then
        do! Task.Delay(250)

    return matched, lastSnapshot, lastBody
  }

/// Cleanup daemon process.
let killDaemon (proc: Process) =
  try
    if not proc.HasExited then
      proc.Kill(entireProcessTree = true)
      proc.WaitForExit(5000) |> ignore
  with _ -> ()
  proc.Dispose()

[<Tests>]
let httpApiHarnessTests =
  testList "HTTP API harness" [
    testCase "daemon startup wait budget matches the documented 60 seconds" <| fun _ ->
      daemonStartupHealthTimeout
      |> Expect.equal "startup wait should match documented 60 seconds" (TimeSpan.FromSeconds(60.0))

    testCase "reserveLoopbackPort returns a genuinely free, bindable port" <| fun _ ->
      // The "skips an occupied preferred port" contract this test used to
      // name moved to TestPortsTests.fs, which exercises the shared
      // allocator directly (including under an assigned BOZZETTO_TEST_PORT_RANGE) —
      // this is a thin smoke check that this file's wrapper still reaches it.
      let reserved = reserveLoopbackPort ()

      (reserved, 0)
      |> Expect.isGreaterThan "reserved port should be positive"

      use validation = new TcpListener(IPAddress.Loopback, reserved)
      validation.Start()
  ]

// ─── Shared Daemon Fixture ─────────────────────────────────────────
// One daemon shared across all integration tests (saves ~170s of startup).
// Tests run sequenced since they share daemon state.

let private sharedPort = reserveLoopbackPort ()

let private sharedDaemon =
  lazy (startDaemon sharedPort |> Async.AwaitTask |> Async.RunSynchronously) // lazy one-time startup, acceptable

let private getSharedClient () = snd sharedDaemon.Value
let private getSharedProc () = fst sharedDaemon.Value

do AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
  if sharedDaemon.IsValueCreated then
    let proc, client = sharedDaemon.Value
    client.Dispose()
    killDaemon proc)

// ─── Tests (shared daemon, sequenced) ─────────────────────────────

[<Tests>]
let daemonStartupSmokeTest =
  Integration.hostList "Daemon startup smoke" [
    testCase "Daemon starts on fresh port and /health responds" <| fun _ ->
      let port = reserveLoopbackPort ()
      let proc, client = startDaemon port |> Async.AwaitTask |> Async.RunSynchronously
      try
        let status, body = getJson client "/health" |> Async.AwaitTask |> Async.RunSynchronously
        status |> Expect.equal "200 OK" 200
        body |> Expect.isNotEmpty "body is not empty"
      finally
        client.Dispose()
        killDaemon proc
  ]
