/// Slice 1 of cohort-integration-plan.md: `CohortOwner` is the shell actor
/// around the pure `Cohort.decide`/`replay` core (Phase 1 item 7). These
/// tests cover the owner's own contract — command application publishes a
/// read frame, every accepted command lands one dense ledger entry, a fresh
/// owner replaying the same ledger reconstructs an identical frame (the
/// "ledger is the source of truth" property), and a refused command changes
/// neither the frame nor the ledger.
module Bozzetto.Tests.CohortOwnerTests

open System
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Cohort
open Bozzetto.Measures
open Bozzetto.MemberTable
open Bozzetto.Features
open Bozzetto.Features.CohortLedger

let private silentLogger =
  { new Bozzetto.Utils.ILogger with
      member _.LogInfo _ = ()
      member _.LogDebug _ = ()
      member _.LogWarning _ = ()
      member _.LogError _ = () }

let private epoch = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)

/// A deterministic clock so ledger/frame equality checks across two owner
/// instances don't depend on wall-clock timing.
let private fixedClock (at: DateTime) : unit -> DateTime = fun () -> at

/// Deterministic, distinct entropy per call — real CohortOwner.decide calls
/// mint claim/landing ids from this, so distinct values matter for realistic
/// coverage even though these tests don't inspect minted ids directly.
let private counterEntropy () : unit -> byte[] =
  let mutable n = 0
  fun () ->
    let bytes = BitConverter.GetBytes n
    n <- n + 1
    bytes

let private alice = MemberId.Minted "alice"
let private bob = MemberId.Minted "bob"

