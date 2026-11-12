using Jentl.Cards;
using Jentl.Cards.Rulesets;
using Jentl.Core.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Jentl.Multiplayer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// Any card game for several players written as a ruleset (Crazy Eights, Uno), you against bots: the others' hands
/// face down along the top, the stock and discard in the middle, yours along the bottom. Tap a card to play it and
/// the stock to draw; buttons keep a drawn card or name a suit. The card kit checks every move and plays the bots.
/// </summary>
internal sealed class RulesetTableScene : Scene
{
    private static readonly string[] BotNames = ["Ruby", "Otto", "Mila", "Juno", "Pim", "Sam", "Ivy"];

    private readonly GameContext _context;
    private readonly CardRuleset _ruleset;
    private readonly RulesetGame _game;
    private readonly Match<RulesetGame, CardInput> _match;
    private readonly TableView _view = new();
    private readonly ButtonBar _buttons = new();
    private IReadOnlyDictionary<int, Rectangle> _seats = new Dictionary<int, Rectangle>();
    private string _buttonState = "";
    private string _message;
    private Vector2 _cursor;

    public RulesetTableScene(GameContext context, CardRuleset ruleset, int? players = null)
    {
        _context = context;
        _ruleset = ruleset;
        var seats = players ?? ruleset.DefaultPlayers;
#pragma warning disable CA5394 // A deal, not security relevant.
        var seed = context.Deal ?? Random.Shared.Next(1, 1_000_000);
#pragma warning restore CA5394
        _game = new RulesetGame(ruleset, seed, seats);
        var people = Enumerable.Range(0, seats)
            .Select(i => i == 0 ? new Player(0, T("You"), PlayerKind.Human) : new Player(i, BotNames[(i - 1) % BotNames.Length], PlayerKind.Bot))
            .ToList();
        _match = new Match<RulesetGame, CardInput>(_game, people, _ => new RulesetBot(), seed);
        _match.Moved += (seat, _) => Describe(seat);
        _message = T("Tap a card to play it, or the stock to draw");
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _cursor = input.Cursor.Position;
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _match.Update(elapsed);
        SetButtons();
        if (_buttons.Update(input))
        {
            return;
        }

        if (_view.Update(input, elapsed) is { } move && _match.IsHumansTurn && !_game.IsChoosingSuit)
        {
            Try(move);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);

        // Top bar: the game, and whose turn it is.
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, (int)fonts.Heading.LineHeight + fonts.Px(16));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Heading, _ruleset.Name, text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, Status(), text, _match.IsHumansTurn ? Palette.Accent : Palette.Text, Align.Right);

        var buttonsHeight = ButtonBar.Height(fonts.Body) + fonts.Px(20);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        var lineHeight = (int)fonts.Small.LineHeight + fonts.Px(8);
        var line = new Rectangle(viewport.X, buttons.Top - lineHeight, viewport.Width, lineHeight);
        batch.DrawText(fonts.Small, _message, line, Palette.Muted, shadow: false);
        _buttons.Draw(batch, fonts.Body, buttons);

        var table = new Rectangle(viewport.X + margin, hud.Bottom + lineHeight, viewport.Width - (2 * margin), line.Top - hud.Bottom - lineHeight);
        _seats = RulesetLayout.Table(_view, _game, table, viewer: 0);
        _view.Draw(batch, _context.Cards, _cursor);

        // Each other player's name and card count under their hand; the one whose turn it is in the accent colour.
        foreach (var (seat, area) in _seats)
        {
            var label = new Rectangle(area.X, area.Bottom, area.Width, lineHeight);
            var name = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1})", _match.Players[seat].Name, _game.Hand(seat).Count);
            batch.DrawText(fonts.Small, name, label, seat == _game.CurrentSeat && !_game.IsOver ? Palette.Accent : Palette.Text, Align.Left);
        }

        if (_game.ChosenSuit is { } suit)
        {
            var discard = _view.PileBounds(_game.Discard);
            batch.DrawText(fonts.Small, T("Suit: {0}", suit), new Rectangle(discard.X, discard.Bottom, discard.Width * 2, lineHeight), Palette.Accent, Align.Left);
        }
    }

    private string Status() =>
        _game.Winner is { } winner ? T("{0} won, scoring {1}", _match.Players[winner].Name, _game.Score(winner))
        : _match.IsHumansTurn ? _game.IsChoosingSuit ? T("Name a suit") : T("Your turn")
        : T("{0}'s turn", _match.Current.Name);

    /// <summary>A tap or drag on the table, as a move of the human's: playing a card from their hand, or drawing.</summary>
    private void Try(CardInput input)
    {
        var hand = _game.Hand(0);
        var move = input switch
        {
            ClickPile click when click.Pile == _game.Stock => new ClickPile(_game.Stock, _game.Stock.Count - 1),
            DragCards drag when drag.From == hand => new ClickPile(hand, drag.Index),
            _ => input,
        };
        if (!_game.LegalMoves(0).Contains(move))
        {
            if (move is ClickPile { Pile: var pile } && pile == hand && _game.Discard.Top is { } top)
            {
                _message = T("That doesn't go on {0}", Name(top.Card));
                _context.Sounds.Play(Sound.Error);
            }

            return;
        }

        _match.Play(move);
    }

    private void Describe(int seat)
    {
        var who = _match.Players[seat].Name;
        var turn = _game.Game.LastTurn!;
        _message = turn.Has("play") && _game.Discard.Top is { } played ? T("{0} played {1}", who, Name(played.Card))
            : turn.Tags.FirstOrDefault(t => t.StartsWith("chose:", StringComparison.Ordinal)) is { } chose ? T("{0} named {1}", who, chose[6..])
            : turn.Has("drew") ? T("{0} drew", who)
            : turn.Has("pass") ? T("{0} passed", who)
            : _message;
        // What the ruleset's script said, after what happened.
        foreach (var said in turn.Tags.Where(t => t.StartsWith("say:", StringComparison.Ordinal)))
        {
            _message += " · " + said[4..];
        }

        if (turn.Has("win"))
        {
            _message = T("{0} won, scoring {1}", who, _game.Score(seat));
        }

        _context.Sounds.Play(turn.Has("win") ? Sound.Fanfare : Sound.Card);
    }

    private static string Name(Card card) => card.IsStandard ? card.Symbol : card.Face.Name;

    /// <summary>The buttons for the moment: a suit each while naming one, Keep after drawing, Pass when stuck; New game when over.</summary>
    private void SetButtons()
    {
        var human = _match.IsHumansTurn;
        var moves = human ? _game.LegalMoves(0) : [];
        var state = $"{_game.IsOver}/{human}/{_game.IsChoosingSuit}/{moves.Contains(PassTurn.Instance)}/{_game.HasDrawn}";
        if (state == _buttonState)
        {
            return;
        }

        _buttonState = state;
        List<Button> buttons = [];
        if (_game.IsOver)
        {
            buttons.Add(new Button(T("New game"), () => Scenes.Replace(new RulesetTableScene(_context, _ruleset, _game.Players), Navigation.Fade()), Highlight: true));
        }
        else if (human && _game.IsChoosingSuit)
        {
            buttons.AddRange(_game.Suits.Select(suit => new Button(suit, () => _match.Play(new ChooseSuit(suit)))));
        }
        else if (human && moves.Contains(PassTurn.Instance))
        {
            buttons.Add(new Button(_game.HasDrawn ? T("Keep it") : T("Pass"), () => _match.Play(PassTurn.Instance)));
        }

        buttons.Add(new Button(T("Leave"), () => Scenes.Pop()));
        _buttons.Set([.. buttons]);
    }
}
