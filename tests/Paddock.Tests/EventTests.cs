namespace Paddock.Tests;

public class EventTests
{
    private static readonly Roster Cars = Roster.FromJson(GameData.Read("cars.json"));
    private static readonly IReadOnlyList<TrackDef> Tracks = GameData.Tracks;
    private static readonly IReadOnlyList<EventDef> Templates = EventDef.ListFromJson(GameData.Read("events.json"));
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static LiveEvent Open(string id = "classic-cup", EntryRules? rules = null)
    {
        var def = Templates.Single(t => t.Id == id);
        return new LiveEvent(rules is null ? def : def with { Rules = rules }, $"{id}@test", Now.AddDays(1));
    }

    private static Profile RichProfile()
    {
        var profile = Profile.New(Cars);
        profile.Garage.AddRange(Cars.All.Select(c => CarInstance.New(c.Id)));
        return profile;
    }

    [Fact]
    public void Calendar_ThreeLive_AllDifferent()
    {
        var live = new EventCalendar(Templates).Live(Now);

        live.Should().HaveCount(3);
        live.Select(e => e.Def.Id).Should().OnlyHaveUniqueItems();
        live.Should().OnlyContain(e => e.Ends > Now && e.Ends <= Now.AddDays(3));
    }

    [Fact]
    public void Calendar_OneEventChangesPerDay()
    {
        var calendar = new EventCalendar(Templates);
        var today = calendar.Live(Now).Select(e => e.InstanceId).ToHashSet();
        var tomorrow = calendar.Live(Now.AddDays(1)).Select(e => e.InstanceId).ToHashSet();

        today.Intersect(tomorrow).Should().HaveCount(2);
    }

    [Fact]
    public void Calendar_SameDateSameEvents() =>
        new EventCalendar(Templates).Live(Now).Should().Equal(new EventCalendar(Templates).Live(Now.AddMinutes(5)));

    [Fact]
    public void Rules_Filter()
    {
        var rules = new EntryRules(HandRqLimit: 200, MaxCarRq: 45, YearFrom: 1960, YearTo: 1979, Tyres: [Tyres.Standard], Bodies: [Body.Saloon, Body.Hatchback]);
        var ok = Cars.Find("brenner-1600-tl")!;
        var tooNew = Cars.Find("halvard-760-turbo")!;
        var wrongBody = Cars.Find("kestrel-swallow")!;

        rules.Allows(ok, Cars.Rq(ok.Id)).Should().BeTrue();
        rules.Allows(tooNew, Cars.Rq(tooNew.Id)).Should().BeFalse();
        rules.Allows(wrongBody, Cars.Rq(wrongBody.Id)).Should().BeFalse();
        rules.Allows(ok, 46).Should().BeFalse();
        (rules with { Classes = [Rarity.S] }).Allows(ok, Cars.Rq(ok.Id)).Should().BeFalse();
        (rules with { Countries = ["Sweden"] }).Allows(ok, Cars.Rq(ok.Id)).Should().BeFalse();
    }

    [Fact]
    public void Rules_Describe_ListsEveryRule()
    {
        var rules = new EntryRules(HandRqLimit: 150, YearFrom: 1960, YearTo: 1979, Tyres: [Tyres.OffRoad]);

        rules.Describe(s => s).Should().Equal("Hand RQ up to 150", "Cars from 1960 to 1979", "Tyres: OffRoad");
    }

    [Fact]
    public void CanEnter_NeedsTicketAndFiveCars()
    {
        var ev = Open();
        var poor = Profile.New(Cars);
        poor.Garage.Clear();

        EventPlay.CanEnter(poor, ev, Cars, Now, out var why).Should().BeFalse();
        why.Should().Be("Not enough cars that may enter");

        var profile = RichProfile();
        EventPlay.CanEnter(profile, ev, Cars, Now, out why).Should().BeTrue();
        why.Should().BeNull();

        profile.Events[ev.InstanceId] = new EventProgress(0, new Jentl.Collect.RechargeState(0, Now), false);
        EventPlay.CanEnter(profile, ev, Cars, Now, out why).Should().BeFalse();
        why.Should().Be("No tickets left");
    }

