using Jentl.Core.Audio;

namespace Games;

/// <summary>Names of the sound effects.</summary>
internal static class Sound
{
    public const string Navigate = "navigate";
    public const string Completed = "completed";
    public const string Card = "card";
    public const string Error = "error";
    public const string Hit = "hit";
    public const string Pickup = "pickup";
    public const string LevelUp = "levelup";
    public const string Fanfare = "fanfare";
    public const string Chips = "chips";
    public const string Click = "click";
    public const string Lose = "lose";
    public const string Jump = "jump";
    public const string Sizzle = "sizzle";
}

/// <summary>
/// The original songs (<see cref="Soundtrack"/> has them all) and the sound effects beyond the two recorded ones,
/// composed for <see cref="Synth"/>: no audio files to ship or license. Each voice is a line of eighth notes ("-" holds, "." rests, "x" is a drum).
/// </summary>
internal static class GameAudio
{
    /// <summary>Adds every track and effect to <paramref name="sounds"/>; tracks are rendered when first played.</summary>
    public static void Register(SoundPlayer sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        foreach (var track in Soundtrack.Tracks)
        {
            sounds.AddMusic(track.Id, () => Synth.Normalise(Synth.Render(track.Tune(), loop: true)));
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
            new Voice(Instruments.Flute, Twice("E5 - G5 - C6 - B5 A5 | G5 - E5 - D5 - C5 - | F5 - A5 - G5 - E5 D5 | C5 - - - . . . .")),
            new Voice(Instruments.Harp with { Volume = 0.3f }, Twice("C4 E4 G4 E4 C4 E4 G4 E4 | A3 C4 E4 C4 A3 C4 E4 C4 | F3 A3 C4 A3 G3 B3 D4 B3 | C4 E4 G4 E4 C4 E4 G4 E4")),
            new Voice(Instruments.Cello, Twice("C3 - - - G2 - - - | A2 - - - E2 - - - | F2 - - - G2 - - - | C3 - - - G2 - - -"))));

        // Sokoban and the code lock: calm, thinking music in A minor.
        yield return (Music.Puzzle, new Tune(
            84,
            new Voice(Instruments.Flute, ". . . . . . . . | . . . . . . . . | . . . . . . . . | . . . . . . . . | E5 - - D5 C5 - - . | A4 - - - . . . . | G4 - C5 - E5 - D5 - | B4 - - - . . . ."),
            new Voice(Instruments.Harp with { Volume = 0.3f }, Twice("A3 C4 E4 C4 A3 C4 E4 C4 | F3 A3 C4 A3 F3 A3 C4 A3 | C4 E4 G4 E4 C4 E4 G4 E4 | G3 B3 D4 B3 G3 B3 D4 B3")),
            new Voice(Instruments.Strings with { Volume = 0.09f }, Twice("A2 - - - - - - - | F2 - - - - - - - | C3 - - - - - - - | G2 - - - - - - -"))));

        // Solitaire: an easy lounge feel in F.
        yield return (Music.Cards, new Tune(
            96,
            new Voice(Instruments.Clarinet, ". . . . . . . . | . . . . . . . . | . . . . . . . . | . . . . . . . . | C5 . A4 . C5 D5 . . | F5 - E5 D5 C5 . . . | D5 . Bb4 . D5 E5 . . | G5 - F5 E5 C5 . . ."),
            new Voice(Instruments.Strings with { Volume = 0.07f }, Twice("A3 - - - - - - - | F3 - - - - - - - | F3 - - - - - - - | E3 - - - - - - -")),
            new Voice(Instruments.Strings with { Volume = 0.07f }, Twice("E4 - - - - - - - | C4 - - - - - - - | D4 - - - - - - - | Bb3 - - - - - - -")),
            new Voice(Instruments.UprightBass, Twice("F2 . C3 . F2 . A2 . | D2 . A2 . D2 . F2 . | G2 . D3 . G2 . Bb2 . | C3 . G2 . C3 . E3 .")),
            new Voice(Instruments.Shaker, Repeat(". . x . . . x . ", 8))));

