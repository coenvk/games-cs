using AssociateWords;
using Jentl.Cards;
using Jentl.Core.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// An Associate Words level on the card kit's table: the draw pile and its waste top left, the category slots top right
/// (each open one labelled with how many of its cards it holds), the five columns fanned below. Drag cards, or tap one
/// to send it where it fits; tap the draw pile to turn a card. The moves left count down in the corner.
/// </summary>
internal sealed class AssociateScene : Scene
{
    private static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(1.5);

    private readonly GameContext _context;
    private readonly ButtonBar _buttons;
    private readonly TableView _view = new();
    private Vector2 _cursor;
    private bool _ended;

    public AssociateScene(GameContext context, int level)
        : this(context, AssociatePuzzle.FromList(context, level))
    {
    }

    public AssociateScene(GameContext context, AssociatePuzzle puzzle)
    {
        _context = context;
        Puzzle = puzzle;
        Game = new AssociateGame(puzzle.Level);
        _view.CanDrag = Game.CanTake;
        _view.EmptyLabel = pile => Game.IsSlot(pile) ? "?" : null;
        _view.DealIn(Game.Table[AssociateGame.Stock]);
        _buttons = new ButtonBar(
            new Button(T("Undo"), Undo),
            new Button(T("Hint"), ShowHint),
            new Button(T("Restart"), Restart),
            new Button(T("Menu"), () => Scenes.Push(new AssociatePauseScene(context, this))));
    }

    public AssociatePuzzle Puzzle { get; }

    /// <summary>The level, from 0 (or the date, or the seed).</summary>
    public int Number => Puzzle.Number;

    public AssociateGame Game { get; }

    public override IEnumerable<string> DebugInfo =>
        [$"{Puzzle.Kind} {Number}  moves {Game.MovesUsed}/{Game.Level.Moves}  done {Game.Completed}/{Game.Level.Groups.Count}  seed {Game.Level.Seed}"];

    public void Restart() => Scenes.Replace(new AssociateScene(_context, Puzzle), Navigation.Fade());

    /// <summary>The next level of the list, or another deal of the same size.</summary>
    public void NextLevel() => Scenes.Replace(
        new AssociateScene(_context, Puzzle.Kind == AssociateKind.Custom ? AssociatePuzzle.Custom(_context, Puzzle.Size) : AssociatePuzzle.FromList(_context, Number + 1)),
        Navigation.Fade());

    protected override void OnEnter() => _context.PlayMood(Music.Words);

    private const float RewardShown = 1.4f;