    [Fact]
    public void Next_OpponentFitsRules_AndIsDeterministic()
    {
        var ev = Open();

        for (var rung = 0; rung < EventPlay.Rungs; rung++)
        {
            var setup = EventPlay.Next(ev, rung, Cars, Tracks);
            setup.Rounds.Should().HaveCount(5);
            setup.Rounds.Should().OnlyContain(r => ev.Def.TrackIds.Contains(r.Track.Id) && r.Track.Conditions.Contains(r.Condition));
            Hands.Fits(setup.Opponent, Cars, ev.Def.Rules.HandRqLimit, m => ev.Def.Rules.Allows(m, Cars.Rq(m.Id))).Should().BeTrue();
            EventPlay.Next(ev, rung, Cars, Tracks).Should().BeEquivalentTo(setup);
        }
    }

    [Fact]
    public void Next_GetsStrongerUpTheLadder()
    {
        var ev = Open();
        int Total(int rung) => EventPlay.Next(ev, rung, Cars, Tracks).Opponent.Sum(c => Cars.Rq(c.ModelId));

        Total(EventPlay.Rungs - 1).Should().BeGreaterThan(Total(0));
    }

    [Fact]
    public void Race_SpendsTicket_WinOrLose()
    {
        var ev = Open();
        var profile = RichProfile();
        var hand = BestHand(profile, ev);

        EventPlay.Race(profile, ev, hand, Cars, Tracks, Now, new Random(1));

        EventPlay.Progress(profile, ev, Now).Tickets.Count.Should().Be(4);
        profile.LastHand.Should().Equal(hand);
    }

    [Fact]
    public void Race_WinAdvances_Rung12GivesPrizeOnce()
    {
        var ev = Open();
        var profile = RichProfile();
        profile.Events[ev.InstanceId] = new EventProgress(EventPlay.Rungs - 1, new Jentl.Collect.RechargeState(5, Now), false);
        var before = profile.Garage.Count;

        var outcome = WinOnce(profile, ev);

        EventPlay.Progress(profile, ev, Now).Rung.Should().Be(EventPlay.Rungs);
        outcome.NewCars.Should().ContainSingle(c => c.ModelId == ev.Def.PrizeCarId);
        profile.Garage.Count.Should().Be(before + Hands.Size + 1); // the helper's five cars and the prize
        profile.Garage.Should().Contain(outcome.NewCars);
        EventPlay.CanEnter(profile, ev, Cars, Now, out var why).Should().BeFalse();
        why.Should().Be("Event finished");
    }

    [Fact]
    public void Race_Rung4GivesAPack()
    {
        var ev = Open();
        var profile = RichProfile();
        profile.Events[ev.InstanceId] = new EventProgress(3, new Jentl.Collect.RechargeState(5, Now), false);

        WinOnce(profile, ev).NewCars.Should().HaveCount(Packs.Starter.Cars);
    }

    [Fact]
    public void Race_IllegalHand_Throws_SpendsNothing()
    {
        var ev = Open();
        var profile = RichProfile();
        var illegal = profile.Garage.Take(4).Select(c => c.Id).ToList();

        var race = () => EventPlay.Race(profile, ev, illegal, Cars, Tracks, Now, new Random(1));

        race.Should().Throw<InvalidOperationException>();
        EventPlay.Progress(profile, ev, Now).Tickets.Count.Should().Be(5);
    }