        // Uno and poker: livelier, for playing against people.
        yield return (Music.Table, new Tune(
            112,
            new Voice(Instruments.Violin, Twice("G4 - Bb4 C5 - . G4 . | A4 - C5 D5 - . A4 . | G4 - Bb4 C5 D5 C5 Bb4 G4 | B4 - - - . . . .")),
            new Voice(Instruments.UprightBass, Twice("C3 . E3 . G3 . A3 Bb3 | F2 . A2 . C3 . D3 Eb3 | C3 . E3 . G3 . A3 Bb3 | G2 . B2 . D3 . F3 .")),
            new Voice(Instruments.FrameDrum, Repeat("C2 . . . C2 . . . ", 8)),
            new Voice(Instruments.Shaker, Repeat(". x . x . x . x ", 8))));

        // Mossbrook: a folk tune for the village.
        yield return (Music.Village, new Tune(
            120,
            new Voice(Instruments.Recorder, Twice("G4 A4 B4 D5 - B4 A4 G4 | A4 B4 A4 G4 E4 - D4 - | G4 A4 B4 D5 - E5 D5 B4 | A4 - G4 - G4 - . .")),
            new Voice(Instruments.Lute, Twice("B3 - D4 - B3 - D4 - | C4 - E4 - D4 - C4 - | B3 - D4 - C4 - E4 - | C4 - B3 - B3 - . .")),
            new Voice(Instruments.UprightBass, Twice("G2 . D3 . G2 . D3 . | C3 . G2 . D3 . A2 . | G2 . D3 . E3 . B2 . | D3 . D3 . G2 . . ."))));

        // The old cellar: dark and sparse.
        yield return (Music.Cellar, new Tune(
            70,
            new Voice(Instruments.Oboe with { Volume = 0.2f }, ". . F4 - E4 . . . | D4 - - - . . . . | . . F4 - G4 . A4 . | E4 - - - . . . . | . . . . . . . . | . . . . . . . . | . . A4 - Bb4 . A4 . | D4 - - - . . . ."),
            new Voice(Instruments.Cello with { Volume = 0.14f, Attack = 0.4f }, Twice("D2 - - - - - - - | D2 - - - - - - - | Bb1 - - - - - - - | A1 - - - - - - -")),
            new Voice(Instruments.Shaker with { Volume = 0.03f }, Repeat("x . . . . . . . . . . . . . . . ", 4))));

        // Paddock's races: a chase in E minor, brass over pumping cellos and timpani, then a lift into the chorus.
        yield return (Music.Race, new Tune(
            150,
            new Voice(Instruments.Brass, "E5 . E5 G5 . E5 D5 . | B4 - - - . . A4 B4 | C5 . C5 E5 . C5 B4 . | D5 - - - F#5 - G5 A5 | "
                + "B5 - A5 G5 - E5 D5 E5 | G5 - F#5 E5 - D5 B4 - | C5 - E5 G5 - A5 G5 E5 | F#5 - - - D5 - F#5 - | "
                + "E5 . E5 G5 . E5 D5 . | B4 - - - . . D5 E5 | C5 . C5 E5 . G5 F#5 . | D5 - - - A4 - B4 D5 | "
                + "E5 - G5 B5 - A5 G5 F#5 | G5 - E5 D5 - B4 A4 B4 | C5 - - E5 - - G5 - | E5 - - - . . . ."),
            new Voice(Instruments.Harpsichord with { Volume = 0.2f }, Twice(Twice("E4 G4 B4 G4 E4 G4 B4 G4 | E4 G4 B4 G4 E4 G4 B4 G4 | C4 E4 G4 E4 C4 E4 G4 E4 | D4 F#4 A4 F#4 D4 F#4 A4 F#4"))),
            new Voice(Instruments.Cello with { Attack = 0.02f, Release = 0.08f }, Twice(Twice("E2 E2 E3 E2 E2 E2 D3 E2 | E2 E2 E3 E2 G2 G2 A2 B2 | C2 C2 C3 C2 C2 C2 B2 C3 | D2 D2 D3 D2 D2 D2 F#2 A2"))),
            new Voice(Instruments.Timpani, Repeat("E2 . . E2 B1 . . . ", 16)),
            new Voice(Instruments.Snare, Repeat(". . x . . . x . ", 16)),
            new Voice(Instruments.Shaker, Repeat("x x x x x x x x ", 16))));

