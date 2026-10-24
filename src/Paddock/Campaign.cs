namespace Paddock;

/// <summary>One campaign stage: a fixed opponent on a region's tracks, with three prize cars to win in turn.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Shown name.</param>
/// <param name="HandRqLimit">The hand's RQ limit here (and the opponent's).</param>
/// <param name="TrackIds">The tracks its rounds are raced on.</param>
/// <param name="PrizeCarIds">The three prize cars, won one per win.</param>
public sealed record StageDef(string Id, string Name, int HandRqLimit, IReadOnlyList<string> TrackIds, IReadOnlyList<string> PrizeCarIds);

/// <summary>A campaign region: six stages in order.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Shown name (English; translated through the language file).</param>
/// <param name="Stages">Its stages, easiest first.</param>
public sealed record RegionDef(string Id, string Name, IReadOnlyList<StageDef> Stages)
{
    /// <summary>Reads <c>campaign.json</c>.</summary>
    public static IReadOnlyList<RegionDef> ListFromJson(string json) =>
        (System.Text.Json.JsonSerializer.Deserialize(json, PaddockJson.Default.CampaignFile) ?? throw new FormatException("No campaign.")).Regions;
}

/// <summary>The contents of <c>campaign.json</c>.</summary>
internal sealed record CampaignFile(IReadOnlyList<RegionDef> Regions);

/// <summary>The player's campaign progress.</summary>
public sealed class CampaignProgress
{
    /// <summary>Prize cars won per stage (0 to 3); a stage opens once the one before has been won.</summary>
    public Dictionary<string, int> PrizesWon { get; set; } = [];

    /// <summary>Prize cars won at <paramref name="stage"/>.</summary>
    public int Won(StageDef stage) => stage is null ? 0 : PrizesWon.GetValueOrDefault(stage.Id);
}

/// <summary>What a raced campaign match brought.</summary>
/// <param name="Result">The match.</param>
/// <param name="NewCars">The prize car won, if any.</param>
/// <param name="Cash">Cash paid.</param>
/// <param name="Xp">Experience gained.</param>
public sealed record CampaignOutcome(MatchResult Result, IReadOnlyList<CarInstance> NewCars, long Cash, int Xp);

/// <summary>Playing the campaign: no tickets, as often as you like.</summary>
public static class CampaignPlay
{
    /// <summary>Whether <paramref name="stage"/> can be raced: the first stage always, any other once the stage before it is won.</summary>
    public static bool IsOpen(Profile profile, IReadOnlyList<RegionDef> regions, StageDef stage)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(regions);
        var all = regions.SelectMany(r => r.Stages).ToList();
        var index = all.FindIndex(s => s.Id == stage?.Id);
        return index == 0 || (index > 0 && profile.Campaign.Won(all[index - 1]) > 0);
    }

    /// <summary>Whether the player can race <paramref name="stage"/>: it is open, and five cars fit its limit. If not, <paramref name="why"/> says why (English, for translation).</summary>
    public static bool CanEnter(Profile profile, IReadOnlyList<RegionDef> regions, StageDef stage, Roster roster, out string? why)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(roster);
        var cheapest = profile.Garage.Where(c => roster.Find(c.ModelId) is not null).Select(c => roster.Rq(c.ModelId)).Order().Take(Hands.Size).ToList();
        why = !IsOpen(profile, regions, stage) ? "Win the stage before this one first."
            : cheapest.Count < Hands.Size || cheapest.Sum() > HandLimit(profile, stage) ? "Not enough cars that may enter"
            : null;
        return why is null;
    }

    /// <summary>The hand's RQ limit here: the stage's, or the player's level's if that is lower, so levelling up opens later stages fully.</summary>
    public static int HandLimit(Profile profile, StageDef stage)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(stage);
        return Math.Min(stage.HandRqLimit, Progression.RqLimit(profile.Level));
    }

    /// <summary>The stage's match: always the same, with an opponent at 85% of the limit, upgraded more in later stages.</summary>
    public static MatchSetup Setup(StageDef stage, Roster roster, IReadOnlyList<TrackDef> tracks)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var random = new Random(Jentl.Collect.StableSeed.Of(stage.Id));
        var upgrades = Math.Min(Upgrades.PointsPerStage, (stage.HandRqLimit - Balance.FirstRqLimit) / 50);
        var opponent = Opponents.Pick(roster, _ => true, stage.HandRqLimit, Balance.CampaignStrength, upgrades, random);
        return new MatchSetup(Opponents.Rounds(stage.TrackIds, tracks, random), opponent);
    }

    /// <summary>Races the stage with the player's cars in <paramref name="order"/>; a win pays the next prize car until all three are won.</summary>
    /// <exception cref="InvalidOperationException">The stage is locked or the hand breaks the rules.</exception>
    public static CampaignOutcome Race(Profile profile, IReadOnlyList<RegionDef> regions, StageDef stage, IReadOnlyList<Guid> order, Roster roster, IReadOnlyList<TrackDef> tracks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(roster);
        if (!IsOpen(profile, regions, stage))
        {
            throw new InvalidOperationException("This stage is still locked.");
        }

        var hand = order.Select(profile.Car).ToList();
        if (hand.Any(c => c is null) || !Hands.Fits(hand!, roster, HandLimit(profile, stage)))
        {
            throw new InvalidOperationException("This hand may not enter.");
        }

        var setup = Setup(stage, roster, tracks);
        var result = Matches.Play(setup, hand!, roster);
        var (cash, xp) = Progression.Pay(profile, result.PlayerWon, setup.Opponent.Average(c => roster.Rq(c.ModelId)));
        var newCars = new List<CarInstance>();
        var won = profile.Campaign.Won(stage);
        if (result.PlayerWon && won < stage.PrizeCarIds.Count)
        {
            newCars.Add(CarInstance.New(stage.PrizeCarIds[won]));
            profile.Campaign.PrizesWon[stage.Id] = won + 1;
        }

        profile.Garage.AddRange(newCars);
        profile.LastHand = [.. order];
        return new CampaignOutcome(result, newCars, cash, xp);
    }
}
