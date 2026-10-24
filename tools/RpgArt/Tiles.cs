using static RpgArt.Palette;

namespace RpgArt;

/// <param name="Solid">Blocks movement.</param>
/// <param name="Opaque">Blocks line of sight.</param>
/// <param name="Walk">Name of the tile holding this character's walk frame.</param>
/// <param name="Animation">In-place animation as (tile name, duration in ms) frames.</param>
/// <param name="Light">A light the tile gives off in dark areas: radius in tiles and colour (#rrggbb).</param>
internal sealed record TileDef(string Name, bool Solid, bool Opaque, Action<Sprite, Rng> Draw, string? Walk = null, IReadOnlyList<(string Tile, int Duration)>? Animation = null, (float Radius, string Color)? Light = null);

/// <summary>
/// The tiles in sheet order (row-major); the index is the Tiled local tile id. Terrain is
/// drawn as full squares, decor, characters and items on a transparent background.
/// Null entries are empty slots. Animation frames are appended after the original tiles so
/// existing ids never move; a frame redraws its base tile with the base tile's seed.
/// </summary>
internal static partial class Tiles
{
    public const int Columns = 8;
    public const int Rows = 10;

    public static readonly IReadOnlyList<TileDef?> All =
    [
        new("grass", false, false, GrassTile),
        new("grass_tall", false, false, GrassTall),
        new("flowers", false, false, Flowers),
        new("path", false, false, Path),
        new("bridge", false, false, Bridge),
        new("water", true, false, (s, r) => WaterTile(s, r, 0), Animation: [("water", 600), ("water_2", 600)]),
        new("tree", true, true, Tree),
        new("bush", true, false, Bush),
        new("fence", true, false, Fence),
        new("house_wall", true, true, HouseWall),
        new("roof", true, true, RoofTile),
        new("door", false, true, Door),
        new("window", true, true, Window),
        new("well", true, false, Well),
        new("sign", true, false, Sign),
        new("hatch", false, false, Hatch),
        new("stone_floor", false, false, StoneFloor),
        new("stone_wall", true, true, StoneWall),
        new("stairs_down", false, false, StairsDown),
        new("stairs_up", false, false, StairsUp),
        new("rubble", false, false, Rubble),
        new("torch_wall", true, true, (s, r) => TorchWall(s, r, false), Animation: [("torch_wall", 180), ("torch_wall_2", 140)], Light: (4.5f, "#ffb060")),
        new("crate", true, false, Crate),
        new("barrel", true, false, Barrel),
        new("player", false, false, (s, r) => Player(s, r, false), Walk: "player_walk"),
        new("elder", false, false, (s, r) => Elder(s, r, false), Walk: "elder_walk"),
        new("herbalist", false, false, (s, r) => Herbalist(s, r, false), Walk: "herbalist_walk"),
        new("smith", false, false, (s, r) => Smith(s, r, false), Walk: "smith_walk"),
        new("villager", false, false, (s, r) => Villager(s, r, false), Walk: "villager_walk"),
        new("child", false, false, (s, r) => Child(s, r, false), Walk: "child_walk"),
        new("rat", false, false, (s, r) => Rat(s, r, false), Walk: "rat_walk"),
        new("slime", false, false, (s, r) => Slime(s, r, false), Walk: "slime_walk"),
        new("skeleton", false, false, (s, r) => Skeleton(s, r, false), Walk: "skeleton_walk"),
        new("spider", false, false, (s, r) => Spider(s, r, false), Walk: "spider_walk"),
        new("potion", false, false, Potion),
        new("glowcap", false, false, (s, r) => Glowcap(s, r, false), Animation: [("glowcap", 500), ("glowcap_2", 500)], Light: (2.2f, "#60e8d0")),
        new("amulet", false, false, Amulet),
        new("sword", false, false, Sword),
        new("shield", false, false, Shield),
        new("coins", false, false, Coins),
        new("bread", false, false, Bread),
        new("armor", false, false, Armor),
        new("wood_floor", false, false, WoodFloor),
        new("inner_wall", true, true, InnerWall),
        new("inner_window", true, true, InnerWindow),
        new("fireplace", true, true, Fireplace),
        new("bed", true, false, Bed),
        new("table", true, false, Table),
        new("chair", false, false, Chair),
        new("bookshelf", true, false, Bookshelf),
        new("rug", false, false, Rug),
        new("anvil", true, false, Anvil),
        new("forge", true, true, Forge),
        new("cauldron", true, false, Cauldron),
        new("plant_pot", true, false, PlantPot),
        new("cupboard", true, false, Cupboard),
        new("hay", true, false, Hay),
        new("sack", true, false, Sack),
        new("doormat", false, false, Doormat),
        new("old_woman", false, false, (s, r) => OldWoman(s, r, false), Walk: "old_woman_walk"),
        new("cat", false, false, (s, r) => Cat(s, r, false), Walk: "cat_walk"),
        null,
        null,
        null,
        new("water_2", true, false, (s, _) => WaterTile(s, SeedOf("water"), 2)),
        new("torch_wall_2", true, true, (s, _) => TorchWall(s, SeedOf("torch_wall"), true)),
        new("glowcap_2", false, false, (s, _) => Glowcap(s, SeedOf("glowcap"), true)),
        new("player_walk", false, false, (s, _) => Player(s, SeedOf("player"), true)),
        new("elder_walk", false, false, (s, _) => Elder(s, SeedOf("elder"), true)),
        new("herbalist_walk", false, false, (s, _) => Herbalist(s, SeedOf("herbalist"), true)),
        new("smith_walk", false, false, (s, _) => Smith(s, SeedOf("smith"), true)),
        new("villager_walk", false, false, (s, _) => Villager(s, SeedOf("villager"), true)),
        new("child_walk", false, false, (s, _) => Child(s, SeedOf("child"), true)),
        new("rat_walk", false, false, (s, _) => Rat(s, SeedOf("rat"), true)),
        new("slime_walk", false, false, (s, _) => Slime(s, SeedOf("slime"), true)),
        new("skeleton_walk", false, false, (s, _) => Skeleton(s, SeedOf("skeleton"), true)),
        new("spider_walk", false, false, (s, _) => Spider(s, SeedOf("spider"), true)),
        new("old_woman_walk", false, false, (s, _) => OldWoman(s, SeedOf("old_woman"), true)),
        new("cat_walk", false, false, (s, _) => Cat(s, SeedOf("cat"), true)),
    ];

