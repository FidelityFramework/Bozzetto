namespace Bozzetto

open System
open System.Reflection

/// Product release labels are independent of CLR identity and build evidence.
/// Directory.Build.props supplies the release; the SDK may append +revision
/// to informational metadata, which remains available for diagnostics.
module ReleaseVersion =
  type private AssemblyMarker = class end

  let internal format (informational: string option) (assemblyVersion: Version option) =
    let release =
      informational
      |> Option.filter (String.IsNullOrWhiteSpace >> not)
      |> Option.map (fun value -> value.Split('+').[0].Trim())
      |> Option.filter (String.IsNullOrWhiteSpace >> not)
    match release, assemblyVersion with
    | Some value, _ -> value
    | None, Some value -> sprintf "%d.%d.%d" value.Major value.Minor (max value.Build 0)
    | None, None -> "unknown"

  let private fromAssembly (assembly: Assembly) =
    let informational =
      assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
      |> Option.ofObj
      |> Option.map _.InformationalVersion
    format informational (assembly.GetName().Version |> Option.ofObj)

  let current () = fromAssembly typeof<AssemblyMarker>.Assembly
