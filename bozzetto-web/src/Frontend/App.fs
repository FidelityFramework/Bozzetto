/// Bozzetto's browser UI, first screen: the Composer dashboard, in
/// Partas.Solid (SolidJS via Fable). Every action is a Command sent over the
/// bridge; the backend is the update function; Events fold into the stores
/// (Model.fs) and Solid updates only the DOM that reads what changed.
module Bozzetto.Web.Frontend.App

open Partas.Solid
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Web.Frontend
open Bozzetto.Web.Frontend.Model

// ── Display helpers (pure; full class names so Tailwind keeps them) ────────

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
  | _ -> "Composer not configured"

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
let TopBar (link: unit -> Bridge.Link) (dark: unit -> bool) (toggleTheme: unit -> unit) (refresh: unit -> unit) =
  div (class' = "navbar min-h-0 h-12 px-4 gap-3 bg-base-100 shadow-sm border-b border-base-content/10") {
    div (class' = "flex-1 flex items-center gap-3 min-w-0") {
      span (class' = "font-heading font-extrabold text-lg tracking-tight") { "Bozzetto" }
      span (class' = "text-xs font-semibold uppercase tracking-widest text-accent") { "Composer" }
      span (class' = "flex items-center gap-1.5 text-xs") {
        span (class' = linkDot (link ()))
        span (class' = "opacity-70") { linkText (link ()) }
      }
    }
    div (class' = "flex-none flex items-center gap-2") {
      button (class' = "btn btn-outline btn-accent btn-sm font-medium", title = "Request a fresh snapshot", onClick = fun _ -> refresh ()) { "↻ Refresh" }
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
  div (class' = "flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-1.5 text-xs bg-base-200 border-b border-base-content/10") {
    Show (when' = model.healthSeen, fallback = (span (class' = "opacity-50") { "waiting for daemon health…" })) {
      span (class' = overallBadge model.health.overall) { model.health.overall }
      span (class' = "font-mono") { "v" + model.health.version }
      span (class' = "opacity-70") { "pid " + string model.health.pid + " · :" + string model.health.port }
      span (class' = "opacity-70 font-mono", title = "Daemon uptime, pushed by the daemon's clock each second, and the process start time") {
        uptimeText (uptime ()) model.startedAt
      }
      span () {
        span (class' = "opacity-50 mr-1") { "rss" }
        span (class' = "font-mono") { bytes model.health.rss }
      }
      span () {
        span (class' = "opacity-50 mr-1") { "machine" }
        span (class' = "font-mono") { bytes model.health.available + " free / " + bytes model.health.total }
      }
      span () {
        span (class' = "opacity-50 mr-1") { "cpu" }
        span (class' = "font-mono") { fixedPoint model.health.cpu 1 + "%" }
      }
      span (class' = pressureBadge model.health.pressure) { "memory " + model.health.pressure }
      For (each = model.health.processes) {
        yield fun p _ ->
          span (class' = "font-mono opacity-60") { p.role + " " + string p.pid + " " + bytes p.rss }
      }
      For (each = model.health.alarms) {
        yield fun a _ -> span (class' = "badge badge-sm badge-warning", title = a.message) { "⚠ " + a.signal + " " + a.state }
      }
    }
    span (class' = "flex-1")
    span (class' = workerBadge model.worker.state) { workerText model.worker }
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
      h2 (class' = "text-sm font-bold uppercase tracking-wider opacity-80") { "Sessions" }
      span (class' = "text-xs opacity-50") { string model.sessions.Length }
    }
    Show (
      when' = (model.sessions.Length > 0),
      fallback = (div (class' = "text-sm opacity-60 border border-dashed border-base-content/20 rounded-xl p-6 text-center") {
        "No Composer sessions are open. Open a .fidproj to begin; reserve before editing, build the reservation, then run."
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
      }
      Show (
        when' = (model.worker.state = "running"),
        fallback = (div (class' = "text-xs opacity-60") {
          if model.worker.state = "unconfigured" then
            "Set BOZZETTO_COMPOSER_WORKER before starting the daemon."
          else "No live worker; opening a project starts one."
        })
      ) {
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
        span (class' = "text-xs opacity-50") { "before replacing compiler binaries" }
      }
      CommandOutcome (fun () -> Interop.dictTryGet local.outcomes WorkerKey)
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
        fallback = (div (class' = "text-xs opacity-60") { "No leases held or queued." })
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
                  td (class' = "font-mono truncate max-w-[10rem]", title = l.holder + " · " + l.id) { l.holder }
                  td () {
                    span (class' = "badge badge-xs badge-info mr-1") { "held" }
                    span (class' = "opacity-70", title = "granted " + dateTime l.grantedAt) { "until " + clockTime l.expiresAt }
                  }
                }
            }
            For (each = model.queue) {
              yield fun w _ ->
                tr () {
                  td (class' = "font-mono") { w.kind }
                  td (class' = "font-mono truncate max-w-[10rem]", title = w.holder) { w.holder }
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
  let selected, setSelected = createSignal 0
  let run () =
    let all = model.runs
    match all |> Array.tryFind (fun r -> r.id = selected ()) with
    | Some r -> Some r
    | None -> Array.tryHead all
  let field (pick: RunView -> string) = run () |> Option.map pick |> Option.defaultValue ""
  div (class' = "flex flex-col h-[34vh] min-h-40 border-t border-base-content/10 bg-base-100") {
    div (class' = "flex items-center gap-2 px-4 py-1 border-b border-base-content/10") {
      div (class' = "tabs tabs-bordered tabs-sm") {
        button (class' = (if tab () = "runs" then "tab tab-active !border-accent text-accent font-semibold" else "tab"), onClick = fun _ -> setTab "runs") {
          "Run output (" + string model.runs.Length + ")"
        }
        button (class' = (if tab () = "activity" then "tab tab-active !border-accent text-accent font-semibold" else "tab"), onClick = fun _ -> setTab "activity") {
          "Activity (" + string model.activity.Length + ")"
        }
      }
      div (class' = (if tab () = "runs" then "flex-1 flex gap-1 overflow-x-auto" else "hidden")) {
        For (each = model.runs) {
          yield fun r _ ->
            button (
              class' = (if (run () |> Option.map (fun x -> x.id)) = Some r.id then "btn btn-xs btn-active font-mono" else "btn btn-xs btn-ghost font-mono"),
              onClick = fun _ -> setSelected r.id
            ) { "#" + string r.id + " " + r.project + " → " + string r.exitCode }
        }
      }
    }
    div (class' = (if tab () = "runs" then "flex-1 min-h-0 overflow-auto px-4 py-3" else "hidden")) {
      Show (when' = (run ()).IsSome, fallback = (div (class' = "text-xs opacity-50") { "Runs appear here: stdout, stderr and exit code of each Run." })) {
        div (class' = "flex items-center gap-2 text-xs mb-2") {
          span (class' = exitBadge (run () |> Option.map (fun r -> r.exitCode) |> Option.defaultValue 0)) {
            "exit " + string (run () |> Option.map (fun r -> r.exitCode) |> Option.defaultValue 0)
          }
          span (class' = "font-semibold") { field (fun r -> r.project) }
          span (class' = "font-mono opacity-60") {
            "gen " + field (fun r -> r.generation) + " · src " + shortHash (field (fun r -> r.sourceVersion))
          }
          span (class' = "opacity-50") { run () |> Option.map (fun r -> clockTime r.at) |> Option.defaultValue "" }
        }
        pre (class' = "panel diagnostic-trace") { TraceText (fun () -> field (fun r -> r.stdout)) }
        div (class' = (if field (fun r -> r.stderr) = "" then "hidden" else "mt-2")) {
          DiagnosticBlock false (fun () -> "Standard error") (fun () -> field (fun r -> r.stderr))
        }
      }
    }
    div (class' = (if tab () = "activity" then "flex-1 min-h-0 overflow-auto px-4 py-1" else "hidden")) {
      Show (when' = (model.activity.Length > 0), fallback = (div (class' = "text-xs opacity-50 py-2") { "Command outcomes appear here." })) {
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

[<SolidComponent>]
let App () =
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
    if Bridge.send wire then Interop.consoleLog ("→ " + wire)
    else Model.abandon stores commands frame

  let receive (payload: string) =
    match Codec.decodeEvent payload with
    | Ok event ->
      match event with
      | Health _ | Leases _ | Uptime _ -> ()
      | _ -> Interop.consoleLog ("← " + payload)
      Model.apply stores commands event
    | Error message -> Interop.consoleLog ("← undecodable (" + message + "): " + payload)

  Bridge.start receive (fun next ->
    match next with
    | Bridge.Retrying _ -> Model.abandonAll stores commands
    | Bridge.Connecting | Bridge.Connected -> ()
    setLink next)

  // ── View ─────────────────────────────────────────────────────────────────
  div (class' = "flex flex-col h-screen bg-base-100 text-base-content text-sm") {
    TopBar link dark toggleTheme (fun () -> dispatch RequestSnapshot)
    HealthStrip model stores.Uptime
    Show (when' = not (connected ())) {
      div (class' = "px-4 py-1 text-xs bg-warning text-warning-content") {
        "Bridge disconnected: commands are disabled and the state below is the last received; "
        + linkText (link ())
        + "."
      }
    }
    Show (when' = model.protocolMismatch) {
      div (class' = "px-4 py-1 text-xs bg-error text-error-content") {
        "The daemon speaks a different UI protocol version; reload after updating Bozzetto."
      }
    }
    div (class' = (if connected () then "flex-1 min-h-0 overflow-auto" else "flex-1 min-h-0 overflow-auto opacity-70")) {
      div (class' = "grid grid-cols-1 lg:grid-cols-3 gap-4 p-4") {
        div (class' = "lg:col-span-2 flex flex-col gap-3 min-w-0") {
          OpenBar local connected dispatch
          SessionsPanel model local connected dispatch
        }
        div (class' = "flex flex-col gap-3 min-w-0") {
          WorkerPanel model local connected dispatch
          LeasesPanel model
        }
      }
    }
    OutputPane model
  }