    public static Rng Seed(int id) => new(1000 + id);

    public static int Id(string name)
    {
        for (var id = 0; id < All.Count; id++)
        {
            if (All[id]?.Name == name)
            {
                return id;
            }
        }

        throw new ArgumentException($"Unknown tile '{name}'.", nameof(name));
    }

    private static Rng SeedOf(string name) => Seed(Id(name));

    private static void GrassTile(Sprite s, Rng r)
    {
        s.Fill(Grass);
        s.Speckle(r, 18, GrassDark);
        s.Speckle(r, 10, GrassLight);
    }

    private static void GrassTall(Sprite s, Rng r)
    {
        GrassTile(s, r);
        (int X, int Y)[] tufts = [(3, 5), (11, 3), (8, 10), (13, 13), (3, 14)];
        foreach (var (x, y) in tufts)
        {
            s.HLine(x - 1, x + 1, y, GrassDark);
            (s[x - 1, y - 1], s[x, y - 1], s[x + 1, y - 1]) = (GrassLight, GrassLight, GrassLight);
            (s[x - 2, y - 2], s[x, y - 2], s[x + 2, y - 2]) = (GrassLight, GrassBright, GrassBright);
            s[x, y - 3] = GrassBright;
        }
    }

    private static void Flowers(Sprite s, Rng r)
    {
        GrassTile(s, r);
        var pink = Rgba.Hex(0xf080b0);
        var yellow = Rgba.Hex(0xf8d838);
        (int X, int Y, Rgba Petal, Rgba Centre)[] flowers =
        [
            (3, 3, White, yellow), (11, 2, pink, yellow), (7, 7, yellow, Rgba.Hex(0xe07020)),
            (13, 9, White, yellow), (3, 11, pink, White), (9, 13, yellow, Rgba.Hex(0xe07020)),
        ];
        foreach (var (x, y, petal, centre) in flowers)
        {
            (s[x - 1, y], s[x + 1, y], s[x, y - 1], s[x, y + 1]) = (petal, petal, petal, petal);
            s[x, y] = centre;
            s[x + 1, y + 1] = GrassDark;
        }
    }

