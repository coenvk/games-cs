using System.Globalization;
using Jentl.Core.Content;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Puzzle;
using Jentl.Core.Rpg;
using Jentl.Core.Scenes;
using Jentl.Puzzle;
using Jentl.Rpg;
using Jentl.Rpg.Maps;
using Jentl.Rpg.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mossbrook;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>
/// Mossbrook, the RPG demo: the map around the player with health and level on top, the latest messages
/// and Bag / Journal / Wait / Menu buttons below. Arrow keys or swipes step (into monsters to fight, into
/// villagers to talk); tapping a cell walks there.
/// </summary>
internal sealed class AdventureScene : Scene
{
    public const string Folder = "Assets/rpg";

    private const int HudHeight = 64;
    private const int LogLines = 3;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(130);
    private static readonly TimeSpan WalkInterval = TimeSpan.FromMilliseconds(130);
    private static readonly TimeSpan EndDelay = TimeSpan.FromMilliseconds(700);

    // Saved at least this often while just walking about; area changes and menus save straight away.
    private const int AutosaveTurns = 10;

    private readonly GameContext _context;
    private readonly MapView _view;
    private readonly ButtonBar _buttons;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Queue<Cell> _walk = new();
    private readonly ActionRepeater<GameAction> _repeater = new(
        RepeatDelay,
        RepeatInterval,
        GameAction.Up,
        GameAction.Down,
        GameAction.Left,
        GameAction.Right,
        GameAction.Wait);

    private GraphicsDevice? _device;
    private Rectangle _mapArea;
    private TimeSpan _untilWalk;
    private TimeSpan _overFor;
    private bool _endShown;
    private int _savedTurn;

    private AdventureScene(GameContext context, Adventure adventure)
    {
        _context = context;
        Adventure = adventure;

        // A loaded game that is already won does not celebrate again.
        _endShown = adventure.IsWon;
        _savedTurn = adventure.World.Turn;
        _view = new MapView(Texture, new FontTextDrawer(context.Fonts));
        _buttons = new ButtonBar(
            new Button("Bag", OpenBag),
            new Button("Journal", OpenJournal),
            new Button("Wait", () => Process(World.Wait())),
            new Button("Menu", () => Scenes.Push(new AdventurePauseScene(context, this))));
    }

    public Adventure Adventure { get; }

    public RpgWorld World => Adventure.World;

