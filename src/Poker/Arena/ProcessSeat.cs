using Jentl.Net;

namespace Poker.Arena;

/// <summary>Seats a bot in a process of its own: an <see cref="AgentSeat"/> over an <see cref="AgentProcess"/>.</summary>
public static class ProcessSeat
{
    /// <summary>Starts the bot's process and seats it under <paramref name="name"/>.</summary>
    public static AgentSeat Start(AgentCommand command, string name) => new(AgentProcess.Start(command), name);
}
