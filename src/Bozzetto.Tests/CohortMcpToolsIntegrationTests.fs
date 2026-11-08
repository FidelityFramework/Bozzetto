module Bozzetto.Tests.CohortMcpToolsIntegrationTests

/// RED integration tests for cohort-integration-plan.md Slice 2 (item 9):
/// the CohortOwner wired into the real daemon, exposed as Claims v1 MCP
/// tools (join_cohort/leave_cohort/acquire_claim/release_claim/
/// reassign_claim/request_landing/get_cohort_status).
///
/// Unlike HttpApiIntegrationTests.fs's raw-REST-endpoint tests, cohort tools
/// are ONLY registered as real `[<McpServerTool>]` members (no `/api/...`
/// REST wrapper exists for them, by design — see McpServer.fs's
/// `configureMcpProtocol`/`MapMcp()`), so this suite drives them through a
/// REAL MCP client (`ModelContextProtocol.Client.McpClient` — already a
/// transitive dependency via Bozzetto.fsproj's `ModelContextProtocol`
/// PackageReference, no new NuGet dependency added here) against a daemon
/// this suite spawns on an isolated port with an isolated BOZZETTO_DATA_DIR,
/// exactly the isolation discipline HttpApiIntegrationTests.fs uses.
///
/// Identity is bound to the MCP CONNECTION (Mcp.fs's `memberIdFor`/
/// `currentTransportSessionId`), not to the `agentName` argument — so two
/// "different identities" in cohort terms means two SEPARATE McpClient
/// connections to the same daemon, not two calls on one client with
/// different agentName strings (that would be the SAME member joining
/// twice and hitting DuplicateJoin). Verified live against a real spawned
/// daemon before this file was written.
///
/// EACH TEST GETS ITS OWN DAEMON (own port, own BOZZETTO_DATA_DIR, own
/// cohort.ledger.db): the daemon holds exactly one implicit cohort for its
/// whole lifetime (v1, D2), so "the first joiner becomes conductor" is only
/// true once per daemon — sharing one daemon across test cases would make
/// every test after the first observe a cohort that already has a
/// conductor and members from earlier cases.

open System
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol
open Bozzetto

module Integration = Bozzetto.Tests.TestInfrastructure.Integration

