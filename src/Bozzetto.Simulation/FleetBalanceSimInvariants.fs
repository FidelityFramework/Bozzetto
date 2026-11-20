namespace Bozzetto.Simulation

open System
open Bozzetto

// Pure functional Zone A invariants for FleetBalance placement/throttling and silent-agent/auditor checks.
module FleetBalanceSimInvariants =

  type Violation = {
    RuleId: string
    Step: int
    Detail: string
  }

  let private isOffloadableKind =
    function
    | ExpensiveWorkLease.Kind.FullBuild
    | ExpensiveWorkLease.Kind.TestSuiteRun
    | ExpensiveWorkLease.Kind.Rebuild -> true
    | ExpensiveWorkLease.Kind.SessionCreateOrWarmup
    | ExpensiveWorkLease.Kind.RunApp -> false

  let private describeDecision =
    function
    | FleetBalance.PlacementDecision.Proceed None -> "Proceed(None)"
    | FleetBalance.PlacementDecision.Proceed(Some adv) -> sprintf "Proceed(Some %s)" adv.RecommendedNode
    | FleetBalance.PlacementDecision.BypassedPinned reason -> sprintf "BypassedPinned(%s)" reason
    | FleetBalance.PlacementDecision.Throttled adv -> sprintf "Throttled(%s)" adv.RecommendedNode

  let private describeNode (n: FleetBalance.NodeBalanceInput) =
    let pressure = n.Pressure |> Option.map MemoryPressure.describe |> Option.defaultValue "none"
    let cap = n.LeaseCap |> Option.map string |> Option.defaultValue "none"
    sprintf "id=%s local=%b avail=%b sync=%s pressure=%s active=%d cap=%s"
      n.NodeId n.IsLocal n.Available (FleetBalance.CheckoutSyncStatus.toToken n.CheckoutSync) pressure n.ActiveLeases cap

  let private describeUtcOpt =
    function
    | None -> "None"
    | Some (dt: DateTime) -> dt.ToString("O")

  let private compareExpected (a: FleetBalance.NodeCapacitySummary) (b: FleetBalance.NodeCapacitySummary) =
    let freeA = a.FreeSlots |> Option.defaultValue 0
    let freeB = b.FreeSlots |> Option.defaultValue 0
    match compare freeB freeA with
    | 0 ->
      match compare (a.ActiveLeases + a.QueuedLeases) (b.ActiveLeases + b.QueuedLeases) with
      | 0 ->
        match compare b.AvailableRamBytes a.AvailableRamBytes with
        | 0 -> StringComparer.Ordinal.Compare(a.NodeId, b.NodeId)
        | c -> c
      | c -> c
    | c -> c

  let checkPlacement (trace: FleetBalanceSim.Trace) : Violation list =
    trace.PlacementSteps
    |> List.mapi (fun step record ->
      let localBusy = FleetBalance.isLocalBusy record.LocalNodeId record.Nodes
      let eligible = FleetBalance.rankEligibleCandidates record.LocalNodeId record.Nodes
      let mk rule detail = { RuleId = rule; Step = step; Detail = detail }
      let policyViolations =
        if record.PinnedToLocal || not (isOffloadableKind record.Kind) then
          match record.Decision with
          | FleetBalance.PlacementDecision.BypassedPinned _ -> []
          | other -> [ mk "PINNED-AND-LOCAL-ONLY-BYPASS" (sprintf "Expected BypassedPinned, got %s" (describeDecision other)) ]
        else
          [ if record.Policy = FleetBalance.Z13ThrottlePolicy.EnforceWhenLanIdle && localBusy && not eligible.IsEmpty then
              match record.Decision with
              | FleetBalance.PlacementDecision.Throttled adv when adv.Throttled -> ()
              | other ->
                yield mk "ENFORCE-WHEN-LAN-IDLE-THROTTLES" (sprintf "Expected Throttled when LAN idle, got %s" (describeDecision other))
            if record.Policy = FleetBalance.Z13ThrottlePolicy.Off then
              match record.Decision with
              | FleetBalance.PlacementDecision.Proceed None -> ()
              | other ->
                yield mk "OFF-POLICY-NO-ADVISORY" (sprintf "Expected Proceed(None) for Off, got %s" (describeDecision other)) ]
      let advisoryViolations =
        match record.Decision with
        | FleetBalance.PlacementDecision.Proceed(Some adv)
        | FleetBalance.PlacementDecision.Throttled adv ->
          let orderViolations =
            if List.isEmpty adv.Candidates then
              [ mk "NO-UNSYNCED-OR-CRITICAL-OR-LOCAL-RECOMMENDATION" "Advisory had empty candidate list." ]
            else
              [ if adv.RecommendedNode <> adv.Candidates.Head.NodeId then
                  yield mk "RANKING-MONOTONIC-DETERMINISTIC"
                    (sprintf "RecommendedNode %s <> top candidate %s" adv.RecommendedNode adv.Candidates.Head.NodeId)
                let sorted = adv.Candidates |> List.sortWith compareExpected
                if adv.Candidates <> sorted then
                  yield mk "RANKING-MONOTONIC-DETERMINISTIC" "Candidates not strictly ordered by ranking comparator." ]
          let matchingNode =
            record.Nodes
            |> List.tryFind (fun n -> String.Equals(n.NodeId, adv.RecommendedNode, StringComparison.Ordinal))
          let eligibilityViolations =
            match matchingNode with
            | None ->
              [ mk "NO-UNSYNCED-OR-CRITICAL-OR-LOCAL-RECOMMENDATION"
                  (sprintf "Recommended unknown node %s" adv.RecommendedNode) ]
            | Some n ->
              let bad =
                n.IsLocal
                || String.Equals(n.NodeId, record.LocalNodeId, StringComparison.Ordinal)
                || not n.Available
                || n.CheckoutSync <> FleetBalance.CheckoutSyncStatus.Synced
                || n.Pressure = Some MemoryPressure.Critical
                || n.LeaseCap.IsNone
                || n.QueuedLeases <> 0
                || (n.LeaseCap |> Option.exists (fun cap -> n.ActiveLeases >= cap))
              if bad then
                [ mk "NO-UNSYNCED-OR-CRITICAL-OR-LOCAL-RECOMMENDATION"
                    (sprintf "Recommended ineligible node %s (%s)" adv.RecommendedNode (describeNode n)) ]
              else []
          orderViolations @ eligibilityViolations
        | FleetBalance.PlacementDecision.Proceed None
        | FleetBalance.PlacementDecision.BypassedPinned _ -> []
      policyViolations @ advisoryViolations)
    |> List.collect id

  let checkSilence (trace: FleetBalanceSim.Trace) : Violation list =
    trace.SilenceSteps
    |> List.mapi (fun step record ->
      let mk rule detail = { RuleId = rule; Step = step; Detail = detail }
      let workRelevantFor (role: Cohort.JoinableRole) (work: FleetBalance.ActiveWorkContext) =
        match role with
        | Cohort.JoinableRole.Implementer ->
          work.ActiveLeaseCount > 0 || work.OpenClaimCount > 0 || work.DirtyTrancheCount > 0
        | Cohort.JoinableRole.Verifier
        | Cohort.JoinableRole.Observer ->
          work.ActiveLeaseCount > 0 || work.OpenClaimCount > 0 || work.UnauditedRunCount > 0 || work.DirtyTrancheCount > 0

      let auditorViolations =
        record.Members
        |> List.choose (fun m ->
          if m.Present && m.Role = Cohort.JoinableRole.Verifier && workRelevantFor m.Role record.Work then
            let lastSeen = FleetBalance.effectiveLastSeen m
            let elapsed = record.Now - lastSeen
            if elapsed >= record.Threshold then
              let hasAlert =
                record.Evaluation.Alerts
                |> List.exists (fun a -> a.MemberId = m.MemberId && a.Kind = "silent_auditor")
              if not hasAlert then
                Some(mk "SILENT-AUDITOR-VISIBLE"
                  (sprintf "Verifier %s silent %.0fs with leases=%d claims=%d unaudited=%d dirty=%d but no silent_auditor alert"
                    m.MemberId elapsed.TotalSeconds record.Work.ActiveLeaseCount record.Work.OpenClaimCount record.Work.UnauditedRunCount record.Work.DirtyTrancheCount))
              else None
            else None
          else None)

      let expectedNext =
        record.Members
        |> List.filter (fun m -> m.Present && workRelevantFor m.Role record.Work)
        |> List.choose (fun m ->
          let lastSeen = FleetBalance.effectiveLastSeen m
          if (record.Now - lastSeen) < record.Threshold then
            Some(lastSeen + record.Threshold)
          else None)
        |> List.filter (fun dt -> dt > record.Now)
        |> function
           | [] -> None
           | xs -> Some(List.min xs)
      let transitionViolations =
        if record.Evaluation.NextSilenceTransitionUtc <> expectedNext then
          [ mk "NEXT-TRANSITION-EXACT"
              (sprintf "Expected NextSilenceTransitionUtc %s, got %s"
                (describeUtcOpt expectedNext) (describeUtcOpt record.Evaluation.NextSilenceTransitionUtc)) ]
        else []

      auditorViolations @ transitionViolations)
    |> List.collect id

  let checkAll (trace: FleetBalanceSim.Trace) : Violation list =
    checkPlacement trace @ checkSilence trace
