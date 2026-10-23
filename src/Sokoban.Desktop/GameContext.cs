using Jentl.Core.Input;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Model;

namespace Sokoban.Desktop;

/// <summary>Shared game services handed to every scene.</summary>
internal sealed record GameContext(IReadOnlyList<Level> Levels, Tileset Tileset, ActionMap<GameAction> Actions);
