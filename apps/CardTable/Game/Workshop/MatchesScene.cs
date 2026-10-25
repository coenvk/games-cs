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
/// The matches played before, newest first: a file in the workshop's matches folder each, with when it was played and
/// how, and beside the list the bots and hands of the one chosen. Choosing one opens its results; a file that cannot
/// be read is listed with what is wrong with it.
/// </summary>
internal sealed class MatchesScene : Scene
{
    private static readonly Color Problem = new(236, 120, 108);
    private readonly WorkshopContext _workshop;
    private readonly GameContext _context;
    private readonly List<Entry> _entries;
    private readonly Menu _menu;

    public MatchesScene(WorkshopContext workshop)
    {
        ArgumentNullException.ThrowIfNull(workshop);
        _workshop = workshop;
        _context = workshop.Game;
        _entries = Scan(workshop.MatchesFolder);
        _menu = new Menu(
            _context.Sounds,
            [
                .. _entries.Select((e, i) => new MenuItem(e.Label, () => Open(i))),
                new MenuItem(T("Back"), () => Scenes.Pop()),
            ]);
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
        var width = Math.Min(viewport.Width - 2 * padding, fonts.Px(1100));
        var height = Math.Min(viewport.Height - fonts.Px(40), fonts.Px(600));
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Center.Y - height / 2, width, height);
        batch.FillRectangle(panel, Palette.Panel);
        batch.FillRectangle(new Rectangle(panel.X, panel.Y, panel.Width, 6), Palette.Accent);
        batch.DrawText(fonts.Heading, T("Past matches"), new Vector2(panel.Center.X, panel.Y + fonts.Px(24)), Palette.Text, Align.Center);

        var top = panel.Y + fonts.Px(24) + (int)fonts.Heading.LineHeight + fonts.Px(20);
        var bottom = panel.Bottom - fonts.Px(16);
        _menu.Draw(batch, fonts.Small, panel.X + panel.Width * 3 / 10, top, bottom - top);
        DrawDetails(batch, new Rectangle(panel.X + panel.Width * 6 / 10, top, panel.Width * 4 / 10 - fonts.Px(24), bottom - top));
    }

    /// <summary>The folder's match files, newest first; each is read to say what it holds, or what is wrong with it.</summary>
    private static List<Entry> Scan(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var files = new DirectoryInfo(folder).GetFiles("*.json")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal);
        var entries = new List<Entry>();
        foreach (var file in files)
        {
            var (match, problem) = Read(file.FullName);
            entries.Add(match is null
                ? new Entry(file.FullName, T("{0} (cannot be read)", file.Name), [file.Name, problem!], Problem: true)
                : new Entry(
                    file.FullName,
                    $"{ResultsScene.When(match)}  ·  {ResultsScene.FormatName(match)}",
                    [string.Join(", ", match.Bots), N(match.Hands.Count, "{0} hand", "{0} hands"), file.Name],
                    Problem: false));
        }

        return entries;
    }

    private static (MatchResult? Match, string? Problem) Read(string path)
    {
        try
        {
            return (MatchFile.Read(File.ReadAllText(path)), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (null, e.Message.ReplaceLineEndings(" "));
        }
    }

    // Read again when chosen, as the file may have changed since the list was made.
    private void Open(int index)
    {
        var entry = _entries[index];
        var (match, problem) = Read(entry.Path);
        if (match is null)
        {
            _entries[index] = entry with { Lines = [Path.GetFileName(entry.Path), problem!], Problem = true };
            return;
        }

        Scenes.Push(_workshop.MatchResults(match, entry.Path));
    }

    private void DrawDetails(SpriteBatch batch, Rectangle area)
    {
        var font = _context.Fonts.Small;
        var line = (int)font.LineHeight + 2;
        var y = area.Y;
        void Line(string text, Color color)
        {
            foreach (var part in font.Wrap(text, area.Width))
            {
                if (y + line > area.Bottom)
                {
                    return;
                }

                batch.DrawText(font, part, new Vector2(area.X, y), color, shadow: false);
                y += line;
            }
        }

        if (_entries.Count == 0)
        {
            Line(T("No matches yet. Watch a match or run a batch, and it is kept here."), Palette.Muted);
            return;
        }

        if (_menu.Selected >= _entries.Count)
        {
            Line(N(_entries.Count, "{0} match in the matches folder", "{0} matches in the matches folder"), Palette.Muted);
            return;
        }

        var entry = _entries[_menu.Selected];
        for (var i = 0; i < entry.Lines.Count; i++)
        {
            Line(entry.Lines[i], i == 0 && !entry.Problem ? Palette.Text : entry.Problem && i > 0 ? Problem : Palette.Muted);
        }
    }

    /// <summary>A match file: what the list says, the lines shown beside it, and whether those say what is wrong.</summary>
    private sealed record Entry(string Path, string Label, IReadOnlyList<string> Lines, bool Problem);
}
