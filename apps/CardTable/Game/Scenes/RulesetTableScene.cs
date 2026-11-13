using System.Globalization;
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
/// Any card game for several players written as a ruleset (Crazy Eights, Colour Cards, Mau-Mau), played by people and
/// bots: the others' hands face down along the top, the stock and discard in the middle, the hand of the person at the
/// device along the bottom. With several people the device goes round, the table hidden between them. Rounds make a
/// match: each round's winner scores, and the first to <see cref="MatchPoints"/> wins it. Tap a card to play it and
/// the stock to draw; buttons keep a drawn card or name a suit. The card kit checks every move and plays the bots.
/// </summary>
internal sealed class RulesetTableScene : Scene
{
    public const int MatchPoints = 100;

    private static readonly string[] BotNames = ["Ruby", "Otto", "Mila", "Juno", "Pim", "Sam", "Ivy"];

    private readonly GameContext _context;
    private readonly CardRuleset _ruleset;
    private readonly IReadOnlyList<Player> _people;
    private readonly int[] _totals;
    private readonly TableView _view = new();
    private readonly ButtonBar _buttons = new();
    private RulesetGame _game = null!;
    private Match<RulesetGame, CardInput> _match = null!;
    private IReadOnlyDictionary<int, Rectangle> _seats = new Dictionary<int, Rectangle>();
    private string _buttonState = "";
    private string _message = "";
    private Vector2 _cursor;
    private int _viewer;
    private int _round;
    private bool _handingOver;

    /// <summary>You against bots: as many players as the ruleset likes best.</summary>
    public RulesetTableScene(GameContext context, CardRuleset ruleset)
        : this(context, ruleset, VersusBots(ruleset.DefaultPlayers - 1))
    {
    }

    public RulesetTableScene(GameContext context, CardRuleset ruleset, IReadOnlyList<Player> people)
    {
        _context = context;
        _ruleset = ruleset;
        _people = people;
        _totals = new int[people.Count];
        StartRound();
    }

    /// <summary>The person at the device and <paramref name="bots"/> bots.</summary>
    public static IReadOnlyList<Player> VersusBots(int bots) =>
        [new Player(0, T("You"), PlayerKind.Human), .. Enumerable.Range(1, bots).Select(i => new Player(i, BotNames[(i - 1) % BotNames.Length], PlayerKind.Bot))];

    /// <summary>People sharing the device.</summary>
    public static IReadOnlyList<Player> PassAndPlay(int people) =>
        [.. Enumerable.Range(0, people).Select(i => new Player(i, T("Player {0}", i + 1), PlayerKind.Human))];

    private bool Shared => _people.Count(p => p.Kind == PlayerKind.Human) > 1;

    /// <summary>The match's winner: the first to <see cref="MatchPoints"/>, once a round is over.</summary>
    private int? MatchWinner => _game.IsOver && _totals.Max() >= MatchPoints ? Array.IndexOf(_totals, _totals.Max()) : null;

    private void StartRound()
    {
#pragma warning disable CA5394 // A deal, not security relevant.
        var seed = (_context.Deal ?? Random.Shared.Next(1, 1_000_000)) + _round;
#pragma warning restore CA5394
        _round++;
        _game = new RulesetGame(_ruleset, seed, _people.Count);
        _match = new Match<RulesetGame, CardInput>(_game, _people, _ => new RulesetBot(), seed);
        _match.Moved += (seat, _) => Describe(seat);
        _viewer = _people.First(p => p.Kind == PlayerKind.Human).Seat;
        _view.DealIn(_game.Stock);
        _buttonState = "";
        _message = T("Tap a card to play it, or the stock to draw");
    }

