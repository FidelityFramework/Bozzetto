module Bozzetto.Tests.McpWireProtocolTests

/// Tests that pin the MCP wire protocol surface: tool registration
/// completeness, SseEvent JSON shapes, and SSE frame format.
/// Build wire protocol tests next to PluginContractTests (per expert consensus).

open System
open System.Reflection
open Fidelity.Data.JSON
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.WarmUp
open Bozzetto.Server

// ─── Tool Registration ────────────────────────────────────────────

let private findMcpToolMethods () =
  let bozzettoAsm =
    AppDomain.CurrentDomain.GetAssemblies()
    |> Array.find (fun a -> a.GetName().Name = "Bozzetto")
  let mcpAttrType =
    bozzettoAsm.GetReferencedAssemblies()
    |> Array.collect (fun an ->
      try
        let a = Assembly.Load(an)
        a.GetTypes()
        |> Array.filter (fun t -> t.Name = "McpServerToolAttribute")
      with _ -> [||])
    |> Array.head
  bozzettoAsm.GetTypes()
  |> Array.collect (fun t ->
    t.GetMethods(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly))
  |> Array.filter (fun m -> m.GetCustomAttributes(mcpAttrType, false).Length > 0)

[<Tests>]
let mcpToolRegistrationTests = testList "MCP tool registration" [
  test "all expected tools are registered via [<McpServerTool>]" {
    let expected = set [
      "acquire_claim"
      "acquire_full_build_lease"
      "acquire_run_app_lease"
      "acquire_test_suite_lease"
      "await_retrieval_fresh"
      "cancel_eval"
      "check_fsharp_code"
      "composer_build"
      "composer_cancel"
      "composer_close_session"
      "composer_format_preview"
      "composer_list_sessions"
      "composer_open_project"
      "composer_reserve_edit"
      "composer_retire_worker"
      "composer_run_current"
      "composer_session_status"
      "coverage_intel"
      "create_bare_session"
      "create_project_session"
      "create_solution_session"
      "decompose_pipeline"
      "diagnose"
      "discover_features"
      "explain_test_failure"
      "export_notebook"
      "export_session_transcript"
      "get_agent_work"
      "get_available_projects"
      "get_cell_dependencies"
      "get_cohort_status"
      "get_daemon_status"
      "get_eval_diff"
      "get_eval_timeline"
      "get_friction_report"
      "get_friction_summary"
      "get_message_journal"
      "get_recent_fsi_events"
      "get_session_filmstrip"
      "get_session_status"
      "hard_reset_fsi_session"
      "impact_forecast"
      "join_cohort"
      "leave_cohort"
      "list_sessions"
      "list_tests"
      "manage_local_data"
      "manage_scratch_pad"
      "plan_ripple"
      "preview_what_if"
      "reassign_claim"
      "reclaim_claim"
      "release_claim"
      "report_agent_work"
      "report_friction"
      "request_landing"
      "release_work_lease"
      "reset_fsi_session"
      "send_fsharp_code"
      "set_integration_ref"
      "stop_session"
      "suggest_next_action"
      "suggest_next_cell"
      "suggest_repair"
      "switch_session"
      "switch_workflow"
      "targeted_verify"
    ]
    let actual =
      findMcpToolMethods ()
      |> Array.map (fun m -> m.Name)
      |> set
    actual |> Expect.equal "registered tools match expected set" expected
  }

  test "no tool has an empty description" {
    let descAttrType = typeof<System.ComponentModel.DescriptionAttribute>
    let methods = findMcpToolMethods ()
    for m in methods do
      let desc =
        m.GetCustomAttributes(descAttrType, false)
        |> Array.tryHead
        |> Option.map (fun a -> (a :?> System.ComponentModel.DescriptionAttribute).Description)
      match desc with
      | Some d when String.IsNullOrWhiteSpace(d) ->
        failwithf "Tool %s has empty description" m.Name
      | None ->
        failwithf "Tool %s has no [<Description>] attribute" m.Name
      | _ -> ()
  }
]

// ─── SessionEvent Serialization ───────────────────────────────────

let private getProp (name: string) (v: JsonValue) =
  match JsonValue.prop name v with
  | Some p -> p
  | None -> failtestf "missing property '%s' in %s" name (Json.serialize v)

let private getStr (name: string) (v: JsonValue) =
  match getProp name v |> JsonValue.asString with
  | Some s -> s
  | None -> failtestf "property '%s' is not a string" name

let private getInt (name: string) (v: JsonValue) =
  match getProp name v |> JsonValue.asInt with
  | Some n -> int n
  | None -> failtestf "property '%s' is not an int" name

