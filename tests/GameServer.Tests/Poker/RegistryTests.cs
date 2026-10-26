using GameServer.Poker;

namespace GameServer.Tests.Poker;

/// <summary>Owners, bots and their tokens, over a temporary data folder.</summary>
public sealed class RegistryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "poker-registry-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private readonly Registry _registry;
    private readonly Registration _registration;

    public RegistryTests()
    {
        _registry = new Registry(new PokerData(_folder), _clock);
        _registration = new Registration(_registry, new RegistrationLimits(), _clock);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ABot_IsFoundByItsToken_AndTheTokenIsNotStored()
    {
        var owner = _registration.NewOwner("Coen", "1.2.3.4").Value;
        var bot = _registration.NewBot(owner.Token, "Shark", "1.2.3.4").Value;

        _registry.BotOfToken(bot.Token)!.Name.Should().Be("Shark");
        _registry.OwnerOfToken(owner.Token)!.Id.Should().Be(bot.Account.OwnerId);
        File.ReadAllText(Path.Combine(_folder, "bots.json")).Should().NotContain(bot.Token);
        File.ReadAllText(Path.Combine(_folder, "owners.json")).Should().NotContain(owner.Token);
    }

    [Fact]
    public void ANewToken_ReplacesTheOld()
    {
        var owner = _registration.NewOwner("Coen", "1.2.3.4").Value;
        var bot = _registration.NewBot(owner.Token, "Shark", "1.2.3.4").Value;

        var fresh = _registration.NewBotToken(owner.Token, "Shark");

        fresh.Should().NotBeNull();
        _registry.BotOfToken(bot.Token).Should().BeNull();
        _registry.BotOfToken(fresh!)!.Name.Should().Be("Shark");
    }

    [Fact]
    public void ANewToken_IsOnlyForTheBotsOwner()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        var other = _registration.NewOwner("Anna", null).Value;
        _registration.NewBot(owner.Token, "Shark", null);

        _registration.NewBotToken(other.Token, "Shark").Should().BeNull();
    }

    [Fact]
    public void ASixthBot_IsRefused()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        for (var i = 1; i <= 5; i++)
        {
            _registration.NewBot(owner.Token, $"Bot {i}", null).Ok.Should().BeTrue();
        }

        var sixth = _registration.NewBot(owner.Token, "Bot 6", null);

        sixth.Ok.Should().BeFalse();
        sixth.Refusal.Should().Be("5 bots already");
    }

    [Fact]
    public void NamesAreUnique_IgnoringCase()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        var other = _registration.NewOwner("Anna", null).Value;
        _registration.NewBot(owner.Token, "Shark", null).Ok.Should().BeTrue();

        _registration.NewBot(other.Token, "shark", null).Refusal.Should().Be("name taken");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("a name that is far too long")]
    [InlineData(" Shark")]
    [InlineData("Shark!")]
    public void BadNames_AreRefused(string name)
    {
        var owner = _registration.NewOwner("Coen", null).Value;

        _registration.NewBot(owner.Token, name, null).Refusal.Should().Be("not a name");
        _registration.NewOwner(name, null).Refusal.Should().Be("not a name");
    }

    [Fact]
    public void AnUnknownOwner_IsRefused() =>
        _registration.NewBot("nonsense", "Shark", null).Refusal.Should().Be("unknown owner");

    [Fact]
    public void TooManyRegistrationsFromOneAddress_AreRefused()
    {
        for (var i = 1; i <= 10; i++)
        {
            _registration.NewOwner($"Owner {i}", "1.2.3.4").Ok.Should().BeTrue();
        }

        _registration.NewOwner("Owner 11", "1.2.3.4").Refusal.Should().Be("too many registrations today");
        _registration.NewOwner("Owner 11", "5.6.7.8").Ok.Should().BeTrue();

        _clock.Advance(TimeSpan.FromDays(1));

        _registration.NewOwner("Owner 11", "1.2.3.4").Ok.Should().BeTrue();
    }

    [Fact]
    public void ARestart_KeepsOwnersAndBots()
    {
        var owner = _registration.NewOwner("Coen", "1.2.3.4").Value;
        var bot = _registration.NewBot(owner.Token, "Shark", "1.2.3.4").Value;

        var restarted = new Registry(new PokerData(_folder), _clock);

        restarted.BotOfToken(bot.Token)!.Name.Should().Be("Shark");
        restarted.OwnerOfToken(owner.Token)!.Name.Should().Be("Coen");
        restarted.BotsOf(owner.Account.Id).Select(b => b.Name).Should().Equal("Shark");
    }

    [Fact]
    public void ABannedBot_IsNotFoundByItsToken()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        var bot = _registration.NewBot(owner.Token, "Shark", null).Value;

        _registry.Ban("Shark");

        _registry.BotOfToken(bot.Token).Should().BeNull();
        _registry.Bot("Shark")!.Banned.Should().BeTrue();
        _registration.NewBotToken(owner.Token, "Shark").Should().BeNull();
    }

    [Fact]
    public void ABannedOwner_AndTheirBots_AreNotFoundByToken()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        var bot = _registration.NewBot(owner.Token, "Shark", null).Value;

        _registry.Ban(owner.Account.Id);

        _registry.OwnerOfToken(owner.Token).Should().BeNull();
        _registry.BotOfToken(bot.Token).Should().BeNull();
        _registration.NewBot(owner.Token, "Other", null).Refusal.Should().Be("unknown owner");
    }

    /// <summary>A clock that stands still until told to move.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
