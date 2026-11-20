namespace Bozzetto

open System
open System.Threading
open Fidelity.Data.JSON
open Bozzetto.McpJson
open Bozzetto.Web.Shared.Protocol

// Live daemon projection, JSON codecs, and lease-placement enforcement for FleetBalance.
// Isolates clocks, environment config, and live reader hooks at the Zone B shell edge
// while delegating ranking, throttling, and silence classification to Bozzetto.FleetBalance.
module FleetBalanceService =

  let [<Literal>] DefaultSilentAgentSeconds = 300

  type CohortWorkFacts = {
    Members: FleetBalance.CohortMemberHeartbeat list
    OpenClaimCount: int
    UnauditedRunCount: int
    DirtyTrancheCount: int
  }

  let emptyCohortWorkFacts : CohortWorkFacts = {
    Members = []
    OpenClaimCount = 0
    UnauditedRunCount = 0
    DirtyTrancheCount = 0
  }

  let private gate = obj ()
  let mutable private workBoardReader: (unit -> WorkBoard option) option = None
  let mutable private localNodeIdOverride: string option = None
  let mutable private cohortOwnerRef: Features.CohortOwner.Handle option = None
  let mutable private activityTrackerRef: AgentActivityTracker.Tracker option = None

  let installWorkBoardReader (reader: unit -> WorkBoard option) : IDisposable =
    let target = Some reader
    lock gate (fun () -> Volatile.Write(&workBoardReader, target))
    { new IDisposable with
        member _.Dispose() =
          lock gate (fun () ->
            match Volatile.Read(&workBoardReader) with
            | Some current when obj.ReferenceEquals(current, reader) -> Volatile.Write(&workBoardReader, None)
            | _ -> ()) }

  let internal installLocalNodeId (nodeId: string) : IDisposable =
    let target = Some nodeId
    lock gate (fun () -> Volatile.Write(&localNodeIdOverride, target))
    { new IDisposable with
        member _.Dispose() =
          lock gate (fun () ->
            match Volatile.Read(&localNodeIdOverride) with
            | Some current when obj.ReferenceEquals(current, nodeId) -> Volatile.Write(&localNodeIdOverride, None)
            | _ -> ()) }

  let installCohortReaders (ownerOpt: Features.CohortOwner.Handle option) (tracker: AgentActivityTracker.Tracker) : IDisposable =
    let trackerOpt = Some tracker
    lock gate (fun () ->
      cohortOwnerRef <- ownerOpt
      activityTrackerRef <- trackerOpt)
    { new IDisposable with
        member _.Dispose() =
          lock gate (fun () ->
            match cohortOwnerRef, ownerOpt with
            | Some c, Some o when obj.ReferenceEquals(c, o) -> cohortOwnerRef <- None
            | None, None -> ()
            | _ -> ()
            match activityTrackerRef with
            | Some t when obj.ReferenceEquals(t, tracker) -> activityTrackerRef <- None
            | _ -> ()) }

  let readWorkBoard () : WorkBoard option =
    match Volatile.Read(&workBoardReader) with
    | Some reader ->
      try reader () with _ -> None
    | None -> None

  let private readAmbientCohortAndTracker () =
    lock gate (fun () -> cohortOwnerRef, activityTrackerRef)

  let localNodeId () : string =
    Volatile.Read(&localNodeIdOverride) |> Option.defaultValue BozzettoConfig.WorkNodeId

  let parsePolicyWithRaw (rawOpt: string option) : FleetBalance.Z13ThrottlePolicy * string option =
    match FleetBalance.tryParsePolicy rawOpt with
    | Ok policy -> policy, None
    | Error err -> FleetBalance.Z13ThrottlePolicy.AdvisoryOnly, Some(FleetBalance.Z13ThrottlePolicy.describeParseError err)

  let currentPolicy () : FleetBalance.Z13ThrottlePolicy * string option =
    let raw = Environment.GetEnvironmentVariable "BOZZETTO_Z13_THROTTLE"
    parsePolicyWithRaw (if isNull raw then None else Some raw)

  let parseSilenceThreshold (rawOpt: string option) : TimeSpan =
    match rawOpt with
    | Some raw when not (String.IsNullOrWhiteSpace raw) ->
      match Int32.TryParse(raw.Trim()) with
      | true, secs when secs >= 1 -> TimeSpan.FromSeconds(float secs)
      | _ -> TimeSpan.FromSeconds(float DefaultSilentAgentSeconds)
    | _ -> TimeSpan.FromSeconds(float DefaultSilentAgentSeconds)

  let currentSilenceThreshold () : TimeSpan =
    let raw = Environment.GetEnvironmentVariable "BOZZETTO_SILENT_AGENT_SECONDS"
    parseSilenceThreshold (if isNull raw then None else Some raw)

  let parsePressureOpt (raw: string) : MemoryPressure option =
    if String.IsNullOrWhiteSpace raw then None
    else
      match raw.Trim().ToLowerInvariant() with
      | "normal" -> Some MemoryPressure.Normal
      | "tight" -> Some MemoryPressure.Tight
      | "critical" -> Some MemoryPressure.Critical
      | _ -> None

  let checkoutSyncedOfRetrieval
      (branch: Bozzetto.Server.RetrievalObservation.BranchInfo)
      (retrieval: RetrievalStatus)
      : bool option =
    Bozzetto.Server.RetrievalObservation.checkoutSyncedForBranch branch retrieval

  let buildNodeBalanceInputs
      (localId: string)
      (localPressure: MemoryPressure option)
      (localPool: Features.LeaseWatch.Pool)
      (machine: Features.MachineMemory.Stats)
      (localCheckoutSynced: bool option)
      (boardOpt: WorkBoard option)
      : FleetBalance.NodeBalanceInput list =
    let boardNodes =
      match boardOpt with
      | Some b -> b.Nodes
      | None -> [||]
    let boardLocal =
      boardNodes
      |> Array.tryFind (fun n -> n.Local || String.Equals(n.Source.Node, localId, StringComparison.Ordinal))
    let localSnapCap = boardLocal |> Option.bind (fun n -> n.Value) |> Option.bind (fun v -> v.Capacity)
    let localSync = localCheckoutSynced |> FleetBalance.CheckoutSyncStatus.ofOption
    let effectiveLocalPressure =
      match localPressure with
      | Some _ -> localPressure
      | None -> localSnapCap |> Option.bind (fun c -> parsePressureOpt c.MemoryPressure)
    let localCap =
      match effectiveLocalPressure with
      | Some p -> Some(ExpensiveWorkLease.maxConcurrentFor p)
      | None -> localSnapCap |> Option.bind (fun c -> c.LeaseCap |> Option.map int)
    let localInput : FleetBalance.NodeBalanceInput = {
      NodeId = localId
      IsLocal = true
      Available = true
      CheckoutSync = localSync
      Pressure = effectiveLocalPressure
      ActiveLeases = localPool.Active.Length
      LeaseCap = localCap
      QueuedLeases = localPool.Queue.Length
      AvailableRamBytes = max 0L machine.AvailableBytes
      TotalRamBytes = max 0L machine.TotalBytes
    }
    let remoteInputs =
      boardNodes
      |> Array.toList
      |> List.choose (fun node ->
        if node.Local || String.Equals(node.Source.Node, localId, StringComparison.Ordinal) then
          None
        else
          match node.Value with
          | None ->
            Some ({
              NodeId = node.Source.Node
              IsLocal = false
              Available = false
              CheckoutSync = FleetBalance.CheckoutSyncStatus.Unknown
              Pressure = None
              ActiveLeases = 0
              LeaseCap = None
              QueuedLeases = 0
              AvailableRamBytes = 0L
              TotalRamBytes = 0L
            } : FleetBalance.NodeBalanceInput)
          | Some snap ->
            let available = (node.Status = "available")
            let syncStatus =
              if not available then FleetBalance.CheckoutSyncStatus.Unknown
              else snap.Capacity |> Option.bind _.CheckoutSynced |> FleetBalance.CheckoutSyncStatus.ofOption
            let pressure = snap.Capacity |> Option.bind (fun c -> parsePressureOpt c.MemoryPressure)
            let leaseCap = snap.Capacity |> Option.bind (fun c -> c.LeaseCap |> Option.map int)
            let availRam = snap.Capacity |> Option.map (fun c -> c.MachineAvailableBytes) |> Option.defaultValue 0L
            let totalRam = snap.Capacity |> Option.map (fun c -> c.MachineTotalBytes) |> Option.defaultValue 0L
            Some ({
              NodeId = node.Source.Node
              IsLocal = false
              Available = available
              CheckoutSync = syncStatus
              Pressure = pressure
              ActiveLeases = snap.Leases.Active.Length
              LeaseCap = leaseCap
              QueuedLeases = snap.Leases.Queued.Length
              AvailableRamBytes = max 0L availRam
              TotalRamBytes = max 0L totalRam
            } : FleetBalance.NodeBalanceInput))
    localInput :: remoteInputs

  let activityMapFromTracker (tracker: AgentActivityTracker.Tracker) : Map<string, DateTime> =
    AgentActivityTracker.snapshotWithVersion tracker |> snd

  let normalizeProjectKey (project: string) : string =
    if String.IsNullOrWhiteSpace project then ""
    else project.Trim().Replace('\\', '/').TrimEnd('/')

  let private normalizeStatus (status: string) : string =
    if isNull status then "" else status.Trim().ToLowerInvariant()

  let extractCohortWorkFacts
      (cohortFrameOpt: Cohort.CohortFrame<MemberTable.MemberId> option)
      (workOpt: AgentWork.State option)
      (activityMap: Map<string, DateTime>)
      : CohortWorkFacts =
    match cohortFrameOpt with
    | None -> emptyCohortWorkFacts
    | Some frame ->
      let members =
        [| for i in 0 .. frame.MemberIds.Length - 1 do
             let key = MemberTable.MemberId.display frame.MemberIds[i]
             let rawAgent = if i < frame.MemberAgentName.Length then frame.MemberAgentName[i] |> Option.defaultValue "" else ""
             let display = if String.IsNullOrWhiteSpace rawAgent then key else rawAgent
             let present =
               match frame.MemberSeat[i] with
               | Cohort.SeatState.Present -> true
               | Cohort.SeatState.Departed _ -> false
             let family = FleetBalance.recognizeAgentFamily display key
             ({ MemberId = key
                DisplayName = display
                AgentFamily = family
                Role = frame.MemberRole[i]
                Present = present
                LastActivityUtc = activityMap |> Map.tryFind key
                LastRenewalUtc = frame.MemberRenewal[i] } : FleetBalance.CohortMemberHeartbeat) |]
        |> Array.toList
      let openClaims =
        frame.ClaimState
        |> Array.sumBy (function
          | Cohort.ClaimState.Held _
          | Cohort.ClaimState.Orphaned _ -> 1
          | Cohort.ClaimState.Released _ -> 0)
      let unsettledLandings =
        frame.LandingState
        |> Array.sumBy (function
          | Cohort.LandingState.Queued
          | Cohort.LandingState.Rebasing _
          | Cohort.LandingState.Verifying _ -> 1
          | Cohort.LandingState.Blocked _
          | Cohort.LandingState.Landed _
          | Cohort.LandingState.Withdrawn -> 0)
      let orphanedClaims =
        frame.ClaimState
        |> Array.sumBy (function
          | Cohort.ClaimState.Orphaned _ -> 1
          | Cohort.ClaimState.Held _
          | Cohort.ClaimState.Released _ -> 0)
      let unauditedRuns =
        match workOpt with
        | None -> 0
        | Some work ->
          let latestVerifierRunByProject =
            work.Runs
            |> Map.toSeq
            |> Seq.choose (fun (_, run) ->
              if run.Role <> Cohort.JoinableRole.Verifier then None
              else
                let status = normalizeStatus run.Report.Status
                let projectKey = normalizeProjectKey run.Report.Project
                if projectKey = "" || status <> "completed" then
                  None
                else
                  Some(projectKey, run.UpdatedAt))
            |> Seq.fold (fun acc (proj, updatedAt) ->
              match Map.tryFind proj acc with
              | Some existing when existing >= updatedAt -> acc
              | _ -> Map.add proj updatedAt acc) Map.empty
          work.Runs
          |> Map.toSeq
          |> Seq.sumBy (fun (_, run) ->
            let status = normalizeStatus run.Report.Status
            let finished = status = "completed" || status = "failed" || status = "aborted" || status = "disconnected"
            if finished && run.Role <> Cohort.JoinableRole.Verifier then
              let projKey = normalizeProjectKey run.Report.Project
              let auditedByVerifierRun =
                projKey <> ""
                && (match Map.tryFind projKey latestVerifierRunByProject with
                    | Some verAt -> verAt >= run.UpdatedAt
                    | None -> false)
              if auditedByVerifierRun then 0 else 1
            else 0)
      { Members = members
        OpenClaimCount = openClaims
        UnauditedRunCount = unauditedRuns
        DirtyTrancheCount = unsettledLandings + orphanedClaims }

  let computeWorkContext
      (localId: string)
      (localPool: Features.LeaseWatch.Pool)
      (boardOpt: WorkBoard option)
      (facts: CohortWorkFacts)
      : FleetBalance.ActiveWorkContext =
    let remoteActiveLeases =
      match boardOpt with
      | None -> 0
      | Some board ->
        board.Nodes
        |> Array.sumBy (fun node ->
          if node.Local || String.Equals(node.Source.Node, localId, StringComparison.Ordinal) then 0
          elif node.Status <> "available" then 0
          else
            match node.Value with
            | Some snap -> snap.Leases.Active.Length
            | None -> 0)
    { ActiveLeaseCount = localPool.Active.Length + remoteActiveLeases
      OpenClaimCount = facts.OpenClaimCount
      UnauditedRunCount = facts.UnauditedRunCount
      DirtyTrancheCount = facts.DirtyTrancheCount }

  let summarizeAt
      (now: DateTime)
      (policy: FleetBalance.Z13ThrottlePolicy)
      (silenceThreshold: TimeSpan)
      (localId: string)
      (localPressure: MemoryPressure option)
      (localPool: Features.LeaseWatch.Pool)
      (machine: Features.MachineMemory.Stats)
      (localCheckoutSynced: bool option)
      (boardOpt: WorkBoard option)
      (facts: CohortWorkFacts)
      : FleetBalance.FleetBalanceSummary * FleetBalance.ActiveWorkContext =
    let nodes = buildNodeBalanceInputs localId localPressure localPool machine localCheckoutSynced boardOpt
    let workCtx = computeWorkContext localId localPool boardOpt facts
    let summary =
      FleetBalance.summarizeFleetBalance now policy silenceThreshold localId nodes facts.Members workCtx
    summary, workCtx

  let liveSummaryWith
      (now: DateTime)
      (ownerOpt: Features.CohortOwner.Handle option)
      (trackerOpt: AgentActivityTracker.Tracker option)
      : FleetBalance.FleetBalanceSummary * FleetBalance.ActiveWorkContext * string option =
    let policy, policyWarning = currentPolicy ()
    let threshold = currentSilenceThreshold ()
    let localId = localNodeId ()
    let tickOpt = Bozzetto.Server.HealthTick.current ()
    let pool = Features.LeaseWatch.current ()
    let machine =
      match tickOpt with
      | Some tick -> tick.Machine
      | None -> Features.MachineMemory.current ()
    let localLevel = tickOpt |> Option.map _.Level
    let localCheckoutSynced =
      Bozzetto.Server.RetrievalObservation.current ()
      |> Bozzetto.Server.RetrievalObservation.checkoutSynced
    let boardOpt = readWorkBoard ()
    let frameOpt = ownerOpt |> Option.map _.ReadFrame()
    let workOpt = ownerOpt |> Option.map _.ReadWork()
    let activityMap = trackerOpt |> Option.map activityMapFromTracker |> Option.defaultValue Map.empty
    let facts = extractCohortWorkFacts frameOpt workOpt activityMap
    let summary, workCtx = summarizeAt now policy threshold localId localLevel pool machine localCheckoutSynced boardOpt facts
    summary, workCtx, policyWarning

  let toWireCandidate (c: FleetBalance.NodeCapacitySummary) : RebalanceCandidate = {
    NodeId = c.NodeId
    FreeSlots = c.FreeSlots |> Option.defaultValue 0 |> int32
    ActiveLeases = int32 c.ActiveLeases
    LeaseCap = c.LeaseCap |> Option.defaultValue 0 |> int32
    QueuedLeases = int32 c.QueuedLeases
    AvailableRamBytes = c.AvailableRamBytes
    TotalRamBytes = c.TotalRamBytes
    Pressure = c.Pressure
  }

  let toWireAdvisory (a: FleetBalance.RebalanceAdvisory) : RebalanceAdvisoryWire = {
    RecommendedNode = a.RecommendedNode
    Reason = a.Reason
    Policy = a.Policy
    Throttled = a.Throttled
    Candidates = a.Candidates |> List.map toWireCandidate |> List.toArray
  }

  let toWireAlert (a: FleetBalance.SilentAgentAlert) : SilentAgentAlertWire = {
    MemberKey = a.MemberId
    DisplayName = a.DisplayName
    AgentFamily = a.AgentFamily
    Role = a.Role
    Kind = a.Kind
    IdleSeconds = a.SilentSeconds
    LastActivityMs = DateTimeOffset(DateTime.SpecifyKind(a.LastSeenUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
    Reason = a.Reason
  }

  let toWireStatus
      (revision: int64)
      (work: FleetBalance.ActiveWorkContext)
      (summary: FleetBalance.FleetBalanceSummary)
      : FleetBalanceStatus = {
    Revision = revision
    Policy = FleetBalance.Z13ThrottlePolicy.toToken summary.Policy
    SilentThresholdSeconds = int32 summary.SilenceThresholdSeconds
    LocalNodeId = summary.LocalNodeId
    LocalBusy = summary.LocalBusy
    Advisory = summary.Advisory |> Option.map toWireAdvisory
    SilentAlerts = summary.SilentAlerts |> List.map toWireAlert |> List.toArray
    ActiveLeaseCount = int32 work.ActiveLeaseCount
    OpenClaimCount = int32 work.OpenClaimCount
    UnauditedRunCount = int32 work.UnauditedRunCount
    DirtyTrancheCount = int32 work.DirtyTrancheCount
  }

  let candidateJsonValue (c: FleetBalance.NodeCapacitySummary) : JsonValue =
    object [
      "node_id", text c.NodeId
      "available", JsonValue.Bool c.Available
      "checkout_sync", text (FleetBalance.CheckoutSyncStatus.toToken c.CheckoutSync)
      "free_slots", match c.FreeSlots with Some n -> integer n | None -> JsonValue.Null
      "active_leases", integer c.ActiveLeases
      "lease_cap", match c.LeaseCap with Some n -> integer n | None -> JsonValue.Null
      "queued_leases", integer c.QueuedLeases
      "available_ram_bytes", signed c.AvailableRamBytes
      "total_ram_bytes", signed c.TotalRamBytes
      "pressure", text c.Pressure
    ]

  let advisoryJsonValue (advisory: FleetBalance.RebalanceAdvisory) : JsonValue =
    object [
      "recommended_node", text advisory.RecommendedNode
      "reason", text advisory.Reason
      "policy", text advisory.Policy
      "throttled", JsonValue.Bool advisory.Throttled
      "candidates", array candidateJsonValue advisory.Candidates
    ]

  let silentAlertJsonValue (alert: FleetBalance.SilentAgentAlert) : JsonValue =
    object [
      "member_id", text alert.MemberId
      "display_name", text alert.DisplayName
      "agent_family", text alert.AgentFamily
      "role", text alert.Role
      "kind", text alert.Kind
      "silent_seconds", signed alert.SilentSeconds
      "last_seen_utc", timestamp (DateTimeOffset(DateTime.SpecifyKind(alert.LastSeenUtc, DateTimeKind.Utc)))
      "reason", text alert.Reason
    ]

  let summaryJsonValue
      (policyWarning: string option)
      (work: FleetBalance.ActiveWorkContext)
      (summary: FleetBalance.FleetBalanceSummary)
      : JsonValue =
    let recommendedNodeJson =
      match summary.Advisory with
      | Some a -> text a.RecommendedNode
      | None -> JsonValue.Null
    let advisoryJson =
      match summary.Advisory with
      | Some a -> advisoryJsonValue a
      | None -> JsonValue.Null
    let fields = [
      yield "policy", text (FleetBalance.Z13ThrottlePolicy.toToken summary.Policy)
      match policyWarning with
      | Some warning -> yield "policy_warning", text warning
      | None -> ()
      yield "silent_threshold_seconds", integer summary.SilenceThresholdSeconds
      yield "local_node_id", text summary.LocalNodeId
      yield "local_busy", JsonValue.Bool summary.LocalBusy
      yield "recommended_node", recommendedNodeJson
      yield "rebalance_advisory", advisoryJson
      yield "silent_alerts", array silentAlertJsonValue summary.SilentAlerts
      yield "work", object [
        "active_leases", integer work.ActiveLeaseCount
        "open_claims", integer work.OpenClaimCount
        "unaudited_runs", integer work.UnauditedRunCount
        "dirty_tranches", integer work.DirtyTrancheCount
      ]
      yield "nodes", array candidateJsonValue summary.Nodes
    ]
    object fields

  let summaryJsonForContext
      (ownerOpt: Features.CohortOwner.Handle option)
      (tracker: AgentActivityTracker.Tracker)
      : JsonValue =
    let summary, workCtx, policyWarning = liveSummaryWith DateTime.UtcNow ownerOpt (Some tracker)
    summaryJsonValue policyWarning workCtx summary

  let currentSummaryJson () : JsonValue =
    let ownerOpt, trackerOpt = readAmbientCohortAndTracker ()
    let summary, workCtx, policyWarning = liveSummaryWith DateTime.UtcNow ownerOpt trackerOpt
    summaryJsonValue policyWarning workCtx summary

  let appendJsonField (fieldName: string) (fieldValue: JsonValue) (rawJson: string) : string =
    match Json.parse rawJson with
    | Ok(JsonValue.Object fields) ->
      let filtered = fields |> List.filter (fun (k, _) -> k <> fieldName)
      JsonValue.Object(filtered @ [ fieldName, fieldValue ]) |> Json.serialize
    | _ -> rawJson

  let leaseDecisionJsonWithAdvisory
      (kind: ExpensiveWorkLease.Kind)
      (pinnedToLocal: bool)
      (advisoryOpt: FleetBalance.RebalanceAdvisory option)
      (decision: ExpensiveWorkLease.Decision)
      : string =
    let kindName = ExpensiveWorkLease.Kind.toToken kind
    let baseFields =
      match decision with
      | ExpensiveWorkLease.Decision.Granted(leaseId, expiresAt) ->
        [ "decision", text "granted"
          "leaseId", text (ExpensiveWorkLease.LeaseId.value leaseId)
          "expiresAt", timestamp expiresAt ]
      | ExpensiveWorkLease.Decision.Wait (retryAfter, reason) ->
        [ "decision", text "wait"
          "retryAfterSeconds", real retryAfter.TotalSeconds
          "reason", text reason ]
      | ExpensiveWorkLease.Decision.Refused reason ->
        [ "decision", text "refused"
          "reason", text reason ]
    let extraFields = [
      yield "throttled", JsonValue.Bool false
      if pinnedToLocal then
        yield "pinned_to_z13", JsonValue.Bool true
      match advisoryOpt with
      | Some advisory -> yield "rebalance_advisory", advisoryJsonValue advisory
      | None -> ()
    ]
    object (("kind", text kindName) :: (baseFields @ extraFields)) |> render

  let throttledLeaseJson (kind: ExpensiveWorkLease.Kind) (advisory: FleetBalance.RebalanceAdvisory) : string =
    let kindName = ExpensiveWorkLease.Kind.toToken kind
    object [
      "kind", text kindName
      "decision", text "refused"
      "throttled", JsonValue.Bool true
      "reason", text advisory.Reason
      "recommended_node", text advisory.RecommendedNode
      "rebalance_advisory", advisoryJsonValue advisory
    ]
    |> render

  let evaluateAndAcquireWorkLeaseWithPolicy
      (policy: FleetBalance.Z13ThrottlePolicy)
      (localId: string)
      (nodesOverride: FleetBalance.NodeBalanceInput list option)
      (holder: string)
      (kind: ExpensiveWorkLease.Kind)
      (pinnedToLocal: bool)
      : string =
    let tick = Bozzetto.Server.HealthTick.sample ()
    let localCheckoutSynced =
      Bozzetto.Server.RetrievalObservation.current ()
      |> Bozzetto.Server.RetrievalObservation.checkoutSynced
    let evaluate (livePool: ExpensiveWorkLease.PoolState) (pressure: MemoryPressure) (_now: DateTimeOffset) =
      let boardOpt = readWorkBoard ()
      let nodes =
        match nodesOverride with
        | Some explicitNodes ->
          explicitNodes
          |> List.map (fun n ->
            if n.IsLocal || String.Equals(n.NodeId, localId, StringComparison.Ordinal) then
              { n with
                  Pressure = Some pressure
                  ActiveLeases = livePool.Active.Length
                  LeaseCap = Some(ExpensiveWorkLease.maxConcurrentFor pressure)
                  QueuedLeases = livePool.Queue.Length }
            else n)
        | None ->
          let poolView : Features.LeaseWatch.Pool = {
            Serial = 0L
            Active =
              livePool.Active
              |> List.map (fun l ->
                { ExpensiveWorkLease.GrantView.Holder = l.Holder
                  Kind = ExpensiveWorkLease.Kind.toToken l.Kind
                  GrantedAt = l.GrantedAt
                  ExpiresAt = l.ExpiresAt })
            Queue =
              livePool.Queue
              |> List.map (fun q ->
                { ExpensiveWorkLease.QueueView.Holder = q.Holder
                  Kind = ExpensiveWorkLease.Kind.toToken q.Kind
                  RequestedAt = q.FirstAskedAt })
          }
          buildNodeBalanceInputs localId (Some pressure) poolView tick.Machine localCheckoutSynced boardOpt
      FleetBalance.evaluatePlacement policy localId nodes kind pinnedToLocal
    match Features.LeaseWatch.requestWithPlacement holder kind evaluate with
    | Error advisory ->
      throttledLeaseJson kind advisory
    | Ok(decision, advisoryOpt) ->
      leaseDecisionJsonWithAdvisory kind pinnedToLocal advisoryOpt decision

  let evaluateAndAcquireWorkLeaseWithNodes
      (localId: string)
      (nodesOverride: FleetBalance.NodeBalanceInput list option)
      (holder: string)
      (kind: ExpensiveWorkLease.Kind)
      (pinnedToLocal: bool)
      : string =
    let policy, _ = currentPolicy ()
    evaluateAndAcquireWorkLeaseWithPolicy policy localId nodesOverride holder kind pinnedToLocal

  let acquireWorkLeaseWithPin
      (tracker: AgentActivityTracker.Tracker)
      (who: MemberTable.MemberId)
      (holder: string)
      (kind: ExpensiveWorkLease.Kind)
      (pinnedToLocal: bool)
      : string =
    AgentActivityTracker.recordMemberActivity
      tracker who "" None (Some (sprintf "lease poll: %s" (ExpensiveWorkLease.Kind.toToken kind))) DateTime.UtcNow
    evaluateAndAcquireWorkLeaseWithNodes (localNodeId ()) None holder kind pinnedToLocal
