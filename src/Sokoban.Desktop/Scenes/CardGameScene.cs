using System.Globalization;
using Jentl.Cards;
using Jentl.Core.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Jentl.Core.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>
/// Everything a card game screen shares: the top bar, buttons and keys, hints, the timer, the game's
/// automatic moves, and the win and pause screens. A game supplies its rules (through the
/// <see cref="CardGame"/>), a deal, and where its piles go; see <see cref="KlondikeScene"/> and
/// <see cref="FreeCellScene"/>.
/// </summary>
internal abstract class CardGameScene : Scene
{
    private const int Margin = 24;
    private static readonly TimeSpan AutoMoveInterval = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WinDelay = TimeSpan.FromMilliseconds(500);

    private readonly ButtonBar _buttons;
    private readonly string _homeTag;
    private TimeSpan _untilAutoMove;
    private TimeSpan _wonFor;
    private bool _winShown;
    private string? _message;
    private TimeSpan _messageFor;
    private Vector2 _cursor;

    // After an undo, automatic moves wait for the player's next move; otherwise they would
    // immediately redo what was just undone.
    private bool _autoMovesPaused;

    /// <param name="name">Shown in the top bar and menus, e.g. "FreeCell".</param>
    /// <param name="deal">Deal number; the same number always deals the same cards.</param>
    /// <param name="homeTag">Turn tag for a card reaching a foundation, which plays a sound.</param>
    protected CardGameScene(GameContext context, string name, int deal, CardGame game, string homeTag)
    {
        Context = context;
        Name = name;
        Deal = deal;
        Game = game;
        _homeTag = homeTag;
        _buttons = new ButtonBar(
            new Button("New deal", NewDeal),
            new Button("Undo", Undo),
            new Button("Hint", ShowHint),
            new Button("Menu", () => Scenes.Push(new CardPauseScene(Context, this))));
    }

    public string Name { get; }

    public int Deal { get; }

    public TimeSpan Time { get; private set; }

    public int MoveCount => Game.History.Count;

    protected GameContext Context { get; }

    protected CardGame Game { get; }

    protected TableView View { get; } = new();

    /// <summary>The line under the table when there is nothing else to say.</summary>
    protected virtual string Help => "Drag cards, or tap one to send it where it fits";

