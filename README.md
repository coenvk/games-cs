# Games on Jentl

**Play in your browser: https://coenvk.github.io/games-cs/**

Six small games made with [Jentl](https://github.com/coenvk/jentl-cs), a C# game engine on MonoGame (desktop) and KNI (the
browser). It started as a C# rewrite of [coenvk/Sokoban](https://github.com/coenvk/Sokoban), and the engine grew alongside;
the engine now lives in its own repository and comes in here as packages.

| Game | What it is |
|---|---|
| **Sokoban** | Push every crate onto a goal: 100 classic levels, 20 ice levels, generated levels, hints from a solver |
| **Solitaire** | Klondike, Easthaven, Forty Thieves, FreeCell, Golf, Pyramid, Spider, TriPeaks and Yukon |
| **Card Table** | Uno and Texas Hold'em against bots or passing the device round, and Uno online |
| **Mossbrook** | A small turn-based adventure: a village, a cellar and a lost amulet |
| **Code Lock** | Mastermind on a combination lock |
| **Bricks** | A brick breaker with power-ups |

Each is its own app, on the desktop and in the browser (each in its own folder of the site above).

## Layout

| Path | What |
|---|---|
| `apps/<Game>/Game` | The game's screens and its part of the app (`<Game>App.cs`: title screen, what it loads) |
| `apps/<Game>/Assets` | The game's own assets (Sokoban's sprite atlas, Mossbrook's maps and content); Sokoban's sprite sources are in `apps/Sokoban/Art` |
| `apps/<Game>/Desktop`, `apps/<Game>/Web` | The game as a desktop app (MonoGame) and as a browser app (KNI in Blazor WebAssembly): a few lines each |
| `shell` | What every app shares: the host (`ShellGame`), the context, options, title screen, panels, audio, controls and translations (`shell/Shell`); the desktop and browser entry points; shared assets (font, sounds, `lang/nl.json`); and `Desktop.props` and `Web.props`, which turn an app folder into an app |
| `src/Sokoban.Model`, `src/Sokoban.Solver` | Sokoban's rules on the puzzle kit, and its solver and level generator |
| `src/Klondike` ... `src/Yukon` | The solitaire games' rules on the card kit |
| `src/Uno`, `src/Poker` | Uno (rules, bots, its online room) and Texas Hold'em |
| `src/Mossbrook`, `src/CodeLock`, `src/Bricks` | The adventure on the RPG kit, the code lock and its solver, and the brick breaker on the ECS and physics |
| `src/GameServer` | The online game server (rooms over WebSockets, hosting Uno), with a Dockerfile |
| `tests/*` | xUnit tests for every game library and the server; `tests/Apps.Tests` checks the apps' translations and assets |
| `tools/LevelConverter`, `tools/SolverBench`, `tools/RpgArt` | The old level converter, the solver bench, and the RPG tileset drawn in code |
| `levels` | Sokoban's level packs |
| `build` | Packaging, the landing page, and scripts for the engine packages and the sprite atlas |

## Build

The engine comes from packages in `.jentl-packages`. `build/jentl.sh` makes them: from a `jentl` checkout next to this
one if there is one, otherwise from GitHub at the version in `dependencies.props`.

```bash
build/jentl.sh
dotnet test Games.slnx
dotnet run --project apps/Sokoban/Desktop        # or Solitaire, CardTable, Mossbrook, CodeLock, Bricks
dotnet run --project apps/Sokoban/Web            # in the browser
```

Changing the engine and a game together: edit `../jentl`, run `build/jentl.sh` again and clear NuGet's cache of the
version (`dotnet nuget locals global-packages --clear`), or give the engine a new version.

Command-line options (handy for checking screens without playing), for every app:

- `--level <n>` opens Sokoban level n directly (above the title screen)
- `--keys "<k1> <k2> ..."` plays these keys instead of reading the keyboard (MonoGame `Keys` names; `Wait` idles half a second), plus
  `Tap:x,y` and `Drag:x1,y1,x2,y2` for pointer input, and `Pad:A` (MonoGame `Buttons` names) and
  `RightStick:x,y,frames[,trigger]` for a gamepad
