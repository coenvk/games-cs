using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;
using TriPeaks;
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.Scenes;

/// <summary>TriPeaks: the three peaks at the top, the stock and the waste below them.</summary>
internal sealed class TriPeaksScene : CardGameScene
{
    // Each row of a peak overlaps the one above by half a card.
    private const float RowStep = 0.5f;

    public TriPeaksScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 999_999))
    {
    }

    private TriPeaksScene(GameContext context, int deal)
        : base(context, "TriPeaks", deal, new CardGame(TriPeaksRules.Deal(deal), new TriPeaksRules()), TriPeaksRules.PlayTag)
    {
        View.CanDrag = (pile, _) => TriPeaksRules.IsFree(Game.Table, pile);
        View.HasSlot = pile => !pile.Name.StartsWith("peak", StringComparison.Ordinal);
    }

    protected override string Help => T("Play cards one higher or lower than the waste");

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;
        var peaksHigh = 1 + RowStep * (TriPeaksRules.Rows - 1);
        var (card, gap, x) = Columns(area, TriPeaksRules.Width, peaksHigh + 1.3f);
        var step = x(1) - x(0);
        View.Clear();
        for (var i = 0; i < TriPeaksRules.Cards; i++)
        {
            var at = new Point(x(0) + (int)(TriPeaksRules.ColumnOf(i) * step), area.Y + (int)(TriPeaksRules.RowOf(i) * card.Y * RowStep));
            View.Place(table[TriPeaksRules.Position(i)], at);
        }

        var bottom = area.Y + (int)(card.Y * peaksHigh) + gap * 3;
        View.Place(table[TriPeaksRules.Stock], new Point(x(4) - step / 2, bottom));
        View.Place(table[TriPeaksRules.Waste], new Point(x(5) + step / 2, bottom));
    }

    protected override CardGameScene CreateNewDeal() => new TriPeaksScene(Context);

    protected override void OnConfirm()
    {
        var stock = Game.Table[TriPeaksRules.Stock];
        Play(new ClickPile(stock, stock.Count - 1));
    }

    protected override string? DescribeRejected(CardInput input)
    {
        var table = Game.Table;
        return input switch
        {
            ClickPile { Pile.Name: TriPeaksRules.Stock } => T("The stock is empty"),
            ClickPile { Pile: var pile } when TriPeaksRules.IsFree(table, pile) && table[TriPeaksRules.Waste].Top is { } top =>
                T("Play a card one higher or lower than the {0}", CardName(top.Card)),
            DragCards { From: var from, To.Name: TriPeaksRules.Waste } when table[TriPeaksRules.Waste].Top is { } top && from.Top is not null =>
                T("Play a card one higher or lower than the {0}", CardName(top.Card)),
            _ => null,
        };
    }

    protected override string DescribeHint(CardInput hint) =>
        hint is ClickPile { Pile.Name: TriPeaksRules.Stock } ? T("Turn over a card from the stock")
        : hint is ClickPile { Pile: var pile } ? T("Play the {0}", CardName(pile.Top!.Value.Card))
        : base.DescribeHint(hint);
}
