namespace Paddock.Tests;

public class CampaignTests
{
    private static readonly Roster Cars = Roster.FromJson(GameData.Read("cars.json"));
    private static readonly IReadOnlyList<TrackDef> Tracks = GameData.Tracks;
    private static readonly IReadOnlyList<RegionDef> Regions = RegionDef.ListFromJson(GameData.Read("campaign.json"));
    private static readonly StageDef First = Regions[0].Stages[0];
    private static readonly StageDef Second = Regions[0].Stages[1];

    [Fact]
    public void FirstStageOpen_RestLocked()
    {
        var profile = Profile.New(Cars);

        CampaignPlay.IsOpen(profile, Regions, First).Should().BeTrue();
        CampaignPlay.IsOpen(profile, Regions, Second).Should().BeFalse();
        CampaignPlay.IsOpen(profile, Regions, Regions[1].Stages[0]).Should().BeFalse();
    }

    [Fact]
    public void Win_GivesNextPrize_UpToThree_AndOpensTheNextStage()
    {
        var profile = Profile.New(Cars);
        var won = new List<string>();

        for (var i = 0; i < 4; i++)
        {
            won.AddRange(WinOnce(profile, First).NewCars.Select(c => c.ModelId));
        }

        won.Should().Equal(First.PrizeCarIds);
        profile.Campaign.PrizesWon[First.Id].Should().Be(3);
        CampaignPlay.IsOpen(profile, Regions, Second).Should().BeTrue();
    }

    [Fact]
    public void NoTickets_Needed()
    {
        var profile = Profile.New(Cars);
        var hand = Cheapest(profile);

        for (var i = 0; i < 10; i++)
        {
            CampaignPlay.Race(profile, Regions, First, hand, Cars, Tracks);
        }

        profile.LastHand.Should().Equal(hand);
    }

    [Fact]
    public void Race_LockedStage_Throws()
    {
        var profile = Profile.New(Cars);

        var race = () => CampaignPlay.Race(profile, Regions, Second, Cheapest(profile), Cars, Tracks);

        race.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Setup_IsFixedPerStage_AndFitsTheLimit()
    {
        foreach (var stage in Regions.SelectMany(r => r.Stages))
        {
            var setup = CampaignPlay.Setup(stage, Cars, Tracks);
            setup.Should().BeEquivalentTo(CampaignPlay.Setup(stage, Cars, Tracks));
            Hands.Fits(setup.Opponent, Cars, stage.HandRqLimit).Should().BeTrue(stage.Id);
        }
    }

    [Fact]
    public void Data_ReferencesExist()
    {
        Regions.Should().HaveCount(5).And.OnlyContain(r => r.Stages.Count == 6);
        foreach (var stage in Regions.SelectMany(r => r.Stages))
        {
            stage.PrizeCarIds.Should().HaveCount(3).And.OnlyContain(id => Cars.Find(id) != null, stage.Id);
            stage.TrackIds.Should().OnlyContain(id => Tracks.Any(t => t.Id == id), stage.Id);
        }

        Regions.SelectMany(r => r.Stages).Select(s => s.HandRqLimit).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Progress_SurvivesSaving()
    {
        var profile = Profile.New(Cars);
        WinOnce(profile, First);

        Profile.FromJson(profile.ToJson(), Cars).Campaign.PrizesWon[First.Id].Should().Be(1);
    }

    private static List<Guid> Cheapest(Profile profile) =>
        [.. profile.Garage.OrderBy(c => Cars.Rq(c.ModelId)).Take(5).Select(c => c.Id)];

    // Wins with five fully upgraded copies of the strongest cars that fit, trying orders until one wins.
    private static CampaignOutcome WinOnce(Profile profile, StageDef stage)
    {
        var hand = new List<CarInstance>();
        var budget = stage.HandRqLimit;
        var byRq = Cars.All.OrderByDescending(m => Cars.Rq(m.Id)).ToList();
        var cheapest = byRq.Min(m => Cars.Rq(m.Id));
        while (hand.Count < 5)
        {
            var m = byRq.First(m => Cars.Rq(m.Id) + (cheapest * (4 - hand.Count)) <= budget);
            hand.Add(CarInstance.New(m.Id) with { Stage = 3, Tune = new Tune(9, 9, 6) });
            budget -= Cars.Rq(m.Id);
        }

        profile.Garage.AddRange(hand);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var order = hand.OrderBy(c => (c.Id.GetHashCode() * (attempt + 1)) % 97).Select(c => c.Id).ToList();
            var outcome = CampaignPlay.Race(profile, Regions, stage, order, Cars, Tracks);
            if (outcome.Result.PlayerWon)
            {
                return outcome;
            }
        }

        throw new InvalidOperationException("Could not win with the strongest cars.");
    }
}
