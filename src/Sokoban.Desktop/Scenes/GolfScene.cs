using System.Globalization;
using Golf;
using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;

namespace Sokoban.Desktop.Scenes;

/// <summary>Golf: seven fanned columns on top, the stock and the waste below them.</summary>
internal sealed class GolfScene : CardGameScene
{
    public GolfScene(GameContext context, int? deal = null)
        : this(context, PickDeal(context, deal, 999_999))
    {
    }

    private GolfScene(GameContext context, int deal)
        : base(context, "Golf", deal, new CardGame(GolfRules.Deal(deal), new GolfRules()), GolfRules.PlayTag)
    {
        View.CanDrag = (pile, index) => pile.Name.StartsWith("column", StringComparison.Ordinal) && index == pile.Count - 1;
    }

    // Like golf, the score is what is left: shown as the game goes.
    protected override string Help =>
        string.Create(CultureInfo.InvariantCulture, $"Play cards one higher or lower than the waste ({GolfRules.CardsLeft(Game.Table)} left)");

    protected override void LayOut(Rectangle area)
    {
        var table = Game.Table;
        var (card, gap, x) = Columns(area, GolfRules.Columns, 3.2f);
        View.Clear();
        var bottom = area.Bottom - card.Y;
        for (var i = 0; i < GolfRules.Columns; i++)
        {
            View.Place(table[GolfRules.Column(i)], new Point(x(i), area.Y), Fan.Down, bottom - gap * 2 - area.Y);
        }

        var step = x(1) - x(0);
        View.Place(table[GolfRules.Stock], new Point(x(2) + step / 2, bottom));
        View.Place(table[GolfRules.Waste], new Point(x(3) + step / 2, bottom));
    }

    protected override CardGameScene CreateNewDeal() => new GolfScene(Context);

    protected override void OnConfirm()
    {
        var stock = Game.Table[GolfRules.Stock];
        Play(new ClickPile(stock, stock.Count - 1));
    }

    protected override string? DescribeRejected(CardInput input)
    {
        var waste = Game.Table[GolfRules.Waste].Top?.Card;
        return input switch
        {
            ClickPile { Pile.Name: GolfRules.Stock } => "The stock is empty",
            ClickPile or DragCards when waste is { Rank: Card.King } => "Nothing goes on a king: turn over a card from the stock",
            ClickPile { Pile: var pile } when waste is { } top && !pile.IsEmpty => $"Play a card one higher or lower than the {CardName(top)}",
            _ => null,
        };
    }

    protected override string DescribeHint(CardInput hint) =>
        hint is ClickPile { Pile.Name: GolfRules.Stock } ? "Turn over a card from the stock"
        : hint is ClickPile { Pile: var pile } ? $"Play the {CardName(pile.Top!.Value.Card)}"
        : base.DescribeHint(hint);
}
