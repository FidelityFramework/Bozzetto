/// Observes and places an externally launched demo window; it never starts
/// the application process.
///
/// Two `AppKind`s, one real capture mechanism: `Raylib`/`Console` apps draw
/// their own window directly on the shared `:99` display (a GUI window for
/// Raylib; a terminal surface for Console). This actor watches the display's
/// root window for a NEW top-level window to appear (a real, live
/// `XQueryTree` diff — the exact "attach to a real launched window" proof the
/// RED tests demand, never a guess at a window id), moves/resizes it into the
/// co-actor's rect via `XMoveResizeWindow`, and observes content changes via
/// a real `XGetImage` pixel-fingerprint diff — the plan's "region diff"
/// mechanism. (A former `Web` kind, which opened a second Chromium on a web
/// sample's URL and diffed Playwright screenshots, left with the Datastar web
/// sample.)
///
/// Deliberately its own small, self-contained P/Invoke surface (`Native`
/// below) rather than growing `XTest.fs`'s contract: `XTest.fs` is the
/// input-delivery edge every actor shares verbatim (§1 — "do NOT rewrite");
/// window discovery/placement/pixel-capture is a DIFFERENT concern only this
/// actor needs, so it does not force every other actor's shared edge to grow
/// a window-management API it will never use.
module Bozzetto.Demos.Actors.App

open System
open System.Runtime.InteropServices
open Bozzetto.Demos.Domain
open Bozzetto.Demos.Actors.Actor

/// Xlib's DEFAULT error handler does not just print an `XErrorEvent` — it
/// calls `exit(1)` on the whole process for anything it does not recognize
/// as one of a handful of always-ignorable cases. A transient, genuinely
/// recoverable protocol error (§4.11's own doctrine: "a real X protocol
/// error is a real bug to fix, never something to guard-and-ignore" governs
/// XTEST *input delivery*; a capture race against a window's own map/resize
/// timing is a DIFFERENT, expected transient — e.g. `XGetImage`'s BadMatch
/// on a window this actor just discovered via `XQueryTree` a moment before
/// it finished mapping) must never be allowed to kill the actor, let alone
/// the whole recording run. Installing a handler that returns instead of
/// aborting turns every such error into an ordinary failed call (a NULL/zero
/// return this module already treats as `None`/`false`), which is the
/// correct, honest outcome for a transient capture race.
type private XErrorHandler = delegate of nativeint * nativeint -> int

module private Native =
  [<DllImport("libX11.so.6")>]
  extern nativeint XOpenDisplay(string display)

  [<DllImport("libX11.so.6")>]
  extern nativeint XSetErrorHandler(XErrorHandler handler)

  [<DllImport("libX11.so.6")>]
  extern int XCloseDisplay(nativeint display)

  [<DllImport("libX11.so.6")>]
  extern nativeint XDefaultRootWindow(nativeint display)

  [<DllImport("libX11.so.6")>]
  extern int XQueryTree(nativeint display, nativeint w, nativeint& root_return, nativeint& parent_return, nativeint& children_return, uint32& nchildren_return)

  [<DllImport("libX11.so.6")>]
  extern int XFree(nativeint data)

  [<DllImport("libX11.so.6")>]
  extern int XGetGeometry(nativeint display, nativeint d, nativeint& root_return, int& x_return, int& y_return, uint32& width_return, uint32& height_return, uint32& border_width_return, uint32& depth_return)

  [<DllImport("libX11.so.6")>]
  extern int XMoveResizeWindow(nativeint display, nativeint w, int x, int y, uint32 width, uint32 height)

  [<DllImport("libX11.so.6")>]
  extern int XSync(nativeint display, bool discard)

  [<DllImport("libX11.so.6")>]
  extern nativeint XGetImage(nativeint display, nativeint d, int x, int y, uint32 width, uint32 height, unativeint plane_mask, int format)

  [<DllImport("libX11.so.6")>]
  extern unativeint XGetPixel(nativeint ximage, int x, int y)

  [<DllImport("libX11.so.6")>]
  extern int XDestroyImage(nativeint ximage)

[<Literal>]
let private ZPixmap = 2

/// Xlib's `AllPlanes` macro (`((unsigned long)~0L)`) — every plane, i.e. the
/// full pixel value, not a masked subset.
let private allPlanes: unativeint = unativeint UInt64.MaxValue

/// Kept alive for the whole process (a delegate passed to native code is
/// otherwise eligible for GC as soon as this expression finishes, leaving
/// Xlib holding a dangling function pointer the moment it next errors).
let mutable private errorHandler: XErrorHandler = Unchecked.defaultof<_>

