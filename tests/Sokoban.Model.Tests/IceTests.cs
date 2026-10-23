namespace Sokoban.Model.Tests;

public class IceTests
{
    [Fact]
    public void Push_OntoIce_SlidesUntilFirstCellThatIsNotIce()
    {
        // Crate at (2,1) slides over the ice at (3,1)-(5,1) and stops on the floor at (6,1).
        var session = new LevelSession(Xsb.ParseLevel("##########\n#@$~~~ .#\n##########"));

        session.TryMove(Direction.Right).Should().Be(MoveResult.Pushed);

        session.HasCrate(session.Board.IndexOf(6, 1)).Should().BeTrue();
        session.LastMove.Should().Be(new Move(Direction.Right, IsPush: true, Slide: 3));
        session.Player.Should().Be(session.Board.IndexOf(2, 1));
    }

    [Fact]
    public void Push_OnIce_StopsAgainstWall()
    {
        var session = new LevelSession(Xsb.ParseLevel("#######\n#.@$~~#\n#######"));

        session.TryMove(Direction.Right);

        session.HasCrate(session.Board.IndexOf(5, 1)).Should().BeTrue("the crate stops on the last ice cell before the wall");
    }

    [Fact]
    public void Push_OnIce_StopsAgainstAnotherCrate()
    {
        var session = new LevelSession(Xsb.ParseLevel("#########\n#@$~~$ ..#\n#########"));

        session.TryMove(Direction.Right);

        session.HasCrate(session.Board.IndexOf(4, 1)).Should().BeTrue();
        session.LastMove!.Value.Slide.Should().Be(1);
    }

    [Fact]
    public void Push_SlidingAcrossGoal_DoesNotStopThere()
    {
        // The goal at (6,1) sits past the ice; the crate stops on it because it is the first non-ice cell.
        var session = new LevelSession(Xsb.ParseLevel("#########\n#@$~~~. #\n#########"));

        session.TryMove(Direction.Right);

        session.IsSolved.Should().BeTrue();
    }

    [Fact]
    public void PlayerWalkingOnIce_DoesNotSlide()
    {
        var session = new LevelSession(Xsb.ParseLevel("#######\n#@~~~ #\n# $. ##\n#######"));

        session.TryMove(Direction.Right).Should().Be(MoveResult.Walked);

        session.Player.Should().Be(session.Board.IndexOf(2, 1));
    }

    [Fact]
    public void Undo_ReturnsSlidCrateToWhereItWasPushedFrom()
    {
        var session = new LevelSession(Xsb.ParseLevel("##########\n#@$~~~ .#\n##########"));
        var start = Xsb.Write(session);
        session.TryMove(Direction.Right);

        session.Undo();

        Xsb.Write(session).Should().Be(start);
        session.PushCount.Should().Be(0);
    }

    [Fact]
    public void Redo_SlidesAgain()
    {
        var session = new LevelSession(Xsb.ParseLevel("##########\n#@$~~~ .#\n##########"));
        session.TryMove(Direction.Right);
        var after = Xsb.Write(session);
        session.Undo();

        session.Redo();

        Xsb.Write(session).Should().Be(after);
    }

    [Fact]
    public void Xsb_RoundTripsIce_IncludingCratesAndPlayerOnIce()
    {
        var session = new LevelSession(Xsb.ParseLevel("#########\n#@$~~ ~.#\n#########"));
        session.TryMove(Direction.Right); // the crate slides to (5,1), the player stands at (2,1)
        session.TryMove(Direction.Right); // the player steps onto ice at (3,1)

        var written = Xsb.Write(session);

        written.Should().Be("#########\n#  &~$~.#\n#########\n");
        Xsb.Write(Xsb.ParseLevel(written)).Should().Be(written);
    }

    [Fact]
    public void Level_RejectsIceOnGoal()
    {
        var act = () => Level.Create(5, 3, [0, 1, 2, 3, 4, 5, 9, 10, 11, 12, 13, 14], goals: [8], player: 6, crates: [7], ice: [8]);

        act.Should().Throw<InvalidLevelException>().WithMessage("*goal*");
    }

    [Fact]
    public void LevelsWithoutIce_KeepTheirKey()
    {
        // Progress is keyed by layout, so adding the ice extension must not change classic levels.
        var level = Xsb.ParseLevel("#####\n#@$.#\n#####");

        level.Board.HasIce.Should().BeFalse();
        Xsb.Write(level).Should().NotContain("~");
    }
}
