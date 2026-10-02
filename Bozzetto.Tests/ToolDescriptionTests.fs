module Bozzetto.Tests.ToolDescriptionTests

open Expecto
open Expecto.Flip
open VerifyExpecto
open VerifyTests
open System
open System.IO
open System.ComponentModel
open System.Reflection
open Bozzetto.Server.McpTools

let verifyText name (value: string) =
  Bozzetto.Tests.TestInfrastructure.Snapshots.verify "ToolDescriptionTests" name "txt" value

/// Extract all [<Description>] attributes from MCP tool methods
let toolDescriptions =
  typeof<BozzettoTools>.GetMethods(BindingFlags.Instance ||| BindingFlags.Public)
  |> Array.choose (fun m ->
    m.GetCustomAttribute<DescriptionAttribute>()
    |> Option.ofObj
    |> Option.map (fun attr -> m.Name, attr.Description))
  |> Array.toList

let registeredToolDescriptions =
  typeof<BozzettoTools>.GetMethods(BindingFlags.Instance ||| BindingFlags.Public)
  |> Array.filter (fun m ->
    m.GetCustomAttributes(true)
    |> Array.exists (fun attr -> attr.GetType().Name = "McpServerToolAttribute"))
  |> Array.choose (fun m ->
    m.GetCustomAttribute<DescriptionAttribute>()
    |> Option.ofObj
    |> Option.map (fun attr -> m.Name, attr.Description))
  |> Array.toList

/// Registered MCP tool methods (those carrying [<McpServerTool>])
let registeredToolMethods =
  typeof<BozzettoTools>.GetMethods(BindingFlags.Instance ||| BindingFlags.Public)
  |> Array.filter (fun m ->
    m.GetCustomAttributes(true)
    |> Array.exists (fun attr -> attr.GetType().Name = "McpServerToolAttribute"))

/// Public BozzettoTools members that carry a [<Description>] (i.e. are written
/// to look like MCP tools). Every one of them must also carry
/// [<McpServerTool>] — otherwise it is a write-only member that no agent can
/// ever call (roast-2 item 7: ~17 feature tools were unreachable because they
/// had a Description but no registration attribute).
let describedToolMethods =
  typeof<BozzettoTools>.GetMethods(BindingFlags.Instance ||| BindingFlags.Public)
  |> Array.filter (fun m -> m.GetCustomAttribute<DescriptionAttribute>() |> Option.ofObj |> Option.isSome)

/// WHY — MCP SDK reflection marks every parameter WITHOUT a default value as
/// REQUIRED in the tool schema. A parameter whose handler tolerates absence but
/// whose signature lacks a default makes the schema lie to agents: omitting it
/// throws ArgumentException inside the marshaller instead of reaching the handler
/// (observed 2026-08: send_fsharp_code without block_start_line crashed the tool call).
/// Because — every parameter must either be genuinely required or carry a default,
/// so the reflected schema matches what handlers actually accept.
let requiredParamsByTool =
  [ "send_fsharp_code", set ["agentName"; "code"]
    "check_fsharp_code", set ["code"]
    "create_project_session", set ["project"; "working_directory"]
    "create_solution_session", set ["solution"; "working_directory"]
    "create_bare_session", set ["working_directory"]
    "release_work_lease", set ["lease_id"]
    "stop_session", set ["session_id"]
    "switch_session", set ["session_id"]
    "switch_workflow", set ["target"]
    "targeted_verify", set ["behavior"]
    "report_friction", set ["tool_name"; "feedback_kind"; "short_reason"]
    "explain_test_failure", set ["test_name"]
    "decompose_pipeline", set ["code"]
    "plan_ripple", set ["changed_cells"]
    "preview_what_if", set ["binding_name"; "new_code"]
    "manage_scratch_pad", set ["action"]
    "suggest_repair", set ["test_name"]
    // Claims v1 cohort tools (cohort-integration-plan.md Slice 2) — every
    // arg is required (v1 has no optional working_directory/session_id
    // routing; cohort membership is daemon-scoped, not session-scoped).
    "join_cohort", set ["agentName"; "role"]
    "leave_cohort", set ["agentName"]
    "acquire_claim", set ["agentName"; "scope"; "purpose"]
    "release_claim", set ["agentName"; "claimId"; "fence"]
    "reassign_claim", set ["agentName"; "claimId"; "toMember"]
    "request_landing", set ["agentName"; "claims"; "commits"; "statement"]
    // Item 14c: both args required — v1 has no optional routing here either.
    "set_integration_ref", set ["agentName"; "integrationRef"] ]
  |> Map.ofList

