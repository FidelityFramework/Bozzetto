module Bozzetto.Tests.CliFirstRunTests

// The Bozzetto CLI first-run experience (AGENTS.md's "STOP — Read This Before
// Anything Else" audience: a new user who just installed the tool). These
// tests cover the pure decisions behind:
//   - `--proj`/`--sln`/`--no-watch` must never be silently accepted: they are
//     parsed for backward compatibility but wired to nothing, so the CLI
//     refuses to start with guidance instead of pretending the flag worked.
//   - `.bozzetto/config.fsx` and `.fsproj` discovery describe what they
//     actually did (existence-only, non-recursive) rather than implying more.
//   - `--help` documents the exit codes it can actually produce and never
//     advertises a flag it does not honor.
//
// `boz check`'s SDK-check decision and `boz stop`'s exit codes already
// have dedicated coverage (EnvCheckTests.fs, CliStopExitCodeTests.fs) and are
// deliberately not re-tested here.

open System
open System.IO
open Expecto
open Expecto.Flip
open Bozzetto

// ─────────────────────────────────────────────────────────────────────────
// Args.UnimplementedFlag.detectAll — pure, no IO
// ─────────────────────────────────────────────────────────────────────────

[<Tests>]
let unimplementedFlagDetectionTests =
  testList "Args.UnimplementedFlag.detectAll" [

    testCase "no args detects nothing" <| fun () ->
      Args.UnimplementedFlag.detectAll []
      |> Expect.isEmpty "empty args have no unimplemented flags"

    testCase "implemented flags alone detect nothing" <| fun () ->
      Args.UnimplementedFlag.detectAll ["--no-resume"; "--prune"; "--mcp-port"; "47700"]
      |> Expect.isEmpty "known-good flags must not be flagged as unimplemented"

    testCase "detects --proj" <| fun () ->
      Args.UnimplementedFlag.detectAll ["--proj"; "Foo.fsproj"]
      |> Expect.equal "proj detected" [Args.UnimplementedFlag.Proj]

    testCase "detects --sln" <| fun () ->
      Args.UnimplementedFlag.detectAll ["--sln"; "Foo.slnx"]
      |> Expect.equal "sln detected" [Args.UnimplementedFlag.Sln]

    testCase "detects --no-watch" <| fun () ->
      Args.UnimplementedFlag.detectAll ["--no-watch"]
      |> Expect.equal "no-watch detected" [Args.UnimplementedFlag.NoWatch]

    testCase "detects multiple flags in --proj, --sln, --no-watch order" <| fun () ->
      Args.UnimplementedFlag.detectAll ["--no-watch"; "--sln"; "Bar.slnx"; "--proj"; "Foo.fsproj"]
      |> Expect.equal "stable detection order regardless of argument order"
           [Args.UnimplementedFlag.Proj; Args.UnimplementedFlag.Sln; Args.UnimplementedFlag.NoWatch]

    testCase "a bare --proj with no value is still detected" <| fun () ->
      Args.UnimplementedFlag.detectAll ["--proj"]
      |> Expect.equal "presence-only detection" [Args.UnimplementedFlag.Proj]

    testCase "text renders the exact flag spelling" <| fun () ->
      Args.UnimplementedFlag.text Args.UnimplementedFlag.Proj |> Expect.equal "proj text" "--proj"
      Args.UnimplementedFlag.text Args.UnimplementedFlag.Sln |> Expect.equal "sln text" "--sln"
      Args.UnimplementedFlag.text Args.UnimplementedFlag.NoWatch |> Expect.equal "no-watch text" "--no-watch"
  ]

// ─────────────────────────────────────────────────────────────────────────
// Program.unimplementedFlagRejection — pure decision Program.fs's Daemon
// branch acts on (print + exit 2), tested without ever touching a daemon.
// ─────────────────────────────────────────────────────────────────────────

