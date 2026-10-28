using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The keys for the game's controls, from the Options panel: choose one and press its new key. A key taken from
/// another control leaves it. Changes save immediately and the menus follow them. Keys only: the gamepad's buttons
/// are shared between controls no screen uses together, which rebinding one at a time would undo.
/// </summary>
internal sealed class ControlsScene : Scene
{
    private readonly GameContext _context;
    private readonly Form _form;

    public ControlsScene(GameContext context)
    {
        _context = context;
        List<FormRow> rows = [.. context.App.ControlActions.Select(a => new BindingRow<GameAction>(Controls.Name(a), context.Actions, a, changed: Save))];
        rows.Add(new ButtonRow(T("Reset controls"), () =>
        {
            context.Actions.Reset();
            Save();
        }));
        rows.Add(new ButtonRow(T("Back"), () => Scenes.Pop()));
        _form = new Form(context.Sounds, [.. rows]);
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _form.Update(input, Controls.Ui, elapsed);

        // The Escape that cancels waiting for a key must not also close the screen.
        if (!_form.IsEditing && _context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) =>
        OverlayPanel.Draw(batch, viewport, _context.Fonts, T("Controls"), [new PanelLine(T("Choose a control, then press its new key. Esc cancels, Backspace clears."), Palette.Muted)], _form);

    private void Save()
    {
        Controls.Follow(_context.Actions);
        _context.ControlsFile.Save();
    }
}
