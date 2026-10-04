#!/usr/bin/env dotnet fsi
// Zero-dependency build script for Bozzetto.
// Usage:
//   dotnet fsi build.fsx            # restore pinned packages + build
//   dotnet fsi build.fsx -- test    # build + run tests
//   dotnet fsi build.fsx -- install # build + pack + install as global tool
//   dotnet fsi build.fsx -- ext     # build + package + install the VS Code extension
//   dotnet fsi build.fsx -- all     # build + test + pack + install + VS Code extension

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices

let rootDir = __SOURCE_DIRECTORY__
let vscodeDir = Path.Combine(rootDir, "bozzetto-vscode")

/// Run a command. On Windows, .cmd/.bat scripts (npm, npx, code, etc.)
/// need to go through cmd.exe since FSI can't launch them directly.
let run (cmd: string) (args: string) (workDir: string) =
  let fileName, arguments =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
       && not (cmd.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) then
      "cmd.exe", sprintf "/c %s %s" cmd args
    else
      cmd, args
  let psi =
    ProcessStartInfo(
      FileName = fileName,
      Arguments = arguments,
      WorkingDirectory = workDir,
      UseShellExecute = false)
  let p = Process.Start(psi)
  p.WaitForExit()
  if p.ExitCode <> 0 then
    failwithf "FAILED (exit %d): %s %s" p.ExitCode cmd args

let runAllowCodes (codes: int list) (cmd: string) (args: string) (workDir: string) =
  let fileName, arguments =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
       && not (cmd.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) then
      "cmd.exe", sprintf "/c %s %s" cmd args
    else
      cmd, args
  let psi =
    ProcessStartInfo(
      FileName = fileName,
      Arguments = arguments,
      WorkingDirectory = workDir,
      UseShellExecute = false)
  let p = Process.Start(psi)
  p.WaitForExit()
  if p.ExitCode <> 0 && not (List.contains p.ExitCode codes) then
    failwithf "FAILED (exit %d): %s %s" p.ExitCode cmd args

let target =
  match fsi.CommandLineArgs |> Array.tryItem 1 with
  | Some t -> t.ToLowerInvariant()
  | None -> "build"

// --- Step 1: Build ---
printfn "=== Build ==="
run "dotnet" "build" rootDir
printfn "  Build succeeded."

// --- Step 2 (optional): Test ---
if target = "test" || target = "all" then
  printfn "=== Test ==="
  // Expecto exit code 2 = no TTY (cosmetic), treat as success
  runAllowCodes [2] "dotnet" "run --no-build --project Bozzetto.Tests -- --summary" rootDir
  printfn "  Tests passed."

// --- Step 3 (optional): Pack + Install ---
if target = "install" || target = "all" then
  printfn "=== Pack CLI ==="
  run "dotnet" "pack Bozzetto -c Release" rootDir
  printfn "  Packed Bozzetto CLI."

  printfn "=== Install CLI ==="
  let nupkgDir = Path.Combine(rootDir, "nupkg")
  try
    run "dotnet" (sprintf "tool update --global Bozzetto --add-source \"%s\" --no-cache" nupkgDir) rootDir
    printfn "  Updated global Bozzetto tool."
  with _ ->
    run "dotnet" (sprintf "tool install --global Bozzetto --add-source \"%s\" --no-cache" nupkgDir) rootDir
    printfn "  Installed global Bozzetto tool."

// --- Step 4 (optional): Build + Install the VS Code Extension ---
if target = "ext" || target = "extensions" || target = "all" then
  // -- VS Code extension --
  printfn "=== VS Code Extension ==="
  run "dotnet" "tool restore" vscodeDir
  run "npm" "ci" vscodeDir
  run "npm" "run compile" vscodeDir
  printfn "  Compiled VS Code extension."

  // Package as VSIX
  let version =
    let props = File.ReadAllText(Path.Combine(rootDir, "Directory.Build.props"))
    let m = Text.RegularExpressions.Regex.Match(props, @"<Version>([^<]+)</Version>")
    if m.Success then m.Groups.[1].Value else "0.0.0"
  let vsixPath = Path.Combine(vscodeDir, sprintf "bozzetto-%s.vsix" version)
  run "npx" (sprintf "@vscode/vsce package -o \"%s\"" vsixPath) vscodeDir
  printfn "  Packaged: %s" vsixPath

  // Install into VS Code
  try
    run "code" (sprintf "--install-extension \"%s\" --force" vsixPath) rootDir
    printfn "  Installed VS Code extension."
  with ex ->
    printfn "  ⚠ VS Code install skipped: %s" ex.Message

printfn "=== Done ==="
