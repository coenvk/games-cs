using System.Text.Json;
using Jentl.Cards;
using Jentl.Net;

namespace Poker.Tests;

/// <summary>Hold'em on a room server: the engine decides, bots and the clock play for whoever doesn't, and each seat sees only its own.</summary>
public class HoldemRoomTests
{
    private static readonly DealSeed Seed = DealSeed.Parse("5eed0000c0ffee00000000000000beef");
    private static readonly string[] PeopleNames = ["Ann", "Bob", "Cy", "Di", "Ed", "Flo"];

    [Fact]
    public void ActionsAndStates_SurviveJson()
    {
        HoldemOnlineAction[] actions = [new HoldemMove("raise", 60), new HoldemMove("fold"), new HoldemNextHand(), new HoldemNewGame()];
        foreach (var action in actions)
        {
            HoldemOnline.ReadAction(HoldemOnline.Write(action)).Should().Be(action);
        }

        var game = Started(People(3));
        var state = HoldemOnline.ReadState(game.StateFor(1, -1))!;
        var back = HoldemOnline.ReadState(HoldemOnline.Write(state))!;
        back.Hand.Holes.Should().BeEquivalentTo(state.Hand.Holes, o => o.WithStrictOrdering());
        back.Names.Should().Equal(state.Names);
        HoldemOnline.ReadState(JsonDocument.Parse("{\"seat\":\"x\"}").RootElement).Should().BeNull();
        HoldemOnline.ReadAction(JsonDocument.Parse("{\"action\":\"teleport\"}").RootElement).Should().BeNull();
    }

    [Fact]
    public void TheFirstHand_IsDealtAtTheStart_AndASeatSeesOnlyItsOwnCards()
    {
        var game = Started(People(3));
        var hand = game.Table!.Hand!;

        var state = HoldemOnline.ReadState(game.StateFor(1, -1))!;

        state.Seat.Should().Be(1);
        state.Names.Should().Equal("Ann", "Bob", "Cy");
        state.HandNumber.Should().Be(1);
        state.Hand.StartStacks.Should().Equal(1000, 1000, 1000);
        state.Hand.Holes[1].Should().Equal(hand.Hole(1).Select(BotView.Code));
        state.Hand.Holes[0].Should().BeEmpty();
        state.Hand.Holes[2].Should().BeEmpty();
        state.Hand.Board.Should().BeEmpty();
        state.TurnMilliseconds.Should().Be(60_000, "a person has a minute for each decision");
        game.MinSeats.Should().Be(2);
        game.MaxSeats.Should().Be(6);
    }

    [Fact]
    public void TheRoom_Refuses_WhatTheRulesDoNot()
    {
        var game = Started(People(2));
        var hand = game.Table!.Hand!;
        var turn = hand.CurrentSeat;

        game.Act(1 - turn, Move("call")).Should().Contain("not your turn");
        game.Act(turn, Move("check")).Should().Contain("can't");
        game.Act(turn, Move("raise", 5)).Should().Contain("can't");
        game.Act(turn, Move("dance")).Should().Contain("can't");
        game.Act(turn, HoldemOnline.Write(new HoldemNextHand())).Should().Be("Not now.");
        game.Act(turn, JsonDocument.Parse("{\"action\":\"teleport\"}").RootElement).Should().Contain("isn't");
        game.Act(turn, Move("raise", 60)).Should().BeNull();
        hand.CurrentBet.Should().Be(60);
    }

    [Fact]
    public void APersonWhoTakesTooLong_ChecksWhenItIsFree_AndFoldsOtherwise()
    {
        var game = Started(People(2));
        var hand = game.Table!.Hand!;
        var first = hand.CurrentSeat;

        game.Update(TimeSpan.FromSeconds(50));
        HoldemOnline.ReadState(game.StateFor(0, -1))!.TurnMilliseconds.Should().Be(10_000, "the countdown everyone sees");
        hand.CurrentSeat.Should().Be(first);
        game.Update(TimeSpan.FromSeconds(10));

        hand.IsOver.Should().BeTrue("the small blind faced a bet, so the clock folded");
        hand.HasFolded(first).Should().BeTrue();

        game.Act(0, HoldemOnline.Write(new HoldemNextHand())).Should().BeNull();
        game.Act(1, HoldemOnline.Write(new HoldemNextHand())).Should().BeNull();
        var next = game.Table.Hand!;
        next.Should().NotBeSameAs(hand, "everyone asked for the next hand");
        game.Act(next.CurrentSeat, Move("call")).Should().BeNull();
        var big = next.CurrentSeat;
        game.Update(TimeSpan.FromSeconds(60));
        next.Street.Should().Be(Street.Flop, "the big blind's free option was a check");
        next.HasFolded(big).Should().BeFalse();
    }

