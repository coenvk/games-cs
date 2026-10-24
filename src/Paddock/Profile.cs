namespace Paddock;

/// <summary>The player's progress: cash, level, garage and the last hand raced. Saved as JSON.</summary>
public sealed class Profile
{
    /// <summary>Cash to spend on upgrades and packs.</summary>
    public long Cash { get; set; }

    /// <summary>Experience towards the next level.</summary>
    public int Xp { get; set; }

    /// <summary>The player's level; the hand's RQ limit grows with it.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Every car the player owns.</summary>
    public List<CarInstance> Garage { get; set; } = [];

    /// <summary>The cars of the last hand raced, in order, to start the next hand from.</summary>
    public List<Guid> LastHand { get; set; } = [];

    /// <summary>Progress per run of an event (by <see cref="LiveEvent.InstanceId"/>).</summary>
    public Dictionary<string, EventProgress> Events { get; set; } = [];

    /// <summary>Campaign progress.</summary>
    public CampaignProgress Campaign { get; set; } = new();

    /// <summary>A new player's profile: 5 000 cash and eight starter cars of classes F and E.</summary>
    public static Profile New(Roster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var f = roster.All.Where(c => roster.Class(c.Id) == Rarity.F).Take(6);
        var e = roster.All.Where(c => roster.Class(c.Id) == Rarity.E).Take(2);
        return new Profile { Cash = Balance.StartingCash, Garage = [.. f.Concat(e).Select(c => CarInstance.New(c.Id))] };
    }

    /// <summary>The car with this id, or null.</summary>
    public CarInstance? Car(Guid id) => Garage.Find(c => c.Id == id);

    /// <summary>Replaces the car with the same id.</summary>
    public void Replace(CarInstance car)
    {
        ArgumentNullException.ThrowIfNull(car);
        Garage[Garage.FindIndex(c => c.Id == car.Id)] = car;
    }

    /// <summary>The profile as JSON.</summary>
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, PaddockJson.Default.Profile);

    /// <summary>Reads a saved profile: cars whose model is no longer in the roster are dropped, missing fields get defaults.</summary>
    public static Profile FromJson(string json, Roster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var profile = System.Text.Json.JsonSerializer.Deserialize(json, PaddockJson.Default.Profile) ?? new Profile();
        profile.Garage = [.. (profile.Garage ?? []).Where(c => c is not null && roster.Find(c.ModelId) is not null)];
        var owned = profile.Garage.Select(c => c.Id).ToHashSet();
        profile.LastHand = [.. (profile.LastHand ?? []).Where(owned.Contains)];
        profile.Level = Math.Max(1, profile.Level);
        profile.Events ??= [];
        profile.Campaign ??= new();
        profile.Campaign.PrizesWon ??= [];
        return profile;
    }
}

/// <summary>Levels, the RQ limit and race payouts.</summary>
public static class Progression
{
    /// <summary>The hand's RQ limit at a level: 100, plus 20 a level, at most 500.</summary>
    public static int RqLimit(int level) => Math.Min(Balance.MaxRqLimit, Balance.FirstRqLimit + (Balance.RqLimitPerLevel * (level - 1)));

    /// <summary>The experience needed to go from <paramref name="level"/> to the next.</summary>
    public static int XpFor(int level) => Balance.XpPerLevel * level;

    /// <summary>Adds experience, levelling up as often as it allows.</summary>
    public static void AddXp(Profile profile, int xp)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Xp += xp;
        while (profile.Xp >= XpFor(profile.Level))
        {
            profile.Xp -= XpFor(profile.Level);
            profile.Level++;
        }
    }

    /// <summary>Cash and experience for a match: more for a win, and more against stronger cars.</summary>
    public static (long Cash, int Xp) Payout(bool won, double opponentAverageRq)
    {
        var cash = Balance.WinCash * (1 + (opponentAverageRq / 20));
        return won ? ((long)Math.Round(cash), Balance.WinXp) : ((long)Math.Round(cash / 3), Balance.LossXp);
    }

    /// <summary>Pays a match's cash and experience into the profile.</summary>
    public static (long Cash, int Xp) Pay(Profile profile, bool won, double opponentAverageRq)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var payout = Payout(won, opponentAverageRq);
        profile.Cash += payout.Cash;
        AddXp(profile, payout.Xp);
        return payout;
    }
}

/// <summary>Spending cash on the garage's cars.</summary>
public static class Workshop
{
    /// <summary>Buys an upgrade point if it fits and the player can pay.</summary>
    public static bool Upgrade(Profile profile, Guid carId, Area area, Roster roster)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(roster);
        if (profile.Car(carId) is not { } car || !Upgrades.CanUpgrade(car, area))
        {
            return false;
        }

        var cost = Upgrades.Cost(car, area, roster.Class(car.ModelId));
        if (profile.Cash < cost)
        {
            return false;
        }

        profile.Cash -= cost;
        profile.Replace(Upgrades.Upgrade(car, area));
        return true;
    }

    /// <summary>Fuses <paramref name="donorId"/> into <paramref name="targetId"/> if allowed; the donor leaves the garage (and the last hand).</summary>
    public static bool Fuse(Profile profile, Guid targetId, Guid donorId, Roster roster)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(roster);
        if (profile.Car(targetId) is not { } target || profile.Car(donorId) is not { } donor
            || !Upgrades.CanFuse(target, donor, roster.Class(target.ModelId), roster.Class(donor.ModelId)))
        {
            return false;
        }

        profile.Garage.Remove(donor);
        profile.LastHand.Remove(donorId);
        profile.Replace(Upgrades.Fuse(target));
        return true;
    }

    /// <summary>Takes the car's upgrade points back (to be bought again); the stage stays.</summary>
    public static bool Reset(Profile profile, Guid carId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Car(carId) is not { } car)
        {
            return false;
        }

        profile.Replace(Upgrades.Reset(car));
        return true;
    }
}
