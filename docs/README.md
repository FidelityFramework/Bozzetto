# Bozzetto Documentation

Bozzetto's current workflow is Clef/Composer development through shared MCP and browser sessions. F#/.NET development uses a separate SageFS service; the inherited engine's technical references remain available below.

## Start here

- **[Get Started](../Readme.md#get-started)**: use the reviewed installation, connect to the shared daemon, then open, reserve, build and run a Clef project
- **[Using Bozzetto with AI agents](agents.md)**: connect MCP and follow the Composer session workflow
- **[MCP Tools](mcp-tools.md)**: Composer operations and the retained tool catalog
- **[Live provider checkpoint](Bozzetto_Live_Provider_Checkpoint_2026-09-30.md)**: deployment, endpoints and recorded acceptance
- **[Deployment correction](Bozzetto_Live_Provider_Audit_Response_2026-09-30.md)**: corrected launcher/status deployment following the live audit
- **[Independent incremental workflow assessment](Bozzetto_Incremental_Workflow_Auditor_Assessment_2026-09-30.md)**: cold build, unchanged rebuild, one-function edit and run-authority withdrawal
- **[Clef/Composer development plan](Clef_Composer_Development_Plan.md)**: delivered foundations and remaining editor, ORC and self-hosting work

## Retained F# engine and editor references

These guides describe the inherited F# implementation and compatibility surface. They are not prerequisites for opening a Composer `.fidproj`.

- **[Workflow Modes](workflow-modes.md)**: REPL, Live Testing and Hot Reload
- **[Hot Reload](hot-reload.md)**: file watching, FSI evaluation, Harmony patching and current limits
- **[Live Testing As You Type](live-testing-as-you-type.md)**: the feedback pipeline
- **[Multi-Session](multi-session.md)** and **[Session Isolation](session-isolation.md)**: F# workers and session boundaries
- **[Feature Matrix](FEATURE_MATRIX.md)**: inherited capabilities across VS Code, Neovim, the dashboard and MCP
- **[Ecosystem compatibility](ecosystem-compatibility.md)**: .NET web frameworks, Fable, AOT and related integration limits
- **[SSE Events](sse-events.md)**: events consumed by editor integrations
- **[Troubleshooting](TROUBLESHOOTING.md)**: inherited host, runtime and editor issues
- **[Why F#?](why-fsharp.md)** and **[samples](../samples/README.md)**: language background and examples, including the retained Raylib application demos

## Implementation reference

- **[System Architecture](architecture.md)**: daemon, F# workers, dashboard and inherited MCP surface
- **[Binary Format Spec](binary-format-spec.md)** and **[benchmarks](binary-format-benchmarks.md)**: session/test persistence
- **[Contributing Guide](../CONTRIBUTING.md)** and **[agent guidelines](../AGENTS.md)**: development workflow, testing and coding standards
- **[Architecture Decision Records](architecture-decisions.md)**: persistence, typed errors, MCP and prior frontend decisions
- **[Live Testing Guide](LIVE_TESTING_GUIDE.md)**: implementation details of the test pipeline
- **[Features Survey](FEATURES_SURVEY.md)**: module inventory
