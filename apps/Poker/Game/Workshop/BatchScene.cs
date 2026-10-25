using System.Diagnostics;
using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Multiplayer;
using Games.Input;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Poker.Arena;
using static Games.UI.Tr;

namespace Games.Workshop;

/// <summary>
/// Runs a batch without showing the hands: the part done as a bar, the hands (or tournaments) played and how many a
/// second, and Cancel, which stops after the hand being played and keeps what was played. The batch runs off the UI
/// thread. When it ends it is saved to the workshop's matches folder and its results open; leaving before then stops
/// it, saves nothing, and its bots are stopped once the hand in play is over.
/// </summary>
internal sealed class BatchScene : Scene
{
    private static readonly Color Problem = new(236, 120, 108);

    private readonly WorkshopContext _workshop;
    private readonly GameContext _context;
    private readonly BatchRequest _request;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Done _done = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ButtonBar _buttons = new();
    private readonly Task<MatchResult> _run;

    // Hands for a duplicate batch (each deal once per seating), tournaments for a series.
    private readonly int _total;
    private string? _error;
    private bool _finished;
    private bool _released;

    public BatchScene(WorkshopContext workshop, BatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(workshop);
        ArgumentNullException.ThrowIfNull(request);
        _workshop = workshop;
        _context = workshop.Game;
        _request = request;
        var settings = request.Settings;
        _total = IsSeries ? settings.Hands : settings.Hands * Seatings.Duplicate(request.Bots.Count).Count;

        // Every run seats fresh bots; a house bot gets the seed of its place in the list, so a batch can be run again.
        // A bot entered twice is numbered, so that each keeps its own results and statistics.
        var names = SeatNames.Distinct([.. request.Bots.Select(b => b.Name)]);
        var bots = request.Bots
            .Select((bot, i) => (Func<IArenaSeat>)(() => workshop.Seat(bot, unchecked((int)ArenaTable.SeatSeed(settings.Seed, 0, i)), names[i])))
            .ToList();
        var batch = new ArenaBatch(bots, settings);
        _run = Task.Run(() => batch.RunAsync(_done, _cancel.Token));
        SetButtons();
    }

    private bool IsSeries => _request.Settings.Format == BatchFormat.SitAndGoSeries;

    public override bool IsOverlay => true;

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

        if (_context.Actions.WasPressed(input, GameAction.Confirm))
        {
            if (_error is null)
            {
                Cancel();
            }
            else
            {
                Scenes.Pop();
            }

            return;
        }

