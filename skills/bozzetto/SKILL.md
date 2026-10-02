---
name: bozzetto
description: "How to work in F# when the Bozzetto daemon (MCP, ports 47749/47750) is available: Clef/Composer work goes through the composer_* tools, and F# changes to Bozzetto's own code are validated with dotnet build and the unfiltered test suite under the daemon's work leases. Use at the start of every F# task, before you run dotnet build, dotnet test or dotnet run (take the matching lease first), when a Bozzetto tool errors, and when writing a brief for a sub-agent that will touch F#."
license: MIT
---

# Working in F# with Bozzetto

## Provider scope

For current Clef/Composer development, use Bozzetto's `composer_*` tools and
shared browser/API on ports 47749/47750. Reserve before writing source and run
through `composer_run_current`. No FSI session is required. LLVM ORC JIT remains
the intended future Clef REPL backend.

No separate F# REPL service is part of this workflow; Bozzetto on 47749/47750 is
the only daemon surface. The workflow below describes the retained F#
implementation, host code that is not a product surface; it is not a requirement
to route Clef through FSI or expand Bozzetto's F# product surface. F# changes to
Bozzetto's own code are validated with `dotnet build` and the unfiltered test
suite, started only after `acquire_full_build_lease` /
`acquire_test_suite_lease` and ended with `release_work_lease`.

The [October 1 host transition](../../docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md)
removes embedded production FSI hosting from this checkout, which refuses
requests to create, resume or rebuild an inherited F# session. The REPL
instructions below, including session creation, reload and cleanup, describe
that retained host code; they are not a product surface. Bozzetto's retained F#
tool names do not establish an available F# execution provider. Clefx/ORC
execution remains planned.

For the shared Bozzetto deployment, use `scripts/start-shared-daemon`: it launches
the reviewed installed CLI from an external, dedicated workspace, with logs in
external state storage. Launching from home or the repositories parent causes
the inherited recursive watcher to scan too broadly. Connection and deployment
instructions are in [the live checkpoint](../../docs/Bozzetto_Live_Provider_Checkpoint_2026-09-30.md).

