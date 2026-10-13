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
  testSequenced
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
    testTask "an unavailable durable journal cannot acknowledge a running command" {
      do!
        withCommandServer false (fun client _ _ ->
          task {
            let leaseId = granted "http-command-owner" ExpensiveWorkLease.Kind.FullBuild
            let! response = post client "start" (commandBody leaseId)
            requireRefusal 409 "durable operations journal" response
          })
    }
  ]
