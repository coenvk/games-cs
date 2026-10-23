namespace RpgArt;

internal readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba Clear = new(0, 0, 0, 0);

    public static Rgba Hex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public bool IsClear => A == 0;

    /// <summary>Blends towards <paramref name="other"/> by <paramref name="t"/> (0 to 1).</summary>
    public Rgba Mix(Rgba other, double t) => new(Lerp(R, other.R, t), Lerp(G, other.G, t), Lerp(B, other.B, t), Lerp(A, other.A, t));

    public Rgba WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>Composites <paramref name="top"/> over this (opaque) colour.</summary>
    public Rgba Over(Rgba top) => Mix(top with { A = 255 }, top.A / 255.0);

    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
}

internal sealed class Rng(int seed)
{
#pragma warning disable CA5394 // Procedural art, not security relevant.
    private readonly Random _random = new(seed);

    public int Next(int max) => _random.Next(max);

    public int Next(int min, int max) => _random.Next(min, max);
#pragma warning restore CA5394
}

/// <summary>A 16x16 drawing surface; writes outside the tile are ignored.</summary>
internal sealed class Sprite
{
    public const int Size = 16;

    private readonly Rgba[] _pixels = new Rgba[Size * Size];

    public Rgba this[int x, int y]
    {
        get => Inside(x, y) ? _pixels[(y * Size) + x] : Rgba.Clear;
        set
        {
            if (Inside(x, y))
            {
                _pixels[(y * Size) + x] = value;
            }
        }
    }

    public static bool Inside(int x, int y) => x is >= 0 and < Size && y is >= 0 and < Size;

    public void Fill(Rgba color) => Array.Fill(_pixels, color);

    public void Rect(int x, int y, int w, int h, Rgba color)
    {
        for (var py = y; py < y + h; py++)
        {
            for (var px = x; px < x + w; px++)
            {
                this[px, py] = color;
            }
        }
    }

    public void HLine(int x0, int x1, int y, Rgba color) => Rect(x0, y, x1 - x0 + 1, 1, color);

    public void VLine(int x, int y0, int y1, Rgba color) => Rect(x, y0, 1, y1 - y0 + 1, color);

    public void Line(int x0, int y0, int x1, int y1, Rgba color)
    {
        var (dx, dy) = (Math.Abs(x1 - x0), -Math.Abs(y1 - y0));
        var (sx, sy) = (x0 < x1 ? 1 : -1, y0 < y1 ? 1 : -1);
        var err = dx + dy;
        while (true)
        {
            this[x0, y0] = color;
            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>Fills an ellipse shaded in three tones, lit from the upper left.</summary>
    public void Ball(double cx, double cy, double rx, double ry, Rgba dark, Rgba mid, Rgba light)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var (dx, dy) = ((x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry);
                if ((dx * dx) + (dy * dy) > 1)
                {
                    continue;
                }

                var shade = (dx * 0.6) + (dy * 0.8);
                this[x, y] = shade < -0.35 ? light : shade > 0.4 ? dark : mid;
            }
        }
    }

    public void Ellipse(double cx, double cy, double rx, double ry, Rgba color) => Ball(cx, cy, rx, ry, color, color, color);

    /// <summary>Draws a one-pixel ellipse outline, alternating two colours like chain links.</summary>
    public void Ring(double cx, double cy, double rx, double ry, Rgba a, Rgba b)
    {
        var inner = 1 - (1.1 / Math.Min(rx, ry));
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var (dx, dy) = ((x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry);
                var d = Math.Sqrt((dx * dx) + (dy * dy));
                if (d <= 1 && d > inner)
                {
                    this[x, y] = (x + y) % 2 == 0 ? a : b;
                }
            }
        }
    }

    /// <summary>Scatters single pixels over the non-transparent part of the sprite.</summary>
    public void Speckle(Rng rng, int count, Rgba color)
    {
        for (var i = 0; i < count; i++)
        {
            var (x, y) = (rng.Next(Size), rng.Next(Size));
            if (!this[x, y].IsClear)
            {
                this[x, y] = color;
            }
        }
    }

    /// <summary>Recolours pixels of one colour, e.g. to tint a flat area.</summary>
    public void Replace(Rgba from, Rgba to)
    {
        for (var i = 0; i < _pixels.Length; i++)
        {
            if (_pixels[i] == from)
            {
                _pixels[i] = to;
            }
        }
    }

    /// <summary>Adds a dark border around the drawn shape (4-connected).</summary>
    public void Outline(Rgba color)
    {
        var copy = (Rgba[])_pixels.Clone();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (copy[(y * Size) + x].IsClear && (Solid(copy, x - 1, y) || Solid(copy, x + 1, y) || Solid(copy, x, y - 1) || Solid(copy, x, y + 1)))
                {
                    this[x, y] = color;
                }
            }
        }
    }

    /// <summary>Fills transparent pixels near a point with a fading, translucent halo.</summary>
    public void Glow(double cx, double cy, double radius, Rgba color, int maxAlpha)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var d = Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2));
                if (this[x, y].IsClear && d < radius)
                {
                    this[x, y] = color.WithAlpha((byte)(maxAlpha * (1 - (d / radius))));
                }
            }
        }
    }

    /// <summary>Tints opaque pixels towards a colour, strongest at the given point.</summary>
    public void Light(double cx, double cy, double radius, Rgba color, double strength)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var d = Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2));
                if (!this[x, y].IsClear && d < radius)
                {
                    this[x, y] = this[x, y].Mix(color, strength * (1 - (d / radius)));
                }
            }
        }
    }

    private static bool Solid(Rgba[] pixels, int x, int y) => Inside(x, y) && !pixels[(y * Size) + x].IsClear;
}
