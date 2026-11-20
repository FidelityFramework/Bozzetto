/// Slice 1 of cohort-integration-plan.md: `CohortLedgerSqlite.Sqlite` is the
/// SQLite implementation of `CohortLedger.LedgerPort<MemberId>`. This suite
/// proves the codec round-trips — `ReadAll` after `Append` is the identity
/// on what was appended, including entropy bytes and every DU shape
/// `CohortCommand`/`CohortEvent` can take. It touches a temp SQLite file, so
/// it is registered `[Integration]` (runs under `--integration-host`) rather
/// than joining the default suite.
module Bozzetto.Tests.CohortLedgerSqliteTests

open System
open System.IO
open Expecto
open Expecto.Flip
open FsCheck
open FsCheck.FSharp
open Microsoft.Data.Sqlite
open Bozzetto
open Bozzetto.Cohort
open Bozzetto.Measures
open Bozzetto.MemberTable
open Bozzetto.Features.CohortLedger
open Bozzetto.Features.CohortLedgerSqlite
open Bozzetto.Tests.TestInfrastructure

let private tempDbPath () =
  Path.Combine(Path.GetTempPath(), sprintf "bozzetto-cohort-ledger-%s.db" (Guid.NewGuid().ToString("N")))

let private cleanup (path: string) =
  try File.Delete path with _ -> ()

// ── Generators covering the DU shapes the codec must round-trip ───────────

let private genMember =
  Gen.oneof [
    Gen.elements [ "a"; "b"; "c" ] |> Gen.map MemberId.Minted
    Gen.elements [ "s1"; "s2" ] |> Gen.map MemberId.Browser
    Gen.elements [ "t1"; "t2" ] |> Gen.map MemberId.Mcp
  ]

let private genRole =
  Gen.elements [ JoinableRole.Implementer; JoinableRole.Verifier; JoinableRole.Observer ]

/// Covers both shapes of the new (item 13c) `session` field so the codec
/// round-trip actually exercises `Some`/`None`, not just one of them.
let private genSessionOpt : Gen<string option> =
  Gen.oneof [
    Gen.constant None
    Gen.elements [ "sess-1"; "sess-2" ] |> Gen.map Some
  ]

let private genScope =
  Gen.oneof [
    Gen.elements [ "A.fs"; "B.fs" ] |> Gen.map ClaimScope.File
    Gen.elements [ "X.fsproj"; "Y.fsproj" ] |> Gen.map ClaimScope.Project
  ]

let private genClaimId = Gen.elements [ "c-1"; "c-2"; "c-3" ] |> Gen.map ClaimId
let private genLandingId = Gen.elements [ "l-1"; "l-2" ] |> Gen.map LandingId
let private genTestId = Gen.elements [ "t-a"; "t-b" ] |> Gen.map TestId

let private genFence : Gen<int64<fence>> =
  Gen.choose (0, 100) |> Gen.map (fun n -> LanguagePrimitives.Int64WithMeasure<fence> (int64 n))

let private genDateTime : Gen<DateTime> =
  Gen.choose (0, 1_000_000)
  |> Gen.map (fun s -> DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(float s))