[<Tests>]
let unimplementedFlagRejectionTests =
  testList "Program.unimplementedFlagRejection" [

    testCase "no unimplemented flags means nothing blocks startup" <| fun () ->
      Program.unimplementedFlagRejection [| "--mcp-port"; "47700" |]
      |> Expect.isEmpty "normal daemon args are never rejected"

    testCase "--proj is rejected with guidance naming where to create a session" <| fun () ->
      let rejected = Program.unimplementedFlagRejection [| "--proj"; "Foo.fsproj" |]
      match rejected with
      | [ (flag, guidance) ] ->
        flag |> Expect.equal "flag identified" Args.UnimplementedFlag.Proj
        guidance |> Expect.stringContains "guidance names the concrete next step" "session"
        guidance |> Expect.stringContains "guidance names where to do it" "MCP client"
      | other -> failtestf "expected exactly one rejection, got %A" other

    testCase "--sln gets the same guidance shape as --proj" <| fun () ->
      let projGuidance = Program.unimplementedFlagRejection [| "--proj"; "x" |] |> List.map snd
      let slnGuidance = Program.unimplementedFlagRejection [| "--sln"; "x" |] |> List.map snd
      slnGuidance |> Expect.equal "proj and sln share the same startup-project guidance" projGuidance

    testCase "--no-watch guidance says it has no effect rather than implying it works" <| fun () ->
      match Program.unimplementedFlagRejection [| "--no-watch" |] with
      | [ (Args.UnimplementedFlag.NoWatch, guidance) ] ->
        guidance |> Expect.stringContains "guidance is honest about doing nothing" "does nothing"
      | other -> failtestf "expected exactly one no-watch rejection, got %A" other

    testCase "multiple unimplemented flags are all reported, not just the first" <| fun () ->
      let rejected = Program.unimplementedFlagRejection [| "--proj"; "Foo.fsproj"; "--no-watch" |]
      rejected |> List.map fst
      |> Expect.equal "both flags surfaced" [Args.UnimplementedFlag.Proj; Args.UnimplementedFlag.NoWatch]
  ]

// ─────────────────────────────────────────────────────────────────────────
// main(): the Daemon branch refuses instead of starting when an
// unimplemented flag is present. This never reaches decideDaemonLaunch /
// DaemonState.readOnPort — it must exit before touching any daemon state,
// so it is safe to call `Program.main` directly here.
// ─────────────────────────────────────────────────────────────────────────

let private runMain (args: string array) =
  let origOut = Console.Out
  let origErr = Console.Error
  use outWriter = new StringWriter()
  use errWriter = new StringWriter()
  Console.SetOut(outWriter)
  Console.SetError(errWriter)
  try
    let code = Program.main args
    code, outWriter.ToString(), errWriter.ToString()
  finally
    Console.SetOut(origOut)
    Console.SetError(origErr)

[<Tests>]
let mainRejectsUnimplementedFlagsTests =
  testSequenced <| testList "main() refuses unimplemented daemon-startup flags" [

    testCase "retired Jupyter execution refuses before reading a connection file" <| fun () ->
      let code, _, stderr = runMain [| "--jupyter"; "this-file-does-not-exist.json" |]
      code |> Expect.equal "retired command has an explicit refusal exit" 2
      stderr.Trim() |> Expect.equal "the refusal explains the current provider boundary" Bozzetto.ExternalFSharpService.message

    testCase "boz --proj exits non-zero without starting a daemon" <| fun () ->
      let code, _, stderr = runMain [| "--proj"; "Foo.fsproj" |]
      Expect.isTrue "must exit non-zero" (code <> 0)
      stderr |> Expect.stringContains "stderr names the flag" "--proj"

    testCase "boz --sln exits non-zero without starting a daemon" <| fun () ->
      let code, _, stderr = runMain [| "--sln"; "Foo.slnx" |]
      Expect.isTrue "must exit non-zero" (code <> 0)
      stderr |> Expect.stringContains "stderr names the flag" "--sln"

    testCase "boz --no-watch exits non-zero without starting a daemon" <| fun () ->
      let code, _, stderr = runMain [| "--no-watch" |]
      Expect.isTrue "must exit non-zero" (code <> 0)
      stderr |> Expect.stringContains "stderr names the flag" "--no-watch"

    testCase "WHY — an unimplemented flag must never share the success exit code" <| fun () ->
      let code, _, _ = runMain [| "--no-watch" |]
      Expect.notEqual "0 means success; a refused request is not success" 0 code

    // --help: documents its real exit codes, never advertises a flag it
    // refuses. `--help` never touches daemon state (readOnPort, a port, a
    // process) so it is safe to drive through `Program.main` directly, in
    // process — no real Bozzetto.exe needs to be spawned to prove this. Used to
    // be split: this file covered "what's new" and DaemonIntegrationTests.fs's
    // live-process test separately pinned the legacy --proj/--sln absence.
    // Folded back together here now that both assertions are equally provable
    // in-process, so the live-process copy was retired.
    // Kept in this same sequenced list because it also drives main() through
    // the shared Console.Out/Error capture.
    testCase "boz --help documents check and stop exit codes and gives a first-run next step" <| fun () ->
      let code, stdout, _ = runMain [| "--help" |]
      Expect.equal "help exits 0" 0 code
      stdout |> Expect.stringContains "documents check's failing exit code" "1 = at least one check failed"
      stdout |> Expect.stringContains "documents stop's no-op exit code" "1 = there was nothing to stop"
      stdout |> Expect.stringContains "tells a new user the daemon does not create a session" "does not do it for you"
      stdout |> Expect.stringContains "mentions daemon" "daemon"
      stdout |> Expect.stringContains "mentions stop" "stop"
      stdout |> Expect.stringContains "mentions status" "status"
      (stdout.Contains "--no-watch")
      |> Expect.isFalse "must not advertise a flag it refuses to honor"
      (stdout.Contains "--proj")
      |> Expect.isFalse "must not advertise the legacy startup-project flag it refuses to honor"
      (stdout.Contains "--sln")
      |> Expect.isFalse "must not advertise the legacy startup-solution flag it refuses to honor"
  ]

