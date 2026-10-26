using PokerBots;

// Modes (arguments): chatty (prints a debug line before each answer), shout (prints a debug line of 10,000 characters
// before its first answer), slow:<ms> (the first answer takes that long),
// stale (sends a wrong-id answer before each right one), protocol2 (greets with protocol 2 and then plays dead),
// skip:<n,m,...> (leaves those decisions unanswered), wait:<ms> (sleeps that long before reading anything, as a bot that is
// slow to start), calls (answers "call" even when checking is free, as a bot that never looks at `legal.check`).
if (args.FirstOrDefault(a => a.StartsWith("wait:", StringComparison.Ordinal)) is { } wait)
{
    Thread.Sleep(int.Parse(wait[5..], System.Globalization.CultureInfo.InvariantCulture));
}

var chatty = args.Contains("chatty");
var shout = args.Contains("shout");
var stale = args.Contains("stale");
var calls = args.Contains("calls");
var slow = args.FirstOrDefault(a => a.StartsWith("slow:", StringComparison.Ordinal)) is { } s ? int.Parse(s[5..], System.Globalization.CultureInfo.InvariantCulture) : 0;

// skip:<n,m,...> leaves those decisions (counting from 1) unanswered, as a bot that hangs would, and answers the others.
if (args.FirstOrDefault(a => a.StartsWith("skip:", StringComparison.Ordinal)) is { } skip)
{
    var skipped = skip[5..].Split(',').Select(n => int.Parse(n, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
    var asked = 0;
    while (Console.In.ReadLine() is { } line)
    {
        var reply = Protocol.Read(line) switch
        {
            Hello => new Hello(Protocol.Version, "Echo"),
            Decision d when !skipped.Contains(++asked) => (d.Legal.Check ? BotAction.Check : BotAction.Call) with { Id = d.Id },
            _ => (Message?)null,
        };
        if (reply is not null)
        {
            Console.Out.WriteLine(Protocol.Write(reply));
            Console.Out.Flush();
        }
    }

    return 0;
}

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

return BotRunner.Run(new CallingEchoBot(chatty, stale, slow, shout, calls), Console.In, Console.Out, Console.Error);

internal sealed class CallingEchoBot(bool chatty, bool stale, int slowMs, bool shout = false, bool calls = false) : IPokerBot
{
    private bool _answered;

    public string Name => "Echo";

    public BotAction Decide(Decision d)
    {
        if (shout && !_answered)
        {
            Console.Out.WriteLine(new string('x', 10_000));
            Console.Out.Flush();
        }

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
        return d.Legal.Check && !calls ? BotAction.Check : BotAction.Call;
    }
}
