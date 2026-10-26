using GameServer.Poker;
using Jentl.Collect;

namespace GameServer.Tests.Poker;

/// <summary>Glicko-2 ratings and the ranking, over a temporary data folder.</summary>
public sealed class RatingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "poker-ratings-" + Guid.NewGuid().ToString("N"));
    private readonly Registry _registry;
    private readonly Registration _registration;
    private readonly Ratings _ratings;

    public RatingsTests()
    {
        var clock = TimeProvider.System;
        _registry = new Registry(new PokerData(_folder), clock);
        _registration = new Registration(_registry, new RegistrationLimits(), clock);
        _ratings = new Ratings(new PokerData(_folder), _registry);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string AddBot(string owner, string bot)
    {
        var issued = _registration.NewOwner(owner, null).Value;
        _registration.NewBot(issued.Token, bot, null);
        return bot;
    }

    [Fact]
    public void AWin_MovesBothBots_FromTheirRatingsBefore()
    {
        AddBot("Coen", "Shark");
        AddBot("Anna", "Tiger");

        _ratings.Record(new RankedOutcome("Shark", "Tiger", 1, 10, [2, 2, 2, 2, 2, 2, 2, 2, 2, 2]));

        var a = _ratings.Of("Shark");
        var b = _ratings.Of("Tiger");
        a.Rating.Rating.Should().BeGreaterThan(1500);
        b.Rating.Rating.Should().BeLessThan(1500);
        (a.Rating.Rating - 1500).Should().BeApproximately(1500 - b.Rating.Rating, 1e-6);
        a.Rating.Deviation.Should().BeLessThan(350);
        b.Rating.Deviation.Should().BeLessThan(350);
        (a.Matches, a.Won, a.Drawn, a.Lost).Should().Be((1, 1, 0, 0));
        (b.Matches, b.Won, b.Drawn, b.Lost).Should().Be((1, 0, 0, 1));
    }

    [Fact]
    public void NewBots_AreProvisional_AndListedAfterEstablishedOnes()
    {
        AddBot("Coen", "Veteran");
        AddBot("Anna", "Rookie");
        AddBot("Bert", "Other");
        File.WriteAllText(Path.Combine(_folder, "ratings.json"),
            """[{"bot":"Veteran","rating":{"rating":1400,"deviation":90,"volatility":0.06},"matches":50,"won":20,"drawn":0,"lost":30,"hands":5000,"netChips":0,"sumSquares":0,"lastPlayed":null}]""");
        var ratings = new Ratings(new PokerData(_folder), _registry);

        ratings.Record(new RankedOutcome("Rookie", "Other", 1, 4, [2, 2, 2, 2]));
        var ranking = ratings.Ranking();

        ranking.Select(r => r.Bot).Should().Equal("Veteran", "Rookie", "Other");
        ranking[0].Place.Should().Be(1);
        ranking[0].Provisional.Should().BeFalse();
        ranking[0].Owner.Should().Be("Coen");
        ranking[1].Place.Should().BeNull();
        ranking[1].Provisional.Should().BeTrue();
        ranking[2].Place.Should().BeNull();
    }

    [Fact]
    public void BbPer100_AndItsMargin_ComeFromAllHands()
    {
        AddBot("Coen", "Shark");
        AddBot("Anna", "Tiger");

        _ratings.Record(new RankedOutcome("Shark", "Tiger", 0.5, 2, [2, -2]));
        _ratings.Record(new RankedOutcome("Shark", "Tiger", 0.5, 2, [4, 0]));

        var row = _ratings.Ranking().Single(r => r.Bot == "Shark");
        var stats = new RunningStats();
        foreach (var bb in new[] { 1.0, -1, 2, 0 })
        {
            stats.Add(bb);
        }

        row.BbPer100.Should().BeApproximately(50, 1e-9);
        row.Margin.Should().BeApproximately(1.96 * stats.StandardDeviation / Math.Sqrt(4) * 100, 1e-6);
        var other = _ratings.Ranking().Single(r => r.Bot == "Tiger");
        other.BbPer100.Should().BeApproximately(-50, 1e-9);
        other.Margin.Should().BeApproximately(row.Margin, 1e-6);
        _ratings.Of("Shark").Drawn.Should().Be(2);
    }

    [Fact]
    public void AMatchWithoutEnoughHands_HasNoMargin()
    {
        AddBot("Coen", "Shark");
        AddBot("Anna", "Tiger");

        _ratings.Record(new RankedOutcome("Shark", "Tiger", 1, 1, [2]));

        _ratings.Ranking().Single(r => r.Bot == "Shark").Margin.Should().Be(0);
    }

    [Fact]
    public void ARestart_KeepsTheRatings()
    {
        AddBot("Coen", "Shark");
        AddBot("Anna", "Tiger");
        _ratings.Record(new RankedOutcome("Shark", "Tiger", 1, 3, [2, 2, -2]));

        var restarted = new Ratings(new PokerData(_folder), _registry);

        restarted.Of("Shark").Should().BeEquivalentTo(_ratings.Of("Shark"));
        restarted.Of("Shark").Rating.Rating.Should().BeGreaterThan(1500);
        restarted.Of("Nobody").Rating.Should().Be(GlickoRating.New);
    }

    [Fact]
    public void BannedBots_AreLeftOut()
    {
        AddBot("Coen", "Shark");
        AddBot("Anna", "Tiger");
        AddBot("Bert", "Lion");
        _ratings.Record(new RankedOutcome("Shark", "Tiger", 1, 1, [2]));
        _ratings.Record(new RankedOutcome("Tiger", "Lion", 1, 1, [2]));

        _registry.Ban("Shark");
        _ratings.Ranking().Select(r => r.Bot).Should().BeEquivalentTo("Tiger", "Lion");

        var tiger = _registry.Bot("Tiger")!;
        _registry.Ban(tiger.OwnerId);
        _ratings.Ranking().Select(r => r.Bot).Should().Equal("Lion");
    }
}
