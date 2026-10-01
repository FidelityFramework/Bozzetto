module Bozzetto.Tests.SessionCreationTests

open System
open System.IO
open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.Tests.TestInfrastructure
open Bozzetto.Server.Dashboard
open Bozzetto.Server.DashboardTypes

/// Helper: write text to a file with explicit types.
let writeText (path: string) (content: string) =
  File.WriteAllText(path, content)

/// Create a temp directory, run setup + test, then clean up.
let withTempDir (setup: string -> unit) (test: string -> unit) =
  let dir =
    Path.Combine(
      Path.GetTempPath(),
      sprintf "bozzetto-test-%s" (Guid.NewGuid().ToString("N").[..7]))
  try
    Directory.CreateDirectory(dir) |> ignore
    setup dir
    test dir
  finally
    if Directory.Exists dir then
      Directory.Delete(dir, true)

let addFakeProject dir name =
  writeText (Path.Combine(dir, name)) "<Project />"

let addConfig dir content =
  let configDir = Path.Combine(dir, ".bozzetto")
  Directory.CreateDirectory(configDir) |> ignore
  writeText (Path.Combine(configDir, "config.fsx")) content

let addSolution dir name =
  writeText (Path.Combine(dir, name)) ""

/// Unwrap the Ok list from resolveSessionProjects, failing the test on Error.
let okProjects (msg: string) (r: Result<string list, BozzettoError>) : string list =
  match r with
  | Ok ps -> ps
  | Error e -> failtestf "%s: expected Ok, got Error %A" msg e

