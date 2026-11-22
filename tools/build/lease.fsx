// Run one command under a work lease, through the same client ci-pipeline.fsx uses:
//
//   dotnet fsi tools/build/lease.fsx full_build dotnet build
//   dotnet fsi tools/build/lease.fsx test_suite_run dotnet <testDll> --summary
//   dotnet fsi tools/build/lease.fsx --canary hello --receipt-path out.txt full_build <cmd> [args...]
//
// The holder is "BOZZETTO_AGENT · BOZZETTO_PROJECT" (default: the current directory). A deferred
// ask waits in line as the same ask; the lease renews while the command runs; the command's
// process group is killed if a watchdog fuse trips or the daemon reports the lease gone; the
// lease is released at the end.
// Exit status: the command's own (124 on wall fuse breach, 137 on RSS fuse breach), 64 for bad
// usage or invalid watchdog config, 69 when the lease is refused or withdrawn.
#load "../../src/bozzetto-web/src/Shared/Protocol.fs"
#load "../../src/bozzetto-web/src/Shared/BinaryCodec.fs"
#load "../../src/Bozzetto.Core/OwnerMonitor.fs"
#load "../../src/Bozzetto.Core/ProcessWatchdog.fs"
#load "../../src/Bozzetto/LinuxProcessEdge.fs"
#load "WatchdogCli.fs"
#load "WorkLease.fs"

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Bozzetto
open Bozzetto.Build
open Bozzetto.Server
open Bozzetto.Web.Shared
open Bozzetto.Web.Shared.Protocol

let private streamTailLimit = 16384
let private receiptStreamLimit = 4 * 1024 * 1024

let reportCommand phase observation =
  async {
    try
      let bytes = BinaryCodec.encodeCommandObservationRequest observation

      match! WorkLease.observeCommand CommandObservationMediaType phase bytes with
      | Error reason -> return Error reason
      | Ok(status, bytes) ->
        match BinaryCodec.decodeCommandObservationReply bytes with
        | Ok(CommandObservationAccepted _) when status = 200 -> return Ok()
        | Ok(CommandObservationRefused reason) -> return Error reason
        | Error err when err.Contains("unsupported version", StringComparison.OrdinalIgnoreCase) ->
          return Error $"protocol_mismatch: command/{phase} HTTP {status}; {err}"
        | Error err ->
          return Error $"command/{phase}: HTTP {status}; invalid binary acknowledgement ({err})"
        | _ -> return Error $"command/{phase}: HTTP {status}; invalid binary acknowledgement"
    with error ->
      return Error $"command/{phase}: {error.Message}"
  }

let executablePath (command: string) =
  if Path.IsPathRooted command || command.Contains(Path.DirectorySeparatorChar) then
    Path.GetFullPath command
  else
    (Environment.GetEnvironmentVariable "PATH"
     |> Option.ofObj
     |> Option.defaultValue "")
      .Split(Path.PathSeparator)
    |> Array.map (fun directory -> Path.GetFullPath(Path.Combine(directory, command)))
    |> Array.tryFind File.Exists
    |> Option.defaultWith (fun () -> invalidOp $"Executable not found on PATH: {command}")

// Tee each stream to the caller while retaining only the journal's bounded tail.
// Reading starts before waiting for the process, so neither pipe can deadlock it.
let drain (reader: StreamReader) (writer: TextWriter) (captureReceipt: bool) =
  let rec loop (tail: string) truncated (receiptBuf: System.Text.StringBuilder option) receiptTruncated =
    async {
      let buffer = Array.zeroCreate<char> 4096
      let! count = reader.ReadAsync(buffer, 0, buffer.Length) |> Async.AwaitTask

      if count = 0 then
        let receiptText =
          match receiptBuf with
          | Some sb ->
            if receiptTruncated then sb.ToString() + "\n[cut: receipt output exceeded 4 MiB limit]"
            else sb.ToString()
          | None -> tail
        return tail, truncated, receiptText
      else
        let text = String(buffer, 0, count)
        do! writer.WriteAsync text |> Async.AwaitTask
        let combined = tail + text

        let retained =
          if combined.Length > streamTailLimit then
            "[cut: earlier command output omitted]\n"
            + combined.Substring(combined.Length - streamTailLimit)
          else
            combined

        let nextReceiptTruncated =
          match receiptBuf with
          | Some sb ->
            let remaining = receiptStreamLimit - sb.Length
            if remaining <= 0 then true
            elif text.Length <= remaining then
              sb.Append text |> ignore
              receiptTruncated
            else
              sb.Append(text, 0, remaining) |> ignore
              true
          | None -> false

        return! loop retained (truncated || combined.Length > streamTailLimit) receiptBuf nextReceiptTruncated
    }
  let buf = if captureReceipt then Some(System.Text.StringBuilder()) else None
  loop "" false buf false