let private repoRoot =
  IO.Path.GetFullPath(IO.Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let private bozzettoExe = Bozzetto.Tests.TestInfrastructure.BozzettoBinary.path ()

/// Spawn an isolated daemon (own port, own BOZZETTO_DATA_DIR) and wait for
/// /health to respond. Owned by this test process (--owner-pid/--owner-start,
/// the same ownership fencing HttpApiIntegrationTests.fs uses) plus a --ttl
/// belt-and-braces, so a killed/crashed test process can never orphan it.
let private startIsolatedDaemon () : Task<Process * int> = task {
  // TestPorts.reservePair scans only this tier's assigned
  // BOZZETTO_TEST_PORT_RANGE when one is set, so a concurrently-running
  // tier's daemon can never win the reserve-then-bind race for this pair
  // (this daemon's dashboard is unused, but Bozzetto always binds it too).
  let port, _dashboardPort = Bozzetto.Tests.TestInfrastructure.TestPorts.reservePair ()
  let psi = ProcessStartInfo()
  psi.FileName <- bozzettoExe
  psi.UseShellExecute <- false
  psi.CreateNoWindow <- true
  psi.WorkingDirectory <- repoRoot
  psi.ArgumentList.Add "--mcp-port"
  psi.ArgumentList.Add(string port)
  psi.ArgumentList.Add("--owner-pid")
  psi.ArgumentList.Add(string (System.Diagnostics.Process.GetCurrentProcess().Id))
  let self = Process.GetCurrentProcess()
  psi.ArgumentList.Add "--owner-pid"
  psi.ArgumentList.Add(string self.Id)
  psi.ArgumentList.Add "--owner-start"
  psi.ArgumentList.Add(string (self.StartTime.ToUniversalTime().Ticks))
  psi.ArgumentList.Add "--ttl"
  psi.ArgumentList.Add "10m"
  let dataDir = IO.Path.Combine(IO.Path.GetTempPath(), "bozzetto-test-cohort", Guid.NewGuid().ToString "N")
  psi.Environment["BOZZETTO_DATA_DIR"] <- dataDir

  let proc = Process.Start psi
  use client = new Net.Http.HttpClient()
  client.BaseAddress <- Uri(sprintf "http://localhost:%d" port)
  client.Timeout <- TimeSpan.FromSeconds 5.0

  // Deadline-based (not a fixed attempt count) so the poll cadence and the
  // give-up bound are independently named and configurable — same pattern as
  // CohortLandingGateIntegrationTests.fs's startIsolatedDaemon, reusing the
  // shared Timeouts constants rather than a bare literal.
  let deadline = DateTime.UtcNow.Add Timeouts.integrationDaemonReady
  let mutable ready = false
  while not ready && DateTime.UtcNow < deadline do
    do! Task.Delay Timeouts.cohortLandingPoll
    try
      let! resp = client.GetAsync "/health"
      if int resp.StatusCode > 0 then ready <- true
    with _ -> ()

  if not ready then
    let killAttempt = try proc.Kill true; true with _ -> false
    ignore killAttempt
    proc.Dispose()
    failwithf "cohort test daemon failed to start on port %d within %O" port Timeouts.integrationDaemonReady

  return proc, port
}

let private killDaemon (proc: Process) =
  try
    if not proc.HasExited then
      proc.Kill(entireProcessTree = true)
      proc.WaitForExit 5000 |> ignore
  with _ -> ()
  proc.Dispose()

/// Extract the concatenated text of every TextContentBlock in a tool result
/// — the same shape every BozzettoTools member returns (see McpTools.fs's
/// withEcho/withEchoOutcome: always a single text block).
let private textOf (result: CallToolResult) : string =
  result.Content
  |> Seq.choose (function :? TextContentBlock as t -> Some t.Text | _ -> None)
  |> String.concat ""

/// Connect a fresh MCP client — a fresh client is a fresh transport session,
/// which is a fresh bound cohort identity (Mcp.fs's memberIdFor). Returned
/// as IAsyncDisposable so callers can `use!` it.
let connect (port: int) : Task<McpClient> =
  let opts = HttpClientTransportOptions(Endpoint = Uri(sprintf "http://localhost:%d/" port))
  let transport = HttpClientTransport(opts, (null: Microsoft.Extensions.Logging.ILoggerFactory))
  McpClient.CreateAsync(transport, null, null, CancellationToken.None)

let private callTool (client: McpClient) (name: string) (args: (string * obj) list) : Task<string> =
  task {
    let! result = client.CallToolAsync(name, readOnlyDict args, null, null, CancellationToken.None)
    return textOf result
  }

let private joinCohort (client: McpClient) (agentName: string) (role: string) =
  callTool client "join_cohort" [ "agentName", box agentName; "role", box role ]

/// Item 13c: `join_cohort`'s new optional `working_directory` — resolved to a
/// session id the same way `send_fsharp_code` resolves it (Mcp.fs's
/// `resolveSessionId`) and recorded on the joining member.
let private joinCohortWithDir (client: McpClient) (agentName: string) (role: string) (workingDirectory: string) =
  callTool client "join_cohort" [ "agentName", box agentName; "role", box role; "working_directory", box workingDirectory ]

let private createProjectSession (client: McpClient) (project: string) (workingDirectory: string) =
  callTool client "create_project_session" [ "project", box project; "working_directory", box workingDirectory ]

let private getCohortStatus (client: McpClient) =
  callTool client "get_cohort_status" []

let private acquireClaim (client: McpClient) (agentName: string) (scope: string) (purpose: string) =
  callTool client "acquire_claim" [ "agentName", box agentName; "scope", box scope; "purpose", box purpose ]

let private releaseClaim (client: McpClient) (agentName: string) (claimId: string) (fence: int64) =
  callTool client "release_claim" [ "agentName", box agentName; "claimId", box claimId; "fence", box fence ]

let private reassignClaim (client: McpClient) (agentName: string) (claimId: string) (toMember: string) =
  callTool client "reassign_claim" [ "agentName", box agentName; "claimId", box claimId; "toMember", box toMember ]

let private reclaimClaim (client: McpClient) (agentName: string) (claimId: string) (fence: int64) =
  callTool client "reclaim_claim" [ "agentName", box agentName; "claimId", box claimId; "fence", box fence ]

let private leaveCohort (client: McpClient) (agentName: string) =
  callTool client "leave_cohort" [ "agentName", box agentName ]

/// "Joined cohort as mcp:XXXX (Implementer)..." → "mcp:XXXX", the display
/// name `reassign_claim` takes as its recipient.
let private memberFromJoinResult (text: string) : string =
  let m = Text.RegularExpressions.Regex.Match(text, @"Joined cohort as (\S+) \(")
  if not m.Success then failwithf "join_cohort did not report a member: %s" text
  m.Groups.[1].Value

/// The `fence=` and `state=` get_cohort_status prints for one claim id.
let private claimRow (status: string) (claimId: string) : int64 * string =
  let m = Text.RegularExpressions.Regex.Match(status, Text.RegularExpressions.Regex.Escape claimId + @" .* fence=(\d+) state=(\w+)")
  if not m.Success then failwithf "status does not list claim %s:\n%s" claimId status
  int64 m.Groups.[1].Value, m.Groups.[2].Value

/// The renewal time get_cohort_status prints for the one present member
/// with `role` ("[Implementer] present renewed 2026-11-08 01:02:03Z").
let private renewalOf (status: string) (role: string) : DateTime =
  let m = Text.RegularExpressions.Regex.Match(status, Text.RegularExpressions.Regex.Escape(sprintf "[%s] present renewed " role) + @"(\S+ \S+)")
  if not m.Success then failwithf "status shows no renewal for a present %s:\n%s" role status
  DateTime.Parse(m.Groups.[1].Value, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AdjustToUniversal)

/// One lease decision as the daemon wired it: (decision, retryAfterSeconds,
/// queue position from the reason, leaseId).
type private LeaseReply = { Decision: string; RetryAfter: float; Position: int option; LeaseId: string option; Raw: string }

let private requestLease (client: McpClient) : Task<LeaseReply> = task {
  let! text = callTool client "acquire_full_build_lease" []
  use doc = Text.Json.JsonDocument.Parse text
  let root = doc.RootElement
  let str (name: string) = match root.TryGetProperty name with | true, v -> Some (v.GetString()) | _ -> None
  let retry = match root.TryGetProperty "retryAfterSeconds" with | true, v -> v.GetDouble() | _ -> 0.0
  let position =
    str "reason" |> Option.bind (fun reason ->
      let m = Text.RegularExpressions.Regex.Match(reason, @"you are #(\d+) in queue")
      if m.Success then Some (int m.Groups.[1].Value) else None)
  return { Decision = defaultArg (str "decision") "?"; RetryAfter = retry; Position = position; LeaseId = str "leaseId"; Raw = text }
}

let private releaseLease (client: McpClient) (leaseId: string) =
  callTool client "release_work_lease" [ "lease_id", box leaseId ]

/// "Acquired claim c-XXXX over ..." → "c-XXXX". Fragile-string-averse: this
/// parses OUR OWN tool's documented output shape (McpTools.fs's
/// acquire_claim description), not an unrelated format.
let private claimIdFromAcquireResult (text: string) : string =
  let marker = "Acquired claim "
  let start = text.IndexOf marker
  if start < 0 then failwithf "acquire_claim did not report success: %s" text
  let afterMarker = start + marker.Length
  let stop = text.IndexOf(' ', afterMarker)
  text.Substring(afterMarker, stop - afterMarker)

/// Run `body` against a freshly spawned, isolated daemon; the daemon is
/// killed afterwards even on failure.
let withDaemon (body: int -> Task<unit>) : Task<unit> = task {
  let! proc, port = startIsolatedDaemon ()
  try
    do! body port
  finally
    killDaemon proc
}

[<Tests>]
let cohortMcpToolsTests =
  Integration.hostList "Cohort MCP tools" [

    testTask "agent work is bound, idempotent and Observer self-leave remains claim-free" {
      do! withDaemon (fun port -> task {
        use! a = connect port
        use! b = connect port
        let args key seq = [
          "run", box key; "session", box "pi-session"; "sequence", box seq
          "name", box "same-name"; "model", box "reported"; "endpoint", box "LAN"
          "project", box "/repo"; "focus", box "visibility"; "operation", box "reading"; "status", box "running"
          "usageAvailable", box true; "input", box 10L; "output", box 0L
          "cacheRead", box 0L; "cacheWrite", box 0L; "total", box 10L; "estimateUsd", box -1. ]
        let invoke (client: McpClient) arguments = client.CallToolAsync("report_agent_work", readOnlyDict arguments, null, null, CancellationToken.None)
        let! absent = invoke a (args "run-a" 1L)
        absent.IsError.GetValueOrDefault() |> Expect.isTrue "unjoined reporting is a real MCP error"
        textOf absent |> Expect.stringContains "specific membership refusal" "member_not_present"
        let! _ = joinCohort a "same-name" "Implementer"
        let! _ = joinCohort b "same-name" "Observer"
        let! first = invoke a (args "run-a" 1L)
        first.IsError.GetValueOrDefault() |> Expect.isFalse "bound enrolled reporter accepted"
        let! duplicate = invoke a (args "run-a" 1L)
        textOf duplicate |> Expect.equal "same report is idempotent over actual MCP" (textOf first)
        let! forged = invoke b (args "run-a" 2L)
        forged.IsError.GetValueOrDefault() |> Expect.isTrue "same name cannot take over another connection's run"
        textOf forged |> Expect.stringContains "specific ownership refusal" "wrong_member"
        let! second = invoke b (args "run-b" 1L)
        second.IsError.GetValueOrDefault() |> Expect.isFalse "Observer can report its own work"
        let! denied = b.CallToolAsync("acquire_claim", readOnlyDict ["agentName", box "same-name"; "scope", box "file:Forbidden.fs"; "purpose", box "control"], null, null, CancellationToken.None)
        denied.IsError.GetValueOrDefault() |> Expect.isTrue "Observer gains no claim authority"
        let! departed = b.CallToolAsync("leave_cohort", readOnlyDict ["agentName", box "same-name"], null, null, CancellationToken.None)
        departed.IsError.GetValueOrDefault() |> Expect.isFalse "non-conductor Observer can self-leave"
        let! board = callTool a "get_agent_work" []
        board |> Expect.stringContains "departure projects as disconnected, never completed" "disconnected"
        board |> Expect.stringContains "independent Observer meter remains retained" "Observer"
        board |> Expect.stringContains "independent Implementer meter remains retained" "Implementer"
      })
    }

    testTask "join from one identity shows that member as conductor in get_cohort_status" {
      do! withDaemon (fun port -> task {
        use! alice = connect port

        let! joinResult = joinCohort alice "alice" "Implementer"
        joinResult
        |> Expect.stringContains "the first joiner becomes conductor" "You are the conductor"

        let! status = getCohortStatus alice
        status
        |> Expect.stringContains "status should list the joined member as Present" "[Implementer] present"

        status
        |> Expect.stringContains "the conductor line should name a real member, not the unknown placeholder" "Conductor: mcp:"
      })
    }

    testTask "WHY — join_cohort with no resolvable session still joins, but says so honestly (item 13c)" {
      do! withDaemon (fun port -> task {
        use! client = connect port
        // No session exists yet in this fresh, isolated daemon, and no
        // working_directory is given — resolution falls all the way through
        // to Gone, so the member joins with no session bound.
        let! joinResult = joinCohort client "no-session-agent" "Observer"
        joinResult
        |> Expect.stringContains "the member still joins" "Joined cohort as"
        joinResult
        |> Expect.stringContains "an unresolved session is reported honestly, not silently dropped" "No session was resolved"
      })
    }

    testTask "join from a second identity shows two members, second is not conductor" {
      do! withDaemon (fun port -> task {
        use! a = connect port
        use! b = connect port

        let! _ = joinCohort a "member-a" "Implementer"
        let! secondJoin = joinCohort b "member-b" "Verifier"
        secondJoin
        |> Expect.stringContains "the second joiner is a member" "Joined cohort as"
        secondJoin.Contains "You are the conductor"
        |> Expect.isFalse "the second joiner must not become conductor"

        let! status = getCohortStatus a
        status
        |> Expect.stringContains "status should report two members" "Members (2):"
        status
        |> Expect.stringContains "the verifier role should be visible" "[Verifier] present"
      })
    }

    testTask "acquire a claim, status shows it held by the acquirer" {
      do! withDaemon (fun port -> task {
        use! c = connect port

        let! _ = joinCohort c "claimant" "Implementer"
        let! acquireResult = acquireClaim c "claimant" "file:src/Acquired.fs" "editing Acquired.fs"
        acquireResult
        |> Expect.stringContains "acquire_claim should report the new claim" "Acquired claim c-"

        let claimId = claimIdFromAcquireResult acquireResult

        let! status = getCohortStatus c
        status
        |> Expect.stringContains "status should list the acquired claim by id" claimId
        status
        |> Expect.stringContains "status should show the claim held by a real member, not '(none)'" "held-by=mcp:"
      })
    }

    testTask "release by a Verifier is refused before reaching the core (Slice 3 role gate)" {
      do! withDaemon (fun port -> task {
        use! holder = connect port
        use! other = connect port

        let! _ = joinCohort holder "holder" "Implementer"
        let! _ = joinCohort other "bystander" "Verifier"
        let! acquireResult = acquireClaim holder "holder" "file:src/NotHeld2.fs" "editing NotHeld2.fs"
        let claimId = claimIdFromAcquireResult acquireResult

        // A Verifier's `cohortTools` (Affordances.fs, Slice 3) does not
        // include `release_claim` — refused at the authority gate, before
        // `Cohort.decide` ever sees the command (never a NotClaimHolder).
        let! releaseResult = releaseClaim other "bystander" claimId 1L
        releaseResult
        |> Expect.stringContains "a Verifier's release_claim call must be refused by the role gate" "does not permit it"
      })
    }

    testTask "reassign by a non-conductor is refused before reaching the core (Slice 3 role gate)" {
      do! withDaemon (fun port -> task {
        use! conductor = connect port
        use! nonConductor = connect port

        // conductor joins FIRST — v1's implicit create_cohort semantics
        // (Cohort.fs: the first joiner of an empty cohort becomes conductor).
        let! _ = joinCohort conductor "the-conductor" "Implementer"
        let! _ = joinCohort nonConductor "not-the-conductor" "Verifier"
        let! acquireResult = acquireClaim conductor "the-conductor" "file:src/Reassign.fs" "editing Reassign.fs"
        let claimId = claimIdFromAcquireResult acquireResult

        // Slice 3 (item 11): `reassign_claim` is in NO role's `cohortTools`
        // set except `Conductor` — a caller whose Authority (resolved from
        // the same frame `Cohort.decide` itself reads) is not Conductor is
        // always refused HERE, at the role gate, before `Cohort.decide` runs
        // at all. This makes the core's own `NotConductor` refusal
        // unreachable via this MCP path (it is still exercised directly
        // against `decide` by CohortPropertyTests.fs's property 19) — the
        // role gate is a strictly earlier, friendlier version of the same
        // guarantee, not a bypass of it.
        let! reassignResult = reassignClaim nonConductor "not-the-conductor" claimId "the-conductor"
        reassignResult
        |> Expect.stringContains "a non-conductor's reassign must be refused by the role gate" "does not permit it"
      })
    }
    // Bozzetto #22 and #24 over real MCP sessions against this isolated daemon
    // (never the shared one): a waiter that stops polling (a killed client
    // sends no withdraw and no DELETE) holds the head of the queue only until
    // its entry expires; the live waiters behind it keep their places and are
    // granted in order; and a cohort member whose only traffic is that lease
    // poll is renewed, so its membership and claim survive the wait. Real
    // intervals: the daemon has no injectable clock, and the shortest queue
    // lifetime is ExpensiveWorkLease.queuedAskLifetime (two minutes).
    testTask "a killed lease waiter expires, live waiters keep FIFO places and are granted in order, and a waiting member stays present (Bozzetto #22 #24)" {
      do! withDaemon (fun port -> task {
        let clients = ResizeArray<McpClient>()
        let open' () = task {
          let! c = connect port
          clients.Add c
          return c }
        try
          let! b = open' ()
          let! _ = joinCohort b "waiter-b" "Implementer"
          let! acquired = acquireClaim b "waiter-b" "project:Bozzetto/src/Bozzetto.Core/Bozzetto.Core.fsproj" "held across a lease wait"
          let claimId = claimIdFromAcquireResult acquired
          let! atJoin = getCohortStatus b
          let renewedAtJoin = renewalOf atJoin "Implementer"
          // Fill whatever cap the machine's memory pressure allows (4, 1, or 0).
          let holders = ResizeArray<McpClient * string>()
          let mutable firstWaiter = None
          while firstWaiter.IsNone do
            if holders.Count > Bozzetto.ExpensiveWorkLease.maxConcurrentFor Bozzetto.MemoryPressure.Normal then
              failtest "more grants than the Normal cap: the pool is not admitting by cap"
            let! c = open' ()
            let! reply = requestLease c
            match reply.Decision, reply.LeaseId with
            | "granted", Some id -> holders.Add((c, id))
            | "wait", _ -> firstWaiter <- Some (c, reply)
            | _ -> failtestf "unexpected lease reply: %s" reply.Raw
          if holders.Count = 0 then
            failtestf "the machine is at critical memory pressure (cap 0), so no lease can be granted and the acceptance cannot be observed: %s" (snd firstWaiter.Value).Raw
          let a, aFirst = firstWaiter.Value
          aFirst.Position |> Expect.equal "the killed-to-be waiter heads the queue" (Some 1)
          let! bFirst = requestLease b
          bFirst.Position |> Expect.equal "the member queues second" (Some 2)
          let! c = open' ()
          let! cFirst = requestLease c
          cFirst.Position |> Expect.equal "a third live waiter queues third" (Some 3)
          // A's last poll; from here it is gone. Then free all capacity.
          let! aLast = requestLease a
          let killedAt = DateTime.UtcNow
          aLast.Position |> Expect.equal "a retry keeps the head position" (Some 1)
          for holder, id in holders do
            let! released = releaseLease holder id
            released |> Expect.stringContains "the holder's own lease is released" "released"
          // B and C poll concurrently at the advised interval, as the client does.
          let poll (client: McpClient) (first: LeaseReply) (onGrant: string -> Task<unit>) = task {
            let log = ResizeArray<DateTime * LeaseReply>()
            let mutable reply = first
            let deadline = killedAt + Bozzetto.ExpensiveWorkLease.queuedAskLifetime + TimeSpan.FromMinutes 2.0
            while reply.Decision = "wait" && DateTime.UtcNow < deadline do
              do! Task.Delay(TimeSpan.FromSeconds(Math.Ceiling reply.RetryAfter))
              let! next = requestLease client
              log.Add((DateTime.UtcNow, next))
              reply <- next
            match reply.Decision, reply.LeaseId with
            | "granted", Some id -> do! onGrant id
            | _ -> ()
            return log
          }
          let bGrantedAt = ref DateTime.MaxValue
          let cGrantedAt = ref DateTime.MaxValue
          let grantedThenRelease (client: McpClient) (at: DateTime ref) (id: string) = task {
            at.Value <- DateTime.UtcNow
            let! _ = releaseLease client id
            return () }
          let bTask = poll b bFirst (grantedThenRelease b bGrantedAt)
          let cTask = poll c cFirst (grantedThenRelease c cGrantedAt)
          let! bLog = bTask
          let! cLog = cTask
          let positions (log: ResizeArray<DateTime * LeaseReply>) = log |> Seq.choose (fun (_, r) -> r.Position) |> List.ofSeq
          (bGrantedAt.Value < DateTime.MaxValue) |> Expect.isTrue (sprintf "the surviving member is granted once the abandoned head expires; polls: %A" (positions bLog))
          (cGrantedAt.Value < DateTime.MaxValue) |> Expect.isTrue (sprintf "the third waiter is granted too; polls: %A" (positions cLog))
          (bGrantedAt.Value <= cGrantedAt.Value) |> Expect.isTrue "grants follow arrival order: the member before the third waiter"
          positions bLog |> List.forall (fun p -> p <= 2) |> Expect.isTrue (sprintf "the member never loses its place: %A" (positions bLog))
          positions cLog |> List.forall (fun p -> p <= 3) |> Expect.isTrue (sprintf "the third waiter never loses its place: %A" (positions cLog))
          positions bLog |> List.contains 2 |> Expect.isTrue "with capacity free, the abandoned head still blocked the member until it expired"
          (bGrantedAt.Value - killedAt >= Bozzetto.ExpensiveWorkLease.queuedAskLifetime - TimeSpan.FromSeconds 5.0)
          |> Expect.isTrue (sprintf "the head is held for its lifetime, not dropped early (granted %O after the kill)" (bGrantedAt.Value - killedAt))
          // Bozzetto #24: the member's only traffic was the lease poll.
          let! afterWait = getCohortStatus b
          (renewalOf afterWait "Implementer" > renewedAtJoin)
          |> Expect.isTrue (sprintf "lease polls renewed the waiting member (joined %O):\n%s" renewedAtJoin afterWait)
          claimRow afterWait claimId |> snd |> Expect.equal "its claim is still held" "Held"
        finally
          for client in clients do
            try (client :> IAsyncDisposable).DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds 5.0) |> ignore with _ -> ()
      })
    }

    testTask "an orphaned claim blocks others; its holder reclaims with the last fence; the conductor can still reassign (Bozzetto #24)" {
      do! withDaemon (fun port -> task {
        use! conductor = connect port
        use! holder = connect port
        use! other = connect port
        let! _ = joinCohort conductor "the-conductor" "Implementer"
        let! _ = joinCohort holder "holder" "Implementer"
        let! otherJoin = joinCohort other "other" "Implementer"
        let otherMember = memberFromJoinResult otherJoin
        let! acquired = acquireClaim holder "holder" "project:Bozzetto/src/Bozzetto.Core/Bozzetto.Core.fsproj" "editing Core"
        let claimId = claimIdFromAcquireResult acquired
        let! _ = leaveCohort holder "holder"
        let! orphaned = getCohortStatus conductor
        let lastFence, state = claimRow orphaned claimId
        state |> Expect.equal "leaving orphans the holder's claim" "Orphaned"
        let! blocked = acquireClaim other "other" "file:Bozzetto/src/Bozzetto.Core/Cohort.fs" "editing Cohort.fs"
        blocked |> Expect.stringContains "the orphan blocks an overlapping acquire" (sprintf "overlaps orphaned claim %s" claimId)
        let! stranger = reclaimClaim other "other" claimId lastFence
        stranger |> Expect.stringContains "only the previous holder may reclaim" "does not hold claim"
        let! _ = joinCohort holder "holder" "Implementer"
        let! stale = reclaimClaim holder "holder" claimId (lastFence - 1L)
        stale |> Expect.stringContains "a wrong fence is refused" "stale"
        let! reclaimed = reclaimClaim holder "holder" claimId lastFence
        reclaimed |> Expect.stringContains "the holder reclaims with the last fence" (sprintf "Reclaimed claim %s" claimId)
        let! held = getCohortStatus conductor
        claimRow held claimId |> snd |> Expect.equal "the claim is held again" "Held"
        let! _ = leaveCohort holder "holder"
        let! reassigned = reassignClaim conductor "the-conductor" claimId otherMember
        reassigned |> Expect.stringContains "the conductor reassigns an orphan" (sprintf "Reassigned claim %s to %s" claimId otherMember)
      })
    }
  ]
