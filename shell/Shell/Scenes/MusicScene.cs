using Jentl.Core.UI;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The music player: every song of the soundtrack, the ones heard so far by name (choose one to play it), the rest
/// locked, with where they are heard.
/// </summary>
internal sealed class MusicScene : PanelScene
{
    private string? _hint;

    public MusicScene(GameContext context)
        : base(context) => Menu = Build(0);

    protected override string Title => T("Music");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(T("{0} of {1} tracks unlocked", Context.Music.UnlockedCount, Soundtrack.Tracks.Count), Palette.Muted),
        new(_hint ?? (Context.Music.Playing is { } playing ? T("Playing: {0}", playing.Title) : " "), _hint is null ? Palette.Text : Palette.Accent),
    ];

    /// <summary>Where a mood's songs are heard, for a locked song's hint.</summary>
    private static string Place(string mood) => mood switch
    {
        Music.Menu => T("on the title screens"),
        Music.Puzzle => T("in the puzzles"),
        Music.Cards => T("in solitaire"),
        Music.Table => T("at the card tables"),
        Music.Village => T("in Mossbrook's village"),
        Music.Cellar => T("in the cellar"),
        Music.Race => T("at the races"),
        Music.Kitchen => T("in Hot Pan"),
        Music.Words => T("in the word games"),
        _ => T("in Bricks"),
    };

    private static string ModeLabel(MusicMode mode) => mode switch
    {
        MusicMode.Loop => T("Mode: loop the chosen song"),
        MusicMode.Shuffle => T("Mode: shuffle"),
        _ => T("Mode: by place"),
    };

    private Menu Build(int selected)
    {
        var music = Context.Music;
        List<MenuItem> items =
        [
            new(ModeLabel(music.Mode), () =>
            {
                music.Mode = (MusicMode)(((int)music.Mode + 1) % 3);
                _hint = null;
                Menu = Build(Menu.Selected);
            }),
        ];
        foreach (var track in Soundtrack.Tracks)
        {
            var known = music.IsUnlocked(track);
            items.Add(new MenuItem(known ? track.Title : "? ? ?", () =>
            {
                if (music.IsUnlocked(track))
                {
                    _hint = null;
                    music.Choose(track);
                }
                else
                {
                    _hint = T("Heard {0}", Place(track.Mood));
                    Context.Sounds.Play(Sound.Error);
                }

                Menu = Build(Menu.Selected);
            })
            { Dimmed = !known });
        }

        items.Add(new MenuItem(T("Back"), () => Scenes.Pop()));
        return new Menu(Context.Sounds, [.. items]) { Selected = selected };
    }
}
