using System.Text.Json;
using System.Text.Json.Nodes;
using AssociateWords;
using Jentl.Core.Content;
using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Games.Input;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Associate Words.</summary>
internal static class Entry
{
    public static IGameApp Create() => new AssociateWordsApp();
}

/// <summary>Associate Words: a solitaire of categories, words gathered under their category cards.</summary>
internal sealed class AssociateWordsApp : IGameApp
{
    public string Title => "Associate Words";

    public string SaveName => "AssociateWords";

    public IReadOnlyList<GameAction> ControlActions => [GameAction.Undo, GameAction.Hint, GameAction.Confirm, GameAction.Restart];

    public void Load(GameContext context, GraphicsDevice device)
    {
        CardTableArt.Load(context, device);
        context.Add(new AssociateStore(context.Store, AssociateLevels.FromJson(GameFiles.ReadAllText("Assets/associatewords/levels.json"))));
    }

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new AssociateScene(context, context.Associate.NextLevel))),
            new(T("Select level"), () => title.Open(new AssociateLevelsScene(context), fade: false)),
        ],
        () => T("Gather the words under their categories"));

    public IReadOnlyList<Scene> Start(GameContext context) =>
        context.Options.StartLevel is { } level ? [MainMenu(context), new AssociateScene(context, level)] : [MainMenu(context)];
}

/// <summary>The levels, and how far the player got: the best stars and moves per level won.</summary>
internal sealed class AssociateStore
{
    private readonly SavedFile<Dictionary<int, (int Moves, int Stars)>> _file;

    public AssociateStore(ISaveStore? store, IReadOnlyList<AssociateLevel> levels)
    {
        Levels = levels;
        _file = SavedFile<Dictionary<int, (int Moves, int Stars)>>.Load(store, "associatewords.json", FromJson, ToJson, () => []);
    }

    public IReadOnlyList<AssociateLevel> Levels { get; }

    /// <summary>The first level not yet won (the last one once all are).</summary>
    public int NextLevel => Math.Min(Enumerable.Range(0, Levels.Count).FirstOrDefault(i => Best(i) is null, Levels.Count - 1), Levels.Count - 1);

    /// <summary>A level is open once the one before it is won.</summary>
    public bool IsOpen(int level) => level == 0 || Best(level - 1) is not null;

    public (int Moves, int Stars)? Best(int level) => _file.Value.TryGetValue(level, out var best) ? best : null;

    /// <summary>Records a win; true when it beats the best so far (more stars, or as many in fewer moves).</summary>
    public bool Record(int level, int moves, int stars)
    {
        if (Best(level) is { } best && (best.Stars > stars || (best.Stars == stars && best.Moves <= moves)))
        {
            return false;
        }

        _file.Value[level] = (moves, stars);
        _file.Save();
        return true;
    }

    private static Dictionary<int, (int Moves, int Stars)> FromJson(string json)
    {
        var best = new Dictionary<int, (int Moves, int Stars)>();
        if (JsonNode.Parse(json)?["best"] is JsonObject levels)
        {
            foreach (var (level, value) in levels)
            {
                best[int.Parse(level, Culture)] = ((int)value!["moves"]!, (int)value["stars"]!);
            }
        }

        return best;
    }

    private static string ToJson(Dictionary<int, (int Moves, int Stars)> best) => new JsonObject
    {
        ["version"] = 1,
        ["best"] = new JsonObject(best.OrderBy(b => b.Key).Select(b => KeyValuePair.Create<string, JsonNode?>(
            b.Key.ToString(Culture), new JsonObject { ["moves"] = b.Value.Moves, ["stars"] = b.Value.Stars }))),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Associate Words' part of the context.</summary>
internal static class AssociateContext
{
    extension(GameContext context)
    {
        public AssociateStore Associate => context.Get<AssociateStore>();
    }
}
