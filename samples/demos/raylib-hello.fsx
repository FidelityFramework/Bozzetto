// ============================================================
//  🎮  Raylib Hello World — Bozzetto Edition
//  A GPU-rendered window for rendering and animation experiments.
//  Edit, rebuild and restart the corresponding RaylibHello project.
// ============================================================
//
//  Dependencies: Raylib-cs  (in Directory.Packages.props)
//  Run the project: dotnet run --project Bozzetto.Samples.RaylibHello

// #r "nuget: Raylib-cs"

open Raylib_cs
open System.Numerics

// ── Everything that changes goes here — make it a function ──
// Keep rendering logic in a top-level function for clarity.

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
  Raylib.DrawText("Edit, rebuild and restart to explore changes", 80, 530, 16, Color.LightGray)

// └─────────────────────────────────────────────────────────────┘

// ── Window setup — runs once ──
let screenWidth  = 800
let screenHeight = 600

Raylib.InitWindow(screenWidth, screenHeight, "Bozzetto + Raylib Demo")
Raylib.SetTargetFPS(60)

// ── Game loop ──
while not (Raylib.WindowShouldClose()) do
  let time = Raylib.GetTime() |> float32

  Raylib.BeginDrawing()
  // IMPORTANT: ClearBackground MUST be called inside BeginDrawing/EndDrawing.
  // BeginDrawing() sets up the render state but does NOT clear the framebuffer.
  // Without ClearBackground(), previous frame content persists → smearing artifacts.
  drawFrame time
  Raylib.EndDrawing()

Raylib.CloseWindow()

// ── What to try ──
// 1. Change `backgroundColor` to Color.DarkPurple — rebuild and restart
// 2. Change the title string — rebuild and restart
// 3. Add a second animated shape in `drawFrame`
// 4. Try Raylib.DrawRectangle, Raylib.DrawTriangle, Raylib.DrawLine
// 5. Change the animation formula — sin → cos, multiply speed