- `--screenshot <path>` saves a PNG once the keys have played out, then exits
- `--save-dir <path>` keeps progress in this directory (scripted runs otherwise keep progress in memory only)
- `--settle <frames>` idle frames after the keys before the screenshot (default 60; use 1 to catch an animation mid-step)
- `--deal <n>` deals card game n (or seeds the other games); `--size <w>x<h>` sets the window size (e.g. `390x844` for a phone layout)
- `--virtual <w>x<h>[:fit|pixel|expand]` renders at a fixed design resolution scaled into the window (Jentl's
  `VirtualScreen`); the apps normally lay out for the window itself
- `--debug true` starts with the debug overlay showing (F3 toggles it, as does Options > Debug overlay)
- `--server <url>` uses this game server for online play (default `ws://localhost:5080/play`, or `JENTL_SERVER`)
- `--watch <folder>` reloads assets from this folder as they change: `apps/<Game>` for the game's, `shell` for shared ones

```bash
dotnet run --project apps/Sokoban/Desktop -- --keys "Down Enter Wait" --screenshot select.png
```

## Progress

Each game keeps its files in a folder of its own (`Sokoban`, `Solitaire`, `CardTable`, `Mossbrook`, `CodeLock`, `Bricks`); for Sokoban, progress (best moves, pushes, time and the fewest-moves solution per level) is saved as JSON after every improvement:

- macOS: `~/Library/Application Support/Sokoban/progress.json`
- Windows: `%LOCALAPPDATA%\Sokoban\progress.json`
- Linux: `~/.local/share/Sokoban/progress.json`

Settings (sound, fullscreen, language) live next to it in `settings.json`, per game.

Levels are keyed by a hash of their layout, so reordering or retitling a pack keeps progress. An unreadable file is renamed to `progress.json.corrupt-<timestamp>` and the game starts fresh.

## Music and sound

All the music and most sound effects are composed in code for the engine's synthesizer (`shell/Shell/GameAudio.cs`):
a theme for the menu, calm music for the puzzles, a lounge tune for solitaire, a livelier one for Uno and poker, a
folk tune for Mossbrook's village and a dark one for its cellar. Options has a switch and a volume for music and for
sound effects.

## Languages

