#!/usr/bin/env -S dotnet fsi
// ci-pipeline.fsx
//
// THIS SCRIPT IS THE BUILD AND TEST PIPELINE. Every build/test step is encoded
// here, so the same stages run on a developer machine and on any CI runner,
// which needs to supply only the checkout and the SDK/Node/xvfb install:
//
//   dotnet fsi ci-pipeline.fsx                # build, format, unit + integration
//   dotnet fsi ci-pipeline.fsx -- ci          # + the mutation-score gate
//   dotnet fsi ci-pipeline.fsx -- composer    # + the Composer provider tier
//   dotnet fsi ci-pipeline.fsx -- pack        # + pack the CLI tool into nupkg/
//
// The design goal: build the solution ONCE in Release, then run every
// downstream check --no-build off that single output, on one Linux machine.
//
// Stage selection:
//   * unconditional — build, format, samples, VS Code extension compile +
//     client contract tests (npm run test:golden + every
//     bozzetto-vscode/tests/*.fsx), then "test tiers": the default suite and
//     the integration-host suites.
//   * `ci` adds to "test tiers" — the mutation-score gate.
//   * `composer` builds the Composer provider worker and adds its tier; it
//     needs BOZZETTO_COMPOSER_DISTRIBUTION and BOZZETTO_COMPOSER_FIXTURE.
//   * "test tiers" runs every tier regardless of the others; concurrently, each
//     in a private copy-on-write clone, where the machine supports it (see the
//     tier scheduler section and build/TierPlan.fs).
//   * always, after the tiers — the "trust report": one table of every tier's
//     registered/ran/verdict; the one place a red test tier fails the run.
//   * `pack` — after a trusted run, pack the CLI tool package into nupkg/ and
//     check its payload. Nothing is published from this pipeline.
//
// Cross-platform packing: every per-RID tree-sitter native is committed under
// runtimes/ and the fsproj includes them by Condition="Exists(...)", so the
// single Linux pack produces a complete cross-platform nupkg.

#r "nuget: Fun.Build, 1.2.0"

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text.Json
open System.Xml.Linq
open Fun.Build
open Fun.Build.Github


let rootDir = __SOURCE_DIRECTORY__
/// Where `pack` writes the CLI tool package (gitignored).
let packDir = Path.Combine(rootDir, "nupkg")
let vscodeDir = Path.Combine(rootDir, "bozzetto-vscode")
// Every downstream check runs against this ONE Release build (see "build" stage).
let testBinDir = "Bozzetto.Tests/bin/Release/net10.0"
let testDll = $"{testBinDir}/Bozzetto.Tests.dll"
let leaseRunner = Path.Combine(rootDir, "scripts/work-lease")
let leasedCommand kind command = $"\"{leaseRunner}\" run {kind} {command}"

type WorkLeaseScope = {
  Deadline: DateTimeOffset
  Cancellation: Threading.CancellationToken
}

// Pipeline stages are serial. The whole parallel tier stage shares one test
// lease; its children do not acquire nested leases or release their parent.
let mutable activeWorkLease: WorkLeaseScope option = None

let leaseControl arguments =
  use child = new Diagnostics.Process()
  child.StartInfo.FileName <- leaseRunner
  arguments |> List.iter child.StartInfo.ArgumentList.Add
  child.StartInfo.RedirectStandardOutput <- true
  child.StartInfo.RedirectStandardError <- true
  child.Start() |> ignore
  let stdout = child.StandardOutput.ReadToEndAsync()
  let stderr = child.StandardError.ReadToEndAsync()
  child.WaitForExit()
  child.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult()

