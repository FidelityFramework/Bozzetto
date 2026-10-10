# bozzetto-web

Bozzetto's browser UI: separate operations dashboard and Composer pages. It is an
F# (Partas.Solid) single-page app compiled by Fable, bundled by Vite into one
self-contained HTML page, and welded into the daemon, which serves it and
talks to it over one WebSocket.

The MCP listener (normally port 47749) serves two route-selected views from
one bundle, with shared styling, navigation, bridge protocol and security:

- `/dashboard`: explicitly requested, bounded Linux whole-host observations
  (CPU, RAM/swap, pressure, available GPU counters and external processes), live
  global agent work and work leases, and a labelled connection-time daemon
  snapshot. `boz rss` is the daemon reading, not total host memory.
- `/composer`: Clef project sessions, compiler worker and run output. Global
  agents and leases belong to Dashboard and are not duplicated here. Every
  `composer_open_project` call uses the same supervisor. The opener is under
  **Open project**. Build/run controls remain available.

Each view has its own browser title and connection, so both can remain open
side by side without repeating global panels. Navigation uses ordinary links,
which can also open in another tab. Reservations and command outcomes remain page-local: use the same
Composer tab to reserve and build; the split does not fix the reservation
reporting gap documented below. There is no Refresh button: reconnect restores
current shared state, Composer changes push automatically, and leases push on
pool mutations or their next known expiry. Legacy health remains explicitly
labelled as a connection-time snapshot; live host observation is separate.

Bookmarks for either path on the control listener (normally port 47750)
redirect to the corresponding view. Composer's `/api/composer/*` HTTP contracts
remain available to local clients; there is no separate JSON display page.

It follows the destination architecture of
[WrenHello](../../WrenHello/README.md), the reference WREN Stack application.
Here, .NET Bozzetto is the backend-for-frontend in place of WrenHello's
native Clef backend compiled by Composer.

```
┌────────────────────────────────────────────────────────────┐
│ Frontend (this directory)  │  Backend (Bozzetto daemon)    │
│ Partas.Solid → Fable → JSX │  .NET today: ComposerSupervisor│
│ → Solid/Vite, DaisyUI      │  health, leases               │
│ Solid stores = the model   │  = the update function        │
├────────────────────────────────────────────────────────────┤
│  src/Shared/Protocol.fs: Command / Event, types only       │
│  one WebSocket on the daemon's MCP port: /ui/bridge        │
│  (today: JSON text frames; roadmap: BAREWire binary)       │
└────────────────────────────────────────────────────────────┘
```

## Architecture

- **One vocabulary.** [`src/Shared/Protocol.fs`](src/Shared/Protocol.fs) holds
  the `Command` (UI → backend) and `Event` (backend → UI) unions and their
  payload records. It has types only and no behavior. Its shapes stay inside
  Composer's proven surface (BAREWire `docs/12 Intersection Subset.md`,
  WrenHello `docs/composer-findings.md`) so that Composer can compile the same
  file once the backend moves to Clef. That means one payload per case
  (records, never tuples or named case fields), at least two cases per union,
  arrays rather than lists, no Map, and int32/int64 rather than `int` on the
  wire.
- **Codecs per side.** [`src/Frontend/Codec.fs`](src/Frontend/Codec.fs)
  encodes `CommandFrame`s and decodes `Event`s. The backend's codec is written
  inside Bozzetto. [`src/Mock/Codec.fs`](src/Mock/Codec.fs) is its Fable twin,
  used by the mock bridge and the tests, and is the reference for the .NET
  codec. When BAREWire compiles under both Fable and Composer, the codecs swap
  to binary framing; `Protocol.fs` and the UI stay as they are.
- **MVU as a metaphor.** There is no Elmish. Solid stores are the model
  ([`Model.fs`](src/Frontend/Model.fs)), the backend is the update function,
  `dispatch` sends a `Command` over the bridge, and each `Event` folds into the
  stores. Snapshots go in through `reconcile`, keyed by `id`, so only the DOM
  that reads a changed field updates. State that belongs only to the UI (the
  theme, the link status, card inputs, the outcome log) stays local, like
  WrenHello's theme.
