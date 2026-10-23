using Jentl.Core.Storage;

namespace Sokoban.Desktop;

/// <summary>
/// The adventure's single save slot. Like <see cref="SavedFile{T}"/>, storage problems are reported on
/// stderr and never crash the game; without a store, nothing is kept.
/// </summary>
internal sealed class AdventureSaves(ISaveStore? store)
{
    private const string Name = "adventure.json";

    /// <summary>The saved game, or null if there is none (or it cannot be read).</summary>
    public string? Read()
    {
        try
        {
            return store?.ReadText(Name) is { Length: > 0 } text ? text : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read {Name}: {e.Message}");
            return null;
        }
    }

    public bool Exists => Read() is not null;

    public void Write(string save)
    {
        try
        {
            store?.WriteText(Name, save);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not save {Name}: {e.Message}");
        }
    }

    public void Delete()
    {
        try
        {
            store?.Delete(Name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not delete {Name}: {e.Message}");
        }
    }

    /// <summary>Moves a save that no longer loads (say, after a content change) aside, keeping it for inspection.</summary>
    public void Discard(string reason)
    {
        try
        {
            var moved = store?.Quarantine(Name, DateTimeOffset.Now);
            Console.Error.WriteLine($"{Name} could not be loaded ({reason}); moved to {moved}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not move {Name} aside: {e.Message}");
        }
    }
}
