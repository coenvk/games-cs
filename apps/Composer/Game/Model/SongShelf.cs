using System.Text.Json;
using System.Text.Json.Nodes;
using Jentl.Core.Audio;
using Jentl.Core.Storage;

namespace Composer;

/// <summary>
/// The songs the player has saved: each in a file of its own ("song-&lt;name&gt;.json"), with an index of their
/// titles (the store can't list its files). Without a store (a test, or saving switched off) it keeps them in memory.
/// </summary>
public sealed class SongShelf(ISaveStore? store)
{
    private const string IndexFile = "songs.json";

    private readonly Dictionary<string, string> _memory = new(StringComparer.Ordinal);

    /// <summary>The titles of the saved songs, most recently saved first.</summary>
    public IReadOnlyList<string> Titles => ReadIndex();

    public void Save(Composition song)
    {
        ArgumentNullException.ThrowIfNull(song);
        Write(FileOf(song.Title), song.ToJson());
        var titles = ReadIndex().Where(t => t != song.Title).Prepend(song.Title).ToList();
        Write(IndexFile, new JsonObject { ["songs"] = new JsonArray([.. titles.Select(t => (JsonNode?)t)]) }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The song saved under <paramref name="title"/>, or null when it is gone or can't be read.</summary>
    public Composition? Open(string title)
    {
        var text = Read(FileOf(title));
        try
        {
            return text is null ? null : Composition.FromJson(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public void Delete(string title)
    {
        if (store is null)
        {
            _memory.Remove(FileOf(title));
        }
        else
        {
            store.Delete(FileOf(title));
        }

        var titles = ReadIndex().Where(t => t != title).ToList();
        Write(IndexFile, new JsonObject { ["songs"] = new JsonArray([.. titles.Select(t => (JsonNode?)t)]) }.ToJsonString());
    }

    /// <summary>A file name for a title: letters, digits and dashes.</summary>
    public static string FileOf(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var name = string.Concat(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        return $"song-{(name.Length == 0 ? "untitled" : name)}.json";
    }

    private List<string> ReadIndex()
    {
        try
        {
            return Read(IndexFile) is { } text && JsonNode.Parse(text)?["songs"] is JsonArray songs
                ? [.. songs.Select(t => (string?)t).OfType<string>()]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string? Read(string name) => store is null ? _memory.GetValueOrDefault(name) : store.ReadText(name);

    private void Write(string name, string text)
    {
        if (store is null)
        {
            _memory[name] = text;
        }
        else
        {
            store.WriteText(name, text);
        }
    }
}
