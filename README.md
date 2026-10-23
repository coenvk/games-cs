# Sokoban

A C# rewrite of [coenvk/Sokoban](https://github.com/coenvk/Sokoban), with engine ideas from
[coenvk/Jentl](https://github.com/coenvk/Jentl). Rendering will use MonoGame.

## Layout

| Path | What |
|---|---|
| `src/Sokoban.Model` | Pure game logic, no rendering: `Board`, `Level`, `LevelSession` (moves, undo/redo), `Xsb` parser/writer |
| `tests/Sokoban.Model.Tests` | xUnit tests for the model |
| `tools/LevelConverter` | One-shot converter from the old Java level format to XSB |
| `levels/original.xsb` | The 100 levels from the Java version |

## Build

```bash
dotnet build
dotnet test
```

## Roadmap

1. ~~Model, XSB parser, level conversion~~
2. MonoGame play scene: grid rendering, movement, undo/redo, win check
3. Engine layer (`Jentl.Core`): scene stack, transitions, input action map
4. Menus, level select, pause, completion screen
5. Saved progress (best moves/pushes/time)
6. Polish: tweened movement, animation, sound
7. Solver (hints, deadlock detection), level generator, twist mechanic
