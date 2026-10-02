/// What Bozzetto tells agents about how to work: the MCP server instructions
/// every client gets on connect, and the prompts a user can fire to put a
/// drifting agent back on the provider and work-lease loop.
///
/// This is the short, always-on version of skills/bozzetto/SKILL.md. Every
/// client pays for the instructions in tokens on every connection, so keep
/// them tight and point at the skill for the rest.
module Bozzetto.Server.AgentGuidance

open System.ComponentModel
open ModelContextProtocol.Server

/// Where the full rulebook lives.
[<Literal>]
let SkillPath = "skills/bozzetto/SKILL.md"

[<Literal>]
let BackToTheReplPromptName = "back_to_the_repl"

[<Literal>]
let BozzettoLoopPromptName = "bozzetto_loop"

/// The loop, one line per step. The instructions and both prompts all
/// render these same lines, so they can't drift apart.
let loopSteps = [
  "Clef: composer_open_project with an absolute .fidproj. Retain the returned host/session/epoch and source revision."
  "Call composer_reserve_edit before source writes; proceed only on success. Edit, then composer_build with that reservation."
  "After an accepted build, execute only with composer_run_current. Observe composer://sessions or /composer. Use composer_retire_worker before compiler replacement, coordinating with other owners."
  "F# implementation: RED: write the smallest failing Expecto test in Bozzetto.Tests. GREEN: fix the .fs file; use 2 spaces and Expecto.Flip messages first."
  "Build: acquire_full_build_lease; after grant run dotnet build; release_work_lease with lease_id=<granted leaseId> when the process completes, including failure."
  "Tests: acquire_test_suite_lease; after grant run unfiltered dotnet Bozzetto.Tests/bin/<cfg>/net10.0/Bozzetto.Tests.dll --summary; require TRUST verdict=Trusted; release_work_lease with lease_id=<granted leaseId> when the process completes, including failure."
]

let firstMinute = [
  "Call get_daemon_status; compare daemon identity/version with source and the installed checkpoint. Report skew and preserve the shared daemon."
  "Call composer_list_sessions and list_sessions. Match the working directory, project and host/session/epoch. A git worktree is its own routing boundary; use its own session."
]

let gotchas = [
  "Lease wait/refused is BUSY: wait the named time and retry the identical request. Never bypass admission with shell work or another daemon."
  "Before a caller-owned app process, acquire_run_app_lease; after grant run it, then release_work_lease with lease_id=<granted leaseId> when it completes, including failure. Composer execution stays in its provider loop."
  "A filtered test run is never the acceptance check. Only an unfiltered run counts."
  "If a library shadows Result cases, write Result.Error/Result.Ok. Read source and tests for API shapes."
]

let private numbered (lines: string list) =
  lines |> List.mapi (fun i line -> sprintf "%d. %s" (i + 1) line)

let private bulleted (lines: string list) =
  lines |> List.map (sprintf "- %s")

/// The MCP ServerInstructions text.
let serverInstructions =
  String.concat "\n" [
    "Bozzetto on 47749/47750 serves Clef/Composer. Embedded production FSI hosting is retired; no separate F# REPL service is part of this workflow. F# changes to Bozzetto itself use the work-lease loop below."
    ""
    "First minute:"
    yield! numbered firstMinute
    ""
    "The loop:"
    yield! numbered loopSteps
    ""
    "Things that bite:"
    yield! bulleted gotchas
    ""
    "Report friction with the tool, input and exact error; don't fall back silently."
    "Never stop, restart or reinstall the user's Bozzetto daemon without asking. Other agents may be using it. Close only sessions you created."
    sprintf "The full rules are in %s in the Bozzetto repo. If you drift off the loop, the %s prompt puts you back on it." SkillPath BackToTheReplPromptName
  ]

/// The compatibility name remains; the prompt teaches the current workflow.
let backToTheRepl =
  String.concat "\n" [
    "You left the Bozzetto working loop."
    ""
    "Name the step where you left the loop and what pulled you off it."
    ""
    serverInstructions
    ""
    "Resume the provider and work-lease loop at that step."
  ]

/// The bozzetto_loop prompt text: the whole always-on guidance, on demand.
let bozzettoLoop = serverInstructions

[<McpServerPromptType>]
type BozzettoPrompts() =

  [<McpServerPrompt(Name = BackToTheReplPromptName, Title = "Back to the Bozzetto loop")>]
  [<Description("Resume the current Composer or leased F# implementation workflow: name where work drifted and restate the provider and work-lease loop.")>]
  static member BackToTheRepl() : string = backToTheRepl

  [<McpServerPrompt(Name = BozzettoLoopPromptName, Title = "The Bozzetto loop")>]
  [<Description("The current Bozzetto loop: provider identity, Composer reservation/build/run, leased F# build and unfiltered trusted tests, and daemon ownership.")>]
  static member BozzettoLoop() : string = bozzettoLoop
