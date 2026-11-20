module Bozzetto.Tests.ReplGuardTests

/// The bozzetto-repl-guard hook's decision (tools/agent-hooks/ReplGuard.fs,
/// linked into this project). The hook script only does IO around this
/// function, so this is where the rules get pinned.
///
/// Three things this guard decides, layered:
///   1. An UNDECLARED slow verb ("you should be using the REPL") denies —
///      but only when there is a Ready daemon session whose working
///      directory covers the command's cwd. With Bozzetto up but no session
///      there yet, there is no REPL to send this code to — that is exactly
///      the one build `create_session` itself needs, so it must pass
///      (issue #144).
///   2. A DECLARED final gate now ALSO asks for an expensive-work lease —
///      legitimate, but still costs memory the daemon needs to know about.
///      `LeaseGranted` allows it; `LeaseRefused`/`LeaseMustWait` denies it
///      as BUSY (never as broken), with the reason and the wait.
///   3. The session-covers-cwd question itself is decided by `sessionProbe`,
///      layered on the pure path-containment check `directoryContains`.

open Expecto
open Expecto.Flip
open FsCheck
open FsCheck.FSharp
open Bozzetto.AgentHooks.ReplGuard

let private live = { Project = FSharpWorkspace; Daemon = DaemonAnswering; Session = Some ReadySessionCoversCwd; Lease = None }
let private liveNoSessionHere = { live with Session = Some NoReadySessionCoversCwd }
let private liveGranted = { live with Lease = Some LeaseGranted }
let private liveMustWait = { live with Lease = Some(LeaseMustWait(12.0, "1 lease(s) active (cap 1 at tight pressure)")) }
let private liveRefused = { live with Lease = Some(LeaseRefused "you already hold 1/1 leases") }

let private isDeny =
  function
  | Deny _ -> true
  | Allow -> false

/// Plain argument words: no quotes, separators or spaces, so they never
/// change how the command splits.
let private argWord =
  Gen.elements [ "-c"; "Release"; "--no-restore"; "Bozzetto.Tests"; "--project"; "X.fsproj"; "--filter"; "Foo"; "-v"; "q" ]

let private args = Gen.listOfLength 3 argWord |> Gen.map (String.concat " ")

let private slowVerb = Gen.elements SlowVerb.all

let private contexts =
  Gen.elements [
    { Project = FSharpWorkspace; Daemon = DaemonAnswering; Session = None; Lease = None }
    { Project = FSharpWorkspace; Daemon = DaemonNotAnswering; Session = None; Lease = None }
    { Project = NotFSharpWorkspace; Daemon = DaemonAnswering; Session = None; Lease = None }
    { Project = NotFSharpWorkspace; Daemon = DaemonNotAnswering; Session = None; Lease = None }
  ]

let private prop gen body = Prop.forAll (Arb.fromGen gen) body

