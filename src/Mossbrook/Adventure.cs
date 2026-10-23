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

    /// <summary>The story flag the amulet quest sets; a loaded game with it is already won.</summary>
    public const string WonFlag = "amulet_returned";

    /// <summary>The house interiors, each a Tiled map named after its area ("house_elder.tmx").</summary>
    public static IReadOnlyList<string> Houses { get; } = ["house_elder", "house_herbalist", "house_smith", "house_farmer"];

    private Adventure(RpgWorld world)
    {
        World = world;
        world.State.CustomEffect = effect => IsWon |= effect.Name == VictoryEffect;
    }

    public RpgWorld World { get; }

    public bool IsWon { get; private set; }

    /// <summary>Reads the game's content files and starts in the village.</summary>
    /// <param name="readFile">Reads a content file by name, e.g. "items.json" or "village.tmx".</param>
    /// <param name="seed">Lays out the cellar and drives combat; the same seed plays the same.</param>
    public static Adventure Start(Func<string, string> readFile, int seed)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        var adventure = new Adventure(new RpgWorld(LoadContent(readFile), Areas(readFile), seed: seed));
        adventure.World.Enter(Village);
        adventure.World.Log("Welcome to Mossbrook. Walk into people to talk to them.");
        return adventure;
    }

    /// <summary>Carries on a game saved with <see cref="Save"/>; throws <see cref="ContentException"/> for a save that cannot be read.</summary>
    public static Adventure Load(Func<string, string> readFile, string save)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        var adventure = new Adventure(RpgWorld.Load(LoadContent(readFile), Areas(readFile), save));
        adventure.IsWon = adventure.World.State.Flags.Contains(WonFlag);
        return adventure;
    }

    public static RpgContent LoadContent(Func<string, string> readFile)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        return RpgContent.FromJson(readFile("items.json"), readFile("actors.json"), readFile("quests.json"), readFile("dialogue.json"));
    }

    public string Save() => World.Save();

    /// <summary>The village and the houses come from their Tiled maps; the cellar floors are generated from the game's seed.</summary>
    private static Func<RpgWorld, string, Area> Areas(Func<string, string> readFile)
    {
        var tileset = TmxReader.ReadTileset(readFile("tiles.tsx"));
        return (w, id) => id == Village || Houses.Contains(id)
            ? w.LoadArea(id, TmxReader.ReadMap(readFile($"{id}.tmx"), readFile))
            : Cellar.Build(w, id, tileset, w.Seed);
    }
}