// ─────────────────────────────────────────────────────────────────────────
// EnvCheck.checkFsproj — honest about non-recursive scope
// ─────────────────────────────────────────────────────────────────────────

[<Tests>]
let checkFsprojScopeTests =
  testList "EnvCheck.checkFsproj describes its own search scope honestly" [

    testCase "pass detail says found directly / not recursive" <| fun () ->
      let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
      Directory.CreateDirectory(dir) |> ignore
      try
        File.WriteAllText(Path.Combine(dir, "App.fsproj"), "<Project/>")
        let r = EnvCheck.checkFsproj dir
        r.Detail |> Expect.stringContains "says not recursive" "not recursive"
      finally
        Directory.Delete(dir, true)

    testCase "warn hint does not claim it searched subdirectories" <| fun () ->
      let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
      Directory.CreateDirectory(dir) |> ignore
      try
        let r = EnvCheck.checkFsproj dir
        r.Detail |> Expect.stringContains "warns it did not search subdirectories" "does not search subdirectories"
      finally
        Directory.Delete(dir, true)
  ]

// ─────────────────────────────────────────────────────────────────────────
// EnvCheck.checkDirectoryConfig — .bozzetto/config.fsx existence, no eval
// ─────────────────────────────────────────────────────────────────────────

[<Tests>]
let checkDirectoryConfigTests =
  testList "EnvCheck.checkDirectoryConfig" [

    testCase "Pass and honest when config.fsx is absent" <| fun () ->
      let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
      Directory.CreateDirectory(dir) |> ignore
      try
        let r = EnvCheck.checkDirectoryConfig dir
        r.Status |> Expect.equal "absence of an optional file is not a failure" EnvCheck.Status.Pass
        r.Detail |> Expect.stringContains "says optional" "optional"
      finally
        Directory.Delete(dir, true)

    testCase "Pass and honest when config.fsx is present, without claiming it was evaluated" <| fun () ->
      let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
      let bozzettoDir = Path.Combine(dir, ".bozzetto")
      Directory.CreateDirectory(bozzettoDir) |> ignore
      try
        File.WriteAllText(Path.Combine(bozzettoDir, "config.fsx"), "DirectoryConfig.empty")
        let r = EnvCheck.checkDirectoryConfig dir
        r.Status |> Expect.equal "presence is fine" EnvCheck.Status.Pass
        r.Detail |> Expect.stringContains "found" "Found"
        r.Detail |> Expect.stringContains "does not overclaim validity" "not evaluate"
      finally
        Directory.Delete(dir, true)
  ]


