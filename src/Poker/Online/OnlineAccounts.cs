using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poker.Online;

/// <summary>
/// What a computer keeps of its player's place in poker leagues: the owner token and each bot's token and bot file, per
/// server (its HTTP address). The workshop keeps it in <c>workshop/online.json</c> in the save folder, never in the
/// settings and never shown, since a token is all it takes to act as the owner.
/// </summary>
public sealed class OnlineAccounts
{
    public Dictionary<string, OnlineAccount> Servers { get; init; } = [];

    /// <summary>
    /// Where a file that could not be read was moved to when it was loaded (<c>online.json.broken-&lt;time&gt;</c>), so that
    /// saving does not overwrite the tokens in it; null when there was none.
    /// </summary>
    [JsonIgnore]
    public string? BrokenCopy { get; private set; }

    /// <summary>
    /// The accounts in <paramref name="path"/>; none when there is no file. A file that is not such JSON is moved aside
    /// (see <see cref="BrokenCopy"/>) and gives none.
    /// </summary>
    /// <exception cref="IOException">The file could not be read, or not moved aside.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be read or moved.</exception>
    public static OnlineAccounts Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return new OnlineAccounts();
        }

        var json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize(json, OnlineAccountsJson.Default.OnlineAccounts) ?? throw new JsonException("The file holds null.");
        }
        catch (JsonException)
        {
            var copy = $"{path}.broken-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
            File.Move(path, copy, overwrite: true);
            return new OnlineAccounts { BrokenCopy = copy };
        }
    }

    /// <summary>The account on the server at <paramref name="server"/>, an empty one until an owner is made there.</summary>
    public OnlineAccount For(string server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!Servers.TryGetValue(server, out var account))
        {
            account = new OnlineAccount();
            Servers[server] = account;
        }

        return account;
    }

    /// <summary>
    /// Writes the accounts to <paramref name="path"/>: to a file beside it first, readable by this user only where the
    /// system has such modes, which then takes the old one's place, so a crash leaves the old file whole.
    /// </summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be written to.</exception>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".new";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsBrowser())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(temporary, new UTF8Encoding(false), options))
        {
            writer.Write(JsonSerializer.Serialize(this, OnlineAccountsJson.Default.OnlineAccounts));
        }

        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>
/// The player's owner token on one server, and the tokens and bot files of the bots registered there, by the names the
/// server gives the bots.
/// </summary>
public sealed class OnlineAccount
{
    public string? OwnerToken { get; set; }

    public Dictionary<string, string> BotTokens { get; init; } = [];

    /// <summary>The file each bot is run from when it goes online (a <c>*.bot.json</c> or a DLL in the bots folder).</summary>
    public Dictionary<string, string> BotFiles { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(OnlineAccounts))]
internal sealed partial class OnlineAccountsJson : JsonSerializerContext;
