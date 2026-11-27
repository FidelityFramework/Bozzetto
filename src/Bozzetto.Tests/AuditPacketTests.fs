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
  CommandExitCode = Some 0
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
  SchemaVersion = None
  ProjectionScope = None
  LegacyProjectionSites = None
  Cvc5SpawnsByPhase = []
  Cvc5ToolIdentity = None
  TelemetryTransport = None
  TelemetryDropped = None
  TelemetryDrain = None
  TelemetryObservations = None
  TelemetryProducerOverheadBytes = None
  TelemetryProducerOverheadTicks = None
  BudgetAccepted = None
  BudgetBreaches = []
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

      testCase "canary text keeps an interrupted compiler invocation separate from native exit" <| fun _ ->
        let stopped =
          String.concat "\n" [
            "target=01_HelloWorldDirect"
            "[12:43:01.514] [FrontEnd:1] Clef → PSG (Type Checking & Semantic Graph)..."
            "[CCS phase] program-storage 6.800s"
            "WATCHDOG rootPid=3901165 pgid=3901165 elapsedMs=15164 peakRssMB=302.1 peakRssBytes=316805120 breach=wall_limit_exceeded(15.13s>=15.00s) exitCode=124 survivors=none" ]
          |> parseCanaryReceipt
          |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        stopped.FrontEndMs |> Expect.isNone "a started front end has no completion"
        stopped.ProofDischargeMs |> Expect.isNone "proof discharge was not observed"
        stopped.NativeExitCode |> Expect.isNone "the watchdog did not observe a native exit"
        stopped.CompileExitCode |> Expect.isNone "the wrapper exit is not a compiler exit"
        stopped.CommandExitCode |> Expect.equal "the wrapper exit is retained" (Some 124)
        stopped.TotalWallMs |> Expect.equal "whole command elapsed" (Some 15164.0)
        stopped.WatchdogBreach |> Expect.equal "watchdog recorded a wall breach" (Some "wall_limit_exceeded(15.13s>=15.00s)")

      testCase "canary text accepts Composer completion lines and distinct explicit exits" <| fun _ ->
        let completed =
          String.concat "\n" [
            "target=01_HelloWorldDirect"
            "[FrontEnd:1] Done (5150.125ms)"
            "[FrontEnd.ProofDischarge:5] Done (387.305ms)"
            "compileExitCode=0"
            "nativeExitCode=3"
            "WATCHDOG rootPid=101 pgid=101 elapsedMs=6400 peakRssBytes=167772160 breach=none exitCode=3 survivors=none" ]
          |> parseCanaryReceipt
          |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        completed.FrontEndMs |> Expect.equal "Composer front-end completion" (Some 5150.125)
        completed.ProofDischargeMs |> Expect.equal "Composer proof completion with an invocation-local span ID" (Some 387.305)
        completed.CompileExitCode |> Expect.equal "explicit compiler exit" (Some 0)
        completed.NativeExitCode |> Expect.equal "explicit native exit" (Some 3)

      testCase "canary text accepts legacy phase timings with spaces" <| fun _ ->
        let legacy =
          parseCanaryReceipt "  [FrontEnd:1] 5.15s\r\n  [FrontEnd.ProofDischarge:3] 387.305ms"
          |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        legacy.FrontEndMs |> Expect.equal "legacy seconds" (Some 5150.0)
        legacy.ProofDischargeMs |> Expect.equal "legacy milliseconds" (Some 387.305)

      testCase "canary text refuses a failed whole command even when the observed compiler and program exited zero" <| fun _ ->
        let wrapperFailed =
          String.concat "\n" [
            "target=01_HelloWorldDirect"
            "compileExitCode=0"
            "nativeExitCode=0"
            "WATCHDOG rootPid=101 pgid=101 elapsedMs=6400 peakRssBytes=167772160 breach=none exitCode=1 survivors=none" ]
          |> parseCanaryReceipt
          |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        wrapperFailed.CompileExitCode |> Expect.equal "the compiler was observed to exit zero" (Some 0)
        wrapperFailed.NativeExitCode |> Expect.equal "the program was observed to exit zero" (Some 0)
        wrapperFailed.WatchdogCleared |> Expect.isFalse "the whole command exited with failure"
        wrapperFailed.CommandExitCode |> Expect.equal "the command failure remains observable" (Some 1)
        let input = cleanPacketInput ()
        let candidate = { cleanCanaryReceipt "01_HelloWorldDirect" with CommandExitCode = wrapperFailed.CommandExitCode }
        let verdict = evaluateVetoes { input with CanaryCandidate = Ok(Some candidate) }
        verdict.Vetoes |> Expect.contains "nonzero command exit vetoes even in a complete receipt" (MechanicalVeto.CanaryExitBreach("command", 1))

      testCase "canary text refuses to choose between repeated observations" <| fun _ ->
        let repeated =
          String.concat "\n" [
            "[FrontEnd:1] Done (5150.125ms)"
            "[FrontEnd.ProofDischarge:5] Done (387.305ms)"
            "[FrontEnd.ProofDischarge:9] Done (1017.500ms)"
            "nativeExitCode=0"
            "nativeExitCode=139"
            "WATCHDOG rootPid=101 pgid=101 elapsedMs=6400 peakRssBytes=167772160 breach=none exitCode=0 survivors=none"
            "WATCHDOG rootPid=102 pgid=102 elapsedMs=6500 peakRssBytes=167772160 breach=none exitCode=139 survivors=none" ]
          |> parseCanaryReceipt
          |> Result.defaultWith (fun e -> failtestf "%s" (PacketParseError.describe e))
        repeated.ProofDischargeMs |> Expect.isNone "two proof completions cannot be extrapolated to one"
        repeated.NativeExitCode |> Expect.isNone "two native results are ambiguous"
        repeated.CommandExitCode |> Expect.isNone "two wrapper results are ambiguous"
        repeated.WatchdogCleared |> Expect.isFalse "two command outcomes cannot clear a single run"

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

    testList "Part 5 strict schemaVersion: 1 canary receipt contract" [

      // Canonical schemaVersion: 1 fixture aligned with the Composer #192 producer shape.
      let strictReceipt =
        """{"schemaVersion":1,"target":"01_HelloWorldDirect","compileExitCode":0,"nativeExitCode":0,"totalAllocBytes":12000000,"phaseTimingsMs":{"obligations":410.0,"witness-publication":620.0},"phaseAllocBytes":{"obligations":4000000,"witness-publication":5000000},"counters":{"platformResolutionReads":1,"numericProjectionCalls":1,"memoryProjectionCalls":1,"spatialProjectionCalls":0,"spatialExecuted":false,"hardwareModules":0,"kernelModules":0,"projectionCoverage":{"scope":"driver-path","legacySites":0},"cvc5Spawns":4,"uniqueObligations":12,"cvc5SpawnsByPhase":{"obligations":4},"cvc5ToolIdentity":"cvc5-1.2.1-sha256:abc123"},"scaling":{"scale1xAllocBytes":2000000,"scale4xAllocBytes":8000000,"scalingSlope":4.0,"scale1xWallMs":120.0,"scale4xWallMs":490.0,"scalingPasses":[{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}]},"telemetry":{"transport":"synchronous","dropped":0,"drain":"NotApplicable","observations":14,"producerOverheadBytes":2048,"producerOverheadTicks":310},"budget":{"accepted":true,"breaches":[]},"totalWallMs":3100.0,"frontEndMs":1850.0,"proofDischargeMs":240.0,"peakRssBytes":199712256,"watchdogSurvivors":0}"""

      let parseReceipt (raw: string) : Result<CanaryReceipt, PacketParseError> = parseCanaryReceipt raw

      let strictPairInput (receipt: CanaryReceipt) : BlindAuditPacketInput =
        { cleanPacketInput () with CanaryBase = Ok(Some receipt); CanaryCandidate = Ok(Some receipt) }

      testCase "positive control: canonical schemaVersion 1 receipt matching Composer producer parses completely and evaluates with no veto" <| fun _ ->
        match parseReceipt strictReceipt with
        | Error err -> failtestf "canonical schemaVersion 1 receipt must parse: %s" (PacketParseError.describe err)
        | Ok receipt ->
          receipt.SchemaVersion |> Expect.equal "schemaVersion is recorded as 1" (Some 1)
          receipt.NumericProjectionCalls |> Expect.equal "canonical numericProjectionCalls is read" (Some 1)
          receipt.MemoryProjectionCalls |> Expect.equal "canonical memoryProjectionCalls is read" (Some 1)
          receipt.Cvc5Spawns |> Expect.equal "canonical cvc5Spawns is read" (Some 4)
          receipt.Cvc5SpawnsByPhase |> Expect.equal "optional cvc5SpawnsByPhase is preserved" [ "obligations", 4 ]
          receipt.Cvc5ToolIdentity |> Expect.equal "optional cvc5ToolIdentity is preserved" (Some "cvc5-1.2.1-sha256:abc123")
          receipt.ProjectionScope |> Expect.equal "projectionCoverage.scope is read" (Some "driver-path")
          receipt.LegacyProjectionSites |> Expect.equal "projectionCoverage.legacySites is read" (Some 0)
          receipt.TelemetryTransport |> Expect.equal "telemetry.transport is read" (Some "synchronous")
          receipt.TelemetryDropped |> Expect.equal "telemetry.dropped is read as int64" (Some 0L)
          receipt.TelemetryDrain |> Expect.equal "telemetry.drain is read as string" (Some "NotApplicable")
          receipt.TelemetryObservations |> Expect.equal "optional telemetry.observations is preserved" (Some 14L)
          receipt.TelemetryProducerOverheadBytes |> Expect.equal "optional telemetry.producerOverheadBytes is preserved" (Some 2048L)
          receipt.TelemetryProducerOverheadTicks |> Expect.equal "optional telemetry.producerOverheadTicks is preserved" (Some 310L)
          receipt.BudgetAccepted |> Expect.equal "optional budget.accepted is preserved" (Some true)
          receipt.BudgetBreaches |> Expect.isEmpty "optional budget.breaches is empty"
          missingCanaryMetrics receipt |> Expect.isEmpty "a strict receipt carries every required budget metric"
          let verdict = evaluateVetoes (strictPairInput receipt)
          verdict.Completeness.IsComplete |> Expect.isTrue "strict schemaVersion 1 receipt makes the packet complete"
          verdict.Vetoes |> Expect.isEmpty "within-budget strict receipt produces no veto"

        // Minimal required-only schemaVersion 1 receipt (all optional fields omitted) must parse cleanly.
        let minimalRequiredReceipt =
          """{"schemaVersion":1,"target":"01_HelloWorldDirect","compileExitCode":0,"totalAllocBytes":12000000,"phaseTimingsMs":{"obligations":410.0},"phaseAllocBytes":{"obligations":4000000},"counters":{"platformResolutionReads":1,"numericProjectionCalls":1,"memoryProjectionCalls":1,"spatialProjectionCalls":0,"spatialExecuted":false,"hardwareModules":0,"kernelModules":0,"projectionCoverage":{"scope":"driver-path","legacySites":0},"cvc5Spawns":4,"uniqueObligations":12},"scaling":{"scale1xAllocBytes":2000000,"scale4xAllocBytes":8000000,"scalingSlope":4.0,"scalingPasses":[{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}]},"telemetry":{"transport":"synchronous","dropped":0,"drain":"NotApplicable"}}"""
        match parseReceipt minimalRequiredReceipt with
        | Error err -> failtestf "minimal required-only schemaVersion 1 receipt must parse: %s" (PacketParseError.describe err)
        | Ok receipt ->
          receipt.SchemaVersion |> Expect.equal "minimal receipt schemaVersion is 1" (Some 1)
          receipt.NativeExitCode |> Expect.isNone "omitted optional nativeExitCode is None"
          receipt.Cvc5SpawnsByPhase |> Expect.isEmpty "omitted optional cvc5SpawnsByPhase is empty"
          receipt.Cvc5ToolIdentity |> Expect.isNone "omitted optional cvc5ToolIdentity is None"
          receipt.BudgetAccepted |> Expect.isNone "omitted optional budget is None"

        // Producer parity: owner-site scope, async-two-lane transport, Drained(...) string,
        // and scalingSlope equal to max per-pass slope rather than overall ratio.
        let asyncProducerReceipt =
          strictReceipt
            .Replace("\"scope\":\"driver-path\"", "\"scope\":\"owner-site\"")
            .Replace("\"transport\":\"synchronous\",\"dropped\":0,\"drain\":\"NotApplicable\"", "\"transport\":\"async-two-lane\",\"dropped\":0,\"drain\":\"Drained(12ms)\"")
            .Replace("\"scalingSlope\":4.0", "\"scalingSlope\":5.0")
            .Replace(
              """[{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}]""",
              """[{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2500000,"slope":5.0},{"pass":"Obligations","alloc1xBytes":1500000,"alloc4xBytes":5500000,"slope":3.6667}]""")
        match parseReceipt asyncProducerReceipt with
        | Error err -> failtestf "async-two-lane owner-site receipt with max-pass scalingSlope must parse: %s" (PacketParseError.describe err)
        | Ok receipt ->
          receipt.ProjectionScope |> Expect.equal "owner-site scope accepted" (Some "owner-site")
          receipt.TelemetryTransport |> Expect.equal "async-two-lane transport accepted" (Some "async-two-lane")
          receipt.TelemetryDrain |> Expect.equal "Drained(...) string accepted" (Some "Drained(12ms)")
          (evaluateVetoes (strictPairInput receipt)).Vetoes |> Expect.isEmpty "max-pass slope <= 6.0 produces no veto"

      testCase "exact-reason negative (B1): per-pass and overall scaling slopes are computed from bytes and mismatch or zero-baseline growth is refused" <| fun _ ->
        let lyingPass =
          strictReceipt.Replace(
            """{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}""",
            """{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":50000000,"slope":4.0}""")
        match parseReceipt lyingPass with
        | Ok receipt -> failtestf "forged per-pass slope 4.0 for 100x allocation growth must be refused, got %A" receipt.ScalingPasses
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names slope" "slope"
          detail |> Expect.stringContains "exact reason reports computed ratio" "100.0000"
        | Error err -> failtestf "expected slope disagreement error, got %s" (PacketParseError.describe err)

        let zeroBaselinePass =
          strictReceipt.Replace(
            """{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}""",
            """{"pass":"WitnessSegmentation","alloc1xBytes":0,"alloc4xBytes":100,"slope":0.0}""")
        match parseReceipt zeroBaselinePass with
        | Ok receipt -> failtestf "zero-baseline growth (0 -> 100) with slope 0.0 must be refused, got %A" receipt.ScalingPasses
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "zero-baseline refusal names slope" "slope"
          detail |> Expect.stringContains "exact reason reports +Infinity" "+Infinity"
        | Error err -> failtestf "expected zero-baseline slope error, got %s" (PacketParseError.describe err)

        let lyingRootSlope = strictReceipt.Replace("\"scalingSlope\":4.0", "\"scalingSlope\":1.5")
        match parseReceipt lyingRootSlope with
        | Ok _ -> failtest "declared scalingSlope disagreeing with both overall ratio and max pass slope must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "root slope refusal names scalingSlope" "scalingSlope"
          detail |> Expect.stringContains "root slope refusal states disagreement" "disagrees"
        | Error err -> failtestf "expected scalingSlope disagreement error, got %s" (PacketParseError.describe err)

        let zeroBaselineRoot =
          strictReceipt
            .Replace("\"scale1xAllocBytes\":2000000,\"scale4xAllocBytes\":8000000,\"scalingSlope\":4.0",
                     "\"scale1xAllocBytes\":0,\"scale4xAllocBytes\":100,\"scalingSlope\":0.0")
        match parseReceipt zeroBaselineRoot with
        | Ok _ -> failtest "zero-baseline overall growth (0 -> 100) with scalingSlope 0.0 must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "zero-baseline overall refusal names scalingSlope" "scalingSlope"
          detail |> Expect.stringContains "exact reason reports +Infinity" "+Infinity"
        | Error err -> failtestf "expected zero-baseline overall error, got %s" (PacketParseError.describe err)

        // Consistent high per-pass slope (> 6.0) parses cleanly and triggers per-pass veto.
        let highPass =
          strictReceipt
            .Replace("\"scalingSlope\":4.0", "\"scalingSlope\":8.0")
            .Replace(
              """{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}""",
              """{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":4000000,"slope":8.0}""")
        match parseReceipt highPass with
        | Error err -> failtestf "consistent 8.0x per-pass receipt must parse for diagnosis: %s" (PacketParseError.describe err)
        | Ok receipt ->
          (evaluateVetoes (strictPairInput receipt)).Vetoes
          |> Expect.contains "per-pass 8.0x allocation ratio trips ScalingSlopeExceeded"
            (MechanicalVeto.ScalingSlopeExceeded("WitnessSegmentation", 8.0, 6.0))

        // Consistent high overall slope (> 6.0) parses cleanly and triggers overall veto.
        let slopeOver =
          strictReceipt
            .Replace("\"scale4xAllocBytes\":8000000", "\"scale4xAllocBytes\":14000000")
            .Replace("\"scalingSlope\":4.0", "\"scalingSlope\":7.0")
        match parseReceipt slopeOver with
        | Error err -> failtestf "consistent 7.0x overall receipt must parse: %s" (PacketParseError.describe err)
        | Ok receipt ->
          (evaluateVetoes (strictPairInput receipt)).Vetoes
          |> Expect.contains "overall scaling ratio 7.0 > 6.0 vetoes"
            (MechanicalVeto.ScalingSlopeExceeded("Scale_4x/Scale_1x", 7.0, 6.0))

      testCase "exact-reason negative (B2): duplicate JSON keys are refused across root, counters, scaling, passes, phase maps, telemetry, and budget" <| fun _ ->
        let dupCounter =
          strictReceipt.Replace("\"numericProjectionCalls\":1", "\"numericProjectionCalls\":3,\"numericProjectionCalls\":1")
        match parseReceipt dupCounter with
        | Ok receipt -> failtestf "duplicate numericProjectionCalls must be refused, got %A" receipt.NumericProjectionCalls
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated counter key" "numericProjectionCalls"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate-key refusal, got %s" (PacketParseError.describe err)

        let dupSchemaVersion =
          strictReceipt.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":0")
        match parseReceipt dupSchemaVersion with
        | Ok receipt -> failtestf "duplicate schemaVersion must never downgrade to legacy, got %A" receipt.SchemaVersion
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated schemaVersion" "schemaVersion"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate schemaVersion refusal, got %s" (PacketParseError.describe err)

        let dupPhaseAlloc =
          strictReceipt.Replace("\"obligations\":4000000", "\"obligations\":9000000,\"obligations\":4000000")
        match parseReceipt dupPhaseAlloc with
        | Ok _ -> failtest "duplicate key in phaseAllocBytes must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated phase key" "obligations"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate phaseAllocBytes key refusal, got %s" (PacketParseError.describe err)

        let dupPassKey =
          strictReceipt.Replace("\"slope\":4.0}", "\"slope\":100.0,\"slope\":4.0}")
        match parseReceipt dupPassKey with
        | Ok _ -> failtest "duplicate key inside scalingPasses entry must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated pass key" "slope"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate scaling pass key refusal, got %s" (PacketParseError.describe err)

        let dupTelemetryKey =
          strictReceipt.Replace("\"dropped\":0", "\"dropped\":5,\"dropped\":0")
        match parseReceipt dupTelemetryKey with
        | Ok _ -> failtest "duplicate key in telemetry block must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated telemetry key" "dropped"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate telemetry key refusal, got %s" (PacketParseError.describe err)

        let dupBudgetKey =
          strictReceipt.Replace("\"accepted\":true", "\"accepted\":false,\"accepted\":true")
        match parseReceipt dupBudgetKey with
        | Ok _ -> failtest "duplicate key in budget block must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated budget key" "accepted"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate budget key refusal, got %s" (PacketParseError.describe err)

        let dupCoverageKey =
          strictReceipt.Replace("\"legacySites\":0", "\"legacySites\":3,\"legacySites\":0")
        match parseReceipt dupCoverageKey with
        | Ok _ -> failtest "duplicate key in projectionCoverage must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated coverage key" "legacySites"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate coverage key refusal, got %s" (PacketParseError.describe err)

        let dupSpawnsByPhase =
          strictReceipt.Replace("\"cvc5SpawnsByPhase\":{\"obligations\":4}", "\"cvc5SpawnsByPhase\":{\"obligations\":9,\"obligations\":4}")
        match parseReceipt dupSpawnsByPhase with
        | Ok _ -> failtest "duplicate key in cvc5SpawnsByPhase must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated cvc5SpawnsByPhase key" "obligations"
          detail |> Expect.stringContains "exact reason names duplicate key" "duplicate JSON key"
        | Error err -> failtestf "expected duplicate cvc5SpawnsByPhase key refusal, got %s" (PacketParseError.describe err)

        let dupPassName =
          strictReceipt.Replace(
            """[{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}]""",
            """[{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0},{"pass":"WitnessSegmentation","alloc1xBytes":500000,"alloc4xBytes":2000000,"slope":4.0}]""")
        match parseReceipt dupPassName with
        | Ok _ -> failtest "duplicate scaling pass name must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names duplicated pass" "pass"
          detail |> Expect.stringContains "exact reason names duplicate scaling pass" "duplicate scaling pass"
        | Error err -> failtestf "expected duplicate pass name refusal, got %s" (PacketParseError.describe err)

      testCase "exact-reason negative (B3): invalid schemaVersion, unversioned v1 blocks, and real legacySites > 0 veto preserving diagnosis" <| fun _ ->
        for label, replacement, expectedField in [
          "explicit schemaVersion 0", "\"schemaVersion\":0", "schemaVersion"
          "unsupported schemaVersion 2", "\"schemaVersion\":2", "schemaVersion"
          "negative schemaVersion -1", "\"schemaVersion\":-1", "schemaVersion"
          "string schemaVersion", "\"schemaVersion\":\"1\"", "schemaVersion"
          "fractional schemaVersion", "\"schemaVersion\":1.0", "schemaVersion"
          "null schemaVersion", "\"schemaVersion\":null", "schemaVersion"
          "string legacy alias schema_version", "\"schema_version\":\"1\"", "schema_version"
          "int legacy alias schema_version", "\"schema_version\":1", "schema_version"
          "zero legacy alias schema_version", "\"schema_version\":0", "schema_version"
        ] do
          let mutated = strictReceipt.Replace("\"schemaVersion\":1", replacement)
          match parseReceipt mutated with
          | Ok receipt -> failtestf "%s must be refused, got SchemaVersion=%A" label receipt.SchemaVersion
          | Error(PacketParseError.InvalidJsonField(_, field, _)) ->
            field |> Expect.equal (sprintf "%s refusal names %s" label expectedField) expectedField
          | Error err -> failtestf "%s expected InvalidJsonField, got %s" label (PacketParseError.describe err)

        // Stripping schemaVersion from a receipt carrying v1-only blocks must fail closed.
        let unversionedWithTelemetry =
          """{"target":"01_HelloWorldDirect","telemetry":{"transport":"synchronous","dropped":0,"drain":"NotApplicable"}}"""
        match parseReceipt unversionedWithTelemetry with
        | Ok _ -> failtest "unversioned receipt carrying telemetry must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names telemetry" "telemetry"
          detail |> Expect.stringContains "exact reason requires schemaVersion 1" "schemaVersion 1"
        | Error err -> failtestf "expected telemetry refusal on unversioned receipt, got %s" (PacketParseError.describe err)

        let unversionedWithCoverage =
          """{"target":"01_HelloWorldDirect","counters":{"numericProjectionCalls":1,"projectionCoverage":{"scope":"driver-path","legacySites":3}}}"""
        match parseReceipt unversionedWithCoverage with
        | Ok _ -> failtest "unversioned receipt carrying projectionCoverage must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
          field |> Expect.equal "refusal names projectionCoverage" "projectionCoverage"
          detail |> Expect.stringContains "exact reason requires schemaVersion 1" "schemaVersion 1"
        | Error err -> failtestf "expected projectionCoverage refusal on unversioned receipt, got %s" (PacketParseError.describe err)

        // Strict schemaVersion 1 receipt with legacySites = 2 parses cleanly so diagnosis
        // data is preserved AND AuditPacketEval fires MechanicalVeto.ProjectionCoverageLegacySites.
        let legacySitesTwo = strictReceipt.Replace("\"legacySites\":0", "\"legacySites\":2")
        match parseReceipt legacySitesTwo with
        | Error err -> failtestf "schemaVersion 1 receipt with legacySites=2 must parse for diagnosis: %s" (PacketParseError.describe err)
        | Ok receipt ->
          receipt.LegacyProjectionSites |> Expect.equal "parsed LegacyProjectionSites is Some 2" (Some 2)
          receipt.NumericProjectionCalls |> Expect.equal "diagnostic counters remain intact" (Some 1)
          receipt.TotalAllocBytes |> Expect.equal "diagnostic allocations remain intact" (Some 12_000_000L)
          let verdict = evaluateVetoes (strictPairInput receipt)
          verdict.Completeness.IsComplete |> Expect.isTrue "diagnostic packet remains complete"
          verdict.Vetoes
          |> Expect.contains "real parsed legacySites = 2 trips ProjectionCoverageLegacySites veto"
            (MechanicalVeto.ProjectionCoverageLegacySites 2)
          let rendered = renderPacket verdict
          rendered |> Expect.stringContains "rendered packet states legacySites veto" "projectionCoverage.legacySites = 2"
          rendered |> Expect.stringContains "rendered packet preserves Part 5 diagnostics" "WitnessSegmentation"

        // Negative legacySites is malformed and refused at parse time.
        let negLegacySites = strictReceipt.Replace("\"legacySites\":0", "\"legacySites\":-1")
        match parseReceipt negLegacySites with
        | Ok _ -> failtest "negative legacySites must be refused"
        | Error(PacketParseError.InvalidJsonField(_, field, _)) ->
          field |> Expect.equal "negative legacySites refusal names legacySites" "legacySites"
        | Error err -> failtestf "expected InvalidJsonField for negative legacySites, got %s" (PacketParseError.describe err)

        // Real parsed schemaVersion 1 receipt with numericProjectionCalls = 2 trips ProjectionCountExceeded.
        let projOverReceipt = strictReceipt.Replace("\"numericProjectionCalls\":1", "\"numericProjectionCalls\":2")
        match parseReceipt projOverReceipt with
        | Error err -> failtestf "projection-count-over strict receipt must parse for diagnosis: %s" (PacketParseError.describe err)
        | Ok receipt ->
          (evaluateVetoes (strictPairInput receipt)).Vetoes
          |> Expect.contains "real parsed numericProjectionCalls = 2 vetoes"
            (MechanicalVeto.ProjectionCountExceeded("NumericPublication.project", 2))

      testCase "exact-reason negative: missing required fields, legacy aliases, unknown keys, and invalid scope/transport/drain are refused" <| fun _ ->
        // Missing required counters and top-level containers
        for label, snippet, expectedField in [
          "missing cvc5Spawns", "\"cvc5Spawns\":4,", "cvc5Spawns"
          "missing numericProjectionCalls", "\"numericProjectionCalls\":1,", "numericProjectionCalls"
          "missing hardwareModules", "\"hardwareModules\":0,", "hardwareModules"
          "missing kernelModules", "\"kernelModules\":0,", "kernelModules"
          "missing spatialExecuted", "\"spatialExecuted\":false,", "spatialExecuted"
          "missing uniqueObligations", "\"uniqueObligations\":12,", "uniqueObligations"
          "missing projectionCoverage", "\"projectionCoverage\":{\"scope\":\"driver-path\",\"legacySites\":0},", "projectionCoverage"
          "missing telemetry", "\"telemetry\":{\"transport\":\"synchronous\",\"dropped\":0,\"drain\":\"NotApplicable\",\"observations\":14,\"producerOverheadBytes\":2048,\"producerOverheadTicks\":310},", "telemetry"
          "missing scaling", "\"scaling\":{\"scale1xAllocBytes\":2000000,\"scale4xAllocBytes\":8000000,\"scalingSlope\":4.0,\"scale1xWallMs\":120.0,\"scale4xWallMs\":490.0,\"scalingPasses\":[{\"pass\":\"WitnessSegmentation\",\"alloc1xBytes\":500000,\"alloc4xBytes\":2000000,\"slope\":4.0}]},", "scaling"
        ] do
          let mutated = strictReceipt.Replace(snippet, "")
          match parseReceipt mutated with
          | Ok _ -> failtestf "%s must be refused" label
          | Error(PacketParseError.MissingJsonField(_, field)) ->
            field |> Expect.equal (sprintf "%s refusal names %s" label expectedField) expectedField
          | Error err -> failtestf "%s expected MissingJsonField '%s', got %s" label expectedField (PacketParseError.describe err)

        // Legacy aliases must report InvalidJsonField naming the alias and canonical key (checked BEFORE missing-field!)
        for label, oldText, newText, expectedAlias, expectedCanonical in [
          "counter alias numeric_projection_calls", "\"numericProjectionCalls\":1", "\"numeric_projection_calls\":1", "numeric_projection_calls", "numericProjectionCalls"
          "container alias projections", "\"counters\":{", "\"projections\":{", "projections", "counters"
          "bare exitCode alias", "\"nativeExitCode\":0", "\"exitCode\":0", "exitCode", "nativeExitCode"
          "scaling pass alias passName", "\"pass\":\"WitnessSegmentation\"", "\"passName\":\"WitnessSegmentation\"", "passName", "pass"
        ] do
          let mutated = strictReceipt.Replace(oldText, newText)
          match parseReceipt mutated with
          | Ok _ -> failtestf "%s must be refused" label
          | Error(PacketParseError.InvalidJsonField(_, field, detail)) ->
            field |> Expect.equal (sprintf "%s refusal names alias %s" label expectedAlias) expectedAlias
            detail |> Expect.stringContains (sprintf "%s exact reason names canonical %s" label expectedCanonical) expectedCanonical
          | Error err -> failtestf "%s expected alias InvalidJsonField, got %s" label (PacketParseError.describe err)

        // Undefined keys & flat root counters
        let unknownKey = strictReceipt.Replace("\"cvc5Spawns\":4", "\"cvc5Spawns\":4,\"cvc5_launches\":4")
        match parseReceipt unknownKey with
        | Error(PacketParseError.InvalidJsonField(_, field, _)) -> field |> Expect.equal "names undefined key" "cvc5_launches"
        | other -> failtestf "expected undefined key refusal, got %A" other

        let flatRootCounter = strictReceipt.Replace("\"target\":\"01_HelloWorldDirect\"", "\"target\":\"01_HelloWorldDirect\",\"numericProjectionCalls\":1")
        match parseReceipt flatRootCounter with
        | Error(PacketParseError.InvalidJsonField(_, field, _)) -> field |> Expect.equal "names flat root counter" "numericProjectionCalls"
        | other -> failtestf "expected flat root counter refusal, got %A" other

        // Domain checks on scope, transport, and string drain
        for label, oldText, newText, expectedField in [
          "unrecognized projectionCoverage.scope", "\"scope\":\"driver-path\"", "\"scope\":\"ad-hoc\"", "scope"
          "blank projectionCoverage.scope", "\"scope\":\"driver-path\"", "\"scope\":\"   \"", "scope"
          "unrecognized telemetry.transport", "\"transport\":\"synchronous\"", "\"transport\":\"stderr\"", "transport"
          "integer telemetry.drain", "\"drain\":\"NotApplicable\"", "\"drain\":0", "drain"
        ] do
          let mutated = strictReceipt.Replace(oldText, newText)
          match parseReceipt mutated with
          | Ok _ -> failtestf "%s must be refused" label
          | Error(PacketParseError.InvalidJsonField(_, field, _)) ->
            field |> Expect.equal (sprintf "%s refusal names %s" label expectedField) expectedField
          | Error err -> failtestf "%s expected InvalidJsonField, got %s" label (PacketParseError.describe err)

      testCase "positive legacy compatibility control: unversioned legacy receipts without v1 blocks keep tolerant aliases" <| fun _ ->
        let legacyAliasReceipt =
          """{"target":"01_HelloWorldDirect","frontEndMs":1900,"proofDischargeMs":200,"totalWallMs":3100,"peakRssMb":180,"nativeExitCode":0,"watchdogSurvivors":0,"gcAllocatedBytes":9000000,"phaseTimingsMs":{"obligations":410.0},"phaseAllocBytes":{"obligations":4000000},"counters":{"numeric_projection_calls":1,"cvc5Spawns":4},"scaling":{"scale1x":{"allocBytes":1000000,"wallMs":120.0},"scale4x":{"allocBytes":4050000,"wallMs":490.0},"passes":[{"pass":"WitnessSegmentation","alloc1x":500000,"alloc4x":2050000}]}}"""
        match parseReceipt legacyAliasReceipt with
        | Error err -> failtestf "unversioned legacy receipt must still parse: %s" (PacketParseError.describe err)
        | Ok receipt ->
          receipt.SchemaVersion |> Expect.equal "legacy receipt has no schema version" None
          receipt.NumericProjectionCalls |> Expect.equal "legacy alias spelling is still honoured" (Some 1)
          receipt.LegacyProjectionSites |> Expect.isNone "legacy receipt has no projectionCoverage"

      testCase "fault-injection negative control: evaluator enforces computed per-pass ratios even if a faulty parser stores a deceptive slope" <| fun _ ->
        let baseStrict =
          parseReceipt strictReceipt
          |> Result.defaultWith (fun e -> failtestf "base strict receipt must parse: %s" (PacketParseError.describe e))
        let deceptivePass =
          { baseStrict with
              ScalingPasses = [ { PassName = "WitnessSegmentation"; Alloc1xBytes = 500_000L; Alloc4xBytes = 50_000_000L; Slope = 4.0 } ] }
        let deceptiveVerdict = evaluateVetoes (strictPairInput deceptivePass)
        deceptiveVerdict.Vetoes
        |> Expect.contains "evaluator computes 100.0 ratio from bytes rather than trusting deceptive Slope = 4.0"
          (MechanicalVeto.ScalingSlopeExceeded("WitnessSegmentation", 100.0, 6.0))

        let zeroDenomDeceptive =
          { baseStrict with
              ScalingPasses = [ { PassName = "ZeroBaselinePass"; Alloc1xBytes = 0L; Alloc4xBytes = 100L; Slope = 0.0 } ] }
        let zeroVerdict = evaluateVetoes (strictPairInput zeroDenomDeceptive)
        zeroVerdict.Vetoes
        |> List.exists (function MechanicalVeto.ScalingSlopeExceeded("ZeroBaselinePass", s, 6.0) -> Double.IsPositiveInfinity s | _ -> false)
        |> Expect.isTrue "evaluator treats 0 -> 100 allocation growth as +Infinity regardless of deceptive Slope = 0.0"
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