let withTestSuiteLease (work: unit -> Async<Result<unit, string>>) =
  async {
    let! callerCancellation = Async.CancellationToken
    let code, response, diagnostic =
      leaseControl [ "acquire"; "test_suite_run"; $"gate-tiers-{Environment.ProcessId}" ]
    if code <> 0 then
      return Result.Error $"Test-suite admission stopped the stage: {diagnostic.Trim()}"
    else
      use document = JsonDocument.Parse response
      let id = document.RootElement.GetProperty("leaseId").GetString()
      let expires = document.RootElement.GetProperty("expiresAt").GetDateTimeOffset()
      use ownedCancellation = new Threading.CancellationTokenSource()
      let previous = activeWorkLease
      activeWorkLease <- Some { Deadline = expires.AddMinutes(-1.); Cancellation = ownedCancellation.Token }
      use registration = callerCancellation.Register(fun () -> ownedCancellation.Cancel())
      // Independent observation retains the stage until every owned process
      // has joined, including caller cancellation. Release follows that join.
      let owned = Async.StartAsTask(work(), cancellationToken = Threading.CancellationToken.None)
      let mutable releaseFailure = None
      let! result =
        async {
          try
            return! owned |> Async.AwaitTask
          finally
            ownedCancellation.Cancel()
            try owned.GetAwaiter().GetResult() |> ignore with _ -> ()
            activeWorkLease <- previous
            let releaseCode, _, releaseDiagnostic = leaseControl [ "release"; id ]
            if releaseCode <> 0 then
              let message = $"Owned test-suite lease release failed: {releaseDiagnostic.Trim()}"
              eprintfn "%s" message
              releaseFailure <- Some message
        }
      match releaseFailure with
      | Some message -> return Result.Error message
      | None -> return result
  }

// ---- packing ------------------------------------------------------------------

/// The single source-of-truth version, from Directory.Build.props.
let propsVersion () =
  let doc = XDocument.Load(Path.Combine(rootDir, "Directory.Build.props"))
  doc.Descendants()
  |> Seq.tryFind (fun e -> e.Name.LocalName = "Version")
  |> Option.map (fun e -> e.Value.Trim())
  |> Option.defaultWith (fun () -> failwith "Directory.Build.props: no <Version> element found")

/// Verify the packed tool payload matches the supported stable runtime.
let requiredToolTfms = [ "net10.0" ]
let verifyToolInstallable () =
  // The package for THIS build's version, never whichever nupkg sorts first.
  let expected = Path.Combine(packDir, $"Bozzetto.{propsVersion ()}.nupkg")
  match File.Exists expected with
  | false -> failwithf "No %s in nupkg/. Did pack fail?" (Path.GetFileName expected)
  | true ->
    let nupkg = expected
    use zip = ZipFile.OpenRead nupkg
    let tfms =
      zip.Entries
      |> Seq.map (fun e -> e.FullName)
      |> Seq.filter (fun n -> n.StartsWith "tools/" && n.EndsWith "/any/DotnetToolSettings.xml")
      |> Seq.map (fun n -> (n.Split('/')).[1])
      |> Seq.distinct
      |> Seq.toList
    // Bozzetto loads exactly one tree-sitter grammar, its own F# one. Any other
    // grammar in the package is dead weight that got copied in from
    // TreeSitter.DotNet (Directory.Build.targets strips them).
    let strayGrammars =
      zip.Entries
      |> Seq.map (fun e -> Path.GetFileName e.FullName)
      |> Seq.filter (fun f ->
        (f.StartsWith "tree-sitter-" || f.StartsWith "libtree-sitter-")
        && not (f.Contains "tree-sitter-fsharp"))
      |> Seq.distinct
      |> Seq.toList
    match strayGrammars with
    | [] -> ()
    | stray -> failwithf "%s bundles tree-sitter grammars Bozzetto never loads: %s" (Path.GetFileName nupkg) (String.concat ", " stray)
    let missing = requiredToolTfms |> List.filter (fun t -> not (List.contains t tfms))
    let unexpected = tfms |> List.filter (fun t -> not (List.contains t requiredToolTfms))
    match missing, unexpected with
    | [], [] ->
      printfn "OK %s carries exactly the %s tool payload." (Path.GetFileName nupkg) (String.concat " and " requiredToolTfms)
    | missing, [] ->
      failwithf "DotnetToolSettings.xml missing for %s in %s — those SDK users' installs will fail (issue #131)." (String.concat ", " missing) (Path.GetFileName nupkg)
    | [], unexpected ->
      failwithf "Tool package also targets unexpected TFM(s) %s in %s — the repo must pack exactly %s." (String.concat ", " unexpected) (Path.GetFileName nupkg) (String.concat ", " requiredToolTfms)
    | missing, unexpected ->
      failwithf "Tool package TFM mismatch in %s: missing %s, unexpected %s." (Path.GetFileName nupkg) (String.concat ", " missing) (String.concat ", " unexpected)