    /// <summary>A fresh game, replacing any saved one once it is first saved.</summary>
    public static AdventureScene New(GameContext context, int? seed = null) =>
        new(context, Adventure.Start(Read, seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000)));

    /// <summary>
    /// The saved game, or null if there is none. A save that no longer loads (after a content change) is moved
    /// aside and a new game begins, with a message saying so.
    /// </summary>
    public static AdventureScene? Continue(GameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Adventures.Read() is not { } save)
        {
            return null;
        }

        try
        {
            return new AdventureScene(context, Adventure.Load(Read, save));
        }
        catch (ContentException e)
        {
            context.Adventures.Discard(e.Message);
            var scene = New(context);
            scene.World.Log("Your saved adventure could not be loaded, so a new one begins.");
            return scene;
        }
    }

    /// <summary>Starts over with a new cellar.</summary>
    public void NewAdventure() => Scenes.Replace(New(_context), Navigation.Fade());

    /// <summary>Writes the game to the save slot; a fallen hero's save is removed instead.</summary>
    public void SaveGame()
    {
        if (World.IsOver)
        {
            _context.Adventures.Delete();
            return;
        }

        _context.Adventures.Write(Adventure.Save());
        _savedTurn = World.Turn;
    }

    /// <summary>Shows a turn's outcome: animations, a conversation, the end of the game.</summary>
    public void Process(TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _view.Show(result.Events);
        if (result.Events.Any(e => e is ActorAttacked or AreaEntered) || result.TalkTo is not null)
        {
            _walk.Clear();
        }

        if (result.Events.OfType<ItemPickedUp>().Any() || result.Events.OfType<LevelGained>().Any())
        {
            _context.Sounds.Play(Sound.Navigate);
        }

        if (result.TalkTo is { } someone)
        {
            Scenes.Push(new DialogueScene(_context, World.Talk(someone)));
        }
        else if (result.Events.Any(e => e is AreaEntered) || World.Turn - _savedTurn >= AutosaveTurns)
        {
            SaveGame();
        }
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        var actions = _context.Actions;
        _view.Update(elapsed);
        if (CheckEnd(elapsed))
        {
            return;
        }

        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new AdventurePauseScene(_context, this));
            return;
        }

        if (actions.WasPressed(input, GameAction.Inventory))
        {
            OpenBag();
            return;
        }

        if (actions.WasPressed(input, GameAction.Journal))
        {
            OpenJournal();
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        input.Gestures.SwipeDistance = Math.Max(24, _view.TileSize * 0.6f);
        switch (_repeater.Update(input, actions, elapsed))
        {
            case GameAction.Up:
                Step(GridDirection.Up);
                return;
            case GameAction.Down:
                Step(GridDirection.Down);
                return;
            case GameAction.Left:
                Step(GridDirection.Left);
                return;
            case GameAction.Right:
                Step(GridDirection.Right);
                return;
            case GameAction.Wait:
                Process(World.Wait());
                return;
        }

        if (actions.WasPressed(input, GameAction.Confirm))
        {
            Process(World.Wait());
            return;
        }

        if (input.Swipe is { } swipe)
        {
            Step(GridView.ToGrid(swipe));
            return;
        }

        if (input.Tap is { } tap && _mapArea.Contains(tap) && _view.CellAt(tap) is { } cell)
        {
            TapCell(cell);
            return;
        }

        WalkOn(elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        _device ??= batch.GraphicsDevice;
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);
        var player = World.Player;

        // Top bar: where you are, then health, level and coins.
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(HudHeight));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        var stats = string.Create(
            CultureInfo.InvariantCulture,
            $"Lv {player.Level}   {World.State.Inventory.Count("coins")} coins");
        var statsWidth = (int)fonts.Body.MeasureString(stats).X;
        var barWidth = Math.Min(fonts.Px(200), Math.Max(fonts.Px(80), text.Width - statsWidth - margin * 2 - (int)fonts.Body.MeasureString(World.Area.Name).X));
        if (!fonts.IsPortrait)
        {
            batch.DrawText(fonts.Body, World.Area.Name, text, Palette.Text, Align.Left);
        }

        var bar = new Rectangle(
            fonts.IsPortrait ? text.X : text.Right - statsWidth - margin - barWidth,
            hud.Center.Y - fonts.Px(10),
            barWidth,
            fonts.Px(20));
        batch.FillRectangle(bar, Palette.Ink);
        batch.FillRectangle(bar with { Width = bar.Width * player.Hp / player.MaxHp }, player.Hp * 3 <= player.MaxHp ? new Color(220, 70, 60) : Palette.Solved);
        batch.DrawText(fonts.Small, string.Create(CultureInfo.InvariantCulture, $"{player.Hp}/{player.MaxHp}"), bar, Palette.Text, shadow: false);
        batch.DrawText(fonts.Body, stats, text, Palette.Accent, Align.Right);

        // Footer: the latest messages above the buttons.
        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var lineHeight = (int)fonts.Small.LineHeight;
        var logHeight = LogLines * lineHeight + fonts.Px(12);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var log = new Rectangle(viewport.X, buttons.Top - logHeight, viewport.Width, logHeight);
        _mapArea = new Rectangle(viewport.X, hud.Bottom, viewport.Width, log.Top - hud.Bottom);

        batch.FillRectangle(_mapArea, new Color(8, 9, 14));
        _view.Draw(batch, _mapArea, World.Area, player);

        batch.FillRectangle(new Rectangle(log.X, log.Y, log.Width, viewport.Bottom - log.Y), Palette.Background);
        var messages = World.Messages.TakeLast(LogLines).ToArray();
        var lines = messages.SelectMany(m => fonts.Small.Wrap(m, log.Width - 2 * margin)).TakeLast(LogLines).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            var color = i == lines.Length - 1 ? Palette.Text : Palette.Muted;
            batch.DrawText(fonts.Small, lines[i], new Vector2(log.X + margin, log.Y + fonts.Px(6) + i * lineHeight), color, shadow: false);
        }

        _buttons.Draw(batch, buttonFont, buttons);
    }

    protected override void OnCovered()
    {
        // Only the top scene updates, so finish animations rather than freeze them under a dialogue or the bag.
        _view.Stop();
        _repeater.Reset();
        _walk.Clear();
        SaveGame();
    }

    // A new game takes the save slot straight away, so "New adventure" really does start over.
    protected override void OnEnter() => SaveGame();

    // After a conversation, the bag or the pause menu: whatever they changed is kept.
    protected override void OnUncovered() => SaveGame();

    protected override void OnExit() => SaveGame();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var texture in _textures.Values)
            {
                texture.Dispose();
            }

            _textures.Clear();
        }

        base.Dispose(disposing);
    }

    private static string Read(string name) => GameFiles.ReadAllText($"{Folder}/{name}");

    private Texture2D Texture(Tileset tileset)
    {
        if (!_textures.TryGetValue(tileset.Image, out var texture))
        {
            texture = GameFiles.LoadTexture(_device!, $"{Folder}/{tileset.Image}");
            _textures[tileset.Image] = texture;
        }

        return texture;
    }

    private void Step(GridDirection direction)
    {
        _walk.Clear();
        Process(World.Step(direction));
    }

    /// <summary>Next to the player: step (or fight, or talk) that way. Further: walk there along the shortest known path.</summary>
    private void TapCell(Cell cell)
    {
        var area = World.Area;
        var from = World.Player.Position;
        if (from.DirectionTo(cell) is { } direction)
        {
            Step(direction);
            return;
        }

        if (!area.IsExplored(cell) || area.Map.IsSolid(cell))
        {
            return;
        }

        _walk.Clear();
        if (Pathfinder.Find(from, cell, c => area.IsExplored(c) && area.CanEnter(c), maxSearch: 2000) is { } path)
        {
            foreach (var next in path)
            {
                _walk.Enqueue(next);
            }

            _untilWalk = TimeSpan.Zero;
        }
    }

    private void WalkOn(TimeSpan elapsed)
    {
        if (_walk.Count == 0)
        {
            return;
        }

        _untilWalk -= elapsed;
        if (_untilWalk > TimeSpan.Zero)
        {
            return;
        }

        _untilWalk += WalkInterval;
        var next = _walk.Dequeue();
        if (World.Player.Position.DirectionTo(next) is not { } direction)
        {
            _walk.Clear();
            return;
        }

        var area = World.Area;
        Process(World.Step(direction));

        // Stop walking when a monster comes into view, so the player decides what to do.
        if (area == World.Area && area.Actors.Any(a => a.Faction == Faction.Hostile && area.IsVisible(a.Position)
            && Pathfinder.Distance(a.Position, World.Player.Position) <= 5))
        {
            _walk.Clear();
        }
    }

    private bool CheckEnd(TimeSpan elapsed)
    {
        if (_endShown)
        {
            return false;
        }

        if (Adventure.IsWon)
        {
            _endShown = true;
            _context.Sounds.Play(Sound.Completed);
            Scenes.Push(new AdventureEndScene(_context, this, won: true));
            return true;
        }

        if (World.IsOver)
        {
            // Let the final blow show before the game over panel.
            _overFor += elapsed;
            if (_overFor >= EndDelay)
            {
                _endShown = true;
                SaveGame();
                Scenes.Push(new AdventureEndScene(_context, this, won: false));
            }

            return true;
        }

        return false;
    }

    private void OpenBag() => Scenes.Push(new BagScene(_context, this));

    private void OpenJournal() => Scenes.Push(new JournalScene(_context, World));
}
