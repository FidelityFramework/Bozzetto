/// The actor-dispatch seam (Island F, demo-actors-plan.md §1.2, §1.3). Before
/// it, the cell-agent hard-called exactly one actor by name (`CellAgent.fs`,
/// pre-seam) — which meant every new actor (VS Code, Neovim, the App
/// co-actor, the Agent/MCP viz) would have to edit the same file to plug in,
/// colliding with every other actor island. `LiveActor` is the fix:
/// a plain record of async functions (mirrors `Domain.DemoRuntime`'s
/// injected-edge doctrine — every side effect this project performs is a
/// swappable function value, never a class hierarchy) so the cell-agent can
/// hold a `Map<ActorId, LiveActor>` and route each `WireStep` to the one
/// actor it targets, instead of a fixed handle. The shape below is the
/// `launch`/`resolve`/`observe`/`close` shape the first (web dashboard,
/// since removed) actor had — Island F only names it once so every actor
/// implements the same one.
module Bozzetto.Demos.Actors.Actor

open Bozzetto.Demos.Domain

/// One live actor inside a cell.
type LiveActor =
  { Id: ActorId
    /// Resolve a wire selector/target to the screen rect this actor measured
    /// live (never a guess — §3 ResolvedTarget doctrine).
    ResolveRect: string -> Async<ScreenRect option>
    /// Observe a wire expectation selector through this actor's own semantic
    /// surface (ext-host/daemon HTTP for VS Code, RPC for nvim, X11 pixels
    /// for the App, MCP responses for the Agent).
    Observe: string -> float -> Async<bool>
    /// Run a non-input client command (OpenFile/RunApp/...) if this actor
    /// supports it; used by Action.Setup steps.
    Command: string -> Async<unit>
    Close: unit -> Async<unit> }
