using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jentl.Core.Audio;
using Jentl.Core.Graphics;
using Jentl.Core.Storage;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>How the music is chosen.</summary>
internal enum MusicMode
{
    /// <summary>By place: each asks for a mood, whose songs follow one another while the player stays.</summary>
    Area,

    /// <summary>The song chosen in the Music screen, over and over, wherever the player goes.</summary>
    Loop,

    /// <summary>The unlocked songs in a random order, wherever the player goes.</summary>
    Shuffle,
}

/// <summary>
/// The soundtrack as the player knows it, as RuneScape keeps it: places in the games ask for a mood
/// (<see cref="PlayMood"/>) and get its songs one after another, and a song heard for the first time is unlocked
/// (saved) with a notice at the top of the screen; the Music screen lists them, plays any unlocked one, and sets the
/// <see cref="Mode"/>.
/// </summary>
internal sealed class MusicLibrary
{
    private static readonly TimeSpan NoticeTime = TimeSpan.FromSeconds(4.5);

    private readonly SavedFile<MusicSave> _file;
    private readonly Dictionary<string, TimeSpan> _lengths = new(StringComparer.Ordinal);
    private readonly Stopwatch _songClock = new();
    private readonly Random _random = new();
    private string? _mood;
    private Track? _song;
    private readonly SoundPlayer _sounds;
    private readonly Fonts _fonts;
    private readonly Dictionary<string, int> _next = new(StringComparer.Ordinal);
    private readonly Stopwatch _noticeClock = new();
    private string? _notice;

    /// <param name="moods">The moods the app plays: its Music screen and shuffle keep to their songs (and the title
    /// screen's); null for the whole soundtrack.</param>
    public MusicLibrary(ISaveStore? store, SoundPlayer sounds, Fonts fonts, IReadOnlyCollection<string>? moods = null)
    {
        Tracks = moods is null ? Soundtrack.Tracks : [.. Soundtrack.Tracks.Where(t => t.Mood == Music.Menu || moods.Contains(t.Mood))];
        _sounds = sounds;
        _fonts = fonts;
        _file = SavedFile<MusicSave>.Load(store, "music.json", FromJson, ToJson, () => new MusicSave());
    }

    public MusicMode Mode
    {
        get => _file.Value.Mode;
        set
        {
            if (_file.Value.Mode != value)
            {
                _file.Value.Mode = value;
                _file.Save();
                if (value == MusicMode.Shuffle)
                {
                    PlayNext();
                }
                else if (value == MusicMode.Area && _mood is { } mood)
                {
                    _song = null;
                    PlayMood(mood);
                }
            }
        }
    }

    /// <summary>The app's songs: its moods' and the title screen's.</summary>
    public IReadOnlyList<Track> Tracks { get; }

    public int UnlockedCount => Tracks.Count(IsUnlocked);

    public bool IsUnlocked(Track track) => _file.Value.Tracks.Contains(track.Id);

    /// <summary>The song playing, if it is one of the soundtrack's.</summary>
    public Track? Playing => _sounds.CurrentMusic is { } id ? Soundtrack.Find(id) : null;

    /// <summary>
    /// The place's mood: plays one of its songs (the next in turn), unless one of them is playing already. Only
    /// remembered while the Music screen has set a song or shuffle.
    /// </summary>
    public void PlayMood(string mood)
    {
        _mood = mood;
        if (Mode != MusicMode.Area)
        {
            if (_song is null)
            {
                PlayNext();
            }

            return;
        }

        if (Playing?.Mood == mood)
        {
            return;
        }

        PlayNext();
    }

    /// <summary>Plays <paramref name="track"/>, chosen in the Music screen: it loops, unless the mode is shuffle.</summary>
    public void Choose(Track track)
    {
        if (Mode == MusicMode.Area)
        {
            _file.Value.Mode = MusicMode.Loop;
        }

        Play(track);
    }

    /// <summary>Moves on to the next song when the one playing has been heard through; call once a frame.</summary>
    public void Update()
    {
        // Only while the song is still what plays: a screen may have stopped it or played something of its own.
        if (_song is { } song && Mode != MusicMode.Loop && _sounds.CurrentMusic == song.Id && _songClock.Elapsed >= Length(song))
        {
            PlayNext();
        }
    }