[<Tests>]
let cohortOwnerTests =
  testList "CohortOwner" [

    testTask "joining publishes the member in the read frame" {
      let ledger = InMemory.create<MemberId> ()
      use owner = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
      let! result = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
      result |> Result.isOk |> Expect.isTrue "the join is accepted"
      owner.ReadFrame().MemberIds
      |> Array.contains alice
      |> Expect.isTrue "the member appears in the frame"
    }

    testTask "N accepted commands append N dense, increasing ledger entries" {
      let ledger = InMemory.create<MemberId> ()
      use owner = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
      let! _ = owner.Commit(CohortCommand.Join(bob, JoinableRole.Verifier, None))
      let! _ = owner.Commit(CohortCommand.RenewLease alice)
      let entries = ledger.ReadAll ()
      entries |> List.length |> Expect.equal "three accepted commands, three entries" 3
      entries
      |> List.map (fun e -> e.Seq)
      |> Expect.equal "seq is dense and increasing from 0" [ 0L<ledgerSeq>; 1L<ledgerSeq>; 2L<ledgerSeq> ]
    }

    testTask "a fresh owner replaying the same ledger reconstructs an identical frame" {
      let ledger = InMemory.create<MemberId> ()
      use ownerA = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
      let! _ = ownerA.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
      let! _ = ownerA.Commit(CohortCommand.Join(bob, JoinableRole.Verifier, None))
      let! _ = ownerA.Commit(CohortCommand.AcquireClaim(alice, ClaimScope.File "A.fs", "working on A"))
      let frameA = ownerA.ReadFrame()
      use ownerB = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
      let frameB = ownerB.ReadFrame()
      frameB |> Expect.equal "replay over the same ledger reconstructs an identical frame" frameA
    }

    testTask "a refused command leaves the frame and the ledger unchanged" {
      let ledger = InMemory.create<MemberId> ()
      use owner = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
      let frameBefore = owner.ReadFrame()
      let entriesBefore = ledger.ReadAll ()
      // alice never acquired this claim id, so releasing it is refused.
      let! result = owner.Commit(CohortCommand.ReleaseClaim(alice, ClaimId "nope", 0L<fence>))
      match result with
      | Error(CohortError.UnknownClaim _) -> ()
      | other -> failtestf "expected UnknownClaim, got %A" other
      owner.ReadFrame() |> Expect.equal "the frame is unchanged by a refused command" frameBefore
      ledger.ReadAll () |> Expect.equal "nothing new was appended for a refused command" entriesBefore
    }

    testTask "a command refused for one member does not let it touch another member's claim" {
      let ledger = InMemory.create<MemberId> ()
      use owner = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
      let! _ = owner.Commit(CohortCommand.Join(bob, JoinableRole.Verifier, None))
      let! _ = owner.Commit(CohortCommand.AcquireClaim(alice, ClaimScope.File "Shared.fs", "purpose"))
      let claimId =
        owner.ReadFrame().ClaimIds
        |> Array.tryHead
        |> Option.defaultWith (fun () -> failtest "expected one claim to exist")
      let fence = owner.ReadFrame().ClaimFence.[0]
      let! result = owner.Commit(CohortCommand.ReleaseClaim(bob, claimId, fence))
      match result with
      | Error(CohortError.NotClaimHolder(errClaimId, requester)) ->
        errClaimId |> Expect.equal "the refusal names the contested claim" claimId
        requester |> Expect.equal "the refusal names the non-holder who tried" bob
      | other -> failtestf "expected NotClaimHolder, got %A" other
      let frameAfter = owner.ReadFrame()
      let aliceIndex = frameAfter.MemberIds |> Array.findIndex (fun m -> m = alice)
      frameAfter.ClaimHolderIndex.[0]
      |> Expect.equal "the claim is still held by alice, not bob" aliceIndex
    }

    testTask "WHY — a member joined WITH a session shows that session's outcomes in the frame (item 13c)" {
      let ledger = InMemory.create<MemberId> ()
      let t1 = Cohort.TestId "t1"
      let t2 = Cohort.TestId "t2"
      let outcomesFor (sid: string) : CohortOwner.SessionTestOutcomes =
        if sid = "sess-1" then [ t1 ], [ t2 ], [], 7L else [], [], [], 0L
      use owner = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) outcomesFor
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, Some "sess-1"))
      let frame = owner.ReadFrame()
      frame.TestIds |> Expect.equal "TestIds is the distinct, sorted union of every bound session's tests" [| t1; t2 |]
      frame.SessionGens |> Expect.equal "SessionGens carries the joined member's session generation" [| 7L |]
      frame.Pass.[0] |> Expect.equal "the member's row passes t1, not t2" [| true; false |]
      frame.Fail.[0] |> Expect.equal "the member's row fails t2, not t1" [| false; true |]
      frame.Stale.[0] |> Expect.equal "the member's row has no stale tests" [| false; false |]
    }

    testTask "WHY — a member joined WITHOUT a session contributes no row to the matrix" {
      let ledger = InMemory.create<MemberId> ()
      let t1 = Cohort.TestId "should-never-appear"
      // If frameOf ever attributed a row to a session-less member, this
      // outcome would leak into the frame even though nothing bound "sess-1".
      let outcomesFor (_: string) : CohortOwner.SessionTestOutcomes = [ t1 ], [], [], 1L
      use owner = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) outcomesFor
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
      owner.ReadFrame().TestIds
      |> Expect.equal "no session means no row, so no tests appear" [||]
    }

    testTask "getSessionTestOutcomes is re-read on every applied command, not cached from startup" {
      let ledger = InMemory.create<MemberId> ()
      let t1 = Cohort.TestId "t1"
      let live: (Cohort.TestId list * Cohort.TestId list * Cohort.TestId list * int64) ref = ref ([], [], [], 0L)
      use owner = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> live.Value)
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, Some "sess-1"))
      owner.ReadFrame().TestIds
      |> Expect.equal "no outcomes were supplied at startup" [||]
      live.Value <- [ t1 ], [], [], 1L
      let! _ = owner.Commit(CohortCommand.Join(bob, JoinableRole.Verifier, Some "sess-2"))
      owner.ReadFrame().TestIds
      |> Expect.equal "the next applied command re-reads getSessionTestOutcomes and picks up the new value" [| t1 |]
    }

    testTask "WHY — replay preserves the joined member's session (item 13c)" {
      let ledger = InMemory.create<MemberId> ()
      use ownerA = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) (fun _ -> ([], [], [], 0L))
      let! _ = ownerA.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, Some "sess-1"))
      // A fresh owner over the same ledger must reconstruct the SAME
      // Session-bound member — proving `replay` (which folds `decide` over
      // the recorded commands, Cohort.fs) round-trips `MemberRecord.Session`
      // via the ledger alone, not via any in-memory state carried forward.
      let t1 = Cohort.TestId "t1"
      let outcomesFor (sid: string) : CohortOwner.SessionTestOutcomes =
        if sid = "sess-1" then [ t1 ], [], [], 3L else [], [], [], 0L
      use ownerB = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy ()) outcomesFor
      ownerB.ReadFrame().TestIds
      |> Expect.equal "replay reconstructed alice's session binding, so her outcomes populate the frame" [| t1 |]
    }
  ]

