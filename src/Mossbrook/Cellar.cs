using Jentl.Puzzle;
using Jentl.Rpg.Maps;
using Jentl.Rpg.World;

namespace Mossbrook;

/// <summary>
/// The old cellar: dark floors of rooms and corridors generated from the game's seed. Stairs up lead
/// back (to the village from the first floor); the first floor's stairs down lead to the second, where
/// the amulet waits in the last room.
/// </summary>
internal static class Cellar
{
    public const int Floors = 2;

    public static string FloorId(int depth) => $"cellar{depth}";

    public static Area Build(RpgWorld world, string id, Tileset tileset, int seed)
    {
        var depth = id.StartsWith("cellar", StringComparison.Ordinal) && int.TryParse(id.AsSpan("cellar".Length), out var d) && d is >= 1 and <= Floors
            ? d
            : throw new ArgumentException($"There is no area '{id}'.", nameof(id));
        var random = new Random(unchecked(seed * 31 + depth));
        var layout = new DungeonGenerator { Width = 34, Height = 22, MaxRooms = 7 + depth, MaxRoomSize = 8 }.Generate(random.Next());

        var set = new MapTileset(1, tileset);
        var ground = new TileLayer("ground", layout.Width, layout.Height);
        var decor = new TileLayer("decor", layout.Width, layout.Height);
#pragma warning disable CA5394 // Level generation, not security relevant.
        foreach (var cell in layout.FloorCells)
        {
            ground[cell] = set.Gid("stone_floor");
        }

        foreach (var cell in layout.EdgeWalls)
        {
            ground[cell] = set.Gid(random.Next(9) == 0 ? "torch_wall" : "stone_wall");
        }

        var map = new TileMap(layout.Width, layout.Height, tileset.TileWidth, tileset.TileHeight, [set], [ground, decor]);
        var area = new Area(id, map) { IsLit = false, Name = $"Old cellar, level {depth}" };

        decor[layout.Entrance] = set.Gid("stairs_up");
        area.Spawns["start"] = layout.Entrance;
        area.Portals.Add(depth == 1 ? new Portal(layout.Entrance, Adventure.Village, "hatch") : new Portal(layout.Entrance, FloorId(depth - 1), "down"));
        if (depth < Floors)
        {
            decor[layout.Exit] = set.Gid("stairs_down");
            area.Spawns["down"] = layout.Exit;
            area.Portals.Add(new Portal(layout.Exit, FloorId(depth + 1), "start"));
        }
        else
        {
            world.Place(area, "amulet", 1, layout.Exit);
        }

        // Everything else goes on free cells of the rooms after the first, so the stairs are safe.
        var free = layout.Rooms.Skip(1).SelectMany(r => r.Cells).Where(c => c != layout.Exit).ToList();
        Cell Take()
        {
            var index = random.Next(free.Count);
            var cell = free[index];
            free.RemoveAt(index);
            return cell;
        }

        foreach (var room in layout.Rooms.Skip(1))
        {
            for (var i = random.Next(1, 3); i > 0 && free.Count > 0; i--)
            {
                var roll = random.NextDouble();
                var kind = depth == 1 ? (roll < 0.7 ? "rat" : "slime") : roll < 0.4 ? "skeleton" : roll < 0.8 ? "spider" : "slime";
                var spots = free.Where(room.Contains).ToList();
                if (spots.Count == 0)
                {
                    break;
                }

                var spot = spots[random.Next(spots.Count)];
                free.Remove(spot);
                world.Spawn(area, kind, spot);
            }
        }

        foreach (var (item, count) in new[] { ("glowcap", 1), ("glowcap", 1), ("potion", 1), ("coins", 3 + depth * 2), ("coins", 4) })
        {
            if (free.Count > 0)
            {
                world.Place(area, item, count, Take());
            }
        }

        for (var i = 0; i < 6 && free.Count > 0; i++)
        {
            decor[Take()] = set.Gid("rubble");
        }
#pragma warning restore CA5394

        return area;
    }
}
