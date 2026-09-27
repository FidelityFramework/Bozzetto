module SageFs.Vscode.SafeInterop

open Fable.Core
open Fable.Core.JsInterop

// ── JS runtime type checks (compile to ACTUAL checks, not erased) ───

[<Emit("typeof $0")>]
let jsTypeof (x: obj) : string = jsNative

[<Emit("($0 == null)")>]
let jsIsNullOrUndefined (x: obj) : bool = jsNative

[<Emit("Array.isArray($0)")>]
let jsIsArray (x: obj) : bool = jsNative

[<Emit("Number.isInteger($0)")>]
let jsIsInteger (x: obj) : bool = jsNative

[<Emit("console.warn('[SageFs]', $0)")>]
let private logWarnRaw (msg: string) : unit = jsNative

// ── Null-guard combinator (shared across all cast functions) ────────

let inline private withNullCheck (f: obj -> 'T option) (x: obj) : 'T option =
  match jsIsNullOrUndefined x with
  | true -> None
  | false -> f x

// ── Runtime-checked type casts ──────────────────────────────────────
// Unlike unbox<'T> which Fable erases to a no-op, these do ACTUAL
// JavaScript typeof / Array.isArray / Number.isInteger checks.

let tryCastString : obj -> string option =
  withNullCheck (fun x ->
    match jsTypeof x with
    | "string" -> Some (unbox x)
    | _ -> None)

let tryCastInt : obj -> int option =
  withNullCheck (fun x ->
    match jsIsInteger x with
    | true -> Some (unbox x)
    | false -> None)

let tryCastFloat : obj -> float option =
  withNullCheck (fun x ->
    match jsTypeof x with
    | "number" -> Some (unbox x)
    | _ -> None)

let tryCastBool : obj -> bool option =
  withNullCheck (fun x ->
    match jsTypeof x with
    | "boolean" -> Some (unbox x)
    | _ -> None)

let tryCastArray : obj -> obj array option =
  withNullCheck (fun x ->
    match jsIsArray x with
    | true -> Some (unbox x)
    | false -> None)

// ── Compound cast helpers ───────────────────────────────────────────

let tryCastStringArray (x: obj) : string array option =
  tryCastArray x
  |> Option.map (Array.choose tryCastString)

let tryCastIntArray (x: obj) : int array option =
  tryCastArray x
  |> Option.map (Array.choose tryCastInt)

// ── Typed field accessors (replace generic tryField<'T>) ────────────
// Each accessor validates the JS type at runtime before returning.
// On type mismatch, logs a warning and returns None.

let private rawField (name: string) (obj: obj) : obj option =
  match jsIsNullOrUndefined obj with
  | true -> None
  | false ->
    let v = obj?(name)
    match jsIsNullOrUndefined v with
    | true -> None
    | false -> Some v

let private fieldWithCast (typeName: string) (cast: obj -> 'T option) (name: string) (obj: obj) : 'T option =
  match rawField name obj with
  | None -> None
  | Some v ->
    match cast v with
    | Some x -> Some x
    | None ->
      logWarnRaw (sprintf "Field '%s': expected %s, got %s" name typeName (jsTypeof v))
      None

let fieldString (name: string) (obj: obj) : string option = fieldWithCast "string" tryCastString name obj
let fieldInt (name: string) (obj: obj) : int option = fieldWithCast "int" tryCastInt name obj
let fieldFloat (name: string) (obj: obj) : float option = fieldWithCast "float" tryCastFloat name obj
let fieldBool (name: string) (obj: obj) : bool option = fieldWithCast "boolean" tryCastBool name obj
let fieldArray (name: string) (obj: obj) : obj array option = fieldWithCast "array" tryCastArray name obj
let fieldObj : string -> obj -> obj option = rawField

let fieldStringArray (name: string) (obj: obj) : string array option =
  rawField name obj |> Option.bind tryCastStringArray

let fieldIntArray (name: string) (obj: obj) : int array option =
  rawField name obj |> Option.bind tryCastIntArray

// ── DU parsing helpers (Fable serializes DUs as { Case, Fields }) ───

let duCase (du: obj) : string option =
  fieldString "Case" du
  |> Option.orElse (
    withNullCheck (fun x -> Some (string x)) du)

let duFieldsArray (du: obj) : obj array option =
  rawField "Fields" du |> Option.bind tryCastArray

let duFirstFieldString (du: obj) : string option =
  duFieldsArray du
  |> Option.bind (fun arr ->
    match arr.Length with
    | 0 -> None
    | _ -> tryCastString arr.[0])

let duFirstFieldInt (du: obj) : int option =
  duFieldsArray du
  |> Option.bind (fun arr ->
    match arr.Length with
    | 0 -> None
    | _ -> tryCastInt arr.[0])

// ── Error isolation ─────────────────────────────────────────────────

[<Emit("console.error('[SageFs]', $0, $1)")>]
let private logErrorRaw (msg: string) (err: obj) : unit = jsNative

let tryHandleEvent (eventType: string) (fn: unit -> unit) : unit =
  try fn ()
  with ex -> logErrorRaw (sprintf "SSE handler error for '%s'" eventType) ex
