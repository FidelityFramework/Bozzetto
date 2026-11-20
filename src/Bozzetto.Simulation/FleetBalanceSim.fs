namespace Bozzetto.Simulation

open System
open Bozzetto

// Pure functional Zone A deterministic state-machine simulation for FleetBalance
// placement/throttling and cohort silent-agent/auditor transitions across multi-node topologies.
module FleetBalanceSim =

  [<RequireQualifiedAccess>]
  type FaultMode =
    | NoFault
    | RecommendUnsyncedOrLocal
    | SuppressThrottleWhenLanIdle
    | SuppressSilentAuditorAlert

  type PlacementRecord = {
    Policy: FleetBalance.Z13ThrottlePolicy
    LocalNodeId: string
    Kind: ExpensiveWorkLease.Kind
    PinnedToLocal: bool
    Nodes: FleetBalance.NodeBalanceInput list
    Decision: FleetBalance.PlacementDecision
  }

  type SilenceRecord = {
    Now: DateTime
    Threshold: TimeSpan
    Members: FleetBalance.CohortMemberHeartbeat list
    Work: FleetBalance.ActiveWorkContext
    Evaluation: FleetBalance.SilentAgentEvaluation
  }

  type Trace = {
    Seed: int
    Fault: FaultMode
    PlacementSteps: PlacementRecord list
    SilenceSteps: SilenceRecord list
  }

  type private RngState = { State: uint64 }

  let private rngInit (seed: int) : RngState =
    let mixed = (uint64 (int64 seed) ^^^ 0x9E3779B97F4A7C15UL) * 6364136223846793005UL + 1442695040888963407UL
    { State = if mixed = 0UL then 1UL else mixed }

  let private rngNextU32 (rng: RngState) : uint32 * RngState =
    let next = rng.State * 6364136223846793005UL + 1442695040888963407UL
    uint32 (next >>> 32), { State = next }

  let private rngRange (minInclusive: int) (maxExclusive: int) (rng: RngState) : int * RngState =
    let span = max 1 (maxExclusive - minInclusive)
    let raw, nextRng = rngNextU32 rng
    minInclusive + int (raw % uint32 span), nextRng

  let private rngPick (items: 'a list) (rng: RngState) : 'a * RngState =
    let idx, nextRng = rngRange 0 items.Length rng
    List.item idx items, nextRng

  let private memberRoles = [
    "pi-auditor", "pi", Cohort.JoinableRole.Verifier
    "claude-impl", "claude-code", Cohort.JoinableRole.Implementer
    "codex-impl", "codex", Cohort.JoinableRole.Implementer
    "antigravity-lead", "antigravity", Cohort.JoinableRole.Observer
  ]

  let private pressures = [
    MemoryPressure.Normal
    MemoryPressure.Normal
    MemoryPressure.Tight
    MemoryPressure.Critical
  ]

  let private syncStates = [
    FleetBalance.CheckoutSyncStatus.Synced
    FleetBalance.CheckoutSyncStatus.Synced
    FleetBalance.CheckoutSyncStatus.Unsynced
    FleetBalance.CheckoutSyncStatus.Unknown
  ]

  let private leaseKinds = [
    ExpensiveWorkLease.Kind.FullBuild
    ExpensiveWorkLease.Kind.TestSuiteRun
    ExpensiveWorkLease.Kind.Rebuild
    ExpensiveWorkLease.Kind.SessionCreateOrWarmup
    ExpensiveWorkLease.Kind.RunApp
  ]

  let private policies = [
    FleetBalance.Z13ThrottlePolicy.Off
    FleetBalance.Z13ThrottlePolicy.AdvisoryOnly
    FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle
  ]

  let private nodeNames = [ "z13"; "one"; "two"; "three" ]

  let private injectPlacementFault (fault: FaultMode) (localNodeId: string) (nodes: FleetBalance.NodeBalanceInput list) (decision: FleetBalance.PlacementDecision) =
    match fault, decision with
    | FaultMode.RecommendUnsyncedOrLocal, FleetBalance.PlacementDecision.Proceed(Some adv) ->
      let badNode =
        nodes
        |> List.tryFind (fun n ->
          n.IsLocal
          || n.CheckoutSync <> FleetBalance.CheckoutSyncStatus.Synced
          || n.Pressure = Some MemoryPressure.Critical)
        |> Option.map _.NodeId
        |> Option.defaultValue localNodeId
      FleetBalance.PlacementDecision.Proceed(Some { adv with RecommendedNode = badNode })
    | FaultMode.RecommendUnsyncedOrLocal, FleetBalance.PlacementDecision.Throttled adv ->
      FleetBalance.PlacementDecision.Throttled { adv with RecommendedNode = localNodeId }
    | FaultMode.SuppressThrottleWhenLanIdle, FleetBalance.PlacementDecision.Throttled adv ->
      FleetBalance.PlacementDecision.Proceed(Some { adv with Throttled = false })
    | _ -> decision

  let private injectSilenceFault (fault: FaultMode) (evaluation: FleetBalance.SilentAgentEvaluation) =
    match fault with
    | FaultMode.SuppressSilentAuditorAlert ->
      { evaluation with
          Alerts = evaluation.Alerts |> List.filter (fun a -> a.Kind <> "silent_auditor") }
    | _ -> evaluation

  type private SimState = {
    Rng: RngState
    Now: DateTime
    Members: FleetBalance.CohortMemberHeartbeat list
    PlacementStepsRev: PlacementRecord list
    SilenceStepsRev: SilenceRecord list
  }

  let private generateNode (localNodeId: string) (rng: RngState) (name: string) : FleetBalance.NodeBalanceInput * RngState =
    let isLocal = name = localNodeId
    let pRoll, r1 = rngRange 0 100 rng
    let pressure, r2 =
      if pRoll < 15 then None, r1
      else
        let p, r = rngPick pressures r1
        Some p, r
    let cap =
      match pressure with
      | Some p -> Some(ExpensiveWorkLease.maxConcurrentFor p)
      | None -> None
    let active, r3 =
      match cap with
      | Some c -> rngRange 0 (c + 2) r2
      | None -> rngRange 0 2 r2
    let availRoll, r4 = rngRange 0 100 r3
    let available = if isLocal then true else availRoll >= 20
    let syncStatus, r5 =
      if isLocal then FleetBalance.CheckoutSyncStatus.Synced, r4
      elif not available then FleetBalance.CheckoutSyncStatus.Unknown, r4
      else rngPick syncStates r4
    let qRoll, r6 = rngRange 0 10 r5
    let queued = if qRoll < 7 then 0 else qRoll - 6
    let availGb, r7 = rngRange 4 128 r6
    let node : FleetBalance.NodeBalanceInput = {
      NodeId = name
      IsLocal = isLocal
      Available = available
      CheckoutSync = syncStatus
      Pressure = pressure
      ActiveLeases = active
      LeaseCap = cap
      QueuedLeases = queued
      AvailableRamBytes = int64 availGb * 1_073_741_824L
      TotalRamBytes = 128L * 1_073_741_824L
    }
    node, r7

  let private stepSim (fault: FaultMode) (localNodeId: string) (threshold: TimeSpan) (state: SimState) : SimState =
    let deltaSecs, r1 = rngRange 20 240 state.Rng
    let now = state.Now.AddSeconds(float deltaSecs)
    let nodesRev, r2 =
      (([], r1), nodeNames)
      ||> List.fold (fun (acc, rngAcc) name ->
        let node, nextRng = generateNode localNodeId rngAcc name
        node :: acc, nextRng)
    let nodes = List.rev nodesRev

    let policy, r3 = rngPick policies r2
    let kind, r4 = rngPick leaseKinds r3
    let pinRoll, r5 = rngRange 0 100 r4
    let pinned = pinRoll < 25
    let rawDecision = FleetBalance.evaluatePlacement policy localNodeId nodes kind pinned
    let decision = injectPlacementFault fault localNodeId nodes rawDecision
    let placementRecord : PlacementRecord = {
      Policy = policy
      LocalNodeId = localNodeId
      Kind = kind
      PinnedToLocal = pinned
      Nodes = nodes
      Decision = decision
    }

    let memberIdx, r6 = rngRange 0 state.Members.Length r5
    let memberAction, r7 = rngRange 0 4 r6
    let nextMembers =
      state.Members
      |> List.mapi (fun i current ->
        if i <> memberIdx then current
        else
          match memberAction with
          | 0 -> { current with Present = true; LastActivityUtc = Some now; LastRenewalUtc = now }
          | 1 -> { current with Present = true; LastRenewalUtc = now }
          | 2 -> { current with Present = false }
          | _ -> current)

    let fleetActiveLeases =
      nodes
      |> List.sumBy (fun n -> if n.IsLocal || n.Available then n.ActiveLeases else 0)
    let openClaims, r8 = rngRange 0 4 r7
    let unauditedRuns, r9 = rngRange 0 3 r8
    let dirtyTranches, r10 = rngRange 0 2 r9
    let work : FleetBalance.ActiveWorkContext = {
      ActiveLeaseCount = fleetActiveLeases
      OpenClaimCount = openClaims
      UnauditedRunCount = unauditedRuns
      DirtyTrancheCount = dirtyTranches
    }
    let rawEval = FleetBalance.evaluateSilentAgents now threshold nextMembers work
    let evaluation = injectSilenceFault fault rawEval
    let silenceRecord : SilenceRecord = {
      Now = now
      Threshold = threshold
      Members = nextMembers
      Work = work
      Evaluation = evaluation
    }
    { Rng = r10
      Now = now
      Members = nextMembers
      PlacementStepsRev = placementRecord :: state.PlacementStepsRev
      SilenceStepsRev = silenceRecord :: state.SilenceStepsRev }

  let run (seed: int) (steps: int) (fault: FaultMode) : Trace =
    let localNodeId = "z13"
    let t0 = DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
    let threshold = TimeSpan.FromSeconds 300.0
    let initialMembers =
      memberRoles
      |> List.mapi (fun i (name, family, role) ->
        ({ MemberId = sprintf "mcp:m-%d" i
           DisplayName = name
           AgentFamily = family
           Role = role
           Present = (i < 3)
           LastActivityUtc = Some t0
           LastRenewalUtc = t0 } : FleetBalance.CohortMemberHeartbeat))
    let initialState : SimState = {
      Rng = rngInit seed
      Now = t0
      Members = initialMembers
      PlacementStepsRev = []
      SilenceStepsRev = []
    }
    let finalState =
      [ 1 .. max 0 steps ]
      |> List.fold (fun st _ -> stepSim fault localNodeId threshold st) initialState
    { Seed = seed
      Fault = fault
      PlacementSteps = List.rev finalState.PlacementStepsRev
      SilenceSteps = List.rev finalState.SilenceStepsRev }
