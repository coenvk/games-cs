using static RpgArt.Palette;

namespace RpgArt;

internal static partial class Tiles
{
    private static void Potion(Sprite s, Rng r)
    {
        var glass = Rgba.Hex(0xb8d8e8);
        s.Ball(8, 10.5, 4.5, 4, Rgba.Hex(0x8a1828), Rgba.Hex(0xd03040), Rgba.Hex(0xff6a74));
        s.Rect(7, 4, 2, 3, glass);
        s.HLine(6, 9, 4, glass);
        s.Rect(7, 2, 2, 2, Wood);
        s[7, 2] = WoodLight;
        (s[5, 9], s[5, 10], s[6, 8]) = (White, White, Rgba.Hex(0xffb0b4));
        s.Outline(Ink);
    }

    // The pulse frame has a smaller, fainter halo.
    private static void Glowcap(Sprite s, Rng r, bool dim)
    {
        var (dark, mid, light) = (Rgba.Hex(0x1a9aa8), Rgba.Hex(0x40e0e8), Rgba.Hex(0xb0ffff));
        s.Ball(8, 8.5, 5.5, 5, dark, mid, light);
        s.Rect(0, 9, 16, 7, Rgba.Clear);
        s.HLine(4, 11, 8, dark);
        (s[6, 5], s[10, 6], s[8, 4]) = (White, White, White);
        var stem = Rgba.Hex(0xe8f0e0);
        s.Rect(7, 9, 2, 5, stem);
        s.VLine(8, 9, 13, Rgba.Hex(0xb8c8b8));
        s.HLine(6, 9, 13, stem);
        s.Outline(Ink);
        s.Glow(8, 7.5, dim ? 6.5 : 8, Rgba.Hex(0x60ffff), dim ? 55 : 90);
    }

    private static void Amulet(Sprite s, Rng r)
    {
        s.Ring(8, 6, 4.5, 4.5, Gold, GoldDark);
        s.Ball(8, 11, 2.5, 2.5, GoldDark, Gold, GoldLight);
        s.Ball(8, 11, 1.6, 1.6, Rgba.Hex(0x1a3a9a), Rgba.Hex(0x3a7ae0), Rgba.Hex(0x9ad0ff));
        s[7, 10] = Rgba.Hex(0xd8f0ff);
        s.Outline(Ink);
    }

    private static void Sword(Sprite s, Rng r)
    {
        for (var i = 0; i < 8; i++)
        {
            s[6 + i, 9 - i] = SteelLight;
            s[6 + i, 10 - i] = SteelDark;
            s[7 + i, 10 - i] = Steel;
        }

        s[14, 1] = SteelLight;
        s.Line(3, 8, 7, 12, GoldDark);
        (s[4, 9], s[6, 11]) = (Gold, Gold);
        (s[4, 11], s[3, 12]) = (Wood, WoodDark);
        s.Rect(1, 13, 2, 2, Gold);
        s[1, 13] = GoldLight;
        s.Outline(Ink);
    }

    private static void Shield(Sprite s, Rng r)
    {
        s.Ball(8, 8, 6.5, 6.5, WoodDark, Wood, WoodLight);
        for (var y = 3; y <= 12; y++)
        {
            foreach (var x in (int[])[5, 10])
            {
                if (s[x, y] != Rgba.Clear)
                {
                    s[x, y] = s[x, y].Mix(Ink, 0.3);
                }
            }
        }

        s.Ring(8, 8, 6.5, 6.5, SteelDark, Iron);
        s.Ball(8, 8, 2, 2, SteelDark, Steel, SteelLight);
        s.Outline(Ink);
    }

    private static void Coins(Sprite s, Rng r)
    {
        (int X, int Y)[] coins = [(6, 5), (4, 7), (8, 7), (2, 9), (6, 9), (10, 9), (4, 11), (8, 11)];
        foreach (var (x, y) in coins)
        {
            s.HLine(x + 1, x + 3, y, GoldLight);
            s.HLine(x, x + 4, y + 1, Gold);
            s.HLine(x + 1, x + 3, y + 2, GoldDark);
        }

        s[12, 3] = White;
        (s[11, 3], s[13, 3], s[12, 2], s[12, 4]) = (GoldLight, GoldLight, GoldLight, GoldLight);
        s.Outline(Ink);
    }

    private static void Bread(Sprite s, Rng r)
    {
        var crust = Rgba.Hex(0x8a4a18);
        s.Ball(8, 10, 6.5, 3.8, Rgba.Hex(0x9a5a20), Rgba.Hex(0xc88a40), Rgba.Hex(0xe8b464));
        s.HLine(3, 12, 13, crust);
        foreach (var x in (int[])[4, 7, 10])
        {
            (s[x + 1, 8], s[x, 9]) = (crust, crust);
            s[x + 2, 8] = Rgba.Hex(0xf8d090);
        }

        s.Outline(Ink);
    }

    private static void Armor(Sprite s, Rng r)
    {
        var (dark, mid, light) = (Rgba.Hex(0x5e3a1a), Rgba.Hex(0x8a5a2e), Rgba.Hex(0xae7a44));
        s.HLine(4, 6, 3, light);
        s.HLine(9, 11, 3, mid);
        s.Rect(3, 4, 10, 5, mid);
        s.Rect(4, 9, 8, 5, mid);
        s.Rect(7, 3, 2, 4, Rgba.Clear);
        s.VLine(3, 4, 8, light);
        s.VLine(4, 9, 13, light);
        s.VLine(12, 4, 8, dark);
        s.VLine(11, 9, 13, dark);
        s.HLine(4, 11, 13, dark);
        s.HLine(4, 11, 8, dark.Mix(mid, 0.5));
        var lace = Rgba.Hex(0xe8d09a);
        (s[7, 7], s[8, 8], s[7, 9], s[8, 10], s[7, 11]) = (lace, lace, lace, lace, lace);
        s.Outline(Ink);
    }
}