[<Tests>]
let tests = testSequenced <| testList "Session Creation" [

  testList "resolveSessionProjects" [

    testCase "legacy NoLoad script is preserved but no longer controls discovery" (fun _ ->
      withTempDir
        (fun dir ->
          addFakeProject dir "Fake.fsproj"
          addConfig dir "{ DirectoryConfig.empty with Load = NoLoad }")
        (fun dir ->
          resolveSessionProjects dir ""
          |> okProjects "retired config"
          |> Expect.isNonEmpty "discovery is independent of FSharp script evaluation"))

    testCase "auto-discovers when no config exists" <| fun _ ->
      withTempDir
        (fun dir -> addFakeProject dir "Fake.fsproj")
        (fun dir ->
          resolveSessionProjects dir ""
          |> okProjects "no config"
          |> Expect.isNonEmpty "should auto-discover when no config file")

    testCase "legacy project selection is not evaluated during filesystem discovery" (fun _ ->
      withTempDir
        (fun dir ->
          addFakeProject dir "Fake.fsproj"
          addFakeProject dir "Other.fsproj"
          addConfig dir "{ DirectoryConfig.empty with Load = Projects [\"Other.fsproj\"] }")
        (fun dir ->
          let result = resolveSessionProjects dir "" |> okProjects "retired config Projects"
          result |> Expect.hasLength "both filesystem projects are discovered" 2))

    testCase "returns empty for empty directory" <| fun _ ->
      withTempDir
        (fun _ -> ())
        (fun dir ->
          resolveSessionProjects dir ""
          |> Expect.equal "should return empty for empty directory" (Ok []))

    testCase "prefers manual over config" <| fun _ ->
      withTempDir
        (fun dir ->
          addFakeProject dir "Fake.fsproj"
          addFakeProject dir "Manual.fsproj"
          addConfig dir """{ DirectoryConfig.empty with Load = Projects ["Fake.fsproj"] }""")
        (fun dir ->
          let result = resolveSessionProjects dir "Manual.fsproj" |> okProjects "manual over config"
          result |> Expect.hasLength "should use manual project" 1
          result.[0]
          |> Expect.stringContains "should be manual project" "Manual.fsproj")

    testCase "prefers solution over project" <| fun _ ->
      withTempDir
        (fun dir ->
          addFakeProject dir "Fake.fsproj"
          addSolution dir "Fake.sln")
        (fun dir ->
          let result = resolveSessionProjects dir "" |> okProjects "solution over project"
          result |> Expect.hasLength "should find one solution" 1
          result.[0]
          |> Expect.stringContains "should prefer solution" "Fake.sln")

    testCase "solution discovery works without evaluating a legacy config" (fun _ ->
      withTempDir
        (fun dir ->
          addSolution dir "MyApp.sln"
          addConfig dir """{ DirectoryConfig.empty with Load = Solution "MyApp.sln" }""")
        (fun dir ->
          let result = resolveSessionProjects dir "" |> okProjects "config solution"
          result |> Expect.hasLength "should find one solution" 1
          result.[0]
          |> Expect.stringContains "should use config solution" "MyApp.sln"))

    testCase "REJECTS manual projects outside the working directory (not silently dropped)" <| fun _ ->
      withTempDir
        (fun dir -> addFakeProject dir "Inside.fsproj")
        (fun dir ->
          // A rooted project path elsewhere on disk must be REFUSED loudly — a
          // dashboard peer cannot point the daemon at arbitrary projects, and a
          // caller who names N projects must never get a session quietly missing
          // one (roast-8 §4: unify with validateSessionCreateRequest).
          let outside =
            Path.Combine(Path.GetTempPath(), sprintf "bozzetto-outside-%s.fsproj" (Guid.NewGuid().ToString("N").[..7]))
          try
            writeText outside "<Project />"
            match resolveSessionProjects dir outside with
            | Error (BozzettoError.UnsafeSessionPath(p, _)) ->
              p |> Expect.equal "error must name the escaping path" outside
            | other ->
              failtestf "escaping manual project must be rejected, got %A" other
          finally
            if File.Exists outside then File.Delete outside)

    testCase "REJECTS on the first escaping project even when a valid one is also named" <| fun _ ->
      withTempDir
        (fun dir -> addFakeProject dir "Inside.fsproj")
        (fun dir ->
          let inside = Path.Combine(dir, "Inside.fsproj")
          let outside =
            Path.Combine(Path.GetTempPath(), sprintf "bozzetto-outside-%s.fsproj" (Guid.NewGuid().ToString("N").[..7]))
          try
            writeText outside "<Project />"
            match resolveSessionProjects dir (inside + "," + outside) with
            | Error (BozzettoError.UnsafeSessionPath(p, _)) ->
              p |> Expect.equal "error names the escaping path, not the valid one" outside
            | other ->
              failtestf "a mix with one escaping project must be rejected, got %A" other
          finally
            if File.Exists outside then File.Delete outside)

    testCase "keeps rooted manual projects inside the working directory" <| fun _ ->
      withTempDir
        (fun dir -> addFakeProject dir "Inside.fsproj")
        (fun dir ->
          let rootedInside = Path.Combine(dir, "Inside.fsproj")
          let result = resolveSessionProjects dir rootedInside |> okProjects "rooted inside"
          result |> Expect.hasLength "rooted manual project inside the working dir must be kept" 1
          result.[0]
          |> Expect.stringContains "should be the inside project" "Inside.fsproj")
  ]

  testList "DirectoryConfig.autoOpenNamespacesForDirectory compatibility default" [

    testCase "retired config is not applied to the compatibility default" <| fun _ ->
      withTempDir
        (fun dir ->
          addConfig dir """{ DirectoryConfig.empty with AutoOpenNamespaces = false }""")
        (fun dir ->
          DirectoryConfig.autoOpenNamespacesForDirectory dir
          |> Expect.isTrue "retired FSharp configuration is not evaluated")

    testCase "defaults to true when no config exists" <| fun _ ->
      withTempDir
        (fun _ -> ())
        (fun dir ->
          DirectoryConfig.autoOpenNamespacesForDirectory dir
          |> Expect.isTrue "should default to true")
  ]
]
