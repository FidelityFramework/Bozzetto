module Bozzetto.Tests.FleetBalanceTests

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Fidelity.Data.JSON
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Build
open Bozzetto.Cohort
open Bozzetto.MemberTable
open Bozzetto.Features
open Bozzetto.Server
open Bozzetto.Simulation
open Bozzetto.Web.Shared.Protocol

module Facts = Bozzetto.Server.UiBridge.Facts
module BinaryCodec = Bozzetto.Web.Shared.BinaryCodec
module WatchdogCli = Bozzetto.Build.WatchdogCli

let private t0 = DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)

let private silentLogger =
  { new Bozzetto.Utils.ILogger with
      member _.LogInfo _ = ()
      member _.LogDebug _ = ()
      member _.LogWarning _ = ()
      member _.LogError _ = () }

let private counterEntropy () : unit -> byte[] =
  let mutable n = 0
  fun () ->
    n <- n + 1
    BitConverter.GetBytes n

let private emptyFrame (version: int64) (members: (MemberId * JoinableRole * SeatState * DateTime * string) list) : CohortFrame<MemberId> =
  { Version = LanguagePrimitives.Int64WithMeasure<Measures.ledgerSeq> version
    SessionGens = [||]
    Dirty = FrameRegions.NoRegions
    Conductor = members |> List.tryHead |> Option.map (fun (m, _, _, _, _) -> m)
    MemberIds = members |> List.map (fun (m, _, _, _, _) -> m) |> List.toArray
    MemberRole = members |> List.map (fun (_, r, _, _, _) -> r) |> List.toArray
    MemberSeat = members |> List.map (fun (_, _, s, _, _) -> s) |> List.toArray
    MemberRenewal = members |> List.map (fun (_, _, _, d, _) -> d) |> List.toArray
    MemberAgentName = members |> List.map (fun (_, _, _, _, n) -> if String.IsNullOrWhiteSpace n then None else Some n) |> List.toArray
    ClaimIds = [||]
    ClaimScope = [||]
    ClaimHolderIndex = [||]
    ClaimHolderName = [||]
    ClaimFence = [||]
    ClaimState = [||]
    TestIds = [||]
    Pass = [||]
    Fail = [||]
    Stale = [||]
    IntegrationHead = Cohort.nullSha
    LandingIds = [||]
    LandingRequesterIndex = [||]
    LandingStatement = [||]
    LandingCommits = [||]
    LandingState = [||]
    LandingQueuePosition = [||] }

let private mkNode name isLocal avail syncStatus pressure active cap queued availGb : FleetBalance.NodeBalanceInput =
  { NodeId = name
    IsLocal = isLocal
    Available = avail
    CheckoutSync = syncStatus
    Pressure = pressure
    ActiveLeases = active
    LeaseCap = cap
    QueuedLeases = queued
    AvailableRamBytes = int64 availGb * 1_073_741_824L
    TotalRamBytes = 128L * 1_073_741_824L }

let private mkSnap sequence activeLeases cap pressure availGb checkoutSynced : WorkSnapshot =
  let grants =
    Array.init activeLeases (fun i ->
      { LeaseGrant.Id = sprintf "lease-%d" i
        Kind = "full_build"
        Holder = sprintf "worker-%d" i
        GrantedAtMs = 100L
        ExpiresAtMs = 200L })
  let capacity : NodeCapacity option =
    cap
    |> Option.map (fun c ->
      { LeaseCap = Some(int32 c)
        PidVerifiedLeases = int32 activeLeases
        UnverifiedLeases = 0
        StalePidLeases = 0
        MemoryPressure = pressure
        MachineAvailableBytes = int64 availGb * 1_073_741_824L
        MachineTotalBytes = 128L * 1_073_741_824L
        ResidentBytes = 100_000_000L
        PeakResidentBytes = 150_000_000L
        CheckoutSynced = checkoutSynced })
  { Sequence = sequence
    SampledAtMs = 1_000L
    Snapshot = { Revision = 1L; Worker = Idle; Sessions = [||] }
    Leases = { Revision = sequence; Active = grants; Queued = [||] }
    Operations = { Revision = 1L; Incarnation = "j"; StorageError = None; Entries = [||] }
    TranscriptsOmitted = false
    Capacity = capacity }

let private mkRetrieval (state: string) (branchStatus: string) : RetrievalStatus =
  { Revision = 1L
    State = state
    BranchStatus = branchStatus
    BranchName = if branchStatus = "on_main_indexed" then "main" else "side"
    OnMain = (branchStatus = "on_main_indexed")
    Generation = 1L
    SnapshotId = "snap-1"
    PublishedAtMs = 1000L
    ChangedAtMs = None
    HeldUntilMs = None
    QueuedRetrievals = 0
    ExpectedSources = [||]
    PendingSources = [||]
    StaleSources = [||]
    Reasons = [||]
    Revisions = [||]
    Note = "" }

let private mkWorkReport runId name project status seq : AgentWork.Report =
  { Run = runId
    Session = "s-" + runId
    Sequence = seq
    Name = name
    Model = "test-model"
    Endpoint = "LAN"
    Project = project
    Focus = "work"
    Operation = "op"
    Status = status
    Usage = None }

let private jsonObj value =
  match value with
  | JsonValue.Object fields -> fields |> Map.ofList
  | other -> failtestf "expected JsonValue.Object, got %A" other

let private parseJsonObj (raw: string) =
  match Json.parse raw with
  | Ok value -> jsonObj value
  | Error err -> failtestf "invalid JSON '%s': %s" raw err

