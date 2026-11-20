module Bozzetto.Server.DaemonTelemetry

open System
open System.Collections.Generic
open System.Diagnostics

type ProcessTelemetry = {
    ProcessId: int
    Role: string
    ResidentBytes: int64
    CpuTime: TimeSpan
    CpuPercent: float
}

type Snapshot = {
    SampledAt: DateTimeOffset
    AggregateResidentBytes: int64
    AggregateCpuPercent: float
    Processes: ProcessTelemetry list
}

type private Previous = DateTimeOffset * TimeSpan
type private State = {
    mutable Previous: Dictionary<int, Previous>
    mutable Current: Snapshot option
}

let private state = { Previous = Dictionary<int, Previous>(); Current = None }
let private gate = obj ()

let private processTelemetry (role: string) (previous: Previous option) (now: DateTimeOffset) (child: Process) =
    try
        child.Refresh()
        let cpuTime = child.TotalProcessorTime
        let resident = child.WorkingSet64
        let cpuPercent =
            match previous with
            | Some (previousAt, previousCpu) when now > previousAt ->
                let elapsedSeconds = (now - previousAt).TotalSeconds
                if elapsedSeconds > 0.0 then
                    ((cpuTime - previousCpu).TotalSeconds / elapsedSeconds / float Environment.ProcessorCount) * 100.0
                else
                    0.0
            | _ -> 0.0
        let item = {
            ProcessId = child.Id
            Role = role
            ResidentBytes = resident
            CpuTime = cpuTime
            CpuPercent = max 0.0 cpuPercent
        }
        Some(child.Id, (now, cpuTime), item)
    with _ ->
        None

/// The processes the daemon owns, labelled as the health views show them:
/// the daemon itself, its Composer worker while one is alive, then the
/// inherited F# session workers (already labelled by their caller).
let ownedProcesses (daemonPid: int) (composerWorker: int option) (sessionWorkers: (int * string) list) : (int * string) list =
  (daemonPid, "daemon")
  :: (composerWorker |> Option.map (fun pid -> pid, "composer-worker") |> Option.toList)
  @ sessionWorkers

let sample (processes: (int * string) list) : Snapshot =
    let now = DateTimeOffset.UtcNow
    let sampled = ResizeArray<int * Previous * ProcessTelemetry>()
    let nextPrevious = Dictionary<int, Previous>()
    for pid, role in processes |> List.distinctBy fst do
        try
            use child = Process.GetProcessById pid
            let previous = match state.Previous.TryGetValue pid with | true, value -> Some value | _ -> None
            match processTelemetry role previous now child with
            | Some (id, latest, item) ->
                nextPrevious[id] <- latest
                sampled.Add(id, latest, item)
            | None -> ()
        with _ -> ()
    let items = sampled |> Seq.map (fun (_, _, item) -> item) |> Seq.toList
    let snapshot = {
        SampledAt = now
        AggregateResidentBytes = items |> List.sumBy _.ResidentBytes
        AggregateCpuPercent = items |> List.sumBy _.CpuPercent
        Processes = items
    }
    lock gate (fun () ->
        state.Previous <- nextPrevious
        state.Current <- Some snapshot)
    snapshot

let current () : Snapshot option = lock gate (fun () -> state.Current)

let currentOrSample (fallbackProcesses: (int * string) list) : Snapshot =
    match current () with
    | Some snapshot -> snapshot
    | None -> sample fallbackProcesses

type LeasedCommandLiveness = {
    RunId: string
    LeaseId: string
    ProcessId: int
    Outcome: string
    StartedAtMs: int64
    LastLivenessVerifiedAtMs: int64 option
    LeaseLive: bool
    LeaseExpiresAt: DateTimeOffset option
}

type UnleasedSubprocess = {
    Pid: int
    Ppid: int
    Pgid: int
    StartTimeTicks: int64
    Role: Bozzetto.ProcessWatchdog.WatchedProcessRole
    ResidentBytes: int64
    Command: string
    IsOrphaned: bool
    OrphanRootPid: int option
}

