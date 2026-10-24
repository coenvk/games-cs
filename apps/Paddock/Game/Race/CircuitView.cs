using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Paddock;

namespace Games.Race;

/// <summary>
/// A round on a circuit, road or trail seen from above, close up: the camera follows the player's car (fixed north-up
/// or turning with the car), the opponent shows when near. Distances are in metres along the track.
/// </summary>
internal sealed class CircuitView(TrackArt art, Sprite player, Sprite opponent, RaceCamera mode)
{
    private readonly Camera2D _camera = new() { RoundToPixels = true };
    private bool _started;

    public TrackArt Art => art;

    /// <summary>Moves the camera to the player's car.</summary>
    public void Update(double playerMetres, Rectangle viewport, TimeSpan elapsed)
    {
        var pose = Pose(playerMetres, TrackArt.Lane);
        _camera.Viewport = viewport;
        _camera.Zoom = Math.Max(1, MathF.Round(viewport.Height / 260f));
        if (mode == RaceCamera.TurnWithCar)
        {
            if (!_started)
            {
                _camera.Rotation = pose.Heading + MathHelper.PiOver2;
            }

            _camera.FollowHeading(pose.Position, pose.Heading, elapsed, 5, new Vector2(0.5f, 0.7f));
        }
        else
        {
            _camera.Rotation = 0;
            _camera.FollowHeading(pose.Position, -MathHelper.PiOver2, elapsed, 1000, new Vector2(0.5f, 0.55f));
        }

        _started = true;
    }

    public void Draw(SpriteBatch batch, double playerMetres, double opponentMetres)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.End();
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.PointClamp, transformMatrix: _camera.Transform);
        var visible = _camera.VisibleArea;
        visible.Inflate(4, 4);
        batch.FillRectangle(visible, art.Ground);
        batch.Draw(art.World, new Vector2(art.Bounds.X, art.Bounds.Y), Color.White);
        DrawCar(batch, opponent, Pose(opponentMetres, -TrackArt.Lane));
        DrawCar(batch, player, Pose(playerMetres, TrackArt.Lane));
        batch.End();
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);
    }

    /// <summary>The mini-map with both cars, at <paramref name="at"/>'s top-left.</summary>
    public void DrawMap(SpriteBatch batch, Point at, double playerMetres, double opponentMetres, Color playerColor, Color opponentColor)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(art.Map, new Vector2(at.X, at.Y), Color.White);
        foreach (var (metres, color) in new[] { (opponentMetres, opponentColor), (playerMetres, playerColor) })
        {
            var p = art.ToMap(Pose(metres, 0).Position) + new Vector2(at.X, at.Y);
            batch.FillRectangle(new Rectangle((int)p.X - 2, (int)p.Y - 2, 5, 5), color);
        }
    }

    // Where a car is: along the centre line, shifted sideways into its lane.
    private (Vector2 Position, float Heading) Pose(double metres, float lane)
    {
        var pose = art.Path.At(metres * TrackArt.PixelsPerMetre);
        var (sin, cos) = MathF.SinCos(pose.Heading);
        var left = new Vector2(sin, -cos);
        return (new Vector2(pose.Position.X, pose.Position.Y) + (left * lane), pose.Heading);
    }

    private static void DrawCar(SpriteBatch batch, Sprite sprite, (Vector2 Position, float Heading) pose)
    {
        var origin = new Vector2(sprite.Source.Width / 2f, sprite.Source.Height / 2f);
        batch.Draw(sprite.Texture, pose.Position + new Vector2(1, 2), sprite.Source, new Color(0, 0, 0, 90), pose.Heading, origin, 1, SpriteEffects.None, 0);
        batch.Draw(sprite.Texture, pose.Position, sprite.Source, Color.White, pose.Heading, origin, 1, SpriteEffects.None, 0);
    }
}