let private withEnvVar (name: string) (value: string option) (run: unit -> Task<'a>) : Task<'a> = task {
  let prior = Environment.GetEnvironmentVariable name
  try
    Environment.SetEnvironmentVariable(name, Option.toObj value)
    return! run ()
  finally
    Environment.SetEnvironmentVariable(name, prior)
}

let private pureAndSimTests =
  testList "FleetBalance pure core, codec, CLI, and DST simulation" [

    testCase "tryParsePolicy parses valid values and rejects unknown values with typed error" <| fun () ->
      FleetBalance.tryParsePolicy None |> Expect.equal "default None -> AdvisoryOnly" (Ok FleetBalance.Z13ThrottlePolicy.AdvisoryOnly)
      FleetBalance.tryParsePolicy (Some "  ") |> Expect.equal "blank -> AdvisoryOnly" (Ok FleetBalance.Z13ThrottlePolicy.AdvisoryOnly)
      FleetBalance.tryParsePolicy (Some "off") |> Expect.equal "off" (Ok FleetBalance.Z13ThrottlePolicy.Off)
      FleetBalance.tryParsePolicy (Some "ADVISORY_ONLY") |> Expect.equal "advisory_only" (Ok FleetBalance.Z13ThrottlePolicy.AdvisoryOnly)
      FleetBalance.tryParsePolicy (Some "enforce_when_lan_idle") |> Expect.equal "enforce_when_lan_idle" (Ok FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle)
      match FleetBalance.tryParsePolicy (Some "aggressive") with
      | Error(FleetBalance.PolicyParseError.UnrecognizedPolicy raw) ->
        raw |> Expect.equal "preserves trimmed input" "aggressive"
        FleetBalance.Z13ThrottlePolicy.describeParseError (FleetBalance.PolicyParseError.UnrecognizedPolicy raw)
        |> Expect.stringContains "mentions expected tokens" "enforce_when_lan_idle"
      | Ok other -> failtestf "expected error for unrecognized policy, got %A" other

    testCase "evaluatePlacement ranks least-loaded healthy synced LAN worker and supports Z13 pin and throttle" <| fun () ->
      let synced = FleetBalance.CheckoutSyncStatus.Synced
      let nodes = [
        mkNode "z13" true true synced (Some MemoryPressure.Normal) 2 (Some 4) 1 32
        mkNode "one" false true synced (Some MemoryPressure.Normal) 2 (Some 4) 0 64
        mkNode "two" false true synced (Some MemoryPressure.Normal) 1 (Some 4) 0 48
        mkNode "three" false true synced (Some MemoryPressure.Normal) 1 (Some 4) 0 96
      ]
      match FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.AdvisoryOnly "z13" nodes ExpensiveWorkLease.Kind.FullBuild false with
      | FleetBalance.PlacementDecision.Proceed(Some adv) ->
        adv.RecommendedNode |> Expect.equal "same free slots (3), equal queue (1), higher RAM breaks tie to three" "three"
        adv.Throttled |> Expect.isFalse "advisory only never throttles"
        adv.Candidates |> List.map _.NodeId |> Expect.equal "ranked candidate order" [ "three"; "two"; "one" ]
      | other -> failtestf "expected Proceed(Some), got %A" other

      match FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle "z13" nodes ExpensiveWorkLease.Kind.TestSuiteRun false with
      | FleetBalance.PlacementDecision.Throttled adv ->
        adv.Throttled |> Expect.isTrue "EnforceWhenLanIdle throttles non-pinned test run"
        adv.RecommendedNode |> Expect.equal "throttled names best LAN candidate" "three"
        adv.Reason |> Expect.stringContains "names pinned override" "--pin-z13"
      | other -> failtestf "expected Throttled, got %A" other

      FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle "z13" nodes ExpensiveWorkLease.Kind.FullBuild true
      |> Expect.equal "Z13 pin bypasses throttle" (FleetBalance.PlacementDecision.BypassedPinned "pinned_to_local")

      for kind in [ ExpensiveWorkLease.Kind.SessionCreateOrWarmup; ExpensiveWorkLease.Kind.RunApp ] do
        FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle "z13" nodes kind false
        |> Expect.equal (sprintf "%A always bypasses Z13 throttle" kind) (FleetBalance.PlacementDecision.BypassedPinned "local_only_lease_kind")

      FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.Off "z13" nodes ExpensiveWorkLease.Kind.Rebuild false
      |> Expect.equal "Off suppresses advisory and throttle" (FleetBalance.PlacementDecision.Proceed None)

    testCase "restricts Z13 throttle and offload policy strictly to Z13/workstation origin and excludes both as remote candidates" <| fun () ->
      FleetBalance.isZ13Origin "z13" |> Expect.isTrue "lowercase z13 is Z13 origin"
      FleetBalance.isZ13Origin " Z13 " |> Expect.isTrue "trimmed uppercase Z13 is Z13 origin"
      FleetBalance.isZ13Origin "workstation" |> Expect.isTrue "installed boz launcher BOZZETTO_NODE_ID=workstation is Z13 origin"
      FleetBalance.isZ13Origin " WORKSTATION " |> Expect.isTrue "trimmed uppercase WORKSTATION is Z13 origin"
      FleetBalance.isZ13Origin "one" |> Expect.isFalse "one is not Z13 origin"
      FleetBalance.isZ13Origin "two" |> Expect.isFalse "two is not Z13 origin"

      let synced = FleetBalance.CheckoutSyncStatus.Synced
      let workstationNodes = [
        mkNode "workstation" true true synced (Some MemoryPressure.Tight) 1 (Some 1) 0 32
        mkNode "one" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
      ]
      match FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle "workstation" workstationNodes ExpensiveWorkLease.Kind.FullBuild false with
      | FleetBalance.PlacementDecision.Throttled adv ->
        adv.RecommendedNode |> Expect.equal "workstation origin throttles and recommends idle synced one" "one"
      | other -> failtestf "expected Throttled for workstation origin, got %A" other

      let lanNodes = [
        mkNode "one" true true synced (Some MemoryPressure.Tight) 1 (Some 1) 1 32
        mkNode "two" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
        mkNode "z13" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
        mkNode "workstation" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
      ]
      FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle "one" lanNodes ExpensiveWorkLease.Kind.FullBuild false
      |> Expect.equal "busy ONE + idle synced TWO under EnforceWhenLanIdle returns Proceed None (LAN worker never throttles)" (FleetBalance.PlacementDecision.Proceed None)
      FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.AdvisoryOnly "one" lanNodes ExpensiveWorkLease.Kind.FullBuild false
      |> Expect.equal "busy ONE under AdvisoryOnly returns Proceed None" (FleetBalance.PlacementDecision.Proceed None)

      let candidatesFromTwo = FleetBalance.rankEligibleCandidates "two" [
        mkNode "two" true true synced (Some MemoryPressure.Normal) 1 (Some 4) 0 64
        mkNode "z13" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
        mkNode "workstation" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
        mkNode "three" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 64
      ]
      candidatesFromTwo |> List.map _.NodeId |> Expect.equal "summarizeEligibleNode excludes z13 and workstation as remote offload candidates" [ "three" ]

    testCase "negative and fault controls: unsynced, unknown-sync (v13/v11), Critical, unavailable, unknown-cap, and full LAN workers are never recommended and disconnected peers report FreeSlots = None" <| fun () ->
      let synced = FleetBalance.CheckoutSyncStatus.Synced
      let badNodes = [
        mkNode "z13" true true synced (Some MemoryPressure.Tight) 1 (Some 1) 1 16
        mkNode "one" false true FleetBalance.CheckoutSyncStatus.Unsynced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
        mkNode "two" false true FleetBalance.CheckoutSyncStatus.Unknown (Some MemoryPressure.Normal) 0 (Some 4) 0 96
        mkNode "three" false true synced (Some MemoryPressure.Critical) 0 (Some 0) 0 96
        mkNode "four" false true synced None 0 None 0 96
        mkNode "five" false false synced (Some MemoryPressure.Normal) 0 (Some 4) 0 96
        mkNode "six" false true synced (Some MemoryPressure.Normal) 4 (Some 4) 0 96
        mkNode "seven" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 1 96
      ]
      FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle "z13" badNodes ExpensiveWorkLease.Kind.FullBuild false
      |> Expect.equal "refuses to recommend or throttle when no healthy synced LAN slot exists" (FleetBalance.PlacementDecision.Proceed None)

      let summary =
        FleetBalance.summarizeFleetBalance
          t0 FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle (TimeSpan.FromSeconds 300.0) "z13" badNodes []
          { ActiveLeaseCount = 1; OpenClaimCount = 0; UnauditedRunCount = 0; DirtyTrancheCount = 0 }
      let disconnected = summary.Nodes |> List.find (fun n -> n.NodeId = "five")
      disconnected.Available |> Expect.isFalse "disconnected peer carries Available = false"
      disconnected.FreeSlots |> Expect.isNone "disconnected peer with retained cap=4 NEVER advertises FreeSlots = Some 4"
      let unknownCap = summary.Nodes |> List.find (fun n -> n.NodeId = "four")
      unknownCap.FreeSlots |> Expect.isNone "unknown-cap peer reports FreeSlots = None"
      unknownCap.LeaseCap |> Expect.isNone "unknown-cap peer reports LeaseCap = None"
      let v13Peer = summary.Nodes |> List.find (fun n -> n.NodeId = "two")
      v13Peer.CheckoutSync |> Expect.equal "v13 peer retains CheckoutSync = Unknown" FleetBalance.CheckoutSyncStatus.Unknown

      let jsonNodes =
        match (jsonObj (FleetBalanceService.summaryJsonValue None { ActiveLeaseCount = 1; OpenClaimCount = 0; UnauditedRunCount = 0; DirtyTrancheCount = 0 } summary))["nodes"] with
        | JsonValue.Array items -> items |> List.map jsonObj
        | other -> failtestf "expected nodes array, got %A" other
      let fiveJson = jsonNodes |> List.find (fun m -> m["node_id"] = JsonValue.String "five")
      fiveJson["available"] |> Expect.equal "disconnected node available=false" (JsonValue.Bool false)
      fiveJson["free_slots"] |> Expect.equal "disconnected node free_slots=null" JsonValue.Null

      let withHealthy = (mkNode "two" false true synced (Some MemoryPressure.Normal) 0 (Some 4) 0 32) :: (badNodes |> List.filter (fun n -> n.NodeId <> "two"))
      match FleetBalance.evaluatePlacement FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle "z13" withHealthy ExpensiveWorkLease.Kind.FullBuild false with
      | FleetBalance.PlacementDecision.Throttled adv ->
        adv.RecommendedNode |> Expect.equal "picks only healthy synced node" "two"
        adv.Candidates |> List.map _.NodeId |> Expect.equal "excludes all ineligible nodes" [ "two" ]
      | other -> failtestf "expected Throttled on two, got %A" other

    testCase "revision-verified checkoutSyncedOfRetrieval, readCheckoutBranch, and PublishLocalWork enforce 40-hex SHA match and exclude stale/unknown local main from placement" <| fun () ->
      let shaA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
      let shaAUpper = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
      let shaB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
      let shaC = "cccccccccccccccccccccccccccccccccccccccc"
      let mainWithSha shaOpt : RetrievalObservation.BranchInfo =
        { BranchName = "main"; OnMain = true; Repository = Some "Bozzetto"; HeadRevision = shaOpt }
      let freshWithRev revOpt =
        { mkRetrieval "fresh" "on_main_indexed" with
            Revisions = [| { Source = "FidelityFramework/Bozzetto"; Revision = revOpt } |] }

      FleetBalanceService.checkoutSyncedOfRetrieval (mainWithSha (Some shaA)) (freshWithRev (Some shaAUpper))
      |> Expect.equal "matching 40-hex SHA (case-insensitive) -> Some true" (Some true)
      FleetBalanceService.checkoutSyncedOfRetrieval (mainWithSha (Some shaA)) (freshWithRev (Some shaB))
      |> Expect.equal "stale/behind local main 40-hex SHA -> Some false" (Some false)
      FleetBalanceService.checkoutSyncedOfRetrieval (mainWithSha None) (freshWithRev (Some shaA))
      |> Expect.equal "missing local HeadRevision -> None" None
      FleetBalanceService.checkoutSyncedOfRetrieval (mainWithSha (Some shaA)) (freshWithRev None)
      |> Expect.equal "missing indexed Revision -> None" None
      FleetBalanceService.checkoutSyncedOfRetrieval (mainWithSha (Some shaA)) (mkRetrieval "fresh" "on_main_indexed")
      |> Expect.equal "no matching repo revision proof -> None" None

      for state, branchStatus in [
        "fresh", "quarantined_branch"
        "fresh", "indexing_pending"
        "crossing_held", "on_main_indexed"
        "indexing", "on_main_indexed"
        "indexing", "unmanaged_checkout"
      ] do
        FleetBalanceService.checkoutSyncedOfRetrieval (mainWithSha (Some shaA)) (mkRetrieval state branchStatus)
        |> Expect.equal (sprintf "%s/%s -> Some false" state branchStatus) (Some false)
      for state, branchStatus in [
        "unconfigured", "unconfigured"
        "fresh", "branch_unknown"
        "unavailable", "unavailable"
        "fresh", "unmanaged_checkout"
      ] do
        FleetBalanceService.checkoutSyncedOfRetrieval (mainWithSha (Some shaA)) (mkRetrieval state branchStatus)
        |> Expect.equal (sprintf "%s/%s -> None" state branchStatus) None

      let tempRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../test-results/scratch", "boz-rev-sync-" + Guid.NewGuid().ToString("N")))
      let lockedRefsHeads = Path.Combine(tempRoot, "unreadable-loose", ".git", "refs", "heads")
      try
        let looseRepo = Path.Combine(tempRoot, "Bozzetto")
        Directory.CreateDirectory(Path.Combine(looseRepo, ".git", "refs", "heads")) |> ignore
        File.WriteAllText(Path.Combine(looseRepo, ".git", "HEAD"), "ref: refs/heads/main\n")
        File.WriteAllText(Path.Combine(looseRepo, ".git", "refs", "heads", "main"), shaA + "\n")
        (RetrievalObservation.readCheckoutBranch looseRepo).HeadRevision
        |> Expect.equal "resolves loose .git/refs/heads/main SHA" (Some shaA)

        // Fault-injection negative control: corrupt loose ref MUST NOT fall through to stale packed-refs.
        let corruptLooseRepo = Path.Combine(tempRoot, "corrupt-loose")
        Directory.CreateDirectory(Path.Combine(corruptLooseRepo, ".git", "refs", "heads")) |> ignore
        File.WriteAllText(Path.Combine(corruptLooseRepo, ".git", "HEAD"), "ref: refs/heads/main\n")
        File.WriteAllText(Path.Combine(corruptLooseRepo, ".git", "refs", "heads", "main"), "corrupt-not-sha40\n")
        File.WriteAllText(Path.Combine(corruptLooseRepo, ".git", "packed-refs"), "# pack-refs\n" + shaB + " refs/heads/main\n")
        (RetrievalObservation.readCheckoutBranch corruptLooseRepo).HeadRevision
        |> Expect.isNone "corrupt loose ref returns None rather than falling through to stale packed-refs"

        // Fault-injection negative control: unreadable refs/heads dir (chmod 000) MUST NOT fall through to stale packed-refs.
        let unreadableRepo = Path.Combine(tempRoot, "unreadable-loose")
        Directory.CreateDirectory lockedRefsHeads |> ignore
        File.WriteAllText(Path.Combine(unreadableRepo, ".git", "HEAD"), "ref: refs/heads/main\n")
        File.WriteAllText(Path.Combine(lockedRefsHeads, "main"), shaA + "\n")
        File.WriteAllText(Path.Combine(unreadableRepo, ".git", "packed-refs"), "# pack-refs\n" + shaB + " refs/heads/main\n")
        if OperatingSystem.IsLinux() then
          File.SetUnixFileMode(lockedRefsHeads, UnixFileMode.None)
          (RetrievalObservation.readCheckoutBranch unreadableRepo).HeadRevision
          |> Expect.isNone "unreadable refs/heads returns None rather than falling through to stale packed-refs"
          File.SetUnixFileMode(lockedRefsHeads, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

        let packedRepo = Path.Combine(tempRoot, "packed-repo")
        Directory.CreateDirectory(Path.Combine(packedRepo, ".git")) |> ignore
        File.WriteAllText(Path.Combine(packedRepo, ".git", "HEAD"), "ref: refs/heads/main\n")
        File.WriteAllText(Path.Combine(packedRepo, ".git", "packed-refs"), "# pack-refs\n" + shaB + " refs/heads/main\n")
        (RetrievalObservation.readCheckoutBranch packedRepo).HeadRevision
        |> Expect.equal "resolves .git/packed-refs SHA" (Some shaB)

        let wtDir = Path.Combine(tempRoot, "wt-dir")
        let wtMeta = Path.Combine(tempRoot, "bare-git", "worktrees", "wt-1")
        Directory.CreateDirectory wtDir |> ignore
        Directory.CreateDirectory(Path.Combine(tempRoot, "bare-git", "refs", "heads")) |> ignore
        Directory.CreateDirectory wtMeta |> ignore
        File.WriteAllText(Path.Combine(wtDir, ".git"), "gitdir: ../bare-git/worktrees/wt-1\n")
        File.WriteAllText(Path.Combine(wtMeta, "commondir"), "../..\n")
        File.WriteAllText(Path.Combine(wtMeta, "HEAD"), "ref: refs/heads/main\n")
        File.WriteAllText(Path.Combine(tempRoot, "bare-git", "refs", "heads", "main"), shaC + "\n")
        (RetrievalObservation.readCheckoutBranch wtDir).HeadRevision
        |> Expect.equal "resolves worktree commondir ref SHA" (Some shaC)
      finally
        if OperatingSystem.IsLinux() && Directory.Exists lockedRefsHeads then
          try File.SetUnixFileMode(lockedRefsHeads, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) with _ -> ()
        if Directory.Exists tempRoot then Directory.Delete(tempRoot, true)

      let localSource : Facts.ObservationSource = { Node = "one"; Incarnation = "one-inc" }
      let producer : Facts.ObservationProducer = { Source = localSource; Correlation = Guid.NewGuid() }
      let constants : Facts.Constants = { McpPort = 47749; Version = "test"; ProcessId = 42 }
      let graph = Facts.Graph(constants, "", "j-sync", observationOwners = [ producer ], localNode = "one")
      try
        graph.Tick {
          HealthTick.Tick.Serial = 1L; Record = None; Telemetry = None
          Machine = { TotalBytes = 64L * 1_073_741_824L; AvailableBytes = 32L * 1_073_741_824L }
          Level = MemoryPressure.Normal; SampledAtMs = 100L; RecordAtMs = 0L } |> Expect.isOk "tick"
        graph.Pool { Serial = 1L; Active = []; Queue = [] } |> Expect.isOk "pool"
        graph.Journal (Bozzetto.ComposerIntegration.ComposerJournal.empty "j-sync") |> Expect.isOk "journal"
        let snap : ComposerSnapshot = { Revision = 1L; Worker = WorkerState.Idle; Sessions = [||] }
        let capSynced () =
          graph.Current()
          |> List.pick (function WorkNodes b -> b.Nodes |> Array.tryFind (fun n -> n.Source.Node = "one") | _ -> None)
          |> fun n -> n.Value |> Option.bind _.Capacity |> Option.bind _.CheckoutSynced
        graph.PublishLocalWork(producer, snap, 1000L) |> Expect.isOk "before retrieval"
        capSynced () |> Expect.isNone "unobserved retrieval yields CheckoutSynced = None"

        let mkObs serial branch readiness : RetrievalObservation.Observation =
          { Serial = serial; ObservedAtMs = serial * 1000L; Branch = branch; Readiness = readiness; LastFreshSources = [| "Bozzetto" |] }
        let mainSyncedBranch = mainWithSha (Some shaA)
        let mainStaleBranch = mainWithSha (Some shaB)
        let mainMissingShaBranch = mainWithSha None
        let sideBranch : RetrievalObservation.BranchInfo =
          { BranchName = "feat/side"; OnMain = false; Repository = Some "Bozzetto"; HeadRevision = Some shaA }
        let freshReadiness =
          RetrievalObservation.Readiness.Fresh {
            Generation = 1L; SnapshotId = "s1"; PublishedAtMs = 1000L
            Revisions = [| { Source = "Bozzetto"; Revision = Some shaAUpper } |] }
        graph.Retrieval(mkObs 2L mainSyncedBranch freshReadiness) |> Expect.isOk "fresh matching SHA retrieval"
        graph.PublishLocalWork(producer, snap, 2000L) |> Expect.isOk "publish synced"
        capSynced () |> Expect.equal "matching 40-hex SHA -> Some true" (Some true)

        graph.Retrieval(mkObs 3L mainStaleBranch freshReadiness) |> Expect.isOk "stale local main SHA retrieval"
        graph.PublishLocalWork(producer, snap, 3000L) |> Expect.isOk "publish stale main"
        capSynced () |> Expect.equal "stale/behind local main SHA -> Some false" (Some false)

        graph.Retrieval(mkObs 4L mainMissingShaBranch freshReadiness) |> Expect.isOk "missing local SHA retrieval"
        graph.PublishLocalWork(producer, snap, 4000L) |> Expect.isOk "publish missing SHA"
        capSynced () |> Expect.isNone "missing local SHA -> None"

        graph.Retrieval(mkObs 5L sideBranch freshReadiness) |> Expect.isOk "quarantined branch retrieval"
        graph.PublishLocalWork(producer, snap, 5000L) |> Expect.isOk "publish quarantined"
        capSynced () |> Expect.equal "quarantined_branch -> Some false" (Some false)

        graph.Retrieval(mkObs 6L mainSyncedBranch RetrievalObservation.Readiness.Unconfigured) |> Expect.isOk "unconfigured retrieval"
        graph.PublishLocalWork(producer, snap, 6000L) |> Expect.isOk "publish unconfigured"
        capSynced () |> Expect.isNone "unconfigured -> None"

        // Placement check: stale-SHA localSync (Some false) and missing-SHA localSync (None) -> Unknown/Unsynced, never Synced.
        let z13Pool : Features.LeaseWatch.Pool = { Serial = 1L; Active = []; Queue = [] }
        let inputsFromStale =
          FleetBalanceService.buildNodeBalanceInputs "one" (Some MemoryPressure.Normal) z13Pool
            { TotalBytes = 64L * 1_073_741_824L; AvailableBytes = 32L * 1_073_741_824L } (Some false) None
        inputsFromStale.Head.CheckoutSync |> Expect.equal "stale SHA maps to Unsynced" FleetBalance.CheckoutSyncStatus.Unsynced
        let inputsFromMissing =
          FleetBalanceService.buildNodeBalanceInputs "one" (Some MemoryPressure.Normal) z13Pool
            { TotalBytes = 64L * 1_073_741_824L; AvailableBytes = 32L * 1_073_741_824L } None None
        inputsFromMissing.Head.CheckoutSync |> Expect.equal "missing SHA maps to Unknown" FleetBalance.CheckoutSyncStatus.Unknown
      finally
        graph.Stop()

    testCase "evaluateSilentAgents recognizes cohort families, alerts silent auditors/implementers when work accumulates, and computes exact transition" <| fun () ->
      let members : FleetBalance.CohortMemberHeartbeat list = [
        { MemberId = "mcp:auditor"; DisplayName = "pi-verifier"; AgentFamily = "pi"; Role = JoinableRole.Verifier
          Present = true; LastActivityUtc = Some(t0.AddSeconds -310.0); LastRenewalUtc = t0.AddSeconds -310.0 }
        { MemberId = "mcp:claude"; DisplayName = "claude-code-worker"; AgentFamily = ""; Role = JoinableRole.Implementer
          Present = true; LastActivityUtc = Some(t0.AddSeconds -100.0); LastRenewalUtc = t0.AddSeconds -100.0 }
        { MemberId = "mcp:codex"; DisplayName = "codex-runner"; AgentFamily = ""; Role = JoinableRole.Implementer
          Present = true; LastActivityUtc = None; LastRenewalUtc = t0.AddSeconds -305.0 }
        { MemberId = "mcp:anti"; DisplayName = "antigravity-coord"; AgentFamily = ""; Role = JoinableRole.Observer
          Present = true; LastActivityUtc = Some(t0.AddSeconds -50.0); LastRenewalUtc = t0.AddSeconds -20.0 }
        { MemberId = "mcp:departed"; DisplayName = "departed-pi"; AgentFamily = "pi"; Role = JoinableRole.Verifier
          Present = false; LastActivityUtc = None; LastRenewalUtc = t0.AddHours -1.0 }
      ]
      let idleWork : FleetBalance.ActiveWorkContext = {
        ActiveLeaseCount = 0; OpenClaimCount = 0; UnauditedRunCount = 0; DirtyTrancheCount = 0
      }
      let idleEval = FleetBalance.evaluateSilentAgents t0 (TimeSpan.FromSeconds 300.0) members idleWork
      idleEval.Alerts |> Expect.isEmpty "no alert when zero work is accumulating"
      idleEval.NextSilenceTransitionUtc |> Expect.isNone "no deadline armed when no relevant work exists"

      let busyWork : FleetBalance.ActiveWorkContext = {
        ActiveLeaseCount = 1; OpenClaimCount = 2; UnauditedRunCount = 1; DirtyTrancheCount = 1
      }
      let busyEval = FleetBalance.evaluateSilentAgents t0 (TimeSpan.FromSeconds 300.0) members busyWork
      busyEval.Alerts |> List.map (fun a -> a.Kind, a.AgentFamily, a.MemberId)
      |> Expect.equal "silent auditor ranked before silent implementer" [
        "silent_auditor", "pi", "mcp:auditor"
        "silent_implementer", "codex", "mcp:codex"
      ]
      busyEval.NextSilenceTransitionUtc |> Expect.equal "next deadline is claude at t0 + 200s" (Some(t0.AddSeconds 200.0))

    testCase "strict completed-audit unaudited-tranche accounting ignores verifier get_agent_work, running/unknown/failed/aborted/disconnected verifier runs, and historic landings, clearing only on completed same-project verifier run at >= UpdatedAt" <| fun () ->
      let implA = MemberId.Mcp "impl-a"
      let implB = MemberId.Mcp "impl-b"
      let verActive = MemberId.Mcp "ver-active"
      let verSilent = MemberId.Mcp "ver-silent"
      let oldTime = t0.AddHours -2.0 // well past display classifyDwell windows (5m/30m)
      let frame =
        emptyFrame 1L [
          implA, JoinableRole.Implementer, SeatState.Present, t0, "claude-code"
          implB, JoinableRole.Implementer, SeatState.Present, t0, "codex"
          verActive, JoinableRole.Verifier, SeatState.Present, t0, "pi-active"
          verSilent, JoinableRole.Verifier, SeatState.Present, t0.AddMinutes -15.0, "pi-silent"
        ]
      let baseWork =
        AgentWork.empty
        |> fun s -> AgentWork.report oldTime implA JoinableRole.Implementer (mkWorkReport "run-boz" "claude-code" "/repo/Bozzetto" "completed" 1L) s |> Result.defaultWith (fun e -> failtestf "%A" e)
      // Negative control: verActive has a fresh AgentActivityTracker timestamp (e.g. from get_agent_work), NOT a verifier run.
      let activityAfterRead =
        Map.ofList [
          MemberId.display implA, t0
          MemberId.display implB, t0
          MemberId.display verActive, t0
          MemberId.display verSilent, t0.AddMinutes -15.0
        ]
      let factsAfterRead = FleetBalanceService.extractCohortWorkFacts (Some frame) (Some baseWork) activityAfterRead
      factsAfterRead.UnauditedRunCount |> Expect.equal "aged terminal run stays unaudited despite verifier activity and past dwell window" 1
      let silentEval =
        FleetBalance.evaluateSilentAgents t0 (TimeSpan.FromSeconds 300.0) factsAfterRead.Members
          { ActiveLeaseCount = 0; OpenClaimCount = 0; UnauditedRunCount = factsAfterRead.UnauditedRunCount; DirtyTrancheCount = 0 }
      silentEval.Alerts |> List.exists (fun a -> a.Kind = "silent_auditor" && a.MemberId = MemberId.display verSilent)
      |> Expect.isTrue "verifier calling get_agent_work does NOT suppress another silent auditor's alert"

      // Negative control: verifier run on a DIFFERENT project (or different case on Linux) does NOT clear /repo/Bozzetto.
      for wrongProj in [ "/repo/Farscape"; "/repo/bozzetto" ] do
        let wrongProjWork =
          AgentWork.report t0 verActive JoinableRole.Verifier (mkWorkReport ("ver-other-" + wrongProj) "pi-active" wrongProj "completed" 1L) baseWork
          |> Result.defaultWith (fun e -> failtestf "%A" e)
        (FleetBalanceService.extractCohortWorkFacts (Some frame) (Some wrongProjWork) activityAfterRead).UnauditedRunCount
        |> Expect.equal (sprintf "verifier audit on %s leaves /repo/Bozzetto unaudited" wrongProj) 1

      // Negative control: non-completed ("running", "unknown", "failed", "aborted", "disconnected") verifier runs on same project do NOT clear /repo/Bozzetto.
      for nonTerminalOrFailed in [ "running"; "unknown"; "failed"; "aborted"; "disconnected" ] do
        let nonAuditedVerWork =
          AgentWork.report t0 verActive JoinableRole.Verifier (mkWorkReport ("ver-" + nonTerminalOrFailed) "pi-active" "/repo/Bozzetto" nonTerminalOrFailed 1L) baseWork
          |> Result.defaultWith (fun e -> failtestf "%A" e)
        (FleetBalanceService.extractCohortWorkFacts (Some frame) (Some nonAuditedVerWork) activityAfterRead).UnauditedRunCount
        |> Expect.equal (sprintf "verifier %s run does not clear unaudited count" nonTerminalOrFailed) 1

      // Negative control: historic LandingState.Landed on requester (e.g. from earlier project A) does NOT clear a later unaudited run on project B.
      let landedFrame =
        { frame with
            LandingIds = [| LandingId "landing-1" |]
            LandingRequesterIndex = [| 0 |] // implA
            LandingState = [| LandingState.Landed "deadbeef" |]
            LandingQueuePosition = [| -1 |] }
      (FleetBalanceService.extractCohortWorkFacts (Some landedFrame) (Some baseWork) activityAfterRead).UnauditedRunCount
      |> Expect.equal "historic LandingState.Landed does not clear unaudited run" 1

      // Positive & negative control (#44): live Held claims count as OpenClaimCount, while historical Orphaned claims
      // do not inflate OpenClaimCount or DirtyTrancheCount on an otherwise idle workstation.
      let orphanOnlyFrame =
        { frame with
            ClaimState = [| ClaimState.Orphaned(implA, t0.AddHours -6.0); ClaimState.Released(implA, t0.AddHours -8.0) |] }
      let orphanFacts = FleetBalanceService.extractCohortWorkFacts (Some orphanOnlyFrame) None Map.empty
      orphanFacts.OpenClaimCount |> Expect.equal "historical Orphaned claims excluded from OpenClaimCount" 0
      orphanFacts.DirtyTrancheCount |> Expect.equal "historical Orphaned claims excluded from DirtyTrancheCount" 0
      let heldAndQueuedFrame =
        { frame with
            ClaimState = [| ClaimState.Held implA; ClaimState.Orphaned(implA, t0.AddHours -2.0) |]
            LandingState = [| LandingState.Queued |] }
      let heldFacts = FleetBalanceService.extractCohortWorkFacts (Some heldAndQueuedFrame) None Map.empty
      heldFacts.OpenClaimCount |> Expect.equal "live Held claim counted in OpenClaimCount" 1
      heldFacts.DirtyTrancheCount |> Expect.equal "unsettled Queued landing counted in DirtyTrancheCount" 1

      // Negative control: older completed verifier run (UpdatedAt < run.UpdatedAt) does NOT clear it.
      let olderAuditWork =
        AgentWork.report (oldTime.AddMinutes -5.0) verActive JoinableRole.Verifier (mkWorkReport "ver-older" "pi-active" "/repo/Bozzetto" "completed" 1L) baseWork
        |> Result.defaultWith (fun e -> failtestf "%A" e)
      (FleetBalanceService.extractCohortWorkFacts (Some frame) (Some olderAuditWork) activityAfterRead).UnauditedRunCount
      |> Expect.equal "older verifier audit before implementer run does not clear unaudited count" 1

      // Positive control: terminal "completed" verifier audit run on SAME normalized project ("/repo/Bozzetto/") with UpdatedAt >= run.UpdatedAt clears it!
      let auditedWork =
        AgentWork.report t0 verActive JoinableRole.Verifier (mkWorkReport "ver-ok" "pi-active" " /repo/Bozzetto/ " "completed" 1L) baseWork
        |> Result.defaultWith (fun e -> failtestf "%A" e)
      (FleetBalanceService.extractCohortWorkFacts (Some frame) (Some auditedWork) activityAfterRead).UnauditedRunCount
      |> Expect.equal "matching normalized project completed verifier run clears unaudited count" 0

    testCase "DST FleetBalanceSim satisfies all invariants across 20 seeds and catches all 3 fault-injection twins" <| fun () ->
      for seed in 1 .. 20 do
        let trace = FleetBalanceSim.run seed 40 FleetBalanceSim.FaultMode.NoFault
        FleetBalanceSimInvariants.checkAll trace
        |> Expect.equal (sprintf "seed %d has zero invariant violations" seed) []

      let faultedBadNode = FleetBalanceSim.run 7 50 FleetBalanceSim.FaultMode.RecommendUnsyncedOrLocal |> FleetBalanceSimInvariants.checkAll
      faultedBadNode |> List.exists (fun v -> v.RuleId = "NO-UNSYNCED-OR-CRITICAL-OR-LOCAL-RECOMMENDATION")
      |> Expect.isTrue "RecommendUnsyncedOrLocal twin caught"

      let faultedSuppressThrottle = FleetBalanceSim.run 11 50 FleetBalanceSim.FaultMode.SuppressThrottleWhenLanIdle |> FleetBalanceSimInvariants.checkAll
      faultedSuppressThrottle |> List.exists (fun v -> v.RuleId = "ENFORCE-WHEN-LAN-IDLE-THROTTLES")
      |> Expect.isTrue "SuppressThrottleWhenLanIdle twin caught"

      let faultedSuppressAuditor = FleetBalanceSim.run 13 50 FleetBalanceSim.FaultMode.SuppressSilentAuditorAlert |> FleetBalanceSimInvariants.checkAll
      faultedSuppressAuditor |> List.exists (fun v -> v.RuleId = "SILENT-AUDITOR-VISIBLE")
      |> Expect.isTrue "SuppressSilentAuditorAlert twin caught"

    testCase "WatchdogCli parses --pin-z13 and BOZZETTO_PIN_Z13 with exact negative control" <| fun () ->
      let envClean = WatchdogCli.parseWatchdogEnv None None None None |> Result.defaultWith (fun e -> failtestf "%A" e)
      envClean.PinZ13 |> Expect.isFalse "default PinZ13 is false"
      match WatchdogCli.parseWatchdogArgs envClean [ "--pin-z13"; "full_build"; "dotnet" ] with
      | Ok(opts, rest) ->
        opts.PinZ13 |> Expect.isTrue "--pin-z13 sets PinZ13"
        rest |> Expect.equal "passes command through" [ "full_build"; "dotnet" ]
      | Error e -> failtestf "%A" e

      WatchdogCli.parsePinZ13Env (Some "1") |> Expect.equal "BOZZETTO_PIN_Z13=1" (Ok true)
      WatchdogCli.parsePinZ13Env (Some "0") |> Expect.equal "BOZZETTO_PIN_Z13=0" (Ok false)
      match WatchdogCli.parsePinZ13Env (Some "maybe") with
      | Error(WatchdogCli.WatchdogOptionError.InvalidBoolean(source, raw)) ->
        source |> Expect.equal "exact env source" "BOZZETTO_PIN_Z13"
        raw |> Expect.equal "preserves invalid token" "maybe"
      | other -> failtestf "expected InvalidBoolean, got %A" other

    testCase "BinaryCodec v14 round-trips FleetBalance and rejects unknown tag 16" <| fun () ->
      let sample =
        FleetBalance {
          Revision = 42L
          Policy = "enforce_when_lan_idle"
          SilentThresholdSeconds = 300
          LocalNodeId = "z13"
          LocalBusy = true
          Advisory = Some {
            RecommendedNode = "one"
            Reason = "Local node z13 is busy."
            Policy = "enforce_when_lan_idle"
            Throttled = true
            Candidates = [|
              { NodeId = "one"; FreeSlots = 3; ActiveLeases = 1; LeaseCap = 4; QueuedLeases = 0
                AvailableRamBytes = 64_000_000_000L; TotalRamBytes = 96_000_000_000L; Pressure = "Normal" }
            |] }
          SilentAlerts = [|
            { MemberKey = "mcp:pi-verifier"; DisplayName = "pi-auditor"; AgentFamily = "pi"; Role = "Verifier"
              Kind = "silent_auditor"; IdleSeconds = 360L; LastActivityMs = 1759420440000L
              Reason = "pi-auditor silent" }
          |]
          ActiveLeaseCount = 1
          OpenClaimCount = 1
          UnauditedRunCount = 2
          DirtyTrancheCount = 1
        }
      let encoded = BinaryCodec.encodeEvent sample
      encoded[4..6] |> Expect.equal "v14 tag 15 envelope" [| 14uy; 2uy; 15uy |]
      BinaryCodec.decodeEvent encoded |> Expect.equal "FleetBalance round-trips losslessly" (Ok sample)
      let badTag = Array.copy encoded
      badTag[6] <- 16uy
      BinaryCodec.decodeEvent badTag |> Expect.equal "tag 16 rejected" (Error "binary: unknown Event tag")
  ]

let private reactiveGraphTests =
  testList "FleetBalance reactive UiBridge.Facts.Graph & one-shot silenceTimer" [

    testTask "positive control: StartFleetBalance pushes rebalance advisory and reactive one-shot silence_auditor transition without polling, clears on activity, and Stop disposes timer" {
      do! task {
        let localSource : Facts.ObservationSource = { Node = "z13"; Incarnation = "z13-inc" }
        let peerOne : Facts.ObservationSource = { Node = "one"; Incarnation = "one-inc" }
        let producer : Facts.ObservationProducer = { Source = localSource; Correlation = Guid.NewGuid() }
        let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 99 }
        let tracker = AgentActivityTracker.create ()
        let ledger = CohortLedger.InMemory.create<MemberId> ()
        let verifier = MemberId.Mcp "verifier-conn"
        let impl = MemberId.Mcp "impl-conn"
        let tBase = DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
        let mutable graphNowTicks = tBase.Ticks
        let utcNow () = DateTime(Interlocked.Read(&graphNowTicks), DateTimeKind.Utc)
        use owner = CohortOwner.start silentLogger ledger (fun () -> tBase) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
        let! joinImpl = owner.Commit(CohortCommand.JoinNamed(impl, JoinableRole.Implementer, None, "claude-code"))
        joinImpl |> Expect.isOk "implementer joined"
        let! joinVer = owner.Commit(CohortCommand.JoinNamed(verifier, JoinableRole.Verifier, None, "pi-auditor"))
        joinVer |> Expect.isOk "verifier joined"
        let! claimRes = owner.Commit(CohortCommand.AcquireClaim(impl, ClaimScope.File "src/Main.fs", "editing"))
        claimRes |> Expect.isOk "claim acquired"

        AgentActivityTracker.recordMemberActivity tracker impl "" None (Some "edit") tBase
        AgentActivityTracker.recordMemberActivity tracker verifier "" None (Some "audit") tBase

        let graph =
          Facts.Graph(
            constants, owner.ReadWork().Incarnation, "j-inc",
            observationOwners = [ producer ], localNode = "z13", peerNodes = [ "one" ],
            utcNow = utcNow, silenceThreshold = TimeSpan.FromSeconds 1.0)
        let balancedReady = TaskCompletionSource<FleetBalanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously)
        let silentReady = TaskCompletionSource<FleetBalanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously)
        let clearedReady = TaskCompletionSource<FleetBalanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, sub = graph.Register(fun kind ev ->
          match kind, ev with
          | UiBridge.Push.FleetBalance, FleetBalance status ->
            let hasAdvisory = status.Advisory |> Option.exists (fun a -> a.RecommendedNode = "one")
            let hasAuditor = status.SilentAlerts |> Array.exists (fun a -> a.Kind = "silent_auditor" && a.AgentFamily = "pi")
            if hasAdvisory && not hasAuditor && not balancedReady.Task.IsCompleted then
              balancedReady.TrySetResult status |> ignore
            elif hasAdvisory && hasAuditor && not silentReady.Task.IsCompleted then
              silentReady.TrySetResult status |> ignore
            elif silentReady.Task.IsCompleted && Array.isEmpty status.SilentAlerts && not clearedReady.Task.IsCompleted then
              clearedReady.TrySetResult status |> ignore
          | _ -> ())
        use _sub = sub

        let grant : ExpensiveWorkLease.GrantView =
          { Holder = "claude · /repo"; Kind = "full_build"
            GrantedAt = DateTimeOffset.UtcNow; ExpiresAt = DateTimeOffset.UtcNow.AddMinutes 10.0 }
        graph.Tick {
          HealthTick.Tick.Serial = 1L; Record = None; Telemetry = None
          Machine = { TotalBytes = 128L * 1_073_741_824L; AvailableBytes = 64L * 1_073_741_824L }
          Level = MemoryPressure.Normal; SampledAtMs = 100L; RecordAtMs = 0L } |> Expect.isOk "tick admitted"
        graph.Pool { Serial = 1L; Active = [ grant ]; Queue = [] } |> Expect.isOk "pool admitted"
        graph.Membership(owner.ReadFrame()) |> Expect.isOk "membership admitted"
        let peerProd = graph.BindPeer peerOne
        graph.PublishWork(peerProd, mkSnap 1L 0 (Some 4) "Normal" 96 (Some true)) |> Expect.isOk "peer one published"
        use _fleetSub = graph.StartFleetBalance(tracker.Changed, fun () -> AgentActivityTracker.snapshotWithVersion tracker)

        let! initial = balancedReady.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
        initial.Advisory |> Option.map _.RecommendedNode |> Expect.equal "advises idle synced LAN worker one" (Some "one")

        let tExpired = tBase.AddSeconds 2.0
        Interlocked.Exchange(&graphNowTicks, tExpired.Ticks) |> ignore

        let! silentStatus = silentReady.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
        silentStatus.SilentAlerts |> Array.exists (fun a -> a.Kind = "silent_auditor" && a.AgentFamily = "pi")
        |> Expect.isTrue "one-shot timer pushed silent_auditor reactively"

        AgentActivityTracker.recordMemberActivity tracker impl "" None (Some "keep active") tExpired
        AgentActivityTracker.recordMemberActivity tracker verifier "" None (Some "audit resumed") tExpired
        let! cleared = clearedReady.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
        cleared.SilentAlerts |> Expect.isEmpty "silent_auditor cleared after verifier activity"

        graph.Stop()
        graph.Drained |> Expect.isTrue "graph stopped and drained cleanly"
      }
    }

    testCase "auditor silent while zero work is active immediately emits silent_auditor when a lease arrives without waiting for any timer (plus unrefreshed-clock fault control)" <| fun () ->
      let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 99 }
      let tStart = DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
      let mutable nowTicks = tStart.Ticks
      let utcNow () = DateTime(Interlocked.Read(&nowTicks), DateTimeKind.Utc)
      let tracker = AgentActivityTracker.create ()
      let verifier = MemberId.Mcp "ver-idle-start"
      let impl = MemberId.Mcp "impl-idle-start"
      AgentActivityTracker.recordMemberActivity tracker verifier "" None (Some "idle") tStart
      AgentActivityTracker.recordMemberActivity tracker impl "" None (Some "idle") tStart
      let frame =
        emptyFrame 1L [
          impl, JoinableRole.Implementer, SeatState.Present, tStart, "claude-code"
          verifier, JoinableRole.Verifier, SeatState.Present, tStart, "pi-auditor"
        ]
      let graph = Facts.Graph(constants, "inc-work-start", "j-inc", utcNow = utcNow, silenceThreshold = TimeSpan.FromSeconds 60.0)
      let mutable latestBalance : FleetBalanceStatus option = None
      let _, sub = graph.Register(fun kind ev ->
        match kind, ev with
        | UiBridge.Push.FleetBalance, FleetBalance st -> latestBalance <- Some st
        | _ -> ())
      use _sub = sub
      graph.Tick {
        HealthTick.Tick.Serial = 1L; Record = None; Telemetry = None
        Machine = { TotalBytes = 128L * 1_073_741_824L; AvailableBytes = 64L * 1_073_741_824L }
        Level = MemoryPressure.Normal; SampledAtMs = 100L; RecordAtMs = 0L } |> Expect.isOk "tick"
      graph.Pool { Serial = 1L; Active = []; Queue = [] } |> Expect.isOk "empty pool"
      graph.Membership frame |> Expect.isOk "membership"
      use _fleetSub = graph.StartFleetBalance(tracker.Changed, fun () -> AgentActivityTracker.snapshotWithVersion tracker)

      // Zero work active at tStart -> zero alerts.
      let beforeStatus = latestBalance |> Option.defaultWith (fun () -> failtest "expected initial FleetBalance")
      beforeStatus.SilentAlerts |> Expect.isEmpty "no alert when zero work is active"

      // Advance clock 10 minutes while zero work is active (no timer was armed), then a lease arrives!
      let tLater = tStart.AddMinutes 10.0
      Interlocked.Exchange(&nowTicks, tLater.Ticks) |> ignore
      let grant : ExpensiveWorkLease.GrantView =
        { Holder = "claude · /repo"; Kind = "full_build"
          GrantedAt = DateTimeOffset(tLater); ExpiresAt = DateTimeOffset(tLater.AddMinutes 10.0) }
      graph.Pool { Serial = 2L; Active = [ grant ]; Queue = [] } |> Expect.isOk "lease admitted"

      // Synchronous immediate push with NO timer wait!
      let afterStatus = latestBalance |> Option.defaultWith (fun () -> failtest "expected updated FleetBalance")
      let hasSilentAuditor (st: FleetBalanceStatus) =
        st.SilentAlerts |> Array.exists (fun a -> a.Kind = "silent_auditor" && a.MemberKey = MemberId.display verifier)
      hasSilentAuditor afterStatus |> Expect.isTrue "Event.FleetBalance immediately emitted silent_auditor when work started"
      graph.Stop()

      // Fault-injection negative control: an unrefreshed evaluation clock frozen at tStart misses silent_auditor!
      let staleFacts = FleetBalanceService.extractCohortWorkFacts (Some frame) None (AgentActivityTracker.snapshotWithVersion tracker |> snd)
      let staleSummary, staleWorkCtx =
        FleetBalanceService.summarizeAt
          tStart FleetBalance.Z13ThrottlePolicy.AdvisoryOnly (TimeSpan.FromSeconds 60.0) "z13"
          (Some MemoryPressure.Normal) { Serial = 2L; Active = [ grant ]; Queue = [] }
          { TotalBytes = 128L * 1_073_741_824L; AvailableBytes = 64L * 1_073_741_824L }
          (Some true) None staleFacts
      let staleWire = FleetBalanceService.toWireStatus 2L staleWorkCtx staleSummary
      hasSilentAuditor staleWire |> Expect.isFalse "fault control: unrefreshed clock tStart fails to emit silent_auditor"

    testCase "LAN-only build running on ONE while Z13 has 0 leases immediately triggers silent_auditor on UiBridge and clears when ONE disconnects" <| fun () ->
      let localSource : Facts.ObservationSource = { Node = "z13"; Incarnation = "z13-inc" }
      let peerOne : Facts.ObservationSource = { Node = "one"; Incarnation = "one-inc" }
      let producer : Facts.ObservationProducer = { Source = localSource; Correlation = Guid.NewGuid() }
      let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 99 }
      let tStart = DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
      let mutable nowTicks = tStart.Ticks
      let utcNow () = DateTime(Interlocked.Read(&nowTicks), DateTimeKind.Utc)
      let tracker = AgentActivityTracker.create ()
      let verifier = MemberId.Mcp "ver-lan-only"
      AgentActivityTracker.recordMemberActivity tracker verifier "" None (Some "idle") tStart
      let frame = emptyFrame 1L [ verifier, JoinableRole.Verifier, SeatState.Present, tStart, "pi-auditor" ]
      let graph =
        Facts.Graph(
          constants, "inc-lan", "j-inc",
          observationOwners = [ producer ], localNode = "z13", peerNodes = [ "one" ],
          utcNow = utcNow, silenceThreshold = TimeSpan.FromSeconds 60.0)
      let mutable latestBalance : FleetBalanceStatus option = None
      let _, sub = graph.Register(fun kind ev ->
        match kind, ev with
        | UiBridge.Push.FleetBalance, FleetBalance st -> latestBalance <- Some st
        | _ -> ())
      use _sub = sub
      graph.Tick {
        HealthTick.Tick.Serial = 1L; Record = None; Telemetry = None
        Machine = { TotalBytes = 128L * 1_073_741_824L; AvailableBytes = 64L * 1_073_741_824L }
        Level = MemoryPressure.Normal; SampledAtMs = 100L; RecordAtMs = 0L } |> Expect.isOk "tick"
      graph.Pool { Serial = 1L; Active = []; Queue = [] } |> Expect.isOk "z13 has 0 leases"
      graph.Membership frame |> Expect.isOk "membership"
      let peerProd = graph.BindPeer peerOne
      graph.PublishWork(peerProd, mkSnap 1L 0 (Some 4) "Normal" 96 (Some true)) |> Expect.isOk "one initially idle"
      use _fleetSub = graph.StartFleetBalance(tracker.Changed, fun () -> AgentActivityTracker.snapshotWithVersion tracker)

      (latestBalance |> Option.get).SilentAlerts |> Expect.isEmpty "idle fleet has 0 alerts"

      // Advance clock 5 minutes; a build starts ONLY on ONE (Z13 still has 0 leases)!
      Interlocked.Exchange(&nowTicks, tStart.AddMinutes(5.0).Ticks) |> ignore
      graph.PublishWork(peerProd, mkSnap 2L 1 (Some 4) "Normal" 96 (Some true)) |> Expect.isOk "LAN build on one"
      let lanAlert = latestBalance |> Option.get
      lanAlert.ActiveLeaseCount |> Expect.equal "fleet-wide active leases counts ONE's lease" 1
      lanAlert.SilentAlerts |> Array.exists (fun a -> a.Kind = "silent_auditor" && a.MemberKey = MemberId.display verifier)
      |> Expect.isTrue "LAN-only build on ONE triggers silent_auditor on UiBridge"

      // Disconnecting ONE clears its retained lease from fleet-wide active work!
      graph.PeerUnavailable(peerProd, "disconnected") |> Expect.isOk "peer disconnected"
      let afterDisconnect = latestBalance |> Option.get
      afterDisconnect.ActiveLeaseCount |> Expect.equal "unavailable peer retained lease does not count as active" 0
      afterDisconnect.SilentAlerts |> Expect.isEmpty "disconnecting only busy peer clears active-lease silence alert"
      graph.Stop()

    testCase "PublishActivity drops out-of-order stale version callbacks via serial monotonicity without regressing state" <| fun () ->
      let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 99 }
      let tNow = DateTime(2026, 11, 20, 12, 10, 0, DateTimeKind.Utc)
      let verifier = MemberId.Mcp "ver-order"
      let frame = emptyFrame 1L [ verifier, JoinableRole.Verifier, SeatState.Present, tNow.AddMinutes -10.0, "pi-auditor" ]
      let graph = Facts.Graph(constants, "inc-order", "j-inc", utcNow = (fun () -> tNow), silenceThreshold = TimeSpan.FromSeconds 60.0)
      try
        let mutable latest : FleetBalanceStatus option = None
        let _, sub = graph.Register(fun kind ev ->
          match kind, ev with
          | UiBridge.Push.FleetBalance, FleetBalance st -> latest <- Some st
          | _ -> ())
        use _sub = sub
        graph.Tick {
          HealthTick.Tick.Serial = 1L; Record = None; Telemetry = None
          Machine = { TotalBytes = 128L * 1_073_741_824L; AvailableBytes = 64L * 1_073_741_824L }
          Level = MemoryPressure.Normal; SampledAtMs = 100L; RecordAtMs = 0L } |> Expect.isOk "tick"
        graph.Pool { Serial = 1L; Active = [ { Holder = "h"; Kind = "full_build"; GrantedAt = DateTimeOffset(tNow); ExpiresAt = DateTimeOffset(tNow.AddMinutes 5.0) } ]; Queue = [] } |> Expect.isOk "pool"
        graph.Membership frame |> Expect.isOk "membership"
        let evt = Event<int64 * Map<string, DateTime>>()
        use _fleetSub = graph.StartFleetBalance(evt.Publish, fun () -> 0L, Map.ofList [ MemberId.display verifier, tNow.AddMinutes -10.0 ])
        (latest |> Option.get).SilentAlerts |> Array.exists (fun a -> a.Kind = "silent_auditor") |> Expect.isTrue "initially silent at v0"

        // Deliver v2 (fresh) BEFORE stale v1 (old): v2 clears alert, and late v1 must be dropped as Older!
        graph.PublishActivity(2L, Map.ofList [ MemberId.display verifier, tNow ]) |> Expect.isOk "v2 admitted"
        (latest |> Option.get).SilentAlerts |> Expect.isEmpty "v2 cleared silent_auditor"

        graph.PublishActivity(1L, Map.ofList [ MemberId.display verifier, tNow.AddMinutes -10.0 ])
        |> Expect.equal "out-of-order stale v1 dropped as Older" (Error Facts.Refusal.Older)
        (latest |> Option.get).SilentAlerts |> Expect.isEmpty "stale v1 did not regress state back to silent"
      finally
        graph.Stop()

    testTask "fault injection: backward clock step keeps silenceTimer armed and emits alert once clock advances" {
      do! task {
        let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 99 }
        let baseNow = DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc)
        let expires = baseNow.AddMilliseconds 60.0
        let firstWake = TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously)
        let mutable reads = 0
        let utcNow () =
          match Interlocked.Increment(&reads) with
          | 1 | 2 -> baseNow
          | 3 ->
            let steppedBack = baseNow.AddMilliseconds -150.0
            firstWake.TrySetResult steppedBack |> ignore
            steppedBack
          | _ -> expires.AddMilliseconds 10.0
        let graph = Facts.Graph(constants, "inc-back", "j-inc", utcNow = utcNow, silenceThreshold = TimeSpan.FromSeconds 1.0)
        let silentAlerted = TaskCompletionSource<FleetBalanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously)
        let verifier = MemberId.Mcp "ver-back"
        let actTime = baseNow.AddSeconds(-1.0).AddMilliseconds(60.0)
        let tracker = AgentActivityTracker.create ()
        AgentActivityTracker.recordMemberActivity tracker verifier "" None (Some "audit") actTime
        let frame =
          emptyFrame 1L [
            MemberId.Mcp "impl-back", JoinableRole.Implementer, SeatState.Present, baseNow, "codex"
            verifier, JoinableRole.Verifier, SeatState.Present, actTime, "pi-auditor"
          ]
        let _, sub = graph.Register(fun kind ev ->
          match kind, ev with
          | UiBridge.Push.FleetBalance, FleetBalance status when status.SilentAlerts |> Array.exists (fun a -> a.Kind = "silent_auditor") ->
            silentAlerted.TrySetResult status |> ignore
          | _ -> ())
        use _sub = sub
        graph.Tick {
          HealthTick.Tick.Serial = 1L; Record = None; Telemetry = None
          Machine = { TotalBytes = 128L * 1_073_741_824L; AvailableBytes = 64L * 1_073_741_824L }
          Level = MemoryPressure.Normal; SampledAtMs = 100L; RecordAtMs = 0L } |> Expect.isOk "tick admitted"
        graph.Pool { Serial = 1L; Active = [ { Holder = "impl · /r"; Kind = "full_build"; GrantedAt = DateTimeOffset(baseNow); ExpiresAt = DateTimeOffset(baseNow.AddMinutes 5.0) } ]; Queue = [] } |> Expect.isOk "pool admitted"
        graph.Membership frame |> Expect.isOk "membership admitted"
        use _fleetSub = graph.StartFleetBalance(tracker.Changed, fun () -> AgentActivityTracker.snapshotWithVersion tracker)

        let! steppedBack = firstWake.Task.WaitAsync(TimeSpan.FromSeconds 5.0)
        steppedBack |> Expect.equal "first timer wake observed backward wall-clock step" (baseNow.AddMilliseconds -150.0)
        let! alerted = silentAlerted.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
        alerted.SilentAlerts |> Array.exists (fun a -> a.Kind = "silent_auditor") |> Expect.isTrue "rearmed timer fires after backward step"
        graph.Stop()
      }
    }
  ]

