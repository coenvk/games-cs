using Jentl.Collect;
using Jentl.Net;

namespace Games;

/// <summary>
/// A game's leaderboard: the top ten on this device (saved as <c>scores-&lt;id&gt;.json</c>), and the same board on
/// the game server, which gets every score too. Add one to the context in the app's <c>Load</c>.
/// </summary>
internal sealed class ScoreBoard
{
    private readonly GameContext _context;
    private readonly SavedFile<Leaderboard> _local;

    /// <param name="id">The board's name, here and on the server ("bricks").</param>
    /// <param name="format">How a score is written ("12,400", "1:23").</param>
    public ScoreBoard(GameContext context, string id, ScoreOrder order, Func<long, string> format)
    {
        _context = context;
        (Id, Order, Format) = (id, order, format);
        _local = SavedFile<Leaderboard>.Load(context.Store, $"scores-{id}.json", json => Leaderboard.FromJson(json, order), b => b.ToJson(), () => new Leaderboard(order));
    }

    public string Id { get; }

    public ScoreOrder Order { get; }

    public Func<long, string> Format { get; }

    /// <summary>The top ten on this device.</summary>
    public Leaderboard Local => _local.Value;

    /// <summary>The place the last score took here (0 is the top), or null if it didn't make the board.</summary>
    public int? LastPlace { get; private set; }

    /// <summary>Puts a finished game's score on this device's board and sends it to the server; returns its place here.</summary>
    public int? Submit(long score)
    {
        var name = _context.PlayerName;
        LastPlace = Local.Submit(new LeaderboardEntry(name, score, DateTimeOffset.Now));
        if (LastPlace is not null)
        {
            _local.Save();
        }

        _context.OnlineScores().Send(new SubmitScore(Id, name, score));
        return LastPlace;
    }
}

/// <summary>
/// The connection to the game server for leaderboards, opened when first needed and opened again after it drops.
/// Scenes send messages and read <see cref="Latest"/> for each board's newest <see cref="Scores"/>; call
/// <see cref="Update"/> each frame (the scenes that show scores do).
/// </summary>
internal sealed class OnlineScores(Uri server) : IDisposable
{
    private readonly Dictionary<string, Scores> _latest = new(StringComparer.Ordinal);
    private NetClient? _client;

    public Uri Server => server;

    /// <summary>Why the last attempt failed, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Whether a connection is being made or is open.</summary>
    public bool IsUp => _client is { Status: not NetStatus.Closed };

    public Scores? Latest(string board) => _latest.GetValueOrDefault(board);

    public void Send(ClientMessage message)
    {
        if (_client is null || _client.Status == NetStatus.Closed)
        {
            _client?.Dispose();
            _client = new NetClient(server);
            Problem = null;
        }

        _client.Send(message);
    }

    public void Update()
    {
        if (_client is null)
        {
            return;
        }

        while (_client.TryReceive(out var message))
        {
            switch (message)
            {
                case Scores scores:
                    _latest[scores.Board] = scores;
                    Problem = null;
                    break;
                case ServerError error:
                    Problem = error.Text;
                    break;
            }
        }

        if (_client.Status == NetStatus.Closed)
        {
            Problem ??= _client.Error;
        }
    }

    public void Dispose() => _client?.Dispose();
}

internal static class OnlineScoresContext
{
    extension(GameContext context)
    {
        /// <summary>The game's <see cref="ScoreBoard"/> (the app adds it).</summary>
        public ScoreBoard ScoreBoard => context.Get<ScoreBoard>();

        /// <summary>The leaderboard connection, made the first time it is asked for.</summary>
        public OnlineScores OnlineScores() => context.Has<OnlineScores>() ? context.Get<OnlineScores>() : context.Add(new OnlineScores(context.GameServer));
    }
}
