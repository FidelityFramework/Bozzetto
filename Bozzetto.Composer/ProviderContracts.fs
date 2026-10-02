namespace Bozzetto.Providers

open System
open System.Threading
open System.Threading.Tasks

/// Tickets remain process-local. Only the owning backend creates/consumes them.
/// The provider never serializes or reconstructs a compiler ticket.
/// Reserve and Dispose may mutate state before throwing. RunCurrentAsync must
/// select and launch its artifact synchronously before returning its task:
/// the adapter serializes this prefix with Reserve, then awaits outside that
/// boundary. Status never calls Current because its getter may perform IO or
/// contend with compiler publication. It reports observed accepted metadata.
type IProjectBackend<'Ticket> =
  inherit IDisposable
  abstract Reserve: label: string -> 'Ticket
  abstract BuildAsync: ticket: 'Ticket * cancellation: CancellationToken -> Task<Result<AcceptedArtifact, string>>
  abstract RunCurrentAsync: arguments: string list * cancellation: CancellationToken -> Task<Result<RunResult, string>>
  /// Backend-owned accepted artifact metadata. The getter may perform IO;
  /// callers must use the invocation fence rather than a status read.
  abstract Current: AcceptedArtifact option
  abstract ManifestPath: string
