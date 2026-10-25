using System.Text.Json;
using Jentl.Net;

namespace Poker.Arena;

/// <summary>What kind of bot an entry is.</summary>
public enum BotKind
{
    /// <summary>A bot that comes with the app and runs in its process.</summary>
    House,

    /// <summary>A program described by a <c>*.bot.json</c> file, in any language.</summary>
    Program,

    /// <summary>A C# bot in a DLL, run by the bot host.</summary>
    CSharp,
}

/// <summary>A bot found in the bots folder.</summary>
/// <param name="Name">The name to list it under.</param>
/// <param name="Kind">What kind of bot it is.</param>
/// <param name="Command">How to run it, or null when its description could not be read.</param>
/// <param name="Source">The file it came from; for an unreadable description, that file and what is wrong with it.</param>
public sealed record BotEntry(string Name, BotKind Kind, AgentCommand? Command, string Source);

/// <summary>Finds the bots a player dropped in the bots folder.</summary>
public static class BotCatalog
{
    private const string Description = ".bot.json";

    /// <summary>
    /// Lists every <c>*.bot.json</c> (<c>{ "name", "command", "args": [], "workingDirectory" }</c>, the working directory
    /// relative to the file and by default its folder) and every <c>*.dll</c> except the SDK, <c>Poker.Bots.dll</c>,
    /// as a C# bot run by <paramref name="host"/> with the DLL path appended. A description that cannot be read becomes
    /// an entry without a command whose <see cref="BotEntry.Source"/> says what is wrong. Ordered by name; a folder
    /// that does not exist gives nothing.
    /// </summary>
    public static IReadOnlyList<BotEntry> Scan(string folder, AgentCommand host)
    {
        ArgumentNullException.ThrowIfNull(host);
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var entries = new List<BotEntry>();
        foreach (var file in Directory.EnumerateFiles(folder, "*" + Description))
        {
            entries.Add(ReadDescription(file));
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.dll"))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.Equals("Poker.Bots.dll", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new BotEntry(Path.GetFileNameWithoutExtension(file), BotKind.CSharp, host with { Arguments = [.. host.Arguments, file] }, file));
            }
        }

        return [.. entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Source, StringComparer.Ordinal)];
    }

    private static BotEntry ReadDescription(string file)
    {
        var stem = Path.GetFileName(file)[..^Description.Length];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var program = root.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(program))
            {
                return Unreadable(stem, file, "it has no \"command\"");
            }

            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString()! : stem;
            var arguments = root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : [];
            var directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
            if (root.TryGetProperty("workingDirectory", out var w) && w.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(w.GetString()))
            {
                directory = Path.GetFullPath(w.GetString()!, directory);
            }

            // A command with a path in it is relative to the working directory; a bare name is looked up on the PATH.
            if (program.Contains('/', StringComparison.Ordinal) || program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                program = Path.GetFullPath(program, directory);
            }

            return new BotEntry(name, BotKind.Program, new AgentCommand(program, arguments, directory), file);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Unreadable(stem, file, e.Message);
        }
    }

    private static BotEntry Unreadable(string name, string file, string problem) =>
        new(name, BotKind.Program, null, $"{file}: {problem}");
}
