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
    [InlineData("Random")]
    [InlineData("always call")]
    [InlineData("TIGHT")]
    public void AHouseBotsName_IsTaken(string name)
    {
        var owner = _registration.NewOwner("Coen", null).Value;

        _registration.NewBot(owner.Token, name, null).Refusal.Should().Be("name taken");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("a name that is far too long")]
    [InlineData(" Shark")]
    [InlineData("Shark!")]
    [InlineData("Shark\n")]
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
        _registration.NewBot(owner.Token, "Other", null).Refusal.Should().Be("banned");
    }

    [Fact]
    public void ABannedOwner_GetsBanned_NotUnknownOwner()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        var bot = _registration.NewBot(owner.Token, "Shark", null).Value;
        _registry.Ban(owner.Account.Id);

        _registration.NewBot(owner.Token, "Other", null).Refusal.Should().Be("banned");
        _registration.RenewBotToken(owner.Token, "Shark").Refusal.Should().Be("banned");
        _registration.RenewBotToken("nonsense", "Shark").Refusal.Should().Be("unknown owner");
        _registration.NewBotToken(owner.Token, "Shark").Should().BeNull();
        bot.Account.Name.Should().Be("Shark");
    }

    [Fact]
    public void ABannedBot_AskingForANewToken_GetsBotBanned()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        _registration.NewBot(owner.Token, "Shark", null);
        _registry.Ban("Shark");

        _registration.RenewBotToken(owner.Token, "Shark").Refusal.Should().Be("bot banned", "the bot is banned, not its owner");
    }

    [Fact]
    public async Task ABan_IsNotUndoneByARacingNewToken()
    {
        for (var i = 0; i < 200; i++)
        {
            var owner = _registration.NewOwner($"Owner {i}", null).Value;
            var name = $"Bot {i}";
            _registration.NewBot(owner.Token, name, null);

            var renewing = Task.Run(() =>
            {
                for (var n = 0; n < 50; n++)
                {
                    _registration.NewBotToken(owner.Token, name);
                }
            });
            var banning = Task.Run(() => _registry.Ban(name));
            await Task.WhenAll(renewing, banning);

            _registry.Bot(name)!.Banned.Should().BeTrue();
            new Registry(new PokerData(_folder), _clock).Bot(name)!.Banned.Should().BeTrue();
        }
    }

    [Fact]
    public void ABannedOwner_BansTheirBotsToo_AlsoAfterARestart()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        _registration.NewBot(owner.Token, "Shark", null);
        _registration.NewBot(owner.Token, "Tiger", null);

        _registry.Ban(owner.Account.Id);

        _registry.Bot("Shark")!.Banned.Should().BeTrue();
        _registry.BotsOf(owner.Account.Id).Should().OnlyContain(b => b.Banned);
        var restarted = new Registry(new PokerData(_folder), _clock);
        restarted.Bot("Tiger")!.Banned.Should().BeTrue();
        restarted.BotsOf(owner.Account.Id).Should().OnlyContain(b => b.Banned);
    }

    [Fact]
    public void ABrokenFile_IsMovedAside_NotOverwritten()
    {
        var owner = _registration.NewOwner("Coen", null).Value;
        _registration.NewBot(owner.Token, "Shark", null);
        File.WriteAllText(Path.Combine(_folder, "bots.json"), "{ this is not json");

        var data = new PokerData(_folder);
        var restarted = new Registry(data, _clock);
        new Registration(restarted, new RegistrationLimits(), _clock).NewBot(owner.Token, "Tiger", null);

        data.Problems.Should().ContainSingle().Which.Should().Contain("bots.json");

        var aside = Directory.GetFiles(_folder, "bots.json.broken-*");
        aside.Should().ContainSingle();
        File.ReadAllText(aside[0]).Should().Be("{ this is not json");
        restarted.Bot("Tiger").Should().NotBeNull();
    }

    /// <summary>A clock that stands still until told to move.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
