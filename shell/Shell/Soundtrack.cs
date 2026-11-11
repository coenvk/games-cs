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
    private enum Style
    {
        /// <summary>Pastoral: flute over harp, cello beneath.</summary>
        Folk,

        /// <summary>The minstrels' corner: clarinet, piano on the off-beats, upright bass.</summary>
        Lounge,

        /// <summary>Oboe over strings and a slow harp.</summary>
        Calm,

        /// <summary>Chiptune, for the arcade.</summary>
        Chip,

        /// <summary>Chiptune with a backbeat.</summary>
        ChipRock,

        /// <summary>Brass over driving strings, timpani and snare.</summary>
        Battle,

        /// <summary>A thin flute over a choir, low strings and a bell now and then.</summary>
        Spooky,

        /// <summary>The tavern: fiddle, lute, frame drum.</summary>
        Jig,

        /// <summary>Accordion, piano and a walking bass.</summary>
        Swing,

        /// <summary>Three to a bar: violin over a harp's oom-pah-pah.</summary>
        Waltz,

        /// <summary>Horn over choir and strings, timpani under it.</summary>
        Hymn,

        /// <summary>Pan pipes, celesta and a distant choir.</summary>
        Mystic,

        /// <summary>Celesta over harp and pizzicato, as a music box.</summary>
        MusicBox,
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
        Track Original(string mood, string title, float reverb) => new(mood, title, mood, () => originals[mood] with { Reverb = reverb });
        Track New(string mood, Song song) =>
            new($"song-{string.Concat(song.Title.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ')).Replace(' ', '-')}", song.Title, mood, song.ToTune);

        yield return Original(Music.Menu, "First Light", 0.3f);
        yield return New(Music.Menu, Write("Open Doors", 108, Style.Folk, "a a b a",
            ("G | D | Em | C", "B4 - D5 - G5 - F#5 E5 | D5 - - - A4 - B4 C5 | B4 - G4 - E4 - G4 B4 | C5 - - - . . . ."),
            ("C | G | Am | D", "E5 - - D5 C5 - E5 - | D5 - B4 - G4 - - - | A4 - C5 - E5 - D5 C5 | D5 - - - F#5 - - .")));
        yield return New(Music.Menu, Write("Sunday Best", 92, Style.Lounge, "a b a b",
            ("Fmaj7 | Dm7 | Gm7 | C7", "A4 - C5 - E5 - - . | D5 - - C5 A4 - - . | Bb4 - D5 - F5 - E5 D5 | C5 - - - G4 - - ."),
            ("Bb | Am | Gm7 | C7", "D5 - F5 - Bb4 - - . | C5 - E5 - A4 - - . | Bb4 - A4 G4 F4 - G4 A4 | G4 - - - . . . .")));
        yield return New(Music.Menu, Write("Paper Boats", 84, Style.Calm, "a a b a",
            ("D | Bm | G | A", "F#5 - - E5 D5 - A4 - | B4 - - - D5 - F#5 - | G5 - - F#5 E5 - D5 - | E5 - - - . . . ."),
            ("G | A | F#m | Bm", "D5 - B4 - G4 - B4 D5 | E5 - C#5 - A4 - - . | F#5 - E5 - C#5 - A4 - | B4 - - - . . . .")));
        yield return New(Music.Menu, Write("Kingdom Morning", 84, Style.Hymn, "a a b a",
            ("D | G | D | A", "D5 - - - F#5 - A5 - | B5 - - - A5 - G5 - | F#5 - - - E5 - D5 - | E5 - - - - - . ."),
            ("Bm | G | D | A", "F#5 - - - E5 - D5 - | D5 - B4 - - - G4 - | A4 - D5 - F#5 - A5 - | A5 - - - - - . .")));
        yield return New(Music.Menu, Write("The Long Road", 132, Style.Waltz, "a b a b",
            ("G | Em | C | D | G | Em | Am D | G", "D5 - - - B4 C5 | D5 - - - G5 - | E5 - - - C5 D5 | E5 - D5 - C5 - | B4 - - - G4 A4 | B4 - - - E5 - | C5 - - A4 - F#4 | G4 - - - - ."),
            ("C | G | Am | D | C | G | Am D | G", "E5 - - - G5 - | D5 - - - B4 - | C5 - - - E5 - | D5 - - - F#5 - | G5 - - - E5 - | D5 - - - B4 G4 | A4 - C5 B4 - A4 | G4 - - - - .")));

        yield return Original(Music.Puzzle, "Thinking Cap", 0.35f);
        yield return New(Music.Puzzle, Write("Slow Gears", 76, Style.Calm, "a b a b",
            ("Am | Em | F | G", "E5 - - - C5 - - - | B4 - - - G4 - - - | A4 - C5 - F5 - - - | D5 - - - . . . ."),
            ("Dm | Am | E | E", "F5 - - - D5 - A4 - | C5 - - - E5 - - - | B4 - - G#4 E4 - - - | . . . . . . . .")));
        yield return New(Music.Puzzle, Write("Night Library", 70, Style.Calm, "a a b a",
            ("Em | Cmaj7 | Am | B7", "G4 - - - B4 - E5 - | D5 - - - B4 - - - | C5 - - - A4 - E4 - | F#4 - - - D#4 - - ."),
            ("C | G | Am | B7", "E5 - - D5 C5 - - - | B4 - - - G4 - - - | A4 - - B4 C5 - E5 - | D#5 - - - B4 - - .")));
        yield return New(Music.Puzzle, Write("Glass Marbles", 100, Style.MusicBox, "a b a b",
            ("C | F | G | C", "E5 . G5 . C6 . G5 . | F5 . A5 . C6 . A5 . | G5 . B5 . D6 . B5 . | C6 - - - . . . ."),
            ("Am | Dm | G7 | C", "A5 - G5 - E5 - C5 - | D5 - F5 - A5 - - - | G5 - F5 - D5 - B4 - | C5 - - - . . . .")));
        yield return New(Music.Puzzle, Write("Wizard's Study", 80, Style.Mystic, "a b a b",
            ("Am | F | C | E", "E5 - - - A5 - G5 - | F5 - - - C5 - - . | E5 - G5 - C6 - B5 - | G#5 - - - - - . ."),
            ("F | G | Em | Am", "A5 - - - F5 - C5 - | B4 - D5 - G5 - - . | G5 - E5 - B4 - E5 - | A4 - - - - - . .")));
        yield return New(Music.Puzzle, Write("Starlit Runes", 92, Style.MusicBox, "a a b a",
            ("C | Am | F | G", "E6 - G5 - C6 - E6 - | C6 - A5 - E5 - A5 - | A5 - F5 - C6 - A5 - | B5 - - - D6 - - ."),
            ("F | G | Em | Am", "A5 - C6 - F6 - E6 - | D6 - B5 - G5 - - . | G5 - B5 - E6 - D6 - | C6 - - - A5 - - .")));

        yield return Original(Music.Cards, "Easy Shuffle", 0.2f);
        yield return New(Music.Cards, Write("Velvet Felt", 88, Style.Lounge, "a a b a",
            ("Bbmaj7 | Gm7 | Cm7 | F7", "D5 - F5 - A5 - - . | G5 - - F5 D5 - - . | Eb5 - G5 - Bb5 - A5 G5 | F5 - - - . . . ."),
            ("Ebmaj7 | Dm7 | Cm7 | F7", "G5 - Bb5 - D6 - - . | C6 - - A5 F5 - - . | G5 - F5 Eb5 C5 - D5 Eb5 | F5 - - - . . . .")));
        yield return New(Music.Cards, Write("Shuffle Swing", 112, Style.Swing, "a b a b",
            ("C | A7 | Dm7 | G7", "E5 . G5 . E5 D5 C5 . | C#5 . E5 . G5 . A5 . | F5 . D5 . A4 . C5 . | B4 - - - G4 - . ."),
            ("F | Fm | C | G7", "A5 . F5 . C5 . A4 . | Ab4 . C5 . F5 . Ab5 . | G5 . E5 . C5 . E5 . | D5 - - - B4 - . .")));
        yield return New(Music.Cards, Write("Patience", 80, Style.Calm, "a b a b",
            ("G | Em | C | D", "D5 - - B4 G4 - - - | E5 - - - B4 - - - | C5 - E5 - G5 - E5 - | D5 - - - . . . ."),
            ("C | D | Bm | Em", "E5 - - - G5 - - - | F#5 - - - A5 - - - | D5 - - B4 F#4 - - - | G4 - - - . . . .")));
        yield return New(Music.Cards, Write("Minstrel's Rest", 108, Style.Waltz, "a b a b",
            ("F | C | Dm | Bb | F | C | Bb C | F", "C5 - - - A4 - | G4 - - - C5 - | D5 - - - F5 - | F5 - D5 - Bb4 - | A4 - - - C5 - | E5 - - - G5 - | F5 - D5 E5 - C5 | F5 - - - - ."),
            ("Bb | F | Gm | C | Bb | F | Gm C | F", "D5 - - - F5 - | C5 - - - A4 - | Bb4 - - - D5 - | C5 - - - E5 - | F5 - - - D5 - | C5 - - - A4 - | G4 - Bb4 G4 - E4 | F4 - - - - .")));

        yield return Original(Music.Table, "Table Talk", 0.15f);
        yield return New(Music.Table, Write("High Stakes", 120, Style.Battle, "a a b a",
            ("Dm | Bb | C | A", "D5 . D5 F5 . A5 . G5 | F5 - D5 - Bb4 - - . | C5 . E5 G5 . C6 . Bb5 | A5 - - - C#5 - E5 -"),
            ("Gm | Dm | Bb | A", "G5 - Bb5 - D6 - C6 Bb5 | A5 - F5 - D5 - - . | Bb4 - D5 - F5 - E5 D5 | C#5 - - - A4 - - .")));
        yield return New(Music.Table, Write("Friendly Rivals", 126, Style.Jig, "a b a b",
            ("F | C | Dm | Bb C", "A4 . C5 . F5 . A5 . | G5 . E5 . C5 . E5 . | F5 . D5 . A4 . D5 . | D5 . . . E5 . . ."),
            ("Bb | F | G7 | C", "D5 - F5 - Bb5 - A5 G5 | A5 - F5 - C5 - - . | B4 - D5 - G5 - F5 E5 | E5 - - - C5 - - .")));
        yield return New(Music.Table, Write("Last Hand", 108, Style.Hymn, "a a b a",
            ("Am | F | Dm | E", "A4 - C5 - E5 - D5 C5 | C5 - A4 - F4 - - . | D5 - F5 - A5 - G#5 A5 | B5 - - - G#5 - E5 -"),
            ("F | G | Em | Am", "F5 - E5 - D5 - C5 - | D5 - - - B4 - G4 - | E5 - - - G5 - B5 - | A5 - - - . . . .")));
        yield return New(Music.Table, Write("Tavern Dice", 144, Style.Jig, "a a b a",
            ("G | F | G | D", "G5 . D5 G5 . B5 A5 G5 | F5 . C5 F5 . A5 G5 F5 | G5 . B5 D6 . B5 G5 . | A5 - F#5 - D5 - - ."),
            ("C | G | F | D", "E5 . G5 C6 . G5 E5 . | D5 . G5 B5 . G5 D5 . | C5 . F5 A5 . F5 C5 . | D5 - - - F#5 - A5 -")));
        yield return New(Music.Table, Write("The Merchant's Game", 104, Style.Lounge, "a b a b",
            ("Dm7 | G7 | Dm7 | G7", "D5 - F5 - A5 - G5 F5 | B4 - - - D5 - - . | F5 - E5 - D5 - C5 - | B4 - G4 - - - . ."),
            ("Fmaj7 | Em7 | Dm7 | G7", "A5 - - - G5 - F5 - | E5 - - - D5 - - . | C5 - D5 - F5 - A5 - | G5 - - - . . . .")));

        yield return Original(Music.Village, "Mossbrook", 0.2f);
        yield return New(Music.Village, Write("Mill Wheel", 116, Style.Folk, "a a b a",
            ("D | G | D | A", "F#4 A4 D5 - D5 E5 F#5 - | G5 - F#5 E5 D5 - B4 - | A4 - F#4 - D4 - F#4 A4 | E5 - - - C#5 - A4 -"),
            ("G | D | Em | A", "B4 - D5 - G5 - - . | F#5 - E5 - D5 - - . | E5 - G5 - B5 - A5 G5 | E5 - - - . . . .")));
        yield return New(Music.Village, Write("Harvest Jig", 138, Style.Jig, "a a b a",
            ("G | C | G | D", "G4 B4 D5 B4 G4 B4 D5 G5 | E5 G5 C5 E5 G5 E5 C5 E5 | D5 B4 G4 B4 D5 G5 B5 G5 | F#5 A5 D5 F#5 A5 - - ."),
            ("C | G | Am | D", "E5 - G5 - C6 - B5 A5 | G5 - D5 - B4 - - . | C5 - E5 - A5 - G5 F#5 | D5 - - - . . . .")));
        yield return New(Music.Village, Write("Lantern Lane", 84, Style.Calm, "a b a b",
            ("E | C#m | A | B", "G#4 - B4 - E5 - - - | C#5 - - - E5 - - - | A4 - C#5 - E5 - F#5 - | D#5 - - - . . . ."),
            ("A | B | G#m | C#m", "C#5 - - - E5 - A5 - | B4 - - - D#5 - F#5 - | G#5 - - E5 D#5 - B4 - | C#5 - - - . . . .")));
        yield return New(Music.Village, Write("Hearthside", 100, Style.Folk, "a a b a",
            ("Am | G | Am | Em", "A4 - B4 C5 D5 - E5 - | D5 - B4 - G4 - - . | A4 - C5 - E5 - F#5 G5 | E5 - - - B4 - - ."),
            ("C | G | D | Em", "G5 - E5 - C5 - E5 - | D5 - B4 - G4 - B4 - | A4 - D5 - F#5 - E5 D5 | E5 - - - . . . .")));
        yield return New(Music.Village, Write("Meadow Path", 112, Style.Folk, "a b a b",
            ("G | F | C | G", "G4 - B4 - D5 - - G5 | F5 - E5 - D5 - C5 - | E5 - - - C5 - A4 - | G4 - - - . . . ."),
            ("C | G | F | D", "E5 - G5 - C6 - B5 - | B5 - G5 - D5 - - . | A5 - F5 - C5 - A4 - | A4 - - - D5 - - .")));
        yield return New(Music.Village, Write("Market Morning", 132, Style.Jig, "a a b a",
            ("D | A | G | A", "F#5 . E5 F#5 . A5 F#5 D5 | E5 . C#5 E5 . A5 E5 C#5 | D5 . B4 D5 . G5 D5 B4 | C#5 - A4 - B4 - C#5 -"),
            ("D | G | A | D", "D5 - F#5 - A5 - F#5 - | G5 - B5 - G5 - D5 - | E5 - C#5 - A4 - E5 - | D5 - - - . . . .")));
        yield return New(Music.Village, Write("The Old Well", 120, Style.Waltz, "a b a b",
            ("Em | C | G | D | Em | C | Am B | Em", "B4 - - - E5 - | G5 - - - E5 - | D5 - - - B4 - | A4 - - - F#4 - | G4 - - - B4 - | E5 - - - G5 - | E5 - - D#5 - F#5 | E5 - - - - ."),
            ("C | D | Bm | Em | C | D | B7 | Em", "C5 - E5 - G5 - | F#5 - - - A5 - | F#5 - D5 - B4 - | E5 - - - G5 - | G5 - E5 - C5 - | D5 - F#5 - A5 - | B5 - A5 - F#5 - | E5 - - - - .")));

        yield return Original(Music.Cellar, "The Old Cellar", 0.5f);
        yield return New(Music.Cellar, Write("Dripping Stone", 64, Style.Spooky, "a b a b",
            ("Dm | Dm | Bb | A", ". . D5 - . . . . | . . . . A4 - . . | . . F4 - E4 - D4 - | C#4 - - - . . . ."),
            ("Gm | Dm | Bb | A", ". . Bb4 - A4 - G4 - | F4 - - - . . . . | . . D5 - C5 - Bb4 - | A4 - - - . . . .")));
        yield return New(Music.Cellar, Write("Under the Stairs", 72, Style.Spooky, "a b a b",
            ("Em | Em | C | B", "E4 - - - . . G4 - | F#4 - - - . . . . | E4 - - - G4 - C5 - | B4 - - - . . . ."),
            ("Am | Em | F | B", "C5 - - - A4 - - - | B4 - - - G4 - - - | A4 - - - F4 - - - | D#4 - - - . . . .")));
        yield return New(Music.Cellar, Write("Candle and Cobweb", 66, Style.Spooky, "a b a b",
            ("Dm | Bb | Gm | A", "D5 - - - F5 - E5 - | D5 - - - - - . . | Bb4 - - - D5 - C#5 - | A4 - - - - - . ."),
            ("Dm | Gm | Bb | A7", "A5 - - - G5 - F5 - | G5 - - - Bb4 - - . | F5 - - - E5 - D5 - | C#5 - - - E5 - - .")));
        yield return New(Music.Cellar, Write("Forgotten Vault", 72, Style.Mystic, "a b a b",
            ("Em | F | Em | F", "E5 - - - . . B4 - | C5 - - - F5 - E5 - | B4 - - - . . G4 - | A4 - - - - - . ."),
            ("Am | G | F | E", "A4 - C5 - E5 - - . | D5 - B4 - G4 - - . | C5 - A4 - F4 - A4 - | G#4 - - - - - . .")));

        yield return Original(Music.Race, "Pole Position", 0.2f);
        yield return New(Music.Race, Write("Open Throttle", 152, Style.Battle, "a a b a",
            ("A | F#m | D | E", "E5 . E5 A5 . E5 C#5 . | F#5 - - - E5 - C#5 - | D5 . D5 F#5 . A5 F#5 . | E5 - - - . . E5 F#5"),
            ("D | E | F#m | E", "F#5 - A5 - D6 - C#6 B5 | B5 - G#5 - E5 - - . | C#6 - B5 - A5 - F#5 - | G#5 - - - E5 - - .")));
        yield return New(Music.Race, Write("Rain Circuit", 144, Style.Battle, "a a b a",
            ("Bm | G | D | A", "B4 . B4 D5 . F#5 . E5 | D5 - B4 - G4 - - . | A4 . D5 F#5 . A5 . F#5 | E5 - - - C#5 - - ."),
            ("G | A | F#m | Bm", "D5 - G5 - B5 - A5 G5 | A5 - E5 - C#5 - - . | F#5 - A5 - C#6 - B5 A5 | B5 - - - . . . .")));
        yield return New(Music.Race, Write("Final Lap", 168, Style.Battle, "a a b a",
            ("E | B | C#m | A", "B4 E5 G#5 B5 - G#5 E5 B4 | D#5 F#5 B5 - F#5 D#5 B4 - | C#5 E5 G#5 C#6 - G#5 E5 C#5 | A4 C#5 E5 A5 - - - ."),
            ("A | B | E | E", "C#6 - B5 - A5 - G#5 - | F#5 - - - D#5 - B4 - | E5 - G#5 - B5 - E6 - | E6 - - - . . . .")));
        yield return New(Music.Race, Write("Gallop", 160, Style.Battle, "a a b a",
            ("Dm | C | Bb | A", "D5 - D5 F5 - A5 - D6 | C6 - - - G5 - E5 - | F5 - F5 D5 - Bb4 - D5 | C#5 - - - E5 - A5 -"),
            ("Bb | C | Dm | A", "Bb5 - - - F5 - D5 - | C6 - - - G5 - E5 - | A5 - F5 - D5 - F5 - | E5 - - - C#5 - A4 -")));
        yield return New(Music.Race, Write("Knights' Charge", 140, Style.Battle, "a b a b",
            ("Cm | Ab | Eb | G", "C5 - - Eb5 G5 - - C6 | Ab5 - - - G5 - F5 - | G5 - - Bb4 Eb5 - - G5 | G5 - - - D5 - B4 -"),
            ("Ab | Bb | Cm | G", "C6 - - - Ab5 - Eb5 - | D5 - - - F5 - Bb5 - | G5 - - - Eb5 - C5 - | D5 - - - B4 - G4 -")));

        yield return Original(Music.Kitchen, "Kitchen Rush", 0.12f);
        yield return New(Music.Kitchen, Write("Sizzle Shuffle", 132, Style.Swing, "a b a b",
            ("F | Bb | F | C7", "C5 . F5 . A5 . F5 . | D5 . F5 . Bb5 . F5 . | A5 . F5 . C5 . A4 . | G4 - Bb4 - E5 - G5 -"),
            ("Bb | F | G7 | C7", "D5 - F5 - Bb5 - A5 - | A5 - F5 - C5 - - . | B4 - D5 - G5 - F5 - | E5 - - - C5 - - .")));
        yield return New(Music.Kitchen, Write("Tea Time Panic", 160, Style.Jig, "a a b a",
            ("C | G | Am | F", "C5 E5 G5 E5 C5 E5 G5 C6 | B4 D5 G5 D5 B4 D5 G5 B5 | A4 C5 E5 C5 A4 C5 E5 A5 | F5 - A5 - C6 - - ."),
            ("F | G | C | G", "A5 - F5 - C5 - F5 - | B5 - G5 - D5 - G5 - | C6 - G5 - E5 - C5 - | D5 - - - . . . .")));
        yield return New(Music.Kitchen, Write("Busy Ladle", 150, Style.Jig, "a a b a",
            ("C | F | G | C", "E5 . G5 E5 . C5 D5 E5 | F5 . A5 F5 . C5 D5 F5 | G5 . B5 G5 . D5 E5 F5 | E5 - C5 - . . . ."),
            ("F | C | D | G", "A5 . F5 A5 . C6 A5 F5 | G5 . E5 G5 . C6 G5 E5 | F#5 . D5 F#5 . A5 F#5 D5 | G5 - - - B4 - D5 -")));

        yield return New(Music.Words, Write("Letters in Bloom", 96, Style.Calm, "a b a b",
            ("C | Em | F | G", "E5 - G5 - C6 - - . | B5 - G5 - E5 - - . | A5 - - G5 F5 - C5 - | D5 - - - . . . ."),
            ("Am | Em | F | G", "C5 - E5 - A5 - - . | B4 - E5 - G5 - - . | F5 - A5 - C6 - B5 A5 | G5 - - - . . . .")));
        yield return New(Music.Words, Write("Quiet Puzzle Room", 78, Style.Calm, "a b a b",
            ("F | Am | Bb | C", "A4 - - - C5 - F5 - | E5 - - - C5 - - - | D5 - - - F5 - Bb5 - | G5 - - - . . . ."),
            ("Dm | Am | Bb | C", "F5 - - - D5 - A4 - | C5 - - - E5 - - - | D5 - - - Bb4 - - - | C5 - - - . . . .")));
        yield return New(Music.Words, Write("Word Garden", 104, Style.Folk, "a a b a",
            ("G | C | D | G", "D5 - B4 - G4 - B4 - | C5 - E5 - G5 - E5 - | F#5 - A5 - D5 - C5 - | B4 - - - . . . ."),
            ("Em | C | D | D", "E5 - G5 - B5 - - . | C6 - G5 - E5 - - . | D5 - F#5 - A5 - - . | A5 - - - F#5 - - .")));
        yield return New(Music.Words, Write("Scribe's Ink", 88, Style.Calm, "a b a b",
            ("F | Dm | Bb | C", "A4 - C5 - F5 - E5 - | D5 - - - A4 - - . | Bb4 - D5 - F5 - D5 - | C5 - - - G4 - - ."),
            ("Bb | F | Gm | C", "D5 - F5 - Bb5 - A5 - | A5 - - - F5 - - . | G5 - Bb4 - D5 - F5 - | E5 - - - C5 - - .")));

        yield return New(Music.Arcade, Write("Brick by Brick", 140, Style.Chip, "a a b a",
            ("Em | C | G | D", "E5 . G5 . B5 . G5 . | C5 . E5 . G5 . E5 . | D5 . G5 . B5 . D6 . | A5 - F#5 - D5 - - ."),
            ("C | D | Em | Em", "E5 - G5 - C6 - B5 - | A5 - F#5 - D5 - - . | E5 - G5 - B5 - E6 - | E6 - - - . . . .")));
        yield return New(Music.Arcade, Write("Neon Paddle", 128, Style.ChipRock, "a b a b",
            ("Am | G | F | E", "A5 - E5 - A5 - C6 - | B5 - G5 - D5 - G5 - | A5 - F5 - C5 - F5 - | G#5 - - - E5 - - ."),
            ("F | G | Am | E", "C6 - A5 - F5 - A5 - | D6 - B5 - G5 - B5 - | C6 - - - E6 - - - | B5 - - - G#5 - - .")));
    }

    /// <summary>A song of two sections, A and B, in a style, played in <paramref name="order"/> (as "a a b a").</summary>
    /// <exception cref="ArgumentException">A bar of the melody is not a bar long: a slip in the writing.</exception>
    private static Song Write(string title, int tempo, Style style, string order, (string Chords, string Melody) a, (string Chords, string Melody) b)
    {
        var beats = style == Style.Waltz ? 3 : 4;
        var stepsPerBar = beats * 2;
        Section Section(string name, (string Chords, string Melody) section)
        {
            var bars = section.Melody.Split('|');
            for (var i = 0; i < bars.Length; i++)
            {
                var steps = bars[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (steps != stepsPerBar)
                {
                    throw new ArgumentException($"{title}: bar {i + 1} of {name}'s melody has {steps} steps, not {stepsPerBar}.");
                }
            }

            var parts = Accompany(style, section.Chords, stepsPerBar);
            parts["lead"] = section.Melody;
            return new Section(name, section.Chords.Split('|').Length, parts);
        }

        return new Song(title, tempo, Parts(style), [Section("a", a), Section("b", b)], order.Split(' ', StringSplitOptions.RemoveEmptyEntries), BeatsPerBar: beats)
        {
            Reverb = Reverb(style),
        };
    }

    private static float Reverb(Style style) => style switch
    {
        Style.Chip or Style.ChipRock => 0,
        Style.Jig or Style.Swing => 0.15f,
        Style.Lounge => 0.2f,
        Style.Folk or Style.Battle => 0.25f,
        Style.Waltz => 0.3f,
        Style.Calm or Style.MusicBox => 0.35f,
        Style.Hymn or Style.Mystic => 0.45f,
        _ => 0.5f,
    };

    private static List<SongPart> Parts(Style style)
    {
        var quick = Instruments.Strings with { Attack = 0.02f, Release = 0.1f, Volume = 0.1f };
        return style switch
        {
            Style.Folk => [new("lead", Instruments.Flute), new("bass", Instruments.Cello), new("arp", Instruments.Harp), new("hat", Instruments.Shaker with { Volume = 0.035f })],
            Style.Jig => [new("lead", Instruments.Violin), new("bass", Instruments.UprightBass), new("arp", Instruments.Lute), new("drum", Instruments.FrameDrum), new("hat", Instruments.Shaker)],
            Style.Lounge => [new("lead", Instruments.Clarinet), new("bass", Instruments.UprightBass), new("keys", Instruments.Piano with { Volume = 0.3f }), new("pad", Instruments.Strings with { Volume = 0.07f }), new("hat", Instruments.Shaker with { Volume = 0.04f })],
            Style.Swing => [new("lead", Instruments.Accordion), new("bass", Instruments.UprightBass), new("keys", Instruments.Piano with { Volume = 0.3f }), new("hat", Instruments.Shaker)],
            Style.Calm => [new("lead", Instruments.Oboe with { Volume = 0.2f }), new("bass", Instruments.Cello with { Volume = 0.13f }), new("pad", Instruments.Strings with { Volume = 0.09f }), new("arp", Instruments.Harp with { Volume = 0.32f })],
            Style.Chip => [new("lead", Instruments.ChipLead), new("bass", Instruments.ChipBass), new("arp", Instruments.ChipArp), new("kick", Instruments.Kick), new("snare", Instruments.Snare with { Volume = 0.1f, Ring = false, Cutoff = 0 })],
            Style.ChipRock => [new("lead", Instruments.ChipLead), new("bass", Instruments.ChipBass), new("arp", Instruments.ChipArp), new("kick", Instruments.Kick), new("snare", Instruments.Snare with { Volume = 0.1f, Ring = false, Cutoff = 0 }), new("hat", Instruments.ChipHat)],
            Style.Battle => [new("lead", Instruments.Brass), new("bass", Instruments.Cello with { Attack = 0.02f, Release = 0.08f }), new("ostinato", quick), new("timpani", Instruments.Timpani), new("snare", Instruments.Snare)],
            Style.Spooky => [new("lead", Instruments.Flute with { Volume = 0.22f }), new("bass", Instruments.Cello with { Volume = 0.12f }), new("pad", Instruments.Choir with { Volume = 0.3f }), new("bell", Instruments.Bells with { Volume = 0.12f })],
            Style.Waltz => [new("lead", Instruments.Violin), new("bass", Instruments.UprightBass), new("arp", Instruments.Harp with { Volume = 0.34f }), new("pad", Instruments.Strings with { Volume = 0.06f })],
            Style.Hymn => [new("lead", Instruments.Horn), new("bass", Instruments.Cello with { Volume = 0.13f }), new("pad", Instruments.Strings with { Volume = 0.08f }), new("choir", Instruments.Choir with { Volume = 0.3f }), new("timpani", Instruments.Timpani with { Volume = 0.45f })],
            Style.Mystic => [new("lead", Instruments.PanPipes), new("arp", Instruments.Celesta with { Volume = 0.18f }), new("pad", Instruments.Strings with { Volume = 0.08f }), new("choir", Instruments.Choir with { Volume = 0.22f }), new("bass", Instruments.Cello with { Volume = 0.11f })],
            _ => [new("lead", Instruments.Celesta), new("bass", Instruments.Pizzicato), new("arp", Instruments.Harp with { Volume = 0.3f }), new("pad", Instruments.Strings with { Volume = 0.06f })],
        };
    }

    private static Dictionary<string, string> Accompany(Style style, string chords, int stepsPerBar)
    {
        var bars = chords.Split('|').Length;
        string Play(int octave, string pattern) => Chords.Play(chords, octave, pattern, stepsPerBar);
        string Hold(int octave, int tone) => Chords.Hold(chords, octave, tone, stepsPerBar);
        string Beat(string bar) => Chords.Repeat(bar, bars);
        return style switch
        {
            Style.Folk => new()
            {
                ["bass"] = Hold(2, 0),
                ["arp"] = Play(4, "1 3 5 3"),
                ["hat"] = Beat(". . x . . . x ."),
            },
            Style.Jig => new()
            {
                ["bass"] = Play(2, "1 . . 5 . . 1 ."),
                ["arp"] = Play(3, "1 5 8 5"),
                ["drum"] = Beat("C2 . . C2 . . C2 ."),
                ["hat"] = Beat("x . x x . x x ."),
            },
            Style.Lounge => new()
            {
                ["bass"] = Play(2, "1 . . . 5 . 3 ."),
                ["keys"] = Play(4, ". . 3 . . . 5 ."),
                ["pad"] = Hold(4, 1),
                ["hat"] = Beat(". . x . . . x ."),
            },
            Style.Swing => new()
            {
                ["bass"] = Play(2, "1 . 3 . 5 . 3 ."),
                ["keys"] = Play(4, ". . 3 . . 5 3 ."),
                ["hat"] = Beat(". . x . . x x ."),
            },
            Style.Calm => new()
            {
                ["bass"] = Hold(2, 0),
                ["pad"] = Hold(3, 1),
                ["arp"] = Play(4, "1 . . 5 . . 8 ."),
            },
            Style.Chip => new()
            {
                ["bass"] = Play(2, "1 1 8 1 1 1 8 1"),
                ["arp"] = Play(4, "1 3 5 8"),
                ["kick"] = Beat("C2 . . . C2 . . ."),
                ["snare"] = Beat(". . x . . . x ."),
            },
            Style.ChipRock => new()
            {
                ["bass"] = Play(2, "1 1 1 1 1 1 1 1"),
                ["arp"] = Play(3, "1 5 8 5"),
                ["kick"] = Beat("C2 . . C2 C2 . . ."),
                ["snare"] = Beat(". . x . . . x ."),
                ["hat"] = Beat("x x x x x x x x"),
            },
            Style.Battle => new()
            {
                ["bass"] = Play(2, "1 1 1 1 1 1 1 1"),
                ["ostinato"] = Play(3, "1 1 5 5 8 8 5 5"),
                ["timpani"] = Play(2, "1 . . . -5 . 1 ."),
                ["snare"] = Beat(". . x . . . x x"),
            },
            Style.Waltz => new()
            {
                ["bass"] = Play(2, "1 . . . . ."),
                ["arp"] = Play(4, ". . 3 . 5 ."),
                ["pad"] = Hold(4, 2),
            },
            Style.Hymn => new()
            {
                ["bass"] = Hold(2, 0),
                ["pad"] = Hold(3, 2),
                ["choir"] = Hold(4, 1),
                ["timpani"] = Play(2, "1 . . . . . . ."),
            },
            Style.Mystic => new()
            {
                ["bass"] = Hold(2, 0),
                ["arp"] = Play(5, "1 5 8 10"),
                ["pad"] = Hold(3, 1),
                ["choir"] = Hold(4, 0),
            },
            Style.MusicBox => new()
            {
                ["bass"] = Play(2, "1 . . . 5 . . ."),
                ["arp"] = Play(4, "1 5 8 5"),
                ["pad"] = Hold(4, 1),
            },
            _ => new()
            {
                ["bass"] = Hold(2, 0),
                ["pad"] = Hold(3, 1),
                ["bell"] = Play(5, "1 . . . . . . ."),
            },
        };
    }
}
