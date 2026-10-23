namespace Sokoban.Model.Tests;

public class PathTests
{
    // Player at (1,1); a crate at (3,1) blocks the top row, so reaching (4,1) means going round below.
    private readonly LevelSession _session = new(Xsb.ParseLevel("######\n#@ $ #\n#    #\n#.   #\n######"));

    [Fact]
    public void FindPath_WalksAroundCrates()
    {
        var path = _session.FindPath(_session.Board.IndexOf(4, 1));

        path.Should().NotBeNull();
        path!.Count.Should().Be(5, "down, right three times, up");
        foreach (var step in path)
        {
            _session.TryMove(step).Should().Be(MoveResult.Walked);
        }

        _session.Player.Should().Be(_session.Board.IndexOf(4, 1));
    }

    [Fact]
    public void FindPath_ToOwnCell_IsEmpty() =>
        _session.FindPath(_session.Player).Should().BeEmpty();

    [Theory]
    [InlineData(3, 1)] // crate
    [InlineData(0, 0)] // wall
    public void FindPath_ToBlockedCell_IsNull(int x, int y) =>
        _session.FindPath(_session.Board.IndexOf(x, y)).Should().BeNull();

    [Fact]
    public void FindPath_ToUnreachableCell_IsNull()
    {
        // The player is boxed in on the left by two crates against the walls.
        var session = new LevelSession(Xsb.ParseLevel("#######\n#@$ ..#\n#$    #\n#######"));

        session.FindPath(session.Board.IndexOf(4, 2)).Should().BeNull();
    }
}
