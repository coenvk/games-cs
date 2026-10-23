using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Sound and fullscreen toggles, shown over the menu or the paused level. Changes save immediately.</summary>
internal sealed class OptionsScene : Scene
{
    private readonly GameContext _context;
    private Menu _menu;

    public OptionsScene(GameContext context)
    {
        _context = context;
        _menu = BuildMenu();
    }

    public override bool IsOverlay => true;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _menu.Update(input, _context.Actions, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var lines = _context.Sounds.IsAvailable
            ? Array.Empty<PanelLine>()
            : [new PanelLine("No audio device found", Palette.Muted)];
        OverlayPanel.Draw(batch, viewport, _context.Fonts, "Options", lines, _menu);
    }

    private Menu BuildMenu(int selected = 0)
    {
        var settings = _context.Settings.Value;
        List<MenuItem> items = [new($"Sound: {OnOff(settings.Sound)}", ToggleSound)];
        if (_context.Platform.CanFullscreen)
        {
            items.Add(new MenuItem($"Fullscreen: {OnOff(settings.Fullscreen)}", ToggleFullscreen));
        }

        items.Add(new MenuItem("Back", () => Scenes.Pop()));
        return new Menu(_context.Sounds, [.. items])
        {
            Selected = selected,
        };
    }

    private void ToggleSound()
    {
        var settings = _context.Settings.Value;
        settings.Sound = !settings.Sound;
        _context.Sounds.Enabled = settings.Sound;
        _context.Sounds.Play(Sound.Navigate);
        Changed();
    }

    private void ToggleFullscreen()
    {
        var settings = _context.Settings.Value;
        settings.Fullscreen = !settings.Fullscreen;
        _context.SetFullscreen(settings.Fullscreen);
        Changed();
    }

    private void Changed()
    {
        _context.Settings.Save();
        _menu = BuildMenu(_menu.Selected);
    }

    private static string OnOff(bool value) => value ? "On" : "Off";
}
