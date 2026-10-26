using System.Security.Cryptography;
using System.Text;

namespace GameServer.Poker;

/// <summary>
/// Someone who registers bots. An owner is known by a token alone (no password, no e-mail); only its hash is kept.
/// <paramref name="Address"/> is where the registration came from, for limits and for finding abuse.
/// </summary>
public sealed record Owner(string Id, string TokenHash, string Name, DateTimeOffset Created, string? Address, bool Banned);

/// <summary>A bot on the ladder: its unique name, its owner, and the hash of the token it connects with.</summary>
public sealed record BotAccount(string Name, string OwnerId, string TokenHash, DateTimeOffset Created, bool Banned);

/// <summary>
/// The owners and bots there are, kept in <c>owners.json</c> and <c>bots.json</c> so they outlive a restart. Tokens are
/// looked up by their SHA-256 hash and never stored. A banned bot, or a bot of a banned owner, is not found by its
/// token. Thread-safe: WebSocket and HTTP handlers read it at the same time.
/// </summary>
public sealed class Registry
{
    private const string OwnersFile = "owners.json";
    private const string BotsFile = "bots.json";

    private readonly object _lock = new();
    private readonly PokerData _data;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Owner> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BotAccount> _bots = new(StringComparer.OrdinalIgnoreCase);

    public Registry(PokerData data, TimeProvider clock)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        foreach (var owner in _data.Read(OwnersFile, RegistryJson.Default.ListOwner) ?? [])
        {
            _owners[owner.Id] = owner;
        }

        foreach (var bot in _data.Read(BotsFile, RegistryJson.Default.ListBotAccount) ?? [])
        {
            _bots[bot.Name] = bot;
        }
    }

    /// <summary>
    /// Raised after a ban with the names of the bots it banned (the bot, or all of the owner's), so their runners can be
    /// cut off at once: a bot's token is checked only when its runner connects.
    /// </summary>
    public event Action<IReadOnlyList<string>>? BotsBanned;

    /// <summary>The time, for when something was registered.</summary>
    internal DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>The hash a token is kept and looked up by.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>The owner a token belongs to, or null when it is unknown or the owner is banned.</summary>
    public Owner? OwnerOfToken(string token)
    {
        var hash = Hash(token);
        lock (_lock)
        {
            return _owners.Values.FirstOrDefault(o => !o.Banned && Same(o.TokenHash, hash));
        }
    }

    /// <summary>The owner a token belongs to, banned or not, so a refusal can say "banned" rather than "unknown".</summary>
    internal Owner? AnyOwnerOfToken(string token)
    {
        var hash = Hash(token);
        lock (_lock)
        {
            return _owners.Values.FirstOrDefault(o => Same(o.TokenHash, hash));
        }
    }

    /// <summary>The bot a token belongs to, or null when it is unknown or the bot or its owner is banned.</summary>
    public BotAccount? BotOfToken(string token)
    {
        var hash = Hash(token);
        lock (_lock)
        {
            return _bots.Values.FirstOrDefault(b => !b.Banned && Same(b.TokenHash, hash) && !IsOwnerBanned(b));
        }
    }

    /// <summary>The bot with this name (case does not matter), banned or not, or null.</summary>
    public BotAccount? Bot(string name)
    {
        lock (_lock)
        {
            return _bots.GetValueOrDefault(name);
        }
    }

    /// <summary>The owner with this id, banned or not, or null.</summary>
    internal Owner? OwnerById(string id)
    {
        lock (_lock)
        {
            return _owners.GetValueOrDefault(id);
        }
    }

    /// <summary>The bots of an owner, banned ones included.</summary>
    public IReadOnlyList<BotAccount> BotsOf(string ownerId)
    {
        lock (_lock)
        {
            return [.. _bots.Values.Where(b => b.OwnerId == ownerId).OrderBy(b => b.Created).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>
    /// Bans a bot by name, or an owner by id. An owner's ban marks every bot of theirs banned too, so
    /// <see cref="Bot"/> and <see cref="BotsOf"/> tell the whole truth: <c>Banned</c> on a bot is all that later code needs to read.
    /// </summary>
    public void Ban(string botOrOwner)
    {
        List<string> banned = [];
        lock (_lock)
        {
            if (_bots.TryGetValue(botOrOwner, out var bot))
            {
                _bots[bot.Name] = bot with { Banned = true };
                banned.Add(bot.Name);
                SaveBots();
            }
            else if (_owners.TryGetValue(botOrOwner, out var owner))
            {
                _owners[owner.Id] = owner with { Banned = true };
                foreach (var theirs in _bots.Values.Where(b => b.OwnerId == owner.Id).ToList())
                {
                    _bots[theirs.Name] = theirs with { Banned = true };
                    banned.Add(theirs.Name);
                }

                SaveOwners();
                SaveBots();
            }
        }

        if (banned.Count > 0)
        {
            BotsBanned?.Invoke(banned);
        }
    }

    /// <summary>Adds an owner and saves.</summary>
    internal void Add(Owner owner)
    {
        lock (_lock)
        {
            _owners[owner.Id] = owner;
            SaveOwners();
        }
    }

    /// <summary>Adds a bot and saves; false, and nothing changed, when the name is taken (case does not matter).</summary>
    internal bool TryAdd(BotAccount bot)
    {
        lock (_lock)
        {
            if (!_bots.TryAdd(bot.Name, bot))
            {
                return false;
            }

            SaveBots();
            return true;
        }
    }

    /// <summary>
    /// Swaps the token hash on the bot's current record, under the registry lock, so a ban that landed since the caller
    /// looked is kept. Null, and nothing changed, when the bot is unknown or banned.
    /// </summary>
    internal BotAccount? ReplaceToken(string name, string tokenHash)
    {
        lock (_lock)
        {
            if (!_bots.TryGetValue(name, out var bot) || bot.Banned || IsOwnerBanned(bot))
            {
                return null;
            }

            var renewed = bot with { TokenHash = tokenHash };
            _bots[renewed.Name] = renewed;
            SaveBots();
            return renewed;
        }
    }

    private bool IsOwnerBanned(BotAccount bot) => !_owners.TryGetValue(bot.OwnerId, out var owner) || owner.Banned;

    private static bool Same(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    private void SaveOwners() => _data.Write(OwnersFile, [.. _owners.Values.OrderBy(o => o.Created)], RegistryJson.Default.ListOwner);

    private void SaveBots() => _data.Write(BotsFile, [.. _bots.Values.OrderBy(b => b.Created)], RegistryJson.Default.ListBotAccount);
}
