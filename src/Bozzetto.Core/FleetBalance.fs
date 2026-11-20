namespace Bozzetto

open System

// Pure fleet load-rebalancing, Z13 throttle evaluation, and silent-agent
// visibility rules. Clock-free and allocation-clean Zone A logic: callers inject
// `now`, policy, node capacity telemetry, and cohort/activity heartbeats.
module FleetBalance =

  [<RequireQualifiedAccess>]
  type Z13ThrottlePolicy =
    | Off
    | AdvisoryOnly
    | EnforceWhenLanIdle

  [<RequireQualifiedAccess>]
  type PolicyParseError =
    | UnrecognizedPolicy of raw: string

  module Z13ThrottlePolicy =
    let toToken =
      function
      | Z13ThrottlePolicy.Off -> "off"
      | Z13ThrottlePolicy.AdvisoryOnly -> "advisory_only"
      | Z13ThrottlePolicy.EnforceWhenLanIdle -> "enforce_when_lan_idle"

    let describeParseError =
      function
      | PolicyParseError.UnrecognizedPolicy raw ->
        sprintf
          "Unrecognized BOZZETTO_Z13_THROTTLE value '%s' (expected off, advisory_only, or enforce_when_lan_idle)."
          raw

  let tryParsePolicy (raw: string option) : Result<Z13ThrottlePolicy, PolicyParseError> =
    match raw with
    | None -> Ok Z13ThrottlePolicy.AdvisoryOnly
    | Some s when String.IsNullOrWhiteSpace s -> Ok Z13ThrottlePolicy.AdvisoryOnly
    | Some s ->
      match s.Trim().ToLowerInvariant() with
      | "off" | "0" | "false" | "disabled" -> Ok Z13ThrottlePolicy.Off
      | "advisory" | "advisory_only" | "advisoryonly" | "advisory-only" -> Ok Z13ThrottlePolicy.AdvisoryOnly
      | "enforce" | "enforce_when_lan_idle" | "enforcewhenlanidle" | "enforce-when-lan-idle" | "strict" ->
        Ok Z13ThrottlePolicy.EnforceWhenLanIdle
      | _ -> Error(PolicyParseError.UnrecognizedPolicy(s.Trim()))

  [<RequireQualifiedAccess>]
  type CheckoutSyncStatus =
    | Synced
    | Unsynced
    | Unknown

  module CheckoutSyncStatus =
    let toToken =
      function
      | CheckoutSyncStatus.Synced -> "synced"
      | CheckoutSyncStatus.Unsynced -> "unsynced"
      | CheckoutSyncStatus.Unknown -> "unknown"

    let ofOption =
      function
      | Some true -> CheckoutSyncStatus.Synced
      | Some false -> CheckoutSyncStatus.Unsynced
      | None -> CheckoutSyncStatus.Unknown

  type NodeBalanceInput = {
    NodeId: string
    IsLocal: bool
    Available: bool
    CheckoutSync: CheckoutSyncStatus
    Pressure: MemoryPressure option
    ActiveLeases: int
    LeaseCap: int option
    QueuedLeases: int
    AvailableRamBytes: int64
    TotalRamBytes: int64
  }

  type NodeCapacitySummary = {
    NodeId: string
    Available: bool
    CheckoutSync: CheckoutSyncStatus
    FreeSlots: int option
    ActiveLeases: int
    LeaseCap: int option
    QueuedLeases: int
    AvailableRamBytes: int64
    TotalRamBytes: int64
    Pressure: string
  }

  type RebalanceAdvisory = {
    RecommendedNode: string
    Reason: string
    Policy: string
    Throttled: bool
    Candidates: NodeCapacitySummary list
  }

  [<RequireQualifiedAccess>]
  type PlacementDecision =
    | Proceed of advisory: RebalanceAdvisory option
    | BypassedPinned of reason: string
    | Throttled of advisory: RebalanceAdvisory

  let private isOffloadableKind =
    function
    | ExpensiveWorkLease.Kind.FullBuild
    | ExpensiveWorkLease.Kind.TestSuiteRun
    | ExpensiveWorkLease.Kind.Rebuild -> true
    | ExpensiveWorkLease.Kind.SessionCreateOrWarmup
    | ExpensiveWorkLease.Kind.RunApp -> false

  let isZ13Origin (nodeId: string) : bool =
    if isNull nodeId then false
    else
      let trimmed = nodeId.Trim()
      String.Equals(trimmed, "z13", StringComparison.OrdinalIgnoreCase)
      || String.Equals(trimmed, "workstation", StringComparison.OrdinalIgnoreCase)

  let isLocalBusy (localNodeId: string) (nodes: NodeBalanceInput list) : bool =
    nodes
    |> List.exists (fun n ->
      (n.IsLocal || String.Equals(n.NodeId, localNodeId, StringComparison.Ordinal))
      && (n.ActiveLeases >= 1
          || n.QueuedLeases > 0
          || n.Pressure = Some MemoryPressure.Tight
          || n.Pressure = Some MemoryPressure.Critical))

  let summarizeEligibleNode (localNodeId: string) (node: NodeBalanceInput) : NodeCapacitySummary option =
    if String.IsNullOrWhiteSpace node.NodeId then None
    elif node.IsLocal || String.Equals(node.NodeId, localNodeId, StringComparison.Ordinal) || isZ13Origin node.NodeId then None
    elif not node.Available || node.CheckoutSync <> CheckoutSyncStatus.Synced then None
    elif node.Pressure = Some MemoryPressure.Critical then None
    else
      match node.LeaseCap with
      | None -> None
      | Some cap when cap <= 0 || node.ActiveLeases < 0 || node.QueuedLeases <> 0 || node.ActiveLeases >= cap -> None
      | Some cap ->
        let pressureText =
          node.Pressure
          |> Option.map MemoryPressure.describe
          |> Option.defaultValue "unknown"
        Some {
          NodeId = node.NodeId
          Available = true
          CheckoutSync = CheckoutSyncStatus.Synced
          FreeSlots = Some(cap - node.ActiveLeases)
          ActiveLeases = node.ActiveLeases
          LeaseCap = Some cap
          QueuedLeases = node.QueuedLeases
          AvailableRamBytes = max 0L node.AvailableRamBytes
          TotalRamBytes = max 0L node.TotalRamBytes
          Pressure = pressureText
        }

  let private compareCandidate (a: NodeCapacitySummary) (b: NodeCapacitySummary) : int =
    let freeA = a.FreeSlots |> Option.defaultValue 0
    let freeB = b.FreeSlots |> Option.defaultValue 0
    match compare freeB freeA with
    | 0 ->
      let loadA = a.ActiveLeases + a.QueuedLeases
      let loadB = b.ActiveLeases + b.QueuedLeases
      match compare loadA loadB with
      | 0 ->
        match compare b.AvailableRamBytes a.AvailableRamBytes with
        | 0 -> StringComparer.Ordinal.Compare(a.NodeId, b.NodeId)
        | c -> c
      | c -> c
    | c -> c

  let rankEligibleCandidates (localNodeId: string) (nodes: NodeBalanceInput list) : NodeCapacitySummary list =
    nodes
    |> List.choose (summarizeEligibleNode localNodeId)
    |> List.sortWith compareCandidate

  let evaluatePlacement
      (policy: Z13ThrottlePolicy)
      (localNodeId: string)
      (nodes: NodeBalanceInput list)
      (kind: ExpensiveWorkLease.Kind)
      (pinnedToLocal: bool)
      : PlacementDecision =
    if not (isZ13Origin localNodeId) then
      PlacementDecision.Proceed None
    elif pinnedToLocal then
      PlacementDecision.BypassedPinned "pinned_to_local"
    elif not (isOffloadableKind kind) then
      PlacementDecision.BypassedPinned "local_only_lease_kind"
    else
      match policy with
      | Z13ThrottlePolicy.Off -> PlacementDecision.Proceed None
      | Z13ThrottlePolicy.AdvisoryOnly
      | Z13ThrottlePolicy.EnforceWhenLanIdle ->
        let localBusy = isLocalBusy localNodeId nodes
        let candidates = rankEligibleCandidates localNodeId nodes
        match localBusy, candidates with
        | true, best :: _ ->
          let throttled = (policy = Z13ThrottlePolicy.EnforceWhenLanIdle)
          let kindToken = ExpensiveWorkLease.Kind.toToken kind
          let policyToken = Z13ThrottlePolicy.toToken policy
          let bestFree = best.FreeSlots |> Option.defaultValue 0
          let reason =
            if throttled then
              sprintf
                "Local node %s is busy/constrained while LAN worker %s has %d free slot(s); policy %s deferred local %s (pass pinned_to_z13 / --pin-z13 to override)."
                localNodeId best.NodeId bestFree policyToken kindToken
            else
              sprintf
                "Local node %s is busy/constrained while LAN worker %s has %d free slot(s) ready for %s."
                localNodeId best.NodeId bestFree kindToken
          let advisory = {
            RecommendedNode = best.NodeId
            Reason = reason
            Policy = policyToken
            Throttled = throttled
            Candidates = candidates
          }
          if throttled then PlacementDecision.Throttled advisory
          else PlacementDecision.Proceed(Some advisory)
        | _ -> PlacementDecision.Proceed None

  type CohortMemberHeartbeat = {
    MemberId: string
    DisplayName: string
    AgentFamily: string
    Role: Cohort.JoinableRole
    Present: bool
    LastActivityUtc: DateTime option
    LastRenewalUtc: DateTime
  }

  type ActiveWorkContext = {
    ActiveLeaseCount: int
    OpenClaimCount: int
    UnauditedRunCount: int
    DirtyTrancheCount: int
  }

  type SilentAgentAlert = {
    MemberId: string
    DisplayName: string
    AgentFamily: string
    Role: string
    Kind: string
    SilentSeconds: int64
    LastSeenUtc: DateTime
    Reason: string
  }

  type SilentAgentEvaluation = {
    Alerts: SilentAgentAlert list
    NextSilenceTransitionUtc: DateTime option
  }

  let private tokenSeparators = [| '-'; '_'; ':'; '/'; ' '; '.'; '('; ')'; '['; ']' |]

  let recognizeAgentFamily (label: string) (fallbackMemberId: string) : string =
    let candidate =
      if String.IsNullOrWhiteSpace label then fallbackMemberId
      else label.Trim()
    if String.IsNullOrWhiteSpace candidate then "unknown"
    else
      let lower = candidate.ToLowerInvariant()
      let tokens = lower.Split(tokenSeparators, StringSplitOptions.RemoveEmptyEntries)
      let hasToken target = tokens |> Array.exists (fun t -> t = target)
      if lower.Contains("antigravity", StringComparison.Ordinal) || hasToken "antigravity" then "antigravity"
      elif lower.Contains("claude-code", StringComparison.Ordinal) || hasToken "claude" then "claude-code"
      elif lower.Contains("codex", StringComparison.Ordinal) || hasToken "codex" then "codex"
      elif hasToken "pi" then "pi"
      else candidate

  let private alertKindForRole =
    function
    | Cohort.JoinableRole.Verifier -> "silent_auditor"
    | Cohort.JoinableRole.Implementer -> "silent_implementer"
    | Cohort.JoinableRole.Observer -> "silent_observer"

  let private rolePriority =
    function
    | "silent_auditor" -> 0
    | "silent_implementer" -> 1
    | _ -> 2

  let private roleName =
    function
    | Cohort.JoinableRole.Verifier -> "Verifier"
    | Cohort.JoinableRole.Implementer -> "Implementer"
    | Cohort.JoinableRole.Observer -> "Observer"

  let private isRelevantWorkAccumulating (role: Cohort.JoinableRole) (work: ActiveWorkContext) : bool =
    match role with
    | Cohort.JoinableRole.Verifier ->
      work.UnauditedRunCount > 0
      || work.OpenClaimCount > 0
      || work.ActiveLeaseCount > 0
      || work.DirtyTrancheCount > 0
    | Cohort.JoinableRole.Implementer ->
      work.ActiveLeaseCount > 0
      || work.OpenClaimCount > 0
      || work.DirtyTrancheCount > 0
    | Cohort.JoinableRole.Observer ->
      work.ActiveLeaseCount > 0
      || work.OpenClaimCount > 0
      || work.UnauditedRunCount > 0
      || work.DirtyTrancheCount > 0

  let private safeAddUtc (dt: DateTime) (span: TimeSpan) : DateTime =
    if span <= TimeSpan.Zero then dt
    elif dt > DateTime.MaxValue - span then DateTime.MaxValue
    else dt + span

  let effectiveLastSeen (heartbeat: CohortMemberHeartbeat) : DateTime =
    match heartbeat.LastActivityUtc with
    | Some act when act > heartbeat.LastRenewalUtc -> act
    | _ -> heartbeat.LastRenewalUtc

  let evaluateSilentAgents
      (now: DateTime)
      (silenceThreshold: TimeSpan)
      (members: CohortMemberHeartbeat list)
      (work: ActiveWorkContext)
      : SilentAgentEvaluation =
    let effectiveThreshold =
      if silenceThreshold < TimeSpan.Zero then TimeSpan.Zero
      else silenceThreshold
    let alerts, soonest =
      (([], None), members)
      ||> List.fold (fun (accAlerts, accNext) m ->
        if not m.Present || not (isRelevantWorkAccumulating m.Role work) then
          accAlerts, accNext
        else
          let lastSeen = effectiveLastSeen m
          let elapsed = now - lastSeen
          let kind = alertKindForRole m.Role
          let family =
            if not (String.IsNullOrWhiteSpace m.AgentFamily) then
              recognizeAgentFamily m.AgentFamily m.MemberId
            else
              recognizeAgentFamily m.DisplayName m.MemberId
          let display =
            if String.IsNullOrWhiteSpace m.DisplayName then m.MemberId
            else m.DisplayName
          if elapsed >= effectiveThreshold then
            let silentSecs = max 0L (int64 elapsed.TotalSeconds)
            let reason =
              sprintf
                "%s (%s, %s) silent for %ds while work is accumulating (%d active lease(s), %d open claim(s), %d unaudited run(s), %d dirty tranche(s))."
                display family (roleName m.Role) silentSecs work.ActiveLeaseCount work.OpenClaimCount work.UnauditedRunCount work.DirtyTrancheCount
            let alert = {
              MemberId = m.MemberId
              DisplayName = display
              AgentFamily = family
              Role = roleName m.Role
              Kind = kind
              SilentSeconds = silentSecs
              LastSeenUtc = lastSeen
              Reason = reason
            }
            alert :: accAlerts, accNext
          else
            let transitionAt = safeAddUtc lastSeen effectiveThreshold
            let nextBest =
              if transitionAt > now then
                match accNext with
                | None -> Some transitionAt
                | Some best -> Some(if transitionAt < best then transitionAt else best)
              else
                accNext
            accAlerts, nextBest)
    let sortedAlerts =
      alerts
      |> List.sortWith (fun a b ->
        match compare (rolePriority a.Kind) (rolePriority b.Kind) with
        | 0 ->
          match compare b.SilentSeconds a.SilentSeconds with
          | 0 -> StringComparer.Ordinal.Compare(a.MemberId, b.MemberId)
          | c -> c
        | c -> c)
    { Alerts = sortedAlerts; NextSilenceTransitionUtc = soonest }

  type FleetBalanceSummary = {
    Policy: Z13ThrottlePolicy
    SilenceThresholdSeconds: int
    LocalNodeId: string
    LocalBusy: bool
    Advisory: RebalanceAdvisory option
    SilentAlerts: SilentAgentAlert list
    NextSilenceTransitionUtc: DateTime option
    Nodes: NodeCapacitySummary list
  }

  let summarizeFleetBalance
      (now: DateTime)
      (policy: Z13ThrottlePolicy)
      (silenceThreshold: TimeSpan)
      (localNodeId: string)
      (nodes: NodeBalanceInput list)
      (members: CohortMemberHeartbeat list)
      (work: ActiveWorkContext)
      : FleetBalanceSummary =
    let placement = evaluatePlacement policy localNodeId nodes ExpensiveWorkLease.Kind.FullBuild false
    let advisory =
      match placement with
      | PlacementDecision.Throttled a -> Some a
      | PlacementDecision.Proceed a -> a
      | PlacementDecision.BypassedPinned _ -> None
    let silence = evaluateSilentAgents now silenceThreshold members work
    let allNodeSummaries =
      nodes
      |> List.choose (fun node ->
        if String.IsNullOrWhiteSpace node.NodeId then None
        else
          let leaseCap =
            match node.LeaseCap with
            | Some cap when cap >= 0 -> Some cap
            | _ -> None
          let freeSlots =
            if not node.Available then None
            else
              match leaseCap with
              | Some cap -> Some(max 0 (cap - node.ActiveLeases))
              | None -> None
          Some {
            NodeId = node.NodeId
            Available = node.Available
            CheckoutSync = node.CheckoutSync
            FreeSlots = freeSlots
            ActiveLeases = node.ActiveLeases
            LeaseCap = leaseCap
            QueuedLeases = node.QueuedLeases
            AvailableRamBytes = max 0L node.AvailableRamBytes
            TotalRamBytes = max 0L node.TotalRamBytes
            Pressure = node.Pressure |> Option.map MemoryPressure.describe |> Option.defaultValue "unknown"
          })
      |> List.sortWith (fun a b ->
        let aLocal = String.Equals(a.NodeId, localNodeId, StringComparison.Ordinal)
        let bLocal = String.Equals(b.NodeId, localNodeId, StringComparison.Ordinal)
        if aLocal <> bLocal then (if aLocal then -1 else 1)
        else StringComparer.Ordinal.Compare(a.NodeId, b.NodeId))
    {
      Policy = policy
      SilenceThresholdSeconds = max 0 (int silenceThreshold.TotalSeconds)
      LocalNodeId = localNodeId
      LocalBusy = isLocalBusy localNodeId nodes
      Advisory = advisory
      SilentAlerts = silence.Alerts
      NextSilenceTransitionUtc = silence.NextSilenceTransitionUtc
      Nodes = allNodeSummaries
    }
