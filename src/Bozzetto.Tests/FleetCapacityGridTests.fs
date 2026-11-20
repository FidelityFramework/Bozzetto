module Bozzetto.Tests.FleetCapacityGridTests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Providers
open Bozzetto.ComposerIntegration
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Server.UiBridge

let private t0 = DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
let private dto0 = DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)
let private memberA = MemberTable.MemberId.Mcp "agent-alpha"
let private memberB = MemberTable.MemberId.Mcp "agent-beta"

let private makeReport runId seq status : AgentWork.Report =
  { Run = runId
    Session = "sess-" + runId
    Sequence = seq
    Name = "worker-" + runId
    Model = "claude-opus"
    Endpoint = "https://api.example"
    Project = "/repos/Bozzetto"
    Focus = "fleet-grid"
    Operation = "edit"
    Status = status
    Usage = Some { Input = 10L; Output = 20L; CacheRead = 5L; CacheWrite = 0L; Total = 35L; EstimateUsd = Some 0.01 } }

let private makeRun runId status updatedAt : AgentWork.Run =
  { Member = memberA
    Role = Cohort.JoinableRole.Implementer
    Report = makeReport runId 1L status
    UpdatedAt = updatedAt
    Activity = [ "edit" ]
    OmittedActivity = 0L }

let private emptyFrame version : Cohort.CohortFrame<MemberTable.MemberId> =
  { Version = version * 1L<Measures.ledgerSeq>
    SessionGens = [||]
    Dirty = Cohort.FrameRegions.NoRegions
    Conductor = None
    MemberIds = [||]
    MemberRole = [||]
    MemberSeat = [||]
    MemberRenewal = [||]
    MemberAgentName = [||]
    ClaimIds = [||]; ClaimScope = [||]; ClaimHolderIndex = [||]; ClaimHolderName = [||]; ClaimFence = [||]; ClaimState = [||]
    TestIds = [||]; Pass = [||]; Fail = [||]; Stale = [||]
    IntegrationHead = ""
    LandingIds = [||]; LandingRequesterIndex = [||]; LandingStatement = [||]; LandingCommits = [||]; LandingState = [||]
    LandingQueuePosition = [||] }

let private tick serial level memoryMb aggregateRss availBytes totalBytes : Server.HealthTick.Tick =
  let record : Features.HealthSnapshot option =
    Some {
      DaemonPid = 100
      DaemonPort = 47749
      Uptime = TimeSpan.FromMinutes 1.0
      Version = "0.1.0"
      SessionSummaries = []
      LiveTestingSummary = None
      MemoryMB = memoryMb
      Anomalies = []
      GcDumpOutcome = None
      MemoryPressure = level
    }
  let telemetry : Server.DaemonTelemetry.Snapshot option =
    aggregateRss
    |> Option.map (fun rss ->
      { Server.DaemonTelemetry.Snapshot.SampledAt = dto0.AddSeconds(float serial)
        AggregateResidentBytes = rss
        AggregateCpuPercent = 2.5
        Processes = [] })
  { Serial = serial
    SampledAtMs = dto0.AddSeconds(float serial).ToUnixTimeMilliseconds()
    Machine = { TotalBytes = totalBytes; AvailableBytes = availBytes }
    Level = level
    Record = record
    Telemetry = telemetry
    RecordAtMs = dto0.AddSeconds(float serial).ToUnixTimeMilliseconds() }

