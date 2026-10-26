using System.Globalization;
using FontStashSharp;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Multiplayer;
using Games.Input;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Poker.Arena;
using static Games.UI.Tr;

namespace Games.Workshop;

/// <summary>
/// How each bot did in a finished match or batch, best first, a row each: its result, its hands and faults, and how it
/// played (VPIP, PFR, AF, WTSD, W$SD over the match's hands). The result is big blinds won per 100 hands with the 95%
/// margin, or for sit-and-gos, where that means nothing, the place (a series: the average place and how often each
/// place was taken). Under the table, why each bot that could not play could not. Below: Replay hands, Export hand
/// histories (into the workshop's histories folder, named after the match file), Run again (the same bots, rules and
/// seed; not for a match downloaded from the server) and Back.
/// </summary>
internal sealed class ResultsScene : Scene
{
    private static readonly Color Problem = new(236, 120, 108);

    private readonly WorkshopContext _workshop;
    private readonly GameContext _context;
    private readonly MatchResult _match;
    private readonly string? _path;
    private readonly ButtonBar _buttons = new();
    private readonly string[] _header;
    private readonly List<string[]> _rows;
    private readonly bool[] _faulty;
    private readonly string[] _problems;

    // What each button does, in order; Replay only when there are hands.
    private Action[] _actions = [];
    private int _selected;
    private string? _note;
    private bool _noteIsProblem;

    /// <param name="path">The file the match was saved to, or null when saving failed.</param>
    public ResultsScene(WorkshopContext workshop, MatchResult match, string? path)
    {
        ArgumentNullException.ThrowIfNull(workshop);
        ArgumentNullException.ThrowIfNull(match);
        _workshop = workshop;
        _context = workshop.Game;
        _match = match;
        _path = path;
        (_header, _rows, _faulty) = Table(match);
        _problems = [.. match.Results.Where(r => r.Problem is not null).Select(r => r.Problem == OwnerView.OpponentProblem
            ? T("{0} had a problem", r.Name)
            : T("{0} could not play: {1}", r.Name, r.Problem!))];
        SetButtons();
    }

    public override bool IsOverlay => true;

    private bool CanReplay => _match.Hands.Count > 0;

    // A match downloaded from the server (kept in the online folder, or not kept at all and without a seed) cannot be run
    // again: it has no seed, and its opponent is someone else's bot.
    private bool IsDownloaded => _path is null
        ? _match.Settings.Seed == default && (_match.Batch?.Seed ?? default) == default
        : string.Equals(Path.GetDirectoryName(Path.GetFullPath(_path)), Path.GetFullPath(_workshop.OnlineMatchesFolder), StringComparison.Ordinal);

    /// <summary>What a batch is called on screen.</summary>
    public static string FormatName(BatchSettings batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return batch.Format switch
        {
            BatchFormat.DuplicateHeadsUp => T("Duplicate heads-up"),
            BatchFormat.DuplicateRing => T("Duplicate ring"),
            _ => T("Sit-and-go series"),
        };
    }

    /// <summary>What a match or batch is called on screen.</summary>
    public static string FormatName(MatchResult match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Batch is { } batch ? FormatName(batch)
            : match.Settings.Format == TableFormat.SitAndGo ? T("Sit-and-go") : T("Fixed stacks");
    }