        // Hot Pan: a busy, bouncy kitchen in G, as if a cooking show were running late.
        yield return (Music.Kitchen, new Tune(
            138,
            new Voice(Instruments.Marimba, "G5 . D5 G5 . B5 A5 G5 | E5 . C5 E5 . G5 F#5 E5 | D5 . A4 D5 . F#5 E5 D5 | G5 - . . D5 . . . | "
                + "C6 . G5 C6 . E6 D6 C6 | B5 . G5 B5 . D6 C6 B5 | A5 . F#5 A5 . C6 B5 A5 | G5 - - - . . . ."),
            new Voice(Instruments.Pizzicato, ". B3 . B3 . B3 . B3 | . E4 . E4 . E4 . E4 | . F#4 . F#4 . F#4 . F#4 | . B3 . B3 . B3 . B3 | "
                + ". E4 . E4 . E4 . E4 | . B3 . B3 . B3 . B3 | . F#4 . F#4 . F#4 . F#4 | . B3 . B3 . . . ."),
            new Voice(Instruments.UprightBass, "G2 . D3 . G2 . D3 . | C3 . G2 . C3 . G2 . | D3 . A2 . D3 . A2 . | G2 . D3 . G2 . F#2 . | "
                + "C3 . G3 . C3 . G3 . | G2 . D3 . G2 . D3 . | D3 . A2 . D3 . F#2 . | G2 . D3 . G2 . . ."),
            new Voice(Instruments.FrameDrum, Repeat("C2 . . . C2 . . . ", 8)),
            new Voice(Instruments.Shaker, Repeat(". x . x . x . x ", 8))));
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

        // Levelling up, cracking a lock: a harp running up, a celesta sparkling on top.
        yield return (Sound.LevelUp, new Tune(
            720,
            new Voice(Instruments.Harp, "C5 E5 G5 C6 - - - -"),
            new Voice(Instruments.Celesta with { Volume = 0.2f }, ". . . C6 - - - -")) { Reverb = 0.2f });

        // Winning a game or a hand: a fanfare, as a level gained: trumpet and horn over a harp and the timpani.
        yield return (Sound.Fanfare, new Tune(
            360,
            new Voice(Instruments.Trumpet with { Volume = 0.26f }, "G4 C5 E5 G5 - E5 G5 - - - - -"),
            new Voice(Instruments.Horn with { Volume = 0.2f }, "E4 G4 C5 E5 - C5 E5 - - - - -"),
            new Voice(Instruments.Harp, "C4 E4 G4 C5 E5 G5 C6 . . . . ."),
            new Voice(Instruments.Timpani, "C2 . . . . . C2 . . . . .")) { Reverb = 0.25f });

        // Chips on the poker table: two quick clicks.
        yield return (Sound.Chips, new Tune(900, new Voice(new Instrument(Wave.Noise, Volume: 0.2f, Decay: 0.015f, Sustain: 0, Release: 0.01f), "x . x")));

        // Turning a code lock wheel.
        yield return (Sound.Click, new Tune(900, new Voice(blip with { Volume = 0.12f, Decay = 0.02f, Sustain = 0 }, "A6")));

        // A jump: a quick rising blip.
        yield return (Sound.Jump, new Tune(600, new Voice(new Instrument(Wave.Square, Volume: 0.13f, Decay: 0.09f, Sustain: 0, Release: 0.02f, Slide: 12), "C5")));

        // Burning feet: a hiss of noise.
        yield return (Sound.Sizzle, new Tune(240, new Voice(new Instrument(Wave.Noise, Volume: 0.16f, Attack: 0.01f, Decay: 0.3f, Sustain: 0, Release: 0.1f), "x - -")));

        // Something bad: losing a game, falling in the cellar: an oboe sinking over the cellos and a soft drum.
        yield return (Sound.Lose, new Tune(
            200,
            new Voice(Instruments.Oboe with { Volume = 0.3f }, "E4 - D4 - C4 - - -"),
            new Voice(Instruments.Cello, "A2 - G2 - F2 - - -"),
            new Voice(Instruments.Timpani with { Volume = 0.4f }, "A1 . . . . . . .")) { Reverb = 0.3f });
    }

    private static string Twice(string steps) => $"{steps} | {steps}";

    private static string Repeat(string steps, int times) => string.Concat(Enumerable.Repeat(steps, times));
}