- **One bridge.** [`Interop.fs`](src/Frontend/Interop.fs) holds the JS ends
  as one-liners. [`Bridge.fs`](src/Frontend/Bridge.fs) owns a single WebSocket
  to `/ui/bridge` on the page's own origin and reconnects with capped backoff
  (1, 2, 4, 8, then every 10 s). That backoff is the page's only timer: it
  retries a dropped socket and polls nothing. State events are pushed only
  when something changed. Pulls are Commands: `RequestSnapshot` is answered
  by Events. The page keeps no clock: times are shown as instants ("since",
  "until"). The one duration shown, the daemon's uptime, is the value the
  daemon's own clock pushes each second (`Uptime`), held in a Solid signal.

### Store rules (Solid)

- Store contents are F# **anonymous records**, which Fable emits as plain JS
  objects. Solid proxies only plain objects and arrays. An F# record or union
  is a class instance, which a store treats as an opaque leaf, and `reconcile`
  could not diff it field by field.
- Setting a store path to an object **merges** into the existing node, and
  `reconcile` mutates nodes in place. Never put one object at two store
  locations, and never store shared constants (placeholders are functions).
- No `option` inside stores: use `hasCurrent`/`current` and
  `healthSeen`/`health` pairs.
- Partas compiles `[<SolidComponent>]` functions to plain functions, and a
  call inside JSX runs inside Solid's insert effect. A component therefore
  reads reactive state only in JSX expressions and handlers, never in its
  setup code.

### Look (Braidpoint)

The UI wears the Braidpoint site's look (`braidpoint-site/hugo`):

Edit [`theme.js`](theme.js) for **all browser colors and font families**.
[`tailwind.config.js`](tailwind.config.js) consumes it for DaisyUI's themes
and semantic utilities; Vite consumes it for the first painted background.
The page shell, sessions, worker, leases, output and diagnostics all use
that shared theme. [`styles.css`](src/Frontend/styles.css) owns reusable
treatments and [`App.fs`](src/Frontend/App.fs) owns layout and components;
neither defines a palette. The saved light/dark choice and reduced-motion
behavior remain local presentation settings. Keep the workplace surface quiet:
state, work, data and actions first; no architectural/editorial banners. Useful
measurement and accounting qualifications belong behind a small ⓘ control.
The native information dialog owns modal focus, Escape/Close dismissal and
focus restoration; it uses the same theme and no additional dependency.
Closed dialogs explicitly use `display: none`: the modal component's grid must
not expose hidden text or Close buttons to the accessibility tree. Agent rows
show **reported** status and the last update instant, not observed process
liveness. Dashboard health/pressure/alarms remain visible outside the detailed
connection-time snapshot.

Diagnostics keep their exact source text, newlines and tab indentation in
readable trace panels. Headings describe the existing status field; bodies
use `text-base-content`. The small presentation tokenizer recognizes only
explicit line-start severity labels and Composer's emitted location/code
format. Error, warning and info markers use `text-error`, `text-warning`
and `text-info` from the same theme. It never infers severity from message
wording, creates source locations, or interprets diagnostic text as HTML.

Future views use the same semantic classes (`bg-base-200`, `text-accent`,
`badge-info`, `btn-plum`, and the shared treatments). Add a role in
`theme.js` when needed; do not put hex/RGB colors or an independent Tailwind
color family in a component. Bundle verification covers every frontend
`.fs` file as well as the shell and stylesheet to keep that rule enforceable.

After a theme or component edit, run `../scripts/work-lease run full_build
npm run build:daemon` from this directory to compile, verify and regenerate
the daemon's [`WebAssets.fs`](../Bozzetto/WebAssets.fs). Rebuild Bozzetto
under its build lease to include that generated page. Do not edit the
generated file. The bundle check validates both themes' logo properties
and the generated first-paint colors against `theme.js`, and rejects copied
palette literals in the frontend components, HTML, stylesheet or Tailwind
configuration.

- **Themes.** Its DaisyUI `dark` and `light` themes, colors unchanged
  ([`theme.js`](theme.js)): dark base-100 `#1a1a1a`,
  base-200 `#242424`, base-300 `#2e2e2e`, text `#eaeaea`; light base-100
  `#ffffff`. Only `color-scheme` is added. The ☀/☾ toggle switches between
  them, and `data-theme` on `<html>` stays the single authority, as on that
  site; the choice is kept in `localStorage`.
