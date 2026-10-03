/// Proves the actor-dispatch seam Island F built (demo-actors-plan.md §1.2):
/// a `WireStep`/`ScenarioPlan` naming the `"vscode"` client resolves,
/// through `CellAgent.actorIdOfString`, to exactly the `ActorId` the wrapped
/// VS Code actor (`Actors.VsCode.toLiveActor`) reports as its own `Id` —
/// i.e. the seam actually routes a VS Code step to the VS Code actor, not
/// merely "the types compile." No cell/Xvfb/VS Code/daemon is spawned:
/// `toLiveActor` only touches its `Handle`'s process/HTTP objects lazily,
/// inside the async functions the seam exposes, so a `Handle` whose fields
/// are never dereferenced here is a legitimate way to inspect its `Id` and
/// prove the wiring without a live editor.
module Bozzetto.Demos.Tests.CellAgentTests

open Expecto
open Expecto.Flip
open Bozzetto.Demos.Domain
open Bozzetto.Demos.CellAgent
open Bozzetto.Demos.Actors

[<Tests>]
let tests =
  testList "CellAgent actor dispatch" [

    testCase "actorIdOfString maps every Wire client/actor token to its ActorId" <| fun _ ->
      actorIdOfString "vscode" |> Expect.equal "vscode maps to ActorId.VsCode" (Some ActorId.VsCode)
      actorIdOfString "neovim" |> Expect.equal "neovim maps to ActorId.Neovim" (Some ActorId.Neovim)
      actorIdOfString "app" |> Expect.equal "app maps to ActorId.App" (Some ActorId.App)
      actorIdOfString "agent" |> Expect.equal "agent maps to ActorId.Agent" (Some ActorId.Agent)

    testCase "actorIdOfString never silently defaults an unknown token to some actor" <| fun _ ->
      actorIdOfString "not-a-real-actor" |> Expect.equal "unknown tokens fail loud (None), never a guess" None

    testCase "actorIdOfString no longer knows the removed web dashboard actor" <| fun _ ->
      actorIdOfString "dashboard" |> Expect.equal "a stale 'dashboard' plan fails loud (None), never a reroute to another actor" None

    testCase "a vscode-targeted step's resolved ActorId matches the VS Code actor's own reported Id — the dispatch seam actually routes to it" <| fun _ ->
      // The Handle's process/HTTP fields are never touched merely by
      // wrapping it — `toLiveActor` only closes over them inside
      // `ResolveRect`/`Observe`/`Command`/`Close`, none of which this test
      // calls — so a `Handle` built from `Unchecked.defaultof` is a safe,
      // honest way to inspect the wrapped `LiveActor`'s `Id` alone.
      let dummyHandle: VsCode.Handle =
        { Process = Unchecked.defaultof<_>
          ControlBaseUrl = "http://127.0.0.1:47760"
          Http = Unchecked.defaultof<_> }

      let liveActor = VsCode.toLiveActor dummyHandle "http://127.0.0.1:47749"
      let resolvedTarget = actorIdOfString "vscode"

      resolvedTarget |> Expect.equal "the plan Client 'vscode' resolves to a real ActorId" (Some liveActor.Id)
      liveActor.Id |> Expect.equal "the wrapped VS Code actor reports ActorId.VsCode" ActorId.VsCode
  ]
