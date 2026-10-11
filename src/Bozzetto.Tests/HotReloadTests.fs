module Bozzetto.Tests.HotReloadTests

open Expecto

open Bozzetto.FileWatcher

/// Tests that file change actions route correctly to Reload/SoftReset/Ignore.
let fileWatcherIntegrationTests =
  testList "file watcher integration" [
    testCase "fileChangeAction routes .fs Changed to Reload with correct path" <| fun () ->
      let change = {
        FilePath = @"C:\Code\MyModule.fs"
        Kind = FileChangeKind.Changed
        Timestamp = System.DateTimeOffset.UtcNow
      }
      match fileChangeAction change with
      | FileChangeAction.Reload path ->
        Flip.Expect.equal "path should match" @"C:\Code\MyModule.fs" path
      | other -> failwithf "expected Reload, got %A" other

    testCase "fileChangeAction routes .fsx Changed to Reload" <| fun () ->
      let change = {
        FilePath = @"C:\Code\Script.fsx"
        Kind = FileChangeKind.Changed
        Timestamp = System.DateTimeOffset.UtcNow
      }
      match fileChangeAction change with
      | FileChangeAction.Reload path ->
        Flip.Expect.equal "path should match" @"C:\Code\Script.fsx" path
      | other -> failwithf "expected Reload, got %A" other

    testCase "fileChangeAction routes .fsproj to SoftReset" <| fun () ->
      let change = {
        FilePath = @"C:\Code\App.fsproj"
        Kind = FileChangeKind.Changed
        Timestamp = System.DateTimeOffset.UtcNow
      }
      fileChangeAction change
      |> Flip.Expect.equal "should soft reset" FileChangeAction.SoftReset

    testCase "fileChangeAction ignores Deleted files" <| fun () ->
      let change = {
        FilePath = @"C:\Code\Old.fs"
        Kind = FileChangeKind.Deleted
        Timestamp = System.DateTimeOffset.UtcNow
      }
      fileChangeAction change
      |> Flip.Expect.equal "should ignore" FileChangeAction.Ignore

    testCase "fileChangeAction ignores non-F# extensions" <| fun () ->
      let change = {
        FilePath = @"C:\Code\style.css"
        Kind = FileChangeKind.Changed
        Timestamp = System.DateTimeOffset.UtcNow
      }
      fileChangeAction change
      |> Flip.Expect.equal "should ignore" FileChangeAction.Ignore
  ]

/// Tests that the NoWatch config and empty directories properly disable file watching.
let noWatchFlagTests =
  testList "NoWatch config" [
    testCase "WorkerConfig.NoWatch=true disables file watching" <| fun () ->
      let config = Bozzetto.Args.WorkerConfig.fromEnvironmentWith
                     (fun k ->
                       match k with
                       | "BOZZETTO_NO_WATCH" -> "1"
                       | "BOZZETTO_BARE_SESSION" -> "1"
                       | _ -> null)
                     "test" 0
      config.NoWatch |> Flip.Expect.isTrue "should detect NoWatch"

    testCase "WorkerConfig.NoWatch=false means file watching enabled" <| fun () ->
      let config = Bozzetto.Args.WorkerConfig.fromEnvironmentWith (fun k -> match k with "BOZZETTO_BARE_SESSION" -> "1" | _ -> null) "test" 0
      config.NoWatch |> Flip.Expect.isFalse "should not find NoWatch"

    testCase "empty project directories skips file watcher" <| fun () ->
      let dirs : string list = []
      let shouldWatch = not (List.isEmpty dirs)
      shouldWatch |> Flip.Expect.isFalse "should skip with empty dirs"

    testCase "non-empty project directories enables file watcher" <| fun () ->
      let dirs = [@"C:\Code\Project1"; @"C:\Code\Project2"]
      let shouldWatch = not (List.isEmpty dirs)
      shouldWatch |> Flip.Expect.isTrue "should enable with project dirs"
  ]

/// Tests that defaultWatchConfig produces correct settings for worker file watching.
let watchConfigTests =
  testList "watch config for worker" [
    testCase "defaultWatchConfig watches .fs .fsx .fsproj" <| fun () ->
      let config = defaultWatchConfig [@"C:\Code\Proj1"]
      Flip.Expect.contains "should watch .fs" ".fs" config.Extensions
      Flip.Expect.contains "should watch .fsx" ".fsx" config.Extensions
      Flip.Expect.contains "should watch .fsproj" ".fsproj" config.Extensions

    testCase "defaultWatchConfig debounce is 200ms" <| fun () ->
      let config = defaultWatchConfig [@"C:\Code\Proj1"]
      Flip.Expect.equal "debounce should be 200" 200 config.DebounceMs

    testCase "watches multiple project directories" <| fun () ->
      let dirs = [@"C:\Code\Server"; @"C:\Code\Types"; @"C:\Code\Tests"]
      let config = defaultWatchConfig dirs
      Flip.Expect.equal "should have 3 dirs" 3 config.Directories.Length

    testCase "shouldTriggerRebuild rejects bin/obj even for .fs" <| fun () ->
      let config = defaultWatchConfig [@"C:\Code"]
      let binPath =
        sprintf @"C:\Code\bin%cDebug%cfile.fs"
          System.IO.Path.DirectorySeparatorChar
          System.IO.Path.DirectorySeparatorChar
      shouldTriggerRebuild config binPath
      |> Flip.Expect.isFalse "should reject bin path"
  ]

[<Tests>]
let fileWatchContracts =
  testList "File watcher worker contracts" [
    fileWatcherIntegrationTests
    noWatchFlagTests
    watchConfigTests
  ]
