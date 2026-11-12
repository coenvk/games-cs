using System.Text.Json;
using System.Text.Json.Nodes;
using Jentl.Core.Content;
using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using Roadworks;
using static Games.UI.Tr;

namespace Games;

/// <summary>This executable is Roadworks.</summary>
internal static class Entry
{
    public static IGameApp Create() => new RoadworksApp();
}

/// <summary>Roadworks: lay a town's roads and junctions within a budget, then watch the traffic try them.</summary>
internal sealed class RoadworksApp : IGameApp
{
    public string Title => "Roadworks";

    public string SaveName => "Roadworks";

    public void Load(GameContext context, GraphicsDevice device) =>
        context.Add(new TownStore(context.Store, TownLevel.FromJson(GameFiles.ReadAllText("Assets/roadworks/levels.json"))));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new TownScene(context, context.Towns.NextLevel))),
            new(T("Select level"), () => title.Open(new TownLevelsScene(context), fade: false)),
        ],
        () => T("Build the roads, then let the traffic in"));

    public IReadOnlyList<Scene> Start(GameContext context) =>
        context.Options.StartLevel is { } level ? [MainMenu(context), new TownScene(context, level)] : [MainMenu(context)];
}

/// <summary>The levels, the plan the player has laid on each, and the best stars won.</summary>
internal sealed class TownStore
{
    private readonly SavedFile<TownProgress> _file;

    public TownStore(ISaveStore? store, IReadOnlyList<TownLevel> levels)
    {
        Levels = levels;
        _file = SavedFile<TownProgress>.Load(store, "roadworks.json", TownProgress.FromJson, p => p.ToJson(), () => new TownProgress());
    }

    public IReadOnlyList<TownLevel> Levels { get; }

    public int NextLevel => Math.Min(Enumerable.Range(0, Levels.Count).FirstOrDefault(i => Stars(i) == 0, Levels.Count - 1), Levels.Count - 1);

    public bool IsOpen(int level) => level == 0 || Stars(level - 1) > 0;

    public int Stars(int level) => _file.Value.Stars.GetValueOrDefault(level);

    /// <summary>The plan last laid on a level, or an empty one.</summary>
    public Plan PlanFor(int level)
    {
        if (_file.Value.Plans.TryGetValue(level, out var json))
        {
            try
            {
                return Plan.FromJson(Levels[level], json);
            }
            catch (JsonException)
            {
                // A broken plan: start again.
            }
        }

        return new Plan(Levels[level]);
    }

    public void Keep(int level, Plan plan)
    {
        _file.Value.Plans[level] = plan.ToJson();
        _file.Save();
    }

    /// <summary>Records stars won; true when more than before.</summary>
    public bool Record(int level, int stars)
    {
        if (stars <= Stars(level))
        {
            return false;
        }

        _file.Value.Stars[level] = stars;
        _file.Save();
        return true;
    }
}

internal sealed class TownProgress
{
    public Dictionary<int, int> Stars { get; } = [];

    public Dictionary<int, string> Plans { get; } = [];

    public static TownProgress FromJson(string json)
    {
        var progress = new TownProgress();
        var root = JsonNode.Parse(json);
        foreach (var (level, stars) in root?["stars"]?.AsObject() ?? [])
        {
            progress.Stars[int.Parse(level, Culture)] = (int)stars!;
        }

        foreach (var (level, plan) in root?["plans"]?.AsObject() ?? [])
        {
            progress.Plans[int.Parse(level, Culture)] = (string)plan!;
        }

        return progress;
    }

    public string ToJson() => new JsonObject
    {
        ["version"] = 1,
        ["stars"] = new JsonObject(Stars.OrderBy(s => s.Key).Select(s => KeyValuePair.Create<string, JsonNode?>(s.Key.ToString(Culture), s.Value))),
        ["plans"] = new JsonObject(Plans.OrderBy(p => p.Key).Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key.ToString(Culture), p.Value))),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Roadworks' part of the context.</summary>
internal static class TownContext
{
    extension(GameContext context)
    {
        public TownStore Towns => context.Get<TownStore>();
    }
}
