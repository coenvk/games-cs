using static RpgArt.Palette;

namespace RpgArt;

// House interiors: floors and walls seen from inside, furniture over a transparent
// background (the map puts wood_floor beneath), and two indoor characters.
internal static partial class Tiles
{
    private static readonly Rgba FloorDark = Rgba.Hex(0x7a4c24);
    private static readonly Rgba Floor = Rgba.Hex(0xa86e38);
    private static readonly Rgba FloorLight = Rgba.Hex(0xc08448);

    private static void WoodFloor(Sprite s, Rng r)
    {
        for (var plank = 0; plank < 4; plank++)
        {
            var y = plank * 4;
            s.Rect(0, y, 16, 3, Floor);
            s.HLine(0, 15, y, FloorLight);
            s.HLine(0, 15, y + 3, FloorDark);
            var joint = plank % 2 == 0 ? 3 : 11;
            s.VLine(joint, y, y + 2, FloorDark);
            s[joint + 1, y] = Floor;
            for (var i = 0; i < 2; i++)
            {
                var x = r.Next(0, 12);
                s.HLine(x, x + r.Next(2, 5), y + 1 + r.Next(2), Floor.Mix(FloorDark, 0.4));
            }
        }
    }

    private static void InnerWall(Sprite s, Rng r)
    {
        s.Fill(Plaster);
        s.Speckle(r, 12, PlasterDark);
        s.HLine(0, 15, 0, Wood);
        s.HLine(0, 15, 1, WoodDark);
        s.HLine(0, 15, 2, PlasterDark);
        s.HLine(0, 15, 11, PlasterDark);
        s.HLine(0, 15, 12, WoodLight);
        s.Rect(0, 13, 16, 2, Wood);
        s.HLine(0, 15, 15, WoodDark);
        s[r.Next(2, 14), 13] = WoodDark;
    }

    private static void InnerWindow(Sprite s, Rng r)
    {
        InnerWall(s, r);
        var sky = Rgba.Hex(0x8ac8f0);
        s.Rect(4, 3, 8, 8, WoodDark);
        s.Rect(5, 4, 6, 6, sky);
        s.HLine(5, 10, 4, Rgba.Hex(0xc8e8ff));
        s.Rect(5, 8, 6, 2, Rgba.Hex(0x7ab860));
        s.VLine(7, 4, 9, WoodDark);
        s.HLine(5, 10, 7, WoodDark);
        (s[5, 5], s[9, 5]) = (White, White);
        s.HLine(3, 12, 10, WoodLight);
        s.HLine(3, 12, 11, WoodDark);
        s.Light(8, 7, 8, Rgba.Hex(0xfff4d0), 0.2);
    }

    private static void Fireplace(Sprite s, Rng r)
    {
        InnerWall(s, r);
        s.Rect(1, 2, 14, 14, BrickDark);
        Rgba[] tones = [Brick, Rgba.Hex(0x747480), Rgba.Hex(0x64646f)];
        for (var course = 0; course < 5; course++)
        {
            var y = 2 + (course * 3);
            for (var x = (course % 2 * 2) - 2; x < 15; x += 4)
            {
                var left = Math.Max(x, 1);
                s.Rect(left, y, Math.Min(x + 3, 15) - left, 2, tones[r.Next(tones.Length)]);
                s.HLine(left, Math.Min(x + 2, 14), y, BrickLight);
            }
        }

        s.Rect(1, 5, 14, 2, WoodDark);
        s.HLine(1, 14, 5, WoodLight);
        s.Rect(4, 8, 8, 8, Rgba.Hex(0x16141c));
        s.HLine(5, 10, 8, Rgba.Hex(0x2a2024));
        s.Light(8, 13, 8, Rgba.Hex(0xffa040), 0.45);
        s.HLine(4, 11, 15, WoodDark);
        s.Line(5, 14, 10, 15, Wood);
        s.Ellipse(8, 12.5, 2.6, 2.8, Rgba.Hex(0xe85818));
        s.Ellipse(8, 13.2, 1.5, 1.8, Rgba.Hex(0xffb030));
        (s[7, 13], s[8, 14]) = (Rgba.Hex(0xfff2b0), Rgba.Hex(0xfff2b0));
        s[6, 10] = Rgba.Hex(0xe85818);
    }

