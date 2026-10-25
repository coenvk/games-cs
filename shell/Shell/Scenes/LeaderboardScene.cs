using Games.Input;
using Jentl.Collect;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// A game's leaderboard, full screen: the top ten on this device, or online (asked from the game server when
/// shown), the player's own line picked out. Left and right (or the tabs) switch; Back closes it.
/// </summary>
internal sealed class LeaderboardScene : Scene
{
    private readonly GameContext _context;
    private readonly ScoreBoard _board;
    private readonly ButtonBar _tabs;
    private readonly ButtonBar _buttons;
    private bool _online;
    private bool _asked;

    /// <param name="online">Whether to open on the online board.</param>
    public LeaderboardScene(GameContext context, bool online = false)
    {
        _context = context;
        _board = context.ScoreBoard;
        _online = online;
        _tabs = new ButtonBar();
        _buttons = new ButtonBar(new Button(T("Back"), () => Scenes.Pop()));
        SetTabs();
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_online && !_asked)
        {
            _context.OnlineScores().Send(new GetScores(_board.Id, _context.PlayerName));
            _asked = true;
        }

        _context.OnlineScores().Update();
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        if (_tabs.Update(input) || _buttons.Update(input))
        {
            return;
        }

        if (_context.Actions.WasPressed(input, GameAction.Left) || _context.Actions.WasPressed(input, GameAction.Right))
        {
            Show(!_online);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Background);
        var pad = fonts.Px(32);
        var width = Math.Min(viewport.Width - (2 * pad), fonts.Px(760));
        var x = viewport.Center.X - (width / 2);
        var y = viewport.Y + pad;

        batch.DrawText(fonts.Heading, T("Leaderboard"), new Vector2(viewport.Center.X, y), Palette.Accent, Align.Center);
        y += (int)fonts.Heading.LineHeight + fonts.Px(8);
        var tabHeight = ButtonBar.Height(fonts.Body);
        _tabs.Draw(batch, fonts.Body, new Rectangle(viewport.X, y, viewport.Width, tabHeight));
        y += tabHeight + fonts.Px(12);
        batch.DrawText(fonts.Small, T("You are {0}", _context.PlayerName), new Vector2(viewport.Center.X, y), Palette.Muted, Align.Center, shadow: false);
        y += (int)fonts.Small.LineHeight + fonts.Px(12);

        var (rows, empty) = Rows();
        LeaderboardTable.Draw(batch, fonts, new Rectangle(x, y, width, viewport.Bottom - y), rows, empty);

        var bar = ButtonBar.Height(fonts.Body);
        _buttons.Draw(batch, fonts.Body, new Rectangle(viewport.X, viewport.Bottom - bar - fonts.Px(16), viewport.Width, bar));
    }

    private (IReadOnlyList<LeaderboardRow> Rows, string Empty) Rows()
    {
        if (!_online)
        {
            var local = _board.Local;
            return (LeaderboardTable.Rows(local.Entries, _board.LastPlace, null, _board.Format), T("No scores yet: play a game."));
        }

        var online = _context.OnlineScores();
        if (online.Latest(_board.Id) is { } scores)
        {
            return (LeaderboardTable.Rows(scores.Top, scores.Rank, scores.Mine, _board.Format), T("No scores yet: be the first."));
        }

        return ([], online.Problem is { } problem
            ? T("{0} Is the server at {1} running?", problem, online.Server)
            : T("Fetching scores from {0}…", online.Server.Host));
    }

    private void Show(bool online)
    {
        _online = online;
        _asked = false;
        _context.Sounds.Play(Sound.Navigate);
        SetTabs();
    }

    private void SetTabs() => _tabs.Set(
        new Button(T("This device"), () => Show(false), Highlight: !_online),
        new Button(T("Online"), () => Show(true), Highlight: _online));
}
