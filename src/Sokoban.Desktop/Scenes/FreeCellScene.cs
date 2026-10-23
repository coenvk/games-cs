using FreeCell;
using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;

namespace Sokoban.Desktop.Scenes;

/// <summary>FreeCell: four free cells and four foundations along the top, eight cascades below.</summary>
internal sealed class FreeCellScene : CardGameScene
{
    // The classic game's deal numbers; nearly every one of them is solvable.
    private const int ClassicDeals = 32_000;

    public FreeCellScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, ClassicDeals))
    {
    }

    private FreeCellScene(GameContext context, int deal)
        : base(context, "FreeCell", deal, new CardGame(FreeCellRules.Deal(deal), new FreeCellRules()), FreeCellRules.FoundationTag)
    {
        View.EmptyLabel = pile => pile.Name.StartsWith("foundation", StringComparison.Ordinal) ? "A" : null;

        // Only a proper run can be picked up; the rules then check it fits where it is dropped.
        View.CanDrag = (pile, index) => !pile.Name.StartsWith("foundation", StringComparison.Ordinal) && FreeCellRules.IsRun(pile, index);
    }

    protected override string Help => "Drag cards or runs; tap a card to send it where it fits";

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;

        // Cascades start seven cards deep and can grow long, so allow more height than Klondike.
        var (card, gap, x) = Columns(area, FreeCellRules.Cascades, 3.8f);
        View.Clear();
        for (var i = 0; i < FreeCellRules.FreeCells; i++)
        {
            View.Place(table[FreeCellRules.FreeCell(i)], new Point(x(i), area.Y));
        }

        for (var i = 0; i < FreeCellRules.Foundations; i++)
        {
            View.Place(table[FreeCellRules.Foundation(i)], new Point(x(FreeCellRules.FreeCells + i), area.Y));
        }

        var top = area.Y + card.Y + gap * 2;
        for (var i = 0; i < FreeCellRules.Cascades; i++)
        {
            View.Place(table[FreeCellRules.Cascade(i)], new Point(x(i), top), Fan.Down, area.Bottom - top);
        }
    }

    protected override CardGameScene CreateNewDeal() => new FreeCellScene(Context);

    protected override string DescribeHint(CardInput hint) =>
        hint is DragCards { To.Name: var to } drag && to.StartsWith("freecell", StringComparison.Ordinal)
            ? $"Park the {CardName(drag.From[drag.Index].Card)} in a free cell"
            : base.DescribeHint(hint);
}