    private static void Bed(Sprite s, Rng r)
    {
        var blanket = Rgba.Hex(0x3a6aa8);
        var blanketDark = Rgba.Hex(0x2a4c80);
        var blanketLight = Rgba.Hex(0x5a8acc);
        s.Rect(2, 0, 12, 16, WoodDark);
        s.HLine(2, 13, 0, Wood);
        s.Rect(3, 1, 10, 14, Rgba.Hex(0xe8e0d4));
        s.Rect(4, 2, 8, 3, White);
        s.HLine(4, 11, 4, Rgba.Hex(0xc8c0b4));
        (s[4, 2], s[11, 2]) = (Rgba.Hex(0xe8e0d4), Rgba.Hex(0xe8e0d4));
        s.Rect(3, 6, 10, 9, blanket);
        s.HLine(3, 12, 6, Rgba.Hex(0xe8e0d4));
        s.HLine(3, 12, 7, blanketLight);
        s.VLine(12, 7, 14, blanketDark);
        s.HLine(3, 12, 14, blanketDark);
        for (var y = 9; y <= 13; y += 2)
        {
            for (var x = 4 + (y % 4 == 1 ? 0 : 1); x < 12; x += 2)
            {
                s[x, y] = blanketDark;
            }
        }

        s.HLine(2, 13, 15, Wood);
        s.Outline(Ink);
    }

    private static void Table(Sprite s, Rng r)
    {
        s.Rect(1, 4, 14, 7, Wood);
        s.HLine(1, 14, 4, WoodLight);
        s.HLine(2, 13, 6, Wood.Mix(WoodLight, 0.4));
        s.HLine(1, 14, 9, WoodDark);
        s.HLine(1, 14, 10, WoodDark.Mix(Ink, 0.3));
        s.Rect(2, 11, 2, 4, WoodDark);
        s.Rect(12, 11, 2, 4, WoodDark);
        s[2, 11] = Wood;
        s[12, 11] = Wood;
        s.Speckle(r, 2, WoodLight);
        s.Rect(6, 5, 3, 2, Rgba.Hex(0xd8d0c0));
        s.HLine(6, 8, 7, Rgba.Hex(0xa8a090));
        s[10, 6] = Rgba.Hex(0xc88a40);
        s.Outline(Ink);
    }

    private static void Chair(Sprite s, Rng r)
    {
        s.Rect(5, 3, 6, 5, Wood);
        s.HLine(5, 10, 3, WoodLight);
        s.VLine(5, 3, 7, WoodDark);
        s.VLine(10, 3, 7, WoodDark);
        s.HLine(6, 9, 5, WoodDark);
        s.Rect(4, 8, 8, 3, WoodLight);
        s.HLine(4, 11, 10, Wood);
        s.Rect(4, 11, 2, 3, WoodDark);
        s.Rect(10, 11, 2, 3, WoodDark);
        s.Outline(Ink);
    }

    private static void Bookshelf(Sprite s, Rng r)
    {
        Rgba[] books = [Rgba.Hex(0xb03a30), Rgba.Hex(0x3a6aa8), Rgba.Hex(0x3a8a44), Rgba.Hex(0xd8a838), Rgba.Hex(0x6a3aa2), Rgba.Hex(0x8a5a2e)];
        s.Rect(1, 0, 14, 16, WoodDark);
        s.HLine(1, 14, 0, WoodLight);
        s.VLine(1, 0, 15, Wood);
        foreach (var top in (int[])[1, 6, 11])
        {
            s.Rect(2, top, 12, 4, Rgba.Hex(0x3a2210));
            for (var x = 2; x < 14; x++)
            {
                if (r.Next(6) == 0)
                {
                    continue;
                }

                var color = books[r.Next(books.Length)];
                var height = r.Next(2, 5);
                s.VLine(x, top + 4 - height, top + 3, x % 2 == 0 ? color : color.Mix(Ink, 0.2));
                s[x, top + 4 - height] = color.Mix(White, 0.3);
            }

            s.HLine(2, 13, top + 4, Wood);
        }

        s.Outline(Ink);
    }

