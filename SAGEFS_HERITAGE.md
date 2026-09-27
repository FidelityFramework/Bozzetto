# SageFs Heritage

## From SageFs to Bozzetto

This project is a hard fork of [SageFs](https://github.com/WillEhrendreich/SageFs), the live F# development daemon created by Will Ehrendreich.

### Why "Bozzetto"

A *bozzetto* is the small model a sculptor shapes in clay or wax before starting the full work. The Italian word is the diminutive of *bozzo*, which means "sketch" or "rough stone". A patron sees the bozzetto and asks for changes while a change still costs little.

A SageFs session already works that way: a definition is evaluated against the live project, judged on its result and revised before any build is cut. We chose the name for that working relationship and for its place beside Atelier: a bozzetto is made in the atelier.

## Original SageFs Project

- **Creator**: Will Ehrendreich
- **Repository**: https://github.com/WillEhrendreich/SageFs
- **License**: MIT
- **Fork point**: commit `5b685fb5ce3f5a90db595b457dee6d239634ba33`, 23 February 2026

We are grateful to Will for the foundation we build on. The daemon architecture and the hot reload engine in this repository are his work. SageFs credits [FsiX](https://github.com/soweli-p/FsiX) as its own inspiration, and we carry that credit forward.

## Fable.SageFs

[Fable.SageFs](https://github.com/shayanhabibi/Fable.SageFs), by Shayan Habibi, runs the Fable compiler inside a SageFs session. Its hackable mode loads Fable from source and patches the compiler's own transforms from the REPL.

Fable.SageFs showed us a compiler hosted and revised inside a live session. We intend the same use for the Clef Compiler Service and Composer in Bozzetto.

## Fork Rationale

We forked because the Fidelity Framework toolchain has requirements of its own:

- **Two kinds of source.** During the transition to a self-hosted compiler, a session will serve F# and Clef together.
- **One semantic authority.** For Clef, Bozzetto will read the Program Semantic Graph that the Clef Compiler Service publishes.
- **Embedded storage.** SQLite and DuckDB remain the fork's intended storage direction. The updated SageFs engine already replaced PostgreSQL with binary session/test manifests and uses SQLite for friction reports.

We made the Clef Compiler Service from the F# compiler service, and Lattice from Ionide, the same way.

## Coexistence with Upstream

Bozzetto will take its own package name, command, state directory and default ports. SageFs and Fable.SageFs will then run beside it on one machine.

We track upstream SageFs and intend to take its later changes where they fit the fork.

## Upstream Updates

On 27 September 2026, Bozzetto integrated SageFs v0.6.834 at `c86c3402460543849e771e527aa75b5892770ea0`. The original fork point above remains part of our lineage. Upstream rewrote its history; the corresponding rewritten fork-point commit is `20300df21547d1f4057d24b0a499df68f0147a8e`. [The update record](docs/UPSTREAM_SYNC.md) explains the merge and its validation.
