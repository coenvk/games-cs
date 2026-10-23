using System.Text;
using Sokoban.Model;

namespace Sokoban.Solver;

/// <param name="Width">Grid width including the outer wall.</param>
/// <param name="Height">Grid height including the outer wall.</param>
/// <param name="Crates">Number of crates (and goals).</param>
/// <param name="MinPushes">Reject levels the solver finishes in fewer pushes than this.</param>
/// <param name="Attempts">Candidates to build; the one needing the most pushes wins.</param>
/// <param name="ReverseSteps">Random reverse moves used to scramble the crates off their goals.</param>
/// <param name="IceCells">Ice tiles to add in small patches; levels whose solution never slides are rejected.</param>
public sealed record GeneratorOptions(
    int Width = 10,
    int Height = 9,
    int Crates = 3,
    int MinPushes = 8,
    int Attempts = 40,
    int ReverseSteps = 500,
    int IceCells = 0);

/// <summary>
/// Generates levels by playing backwards: crates start on their goals and the player walks
/// around randomly, sometimes pulling a crate. Undoing those pulls as pushes solves the level,
/// so every level produced is solvable; the solver then measures how hard it is.
/// </summary>
public static class LevelGenerator
{
    private static readonly Direction[] Directions = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

    /// <summary>The hardest of <see cref="GeneratorOptions.Attempts"/> candidates, or null if none was hard enough.</summary>
    public static Level? Generate(GeneratorOptions options, Random random, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(random);
        Level? best = null;
        var bestPushes = options.MinPushes - 1;
        for (var attempt = 0; attempt < options.Attempts && !cancellation.IsCancellationRequested; attempt++)
        {
            if (TryBuild(options, random) is not { } candidate)
            {
                continue;
            }

            var result = new Solver(candidate.Board).Solve(
                new LevelSession(candidate),
                new SolverOptions(MaxNodes: 50_000),
                cancellation);
            if (result.Status == SolveStatus.Solved && result.Pushes > bestPushes
                && (options.IceCells == 0 || result.Moves.Any(m => m.Slide > 0)))
            {
                best = candidate;
                bestPushes = result.Pushes;
            }
        }

        return best;
    }

#pragma warning disable CA5394 // Level layout randomness, not security relevant.
    private static Level? TryBuild(GeneratorOptions options, Random random)
    {
        int width = options.Width, height = options.Height;
        var floor = CarveRoom(width, height, random);
        var cells = Enumerable.Range(0, width * height).Where(i => floor[i]).ToList();
        if (cells.Count < options.Crates * 3 + 1)
        {
            return null;
        }

        // Crates start on their goals; the player anywhere else.
        var shuffled = cells.OrderBy(_ => random.Next()).ToList();
        var goals = shuffled.Take(options.Crates).ToHashSet();
        var crates = new HashSet<int>(goals);
        var player = shuffled[options.Crates];

        for (var step = 0; step < options.ReverseSteps; step++)
        {
            var direction = Directions[random.Next(Directions.Length)];
            if (!TryStep(player, direction, width, height, out var next) || !floor[next] || crates.Contains(next))
            {
                continue;
            }

            // Pull: the crate behind the player follows it into its old cell.
            if (TryStep(player, direction.Opposite(), width, height, out var behind) && crates.Contains(behind) && random.Next(3) > 0)
            {
                crates.Remove(behind);
                crates.Add(player);
            }

            player = next;
        }

        // Scrambling should leave (almost) every crate off its goal.
        if (crates.Count(c => !goals.Contains(c)) < options.Crates - 1)
        {
            return null;
        }

        var ice = PlaceIce(options.IceCells, width, floor, cells, goals, crates, player, random);
        return Xsb.ParseLevel(ToXsb(width, height, floor, goals, crates, player, ice));
    }