        if (!_finished && _run.IsCompleted)
        {
            Finish();
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Dim);
        var padding = fonts.Px(40);
        var width = Math.Min(viewport.Width - 2 * padding, fonts.Px(900));
        var height = Math.Min(viewport.Height - fonts.Px(40), fonts.Px(400));
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Center.Y - height / 2, width, height);
        batch.FillRectangle(panel, Palette.Panel);
        batch.FillRectangle(new Rectangle(panel.X, panel.Y, panel.Width, 6), Palette.Accent);

        var y = panel.Y + fonts.Px(24);
        batch.DrawText(fonts.Heading, T("Running a batch"), new Vector2(panel.Center.X, y), Palette.Text, Align.Center);
        y += (int)fonts.Heading.LineHeight + fonts.Px(12);
        var inner = width - 2 * padding;
        foreach (var line in fonts.Small.Wrap($"{ResultsScene.FormatName(_request.Settings)}: {string.Join(", ", _request.Bots.Select(b => b.Name))}", inner))
        {
            batch.DrawText(fonts.Small, line, new Vector2(panel.Center.X, y), Palette.Muted, Align.Center, shadow: false);
            y += (int)fonts.Small.LineHeight + 2;
        }

        // The bar, filled as far as the batch has got.
        y += fonts.Px(16);
        var bar = new Rectangle(panel.X + padding, y, inner, fonts.Px(28));
        var part = _done.Value;
        batch.FillRectangle(bar, Palette.PanelLight);
        batch.FillRectangle(new Rectangle(bar.X, bar.Y, (int)(bar.Width * part), bar.Height), Palette.Accent);
        y = bar.Bottom + fonts.Px(16);

        var played = (int)Math.Round(part * _total);
        var seconds = _clock.Elapsed.TotalSeconds;
        var count = IsSeries
            ? T("{0} of {1} tournaments", played.ToString("N0", Tr.Culture), _total.ToString("N0", Tr.Culture))
            : T("{0} of {1} hands", played.ToString("N0", Tr.Culture), _total.ToString("N0", Tr.Culture));
        var rate = seconds < 0.25 ? "" : IsSeries
            ? T("{0} tournaments a minute", (played / seconds * 60).ToString("N1", Tr.Culture))
            : T("{0} hands a second", (played / seconds).ToString("N0", Tr.Culture));
        batch.DrawText(fonts.Body, count, new Vector2(panel.Center.X, y), Palette.Text, Align.Center);
        y += (int)fonts.Body.LineHeight + fonts.Px(4);
        var time = TimeSpan.FromSeconds(Math.Floor(seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
        batch.DrawText(fonts.Small, rate.Length == 0 ? time : $"{rate}  ·  {time}", new Vector2(panel.Center.X, y), Palette.Muted, Align.Center, shadow: false);
        y += (int)fonts.Small.LineHeight + fonts.Px(12);

        var status = _error ?? (_cancel.IsCancellationRequested ? T("Stopping after this hand") : null);
        if (status is not null)
        {
            foreach (var line in fonts.Small.Wrap(status, inner))
            {
                batch.DrawText(fonts.Small, line, new Vector2(panel.Center.X, y), _error is null ? Palette.Muted : Problem, Align.Center, shadow: false);
                y += (int)fonts.Small.LineHeight + 2;
            }
        }

        var buttons = new Rectangle(panel.X + padding, panel.Bottom - padding - ButtonBar.Height(fonts.Body), inner, ButtonBar.Height(fonts.Body));
        _buttons.Draw(batch, fonts.Body, buttons);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Release();
        }

        base.Dispose(disposing);
    }

    private void SetButtons() => _buttons.Set(
        _error is not null ? [new Button(T("Back"), () => Scenes.Pop(), Highlight: true)]
        : _cancel.IsCancellationRequested ? []
        : [new Button(T("Cancel"), Cancel, Highlight: true)]);

    // The batch stops between hands and hands back what was played, which is then saved and shown.
    private void Cancel()
    {
        if (!_cancel.IsCancellationRequested)
        {
            _cancel.Cancel();
            SetButtons();
        }
    }

    private void Finish()
    {
        _finished = true;
        if (_run.IsFaulted)
        {
            _error = T("The batch stopped: {0}", _run.Exception!.GetBaseException().Message);
            SetButtons();
            return;
        }

        var match = _run.Result;
        string? path;
        try
        {
            path = MatchFile.Save(_workshop.MatchesFolder, match);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            path = null;
        }

        Scenes.Replace(_workshop.MatchResults(match, path));
    }

    /// <summary>
    /// Stops a batch that is still running: it ends after the hand in play, disposing its seats and so the bots'
    /// processes, and nothing waits for it any more.
    /// </summary>
    private void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _cancel.Cancel();
        _run.ContinueWith(
            finished =>
            {
                // Looked at here so that a failure after the player left is not left unobserved.
                _ = finished.Exception;
                _cancel.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    /// <summary>The part of the batch done, as the batch reports it from its own threads; it only goes up.</summary>
    private sealed class Done : IProgress<double>
    {
        private double _value;

        public double Value => Volatile.Read(ref _value);

        public void Report(double value)
        {
            var seen = Volatile.Read(ref _value);
            while (value > seen)
            {
                var was = Interlocked.CompareExchange(ref _value, value, seen);
                if (was == seen)
                {
                    return;
                }

                seen = was;
            }
        }
    }
}