// ---- the trust ledger and the tier scheduler -----------------------------------
//
// Every test tier is declared with `testTier` and run by `runTiers`. Things that
// used to lose information or time:
//  * the pipeline stopped at the FIRST red test stage, so every later tier went
//    unrun and unreported — `integration host` stayed red for a day while two
//    later tiers were broken the whole time and nobody could see it;
//  * a stage's exit code was the only signal, and Expecto exits 0 for a run
//    that executed nothing;
//  * tiers ran one after another, so the gate took the SUM of every tier.
// Now every tier runs regardless of the others, each writes one registered/ran/
// verdict row to the ledger (Bozzetto.Tests TestInfrastructure.TrustSignal), and
// the "trust report" stage joins those rows with each tier's exit into ONE table
// and fails the pipeline on any tier that is not Trusted — including a tier whose
// process died before it could report. Where the filesystem can clone
// copy-on-write, tiers run CONCURRENTLY, each in a private clone of the built
// checkout mounted at the checkout's own path (build/TierPlan.fs explains why
// the mount is required), longest-expected tier first. `TrustSignalTests` fails
// the fast suite if a registered tier is not declared here, or if a test run
// bypasses `testTier`.

#load "build/TierPlan.fs"
open Bozzetto.Build

let trustLedger = Path.Combine(rootDir, "test-results", "trust-ledger.jsonl")
Directory.CreateDirectory(Path.GetDirectoryName trustLedger) |> ignore
if File.Exists trustLedger then File.Delete trustLedger

/// (tier, args, did the tier's process exit 0) for every tier this pipeline ran.
let invokedTiers = Collections.Generic.List<string * string * bool>()

let tierNameOf = TierPlan.nameOfArgs

/// Declare a test tier: one `dotnet Bozzetto.Tests.dll <args>` invocation.
let testTier (args: string) = TierPlan.tier args

/// Per-tier scratch, OUTSIDE the checkout: inside a tier's mount namespace the
/// checkout path shows that tier's clone, so anything the parent must read back
/// (ledger rows, logs) has to live elsewhere. These are disposable artifacts,
/// not a sibling project. Key the user cache by checkout path so worktrees and
/// same-named checkouts do not share scratch.
let tierWork =
  let cacheHome =
    match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" with
    | path when not (String.IsNullOrWhiteSpace path) && Path.IsPathFullyQualified path -> path
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
  let checkout = Path.TrimEndingDirectorySeparator(Path.GetFullPath rootDir)
  let key =
    SHA256.HashData(Text.Encoding.UTF8.GetBytes checkout)
    |> Convert.ToHexString
    |> fun hash -> hash.Substring(0, 16).ToLowerInvariant()
  let path = Path.GetFullPath(Path.Combine(cacheHome, "bozzetto", "tiers", $"{Path.GetFileName checkout}-{key}"))
  if path = checkout || path.StartsWith(checkout + string Path.DirectorySeparatorChar, StringComparison.Ordinal) then
    failwith "XDG_CACHE_HOME must place test-tier scratch outside the checkout."
  path

/// Recorded per-tier durations, for longest-first ordering. They live with the
/// results unless BOZZETTO_TIER_HISTORY names a persistent file.
let durationsFile =
  match Environment.GetEnvironmentVariable "BOZZETTO_TIER_HISTORY" with
  | null | "" -> Path.Combine(rootDir, "test-results", "tier-durations.json")
  | path -> path

/// Recorded per-SUITE seconds for the sharded host tier (TierPlan.assign).
let suiteDurationsFile =
  match Environment.GetEnvironmentVariable "BOZZETTO_SUITE_HISTORY" with
  | null | "" -> Path.Combine(tierWork, "suite-durations.json")
  | path -> path

let readJsonMap (path: string) : Map<string, float> =
  try JsonSerializer.Deserialize<Map<string, float>>(File.ReadAllText path)
  with _ -> Map.empty

let readDurations () = readJsonMap durationsFile

/// Run argv to completion with output drained to `log` (files cannot deadlock
/// a child the way an undrained pipe can). Returns the exit code.
/// Exit code for a tier the pipeline had to kill (same number `timeout(1)` uses).
let killedExitCode = 124

