# Bozzetto Documentation

Bozzetto coordinates Fidelity development across compilers, editors, application runtimes and target devices. Its delivered workflow today is Clef/Composer development through shared MCP and browser sessions. The broader roadmap separates that foundation from proposed heterogeneous and federated development capabilities.

## Direction and delivery

- **[Development horizons](Bozzetto_Development_Horizons.md)**: first-horizon compiler/workspace and CPU REPL work, later heterogeneous development and federated sites; cross-target evaluation is research gated by demonstrated demand, feasibility and proof cost
- **[Fidelity component contracts](Bozzetto_Fidelity_Component_Contracts.md)**: proposed interfaces, component ownership and acceptance requirements; these are requests for agreement, not delivered APIs
- **[Clef/Composer development plan](Clef_Composer_Development_Plan.md)**: the concrete first-horizon delivery plan, implemented foundations and remaining integration work
- **[Incremental foundation adoption](Bozzetto_Incremental_Foundation_Adoption.md)**: Fidelity.FSharp.Incremental across Bozzetto and the compiler pipeline, cold work, shared demand and the path from .NET hosting to self-hosting
- **[Minimal host auditor checkpoint](Bozzetto_Minimal_Host_Auditor_Checkpoint_2026-10-01.md)**: architectural correction, purge scope, exact validation and deployment evidence
- **[Binary PSG integration auditor handback](PSG_Transport_Integration_Auditor_Checkpoint_2026-10-01.md)**: current source and deployed transport gap, cross-project ownership, repair sequence and acceptance controls
- **[Clefx host transition](Bozzetto_Clefx_Host_Transition_2026-10-01.md)**: removal of embedded production FSI hosting, retained inherited host code and the future Clef execution boundary

## Start here

- **[Get Started](../Readme.md#get-started)**: use the reviewed installation, connect to the shared daemon, then open, reserve, build and run a Clef project
- **[Using Bozzetto with AI agents](agents.md)**: connect MCP and follow the Composer session workflow
- **[MCP Tools](mcp-tools.md)**: Composer operations and the retained tool catalog
- **[Live provider checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md)**: deployment, endpoints and recorded acceptance
- **[Deployment correction](Bozzetto_Live_Provider_Audit_Response_2026-09-30.md)**: corrected launcher/status deployment following the live audit
- **[Independent incremental workflow assessment](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md)**: cold build, unchanged rebuild, one-function edit and run-authority withdrawal
- **[Compiler continuation follow-up](Bozzetto_Compiler_Continuation_Followup_2026-09-30.md)**: compiler promotion history, the October 1 repeat and remaining compiler boundaries

## Retained F# engine and editor references

The F# engine described here is retained host code, not a current product surface. These guides preserve the inherited F# implementation and its historical behavior. The [Clefx host transition](Bozzetto_Clefx_Host_Transition_2026-10-01.md) retires embedded production FSI hosting in the checkout; these references do not promise F# execution through current Bozzetto source. They are not prerequisites for opening a Composer `.fidproj`.

- **[Workflow Modes](workflow-modes.md)**: retained F# REPL and Live Testing contracts
- **[Hot Reload](hot-reload.md)**: change admission and compiler-owned execution
- **[Live Testing As You Type](live-testing-as-you-type.md)**: the feedback pipeline
- **[Multi-Session](multi-session.md)** and **[Session Isolation](session-isolation.md)**: F# workers and session boundaries
- **[Feature Matrix](FEATURE_MATRIX.md)**: inherited capabilities across VS Code, Neovim and MCP
- **[Ecosystem compatibility](ecosystem-compatibility.md)**: .NET web frameworks, Fable, AOT and related integration limits
- **[SSE Events](sse-events.md)**: events consumed by editor integrations
- **[Troubleshooting](TROUBLESHOOTING.md)**: inherited host, runtime and editor issues
- **[Why F#?](why-fsharp.md)** and **[samples](../samples/README.md)**: language background and examples, including the retained Raylib application demos

## Implementation reference

- **[Minimal hosting architecture](Minimal_Hosting_Architecture.md)**: compiler ownership, justified host facilities and the self-hosting boundary

- **[System Architecture](architecture.md)**: daemon, F# workers and inherited MCP surface
- **[Binary Format Spec](binary-format-spec.md)** and **[benchmarks](binary-format-benchmarks.md)**: session/test persistence
- **[Contributing Guide](../CONTRIBUTING.md)** and **[agent guidelines](../AGENTS.md)**: development workflow, testing and coding standards
- **[Architecture Decision Records](architecture-decisions.md)**: persistence, typed errors, MCP and prior frontend decisions
- **[Live Testing Guide](LIVE_TESTING_GUIDE.md)**: implementation details of the test pipeline
- **[Features Survey](FEATURES_SURVEY.md)**: module inventory
