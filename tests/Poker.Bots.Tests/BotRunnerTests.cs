using PokerBots;

namespace Poker.Bots.Tests;

public class BotRunnerTests
{
    private sealed class Caller : IPokerBot
    {
        public string Name => "Caller";
        public List<string> Seen { get; } = [];

        public void OnMatchStart(MatchStart m) => Seen.Add("match-start");

        public BotAction Decide(Decision d) => BotAction.Call;
    }

    private sealed class Thrower : IPokerBot
    {
        public string Name => "Thrower";

        public BotAction Decide(Decision d) => throw new InvalidOperationException("boom");
    }

    private static string Input(params Message[] messages) => string.Concat(messages.Select(m => Protocol.Write(m) + "\n"));

    private static readonly MatchStart Start = new(0, 2, "fixed-stacks", 1000, 10, 20, 1000, 1);

    [Fact]
    public void TheRunner_AnswersHelloAndDecisions()
    {
        var bot = new Caller();
        var output = new StringWriter();
        var input = Input(new Hello(1), Start, ProtocolTests.SampleDecision(), new MatchEnd([]));

        var code = BotRunner.Run(bot, new StringReader(input), output, new StringWriter());

        code.Should().Be(0);
        output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            """{"type":"hello","protocol":1,"name":"Caller"}""",
            """{"type":"action","action":"call","id":7}""");
        bot.Seen.Should().Equal("match-start");
    }

    [Fact]
    public void TheRunner_IgnoresUnknownLines_AndStopsAtMatchEnd()
    {
        var output = new StringWriter();
        var input = "garbage\n{\"type\":\"later\"}\n\n" + Input(new MatchEnd([]), new Hello(1));

        BotRunner.Run(new Caller(), new StringReader(input), output, new StringWriter()).Should().Be(0);

        output.ToString().Should().BeEmpty("nothing after match-end is read");
    }

    [Fact]
    public void ABotThatThrows_SendsNothing_AndSaysWhy()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var input = Input(new Hello(1), ProtocolTests.SampleDecision());

        BotRunner.Run(new Thrower(), new StringReader(input), output, errors).Should().Be(0);

        output.ToString().Should().Be("""{"type":"hello","protocol":1,"name":"Thrower"}""" + "\n");
        errors.ToString().Should().Contain("boom");
    }
}
