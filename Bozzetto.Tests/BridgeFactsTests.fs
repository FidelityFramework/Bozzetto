/// The bridge's fact plane: owner publications admitted by serial, projected on
/// the shared incremental core, delivered to registered sinks. Each rule has a
/// positive control, a negative control that asserts the reason, and a named
/// mutation (in the comments) the test would catch. The health tick's own
/// rules follow, sequenced, because the tick is daemon-wide state.
module Bozzetto.Tests.BridgeFactsTests

open System
open System.Threading
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Server.UiBridge

let private machine : Features.MachineMemory.Stats = { TotalBytes = 64L; AvailableBytes = 32L }

let private tick serial level : Server.HealthTick.Tick =
  { Serial = serial; SampledAtMs = serial * 1000L; Machine = machine; Level = level
    Record = None; Telemetry = None; RecordAtMs = 0L }

let private pool serial active queue : Features.LeaseWatch.Pool = { Serial = serial; Active = active; Queue = queue }

let private grant holder : ExpensiveWorkLease.GrantView =
  { Holder = holder; Kind = "full_build"; GrantedAt = DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)
    ExpiresAt = DateTimeOffset(2026, 10, 11, 12, 10, 0, TimeSpan.Zero) }

let private constants : Facts.Constants = { McpPort = 47759; Version = "test"; ProcessId = 7 }
let private incarnation = "daemon-t"
let private agents sequence : AgentWork.State = { Incarnation = incarnation; Sequence = sequence; Runs = Map.empty }

let private frame (version: int64) (members: (MemberTable.MemberId * Cohort.JoinableRole * Cohort.SeatState * DateTime) list)
  : Cohort.CohortFrame<MemberTable.MemberId> =
  { Version = version * 1L<Measures.ledgerSeq>
    SessionGens = [||]
    Dirty = Cohort.FrameRegions.NoRegions
    Conductor = None
    MemberIds = members |> List.map (fun (m, _, _, _) -> m) |> List.toArray
    MemberRole = members |> List.map (fun (_, r, _, _) -> r) |> List.toArray
    MemberSeat = members |> List.map (fun (_, _, s, _) -> s) |> List.toArray
    MemberRenewal = members |> List.map (fun (_, _, _, t) -> t) |> List.toArray
    ClaimIds = [||]; ClaimScope = [||]; ClaimHolderIndex = [||]; ClaimFence = [||]; ClaimState = [||]
    TestIds = [||]; Pass = [||]; Fail = [||]; Stale = [||]
    IntegrationHead = ""
    LandingIds = [||]; LandingRequesterIndex = [||]; LandingStatement = [||]; LandingCommits = [||]; LandingState = [||]
    LandingQueuePosition = [||] }

let private present name =
  MemberTable.MemberId.Mcp name, Cohort.JoinableRole.Observer, Cohort.SeatState.Present, DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc)

/// A page's sink: what reached it, in order.
type private Sink() =
  let seen = ResizeArray<Push * Event>()
  member _.Push (kind: Push) (event: Event) = lock seen (fun () -> seen.Add(kind, event))
  member _.Seen = lock seen (fun () -> List.ofSeq seen)
  member this.Healths = this.Seen |> List.choose (function _, Health h -> Some h | _ -> None)
  member this.Boards = this.Seen |> List.choose (function _, AgentWork b -> Some b | _ -> None)

let private healthOf (events: Event list) = events |> List.pick (function Health h -> Some h | _ -> None)
let private boardOf (events: Event list) = events |> List.pick (function AgentWork b -> Some b | _ -> None)

