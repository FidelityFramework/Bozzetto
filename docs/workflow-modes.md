# Workflow contracts

Composer work uses explicit project sessions, change reservation, build, status,
run and close operations. The compiler owns artifact eligibility; selecting a
UI mode never changes that authority.

The retained F# implementation model has two workflow values:

- **Interactive**: evaluate F# implementation code.
- **LiveTesting**: observe affected managed tests while developing that implementation.

These are compatibility contracts for F# tooling. They are not Clef execution
backends. Embedded production FSI hosting is retired; the Composer provider
never converts a Clef request into an FSI evaluation.

The parser accepts explicit supported names and returns an error for unsupported
modes. Switching workflows preserves session ownership and records any restart
cost; it cannot enable runtime method patching.

See [minimal hosting architecture](Minimal_Hosting_Architecture.md) for the
portable obligations and host boundaries relevant to self-hosting.
