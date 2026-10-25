using Jentl.Multiplayer;
using Poker.Arena;

namespace Poker.Tests;

public class MatchFileTests
{
    private static MatchResult Match(BatchSettings? batch = null)
    {
        var withFault = TestHands.BigBlindWins() with { Faults = [(2, Fault.Late), (0, Fault.Illegal)] };
        return new MatchResult(
            new ArenaSettings(TableFormat.FixedStacks, 4, Seed: 42, TimeSpan.FromMilliseconds(250), StackBigBlinds: 50),
            batch,
            new DateTimeOffset(2026, 10, 25, 14, 3, 9, TimeSpan.FromHours(2)),
            ["Ann", "Bob", "Cy"],
            [
                new BotResult("Ann", 12.5, 3.25, 4, 1, 0, 0, [1, 2]),
                new BotResult("Bob", -12.5, 0, 4, 0, 2, 1, []),
            ],
            [TestHands.Showdown(), TestHands.AllInCall(), TestHands.RaiseTakesIt(), withFault]);
    }

    [Fact]
    public void AMatch_RoundTrips()
    {
        var match = Match();

        var read = MatchFile.Read(MatchFile.Write(match));

        read.Should().BeEquivalentTo(match, o => o.RespectingRuntimeTypes());
    }

    [Fact]
    public void ABatch_RoundTrips()
    {
        var match = Match(new BatchSettings(BatchFormat.DuplicateRing, 200, Seed: 9, TimeSpan.FromSeconds(1), Parallel: true));

        var read = MatchFile.Read(MatchFile.Write(match));

        read.Batch.Should().Be(match.Batch);
        read.Should().BeEquivalentTo(match, o => o.RespectingRuntimeTypes());
    }

    [Fact]
    public void TheFile_UsesCardCodes_TaggedMoves_AndMilliseconds()
    {
        var json = MatchFile.Write(Match());

        json.Should().Contain("\"timeLimitMs\":250").And.Contain("\"Ah\"").And.Contain("{\"street\":\"Preflop\",\"seat\":1,\"kind\":\"raise\",\"to\":60,\"amount\":50,\"total\":60}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[1, 2]")]
    public void ThingsThatAreNotAMatchFile_AreRefused(string text) =>
        FluentActions.Invoking(() => MatchFile.Read(text)).Should().Throw<InvalidDataException>();

    [Fact]
    public void ABadCard_IsRefused()
    {
        var json = MatchFile.Write(Match()).Replace("\"Ah\"", "\"Zz\"", StringComparison.Ordinal);

        FluentActions.Invoking(() => MatchFile.Read(json)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AnUnknownMove_IsRefused()
    {
        var json = MatchFile.Write(Match()).Replace("\"kind\":\"raise\"", "\"kind\":\"dance\"", StringComparison.Ordinal);

        FluentActions.Invoking(() => MatchFile.Read(json)).Should().Throw<InvalidDataException>();
    }
}
