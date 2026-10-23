# Sokoban

**Play in your browser: https://coenvk.github.io/sokoban-cs/**

A C# rewrite of [coenvk/Sokoban](https://github.com/coenvk/Sokoban), with engine ideas from
[coenvk/Jentl](https://github.com/coenvk/Jentl). Rendering uses MonoGame.

## Layout

| Path | What |
|---|---|
| `src/Sokoban.Model` | Pure game logic, no rendering: `Board`, `Level`, `LevelSession` (moves, undo/redo), `Xsb` parser/writer |
| `src/Jentl.Core` | Small engine on MonoGame: `JentlGame`, `SceneManager` (scene stack, overlays, transitions), input action map and auto-repeat, easing |
| `src/Jentl.Puzzle` | Puzzle kit: turn-based grid puzzles with rules, undo/redo and level files (see `docs/kits.md`) |
| `src/Jentl.Cards` | Card kit: cards, decks, piles, rules, undo/redo and hints (see `docs/kits.md`) |
| `src/Klondike`, `src/FreeCell` | Klondike and FreeCell rules on the card kit |
| `src/Sokoban.Solver` | Solver (A* over pushes, matching heuristic, dead-square and frozen-block pruning) and level generator (reverse play) |
| `src/Sokoban.Desktop` | MonoGame (DesktopGL) app: scenes (menu, level select, play, pause, complete), rendering, UI |
| `src/Jentl.Core.Kni`, `src/Sokoban.Web` | Browser build: the same engine and game sources compiled against KNI and hosted in Blazor WebAssembly |
| `tests/Sokoban.Model.Tests` | xUnit tests for the model |
| `tests/Jentl.Core.Tests` | xUnit tests for the engine (runs headless) |
| `tests/Sokoban.Solver.Tests` | xUnit tests for the solver, deadlock detection and generator |
| `tests/Jentl.Puzzle.Tests`, `tests/Jentl.Cards.Tests`, `tests/Klondike.Tests`, `tests/FreeCell.Tests` | xUnit tests for the kits and Klondike |
| `tools/LevelConverter` | One-shot converter from the old Java level format to XSB |
| `tools/SolverBench` | `solve`: run the solver over a pack; `generate`: print generated levels as XSB |
| `levels/original.xsb` | The 100 levels from the Java version |
| `levels/ice.xsb` | 20 ice levels: three hand-made tutorials, then generated levels from easy to hard |

## Build

```bash
dotnet build
dotnet test
dotnet run --project src/Sokoban.Desktop
```

Command-line options (handy for checking screens without playing):

- `--level <n>` opens level n directly (above the main menu)
- `--keys "<k1> <k2> ..."` plays these keys instead of reading the keyboard (MonoGame `Keys` names; `Wait` idles half a second), plus
  `Tap:x,y` and `Drag:x1,y1,x2,y2` for pointer input
- `--screenshot <path>` saves a PNG once the keys have played out, then exits
- `--save-dir <path>` keeps progress in this directory (scripted runs otherwise keep progress in memory only)
- `--settle <frames>` idle frames after the keys before the screenshot (default 60; use 1 to catch an animation mid-step)
- `--deal <n>` deals card game n; `--size <w>x<h>` sets the window size (e.g. `390x844` for a phone layout)

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

On a touchscreen or with a mouse: tap or click menu items and level tiles (a selected tile again to play), tap a
cell to walk there, tap a crate next to the player to push it, and swipe or drag to step. The buttons under the
board undo, redo, restart, give a hint and open the menu.

On a keyboard:

| Key | Action |
|---|---|
| Arrows / WASD | Move (hold to repeat) |
| Z / U / Backspace | Undo (hold to repeat) |
| Y | Redo |
| R | Restart level |
| H | Hint: plays the moves up to the next push (stuck crates are marked red) |
| Tab | Switch level pack (Classic / Ice) in level select |
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
7. ~~Solver (hints, deadlock detection), level generator~~, ~~twist mechanic: ice tiles~~

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

## Ice

Crates pushed onto ice keep sliding in the push direction until the next cell is a wall or a crate, or until they
reach a cell that is not ice (where they stop). The player walks on ice normally, and ice never holds a goal.
The XSB format is extended with `~` (ice), `=` (crate on ice) and `&` (player on ice); levels without ice are written
exactly as before, so their progress keys are unchanged.

The solver handles slides directly. Its dead-square analysis treats every cell along a slide as a possible stop,
because another crate could stop a slide there; that keeps the analysis safe. The generator adds ice patches to
reverse-played rooms and keeps a level only if the solver solves it with at least one slide:

```bash
dotnet run -c Release --project tools/SolverBench -- generate 10 7 9 8 3 8 7 > ice-extra.xsb
```

## Packaging

```bash
build/package.sh              # this machine's runtime, compiled with NativeAOT
build/package.sh win-x64      # other runtimes: self-contained, trimmed single file
build/package.sh linux-x64
build/package.sh web          # static website (zip of wwwroot)
```

Pushing to `main` publishes the web build to GitHub Pages (`.github/workflows/pages.yml`).

Each run writes `dist/Sokoban-<version>-<rid>.zip`. On macOS it contains `Sokoban.app` (icon, `Info.plist`, ad-hoc
signature); elsewhere a `Sokoban/` folder with the executable, SDL2/OpenAL libraries, `Assets/` and `Levels/`.

- The ad-hoc signature is enough to run the app on the Mac that built it. Handing it to other Macs needs a Developer ID
  signature and notarisation, otherwise Gatekeeper blocks it.
- Linux needs OpenGL and OpenSSL (`libssl`), both present on desktop distributions. The Linux build was smoke-tested
  in Docker under Xvfb; the Windows build has not been run.
- Without an audio device the game runs silently.

## Web

The browser build compiles the desktop game's sources unchanged against [KNI](https://github.com/kniEngine/kni)
(a MonoGame fork with a Blazor WebAssembly platform):

```bash
dotnet run --project src/Sokoban.Web     # http://localhost:5259
build/package.sh web                     # dist/Sokoban-<version>-web.zip: upload to any static host
```

Differences from desktop:

- Game files are downloaded up front (the list is generated at build time) and read from memory; KNI's synchronous
  file requests cannot carry binary files intact.
- Progress and settings live in `localStorage`.
- Quit and Fullscreen are hidden.
- WebAssembly has one thread, so a hint blocks the game while it searches; its budget is 1 second instead of 3.
- The dev server needs the linked assets physically under `wwwroot`; the build mirrors them there (git-ignored).

## Game kits and Solitaire

Sokoban runs on Jentl's **puzzle kit** (`Jentl.Puzzle`): its rules, ice included, are an `IPuzzleRules`, and moves,
undo, redo and level files come from the kit. The main menu also has card games built on the **card
kit** (`Jentl.Cards`) under **Card games**: Klondike and FreeCell. Drag cards or tap one to send it where it fits,
with undo, hints and automatic moves home. FreeCell deal numbers match Microsoft FreeCell (deal 1 is the classic deal 1).
`docs/kits.md` explains how to build a new puzzle or card game with the kits.
