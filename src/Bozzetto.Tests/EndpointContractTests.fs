module Bozzetto.Tests.EndpointContractTests

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Features
open Bozzetto.Providers
open Bozzetto.ComposerIntegration
open Bozzetto.EndpointContracts
module CommandJournal = Bozzetto.ComposerIntegration.ComposerJournal
module Server = Bozzetto.Server.McpServer
module CommandWire = Bozzetto.Web.Shared.Protocol
module CommandCodec = Bozzetto.Web.Shared.BinaryCodec

let private accepted = function Ok value -> value | Error reason -> failtest reason

let private withCommandServer durable body = task {
  let cache =
    match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" with
    | value when not (String.IsNullOrWhiteSpace value) -> value
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
  let directory = Path.Combine(cache, "bozzetto", "lease-command-tests", Guid.NewGuid().ToString "N")
  Directory.CreateDirectory directory |> ignore
  let store = CommandJournal.openStore (Path.Combine(directory, "operations.sqlite")) |> accepted
  let factory () = Task.FromException<IComposerWorker>(InvalidOperationException "Command reporting cannot start a compiler worker.")
  let supervisor =
    if durable then new ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask), journalStore = Ok store)
    else new ComposerSupervisor(factory, false, (fun () -> Task.CompletedTask))
  let builder = WebApplication.CreateBuilder()
  builder.Logging.ClearProviders() |> ignore
  builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
  let app = builder.Build()
  app.Use(Func<HttpContext, Func<Task>, Task>(fun context next -> Server.errorHandlingMiddleware context next :> Task)) |> ignore
  Server.useOriginGuard (Bozzetto.Server.HttpOriginGuard.OwnOrigins.ofPorts [47749; 47750]) app
  Server.mapLeaseCommandRoutes app (Some supervisor)
  LeaseWatch.reset ()
  try
    do! app.StartAsync()
    use client = new HttpClient(BaseAddress = Uri(Seq.exactlyOne app.Urls), Timeout = TimeSpan.FromSeconds 5.)
    do! body client supervisor store
  finally
    LeaseWatch.reset ()
    supervisor.StopAsync().GetAwaiter().GetResult()
    app.StopAsync().GetAwaiter().GetResult()
    app.DisposeAsync().AsTask().GetAwaiter().GetResult()
    Directory.Delete(directory, true)
}

let private granted holder kind =
  match LeaseWatch.request holder kind with
  | ExpensiveWorkLease.Decision.Granted(id, _) -> ExpensiveWorkLease.LeaseId.value id
  | decision -> failtestf "Fixture needs a granted lease: %A" decision

let private commandBody leaseId : CommandWire.CommandObservationRequest = {
  Holder = "http-command-owner"; Project = "/primary/project"; Kind = "full_build"
  LeaseId = leaseId; RunId = "command-run"; Executable = "/usr/bin/dotnet"
  Arguments = [| "build"; "project with spaces.fsproj" |]; ProcessId = 123; StartedAtMs = 1000L
  Observation = CommandWire.CommandStarted }

let private terminalBody fields =
  let result: CommandWire.CommandObservationResult = {
    FinishedAtMs = 1200L; ExitCode = 0; StandardOutput = "last output"; StandardError = ""
    OutputTruncated = true; ErrorTruncated = false }
  { fields with CommandWire.CommandObservationRequest.Observation = CommandWire.CommandFinished result }

let private postBytes (client: HttpClient) suffix mediaType bytes = task {
  use content = new ByteArrayContent(bytes: byte array)
  content.Headers.ContentType <- Net.Http.Headers.MediaTypeHeaderValue(mediaType)
  use! response = client.PostAsync("/api/lease/command/" + suffix, content)
  response.Content.Headers.ContentType.MediaType |> Expect.equal "typed response media type" CommandWire.CommandObservationMediaType
  let! raw = response.Content.ReadAsByteArrayAsync()
  return int response.StatusCode, CommandCodec.decodeCommandObservationReply raw |> accepted
}

let private post client suffix request =
  postBytes client suffix CommandWire.CommandObservationMediaType (CommandCodec.encodeCommandObservationRequest request)

let private requireSuccess (status, response) =
  status |> Expect.equal "HTTP success requires persisted command observation" 200
  match response with
  | CommandWire.CommandObservationAccepted sequence -> sequence
  | CommandWire.CommandObservationRefused reason -> failtest reason

