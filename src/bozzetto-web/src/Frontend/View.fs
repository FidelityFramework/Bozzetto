// The page's authoring vocabulary on Ranvier: Ranvier.View.Dom's Program
// views (elements built once, then updated in place through their own
// bindings) and Ranvier signals. Nothing here diffs a tree: a text, class or
// attribute binding reads its signals and rewrites only its own DOM node when
// the value it computes changes; a keyed list moves, adds or removes only the
// rows whose keys changed; a conditional remounts only when its condition
// flips.
module Bozzetto.Web.Frontend.View

open Fable.Core
open Fable.Core.JsInterop
open global.Ranvier
open Ranvier.View

type V = Program.View
type M = Typed.Modifier<Ranvier.Dom.HTMLElement>

// The page's one reactive graph, set once by Main before the first mount.
let mutable private graph: Graph = Unchecked.defaultof<Graph>

let useGraph (value: Graph) = graph <- value

// Runs a callback that arrives from outside the graph (a socket frame, a link
// change, a DOM event) on the page's graph, with its writes as one batch: an
// effect that reads several written signals runs once, after the last write.
let act (body: unit -> unit) : unit = graph.Run(fun () -> Api.batch body)

// ── Views ──────────────────────────────────────────────────────────────────

let inline el (tag: string) (modifiers: M array) (children: V array) : V =
  Typed.programElement (HtmlTag tag) modifiers children

let text (value: string) : V = Program.text value

// A text node bound to its reader: rewritten only when the string changes.
let rx (read: unit -> string) : V = Program.bind (=) read

let fragment (children: V array) : V = Program.fragment children

let empty: V = Program.ops.Empty

// One row per item, retained by key; a row reads its current item through
// the reader it is given.
let each (key: 'T -> 'K) (items: unit -> 'T array) (row: (unit -> 'T) -> V) : V =
  Keyed.array Program.ops key items row

// Rows without an identity of their own, retained by position.
let eachIndexed (items: unit -> 'T array) (row: (unit -> 'T) -> V) : V =
  Keyed.array Program.ops fst (fun () -> Array.indexed (items ())) (fun current -> row (fun () -> snd (current ())))

// `yes` while the condition holds, `no` otherwise; each flip remounts.
let showElse (condition: unit -> bool) (yes: unit -> V) (no: unit -> V) : V =
  Branch.select Program.ops id condition (fun current -> if current () then yes () else no ())

let show (condition: unit -> bool) (yes: unit -> V) : V = showElse condition yes (fun () -> empty)

// Setup that runs once per mounted instance: local signals live here.
let comp (setup: unit -> V) : V = Program.createComponent (fun _ -> setup ())

// ── Modifiers ──────────────────────────────────────────────────────────────

let attr (name: string) (value: string) : M = Typed.attribute name value

let attrRx (name: string) (read: unit -> string) : M = Typed.reactiveAttribute name (fun () -> Some(read ()))

let cls (value: string) : M = attr "class" value

let clsRx (read: unit -> string) : M = attrRx "class" read

let title (value: string) : M = attr "title" value

let titleRx (read: unit -> string) : M = attrRx "title" read

let aria (name: string) (value: string) : M = attr ("aria-" + name) value

let flag (name: string) (enabled: bool) : M = Typed.booleanAttribute name enabled

let flagRx (name: string) (read: unit -> bool) : M = Typed.reactiveBooleanAttribute name read

// A live DOM property (an input's or select's current value).
let propRx (name: string) (read: unit -> 'T) : M =
  Typed.reactiveProperty (fun (node: Ranvier.Dom.HTMLElement) (value: 'T) -> node?(name) <- value) read

[<Emit("queueMicrotask($0)")>]
let private afterCommit (action: unit -> unit) : unit = jsNative

// A select's current value. An element's modifiers apply before its options
// are built, and a value naming an option that is not there yet is dropped,
// so the write lands once the commit that built or changed the options ends.
let selectValue (read: unit -> string) : M =
  Typed.reactiveProperty (fun (node: Ranvier.Dom.HTMLElement) (value: string) -> afterCommit (fun () -> node?value <- value)) read

let on (eventName: string) (handler: obj -> unit) : M =
  Typed.listener eventName (fun event -> act (fun () -> handler (box event)))