- **Surfaces.** A Braidpoint nav bar (base-100, a shadow and one subtle
  border), and cards and run transcripts as its code blocks are: a slightly
  raised base-200 panel with one 1 px `base-content/10` border and rounded
  corners (`.panel` in [`styles.css`](src/Frontend/styles.css)). Hashes are
  set as its inline code (orange on a raised chip).
- **Type.** Montserrat for text, Nunito for headings and Fira Code (with
  ligatures) for code, as font stacks of local faces with fallbacks: Nunito
  falls back to Montserrat, then `system-ui`; Fira Code to JetBrains Mono,
  Cascadia Code, `ui-monospace`. No font is embedded or fetched, so the page
  stays self-contained and small, and no package was added.
- **Logo palette.** From `static/images/BraidpointcolorLogo_small.svg`:
  burnt-orange ring `#ba530d`, orange `#ec6911`, rust red `#be350e`, teal
  `#468f99`, slate blue `#315182`, plum `#6b144c`. They are theme-aware
  tokens (defined in `theme.js`, exposed as `bp-*` in Tailwind): the exact color as a fill under white text,
  and an `-ink` shade that reads as text on the theme's background (the color
  itself on white; a lighter tint of the same hue on `#1a1a1a`).

Accents by role:

| Role | Color | Where |
|---|---|---|
| Primary actions | brand orange `#f58220` (theme `accent`, the orange of Braidpoint's outlined Smart Search button) | Open project (filled), Build, Run, Refresh (outlined), the selected output tab |
| State and information | teal (theme `info`) and blue (theme `secondary`) | teal: worker running, session busy, lease held, input focus; blue: a command in flight, the link connecting |
| Reservations | plum `#6b144c` | Reserve, the reservation badge |
| Destructive actions | rust `#be350e` | Close a session, Retire the worker (outlined; filled once armed) |
| Success and accepted artifacts | green (theme `success`) | the accepted artifact, a healthy daemon, a link up, exit 0, accepted outcomes |
| Warnings and failures | theme `warning` (orange) and `error` | degraded health, memory pressure, queued leases, revocation and cleanup; refusals, failed runs, a dropped link |

**Tab icon.** The Clef logo (from `clef-lang-site/hugo/static`), linked as
clef-lang.com links it: `favicon.ico`, and `favicon.svg`, which the app swaps
for `favicon-dark.svg` while the system color scheme is dark. A `data:` URI
would carry the SVG namespace URL the bundle check rejects, so the files live
in [`icons/`](icons) and the daemon embeds and serves them beside the page
(`/favicon.svg`, `/favicon-dark.svg`, `/favicon.ico`); the page links them
relatively and its policy admits `img-src 'self'`. The dev proxy forwards
them to the daemon and the mock serves them from `icons/`.

## Protocol

| Command | Payload | MCP equivalent |
|---|---|---|
| `RequestSnapshot` | none | n/a |
| `OpenProject` | absolute `.fidproj` path | `composer_open_project` |
| `Reserve` | `ReserveEdit { Target; Label }` | `composer_reserve_edit` |
| `Build` | `BuildReserved { Target; Reservation }` | `composer_build` |
| `Run` | `RunCurrent { Target; Arguments }` | `composer_run_current` |
| `Cancel` | `SessionTarget` | `composer_cancel` |
| `CloseSession` | `SessionTarget` | `composer_close_session` |
| `RetireWorker` | `WorkerTarget` | `composer_retire_worker` |

Every command travels as `CommandFrame { Correlation; Command }`. The UI
picks the correlation: positive, and unique for the page's lifetime.

| Event | Payload | When |
|---|---|---|
| `Welcome` | protocol version, daemon version, daemon start time | first, once per connection |
| `Snapshot` | `ComposerSnapshot`: revision, worker state, sessions | on connect, on every supervisor change, on request |
| `Accepted` | correlation, target authority, `Completion` | exactly one per command, or a `Refused` |
| `Refused` | correlation, target, Composer refusal code, message | as above; correlation 0 = undecodable frame |
| `RunOutput` | transcript: generation, exit code, stdout, stderr | before the `Accepted` of a `Run` |
| `Health` | version, pid, verdict, memory pressure, processes, alarms; RSS, machine memory and CPU as read when sent | connection/request snapshot; not live service health |
| `Leases` | active grants and queued requests | on connect, on request, on a pool change (see below) |
| `Uptime` | `UptimeTick`: the daemon's uptime in ms | once a second from the daemon's clock, while at least one page is connected |
| `Resources` | sequenced Linux host metrics, process incarnations and acquisition coverage | only within an explicit 1–120 second observation window |
| `AgentWork` | daemon incarnation, publication sequence, bounded per-run reports, activity and optional usage/price | on connect/resync and common cohort-owned publication changes; client reports are not tool/test/audit acceptance |

**Interim wire format:** one JSON object per WebSocket text frame. The full
grammar is in the header of `Protocol.fs`. int64 values travel as decimal
strings and int32/float as numbers. An option is `null` or the value. A
`SessionTarget` is flattened to `{"host","epoch","session"}`. Commands look
like `{"correlation":7,"command":"reserve","target":{…},"label":"…"}` and
events like `{"event":"accepted","correlation":7,"target":{…},"completion":{"kind":"edit_reserved","reservation":"…"}}`.

Project and lease updates are owner-driven, not scan-and-diff. Counter-only
host metrics use the explicit bounded window described below. The start time
travels once, in `Welcome`, as
`startedAtMs`, because it is fixed for the daemon's lifetime like its version,
and a restart drops the socket so the reconnect's `Welcome` carries the new
one. `Uptime` (protocol version 3) is the one clock-owned push: the bridge's
hub runs a single 1 Hz clock that starts when the first page connects and
stops when the last one closes, and each tick is pushed to every page through
that page's writer as it is. A tick reads nothing else and diffs nothing, so
it is not a poll. The mock bridge does the same.

**Known gap:** the backend does not report reservation tokens back yet. The UI
keeps the token it was given in a page-local store, so a reload or another tab
cannot build that reservation.

LeaseWatch now publishes pool changes; the bridge relays only that changed
slice through its existing coalesced slot. One pool-owned, one-shot deadline
publishes expiry without a periodic scan. This relay introduces no second
admission or invalidation engine. Broader shared-foundation adoption remains
separate work; this does not claim a completed monitoring graph.

`work-lease run` labels its lease with `BOZZETTO_AGENT` (default `caller`) and
`BOZZETTO_PROJECT` (default current directory). These are caller-reported
context, not authenticated identity, evidence of test success, or a persistent
project registry. Composer's MCP authority remains connection/session-bound.
External component builds are visible while admitted, but do not become Clef
Composer sessions. Their result/evidence ingestion is still outstanding.

## Develop

Prerequisites: the .NET 10 SDK (repository `global.json`) and Node.js 20.19+.

```bash
cd bozzetto-web
dotnet tool restore    # Fable 5.0.0-alpha.14, pinned here only
npm install
```

In this repository, run every Fable or Vite build under a work lease, for
example `../scripts/work-lease run full_build npm run build`. Exit 75 means
deferred: stop and inspect the admission reason and owned queued request. Do
not poll, bypass FIFO or leave repeated fresh-holder requests behind. The
current native API has no queued-request withdrawal operation.

| Command | What it does |
|---|---|
| `npm run fable` | Fable: `src/Frontend` → `output/*.fs.jsx` |
| `npm run fable:watch` | Fable in watch mode, for use alongside `npm run dev` |
| `npm run dev:mock` | Fable-compiles the mock, then Vite with an in-process mock `/ui/bridge` |
| `npm run dev` | Vite, proxying `/ui/bridge` to `$BOZZETTO_DAEMON` (default `http://127.0.0.1:47759`) |
| `npm run preview:mock` | Serves the **built** `dist/index.html` with the mock bridge |
| `npm test` | Codec round trip under node, for every Command and Event case plus malformed frames |
| `npm run test:pages` | Chromium mock checks: populated agent rows and usage distinctions, route separation, visible health, information-dialog visibility/accessibility/focus, caught stylesheet mutation, navigation/shared-owner command/reload and 900/480 px layouts |
| `npm run build` | Fable → `vite build` → verify → weld |
| `npm run build:daemon` | The same verified build, welded into `../Bozzetto/WebAssets.fs` for the daemon |

The page checks need `npm run build` and `npm run fable:mock` first (under a
`full_build` lease), then `../scripts/work-lease run test_suite_run npm run
test:pages`. They use the installed `chromium` (`CHROMIUM` can override the
executable), own a preview server and browser for at most 45 seconds, and
leave screenshots/logs under `${XDG_CACHE_HOME:-$HOME/.cache}/bozzetto/page-checks/`.
They never start or replace a Bozzetto daemon. This is a browser check, not an
Expecto TRUST verdict.

**Against the mock.** Run `npm run fable && npm run dev:mock` and open the
printed URL. Any absolute `*.fidproj` path opens. A path containing `broken`
builds into a compiler refusal, and `"--fail"` among the run arguments exits
with code 3. Reservations can be used once. A build holds a `full_build` lease
and a run a `run_app` lease, one of each kind at a time, so a second build
queues. The mock pushes Leases on every grant, queue and release, and Health
when its verdict, process set or alarms change. Its state is shared by every
tab, as the daemon's is.

**Against a daemon.** Bring up a dedicated dev daemon (never the installed one
on 47749/47750), then run
`BOZZETTO_DAEMON=http://127.0.0.1:47759 npm run dev`. The proxy rewrites Host
and Origin to the daemon's own values, because the daemon's origin guard
refuses foreign origins. It does this **only** for the Vite page itself
(loopback Host, matching Origin). Any other origin is forwarded unchanged, so
the guard still refuses it, and a website you visit cannot reach the daemon
through the proxy.

## Build and weld

`npm run build` writes `dist/index.html`: one self-contained page with all
scripts, styles and assets inlined, about 143 KiB (36 KiB gzipped).
[`scripts/verify-bundle.js`](scripts/verify-bundle.js) fails the build if
`dist/` holds anything else or if the page contains any `http(s)://` URL. It
rejects even XML namespace strings, which is why the UI uses a CSS spinner
instead of DaisyUI's mask-based one. The only reference it admits is a
relative `<link rel="icon">` to one of the three tab icons the daemon serves.

[`scripts/weld.js`](scripts/weld.js) freezes the page into an F# module with
a `[<Literal>] IndexHtml` triple-quoted string, as WrenHello does for its
native binary. By default it writes `dist/EmbeddedAssets.fs` (module
`Bozzetto.Web.EmbeddedAssets`). The backend chooses where the file goes:

```bash
npm run build:daemon
# or, after an already verified build:
node scripts/weld.js --out ../Bozzetto/WebAssets.fs --module Bozzetto.Server.WebAssets
# or: BOZZETTO_WEB_WELD_OUT=… BOZZETTO_WEB_WELD_MODULE=… npm run weld
```

A backend that prefers an embedded resource can take `dist/index.html` as it
is.

## Isolation from the repository build

`Directory.Build.props` and `Directory.Packages.props` here replace the
repository root's files for this subtree. The root pins Fable.Core 4.5.0 for
`bozzetto-vscode` (Fable 4.29), and Partas.Solid 2.1.3 needs Fable.Core
5.0.0-beta.1 or later, which would otherwise raise NU1605. The subtree keeps
central package management, net10.0, warnings as errors (Fable honours them)
and committed `packages.lock.json` files. The Fable CLI is pinned to
5.0.0-alpha.14 in `.config/dotnet-tools.json`, because the published
Partas.Solid.FablePlugin 2.1.3 is ABI-bound to Fable.AST 5.0.0-beta.2. Partas
comes from NuGet, not from a local checkout.

Why the frontend codec does not use Fidelity.Data.JSON: under Fable
5.0.0-alpha.14, its `JsonParser.fs` fails to emit. The lone-surrogate char
literals `'\uD800'`…`'\uDFFF'` cannot be written as JavaScript output. Its
TOML and XML parsers also fail FS0748 on `return { … } : T`. With those
literals rewritten as `char 0xD800` in a scratch copy, the JSON subset
round-trips exact int64/uint64 extremes and non-ASCII text under node, so a
small upstream fix would make it usable here.

## Backend work (not in this directory)

1. **Bridge endpoint.** Accept a WebSocket `GET /ui/bridge` on the MCP port
   (`UseWebSockets`) behind the existing origin guard. Use UTF-8 text frames
   with a size cap (the Composer routes use 1 MiB) and refuse binary frames
   for now. Send through one writer per socket (a channel), and do not
   serialize the receive loop on long commands: a build may take minutes
   while other commands proceed.
2. **Shared types.** Compile `bozzetto-web/src/Shared/Protocol.fs` into
   Bozzetto with `<Compile Include>` on the same file, never a copy. Its type
   names avoid clashes with `Bozzetto.Composer.Protocol` and
   `Bozzetto.Providers`.
3. **Backend codec** in .NET over Fidelity.Data.JSON, mirroring
   `src/Mock/Codec.fs` field for field. int64 travels as a string, absent
   values as `null`. Echo the correlation whenever it can be read; otherwise
   use 0 with an empty target. Port the samples of `tests/RoundTrip.fs` as
   golden frames.
4. **Route Commands to `ComposerSupervisor.ExecuteAsync`:**
   - `OpenProject` → `Open` (empty host and epoch, as the MCP tool sends)
   - `Reserve` → `Reserve`, `Build` → `Build`, `Run` → `Run`
   - `Cancel` → `Cancel`, `CloseSession` → `Close`
   - `RetireWorker` → `PrepareCompilerChange`

   Map each reply to exactly one outcome. `Ok` maps to `Accepted`, with
   `Target` = `Reply.Authority` and the completion:
   - `Opened` → `SessionOpened`
   - `Reserved` → `EditReserved token`
   - `Built` → `ReservationBuilt` (artifact summary with counts)
   - `Ran` → a `RunOutput` event, then `RunFinished exitCode`
   - `Canceled` → `WorkCanceled`
   - `Closed` → `SessionClosed`
   - `CompilerRetired` → `WorkerRetired`

   `Error` maps to `Refused` with `ComposerClientJson.refusalCode` and the
   message. `RequestSnapshot` pushes `Snapshot`, `Health` and `Leases`, then
   sends `Accepted SnapshotSent`.
5. **Snapshot projection** from `ComposerDirectory`:
   - Not configured → `Unconfigured`; no live worker → `Idle`.
   - A `HelloAccepted` worker → `Running`, with host/epoch from its
     authority, `Compiler.Version` and `supervisor.WorkerPid`.
   - Each `Observed` session → `SessionStatus`, with `StatusFresh`,
     `StatusError` and `WorkerError` taken from the `ComposerResponse`.

   Decide how to surface entries that are not `Observed`.
6. **Pushes.** Subscribe to `supervisor.Changed`, coalesced like the SSE route
   (bounded channel, `DropOldest`), and broadcast `Snapshot` to every
   connection. `SessionsAsync` refreshes each session through the worker,
   which may call for a cheaper cached read for pushes. Build `Health` from
   the `DaemonStatusPayload` sources: `HealthSnapshot`, `DaemonTelemetry`
   (which samples the Composer worker too), `MachineMemory` and the
   `HealthAnomaly` verdicts as `Alarms`. Never push on a timer or by polling
   and diffing: push `Health` and `Leases` only when an owner's discrete
   state changes (see the known gap above). On each connect, send
   `Welcome { ProtocolVersion; DaemonVersion; StartedAtMs }`, then the three
   state events. `Uptime` comes from the hub's clock alone, which runs only
   while a page is connected.
7. **Lease snapshot gaps.** `ExpensiveWorkLease.snapshot` exposes neither
   `GrantedAt` nor an id. The `LeaseId` is the release capability and must
   never be sent, so add a non-secret display id.
8. **Page serving is implemented.** Both `/composer` and `/dashboard` serve
   the welded page as `text/html; charset=utf-8` from memory, with
   `Cache-Control: no-store` and hashes of the exact inline script/styles in
   the CSP. The bridge uses the page's origin; tab icons use `img-src 'self'`.
9. **Reservation reporting**, so tokens survive reloads and are visible to
   other tabs: report the active reservation per session, or accept a
   re-reserve.
