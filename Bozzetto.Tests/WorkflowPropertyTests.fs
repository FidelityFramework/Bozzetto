module Bozzetto.Tests.WorkflowPropertyTests

open Expecto

open Expecto.Flip

open Bozzetto.WorkflowTypes

[<Tests>]
let projectKindTests =
  testList "ProjectKind classification" [
    testCase "web frameworks classify as Web" <| fun _ ->
      ProjectKind.classify [ "Falco"; "FSharp.Core" ] |> ProjectKind.label
      |> Expect.equal "Falco is web" "web"
      ProjectKind.classify [ "Microsoft.AspNetCore.App" ] |> ProjectKind.label
      |> Expect.equal "AspNetCore is web" "web"
      ProjectKind.classify [ "Giraffe" ] |> ProjectKind.label
      |> Expect.equal "Giraffe is web" "web"

    testCase "native game AND desktop-UI libraries classify as NativeGui" <| fun _ ->
      ProjectKind.classify [ "Raylib-cs" ] |> ProjectKind.label
      |> Expect.equal "Raylib is native-gui" "native-gui"
      ProjectKind.classify [ "SDL2-CS" ] |> ProjectKind.label
      |> Expect.equal "SDL2 is native-gui" "native-gui"
      // Desktop UI frameworks are native-GUI too (no WebApplication).
      ProjectKind.classify [ "Avalonia"; "Avalonia.Desktop"; "SkiaSharp" ] |> ProjectKind.label
      |> Expect.equal "Avalonia is native-gui" "native-gui"
      ProjectKind.classify [ "Microsoft.Maui.Controls" ] |> ProjectKind.label
      |> Expect.equal "MAUI is native-gui" "native-gui"
      ProjectKind.classify [ "Microsoft.WindowsAppSDK" ] |> ProjectKind.label
      |> Expect.equal "WinUI is native-gui" "native-gui"
      ProjectKind.classify [ "Uno.WinUI" ] |> ProjectKind.label
      |> Expect.equal "Uno is native-gui" "native-gui"
      // WPF/WinForms have NO package — they are MSBuild properties, surfaced as
      // markers by classifyProject. WPF detection must survive a Windows checkout.
      ProjectKind.classify [ "FSharp.Core"; "UseWPF" ] |> ProjectKind.label
      |> Expect.equal "WPF (UseWPF marker) is native-gui" "native-gui"
      ProjectKind.classify [ "UseWindowsForms" ] |> ProjectKind.label
      |> Expect.equal "WinForms (UseWindowsForms marker) is native-gui" "native-gui"

    testCase "everything else is Console" <| fun _ ->
      ProjectKind.classify [ "Expecto"; "FSharp.Core" ] |> ProjectKind.label
      |> Expect.equal "plain is console" "console"
      ProjectKind.classify [] |> ProjectKind.label
      |> Expect.equal "empty is console" "console"

    testCase "NativeGui wins over Web when both are present" <| fun _ ->
      // A native game that also references a web lib is still a game — native
      // windowing dominates the reload strategy.
      ProjectKind.classify [ "Raylib-cs"; "Microsoft.AspNetCore.App" ] |> ProjectKind.label
      |> Expect.equal "native-gui precedence" "native-gui"

    testCase "a Web project structurally carries a browser config" <| fun _ ->
      match ProjectKind.classify [ "Falco" ] with
      | ProjectKind.Web cfg -> cfg.WatchPatterns |> Expect.isNonEmpty "web carries a config"
      | other -> failtestf "expected Web, got %A" other
  ]