type UnleasedObservation = {
    SampledAt: DateTimeOffset
    Result: Result<UnleasedSubprocess list, Bozzetto.ProcessWatchdog.ProcScanError>
}

type OrphanCleanupTarget = {
    Pid: int
    Pgid: int
    Role: Bozzetto.ProcessWatchdog.WatchedProcessRole
    Outcome: Bozzetto.ProcessWatchdog.TerminationOutcome
}

type OrphanCleanupReport = {
    SampledAt: DateTimeOffset
    Targets: OrphanCleanupTarget list
    ScanError: Bozzetto.ProcessWatchdog.ProcScanError option
}

let private unleasedGate = obj ()
let private unleasedOpLock = new System.Threading.SemaphoreSlim(1, 1)
let mutable private latestUnleased : UnleasedObservation option = None
let mutable private orphanFirstSeenMs : Map<int * int64, int64> = Map.empty
let mutable private latestLeasedLiveness : LeasedCommandLiveness list = []

let publishLeasedCommandLiveness (items: LeasedCommandLiveness list) : unit =
    lock unleasedGate (fun () -> latestLeasedLiveness <- items)

let currentLeasedCommandLiveness () : LeasedCommandLiveness list =
    lock unleasedGate (fun () -> latestLeasedLiveness)

// Pure detector: flags live watched compiler/solver processes whose PID is not
// covered by daemon-owned PPID subtrees or live-leased root subtrees/PGIDs.
// Walks through watched ancestors and same-PGID launcher/shell wrappers (bash/sh/setsid)
// so detached `setsid sh -c "Composer ..."` trees are accurately classified as orphaned.
// When untrackedBuildOrTestLeaseCount > 0 (e.g., an active MCP FullBuild/TestSuiteRun/Rebuild
// lease whose holder did not post /api/lease/command/start), non-orphaned builds are
// spared while reparented orphaned subtrees (IsOrphaned = true) are STILL flagged.
let detectUnleasedSubprocesses
    (ownedPids: Set<int>)
    (leasedRootPids: Set<int>)
    (untrackedBuildOrTestLeaseCount: int)
    (procs: Bozzetto.ProcessWatchdog.ProcRecord list)
    : UnleasedSubprocess list =
    let coveredOwned =
        ownedPids
        |> Seq.collect (fun root ->
            Bozzetto.ProcessWatchdog.subtreePids root None procs)
    let coveredLeased =
        leasedRootPids
        |> Seq.collect (fun root ->
            let leaderPgid =
                procs
                |> List.tryFind (fun p -> p.Pid = root && p.Pgid = p.Pid)
                |> Option.map _.Pgid
            Bozzetto.ProcessWatchdog.subtreePids root leaderPgid procs)
    let covered = Seq.append coveredOwned coveredLeased |> Set.ofSeq
    let byPid = procs |> List.map (fun p -> p.Pid, p) |> Map.ofList
    let uncoveredWatched =
        procs
        |> List.filter Bozzetto.ProcessWatchdog.isLiveRecord
        |> List.filter (fun p -> not (Set.contains p.Pid covered))
        |> List.choose (fun p ->
            Bozzetto.ProcessWatchdog.classifyWatchedSubprocess p.Comm p.Argv
            |> Option.map (fun role -> p, role))
    let watchedPids = uncoveredWatched |> List.map (fun (p, _) -> p.Pid) |> Set.ofList
    uncoveredWatched
    |> List.choose (fun (p, role) ->
        let orphanRoot = Bozzetto.ProcessWatchdog.orphanRootPid byPid watchedPids p
        let isOrphaned = orphanRoot.IsSome
        if untrackedBuildOrTestLeaseCount > 0 && not isOrphaned then None
        else
            Some {
                Pid = p.Pid
                Ppid = p.Ppid
                Pgid = p.Pgid
                StartTimeTicks = p.StartTimeTicks
                Role = role
                ResidentBytes = p.ResidentBytes
                Command = if String.IsNullOrWhiteSpace p.Cmdline then p.Comm else p.Cmdline
                IsOrphaned = isOrphaned
                OrphanRootPid = orphanRoot
            })
    |> List.sortBy _.Pid

