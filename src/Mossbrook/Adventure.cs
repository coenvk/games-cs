using Jentl.Rpg;
using Jentl.Rpg.Maps;
using Jentl.Rpg.World;

namespace Mossbrook;

/// <summary>
/// One playthrough of Mossbrook: the village (a Tiled map), the old cellar below the chapel ruins
/// (generated floors), and the quests. Returning the Elder's amulet wins the game.
/// </summary>
public sealed class Adventure
{
    public const string Village = "village";

    /// <summary>The custom effect the amulet quest's reward runs.</summary>
    public const string VictoryEffect = "victory";

    private Adventure(RpgWorld world) => World = world;

    public RpgWorld World { get; }

    public bool IsWon { get; private set; }

    public int Seed { get; private init; }

    /// <summary>Reads the game's content files and starts in the village.</summary>
    /// <param name="readFile">Reads a content file by name, e.g. "items.json" or "village.tmx".</param>
    /// <param name="seed">Lays out the cellar and drives combat; the same seed plays the same.</param>
    public static Adventure Start(Func<string, string> readFile, int seed)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        var content = LoadContent(readFile);
        var tileset = TmxReader.ReadTileset(readFile("tiles.tsx"));
        var world = new RpgWorld(
            content,
            (w, id) => id == Village ? w.LoadArea(id, TmxReader.ReadMap(readFile("village.tmx"), readFile)) : Cellar.Build(w, id, tileset, seed),
            seed: seed);
        var adventure = new Adventure(world) { Seed = seed };
        world.State.CustomEffect = effect => adventure.IsWon |= effect.Name == VictoryEffect;
        world.Enter(Village);
        world.Log("Welcome to Mossbrook. Walk into people to talk to them.");
        return adventure;
    }

    public static RpgContent LoadContent(Func<string, string> readFile)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        return RpgContent.FromJson(readFile("items.json"), readFile("actors.json"), readFile("quests.json"), readFile("dialogue.json"));
    }
}
