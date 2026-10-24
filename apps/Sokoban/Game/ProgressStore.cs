using Jentl.Core.Storage;
using Sokoban.Model;

namespace Games;

/// <summary>Owns the player's <see cref="Sokoban.Model.Progress"/> and saves it after every improvement.</summary>
internal sealed class ProgressStore
{
    private readonly SavedFile<Progress> _file;

    /// <param name="store">Where to save, or null to keep progress in memory only.</param>
    public ProgressStore(ISaveStore? store) =>
        _file = SavedFile<Progress>.Load(store, "progress.json", Progress.FromJson, p => p.ToJson(), () => new Progress());

    public Progress Progress => _file.Value;

    public Improvement Record(LevelSession session, TimeSpan time)
    {
        var improvement = Progress.Record(session, time, DateTimeOffset.Now);
        if (improvement != Improvement.None)
        {
            _file.Save();
        }

        return improvement;
    }
}
