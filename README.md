# Sokoban

A C# rewrite of [coenvk/Sokoban](https://github.com/coenvk/Sokoban), with engine ideas from
[coenvk/Jentl](https://github.com/coenvk/Jentl). Rendering uses MonoGame.

## Layout

| Path | What |
|---|---|
| `src/Sokoban.Model` | Pure game logic, no rendering: `Board`, `Level`, `LevelSession` (moves, undo/redo), `Xsb` parser/writer |
| `src/Jentl.Core` | Small engine on MonoGame: `JentlGame`, `SceneManager` (scene stack, overlays, transitions), input action map and auto-repeat, easing |
| `src/Sokoban.Desktop` | MonoGame (DesktopGL) app: rendering, input, level switching |
| `tests/Sokoban.Model.Tests` | xUnit tests for the model |
| `tests/Jentl.Core.Tests` | xUnit tests for the engine (runs headless) |
| `tools/LevelConverter` | One-shot converter from the old Java level format to XSB |
| `levels/original.xsb` | The 100 levels from the Java version |

## Build

```bash
dotnet build
dotnet test
dotnet run --project src/Sokoban.Desktop
```

Command-line options: `--level <n>` starts at level n; `--screenshot <path>` renders one frame to a PNG and exits.

## Controls

| Key | Action |
|---|---|
| Arrows / WASD | Move (hold to repeat) |
| Z / U / Backspace | Undo (hold to repeat) |
| Y | Redo |
| R | Restart level |
| Enter / Space | Next level (once solved) |
| PageDown / `]`, PageUp / `[` | Next / previous level |
| Esc | Back (quits from the level for now) |

Moves, pushes and the solved state are shown in the window title until there is an in-game HUD.

## Roadmap

1. ~~Model, XSB parser, level conversion~~
2. ~~MonoGame play scene: grid rendering, movement, undo/redo, win check~~
3. ~~Engine layer (`Jentl.Core`): scene stack, transitions, input action map~~
4. Menus, level select, pause, completion screen
5. Saved progress (best moves/pushes/time)
6. Polish: tweened movement, animation, sound
7. Solver (hints, deadlock detection), level generator, twist mechanic
