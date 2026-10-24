using Jentl.Core.Tracks;
using Microsoft.Xna.Framework;
using Paddock;

namespace Games.Race;

/// <summary>
/// Scenery around a painted road: speckled ground, and trees, rocks, buildings or dunes away from the road,
/// depending on the track and the weather. The same track always gets the same scenery.
/// </summary>
internal static class TrackScenery
{
    private const int Clearance = 14;

    public static void Decorate(Color[] pixels, Rectangle bounds, RoadStyle style, TrackDef track, Condition condition)
    {
        var random = new Random(Jentl.Collect.StableSeed.Of(track.Id));
        var (width, height) = (bounds.Width, bounds.Height);
        bool IsGround(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && pixels[(y * width) + x] == style.Ground;

        // Whether a spot is clear of the road by a margin (checked on a coarse grid of the road painted so far).
        var near = NearRoad(pixels, width, height, style.Ground);
        bool Open(int x, int y, int radius) =>
            x - radius >= 0 && y - radius >= 0 && x + radius < width && y + radius < height
            && !near[((y / 8) * ((width / 8) + 1)) + (x / 8)];

        // Speckles in two shades of the ground.
        for (var i = 0; i < width * height / 40; i++)
        {
            var (x, y) = (random.Next(width), random.Next(height));
            if (IsGround(x, y))
            {
                var shade = random.Next(2) == 0 ? Darker(style.Ground, 0.08f) : Lighter(style.Ground, 0.06f);
                Fill(pixels, width, height, x, y, 2, 2, shade);
            }
        }

        // The track's own scenery.
        var count = width * height / 2500;
        for (var i = 0; i < count; i++)
        {
            var (x, y) = (random.Next(width), random.Next(height));
            switch (track.Kind, condition)
            {
                case (TrackKind.City, _) when Open(x, y, 18):
                    var (w, h) = (16 + random.Next(20), 16 + random.Next(20));
                    var roof = new[] { new Color(150, 84, 70), new Color(96, 104, 120), new Color(170, 160, 140) }[random.Next(3)];
                    Fill(pixels, width, height, x - (w / 2), y - (h / 2), w, h, Darker(roof, 0.35f));
                    Fill(pixels, width, height, x - (w / 2) + 2, y - (h / 2) + 2, w - 4, h - 4, roof);
                    break;
                case (_, Condition.Sand) when Open(x, y, 10):
                    Blob(pixels, width, height, x, y, 8 + random.Next(6), 4, Darker(style.Ground, 0.1f));
                    break;
                case (TrackKind.OffRoad or TrackKind.HillClimb, _) when random.Next(3) == 0 && Open(x, y, 6):
                    Blob(pixels, width, height, x, y, 3 + random.Next(3), 3, new Color(120, 116, 110));
                    break;
                case (_, _) when condition != Condition.Ice && Open(x, y, 8):
                    var leaves = condition == Condition.Snow ? new Color(46, 84, 60) : new Color(31, 67, 32);
                    Blob(pixels, width, height, x, y, 6, 6, leaves);
                    Blob(pixels, width, height, x - 1, y - 2, 3, 3, condition == Condition.Snow ? new Color(236, 241, 246) : Lighter(leaves, 0.25f));
                    break;
            }
        }

        // Puddles: on a wet track, sky-blue pools with a shine on the road, the verge and the grass; on dirt, brown mud.
        if (condition is Condition.Wet or Condition.Dirt)
        {
            var road = condition == Condition.Wet ? new Color(98, 126, 168) : new Color(96, 70, 46);
            var shine = condition == Condition.Wet ? new Color(170, 196, 230) : new Color(140, 110, 80);
            var ground = condition == Condition.Wet ? new Color(70, 104, 120) : new Color(84, 64, 44);
            for (var i = 0; i < width * height / 700; i++)
            {
                var (x, y) = (random.Next(width), random.Next(height));
                var here = pixels[(y * width) + x];
                var onRoad = here == style.Surface || here == style.Verge;
                if (!onRoad && (here != style.Ground || random.Next(3) != 0))
                {
                    continue;
                }

                var (rx, ry) = (5 + random.Next(7), 2 + random.Next(3));
                Blob(pixels, width, height, x, y, rx, ry, onRoad ? road : ground);
                Fill(pixels, width, height, x - (rx / 2), y - (ry / 2), Math.Max(2, rx / 2), 1, shine);
            }
        }
    }

    private static bool[] NearRoad(Color[] pixels, int width, int height, Color ground)
    {
        var columns = (width / 8) + 1;
        var near = new bool[columns * ((height / 8) + 1)];
        for (var y = 0; y < height; y += 2)
        {
            for (var x = 0; x < width; x += 2)
            {
                if (pixels[(y * width) + x] != ground)
                {
                    for (var dy = -Clearance; dy <= Clearance; dy += 8)
                    {
                        for (var dx = -Clearance; dx <= Clearance; dx += 8)
                        {
                            var (cx, cy) = ((x + dx) / 8, (y + dy) / 8);
                            if (cx >= 0 && cy >= 0 && cx < columns && cy <= height / 8)
                            {
                                near[(cy * columns) + cx] = true;
                            }
                        }
                    }
                }
            }
        }

        return near;
    }

    private static void Fill(Color[] pixels, int width, int height, int x, int y, int w, int h, Color color)
    {
        for (var py = Math.Max(0, y); py < Math.Min(height, y + h); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(width, x + w); px++)
            {
                pixels[(py * width) + px] = color;
            }
        }
    }

    private static void Blob(Color[] pixels, int width, int height, int cx, int cy, int rx, int ry, Color color)
    {
        for (var y = -ry; y <= ry; y++)
        {
            for (var x = -rx; x <= rx; x++)
            {
                if (((x * x) / (float)(rx * rx)) + ((y * y) / (float)(ry * ry)) <= 1)
                {
                    Fill(pixels, width, height, cx + x, cy + y, 1, 1, color);
                }
            }
        }
    }

    private static Color Darker(Color c, float t) => Color.Lerp(c, Color.Black, t);

    private static Color Lighter(Color c, float t) => Color.Lerp(c, Color.White, t);
}
