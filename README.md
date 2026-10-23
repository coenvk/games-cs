# Sokoban

A C# rewrite of [coenvk/Sokoban](https://github.com/coenvk/Sokoban), with engine ideas from
[coenvk/Jentl](https://github.com/coenvk/Jentl). Rendering uses MonoGame.

## Layout

| Path | What |
|---|---|
| `src/Sokoban.Model` | Pure game logic, no rendering: `Board`, `Level`, `LevelSession` (moves, undo/redo), `Xsb` parser/writer |
| `src/Jentl.Core` | Small engine on MonoGame: `JentlGame`, `SceneManager` (scene stack, overlays, transitions), input action map and auto-repeat, easing |
| `src/Sokoban.Desktop` | MonoGame (DesktopGL) app: scenes (menu, level select, play, pause, complete), rendering, UI |
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

Command-line options (handy for checking screens without playing):

- `--level <n>` opens level n directly (above the main menu)
- `--keys "<k1> <k2> ..."` plays these keys instead of reading the keyboard (MonoGame `Keys` names; `Wait` idles half a second)
- `--screenshot <path>` saves a PNG once the keys have played out, then exits

```bash
dotnet run --project src/Sokoban.Desktop -- --keys "Down Enter Wait" --screenshot select.png
```

## Controls

| Key | Action |
|---|---|
| Arrows / WASD | Move (hold to repeat) |
| Z / U / Backspace | Undo (hold to repeat) |
| Y | Redo |
| R | Restart level |
| Enter / Space | Select menu item; reopen the completion screen on a solved level |
| PageDown / `]`, PageUp / `[` | Next / previous level |
| Esc | Pause in a level; back in menus |

## Roadmap

1. ~~Model, XSB parser, level conversion~~
2. ~~MonoGame play scene: grid rendering, movement, undo/redo, win check~~
3. ~~Engine layer (`Jentl.Core`): scene stack, transitions, input action map~~
4. ~~Menus, level select, pause, completion screen~~
5. Saved progress (best moves/pushes/time)
6. Polish: tweened movement, animation, sound
7. Solver (hints, deadlock detection), level generator, twist mechanic
