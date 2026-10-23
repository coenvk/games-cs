// Converts levels from the old Java Sokoban (github.com/coenvk/Sokoban) to an XSB pack.
// The old format is one file per level, named by its number, holding a grid of
// space-separated tile codes (see LevelConstants.java in the old project).
//
// Usage: LevelConverter <old-levels-dir> <output.xsb>

using System.Globalization;
using System.Text;
using Sokoban.Model;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: LevelConverter <old-levels-dir> <output.xsb>");
    return 1;
}

var (inputDir, outputFile) = (args[0], args[1]);

var files = Directory.GetFiles(inputDir)
    .Select(path => (Path: path, Number: int.TryParse(Path.GetFileName(path), CultureInfo.InvariantCulture, out var n) ? n : -1))
    .Where(f => f.Number >= 0)
    .OrderBy(f => f.Number)
    .ToList();

var pack = new StringBuilder();
foreach (var (path, number) in files)
{
    var map = new StringBuilder();
    foreach (var line in File.ReadAllLines(path).Where(l => l.Trim().Length > 0))
    {
        var codes = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        map.Append(string.Concat(codes.Select(ToXsb)).TrimEnd()).Append('\n');
    }

    var title = $"Level {number}";

    // Round-trip through the parser so every converted level is validated.
    var level = Xsb.ParseLevel(map.ToString());
    pack.Append("; ").Append(title).Append("\n\n").Append(Xsb.Write(level)).Append('\n');
}

File.WriteAllText(outputFile, pack.ToString());
Console.WriteLine($"Converted {files.Count} levels to {outputFile}.");
return 0;

static char ToXsb(string code) => code switch
{
    "0" => ' ', // NONE (outside the level)
    "1" => ' ', // FLOOR
    "2" => '#', // WALL
    "3" => '.', // AREA (goal)
    "4" => '$', // CRATE
    "5" => '@', // PLAYER
    "6" => '*', // CRATE_AREA
    "7" => '+', // PLAYER_AREA
    _ => throw new InvalidLevelException($"Unknown tile code '{code}'."),
};