let private genShortList (g: Gen<'a>) : Gen<'a list> =
  Gen.choose (0, 2) |> Gen.bind (fun n -> Gen.listOfLength n g)

let private genCommand : Gen<CohortCommand<MemberId>> =
  Gen.oneof [
    Gen.map3 (fun m r s -> CohortCommand.Join(m, r, s)) genMember genRole genSessionOpt
    Gen.map CohortCommand.Depart genMember
    Gen.map CohortCommand.RenewLease genMember
    Gen.constant CohortCommand.Tick
    Gen.map3 (fun m s p -> CohortCommand.AcquireClaim(m, s, p)) genMember genScope (Gen.constant "purpose")
    Gen.map3 (fun m c f -> CohortCommand.ReleaseClaim(m, c, f)) genMember genClaimId genFence
    Gen.map3 (fun by c t -> CohortCommand.ReassignClaim(by, c, t)) genMember genClaimId genMember
    Gen.map3 (fun m c f -> CohortCommand.ReclaimClaim(m, c, f)) genMember genClaimId genFence
    Gen.map2 (fun by t -> CohortCommand.DelegateConductor(by, t)) genMember genMember
    Gen.constant CohortCommand.PromoteConductor
    Gen.constant CohortCommand.ExpireOrphanedClaims
    Gen.map3 (fun m r s -> CohortCommand.JoinNamed(m, r, s, "agent-a")) genMember genRole genSessionOpt
    Gen.map2 (fun m p -> CohortCommand.ObserveSave(m, p)) genMember (Gen.elements [ "P.fs"; "Q.fs" ])
    (gen {
      let! r = genMember
      let! claims = genShortList (Gen.map2 (fun c f -> c, f) genClaimId genFence)
      let! commits = genShortList (Gen.elements [ "sha1"; "sha2" ])
      return CohortCommand.RequestLanding(r, claims, commits, "statement")
    })
    (gen {
      let! l = genLandingId
      let! ok = Gen.elements [ true; false ]
      if ok then
        let! sha = Gen.elements [ "sha-a"; "sha-b" ]
        return CohortCommand.RebaseCompleted(l, Ok sha)
      else
        let! files = genShortList (Gen.elements [ "conflict.fs"; "other.fs" ])
        return CohortCommand.RebaseCompleted(l, Error files)
    })
    Gen.map2 (fun l ts -> CohortCommand.AffectedComputed(l, ts)) genLandingId (genShortList genTestId)
    Gen.map2 (fun l ts -> CohortCommand.TestsCompleted(l, ts)) genLandingId (genShortList genTestId)
    Gen.map2 (fun l sha -> CohortCommand.FastForwardCompleted(l, sha)) genLandingId (Gen.elements [ "final-a"; "final-b" ])
    Gen.map2 (fun m l -> CohortCommand.WithdrawLanding(m, l)) genMember genLandingId
    Gen.map3 (fun by l reason -> CohortCommand.VetoLanding(by, l, reason)) genMember genLandingId (Gen.constant "reason")
  ]

let private genLandingState : Gen<LandingState<MemberId>> =
  Gen.oneof [
    Gen.constant LandingState.Queued
    Gen.map LandingState.Rebasing (Gen.elements [ "onto-a"; "onto-b" ])
    Gen.map4 (fun base' head n r -> LandingState.Verifying(base', head, n, r)) (Gen.elements [ "onto-a"; "onto-b" ]) (Gen.elements [ "head-a"; "head-b" ]) (Gen.choose (0, 5)) (Gen.choose (0, 5))
    (gen {
      let! blocker =
        Gen.oneof [
          genShortList (Gen.elements [ "x.fs"; "y.fs" ]) |> Gen.map LandingBlocker.RebaseConflict
          genShortList genTestId |> Gen.map LandingBlocker.FailingTests
          Gen.map LandingBlocker.StaleClaimFence genClaimId
          Gen.map2 (fun f t -> LandingBlocker.HeadMoved(f, t)) (Gen.elements [ "h1" ]) (Gen.elements [ "h2" ])
          Gen.map2 (fun m r -> LandingBlocker.VetoedBy(m, r)) genMember (Gen.constant "reason")
        ]
      let! action =
        Gen.oneof [
          Gen.constant NextAction.RebaseAndResubmit
          Gen.constant NextAction.AwaitConductor
          genShortList genTestId |> Gen.map NextAction.FixTests
          Gen.constant NextAction.Withdraw
        ]
      return LandingState.Blocked(blocker, action)
    })
    Gen.map LandingState.Landed (Gen.elements [ "landed-a"; "landed-b" ])
    Gen.constant LandingState.Withdrawn
  ]

