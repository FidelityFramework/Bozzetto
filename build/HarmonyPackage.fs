module Bozzetto.Build.HarmonyPackage

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Xml.Linq

let packageId = "Bozzetto.Harmony"
let upstreamBase = "ffb6e9cabd1b83d4a51ef02fcaa914bf1269e51d"

let private require condition message =
  if not condition then invalidOp message

let private absoluteDirectory name (path: string) =
  require (Path.IsPathFullyQualified path) (name + " must be an absolute path")
  require (Directory.Exists path) (name + " does not exist: " + path)
  Path.GetFullPath path

let private sha256 (bytes: byte[]) =
  Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

let expectedVersion repoRoot =
  let root = absoluteDirectory "Repository" repoRoot
  let versions =
    XDocument.Load(Path.Combine(root, "Directory.Packages.props")).Descendants()
    |> Seq.filter (fun node ->
      node.Name.LocalName = "PackageVersion"
      && string (node.Attribute(XName.Get "Include")) <> ""
      && node.Attribute(XName.Get "Include").Value = packageId)
    |> Seq.toArray
  require (versions.Length = 1) "Directory.Packages.props must contain exactly one Bozzetto.Harmony version"
  let version = versions[0].Attribute(XName.Get "Version")
  require (not (isNull version) && not (String.IsNullOrWhiteSpace version.Value)) "Bozzetto.Harmony version is missing"
  version.Value

let private inspectPackage (bytes: byte[]) (version: string) =
  use stream = new MemoryStream(bytes, false)
  use archive = new ZipArchive(stream, ZipArchiveMode.Read)
  let names = archive.Entries |> Seq.map (fun entry -> entry.FullName) |> Seq.toArray
  require ((names |> Array.distinct).Length = names.Length) "Harmony package contains duplicate ZIP entries"
  let nuspecs = archive.Entries |> Seq.filter (fun entry -> entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)) |> Seq.toArray
  require (nuspecs.Length = 1) "Harmony package must contain exactly one nuspec"
  use nuspecStream = nuspecs[0].Open()
  let nuspec = XDocument.Load nuspecStream
  let metadata = nuspec.Root.Elements() |> Seq.find (fun node -> node.Name.LocalName = "metadata")
  let field name =
    metadata.Elements() |> Seq.find (fun node -> node.Name.LocalName = name) |> fun node -> node.Value
  require (field "id" = packageId) ("Expected package identity " + packageId)
  require (field "version" = version) ("Expected package version " + version)
  let repositories = metadata.Elements() |> Seq.filter (fun node -> node.Name.LocalName = "repository") |> Seq.toArray
  require (repositories.Length = 1) "Harmony package must identify its source repository commit"
  let attribute name =
    let value = repositories[0].Attribute(XName.Get name)
    if isNull value then "" else value.Value
  let sourceCommit = attribute "commit"
  require (attribute "type" = "git" && sourceCommit.Length = 40 && (sourceCommit |> Seq.forall Uri.IsHexDigit))
    "Harmony package must contain a full Git source commit in its nuspec repository metadata"
  require (names |> Array.forall (fun name -> not (name.EndsWith("/0Harmony.dll", StringComparison.OrdinalIgnoreCase)))) "Package still contains upstream 0Harmony.dll"
  for framework in [ "net8.0"; "net10.0" ] do
    let name = "lib/" + framework + "/" + packageId + ".dll"
    let entry = archive.GetEntry name
    require (not (isNull entry)) ("Missing package assembly " + name)
    use input = entry.Open()
    use assemblyStream = new MemoryStream()
    input.CopyTo assemblyStream
    assemblyStream.Position <- 0L
    use reader = new PEReader(assemblyStream)
    require reader.HasMetadata (name + " has no CLR metadata")
    let metadataReader = reader.GetMetadataReader()
    require metadataReader.IsAssembly (name + " is not a CLR assembly")
    let definition = metadataReader.GetAssemblyDefinition()
    let identity = metadataReader.GetString definition.Name
    require (identity = packageId) (name + " has wrong CLR assembly identity: " + identity)
  sourceCommit

let validate repoRoot : Result<unit, string> =
  try
    let version = expectedVersion repoRoot
    let directory = Path.Combine(repoRoot, "vendor", packageId)
    use manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "package.json")))
    let root = manifest.RootElement
    let text name = root.GetProperty(name: string).GetString()
    require (root.GetProperty("schemaVersion").GetInt32() = 1) "Unsupported Harmony package manifest schema"
    require (text "id" = packageId && text "version" = version) "Harmony manifest identity/version disagrees with central package pin"
    let filename = packageId + "." + version + ".nupkg"
    require (text "file" = filename) "Unexpected Harmony package filename in manifest"
    let bytes = File.ReadAllBytes(Path.Combine(directory, filename))
    require (sha256 bytes = text "sha256") "Harmony package SHA256 differs from committed manifest; import the reviewed source build explicitly"
    let sourceCommit = inspectPackage bytes version
    let source = root.GetProperty "source"
    require (source.GetProperty("commit").GetString() = sourceCommit)
      "Harmony manifest source commit disagrees with the package's embedded repository commit"
    require (source.GetProperty("upstreamBase").GetString() = upstreamBase)
      "Harmony manifest has an unexpected upstream base"
    require (not (source.GetProperty("dirty").GetBoolean()))
      "Dirty Harmony source attribution requires a hash-bound build attestation; this package format records only a source commit"
    Ok ()
  with ex -> Error ("Bozzetto.Harmony verification failed: " + ex.Message)