    /// <summary>The person whose turn it is has the device now (after a handover).</summary>
    public void ConfirmHandover(int seat)
    {
        _viewer = seat;
        _handingOver = false;
        _buttonState = "";
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        _cursor = input.Cursor.Position;
        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        // Another person's turn: hide the table until they have the device.
        if (Shared && !_game.IsOver && _match.IsHumansTurn && _match.Current.Seat != _viewer)
        {
            if (!_handingOver)
            {
                _handingOver = true;
                Scenes.Push(new RulesetHandoverScene(_context, this, _match.Current));
            }

            return;
        }

        _match.Update(elapsed);
        SetButtons();
        if (_buttons.Update(input))
        {
            return;
        }

        if (_view.Update(input, elapsed) is { } move && _match.IsHumansTurn && _match.Current.Seat == _viewer && !_game.IsChoosingSuit)
        {
            Try(move);
        }
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        var margin = fonts.Px(16);

        // Top bar: the game and round, and whose turn it is.
        var hud = new Rectangle(viewport.X, viewport.Y, viewport.Width, (int)fonts.Heading.LineHeight + fonts.Px(16));
        batch.FillRectangle(hud, Palette.Panel);
        var text = hud;
        text.Inflate(-margin, 0);
        batch.DrawText(fonts.Heading, T("{0}, round {1}", _ruleset.Name, _round), text, Palette.Text, Align.Left);
        batch.DrawText(fonts.Body, Status(), text, _match.IsHumansTurn ? Palette.Accent : Palette.Text, Align.Right);

        var buttonsHeight = ButtonBar.Height(fonts.Body) + fonts.Px(20);
        var buttons = new Rectangle(viewport.X + margin, viewport.Bottom - buttonsHeight, viewport.Width - (2 * margin), buttonsHeight);
        var lineHeight = (int)fonts.Small.LineHeight + fonts.Px(8);
        var line = new Rectangle(viewport.X, buttons.Top - lineHeight, viewport.Width, lineHeight);
        batch.DrawText(fonts.Small, _message, line, Palette.Muted, shadow: false);
        _buttons.Draw(batch, fonts.Body, buttons);

        var table = new Rectangle(viewport.X + margin, hud.Bottom + lineHeight, viewport.Width - (2 * margin), line.Top - hud.Bottom - lineHeight);
        _seats = RulesetLayout.Table(_view, _game, table, viewer: _viewer);
        if (!_handingOver)
        {
            _view.Draw(batch, _context.Cards, _cursor);
        }

        // Each other player's name, card count and points under their hand; the one whose turn it is in the accent colour.
        foreach (var (seat, area) in _seats)
        {
            var label = new Rectangle(area.X, area.Bottom, area.Width, lineHeight);
            var name = string.Format(CultureInfo.InvariantCulture, "{0} ({1})  ·  {2}", _match.Players[seat].Name, _game.Hand(seat).Count, T("{0} pts", _totals[seat]));
            batch.DrawText(fonts.Small, name, label, seat == _game.CurrentSeat && !_game.IsOver ? Palette.Accent : Palette.Text, Align.Left);
        }

        batch.DrawText(fonts.Small, T("{0}: {1} pts", _match.Players[_viewer].Name, _totals[_viewer]), new Rectangle(viewport.X + margin, line.Y, viewport.Width - (2 * margin), lineHeight), Palette.Text, Align.Right, shadow: false);
        if (_game.ChosenSuit is { } suit)
        {
            var discard = _view.PileBounds(_game.Discard);
            batch.DrawText(fonts.Small, T("Suit: {0}", suit), new Rectangle(discard.X, discard.Bottom, discard.Width * 2, lineHeight), Palette.Accent, Align.Left);
        }
    }

    private string Status() =>
        MatchWinner is { } champion ? T("{0} wins the match!", _match.Players[champion].Name)
        : _game.Winner is { } winner ? T("{0} won, scoring {1}", _match.Players[winner].Name, _game.Score(winner))
        : _match.IsHumansTurn ? _game.IsChoosingSuit ? T("Name a suit") : Shared ? T("{0}'s turn", _match.Current.Name) : T("Your turn")
        : T("{0}'s turn", _match.Current.Name);

    /// <summary>A tap or drag on the table, as a move of the person at the device: playing a card from their hand, or drawing.</summary>
    private void Try(CardInput input)
    {
        var hand = _game.Hand(_viewer);
        var move = input switch
        {
            ClickPile click when click.Pile == _game.Stock => new ClickPile(_game.Stock, _game.Stock.Count - 1),
            DragCards drag when drag.From == hand => new ClickPile(hand, drag.Index),
            _ => input,
        };
        if (!_game.LegalMoves(_viewer).Contains(move))
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
        _message = turn.Has("call") && _ruleset.Turns.Call is { } called ? T("{0}: “{1}”", who, T(called.Name))
            : turn.Has("play") && _game.Discard.Top is { } played ? T("{0} played {1}", who, Name(played.Card))
            : turn.Tags.FirstOrDefault(t => t.StartsWith("chose:", StringComparison.Ordinal)) is { } chose ? T("{0} named {1}", who, chose[6..])
            : turn.Has("drew") ? T("{0} drew", who)
            : turn.Has("pass") ? T("{0} passed", who)
            : _message;
        // Down to the call's count: forgetting it cost cards.
        if (turn.Tags.FirstOrDefault(t => t.StartsWith("forgot:", StringComparison.Ordinal)) is { } forgot)
        {
            _message += " · " + T("forgot to call, and drew {0}", forgot[7..]);
        }

        // What the ruleset's script said, after what happened.
        foreach (var said in turn.Tags.Where(t => t.StartsWith("say:", StringComparison.Ordinal)))
        {
            _message += " · " + said[4..];
        }

        if (turn.Has("win"))
        {
            var score = _game.Score(seat);
            _totals[seat] += score;
            _message = T("{0} won, scoring {1}", who, score);
        }

        _context.Sounds.Play(turn.Has("win") ? Sound.Fanfare : Sound.Card);
    }