    /// <summary>The next song: the mood's next in turn, or a random unlocked one when shuffling.</summary>
    private void PlayNext()
    {
        if (Mode == MusicMode.Shuffle)
        {
            var unlocked = Tracks.Where(t => IsUnlocked(t) && t != _song).ToList();
            if (unlocked.Count > 0)
            {
#pragma warning disable CA5394 // A song order, not security relevant.
                Play(unlocked[_random.Next(unlocked.Count)]);
#pragma warning restore CA5394
            }

            return;
        }

        if (_mood is not { } mood)
        {
            return;
        }

        var playlist = Soundtrack.Playlist(mood);
        if (playlist.Count == 0)
        {
            _sounds.PlayMusic(mood);
            return;
        }

        var turn = _next.GetValueOrDefault(mood);
        _next[mood] = turn + 1;
        Play(playlist[turn % playlist.Count]);
    }

    /// <summary>How long a song takes to play through once (its tune is cheap to build; rendering is what costs).</summary>
    private TimeSpan Length(Track track)
    {
        if (!_lengths.TryGetValue(track.Id, out var length))
        {
            var tune = track.Tune();
            _lengths[track.Id] = length = TimeSpan.FromSeconds(tune.Steps * tune.StepSeconds);
        }

        return length;
    }

    private void Play(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _song = track;
        _songClock.Restart();
        _sounds.PlayMusic(track.Id);
        if (_sounds.MusicEnabled && _file.Value.Tracks.Add(track.Id))
        {
            _file.Save();
            _notice = T("You have unlocked a new music track: {0}", track.Title);
            _noticeClock.Restart();
        }
    }

    /// <summary>
    /// The notice of a newly unlocked song, sliding up in the bottom-left corner above the buttons (however tall larger text makes them) and fading after a
    /// few seconds: the corner the title screens and the games leave free, where titles, scores and meters aren't.
    /// </summary>
    public void DrawNotice(SpriteBatch batch, Rectangle viewport)
    {
        if (_notice is null || _noticeClock.Elapsed > NoticeTime)
        {
            return;
        }

        var age = (float)_noticeClock.Elapsed.TotalSeconds;
        var fade = Math.Clamp(Math.Min(age * 4, ((float)NoticeTime.TotalSeconds - age) * 2), 0, 1);
        var font = _fonts.Small;
        var size = font.MeasureString(_notice);
        var pad = _fonts.Px(12);
        var (width, height) = ((int)size.X + (2 * pad), (int)size.Y + pad);
        // Above the button bar, however tall its text makes it.
        var above = ButtonBar.Height(font) + _fonts.Px(16) + _fonts.Px(6);
        var box = new Rectangle(viewport.X + pad, viewport.Bottom - above - height + (int)((1 - Math.Clamp(age * 4, 0, 1)) * _fonts.Px(30)), width, height);
        batch.FillRoundedRectangle(box, _fonts.Px(8), Palette.Panel * (0.95f * fade));
        batch.DrawText(font, _notice, box, Palette.Accent * fade, Align.Center, shadow: false);
    }

    private static MusicSave FromJson(string json)
    {
        var node = JsonNode.Parse(json);
        var save = new MusicSave
        {
            Mode = Enum.TryParse<MusicMode>((string?)node?["mode"], ignoreCase: true, out var mode) ? mode : MusicMode.Area,
        };
        if (node?["tracks"] is JsonArray tracks)
        {
            save.Tracks.UnionWith(tracks.Select(t => (string)t!));
        }

        return save;
    }

    private static string ToJson(MusicSave save) => new JsonObject
    {
        ["version"] = 1,
        ["mode"] = save.Mode.ToString().ToLowerInvariant(),
        ["tracks"] = new JsonArray([.. save.Tracks.Order(StringComparer.Ordinal).Select(t => (JsonNode?)t)]),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>What music.json keeps: the songs unlocked and how music is chosen.</summary>
    private sealed class MusicSave
    {
        public HashSet<string> Tracks { get; } = new(StringComparer.Ordinal);

        public MusicMode Mode { get; set; }
    }
}

/// <summary>The soundtrack's part of the context.</summary>
internal static class MusicContext
{
    extension(GameContext context)
    {
        public MusicLibrary Music => context.Get<MusicLibrary>();

        /// <summary>Plays one of a mood's songs (<see cref="Games.Music"/>), unlocking it if it is new.</summary>
        public void PlayMood(string mood) => context.Music.PlayMood(mood);
    }
}
