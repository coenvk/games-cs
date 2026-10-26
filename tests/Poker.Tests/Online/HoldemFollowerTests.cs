using Jentl.Net;

namespace Poker.Tests;

/// <summary>A device following an online table: which states start a hand afresh, which carry it on, and what a bad one does.</summary>
public class HoldemFollowerTests
{
    private static readonly SeatInfo[] TwoPeople = [new(0, "Ann", SeatKind.Human, true), new(1, "Bob", SeatKind.Human, true)];

    [Fact]
    public void TheFirstState_StartsAHand_WithItsBlinds()
    {
        var game = Started();
        var follower = new HoldemFollower();

        var step = follower.Follow(State(game))!;

        step.Fresh.Should().BeTrue();
        step.Events.OfType<BlindPosted>().Should().HaveCount(2);
        follower.Applied.Should().Be(0);
    }

    [Fact]
    public void MoreMoves_CarryTheHandOn_WithOnlyTheNewEvents()
    {
        var game = Started();
        var follower = new HoldemFollower();
        var first = follower.Follow(State(game))!;
        Play(game, "call");

        var step = follower.Follow(State(game))!;

        step.Fresh.Should().BeFalse();
        step.Previous.Should().BeSameAs(first.Hand);
        step.Events.Should().ContainSingle().Which.Should().BeOfType<Acted>();
        follower.Follow(State(game)).Should().BeNull("nothing new happened");
    }

    [Fact]
    public void ANewGamesFirstHand_StartsAfresh_AfterTheLastGameEndedInItsFirstHand()
    {
        var game = Started();
        var follower = new HoldemFollower();
        follower.Follow(State(game));
        Play(game, "call");
        Play(game, "check");
        follower.Follow(State(game));
        follower.Applied.Should().Be(2);

        // The same hand number with no moves, in the next game: a new deal, not a hand going backwards.
        var next = Started();
        var step = follower.Follow(State(next) with { Game = 2 })!;

        step.Fresh.Should().BeTrue();
        follower.Applied.Should().Be(0);
        follower.Hand!.Board.Should().BeEmpty();
    }

    [Fact]
    public void FewerMovesThanShown_StartAfresh()
    {
        var game = Started();
        var follower = new HoldemFollower();
        var start = State(game);
        Play(game, "call");
        follower.Follow(State(game));

        var step = follower.Follow(start)!;

        step.Fresh.Should().BeTrue();
        follower.Applied.Should().Be(0);
    }

    [Fact]
    public void ABadState_IsRefused_AndTheNextGoodOneStartsAfresh()
    {
        var game = Started();
        var follower = new HoldemFollower();
        follower.Follow(State(game));
        Play(game, "call");
        var good = State(game);

        var bad = good with { Hand = good.Hand with { Button = 7 } };
        follower.Invoking(f => f.Follow(bad)).Should().Throw<InvalidDataException>();
        var nonsense = good with { Hand = good.Hand with { Actions = [new HoldemPlay(0, "dance")] } };
        follower.Invoking(f => f.Follow(nonsense)).Should().Throw<InvalidDataException>();
        var broken = good with { Hand = good.Hand with { Holes = [null!, []] } };
        follower.Invoking(f => f.Follow(broken)).Should().Throw<InvalidDataException>();

        follower.Follow(good)!.Fresh.Should().BeTrue("after a bad state the device starts over from the next good one");
    }

    [Fact]
    public void AStateWhoseSeatsDoNotAgree_IsNotRead()
    {
        var good = State(Started());

        HoldemOnline.ReadState(HoldemOnline.Write(good with { Names = ["Ann"] })).Should().BeNull();
        HoldemOnline.ReadState(HoldemOnline.Write(good with { Hand = good.Hand with { Holes = [[]] } })).Should().BeNull();
        HoldemOnline.ReadState(HoldemOnline.Write(good)).Should().NotBeNull();
    }

    private static HoldemRoomGame Started()
    {
        var game = new HoldemRoomGame(DealSeed.FromNumber(4), samples: 20);
        game.Start(TwoPeople, seed: 1);
        return game;
    }

    private static HoldemOnlineState State(HoldemRoomGame game) => HoldemOnline.ReadState(game.StateFor(0, -1))!;

    private static void Play(HoldemRoomGame game, string move) =>
        game.Act(game.Table!.Hand!.CurrentSeat, HoldemOnline.Write(new HoldemMove(game.GameNumber, game.Table.HandNumber, move))).Should().BeNull();
}
