using System.Numerics;
using Jentl.Raster;

namespace Games.Scenes;

/// <summary>Small textures drawn in code for the 3D demo.</summary>
internal static class DemoTextures
{
    /// <summary>Two greens in an 8 by 8 checker.</summary>
    public static Texture Grass()
    {
        uint light = Colors.Pack(new Vector4(0.36f, 0.62f, 0.33f, 1)), dark = Colors.Pack(new Vector4(0.27f, 0.5f, 0.26f, 1));
        return Make(64, 64, (x, y) => ((x / 8) + (y / 8)) % 2 == 0 ? light : dark);
    }

    /// <summary>A wooden crate face: planks with dark seams and a frame.</summary>
    public static Texture Crate() => Make(32, 32, (x, y) =>
    {
        var frame = x < 3 || y < 3 || x > 28 || y > 28 || Math.Abs(x - y) < 2;
        var seam = y % 8 == 0;
        var shade = frame ? 0.45f : seam ? 0.35f : 0.6f + (((x * 7) + (y * 3)) % 5 * 0.02f);
        return Colors.Pack(new Vector4(shade, shade * 0.72f, shade * 0.42f, 1));
    });

    /// <summary>A car's side, across its outline: red paint, windows along the cabin, a pale stripe.</summary>
    public static Texture CarSide() => Make(64, 32, (x, y) =>
    {
        var u = x / 64f;
        var v = y / 32f;
        if (v > 0.08f && v < 0.42f && u > 0.18f && u < 0.75f && Math.Abs(u - 0.47f) > 0.02f)
        {
            return Colors.Pack(new Vector4(0.62f, 0.82f, 0.95f, 1));
        }

        return v > 0.62f && v < 0.68f ? Colors.Pack(new Vector4(0.95f, 0.9f, 0.85f, 1)) : Colors.Pack(new Vector4(0.85f, 0.2f, 0.15f, 1));
    });

    private static Texture Make(int width, int height, Func<int, int, uint> pixel)
    {
        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = pixel(x, y);
            }
        }

        return new Texture(width, height, pixels);
    }
}
