using Jentl.Core.Audio;

namespace Games;

/// <summary>Names of the music tracks.</summary>
internal static class Music
{
    public const string Menu = "music-menu";
    public const string Puzzle = "music-puzzle";
    public const string Cards = "music-cards";
    public const string Table = "music-table";
    public const string Village = "music-village";
    public const string Cellar = "music-cellar";
}

/// <summary>
/// The game's music and the sound effects beyond the two recorded ones, composed for <see cref="Synth"/>: no
/// audio files to ship or license. Each voice is a line of eighth notes ("-" holds, "." rests, "x" is a drum).
/// </summary>
internal static class GameAudio
{
    // Instruments shared by the tunes.
    private static readonly Instrument Lead = new(Wave.Pulse, Volume: 0.16f, Decay: 0.15f, Sustain: 0.5f, Release: 0.12f, Vibrato: 0.08f);
    private static readonly Instrument Flute = new(Wave.Sine, Volume: 0.3f, Attack: 0.03f, Decay: 0.2f, Sustain: 0.7f, Release: 0.2f, Vibrato: 0.12f);
    private static readonly Instrument Bass = new(Wave.Triangle, Volume: 0.45f, Decay: 0.2f, Sustain: 0.6f, Release: 0.06f);
    private static readonly Instrument Arp = new(Wave.Square, Volume: 0.07f, Decay: 0.08f, Sustain: 0.3f, Release: 0.05f);
    private static readonly Instrument Pad = new(Wave.Sine, Volume: 0.16f, Attack: 0.25f, Decay: 0.3f, Sustain: 0.8f, Release: 0.6f);
    private static readonly Instrument Hat = new(Wave.Noise, Volume: 0.06f, Decay: 0.03f, Sustain: 0, Release: 0.01f);
    private static readonly Instrument Kick = new(Wave.Sine, Volume: 0.5f, Decay: 0.12f, Sustain: 0, Release: 0.02f, Slide: -24);

    /// <summary>Adds every track and effect to <paramref name="sounds"/>; tracks are rendered when first played.</summary>
    public static void Register(SoundPlayer sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        foreach (var (name, tune) in Tracks())
        {
            sounds.AddMusic(name, () => Synth.Render(tune, loop: true));
        }

        foreach (var (name, tune) in Effects())
        {
            sounds.Add(name, Synth.Render(tune));
        }
    }

    public static IEnumerable<(string Name, Tune Tune)> Tracks()
    {
        // The main menu: bright and simple, in C major.
        yield return (Music.Menu, new Tune(
            100,
            new Voice(Lead, Twice("E5 - G5 - C6 - B5 A5 | G5 - E5 - D5 - C5 - | F5 - A5 - G5 - E5 D5 | C5 - - - . . . .")),
            new Voice(Arp, Twice("C4 E4 G4 E4 C4 E4 G4 E4 | A3 C4 E4 C4 A3 C4 E4 C4 | F3 A3 C4 A3 G3 B3 D4 B3 | C4 E4 G4 E4 C4 E4 G4 E4")),
            new Voice(Bass, Twice("C3 - - - G2 - - - | A2 - - - E2 - - - | F2 - - - G2 - - - | C3 - - - G2 - - -"))));

        // Sokoban and the code lock: calm, thinking music in A minor.
        yield return (Music.Puzzle, new Tune(
            84,
            new Voice(Flute, ". . . . . . . . | . . . . . . . . | . . . . . . . . | . . . . . . . . | E5 - - D5 C5 - - . | A4 - - - . . . . | G4 - C5 - E5 - D5 - | B4 - - - . . . ."),
            new Voice(Arp, Twice("A3 C4 E4 C4 A3 C4 E4 C4 | F3 A3 C4 A3 F3 A3 C4 A3 | C4 E4 G4 E4 C4 E4 G4 E4 | G3 B3 D4 B3 G3 B3 D4 B3")),
            new Voice(Pad, Twice("A2 - - - - - - - | F2 - - - - - - - | C3 - - - - - - - | G2 - - - - - - -"))));

        // Solitaire: an easy lounge feel in F.
        yield return (Music.Cards, new Tune(
            96,
            new Voice(Lead, ". . . . . . . . | . . . . . . . . | . . . . . . . . | . . . . . . . . | C5 . A4 . C5 D5 . . | F5 - E5 D5 C5 . . . | D5 . Bb4 . D5 E5 . . | G5 - F5 E5 C5 . . ."),
            new Voice(Pad, Twice("A3 - - - - - - - | F3 - - - - - - - | F3 - - - - - - - | E3 - - - - - - -")),
            new Voice(Pad, Twice("E4 - - - - - - - | C4 - - - - - - - | D4 - - - - - - - | Bb3 - - - - - - -")),
            new Voice(Bass, Twice("F2 . C3 . F2 . A2 . | D2 . A2 . D2 . F2 . | G2 . D3 . G2 . Bb2 . | C3 . G2 . C3 . E3 .")),
            new Voice(Hat, Repeat(". . x . . . x . ", 8))));

        // Uno and poker: livelier, for playing against people.
        yield return (Music.Table, new Tune(
            112,
            new Voice(Lead, Twice("G4 - Bb4 C5 - . G4 . | A4 - C5 D5 - . A4 . | G4 - Bb4 C5 D5 C5 Bb4 G4 | B4 - - - . . . .")),
            new Voice(Bass, Twice("C3 . E3 . G3 . A3 Bb3 | F2 . A2 . C3 . D3 Eb3 | C3 . E3 . G3 . A3 Bb3 | G2 . B2 . D3 . F3 .")),
            new Voice(Kick, Repeat("C2 . . . C2 . . . ", 8)),
            new Voice(Hat, Repeat(". x . x . x . x ", 8))));

        // Mossbrook: a folk tune for the village.
        yield return (Music.Village, new Tune(
            120,
            new Voice(Flute, Twice("G4 A4 B4 D5 - B4 A4 G4 | A4 B4 A4 G4 E4 - D4 - | G4 A4 B4 D5 - E5 D5 B4 | A4 - G4 - G4 - . .")),
            new Voice(Arp, Twice("B3 - D4 - B3 - D4 - | C4 - E4 - D4 - C4 - | B3 - D4 - C4 - E4 - | C4 - B3 - B3 - . .")),
            new Voice(Bass, Twice("G2 . D3 . G2 . D3 . | C3 . G2 . D3 . A2 . | G2 . D3 . E3 . B2 . | D3 . D3 . G2 . . ."))));

        // The old cellar: dark and sparse.
        yield return (Music.Cellar, new Tune(
            70,
            new Voice(Flute with { Volume = 0.2f }, ". . F4 - E4 . . . | D4 - - - . . . . | . . F4 - G4 . A4 . | E4 - - - . . . . | . . . . . . . . | . . . . . . . . | . . A4 - Bb4 . A4 . | D4 - - - . . . ."),
            new Voice(Pad with { Volume = 0.22f }, Twice("D2 - - - - - - - | D2 - - - - - - - | Bb1 - - - - - - - | A1 - - - - - - -")),
            new Voice(Hat with { Volume = 0.04f }, Repeat("x . . . . . . . . . . . . . . . ", 4))));
    }

