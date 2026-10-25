using PokerBots;

namespace Poker.Bots.Tests;

public class ProtocolTests
{
    internal static Decision SampleDecision() => new(
        Id: 7, Hand: 3, Street: "flop", You: 1, Hole: ["As", "Td"], Board: ["2c", "7h", "Jd"],
        Button: 0, SmallBlindSeat: 1, BigBlindSeat: 2, SmallBlind: 10, BigBlind: 20, Level: 1,
        Seats: [new TableSeat(0, "Ann", 980, 0, "active"), new TableSeat(1, "Bot", 990, 0, "active")],
        Pots: [new Pot(60, [0, 1])], ToCall: 20,
        Legal: new LegalActions(Fold: true, Check: false, Call: 20, MinRaiseTo: 40, MaxRaiseTo: 990, AllIn: true),
        Actions: [new PlayedAction("preflop", 0, "call", 20, 20)], TimeLimitMs: 1000);

    public static TheoryData<Message> Messages() => new()
    {
        new Hello(1),
        new Hello(1, "Caller"),
        new MatchStart(2, 6, "sit-and-go", 1000, 10, 20, 1000, 123456789012L),
        new HandStart(1, 0, 10, 20, [new HandSeat(0, "Ann", 1000, "active"), new HandSeat(1, "Bob", 0, "out")]),
        SampleDecision(),
        new HandEnd(
            3, ["2c", "7h", "Jd", "Qs", "3s"], [new PlayedAction("river", 0, "check", 0, 0)],
            [new ShownHand(0, ["As", "Ks"], "flush")], [new Win(0, 120)],
            [new PotResult(120, [0, 1], [new Win(0, 120)]), new PotResult(0, [1], [])], [1060, 940]),
        new MatchEnd([new Standing(0, "Ann", 2000, 1), new Standing(1, "Bob", 0, 2)]),
        BotAction.Fold,
        BotAction.Raise(60) with { Id = 4 },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void EveryMessage_RoundTrips(Message message)
    {
        var read = Protocol.Read(Protocol.Write(message));

        read.Should().BeEquivalentTo(message, o => o.RespectingRuntimeTypes());
        read.Should().BeOfType(message.GetType());
    }

    [Fact]
    public void Write_UsesCamelCaseKebabTypesAndLeavesNullsOut()
    {
        Protocol.Write(new Hello(1, "Caller")).Should().Be("""{"type":"hello","protocol":1,"name":"Caller"}""");
        Protocol.Write(BotAction.AllIn).Should().Be("""{"type":"action","action":"all-in"}""");
        Protocol.Write(BotAction.Raise(50)).Should().Be("""{"type":"action","action":"raise","to":50}""");
        Protocol.Write(new MatchEnd([])).Should().Be("""{"type":"match-end","standings":[]}""");
        Protocol.Write(SampleDecision()).Should().Contain("\"smallBlindSeat\":1").And.Contain("\"minRaiseTo\":40");
    }

    [Fact]
    public void UnknownFieldsAndTypes_AreIgnored()
    {
        var json = Protocol.Write(SampleDecision()).Replace("}", ",\"future\":3}");

        Protocol.Read(json).Should().BeOfType<Decision>();
        Protocol.Read("""{"type":"later-message"}""").Should().BeNull();
        Protocol.Read("hello world").Should().BeNull();
    }

    [Fact]
    public void TheTypeMayComeAnywhereInTheObject()
    {
        Protocol.Read("""{"action":"call","id":2,"type":"action"}""").Should().Be(new BotAction("call", null, 2));
        Protocol.Read("""{"name":"Py","protocol":1,"type":"hello"}""").Should().Be(new Hello(1, "Py"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"type":7}""")]
    [InlineData("""{"type":"action"}""")]
    [InlineData("""{"type":"action","action":5}""")]
    [InlineData("""{"type":"decision","hand":"one"}""")]
    [InlineData("""{"type":"match-end","standings":null}""")]
    [InlineData("""{"type":"hello""")]
    public void MalformedInput_IsNull_NotAnException(string line) => Protocol.Read(line).Should().BeNull();

    [Fact]
    public void AnUnknownActionName_IsKeptForTheAppToJudge() =>
        Protocol.Read("""{"type":"action","action":"bluff"}""").Should().Be(new BotAction("bluff"));

    [Fact]
    public void ADecisionWithoutItsId_IsNull()
    {
        var json = Protocol.Write(SampleDecision()).Replace("\"id\":7,", "");

        json.Should().NotContain("\"id\"");
        Protocol.Read(json).Should().BeNull();
    }
}
