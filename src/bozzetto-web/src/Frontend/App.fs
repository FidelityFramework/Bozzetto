// Bozzetto's browser UI: separate Dashboard and Work views on Ranvier
// (Ranvier.View.Dom Program views over Ranvier signals). Every action is a
// Command sent over the bridge; the backend is the update function; Events
// fold into the signals (Model.fs), and each binding rewrites only its own DOM
// node when a signal it reads changes.
module Bozzetto.Web.Frontend.App

open Fable.Core
open global.Ranvier
open Bozzetto.Web.Shared.Protocol
open Bozzetto.Web.Frontend
open Bozzetto.Web.Frontend.Model
open Bozzetto.Web.Frontend.View

// ── Elements ───────────────────────────────────────────────────────────────

let private div m c = el "div" m c
let private span m c = el "span" m c
let private para m c = el "p" m c
let private h2 m c = el "h2" m c
let private h3 m c = el "h3" m c
let private h4 m c = el "h4" m c
let private pre m c = el "pre" m c
let private button m c = el "button" m c
let private anchor m c = el "a" m c
let private details m c = el "details" m c
let private summary m c = el "summary" m c
let private dialog m c = el "dialog" m c
let private label m c = el "label" m c
let private select m c = el "select" m c
let private option m c = el "option" m c
let private table m c = el "table" m c
let private thead m c = el "thead" m c
let private tbody m c = el "tbody" m c
let private tr m c = el "tr" m c
let private th m c = el "th" m c
let private td m c = el "td" m c
let private input m = el "input" m [||]

// ── Display helpers (pure; full class names so Tailwind keeps them) ────────

[<Emit("location.pathname")>]
let private pathname () : string = jsNative

[<Emit("document.title = $0")>]
let private setTitle (value: string) : unit = jsNative

// Both routes share a bundle and bridge, not a layout. The route is fixed for
// this page lifetime; ordinary links open a fresh, independent page.
let private isWorkPage () = pathname().TrimEnd('/') = "/work"

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

let private processRole = function
  | "composer-worker" -> "Compiler worker"
  | role -> role

// A CSS-only spinner. (DaisyUI's spinner component masks are data-URI SVGs that
// carry the SVG namespace URL, which the self-contained check forbids.)
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
// A component function builds its view once. Anything that changes is read
// inside a reader (`rx`, `clsRx`, `attrRx`, `flagRx`, a list's items or a
// condition), never in the function body, so the read subscribes only the
// binding that shows it. A component with local signals creates them in
// `comp`, once per mounted instance.

let private information (title: string) (paragraphs: unit -> string array) =
  div [| cls "inline-flex" |] [|
    button [| cls "information-button"; View.title (title + " information"); aria "label" (title + " information"); on "click" Interop.openInformation |] [| text "ⓘ" |]
    dialog [| cls "modal"; aria "label" title; on "click" (fun event -> if Interop.isCurrentTarget event then Interop.closeInformation event) |] [|
      div [| cls "modal-box" |] [|
        h3 [| cls "text-base font-semibold" |] [| text title |]
        div [| cls "space-y-3 text-sm py-4" |] [|
          eachIndexed paragraphs (fun paragraph -> para [||] [| rx paragraph |])
        |]
        div [| cls "modal-action" |] [|
          button [| cls "btn btn-sm btn-ghost"; on "click" Interop.closeInformation |] [| text "Close" |]
        |]
      |]
    |]
  |]

let private traceText (value: unit -> string) =
  each (fun (segment: DiagnosticText.Segment) -> segment.id) (fun () -> DiagnosticText.segments (value ())) (fun segment ->
    span [| clsRx (fun () -> diagnosticTokenClass (segment ()).severity) |] [| rx (fun () -> (segment ()).text) |])

let private diagnosticBlock (errorContext: bool) (heading: unit -> string) (value: unit -> string) =
  div [| cls (if errorContext then "diagnostic diagnostic-error" else "diagnostic") |] [|
    div [| cls "diagnostic-header" |] [|
      span [| cls (if errorContext then "badge badge-sm badge-outline badge-error" else "badge badge-sm badge-ghost") |] [|
        text (if errorContext then "Diagnostic" else "Output")
      |]
      h3 [| cls "text-sm font-semibold" |] [| rx heading |]
    |]
    // A text child preserves the trace without executing markup from a compiler.
    pre [| cls "diagnostic-trace" |] [| traceText value |]
  |]

let private commandOutcome (outcome: unit -> OutcomeView option) =
  let field pick = outcome () |> Option.map pick |> Option.defaultValue ""
  show (fun () -> (outcome ()).IsSome) (fun () ->
    showElse (fun () -> outcome () |> Option.exists (fun value -> not value.ok))
      (fun () ->
        div [| cls "diagnostic diagnostic-error" |] [|
          div [| cls "diagnostic-header" |] [|
            span [| cls "badge badge-sm badge-outline badge-error" |] [| text "Refused" |]
            h3 [| cls "text-sm font-semibold" |] [| text "Command outcome" |]
            span [| cls "text-xs opacity-70" |] [| rx (fun () -> field (fun value -> clockTime value.at)) |]
          |]
          pre [| cls "diagnostic-trace" |] [| traceText (fun () -> field (fun value -> value.text)) |]
        |])
      (fun () ->
        div [| cls "text-xs text-success whitespace-pre-wrap break-words" |] [|
          rx (fun () -> field (fun value -> clockTime value.at + " · " + value.text))
        |]))

let private retrievalPill (stores: Stores) =
  comp (fun () ->
    let presentation = Api.createMemo (fun _ ->
      let status = stores.Retrieval.Value
      let badge style label details busy =
        let checkoutDetail =
          if status.branchStatus = "unmanaged_checkout" then "This checkout is outside Fanion's managed index." else ""
        let tooltip = List.append details [ checkoutDetail; status.note ] |> List.filter (fun line -> line <> "") |> String.concat "\n"
        {| style = style; label = label; tooltip = tooltip; busy = busy |}
      let listDetail label sources =
        if Array.isEmpty sources then "" else label + ": " + String.concat ", " sources
      let sourcesInLabel sources =
        if Array.isEmpty sources then "" else " (" + String.concat ", " sources + ")"
      let queued =
        if status.queuedRetrievals > 0 then " · " + string status.queuedRetrievals + " queued" else ""
      let reasons = listDetail "Reasons" status.reasons
      let pending = listDetail "Pending sources" status.pendingSources
      match status.branchStatus, status.state with
      | "quarantined_branch", _ ->
        let generation = if status.generation > 0L then " · gen " + string status.generation else ""
        let label = "Quarantined branch (" + status.branch + ")" + generation
        let detail = "Quarantined branch (" + status.branch + ", intentionally outside retrieval index)"
        badge "badge-plum" label [ detail ] false
      | _, "fresh" ->
        let prefix = if status.branchStatus = "on_main_indexed" then "On main · " else ""
        let label = prefix + "Index gen " + string status.generation + " · Fresh"
        let snapshot = if status.snapshotId = "" then "" else "Snapshot " + shortHash status.snapshotId
        let published = if status.publishedAt > 0.0 then "Published " + dateTime status.publishedAt else ""
        badge "badge-success" label [ snapshot; published ] false
      | _, "crossing_held" ->
        let sources = if Array.isEmpty status.pendingSources then status.expectedSources else status.pendingSources
        let label = "Crossing held" + sourcesInLabel sources + "…" + queued
        badge "badge-warning gap-1" label [ listDetail "Expected sources" status.expectedSources; pending; reasons ] true
      | _, "indexing" ->
        let label = "Indexing" + sourcesInLabel status.pendingSources + "…" + queued
        badge "badge-warning gap-1" label [ pending; listDetail "Stale sources" status.staleSources; reasons ] true
      | _, "unconfigured" -> badge "badge-ghost" "Index unconfigured" [] false
      | _ -> badge "badge-error" "Index unavailable" [ reasons ] false)
    span [| cls "retrieval-pill min-w-0"; attr "role" "status"; flagRx "hidden" (fun () -> not stores.RetrievalSeen.Value) |] [|
      span [|
        clsRx (fun () -> "retrieval-badge badge badge-sm " + presentation.Value.style + " max-w-full h-auto py-0.5")
        titleRx (fun () -> presentation.Value.tooltip)
      |] [|
        span [| aria "hidden" "true"; clsRx (fun () -> if presentation.Value.busy then spinner else "hidden") |] [||]
        span [| cls "min-w-0 break-words" |] [| rx (fun () -> presentation.Value.label) |]
      |]
    |])

let private topBar (stores: Stores) (link: Signal<Bridge.Link>) (dark: Signal<bool>) (toggleTheme: unit -> unit) =
  let workPage = isWorkPage ()
  div [| cls "navbar flex-wrap shrink-0 min-h-12 px-4 py-2 gap-2 bg-base-100 shadow-sm border-b border-base-content/10" |] [|
    div [| cls "grow flex flex-wrap items-center gap-x-3 gap-y-1 min-w-0" |] [|
      span [| cls "font-heading font-extrabold text-lg tracking-tight" |] [| text "Bozzetto" |]
      div [| cls "flex items-center gap-1" |] [|
        anchor [|
          attr "href" "/dashboard"
          cls (if workPage then "btn btn-ghost btn-sm" else "btn btn-ghost btn-sm btn-active text-accent")
          View.title "Daemon resources and health"
        |] [| text "Dashboard" |]
        anchor [|
          attr "href" "/work"
          cls (if workPage then "btn btn-ghost btn-sm btn-active text-accent" else "btn btn-ghost btn-sm")
          View.title "Compiler worker, project sessions, build/test leases and run output"
        |] [| text "Work" |]
      |]
      span [| cls "flex items-center gap-1.5 text-xs" |] [|
        span [| clsRx (fun () -> linkDot link.Value) |] [||]
        span [| cls "opacity-70" |] [| rx (fun () -> linkText link.Value) |]
      |]
    |]
    div [| cls "flex max-w-full items-center gap-2" |] [|
      retrievalPill stores
      button [| cls "btn btn-ghost btn-sm btn-circle shrink-0 text-base"; View.title "Toggle theme"; on "click" (fun _ -> toggleTheme ()) |] [|
        rx (fun () -> if dark.Value then "☀" else "☾")
      |]
    |]
  |]

// The uptime the daemon's clock pushed, with the start time from Welcome.
let private uptimeText (uptime: float) (startedAt: float) =
  if uptime < 0.0 then "up since " + dateTime startedAt
  else "up " + duration uptime + " · since " + dateTime startedAt