[<Tests>]
let factsTests =
  testList "UiBridge.Facts — the fact plane" [
    testCase "an older serial arriving after a newer one is dropped before the core sees it" <| fun () ->
      // Mutation caught: remove the serial compare in admission and the Normal reading would be applied.
      let graph = Facts.Graph(constants, incarnation)
      let sink = Sink()
      let _, _ = graph.Register sink.Push
      graph.Tick(tick 2L MemoryPressure.Tight) |> Expect.equal "the newer reading is admitted" (Ok())
      graph.Tick(tick 1L MemoryPressure.Normal) |> Expect.equal "the older reading is refused as older" (Error Facts.Refusal.Older)
      sink.Healths |> List.map (fun h -> h.MemoryPressure) |> Expect.equal "one Health, the newer" [ "Tight" ]
      (healthOf (graph.Current())).MemoryPressure |> Expect.equal "the current value is the newer" "Tight"

    testCase "the same serial with other content is an owner protocol fault and is not applied; an exact duplicate is ignored" <| fun () ->
      // Mutation caught: compare serials only and the conflicting Tight reading would be admitted as a duplicate.
      let graph = Facts.Graph(constants, incarnation)
      graph.Tick(tick 1L MemoryPressure.Normal) |> Expect.equal "admitted" (Ok())
      graph.Tick(tick 1L MemoryPressure.Normal) |> Expect.equal "duplicate" (Error Facts.Refusal.Duplicate)
      graph.Faults |> Expect.equal "a duplicate is not a fault" 0
      graph.Tick(tick 1L MemoryPressure.Tight) |> Expect.equal "conflict" (Error Facts.Refusal.Conflict)
      graph.Faults |> Expect.equal "a conflict is counted" 1
      (healthOf (graph.Current())).MemoryPressure |> Expect.equal "the first content stands" "Normal"

    testCase "each admitted reading settles Health once, carrying that reading's level and figures, stamped one on" <| fun () ->
      // Mutation caught: sample the machine inside the projection and the figures would not be the reading's.
      let graph = Facts.Graph(constants, incarnation)
      let sink = Sink()
      let _, _ = graph.Register sink.Push
      graph.Tick(tick 1L MemoryPressure.Normal) |> ignore
      graph.Tick({ tick 2L MemoryPressure.Tight with Machine = { TotalBytes = 64L; AvailableBytes = 8L } }) |> ignore
      graph.Tick(tick 3L MemoryPressure.Normal) |> ignore
      sink.Healths |> List.map (fun h -> h.Revision, h.MemoryPressure, h.MachineAvailableBytes)
      |> Expect.equal "one publication per reading, stamps increasing, figures from the same reading"
           [ 1L, "Normal", 32L; 2L, "Tight", 8L; 3L, "Normal", 32L ]

    testCase "a presence-only change advances the board's stamp while the reports stand" <| fun () ->
      // Mutation caught: stamp the board with the owner's report sequence and the second board would repeat 5.
      let graph = Facts.Graph(constants, incarnation)
      let sink = Sink()
      let _, _ = graph.Register sink.Push
      graph.Agents(agents 5L) |> Expect.equal "reports" (Ok())
      graph.Pool(pool 1L [] []) |> Expect.equal "pool" (Ok())
      graph.Current() |> List.length |> Expect.equal "no board until the membership is known" 1
      graph.Membership(frame 1L [ present "pi" ]) |> Expect.equal "membership" (Ok())
      graph.Pool(pool 2L [ grant "pi · /repo" ] []) |> Expect.equal "a lease appears" (Ok())
      let boards = sink.Boards
      boards |> List.map (fun b -> b.Sequence, b.Incarnation, b.Runs.Length, b.Presence.Length)
      |> Expect.equal "two boards: membership, then the lease; stamps advance, reports unchanged"
           [ 1L, incarnation, 0, 1; 2L, incarnation, 0, 2 ]
      (boardOf (graph.Current())).Presence |> Array.map (fun p -> p.Source) |> Expect.equal "the lease row joins the member row" [| "lease"; "member" |]

    testCase "a page registered after some facts receives the current values, then every later publication in increasing order, never one older than its snapshot" <| fun () ->
      // Mutation caught: take the snapshot outside the registration section and a publication between the two could be doubled or lost.
      let graph = Facts.Graph(constants, incarnation)
      graph.Tick(tick 1L MemoryPressure.Normal) |> ignore
      graph.Tick(tick 2L MemoryPressure.Tight) |> ignore
      graph.Pool(pool 1L [] []) |> ignore
      let sink = Sink()
      let current, registration = graph.Register sink.Push
      current |> List.map (function Health h -> "Health " + string h.Revision | Leases _ -> "Leases" | other -> string other)
      |> Expect.equal "the snapshot: Health as stamped, then Leases" [ "Health 2"; "Leases" ]
      graph.Tick(tick 3L MemoryPressure.Normal) |> ignore
      graph.Tick(tick 4L MemoryPressure.Normal) |> ignore
      sink.Healths |> List.map _.Revision |> Expect.equal "every later publication, in order" [ 3L; 4L ]
      registration.Dispose()
      graph.Tick(tick 5L MemoryPressure.Tight) |> ignore
      sink.Healths |> List.length |> Expect.equal "a disposed registration receives nothing more" 2

    testCase "the plane reads no owner after start: values come through admission alone" <| fun () ->
      // Mutation caught: read an owner inside Current and the counter would move with each call.
      let graph = Facts.Graph(constants, incarnation)
      let reads = ref 0
      let tickChanged = Event<Server.HealthTick.Tick>()
      let poolChanged = Event<Features.LeaseWatch.Pool>()
      use _ =
        graph.Start
          { Facts.Owners.Tick = tickChanged.Publish, (fun () -> Interlocked.Increment &reads.contents |> ignore; Some(tick 1L MemoryPressure.Normal))
            Pool = poolChanged.Publish, (fun () -> Interlocked.Increment &reads.contents |> ignore; pool 1L [] [])
            Agents = None
            Membership = None }
      reads.Value |> Expect.equal "each owner is read once, at start" 2
      for _ in 1 .. 5 do graph.Current() |> ignore
      let _, registration = graph.Register(fun _ _ -> ())
      registration.Dispose()
      reads.Value |> Expect.equal "neither Current nor Register reads an owner" 2
      tickChanged.Trigger(tick 2L MemoryPressure.Tight)
      (healthOf (graph.Current())).MemoryPressure |> Expect.equal "a publication is admitted through the subscription" "Tight"

    testCase "a publication fired during subscribe-and-snapshot is not lost and is ordered by serial" <| fun () ->
      // The owner announces a newer reading while its current value is being taken: the newer wins.
      // Mutation caught: subscribe after taking the current value and the announced reading would be lost.
      let graph = Facts.Graph(constants, incarnation)
      let tickChanged = Event<Server.HealthTick.Tick>()
      let poolChanged = Event<Features.LeaseWatch.Pool>()
      use _ =
        graph.Start
          { Facts.Owners.Tick = tickChanged.Publish, (fun () -> tickChanged.Trigger(tick 2L MemoryPressure.Tight); Some(tick 1L MemoryPressure.Normal))
            Pool = poolChanged.Publish, (fun () -> pool 1L [] [])
            Agents = None
            Membership = None }
      let health = healthOf (graph.Current())
      (health.MemoryPressure, health.Revision) |> Expect.equal "the announced newer reading stands; the older current value was dropped" ("Tight", 1L)

    testCase "agent work from another incarnation is refused and counted; the board is not rolled back" <| fun () ->
      // Mutation caught: admit a new incarnation as a reset and the retired owner's board would replace the current one.
      let graph = Facts.Graph(constants, incarnation)
      graph.Pool(pool 1L [] []) |> ignore
      graph.Membership(frame 1L []) |> ignore
      graph.Agents(agents 3L) |> Expect.equal "the bound incarnation" (Ok())
      graph.Agents({ agents 9L with Incarnation = "daemon-retired" }) |> Expect.equal "another incarnation" (Error Facts.Refusal.ForeignIncarnation)
      graph.Faults |> Expect.equal "counted" 1
      (boardOf (graph.Current())).Incarnation |> Expect.equal "the board keeps its incarnation" incarnation

    testCase "within one step sequence a Withdraw is followed by exactly one re-Offer: one publication, and the store holds one result per work" <| fun () ->
      // Mutation caught: skip the Withdraw's release and the stored results would grow by one per set.
      let graph = Facts.Graph(constants, incarnation)
      let sink = Sink()
      let _, _ = graph.Register sink.Push
      graph.Tick(tick 1L MemoryPressure.Normal) |> ignore
      graph.Pool(pool 1L [] []) |> ignore
      graph.StoredResults |> Expect.equal "Health and Leases settled" 2
      for serial in 2L .. 6L do graph.Pool(pool serial [ grant "pi · /repo" ] []) |> ignore
      sink.Seen |> List.filter (fun (kind, _) -> kind = Push.Leases) |> List.length |> Expect.equal "one publication per set" 6
      graph.StoredResults |> Expect.equal "still one result per work: each Withdraw released the superseded one" 2
      sink.Seen |> List.choose (function _, Leases l -> Some l.Revision | _ -> None) |> Expect.equal "lease stamps increase by one per publication" [ 1L .. 6L ]

    testCase "a page registering while another thread publishes sees a snapshot and then only newer stamps, strictly increasing" <| fun () ->
      // Controlled overlap: the publisher runs on its own thread; the registration lands somewhere in its sequence.
      // Mutation caught: deliver before committing the registration's snapshot and a stamp below the snapshot's could follow it.
      let graph = Facts.Graph(constants, incarnation)
      let started = new ManualResetEventSlim(false)
      let publisher =
        Tasks.Task.Run(fun () ->
          for serial in 1L .. 400L do
            graph.Tick(tick serial (if serial % 2L = 0L then MemoryPressure.Tight else MemoryPressure.Normal)) |> ignore
            if serial = 50L then started.Set())
      started.Wait(TimeSpan.FromSeconds 5.) |> Expect.isTrue "the publisher is running"
      let sink = Sink()
      let current, _ = graph.Register sink.Push
      publisher.Wait(TimeSpan.FromSeconds 10.) |> Expect.isTrue "the publisher finished"
      let snapshot = (healthOf current).Revision
      let later = sink.Healths |> List.map _.Revision
      (later |> List.forall (fun r -> r > snapshot)) |> Expect.isTrue "every delivered stamp is newer than the snapshot's"
      (later |> List.pairwise |> List.forall (fun (a, b) -> b > a)) |> Expect.isTrue "stamps reach the page strictly increasing"
      (healthOf (graph.Current())).Revision |> Expect.equal "the final value is the last reading" 400L

    testCase "shutdown retires the epoch: nothing is admitted or delivered afterwards, and the core reports drained" <| fun () ->
      // Mutation caught: forget Retire and the core would not report drained; forget the flag and a late callback would be admitted.
      let graph = Facts.Graph(constants, incarnation)
      let sink = Sink()
      let _, _ = graph.Register sink.Push
      graph.Tick(tick 1L MemoryPressure.Normal) |> ignore
      graph.Pool(pool 1L [] []) |> ignore
      graph.Stop()
      graph.Drained |> Expect.isTrue "the retired epoch drained"
      graph.Tick(tick 2L MemoryPressure.Tight) |> Expect.equal "a late callback is refused" (Error Facts.Refusal.Retired)
      graph.Pool(pool 2L [ grant "pi · /repo" ] []) |> Expect.equal "so is a late pool" (Error Facts.Refusal.Retired)
      sink.Seen |> List.length |> Expect.equal "nothing was delivered after the stop" 2
      graph.Stop()

    testCase "an epoch handover at the bound keeps wire identity: stamps continue, nothing is re-delivered, values persist" <| fun () ->
      // Mutation caught: deliver during the handover and the sink would see one more Health than readings.
      let graph = Facts.Graph(constants, incarnation)
      let sink = Sink()
      let _, _ = graph.Register sink.Push
      graph.Pool(pool 1L [ grant "pi · /repo" ] []) |> ignore
      let readings = Facts.recycleBound
      for serial in 1 .. readings do
        graph.Tick(tick (int64 serial) (if serial % 2 = 0 then MemoryPressure.Tight else MemoryPressure.Normal)) |> ignore
      graph.Epoch |> Expect.equal "the bound retired the first epoch" 2UL
      sink.Healths |> List.length |> Expect.equal "one Health per reading, none from the handover" readings
      let before = graph.Current()
      graph.Tick(tick (int64 readings + 1L) MemoryPressure.Normal) |> ignore
      (healthOf (graph.Current())).Revision |> Expect.equal "the stamp continues across the handover" (int64 readings + 1L)
      before |> List.choose (function Leases l -> Some l | _ -> None) |> List.length |> Expect.equal "the lease board survived the handover" 1
      (healthOf before).Revision |> Expect.equal "the value before the next reading is the last reading's" (int64 readings)
  ]

