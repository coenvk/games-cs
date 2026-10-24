using Jentl.Core.Graphics;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;

namespace Sokoban.Desktop.Scenes;

/// <summary>Screen-wide effects on the scene manager's effects layer, so they show over win panels too.</summary>
internal static class Celebration
{
    /// <summary>Confetti thrown in from the top edge, as much as the screen is wide.</summary>
    public static void Confetti(SceneManager scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var screen = scenes.Viewport;
        var count = (int)(ParticleEffects.Confetti.Count * Math.Clamp(screen.Width / 1280f, 0.4f, 1.5f));
        scenes.Effects.EmitArea(new Rectangle(screen.X, screen.Y - 12, screen.Width, 8), ParticleEffects.Confetti, count: count);
    }

    /// <summary>A small burst of sparkles at a point on the screen.</summary>
    public static void Sparkle(SceneManager scenes, Vector2 at, ParticleEffect? effect = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        scenes.Effects.Emit(at, effect ?? ParticleEffects.Sparkles);
    }
}
