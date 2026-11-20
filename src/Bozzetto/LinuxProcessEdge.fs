namespace Bozzetto.Server

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open Bozzetto

// Zone B Linux /proc and POSIX kill(2)/sysconf(2) edge adapter.
// Isolated outside Bozzetto.Core so Bozzetto.Simulation stays 100% pure/no-IO.
// Scan and signal failures propagate as explicit Result/TerminationOutcome values.
module LinuxProcessEdge =

  [<DllImport("libc", EntryPoint = "kill", SetLastError = true)>]
  extern int private posixKill(int pid, int signal)

  [<DllImport("libc", EntryPoint = "sysconf", SetLastError = true)>]
  extern int64 private posixSysconf(int name)

  [<Literal>]
  let private Esrch = 3

  [<Literal>]
  let private ScClkTck = 2

  let private ticksGate = obj ()
  let mutable private cachedClockTicksPerSec : int64 option = None
  let mutable private cachedSelfPgid : int option = None

  // Read /proc/stat btime fresh on demand so NTP clock steps or suspend/resume
  // never drift a cached boot time, and transient read failures never latch None.
  let readBootTimeMs () : int64 option =
    if not (OperatingSystem.IsLinux()) then None
    else
      try
        ProcessWatchdog.parseBootTimeMs (File.ReadAllText "/proc/stat")
      with _ -> None

  // Cache only a valid positive sysconf(_SC_CLK_TCK) result; retry while unset.
  let clockTicksPerSecond () : int64 option =
    if not (OperatingSystem.IsLinux()) then None
    else
      match lock ticksGate (fun () -> cachedClockTicksPerSec) with
      | Some ticks -> Some ticks
      | None ->
        try
          let v = posixSysconf ScClkTck
          if v > 0L then
            lock ticksGate (fun () -> cachedClockTicksPerSec <- Some v)
            Some v
          else None
        with _ -> None

  let tryResolveSetsid () : string option =
    if not (OperatingSystem.IsLinux()) then None
    else
      let candidates =
        [ yield "/usr/bin/setsid"
          yield "/bin/setsid"
          let pathEnv = Environment.GetEnvironmentVariable "PATH"
          if not (String.IsNullOrWhiteSpace pathEnv) then
            for dir in pathEnv.Split(Path.PathSeparator) do
              if not (String.IsNullOrWhiteSpace dir) then
                yield Path.Combine(dir, "setsid") ]
      candidates |> List.tryFind File.Exists

  let private readLinuxProcWith (procRoot: string) (readText: string -> string) (bootMsOpt: int64 option) (clkTck: int64) (includeCmdline: bool) (strictCmdline: bool) (pid: int) : ProcessWatchdog.ProcProbeObservation =
    if not (OperatingSystem.IsLinux()) || pid <= 0 then ProcessWatchdog.ProcProbeObservation.Absent
    else
      let pidDir = Path.Combine(procRoot, string pid)
      let statPath = Path.Combine(pidDir, "stat")
      try
        let statText = readText statPath
        match ProcessWatchdog.parseProcStat (int64 Environment.SystemPageSize) bootMsOpt clkTck statText with
        | None -> ProcessWatchdog.ProcProbeObservation.Inaccessible(sprintf "Malformed /proc/%d/stat" pid)
        | Some record ->
          if not includeCmdline then ProcessWatchdog.ProcProbeObservation.Present record
          else
            let cmdlinePath = Path.Combine(pidDir, "cmdline")
            try
              let argv = readText cmdlinePath |> ProcessWatchdog.parseCmdlineArgv
              let cmdline = String.concat " " argv
              ProcessWatchdog.ProcProbeObservation.Present { record with Cmdline = cmdline; Argv = argv }
            with
            | :? FileNotFoundException
            | :? DirectoryNotFoundException -> ProcessWatchdog.ProcProbeObservation.Absent
            | :? IOException as ex when ex.HResult = Esrch -> ProcessWatchdog.ProcProbeObservation.Absent
            | (:? UnauthorizedAccessException | :? IOException) when not strictCmdline ->
              ProcessWatchdog.ProcProbeObservation.Present { record with Cmdline = ""; Argv = [] }
      with
      | :? FileNotFoundException
      | :? DirectoryNotFoundException -> ProcessWatchdog.ProcProbeObservation.Absent
      | :? UnauthorizedAccessException as ex -> ProcessWatchdog.ProcProbeObservation.Inaccessible ex.Message
      | :? IOException as ex when ex.HResult = Esrch -> ProcessWatchdog.ProcProbeObservation.Absent
      | :? IOException as ex -> ProcessWatchdog.ProcProbeObservation.Inaccessible ex.Message

  let readLinuxProc (pid: int) : ProcessWatchdog.ProcRecord option =
    let clkTck = clockTicksPerSecond () |> Option.defaultValue 0L
    match readLinuxProcWith "/proc" File.ReadAllText (readBootTimeMs ()) clkTck true false pid with
    | ProcessWatchdog.ProcProbeObservation.Present proc -> Some proc
    | ProcessWatchdog.ProcProbeObservation.Absent
    | ProcessWatchdog.ProcProbeObservation.Inaccessible _ -> None

  // Bounded /proc enumeration returning explicit ProcScanError on failure or truncation.
  let private readLinuxProcessesCore procRoot readText (includeCmdline: bool) (strict: bool) : Result<ProcessWatchdog.ProcRecord list, ProcessWatchdog.ProcScanError> =
    if not (OperatingSystem.IsLinux()) then Error ProcessWatchdog.ProcScanError.UnsupportedPlatform
    else
      let bootMsOpt = readBootTimeMs ()
      let clkTck = clockTicksPerSecond () |> Option.defaultValue 0L
      let results = ResizeArray<ProcessWatchdog.ProcRecord>()
      let mutable seenNumeric = 0
      let mutable truncated = false
      let mutable inaccessible = None
      try
        for dir in Directory.EnumerateDirectories procRoot do
          let name = Path.GetFileName dir
          match Int32.TryParse name with
          | true, pid when pid > 0 ->
            seenNumeric <- seenNumeric + 1
            if seenNumeric > ProcessWatchdog.maxProcScanEntries then
              truncated <- true
            elif not truncated then
              match readLinuxProcWith procRoot readText bootMsOpt clkTck includeCmdline strict pid with
              | ProcessWatchdog.ProcProbeObservation.Present proc -> results.Add proc
              | ProcessWatchdog.ProcProbeObservation.Absent -> ()
              | ProcessWatchdog.ProcProbeObservation.Inaccessible reason ->
                if strict && inaccessible.IsNone then inaccessible <- Some reason
          | _ -> ()
        if truncated then Error(ProcessWatchdog.ProcScanError.ScanTruncated ProcessWatchdog.maxProcScanEntries)
        else
          match inaccessible with
          | Some reason -> Error(ProcessWatchdog.ProcScanError.ProcDirectoryInaccessible reason)
          | None -> Ok(results |> Seq.toList)
      with ex ->
        Error(ProcessWatchdog.ProcScanError.ProcDirectoryInaccessible ex.Message)

  let internal readLinuxProcessesFrom procRoot readText (includeCmdline: bool) =
    readLinuxProcessesCore procRoot readText includeCmdline false

  let readLinuxProcesses includeCmdline =
    readLinuxProcessesFrom "/proc" File.ReadAllText includeCmdline

  // Fail-closed /proc scan requiring readable stat and cmdline across every live PID
  // (used exclusively by startup cache cleanup when no live worker may be missed).
  let internal readLinuxProcessesFromStrict procRoot readText =
    readLinuxProcessesCore procRoot readText true true

  let readLinuxProcessesStrict () =
    readLinuxProcessesFromStrict "/proc" File.ReadAllText

  let probeProcessLiveness (pid: int) (recordedStartMs: int64) : ProcessWatchdog.ProcessLiveness =
    if pid <= 0 then ProcessWatchdog.ProcessLiveness.Exited
    elif OperatingSystem.IsLinux() then
      match readBootTimeMs (), clockTicksPerSecond () with
      | Some bootMs, Some clkTck ->
        let observed = readLinuxProcWith "/proc" File.ReadAllText (Some bootMs) clkTck false false pid
        ProcessWatchdog.evaluateProcessLiveness recordedStartMs observed
      | None, _ ->
        ProcessWatchdog.ProcessLiveness.Unknown "Could not read /proc/stat btime to verify process start time."
      | _, None ->
        ProcessWatchdog.ProcessLiveness.Unknown "Could not read POSIX _SC_CLK_TCK to verify process start time."
    else
      try
        use proc = Process.GetProcessById pid
        if proc.HasExited then ProcessWatchdog.ProcessLiveness.Exited
        else
          let observedMs = DateTimeOffset(proc.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds()
          if abs (observedMs - recordedStartMs) <= ProcessWatchdog.startTimeToleranceMs then
            ProcessWatchdog.ProcessLiveness.Alive
          else
            ProcessWatchdog.ProcessLiveness.PidReused observedMs
      with
      | :? ArgumentException -> ProcessWatchdog.ProcessLiveness.Exited
      | :? InvalidOperationException -> ProcessWatchdog.ProcessLiveness.Exited
      | ex -> ProcessWatchdog.ProcessLiveness.Unknown ex.Message

  let sendSignalChecked (pidOrNegPgid: int) (sigNum: int) : string option =
    if not (OperatingSystem.IsLinux()) then
      Some(ProcessWatchdog.describeProcScanError ProcessWatchdog.ProcScanError.UnsupportedPlatform)
    else
      let rc = posixKill(pidOrNegPgid, sigNum)
      if rc = 0 then None
      else
        let errno = Marshal.GetLastPInvokeError()
        if errno = Esrch then None
        else Some(sprintf "kill(%d, %d) failed with errno=%d" pidOrNegPgid sigNum errno)

  let private currentSelfPgid () : int option =
    match lock ticksGate (fun () -> cachedSelfPgid) with
    | Some pgid -> Some pgid
    | None ->
      match readLinuxProc Environment.ProcessId |> Option.map _.Pgid with
      | Some pgid when pgid > 0 ->
        lock ticksGate (fun () -> cachedSelfPgid <- Some pgid)
        Some pgid
      | _ -> None

  // O(1) non-blocking -pgid signal with zero /proc walk (for timer/cancellation callbacks).
  let signalProcessGroupFast (targetPgid: int option) (sigNum: int) : Result<string option, ProcessWatchdog.ProcScanError> =
    if not (OperatingSystem.IsLinux()) then Error ProcessWatchdog.ProcScanError.UnsupportedPlatform
    else
      match ProcessWatchdog.safeTargetPgid targetPgid (currentSelfPgid ()) with
      | Some pgid -> Ok(sendSignalChecked (-pgid) sigNum)
      | None -> Ok None

  // Send a single non-blocking signal to an isolated -targetPgid and rootPid's
  // subtree, propagating /proc scan errors instead of swallowing them.
  let signalProcessGroupNow (rootPid: int) (targetPgid: int option) (sigNum: int) : Result<string list, ProcessWatchdog.ProcScanError> =
    if not (OperatingSystem.IsLinux()) then Error ProcessWatchdog.ProcScanError.UnsupportedPlatform
    else
      let selfPid = Environment.ProcessId
      let safePgid = ProcessWatchdog.safeTargetPgid targetPgid (currentSelfPgid ())
      let groupErr =
        match safePgid with
        | Some pgid -> sendSignalChecked (-pgid) sigNum |> Option.toList
        | None -> []
      match readLinuxProcesses false with
      | Error err -> Error err
      | Ok procs ->
        let pids = ProcessWatchdog.subtreePids rootPid safePgid procs
        let pidErrs =
          [ for pid in pids do
              if pid > 0 && pid <> selfPid then
                match sendSignalChecked pid sigNum with
                | Some err -> yield err
                | None -> () ]
        Ok(groupErr @ pidErrs |> List.distinct)

  // Read-only verification that no live descendants remain in rootPid / safePgid.
  let verifyNoProcessGroupSurvivors (rootPid: int) (targetPgid: int option) : ProcessWatchdog.TerminationOutcome =
    if not (OperatingSystem.IsLinux()) then ProcessWatchdog.TerminationOutcome.Unverified ProcessWatchdog.ProcScanError.UnsupportedPlatform
    else
      let selfPid = Environment.ProcessId
      let safePgid = ProcessWatchdog.safeTargetPgid targetPgid (currentSelfPgid ())
      match readLinuxProcesses false with
      | Error err -> ProcessWatchdog.TerminationOutcome.Unverified err
      | Ok procs ->
        let remaining =
          ProcessWatchdog.subtreePids rootPid safePgid procs
          |> Set.remove selfPid
          |> Set.toList
          |> List.sort
        if List.isEmpty remaining then ProcessWatchdog.TerminationOutcome.Cleared
        else ProcessWatchdog.TerminationOutcome.Survivors(remaining, [])

  // Escalating async process-group termination with StartTimeTicks fencing for root AND
  // every discovered descendant:
  // - Retains a fenced Map<int, int64> (pid -> StartTimeTicks) across SIGTERM -> grace -> SIGKILL
  //   so descendants that trap/ignore or delay SIGTERM and get reparented to init / systemd --user
  //   (including shared non-exclusive PGIDs where safePgid = None or children that called setsid
  //   into their own PGID) remain tracked, receive SIGKILL escalation, and are verified gone.
  // - Never signals a live PID whose StartTimeTicks no longer matches its fenced identity.
  // - Returns Cleared immediately when the initial scan finds zero live processes in the
  //   fenced subtree/PGID (never issuing a blind kill(-pgid, SIGTERM)).
  let terminateProcessGroupWithFenceAsync
      (rootPid: int)
      (targetPgid: int option)
      (expectedRootStartTicks: int64 option)
      (graceMs: int)
      : Async<ProcessWatchdog.TerminationOutcome> =
    async {
      if not (OperatingSystem.IsLinux()) then
        return ProcessWatchdog.TerminationOutcome.Unverified ProcessWatchdog.ProcScanError.UnsupportedPlatform
      else
        let selfPid = Environment.ProcessId
        let safePgid = ProcessWatchdog.safeTargetPgid targetPgid (currentSelfPgid ())
        let initialSeen =
          match expectedRootStartTicks with
          | Some ticks -> Map.add rootPid ticks Map.empty
          | None -> Map.empty
        let scanAndTrack (tracked: Map<int, int64>) (seen: Map<int, int64>) =
          readLinuxProcesses false
          |> Result.map (fun procs ->
            let byPid =
              procs
              |> List.choose (fun p -> if ProcessWatchdog.isLiveRecord p then Some(p.Pid, p) else None)
              |> Map.ofList
            let isRecycled (pid: int) =
              match Map.tryFind pid seen, Map.tryFind pid byPid with
              | Some expectedTicks, Some p -> p.StartTimeTicks <> expectedTicks
              | _ -> false
            let rootRecycled = isRecycled rootPid
            let stillTracked =
              tracked
              |> Map.filter (fun pid expectedTicks ->
                match Map.tryFind pid byPid with
                | Some p -> p.StartTimeTicks = expectedTicks
                | None -> false)
            let discoveryPgid = if rootRecycled then None else safePgid
            let rootDiscovered =
              if rootRecycled then Set.empty
              else ProcessWatchdog.subtreePids rootPid discoveryPgid procs
            let childDiscovered =
              stillTracked
              |> Map.toSeq
              |> Seq.collect (fun (pid, _) -> ProcessWatchdog.subtreePids pid None procs)
              |> Set.ofSeq
            let allDiscovered =
              Set.union rootDiscovered childDiscovered
              |> Set.remove selfPid
              |> Set.filter (fun pid -> not (isRecycled pid))
            let mergedTracked, mergedSeen =
              allDiscovered
              |> Set.fold (fun (tAcc, sAcc) pid ->
                match Map.tryFind pid byPid with
                | Some p ->
                  let tNext = if Map.containsKey pid tAcc then tAcc else Map.add pid p.StartTimeTicks tAcc
                  let sNext = if Map.containsKey pid sAcc then sAcc else Map.add pid p.StartTimeTicks sAcc
                  tNext, sNext
                | None -> tAcc, sAcc) (stillTracked, seen)
            let effectivePgid =
              match discoveryPgid with
              | Some pgid ->
                let hasMemberInPgid =
                  mergedTracked
                  |> Map.exists (fun pid _ ->
                    match Map.tryFind pid byPid with
                    | Some p -> p.Pgid = pgid
                    | None -> false)
                if hasMemberInPgid then Some pgid else None
              | None -> None
            let pids = mergedTracked |> Map.toList |> List.map fst |> List.sort
            pids, effectivePgid, mergedTracked, mergedSeen)
        let signalAll (effectivePgid: int option) sigNum pids =
          [ match effectivePgid with
            | Some pgid ->
              match sendSignalChecked (-pgid) sigNum with
              | Some err -> yield err
              | None -> ()
            | None -> ()
            for pid in pids do
              if pid > 0 && pid <> selfPid then
                match sendSignalChecked pid sigNum with
                | Some err -> yield err
                | None -> () ]
        let rec waitUntilEmpty (deadline: DateTimeOffset) (stepMs: int) (tracked: Map<int, int64>) (seen: Map<int, int64>) = async {
          match scanAndTrack tracked seen with
          | Error err -> return Error err
          | Ok([], _, nextTracked, nextSeen) -> return Ok([], None, nextTracked, nextSeen)
          | Ok(alive, pgid, nextTracked, nextSeen) when DateTimeOffset.UtcNow >= deadline -> return Ok(alive, pgid, nextTracked, nextSeen)
          | Ok(_, _, nextTracked, nextSeen) ->
            do! Async.Sleep stepMs
            return! waitUntilEmpty deadline stepMs nextTracked nextSeen
        }
        match scanAndTrack Map.empty initialSeen with
        | Error err -> return ProcessWatchdog.TerminationOutcome.Unverified err
        | Ok([], _, _, _) -> return ProcessWatchdog.TerminationOutcome.Cleared
        | Ok(initial, initialPgid, trackedAfterInit, seenAfterInit) ->
          let termErrors = signalAll initialPgid ProcessWatchdog.SigTerm initial
          let graceDeadline = DateTimeOffset.UtcNow.AddMilliseconds(float (max 0 graceMs))
          match! waitUntilEmpty graceDeadline 40 trackedAfterInit seenAfterInit with
          | Error err -> return ProcessWatchdog.TerminationOutcome.Unverified err
          | Ok([], _, _, _) ->
            if List.isEmpty termErrors then return ProcessWatchdog.TerminationOutcome.Cleared
            else return ProcessWatchdog.TerminationOutcome.Survivors([], termErrors)
          | Ok(afterTerm, afterTermPgid, trackedAfterTerm, seenAfterTerm) ->
            let killErrors = signalAll afterTermPgid ProcessWatchdog.SigKill afterTerm
            match! waitUntilEmpty (DateTimeOffset.UtcNow.AddMilliseconds 400.0) 30 trackedAfterTerm seenAfterTerm with
            | Error err -> return ProcessWatchdog.TerminationOutcome.Unverified err
            | Ok([], _, _, _) ->
              let allErrors = termErrors @ killErrors |> List.distinct
              if List.isEmpty allErrors then return ProcessWatchdog.TerminationOutcome.Cleared
              else return ProcessWatchdog.TerminationOutcome.Survivors([], allErrors)
            | Ok(remaining, _, _, _) ->
              let allErrors = termErrors @ killErrors |> List.distinct
              return ProcessWatchdog.TerminationOutcome.Survivors(remaining, allErrors)
    }

  let terminateProcessGroupAsync (rootPid: int) (targetPgid: int option) (graceMs: int) : Async<ProcessWatchdog.TerminationOutcome> =
    terminateProcessGroupWithFenceAsync rootPid targetPgid None graceMs

  // Identity-fenced termination for orphaned anomalies: confirms rootPid still carries
  // the exact expected StartTimeTicks before signaling anything, preventing PID-reuse kills.
  // Distinguishes Absent (Cleared) from Inaccessible (Unverified).
  let terminateFencedProcessGroupAsync
      (rootPid: int)
      (targetPgid: int option)
      (expectedStartTicks: int64)
      (graceMs: int)
      : Async<ProcessWatchdog.TerminationOutcome> =
    async {
      if not (OperatingSystem.IsLinux()) then
        return ProcessWatchdog.TerminationOutcome.Unverified ProcessWatchdog.ProcScanError.UnsupportedPlatform
      else
        let clkTck = clockTicksPerSecond () |> Option.defaultValue 0L
        match readLinuxProcWith "/proc" File.ReadAllText (readBootTimeMs ()) clkTck false false rootPid with
        | ProcessWatchdog.ProcProbeObservation.Inaccessible detail ->
          return ProcessWatchdog.TerminationOutcome.Unverified(ProcessWatchdog.ProcScanError.ProcDirectoryInaccessible detail)
        | ProcessWatchdog.ProcProbeObservation.Absent ->
          return ProcessWatchdog.TerminationOutcome.Cleared
        | ProcessWatchdog.ProcProbeObservation.Present current when not (ProcessWatchdog.isLiveRecord current) ->
          return ProcessWatchdog.TerminationOutcome.Cleared
        | ProcessWatchdog.ProcProbeObservation.Present current when current.StartTimeTicks <> expectedStartTicks ->
          return ProcessWatchdog.TerminationOutcome.Cleared
        | ProcessWatchdog.ProcProbeObservation.Present _ ->
          return! terminateProcessGroupWithFenceAsync rootPid targetPgid (Some expectedStartTicks) graceMs
    }
