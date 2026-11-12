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
        context.Add(new AssociateStore(
            context.Store,
            AssociateLevels.FromJson(GameFiles.ReadAllText("Assets/associatewords/levels.json")),
            new Dictionary<string, Func<IReadOnlyList<WordGroup>>> { ["nl"] = () => AssociateLevels.GroupsFromJson(GameFiles.ReadAllText("Assets/associatewords/groups.nl.json")) }));
    }

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new AssociateScene(context, context.Associate.NextLevel))),
            new(T("Select level"), () => title.Open(new AssociateLevelsScene(context), fade: false)),
            new(T("Daily deal"), () => title.Open(new AssociateScene(context, AssociatePuzzle.Daily(context, DateOnly.FromDateTime(DateTime.Now))))),
            new(T("Custom game"), () => title.Open(new AssociateCustomScene(context), fade: false)),
        ],
        () => T("Gather the words under their categories"));

    public IReadOnlyList<Scene> Start(GameContext context) =>
        context.Options.StartLevel is { } level ? [MainMenu(context), new AssociateScene(context, level)] : [MainMenu(context)];
}

/// <summary>What is being played: a level of the list, the day's deal, or a custom deal of a size.</summary>
internal enum AssociateKind
{
    Level,
    Daily,
    Custom,
}

/// <summary>A deal to play: its kind, its number (the level from 0, the date as yyyymmdd, or the seed) and the level.</summary>
internal sealed record AssociatePuzzle(AssociateKind Kind, int Number, AssociateLevel Level, DealSize Size = DealSize.Medium)
{
    public static AssociatePuzzle FromList(GameContext context, int level)
    {
        var levels = context.Associate.Levels;
        var number = Math.Clamp(level, 0, levels.Count - 1);
        return new AssociatePuzzle(AssociateKind.Level, number, levels[number]);
    }

    /// <summary>The day's deal, medium-sized: the same for everyone playing in the same language that day.</summary>
    public static AssociatePuzzle Daily(GameContext context, DateOnly date)
    {
        var seed = AssociateDeals.DailySeed(date);
        return new AssociatePuzzle(AssociateKind.Daily, seed, AssociateDeals.Make(context.Associate.Bank, seed, DealSize.Medium));
    }

    public static AssociatePuzzle Custom(GameContext context, DealSize size)
    {
#pragma warning disable CA5394 // A deal's seed, not security relevant.
        var seed = context.Deal ?? Random.Shared.Next(1, 1_000_000);
#pragma warning restore CA5394
        return new AssociatePuzzle(AssociateKind.Custom, seed, AssociateDeals.Make(context.Associate.Bank, seed, size), size);
    }

    public string Title => Kind switch
    {
        AssociateKind.Level => T("Level {0}", Number + 1),
        AssociateKind.Daily => T("Daily deal"),
        _ => T("Deal {0}", Number),
    };
}

/// <summary>
/// The levels in the language being played, and how far the player got in each: the best stars and moves per level
/// won, and the daily deals done. English has its made levels; another language with a bank of categories gets the
/// same levels' shapes filled from it (made when first played), and its own save file.
/// </summary>
internal sealed class AssociateStore
{
    private readonly IReadOnlyList<AssociateLevel> _english;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<WordGroup>>> _banks;
    private readonly Dictionary<string, (IReadOnlyList<WordGroup> Bank, IReadOnlyList<AssociateLevel> Levels)> _languages = [];
    private readonly Dictionary<string, SavedFile<AssociateProgress>> _files = [];
    private readonly ISaveStore? _store;

    public AssociateStore(ISaveStore? store, IReadOnlyList<AssociateLevel> english, IReadOnlyDictionary<string, Func<IReadOnlyList<WordGroup>>> banks)
    {
        _store = store;
        _english = english;
        _banks = banks;
    }

    private string Language => _banks.ContainsKey(Current.Language) ? Current.Language : "en";

