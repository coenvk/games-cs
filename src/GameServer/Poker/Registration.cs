using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Poker;

namespace GameServer.Poker;

/// <summary>What registering may take: how many bots an owner has, and how many registrations one address makes a day.</summary>
public sealed record RegistrationLimits(int BotsPerOwner = 5, int PerAddressPerDay = 10);

/// <summary>A new account and the token that opens it. The token is shown once; only its hash is kept.</summary>
public sealed record Issued<T>(T Account, string Token);

/// <summary>Either a new account with its token, or why not.</summary>
public sealed record RegisterResult<T>
{
    private readonly Issued<T>? _issued;

    private RegisterResult(Issued<T>? issued, string? refusal)
    {
        _issued = issued;
        Refusal = refusal;
    }

    /// <summary>Whether the account was made.</summary>
    public bool Ok => _issued is not null;

    /// <summary>Why not, in words for the person registering; null when it worked.</summary>
    public string? Refusal { get; }

    /// <summary>The account and its token; only when <see cref="Ok"/>.</summary>
    public Issued<T> Value => _issued ?? throw new InvalidOperationException($"Registration was refused: {Refusal}");

    internal static RegisterResult<T> Success(T account, string token) => new(new Issued<T>(account, token), null);

    internal static RegisterResult<T> Refused(string reason) => new(null, reason);
}

/// <summary>
/// How owners and bots come to be. Registration is free for now; it is one service so that invite codes or operator
/// approval can be added here later without touching the endpoints. Tokens are 32 random bytes, base64url, handed
/// out once. Thread-safe.
/// </summary>
public sealed partial class Registration(Registry registry, RegistrationLimits limits, TimeProvider clock)
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private readonly object _lock = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _byAddress = new(StringComparer.Ordinal);

    /// <summary>Makes an owner. A null address (not known) is not limited.</summary>
    public RegisterResult<Owner> NewOwner(string name, string? address)
    {
        if (!IsName(name))
        {
            return RegisterResult<Owner>.Refused("not a name");
        }

        lock (_lock)
        {
            if (TooMany(address))
            {
                return RegisterResult<Owner>.Refused("too many registrations today");
            }

            var token = NewToken();
            var owner = new Owner(RandomId(), Registry.Hash(token), name, clock.GetUtcNow(), address, Banned: false);
            registry.Add(owner);
            Count(address);
            return RegisterResult<Owner>.Success(owner, token);
        }
    }

    /// <summary>Makes a bot for the owner that holds <paramref name="ownerToken"/>.</summary>
    public RegisterResult<BotAccount> NewBot(string ownerToken, string botName, string? address)
    {
        if (!IsName(botName))
        {
            return RegisterResult<BotAccount>.Refused("not a name");
        }

        lock (_lock)
        {
            var owner = registry.AnyOwnerOfToken(ownerToken);
            if (owner is null)
            {
                return RegisterResult<BotAccount>.Refused("unknown owner");
            }

            if (owner.Banned)
            {
                return RegisterResult<BotAccount>.Refused("banned");
            }

            if (registry.BotsOf(owner.Id).Count >= limits.BotsPerOwner)
            {
                return RegisterResult<BotAccount>.Refused($"{limits.BotsPerOwner} bots already");
            }

            // A house bot's name is taken too: a practice match against it would seat two bots of one name.
            if (registry.Bot(botName) is not null || HouseBots.All.Any(h => string.Equals(h.Name, botName, StringComparison.OrdinalIgnoreCase)))
            {
                return RegisterResult<BotAccount>.Refused("name taken");
            }

            if (TooMany(address))
            {
                return RegisterResult<BotAccount>.Refused("too many registrations today");
            }

            var token = NewToken();
            var bot = new BotAccount(botName, owner.Id, Registry.Hash(token), clock.GetUtcNow(), Banned: false);
            if (!registry.TryAdd(bot))
            {
                return RegisterResult<BotAccount>.Refused("name taken");
            }

            Count(address);
            return RegisterResult<BotAccount>.Success(bot, token);
        }
    }

    /// <summary>
    /// Gives a bot a new token; the old one stops working. Refused as "unknown owner", "unknown bot" (not this owner's)
    /// or "banned" (the owner or the bot is banned).
    /// </summary>
    public RegisterResult<BotAccount> RenewBotToken(string ownerToken, string botName)
    {
        lock (_lock)
        {
            var owner = registry.AnyOwnerOfToken(ownerToken);
            if (owner is null)
            {
                return RegisterResult<BotAccount>.Refused("unknown owner");
            }

            var bot = registry.Bot(botName);
            if (bot is null || bot.OwnerId != owner.Id)
            {
                return RegisterResult<BotAccount>.Refused("unknown bot");
            }

            var token = NewToken();

            // The registry swaps the token on its current record, so a ban that lands meanwhile is never undone.
            var renewed = owner.Banned ? null : registry.ReplaceToken(bot.Name, Registry.Hash(token));
            return renewed is null ? RegisterResult<BotAccount>.Refused("banned") : RegisterResult<BotAccount>.Success(renewed, token);
        }
    }

    /// <summary>The new token of <see cref="RenewBotToken"/>, or null when it was refused.</summary>
    public string? NewBotToken(string ownerToken, string botName)
    {
        var result = RenewBotToken(ownerToken, botName);
        return result.Ok ? result.Value.Token : null;
    }

    /// <summary>Whether this is an acceptable name: 3 to 24 letters, digits, spaces, dashes, underscores or dots, not starting or ending in a space.</summary>
    public static bool IsName(string? name) => name is not null && NamePattern().IsMatch(name);

    [GeneratedRegex(@"^(?! )[A-Za-z0-9 _.\-]{3,24}(?<! )\z")]
    private static partial Regex NamePattern();

    private static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string RandomId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Registrations of an address in the last day (a rolling day, so a burst at midnight is no loophole).
    private bool TooMany(string? address)
    {
        if (address is null || !_byAddress.TryGetValue(address, out var times))
        {
            return false;
        }

        var since = clock.GetUtcNow() - Day;
        times.RemoveAll(t => t <= since);
        return times.Count >= limits.PerAddressPerDay;
    }

    private void Count(string? address)
    {
        if (address is null)
        {
            return;
        }

        if (!_byAddress.TryGetValue(address, out var times))
        {
            _byAddress[address] = times = [];
        }

        times.Add(clock.GetUtcNow());
    }
}
