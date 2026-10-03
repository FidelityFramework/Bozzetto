/// The JavaScript ends of the bridge, as one-liners. Everything that crosses
/// the boundary goes through the shared protocol and the codecs; the
/// reconnect policy lives in Bridge.fs, the model in Model.fs.
///
/// WrenHello posts to a WebKit script-message handler and installs
/// window.wrenDispatch. Bozzetto runs in an ordinary browser, so its bridge
/// is the WREN roadmap transport already: one WebSocket on the page's own
/// origin (text frames today, BAREWire binary frames later).
module Bozzetto.Web.Frontend.Interop

open Fable.Core

/// An open (or opening) browser WebSocket.
type Socket = interface end

/// The bridge endpoint on the page's own origin: the daemon serves the welded
/// page on its MCP port, and Vite's dev server proxies the same path.
[<Emit("(location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ui/bridge'")>]
let bridgeUrl () : string = jsNative

[<Emit("new WebSocket($0)")>]
let openSocket (url: string) : Socket = jsNative

[<Emit("$0.onopen = (() => $1())")>]
let onOpen (socket: Socket) (handler: unit -> unit) : unit = jsNative

/// backend -> UI: every text frame is handed to the dispatch handler.
[<Emit("$0.onmessage = (e => { if (typeof e.data === 'string') $1(e.data) })")>]
let onMessage (socket: Socket) (handler: string -> unit) : unit = jsNative

[<Emit("$0.onclose = (() => $1())")>]
let onClose (socket: Socket) (handler: unit -> unit) : unit = jsNative

/// UI -> backend: send one text frame; false unless the socket is open.
[<Emit("($0.readyState === 1 ? ($0.send($1), true) : false)")>]
let trySend (socket: Socket) (payload: string) : bool = jsNative

/// One deferred call. Its only use is the bridge's reconnect backoff after a
/// socket drop, which waits to retry the connection and polls nothing.
[<Emit("setTimeout($1, $0)")>]
let after (milliseconds: int) (action: unit -> unit) : unit = jsNative

[<Emit("Date.now()")>]
let nowMs () : float = jsNative

/// Frontend-local state (no bridge involved): the DaisyUI theme. data-theme on
/// <html> is the single authority, persisted in localStorage (WrenHello's
/// braidpoint-site pattern).
[<Emit("document.documentElement.setAttribute('data-theme', $0)")>]
let setTheme (name: string) : unit = jsNative

[<Emit("(function(){try{return localStorage.getItem('bozzetto-web-theme')||''}catch(e){return ''}})()")>]
let getSavedTheme () : string = jsNative

[<Emit("(function(){try{localStorage.setItem('bozzetto-web-theme', $0)}catch(e){}})()")>]
let saveTheme (name: string) : unit = jsNative

/// The tab icon follows the system color scheme, as clef-lang.com's does:
/// favicon-dark.svg under a dark scheme, favicon.svg otherwise. It listens for
/// the media query's change event; nothing is polled.
[<Emit("(function(){const l=document.getElementById('favicon-svg');if(!l||!window.matchMedia)return;const q=window.matchMedia('(prefers-color-scheme: dark)');const f=()=>{l.setAttribute('href',q.matches?'favicon-dark.svg':'favicon.svg')};f();q.addEventListener('change',f)})()")>]
let followColorScheme () : unit = jsNative

/// Run arguments are typed as a JSON array of strings; null unless valid.
[<Emit("(function(t){try{const v=JSON.parse(t);return Array.isArray(v)&&v.every(x=>typeof x==='string')?v:null}catch(e){return null}})($0)")>]
let parseArguments (text: string) : string array option = jsNative

[<Emit("$0.target.value")>]
let inputValue (event: obj) : string = jsNative

[<Emit("$0.key")>]
let keyOf (event: obj) : string = jsNative

/// Bridge hop tracing, visible in the browser console.
[<Emit("console.log($0)")>]
let consoleLog (line: string) : unit = jsNative

/// A plain JavaScript object used as a string-keyed dictionary inside a Solid
/// store, so each key is tracked on its own.
type JsDict<'T> = interface end

[<Emit("({})")>]
let emptyDict<'T> () : JsDict<'T> = jsNative

[<Emit("$0[$1]")>]
let dictTryGet (dict: JsDict<'T>) (key: string) : 'T option = jsNative

[<Emit("Object.keys($0)")>]
let dictKeys (dict: JsDict<'T>) : string array = jsNative