type private WatchdogRunState = {
  mutable PeakRssBytes: int64
  mutable FuseBreach: ProcessWatchdog.FuseBreach option
  mutable WatchdogScanError: ProcessWatchdog.ProcScanError option
  mutable Termination: ProcessWatchdog.TerminationOutcome
  mutable TerminationRequested: bool
  mutable ChildExited: bool
  mutable ExitCode: int
}

let envOpt name = Environment.GetEnvironmentVariable name |> Option.ofObj

let parsed =
  WatchdogCli.parseWatchdogEnv
    (envOpt "BOZZETTO_CANARY")
    (envOpt "BOZZETTO_WALL_LIMIT_SECS")
    (envOpt "BOZZETTO_RSS_LIMIT_MB")
    (envOpt "BOZZETTO_WATCHDOG_RECEIPT")
  |> Result.bind (fun envOpts ->
    WatchdogCli.parsePinZ13Env (envOpt "BOZZETTO_PIN_Z13")
    |> Result.map (fun pinZ13 -> { envOpts with PinZ13 = pinZ13 }))
  |> Result.bind (fun envOpts ->
    WatchdogCli.parseWatchdogArgs envOpts (fsi.CommandLineArgs |> Array.toList |> List.tail))

match parsed with
| Error err ->
  eprintfn "work-lease: %s" (WatchdogCli.WatchdogOptionError.describe err)
  exit 64
