using Jentl.Net;
using Poker.Arena;
using PokerBots;

namespace Poker.Tests;

public class SeatTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private static readonly MatchStart Start = new(0, 2, "sit-and-go", 1000, 10, 20, 5000, 1);

    private static readonly Decision Decision = new(
        7, 1, "preflop", 0, ["As", "Kd"], [], 0, 0, 1, 10, 20, 1,
        [new TableSeat(0, "A", 1000, 10, "active"), new TableSeat(1, "B", 1000, 20, "active")],
        [new PokerBots.Pot(30, [0, 1])], 10, new LegalActions(true, false, Call: 10, MinRaiseTo: 40, MaxRaiseTo: 1000, AllIn: true), [], 5000);

    private static AgentCommand EchoBot(params string[] arguments) =>
        new("dotnet", [Path.Combine(AppContext.BaseDirectory, "EchoBot", "Poker.Tests.EchoBot.dll"), .. arguments]);

    [Fact]
    public async Task AProcessSeat_PlaysThroughTheProtocol()
    {
        using var seat = new ProcessSeat(EchoBot(), "Echo");
        await seat.StartAsync(Start, Limit);
        seat.IsGone.Should().BeFalse();

        var action = await seat.AskAsync(Decision, TimeoutToken());

        action.Should().Be(BotAction.Call with { Id = 7 });
    }

    [Fact]
    public async Task DebugPrints_AreNotAnswers()
    {
        using var seat = new ProcessSeat(EchoBot("chatty"), "Echo");
        await seat.StartAsync(Start, Limit);

        var action = await seat.AskAsync(Decision, TimeoutToken());

        action.Should().Be(BotAction.Call with { Id = 7 });
    }

    [Fact]
    public async Task ABotThatCannotStart_IsGone_WithItsProblem()
    {
        using var seat = new ProcessSeat(new AgentCommand("no-such-program", []), "Ghost");
        await seat.StartAsync(Start, Limit);

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().StartWith("could not start");
        var ask = () => seat.AskAsync(Decision, TimeoutToken());
        await ask.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task ACancelledAsk_ReturnsNullWithoutWaiting()
    {
        using var seat = new ProcessSeat(EchoBot(), "Echo");
        await seat.StartAsync(Start, Limit);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        (await seat.AskAsync(Decision, cancelled.Token)).Should().BeNull();
    }

    [Fact]
    public async Task AHouseSeat_AnswersFromItsBot()
    {
        using var seat = new HouseSeat(new CallingBot());
        await seat.StartAsync(Start, Limit);

        seat.Name.Should().Be("Always call");
        seat.IsGone.Should().BeFalse();
        (await seat.AskAsync(Decision, CancellationToken.None)).Should().Be(BotAction.Call);
    }

    [Fact]
    public async Task AStaleAnswer_IsSkipped_AndTheRightOneTaken()
    {
        using var seat = new ProcessSeat(EchoBot("stale"), "Echo");
        await seat.StartAsync(Start, Limit);

        (await seat.AskAsync(Decision, TimeoutToken())).Should().Be(BotAction.Call with { Id = 7 });
    }

    [Fact]
    public async Task AWrongProtocolMajor_IsGone_WithItsProblem()
    {
        using var seat = new ProcessSeat(EchoBot("protocol2"), "Old");
        await seat.StartAsync(Start, Limit);

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().Be("speaks protocol 2, the app speaks 1");
    }

    [Fact]
    public async Task ACancelledSlowAsk_DoesNotTakeTheNextAnswer()
    {
        using var seat = new ProcessSeat(EchoBot("slow:700"), "Echo");
        await seat.StartAsync(Start, Limit);
        using var first = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var abandoned = seat.AskAsync(Decision with { Id = 1 }, first.Token);

        (await abandoned).Should().BeNull();
        var second = await seat.AskAsync(Decision with { Id = 2 }, TimeoutToken());

        second.Should().Be(BotAction.Call with { Id = 2 });
    }

    [Fact]
    public async Task AHouseBotThatThrows_IsGone_WithItsProblem()
    {
        using var seat = new HouseSeat(new ThrowingBot());
        await seat.StartAsync(Start, Limit);
        seat.Notify(new MatchEnd([]));
        seat.IsGone.Should().BeFalse();

        var first = () => seat.AskAsync(Decision, CancellationToken.None);
        await first.Should().ThrowAsync<IOException>();

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().Contain("threw").And.Contain("boom");
        var again = () => seat.AskAsync(Decision, CancellationToken.None);
        await again.Should().ThrowAsync<IOException>();
    }

    private sealed class ThrowingBot : IPokerBot
    {
        public string Name => "Thrower";

        public void OnMatchStart(MatchStart m) => throw new InvalidOperationException("hook");

        public void OnMatchEnd(MatchEnd m) => throw new InvalidOperationException("hook");

        public BotAction Decide(Decision d) => throw new InvalidOperationException("boom");
    }

    private static CancellationToken TimeoutToken() => new CancellationTokenSource(Limit).Token;
}
