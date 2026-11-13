using CheeseFarm;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The market in town: fill the cart (up to four wheels, the best-paying first), see what they would fetch by the
/// piece today, and go.
/// </summary>
internal sealed class MarketScene : PanelScene
{
    private readonly HashSet<Wheel> _cart = [];

    public MarketScene(GameContext context)
        : base(context)
    {
        foreach (var wheel in Choices.Take(Market.Cart))
        {
            _cart.Add(wheel);
        }

        Build();
    }

    private Farm Farm => Context.Farm.Farm;

    /// <summary>The wheels to choose from, those fetching most first.</summary>
    private List<Wheel> Choices => [.. Farm.Wheels.OrderByDescending(Price).Take(8)];

    protected override string Title => T("Market day in town");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(Farm.Crowd >= 1.1f ? T("A busy market today") : Farm.Crowd <= 0.9f ? T("A quiet market today") : T("An ordinary market today"), Palette.Accent),
        new(T("Choose up to {0} wheels: sold by the piece, they fetch more than whole", Market.Cart), Palette.Muted),
    ];

    private int Price(Wheel wheel) => Market.Price(wheel, Farm.Day, Farm.Crowd);

    private void Build(int selected = 0)
    {
        var total = _cart.Sum(Price) - Market.Fee;
        List<MenuItem> items =
        [
            .. Choices.Select(wheel => new MenuItem(
                T("{0}, {1}, {2}  ·  {3}  ·  €{4}", Cheese.Name(wheel.Ripeness(Farm.Day)), Cheese.Name(wheel.Flavour), Cheese.Name(wheel.Size), Cheese.Stars(wheel.Stars), Price(wheel))
                    + (_cart.Contains(wheel) ? "  ·  " + T("in the cart") : ""),
                () => Toggle(wheel))
            {
                Dimmed = !_cart.Contains(wheel) && _cart.Count >= Market.Cart,
            }),
            new MenuItem(T("To market: €{0}, after €{1} for the stall", total, Market.Fee), Go) { Dimmed = _cart.Count == 0 },
            new MenuItem(T("Back"), () => Scenes.Pop()),
        ];
        Menu = new Menu(Context.Sounds, [.. items]) { Selected = Math.Min(selected, items.Count - 1) };
    }

    private void Toggle(Wheel wheel)
    {
        if (!_cart.Remove(wheel))
        {
            if (_cart.Count >= Market.Cart)
            {
                Context.Sounds.Play(Sound.Error);
                return;
            }

            _cart.Add(wheel);
        }

        Build(Menu.Selected);
    }

    private void Go()
    {
        if (Farm.GoToMarket(_cart) is { } result)
        {
            Context.Farm.Save();
            Context.Sounds.Play(Sound.Chips);
            Scenes.Replace(new MarketResultScene(Context, result));
        }
        else
        {
            Context.Sounds.Play(Sound.Error);
        }
    }
}

/// <summary>How the market went: each wheel's takings, the stall, and the day's total.</summary>
internal sealed class MarketResultScene : PanelScene
{
    private readonly MarketResult _result;

    public MarketResultScene(GameContext context, MarketResult result)
        : base(context)
    {
        _result = result;
        Menu = new Menu(context.Sounds, new MenuItem(T("Back to the farm"), () => Scenes.Pop()));
    }

    protected override string Title => T("Sold out: €{0}", _result.Total);

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        .. _result.Sales.Select(s => new PanelLine(T("{0}, {1}  ·  €{2}", Cheese.Name(s.Wheel.Flavour), Cheese.Name(s.Wheel.Size), s.Price), Palette.Text)),
        new(T("The stall: €{0}", _result.Fee), Palette.Muted),
    ];
}
