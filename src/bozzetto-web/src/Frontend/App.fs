/// Bozzetto's browser UI: separate Dashboard and Work views, in
/// Partas.Solid (SolidJS via Fable). Every action is a Command sent over the
/// bridge; the backend is the update function; Events fold into the stores
/// (Model.fs) and Solid updates only the DOM that reads what changed.
module Bozzetto.Web.Frontend.App

open Partas.Solid
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Web.Frontend
open Bozzetto.Web.Frontend.Model

// ── Display helpers (pure; full class names so Tailwind keeps them) ────────

// Both routes share a bundle and bridge, not a layout. The route is fixed for
// this page lifetime; ordinary links open a fresh, independent page.
let private isWorkPage () =
  Browser.Dom.window.location.pathname.TrimEnd('/') = "/work"

let private linkDot (link: Bridge.Link) =
  match link with
  | Bridge.Connected -> "inline-block w-2.5 h-2.5 rounded-full bg-success"
  | Bridge.Connecting -> "inline-block w-2.5 h-2.5 rounded-full bg-secondary animate-pulse"
  | Bridge.Retrying _ -> "inline-block w-2.5 h-2.5 rounded-full bg-error animate-pulse"

let private linkText (link: Bridge.Link) =
  match link with
  | Bridge.Connected -> "connected"
  | Bridge.Connecting -> "connecting…"
  | Bridge.Retrying(attempt, at) ->
    "reconnecting at " + clockTime at + " (attempt " + string attempt + ")"

let private overallBadge (overall: string) =
  match overall with
  | "Healthy" -> "badge badge-sm badge-success"
  | "Degraded" -> "badge badge-sm badge-warning"
  | "" | "Unknown" -> "badge badge-sm badge-ghost"
  | _ -> "badge badge-sm badge-error"

let private pressureBadge (pressure: string) =
  match pressure with
  | "Normal" -> "badge badge-sm badge-ghost"
  | "Tight" -> "badge badge-sm badge-warning"
  | _ -> "badge badge-sm badge-error"

let private workerBadge (state: string) =
  match state with
  | "running" -> "badge badge-sm badge-info"
  | "idle" -> "badge badge-sm badge-ghost"
  | _ -> "badge badge-sm badge-warning"

let private workerText (worker: WorkerView) =
  match worker.state with
  | "running" -> "worker running"
  | "idle" -> "worker idle"
  | _ -> "Compiler worker not configured"

let private processRole = function
  | "composer-worker" -> "Compiler worker"
  | role -> role

/// A CSS-only spinner. (DaisyUI's spinner component masks are data-URI SVGs that
/// carry the SVG namespace URL, which the self-contained check forbids.)
let private spinner = "inline-block w-2.5 h-2.5 rounded-full border-2 border-current border-t-transparent animate-spin"

let private exitBadge (exitCode: int) =
  if exitCode = 0 then "badge badge-sm badge-success font-mono" else "badge badge-sm badge-error font-mono"

let private diagnosticTokenClass (severity: string) =
  match severity with
  | "error" -> "font-semibold text-error"
  | "warning" -> "font-semibold text-warning"
  | "info" -> "font-semibold text-info"
  | _ -> ""

// ── Components ─────────────────────────────────────────────────────────────
// Partas compiles each [<SolidComponent>] function to a plain JS function, and
// a call inside JSX runs within Solid's insert effect. So a component must not
// read reactive state in its setup code (only inside its JSX expressions or
// event handlers), or the whole component would re-run on that change.
// Literal text children are JSX text: keep spaces inside expressions, not at
// the edges of a literal, and keep double quotes out of literal attributes.

