using System.Text.Json;
using System.Text.Json.Nodes;
using Jentl.Collect;
using Jentl.Core.Storage;
using Jentl.Core.Scenes;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>Hot Pan: keep your feet off the hot pan by jumping on chicken that's still cool, for as long as you can.</summary>
internal sealed class HotPanApp : IGameApp
{
    public string Title => "Hot Pan";

    public string SaveName => "HotPan";

    public IReadOnlyCollection<string>? Moods => [Music.Kitchen];

    public void Load(GameContext context, GraphicsDevice device)
    {
        context.Add(new ScoreBoard(context, "hotpan", ScoreOrder.HigherIsBetter, score => score.ToString("N0", Culture)));
        context.Add(new DailyPan(context.Store));
    }

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new HotPanScene(context))),
            new(T("Daily pan"), () => title.Open(new HotPanScene(context, DailyPan.Today, daily: true))),
            new(T("Leaderboard"), () => title.Open(new LeaderboardScene(context))),
        ],
        () => T("Keep your feet off the pan"));
}

/// <summary>The daily pan: the same drops for everyone on a day (seeded by the date), and the best score of each day played.</summary>
internal sealed class DailyPan(ISaveStore? store)
{
    private readonly SavedFile<Dictionary<int, int>> _file = SavedFile<Dictionary<int, int>>.Load(store, "hotpan-daily.json", FromJson, ToJson, () => []);

    /// <summary>Today as yyyymmdd: the day's seed.</summary>
    public static int Today
    {
        get
        {
            var today = DateTime.Now;
            return (today.Year * 10000) + (today.Month * 100) + today.Day;
        }
    }

    public int? Best(int day) => _file.Value.TryGetValue(day, out var best) ? best : null;

    /// <summary>Records a day's score; true when it is the day's best.</summary>
    public bool Record(int day, int score)
    {
        if (Best(day) is { } best && best >= score)
        {
            return false;
        }

        _file.Value[day] = score;
        _file.Save();
        return true;
    }

    private static Dictionary<int, int> FromJson(string json) =>
        JsonNode.Parse(json)?["days"] is JsonObject days ? days.ToDictionary(d => int.Parse(d.Key, Culture), d => (int)d.Value!) : [];

    private static string ToJson(Dictionary<int, int> days) => new JsonObject
    {
        ["version"] = 1,
        ["days"] = new JsonObject(days.OrderBy(d => d.Key).Select(d => KeyValuePair.Create<string, JsonNode?>(d.Key.ToString(Culture), d.Value))),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
