using static RpgArt.Palette;

namespace RpgArt;

// Characters and creatures. People share one humanoid routine; accessories are drawn on top.
internal static partial class Tiles
{
    /// <summary>Draws a front-facing person without outline and returns the row of the hands.</summary>
    private static int Figure(Sprite s, Rgba skin, Rgba? hair, Rgba shirt, Rgba legs, Rgba boots, int top = 1, int bodyHeight = 5, int legHeight = 3, bool robe = false)
    {
        var t = top;
        s.Rect(5, t + 1, 6, 4, skin);
        s.HLine(6, 9, t + 5, skin);
        s.VLine(10, t + 2, t + 4, skin.Mix(Ink, 0.18));
        (s[6, t + 3], s[9, t + 3]) = (Ink, Ink);
        if (hair is { } h)
        {
            s.HLine(6, 9, t, h);
            s.HLine(5, 10, t + 1, h);
            (s[5, t + 2], s[6, t + 2], s[10, t + 2]) = (h, h, h);
            s[7, t] = h.Mix(White, 0.25);
        }

        var body = t + 6;
        var hands = body + bodyHeight - 1;
        var shirtDark = shirt.Mix(Ink, 0.3);
        s.Rect(5, body, 6, bodyHeight, shirt);
        s.VLine(10, body, hands, shirtDark);
        s.VLine(4, body, hands - 1, shirt);
        s.VLine(11, body, hands - 1, shirtDark);
        (s[4, hands], s[11, hands]) = (skin, skin);
        s.HLine(5, 10, hands, shirt.Mix(Ink, 0.5));

        var feet = hands + legHeight;
        if (robe)
        {
            s.Rect(5, hands + 1, 6, legHeight, shirt);
            s.HLine(4, 11, feet, shirt);
            s.VLine(10, hands + 1, feet, shirtDark);
            s.HLine(5, 10, feet, shirtDark);
        }
        else
        {
            s.Rect(6, hands + 1, 4, legHeight - 1, legs);
            s.VLine(8, hands + 1, feet - 1, legs.Mix(Ink, 0.35));
            s.HLine(5, 10, feet, boots);
            s[8, feet] = boots.Mix(Ink, 0.4);
        }

        return hands;
    }

    private static void Player(Sprite s, Rng r)
    {
        var hands = Figure(s, Skin, Rgba.Hex(0x7a4420), Rgba.Hex(0x3a6ad0), Rgba.Hex(0x6a4a30), Rgba.Hex(0x3a2414));
        s[7, 3] = Rgba.Hex(0x7a4420);
        s.HLine(5, 10, 7, Rgba.Hex(0x5a8ae8));
        s.VLine(12, 3, hands - 2, Steel);
        s[12, 2] = SteelLight;
        s.VLine(13, 4, hands - 2, SteelDark);
        s.HLine(11, 14, hands - 1, GoldDark);
        s[12, hands] = WoodDark;
        s.Outline(Ink);
    }

    private static void Elder(Sprite s, Rng r)
    {
        var robe = Rgba.Hex(0x6a3aa2);
        var hands = Figure(s, Skin, Rgba.Hex(0xe4e4ec), robe, robe, robe, robe: true);
        s.Rect(6, 5, 4, 3, White);
        s.HLine(7, 8, 8, White);
        s.HLine(5, 10, hands, Gold);
        s.VLine(2, 2, 14, Wood);
        s.VLine(3, 3, 14, WoodDark);
        s.Ellipse(2.5, 2, 1.5, 1.5, Rgba.Hex(0x6ad8ff));
        s[2, 1] = SteelLight;
        s[3, hands] = Skin;
        s.Outline(Ink);
    }

    private static void Herbalist(Sprite s, Rng r)
    {
        var hood = Rgba.Hex(0x2f6e34);
        var hands = Figure(s, Skin, hood, Rgba.Hex(0x4a9a48), hood, hood, robe: true);
        s.HLine(5, 10, 2, hood.Mix(White, 0.15));
        s.VLine(4, 3, 7, hood);
        s.VLine(11, 3, 7, hood.Mix(Ink, 0.3));
        (s[5, 3], s[10, 3], s[5, 4], s[10, 4]) = (hood, hood, hood, hood);
        s[7, 1] = hood.Mix(White, 0.2);
        var wicker = Rgba.Hex(0xc89450);
        s.Rect(11, hands - 1, 4, 3, wicker);
        s.HLine(11, 14, hands, wicker.Mix(Ink, 0.35));
        (s[11, hands - 2], s[14, hands - 2], s[12, hands - 3], s[13, hands - 3]) = (WoodDark, WoodDark, WoodDark, WoodDark);
        (s[12, hands - 1], s[13, hands - 2]) = (LeafLight, Rgba.Hex(0xf080b0));
        s.Outline(Ink);
    }

    private static void Smith(Sprite s, Rng r)
    {
        var tan = Rgba.Hex(0xd89a6a);
        var hands = Figure(s, tan, Rgba.Hex(0x2a2024), Rgba.Hex(0xb0aca4), Rgba.Hex(0x4a3a30), Rgba.Hex(0x2a1a10));
        var leather = Rgba.Hex(0x7a4a24);
        s.Rect(6, 7, 4, hands - 5, leather);
        s.VLine(9, 7, hands + 1, leather.Mix(Ink, 0.3));
        (s[5, 7], s[10, 7]) = (leather, leather);
        s.HLine(6, 8, 5, Rgba.Hex(0x2a2024));
        s.VLine(12, 6, hands, Wood);
        s.Rect(11, 4, 4, 2, Iron);
        s.HLine(11, 14, 4, SteelDark);
        s.Outline(Ink);
    }