[<SolidComponent>]
let Information (label: string) (paragraphs: unit -> string array) =
  div (class' = "inline-flex") {
    (button(class' = "information-button", title = label + " information", onClick = Interop.openInformation).attr("aria-label", label + " information")) { "ⓘ" }
    (dialog(class' = "modal", onClick = fun event ->
      if Interop.isCurrentTarget event then Interop.closeInformation event).attr("aria-label", label)) {
      div (class' = "modal-box") {
        h3 (class' = "text-base font-semibold") { label }
        div (class' = "space-y-3 text-sm py-4") {
          For (each = paragraphs ()) { yield fun text _ -> p () { text } }
        }
        div (class' = "modal-action") {
          button (class' = "btn btn-sm btn-ghost", onClick = Interop.closeInformation) { "Close" }
        }
      }
    }
  }

[<SolidComponent>]
let TraceText (text: unit -> string) =
  For (each = DiagnosticText.segments (text ())) {
    yield fun token _ -> span (class' = diagnosticTokenClass token.severity) { token.text }
  }

[<SolidComponent>]
let DiagnosticBlock (errorContext: bool) (label: unit -> string) (text: unit -> string) =
  div (class' = (if errorContext then "diagnostic diagnostic-error" else "diagnostic")) {
    div (class' = "diagnostic-header") {
      span (class' = (if errorContext then "badge badge-sm badge-outline badge-error" else "badge badge-sm badge-ghost")) {
        if errorContext then "Diagnostic" else "Output"
      }
      h3 (class' = "text-sm font-semibold") { label () }
    }
    // A text child preserves the trace without executing markup from a compiler.
    pre (class' = "diagnostic-trace") { TraceText text }
  }

[<SolidComponent>]
let CommandOutcome (outcome: unit -> OutcomeView option) =
  let field pick = outcome () |> Option.map pick |> Option.defaultValue ""
  Show (when' = (outcome ()).IsSome) {
    Show (
      when' = (outcome () |> Option.exists (fun value -> not value.ok)),
      fallback = (div (class' = "text-xs text-success whitespace-pre-wrap break-words") {
        field (fun value -> clockTime value.at + " · " + value.text)
      })
    ) {
      div (class' = "diagnostic diagnostic-error") {
        div (class' = "diagnostic-header") {
          span (class' = "badge badge-sm badge-outline badge-error") { "Refused" }
          h3 (class' = "text-sm font-semibold") { "Command outcome" }
          span (class' = "text-xs opacity-70") { field (fun value -> clockTime value.at) }
        }
        pre (class' = "diagnostic-trace") { TraceText (fun () -> field (fun value -> value.text)) }
      }
    }
  }

[<SolidComponent>]
let TopBar (link: unit -> Bridge.Link) (dark: unit -> bool) (toggleTheme: unit -> unit) =
  let workPage = isWorkPage ()
  div (class' = "navbar flex-wrap shrink-0 min-h-12 px-4 py-2 gap-2 bg-base-100 shadow-sm border-b border-base-content/10") {
    div (class' = "flex-1 flex items-center gap-3 min-w-0") {
      span (class' = "font-heading font-extrabold text-lg tracking-tight") { "Bozzetto" }
      div (class' = "flex items-center gap-1") {
        a (
          href = "/dashboard",
          class' = (if workPage then "btn btn-ghost btn-sm" else "btn btn-ghost btn-sm btn-active text-accent"),
          title = "Daemon resources and health"
        ) { "Dashboard" }
        a (
          href = "/work",
          class' = (if workPage then "btn btn-ghost btn-sm btn-active text-accent" else "btn btn-ghost btn-sm"),
          title = "Compiler worker, project sessions, build/test leases and run output"
        ) { "Work" }
      }
      span (class' = "flex items-center gap-1.5 text-xs") {
        span (class' = linkDot (link ()))
        span (class' = "opacity-70") { linkText (link ()) }
      }
    }
    div (class' = "flex-none flex items-center gap-2") {
      button (class' = "btn btn-ghost btn-sm btn-circle text-base", title = "Toggle theme", onClick = fun _ -> toggleTheme ()) {
        if dark () then "☀" else "☾"
      }
    }
  }

/// The uptime the daemon's clock pushed, with the start time from Welcome.
let private uptimeText (uptime: float) (startedAt: float) =
  if uptime < 0.0 then "up since " + dateTime startedAt
  else "up " + duration uptime + " · since " + dateTime startedAt

[<SolidComponent>]
let HealthStrip (model: ModelView) (uptime: unit -> float) =
  div (class' = "daemon-health flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-1.5 text-xs bg-base-200 border-b border-base-content/10") {
    Show (when' = model.healthSeen, fallback = (span (class' = "opacity-50") { "waiting for daemon health…" })) {
      span (class' = overallBadge model.health.overall) { model.health.overall }
      span (class' = "font-mono") { "v" + model.health.version }
      span (class' = "opacity-70") { "pid " + string model.health.pid + " · :" + string model.health.port }
      span (class' = "opacity-70 font-mono", title = "Daemon uptime, pushed by the daemon's clock each second, and the process start time") {
        uptimeText (uptime ()) model.startedAt
      }
      span () {
        span (class' = "opacity-50 mr-1") { "boz rss" }
        span (class' = "font-mono") { bytes model.health.rss }
      }
      span () {
        span (class' = "opacity-50 mr-1") { "machine" }
        span (class' = "font-mono") { bytes model.health.available + " available / " + bytes model.health.total }
      }
      span () {
        span (class' = "opacity-50 mr-1") { "owned cpu" }
        span (class' = "font-mono") { fixedPoint model.health.cpu 1 + "%" }
      }
      span (class' = pressureBadge model.health.pressure, title = model.health.note) { "memory " + model.health.pressure }
      Show (when' = (model.health.pressure <> "Normal" && model.health.note <> "")) {
        span (class' = "opacity-60 text-xs") { model.health.note }
      }
      // The age of what this strip shows: the daemon reads when asked, not on a timer, and says when it last looked.
      span (class' = "health-age opacity-50 font-mono", title = "When the daemon last read the machine, and last took its own record") {
        "read " + duration (max 0.0 (model.startedAt + uptime () - model.health.sampledAt)) + " ago"
        + (if model.health.recordAt > 0.0 then " · record " + duration (max 0.0 (model.startedAt + uptime () - model.health.recordAt)) + " ago" else " · no record yet")
      }
      For (each = model.health.processes) {
        yield fun p _ ->
          span (class' = "font-mono opacity-60") { processRole p.role + " " + string p.pid + " " + bytes p.rss }
      }
      For (each = model.health.alarms) {
        yield fun a _ -> span (class' = "badge badge-sm badge-warning", title = a.message) { "⚠ " + a.signal + " " + a.state }
      }
    }
  }

let private metricText (m: ResourceMetricView) =
  if not m.known then "unavailable / warming up"
  elif m.unit = "bytes" then bytes m.value
  elif m.unit = "bytes/s" then bytes m.value + "/s"
  elif m.unit = "percent" then fixedPoint m.value 1 + "%"
  else fixedPoint m.value 0

let private resourceNodeLabel (node: NodeWorkView) =
  if node.local then "Workstation" else node.id.ToUpperInvariant()

[<SolidComponent>]
let HostPanel (model: ModelView) (local: LocalView) (uptime: unit -> float) (connected: unit -> bool)
    (selectedNode: unit -> NodeWorkView option) (selectNode: string -> unit) (dispatch: Command -> unit) =
  let status () =
    if not (connected ()) then "disconnected — last reading only"
    elif model.resources.status = "observing" && model.startedAt + uptime () - model.resources.sampledAt > 6000. then "stale — no recent observation"
    else model.resources.status
  div (class' = "card card-compact panel") {
    div (class' = "card-body gap-3") {
      div (class' = "flex flex-wrap items-center gap-2") {
        h2 (class' = "text-lg font-bold flex-1") { "Whole-machine resources" }
        span (class' = "badge badge-outline") { status () }
        Information "Resource readings" (fun () -> [|
          "Linux counters are sampled every two seconds during the selected observation window. Collection stops when the window ends."
          "Process CPU uses 100% for one logical CPU. RSS includes shared pages. Thread count shows the number of process threads."
          model.resources.note
          "Sequence " + model.resources.sequence + " · interval " + fixedPoint (model.resources.interval / 1000.) 1 + "s · collection " + fixedPoint model.resources.cost 1 + "ms"
        |])
        button (class' = "btn btn-sm btn-accent", disabled = (not (connected ()) || (selectedNode ()).IsNone), onClick = fun _ -> dispatch (ObserveResources 120)) { "Observe for 2 minutes" }
        button (class' = "btn btn-sm btn-ghost", disabled = (not (connected ()) || (selectedNode ()).IsNone), onClick = fun _ -> dispatch (ObserveResources 0)) { "Stop" }
      }
      label (class' = "flex flex-wrap items-center gap-2 text-sm") {
        span (class' = "font-semibold") { "Node" }
        (``select``(class' = "select select-bordered select-sm", value = (selectedNode () |> Option.map _.id |> Option.defaultValue ""), disabled = (not (connected ()) || model.nodes.Length = 0), onChange = (fun event -> selectNode (Interop.inputValue event))).attr("aria-label", "Resource node")) {
          Show (when' = (model.nodes.Length = 0)) { option'(value = "") { "Awaiting node observations" } }
          For (each = model.nodes) {
            yield fun node _ -> option'(value = node.id) { resourceNodeLabel node }
          }
        }
      }
      Show (when' = (selectedNode () |> Option.exists (fun node -> node.status <> "available"))) {
        p (class' = "resource-node-availability text-xs opacity-70") {
          selectedNode () |> Option.map (fun node -> resourceNodeLabel node + " work observation: " + node.status + ". " + node.note) |> Option.defaultValue ""
        }
      }
      CommandOutcome (fun () -> Interop.dictTryGet local.outcomes "resources")
      Show (when' = (model.resources.sampledAt > 0.)) {
        p (class' = "text-xs font-mono opacity-70") {
          "Updated " + clockTime model.resources.sampledAt
        }
      }
      Show (when' = (model.resources.sequence <> ""), fallback = (p (class' = "text-sm opacity-70") { "No measurements received for this node." })) {
        div (class' = "grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-2") {
          For (each = (model.resourceMetrics |> Array.filter (fun m -> m.id = "Host CPU busy" || m.id = "RAM occupied" || m.id = "RAM available" || m.id = "RAM total (OS managed)" || m.id = "Swap used" || m.id.Contains "GPU busy"))) {
            yield fun m _ ->
              div (class' = "panel p-3") {
                div (class' = "text-xs opacity-70") { m.id }
                div (class' = "text-xl font-mono font-semibold") { metricText m }
              }
          }
        }
        details () {
          summary (class' = "cursor-pointer text-sm font-semibold") { "More metrics" }
          table (class' = "table table-xs") {
            tbody () {
              For (each = model.resourceMetrics) {
                yield fun m _ -> tr () { td () { m.id }; td (class' = "font-mono") { metricText m } }
              }
            }
          }
        }
        h3 (class' = "text-sm font-semibold") { "Processes" }
        p (class' = "text-xs opacity-50") {
          string model.resources.count + " processes · " + string model.resources.unreadable + " unreadable · " + string model.resources.omitted + " omitted"
        }
        div (class' = "overflow-x-auto") {
          table (class' = "table table-xs") {
            thead () { tr () { th () { "process" }; th () { "PID / parent" }; th () { "CPU" }; th () { "RSS" }; th () { "threads" } } }
            tbody () {
              For (each = model.resourceProcesses) {
                yield fun p _ ->
                  tr () {
                    td (class' = "font-mono", title = p.context + " · identity " + p.id) { p.name }
                    td (class' = "font-mono") { string p.pid + " / " + string p.parent }
                    td (class' = "font-mono") { if p.cpuKnown then fixedPoint p.cpu 1 + "%" else "—" }
                    td (class' = "font-mono") { bytes p.rss }
                    td (class' = "font-mono") { string p.threads }
                  }
              }
            }
          }
        }
      }
    }
  }

/// This roster is daemon-owned processes only, not a whole-host inventory.
[<SolidComponent>]
let ResourcesPanel (model: ModelView) =
  div (class' = "card card-compact panel") {
    div (class' = "card-body gap-2") {
      div (class' = "flex items-center gap-2") {
        h2 (class' = "text-sm font-bold uppercase tracking-wider opacity-80 flex-1") { "Daemon resources" }
        Information "Daemon snapshot" (fun () -> [|
          "These readings capture the daemon and its compiler worker at connection time."
          "CPU is normalized by processor count. Summed RSS can count shared pages more than once."
        |])
      }
      Show (when' = model.healthSeen, fallback = (p (class' = "text-xs opacity-50") { "Waiting for daemon health…" })) {
        div (class' = "overflow-x-auto") {
          table (class' = "table table-xs") {
            thead () {
              tr () {
                th () { "role" }
                th () { "pid" }
                th () { "resident" }
                th () { "normalized CPU" }
              }
            }
            tbody () {
              For (each = model.health.processes) {
                yield fun p _ ->
                  tr () {
                    td (class' = "font-mono") { processRole p.role }
                    td (class' = "font-mono") { string p.pid }
                    td (class' = "font-mono") { bytes p.rss }
                    td (class' = "font-mono") { fixedPoint p.cpu 1 + "%" }
                  }
              }
            }
          }
        }
      }
    }
  }

[<SolidComponent>]
let OpenBar (local: LocalView) (connected: unit -> bool) (dispatch: Command -> unit) =
  let path, setPath = createSignal ""
  let submit () =
    let project = path().Trim()
    if project <> "" then dispatch (OpenProject project)
  div (class' = "flex flex-col gap-1") {
    div (class' = "join w-full") {
      input (
        class' = "input input-bordered input-sm join-item flex-1 font-mono",
        placeholder = "/absolute/path/Project.fidproj",
        value = path (),
        onInput = (fun e -> setPath (Interop.inputValue e)),
        onKeyDown = (fun e -> if Interop.keyOf e = "Enter" then submit ())
      )
      button (
        class' = "btn btn-sm btn-accent join-item",
        disabled = (not (connected ()) || (Interop.dictTryGet local.pending OpenKey).IsSome || path().Trim() = ""),
        onClick = fun _ -> submit ()
      ) { "Open project" }
    }
    CommandOutcome (fun () -> Interop.dictTryGet local.outcomes OpenKey)
  }

[<SolidComponent>]
let SessionCard (s: SessionView) (local: LocalView) (connected: unit -> bool) (dispatch: Command -> unit) =
  let label, setLabel = createSignal "source edit"
  let args, setArgs = createSignal "[]"
  let argsError, setArgsError = createSignal ""
  let reservation () = Interop.dictTryGet local.reservations s.id
  let pending () = Interop.dictTryGet local.pending s.id
  let ready () = connected () && not s.closed && (pending ()).IsNone
  let reserve () = dispatch (Reserve { ReserveEdit.Target = targetOf s; Label = label () })
  let build () =
    match reservation () with
    | Some token -> dispatch (Build { BuildReserved.Target = targetOf s; Reservation = token })
    | None -> ()
  let run () =
    match Interop.parseArguments (args ()) with
    | Some arguments ->
      setArgsError ""
      dispatch (Run { RunCurrent.Target = targetOf s; Arguments = arguments })
    | None -> setArgsError "Arguments must be a JSON array of strings, e.g. [\"--verbose\"]."
  div (class' = (if s.closed then "card card-compact panel opacity-60" else "card card-compact panel")) {
    div (class' = "card-body gap-2") {
      div (class' = "flex items-start gap-2") {
        div (class' = "flex-1 min-w-0") {
          div (class' = "font-heading font-bold truncate", title = s.project) { fileName s.project }
          div (class' = "text-xs font-mono opacity-50 truncate", title = s.project) { s.project }
        }
        div (class' = "flex flex-wrap justify-end gap-1") {
          Show (when' = s.busy) {
            span (class' = "badge badge-sm badge-info gap-1") {
              span (class' = spinner)
              "busy"
            }
          }
          Show (when' = (pending ()).IsSome) {
            span (class' = "badge badge-sm badge-outline badge-secondary gap-1") {
              span (class' = spinner)
              pending () |> Option.defaultValue ""
            }
          }
          Show (when' = s.closed) { span (class' = "badge badge-sm badge-neutral") { "closed" } }
          Show (when' = not s.fresh) { span (class' = "badge badge-sm badge-ghost", title = "Status is being re-read") { "stale" } }
          Show (when' = s.revocationPending) { span (class' = "badge badge-sm badge-warning") { "revoking" } }
          Show (when' = (s.cleanupPending || s.formatterCleanupPending)) {
            span (class' = "badge badge-sm badge-warning") { "cleanup" }
          }
          Show (when' = (reservation ()).IsSome) {
            span (class' = "badge badge-sm badge-plum font-mono", title = (reservation () |> Option.defaultValue "")) {
              "reserved " + shortHash (reservation () |> Option.defaultValue "")
            }
          }
        }
      }
      div (class' = "text-xs font-mono opacity-60 truncate") {
        "session " + s.session + " · epoch " + s.epoch + " · rev " + s.generation
      }
      Show (when' = s.hasCurrent, fallback = (div (class' = "text-xs opacity-50") { "no accepted artifact" })) {
        div (class' = "text-xs font-mono flex flex-wrap items-center gap-x-3 gap-y-0.5 border-l-2 border-success pl-2") {
          span (class' = "text-success") {
            span (class' = "mr-1") { "✓ accepted gen" }
            s.current.generation
          }
          span (class' = "chip-code", title = s.current.sha) { shortHash s.current.sha }
          span (class' = "opacity-70") { "src " + shortHash s.current.sourceVersion }
          span (class' = "opacity-70") {
            string s.current.compiled + " compiled · " + string s.current.reused + " reused · "
            + string s.current.retired + " retired · " + string s.current.changed + " witnesses changed"
          }
        }
        // What the change recompiled and what it reused, region by region: the
        // differential fact itself, not only its totals.
        Show (when' = (s.current.regions.Length > 0)) {
          table (class' = "table table-xs mt-1") {
            thead () {
              tr () {
                th () { "region" }
                th () { "witness" }
                th (class' = "text-right") { "visits" }
                th () { "object" }
              }
            }
            tbody () {
              For (each = s.current.regions) {
                yield fun r _ ->
                  tr () {
                    td (class' = "font-mono break-all max-w-[28rem]") { r.Identity }
                    td () { r.Witness }
                    td (class' = "text-right font-mono") { string r.Visits }
                    td () { r.Object }
                  }
              }
            }
          }
        }
      }
      For (each = s.notices) {
        yield fun n _ -> DiagnosticBlock true (fun () -> n.label) (fun () -> n.text)
      }
      div (class' = "flex flex-wrap items-center gap-1.5") {
        div (class' = "join") {
          input (
            class' = "input input-bordered input-xs join-item w-36",
            placeholder = "edit label",
            value = label (),
            onInput = (fun e -> setLabel (Interop.inputValue e))
          )
          button (class' = "btn btn-xs btn-plum join-item", disabled = not (ready ()), onClick = fun _ -> reserve ()) { "Reserve" }
        }
        button (
          class' = "btn btn-xs btn-outline btn-accent",
          title = "Build the reserved revision",
          disabled = (not (ready ()) || (reservation ()).IsNone),
          onClick = fun _ -> build ()
        ) { "Build" }
        div (class' = "join") {
          input (
            class' = "input input-bordered input-xs join-item w-36 font-mono",
            placeholder = "JSON args: []",
            value = args (),
            onInput = (fun e -> setArgs (Interop.inputValue e)),
            onKeyDown = (fun e -> if Interop.keyOf e = "Enter" then run ())
          )
          button (class' = "btn btn-xs btn-outline btn-accent join-item", disabled = (not (ready ()) || not s.hasCurrent), onClick = fun _ -> run ()) { "Run" }
        }
        button (class' = "btn btn-xs btn-ghost", disabled = (not (connected ()) || s.closed), onClick = fun _ -> dispatch (Cancel(targetOf s))) { "Cancel" }
        button (class' = "btn btn-xs btn-rust", disabled = (not (connected ()) || s.closed), onClick = fun _ -> dispatch (CloseSession(targetOf s))) { "Close" }
      }
      div (class' = (if argsError () = "" then "hidden" else "text-xs text-error")) { argsError () }
      CommandOutcome (fun () -> Interop.dictTryGet local.outcomes s.id)
    }
  }

[<SolidComponent>]
let SessionsPanel (model: ModelView) (local: LocalView) (connected: unit -> bool) (dispatch: Command -> unit) =
  div (class' = "flex flex-col gap-2") {
    div (class' = "flex items-baseline gap-2") {
      h2 (class' = "text-sm font-bold uppercase tracking-wider opacity-80") { "Composer sessions" }
      span (class' = "text-xs opacity-50") { string model.sessions.Length }
    }
    Show (
      when' = (model.sessions.Length > 0),
      fallback = (div (class' = "text-sm opacity-60 border border-dashed border-base-content/20 rounded-xl p-6 text-center") {
        "No open projects."
      })
    ) {
      div (class' = (if model.sessions.Length = 1 then "grid grid-cols-1 gap-2" else "grid grid-cols-1 xl:grid-cols-2 gap-2")) {
        For (each = model.sessions) {
          yield fun s _ -> SessionCard s local connected dispatch
        }
      }
    }
  }

[<SolidComponent>]
let WorkerPanel (model: ModelView) (local: LocalView) (connected: unit -> bool) (dispatch: Command -> unit) =
  // Armed by the first click, fired by the second; leaving the button (pointer
  // or focus) disarms it. No timer: the page keeps none but reconnect backoff.
  let confirming, setConfirming = createSignal false
  let retire () =
    if confirming () then
      setConfirming false
      dispatch (RetireWorker { WorkerTarget.Host = model.worker.host; Epoch = model.worker.epoch })
    else
      setConfirming true
  div (class' = "card card-compact panel") {
    div (class' = "card-body gap-1.5") {
      div (class' = "flex items-center gap-2") {
        h2 (class' = "text-sm font-bold uppercase tracking-wider opacity-80 flex-1") { "Compiler worker" }
        span (class' = workerBadge model.worker.state) { model.worker.state }
        Information "Compiler worker" (fun () -> [|
          "Opening a Clef project starts the worker. BOZZETTO_COMPOSER_WORKER selects the installed worker."
          "Retire the worker before replacing compiler binaries. Retirement closes its sessions."
        |])
      }
      Show (when' = (model.worker.state = "running")) {
        div (class' = "text-xs font-mono opacity-70 break-all") {
          "compiler " + model.worker.compilerVersion + " · pid " + (if model.worker.pid = "" then "?" else model.worker.pid)
        }
        div (class' = "text-xs font-mono opacity-50 break-all") { model.worker.host + " · " + model.worker.epoch }
      }
      div (class' = "flex items-center gap-2 pt-1") {
        button (
          class' = (if confirming () then "btn btn-xs btn-rust-armed" else "btn btn-xs btn-rust"),
          disabled = (not (connected ()) || model.worker.state <> "running" || (Interop.dictTryGet local.pending WorkerKey).IsSome),
          onClick = (fun _ -> retire ()),
          onMouseLeave = (fun _ -> setConfirming false),
          onBlur = (fun _ -> setConfirming false)
        ) { if confirming () then "Confirm: retire every session" else "Retire worker" }
      }
      CommandOutcome (fun () -> Interop.dictTryGet local.outcomes WorkerKey)
    }
  }

let private currentAgent (agent: AgentView) =
  agent.status = "running" || agent.status = "unknown"

[<SolidComponent>]
let private AgentDetails (agent: AgentView) =
  div (class' = "flex flex-col gap-1 py-2 text-xs break-words") {
    div (class' = "flex items-center gap-2") {
      span () { agent.model }
      Information "Invocation" (fun () -> [|
        "Member: " + agent.memberId
        "Session: " + agent.session + " · run: " + agent.id
        "Report sequence: " + agent.reportSequence + " · updated " + dateTime agent.updatedAt
        "Reported inference endpoint: " + agent.endpoint
      |])
    }
    div () { agent.project + " · " + agent.focus }
    div () { agent.operation }
    div () { agent.usage + " · " + agent.cost }
    div (class' = "opacity-60") {
      agent.activity + (if agent.omitted = "0" then "" else " · " + agent.omitted + " earlier omitted")
    }
  }

[<SolidComponent>]
let AgentsPanel (model: ModelView) =
  div (class' = "card card-compact panel") {
    div (class' = "card-body gap-2") {
      div (class' = "flex items-center gap-2") {
        h2 (class' = "text-sm font-bold uppercase tracking-wider opacity-80 flex-1") { "Agent work" }
        Information "Agent accounting" (fun () -> [|
          "Current reporting activity stays visible. Earlier passes are expandable, newest first. Missing reports do not establish that no work is happening."
          "Presence rows derive from facts the daemon owns, a lease held or queued and cohort membership, so an agent that never reports is still seen."
          "Identity, progress and usage are participant-reported; unknown usage and cost are not zero."
          "Execution host: " + model.executionHost + " · publication " + model.agentSequence
          "This board retains up to 64 reporting participants and 256 runs during the current daemon lifetime."
        |])
      }
      Show (
        when' = (model.agents |> Array.exists currentAgent || model.presence.Length > 0),
        fallback = (p (class' = "text-xs opacity-50") {
          // Absence is claimed only from a received board; before one arrives the
          // daemon has said nothing about agents.
          if model.agentIncarnation = "" then "No agent board received from the daemon yet."
          else "No agents present: no lease held or queued, no cohort member, no running report."
        })
      ) {
        For (each = (model.agents |> Array.filter currentAgent |> Array.sortByDescending (fun agent -> agent.updatedAt, agent.id))) {
          yield fun agent _ ->
            div (class' = "border-t border-base-content/10 pt-2 text-xs break-words") {
              div (class' = "font-semibold") { agent.name + " · " + agent.role + " · reported " + agent.status }
              div (class' = "opacity-60") { "Updated " + clockTime agent.updatedAt + " · " + agent.model + " · " + agent.endpoint }
              div () { agent.project + " · " + agent.operation }
              details () {
                summary (class' = "cursor-pointer opacity-60") { "Details" }
                AgentDetails agent
              }
            }
        }
        For (each = (model.presence |> Array.sortByDescending (fun here -> here.since, here.id))) {
          yield fun here _ ->
            div (class' = "presence border-t border-base-content/10 pt-2 text-xs break-words") {
              div (class' = "font-semibold") { here.name + " · " + here.detail }
              div (class' = "opacity-60") {
                // A lease's time is its grant or ask; a membership's is its latest renewal,
                // which is what the daemon retains, not when the member joined.
                here.source + (if here.source = "member" then " renewed " else " since ") + clockTime here.since
                + (if here.project = "" then "" else " · " + here.project)
              }
            }
        }
      }
      Show (when' = (model.agents |> Array.exists (currentAgent >> not))) {
        details (class' = "border-t border-base-content/10 pt-2") {
          summary (class' = "cursor-pointer text-xs opacity-60") {
            "Earlier passes (" + string (model.agents |> Array.filter (currentAgent >> not) |> Array.length) + ")"
          }
          For (each = (model.agents |> Array.filter (currentAgent >> not) |> Array.sortByDescending (fun agent -> agent.updatedAt, agent.id))) {
            yield fun agent _ ->
              details (class' = "border-t border-base-content/10 mt-2 pt-2 text-xs break-words") {
                summary (class' = "cursor-pointer font-semibold") {
                  agent.name + " · " + agent.role + " · reported " + agent.status + " · " + clockTime agent.updatedAt
                }
                AgentDetails agent
              }
          }
        }
      }
    }
  }

[<SolidComponent>]
let LeasesPanel (model: ModelView) =
  div (class' = "card card-compact panel") {
    div (class' = "card-body gap-1.5") {
      div (class' = "flex items-baseline gap-2") {
        h2 (class' = "text-sm font-bold uppercase tracking-wider opacity-80 flex-1") { "Work leases" }
        span (class' = "text-xs opacity-50") {
          string model.leases.Length + " active · " + string model.queue.Length + " queued"
        }
      }
      Show (
        when' = (model.leases.Length + model.queue.Length > 0),
        fallback = (div (class' = "text-xs opacity-60") { "No queued or active work." })
      ) {
        table (class' = "table table-xs") {
          thead () {
            tr () {
              th () { "kind" }
              th () { "holder" }
              th () { "state" }
            }
          }
          tbody () {
            For (each = model.leases) {
              yield fun l _ ->
                tr () {
                  td (class' = "font-mono") { l.kind }
                  td (class' = "font-mono break-all max-w-[24rem]", title = l.holder + " · " + l.id) { l.holder }
                  td () {
                    span (class' = "badge badge-xs badge-info mr-1") { "granted" }
                    span (class' = "opacity-70", title = "granted " + dateTime l.grantedAt) { "until " + clockTime l.expiresAt }
                  }
                }
            }
            For (each = model.queue) {
              yield fun w _ ->
                tr () {
                  td (class' = "font-mono") { w.kind }
                  td (class' = "font-mono break-all max-w-[24rem]", title = w.holder) { w.holder }
                  td () {
                    span (class' = "badge badge-xs badge-warning mr-1") { "queued" }
                    span (class' = "opacity-70") { "since " + clockTime w.requestedAt }
                  }
                }
            }
          }
        }
      }
    }
  }

[<SolidComponent>]
let OutputPane (model: ModelView) =
  let tab, setTab = createSignal "runs"
  // Runs come from two places: the daemon's journal (every caller's, pushed as
  // facts) and this page's own commands. A pick names its source.
  let selected, setSelected = createSignal ""
  // The pane can be folded to its tab bar; the choice is this page's own.
  let collapsed, setCollapsed = createSignal false
  let picks () =
    Array.append
      (model.operations
       |> Array.filter (fun o -> o.hasRun)
       |> Array.map (fun o ->
         {| key = "j:" + o.id; label = "#" + o.id + " " + o.caller + " " + o.project + " → " + string o.exitCode
            project = o.project; exitCode = o.exitCode; generation = o.generation; sourceVersion = o.sourceVersion; at = o.at
            stdout = (if o.dropped then "[transcript dropped for space]" else o.stdout); stderr = o.stderr; source = o.caller |}))
      (model.runs
       |> Array.map (fun r ->
         {| key = "p:" + string r.id; label = "#" + string r.id + " " + r.project + " → " + string r.exitCode + " (this page)"
            project = r.project; exitCode = r.exitCode; generation = r.generation; sourceVersion = r.sourceVersion; at = r.at
            stdout = r.stdout; stderr = r.stderr; source = "this page" |}))
  let run () =
    let all = picks ()
    match all |> Array.tryFind (fun r -> r.key = selected ()) with
    | Some r -> Some r
    | None -> Array.tryHead all
  let stdoutText () = match run () with Some r -> r.stdout | None -> ""
  let stderrText () = match run () with Some r -> r.stderr | None -> ""
  let projectText () = match run () with Some r -> r.project | None -> ""
  let sourceText () = match run () with Some r -> r.source | None -> ""
  let exitCodeOf () = match run () with Some r -> r.exitCode | None -> 0
  let generationText () = match run () with Some r -> "gen " + r.generation + " · src " + shortHash r.sourceVersion | None -> ""
  let atText () = match run () with Some r -> clockTime r.at | None -> ""
  let activityCount () = model.operations.Length + model.activity.Length
  div (class' = (if collapsed () then "flex flex-col border-t border-base-content/10 bg-base-100" else "flex flex-col h-[34vh] min-h-40 border-t border-base-content/10 bg-base-100")) {
    div (class' = "flex items-center gap-2 px-4 py-1 border-b border-base-content/10") {
      button (
        class' = "btn btn-xs btn-ghost output-fold",
        title = (if collapsed () then "Show the output pane" else "Fold the output pane to its tabs"),
        onClick = fun _ -> setCollapsed (not (collapsed ()))
      ) { if collapsed () then "▲" else "▼" }
      div (class' = "tabs tabs-bordered tabs-sm") {
        button (class' = (if tab () = "runs" then "tab tab-active !border-accent text-accent font-semibold" else "tab"), onClick = fun _ -> setTab "runs") {
          "Run output (" + string (picks ()).Length + ")"
        }
        button (class' = (if tab () = "activity" then "tab tab-active !border-accent text-accent font-semibold" else "tab"), onClick = fun _ -> setTab "activity") {
          "Activity (" + string (activityCount ()) + ")"
        }
      }
      div (class' = (if tab () = "runs" then "flex-1 flex gap-1 overflow-x-auto" else "hidden")) {
        For (each = picks ()) {
          yield fun r _ ->
            button (
              class' = (if (run () |> Option.map (fun x -> x.key)) = Some r.key then "btn btn-xs btn-active font-mono" else "btn btn-xs btn-ghost font-mono"),
              onClick = fun _ -> setSelected r.key
            ) { r.label }
        }
      }
    }
    div (class' = (if tab () = "runs" && not (collapsed ()) then "flex-1 min-h-0 overflow-auto px-4 py-3" else "hidden")) {
      Show (when' = (run ()).IsSome, fallback = (div (class' = "text-xs opacity-50") { "No runs yet: runs from every caller appear here as the daemon journals them." })) {
        div (class' = "flex items-center gap-2 text-xs mb-2") {
          span (class' = exitBadge (exitCodeOf ())) { "exit " + string (exitCodeOf ()) }
          span (class' = "font-semibold") { projectText () }
          span (class' = "font-mono opacity-60") { generationText () }
          span (class' = "font-mono opacity-60", title = "Who asked, as the adapter reported it; not an authenticated identity") { sourceText () }
          span (class' = "opacity-50") { atText () }
        }
        pre (class' = "panel diagnostic-trace") { TraceText stdoutText }
        div (class' = (if stderrText () = "" then "hidden" else "mt-2")) {
          DiagnosticBlock false (fun () -> "Standard error") stderrText
        }
      }
    }
    div (class' = (if tab () = "activity" && not (collapsed ()) then "flex-1 min-h-0 overflow-auto px-4 py-1" else "hidden")) {
      Show (when' = (activityCount () > 0), fallback = (div (class' = "text-xs opacity-50 py-2") { "No activity yet: every caller's Composer operations appear here as the daemon journals them." })) {
        For (each = model.operations) {
          yield fun o _ ->
            div (class' = "flex gap-2 text-xs py-2 border-b border-base-content/5 operation") {
              span (class' = "font-mono opacity-50 shrink-0") { clockTime o.at }
              span (class' = (if o.ok then "text-success shrink-0" else "text-error shrink-0")) { if o.ok then "✓" else "✗" }
              span (class' = "font-mono opacity-70 shrink-0", title = "Who asked, as the adapter reported it; not an authenticated identity") { o.caller }
              span (class' = "font-semibold shrink-0") { o.operation }
              span (class' = "font-mono opacity-70 shrink-0") { o.project }
              Show (when' = not o.ok) { span (class' = "opacity-70") { o.code + ": " + o.message } }
            }
        }
        Show (when' = (model.activity.Length > 0)) {
          div (class' = "text-xs opacity-50 pt-2") { "this page" }
          For (each = model.activity) {
            yield fun a _ ->
              div (class' = "flex gap-2 text-xs py-2 border-b border-base-content/5") {
                span (class' = "font-mono opacity-50 shrink-0") { clockTime a.at }
                span (class' = (if a.ok then "text-success shrink-0" else "text-error shrink-0")) { if a.ok then "✓" else "✗" }
                span (class' = "font-mono opacity-70 shrink-0") { a.scope }
                pre (class' = "activity-trace") { TraceText (fun () -> a.text) }
              }
          }
        }
      }
    }
  }

let private runningOperation (node: NodeWorkView) (operation: NodeOperationView) =
  operation.outcome = "running" || (operation.hasLease && (node.leases |> Array.exists (fun lease -> lease.id = operation.leaseId)))

let private operationKey (node: NodeWorkView) (operation: NodeOperationView) =
  node.id + ":" + node.journal + ":" + operation.sequence

[<SolidComponent>]
let WorkNodesPanel (model: ModelView) (uptime: unit -> float) (connected: unit -> bool) (read: NodeWorkView -> NodeOperationView -> TranscriptStream -> unit) =
  let retired, setRetired = createSignal (Interop.getRetiredWork ())
  let retire keys =
    let next = Array.append keys (retired ()) |> Array.distinct |> Array.truncate 256
    Interop.saveRetiredWork next
    setRetired next
  let recent (node: NodeWorkView) =
    let now = if uptime () < 0. then node.sampledAt else model.startedAt + uptime ()
    node.operations
    |> Array.filter (fun operation ->
      not (runningOperation node operation) && now - operation.at <= 86400000.
      && not (retired () |> Array.contains (operationKey node operation)))
    |> Array.sortByDescending (fun operation -> operation.at, int64 operation.sequence)
    |> Array.truncate 20
  let active (node: NodeWorkView) = node.operations |> Array.filter (runningOperation node)
  details (open' = true, class' = "work-nodes flex flex-col gap-3") {
    summary (class' = "cursor-pointer text-lg font-bold") { "Work across nodes" }
    div (class' = "flex items-center gap-2 text-xs opacity-70") {
      span () { "Completed entries retire after 24 hours; at most 20 per node are shown." }
      Show (when' = ((retired ()).Length > 0)) {
        button (class' = "btn btn-xs btn-ghost", onClick = fun _ ->
          Interop.saveRetiredWork [||]
          setRetired [||]) { "Restore retired entries" }
      }
      Information "Work history" (fun () -> [|
        "Running work, queued leases and open compiler projects never retire from this view."
        "Retiring or hiding an entry affects this browser only. The node's SQLite journal retains its stored evidence."
        "Compiler sessions are opened Clef projects. Leased command projects are displayed separately; a checkout alone is not an open compiler session."
      |])
    }
    Show (when' = (model.nodes.Length = 0)) {
      p (class' = "text-sm opacity-70") { "No node work observation has arrived. Availability is unknown." }
    }
    div (class' = "grid grid-cols-1 lg:grid-cols-2 gap-3") {
      For (each = model.nodes) {
        yield fun node _ ->
          div (class' = "card card-compact panel") {
            div (class' = "card-body gap-2") {
              div (class' = "flex flex-wrap gap-2 items-center") {
                h3 (class' = "font-bold flex-1") { node.id }
                span (class' = "badge badge-outline") { if node.local then "controller" else "remote · read-only" }
                span (class' = "badge badge-outline") { if connected () then node.status else "controller disconnected" }
              }
              p (class' = "text-xs opacity-70") { node.note }
              Show (when' = node.hasValue, fallback = (p (class' = "text-sm opacity-70") { "Work state unavailable; no idle state inferred." })) {
                p (class' = "work-sample-age text-xs opacity-60 font-mono") {
                  "Work sample " + clockTime node.sampledAt + " · "
                  + (if uptime () < 0. then "age unavailable (awaiting clock)"
                     else duration (max 0. (model.startedAt + uptime () - node.sampledAt)) + " ago")
                }
                p (class' = "text-sm") {
                  string (node.sessions |> Array.filter (fun session -> not session.closed)).Length + " compiler sessions · " + string node.leases.Length + " active leases · " + string node.queue.Length + " queued"
                }
                Show (when' = (node.status <> "available" || not (connected ()))) {
                  p (class' = "text-xs text-warning") { "Retained observation; current work is unavailable." }
                }
                p (class' = "text-xs font-mono node-worker") {
                  "Compiler worker · " + node.worker.state
                  + (if node.worker.pid = "" then "" else " · PID " + node.worker.pid)
                }
                div (class' = "node-projects flex flex-col gap-1") {
                  h4 (class' = "text-sm font-semibold") { "Compiler projects" }
                  Show (when' = (node.sessions |> Array.exists (fun session -> not session.closed)), fallback = (p (class' = "text-xs opacity-60") { "No open compiler projects in this observation." })) {
                    For (each = (node.sessions |> Array.filter (fun session -> not session.closed))) {
                      yield fun session _ ->
                        p (class' = "text-xs font-mono break-words node-session") {
                          session.project + " · " + (if session.busy then "busy" else "open")
                        }
                    }
                  }
                }
                Show (when' = ((recent node).Length > 0)) {
                  div (class' = "node-recent-projects flex flex-col gap-1") {
                    h4 (class' = "text-sm font-semibold") { "Recent project activity" }
                    For (each = (recent node |> Array.distinctBy _.project |> Array.truncate 3)) {
                      yield fun operation _ ->
                        p (class' = "text-xs font-mono break-words") { operation.project + " · " + operation.outcome + " · " + clockTime operation.at }
                    }
                  }
                }
                For (each = node.leases) {
                  yield fun lease _ ->
                    div (class' = "text-xs font-mono break-words node-lease") {
                      p (class' = "font-semibold") { lease.kind + " · active" }
                      p () { lease.holder }
                      p (class' = "opacity-60") { "Lease " + lease.id + " · granted " + clockTime lease.grantedAt }
                      Show (when' = not (node.operations |> Array.exists (fun operation -> operation.hasLease && operation.leaseId = lease.id))) {
                        p (class' = "opacity-60") { "Command process details have not been reported for this lease." }
                      }
                    }
                }
                For (each = node.queue) {
                  yield fun ask _ ->
                    p (class' = "text-xs font-mono break-words opacity-70") { ask.holder + " · " + ask.kind + " · queued since " + clockTime ask.requestedAt }
                }
                Show (when' = (connected () && node.status = "available" && node.leases.Length = 0 && node.queue.Length = 0)) {
                  p (class' = "text-xs opacity-70") { "No active or queued leases in this observation." }
                }
                For (each = active node) {
                  yield fun operation _ ->
                    div (class' = "node-operation node-active py-2 border-t border-base-300") {
                      p (class' = "text-sm font-semibold break-words") { operation.project }
                      p (class' = "text-xs font-mono break-words") { operation.operation + " · " + operation.outcome + " · PID " + string operation.pid }
                      p (class' = "text-xs font-mono break-words") { operation.executable + (operation.arguments |> Array.tryHead |> Option.map (fun argument -> " · " + argument) |> Option.defaultValue "") }
                      details () {
                        summary (class' = "cursor-pointer text-xs opacity-60") { "Command arguments" }
                        pre (class' = "text-xs font-mono whitespace-pre-wrap break-words max-h-48 overflow-auto") { String.concat "\n" operation.arguments }
                      }
                      p (class' = "text-xs opacity-60 break-words") { "Lease " + operation.leaseId + " · run " + operation.runId }
                    }
                }
                details (class' = "node-operations") {
                  summary (class' = "cursor-pointer text-sm font-semibold") { "Recent operations (" + string (recent node).Length + ")" }
                  div (class' = "flex flex-col gap-2 pt-2") {
                    Show (when' = (node.storageError <> "")) { p (class' = "text-xs text-warning") { "Transcript store unavailable: " + node.storageError } }
                    Show (when' = ((recent node).Length = 0)) { p (class' = "text-xs opacity-70") { "No recent completed operations in this view." } }
                    Show (when' = node.transcriptsOmitted) { p (class' = "text-xs opacity-70") { "Transcript bytes are loaded from the node on request." } }
                    Show (when' = ((recent node).Length > 0)) {
                      button (class' = "btn btn-xs btn-ghost self-start", onClick = fun _ -> retire (recent node |> Array.map (operationKey node))) { "Retire shown entries" }
                    }
                    For (each = recent node) {
                      yield fun operation _ ->
                        details (class' = "node-operation py-2 border-t border-base-300") {
                          summary (class' = "cursor-pointer text-xs font-mono break-words") {
                            operation.project + " · " + operation.operation + " · " + operation.outcome
                            + (if operation.hasRun then " · exit " + string operation.exitCode else "") + " · " + clockTime operation.at
                          }
                          div (class' = "flex flex-col gap-2 pt-2") {
                            Show (when' = operation.hasLease) {
                              p (class' = "text-xs font-mono break-words") { "PID " + string operation.pid + " · started " + clockTime operation.startedAt }
                              pre (class' = "text-xs font-mono whitespace-pre-wrap break-words max-h-48 overflow-auto", title = "Executable, followed by one argument per line") { operation.executable + "\n" + String.concat "\n" operation.arguments }
                              p (class' = "text-xs font-mono opacity-60 break-words") { "Lease " + operation.leaseId + " · run " + operation.runId }
                            }
                            Show (when' = operation.dropped) { p (class' = "text-xs text-warning") { "Source transcript was not retained." } }
                            Show (when' = operation.hasRun) {
                              div (class' = "flex gap-2") {
                                button (class' = "btn btn-xs btn-outline", disabled = not (connected ()), onClick = fun _ -> read node operation Stdout) { "Load stdout" }
                                button (class' = "btn btn-xs btn-outline", disabled = not (connected ()), onClick = fun _ -> read node operation Stderr) { "Load stderr" }
                              }
                            }
                            button (class' = "btn btn-xs btn-ghost self-start", onClick = fun _ -> retire [| operationKey node operation |]) { "Retire entry" }
                          }
                        }
                    }
                  }
                }
              }
            }
          }
      }
    }
  }

[<SolidComponent>]
let App () =
  let workPage = isWorkPage ()
  Browser.Dom.document.title <- (if workPage then "Work · Bozzetto" else "Dashboard · Bozzetto")

  // ── Model ────────────────────────────────────────────────────────────────
  // Stores hold what the backend reported; the backend is the update.
  let stores = Model.create ()
  let model = stores.Model
  let local = stores.Local
  let commands = Model.dispatcher ()

  // Pure UI state: link status and theme. No clock: times are shown as
  // instants, the uptime is the daemon's push, and the page runs no timer but
  // the bridge's reconnect backoff.
  let link, setLink = createSignal Bridge.Connecting
  let connected () =
    match link () with
    | Bridge.Connected -> true
    | Bridge.Connecting | Bridge.Retrying _ -> false

  let initialDark = Interop.getSavedTheme () <> "light"
  let dark, setDark = createSignal initialDark
  Interop.setTheme (if initialDark then "dark" else "light")
  let toggleTheme () =
    let next = not (dark ())
    setDark next
    let name = if next then "dark" else "light"
    Interop.setTheme name
    Interop.saveTheme name

  // ── Update ───────────────────────────────────────────────────────────────
  // dispatch sends a Command across the bridge; the backend's Events come
  // back through `receive` and fold into the stores. Each hop is echoed to
  // the browser console with its wire payload.
  let dispatch (command: Command) =
    let frame = Model.begin' stores commands command
    let wire = Codec.encodeCommand frame
    if Bridge.send wire then Interop.consoleLog (sprintf "→ binary frame (%d bytes)" wire.Length)
    else Model.abandon stores commands frame

  let receive (payload: byte array) =
    match Codec.decodeEvent payload with
    | Ok event ->
      match event with
      | Health _ | Leases _ | Uptime _ | Resources _ | AgentWork _ | WorkNodes _ | NodeResources _ | NodeTranscript _ -> ()
      | _ -> Interop.consoleLog (sprintf "← binary frame (%d bytes)" payload.Length)
      Model.apply stores commands event
    | Error message -> Interop.consoleLog ("← undecodable binary frame: " + message)

  Bridge.start receive (fun next ->
    match next with
    | Bridge.Retrying _ -> Model.abandonAll stores commands
    | Bridge.Connecting | Bridge.Connected -> ()
    setLink next)

  let focus (node: NodeWorkView) =
    if model.nodeFocus <> "" && model.nodeFocus <> node.id then
      dispatch (ObserveNodeResources { Node = model.nodeFocus; Seconds = 0 })
    Model.focusNode stores node.id node.incarnation
    dispatch (ObserveNodeResources { Node = node.id; Seconds = 120 })
  let selectedResourceNode () =
    model.nodes |> Array.tryFind (fun node -> node.id = model.nodeFocus)
    |> Option.orElseWith (fun () -> model.nodes |> Array.tryFind _.local)
  let selectResourceNode nodeId =
    model.nodes |> Array.tryFind (fun node -> node.id = nodeId) |> Option.iter focus
  let resourceDispatch = function
    | ObserveResources seconds ->
      match selectedResourceNode () with
      | Some node ->
        if model.nodeFocus <> node.id || model.nodeIncarnation <> node.incarnation then
          Model.focusNode stores node.id node.incarnation
        dispatch (ObserveNodeResources { Node = node.id; Seconds = seconds })
      | None -> ()
    | command -> dispatch command
  let readTranscript (node: NodeWorkView) (operation: NodeOperationView) stream =
    dispatch (ReadNodeTranscript {
      Source = { Node = node.id; Incarnation = node.incarnation }; Journal = node.journal
      Sequence = int64 operation.sequence; Stream = stream; Offset = 0L; MaxBytes = MaxTranscriptPageBytes })
  let readNextTranscript () =
    let value = model.transcript
    if value.next <> "" then
      dispatch (ReadNodeTranscript {
        Source = { Node = value.node; Incarnation = value.incarnation }; Journal = value.journal
        Sequence = int64 value.sequence; Stream = value.stream; Offset = int64 value.next; MaxBytes = MaxTranscriptPageBytes })

  // ── View ─────────────────────────────────────────────────────────────────
  div (class' = "flex flex-col h-screen bg-base-100 text-base-content text-sm") {
    TopBar link dark toggleTheme
    Show (when' = not (connected ())) {
      div (class' = "px-4 py-1 text-xs bg-warning text-warning-content") {
        "Disconnected · last received state · commands unavailable"
      }
    }
    Show (when' = model.protocolMismatch) {
      div (class' = "px-4 py-1 text-xs bg-error text-error-content") {
        "UI version changed. Reload this page."
      }
    }
    div (class' = (if connected () then "flex-1 min-h-0 overflow-auto" else "flex-1 min-h-0 overflow-auto opacity-70")) {
      Show (
        when' = workPage,
        fallback = (div (class' = "flex flex-col gap-4 p-4") {
          HealthStrip model stores.Uptime
          AgentsPanel model
          HostPanel model local stores.Uptime connected selectedResourceNode selectResourceNode resourceDispatch
          details () {
            summary (class' = "cursor-pointer text-xs opacity-60") { "Daemon snapshot" }
            ResourcesPanel model
          }
        })
      ) {
        div (class' = "grid grid-cols-1 xl:grid-cols-3 gap-4 p-4") {
          details (open' = true, class' = "xl:col-span-3 controller-compiler") {
            summary (class' = "cursor-pointer text-sm font-semibold") { "Controller compiler controls" }
            div (class' = "grid grid-cols-1 xl:grid-cols-3 gap-4 mt-3") {
              div (class' = "xl:col-span-2 flex flex-col gap-3 min-w-0") {
                SessionsPanel model local connected dispatch
                details () {
                  summary (class' = "cursor-pointer text-xs opacity-60") { "Open project" }
                  OpenBar local connected dispatch
                }
              }
              div (class' = "flex flex-col gap-3 min-w-0") {
                WorkerPanel model local connected dispatch
              }
            }
          }
          div (class' = "xl:col-span-3") { WorkNodesPanel model stores.Uptime connected readTranscript }
          Show (when' = (model.transcript.node <> "")) {
            div (class' = "xl:col-span-3 card card-compact panel transcript-readback") {
              div (class' = "card-body gap-2") {
                h2 (class' = "text-lg font-bold") { "Transcript · " + model.transcript.node + " · " + (if model.transcript.stream = Stdout then "stdout" else "stderr") }
                p (class' = "text-xs font-mono break-words") { model.transcript.journal + " · entry " + model.transcript.sequence }
                p (class' = "text-sm") { model.transcript.status }
                p (class' = "text-xs opacity-70") { string model.transcript.bytes.Length + " stored bytes loaded" + (if model.transcript.total = "" then "" else " of " + model.transcript.total) }
                Show (when' = model.transcript.truncated) { p (class' = "text-xs text-warning") { "Truncated at the source; this is the retained transcript, not complete process output." } }
                Show (when' = (model.transcript.text <> "")) {
                  pre (class' = "text-xs font-mono whitespace-pre-wrap break-words max-h-80 overflow-auto") { model.transcript.text }
                }
                Show (when' = (model.transcript.next <> "")) {
                  button (class' = "btn btn-sm btn-outline self-start", disabled = (not (connected ()) || model.transcript.status = "reading transcript"), onClick = fun _ -> readNextTranscript ()) { "Next transcript page" }
                }
              }
            }
          }

        }
      }
    }
    Show (when' = (workPage && (model.operations.Length > 0 || model.runs.Length > 0 || model.activity.Length > 0))) { OutputPane model }
  }
