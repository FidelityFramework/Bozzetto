module Bozzetto.Tests.BuildPreflightBoundaryTests

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.WorkerProtocol
open Bozzetto.Server.DaemonMode

let private refused<'T> () : Result<'T, BozzettoError> =
  Error (BozzettoError.SessionCreationFailed ExternalFSharpService.message)

let private makeOps () =
  createSessionOpsWithRecovery
    Unchecked.defaultof<_>
    (fun () -> failwith "retired creation must not read a session snapshot")
    Unchecked.defaultof<_>
    (Some(fun _ _ -> failwith "retired creation must not recover or rebuild a project"))

[<Tests>]
let tests =
  testSequenced <| testList "External FSharp service boundary" [
    for target in
      [ SessionProjectTarget.Bare
        SessionProjectTarget.Project "/missing/Project.fsproj"
        SessionProjectTarget.Solution "/missing/Workspace.slnx" ] do
      testTask (sprintf "create %A refuses before config, recovery, mailbox or manifest work" target) {
        let! result = (makeOps ()).CreateSession [ target ] "/missing/working-directory" WorkflowTypes.SessionWorkflow.Interactive
        result |> Expect.equal "the external provider owns FSharp execution" (refused ())
      }

    testTask "restart refuses before inspecting or stopping the previous session" {
      let! result = (makeOps ()).RestartSession (SessionId.newId()) true
      result |> Expect.equal "restart cannot invoke build recovery or a worker" (refused ())
    }

    testTask "Elm creation and restart cannot bypass the provider refusal" {
      let deps =
        Bozzetto.ElmDaemon.createEffectDeps
          Unchecked.defaultof<_>
          (fun () -> failwith "retired Elm operation must not read a snapshot")
          (fun _ -> failwith "retired Elm operation must not evaluate configuration")
          (fun _ -> failwith "retired Elm operation must not write configuration")
      let! created =
        deps.CreateSession [ SessionProjectTarget.Bare ] "/missing" WorkflowTypes.SessionWorkflow.Interactive
        |> Async.StartAsTask
      created |> Expect.equal "Elm create refuses" (refused ())
      let! restarted = deps.RestartSession (SessionId.newId()) true |> Async.StartAsTask
      restarted |> Expect.equal "Elm restart refuses" (refused ())
    }

    testCase "raw worker creation refuses before resolving paths or starting a process" <| fun () ->
      match SessionManager.startWorkerProcess (SessionId.newId()) [ SessionProjectTarget.Bare ] null false WorkflowTypes.SessionWorkflow.Interactive (fun _ _ -> failwith "no worker can exit") with
      | Error (BozzettoError.WorkerSpawnFailed message) ->
        message |> Expect.equal "the worker boundary reports the retired FSharp hosting refusal" ExternalFSharpService.message
      | other -> failtestf "expected the production worker refusal, got %A" other

    testTask "production build delegate refuses without examining projects or a working directory" {
      let! result =
        SessionManager.defaultRuntime.RunBuildAsync Unchecked.defaultof<_> null
        |> Async.StartAsTask
      result |> Expect.equal "raw rebuild cannot recreate FSharp hosting" (refused ())
    }

    testTask "startup resume preserves the old manifest and never calls session operations" {
      let infra : DaemonInfra =
        { Log = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
          LoggerFactory = Unchecked.defaultof<_>
          HttpClient = Unchecked.defaultof<_>
          FrictionStore = None
          DaemonStreamId = "retirement-test"
          Cts = Unchecked.defaultof<_>
          StateChangedEvent = Unchecked.defaultof<_>
          McpFetchTimeoutSec = 1.0 }
      do! resumePreviousSessions infra Unchecked.defaultof<_> Unchecked.defaultof<_> null (fun () -> failwith "retired sessions must not resume")
    }

    testTask "cohort integration refuses before changing its binding or provisioning a worktree" {
      let previous = McpCohortIntegration.cohortIntegrationRef.Value
      let expected : McpCohortIntegration.CohortIntegrationBinding =
        { WorktreePath = "/preserved/worktree"
          Branch = "preserved-branch"
          Session = McpCohortIntegration.IntegrationSession.Pending }
      try
        McpCohortIntegration.cohortIntegrationRef.Value <- Some expected
        let! result = McpCohortIntegration.setIntegrationRef Unchecked.defaultof<_> "test" "HEAD"
        result |> Expect.equal "FSharp integration provisioning is refused before accessing context" (refused ())
        McpCohortIntegration.cohortIntegrationRef.Value
        |> Expect.equal "the existing binding remains untouched" (Some expected)
      finally
        McpCohortIntegration.cohortIntegrationRef.Value <- previous
    }
  ]

[<Tests>]
let cliEntryTests =
  testList "Retired FSharp CLI entry point" [
    testTask "Jupyter exits 2 with the provider refusal before inspecting a missing connection file" {
      return! task {
        let output = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
        let framework = Path.GetFileName output
        let configuration = Directory.GetParent(output).Name
        let cli = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Bozzetto", "bin", configuration, framework, "Bozzetto.dll"))
        File.Exists cli |> Expect.isTrue (sprintf "the CLI output for this test configuration must exist: %s" cli)
        let connectionFile = Path.Combine(output, sprintf "absent-jupyter-%s.json" (Guid.NewGuid().ToString("N")))
        File.Exists connectionFile |> Expect.isFalse "the connection file deliberately does not exist"
        let start = ProcessStartInfo("dotnet")
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        start.ArgumentList.Add cli
        start.ArgumentList.Add "--jupyter"
        start.ArgumentList.Add connectionFile
        use proc = Process.Start start
        use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
        let stdout = proc.StandardOutput.ReadToEndAsync(deadline.Token)
        let stderr = proc.StandardError.ReadToEndAsync(deadline.Token)
        try
          do! proc.WaitForExitAsync(deadline.Token)
          let! (out: string) = stdout
          let! (error: string) = stderr
          proc.ExitCode |> Expect.equal "retired Jupyter execution is refused" 2
          error.Trim() |> Expect.equal "one actionable refusal" ExternalFSharpService.message
          out |> Expect.equal "the retired entry point cannot start a kernel or daemon" ""
          File.Exists connectionFile |> Expect.isFalse "the retired entry point preserves the absent connection file"
        finally
          if not proc.HasExited then proc.Kill(true)
      }
    }
  ]
