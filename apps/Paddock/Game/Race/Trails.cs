using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Paddock;

namespace Games.Race;

/// <summary>What the wheels throw up, the same from above and in 3D.</summary>
internal static class Trails
{
    /// <summary>Water on a wet road, grit or snow on loose ground; nothing on dry tarmac or ice.</summary>
    public static ParticleEffect? For(Condition condition) => condition switch
    {
        Condition.Wet => ParticleEffects.Spray,
        Condition.Dirt => ParticleEffects.Kickup,
        Condition.Gravel => ParticleEffects.Kickup with { Colors = [new Color(170, 160, 140, 200), new Color(130, 122, 108, 180)] },
        Condition.Sand => ParticleEffects.Kickup with { Colors = [new Color(226, 204, 150, 200), new Color(200, 176, 120, 180)] },
        Condition.Snow => ParticleEffects.Kickup with { Colors = [new Color(255, 255, 255, 220), new Color(220, 228, 238, 200)] },
        _ => null,
    };
}
