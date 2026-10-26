using Jentl.Net;
using Poker.Arena;
using PokerBots;

namespace Poker.Tests;

public class BotHostTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private static readonly MatchStart Start = new(0, 2, "sit-and-go", 1000, 10, 20, 5000, 1);

    private static readonly Decision Decision = new(
        7, 1, "preflop", 0, ["As", "Kd"], [], 0, 0, 1, 10, 20, 1,
        [new TableSeat(0, "A", 1000, 10, "active"), new TableSeat(1, "B", 1000, 20, "active")],
        [new PokerBots.Pot(30, [0, 1])], 10, new LegalActions(true, false, Call: 10, MinRaiseTo: 40, MaxRaiseTo: 1000, AllIn: true), [], 5000);

    private static string SampleDll => Path.Combine(AppContext.BaseDirectory, "SampleBots", "Poker.SampleBots.dll");

    private static AgentCommand Host(string dll, params string[] arguments) =>
        new("dotnet", [Path.Combine(AppContext.BaseDirectory, "BotHost", "PokerBotHost.dll"), dll, .. arguments]);

    [Fact]
    public async Task TheHost_RunsACSharpBot()
    {
        using var seat = ProcessSeat.Start(Host(SampleDll, "GoodBot"), "Good");
        await seat.StartAsync(Start, Limit);
        seat.IsGone.Should().BeFalse();

        (await seat.AskAsync(Decision, TimeoutToken())).Should().Be(BotAction.Call with { Id = 7 });
    }

    [Fact]
    public async Task TheHost_RunsThroughItsOwnProgram_WithoutDotnet()
    {
        var command = AgentCommand.DotNet(Path.Combine(AppContext.BaseDirectory, "BotHost", "PokerBotHost.dll"), [SampleDll, "GoodBot"]);
        command.FileName.Should().NotBe("dotnet", "the host is built with its apphost");

        using var seat = ProcessSeat.Start(command, "Good");
        await seat.StartAsync(Start, Limit);
        seat.IsGone.Should().BeFalse();

        (await seat.AskAsync(Decision, TimeoutToken())).Should().Be(BotAction.Call with { Id = 7 });
    }

    [Fact]
    public async Task ABotThatPrintsPartialLines_StillAnswers()
    {
        using var seat = ProcessSeat.Start(Host(SampleDll, "ChattyBot"), "Chatty");
        await seat.StartAsync(Start, Limit);

        (await seat.AskAsync(Decision, TimeoutToken())).Should().Be(BotAction.Call with { Id = 7 });
        (await seat.AskAsync(Decision with { Id = 8 }, TimeoutToken())).Should().Be(BotAction.Call with { Id = 8 });
    }

    [Fact]
    public async Task TheHost_FindsABotByItsFullName()
    {
        using var seat = ProcessSeat.Start(Host(SampleDll, "Poker.SampleBots.GoodBot"), "Good");
        await seat.StartAsync(Start, Limit);

        (await seat.AskAsync(Decision, TimeoutToken())).Should().Be(BotAction.Call with { Id = 7 });
    }

    [Fact]
    public async Task ADllWithSeveralBots_NeedsAName()
    {
        using var seat = ProcessSeat.Start(Host(SampleDll), "Many");
        await seat.StartAsync(Start, Limit);

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().Contain("has 5 bots: ChattyBot, ExitingBot, GoodBot, SlowBot, ThrowingBot; name one");
    }

    [Fact]
    public async Task AnUnknownBotName_IsGone_WithItsProblem()
    {
        using var seat = ProcessSeat.Start(Host(SampleDll, "NoSuchBot"), "Nobody");
        await seat.StartAsync(Start, Limit);

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().Contain("no bot named NoSuchBot");
    }

    [Fact]
    public async Task ADllWithoutABot_IsGone_WithItsProblem()
    {
        // The SDK itself holds no bot.
        using var seat = ProcessSeat.Start(Host(Path.Combine(AppContext.BaseDirectory, "BotHost", "Poker.Bots.dll")), "Empty");
        await seat.StartAsync(Start, Limit);

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().Contain("Poker.Bots.dll has no bot");
    }

    [Fact]
    public async Task AMissingDll_IsGone_WithItsProblem()
    {
        using var seat = ProcessSeat.Start(Host(Path.Combine(Path.GetTempPath(), "no-such-bot.dll")), "Missing");
        await seat.StartAsync(Start, Limit);

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().Contain("exited with code 2").And.Contain("no-such-bot.dll");
    }

    [Fact]
    public async Task ARebuiltDll_CanBeReplacedWhileItsBotRuns()
    {
        var folder = Directory.CreateTempSubdirectory("bothost").FullName;
        try
        {
            var dll = Path.Combine(folder, "Mine.dll");
            File.Copy(SampleDll, dll);
            using var seat = ProcessSeat.Start(Host(dll, "GoodBot"), "Good");
            await seat.StartAsync(Start, Limit);
            (await seat.AskAsync(Decision, TimeoutToken())).Should().Be(BotAction.Call with { Id = 7 });

            var rebuild = () => File.Copy(SampleDll, dll, overwrite: true);

            rebuild.Should().NotThrow();
            seat.IsGone.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AThrowingBot_DoesNotStopTheHost()
    {
        using var seat = ProcessSeat.Start(Host(SampleDll, "ThrowingBot"), "Thrower");
        await seat.StartAsync(Start, Limit);
        using var shortWait = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        (await seat.AskAsync(Decision, shortWait.Token)).Should().BeNull();

        seat.IsGone.Should().BeFalse();
    }

    [Fact]
    public async Task AnExitingBot_EndsOnlyItsOwnProcess()
    {
        using var seat = ProcessSeat.Start(Host(SampleDll, "ExitingBot"), "Quitter");
        await seat.StartAsync(Start, Limit);

        var ask = () => seat.AskAsync(Decision, TimeoutToken());

        await ask.Should().ThrowAsync<IOException>().WithMessage("exited with code 4*");
        seat.IsGone.Should().BeTrue();
    }

    private static CancellationToken TimeoutToken() => new CancellationTokenSource(Limit).Token;
}
