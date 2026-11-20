namespace Bozzetto

open System

module internal McpCohortHelpers =

  let cohortErrorToBozzettoError (err: Cohort.CohortError<MemberTable.MemberId>) : BozzettoError =
    let mid = MemberTable.MemberId.display
    let scopeStr (scope: Cohort.ClaimScope) =
      match scope with
      | Cohort.ClaimScope.File f -> sprintf "file:%s" f
      | Cohort.ClaimScope.Project p -> sprintf "project:%s" p
    let failed reason suggestion = BozzettoError.CohortActionFailed(reason, suggestion)
    match err with
    | Cohort.CohortError.DuplicateJoin who ->
      failed
        (sprintf "%s is already a member of this cohort." (mid who))
        "Run get_cohort_status to see your current role; there is no need to join again."
    | Cohort.CohortError.MemberNotPresent who ->
      failed
        (sprintf "%s is not a present member of this cohort." (mid who))
        "Run join_cohort before acting in the cohort."
    | Cohort.CohortError.ClaimConflict(scope, holder) ->
      failed
        (sprintf "The scope %s is already claimed by %s." (scopeStr scope) (mid holder))
        "Coordinate with the current holder, or acquire a different, non-overlapping scope."
    | Cohort.CohortError.OrphanedClaimConflict(scope, Cohort.ClaimId cid, previous) ->
      failed
        (sprintf "The scope %s overlaps orphaned claim %s, last held by %s; an orphaned claim still blocks its scope." (scopeStr scope) cid (mid previous))
        "Ask the cohort conductor to reassign_claim it, or its previous holder to rejoin and reclaim_claim it with its current fence."
    | Cohort.CohortError.NotClaimHolder(Cohort.ClaimId cid, requester) ->
      failed
        (sprintf "%s does not hold claim %s." (mid requester) cid)
        "Acquire it with acquire_claim, or ask the current holder to release it."
    | Cohort.CohortError.UnknownClaim(Cohort.ClaimId cid) ->
      failed
        (sprintf "No claim %s exists in this cohort." cid)
        "Run get_cohort_status to list the current claims and their ids."
    | Cohort.CohortError.ClaimNotOrphaned(Cohort.ClaimId cid) ->
      failed
        (sprintf "Claim %s is not orphaned, so it cannot be reassigned." cid)
        "Only a claim whose holder has departed can be reassigned; check get_cohort_status."
    | Cohort.CohortError.DuplicateClaimId(Cohort.ClaimId cid) ->
      failed
        (sprintf "A claim with id %s already exists." cid)
        "Retry the acquire; claim ids are minted per acquire_claim."
    | Cohort.CohortError.StaleClaimFence(Cohort.ClaimId cid, presented, current) ->
      failed
        (sprintf "Your fence for claim %s is stale — you presented %d but the current fence is %d." cid (int64 presented) (int64 current))
        "Re-read get_cohort_status and retry with the claim's current fence."
    | Cohort.CohortError.InvalidPurpose reason ->
      failed
        (sprintf "The claim purpose is invalid: %s" reason)
        "Provide a non-empty purpose describing why you are claiming the scope."
    | Cohort.CohortError.InvalidStatement reason ->
      failed
        (sprintf "The landing statement is invalid: %s" reason)
        "Provide a non-empty statement describing what this landing changes."
    | Cohort.CohortError.UnknownLanding(Cohort.LandingId lid) ->
      failed
        (sprintf "No landing %s exists in this cohort." lid)
        "Run get_cohort_status to list the current landings."
    | Cohort.CohortError.DuplicateLandingId(Cohort.LandingId lid) ->
      failed
        (sprintf "A landing with id %s already exists." lid)
        "Retry the request; landing ids are minted per request_landing."
    | Cohort.CohortError.NotLandingRequester(Cohort.LandingId lid, who) ->
      failed
        (sprintf "%s did not request landing %s." (mid who) lid)
        "Only the landing's requester can act on it; check get_cohort_status."
    | Cohort.CohortError.LandingNotAtFrontOfQueue(Cohort.LandingId lid) ->
      failed
        (sprintf "Landing %s is not at the front of the landing queue." lid)
        "Landings are strictly serial; wait until the earlier landings ahead of it complete."
    | Cohort.CohortError.LandingNotInExpectedState(Cohort.LandingId lid, expected) ->
      failed
        (sprintf "Landing %s is not in the expected state (%s)." lid expected)
        "Run get_cohort_status to see the landing's current state before acting on it."
    | Cohort.CohortError.NotConductor who ->
      failed
        (sprintf "This is a conductor-only action, and %s is not the cohort conductor." (mid who))
        "Ask the cohort conductor to perform it, or have the conductor delegate the role to you."
    | Cohort.CohortError.ConductorNotPromotable conductor ->
      failed
        (sprintf "The conductor seat (%s) is not vacant or has no present member to take it." (conductor |> Option.map mid |> Option.defaultValue "unbound"))
        "Run get_cohort_status; the daemon promotes a present member when the conductor departs."
    | Cohort.CohortError.NoExpiredOrphanedClaims ->
      failed "No orphaned claims are eligible for expiry." "Run get_cohort_status to inspect the current claims."
    | Cohort.CohortError.LedgerAppendFailed ->
      failed "The cohort ledger could not commit the command." "Check the daemon log before retrying."

  let parseJoinableRole (raw: string) : Result<Cohort.JoinableRole, BozzettoError> =
    match (if isNull raw then "" else raw.Trim().ToLowerInvariant()) with
    | "implementer" -> Ok Cohort.JoinableRole.Implementer
    | "verifier" -> Ok Cohort.JoinableRole.Verifier
    | "observer" -> Ok Cohort.JoinableRole.Observer
    | other -> Error (BozzettoError.SessionCreationFailed (sprintf "unknown cohort role '%s' — expected Implementer, Verifier, or Observer" other))

  let parseClaimScope (raw: string) : Result<Cohort.ClaimScope, BozzettoError> =
    let raw = if isNull raw then "" else raw.Trim()
    match raw.IndexOf ':' with
    | -1 -> Error (BozzettoError.SessionCreationFailed (sprintf "claim scope '%s' must be 'file:<path>' or 'project:<path>'" raw))
    | i ->
      let kind = raw.Substring(0, i).Trim().ToLowerInvariant()
      let path = raw.Substring(i + 1).Trim()
      if path = "" then Error (BozzettoError.SessionCreationFailed "claim scope path is empty")
      else
        match kind with
        | "file" -> Ok (Cohort.ClaimScope.File path)
        | "project" -> Ok (Cohort.ClaimScope.Project path)
        | other -> Error (BozzettoError.SessionCreationFailed (sprintf "unknown claim scope kind '%s' — expected 'file' or 'project'" other))

  let parseClaimFenceList (raw: string) : Result<(Cohort.ClaimId * int64<Measures.fence>) list, BozzettoError> =
    let raw = if isNull raw then "" else raw.Trim()
    if raw = "" then Ok []
    else
      raw.Split(',')
      |> Array.toList
      |> List.map (fun pair ->
        match pair.Trim().Split(':') with
        | [| cid; fenceStr |] when cid.Trim() <> "" ->
          match Int64.TryParse(fenceStr.Trim()) with
          | true, f -> Ok (Cohort.ClaimId (cid.Trim()), LanguagePrimitives.Int64WithMeasure<Measures.fence> f)
          | false, _ -> Error (BozzettoError.SessionCreationFailed (sprintf "claim fence '%s' is not an integer in '%s'" fenceStr pair))
        | _ -> Error (BozzettoError.SessionCreationFailed (sprintf "expected 'claimId:fence', got '%s'" pair)))
      |> List.fold (fun acc item ->
        match acc, item with
        | Error e, _ -> Error e
        | Ok _, Error e -> Error e
        | Ok xs, Ok x -> Ok (xs @ [ x ]))
        (Ok [])
