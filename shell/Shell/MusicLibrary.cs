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

/// <summary>
/// The soundtrack as the player knows it, as RuneScape keeps it: places in the games ask for a mood
/// (<see cref="PlayMood"/>) and get one of its songs in turn, and a song heard for the first time is unlocked (saved)
/// with a notice at the top of the screen; the Music screen lists them and plays any unlocked one.
/// </summary>
internal sealed class MusicLibrary
{
    private static readonly TimeSpan NoticeTime = TimeSpan.FromSeconds(4.5);

    private readonly SavedFile<HashSet<string>> _file;
    private readonly SoundPlayer _sounds;
    private readonly Fonts _fonts;
    private readonly Dictionary<string, int> _next = new(StringComparer.Ordinal);
    private readonly Stopwatch _noticeClock = new();
    private string? _notice;

    public MusicLibrary(ISaveStore? store, SoundPlayer sounds, Fonts fonts)
    {
        _sounds = sounds;
        _fonts = fonts;
        _file = SavedFile<HashSet<string>>.Load(store, "music.json", FromJson, ToJson, () => new HashSet<string>(StringComparer.Ordinal));
    }

    public int UnlockedCount => Soundtrack.Tracks.Count(IsUnlocked);

    public bool IsUnlocked(Track track) => _file.Value.Contains(track.Id);

    /// <summary>The song playing, if it is one of the soundtrack's.</summary>
    public Track? Playing => _sounds.CurrentMusic is { } id ? Soundtrack.Find(id) : null;

    /// <summary>Plays one of <paramref name="mood"/>'s songs (the next in turn), unless one of them is playing already.</summary>
    public void PlayMood(string mood)
    {
        if (Playing?.Mood == mood)
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

    public void Play(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _sounds.PlayMusic(track.Id);
        if (_sounds.MusicEnabled && _file.Value.Add(track.Id))
        {
            _file.Save();
            _notice = T("You have unlocked a new music track: {0}", track.Title);
            _noticeClock.Restart();
        }
    }

    /// <summary>The notice of a newly unlocked song, sliding in at the top and fading after a few seconds.</summary>
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
        var box = new Rectangle(viewport.Center.X - ((int)size.X / 2) - pad, viewport.Y + _fonts.Px(8) - (int)((1 - fade) * _fonts.Px(30)), (int)size.X + (2 * pad), (int)size.Y + pad);
        batch.FillRoundedRectangle(box, _fonts.Px(8), Palette.Panel * (0.95f * fade));
        batch.DrawText(font, _notice, box, Palette.Accent * fade, Align.Center, shadow: false);
    }

    private static HashSet<string> FromJson(string json) =>
        JsonNode.Parse(json)?["tracks"] is JsonArray tracks
            ? new HashSet<string>(tracks.Select(t => (string)t!), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static string ToJson(HashSet<string> tracks) => new JsonObject
    {
        ["version"] = 1,
        ["tracks"] = new JsonArray([.. tracks.Order(StringComparer.Ordinal).Select(t => (JsonNode?)t)]),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
