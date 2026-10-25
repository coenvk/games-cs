namespace PokerBots;

/// <summary>Runs an <see cref="IPokerBot"/> over the line-based protocol, normally on stdin and stdout.</summary>
public static class BotRunner
{
    /// <summary>
    /// Reads messages from <paramref name="input"/> until <c>match-end</c> or the end of the input. Answers <c>hello</c>,
    /// passes the other messages to the bot and writes one <c>action</c> per <c>decision</c>, flushing after each line.
    /// Lines that are not known messages are ignored. When a bot method throws, the exception goes to
    /// <paramref name="errors"/> and the runner carries on; a <see cref="IPokerBot.Decide"/> that throws or returns null
    /// sends no answer. Returns 0.
    /// </summary>
    public static int Run(IPokerBot bot, TextReader input, TextWriter output, TextWriter errors)
    {
        while (input.ReadLine() is { } line)
        {
            switch (Protocol.Read(line))
            {
                case Hello:
                    Send(output, new Hello(Protocol.Version, NameOf(bot, errors)));
                    break;
                case MatchStart m:
                    Guard(errors, nameof(IPokerBot.OnMatchStart), () => bot.OnMatchStart(m));
                    break;
                case HandStart h:
                    Guard(errors, nameof(IPokerBot.OnHandStart), () => bot.OnHandStart(h));
                    break;
                case Decision d:
                    Answer(bot, d, output, errors);
                    break;
                case HandEnd h:
                    Guard(errors, nameof(IPokerBot.OnHandEnd), () => bot.OnHandEnd(h));
                    break;
                case MatchEnd m:
                    Guard(errors, nameof(IPokerBot.OnMatchEnd), () => bot.OnMatchEnd(m));
                    return 0;
            }
        }

        return 0;
    }

    private static string NameOf(IPokerBot bot, TextWriter errors)
    {
        string? name = null;
        Guard(errors, nameof(IPokerBot.Name), () => name = bot.Name);
        return name ?? "bot";
    }

    private static void Answer(IPokerBot bot, Decision d, TextWriter output, TextWriter errors)
    {
        BotAction? action = null;
        Guard(errors, nameof(IPokerBot.Decide), () => action = bot.Decide(d));
        if (action is null)
        {
            errors.WriteLine("Decide returned null, so no answer was sent.");
            errors.Flush();
            return;
        }

        Send(output, action with { Id = d.Id });
    }

    private static void Guard(TextWriter errors, string what, Action call)
    {
        try
        {
            call();
        }
        catch (Exception e)
        {
            errors.WriteLine($"{what} threw: {e}");
            errors.Flush();
        }
    }

    private static void Send(TextWriter output, Message message)
    {
        output.Write(Protocol.Write(message) + "\n");
        output.Flush();
    }
}
