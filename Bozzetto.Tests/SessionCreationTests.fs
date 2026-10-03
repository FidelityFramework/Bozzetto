module Bozzetto.Tests.SessionCreationTests

open System
open System.IO
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Tests.TestInfrastructure

/// Helper: write text to a file with explicit types.
let writeText (path: string) (content: string) =
  File.WriteAllText(path, content)

/// Create a temp directory, run setup + test, then clean up.
let withTempDir (setup: string -> unit) (test: string -> unit) =
  let dir =
    Path.Combine(
      Path.GetTempPath(),
      sprintf "bozzetto-test-%s" (Guid.NewGuid().ToString("N").[..7]))
  try
    Directory.CreateDirectory(dir) |> ignore
    setup dir
    test dir
  finally
    if Directory.Exists dir then
      Directory.Delete(dir, true)

let addConfig dir content =
  let configDir = Path.Combine(dir, ".bozzetto")
  Directory.CreateDirectory(configDir) |> ignore
  writeText (Path.Combine(configDir, "config.fsx")) content

[<Tests>]
let tests = testSequenced <| testList "Session Creation" [

  testList "DirectoryConfig.autoOpenNamespacesForDirectory compatibility default" [

    testCase "retired config is not applied to the compatibility default" <| fun _ ->
      withTempDir
        (fun dir ->
          addConfig dir """{ DirectoryConfig.empty with AutoOpenNamespaces = false }""")
        (fun dir ->
          DirectoryConfig.autoOpenNamespacesForDirectory dir
          |> Expect.isTrue "retired FSharp configuration is not evaluated")

    testCase "defaults to true when no config exists" <| fun _ ->
      withTempDir
        (fun _ -> ())
        (fun dir ->
          DirectoryConfig.autoOpenNamespacesForDirectory dir
          |> Expect.isTrue "should default to true")
  ]
]
