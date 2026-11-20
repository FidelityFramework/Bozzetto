namespace Bozzetto

open System
open System.Globalization

// Pure process-tree, liveness, watchdog-fuse, and watched-subprocess rules
// shared by Bozzetto daemon internals, ComposerWorkerClient, and tools/build/lease.fsx.
// Zero OS I/O and zero P/Invoke live here; Linux /proc and POSIX kill(2) edge
// adapters live in Bozzetto.Server.LinuxProcessEdge.
module ProcessWatchdog =

  [<Literal>]
  let SigTerm = 15

  [<Literal>]
  let SigKill = 9

  // Derived directly from Bozzetto.OwnerMonitor.startTimeToleranceTicks (2 seconds).
  let startTimeToleranceMs : int64 =
    OwnerMonitor.startTimeToleranceTicks / TimeSpan.TicksPerMillisecond

  // Bounded scan cap matching LinuxResources.fs.
  let maxProcScanEntries = 8192

  type FuseLimits = {
    WallLimitSeconds: float option
    RssLimitBytes: int64 option
  }

  let noLimits = { WallLimitSeconds = None; RssLimitBytes = None }

  [<RequireQualifiedAccess>]
  type FuseBreach =
    | WallLimitExceeded of elapsedSeconds: float * limitSeconds: float
    | RssLimitExceeded of peakBytes: int64 * limitBytes: int64

  let describeBreach =
    function
    | None -> "none"
    | Some(FuseBreach.WallLimitExceeded(elapsed, limit)) ->
      sprintf "wall_limit_exceeded(%.2fs>=%.2fs)" elapsed limit
    | Some(FuseBreach.RssLimitExceeded(peak, limit)) ->
      sprintf "rss_limit_exceeded(%.1fMB>=%.1fMB)" (float peak / 1_048_576.0) (float limit / 1_048_576.0)

  [<RequireQualifiedAccess>]
  type ProcessLiveness =
    | Alive
    | Exited
    | PidReused of observedStartMs: int64
    | Unknown of detail: string

  [<RequireQualifiedAccess>]
  type ProcScanError =
    | UnsupportedPlatform
    | ProcDirectoryInaccessible of detail: string
    | ScanTruncated of limit: int

  let describeProcScanError =
    function
    | ProcScanError.UnsupportedPlatform -> "unsupported platform for /proc inspection"
    | ProcScanError.ProcDirectoryInaccessible detail -> sprintf "/proc inaccessible: %s" detail
    | ProcScanError.ScanTruncated limit -> sprintf "/proc entry count exceeded scan cap %d" limit

  [<RequireQualifiedAccess>]
  type TerminationOutcome =
    | Cleared
    | Survivors of pids: int list * errors: string list
    | Unverified of ProcScanError

  [<RequireQualifiedAccess>]
  type WatchedProcessRole =
    | Composer
    | SolverCvc5
    | SolverZ3
    | DotnetBuildOrTest

  module WatchedProcessRole =
    let label =
      function
      | WatchedProcessRole.Composer -> "composer"
      | WatchedProcessRole.SolverCvc5 -> "solver:cvc5"
      | WatchedProcessRole.SolverZ3 -> "solver:z3"
      | WatchedProcessRole.DotnetBuildOrTest -> "dotnet-build"

  // Single pure /proc/<pid>/stat field extraction shared by ProcessWatchdog and LinuxResources.
  type ProcStatFields = {
    Pid: int
    Comm: string
    State: char
    Ppid: int
    Pgid: int
    UtimeTicks: int64
    StimeTicks: int64
    Threads: int
    StartTimeTicks: int64
    RssPages: int64
  }

  type ProcRecord = {
    Pid: int
    Ppid: int
    Pgid: int
    State: char
    StartTimeTicks: int64
    ResidentBytes: int64
    Comm: string
    Cmdline: string
    Argv: string list
    StartTimeMsOpt: int64 option
  }

  let isLiveRecord (record: ProcRecord) : bool =
    record.State <> 'Z' && record.State <> 'X'

  // Fail-closed self-PGID guard: never allow signaling -pgid when the caller's own
  // PGID is unknown (None) or matches targetPgid.
  let safeTargetPgid (targetPgid: int option) (selfPgid: int option) : int option =
    match targetPgid, selfPgid with
    | Some pgid, Some myPgid when pgid > 1 && pgid <> myPgid -> Some pgid
    | _ -> None

  // Parse Linux /proc/<pid>/stat handling spaces and ')' inside comm.
  // Fields after last ')':
  //   0: state, 1: ppid, 2: pgrp, 11: utime, 12: stime, 17: num_threads, 19: starttime, 21: rss
  let parseProcStatFields (text: string) : ProcStatFields option =
    if String.IsNullOrEmpty text then None
    else
      let openIdx = text.IndexOf('(')
      let closeIdx = text.LastIndexOf(')')
      if openIdx < 1 || closeIdx <= openIdx || closeIdx + 2 > text.Length then None
      else
        let pidPart = text.Substring(0, openIdx).Trim()
        let comm = text.Substring(openIdx + 1, closeIdx - openIdx - 1)
        let tail = text.Substring(closeIdx + 1).Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        if tail.Length < 22 || tail[0].Length = 0 then None
        else
          let parseInt (s: string) =
            match Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, v -> Some v
            | _ -> None
          let parseInt64 (s: string) =
            match Int64.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, v -> Some v
            | _ -> None
          match parseInt pidPart, parseInt tail[1], parseInt tail[2], parseInt64 tail[11], parseInt64 tail[12], parseInt tail[17], parseInt64 tail[19], parseInt64 tail[21] with
          | Some pid, Some ppid, Some pgid, Some utime, Some stime, Some threads, Some startTicks, Some rssPages
            when pid > 0 && utime >= 0L && stime >= 0L && startTicks >= 0L && rssPages >= 0L ->
            Some {
              Pid = pid
              Comm = comm
              State = tail[0][0]
              Ppid = ppid
              Pgid = pgid
              UtimeTicks = utime
              StimeTicks = stime
              Threads = threads
              StartTimeTicks = startTicks
              RssPages = rssPages
            }
          | _ -> None

  let parseProcStat (pageSize: int64) (bootTimeMsOpt: int64 option) (clockTicksPerSec: int64) (text: string) : ProcRecord option =
    parseProcStatFields text
    |> Option.map (fun f ->
      let startTimeMsOpt =
        match bootTimeMsOpt with
        | Some bootMs when clockTicksPerSec > 0L ->
          Some(bootMs + (f.StartTimeTicks * 1000L) / clockTicksPerSec)
        | _ -> None
      { Pid = f.Pid
        Ppid = f.Ppid
        Pgid = f.Pgid
        State = f.State
        StartTimeTicks = f.StartTimeTicks
        ResidentBytes = max 0L (f.RssPages * max 0L pageSize)
        Comm = f.Comm
        Cmdline = ""
        Argv = []
        StartTimeMsOpt = startTimeMsOpt })

  let parseBootTimeMs (procStatText: string) : int64 option =
    if String.IsNullOrEmpty procStatText then None
    else
      procStatText.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
      |> Array.tryPick (fun line ->
        if line.StartsWith("btime ", StringComparison.Ordinal) then
          let parts = line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
          if parts.Length >= 2 then
            match Int64.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, seconds when seconds > 0L -> Some(seconds * 1000L)
            | _ -> None
          else None
        else None)

  // Parse /proc/<pid>/cmdline NUL-delimited bytes into exact argv tokens.
  let parseCmdlineArgv (raw: string) : string list =
    if String.IsNullOrEmpty raw then []
    else
      raw.Split('\000')
      |> Array.filter (fun token -> token.Length > 0)
      |> Array.toList

  [<RequireQualifiedAccess>]
  type ProcProbeObservation =
    | Present of ProcRecord
    | Absent
    | Inaccessible of detail: string

  // Pure liveness evaluation accounting for PID reuse and inaccessible/unknown reads.
  let evaluateProcessLiveness (recordedStartMs: int64) (observed: ProcProbeObservation) : ProcessLiveness =
    match observed with
    | ProcProbeObservation.Inaccessible detail -> ProcessLiveness.Unknown detail
    | ProcProbeObservation.Absent -> ProcessLiveness.Exited
    | ProcProbeObservation.Present proc ->
      if not (isLiveRecord proc) then ProcessLiveness.Exited
      else
        match proc.StartTimeMsOpt with
        | None ->
          ProcessLiveness.Unknown(sprintf "Process %d is present, but start time could not be verified." proc.Pid)
        | Some observedStartMs ->
          if abs (observedStartMs - recordedStartMs) <= startTimeToleranceMs then
            ProcessLiveness.Alive
          else
            ProcessLiveness.PidReused observedStartMs

  // Collect every live PID belonging to rootPid's descendant tree or targetPgid group.
  let subtreePids (rootPid: int) (targetPgid: int option) (procs: ProcRecord list) : Set<int> =
    let live = procs |> List.filter isLiveRecord
    let childrenByParent =
      live
      |> List.groupBy _.Ppid
      |> Map.ofList
    let groupSeeds =
      match targetPgid with
      | Some pgid when pgid > 1 ->
        live |> List.choose (fun p -> if p.Pgid = pgid then Some p.Pid else None)
      | _ -> []
    let initialSeeds =
      let rootAlive = live |> List.exists (fun p -> p.Pid = rootPid)
      (if rootAlive then [ rootPid ] else []) @ groupSeeds
      |> Set.ofList
    let rec expand (frontier: Set<int>) (visited: Set<int>) =
      if Set.isEmpty frontier then visited
      else
        let nextFrontier =
          frontier
          |> Seq.collect (fun pid ->
            match Map.tryFind pid childrenByParent with
            | Some children -> children |> Seq.map _.Pid
            | None -> Seq.empty)
          |> Seq.filter (fun pid -> not (Set.contains pid visited))
          |> Set.ofSeq
        expand nextFrontier (Set.union visited nextFrontier)
    expand initialSeeds initialSeeds

  // Sum resident bytes across the entire rootPid / targetPgid subtree.
  let subtreeRssBytes (rootPid: int) (targetPgid: int option) (procs: ProcRecord list) : int64 =
    let pids = subtreePids rootPid targetPgid procs
    procs
    |> List.sumBy (fun p -> if Set.contains p.Pid pids && isLiveRecord p then max 0L p.ResidentBytes else 0L)

  // Evaluate watchdog wall-clock and peak-RSS fuses (RSS checked first on tie).
  let evaluateFuse (limits: FuseLimits) (elapsedSeconds: float) (peakRssBytes: int64) : FuseBreach option =
    match limits.RssLimitBytes with
    | Some limitBytes when limitBytes > 0L && peakRssBytes >= limitBytes ->
      Some(FuseBreach.RssLimitExceeded(peakRssBytes, limitBytes))
    | _ ->
      match limits.WallLimitSeconds with
      | Some limitSecs when limitSecs > 0.0 && elapsedSeconds >= limitSecs ->
        Some(FuseBreach.WallLimitExceeded(elapsedSeconds, limitSecs))
      | _ -> None

  let private fileBaseName (token: string) : string =
    if String.IsNullOrWhiteSpace token then ""
    else
      let trimmed = token.Trim().TrimEnd('/', '\\')
      let slash = trimmed.LastIndexOfAny([| '/'; '\\' |])
      if slash >= 0 && slash + 1 < trimmed.Length then trimmed.Substring(slash + 1)
      else trimmed

  let private dotnetHostValueFlags =
    Set.ofList [
      "--roll-forward"
      "--fx-version"
      "--runtimeconfig"
      "--depsfile"
      "--additionalprobingpath"
      "--additional-deps"
    ]

  // Extract the effective subcommand or assembly target after `dotnet` host options
  // (including `dotnet exec <target>` and two-token flags like `--roll-forward Major`).
  let private findDotnetTarget (argsAfterDotnet: string list) : string option =
    let rec walk (remaining: string list) =
      match remaining with
      | [] -> None
      | tok :: rest ->
        let lower = tok.ToLowerInvariant()
        if lower = "exec" then walk rest
        elif Set.contains lower dotnetHostValueFlags then
          match rest with
          | _ :: afterVal -> walk afterVal
          | [] -> None
        elif tok.StartsWith("-", StringComparison.Ordinal) then
          walk rest
        else
          Some tok
    walk argsAfterDotnet

  // Classify a process as a watched compiler/solver/build subprocess using strict
  // executable and argv-token matching (never substring matching on arbitrary paths
  // or arguments such as shell commands, editors, or `dotnet fsi lease.fsx`).
  let classifyWatchedSubprocess (comm: string) (argv: string list) : WatchedProcessRole option =
    let commLower = (if isNull comm then "" else comm.Trim()).ToLowerInvariant()
    let exeName =
      match argv with
      | head :: _ -> (fileBaseName head).ToLowerInvariant()
      | [] -> commLower
    let isExe name = exeName = name || commLower = name
    if isExe "cvc5" then Some WatchedProcessRole.SolverCvc5
    elif isExe "z3" then Some WatchedProcessRole.SolverZ3
    elif isExe "composer" then Some WatchedProcessRole.Composer
    elif isExe "dotnet" then
      let args = match argv with _ :: rest -> rest | [] -> []
      match findDotnetTarget args with
      | None -> None
      | Some target ->
        let targetBase = (fileBaseName target).ToLowerInvariant()
        if targetBase = "composer.dll" then Some WatchedProcessRole.Composer
        elif targetBase = "fsi" || targetBase = "fsi.dll" then None
        elif targetBase = "build" || targetBase = "test" || targetBase = "msbuild" || targetBase = "msbuild.dll" then
          Some WatchedProcessRole.DotnetBuildOrTest
        else None
    else None

  // True when a process was directly reparented to init (PID 1), a systemd --user
  // subreaper (comm = systemd/init with PPID = 1), or a vanished parent PID.
  let isDirectOrphan (byPid: Map<int, ProcRecord>) (record: ProcRecord) : bool =
    if record.Ppid <= 1 then true
    else
      match Map.tryFind record.Ppid byPid with
      | None -> true
      | Some parent ->
        parent.Ppid <= 1
        && (parent.Comm.Equals("systemd", StringComparison.OrdinalIgnoreCase)
            || parent.Comm.Equals("init", StringComparison.OrdinalIgnoreCase))

  let private launcherWrapperComms =
    Set.ofList [ "sh"; "bash"; "dash"; "zsh"; "setsid"; "nohup"; "env"; "timeout"; "stdbuf"; "nice" ]

  let isLauncherWrapper (comm: string) : bool =
    if String.IsNullOrWhiteSpace comm then false
    else Set.contains (comm.Trim().ToLowerInvariant()) launcherWrapperComms

  // Walk upward through watched ancestors and same-PGID shell/launcher wrappers
  // (covering detached `setsid bash -c "Composer compile ..."` trees). Returns the
  // orphan root PID when the top of that chain has been reparented to init / systemd --user.
  let orphanRootPid (byPid: Map<int, ProcRecord>) (watchedPids: Set<int>) (record: ProcRecord) : int option =
    let rec walk hops (current: ProcRecord) =
      if hops >= 64 then None
      elif current.Ppid > 1 then
        match Map.tryFind current.Ppid byPid with
        | None -> Some current.Pid
        | Some parent when Set.contains parent.Pid watchedPids ->
          walk (hops + 1) parent
        | Some parent when parent.Pgid = current.Pgid && isLauncherWrapper parent.Comm ->
          walk (hops + 1) parent
        | Some _ ->
          if isDirectOrphan byPid current then Some current.Pid else None
      else
        Some current.Pid
    walk 0 record

  let isOrphanedWatchedProcess (byPid: Map<int, ProcRecord>) (watchedPids: Set<int>) (record: ProcRecord) : bool =
    orphanRootPid byPid watchedPids record |> Option.isSome

  // Fail-closed PGID admission for orphan anomaly cleanup: admits Some targetPgid ONLY
  // when targetRootPid leads targetPgid AND every live process in targetPgid belongs
  // exclusively to eligibleOrphanPids or targetRootPid's descendant tree (zero unrelated
  // live processes — such as an agent shell, IDE, or active MCP build — inhabit targetPgid).
  let exclusiveOrphanPgid
      (targetRootPid: int)
      (targetPgid: int)
      (eligibleOrphanPids: Set<int>)
      (procs: ProcRecord list)
      : int option =
    if targetPgid <= 1 || targetPgid <> targetRootPid then None
    else
      let allowed = Set.union eligibleOrphanPids (subtreePids targetRootPid None procs)
      let hasForeignLiveMember =
        procs
        |> List.exists (fun p -> isLiveRecord p && p.Pgid = targetPgid && not (Set.contains p.Pid allowed))
      if hasForeignLiveMember then None else Some targetPgid
