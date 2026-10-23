using Jentl.Core.Storage;

namespace Sokoban.Desktop;

/// <summary>
/// A value kept in a save file. Loading recovers from missing or corrupt files, saving reports
/// I/O problems on stderr; neither ever crashes the game. Without a store the value lives in memory only.
/// </summary>
internal sealed class SavedFile<T>
    where T : class
{
    private readonly SaveStore? _store;
    private readonly string _name;
    private readonly Func<T, string> _serialize;

    private SavedFile(SaveStore? store, string name, T value, Func<T, string> serialize)
    {
        _store = store;
        _name = name;
        Value = value;
        _serialize = serialize;
    }

    public T Value { get; }

    /// <param name="parse">Parses the file; throws <see cref="InvalidDataException"/> for bad contents.</param>
    public static SavedFile<T> Load(SaveStore? store, string name, Func<string, T> parse, Func<T, string> serialize, Func<T> create)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(create);
        if (store is null)
        {
            return new SavedFile<T>(null, name, create(), serialize);
        }

        string? text;
        try
        {
            text = store.ReadText(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Without a readable file, keep the value in memory rather than overwrite what is there.
            Console.Error.WriteLine($"Could not read {name}, changes will not be saved this session: {e.Message}");
            return new SavedFile<T>(null, name, create(), serialize);
        }

        if (text is null)
        {
            return new SavedFile<T>(store, name, create(), serialize);
        }

        try
        {
            return new SavedFile<T>(store, name, parse(text), serialize);
        }
        catch (InvalidDataException e)
        {
            var moved = store.Quarantine(name, DateTimeOffset.Now);
            Console.Error.WriteLine($"{name} was unreadable ({e.Message}); moved to {moved} and starting fresh.");
            return new SavedFile<T>(store, name, create(), serialize);
        }
    }

    public void Save()
    {
        try
        {
            _store?.WriteText(_name, _serialize(Value));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not save {_name}: {e.Message}");
        }
    }
}
