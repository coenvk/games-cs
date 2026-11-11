using Jentl.Core.Audio;

namespace Composer;

/// <summary>Which notes a piano roll lights up as belonging; the rest are still allowed.</summary>
public enum ScaleKind
{
    Chromatic,
    Major,
    Minor,
    Pentatonic,
    Dorian,
}

/// <summary>
/// A song being written: the <see cref="Composition"/>, which channel and pattern are being edited, the key the roll
/// shows, and every change undoable. Each change takes a snapshot first; <see cref="Undo"/> and <see cref="Redo"/>
/// move between them.
/// </summary>
public sealed class ComposerSession
{
    public const int MaxChannels = 8;
    public const int MaxUndo = 200;

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public ComposerSession(Composition song)
    {
        Song = song ?? throw new ArgumentNullException(nameof(song));
        if (Song.Channels.Count == 0)
        {
            Song.Channels.Add(new Channel());
        }
    }

    public Composition Song { get; private set; }

    /// <summary>The channel being edited.</summary>
    public int Channel { get; private set; }

    /// <summary>The pattern of <see cref="Channel"/> on the piano roll.</summary>
    public int Pattern { get; private set; }

    /// <summary>The key's root (0 is C) and its scale, for the roll's shading.</summary>
    public int Key { get; set; }

    public ScaleKind Scale { get; set; } = ScaleKind.Major;

