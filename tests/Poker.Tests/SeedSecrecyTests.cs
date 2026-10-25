using Jentl.Cards;
using Poker.Arena;
using PokerBots;

namespace Poker.Tests;

/// <summary>
/// A bot knows its own seed, the settings and its own cards. These tests play the attacks that worked on the old deal
/// (a seat seed that was the match seed mixed by SplitMix64, a match seed from 1 to 999,999, and hands shuffled by
/// <c>new Random(seed + n * 7919)</c>) and check that they no longer tell the bot anything about the other hands.
/// </summary>
public class SeedSecrecyTests
{
    private const int Seats = 2;

    [Fact]
    public async Task ABot_CannotWorkOutItsOpponentsCards_FromItsSeedAndItsOwnCards()
    {
        var seen = new List<Decision>();
        var bot = new FakeSeat(d =>
        {
            seen.Add(d);
            return d.Legal.Check ? BotAction.Check : BotAction.Call;
        });
        var opponent = new FakeSeat(d => d.Legal.Check ? BotAction.Check : BotAction.Call);
        using var table = new ArenaTable([bot, opponent], new ArenaSettings(TableFormat.FixedStacks, 3, DealSeed.Parse("0f1e2d3c4b5a69788796a5b4c3d2e1f0"), TimeSpan.FromSeconds(5)));
        await table.RunAsync(CancellationToken.None);

        // What the bot saw: its seed and its own hole cards of hands 1 and 2.
        var start = bot.Messages.OfType<MatchStart>().Single();
        var mine = seen.GroupBy(d => d.Hand).ToDictionary(g => g.Key, g => g.First().Hole);
        var truth = table.Hands[2].Holes[1].Select(BotView.Code).ToList();

        // Attack 1: undo SplitMix64 on the seat seed; its top half was the match seed.
        var unmixed = Unmix(start.Seed);
        ((uint)unmixed).Should().NotBe((uint)start.Seat, "an undone seat seed must not end in the seat number");
        (Predict((int)(unmixed >> 32), mine) ?? []).Should().NotEqual(truth);

        // Attack 2: try every seed the setup used to pick, keeping those that deal the bot its own cards.
        var predictions = Enumerable.Range(1, 999_999).Select(seed => Predict(seed, mine)).OfType<List<string>>().ToList();
        predictions.Should().NotContain(p => p.SequenceEqual(truth));
    }

    /// <summary>The opponent's cards of hand 3 under the old deal from <paramref name="seed"/>, or null when that seed does not deal seat 0 the cards it saw.</summary>
    private static List<string>? Predict(int seed, Dictionary<int, IReadOnlyList<string>> mine)
    {
        for (var index = 0; index < 2; index++)
        {
            if (!Hole(seed, index, 0).SequenceEqual(mine[index + 1]))
            {
                return null;
            }
        }

        return Hole(seed, 2, 1);
    }

    private static List<string> Hole(int seed, int index, int seat)
    {
        var deck = Deck.Shuffled(Deck.Standard52(), new Random(unchecked(seed + index * 7919))).Reverse().ToArray();
        return [.. Positions[index % Seats][seat].Select(p => BotView.Code(deck[p]))];
    }

    // Where in the deck each seat's hole cards are, by button: dealt from a deck in order, the cards give their positions.
    private static readonly int[][][] Positions = [.. Enumerable.Range(0, Seats).Select(button =>
    {
        var ordered = Deck.Standard52().ToList();
        var hand = new HoldemHand([2000, 2000], button, 10, 20, ordered);
        return Enumerable.Range(0, Seats).Select(seat => hand.Hole(seat).Select(c => ordered.IndexOf(c)).ToArray()).ToArray();
    })];

    // SplitMix64's finalizer undone step by step, then its added constant taken off.
    private static ulong Unmix(long seed)
    {
        var z = (ulong)seed;
        z ^= (z >> 31) ^ (z >> 62);
        z *= Inverse(0x94D049BB133111EB);
        z ^= (z >> 27) ^ (z >> 54);
        z *= Inverse(0xBF58476D1CE4E5B9);
        z ^= (z >> 30) ^ (z >> 60);
        return z - 0x9E3779B97F4A7C15;
    }

    // The odd number's inverse modulo 2^64, by Newton's method.
    private static ulong Inverse(ulong a)
    {
        var x = a;
        for (var i = 0; i < 5; i++)
        {
            x *= 2 - a * x;
        }

        return x;
    }
}
