using FortyThieves;
using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;

namespace Sokoban.Desktop.Scenes;

/// <summary>Forty Thieves: the stock, the waste and eight foundations along the top, ten columns below.</summary>
internal sealed class FortyThievesScene : CardGameScene
{
    public FortyThievesScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 999_999))
    {
    }

    private FortyThievesScene(GameContext context, int deal)
        : base(context, "Forty Thieves", deal, new CardGame(FortyThievesRules.Deal(deal), new FortyThievesRules()), FortyThievesRules.FoundationTag)
    {
        View.EmptyLabel = pile => pile.Name.StartsWith("foundation", StringComparison.Ordinal) ? "A" : null;

        // One card at a time: the top of a column or of the waste.
        View.CanDrag = (pile, index) => index == pile.Count - 1
            && (pile.Name == FortyThievesRules.Waste || pile.Name.StartsWith("column", StringComparison.Ordinal));
    }

    protected override string Help => "Build down in suit, one card at a time; tap the stock for a new card";

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;
        var (card, gap, x) = Columns(area, FortyThievesRules.Columns, 4.2f);
        View.Clear();
        View.Place(table[FortyThievesRules.Stock], new Point(x(0), area.Y));
        View.Place(table[FortyThievesRules.Waste], new Point(x(1), area.Y));
        for (var i = 0; i < FortyThievesRules.Foundations; i++)
        {
            View.Place(table[FortyThievesRules.Foundation(i)], new Point(x(2 + i), area.Y));
        }

        var top = area.Y + card.Y + gap * 2;
        for (var i = 0; i < FortyThievesRules.Columns; i++)
        {
            View.Place(table[FortyThievesRules.Column(i)], new Point(x(i), top), Fan.Down, area.Bottom - top);
        }
    }

    protected override CardGameScene CreateNewDeal() => new FortyThievesScene(Context);

    protected override void OnConfirm()
    {
        var stock = Game.Table[FortyThievesRules.Stock];
        Play(new ClickPile(stock, stock.Count - 1));
    }

    protected override string? DescribeRejected(CardInput input) =>
        input is ClickPile { Pile.Name: FortyThievesRules.Stock } ? "The stock is empty" : null;

    protected override string DescribeHint(CardInput hint) =>
        hint is ClickPile { Pile.Name: FortyThievesRules.Stock } ? "Turn over a card from the stock" : base.DescribeHint(hint);
}