let private report : AgentWork.Report = {
  Run = "run-1"; Session = "pi-1"; Sequence = 1L; Name = "same-name"; Model = "reported-model"
  Endpoint = "LAN adviser"; Project = "/repo"; Focus = "visibility"; Operation = "reading"; Status = "running"
  Usage = Some { Input = 1L; Output = 2L; CacheRead = 3L; CacheWrite = 4L; Total = 10L; EstimateUsd = None } }
let private accepted = function Ok state -> state | Error error -> failtestf "unexpected refusal %A" error

[<Tests>]
let agentWorkTests =
  testList "Agent work" [
    test "positive cumulative reporting, idempotence and same names preserve identities" {
      let a = MemberId.Mcp "connection-a"
      let b = MemberId.Mcp "connection-b"
      let first = AgentWork.report epoch a JoinableRole.Implementer report AgentWork.empty |> accepted
      AgentWork.report epoch a JoinableRole.Implementer report first |> accepted |> Expect.equal "duplicate is exactly idempotent" first
      let second = AgentWork.report epoch b JoinableRole.Observer { report with Run = "run-2" } first |> accepted
      second.Runs.Count |> Expect.equal "same names do not merge connections" 2
      second.Runs.["run-2"].Role |> Expect.equal "independent role attribution" JoinableRole.Observer
      second.Runs.["run-1"].Report.Usage.Value.Total |> Expect.equal "duplicate usage never added" 10L
    }
    test "specific refusals leave immutable state untouched" {
      let first = AgentWork.report epoch alice JoinableRole.Implementer report AgentWork.empty |> accepted
      let cases = [
        bob, { report with Sequence = 2L }, AgentWork.WrongMember
        alice, { report with Sequence = 0L }, AgentWork.InvalidField "sequence"
        alice, { report with Sequence = 1L; Focus = "changed" }, AgentWork.ConflictingDuplicate
        alice, { report with Sequence = 2L; Focus = String.replicate 257 "x" }, AgentWork.OversizedField "focus"
        alice, { report with Sequence = 2L; Usage = None }, AgentWork.RegressingUsage
        alice, { report with Sequence = 2L; Usage = Some { report.Usage.Value with Total = 9L } }, AgentWork.InvalidUsage
        alice, { report with Sequence = 2L; Usage = Some { report.Usage.Value with EstimateUsd = Some nan } }, AgentWork.InvalidUsage
        alice, { report with Sequence = 2L; Status = "green" }, AgentWork.InvalidField "status"
        alice, { report with Sequence = 2L; Name = "replacement" }, AgentWork.InvalidField "name"
        alice, { report with Sequence = 2L; Session = "replacement" }, AgentWork.InvalidField "session"
        alice, { report with Sequence = 2L; Endpoint = "https://user:secret@host" }, AgentWork.InvalidField "endpoint"
        alice, { report with Sequence = 2L; Operation = "raw\noutput" }, AgentWork.InvalidField "operation"
        alice, { report with Sequence = 2L; Usage = Some { report.Usage.Value with Input = 0L; Total = 9L } }, AgentWork.RegressingUsage ]
      for who, candidate, expected in cases do
        AgentWork.report epoch who JoinableRole.Implementer candidate first |> Expect.equal "specific reason" (Error expected)
      let newer = AgentWork.report epoch alice JoinableRole.Implementer { report with Sequence = 3L } first |> accepted
      AgentWork.report epoch alice JoinableRole.Implementer { report with Sequence = 2L } newer |> Expect.equal "out of order is not duplicate" (Error AgentWork.StaleSequence)
      let finished = AgentWork.report epoch alice JoinableRole.Implementer { report with Sequence = 4L; Status = "completed" } newer |> accepted
      AgentWork.report epoch alice JoinableRole.Implementer { report with Sequence = 5L } finished |> Expect.equal "cannot revive terminal run" (Error AgentWork.TerminalRun)
    }
    test "bounded activity, known zero, missing usage and capacity" {
      let zero = { report.Usage.Value with Input = 0L; Output = 0L; CacheRead = 0L; CacheWrite = 0L; Total = 0L; EstimateUsd = Some 0. }
      let first = AgentWork.report epoch alice JoinableRole.Implementer { report with Usage = Some zero } AgentWork.empty |> accepted
      let latest = (first, [2L..40L]) ||> List.fold (fun state seq -> AgentWork.report epoch alice JoinableRole.Implementer { report with Sequence = seq; Usage = Some zero } state |> accepted)
      latest.Runs.[report.Run].Activity.Length |> Expect.equal "history bound" 16
      latest.Runs.[report.Run].OmittedActivity |> Expect.equal "history omissions" 24L
      let full = (AgentWork.empty, [1..256]) ||> List.fold (fun state n -> AgentWork.report epoch alice JoinableRole.Implementer { report with Run = string n; Usage = None } state |> accepted)
      AgentWork.report epoch alice JoinableRole.Implementer { report with Run = "257" } full |> Expect.equal "retained run keys never evict and become takeovers" (Error AgentWork.Capacity)
    }
    test "publication survives epoch recycling and preserves nonempty wire projection" {
      let publication = Bozzetto.Server.ResourceGraph.AgentPublication.create()
      let mutable notifications = 0
      use subscription = publication.Changed.Subscribe(fun () -> notifications <- notifications + 1)
      let mutable state = publication.Read()
      for sequence in 1L .. 300L do
        state <- AgentWork.report epoch alice JoinableRole.Implementer { report with Sequence = sequence } state |> accepted
        publication.Publish state
        publication.Read() |> Expect.equal "offered snapshot is the current immutable input, including across graph recycle" state
      notifications |> Expect.equal "one notification per committed publication" 300
      let board = Bozzetto.Server.UiBridge.Project.agentWork state
      board.Runs.[0].Usage.Value.Total |> Expect.equal "latest meter, not a sum of repeated snapshots" 10L
      let wire = Bozzetto.Server.UiBridgeCodec.encodeEvent (Bozzetto.Web.Shared.Protocol.AgentWork board)
      let sourceJson = AgentWork.json state |> Fidelity.Data.JSON.Json.parse |> function Ok v -> v | Error e -> failtest e
      let bridgeJson = wire |> Fidelity.Data.JSON.Json.parse |> function Ok v -> v | Error e -> failtest e
      for field in ["incarnation"; "sequence"; "executionHost"; "capacity"; "runs"] do
        Fidelity.Data.JSON.JsonValue.prop field bridgeJson |> Expect.equal "MCP and typed bridge project identical owner facts" (Fidelity.Data.JSON.JsonValue.prop field sourceJson)
    }
    testTask "publication failure is specific and exact retry reconciles without charging twice" {
      let ledger = InMemory.create<MemberId>()
      let inner = AgentWork.inMemoryPublication()
      let mutable fail = true
      let publication = { inner with Publish = fun state -> if fail then invalidOp "injected publication failure" else inner.Publish state }
      use owner = CohortOwner.startWithWorkPublication silentLogger ledger (fixedClock epoch) (counterEntropy()) (fun _ -> [], [], [], 0L) CohortOwner.LandingPerformer.stub publication
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
      let! failed = owner.ReportWork(alice, report) |> Async.StartAsTask
      failed |> Expect.equal "explicit publication failure" (Error AgentWork.PublicationFailed)
      fail <- false
      let! retry = owner.ReportWork(alice, report) |> Async.StartAsTask
      retry |> Expect.equal "same report reconciles original commitment" (Ok 1L)
      owner.ReadWork().Runs.[report.Run].Report.Usage.Value.Total |> Expect.equal "no double accounting" 10L
    }
    testTask "departure publication failure retains commitment, settles caller and reconciles" {
      // Exercise failure both before offer and after offer (e.g. a Changed
      // subscriber throws). Equality of Read() cannot clear pending delivery.
      for afterOffer in [false; true] do
        let ledger = InMemory.create<MemberId>()
        let inner = AgentWork.inMemoryPublication()
        let mutable fail = false
        let mutable attempts = 0
        let publication =
          { inner with
              Publish = fun state ->
                attempts <- attempts + 1
                if not fail || afterOffer then inner.Publish state
                if fail then invalidOp "injected departure publication failure" }
        use owner = CohortOwner.startWithWorkPublication silentLogger ledger (fixedClock epoch) (counterEntropy()) (fun _ -> [], [], [], 0L) CohortOwner.LandingPerformer.stub publication
        let! joined = owner.Commit(CohortCommand.Join(alice, JoinableRole.Implementer, None))
        joined |> Expect.isOk "positive: joined before publication fault"
        let! submitted = owner.ReportWork(alice, report) |> Async.StartAsTask
        submitted |> Expect.equal "positive: work accepted" (Ok 1L)
        fail <- true
        let departure = owner.Commit(CohortCommand.Depart alice)
        let! settled = System.Threading.Tasks.Task.WhenAny(departure :> System.Threading.Tasks.Task, System.Threading.Tasks.Task.Delay 2000)
        obj.ReferenceEquals(settled, departure) |> Expect.isTrue "committed departure reply must settle despite publication failure"
        let! departed = departure
        departed |> Expect.isOk "publication failure cannot reverse successful departure"
        owner.ReadCohortState().Members.[alice].Presence |> Expect.equal "committed member state is retained" (MemberPresence.Departed epoch)
        let! subsequent = owner.Commit(CohortCommand.Join(bob, JoinableRole.Observer, None))
        subsequent |> Expect.isOk "next command still settles"
        ledger.ReadAll() |> List.map (fun entry -> int64 entry.Seq) |> Expect.equal "no reuse of committed ledger sequence" [0L; 1L; 2L]
        let! pending = owner.ReconcileWork() |> Async.StartAsTask
        pending |> Expect.equal "specific loud reconciliation failure" (Error AgentWork.PublicationFailed)
        let beforeRetry = attempts
        fail <- false
        let! reconciled = owner.ReconcileWork() |> Async.StartAsTask
        reconciled |> Expect.equal "retry reconciles the retained owner state" (Ok 2L)
        attempts |> Expect.equal "retry offers even when previous failed delivery already changed Read()" (beforeRetry + 1)
        let retained = owner.ReadWork().Runs.[report.Run]
        retained.Report.Status |> Expect.equal "failed publication never discards the disconnected run" "disconnected"
        retained.Report.Usage.Value.Total |> Expect.equal "reconciliation never charges again" 10L
        let! again = owner.ReconcileWork() |> Async.StartAsTask
        again |> Expect.equal "settled reconciliation is idempotent" (Ok 2L)
        attempts |> Expect.equal "read demand does not republish a settled board" (beforeRetry + 1)
    }
    testTask "owner membership, departure, publication and restart" {
      let ledger = InMemory.create<MemberId>()
      let publication = Bozzetto.Server.ResourceGraph.AgentPublication.create()
      use owner = CohortOwner.startWithWorkPublication silentLogger ledger (fixedClock epoch) (counterEntropy()) (fun _ -> [], [], [], 0L) CohortOwner.LandingPerformer.stub publication
      let! absent = owner.ReportWork(alice, report) |> Async.StartAsTask
      absent |> Expect.equal "membership required" (Error AgentWork.NotPresent)
      let! _ = owner.Commit(CohortCommand.Join(alice, JoinableRole.Observer, None))
      let! submitted = owner.ReportWork(alice, report) |> Async.StartAsTask
      submitted |> Expect.equal "observer reports its own work" (Ok 1L)
      owner.ReadWork().Runs.Count |> Expect.equal "foundation offers published snapshot" 1
      let! _ = owner.Commit(CohortCommand.Depart alice)
      owner.ReadWork().Runs.[report.Run].Report.Status |> Expect.equal "departure is not completion" "disconnected"
      use restarted = CohortOwner.start silentLogger ledger (fixedClock epoch) (counterEntropy()) (fun _ -> [], [], [], 0L)
      restarted.ReadWork().Runs.IsEmpty |> Expect.isTrue "history never restores live runs"
    }
  ]
