using CheeseFarm;

namespace CheeseFarm.Tests;

public class SeasonTests
{
    private static Farm Rich() => Farm.FromJson(new Farm(1).ToJson().Replace("\"money\": 80", "\"money\": 5000", StringComparison.Ordinal));

    private static void To(Farm farm, Season season)
    {
        while (farm.Season != season)
        {
            farm.NextDay();
        }
    }

    [Theory]
    [InlineData(1, Season.Spring, 1)]
    [InlineData(14, Season.Spring, 1)]
    [InlineData(15, Season.Summer, 1)]
    [InlineData(43, Season.Winter, 1)]
    [InlineData(57, Season.Spring, 2)]
    public void TheYear_GoesRound(int day, Season season, int year)
    {
        Seasons.Of(day).Should().Be(season);
        Seasons.YearOf(day).Should().Be(year);
    }

    [Fact]
    public void ANewSeason_IsSaidInTheMorning()
    {
        var farm = Rich();
        var reports = Enumerable.Range(0, 20).Select(_ => farm.NextDay()).ToList();
        reports.Where(r => r.NewSeason is not null).Select(r => (r.Day, r.NewSeason)).Should().Equal((15, Season.Summer));
    }

    [Fact]
    public void InSummer_EachFieldIsMownOnce_ForHay()
    {
        var farm = Rich();
        farm.CanMow(0).Should().BeFalse("not in spring");
        To(farm, Season.Summer);

        farm.Mow(0).Should().Be(Seasons.Bales(Field.Grass));
        farm.Hay.Should().Be(Seasons.Bales(Field.Grass));
        farm.CanMow(0).Should().BeFalse("once a year");
        farm.Mow(0).Should().Be(0);
        farm.CanMow(1).Should().BeTrue();
    }

    [Fact]
    public void InWinter_TheHerdEatsHay_ThenBoughtFeed()
    {
        var farm = Rich();
        To(farm, Season.Summer);
        farm.Mow(0);
        To(farm, Season.Winter);
        farm.Grazing.Should().Be(0, "the herd is in the barn");

        var hay = farm.Hay;
        var report = farm.NextDay();
        report.HayEaten.Should().Be(Math.Min(hay, farm.Herd.Count));
        report.FeedBought.Should().Be(0, "there was hay enough");

        while (farm.Hay > 0)
        {
            farm.NextDay();
        }

        farm.Season.Should().Be(Season.Winter);
        farm.NextDay().FeedBought.Should().Be(farm.Herd.Count * Meadow.FeedPrice);
    }

    [Fact]
    public void MeadowBirds_NestOnlyInSpringAndSummer()
    {
        var farm = Rich();
        To(farm, Season.Autumn);
        farm.SetField(0, Field.Birds);
        var fledged = 0;
        while (Seasons.Of(farm.Day + 1) != Season.Spring)
        {
            fledged += farm.NextDay().Fledged;
        }

        fledged.Should().Be(0);
    }

    [Fact]
    public void HayAndMowing_AreSaved()
    {
        var farm = Rich();
        To(farm, Season.Summer);
        farm.Mow(2);
        var back = Farm.FromJson(farm.ToJson());
        back.Hay.Should().Be(farm.Hay);
        back.CanMow(2).Should().BeFalse();
        back.CanMow(3).Should().BeTrue();
    }
}
