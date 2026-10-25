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
    public void AHouseSeat_CanBeGivenAName()
    {
        using var seat = new HouseSeat(new CallingBot(), "Always call 2");

        seat.Name.Should().Be("Always call 2");
    }

    [Fact]
    public void SeatNames_NumberTheRepeats_AndKeepClearOfTheNamesGiven()
    {
        SeatNames.Distinct(["Tight", "Balanced", "Tight", "Tight"]).Should().Equal("Tight", "Balanced", "Tight 2", "Tight 3");
        SeatNames.Distinct(["Tight", "Tight", "Tight 2"]).Should().Equal("Tight", "Tight 3", "Tight 2");
        SeatNames.Distinct(["A", "B"]).Should().Equal("A", "B");
    }

    [Fact]
    public void SeatNames_Original_GivesTheNameASeatWasNumberedFrom()
    {
        string[] bots = ["Tight", "Balanced", "Echo 2"];

        SeatNames.Original("Tight", bots).Should().Be("Tight");
        SeatNames.Original("Tight 3", bots).Should().Be("Tight");
        SeatNames.Original("Echo 2", bots).Should().Be("Echo 2", "a name that is a bot's own is kept");
        SeatNames.Original("Loose", bots).Should().BeNull();
        SeatNames.Original("Loose 2", bots).Should().BeNull();
        SeatNames.Original("Tight x", bots).Should().BeNull();

        string[] seated = ["Tight", "Balanced", "Tight", "Tight"];
        SeatNames.Distinct(seated).Select(n => SeatNames.Original(n, seated)).Should().Equal(seated);
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
    public async Task ABotThatDoesNotAnswerThreeTimesInARow_IsStopped()
    {
        using var seat = new ProcessSeat(EchoBot("skip:1,2,3"), "Echo");
        await seat.StartAsync(Start, Limit);

        for (var id = 1; id <= 3; id++)
        {
            seat.IsGone.Should().BeFalse();
            (await seat.AskAsync(Decision with { Id = id }, ShortToken())).Should().BeNull();
        }

        seat.IsGone.Should().BeTrue();
        seat.Problem.Should().Be("stopped: did not answer 3 times in a row");
        var ask = () => seat.AskAsync(Decision with { Id = 4 }, TimeoutToken());
        await ask.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task AnAnswerInTime_StartsTheCountAgain()
    {
        using var seat = new ProcessSeat(EchoBot("skip:1,2,4,5"), "Echo");
        await seat.StartAsync(Start, Limit);

        var answers = new List<BotAction?>();
        for (var id = 1; id <= 5; id++)
        {
            answers.Add(await seat.AskAsync(Decision with { Id = id }, id == 3 ? TimeoutToken() : ShortToken()));
        }

        answers.Should().Equal(null, null, BotAction.Call with { Id = 3 }, null, null);
        seat.IsGone.Should().BeFalse();
        seat.Problem.Should().BeNull();
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

    // Long enough for an answer from a bot that is up, short enough to wait for three that never come.
    private static CancellationToken ShortToken() => new CancellationTokenSource(TimeSpan.FromMilliseconds(400)).Token;
}
