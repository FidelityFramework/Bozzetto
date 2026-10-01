# Changes and execution lifetime

Bozzetto coordinates file-change observation, edit reservation, compiler work
and owned process lifetime. Composer decides whether an artifact may execute.

A source edit first reserves the change and withdraws affected run authority.
The compiler then re-evaluates affected graph regions and proof premises before
accepting another artifact. Unaffected objects can be reused when their retained
contracts remain valid. A process restart cannot bypass those checks.

LLVM ORC replacement is a future Composer backend operation and must obey the
same admission, invalidation and cleanup contracts. Its implementation is not
established by file watching or by replacing a hosted worker process.

See [minimal hosting architecture](Minimal_Hosting_Architecture.md) for ownership
and native hosting requirements, and the [incremental foundation adoption
contract](Bozzetto_Incremental_Foundation_Adoption.md) for consumer acceptance.