    private float _clock;
    private float _rewarded = float.NegativeInfinity;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _clock += (float)elapsed.TotalSeconds;
        var actions = _context.Actions;
        _cursor = input.Cursor.Position;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new AssociatePauseScene(_context, this));
            return;
        }

        if (_buttons.Update(input))
        {
            return;
        }

        if (actions.WasPressed(input, GameAction.Undo))
        {
            Undo();
        }
        else if (actions.WasPressed(input, GameAction.Hint))
        {
            ShowHint();
        }
        else if (actions.WasPressed(input, GameAction.Restart))
        {
            Restart();
            return;
        }
        else if (actions.WasPressed(input, GameAction.Confirm))
        {
            var stock = Game.Table[AssociateGame.Stock];
            Play(new ClickPile(stock, stock.Count - 1));
        }

        if (_view.Update(input, elapsed) is { } move)
        {
            Play(move);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, fonts.Px(64));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Body, Puzzle.Title, text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, T("{0} moves left", Game.MovesLeft), text, Game.MovesLeft <= 5 ? new Color(232, 72, 85) : Palette.Accent, Align.Right);

        // A move given back for a finished category rises from the count and fades.
        var since = _clock - _rewarded;
        if (since is >= 0 and < RewardShown)
        {
            var t = since / RewardShown;
            var label = AssociateGame.Reward == 1 ? T("+1 move") : T("+{0} moves", AssociateGame.Reward);
            var width = fonts.Body.MeasureString(label).X;
            batch.DrawText(fonts.Body, label, new Vector2(text.Right - width, hud.Bottom + fonts.Px(4) + (fonts.Px(30) * (1 - t))), new Color(120, 220, 120) * (1 - (t * t)), Align.Left);
        }

        var buttonsHeight = ButtonBar.Height(fonts.Small) + fonts.Px(16);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        _buttons.Draw(batch, fonts.Small, buttons);

        var area = new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - (2 * margin), buttons.Top - hud.Bottom - (2 * margin));
        LayOut(area);
        _view.Draw(batch, _context.Cards, _cursor);

        // Each open slot says which category it is gathering and how far along it is.
        foreach (var slot in Game.Slots.Where(s => !s.IsEmpty))
        {
            var (held, needed) = Game.Progress(slot);
            var bounds = _view.PileBounds(slot);
            var label = new Rectangle(bounds.X - fonts.Px(10), bounds.Bottom + fonts.Px(2), bounds.Width + fonts.Px(20), (int)fonts.Small.LineHeight);
            batch.DrawText(fonts.Small, $"{held} / {needed}", label, Palette.Accent, Align.Center);
        }
    }

    // The draw pile and waste on the left of the top row, the slots on the right; the columns fanned below.
    private void LayOut(Rectangle area)
    {
        var across = Math.Max(AssociateLevels.ColumnCount, 2 + Game.Slots.Count + 0.5f);
        var width = Math.Min(area.Width / ((across * 1.12f) - 0.12f), area.Height / 4.2f * CardArt.AspectRatio);
        var card = new Point((int)width, (int)(width / CardArt.AspectRatio));
        var step = (int)(card.X * 1.12f);
        var left = area.Center.X - (int)((((across * 1.12f) - 0.12f) * card.X) / 2);
        _view.CardSize = card;
        _view.Clear();
        _view.Place(Game.Table[AssociateGame.Stock], new Point(left, area.Y));
        _view.Place(Game.Table[AssociateGame.Waste], new Point(left + step, area.Y));
        var right = left + (int)(((across * 1.12f) - 0.12f) * card.X);
        for (var i = 0; i < Game.Slots.Count; i++)
        {
            _view.Place(Game.Slots[i], new Point(right - card.X - ((Game.Slots.Count - 1 - i) * step), area.Y));
        }

        var columnsLeft = area.Center.X - (((AssociateLevels.ColumnCount * step) - (step - card.X)) / 2);
        var top = area.Y + card.Y + (int)(_context.Fonts.Small.LineHeight * 1.6f);
        for (var i = 0; i < Game.Columns.Count; i++)
        {
            _view.Place(Game.Columns[i], new Point(columnsLeft + (i * step), top), Fan.Down, area.Bottom - top);
        }
    }

    private void Play(CardInput input)
    {
        if (_ended)
        {
            return;
        }

        if (Game.Game.Play(input) is not { } turn)
        {
            if (input is DragCards)
            {
                _context.Sounds.Play(Sound.Error);
            }

            return;
        }

        if (turn.Has("reward"))
        {
            _rewarded = _clock;
        }

        if (turn.Has("complete"))
        {
            _context.Sounds.Play(Sound.LevelUp);
            var slot = turn.Changes.OfType<CardsMoved>().Last(m => m.To.Name == AssociateGame.Done).From;
            Scenes.Effects.Emit(_view.PileBounds(slot).Center.ToVector2(), ParticleEffects.Sparkles, _view.CardSize.X / 70f);
        }
        else
        {
            _context.Sounds.Play(turn.Has("slot") ? Sound.Pickup : Sound.Card);
        }

        if (Game.IsCleared)
        {
            _ended = true;
            _context.Sounds.Play(Sound.Fanfare);
            Celebration.Confetti(Scenes);
            var best = Puzzle.Kind switch
            {
                AssociateKind.Level => _context.Associate.Record(Number, Game.MovesUsed, Game.Stars),
                AssociateKind.Daily => _context.Associate.RecordDaily(Number, Game.MovesUsed, Game.Stars),
                _ => false,
            };
            Timers.After(TimeSpan.FromSeconds(0.9), () => Scenes.Push(new AssociateEndScene(_context, this, won: true, best)));
        }
        else if (Game.IsLost)
        {
            _ended = true;
            _context.Sounds.Play(Sound.Lose);
            Timers.After(TimeSpan.FromSeconds(0.6), () => Scenes.Push(new AssociateEndScene(_context, this, won: false, best: false)));
        }
    }

    private void Undo()
    {
        if (Game.Game.Undo() is not null)
        {
            _ended = false;
            _context.Sounds.Play(Sound.Card);
        }
    }

    private void ShowHint()
    {
        if (Game.Hints(Game.Table).FirstOrDefault() is { } hint)
        {
            var (pile, index) = hint switch
            {
                DragCards drag => (drag.From, drag.Index),
                ClickPile click => (click.Pile, Math.Max(click.Index, 0)),
                _ => (Game.Table[AssociateGame.Stock], 0),
            };
            _view.Highlight(pile, pile.IsEmpty ? -1 : Math.Min(index, pile.Count - 1), HintDuration);
            _context.Sounds.Play(Sound.Navigate);
        }
    }

    /// <summary>Undoes the last move from the end panel: a lost level gets one more try at its last move.</summary>
    public void TakeBack() => Undo();
}