    /// <summary>When the match was played, as the player reads dates.</summary>
    public static string When(MatchResult match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.At.ToLocalTime().ToString("g", Tr.Culture);
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        // The buttons by keyboard: left and right choose, confirm presses; R and E are shortcuts.
        var count = _actions.Length;
        if (Controls.Ui.WasPressed(input, UiAction.Left))
        {
            _selected = (_selected + count - 1) % count;
            SetButtons();
        }
        else if (Controls.Ui.WasPressed(input, UiAction.Right))
        {
            _selected = (_selected + 1) % count;
            SetButtons();
        }
        else if (Controls.Ui.WasPressed(input, UiAction.Confirm))
        {
            _actions[_selected]();
        }
        else if (input.WasPressed(Keys.R) && CanReplay)
        {
            Replay();
        }
        else if (input.WasPressed(Keys.E))
        {
            Export();
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Dim);
        var margin = fonts.Px(24);
        var panel = new Rectangle(viewport.X + margin, viewport.Y + margin, viewport.Width - 2 * margin, viewport.Height - 2 * margin);
        Theme.DrawPanel(batch, panel);
        var padding = fonts.Px(28);
        var inner = new Rectangle(panel.X + padding, panel.Y + fonts.Px(20), panel.Width - 2 * padding, panel.Height - fonts.Px(20) - padding / 2);

        var y = inner.Y;
        batch.DrawText(fonts.Heading, T("Match results"), new Vector2(panel.Center.X, y), Palette.Text, Align.Center);
        y += (int)fonts.Heading.LineHeight + fonts.Px(4);
        var small = fonts.Small;
        foreach (var (text, color) in Summary())
        {
            foreach (var line in small.Wrap(text, inner.Width))
            {
                batch.DrawText(small, line, new Vector2(panel.Center.X, y), color, Align.Center, shadow: false);
                y += (int)small.LineHeight + 2;
            }
        }

        var buttonFont = _buttons.Width(fonts.Body) <= inner.Width ? fonts.Body : small;
        var buttonsHeight = ButtonBar.Height(buttonFont);
        var buttons = new Rectangle(inner.X, inner.Bottom - buttonsHeight, inner.Width, buttonsHeight);
        // Why a bot could not play, between the table and the buttons.
        var problems = _problems.SelectMany(p => small.Wrap(p, inner.Width)).ToList();
        var problemsTop = buttons.Top - fonts.Px(12) - problems.Count * ((int)small.LineHeight + 2);
        DrawTable(batch, new Rectangle(inner.X, y + fonts.Px(12), inner.Width, problemsTop - y - fonts.Px(24)));
        foreach (var line in problems)
        {
            batch.DrawText(small, line, new Vector2(panel.Center.X, problemsTop), Problem, Align.Center, shadow: false);
            problemsTop += (int)small.LineHeight + 2;
        }

        _buttons.Draw(batch, buttonFont, buttons);
    }

    private void SetButtons()
    {
        List<(string Label, Action Press)> buttons = [];
        if (CanReplay)
        {
            buttons.Add((T("Replay hands"), Replay));
        }

        buttons.Add((T("Export hand histories"), Export));
        if (!IsDownloaded)
        {
            buttons.Add((T("Run again"), RunAgain));
        }

        buttons.Add((T("Back"), () => Scenes.Pop()));
        _actions = [.. buttons.Select(b => b.Press)];
        _selected = Math.Clamp(_selected, 0, _actions.Length - 1);
        _buttons.Set([.. buttons.Select((b, i) => new Button(b.Label, b.Press, Highlight: i == _selected))]);
    }

    private void Replay() => Scenes.Push(new ReplayScene(_workshop, _match, 0));

    /// <summary>
    /// Plays the match (or batch) again in place of these results: the same bots in the same seats, by their names in
    /// the bots list now (the player at the seat that was theirs), with the same rules and seed, so bots that play by
    /// their seeds play it out the same way.
    /// </summary>
    private void RunAgain()
    {
        var you = T("You");
        var bots = _workshop.Bots().Where(b => b.Kind == BotKind.House || b.Command is not null).ToList();
        string[] names = [.. bots.Select(b => b.Name), .. _match.Batch is null ? [you] : Array.Empty<string>()];
        var seats = new List<BotEntry?>();
        foreach (var seat in _match.Bots)
        {
            if (SeatNames.Original(seat, names) is not { } name)
            {
                _note = T("Cannot run it again: {0} is not in the bots folder.", seat);
                _noteIsProblem = true;
                return;
            }

            seats.Add(name == you && _match.Batch is null ? null : bots.First(b => b.Name == name));
        }

        Scenes.Replace(_match.Batch is { } batch
            ? _workshop.RunBatch(new BatchRequest([.. seats.OfType<BotEntry>()], batch))
            : _workshop.WatchMatch(new MatchRequest(seats, _match.Settings)));
    }

