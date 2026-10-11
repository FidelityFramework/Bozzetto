// Run one command under a work lease, through the same client ci-pipeline.fsx uses:
//
//   dotnet fsi tools/build/lease.fsx full_build dotnet build
//   dotnet fsi tools/build/lease.fsx test_suite_run dotnet <testDll> --summary
//
// The holder is "BOZZETTO_AGENT · BOZZETTO_PROJECT" (default: the current directory). A deferred
// ask waits in line as the same ask; the lease renews while the command runs; the command's
// process tree is killed if the daemon reports the lease gone; the lease is released at the end.
// Exit status: the command's own, 64 for bad usage, 69 when the lease is refused or the ask is
// withdrawn after ten minutes in line.
#load "WorkLease.fs"

open System
open System.Diagnostics
open Bozzetto.Build

match fsi.CommandLineArgs |> Array.toList |> List.tail with
| kind :: command :: args when kind = "full_build" || kind = "test_suite_run" ->
  let exitCode = ref 0
  let work (gone: Threading.CancellationToken) =
    async {
      let start = ProcessStartInfo(command)
      args |> List.iter start.ArgumentList.Add
      use child = Process.Start start
      use _ = gone.Register(fun () -> try child.Kill(entireProcessTree = true) with _ -> ())
      do! child.WaitForExitAsync() |> Async.AwaitTask
      exitCode.Value <- child.ExitCode
      return if child.ExitCode = 0 then Ok() else Error $"{command} exited {child.ExitCode}"
    }
  match WorkLease.run kind (WorkLease.holder Environment.CurrentDirectory) work |> Async.RunSynchronously with
  | Ok() -> exit 0
  | Error _ when exitCode.Value <> 0 -> exit exitCode.Value
  | Error reason ->
    eprintfn "%s" reason
    exit 69
| _ ->
  eprintfn "usage: dotnet fsi tools/build/lease.fsx <full_build|test_suite_run> <command> [args...]"
  exit 64