/// Run argv, streaming both pipes to `log`. The whole process tree is killed
/// on timeout or cancellation. It used to just stop waiting, and a hung tier
/// kept running for an hour after the stage was cancelled.
let execToLog (timeout: TimeSpan) (workingDir: string) (env: (string * string) list) (log: string) (argv: string list) =
  async {
    let! ct = Async.CancellationToken
    let scope = activeWorkLease
    let timeout =
      match scope with
      | Some lease -> min timeout (lease.Deadline - DateTimeOffset.UtcNow)
      | None -> timeout
    if timeout <= TimeSpan.Zero || (scope |> Option.exists (fun lease -> lease.Cancellation.IsCancellationRequested)) then
      File.WriteAllText(log, "KILLED: the owning work lease has no admitted time remaining.\n")
      return killedExitCode
    else
      let psi = Diagnostics.ProcessStartInfo(List.head argv)
      List.tail argv |> List.iter psi.ArgumentList.Add
      psi.WorkingDirectory <- workingDir
      psi.RedirectStandardOutput <- true
      psi.RedirectStandardError <- true
      for (k, v) in env do psi.Environment[k] <- v
      use writer = new StreamWriter(log, false)
      let gate = obj ()
      let write (line: string) = if not (isNull line) then lock gate (fun () -> writer.WriteLine line)
      use p = new Diagnostics.Process(StartInfo = psi)
      p.OutputDataReceived.Add(fun e -> write e.Data)
      p.ErrorDataReceived.Add(fun e -> write e.Data)
      p.Start() |> ignore
      p.BeginOutputReadLine()
      p.BeginErrorReadLine()
      let killTree () = try p.Kill(entireProcessTree = true) with _ -> ()
      use _ = ct.Register(fun () -> killTree ())
      use _ =
        match scope with
        | Some lease -> lease.Cancellation.Register(fun () -> killTree ())
        | None -> Unchecked.defaultof<Threading.CancellationTokenRegistration>
      try
        let exited = p.WaitForExitAsync()
        let! finished = Threading.Tasks.Task.WhenAny(exited, Threading.Tasks.Task.Delay timeout) |> Async.AwaitTask
        match obj.ReferenceEquals(finished, exited) with
        | true ->
          p.WaitForExit() // flush the async readers
          return p.ExitCode
        | false ->
          killTree ()
          p.WaitForExit()
          write (sprintf "KILLED: no exit after %.0fs. Timed out, not failed; see the log above for where it stopped." timeout.TotalSeconds)
          return killedExitCode
      finally
        if not p.HasExited then killTree ()
        p.WaitForExit()
  }

let private exitOf (argv: string list) =
  try
    let psi = Diagnostics.ProcessStartInfo(List.head argv)
    List.tail argv |> List.iter psi.ArgumentList.Add
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    use p = Diagnostics.Process.Start psi
    if p.WaitForExit(5000) then p.ExitCode
    else
      p.Kill(entireProcessTree = true)
      p.WaitForExit()
      killedExitCode
  with _ -> -1

/// CopyOnWrite only when BOTH a reflink clone and a rootless mount namespace
/// actually work here — probed, never assumed. Anything else runs serially.
let detectIsolation () =
  try
    Directory.CreateDirectory tierWork |> ignore
    let probe = Path.Combine(tierWork, ".reflink-probe")
    // Probe across the actual source/destination filesystems: a user cache
    // may be on a different volume where cloning the checkout cannot work.
    let reflink = exitOf [ "cp"; "--reflink=always"; Path.Combine(rootDir, "ci-pipeline.fsx"); probe ] = 0
    let userns = exitOf [ "unshare"; "--user"; "--map-root-user"; "--mount"; "--"; "true" ] = 0
    try File.Delete probe with _ -> ()
    // Either path under /tmp would be hidden by the tier's private /tmp mount.
    let outsideTmp =
      [ rootDir; tierWork ] |> List.forall (fun path -> path <> "/tmp" && not (path.StartsWith "/tmp/"))
    match reflink && userns && outsideTmp with
    | true -> TierPlan.CopyOnWrite
    | false -> TierPlan.Shared
  with _ -> TierPlan.Shared

let private procId (field: string) =
  File.ReadAllLines "/proc/self/status"
  |> Array.find (fun l -> l.StartsWith(field + ":"))
  |> fun l -> int (l.Split([| '\t'; ' ' |], StringSplitOptions.RemoveEmptyEntries).[1])

