using System.Globalization;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.Rendering;

namespace Sokoban.Desktop.Scenes;

/// <summary>Music and sound effects (each with a volume) and fullscreen, shown over the menu or a paused game. Changes save immediately.</summary>
internal sealed class OptionsScene : Scene
{
    private readonly GameContext _context;
    private readonly Form _form;

    public OptionsScene(GameContext context)
    {
        _context = context;
        var settings = context.Settings.Value;
        static string Percent(float v) => string.Create(CultureInfo.InvariantCulture, $"{v * 100:0}%");
        List<FormRow> rows =
        [
            new ToggleRow("Music", () => settings.Music, on =>
            {
                settings.Music = on;
                context.Sounds.MusicEnabled = on;
                Save();
            }),
            new SliderRow("Music volume", () => settings.MusicVolume, volume =>
            {
                settings.MusicVolume = volume;
                context.Sounds.MusicVolume = volume;
                Save();
            }, step: 0.1f, format: Percent),
            new ToggleRow("Sound effects", () => settings.Sound, on =>
            {
                settings.Sound = on;
                context.Sounds.Enabled = on;
                Save();
            }),
            new SliderRow("Effects volume", () => settings.Volume, volume =>
            {
                settings.Volume = volume;
                context.Sounds.Volume = volume;
                context.Sounds.Play(Sound.Click);
                Save();
            }, step: 0.1f, format: Percent),
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

        rows.Add(new ToggleRow("Retro screen", () => settings.Retro, on =>
        {
            settings.Retro = on;
            RetroScreen.Apply(Scenes.Post, on);
            Save();
        }));
        // Not saved: a tool for the session (F3 does the same).
        rows.Add(new ToggleRow("Debug overlay", () => Scenes.Debug.IsVisible, on => Scenes.Debug.IsVisible = on));
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
