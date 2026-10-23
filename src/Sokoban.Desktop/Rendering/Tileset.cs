using Jentl.Core.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Model;

namespace Sokoban.Desktop.Rendering;

/// <summary>The sprites for one level. Each level gets its own floor, wall and crate colour.</summary>
internal sealed record Theme(Texture2D Floor, Texture2D Wall, Texture2D Goal, Texture2D Crate, Texture2D CrateOnGoal);

/// <summary>
/// Loads the sprite set from <c>Assets/</c>. Crates come in eight colours, each with a light
/// variant and a dark variant (used for a crate on a goal) and a goal marker of the same colour.
/// </summary>
internal sealed class Tileset : IDisposable
{
    public const int TileSize = 56;

    private const int ColorCount = 8;

    private readonly Texture2D[] _floors;
    private readonly Texture2D[] _walls;
    private readonly Texture2D[] _goals;
    private readonly Texture2D[] _crates;
    private readonly Texture2D[] _players;
    private readonly Texture2D _ice;

    public Tileset(GraphicsDevice device)
    {
        _floors = Load(device, "floors/floor", 8);
        _walls = Load(device, "walls/wall", 4);
        _goals = Load(device, "areas/area", ColorCount);
        _crates = Load(device, "crates/crate", 2 * ColorCount);
        _players = Load(device, "players/player", 10);
        _ice = CreateIce(device);
    }

    /// <summary>Picks a theme for a level; the same level always gets the same theme.</summary>
    /// <summary>Ice drawn in place of the floor. Generated, as the sprite pack has no ice tile.</summary>
    public Texture2D Ice => _ice;

    public Theme ThemeFor(int levelNumber)
    {
#pragma warning disable CA5394 // Cosmetic randomness, not security relevant.
        var random = new Random(levelNumber);
        var color = random.Next(ColorCount);
        return new Theme(
            Floor: _floors[random.Next(_floors.Length)],
            Wall: _walls[random.Next(_walls.Length)],
            Goal: _goals[color],
            Crate: _crates[LightCrate(color)],
            CrateOnGoal: _crates[LightCrate(color) + 4]);
#pragma warning restore CA5394
    }

    /// <summary>The player frames facing <paramref name="direction"/>.</summary>
    public ReadOnlySpan<Texture2D> PlayerFrames(Direction direction) => direction switch
    {
        Direction.Down => _players.AsSpan(0, 3),
        Direction.Up => _players.AsSpan(3, 3),
        Direction.Right => _players.AsSpan(6, 2),
        Direction.Left => _players.AsSpan(8, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    public void Dispose()
    {
        foreach (var texture in _floors.Concat(_walls).Concat(_goals).Concat(_crates).Concat(_players))
        {
            texture.Dispose();
        }

        _ice.Dispose();
    }

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

    private static Texture2D[] Load(GraphicsDevice device, string prefix, int count) =>
        Enumerable.Range(1, count)
            .Select(i => GameFiles.LoadTexture(device, $"Assets/{prefix}{i}.png"))
            .ToArray();
}