    public static IEnumerable<(string Name, Tune Tune)> Effects()
    {
        var blip = new Instrument(Wave.Square, Volume: 0.25f, Decay: 0.05f, Sustain: 0.3f, Release: 0.03f);
        var thud = new Instrument(Wave.Noise, Volume: 0.35f, Decay: 0.05f, Sustain: 0, Release: 0.02f);

        // A card laid down: a short soft thud.
        yield return (Sound.Card, new Tune(480, new Voice(thud with { Volume = 0.25f }, "x")));

        // A move that is not allowed: a low double buzz.
        yield return (Sound.Error, new Tune(600, new Voice(new Instrument(Wave.Saw, Volume: 0.18f, Decay: 0.05f, Sustain: 0.5f, Release: 0.03f), "C3 . C3")));

        // A blow in a fight.
        yield return (Sound.Hit, new Tune(400, new Voice(thud, "x"), new Voice(new Instrument(Wave.Square, Volume: 0.2f, Decay: 0.08f, Sustain: 0, Slide: -12), "C4")));

        // Picking something up: a rising coin blip.
        yield return (Sound.Pickup, new Tune(900, new Voice(blip, "E6 B6 -")));

        // Levelling up, cracking a lock: a quick arpeggio.
        yield return (Sound.LevelUp, new Tune(720, new Voice(blip, "C5 E5 G5 C6 - -")));

        // Winning a game or a hand: a little fanfare.
        yield return (Sound.Fanfare, new Tune(360, new Voice(Lead with { Volume = 0.3f }, "G4 C5 E5 G5 - E5 G5 - - -"), new Voice(Bass, "C3 - - - - C3 - - - -")));

        // Chips on the poker table: two quick clicks.
        yield return (Sound.Chips, new Tune(900, new Voice(new Instrument(Wave.Noise, Volume: 0.2f, Decay: 0.015f, Sustain: 0, Release: 0.01f), "x . x")));

        // Turning a code lock wheel.
        yield return (Sound.Click, new Tune(900, new Voice(blip with { Volume = 0.12f, Decay = 0.02f, Sustain = 0 }, "A6")));

        // Something bad: losing a game, falling in the cellar.
        yield return (Sound.Lose, new Tune(200, new Voice(Lead with { Volume = 0.25f, Slide = -1 }, "E4 - D4 - C4 - - -")));
    }

    private static string Twice(string steps) => $"{steps} | {steps}";

    private static string Repeat(string steps, int times) => string.Concat(Enumerable.Repeat(steps, times));
}
