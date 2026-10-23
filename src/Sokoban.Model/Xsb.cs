using Jentl.Puzzle;

namespace Sokoban.Model;

/// <summary>
/// Reads and writes the standard XSB level format, through the Jentl puzzle kit's level files with
/// <see cref="SokobanRules.Legend"/>.
/// <c>#</c> wall, <c>@</c> player, <c>+</c> player on goal, <c>$</c> crate, <c>*</c> crate on goal,
/// <c>.</c> goal, and space, <c>-</c> or <c>_</c> floor.
/// Extension for ice: <c>~</c> ice, <c>=</c> crate on ice, <c>&amp;</c> player on ice (ice never holds a goal).
/// </summary>
public static class Xsb
{
    /// <summary>Parses text containing exactly one level.</summary>
    public static Level ParseLevel(string text)
    {
        var levels = ParsePack(text);
        return levels.Count == 1
            ? levels[0]
            : throw new InvalidLevelException($"Expected one level, found {levels.Count}.");
    }

    /// <summary>
    /// Parses a level pack: maps separated by non-map lines. A level's title is taken from a
    /// <c>Title:</c> line after its map, otherwise from the last comment or text line before it.
    /// </summary>
    public static IReadOnlyList<Level> ParsePack(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<LevelData> maps;
        try
        {
            maps = LevelFile.ReadPack(text, SokobanRules.Legend);
        }
        catch (FormatException e)
        {
            throw new InvalidLevelException(e.Message, e);
        }

        return maps.Select((map, i) => ToLevel(map, i)).ToArray();
    }

    /// <summary>Writes the level in its starting position.</summary>
    public static string Write(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return LevelFile.Write(SokobanRules.ToBoard(level), SokobanRules.Legend);
    }

    /// <summary>Writes the session's current position.</summary>
    public static string Write(LevelSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return LevelFile.Write(session.PuzzleBoard, SokobanRules.Legend);
    }

    /// <summary>Validates a kit board as a Sokoban level: exactly one player, matching crates and goals, enclosed.</summary>
    private static Level ToLevel(LevelData map, int index)
    {
        var board = map.Board;
        int Index(Cell cell) => cell.Y * board.Width + cell.X;

        try
        {
            var players = board.PiecesOf(SokobanRules.Player).ToArray();
            if (players.Length == 0)
            {
                throw new InvalidLevelException("Level has no player.");
            }

            if (players.Length > 1)
            {
                throw new InvalidLevelException("Level has more than one player.");
            }

            return Level.Create(
                board.Width,
                board.Height,
                board.CellsOf(SokobanRules.Wall).Select(Index),
                board.CellsOf(SokobanRules.Goal).Select(Index),
                Index(players[0].Position),
                board.PiecesOf(SokobanRules.Crate).Select(p => Index(p.Position)),
                map.Title,
                board.CellsOf(SokobanRules.Ice).Select(Index));
        }
        catch (InvalidLevelException e)
        {
            throw new InvalidLevelException($"Level {index + 1}{(map.Title is null ? "" : $" ({map.Title})")}: {e.Message}", e);
        }
    }
}
