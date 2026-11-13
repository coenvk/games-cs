using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;

namespace Games.Scenes;

/// <summary>
/// Jentl's UI pieces that no game uses yet. Nine-slice frames: one small texture, made here (a rounded frame with a
/// bevel and studs in its corners), stretched to boxes of every shape while its corners keep their size. Tooltips:
/// the box under the pointer, or chosen with left and right, says what it is, below it, or above near the bottom.
/// </summary>
internal sealed class UiScene : Scene
{
    private const int Art = 24;
    private const int Border = 8;

    private static readonly string[] Tips =
    [
        "A wide, short frame: the corners stay as drawn, the edges stretch.",
        "A tall, thin one: the same texture, the same corners.",
        "A big square. Tooltips stay inside the screen and go above when there is no room below.",
        "A small frame, smaller than its corners together: they shrink to fit.",
        "A frame at twice its corners' size.",
    ];

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly Rectangle[] _boxes = new Rectangle[5];
    private NineSlice? _frame;
    private int _chosen;

    public UiScene(GameContext context)
    {
        _context = context;
        _buttons = new ButtonBar(new Button("Back", () => Scenes.Pop()));
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        if (_context.Actions.WasPressed(input, GameAction.Left))
        {
            _chosen = (_chosen + _boxes.Length - 1) % _boxes.Length;
        }
        else if (_context.Actions.WasPressed(input, GameAction.Right))
        {
            _chosen = (_chosen + 1) % _boxes.Length;
        }

        if (input.PointerMoved && Array.FindIndex(_boxes, b => b.Contains(input.Cursor.Position)) is var hovered and >= 0)
        {
            _chosen = hovered;
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        _frame ??= MakeFrame(batch.GraphicsDevice);
        batch.FillRectangle(viewport, Palette.Background);
        batch.DrawText(fonts.Body, "Nine-slice frames and tooltips", new Vector2(viewport.Center.X, viewport.Y + fonts.Px(30)), Palette.Text, Align.Center);
        batch.DrawText(fonts.Small, "Point at a frame, or use left and right", new Vector2(viewport.Center.X, viewport.Y + fonts.Px(62)), Palette.Muted, Align.Center, shadow: false);

        var u = Math.Min(viewport.Width / 16, viewport.Height / 10);
        var top = viewport.Y + fonts.Px(100);
        var left = viewport.Center.X - (u * 7);
        _boxes[0] = new Rectangle(left, top, u * 6, u * 2);
        _boxes[1] = new Rectangle(left + (u * 7), top, u * 2, u * 5);
        _boxes[2] = new Rectangle(left + (u * 10), top, u * 4, u * 4);
        _boxes[3] = new Rectangle(left, top + (u * 3), u, u);
        _boxes[4] = new Rectangle(left + (u * 2), top + (u * 3), u * 4, u * 3);
        for (var i = 0; i < _boxes.Length; i++)
        {
            _frame.Draw(batch, _boxes[i], i == _chosen ? Palette.Accent : Color.White, scale: i == 4 ? 2 : 1);
        }

        Tooltip.Draw(batch, fonts.Small, Tips[_chosen], _boxes[_chosen], viewport, maxWidth: fonts.Px(360));

        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        _buttons.Draw(batch, fonts.Small, new Rectangle(viewport.X, viewport.Bottom - buttonsHeight, viewport.Width, buttonsHeight));
    }

    /// <summary>A 24-pixel frame: a dark rounded edge, a lighter bevel inside it, studs in the corners, a plain middle.</summary>
    private static NineSlice MakeFrame(GraphicsDevice device)
    {
        var pixels = new Color[Art * Art];
        var edge = new Color(70, 52, 40);
        var bevel = new Color(176, 140, 96);
        var middle = new Color(232, 214, 176);
        for (var y = 0; y < Art; y++)
        {
            for (var x = 0; x < Art; x++)
            {
                // Distance in from the nearest side, with the corners rounded off.
                var dx = Math.Min(x, Art - 1 - x);
                var dy = Math.Min(y, Art - 1 - y);
                var inside = dx < 3 && dy < 3 ? 3 - MathF.Sqrt(((3 - dx) * (3 - dx)) + ((3 - dy) * (3 - dy))) + 1 : Math.Min(dx, dy);
                pixels[(y * Art) + x] = inside < 0 ? Color.Transparent : inside < 2 ? edge : inside < 5 ? bevel : middle;
                if (dx is >= 3 and <= 4 && dy is >= 3 and <= 4)
                {
                    pixels[(y * Art) + x] = new Color(240, 200, 80);
                }
            }
        }

        var texture = new Texture2D(device, Art, Art);
        texture.SetData(pixels);
        return new NineSlice(texture, new Rectangle(0, 0, Art, Art), Border, Border, Border, Border);
    }
}
