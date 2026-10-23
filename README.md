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
| `src/Jentl.Rpg` | RPG kit: Tiled maps, field of view, pathfinding, dungeon generation, turn engine, inventory, quests and dialogue (see `docs/kits.md`) |
| `src/Fluffy` | Fluffy, a finite state machine engine: guards, nested states, timers, diagrams (see `docs/fluffy.md`) |
| `src/CodeLock` | Code lock: Mastermind with a combination lock's digits, and a minimax hint solver |
| `src/Mossbrook` | Mossbrook, the RPG demo: starting a game and generating the cellar floors; its content is in `src/Sokoban.Desktop/Assets/rpg` |
| `src/Klondike`, `src/Easthaven`, `src/FortyThieves`, `src/FreeCell`, `src/Golf`, `src/Pyramid`, `src/Spider`, `src/TriPeaks`, `src/Yukon` | The solitaire games' rules on the card kit |
| `src/Sokoban.Solver` | Solver (A* over pushes, matching heuristic, dead-square and frozen-block pruning) and level generator (reverse play) |
| `src/Sokoban.Desktop` | MonoGame (DesktopGL) app: scenes (menu, level select, play, pause, complete), rendering, UI |
| `src/Jentl.Core.Kni`, `src/Sokoban.Web` | Browser build: the same engine and game sources compiled against KNI and hosted in Blazor WebAssembly |
| `tests/Sokoban.Model.Tests` | xUnit tests for the model |
| `tests/Jentl.Core.Tests` | xUnit tests for the engine (runs headless) |
| `tests/Sokoban.Solver.Tests` | xUnit tests for the solver, deadlock detection and generator |
| `tests/Jentl.Puzzle.Tests`, `tests/Jentl.Cards.Tests`, `tests/Klondike.Tests`, `tests/FreeCell.Tests`, `tests/Pyramid.Tests`, `tests/Spider.Tests`, `tests/TriPeaks.Tests`, `tests/Golf.Tests`, `tests/Yukon.Tests`, `tests/FortyThieves.Tests`, `tests/Easthaven.Tests` | xUnit tests for the kits and card games |
| `tests/Fluffy.Tests` | xUnit tests for Fluffy |
| `tests/CodeLock.Tests` | xUnit tests for the code lock rules and solver |
| `tests/Jentl.Rpg.Tests`, `tests/Mossbrook.Tests` | xUnit tests for the RPG kit, and checks of the shipped Mossbrook content |
| `tools/LevelConverter` | One-shot converter from the old Java level format to XSB |
| `tools/SolverBench` | `solve`: run the solver over a pack; `generate`: print generated levels as XSB |
| `tools/RpgArt` | Draws the RPG tileset (`tiles.png` and `tiles.tsx`) in code |
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
kit** (`Jentl.Cards`) under **Card games**: Klondike, Easthaven (a Klondike and Spider cross: the stock deals a card onto every column, and
empty columns take anything), Forty Thieves (two decks, build down in suit one card at a time), FreeCell, Golf (clear the
columns onto the waste; the score is the cards left), Pyramid, Spider (1, 2 or 4 suits), TriPeaks and Yukon (move any
face-up card with everything on it). Drag cards or tap one to send it where it fits,
with undo, hints and automatic moves home. FreeCell deal numbers match Microsoft FreeCell (deal 1 is the classic deal 1).
`docs/kits.md` explains how to build a new puzzle or card game with the kits.

## Adventure: Mossbrook

**Adventure** on the main menu is a small turn-based RPG on Jentl's **RPG kit** (`Jentl.Rpg`). Thieves have hidden
Elder Maren's amulet in the old cellar under the chapel ruins. Talk to the villagers, step inside their houses (each a small Tiled map
of its own, with a few things to find and Gran Hettie and Whiskers the cat to meet), pick up quests, get a sword from
Bram the smith, and go down: the cellar's two floors are generated from the game's seed (`--deal` sets it), dark, and
full of rats, slimes, spiders and skeletons. Spiders run on a state machine (Fluffy): they hunt you, flee when badly hurt, and rest out of sight to heal. Returning the amulet wins; two side quests give potions and armour.

Walk into people to talk and into monsters to fight; walking over items picks them up. Arrows/WASD or swipes step,
tapping a cell walks there, `.` or T waits a turn, I or B opens the bag (use potions, equip gear), J or Q the journal.

The game saves itself: on entering an area, opening or closing a conversation, the bag or the menu, every ten turns,
and on leaving. **Adventure** then offers Continue or New adventure. Falling in the cellar deletes the save, as in a
roguelike. Saves live next to `progress.json` on desktop and in `localStorage` in the browser.

The content is plain files in `src/Sokoban.Desktop/Assets/rpg`: `items.json`, `actors.json`, `quests.json`,
`dialogue.json`, the village map `village.tmx`, the house interiors `house_*.tmx`, and their tileset `tiles.tsx`, all of which open in the
[Tiled](https://www.mapeditor.org) map editor. The tileset image is drawn by `tools/RpgArt`:

```sh
dotnet run --project tools/RpgArt -- src/Sokoban.Desktop/Assets/rpg              # tiles.png and tiles.tsx
dotnet run --project tools/RpgArt -- src/Sokoban.Desktop/Assets/rpg --preview x.png   # plus a 4x preview
```

`tests/Mossbrook.Tests` validates the content (every dialogue node, item, quest and map object referenced exists),
checks that every villager and the cellar's stairs and amulet can be reached, and plays the quests through.

## Code lock

**Code lock** on the main menu is Mastermind with a combination lock: find the code in ten tries. After each try the
lock lights one green lamp for every digit in the right place and one amber lamp for every right digit in the wrong
place. Easy locks have 3 different digits, normal ones 4, and hard ones 5 that may repeat. Turn the wheels with the
arrows above and below them (or swipe them), or type digits; Enter tries the code. **Hint** sets the wheels to a code
that fits every answer so far, picked to split the remaining possibilities as evenly as possible (minimax, as in
Knuth's Mastermind strategy), and the status line says how many codes still fit. `--deal` picks the lock.

Each difficulty keeps its record in `codelock.json`, next to `progress.json` (in `localStorage` in the browser): games
played and won, the fewest tries, and the current and longest winning streak. The difficulty menu shows them, and the
end screen points out a new best. Only finished locks count; one left half-cracked is neither a win nor a loss.