| Ok(options, kind :: command :: args) when kind = "full_build" || kind = "test_suite_run" ->
  let limits = options.Limits
  let receiptPath = options.ReceiptPath

  if not (OperatingSystem.IsLinux()) && limits.RssLimitBytes.IsSome then
    eprintfn
      "work-lease: --rss-limit-mb is unsupported on this platform (%s)."
      (ProcessWatchdog.describeProcScanError ProcessWatchdog.ProcScanError.UnsupportedPlatform)
    exit 64

  let setsidPath =
    if OperatingSystem.IsLinux() then
      match LinuxProcessEdge.tryResolveSetsid () with
      | Some path -> Some path
      | None ->
        eprintfn "work-lease: setsid was not found on PATH; Linux leased runs require process-group isolation."
        exit 64
    else None

  let command = executablePath command
  let monitorActive = limits <> ProcessWatchdog.noLimits || receiptPath.IsSome

  let project =
    match Environment.GetEnvironmentVariable "BOZZETTO_PROJECT" with
    | null | "" -> Environment.CurrentDirectory
    | path -> Path.GetFullPath path

  let holder = WorkLease.holder project
  let state = {
    PeakRssBytes = 0L
    FuseBreach = None
    WatchdogScanError = None
    Termination =
      if OperatingSystem.IsLinux() then ProcessWatchdog.TerminationOutcome.Cleared
      else ProcessWatchdog.TerminationOutcome.Unverified ProcessWatchdog.ProcScanError.UnsupportedPlatform
    TerminationRequested = false
    ChildExited = false
    ExitCode = 0
  }

  let work leaseId (gone: CancellationToken) =
    async {
      let start =
        match setsidPath with
        | Some setsid ->
          let psi = ProcessStartInfo(setsid)
          psi.ArgumentList.Add "--wait"
          psi.ArgumentList.Add command
          psi
        | None -> ProcessStartInfo(command)
      start.UseShellExecute <- false
      start.RedirectStandardOutput <- true
      start.RedirectStandardError <- true
      let runId = Guid.NewGuid().ToString("N")
      start.Environment["BOZZETTO_LEASED_RUN_ID"] <- runId
      if options.CanaryPreset.IsSome then
        if String.IsNullOrEmpty(Environment.GetEnvironmentVariable "CLEF_PHASE_TIMING") then
          start.Environment["CLEF_PHASE_TIMING"] <- "1"
        if String.IsNullOrEmpty(Environment.GetEnvironmentVariable "CLEF_BUDGET_GATE") then
          start.Environment["CLEF_BUDGET_GATE"] <- "1"
      args |> List.iter start.ArgumentList.Add

      let started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
      let stopwatch = Stopwatch.StartNew()
      use child = Process.Start start
      // Capture root StartTimeTicks while child is unreaped so post-exit sweeps stay
      // fenced against PID reuse. Only a setsid-isolated child owns its own PGID.
      let targetPgid = if setsidPath.IsSome then Some child.Id else None
      let rootStartTicks =
        if OperatingSystem.IsLinux() then LinuxProcessEdge.readLinuxProc child.Id |> Option.map _.StartTimeTicks
        else None
      let killGate = obj ()
      use killLock = new SemaphoreSlim(1, 1)
      let mutable cancelTerminationTask : Task option = None

      let sampleOnce () =
        if not (OperatingSystem.IsLinux()) then
          if limits.RssLimitBytes.IsSome then Error ProcessWatchdog.ProcScanError.UnsupportedPlatform
          else Ok 0L
        else
          match LinuxProcessEdge.readLinuxProcesses false with
          | Ok procs ->
            let rss = ProcessWatchdog.subtreeRssBytes child.Id targetPgid procs
            lock killGate (fun () ->
              if rss > state.PeakRssBytes then state.PeakRssBytes <- rss)
            Ok rss
          | Error err -> Error err

      let terminateTreeAsync graceMs = async {
        do! killLock.WaitAsync() |> Async.AwaitTask
        try
          let shouldKill =
            lock killGate (fun () ->
              if state.ChildExited then false
              else
                state.TerminationRequested <- true
                true)
          if shouldKill then
            if OperatingSystem.IsLinux() then
              // Capture RSS before killing so cancelled runs without --canary still record peak RSS.
              sampleOnce () |> ignore
              let! outcome = LinuxProcessEdge.terminateProcessGroupWithFenceAsync child.Id targetPgid rootStartTicks graceMs
              lock killGate (fun () -> state.Termination <- outcome)
            else
              lock killGate (fun () ->
                state.Termination <- ProcessWatchdog.TerminationOutcome.Unverified ProcessWatchdog.ProcScanError.UnsupportedPlatform)
            try
              if not child.HasExited then child.Kill(entireProcessTree = true)
            with :? InvalidOperationException -> ()
        finally
          killLock.Release() |> ignore
      }

      let triggerCancellationCleanup () =
        let shouldStart =
          lock killGate (fun () ->
            if state.ChildExited then false
            else
              state.TerminationRequested <- true
              cancelTerminationTask.IsNone)
        if shouldStart then
          let task = Async.StartAsTask(terminateTreeAsync 500) :> Task
          lock killGate (fun () ->
            if cancelTerminationTask.IsNone then cancelTerminationTask <- Some task)

      use goneReg = gone.Register(fun () -> triggerCancellationCleanup ())
      use stopWatchdog = new CancellationTokenSource()

      let watchdog =
        if monitorActive then
          Some(
            Async.StartAsTask(
              async {
                while not stopWatchdog.IsCancellationRequested && not child.HasExited do
                  let scan = sampleOnce ()
                  match scan with
                  | Error err ->
                    lock killGate (fun () ->
                      state.WatchdogScanError <- Some err
                      state.Termination <- ProcessWatchdog.TerminationOutcome.Unverified err)
                    eprintfn "work-lease: watchdog /proc scan failed (%s); terminating process group %A" (ProcessWatchdog.describeProcScanError err) targetPgid
                    do! terminateTreeAsync 500
                    stopWatchdog.Cancel()
                  | Ok _ ->
                    let peak = lock killGate (fun () -> state.PeakRssBytes)
                    match ProcessWatchdog.evaluateFuse limits stopwatch.Elapsed.TotalSeconds peak with
                    | Some breach ->
                      lock killGate (fun () -> state.FuseBreach <- Some breach)
                      eprintfn "work-lease: watchdog fuse breached (%s); terminating process group %A" (ProcessWatchdog.describeBreach (Some breach)) targetPgid
                      do! terminateTreeAsync 500
                      stopWatchdog.Cancel()
                    | None ->
                      try
                        do! Async.Sleep 250
                      with :? OperationCanceledException when stopWatchdog.IsCancellationRequested -> ()
              },
              cancellationToken = stopWatchdog.Token))
        else None

      let! stdout = Async.StartChild(drain child.StandardOutput Console.Out receiptPath.IsSome)
      let! stderr = Async.StartChild(drain child.StandardError Console.Error receiptPath.IsSome)

      let observed: CommandObservationRequest =
        {
          Holder = holder
          Project = project
          Kind = kind
          LeaseId = leaseId
          RunId = runId
          Executable = command
          Arguments = List.toArray args
          ProcessId = child.Id
          StartedAtMs = started
          Observation = CommandStarted
        }

      let! announced = reportCommand "start" observed

      match announced with
      | Error reason ->
        eprintfn "work-lease: command observation refused: %s" reason
        do! terminateTreeAsync 500
      | Ok() -> ()

      // Wait for the child to exit (cancellation triggers triggerCancellationCleanup above).
      do! child.WaitForExitAsync() |> Async.AwaitTask
      stopwatch.Stop()
      // Unregister lease-loss callback immediately once child is reaped so a
      // late cancellation during settlement never touches a recycled PID.
      goneReg.Dispose()
      stopWatchdog.Cancel()

      match watchdog with
      | Some task ->
        try
          do! task |> Async.AwaitTask
        with
        | :? OperationCanceledException when stopWatchdog.IsCancellationRequested -> ()
        | :? AggregateException as agg when stopWatchdog.IsCancellationRequested && (agg.InnerExceptions |> Seq.forall (fun e -> e :? OperationCanceledException)) -> ()
      | None -> ()

      // Join any cancellation-initiated termination task BEFORE disposing killLock.
      match lock killGate (fun () -> cancelTerminationTask) with
      | Some task -> do! task |> Async.AwaitTask
      | None -> ()

      // Serialize StartTimeTicks-fenced post-exit process-group cleanup & survivor verification
      // under killLock before marking ChildExited. Leased work stays leased: no descendant in
      // the isolated setsid PGID may outlive the lease, zero blind kill(-pgid) runs when empty,
      // and recycled PIDs are never signaled.
      do! killLock.WaitAsync() |> Async.AwaitTask
      try
        if OperatingSystem.IsLinux() && targetPgid.IsSome then
          let! finalOutcome = LinuxProcessEdge.terminateProcessGroupWithFenceAsync child.Id targetPgid rootStartTicks 100
          lock killGate (fun () -> state.Termination <- finalOutcome)
        lock killGate (fun () -> state.ChildExited <- true)
      finally
        killLock.Release() |> ignore

      let breach, scanError, termination, peakRss, wasTerminated =
        lock killGate (fun () ->
          state.FuseBreach, state.WatchdogScanError, state.Termination, state.PeakRssBytes, state.TerminationRequested)

      let terminationFailed =
        match termination with
        | ProcessWatchdog.TerminationOutcome.Cleared -> false
        | ProcessWatchdog.TerminationOutcome.Survivors _ -> true
        | ProcessWatchdog.TerminationOutcome.Unverified _ -> OperatingSystem.IsLinux() || wasTerminated

      let resolvedExit =
        match breach with
        | Some(ProcessWatchdog.FuseBreach.WallLimitExceeded _) -> 124
        | Some(ProcessWatchdog.FuseBreach.RssLimitExceeded _) -> 137
        | None when scanError.IsSome || terminationFailed -> 1
        | None -> child.ExitCode

      lock killGate (fun () -> state.ExitCode <- resolvedExit)
      let! (output, outputTruncated, receiptOutput) = stdout
      let! (error, errorTruncated, receiptError) = stderr

      let receipt: WatchdogCli.WatchdogReceipt =
        {
          RootPid = child.Id
          TargetPgid = targetPgid
          WallElapsedMs = stopwatch.ElapsedMilliseconds
          PeakRssBytes = peakRss
          Breach = breach
          ScanError = scanError
          ExitCode = resolvedExit
          Termination = termination
        }

      let receiptLine = WatchdogCli.formatReceipt receipt
      receiptPath
      |> Option.iter (fun path ->
        let parent = Path.GetDirectoryName path
        if not (String.IsNullOrWhiteSpace parent) then Directory.CreateDirectory parent |> ignore
        let sections =
          [ yield receiptLine
            if not (String.IsNullOrEmpty receiptOutput) then
              yield "--- STDOUT ---"
              yield receiptOutput.TrimEnd()
            if not (String.IsNullOrEmpty receiptError) then
              yield "--- STDERR ---"
              yield receiptError.TrimEnd() ]
        File.WriteAllText(path, String.concat "\n" sections + "\n"))

      let finalError, finalErrorTruncated =
        if breach.IsSome || scanError.IsSome || terminationFailed || wasTerminated || receiptPath.IsSome then
          eprintfn "%s" receiptLine
          WatchdogCli.appendBoundedReceipt streamTailLimit error receiptLine errorTruncated
        else error, errorTruncated

      let result =
        {
          FinishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
          ExitCode = resolvedExit
          StandardOutput = output
          StandardError = finalError
          OutputTruncated = outputTruncated
          ErrorTruncated = finalErrorTruncated
        }

      let! settled =
        reportCommand
          "finish"
          { observed with
              Observation = CommandFinished result
          }
      // Settlement precedes lease release, after the process and both streams join.
      // A reporting failure remains visible even when the command itself succeeded.
      match announced, settled with
      | _, Error reason ->
        eprintfn "work-lease: command result was not recorded: %s" reason
        return Error reason
      | Error reason, _ -> return Error reason
      | Ok(), Ok() ->
        match breach, scanError, termination with
        | Some b, _, _ ->
          return Error $"{command} terminated by watchdog: {ProcessWatchdog.describeBreach (Some b)}"
        | None, Some err, _ ->
          return Error $"{command} watchdog /proc scan failed: {ProcessWatchdog.describeProcScanError err}"
        | None, None, ProcessWatchdog.TerminationOutcome.Survivors(pids, errs) ->
          return Error $"{command} left surviving descendants after termination: pids={pids} errors={errs}"
        | None, None, ProcessWatchdog.TerminationOutcome.Unverified _ when not (OperatingSystem.IsLinux()) && not wasTerminated ->
          return
            if resolvedExit = 0 then Ok()
            else Error $"{command} exited {resolvedExit}"
        | None, None, ProcessWatchdog.TerminationOutcome.Unverified err ->
          return Error $"{command} could not verify child termination: {ProcessWatchdog.describeProcScanError err}"
        | None, None, ProcessWatchdog.TerminationOutcome.Cleared ->
          return
            if resolvedExit = 0 then Ok()
            else Error $"{command} exited {resolvedExit}"
    }

  use hostCancel = new CancellationTokenSource()
  let registerSignal (signal: PosixSignal) =
    if OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() then
      try
        Some(
          PosixSignalRegistration.Create(
            signal,
            fun ctx ->
              ctx.Cancel <- true
              try hostCancel.Cancel() with _ -> ()))
      with _ -> None
    else None
  let sigTermReg = registerSignal PosixSignal.SIGTERM
  let sigIntReg = registerSignal PosixSignal.SIGINT
  try
    match
      WorkLease.runWithCancellationPinned options.PinZ13 kind holder hostCancel.Token work
      |> Async.RunSynchronously
    with
    | Ok() -> exit 0
    | Error _ when state.ExitCode <> 0 -> exit state.ExitCode
    | Error reason ->
      eprintfn "%s" reason
      exit 69
  finally
    sigTermReg |> Option.iter _.Dispose()
    sigIntReg |> Option.iter _.Dispose()
| _ ->
  eprintfn "usage: dotnet fsi tools/build/lease.fsx [--canary hello|hello-p3|sqlite|challenge|challenge-p3] [--wall-limit-secs N] [--rss-limit-mb N] [--receipt-path PATH] [--pin-z13] <full_build|test_suite_run> <command> [args...]"
  exit 64