/// Run one tier (in its own clone when isolated) and record its outcome.
/// `slotIndex` is which of the `slots` concurrently-running workers is
/// running it (see `runTiers`) — NOT the tier's own index, since one slot
/// runs many tiers over the run, one after another, off the shared queue.
/// The port range is keyed to the slot for exactly that reason: whichever
/// tier a slot is running at a given moment is the only one using that
/// slot's range at that moment.
let runTier (isolation: TierPlan.Isolation) (slots: int) (slotIndex: int) (t: TierPlan.Tier) =
  async {
    let safe = TierPlan.fileNameOf t.Name
    let clone = Path.Combine(tierWork, safe)
    let log = Path.Combine(tierWork, safe + ".log")
    let ledger = Path.Combine(tierWork, safe + ".jsonl")
    let dataDir = Path.Combine(tierWork, safe + ".data")
    let tmpDir = Path.Combine(tierWork, safe + ".tmp")
    for d in [ clone; dataDir; tmpDir ] do
      if Directory.Exists d then Directory.Delete(d, true)
    if File.Exists ledger then File.Delete ledger
    Directory.CreateDirectory dataDir |> ignore
    Directory.CreateDirectory tmpDir |> ignore
    let portLo, portHi = TierPlan.portRangeOf slots slotIndex
    let env =
      [ "BOZZETTO_TRUST_LEDGER", ledger
        "BOZZETTO_DATA_DIR", dataDir
        "TMPDIR", tmpDir
        // A build node that outlives its tier could serve the next tier's build
        // from the wrong filesystem view; the private /tmp already hides it, and
        // this stops tiers leaving nodes behind at all.
        "MSBUILDDISABLENODEREUSE", "1"
        "BOZZETTO_SUITE_DURATIONS", suiteDurationsFile
        "BOZZETTO_SUITE_TIMINGS_OUT", Path.Combine(tierWork, safe + ".suites.json")
        // Every harness that spawns a real daemon reserves its ports through
        // TestPorts.reservePair(), which scans ONLY inside this range — see
        // build/TierPlan.fs `portRangeOf` for why disjoint-per-slot ranges
        // make a cross-tier port collision structurally impossible.
        "BOZZETTO_TEST_PORT_RANGE", $"{portLo}-{portHi}" ]
    let command = $"dotnet {testDll} {t.Args}"
    let tierTimeout = TierPlan.timeoutOf (readDurations ()) t
    let sw = Diagnostics.Stopwatch.StartNew()
    let! code =
      match isolation with
      | TierPlan.Shared -> execToLog tierTimeout rootDir env log [ "sh"; "-c"; command ]
      | TierPlan.CopyOnWrite ->
        async {
          match exitOf [ "cp"; "-a"; "--reflink=always"; rootDir; clone ] with
          | 0 ->
            let argv = TierPlan.isolatedArgv (procId "Uid") (procId "Gid") rootDir clone tmpDir command
            return! execToLog tierTimeout rootDir env log argv
          | failed ->
            File.WriteAllText(log, sprintf "could not clone the checkout for this tier (cp exit %d)" failed)
            return failed
        }
    let seconds = sw.Elapsed.TotalSeconds
    lock invokedTiers (fun () -> invokedTiers.Add((t.Name, t.Args, (code = 0))))
    let tail =
      match code with
      | 0 -> ""
      | _ ->
        File.ReadAllLines log
        |> Array.map (fun l -> Text.RegularExpressions.Regex.Replace(l, "\x1b\\[[0-9;?]*[A-Za-z]", ""))
        |> fun lines -> lines[max 0 (lines.Length - 60) ..]
        |> String.concat "\n"
    lock invokedTiers (fun () ->
      printfn "── tier %-28s exit=%d in %.0fs  (log: %s)" t.Name code seconds log
      if tail <> "" then printfn "%s\n── end of %s" tail t.Name)
    // A passing tier's clone is only scratch; a failing one is kept to inspect.
    if code = 0 && Directory.Exists clone then
      try Directory.Delete(clone, true) with _ -> ()
    return t.Name, seconds
  }

