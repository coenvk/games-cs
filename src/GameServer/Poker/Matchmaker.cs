namespace GameServer.Poker;

/// <summary>
/// How bots are paired. A bot accepts an opponent whose rating is within <paramref name="Window"/> points, plus
/// <paramref name="WindowGrowthPerMinute"/> for every minute it has waited. Two bots do not meet again while either has
/// played the other in its last <paramref name="Cooldown"/> matches.
/// </summary>
public sealed record MatchmakerSettings(double Window = 200, double WindowGrowthPerMinute = 100, int Cooldown = 3);

/// <summary>
/// Chooses which waiting poker bots play each other. Never a bot against itself or against a bot of the same owner, always
/// close in rating (the window grows while a bot waits, so nobody waits for ever), and not the same two bots again within
/// the cooldown, unless one of them has waited ten minutes and nobody else fits. Safe to call from several threads: bots
/// connect and disconnect on their own threads while <see cref="Pair"/> runs on the league's tick.
/// </summary>
public sealed class Matchmaker(Func<string, (string Owner, double Rating)> lookup, TimeProvider clock, MatchmakerSettings settings)
{
    private static readonly TimeSpan _patience = TimeSpan.FromMinutes(10);

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _waiting = [];
    private readonly Dictionary<string, List<string>> _recent = [];

    /// <summary>Puts a bot in the queue. A bot that is already waiting keeps its place.</summary>
    public void Enqueue(string bot)
    {
        lock (_lock)
        {
            _waiting.TryAdd(bot, clock.GetUtcNow());
        }
    }

    /// <summary>Takes a bot out of the queue, because it disconnected or started a match.</summary>
    public void Remove(string bot)
    {
        lock (_lock)
        {
            _waiting.Remove(bot);
        }
    }

    /// <summary>Records that <paramref name="a"/> and <paramref name="b"/> played, so they are not paired again too soon.</summary>
    public void Played(string a, string b)
    {
        lock (_lock)
        {
            Remember(a, b);
            Remember(b, a);
        }
    }

    /// <summary>
    /// Pairs the waiting bots, longest waiting first, each with the closest rating that fits, and takes the paired bots
    /// out of the queue. Call it regularly: windows grow with time. A bot the lookup cannot describe (for instance one
    /// deleted while it waited) is dropped from the queue, so it cannot hold up the others.
    /// </summary>
    public IReadOnlyList<(string A, string B)> Pair()
    {
        List<string> queue;
        lock (_lock)
        {
            queue = _waiting.OrderBy(w => w.Value).Select(w => w.Key).ToList();
        }

        // The lookup takes other locks (registry, ratings), so it runs outside ours, once per bot.
        var info = new Dictionary<string, (string Owner, double Rating)>();
        var unknown = new List<string>();
        foreach (var bot in queue)
        {
            try
            {
                info[bot] = lookup(bot);
            }
            catch (Exception)
            {
                unknown.Add(bot);
            }
        }

        lock (_lock)
        {
            foreach (var bot in unknown)
            {
                _waiting.Remove(bot);
            }

            var now = clock.GetUtcNow();
            var pairs = new List<(string A, string B)>();

            // Bots removed or paired elsewhere while the lookups ran are no longer waiting.
            var present = queue.Where(b => info.ContainsKey(b) && _waiting.ContainsKey(b)).ToList();
            var taken = new HashSet<string>();

            foreach (var bot in present)
            {
                if (taken.Contains(bot))
                {
                    continue;
                }

                var (owner, rating) = info[bot];
                var candidates = present
                    .Where(other => other != bot && !taken.Contains(other) && info[other].Owner != owner
                        && Math.Abs(info[other].Rating - rating) <= Reach(bot, other, now))
                    .ToList();
                var fresh = candidates.Where(other => !PlayedRecently(bot, other)).ToList();
                var pool = fresh.Count > 0
                    ? fresh
                    : candidates.Where(other => now - _waiting[bot] >= _patience || now - _waiting[other] >= _patience).ToList();
                if (pool.Count == 0)
                {
                    continue;
                }

                var best = pool.MinBy(other => Math.Abs(info[other].Rating - rating))!;
                taken.Add(bot);
                taken.Add(best);
                pairs.Add((bot, best));
            }

            foreach (var (a, b) in pairs)
            {
                _waiting.Remove(a);
                _waiting.Remove(b);
            }

            return pairs;
        }
    }

    /// <summary>The rating difference two bots accept: the wider of their two windows.</summary>
    private double Reach(string a, string b, DateTimeOffset now)
    {
        var waited = Math.Max((now - _waiting[a]).TotalMinutes, (now - _waiting[b]).TotalMinutes);
        return settings.Window + settings.WindowGrowthPerMinute * waited;
    }

    private bool PlayedRecently(string a, string b) =>
        (_recent.TryGetValue(a, out var ofA) && ofA.Contains(b)) || (_recent.TryGetValue(b, out var ofB) && ofB.Contains(a));

    private void Remember(string bot, string opponent)
    {
        if (!_recent.TryGetValue(bot, out var list))
        {
            _recent[bot] = list = [];
        }

        list.Add(opponent);
        if (list.Count > settings.Cooldown)
        {
            list.RemoveRange(0, list.Count - settings.Cooldown);
        }
    }
}
