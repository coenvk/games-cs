using System.Reflection;
using Jentl.Multiplayer;

namespace Uno.Tests;

public class UnoGameTests
{
    [Fact]
    public void TheDeck_Has108Cards_InTheRightMix()
    {
        var deck = UnoCard.Deck();

        deck.Should().HaveCount(108).And.OnlyHaveUniqueItems();
        deck.Count(c => c.Value == UnoValue.Zero).Should().Be(4);
        deck.Count(c => c.Value == UnoValue.Seven).Should().Be(8);
        deck.Count(c => c.Value == UnoValue.DrawTwo).Should().Be(8);
        deck.Count(c => c.IsWild).Should().Be(8);
        deck.Sum(c => c.Points).Should().Be(4 * 90 + 24 * 20 + 8 * 50, "two of each 1-9 per colour, 24 action cards, 8 wilds");
    }

    [Fact]
    public void ANewRound_DealsSevenEach_AndNeverStartsOnAWild()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var game = new UnoGame(4, seed);

            Enumerable.Range(0, 4).Select(s => game.Hand(s).Count).Should().OnlyContain(n => n >= 7);
            game.Top.IsWild.Should().BeFalse();
            AllCards(game).Should().HaveCount(108).And.OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void CardsMatchByColourOrValue_AndWildsAlwaysPlay()
    {
        var game = Rigged(top: Card(UnoValue.Five, UnoColor.Red), hands: [[Card(UnoValue.Five, UnoColor.Blue), Card(UnoValue.Two, UnoColor.Red), Card(UnoValue.Two, UnoColor.Green), Wild()], []]);

        game.CanPlay(0, Card(UnoValue.Five, UnoColor.Blue)).Should().BeTrue("same number");
        game.CanPlay(0, Card(UnoValue.Two, UnoColor.Red)).Should().BeTrue("same colour");
        game.CanPlay(0, Card(UnoValue.Two, UnoColor.Green)).Should().BeFalse();
        game.CanPlay(0, Wild()).Should().BeTrue();
    }

