module Bozzetto.Tests.AuditPacketTests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Fidelity.Data.JSON
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Bozzetto.Audit

let private sampleSha = "6aecb25271c9717094d42c08f044b1a117d88e0a"
let private sampleIssueUrl = "https://forge.spkez.dev/FidelityFramework/Bozzetto/issues/38"
let private sampleWikiUrl = "https://forge.spkez.dev/FidelityFramework/Farscape/wiki/Course-Correction-2026-11-19"
let private validSha256A = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
let private validSha256B = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"

let private cleanCanaryReceipt (target: string) : CanaryReceipt = {
  Target = target
  FrontEndMs = Some 1850.0
  FrontEndLimitMs = Some 5000.0
  ProofDischargeMs = Some 240.0
  ProofDischargeLimitMs = Some 1000.0
  TotalWallMs = Some 3100.0
  WallLimitMs = Some 15000.0
  PeakRssBytes = Some(190L * 1_048_576L)
  RssLimitBytes = Some(512L * 1_048_576L)
  NativeExitCode = Some 0
  CompileExitCode = Some 0
  WatchdogBreach = None
  WatchdogSurvivors = None
  WatchdogScanError = None
  WatchdogCleared = true
  TotalAllocBytes = Some 12_000_000L
  PhaseTimingsMs = [ "obligations", 410.0; "witness-publication", 620.0 ]
  PhaseAllocBytes = [ "obligations", 4_000_000L; "witness-publication", 5_000_000L ]
  PlatformResolutionReads = Some 1
  NumericProjectionCalls = Some 1
  MemoryProjectionCalls = Some 1
  SpatialProjectionCalls = Some 0
  SpatialExecuted = Some false
  HardwareAndKernelModuleCount = Some 0
  Cvc5Spawns = Some 4
  UniqueObligations = Some 12
  Scale1xAllocBytes = Some 2_000_000L
  Scale4xAllocBytes = Some 8_200_000L
  Scale1xWallMs = Some 120.0
  Scale4xWallMs = Some 490.0
  ExplicitScalingSlope = None
  ScalingPasses = [ { PassName = "WitnessSegmentation"; Alloc1xBytes = 500_000L; Alloc4xBytes = 2_050_000L; Slope = 4.1 } ]
  FailedTestCompile = Some false
}

let private sampleRawSarif =
  sprintf
    """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"Alex.Analyzers"}},"results":[{"ruleId":"AXD000","level":"note","message":{"text":"{\"hashes\":{\"src/Compiler/Clean.fs\":\"%s\"}}"}}]}]}"""
    validSha256A

let private cleanAnalyzerReceipt () : AnalyzerReceipt = {
  Tools = [ "Alex.Analyzers" ]
  TotalDiagnostics = 1
  ErrorCount = 0
  WarningCount = 0
  NoteCount = 1
  RedTargetDiagnostics = []
  AllDiagnostics = [
    { RuleId = "AXD000"
      Level = "note"
      File = "src/Compiler/Clean.fs"
      Line = Some 1
      Message = "digest"
      IsTargetRedRule = false }
  ]
  Axd000Digests = [ { Path = "src/Compiler/Clean.fs"; Sha256 = validSha256A.ToUpperInvariant() } ]
  RevisionIds = []
  Axd000Disagreement = None
  HasInvalidAxd000Entry = false
  Axd000Valid = true
  RawSarif = sampleRawSarif
}

let private cleanRetrievalSnapshot () : RetrievalSnapshot = {
  Fresh = true
  State = "fresh"
  SnapshotId = "554b47c116dbfe3ac8d85d65cbff47b8b32844e2e70ccbb015a70bd2d5ea6156"
  CurrentSnapshotId = Some "554b47c116dbfe3ac8d85d65cbff47b8b32844e2e70ccbb015a70bd2d5ea6156"
  Generation = Some 208L
  BranchName = Some "main"
  BranchStatus = Some "on_main_indexed"
  OnMain = Some true
  Revisions = [ { Source = "clef"; Revision = Some sampleSha } ]
  Note = None
  IsValidFresh = true
  InvalidReason = None
  Provenance = RetrievalProvenance.SnapshotFile("retrieval.json", Some "clef", Some sampleSha)
}

let private cleanIssueAuthority () : IssueAuthority = {
  Number = 38
  Title = "Phase 0.5: add machine-assembled Blind Audit Packet generator"
  State = "open"
  HtmlUrl = sampleIssueUrl
  Milestone = Some "Validation runner reset (.NET)"
  Labels = []
  Body = sprintf "Verbatim issue body.\nSee %s" sampleWikiUrl
  Comments = []
  ExpectedCommentCount = Some 0
  CommentsVerified = true
  ReferencedUrls = [ sampleWikiUrl ]
  Provenance = AuthorityProvenance.LiveFetch "https://forge.spkez.dev/api/v1/repos/FidelityFramework/Bozzetto/issues/38"
  WikiRulings = [
    { Url = sampleWikiUrl
      Title = "Course Correction 2026-11-19"
      PageName = "Course-Correction-2026-11-19"
      Content = "# Course Correction\nZone A pure core ruling."
      Provenance = AuthorityProvenance.LiveFetch "https://forge.spkez.dev/api/v1/repos/FidelityFramework/Farscape/wiki/page/Course-Correction-2026-11-19" }
  ]
  WikiRulingFailures = []
}

let private cleanCodeDelta () : CodeDeltaReport =
  let raw =
    String.concat "\n" [
      "diff --git a/src/Compiler/Clean.fs b/src/Compiler/Clean.fs"
      "--- a/src/Compiler/Clean.fs"
      "+++ b/src/Compiler/Clean.fs"
      "@@ -1,2 +1,2 @@"
      "-let oldVal = 1"
      "+let newVal = 2"
    ]
  let numstat = "1\t1\tsrc/Compiler/Clean.fs"
  let counts = Map.ofList [ "src/Compiler/Clean.fs", (Some 20, Some 20) ]
  scanDiff sampleSha "HEAD" sampleSha (Some "main") raw numstat counts

let private cleanPacketInput () : BlindAuditPacketInput = {
  RepoPath = "/repos/clef"
  BaseSha = sampleSha
  CandidateRef = "HEAD"
  IssueUrl = sampleIssueUrl
  OutsideIndex = false
  Issue = Ok(cleanIssueAuthority ())
  CodeDelta = Ok(cleanCodeDelta ())
  Analyzer = Ok(Some(cleanAnalyzerReceipt ()))
  TestReceipt = Ok(Some { CompileSucceeded = true; ExitCode = Some 0; Verdict = Some "Trusted"; Passed = Some 42; Failed = Some 0; Errored = Some 0; Summary = "Trusted (42/42)" })
  CanaryBase = Ok(Some(cleanCanaryReceipt "01_HelloWorldDirect"))
  CanaryCandidate = Ok(Some(cleanCanaryReceipt "01_HelloWorldDirect"))
  Retrieval = Ok(Some(cleanRetrievalSnapshot ()))
  SpecBallast = SpecBallastStatus.Loaded("Zone A boundary", "hybrid", [
    { Title = "Clef Core Boundary"
      Section = "Zone A"
      Url = "https://clef-lang.com/spec/zone-a"
      ContentType = "spec"
      Snippet = "Pure algebraic F# subset." }
  ])
}

let private runProcEnvAsync (exe: string) (workingDir: string) (envExtras: (string * string) list) (args: string list) = task {
  let psi = ProcessStartInfo(exe)
  psi.WorkingDirectory <- workingDir
  psi.UseShellExecute <- false
  psi.RedirectStandardOutput <- true
  psi.RedirectStandardError <- true
  psi.StandardOutputEncoding <- Encoding.UTF8
  psi.StandardErrorEncoding <- Encoding.UTF8
  for k, v in envExtras do
    psi.Environment[k] <- v
  args |> List.iter psi.ArgumentList.Add
  use proc =
    match Option.ofObj (Process.Start psi) with
    | Some p -> p
    | None -> failtestf "Process.Start returned null for '%s'" exe
  use cts = new CancellationTokenSource(TimeSpan.FromSeconds 60.0)
  let outTask = proc.StandardOutput.ReadToEndAsync(cts.Token)
  let errTask = proc.StandardError.ReadToEndAsync(cts.Token)
  let killAndCleanup () = task {
    try proc.Kill(entireProcessTree = true) with _ -> ()
    try
      use cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds 2.0)
      do! proc.WaitForExitAsync(cleanupCts.Token)
    with _ -> ()
    try ignore outTask.Exception with _ -> ()
    try ignore errTask.Exception with _ -> ()
  }
  try
    do! proc.WaitForExitAsync(cts.Token)
    let! outText = outTask
    let! errText = errTask
    return proc.ExitCode, outText, errText
  with
  | :? OperationCanceledException ->
    do! killAndCleanup ()
    return failtestf "Process '%s %s' timed out after 60s and process tree was killed" exe (String.concat " " args)
  | ex ->
    do! killAndCleanup ()
    return failtestf "Process '%s %s' failed: %s" exe (String.concat " " args) ex.Message
}

let private runProcAsync (exe: string) (workingDir: string) (args: string list) =
  runProcEnvAsync exe workingDir [] args

let private runGitOrFailAsync (repoDir: string) (args: string list) = task {
  let! code, outText, errText = runProcAsync "git" repoDir args
  if code <> 0 then
    failtestf "git %s failed (exit %d): stdout=%s stderr=%s" (String.concat " " args) code outText errText
  return outText.Trim()
}

let private validCanaryJson =
  """{"target":"01_HelloWorldDirect","frontEndMs":1900,"proofDischargeMs":200,"totalWallMs":3100,"peakRssMb":180,"nativeExitCode":0,"watchdogSurvivors":0,"gcAllocatedBytes":9000000,"phaseTimingsMs":{"obligations":410.0,"witness-publication":620.0},"phaseAllocBytes":{"obligations":4000000,"witness-publication":5000000},"counters":{"platformResolutionReads":1,"numericProjectionCalls":1,"memoryProjectionCalls":1,"spatialProjectionCalls":0,"spatialExecuted":false,"hardwareModules":0,"kernelModules":0,"cvc5Spawns":4},"scaling":{"scale1x":{"allocBytes":1000000,"wallMs":120.0},"scale4x":{"allocBytes":4050000,"wallMs":490.0},"passes":[{"pass":"WitnessSegmentation","alloc1x":500000,"alloc4x":2050000}]}}"""

let private validSpecBallastJson =
  """{"fusionMethod":"hybrid","results":[{"pageTitle":"Zone A Pure Core","sectionTitle":"Core Boundary","pageUrl":"https://clef-lang.com/spec/zone-a","contentType":"spec","snippet":"Zone A compiler passes remain pure and host-independent."}]}"""

let private validTestReceiptJson =
  """{"compileSucceeded":true,"exitCode":0,"verdict":"Trusted","passed":10,"failed":0,"errored":0}"""

let private validRetrievalJsonFor (repoName: string) (sha: string) =
  sprintf """{"freshness":{"fresh":true,"snapshot_id":"snap-live-208","current_snapshot_id":"snap-live-208"},"generation":208,"revisions":{"%s":"%s"}}""" repoName sha

