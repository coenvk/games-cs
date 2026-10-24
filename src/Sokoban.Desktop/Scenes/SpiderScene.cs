using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;
using Spider;
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.Scenes;

/// <summary>Spider: the stock and eight places for completed runs along the top, ten columns below.</summary>
internal sealed class SpiderScene : CardGameScene
{
    private readonly int _suits;

    public SpiderScene(GameContext context, int suits, int? deal = null)
        : this(context, suits, PickDeal(context, deal, 999_999))
    {
    }

    private SpiderScene(GameContext context, int suits, int deal)
        : base(context, N(suits, "Spider ({0} suit)", "Spider ({0} suits)"), deal, new CardGame(SpiderRules.Deal(deal, suits), new SpiderRules()), SpiderRules.CompleteTag)
    {
        _suits = suits;
        View.CanDrag = (pile, index) => pile.Name.StartsWith("column", StringComparison.Ordinal) && SpiderRules.IsMovableRun(pile, index);
    }

    protected override string Help => T("Build runs of one suit from king to ace; tap the stock to deal");

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;

        // Columns start six deep and grow with every deal, so give them most of the height.
        var (card, gap, x) = Columns(area, SpiderRules.Columns, 4.2f);
        View.Clear();
        View.Place(table[SpiderRules.Stock], new Point(x(0), area.Y));
        for (var i = 0; i < SpiderRules.Runs; i++)
        {
            View.Place(table[SpiderRules.Completed(i)], new Point(x(2 + i), area.Y));
        }

        var top = area.Y + card.Y + gap * 2;
        for (var i = 0; i < SpiderRules.Columns; i++)
        {
            View.Place(table[SpiderRules.Column(i)], new Point(x(i), top), Fan.Down, area.Bottom - top);
        }
    }

    protected override CardGameScene CreateNewDeal() => new SpiderScene(Context, _suits);

    protected override void OnConfirm()
    {
        var stock = Game.Table[SpiderRules.Stock];
        Play(new ClickPile(stock, stock.Count - 1));
    }

    protected override string? DescribeRejected(CardInput input) =>
        input is ClickPile { Pile.Name: SpiderRules.Stock } && !Game.Table[SpiderRules.Stock].IsEmpty
            ? T("Fill every column before dealing")
            : null;

    protected override string DescribeHint(CardInput hint) =>
        hint is ClickPile { Pile.Name: SpiderRules.Stock } ? T("Deal a new row from the stock") : base.DescribeHint(hint);
}
