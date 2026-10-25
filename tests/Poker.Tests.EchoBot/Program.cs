using PokerBots;

var chatty = args.Contains("chatty");
return BotRunner.Run(new CallingEchoBot(chatty), Console.In, Console.Out, Console.Error);

internal sealed class CallingEchoBot(bool chatty) : IPokerBot
{
    public string Name => "Echo";

    public BotAction Decide(Decision d)
    {
        if (chatty)
        {
            Console.Out.WriteLine("thinking…");
            Console.Out.Flush();
        }

        return d.Legal.Check ? BotAction.Check : BotAction.Call;
    }
}
