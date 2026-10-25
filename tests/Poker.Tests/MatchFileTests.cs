using Jentl.Multiplayer;
using Poker.Arena;

namespace Poker.Tests;

public class MatchFileTests
{
    private static MatchResult Match(BatchSettings? batch = null)
    {
        var withFault = TestHands.BigBlindWins() with { Faults = [(2, Fault.Late), (0, Fault.Illegal)] };
        return new MatchResult(
            new ArenaSettings(TableFormat.FixedStacks, 4, Seed: DealSeed.FromNumber(42), TimeSpan.FromMilliseconds(250), StackBigBlinds: 50),
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
        read.Hands.Select(h => (h.SmallBlindSeat, h.BigBlindSeat, h.SmallBlindPosted, h.BigBlindPosted)).Should().Equal(match.Hands.Select(h => (h.SmallBlindSeat, h.BigBlindSeat, h.SmallBlindPosted, h.BigBlindPosted)));
    }

    [Fact]
    public void ABatch_RoundTrips()
    {
        var match = Match(new BatchSettings(BatchFormat.DuplicateRing, 200, Seed: DealSeed.FromNumber(9), TimeSpan.FromSeconds(1), Parallel: true));

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

    [Fact]
    public async Task ASavedBatch_KeepsItsSeedInHex_AndRunsAgainTheSame()
    {
        var seed = DealSeed.New();
        var first = await Run(new BatchSettings(BatchFormat.DuplicateRing, 6, seed, TimeSpan.FromSeconds(5)));
        var json = MatchFile.Write(first);

        json.Should().Contain($"\"seed\":\"{seed}\"");
        var read = MatchFile.Read(json);
        read.Batch!.Seed.Should().Be(seed);
        read.Settings.Seed.Should().Be(seed);
        var again = await Run(read.Batch);
        again.Hands.Should().BeEquivalentTo(first.Hands, o => o.WithStrictOrdering().RespectingRuntimeTypes());
        again.Results.Should().BeEquivalentTo(first.Results, o => o.WithStrictOrdering());

        static Task<MatchResult> Run(BatchSettings settings) => new ArenaBatch(
            [
                () => new HouseSeat(new EquityBot(EquityStyle.Balanced, 1, samples: 20)),
                () => new HouseSeat(new RandomBot(2)),
                () => new HouseSeat(new RaisingBot()),
            ],
            settings).RunAsync(new Progress<double>(), CancellationToken.None);
    }

    [Fact]
    public void ASeedThatIsNotHex_IsNotAMatchFile()
    {
        var json = MatchFile.Write(Match()).Replace($"\"seed\":\"{DealSeed.FromNumber(42)}\"", "\"seed\":\"42!\"", StringComparison.Ordinal);

        FluentActions.Invoking(() => MatchFile.Read(json)).Should().Throw<InvalidDataException>();
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
    public void ANullWhereAValueIsNeeded_IsRefused()
    {
        var json = MatchFile.Write(Match()).Replace("\"bots\":[\"Ann\",\"Bob\",\"Cy\"]", "\"bots\":null", StringComparison.Ordinal);

        FluentActions.Invoking(() => MatchFile.Read(json)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AHandWhoseHolesDoNotMatchItsNames_IsRefused()
    {
        var json = MatchFile.Write(Match()).Replace("\"holes\":[[\"Ac\",\"Jc\"],[\"Ks\",\"Kh\"],[\"5s\",\"4s\"]]", "\"holes\":[[\"Ac\",\"Jc\"]]", StringComparison.Ordinal);

        FluentActions.Invoking(() => MatchFile.Read(json)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ASeatOutOfRange_IsRefused()
    {
        var json = MatchFile.Write(Match()).Replace("\"shown\":[0,1]", "\"shown\":[0,9]", StringComparison.Ordinal);

        FluentActions.Invoking(() => MatchFile.Read(json)).Should().Throw<InvalidDataException>();
    }

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

    [Fact]
    public async Task AWatchedMatch_HasOneResultPerSeat_InBigBlindsPer100()
    {
        var at = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        using var table = new ArenaTable([new HouseSeat(new CallingBot()), new HouseSeat(new RaisingBot()), new HouseSeat(new CallingBot())], new ArenaSettings(TableFormat.SitAndGo, 50, DealSeed.FromNumber(7), TimeSpan.FromSeconds(1)));
        await table.RunAsync(CancellationToken.None);

        var match = MatchResult.FromTable(table, at);

        match.Settings.Should().Be(table.Settings);
        match.Batch.Should().BeNull();
        match.At.Should().Be(at);
        match.Bots.Should().Equal(table.Results().Select(r => r.Name));
        match.Hands.Should().Equal(table.Hands);
        match.Results.Should().HaveCount(3);
        for (var seat = 0; seat < 3; seat++)
        {
            var seatResult = table.Results()[seat];
            var result = match.Results[seat];
            result.Name.Should().Be(seatResult.Name);
            result.BigBlindsPer100.Should().BeApproximately(seatResult.Net / 20.0 * 100 / seatResult.Hands, 1e-9);
            result.Margin95.Should().Be(0);
            result.Hands.Should().Be(seatResult.Hands);
            result.Gone.Should().Be(0);
            result.Places.Should().Equal(seatResult.Place!.Value);
        }
    }

    [Fact]
    public void Save_NamesTheFileByItsLocalTime_AndNeverOverwrites()
    {
        var folder = Path.Combine(Path.GetTempPath(), "matchfile-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Named in local time: the same moment as the batch clock (UTC) dates it.
            var match = Match() with { At = new DateTimeOffset(new DateTime(2026, 10, 25, 21, 8, 53, DateTimeKind.Local)).ToUniversalTime() };

            var first = MatchFile.Save(folder, match);
            var second = MatchFile.Save(folder, match);
            var third = MatchFile.Save(folder, match);

            Path.GetFileName(first).Should().Be("20261025-210853.json");
            Path.GetFileName(second).Should().Be("20261025-210853-2.json");
            Path.GetFileName(third).Should().Be("20261025-210853-3.json");
            MatchFile.Read(File.ReadAllText(second)).Results.Should().BeEquivalentTo(match.Results);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void Save_WhenWritingFails_LeavesNoPartFile_AndSaysSo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "matchfile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var match = Match() with { At = new DateTimeOffset(2026, 10, 25, 21, 8, 53, TimeSpan.FromHours(1)) };
            var kept = MatchFile.Save(folder, match);

            var save = () => MatchFile.Save(folder, match, (stream, _) =>
            {
                stream.WriteByte((byte)'{');
                throw new IOException("The disk is full.");
            });

            save.Should().Throw<IOException>().WithMessage("The disk is full.");
            Directory.GetFiles(folder).Should().Equal(kept);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
