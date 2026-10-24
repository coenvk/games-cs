using Jentl.Cards;
using Jentl.Core.Cards;
using Klondike;
using Microsoft.Xna.Framework;
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.Scenes;

/// <summary>Klondike: stock, waste and four foundations along the top, seven columns below.</summary>
internal sealed class KlondikeScene : CardGameScene
{
    public KlondikeScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 999_999))
    {
    }

    private KlondikeScene(GameContext context, int deal)
        : base(context, "Klondike", deal, new CardGame(KlondikeRules.Deal(deal), new KlondikeRules()), KlondikeRules.FoundationTag)
    {
        View.EmptyLabel = pile => pile.Name.StartsWith("foundation", StringComparison.Ordinal) ? "A"
            : pile.Name.StartsWith("tableau", StringComparison.Ordinal) ? "K"
            : null;
    }

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;

        // Room for the top row and a column of about two and a half card heights (fans tighten to fit).
        var (card, gap, x) = Columns(area, KlondikeRules.Columns, 3.4f);
        View.Clear();
        View.Place(table[KlondikeRules.Stock], new Point(x(0), area.Y));
        View.Place(table[KlondikeRules.Waste], new Point(x(1), area.Y));
        for (var i = 0; i < KlondikeRules.Foundations; i++)
        {
            View.Place(table[KlondikeRules.Foundation(i)], new Point(x(3 + i), area.Y));
        }

        var top = area.Y + card.Y + gap * 2;
        for (var i = 0; i < KlondikeRules.Columns; i++)
        {
            View.Place(table[KlondikeRules.Column(i)], new Point(x(i), top), Fan.Down, area.Bottom - top);
        }
    }

    protected override CardGameScene CreateNewDeal() => new KlondikeScene(Context);

    protected override void OnConfirm()
    {
        var stock = Game.Table[KlondikeRules.Stock];
        Play(new ClickPile(stock, stock.Count - 1));
    }

    protected override string DescribeHint(CardInput hint) =>
        hint is ClickPile { Pile.Name: KlondikeRules.Stock } ? T("Turn over a card from the stock") : base.DescribeHint(hint);
}
