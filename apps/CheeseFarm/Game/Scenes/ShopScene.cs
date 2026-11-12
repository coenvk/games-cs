using CheeseFarm;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The farm shop: today's customers and what each wants. Choosing one sells them the wheel they'd pay most for; those
/// who were served, or for whom there's nothing on the racks, are dimmed.
/// </summary>
internal sealed class ShopScene : PanelScene
{
    public ShopScene(GameContext context)
        : base(context) => Build();

    private Farm Farm => Context.Farm.Farm;

    protected override string Title => T("Farm shop");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(Farm.Wheels.Count == 1 ? T("€ {0}  ·  1 wheel on the racks", Farm.Money) : T("€ {0}  ·  {1} wheels on the racks", Farm.Money, Farm.Wheels.Count), Palette.Accent),
        new(T("They take their flavour, one ripeness either side; exactly what they asked for pays most"), Palette.Muted),
    ];

    private void Build(int selected = 0)
    {
        var customers = Farm.Customers;
        List<MenuItem> items = [];
        for (var i = 0; i < customers.Count; i++)
        {
            var (customer, index) = (customers[i], i);
            var wish = T("{0}: {1}, {2}", customer.Name, Cheese.Name(customer.Wants), Cheese.Name(customer.Flavour));
            if (Farm.Served(index))
            {
                items.Add(new MenuItem(T("{0}  ·  served", wish), () => { }) { Dimmed = true });
                continue;
            }

            var best = Farm.Wheels.Select(w => (Wheel: w, Price: customer.Offer(w, Farm.Day))).Where(o => o.Price > 0).OrderByDescending(o => o.Price).FirstOrDefault();
            items.Add(best.Wheel is null
                ? new MenuItem(T("{0}  ·  nothing for them", wish), () => Context.Sounds.Play(Sound.Error)) { Dimmed = true }
                : new MenuItem(T("{0}  ·  sell for €{1}", wish, best.Price), () => Sell(index, best.Wheel)));
        }

        items.Add(new MenuItem(T("Back"), () => Scenes.Pop()));
        Menu = new Menu(Context.Sounds, [.. items]) { Selected = Math.Min(selected, items.Count - 1) };
    }

    private void Sell(int customer, Wheel wheel)
    {
        if (Farm.Sell(customer, wheel) > 0)
        {
            Context.Farm.Save();
            Context.Sounds.Play(Sound.Chips);
        }

        Build(Menu.Selected);
    }
}

/// <summary>What the farm can buy: cows, racks, flavours, a better kettle, signs by the road.</summary>
internal sealed class FarmShopScene : PanelScene
{
    public FarmShopScene(GameContext context)
        : base(context) => Build();

    private Farm Farm => Context.Farm.Farm;

    protected override string Title => T("Buy for the farm");

    protected override IReadOnlyList<PanelLine> Lines => [new(T("€ {0}  ·  {1} cows  ·  {2} racks", Farm.Money, Farm.Cows, Farm.Racks), Palette.Accent)];

    private static string Name(Upgrade upgrade) => upgrade switch
    {
        Upgrade.Cow => T("A cow: 25 litres more milk a day"),
        Upgrade.Rack => T("A rack: room for 4 more wheels"),
        Upgrade.Cumin => T("Cumin, for cumin cheese"),
        Upgrade.Nettle => T("Nettle, for nettle cheese"),
        Upgrade.Herbs => T("Herbs, for herb cheese"),
        Upgrade.Kettle => T("A better kettle: a degree more room either side"),
        _ => T("A sign by the road: one more customer a day"),
    };

    private void Build(int selected = 0)
    {
        List<MenuItem> items =
        [
            .. Enum.GetValues<Upgrade>().Where(Farm.Available).Select(upgrade => new MenuItem(T("{0}  ·  €{1}", Name(upgrade), Farm.Cost(upgrade)), () => Buy(upgrade)) { Dimmed = !Farm.CanBuy(upgrade) }),
            new MenuItem(T("Back"), () => Scenes.Pop()),
        ];
        Menu = new Menu(Context.Sounds, [.. items]) { Selected = Math.Min(selected, items.Count - 1) };
    }

    private void Buy(Upgrade upgrade)
    {
        if (Farm.Buy(upgrade))
        {
            Context.Farm.Save();
            Context.Sounds.Play(Sound.LevelUp);
        }
        else
        {
            Context.Sounds.Play(Sound.Error);
        }

        Build(Menu.Selected);
    }
}
