namespace Sokoban.Model.Tests;

public class LevelSessionTests
{
    // Player at (1,1), crates at (3,1) and (2,2), goals at (5,1) and (2,3).
    private const string Map = """
        #######
        #@ $ .#
        # $   #
        # .   #
        #######
        """;

    private static LevelSession NewSession() => new(Xsb.ParseLevel(Map));

    private static (int X, int Y) PlayerPos(LevelSession s) => (s.Board.XOf(s.Player), s.Board.YOf(s.Player));

    [Fact]
    public void TryMove_WalksOntoFloor()
    {
        var session = NewSession();

        session.TryMove(Direction.Right).Should().Be(MoveResult.Walked);

        PlayerPos(session).Should().Be((2, 1));
        session.MoveCount.Should().Be(1);
        session.PushCount.Should().Be(0);
    }

    [Fact]
    public void TryMove_IsBlockedByWall()
    {
        var session = NewSession();

        session.TryMove(Direction.Up).Should().Be(MoveResult.Blocked);

        PlayerPos(session).Should().Be((1, 1));
        session.MoveCount.Should().Be(0);
    }

    [Fact]
    public void TryMove_PushesCrate()
    {
        var session = NewSession();
        session.TryMove(Direction.Right);

        session.TryMove(Direction.Right).Should().Be(MoveResult.Pushed);

        session.HasCrate(session.Board.IndexOf(4, 1)).Should().BeTrue();
        session.HasCrate(session.Board.IndexOf(3, 1)).Should().BeFalse();
        session.PushCount.Should().Be(1);
    }

    [Fact]
    public void TryMove_CannotPushCrateIntoWall()
    {
        var session = NewSession();
        session.TryMove(Direction.Down);
        session.TryMove(Direction.Down);
        session.TryMove(Direction.Right); // now at (2,3), crate (2,2) above

        session.TryMove(Direction.Up).Should().Be(MoveResult.Pushed);
        session.TryMove(Direction.Up).Should().Be(MoveResult.Blocked); // crate at (2,1) against wall
    }

    [Fact]
    public void TryMove_CannotPushTwoCrates()
    {
        var session = new LevelSession(Xsb.ParseLevel("""
            #######
            #@$$..#
            #######
            """));

        session.TryMove(Direction.Right).Should().Be(MoveResult.Blocked);
    }

    [Fact]
    public void IsSolved_WhenAllCratesOnGoals()
    {
        var session = NewSession();
        Play(session, "rrr"); // crate 1 onto goal (5,1)
        session.IsSolved.Should().BeFalse();

        Play(session, "lld"); // back to (2,1), push crate 2 down onto (2,3)

        session.IsSolved.Should().BeTrue();
        session.ToLurd().Should().Be("rRRllD");
    }

    [Fact]
    public void ToLurd_UppercasesPushes()
    {
        var session = NewSession();

        Play(session, "rrrd");

        session.ToLurd().Should().Be("rRRd");
    }

    [Fact]
    public void Undo_RevertsWalkAndPush()
    {
        var session = NewSession();
        var start = Xsb.Write(session);
        Play(session, "rr");

        session.Undo().Should().BeTrue();
        session.Undo().Should().BeTrue();

        Xsb.Write(session).Should().Be(start);
        session.PushCount.Should().Be(0);
        session.Undo().Should().BeFalse();
    }

    [Fact]
    public void Undo_RevertsSolvedState()
    {
        var session = NewSession();
        Play(session, "rrrlld");
        session.IsSolved.Should().BeTrue();

        session.Undo();

        session.IsSolved.Should().BeFalse();
    }

    [Fact]
    public void Redo_ReappliesUndoneMoves()
    {
        var session = NewSession();
        Play(session, "rrr");
        var after = Xsb.Write(session);
        session.Undo();
        session.Undo();

        session.Redo().Should().BeTrue();
        session.Redo().Should().BeTrue();

        Xsb.Write(session).Should().Be(after);
        session.PushCount.Should().Be(2);
        session.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void TryMove_ClearsRedo()
    {
        var session = NewSession();
        Play(session, "rr");
        session.Undo();

        session.TryMove(Direction.Down);

        session.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Restart_ResetsEverything()
    {
        var session = NewSession();
        var start = Xsb.Write(session);
        Play(session, "rrrd");

        session.Restart();

        Xsb.Write(session).Should().Be(start);
        session.MoveCount.Should().Be(0);
        session.CanUndo.Should().BeFalse();
    }

    private static void Play(LevelSession session, string lurd)
    {
        foreach (var c in lurd)
        {
            var direction = char.ToLowerInvariant(c) switch
            {
                'u' => Direction.Up,
                'd' => Direction.Down,
                'l' => Direction.Left,
                'r' => Direction.Right,
                _ => throw new ArgumentException($"Bad move '{c}'.", nameof(lurd)),
            };
            session.TryMove(direction).Should().NotBe(MoveResult.Blocked, $"move '{c}' should be legal");
        }
    }
}
