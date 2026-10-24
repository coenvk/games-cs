using Jentl.Core.Content;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Model;

namespace Sokoban.Desktop.Rendering;

/// <summary>The sprites for one level. Each level gets its own floor, wall and crate colour.</summary>
internal sealed record Theme(Sprite Floor, Sprite Wall, Sprite Goal, Sprite Crate, Sprite CrateOnGoal);

/// <summary>
/// The sprite set, from the sprite atlas (<c>Assets/sprites.atlas.json</c>, packed from <c>Art/sprites</c> by the
/// Assets tool). Crates come in eight colours, each with a light
/// variant and a dark variant (used for a crate on a goal) and a goal marker of the same colour.
/// </summary>
internal sealed class Tileset : IDisposable
{
    public const int TileSize = 56;

    private const int ColorCount = 8;

    public const string AtlasPath = "Assets/sprites.atlas.json";

    private readonly TextureAtlas _atlas;
    private readonly Texture2D _ice;

    public Tileset(GraphicsDevice device, AssetStore assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _atlas = assets.Atlas(AtlasPath);
        _ice = CreateIce(device);
    }

    /// <summary>Picks a theme for a level; the same level always gets the same theme.</summary>
    /// <summary>Ice drawn in place of the floor. Generated, as the sprite pack has no ice tile.</summary>
    public Sprite Ice => Sprite.Whole(_ice);

    public Theme ThemeFor(int levelNumber)
    {
#pragma warning disable CA5394 // Cosmetic randomness, not security relevant.
        var random = new Random(levelNumber);
        var color = random.Next(ColorCount);
        return new Theme(
            Floor: Numbered("floors/floor", random.Next(8)),
            Wall: Numbered("walls/wall", random.Next(4)),
            Goal: Numbered("areas/area", color),
            Crate: Numbered("crates/crate", LightCrate(color)),
            CrateOnGoal: Numbered("crates/crate", LightCrate(color) + 4));
#pragma warning restore CA5394
    }

    /// <summary>The player frames facing <paramref name="direction"/>.</summary>
    public Sprite[] PlayerFrames(Direction direction) => direction switch
    {
        Direction.Down => Frames(0, 3),
        Direction.Up => Frames(3, 3),
        Direction.Right => Frames(6, 2),
        Direction.Left => Frames(8, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    // The atlas belongs to the asset store; only the generated ice is this tileset's own.
    public void Dispose() => _ice.Dispose();

    private static Texture2D CreateIce(GraphicsDevice device)
    {
        const int size = TileSize;
        var top = new Color(218, 243, 252);
        var bottom = new Color(168, 218, 242);
        var edge = new Color(128, 186, 222);
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var color = Color.Lerp(top, bottom, (x + y) / (2f * (size - 1)));

                // Two thin diagonal shine streaks.
                var diagonal = (x - y + size) % size;
                if (diagonal is >= 14 and < 17 || diagonal is >= 36 and < 38)
                {
                    color = Color.Lerp(color, Color.White, 0.65f);
                }

                if (x < 2 || y < 2 || x >= size - 2 || y >= size - 2)
                {
                    color = Color.Lerp(color, edge, 0.6f);
                }

                pixels[y * size + x] = color;
            }
        }

        var texture = new Texture2D(device, size, size);
        texture.SetData(pixels);
        return texture;
    }

    // Files 1-4 and 9-12 are light crates; the dark variant of each is four files later.
    private static int LightCrate(int color) => color < 4 ? color : color + 4;

    /// <summary>The sprite numbered <paramref name="index"/> + 1 in a series ("crates/crate" 0 is "crates/crate1").</summary>
    private Sprite Numbered(string series, int index) => _atlas[series + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)];

    private Sprite[] Frames(int first, int count) => [.. Enumerable.Range(first, count).Select(i => Numbered("players/player", i))];
}
