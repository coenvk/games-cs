using PokerBots;

// Modes (arguments): chatty (prints a debug line before each answer), slow:<ms> (the first answer takes that long),
// stale (sends a wrong-id answer before each right one), protocol2 (greets with protocol 2 and then plays dead).
var chatty = args.Contains("chatty");
var stale = args.Contains("stale");
var slow = args.FirstOrDefault(a => a.StartsWith("slow:", StringComparison.Ordinal)) is { } s ? int.Parse(s[5..], System.Globalization.CultureInfo.InvariantCulture) : 0;

if (args.Contains("protocol2"))
{
    while (Console.In.ReadLine() is { } line)
    {
        if (Protocol.Read(line) is Hello)
        {
            Console.Out.WriteLine(Protocol.Write(new Hello(2, "Old")));
            Console.Out.Flush();
        }
    }

    return 0;
}

return BotRunner.Run(new CallingEchoBot(chatty, stale, slow), Console.In, Console.Out, Console.Error);

internal sealed class CallingEchoBot(bool chatty, bool stale, int slowMs) : IPokerBot
{
    private bool _answered;

    public string Name => "Echo";

    public BotAction Decide(Decision d)
    {
        if (chatty)
        {
            Console.Out.WriteLine("thinking…");
            Console.Out.Flush();
        }

        if (stale)
        {
            Console.Out.WriteLine(Protocol.Write(BotAction.Fold with { Id = d.Id - 1 }));
            Console.Out.Flush();
        }

        if (!_answered && slowMs > 0)
        {
            Thread.Sleep(slowMs);
        }

        _answered = true;
        return d.Legal.Check ? BotAction.Check : BotAction.Call;
    }
}