[<Tests>]
let auditPacketTests =
  testList "Blind Audit Packet (Bozzetto #38)" [

    testList "CLI argument parsing, port validation & Forgejo token origin scoping" [

      testCase "positive control: parses required positionals, repeatable --wiki-file, and all optional flags cleanly" <| fun _ ->
        let argv = [
          "--out"; "packet.md"
          "--candidate"; "feat/phase-1"
          "--issue-file"; "issue.json"
          "--wiki-file"; sprintf "%s=ruling1.md" sampleWikiUrl
          "--wiki-file"; "https://forge.spkez.dev/FidelityFramework/Composer/wiki/Pipeline-Guardrails-and-Canary-Budgets=ruling2.md"
          "--sarif"; "report.sarif"
          "--axd000"; "axd.sha256"
          "--canary-base"; "base.json"
          "--canary-candidate"; "cand.json"
          "--retrieval-snapshot"; "ret.json"
          "--test-receipt"; "test.json"
          "--spec-ballast-query"; "Zone A pure core"
          "--bozzetto-port"; "47749"
          "--skip-retrieval-await"
          "--skip-clef-search"
          "--outside-index"
          "/repos/clef"
          sampleSha
          sampleIssueUrl
        ]
        match parseArgs argv with
        | Error err -> failtestf "expected valid parse, got %A" err
        | Ok opts ->
          opts.RepoPath |> Expect.equal "repo" "/repos/clef"
          opts.BaseSha |> Expect.equal "baseSha" sampleSha
          opts.IssueUrl |> Expect.equal "issueUrl" sampleIssueUrl
          opts.IssueRef |> Expect.equal "issueRef" { BaseUri = "https://forge.spkez.dev"; Owner = "FidelityFramework"; Repo = "Bozzetto"; Number = 38 }
          opts.WikiFiles.Length |> Expect.equal "2 wiki file mappings parsed" 2
          opts.OutPath |> Expect.equal "out" (Some "packet.md")
          opts.CandidateRef |> Expect.equal "candidate" "feat/phase-1"
          opts.BozzettoPort |> Expect.equal "port" (Some 47749)
          opts.SkipRetrievalAwait |> Expect.isTrue "skip retrieval await"
          opts.SkipClefSearch |> Expect.isTrue "skip clef search"
          opts.OutsideIndex |> Expect.isTrue "outside index"

      testCase "negative controls: refuses unknown flags, bad --wiki-file, bad SHAs, bad URLs, and invalid BOZZETTO_MCP_PORT" <| fun _ ->
        parseArgs [ "--unknown-flag"; "/repo"; sampleSha; sampleIssueUrl ]
        |> Expect.equal "unknown flag refused" (Error(CliError.UnknownFlag "--unknown-flag"))

        parseArgs [ "--wiki-file"; "not-a-mapping"; "/repo"; sampleSha; sampleIssueUrl ]
        |> Expect.equal "malformed --wiki-file refused" (Error(CliError.InvalidWikiFileMapping "not-a-mapping"))

        parseArgs [ "--wiki-file"; "https://forge.spkez.dev/FidelityFramework/Bozzetto/issues/38=foo.md"; "/repo"; sampleSha; sampleIssueUrl ]
        |> Expect.equal "non-wiki URL in --wiki-file refused" (Error(CliError.InvalidWikiFileMapping "https://forge.spkez.dev/FidelityFramework/Bozzetto/issues/38=foo.md"))

        parseArgs [ "/repo"; "not-a-sha"; sampleIssueUrl ]
        |> Expect.equal "malformed SHA refused" (Error(CliError.InvalidBaseSha "not-a-sha"))

        parseArgs [ "/repo"; sampleSha; "ftp://forge.spkez.dev/FidelityFramework/Bozzetto/issues/38" ]
        |> Expect.equal "non-http(s) scheme refused" (Error(CliError.InvalidIssueUrl "ftp://forge.spkez.dev/FidelityFramework/Bozzetto/issues/38"))

        parseArgs [ "/repo"; sampleSha; "https://user:pass@forge.spkez.dev/FidelityFramework/Bozzetto/issues/38" ]
        |> Expect.equal "URL with userinfo refused" (Error(CliError.InvalidIssueUrl "https://user:pass@forge.spkez.dev/FidelityFramework/Bozzetto/issues/38"))

        parseArgs [ "--bozzetto-port"; "99999"; "/repo"; sampleSha; sampleIssueUrl ]
        |> Expect.equal "out-of-range CLI port refused" (Error(CliError.InvalidPort "99999"))

        resolveBozzettoPort None (Some "notaport")
        |> Expect.equal "invalid BOZZETTO_MCP_PORT refuses without fallback" (Error(CliError.InvalidEnvPort("BOZZETTO_MCP_PORT", "notaport")))

        resolveBozzettoPort None (Some "70000")
        |> Expect.equal "out-of-range BOZZETTO_MCP_PORT refuses" (Error(CliError.InvalidEnvPort("BOZZETTO_MCP_PORT", "70000")))

        resolveBozzettoPort None None
        |> Expect.equal "default port when unset" (Ok 47749)

      testCase "security controls: Forgejo token origin scoping refuses arbitrary HTTP/untrusted hosts and gates loopback" <| fun _ ->
        shouldSendForgeToken "https://forge.spkez.dev" None None
        |> Expect.isTrue "default trusted HTTPS origin receives token"

        shouldSendForgeToken "http://forge.spkez.dev" None None
        |> Expect.isFalse "downgraded HTTP origin never receives token"

        shouldSendForgeToken "http://evil.example:8080" None None
        |> Expect.isFalse "arbitrary HTTP host never receives token"

        shouldSendForgeToken "http://evil.example" (Some "http://evil.example") None
        |> Expect.isFalse "non-HTTPS BOZZETTO_FORGE_ORIGIN is ignored"

        shouldSendForgeToken "https://custom.forge.local" (Some "https://custom.forge.local") None
        |> Expect.isTrue "explicit HTTPS BOZZETTO_FORGE_ORIGIN receives token"

        shouldSendForgeToken "http://127.0.0.1:5000" None None
        |> Expect.isFalse "loopback without BOZZETTO_ALLOW_TEST_TOKEN_ORIGIN never receives token"

        shouldSendForgeToken "http://127.0.0.1:5000" None (Some "http://127.0.0.1:5000")
        |> Expect.isTrue "matching loopback test origin receives token"

        shouldSendForgeToken "http://127.0.0.1:5001" None (Some "http://127.0.0.1:5000")
        |> Expect.isFalse "mismatched loopback port never receives token"

        shouldSendForgeToken "http://evil.example:5000" None (Some "http://evil.example:5000")
        |> Expect.isFalse "non-loopback BOZZETTO_ALLOW_TEST_TOKEN_ORIGIN refused"
    ]

    testList "Anti-pattern delta scanner, F# lexical stripping & rename controls" [

      testCase "positive control: strips F# line comments, nested block comments, regular/verbatim/triple strings, and char literals without breaking (*) operator or type params" <| fun _ ->
        let source =
          String.concat "\n" [
            "module Clean"
            "let prod (xs: 'a list) = List.fold (*) 1 [ 1; 2 ]"
            "let quoteChar = '\"'"
            "let s1 = \"ConditionalWeakTable System.Reflection\""
            "let s2 = @\"ConcurrentDictionary \"\"ConditionalWeakTable\"\"\""
            "let s3 = \"\"\"FSharpValue.GetUnionFields"
            "ConditionalWeakTable\"\"\""
            "(* outer (* nested *) ConditionalWeakTable ConcurrentQueue *)"
            "// ConditionalWeakTable PropertyInfo"
            "let ok = 42"
          ]
        let counts = countAntiPatternsInSource true source
        counts.ConditionalWeakTable |> Expect.equal "CWT count in strings/comments is 0" 0
        counts.Concurrent |> Expect.equal "Concurrent count in strings/comments is 0" 0
        counts.SystemReflection |> Expect.equal "SysReflection count in strings/comments is 0" 0
        counts.FSharpReflection |> Expect.equal "FSReflection count in strings/comments is 0" 0

        let interpSource = "let s = $\"val={ConditionalWeakTable<obj, obj>()}\""
        let interpCounts = countAntiPatternsInSource false interpSource
        interpCounts.ConditionalWeakTable |> Expect.equal "CWT inside interpolated hole IS detected" 1

        let rawDoubleInterp = "let s = $$\"\"\"literal {ConditionalWeakTable} hole {{ConditionalWeakTable<obj, obj>()}}\"\"\""
        (countAntiPatternsInSource false rawDoubleInterp).ConditionalWeakTable
        |> Expect.equal "CWT inside $$ triple-quoted {{...}} hole IS detected while single-brace literal is stripped" 1

        let rawTripleInterp = "let s = $$$\"\"\"literal {{ConditionalWeakTable}} hole {{{ let r = { A = ConditionalWeakTable<obj, obj>() }; r }}}\"\"\""
        (countAntiPatternsInSource false rawTripleInterp).ConditionalWeakTable
        |> Expect.equal "CWT inside $$$ triple-quoted {{{...}}} hole with nested record IS detected" 1

        let rawRegularTriple = "let s = $$$\"{{ConditionalWeakTable}} {{{ConditionalWeakTable<obj, obj>()}}}\""
        (countAntiPatternsInSource false rawRegularTriple).ConditionalWeakTable
        |> Expect.equal "CWT inside $$$ regular interpolated {{{...}}} hole IS detected" 1

      testCase "fault-injection control: removing string literal containing ConditionalWeakTable cannot cancel adding real ConditionalWeakTable" <| fun _ ->
        let diff =
          String.concat "\n" [
            "diff --git a/src/Compiler/Cache.fs b/src/Compiler/Cache.fs"
            "--- a/src/Compiler/Cache.fs"
            "+++ b/src/Compiler/Cache.fs"
            "@@ -1,2 +1,2 @@"
            "-let warningMsg = \"ConditionalWeakTable\""
            "+let cache = ConditionalWeakTable<obj, obj>()"
          ]
        let delta = scanDiff sampleSha "HEAD" sampleSha (Some "feat/mask") diff "1\t1\tsrc/Compiler/Cache.fs" Map.empty
        delta.AntiPatterns.ConditionalWeakTable.Delta |> Expect.equal "net delta is +1, not 0" 1
        let evaluated = evaluateVetoes { cleanPacketInput () with CodeDelta = Ok delta }
        evaluated.Vetoes
        |> Expect.contains "triggers exact AntiPatternIncrease veto" (MechanicalVeto.AntiPatternIncrease("ΔConditionalWeakTable", 1))

      testCase "fault-injection controls: detects .GetType().GetProperty/GetMethod/GetField/GetMethods and typeof<T>.GetMethods" <| fun _ ->
        let cases = [
          "x.GetType().GetProperty(\"A\")"
          "x.GetType().GetMethod(\"B\")"
          "x.GetType().GetField(\"C\")"
          "x.GetType().GetMethods()"
          "typeof<string>.GetMethods()"
          "typeof<int>.GetProperty(\"D\")"
        ]
        for expr in cases do
          let counts = countAntiPatternsInSource false (sprintf "let r = %s" expr)
          counts.SystemReflection |> Expect.equal (sprintf "%s counted as System.Reflection" expr) 1

      testCase "fault-injection control: pure rename (R100) from src/Compiler.Tests/Cache.fs into src/Compiler/Cache.fs with CWT triggers AntiPatternIncrease" <| fun _ ->
        let statusRaw = "R100\000src/Compiler.Tests/Cache.fs\000src/Compiler/Cache.fs\000"
        let parsedStatus = parseNameStatusZ statusRaw |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedStatus |> Expect.equal "parses R100 cleanly" [ GitChangeKind.Renamed 100, Some "src/Compiler.Tests/Cache.fs", "src/Compiler/Cache.fs" ]

        let blob = "module Cache\nlet c = ConditionalWeakTable<obj, obj>()\n"
        let change : GitFileChange = {
          Kind = GitChangeKind.Renamed 100
          OldPath = Some "src/Compiler.Tests/Cache.fs"
          NewPath = "src/Compiler/Cache.fs"
          BeforeBlob = Some blob
          AfterBlob = Some blob
          ReadError = None
        }
        let delta =
          scanDiffWithChanges
            sampleSha "HEAD" sampleSha (Some "feat/rename")
            "" "0\t0\tsrc/{Compiler.Tests => Compiler}/Cache.fs"
            [ { Path = "src/Compiler/Cache.fs"; OldPath = Some "src/Compiler.Tests/Cache.fs"; AddedLines = Some 0; RemovedLines = Some 0; IsBinary = false } ]
            [ change ]
            (Map.ofList [ "src/Compiler/Cache.fs", (Some 2, Some 2) ])
        delta.AntiPatterns.ConditionalWeakTable.Delta |> Expect.equal "pure rename into src/ yields +1 CWT" 1
        let evaluated = evaluateVetoes { cleanPacketInput () with CodeDelta = Ok delta }
        evaluated.Vetoes
        |> Expect.contains "pure rename triggers exact veto" (MechanicalVeto.AntiPatternIncrease("ΔConditionalWeakTable", 1))

      testCase "fault-injection controls: upward delta in CWT, Concurrent*, System.Reflection, FSharp.Reflection, BCL_Mutable, let_mutable, and NativeService.fs .normalize trigger exact vetoes" <| fun _ ->
        let cases = [
          "ConditionalWeakTable", "src/Compiler/PSGSaturation/Bad.fs", "+let cache = ConditionalWeakTable<string, int>()", "ΔConditionalWeakTable"
          "Concurrent*", "src/Compiler/PSGSaturation/Bad.fs", "+let q = ConcurrentDictionary<string, int>()", "ΔConcurrent*"
          "System.Reflection", "src/Compiler/PSGSaturation/Bad.fs", "+open System.Reflection", "ΔSystem.Reflection"
          "Microsoft.FSharp.Reflection", "src/Compiler/Nanopass/SSAAssignment.fs", "+let u = FSharpValue.GetUnionFields(x, typeof<int>)", "ΔMicrosoft.FSharp.Reflection"
          "BCL_Mutable", "src/Compiler/PSGSaturation/Bad.fs", "+let d = Dictionary<string, int>()", "ΔBCL_Mutable"
          "let_mutable", "src/Compiler/PSGSaturation/Bad.fs", "+let mutable counter = 0", "Δlet_mutable"
          "NativeService.fs .normalize", "src/Compiler/NativeTypedTree/NativeService.fs", "+let g2 = EagerDemand.normalize g1", "ΔNativeService_normalize"
        ]
        for label, filePath, addedLine, expectedMetric in cases do
          let diff =
            String.concat "\n" [
              sprintf "diff --git a/%s b/%s" filePath filePath
              sprintf "--- a/%s" filePath
              sprintf "+++ b/%s" filePath
              "@@ -1,1 +1,2 @@"
              " module Bad"
              addedLine
            ]
          let delta = scanDiff sampleSha "HEAD" sampleSha (Some "feat/bad") diff (sprintf "1\t0\t%s" filePath) Map.empty
          let evaluated = evaluateVetoes { cleanPacketInput () with CodeDelta = Ok delta }
          evaluated.Vetoes
          |> Expect.contains (sprintf "%s triggers exact veto" label) (MechanicalVeto.AntiPatternIncrease(expectedMetric, 1))
          let rendered = renderPacket evaluated
          rendered |> Expect.stringContains (sprintf "%s rendered as vetoing row" label) "`<= 0` (VETO if any file `> 0`) | VETO |"

      testCase "Part 2 exact git paths & ReadError controls: preserves literal a/ and backslash in -z paths and fails completeness on non-src ReadError" <| fun _ ->
        let statusRaw = "M\000a/KeepPrefix.fs\000M\000src/a\\b.fs\000"
        let parsedStatus = parseNameStatusZ statusRaw |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedStatus
        |> Expect.equal "parseNameStatusZ preserves exact a/ and backslash paths"
             [ GitChangeKind.Modified, Some "a/KeepPrefix.fs", "a/KeepPrefix.fs"
               GitChangeKind.Modified, Some "src/a\\b.fs", "src/a\\b.fs" ]

        let numstatZ = "2\t1\ta/KeepPrefix.fs\0003\t0\tsrc/a\\b.fs\000"
        let parsedNs = parseNumstatZ numstatZ
        parsedNs |> List.map _.Path
        |> Expect.equal "parseNumstatZ preserves exact a/ and backslash paths" [ "a/KeepPrefix.fs"; "src/a\\b.fs" ]

        let failedNonSrc : GitFileChange = {
          Kind = GitChangeKind.Modified
          OldPath = Some "README.md"
          NewPath = "README.md"
          BeforeBlob = None
          AfterBlob = Some "hi\n"
          ReadError = Some "git cat-file blob failed"
        }
        let deltaErr =
          scanDiffWithChanges sampleSha "HEAD" sampleSha (Some "feat/x") "" "1\t0\tREADME.md"
            [ { Path = "README.md"; OldPath = None; AddedLines = Some 1; RemovedLines = Some 0; IsBinary = false } ]
            [ failedNonSrc ] Map.empty
        deltaErr.UnaccountedSrcFiles |> Expect.equal "surfaces non-src ReadError" [ "README.md", "git cat-file blob failed" ]
        let evalErr = evaluateVetoes { cleanPacketInput () with CodeDelta = Ok deltaErr }
        evalErr.Completeness.IsComplete |> Expect.isFalse "failed blob read on text file fails completeness"
    ]

    testList "Part 3 soundness: strict SARIF, AXD000, and mandatory strict test receipt" [

      testCase "negative controls: rejects empty runs [], missing tool.driver.name, missing results, non-array results, vetoes all error-level diagnostics + rule.id, and preserves raw SARIF" <| fun _ ->
        match parseSarifAndAxd000 (Some """{"version":"2.1.0","runs":[]}""") (Some(sprintf "%s  a.fs" validSha256A)) with
        | Error(PacketParseError.InvalidJsonField("Analyzer SARIF", "runs", _)) -> ()
        | other -> failtestf "expected InvalidJsonField for empty runs [], got %A" other

        match parseSarifAndAxd000 (Some """{"version":"2.1.0","runs":[{"results":[]}]}""") None with
        | Error(PacketParseError.MissingJsonField("Analyzer SARIF run", "tool.driver.name")) -> ()
        | other -> failtestf "expected MissingJsonField for missing tool.driver.name, got %A" other

        match parseSarifAndAxd000 (Some """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"Alex"}}}]}""") None with
        | Error(PacketParseError.MissingJsonField("Analyzer SARIF run", "results")) -> ()
        | other -> failtestf "expected MissingJsonField for missing results, got %A" other

        match parseSarifAndAxd000 (Some """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"Alex"}},"results":"invalid"}]}""") None with
        | Error(PacketParseError.InvalidJsonField("Analyzer SARIF run", "results", _)) -> ()
        | other -> failtestf "expected InvalidJsonField for non-array results, got %A" other

        let nonPrefixAndRuleIdSarif =
          sprintf
            """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"Alex.Analyzers","rules":[{"id":"FAR001","defaultConfiguration":{"level":"error"}}]}},"results":[{"ruleId":"AXD000","level":"note","message":{"text":"%s  a.fs"}},{"ruleId":"AXD001","level":"error","message":{"text":"custom red"}},{"rule":{"id":"FAR001"},"message":{"text":"rule.id red"}},{"level":"error","message":{"text":"nameless red"}}]}]}"""
            validSha256A
        match parseSarifAndAxd000 (Some nonPrefixAndRuleIdSarif) None with
        | Ok(Some r) ->
          r.RedTargetDiagnostics.Length |> Expect.equal "all 3 error-level results vetoed (AXD001, rule.id FAR001, empty ruleId)" 3
          r.RedTargetDiagnostics |> List.map _.RuleId |> Expect.equal "resolves rule.id" [ "AXD001"; "FAR001"; "" ]
          let evalRed = evaluateVetoes { cleanPacketInput () with Analyzer = Ok(Some r) }
          let redVetoes = evalRed.Vetoes |> List.filter (function MechanicalVeto.RedAnalyzerDiagnostic _ -> true | _ -> false)
          redVetoes.Length |> Expect.equal "emits 3 RedAnalyzerDiagnostic vetoes" 3
        | other -> failtestf "expected Ok(Some) for non-prefix + rule.id SARIF, got %A" other

        match parseSarifAndAxd000 (Some sampleRawSarif) None with
        | Ok(Some r) ->
          r.RawSarif |> Expect.equal "preserves exact raw SARIF" sampleRawSarif
          r.Axd000Valid |> Expect.isTrue "valid AXD000 digest"
        | other -> failtestf "expected Ok(Some), got %A" other

      testCase "negative & positive controls: missing --test-receipt is incomplete, {} / NothingRan / bare ran=1 rejected, nonzero compileExitCode vetoes, and TRUST log ignores fixture FS0001" <| fun _ ->
        let missingTest = evaluateVetoes { cleanPacketInput () with TestReceipt = Ok None }
        missingTest.Completeness.IsComplete |> Expect.isFalse "missing test receipt fails completeness"
        missingTest.Completeness.MissingParts
        |> List.exists (fun s -> s.Contains("Test Compilation Receipt", StringComparison.Ordinal))
        |> Expect.isTrue "MissingParts names Test Compilation Receipt"

        for label, badPayload in [
          "empty JSON object", "{}"
          "NothingRan JSON", """{"verdict":"NothingRan","exitCode":0}"""
          "NothingRan JSON even with compileSucceeded", """{"compileSucceeded":true,"verdict":"NothingRan"}"""
          "exitCode 0 without compile/test evidence", """{"exitCode":0}"""
          "NothingRan TRUST text", "TRUST tier=default ran=0 verdict=NothingRan"
          "bare ran=1 without terminal TRUST line", "partial output ran=1"
          "truncated build log", "Determining projects to restore...\nBuilding Bozzetto.Tests..."
        ] do
          match parseTestReceipt badPayload with
          | Error(PacketParseError.InvalidTestReceipt _) -> ()
          | other -> failtestf "%s must return Error(InvalidTestReceipt), got %A" label other

        match parseTestReceipt """{"compileExitCode":1,"verdict":"TestsFailed","ran":1}""" with
        | Ok r ->
          r.CompileSucceeded |> Expect.isFalse "nonzero compileExitCode MUST yield CompileSucceeded=false even with TestsFailed + ran=1"
          let evalCompFail = evaluateVetoes { cleanPacketInput () with TestReceipt = Ok(Some r) }
          evalCompFail.Vetoes
          |> List.exists (function MechanicalVeto.FailedTestCompile _ -> true | _ -> false)
          |> Expect.isTrue "triggers FailedTestCompile veto"
        | Error e -> failtestf "expected Ok(CompileSucceeded=false), got %s" (PacketParseError.describe e)

        let bozzettoRealLog =
          "fixture output: src/Fake.fs(1,1): error FS0001: incidental test string\n\u001b[0mTRUST tier=default registered=8675 ran=8675 passed=8666 failed=6 errored=0 ignored=3 verdict=TestsFailed (6 failed, 0 errored)"
        match parseTestReceipt bozzettoRealLog with
        | Ok r -> r.CompileSucceeded |> Expect.isTrue "valid TRUST summary with ran>0 is not falsely vetoed by fixture error FS0001"
        | Error e -> failtestf "expected TRUST summary Ok, got %s" (PacketParseError.describe e)

        match parseTestReceipt "Build succeeded.\n    0 Warning(s)\n    0 Error(s)" with
        | Ok r -> r.CompileSucceeded |> Expect.isTrue "Build succeeded text parses as compile success"
        | Error e -> failtestf "expected Build succeeded Ok, got %s" (PacketParseError.describe e)

        match parseTestReceipt "TRUST tier=default ran=1 verdict=Trusted" with
        | Error(PacketParseError.InvalidTestReceipt _) -> ()
        | other -> failtestf "partial TRUST line missing registered/passed/failed/errored must fail, got %A" other

        match parseTestReceipt "TRUST tier=default registered=8673 ran=8673 passed=8664 failed=6 errored=0 ignored=3 verdict=TestsFailed\nexitCode=1" with
        | Ok r -> r.CompileSucceeded |> Expect.isTrue "full TestsFailed TRUST summary with executed tests parses as compile succeeded"
        | Error e -> failtestf "expected TestsFailed Ok, got %s" (PacketParseError.describe e)

        match parseTestReceipt "Build FAILED.\nTRUST tier=default registered=10 ran=10 passed=10 failed=0 errored=0 ignored=0 verdict=Trusted" with
        | Ok r -> r.CompileSucceeded |> Expect.isFalse "explicit Build FAILED line is never masked by another project's TRUST line"
        | Error e -> failtestf "expected Build FAILED Ok(false), got %s" (PacketParseError.describe e)

        match parseTestReceipt "src/A.fs(10,1): error FS0039: undefined\nBuild FAILED." with
        | Ok r -> r.CompileSucceeded |> Expect.isFalse "Build FAILED parses as compile failure"
        | Error e -> failtestf "expected Build FAILED Ok(false), got %s" (PacketParseError.describe e)
    ]

    testList "Part 5 soundness: required metrics, watchdog survivors/scanError, and non-relaxable governing caps" [

      testCase "negative controls: empty canary {} has no target fallback and lists every missing required metric; malformed scalingPasses and watchdog survivors rejected" <| fun _ ->
        let emptyCanary = parseCanaryReceipt "{}" |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        emptyCanary.Target |> Expect.equal "missing target does not default to 01_HelloWorldDirect" ""
        let missing = missingCanaryMetrics emptyCanary
        for field in [ "target"; "totalWallMs"; "peakRssBytes"; "nativeExitCode"; "totalAllocBytes"; "platformResolutionReads"; "numericProjectionCalls"; "memoryProjectionCalls"; "spatialProjectionCalls"; "hardwareAndKernelModuleCount"; "cvc5Spawns" ] do
          missing |> Expect.contains (sprintf "missingCanaryMetrics includes %s" field) field
        missing |> List.exists (fun s -> s.StartsWith("scaling", StringComparison.Ordinal))
        |> Expect.isTrue "missingCanaryMetrics requires scaling evidence"

        let helloMissingPhases = { cleanCanaryReceipt "01_HelloWorldDirect" with FrontEndMs = None; ProofDischargeMs = None; Scale1xAllocBytes = None; Scale4xAllocBytes = None; ScalingPasses = [] }
        let missingHello = missingCanaryMetrics helloMissingPhases
        missingHello |> Expect.contains "HelloWorld requires frontEndMs" "frontEndMs"
        missingHello |> Expect.contains "HelloWorld requires proofDischargeMs" "proofDischargeMs"

        let sqliteMissingObl = { cleanCanaryReceipt "sqlite" with UniqueObligations = None }
        missingCanaryMetrics sqliteMissingObl |> Expect.contains "sqlite requires uniqueObligations" "uniqueObligations"

        let evalEmpty = evaluateVetoes { cleanPacketInput () with CanaryBase = Ok(Some emptyCanary); CanaryCandidate = Ok(Some emptyCanary) }
        evalEmpty.Completeness.IsComplete |> Expect.isFalse "empty canaries fail completeness"

        let unknownTarget = cleanCanaryReceipt "HelloWorld.fidproj"
        let evalUnknown = evaluateVetoes { cleanPacketInput () with CanaryBase = Ok(Some unknownTarget); CanaryCandidate = Ok(Some unknownTarget) }
        evalUnknown.Completeness.IsComplete |> Expect.isFalse "unknown canary target fails completeness"

        let evalMismatch = evaluateVetoes { cleanPacketInput () with CanaryBase = Ok(Some(cleanCanaryReceipt "01_HelloWorldDirect")); CanaryCandidate = Ok(Some(cleanCanaryReceipt "hello-p3")) }
        evalMismatch.Completeness.IsComplete |> Expect.isFalse "mismatched base/candidate governing canary targets fail completeness"

        let malformedPassJson = """{"target":"01_HelloWorldDirect","scaling":{"passes":[{"pass":"BadPass"}]}}"""
        match parseCanaryReceipt malformedPassJson with
        | Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", _)) -> ()
        | other -> failtestf "malformed scalingPasses row must be rejected, got %A" other

        let illTypedAllocWithSlope = """{"target":"01_HelloWorldDirect","scaling":{"passes":[{"pass":"BadAlloc","alloc1x":"oops","alloc4x":"oops","slope":2.0}]}}"""
        match parseCanaryReceipt illTypedAllocWithSlope with
        | Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "scalingPasses", _)) -> ()
        | other -> failtestf "ill-typed alloc1x/alloc4x must NOT fall through to slope, got %A" other

        let arraySurvivorsJson = """{"target":"01_HelloWorldDirect","survivors":[1234,5678]}"""
        match parseCanaryReceipt arraySurvivorsJson with
        | Ok r ->
          r.WatchdogSurvivors.IsSome |> Expect.isTrue "non-empty survivors array becomes watchdog breach"
          let evalArr = evaluateVetoes { cleanPacketInput () with CanaryCandidate = Ok(Some { cleanCanaryReceipt "01_HelloWorldDirect" with WatchdogSurvivors = r.WatchdogSurvivors }) }
          evalArr.Vetoes |> List.exists (function MechanicalVeto.CanaryWatchdogBreach _ -> true | _ -> false)
          |> Expect.isTrue "array survivors triggers CanaryWatchdogBreach"
        | Error e -> failtestf "expected Ok with WatchdogSurvivors for array, got %s" (PacketParseError.describe e)

        let objSurvivorsJson = """{"target":"01_HelloWorldDirect","survivors":{"pids":[1234]}}"""
        match parseCanaryReceipt objSurvivorsJson with
        | Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "watchdogSurvivors", _)) -> ()
        | other -> failtestf "non-null object survivors must be rejected as parse error, got %A" other

      testCase "fault-injection controls: watchdog survivors and scanError trigger CanaryWatchdogBreach veto" <| fun _ ->
        let wdSurvivors = "WATCHDOG rootPid=101 pgid=101 elapsedMs=2400 peakRssMB=160.0 peakRssBytes=167772160 breach=none exitCode=0 survivors=2"
        let parsedSurv = parseCanaryReceipt wdSurvivors |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedSurv.Target |> Expect.equal "text receipt without target leaves target empty" ""
        parsedSurv.WatchdogSurvivors |> Expect.equal "parses survivors=2" (Some "2")

        let baseInput = cleanPacketInput ()
        let candWithSurv = { cleanCanaryReceipt "01_HelloWorldDirect" with WatchdogSurvivors = parsedSurv.WatchdogSurvivors }
        let evalSurv = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some candWithSurv) }
        evalSurv.Vetoes |> Expect.contains "survivors=2 vetoes" (MechanicalVeto.CanaryWatchdogBreach "survivors=2")

        let wdScanErr = "WATCHDOG rootPid=101 pgid=101 elapsedMs=2400 peakRssMB=160.0 peakRssBytes=167772160 breach=none exitCode=0 survivors=none scanError=proc_read_failed"
        let parsedScan = parseCanaryReceipt wdScanErr |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedScan.WatchdogSurvivors |> Expect.equal "survivors=none is clean" None
        parsedScan.WatchdogScanError |> Expect.equal "parses scanError" (Some "proc_read_failed")
        let candWithScan = { cleanCanaryReceipt "01_HelloWorldDirect" with WatchdogScanError = parsedScan.WatchdogScanError }
        let evalScan = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some candWithScan) }
        evalScan.Vetoes |> Expect.contains "scanError vetoes" (MechanicalVeto.CanaryWatchdogBreach "scanError=proc_read_failed")

      testCase "fault-injection controls: 01_HelloWorldDirect governing caps cannot be relaxed by receipt limits or uniqueObligations" <| fun _ ->
        let baseInput = cleanPacketInput ()
        let hello = cleanCanaryReceipt "01_HelloWorldDirect"

        let cvc5Over = { hello with Cvc5Spawns = Some 5; UniqueObligations = Some 129 }
        let evalCvc5 = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some cvc5Over) }
        evalCvc5.Vetoes
        |> List.exists (function MechanicalVeto.Cvc5SpawnSpike(5, 4, _) -> true | _ -> false)
        |> Expect.isTrue "cvc5=5 with uniqueObligations=129 still vetoes against HelloWorld cap=4"

        let feOver = { hello with FrontEndMs = Some 7000.0; FrontEndLimitMs = None; TotalWallMs = Some 8000.0 }
        let evalFe = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some feOver) }
        evalFe.Vetoes
        |> Expect.contains "FrontEnd=7s without receipt limit vetoes against 6.5s Phase 1 policy cap" (MechanicalVeto.CanaryPhaseWallBreach("FrontEnd", 7000.0, 6500.0))

        let pdOver = { hello with ProofDischargeMs = Some 1500.0; ProofDischargeLimitMs = None }
        let evalPd = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some pdOver) }
        evalPd.Vetoes
        |> Expect.contains "ProofDischarge=1.5s vetoes against 1.0s governing policy cap" (MechanicalVeto.CanaryPhaseWallBreach("ProofDischarge", 1500.0, 1000.0))

        let helloP3Base = { cleanCanaryReceipt "hello-p3" with FrontEndLimitMs = None; ProofDischargeLimitMs = None; WallLimitMs = None; RssLimitBytes = None }
        let helloP3Over = { helloP3Base with FrontEndMs = Some 4000.0; ProofDischargeMs = Some 800.0 }
        let evalP3 = evaluateVetoes { baseInput with CanaryBase = Ok(Some helloP3Base); CanaryCandidate = Ok(Some helloP3Over) }
        evalP3.Vetoes |> Expect.contains "hello-p3 FrontEnd=4s vetoes against 3s governing cap" (MechanicalVeto.CanaryPhaseWallBreach("FrontEnd", 4000.0, 3000.0))
        evalP3.Vetoes |> Expect.contains "hello-p3 ProofDischarge=0.8s vetoes against 0.5s governing cap" (MechanicalVeto.CanaryPhaseWallBreach("ProofDischarge", 800.0, 500.0))

        let sqliteBase = { cleanCanaryReceipt "sqlite" with UniqueObligations = Some 10; Cvc5Spawns = Some 2; FrontEndLimitMs = None; ProofDischargeLimitMs = None; WallLimitMs = None; RssLimitBytes = None }
        let sqliteCvc5Over = { sqliteBase with Cvc5Spawns = Some 3 }
        let evalSqliteCvc5 = evaluateVetoes { baseInput with CanaryBase = Ok(Some sqliteBase); CanaryCandidate = Ok(Some sqliteCvc5Over) }
        evalSqliteCvc5.Vetoes
        |> List.exists (function MechanicalVeto.Cvc5SpawnSpike(3, 2, _) -> true | _ -> false)
        |> Expect.isTrue "sqlite cvc5=3 with uniqueObligations=10 vetoes against exact ceil(10/64)*2 = 2 cap (no max 4 floor)"

        let allocRegressed = { hello with PhaseAllocBytes = [ "obligations", 4_800_000L; "witness-publication", 5_000_000L ] }
        let evalAlloc = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some allocRegressed) }
        evalAlloc.Vetoes
        |> List.exists (function MechanicalVeto.CanaryPhaseAllocRegression("obligations", 4_000_000L, 4_800_000L, pct) when pct > 15.0 -> true | _ -> false)
        |> Expect.isTrue ">15% per-phase heap allocation increase triggers CanaryPhaseAllocRegression veto"
        renderPacket evalAlloc |> Expect.stringContains "renders phase allocation regression as VETO" "### Phase Allocation Regressions (`> +15%` vs Base — VETO)"

        let relaxedLimits = { hello with TotalWallMs = Some 16000.0; WallLimitMs = Some 30000.0; PeakRssBytes = Some(600L * 1_048_576L); RssLimitBytes = Some(2048L * 1_048_576L) }
        let evalRelaxed = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some relaxedLimits) }
        evalRelaxed.Vetoes |> Expect.contains "WallLimitMs=30s cannot relax 15s cap" (MechanicalVeto.CanaryWallBreach(16000.0, 15000.0))
        evalRelaxed.Vetoes |> Expect.contains "RssLimitBytes=2GiB cannot relax 512MiB cap" (MechanicalVeto.CanaryRssBreach(600L * 1_048_576L, 512L * 1_048_576L))

        let tightenedLimit = { hello with TotalWallMs = Some 11000.0; WallLimitMs = Some 10000.0 }
        let evalTight = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some tightenedLimit) }
        evalTight.Vetoes |> Expect.contains "receipt WallLimitMs=10s tightens governing cap via min" (MechanicalVeto.CanaryWallBreach(11000.0, 10000.0))

      testCase "fault-injection controls: projection counts, scaling slope > 6.0, Spatial zero-kernel, canary exit/compile flags, missing phaseAllocBytes, SARIF invocations/ruleIndex, block-comment strings, per-file anti-patterns, duplicate comments, and canonical Spec Ballast origin" <| fun _ ->
        let baseInput = cleanPacketInput ()
        let hello = cleanCanaryReceipt "01_HelloWorldDirect"

        let projOver = { hello with PlatformResolutionReads = Some 2; NumericProjectionCalls = Some 2; MemoryProjectionCalls = Some 3; SpatialProjectionCalls = Some 2 }
        let evalProj = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some projOver) }
        evalProj.Vetoes |> Expect.contains "PlatformResolution > 1 vetoes" (MechanicalVeto.ProjectionCountExceeded("PlatformResolution.read*", 2))
        evalProj.Vetoes |> Expect.contains "NumericPublication > 1 vetoes" (MechanicalVeto.ProjectionCountExceeded("NumericPublication.project", 2))
        evalProj.Vetoes |> Expect.contains "MemoryPublication > 1 vetoes" (MechanicalVeto.ProjectionCountExceeded("MemoryPublication.project", 3))
        evalProj.Vetoes |> Expect.contains "SpatialPublication > 1 vetoes" (MechanicalVeto.ProjectionCountExceeded("SpatialPublication.project", 2))

        let slopeOver = { hello with Scale1xAllocBytes = Some 1_000_000L; Scale4xAllocBytes = Some 7_500_000L; ScalingPasses = [ { PassName = "WitnessSeg"; Alloc1xBytes = 100L; Alloc4xBytes = 900L; Slope = 9.0 } ] }
        let evalSlope = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some slopeOver) }
        evalSlope.Vetoes |> Expect.contains "overall scaling slope 7.5 > 6.0 vetoes" (MechanicalVeto.ScalingSlopeExceeded("Scale_4x/Scale_1x", 7.5, 6.0))
        evalSlope.Vetoes |> Expect.contains "per-pass scaling slope 9.0 > 6.0 vetoes" (MechanicalVeto.ScalingSlopeExceeded("WitnessSeg", 9.0, 6.0))

        let spatialZero = { hello with HardwareAndKernelModuleCount = Some 0; SpatialProjectionCalls = Some 1; SpatialExecuted = Some true }
        let evalSpatial = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some spatialZero) }
        evalSpatial.Vetoes |> Expect.contains "Spatial with 0 kernel modules vetoes" (MechanicalVeto.SpatialExecutedWithZeroKernelModules 1)

        let spatialFanOutOnly = { spatialZero with SpatialProjectionCalls = Some 0 }
        let evalSpatialFanOut = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some spatialFanOutOnly) }
        evalSpatialFanOut.Vetoes |> Expect.contains "Spatial settlement with zero publication calls still vetoes" (MechanicalVeto.SpatialExecutedWithZeroKernelModules 1)

        let checkedSpatialAbsence = { spatialFanOutOnly with SpatialExecuted = Some false }
        missingCanaryMetrics checkedSpatialAbsence |> Expect.isEmpty "recorded absence with zero modules has complete metrics"
        let unobservedSpatialExecution = { checkedSpatialAbsence with SpatialExecuted = None }
        missingCanaryMetrics unobservedSpatialExecution |> Expect.contains "zero publication calls require settlement execution evidence" "spatialExecuted"

        let exitAndTestFail = { hello with NativeExitCode = Some 1; CompileExitCode = Some 2; FailedTestCompile = Some true }
        let evalExit = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some exitAndTestFail) }
        evalExit.Vetoes |> Expect.contains "native exit != 0 vetoes" (MechanicalVeto.CanaryExitBreach("native", 1))
        evalExit.Vetoes |> Expect.contains "compile exit != 0 vetoes" (MechanicalVeto.CanaryExitBreach("compile", 2))
        evalExit.Vetoes |> List.exists (function MechanicalVeto.FailedTestCompile _ -> true | _ -> false)
        |> Expect.isTrue "canary FailedTestCompile flag triggers FailedTestCompile veto"

        let missingPhaseKey = { hello with PhaseAllocBytes = [ "obligations", 4_000_000L ] }
        let evalMissingPhase = evaluateVetoes { baseInput with CanaryCandidate = Ok(Some missingPhaseKey) }
        evalMissingPhase.Completeness.IsComplete |> Expect.isFalse "missing base phaseAllocBytes key fails completeness"

        let emptyPhaseAlloc = { hello with PhaseAllocBytes = [] }
        missingCanaryMetrics emptyPhaseAlloc |> Expect.contains "empty phaseAllocBytes flagged" "phaseAllocBytes"

        let negCounter = { hello with PeakRssBytes = Some -1L; Cvc5Spawns = Some -2 }
        missingCanaryMetrics negCounter |> Expect.contains "negative RSS rejected" "peakRssBytes (must be >= 0)"
        missingCanaryMetrics negCounter |> Expect.contains "negative cvc5 rejected" "cvc5Spawns (must be >= 0)"

        tryGoverningPolicyFor "My01_HelloWorldDirectCopy.fidproj" |> Expect.isNone "spoofed target substring rejected"

        let sarifRuleIndexAndFail =
          sprintf """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"Alex.Analyzers","rules":[{"id":"AXC009","defaultConfiguration":{"level":"error"}}]}},"invocations":[{"executionSuccessful":false}],"results":[{"ruleIndex":0,"message":{"text":"Reflection banned"}},{"rule":{"id":"AXD000"},"level":"note","message":{"text":"AXD000: %s"}}]}]}""" validSha256A
        match parseSarifAndAxd000 (Some sarifRuleIndexAndFail) None with
        | Error e -> failtestf "expected SARIF parse Ok, got %s" (PacketParseError.describe e)
        | Ok(Some a) ->
          a.RedTargetDiagnostics |> List.exists (fun d -> d.RuleId = "AXC009" && d.Level = "error")
          |> Expect.isTrue "resolves ruleIndex -> AXC009 with rule defaultConfiguration error level"
          a.RedTargetDiagnostics |> List.exists (fun d -> d.RuleId = "SARIF_INVOCATION_FAILED")
          |> Expect.isTrue "invocations[].executionSuccessful=false triggers red diagnostic"
        | Ok None -> failtest "expected Some analyzer receipt"

        let blockCommentWithString = "(* \"(*\" *)\nlet mutable counter = 0"
        let lexCounts = countAntiPatternsInSource false blockCommentWithString
        lexCounts.LetMutable |> Expect.equal "quoted string inside block comment does not swallow subsequent let mutable" 1

        let cancelChanges : GitFileChange list = [
          { Kind = GitChangeKind.Modified; OldPath = Some "src/Compiler/A.fs"; NewPath = "src/Compiler/A.fs"; BeforeBlob = Some "let c = ConditionalWeakTable<string,int>()"; AfterBlob = Some "let c = Map.empty"; ReadError = None }
          { Kind = GitChangeKind.Modified; OldPath = Some "src/Compiler/B.fs"; NewPath = "src/Compiler/B.fs"; BeforeBlob = Some "let x = 1"; AfterBlob = Some "let c = ConditionalWeakTable<string,int>()"; ReadError = None }
        ]
        let cancelDelta = scanDiffWithChanges sampleSha "HEAD" sampleSha (Some "main") "" "" [] cancelChanges Map.empty
        let evalCancel = evaluateVetoes { baseInput with CodeDelta = Ok cancelDelta }
        evalCancel.Vetoes |> Expect.contains "per-file CWT addition in B.fs is NOT canceled by removal in A.fs" (MechanicalVeto.AntiPatternIncrease("ΔConditionalWeakTable", 1))

        let dupCommentsIssue = """{"number":38,"title":"Dup","body":"body","comments":2}"""
        let dupCommentsJson = """[{"id":11,"body":"first"},{"id":11,"body":"first"}]"""
        match parseIssuePayload 38 sampleIssueUrl dupCommentsIssue (Some dupCommentsJson) with
        | Error e -> failtestf "unexpected error: %s" (PacketParseError.describe e)
        | Ok parsedDup ->
          let evalDup = evaluateVetoes { baseInput with Issue = Ok parsedDup }
          evalDup.Completeness.IsComplete |> Expect.isFalse "duplicate comment IDs cannot satisfy ExpectedCommentCount=2"

        match parseIssuePayload 38 sampleIssueUrl """{"number":38,"title":"NoBody"}""" None with
        | Error(PacketParseError.MissingJsonField("Forgejo issue JSON", "body")) -> ()
        | other -> failtestf "issue JSON missing body must fail, got %A" other

        match parseClefSearchResponse "q" """{"results":[{"title":"Port","url":"https://clef-lang.com:8443/spec/x","snippet":"nonblank"}]}""" with
        | Error(PacketParseError.InvalidJsonField("clef-search response JSON", "results", _)) -> ()
        | other -> failtestf "non-canonical port :8443 on clef-lang.com must fail parse, got %A" other
        isValidSpecBallastHit { Title = "Port"; Url = "https://clef-lang.com:8443/spec/x"; Section = "spec"; ContentType = "text/markdown"; Snippet = "nonblank" }
        |> Expect.isFalse "isValidSpecBallastHit rejects non-canonical port :8443"

        // v5 controls: Δmax_file_lines scope & over-budget veto, nested typeof<Map<int,int>>.GetMethods, block-comment char literal,
        // zero-baseline phase alloc regression, non-HelloWorld canary pair, plain markdown issue completeness, SARIF extension rules & revision binding,
        // bare JSON {"ran":1} & earlier compileExitCode=1 preservation, private-query allowlist, and wrongly typed canary JSON fields.
        let docAndOverBudgetChanges : GitFileChange list = [
          { Kind = GitChangeKind.Added; OldPath = None; NewPath = "docs/huge.md"; BeforeBlob = None; AfterBlob = Some(String.concat "\n" (List.init 900 (fun i -> sprintf "line %d" i))); ReadError = None }
          { Kind = GitChangeKind.Modified; OldPath = Some "src/Compiler/Small.fs"; NewPath = "src/Compiler/Small.fs"; BeforeBlob = Some "let a = 1"; AfterBlob = Some "let a = 1\nlet b = 2"; ReadError = None }
        ]
        let docOnlyDelta = scanDiffWithChanges sampleSha "HEAD" sampleSha (Some "main") "" "" [] docAndOverBudgetChanges (Map.ofList [ "docs/huge.md", (None, Some 900); "src/Compiler/Small.fs", (Some 1, Some 2) ])
        docOnlyDelta.AntiPatterns.MaxFileLinesAfter |> Expect.equal "docs/huge.md excluded from src/ max_file_lines" (Some 2)
        docOnlyDelta.AntiPatterns.OverBudgetFileGrowth |> Expect.isEmpty "under-750 src/ growth does not veto"

        let overBudgetChanges : GitFileChange list = [
          { Kind = GitChangeKind.Modified; OldPath = Some "src/Compiler/Big.fs"; NewPath = "src/Compiler/Big.fs"; BeforeBlob = Some "let a = 1"; AfterBlob = Some "let a = 2"; ReadError = None }
        ]
        let overBudgetDelta = scanDiffWithChanges sampleSha "HEAD" sampleSha (Some "main") "" "" [] overBudgetChanges (Map.ofList [ "src/Compiler/Big.fs", (Some 760, Some 770) ])
        let evalOverBudget = evaluateVetoes { baseInput with CodeDelta = Ok overBudgetDelta }
        evalOverBudget.Vetoes |> Expect.contains "growing >750-line src/ F# file vetoes Δmax_file_lines" (MechanicalVeto.AntiPatternIncrease("Δmax_file_lines", 10))

        let nestedRefl = "(* '\"' *)\nlet ms = typeof<Map<int, int>>.GetMethods()\nlet a = Activator.CreateInstance(typeof<int>)\nlet sd = SortedDictionary<string,int>()"
        let nestedCounts = countAntiPatternsInSource false nestedRefl
        nestedCounts.SystemReflection |> Expect.equal "nested typeof<Map<int,int>>.GetMethods + Activator.CreateInstance detected past block-comment char quote" 2
        nestedCounts.BclMutable |> Expect.equal "SortedDictionary counted in ΔBCL_Mutable" 1

        let zeroBasePhase = { hello with PhaseAllocBytes = [ "obligations", 0L; "witness-publication", 5_000_000L ] }
        let zeroCandPhase = { hello with PhaseAllocBytes = [ "obligations", 1024L; "witness-publication", 5_000_000L ] }
        let evalZeroAlloc = evaluateVetoes { baseInput with CanaryBase = Ok(Some zeroBasePhase); CanaryCandidate = Ok(Some zeroCandPhase) }
        evalZeroAlloc.Vetoes
        |> List.exists (function MechanicalVeto.CanaryPhaseAllocRegression("obligations", 0L, 1024L, pct) -> Double.IsPositiveInfinity pct | _ -> false)
        |> Expect.isTrue "zero-to-positive phase allocation triggers CanaryPhaseAllocRegression"

        let sqliteCanary = cleanCanaryReceipt "Fidelity.SQLite"
        let evalSqliteOnly = evaluateVetoes { baseInput with CanaryBase = Ok(Some sqliteCanary); CanaryCandidate = Ok(Some sqliteCanary) }
        evalSqliteOnly.Completeness.IsComplete |> Expect.isFalse "Fidelity.SQLite cannot stand in for required 01_HelloWorldDirect baseline"

        let missingCanaryFields = missingCanaryMetrics { hello with PhaseTimingsMs = []; Scale1xWallMs = None; ScalingPasses = []; WatchdogCleared = false }
        missingCanaryFields |> Expect.contains "phaseTimingsMs required" "phaseTimingsMs"
        missingCanaryFields |> Expect.contains "scalingPasses required" "scalingPasses (per-pass scaling allocations required)"
        missingCanaryFields |> Expect.contains "watchdogClearance required" "watchdogClearance (watchdogSurvivors=0/none or explicit watchdog outcome required)"

        let evalSkippedBallast = evaluateVetoes { baseInput with SpecBallast = SpecBallastStatus.Skipped "--skip-clef-search flag set" }
        evalSkippedBallast.Completeness.IsComplete |> Expect.isFalse "--skip-clef-search marks packet incomplete (exit 3)"

        match parseIssuePayloadWithProvenance (AuthorityProvenance.LocalFile "issue.md") 38 sampleIssueUrl "# Plain markdown issue\nNo comment inventory." None with
        | Ok mdIssue ->
          mdIssue.CommentsVerified |> Expect.isFalse "plain markdown --issue-file has CommentsVerified=false"
          let evalMd = evaluateVetoes { baseInput with Issue = Ok mdIssue }
          evalMd.Completeness.IsComplete |> Expect.isFalse "plain markdown --issue-file without comment inventory fails completeness"
        | Error e -> failtestf "expected Ok parsed md issue for completeness check, got %A" e

        let extSarif =
          sprintf """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"Alex.Analyzers"},"extensions":[{"name":"Fidelity.Ext","rules":[{"id":"EXT001","defaultConfiguration":{"level":"error"}}]}]},"versionControlProvenance":[{"revisionId":"deadbeefdeadbeefdeadbeefdeadbeefdeadbeef"}],"results":[{"rule":{"id":"EXT001","toolComponent":{"name":"Fidelity.Ext"}},"message":{"text":"Ext red error"}},{"ruleId":"AXD000","level":"note","message":{"text":"AXD000: %s"}}]}]}""" validSha256A
        match parseSarifAndAxd000 (Some extSarif) (Some validSha256B) with
        | Ok(Some a) ->
          a.RedTargetDiagnostics |> List.exists (fun d -> d.RuleId = "EXT001" && d.Level = "error")
          |> Expect.isTrue "SARIF extension rule error severity resolved"
          a.Axd000Disagreement.IsSome |> Expect.isTrue "SARIF vs standalone AXD000 disagreement flagged"
          a.Axd000Valid |> Expect.isFalse "disagreeing AXD000 invalidates receipt"
          let evalRevMismatch = evaluateVetoes { baseInput with Analyzer = Ok(Some a) }
          evalRevMismatch.Vetoes
          |> List.exists (function MechanicalVeto.MissingAxd000Hash msg -> msg.Contains("versionControlProvenance") | _ -> false)
          |> Expect.isTrue "mismatched SARIF versionControlProvenance revision triggers veto"
        | other -> failtestf "expected Ok(Some) for extension SARIF, got %A" other

        match parseTestReceipt """{"ran":1}""" with
        | Error(PacketParseError.InvalidTestReceipt _) -> ()
        | other -> failtestf "bare JSON {ran:1} must be rejected, got %A" other

        match parseTestReceipt "compileExitCode=1\ncompileExitCode=0\nBuild succeeded." with
        | Ok tr -> tr.CompileSucceeded |> Expect.isFalse "earlier compileExitCode=1 cannot be masked by later compileExitCode=0"
        | Error e -> failtestf "expected failed TestReceipt, got %A" e

        let privateIssue = { cleanIssueAuthority () with Title = "SecretCustomerFoo internal nanopass PSG bug"; Body = "Do not leak InternalProjectXYZ" }
        let safeQuery = deriveSpecBallastQuery (Some privateIssue) [ "src/PrivateClientSecret/BakerPass.fs" ]
        safeQuery.Contains("SecretCustomerFoo", StringComparison.OrdinalIgnoreCase) |> Expect.isFalse "private title tokens never sent to public clef-search"
        safeQuery.Contains("PrivateClientSecret", StringComparison.OrdinalIgnoreCase) |> Expect.isFalse "private path stems never sent to public clef-search"
        safeQuery |> Expect.stringContains "retains public compiler vocabulary" "nanopass"

        match parseCanaryReceipt """{"target":"01_HelloWorldDirect","failedTestCompile":"true"}""" with
        | Error(PacketParseError.InvalidJsonField("Canary receipt JSON", "failedTestCompile", _)) -> ()
        | other -> failtestf "string failedTestCompile must fail parse, got %A" other
    ]

    testList "Part 1 & Part 6 soundness: comments, wiki rulings, retrieval revision coverage, --outside-index, and Spec Ballast" [

      testCase "negative controls: malformed/missing comments rejected, wiki decode seam, revision-bound file snapshots, dynamic fences, verbatim SARIF, and strict Spec Ballast" <| fun _ ->
        let issueJson = """{"number":38,"title":"Authority","state":"open","comments":1,"body":"Body."}"""
        match parseIssuePayload 38 sampleIssueUrl issueJson (Some "not-json") with
        | Error(PacketParseError.InvalidJson("Forgejo comments JSON", _)) -> ()
        | other -> failtestf "malformed comments JSON must fail, got %A" other

        match parseIssuePayload 38 sampleIssueUrl issueJson (Some "[{}]") with
        | Error(PacketParseError.MissingJsonField("Forgejo comments JSON", "comments[].body")) -> ()
        | other -> failtestf "comment object missing body must fail, got %A" other

        let droppedCommentsIssue =
          parseIssuePayload 38 sampleIssueUrl issueJson (Some "[]")
          |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        droppedCommentsIssue.ExpectedCommentCount |> Expect.equal "records comments: 1" (Some 1)
        let evalDroppedComments = evaluateVetoes { cleanPacketInput () with Issue = Ok droppedCommentsIssue }
        evalDroppedComments.Completeness.IsComplete |> Expect.isFalse "comments: 1 with [] fails Part 1 completeness"

        let issueFileMissingComments =
          parseIssuePayloadWithProvenance (AuthorityProvenance.LocalFile "issue.json") 38 sampleIssueUrl issueJson None
          |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        let evalFileMissingComments = evaluateVetoes { cleanPacketInput () with Issue = Ok issueFileMissingComments }
        evalFileMissingComments.Completeness.IsComplete |> Expect.isFalse "--issue-file with comments: 1 and no loaded comments fails completeness"

        let wikiRef = parseWikiUrl sampleWikiUrl |> Option.defaultWith (fun () -> failtest "valid wiki URL")
        let b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes "# Ruling\nMust hold.\n")
        let wikiJson = sprintf """{"title":"Course Correction","content_base64":"%s"}""" b64
        extractWikiBase64Field sampleWikiUrl wikiJson |> Expect.equal "extracts Base64 field for Zone B decoder" (Ok(Some b64))
        match parseWikiPagePayload (AuthorityProvenance.LiveFetch sampleWikiUrl) wikiRef wikiJson (Some "# Ruling\nMust hold.\n") with
        | Ok ruling ->
          ruling.Content |> Expect.equal "preserves decoded wiki ruling verbatim" "# Ruling\nMust hold.\n"
        | Error e -> failtestf "expected valid wiki ruling, got %s" (PacketParseError.describe e)
        match parseWikiPagePayload (AuthorityProvenance.LiveFetch sampleWikiUrl) wikiRef wikiJson None with
        | Error(PacketParseError.InvalidWikiPayload _) -> ()
        | other -> failtestf "undecoded content_base64 must fail, got %A" other

        let fileMissingCurrent = """{"freshness":{"fresh":true,"snapshot_id":"snap-only"}}"""
        let parsedMissingCur = parseRetrievalSnapshot fileMissingCurrent |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedMissingCur.IsValidFresh |> Expect.isFalse "file snapshot missing current_snapshot_id fails freshness"

        let fileBoundProv = RetrievalProvenance.SnapshotFile("retrieval.json", Some "clef", Some sampleSha)
        let fileWrongShaJson = """{"freshness":{"fresh":true,"snapshot_id":"snap-1","current_snapshot_id":"snap-1"},"revisions":{"clef":"deadbeef00000000000000000000000000000000"}}"""
        let parsedFileWrongSha = parseRetrievalSnapshotWithProvenance fileBoundProv fileWrongShaJson |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedFileWrongSha.IsValidFresh |> Expect.isFalse "file snapshot with wrong SHA fails revision coverage"

        let fileShortPrefixJson = sprintf """{"freshness":{"fresh":true,"snapshot_id":"snap-1","current_snapshot_id":"snap-1"},"revisions":{"clef":"%s"}}""" (sampleSha.Substring(0, 7))
        let parsedShortPrefix = parseRetrievalSnapshotWithProvenance fileBoundProv fileShortPrefixJson |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedShortPrefix.IsValidFresh |> Expect.isFalse "7-char SHA prefix does not satisfy full 40-char expected SHA"

        let evalOutsideStale =
          evaluateVetoes {
            cleanPacketInput () with
              OutsideIndex = true
              Retrieval = Ok(Some parsedMissingCur)
          }
        evalOutsideStale.Vetoes
        |> List.exists (function MechanicalVeto.StaleOrMissingRetrievalSnapshot _ -> true | _ -> false)
        |> Expect.isTrue "--outside-index must NOT suppress stale veto when --retrieval-snapshot was supplied"

        let liveProv = RetrievalProvenance.LiveAwait("http://127.0.0.1:47749/api/retrieval/await", Some "clef", Some sampleSha)
        let wrongShaJson = """{"ok":true,"status":{"state":"fresh","snapshotId":"snap-1","revisions":{"clef":"deadbeef00000000000000000000000000000000"}}}"""
        let parsedWrongSha = parseRetrievalSnapshotWithProvenance liveProv wrongShaJson |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        parsedWrongSha.IsValidFresh |> Expect.isFalse "live await response missing expectedSha coverage fails freshness"

        match parseClefSearchResponse "Zone A" """{"results":[{}]}""" with
        | Error(PacketParseError.InvalidJsonField("clef-search response JSON", "results", _)) -> ()
        | other -> failtestf "empty Spec Ballast object hit must fail, got %A" other

        match parseClefSearchResponse "Zone A" """{"results":[{"pageUrl":"https://evil.example/spec","snippet":"foreign"}]}""" with
        | Error(PacketParseError.InvalidJsonField("clef-search response JSON", "results", _)) -> ()
        | other -> failtestf "foreign host Spec Ballast hit must fail, got %A" other

        match parseClefSearchResponse "Zone A" """{"results":[{"pageUrl":"/spec/zone-a","snippet":"   "}]}""" with
        | Error(PacketParseError.InvalidJsonField("clef-search response JSON", "results", _)) -> ()
        | other -> failtestf "blank snippet Spec Ballast hit must fail, got %A" other

        let emptyBallast = evaluateVetoes { cleanPacketInput () with SpecBallast = SpecBallastStatus.Loaded("q", "hybrid", []) }
        emptyBallast.Completeness.IsComplete |> Expect.isFalse "empty Spec Ballast without --skip-clef-search is incomplete"

        let clean = evaluateVetoes (cleanPacketInput ())
        clean.Vetoes |> Expect.isEmpty "clean packet has 0 vetoes"
        let md = renderPacket clean
        md |> Expect.stringContains "renders raw numstat section" "### Verbatim `git diff --numstat`"
        md |> Expect.stringContains "renders raw SARIF section" "### Raw Analyzer SARIF (Verbatim)"
        md |> Expect.stringContains "renders verbatim wiki ruling" "Zone A pure core ruling."
        md |> Expect.stringContains "renders Part 1 provenance" "- Provenance: live fetch"
        md |> Expect.stringContains "renders Part 6 provenance" "- Provenance: local file (`--retrieval-snapshot retrieval.json`)"

        let fiveTickRuling = "Line1\n`````fsharp\nlet x = 1\n`````\nTrailing\n"
        let bigSarifTail = "UNIQUE_SARIF_TAIL_MARKER_70K"
        let hugeRawSarif = String(' ', 70000) + bigSarifTail
        let baseClean = cleanPacketInput ()
        let issueWithTicks = { cleanIssueAuthority () with WikiRulings = [ { (cleanIssueAuthority ()).WikiRulings[0] with Content = fiveTickRuling } ] }
        let analyzerHuge = { cleanAnalyzerReceipt () with RawSarif = hugeRawSarif }
        let renderedVerbatim = renderPacket (evaluateVetoes { baseClean with Issue = Ok issueWithTicks; Analyzer = Ok(Some analyzerHuge) })
        renderedVerbatim |> Expect.stringContains "uses 6-backtick fence when ruling contains 5 backticks" ("``````markdown\n" + fiveTickRuling + "``````")
        renderedVerbatim |> Expect.stringContains "preserves full raw SARIF > 64 KiB without truncation" bigSarifTail
    ]

    testList "Live dotnet fsi tools/audit/make-packet.fsx independent fault-injection controls" [

      testCaseTask "live CLI independent controls: clean (0), CWT-only (2), missing-AXD000-only (2), pure rename into src/ (2), string-literal non-masking (2), empty {} receipts (3), bad env port (64), unwritable --out (3)" <| fun () -> task {
        let bozzettoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
        let scriptPath = Path.Combine(bozzettoRoot, "tools", "audit", "make-packet.fsx")
        let scratchDir = Path.Combine(bozzettoRoot, "test-results", "scratch", "audit-packet-v2-cli-" + Guid.NewGuid().ToString("N"))
        let repoDir = Path.Combine(scratchDir, "repo")
        let srcDir = Path.Combine(repoDir, "src", "Compiler")
        let testSrcDir = Path.Combine(repoDir, "src", "Compiler.Tests")
        Directory.CreateDirectory srcDir |> ignore
        Directory.CreateDirectory testSrcDir |> ignore
        try
          let! _ = runGitOrFailAsync repoDir [ "init" ]
          let targetFs = Path.Combine(srcDir, "Sample.fs")
          let testCacheFs = Path.Combine(testSrcDir, "Cache.fs")
          File.WriteAllText(targetFs, "module Sample\nlet msg = \"ConditionalWeakTable\"\nlet x = 1\n")
          File.WriteAllText(testCacheFs, "module TestCache\nlet c = ConditionalWeakTable<obj, obj>()\n")
          let! _ = runGitOrFailAsync repoDir [ "add"; "." ]
          let! _ = runGitOrFailAsync repoDir [ "-c"; "user.name=AuditTest"; "-c"; "user.email=audit@test.local"; "commit"; "-m"; "base" ]
          let! baseSha = runGitOrFailAsync repoDir [ "rev-parse"; "HEAD" ]

          // Commit 1: clean change
          File.WriteAllText(targetFs, "module Sample\nlet msg = \"ConditionalWeakTable\"\nlet x = 2\n")
          let! _ = runGitOrFailAsync repoDir [ "add"; "." ]
          let! _ = runGitOrFailAsync repoDir [ "-c"; "user.name=AuditTest"; "-c"; "user.email=audit@test.local"; "commit"; "-m"; "clean" ]
          let! cleanSha = runGitOrFailAsync repoDir [ "rev-parse"; "HEAD" ]

          // Commit 2 (from base): CWT-only fault (also removes string literal "ConditionalWeakTable" to prove string removal cannot mask real CWT!)
          let! _ = runGitOrFailAsync repoDir [ "checkout"; "-b"; "feat/cwt-only"; baseSha ]
          File.WriteAllText(targetFs, "module Sample\nlet c = ConditionalWeakTable<obj, obj>()\nlet x = 1\n")
          let! _ = runGitOrFailAsync repoDir [ "add"; "." ]
          let! _ = runGitOrFailAsync repoDir [ "-c"; "user.name=AuditTest"; "-c"; "user.email=audit@test.local"; "commit"; "-m"; "cwt-only" ]
          let! cwtSha = runGitOrFailAsync repoDir [ "rev-parse"; "HEAD" ]

          // Commit 3 (from base): pure git mv from src/Compiler.Tests/Cache.fs to src/Compiler/Cache.fs (0 edited lines!)
          let! _ = runGitOrFailAsync repoDir [ "checkout"; "-b"; "feat/pure-rename"; baseSha ]
          let! _ = runGitOrFailAsync repoDir [ "mv"; "src/Compiler.Tests/Cache.fs"; "src/Compiler/Cache.fs" ]
          let! _ = runGitOrFailAsync repoDir [ "-c"; "user.name=AuditTest"; "-c"; "user.email=audit@test.local"; "commit"; "-m"; "pure-rename" ]
          let! renameSha = runGitOrFailAsync repoDir [ "rev-parse"; "HEAD" ]

          let issueFile = Path.Combine(scratchDir, "issue.json")
          File.WriteAllText(issueFile, sprintf """{"number":38,"title":"Clean tranche","state":"open","comments":0,"html_url":"%s","body":"See %s"}""" sampleIssueUrl sampleWikiUrl)
          let wikiFile = Path.Combine(scratchDir, "ruling.md")
          File.WriteAllText(wikiFile, "# Course Correction Ruling\nExact offline wiki ruling text.")
          let cleanSarif = Path.Combine(scratchDir, "clean.sarif")
          File.WriteAllText(cleanSarif, sampleRawSarif)
          let noAxdSarif = Path.Combine(scratchDir, "no-axd.sarif")
          File.WriteAllText(noAxdSarif, """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"Alex.Analyzers"}},"results":[]}]}""")
          let emptyObjFile = Path.Combine(scratchDir, "empty.json")
          File.WriteAllText(emptyObjFile, "{}")
          let canaryBase = Path.Combine(scratchDir, "canary-base.json")
          File.WriteAllText(canaryBase, validCanaryJson)
          let canaryClean = Path.Combine(scratchDir, "canary-clean.json")
          File.WriteAllText(canaryClean, validCanaryJson)
          let retrievalSnap = Path.Combine(scratchDir, "retrieval.json")
          File.WriteAllText(retrievalSnap, validRetrievalJsonFor "repo" baseSha)
          let testReceipt = Path.Combine(scratchDir, "test-receipt.json")
          File.WriteAllText(testReceipt, validTestReceiptJson)
          let specBallastFile = Path.Combine(scratchDir, "spec-ballast.json")
          File.WriteAllText(specBallastFile, validSpecBallastJson)

          let runCli envExtras extraArgs =
            runProcEnvAsync "dotnet" bozzettoRoot envExtras ([ "fsi"; scriptPath; repoDir; baseSha; sampleIssueUrl ] @ extraArgs)

          let baseValidArgs candSha outPath = [
            "--candidate"; candSha
            "--issue-file"; issueFile
            "--wiki-file"; sprintf "%s=%s" sampleWikiUrl wikiFile
            "--sarif"; cleanSarif
            "--canary-base"; canaryBase
            "--canary-candidate"; canaryClean
            "--retrieval-snapshot"; retrievalSnap
            "--test-receipt"; testReceipt
            "--spec-ballast-file"; specBallastFile
            "--skip-retrieval-await"
            "--out"; outPath
          ]

          // Positive control: clean candidate with --wiki-file and --test-receipt -> exit 0
          let cleanOut = Path.Combine(scratchDir, "clean.md")
          let! cleanExit, _, cleanErr = runCli [] (baseValidArgs cleanSha cleanOut)
          cleanExit |> Expect.equal (sprintf "clean candidate exits 0 (stderr=%s)" cleanErr) 0
          let cleanMd = File.ReadAllText cleanOut
          cleanMd |> Expect.stringContains "clean says PASS" "MECHANICAL VERDICT: PASS"
          cleanMd |> Expect.stringContains "embeds offline wiki ruling verbatim" "Exact offline wiki ruling text."
          cleanMd |> Expect.stringContains "embeds verbatim SARIF" "### Raw Analyzer SARIF (Verbatim)"
          cleanMd |> Expect.stringContains "embeds verbatim numstat" "### Verbatim `git diff --numstat`"

          // (a) & (e) Independent CWT-only fault (with string literal removal attempting to mask CWT) -> exit 2
          let cwtOut = Path.Combine(scratchDir, "cwt-only.md")
          let! cwtExit, _, cwtErr = runCli [] (baseValidArgs cwtSha cwtOut)
          cwtExit |> Expect.equal (sprintf "CWT-only fault exits 2 (stderr=%s)" cwtErr) 2
          let cwtMd = File.ReadAllText cwtOut
          cwtMd |> Expect.stringContains "CWT-only says REJECT" "MECHANICAL VERDICT: REJECT"
          cwtMd |> Expect.stringContains "CWT-only emits exact VETO line" "- **VETO:** Upward anti-pattern delta ΔConditionalWeakTable = +1 (> 0)"
          cwtMd.Contains("Missing/invalid AXD000 hash", StringComparison.Ordinal)
          |> Expect.isFalse "CWT-only test must NOT conflate AXD000 fault"

          // (b) Independent missing-AXD000-only fault (clean code, SARIF lacks AXD000) -> exit 2
          let axdOut = Path.Combine(scratchDir, "axd-only.md")
          let! axdExit, _, axdErr =
            runCli [] [
              "--candidate"; cleanSha
              "--issue-file"; issueFile
              "--wiki-file"; sprintf "%s=%s" sampleWikiUrl wikiFile
              "--sarif"; noAxdSarif
              "--canary-base"; canaryBase
              "--canary-candidate"; canaryClean
              "--retrieval-snapshot"; retrievalSnap
              "--test-receipt"; testReceipt
              "--spec-ballast-file"; specBallastFile
              "--skip-retrieval-await"
              "--out"; axdOut
            ]
          axdExit |> Expect.equal (sprintf "missing-AXD000-only fault exits 2 (stderr=%s)" axdErr) 2
          let axdMd = File.ReadAllText axdOut
          axdMd |> Expect.stringContains "AXD000-only emits exact VETO" "- **VETO:** Missing/invalid AXD000 hash:"
          axdMd.Contains("Upward anti-pattern delta", StringComparison.Ordinal)
          |> Expect.isFalse "AXD000-only test must NOT conflate anti-pattern fault"

          // (d) Pure rename from src/Compiler.Tests/Cache.fs to src/Compiler/Cache.fs carrying CWT -> exit 2
          let renameOut = Path.Combine(scratchDir, "rename-cwt.md")
          let! renameExit, _, renameErr = runCli [] (baseValidArgs renameSha renameOut)
          renameExit |> Expect.equal (sprintf "pure rename carrying CWT exits 2 (stderr=%s)" renameErr) 2
          let renameMd = File.ReadAllText renameOut
          renameMd |> Expect.stringContains "pure rename emits exact CWT VETO" "- **VETO:** Upward anti-pattern delta ΔConditionalWeakTable = +1 (> 0)"

          // (c) Empty {} SARIF, empty {} canary, and empty {} test receipt each independently exit 3
          let emptySarifOut = Path.Combine(scratchDir, "empty-sarif.md")
          let! emptySarifExit, _, _ =
            runCli [] [
              "--candidate"; cleanSha; "--issue-file"; issueFile; "--wiki-file"; sprintf "%s=%s" sampleWikiUrl wikiFile
              "--sarif"; emptyObjFile; "--canary-base"; canaryBase; "--canary-candidate"; canaryClean
              "--retrieval-snapshot"; retrievalSnap; "--test-receipt"; testReceipt
              "--skip-retrieval-await"; "--skip-clef-search"; "--out"; emptySarifOut
            ]
          emptySarifExit |> Expect.equal "empty {} SARIF exits 3" 3

          let emptyCanaryOut = Path.Combine(scratchDir, "empty-canary.md")
          let! emptyCanaryExit, _, _ =
            runCli [] [
              "--candidate"; cleanSha; "--issue-file"; issueFile; "--wiki-file"; sprintf "%s=%s" sampleWikiUrl wikiFile
              "--sarif"; cleanSarif; "--canary-base"; emptyObjFile; "--canary-candidate"; emptyObjFile
              "--retrieval-snapshot"; retrievalSnap; "--test-receipt"; testReceipt
              "--skip-retrieval-await"; "--skip-clef-search"; "--out"; emptyCanaryOut
            ]
          emptyCanaryExit |> Expect.equal "empty {} canary exits 3" 3

          let emptyTestOut = Path.Combine(scratchDir, "empty-test.md")
          let! emptyTestExit, _, _ =
            runCli [] [
              "--candidate"; cleanSha; "--issue-file"; issueFile; "--wiki-file"; sprintf "%s=%s" sampleWikiUrl wikiFile
              "--sarif"; cleanSarif; "--canary-base"; canaryBase; "--canary-candidate"; canaryClean
              "--retrieval-snapshot"; retrievalSnap; "--test-receipt"; emptyObjFile
              "--skip-retrieval-await"; "--skip-clef-search"; "--out"; emptyTestOut
            ]
          emptyTestExit |> Expect.equal "empty {} test receipt exits 3" 3

          // Omitting Spec Ballast via --skip-clef-search -> exit 3 (incomplete partial packet per #11094)
          let skipSearchOut = Path.Combine(scratchDir, "skip-search.md")
          let! skipSearchExit, _, _ =
            runCli [] [
              "--candidate"; cleanSha; "--issue-file"; issueFile; "--wiki-file"; sprintf "%s=%s" sampleWikiUrl wikiFile
              "--sarif"; cleanSarif; "--canary-base"; canaryBase; "--canary-candidate"; canaryClean
              "--retrieval-snapshot"; retrievalSnap; "--test-receipt"; testReceipt
              "--skip-retrieval-await"; "--skip-clef-search"; "--out"; skipSearchOut
            ]
          skipSearchExit |> Expect.equal "--skip-clef-search exits 3 (incomplete)" 3

          // Plain markdown --issue-file without comment inventory -> exit 3
          let mdIssueFile = Path.Combine(scratchDir, "issue.md")
          File.WriteAllText(mdIssueFile, "# Plain markdown\nNo comments array.")
          let mdIssueOut = Path.Combine(scratchDir, "md-issue.md")
          let! mdIssueExit, _, _ =
            runCli [] [
              "--candidate"; cleanSha; "--issue-file"; mdIssueFile
              "--sarif"; cleanSarif; "--canary-base"; canaryBase; "--canary-candidate"; canaryClean
              "--retrieval-snapshot"; retrievalSnap; "--test-receipt"; testReceipt
              "--spec-ballast-file"; specBallastFile; "--skip-retrieval-await"; "--out"; mdIssueOut
            ]
          mdIssueExit |> Expect.equal "plain markdown --issue-file exits 3 (unverified comments)" 3

          // Invalid BOZZETTO_MCP_PORT -> exit 64 (no silent fallback)
          let! badPortExit, _, _ = runCli [ "BOZZETTO_MCP_PORT", "notaport" ] (baseValidArgs cleanSha (Path.Combine(scratchDir, "bad-port.md")))
          badPortExit |> Expect.equal "invalid BOZZETTO_MCP_PORT exits 64" 64

          // Unwritable --out (existing directory path) -> exit 3 cleanly
          let! badOutExit, _, _ = runCli [] (baseValidArgs cleanSha scratchDir)
          badOutExit |> Expect.equal "unwritable --out exits 3 cleanly" 3
        finally
          try
            if Directory.Exists scratchDir then Directory.Delete(scratchDir, true)
          with _ -> ()
      }

      testCaseTask "live CLI HTTP integration: fetches issue + comments + same-host wiki ruling, passes expectedRepo/expectedSha, scopes Forgejo token, and fails exit 3 on wiki/comments failure" <| fun () -> task {
        let bozzettoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
        let scriptPath = Path.Combine(bozzettoRoot, "tools", "audit", "make-packet.fsx")
        let scratchDir = Path.Combine(bozzettoRoot, "test-results", "scratch", "audit-packet-v2-http-" + Guid.NewGuid().ToString("N"))
        let repoDir = Path.Combine(scratchDir, "repo")
        Directory.CreateDirectory(Path.Combine(repoDir, "src")) |> ignore

        File.WriteAllText(Path.Combine(repoDir, "src", "A.fs"), "module A\nlet a = 1\n")
        let! _ = runGitOrFailAsync repoDir [ "init" ]
        let! _ = runGitOrFailAsync repoDir [ "remote"; "add"; "origin"; "https://forge.spkez.dev/FidelityFramework/Bozzetto.git" ]
        let! _ = runGitOrFailAsync repoDir [ "add"; "." ]
        let! _ = runGitOrFailAsync repoDir [ "-c"; "user.name=AuditTest"; "-c"; "user.email=audit@test.local"; "commit"; "-m"; "base" ]
        let! baseSha = runGitOrFailAsync repoDir [ "rev-parse"; "HEAD" ]
        File.WriteAllText(Path.Combine(repoDir, "src", "A.fs"), "module A\nlet a = 2\n")
        let! _ = runGitOrFailAsync repoDir [ "add"; "." ]
        let! _ = runGitOrFailAsync repoDir [ "-c"; "user.name=AuditTest"; "-c"; "user.email=audit@test.local"; "commit"; "-m"; "cand" ]

        let captureGate = obj ()
        let capturedAuth = ResizeArray<string>()
        let capturedAwaitBodies = ResizeArray<string>()

        let builder = WebApplication.CreateBuilder()
        builder.Logging.ClearProviders() |> ignore
        builder.WebHost.UseUrls("http://127.0.0.1:0") |> ignore
        let app = builder.Build()

        let jsonHandler (statusCode: int) (payload: string) : RequestDelegate =
          RequestDelegate(fun (ctx: HttpContext) ->
            task {
              match ctx.Request.Headers.TryGetValue("Authorization") with
              | true, values -> lock captureGate (fun () -> capturedAuth.Add(values.ToString()))
              | false, _ -> ()
              ctx.Response.StatusCode <- statusCode
              ctx.Response.ContentType <- "application/json"
              let bytes = Encoding.UTF8.GetBytes payload
              do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            } :> Task)

        let awaitHandler : RequestDelegate =
          RequestDelegate(fun (ctx: HttpContext) ->
            task {
              use reader = new StreamReader(ctx.Request.Body, Encoding.UTF8)
              let! reqBody = reader.ReadToEndAsync()
              lock captureGate (fun () -> capturedAwaitBodies.Add reqBody)
              ctx.Response.StatusCode <- 200
              ctx.Response.ContentType <- "application/json"
              let respJson =
                sprintf
                  """{"ok":true,"outcome":"fresh","status":{"state":"fresh","branchStatus":"on_main_indexed","branch":"main","onMain":true,"generation":215,"snapshotId":"snap-http-215","revisions":{"Bozzetto":"%s"}}}"""
                  baseSha
              let bytes = Encoding.UTF8.GetBytes respJson
              do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            } :> Task)

        let wikiBodyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes "# Live Wiki Ruling\nGoverning verbatim wiki content.")

        // Issue 38: full positive control (issue + comments + same-host wiki ruling)
        let issue38Handler : RequestDelegate =
          RequestDelegate(fun (ctx: HttpContext) ->
            task {
              match ctx.Request.Headers.TryGetValue("Authorization") with
              | true, values -> lock captureGate (fun () -> capturedAuth.Add(values.ToString()))
              | false, _ -> ()
              let host = ctx.Request.Host.Value
              let payload =
                sprintf
                  """{"number":38,"title":"HTTP Issue Fetch","state":"open","comments":1,"html_url":"http://%s/FidelityFramework/Bozzetto/issues/38","body":"Live body referencing http://%s/FidelityFramework/Farscape/wiki/Course-Correction-2026-11-19"}"""
                  host host
              ctx.Response.StatusCode <- 200
              ctx.Response.ContentType <- "application/json"
              let bytes = Encoding.UTF8.GetBytes payload
              do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            } :> Task)

        // Issue 39: references a missing wiki page -> wiki fetch returns 404 -> exit 3
        let issue39Handler : RequestDelegate =
          RequestDelegate(fun (ctx: HttpContext) ->
            task {
              let host = ctx.Request.Host.Value
              let payload =
                sprintf
                  """{"number":39,"title":"Missing Wiki Test","state":"open","comments":0,"html_url":"http://%s/FidelityFramework/Bozzetto/issues/39","body":"See http://%s/FidelityFramework/Farscape/wiki/Missing-Page"}"""
                  host host
              ctx.Response.StatusCode <- 200
              ctx.Response.ContentType <- "application/json"
              let bytes = Encoding.UTF8.GetBytes payload
              do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            } :> Task)

        app.MapGet("/api/v1/repos/FidelityFramework/Bozzetto/issues/38", issue38Handler) |> ignore
        app.MapGet("/api/v1/repos/FidelityFramework/Bozzetto/issues/38/comments", jsonHandler 200 """[{"id":1,"user":{"username":"hhh"},"body":"Spec Ballast comment.","html_url":"http://127.0.0.1/#c1","created_at":"2026-11-20T00:00:00Z"}]""") |> ignore
        app.MapGet("/api/v1/repos/FidelityFramework/Farscape/wiki/page/Course-Correction-2026-11-19", jsonHandler 200 (sprintf """{"title":"Course Correction 2026-11-19","content_base64":"%s"}""" wikiBodyB64)) |> ignore

        app.MapGet("/api/v1/repos/FidelityFramework/Bozzetto/issues/39", issue39Handler) |> ignore
        app.MapGet("/api/v1/repos/FidelityFramework/Bozzetto/issues/39/comments", jsonHandler 200 "[]") |> ignore
        app.MapGet("/api/v1/repos/FidelityFramework/Farscape/wiki/page/Missing-Page", jsonHandler 404 """{"message":"wiki page not found"}""") |> ignore

        // Issue 40: comments endpoint fails with HTTP 500 -> exit 3
        app.MapGet("/api/v1/repos/FidelityFramework/Bozzetto/issues/40", jsonHandler 200 """{"number":40,"title":"Broken Comments","state":"open","comments":0,"body":"No wiki."}""") |> ignore
        app.MapGet("/api/v1/repos/FidelityFramework/Bozzetto/issues/40/comments", jsonHandler 500 """{"message":"internal error"}""") |> ignore

        app.MapPost("/api/retrieval/await", awaitHandler) |> ignore
        app.MapPost("/search/hybrid", jsonHandler 200 """{"fusionMethod":"rrf","results":[{"pageTitle":"Clef Saturation","sectionTitle":"Core Boundary","pageUrl":"/spec/saturation","contentType":"spec","snippet":"Monotone saturation cone."}]}""") |> ignore
        app.MapPost("/search/empty", jsonHandler 200 """{"fusionMethod":"rrf","results":[]}""") |> ignore

        do! app.StartAsync()
        let! runResult = task {
          try
            let address =
              app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses
              |> Seq.head
            let uri = Uri address
            let port = uri.Port

            let sarifPath = Path.Combine(scratchDir, "ok.sarif")
            File.WriteAllText(sarifPath, sampleRawSarif)
            let canaryPath = Path.Combine(scratchDir, "canary.json")
            File.WriteAllText(canaryPath, validCanaryJson)
            let testReceiptPath = Path.Combine(scratchDir, "test-receipt.json")
            File.WriteAllText(testReceiptPath, validTestReceiptJson)
            let searchUrl = sprintf "http://127.0.0.1:%d/search/hybrid" port
            let emptySearchUrl = sprintf "http://127.0.0.1:%d/search/empty" port

            // (f-pos) Live HTTP positive control: ambient FORGEJO_TOKEN must NOT be sent to loopback without BOZZETTO_ALLOW_TEST_TOKEN_ORIGIN,
            // and ambient HTTP_PROXY must NOT intercept loopback requests.
            let out38 = Path.Combine(scratchDir, "http-38.md")
            let! exit38, _, err38 =
              runProcEnvAsync "dotnet" bozzettoRoot [
                "CLEF_SEARCH_URL", searchUrl
                "FORGEJO_TOKEN", "secret-must-not-leak"
                "BOZZETTO_ALLOW_TEST_TOKEN_ORIGIN", ""
                "HTTP_PROXY", "http://127.0.0.1:1"
              ] [
                "fsi"; scriptPath
                repoDir; baseSha; sprintf "http://127.0.0.1:%d/FidelityFramework/Bozzetto/issues/38" port
                "--sarif"; sarifPath
                "--canary-base"; canaryPath
                "--canary-candidate"; canaryPath
                "--test-receipt"; testReceiptPath
                "--bozzetto-port"; string port
                "--spec-ballast-query"; "Zone A pure boundary"
                "--out"; out38
              ]
            exit38 |> Expect.equal (sprintf "live HTTP packet exited cleanly (stderr=%s)" err38) 0
            let md38 = File.ReadAllText out38
            md38 |> Expect.stringContains "fetched live issue title" "HTTP Issue Fetch"
            md38 |> Expect.stringContains "fetched live comment" "Spec Ballast comment."
            md38 |> Expect.stringContains "fetched live wiki ruling verbatim" "Governing verbatim wiki content."
            md38 |> Expect.stringContains "awaited live retrieval snapshot" "snap-http-215"
            md38 |> Expect.stringContains "embedded live Spec Ballast hit" "Clef Saturation"

            let leakedAuth = lock captureGate (fun () -> capturedAuth |> Seq.toList)
            leakedAuth |> Expect.isEmpty "ambient FORGEJO_TOKEN must NOT be sent to untrusted loopback HTTP host"

            let awaitBodies = lock captureGate (fun () -> capturedAwaitBodies |> Seq.toList)
            awaitBodies |> Expect.isNonEmpty "POST /api/retrieval/await was called"
            match Json.parse awaitBodies[0] with
            | Error e -> failtestf "await body invalid JSON: %s" e
            | Ok bodyJson ->
              tryNonBlankStringProp [ "expectedRepo" ] bodyJson |> Expect.equal "sends expectedRepo from git origin" (Some "Bozzetto")
              tryNonBlankStringProp [ "expectedSha" ] bodyJson |> Expect.equal "sends expectedSha matching baseSha" (Some baseSha)

            // (f-fail) Same-host wiki ruling fetch failure (404) -> exit 3
            let out39 = Path.Combine(scratchDir, "http-39.md")
            let! exit39, _, _ =
              runProcEnvAsync "dotnet" bozzettoRoot [ "CLEF_SEARCH_URL", searchUrl ] [
                "fsi"; scriptPath
                repoDir; baseSha; sprintf "http://127.0.0.1:%d/FidelityFramework/Bozzetto/issues/39" port
                "--sarif"; sarifPath; "--canary-base"; canaryPath; "--canary-candidate"; canaryPath
                "--test-receipt"; testReceiptPath; "--bozzetto-port"; string port; "--out"; out39
              ]
            exit39 |> Expect.equal "failed same-host wiki ruling fetch exits 3" 3
            File.ReadAllText out39 |> Expect.stringContains "wiki failure renders BLOCKED" "BLOCKED: incomplete packet"

            // (h) Comments fetch failure (500) -> exit 3
            let out40 = Path.Combine(scratchDir, "http-40.md")
            let! exit40, _, _ =
              runProcEnvAsync "dotnet" bozzettoRoot [ "CLEF_SEARCH_URL", searchUrl ] [
                "fsi"; scriptPath
                repoDir; baseSha; sprintf "http://127.0.0.1:%d/FidelityFramework/Bozzetto/issues/40" port
                "--sarif"; sarifPath; "--canary-base"; canaryPath; "--canary-candidate"; canaryPath
                "--test-receipt"; testReceiptPath; "--bozzetto-port"; string port; "--out"; out40
              ]
            exit40 |> Expect.equal "comments fetch failure exits 3" 3

            // Empty Spec Ballast without --skip-clef-search -> exit 3
            let outEmptySearch = Path.Combine(scratchDir, "http-empty-ballast.md")
            let! exitEmptySearch, _, _ =
              runProcEnvAsync "dotnet" bozzettoRoot [ "CLEF_SEARCH_URL", emptySearchUrl ] [
                "fsi"; scriptPath
                repoDir; baseSha; sprintf "http://127.0.0.1:%d/FidelityFramework/Bozzetto/issues/38" port
                "--sarif"; sarifPath; "--canary-base"; canaryPath; "--canary-candidate"; canaryPath
                "--test-receipt"; testReceiptPath; "--bozzetto-port"; string port; "--out"; outEmptySearch
              ]
            exitEmptySearch |> Expect.equal "empty Spec Ballast without --skip-clef-search exits 3" 3

            return Ok ()
          with ex ->
            return Error ex
        }
        do! app.StopAsync()
        do! app.DisposeAsync().AsTask()
        try
          if Directory.Exists scratchDir then Directory.Delete(scratchDir, true)
        with _ -> ()
        match runResult with
        | Ok () -> ()
        | Error ex -> raise ex
      }
    ]
  ]
