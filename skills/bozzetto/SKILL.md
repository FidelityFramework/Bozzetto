---
name: bozzetto
description: Work on Bozzetto’s F# implementation or use its Clef/Composer development controller, with the shared daemon’s build and test leases.
license: MIT
---

# Working with Bozzetto

Read the checkout’s [agent guidelines](../../AGENTS.md) first. They own the
engineering principles, retrieval discipline, supported clients, validation
posture and daemon lifecycle. Use this skill to select the current workflow.

## Clef and Composer projects

Use the shared daemon’s `composer_*` tools and `/composer` page on ports
47749/47750. Open an explicit `.fidproj`, reserve before editing, build with
that reservation, and execute through `composer_run_current`.

Embedded F# session hosting is retired. Creating, resuming or rebuilding an
inherited F# session is refused; do not retry it or substitute an F# REPL.
LLVM ORC JIT remains a planned backend. See the
[host transition](../../docs/Bozzetto_Clefx_Host_Transition_2026-10-01.md).

## Bozzetto’s own F# implementation

No project session is needed. Use targeted owner/consumer checks during
implementation and one unfiltered acceptance run under a work lease. Read the
`TRUST` result as described in the agent guidelines.

```bash
scripts/work-lease run full_build dotnet build
scripts/work-lease run test_suite_run dotnet Bozzetto.Tests/bin/Debug/net10.0/Bozzetto.Tests.dll --summary
```

With MCP, acquire `acquire_full_build_lease` or `acquire_test_suite_lease`,
proceed only when granted, and release the exact `leaseId` with the same
connection after the work finishes, including after failure. On a deferred or
refused lease, follow the memory-pressure procedure in the agent guidelines.

Preserve the running shared daemon. A newer checkout does not establish that
the installed daemon is current. For connection, worker and deployment binding,
read the [live checkpoint](../../docs/Bozzetto_Live_Provider_Checkpoint_2026-09-30.md).
Starting or replacing the shared daemon is an owner-triggered operational step.

When delegating F# work, include the governing source, retrieval sequence,
direct-read exception where applicable, growth rule, matching lease procedure
and acceptance requirement in the brief.