The games speak English and Dutch. **Options > Language** picks one, or "Automatic" follows the system (the browser's
language on the web). Translations live in `shell/Assets/lang` (one file for all the games); see [Jentl's guide](https://github.com/coenvk/jentl-cs/blob/main/docs/kits.md) for
how to add one. The adventure's story text (dialogue, quests, items) is in English only.

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

With a gamepad (any connected controller; in the browser, press a button first so the page sees it):

| Button | Action |
|---|---|
| D-pad / left stick | Move; menu selection |
| A | Select / confirm |
| B / Start | Pause in a level; back in menus |
| X / LB | Undo |
| RB | Redo; next pack in level select |
| Y | Hint |
| View (Back) | Restart level |
| Right stick + right trigger | An on-screen pointer: move it and pull the trigger to tap, hold to drag. Plays the card games and walks the adventure map; the mouse or a touch takes over again as soon as it moves |

In the adventure Y opens the bag, X the journal and RB waits a turn.

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
build/package.sh Sokoban              # this machine's runtime, compiled with NativeAOT
build/package.sh Solitaire win-x64    # other runtimes: self-contained, trimmed single file
build/package.sh Bricks linux-x64
build/package.sh Mossbrook web        # static website (zip of wwwroot)
```

Each run writes `dist/<Game>-<version>-<rid>.zip`. On macOS it contains `<Game>.app` (icon, `Info.plist`, ad-hoc
signature); elsewhere a folder with the executable, SDL2/OpenAL libraries and `Assets/`.

- The ad-hoc signature is enough to run the app on the Mac that built it. Handing it to other Macs needs a Developer ID
  signature and notarisation, otherwise Gatekeeper blocks it.
- Linux needs OpenGL and OpenSSL (`libssl`), both present on desktop distributions. The Linux build was smoke-tested
  in Docker under Xvfb; the Windows build has not been run.
- Without an audio device the games run silently.
- Every app uses Sokoban's icon for now.

## Web

The browser apps compile the same sources against [KNI](https://github.com/kniEngine/kni) (a MonoGame fork with a
Blazor WebAssembly platform). Pushing to `main` tests everything and publishes the site (`.github/workflows/pages.yml`):
a landing page (`build/site`) with each game in its own folder (`sokoban/`, `solitaire/`, `card-table/`, `mossbrook/`,
`code-lock/`, `bricks/`). CI builds the engine packages from the public jentl repository, so it needs no tokens.

Differences from desktop:

- Game files are downloaded up front (the list is generated at build time) and read from memory; KNI's synchronous
  file requests cannot carry binary files intact.
- Progress and settings live in `localStorage`, under a prefix per game (`sokoban/`, `solitaire/`, ...).
- Quit and Fullscreen are hidden.
- WebAssembly has one thread, so a Sokoban hint blocks the game while it searches; its budget is 1 second instead of 3.
- The page is made from `shell/Web/index.template.html` with the game's title; the dev server needs the linked assets
  physically under `wwwroot`, so the build mirrors them there (git-ignored).

## Sokoban and Solitaire on the kits

Sokoban runs on Jentl's **puzzle kit** (`Jentl.Puzzle`): its rules, ice included, are an `IPuzzleRules`, and moves,
undo, redo and level files come from the kit. **Solitaire** has nine games on the **card
kit** (`Jentl.Cards`): Klondike, Easthaven (a Klondike and Spider cross: the stock deals a card onto every column, and
empty columns take anything), Forty Thieves (two decks, build down in suit one card at a time), FreeCell, Golf (clear the
columns onto the waste; the score is the cards left), Pyramid, Spider (1, 2 or 4 suits), TriPeaks and Yukon (move any
face-up card with everything on it). Drag cards or tap one to send it where it fits,
with undo, hints and automatic moves home. FreeCell deal numbers match Microsoft FreeCell (deal 1 is the classic deal 1).
[Jentl's guide](https://github.com/coenvk/jentl-cs/blob/main/docs/kits.md) explains how to build a new puzzle or card game with the kits.

## Mossbrook

**Mossbrook** is a small turn-based RPG on Jentl's **RPG kit** (`Jentl.Rpg`). Thieves have hidden
Elder Maren's amulet in the old cellar under the chapel ruins. Talk to the villagers, step inside their houses (each a small Tiled map
of its own, with a few things to find and Gran Hettie and Whiskers the cat to meet), pick up quests, get a sword from
Bram the smith, and go down: the cellar's two floors are generated from the game's seed (`--deal` sets it), dark, and
full of rats, slimes, spiders and skeletons. Spiders run on a state machine (Fluffy): they hunt you, flee when badly hurt, and rest out of sight to heal. Returning the amulet wins; two side quests give potions and armour.

Walk into people to talk and into monsters to fight; walking over items picks them up. Arrows/WASD or swipes step,
tapping a cell walks there, `.` or T waits a turn, I or B opens the bag (use potions, equip gear), J or Q the journal.

The game saves itself: on entering an area, opening or closing a conversation, the bag or the menu, every ten turns,
and on leaving. The title screen then offers Continue or New adventure. Falling in the cellar deletes the save, as in a
roguelike. Saves live in the `Mossbrook` folder on desktop and in `localStorage` in the browser.

The content is plain files in `apps/Mossbrook/Assets/rpg`: `items.json`, `actors.json`, `quests.json`,
`dialogue.json`, the village map `village.tmx`, the house interiors `house_*.tmx`, and their tileset `tiles.tsx`, all of which open in the
[Tiled](https://www.mapeditor.org) map editor. The tileset image is drawn by `tools/RpgArt`:

```sh
dotnet run --project tools/RpgArt -- apps/Mossbrook/Assets/rpg              # tiles.png and tiles.tsx
dotnet run --project tools/RpgArt -- apps/Mossbrook/Assets/rpg --preview x.png   # plus a 4x preview
```

`tests/Mossbrook.Tests` validates the content (every dialogue node, item, quest and map object referenced exists),
checks that every villager and the cellar's stairs and amulet can be reached, and plays the quests through.

## Code Lock

**Code Lock** is Mastermind with a combination lock: find the code in ten tries. After each try the
lock lights one green lamp for every digit in the right place and one amber lamp for every right digit in the wrong
place. Easy locks have 3 different digits, normal ones 4, and hard ones 5 that may repeat. Turn the wheels with the
arrows above and below them (or swipe them), or type digits; Enter tries the code. **Hint** sets the wheels to a code
that fits every answer so far, picked to split the remaining possibilities as evenly as possible (minimax, as in
Knuth's Mastermind strategy), and the status line says how many codes still fit. `--deal` picks the lock.

Each difficulty keeps its record in `codelock.json`, in the `CodeLock` folder (in `localStorage` in the browser): games
played and won, the fewest tries, and the current and longest winning streak. The difficulty menu shows them, and the
end screen points out a new best. Only finished locks count; one left half-cracked is neither a win nor a loss.

## Bricks

**Bricks** is a brick breaker built on Jentl's entity component system and physics (see
[Jentl's guide](https://github.com/coenvk/jentl-cs/blob/main/docs/kits.md)). Steer the paddle with the mouse, a finger, the arrow keys or a gamepad, and launch with a
tap, Enter or A. Where the ball lands on the paddle sets its angle. Bricks come in one-hit, two-hit (they crack) and
unbreakable kinds, over four walls that come round again faster; one brick in six drops a power-up to catch: **W**
widens the paddle, **M** splits each ball into three, **+** is an extra life. Three lives. `--deal` picks the seed,
and the debug overlay (F3) outlines every physics body.

## Uno

**Card Table > Uno** plays Uno against computer opponents (you and 1 to 3 bots) or with 2 to 4 people passing the
device around; with several people, hands stay hidden until the next player confirms they have the device. The
official basic rules: match the top card by colour or by number or symbol, or play a wild; Skip, Reverse (a Skip
with two players), Draw Two, Wild and Wild Draw Four (only without a card of the current colour). Tap Draw when
nothing fits; a drawn card that fits may be played straight away, or kept. Tap Uno! before playing your
second-to-last card, or draw two. The round's winner scores the cards left in the other hands, and the first to 500
wins the match. On a keyboard: Left/Right pick a card, Enter plays it, Down draws, Up calls Uno.

**Play online** (in the Uno menu) plays against people on other devices through a game server. Pick "New room" for 2
to 4 players and share the four-digit code it shows, or "Join a room" and type a friend's code; the host starts, and
bots fill any empty seats (and take over for anyone who leaves). Run a server with `dotnet run --project
src/GameServer` (port 5080); the app connects to `ws://localhost:5080/play` unless told otherwise: `--server
wss://example.org/play` or the `JENTL_SERVER` variable on the desktop, `?server=wss://example.org/play` in the page
address in the browser. See [Jentl's guide](https://github.com/coenvk/jentl-cs/blob/main/docs/kits.md) for how it works and how to host it.

The bots match colour before number, keep wilds for when nothing else fits, and attack with Skip, Reverse and Draw
Two when the next player is close to going out; in the tests they beat a random player well over half the time.

## Texas Hold'em

**Card Table > Texas Hold'em** is No-Limit Hold'em as a sit-and-go: 1,000 chips each, blinds from 10/20 doubling
every ten hands, and the last player with chips wins. Play against 1, 3 or 5 bots, or pass the device around between
2 to 4 people (hole cards stay hidden between turns). Fold, Check or Call, Bet or Raise (- and + step the amount by
the big blind), or All in; on a keyboard Enter checks or calls, Up raises, Left/Right change the amount and F folds.
Showdowns name the hands ("Two pair, sevens and threes"), and all-ins for different amounts make side pots.

The bots deal out the unknown cards a hundred and fifty times to estimate their chances, then weigh that against
the pot odds; each seat plays its own style, from tight and careful to loose and pushy, with the odd bluff. The
engine (`src/Poker`) is framework-free and runs on the same multiplayer layer as Uno.
