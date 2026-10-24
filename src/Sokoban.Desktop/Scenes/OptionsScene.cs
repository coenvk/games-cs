using System.Globalization;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;

namespace Sokoban.Desktop.Scenes;

/// <summary>Sound, volume and fullscreen, shown over the menu or a paused game. Changes save immediately.</summary>
internal sealed class OptionsScene : Scene
{
    private readonly GameContext _context;
    private readonly Form _form;

    public OptionsScene(GameContext context)
    {
        _context = context;
        var settings = context.Settings.Value;
        List<FormRow> rows =
        [
            new ToggleRow("Sound", () => settings.Sound, on =>
            {
                settings.Sound = on;
                context.Sounds.Enabled = on;
                Save();
            }),
            new SliderRow("Volume", () => settings.Volume, volume =>
            {
                settings.Volume = volume;
                context.Sounds.Volume = volume;
                Save();
            }, step: 0.1f, format: v => string.Create(CultureInfo.InvariantCulture, $"{v * 100:0}%")),
        ];
        if (context.Platform.CanFullscreen)
        {
            rows.Add(new ToggleRow("Fullscreen", () => settings.Fullscreen, on =>
            {
                settings.Fullscreen = on;
                context.SetFullscreen(on);
                Save();
            }));
        }

        rows.Add(new ButtonRow("Back", () => Scenes.Pop()));
        _form = new Form(context.Sounds, [.. rows]);
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _form.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var lines = _context.Sounds.IsAvailable
            ? Array.Empty<PanelLine>()
            : [new PanelLine("No audio device found", Palette.Muted)];
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Options", lines, _form);
    }

    private void Save() => _context.Settings.Save();
}
