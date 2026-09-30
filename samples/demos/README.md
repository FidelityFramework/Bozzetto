# Bozzetto Demo Applications

Working applications demonstrating Bozzetto with different frameworks.

## Projects

### 🎨 Raylib Hello — Animated Shapes

A simple Raylib window with animated circle and pulsing ring.
Great for learning how Bozzetto hot-reloads graphics code.

```bash
cd Bozzetto.Samples.RaylibHello
dotnet run
```

Change colors, shapes, or animation speeds — Bozzetto patches function
pointers via Harmony, so your changes appear instantly in the running window.

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

## Using with Bozzetto

For the best development experience:

```bash
cd Bozzetto.Samples.RaylibHello   # or any demo
boz watch .
```

Bozzetto provides:
- **Hot reload** — edit functions and see changes in the running app
- **Alt+Enter** — evaluate any expression inline
- **Gutter markers** — see test results next to your code

## Requirements

- .NET 10 SDK
- For Raylib demos: a display (won't work in headless environments)
- For the web demo: a web browser
