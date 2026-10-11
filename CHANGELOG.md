# Changelog

Bozzetto 0.1.0 is unreleased. Earlier history is in git.

## 0.1.0 (unreleased)

- Bozzetto is the development controller for the Fidelity Framework's Clef and Composer work: one daemon on port 47749 for MCP, the HTTP API and the browser, and 47750 for control, supervising one Composer worker.
- The daemon and the Composer worker deploy as a matching pair from one build, checked by their shared PSG contract fingerprint.
- Build and test leases renew on a heartbeat, keep one holder per agent and project, and show on the Composer page.
- The browser bridge derives health, leases, the agent board and the operations journal on Fidelity.FSharp.Incremental, in Ranvier's shape, and pushes each fact as it settles.
- The operations journal records every caller's Composer operations and run output.
- The Composer page shows each accepted build's regions with their witness and object states.
- Source lives under `src/`. Build contracts, the pipeline's tier plan and lease client, the quality matrix, the agent skill and the agent hooks live under `tools/`.
- Removed: embedded F# session hosting, runtime patching, the Raylib GUI, the Visual Studio extension, SageTUI, the demo recorder, the samples, the shell scripts and the inherited SageFs release history.
- Bozzetto began as a fork of SageFs; the license keeps the original copyright notice.