let private startTestHttpServer (ownerOpt: CohortOwner.Handle option) (tracker: AgentActivityTracker.Tracker) = task {
  let builder = WebApplication.CreateBuilder([||])
  builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
  builder.Logging.ClearProviders() |> ignore
  let ops = SessionManagementOps.stub
  let config : McpServer.McpServerConfig =
    { DiagnosticsChanged = (Event<Bozzetto.Features.DiagnosticsStore.T>()).Publish
      StateChanged = None
      FrictionStore = None
      Port = 0
      Headless = true
      BindHost = BozzettoConfig.LoopbackHost.Localhost
      OwnOrigins = HttpOriginGuard.OwnOrigins.ofPorts [ 0 ]
      SessionOps = ops
      ElmRuntime = None
      GetWarmupContext = None
      SharedBindingScope = ref None
      SharedFeatureState = None
      ActivityTracker = tracker
      LiveSnapshotSink = None
      CohortOwner = ownerOpt
      GetDaemonHealth = fun () -> None
      Composer = None }
  let mcpCtx : McpTools.McpContext =
    { FrictionStore = None
      DiagnosticsChanged = config.DiagnosticsChanged
      StateChanged = None
      SessionOps = ops
      SessionMap = ConcurrentDictionary<string, string>()
      McpPort = 0
      Dispatch = None
      GetElmModel = None
      GetElmRegions = None
      GetWarmupContext = None
      GetFeatureState = None; RecordEval = None
      ActivityTracker = tracker
      LiveSnapshotSink = None
      CohortOwner = ownerOpt
      GetDaemonHealth = fun () -> None
      GetProcessTelemetry = fun () -> None }
  let sseCtx : McpServer.SseContext =
    { GetElmModel = None
      GetWarmupContext = None
      SseJsonOpts = JsonCasing.PascalCase
      TestEventBroadcast = Event<string>()
      SessionEventBroadcast = Event<string>()
      ServerTracker = McpServer.McpServerTracker()
      CohortOwner = ownerOpt }
  let rctx : McpServer.RouteContext =
    { Config = config
      McpContext = mcpCtx
      SseContext = sseCtx
      Dispatch = None
      GetElmRegions = None
      FsiBindings = ref Map.empty
      FeaturePushState = ref FeatureHooks.FeaturePushState.empty
      LastFeatureOutputCount = ref 0
      LastEvalContext = ref None }
  let app = builder.Build()
  McpServer.mapStatusRoutes app rctx
  McpServer.mapSessionRoutes app rctx
  do! app.StartAsync()
  let baseUrl =
    app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses
    |> Seq.head
  return app, mcpCtx, baseUrl
}

