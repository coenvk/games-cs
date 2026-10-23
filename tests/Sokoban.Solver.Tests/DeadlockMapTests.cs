using Sokoban.Model;

namespace Sokoban.Solver.Tests;

public class DeadlockMapTests
{
    // Goal at (3,3). Corners (1,1), (3,1), (1,3) are dead; so is the top wall row,
    // and the left column (no goal along either). The right column and bottom row lead to the goal.
    private static readonly Level Level = Xsb.ParseLevel("#####\n#@  #\n# $ #\n#  .#\n#####");

    private readonly DeadlockMap _map = new(Level.Board);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    public void IsDeadSquare_CornersAndGoalLessWalls(int x, int y) =>
        _map.IsDeadSquare(Level.Board.IndexOf(x, y)).Should().BeTrue();

    [Theory]
    [InlineData(2, 2, 2)]
    [InlineData(3, 2, 1)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 0)]
    public void GoalDistance_CountsPushes(int x, int y, int pushes) =>
        _map.GoalDistance(Level.Board.IndexOf(x, y)).Should().Be(pushes);

    [Fact]
    public void IsInFrozenSquare_PairAgainstWall()
    {
        var board = Xsb.ParseLevel("######\n#@   #\n# $$ #\n# .. #\n######").Board;
        var map = new DeadlockMap(board);

        // Two crates side by side against the bottom wall form a 2x2 block with it.
        var onGoals = new HashSet<int> { board.IndexOf(2, 3), board.IndexOf(3, 3) };
        map.IsInFrozenSquare(board.IndexOf(2, 3), onGoals.Contains).Should().BeFalse("both crates are on goals");

        var offGoal = new HashSet<int> { board.IndexOf(3, 3), board.IndexOf(4, 3) };
        map.IsInFrozenSquare(board.IndexOf(4, 3), offGoal.Contains).Should().BeTrue("the crate at (4,3) is off its goal");

        var apart = new HashSet<int> { board.IndexOf(2, 2), board.IndexOf(3, 2) };
        map.IsInFrozenSquare(board.IndexOf(2, 2), apart.Contains).Should().BeFalse("open floor above and below");
    }

    [Fact]
    public void DeadCrates_ReportsOnlyStuckOffGoalCrates()
    {
        var session = new LevelSession(Xsb.ParseLevel("######\n#$ @ #\n#  $ #\n# . .#\n######"));

        var dead = new DeadlockMap(session.Board).DeadCrates(session);

        dead.Should().Equal(session.Board.IndexOf(1, 1));
    }
}
