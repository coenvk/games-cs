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

/// <summary>Connect Word: put words of a category together, by merging cards or lining them up in a row.</summary>
internal sealed class ConnectWordApp : IGameApp
{
    public string Title => "Connect Word";

    public string SaveName => "ConnectWord";

    public IReadOnlyCollection<string>? Moods => [Music.Words];

    public IReadOnlyList<GameAction> ControlActions => [GameAction.Up, GameAction.Down, GameAction.Left, GameAction.Right, GameAction.Confirm, GameAction.Hint, GameAction.Undo, GameAction.Restart];

    public void Load(GameContext context, GraphicsDevice device) =>
        context.Add(new WordStore(context.Store, new Dictionary<string, IReadOnlyList<WordLevel>>
        {
            ["en"] = WordLevels.FromJson(GameFiles.ReadAllText("Assets/connectword/levels.json")),
            ["nl"] = WordLevels.FromJson(GameFiles.ReadAllText("Assets/connectword/levels.nl.json")),
        }));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(T("Play"), () => title.Open(new WordPlayScene(context, context.Words.NextLevel))),
            new(T("Select level"), () => title.Open(new WordLevelsScene(context), fade: false)),
            new(T("Daily puzzle"), () => title.Open(new WordPlayScene(context, WordPuzzle.Daily(context, DateOnly.FromDateTime(DateTime.Now))))),
            new(T("Random puzzle"), () => title.Open(new WordPlayScene(context, WordPuzzle.Random(context)))),
        ],
        () => T("Put the words that belong together in one row"));

    public IReadOnlyList<Scene> Start(GameContext context) =>
        context.Options.StartLevel is { } level ? [MainMenu(context), new WordPlayScene(context, level)] : [MainMenu(context)];
}

/// <summary>What is being played: a level of the list, the day's puzzle, or a random one.</summary>
internal enum PuzzleKind
{
    Level,
    Daily,
    Random,
}

/// <summary>A puzzle to play: its kind, its number (the level from 0, the date as yyyymmdd, or the seed), the level and the deal.</summary>
internal sealed record WordPuzzle(PuzzleKind Kind, int Number, WordLevel Level, int Seed)
{
    public static WordPuzzle FromList(GameContext context, int level)
    {
        var levels = context.Words.Levels;
        var number = Math.Clamp(level, 0, levels.Count - 1);
        return new WordPuzzle(PuzzleKind.Level, number, levels[number], (context.Deal ?? 0) + number + 1);
    }

    /// <summary>The day's puzzle: the same for everyone playing in the same language that day.</summary>
    public static WordPuzzle Daily(GameContext context, DateOnly date)
    {
        var seed = WordBank.DailySeed(date);
        return new WordPuzzle(PuzzleKind.Daily, seed, WordBank.Generate(context.Words.Levels, seed), seed);
    }

    public static WordPuzzle Random(GameContext context)
    {
#pragma warning disable CA5394 // A puzzle's seed, not security relevant.
        var seed = context.Deal ?? System.Random.Shared.Next(1, 1_000_000);
#pragma warning restore CA5394
        return new WordPuzzle(PuzzleKind.Random, seed, WordBank.Generate(context.Words.Levels, seed), seed);
    }

    public string Title => Kind switch
    {
        PuzzleKind.Level => T("Level {0}", Number + 1),
        PuzzleKind.Daily => T("Daily puzzle"),
        _ => T("Puzzle {0}", Number),
    };
}

