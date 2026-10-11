module Bozzetto.Tests.DirectoryConfigTests

open System
open System.IO
open Expecto
open Expecto.Flip
open Bozzetto

let private withDirectory body =
  let dir = Path.Combine(Path.GetTempPath(), "bozzetto-retired-config-" + Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory dir |> ignore
  try body dir
  finally Directory.Delete(dir, true)

let private writeConfig (dir: string) (content: string) =
  let path = DirectoryConfig.configPath dir
  Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
  File.WriteAllText(path, content)
  path

[<Tests>]
let evaluateTests =
  testList "Retired FSharp config evaluation" [
    testCase "valid and malformed scripts return the same actionable service refusal" <| fun () ->
      for content in [ "DirectoryConfig.empty"; "this is not valid F#" ] do
        DirectoryConfig.evaluate content
        |> Expect.equal "Bozzetto does not compile or evaluate the script" (Error ExternalFSharpService.message)

    testCase "an effectful script cannot run through evaluateIn" <| fun () ->
      withDirectory (fun dir ->
        let marker = Path.Combine(dir, "must-not-exist")
        let script = sprintf "System.IO.File.WriteAllText(@\"%s\", \"executed\"); DirectoryConfig.empty" marker
        DirectoryConfig.evaluateIn dir script
        |> Expect.equal "refuses before creating an FSI host" (Error ExternalFSharpService.message)
        File.Exists marker |> Expect.isFalse "no user effect was executed")
  ]

[<Tests>]
let loadTests =
  testList "Retired FSharp config loading" [
    testCase "no config is still absent" <| fun () ->
      withDirectory (fun dir -> DirectoryConfig.load dir |> Expect.isNone "no config")

    testCase "existing valid or malformed scripts are preserved without evaluation or fallback config" <| fun () ->
      for content in [ "DirectoryConfig.empty"; "this is not valid F#" ] do
        withDirectory (fun dir ->
          let path = writeConfig dir content
          DirectoryConfig.load dir |> Expect.isNone "a retired script is not replaced by defaults"
          File.ReadAllText path |> Expect.equal "script bytes remain untouched" content)

    testCase "loading an effectful script preserves it and cannot create its marker" <| fun () ->
      withDirectory (fun dir ->
        let marker = Path.Combine(dir, "must-not-exist")
        let script = sprintf "System.IO.File.WriteAllText(@\"%s\", \"executed\"); DirectoryConfig.empty" marker
        let path = writeConfig dir script
        DirectoryConfig.load dir |> Expect.isNone "no FSharp config is loaded"
        File.Exists marker |> Expect.isFalse "no user effect"
        File.ReadAllText path |> Expect.equal "source is preserved" script)

    testCase "configPath retains the legacy location for diagnostics" <| fun () ->
      DirectoryConfig.configPath "/project"
      |> Expect.equal "the existing file can be identified" (Path.Combine("/project", ".bozzetto", "config.fsx"))
  ]

[<Tests>]
let ensureAutoOpenOptOutTests =
  testList "Retired FSharp config mutation" [
    for name, operation in
      [ "opt out", fun dir -> DirectoryConfig.ensureAutoOpenNamespacesOptOut dir |> Result.map ignore
        "opt in", fun dir -> DirectoryConfig.ensureAutoOpenNamespacesOptIn dir |> Result.map ignore ] do
      testCase (name + " refuses without creating a config directory") <| fun () ->
        withDirectory (fun dir ->
          operation dir |> Expect.equal "external service owns the operation" (Error ExternalFSharpService.message)
          Directory.Exists(DirectoryConfig.configDir dir) |> Expect.isFalse "no file or directory was created")
      testCase (name + " refuses without overwriting an existing script") <| fun () ->
        withDirectory (fun dir ->
          let content = "// user configuration\nDirectoryConfig.empty"
          let path = writeConfig dir content
          operation dir |> Expect.equal "external service owns the operation" (Error ExternalFSharpService.message)
          File.ReadAllText path |> Expect.equal "source bytes are preserved" content)
  ]
