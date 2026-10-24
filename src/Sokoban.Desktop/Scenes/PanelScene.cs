using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Base for panels over a game: a title, some lines and a menu; Back does <see cref="OnBack"/>.</summary>
internal abstract class PanelScene(GameContext context) : Scene
{
    protected GameContext Context { get; } = context;

    protected Menu Menu { get; set; } = null!;

    public override bool IsOverlay => true;

    protected abstract string Title { get; }

    protected virtual IReadOnlyList<PanelLine> Lines => [];

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (Context.Actions.WasPressed(input, GameAction.Back))
        {
            OnBack();
            return;
        }

        Menu.Update(input, Context.Actions, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport) => OverlayPanel.Draw(batch, viewport, Context.Fonts, Title, Lines, Menu);

    protected virtual void OnBack() => Scenes.Pop();
}
