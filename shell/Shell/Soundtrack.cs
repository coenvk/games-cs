using Jentl.Core.Audio;

namespace Games;

/// <summary>The moods of the music: a place in a game plays one (<see cref="MusicContext"/>), and gets one of its songs.</summary>
internal static class Music
{
    public const string Menu = "music-menu";
    public const string Puzzle = "music-puzzle";
    public const string Cards = "music-cards";
    public const string Table = "music-table";
    public const string Village = "music-village";
    public const string Cellar = "music-cellar";
    public const string Race = "music-race";
    public const string Kitchen = "music-kitchen";
    public const string Words = "music-words";
    public const string Arcade = "music-arcade";
}

/// <summary>A song of the soundtrack: its id (as the sound player knows it), its title, and the mood it plays in.</summary>
internal sealed record Track(string Id, string Title, string Mood, Func<Tune> Tune);

/// <summary>
/// The games' soundtrack: every song, each with the mood it belongs to (the title screens, puzzles, the village, races).
/// A place in a game asks for a mood (<see cref="Music"/>) and gets one of that mood's songs, so the more a player
/// plays, the more songs they hear and unlock in the Music screen. Composed for <see cref="Synth"/>: a melody written
/// out per section over chords, with the accompaniment of the song's style made from the chords.
/// </summary>
internal static class Soundtrack
{
    private static readonly Instrument Lead = new(Wave.Pulse, Volume: 0.15f, Decay: 0.15f, Sustain: 0.5f, Release: 0.12f, Vibrato: 0.08f);
    private static readonly Instrument SoftLead = new(Wave.Square, Volume: 0.1f, Attack: 0.01f, Decay: 0.2f, Sustain: 0.6f, Release: 0.15f, Vibrato: 0.1f);
    private static readonly Instrument Flute = new(Wave.Sine, Volume: 0.3f, Attack: 0.03f, Decay: 0.2f, Sustain: 0.7f, Release: 0.2f, Vibrato: 0.12f);
    private static readonly Instrument Bass = new(Wave.Triangle, Volume: 0.42f, Decay: 0.2f, Sustain: 0.6f, Release: 0.06f);
    private static readonly Instrument Arp = new(Wave.Square, Volume: 0.06f, Decay: 0.08f, Sustain: 0.3f, Release: 0.05f);
    private static readonly Instrument Pad = new(Wave.Sine, Volume: 0.13f, Attack: 0.25f, Decay: 0.3f, Sustain: 0.8f, Release: 0.6f);
    private static readonly Instrument Hat = new(Wave.Noise, Volume: 0.05f, Decay: 0.03f, Sustain: 0, Release: 0.01f);
    private static readonly Instrument Snare = new(Wave.Noise, Volume: 0.1f, Decay: 0.09f, Sustain: 0, Release: 0.03f);
    private static readonly Instrument Kick = new(Wave.Sine, Volume: 0.45f, Decay: 0.12f, Sustain: 0, Release: 0.02f, Slide: -24);

    private enum Style
    {
        Folk,
        Lounge,
        Calm,
        Chip,
        Rock,
        Spooky,
        Jig,
        Swing,
    }

    /// <summary>Every song, in the order the Music screen lists them: mood by mood.</summary>
    public static IReadOnlyList<Track> Tracks { get; } = [.. Build()];

    /// <summary>A mood's songs, the first one the original.</summary>
    public static IReadOnlyList<Track> Playlist(string mood) => [.. Tracks.Where(t => t.Mood == mood)];

    public static Track? Find(string id) => Tracks.FirstOrDefault(t => t.Id == id);

