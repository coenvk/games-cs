using Sokoban.Model;

namespace Sokoban.Solver.Tests;

public class SolverTests
{
    [Fact]
    public void Solve_SimpleLevel_ReturnsMovesThatSolveIt()
    {
        var level = Xsb.ParseLevel("#######\n#@ $ .#\n#######");

        var result = new Solver(level.Board).Solve(new LevelSession(level));

        result.Status.Should().Be(SolveStatus.Solved);
        result.Pushes.Should().Be(2);
        ReplaySolves(level, result).Should().BeTrue();
    }

    [Fact]
    public void Solve_NeedsWalkingAroundCrates()
    {
        // The left crate must go up, so the player has to walk around to push it from below.
        var level = Xsb.ParseLevel("""
            #######
            #  .  #
            # #$# #
            #  @  #
            #.$   #
            #######
            """);

        var result = new Solver(level.Board).Solve(new LevelSession(level));

        result.Status.Should().Be(SolveStatus.Solved);
        ReplaySolves(level, result).Should().BeTrue();
    }

    [Fact]
    public void Solve_FromMidGamePosition()
    {
        var level = Xsb.ParseLevel("#######\n#@ $ .#\n#######");
        var session = new LevelSession(level);
        session.TryMove(Direction.Right);

        var result = new Solver(level.Board).Solve(session);

        result.Status.Should().Be(SolveStatus.Solved);
        foreach (var move in result.Moves)
        {
            session.TryMove(move.Direction).Should().NotBe(MoveResult.Blocked);
        }

        session.IsSolved.Should().BeTrue();
    }

    [Fact]
    public void Solve_CrateInCorner_IsUnsolvableWithoutSearching()
    {
        var stuck = new LevelSession(Xsb.ParseLevel("#####\n#$  #\n#  @#\n#  .#\n#####"));

        var result = new Solver(stuck.Board).Solve(stuck);

        result.Status.Should().Be(SolveStatus.Unsolvable);
        result.Explored.Should().Be(0);
    }

    [Fact]
    public void Solve_NoLegalPush_IsUnsolvableAfterSearching()
    {
        // The player is walled in to the right of the crate; pushing it left is the only push,
        // and from there no crate can reach the far goal.
        var level = Xsb.ParseLevel("""
            #######
            #.  $@#
            ## ####
            #.$   #
            #######
            """);

        var result = new Solver(level.Board).Solve(new LevelSession(level));

        result.Status.Should().Be(SolveStatus.Unsolvable);
        result.Explored.Should().BePositive();
    }

    [Fact]
    public void Solve_RespectsNodeBudget()
    {
        var level = OriginalLevels()[99];

        var result = new Solver(level.Board).Solve(new LevelSession(level), new SolverOptions(MaxNodes: 10));

        result.Status.Should().Be(SolveStatus.LimitReached);
    }

    [Fact]
    public void Solve_HonoursCancellation()
    {
        var level = OriginalLevels()[0];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = new Solver(level.Board).Solve(new LevelSession(level), cancellation: cancellation.Token);

        result.Status.Should().Be(SolveStatus.Cancelled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Solve_OriginalLevels(int index)
    {
        var level = OriginalLevels()[index];

        var result = new Solver(level.Board).Solve(new LevelSession(level));

        result.Status.Should().Be(SolveStatus.Solved);
        ReplaySolves(level, result).Should().BeTrue();
    }

    private static bool ReplaySolves(Level level, SolveResult result)
    {
        var session = new LevelSession(level);
        foreach (var move in result.Moves)
        {
            var outcome = session.TryMove(move.Direction);
            if (outcome == MoveResult.Blocked || (outcome == MoveResult.Pushed) != move.IsPush)
            {
                return false;
            }
        }

        return session.IsSolved;
    }

    private static IReadOnlyList<Level> OriginalLevels() =>
        Xsb.ParsePack(File.ReadAllText(Path.Combine("levels", "original.xsb")));
}
