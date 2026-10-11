/// Runs INSIDE the real VS Code extension host (loaded by @vscode/test-electron
/// via `extensionTestsPath`) and proves that invoking a real Bozzetto command —
/// through the real, stable `vscode.commands.executeCommand` API, never a
/// simulated keystroke or a scraped DOM element — actually reaches the real
/// daemon and changes its real state. No command palette, no CSS selectors,
/// no CDP debug port: this is the mechanism VS Code itself provides for
/// exactly this kind of test, so the whole class of UI-automation flakiness
/// the old CDP-driven journeys hit (VscodeExtensionTests.fs, retired) cannot
/// occur here by construction.
///
/// Exports `run: unit -> JS.Promise<unit>`, the exact contract
/// @vscode/test-electron requires: resolve to prove the suite passed,
/// reject (here: let an exception propagate) to prove it failed.
module Bozzetto.VscodeTestElectron.ExtensionHostSuite

open Fable.Core
open Vscode
open Bozzetto.VscodeTestElectron.Command
open Bozzetto.VscodeTestElectron.DaemonContract
open Bozzetto.VscodeTestElectron.Bindings

let private mcpPort () = NodeProcess.getEnv "BOZZETTO_MCP_PORT"

/// Invoke `command`, then poll `probe` (the daemon's own state, independent
/// of the extension's UI) until `isExpected` holds, up to `maxAttempts`
/// times, 500ms apart. `describeExpected` names what was being waited for,
/// so a failure reports exactly what was proven or why it wasn't — never a
/// bare true/false. The predicate describes the expected daemon state.
let private proveCommandReachesState
  (command: Command)
  (probe: unit -> JS.Promise<'state>)
  (isExpected: 'state -> bool)
  (describeExpected: string)
  (maxAttempts: int)
  : JS.Promise<ProofOutcome> =
  promise {
    let! _ = Commands.executeCommand (Command.id command)
    let rec poll attemptsLeft =
      promise {
        match attemptsLeft with
        | 0 ->
          let! last = probe ()
          return DisprovenBy (sprintf "daemon still reports %A after invoking %A, expected %s" last command describeExpected)
        | _ ->
          let! current = probe ()
          match isExpected current with
          | true -> return Proven
          | false ->
            do! sleep 500
            return! poll (attemptsLeft - 1)
      }
    return! poll maxAttempts
  }

let private assertProven (outcome: JS.Promise<ProofOutcome>) : JS.Promise<unit> =
  promise {
    match! outcome with
    | Proven -> ()
    | DisprovenBy reason -> failwith reason
  }

let run () : JS.Promise<unit> =
  promise {
    let liveTesting () = getLiveTestingState (mcpPort ())
    do! proveCommandReachesState EnableLiveTesting liveTesting ((=) Enabled) "Enabled" 20 |> assertProven
    do! proveCommandReachesState DisableLiveTesting liveTesting ((=) Disabled) "Disabled" 20 |> assertProven
  }
