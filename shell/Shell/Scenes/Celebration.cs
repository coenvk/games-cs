using Jentl.Core.Graphics;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;

namespace Games.Scenes;

/// <summary>Screen-wide effects on the scene manager's effects layer, so they show over win panels too.</summary>
internal static class Celebration
{
    private static readonly TimeSpan SecondWave = TimeSpan.FromMilliseconds(450);

    /// <summary>Confetti thrown in from the top edge in two waves, as much as the screen is wide.</summary>
    public static void Confetti(SceneManager scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var screen = scenes.Viewport;
        var count = (int)(ParticleEffects.Confetti.Count * Math.Clamp(screen.Width / 1280f, 0.4f, 1.5f));
        scenes.Effects.EmitArea(new Rectangle(screen.X, screen.Y - 12, screen.Width, 8), ParticleEffects.Confetti, count: count);

        // A smaller second handful a moment later keeps the air full for longer.
        scenes.Timers.After(SecondWave, () => scenes.Effects.EmitArea(new Rectangle(screen.X, screen.Y - 12, screen.Width, 8), ParticleEffects.Confetti, count: count / 2));
    }

    /// <summary>A small burst of sparkles at a point on the screen.</summary>
    public static void Sparkle(SceneManager scenes, Vector2 at, ParticleEffect? effect = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        scenes.Effects.Emit(at, effect ?? ParticleEffects.Sparkles);
    }
}
