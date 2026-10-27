open System
open System.IO
open System.Text.Json

let arguments = fsi.CommandLineArgs |> Array.skip 1
if arguments.Length <> 2 then failwith "Expected evaluated Compile JSON and output path."
let document = JsonDocument.Parse(File.ReadAllText(arguments[0]))
let files =
  document.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
  |> Seq.map (fun item ->
    match item.GetProperty("FullPath").GetString() |> Option.ofObj with
    | Some path when Path.IsPathFullyQualified(path) && File.Exists(path) -> Path.GetFullPath(path)
    | _ -> failwith "An evaluated Compile item has no existing absolute path.")
  |> Seq.toList
if files.IsEmpty then failwith "The evaluated project has no Compile items."
if files.Length <> (files |> List.distinct |> List.length) then failwith "The evaluated project repeats a Compile item."
File.WriteAllLines(arguments[1], files |> List.sort)
