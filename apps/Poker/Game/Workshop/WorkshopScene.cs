using System.ComponentModel;
using System.Diagnostics;
using FontStashSharp;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.Input;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Poker.Arena;
using static Games.UI.Tr;

namespace Games.Workshop;

/// <summary>
/// The bot workshop: the bots there are (the house bots and the ones in the bots folder, with what is wrong with
/// those that cannot run) beside the choices: watch a match, run a batch, look at past matches, refresh the list,
/// open the folder.
/// </summary>
internal sealed class WorkshopScene : Scene
{
    private static readonly Color Problem = new(236, 120, 108);
    private readonly WorkshopContext _workshop;
    private readonly GameContext _context;
    private readonly Menu _menu;
    private IReadOnlyList<BotEntry> _bots;
    private string? _note;

    public WorkshopScene(WorkshopContext workshop)
    {
        _workshop = workshop;
        _context = workshop.Game;
        workshop.EnsureFolders();
        _bots = workshop.Bots();
        _menu = new Menu(
            _context.Sounds,
            new MenuItem(T("Watch a match"), () => Scenes.Push(new MatchSetupScene(workshop, batch: false))),
            new MenuItem(T("Run a batch"), () => Scenes.Push(new MatchSetupScene(workshop, batch: true))),
            new MenuItem(T("Past matches"), () => Scenes.Push(workshop.PastMatches())),
            new MenuItem(T("Refresh"), Refresh),
            new MenuItem(T("Open bots folder"), OpenFolder),
            new MenuItem(T("Back"), () => Scenes.Pop()));
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Dim);
        var padding = fonts.Px(40);
        var width = Math.Min(viewport.Width - 2 * padding, fonts.Px(1000));
        var height = Math.Min(viewport.Height - fonts.Px(40), fonts.Px(520));
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Center.Y - height / 2, width, height);
        batch.FillRectangle(panel, Palette.Panel);
        batch.FillRectangle(new Rectangle(panel.X, panel.Y, panel.Width, 6), Palette.Accent);
        batch.DrawText(fonts.Heading, T("Bot workshop"), new Vector2(panel.Center.X, panel.Y + fonts.Px(24)), Palette.Text, Align.Center);

        var top = panel.Y + fonts.Px(24) + (int)fonts.Heading.LineHeight + fonts.Px(20);
        _menu.Draw(batch, fonts.Body, panel.X + panel.Width / 4, top, panel.Bottom - top - fonts.Px(16));
        DrawBots(batch, new Rectangle(panel.Center.X, top, panel.Width / 2 - fonts.Px(24), panel.Bottom - top - fonts.Px(16)));
    }

    protected override void OnUncovered() => Refresh();

    private void Refresh()
    {
        _note = null;
        _bots = _workshop.Bots();
    }

    private void OpenFolder()
    {
        // The workshop is not offered in a browser; this keeps the platform analyzer sure of it.
        if (OperatingSystem.IsBrowser())
        {
            return;
        }

        _workshop.EnsureFolders();
        var opener = OperatingSystem.IsWindows() ? "explorer" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        try
        {
            using var started = Process.Start(new ProcessStartInfo(opener) { ArgumentList = { _workshop.BotsFolder }, UseShellExecute = false });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            _note = T("Could not open the folder: {0}", e.Message);
        }
    }

    private void DrawBots(SpriteBatch batch, Rectangle area)
    {
        var font = _context.Fonts.Small;
        var line = (int)font.LineHeight + 2;
        var y = area.Y;

        // Lines that no longer fit are left out; the last one says so.
        void Line(string text, Color color, int indent = 0)
        {
            foreach (var part in font.Wrap(text, area.Width - indent))
            {
                if (y + line > area.Bottom)
                {
                    return;
                }

                batch.DrawText(font, part, new Vector2(area.X + indent, y), color, shadow: false);
                y += line;
            }
        }

        Line(T("House bots"), Palette.Accent);
        Line(string.Join(", ", _bots.Where(b => b.Kind == BotKind.House).Select(b => b.Name)), Palette.Text, 12);
        y += line / 2;
        Line(T("Your bots"), Palette.Accent);
        var own = _bots.Where(b => b.Kind != BotKind.House).ToList();
        if (own.Count == 0)
        {
            Line(T("None yet. Put a bot in the bots folder, then choose Refresh."), Palette.Muted, 12);
        }

        foreach (var bot in own)
        {
            var kind = bot.Kind == BotKind.CSharp ? T("C# bot") : T("program");
            Line($"{bot.Name}  ({kind})", bot.Command is null ? Problem : Palette.Text, 12);
            if (bot.Command is null)
            {
                Line(Why(bot), Problem, 28);
            }
        }

        y += line / 2;
        Line(T("Folder"), Palette.Accent);
        Line(_note ?? Tail(font, _workshop.BotsFolder, area.Width - 12), Palette.Muted, 12);
    }

    // A path too long for the width keeps its end, which is the part that tells folders apart.
    private static string Tail(SpriteFontBase font, string path, float width)
    {
        var shown = path;
        while (shown.Length > 1 && font.MeasureString(shown).X > width)
        {
            shown = shown[1..];
        }

        return shown.Length == path.Length ? path : "…" + shown[1..];
    }

    // The entry's source reads "<file>: <problem>"; the file is shown by name only.
    private static string Why(BotEntry bot)
    {
        var at = bot.Source.IndexOf(": ", StringComparison.Ordinal);
        return $"{Path.GetFileName(bot.Source[..(at < 0 ? bot.Source.Length : at)])}: {(at < 0 ? "" : bot.Source[(at + 2)..])}".Trim();
    }
}
