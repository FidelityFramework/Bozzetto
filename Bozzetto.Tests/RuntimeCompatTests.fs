module Bozzetto.Tests.RuntimeCompatTests

open System
open Expecto
open Expecto.Flip
open Bozzetto.RuntimeCompat

let private net10 =
  """{"runtimeOptions":{"tfm":"net10.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0-rc.1.26425.128"},{"name":"Microsoft.AspNetCore.App","version":"10.0.0-rc.1.26425.128"}]}}"""

let private net9 =
  """{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}"""

let private frameworkDirs = [ "9.0.12"; "10.0.0-rc.1.26425.128"; "9.0.4" ]

/// Map an arbitrary byte onto a runtime major in 8..13.
let private majorOf (raw: byte) = 8 + int raw % 6

[<Tests>]
let tests =
  testList "RuntimeCompat" [
    testList "parseRuntimeRequirement" [
      testCase "reads the highest major across an ASP.NET project's frameworks and flags the preview" <| fun _ ->
        parseRuntimeRequirement net10
        |> Expect.equal "net10 rc" (Result.Ok { Major = 10; Stability = Prerelease })

      testCase "reads a single-framework stable runtimeconfig" <| fun _ ->
        parseRuntimeRequirement net9
        |> Expect.equal "net9" (Result.Ok { Major = 9; Stability = Stable })

      testCase "malformed json is a typed Error, never an exception" <| fun _ ->
        match parseRuntimeRequirement "{nope" with
        | Result.Error(NotJsonRuntimeConfig _) -> ()
        | other -> failtestf "expected NotJsonRuntimeConfig, got %A" other

      testCase "a runtimeconfig that names no framework is NoFrameworkVersion" <| fun _ ->
        parseRuntimeRequirement "{}" |> Expect.equal "no framework" (Result.Error NoFrameworkVersion)

      testCase "an unparseable framework version is named in the error" <| fun _ ->
        parseRuntimeRequirement """{"runtimeOptions":{"framework":{"name":"x","version":"banana"}}}"""
        |> Expect.equal "named" (Result.Error(UnrecognisedFrameworkVersion "banana"))
    ]

    testList "decide" [
      testCase "rolls forward when the project needs a newer runtime that is installed" <| fun _ ->
        decide 9 [ 9; 10 ] (parseRuntimeRequirement net10)
        |> Expect.equal "roll" (RollForward(10, Prerelease))

      testCase "reports the missing runtime when none new enough is installed" <| fun _ ->
        decide 9 [ 9 ] (parseRuntimeRequirement net10)
        |> Expect.equal "missing" (RuntimeMissing 10)

      testCase "leaves a project the host already satisfies alone" <| fun _ ->
        decide 9 [ 9; 10 ] (parseRuntimeRequirement net9)
        |> Expect.equal "fits" HostFits

      testCase "an unreadable requirement keeps the default launch and carries the reason" <| fun _ ->
        decide 9 [ 9 ] (Error(ProjectNotBuilt "Sample.fsproj"))
        |> Expect.equal "unknown" (Unknown(ProjectNotBuilt "Sample.fsproj"))

      testProperty "rolls forward only for a strictly newer requirement, and only then sets the environment"
      <| fun (hostRaw: byte) (installedRaw: byte list) (requiredRaw: byte) (isPreview: bool) (readable: bool) ->
        let host = majorOf hostRaw
        let installed = installedRaw |> List.map majorOf
        let stability = if isPreview then Prerelease else Stable
        let requirement =
          if readable then Result.Ok { Major = majorOf requiredRaw; Stability = stability } else Error NoFrameworkVersion
        let choice = decide host installed requirement
        let sets = not (List.isEmpty (rollForwardEnv choice))
        match choice, requirement with
        | RollForward(major, _), Result.Ok required -> sets && major = required.Major && required.Major > host
        | RollForward _, Error _ -> false
        | _ -> not sets
    ]

    testList "rollForwardEnv" [
      testCase "a preview requirement also allows prerelease runtimes" <| fun _ ->
        rollForwardEnv (RollForward(10, Prerelease))
        |> Expect.equal "env" [ "DOTNET_ROLL_FORWARD", "LatestMajor"; "DOTNET_ROLL_FORWARD_TO_PRERELEASE", "1" ]

      testCase "a stable requirement does not allow prerelease runtimes" <| fun _ ->
        rollForwardEnv (RollForward(10, Stable))
        |> Expect.equal "env" [ "DOTNET_ROLL_FORWARD", "LatestMajor" ]
    ]

    testList "describe" [
      testCase "a missing runtime names what to install and where" <| fun _ ->
        let text = describe (RuntimeMissing 10)
        Expect.stringContains "names the major" "10" text
        Expect.stringContains "says where to get it" "dotnet.microsoft.com/download" text
    ]

    testList "selectFrameworkDir" [
      testCase "a net9 worker gets the net9 directory even though net10 is installed" <| fun _ ->
        selectFrameworkDir (System.Version(9, 0, 12)) frameworkDirs |> Expect.equal "9" (Result.Ok "9.0.12")

      testCase "a worker rolled forward to net10 gets the net10 preview directory" <| fun _ ->
        selectFrameworkDir (System.Version(10, 0, 0)) frameworkDirs |> Expect.equal "10" (Result.Ok "10.0.0-rc.1.26425.128")

      testCase "a different patch of the same major falls back to the highest of that major" <| fun _ ->
        selectFrameworkDir (System.Version(9, 0, 3)) frameworkDirs |> Expect.equal "same major" (Result.Ok "9.0.12")

      testCase "no directory of the running major is an Error, not a guess from another major" <| fun _ ->
        selectFrameworkDir (System.Version(8, 0, 1)) frameworkDirs
        |> Expect.equal "typed error carries the major and what was available" (Result.Error(NoFrameworkForMajor(8, frameworkDirs)))

      testProperty "never returns a directory of a different major than the running runtime"
      <| fun (majorRaw: byte) (minor: byte) (patch: byte) ->
        let major = majorOf majorRaw
        match selectFrameworkDir (System.Version(major, int minor, int patch)) frameworkDirs with
        | Result.Ok name -> System.Version.Parse(name.Split('-').[0]).Major = major
        | Error _ -> true
    ]

    testList "SessionKinds (isolation is unconditional)" [
      testCase "the test constructor pins the in-process reference implementation, so tests never depend on the environment" <| fun _ ->
        let args = Bozzetto.ActorCreation.mkCommonActorArgs (Bozzetto.Utils.Log.asILogger ()) false ignore Bozzetto.Args.ProjectLoadConfig.empty
        Expect.equal "in-process" Bozzetto.SessionKinds.InProcess args.FsiKind
    ]
  ]
