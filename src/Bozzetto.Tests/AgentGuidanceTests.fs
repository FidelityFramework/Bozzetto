module Bozzetto.Tests.AgentGuidanceTests

/// The connected client and compatibility prompts must teach the current
/// provider workflow without sending agents back to retired FSI hosting.

open System
open System.Reflection
open Expecto
open Expecto.Flip
open ModelContextProtocol.Server
open Bozzetto.Server.AgentGuidance

let private contains (needle: string) (text: string) =
  text.Contains(needle, StringComparison.OrdinalIgnoreCase)

let private sentences (text: string) =
  text.Split([| '.'; '\n' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

let private guidanceSurfaces () =
  [ "server instructions", serverInstructions
    "back_to_the_repl", BozzettoPrompts.BackToTheRepl()
    "bozzetto_loop", BozzettoPrompts.BozzettoLoop() ]

let private expectOrdered label (needles: string list) (text: string) =
  needles
  |> List.fold (fun start needle ->
    let index = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase)
    (index, start) |> Expect.isGreaterThanOrEqual (sprintf "%s must name %s in workflow order" label needle)
    index + needle.Length) 0
  |> ignore

let private promptMethods =
  typeof<BozzettoPrompts>.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
  |> Array.choose (fun m ->
    match m.GetCustomAttribute<McpServerPromptAttribute>() with
    | null -> None
    | attr -> Some(attr.Name, m))
  |> Map.ofArray

[<Tests>]
let serverInstructionsTests = testList "MCP server instructions" [

  testCase "Composer guidance reserves the explicit project before writes, builds that reservation, then runs accepted work" <| fun _ ->
    for label, text in guidanceSurfaces () do
      text |> expectOrdered label [ "composer_open_project"; ".fidproj"; "composer_reserve_edit"; "composer_build"; "composer_run_current" ]
      [ "before source writes"; "only on success"; "with that reservation"; "accepted build"; "execute only" ]
      |> List.filter (fun needle -> not (contains needle text))
      |> Expect.isEmpty (sprintf "%s must state reservation and execution admission" label)

  testCase "FSharp implementation guidance requires leased build and unfiltered trusted tests in every prompt" <| fun _ ->
    for label, text in guidanceSurfaces () do
      text |> expectOrdered label [ "RED:"; "GREEN:"; "acquire_full_build_lease"; "dotnet build"; "release_work_lease"; "acquire_test_suite_lease"; "Bozzetto.Tests.dll --summary"; "verdict=Trusted"; "release_work_lease" ]
      [ "grant"; "unfiltered"; "lease_id=<granted leaseId>"; "including failure" ]
      |> List.filter (fun needle -> not (contains needle text))
      |> Expect.isEmpty (sprintf "%s must state lease admission, trust and cleanup" label)

  testCase "retired FSI provider calls cannot be advertised by connected guidance or compatibility prompts" <| fun _ ->
    for label, text in guidanceSurfaces () do
      [ "get_fsi_status"; "get_startup_info"; "create_session"; "send_fsharp_code"; "check_fsharp_code"
        "create_project_session"; "create_solution_session"; "create_bare_session"; "hard_reset_fsi_session"
        "reset_fsi_session"; "get_session_status"; "cancel_eval"; "targeted_verify"; "Resume from the REPL"; "separate SageFS" ]
      |> List.filter (fun needle -> contains needle text)
      |> Expect.isEmpty (sprintf "%s must not prescribe a retired execution provider" label)

  testCase "WHY — every loop step is in them, so the instructions and the prompts can't drift apart" <| fun _ ->
    loopSteps
    |> List.filter (fun step -> not (serverInstructions.Contains step))
    |> Expect.isEmpty "each step of the loop should appear"

  testCase "provider identity and checkout routing remain explicit in every guidance surface" <| fun _ ->
    for label, text in guidanceSurfaces () do
      [ "get_daemon_status"; "identity"; "version"; "composer_list_sessions"; "list_sessions"; "working directory"
        "worktree"; "host/session/epoch"; "source revision"; "composer://sessions"; "/composer"; "composer_retire_worker" ]
      |> List.filter (fun needle -> not (contains needle text))
      |> Expect.isEmpty (sprintf "%s must retain provider and checkout authority" label)

  testCase "FSharp and acceptance gotchas remain useful without an FSI session" <| fun _ ->
    [ "Result.Ok"; "Result.Error"; "filtered test run" ]
    |> List.filter (fun needle -> not (contains needle serverInstructions))
    |> Expect.isEmpty "each gotcha should be named"

  testCase "BUSY leases are retried without unaccounted work and actual friction is reported" <| fun _ ->
    for label, text in guidanceSurfaces () do
      [ "wait/refused"; "BUSY"; "wait the named time"; "retry the identical request"; "never bypass"
        "another daemon"; "tool, input and exact error"; "don't fall back silently" ]
      |> List.filter (fun needle -> not (contains needle text))
      |> Expect.isEmpty (sprintf "%s must distinguish admission delay from provider failure" label)

  testCase "WHY — they never tell the agent to stop, restart or reinstall the user's daemon on its own" <| fun _ ->
    serverInstructions
    |> sentences
    |> List.filter (fun s -> contains "restart" s || contains "reinstall" s)
    |> List.filter (fun s -> not (contains "never" s && contains "without asking" s))
    |> Expect.isEmpty "every sentence about restarting or reinstalling must be a never-without-asking"

  testCase "WHY — they're platform neutral, because agents on Linux and macOS read them too" <| fun _ ->
    [ "PowerShell"; "Start-Process"; "terminal window"; "You OWN" ]
    |> List.filter (fun needle -> contains needle serverInstructions)
    |> Expect.isEmpty "no Windows-only process rules and no 'you own the daemon'"

  testCase "WHY — they point at the full skill and the back_to_the_repl prompt" <| fun _ ->
    [ SkillPath; BackToTheReplPromptName ]
    |> List.filter (fun needle -> not (serverInstructions.Contains needle))
    |> Expect.isEmpty "both pointers should be there"

  testCase "WHY — they stay short, because every client pays for them on every connection" <| fun _ ->
    (serverInstructions.Length, 3000) |> Expect.isLessThan "keep it under 3000 chars"
]

[<Tests>]
let promptTests = testList "MCP prompts" [

  testCase "WHY — back_to_the_repl and bozzetto_loop are registered as MCP prompts, so clients list them" <| fun _ ->
    [ BackToTheReplPromptName; BozzettoLoopPromptName ]
    |> List.filter (fun name -> not (promptMethods.ContainsKey name))
    |> Expect.isEmpty "both prompts should carry [<McpServerPrompt>]"

  testCase "WHY — the prompt type carries [<McpServerPromptType>], which WithPrompts<T> reflects over" <| fun _ ->
    typeof<BozzettoPrompts>.GetCustomAttribute<McpServerPromptTypeAttribute>()
    |> isNull
    |> Expect.isFalse "BozzettoPrompts should be an MCP prompt type"

  testCase "WHY — the SDK builds a prompt named back_to_the_repl from the method, the same way the server does" <| fun _ ->
    let prompt = McpServerPrompt.Create(promptMethods[BackToTheReplPromptName], (null: obj))
    prompt.ProtocolPrompt.Name |> Expect.equal "the protocol name is what the slash command uses" BackToTheReplPromptName

  testCase "the compatibility prompt title and description advertise the current working loop" <| fun _ ->
    let method = promptMethods[BackToTheReplPromptName]
    let title = method.GetCustomAttribute<McpServerPromptAttribute>().Title
    let description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description
    title |> contains "Bozzetto loop" |> Expect.isTrue "the listed title names the working loop"
    [ "Composer"; "leased F#"; "work-lease loop" ]
    |> List.filter (fun needle -> not (contains needle description))
    |> Expect.isEmpty "the listed description must teach the current providers"
    [ title; description ]
    |> List.filter (contains "REPL")
    |> Expect.isEmpty "metadata must not advertise the retired REPL workflow"

  testCase "the compatibility prompt asks where work drifted and resumes the current provider loop" <| fun _ ->
    let text = BozzettoPrompts.BackToTheRepl()
    [ "You left the Bozzetto working loop"; "Name the step where you left"; "Resume the provider and work-lease loop" ]
    |> List.filter (fun needle -> not (text.Contains needle))
    |> Expect.isEmpty "each part of the nudge should be there"
    loopSteps
    |> List.filter (fun step -> not (text.Contains step))
    |> Expect.isEmpty "the loop should be restated in full"

  testCase "WHY — bozzetto_loop carries the same guidance the instructions do" <| fun _ ->
    BozzettoPrompts.BozzettoLoop() |> Expect.equal "one source of truth" serverInstructions
]
