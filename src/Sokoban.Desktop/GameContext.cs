using Jentl.Core.Audio;
using Jentl.Core.Cards;
using Jentl.Core.Input;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;
using Sokoban.Desktop.UI;
using Sokoban.Model;

namespace Sokoban.Desktop;

internal static class Sound
{
    public const string Navigate = "navigate";
    public const string Completed = "completed";
}

/// <param name="Name">Shown on the level select tab.</param>
/// <param name="Prefix">Used for untitled levels, e.g. "Ice" gives "Ice 3".</param>
internal sealed record LevelPack(string Name, string Prefix, IReadOnlyList<Level> Levels);

/// <summary>What the platform supports; a browser tab cannot quit or switch to fullscreen by itself.</summary>
/// <param name="HintTime">
/// Search budget for a hint. WebAssembly has a single thread, so there the search holds up the frame
/// and must stay short.
/// </param>
internal sealed record PlatformFeatures(bool CanQuit, bool CanFullscreen, TimeSpan HintTime)
{
    public static PlatformFeatures Desktop { get; } = new(CanQuit: true, CanFullscreen: true, TimeSpan.FromSeconds(3));

    public static PlatformFeatures Browser { get; } = new(CanQuit: false, CanFullscreen: false, TimeSpan.FromSeconds(1));
}

/// <summary>A level by its place in the packs.</summary>
internal readonly record struct LevelRef(int Pack, int Index);

/// <summary>Shared game services handed to every scene.</summary>
internal sealed record GameContext(
    IReadOnlyList<LevelPack> Packs,
    Tileset Tileset,
    Fonts Fonts,
    ActionMap<GameAction> Actions,
    ProgressStore Progress,
    SoundPlayer Sounds,
    SavedFile<Settings> Settings,
    Action<bool> SetFullscreen,
    PlatformFeatures Platform,
    CardRenderer Cards,
    AdventureSaves Adventures,
    CodeLockStore Locks,
    int? Deal = null)
{
    public IEnumerable<Level> AllLevels => Packs.SelectMany(p => p.Levels);

    /// <summary>Where "Continue" picks up: the first unsolved level, pack by pack; the very first level once all are solved.</summary>
    public LevelRef Continue
    {
        get
        {
            for (var pack = 0; pack < Packs.Count; pack++)
            {
                if (Progress.Progress.FirstUnsolved(Packs[pack].Levels) is { } index)
                {
                    return new LevelRef(pack, index);
                }
            }

            return new LevelRef(0, 0);
        }
    }

    /// <summary>Picks a level's colours; the classic pack keeps the seeds it always had.</summary>
    public static int ThemeSeed(LevelRef where) => where.Pack * 1000 + where.Index + 1;

    public Level LevelAt(LevelRef where) => Packs[where.Pack].Levels[where.Index];

    public string TitleOf(LevelRef where) =>
        LevelAt(where).Title ?? $"{Packs[where.Pack].Prefix} {where.Index + 1}";
}