    private static void Path(Sprite s, Rng r)
    {
        s.Fill(Dirt);
        s.Speckle(r, 22, DirtDark);
        s.Speckle(r, 16, DirtLight);
        for (var i = 0; i < 5; i++)
        {
            var (x, y) = (r.Next(1, 14), r.Next(1, 14));
            (s[x, y], s[x + 1, y]) = (PebbleLight, Pebble);
            (s[x, y + 1], s[x + 1, y + 1]) = (DirtDark, DirtDark);
        }
    }

    private static void Bridge(Sprite s, Rng r)
    {
        for (var plank = 0; plank < 4; plank++)
        {
            var y = plank * 4;
            s.HLine(0, 15, y, WoodLight);
            s.Rect(0, y + 1, 16, 2, Wood);
            s.HLine(0, 15, y + 3, WoodDark);
            s.VLine(plank % 2 == 0 ? 5 : 11, y, y + 2, WoodDark);
            (s[1, y + 1], s[14, y + 1]) = (Iron, Iron);
            for (var i = 0; i < 3; i++)
            {
                s.HLine(r.Next(2, 10), r.Next(10, 14), y + 1 + r.Next(2), Rgba.Hex(0x7c4e24));
            }
        }
    }

    // Shifting the ripples sideways (wrapping, so tiles still join) gives the second frame.
    private static void WaterTile(Sprite s, Rng r, int shift)
    {
        s.Fill(Water);
        s.Speckle(r, 12, WaterDark);
        (int X, int Y, int Length)[] ripples = [(1, 2, 5), (9, 4, 6), (4, 8, 5), (11, 10, 4), (1, 13, 6), (10, 14, 5)];
        foreach (var (x0, y, length) in ripples)
        {
            var x = x0 + shift;
            for (var px = x + 1; px <= x + length - 2; px++)
            {
                s[px % Sprite.Size, y] = WaterLight;
            }

            (s[x % Sprite.Size, y + 1], s[(x + length - 1) % Sprite.Size, y + 1]) = (WaterLight, WaterLight);
            s[(x + (length / 2) - (shift == 0 ? 0 : 1)) % Sprite.Size, y] = WaterFoam;
        }
    }

    private static void Tree(Sprite s, Rng r)
    {
        s.Rect(6, 11, 4, 4, Wood);
        s.VLine(6, 11, 14, WoodLight);
        s.VLine(9, 11, 14, WoodDark);
        (s[5, 14], s[10, 14]) = (Wood, WoodDark);
        s.Ball(8, 6.5, 6.5, 5.5, LeafDark, Leaf, LeafLight);
        s.HLine(6, 9, 12, WoodDark);
        for (var i = 0; i < 24; i++)
        {
            var (x, y) = (r.Next(2, 14), r.Next(1, 12));
            if (s[x, y] == Leaf)
            {
                s[x, y] = y < 6 ? LeafLight : LeafDark;
                s[x + 1, y + 1] = s[x + 1, y + 1] == Leaf ? LeafDark : s[x + 1, y + 1];
            }
        }

        s.Ellipse(5.5, 4, 1.5, 1, Rgba.Hex(0x80c860));
        s.Outline(Ink);
    }

    private static void Bush(Sprite s, Rng r)
    {
        s.Ball(5.5, 10.5, 3.6, 3.5, LeafDark, Leaf, LeafLight);
        s.Ball(10.5, 10.5, 3.6, 3.5, LeafDark, Leaf, LeafLight);
        s.Ball(8, 8.5, 3.8, 3.2, LeafDark, Leaf, LeafLight);
        s.HLine(4, 11, 13, LeafDark);
        s.Speckle(r, 6, LeafDark);
        var berry = Rgba.Hex(0xd83a3a);
        (s[5, 10], s[10, 9], s[8, 12], s[11, 12]) = (berry, berry, berry, berry);
        s.Outline(Ink);
    }

