// Procedurally draws the pixel-art tileset for the RPG demo and writes it as a PNG
// plus a Tiled tileset (.tsx) carrying each tile's name, solid and opaque flags, lights, the
// characters' walk frames and the in-place tile animations.
// The output is deterministic: every tile is drawn with its own fixed seed.
//
// Usage: RpgArt <output-dir> [--preview <path>]
//   --preview writes a 4x nearest-neighbour enlargement on a dark background.

using System.Globalization;
using System.Text;
using RpgArt;

if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--preview"))
{
    Console.Error.WriteLine("Usage: RpgArt <output-dir> [--preview <path>]");
    return 1;
}

var outputDir = args[0];
var previewPath = args.Length == 3 ? args[2] : null;

const int size = Sprite.Size;
const int width = Tiles.Columns * size;
const int height = Tiles.Rows * size;

var sheet = new Rgba[width * height];
for (var id = 0; id < Tiles.All.Count; id++)
{
    if (Tiles.All[id] is not { } tile)
    {
        continue;
    }

    var sprite = new Sprite();
    tile.Draw(sprite, Tiles.Seed(id));
    var (left, top) = (id % Tiles.Columns * size, id / Tiles.Columns * size);
    for (var y = 0; y < size; y++)
    {
        for (var x = 0; x < size; x++)
        {
            sheet[((top + y) * width) + left + x] = sprite[x, y];
        }
    }
}

Directory.CreateDirectory(outputDir);
File.WriteAllBytes(Path.Combine(outputDir, "tiles.png"), Png.Encode(width, height, sheet));
File.WriteAllText(Path.Combine(outputDir, "tiles.tsx"), Tsx(width, height), new UTF8Encoding(false));
Console.WriteLine($"Wrote {Tiles.All.Count(tile => tile is not null)} tiles to {outputDir}.");

if (previewPath is not null)
{
    File.WriteAllBytes(previewPath, Png.Encode(width * 4, height * 4, Preview(sheet, 4)));
    Console.WriteLine($"Wrote preview to {previewPath}.");
}

return 0;

static string Tsx(int width, int height)
{
    var xml = new StringBuilder();
    var c = CultureInfo.InvariantCulture;
    xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
    xml.Append(c, $"<tileset version=\"1.10\" tiledversion=\"1.10.2\" name=\"tiles\" tilewidth=\"{Sprite.Size}\" tileheight=\"{Sprite.Size}\" tilecount=\"{Tiles.Columns * Tiles.Rows}\" columns=\"{Tiles.Columns}\">\n");
    xml.Append(c, $" <image source=\"tiles.png\" width=\"{width}\" height=\"{height}\"/>\n");
    for (var id = 0; id < Tiles.All.Count; id++)
    {
        if (Tiles.All[id] is not { } tile)
        {
            continue;
        }

        xml.Append(c, $" <tile id=\"{id}\">\n  <properties>\n");
        xml.Append(c, $"   <property name=\"name\" value=\"{tile.Name}\"/>\n");
        if (tile.Solid)
        {
            xml.Append("   <property name=\"solid\" type=\"bool\" value=\"true\"/>\n");
        }

        if (tile.Opaque)
        {
            xml.Append("   <property name=\"opaque\" type=\"bool\" value=\"true\"/>\n");
        }

        if (tile.Walk is { } walk)
        {
            _ = Tiles.Id(walk); // Fails on a typo rather than writing a dangling name.
            xml.Append(c, $"   <property name=\"walk\" value=\"{walk}\"/>\n");
        }

        if (tile.Light is { } light)
        {
            xml.Append(c, $"   <property name=\"light\" type=\"float\" value=\"{light.Radius}\"/>\n");
            xml.Append(c, $"   <property name=\"light_color\" value=\"{light.Color}\"/>\n");
        }

        xml.Append("  </properties>\n");
        if (tile.Animation is { } frames)
        {
            xml.Append("  <animation>\n");
            foreach (var (frame, duration) in frames)
            {
                xml.Append(c, $"   <frame tileid=\"{Tiles.Id(frame)}\" duration=\"{duration}\"/>\n");
            }

            xml.Append("  </animation>\n");
        }

        xml.Append(" </tile>\n");
    }

    xml.Append("</tileset>\n");
    return xml.ToString();
}

// Enlarges the sheet over a dark checkerboard (one shade per tile) so tile bounds stay visible.
static Rgba[] Preview(Rgba[] sheet, int scale)
{
    const int width = Tiles.Columns * Sprite.Size;
    var (bigWidth, bigHeight) = (width * scale, sheet.Length / width * scale);
    var dark = Rgba.Hex(0x1c1a24);
    var darker = Rgba.Hex(0x16141c);
    var big = new Rgba[bigWidth * bigHeight];
    for (var y = 0; y < bigHeight; y++)
    {
        for (var x = 0; x < bigWidth; x++)
        {
            var (sx, sy) = (x / scale, y / scale);
            var checker = ((sx / Sprite.Size) + (sy / Sprite.Size)) % 2 == 0 ? dark : darker;
            big[(y * bigWidth) + x] = checker.Over(sheet[(sy * width) + sx]);
        }
    }

    return big;
}
