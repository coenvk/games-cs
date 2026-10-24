using Jentl.Core.Graphics;

namespace Games.Rendering;

/// <summary>The "Retro screen" option: a soft glow, scanlines and darkened corners, like an old television.</summary>
internal static class RetroScreen
{
    private static readonly PostEffect[] Effects =
    [
        new Bloom { Threshold = 0.8f, Intensity = 0.35f },
        new Scanlines { Strength = 0.2f },
        new Vignette { Strength = 0.5f },
    ];

    public static void Apply(PostProcessor post, bool on)
    {
        ArgumentNullException.ThrowIfNull(post);
        foreach (var effect in Effects)
        {
            post.Effects.Remove(effect);
            if (on)
            {
                post.Effects.Add(effect);
            }
        }
    }
}
