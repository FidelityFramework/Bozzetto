/// Frontend entry point: mount the App into the #app div.
module Bozzetto.Web.Frontend.Main

open Browser.Dom
open Partas.Solid
open Bozzetto.Web.Frontend

Interop.followColorScheme ()
render ((fun () -> App.App ()), document.getElementById "app")
