using System.Text;

namespace Sokoban.Model;

/// <summary>
/// Reads and writes the standard XSB level format.
/// <c>#</c> wall, <c>@</c> player, <c>+</c> player on goal, <c>$</c> crate, <c>*</c> crate on goal,
/// <c>.</c> goal, and space, <c>-</c> or <c>_</c> floor.
/// </summary>
public static class Xsb
{
    private const string MapChars = "#@+$*.-_ ";

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
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var levels = new List<Level>();
        string? pendingTitle = null;

        for (var i = 0; i < lines.Length;)
        {
            if (!IsMapLine(lines[i]))
            {
                var line = lines[i].Trim();
                if (line.StartsWith(';'))
                {
                    pendingTitle = line.TrimStart(';').Trim();
                }
                else if (line.Length > 0 && !IsMetadataLine(line))
                {
                    pendingTitle = line;
                }

                i++;
                continue;
            }

            var start = i;
            while (i < lines.Length && IsMapLine(lines[i]))
            {
                i++;
            }

            var title = FindTitleLine(lines, i) ?? pendingTitle;
            pendingTitle = null;
            try
            {
                levels.Add(ParseMap(lines.AsSpan(start, i - start), title));
            }
            catch (InvalidLevelException e)
            {
                throw new InvalidLevelException($"Level at line {start + 1}: {e.Message}", e);
            }
        }

        return levels;
    }

    /// <summary>Writes the level in its starting position.</summary>
    public static string Write(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var crates = level.CrateStarts.ToHashSet();
        return Write(level.Board, level.PlayerStart, crates.Contains);
    }

    /// <summary>Writes the session's current position.</summary>
    public static string Write(LevelSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Write(session.Board, session.Player, session.HasCrate);
    }

    private static string Write(Board board, int player, Func<int, bool> hasCrate)
    {
        var sb = new StringBuilder();
        var row = new StringBuilder(board.Width);
        for (var y = 0; y < board.Height; y++)
        {
            row.Clear();
            for (var x = 0; x < board.Width; x++)
            {
                var i = board.IndexOf(x, y);
                var goal = board.IsGoal(i);
                row.Append(
                    board.IsWall(i) ? '#'
                    : i == player ? (goal ? '+' : '@')
                    : hasCrate(i) ? (goal ? '*' : '$')
                    : goal ? '.'
                    : ' ');
            }

            sb.Append(row.ToString().TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    private static bool IsMapLine(string line) =>
        line.Contains('#', StringComparison.Ordinal) && line.TrimEnd().All(c => MapChars.Contains(c, StringComparison.Ordinal));

    /// <summary>Lines like <c>Title: ...</c> or <c>Author: ...</c>.</summary>
    private static bool IsMetadataLine(string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && line[..colon].All(char.IsLetter);
    }

    private static string? FindTitleLine(string[] lines, int from)
    {
        for (var i = from; i < lines.Length && !IsMapLine(lines[i]); i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            {
                return line["Title:".Length..].Trim();
            }
        }

        return null;
    }

    private static Level ParseMap(ReadOnlySpan<string> rows, string? title)
    {
        var height = rows.Length;
        var width = 0;
        foreach (var row in rows)
        {
            width = Math.Max(width, row.TrimEnd().Length);
        }

        var walls = new List<int>();
        var goals = new List<int>();
        var crates = new List<int>();
        int? player = null;

        for (var y = 0; y < height; y++)
        {
            var row = rows[y].TrimEnd();
            for (var x = 0; x < row.Length; x++)
            {
                var i = y * width + x;
                switch (row[x])
                {
                    case '#':
                        walls.Add(i);
                        break;
                    case '.':
                        goals.Add(i);
                        break;
                    case '$':
                        crates.Add(i);
                        break;
                    case '*':
                        crates.Add(i);
                        goals.Add(i);
                        break;
                    case '+':
                        goals.Add(i);
                        goto case '@';
                    case '@':
                        if (player is not null)
                        {
                            throw new InvalidLevelException("Level has more than one player.");
                        }

                        player = i;
                        break;
                }
            }
        }

        if (player is null)
        {
            throw new InvalidLevelException("Level has no player.");
        }

        return Level.Create(width, height, walls, goals, player.Value, crates, title);
    }
}