    private static void Villager(Sprite s, Rng r)
    {
        Figure(s, Skin, Rgba.Hex(0x8a5a2a), Rgba.Hex(0xc8b080), Rgba.Hex(0x6a5a3e), Rgba.Hex(0x3a2a1a));
        var straw = Rgba.Hex(0xe8c860);
        s.HLine(6, 9, 1, straw);
        s.HLine(3, 12, 2, straw.Mix(Ink, 0.15));
        s.HLine(6, 9, 2, Rgba.Hex(0xb04030));
        s[7, 1] = straw.Mix(White, 0.4);
        s.Rect(7, 7, 2, 3, Rgba.Hex(0xa8946a));
        s.Outline(Ink);
    }

    private static void Child(Sprite s, Rng r)
    {
        Figure(s, Skin, Rgba.Hex(0xe8c050), Rgba.Hex(0xd23c3c), Rgba.Hex(0x3a5a9a), Rgba.Hex(0x3a2414), top: 4, bodyHeight: 3, legHeight: 2);
        s[9, 6] = Rgba.Hex(0xe8c050);
        s.Outline(Ink);
    }

    private static void Rat(Sprite s, Rng r)
    {
        var pink = Rgba.Hex(0xf0a0b0);
        s.Ball(8.5, 11, 4.5, 3, Rgba.Hex(0x5a5a66), Rgba.Hex(0x8a8a96), Rgba.Hex(0xacacb6));
        s.Ball(4.5, 10.5, 2.5, 2, Rgba.Hex(0x6a6a76), Rgba.Hex(0x8a8a96), Rgba.Hex(0xacacb6));
        s.Ellipse(5.5, 8, 1.5, 1.5, Rgba.Hex(0x8a8a96));
        (s[5, 8], s[2, 11], s[3, 10]) = (pink, pink, Ink);
        (s[6, 14], s[11, 14]) = (pink.Mix(Ink, 0.3), pink.Mix(Ink, 0.3));
        (s[13, 12], s[14, 11], s[14, 10], s[14, 9], s[13, 8]) = (pink, pink, pink, pink, pink);
        s.Outline(Ink);
    }

    private static void Slime(Sprite s, Rng r)
    {
        var (dark, mid, light) = (Rgba.Hex(0x2a8a3a), Rgba.Hex(0x5cc85a), Rgba.Hex(0x9ce88a));
        s.Ball(8, 11, 6, 5, dark, mid, light);
        s.Rect(2, 11, 12, 4, Rgba.Clear);
        s.Ball(8, 11, 6, 4, dark, mid, light);
        s.Ellipse(8, 12, 3.5, 1.5, mid.Mix(dark, 0.35));
        s.HLine(3, 12, 14, dark);
        (s[5, 8], s[6, 7], s[5, 9]) = (Rgba.Hex(0xe0ffe0), Rgba.Hex(0xe0ffe0), light);
        foreach (var x in (int[])[6, 9])
        {
            (s[x, 10], s[x, 11]) = (Ink, Ink);
        }

        (s[7, 12], s[8, 12]) = (dark, dark);
        s.Outline(Ink);
        for (var y = 0; y < Sprite.Size; y++)
        {
            for (var x = 0; x < Sprite.Size; x++)
            {
                if (s[x, y] == mid)
                {
                    s[x, y] = mid.WithAlpha(225);
                }
            }
        }
    }

    private static void Skeleton(Sprite s, Rng r)
    {
        var bone = Rgba.Hex(0xe8e4d4);
        var boneDark = Rgba.Hex(0xb0aa98);
        var hands = Figure(s, bone, null, bone, bone, bone);
        (s[6, 4], s[9, 4], s[6, 3], s[9, 3]) = (Ink, Ink, Ink, Ink);
        (s[7, 6], s[9, 6]) = (boneDark, boneDark);
        s.Rect(4, 7, 8, 5, Rgba.Clear);
        s.VLine(7, 7, 11, bone);
        s.VLine(8, 7, 11, boneDark);
        s.HLine(5, 10, 8, bone);
        s.HLine(5, 10, 10, bone);
        s.HLine(6, 9, hands, boneDark);
        s.VLine(4, 7, hands, bone);
        s.VLine(11, 7, hands, boneDark);
        s.Rect(6, hands + 1, 4, 3, Rgba.Clear);
        s.VLine(6, hands + 1, 14, bone);
        s.VLine(9, hands + 1, 14, boneDark);
        s.Outline(Ink);
    }

    private static void Spider(Sprite s, Rng r)
    {
        var leg = Rgba.Hex(0x7a5a96);
        (int X0, int Y0, int X1, int Y1, int X2, int Y2)[] legs =
        [
            (6, 7, 3, 4, 1, 6), (5, 9, 2, 7, 1, 10), (5, 10, 2, 11, 1, 14), (6, 12, 4, 13, 3, 15),
        ];
        foreach (var (x0, y0, x1, y1, x2, y2) in legs)
        {
            s.Line(x0, y0, x1, y1, leg);
            s.Line(x1, y1, x2, y2, leg);
            s.Line(15 - x0, y0, 15 - x1, y1, leg);
            s.Line(15 - x1, y1, 15 - x2, y2, leg);
        }

        s.Ball(8, 10.5, 3.5, 3, Rgba.Hex(0x2a1838), Rgba.Hex(0x4c2c66), Rgba.Hex(0x7050a0));
        s.Ball(8, 6.5, 2.2, 1.8, Rgba.Hex(0x2a1838), Rgba.Hex(0x4c2c66), Rgba.Hex(0x7050a0));
        (s[7, 6], s[8, 6]) = (Rgba.Hex(0xff4848), Rgba.Hex(0xff4848));
        (s[7, 10], s[8, 11]) = (Rgba.Hex(0x9070c0), Rgba.Hex(0x9070c0));
        s.Outline(Ink);
    }
}