[<Tests>]
let replGuardTests = testList "bozzetto-repl-guard decision" [

  testProperty "WHY — an UNDECLARED slow-loop verb in an F# repo with a Ready session covering cwd is denied, because that's the drift the hook exists to catch" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "dotnet %s %s" (SlowVerb.toToken verb) a) live |> isDeny)

  testProperty "WHY — issue #144: an UNDECLARED slow-loop verb with Bozzetto up but NO Ready session covering cwd is allowed, because that's exactly the one build create_session itself needs" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "dotnet %s %s" (SlowVerb.toToken verb) a) liveNoSessionHere = Allow)

  testProperty "WHY — a Session that was never probed (Session=None) is treated the same as no covering session, fail-open rather than fail-closed" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "dotnet %s %s" (SlowVerb.toToken verb) a) { live with Session = None } = Allow)

  testProperty "WHY — a DECLARED final gate with the lease GRANTED always passes, because the final gate has to be runnable" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "BOZZETTO_FINAL_GATE=1 dotnet %s %s" (SlowVerb.toToken verb) a) liveGranted = Allow)

  testProperty "WHY — a DECLARED final gate the daemon asks to WAIT for is denied as BUSY, not silently allowed" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "BOZZETTO_FINAL_GATE=1 dotnet %s %s" (SlowVerb.toToken verb) a) liveMustWait |> isDeny)

  testProperty "WHY — a DECLARED final gate the daemon REFUSES a lease for is denied too" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "BOZZETTO_FINAL_GATE=1 dotnet %s %s" (SlowVerb.toToken verb) a) liveRefused |> isDeny)

  testProperty "WHY — with no Bozzetto daemon answering, everything passes, because there's no REPL and no pool to ask" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "dotnet %s %s" (SlowVerb.toToken verb) a) { live with Daemon = DaemonNotAnswering } = Allow)

  testProperty "WHY — a daemon that answered but was never asked for a lease (Lease=None) still allows the final gate" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "BOZZETTO_FINAL_GATE=1 dotnet %s %s" (SlowVerb.toToken verb) a) live = Allow)

  testProperty "WHY — outside an F# repo everything passes, because Bozzetto has nothing to load there" <|
    prop (Gen.zip slowVerb args) (fun (verb, a) ->
      decide (sprintf "dotnet %s %s" (SlowVerb.toToken verb) a) { live with Project = NotFSharpWorkspace } = Allow)

  testProperty "WHY — packaging and tool management pass even with Bozzetto up, because the REPL can't do them" <|
    prop (Gen.zip (Gen.elements [ "pack"; "tool update Bozzetto -g"; "tool install"; "--version"; "--list-sdks"; "restore" ]) args) (fun (verb, a) ->
      decide (sprintf "dotnet %s %s" verb a) live = Allow)

  testProperty "WHY — a command that never runs dotnet passes in any context" <|
    prop (Gen.zip (Gen.elements [ "ls -la"; "git status"; "echo dotnet build"; "grep -rn \"dotnet test\" ."; "git commit -m 'dotnet run'" ]) contexts) (fun (cmd, ctx) ->
      decide cmd ctx = Allow)

  testCase "WHY — a slow verb after a cd or a wrapper is still caught" <| fun _ ->
    [ "cd Bozzetto.Tests && dotnet test"
      "timeout 600 dotnet run --project X.fsproj"
      "env FOO=1 dotnet build -c Release"
      "ls; /usr/bin/dotnet fsi script.fsx"
      "dotnet pack -o nupkg && dotnet build" ]
    |> List.filter (fun c -> decide c live = Allow)
    |> Expect.isEmpty "every one of these runs a slow-loop verb"

  testCase "WHY — an escape hatch on one segment doesn't excuse a later undeclared one" <| fun _ ->
    decide "BOZZETTO_FINAL_GATE=1 dotnet build && dotnet test" liveGranted
    |> isDeny
    |> Expect.isTrue "the second segment never declared the final gate"

  testCase "WHY — export BOZZETTO_FINAL_GATE=1 before the call counts as the declaration" <| fun _ ->
    decide "export BOZZETTO_FINAL_GATE=1 && dotnet test" liveGranted
    |> Expect.equal "an exported gate applies to what follows" Allow

  testCase "WHY — the undeclared deny reason restates the loop and names the escape hatch" <| fun _ ->
    match decide "dotnet test" live with
    | Allow -> failtest "dotnet test with Bozzetto up must be denied"
    | Deny reason ->
      [ "send_fsharp_code"; "hard_reset_fsi_session"; "rebuild=true"; "final gate"; "BOZZETTO_FINAL_GATE=1 dotnet test" ]
      |> List.filter (fun needle -> not (reason.Contains needle))
      |> Expect.isEmpty "the reason should carry the loop and the way out"

  testCase "WHY — the busy deny reason says BUSY, names the wait, and never says to fall back to dotnet" <| fun _ ->
    match decide "BOZZETTO_FINAL_GATE=1 dotnet build" liveMustWait with
    | Allow -> failtest "a MustWait lease outcome must deny"
    | Deny reason ->
      reason |> Expect.stringContains "explicitly says BUSY, not BROKEN" "BUSY, not broken"
      reason |> Expect.stringContains "carries the wait" "wait 12"
      (reason.Contains "1 lease(s) active") |> Expect.isTrue "carries the pool's own reason"

  testCase "WHY — the busy deny reason for a Refused lease says so too" <| fun _ ->
    match decide "BOZZETTO_FINAL_GATE=1 dotnet build" liveRefused with
    | Allow -> failtest "a Refused lease outcome must deny"
    | Deny reason -> reason |> Expect.stringContains "carries the refusal reason" "you already hold 1/1 leases"

  testCase "WHY — needsContext is true for a DECLARED final gate now too, since it still has to ask for a lease" <| fun _ ->
    [ "git status"; "dotnet pack" ]
    |> List.filter needsContext
    |> Expect.isEmpty "these never touch dotnet's slow loop at all"
    needsContext "BOZZETTO_FINAL_GATE=1 dotnet test" |> Expect.isTrue "a final gate still needs a lease check"
    needsContext "dotnet test" |> Expect.isTrue "an undeclared slow verb needs the daemon-answering check"

  testCase "WHY — directoryContains: a session rooted exactly at cwd covers it" <| fun _ ->
    directoryContains "/repo" "/repo" |> Expect.isTrue "same directory"

  testCase "WHY — directoryContains: a session rooted ABOVE cwd covers a build run from a subdirectory" <| fun _ ->
    directoryContains "/repo" "/repo/Bozzetto.Tests" |> Expect.isTrue "cwd nested under the session root"

  testCase "WHY — directoryContains: a session rooted BELOW cwd does not cover it" <| fun _ ->
    directoryContains "/repo/Bozzetto.Tests" "/repo" |> Expect.isFalse "the session is the narrower directory, not cwd"

  testCase "WHY — directoryContains: an unrelated directory never counts, even with a shared prefix string" <| fun _ ->
    directoryContains "/repo" "/repo-other/Bozzetto.Tests" |> Expect.isFalse "textual prefix, not a real path ancestor"

  testCase "WHY — directoryContains: trailing slashes and backslashes normalize the same way" <| fun _ ->
    directoryContains "/repo/" "/repo/Bozzetto.Tests/" |> Expect.isTrue "trailing slash on either side"
    directoryContains @"C:\repo" @"C:\repo\Bozzetto.Tests" |> Expect.isTrue "backslash-separated paths"

  testCase "WHY — sessionProbe: a Ready session covering cwd is ReadySessionCoversCwd" <| fun _ ->
    sessionProbe "/repo/Bozzetto.Tests" [ { WorkingDirectory = "/repo"; Status = "Ready" } ]
    |> Expect.equal "the one Ready session roots above cwd" ReadySessionCoversCwd

  testCase "WHY — sessionProbe: status match is case-insensitive, since the wire label's casing isn't this file's contract to police" <| fun _ ->
    sessionProbe "/repo" [ { WorkingDirectory = "/repo"; Status = "ready" } ]
    |> Expect.equal "case-insensitive status match" ReadySessionCoversCwd

  testCase "WHY — sessionProbe: a Faulted session at the same directory does not count — its REPL is not up" <| fun _ ->
    sessionProbe "/repo" [ { WorkingDirectory = "/repo"; Status = "Faulted" } ]
    |> Expect.equal "not Ready, so no REPL to send code to" NoReadySessionCoversCwd

  testCase "WHY — sessionProbe: a Ready session for an unrelated directory does not count" <| fun _ ->
    sessionProbe "/repo" [ { WorkingDirectory = "/somewhere/else"; Status = "Ready" } ]
    |> Expect.equal "wrong directory entirely" NoReadySessionCoversCwd

  testCase "WHY — sessionProbe: an empty session list never counts, whatever cwd is" <| fun _ ->
    sessionProbe "/repo" [] |> Expect.equal "no sessions at all" NoReadySessionCoversCwd

  testCase "WHY — sessionProbe: one Ready match among several sessions is enough" <| fun _ ->
    sessionProbe "/repo/X"
      [ { WorkingDirectory = "/elsewhere"; Status = "Ready" }
        { WorkingDirectory = "/repo"; Status = "Faulted" }
        { WorkingDirectory = "/repo"; Status = "Ready" } ]
    |> Expect.equal "the third session is Ready and covers cwd" ReadySessionCoversCwd

  testCase "WHY — protocol_mismatch on POST /api/lease/request fails closed (Deny), never fails open, and exposes legacy leaseId for release" <| fun _ ->
    ExpectedProtocolVersion |> Expect.equal "ReplGuard.ExpectedProtocolVersion matches shared ProtocolVersion" Bozzetto.Web.Shared.Protocol.ProtocolVersion
    // Positive control: strict v14 granted -> Allow
    let okProbe, okRelease = parseLeaseResponse 200 """{"protocolVersion":14,"decision":"granted","leaseId":"v14-1"}"""
    okProbe |> Expect.equal "v14 grant parses as LeaseGranted" (Some LeaseGranted)
    okRelease |> Expect.isNone "v14 grant needs no rollback release"
    decide "BOZZETTO_FINAL_GATE=1 dotnet build" { live with Lease = okProbe } |> Expect.equal "v14 grant allows" Allow

    // Exact-reason negative control: HTTP 400 protocol_mismatch -> Deny naming protocol_mismatch
    let bad400Probe, bad400Release =
      parseLeaseResponse 400 """{"error":"protocol_mismatch","reason":"protocol_mismatch: expected protocolVersion 14"}"""
    bad400Release |> Expect.isNone "HTTP 400 has no granted lease to release"
    match decide "BOZZETTO_FINAL_GATE=1 dotnet test" { live with Lease = bad400Probe } with
    | Allow -> failtest "HTTP 400 protocol_mismatch must NEVER fail open"
    | Deny reason ->
      reason |> Expect.stringContains "names protocol_mismatch" "protocol_mismatch"
      reason |> Expect.stringContains "tells operator to update daemon/hook" "Update the Bozzetto daemon"

    // Fault-injection negative controls: skewed v13, unversioned legacy 200 grant, OR unparseable/empty/non-object 2xx -> Deny
    for label, status, payload, expectedId in [
      "unversioned legacy", 200, """{"decision":"granted","leaseId":"legacy-1"}""", Some "legacy-1"
      "skewed v13", 200, """{"protocolVersion":13,"decision":"granted","leaseId":"legacy-13"}""", Some "legacy-13"
      "HTTP 204 empty", 204, "", None
      "HTTP 200 empty", 200, "", None
      "HTTP 200 JSON array", 200, "[]", None
      "HTTP 200 malformed JSON", 200, "not-json", None
      "HTTP 200 missing decision", 200, """{"protocolVersion":14}""", None
    ] do
      let skewProbe, skewRelease = parseLeaseResponse status payload
      skewRelease |> Expect.equal (sprintf "%s exposes granted leaseId for rollback release" label) expectedId
      match decide "BOZZETTO_FINAL_GATE=1 dotnet build" { live with Lease = skewProbe } with
      | Allow -> failtestf "%s reply must NEVER fail open" label
      | Deny reason ->
        reason |> Expect.stringContains (sprintf "%s deny carries protocol_mismatch" label) "protocol_mismatch"

    // Non-protocol transport/500 failure remains fail-open (None -> Allow)
    parseLeaseResponse 503 "service unavailable" |> Expect.equal "503 stays None" (None, None)
    decide "BOZZETTO_FINAL_GATE=1 dotnet build" { live with Lease = None } |> Expect.equal "transport None stays Allow" Allow
]
