using System.Globalization;
using PokerBots;

namespace Poker.Arena;

/// <summary>How the arena talks to one bot: a house bot in this process, or a bot in a process of its own.</summary>
public interface IArenaSeat : IDisposable
{
    /// <summary>The name shown at the table.</summary>
    string Name { get; }

    /// <summary>What is wrong with the bot ("could not start: …", "speaks protocol 2, the app speaks 1"), or null when all is well.</summary>
    string? Problem { get; }

    /// <summary>Whether the bot can no longer play: it could not start, said the wrong thing at hello, has exited, or was stopped.</summary>
    bool IsGone { get; }

    /// <summary>Whether its decisions have to be made within the match's time limit; a person at the table has no limit.</summary>
    bool HasTimeLimit => true;

    /// <summary>Gets the bot ready for the match, within <paramref name="limit"/>. Never throws for a bot that misbehaves: it is <see cref="IsGone"/> instead.</summary>
    Task StartAsync(MatchStart start, TimeSpan limit);

    /// <summary>Tells the bot something that needs no answer. Never blocks or throws.</summary>
    void Notify(Message message);

    /// <summary>
    /// Asks the bot for its move. Null when it did not answer with an action before <paramref name="cancel"/> was
    /// cancelled. Throws <see cref="IOException"/> when the bot is gone.
    /// </summary>
    Task<BotAction?> AskAsync(Decision decision, CancellationToken cancel);
}

/// <summary>Names for the seats of a match, so that each bot's results and statistics can be told apart.</summary>
public static class SeatNames
{
    /// <summary>
    /// <paramref name="names"/> with a number added to each repeat of a name, in order: "Tight", "Tight 2", "Tight 3".
    /// The first holder of a name keeps it, and a number that would give a name already in the list is skipped.
    /// </summary>
    public static IReadOnlyList<string> Distinct(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var given = new HashSet<string>(names, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            var unique = name;
            var number = 1;
            while (!used.Add(unique))
            {
                do
                {
                    number++;
                    unique = $"{name} {number}";
                }
                while (given.Contains(unique));
            }

            result.Add(unique);
        }

        return result;
    }

    /// <summary>
    /// The name in <paramref name="names"/> that <see cref="Distinct"/> gave the seat called <paramref name="seat"/>:
    /// the name itself when it is in the list, else the name without its number ("Tight 2" is "Tight") when that is;
    /// null when neither is. For seating the same bots again from a match's names.
    /// </summary>
    public static string? Original(string seat, IReadOnlyCollection<string> names)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(names);
        if (names.Contains(seat, StringComparer.Ordinal))
        {
            return seat;
        }

        var space = seat.LastIndexOf(' ');
        if (space > 0 && int.TryParse(seat.AsSpan(space + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 2)
        {
            var name = seat[..space];
            return names.Contains(name, StringComparer.Ordinal) ? name : null;
        }

        return null;
    }
}
