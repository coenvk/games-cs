using System.Diagnostics;
using System.Globalization;
using Composer;
using FontStashSharp;
using Jentl.Core.Audio;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Games.Input;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The Composer's editor, in the manner of Bosca Ceoil. At the top the song's settings; in the middle the piano roll
/// of the pattern being written (press a cell for a note, drag right to hold it, press it again to take it away),
/// with the pattern's controls beside it; at the bottom the arrangement, a row per channel and a column per bar
/// (press a bar to put the pattern in it or take it out, or a bar of another channel to pick that channel and its
/// pattern). Space plays and stops; while it plays, changes are heard from the bar playing.
/// </summary>
internal sealed class ComposerScene : Scene
{
    private const string Track = "composer-song";
    private const int RollRows = 24;
    private const int BarsShown = 16;

    private static readonly Color[] ChannelColours =
    [
        new(255, 199, 44), new(110, 200, 255), new(140, 220, 120), new(240, 120, 110),
        new(200, 150, 255), new(255, 160, 80), new(120, 230, 210), new(240, 140, 200),
    ];

    private static readonly string[] KeyNames = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    private readonly GameContext _context;
    private readonly ButtonBar _bar;
    private readonly List<(Rectangle Area, Action Press)> _hits = [];
    private readonly HashSet<string> _auditioned = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = new();
    private Rectangle _roll;
    private Rectangle _arrangement;
    private int _rowHeight;
    private int _lowest = 55;
    private int _firstBar;
    private bool _playing;
    private int _playFrom;
    private int _loopFrom;
    private int _loopLength;
    private string? _heard;
    private bool _wasDown;
    private Drag _drag;
    private (int Step, int Pitch)? _dragFrom;
    private (int Step, int Pitch)? _dragTo;
    private List<ComposedNote> _dragged = [];
    private (int Steps, int Semitones) _dragMoved;
    private bool _dragChanged;
    private List<ComposedNote> _selected = [];
    private bool _selecting;

    /// <summary>What a press on the roll does as the pointer moves.</summary>
    private enum Drag
    {
        /// <summary>A new note drawn out to its length.</summary>
        Draw,

        /// <summary>A note's end pulled to a new length.</summary>
        Resize,

        /// <summary>A note, or the notes selected, moved in time and pitch.</summary>
        Move,

        /// <summary>A box drawn round the notes to select.</summary>
        Select,
    }
    private (int Channel, int Pattern)? _shown;

    public ComposerScene(GameContext context, ComposerSession session)
    {
        _context = context;
        Session = session;
        _bar = new ButtonBar();
        SetButtons();
    }

    public ComposerSession Session { get; }

    public override IEnumerable<string> DebugInfo =>
    [
        string.Create(CultureInfo.InvariantCulture, $"{Session.Song.Title}: channel {Session.Channel + 1}, pattern {Session.Pattern + 1}, {Session.Song.Bars} bars at {Session.Song.Tempo}"),
        _playing ? string.Create(CultureInfo.InvariantCulture, $"Playing step {PlayStep()} from bar {_playFrom + 1}") : "Stopped",
    ];

    private Composition Song => Session.Song;

    private static Keys Command => OperatingSystem.IsMacOS() ? Keys.LeftWindows : Keys.LeftControl;

    protected override void OnEnter() => _context.Sounds.StopMusic();

    /// <summary>When another pattern comes on the roll, the roll moves to show its notes (or middle C, for an empty one).</summary>
    private void FollowPattern()
    {
        if (_shown == (Session.Channel, Session.Pattern))
        {
            return;
        }

        _shown = (Session.Channel, Session.Pattern);
        _selected.Clear();
        var pitches = Session.CurrentPattern.Notes.Select(n => n.Pitch).ToList();
        var middle = pitches.Count == 0 ? 60 : (pitches.Min() + pitches.Max()) / 2;
        _lowest = Math.Clamp(middle - (RollRows / 2), 12, 108 - RollRows);
    }

    protected override void OnExit() => Stop();

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            if (_selected.Count > 0 || _selecting)
            {
                // Back lets go of the notes selected before it opens the menu.
                _selected.Clear();
                _selecting = false;
                return;
            }