    /// <summary>Writes every hand as a PokerStars hand history into one text file named after the match file.</summary>
    private void Export()
    {
        var name = _path is null ? MatchFile.Name(_match) : Path.GetFileNameWithoutExtension(_path);
        var file = Path.Combine(_workshop.HistoriesFolder, name + ".txt");
        try
        {
            Directory.CreateDirectory(_workshop.HistoriesFolder);
            File.WriteAllText(file, HandHistoryText.WriteMatch(_match, name));
            _note = T("Hand histories written to {0}", _workshop.Shown(file));
            _noteIsProblem = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _note = T("Could not write the hand histories: {0}", e.Message);
            _noteIsProblem = true;
        }
    }

    // The lines under the title: what was played and when, where it was saved, and what the last export did.
    private IEnumerable<(string Text, Color Color)> Summary()
    {
        var hands = N(_match.Hands.Count, "{0} hand", "{0} hands");
        // A match downloaded from the server has no seed: it would deal the opponent's hidden cards again.
        var seed = _match.Batch?.Seed ?? _match.Settings.Seed;
        yield return seed == default
            ? ($"{FormatName(_match)}  ·  {When(_match)}  ·  {hands}", Palette.Muted)
            : ($"{FormatName(_match)}  ·  {When(_match)}  ·  {hands}  ·  {T("seed {0}", seed.ToString())}", Palette.Muted);
        yield return _path is null
            ? (T("The match could not be saved."), Problem)
            : (T("Saved to {0}", _workshop.Shown(_path)), Palette.Muted);
        if (IsCutShort(_match))
        {
            yield return (T("Stopped before the end: only what was played in full counts."), Problem);
        }

        if (_note is not null)
        {
            yield return (_note, _noteIsProblem ? Problem : Palette.Accent);
        }
    }

    // A batch that was cancelled (or a sit-and-go of a series that ran out of hands): fewer deals or places than asked.
    private static bool IsCutShort(MatchResult match) => match.Batch switch
    {
        null => false,
        { Format: BatchFormat.SitAndGoSeries } batch => match.Results.Any(r => r.Places.Count < batch.Hands),
        var batch => match.Results.Any(r => r.Hands < batch.Hands * Seatings.Duplicate(match.Results.Count).Count),
    };

    /// <summary>The header and a row of text per bot, best first, and which rows have faults to show in red.</summary>
    private static (string[] Header, List<string[]> Rows, bool[] Faulty) Table(MatchResult match)
    {
        var sitAndGo = match.Settings.Format == TableFormat.SitAndGo;
        var series = match.Batch?.Format == BatchFormat.SitAndGoSeries;
        var places = series ? match.Results.Count : 0;
        List<string> header = [T("Bot"), sitAndGo ? (series ? T("Avg place") : T("Place")) : T("bb/100")];
        header.AddRange(Enumerable.Range(1, places).Select(p => "#" + p.ToString(CultureInfo.InvariantCulture)));
        header.AddRange([T("Hands"), T("Late"), T("Illegal"), T("Gone"), "VPIP", "PFR", "AF", "WTSD", "W$SD"]);

        // Best first: the most big blinds won, or the best (average) place; a bot without a place goes last.
        var ordered = sitAndGo
            ? match.Results.OrderBy(r => r.Places.Count == 0 ? double.MaxValue : r.Places.Average())
            : match.Results.OrderByDescending(r => r.BigBlindsPer100);
        var rows = new List<string[]>();
        var faulty = new List<bool>();
        foreach (var result in ordered)
        {
            var stats = PokerStats.For(match.Hands, result.Name);
            List<string> row = [result.Name, Score(result, sitAndGo, series, match.Batch is not null)];
            row.AddRange(Enumerable.Range(1, places).Select(p => result.Places.Count(x => x == p).ToString(Tr.Culture)));
            row.AddRange(
            [
                result.Hands.ToString("N0", Tr.Culture),
                result.Late.ToString(Tr.Culture),
                result.Illegal.ToString(Tr.Culture),
                result.Gone.ToString(Tr.Culture),
                Percent(stats.Vpip),
                Percent(stats.Pfr),
                stats.AggressionFactor?.ToString("0.0", Tr.Culture) ?? "—",
                Percent(stats.WentToShowdown),
                Percent(stats.WonAtShowdown),
            ]);
            rows.Add([.. row]);
            faulty.Add(result.Late + result.Illegal + result.Gone > 0);
        }

        return ([.. header], rows, [.. faulty]);
    }

