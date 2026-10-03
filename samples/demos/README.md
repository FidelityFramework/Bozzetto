# Bozzetto Demo Applications

Standalone F# applications demonstrating graphics and game loops.

## Projects

### 🎨 Raylib Hello — Animated Shapes

A simple Raylib window with animated circle and pulsing ring.
Use it to explore rendering and animation.

```bash
cd Bozzetto.Samples.RaylibHello
dotnet run
```

Change colors, shapes or animation speeds, then rebuild and restart the application.

### 🎮 Raylib Game — Star Catcher

A simple game: catch falling stars with arrow keys. Demonstrates game loops,
scoring, and collision detection.

```bash
cd Bozzetto.Samples.RaylibGame
dotnet run
```

These demos are standalone .NET applications: edit them, then rebuild and
rerun with `dotnet run`. Bozzetto’s embedded production FSI hosting is
retired, so they run without a Bozzetto session. Composer projects use
Bozzetto’s explicit compiler sessions and artifact authority.

## Requirements

- .NET 10 SDK
- For Raylib demos: a display (won't work in headless environments)
