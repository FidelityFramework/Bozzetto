#load "../build/HarmonyPackage.fs"

open Bozzetto.Build.HarmonyPackage

let result =
  match fsi.CommandLineArgs |> Array.skip 1 |> Array.skipWhile ((=) "--") with
  | [| "verify"; root |] -> validate root
  | [| "import"; package; source; root |] -> importPackage package source root
  | _ -> Error "Usage: harmony-package.fsx verify REPO_ROOT | import NUPKG SOURCE_FORK REPO_ROOT (absolute paths required)"

match result with
| Ok () -> printfn "Bozzetto.Harmony package verified."
| Error message ->
  eprintfn "%s" message
  exit 1
