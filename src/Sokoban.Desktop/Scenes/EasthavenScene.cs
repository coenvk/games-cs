using Easthaven;
using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.Scenes;

/// <summary>Easthaven: the stock and four foundations along the top, seven columns below.</summary>
internal sealed class EasthavenScene : CardGameScene
{
    public EasthavenScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 999_999))
    {
    }

    private EasthavenScene(GameContext context, int deal)
        : base(context, "Easthaven", deal, new CardGame(EasthavenRules.Deal(deal), new EasthavenRules()), EasthavenRules.FoundationTag)
    {
        View.EmptyLabel = pile => pile.Name.StartsWith("foundation", StringComparison.Ordinal) ? "A" : null;

        // A run built down in alternating colours can be picked up whole.
        View.CanDrag = (pile, index) => pile.Name.StartsWith("column", StringComparison.Ordinal) && EasthavenRules.IsRun(pile, index);
    }

    protected override string Help => T("Build down in alternating colours; tap the stock to deal a card onto every column");

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;

        // Every deal adds a card to each column, so they get most of the height.
        var (card, gap, x) = Columns(area, EasthavenRules.Columns, 4.0f);
        View.Clear();
        View.Place(table[EasthavenRules.Stock], new Point(x(0), area.Y));
        for (var i = 0; i < EasthavenRules.Foundations; i++)
        {
            View.Place(table[EasthavenRules.Foundation(i)], new Point(x(3 + i), area.Y));
        }

        var top = area.Y + card.Y + gap * 2;
        for (var i = 0; i < EasthavenRules.Columns; i++)
        {
            View.Place(table[EasthavenRules.Column(i)], new Point(x(i), top), Fan.Down, area.Bottom - top);
        }
    }

    protected override CardGameScene CreateNewDeal() => new EasthavenScene(Context);

    protected override void OnConfirm()
    {
        var stock = Game.Table[EasthavenRules.Stock];
        Play(new ClickPile(stock, stock.Count - 1));
    }

    protected override string? DescribeRejected(CardInput input) =>
        input is ClickPile { Pile.Name: EasthavenRules.Stock } ? T("The stock is empty") : null;

    protected override string DescribeHint(CardInput hint) =>
        hint is ClickPile { Pile.Name: EasthavenRules.Stock } ? T("Deal a card onto every column") : base.DescribeHint(hint);
}