    private static IEnumerable<Track> Build()
    {
        // The songs each place started with, under their moods' names.
        var originals = GameAudio.Tracks().ToDictionary(t => t.Name, t => t.Tune);
        Track Original(string mood, string title) => new(mood, title, mood, () => originals[mood]);
        Track New(string mood, Song song) => new($"song-{song.Title.ToLowerInvariant().Replace(' ', '-')}", song.Title, mood, song.ToTune);

        yield return Original(Music.Menu, "First Light");
        yield return New(Music.Menu, Write("Open Doors", 108, Style.Folk, "a a b a",
            ("G | D | Em | C", "B4 - D5 - G5 - F#5 E5 | D5 - - - A4 - B4 C5 | B4 - G4 - E4 - G4 B4 | C5 - - - . . . ."),
            ("C | G | Am | D", "E5 - - D5 C5 - E5 - | D5 - B4 - G4 - - - | A4 - C5 - E5 - D5 C5 | D5 - - - F#5 - - .")));
        yield return New(Music.Menu, Write("Sunday Best", 92, Style.Lounge, "a b a b",
            ("Fmaj7 | Dm7 | Gm7 | C7", "A4 - C5 - E5 - - . | D5 - - C5 A4 - - . | Bb4 - D5 - F5 - E5 D5 | C5 - - - G4 - - ."),
            ("Bb | Am | Gm7 | C7", "D5 - F5 - Bb4 - - . | C5 - E5 - A4 - - . | Bb4 - A4 G4 F4 - G4 A4 | G4 - - - . . . .")));
        yield return New(Music.Menu, Write("Paper Boats", 84, Style.Calm, "a a b a",
            ("D | Bm | G | A", "F#5 - - E5 D5 - A4 - | B4 - - - D5 - F#5 - | G5 - - F#5 E5 - D5 - | E5 - - - . . . ."),
            ("G | A | F#m | Bm", "D5 - B4 - G4 - B4 D5 | E5 - C#5 - A4 - - . | F#5 - E5 - C#5 - A4 - | B4 - - - . . . .")));

        yield return Original(Music.Puzzle, "Thinking Cap");
        yield return New(Music.Puzzle, Write("Slow Gears", 76, Style.Calm, "a b a b",
            ("Am | Em | F | G", "E5 - - - C5 - - - | B4 - - - G4 - - - | A4 - C5 - F5 - - - | D5 - - - . . . ."),
            ("Dm | Am | E | E", "F5 - - - D5 - A4 - | C5 - - - E5 - - - | B4 - - G#4 E4 - - - | . . . . . . . .")));
        yield return New(Music.Puzzle, Write("Night Library", 70, Style.Calm, "a a b a",
            ("Em | Cmaj7 | Am | B7", "G4 - - - B4 - E5 - | D5 - - - B4 - - - | C5 - - - A4 - E4 - | F#4 - - - D#4 - - ."),
            ("C | G | Am | B7", "E5 - - D5 C5 - - - | B4 - - - G4 - - - | A4 - - B4 C5 - E5 - | D#5 - - - B4 - - .")));
        yield return New(Music.Puzzle, Write("Glass Marbles", 100, Style.Chip, "a b a b",
            ("C | F | G | C", "E5 . G5 . C6 . G5 . | F5 . A5 . C6 . A5 . | G5 . B5 . D6 . B5 . | C6 - - - . . . ."),
            ("Am | Dm | G7 | C", "A5 - G5 - E5 - C5 - | D5 - F5 - A5 - - - | G5 - F5 - D5 - B4 - | C5 - - - . . . .")));

        yield return Original(Music.Cards, "Easy Shuffle");
        yield return New(Music.Cards, Write("Velvet Felt", 88, Style.Lounge, "a a b a",
            ("Bbmaj7 | Gm7 | Cm7 | F7", "D5 - F5 - A5 - - . | G5 - - F5 D5 - - . | Eb5 - G5 - Bb5 - A5 G5 | F5 - - - . . . ."),
            ("Ebmaj7 | Dm7 | Cm7 | F7", "G5 - Bb5 - D6 - - . | C6 - - A5 F5 - - . | G5 - F5 Eb5 C5 - D5 Eb5 | F5 - - - . . . .")));
        yield return New(Music.Cards, Write("Shuffle Swing", 112, Style.Swing, "a b a b",
            ("C | A7 | Dm7 | G7", "E5 . G5 . E5 D5 C5 . | C#5 . E5 . G5 . A5 . | F5 . D5 . A4 . C5 . | B4 - - - G4 - . ."),
            ("F | Fm | C | G7", "A5 . F5 . C5 . A4 . | Ab4 . C5 . F5 . Ab5 . | G5 . E5 . C5 . E5 . | D5 - - - B4 - . .")));
        yield return New(Music.Cards, Write("Patience", 80, Style.Calm, "a b a b",
            ("G | Em | C | D", "D5 - - B4 G4 - - - | E5 - - - B4 - - - | C5 - E5 - G5 - E5 - | D5 - - - . . . ."),
            ("C | D | Bm | Em", "E5 - - - G5 - - - | F#5 - - - A5 - - - | D5 - - B4 F#4 - - - | G4 - - - . . . .")));

        yield return Original(Music.Table, "Table Talk");
        yield return New(Music.Table, Write("High Stakes", 120, Style.Rock, "a a b a",
            ("Dm | Bb | C | A", "D5 . D5 F5 . A5 . G5 | F5 - D5 - Bb4 - - . | C5 . E5 G5 . C6 . Bb5 | A5 - - - C#5 - E5 -"),
            ("Gm | Dm | Bb | A", "G5 - Bb5 - D6 - C6 Bb5 | A5 - F5 - D5 - - . | Bb4 - D5 - F5 - E5 D5 | C#5 - - - A4 - - .")));
        yield return New(Music.Table, Write("Friendly Rivals", 126, Style.Chip, "a b a b",
            ("F | C | Dm | Bb C", "A4 . C5 . F5 . A5 . | G5 . E5 . C5 . E5 . | F5 . D5 . A4 . D5 . | D5 . . . E5 . . ."),
            ("Bb | F | G7 | C", "D5 - F5 - Bb5 - A5 G5 | A5 - F5 - C5 - - . | B4 - D5 - G5 - F5 E5 | E5 - - - C5 - - .")));
        yield return New(Music.Table, Write("Last Hand", 108, Style.Rock, "a a b a",
            ("Am | F | Dm | E", "A4 - C5 - E5 - D5 C5 | C5 - A4 - F4 - - . | D5 - F5 - A5 - G#5 A5 | B5 - - - G#5 - E5 -"),
            ("F | G | Em | Am", "F5 - E5 - D5 - C5 - | D5 - - - B4 - G4 - | E5 - - - G5 - B5 - | A5 - - - . . . .")));

        yield return Original(Music.Village, "Mossbrook");
        yield return New(Music.Village, Write("Mill Wheel", 116, Style.Folk, "a a b a",
            ("D | G | D | A", "F#4 A4 D5 - D5 E5 F#5 - | G5 - F#5 E5 D5 - B4 - | A4 - F#4 - D4 - F#4 A4 | E5 - - - C#5 - A4 -"),
            ("G | D | Em | A", "B4 - D5 - G5 - - . | F#5 - E5 - D5 - - . | E5 - G5 - B5 - A5 G5 | E5 - - - . . . .")));
        yield return New(Music.Village, Write("Harvest Jig", 138, Style.Jig, "a a b a",
            ("G | C | G | D", "G4 B4 D5 B4 G4 B4 D5 G5 | E5 G5 C5 E5 G5 E5 C5 E5 | D5 B4 G4 B4 D5 G5 B5 G5 | F#5 A5 D5 F#5 A5 - - ."),
            ("C | G | Am | D", "E5 - G5 - C6 - B5 A5 | G5 - D5 - B4 - - . | C5 - E5 - A5 - G5 F#5 | D5 - - - . . . .")));
        yield return New(Music.Village, Write("Lantern Lane", 84, Style.Calm, "a b a b",
            ("E | C#m | A | B", "G#4 - B4 - E5 - - - | C#5 - - - E5 - - - | A4 - C#5 - E5 - F#5 - | D#5 - - - . . . ."),
            ("A | B | G#m | C#m", "C#5 - - - E5 - A5 - | B4 - - - D#5 - F#5 - | G#5 - - E5 D#5 - B4 - | C#5 - - - . . . .")));

        yield return Original(Music.Cellar, "The Old Cellar");
        yield return New(Music.Cellar, Write("Dripping Stone", 64, Style.Spooky, "a b a b",
            ("Dm | Dm | Bb | A", ". . D5 - . . . . | . . . . A4 - . . | . . F4 - E4 - D4 - | C#4 - - - . . . ."),
            ("Gm | Dm | Bb | A", ". . Bb4 - A4 - G4 - | F4 - - - . . . . | . . D5 - C5 - Bb4 - | A4 - - - . . . .")));
        yield return New(Music.Cellar, Write("Under the Stairs", 72, Style.Spooky, "a b a b",
            ("Em | Em | C | B", "E4 - - - . . G4 - | F#4 - - - . . . . | E4 - - - G4 - C5 - | B4 - - - . . . ."),
            ("Am | Em | F | B", "C5 - - - A4 - - - | B4 - - - G4 - - - | A4 - - - F4 - - - | D#4 - - - . . . .")));

        yield return Original(Music.Race, "Pole Position");
        yield return New(Music.Race, Write("Open Throttle", 152, Style.Rock, "a a b a",
            ("A | F#m | D | E", "E5 . E5 A5 . E5 C#5 . | F#5 - - - E5 - C#5 - | D5 . D5 F#5 . A5 F#5 . | E5 - - - . . E5 F#5"),
            ("D | E | F#m | E", "F#5 - A5 - D6 - C#6 B5 | B5 - G#5 - E5 - - . | C#6 - B5 - A5 - F#5 - | G#5 - - - E5 - - .")));
        yield return New(Music.Race, Write("Rain Circuit", 144, Style.Rock, "a a b a",
            ("Bm | G | D | A", "B4 . B4 D5 . F#5 . E5 | D5 - B4 - G4 - - . | A4 . D5 F#5 . A5 . F#5 | E5 - - - C#5 - - ."),
            ("G | A | F#m | Bm", "D5 - G5 - B5 - A5 G5 | A5 - E5 - C#5 - - . | F#5 - A5 - C#6 - B5 A5 | B5 - - - . . . .")));
        yield return New(Music.Race, Write("Final Lap", 168, Style.Rock, "a a b a",
            ("E | B | C#m | A", "B4 E5 G#5 B5 - G#5 E5 B4 | D#5 F#5 B5 - F#5 D#5 B4 - | C#5 E5 G#5 C#6 - G#5 E5 C#5 | A4 C#5 E5 A5 - - - ."),
            ("A | B | E | E", "C#6 - B5 - A5 - G#5 - | F#5 - - - D#5 - B4 - | E5 - G#5 - B5 - E6 - | E6 - - - . . . .")));

        yield return Original(Music.Kitchen, "Kitchen Rush");
        yield return New(Music.Kitchen, Write("Sizzle Shuffle", 132, Style.Swing, "a b a b",
            ("F | Bb | F | C7", "C5 . F5 . A5 . F5 . | D5 . F5 . Bb5 . F5 . | A5 . F5 . C5 . A4 . | G4 - Bb4 - E5 - G5 -"),
            ("Bb | F | G7 | C7", "D5 - F5 - Bb5 - A5 - | A5 - F5 - C5 - - . | B4 - D5 - G5 - F5 - | E5 - - - C5 - - .")));
        yield return New(Music.Kitchen, Write("Tea Time Panic", 160, Style.Chip, "a a b a",
            ("C | G | Am | F", "C5 E5 G5 E5 C5 E5 G5 C6 | B4 D5 G5 D5 B4 D5 G5 B5 | A4 C5 E5 C5 A4 C5 E5 A5 | F5 - A5 - C6 - - ."),
            ("F | G | C | G", "A5 - F5 - C5 - F5 - | B5 - G5 - D5 - G5 - | C6 - G5 - E5 - C5 - | D5 - - - . . . .")));

        yield return New(Music.Words, Write("Letters in Bloom", 96, Style.Calm, "a b a b",
            ("C | Em | F | G", "E5 - G5 - C6 - - . | B5 - G5 - E5 - - . | A5 - - G5 F5 - C5 - | D5 - - - . . . ."),
            ("Am | Em | F | G", "C5 - E5 - A5 - - . | B4 - E5 - G5 - - . | F5 - A5 - C6 - B5 A5 | G5 - - - . . . .")));
        yield return New(Music.Words, Write("Quiet Puzzle Room", 78, Style.Calm, "a b a b",
            ("F | Am | Bb | C", "A4 - - - C5 - F5 - | E5 - - - C5 - - - | D5 - - - F5 - Bb5 - | G5 - - - . . . ."),
            ("Dm | Am | Bb | C", "F5 - - - D5 - A4 - | C5 - - - E5 - - - | D5 - - - Bb4 - - - | C5 - - - . . . .")));
        yield return New(Music.Words, Write("Word Garden", 104, Style.Folk, "a a b a",
            ("G | C | D | G", "D5 - B4 - G4 - B4 - | C5 - E5 - G5 - E5 - | F#5 - A5 - D5 - C5 - | B4 - - - . . . ."),
            ("Em | C | D | D", "E5 - G5 - B5 - - . | C6 - G5 - E5 - - . | D5 - F#5 - A5 - - . | A5 - - - F#5 - - .")));

        yield return New(Music.Arcade, Write("Brick by Brick", 140, Style.Chip, "a a b a",
            ("Em | C | G | D", "E5 . G5 . B5 . G5 . | C5 . E5 . G5 . E5 . | D5 . G5 . B5 . D6 . | A5 - F#5 - D5 - - ."),
            ("C | D | Em | Em", "E5 - G5 - C6 - B5 - | A5 - F#5 - D5 - - . | E5 - G5 - B5 - E6 - | E6 - - - . . . .")));
        yield return New(Music.Arcade, Write("Neon Paddle", 128, Style.Rock, "a b a b",
            ("Am | G | F | E", "A5 - E5 - A5 - C6 - | B5 - G5 - D5 - G5 - | A5 - F5 - C5 - F5 - | G#5 - - - E5 - - ."),
            ("F | G | Am | E", "C6 - A5 - F5 - A5 - | D6 - B5 - G5 - B5 - | C6 - - - E6 - - - | B5 - - - G#5 - - .")));
    }