/// Installs the non-aborting error handler (module doc above) exactly once
/// per process — idempotent, since a second `XSetErrorHandler` call is
/// harmless but pointless. Called before this module's first real X11
/// connection so no window-management call can ever take the process down.
let private installErrorHandler =
  lazy
    (errorHandler <- XErrorHandler(fun _ _ -> 0)
     Native.XSetErrorHandler errorHandler |> ignore)

/// The live children of `window`, in server order — with no window manager
/// running inside a cell (`Runtime.fs`'s inner script starts only Xvfb, never
/// a WM), every app's own top-level window is a DIRECT child of the root
/// window, so this is exactly the set of "top-level app windows" on the
/// display, no EWMH `_NET_CLIENT_LIST` (which only a WM publishes) required.
let private queryChildren (display: nativeint) (window: nativeint) : nativeint list =
  let mutable root = IntPtr.Zero
  let mutable parent = IntPtr.Zero
  let mutable children = IntPtr.Zero
  let mutable count = 0u

  let status = Native.XQueryTree(display, window, &root, &parent, &children, &count)

  if status = 0 || children = IntPtr.Zero then
    []
  else
    try
      [ for i in 0 .. int count - 1 -> Marshal.ReadIntPtr(children, i * IntPtr.Size) ]
    finally
      Native.XFree children |> ignore

/// `window`'s CURRENT geometry, always queried live against the display —
/// never cached — per §3's "a `ResolvedTarget` is a screen rect that came
/// from a live actor, never a guess". `None` only when the window is already
/// gone (a real, honest failure to report, not a stale rect).
let private geometryOf (display: nativeint) (window: nativeint) : Rect option =
  let mutable root = IntPtr.Zero
  let mutable x = 0
  let mutable y = 0
  let mutable width = 0u
  let mutable height = 0u
  let mutable border = 0u
  let mutable depth = 0u

  let status =
    Native.XGetGeometry(display, window, &root, &x, &y, &width, &height, &border, &depth)

  if status = 0 then None else Some { X = x; Y = y; W = int width; H = int height }

let private placeWindow (display: nativeint) (window: nativeint) (rect: Rect) : unit =
  Native.XMoveResizeWindow(display, window, rect.X, rect.Y, uint32 rect.W, uint32 rect.H)
  |> ignore

  Native.XSync(display, false) |> ignore

/// A cheap, REAL content fingerprint of `window`'s current on-screen pixels
/// (never faked, per the repo's "a demo step must genuinely pass" doctrine):
/// grabs one real `XImage` of the window's current rect and folds an FNV-1a
/// hash over a sparse grid of real sampled pixels (every 8th row/column —
/// full-resolution capture is unnecessary to answer "did this visibly
/// change", and sparse sampling keeps a per-poll capture cheap enough for a
/// tight observation loop). `None` only on a real capture failure (the
/// window has already gone away, or shrunk to zero) — a genuine signal, not
/// something papered over as "unchanged".
let private capturePixelFingerprint (display: nativeint) (window: nativeint) (rect: Rect) : uint64 option =
  if rect.W <= 0 || rect.H <= 0 then
    None
  else
    let image = Native.XGetImage(display, window, 0, 0, uint32 rect.W, uint32 rect.H, allPlanes, ZPixmap)

    if image = IntPtr.Zero then
      None
    else
      try
        let mutable hash = 1469598103934665603UL // FNV-1a offset basis
        let step = 8

        for py in 0 .. step .. rect.H - 1 do
          for px in 0 .. step .. rect.W - 1 do
            let pixel = uint64 (Native.XGetPixel(image, px, py))
            hash <- (hash ^^^ pixel) * 1099511628211UL // FNV-1a prime

        Some hash
      finally
        Native.XDestroyImage image |> ignore

/// The minimum plausible content-window side, in pixels — small enough to
/// never reject a genuinely tiny real app window, large enough to skip the
/// 1x1/unset-size helper windows a real GUI toolkit routinely creates
/// alongside its real one (a WM-client-leader window, an IPC/drag-and-drop
/// proxy, ...) — none of which is ever a window a scenario wants captured.
[<Literal>]
let private MinPlausibleWindowSide = 8

/// Among a set of candidate windows, the one with the largest current area —
/// the best available live signal for "the real content window" versus a
/// same-process helper window, since a toolkit's own hint/proxy windows are
/// routinely created at a degenerate 1x1 (or unset) size while a real
/// top-level content window is not. Only windows at least
/// `MinPlausibleWindowSide` on each side are even considered; `None` if
/// every candidate is still too small to judge (the poll loop keeps going
/// rather than locking onto a decoy).
let private largestPlausibleWindow (display: nativeint) (candidates: nativeint list) : nativeint option =
  candidates
  |> List.choose (fun w -> geometryOf display w |> Option.map (fun r -> w, r))
  |> List.filter (fun (_, r) -> r.W >= MinPlausibleWindowSide && r.H >= MinPlausibleWindowSide)
  |> List.sortByDescending (fun (_, r) -> r.W * r.H)
  |> List.tryHead
  |> Option.map fst

