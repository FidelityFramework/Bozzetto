namespace Bozzetto

open System
open System.Globalization
open Fidelity.Data.JSON
open Bozzetto.Cohort
open Bozzetto.MemberTable
open Bozzetto.Measures

/// Explicit schema for the existing cohort ledger JSON. Union tags and arrays
/// retain the recorded adjacent-tag format; no runtime type inspection is used.
module CohortJson =
  let private variant name fields =
    JsonValue.Object (["type", JsonValue.String name]
      @ (if List.isEmpty fields then [] else ["value", JsonValue.Array fields]))

  let private tagged value =
    match value with
    | JsonValue.Object properties ->
      let fields = Map.ofList properties
      match fields.TryFind "type", fields.TryFind "value" with
      | Some (JsonValue.String tag), None -> Ok (tag, [])
      | Some (JsonValue.String tag), Some (JsonValue.Array values) -> Ok (tag, values)
      | _ -> Error "A cohort union needs a string type and an optional value array."
    | _ -> Error "A cohort union must be an object."

  let private text = JsonValue.String
  let private readText = function JsonValue.String value -> Ok value | _ -> Error "Expected a string."
  let private signed (value: int64) = JsonValue.ofInt64 value
  let private readSigned value = match JsonValue.tryAsInt64 value with Some number -> Ok number | None -> Error "Expected an exact integer-spelled Int64."
  let private integer (value: int) = signed (int64 value)
  let private readInteger value =
    readSigned value |> Result.bind (fun number ->
      if number < int64 Int32.MinValue || number > int64 Int32.MaxValue then Error "Integer exceeds Int32."
      else Ok (int number))
  let private fence (value: int64<fence>) = signed (int64 value)
  let private readFence value = readSigned value |> Result.map LanguagePrimitives.Int64WithMeasure<fence>
  let private date (value: DateTime) = text (value.ToString("O", CultureInfo.InvariantCulture))
  let private readDate value =
    readText value |> Result.bind (fun raw ->
      match DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) with
      | true, parsed -> Ok parsed
      | false, _ -> Error "Expected an ISO date-time string.")
  let private array encode values = JsonValue.Array (List.map encode values)
  let private readArray decode = function
    | JsonValue.Array values ->
      values |> List.fold (fun previous value ->
        previous |> Result.bind (fun decoded -> decode value |> Result.map (fun item -> item :: decoded))) (Ok [])
      |> Result.map List.rev
    | _ -> Error "Expected an array."
  let private map2 constructor a b =
    a |> Result.bind (fun a -> b |> Result.map (fun b -> constructor a b))
  let private map3 constructor a b c =
    map2 (fun a b -> a, b) a b |> Result.bind (fun (a, b) -> c |> Result.map (fun c -> constructor a b c))
  let private map4 constructor a b c d =
    map3 (fun a b c -> a, b, c) a b c |> Result.bind (fun (a, b, c) -> d |> Result.map (fun d -> constructor a b c d))

  let memberValue (value: MemberId) =
    match value with
    | MemberId.Browser a -> variant "Browser" [text a]
    | MemberId.Mcp a -> variant "Mcp" [text a]
    | MemberId.Minted a -> variant "Minted" [text a]

  let readMember value : Result<MemberId, string> =
    tagged value |> Result.bind (function
      | "Browser", [a] -> readText a |> Result.map MemberId.Browser
      | "Mcp", [a] -> readText a |> Result.map MemberId.Mcp
      | "Minted", [a] -> readText a |> Result.map MemberId.Minted
      | tag, _ -> Error ("Unknown MemberId case or invalid field count: " + tag))

  let roleValue (value: JoinableRole) =
    match value with
    | JoinableRole.Implementer -> variant "Implementer" []
    | JoinableRole.Verifier -> variant "Verifier" []
    | JoinableRole.Observer -> variant "Observer" []

  let readRole value : Result<JoinableRole, string> =
    tagged value |> Result.bind (function
      | "Implementer", [] -> Ok JoinableRole.Implementer
      | "Verifier", [] -> Ok JoinableRole.Verifier
      | "Observer", [] -> Ok JoinableRole.Observer
      | tag, _ -> Error ("Unknown JoinableRole case or invalid field count: " + tag))

  let scopeValue (value: ClaimScope) =
    match value with
    | ClaimScope.File a -> variant "File" [text a]
    | ClaimScope.Project a -> variant "Project" [text a]

  let readScope value : Result<ClaimScope, string> =
    tagged value |> Result.bind (function
      | "File", [a] -> readText a |> Result.map ClaimScope.File
      | "Project", [a] -> readText a |> Result.map ClaimScope.Project
      | tag, _ -> Error ("Unknown ClaimScope case or invalid field count: " + tag))

  let claimIdValue (value: ClaimId) =
    match value with
    | ClaimId a -> variant "ClaimId" [text a]

  let readClaimId value : Result<ClaimId, string> =
    tagged value |> Result.bind (function
      | "ClaimId", [a] -> readText a |> Result.map ClaimId
      | tag, _ -> Error ("Unknown ClaimId case or invalid field count: " + tag))

  let landingIdValue (value: LandingId) =
    match value with
    | LandingId a -> variant "LandingId" [text a]

  let readLandingId value : Result<LandingId, string> =
    tagged value |> Result.bind (function
      | "LandingId", [a] -> readText a |> Result.map LandingId
      | tag, _ -> Error ("Unknown LandingId case or invalid field count: " + tag))

  let testIdValue (value: TestId) =
    match value with
    | TestId a -> variant "TestId" [text a]

  let readTestId value : Result<TestId, string> =
    tagged value |> Result.bind (function
      | "TestId", [a] -> readText a |> Result.map TestId
      | tag, _ -> Error ("Unknown TestId case or invalid field count: " + tag))

  let nextActionValue (value: NextAction) =
    match value with
    | NextAction.RebaseAndResubmit -> variant "RebaseAndResubmit" []
    | NextAction.AwaitConductor -> variant "AwaitConductor" []
    | NextAction.FixTests a -> variant "FixTests" [(array testIdValue) a]
    | NextAction.Withdraw -> variant "Withdraw" []

  let readNextAction value : Result<NextAction, string> =
    tagged value |> Result.bind (function
      | "RebaseAndResubmit", [] -> Ok NextAction.RebaseAndResubmit
      | "AwaitConductor", [] -> Ok NextAction.AwaitConductor
      | "FixTests", [a] -> (readArray readTestId) a |> Result.map NextAction.FixTests
      | "Withdraw", [] -> Ok NextAction.Withdraw
      | tag, _ -> Error ("Unknown NextAction case or invalid field count: " + tag))

  let blockerValue (value: LandingBlocker<MemberId>) =
    match value with
    | LandingBlocker.RebaseConflict a -> variant "RebaseConflict" [(array text) a]
    | LandingBlocker.FailingTests a -> variant "FailingTests" [(array testIdValue) a]
    | LandingBlocker.StaleClaimFence a -> variant "StaleClaimFence" [claimIdValue a]
    | LandingBlocker.HeadMoved (a, b) -> variant "HeadMoved" [text a; text b]
    | LandingBlocker.VetoedBy (a, b) -> variant "VetoedBy" [memberValue a; text b]
    | LandingBlocker.Inconclusive a -> variant "Inconclusive" [text a]

  let readBlocker value : Result<LandingBlocker<MemberId>, string> =
    tagged value |> Result.bind (function
      | "RebaseConflict", [a] -> (readArray readText) a |> Result.map LandingBlocker.RebaseConflict
      | "FailingTests", [a] -> (readArray readTestId) a |> Result.map LandingBlocker.FailingTests
      | "StaleClaimFence", [a] -> readClaimId a |> Result.map LandingBlocker.StaleClaimFence
      | "HeadMoved", [a; b] -> map2 (fun a b -> LandingBlocker.HeadMoved(a, b)) (readText a) (readText b)
      | "VetoedBy", [a; b] -> map2 (fun a b -> LandingBlocker.VetoedBy(a, b)) (readMember a) (readText b)
      | "Inconclusive", [a] -> readText a |> Result.map LandingBlocker.Inconclusive
      | tag, _ -> Error ("Unknown LandingBlocker case or invalid field count: " + tag))

  let landingStateValue (value: LandingState<MemberId>) =
    match value with
    | LandingState.Queued -> variant "Queued" []
    | LandingState.Rebasing a -> variant "Rebasing" [text a]
    | LandingState.Verifying (a, b, c, d) -> variant "Verifying" [text a; text b; integer c; integer d]
    | LandingState.Blocked (a, b) -> variant "Blocked" [blockerValue a; nextActionValue b]
    | LandingState.Landed a -> variant "Landed" [text a]
    | LandingState.Withdrawn -> variant "Withdrawn" []

  let readLandingState value : Result<LandingState<MemberId>, string> =
    tagged value |> Result.bind (function
      | "Queued", [] -> Ok LandingState.Queued
      | "Rebasing", [a] -> readText a |> Result.map LandingState.Rebasing
      | "Verifying", [a; b; c; d] -> map4 (fun a b c d -> LandingState.Verifying(a, b, c, d)) (readText a) (readText b) (readInteger c) (readInteger d)
      | "Blocked", [a; b] -> map2 (fun a b -> LandingState.Blocked(a, b)) (readBlocker a) (readNextAction b)
      | "Landed", [a] -> readText a |> Result.map LandingState.Landed
      | "Withdrawn", [] -> Ok LandingState.Withdrawn
      | tag, _ -> Error ("Unknown LandingState case or invalid field count: " + tag))

  let pruneReasonValue (value: ClaimPruneReason) =
    match value with
    | ClaimPruneReason.OrphanedPastRetention a -> variant "OrphanedPastRetention" [date a]
    | ClaimPruneReason.ReleasedPastRetention a -> variant "ReleasedPastRetention" [date a]
    | ClaimPruneReason.SupersededBy a -> variant "SupersededBy" [claimIdValue a]

  let readPruneReason value : Result<ClaimPruneReason, string> =
    tagged value |> Result.bind (function
      | "OrphanedPastRetention", [a] -> readDate a |> Result.map ClaimPruneReason.OrphanedPastRetention
      | "ReleasedPastRetention", [a] -> readDate a |> Result.map ClaimPruneReason.ReleasedPastRetention
      | "SupersededBy", [a] -> readClaimId a |> Result.map ClaimPruneReason.SupersededBy
      | tag, _ -> Error ("Unknown ClaimPruneReason case or invalid field count: " + tag))

  let private session = function
    | Some value -> variant "Some" [text value]
    | None -> variant "None" []
  let private readSession value =
    tagged value |> Result.bind (function
      | "Some", [value] -> readText value |> Result.map Some
      | "None", [] -> Ok None
      | _ -> Error "Invalid optional session identity.")
  let private claims values =
    values |> array (fun (claim, stamp) -> JsonValue.Array [claimIdValue claim; fence stamp])
  let private readClaims value =
    value |> readArray (function
      | JsonValue.Array [claim; stamp] -> map2 (fun claim stamp -> claim, stamp) (readClaimId claim) (readFence stamp)
      | _ -> Error "A claim fence requires a two-element array.")
  let private rebaseResult = function
    | Ok sha -> variant "Ok" [text sha]
    | Error files -> variant "Error" [array text files]
  let private readRebaseResult value =
    tagged value |> Result.bind (function
      | "Ok", [sha] -> readText sha |> Result.map Ok
      | "Error", [files] -> readArray readText files |> Result.map Error
      | _ -> Error "Invalid rebase result.")

  let commandValue (value: CohortCommand<MemberId>) =
    match value with
    | CohortCommand.Join (a, b, c) -> variant "Join" [memberValue a; roleValue b; session c]
    | CohortCommand.Depart a -> variant "Depart" [memberValue a]
    | CohortCommand.RenewLease a -> variant "RenewLease" [memberValue a]
    | CohortCommand.Tick -> variant "Tick" []
    | CohortCommand.AcquireClaim (a, b, c) -> variant "AcquireClaim" [memberValue a; scopeValue b; text c]
    | CohortCommand.ReleaseClaim (a, b, c) -> variant "ReleaseClaim" [memberValue a; claimIdValue b; fence c]
    | CohortCommand.ReassignClaim (a, b, c) -> variant "ReassignClaim" [memberValue a; claimIdValue b; memberValue c]
    | CohortCommand.DelegateConductor (a, b) -> variant "DelegateConductor" [memberValue a; memberValue b]
    | CohortCommand.ObserveSave (a, b) -> variant "ObserveSave" [memberValue a; text b]
    | CohortCommand.RequestLanding (a, b, c, d) -> variant "RequestLanding" [memberValue a; claims b; (array text) c; text d]
    | CohortCommand.RebaseCompleted (a, b) -> variant "RebaseCompleted" [landingIdValue a; rebaseResult b]
    | CohortCommand.AffectedComputed (a, b) -> variant "AffectedComputed" [landingIdValue a; (array testIdValue) b]
    | CohortCommand.TestsCompleted (a, b) -> variant "TestsCompleted" [landingIdValue a; (array testIdValue) b]
    | CohortCommand.VerificationInconclusive (a, b) -> variant "VerificationInconclusive" [landingIdValue a; text b]
    | CohortCommand.FastForwardCompleted (a, b) -> variant "FastForwardCompleted" [landingIdValue a; text b]
    | CohortCommand.FastForwardFailed (a, b) -> variant "FastForwardFailed" [landingIdValue a; text b]
    | CohortCommand.WithdrawLanding (a, b) -> variant "WithdrawLanding" [memberValue a; landingIdValue b]
    | CohortCommand.VetoLanding (a, b, c) -> variant "VetoLanding" [memberValue a; landingIdValue b; text c]
    | CohortCommand.SetIntegrationHead (a, b) -> variant "SetIntegrationHead" [memberValue a; text b]
    | CohortCommand.ResolveVeto (a, b) -> variant "ResolveVeto" [memberValue a; landingIdValue b]

  let readCommand value : Result<CohortCommand<MemberId>, string> =
    tagged value |> Result.bind (function
      | "Join", [a; b; c] -> map3 (fun a b c -> CohortCommand.Join(a, b, c)) (readMember a) (readRole b) (readSession c)
      | "Depart", [a] -> readMember a |> Result.map CohortCommand.Depart
      | "RenewLease", [a] -> readMember a |> Result.map CohortCommand.RenewLease
      | "Tick", [] -> Ok CohortCommand.Tick
      | "AcquireClaim", [a; b; c] -> map3 (fun a b c -> CohortCommand.AcquireClaim(a, b, c)) (readMember a) (readScope b) (readText c)
      | "ReleaseClaim", [a; b; c] -> map3 (fun a b c -> CohortCommand.ReleaseClaim(a, b, c)) (readMember a) (readClaimId b) (readFence c)
      | "ReassignClaim", [a; b; c] -> map3 (fun a b c -> CohortCommand.ReassignClaim(a, b, c)) (readMember a) (readClaimId b) (readMember c)
      | "DelegateConductor", [a; b] -> map2 (fun a b -> CohortCommand.DelegateConductor(a, b)) (readMember a) (readMember b)
      | "ObserveSave", [a; b] -> map2 (fun a b -> CohortCommand.ObserveSave(a, b)) (readMember a) (readText b)
      | "RequestLanding", [a; b; c; d] -> map4 (fun a b c d -> CohortCommand.RequestLanding(a, b, c, d)) (readMember a) (readClaims b) ((readArray readText) c) (readText d)
      | "RebaseCompleted", [a; b] -> map2 (fun a b -> CohortCommand.RebaseCompleted(a, b)) (readLandingId a) (readRebaseResult b)
      | "AffectedComputed", [a; b] -> map2 (fun a b -> CohortCommand.AffectedComputed(a, b)) (readLandingId a) ((readArray readTestId) b)
      | "TestsCompleted", [a; b] -> map2 (fun a b -> CohortCommand.TestsCompleted(a, b)) (readLandingId a) ((readArray readTestId) b)
      | "VerificationInconclusive", [a; b] -> map2 (fun a b -> CohortCommand.VerificationInconclusive(a, b)) (readLandingId a) (readText b)
      | "FastForwardCompleted", [a; b] -> map2 (fun a b -> CohortCommand.FastForwardCompleted(a, b)) (readLandingId a) (readText b)
      | "FastForwardFailed", [a; b] -> map2 (fun a b -> CohortCommand.FastForwardFailed(a, b)) (readLandingId a) (readText b)
      | "WithdrawLanding", [a; b] -> map2 (fun a b -> CohortCommand.WithdrawLanding(a, b)) (readMember a) (readLandingId b)
      | "VetoLanding", [a; b; c] -> map3 (fun a b c -> CohortCommand.VetoLanding(a, b, c)) (readMember a) (readLandingId b) (readText c)
      | "SetIntegrationHead", [a; b] -> map2 (fun a b -> CohortCommand.SetIntegrationHead(a, b)) (readMember a) (readText b)
      | "ResolveVeto", [a; b] -> map2 (fun a b -> CohortCommand.ResolveVeto(a, b)) (readMember a) (readLandingId b)
      | tag, _ -> Error ("Unknown CohortCommand case or invalid field count: " + tag))

  let eventValue (value: CohortEvent<MemberId>) =
    match value with
    | CohortEvent.MemberJoined (a, b, c) -> variant "MemberJoined" [memberValue a; roleValue b; session c]
    | CohortEvent.MemberDeparted (a, b) -> variant "MemberDeparted" [memberValue a; date b]
    | CohortEvent.LeaseRenewed a -> variant "LeaseRenewed" [memberValue a]
    | CohortEvent.ConductorBound a -> variant "ConductorBound" [memberValue a]
    | CohortEvent.ConductorDelegated (a, b) -> variant "ConductorDelegated" [memberValue a; memberValue b]
    | CohortEvent.ClaimAcquired (a, b, c, d) -> variant "ClaimAcquired" [claimIdValue a; scopeValue b; memberValue c; fence d]
    | CohortEvent.ClaimReleased (a, b, c) -> variant "ClaimReleased" [claimIdValue a; memberValue b; fence c]
    | CohortEvent.ClaimOrphaned (a, b, c) -> variant "ClaimOrphaned" [claimIdValue a; memberValue b; fence c]
    | CohortEvent.ClaimReassigned (a, b, c) -> variant "ClaimReassigned" [claimIdValue a; memberValue b; fence c]
    | CohortEvent.ClaimViolationObserved (a, b, c, d) -> variant "ClaimViolationObserved" [claimIdValue a; memberValue b; memberValue c; text d]
    | CohortEvent.LandingQueued (a, b) -> variant "LandingQueued" [landingIdValue a; memberValue b]
    | CohortEvent.LandingStateChanged (a, b) -> variant "LandingStateChanged" [landingIdValue a; landingStateValue b]
    | CohortEvent.LandingLanded (a, b) -> variant "LandingLanded" [landingIdValue a; text b]
    | CohortEvent.LandingWithdrawn a -> variant "LandingWithdrawn" [landingIdValue a]
    | CohortEvent.LandingVetoed (a, b, c) -> variant "LandingVetoed" [landingIdValue a; memberValue b; text c]
    | CohortEvent.IntegrationConfigured a -> variant "IntegrationConfigured" [text a]
    | CohortEvent.LandingVetoResolved (a, b) -> variant "LandingVetoResolved" [landingIdValue a; memberValue b]
    | CohortEvent.ClaimPruned (a, b) -> variant "ClaimPruned" [claimIdValue a; pruneReasonValue b]
    | CohortEvent.LandingPruned (a, b) -> variant "LandingPruned" [landingIdValue a; date b]
    | CohortEvent.MemberPurged (a, b) -> variant "MemberPurged" [memberValue a; date b]

  let readEvent value : Result<CohortEvent<MemberId>, string> =
    tagged value |> Result.bind (function
      | "MemberJoined", [a; b; c] -> map3 (fun a b c -> CohortEvent.MemberJoined(a, b, c)) (readMember a) (readRole b) (readSession c)
      | "MemberDeparted", [a; b] -> map2 (fun a b -> CohortEvent.MemberDeparted(a, b)) (readMember a) (readDate b)
      | "LeaseRenewed", [a] -> readMember a |> Result.map CohortEvent.LeaseRenewed
      | "ConductorBound", [a] -> readMember a |> Result.map CohortEvent.ConductorBound
      | "ConductorDelegated", [a; b] -> map2 (fun a b -> CohortEvent.ConductorDelegated(a, b)) (readMember a) (readMember b)
      | "ClaimAcquired", [a; b; c; d] -> map4 (fun a b c d -> CohortEvent.ClaimAcquired(a, b, c, d)) (readClaimId a) (readScope b) (readMember c) (readFence d)
      | "ClaimReleased", [a; b; c] -> map3 (fun a b c -> CohortEvent.ClaimReleased(a, b, c)) (readClaimId a) (readMember b) (readFence c)
      | "ClaimOrphaned", [a; b; c] -> map3 (fun a b c -> CohortEvent.ClaimOrphaned(a, b, c)) (readClaimId a) (readMember b) (readFence c)
      | "ClaimReassigned", [a; b; c] -> map3 (fun a b c -> CohortEvent.ClaimReassigned(a, b, c)) (readClaimId a) (readMember b) (readFence c)
      | "ClaimViolationObserved", [a; b; c; d] -> map4 (fun a b c d -> CohortEvent.ClaimViolationObserved(a, b, c, d)) (readClaimId a) (readMember b) (readMember c) (readText d)
      | "LandingQueued", [a; b] -> map2 (fun a b -> CohortEvent.LandingQueued(a, b)) (readLandingId a) (readMember b)
      | "LandingStateChanged", [a; b] -> map2 (fun a b -> CohortEvent.LandingStateChanged(a, b)) (readLandingId a) (readLandingState b)
      | "LandingLanded", [a; b] -> map2 (fun a b -> CohortEvent.LandingLanded(a, b)) (readLandingId a) (readText b)
      | "LandingWithdrawn", [a] -> readLandingId a |> Result.map CohortEvent.LandingWithdrawn
      | "LandingVetoed", [a; b; c] -> map3 (fun a b c -> CohortEvent.LandingVetoed(a, b, c)) (readLandingId a) (readMember b) (readText c)
      | "IntegrationConfigured", [a] -> readText a |> Result.map CohortEvent.IntegrationConfigured
      | "LandingVetoResolved", [a; b] -> map2 (fun a b -> CohortEvent.LandingVetoResolved(a, b)) (readLandingId a) (readMember b)
      | "ClaimPruned", [a; b] -> map2 (fun a b -> CohortEvent.ClaimPruned(a, b)) (readClaimId a) (readPruneReason b)
      | "LandingPruned", [a; b] -> map2 (fun a b -> CohortEvent.LandingPruned(a, b)) (readLandingId a) (readDate b)
      | "MemberPurged", [a; b] -> map2 (fun a b -> CohortEvent.MemberPurged(a, b)) (readMember a) (readDate b)
      | tag, _ -> Error ("Unknown CohortEvent case or invalid field count: " + tag))

  let entryValue (entry: LedgerEntry<MemberId>) =
    JsonValue.Object [
      "seq", signed (int64 entry.Seq)
      "clock", date entry.Clock
      "entropy", text (Convert.ToBase64String entry.Entropy)
      "command", commandValue entry.Command
      "events", array eventValue entry.Events
    ]

  let readEntry value =
    match value with
    | JsonValue.Object properties ->
      let fields = Map.ofList properties
      let field name decode =
        match fields.TryFind name with
        | Some value -> decode value
        | None -> Error ("Missing cohort ledger field: " + name)
      let entropy = field "entropy" readText |> Result.bind (fun value ->
        try Ok (Convert.FromBase64String value)
        with :? FormatException -> Error "Invalid base64 ledger entropy.")
      map4 (fun sequence clock entropy command -> sequence, clock, entropy, command)
        (field "seq" readSigned) (field "clock" readDate) entropy (field "command" readCommand)
      |> Result.bind (fun (sequence, clock, entropy, command) ->
        field "events" (readArray readEvent) |> Result.map (fun events ->
          { Seq = LanguagePrimitives.Int64WithMeasure<ledgerSeq> sequence
            Clock = clock; Entropy = entropy; Command = command; Events = events }))
    | _ -> Error "A cohort ledger entry must be an object."

  let commandToJson value = commandValue value |> Json.serialize
  let commandFromJson json = Json.parse json |> Result.bind readCommand
  let eventsToJson values = array eventValue values |> Json.serialize
  let eventsFromJson json = Json.parse json |> Result.bind (readArray readEvent)
  let entryToJson value = entryValue value |> Json.serialize
  let entryFromJson json = Json.parse json |> Result.bind readEntry
