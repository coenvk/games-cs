using System.Globalization;
using Jentl.Cards;
using Jentl.Core.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Klondike;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>
/// Klondike solitaire on the Jentl card kit: <see cref="KlondikeRules"/> decide, <see cref="CardGame"/>
/// keeps the history, <see cref="TableView"/> lays out, drags and animates. This scene only arranges
/// the piles for the screen and wires up buttons and keys.
/// </summary>
internal sealed class SolitaireScene : Scene
{
    private const int Margin = 24;
    private static readonly TimeSpan AutoMoveInterval = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WinDelay = TimeSpan.FromMilliseconds(500);

    private readonly GameContext _context;
    private readonly TableView _view = new();
    private readonly ButtonBar _buttons;
    private readonly int _seed;
    private readonly CardGame _game;
    private TimeSpan _untilAutoMove;
    private TimeSpan _wonFor;
    private bool _winShown;
    private string? _message;
    private TimeSpan _messageFor;

    // Where the pointer was last update; dragged cards are drawn there.
    private Vector2 _cursor;

    /// <param name="seed">Deal number; the same number always deals the same cards.</param>
    public SolitaireScene(GameContext context, int? seed = null)
    {
        _context = context;
#pragma warning disable CA5394 // Picking a deal, not security relevant.
        _seed = seed ?? context.Deal ?? Random.Shared.Next(1, 1_000_000);
#pragma warning restore CA5394
        _game = new CardGame(KlondikeRules.Deal(_seed), new KlondikeRules());
        _view.EmptyLabel = pile => pile.Name.StartsWith("foundation", StringComparison.Ordinal) ? "A"
            : pile.Name.StartsWith("tableau", StringComparison.Ordinal) ? "K"
            : null;
        _buttons = new ButtonBar(
            new Button("New deal", NewDeal),
            new Button("Undo", Undo),
            new Button("Hint", ShowHint),
            new Button("Menu", () => Scenes.Push(new SolitairePauseScene(_context, this))));
    }

    public TimeSpan Time { get; private set; }

    public int MoveCount => _game.History.Count;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _cursor = input.Cursor.Position;
        var actions = _context.Actions;
        if (actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Push(new SolitairePauseScene(_context, this));
            return;
        }

        if (actions.WasPressed(input, GameAction.Undo))
        {
            Undo();
        }
        else if (actions.WasPressed(input, GameAction.Redo))
        {
            _game.Redo();
        }
        else if (actions.WasPressed(input, GameAction.Hint))
        {
            ShowHint();
        }
        else if (actions.WasPressed(input, GameAction.Confirm))
        {
            Play(new ClickPile(_game.Table[KlondikeRules.Stock], _game.Table[KlondikeRules.Stock].Count - 1));
        }

        if (!_game.IsWon)
        {
            Time += elapsed;
        }

        if (!_buttons.Update(input) && _view.Update(input, elapsed) is { } move)
        {
            Play(move);
        }

        PlayAutoMoves(elapsed);
        UpdateWin(elapsed);
        _messageFor -= elapsed;
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.IsPortrait ? fonts.Px(Margin / 2) : fonts.Px(Margin);