/// Polls the display's root window until a window id NOT in `before`
/// appears AND is plausibly a real content window (`largestPlausibleWindow`
/// above — never just "whichever window id happened to sort first", which a
/// same-process helper window can just as easily be), or fails loud with an
/// actionable message on timeout — the "never a silent green no-op"
/// doctrine (§2): a missing/never-mapped app window is reported as a real
/// `Error`, never treated as "nothing to capture".
let private pollForNewWindow (display: nativeint) (root: nativeint) (before: Set<nativeint>) (timeoutMs: int) : Async<Result<nativeint, string>> =
  async {
    let sw = Diagnostics.Stopwatch.StartNew()
    let mutable found: nativeint option = None

    while found.IsNone && sw.ElapsedMilliseconds < int64 timeoutMs do
      let fresh = (queryChildren display root |> Set.ofList) - before

      match largestPlausibleWindow display (fresh |> Set.toList) with
      | Some w -> found <- Some w
      | None -> do! Async.Sleep 200

    match found with
    | Some w -> return Ok w
    | None ->
      return
        Error(
          sprintf
            "no new X11 window appeared on the display within %dms — the run-app-launched app never mapped a top-level window (is run-app actually wired to start it before the App actor waits?)"
            timeoutMs
        )
  }

/// Fully-resolved launch configuration (the cell-agent builds this — this
/// actor itself never resolves a path, a port, or an env var).
type LaunchConfig =
  { /// The Xvfb display every actor in the cell shares (`CellAgent.CellDisplay`,
    /// kept a plain string here so this module never depends on the
    /// cell-agent).
    Display: string
    /// How long to wait for the run-app-launched window to appear on the
    /// shared display before failing loud — never silently hanging forever.
    ReadyTimeoutMs: int }

module LaunchConfig =
  /// A reasonable default: a real session warmup + `dotnet run`-launched
  /// sample can genuinely take longer than a UI click ever needed to
  /// (mirrors `CellAgent.fs`'s own 90s `Observe` ceiling's reasoning).
  let DefaultReadyTimeoutMs = 30_000

/// One live App co-actor: the raw X11 window it captures on its own display
/// connection (`launchWindowed` is the only producer).
type Handle =
  { Kind: AppKind
    Display: nativeint
    Window: nativeint option
    /// The rect this actor was placed at — only a fallback for `observe`'s
    /// capture rect; `resolveRect` always re-queries live geometry instead
    /// of trusting this.
    PlacedRect: Rect }

let private launchWindowed (kind: AppKind) (config: LaunchConfig) (rect: Rect) : Async<Result<Handle, string>> =
  async {
    installErrorHandler.Force()
    let display = Native.XOpenDisplay(config.Display)

    if display = IntPtr.Zero then
      return Error(sprintf "App(%A) could not open X11 display '%s' — is Xvfb running on this cell?" kind config.Display)
    else
      let root = Native.XDefaultRootWindow display
      let before = queryChildren display root |> Set.ofList

      match! pollForNewWindow display root before config.ReadyTimeoutMs with
      | Error message ->
        Native.XCloseDisplay display |> ignore
        return Error(sprintf "App(%A): %s" kind message)
      | Ok window ->
        placeWindow display window rect

        return
          Ok
            { Kind = kind
              Display = display
              Window = Some window
              PlacedRect = rect }
  }

/// Launches the App co-actor for `appKind` at `rect` — waiting for (never
/// starting) the window the daemon's run-app already produced, per
/// §2.3's "finds, places, and observes ... does not start it". Fails loud
/// (`Result.Error` with an actionable message), never a silent no-op, for
/// every case the common external-dependency doctrine (§2) requires: no new
/// window ever appears, or `AppKind.NoApp` (which has no window to capture
/// at all — a scenario author error, not something this actor can paper
/// over).
let launch (config: LaunchConfig) (rect: Rect) (appKind: AppKind) : Async<Result<Handle, string>> =
  async {
    match appKind with
    | AppKind.Raylib -> return! launchWindowed AppKind.Raylib config rect
    | AppKind.Console -> return! launchWindowed AppKind.Console config rect
    | AppKind.NoApp ->
      return Error "App co-actor cannot launch for AppKind.NoApp — a NoApp scenario never places the App actor in its Layout (demo-actors-plan.md §2.3)"
  }