    private static void Fence(Sprite s, Rng r)
    {
        foreach (var y in (int[])[5, 10])
        {
            s.HLine(0, 15, y, WoodLight);
            s.HLine(0, 15, y + 1, Wood);
        }

        foreach (var x in (int[])[2, 12])
        {
            s.Rect(x, 3, 2, 12, Wood);
            s.VLine(x, 3, 14, WoodLight);
            s.VLine(x + 1, 4, 14, WoodDark);
        }

        s.Speckle(r, 3, WoodDark);
        s.Outline(Ink);
    }

    private static void WallBase(Sprite s, Rng r)
    {
        s.Fill(Plaster);
        s.Speckle(r, 14, PlasterDark);
        s.HLine(0, 15, 0, Wood);
        s.HLine(0, 15, 1, WoodDark);
        s.HLine(0, 15, 15, WoodDark);
        s.VLine(0, 0, 15, Wood);
        s.VLine(1, 2, 15, WoodDark);
    }

    private static void HouseWall(Sprite s, Rng r)
    {
        WallBase(s, r);
        s.HLine(2, 15, 8, Wood);
        s.HLine(2, 15, 9, WoodDark);
    }

    private static void RoofTile(Sprite s, Rng r)
    {
        s.Fill(Roof);
        for (var course = 0; course < 4; course++)
        {
            // Rounded shingles: shadowed under the course above, lit along their lower edge.
            var y = course * 4;
            s.HLine(0, 15, y, RoofDark);
            s.HLine(0, 15, y + 3, RoofLight);
            for (var x = course % 2 * 2; x < 16; x += 4)
            {
                s.VLine(x, y + 1, y + 3, RoofDark);
                s[x + 1, y + 3] = Roof;
                s[x + 3, y + 3] = Roof;
            }
        }

        s.Speckle(r, 5, RoofDark.Mix(Roof, 0.5));
    }

    private static void Door(Sprite s, Rng r)
    {
        WallBase(s, r);
        s.Rect(3, 2, 10, 14, WoodDark);
        s.Rect(4, 3, 8, 13, Wood);
        s.HLine(5, 10, 3, WoodLight);
        (s[3, 2], s[12, 2], s[4, 3], s[11, 3]) = (Plaster, Plaster, WoodDark, WoodDark);
        s.VLine(6, 4, 15, WoodDark);
        s.VLine(9, 4, 15, WoodDark);
        s.HLine(4, 11, 5, Iron);
        s.HLine(4, 11, 12, Iron);
        s[10, 9] = Gold;
        s[10, 10] = GoldDark;
    }

    private static void Window(Sprite s, Rng r)
    {
        WallBase(s, r);
        s.Rect(3, 3, 10, 9, WoodDark);
        s.Rect(4, 4, 8, 7, Rgba.Hex(0xf2c85a));
        s.Rect(4, 4, 8, 2, Rgba.Hex(0xfde89a));
        s.Rect(7, 4, 2, 7, WoodDark);
        s.HLine(4, 11, 7, WoodDark);
        (s[5, 5], s[10, 5]) = (Rgba.Hex(0xfffbe0), Rgba.Hex(0xfffbe0));
        s.HLine(2, 13, 12, WoodLight);
        s.HLine(2, 13, 13, WoodDark);
        s.Light(8, 8, 9, Rgba.Hex(0xffe080), 0.25);
    }

    private static void Well(Sprite s, Rng r)
    {
        s.HLine(6, 9, 1, RoofLight);
        s.HLine(4, 11, 2, Roof);
        s.HLine(2, 13, 3, RoofDark);
        s.VLine(3, 4, 9, Wood);
        s.VLine(12, 4, 9, WoodDark);
        s.VLine(8, 4, 6, Rgba.Hex(0xc8b07a));
        s.HLine(2, 13, 9, BrickLight);
        s.HLine(3, 12, 8, BrickTop);
        s.HLine(4, 11, 9, Rgba.Hex(0x16243c));
        s.Rect(7, 7, 3, 2, Wood);
        s.HLine(7, 9, 7, WoodLight);
        s.HLine(2, 13, 10, BrickTop);
        s.Rect(2, 11, 12, 4, Brick);
        s.HLine(2, 13, 12, BrickDark);
        s.HLine(3, 12, 14, BrickDark);
        (s[2, 14], s[13, 14]) = (Rgba.Clear, Rgba.Clear);
        foreach (var x in (int[])[5, 10])
        {
            s[x, 11] = BrickDark;
        }

        foreach (var x in (int[])[3, 8, 12])
        {
            s[x, 13] = BrickDark;
        }

        s.Speckle(r, 3, BrickLight);
        s.Outline(Ink);
    }