/// <summary>
/// The levels in the language being played (Dutch has its own; the rest play the English ones), and how far the
/// player got in each: the best moves and stars per level won, and the daily puzzles done with their times.
/// </summary>
internal sealed class WordStore
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<WordLevel>> _levels;
    private readonly Dictionary<string, SavedFile<WordProgress>> _files = [];
    private readonly ISaveStore? _store;

    public WordStore(ISaveStore? store, IReadOnlyDictionary<string, IReadOnlyList<WordLevel>> levels)
    {
        _store = store;
        _levels = levels;
    }

    private string Language => _levels.ContainsKey(Current.Language) ? Current.Language : "en";

    public IReadOnlyList<WordLevel> Levels => _levels[Language];

    /// <summary>The first level not yet won (the last one once all are).</summary>
    public int NextLevel => Math.Min(Enumerable.Range(0, Levels.Count).FirstOrDefault(i => Best(i) is null, Levels.Count - 1), Levels.Count - 1);

    private SavedFile<WordProgress> File
    {
        get
        {
            var language = Language;
            if (!_files.TryGetValue(language, out var file))
            {
                // English keeps the file it always had; other languages have their own.
                var name = language == "en" ? "connectword.json" : $"connectword-{language}.json";
                _files[language] = file = SavedFile<WordProgress>.Load(_store, name, WordProgress.FromJson, p => p.ToJson(), () => new WordProgress());
            }

            return file;
        }
    }

    /// <summary>A level is open once the one before it is won.</summary>
    public bool IsOpen(int level) => level == 0 || Best(level - 1) is not null;

    public (int Moves, int Stars)? Best(int level) => File.Value.Best.TryGetValue(level, out var best) ? best : null;

    /// <summary>The time and moves of a day's puzzle, if it was done.</summary>
    public (int Seconds, int Moves)? Daily(int date) => File.Value.Daily.TryGetValue(date, out var done) ? done : null;

    /// <summary>Records a win; true when it beats the best so far.</summary>
    public bool Record(int level, int moves, int stars)
    {
        if (Best(level) is { } best && best.Moves <= moves)
        {
            return false;
        }

        File.Value.Best[level] = (moves, stars);
        File.Save();
        return true;
    }

    /// <summary>Records a day's puzzle done; true when it beats an earlier time that day.</summary>
    public bool RecordDaily(int date, int seconds, int moves)
    {
        if (Daily(date) is { } done && done.Seconds <= seconds)
        {
            return false;
        }

        File.Value.Daily[date] = (seconds, moves);
        File.Save();
        return true;
    }

    /// <summary>Days in a row, up to <paramref name="today"/>, with the daily puzzle done.</summary>
    public int Streak(DateOnly today)
    {
        var streak = 0;
        for (var day = today; Daily(WordBank.DailySeed(day)) is not null; day = day.AddDays(-1))
        {
            streak++;
        }

        return streak;
    }
}

internal sealed class WordProgress
{
    public Dictionary<int, (int Moves, int Stars)> Best { get; } = [];

    public Dictionary<int, (int Seconds, int Moves)> Daily { get; } = [];

    public static WordProgress FromJson(string json)
    {
        var progress = new WordProgress();
        var root = JsonNode.Parse(json);
        if (root?["best"] is JsonObject best)
        {
            foreach (var (level, value) in best)
            {
                progress.Best[int.Parse(level, Culture)] = ((int)value!["moves"]!, (int)value["stars"]!);
            }
        }

        if (root?["daily"] is JsonObject daily)
        {
            foreach (var (date, value) in daily)
            {
                progress.Daily[int.Parse(date, Culture)] = ((int)value!["seconds"]!, (int)value["moves"]!);
            }
        }

        return progress;
    }

    public string ToJson() => new JsonObject
    {
        ["version"] = 1,
        ["best"] = new JsonObject(Best.OrderBy(b => b.Key).Select(b => KeyValuePair.Create<string, JsonNode?>(
            b.Key.ToString(Culture), new JsonObject { ["moves"] = b.Value.Moves, ["stars"] = b.Value.Stars }))),
        ["daily"] = new JsonObject(Daily.OrderBy(d => d.Key).Select(d => KeyValuePair.Create<string, JsonNode?>(
            d.Key.ToString(Culture), new JsonObject { ["seconds"] = d.Value.Seconds, ["moves"] = d.Value.Moves }))),
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
