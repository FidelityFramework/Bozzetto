// Frontend entry point: one reactive graph for the page, and the App mounted
// into the #app div on it.
module Bozzetto.Web.Frontend.Main

open Fable.Core
open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Bozzetto.Web.Frontend

[<Emit("document.getElementById($0)")>]
let private element (id: string) : Ranvier.Dom.Element = jsNative

let private report (errors: exn list) =
  for error in errors do
    JS.console.error ("Bozzetto view failure", error)

let private graph = new Graph()
View.useGraph graph
Interop.followColorScheme ()
graph.Run(fun () ->
  let page = App.app ()
  Mounting.mount graph None report (element "app") None (fun context -> Program.run page context.Backend) |> ignore)