Bozzetto's daemon accounts for the machine memory that every agent on it
spends. A `dotnet build` takes tens of seconds to minutes and real memory, and
a test-suite run takes longer and more. Several of those started at once
against one daemon is how the box ran out of memory (see "Before anything
expensive: ask" below). So:

**The loop is lease-gated. `acquire_full_build_lease` before `dotnet build`,
`acquire_test_suite_lease` before the unfiltered test suite, and
`release_work_lease` after each.** Not optional, not "just to check quickly",
and never shelled around when the daemon says to wait. The unfiltered suite's
`TRUST … verdict=Trusted` line is the acceptance check; a filtered run never
is. Clef/Composer work goes through the `composer_*` tools, not through any
F# session.

Agents drift the moment the daemon feels slow: they build without a lease, or
they reach for an inherited F# session tool that this checkout refuses. That
drift is the failure this skill exists to stop. If you catch yourself typing
`dotnet build` without a lease in hand, stop and read "Before anything
expensive: ask" and "Busy versus broken" below.

## The first minute

This hard fork is installed from a reviewed local build, not a presumed public
NuGet release. For the current daemon, compiler worker and MCP connection, use
[the live checkpoint](../../docs/Bozzetto_Live_Provider_Checkpoint_2026-09-30.md).
A source checkout or installed CLI does not connect an agent to MCP. If tools are
absent, follow that checkpoint's connection steps before declaring the runtime
broken. Preserve a running shared daemon and its active sessions.

1. **Is Bozzetto up?** Call `get_daemon_status` (or `list_sessions`). If the
   tools aren't there at all, Bozzetto isn't connected. Tell the user. Don't work
   around it silently.
2. **Is the daemon current?** Do this before you trust a single result. A stale
   daemon serves old code and gives you wrong answers that look right, and it
   is the most expensive failure in this whole document. See "a stale daemon"
   under "Things that will bite you" for the disguises it wears.

   Read its version from `get_daemon_status`, `boz status`, or the dashboard's
   `/api/daemon-info`, and compare it against the code you're about to work on.
   In the Bozzetto repo itself that's `Directory.Build.props`; anywhere else it's
   "was this daemon started after the last build of this project?" If you can't
   tell, the cheap tell is whether a symbol you just added is visible in the
   session.

   If it's behind, **tell the user and ask them to restart it**. Don't stop,
   restart or reinstall it yourself. It's theirs, and other agents may be on
   it. Use the reviewed local deployment and restart instructions in the live
   checkpoint. Do not replace this hard fork with an assumed public NuGet package.
3. **Know where you're working.** Sessions are tied to a working directory,
   and **a git worktree is its own routing boundary**. A session for the main
   checkout is not yours if you're in `.claude/worktrees/whatever`. Check
   `list_sessions` (and `composer_list_sessions` for Composer) before
   assuming anything there is yours to use.
4. **Choose the work explicitly.** For Clef/Composer: `composer_open_project`
   with an absolute `.fidproj`, `composer_reserve_edit` and a successful
   response before any write, `composer_build` with that reservation, and
   execution only through `composer_run_current`. For Bozzetto's own F# code
   there is nothing to create: take the matching lease and run the build or
   the suite yourself, as "The loop" below describes.
5. **The inherited F# session tools are retained host code, not a step.**
   `get_available_projects`, `create_project_session`,
   `create_solution_session`, `create_bare_session` and the
   `get_session_status` warmup poll described an embedded F# session that
   this checkout refuses to create, resume or rebuild at the retired provider
   boundary. That refusal is the documented boundary, not a broken daemon:
   don't poll it, retry it, or route around it with a second daemon.

## The loop

1. **RED in the test project.** Add the smallest Expecto case to
   `Bozzetto.Tests` that shows the problem: the failing case, the wrong value,
   the bad parse. Message first, `Expecto.Flip` style.
2. **GREEN in the `.fs` file.** Small, composable changes; keep domain logic
   pure and side effects at the edges.
3. **Build under a lease.** `acquire_full_build_lease`, then `dotnet build`
   (warnings are errors), then `release_work_lease` with the exact `leaseId`
   on the normal exit path.
4. **Run the suite under a lease.** `acquire_test_suite_lease`, then the
   unfiltered `dotnet Bozzetto.Tests/bin/<cfg>/net10.0/Bozzetto.Tests.dll --summary`
   (`<cfg>` is `Debug` after a plain `dotnet build`; CI uses `Release`), then
   `release_work_lease`. While you iterate, a filter (`--filter`,
   `--filter-test-list`, `--filter-test-case`) narrows the run; that is inner
   loop only and never the acceptance check.
5. **Read the TRUST line, not the exit colour.** Every tier prints one
   `TRUST tier=… registered=N ran=N … verdict=…` line. `Trusted` is the only
   acceptance; `NarrowedRun` means a filter was on, and `NothingRan` or
   `CountMismatch` exit 3.
6. **Re-verify, then hand the changeset over** for review and commit.

Useful while you're in the loop:
- A lease answer of `wait` or `refused` is the daemon being busy, not broken.
  Wait the time it names and retry the identical request (see "Busy versus
  broken" below).
- **Want to know an API's or an AST's shape?** Read the source and the test
  that already exercises it. Never guess, and never spin up a throwaway
  `dotnet fsi` script to find out.
- The inherited session tools (`send_fsharp_code`, `check_fsharp_code`,
  `hard_reset_fsi_session`, `cancel_eval`, `explain_test_failure`,
  `targeted_verify`) are retained host code that acted on an embedded F#
  session; in this checkout there is no such session for them to act on.

## Things that will bite you

- **"Operation could not be completed due to earlier error"** means a previous
  statement failed. Read the diagnostics and fix that statement. The session is
  fine, so don't reset it.
- **A bare `Error` or `Ok` that resolves to the wrong type.** If a match on a
  `Result` fails with "This union case does not take arguments" or names some
  other type, a union case in scope is shadowing `Result.Error` / `Result.Ok`.
  Write `Result.Error` / `Result.Ok`. Bozzetto's own types can't do this anymore:
  no Bozzetto union has a case named `Ok`, `Error`, `Some` or `None`, and a test
  enforces that. A library you open still might.
- **Never `#r` a DLL the session already loaded from the project.** It creates a
  second copy of every type ("type X is not compatible with type X"). `#r` also
  locks the DLL, so a later rebuild can't overwrite it.
- **A stale daemon is the most expensive failure here, because it looks like
  every other failure.** A daemon that has been running since before your code
  changed keeps serving the assemblies it started with. Nothing warns you. It
  wears at least three disguises:
  - `"type not found, Version=..."` or a Core version mismatch.
  - `Could not load file or assembly 'System.Runtime, Version=N.0.0.0'` in
    worker stderr, on a project that builds fine on its own. That one means the
    daemon's worker is on an older .NET than your project targets — it starts
    fine and then chokes the moment it loads your DLLs.
  - No error at all: evals that quietly disagree with the code in front of you.

  **Check the daemon's version before you believe anything else.**
  `get_daemon_status` or `get_session_status`, `boz status`, or
  `/health`. If the daemon is behind the code
  you're working on, say so and ask the user to restart it — that is the fix,
  and no amount of `hard_reset_fsi_session` will substitute for it, because the
  daemon process itself is the stale thing. Don't restart it yourself; it's
  theirs and other agents may be on it.

  An agent lost an entire session to this: it read the load error as "Bozzetto is
  broken", spent hours working around it with `dotnet`, and the daemon was
  simply old. Checking the version first would have cost one tool call.
- **A filtered test run is never the acceptance check.** A filter that matches
  nothing prints `0 failed` and exits 0 in plain Expecto. Bozzetto's trust line
  says `NothingRan` or `NarrowedRun`. Only an unfiltered run counts.
- **Clean up.** `stop_session` on every session you created. Kill only
  processes you started, by exact PID, never by name.

## Before anything expensive: ask

Session create/warmup, `hard_reset_fsi_session rebuild=true`, a full `dotnet
build`, a test-suite run, starting an app — these cost real machine memory.
One night, five agents each did one of these against a single daemon, all at
once. Nobody was misbehaving; nothing coordinated. The daemon had no way to
know until its RSS was already at 55GB of a 62GB box.

Bozzetto-managed session creation and `hard_reset_fsi_session rebuild=true`
acquire their own coordination leases. Do not manually lease those operations.

Before a caller-owned full `dotnet build`, unfiltered test suite, or app run,
use the matching MCP tool:

- `acquire_full_build_lease` for a build you start yourself;
- `acquire_test_suite_lease` for a test-suite process you start yourself;
- `acquire_run_app_lease` for a run-app process you start yourself.

A granted tool returns an opaque `leaseId`. Call `release_work_lease` with that
exact id on the normal exit path. If a lease tool denies or delays the work,
follow the decision it returns; do not shell around the daemon and spend the
same memory outside its accounting.

## Busy versus broken — the distinction that matters most

When Bozzetto refuses or delays something, figure out which of these you're in
before you do anything else. Getting this backwards is exactly how the
incident above happened: every agent read "the REPL is fighting me" and
reached for `dotnet`, when the REPL wasn't broken — the daemon was busy, and
`dotnet` spent the same memory anyway, outside its accounting, at the exact
moment it was trying to shed load.

- **BROKEN**: a real bug, a version skew, a tool erroring for reasons that
  aren't your code, the REPL genuinely not doing what it says. The escape
  hatch below is correct: use `dotnet` for that one step, and report it,
  because the report is how it gets fixed.
- **BUSY**: Bozzetto (or the `bozzetto-repl-guard` hook, if it's installed) tells
  you pressure is `tight` or `critical`, a lease request came back `wait` or
  `refused`, or a declared final-gate `dotnet build`/`test`/`run` gets denied
  with a message that says "BUSY, not broken." The escape hatch is exactly
  the WRONG move here. **Wait the time it names, then retry the identical
  command or lease request.** Shelling out anyway, or spinning up your own
  daemon to get around a busy one, spends the exact memory Bozzetto is trying
  to reclaim — invisibly to it. That is the whole mechanism of the incident
  this section exists to prevent.

If you genuinely cannot tell which one you're in, that itself is a bug: report
it exactly like a BROKEN case (tool, input, full error) rather than guessing.

## When the REPL fights you (this means BROKEN, not busy)

Sometimes it will. A version skew, a load error, a tool that errors for reasons
that aren't your code — this is the BROKEN case above, not the BUSY one. When
that happens:

1. **Don't fall back silently.** Write down exactly what broke: the tool, the
   input, the full error.
2. Try the obvious fix once: build first, qualify `Result.Ok`, create the
   session in the right worktree, check the daemon version.
3. If it still fights you, use `dotnet` for **that one step only** — after
   taking a lease for it if Bozzetto is up (see above) — and say so in your
   report with the error from step 1. That report is how Bozzetto gets fixed.
   Silent fallbacks are how it stays broken.
4. **Never spin up your own daemon to get around a busy one.** A second
   daemon spends the same machine memory the first one is trying to protect,
   completely outside anyone's accounting. Only spawn a second daemon when
   you are testing daemon code itself that the running daemon predates — see
   AGENTS.md's multi-agent section — and give it an explicit owner/TTL.

## Permissions and auto mode (Claude Code)

The REPL also gets you out of most permission friction, which is one more
reason to stay on it.

- **Allow Bozzetto once.** Add `"mcp__bozzetto__*"` (or `"mcp__bozzetto"`) to
  `permissions.allow` in settings.json. An action that matches an allow rule
  resolves right away, so Bozzetto calls never wait on a prompt or on auto mode's
  classifier. See the
  [permissions docs](https://code.claude.com/docs/en/permissions.md).
- **Shell commands mostly don't get that.** In auto mode, broad Bash allow rules
  (`Bash(*)`, wildcarded interpreters, package-manager run commands) are
  dropped, so most `dotnet` commands go through the classifier one at a time.
  That's slower, and every one is another chance of a block or a "cannot
  determine the safety" denial. See the
  [permission modes docs](https://code.claude.com/docs/en/permission-modes.md).
- **Keep shell commands narrow and single-purpose.** A compound command is
  checked piece by piece, so `cd x && dotnet build && ...` needs every piece
  approved. Use absolute paths instead of `cd`.
- **Don't wait with sleep.** A `sleep N; check` pattern gets blocked. For a
  session, poll `get_session_status`. For a long command, run it in the background
  and let it tell you when it's done.
- **Kill only by exact PID, never by name.** Mass kills look destructive, and
  they can take down the user's daemon.
- **If the classifier times out** ("cannot determine the safety of ... right
  now"), it isn't a verdict on your command. Do the read-only work you can,
  then retry. Don't rewrite the command to sneak past it.

## Getting back on track

If the user says "back to the REPL", "back to the loop", "mandate 1", or
invokes the Bozzetto `back_to_the_repl` prompt, you've drifted. The prompt's
name is inherited; in this checkout the loop it points back to is the
lease-gated build and unfiltered test loop above, not an F# session. Stop what
you're doing, name the step where you left the loop, and pick it back up from
there. Don't argue it, and don't finish the unleased or filtered run first.

## Briefing another agent

Sub-agents don't inherit any of this. Every brief for F# work must include the
loop explicitly:

1. `get_daemon_status`; check the daemon's version against the code and tell
   the user if it's behind — never restart it
2. `list_sessions`; the agent's own worktree is its own routing boundary, so
   it never borrows the main checkout's session
3. write the failing test in `Bozzetto.Tests`
4. make it pass in the `.fs` file
5. `acquire_full_build_lease`, `dotnet build`, `release_work_lease`
6. `acquire_test_suite_lease`, the unfiltered `Bozzetto.Tests` run, read the
   `TRUST` line for `Trusted`, `release_work_lease`
7. a filtered run is inner loop only, never the acceptance check
8. a `wait` or `refused` lease is BUSY: wait the named time and retry the
   identical request; never shell around it or start a second daemon
9. report tool friction (tool, input, full error) instead of silently falling
   back
10. for Clef/Composer: `composer_open_project` on an absolute `.fidproj`,
    `composer_reserve_edit` before writes, `composer_build`, and execution
    only through `composer_run_current`

The easiest way is to tell it to load this skill.