let private genEvent : Gen<CohortEvent<MemberId>> =
  Gen.oneof [
    Gen.map3 (fun m r s -> CohortEvent.MemberJoined(m, r, s)) genMember genRole genSessionOpt
    Gen.map2 (fun m d -> CohortEvent.MemberDeparted(m, d)) genMember genDateTime
    Gen.map CohortEvent.LeaseRenewed genMember
    Gen.map CohortEvent.ConductorBound genMember
    Gen.map2 (fun f t -> CohortEvent.ConductorDelegated(f, t)) genMember genMember
    Gen.map2 (fun p n -> CohortEvent.ConductorPromoted(p, n)) genMember genMember
    (gen {
      let! cid = genClaimId
      let! scope = genScope
      let! holder = genMember
      let! f = genFence
      return CohortEvent.ClaimAcquired(cid, scope, holder, f)
    })
    (gen {
      let! cid = genClaimId
      let! by = genMember
      let! f = genFence
      return CohortEvent.ClaimReleased(cid, by, f)
    })
    (gen {
      let! cid = genClaimId
      let! prev = genMember
      let! f = genFence
      return CohortEvent.ClaimOrphaned(cid, prev, f)
    })
    (gen {
      let! cid = genClaimId
      let! toM = genMember
      let! f = genFence
      return CohortEvent.ClaimReassigned(cid, toM, f)
    })
    (gen {
      let! cid = genClaimId
      let! observer = genMember
      let! holder = genMember
      let! path = Gen.elements [ "P.fs"; "Q.fs" ]
      return CohortEvent.ClaimViolationObserved(cid, observer, holder, path)
    })
    Gen.map2 (fun l r -> CohortEvent.LandingQueued(l, r)) genLandingId genMember
    Gen.map2 (fun l s -> CohortEvent.LandingStateChanged(l, s)) genLandingId genLandingState
    Gen.map2 (fun l sha -> CohortEvent.LandingLanded(l, sha)) genLandingId (Gen.elements [ "sha-x"; "sha-y" ])
    Gen.map CohortEvent.LandingWithdrawn genLandingId
    Gen.map3 (fun l by reason -> CohortEvent.LandingVetoed(l, by, reason)) genLandingId genMember (Gen.constant "reason")
  ]

let private genEntropy : Gen<byte[]> =
  gen {
    let! len = Gen.choose (0, 32)
    let! bytes = Gen.listOfLength len (Gen.choose (0, 255) |> Gen.map byte)
    return List.toArray bytes
  }