[<Tests>]
let tests =
  testList "Fleet capacity grid and dashboard dwell aging" [

    testCase "positive control: dwell durations and boundaries for active, green, and troubled runs" <| fun () ->
      AgentWork.greenDwellWindow |> Expect.equal "green dwell window is 5 minutes" (TimeSpan.FromMinutes 5.0)
      AgentWork.troubledDwellWindow |> Expect.equal "troubled dwell window is 30 minutes" (TimeSpan.FromMinutes 30.0)
      AgentWork.dwellDuration "completed" |> Expect.equal "completed uses 5m" (Some AgentWork.greenDwellWindow)
      for status in [ "failed"; "aborted"; "disconnected" ] do
        AgentWork.dwellDuration status |> Expect.equal (status + " uses 30m") (Some AgentWork.troubledDwellWindow)
      for status in [ "running"; "unknown"; "other" ] do
        AgentWork.dwellDuration status |> Expect.isNone (status + " never ages out")

      let running = makeRun "r-run" "running" t0
      let unknown = makeRun "r-unk" "unknown" t0
      AgentWork.dwellExpiry running |> Expect.isNone "running has no expiry"
      AgentWork.dwellExpiry unknown |> Expect.isNone "unknown has no expiry"
      AgentWork.classifyDwell (t0.AddHours 2.0) running |> Expect.equal "running stays Primary after 2 hours" AgentWork.Primary
      AgentWork.classifyDwell (t0.AddHours 2.0) unknown |> Expect.equal "unknown stays Primary after 2 hours" AgentWork.Primary

      let completed = makeRun "r-ok" "completed" t0
      AgentWork.dwellExpiry completed |> Expect.equal "completed expires at +5m" (Some (t0.AddMinutes 5.0))
      AgentWork.classifyDwell (t0.AddMinutes(5.0).AddSeconds(-1.0)) completed
      |> Expect.equal "completed stays Primary at 4m59s" AgentWork.Primary
      AgentWork.classifyDwell (t0.AddMinutes 5.0) completed
      |> Expect.equal "completed moves to RecentCompleted at 5m00s" AgentWork.RecentCompleted

      for status in [ "failed"; "aborted"; "disconnected" ] do
        let troubled = makeRun ("r-" + status) status t0
        AgentWork.dwellExpiry troubled |> Expect.equal (status + " expires at +30m") (Some (t0.AddMinutes 30.0))
        AgentWork.classifyDwell (t0.AddMinutes(29.0).AddSeconds(59.0)) troubled
        |> Expect.equal (status + " stays Primary at 29m59s") AgentWork.Primary
        AgentWork.classifyDwell (t0.AddMinutes 30.0) troubled
        |> Expect.equal (status + " moves to RecentCompleted at 30m00s") AgentWork.RecentCompleted

    testCase "positive control: full daemon history is preserved when Project.agentBoardAt marks Aged = true" <| fun () ->
      let s0 = { AgentWork.empty with Incarnation = "inc-b1" }
      let s1 = AgentWork.report t0 memberA Cohort.JoinableRole.Implementer (makeReport "run-old-ok" 1L "completed") s0 |> Result.defaultWith (fun e -> failtestf "%A" e)
      let s2 = AgentWork.report (t0.AddMinutes 2.0) memberB Cohort.JoinableRole.Observer (makeReport "run-active" 1L "running") s1 |> Result.defaultWith (fun e -> failtestf "%A" e)
      let s3 = AgentWork.report (t0.AddMinutes 3.0) memberB Cohort.JoinableRole.Verifier (makeReport "run-recent-fail" 1L "failed") s2 |> Result.defaultWith (fun e -> failtestf "%A" e)

      let now = t0.AddMinutes 10.0
      let board = Project.agentBoardAt now 42L s3 [||]
      board.Refusal |> Expect.equal "board fits without refusal" ""
      board.OmittedRuns |> Expect.equal "no runs omitted by dwell aging" 0L
      s3.Runs.Count |> Expect.equal "AgentWork.State.Runs retains every run intact" 3
      s3.Runs["run-old-ok"].Activity |> Expect.equal "aged run retains full activity in daemon memory" [ "edit" ]

      let agedById = board.Runs |> Array.map (fun r -> r.Id, r.Aged) |> Map.ofArray
      agedById["run-old-ok"] |> Expect.isTrue "completed run aged after 10m"
      agedById["run-active"] |> Expect.isFalse "running run never ages"
      agedById["run-recent-fail"] |> Expect.isFalse "failed run stays Primary at 7m elapsed"

      let boardLater = Project.agentBoardAt (t0.AddMinutes 33.0) 43L s3 [||]
      let agedLater = boardLater.Runs |> Array.map (fun r -> r.Id, r.Aged) |> Map.ofArray
      agedLater["run-recent-fail"] |> Expect.isTrue "failed run ages at 30m elapsed"
      agedLater["run-active"] |> Expect.isFalse "running run still unaged after 31m"

    testCase "exact-reason negative control: nextDwellExpiry returns None when only running, unknown, or already-aged runs exist" <| fun () ->
      AgentWork.nextDwellExpiry t0 AgentWork.empty |> Expect.isNone "empty state arms no timer"
      let onlyActive =
        { AgentWork.empty with
            Incarnation = "inc"
            Sequence = 2L
            Runs = Map.ofList [ "a", makeRun "a" "running" t0; "b", makeRun "b" "unknown" t0 ] }
      AgentWork.nextDwellExpiry t0 onlyActive |> Expect.isNone "running/unknown runs never arm timer"
      let alreadyAged =
        { AgentWork.empty with
            Incarnation = "inc"
            Sequence = 2L
            Runs = Map.ofList [
              "ok", makeRun "ok" "completed" (t0.AddMinutes -10.0)
              "fail", makeRun "fail" "failed" (t0.AddMinutes -40.0)
              "run", makeRun "run" "running" t0
            ] }
      AgentWork.nextDwellExpiry t0 alreadyAged |> Expect.isNone "already-aged runs arm no idle timer"

      let mixed =
        { alreadyAged with
            Runs =
              alreadyAged.Runs
              |> Map.add "soon-fail" (makeRun "soon-fail" "failed" (t0.AddMinutes -28.0))
              |> Map.add "sooner-ok" (makeRun "sooner-ok" "completed" (t0.AddMinutes -4.0)) }
      AgentWork.nextDwellExpiry t0 mixed
      |> Expect.equal "picks earliest expiry strictly greater than now" (Some (t0.AddMinutes 1.0))

    testCase "fault injection: disconnected run inherits 30m troubled dwell window, not 5m" <| fun () ->
      let s0 = { AgentWork.empty with Incarnation = "inc-disc" }
      let s1 = AgentWork.report t0 memberA Cohort.JoinableRole.Implementer (makeReport "run-disc" 1L "running") s0 |> Result.defaultWith (fun e -> failtestf "%A" e)
      let disconnectAt = t0.AddMinutes 1.0
      let s2 = AgentWork.disconnect disconnectAt memberA s1
      let discRun = s2.Runs["run-disc"]
      discRun.Report.Status |> Expect.equal "disconnect transitions running run to disconnected" "disconnected"
      AgentWork.classifyDwell (disconnectAt.AddMinutes 10.0) discRun
      |> Expect.equal "disconnected run stays Primary at 10m (not 5m green window)" AgentWork.Primary
      AgentWork.classifyDwell (disconnectAt.AddMinutes 29.0) discRun
      |> Expect.equal "disconnected run stays Primary at 29m" AgentWork.Primary
      AgentWork.classifyDwell (disconnectAt.AddMinutes 30.0) discRun
      |> Expect.equal "disconnected run ages to RecentCompleted at 30m" AgentWork.RecentCompleted
      AgentWork.nextDwellExpiry (disconnectAt.AddMinutes 10.0) s2
      |> Expect.equal "nextDwellExpiry reflects 30m window from disconnect time" (Some (disconnectAt.AddMinutes 30.0))

    testTask "positive control: live UiBridge.Facts.Graph one-shot dwell timer transitions Aged = false -> true without polling and Stop disposes cleanly" {
      do! task {
        let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 7 }
        let graph = Facts.Graph(constants, "inc-live")
        let initialReady = TaskCompletionSource<AgentWorkBoard>(TaskCreationOptions.RunContinuationsAsynchronously)
        let agedReady = TaskCompletionSource<AgentWorkBoard>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, reg = graph.Register(fun kind ev ->
          match kind, ev with
          | Push.AgentWork, AgentWork board ->
            let isAged id = board.Runs |> Array.tryFind (fun r -> r.Id = id) |> Option.map _.Aged
            if isAged "live-done" = Some false then initialReady.TrySetResult board |> ignore
            elif isAged "live-done" = Some true then agedReady.TrySetResult board |> ignore
          | _ -> ())
        use _reg = reg
        graph.Pool({ Serial = 1L; Active = []; Queue = [] }) |> Expect.isOk "pool admitted"
        graph.Membership(emptyFrame 1L) |> Expect.isOk "membership admitted"

        // Expire ~80ms from now so the real 5m dwell window fires live under one-shot timer.
        let now = DateTime.UtcNow
        let doneUpdated = now.AddMinutes(-5.0).AddMilliseconds(80.0)
        let state =
          { AgentWork.empty with
              Incarnation = "inc-live"
              Sequence = 1L
              Runs = Map.ofList [
                "live-done", makeRun "live-done" "completed" doneUpdated
                "live-active", makeRun "live-active" "running" (now.AddHours -1.0)
              ] }
        graph.Agents state |> Expect.isOk "agents admitted"
        let! initial = initialReady.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        let initMap = initial.Runs |> Array.map (fun r -> r.Id, r.Aged) |> Map.ofArray
        initMap["live-done"] |> Expect.isFalse "completed run starts unaged before dwell deadline"
        initMap["live-active"] |> Expect.isFalse "running run starts unaged"

        let! aged = agedReady.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        let agedMap = aged.Runs |> Array.map (fun r -> r.Id, r.Aged) |> Map.ofArray
        agedMap["live-done"] |> Expect.isTrue "completed run aged reactively via one-shot dwellInput"
        agedMap["live-active"] |> Expect.isFalse "running run remained unaged"
        (aged.Sequence > initial.Sequence) |> Expect.isTrue "dwell transition bumped wire publication revision"
        graph.Stop()
        graph.Drained |> Expect.isTrue "graph drained cleanly"

        // Negative/cleanup control: Graph.Stop() cancels and disposes an armed dwell timer before expiry.
        let graph2 = Facts.Graph(constants, "inc-stop")
        let spuriousPush = TaskCompletionSource<AgentWorkBoard>(TaskCreationOptions.RunContinuationsAsynchronously)
        let firstPush = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, reg2 = graph2.Register(fun kind ev ->
          match kind, ev with
          | Push.AgentWork, AgentWork board ->
            if not firstPush.Task.IsCompleted then firstPush.TrySetResult() |> ignore
            else spuriousPush.TrySetResult board |> ignore
          | _ -> ())
        use _reg2 = reg2
        graph2.Pool({ Serial = 1L; Active = []; Queue = [] }) |> ignore
        graph2.Membership(emptyFrame 1L) |> ignore
        let now2 = DateTime.UtcNow
        graph2.Agents { AgentWork.empty with Incarnation = "inc-stop"; Sequence = 1L; Runs = Map.ofList [ "x", makeRun "x" "completed" (now2.AddMinutes(-5.0).AddMilliseconds(100.0)) ] }
        |> Expect.isOk "second graph admitted"
        do! firstPush.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        graph2.Stop()
        graph2.Drained |> Expect.isTrue "stopped graph drained"
        do! Task.Delay 200
        spuriousPush.Task.IsCompleted |> Expect.isFalse "stopped timer never fires a post-stop dwell push"
      }
    }

    testTask "fault injection: a backward clock step keeps the dwell timer armed" {
      let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 7 }
      let expires = t0.AddMilliseconds 50.
      let firstWake = TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously)
      let mutable reads = 0
      let utcNow () =
        match Interlocked.Increment(&reads) with
        | 1 | 2 -> t0 // Graph initialization and Agents publication.
        | 3 ->
          let steppedBack = t0.AddMilliseconds -200.
          firstWake.TrySetResult steppedBack |> ignore
          steppedBack
        | _ -> expires.AddMilliseconds 1.
      let graph = Facts.Graph(constants, "inc-clock", "", utcNow = utcNow)
      let mutable subscription : IDisposable option = None
      try
        let aged = TaskCompletionSource<AgentRun>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, sub = graph.Register(fun _ event ->
          match event with
          | AgentWork board ->
            board.Runs |> Array.tryFind (fun run -> run.Id = "clock-done" && run.Aged)
            |> Option.iter (fun run -> aged.TrySetResult run |> ignore)
          | _ -> ())
        subscription <- Some sub
        graph.Pool({ Serial = 1L; Active = []; Queue = [] }) |> Expect.isOk "pool admitted"
        graph.Membership(emptyFrame 1L) |> Expect.isOk "membership admitted"
        let run = makeRun "clock-done" "completed" (expires - AgentWork.greenDwellWindow)
        let state = { AgentWork.empty with Incarnation = "inc-clock"; Sequence = 1L; Runs = Map.ofList ["clock-done", run] }
        graph.Agents state |> Expect.isOk "the only Agents publication arms the dwell timer"
        let! steppedBack = firstWake.Task.WaitAsync(TimeSpan.FromSeconds 5.)
        steppedBack |> Expect.equal "first timer wake observes a backward wall-clock step" (t0.AddMilliseconds -200.)
        let! settled = aged.Task.WaitAsync(TimeSpan.FromSeconds 10.)
        settled.Aged |> Expect.isTrue "the rearmed timer pushes aged after the clock advances"
      finally
        subscription |> Option.iter _.Dispose()
        graph.Stop()
    }

    testCase "positive and negative controls: ExpensiveWorkLease.reconcileCapacity classifies PID-verified, stale, unverified, and queue wait" <| fun () ->
      let g1 : ExpensiveWorkLease.GrantView = { Holder = "a · /r"; Kind = "full_build"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 10.0 }
      let g2 : ExpensiveWorkLease.GrantView = { Holder = "b · /r"; Kind = "test_suite_run"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 15.0 }
      let g3 : ExpensiveWorkLease.GrantView = { Holder = "c · /r"; Kind = "rebuild"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 10.0 }
      let g4 : ExpensiveWorkLease.GrantView = { Holder = "d · /r"; Kind = "run_app"; GrantedAt = dto0; ExpiresAt = dto0.AddHours 1.0 }

      let statuses : ExpensiveWorkLease.LeasedPidStatus list = [
        // Verified alive by (Holder, Kind)
        { LeaseId = ""; Holder = g1.Holder; Kind = g1.Kind; ProcessId = 1001; LivenessVerified = true; Alive = true }
        // Dead/stale PID matched by computed displayId (even when Holder/Kind left blank)
        { LeaseId = ExpensiveWorkLease.displayId g2.Holder g2.Kind g2.GrantedAt; Holder = ""; Kind = ""; ProcessId = 1002; LivenessVerified = true; Alive = false }
        // Reported command started, liveness NOT yet verified -> UnverifiedLeases
        { LeaseId = ""; Holder = g3.Holder; Kind = g3.Kind; ProcessId = 1003; LivenessVerified = false; Alive = true }
        // Orphaned status for unrelated holder -> must NEVER inflate counts
        { LeaseId = ""; Holder = "ghost"; Kind = "full_build"; ProcessId = 9999; LivenessVerified = true; Alive = true }
      ]
      let queue : ExpensiveWorkLease.QueueView list = [
        { Holder = "w1"; Kind = "full_build"; RequestedAt = dto0.AddSeconds 30.0 }
        { Holder = "w2"; Kind = "rebuild"; RequestedAt = dto0.AddSeconds 10.0 }
      ]
      let now = dto0.AddSeconds 45.0
      let grants = [ g1; g2; g3; g4 ] |> List.map ExpensiveWorkLease.reconciledGrant
      let cap = ExpensiveWorkLease.reconcileCapacity now (Some MemoryPressure.Normal) grants queue statuses
      cap.LeaseCap |> Expect.equal "Normal cap is observed as 4" (Some 4)
      cap.ActiveLeaseCount |> Expect.equal "4 active grants" 4
      cap.PidVerifiedLeases |> Expect.equal "only g1 is live PID-verified" 1
      cap.StalePidLeases |> Expect.equal "g2 has stale/dead PID" 1
      cap.UnverifiedLeases |> Expect.equal "g3 (unverified) + g4 (unreported) = 2 unverified" 2
      (cap.PidVerifiedLeases + cap.StalePidLeases + cap.UnverifiedLeases)
      |> Expect.equal "partition invariant equals ActiveLeaseCount" cap.ActiveLeaseCount
      cap.QueueWaiterCount |> Expect.equal "2 queued waiters" 2
      cap.OldestWaitMs |> Expect.equal "oldest waiter waited 35s" (Some 35_000L)

      let tightEmpty = ExpensiveWorkLease.reconcileCapacity dto0 (Some MemoryPressure.Tight) [ ExpensiveWorkLease.reconciledGrant g1 ] [] [ { LeaseId = ""; Holder = g1.Holder; Kind = g1.Kind; ProcessId = 100; LivenessVerified = true; Alive = false } ]
      tightEmpty.LeaseCap |> Expect.equal "Tight cap is observed as 1" (Some 1)
      tightEmpty.PidVerifiedLeases |> Expect.equal "dead PID is never counted in PidVerifiedLeases" 0
      tightEmpty.StalePidLeases |> Expect.equal "dead PID counted strictly in StalePidLeases" 1
      tightEmpty.OldestWaitMs |> Expect.isNone "empty queue has OldestWaitMs = None"

      let criticalClamped = ExpensiveWorkLease.reconcileCapacity dto0 (Some MemoryPressure.Critical) [] [ { Holder = "w"; Kind = "full_build"; RequestedAt = dto0.AddSeconds 5.0 } ] []
      criticalClamped.LeaseCap |> Expect.equal "Critical cap is observed as 0" (Some 0)
      criticalClamped.OldestWaitMs |> Expect.equal "negative skew clamps to 0L" (Some 0L)
      let unknown = ExpensiveWorkLease.reconcileCapacity dto0 None [] [] []
      unknown.LeaseCap |> Expect.isNone "absent pressure leaves the cap unknown"

    testCase "an explicit retired PID status cannot match a new grant for the same holder and kind" <| fun () ->
      let grant : ExpensiveWorkLease.GrantView = { Holder = "agent · /repo"; Kind = "full_build"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 10. }
      let active = ExpensiveWorkLease.reconciledGrant grant
      let retiredId = ExpensiveWorkLease.displayId grant.Holder grant.Kind (dto0.AddMinutes -15.)
      let retired : ExpensiveWorkLease.LeasedPidStatus =
        { LeaseId = retiredId; Holder = grant.Holder; Kind = grant.Kind; ProcessId = 1001; LivenessVerified = true; Alive = false }
      let classify status =
        let capacity = ExpensiveWorkLease.reconcileCapacity dto0 (Some MemoryPressure.Normal) [active] [] [status]
        capacity.UnverifiedLeases, capacity.StalePidLeases, capacity.PidVerifiedLeases
      retiredId |> Expect.notEqual "retired ID differs from the active grant's display ID" active.DisplayId
      classify retired |> Expect.equal "an explicit ID mismatch leaves the active grant unverified" (1, 0, 0)
      classify { retired with LeaseId = active.DisplayId } |> Expect.equal "matching the exact ID classifies the stale PID" (0, 1, 0)

    testCase "legacy reconciliation uses newest journal sequence and the authoritative grant ID" <| fun () ->
      let grantedAt = dto0.AddTicks 1L
      let holder, kind = "pi · /repo", "full_build"
      let id = ExpensiveWorkLease.displayId holder kind grantedAt
      let grant : LeaseGrant =
        { Id = id; Holder = holder; Kind = kind; GrantedAtMs = grantedAt.ToUnixTimeMilliseconds()
          ExpiresAtMs = grantedAt.AddMinutes(10.).ToUnixTimeMilliseconds() }
      ExpensiveWorkLease.displayId holder kind (DateTimeOffset.FromUnixTimeMilliseconds grant.GrantedAtMs)
      |> Expect.notEqual "millisecond precision loses the ticks used in the transmitted ID" id
      let running : OperationEntry =
        { Sequence = 2L; AtMs = grant.GrantedAtMs; CallerLabel = holder; Operation = kind; Session = ""; Project = "/repo"
          Ok = false; Code = "running"; Message = ""; Run = None
          Lease = Some { Kind = kind; LeaseId = id; RunId = "new"; Executable = "dotnet"; Arguments = [|"build"|]
                         ProcessId = 444; StartedAtMs = grant.GrantedAtMs } }
      let finished =
        { running with
            Sequence = 1L; AtMs = running.AtMs + 1L; Ok = true; Code = ""
            Lease = running.Lease |> Option.map (fun command -> { command with RunId = "old"; ProcessId = 443 }) }
      let classify entries =
        let capacity =
          Project.reconcileSnapshotCapacity
            { Sequence = 1L; SampledAtMs = grant.GrantedAtMs
              Snapshot = { Revision = 1L; Worker = Idle; Sessions = [||] }
              Leases = { Revision = 1L; Active = [|grant|]; Queued = [||] }
              Operations = { Revision = 1L; Incarnation = "journal"; StorageError = None; Entries = entries }
              TranscriptsOmitted = true; Capacity = None }
        capacity.UnverifiedLeases, capacity.StalePidLeases, capacity.PidVerifiedLeases
      for entries in [ [|running; finished|]; [|finished; running|] ] do
        classify entries |> Expect.equal "the newest running row wins by sequence over a finished row with later wall time" (1, 0, 0)
      let verified = { running with Message = "owner liveness verified 2026-11-20T12:00:00.0000000+00:00" }
      classify [|finished; verified|] |> Expect.equal "the wire ID still classifies verified liveness after time truncation" (0, 0, 1)

    testTask "positive control: PublishLocalWork populates Capacity with monotonic PeakResidentBytes and reconciled lease counts; remote v11 peers derive lease counts" {
      let directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../../test-results/scratch/fleet-grid-controls", Guid.NewGuid().ToString("N")))
      System.IO.Directory.CreateDirectory directory |> ignore
      let dbPath = System.IO.Path.Combine(directory, "operations.db")
      let store = ComposerJournal.openStore dbPath |> Result.defaultWith failtest
      let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "no worker needed")
      let supervisor = ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok store)
      let! outcome = task {
        try
        let localSource : Facts.ObservationSource = { Node = "z13"; Incarnation = "z13-inc-1" }
        let peerSource : Facts.ObservationSource = { Node = "one"; Incarnation = "one-inc-1" }
        let producer : Facts.ObservationProducer = { Source = localSource; Correlation = Guid.NewGuid() }
        let constants : Facts.Constants = { McpPort = 47749; Version = "0.1.0"; ProcessId = 100 }
        let graph = Facts.Graph(constants, "", supervisor.Journal.Incarnation, observationOwners = [ producer ], localNode = "z13", peerNodes = [ "one" ])

        let gVerified : ExpensiveWorkLease.GrantView = { Holder = "h-verified · /p"; Kind = "full_build"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 10.0 }
        let gUnverified : ExpensiveWorkLease.GrantView = { Holder = "h-unverified · /p"; Kind = "test_suite_run"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 15.0 }
        let gNoCmd : ExpensiveWorkLease.GrantView = { Holder = "h-mcp · /p"; Kind = "rebuild"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 10.0 }
        let gStale : ExpensiveWorkLease.GrantView = { Holder = "h-stale · /p"; Kind = "full_build"; GrantedAt = dto0.AddSeconds 1.0; ExpiresAt = dto0.AddMinutes 10.0 }

        let dispVerified = Project.leaseDisplayId gVerified.Holder gVerified.Kind gVerified.GrantedAt
        let dispUnverified = Project.leaseDisplayId gUnverified.Holder gUnverified.Kind gUnverified.GrantedAt
        let dispStale = Project.leaseDisplayId gStale.Holder gStale.Kind gStale.GrantedAt

        let mkCmd kind dispId runId pid : ComposerJournal.LeasedCommand =
          { Kind = kind; LeaseId = "priv-" + runId; LeaseDisplayId = dispId; RunId = runId
            Executable = "/sdk/dotnet"; Arguments = [ "build" ]; ProcessId = pid; StartedAtMs = dto0.ToUnixTimeMilliseconds() }
        let cmdVerified = mkCmd ComposerJournal.LeasedKind.Build dispVerified "run-verified" 3001
        let cmdUnverified = mkCmd ComposerJournal.LeasedKind.Test dispUnverified "run-unverified" 3002
        let cmdStale = mkCmd ComposerJournal.LeasedKind.Build dispStale "run-stale" 3003

        supervisor.BeginLeasedCommand(gVerified.Holder, "/p", cmdVerified) |> Result.defaultWith failtest |> ignore
        supervisor.BeginLeasedCommand(gUnverified.Holder, "/p", cmdUnverified) |> Result.defaultWith failtest |> ignore
        supervisor.BeginLeasedCommand(gStale.Holder, "/p", cmdStale) |> Result.defaultWith failtest |> ignore

        // Mark cmdVerified alive, cmdUnverified unknown/unverified, cmdStale dead (while lease still in pool!).
        supervisor.ReconcileLeasedCommands(
          (fun pid _ ->
            if pid = 3001 then ProcessWatchdog.ProcessLiveness.Alive
            elif pid = 3002 then ProcessWatchdog.ProcessLiveness.Unknown "unreadable"
            else ProcessWatchdog.ProcessLiveness.Exited),
          (fun lid -> lid = "priv-run-verified" || lid = "priv-run-unverified"),
          5_000L,
          dto0.AddSeconds(5.0).ToUnixTimeMilliseconds(),
          ignore) |> ignore

        graph.Pool({ Serial = 1L; Active = [ gVerified; gUnverified; gNoCmd; gStale ]; Queue = [] }) |> Expect.isOk "pool 1"
        graph.Journal(supervisor.Journal) |> Expect.isOk "journal"
        graph.Liveness(supervisor.CurrentLivenessSnapshot()) |> Expect.isOk "liveness"

        let emptySnap : ComposerSnapshot = { Revision = 1L; Worker = WorkerState.Idle; Sessions = [||] }
        let capOf node =
          graph.Current()
          |> List.pick (function WorkNodes b -> b.Nodes |> Array.tryFind (fun n -> n.Source.Node = node) | _ -> None)
          |> fun n -> n.Value |> Option.bind _.Capacity

        graph.PublishLocalWork(producer, emptySnap, dto0.AddSeconds(5.0).ToUnixTimeMilliseconds()) |> Expect.isOk "local work before health"
        let initialCap = capOf "z13" |> Option.defaultWith (fun () -> failtest "expected local Capacity before health")
        initialCap.LeaseCap |> Expect.isNone "a local snapshot before the first tick has an unknown cap"

        graph.Tick(tick 1L MemoryPressure.Normal 200 (Some 350_000_000L) 32_000_000_000L 64_000_000_000L) |> Expect.isOk "tick 1"
        graph.PublishLocalWork(producer, emptySnap, dto0.AddSeconds(6.0).ToUnixTimeMilliseconds()) |> Expect.isOk "local work 1"
        let cap1 = capOf "z13" |> Option.defaultWith (fun () -> failtest "expected local Capacity")
        cap1.LeaseCap |> Expect.equal "Normal LeaseCap" (Some 4)
        cap1.PidVerifiedLeases |> Expect.equal "1 PID-verified lease" 1
        cap1.UnverifiedLeases |> Expect.equal "2 unverified leases (Unknown + unreported MCP lease)" 2
        cap1.StalePidLeases |> Expect.equal "1 stale/exited PID lease" 1
        cap1.MemoryPressure |> Expect.equal "Normal pressure" "Normal"
        cap1.MachineAvailableBytes |> Expect.equal "avail RAM" 32_000_000_000L
        cap1.MachineTotalBytes |> Expect.equal "total RAM" 64_000_000_000L
        cap1.ResidentBytes |> Expect.equal "current RSS from aggregate telemetry" 350_000_000L
        cap1.PeakResidentBytes |> Expect.equal "peak RSS starts at 350MB" 350_000_000L

        // RSS drops and pressure tightens -> PeakResidentBytes remains monotonically at 350MB while ResidentBytes drops!
        graph.Tick(tick 2L MemoryPressure.Tight 110 (Some 140_000_000L) 8_000_000_000L 64_000_000_000L) |> Expect.isOk "tick 2"
        graph.PublishLocalWork(producer, emptySnap, dto0.AddSeconds(7.0).ToUnixTimeMilliseconds()) |> Expect.isOk "local work 2"
        let cap2 = capOf "z13" |> Option.defaultWith (fun () -> failtest "expected local Capacity")
        cap2.LeaseCap |> Expect.equal "Tight LeaseCap is 1" (Some 1)
        cap2.MemoryPressure |> Expect.equal "Tight pressure" "Tight"
        cap2.ResidentBytes |> Expect.equal "current RSS dropped to 140MB" 140_000_000L
        cap2.PeakResidentBytes |> Expect.equal "PeakResidentBytes stayed monotonically at 350MB" 350_000_000L

        graph.Tick(tick 3L MemoryPressure.Critical 90 None 1_000_000_000L 64_000_000_000L) |> Expect.isOk "tick 3"
        graph.PublishLocalWork(producer, emptySnap, dto0.AddSeconds(8.0).ToUnixTimeMilliseconds()) |> Expect.isOk "local work 3"
        let cap3 = capOf "z13" |> Option.defaultWith (fun () -> failtest "expected local Capacity under critical pressure")
        cap3.LeaseCap |> Expect.equal "Critical pressure publishes an observed zero cap" (Some 0)

        // Remote v11 peer snapshot (Capacity = None) still derives live reconciled lease counts!
        let peerProducer = graph.BindPeer peerSource
        let peerGrant : LeaseGrant = { Id = "lease-peer1"; Kind = "full_build"; Holder = "pi · /repo"; GrantedAtMs = 1L; ExpiresAtMs = 100_000L }
        let peerUnreported : LeaseGrant = { Id = "lease-peer2"; Kind = "rebuild"; Holder = "mcp · /repo"; GrantedAtMs = 2L; ExpiresAtMs = 100_000L }
        let peerOp : OperationEntry =
          { Sequence = 1L; AtMs = 1L; CallerLabel = "pi · /repo"; Operation = "full_build"; Session = ""; Project = "/repo"
            Ok = false; Code = "running"; Message = "owner liveness verified 2026-11-20T12:00:00.0000000+00:00"; Run = None
            Lease = Some { Kind = "full_build"; LeaseId = "lease-peer1"; RunId = "r1"; Executable = "dotnet"; Arguments = [| "build" |]; ProcessId = 444; StartedAtMs = 1L } }
        let peerSnap : WorkSnapshot =
          { Sequence = 1L; SampledAtMs = 5_000L; Snapshot = emptySnap
            Leases = { Revision = 1L; Active = [| peerGrant; peerUnreported |]; Queued = [||] }
            Operations = { Revision = 1L; Incarnation = "j-one"; StorageError = None; Entries = [| peerOp |] }
            TranscriptsOmitted = false; Capacity = None }
        graph.PublishWork(peerProducer, peerSnap) |> Expect.isOk "peer v11 work admitted"
        let peerCap = capOf "one" |> Option.defaultWith (fun () -> failtest "expected derived peer Capacity")
        peerCap.PidVerifiedLeases |> Expect.equal "remote peer derived 1 PID-verified lease" 1
        peerCap.UnverifiedLeases |> Expect.equal "remote peer derived 1 unverified lease" 1
        peerCap.StalePidLeases |> Expect.equal "remote peer derived 0 stale leases" 0
        peerCap.LeaseCap |> Expect.isNone "v11 peer leaves the cap unknown"
        peerCap.MemoryPressure |> Expect.equal "v11 peer leaves pressure blank until resources/upgrade" ""
        peerCap.MachineTotalBytes |> Expect.equal "v11 peer leaves RAM 0 until resources/upgrade" 0L

        graph.Stop()

        // New graph (daemon restart) resets PeakResidentBytes to new incarnation's RSS.
        let producer2 : Facts.ObservationProducer = { Source = { Node = "z13"; Incarnation = "z13-inc-2" }; Correlation = Guid.NewGuid() }
        let graphRestarted = Facts.Graph(constants, "", supervisor.Journal.Incarnation, observationOwners = [ producer2 ], localNode = "z13")
        graphRestarted.Tick(tick 1L MemoryPressure.Normal 90 None 32_000_000_000L 64_000_000_000L) |> Expect.isOk "restarted tick"
        graphRestarted.Pool({ Serial = 1L; Active = []; Queue = [] }) |> Expect.isOk "restarted pool"
        graphRestarted.Journal(ComposerJournal.empty supervisor.Journal.Incarnation) |> Expect.isOk "restarted journal"
        graphRestarted.PublishLocalWork(producer2, emptySnap, dto0.ToUnixTimeMilliseconds()) |> Expect.isOk "restarted local work"
        let restartedCap =
          graphRestarted.Current()
          |> List.pick (function WorkNodes b -> b.Nodes |> Array.tryFind (fun n -> n.Source.Node = "z13") | _ -> None)
          |> fun n -> n.Value |> Option.bind _.Capacity |> Option.get
        restartedCap.PeakResidentBytes |> Expect.equal "daemon restart resets PeakResidentBytes" (90L * 1_048_576L)
        graphRestarted.Stop()
        return Ok ()
        with ex -> return Error ex
      }
      do! supervisor.StopAsync()
      if System.IO.Directory.Exists directory then System.IO.Directory.Delete(directory, true)
      match outcome with
      | Ok () -> ()
      | Error ex -> return raise ex
    }

    testCase "PublishLocalWork classifies each active lease by its newest journal sequence" <| fun () ->
      let localSource : Facts.ObservationSource = { Node = "z13"; Incarnation = "z13-seq" }
      let producer : Facts.ObservationProducer = { Source = localSource; Correlation = Guid.NewGuid() }
      let constants : Facts.Constants = { McpPort = 47749; Version = "0.1.0"; ProcessId = 100 }
      let graph = Facts.Graph(constants, "", "j-seq", observationOwners = [ producer ], localNode = "z13")
      try
        let gRunning : ExpensiveWorkLease.GrantView = { Holder = "h-run · /p"; Kind = "full_build"; GrantedAt = dto0; ExpiresAt = dto0.AddMinutes 10.0 }
        let gUnknown : ExpensiveWorkLease.GrantView = { Holder = "h-unk · /p"; Kind = "test_suite_run"; GrantedAt = dto0.AddSeconds 1.0; ExpiresAt = dto0.AddMinutes 10.0 }
        let dispRunning = Project.leaseDisplayId gRunning.Holder gRunning.Kind gRunning.GrantedAt
        let dispUnknown = Project.leaseDisplayId gUnknown.Holder gUnknown.Kind gUnknown.GrantedAt
        let mkEntry seq dispId runId pid outcome : ComposerJournal.Entry =
          { Sequence = seq; AtMs = dto0.ToUnixTimeMilliseconds() + seq * 1000L; Caller = "h · /p"
            Project = "/p"; Outcome = outcome; Run = None
            Context = ComposerJournal.Context.LeasedCommand {
              Kind = ComposerJournal.LeasedKind.Build; LeaseId = "priv-" + runId; LeaseDisplayId = dispId; RunId = runId
              Executable = "/sdk/dotnet"; Arguments = [ "build" ]; ProcessId = pid; StartedAtMs = dto0.ToUnixTimeMilliseconds() } }
        let settledRunning = mkEntry 1L dispRunning "run-1" 4001 ComposerJournal.Outcome.Succeeded
        let activeRunning = mkEntry 2L dispRunning "run-2" 4002 ComposerJournal.Outcome.Running
        let settledUnknown = mkEntry 3L dispUnknown "unk-1" 4003 (ComposerJournal.Outcome.Failed "exit 1")
        let activeUnknown = mkEntry 4L dispUnknown "unk-2" 4004 (ComposerJournal.Outcome.Unknown "proc unreadable")
        let emptySnap : ComposerSnapshot = { Revision = 1L; Worker = WorkerState.Idle; Sessions = [||] }
        let capOf () =
          graph.Current()
          |> List.pick (function WorkNodes b -> b.Nodes |> Array.tryFind (fun n -> n.Source.Node = "z13") | _ -> None)
          |> fun n -> n.Value |> Option.bind _.Capacity |> Option.get
        graph.Tick(tick 1L MemoryPressure.Normal 120 None 32_000_000_000L 64_000_000_000L) |> Expect.isOk "tick"
        graph.Pool({ Serial = 1L; Active = [ gRunning; gUnknown ]; Queue = [] }) |> Expect.isOk "pool"
        // Place older settled rows both before and after newer rows in storage order; Sequence must decide.
        graph.Journal({ Sequence = 4L; Incarnation = "j-seq"; Entries = [ settledRunning; activeRunning; activeUnknown; settledUnknown ]; StorageError = None }) |> Expect.isOk "journal"
        graph.Liveness({ Serial = 1L; VerifiedAtMs = Map.empty }) |> Expect.isOk "unverified liveness"
        graph.PublishLocalWork(producer, emptySnap, dto0.AddSeconds(5.0).ToUnixTimeMilliseconds()) |> Expect.isOk "publish unverified"
        let before = capOf ()
        before.PidVerifiedLeases |> Expect.equal "0 verified before liveness probe" 0
        before.UnverifiedLeases |> Expect.equal "newest Running + newest Unknown stay unverified, not stale" 2
        before.StalePidLeases |> Expect.equal "older settled runs under same grant never trigger stale PID" 0
        graph.Liveness({ Serial = 2L; VerifiedAtMs = Map.ofList [ "run-2", dto0.AddSeconds(6.0).ToUnixTimeMilliseconds() ] }) |> Expect.isOk "verified liveness"
        graph.PublishLocalWork(producer, emptySnap, dto0.AddSeconds(6.0).ToUnixTimeMilliseconds()) |> Expect.isOk "publish verified"
        let after = capOf ()
        after.PidVerifiedLeases |> Expect.equal "verified newest Running counts as PID-verified" 1
        after.UnverifiedLeases |> Expect.equal "newest Unknown remains unverified" 1
        after.StalePidLeases |> Expect.equal "still 0 stale PIDs" 0
      finally
        graph.Stop()

    testCase "mixed v9/v11/v12/v13 peer frame and browser v13 codec round-trip" <| fun () ->
      let cap : NodeCapacity =
        { LeaseCap = Some 4; PidVerifiedLeases = 2; UnverifiedLeases = 1; StalePidLeases = 1
          MemoryPressure = "Normal"; MachineAvailableBytes = 10_000L; MachineTotalBytes = 20_000L
          ResidentBytes = 3_000L; PeakResidentBytes = 4_000L }
      let snap : WorkSnapshot =
        { Sequence = 9L; SampledAtMs = 12345L
          Snapshot = { Revision = 1L; Worker = Idle; Sessions = [||] }
          Leases = { Revision = 1L; Active = [||]; Queued = [||] }
          Operations = { Revision = 1L; Incarnation = "j"; StorageError = None; Entries = [||] }
          TranscriptsOmitted = true; Capacity = Some cap }

      // Browser event (family 2, v13) round-trips Capacity and AgentRun.Aged.
      let workBytes = Bozzetto.Web.Shared.BinaryCodec.encodeEvent (WorkNodes { Revision = 1L; Nodes = [| { Source = { Node = "z13"; Incarnation = "i" }; Local = true; Status = "available"; Note = ""; Value = Some snap } |] })
      workBytes[4] |> Expect.equal "browser envelope is v13" (byte ProtocolVersion)
      Bozzetto.Web.Shared.BinaryCodec.decodeEvent workBytes
      |> Expect.equal "browser v13 WorkNodes preserves Capacity" (Ok (WorkNodes { Revision = 1L; Nodes = [| { Source = { Node = "z13"; Incarnation = "i" }; Local = true; Status = "available"; Note = ""; Value = Some snap } |] }))

      // PeerFrame (family 4) emits v11 (Capacity omitted on wire for deployed peers), accepts {9, 11, 12, 13}.
      let peerBytes = Bozzetto.Web.Shared.BinaryCodec.encodePeerFrame (PeerWork snap)
      peerBytes[4] |> Expect.equal "peer frame emits v11 compat" (byte Bozzetto.Web.Shared.BinaryCodec.PeerCompatProtocolVersion)
      for version in [ byte LegacyProtocolVersion; 11uy; 12uy ] do
        let copy = Array.copy peerBytes
        copy[4] <- version
        Bozzetto.Web.Shared.BinaryCodec.decodePeerFrame copy
        |> Expect.equal (sprintf "v%d peer frame decodes with Capacity = None" version) (Ok (PeerWork { snap with Capacity = None }))

      let peerV13Bytes = Bozzetto.Web.Shared.BinaryCodec.encodePeerFrameVersion ProtocolVersion (PeerWork snap)
      peerV13Bytes[4] |> Expect.equal "v13 peer envelope carries version 13" (byte ProtocolVersion)
      Bozzetto.Web.Shared.BinaryCodec.decodePeerFrame peerV13Bytes
      |> Expect.equal "v13 peer frame decodes Capacity losslessly" (Ok (PeerWork snap))
  ]
