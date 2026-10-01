# Bozzetto Demo Applications

Standalone F# applications demonstrating graphics, game loops and web frameworks.

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

### 🌐 Webapp Datastar — Reactive Todo List

A real-time web application using Falco (F# web framework) and Datastar
(SSE-based reactivity). Add, toggle, and delete todos with instant updates.

```bash
cd Bozzetto.Samples.WebappDatastar
dotnet run
```

Then open `http://localhost:5000` in your browser.

F# implementation experiments can use the separate SageFS service. Composer
projects use Bozzetto’s explicit compiler sessions and artifact authority.

## Requirements

- .NET 10 SDK
- For Raylib demos: a display (won't work in headless environments)
- For the web demo: a web browser