/// Resolves the App co-actor's CURRENT window rect. The `_selector`
/// parameter (the `LiveActor.ResolveRect` contract's own shape) is unused —
/// this actor tracks exactly one window, never a set of selectable elements
/// inside it, so every call reports the same live rect regardless of the
/// string passed.
let resolveRect (handle: Handle) (_selector: string) : Async<ScreenRect option> =
  async {
    match handle.Window with
    | Some window when handle.Display <> IntPtr.Zero -> return geometryOf handle.Display window
    | _ -> return None
  }

/// Retries a real pixel capture for up to `graceMs` before giving up — a
/// window this actor just attached to via `pollForNewWindow` can be a beat
/// away from actually being viewable (created, but not yet mapped/painted
/// by its owning process), so the FIRST capture attempt failing is not yet
/// the honest "no baseline exists" answer; only exhausting the grace period
/// is.
let private captureWithRetry (display: nativeint) (window: nativeint) (graceMs: int) : Async<uint64 option> =
  async {
    let sw = Diagnostics.Stopwatch.StartNew()
    let mutable result = None

    while result.IsNone && sw.ElapsedMilliseconds < int64 graceMs do
      let rect = geometryOf display window |> Option.defaultValue { X = 0; Y = 0; W = 0; H = 0 }
      result <- capturePixelFingerprint display window rect

      if result.IsNone then
        do! Async.Sleep 200

    return result
  }

/// Observes `Signal.AppOutputChanged` via a REAL content diff — an XGetImage
/// pixel fingerprint of the app's window — polling until the content
/// genuinely differs from its value at the START of this call, or the
/// timeout elapses. Never a faked "yes" — a capture failure (window already
/// gone) reports `false`, the honest "did not observe a change" outcome.
let observe (handle: Handle) (_selector: string) (timeoutMs: float) : Async<bool> =
  async {
    match handle.Window with
    | Some window when handle.Display <> IntPtr.Zero ->
      let rect = geometryOf handle.Display window |> Option.defaultValue handle.PlacedRect
      let graceMs = min 3000 (int timeoutMs)

      match! captureWithRetry handle.Display window graceMs with
      | None -> return false
      | Some baseline ->
        let sw = Diagnostics.Stopwatch.StartNew()
        let mutable changed = false

        while not changed && sw.Elapsed.TotalMilliseconds < timeoutMs do
          do! Async.Sleep 250

          match capturePixelFingerprint handle.Display window rect with
          | Some current when current <> baseline -> changed <- true
          | _ -> ()

        return changed
    | _ -> return false
  }

let close (handle: Handle) : Async<unit> =
  async {
    if handle.Display <> IntPtr.Zero then
      Native.XCloseDisplay handle.Display |> ignore
  }

/// A real, live presence poll — "is the app's window discoverable yet"
/// — through the SAME `resolveRect` a caller would use to place/observe it,
/// never a guess that a daemon run-app call "must have worked" by now. Used
/// by the wire's `"app-running"` selector (seam-integration threading:
/// `Runtime.fs`'s `expectationWire` maps `Expectation.AppState
/// AppRunStateCase.Running` here) — genuinely polls until this actor itself
/// can resolve a rect, or the timeout elapses.
let private pollRunning (handle: Handle) (timeoutMs: float) : Async<bool> =
  let deadline = Diagnostics.Stopwatch.StartNew()

  let rec loop () =
    async {
      match! resolveRect handle "" with
      | Some _ -> return true
      | None ->
        if deadline.Elapsed.TotalMilliseconds > timeoutMs then
          return false
        else
          do! Async.Sleep 250
          return! loop ()
    }

  loop ()

/// Wraps this actor behind the cell-agent's actor-dispatch seam (Island F,
/// demo-actors-plan.md §1.2). `Command` is a no-op: the App co-actor is
/// never the target of a `ClientCommand` (it does not open files or run
/// itself — the primary actor does that). `Observe` branches on the wire
/// selector: a presence poll for `"app-running"`, the existing real
/// content-diff for everything else (`"app-output-changed"` and any future
/// region-diff selector this actor gains) — never a fabricated pass either
/// way.
let toLiveActor (handle: Handle) : LiveActor =
  let observeSelector (selector: string) (timeoutMs: float) : Async<bool> =
    if selector = "app-running" then
      pollRunning handle timeoutMs
    else
      observe handle selector timeoutMs

  { Id = ActorId.App
    ResolveRect = resolveRect handle
    Observe = observeSelector
    Command = fun _ -> async { return () }
    Close = fun () -> close handle }