/// <summary>Every level, with its stars once won; levels open one after another.</summary>
internal sealed class AssociateLevelsScene : PanelScene
{
    public AssociateLevelsScene(GameContext context)
        : base(context)
    {
        var store = context.Associate;
        Menu = new Menu(
            context.Sounds,
            [
                .. Enumerable.Range(0, store.Levels.Count).Where(store.IsOpen).Select(level => new MenuItem(Label(store, level), () =>
                {
                    Scenes.Pop();
                    Scenes.Push(new AssociateScene(context, level), Navigation.Fade());
                })),
                new MenuItem(T("Back"), () => Scenes.Pop()),
            ]);
    }

    protected override string Title => T("Select level");

    private static string Label(AssociateStore store, int level) => store.Best(level) is { } best
        ? T("Level {0}  {1}  {2} moves", level + 1, new string('★', best.Stars) + new string('☆', 3 - best.Stars), best.Moves)
        : T("Level {0}", level + 1);
}

internal sealed class AssociatePauseScene : PanelScene
{
    private readonly AssociateScene _game;

    public AssociatePauseScene(GameContext context, AssociateScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Restart"), () =>
            {
                Scenes.Pop();
                _game.Restart();
            }),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => _game.Puzzle.Title;

    protected override IReadOnlyList<PanelLine> Lines =>
        [new(T("{0} of {1} categories, {2} moves left", _game.Game.Completed, _game.Game.Level.Groups.Count, _game.Game.MovesLeft), Palette.Muted)];
}

/// <summary>The end of a level: won (stars, next level) or out of moves (try again, or undo the last move).</summary>
internal sealed class AssociateEndScene : PanelScene
{
    private readonly AssociateScene _game;
    private readonly bool _won;
    private readonly bool _best;

    public AssociateEndScene(GameContext context, AssociateScene game, bool won, bool best)
        : base(context)
    {
        (_game, _won, _best) = (game, won, best);
        var next = game.Puzzle.Kind switch
        {
            AssociateKind.Level when game.Number < context.Associate.Levels.Count - 1 => T("Next level"),
            AssociateKind.Custom => T("Another deal"),
            _ => null,
        };
        List<MenuItem> items = [];
        if (won && next is not null)
        {
            items.Add(new MenuItem(next, () =>
            {
                Scenes.Pop();
                _game.NextLevel();
            }));
        }

        if (!won)
        {
            items.Add(new MenuItem(T("Undo the last move"), () =>
            {
                Scenes.Pop();
                _game.TakeBack();
            }));
        }

        items.Add(new MenuItem(won ? T("Play again") : T("Try again"), () =>
        {
            Scenes.Pop();
            _game.Restart();
        }));
        items.Add(new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
        Menu = new Menu(context.Sounds, [.. items]);
    }

    protected override string Title => !_won ? T("Out of moves")
        : _game.Puzzle.Kind == AssociateKind.Level ? T("Level {0} done!", _game.Number + 1)
        : T("Solved!");

    protected override IReadOnlyList<PanelLine> Lines => _won
        ?
        [
            new(new string('★', _game.Game.Stars) + new string('☆', 3 - _game.Game.Stars), Palette.Accent),
            new(T("{0} moves left", _game.Game.MovesLeft), Palette.Text),
            new(
                _game.Puzzle.Kind == AssociateKind.Daily ? N(Context.Associate.Streak(DateOnly.FromDateTime(DateTime.Now)), "{0} day in a row", "{0} days in a row")
                : _best ? T("Your best for this level") : T("Every word under its category"),
                Palette.Muted),
        ]
        : [new(T("{0} of {1} categories complete", _game.Game.Completed, _game.Game.Level.Groups.Count), Palette.Muted)];

    protected override void OnBack() => Navigation.ToMainMenu(Scenes, Context);
}

/// <summary>A custom game: a fresh deal of the size chosen.</summary>
internal sealed class AssociateCustomScene : PanelScene
{
    public AssociateCustomScene(GameContext context)
        : base(context) => Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Small: three categories"), () => Play(DealSize.Small)),
            new MenuItem(T("Medium: five categories"), () => Play(DealSize.Medium)),
            new MenuItem(T("Large: seven categories"), () => Play(DealSize.Large)),
            new MenuItem(T("Back"), () => Scenes.Pop()));

    protected override string Title => T("Custom game");

    protected override IReadOnlyList<PanelLine> Lines => [new(T("A fresh deal from every category"), Palette.Muted)];

    private void Play(DealSize size) => Scenes.Replace(new AssociateScene(Context, AssociatePuzzle.Custom(Context, size)), Navigation.Fade());
}
