namespace AssociateWords.Tests;

public class AssociateDealsTests
{
    private static string Asset(string file)
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "Games.slnx")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        return File.ReadAllText(Path.Combine(folder, "apps", "AssociateWords", "Assets", "associatewords", file));
    }

    private static IReadOnlyList<AssociateLevel> English => AssociateLevels.FromJson(Asset("levels.json"));

    private static IReadOnlyList<WordGroup> Dutch => AssociateLevels.GroupsFromJson(Asset("groups.nl.json"));

    [Theory]
    [InlineData(DealSize.Small)]
    [InlineData(DealSize.Medium)]
    [InlineData(DealSize.Large)]
    public void Deals_OfEverySize_CanBeWon_InBothLanguages(DealSize size)
    {
        foreach (var bank in new[] { AssociateDeals.Bank(English), Dutch })
        {
            for (var seed = 1; seed <= 5; seed++)
            {
                var level = AssociateDeals.Make(bank, seed, size);

                AssociateAutoplay.Play(new AssociateGame(level)).Should().NotBeNull().And.BeLessThanOrEqualTo(level.Moves);
            }
        }
    }

    [Fact]
    public void ADeal_IsTheSameForTheSameSeed()
    {
        var bank = AssociateDeals.Bank(English);
        var date = AssociateDeals.DailySeed(new DateOnly(2026, 11, 12));

        AssociateDeals.Make(bank, date, DealSize.Medium).Should().BeEquivalentTo(AssociateDeals.Make(bank, date, DealSize.Medium), o => o.WithStrictOrdering());
        date.Should().Be(20261112);
    }

    [Fact]
    public void TheDutchLevels_FollowTheEnglishShapes_AndCanAllBeWon()
    {
        var english = English;
        var dutch = AssociateDeals.Translate(english, Dutch);

        dutch.Should().HaveCount(english.Count);
        for (var i = 0; i < dutch.Count; i++)
        {
            dutch[i].Slots.Should().Be(english[i].Slots);
            dutch[i].Columns.Should().Equal(english[i].Columns);
            dutch[i].Groups.Should().HaveCount(english[i].Groups.Count);
            AssociateAutoplay.Play(new AssociateGame(dutch[i])).Should().NotBeNull();
        }
    }
}
