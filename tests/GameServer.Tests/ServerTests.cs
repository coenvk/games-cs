using Jentl.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Uno;

namespace GameServer.Tests;

/// <summary>The real server in memory, with real <see cref="NetClient"/>s talking to it over WebSockets.</summary>
public sealed class ServerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TheStatusPage_Answers()
    {
        using var http = factory.CreateClient();

        var text = await http.GetStringAsync(new Uri("/", UriKind.Relative));

        text.Should().Contain("Jentl game server");
    }

    [Fact]
    public async Task TwoPlayers_MeetInARoom_AndPlay()
    {
        using var host = Connect();
        using var guest = Connect();

        host.Send(new CreateRoom(UnoOnline.Game, 2));
        var code = (await Next<RoomJoined>(host)).Code;
        guest.Send(new JoinRoom(code));
        (await Next<RoomJoined>(guest)).Seat.Should().Be(1);

        host.Send(new StartGame());
        await Next<GameStarted>(guest);
        var hostState = UnoOnline.ReadState((await Next<GameState>(host)).State)!;
        var guestState = UnoOnline.ReadState((await Next<GameState>(guest)).State)!;
        hostState.View.Seat.Should().Be(0);
        guestState.View.Seat.Should().Be(1);
        hostState.Names.Should().Equal("Player 1", "Player 2");

        // Whoever's turn it is draws; the other hears about it.
        var (mover, other) = hostState.View.CurrentSeat == 0 ? (host, guest) : (guest, host);
        mover.Send(new PlayerAction(UnoOnline.Write(new UnoPlay(DrawCard.Instance))));

        var heard = UnoOnline.ReadState((await Next<GameState>(other)).State)!;
        heard.Events.Should().Contain(e => e is CardsDrawn);
    }

    [Fact]
    public async Task ABadServerAddress_EndsInAnError()
    {
        using var client = new NetClient(_ => throw new System.Net.WebSockets.WebSocketException("refused"));

        var deadline = DateTime.UtcNow + Patience;
        while (client.Status != NetStatus.Closed && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        client.Status.Should().Be(NetStatus.Closed);
        client.Error.Should().Contain("Could not reach");
    }

    [Fact]
    public async Task AScore_GoesOnTheBricksLeaderboard_AndIsKeptInTheFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"scores-{Guid.NewGuid():N}.json");
        try
        {
            using var server = factory.WithWebHostBuilder(b => b.UseSetting("scores", file));
            using var player = new NetClient(async cancel =>
                await server.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/play"), cancel));

            player.Send(new SubmitScore("bricks", "Swift Otter 27", 12_400));
            var scores = await Next<Scores>(player);

            scores.Rank.Should().Be(0);
            scores.Top.Should().ContainSingle().Which.Score.Should().Be(12_400);
            File.ReadAllText(file).Should().Contain("Swift Otter 27");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private NetClient Connect() => new(async cancel =>
        await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/play"), cancel));

    /// <summary>Waits for the next message of a kind, skipping others.</summary>
    private static async Task<T> Next<T>(NetClient client)
        where T : ServerMessage
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            while (client.TryReceive(out var message))
            {
                if (message is T wanted)
                {
                    return wanted;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"No {typeof(T).Name} arrived (status {client.Status}, {client.Error}).");
    }
}
