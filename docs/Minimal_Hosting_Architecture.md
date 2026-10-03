# Minimal hosting architecture

Bozzetto coordinates compiler work, editor demand and owned processes. Its
portable contract is the ordering of those operations, their authority and their
lifetime. The present .NET implementation supplies an execution environment for
that contract; self-hosting must preserve the contract when the environment changes.

## Responsibilities

| Owner | Responsibility |
| --- | --- |
| CCS/Baker | Source semantics, elaboration, saturation, SSA and ABI settlement, proof premises and invalidation scope. |
| Fidelity.PSG | The published revision containing settled graph facts and their relationships. Structural integrity checking does not confer proof or execution authority. |
| Alex | Passive composition of elements, patterns and witnesses from published facts. |
| Composer | Backend lowering, proof-dependent artifact acceptance, incremental region/object reuse and authority to launch an accepted artifact. |
| Fidelity.FSharp.Incremental | Dependency and demand bookkeeping, invalidation, result eligibility and explicit work lifetime. It cannot certify compiler semantics. |
| Bozzetto | Shared workspace orchestration, request admission, edit reservation, cancellation, observation and joined process cleanup. |
| Lattice/editor adapters | Editing protocols and presentation of compiler-owned evidence. A second editor cache must not become an independent compiler authority. |

## The host boundary

Each retained host facility needs a concrete caller and a bounded purpose.
These are obligations for native implementations, not promises that the native
implementation already exists.

| Present facility | Targeted purpose | Native replacement must preserve |
| --- | --- | --- |
| FSharp.Core and the .NET runtime | Execute the current F# implementation and its typed data structures. | Clef's own types and semantics; no CLR representation may silently become a language rule. |
| F# Async, Task adapters, cancellation tokens and synchronization primitives | Start explicitly demanded work; serialize shared state; distinguish cancellation from failure; drain owned work. | Cold admission, shared-demand cancellation, exact completion classification and cleanup before close completes. |
| Process, stream, file and watcher APIs | Launch owned tools/artifacts, exchange bounded messages, detect edits and manage leases. | Reservation before source mutation, authority checked at actual launch, bounded framing, process identity and joined cleanup. |
| ASP.NET and MCP hosting | Serve browser/editor/agent protocols. | The same typed workspace operations and authority checks, regardless of transport. External JSON protocols do not determine the internal graph representation. |
| Diagnostics and logging | Observe lifecycle and failures. | Observation cannot authorize work, change proof outcomes or hide cleanup failure. |

FSharp.Compiler.Service, Ionide project loading, Fantomas parsing and managed
test instrumentation serve retained F# implementation tooling. They are not
Clef semantic dependencies. Their current presence in a shared assembly is a
packaging boundary still to reduce, not a requirement for the native compiler.
In particular, Mono.Cecil's retained use is coverage instrumentation of managed
test assemblies in a shadow workspace. That mechanism must never rewrite a
Composer artifact or serve as an application update strategy.

The retained F# compiler-service API currently requires its matching preview
FSharp.Core package. Those packages target .NET Standard; they do not require
a .NET 11 runtime. The daemon and worker run on stable .NET 10. Separating this
F# tooling closure from Composer orchestration remains a packaging task; it
must not become a requirement on Clef programs or native hosting.

## Integrity across a change

An edit reservation withdraws affected execution authority before the source
write. Invalidation follows the compiler's graph dependencies and proof premises.
Unchanged regions may retain witnesses and objects only when their complete
dependencies remain valid. A new result becomes usable through the owning
compiler's acceptance gate. Cancellation of an observer is distinct from
termination of shared work; closing an owner must join its children.

Process replacement and future LLVM ORC operations belong behind that same
authority boundary. They do not manufacture semantic facts or bypass stale
proofs. Unsupported operations return a diagnostic.

## Remaining integration boundaries

The PSG binary codec and provider transport must carry the actual published
revision under one versioned contract. A control-channel handshake alone does
not establish that integration. Likewise, shared editor/build authority and
native ORC hosting require consumer acceptance tests; this architecture does
not claim those future gates have passed.

> Provenance: Bozzetto inherited SageFS's F# tooling foundation. Its compiler
> orchestration purpose led to a narrower execution surface: runtime method
> detours and their source/browser injection machinery are excluded. This is a
> targeted ownership decision; ordinary managed hosting remains useful where
> its role and lifetime are explicit.