    // A repeating diamond pattern with period 8, so a patch of rugs joins seamlessly.
    private static void Rug(Sprite s, Rng r)
    {
        var red = Rgba.Hex(0xa83a2c);
        var redDark = Rgba.Hex(0x7a2420);
        var ochre = Rgba.Hex(0xd8a040);
        var cream = Rgba.Hex(0xf0d8a0);
        for (var y = 0; y < Sprite.Size; y++)
        {
            for (var x = 0; x < Sprite.Size; x++)
            {
                var d = Math.Abs((x % 8) - 3.5) + Math.Abs((y % 8) - 3.5);
                s[x, y] = d switch
                {
                    < 1.5 => cream,
                    < 2.5 => redDark,
                    < 3.5 => ochre,
                    < 5.5 => red,
                    _ => redDark,
                };
            }
        }

        s.Speckle(r, 6, red.Mix(redDark, 0.5));
    }

    private static void Anvil(Sprite s, Rng r)
    {
        var dark = Rgba.Hex(0x34343e);
        s.Rect(4, 5, 10, 3, Iron);
        s.HLine(4, 13, 5, SteelDark);
        s.HLine(1, 3, 6, Iron);
        s.HLine(2, 3, 7, dark);
        s[1, 6] = SteelDark;
        s.HLine(4, 13, 7, dark);
        s.Rect(6, 8, 5, 3, Iron);
        s.VLine(10, 8, 10, dark);
        s.Rect(4, 11, 9, 3, Iron);
        s.HLine(4, 12, 11, SteelDark);
        s.HLine(4, 12, 13, dark);
        (s[6, 5], s[7, 5]) = (Steel, SteelLight);
        s.Outline(Ink);
    }

    private static void Forge(Sprite s, Rng r)
    {
        StoneWall(s, r);
        s.Rect(4, 0, 8, 5, BrickDark);
        s.VLine(4, 0, 4, BrickLight);
        s.Rect(1, 5, 14, 11, Brick);
        s.HLine(1, 14, 5, BrickTop);
        s.HLine(1, 14, 6, BrickLight);
        s.VLine(1, 5, 15, BrickLight);
        s.VLine(14, 6, 15, BrickDark);
        s.HLine(1, 14, 15, BrickDark);
        foreach (var (x, y) in ((int X, int Y)[])[(5, 13), (10, 13), (3, 14), (8, 14), (12, 14)])
        {
            s[x, y] = BrickDark;
        }

        s.Rect(3, 7, 10, 5, Rgba.Hex(0x2a1810));
        s.HLine(3, 12, 12, BrickTop);
        Rgba[] coals = [Rgba.Hex(0xe85818), Rgba.Hex(0xffb030), Rgba.Hex(0xa82c10), Rgba.Hex(0xfff2b0)];
        for (var y = 9; y <= 11; y++)
        {
            for (var x = 3; x <= 12; x++)
            {
                s[x, y] = coals[r.Next(y == 9 ? 3 : 4)];
            }
        }

        s.Light(8, 10, 9, Rgba.Hex(0xffa040), 0.4);
    }

    private static void Cauldron(Sprite s, Rng r)
    {
        var (dark, mid, light) = (Rgba.Hex(0x16141c), Rgba.Hex(0x2c2a34), Rgba.Hex(0x4a4854));
        s.Ball(8, 10, 6, 4.5, dark, mid, light);
        s.Rect(4, 14, 2, 2, dark);
        s.Rect(10, 14, 2, 2, dark);
        s.Ellipse(8, 7, 6, 2, Iron);
        s.Ellipse(8, 7, 5, 1.4, Rgba.Hex(0x3cae3a));
        s.HLine(5, 10, 7, Rgba.Hex(0x6ae05a));
        var bubble = Rgba.Hex(0xb0ff90);
        (s[6, 6], s[9, 7], s[11, 6]) = (bubble, bubble, bubble);
        (s[8, 4], s[10, 3], s[7, 2]) = (Rgba.Hex(0x9ae880), Rgba.Hex(0x9ae880), Rgba.Hex(0x9ae880).WithAlpha(170));
        s.Outline(Ink);
        s.Glow(8, 6, 6, Rgba.Hex(0x80ff60), 60);
    }

