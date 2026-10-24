using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;
using Pyramid;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>Pyramid: stock, waste and removed cards along the top, the pyramid below.</summary>
internal sealed class PyramidScene : CardGameScene
{
    private static readonly TimeSpan SelectionTime = TimeSpan.FromHours(1);

    // The first card of a pair picked by tapping; the second tap plays the pair.
    private Pile? _selected;

    public PyramidScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 999_999))
    {
    }

    private PyramidScene(GameContext context, int deal)
        : base(context, "Pyramid", deal, new CardGame(PyramidRules.Deal(deal), new PyramidRules()), PyramidRules.RemoveTag)
    {
        View.CanDrag = (pile, _) => PyramidRules.IsAvailable(Game.Table, pile);
        View.HasSlot = pile => !pile.Name.StartsWith("pyramid", StringComparison.Ordinal);
        View.EmptyLabel = pile => pile.Name == PyramidRules.Stock ? RedealsLeft() : null;
    }

    protected override string Help => T("Pair cards that add up to 13: drag one onto the other, or tap both");

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;

        // The top row, then seven rows that each overlap the one above by half a card.
        const float RowStep = 0.5f;
        var (card, gap, x) = Columns(area, PyramidRules.Rows, 2 + RowStep * (PyramidRules.Rows - 1) + 0.2f);
        View.Clear();
        View.Place(table[PyramidRules.Stock], new Point(x(0), area.Y));
        View.Place(table[PyramidRules.Waste], new Point(x(1), area.Y));
        View.Place(table[PyramidRules.Removed], new Point(x(PyramidRules.Rows - 1), area.Y));

        var top = area.Y + card.Y + gap * 2;
        var step = x(1) - x(0);
        for (var row = 0; row < PyramidRules.Rows; row++)
        {
            for (var column = 0; column <= row; column++)
            {
                var left = x(0) + (PyramidRules.Rows - 1 - row) * step / 2 + column * step;
                View.Place(table[PyramidRules.Position(row, column)], new Point(left, top + (int)(row * card.Y * RowStep)));
            }
        }
    }

    protected override CardGameScene CreateNewDeal() => new PyramidScene(Context);

    protected override void OnConfirm()
    {
        Deselect();
        var stock = Game.Table[PyramidRules.Stock];
        Play(new ClickPile(stock, stock.Count - 1));
    }

    protected override void OnInput(CardInput input)
    {
        var table = Game.Table;
        if (input is not ClickPile { Pile: var pile } || !PyramidRules.IsAvailable(table, pile) || pile.Top!.Value.Card.Rank == Card.King)
        {
            Deselect();
            Play(input);
            return;
        }

        if (_selected is { } first && PyramidRules.CanPair(table, first, pile))
        {
            Deselect();
            Play(new DragCards(first, first.Count - 1, pile));
            return;
        }

        if (_selected == pile)
        {
            Deselect();
            return;
        }

        _selected = pile;
        View.Highlight(pile, pile.Count - 1, SelectionTime);
    }

    protected override string? DescribeRejected(CardInput input) => input switch
    {
        ClickPile { Pile.Name: PyramidRules.Stock } => T("The stock has been gone through three times"),
        DragCards { From: var from, To: var to } when from.Top is { } a && to.Top is { } b && a.Card.Rank + b.Card.Rank != PyramidRules.Pair =>
            T("{0} and {1} add up to {2}, not 13", a.Card.RankLabel, b.Card.RankLabel, a.Card.Rank + b.Card.Rank),
        _ => null,
    };

    protected override string DescribeHint(CardInput hint)
    {
        _selected = null; // the hint outline replaces the selection outline
        return hint switch
        {
            ClickPile { Pile.Name: PyramidRules.Stock } when Game.Table[PyramidRules.Stock].IsEmpty => T("Turn the waste over for another pass"),
            ClickPile { Pile.Name: PyramidRules.Stock } => T("Turn over a card from the stock"),
            ClickPile { Pile: var king } => T("Remove the {0}", CardName(king.Top!.Value.Card)),
            DragCards drag => T("Pair the {0} with the {1}", CardName(drag.From.Top!.Value.Card), CardName(drag.To.Top!.Value.Card)),
            _ => base.DescribeHint(hint),
        };
    }

    private string? RedealsLeft()
    {
        var left = PyramidRules.Redeals - Game.Table.Counter(PyramidRules.RedealCounter);
        return left > 0 && !Game.Table[PyramidRules.Waste].IsEmpty ? $"{left}x" : null;
    }

    private void Deselect()
    {
        if (_selected is not null)
        {
            _selected = null;
            View.ClearHighlight();
        }
    }
}
