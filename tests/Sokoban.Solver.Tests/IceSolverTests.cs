using Sokoban.Model;

namespace Sokoban.Solver.Tests;

public class IceSolverTests
{
    [Fact]
    public void Solve_UsesSlides()
    {
        // One push slides the crate across the ice onto the goal.
        var level = Xsb.ParseLevel("#########\n#@$~~~. #\n#########");

        var result = new Solver(level.Board).Solve(new LevelSession(level));

        result.Status.Should().Be(SolveStatus.Solved);
        result.Pushes.Should().Be(1);
        result.Moves.Should().Equal(new Move(Direction.Right, IsPush: true, Slide: 3));
    }

    [Fact]
    public void Solve_MixesSlidesAndOrdinaryPushes()
    {
        // The top crate slides over the ice to (6,2) and needs one more push; the bottom one is pushed on floor.
        var level = Xsb.ParseLevel("#########\n#   #   #\n#@$~~~ .#\n#   $  .#\n#########");

        var result = new Solver(level.Board).Solve(new LevelSession(level));

        result.Status.Should().Be(SolveStatus.Solved);
        result.Moves.Should().Contain(m => m.Slide > 0);
        Replay(level, result).IsSolved.Should().BeTrue();
    }

    [Fact]
    public void DeadSquares_CountStopsPartWayAcrossIce()
    {
        // The goal at (3,1) is above the ice. Pushed right from (2,2), a crate only stops on the ice
        // at (3,2) if another crate blocks it, but that is possible, so the analysis must allow it.
        var board = Xsb.ParseLevel("#######\n#  .  #\n#@$~~ #\n#     #\n#######").Board;

        var map = new DeadlockMap(board);

        map.GoalDistance(board.IndexOf(3, 2)).Should().Be(1);
        map.IsDeadSquare(board.IndexOf(2, 2)).Should().BeFalse();
        map.GoalDistance(board.IndexOf(2, 2)).Should().Be(2, "right onto the ice, stopped there, then up");
    }

    [Fact]
    public void Solve_GeneratedIceLevels_ReplayToSolved()
    {
        var options = new GeneratorOptions(Width: 9, Height: 8, Crates: 3, MinPushes: 4, Attempts: 30, IceCells: 6);

        for (var seed = 1; seed <= 3; seed++)
        {
            var level = LevelGenerator.Generate(options, new Random(seed));
            level.Should().NotBeNull($"seed {seed} should produce a level");
            level!.Board.HasIce.Should().BeTrue();

            var result = new Solver(level.Board).Solve(new LevelSession(level));
            result.Status.Should().Be(SolveStatus.Solved);
            result.Moves.Should().Contain(m => m.Slide > 0, "generated ice levels must actually use the ice");
            Replay(level, result).IsSolved.Should().BeTrue();
        }
    }

    private static LevelSession Replay(Level level, SolveResult result)
    {
        var session = new LevelSession(level);
        foreach (var move in result.Moves)
        {
            session.TryMove(move.Direction).Should().NotBe(MoveResult.Blocked);
            session.LastMove!.Value.Should().Be(move);
        }

        return session;
    }
}