// Always reads /proc so scan failures and reparented orphaned subtrees are never masked.
let scanUnleasedSubprocesses
    (ownedPids: Set<int>)
    (leasedRootPids: Set<int>)
    (untrackedBuildOrTestLeaseCount: int)
    : Result<UnleasedSubprocess list, Bozzetto.ProcessWatchdog.ProcScanError> =
    LinuxProcessEdge.readLinuxProcesses true
    |> Result.map (detectUnleasedSubprocesses ownedPids leasedRootPids untrackedBuildOrTestLeaseCount)

// Project detected unleased subprocesses into a discrete OperationalFault verdict
// with zero fabricated EWMA/CUSUM statistical fields.
let unleasedAnomalyVerdict (now: DateTimeOffset) (unleased: UnleasedSubprocess list) : Bozzetto.Features.HealthAnomaly.Verdict option =
    match unleased with
    | [] -> None
    | items ->
        let summary =
            items
            |> List.truncate 5
            |> List.map (fun p ->
                let orphanTag = if p.IsOrphaned then ",orphaned" else ""
                sprintf "%s(pid=%d,rss=%dMB%s)"
                    (Bozzetto.ProcessWatchdog.WatchedProcessRole.label p.Role)
                    p.Pid
                    (p.ResidentBytes / 1_048_576L)
                    orphanTag)
            |> String.concat ", "
        let more = if items.Length > 5 then sprintf " +%d more" (items.Length - 5) else ""
        let detail = sprintf "%s%s" summary more
        Some (Bozzetto.Features.HealthAnomaly.Verdict.OperationalFault {
            Kind = Bozzetto.Features.HealthAnomaly.OperationalFaultKind.UnleasedSubprocess
            ObservedAt = now
            Count = items.Length
            Detail = detail
        })

let procScanFailureVerdict (now: DateTimeOffset) (err: Bozzetto.ProcessWatchdog.ProcScanError) : Bozzetto.Features.HealthAnomaly.Verdict option =
    match err with
    | Bozzetto.ProcessWatchdog.ProcScanError.UnsupportedPlatform -> None
    | other ->
        Some (Bozzetto.Features.HealthAnomaly.Verdict.OperationalFault {
            Kind = Bozzetto.Features.HealthAnomaly.OperationalFaultKind.ProcScanFailure
            ObservedAt = now
            Count = 1
            Detail = Bozzetto.ProcessWatchdog.describeProcScanError other
        })

let observationToVerdict (obs: UnleasedObservation) : Bozzetto.Features.HealthAnomaly.Verdict option =
    match obs.Result with
    | Ok items -> unleasedAnomalyVerdict obs.SampledAt items
    | Error scanErr -> procScanFailureVerdict obs.SampledAt scanErr

let private anomalySignature (verdict: Bozzetto.Features.HealthAnomaly.Verdict option) : string option =
    verdict
    |> Option.map (fun v ->
        match v with
        | Bozzetto.Features.HealthAnomaly.Verdict.OperationalFault f ->
            sprintf "%s:%d:%s" (Bozzetto.Features.HealthAnomaly.operationalFaultName f.Kind) f.Count f.Detail
        | other -> Bozzetto.Features.HealthAnomaly.describe other |> Option.defaultValue "")

let currentUnleasedObservation () : UnleasedObservation option =
    lock unleasedGate (fun () -> latestUnleased)

// O(1) read for getDaemonHealth() and /health — zero /proc I/O on Kestrel/MCP threads.
let currentUnleasedVerdict () : Bozzetto.Features.HealthAnomaly.Verdict option =
    currentUnleasedObservation () |> Option.bind observationToVerdict

