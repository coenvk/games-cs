using Games.Scenes;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework.Graphics;

namespace Games;

/// <summary>Jentl Lab: small demos of engine features, for trying and timing them. A tool, not one of the games.</summary>
internal sealed class LabApp : IGameApp
{
    public string Title => "Jentl Lab";

    public string SaveName => "Lab";

    public void Load(GameContext context, GraphicsDevice device)
    {
    }

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new("3D: software and GPU", () => title.Open(new ThreeDScene(context))),
            new("Traffic", () => title.Open(new TrafficScene(context))),
            new("Animation", () => title.Open(new AnimationScene(context))),
            new("UI: frames and tooltips", () => title.Open(new UiScene(context))),
        ],
        () => "Engine demos");
}
