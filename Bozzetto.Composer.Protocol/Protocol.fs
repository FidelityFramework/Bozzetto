// Shared closed binary worker contract. No compiler tickets or semantic inference.
namespace Bozzetto.Composer.Protocol

open Bozzetto.Providers

[<RequireQualifiedAccess>]
type EncodingId = BAREWire1

/// This is a host/epoch address, not artifact or run authority.
type WorkerAddress = { Host: string; Epoch: string; Provider: ProviderIdentity }
type SessionAddress = { Worker: WorkerAddress; Session: string }

[<RequireQualifiedAccess>]
type Operation =
  | Hello | Open | Reserve | Build | Status | Run | Cancel | CancelRequest
  | Close | PrepareCompilerChange

/// A closed, explicit wire schema. A binary v2 peer accepts exactly this
/// agreement before admitting any request that carries a WorkerAddress.
type Agreement = { ProtocolVersion: uint16; Encoding: EncodingId; ContractDigest: string }

type RequestBody =
  | Hello of expected: Agreement
  | Open of target: WorkerAddress * project: string
  | Reserve of target: SessionAddress * label: string
  | Build of target: SessionAddress * reservation: string
  | Status of target: SessionAddress
  | Run of target: SessionAddress * arguments: string array
  | Cancel of target: SessionAddress
  | CancelRequest of target: WorkerAddress * targetRequestId: string
  | Close of target: SessionAddress
  | PrepareCompilerChange of target: WorkerAddress

/// Full string identity is preserved. No truncation/hash/uint32 recycling.
/// ProtocolVersion is encoded before the request union and checked before it.
type Request = { ProtocolVersion: uint16; RequestId: string; Body: RequestBody }

type CompilerIdentity = { AssemblyPath: string; Sha256: string; Version: string }
/// A description of the loaded semantic contract, not a transferred revision.
type PsgIdentity = { Schema: int; AssemblySha256: string }
type HelloResult = {
  Agreement: Agreement
  Compiler: CompilerIdentity
  Psg: PsgIdentity
  Operations: Operation array
  InMemoryPatchAllowed: bool
}
type OpenResult = { Observation: uint64; Project: string; ManifestPath: string }
type ReserveResult = { Reservation: string }
type CancellationResult = { TargetRequestId: string; CancellationRequested: bool }
type CloseResult = {
  Observation: uint64
  Closed: bool
  CleanupPending: bool
  CleanupError: string option
}
type RetirementResult = { RestartRequired: bool; InMemoryPatchAllowed: bool }

[<RequireQualifiedAccess>]
type RefusalCode =
  | InvalidRequest | ProtocolVersion | EncodingMismatch | ContractMismatch
  | WrongAuthority | WrongProvider | InvalidProject | InvalidCache
  | CompilerRetired | UnknownSession | Closed | UnsupportedOperation
  | RequestRefused | DuplicateRequest | CleanupFailed | Superseded
  | CompilerRefused | Canceled | ObservationCapacity | Busy | SessionCapacity
  | BackendFailed | InvalidReservation | NotAccepted | FrameTooLarge
  | MalformedPayload

type Refusal = { Code: RefusalCode; Message: string }

type ReplyBody =
  | HelloAccepted of HelloResult
  | Opened of OpenResult
  | Reserved of ReserveResult
  | Built of AcceptedArtifact
  | Observed of SessionSnapshot
  | Ran of RunResult
  | Canceled
  | RequestCanceled of CancellationResult
  | Closed of CloseResult
  | CompilerRetired of RetirementResult

type Reply = {
  ProtocolVersion: uint16
  RequestId: string
  Authority: Authority
  Outcome: Result<ReplyBody, Refusal>
}

/// Outer framing failures without a decoded request identity are connection
/// failures. They never invent an authority-bearing session reply.
type FrameFailure =
  | TruncatedPrefix | TruncatedBody | InvalidLength of uint32
  | UnexpectedFrameKind of byte | InvalidCorrelation of uint32
  | InvalidPayload | UnsupportedVersion of uint16