    [Fact]
    public void Tickets_RechargeWhileClosed()
    {
        var ev = Open();
        var profile = RichProfile();
        var hand = BestHand(profile, ev);
        for (var i = 0; i < 3; i++)
        {
            EventPlay.Race(profile, ev, hand, Cars, Tracks, Now, new Random(i));
        }

        var loaded = Profile.FromJson(profile.ToJson(), Cars);

        EventPlay.Progress(loaded, ev, Now).Tickets.Count.Should().Be(2);
        EventPlay.Progress(loaded, ev, Now.AddHours(1)).Tickets.Count.Should().Be(4);
    }

    [Fact]
    public void EventsJson_IsValid()
    {
        Templates.Should().HaveCount(12);
        Templates.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        foreach (var t in Templates)
        {
            Cars.Find(t.PrizeCarId).Should().NotBeNull(t.Id);
            t.TrackIds.Should().OnlyContain(id => Tracks.Any(tr => tr.Id == id), t.Id);
            var allowed = Cars.All.Where(m => t.Rules.Allows(m, Cars.Rq(m.Id))).Select(m => Cars.Rq(m.Id)).Order().ToList();
            allowed.Count.Should().BeGreaterThanOrEqualTo(8, t.Id);
            allowed.Take(5).Sum().Should().BeLessThanOrEqualTo(t.Rules.HandRqLimit, t.Id);
        }
    }

    // The five allowed cars with the best stats that fit the limit, cheapest-first fallback.
    private static List<Guid> BestHand(Profile profile, LiveEvent ev) =>
        [.. profile.Garage.Where(c => ev.Def.Rules.Allows(Cars.Find(c.ModelId)!, Cars.Rq(c.ModelId)))
            .OrderBy(c => Cars.Rq(c.ModelId)).Take(5).Select(c => c.Id)];

    // Wins the next match with fully upgraded copies of the strongest allowed cars that fit.
    private static EventOutcome WinOnce(Profile profile, LiveEvent ev)
    {
        var allowed = Cars.All.Where(m => ev.Def.Rules.Allows(m, Cars.Rq(m.Id))).OrderByDescending(m => Cars.Rq(m.Id)).ToList();
        var hand = new List<CarInstance>();
        var budget = ev.Def.Rules.HandRqLimit;
        foreach (var m in allowed)
        {
            var cheapestRest = allowed.Select(a => Cars.Rq(a.Id)).Order().Take(4 - hand.Count).Sum();
            if (hand.Count < 5 && Cars.Rq(m.Id) + cheapestRest <= budget)
            {
                hand.Add(CarInstance.New(m.Id) with { Stage = 3, Tune = new Tune(9, 9, 6) });
                budget -= Cars.Rq(m.Id);
            }
        }

        profile.Garage.AddRange(hand);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var order = hand.OrderBy(_ => attempt % 7).ThenBy(c => (c.Id.GetHashCode() * (attempt + 1)) % 97).Select(c => c.Id).ToList();
            var rung = EventPlay.Progress(profile, ev, Now).Rung;
            profile.Events[ev.InstanceId] = EventPlay.Progress(profile, ev, Now) with { Tickets = new Jentl.Collect.RechargeState(5, Now) };
            var outcome = EventPlay.Race(profile, ev, order, Cars, Tracks, Now, new Random(attempt));
            if (outcome.Result.PlayerWon)
            {
                return outcome;
            }

            EventPlay.Progress(profile, ev, Now).Rung.Should().Be(rung);
        }

        throw new InvalidOperationException("Could not win with the strongest cars.");
    }
}

public class RulesWordingTests
{
    [Fact]
    public void Describe_NamesValuesThroughTheNamer()
    {
        var rules = new EntryRules(HandRqLimit: 150, Tyres: [Tyres.AllSurface, Tyres.OffRoad], Bodies: [Body.Suv]);

        rules.Describe(s => s, value => value is Tyres.AllSurface ? "All-surface" : value is Tyres.OffRoad ? "Off-road" : value.ToString()!)
            .Should().Equal("Hand RQ up to 150", "Tyres: All-surface, Off-road", "Bodies: Suv");
    }
}
