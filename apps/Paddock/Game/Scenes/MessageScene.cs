using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>A panel with a title, a few lines and buttons (OK by default).</summary>
internal sealed class MessageScene : PanelScene
{
    private readonly string _title;
    private readonly IReadOnlyList<PanelLine> _lines;

    public MessageScene(GameContext context, string title, params string[] lines)
        : this(context, title, lines, [])
    {
    }

    /// <param name="buttons">The choices; each closes the panel before doing its thing. None means a single OK.</param>
    public MessageScene(GameContext context, string title, IReadOnlyList<string> lines, IReadOnlyList<MenuItem> buttons)
        : base(context)
    {
        _title = title;
        _lines = [.. lines.Select(l => new PanelLine(l, Palette.Text))];
        MenuItem[] items = buttons.Count > 0
            ? [.. buttons.Select(b => new MenuItem(b.Label, () => { Scenes.Pop(); b.Select(); }))]
            : [new MenuItem(T("OK"), () => Scenes.Pop())];
        Menu = new Menu(context.Sounds, items);
    }

    /// <summary>A yes-or-no question; <paramref name="yes"/> runs on Yes.</summary>
    public static MessageScene Confirm(GameContext context, string title, string line, string yesLabel, Action yes) =>
        new(context, title, [line], [new MenuItem(yesLabel, yes), new MenuItem(T("Cancel"), () => { })]);

    protected override string Title => _title;

    protected override IReadOnlyList<PanelLine> Lines => _lines;
}
