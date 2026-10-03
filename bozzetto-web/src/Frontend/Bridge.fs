/// The bridge as the UI sees it: send one frame, receive frames, and know
/// whether the link is up. One WebSocket, reconnected with capped backoff.
/// Frames are already encoded; this module knows nothing of the protocol.
module Bozzetto.Web.Frontend.Bridge

open Bozzetto.Web.Frontend

/// Link state: pure UI state, never sent over the bridge.
type Link =
  | Connecting
  | Connected
  /// Attempt number and the time (epoch ms) of the next attempt.
  | Retrying of int * float

let mutable private socket: Interop.Socket option = None

/// Send one encoded frame; false when the link is down (nothing is queued:
/// a command issued while disconnected is refused locally, not replayed).
let send (payload: string) : bool =
  match socket with
  | Some open' -> Interop.trySend open' payload
  | None -> false

/// Connect and keep reconnecting: 1 s, 2 s, 4 s, 8 s, then every 10 s.
/// `onFrame` receives every backend frame; `onLink` every link change. The
/// backend pushes current state on each new connection, so a reconnect
/// needs no replay.
let start (onFrame: string -> unit) (onLink: Link -> unit) : unit =
  let url = Interop.bridgeUrl ()
  let rec connect (attempt: int) =
    if attempt = 0 then onLink Connecting
    let current = Interop.openSocket url
    socket <- Some current
    let opened = ref false
    Interop.onOpen current (fun () ->
      opened.Value <- true
      onLink Connected)
    Interop.onMessage current onFrame
    Interop.onClose current (fun () ->
      socket <- None
      let next = if opened.Value then 1 else attempt + 1
      let delay = min 10000 (500 * pown 2 (min next 5))
      onLink (Retrying(next, Interop.nowMs () + float delay))
      Interop.after delay (fun () -> connect next))
  connect 0