let rec private genAll (gens: Gen<'a> list) : Gen<'a list> =
  match gens with
  | [] -> Gen.constant []
  | g :: rest -> gen {
      let! x = g
      let! xs = genAll rest
      return x :: xs
    }

let private genEntryAt (seq: int64) : Gen<LedgerEntry<MemberId>> =
  gen {
    let! clock = genDateTime
    let! entropy = genEntropy
    let! command = genCommand
    let! events = genShortList genEvent
    return { Seq = LanguagePrimitives.Int64WithMeasure<ledgerSeq> seq; Clock = clock; Entropy = entropy; Command = command; Events = events }
  }

/// Dense, increasing Seq (0, 1, 2, ...) — matching what `CohortOwner` itself
/// assigns, and required by the table's `seq INTEGER PRIMARY KEY`.
let private genEntries : Gen<LedgerEntry<MemberId> list> =
  Gen.choose (0, 8)
  |> Gen.bind (fun n -> genAll [ for i in 0 .. n - 1 -> genEntryAt (int64 i) ])

let private config = { FsCheckConfig.defaultConfig with maxTest = 60 }

[<Tests>]
let cohortLedgerSqliteTests =
  Integration.hostList "CohortLedgerSqlite" [

    test "creating a ledger records the schema version via PRAGMA user_version" {
      let path = tempDbPath ()
      try
        Sqlite.create path |> ignore
        use connection = new SqliteConnection(sprintf "Data Source=%s" path)
        connection.Open()
        use cmd = connection.CreateCommand()
        cmd.CommandText <- "PRAGMA user_version;"
        let version = cmd.ExecuteScalar() :?> int64
        version |> Expect.equal "the ledger's schema version is recorded" 1L
      finally
        cleanup path
    }

    test "a fresh ledger reads back as empty" {
      let path = tempDbPath ()
      try
        let port = Sqlite.create path
        port.ReadAll () |> Expect.isEmpty "nothing has been appended yet"
      finally
        cleanup path
    }

    testTask "a fault in the last insert rolls back the trigger, promotion, and expiry together" {
      let path = tempDbPath ()
      try
        let ledger = Sqlite.create path
        let clock = ref (DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc))
        let logger =
          { new Bozzetto.Utils.ILogger with
              member _.LogInfo _ = ()
              member _.LogDebug _ = ()
              member _.LogWarning _ = ()
              member _.LogError _ = () }
        use owner = Bozzetto.Features.CohortOwner.start logger ledger (fun () -> clock.Value) (fun () -> [| 1uy |]) (fun _ -> ([], [], [], 0L))
        let lead, successor, holder = MemberId.Minted "lead", MemberId.Minted "successor", MemberId.Minted "holder"
        for cmd in [ CohortCommand.Join(lead, JoinableRole.Implementer, None)
                     CohortCommand.Join(successor, JoinableRole.Observer, None)
                     CohortCommand.Join(holder, JoinableRole.Implementer, None)
                     CohortCommand.AcquireClaim(holder, ClaimScope.File "src/Stale.fs", "stale")
                     CohortCommand.Depart holder ] do
          let! accepted = owner.Commit cmd
          accepted |> Result.isOk |> Expect.isTrue "setup command commits"
        let before = ledger.ReadAll ()
        let frame = owner.ReadFrame()
        let state = owner.ReadCohortState()
        clock.Value <- clock.Value + orphanClaimTtl
        use connection = new SqliteConnection(sprintf "Data Source=%s" path)
        connection.Open()
        use fault = connection.CreateCommand()
        fault.CommandText <- "CREATE TRIGGER fail_expiry BEFORE INSERT ON cohort_ledger WHEN NEW.seq = 7 BEGIN SELECT RAISE(ABORT, 'expiry insert fault'); END;"
        fault.ExecuteNonQuery() |> ignore
        let! failed = owner.Commit(CohortCommand.Depart lead)
        failed |> Expect.equal "the owner returns a durable write failure" (Error CohortError.LedgerAppendFailed)
        ledger.ReadAll () |> Expect.equal "all three rows roll back" before
        owner.ReadFrame() |> Expect.equal "no failed batch is published" frame
        owner.ReadCohortState() |> Expect.equal "the actor keeps its previous state" state

        fault.CommandText <- "DROP TRIGGER fail_expiry;"
        fault.ExecuteNonQuery() |> ignore
        let! retry = owner.Commit(CohortCommand.Depart lead)
        retry |> Result.isOk |> Expect.isTrue "the same command succeeds once the fault is removed"
        let after = ledger.ReadAll ()
        after |> List.skip before.Length |> List.map (fun e -> int64 e.Seq, e.Command)
        |> Expect.equal "retry reuses the sequence without gaps"
            [ 5L, CohortCommand.Depart lead; 6L, CohortCommand.PromoteConductor; 7L, CohortCommand.ExpireOrphanedClaims ]
        owner.ReadCohortState() |> Expect.equal "live state matches the committed replay" (Cohort.replay after)
      finally
        cleanup path
    }

    testPropertyWithConfig config "append then readAll is the identity on the appended entries, including entropy bytes and every DU command/event shape" <|
      Prop.forAll (Arb.fromGen genEntries) (fun entries ->
        let path = tempDbPath ()
        try
          let port = Sqlite.create path
          port.AppendBatch entries |> Expect.equal "the batch commits" (Ok ())
          port.ReadAll () = entries
        finally
          cleanup path)

    test "SQLite adapter maps constraint and storage failures to typed LedgerWriteError cases" {
      let path = tempDbPath ()
      try
        let port = Sqlite.create path
        let entry: LedgerEntry<MemberId> =
          { Seq = 0L<ledgerSeq>; Clock = DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
            Entropy = [| 1uy |]; Command = CohortCommand.Tick; Events = [] }
        port.AppendBatch [ entry ] |> Expect.equal "first insert succeeds" (Ok ())
        match port.AppendBatch [ entry ] with
        | Error(LedgerWriteError.ConstraintViolation detail) ->
          detail |> Expect.stringContains "duplicate primary key surfaces exact SQLite constraint detail" "UNIQUE constraint failed"
        | other -> failtestf "expected ConstraintViolation, got %A" other
        if OperatingSystem.IsLinux() then
          SqliteConnection.ClearAllPools()
          File.SetUnixFileMode(path, UnixFileMode.UserRead)
          try
            match port.AppendBatch [ { entry with Seq = 1L<ledgerSeq> } ] with
            | Error(LedgerWriteError.StorageUnavailable _) -> ()
            | other -> failtestf "expected StorageUnavailable on read-only ledger file, got %A" other
          finally
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
      finally
        SqliteConnection.ClearAllPools()
        cleanup path
    }
  ]