// Live fleet rebalancing and silent-agent visibility banner (pure Ranvier binding to pushed FleetBalance).
let private fleetBalanceBanner (stores: Stores) =
  let balance () = stores.FleetBalance.Value
  let isUrgent () =
    let b = balance ()
    (b.hasAdvisory && b.advisory.throttled)
    || (b.silentAlerts |> Array.exists (fun a -> a.kind = "silent_auditor"))
  let silentLabel kind =
    match kind with
    | "silent_auditor" -> "Silent auditor"
    | "silent_implementer" -> "Silent implementer"
    | _ -> "Silent observer"
  show (fun () ->
    stores.FleetBalanceSeen.Value
    && (balance().hasAdvisory || balance().silentAlerts.Length > 0)) (fun () ->
    div [|
      clsRx (fun () ->
        let tone = if isUrgent () then "border-error/40" else "border-warning/40"
        "fleet-balance-banner flex flex-col gap-2 p-3 rounded-box bg-base-200 border text-xs w-full max-w-full min-w-0 " + tone)
      attr "role" "status"
    |] [|
      show (fun () -> (balance ()).hasAdvisory) (fun () ->
        let adv () = (balance ()).advisory
        div [| cls "fleet-balance-rebalance flex flex-wrap items-center gap-2 min-w-0" |] [|
          span [| clsRx (fun () -> if (adv ()).throttled then "badge badge-sm badge-error" else "badge badge-sm badge-warning") |] [|
            rx (fun () -> if (adv ()).throttled then "Z13 throttled" else "Rebalance advisory")
          |]
          span [| cls "font-semibold" |] [|
            rx (fun () -> "Recommend " + (adv ()).recommendedNode.ToUpperInvariant())
          |]
          span [| cls "badge badge-xs badge-ghost font-mono" |] [|
            rx (fun () -> "policy " + (adv ()).policy)
          |]
          span [| cls "opacity-80 min-w-0 break-words" |] [|
            rx (fun () ->
              let cands =
                (adv ()).candidates
                |> Array.map (fun c ->
                  c.nodeId.ToUpperInvariant() + " " + string c.freeSlots + "/" + string c.leaseCap + " free (" + bytes c.availableRamBytes + ")")
                |> String.concat " · "
              let detail = (adv ()).reason
              if cands = "" then detail else detail + " [" + cands + "]")
          |]
        |])
      show (fun () -> (balance ()).silentAlerts.Length > 0) (fun () ->
        div [| cls "fleet-balance-silent flex flex-col gap-1 min-w-0" |] [|
          div [| cls "flex flex-wrap items-center gap-2 min-w-0 opacity-80" |] [|
            span [| cls "font-semibold" |] [|
              rx (fun () -> string (balance ()).silentAlerts.Length + " idle/silent cohort member(s)")
            |]
            span [| cls "font-mono text-xs" |] [|
              rx (fun () ->
                let b = balance ()
                string b.activeLeaseCount + " active lease · " + string b.openClaimCount + " open claims · " + string b.unauditedRunCount + " unaudited · " + string b.dirtyTrancheCount + " dirty")
            |]
          |]
          each (fun (a: SilentAgentAlertView) -> a.memberKey) (fun () -> (balance ()).silentAlerts) (fun alert ->
            div [| cls "fleet-silent-row flex flex-wrap items-center gap-2 min-w-0 break-words" |] [|
              span [|
                clsRx (fun () ->
                  if (alert ()).kind = "silent_auditor" then "badge badge-xs badge-error"
                  else "badge badge-xs badge-warning")
              |] [| rx (fun () -> silentLabel (alert ()).kind) |]
              span [| cls "font-semibold" |] [|
                rx (fun () -> (alert ()).displayName + " (" + (alert ()).agentFamily + ", " + (alert ()).role + ")")
              |]
              span [| cls "font-mono opacity-75" |] [|
                rx (fun () -> "silent " + string (int64 (alert ()).idleSeconds) + "s")
              |]
              span [| cls "opacity-80 min-w-0 break-words" |] [| rx (fun () -> (alert ()).reason) |]
            |])
        |])
    |])

