using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>A panel with a title, a few lines and an OK button.</summary>
internal sealed class MessageScene : PanelScene
{
    private readonly string _title;
    private readonly IReadOnlyList<PanelLine> _lines;

    public MessageScene(GameContext context, string title, params string[] lines)
        : base(context)
    {
        _title = title;
        _lines = [.. lines.Select(l => new PanelLine(l, Palette.Text))];
        Menu = new Menu(context.Sounds, [new MenuItem(T("OK"), () => Scenes.Pop())]);
    }

    protected override string Title => _title;

    protected override IReadOnlyList<PanelLine> Lines => _lines;
}