    // "+4.2 ± 1.3" with a margin (a batch, a watched match with fixed stacks), "+4.2" for a watched match saved before
    // those had one, a place for a sit-and-go, "—" for nothing.
    private static string Score(BotResult result, bool sitAndGo, bool series, bool batch)
    {
        if (sitAndGo)
        {
            return result.Places.Count == 0 ? "—"
                : series ? result.Places.Average().ToString("0.00", Tr.Culture)
                : result.Places[0].ToString(Tr.Culture);
        }

        if (result.Hands == 0)
        {
            return "—";
        }

        var value = result.BigBlindsPer100.ToString("+0.0;-0.0;0.0", Tr.Culture);
        return batch || result.Margin95 > 0 ? $"{value} ± {result.Margin95.ToString("0.0", Tr.Culture)}" : value;
    }

    private static string Percent(double rate) => (rate * 100).ToString("0", Tr.Culture) + "%";

    /// <summary>
    /// The table in <paramref name="area"/>: each column as wide as its widest cell, the name left and the numbers
    /// right, in a smaller font when the normal one does not fit; a name too long for its column is cut short.
    /// </summary>
    private void DrawTable(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        var font = fonts.Body;
        var gap = fonts.Px(18);
        int[] widths;
        var nameWidth = fonts.Px(260);
        while (true)
        {
            widths = Widths(font, nameWidth);
            var lineHeight = (int)font.LineHeight + fonts.Px(8);
            var fits = widths.Sum() + gap * (widths.Length - 1) <= area.Width && lineHeight * (_rows.Count + 1) <= area.Height;
            if (fits || font.LineHeight <= 10)
            {
                break;
            }

            font = fonts.Sized(font.LineHeight * 0.85f);
        }

        var total = widths.Sum() + gap * (widths.Length - 1);
        var line = (int)font.LineHeight + fonts.Px(8);
        var x0 = area.X + Math.Max(0, (area.Width - total) / 2);
        var y = area.Y;
        DrawRow(batch, font, _header, widths, x0, y, gap, Palette.Accent);
        y += line;
        batch.FillRectangle(new Rectangle(x0, y - fonts.Px(5), total, 1), Palette.PanelLight);
        for (var r = 0; r < _rows.Count; r++)
        {
            if (r % 2 == 1)
            {
                batch.FillRectangle(new Rectangle(x0 - fonts.Px(6), y - fonts.Px(3), total + fonts.Px(12), line), Palette.PanelLight * 0.45f);
            }

            DrawRow(batch, font, _rows[r], widths, x0, y, gap, Palette.Text, _faulty[r]);
            y += line;
        }
    }

    private int[] Widths(SpriteFontBase font, int nameWidth)
    {
        var widths = new int[_header.Length];
        for (var c = 0; c < widths.Length; c++)
        {
            var widest = _rows.Select(r => r[c]).Append(_header[c]).Max(t => font.MeasureString(t).X);
            widths[c] = (int)MathF.Ceiling(c == 0 ? Math.Min(widest, nameWidth) : widest);
        }

        return widths;
    }

    // The faults (late, illegal, gone) are red where there are any.
    private static void DrawRow(SpriteBatch batch, SpriteFontBase font, string[] cells, int[] widths, int x, int y, int gap, Color color, bool faulty = false)
    {
        var faultColumns = cells.Length - 8;
        for (var c = 0; c < cells.Length; c++)
        {
            var text = c == 0 ? Cut(font, cells[c], widths[c]) : cells[c];
            var cellColor = faulty && c >= faultColumns && c < faultColumns + 3 && cells[c] != "0" ? Problem : color;
            var at = c == 0 ? x : x + widths[c] - font.MeasureString(text).X;
            batch.DrawText(font, text, new Vector2(at, y), cellColor, shadow: false);
            x += widths[c] + gap;
        }
    }

    private static string Cut(SpriteFontBase font, string text, int width)
    {
        if (font.MeasureString(text).X <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && font.MeasureString(cut + "…").X > width)
        {
            cut = cut[..^1];
        }

        return cut + "…";
    }
}
