using Jentl.Core.Storage;
using Sokoban.Model;

namespace Sokoban.Desktop;

/// <summary>
/// Owns the player's <see cref="Model.Progress"/> and saves it after every improvement.
/// I/O problems are reported on stderr and never crash the game.
/// </summary>
internal sealed class ProgressStore
{
    private const string FileName = "progress.json";

    private readonly SaveStore? _store;

    private ProgressStore(SaveStore? store, Progress progress)
    {
        _store = store;
        Progress = progress;
    }

    public Progress Progress { get; }

    /// <summary>Progress that lives only as long as the process, for scripted runs.</summary>
    public static ProgressStore InMemory() => new(null, new Progress());

    public static ProgressStore Load(SaveStore store)
    {
        string? json;
        try
        {
            json = store.ReadText(FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Without a readable file, keep progress in memory rather than overwrite what is there.
            Console.Error.WriteLine($"Could not read progress, it will not be saved this session: {e.Message}");
            return InMemory();
        }

        if (json is null)
        {
            return new ProgressStore(store, new Progress());
        }

        try
        {
            return new ProgressStore(store, Progress.FromJson(json));
        }
        catch (InvalidDataException e)
        {
            var moved = store.Quarantine(FileName, DateTimeOffset.Now);
            Console.Error.WriteLine($"Progress file was unreadable ({e.Message}); moved to {moved} and starting fresh.");
            return new ProgressStore(store, new Progress());
        }
    }

    public Improvement Record(LevelSession session, TimeSpan time)
    {
        var improvement = Progress.Record(session, time, DateTimeOffset.Now);
        if (improvement != Improvement.None)
        {
            Save();
        }

        return improvement;
    }

    private void Save()
    {
        try
        {
            _store?.WriteText(FileName, Progress.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not save progress: {e.Message}");
        }
    }
}