[<Tests>]
let healthTickTests =
  testSequenced <| testList "HealthTick — the daemon's one sequencer of its readings" [
    testCase "a reading folds the level and is published with an increasing serial; a level move brings the record once" <| fun () ->
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()
      let seen = ResizeArray<Server.HealthTick.Tick>()
      use _ = Server.HealthTick.Changed.Subscribe(fun tick -> lock seen (fun () -> seen.Add tick))
      let normal = Server.HealthTick.observe { TotalBytes = 100L; AvailableBytes = 50L }
      let tight = Server.HealthTick.observe { TotalBytes = 100L; AvailableBytes = 10L }
      (normal.Level, tight.Level) |> Expect.equal "the level owner's hysteresis applies" (MemoryPressure.Normal, MemoryPressure.Tight)
      seen |> Seq.map _.Serial |> List.ofSeq |> Expect.equal "reading, reading, then the record the move asked for" [ 1L; 2L; 3L ]
      seen[2].Level |> Expect.equal "the record publication carries the moved level" MemoryPressure.Tight
      (seen[2].Record, seen[2].RecordAtMs) |> Expect.equal "no reader is installed, so no record and no record time are claimed" (None, 0L)
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()

    testCase "a refresh publishes once, with the installed record and the reading taken inside it" <| fun () ->
      // Mutation caught: publish the inner reading on its own and the subscriber would see two publications.
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()
      let record : Features.HealthSnapshot =
        { DaemonPid = 1; DaemonPort = 2; Uptime = TimeSpan.Zero; Version = "r"; SessionSummaries = []; LiveTestingSummary = None
          MemoryMB = 3; Anomalies = []; GcDumpOutcome = None; MemoryPressure = MemoryPressure.Normal }
      Server.HealthTick.install (fun () ->
        Server.HealthTick.observe { TotalBytes = 100L; AvailableBytes = 70L } |> ignore
        Some record)
      let seen = ResizeArray<Server.HealthTick.Tick>()
      use _ = Server.HealthTick.Changed.Subscribe(fun tick -> lock seen (fun () -> seen.Add tick))
      Server.HealthTick.refresh () |> ignore
      seen.Count |> Expect.equal "one publication" 1
      (seen[0].Record, seen[0].Machine.AvailableBytes) |> Expect.equal "the record and the reading taken for it" (Some record, 70L)
      (seen[0].RecordAtMs > 0L) |> Expect.isTrue "the record publication says when the daemon was asked"
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()

    testCase "a record that fails keeps the previous record and its time, and still publishes the reading" <| fun () ->
      // Mutation caught: stamp RecordAtMs on every refresh and stale accounting would look freshly taken.
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()
      let record : Features.HealthSnapshot =
        { DaemonPid = 1; DaemonPort = 2; Uptime = TimeSpan.Zero; Version = "kept"; SessionSummaries = []; LiveTestingSummary = None
          MemoryMB = 3; Anomalies = []; GcDumpOutcome = None; MemoryPressure = MemoryPressure.Normal }
      Server.HealthTick.install (fun () -> Some record)
      let first = Server.HealthTick.refresh ()
      Server.HealthTick.install (fun () -> failwith "the accounting is away")
      let second = Server.HealthTick.refresh ()
      (second.Record |> Option.map _.Version, second.RecordAtMs) |> Expect.equal "the previous record and its time stand" (Some "kept", first.RecordAtMs)
      (second.Serial > first.Serial) |> Expect.isTrue "the reading itself was published"
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()

    testCase "a reader with no record to give keeps the previous record and its time" <| fun () ->
      // Mutation caught: treat `None` from the reader as a record and the kept accounting would be cleared with a fresh time.
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()
      let record : Features.HealthSnapshot =
        { DaemonPid = 1; DaemonPort = 2; Uptime = TimeSpan.Zero; Version = "kept"; SessionSummaries = []; LiveTestingSummary = None
          MemoryMB = 3; Anomalies = []; GcDumpOutcome = None; MemoryPressure = MemoryPressure.Normal }
      Server.HealthTick.install (fun () -> Some record)
      let first = Server.HealthTick.refresh ()
      Server.HealthTick.install (fun () -> None)
      let second = Server.HealthTick.refresh ()
      (second.Record |> Option.map _.Version, second.RecordAtMs) |> Expect.equal "the record and its time stand" (Some "kept", first.RecordAtMs)
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()

    testCase "a refresh that finishes after a later-started one keeps the newer record" <| fun () ->
      // Mutation caught: install whichever record arrives last and the older accounting would replace the newer.
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()
      let record version : Features.HealthSnapshot =
        { DaemonPid = 1; DaemonPort = 2; Uptime = TimeSpan.Zero; Version = version; SessionSummaries = []; LiveTestingSummary = None
          MemoryMB = 3; Anomalies = []; GcDumpOutcome = None; MemoryPressure = MemoryPressure.Normal }
      let slowEntered = new ManualResetEventSlim(false)
      let release = new ManualResetEventSlim(false)
      let calls = ref 0
      Server.HealthTick.install (fun () ->
        if Interlocked.Increment &calls.contents = 1 then
          slowEntered.Set()
          release.Wait(TimeSpan.FromSeconds 5.) |> ignore
          Some(record "older")
        else Some(record "newer"))
      let slow = Tasks.Task.Run(fun () -> Server.HealthTick.refresh ())
      slowEntered.Wait(TimeSpan.FromSeconds 5.) |> Expect.isTrue "the slow refresh is reading"
      let fast = Server.HealthTick.refresh ()
      release.Set()
      let late = slow.Result
      (fast.Record |> Option.map _.Version) |> Expect.equal "the later-started refresh installed its record" (Some "newer")
      (late.Record |> Option.map _.Version, late.RecordAtMs) |> Expect.equal "the earlier-started one, finishing last, kept the newer record and its time" (Some "newer", fast.RecordAtMs)
      Server.HealthTick.reset ()
      Features.MemoryPressureWatch.reset ()
  ]
