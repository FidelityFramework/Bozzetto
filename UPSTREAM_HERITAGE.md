# Bozzetto Upstream Heritage

## From SageFs to Bozzetto

This project is a hard fork of [SageFs](https://github.com/WillEhrendreich/SageFs), the live F# development daemon created by Will Ehrendreich.

### Why "Bozzetto"

A *bozzetto* is the small model a sculptor shapes in clay or wax before starting the full work. The Italian word is the diminutive of *bozzo*, which means "sketch" or "rough stone". A patron sees the bozzetto and asks for changes while a change still costs little.

We chose the name for the working relationship: try an idea against the project, judge its result and revise it while changes are inexpensive. It also belongs beside Atelier: a bozzetto is made in the atelier.

## Original SageFs Project

- **Creator**: Will Ehrendreich
- **Repository**: https://github.com/WillEhrendreich/SageFs
- **License**: MIT
- **Fork point**: commit `5b685fb5ce3f5a90db595b457dee6d239634ba33`, 23 February 2026

We are grateful to Will for the foundation we build on. The daemon architecture and the hot reload engine in this repository are his work. SageFs credits [FsiX](https://github.com/soweli-p/FsiX) as its own inspiration, and we carry that credit forward.

## Fable.SageFs

[Fable.SageFs](https://github.com/shayanhabibi/Fable.SageFs), by Shayan Habibi, runs the Fable compiler inside a SageFs session. Its hackable mode loads Fable from source and patches the compiler's own transforms from the REPL.

Fable.SageFs supplied an early example of a compiler hosted and revised inside a live session. Bozzetto's current Composer worker uses explicit epoch retirement and replacement; that precedent does not authorize patching an active worker in place. The [development horizons](docs/Bozzetto_Development_Horizons.md) describe the broader direction.

## Fork Rationale

We forked because the Fidelity Framework toolchain has requirements of its own:

- **Independent implementation and application workflows.** Clef projects use explicit Composer sessions in Bozzetto. F#/.NET implementation work uses a separate SageFS service; native hosting remains a planned Fidelity objective.
- **One semantic authority.** For Clef, Bozzetto will read the Program Semantic Graph that the Clef Compiler Service publishes.
- **Embedded storage.** SQLite and DuckDB remain the fork's intended storage direction. The updated SageFs engine already replaced PostgreSQL with binary session/test manifests and uses SQLite for friction reports.

We made the Clef Compiler Service from the F# compiler service, and Lattice from Ionide, the same way.

## Coexistence with Upstream

Bozzetto now uses package `Bozzetto`, command `boz`, state directory `~/.bozzetto`, MCP port `47749` and dashboard port `47750`. SageFs and Fable.SageFs retain their separate identities. The separate SageFS service uses MCP port `37749` and dashboard port `37750`. The [identity migration checkpoint](docs/Bozzetto_Identity_Migration_Inventory.md) records the implementation and validation scope.

We track upstream SageFs and intend to take its later changes where they fit the fork.

## Upstream Updates

On 27 September 2026, Bozzetto integrated SageFs v0.6.834 at `c86c3402460543849e771e527aa75b5892770ea0`. The original fork point above remains part of our lineage. Upstream rewrote its history; the corresponding rewritten fork-point commit is `20300df21547d1f4057d24b0a499df68f0147a8e`. [The update record](docs/UPSTREAM_SYNC.md) explains the merge and its validation.

## Acknowledgments

The following credits were retained from the repository README when its focus moved to Fidelity's interactive development horizons:

- [SageFs](https://github.com/WillEhrendreich/SageFs), by Will Ehrendreich: the daemon architecture and inherited F# engine on which this fork began.
- [Fable.SageFs](https://github.com/shayanhabibi/Fable.SageFs), by Shayan Habibi: the compiler-residency and live compiler-editing reference described above.
- [FsiX](https://github.com/soweli-p/FsiX): the original F# Interactive experience credited by SageFs.
- [sagefs.nvim](https://github.com/WillEhrendreich/sagefs.nvim): the separate upstream Neovim plugin.
- [Falco](https://github.com/pimbrouwers/Falco) and [Falco.Datastar](https://github.com/spiraloss/Falco.Datastar): the dashboard framework.
- [Harmony](https://github.com/pardeike/Harmony): the inherited runtime method-patching foundation. Bozzetto's reviewed package comes from its separately controlled fork; see [repository guidance](AGENTS.md#owned-harmony-dependency).
- [Ionide.ProjInfo](https://github.com/ionide/proj-info/): project file parsing.
- [Raylib-cs](https://github.com/ChrisDill/Raylib-cs): graphics and game demos.
- [Fable](https://fable.io/): F# to JavaScript compilation for the retained VS Code extension.
- [ModelContextProtocol](https://modelcontextprotocol.io/): the agent integration standard.

Upstream also credits Jo Van Eyck's [fsi-mcp-server](https://github.com/jovaneyck/fsi-mcp-server) for demonstrating FSI access through MCP.

## License and Copyright

The repository retains its [MIT license](LICENSE), including **Copyright (c) 2025-2026 Will Ehrendreich** and the license's permission and copyright-preservation requirements. Moving attribution out of the README does not change that license or remove source and dependency notices.