    private static void PlantPot(Sprite s, Rng r)
    {
        var (clay, clayDark, clayLight) = (Rgba.Hex(0xb8603a), Rgba.Hex(0x80402a), Rgba.Hex(0xd8845a));
        s.Rect(4, 9, 8, 2, clayLight);
        s.HLine(4, 11, 10, clay);
        s.Rect(5, 11, 6, 4, clay);
        s.VLine(10, 11, 14, clayDark);
        s.HLine(5, 10, 14, clayDark);
        s.VLine(5, 11, 13, clayLight);
        s.VLine(8, 4, 9, LeafDark);
        s.Line(8, 8, 5, 5, Leaf);
        s.Line(8, 7, 11, 4, Leaf);
        s.Ball(5, 4.5, 2, 1.6, LeafDark, Leaf, LeafLight);
        s.Ball(11, 4, 2, 1.6, LeafDark, Leaf, LeafLight);
        s.Ball(8, 2.5, 1.8, 1.8, LeafDark, Leaf, LeafLight);
        s.Ball(6, 8, 1.8, 1.3, LeafDark, Leaf, LeafLight);
        s.Ball(10.5, 7.5, 1.8, 1.3, LeafDark, Leaf, LeafLight);
        s.HLine(5, 10, 9, Rgba.Hex(0x4a3018));
        s.Speckle(r, 2, LeafLight);
        s.Outline(Ink);
    }

    private static void Cupboard(Sprite s, Rng r)
    {
        s.Rect(2, 1, 12, 15, Wood);
        s.HLine(1, 14, 1, WoodLight);
        s.HLine(1, 14, 2, WoodDark);
        s.VLine(13, 3, 15, WoodDark);
        s.VLine(2, 3, 15, WoodLight);
        s.Rect(3, 4, 10, 3, WoodDark);
        s.Rect(4, 4, 8, 2, Wood.Mix(WoodLight, 0.4));
        s[8, 5] = Gold;
        foreach (var x in (int[])[3, 8])
        {
            s.Rect(x, 8, 5, 7, WoodDark);
            s.Rect(x + 1, 9, 3, 5, Wood);
            s.HLine(x + 1, x + 3, 9, WoodLight);
        }

        (s[7, 11], s[8, 11]) = (Gold, Gold);
        s.HLine(2, 13, 15, WoodDark);
        s.Speckle(r, 2, WoodDark);
        s.Outline(Ink);
    }

    private static void Hay(Sprite s, Rng r)
    {
        var (straw, strawDark, strawLight) = (Rgba.Hex(0xd8b040), Rgba.Hex(0xa88020), Rgba.Hex(0xf0d878));
        s.Rect(1, 4, 14, 11, straw);
        s.HLine(2, 13, 3, strawLight);
        (s[1, 4], s[14, 4]) = (Rgba.Clear, Rgba.Clear);
        s.HLine(1, 14, 4, strawLight);
        s.HLine(1, 14, 8, strawLight.Mix(straw, 0.5));
        s.HLine(1, 14, 14, strawDark);
        s.VLine(14, 5, 14, strawDark);
        for (var i = 0; i < 14; i++)
        {
            var (x, y) = (r.Next(2, 13), r.Next(5, 14));
            s.HLine(x, x + r.Next(1, 3), y, r.Next(2) == 0 ? strawDark : strawLight);
        }

        var twine = Rgba.Hex(0x8a4a24);
        s.VLine(4, 3, 14, twine);
        s.VLine(11, 3, 14, twine);
        (s[0, 7], s[15, 11], s[6, 2]) = (strawLight, straw, strawLight);
        s.Outline(Ink);
    }