    private (IReadOnlyList<WordGroup> Bank, IReadOnlyList<AssociateLevel> Levels) Set
    {
        get
        {
            var language = Language;
            if (!_languages.TryGetValue(language, out var set))
            {
                if (language == "en")
                {
                    set = (AssociateDeals.Bank(_english), _english);
                }
                else
                {
                    var bank = _banks[language]();
                    set = (bank, AssociateDeals.Translate(_english, bank));
                }

                _languages[language] = set;
            }

            return set;
        }
    }

    public IReadOnlyList<AssociateLevel> Levels => Set.Levels;

    /// <summary>The categories deals are made from.</summary>
    public IReadOnlyList<WordGroup> Bank => Set.Bank;

    /// <summary>The first level not yet won (the last one once all are).</summary>
    public int NextLevel => Math.Min(Enumerable.Range(0, Levels.Count).FirstOrDefault(i => Best(i) is null, Levels.Count - 1), Levels.Count - 1);

    private SavedFile<AssociateProgress> File
    {
        get
        {
            var language = Language;
            if (!_files.TryGetValue(language, out var file))
            {
                var name = language == "en" ? "associatewords.json" : $"associatewords-{language}.json";
                _files[language] = file = SavedFile<AssociateProgress>.Load(_store, name, AssociateProgress.FromJson, p => p.ToJson(), () => new AssociateProgress());
            }

            return file;
        }
    }

    /// <summary>A level is open once the one before it is won.</summary>
    public bool IsOpen(int level) => level == 0 || Best(level - 1) is not null;

    public (int Moves, int Stars)? Best(int level) => File.Value.Best.TryGetValue(level, out var best) ? best : null;

    public (int Moves, int Stars)? Daily(int date) => File.Value.Daily.TryGetValue(date, out var done) ? done : null;

    /// <summary>Records a win; true when it beats the best so far (more stars, or as many in fewer moves).</summary>
    public bool Record(int level, int moves, int stars) => Record(File.Value.Best, level, moves, stars);

    public bool RecordDaily(int date, int moves, int stars) => Record(File.Value.Daily, date, moves, stars);

    /// <summary>Days in a row, up to <paramref name="today"/>, with the daily deal won.</summary>
    public int Streak(DateOnly today)
    {
        var streak = 0;
        for (var day = today; Daily(AssociateDeals.DailySeed(day)) is not null; day = day.AddDays(-1))
        {
            streak++;
        }

        return streak;
    }

    private bool Record(Dictionary<int, (int Moves, int Stars)> records, int key, int moves, int stars)
    {
        if (records.TryGetValue(key, out var best) && (best.Stars > stars || (best.Stars == stars && best.Moves <= moves)))
        {
            return false;
        }

        records[key] = (moves, stars);
        File.Save();
        return true;
    }
}

internal sealed class AssociateProgress
{
    public Dictionary<int, (int Moves, int Stars)> Best { get; } = [];

    public Dictionary<int, (int Moves, int Stars)> Daily { get; } = [];

    public static AssociateProgress FromJson(string json)
    {
        var progress = new AssociateProgress();
        var root = JsonNode.Parse(json);
        Read(root?["best"], progress.Best);
        Read(root?["daily"], progress.Daily);
        return progress;

        static void Read(JsonNode? node, Dictionary<int, (int Moves, int Stars)> into)
        {
            if (node is JsonObject records)
            {
                foreach (var (key, value) in records)
                {
                    into[int.Parse(key, Culture)] = ((int)value!["moves"]!, (int)value["stars"]!);
                }
            }
        }
    }

    public string ToJson() => new JsonObject
    {
        ["version"] = 1,
        ["best"] = Write(Best),
        ["daily"] = Write(Daily),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static JsonObject Write(Dictionary<int, (int Moves, int Stars)> records) => new(records.OrderBy(b => b.Key).Select(b => KeyValuePair.Create<string, JsonNode?>(
        b.Key.ToString(Culture), new JsonObject { ["moves"] = b.Value.Moves, ["stars"] = b.Value.Stars })));
}

/// <summary>Associate Words' part of the context.</summary>
internal static class AssociateContext
{
    extension(GameContext context)
    {
        public AssociateStore Associate => context.Get<AssociateStore>();
    }
}
