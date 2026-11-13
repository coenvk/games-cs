using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.Input;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// A game's title screen: its name, a line under it, and a menu of the game's own choices followed by Options and
/// (where the platform can) Quit. The choices are rebuilt whenever the screen shows again, so labels such as
/// "Continue" stay current.
/// </summary>
internal sealed class TitleScene : Scene
{
    private readonly GameContext _context;
    private readonly Func<TitleScene, IReadOnlyList<MenuItem>> _items;
    private readonly Func<string> _subtitle;
    private Menu _menu;
    private bool _canLeave;

    /// <param name="items">The game's choices (they can <see cref="Open"/> a game); Options and Quit follow by themselves.</param>
    /// <param name="subtitle">The line under the title (it may change, e.g. with the records).</param>
    public TitleScene(GameContext context, Func<TitleScene, IReadOnlyList<MenuItem>> items, Func<string> subtitle)
    {
        _context = context;
        _items = items;
        _subtitle = subtitle;
        _menu = BuildMenu();
    }

    /// <summary>Opens <paramref name="scene"/> over the title screen: a game (with a fade) or a panel.</summary>
    public void Open(Scene scene, bool fade = true) => Scenes.Push(scene, fade ? Navigation.Fade() : null);

    /// <summary>The scene manager this screen is on, for choices that replace it (a suite starting a game).</summary>
    public SceneManager Manager => Scenes;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        // Esc moves to the way out (Quit, or All games) rather than taking it, so a stray press is harmless.
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            if (_canLeave)
            {
                _menu.Selected = _menu.Items.Count - 1;
            }
            else
            {
                _context.ToBackground?.Invoke();
            }

            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var centerX = viewport.Center.X;
        var gap = fonts.Px(24);
        var footerTop = viewport.Bottom - (int)fonts.Small.LineHeight - gap;
        var titleHeight = (int)fonts.Title.LineHeight;
        var subtitleHeight = (int)fonts.Small.LineHeight;

        // Whatever doesn't fit between the title and the footer scrolls.
        var room = footerTop - gap - (viewport.Y + titleHeight + subtitleHeight + 2 * gap);
        var menuHeight = _menu.Height(fonts.Body, room);
        var column = titleHeight + subtitleHeight + 2 * gap + menuHeight;
        var top = viewport.Y + Math.Max(0, (footerTop - viewport.Y - column) / 2);

        batch.DrawText(fonts.Title, _context.App.Title, new Vector2(centerX, top), Palette.Accent, Align.Center);
        batch.DrawText(fonts.Small, _subtitle(), new Vector2(centerX, top + titleHeight), Palette.Muted, Align.Center, shadow: false);
        _menu.Draw(batch, fonts.Body, centerX, top + titleHeight + subtitleHeight + 2 * gap, room);
        batch.DrawText(fonts.Small, _context.Platform.ChooseHint, new Vector2(centerX, footerTop), Palette.Muted, Align.Center, shadow: false);
    }

    protected override void OnEnter() => _context.PlayMood(Music.Menu);

    // Back from a game, the menu's music returns, and its labels are rebuilt.
    protected override void OnUncovered()
    {
        _context.PlayMood(Music.Menu);
        _menu = BuildMenu(_menu.Selected);
    }

    private Menu BuildMenu(int selected = 0)
    {
        List<MenuItem> items = [.. _items(this), new(T("Music"), () => Scenes.Push(new MusicScene(_context))), new(T("Options"), () => Scenes.Push(new OptionsScene(_context)))];
        var leave = Navigation.Leave(_context, () => Scenes);
        _canLeave = leave is not null;
        if (leave is not null)
        {
            items.Add(leave);
        }

        return new Menu(_context.Sounds, [.. items]) { Selected = Math.Min(selected, items.Count - 1) };
    }
}
