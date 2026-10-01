// ============================================================
//  🎮  Raylib Hello World — Bozzetto Edition
//  A GPU-rendered window with an animated drawing loop.
//  Change the color, text, or layout, then rebuild and restart.
// ============================================================
//
//  Dependencies: Raylib-cs  (in Directory.Packages.props)
//  Run this sample project directly after building its dependencies.

module Bozzetto.Samples.RaylibHello.Program

#nowarn "3391" // implicit CBool -> bool conversion from Raylib-cs

open Raylib_cs
open System.Numerics
open Bozzetto.Samples.DemoEnv

let backgroundColor = Color.RayWhite   // try: Color.SkyBlue, Color.DarkGray

let titleText = "Hello from Bozzetto + Raylib! 🦅"
let subtitleText = "Edit me and save. No restart. Just magic."

let drawFrame (time: float32) =
  // Clear the screen — ALWAYS required before drawing.
  // BeginDrawing() sets up the render state but does NOT clear the framebuffer.
  // Without ClearBackground(), the previous frame's content persists → smearing artifacts.
  Raylib.ClearBackground(backgroundColor)

  // Title
  Raylib.DrawText(titleText, 80, 120, 36, Color.DarkGray)

  // Subtitle
  Raylib.DrawText(subtitleText, 80, 175, 20, Color.Gray)

  // Animated circle — bounces left/right
  let t = time * 1.5f
  let cx = int (400.0f + System.MathF.Sin(t) * 250.0f)
  let cy = 300
  Raylib.DrawCircle(cx, cy, 30.0f, Color.SkyBlue)
  Raylib.DrawCircleLines(cx, cy, 30.0f, Color.DarkBlue)

  // Pulsing ring
  let pulse = 20.0f + 10.0f * System.MathF.Abs(System.MathF.Sin(t * 0.5f))
  Raylib.DrawCircleLines(400, 300, pulse, Color.Purple)

  // FPS counter
  Raylib.DrawFPS(10, 10)

  // Hot-reload hint
  Raylib.DrawText("💾 Save this file to see hot reload in action", 80, 530, 16, Color.LightGray)

// └─────────────────────────────────────────────────────────────┘

// ── Window setup — runs once ──
let screenWidth  = 800
let screenHeight = 600

// ── Demo-recording controls (env-driven, off by default — see DemoEnv.fs) ──
// BOZZETTO_DEMO_WINDOW="x,y,w,h" places/sizes the window at startup.
// Unset or malformed values leave the window exactly as it was before this file existed.
let private envVar name = System.Environment.GetEnvironmentVariable name |> Option.ofObj
let demoWindow = parseWindow (envVar "BOZZETTO_DEMO_WINDOW")

[<EntryPoint>]
let main _argv =
  Raylib.InitWindow(screenWidth, screenHeight, "Bozzetto + Raylib Demo")
  Raylib.SetTargetFPS(60)

  match demoWindow with
  | Some spec ->
    Raylib.SetWindowPosition(spec.X, spec.Y)
    Raylib.SetWindowSize(spec.Width, spec.Height)
  | None -> ()

  // ── Game loop ──
  while not (Raylib.WindowShouldClose()) do
    let time = Raylib.GetTime() |> float32

    Raylib.BeginDrawing()
    drawFrame time
    Raylib.EndDrawing()

  Raylib.CloseWindow()
  0

// ── What to try ──
// Rebuild and restart after edits to try each change:
// 1. Change `backgroundColor` to Color.DarkPurple
// 2. Change the title string
// 3. Add a second animated shape in `drawFrame`
// 4. Try Raylib.DrawRectangle, Raylib.DrawTriangle, Raylib.DrawLine
// 5. Change the animation formula — sin → cos, multiply speed
