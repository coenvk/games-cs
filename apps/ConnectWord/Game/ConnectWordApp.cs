using System.Text.Json;
using System.Text.Json.Nodes;
using ConnectWord;
using Jentl.Core.Content;
using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Games.Input;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Connect Word.</summary>
internal static class Entry
{
    public static IGameApp Create() => new ConnectWordApp();
}

/// <summary>Connect Word: put words of a category together, by merging cards or lining them up in a row.</summary>
internal sealed class ConnectWordApp : IGameApp
{
    public string Title => "Connect Word";

    public string SaveName => "ConnectWord";

    public IReadOnlyList<GameAction> ControlActions => [GameAction.Up, GameAction.Down, GameAction.Left, GameAction.Right, GameAction.Confirm, GameAction.Hint, GameAction.Restart];

    public void Load(GameContext context, GraphicsDevice device) =>
        context.Add(new WordStore(context.Store, WordLevels.FromJson(GameFiles.ReadAllText("Assets/connectword/levels.json"))));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new WordPlayScene(context, context.Words.NextLevel))),
            new(T("Select level"), () => title.Open(new WordLevelsScene(context), fade: false)),
        ],
        () => T("Put the words that belong together in one row"));

    public IReadOnlyList<Scene> Start(GameContext context) =>
        context.Options.StartLevel is { } level ? [MainMenu(context), new WordPlayScene(context, level)] : [MainMenu(context)];
}

/// <summary>The levels, and how far the player got: the best moves and stars per level won.</summary>
internal sealed class WordStore
{
    private readonly SavedFile<WordProgress> _file;

    public WordStore(ISaveStore? store, IReadOnlyList<WordLevel> levels)
    {
        Levels = levels;
        _file = SavedFile<WordProgress>.Load(store, "connectword.json", WordProgress.FromJson, p => p.ToJson(), () => new WordProgress());
    }

    public IReadOnlyList<WordLevel> Levels { get; }

    /// <summary>The first level not yet won (the last one once all are).</summary>
    public int NextLevel => Math.Min(Enumerable.Range(0, Levels.Count).FirstOrDefault(i => Best(i) is null, Levels.Count - 1), Levels.Count - 1);

    /// <summary>A level is open once the one before it is won.</summary>
    public bool IsOpen(int level) => level == 0 || Best(level - 1) is not null;

    public (int Moves, int Stars)? Best(int level) => _file.Value.Best.TryGetValue(level, out var best) ? best : null;

    /// <summary>Records a win; true when it beats the best so far.</summary>
    public bool Record(int level, int moves, int stars)
    {
        if (Best(level) is { } best && best.Moves <= moves)
        {
            return false;
        }

        _file.Value.Best[level] = (moves, stars);
        _file.Save();
        return true;
    }
}

internal sealed class WordProgress
{
    public Dictionary<int, (int Moves, int Stars)> Best { get; } = [];

    public static WordProgress FromJson(string json)
    {
        var progress = new WordProgress();
        if (JsonNode.Parse(json)?["best"] is JsonObject best)
        {
            foreach (var (level, value) in best)
            {
                progress.Best[int.Parse(level, Culture)] = ((int)value!["moves"]!, (int)value["stars"]!);
            }
        }

        return progress;
    }

    public string ToJson() => new JsonObject
    {
        ["version"] = 1,
        ["best"] = new JsonObject(Best.OrderBy(b => b.Key).Select(b => KeyValuePair.Create<string, JsonNode?>(
            b.Key.ToString(Culture), new JsonObject { ["moves"] = b.Value.Moves, ["stars"] = b.Value.Stars }))),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Connect Word's part of the context.</summary>
internal static class WordContext
{
    extension(GameContext context)
    {
        public WordStore Words => context.Get<WordStore>();
    }
}