let private getInt64 (name: string) (v: JsonValue) =
  match getProp name v |> JsonValue.asInt with
  | Some n -> n
  | None -> failtestf "property '%s' is not an int64" name

let private getBool (name: string) (v: JsonValue) =
  match getProp name v |> JsonValue.asBool with
  | Some b -> b
  | None -> failtestf "property '%s' is not a bool" name

let private getArr (name: string) (v: JsonValue) =
  match getProp name v with
  | JsonValue.Array items -> items
  | _ -> failtestf "property '%s' is not an array" name

let private expectJsonField (json: string) (field: string) (expected: string) =
  let root = Json.parseOrFail json
  let actual = getStr field root
  actual |> Expect.equal (sprintf "field '%s'" field) expected

let private minimalWarmup : WarmupContext =
  { SourceFilesScanned = 0
    AssembliesLoaded = []
    NamespacesOpened = []
    FailedOpens = []
    PhaseTiming = { ScanSourceFilesMs = 0L; ScanAssembliesMs = 0L; OpenNamespacesMs = 0L; TotalMs = 0L }
    StartedAt = DateTimeOffset.UtcNow }

[<Tests>]
let sessionEventSerializationTests = testList "SessionEvent serialization" [
  test "WarmupContextSnapshot has correct type and sessionId" {
    let evt = WarmupContextSnapshot("sess-1", { minimalWarmup with SourceFilesScanned = 5; PhaseTiming = { ScanSourceFilesMs = 0L; ScanAssembliesMs = 0L; OpenNamespacesMs = 0L; TotalMs = 100L } })
    let json = SseEvent.toJson evt
    expectJsonField json "type" "warmup_context_snapshot"
    expectJsonField json "sessionId" "sess-1"
    let root = Json.parseOrFail json
    let ctx = getProp "context" root
    getInt "sourceFilesScanned" ctx
    |> Expect.equal "files scanned" 5
    getInt64 "warmupDurationMs" ctx
    |> Expect.equal "duration" 100L
  }

  test "WarmupContextSnapshot serializes assemblies and namespaces" {
    let evt = WarmupContextSnapshot("s", {
      minimalWarmup with
        AssembliesLoaded = [{ Name = "A"; Path = "/a.dll"; NamespaceCount = 10; ModuleCount = 3 }]
        NamespacesOpened = [{ Name = "System"; Kind = OpenableKind.Namespace; Source = "auto"; DurationMs = 0.0 }]
        FailedOpens = [{ Name = "Bad"; Kind = OpenableKind.Namespace; ErrorMessage = "err"; Diagnostics = []; RetryCount = 1; DurationMs = 0.0 }]
    })
    let json = SseEvent.toJson evt
    let root = Json.parseOrFail json
    let ctx = getProp "context" root
    let asms = getArr "assembliesLoaded" ctx
    asms.Length |> Expect.equal "1 assembly" 1
    getStr "name" asms.[0] |> Expect.equal "name" "A"
    getInt "namespaceCount" asms.[0] |> Expect.equal "ns count" 10
    let nss = getArr "namespacesOpened" ctx
    nss.Length |> Expect.equal "1 namespace" 1
    getBool "isModule" nss.[0] |> Expect.isFalse "not module"
    let fails = getArr "failedOpens" ctx
    fails.Length |> Expect.equal "1 failed" 1
    getStr "name" fails.[0] |> Expect.equal "fail name" "Bad"
  }



  test "SessionActivated has type and sessionId" {
    let json = SseEvent.toJson (SessionActivated "abc")
    expectJsonField json "type" "session_activated"
    expectJsonField json "sessionId" "abc"
  }

  test "SessionCreated serializes project names" {
    let json = SseEvent.toJson (SessionCreated("s1", ["P1"; "P2"]))
    expectJsonField json "type" "session_created"
    let root = Json.parseOrFail json
    let projects = getArr "projectNames" root
    projects.Length |> Expect.equal "2 projects" 2
    match JsonValue.asString projects.[1] with
    | Some p -> p |> Expect.equal "second project" "P2"
    | None -> failtestf "Expected string at projectNames.[1]"
  }

  test "SessionStopped has type and sessionId" {
    let json = SseEvent.toJson (SessionStopped "s-dead")
    expectJsonField json "type" "session_stopped"
    expectJsonField json "sessionId" "s-dead"
  }

  test "every SessionEvent variant produces valid JSON with type field" {
    let events = [
      WarmupContextSnapshot("s", minimalWarmup)
      SessionActivated "s"
      SessionCreated("s", [])
      SessionStopped "s"
    ]
    for evt in events do
      let json = SseEvent.toJson evt
      let root = Json.parseOrFail json
      getStr "type" root
      |> Expect.isNotNull "type field exists"
  }
]
