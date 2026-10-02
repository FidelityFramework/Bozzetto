module Bozzetto.Server.ComposerTools

open System
open System.ComponentModel
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server
open Bozzetto.ComposerIntegration
open Bozzetto.Providers
open Bozzetto.Composer.Protocol

[<Literal>]
let ResourceUri = "composer://sessions"

let disabledSupervisor () =
  new ComposerSupervisor(
    (fun () -> Task.FromException<IComposerWorker>(InvalidOperationException "Composer worker is not configured.")),
    false)

/// Standard MCP projection of the typed provider result.
let toolResult (response: JsonElement) =
  let result = CallToolResult()
  result.StructuredContent <- Nullable response
  result.Content.Add(TextContentBlock(Text = response.GetRawText()))
  match response.TryGetProperty "success" with
  | true, success when success.ValueKind = JsonValueKind.False -> result.IsError <- Nullable true
  | _ -> ()
  result

type ComposerTools(supervisor: ComposerSupervisor) =
  let address host session epoch: SessionAddress =
    { Worker = { Host = host; Epoch = epoch; Provider = ProviderIdentity.ClefComposer }; Session = session }
  let execute request cancellation = task {
    let! response = supervisor.ExecuteAsync(request, cancellation)
    return toolResult (ComposerClientJson.reply response)
  }

  [<McpServerTool>]
  [<Description("Open an absolute Clef .fidproj in the daemon's Composer provider. Returns explicit host, session and compiler epoch authority shared with the human Composer page.")>]
  member _.composer_open_project(project: string, cancellationToken: CancellationToken) =
    execute (Open((address "" "" "").Worker, project)) cancellationToken

  [<McpServerTool>]
  [<Description("Read the shared Composer session directory and authoritative worker status, including cleanup failures. Accepted metadata does not guarantee execution: composer_run_current revalidates inputs and artifact bytes.")>]
  member _.composer_list_sessions(cancellationToken: CancellationToken) = task {
    let! response = supervisor.SessionsAsync cancellationToken
    return toolResult (ComposerClientJson.directory response)
  }

  [<McpServerTool>]
  [<Description("Reserve a Clef edit before changing source or dependency files. Wait for a successful response before editing; use its reservation with composer_build. Old artifact authority is withdrawn immediately.")>]
  member _.composer_reserve_edit(host: string, session: string, epoch: string, label: string, cancellationToken: CancellationToken) =
    execute (Reserve(address host session epoch, label)) cancellationToken

  [<McpServerTool>]
  [<Description("Build a previously reserved Clef project revision through Composer. The opaque reservation is bound to its session and compiler epoch; concurrent observers share its admitted build. Compiler diagnostics and proof refusals retain their original authority.")>]
  member _.composer_build(host: string, session: string, epoch: string, reservation: string, cancellationToken: CancellationToken) =
    execute (Build(address host session epoch, reservation)) cancellationToken

  [<McpServerTool>]
  [<Description("Read one Composer session's authoritative status, cached accepted metadata, pending revocation and cleanup errors. This status read grants no execution permission; run through composer_run_current.")>]
  member _.composer_session_status(host: string, session: string, epoch: string, cancellationToken: CancellationToken) =
    execute (Status(address host session epoch)) cancellationToken

  [<McpServerTool>]
  [<Description("Preview Calque formatting of an immutable Clef source buffer in this Composer session. Supply its current generation, opaque document identity, GUID incarnation, uint64 revision and configuration clef-two-space-lf-v1. Returns exact base source SHA256 and formatted text; performs no file reads or writes and grants no edit or execution authority. Reserve successfully before applying, compare the exact base buffer again, then build with that reservation.")>]
  member _.composer_format_preview(host: string, session: string, epoch: string, generation: int64, document: string, incarnation: string, revision: uint64, source: string, configuration: string, cancellationToken: CancellationToken) =
    execute (Format(address host session epoch, generation, {
      Document = document; Incarnation = incarnation; Revision = revision
      Source = source; Configuration = configuration })) cancellationToken

  [<McpServerTool>]
  [<Description("Run the current accepted Clef artifact through Composer's input and executable revalidation gates. Arguments are individual strings. Never launch a returned artifact path directly; stale or corrupt artifacts are refused.")>]
  member _.composer_run_current(host: string, session: string, epoch: string, arguments: string array, cancellationToken: CancellationToken) =
    execute (Run(address host session epoch, arguments)) cancellationToken

  [<McpServerTool>]
  [<Description("Withdraw a Composer session's current artifact authority and cancel outstanding work. Logical cancellation is immediate; status exposes pending physical withdrawal or backend errors. Compiler termination may complete later.")>]
  member _.composer_cancel(host: string, session: string, epoch: string, cancellationToken: CancellationToken) =
    execute (Cancel(address host session epoch)) cancellationToken

  [<McpServerTool>]
  [<Description("Logically close a Composer session and begin cleanup. Inspect cleanupPending and cleanupError; a closed session does not imply physical cleanup succeeded. The session cannot be reopened.")>]
  member _.composer_close_session(host: string, session: string, epoch: string, cancellationToken: CancellationToken) =
    execute (Close(address host session epoch)) cancellationToken

  [<McpServerTool>]
  [<Description("Retire every session in this Composer worker before compiler replacement. Wait for the complete cleanup result; failures remain refusals. Replacement requires a fresh worker epoch. Active compiler patching is unsupported.")>]
  member _.composer_retire_worker(host: string, epoch: string, cancellationToken: CancellationToken) =
    execute (PrepareCompilerChange((address host "" epoch).Worker)) cancellationToken

type ComposerResources(supervisor: ComposerSupervisor) =
  [<McpServerResource(UriTemplate = ResourceUri, Name = "composer_sessions", MimeType = "application/json")>]
  [<Description("Composer sessions and worker authority from the same supervisor used by the human Composer page and composer_list_sessions. Subscribe for resource update notifications after shared changes.")>]
  member _.Sessions(cancellationToken: CancellationToken) : Task<string> = task {
    let! response = supervisor.SessionsAsync cancellationToken
    return (ComposerClientJson.directory response).GetRawText()
  }