    private static void Sign(Sprite s, Rng r)
    {
        s.Rect(7, 8, 2, 7, Wood);
        s.VLine(7, 9, 14, WoodLight);
        s.VLine(8, 9, 14, WoodDark);
        s.Rect(2, 2, 12, 7, Wood);
        s.HLine(2, 13, 2, WoodLight);
        s.HLine(2, 13, 8, WoodDark);
        s.HLine(4, 7, 4, WoodDark);
        s.HLine(9, 11, 4, WoodDark);
        s.HLine(4, 10, 6, WoodDark);
        s.Speckle(r, 2, WoodLight);
        s.Outline(Ink);
    }

    private static void Hatch(Sprite s, Rng r)
    {
        Path(s, r);
        s.HLine(3, 14, 14, DirtDark);
        s.VLine(14, 3, 14, DirtDark);
        s.Rect(2, 2, 12, 12, WoodDark);
        s.Rect(3, 3, 10, 10, Wood);
        s.HLine(3, 12, 3, WoodLight);
        s.VLine(6, 3, 12, WoodDark);
        s.VLine(9, 3, 12, WoodDark);
        foreach (var y in (int[])[5, 10])
        {
            s.HLine(3, 12, y, Iron);
            (s[3, y], s[12, y]) = (SteelDark, SteelDark);
        }

        (s[7, 7], s[8, 7], s[6, 8], s[9, 8]) = (SteelDark, SteelDark, Iron, Iron);
        (s[7, 9], s[8, 9]) = (Iron, Iron);
    }

    private static void StoneFloor(Sprite s, Rng r)
    {
        s.Fill(StoneDark);
        Rgba[] tones = [Stone, Rgba.Hex(0x52525c), Rgba.Hex(0x45454e)];
        (int X0, int Y0, int X1, int Y1)[] stones =
        [
            (0, 0, 3, 6), (5, 0, 11, 6), (13, 0, 15, 6),
            (1, 8, 7, 14), (9, 8, 15, 14),
        ];
        foreach (var (x0, y0, x1, y1) in stones)
        {
            var tone = tones[r.Next(tones.Length)];
            s.Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1, tone);
            s.HLine(x0, x1, y0, StoneLight);
            s[r.Next(x0, x1 + 1), r.Next(y0 + 1, y1 + 1)] = StoneDark;
        }