    /// <summary>Whether anything changed since the song was last saved.</summary>
    public bool IsChanged { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public Channel Current => Song.Channels[Channel];

    public Pattern CurrentPattern => Current.Patterns[Pattern];

    public static IReadOnlyList<string> InstrumentNames { get; } = [.. Instruments.All.Keys];

    /// <summary>A new song with a channel each for a melody, chords, a bass and drums.</summary>
    public static Composition Blank() => new()
    {
        Channels =
        {
            new Channel { Instrument = "Flute", Arrangement = { 0, 0, 0, 0, 0, 0, 0, 0 } },
            new Channel { Instrument = "Harp", Arrangement = { 0, 0, 0, 0, 0, 0, 0, 0 } },
            new Channel { Instrument = "Cello", Arrangement = { 0, 0, 0, 0, 0, 0, 0, 0 } },
            new Channel { Instrument = "Frame drum", Arrangement = { 0, 0, 0, 0, 0, 0, 0, 0 } },
        },
    };

    /// <summary>A short example song to start from: a tune over harp chords, a bass and a drum.</summary>
    public static Composition Example()
    {
        var song = new Composition { Title = "Example", Tempo = 100, Bars = 8, Reverb = 0.3f };
        var melody = new Channel { Instrument = "Flute" };
        melody.Patterns[0].Notes.AddRange([new(0, 76, 4), new(4, 79, 4), new(8, 81, 6), new(14, 79, 2)]);
        melody.Patterns.Add(new Pattern());
        melody.Patterns[1].Notes.AddRange([new(0, 76, 4), new(4, 74, 4), new(8, 72, 8)]);
        melody.Arrangement.AddRange([0, 1, 0, 1, 0, 1, 0, 1]);

        var harp = new Channel { Instrument = "Harp" };
        harp.Patterns[0].Notes.AddRange(Arpeggio(57, 60, 64)); // A minor
        harp.Patterns.Add(new Pattern());
        harp.Patterns[1].Notes.AddRange(Arpeggio(53, 57, 60)); // F
        harp.Patterns.Add(new Pattern());
        harp.Patterns[2].Notes.AddRange(Arpeggio(48, 52, 55)); // C
        harp.Patterns.Add(new Pattern());
        harp.Patterns[3].Notes.AddRange(Arpeggio(55, 59, 62)); // G
        harp.Arrangement.AddRange([0, 1, 2, 3, 0, 1, 2, 3]);

        var bass = new Channel { Instrument = "Cello" };
        foreach (var root in new[] { 45, 41, 48, 43 })
        {
            var pattern = new Pattern();
            pattern.Notes.Add(new ComposedNote(0, root, 16));
            bass.Patterns.Add(pattern);
        }

        bass.Patterns.RemoveAt(0);
        bass.Arrangement.AddRange([0, 1, 2, 3, 0, 1, 2, 3]);

        var drum = new Channel { Instrument = "Frame drum" };
        drum.Patterns[0].Notes.AddRange([new(0, 48), new(6, 48), new(8, 48), new(12, 48)]);
        drum.Arrangement.AddRange([-1, -1, 0, 0, 0, 0, 0, 0]);

        song.Channels.AddRange([melody, harp, bass, drum]);
        return song;

        static IEnumerable<ComposedNote> Arpeggio(int a, int b, int c) =>
            [new(0, a, 2), new(2, b, 2), new(4, c, 2), new(6, b, 2), new(8, a, 2), new(10, b, 2), new(12, c, 2), new(14, a + 12, 2)];
    }

    /// <summary>Whether <paramref name="pitch"/> is in the key.</summary>
    public bool InScale(int pitch)
    {
        int[] degrees = Scale switch
        {
            ScaleKind.Major => [0, 2, 4, 5, 7, 9, 11],
            ScaleKind.Minor => [0, 2, 3, 5, 7, 8, 10],
            ScaleKind.Pentatonic => [0, 2, 4, 7, 9],
            ScaleKind.Dorian => [0, 2, 3, 5, 7, 9, 10],
            _ => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
        };
        return degrees.Contains((((pitch - Key) % 12) + 12) % 12);
    }

    /// <summary>The note sounding at a step and pitch of the pattern on the roll (started there or held over it).</summary>
    public ComposedNote? NoteAt(int step, int pitch) =>
        CurrentPattern.Notes.LastOrDefault(n => n.Pitch == pitch && step >= n.Step && step < n.Step + n.Length);

    /// <summary>Removes the note at a step and pitch, or adds one a step long; true when a note was added.</summary>
    public bool Toggle(int step, int pitch)
    {
        if (step < 0 || step >= Song.StepsPerBar)
        {
            return false;
        }

        Change();
        if (NoteAt(step, pitch) is { } note)
        {
            CurrentPattern.Notes.Remove(note);
            return false;
        }

        CurrentPattern.Notes.Add(new ComposedNote(step, pitch, 1));
        return true;
    }

    /// <summary>Makes the note starting at <paramref name="step"/> last up to and including <paramref name="last"/> (drawn out on the roll).</summary>
    public void Stretch(int step, int pitch, int last)
    {
        var note = CurrentPattern.Notes.LastOrDefault(n => n.Pitch == pitch && n.Step == step);
        var length = Math.Clamp(last - step + 1, 1, Song.StepsPerBar * 4);
        if (note is null || note.Length == length)
        {
            return;
        }

        Change();
        CurrentPattern.Notes[CurrentPattern.Notes.IndexOf(note)] = note with { Length = length };
    }

    /// <summary>Moves every note of the pattern up or down by <paramref name="semitones"/>.</summary>
    public void Transpose(int semitones)
    {
        if (CurrentPattern.Notes.Count == 0)
        {
            return;
        }

        Change();
        var moved = CurrentPattern.Notes.Select(n => n with { Pitch = Math.Clamp(n.Pitch + semitones, 12, 108) }).ToList();
        CurrentPattern.Notes.Clear();
        CurrentPattern.Notes.AddRange(moved);
    }

    public void ClearPattern()
    {
        if (CurrentPattern.Notes.Count > 0)
        {
            Change();
            CurrentPattern.Notes.Clear();
        }
    }

    public void SelectChannel(int channel)
    {
        Channel = Math.Clamp(channel, 0, Song.Channels.Count - 1);
        Pattern = Math.Min(Pattern, Current.Patterns.Count - 1);
    }

    public void SelectPattern(int pattern) => Pattern = Math.Clamp(pattern, 0, Current.Patterns.Count - 1);

    /// <summary>A new empty pattern for the channel, or a copy of the one on the roll; it goes on the roll.</summary>
    public void NewPattern(bool copy)
    {
        Change();
        var pattern = new Pattern();
        if (copy)
        {
            pattern.Notes.AddRange(CurrentPattern.Notes);
        }

        Current.Patterns.Add(pattern);
        Pattern = Current.Patterns.Count - 1;
    }

    /// <summary>Puts the pattern on the roll into a bar of the channel's row, or takes it out if it is there already.</summary>
    public void Place(int channel, int bar)
    {
        if (channel < 0 || channel >= Song.Channels.Count || bar < 0 || bar >= Song.Bars)
        {
            return;
        }

        Change();
        var row = Song.Channels[channel].Arrangement;
        while (row.Count <= bar)
        {
            row.Add(-1);
        }

        if (channel != Channel)
        {
            SelectChannel(channel);
        }

        row[bar] = row[bar] == Pattern ? -1 : Pattern;
    }

    /// <summary>Puts the pattern of a bar of the channel's row on the roll, as the one to edit and place.</summary>
    public void Pick(int channel, int bar)
    {
        SelectChannel(channel);
        if (Current.PatternAt(bar) is not null)
        {
            Pattern = Current.Arrangement[bar];
        }
    }

    public bool AddChannel(string instrument = "Harp")
    {
        if (Song.Channels.Count >= MaxChannels)
        {
            return false;
        }

        Change();
        Song.Channels.Add(new Channel { Instrument = instrument });
        SelectChannel(Song.Channels.Count - 1);
        return true;
    }

    public void RemoveChannel()
    {
        if (Song.Channels.Count <= 1)
        {
            return;
        }

        Change();
        Song.Channels.RemoveAt(Channel);
        SelectChannel(Channel);
    }

    /// <summary>The channel's instrument, the next or previous of the presets.</summary>
    public void CycleInstrument(int by)
    {
        Change();
        var index = Math.Max(0, InstrumentNames.ToList().IndexOf(Current.Instrument));
        Current.Instrument = InstrumentNames[(((index + by) % InstrumentNames.Count) + InstrumentNames.Count) % InstrumentNames.Count];
    }

    public void ChangeVolume(float by)
    {
        Change();
        Current.Volume = Math.Clamp(MathF.Round((Current.Volume + by) * 10) / 10, 0.1f, 2);
    }

    public void ChangeTempo(int by)
    {
        Change();
        Song.Tempo = Math.Clamp(Song.Tempo + by, 40, 240);
    }

    public void ChangeReverb(float by)
    {
        Change();
        Song.Reverb = Math.Clamp(MathF.Round((Song.Reverb + by) * 20) / 20, 0, 0.8f);
    }

    public void ChangeBars(int by)
    {
        Change();
        Song.Bars = Math.Clamp(Song.Bars + by, 1, 128);
    }

    /// <summary>Three beats to a bar or four; notes past the new bar's end are dropped.</summary>
    public void ToggleMetre()
    {
        Change();
        Song.BeatsPerBar = Song.BeatsPerBar == 4 ? 3 : 4;
        foreach (var pattern in Song.Channels.SelectMany(c => c.Patterns))
        {
            pattern.Notes.RemoveAll(n => n.Step >= Song.StepsPerBar);
        }
    }

    public void Rename(string title)
    {
        if (!string.IsNullOrWhiteSpace(title) && title != Song.Title)
        {
            Change();
            Song.Title = title.Trim();
        }
    }

    public void Undo() => Step(_undo, _redo);

    public void Redo() => Step(_redo, _undo);

    /// <summary>Marks the song as saved as it is now.</summary>
    public void Saved() => IsChanged = false;

    private void Step(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        to.Push(Song.ToJson());
        Song = Composition.FromJson(from.Pop());
        IsChanged = true;
        SelectChannel(Channel);
    }

    /// <summary>Remembers the song as it is, before a change.</summary>
    private void Change()
    {
        _undo.Push(Song.ToJson());
        if (_undo.Count > MaxUndo)
        {
            var kept = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            kept.ForEach(_undo.Push);
        }

        _redo.Clear();
        IsChanged = true;
    }
}