/// Run every tier, `slots` at a time, longest-expected first, then merge the
/// per-tier ledger rows into the one ledger the trust report reads.
let runTiers (tiers: TierPlan.Tier list) =
  async {
    let isolation = detectIsolation ()
    let requested =
      match Int32.TryParse(Environment.GetEnvironmentVariable "BOZZETTO_TIER_PARALLEL") with
      | true, n -> Some n
      | _ -> None
    let slots = TierPlan.parallelism isolation Environment.ProcessorCount requested
    let durations = readDurations ()
    let ordered = TierPlan.order durations tiers
    let estimate (t: TierPlan.Tier) = durations.TryFind t.Name |> Option.defaultValue 0.0
    let serial = ordered |> List.sumBy estimate
    printfn "Tiers: %d, %d at a time (%A), order: %s" tiers.Length slots isolation
      (ordered |> List.map (fun t -> t.Name) |> String.concat ", ")
    if serial > 0.0 then
      printfn "Expected wall clock %.0fs (serial would be %.0fs), from recorded durations"
        (TierPlan.makespan slots estimate ordered) serial
    let queue = Collections.Concurrent.ConcurrentQueue<TierPlan.Tier>(ordered)
    let worker (slotIndex: int) =
      async {
        let results = ResizeArray()
        let mutable next = Unchecked.defaultof<TierPlan.Tier>
        while queue.TryDequeue(&next) do
          let! r = runTier isolation slots slotIndex next
          results.Add r
        return List.ofSeq results
      }
    // Catch each worker independently and join all of them before returning
    // a failure. Async.Parallel must not abandon a sibling's owned process.
    let! outcomes = List.init slots worker |> List.map Async.Catch |> Async.Parallel
    let measured = outcomes |> Array.choose (function Choice1Of2 value -> Some value | Choice2Of2 _ -> None)
    // Merge ledgers (per-tier files: separate processes never share a writer).
    for t in tiers do
      let ledger = Path.Combine(tierWork, TierPlan.fileNameOf t.Name + ".jsonl")
      if File.Exists ledger then File.AppendAllText(trustLedger, File.ReadAllText ledger)
    // Per-suite timings from every shard feed the next run's balancing.
    let suiteTimings =
      tiers
      |> List.map (fun t -> Path.Combine(tierWork, TierPlan.fileNameOf t.Name + ".suites.json"))
      |> List.filter File.Exists
      |> List.fold (fun (acc: Map<string, float>) f ->
        readJsonMap f |> Map.fold (fun (m: Map<string, float>) k v -> m.Add(k, v)) acc) (readJsonMap suiteDurationsFile)
    try File.WriteAllText(suiteDurationsFile, JsonSerializer.Serialize suiteTimings) with _ -> ()
    let updated =
      measured |> Seq.concat |> Seq.fold (fun (m: Map<string, float>) (n, s) -> m.Add(n, s)) durations
    try
      Directory.CreateDirectory(Path.GetDirectoryName durationsFile) |> ignore
      File.WriteAllText(durationsFile, JsonSerializer.Serialize updated)
    with _ -> ()
    match outcomes |> Array.tryPick (function Choice2Of2 error -> Some error | Choice1Of2 _ -> None) with
    | Some error -> return raise error
    | None -> return ()
  }

type TrustLine =
  { Tier: string
    Registered: string
    Ran: string
    Passed: string
    Failed: string
    Errored: string
    Ignored: string
    Verdict: string
    Detail: string
    Red: bool }

/// Join what the pipeline invoked with what each test process reported.
let trustLines () =
  let rows =
    match File.Exists trustLedger with
    | false -> []
    | true ->
      File.ReadAllLines trustLedger
      |> Array.filter (fun l -> l.Trim() <> "")
      |> Array.map (fun l -> JsonDocument.Parse(l).RootElement.Clone())
      |> Array.toList
  let str (e: JsonElement) (name: string) =
    match e.GetProperty(name).ValueKind with
    | JsonValueKind.Number -> string (e.GetProperty(name).GetInt32())
    | _ -> e.GetProperty(name).GetString()
  [ for (tier, _args, stepOk) in List.ofSeq invokedTiers ->
      match rows |> List.filter (fun r -> str r "Tier" = tier) |> List.tryLast with
      | None ->
        { Tier = tier; Registered = "?"; Ran = "?"; Passed = "?"; Failed = "?"; Errored = "?"; Ignored = "?"
          Verdict = "NoReport"
          Detail =
            match stepOk with
            | true -> "the step passed but the process reported nothing"
            | false -> "the process failed before reporting (runner setup or crash)"
          Red = true }
      | Some r ->
        let verdict = str r "Verdict"
        let greenVerdict = verdict = "Trusted" || verdict = "SurvivorsUnderBar"
        { Tier = tier
          Registered = str r "Registered"
          Ran = str r "Ran"
          Passed = str r "Passed"
          Failed = str r "Failed"
          Errored = str r "Errored"
          Ignored = str r "Ignored"
          Verdict = match greenVerdict, stepOk with | true, false -> "ExitMismatch" | _ -> verdict
          Detail =
            match greenVerdict, stepOk with
            | true, false -> "reported green, but the process exited non-zero"
            | _ -> str r "Detail"
          Red = not (greenVerdict && stepOk) } ]

