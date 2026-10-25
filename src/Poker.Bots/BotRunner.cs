namespace PokerBots;

/// <summary>Runs an <see cref="IPokerBot"/> over the line-based protocol, normally on stdin and stdout.</summary>
public static class BotRunner
{
    /// <summary>
    /// Reads messages from <paramref name="input"/> until <c>match-end</c> or the end of the input. Answers <c>hello</c>,
    /// passes the other messages to the bot and writes one <c>action</c> per <c>decision</c>, flushing after each line.
    /// Lines that are not known messages are ignored. When <see cref="IPokerBot.Decide"/> throws, the exception goes to
    /// <paramref name="errors"/> and no answer is sent. Returns 0.
    /// </summary>
    public static int Run(IPokerBot bot, TextReader input, TextWriter output, TextWriter errors)
    {
        while (input.ReadLine() is { } line)
        {
            switch (Protocol.Read(line))
            {
                case Hello:
                    Send(output, new Hello(Protocol.Version, bot.Name));
                    break;
                case MatchStart m:
                    bot.OnMatchStart(m);
                    break;
                case HandStart h:
                    bot.OnHandStart(h);
                    break;
                case Decision d:
                    Answer(bot, d, output, errors);
                    break;
                case HandEnd h:
                    bot.OnHandEnd(h);
                    break;
                case MatchEnd m:
                    bot.OnMatchEnd(m);
                    return 0;
            }
        }

        return 0;
    }

    private static void Answer(IPokerBot bot, Decision d, TextWriter output, TextWriter errors)
    {
        BotAction action;
        try
        {
            action = bot.Decide(d);
        }
        catch (Exception e)
        {
            errors.WriteLine($"Decide threw: {e}");
            errors.Flush();
            return;
        }

        Send(output, action with { Id = d.Id });
    }

    private static void Send(TextWriter output, Message message)
    {
        output.Write(Protocol.Write(message) + "\n");
        output.Flush();
    }
}