[<Tests>]
let daemonLaunchContractTests =
  testList "CLI parsing" [
    test "--jupyter parses to Jupyter case" {
      Program.CliCommand.parse [| "--jupyter"; "conn.json" |]
      |> Expect.equal "Jupyter" (Program.Jupyter "conn.json")
    }

    test "--jupyter without file falls back to ShowHelp" {
      Program.CliCommand.parse [| "--jupyter" |]
      |> Expect.equal "ShowHelp" Program.ShowHelp
    }

    test "--jupyter with path preserves full path" {
      Program.CliCommand.parse [| "--jupyter"; @"C:\tmp\kernel-1234.json" |]
      |> Expect.equal "full path" (Program.Jupyter @"C:\tmp\kernel-1234.json")
    }

    test "regular args don't match Jupyter" {
      Program.CliCommand.parse [| "--no-watch" |]
      |> function
         | Program.Daemon _ -> ()
         | other -> failtest (sprintf "Expected Daemon but got %A" other)
    }

    test "WHY — tui and gui commands are explicit deprecations because they must not silently enter daemon mode" {
      Program.CliCommand.parse [| "tui" |]
      |> Expect.equal "tui should be recognized as deprecated" (Program.DeprecatedClient "tui")
      Program.CliCommand.parse [| "gui" |]
      |> Expect.equal "gui should be recognized as deprecated" (Program.DeprecatedClient "gui")
      Program.deprecatedClientMessage "tui"
      |> Expect.stringContains "deprecation should direct users to the maintained UI" "/composer"
    }

    test "daemon launch decision starts new daemon when only default port is occupied" {
      let defaultDaemon = {
        Pid = 42
        Port = 47749
        DashboardPort = 47750
        StartedAt = DateTime.UtcNow
        WorkingDirectory = @"C:\Code\Repos\Elsewhere"
        Version = "test"
        ApiVersion = None
        SessionCount = None
        ComponentFailures = []
      }

      let readOnPort port =
        match port with
        | 47749 -> Some defaultDaemon
        | _ -> None

      Program.decideDaemonLaunch readOnPort 37849
      |> Expect.equal "custom port should not attach to default-port daemon" Program.StartNewDaemon
    }

    test "daemon launch decision reuses daemon already running on requested port" {
      let requestedDaemon = {
        Pid = 43
        Port = 37849
        DashboardPort = 37850
        StartedAt = DateTime.UtcNow
        WorkingDirectory = @"C:\Code\Repos\Bozzetto"
        Version = "test"
        ApiVersion = None
        SessionCount = None
        ComponentFailures = []
      }

      let readOnPort port =
        match port with
        | 37849 -> Some requestedDaemon
        | _ -> None

      Program.decideDaemonLaunch readOnPort 37849
      |> Expect.equal "requested-port daemon should be reused" (Program.AttachToExistingDaemon requestedDaemon)
    }

    test "waitForDaemonReady probes only the requested custom port" {
      let requestedDaemon = {
        Pid = 44
        Port = 37849
        DashboardPort = 37850
        StartedAt = DateTime.UtcNow
        WorkingDirectory = @"C:\Code\Repos\Bozzetto"
        Version = "test"
        ApiVersion = None
        SessionCount = None
        ComponentFailures = []
      }

      let probedPorts = System.Collections.Generic.List<int>()
      let mutable attempts = 0

      let readOnPort port =
        probedPorts.Add(port)
        attempts <- attempts + 1
        match attempts with
        | 3 -> Some requestedDaemon
        | _ -> None

      let sleepCalls = System.Collections.Generic.List<int>()
      let sleep ms = sleepCalls.Add(ms)

      Program.waitForDaemonReady sleep readOnPort 37849
      |> Expect.equal "should return daemon on requested port" (Ok requestedDaemon)

      probedPorts
      |> Seq.distinct
      |> Seq.toList
      |> Expect.equal "should only probe requested port" [37849]

      sleepCalls.Count
      |> Expect.equal "should sleep once per probe until ready" 3
    }

    test "waitForDaemonReady times out after 30 probes on the requested port" {
      let probedPorts = System.Collections.Generic.List<int>()
      let readOnPort port =
        probedPorts.Add(port)
        None

      let sleepCalls = System.Collections.Generic.List<int>()
      let sleep ms = sleepCalls.Add(ms)

      Program.waitForDaemonReady sleep readOnPort 37849
      |> function
         | Error (BozzettoError.DaemonStartFailed msg) ->
           msg |> Expect.stringContains "should describe timeout" "did not become ready"
         | Ok _ -> failtest "expected timeout error"
         | Error other -> failtestf "unexpected error: %A" other

      probedPorts.Count
      |> Expect.equal "should probe requested port 30 times" 30

      probedPorts
      |> Seq.distinct
      |> Seq.toList
      |> Expect.equal "should never probe the default port" [37849]

      sleepCalls.Count
      |> Expect.equal "should sleep once per probe attempt" 30
    }
  ]
