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
#load "../../src/bozzetto-web/src/Shared/Protocol.fs"
#load "../../src/bozzetto-web/src/Shared/BinaryCodec.fs"
#load "WorkLease.fs"

open System
open System.Diagnostics
open System.IO
open Bozzetto.Build
open Bozzetto.Web.Shared
open Bozzetto.Web.Shared.Protocol

let reportCommand phase observation =
  async {
    try
      let bytes = BinaryCodec.encodeCommandObservationRequest observation

      match! WorkLease.observeCommand CommandObservationMediaType phase bytes with
      | Error reason -> return Error reason
      | Ok(status, bytes) ->
        match BinaryCodec.decodeCommandObservationReply bytes with
        | Ok(CommandObservationAccepted _) when status = 200 -> return Ok()
        | Ok(CommandObservationRefused reason) -> return Error reason
        | _ -> return Error $"command/{phase}: HTTP {status}; invalid binary acknowledgement"
    with error ->
      return Error $"command/{phase}: {error.Message}"
  }

let executablePath (command: string) =
  if Path.IsPathRooted command || command.Contains(Path.DirectorySeparatorChar) then
    Path.GetFullPath command
  else
    (Environment.GetEnvironmentVariable "PATH"
     |> Option.ofObj
     |> Option.defaultValue "")
      .Split(Path.PathSeparator)
    |> Array.map (fun directory -> Path.GetFullPath(Path.Combine(directory, command)))
    |> Array.tryFind File.Exists
    |> Option.defaultWith (fun () -> invalidOp $"Executable not found on PATH: {command}")

// Tee each stream to the caller while retaining only the journal's bounded tail.
// Reading starts before waiting for the process, so neither pipe can deadlock it.
let rec drain (reader: StreamReader) (writer: TextWriter) (tail: string) truncated =
  async {
    let buffer = Array.zeroCreate<char> 4096
    let! count = reader.ReadAsync(buffer, 0, buffer.Length) |> Async.AwaitTask

    if count = 0 then
      return tail, truncated
    else
      let text = String(buffer, 0, count)
      do! writer.WriteAsync text |> Async.AwaitTask
      let combined = tail + text

      let retained =
        if combined.Length > 16384 then
          "[cut: earlier command output omitted]\n"
          + combined.Substring(combined.Length - 16384)
        else
          combined

      return! drain reader writer retained (truncated || combined.Length > 16384)
  }

match fsi.CommandLineArgs |> Array.toList |> List.tail with
| kind :: command :: args when kind = "full_build" || kind = "test_suite_run" ->
  let command = executablePath command
  let exitCode = ref 0

  let project =
    match Environment.GetEnvironmentVariable "BOZZETTO_PROJECT" with
    | null
    | "" -> Environment.CurrentDirectory
    | path -> Path.GetFullPath path

  let holder = WorkLease.holder project

  let work leaseId (gone: Threading.CancellationToken) =
    async {
      let start = ProcessStartInfo(command)
      start.UseShellExecute <- false
      start.RedirectStandardOutput <- true
      start.RedirectStandardError <- true
      args |> List.iter start.ArgumentList.Add
      let started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
      use child = Process.Start start

      use _ =
        gone.Register(fun () ->
          try
            child.Kill(entireProcessTree = true)
          with _ ->
            ())

      let! stdout = Async.StartChild(drain child.StandardOutput Console.Out "" false)
      let! stderr = Async.StartChild(drain child.StandardError Console.Error "" false)

      let observed: CommandObservationRequest =
        {
          Holder = holder
          Project = project
          Kind = kind
          LeaseId = leaseId
          RunId = Guid.NewGuid().ToString("N")
          Executable = command
          Arguments = List.toArray args
          ProcessId = child.Id
          StartedAtMs = started
          Observation = CommandStarted
        }

      let! announced = reportCommand "start" observed

      match announced with
      | Error reason ->
        eprintfn "work-lease: command observation refused: %s" reason

        try
          child.Kill(entireProcessTree = true)
        with _ ->
          ()
      | Ok() -> ()

      do! child.WaitForExitAsync() |> Async.AwaitTask
      exitCode.Value <- child.ExitCode
      let! (output, outputTruncated) = stdout
      let! (error, errorTruncated) = stderr

      let result =
        {
          FinishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
          ExitCode = child.ExitCode
          StandardOutput = output
          StandardError = error
          OutputTruncated = outputTruncated
          ErrorTruncated = errorTruncated
        }

      let! settled =
        reportCommand
          "finish"
          { observed with
              Observation = CommandFinished result
          }
      // Settlement precedes lease release, after the process and both streams join.
      // A reporting failure remains visible even when the command itself succeeded.
      match announced, settled with
      | _, Error reason ->
        eprintfn "work-lease: command result was not recorded: %s" reason
        return Error reason
      | Error reason, _ -> return Error reason
      | Ok(), Ok() ->
        return
          if child.ExitCode = 0 then
            Ok()
          else
            Error $"{command} exited {child.ExitCode}"
    }

  match
    WorkLease.runWithCancellation kind holder Threading.CancellationToken.None work
    |> Async.RunSynchronously
  with
  | Ok() -> exit 0
  | Error _ when exitCode.Value <> 0 -> exit exitCode.Value
  | Error reason ->
    eprintfn "%s" reason
    exit 69
| _ ->
  eprintfn "usage: dotnet fsi tools/build/lease.fsx <full_build|test_suite_run> <command> [args...]"
  exit 64