let private git directory arguments =
  let start = ProcessStartInfo("git")
  start.WorkingDirectory <- directory
  start.UseShellExecute <- false
  start.RedirectStandardOutput <- true
  start.RedirectStandardError <- true
  for argument in arguments do start.ArgumentList.Add argument
  use child = new Process(StartInfo = start)
  require (child.Start()) "Could not start git"
  let stdout = child.StandardOutput.ReadToEndAsync()
  let stderr = child.StandardError.ReadToEndAsync()
  if not (child.WaitForExit(15000)) then
    child.Kill(true)
    invalidOp "Git provenance query exceeded 15 seconds"
  let output = stdout.GetAwaiter().GetResult()
  let error = stderr.GetAwaiter().GetResult()
  require (child.ExitCode = 0) ("Git provenance query failed: " + error.Trim())
  output

let importPackage (packagePath: string) sourceFork repoRoot : Result<unit, string> =
  try
    require (Path.IsPathFullyQualified packagePath) "Package path must be absolute"
    let source = absoluteDirectory "Source fork" sourceFork
    let root = absoluteDirectory "Repository" repoRoot
    let top = (git source [ "rev-parse"; "--show-toplevel" ]).Trim()
    require (Path.GetFullPath top = source.TrimEnd(Path.DirectorySeparatorChar)) "Source fork must identify its Git checkout root"
    let version = expectedVersion root
    let bytes = File.ReadAllBytes packagePath
    let sourceCommit = inspectPackage bytes version
    let filename = packageId + "." + version + ".nupkg"
    let hash = sha256 bytes
    let directory = Path.Combine(root, "vendor", packageId)
    let manifestPath = Path.Combine(directory, "package.json")
    let destination = Path.Combine(directory, filename)
    if File.Exists destination then
      require (sha256 (File.ReadAllBytes destination) = hash)
        "Cannot replace a retained Bozzetto.Harmony version with different package bytes: increment BozzettoBuild and the central package version, rebuild, then import. NuGet caches package versions immutably."
    let established =
      if not (File.Exists manifestPath) then false
      else
        use existing = JsonDocument.Parse(File.ReadAllText manifestPath)
        let previous = existing.RootElement
        let sameVersion =
          previous.GetProperty("id").GetString() = packageId
          && previous.GetProperty("version").GetString() = version
        if sameVersion then
          require (previous.GetProperty("sha256").GetString() = hash)
            "Cannot replace an existing Bozzetto.Harmony version with different package bytes: increment BozzettoBuild and the central package version, rebuild, then import. NuGet caches package versions immutably."
        sameVersion
    if established then
      // Identical bytes keep their reviewed origin, even if the selected fork
      // has since advanced. Never manufacture new build provenance on reimport.
      validate root
    else
      let head = (git source [ "rev-parse"; "HEAD" ]).Trim()
      require (head = sourceCommit)
        "Selected Harmony source HEAD disagrees with the package's embedded repository commit; import the matching source build"
      git source [ "merge-base"; "--is-ancestor"; upstreamBase; "HEAD" ] |> ignore
      let submodules = (git source [ "submodule"; "status"; "--recursive" ]).TrimEnd()
      require (submodules.Split('\n') |> Array.forall (fun row -> row = "" || row.StartsWith(" ", StringComparison.Ordinal)))
        "Harmony submodules must be initialized at their committed revisions before source attribution"
      let status = git source [ "status"; "--porcelain=v1"; "--untracked-files=all"; "--ignore-submodules=none" ]
      require (String.IsNullOrWhiteSpace status)
        "Cannot attribute a new Harmony package to a dirty checkout: commit source/submodule changes and rebuild. The package records a commit, not a hash-bound dirty-tree build attestation."
      let manifest =
        {| schemaVersion = 1
           id = packageId
           version = version
           file = filename
           sha256 = hash
           source =
             {| upstreamRepository = "https://github.com/WillEhrendreich/LibHarmony.git"
                upstreamBase = upstreamBase
                commit = sourceCommit
                submodules = submodules
                dirty = false
                dirtyDiffSha256 = sha256 (Encoding.UTF8.GetBytes "\000") |} |}
      Directory.CreateDirectory directory |> ignore
      let suffix = "." + Guid.NewGuid().ToString("N") + ".tmp"
      let packageTemp = Path.Combine(directory, filename + suffix)
      let manifestTemp = Path.Combine(directory, "package.json" + suffix)
      try
        File.WriteAllBytes(packageTemp, bytes)
        File.WriteAllText(manifestTemp, JsonSerializer.Serialize(manifest, JsonSerializerOptions(WriteIndented = true)) + "\n")
        File.Move(packageTemp, destination, true)
        File.Move(manifestTemp, Path.Combine(directory, "package.json"), true)
      finally
        if File.Exists packageTemp then File.Delete packageTemp
        if File.Exists manifestTemp then File.Delete manifestTemp
      validate root
  with ex -> Error ("Bozzetto.Harmony import failed: " + ex.Message)