let renderTrustTable (lines: TrustLine list) =
  let header =
    [ "| Tier | Registered | Ran | Passed | Failed | Errored | Ignored | Verdict | Detail |"
      "|---|---:|---:|---:|---:|---:|---:|---|---|" ]
  let body =
    lines
    |> List.map (fun l ->
      let mark = match l.Red with true -> "❌" | false -> "✅"
      $"| {l.Tier} | {l.Registered} | {l.Ran} | {l.Passed} | {l.Failed} | {l.Errored} | {l.Ignored} | {mark} {l.Verdict} | {l.Detail} |")
  String.concat "\n" ("## Test trust report" :: "" :: header @ body)

// ---- the pipeline ------------------------------------------------------------

/// Run shell steps in order, stopping at the first failure.
let rec runSteps (runCommand: string -> Async<Result<unit, string>>) (steps: string list) =
  async {
    match steps with
    | [] -> return Ok()
    | step :: rest ->
      match! runCommand step with
      | Ok () -> return! runSteps runCommand rest
      | Error e -> return Error e
  }

pipeline "bozzetto" {
  description
    "Bozzetto build and test pipeline: build once in Release, then run every \
     check --no-build off that output: format, client contract tests, the test \
     tiers and the trust report. `ci` adds the mutation gate, `composer` the \
     Composer provider tier, `pack` the CLI tool package. Nothing is published."
  workingDir rootDir
  timeout 3600
  timeoutForStep 900
  collapseGithubActionLogs

  stage "build" {
    // Build the whole solution ONCE, in Release. Every downstream stage runs
    // --no-build against this exact output.
    run (leasedCommand "full_build" "dotnet build -c Release")
  }

  stage "build composer provider" {
    whenCmdArg "composer"
    run (fun ctx ->
      async {
        let distribution = Environment.GetEnvironmentVariable "BOZZETTO_COMPOSER_DISTRIBUTION"
        let fixture = Environment.GetEnvironmentVariable "BOZZETTO_COMPOSER_FIXTURE"
        if String.IsNullOrWhiteSpace distribution || not (Path.IsPathFullyQualified distribution)
           || not (File.Exists(Path.Combine(distribution, "Composer.dll"))) then
          return Error "composer requires BOZZETTO_COMPOSER_DISTRIBUTION pointing to built compiler binaries"
        elif String.IsNullOrWhiteSpace fixture || not (Path.IsPathFullyQualified fixture) || not (File.Exists fixture) then
          return Error "composer requires BOZZETTO_COMPOSER_FIXTURE pointing to IncrementalScalarRegions.fidproj"
        else
          Environment.SetEnvironmentVariable("BOZZETTO_COMPOSER_WORKER", Path.Combine(rootDir, "Bozzetto.Composer/bin/Release/net10.0/Bozzetto.Composer.dll"))
          return! ctx.RunCommand (leasedCommand "full_build" $"dotnet build Bozzetto.Composer/Bozzetto.Composer.fsproj -c Release -p:ComposerDistribution=\"{distribution}\"")
      })
  }

  stage "format" {
    run (leasedCommand "full_build" "dotnet format --verify-no-changes --verbosity minimal")
  }

  stage "build samples for integration suites" {
    // The HTTP API integration suites create real sessions on these samples.
    //
    // A session on an UNBUILT sample does not fail loudly — it warms up,
    // cannot find the DLL, and faults, so the test reports "session should
    // reach Ready ... Actual value was false". That is what a missing entry
    // here looks like from the outside, and it cost a full CI cycle when
    // McpAppRunOutcomeTests started sessioning on ConsoleTicker without one.
    // `Architecture — every sample an integration suite sessions on is built
    // by CI` now fails the fast local suite instead of waiting for CI.
    run (leasedCommand "full_build" "dotnet build samples/from-csharp/Bozzetto.Samples.FromCSharp/Bozzetto.Samples.FromCSharp.fsproj -c Release --nologo")
    run (leasedCommand "full_build" "dotnet build samples/demos/Bozzetto.Samples.ConsoleTicker/Bozzetto.Samples.ConsoleTicker.fsproj -c Release --nologo")
  }

  stage "vscode extension compile" {
    // Build the extension for the client contract tests.
    workingDir vscodeDir
    run (leasedCommand "full_build" "dotnet tool restore")
    run (leasedCommand "full_build" "npm ci --include=dev")
    run (leasedCommand "full_build" "npm run compile")
  }

  stage "vscode client contract tests" {
    // Real gates that existed but ran nowhere (outcome-gate-sweep.md Gap
    // B.4): the golden server->client SSE round trip (loads the REAL
    // fable-out/LiveTestingListener.js built by "vscode extension compile"
    // above and feeds it the committed fixtures) plus every standalone
    // bozzetto-vscode/tests/*.fsx contract test. Structural, not an enumerated
    // list, so a new *.fsx contract test is picked up here by construction —
    // the same "join CI by construction" discipline TestInfrastructure.
    // Integration.hostList applies on the .NET side.
    workingDir vscodeDir
    timeoutForStep 300
    run (leasedCommand "test_suite_run" "npm run test:golden")
    run (fun ctx ->
      async {
        let fsxFiles =
          Directory.GetFiles(Path.Combine(vscodeDir, "tests"), "*.fsx")
          |> Array.sort
        return! runSteps ctx.RunCommand [ for f in fsxFiles -> leasedCommand "test_suite_run" $"dotnet fsi \"{f}\"" ]
      })
  }

  stage "test tiers" {
    // EVERY test tier, each once, whatever happens to the others — scheduled by
    // runTiers (longest expected first; concurrently in private copy-on-write
    // clones when this machine supports it, else one at a time). Judged by the
    // "trust report" stage below, not by this step's exit: a red tier must
    // never hide the tiers after it.
    //
    //   always:   the default suite and retained integration-host suites
    //             (component FSI, real daemons, provider-refusal boundaries).
    //             Retired F# product journeys are reported separately by the
    //             test registry and never count as passed or ignored evidence.
    //   `ci`:     the mutation-score gate.
    timeoutForStep 5400
    run (fun _ ->
      withTestSuiteLease (fun () -> async {
        let ci = fsi.CommandLineArgs |> Array.contains "ci"
        // The host tier is sharded: its suites are sequenced WITHIN a process
        // (shared in-process state), not across processes, so each shard is its
        // own concurrent tier. BOZZETTO_HOST_SHARDS overrides the count.
        let hostShards =
          match Int32.TryParse(Environment.GetEnvironmentVariable "BOZZETTO_HOST_SHARDS") with
          | true, n when n >= 1 -> n
          | _ -> 5
        let always =
          testTier "--summary"
          :: [ for k in 1 .. hostShards -> testTier $"--integration-host --shard {k}/{hostShards} --summary" ]
        let ciOnly = [ testTier "--mutation-score" ]
        let runnable =
          match ci with
          | false -> always
          | true -> always @ ciOnly
        let composerTiers =
          if fsi.CommandLineArgs |> Array.contains "composer" then
            [ testTier "--integration-composer --summary" ]
          else []
        do! runTiers (runnable @ composerTiers)
        return Ok()
      }))
  }

  stage "trust report" {
    // ONE table for every tier this run invoked: registered vs ran vs result,
    // plus the verdict. Fails the pipeline if any tier is not Trusted —
    // failed, errored, ran nothing, ran a different count than it registered,
    // or never reported at all. Also appended to the CI step summary when
    // GITHUB_STEP_SUMMARY is set.
    run (fun _ ->
      async {
        let lines = trustLines ()
        let table = renderTrustTable lines
        printfn "%s" table
        // Kept beside the ledger.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName trustLedger, "trust-report.md"), table + "\n")
        match Environment.GetEnvironmentVariable "GITHUB_STEP_SUMMARY" with
        | null | "" -> ()
        | summary -> File.AppendAllText(summary, table + "\n")
        match lines |> List.filter (fun l -> l.Red) with
        | [] when lines.IsEmpty -> return Error "trust report: no test tier ran at all"
        | [] -> return Ok()
        | red ->
          return
            Error(
              sprintf "trust report: %d of %d tier(s) not trusted: %s" red.Length lines.Length
                (red |> List.map (fun l -> $"{l.Tier} ({l.Verdict})") |> String.concat ", "))
      })
  }

  stage "pack" {
    // Pack the CLI tool into nupkg/ and check the package's payload. Runs only
    // after the trust report, so it packs a trusted build. Nothing is
    // published from this pipeline.
    whenCmdArg "pack"
    run (leasedCommand "full_build" "dotnet pack Bozzetto -c Release -o nupkg")
    run (fun _ -> async { verifyToolInstallable (); return Ok() })
  }

  runIfOnlySpecified false
}

tryPrintPipelineCommandHelp ()