let private daemonIntegrationTests =
  testSequenced (testList "FleetBalance MCP, HTTP, atomic LeaseWatch, and service enforcement" [

    testTask "atomic LeaseWatch.requestWithPlacement prevents two concurrent holders from both grabbing Z13 under EnforceWhenLanIdle and leaves zero ghost queue entries" {
      do! task {
        LeaseWatch.reset ()
        MemoryPressureWatch.reset ()
        HealthTick.sample () |> ignore
        let localNode = "z13"
        use _nodeReg = FleetBalanceService.installLocalNodeId localNode
        let peerBoard : WorkBoard = {
          Revision = 1L
          Nodes = [|
            { Source = { Node = localNode; Incarnation = "local-inc" }; Local = true; Status = "available"; Note = ""
              Value = Some(mkSnap 1L 0 (Some 4) "Normal" 64 (Some true)) }
            { Source = { Node = "one"; Incarnation = "one-inc" }; Local = false; Status = "available"; Note = ""
              Value = Some(mkSnap 1L 0 (Some 4) "Normal" 96 (Some true)) }
          |]
        }
        use _boardReg = FleetBalanceService.installWorkBoardReader (fun () -> Some peerBoard)
        try
          // 1. Sequential atomic check: first holder acquires Z13 when idle, second sees Z13 busy atomically and is throttled with 0 ghost queue entries.
          let r1 =
            FleetBalanceService.evaluateAndAcquireWorkLeaseWithPolicy
              FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle localNode None "holder-a · /repo" ExpensiveWorkLease.Kind.FullBuild false
            |> parseJsonObj
          let r2 =
            FleetBalanceService.evaluateAndAcquireWorkLeaseWithPolicy
              FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle localNode None "holder-b · /repo" ExpensiveWorkLease.Kind.FullBuild false
            |> parseJsonObj
          r1["decision"] |> Expect.equal "first holder granted when Z13 idle" (JsonValue.String "granted")
          r2["decision"] |> Expect.equal "second holder refused atomically" (JsonValue.String "refused")
          r2["throttled"] |> Expect.equal "second holder throttled=true" (JsonValue.Bool true)
          (jsonObj r2["rebalance_advisory"])["recommended_node"] |> Expect.equal "advisory recommends one" (JsonValue.String "one")
          let snap1 = LeaseWatch.snapshot ()
          snap1.ActiveCount |> Expect.equal "exactly 1 active Z13 lease" 1
          snap1.QueueDepth |> Expect.equal "0 ghost queue entries after throttle" 0

          // 2. Concurrent contention: reset and race two distinct holders simultaneously.
          LeaseWatch.reset ()
          let gate = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
          let runHolder h =
            Task.Run<Map<string, JsonValue>>(fun () -> task {
              do! gate.Task
              return
                FleetBalanceService.evaluateAndAcquireWorkLeaseWithPolicy
                  FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle localNode None h ExpensiveWorkLease.Kind.FullBuild false
                |> parseJsonObj
            })
          let t1 = runHolder "race-holder-1 · /repo"
          let t2 = runHolder "race-holder-2 · /repo"
          gate.TrySetResult(()) |> ignore
          let! results = Task.WhenAll([| t1; t2 |])
          let grantedCount = results |> Array.filter (fun (m: Map<string, JsonValue>) -> m["decision"] = JsonValue.String "granted") |> Array.length
          let throttledCount = results |> Array.filter (fun (m: Map<string, JsonValue>) -> m["decision"] = JsonValue.String "refused" && m["throttled"] = JsonValue.Bool true) |> Array.length
          grantedCount |> Expect.equal "exactly one concurrent holder granted" 1
          throttledCount |> Expect.equal "exactly one concurrent holder throttled" 1
          let snap2 = LeaseWatch.snapshot ()
          snap2.ActiveCount |> Expect.equal "only 1 Z13 lease granted under race" 1
          snap2.QueueDepth |> Expect.equal "0 ghost queue entries under race" 0

          // 3. Pre-queued stale ask is withdrawn atomically if a retry is throttled under EnforceWhenLanIdle.
          LeaseWatch.reset ()
          let tightStats : MachineMemory.Stats = { TotalBytes = 100L * 1_073_741_824L; AvailableBytes = 15L * 1_073_741_824L }
          MemoryPressureWatch.observe MemorySupervisor.defaultThresholds tightStats |> ignore
          LeaseWatch.request "busy-holder · /repo" ExpensiveWorkLease.Kind.FullBuild |> ignore
          match LeaseWatch.request "queued-holder · /repo" ExpensiveWorkLease.Kind.FullBuild with
          | ExpensiveWorkLease.Decision.Wait _ -> ()
          | other -> failtestf "expected queued Wait under Tight pressure, got %A" other
          (LeaseWatch.snapshot ()).QueueDepth |> Expect.equal "seeded 1 queued ask" 1
          let throttledRetry =
            FleetBalanceService.evaluateAndAcquireWorkLeaseWithPolicy
              FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle localNode None "queued-holder · /repo" ExpensiveWorkLease.Kind.FullBuild false
            |> parseJsonObj
          throttledRetry["throttled"] |> Expect.equal "queued holder throttled" (JsonValue.Bool true)
          (LeaseWatch.snapshot ()).QueueDepth |> Expect.equal "stale queued ask withdrawn on throttle" 0
        finally
          LeaseWatch.reset ()
          MemoryPressureWatch.reset ()
      }
    }

    testTask "LeaseWatch queued-ask expiry timer and published-snapshot diff transition Z13 from busy back to idle" {
      do! task {
        LeaseWatch.reset ()
        MemoryPressureWatch.reset ()
        let now = DateTimeOffset.UtcNow
        let expQ = now.AddMilliseconds 80.0
        let queueOnlyPool : ExpensiveWorkLease.PoolState =
          { Active = []
            Queue = [
              { Holder = "ghost · /repo"
                Kind = ExpensiveWorkLease.Kind.FullBuild
                Seq = 1L
                FirstAskedAt = now.AddMinutes -2.0
                LastAskedAt = now.AddMinutes -2.0
                ExpiresAt = expQ }
            ]
            NextSeq = 2L }
        LeaseWatch.nextPoolExpiry now queueOnlyPool
        |> Expect.equal "nextPoolExpiry includes queued ask expiry" (Some expQ)

        let constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 99 }
        let tracker = AgentActivityTracker.create ()
        let graph = Facts.Graph(constants, "inc-qexp", "j-qexp")
        let idleReached = TaskCompletionSource<FleetBalanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously)
        let busySeen = TaskCompletionSource<FleetBalanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously)
        let _, sub = graph.Register(fun kind ev ->
          match kind, ev with
          | UiBridge.Push.FleetBalance, FleetBalance st ->
            if st.LocalBusy && not busySeen.Task.IsCompleted then busySeen.TrySetResult st |> ignore
            elif busySeen.Task.IsCompleted && not st.LocalBusy && not idleReached.Task.IsCompleted then
              idleReached.TrySetResult st |> ignore
          | _ -> ())
        use _sub = sub
        use _poolSub = LeaseWatch.changed.Subscribe(fun p -> graph.Pool p |> ignore)
        use _fleetSub = graph.StartFleetBalance(tracker.Changed, fun () -> AgentActivityTracker.snapshotWithVersion tracker)
        try
          graph.Tick {
            HealthTick.Tick.Serial = 1L; Record = None; Telemetry = None
            Machine = { TotalBytes = 128L * 1_073_741_824L; AvailableBytes = 64L * 1_073_741_824L }
            Level = MemoryPressure.Normal; SampledAtMs = 100L; RecordAtMs = 0L } |> Expect.isOk "tick"

          LeaseWatch.seedPoolForTest queueOnlyPool
          let! busySt = busySeen.Task.WaitAsync(TimeSpan.FromSeconds 5.0)
          busySt.LocalBusy |> Expect.isTrue "seeded queued ask marks Z13 busy"

          let! idleSt = idleReached.Task.WaitAsync(TimeSpan.FromSeconds 5.0)
          idleSt.LocalBusy |> Expect.isFalse "expired queued ask reaped by timer transitions Z13 back to idle"
          (LeaseWatch.current ()).Queue |> Expect.isEmpty "published pool queue is empty after expiry reap"

          // Also verify published-snapshot diff catches already-expired queue entry on update even when live snapshot before/after is empty.
          let pastExp = DateTimeOffset.UtcNow.AddSeconds -1.0
          let stalePool : ExpensiveWorkLease.PoolState =
            { Active = []
              Queue = [
                { Holder = "stale-ghost · /repo"
                  Kind = ExpensiveWorkLease.Kind.FullBuild
                  Seq = 2L
                  FirstAskedAt = pastExp.AddMinutes -2.0
                  LastAskedAt = pastExp.AddMinutes -2.0
                  ExpiresAt = pastExp }
              ]
              NextSeq = 3L }
          let publishedAfterUpdate = TaskCompletionSource<LeaseWatch.Pool>(TaskCreationOptions.RunContinuationsAsynchronously)
          use _sub2 = LeaseWatch.changed.Subscribe(fun p ->
            if p.Queue.IsEmpty && p.Active.IsEmpty then publishedAfterUpdate.TrySetResult p |> ignore)
          LeaseWatch.seedPoolForTest stalePool
          LeaseWatch.withdraw "unrelated" ExpensiveWorkLease.Kind.FullBuild |> ignore
          let! p = publishedAfterUpdate.Task.WaitAsync(TimeSpan.FromSeconds 5.0)
          p.Queue |> Expect.isEmpty "published-snapshot diff against latest emits empty queue after expiry"
        finally
          graph.Stop()
          LeaseWatch.reset ()
          MemoryPressureWatch.reset ()
      }
    }

    testTask "positive, negative, LAN-only, project-bound audit, protocol v14, and Z13-pin controls across POST /api/lease/request, WorkLease.acquire, /api/system/status, get_daemon_status, and get_agent_work" {
      do! withEnvVar "BOZZETTO_Z13_THROTTLE" (Some "enforce_when_lan_idle") (fun () ->
        withEnvVar "BOZZETTO_SILENT_AGENT_SECONDS" (Some "60") (fun () -> task {
          LeaseWatch.reset ()
          MemoryPressureWatch.reset ()
          HealthTick.sample () |> ignore
          let tracker = AgentActivityTracker.create ()
          let ledger = CohortLedger.InMemory.create<MemberId> ()
          let impl = MemberId.Mcp "impl-http"
          let verActive = MemberId.Mcp "verifier-active-http"
          let verSilent = MemberId.Mcp "verifier-silent-http"
          let mutable now = DateTime.UtcNow.AddMinutes -10.0
          use owner = CohortOwner.start silentLogger ledger (fun () -> now) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
          let! r1 = owner.Commit(CohortCommand.JoinNamed(impl, JoinableRole.Implementer, None, "antigravity"))
          r1 |> Expect.isOk "impl joined"
          let! r2 = owner.Commit(CohortCommand.JoinNamed(verActive, JoinableRole.Verifier, None, "pi-active"))
          r2 |> Expect.isOk "active verifier joined"
          let! r3 = owner.Commit(CohortCommand.JoinNamed(verSilent, JoinableRole.Verifier, None, "pi-silent"))
          r3 |> Expect.isOk "silent verifier joined"
          // Report a completed non-verifier run (NO claims, NO local Z13 leases yet).
          let! repRes = owner.ReportWork(impl, mkWorkReport "impl-run-1" "antigravity" "/repo/Bozzetto" "completed" 1L) |> Async.StartAsTask
          repRes |> Expect.isOk "implementer work reported"
          AgentActivityTracker.recordMemberActivity tracker impl "" None (Some "active") DateTime.UtcNow
          AgentActivityTracker.recordMemberActivity tracker verActive "" None (Some "active") DateTime.UtcNow
          AgentActivityTracker.recordMemberActivity tracker verSilent "" None (Some "silent") (DateTime.UtcNow.AddMinutes -10.0)

          let localNode = "workstation"
          use _nodeReg = FleetBalanceService.installLocalNodeId localNode
          // Seed LAN-only active lease on ONE while Z13 has 0 leases; TWO is unsynced (empty note!);
          // THREE is v13/unknown sync (even with note "synced"!); FOUR is disconnected with retained cap=4 snapshot.
          let lanOnlyBoard : WorkBoard = {
            Revision = 1L
            Nodes = [|
              { Source = { Node = localNode; Incarnation = "local-inc" }; Local = true; Status = "available"; Note = ""
                Value = Some(mkSnap 1L 0 (Some 4) "Normal" 64 (Some true)) }
              { Source = { Node = "one"; Incarnation = "one-inc" }; Local = false; Status = "available"; Note = ""
                Value = Some(mkSnap 1L 1 (Some 4) "Normal" 96 (Some true)) }
              { Source = { Node = "two"; Incarnation = "two-inc" }; Local = false; Status = "available"; Note = ""
                Value = Some(mkSnap 1L 0 (Some 4) "Normal" 96 (Some false)) }
              { Source = { Node = "three"; Incarnation = "three-inc" }; Local = false; Status = "available"; Note = "synced"
                Value = Some(mkSnap 1L 0 (Some 4) "Normal" 96 None) }
              { Source = { Node = "four"; Incarnation = "four-inc" }; Local = false; Status = "unavailable"; Note = "disconnected"
                Value = Some(mkSnap 1L 0 (Some 4) "Normal" 96 (Some true)) }
            |]
          }
          let mutable currentBoard = lanOnlyBoard
          let mutable boardReads = 0
          use _boardReg = FleetBalanceService.installWorkBoardReader (fun () ->
            Interlocked.Increment(&boardReads) |> ignore
            Some currentBoard)
          use _cohortReg = FleetBalanceService.installCohortReaders (Some owner) tracker
          let! app, mcpCtx, baseUrl = startTestHttpServer (Some owner) tracker
          let! outcome = task {
            try
              use http = new HttpClient(BaseAddress = Uri baseUrl)
              WorkLease.ProtocolVersion |> Expect.equal "WorkLease.ProtocolVersion matches ProtocolVersion" ProtocolVersion

              // 0a. Valid protocolVersion=14 with missing holder returns HTTP 400 carrying protocolVersion=14 (not misclassified as protocol_mismatch).
              let! missingHolderResp =
                http.PostAsync("/api/lease/request", new StringContent("""{"protocolVersion":14,"kind":"full_build"}""", Encoding.UTF8, "application/json"))
              missingHolderResp.StatusCode |> Expect.equal "missing holder returns HTTP 400" HttpStatusCode.BadRequest
              let! missingHolderBody = missingHolderResp.Content.ReadAsStringAsync()
              let missingHolderObj = parseJsonObj missingHolderBody
              missingHolderObj["protocolVersion"] |> Expect.equal "validation 400 retains protocolVersion=14" (JsonValue.ofInt64 14L)
              (match missingHolderObj["error"] with JsonValue.String s -> s | _ -> "")
              |> Expect.stringContains "validation 400 reports missing holder, not protocol_mismatch" "missing 'holder'"

              // 0. Exact-reason negative controls on POST /api/lease/request protocolVersion before lease admission.
              for label, badPayload in [
                "missing", """{"holder":"skewed","kind":"full_build"}"""
                "v11", """{"protocolVersion":11,"holder":"skewed","kind":"full_build"}"""
                "string", """{"protocolVersion":"14","holder":"skewed","kind":"full_build"}"""
                "float", """{"protocolVersion":14.5,"holder":"skewed","kind":"full_build"}"""
              ] do
                let! badVerResp =
                  http.PostAsync("/api/lease/request", new StringContent(badPayload, Encoding.UTF8, "application/json"))
                badVerResp.StatusCode |> Expect.equal (sprintf "%s protocolVersion returns HTTP 400" label) HttpStatusCode.BadRequest
                let! badVerBody = badVerResp.Content.ReadAsStringAsync()
                let badVerObj = parseJsonObj badVerBody
                badVerObj["success"] |> Expect.equal "protocol mismatch success=false" (JsonValue.Bool false)
                badVerObj["error"] |> Expect.equal "protocol mismatch error token" (JsonValue.String "protocol_mismatch")
                badVerObj["expectedProtocolVersion"] |> Expect.equal "expectedProtocolVersion=14" (JsonValue.ofInt64 14L)
                (LeaseWatch.current ()).Active.Length |> Expect.equal "protocol_mismatch never grants lease" 0

              // 1. Verifier calling get_agent_work does NOT clear unaudited_runs or suppress silent_auditor,
              //    and LAN-only active lease on ONE counts in fleet-wide work.active_leases = 1!
              let prevTransport = McpTools.currentTransportSessionId.Value
              McpTools.currentTransportSessionId.Value <- Some "verifier-active-http"
              let! workRes1 = McpCohortIntegration.getAgentWork mcpCtx
              McpTools.currentTransportSessionId.Value <- prevTransport
              match workRes1 with
              | Ok workRaw ->
                let workRoot = parseJsonObj workRaw
                let fb = jsonObj workRoot["fleetBalance"]
                let workObj = jsonObj fb["work"]
                workObj["active_leases"] |> Expect.equal "LAN-only lease on ONE counted in get_agent_work" (JsonValue.ofInt64 1L)
                workObj["unaudited_runs"] |> Expect.equal "verifier get_agent_work call does NOT clear unaudited_runs" (JsonValue.ofInt64 1L)
                let alerts = match fb["silent_alerts"] with JsonValue.Array xs -> xs | _ -> []
                alerts |> List.exists (fun item ->
                  let m = jsonObj item
                  m["kind"] = JsonValue.String "silent_auditor" && m["member_id"] = JsonValue.String (MemberId.display verSilent))
                |> Expect.isTrue "silent verifier alerted on get_agent_work despite other verifier reading work"
              | Error err -> failtestf "get_agent_work failed: %A" err

              // 2. Verifier reporting an audit run on the SAME normalized project clears unaudited_runs to 0,
              //    while LAN-only lease on ONE STILL triggers silent_auditor on get_daemon_status!
              now <- DateTime.UtcNow
              let! auditRep = owner.ReportWork(verActive, mkWorkReport "ver-audit-1" "pi-active" "/repo/Bozzetto/" "completed" 1L) |> Async.StartAsTask
              auditRep |> Expect.isOk "verifier audit run reported"
              let! daemonStatusRaw = McpTools.getDaemonStatus mcpCtx
              let daemonRoot = parseJsonObj daemonStatusRaw
              let daemonFb = jsonObj daemonRoot["fleetBalance"]
              let daemonWork = jsonObj daemonFb["work"]
              daemonWork["unaudited_runs"] |> Expect.equal "same-project verifier audit run clears unaudited_runs" (JsonValue.ofInt64 0L)
              daemonWork["active_leases"] |> Expect.equal "LAN-only lease on ONE still counted on get_daemon_status" (JsonValue.ofInt64 1L)
              let daemonAlerts = match daemonFb["silent_alerts"] with JsonValue.Array xs -> xs | _ -> []
              daemonAlerts |> List.exists (fun item -> (jsonObj item)["kind"] = JsonValue.String "silent_auditor")
              |> Expect.isTrue "LAN-only build on ONE triggers silent_auditor on get_daemon_status"

              // 3. Seed a busy Z13 lease -> /api/system/status recommends ONLY synced ONE (excludes unsynced TWO, unknown-sync THREE, disconnected FOUR).
              match LeaseWatch.request "existing-builder · /repo" ExpensiveWorkLease.Kind.FullBuild with
              | ExpensiveWorkLease.Decision.Granted _ -> ()
              | other -> failtestf "expected initial Z13 lease grant, got %A" other

              let! sysResp = http.GetAsync("/api/system/status")
              sysResp.StatusCode |> Expect.equal "/api/system/status 200" HttpStatusCode.OK
              let! sysBody = sysResp.Content.ReadAsStringAsync()
              let sysRoot = parseJsonObj sysBody
              let fbMap = jsonObj sysRoot["fleetBalance"]
              fbMap["recommended_node"] |> Expect.equal "recommends ONLY synced one (excludes unsynced two, unknown three, disconnected four)" (JsonValue.String "one")
              let nodesArr =
                match fbMap["nodes"] with
                | JsonValue.Array items -> items |> List.map jsonObj
                | other -> failtestf "expected nodes array, got %A" other
              let fourJson = nodesArr |> List.find (fun m -> m["node_id"] = JsonValue.String "four")
              fourJson["available"] |> Expect.equal "disconnected four has available=false" (JsonValue.Bool false)
              fourJson["checkout_sync"] |> Expect.equal "disconnected four has checkout_sync=unknown" (JsonValue.String "unknown")
              fourJson["free_slots"] |> Expect.equal "disconnected four never advertises free_slots: 4" JsonValue.Null

              // 4. POST /api/lease/request with protocolVersion=14 without pin is throttled before granting a second Z13 lease and echoes protocolVersion=14.
              let! throttledResp =
                http.PostAsync("/api/lease/request", new StringContent("""{"protocolVersion":14,"holder":"codex","kind":"full_build"}""", Encoding.UTF8, "application/json"))
              throttledResp.StatusCode |> Expect.equal "throttled response 200" HttpStatusCode.OK
              let! throttledBody = throttledResp.Content.ReadAsStringAsync()
              let throttledObj = parseJsonObj throttledBody
              throttledObj["protocolVersion"] |> Expect.equal "throttled reply carries protocolVersion=14" (JsonValue.ofInt64 14L)
              throttledObj["decision"] |> Expect.equal "decision refused on EnforceWhenLanIdle" (JsonValue.String "refused")
              throttledObj["throttled"] |> Expect.equal "throttled=true" (JsonValue.Bool true)
              (jsonObj throttledObj["rebalance_advisory"])["recommended_node"] |> Expect.equal "advisory names one" (JsonValue.String "one")
              (LeaseWatch.current ()).Active.Length |> Expect.equal "throttled lease never consumed a Z13 slot" 1

              // 5. Invalid pinned_to_z13 returns HTTP 400 exact reason (negative control).
              let! badPinResp =
                http.PostAsync("/api/lease/request", new StringContent("""{"protocolVersion":14,"holder":"codex","kind":"full_build","pinned_to_z13":"yes"}""", Encoding.UTF8, "application/json"))
              badPinResp.StatusCode |> Expect.equal "invalid pinned_to_z13 returns 400" HttpStatusCode.BadRequest
              let! badPinBody = badPinResp.Content.ReadAsStringAsync()
              badPinBody |> Expect.stringContains "exact boolean error" "pinned_to_z13"

              // 6. pinned_to_z13 = true bypasses throttle, grants lease, and carries protocolVersion=14.
              let! pinnedResp =
                http.PostAsync("/api/lease/request", new StringContent("""{"protocolVersion":14,"holder":"codex","kind":"full_build","pinned_to_z13":true}""", Encoding.UTF8, "application/json"))
              pinnedResp.StatusCode |> Expect.equal "pinned request 200" HttpStatusCode.OK
              let! pinnedBody = pinnedResp.Content.ReadAsStringAsync()
              let pinnedObj = parseJsonObj pinnedBody
              pinnedObj["protocolVersion"] |> Expect.equal "granted reply carries protocolVersion=14" (JsonValue.ofInt64 14L)
              pinnedObj["decision"] |> Expect.equal "pinned bypasses throttle" (JsonValue.String "granted")
              pinnedObj["pinned_to_z13"] |> Expect.equal "echoes pinned_to_z13=true" (JsonValue.Bool true)
              (LeaseWatch.current ()).Active.Length |> Expect.equal "granted second slot when pinned" 2

              // 6b. Refused duplicate-holder reply and Wait reply also carry protocolVersion=14.
              let! dupRefusedResp =
                http.PostAsync("/api/lease/request", new StringContent("""{"protocolVersion":14,"holder":"codex","kind":"full_build","pinned_to_z13":true}""", Encoding.UTF8, "application/json"))
              dupRefusedResp.StatusCode |> Expect.equal "duplicate holder refused 200" HttpStatusCode.OK
              let! dupRefusedBody = dupRefusedResp.Content.ReadAsStringAsync()
              let dupRefusedObj = parseJsonObj dupRefusedBody
              dupRefusedObj["protocolVersion"] |> Expect.equal "refused reply carries protocolVersion=14" (JsonValue.ofInt64 14L)
              dupRefusedObj["decision"] |> Expect.equal "duplicate holder refused" (JsonValue.String "refused")

              // 6c. WorkLease.acquireWithPinAt succeeds against v14 server and refuses replies lacking protocolVersion=14.
              let leaseEndpoint = baseUrl.TrimEnd('/') + "/api/lease"
              let! cliWaitOrGrant = WorkLease.acquireWithPinAt leaseEndpoint true "full_build" "worklease-v14-ok" |> Async.StartAsTask
              match cliWaitOrGrant with
              | WorkLease.Admission.Granted(lid, _) ->
                let! _ =
                  http.PostAsync("/api/lease/release", new StringContent(sprintf """{"leaseId":"%s"}""" lid, Encoding.UTF8, "application/json"))
                ()
              | other -> failtestf "expected WorkLease.acquireWithPinAt to grant against v14 server, got %A" other

              // Saturate remaining local capacity (Normal cap = 4, Tight = 1, Critical = 0) so the next pinned ask returns "wait" with protocolVersion=14.
              let fillerLeases = ResizeArray<string>()
              let mutable sawWait = false
              let mutable idx = 1
              while not sawWait && idx <= 5 do
                let holder = sprintf "slot-fill-%d" idx
                let reqJson = sprintf """{"protocolVersion":14,"holder":"%s","kind":"full_build","pinned_to_z13":true}""" holder
                let! r = http.PostAsync("/api/lease/request", new StringContent(reqJson, Encoding.UTF8, "application/json"))
                let! b = r.Content.ReadAsStringAsync()
                let obj = parseJsonObj b
                obj["protocolVersion"] |> Expect.equal (sprintf "%s reply carries protocolVersion=14" holder) (JsonValue.ofInt64 14L)
                match obj["decision"] with
                | JsonValue.String "granted" ->
                  match obj["leaseId"] with
                  | JsonValue.String lid -> fillerLeases.Add lid
                  | other -> failtestf "expected leaseId on %s grant, got %A" holder other
                | JsonValue.String "wait" ->
                  sawWait <- true
                  let! _ =
                    http.PostAsync("/api/lease/withdraw", new StringContent(sprintf """{"holder":"%s","kind":"full_build"}""" holder, Encoding.UTF8, "application/json"))
                  ()
                | other -> failtestf "expected granted or wait for %s, got %A" holder other
                idx <- idx + 1
              sawWait |> Expect.isTrue "full pool triggers wait with protocolVersion=14"
              for lid in fillerLeases do
                let! _ =
                  http.PostAsync("/api/lease/release", new StringContent(sprintf """{"leaseId":"%s"}""" lid, Encoding.UTF8, "application/json"))
                ()

              // 6d. Positive & negative WorkLease client verification against unversioned/skewed daemon reply.
              let skewBuilder = WebApplication.CreateBuilder([||])
              skewBuilder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
              skewBuilder.Logging.ClearProviders() |> ignore
              let skewApp = skewBuilder.Build()
              let mutable replyMode = "missing"
              let releasedSkewLeases = System.Collections.Concurrent.ConcurrentQueue<string>()
              skewApp.MapPost("/api/lease/request", Microsoft.AspNetCore.Http.RequestDelegate(fun ctx -> task {
                ctx.Response.ContentType <- "application/json"
                let expires = DateTimeOffset.UtcNow.AddMinutes(5.0).ToString("O")
                let payload =
                  match replyMode with
                  | "missing" -> sprintf """{"success":true,"decision":"granted","leaseId":"bad-1","expiresAt":"%s"}""" expires
                  | _ -> sprintf """{"success":true,"protocolVersion":11,"decision":"granted","leaseId":"bad-2","expiresAt":"%s"}""" expires
                let bytes = Encoding.UTF8.GetBytes payload
                do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
              })) |> ignore
              skewApp.MapPost("/api/lease/release", Microsoft.AspNetCore.Http.RequestDelegate(fun ctx -> task {
                use reader = new StreamReader(ctx.Request.Body)
                let! raw = reader.ReadToEndAsync()
                match Json.parse raw with
                | Ok(JsonValue.Object fields) ->
                  match fields |> List.tryPick (fun (k, v) -> if k = "leaseId" then JsonValue.asString v else None) with
                  | Some lid -> releasedSkewLeases.Enqueue lid
                  | None -> ()
                | _ -> ()
                ctx.Response.ContentType <- "application/json"
                let bytes = Encoding.UTF8.GetBytes """{"success":true}"""
                do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
              })) |> ignore
              do! skewApp.StartAsync()
              let! skewOutcome = task {
                try
                  let skewUrl =
                    (skewApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses |> Seq.head).TrimEnd('/')
                    + "/api/lease"
                  for mode in [ "missing"; "v11" ] do
                    replyMode <- mode
                    let! refusedCli = WorkLease.acquireWithPinAt skewUrl false "full_build" "cli-skew" |> Async.StartAsTask
                    match refusedCli with
                    | WorkLease.Admission.Refused reason ->
                      reason |> Expect.equal (sprintf "WorkLease refuses %s daemon reply" mode)
                        "protocol_mismatch: daemon protocol version does not match CLI v14 — update the daemon"
                    | other -> failtestf "expected WorkLease.Refused for %s, got %A" mode other
                  releasedSkewLeases.ToArray()
                  |> Expect.equal "WorkLease best-effort releases skewed pre-v14 granted leases before refusing" [| "bad-1"; "bad-2" |]
                  let mutable workInvoked = false
                  let skewClient : WorkLease.Client =
                    { Acquire = WorkLease.acquireWithPinAt skewUrl false
                      Renew = WorkLease.renew
                      Release = WorkLease.release
                      Withdraw = WorkLease.withdraw
                      Delay = fun _ _ -> async.Return()
                      Now = fun () -> DateTimeOffset.UtcNow }
                  let! runOutcome =
                    WorkLease.runWith skewClient "full_build" "cli-skew" CancellationToken.None (fun _ _ -> async {
                      workInvoked <- true
                      return Ok()
                    }) |> Async.StartAsTask
                  workInvoked |> Expect.isFalse "skewed daemon reply refuses BEFORE running child work"
                  match runOutcome with
                  | Error err -> err |> Expect.stringContains "runWith surfaces protocol_mismatch" "protocol_mismatch"
                  | Ok() -> failtest "expected Error from WorkLease.runWith on skewed daemon reply"
                  return Ok()
                with ex -> return Error ex
              }
              do! skewApp.StopAsync()
              do! (skewApp :> IAsyncDisposable).DisposeAsync().AsTask()
              match skewOutcome with
              | Ok() -> ()
              | Error ex -> raise ex

              // 7. Switching policy to AdvisoryOnly grants non-pinned request for distinct holder while still attaching rebalance_advisory.
              Environment.SetEnvironmentVariable("BOZZETTO_Z13_THROTTLE", "advisory_only")
              let advMcpRaw = McpTools.acquireWorkLease mcpCtx.ActivityTracker "antigravity" ExpensiveWorkLease.Kind.TestSuiteRun
              let advMcpObj = parseJsonObj advMcpRaw
              advMcpObj["decision"] |> Expect.equal "AdvisoryOnly grants" (JsonValue.String "granted")
              advMcpObj["throttled"] |> Expect.equal "AdvisoryOnly throttled=false" (JsonValue.Bool false)
              (jsonObj advMcpObj["rebalance_advisory"])["recommended_node"] |> Expect.equal "AdvisoryOnly still recommends one" (JsonValue.String "one")

              currentBoard <- lanOnlyBoard
              return Ok()
            with ex -> return Error ex
          }
          do! app.StopAsync()
          do! (app :> IAsyncDisposable).DisposeAsync().AsTask()
          LeaseWatch.reset ()
          MemoryPressureWatch.reset ()
          match outcome with
          | Ok() -> ()
          | Error ex -> raise ex
        }))
    }
  ])

[<Tests>]
let tests =
  testList "Fleet balance" [
    pureAndSimTests
    reactiveGraphTests
    daemonIntegrationTests
  ]
