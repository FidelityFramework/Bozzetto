module Bozzetto.Tests.ReleaseVersionTests

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text.Json
open System.Threading.Tasks
open System.Xml.Linq
open Microsoft.AspNetCore.Http
open Expecto
open Expecto.Flip
open Bozzetto

let private declaredRelease () =
  XDocument.Load(Path.Combine(__SOURCE_DIRECTORY__, "..", "Directory.Build.props")).Descendants()
  |> Seq.find (fun element -> element.Name.LocalName = "Version")
  |> fun element -> element.Value.Trim()

[<Tests>]
let tests = testList "Public release version" [
  testCase "product release omits build metadata without changing its semantic version" <| fun _ ->
    ReleaseVersion.format (Some "1.2.3+commit-sha") (Some(System.Version(9, 8, 7, 6)))
    |> Expect.equal "informational release wins over CLR identity" "1.2.3"

  testCase "a deliberate prerelease suffix is preserved" <| fun _ ->
    ReleaseVersion.format (Some "1.2.3-rc.4+commit-sha") None
    |> Expect.equal "only build evidence is omitted from the release label" "1.2.3-rc.4"

  testCase "assembly fallback never exposes the CLR revision as a product release" <| fun _ ->
    ReleaseVersion.format None (Some(System.Version(1, 2, 3, 4))) |> Expect.equal "three release components" "1.2.3"
    ReleaseVersion.format None (Some(System.Version(1, 2))) |> Expect.equal "missing patch is zero" "1.2.0"

  testCase "missing release metadata falls back safely" <| fun _ ->
    for missing in [ None; Some ""; Some " "; Some "+commit-sha" ] do
      ReleaseVersion.format missing (Some(System.Version(1, 2, 3, 4))) |> Expect.equal "fallback release" "1.2.3"
      ReleaseVersion.format missing None |> Expect.equal "absence has no invented release" "unknown"

  testCase "daemon discovery and MCP metadata use the one declared release while assembly evidence remains available" <| fun _ ->
    let declared = declaredRelease ()
    ReleaseVersion.current () |> Expect.equal "compiled release matches centralized build props" declared
    Bozzetto.Server.DaemonInfo.version |> Expect.equal "discovery and UI source use the release" declared
    let server = Bozzetto.Server.McpServer.publicServerInfo ()
    server.Name |> Expect.equal "MCP product identity" "bozzetto"
    server.Version |> Expect.equal "MCP public release" declared
    for assembly in [ typeof<BozzettoError>.Assembly; typeof<BozzettoModel>.Assembly ] do
      let info =
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        |> Option.ofObj |> Option.map _.InformationalVersion
        |> Option.defaultWith (fun () -> failtest "The loaded assembly is missing its informational build evidence.")
      info.Split('+').[0] |> Expect.equal "build metadata still carries the declared release" declared
      assembly.GetName().Version |> Option.ofObj |> Option.map _.ToString() |> Option.defaultValue ""
      |> Expect.stringContains "CLR identity retains its assembly-version shape" (declared.Split('-').[0] + ".")

  testTask "the production version response agrees with public discovery and MCP metadata" {
    do! task {
      let context = DefaultHttpContext()
      use body = new MemoryStream()
      context.Response.Body <- body
      do! Bozzetto.Server.McpServer.writeVersionResponse context
      context.Response.StatusCode |> Expect.equal "existing HTTP version endpoint succeeds" 200
      use document = JsonDocument.Parse(body.ToArray())
      let root = document.RootElement
      root.GetProperty("version").GetString() |> Expect.equal "HTTP and discovery are consistent" Bozzetto.Server.DaemonInfo.version
      root.GetProperty("version").GetString() |> Expect.equal "HTTP and MCP are consistent" (Bozzetto.Server.McpServer.publicServerInfo ()).Version
      root.GetProperty("apiVersion").GetInt32() |> Expect.equal "API compatibility identity is preserved" EndpointContracts.apiVersion
      root.GetProperty("protocolVersion").GetInt32() |> Expect.equal "HTTP protocol identity is preserved" 1
    }
  }

  testTask "CLI version prints the release rather than a four-part CLR stamp" {
    do! task {
      let info = ProcessStartInfo(TestInfrastructure.BozzettoBinary.path ())
      info.ArgumentList.Add "--version"
      info.UseShellExecute <- false
      info.RedirectStandardOutput <- true
      info.RedirectStandardError <- true
      use child = new Process(StartInfo = info)
      child.Start() |> Expect.isTrue "owned CLI child starts"
      let stdout = child.StandardOutput.ReadToEndAsync()
      let stderr = child.StandardError.ReadToEndAsync()
      use cleanup =
        { new IAsyncDisposable with
            member _.DisposeAsync() = ValueTask(task {
              if not child.HasExited then child.Kill(true)
              do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 5.)
              let! _ = Task.WhenAll(stdout, stderr)
              return ()
            }) }
      do! child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 10.)
      let! output = stdout
      let! errors = stderr
      child.ExitCode |> Expect.equal "CLI version has no runtime failure" 0
      errors |> Expect.equal "version inspection produces no diagnostic error" ""
      output.Trim() |> Expect.equal "CLI product release agrees with every public endpoint" ("Bozzetto version " + ReleaseVersion.current ())
    }
  }
]
