namespace Bozzetto.Providers

open System
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
type ProviderIdentity = FSharp | ClefComposer

module ProviderIdentity =
  let name = function
    | ProviderIdentity.FSharp -> "fsharp"
    | ProviderIdentity.ClefComposer -> "clef-composer"

  let parse = function
    | "fsharp" -> Result.Ok ProviderIdentity.FSharp
    | "clef-composer" -> Result.Ok ProviderIdentity.ClefComposer
    | _ -> Result.Error "Unknown provider; specify fsharp or clef-composer."

type Authority = {
  Host: string
  Session: string
  Provider: ProviderIdentity
  Epoch: string
  /// Adapter revision. Compiler generations in artifact/run payloads are
  /// independent: a compiler mutation can advance state before throwing.
  Generation: int64
}

type AcceptedArtifact = {
  Generation: int64
  SourceVersion: string
  ArtifactPath: string
  ArtifactSha256: string
  ObjectManifest: string
  ChangedWitnesses: string array
  RetainedWitnesses: string array
  RetiredWitnesses: string array
  WitnessVisits: (string * int) array
  CompiledObjects: string array
  ReusedObjects: string array
  RetiredObjects: string array
}

type RunResult = {
  Generation: int64
  SourceVersion: string
  ExitCode: int
  StandardOutput: string
  StandardError: string
}

type Refusal = { Code: string; Message: string }

type Reply<'a> = {
  Authority: Authority
  Outcome: Result<'a, Refusal>
}

type SessionSnapshot = {
  Project: string
  ManifestPath: string
  Closed: bool
  Busy: bool
  Current: AcceptedArtifact option
  RevocationPending: bool
  BackendError: string option
  CleanupPending: bool
  CleanupError: string option
}

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
  abstract Current: AcceptedArtifact option
  abstract ManifestPath: string
