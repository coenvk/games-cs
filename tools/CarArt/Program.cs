// Draws every car of Paddock's roster in code, a 64x24 side view (cards, drag races) and an 18x10 top view
// (circuits), and packs them into one atlas: side/<id> and top/<id>. Deterministic: the same roster draws the same.
//
// Usage: CarArt <cars.json> <output-dir> [--preview <path>] [--preview3d <path>]
//   writes <output-dir>/cars.png and cars.atlas.json; --preview writes every car 3x on a dark background, --preview3d
//   every car's 3D model (Paddock.CarModels) from three-quarters in front, drawn by Jentl's software renderer.

using CarArt;
using Jentl.Assets;
using Paddock;

var options = args.Skip(2).Chunk(2).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "");
if (args.Length < 2 || args.Length % 2 != 0 || options.Keys.Any(k => k is not ("--preview" or "--preview3d")))
{
    Console.Error.WriteLine("Usage: CarArt <cars.json> <output-dir> [--preview <path>] [--preview3d <path>]");
    return 1;
}

var roster = Roster.FromJson(File.ReadAllText(args[0]), _ => 0);
var sprites = new List<(string Name, Image Image)>();
foreach (var car in roster.All)
{
    sprites.Add(($"side/{car.Id}", Painter.Side(car).ToImage()));
    sprites.Add(($"top/{car.Id}", Painter.Top(car).ToImage()));
}

var (sheet, description) = AtlasBuilder.Build(sprites, "cars.png");
File.WriteAllBytes(Path.Combine(args[1], "cars.png"), Png.Encode(sheet));
File.WriteAllText(Path.Combine(args[1], "cars.atlas.json"), description.ToJson());
Console.WriteLine($"Drew {roster.All.Count} cars into {Path.Combine(args[1], "cars.png")} ({sheet.Width}x{sheet.Height}).");

if (options.TryGetValue("--preview3d", out var preview3d))
{
    File.WriteAllBytes(preview3d, Preview3D.Draw(roster.All).ToPng());
    Console.WriteLine($"Drew the 3D cars into {preview3d}.");
}

if (options.TryGetValue("--preview", out var previewPath))
{
    const int scale = 3;
    const int columns = 5;
    var cell = (W: (Painter.SideWidth + Painter.TopWidth + 6) * scale, H: (Painter.SideHeight + 4) * scale);
    var rows = (roster.All.Count + columns - 1) / columns;
    var preview = new PixelCanvas(cell.W * columns, cell.H * rows);
    preview.Fill(Rgba.Hex(0x1c2130));
    for (var i = 0; i < roster.All.Count; i++)
    {
        var (x, y) = (i % columns * cell.W, i / columns * cell.H);
        Blow(preview, Painter.Side(roster.All[i]), x + (2 * scale), y + (2 * scale));
        Blow(preview, Painter.Top(roster.All[i]), x + ((Painter.SideWidth + 4) * scale), y + (8 * scale));
    }

    File.WriteAllBytes(previewPath, preview.ToPng());
}

return 0;

static void Blow(PixelCanvas target, PixelCanvas source, int left, int top)
{
    const int scale = 3;
    for (var y = 0; y < source.Height; y++)
    {
        for (var x = 0; x < source.Width; x++)
        {
            if (!source[x, y].IsClear)
            {
                target.Rect(left + (x * scale), top + (y * scale), scale, scale, source[x, y]);
            }
        }
    }
}
