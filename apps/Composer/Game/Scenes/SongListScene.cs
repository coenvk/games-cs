using Composer;
using Jentl.Core.UI;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>The saved songs, newest first: choose one to open it, or switch to deleting and choose one to delete it.</summary>
internal sealed class SongListScene : PanelScene
{
    private bool _deleting;

    public SongListScene(GameContext context)
        : base(context) => Menu = Build();

    protected override string Title => _deleting ? T("Delete a song") : T("Open a song");

    protected override IReadOnlyList<PanelLine> Lines => Shelf.Titles.Count == 0
        ? [new(T("No songs saved yet"), Palette.Muted)]
        : [new(_deleting ? T("Choose the song to delete") : T("Choose a song to open"), Palette.Muted)];

    private SongShelf Shelf => Context.Get<SongShelf>();

    private Menu Build()
    {
        List<MenuItem> items = [];
        foreach (var title in Shelf.Titles)
        {
            items.Add(new MenuItem(title, () =>
            {
                if (_deleting)
                {
                    Shelf.Delete(title);
                    _deleting = false;
                    Menu = Build();
                }
                else if (Shelf.Open(title) is { } song)
                {
                    Scenes.Replace(new ComposerScene(Context, new ComposerSession(song)));
                }
            }));
        }

        if (items.Count > 0)
        {
            items.Add(new MenuItem(_deleting ? T("Keep them all") : T("Delete a song"), () =>
            {
                _deleting = !_deleting;
                Menu = Build();
            }));
        }

        items.Add(new MenuItem(T("Back"), () => Scenes.Pop()));
        return new Menu(Context.Sounds, [.. items]);
    }
}
