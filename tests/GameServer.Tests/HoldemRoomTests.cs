using Jentl.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Poker;

namespace GameServer.Tests;

/// <summary>Hold'em rooms: two people at one table through the room server, each told only their own cards.</summary>
public sealed class HoldemRoomTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly RoomServer _server = new(new Dictionary<string, Func<IRoomGame>> { [HoldemOnline.Game] = () => new HoldemRoomGame() }, seed: 9);

    [Fact]
    public async Task TheServer_HostsHoldem_ForTwoPeople()
    {
        using var host = Connect();
        using var guest = Connect();

        host.Send(new CreateRoom(HoldemOnline.Game, 2));
        var code = (await Next<RoomJoined>(host)).Code;
        guest.Send(new JoinRoom(code));
        (await Next<RoomJoined>(guest)).Seat.Should().Be(1);
        host.Send(new StartGame());

        var mine = HoldemOnline.ReadState((await Next<GameState>(host)).State)!;
        var theirs = HoldemOnline.ReadState((await Next<GameState>(guest)).State)!;
        mine.Names.Should().Equal("Player 1", "Player 2");
        mine.Hand.Holes[0].Should().HaveCount(2);
        mine.Hand.Holes[1].Should().BeEmpty();
        theirs.Hand.Holes[1].Should().HaveCount(2);
        theirs.Hand.Holes[0].Should().BeEmpty();
    }

    [Fact]
    public void TwoSeats_PlayHands_AndNeitherIsSentTheOthersUnshownCards()
    {
        var host = new FakeConnection();
        _server.Received(host, Protocol.Write(new CreateRoom(HoldemOnline.Game, 2)));
        var guest = new FakeConnection();
        _server.Received(guest, Protocol.Write(new JoinRoom(host.Last<RoomJoined>().Code)));
        _server.Received(host, Protocol.Write(new StartGame()));
        FakeConnection[] people = [host, guest];

        // Both call or check every time, and ask for the next hand; a few hands go by.
        for (var step = 0; step < 400 && State(host).HandNumber < 6; step++)
        {
            var state = State(host);
            var hand = HoldemOnline.Rebuild(state.Hand, state.Hand.Actions.Count);
            if (hand.IsOver)
            {
                foreach (var person in people)
                {
                    _server.Received(person, Act(new HoldemNextHand()));
                }
            }
            else
            {
                _server.Received(people[hand.CurrentSeat], Act(new HoldemMove("call")));
            }
        }

        State(host).HandNumber.Should().BeGreaterThanOrEqualTo(6);
        for (var seat = 0; seat < 2; seat++)
        {
            var me = people[seat];
            var other = people[1 - seat];
            var theirCards = other.All<GameState>().Select(s => HoldemOnline.ReadState(s.State)!)
                .ToLookup(s => s.HandNumber, s => s.Hand.Holes[1 - seat])
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var raw in me.Raw.Where(r => Protocol.ReadServer(r) is GameState))
            {
                var state = HoldemOnline.ReadState(((GameState)Protocol.ReadServer(raw)!).State)!;
                if (state.Hand.Holes[1 - seat].Count > 0)
                {
                    HoldemOnline.Rebuild(state.Hand, state.Hand.Actions.Count).IsOver.Should().BeTrue("cards are shown only at the showdown");
                    continue;
                }

                foreach (var card in theirCards[state.HandNumber])
                {
                    raw.Should().NotContain($"\"{card}\"", "seat {0} is never sent seat {1}'s unshown cards", seat, 1 - seat);
                }
            }
        }
    }

    [Fact]
    public void APersonWhoLeaves_IsReplacedByABot_AndTheHandGoesOn()
    {
        var host = new FakeConnection();
        _server.Received(host, Protocol.Write(new CreateRoom(HoldemOnline.Game, 2)));
        var guest = new FakeConnection();
        _server.Received(guest, Protocol.Write(new JoinRoom(host.Last<RoomJoined>().Code)));
        _server.Received(host, Protocol.Write(new StartGame()));

        _server.Disconnected(guest);
        var moves = State(host).Hand.Actions.Count;
        for (var i = 0; i < 10; i++)
        {
            var state = State(host);
            if (state.Hand.Actions.Count > moves + 1)
            {
                break;
            }

            var hand = HoldemOnline.Rebuild(state.Hand, state.Hand.Actions.Count);
            if (!hand.IsOver && hand.CurrentSeat == 0)
            {
                _server.Received(host, Act(new HoldemMove("call")));
            }

            _server.Update(TimeSpan.FromSeconds(1));
        }

        State(host).Names[1].Should().Be("Player 2 (bot)");
        State(host).Hand.Actions.Should().Contain(a => a.Seat == 1, "the bot played the seat");
    }

    private static string Act(HoldemOnlineAction action) => Protocol.Write(new PlayerAction(HoldemOnline.Write(action)));

    private static HoldemOnlineState State(FakeConnection player) => HoldemOnline.ReadState(player.Last<GameState>().State)!;

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

                if (message is ServerError error)
                {
                    throw new InvalidOperationException(error.Text);
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"No {typeof(T).Name} arrived (status {client.Status}, {client.Error}).");
    }

    private sealed class FakeConnection : IConnection
    {
        public List<string> Raw { get; } = [];

        public void Send(string message) => Raw.Add(message);

        public void Close()
        {
        }

        public IEnumerable<T> All<T>()
            where T : ServerMessage => Raw.Select(Protocol.ReadServer).OfType<T>();

        public T Last<T>()
            where T : ServerMessage => All<T>().Last();
    }
}