    protected override void OnEnter() => Context.Sounds.PlayMusic(Music.Cards);

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _cursor = input.Cursor.Position;
        var actions = Context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new CardPauseScene(Context, this));
            return;
        }

        if (actions.WasPressed(input, GameAction.Undo))
        {
            Undo();
        }
        else if (actions.WasPressed(input, GameAction.Redo))
        {
            Game.Redo();
        }
        else if (actions.WasPressed(input, GameAction.Hint))
        {
            ShowHint();
        }
        else if (actions.WasPressed(input, GameAction.Confirm))
        {
            OnConfirm();
        }

        if (!Game.IsWon)
        {
            Time += elapsed;
        }

        if (!_buttons.Update(input) && View.Update(input, elapsed) is { } move)
        {
            OnInput(move);
        }

        PlayAutoMoves(elapsed);
        UpdateWin(elapsed);
        _messageFor -= elapsed;
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = Context.Fonts;
        var margin = fonts.IsPortrait ? fonts.Px(Margin / 2) : fonts.Px(Margin);

        // Top bar: name and deal on the left, moves and time on the right.
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, (int)fonts.Heading.LineHeight + fonts.Px(16));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        var title = fonts.IsPortrait ? Name : string.Create(CultureInfo.InvariantCulture, $"{Name}  #{Deal}");
        batch.DrawText(fonts.Heading, title, text, Palette.Text, Align.Left);
        var stats = string.Create(CultureInfo.InvariantCulture, $"Moves {MoveCount}    {Format.Duration(Time)}");
        batch.DrawText(fonts.Body, stats, text, Game.IsWon ? Palette.Accent : Palette.Text, Align.Right);

        // Bottom: a status line above the buttons.
        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var statusHeight = (int)fonts.Small.LineHeight + fonts.Px(8);
        var status = new Rectangle(viewport.X, buttons.Top - statusHeight, viewport.Width, statusHeight);
        var showMessage = _message is not null && _messageFor > TimeSpan.Zero;
        batch.DrawText(fonts.Small, showMessage ? _message! : Help, status, showMessage ? Palette.Accent : Palette.Muted, shadow: false);
        _buttons.Draw(batch, buttonFont, buttons);

        LayOut(new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - 2 * margin, status.Top - hud.Bottom - 2 * margin));
        View.Draw(batch, Context.Cards, _cursor);
    }

    public void NewDeal() => Scenes.Replace(CreateNewDeal(), Navigation.Fade());

    public void RestartDeal()
    {
        Game.Restart();
        Time = TimeSpan.Zero;
        _winShown = false;
        _wonFor = TimeSpan.Zero;
    }

    /// <summary>The fixed deal from the command line, or a random one from 1 to <paramref name="highest"/>.</summary>
    protected static int PickDeal(GameContext context, int? deal, int highest)
    {
        ArgumentNullException.ThrowIfNull(context);
#pragma warning disable CA5394 // Picking a deal, not security relevant.
        return deal ?? context.Deal ?? Random.Shared.Next(1, highest + 1);
#pragma warning restore CA5394
    }

    /// <summary>Card size and the x of each column, for <paramref name="columns"/> evenly spaced columns.</summary>
    /// <param name="cardsHigh">How many card heights the area must fit vertically (top row plus fanned columns).</param>
    protected (Point Card, int Gap, Func<int, int> X) Columns(Rectangle area, int columns, float cardsHigh)
    {
        var gap = Math.Max(4, area.Width / 70);
        var byWidth = (area.Width - (columns - 1) * gap) / columns;
        var byHeight = (int)((area.Height - gap) / cardsHigh * CardArt.AspectRatio);
        var width = Math.Max(20, Math.Min(byWidth, byHeight));
        var card = new Point(width, (int)(width / CardArt.AspectRatio));
        var left = area.X + (area.Width - (columns * width + (columns - 1) * gap)) / 2;
        View.CardSize = card;
        return (card, gap, i => left + i * (width + gap));
    }

    /// <summary>Places every pile for this frame (call <see cref="TableView.Clear"/> first).</summary>
    protected abstract void LayOut(Rectangle area);

    protected abstract CardGameScene CreateNewDeal();

    /// <summary>Enter or Space; Klondike turns over the stock.</summary>
    protected virtual void OnConfirm()
    {
    }

    protected virtual string DescribeHint(CardInput hint) => hint switch
    {
        DragCards drag => $"Try moving the {CardName(drag.From[drag.Index].Card)}",
        ClickPile click when click.Index >= 0 => $"Try the {CardName(click.Pile[click.Index].Card)}",
        _ => "Try that pile",
    };

    /// <summary>A drag or tap on the table; plays it by default. Pyramid overrides it to pair cards with two taps.</summary>
    protected virtual void OnInput(CardInput input) => Play(input);

    /// <summary>Why a move was refused, when that is worth telling the player; null says nothing.</summary>
    protected virtual string? DescribeRejected(CardInput input) => null;

    protected void Play(CardInput input)
    {
        if (Game.Play(input) is not { } turn)
        {
            if (DescribeRejected(input) is { } reason)
            {
                ShowMessage(reason);
                Context.Sounds.Play(Sound.Error);
            }

            return;
        }

        _autoMovesPaused = false;
        Context.Sounds.Play(turn.Has(_homeTag) ? Sound.Navigate : Sound.Card);
        SparkleHome(turn);
    }

    /// <summary>Sparkles where a card has just gone home.</summary>
    private void SparkleHome(CardTurn turn)
    {
        if (!turn.Has(_homeTag))
        {
            return;
        }

        // Wherever the game sends cards home: a foundation, a completed run, Pyramid's discards, the waste in TriPeaks.
        foreach (var move in turn.Changes.OfType<CardsMoved>().Where(m => !m.To.IsEmpty).DistinctBy(m => m.To))
        {
            Celebration.Sparkle(Scenes, View.CardBounds(move.To, move.To.Count - 1).Center.ToVector2());
        }
    }

    protected static string CardName(Card card) => card.Rank switch
    {
        Card.Ace => "ace",
        Card.Jack => "jack",
        Card.Queen => "queen",
        Card.King => "king",
        _ => card.Rank.ToString(CultureInfo.InvariantCulture),
    } + " of " + card.Suit.ToString().ToLowerInvariant();

    private void Undo()
    {
        Game.Undo();
        _autoMovesPaused = true;
        _winShown = false;
    }

    private void ShowHint()
    {
        if (Game.Hints.FirstOrDefault() is { } hint)
        {
            var (pile, index) = hint switch
            {
                DragCards drag => (drag.From, drag.Index),
                ClickPile click => (click.Pile, click.Index),
                _ => (null, 0),
            };
            if (pile is not null)
            {
                View.Highlight(pile, index, HintDuration);
            }

            _message = DescribeHint(hint);
        }
        else
        {
            _message = "No moves left. Undo, or start a new deal.";
        }

        _messageFor = MessageDuration;
    }

    private void ShowMessage(string message)
    {
        _message = message;
        _messageFor = MessageDuration;
    }

    /// <summary>Plays the game's own moves one step at a time, so they animate.</summary>
    private void PlayAutoMoves(TimeSpan elapsed)
    {
        if (View.IsDragging || _autoMovesPaused)
        {
            return;
        }

        _untilAutoMove -= elapsed;
        if (_untilAutoMove <= TimeSpan.Zero && Game.PlayAutoMove() is { } turn)
        {
            _untilAutoMove = AutoMoveInterval;
            if (turn.Has(_homeTag))
            {
                Context.Sounds.Play(Sound.Navigate);
                SparkleHome(turn);
            }
        }
    }

    private void UpdateWin(TimeSpan elapsed)
    {
        if (!Game.IsWon)
        {
            _wonFor = TimeSpan.Zero;
            return;
        }

        _wonFor += elapsed;
        if (!_winShown && _wonFor >= WinDelay)
        {
            _winShown = true;
            Context.Sounds.Play(Sound.Fanfare);
            Celebration.Confetti(Scenes);
            Scenes.Push(new CardWonScene(Context, this));
        }
    }
}