    [Fact]
    public void AWildDrawFour_OnlyPlaysWithoutTheCurrentColour()
    {
        var withRed = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.Two, UnoColor.Red), WildFour()], []]);
        var withoutRed = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.Two, UnoColor.Blue), WildFour()], []]);

        withRed.CanPlay(0, WildFour()).Should().BeFalse();
        withoutRed.CanPlay(0, WildFour()).Should().BeTrue();
    }

    [Fact]
    public void Wilds_SetTheColour()
    {
        var game = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Wild(), Card(UnoValue.One, UnoColor.Red), Card(UnoValue.Two, UnoColor.Red)], [Card(UnoValue.Nine, UnoColor.Green)], [Card(UnoValue.Nine, UnoColor.Blue)]]);

        game.Apply(0, new PlayCard(Wild(), UnoColor.Green));

        game.Color.Should().Be(UnoColor.Green);
        game.CurrentSeat.Should().Be(1);
        var noColour = () => game.Apply(1, new PlayCard(Card(UnoValue.Nine, UnoColor.Green)));
        noColour.Should().NotThrow();
    }

    [Fact]
    public void Skip_Reverse_AndDrawTwo_DoWhatTheySay()
    {
        var skip = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.Skip, UnoColor.Red), Filler()], [Filler()], [Filler()]]);
        skip.Apply(0, new PlayCard(Card(UnoValue.Skip, UnoColor.Red)));
        skip.CurrentSeat.Should().Be(2);

        var reverse = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.Reverse, UnoColor.Red), Filler()], [Filler()], [Filler()]]);
        reverse.Apply(0, new PlayCard(Card(UnoValue.Reverse, UnoColor.Red)));
        reverse.CurrentSeat.Should().Be(2, "play turns back the other way");
        reverse.Direction.Should().Be(-1);

        var drawTwo = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.DrawTwo, UnoColor.Red), Filler()], [Filler()], [Filler()]]);
        drawTwo.Apply(0, new PlayCard(Card(UnoValue.DrawTwo, UnoColor.Red)));
        drawTwo.Hand(1).Should().HaveCount(3);
        drawTwo.CurrentSeat.Should().Be(2);
    }

    [Fact]
    public void WithTwoPlayers_AReverseActsAsASkip()
    {
        var game = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.Reverse, UnoColor.Red), Filler()], [Filler()]]);

        game.Apply(0, new PlayCard(Card(UnoValue.Reverse, UnoColor.Red)));

        game.CurrentSeat.Should().Be(0);
    }

    [Fact]
    public void ADrawnCardThatFits_MayBePlayedAtOnce_OrKept()
    {
        var game = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.Two, UnoColor.Green)], [Filler()]], drawPile: [Card(UnoValue.Eight, UnoColor.Red)]);

        game.Apply(0, DrawCard.Instance);

        game.Drawn.Should().Be(Card(UnoValue.Eight, UnoColor.Red));
        game.CurrentSeat.Should().Be(0);
        game.LegalMoves(0).Should().Equal(new PlayCard(Card(UnoValue.Eight, UnoColor.Red), CallUno: true), Pass.Instance);
        game.Apply(0, Pass.Instance);
        game.CurrentSeat.Should().Be(1);
    }

    [Fact]
    public void ADrawnCardThatDoesNotFit_EndsTheTurn()
    {
        var game = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.Two, UnoColor.Green)], [Filler()]], drawPile: [Card(UnoValue.Eight, UnoColor.Blue)]);

        game.Apply(0, DrawCard.Instance);

        game.Drawn.Should().BeNull();
        game.CurrentSeat.Should().Be(1);
    }

    [Fact]
    public void ForgettingToCallUno_CostsTwoCards()
    {
        var called = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.One, UnoColor.Red), Filler()], [Filler()]], drawPile: Fillers(5));
        called.Apply(0, new PlayCard(Card(UnoValue.One, UnoColor.Red), CallUno: true));
        called.Hand(0).Should().HaveCount(1);
        called.TakeEvents().Should().Contain(new UnoCalled(0));

        var forgot = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.One, UnoColor.Red), Filler()], [Filler()]], drawPile: Fillers(5));
        forgot.Apply(0, new PlayCard(Card(UnoValue.One, UnoColor.Red)));
        forgot.Hand(0).Should().HaveCount(3);
        forgot.TakeEvents().Should().Contain(new UnoForgotten(0));
    }

    [Fact]
    public void EmptyingYourHand_WinsTheRound_AndScoresTheOtherHands()
    {
        var game = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.DrawTwo, UnoColor.Red)], [Card(UnoValue.Nine, UnoColor.Blue), Wild()], [Card(UnoValue.Skip, UnoColor.Green)]], drawPile: [Card(UnoValue.Three, UnoColor.Blue), Card(UnoValue.Four, UnoColor.Blue)]);

        game.Apply(0, new PlayCard(Card(UnoValue.DrawTwo, UnoColor.Red)));

        game.Winner.Should().Be(0);
        game.Hand(1).Should().HaveCount(4, "a last Draw Two still makes the next player draw");
        game.TakeEvents().OfType<RoundWon>().Single().Points.Should().Be(9 + 50 + 3 + 4 + 20);
        game.LegalMoves(1).Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyDrawPile_IsRefilledFromTheDiscards()
    {
        var game = Rigged(Card(UnoValue.Five, UnoColor.Red), [[Card(UnoValue.One, UnoColor.Green)], [Filler()]], drawPile: [], discards: [Card(UnoValue.Two, UnoColor.Blue), Card(UnoValue.Three, UnoColor.Blue)]);

        game.Apply(0, DrawCard.Instance);

        game.Hand(0).Should().HaveCount(2);
        game.Top.Should().Be(Card(UnoValue.Five, UnoColor.Red), "the top card stays");
        game.TakeEvents().Should().Contain(new DrawPileReshuffled());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BotRounds_AlwaysEnd_WithEveryCardAccountedFor(int players)
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var game = new UnoGame(players, seed);
            var match = new Match<UnoGame, UnoMove>(game, [.. Enumerable.Range(0, players).Select(s => new Player(s, $"Bot {s}", PlayerKind.Bot))],
                s => s % 2 == 0 ? new SmartUnoBot() : new RandomUnoBot(), seed)
            { BotDelay = TimeSpan.Zero };

            for (var turn = 0; turn < 2000 && !game.IsOver; turn++)
            {
                match.Update(TimeSpan.Zero);
            }

            game.IsOver.Should().BeTrue($"seed {seed} finishes");
            AllCards(game).Should().HaveCount(108).And.OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void SmartBots_BeatRandomOnes_MostOfTheTime()
    {
        var smartWins = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var game = new UnoGame(2, seed);
            var match = new Match<UnoGame, UnoMove>(game, [new Player(0, "Smart", PlayerKind.Bot), new Player(1, "Random", PlayerKind.Bot)],
                s => s == 0 ? new SmartUnoBot() : new RandomUnoBot(), seed)
            { BotDelay = TimeSpan.Zero };
            while (!game.IsOver)
            {
                match.Update(TimeSpan.Zero);
            }

            smartWins += game.Winner == 0 ? 1 : 0;
        }

        smartWins.Should().BeGreaterThan(115);
    }

    [Fact]
    public void AMatch_AddsUpRounds_AndPassesTheDeal()
    {
        var match = new UnoMatch(3, seed: 4, target: 100);
        Finish(match.Round);
        var points = match.ScoreRound();

        match.ScoreRound().Should().Be(points);
        match.Scores.Sum().Should().Be(points, "scoring twice counts once");
        match.NextRound();
        match.RoundNumber.Should().Be(2);
        match.Round.IsOver.Should().BeFalse();
        match.Scores.Sum().Should().Be(points);
        match.Round.Dealer.Should().Be(0, "the deal passes from the last seat to the first");

        while (match.Champion is null)
        {
            Finish(match.Round);
            if (match.Champion is null)
            {
                match.NextRound();
            }
        }

        match.Scores[match.Champion.Value].Should().BeGreaterThanOrEqualTo(100);
    }

    private static void Finish(UnoGame game)
    {
        var match = new Match<UnoGame, UnoMove>(game, [.. Enumerable.Range(0, game.Players).Select(s => new Player(s, "Bot", PlayerKind.Bot))], _ => new SmartUnoBot()) { BotDelay = TimeSpan.Zero };
        while (!game.IsOver)
        {
            match.Update(TimeSpan.Zero);
        }
    }

    private static int _copy = 2;

    private static UnoCard Card(UnoValue value, UnoColor color) => new(value, color);

    private static UnoCard Wild() => new(UnoValue.Wild, null);

    private static UnoCard WildFour() => new(UnoValue.WildDrawFour, null);

    /// <summary>A card that matches nothing red or five-ish in these tests.</summary>
    private static UnoCard Filler() => new(UnoValue.Nine, UnoColor.Yellow, Interlocked.Increment(ref _copy));

    private static UnoCard[] Fillers(int count) => [.. Enumerable.Range(0, count).Select(_ => Filler())];

    private static IEnumerable<UnoCard> AllCards(UnoGame game) =>
        Enumerable.Range(0, game.Players).SelectMany(game.Hand).Concat(Field<List<UnoCard>>(game, "_drawPile")).Concat(Field<List<UnoCard>>(game, "_discards"));

    /// <summary>A game with these hands, top card and draw pile (top of the pile last), seat 0 to play, going clockwise.</summary>
    private static UnoGame Rigged(UnoCard top, UnoCard[][] hands, UnoCard[]? drawPile = null, UnoCard[]? discards = null)
    {
        var game = new UnoGame(hands.Length, seed: 1, dealer: hands.Length - 1);
        for (var seat = 0; seat < hands.Length; seat++)
        {
            var hand = (List<UnoCard>)game.Hand(seat);
            hand.Clear();
            hand.AddRange(hands[seat]);
        }

        var pile = Field<List<UnoCard>>(game, "_drawPile");
        pile.Clear();
        pile.AddRange(drawPile ?? Fillers(10));
        var discardPile = Field<List<UnoCard>>(game, "_discards");
        discardPile.Clear();
        discardPile.AddRange(discards ?? []);
        discardPile.Add(top);
        typeof(UnoGame).GetProperty(nameof(UnoGame.Color))!.SetValue(game, top.Color);
        var order = Field<TurnOrder>(game, "_order");
        order.GoTo(0);
        if (order.Direction < 0)
        {
            order.Reverse();
        }

        game.TakeEvents();
        return game;
    }

    private static T Field<T>(UnoGame game, string name) =>
        (T)typeof(UnoGame).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
}