[<Tests>]
let toolSchemaHonestyTests =
  testList "MCP tool schema honesty" [

    testCase "WHY — registered MCP tools — every non-defaulted parameter is intentionally required because reflection turns missing defaults into hard schema requirements that crash agent calls"
    <| fun _ ->
      let failures =
        registeredToolMethods
        |> Array.collect (fun m ->
          let expected =
            requiredParamsByTool
            |> Map.tryFind m.Name
            |> Option.defaultWith (fun _ -> Set.empty)
          let actualRequired =
            m.GetParameters()
            |> Array.filter (fun p -> not p.HasDefaultValue)
            |> Array.map (fun p -> p.Name)
            |> set
          if actualRequired = expected then [||]
          else [| sprintf "%s: schema-required [%s] but design-required [%s]"
                    m.Name
                    (actualRequired |> Set.toList |> String.concat ", ")
                    (expected |> Set.toList |> String.concat ", ") |])
      failures
      |> Array.toList
      |> Expect.equal "every registered tool's required-parameter set must match its design" []

    testCase "WHY — send_fsharp_code — optional args (eval_mode, block_start_line, intent, working_directory, file_path) are omitted by well-behaved agents because descriptions say so, so their absence must not throw"
    <| fun _ ->
      let m = registeredToolMethods |> Array.find (fun m -> m.Name = "send_fsharp_code")
      for p in m.GetParameters() do
        if p.Name <> "agentName" && p.Name <> "code" then
          p.HasDefaultValue
          |> Expect.isTrue (sprintf "parameter '%s' should have a default value" p.Name)
  ]

[<Tests>]
let descriptionSnapshotTests =
  testSequenced <| testList "Tool description snapshots" [

    testTask "send_fsharp_code description" {
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "send_fsharp_code")
        |> snd
      do! verifyText "send_fsharp_code_description" desc
    }

    testTask "load_fsharp_script description" {
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "load_fsharp_script")
        |> snd
      do! verifyText "load_fsharp_script_description" desc
    }

    testTask "get_daemon_status description" {
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "get_daemon_status")
        |> snd
      do! verifyText "get_daemon_status_description" desc
    }

    testTask "get_session_status description" {
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "get_session_status")
        |> snd
      do! verifyText "get_session_status_description" desc
    }
  ]