    /// <summary>A song of two sections, A and B, in a style, played in <paramref name="order"/> (as "a a b a").</summary>
    private static Song Write(string title, int tempo, Style style, string order, (string Chords, string Melody) a, (string Chords, string Melody) b)
    {
        var parts = Parts(style);
        Section Section(string name, (string Chords, string Melody) section)
        {
            var steps = Accompany(style, section.Chords);
            steps["lead"] = section.Melody;
            return new Section(name, section.Chords.Split('|').Length, steps);
        }

        return new Song(title, tempo, parts, [Section("a", a), Section("b", b)], order.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<SongPart> Parts(Style style)
    {
        var lead = style switch
        {
            Style.Folk or Style.Calm => Flute,
            Style.Spooky => Flute with { Volume = 0.2f },
            Style.Lounge or Style.Swing => SoftLead,
            _ => Lead,
        };
        List<SongPart> parts = [new("lead", lead), new("bass", Bass)];
        switch (style)
        {
            case Style.Folk or Style.Jig:
                parts.AddRange([new("arp", Arp), new("hat", Hat with { Volume = 0.035f })]);
                break;
            case Style.Lounge or Style.Swing:
                parts.AddRange([new("pad", Pad), new("pad2", Pad), new("hat", Hat with { Volume = 0.04f })]);
                break;
            case Style.Calm:
                parts.AddRange([new("pad", Pad), new("arp", Arp with { Volume = 0.045f })]);
                break;
            case Style.Chip:
                parts.AddRange([new("arp", Arp), new("kick", Kick), new("snare", Snare)]);
                break;
            case Style.Rock:
                parts.AddRange([new("arp", Arp), new("kick", Kick), new("snare", Snare), new("hat", Hat)]);
                break;
            case Style.Spooky:
                parts.AddRange([new("pad", Pad with { Volume = 0.2f }), new("hat", Hat with { Volume = 0.035f })]);
                break;
        }

        return parts;
    }

    private static Dictionary<string, string> Accompany(Style style, string chords)
    {
        var bars = chords.Split('|').Length;
        return style switch
        {
            Style.Folk => new()
            {
                ["bass"] = Chords.Play(chords, 2, "1 . 5 . 1 . 5 ."),
                ["arp"] = Chords.Play(chords, 4, "1 3 5 3"),
                ["hat"] = Chords.Repeat(". . x . . . x .", bars),
            },
            Style.Jig => new()
            {
                ["bass"] = Chords.Play(chords, 2, "1 . . 5 . . 1 ."),
                ["arp"] = Chords.Play(chords, 3, "1 5 8 5"),
                ["hat"] = Chords.Repeat("x . x x . x x .", bars),
            },
            Style.Lounge => new()
            {
                ["bass"] = Chords.Play(chords, 2, "1 . . . 5 . 3 ."),
                ["pad"] = Chords.Hold(chords, 3, 1),
                ["pad2"] = Chords.Hold(chords, 4, 3),
                ["hat"] = Chords.Repeat(". . x . . . x .", bars),
            },
            Style.Swing => new()
            {
                ["bass"] = Chords.Play(chords, 2, "1 . 3 . 5 . 3 ."),
                ["pad"] = Chords.Hold(chords, 3, 1),
                ["pad2"] = Chords.Hold(chords, 4, 2),
                ["hat"] = Chords.Repeat(". . x . . x x .", bars),
            },
            Style.Calm => new()
            {
                ["bass"] = Chords.Hold(chords, 2, 0),
                ["pad"] = Chords.Hold(chords, 3, 1),
                ["arp"] = Chords.Play(chords, 4, "1 . . 5 . . 8 ."),
            },
            Style.Chip => new()
            {
                ["bass"] = Chords.Play(chords, 2, "1 1 8 1 1 1 8 1"),
                ["arp"] = Chords.Play(chords, 4, "1 3 5 8"),
                ["kick"] = Chords.Repeat("C2 . . . C2 . . .", bars),
                ["snare"] = Chords.Repeat(". . x . . . x .", bars),
            },
            Style.Rock => new()
            {
                ["bass"] = Chords.Play(chords, 2, "1 1 1 1 1 1 1 1"),
                ["arp"] = Chords.Play(chords, 3, "1 5 8 5"),
                ["kick"] = Chords.Repeat("C2 . . C2 C2 . . .", bars),
                ["snare"] = Chords.Repeat(". . x . . . x .", bars),
                ["hat"] = Chords.Repeat("x x x x x x x x", bars),
            },
            _ => new()
            {
                ["bass"] = Chords.Hold(chords, 2, 0),
                ["pad"] = Chords.Hold(chords, 3, 1),
                ["hat"] = Chords.Repeat("x . . . . . . .", bars),
            },
        };
    }
}
