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

  let create project directory : IProjectBackend =
    let options =
      { ProjectPath = project; OutputPath = None; ArtifactsDirectory = None
        TargetTriple = None; NativeLink = Core.Types.Pipeline.NativeLinkOptions.Empty
        KeepIntermediates = false; PruneIntermediates = false
        EmitMLIROnly = false; EmitLLVMOnly = false
        Verbose = false; ShowTiming = false; TreatWarningsAsErrors = false; Deploy = false }
    let session = new ProjectSession(options, directory)
    { new IProjectBackend with
        member _.Reserve label = box (session.Reserve label)
        member _.Current = session.Current |> Option.map accepted
        member _.ManifestPath = session.ManifestPath
        member _.BuildAsync(ticket, cancellation) = task {
          let! result = session.BuildAsync(unbox<Core.IncrementalBuild.Ticket> ticket, cancellation)
          return Result.map accepted result
        }
        member _.RunCurrentAsync(arguments, cancellation) = task {
          let! result = session.RunCurrentAsync(arguments, cancellation)
          return result |> Result.map (fun value ->
            { Generation = value.Generation; SourceVersion = value.SourceVersion
              ExitCode = value.ExitCode; StandardOutput = value.StandardOutput
              StandardError = value.StandardError })
        }
        member _.Dispose() = (session :> System.IDisposable).Dispose() }