    private static void Sack(Sprite s, Rng r)
    {
        var (dark, mid, light) = (Rgba.Hex(0xa08a60), Rgba.Hex(0xc8b080), Rgba.Hex(0xe4d0a4));
        s.Ball(8, 10.5, 5.5, 4.8, dark, mid, light);
        s.Rect(6, 4, 4, 3, mid);
        s.VLine(9, 4, 6, dark);
        s.HLine(5, 10, 3, light);
        (s[5, 2], s[10, 2], s[7, 2]) = (mid, mid, light);
        s.HLine(6, 9, 6, Rgba.Hex(0x8a4a24));
        s.Line(6, 12, 7, 9, dark);
        s.Line(10, 12, 9, 9, dark);
        (s[4, 9], s[5, 8]) = (light, light);
        s.Outline(Ink);
    }

    private static void Doormat(Sprite s, Rng r)
    {
        WoodFloor(s, r);
        var (mat, matDark, matLight) = (Rgba.Hex(0xb88a48), Rgba.Hex(0x6a4420), Rgba.Hex(0xd4a868));
        var stripe = Rgba.Hex(0xa83a2c);
        s.Rect(2, 5, 12, 9, matDark);
        for (var y = 6; y <= 12; y++)
        {
            for (var x = 3; x <= 12; x++)
            {
                s[x, y] = (x + y) % 2 == 0 ? mat : matLight;
            }
        }

        s.HLine(3, 12, 7, stripe);
        s.HLine(3, 12, 11, stripe);
        s.HLine(2, 13, 14, FloorDark.Mix(Ink, 0.3));
    }

    private static void OldWoman(Sprite s, Rng r)
    {
        var grey = Rgba.Hex(0xb8b8c4);
        var dress = Rgba.Hex(0x5a4a6a);
        var shawl = Rgba.Hex(0xa83e4a);
        var hands = Figure(s, Skin, grey, dress, dress, dress, top: 2, bodyHeight: 4, robe: true);
        s.Ellipse(8, 1.5, 2, 1.5, grey);
        s[7, 1] = grey.Mix(White, 0.35);
        s.HLine(6, 9, 2, grey.Mix(Ink, 0.2));
        s.Rect(4, 8, 8, 2, shawl);
        s.HLine(5, 10, 10, shawl);
        s.HLine(6, 9, 11, shawl.Mix(Ink, 0.2));
        s.VLine(11, 8, 9, shawl.Mix(Ink, 0.3));
        s.VLine(10, 8, 10, shawl.Mix(Ink, 0.3));
        (s[7, 8], s[8, 8]) = (shawl.Mix(White, 0.3), shawl.Mix(White, 0.3));
        s.HLine(12, 13, hands - 3, Wood);
        s.VLine(13, hands - 2, 14, Wood);
        s[11, hands] = Skin;
        s[12, hands - 1] = Skin;
        s.Outline(Ink);
    }

    private static void Cat(Sprite s, Rng r)
    {
        var (dark, mid, light) = (Rgba.Hex(0xb05a1c), Rgba.Hex(0xe0843a), Rgba.Hex(0xf4ac64));
        s.Ball(8, 11.5, 3.5, 3.5, dark, mid, light);
        s.Ball(7, 7, 3, 2.5, dark, mid, light);
        (s[4, 4], s[5, 4], s[4, 5], s[9, 4], s[10, 4], s[10, 5]) = (mid, mid, mid, dark, dark, dark);
        (s[5, 5], s[9, 5]) = (Rgba.Hex(0xf0a0b0), Rgba.Hex(0xf0a0b0));
        (s[6, 7], s[8, 7]) = (Rgba.Hex(0x3a8a34), Rgba.Hex(0x3a8a34));
        s[7, 8] = Rgba.Hex(0xf0a0b0);
        s.HLine(6, 8, 9, light);
        foreach (var y in (int[])[10, 12])
        {
            s.HLine(9, 10, y, dark);
        }

        s.HLine(6, 7, 14, light);
        s.HLine(9, 10, 14, light);
        s.Line(12, 14, 13, 13, mid);
        s.Line(13, 12, 13, 10, mid);
        s[14, 9] = dark;
        s[13, 14] = dark;
        s.Outline(Ink);
    }
}
