namespace Bozzetto.Composer

open Bozzetto.Providers
open Core.CompilationOrchestrator

module ComposerAdapter =
  let private accepted (value: Core.IncrementalBuild.Accepted) : AcceptedArtifact =
    { Generation = value.Generation; SourceVersion = value.SourceVersion
      ArtifactPath = value.ArtifactPath; ArtifactSha256 = value.ArtifactSha256
      ObjectManifest = value.ObjectManifest
      ChangedWitnesses = List.toArray value.Witnesses.Changed
      RetainedWitnesses = List.toArray value.Witnesses.Retained
      RetiredWitnesses = List.toArray value.Witnesses.Retired
      WitnessVisits = Map.toArray value.Witnesses.WitnessedNodes
      CompiledObjects = List.toArray value.CompiledObjects
      ReusedObjects = List.toArray value.ReusedObjects
      RetiredObjects = List.toArray value.RetiredObjects }

  let create (tools: global.Composer.Hosting.ToolOwner) project directory : IProjectBackend<Core.IncrementalBuild.Ticket> =
    let options =
      { ProjectPath = project; OutputPath = None; ArtifactsDirectory = None
        TargetTriple = None; NativeLink = Core.Types.Pipeline.NativeLinkOptions.Empty
        KeepIntermediates = false; PruneIntermediates = false
        EmitMLIROnly = false; EmitLLVMOnly = false
        Verbose = false; ShowTiming = false; TreatWarningsAsErrors = false; Deploy = false }
    let session = new ProjectSession(options, directory, tools, ?capture = DiagnosticCapture.fromEnvironment ())
    { new IProjectBackend<Core.IncrementalBuild.Ticket> with
        member _.Reserve label = session.Reserve label
        member _.Current = session.Current |> Option.map accepted
        member _.ManifestPath = session.ManifestPath
        member _.BuildAsync(ticket, cancellation) = task {
          let! result = session.BuildAsync(ticket, cancellation)
          return Result.map accepted result
        }
        member _.RunCurrentAsync(arguments, cancellation) = task {
          let! result = session.RunCurrentAsync(arguments, cancellation)
          return result |> Result.map (fun value ->
            { Generation = value.Generation; SourceVersion = value.SourceVersion
              ExitCode = value.ExitCode; StandardOutput = value.StandardOutput
              StandardError = value.StandardError })
        }
        member _.Dispose() =
          try (session :> System.IDisposable).Dispose()
          finally
            // Close has joined CCS, so this final drain includes failures from
            // withdrawn checks. Reporting must not mask the close exception.
            let report (message: string) =
              try System.Console.Error.WriteLine message
              with _ -> ()
            try
              for diagnostic in session.DrainDiagnostics() do
                report (sprintf "CCS attempt %A: %s: %s" diagnostic.Attempt diagnostic.Failure.Code diagnostic.Failure.Message)
            with error -> report ("CCS diagnostic drain failed: " + error.Message) }