let private requireRefusal status (fragment: string) (actual, response) =
  actual |> Expect.equal "specific HTTP refusal" status
  match response with
  | CommandWire.CommandObservationAccepted _ -> failtest "refusal cannot report success"
  | CommandWire.CommandObservationRefused reason ->
    reason.Contains(fragment, StringComparison.Ordinal) |> Expect.isTrue ("refusal retains reason: " + fragment)

[<Tests>]
let endpointContractTests = testList "EndpointContracts" [

  testList "Neovim contract" [
    test "all Neovim contract endpoints exist in daemon" {
      let missing = missingEndpoints neovimContract
      missing
      |> Expect.isEmpty
        (sprintf "Neovim contract has endpoints missing from daemon: %A" missing)
    }

    test "Neovim contract has expected count" {
      neovimContract
      |> List.length
      |> Expect.equal "should have 15 endpoints" 15
    }

    test "Neovim contract includes SSE events endpoint" {
      neovimContract
      |> List.exists (fun (m, p) -> m = GET && p = "/events")
      |> Expect.isTrue "SSE /events must be in Neovim contract"
    }

    test "Neovim contract includes /exec" {
      neovimContract
      |> List.exists (fun (m, p) -> m = POST && p = "/exec")
      |> Expect.isTrue "/exec must be in Neovim contract"
    }

    test "Neovim contract includes session management" {
      let sessionPaths =
        neovimContract
        |> List.filter (fun (_, p) -> p.Contains("/api/sessions"))
        |> List.length
      (sessionPaths, 4)
      |> Expect.isGreaterThanOrEqual "at least 4 session endpoints"
    }

    test "Neovim contract includes live testing" {
      let testPaths =
        neovimContract
        |> List.filter (fun (_, p) -> p.Contains("/api/live-testing"))
        |> List.length
      (testPaths, 3)
      |> Expect.isGreaterThanOrEqual "at least 3 testing endpoints"
    }
  ]

  testList "VS Code contract" [
    test "all VS Code contract endpoints exist in daemon" {
      let missing = missingEndpoints vscodeContract
      missing
      |> Expect.isEmpty
        (sprintf "VS Code contract has endpoints missing from daemon: %A" missing)
    }

    test "VS Code contract has expected count" {
      vscodeContract
      |> List.length
      |> Expect.equal "should have 13 endpoints" 13
    }
  ]

  testList "API version" [
    test "apiVersion is a positive integer" {
      (apiVersion, 1)
      |> Expect.isGreaterThanOrEqual "apiVersion must be at least 1"
    }

    test "apiVersion matches current contract shape" {
      // Pin the version so any contract change forces a conscious version bump
      apiVersion
      |> Expect.equal "apiVersion should be 4 for current contract" 4
    }

    test "VS Code extension expectedApiVersion matches daemon apiVersion" {
      // Cross-boundary contract test: reads the Fable source to extract the
      // literal expectedApiVersion and asserts it matches the daemon's value.
      // This prevents the exact mismatch bug reported as "apiVersion=2 is
      // incompatible with this extension (requires v1)".
      let clientFs =
        System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "bozzetto-vscode", "src", "BozzettoClient.fs")
        |> System.IO.File.ReadAllText
      let m =
        System.Text.RegularExpressions.Regex.Match(
          clientFs,
          @"let\s+\[<Literal>\]\s+expectedApiVersion\s*=\s*(\d+)")
      m.Success
      |> Expect.isTrue "should find expectedApiVersion literal in BozzettoClient.fs"
      let extensionVersion = int m.Groups.[1].Value
      extensionVersion
      |> Expect.equal
        "VS Code extension expectedApiVersion must match EndpointContracts.apiVersion"
        apiVersion
    }
  ]

  testList "Endpoint registry" [
    test "all endpoints have non-empty path" {
      all
      |> List.filter (fun ep -> System.String.IsNullOrEmpty(ep.path))
      |> Expect.isEmpty "no endpoint should have empty path"
    }

    test "all endpoints have non-empty description" {
      all
      |> List.filter (fun ep -> System.String.IsNullOrEmpty(ep.description))
      |> Expect.isEmpty "no endpoint should have empty description"
    }

    test "all endpoint paths start with /" {
      all
      |> List.filter (fun ep -> not (ep.path.StartsWith("/")))
      |> Expect.isEmpty "all paths must start with /"
    }

    test "no duplicate endpoint definitions" {
      let dupes =
        all
        |> List.groupBy (fun ep -> (ep.method, normalizePath ep.path))
        |> List.filter (fun (_, group) -> group.Length > 1)
        |> List.map fst
      dupes
      |> Expect.isEmpty (sprintf "duplicate endpoints: %A" dupes)
    }

    test "session-scoped buffer changed endpoint exists" {
      all
      |> List.exists (fun ep -> ep.method = POST && ep.path = "/api/sessions/{sid}/buffer-changed")
      |> Expect.isTrue "daemon must expose a session-scoped buffer changed endpoint for unsaved editor content"
    }

    test "live-testing run route is mapped in McpServer" {
      let serverFs =
        System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "Bozzetto", "McpServer.fs")
      let source = System.IO.File.ReadAllText serverFs

      source.Contains("app.MapPost(\"/api/live-testing/run\"")
      |> Expect.isTrue "McpServer must map POST /api/live-testing/run"
    }

    test "endpoint count pinned — update contracts when adding endpoints" {
      // CI GATE: This count is pinned. When you add a daemon endpoint:
      // 1. Add it to the appropriate list in EndpointContracts.fs
      // 2. Decide which plugin contracts need it (neovimContract, vscodeContract)
      // 3. Bump apiVersion if it's a breaking change
      // 4. Update this count
      all
      |> List.length
      |> Expect.equal
        "endpoint count changed — update contracts and bump this number"
        34
    }
  ]

  testList "Contract validation" [
    test "normalizePath replaces all placeholders" {
      normalizePath "/api/sessions/{sid}/warmup-context"
      |> Expect.equal "should normalize" "/api/sessions/{id}/warmup-context"
    }

    test "normalizePath handles multiple placeholders" {
      normalizePath "/api/{a}/foo/{b}/bar"
      |> Expect.equal "should normalize both" "/api/{id}/foo/{id}/bar"
    }

    test "normalizePath leaves paths without placeholders unchanged" {
      normalizePath "/api/sessions"
      |> Expect.equal "should be unchanged" "/api/sessions"
    }

    test "uncovered endpoints returns daemon endpoints not in any contract" {
      let uncovered = uncoveredEndpoints [neovimContract; vscodeContract]
      // There should be some uncovered endpoints (dashboard, diagnostics, etc.)
      (uncovered |> List.length, 0)
      |> Expect.isGreaterThan "some endpoints are uncovered"
    }

    testProperty "missingEndpoints returns empty for subset of all" <| fun (idx: int) ->
      let safeIdx = abs idx % (max 1 (all.Length))
      let ep = all.[safeIdx]
      let contract = [(ep.method, ep.path)]
      missingEndpoints contract |> List.isEmpty
  ]
]

