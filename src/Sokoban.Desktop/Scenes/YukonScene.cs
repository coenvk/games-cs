using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;
using Yukon;

namespace Sokoban.Desktop.Scenes;

/// <summary>Yukon: four foundations top right, seven long columns below.</summary>
internal sealed class YukonScene : CardGameScene
{
    public YukonScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 999_999))
    {
    }

    private YukonScene(GameContext context, int deal)
        : base(context, "Yukon", deal, new CardGame(YukonRules.Deal(deal), new YukonRules()), YukonRules.FoundationTag)
    {
        View.EmptyLabel = pile => pile.Name.StartsWith("foundation", StringComparison.Ordinal) ? "A"
            : pile.Name.StartsWith("column", StringComparison.Ordinal) ? "K"
            : null;

        // Any face-up card can be picked up, with everything on top of it.
        View.CanDrag = (pile, index) => pile.Name.StartsWith("column", StringComparison.Ordinal) && pile[index].FaceUp;
    }

    protected override string Help => "Move any face-up card with the cards on it; build down in alternating colours";

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;

        // Columns start up to eleven cards deep, so they get most of the height.
        var (card, gap, x) = Columns(area, YukonRules.Columns, 4.4f);
        View.Clear();
        for (var i = 0; i < YukonRules.Foundations; i++)
        {
            View.Place(table[YukonRules.Foundation(i)], new Point(x(3 + i), area.Y));
        }

        var top = area.Y + card.Y + gap * 2;
        for (var i = 0; i < YukonRules.Columns; i++)
        {
            View.Place(table[YukonRules.Column(i)], new Point(x(i), top), Fan.Down, area.Bottom - top);
        }
    }

    protected override CardGameScene CreateNewDeal() => new YukonScene(Context);
}
