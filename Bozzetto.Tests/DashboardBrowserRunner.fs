module Bozzetto.Tests.DashboardBrowserRunner

open System
open System.IO
open System.Net.Http
open Expecto

/// Run the [Integration] Dashboard browser journeys end to end, owning the
/// daemon lifecycle in-process (no external workflow / runner script).
///
/// CI invokes this via `Bozzetto.Tests.dll --integration-browser` after a
/// Release build — the same Expecto CLI shape as --integration-host. The
/// journeys use a real daemon with no F# sessions. The runner owns its
/// isolated BOZZETTO_DATA_DIR and reserved loopback ports, publishes the
/// dashboard endpoint, and runs only the active shell journeys. Retired F#
/// journeys remain registered as historical evidence outside acceptance.
let runBrowserJourneys (cliArgs: string array) : int =
  let repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

  let exe = Bozzetto.Tests.TestInfrastructure.BozzettoBinary.path ()

  // Bozzetto binds the MCP port it is given AND the dashboard at that port + 1
  // — TestPorts.reservePair proves both are free right now, scanning only
  // this tier's assigned BOZZETTO_TEST_PORT_RANGE when one is set, so a
  // concurrently-running tier's daemon can never win the reserve-then-bind
  // race for the same pair.
  let mcpPort, dashboardPort = Bozzetto.Tests.TestInfrastructure.TestPorts.reservePair ()

  let dataDir =
    Path.Combine(Path.GetTempPath(), "bozzetto-browser", Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory(dataDir) |> ignore

  let psi = Diagnostics.ProcessStartInfo()
  psi.FileName <- exe
  psi.UseShellExecute <- false
  psi.CreateNoWindow <- true
  psi.WorkingDirectory <- repoRoot
  psi.ArgumentList.Add("--mcp-port")
  psi.ArgumentList.Add(string mcpPort)
  psi.ArgumentList.Add("--owner-pid")
  psi.ArgumentList.Add(string (System.Diagnostics.Process.GetCurrentProcess().Id))
  psi.ArgumentList.Add("--no-resume")
  psi.Environment["BOZZETTO_DATA_DIR"] <- dataDir
  // Redirect daemon logs to FILES (never pipes): an undrained pipe deadlocks
  // the daemon once its log buffer fills, freezing warmup before Ready. Files
  // cannot deadlock and are dumped to stderr on failure for CI diagnosis.
  let daemonOutLog = Path.Combine(dataDir, "daemon.stdout.log")
  let daemonErrLog = Path.Combine(dataDir, "daemon.stderr.log")
  psi.RedirectStandardOutput <- true
  psi.RedirectStandardError <- true

  let daemon = Diagnostics.Process.Start(psi)
  // Drain the daemon's stdout/stderr asynchronously into the log files so the
  // pipes never fill regardless of log volume.
  let drain (stream: System.IO.StreamReader) (path: string) =
    let writer = new System.IO.StreamWriter(path, append = true)
    let rec loop () =
      async {
        let! line = stream.ReadLineAsync() |> Async.AwaitTask
        if not (isNull line) then
          do! writer.WriteLineAsync(line) |> Async.AwaitTask
          return! loop ()
      }
    async {
      try
        do! loop ()
      with _ -> ()
      writer.Dispose()
    }
    |> Async.Start
  drain daemon.StandardOutput daemonOutLog
  drain daemon.StandardError daemonErrLog

  use client = new HttpClient(BaseAddress = Uri(sprintf "http://localhost:%d" dashboardPort))
  client.Timeout <- TimeSpan.FromSeconds(5.0)

  let dumpDaemonLogs () =
    for path in [ daemonOutLog; daemonErrLog ] do
      try
        if File.Exists path then
          let text = File.ReadAllText(path)
          if not (String.IsNullOrWhiteSpace text) then
            eprintfn "--- %s (tail) ---" (Path.GetFileName path)
            let lines = text.Split('\n')
            let tail = lines |> Array.skip (max 0 (lines.Length - 40))
            tail |> Array.iter (eprintfn "%s")
      with _ -> ()

  let stopDaemon () =
    try
      if not daemon.HasExited then daemon.Kill(entireProcessTree = true)
    with _ -> ()
    try daemon.WaitForExit(5000) |> ignore with _ -> ()
    daemon.Dispose()

  let exitWith (code: int) =
    stopDaemon ()
    code

  try
    // The dashboard must report the PID this runner spawned (up to 60s).
    let mutable healthy = false
    let healthDeadline = DateTime.UtcNow.AddSeconds(60.0)
    while not healthy && DateTime.UtcNow < healthDeadline do
      try
        let body = client.GetStringAsync("/api/daemon-info").GetAwaiter().GetResult()
        healthy <- Bozzetto.Tests.TestInfrastructure.DaemonIdentity.reportsPid body daemon.Id
      with _ ->
        Threading.Thread.Sleep(250)

    if not healthy then
      eprintfn "Browser runner: owned dashboard did not become healthy on port %d" dashboardPort
      dumpDaemonLogs ()
      exitWith 1
    else
      Environment.SetEnvironmentVariable("BOZZETTO_DASHBOARD_PORT", string dashboardPort)
      Environment.SetEnvironmentVariable("BOZZETTO_BROWSER_MCP_PORT", string mcpPort)
      let browserArgv =
        cliArgs
        |> Array.filter (fun a -> a <> "--integration-browser")
      let activeJourneys =
        DashboardBrowserTests.tests
        |> Bozzetto.Tests.TestInfrastructure.Integration.excludeRetired
      let result =
        Bozzetto.Tests.TestInfrastructure.TrustSignal.run "--integration-browser" browserArgv activeJourneys
      exitWith result
  with ex ->
    eprintfn "Browser runner: %s" (ex.ToString())
    exitWith 1
