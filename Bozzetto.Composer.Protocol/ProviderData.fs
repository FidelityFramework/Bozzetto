namespace Bozzetto.Providers


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
  /// Order of actual status captures under the provider gate, scoped by the
  /// enclosing host/session/epoch. It does not grant compiler authority.
  Observation: uint64
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
