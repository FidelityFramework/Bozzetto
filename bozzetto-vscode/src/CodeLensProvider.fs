module Bozzetto.Vscode.CodeLensProvider

open Fable.Core
open Fable.Core.JsInterop
open Vscode

module Blocks = Bozzetto.Vscode.CodeBlocks

/// Creates a CodeLens provider object compatible with VSCode's API.
/// Shows "▶ Eval" at the start of each code block — either ;; delimited or blank-line separated.
/// Respects density setting: disabled in Minimal and Normal modes.
let create () =
  createObj [
    "provideCodeLenses" ==> fun (doc: TextDocument) (_token: obj) ->
      let cfg = Workspace.getConfiguration "bozzetto"
      let density = Bozzetto.Vscode.DensityPure.Density.ofString (cfg.get("density", "full"))
      match Bozzetto.Vscode.DensityPure.shows density Bozzetto.Vscode.DensityPure.AnnotationSurface.EvalCodeLens with
      | false -> [||]
      | true ->
      let lenses = ResizeArray<CodeLens>()
      let blocks = Blocks.getAllBlockRanges doc
      for blockStart, _blockEnd in blocks do
        let range = newRange blockStart 0 blockStart 0
        let cmd = createObj [
          "title" ==> "▶ Eval"
          "command" ==> "bozzetto.eval"
          "arguments" ==> [| box blockStart |]
        ]
        lenses.Add(newCodeLens range cmd)
      lenses.ToArray()
  ]
