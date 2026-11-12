using Jentl.Core.Storage;

namespace Games;

/// <summary>A store whose files' names all start with <see cref="Prefix"/>: one game's corner of a suite's saves.</summary>
internal sealed class PrefixedStore(ISaveStore inner, string prefix) : ISaveStore
{
    public string Prefix { get; } = prefix;

    public string? ReadText(string name) => inner.ReadText(Prefix + name);

    public void WriteText(string name, string contents) => inner.WriteText(Prefix + name, contents);

    public void Delete(string name) => inner.Delete(Prefix + name);

    public string? Quarantine(string name, DateTimeOffset now) => inner.Quarantine(Prefix + name, now);
}
