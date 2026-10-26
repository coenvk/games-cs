using Jentl.Multiplayer;
using Poker.Arena;
using static Poker.Tests.TestHands;

namespace Poker.Tests;

/// <summary>What an owner may see of a match its bot played: its own cards, and an opponent's only when shown.</summary>
public class OwnerViewTests
{
    private static readonly string[] Names = ["Mine", "Theirs"];

    // Hand 1: Theirs folds to a raise, its cards unseen. Hand 2: both check it down and show.
    private static MatchResult Match() => new(
        new ArenaSettings(TableFormat.FixedStacks, 2, DealSeed.New(), TimeSpan.FromSeconds(1), SmallBlind: 1),
        null,
        new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
        Names,
        [new BotResult("Mine", 75, 0, 2, 0, 0, 0, []), new BotResult("Theirs", -75, 0, 2, 0, 0, 0, [])],
        [
            Hand(
                1,
                button: 0,
                [200, 200],
                ["As Ks", "7c 2d"],
                [Act(Street.Preflop, 0, new RaiseTo(6), 5, 6), Act(Street.Preflop, 1, Fold.Instance, 0, 2)],
                "",
                [],
                new Dictionary<int, int> { [0] = 8 },
                smallBlind: 1,
                bigBlind: 2,
                names: Names,
                seed: DealSeed.New()),
            Hand(
                2,
                button: 1,
                [200, 200],
                ["Qh Qd", "Jc Tc"],
                [Act(Street.Preflop, 1, CallBet.Instance, 1, 2), Act(Street.Preflop, 0, Check.Instance, 0, 2)],
                "2h 3h 4s 9d Kc",
                [0, 1],
                new Dictionary<int, int> { [0] = 4 },
                smallBlind: 1,
                bigBlind: 2,
                names: Names,
                seed: DealSeed.New()),
        ]);

    [Fact]
    public void TheOwnersView_HidesFoldedOpponentCards_AndKeepsShownOnes()
    {
        var match = Match();

        var view = OwnerView.For(match, "Mine");

        view.Hands[0].Holes[0].Should().Equal(Cards("As Ks"), "the owner's own cards are always kept");
        view.Hands[0].Holes[1].Should().BeEmpty("the opponent folded: its cards never leave the server");
        view.Hands[1].Holes[0].Should().Equal(Cards("Qh Qd"));
        view.Hands[1].Holes[1].Should().Equal(Cards("Jc Tc"), "cards shown at showdown are seen by everyone");
    }

    [Fact]
    public void TheOtherOwnersView_HidesTheFirstBotsFoldedCards_FromItsOwnSide()
    {
        var view = OwnerView.For(Match(), "Theirs");

        view.Hands[0].Holes[0].Should().BeEmpty();
        view.Hands[0].Holes[1].Should().Equal(Cards("7c 2d"));
        view.Hands[1].Holes[0].Should().Equal(Cards("Qh Qd"));
    }

    [Fact]
    public void TheOwnersView_GivesAwayNoSeed_FromWhichTheHiddenCardsCouldBeDealtAgain()
    {
        var match = Match();

        var view = OwnerView.For(match, "Mine");

        view.Settings.Seed.Should().Be(default(DealSeed));
        view.Hands.Should().OnlyContain(h => h.Seed == default(DealSeed));
    }

    [Fact]
    public void TheOwnersView_KeepsEverythingElse_AndIsAMatchFile()
    {
        var match = Match();

        var view = MatchFile.Read(MatchFile.Write(OwnerView.For(match, "Mine")));

        view.Results.Should().BeEquivalentTo(match.Results);
        view.Bots.Should().Equal(match.Bots);
        view.At.Should().Be(match.At);
        view.Hands.Select(h => h.Board).Should().BeEquivalentTo(match.Hands.Select(h => h.Board));
        view.Hands.Select(h => h.Actions.Count).Should().Equal(match.Hands.Select(h => h.Actions.Count));
        view.Hands.Select(h => h.Shown).Should().BeEquivalentTo(match.Hands.Select(h => h.Shown));
    }
}
