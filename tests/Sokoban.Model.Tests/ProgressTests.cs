namespace Sokoban.Model.Tests;

public class ProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    // "r" solves it in one push; "udr" solves it with a detour.
    private const string Map = "######\n#    #\n#@$. #\n#    #\n######";

    private readonly Level _level = Xsb.ParseLevel(Map);

    [Fact]
    public void Key_IsStableAndIgnoresTitle()
    {
        var titled = Xsb.ParseLevel("; Some title\n" + Map);

        titled.Key.Should().Be(_level.Key).And.HaveLength(16);
        Xsb.ParseLevel(Map).Key.Should().Be(_level.Key);
    }

    [Fact]
    public void Key_DiffersForDifferentLayouts()
    {
        var other = Xsb.ParseLevel("######\n#  # #\n#@$. #\n#    #\n######");

        other.Key.Should().NotBe(_level.Key);
    }

    [Fact]
    public void Record_FirstSolve_StoresEverything()
    {
        var progress = new Progress();

        progress.Record(SolvedSession("r"), TimeSpan.FromSeconds(5), Now).Should().Be(Improvement.FirstSolve);

        progress.Get(_level).Should().Be(new LevelRecord(1, 1, TimeSpan.FromSeconds(5), "R", Now));
        progress.IsSolved(_level).Should().BeTrue();
    }

    [Fact]
    public void Record_ReportsEachImprovementSeparately()
    {
        var progress = new Progress();
        progress.Record(SolvedSession("udr"), TimeSpan.FromSeconds(9), Now);

        var result = progress.Record(SolvedSession("r"), TimeSpan.FromSeconds(20), Now.AddDays(1));

        result.Should().Be(Improvement.Moves);
        var record = progress.Get(_level)!;
        record.BestMoves.Should().Be(1);
        record.Solution.Should().Be("R");
        record.BestTime.Should().Be(TimeSpan.FromSeconds(9));
        record.FirstSolved.Should().Be(Now);
    }

    [Fact]
    public void Record_WorseSolve_ChangesNothing()
    {
        var progress = new Progress();
        progress.Record(SolvedSession("r"), TimeSpan.FromSeconds(5), Now);

        progress.Record(SolvedSession("r"), TimeSpan.FromSeconds(6), Now).Should().Be(Improvement.None);
    }

    [Fact]
    public void Record_UnsolvedSession_Throws()
    {
        var act = () => new Progress().Record(new LevelSession(_level), TimeSpan.Zero, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FirstUnsolved_SkipsSolvedLevels()
    {
        var other = Xsb.ParseLevel("#####\n#@$.#\n#####");
        var levels = new[] { _level, other };
        var progress = new Progress();

        progress.FirstUnsolved(levels).Should().Be(0);
        progress.Record(SolvedSession("r"), TimeSpan.FromSeconds(1), Now);
        progress.FirstUnsolved(levels).Should().Be(1);
        progress.SolvedCount(levels).Should().Be(1);

        var session = new LevelSession(other);
        session.TryMove(Direction.Right);
        progress.Record(session, TimeSpan.FromSeconds(1), Now);
        progress.FirstUnsolved(levels).Should().BeNull();
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var progress = new Progress();
        progress.Record(SolvedSession("r"), TimeSpan.FromSeconds(5.5), Now);

        var restored = Progress.FromJson(progress.ToJson());

        restored.Get(_level).Should().Be(progress.Get(_level));
    }

    [Fact]
    public void Json_UsesVersionedCamelCaseFormat()
    {
        var json = new Progress().ToJson();

        json.Should().Contain("\"version\": 1").And.Contain("\"levels\"");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version": 99, "levels": {}}""")]
    [InlineData("null")]
    public void FromJson_RejectsBadData(string json)
    {
        var act = () => Progress.FromJson(json);

        act.Should().Throw<InvalidDataException>();
    }

    private LevelSession SolvedSession(string lurd)
    {
        var session = new LevelSession(_level);
        foreach (var c in lurd)
        {
            var direction = c switch
            {
                'u' => Direction.Up,
                'd' => Direction.Down,
                'l' => Direction.Left,
                _ => Direction.Right,
            };
            session.TryMove(direction).Should().NotBe(MoveResult.Blocked, $"move '{c}' should be legal");
        }

        session.IsSolved.Should().BeTrue();
        return session;
    }
}