            Scenes.Push(new SongMenuScene(_context, this));
            return;
        }

        if (_bar.Update(input))
        {
            return;
        }

        Keyboard(input);
        var pointer = input.Cursor;
        var pressed = pointer.IsDown && !_wasDown;
        _wasDown = pointer.IsDown;
        if (pressed && _roll.Contains(pointer.Position) && Cell(pointer.Position) is { } cell)
        {
            Press(cell, pointer.Position, _selecting || input.IsDown(Keys.LeftShift) || input.IsDown(Keys.RightShift));
        }
        else if (pointer.IsDown && _dragFrom is not null && Cell(pointer.Position) is { } to)
        {
            DragTo(to);
        }
        else if (!pointer.IsDown && _dragFrom is not null)
        {
            Release();
        }

        if (input.Tap is { } tap && !_roll.Contains(tap))
        {
            foreach (var (area, press) in _hits.ToList())
            {
                if (area.Contains(tap))
                {
                    var before = Song.ToJson();
                    press();
                    Changed(before);
                    _context.Sounds.Play(Sound.Click);
                    break;
                }
            }
        }

        if (input.Wheel != 0 && _roll.Contains(pointer.Position))
        {
            _lowest = Math.Clamp(_lowest + Math.Sign(input.Wheel) * 2, 12, 108 - RollRows);
        }

        if (_playing && !_clock.IsRunning && _context.Sounds.IsPlayingMusic(Track))
        {
            _clock.Restart();
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        _hits.Clear();
        var fonts = _context.Fonts;
        var small = fonts.Small;
        var pad = fonts.Px(10);
        batch.FillRectangle(viewport, Palette.Background);

        // The song's settings along the top, the buttons at its right.
        var line = Math.Max((int)small.LineHeight + pad, ButtonBar.Height(small));
        var buttons = _bar.Width(small);
        var settings = new Rectangle(viewport.X + pad, viewport.Y + pad, viewport.Width - (3 * pad) - buttons, line);
        _bar.Draw(batch, small, new Rectangle(settings.Right + pad, settings.Y, buttons, line));
        DrawSettings(batch, small, settings, pad);
        FollowPattern();

        var side = Math.Max(fonts.Px(230), viewport.Width / 5);
        var body = new Rectangle(viewport.X + pad, settings.Bottom + pad, viewport.Width - (2 * pad), viewport.Bottom - settings.Bottom - (2 * pad));
        _rowHeight = Math.Clamp((body.Height / 2) / (Song.Channels.Count + 2), fonts.Px(22), fonts.Px(40));
        var arrangementHeight = _rowHeight * (Song.Channels.Count + 2);
        var rollArea = new Rectangle(body.X, body.Y, body.Width, body.Height - arrangementHeight - pad);
        DrawPatternTools(batch, small, new Rectangle(rollArea.X, rollArea.Y, side - pad, rollArea.Height), pad);
        DrawRoll(batch, small, new Rectangle(rollArea.X + side, rollArea.Y, rollArea.Width - side, rollArea.Height));
        DrawArrangement(batch, small, new Rectangle(body.X, rollArea.Bottom + pad, body.Width, arrangementHeight), side);
    }

    /// <summary>The bars looped over while working on them (none: the whole song), kept inside the song.</summary>
    private (int From, int To) Looped => _loopLength > 0
        ? (Math.Min(_loopFrom, Song.Bars - 1), Math.Min(Song.Bars, Math.Min(_loopFrom, Song.Bars - 1) + _loopLength))
        : (0, Song.Bars);

    /// <summary>Loops one bar, then two, then four from the bar chosen, then the whole song again.</summary>
    private void CycleLoop()
    {
        _loopLength = _loopLength switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            _ => 0,
        };
        if (_loopLength == 1)
        {
            _loopFrom = _firstBar;
        }

        if (_playing)
        {
            Play(_loopFrom);
        }
    }

    /// <summary>The song as it is now, heard from the bar playing; called after every change while playing.</summary>
    public void Play(int fromBar)
    {
        var (from, to) = Looped;
        _playFrom = fromBar >= from && fromBar < to ? fromBar : from;
        var tune = Song.ToTune(from, to);
        var offset = (int)Math.Round((_playFrom - from) * Song.StepsPerBar * tune.StepSeconds * Synth.SampleRate);
        _context.Sounds.ReplaceMusic(Track, () => Rotate(Synth.Render(tune, loop: true), offset));
        _context.Sounds.PlayMusic(Track, TimeSpan.Zero);
        _heard = Song.ToJson();
        _playing = true;
        _clock.Reset();
        SetButtons();
    }

    public void Stop()
    {
        if (_playing)
        {
            _context.Sounds.StopMusic(TimeSpan.FromSeconds(0.2));
            _playing = false;
            _clock.Reset();
            SetButtons();
        }
    }

    /// <summary>The loop started at a later sample: a song heard from one of its bars, round to the start again.</summary>
    private static short[] Rotate(short[] samples, int offset)
    {
        if (offset <= 0 || offset >= samples.Length)
        {
            return samples;
        }

        return [.. samples.AsSpan(offset), .. samples.AsSpan(0, offset)];
    }

    private void SetButtons() => _bar.Set(
        new Button(_playing ? T("Stop") : T("Play"), () =>
        {
            if (_playing)
            {
                Stop();
            }
            else
            {
                Play(_firstBar);
            }
        }, Highlight: true),
        new Button(T("Undo"), () => Edit(Session.Undo)),
        new Button(T("Redo"), () => Edit(Session.Redo)),
        new Button(T("Menu"), () => Scenes.Push(new SongMenuScene(_context, this))));

    private void Edit(Action change)
    {
        var before = Song.ToJson();
        change();
        Changed(before);
    }

    /// <summary>After a change: heard at once while playing.</summary>
    private void Changed(string before)
    {
        if (_playing && Song.ToJson() != before && Song.ToJson() != _heard)
        {
            Play(PlayStep() / Song.StepsPerBar);
        }
    }

    private void Keyboard(InputState input)
    {
        var command = input.IsDown(Command) || input.IsDown(Keys.LeftControl);
        if (input.WasPressed(Keys.Space))
        {
            if (_playing)
            {
                Stop();
            }
            else
            {
                Play(_firstBar);
            }
        }
        else if (!command && input.WasPressed(Keys.L))
        {
            CycleLoop();
        }
        else if (command && input.WasPressed(Keys.Z))
        {
            Edit(input.IsDown(Keys.LeftShift) ? Session.Redo : Session.Undo);
        }
        else if (command && input.WasPressed(Keys.Y))
        {
            Edit(Session.Redo);
        }
        else if (command && input.WasPressed(Keys.S))
        {
            _context.Get<SongShelf>().Save(Song);
            Session.Saved();
        }
        else if (command && input.WasPressed(Keys.A))
        {
            _selected = [.. Session.CurrentPattern.Notes];
        }
        else if (input.WasPressed(Keys.Delete) || input.WasPressed(Keys.Back))
        {
            DeleteSelected();
        }
        else if (_selected.Count > 0 && (input.WasPressed(Keys.Up) || input.WasPressed(Keys.Down)))
        {
            var octave = input.IsDown(Keys.LeftShift) || input.IsDown(Keys.RightShift) ? 12 : 1;
            Nudge(0, input.WasPressed(Keys.Up) ? octave : -octave);
        }
        else if (_selected.Count > 0 && (input.WasPressed(Keys.Left) || input.WasPressed(Keys.Right)))
        {
            Nudge(input.WasPressed(Keys.Right) ? 1 : -1, 0);
        }
        else if (input.WasPressed(Keys.Up))
        {
            _lowest = Math.Min(_lowest + 12, 108 - RollRows);
        }
        else if (input.WasPressed(Keys.Down))
        {
            _lowest = Math.Max(_lowest - 12, 12);
        }
        else if (input.WasPressed(Keys.Left))
        {
            Session.SelectPattern(Session.Pattern - 1);
        }
        else if (input.WasPressed(Keys.Right))
        {
            Session.SelectPattern(Session.Pattern + 1);
        }
    }

    /// <summary>
    /// A press on the roll: on empty grid a new note (drawn out by dragging), on a note's end its length, on a note
    /// the note or the notes selected with it to move; with Shift held or Select on, a box to select notes with.
    /// </summary>
    private void Press((int Step, int Pitch) cell, Vector2 at, bool selecting)
    {
        (_dragFrom, _dragTo, _dragMoved, _dragChanged) = (cell, cell, (0, 0), false);
        if (selecting)
        {
            _drag = Drag.Select;
            SelectBetween(cell, cell);
            return;
        }

        if (Session.NoteAt(cell.Step, cell.Pitch) is { } note)
        {
            // One undo for the whole drag, or for the tap that takes the note away.
            Session.BeginEdit();
            var cellWidth = _roll.Width / (float)Song.StepsPerBar;
            var end = _roll.X + (Math.Min(note.Step + note.Length, Song.StepsPerBar) * cellWidth);
            var resize = at.X >= end - (cellWidth * 0.35f);
            _drag = resize ? Drag.Resize : Drag.Move;
            _dragged = !resize && _selected.Contains(note) ? [.. _selected] : [note];
            return;
        }

        _selected.Clear();
        var before = Song.ToJson();
        if (Session.Toggle(cell.Step, cell.Pitch) && Session.NoteAt(cell.Step, cell.Pitch) is { } added)
        {
            (_drag, _dragged) = (Drag.Draw, [added]);
            Audition(cell.Pitch);
        }
        else
        {
            _dragFrom = null;
        }

        Changed(before);
    }

    private void DragTo((int Step, int Pitch) to)
    {
        if (_dragFrom is not { } from || to == _dragTo)
        {
            return;
        }

        _dragTo = to;
        var before = Song.ToJson();
        switch (_drag)
        {
            case Drag.Draw or Drag.Resize:
                var note = _dragged[0];
                var length = Math.Max(note.Step, to.Step) - note.Step + 1;
                if (length != note.Length)
                {
                    _dragged = [Session.Resize(note, length)];
                    _dragChanged = true;
                }

                break;
            case Drag.Move:
                var wasSelected = _dragged.All(_selected.Contains) && _selected.Count > 0;
                var moved = Session.Move(_dragged, to.Step - from.Step - _dragMoved.Steps, to.Pitch - from.Pitch - _dragMoved.Semitones);
                if (moved.Count > 0)
                {
                    // Where they really went: the bar's ends and the roll's stop them.
                    var (steps, semitones) = (moved[0].Step - _dragged[0].Step, moved[0].Pitch - _dragged[0].Pitch);
                    _dragMoved = (_dragMoved.Steps + steps, _dragMoved.Semitones + semitones);
                    _dragChanged |= steps != 0 || semitones != 0;
                    if (semitones != 0)
                    {
                        Audition(moved[0].Pitch);
                    }

                    _dragged = [.. moved];
                    if (wasSelected)
                    {
                        _selected = [.. moved];
                    }
                }

                break;
            case Drag.Select:
                SelectBetween(from, to);
                break;
        }

        Changed(before);
    }

    /// <summary>The pointer let go: a note tapped without moving it goes, as tapping a note always did.</summary>
    private void Release()
    {
        if (_drag is Drag.Move or Drag.Resize && !_dragChanged && _dragFrom is { } from && Session.NoteAt(from.Step, from.Pitch) is { } tapped)
        {
            var before = Song.ToJson();
            Session.Remove([tapped]);
            _selected.Remove(tapped);
            Changed(before);
        }

        (_dragFrom, _dragTo, _dragged) = (null, null, []);
    }

    /// <summary>Selects the notes sounding in the box between two cells.</summary>
    private void SelectBetween((int Step, int Pitch) a, (int Step, int Pitch) b)
    {
        var (first, last) = (Math.Min(a.Step, b.Step), Math.Max(a.Step, b.Step));
        var (low, high) = (Math.Min(a.Pitch, b.Pitch), Math.Max(a.Pitch, b.Pitch));
        _selected = [.. Session.CurrentPattern.Notes.Where(n => n.Pitch >= low && n.Pitch <= high && n.Step <= last && n.Step + n.Length - 1 >= first)];
    }

    /// <summary>Moves the notes selected with the arrow keys, a step or a semitone (an octave with Shift).</summary>
    private void Nudge(int steps, int semitones)
    {
        var before = Song.ToJson();
        Session.BeginEdit();
        _selected = [.. Session.Move(_selected, steps, semitones)];
        Changed(before);
        if (semitones != 0 && _selected.Count > 0)
        {
            Audition(_selected[0].Pitch);
        }
    }

    private void DeleteSelected()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        var before = Song.ToJson();
        Session.BeginEdit();
        Session.Remove(_selected);
        _selected.Clear();
        Changed(before);
    }

    /// <summary>The roll's step and pitch under a point, if it is on the grid.</summary>
    private (int Step, int Pitch)? Cell(Vector2 point)
    {
        var columns = Song.StepsPerBar;
        if (_roll.Width <= 0 || point.Y < _roll.Top || point.Y >= _roll.Bottom)
        {
            return null;
        }

        var step = Math.Clamp((int)((point.X - _roll.X) / (_roll.Width / (float)columns)), 0, columns - 1);
        var row = (int)((point.Y - _roll.Y) / (_roll.Height / (float)RollRows));
        return (step, _lowest + RollRows - 1 - Math.Clamp(row, 0, RollRows - 1));
    }

    /// <summary>Plays the note just placed, on the channel's instrument (each rendered once).</summary>
    private void Audition(int pitch)
    {
        var name = $"composer-{Session.Current.Instrument}-{Session.Current.Volume:0.0}-{pitch}";
        if (_auditioned.Add(name))
        {
            _context.Sounds.Add(name, Synth.Render(new Tune(120, new Voice(Session.Current.Sound(), $"{Composition.NoteName(pitch)} -"))));
        }

        _context.Sounds.Play(name);
    }

    private int PlayStep()
    {
        if (!_clock.IsRunning)
        {
            return _playFrom * Song.StepsPerBar;
        }

        // Round the bars looped over, or the whole song.
        var (from, to) = Looped;
        var stepSeconds = 60.0 / Song.Tempo / Song.StepsPerBeat;
        var total = Math.Max(1, (to - from) * Song.StepsPerBar);
        return (from * Song.StepsPerBar) + ((((_playFrom - from) * Song.StepsPerBar) + (int)(_clock.Elapsed.TotalSeconds / stepSeconds)) % total);
    }

    private Rectangle Chip(SpriteBatch batch, SpriteFontBase font, ref int x, Rectangle row, string text, Action? press, bool lit = false)
    {
        var pad = _context.Fonts.Px(8);
        var width = (int)font.MeasureString(text).X + (2 * pad);
        var area = new Rectangle(x, row.Y, width, row.Height);
        batch.FillRoundedRectangle(area, _context.Fonts.Px(6), lit ? Palette.Accent : press is null ? Palette.Panel : Palette.PanelLight);
        batch.DrawText(font, text, area, lit ? Palette.Ink : Palette.Text, Align.Center, shadow: false);
        if (press is not null)
        {
            _hits.Add((area, press));
        }

        x += width + (pad / 2);
        return area;
    }

    private void DrawSettings(SpriteBatch batch, SpriteFontBase font, Rectangle row, int pad)
    {
        var x = row.X;
        Chip(batch, font, ref x, row, Song.Title + (Session.IsChanged ? " *" : ""), null);
        x += pad;
        Chip(batch, font, ref x, row, "−", () => Session.ChangeTempo(-5));
        Chip(batch, font, ref x, row, T("{0} bpm", Song.Tempo), null);
        Chip(batch, font, ref x, row, "+", () => Session.ChangeTempo(5));
        x += pad;
        Chip(batch, font, ref x, row, "−", () => Session.ChangeBars(-1));
        Chip(batch, font, ref x, row, T("{0} bars", Song.Bars), null);
        Chip(batch, font, ref x, row, "+", () => Session.ChangeBars(1));
        x += pad;
        Chip(batch, font, ref x, row, Song.BeatsPerBar == 3 ? "3/4" : "4/4", Session.ToggleMetre);
        x += pad;
        Chip(batch, font, ref x, row, "−", () => Session.ChangeReverb(-0.05f));
        Chip(batch, font, ref x, row, T("Echo {0}", ((int)Math.Round(Song.Reverb * 100)).ToString(CultureInfo.InvariantCulture)), null);
        Chip(batch, font, ref x, row, "+", () => Session.ChangeReverb(0.05f));
        x += pad;
        Chip(batch, font, ref x, row, "−", () => Session.ChangeSwing(-0.05f));
        Chip(batch, font, ref x, row, T("Swing {0}", ((int)Math.Round(Song.Swing * 100)).ToString(CultureInfo.InvariantCulture)), null);
        Chip(batch, font, ref x, row, "+", () => Session.ChangeSwing(0.05f));
        x += pad;
        Chip(batch, font, ref x, row, KeyNames[Session.Key], () => Session.Key = (Session.Key + 1) % 12);
        Chip(batch, font, ref x, row, ScaleName(Session.Scale), () => Session.Scale = (ScaleKind)(((int)Session.Scale + 1) % 5));
    }

    private static string ScaleName(ScaleKind scale) => scale switch
    {
        ScaleKind.Major => T("major"),
        ScaleKind.Minor => T("minor"),
        ScaleKind.Pentatonic => T("pentatonic"),
        ScaleKind.Dorian => T("dorian"),
        _ => T("all notes"),
    };

    private void DrawPatternTools(SpriteBatch batch, SpriteFontBase font, Rectangle area, int pad)
    {
        var height = (int)font.LineHeight + pad;
        var colour = ChannelColours[Session.Channel % ChannelColours.Length];
        var y = area.Y;
        Rectangle Row() => new(area.X, y, area.Width, height);
        batch.FillRoundedRectangle(Row(), _context.Fonts.Px(6), colour * 0.25f);
        batch.DrawText(font, Session.Current.Instrument, Row(), colour, Align.Center, shadow: false);
        y += height + (pad / 2);

        var x = area.X;
        Chip(batch, font, ref x, Row(), "<", () => Session.SelectPattern(Session.Pattern - 1));
        Chip(batch, font, ref x, Row(), T("Pattern {0}/{1}", Session.Pattern + 1, Session.Current.Patterns.Count), null);
        Chip(batch, font, ref x, Row(), ">", () => Session.SelectPattern(Session.Pattern + 1));
        y += height + (pad / 2);

        x = area.X;
        Chip(batch, font, ref x, Row(), T("New"), () => Session.NewPattern(copy: false));
        Chip(batch, font, ref x, Row(), T("Copy"), () => Session.NewPattern(copy: true));
        Chip(batch, font, ref x, Row(), T("Clear"), Session.ClearPattern);
        y += height + (pad / 2);

        x = area.X;
        Chip(batch, font, ref x, Row(), T("Notes"), null);
        Chip(batch, font, ref x, Row(), "−1", () => Session.Transpose(-1));
        Chip(batch, font, ref x, Row(), "+1", () => Session.Transpose(1));
        Chip(batch, font, ref x, Row(), "+12", () => Session.Transpose(12));
        y += height + (pad / 2);

        x = area.X;
        Chip(batch, font, ref x, Row(), T("View"), null);
        Chip(batch, font, ref x, Row(), T("Higher"), () => _lowest = Math.Min(_lowest + 12, 108 - RollRows));
        Chip(batch, font, ref x, Row(), T("Lower"), () => _lowest = Math.Max(_lowest - 12, 12));
        y += height + (pad / 2);

        // Selecting by touch: a box drawn on the roll, then the notes dragged together or taken away.
        x = area.X;
        Chip(batch, font, ref x, Row(), T("Select"), () => _selecting = !_selecting, lit: _selecting);
        if (_selected.Count > 0)
        {
            Chip(batch, font, ref x, Row(), T("Delete {0}", _selected.Count), DeleteSelected);
        }
    }

    private void DrawRoll(SpriteBatch batch, SpriteFontBase font, Rectangle area)
    {
        var labels = _context.Fonts.Px(46);
        _roll = new Rectangle(area.X + labels, area.Y, area.Width - labels, area.Height);
        var columns = Song.StepsPerBar;
        var cellWidth = _roll.Width / (float)columns;
        var cellHeight = _roll.Height / (float)RollRows;
        var colour = ChannelColours[Session.Channel % ChannelColours.Length];
        var label = _context.Fonts.Sized(Math.Min(cellHeight * 0.8f, font.LineHeight));
        for (var row = 0; row < RollRows; row++)
        {
            var pitch = _lowest + RollRows - 1 - row;
            var y = _roll.Y + (int)(row * cellHeight);
            var band = new Rectangle(_roll.X, y, _roll.Width, (int)Math.Ceiling(cellHeight));
            var black = KeyNames[((pitch % 12) + 12) % 12].Length > 1;
            batch.FillRectangle(band, Session.InScale(pitch) ? (black ? Palette.PanelLight * 0.8f : Palette.PanelLight) : Palette.Panel * 0.7f);
            if (pitch % 12 == 0 || row == RollRows - 1)
            {
                batch.DrawText(label, Composition.NoteName(pitch), new Rectangle(area.X, y, labels - 4, band.Height), Palette.Muted, Align.Right, shadow: false);
            }

            batch.FillRectangle(new Rectangle(_roll.X, y, _roll.Width, 1), Palette.Background * 0.6f);
        }

        for (var step = 0; step <= columns; step++)
        {
            var x = _roll.X + (int)(step * cellWidth);
            var beat = step % Song.StepsPerBeat == 0;
            batch.FillRectangle(new Rectangle(x, _roll.Y, beat ? 2 : 1, _roll.Height), Palette.Background * (beat ? 0.9f : 0.4f));
        }

        if (_playing && _clock.IsRunning)
        {
            var bar = PlayStep() / Song.StepsPerBar;
            if (Session.Current.Arrangement.ElementAtOrDefault(bar) == Session.Pattern)
            {
                var x = _roll.X + (int)((PlayStep() % columns) * cellWidth);
                batch.FillRectangle(new Rectangle(x, _roll.Y, (int)cellWidth, _roll.Height), Palette.Text * 0.12f);
            }
        }

        foreach (var note in Session.CurrentPattern.Notes)
        {
            var row = _lowest + RollRows - 1 - note.Pitch;
            if (row < 0 || row >= RollRows)
            {
                continue;
            }

            var cell = new Rectangle(
                _roll.X + (int)(note.Step * cellWidth) + 1,
                _roll.Y + (int)(row * cellHeight) + 1,
                Math.Max(2, (int)(Math.Min(note.Length, columns - note.Step) * cellWidth) - 2),
                Math.Max(2, (int)cellHeight - 2));
            batch.FillRoundedRectangle(cell, Math.Min(4, cell.Height / 3), colour);

            // The end to pull, and the notes selected outlined.
            var grip = Math.Max(2, (int)(cellWidth * 0.12f));
            batch.FillRectangle(new Rectangle(cell.Right - grip - 1, cell.Y + (cell.Height / 4), grip, cell.Height / 2), Palette.Ink * 0.35f);
            if (_selected.Contains(note))
            {
                batch.StrokeRectangle(cell, Palette.Text, 2);
            }
        }

        if (_drag == Drag.Select && _dragFrom is { } from && _dragTo is { } to)
        {
            var left = _roll.X + (int)(Math.Min(from.Step, to.Step) * cellWidth);
            var right = _roll.X + (int)((Math.Max(from.Step, to.Step) + 1) * cellWidth);
            var top = _roll.Y + (int)((_lowest + RollRows - 1 - Math.Max(from.Pitch, to.Pitch)) * cellHeight);
            var bottom = _roll.Y + (int)((_lowest + RollRows - Math.Min(from.Pitch, to.Pitch)) * cellHeight);
            var box = new Rectangle(left, top, right - left, bottom - top);
            batch.FillRectangle(box, Palette.Accent * 0.15f);
            batch.StrokeRectangle(box, Palette.Accent, 2);
        }
    }

    private void DrawArrangement(SpriteBatch batch, SpriteFontBase font, Rectangle area, int side)
    {
        _arrangement = new Rectangle(area.X + side, area.Y + _rowHeight, area.Width - side, area.Height - (2 * _rowHeight));
        var columns = Math.Min(BarsShown, Song.Bars);
        _firstBar = Math.Clamp(_firstBar, 0, Math.Max(0, Song.Bars - columns));
        var cellWidth = _arrangement.Width / (float)columns;

        // The header: the bars shown, and arrows to the others.
        var header = new Rectangle(area.X, area.Y, side, _rowHeight - 2);
        var x = header.X;
        Chip(batch, font, ref x, header, "<", () => _firstBar = Math.Max(0, _firstBar - columns));
        Chip(batch, font, ref x, header, ">", () => _firstBar = Math.Min(Math.Max(0, Song.Bars - columns), _firstBar + columns));
        Chip(batch, font, ref x, header, _loopLength == 0 ? T("Loop") : _loopLength == 1 ? T("Loop 1 bar") : T("Loop {0} bars", _loopLength), CycleLoop, lit: _loopLength > 0);
        var playing = _playing && _clock.IsRunning ? PlayStep() / Song.StepsPerBar : -1;
        var (loopFrom, loopTo) = Looped;
        for (var i = 0; i < columns; i++)
        {
            var bar = _firstBar + i;
            var cell = new Rectangle(_arrangement.X + (int)(i * cellWidth), area.Y, (int)cellWidth - 2, _rowHeight - 2);
            if (_loopLength > 0 && bar >= loopFrom && bar < loopTo)
            {
                // The bars looped over: a band under their numbers.
                batch.FillRectangle(new Rectangle(cell.X, cell.Y, (int)cellWidth, cell.Height), Palette.Accent * 0.45f);
            }

            batch.DrawText(font, (bar + 1).ToString(CultureInfo.InvariantCulture), cell, bar == playing ? Palette.Accent : Palette.Muted, Align.Center, shadow: false);
            var start = bar;
            _hits.Add((cell, () =>
            {
                // A bar chosen: heard from there, and a loop moved to start there.
                _firstBar = start;
                if (_loopLength > 0)
                {
                    _loopFrom = start;
                }

                if (_playing)
                {
                    Play(start);
                }
            }));
        }

        for (var c = 0; c < Song.Channels.Count; c++)
        {
            var channel = Song.Channels[c];
            var colour = ChannelColours[c % ChannelColours.Length];
            var y = _arrangement.Y + (c * _rowHeight);
            var row = new Rectangle(area.X, y, side - _context.Fonts.Px(10), _rowHeight - 2);
            var index = c;
            batch.FillRoundedRectangle(row, _context.Fonts.Px(6), c == Session.Channel ? colour * 0.35f : Palette.Panel);
            x = row.X;
            Chip(batch, font, ref x, row, "<", () =>
            {
                Session.SelectChannel(index);
                Session.CycleInstrument(-1);
            });
            var name = new Rectangle(x, row.Y, row.Right - x - (int)font.MeasureString(">").X - _context.Fonts.Px(20), row.Height);
            batch.DrawText(font, channel.Instrument, name, colour, Align.Center, shadow: false);
            _hits.Add((name, () => Session.SelectChannel(index)));
            x = name.Right + _context.Fonts.Px(4);
            Chip(batch, font, ref x, row, ">", () =>
            {
                Session.SelectChannel(index);
                Session.CycleInstrument(1);
            });

            for (var i = 0; i < columns; i++)
            {
                var bar = _firstBar + i;
                var cell = new Rectangle(_arrangement.X + (int)(i * cellWidth), y, (int)cellWidth - 2, _rowHeight - 2);
                var pattern = channel.PatternAt(bar) is null ? -1 : channel.Arrangement[bar];
                batch.FillRectangle(cell, pattern >= 0 ? colour * (c == Session.Channel ? 0.9f : 0.6f) : Palette.Panel);
                if (pattern >= 0)
                {
                    batch.DrawText(font, (pattern + 1).ToString(CultureInfo.InvariantCulture), cell, Palette.Ink, Align.Center, shadow: false);
                }

                if (bar == playing)
                {
                    batch.FillRectangle(new Rectangle(cell.X, cell.Y, cell.Width, 3), Palette.Text);
                }

                var at = bar;
                _hits.Add((cell, () =>
                {
                    if (index == Session.Channel)
                    {
                        Session.Place(index, at);
                    }
                    else
                    {
                        Session.Pick(index, at);
                    }
                }));
            }
        }

        // Under the rows: the channel's volume, and channels added and taken away.
        var footer = new Rectangle(area.X, _arrangement.Bottom, area.Width, _rowHeight - 2);
        x = footer.X;
        Chip(batch, font, ref x, footer, T("+ Channel"), () => Session.AddChannel());
        Chip(batch, font, ref x, footer, T("− Channel"), Session.RemoveChannel);
        x += _context.Fonts.Px(10);
        Chip(batch, font, ref x, footer, "−", () => Session.ChangeVolume(-0.1f));
        Chip(batch, font, ref x, footer, T("Volume {0}%", ((int)Math.Round(Session.Current.Volume * 100)).ToString(CultureInfo.InvariantCulture)), null);
        Chip(batch, font, ref x, footer, "+", () => Session.ChangeVolume(0.1f));
    }
}