        // Top bar: title and deal on the left, moves and time on the right.
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, (int)fonts.Heading.LineHeight + fonts.Px(16));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        var title = fonts.IsPortrait ? "Solitaire" : string.Create(CultureInfo.InvariantCulture, $"Solitaire  #{_seed}");
        batch.DrawText(fonts.Heading, title, text, Palette.Text, Align.Left);
        var stats = string.Create(CultureInfo.InvariantCulture, $"Moves {MoveCount}    {Format.Duration(Time)}");
        batch.DrawText(fonts.Body, stats, text, _game.IsWon ? Palette.Accent : Palette.Text, Align.Right);

        // Bottom: a status line above the buttons.
        var buttonFont = _buttons.Width(fonts.Body) <= viewport.Width - 2 * margin ? fonts.Body : fonts.Small;
        var buttonsHeight = ButtonBar.Height(buttonFont) + fonts.Px(20);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - 2 * margin, buttonsHeight);
        var statusHeight = (int)fonts.Small.LineHeight + fonts.Px(8);
        var status = new Rectangle(viewport.X, buttons.Top - statusHeight, viewport.Width, statusHeight);
        var statusText = _message is not null && _messageFor > TimeSpan.Zero ? _message : "Drag cards, or tap one to send it where it fits";
        batch.DrawText(fonts.Small, statusText, status, _messageFor > TimeSpan.Zero ? Palette.Accent : Palette.Muted, shadow: false);
        _buttons.Draw(batch, buttonFont, buttons);

        LayOut(new Rectangle(viewport.X + margin, hud.Bottom + margin, viewport.Width - 2 * margin, status.Top - hud.Bottom - 2 * margin));
        _view.Draw(batch, _context.Cards, _cursor);
    }

    public void NewDeal() => Scenes.Replace(new SolitaireScene(_context), Navigation.Fade());

    public void RestartDeal()
    {
        _game.Restart();
        Time = TimeSpan.Zero;
        _winShown = false;
        _wonFor = TimeSpan.Zero;
    }

    /// <summary>Seven columns across; stock, waste and the four foundations above them.</summary>
    private void LayOut(Rectangle area)
    {
        var table = _game.Table;
        var gap = Math.Max(4, area.Width / 70);
        var byWidth = (area.Width - (KlondikeRules.Columns - 1) * gap) / KlondikeRules.Columns;

        // Leave room for the top row and a column of about two and a half card heights (fans tighten to fit).
        var byHeight = (int)((area.Height - gap) / 3.4f * CardArt.AspectRatio);
        var width = Math.Max(20, Math.Min(byWidth, byHeight));
        var height = (int)(width / CardArt.AspectRatio);
        _view.CardSize = new Point(width, height);

        var left = area.X + (area.Width - (KlondikeRules.Columns * width + (KlondikeRules.Columns - 1) * gap)) / 2;
        int Column(int i) => left + i * (width + gap);

        _view.Clear();
        _view.Place(table[KlondikeRules.Stock], new Point(Column(0), area.Y));
        _view.Place(table[KlondikeRules.Waste], new Point(Column(1), area.Y));
        for (var i = 0; i < KlondikeRules.Foundations; i++)
        {
            _view.Place(table[KlondikeRules.Foundation(i)], new Point(Column(3 + i), area.Y));
        }

        var tableauTop = area.Y + height + gap * 2;
        for (var i = 0; i < KlondikeRules.Columns; i++)
        {
            _view.Place(table[KlondikeRules.Column(i)], new Point(Column(i), tableauTop), Fan.Down, area.Bottom - tableauTop);
        }
    }

    private void Play(CardInput input)
    {
        if (_game.Play(input) is { } turn && turn.Has(KlondikeRules.FoundationTag))
        {
            _context.Sounds.Play(Sound.Navigate);
        }
    }

    private void Undo()
    {
        _game.Undo();
        _winShown = false;
    }

    private void ShowHint()
    {
        switch (_game.Hints.FirstOrDefault())
        {
            case DragCards drag:
                _view.Highlight(drag.From, drag.Index, HintDuration);
                _message = $"Try moving the {Name(drag.From[drag.Index].Card)}";
                break;
            case ClickPile click:
                _view.Highlight(click.Pile, click.Index, HintDuration);
                _message = "Turn over a card from the stock";
                break;
            default:
                _message = "No moves left. Undo, or start a new deal.";
                break;
        }

        _messageFor = MessageDuration;
    }

    /// <summary>Plays the game's own moves (finishing a settled game), one step at a time so they animate.</summary>
    private void PlayAutoMoves(TimeSpan elapsed)
    {
        if (_view.IsDragging)
        {
            return;
        }

        _untilAutoMove -= elapsed;
        if (_untilAutoMove <= TimeSpan.Zero && _game.PlayAutoMove() is not null)
        {
            _untilAutoMove = AutoMoveInterval;
        }
    }

    private void UpdateWin(TimeSpan elapsed)
    {
        if (!_game.IsWon)
        {
            _wonFor = TimeSpan.Zero;
            return;
        }

        _wonFor += elapsed;
        if (!_winShown && _wonFor >= WinDelay)
        {
            _winShown = true;
            _context.Sounds.Play(Sound.Completed);
            Scenes.Push(new SolitaireWonScene(_context, this));
        }
    }

    private static string Name(Card card) => card.Rank switch
    {
        Card.Ace => "ace",
        Card.Jack => "jack",
        Card.Queen => "queen",
        Card.King => "king",
        _ => card.Rank.ToString(CultureInfo.InvariantCulture),
    } + " of " + card.Suit.ToString().ToLowerInvariant();
}
