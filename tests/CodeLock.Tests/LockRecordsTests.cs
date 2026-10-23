namespace CodeLock.Tests;

public class LockRecordsTests
{
    [Fact]
    public void Wins_KeepTheFewestTries_AndBuildStreaks()
    {
        var records = new LockRecords();

        records.Record("normal", solved: true, tries: 6).Should().Be(new LockResult(NewBest: true, NewBestStreak: false));
        records.Record("normal", solved: true, tries: 7).Should().Be(new LockResult(NewBest: false, NewBestStreak: true));
        records.Record("normal", solved: true, tries: 4).NewBest.Should().BeTrue();

        records["normal"].Should().Be(new LockRecord(Played: 3, Won: 3, BestTries: 4, Streak: 3, BestStreak: 3));
    }

    [Fact]
    public void ALoss_CountsAsPlayed_AndEndsTheStreak_ButKeepsTheBests()
    {
        var records = new LockRecords();
        records.Record("hard", solved: true, tries: 8);
        records.Record("hard", solved: true, tries: 9);

        records.Record("hard", solved: false, tries: 10).Should().Be(default(LockResult));

        records["hard"].Should().Be(new LockRecord(Played: 3, Won: 2, BestTries: 8, Streak: 0, BestStreak: 2));
        records.Record("hard", solved: true, tries: 9).NewBestStreak.Should().BeFalse("one win does not beat a streak of two");
    }

    [Fact]
    public void Difficulties_AreKeptApart()
    {
        var records = new LockRecords();
        records.Record("easy", solved: true, tries: 3);

        records["normal"].Should().Be(LockRecord.None);
        records["easy"].BestTries.Should().Be(3);
    }

    [Fact]
    public void Records_RoundTripThroughJson()
    {
        var records = new LockRecords();
        records.Record("easy", solved: true, tries: 3);
        records.Record("normal", solved: false, tries: 10);

        var loaded = LockRecords.FromJson(records.ToJson());

        loaded.All.Should().BeEquivalentTo(records.All);
        loaded["normal"].BestTries.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": 9 }""")]
    [InlineData("""{ "version": 1, "locks": { "easy": { "played": "many" } } }""")]
    public void BadFiles_AreReportedAsInvalidData(string json)
    {
        var act = () => LockRecords.FromJson(json);

        act.Should().Throw<InvalidDataException>();
    }
}
