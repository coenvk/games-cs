# Sokoban

A C# rewrite of [coenvk/Sokoban](https://github.com/coenvk/Sokoban), with engine ideas from
[coenvk/Jentl](https://github.com/coenvk/Jentl). Rendering uses MonoGame.

## Layout

| Path | What |
|---|---|
| `src/Sokoban.Model` | Pure game logic, no rendering: `Board`, `Level`, `LevelSession` (moves, undo/redo), `Xsb` parser/writer |
| `src/Jentl.Core` | Small engine on MonoGame: `JentlGame`, `SceneManager` (scene stack, overlays, transitions), input action map and auto-repeat, easing |
| `src/Sokoban.Solver` | Solver (A* over pushes, matching heuristic, dead-square and frozen-block pruning) and level generator (reverse play) |
| `src/Sokoban.Desktop` | MonoGame (DesktopGL) app: scenes (menu, level select, play, pause, complete), rendering, UI |
| `tests/Sokoban.Model.Tests` | xUnit tests for the model |
| `tests/Jentl.Core.Tests` | xUnit tests for the engine (runs headless) |
| `tests/Sokoban.Solver.Tests` | xUnit tests for the solver, deadlock detection and generator |
| `tools/LevelConverter` | One-shot converter from the old Java level format to XSB |
| `tools/SolverBench` | `solve`: run the solver over a pack; `generate`: print generated levels as XSB |
| `levels/original.xsb` | The 100 levels from the Java version |

## Build

```bash
dotnet build
dotnet test
dotnet run --project src/Sokoban.Desktop
```

Command-line options (handy for checking screens without playing):

- `--level <n>` opens level n directly (above the main menu)
- `--keys "<k1> <k2> ..."` plays these keys instead of reading the keyboard (MonoGame `Keys` names; `Wait` idles half a second)
- `--screenshot <path>` saves a PNG once the keys have played out, then exits
- `--save-dir <path>` keeps progress in this directory (scripted runs otherwise keep progress in memory only)
- `--settle <frames>` idle frames after the keys before the screenshot (default 60; use 1 to catch an animation mid-step)

```bash
dotnet run --project src/Sokoban.Desktop -- --keys "Down Enter Wait" --screenshot select.png
```

## Progress

Progress (best moves, pushes, time and the fewest-moves solution per level) is saved as JSON after every improvement:

- macOS: `~/Library/Application Support/Sokoban/progress.json`
- Windows: `%LOCALAPPDATA%\Sokoban\progress.json`
- Linux: `~/.local/share/Sokoban/progress.json`

Settings (sound, fullscreen) live next to it in `settings.json`.

Levels are keyed by a hash of their layout, so reordering or retitling a pack keeps progress. An unreadable file is renamed to `progress.json.corrupt-<timestamp>` and the game starts fresh.

## Controls

| Key | Action |
|---|---|
| Arrows / WASD | Move (hold to repeat) |
| Z / U / Backspace | Undo (hold to repeat) |
| Y | Redo |
| R | Restart level |
| H | Hint: plays the moves up to the next push (stuck crates are marked red) |
| Enter / Space | Select menu item; reopen the completion screen on a solved level |
| PageDown / `]`, PageUp / `[` | Next / previous level |
| Esc | Pause in a level; back in menus |

## Roadmap

1. ~~Model, XSB parser, level conversion~~
2. ~~MonoGame play scene: grid rendering, movement, undo/redo, win check~~
3. ~~Engine layer (`Jentl.Core`): scene stack, transitions, input action map~~
4. ~~Menus, level select, pause, completion screen~~
5. ~~Saved progress (best moves/pushes/time)~~
6. ~~Polish: tweened movement, animation, sound, options (sound, fullscreen)~~
7. ~~Solver (hints, deadlock detection), level generator~~, twist mechanic

## Solver and generator

`Sokoban.Solver` searches over crate pushes with weighted A*. The heuristic is the cheapest assignment of crates to
distinct goals by push distance (Hungarian algorithm); positions with a crate on a dead square or in a frozen 2x2
block are pruned. In-game hints use weight 3 and a 3 second budget, which solves 58 of the 100 original levels from
the start (average 86 ms when it succeeds):

```bash
dotnet run -c Release --project tools/SolverBench -- solve levels/original.xsb 1000000 3 3
```

The generator carves a room, starts with every crate on a goal and plays backwards with random pulls, so every level
it makes is solvable; the solver then picks the candidate needing the most pushes:

```bash
dotnet run -c Release --project tools/SolverBench -- generate 10 42 > generated.xsb
```
