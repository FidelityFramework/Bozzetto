module Bozzetto.Server.ComposerTools

open System
open System.ComponentModel
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server
open Bozzetto.ComposerIntegration

[<Literal>]
let ResourceUri = "composer://sessions"

let disabledSupervisor () =
  new ComposerSupervisor(
    (fun () -> Task.FromException<IComposerWorker>(InvalidOperationException "Composer worker is not configured.")),
    false)

/// Text and structured content are the same worker-owned JSON envelope.
let toolResult (response: JsonElement) =
  let result = CallToolResult()
  result.StructuredContent <- Nullable response
  result.Content.Add(TextContentBlock(Text = response.GetRawText()))
  match response.TryGetProperty "success" with
  | true, success when success.ValueKind = JsonValueKind.False -> result.IsError <- Nullable true
  | _ -> ()
  result

type ComposerTools(supervisor: ComposerSupervisor) =
  let execute operation host session epoch parameters cancellation = task {
    let request: ComposerRequest = {
      Operation = operation; Host = host; Session = session; Epoch = epoch
      Provider = "clef-composer"; Parameters = parameters }
    let! response = supervisor.ExecuteAsync(request, cancellation)
    return toolResult response
  }

  [<McpServerTool>]
  [<Description("Open an absolute Clef .fidproj in the daemon's Composer provider. Returns explicit host, session and compiler epoch authority shared with the human Composer page. F# sessions remain separate.")>]
  member _.composer_open_project(project: string, cancellationToken: CancellationToken) =
    execute "open" "" "" "" [ "project", box project ] cancellationToken

  [<McpServerTool>]
  [<Description("Read the shared Composer session directory and authoritative worker status, including cleanup failures. Accepted metadata does not guarantee execution: composer_run_current revalidates inputs and artifact bytes.")>]
  member _.composer_list_sessions(cancellationToken: CancellationToken) = task {
    let! response = supervisor.SessionsAsync cancellationToken
    return toolResult response
  }

  [<McpServerTool>]
  [<Description("Reserve a Clef edit before changing source or dependency files. Wait for a successful response before editing; use its single-use reservation with composer_build. Old artifact authority is withdrawn immediately.")>]
  member _.composer_reserve_edit(host: string, session: string, epoch: string, label: string, cancellationToken: CancellationToken) =
    execute "reserve" host session epoch [ "label", box label ] cancellationToken

  [<McpServerTool>]
  [<Description("Build a previously reserved Clef project revision through Composer. The opaque reservation is single-use and bound to its session and compiler epoch. Compiler diagnostics and proof refusals retain their original authority.")>]
  member _.composer_build(host: string, session: string, epoch: string, reservation: string, cancellationToken: CancellationToken) =
    execute "build" host session epoch [ "reservation", box reservation ] cancellationToken

  [<McpServerTool>]
  [<Description("Read one Composer session's authoritative status, cached accepted metadata, pending revocation and cleanup errors. This status read grants no execution permission; run through composer_run_current.")>]
  member _.composer_session_status(host: string, session: string, epoch: string, cancellationToken: CancellationToken) =
    execute "status" host session epoch [] cancellationToken

  [<McpServerTool>]
  [<Description("Run the current accepted Clef artifact through Composer's input and executable revalidation gates. Arguments are individual strings. Never launch a returned artifact path directly; stale or corrupt artifacts are refused.")>]
  member _.composer_run_current(host: string, session: string, epoch: string, arguments: string array, cancellationToken: CancellationToken) =
    execute "run" host session epoch [ "arguments", box arguments ] cancellationToken

  [<McpServerTool>]
  [<Description("Withdraw a Composer session's current artifact authority and cancel outstanding work. Logical cancellation is immediate; status exposes pending physical withdrawal or backend errors. Compiler termination may complete later.")>]
  member _.composer_cancel(host: string, session: string, epoch: string, cancellationToken: CancellationToken) =
    execute "cancel" host session epoch [] cancellationToken

  [<McpServerTool>]
  [<Description("Logically close a Composer session and begin cleanup. Inspect cleanupPending and cleanupError; a closed session does not imply physical cleanup succeeded. The session cannot be reopened.")>]
  member _.composer_close_session(host: string, session: string, epoch: string, cancellationToken: CancellationToken) =
    execute "close" host session epoch [] cancellationToken

  [<McpServerTool>]
  [<Description("Retire every session in this Composer worker before compiler replacement. Wait for the complete cleanup result; failures remain refusals. Replacement requires a fresh worker epoch. Active compiler patching is unsupported.")>]
  member _.composer_retire_worker(host: string, epoch: string, cancellationToken: CancellationToken) =
    execute "prepare_compiler_change" host "" epoch [] cancellationToken

type ComposerResources(supervisor: ComposerSupervisor) =
  [<McpServerResource(UriTemplate = ResourceUri, Name = "composer_sessions", MimeType = "application/json")>]
  [<Description("Composer sessions and worker authority from the same supervisor used by the human Composer page and composer_list_sessions. Subscribe for resource update notifications after shared changes.")>]
  member _.Sessions(cancellationToken: CancellationToken) : Task<string> = task {
    let! response = supervisor.SessionsAsync cancellationToken
    return response.GetRawText()
  }