        s.Speckle(r, 6, Rgba.Hex(0x3a3a44));
    }

    private static void StoneWall(Sprite s, Rng r)
    {
        s.Fill(BrickDark);
        s.HLine(0, 15, 0, BrickTop);
        s.HLine(0, 15, 1, BrickLight);
        Rgba[] tones = [Brick, Rgba.Hex(0x747480), Rgba.Hex(0x64646f)];
        for (var course = 0; course < 4; course++)
        {
            var y = 2 + (course * 4);
            for (var x = (course % 2 * 4) - 8; x < 16; x += 8)
            {
                s.Rect(x, y, 7, 3, tones[r.Next(tones.Length)]);
                s.HLine(x, x + 6, y, BrickLight);
            }
        }

        s.Speckle(r, 8, BrickDark);
    }

    // Five steps between two stone side walls; each step is shaded by its depth.
    private static void Steps(Sprite s, Func<int, Rgba> tone)
    {
        s.Fill(Stone);
        s.VLine(0, 0, 15, StoneLight);
        s.VLine(15, 0, 15, StoneDark);
        for (var step = 0; step < 5; step++)
        {
            var y = 1 + (step * 3);
            var color = tone(step);
            s.HLine(2, 13, y, color.Mix(White, 0.2));
            s.HLine(2, 13, y + 1, color);
            s.HLine(2, 13, y + 2, color.Mix(Ink, 0.45));
        }

        s.VLine(1, 1, 15, StoneDark);
        s.VLine(14, 1, 15, StoneDark);
        s.HLine(1, 14, 0, StoneLight);
    }

    private static void StairsDown(Sprite s, Rng r) =>
        Steps(s, step => BrickLight.Mix(Rgba.Hex(0x101014), step / 4.5));

    private static void StairsUp(Sprite s, Rng r) =>
        Steps(s, step => StoneLight.Mix(BrickTop, (4 - step) / 4.0).Mix(White, (4 - step) / 12.0));

    private static void Rubble(Sprite s, Rng r)
    {
        (int X, int Y, int W, int H)[] stones = [(3, 8, 3, 2), (8, 11, 2, 2), (11, 6, 3, 3), (4, 12, 2, 1), (12, 12, 2, 2), (7, 5, 2, 1)];
        foreach (var (x, y, w, h) in stones)
        {
            s.Rect(x, y, w, h, Brick);
            s.HLine(x, x + w - 1, y, BrickTop);
            if (h > 1)
            {
                s.HLine(x + 1, x + w - 1, y + h - 1, BrickDark);
            }
        }

        s.Speckle(r, 2, BrickLight);
        s.Outline(Ink);
    }

    // The flicker frame has a taller, leaning flame and casts a little less light.
    private static void TorchWall(Sprite s, Rng r, bool flicker)
    {
        StoneWall(s, r);
        s.Light(8, 5, flicker ? 8 : 9, Rgba.Hex(0xffa040), flicker ? 0.38 : 0.45);
        s.HLine(6, 9, 11, Iron);
        (s[6, 10], s[9, 10]) = (Iron, Iron);
        s.VLine(7, 7, 12, Wood);
        s.VLine(8, 7, 12, WoodDark);
        if (flicker)
        {
            s.Ellipse(7.7, 4.8, 1.9, 2.8, Rgba.Hex(0xe85818));
            s.Ellipse(7.9, 5.4, 1.1, 1.8, Rgba.Hex(0xffb030));
            (s[7, 2], s[6, 1]) = (Rgba.Hex(0xe85818), Rgba.Hex(0xe85818));
            (s[7, 6], s[8, 5]) = (Rgba.Hex(0xfff2b0), Rgba.Hex(0xfff2b0));
        }
        else
        {
            s.Ellipse(8, 4.5, 2.2, 3, Rgba.Hex(0xe85818));
            s.Ellipse(8, 5.2, 1.3, 2, Rgba.Hex(0xffb030));
            (s[7, 5], s[8, 5]) = (Rgba.Hex(0xfff2b0), Rgba.Hex(0xfff2b0));
        }
    }

    private static void Crate(Sprite s, Rng r)
    {
        s.Rect(2, 3, 12, 12, WoodDark);
        s.Rect(3, 4, 10, 10, WoodLight);
        s.Rect(4, 5, 8, 8, Wood);
        s.Line(4, 11, 11, 4, WoodLight);
        s.Line(4, 12, 11, 5, WoodDark);
        s.Line(5, 12, 11, 6, WoodDark);
        (s[3, 4], s[12, 4], s[3, 13], s[12, 13]) = (Iron, Iron, Iron, Iron);
        s.Speckle(r, 3, WoodDark);
        s.Outline(Ink);
    }

    private static void Barrel(Sprite s, Rng r)
    {
        for (var x = 3; x <= 12; x++)
        {
            var dx = Math.Abs(x + 0.5 - 8);
            var stave = dx > 4 ? WoodDark : dx > 2 ? Wood : WoodLight;
            var top = x is 3 or 12 ? 4 : 2;
            s.VLine(x, top, x is 3 or 12 ? 12 : 14, stave);
        }

        s.HLine(4, 11, 2, WoodDark);
        s.VLine(6, 3, 14, WoodDark.Mix(Wood, 0.5));
        s.VLine(9, 3, 14, WoodDark.Mix(Wood, 0.5));
        foreach (var y in (int[])[4, 11])
        {
            s.HLine(3, 12, y, Iron);
            s.HLine(6, 9, y, SteelDark);
        }

        s.Speckle(r, 2, WoodDark);
        s.Outline(Ink);
    }
}