    private static string Name(Card card) => card.IsStandard ? card.Symbol : card.Face.Name;

    /// <summary>The buttons for the moment: a suit each while naming one, Keep after drawing, Pass when stuck; the next round or match when over.</summary>
    private void SetButtons()
    {
        var mine = _match.IsHumansTurn && _match.Current.Seat == _viewer;
        var moves = mine ? _game.LegalMoves(_viewer) : [];
        var state = $"{_game.IsOver}/{mine}/{_game.IsChoosingSuit}/{moves.Contains(PassTurn.Instance)}/{_game.HasDrawn}/{_viewer}/{moves.Contains(CallOut.Instance)}/{_game.HasCalled}";
        if (state == _buttonState)
        {
            return;
        }

        _buttonState = state;
        List<Button> buttons = [];
        if (MatchWinner is not null)
        {
            buttons.Add(new Button(T("New match"), () => Scenes.Replace(new RulesetTableScene(_context, _ruleset, _people), Navigation.Fade()), Highlight: true));
        }
        else if (_game.IsOver)
        {
            buttons.Add(new Button(T("Next round"), StartRound, Highlight: true));
        }
        else if (mine && _game.IsChoosingSuit)
        {
            buttons.AddRange(_game.Suits.Select(suit => new Button(suit, () => _match.Play(new ChooseSuit(suit)))));
        }
        else if (mine && moves.Contains(PassTurn.Instance))
        {
            buttons.Add(new Button(_game.HasDrawn ? T("Keep it") : T("Pass"), () => _match.Play(PassTurn.Instance)));
        }

        // The ruleset's call ("Last card!"), to make before playing down to the count; lit once made.
        if (mine && _ruleset.Turns.Call is { } call && !_game.IsOver && (moves.Contains(CallOut.Instance) || _game.HasCalled))
        {
            buttons.Insert(0, new Button(T(call.Name), () => _match.Play(CallOut.Instance), Highlight: _game.HasCalled));
        }

        buttons.Add(new Button(T("Leave"), () => Scenes.Pop()));
        _buttons.Set([.. buttons]);
    }
}

/// <summary>Who plays a ruleset game: you against bots, or people passing the device round.</summary>
internal sealed class RulesetSetupScene : PanelScene
{
    private readonly CardRuleset _ruleset;

    public RulesetSetupScene(GameContext context, CardRuleset ruleset)
        : base(context)
    {
        _ruleset = ruleset;
        var most = Math.Min(ruleset.MaxPlayers, 4);
        var fewestBots = Math.Max(1, ruleset.MinPlayers - 1);
        var bots = Enumerable.Range(fewestBots, Math.Max(0, most - fewestBots))
            .OrderByDescending(n => n == ruleset.DefaultPlayers - 1)
            .Select(n => new MenuItem(N(n, "You vs {0} bot", "You vs {0} bots"), () => Open(RulesetTableScene.VersusBots(n))));
        var fewestPeople = Math.Max(2, ruleset.MinPlayers);
        var people = Enumerable.Range(fewestPeople, Math.Max(0, most - fewestPeople + 1))
            .Select(n => new MenuItem(T("Pass and play: {0} players", n), () => Open(RulesetTableScene.PassAndPlay(n))));
        Menu = new Menu(context.Sounds, [.. bots, .. people, new MenuItem(T("Back"), () => Scenes.Pop())]);
    }

    protected override string Title => _ruleset.Name;

    protected override IReadOnlyList<PanelLine> Lines => [new(T("First to {0} points wins the match.", RulesetTableScene.MatchPoints), Palette.Muted)];

    private void Open(IReadOnlyList<Player> people) => Scenes.Replace(new RulesetTableScene(Context, _ruleset, people), Navigation.Fade());
}

/// <summary>Hides the table until the next person has the device.</summary>
internal sealed class RulesetHandoverScene : PanelScene
{
    private readonly string _name;

    public RulesetHandoverScene(GameContext context, RulesetTableScene table, Player next)
        : base(context)
    {
        _name = next.Name;
        Menu = new Menu(context.Sounds, new MenuItem(T("I'm {0}", next.Name), () =>
        {
            Scenes.Pop();
            table.ConfirmHandover(next.Seat);
        }));
    }

    protected override string Title => T("Pass the device");

    protected override IReadOnlyList<PanelLine> Lines => [new(T("It's {0}'s turn.", _name), Palette.Text), new(T("Others, look away!"), Palette.Muted)];

    // There is no backing out of a handover; the menu is the only way on.
    protected override void OnBack()
    {
    }
}