let publishUnleasedObservation (obs: UnleasedObservation) : bool =
    let nowMs = obs.SampledAt.ToUnixTimeMilliseconds()
    lock unleasedGate (fun () ->
        let prevSig = latestUnleased |> Option.bind observationToVerdict |> anomalySignature
        let nextSig = observationToVerdict obs |> anomalySignature
        latestUnleased <- Some obs
        match obs.Result with
        | Ok items ->
            let liveOrphans =
                items
                |> List.choose (fun item ->
                    if item.IsOrphaned then Some(item.Pid, item.StartTimeTicks) else None)
                |> Set.ofList
            let retained =
                orphanFirstSeenMs
                |> Map.filter (fun key _ -> Set.contains key liveOrphans)
            orphanFirstSeenMs <-
                liveOrphans
                |> Set.fold (fun acc key ->
                    if Map.containsKey key acc then acc else Map.add key nowMs acc) retained
        | Error _ -> ()
        prevSig <> nextSig)

let private refreshUnleasedCore
    (getInputs: unit -> Set<int> * Set<int> * int)
    : UnleasedObservation * bool =
    let ownedPids, leasedRootPids, untrackedBuildOrTestLeaseCount = getInputs ()
    let now = DateTimeOffset.UtcNow
    let scan = scanUnleasedSubprocesses ownedPids leasedRootPids untrackedBuildOrTestLeaseCount
    let obs = { SampledAt = now; Result = scan }
    let changed = publishUnleasedObservation obs
    obs, changed

let refreshUnleasedAsync
    (getInputs: unit -> Set<int> * Set<int> * int)
    : Async<UnleasedObservation * bool> =
    async {
        do! unleasedOpLock.WaitAsync() |> Async.AwaitTask
        try
            return refreshUnleasedCore getInputs
        finally
            unleasedOpLock.Release() |> ignore
    }

