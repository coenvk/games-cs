// Solver and generator workbench.
//
// Usage:
//   SolverBench solve <pack.xsb> [max-nodes] [weight] [max-seconds]
//       Runs the solver over a pack and reports, per level, whether it solved it and how hard it worked.
//   SolverBench generate <count> [seed] [width] [height] [crates] [min-pushes] [ice-cells]
//       Generates levels and prints them as an XSB pack (stats on stderr).

using System.Diagnostics;
using System.Globalization;
using Sokoban.Model;
using Sokoban.Solver;

int Arg(int index, int fallback) =>
    args.Length > index ? int.Parse(args[index], CultureInfo.InvariantCulture) : fallback;

switch (args.FirstOrDefault())
{
    case "solve" when args.Length is >= 2 and <= 5:
        return Solve(args[1], new SolverOptions(
            MaxNodes: Arg(2, SolverOptions.Default.MaxNodes),
            Weight: Arg(3, SolverOptions.Default.Weight),
            MaxTime: args.Length > 4 ? TimeSpan.FromSeconds(Arg(4, 0)) : null));
    case "generate" when args.Length is >= 2 and <= 8:
        var defaults = new GeneratorOptions();
        return Generate(
            Arg(1, 1),
            Arg(2, 1),
            defaults with
            {
                Width = Arg(3, defaults.Width),
                Height = Arg(4, defaults.Height),
                Crates = Arg(5, defaults.Crates),
                MinPushes = Arg(6, defaults.MinPushes),
                IceCells = Arg(7, defaults.IceCells),
            });
    default:
        Console.Error.WriteLine("Usage: SolverBench solve <pack.xsb> [max-nodes] [weight] [max-seconds]");
        Console.Error.WriteLine("       SolverBench generate <count> [seed] [width] [height] [crates] [min-pushes] [ice-cells]");
        return 1;
}

static int Solve(string pack, SolverOptions options)
{
    var levels = Xsb.ParsePack(File.ReadAllText(pack));
    var solved = 0;
    var total = Stopwatch.StartNew();

    Console.WriteLine($"{"Level",-12} {"Status",-13} {"Pushes",7} {"Moves",7} {"Nodes",9} {"Time",9}");
    for (var i = 0; i < levels.Count; i++)
    {
        var level = levels[i];
        var watch = Stopwatch.StartNew();
        var result = new Solver(level.Board).Solve(new LevelSession(level), options);
        watch.Stop();

        if (result.Status == SolveStatus.Solved)
        {
            solved++;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{level.Title ?? $"#{i + 1}",-12} {result.Status,-13} {result.Pushes,7} {result.Moves.Count,7} {result.Explored,9} {watch.Elapsed.TotalMilliseconds,7:0}ms  {Lurd(result.Moves)}"));
    }

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"Solved {solved}/{levels.Count} in {total.Elapsed.TotalSeconds:0.0}s (budget {options.MaxNodes} nodes, weight {options.Weight})."));
    return 0;
}

static string Lurd(IEnumerable<Move> moves) => string.Concat(moves.Select(m => m.Direction.ToLurd(m.IsPush)));

static int Generate(int count, int seed, GeneratorOptions options)
{
    var random = new Random(seed);
    var made = 0;
    for (var i = 0; i < count; i++)
    {
        var watch = Stopwatch.StartNew();
        var level = LevelGenerator.Generate(options, random);
        watch.Stop();
        if (level is null)
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"#{i + 1}: nothing hard enough ({watch.Elapsed.TotalMilliseconds:0}ms)"));
            continue;
        }

        made++;
        var pushes = new Solver(level.Board).Solve(new LevelSession(level)).Pushes;
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"#{i + 1}: {pushes} pushes ({watch.Elapsed.TotalMilliseconds:0}ms)"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"; Generated {i + 1} ({pushes} pushes)\n"));
        Console.WriteLine(Xsb.Write(level));
    }

    Console.Error.WriteLine($"Generated {made}/{count}.");
    return 0;
}