// The daemon's health, bound directly to the signals the pushed Health,
// Welcome and Uptime events write: a pushed verdict or memory-pressure change
// rewrites its badge in place, with no reload, request or timer.
let private healthStrip (stores: Stores) =
  let health () = stores.Health.Value
  div [| cls "daemon-health flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-1.5 text-xs bg-base-200 border-b border-base-content/10" |] [|
    showElse (fun () -> stores.HealthSeen.Value)
      (fun () ->
        fragment [|
          span [| clsRx (fun () -> overallBadge (health ()).overall) |] [| rx (fun () -> (health ()).overall) |]
          span [| cls "font-mono" |] [| rx (fun () -> "v" + (health ()).version) |]
          span [| cls "opacity-70" |] [| rx (fun () -> "pid " + string (health ()).pid + " · :" + string (health ()).port) |]
          span [| cls "opacity-70 font-mono"; View.title "Daemon uptime, pushed by the daemon's clock each second, and the process start time" |] [|
            rx (fun () -> uptimeText stores.Uptime.Value stores.StartedAt.Value)
          |]
          span [||] [|
            span [| cls "opacity-50 mr-1" |] [| text "boz rss" |]
            span [| cls "font-mono" |] [| rx (fun () -> bytes (health ()).rss) |]
          |]
          span [||] [|
            span [| cls "opacity-50 mr-1" |] [| text "machine" |]
            span [| cls "font-mono" |] [| rx (fun () -> bytes (health ()).available + " available / " + bytes (health ()).total) |]
          |]
          span [||] [|
            span [| cls "opacity-50 mr-1" |] [| text "owned cpu" |]
            span [| cls "font-mono" |] [| rx (fun () -> fixedPoint (health ()).cpu 1 + "%") |]
          |]
          span [| clsRx (fun () -> pressureBadge (health ()).pressure); titleRx (fun () -> (health ()).note) |] [|
            rx (fun () -> "memory " + (health ()).pressure)
          |]
          show (fun () -> (health ()).pressure <> "Normal" && (health ()).note <> "") (fun () ->
            span [| cls "opacity-60 text-xs" |] [| rx (fun () -> (health ()).note) |])
          // The age of what this strip shows: the daemon reads when asked, not on a timer, and says when it last looked.
          span [| cls "health-age opacity-50 font-mono"; View.title "When the daemon last read the machine, and last took its own record" |] [|
            rx (fun () ->
              let now = stores.StartedAt.Value + stores.Uptime.Value
              let h = health ()
              "read " + duration (max 0.0 (now - h.sampledAt)) + " ago"
              + (if h.recordAt > 0.0 then " · record " + duration (max 0.0 (now - h.recordAt)) + " ago" else " · no record yet"))
          |]
          each (fun (process': ProcessView) -> process'.id) (fun () -> (health ()).processes) (fun process' ->
            span [| cls "font-mono opacity-60" |] [| rx (fun () -> let x = process' () in processRole x.role + " " + string x.pid + " " + bytes x.rss) |])
          each (fun (alarm: AlarmView) -> alarm.id) (fun () -> (health ()).alarms) (fun alarm ->
            span [| cls "badge badge-sm badge-warning"; titleRx (fun () -> (alarm ()).message) |] [| rx (fun () -> "⚠ " + (alarm ()).signal + " " + (alarm ()).state) |])
        |])
      (fun () -> span [| cls "opacity-50" |] [| text "waiting for daemon health…" |])
  |]

let private metricText (m: ResourceMetricView) =
  if not m.known then "unavailable / warming up"
  elif m.unit = "bytes" then bytes m.value
  elif m.unit = "bytes/s" then bytes m.value + "/s"
  elif m.unit = "percent" then fixedPoint m.value 1 + "%"
  else fixedPoint m.value 0

let private resourceNodeLabel (node: NodeWorkView) =
  if node.local then "Workstation" else node.id.ToUpperInvariant()

let private headlineMetric (m: ResourceMetricView) =
  m.id = "Host CPU busy" || m.id = "RAM occupied" || m.id = "RAM available" || m.id = "RAM total (OS managed)" || m.id = "Swap used" || m.id.Contains "GPU busy"

let private hostPanel (stores: Stores) (connected: unit -> bool) (selectedNode: unit -> NodeWorkView option) (selectNode: string -> unit) (dispatch: Command -> unit) =
  let resources () = stores.Resources.Value
  let status () =
    if not (connected ()) then "disconnected — last reading only"
    elif (resources ()).status = "observing" && stores.StartedAt.Value + stores.Uptime.Value - (resources ()).sampledAt > 6000. then "stale — no recent observation"
    else (resources ()).status
  let unavailable () = not (connected ()) || (selectedNode ()).IsNone
  div [| cls "card card-compact panel" |] [|
    div [| cls "card-body gap-3" |] [|
      div [| cls "flex flex-wrap items-center gap-2" |] [|
        h2 [| cls "text-lg font-bold flex-1" |] [| text "Whole-machine resources" |]
        span [| cls "badge badge-outline" |] [| rx status |]
        information "Resource readings" (fun () -> [|
          "Linux counters are sampled every two seconds during the selected observation window. Collection stops when the window ends."
          "Process CPU uses 100% for one logical CPU. RSS includes shared pages. Thread count shows the number of process threads."
          (resources ()).note
          "Sequence " + (resources ()).sequence + " · interval " + fixedPoint ((resources ()).interval / 1000.) 1 + "s · collection " + fixedPoint (resources ()).cost 1 + "ms"
        |])
        button [| cls "btn btn-sm btn-accent"; flagRx "disabled" unavailable; on "click" (fun _ -> dispatch (ObserveResources 120)) |] [| text "Observe for 2 minutes" |]
        button [| cls "btn btn-sm btn-ghost"; flagRx "disabled" unavailable; on "click" (fun _ -> dispatch (ObserveResources 0)) |] [| text "Stop" |]
      |]
      label [| cls "flex flex-wrap items-center gap-2 text-sm" |] [|
        span [| cls "font-semibold" |] [| text "Node" |]
        select [|
          cls "select select-bordered select-sm"
          aria "label" "Resource node"
          selectValue (fun () -> selectedNode () |> Option.map _.id |> Option.defaultValue "")
          flagRx "disabled" (fun () -> not (connected ()) || stores.Nodes.Value.Length = 0)
          on "change" (fun event -> selectNode (Interop.inputValue event))
        |] [|
          show (fun () -> stores.Nodes.Value.Length = 0) (fun () -> option [| attr "value" "" |] [| text "Awaiting node observations" |])
          each (fun (node: NodeWorkView) -> node.id) (fun () -> stores.Nodes.Value) (fun node ->
            option [| attrRx "value" (fun () -> (node ()).id) |] [| rx (fun () -> resourceNodeLabel (node ())) |])
        |]
      |]
      show (fun () -> selectedNode () |> Option.exists (fun node -> node.status <> "available")) (fun () ->
        para [| cls "resource-node-availability text-xs opacity-70" |] [|
          rx (fun () -> selectedNode () |> Option.map (fun node -> resourceNodeLabel node + " work observation: " + node.status + ". " + node.note) |> Option.defaultValue "")
        |])
      commandOutcome (fun () -> stores.Outcomes.Value.TryFind "resources")
      show (fun () -> (resources ()).sampledAt > 0.) (fun () ->
        para [| cls "text-xs font-mono opacity-70" |] [| rx (fun () -> "Updated " + clockTime (resources ()).sampledAt) |])
      showElse (fun () -> (resources ()).sequence <> "")
        (fun () ->
          fragment [|
            div [| cls "grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-2" |] [|
              each (fun (m: ResourceMetricView) -> m.id) (fun () -> stores.ResourceMetrics.Value |> Array.filter headlineMetric) (fun metric ->
                div [| cls "panel p-3" |] [|
                  div [| cls "text-xs opacity-70" |] [| rx (fun () -> (metric ()).id) |]
                  div [| cls "text-xl font-mono font-semibold" |] [| rx (fun () -> metricText (metric ())) |]
                |])
            |]
            details [||] [|
              summary [| cls "cursor-pointer text-sm font-semibold" |] [| text "More metrics" |]
              table [| cls "table table-xs" |] [|
                tbody [||] [|
                  each (fun (m: ResourceMetricView) -> m.id) (fun () -> stores.ResourceMetrics.Value) (fun metric ->
                    tr [||] [| td [||] [| rx (fun () -> (metric ()).id) |]; td [| cls "font-mono" |] [| rx (fun () -> metricText (metric ())) |] |])
                |]
              |]
            |]
            h3 [| cls "text-sm font-semibold" |] [| text "Processes" |]
            para [| cls "text-xs opacity-50" |] [|
              rx (fun () -> string (resources ()).count + " processes · " + string (resources ()).unreadable + " unreadable · " + string (resources ()).omitted + " omitted")
            |]
            div [| cls "overflow-x-auto" |] [|
              table [| cls "table table-xs" |] [|
                thead [||] [| tr [||] [| th [||] [| text "process" |]; th [||] [| text "PID / parent" |]; th [||] [| text "CPU" |]; th [||] [| text "RSS" |]; th [||] [| text "threads" |] |] |]
                tbody [||] [|
                  each (fun (row: ResourceProcessView) -> row.id) (fun () -> stores.ResourceProcesses.Value) (fun row ->
                    tr [||] [|
                      td [| cls "font-mono"; titleRx (fun () -> (row ()).context + " · identity " + (row ()).id) |] [| rx (fun () -> (row ()).name) |]
                      td [| cls "font-mono" |] [| rx (fun () -> string (row ()).pid + " / " + string (row ()).parent) |]
                      td [| cls "font-mono" |] [| rx (fun () -> if (row ()).cpuKnown then fixedPoint (row ()).cpu 1 + "%" else "—") |]
                      td [| cls "font-mono" |] [| rx (fun () -> bytes (row ()).rss) |]
                      td [| cls "font-mono" |] [| rx (fun () -> string (row ()).threads) |]
                    |])
                |]
              |]
            |]
          |])
        (fun () -> para [| cls "text-sm opacity-70" |] [| text "No measurements received for this node." |])
    |]
  |]

// This roster is daemon-owned processes only, not a whole-host inventory.
let private resourcesPanel (stores: Stores) =
  div [| cls "card card-compact panel" |] [|
    div [| cls "card-body gap-2" |] [|
      div [| cls "flex items-center gap-2" |] [|
        h2 [| cls "text-sm font-bold uppercase tracking-wider opacity-80 flex-1" |] [| text "Daemon resources" |]
        information "Daemon snapshot" (fun () -> [|
          "These readings capture the daemon and its compiler worker at connection time."
          "CPU is normalized by processor count. Summed RSS can count shared pages more than once."
        |])
      |]
      showElse (fun () -> stores.HealthSeen.Value)
        (fun () ->
          div [| cls "overflow-x-auto" |] [|
            table [| cls "table table-xs" |] [|
              thead [||] [|
                tr [||] [|
                  th [||] [| text "role" |]
                  th [||] [| text "pid" |]
                  th [||] [| text "resident" |]
                  th [||] [| text "normalized CPU" |]
                |]
              |]
              tbody [||] [|
                each (fun (process': ProcessView) -> process'.id) (fun () -> stores.Health.Value.processes) (fun process' ->
                  tr [||] [|
                    td [| cls "font-mono" |] [| rx (fun () -> processRole (process' ()).role) |]
                    td [| cls "font-mono" |] [| rx (fun () -> string (process' ()).pid) |]
                    td [| cls "font-mono" |] [| rx (fun () -> bytes (process' ()).rss) |]
                    td [| cls "font-mono" |] [| rx (fun () -> fixedPoint (process' ()).cpu 1 + "%") |]
                  |])
              |]
            |]
          |])
        (fun () -> para [| cls "text-xs opacity-50" |] [| text "Waiting for daemon health…" |])
    |]
  |]

let private openBar (stores: Stores) (connected: unit -> bool) (dispatch: Command -> unit) =
  comp (fun () ->
    let path = Api.createSignal ""
    let submit () =
      let project = path.Value.Trim()
      if project <> "" then dispatch (OpenProject project)
    div [| cls "flex flex-col gap-1" |] [|
      div [| cls "join w-full" |] [|
        input [|
          cls "input input-bordered input-sm join-item flex-1 font-mono"
          attr "placeholder" "/absolute/path/Project.fidproj"
          propRx "value" (fun () -> path.Value)
          on "input" (fun event -> path.Value <- Interop.inputValue event)
          on "keydown" (fun event -> if Interop.keyOf event = "Enter" then submit ())
        |]
        button [|
          cls "btn btn-sm btn-accent join-item"
          flagRx "disabled" (fun () -> not (connected ()) || stores.Pending.Value.ContainsKey OpenKey || path.Value.Trim() = "")
          on "click" (fun _ -> submit ())
        |] [| text "Open project" |]
      |]
      commandOutcome (fun () -> stores.Outcomes.Value.TryFind OpenKey)
    |])

let private sessionCard (session: unit -> SessionView) (stores: Stores) (connected: unit -> bool) (dispatch: Command -> unit) =
  comp (fun () ->
    let editLabel = Api.createSignal "source edit"
    let args = Api.createSignal "[]"
    let argsError = Api.createSignal ""
    let id = (Api.untrack session).id
    let reservation () = stores.Reservations.Value.TryFind id
    let pending () = stores.Pending.Value.TryFind id
    let ready () = connected () && not (session ()).closed && (pending ()).IsNone
    let reserve () = dispatch (Reserve { ReserveEdit.Target = targetOf (session ()); Label = editLabel.Value })
    let build () =
      match reservation () with
      | Some token -> dispatch (Build { BuildReserved.Target = targetOf (session ()); Reservation = token })
      | None -> ()
    let run () =
      match Interop.parseArguments args.Value with
      | Some arguments ->
        argsError.Value <- ""
        dispatch (Run { RunCurrent.Target = targetOf (session ()); Arguments = arguments })
      | None -> argsError.Value <- "Arguments must be a JSON array of strings, e.g. [\"--verbose\"]."
    let current () = (session ()).current
    div [| clsRx (fun () -> if (session ()).closed then "card card-compact panel opacity-60" else "card card-compact panel") |] [|
      div [| cls "card-body gap-2" |] [|
        div [| cls "flex items-start gap-2" |] [|
          div [| cls "flex-1 min-w-0" |] [|
            div [| cls "font-heading font-bold truncate"; titleRx (fun () -> (session ()).project) |] [| rx (fun () -> fileName (session ()).project) |]
            div [| cls "text-xs font-mono opacity-50 truncate"; titleRx (fun () -> (session ()).project) |] [| rx (fun () -> (session ()).project) |]
          |]
          div [| cls "flex flex-wrap justify-end gap-1" |] [|
            show (fun () -> (session ()).busy) (fun () ->
              span [| cls "badge badge-sm badge-info gap-1" |] [| span [| cls spinner |] [||]; text "busy" |])
            show (fun () -> (pending ()).IsSome) (fun () ->
              span [| cls "badge badge-sm badge-outline badge-secondary gap-1" |] [|
                span [| cls spinner |] [||]
                rx (fun () -> pending () |> Option.defaultValue "")
              |])
            show (fun () -> (session ()).closed) (fun () -> span [| cls "badge badge-sm badge-neutral" |] [| text "closed" |])
            show (fun () -> not (session ()).fresh) (fun () -> span [| cls "badge badge-sm badge-ghost"; View.title "Status is being re-read" |] [| text "stale" |])
            show (fun () -> (session ()).revocationPending) (fun () -> span [| cls "badge badge-sm badge-warning" |] [| text "revoking" |])
            show (fun () -> (session ()).cleanupPending || (session ()).formatterCleanupPending) (fun () ->
              span [| cls "badge badge-sm badge-warning" |] [| text "cleanup" |])
            show (fun () -> (reservation ()).IsSome) (fun () ->
              span [| cls "badge badge-sm badge-plum font-mono"; titleRx (fun () -> reservation () |> Option.defaultValue "") |] [|
                rx (fun () -> "reserved " + shortHash (reservation () |> Option.defaultValue ""))
              |])
          |]
        |]
        div [| cls "text-xs font-mono opacity-60 truncate" |] [|
          rx (fun () -> "session " + (session ()).session + " · epoch " + (session ()).epoch + " · rev " + (session ()).generation)
        |]
        showElse (fun () -> (session ()).hasCurrent)
          (fun () ->
            fragment [|
              div [| cls "text-xs font-mono flex flex-wrap items-center gap-x-3 gap-y-0.5 border-l-2 border-success pl-2" |] [|
                span [| cls "text-success" |] [|
                  span [| cls "mr-1" |] [| text "✓ accepted gen" |]
                  rx (fun () -> (current ()).generation)
                |]
                span [| cls "chip-code"; titleRx (fun () -> (current ()).sha) |] [| rx (fun () -> shortHash (current ()).sha) |]
                span [| cls "opacity-70" |] [| rx (fun () -> "src " + shortHash (current ()).sourceVersion) |]
                span [| cls "opacity-70" |] [|
                  rx (fun () ->
                    let c = current ()
                    string c.compiled + " compiled · " + string c.reused + " reused · "
                    + string c.retired + " retired · " + string c.changed + " witnesses changed")
                |]
              |]
              // What the change recompiled and what it reused, region by region: the
              // differential fact itself, not only its totals.
              show (fun () -> (current ()).regions.Length > 0) (fun () ->
                table [| cls "table table-xs mt-1" |] [|
                  thead [||] [|
                    tr [||] [|
                      th [||] [| text "region" |]
                      th [||] [| text "witness" |]
                      th [| cls "text-right" |] [| text "visits" |]
                      th [||] [| text "object" |]
                    |]
                  |]
                  tbody [||] [|
                    each (fun (region: RegionRow) -> region.Identity) (fun () -> (current ()).regions) (fun region ->
                      tr [||] [|
                        td [| cls "font-mono break-all max-w-[28rem]" |] [| rx (fun () -> (region ()).Identity) |]
                        td [||] [| rx (fun () -> (region ()).Witness) |]
                        td [| cls "text-right font-mono" |] [| rx (fun () -> string (region ()).Visits) |]
                        td [||] [| rx (fun () -> (region ()).Object) |]
                      |])
                  |]
                |])
            |])
          (fun () -> div [| cls "text-xs opacity-50" |] [| text "no accepted artifact" |])
        each (fun (notice: NoticeView) -> notice.id) (fun () -> (session ()).notices) (fun notice ->
          diagnosticBlock true (fun () -> (notice ()).label) (fun () -> (notice ()).text))
        div [| cls "flex flex-wrap items-center gap-1.5" |] [|
          div [| cls "join" |] [|
            input [|
              cls "input input-bordered input-xs join-item w-36"
              attr "placeholder" "edit label"
              propRx "value" (fun () -> editLabel.Value)
              on "input" (fun event -> editLabel.Value <- Interop.inputValue event)
            |]
            button [| cls "btn btn-xs btn-plum join-item"; flagRx "disabled" (fun () -> not (ready ())); on "click" (fun _ -> reserve ()) |] [| text "Reserve" |]
          |]
          button [|
            cls "btn btn-xs btn-outline btn-accent"
            View.title "Build the reserved revision"
            flagRx "disabled" (fun () -> not (ready ()) || (reservation ()).IsNone)
            on "click" (fun _ -> build ())
          |] [| text "Build" |]
          div [| cls "join" |] [|
            input [|
              cls "input input-bordered input-xs join-item w-36 font-mono"
              attr "placeholder" "JSON args: []"
              propRx "value" (fun () -> args.Value)
              on "input" (fun event -> args.Value <- Interop.inputValue event)
              on "keydown" (fun event -> if Interop.keyOf event = "Enter" then run ())
            |]
            button [| cls "btn btn-xs btn-outline btn-accent join-item"; flagRx "disabled" (fun () -> not (ready ()) || not (session ()).hasCurrent); on "click" (fun _ -> run ()) |] [| text "Run" |]
          |]
          button [| cls "btn btn-xs btn-ghost"; flagRx "disabled" (fun () -> not (connected ()) || (session ()).closed); on "click" (fun _ -> dispatch (Cancel(targetOf (session ())))) |] [| text "Cancel" |]
          button [| cls "btn btn-xs btn-rust"; flagRx "disabled" (fun () -> not (connected ()) || (session ()).closed); on "click" (fun _ -> dispatch (CloseSession(targetOf (session ())))) |] [| text "Close" |]
        |]
        div [| clsRx (fun () -> if argsError.Value = "" then "hidden" else "text-xs text-error") |] [| rx (fun () -> argsError.Value) |]
        commandOutcome (fun () -> stores.Outcomes.Value.TryFind id)
      |]
    |])

let private sessionsPanel (stores: Stores) (connected: unit -> bool) (dispatch: Command -> unit) =
  let sessions () = stores.Sessions.Value
  div [| cls "flex flex-col gap-2" |] [|
    div [| cls "flex items-baseline gap-2" |] [|
      h2 [| cls "text-sm font-bold uppercase tracking-wider opacity-80" |] [| text "Composer sessions" |]
      span [| cls "text-xs opacity-50" |] [| rx (fun () -> string (sessions ()).Length) |]
    |]
    showElse (fun () -> (sessions ()).Length > 0)
      (fun () ->
        div [| clsRx (fun () -> if (sessions ()).Length = 1 then "grid grid-cols-1 gap-2" else "grid grid-cols-1 xl:grid-cols-2 gap-2") |] [|
          each (fun (session: SessionView) -> session.id) sessions (fun session -> sessionCard session stores connected dispatch)
        |])
      (fun () ->
        div [| cls "text-sm opacity-60 border border-dashed border-base-content/20 rounded-xl p-6 text-center" |] [| text "No open projects." |])
  |]

let private workerPanel (stores: Stores) (connected: unit -> bool) (dispatch: Command -> unit) =
  comp (fun () ->
    // Armed by the first click, fired by the second; leaving the button (pointer
    // or focus) disarms it. No timer: the page keeps none but reconnect backoff.
    let confirming = Api.createSignal false
    let worker () = stores.Worker.Value
    let retire () =
      if confirming.Value then
        confirming.Value <- false
        dispatch (RetireWorker { WorkerTarget.Host = (worker ()).host; Epoch = (worker ()).epoch })
      else
        confirming.Value <- true
    div [| cls "card card-compact panel" |] [|
      div [| cls "card-body gap-1.5" |] [|
        div [| cls "flex items-center gap-2" |] [|
          h2 [| cls "text-sm font-bold uppercase tracking-wider opacity-80 flex-1" |] [| text "Compiler worker" |]
          span [| clsRx (fun () -> workerBadge (worker ()).state) |] [| rx (fun () -> (worker ()).state) |]
          information "Compiler worker" (fun () -> [|
            "Opening a Clef project starts the worker. BOZZETTO_COMPOSER_WORKER selects the installed worker."
            "Retire the worker before replacing compiler binaries. Retirement closes its sessions."
          |])
        |]
        show (fun () -> (worker ()).state = "running") (fun () ->
          fragment [|
            div [| cls "text-xs font-mono opacity-70 break-all" |] [|
              rx (fun () -> "compiler " + (worker ()).compilerVersion + " · pid " + (if (worker ()).pid = "" then "?" else (worker ()).pid))
            |]
            div [| cls "text-xs font-mono opacity-50 break-all" |] [| rx (fun () -> (worker ()).host + " · " + (worker ()).epoch) |]
          |])
        div [| cls "flex items-center gap-2 pt-1" |] [|
          button [|
            clsRx (fun () -> if confirming.Value then "btn btn-xs btn-rust-armed" else "btn btn-xs btn-rust")
            flagRx "disabled" (fun () -> not (connected ()) || (worker ()).state <> "running" || stores.Pending.Value.ContainsKey WorkerKey)
            on "click" (fun _ -> retire ())
            on "mouseleave" (fun _ -> confirming.Value <- false)
            on "blur" (fun _ -> confirming.Value <- false)
          |] [| rx (fun () -> if confirming.Value then "Confirm: retire every session" else "Retire worker") |]
        |]
        commandOutcome (fun () -> stores.Outcomes.Value.TryFind WorkerKey)
      |]
    |])

let private primaryAgent (agent: AgentView) = not agent.aged

let private agentDetails (agent: unit -> AgentView) =
  div [| cls "flex flex-col gap-1 py-2 text-xs break-words" |] [|
    div [| cls "flex items-center gap-2" |] [|
      span [||] [| rx (fun () -> (agent ()).model) |]
      information "Invocation" (fun () ->
        let a = agent ()
        [|
          "Member: " + a.memberId
          "Session: " + a.session + " · run: " + a.id
          "Report sequence: " + a.reportSequence + " · updated " + dateTime a.updatedAt
          "Reported inference endpoint: " + a.endpoint
        |])
    |]
    div [||] [| rx (fun () -> (agent ()).project + " · " + (agent ()).focus) |]
    div [||] [| rx (fun () -> (agent ()).operation) |]
    div [||] [| rx (fun () -> (agent ()).usage + " · " + (agent ()).cost) |]
    div [| cls "opacity-60" |] [|
      rx (fun () -> let a = agent () in a.activity + (if a.omitted = "0" then "" else " · " + a.omitted + " earlier omitted"))
    |]
  |]

let private agentsPanel (stores: Stores) =
  let agents () = stores.Agents.Value
  let newestFirst (rows: AgentView array) = rows |> Array.sortByDescending (fun agent -> agent.updatedAt, agent.id)
  div [| cls "card card-compact panel" |] [|
    div [| cls "card-body gap-2" |] [|
      div [| cls "flex items-center gap-2" |] [|
        h2 [| cls "text-sm font-bold uppercase tracking-wider opacity-80 flex-1" |] [| text "Agent work" |]
        information "Agent accounting" (fun () -> [|
          "Active and recently settled runs stay visible until their dwell window expires, then collapse into Recent completed, newest first. Missing reports do not establish that no work is happening."
          "Presence rows derive from facts the daemon owns, a lease held or queued and cohort membership, so an agent that never reports is still seen."
          "Identity, progress and usage are participant-reported; unknown usage and cost are not zero."
          "Execution host: " + stores.ExecutionHost.Value + " · publication " + stores.AgentSequence.Value
          "The daemon retains its full history; the dashboard may omit oldest terminal runs to fit the wire."
        |])
      |]
      show (fun () -> stores.AgentOmittedRuns.Value <> "0") (fun () ->
        para [| cls "text-xs opacity-70" |] [| rx (fun () -> stores.AgentOmittedRuns.Value + " older terminal runs omitted from this board.") |])
      show (fun () -> stores.AgentRefusal.Value <> "") (fun () ->
        para [| cls "text-xs text-error" |] [| rx (fun () -> stores.AgentRefusal.Value) |])
      showElse (fun () -> (agents () |> Array.exists primaryAgent) || stores.Presence.Value.Length > 0)
        (fun () ->
          fragment [|
            each (fun (agent: AgentView) -> agent.id) (fun () -> agents () |> Array.filter primaryAgent |> newestFirst) (fun agent ->
              div [| cls "border-t border-base-content/10 pt-2 text-xs break-words" |] [|
                div [| cls "font-semibold" |] [|
                  rx (fun () -> (agent ()).name + " · " + (agent ()).role + " · reported " + (agent ()).status)
                  show (fun () ->
                    let a = agent ()
                    not a.aged && a.status <> "running" && a.status <> "unknown") (fun () ->
                    span [| cls "badge badge-xs badge-ghost ml-1" |] [| text "dwelling" |])
                |]
                div [| cls "opacity-60" |] [| rx (fun () -> "Updated " + clockTime (agent ()).updatedAt + " · " + (agent ()).model + " · " + (agent ()).endpoint) |]
                div [||] [| rx (fun () -> (agent ()).project + " · " + (agent ()).operation) |]
                details [||] [|
                  summary [| cls "cursor-pointer opacity-60" |] [| text "Details" |]
                  agentDetails agent
                |]
              |])
            each (fun (here: PresenceView) -> here.id) (fun () -> stores.Presence.Value |> Array.sortByDescending (fun here -> here.since, here.id)) (fun here ->
              div [| cls "presence border-t border-base-content/10 pt-2 text-xs break-words" |] [|
                div [| cls "font-semibold" |] [| rx (fun () -> (here ()).name + " · " + (here ()).detail) |]
                div [| cls "opacity-60" |] [|
                  // A lease's time is its grant or ask; a membership's is its latest renewal,
                  // which is what the daemon retains, not when the member joined.
                  rx (fun () ->
                    let h = here ()
                    h.source + (if h.source = "member" then " renewed " else " since ") + clockTime h.since
                    + (if h.project = "" then "" else " · " + h.project))
                |]
              |])
          |])
        (fun () ->
          para [| cls "text-xs opacity-50" |] [|
            // Absence is claimed only from a received board; before one arrives the
            // daemon has said nothing about agents.
            rx (fun () ->
              if stores.AgentRefusal.Value <> "" then "Agent work is unavailable; the daemon could not publish the complete active board."
              elif stores.AgentIncarnation.Value = "" then "No agent board received from the daemon yet."
              else "No agents present: no lease held or queued, no cohort member, no running report.")
          |])
      show (fun () -> agents () |> Array.exists _.aged) (fun () ->
        details [| flag "open" false; cls "border-t border-base-content/10 pt-2" |] [|
          summary [| cls "cursor-pointer text-xs opacity-60" |] [|
            rx (fun () -> "Recent completed (" + string (agents () |> Array.filter _.aged |> Array.length) + ")")
          |]
          each (fun (agent: AgentView) -> agent.id) (fun () -> agents () |> Array.filter _.aged |> newestFirst) (fun agent ->
            details [| cls "border-t border-base-content/10 mt-2 pt-2 text-xs break-words" |] [|
              summary [| cls "cursor-pointer font-semibold" |] [|
                rx (fun () -> (agent ()).name + " · " + (agent ()).role + " · reported " + (agent ()).status + " · " + clockTime (agent ()).updatedAt)
              |]
              agentDetails agent
            |])
        |])
    |]
  |]

type private RunPick =
  {| key: string; label: string; project: string; exitCode: int; generation: string; sourceVersion: string; at: float
     stdout: string; stderr: string; source: string |}

let private outputPane (stores: Stores) =
  comp (fun () ->
    let tab = Api.createSignal "runs"
    // Runs come from two places: the daemon's journal (every caller's, pushed as
    // facts) and this page's own commands. A pick names its source.
    let selected = Api.createSignal ""
    // The pane can be folded to its tab bar; the choice is this page's own.
    let collapsed = Api.createSignal false
    let picks () : RunPick array =
      Array.append
        (stores.Operations.Value
         |> Array.filter (fun o -> o.hasRun)
         |> Array.map (fun o ->
           {| key = "j:" + o.id; label = "#" + o.id + " " + o.caller + " " + o.project + " → " + string o.exitCode
              project = o.project; exitCode = o.exitCode; generation = o.generation; sourceVersion = o.sourceVersion; at = o.at
              stdout = (if o.dropped then "[transcript dropped for space]" else o.stdout); stderr = o.stderr; source = o.caller |}))
        (stores.Runs.Value
         |> Array.map (fun r ->
           {| key = "p:" + string r.id; label = "#" + string r.id + " " + r.project + " → " + string r.exitCode + " (this page)"
              project = r.project; exitCode = r.exitCode; generation = r.generation; sourceVersion = r.sourceVersion; at = r.at
              stdout = r.stdout; stderr = r.stderr; source = "this page" |}))
    let run () =
      let all = picks ()
      match all |> Array.tryFind (fun r -> r.key = selected.Value) with
      | Some r -> Some r
      | None -> Array.tryHead all
    let field (pick: RunPick -> string) = match run () with Some r -> pick r | None -> ""
    let stdoutText () = field _.stdout
    let stderrText () = field _.stderr
    let exitCodeOf () = match run () with Some r -> r.exitCode | None -> 0
    let activityCount () = stores.Operations.Value.Length + stores.Activity.Value.Length
    div [| clsRx (fun () -> if collapsed.Value then "flex flex-col border-t border-base-content/10 bg-base-100" else "flex flex-col h-[34vh] min-h-40 border-t border-base-content/10 bg-base-100") |] [|
      div [| cls "flex items-center gap-2 px-4 py-1 border-b border-base-content/10" |] [|
        button [|
          cls "btn btn-xs btn-ghost output-fold"
          titleRx (fun () -> if collapsed.Value then "Show the output pane" else "Fold the output pane to its tabs")
          on "click" (fun _ -> collapsed.Value <- not collapsed.Value)
        |] [| rx (fun () -> if collapsed.Value then "▲" else "▼") |]
        div [| cls "tabs tabs-bordered tabs-sm" |] [|
          button [| clsRx (fun () -> if tab.Value = "runs" then "tab tab-active !border-accent text-accent font-semibold" else "tab"); on "click" (fun _ -> tab.Value <- "runs") |] [|
            rx (fun () -> "Run output (" + string (picks ()).Length + ")")
          |]
          button [| clsRx (fun () -> if tab.Value = "activity" then "tab tab-active !border-accent text-accent font-semibold" else "tab"); on "click" (fun _ -> tab.Value <- "activity") |] [|
            rx (fun () -> "Activity (" + string (activityCount ()) + ")")
          |]
        |]
        div [| clsRx (fun () -> if tab.Value = "runs" then "flex-1 flex gap-1 overflow-x-auto" else "hidden") |] [|
          each (fun (r: RunPick) -> r.key) picks (fun pick ->
            button [|
              clsRx (fun () -> if (run () |> Option.map (fun x -> x.key)) = Some (pick ()).key then "btn btn-xs btn-active font-mono" else "btn btn-xs btn-ghost font-mono")
              on "click" (fun _ -> selected.Value <- (pick ()).key)
            |] [| rx (fun () -> (pick ()).label) |])
        |]
      |]
      div [| clsRx (fun () -> if tab.Value = "runs" && not collapsed.Value then "flex-1 min-h-0 overflow-auto px-4 py-3" else "hidden") |] [|
        showElse (fun () -> (run ()).IsSome)
          (fun () ->
            fragment [|
              div [| cls "flex items-center gap-2 text-xs mb-2" |] [|
                span [| clsRx (fun () -> exitBadge (exitCodeOf ())) |] [| rx (fun () -> "exit " + string (exitCodeOf ())) |]
                span [| cls "font-semibold" |] [| rx (fun () -> field _.project) |]
                span [| cls "font-mono opacity-60" |] [| rx (fun () -> field (fun r -> "gen " + r.generation + " · src " + shortHash r.sourceVersion)) |]
                span [| cls "font-mono opacity-60"; View.title "Who asked, as the adapter reported it; not an authenticated identity" |] [| rx (fun () -> field _.source) |]
                span [| cls "opacity-50" |] [| rx (fun () -> field (fun r -> clockTime r.at)) |]
              |]
              pre [| cls "panel diagnostic-trace" |] [| traceText stdoutText |]
              div [| clsRx (fun () -> if stderrText () = "" then "hidden" else "mt-2") |] [|
                diagnosticBlock false (fun () -> "Standard error") stderrText
              |]
            |])
          (fun () -> div [| cls "text-xs opacity-50" |] [| text "No runs yet: runs from every caller appear here as the daemon journals them." |])
      |]
      div [| clsRx (fun () -> if tab.Value = "activity" && not collapsed.Value then "flex-1 min-h-0 overflow-auto px-4 py-1" else "hidden") |] [|
        showElse (fun () -> activityCount () > 0)
          (fun () ->
            fragment [|
              each (fun (o: OperationView) -> o.id) (fun () -> stores.Operations.Value) (fun operation ->
                div [| cls "flex gap-2 text-xs py-2 border-b border-base-content/5 operation" |] [|
                  span [| cls "font-mono opacity-50 shrink-0" |] [| rx (fun () -> clockTime (operation ()).at) |]
                  span [| clsRx (fun () -> if (operation ()).ok then "text-success shrink-0" else "text-error shrink-0") |] [| rx (fun () -> if (operation ()).ok then "✓" else "✗") |]
                  span [| cls "font-mono opacity-70 shrink-0"; View.title "Who asked, as the adapter reported it; not an authenticated identity" |] [| rx (fun () -> (operation ()).caller) |]
                  span [| cls "font-semibold shrink-0" |] [| rx (fun () -> (operation ()).operation) |]
                  span [| cls "font-mono opacity-70 shrink-0" |] [| rx (fun () -> (operation ()).project) |]
                  show (fun () -> not (operation ()).ok) (fun () ->
                    span [| cls "opacity-70" |] [| rx (fun () -> (operation ()).code + ": " + (operation ()).message) |])
                |])
              show (fun () -> stores.Activity.Value.Length > 0) (fun () ->
                fragment [|
                  div [| cls "text-xs opacity-50 pt-2" |] [| text "this page" |]
                  each (fun (a: OutcomeView) -> a.id) (fun () -> stores.Activity.Value) (fun entry ->
                    div [| cls "flex gap-2 text-xs py-2 border-b border-base-content/5" |] [|
                      span [| cls "font-mono opacity-50 shrink-0" |] [| rx (fun () -> clockTime (entry ()).at) |]
                      span [| clsRx (fun () -> if (entry ()).ok then "text-success shrink-0" else "text-error shrink-0") |] [| rx (fun () -> if (entry ()).ok then "✓" else "✗") |]
                      span [| cls "font-mono opacity-70 shrink-0" |] [| rx (fun () -> (entry ()).scope) |]
                      pre [| cls "activity-trace" |] [| traceText (fun () -> (entry ()).text) |]
                    |])
                |])
            |])
          (fun () -> div [| cls "text-xs opacity-50 py-2" |] [| text "No activity yet: every caller's Composer operations appear here as the daemon journals them." |])
      |]
    |])

let private runningOperation (node: NodeWorkView) (operation: NodeOperationView) =
  operation.outcome = "running"
  || operation.outcome.StartsWith("unknown:", System.StringComparison.Ordinal)
  || (operation.hasLease && (node.leases |> Array.exists (fun lease -> lease.id = operation.leaseId)))

let private activeOperations (node: NodeWorkView) =
  node.operations |> Array.filter (runningOperation node)

let private operationStartedAt (operation: NodeOperationView) =
  if operation.startedAt > 0. then operation.startedAt else operation.at

let private openNodeSessions (node: NodeWorkView) =
  node.sessions |> Array.filter (fun session -> not session.closed)

let private operationKey (node: NodeWorkView) (operation: NodeOperationView) =
  node.id + ":" + node.journal + ":" + operation.sequence

let private operationStateLabel (operation: NodeOperationView) =
  if operation.code = "running" then "running"
  elif operation.code = "abandoned" then "abandoned"
  elif operation.code = "unknown" then "unknown"
  elif operation.ok then "completed"
  elif operation.code = "command_failed" || operation.code = "exception" then "failed"
  else "refused"

let private operationStateBadge (operation: NodeOperationView) =
  if operation.code = "running" then "badge badge-xs badge-info font-mono mr-1.5 node-outcome-running"
  elif operation.code = "abandoned" then "badge badge-xs badge-warning font-mono mr-1.5 node-outcome-abandoned"
  elif operation.code = "unknown" then "badge badge-xs badge-warning badge-outline font-mono mr-1.5 node-outcome-unknown"
  elif operation.ok then "badge badge-xs badge-success font-mono mr-1.5 node-outcome-completed"
  elif operation.code = "command_failed" || operation.code = "exception" then "badge badge-xs badge-error font-mono mr-1.5 node-outcome-failed"
  else "badge badge-xs badge-error badge-outline font-mono mr-1.5 node-outcome-refused"

let private formatLivenessMessage (message: string) =
  let prefix = "owner liveness verified "
  if message.StartsWith(prefix, System.StringComparison.Ordinal) then
    let parsed = parseIsoMs (message.Substring(prefix.Length).Trim())
    if not (System.Double.IsNaN parsed) && parsed > 0. then
      "Owner liveness verified " + clockTime parsed
    else message
  else message

let private elapsedText (startMs: float) (endMs: float) =
  if startMs > 0. && endMs >= startMs then " · elapsed " + duration (endMs - startMs)
  else ""

let private hasRamTelemetry (capacity: NodeCapacityView) =
  capacity.machineTotal > 0. || capacity.resident > 0. || capacity.peakResident > 0. || capacity.memoryPressure <> ""

let private fleetCapacityGrid (stores: Stores) (connected: unit -> bool) =
  comp (fun () ->
    let nodeRank (node: NodeWorkView) =
      if node.local then 0
      else
        match node.id.ToLowerInvariant() with
        | "one" -> 1
        | "two" -> 2
        | "three" -> 3
        | _ -> 4
    let nodes = Api.createMemo (fun _ -> stores.Nodes.Value |> Array.sortBy (fun node -> nodeRank node, node.id))
    let queueText (node: NodeWorkView) =
      if Array.isEmpty node.queue then "0 queued"
      else
        let earliest = (node.queue |> Array.minBy _.requestedAt).requestedAt
        let uptime = stores.Uptime.Value
        let now = if uptime < 0. then node.sampledAt else stores.StartedAt.Value + uptime
        let age = if uptime < 0. && earliest > node.sampledAt then "awaiting clock" else duration (max 0. (now - earliest))
        string node.queue.Length + " queued · oldest " + age
    let operationText (node: NodeWorkView) (active: NodeOperationView array) =
      match Array.tryHead active with
      | Some op ->
        let uptime = stores.Uptime.Value
        let elapsed =
          if uptime < 0. then " · awaiting clock"
          else elapsedText (operationStartedAt op) (stores.StartedAt.Value + uptime)
        let more = if active.Length > 1 then " · +" + string (active.Length - 1) + " more" else ""
        fileName op.project + " · " + op.operation + " · PID " + string op.pid + elapsed + more
      | None ->
        match Array.tryHead node.leases with
        | Some lease -> lease.kind + " · " + lease.holder + " · process details unreported"
        | None -> if node.status = "available" then "Idle" else "Retained observation"
    let tile (node: unit -> NodeWorkView) =
      comp (fun () ->
        let active = Api.createMemo (fun _ -> activeOperations (node ()))
        let capacity () = (node ()).capacity
        let availability () = if connected () then (node ()).status else "controller disconnected"
        div [| cls "fleet-node-card card card-compact panel min-w-0"; attrRx "data-node" (fun () -> (node ()).id) |] [|
          div [| cls "card-body min-w-0 gap-3 break-words" |] [|
            div [| cls "flex flex-col items-start gap-1" |] [|
              h3 [| cls "max-w-full font-bold" |] [| rx (fun () -> (node ()).id.ToUpperInvariant()) |]
              div [| cls "flex flex-wrap gap-1" |] [|
                span [| cls "badge badge-sm badge-ghost" |] [| rx (fun () -> if (node ()).local then "controller" else "worker") |]
                span [| clsRx (fun () -> if availability () = "available" then "badge badge-sm badge-success" else "badge badge-sm badge-warning") |] [|
                  rx availability
                |]
              |]
            |]
            showElse (fun () -> (node ()).hasValue)
              (fun () ->
                fragment [|
                  div [| cls "fleet-leases" |] [|
                    h4 [| cls "text-xs font-semibold opacity-70" |] [| text "Live leases / cap" |]
                    para [| cls "font-semibold" |] [|
                      rx (fun () ->
                        let n = node ()
                        let cap = if n.hasCapacity && n.capacity.hasLeaseCap then string n.capacity.leaseCap else "?"
                        string n.leases.Length + " / " + cap)
                    |]
                    show (fun () -> (node ()).hasCapacity) (fun () ->
                      div [| cls "flex flex-wrap items-center gap-1 text-xs" |] [|
                        span [||] [| rx (fun () -> string (capacity ()).pidVerifiedLeases + " PID-verified") |]
                        show (fun () -> (capacity ()).unverifiedLeases > 0) (fun () ->
                          span [| cls "badge badge-sm badge-warning" |] [| rx (fun () -> string (capacity ()).unverifiedLeases + " unverified") |])
                        show (fun () -> (capacity ()).stalePidLeases > 0) (fun () ->
                          span [| cls "badge badge-sm badge-error" |] [| rx (fun () -> string (capacity ()).stalePidLeases + " stale PID") |])
                      |])
                  |]
                  div [| cls "fleet-queue" |] [|
                    h4 [| cls "text-xs font-semibold opacity-70" |] [| text "Queue waiters" |]
                    para [||] [| rx (fun () -> queueText (node ())) |]
                  |]
                  div [| cls "fleet-ram" |] [|
                    h4 [| cls "text-xs font-semibold opacity-70" |] [| text "RAM / Peak RSS" |]
                    showElse (fun () -> (node ()).hasCapacity && hasRamTelemetry (capacity ()))
                      (fun () ->
                        fragment [|
                          show (fun () -> (capacity ()).machineTotal > 0.) (fun () ->
                            para [||] [| rx (fun () -> bytes (capacity ()).machineAvailable + " avail / " + bytes (capacity ()).machineTotal) |])
                          show (fun () -> (capacity ()).resident > 0. || (capacity ()).peakResident > 0.) (fun () ->
                            para [| cls "text-xs" |] [| rx (fun () -> "RSS " + bytes (capacity ()).resident + " · peak " + bytes (capacity ()).peakResident) |])
                          show (fun () -> (capacity ()).memoryPressure <> "") (fun () ->
                            span [| clsRx (fun () -> pressureBadge (capacity ()).memoryPressure) |] [| rx (fun () -> "memory " + (capacity ()).memoryPressure) |])
                        |])
                      (fun () -> para [| cls "text-xs opacity-70" |] [| text "RAM telemetry unavailable" |])
                  |]
                  div [| cls "fleet-active-op" |] [|
                    h4 [| cls "text-xs font-semibold opacity-70" |] [| text "Active operation" |]
                    para [||] [| rx (fun () -> operationText (node ()) active.Value) |]
                  |]
                  para [| cls "fleet-compiler-sep mt-auto border-t border-base-content/10 pt-2 text-xs opacity-70" |] [|
                    rx (fun () -> "Composer sessions: " + string (openNodeSessions (node ())).Length + " open · worker " + (node ()).worker.state)
                  |]
                |])
              (fun () ->
                div [| cls "text-sm opacity-70" |] [|
                  para [||] [| text "Capacity unavailable — no idle state inferred." |]
                  show (fun () -> (node ()).note <> "") (fun () ->
                    para [| cls "text-xs" |] [| rx (fun () -> (node ()).note) |])
                |])
          |]
        |])
    el "section" [| cls "fleet-capacity-grid flex flex-col gap-3"; aria "labelledby" "fleet-capacity-heading" |] [|
      div [||] [|
        h2 [| cls "text-lg font-bold"; attr "id" "fleet-capacity-heading" |] [| text "Fleet capacity" |]
        para [| cls "text-xs opacity-70" |] [|
          text "Reconciled node leases, queue waiters, RAM and peak RSS, and active operations. Composer sessions and daemon-aged completed history are shown separately."
        |]
      |]
      div [| cls "grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-3" |] [|
        each (fun (node: NodeWorkView) -> node.id) (fun () -> nodes.Value) tile
      |]
    |])

let private stateBadge (operation: unit -> NodeOperationView) =
  span [| clsRx (fun () -> operationStateBadge (operation ())) |] [| rx (fun () -> operationStateLabel (operation ())) |]

let private workNodesPanel (stores: Stores) (connected: unit -> bool) (read: NodeWorkView -> NodeOperationView -> TranscriptStream -> unit) =
  comp (fun () ->
    let retired = Api.createSignal (Interop.getRetiredWork ())
    let olderOpenNodes = Api.createSignal Array.empty<string>
    let olderWindowHours = Api.createSignal (Interop.getOlderWindowHours ())
    let uptime () = stores.Uptime.Value
    let olderWindowMs () = float olderWindowHours.Value * 3600000.
    let olderWindowLabel () =
      match olderWindowHours.Value with
      | 168 -> "7 days"
      | hours -> string hours + " hours"
    let setNodeOlderOpen (nodeId: string) (isOpen: bool) =
      let current = olderOpenNodes.Value
      olderOpenNodes.Value <-
        if isOpen then Array.append [| nodeId |] current |> Array.distinct
        else current |> Array.filter (fun id -> id <> nodeId)
    let nowOf (node: NodeWorkView) =
      if uptime () < 0. then node.sampledAt else stores.StartedAt.Value + uptime ()
    let settledWhere (inWindow: float -> bool) (node: NodeWorkView) =
      let now = nowOf node
      node.operations
      |> Array.filter (fun operation ->
        not (runningOperation node operation) && inWindow (now - operation.at)
        && not (retired.Value |> Array.contains (operationKey node operation)))
      |> Array.sortByDescending (fun operation -> operation.at, int64 operation.sequence)
    let recent (node: NodeWorkView) = settledWhere (fun age -> age <= olderWindowMs ()) node |> Array.truncate 20
    let older (node: NodeWorkView) = settledWhere (fun age -> age > olderWindowMs ()) node
    let retireFor (node: NodeWorkView) keys =
      let next = Array.append keys retired.Value |> Array.distinct |> Array.truncate 256
      Interop.saveRetiredWork next
      retired.Value <- next
      if (older node).Length = 0 then setNodeOlderOpen node.id false
    let operationRowKey (operation: NodeOperationView) = operation.id
    let historyRow (node: unit -> NodeWorkView) (operation: unit -> NodeOperationView) =
      let startMs () = operationStartedAt (operation ())
      let elapsed () = if (operation ()).hasLease then elapsedText (startMs ()) (operation ()).at else ""
      details [| cls "node-operation py-2 border-t border-base-300" |] [|
        summary [| cls "cursor-pointer text-xs font-mono break-words" |] [|
          stateBadge operation
          rx (fun () ->
            let o = operation ()
            o.project + " · " + o.operation + " · " + o.outcome
            + (if o.hasRun then " · exit " + string o.exitCode else "")
            + (if o.caller <> "" then " · " + o.caller else "") + " · " + clockTime o.at + elapsed ())
        |]
        div [| cls "flex flex-col gap-2 pt-2" |] [|
          show (fun () -> (operation ()).caller <> "") (fun () ->
            para [| cls "text-xs font-mono opacity-70 break-words" |] [| rx (fun () -> "Caller " + (operation ()).caller + " · node " + (node ()).id) |])
          show (fun () -> (operation ()).hasLease) (fun () ->
            fragment [|
              para [| cls "text-xs font-mono break-words" |] [| rx (fun () -> "PID " + string (operation ()).pid + " · started " + clockTime (startMs ()) + elapsed ()) |]
              pre [| cls "text-xs font-mono whitespace-pre-wrap break-words max-h-48 overflow-auto"; View.title "Executable, followed by one argument per line" |] [|
                rx (fun () -> (operation ()).executable + "\n" + String.concat "\n" (operation ()).arguments)
              |]
              para [| cls "text-xs font-mono opacity-60 break-words" |] [| rx (fun () -> "Lease " + (operation ()).leaseId + " · run " + (operation ()).runId) |]
            |])
          show (fun () -> (operation ()).dropped) (fun () -> para [| cls "text-xs text-warning" |] [| text "Source transcript was not retained." |])
          show (fun () -> (operation ()).hasRun) (fun () ->
            div [| cls "flex gap-2" |] [|
              button [| cls "btn btn-xs btn-outline"; flagRx "disabled" (fun () -> not (connected ())); on "click" (fun _ -> read (node ()) (operation ()) Stdout) |] [| text "Load stdout" |]
              button [| cls "btn btn-xs btn-outline"; flagRx "disabled" (fun () -> not (connected ())); on "click" (fun _ -> read (node ()) (operation ()) Stderr) |] [| text "Load stderr" |]
            |])
          button [| cls "btn btn-xs btn-ghost self-start"; on "click" (fun _ -> retireFor (node ()) [| operationKey (node ()) (operation ()) |]) |] [| text "Retire entry" |]
        |]
      |]
    let activeRow (node: unit -> NodeWorkView) (operation: unit -> NodeOperationView) =
      let matchedLease () = (node ()).leases |> Array.tryFind (fun lease -> lease.id = (operation ()).leaseId)
      let startMs () = operationStartedAt (operation ())
      let livenessText () =
        let o = operation ()
        if o.outcome = "running" then
          if o.message <> "" then formatLivenessMessage o.message
          else "Owner liveness not yet verified"
        else ""
      let leaseHeartbeatText () =
        match matchedLease () with
        | Some lease -> " · heartbeat until " + clockTime lease.expiresAt
        | None -> " · no live lease heartbeat"
      div [| cls "node-operation node-active py-2 border-t border-base-300" |] [|
        para [| cls "text-sm font-semibold break-words" |] [|
          stateBadge operation
          rx (fun () -> (operation ()).project)
        |]
        para [| cls "text-xs font-mono break-words" |] [| rx (fun () -> (operation ()).operation + " · " + (operation ()).outcome + " · PID " + string (operation ()).pid) |]
        para [| cls "node-operation-meta text-xs font-mono opacity-70 break-words" |] [|
          rx (fun () ->
            let o = operation ()
            (if o.caller <> "" then "Caller " + o.caller + " · " else "")
            + "started " + clockTime (startMs ())
            + (if uptime () >= 0. then elapsedText (startMs ()) (stores.StartedAt.Value + uptime ()) else ""))
        |]
        show (fun () -> livenessText () <> "") (fun () ->
          para [| cls "node-operation-liveness text-xs opacity-70 font-mono break-words" |] [| rx livenessText |])
        para [| cls "text-xs font-mono break-words" |] [|
          rx (fun () -> (operation ()).executable + ((operation ()).arguments |> Array.tryHead |> Option.map (fun argument -> " · " + argument) |> Option.defaultValue ""))
        |]
        details [||] [|
          summary [| cls "cursor-pointer text-xs opacity-60" |] [| text "Command arguments" |]
          pre [| cls "text-xs font-mono whitespace-pre-wrap break-words max-h-48 overflow-auto" |] [| rx (fun () -> String.concat "\n" (operation ()).arguments) |]
        |]
        para [| cls "node-operation-lease text-xs opacity-60 break-words" |] [| rx (fun () -> "Lease " + (operation ()).leaseId + " · run " + (operation ()).runId + leaseHeartbeatText ()) |]
      |]
    let nodeCard (node: unit -> NodeWorkView) =
      let openSessions () = openNodeSessions (node ())
      let olderOpen () = olderOpenNodes.Value |> Array.contains (node ()).id
      div [| cls "card card-compact panel" |] [|
        div [| cls "card-body gap-2" |] [|
          div [| cls "flex flex-wrap gap-2 items-center" |] [|
            h3 [| cls "font-bold flex-1" |] [| rx (fun () -> (node ()).id) |]
            span [| cls "badge badge-outline" |] [| rx (fun () -> if (node ()).local then "controller" else "remote · read-only") |]
            span [| cls "badge badge-outline" |] [| rx (fun () -> if connected () then (node ()).status else "controller disconnected") |]
          |]
          para [| cls "text-xs opacity-70" |] [| rx (fun () -> (node ()).note) |]
          showElse (fun () -> (node ()).hasValue)
            (fun () ->
              fragment [|
                para [| cls "work-sample-age text-xs opacity-60 font-mono" |] [|
                  rx (fun () ->
                    let n = node ()
                    "Work sample " + clockTime n.sampledAt + " · "
                    + (if uptime () < 0. then "age unavailable (awaiting clock)"
                       else duration (max 0. (stores.StartedAt.Value + uptime () - n.sampledAt)) + " ago"))
                |]
                para [| cls "text-sm" |] [|
                  rx (fun () -> string (openSessions ()).Length + " compiler sessions · " + string (node ()).leases.Length + " active leases · " + string (node ()).queue.Length + " queued")
                |]
                show (fun () -> (node ()).status <> "available" || not (connected ())) (fun () ->
                  para [| cls "text-xs text-warning" |] [| text "Retained observation; current work is unavailable." |])
                para [| cls "text-xs font-mono node-worker" |] [|
                  rx (fun () ->
                    let w = (node ()).worker
                    "Compiler worker · " + w.state + (if w.pid = "" then "" else " · PID " + w.pid))
                |]
                div [| cls "node-projects flex flex-col gap-1" |] [|
                  h4 [| cls "text-sm font-semibold" |] [| text "Compiler projects" |]
                  showElse (fun () -> (openSessions ()).Length > 0)
                    (fun () ->
                      each (fun (session: SessionView) -> session.id) openSessions (fun session ->
                        para [| cls "text-xs font-mono break-words node-session" |] [|
                          rx (fun () -> (session ()).project + " · " + (if (session ()).busy then "busy" else "open"))
                        |]))
                    (fun () -> para [| cls "text-xs opacity-60" |] [| text "No open compiler projects in this observation." |])
                |]
                each (fun (lease: LeaseView) -> lease.id) (fun () -> (node ()).leases) (fun lease ->
                  div [| cls "text-xs font-mono break-words node-lease" |] [|
                    para [| cls "font-semibold" |] [| rx (fun () -> (lease ()).kind + " · active") |]
                    para [||] [| rx (fun () -> (lease ()).holder) |]
                    para [| cls "opacity-60" |] [| rx (fun () -> "Lease " + (lease ()).id + " · granted " + clockTime (lease ()).grantedAt) |]
                    show (fun () -> not ((node ()).operations |> Array.exists (fun operation -> operation.hasLease && operation.leaseId = (lease ()).id))) (fun () ->
                      para [| cls "opacity-60" |] [| text "Command process details have not been reported for this lease." |])
                  |])
                each (fun (ask: WaitView) -> ask.id) (fun () -> (node ()).queue) (fun ask ->
                  para [| cls "text-xs font-mono break-words opacity-70" |] [| rx (fun () -> (ask ()).holder + " · " + (ask ()).kind + " · queued since " + clockTime (ask ()).requestedAt) |])
                show (fun () -> connected () && (node ()).status = "available" && (node ()).leases.Length = 0 && (node ()).queue.Length = 0) (fun () ->
                  para [| cls "text-xs opacity-70" |] [| text "No active or queued leases in this observation." |])
                each operationRowKey (fun () -> activeOperations (node ())) (fun operation -> activeRow node operation)
                show (fun () -> (recent (node ())).Length > 0) (fun () ->
                  div [| cls "node-recent-projects flex flex-col gap-1" |] [|
                    h4 [| cls "text-sm font-semibold" |] [| text "Recent project activity" |]
                    each operationRowKey (fun () -> recent (node ()) |> Array.distinctBy _.project |> Array.truncate 3) (fun operation ->
                      para [| cls "text-xs font-mono break-words" |] [|
                        stateBadge operation
                        rx (fun () -> (operation ()).project + " · " + (operation ()).outcome + " · " + clockTime (operation ()).at)
                      |])
                  |])
                details [| cls "node-operations" |] [|
                  summary [| cls "cursor-pointer text-sm font-semibold" |] [| rx (fun () -> "Recent operations (" + string (recent (node ())).Length + ")") |]
                  div [| cls "flex flex-col gap-2 pt-2" |] [|
                    show (fun () -> (node ()).storageError <> "") (fun () -> para [| cls "text-xs text-warning" |] [| rx (fun () -> "Transcript store unavailable: " + (node ()).storageError) |])
                    show (fun () -> (recent (node ())).Length = 0) (fun () -> para [| cls "text-xs opacity-70" |] [| text "No recent completed operations in this view." |])
                    show (fun () -> (node ()).transcriptsOmitted) (fun () -> para [| cls "text-xs opacity-70" |] [| text "Transcript bytes are loaded from the node on request." |])
                    show (fun () -> (recent (node ())).Length > 0) (fun () ->
                      button [| cls "btn btn-xs btn-ghost self-start"; on "click" (fun _ -> retireFor (node ()) (recent (node ()) |> Array.map (operationKey (node ())))) |] [| text "Retire shown entries" |])
                    each operationRowKey (fun () -> recent (node ())) (fun operation -> historyRow node operation)
                  |]
                |]
                show (fun () -> (older (node ())).Length > 0) (fun () ->
                  details [| cls "node-older-operations"; flagRx "open" olderOpen |] [|
                    summary [|
                      cls "cursor-pointer text-xs opacity-70"
                      on "click" (fun event ->
                        Interop.preventDefault event
                        setNodeOlderOpen (node ()).id (not (olderOpen ())))
                    |] [| rx (fun () -> "Show older (" + string (older (node ())).Length + ")") |]
                    show olderOpen (fun () ->
                      div [| cls "flex flex-col gap-2 pt-2" |] [|
                        each operationRowKey (fun () -> older (node ())) (fun operation -> historyRow node operation)
                      |])
                  |])
              |])
            (fun () -> para [| cls "text-sm opacity-70" |] [| text "Work state unavailable; no idle state inferred." |])
        |]
      |]
    details [| flag "open" true; cls "work-nodes flex flex-col gap-3" |] [|
      summary [| cls "cursor-pointer text-lg font-bold" |] [| text "Work across nodes" |]
      div [| cls "flex flex-wrap items-center gap-2 text-xs opacity-70" |] [|
        span [||] [| rx (fun () -> "Finished and abandoned entries older than " + olderWindowLabel () + " collapse behind Show older; at most 20 recent entries per node are shown.") |]
        label [| cls "flex items-center gap-1" |] [|
          span [||] [| text "Window" |]
          select [|
            cls "select select-bordered select-xs"
            aria "label" "History window"
            selectValue (fun () -> string olderWindowHours.Value)
            on "change" (fun event ->
              match System.Int32.TryParse(Interop.inputValue event) with
              | true, hours when hours = 6 || hours = 24 || hours = 72 || hours = 168 ->
                Interop.saveOlderWindowHours hours
                olderWindowHours.Value <- hours
                olderOpenNodes.Value <- [||]
              | _ -> ())
          |] [|
            option [| attr "value" "6" |] [| text "6h" |]
            option [| attr "value" "24" |] [| text "24h" |]
            option [| attr "value" "72" |] [| text "72h" |]
            option [| attr "value" "168" |] [| text "7d" |]
          |]
        |]
        show (fun () -> retired.Value.Length > 0) (fun () ->
          button [|
            cls "btn btn-xs btn-ghost"
            on "click" (fun _ ->
              Interop.saveRetiredWork [||]
              retired.Value <- [||]
              olderOpenNodes.Value <- [||])
          |] [| text "Restore retired entries" |])
        information "Work history" (fun () -> [|
          "Running work, queued leases and open compiler projects never retire from this view."
          "Retiring or hiding an entry affects this browser only. The node's SQLite journal retains its stored evidence."
          "Compiler sessions are opened Clef projects. Leased command projects are displayed separately; a checkout alone is not an open compiler session."
        |])
      |]
      show (fun () -> stores.Nodes.Value.Length = 0) (fun () ->
        para [| cls "text-sm opacity-70" |] [| text "No node work observation has arrived. Availability is unknown." |])
      div [| cls "grid grid-cols-1 lg:grid-cols-2 gap-3" |] [|
        each (fun (node: NodeWorkView) -> node.id) (fun () -> stores.Nodes.Value) nodeCard
      |]
    |])

let private transcriptPanel (stores: Stores) (connected: unit -> bool) (readNext: unit -> unit) =
  let transcript () = stores.Transcript.Value
  div [| cls "xl:col-span-3 card card-compact panel transcript-readback" |] [|
    div [| cls "card-body gap-2" |] [|
      h2 [| cls "text-lg font-bold" |] [| rx (fun () -> "Transcript · " + (transcript ()).node + " · " + (if (transcript ()).stream = Stdout then "stdout" else "stderr")) |]
      para [| cls "text-xs font-mono break-words" |] [| rx (fun () -> (transcript ()).journal + " · entry " + (transcript ()).sequence) |]
      para [| cls "text-sm" |] [| rx (fun () -> (transcript ()).status) |]
      para [| cls "text-xs opacity-70" |] [|
        rx (fun () -> let t = transcript () in string t.bytes.Length + " stored bytes loaded" + (if t.total = "" then "" else " of " + t.total))
      |]
      show (fun () -> (transcript ()).truncated) (fun () ->
        para [| cls "text-xs text-warning" |] [| text "Truncated at the source; this is the retained transcript, not complete process output." |])
      show (fun () -> (transcript ()).text <> "") (fun () ->
        pre [| cls "text-xs font-mono whitespace-pre-wrap break-words max-h-80 overflow-auto" |] [| rx (fun () -> (transcript ()).text) |])
      show (fun () -> (transcript ()).next <> "") (fun () ->
        button [|
          cls "btn btn-sm btn-outline self-start"
          flagRx "disabled" (fun () -> not (connected ()) || (transcript ()).status = "reading transcript")
          on "click" (fun _ -> readNext ())
        |] [| text "Next transcript page" |])
    |]
  |]

// The page: call inside the page graph's Run.
let app () : V =
  let workPage = isWorkPage ()
  setTitle (if workPage then "Work · Bozzetto" else "Dashboard · Bozzetto")

  // ── Model ────────────────────────────────────────────────────────────────
  // Signals hold what the backend reported; the backend is the update.
  let stores = Model.create ()
  let commands = Model.dispatcher ()

  // Pure UI state: link status and theme. No clock: times are shown as
  // instants, the uptime is the daemon's push, and the page runs no timer but
  // the bridge's reconnect backoff.
  let link = Api.createSignal Bridge.Connecting
  let connected () =
    match link.Value with
    | Bridge.Connected -> stores.Welcomed.Value && not stores.ProtocolMismatch.Value
    | Bridge.Connecting | Bridge.Retrying _ -> false

  let initialDark = Interop.getSavedTheme () <> "light"
  let dark = Api.createSignal initialDark
  Interop.setTheme (if initialDark then "dark" else "light")
  let toggleTheme () =
    let next = not dark.Value
    dark.Value <- next
    let name = if next then "dark" else "light"
    Interop.setTheme name
    Interop.saveTheme name

  // ── Update ───────────────────────────────────────────────────────────────
  // dispatch sends a Command across the bridge; the backend's Events come
  // back through `receive` and fold into the signals. Each hop is echoed to
  // the browser console with its wire payload.
  let dispatch (command: Command) =
    let frame = Model.begin' stores commands command
    if not (connected ()) then
      Model.abandon stores commands frame
    else
      let wire = Codec.encodeCommand frame
      if Bridge.send wire then Interop.consoleLog (sprintf "→ binary frame (%d bytes)" wire.Length)
      else Model.abandon stores commands frame

  let receive (payload: byte array) =
    match Codec.decodeEvent payload with
    | Ok event ->
      match event with
      | Health _ | Retrieval _ | Leases _ | Uptime _ | Resources _ | AgentWork _ | WorkNodes _ | NodeResources _ | NodeTranscript _ -> ()
      | _ -> Interop.consoleLog (sprintf "← binary frame (%d bytes)" payload.Length)
      act (fun () -> Model.apply stores commands event)
    | Error message ->
      Interop.consoleLog ("← undecodable binary frame: " + message)
      act (fun () -> stores.ProtocolMismatch.Value <- true)

  let focus (node: NodeWorkView) =
    let current = stores.NodeFocus.Value
    if current <> "" && current <> node.id then
      dispatch (ObserveNodeResources { Node = current; Seconds = 0 })
    Model.focusNode stores node.id node.incarnation
    dispatch (ObserveNodeResources { Node = node.id; Seconds = 120 })
  let selectedResourceNode () =
    let nodes = stores.Nodes.Value
    nodes |> Array.tryFind (fun node -> node.id = stores.NodeFocus.Value)
    |> Option.orElseWith (fun () -> nodes |> Array.tryFind _.local)
  let selectResourceNode nodeId =
    stores.Nodes.Value |> Array.tryFind (fun node -> node.id = nodeId) |> Option.iter focus
  let resourceDispatch = function
    | ObserveResources seconds ->
      match selectedResourceNode () with
      | Some node ->
        if stores.NodeFocus.Value <> node.id || stores.NodeIncarnation.Value <> node.incarnation then
          Model.focusNode stores node.id node.incarnation
        dispatch (ObserveNodeResources { Node = node.id; Seconds = seconds })
      | None -> ()
    | command -> dispatch command
  let readTranscript (node: NodeWorkView) (operation: NodeOperationView) stream =
    dispatch (ReadNodeTranscript {
      Source = { Node = node.id; Incarnation = node.incarnation }; Journal = node.journal
      Sequence = int64 operation.sequence; Stream = stream; Offset = 0L; MaxBytes = MaxTranscriptPageBytes })
  let readNextTranscript () =
    let value = stores.Transcript.Value
    if value.next <> "" then
      dispatch (ReadNodeTranscript {
        Source = { Node = value.node; Incarnation = value.incarnation }; Journal = value.journal
        Sequence = int64 value.sequence; Stream = value.stream; Offset = int64 value.next; MaxBytes = MaxTranscriptPageBytes })

  // ── View ─────────────────────────────────────────────────────────────────
  let view =
    div [| cls "flex flex-col h-screen bg-base-100 text-base-content text-sm" |] [|
      topBar stores link dark toggleTheme
      show (fun () -> not (connected ()) && not stores.ProtocolMismatch.Value) (fun () ->
        div [| cls "px-4 py-1 text-xs bg-warning text-warning-content" |] [| text "Disconnected · last received state · commands unavailable" |])
      show (fun () -> stores.ProtocolMismatch.Value) (fun () ->
        div [| cls "px-4 py-1 text-xs bg-error text-error-content" |] [| text "UI version changed. Reload this page." |])
      div [| clsRx (fun () -> if connected () then "flex-1 min-h-0 overflow-auto" else "flex-1 min-h-0 overflow-auto opacity-70") |] [|
        if workPage then
          div [| cls "grid grid-cols-1 xl:grid-cols-3 gap-4 p-4" |] [|
            div [| cls "xl:col-span-3" |] [| fleetBalanceBanner stores |]
            div [| cls "xl:col-span-3" |] [| fleetCapacityGrid stores connected |]
            details [| flag "open" true; cls "xl:col-span-3 controller-compiler" |] [|
              summary [| cls "cursor-pointer text-sm font-semibold" |] [| text "Controller compiler controls" |]
              div [| cls "grid grid-cols-1 xl:grid-cols-3 gap-4 mt-3" |] [|
                div [| cls "xl:col-span-2 flex flex-col gap-3 min-w-0" |] [|
                  sessionsPanel stores connected dispatch
                  details [||] [|
                    summary [| cls "cursor-pointer text-xs opacity-60" |] [| text "Open project" |]
                    openBar stores connected dispatch
                  |]
                |]
                div [| cls "flex flex-col gap-3 min-w-0" |] [|
                  workerPanel stores connected dispatch
                |]
              |]
            |]
            div [| cls "xl:col-span-3" |] [| workNodesPanel stores connected readTranscript |]
            show (fun () -> stores.Transcript.Value.node <> "") (fun () -> transcriptPanel stores connected readNextTranscript)
          |]
        else
          div [| cls "flex flex-col gap-4 p-4" |] [|
            healthStrip stores
            fleetBalanceBanner stores
            agentsPanel stores
            hostPanel stores connected selectedResourceNode selectResourceNode resourceDispatch
            details [||] [|
              summary [| cls "cursor-pointer text-xs opacity-60" |] [| text "Daemon snapshot" |]
              resourcesPanel stores
            |]
          |]
      |]
      if workPage then
        show (fun () -> stores.Operations.Value.Length > 0 || stores.Runs.Value.Length > 0 || stores.Activity.Value.Length > 0) (fun () -> outputPane stores)
    |]

  // The bridge starts once the view's signals exist; its callbacks arrive
  // from outside the graph and run on it as batches.
  Bridge.start receive (fun next ->
    act (fun () ->
      match next with
      | Bridge.Retrying _ -> Model.abandonAll stores commands
      | Bridge.Connecting -> stores.Welcomed.Value <- false
      | Bridge.Connected -> ()
      link.Value <- next))
  view