    [Fact]
    public void BotsPlay_AfterAShortPause()
    {
        var game = Started([new(0, "Ann", SeatKind.Human, true), new(1, "Ruby", SeatKind.Bot, false), new(2, "Otto", SeatKind.Bot, false), new(3, "Bot 4", SeatKind.Bot, false)]);
        var hand = game.Table!.Hand!;
        HoldemOnline.ReadState(game.StateFor(0, -1))!.Names.Should().Equal("Ann", "Ruby", "Otto", "Mila");

        for (var i = 0; i < 20 && hand.CurrentSeat != 0 && !hand.IsOver; i++)
        {
            hand.CurrentSeat.Should().NotBe(0);
            game.Update(TimeSpan.FromSeconds(0.2));
        }

        (hand.IsOver || hand.CurrentSeat == 0).Should().BeTrue("the bots took their turns within seconds");
        HoldemOnline.ReadState(game.StateFor(0, -1))!.Hand.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public void ALeaver_BecomesABot_AndTheGameGoesOn()
    {
        var game = Started(People(2));
        var hand = game.Table!.Hand!;
        var leaver = hand.CurrentSeat;
        var before = game.Version;

        game.Replace(leaver);

        game.Version.Should().BeGreaterThan(before);
        HoldemOnline.ReadState(game.StateFor(1 - leaver, -1))!.Names[leaver].Should().EndWith("(bot)");
        game.Update(TimeSpan.FromSeconds(1));
        (hand.IsOver || hand.CurrentSeat != leaver).Should().BeTrue("the bot took the turn at once, not after a minute");
    }

    [Fact]
    public void AFinishedHand_DealsTheNextAfterAPause()
    {
        var game = Started(People(2));
        var hand = game.Table!.Hand!;
        game.Act(hand.CurrentSeat, Move("fold")).Should().BeNull();
        hand.IsOver.Should().BeTrue();

        var state = HoldemOnline.ReadState(game.StateFor(0, -1))!;
        state.NextHandMilliseconds.Should().BePositive();
        game.Act(0, HoldemOnline.Write(new HoldemNextHand())).Should().BeNull();
        HoldemOnline.ReadState(game.StateFor(0, -1))!.Ready.Should().Equal(0);
        game.Table.Hand.Should().BeSameAs(hand, "Bob has not asked yet");

        game.Update(TimeSpan.FromSeconds(10));

        game.Table.HandNumber.Should().Be(2);
        HoldemOnline.ReadState(game.StateFor(0, -1))!.Ready.Should().BeEmpty();
    }

    [Fact]
    public void TheGame_EndsWithAChampion_AndANewOneCanStart()
    {
        var game = Started(People(2));
        PlayOut(game, _ => { });

        game.Table!.Champion.Should().NotBeNull();
        var state = HoldemOnline.ReadState(game.StateFor(0, -1))!;
        state.GameOver.Should().BeTrue();
        state.Champion.Should().Be(game.Table.Champion);
        game.Update(TimeSpan.FromMinutes(1));
        game.Table.Champion.Should().NotBeNull("a finished game waits for someone to start a new one");

        game.Act(1, HoldemOnline.Write(new HoldemNewGame())).Should().BeNull();

        game.Table.HandNumber.Should().Be(1);
        game.Table.Hand!.Stack(0).Should().BeLessThanOrEqualTo(1000);
        HoldemOnline.ReadState(game.StateFor(0, -1))!.GameOver.Should().BeFalse();
    }

    /// <summary>
    /// Whole games, people played by bots and one person leaving halfway: at every change, each seat's state names no
    /// card but its own, the board and the cards shown at a showdown; never a seed; and the hand rebuilt from it on a
    /// device matches the server's in all it shows.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void ASeatsState_NeverHoldsAnotherSeatsUnshownCards_NorASeed(int seats)
    {
        var game = new HoldemRoomGame(Seed, samples: 20);
        game.Start(People(seats), seed: 3);
        var checkedStates = 0;
        var showdowns = 0;
        var folds = 0;
        var lastOver = -1;
        PlayOut(game, _ =>
        {
            var table = game.Table!;
            var hand = table.Hand!;
            if (table.HandNumber == 6 && hand.Street == Street.Flop)
            {
                game.Replace(seats - 1);
            }

            var live = Enumerable.Range(0, seats).Where(hand.IsLive).ToArray();
            var showdown = hand.IsOver && live.Length > 1;
            for (var seat = 0; seat < seats; seat++)
            {
                var json = game.StateFor(seat, -1);
                var text = json.GetRawText();
                text.Should().NotContain(Seed.ToString()).And.NotContain(table.HandSeed.ToString());
                text.Should().NotContainEquivalentOf("seed");

                var allowed = hand.Hole(seat).Concat(hand.Board).ToHashSet();
                if (showdown)
                {
                    allowed.UnionWith(live.SelectMany(hand.Hole));
                }

                Cards(json).Should().BeSubsetOf(allowed, "seat {0} sees no other cards (hand {1})", seat, table.HandNumber);

                var state = HoldemOnline.ReadState(json)!;
                var rebuilt = HoldemOnline.Rebuild(state.Hand, state.Hand.Actions.Count);
                rebuilt.IsOver.Should().Be(hand.IsOver);
                rebuilt.Board.Should().Equal(hand.Board);
                rebuilt.Hole(seat).Should().Equal(hand.Hole(seat));
                Enumerable.Range(0, seats).Select(rebuilt.Stack).Should().Equal(Enumerable.Range(0, seats).Select(hand.Stack));
                rebuilt.Pots.Should().BeEquivalentTo(hand.Pots, o => o.WithStrictOrdering());
                if (hand.IsOver)
                {
                    rebuilt.Winnings.Should().BeEquivalentTo(hand.Winnings);
                }
                else
                {
                    rebuilt.CurrentSeat.Should().Be(hand.CurrentSeat);
                }

                checkedStates++;
            }

            if (hand.IsOver && game.Version != lastOver)
            {
                lastOver = game.Version;
                showdowns += showdown ? 1 : 0;
                folds += showdown ? 0 : 1;
            }
        });

        game.Table!.Champion.Should().NotBeNull();
        checkedStates.Should().BeGreaterThan(100);
        showdowns.Should().BePositive("showdowns were checked");
        folds.Should().BePositive("hands won by a fold were checked");
    }

    private static SeatInfo[] People(int count) =>
        [.. PeopleNames.Take(count).Select((name, seat) => new SeatInfo(seat, name, SeatKind.Human, true))];

    private static HoldemRoomGame Started(IReadOnlyList<SeatInfo> seats)
    {
        var game = new HoldemRoomGame(Seed, samples: 20);
        game.Start(seats, seed: 7);
        return game;
    }

    private static JsonElement Move(string move, int? to = null) => HoldemOnline.Write(new HoldemMove(move, to));

    /// <summary>
    /// Plays the game to its end: the people's seats as a bot would (with an occasional fold to the clock), the bots'
    /// as the server does, asking for each next hand. Calls <paramref name="check"/> after every change.
    /// </summary>
    private static void PlayOut(HoldemRoomGame game, Action<HoldemRoomGame> check)
    {
        var bot = new PokerBot(samples: 20);
        var random = new Random(5);
        var seen = -1;
        for (var step = 0; step < 100_000 && game.Table!.Champion is null; step++)
        {
            if (game.Version != seen)
            {
                seen = game.Version;
                check(game);
            }

            var hand = game.Table.Hand!;
            if (hand.IsOver)
            {
                for (var seat = 0; seat < hand.Seats; seat++)
                {
                    game.Act(seat, HoldemOnline.Write(new HoldemNextHand()));
                }

                continue;
            }

            var turn = hand.CurrentSeat;
            if (game.IsBot(turn) || random.Next(20) == 0)
            {
                game.Update(TimeSpan.FromSeconds(random.Next(20) == 0 ? 60 : 1));
                continue;
            }

            var (move, to) = HoldemOnline.Move(bot.Choose(hand, turn, random));
            game.Act(turn, HoldemOnline.Write(new HoldemMove(move, to))).Should().BeNull();
        }

        check(game);
    }

    /// <summary>Every card named anywhere in the state.</summary>
    private static HashSet<Card> Cards(JsonElement json)
    {
        var cards = new HashSet<Card>();
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        Walk(p.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
                case JsonValueKind.String when e.GetString() is { Length: 2 } code:
                    try
                    {
                        cards.Add(BotView.Card(code));
                    }
                    catch (ArgumentException)
                    {
                    }

                    break;
            }
        }

        Walk(json);
        return cards;
    }
}