    /// <summary>
    /// Grows patches of ice on empty floor (never under a goal, crate or the player), so slides are
    /// a few cells long rather than single scattered tiles.
    /// </summary>
    private static HashSet<int> PlaceIce(
        int count,
        int width,
        bool[] floor,
        List<int> cells,
        HashSet<int> goals,
        HashSet<int> crates,
        int player,
        Random random)
    {
        var ice = new HashSet<int>();
        var free = cells.Where(c => !goals.Contains(c) && !crates.Contains(c) && c != player).ToList();
        for (var patches = 0; ice.Count < count && free.Count > 0 && patches < count; patches++)
        {
            var patch = new List<int> { free[random.Next(free.Count)] };
            var size = random.Next(2, 5);
            for (var grown = 0; grown < size * 4 && patch.Count < size && ice.Count + patch.Count < count; grown++)
            {
                var from = patch[random.Next(patch.Count)];
                var direction = Directions[random.Next(Directions.Length)];
                if (TryStep(from, direction, width, floor.Length / width, out var next) && floor[next]
                    && free.Contains(next) && !patch.Contains(next))
                {
                    patch.Add(next);
                }
            }

            foreach (var cell in patch)
            {
                ice.Add(cell);
                free.Remove(cell);
            }
        }

        return ice;
    }

    /// <summary>A connected room made of overlapping random rectangles inside an outer wall.</summary>
    private static bool[] CarveRoom(int width, int height, Random random)
    {
        var floor = new bool[width * height];
        var interior = (width - 2) * (height - 2);
        var carved = 0;
        for (var tries = 0; tries < 200 && carved < interior * 0.55; tries++)
        {
            var w = random.Next(2, 5);
            var h = random.Next(2, 4);
            var left = random.Next(1, Math.Max(2, width - w));
            var top = random.Next(1, Math.Max(2, height - h));
            var right = Math.Min(left + w, width - 1);
            var bottom = Math.Min(top + h, height - 1);

            // After the first rectangle, only accept ones that touch the room, so it stays connected.
            if (carved > 0 && !Touches(floor, width, left, top, right, bottom))
            {
                continue;
            }

            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    if (!floor[y * width + x])
                    {
                        floor[y * width + x] = true;
                        carved++;
                    }
                }
            }
        }

        return floor;
    }

    private static bool Touches(bool[] floor, int width, int left, int top, int right, int bottom)
    {
        for (var y = top - 1; y <= bottom; y++)
        {
            for (var x = left - 1; x <= right; x++)
            {
                var inside = x >= left && x < right && y >= top && y < bottom;
                var edge = (x >= left && x < right) || (y >= top && y < bottom);
                if ((inside || edge) && floor[y * width + x])
                {
                    return true;
                }
            }
        }

        return false;
    }
#pragma warning restore CA5394

    private static bool TryStep(int cell, Direction direction, int width, int height, out int next)
    {
        int x = cell % width, y = cell / width;
        (x, y) = direction switch
        {
            Direction.Up => (x, y - 1),
            Direction.Down => (x, y + 1),
            Direction.Left => (x - 1, y),
            _ => (x + 1, y),
        };
        next = y * width + x;
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    /// <summary>Walls only where they touch the room (including diagonally), so the level has a clean outline.</summary>
    private static string ToXsb(int width, int height, bool[] floor, HashSet<int> goals, HashSet<int> crates, int player, HashSet<int> ice)
    {
        bool NearFloor(int x, int y)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < width && ny < height && floor[ny * width + nx])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        var sb = new StringBuilder();
        for (var y = 0; y < height; y++)
        {
            var row = new StringBuilder(width);
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                row.Append(
                    !floor[i] ? (NearFloor(x, y) ? '#' : ' ')
                    : i == player ? (goals.Contains(i) ? '+' : '@')
                    : crates.Contains(i) ? (goals.Contains(i) ? '*' : '$')
                    : goals.Contains(i) ? '.'
                    : ice.Contains(i) ? '~'
                    : ' ');
            }

            var line = row.ToString().TrimEnd();
            if (line.Length > 0)
            {
                sb.Append(line).Append('\n');
            }
        }

        return sb.ToString();
    }
}