[<Tests>]
let leasedCommandTests =
  testSequencedGroup "lease-watch-global"
  <| testList "Leased command HTTP" [
    testCase "lease capability matches holder kind and unexpired grant only"
    <| fun () ->
      let now = DateTimeOffset.UtcNow
      let id = ExpensiveWorkLease.LeaseId.ofWire "capability"

      let grant: ExpensiveWorkLease.ActiveLease = {
        Id = id
        Holder = "holder"
        Kind = ExpensiveWorkLease.Kind.FullBuild
        GrantedAt = now
        ExpiresAt = now.AddSeconds 10.
      }

      let pool = {
        ExpensiveWorkLease.empty with
            Active = [ grant ]
      }

      LeaseWatch.matchesAt now "holder" ExpensiveWorkLease.Kind.FullBuild id pool
      |> Expect.isTrue "the exact live grant is available"

      for instant, holder, kind, capability in
        [
          now, "other", ExpensiveWorkLease.Kind.FullBuild, id
          now, "holder", ExpensiveWorkLease.Kind.TestSuiteRun, id
          now, "holder", ExpensiveWorkLease.Kind.FullBuild, ExpensiveWorkLease.LeaseId.ofWire "invented"
          grant.ExpiresAt, "holder", ExpensiveWorkLease.Kind.FullBuild, id
        ] do
        LeaseWatch.matchesAt instant holder kind capability pool
        |> Expect.isFalse "holder, kind, id and expiry each bind admission"

    testTask "start requires exact live admission and retries retain one durable row" {
      do!
        withCommandServer true (fun client _ store ->
          task {
            let leaseId = granted "http-command-owner" ExpensiveWorkLease.Kind.FullBuild
            let original = commandBody leaseId

            for changed in
              [
                { original with LeaseId = "invented" }
                { original with Holder = "other" }
                {
                  original with
                      Kind = "test_suite_run"
                }
              ] do
              let! refused = post client "start" changed
              requireRefusal 409 "live lease matching its id, holder and kind" refused

            (store.Read() |> accepted).Entries
            |> Expect.isEmpty "refused starts never enter durable history"

            let! first = post client "start" original
            let sequence = requireSuccess first
            let! retry = post client "start" original

            requireSuccess retry
            |> Expect.equal "same retry returns original identity" sequence

            let entries = (store.Read() |> accepted).Entries
            entries.Length |> Expect.equal "one durable operation" 1

            entries.Head.Outcome
            |> Expect.equal "running is visible before finish" CommandJournal.Outcome.Running

            let! conflict = post client "start" { original with ProcessId = 124 }
            requireRefusal 409 "immutable context changed" conflict

            (store.Read() |> accepted).Entries.Length
            |> Expect.equal "failed retry does not add a durable row" 1
          })
    }
    testTask "finish after lease release retains exact terminal identity and truncation flags" {
      do!
        withCommandServer true (fun client _ store ->
          task {
            let leaseId = granted "http-command-owner" ExpensiveWorkLease.Kind.FullBuild
            let original = commandBody leaseId
            let! begun = post client "start" original
            let sequence = requireSuccess begun
            LeaseWatch.release (ExpensiveWorkLease.LeaseId.ofWire leaseId) |> ignore
            let terminal = terminalBody original

            for changed in
              [
                { terminal with Holder = "other" }
                {
                  terminal with
                      Project = "/other/project"
                }
                { terminal with Arguments = [||] }
              ] do
              let! refused = post client "finish" changed
              requireRefusal 409 "immutable context changed" refused

            let! completed = post client "finish" terminal

            requireSuccess completed
            |> Expect.equal "joined result belongs to issued operation" sequence

            let settledVersion = (store.Read() |> accepted).Sequence
            let! retry = post client "finish" terminal

            requireSuccess retry
            |> Expect.equal "exact terminal retry is idempotent" sequence

            let journal = store.Read() |> accepted

            journal.Sequence
            |> Expect.equal "retry cannot invent another journal change" settledVersion

            let result =
              journal.Entries.Head.Run
              |> Option.defaultWith (fun () -> failtest "terminal transcript missing")

            result.OutputTruncated
            |> Expect.isTrue "caller truncation remains true even for short tail"

            result.ErrorTruncated |> Expect.isFalse "stderr flag remains independent"

            result.Compiler
            |> Expect.isNone "a shell command has no compiler proof provenance"

            let changedResult =
              match terminal.Observation with
              | CommandWire.CommandFinished result -> {
                  terminal with
                      Observation = CommandWire.CommandFinished { result with ExitCode = 1 }
                }
              | _ -> failtest "fixture must be terminal"

            let! conflicting = post client "finish" changedResult
            requireRefusal 409 "different terminal result" conflicting
          })
    }
    testTask "finish cannot invent a command and released leases cannot begin one" {
      do!
        withCommandServer true (fun client _ store ->
          task {
            let leaseId = granted "http-command-owner" ExpensiveWorkLease.Kind.FullBuild
            let original = commandBody leaseId
            let! terminal = post client "finish" (terminalBody original)
            requireRefusal 409 "not retained in this journal" terminal
            LeaseWatch.release (ExpensiveWorkLease.LeaseId.ofWire leaseId) |> ignore
            let! begun = post client "start" original
            requireRefusal 409 "live lease matching its id, holder and kind" begun

            (store.Read() |> accepted).Entries
            |> Expect.isEmpty "neither refusal creates evidence"
          })
    }
    testTask "malformed and oversized metadata refuses without cutting command identity" {
      do!
        withCommandServer true (fun client _ store ->
          task {
            let leaseId = granted "http-command-owner" ExpensiveWorkLease.Kind.FullBuild
            let original = commandBody leaseId

            for field, request in
              [
                "holder",
                {
                  original with
                      Holder = String('h', CommandJournal.labelLimit + 1)
                }
                "project",
                {
                  original with
                      Project = String('p', CommandJournal.textLimit + 1)
                }
                "kind", { original with Kind = "run_app" }
                "processId", { original with ProcessId = 0 }
                "arguments",
                {
                  original with
                      Arguments = [| String('a', 32769) |]
                }
                "executable", { original with Executable = "dotnet" }
              ] do
              let! refused = post client "start" request
              requireRefusal 400 field refused

            let! wrongPhase = post client "finish" original
            requireRefusal 400 "phase does not match" wrongPhase
            let terminal = CommandCodec.encodeCommandObservationRequest (terminalBody original)

            let! truncated =
              postBytes client "finish" CommandWire.CommandObservationMediaType terminal[.. terminal.Length - 2]

            fst truncated
            |> Expect.equal "missing typed flag refuses the incomplete frame" 400

            let encodedStart = CommandCodec.encodeCommandObservationRequest original
            for mismatchedVersion in [ 9uy; 11uy; 13uy; 15uy ] do
              let skewed = Array.copy encodedStart
              skewed[4] <- mismatchedVersion
              let! refused = postBytes client "start" CommandWire.CommandObservationMediaType skewed
              requireRefusal 400 "protocol_mismatch" refused
              requireRefusal 400 "unsupported version" refused

            let! json =
              postBytes client "start" "application/json" (Text.Encoding.UTF8.GetBytes "{}")

            requireRefusal 415 "Request rejected" json
            client.DefaultRequestHeaders.Add("Origin", "http://foreign.example:9999")
            let! foreignOrigin = post client "start" original
            client.DefaultRequestHeaders.Remove "Origin" |> ignore
            requireRefusal 403 "Request rejected" foreignOrigin

            let! oversized =
              postBytes
                client
                "start"
                CommandWire.CommandObservationMediaType
                (Array.zeroCreate (CommandWire.MaxCommandObservationBytes + 1))

            requireRefusal 413 "body bound" oversized

            (store.Read() |> accepted).Entries
            |> Expect.isEmpty "invalid requests create no durable authority"
          })
    }
    testTask "stop targets only the opted-in isolated command and keeps its admission until settlement" {
      if OperatingSystem.IsLinux() then
        do! withCommandServer true (fun client supervisor _ -> task {
          let holder = "http-command-owner"
          let lease = granted holder ExpensiveWorkLease.Kind.FullBuild
          let start isolated marked =
            let command = { commandBody lease with RunId = Guid.NewGuid().ToString "N"; StartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            let psi = Diagnostics.ProcessStartInfo((if isolated then "/usr/bin/setsid" else "/bin/sh"), UseShellExecute = false, RedirectStandardOutput = true)
            if isolated then psi.ArgumentList.Add "/bin/sh"
            for arg in ["-c"; "echo ready && exec /bin/sleep 20"] do psi.ArgumentList.Add arg
            if marked then psi.Environment["BOZZETTO_LEASED_RUN_ID"] <- command.RunId
            else psi.Environment.Remove("BOZZETTO_LEASED_RUN_ID") |> ignore
            let child = Diagnostics.Process.Start psi
            child.StandardOutput.ReadLine() |> Expect.equal "fixture entered its isolated process group" "ready"
            child, { command with ProcessId = child.Id; Executable = "/bin/sleep"; Arguments = [|"20"|] }
          let child, selected = start true true
          let other, unmarked = start true false
          let shared, sharedGroup = start false true
          use child = child
          use other = other
          use shared = shared
          try
            let! startSelected = post client "start" selected
            let seq = requireSuccess startSelected
            let! startOther = post client "start" unmarked
            let otherSeq = requireSuccess startOther
            let! startShared = post client "start" sharedGroup
            let sharedSeq = requireSuccess startShared
            let journal = supervisor.Journal.Incarnation
            let stop id row run = supervisor.StopLeasedCommand(id,row,run) |> Async.StartAsTask
            supervisor.CurrentLivenessSnapshot().StoppableRunIds |> Expect.equal "only the marker-bearing root opted in" (Set.singleton selected.RunId)
            for id, row, run, reason in
              [ "stale", seq, selected.RunId, "The operation journal is unavailable or has changed."
                journal, otherSeq, selected.RunId, "The selected running leased command is no longer current."
                journal, seq, unmarked.RunId, "The selected running leased command is no longer current."
                journal, sharedSeq, sharedGroup.RunId, "The command did not register an owned isolated process for stopping."
                journal, otherSeq, unmarked.RunId, "The command did not register an owned isolated process for stopping." ] do
              let! refused = stop id row run
              refused |> Expect.equal "wrong identity or registration refused" (Error reason)
            (child.HasExited || other.HasExited || shared.HasExited) |> Expect.isFalse "all refusals leave every fixture root running"
            let! joined = Task.WhenAll([|stop journal seq selected.RunId; stop journal seq selected.RunId|])
            joined |> Expect.equal "both requests join the same fenced cleanup" [|Ok (); Ok ()|]
            child.WaitForExit(2000) |> Expect.isTrue "the exact root exited"
            other.HasExited |> Expect.isFalse "the other registered root remains alive"
            supervisor.CurrentLivenessSnapshot().StoppableRunIds |> Expect.isEmpty "a latched stop cannot be offered again"
            LeaseWatch.matchesLive holder ExpensiveWorkLease.Kind.FullBuild (ExpensiveWorkLease.LeaseId.ofWire lease)
            |> Expect.isTrue "the runner retains admission through reporting"
            let result: CommandWire.CommandObservationResult =
              { FinishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); ExitCode = child.ExitCode; StandardOutput = ""; StandardError = ""
                OutputTruncated = false; ErrorTruncated = false }
            let! terminal = post client "finish" { selected with Observation = CommandWire.CommandFinished result }
            requireSuccess terminal |> Expect.equal "the runner settles its original operation" seq
            let! late = stop journal seq selected.RunId
            late |> Expect.equal "terminal observations cannot be signalled" (Error "The selected running leased command is no longer current.")
          finally
            for fixture in [child;other;shared] do
              if not fixture.HasExited then fixture.Kill(true)
              fixture.WaitForExit()
        })
    }
    testTask "stop joins only the selected process group and refuses a lost root or lease" {
      if OperatingSystem.IsLinux() then
        do! withCommandServer true (fun client supervisor _ -> task {
          let holder = "http-command-owner"
          let lease = granted holder ExpensiveWorkLease.Kind.FullBuild
          let leaseId = ExpensiveWorkLease.LeaseId.ofWire lease
          let journal = supervisor.Journal.Incarnation
          let otherHolder = "http-command-sibling"
          let otherLease = granted otherHolder ExpensiveWorkLease.Kind.FullBuild
          let start owner grant =
            let command = { commandBody grant with Holder = owner; RunId = Guid.NewGuid().ToString "N"; StartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            let script = "sleep 30 & a=$!; sleep 30 & echo $$ $a $!; wait"
            let psi = Diagnostics.ProcessStartInfo("/usr/bin/setsid", UseShellExecute = false, RedirectStandardOutput = true)
            for arg in ["/bin/sh"; "-c"; script] do psi.ArgumentList.Add arg
            psi.Environment["BOZZETTO_LEASED_RUN_ID"] <- command.RunId
            let root = Diagnostics.Process.Start psi
            // Shell output names only the test's children; acquire handles now for independent cleanup after a failed kill.
            let ids =
              try root.StandardOutput.ReadLine().Split(' ') |> Array.map int
              with _ ->
                if not root.HasExited then root.Kill(true)
                root.WaitForExit()
                root.Dispose()
                reraise ()
            ids[0] |> Expect.equal "the shell owns the isolated group" root.Id
            let children = [for pid in ids[1..] -> Diagnostics.Process.GetProcessById pid]
            let cleanup =
              { new IDisposable with
                  member _.Dispose() =
                    for proc in children @ [root] do
                      if not proc.HasExited then proc.Kill()
                      let stopped () =
                        Bozzetto.Server.LinuxProcessEdge.readLinuxProc proc.Id
                        |> Option.forall (ProcessWatchdog.isLiveRecord >> not)
                      let gone = Threading.SpinWait.SpinUntil(Func<bool> stopped, 3000)
                      proc.Dispose()
                      gone |> Expect.isTrue "every fixture process terminated" }
            cleanup, root, Set.ofArray ids, {command with ProcessId = root.Id; Executable = "/bin/sh"; Arguments = [|"-c"; script|] }
          let liveGroup pid =
            match Bozzetto.Server.LinuxProcessEdge.readLinuxProcesses false with
            | Error reason -> failtestf "Cannot inspect test-owned process groups: %A" reason
            | Ok ps -> ps |> List.choose (fun p -> if p.Pgid = pid && ProcessWatchdog.isLiveRecord p then Some p.Pid else None) |> Set.ofList
          let beginCommand cmd = task {
            let! response = post client "start" cmd
            return requireSuccess response
          }
          let stop seq (cmd: CommandWire.CommandObservationRequest) =
            supervisor.StopLeasedCommand(journal,seq,cmd.RunId) |> Async.StartAsTask
          let aCleanup, a, aPids, first = start holder lease
          use _a = aCleanup
          let bCleanup, b, bPids, second = start otherHolder otherLease
          use _b = bCleanup
          let! firstSeq = beginCommand first
          let! secondSeq = beginCommand second
          aPids.Count |> Expect.equal "the selected root owns two children" 3
          bPids.Count |> Expect.equal "the sibling root owns two children" 3
          liveGroup a.Id |> Expect.equal "all three selected processes run before stop" aPids
          liveGroup b.Id |> Expect.equal "all three sibling processes run before stop" bPids
          supervisor.CurrentLivenessSnapshot().StoppableRunIds |> Expect.equal "both rows opted in" (Set.ofList [first.RunId; second.RunId])
          let! stopped = stop firstSeq first
          stopped |> Expect.equal "exact group cleaned up" (Ok ())
          liveGroup a.Id |> Expect.isEmpty "no live member of the selected group remains"
          liveGroup b.Id |> Expect.equal "the other opted-in group is intact" bPids
          supervisor.CurrentLivenessSnapshot().StoppableRunIds |> Expect.equal "only the sibling can still be selected" (Set.singleton second.RunId)
          LeaseWatch.matchesLive holder ExpensiveWorkLease.Kind.FullBuild leaseId |> Expect.isTrue "the runner keeps its admission"
          let cCleanup, c, cPids, vanished = start holder lease
          use _c = cCleanup
          let! goneSeq = beginCommand vanished
          supervisor.CurrentLivenessSnapshot().StoppableRunIds.Contains vanished.RunId |> Expect.isTrue "the disappearing root was admitted"
          c.Kill()
          c.WaitForExit()
          let! stale = stop goneSeq vanished
          stale |> Expect.equal "revalidate the bound incarnation immediately before signalling" (Error "The owned isolated process incarnation can no longer be verified.")
          liveGroup c.Id |> Expect.equal "stale stop leaves both surviving children untouched" (Set.remove c.Id cPids)
          LeaseWatch.releaseOwned otherHolder (ExpensiveWorkLease.LeaseId.ofWire otherLease) |> ignore
          let! lost = stop secondSeq second
          lost |> Expect.equal "receipt no longer owns admission" (Error "The command no longer holds its original live lease.")
          liveGroup b.Id |> Expect.equal "releasing admission cannot target the remaining group" bPids
        })
    }
    testTask "an unavailable durable journal cannot acknowledge a running command" {
      do!
        withCommandServer false (fun client _ _ ->
          task {
            let leaseId = granted "http-command-owner" ExpensiveWorkLease.Kind.FullBuild
            let! response = post client "start" (commandBody leaseId)
            requireRefusal 409 "durable operations journal" response
          })
    }
    testTask "origin guard blocks cross-origin DELETE /mcp before disconnect wakeup while accepted same-origin MCP disconnect wakes cleanup" {
      let builder = WebApplication.CreateBuilder()
      builder.Logging.ClearProviders() |> ignore
      builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
      let app = builder.Build()
      let tracker = Server.McpServerTracker()
      app.Use(Func<HttpContext, Func<Task>, Task>(fun context next -> Server.errorHandlingMiddleware context next :> Task)) |> ignore
      Server.useOriginGuard (Bozzetto.Server.HttpOriginGuard.OwnOrigins.ofPorts [47749; 47750]) app
      Server.useMcpDisconnectObservation tracker app
      app.MapDelete("/mcp", RequestDelegate(fun ctx ->
        task {
          match ctx.Request.Headers.TryGetValue("mcp-session-id") with
          | true, sid when sid.ToString() = "valid-session" -> ctx.Response.StatusCode <- 200
          | _ -> ctx.Response.StatusCode <- 404
        } :> Task)) |> ignore
      let streamEntered = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      app.MapGet("/mcp", RequestDelegate(fun ctx ->
        task {
          streamEntered.TrySetResult() |> ignore
          try do! Task.Delay(TimeSpan.FromSeconds 10., ctx.RequestAborted)
          with :? OperationCanceledException -> ()
        } :> Task)) |> ignore
      let mutable wakeups = 0
      let wakeupEvent = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
      Bozzetto.Server.DaemonTelemetry.installSessionDisconnectWakeup (fun () ->
        if System.Threading.Interlocked.Increment(&wakeups) >= 2 then
          wakeupEvent.TrySetResult() |> ignore)
      let! outcome =
        task {
          try
            do! app.StartAsync()
            use client = new HttpClient(BaseAddress = Uri(Seq.exactlyOne app.Urls), Timeout = TimeSpan.FromSeconds 5.)

            // Negative control 1: cross-origin DELETE /mcp MUST be blocked 403 by useOriginGuard
            // BEFORE useMcpDisconnectObservation runs -> 0 wakeups.
            use foreignDel = new HttpRequestMessage(HttpMethod.Delete, "/mcp")
            foreignDel.Headers.Add("Origin", "http://foreign.example:9999")
            foreignDel.Headers.Add("mcp-session-id", "valid-session")
            let! foreignResp = client.SendAsync foreignDel
            int foreignResp.StatusCode |> Expect.equal "cross-origin DELETE /mcp rejected with 403" 403
            System.Threading.Volatile.Read(&wakeups)
            |> Expect.equal "cross-origin 403 DELETE /mcp must not fire disconnect wakeup" 0

            // Negative control 2: same-origin DELETE /mcp returning 404 MUST NOT fire disconnect wakeup.
            use rejectedDel = new HttpRequestMessage(HttpMethod.Delete, "/mcp")
            rejectedDel.Headers.Add("mcp-session-id", "nonexistent-session-id")
            let! rejectedResp = client.SendAsync rejectedDel
            int rejectedResp.StatusCode |> Expect.equal "unknown session DELETE returns 404" 404
            System.Threading.Volatile.Read(&wakeups)
            |> Expect.equal "non-2xx DELETE /mcp must not fire disconnect wakeup" 0

            // Positive control 3: accepted same-origin DELETE /mcp (200) fires O(1) disconnect wakeup.
            use validDel = new HttpRequestMessage(HttpMethod.Delete, "/mcp")
            validDel.Headers.Add("mcp-session-id", "valid-session")
            let! validDelResp = client.SendAsync validDel
            int validDelResp.StatusCode |> Expect.equal "accepted same-origin DELETE /mcp returns 200" 200
            System.Threading.Volatile.Read(&wakeups)
            |> Expect.equal "accepted same-origin DELETE /mcp fired exactly 1 disconnect wakeup" 1

            // Positive control 4: abrupt same-origin MCP request abort fires O(1) disconnect wakeup.
            use abortCts = new System.Threading.CancellationTokenSource()
            let getTask = client.GetAsync("/mcp", abortCts.Token)
            do! streamEntered.Task.WaitAsync(TimeSpan.FromSeconds 5.)
            abortCts.Cancel()
            try let! _ = getTask in () with :? OperationCanceledException | :? HttpRequestException -> ()
            do! wakeupEvent.Task.WaitAsync(TimeSpan.FromSeconds 5.)
            (System.Threading.Volatile.Read(&wakeups) >= 2)
            |> Expect.isTrue "abrupt MCP disconnect fired O(1) disconnect wakeup"
            return Ok ()
          with ex -> return Error ex
        }
      Bozzetto.Server.DaemonTelemetry.clearSessionDisconnectHandlers ()
      do! app.StopAsync().WaitAsync(TimeSpan.FromSeconds 5.)
      do! app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds 5.)
      match outcome with
      | Ok () -> ()
      | Error ex -> return raise ex
    }
  ]