[<Tests>]
let descriptionPropertyTests =
  testList "Tool description properties" [

    testCase "all MCP tools have substantive descriptions (>= 30 chars)"
    <| fun _ ->
      for (name, desc) in toolDescriptions do
        Expect.isTrue
          (sprintf "Tool '%s' description should be >= 30 chars but was %d" name desc.Length)
          (desc.Length >= 30)

    testCase "send_fsharp_code description teaches incremental usage"
    <| fun _ ->
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "send_fsharp_code")
        |> snd
      desc
      |> Expect.stringContains
        "Should mention ;; as statement separator"
        ";;"

    testCase "send_fsharp_code description warns about large blocks"
    <| fun _ ->
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "send_fsharp_code")
        |> snd
      let mentionsIncremental =
        desc.Contains("incremental", StringComparison.OrdinalIgnoreCase)
        || desc.Contains("small", StringComparison.OrdinalIgnoreCase)
      mentionsIncremental
      |> Expect.isTrue
        "Should teach agents to submit small/incremental blocks"

    testCase "send_fsharp_code description explains error recovery"
    <| fun _ ->
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "send_fsharp_code")
        |> snd
      let mentionsRecovery =
        desc.Contains("previous", StringComparison.OrdinalIgnoreCase)
        || desc.Contains("session", StringComparison.OrdinalIgnoreCase)
      mentionsRecovery
      |> Expect.isTrue
        "Should explain that errors don't corrupt session state"

    testCase "get_session_status description stays focused on session readiness"
    <| fun _ ->
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "get_session_status")
        |> snd
      desc.Contains("get_live_test_status", StringComparison.OrdinalIgnoreCase)
      |> Expect.isFalse "get_session_status should not redirect MCP agents into live-testing tooling"

    testCase "test-suite lease guidance names unfiltered compiled acceptance without the retired session runner"
    <| fun _ ->
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "acquire_test_suite_lease")
        |> snd
      desc |> Expect.stringContains "the caller runs the compiled tests" "built Bozzetto.Tests DLL"
      desc |> Expect.stringContains "the suite must run unfiltered" "unfiltered"
      desc |> Expect.stringContains "the suite reports its own trust result" "--summary"
      desc |> Expect.stringContains "only complete passing coverage is accepted" "TRUST verdict=Trusted"
      desc |> Expect.stringContains "the granted lease must be released" "release_work_lease"
      desc.Contains "run_project_tests"
      |> Expect.isFalse "the lease tool must not redirect acceptance to the retired session runner"

    testCase "snippet check and soft reset guidance preserve the retired provider boundary rather than redirecting to a session rebuild"
    <| fun _ ->
      for tool in [ "check_fsharp_code"; "reset_fsi_session" ] do
        let desc =
          toolDescriptions
          |> List.find (fun (name, _) -> name = tool)
          |> snd
        desc |> Expect.stringContains "the compatibility operation identifies provider retirement" "Embedded production FSI hosting is retired"
        desc |> Expect.stringContains "project validation uses a caller-owned build" "dotnet build"
        desc |> Expect.stringContains "project validation acquires its work lease" "acquire_full_build_lease"
        desc |> Expect.stringContains "project validation releases its work lease" "release_work_lease"
        desc.Contains "hard_reset_fsi_session"
        |> Expect.isFalse "compatibility guidance must not redirect validation to a retired session rebuild"

    testCase "targeted_verify description teaches trust-first workflow"
    <| fun _ ->
      let desc =
        toolDescriptions
        |> List.find (fun (name, _) -> name = "targeted_verify")
        |> snd
      desc |> Expect.stringContains "should mention snippet-first trust" "local snippet-first proof"

    testCase "reduced MCP surface keeps the tool count surgical"
    <| fun _ ->
      // Exact current surface: retired patching and managed app tools are absent.
      registeredToolDescriptions.Length |> Expect.equal "tool count should stay intentionally small" 53

    testCase "every tool-shaped member is registered — no write-only MCP surface"
    <| fun _ ->
      // A [<Description>] advertises a callable MCP tool to agents. If the
      // member lacks [<McpServerTool>], `.WithTools<BozzettoTools>()` never
      // reflects it and the tool is unreachable dead surface (roast-2 item 7:
      // ~17 feature tools carried only [<Description>] and were never
      // callable). Registration must be structural: any future tool-shaped
      // member without the attribute fails this test.
      // The only tolerated exceptions are the legacy members deliberately
      // retired from the MCP surface in 3e73861 ("reduce agent tool surface")
      // — they keep their Description but must never grow back a registration.
      let deliberatelyRetired =
        set [
          "load_fsharp_script"
          "get_startup_info"
          "get_completions"
          "explore_namespace"
          "explore_type"
          "visualize_domain_model"
          "switch_workflow"
          "get_elm_state"
          "explain_test_run"
          "query_test_coverage"
          "get_file_coverage"
        ]
      let registered =
        registeredToolMethods
        |> Array.map (fun m -> m.Name)
        |> Set.ofArray
      let unregistered =
        describedToolMethods
        |> Array.filter (fun m ->
          Set.contains m.Name registered |> not
          && Set.contains m.Name deliberatelyRetired |> not)
        |> Array.map (fun m -> m.Name)
      unregistered
      |> Array.toList
      |> Expect.equal
        "every [<Description>]-attributed BozzettoTools member must carry [<McpServerTool>] or be a known-retired legacy member" []
  ]
