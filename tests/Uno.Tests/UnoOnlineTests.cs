using Jentl.Net;

namespace Uno.Tests;

public class UnoOnlineTests
{
    private static readonly SeatInfo[] TwoPeople =
    [
        new(0, "Ann", SeatKind.Human, true),
        new(1, "Bob", SeatKind.Human, true),
    ];

    [Fact]
    public void AView_ShowsOwnCards_AndOnlyCountsOfTheOthers()
    {
        var match = new UnoMatch(3, seed: 4);

        var view = UnoView.Of(match, 1);

        view.Hand.Should().Equal(match.Round.Hand(1));
        view.HandCounts.Should().Equal(7, 7, 7);
        view.Players.Should().Be(3);
        view.LegalMoves.Should().BeEquivalentTo(match.Round.LegalMoves(1));
    }

    [Fact]
    public void TheDrawnCard_IsOnlyShownToWhoeverDrewIt()
    {
        var match = new UnoMatch(2, seed: 1);
        var seat = match.Round.CurrentSeat;

        // Draw until a playable card is drawn (and kept as Drawn), or give up after the turn passes.
        match.Round.Apply(seat, DrawCard.Instance);

        if (match.Round.Drawn is { } drawn)
        {
            UnoView.Of(match, seat).Drawn.Should().Be(drawn);
            UnoView.Of(match, 1 - seat).Drawn.Should().BeNull();
        }
    }

    [Fact]
    public void Legality_IgnoresTheUnoCall_ButNotTheColour()
    {
        var wild = new UnoCard(UnoValue.Wild, null);
        var seven = new UnoCard(UnoValue.Seven, UnoColor.Red);
        UnoMove[] legal = [new PlayCard(seven, null, CallUno: true), new PlayCard(wild, UnoColor.Blue), DrawCard.Instance];

        UnoView.IsLegal(new PlayCard(seven), legal).Should().BeTrue("forgetting to call Uno is allowed (and punished)");
        UnoView.IsLegal(new PlayCard(wild, UnoColor.Blue), legal).Should().BeTrue();
        UnoView.IsLegal(new PlayCard(wild), legal).Should().BeFalse("a wild needs a colour");
        UnoView.IsLegal(new PlayCard(wild, UnoColor.Red), legal).Should().BeFalse();
        UnoView.IsLegal(Pass.Instance, legal).Should().BeFalse();
        UnoView.IsLegal(DrawCard.Instance, legal).Should().BeTrue();
    }

    [Fact]
    public void ActionsAndStates_SurviveJson()
    {
        var action = new UnoPlay(new PlayCard(new UnoCard(UnoValue.WildDrawFour, null, 2), UnoColor.Green, CallUno: true));

        UnoOnline.ReadAction(UnoOnline.Write(action)).Should().Be(action);

        var match = new UnoMatch(2, seed: 3);
        var state = new UnoOnlineState(UnoView.Of(match, 0), ["Ann", "Bob"], [new CardsDrawn(1, 2), new RoundWon(0, 40)]);
        var back = UnoOnline.ReadState(UnoOnline.Write(state))!;
        back.View.Hand.Should().Equal(state.View.Hand);
        back.View.LegalMoves.Should().Equal(state.View.LegalMoves);
        back.Events.Should().Equal(state.Events);
    }

    [Fact]
    public void TheRoom_Refuses_WhatTheRulesDoNot()
    {
        var game = Started();

        var waiting = 1 - game.Match!.Round.CurrentSeat;
        game.Act(waiting, UnoOnline.Write(new UnoPlay(DrawCard.Instance))).Should().Contain("not your turn");
        game.Act(game.Match.Round.CurrentSeat, UnoOnline.Write(new UnoPlay(Pass.Instance))).Should().Contain("can't");
        game.Act(0, UnoOnline.Write(new UnoNextRound())).Should().Be("Not now.");
    }

    [Fact]
    public void AFinishedRound_DealsTheNextOne_WhenAskedOrAfterAPause()
    {
        var game = Started();
        PlayOutRound(game);
        game.Match!.Round.IsOver.Should().BeTrue();
        var round = game.Match.RoundNumber;

        game.Act(0, UnoOnline.Write(new UnoNextRound())).Should().BeNull();
        game.Match.RoundNumber.Should().Be(round + 1);

        PlayOutRound(game);
        game.Update(TimeSpan.FromSeconds(16));
        game.Match.RoundNumber.Should().Be(round + 2);
    }

    [Fact]
    public void ALeaver_BecomesABot_AndTheGameGoesOn()
    {
        var game = Started();
        var before = game.Version;

        game.Replace(1);

        game.Version.Should().BeGreaterThan(before);
        UnoOnline.ReadState(game.StateFor(0, -1))!.Names[1].Should().Be("Bob (bot)");
        for (var i = 0; i < 50 && game.Match!.Round.CurrentSeat == 1; i++)
        {
            game.Update(TimeSpan.FromSeconds(1));
        }

        game.Match!.Round.CurrentSeat.Should().Be(0, "the bot took its turn");
    }

    [Fact]
    public void States_CarryOnlyTheEventsSinceTheLastOne()
    {
        var game = Started();
        var since = game.Version;
        var seat = game.Match!.Round.CurrentSeat;

        game.Act(seat, UnoOnline.Write(new UnoPlay(DrawCard.Instance)));

        UnoOnline.ReadState(game.StateFor(0, since))!.Events.Should().ContainSingle(e => e is CardsDrawn);
        UnoOnline.ReadState(game.StateFor(0, game.Version))!.Events.Should().BeEmpty();
    }

    private static UnoRoomGame Started()
    {
        var game = new UnoRoomGame();
        game.Start(TwoPeople, seed: 11);
        return game;
    }

    /// <summary>Both people play the bots' way until someone wins the round.</summary>
    private static void PlayOutRound(UnoRoomGame game)
    {
        var bot = new SmartUnoBot();
        var random = new Random(1);
        for (var i = 0; i < 2000 && !game.Match!.Round.IsOver; i++)
        {
            var round = game.Match.Round;
            var move = bot.Choose(round, round.CurrentSeat, random);
            game.Act(round.CurrentSeat, UnoOnline.Write(new UnoPlay(move))).Should().BeNull();
        }
    }
}