// Serialized, identity-fenced daemon anomaly scan + cleanup:
// - Runs a single /proc scan when clean (no 3x scan amplification).
// - Admits -pgid ONLY when ProcessWatchdog.exclusiveOrphanPgid proves the PGID is led by
//   the orphan root AND contains zero unrelated live processes (never kills an external
//   agent shell, IDE, or active MCP build sharing a PGID).
// - Fences every target by exact StartTimeTicks before signaling.
// - Re-scans /proc after cleanup only when targets were actually terminated.
let cleanupOrphanedSubprocessesWithCancellationAsync
    (getInputs: unit -> Set<int> * Set<int> * int)
    (minOrphanAgeMs: int64)
    (graceMs: int)
    (ct: System.Threading.CancellationToken)
    : Async<OrphanCleanupReport * bool> =
    async {
        do! unleasedOpLock.WaitAsync(ct) |> Async.AwaitTask
        try
            let ownedPids, leasedRootPids, untrackedBuildOrTestLeaseCount = getInputs ()
            let now = DateTimeOffset.UtcNow
            let nowMs = now.ToUnixTimeMilliseconds()
            match LinuxProcessEdge.readLinuxProcesses true with
            | Error scanErr ->
                let changed = publishUnleasedObservation { SampledAt = now; Result = Error scanErr }
                return { SampledAt = now; Targets = []; ScanError = Some scanErr }, changed
            | Ok procs ->
                let detected = detectUnleasedSubprocesses ownedPids leasedRootPids untrackedBuildOrTestLeaseCount procs
                let changedBefore = publishUnleasedObservation { SampledAt = now; Result = Ok detected }
                let byPid = procs |> List.map (fun p -> p.Pid, p) |> Map.ofList
                let selfPid = Environment.ProcessId
                let protectedPidSet = Set.union ownedPids leasedRootPids |> Set.add selfPid
                let protectedPgids =
                    procs
                    |> List.choose (fun p ->
                        if Set.contains p.Pid protectedPidSet then Some p.Pgid else None)
                    |> Set.ofList
                let eligible =
                    let firstSeenSnapshot = lock unleasedGate (fun () -> orphanFirstSeenMs)
                    detected
                    |> List.filter (fun item ->
                        if not item.IsOrphaned then false
                        elif minOrphanAgeMs <= 0L then true
                        else
                            match Map.tryFind (item.Pid, item.StartTimeTicks) firstSeenSnapshot with
                            | Some seenAt -> nowMs - seenAt >= minOrphanAgeMs
                            | None -> false)
                if List.isEmpty eligible then
                    return { SampledAt = now; Targets = []; ScanError = None }, changedBefore
                else
                    let eligibleOrphanPids =
                        eligible
                        |> List.collect (fun item -> item.Pid :: Option.toList item.OrphanRootPid)
                        |> Set.ofList
                    // Deduplicate by resolved root PID / exclusive PGID so each orphaned tree is signaled once.
                    let seenRoots = HashSet<int>()
                    let seenGroups = HashSet<int>()
                    let targets = ResizeArray<OrphanCleanupTarget>()
                    for item in eligible do
                        if not ct.IsCancellationRequested then
                            let rootPid = defaultArg item.OrphanRootPid item.Pid
                            let rootProcOpt = Map.tryFind rootPid byPid
                            let targetPid, targetPgid, targetStartTicks =
                                match rootProcOpt with
                                | Some rp -> rp.Pid, rp.Pgid, rp.StartTimeTicks
                                | None -> item.Pid, item.Pgid, item.StartTimeTicks
                            let safePgid =
                                if Set.contains targetPgid protectedPgids then None
                                else Bozzetto.ProcessWatchdog.exclusiveOrphanPgid targetPid targetPgid eligibleOrphanPids procs
                            let shouldRun =
                                match safePgid with
                                | Some pgid -> seenGroups.Add pgid && seenRoots.Add targetPid
                                | None -> seenRoots.Add targetPid
                            if shouldRun then
                                let! outcome =
                                    LinuxProcessEdge.terminateFencedProcessGroupAsync
                                        targetPid safePgid targetStartTicks graceMs
                                targets.Add {
                                    Pid = targetPid
                                    Pgid = targetPgid
                                    Role = item.Role
                                    Outcome = outcome
                                }
                    let changedAfter =
                        if targets.Count > 0 then
                            let _, changed = refreshUnleasedCore getInputs
                            changedBefore || changed
                        else changedBefore
                    return { SampledAt = DateTimeOffset.UtcNow; Targets = targets |> Seq.toList; ScanError = None }, changedAfter
        finally
            unleasedOpLock.Release() |> ignore
    }

let cleanupOrphanedSubprocessesAsync
    (getInputs: unit -> Set<int> * Set<int> * int)
    (minOrphanAgeMs: int64)
    (graceMs: int)
    : Async<OrphanCleanupReport> =
    async {
        let! report, _ = cleanupOrphanedSubprocessesWithCancellationAsync getInputs minOrphanAgeMs graceMs System.Threading.CancellationToken.None
        return report
    }

let mutable private sessionDisconnectWakeup : (unit -> unit) option = None

let installSessionDisconnectWakeup (wake: unit -> unit) : unit =
    lock unleasedGate (fun () -> sessionDisconnectWakeup <- Some wake)

let clearSessionDisconnectHandlers () : unit =
    lock unleasedGate (fun () -> sessionDisconnectWakeup <- None)

// O(1) non-blocking signal for Kestrel/MCP disconnect & stop hooks — never scans /proc on request threads.
let notifySessionDisconnected () : unit =
    match lock unleasedGate (fun () -> sessionDisconnectWakeup) with
    | Some wake -> wake ()
    | None -> ()

let resetUnleasedState () : unit =
    lock unleasedGate (fun () ->
        latestUnleased <- None
        orphanFirstSeenMs <- Map.empty
        latestLeasedLiveness <- []
        sessionDisconnectWakeup <- None)

let reset () : unit =
    lock gate (fun () ->
        state.Previous.Clear()
        state.Current <- None)
    resetUnleasedState ()
