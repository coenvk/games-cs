using CheeseFarm;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The meadow's fields: grass for the herd, herbs for richer milk, or left unmown for the meadow birds. Choosing a
/// field opens what it can become.
/// </summary>
internal sealed class MeadowScene : PanelScene
{
    public MeadowScene(GameContext context)
        : base(context) => Build();

    private Farm Farm => Context.Farm.Farm;

    protected override string Title => T("The meadow");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(Farm.Grazing >= Farm.Cows
            ? T("The meadow feeds {0} cows; the herd is {1}", Farm.Grazing, Farm.Cows)
            : T("The meadow feeds {0} of {1} cows: feed for the rest costs €{2} a day", Farm.Grazing, Farm.Cows, (Farm.Cows - Farm.Grazing) * Meadow.FeedPrice), Palette.Text),
        new(T("Herbs make the milk richer: {0}% of the herd grazes them", (int)MathF.Round(Farm.Richness * 100)), Palette.Accent),
        new(Farm.Fledged > 0
            ? T("Meadow birds' chicks fledged here: {0}", Farm.Fledged)
            : T("Fields left for the birds raise chicks, and walkers stop at the shop"), Palette.Muted),
    ];

    public static string Name(Field field) => field switch
    {
        Field.Herbs => T("herbs and flowers"),
        Field.Birds => T("left for the birds"),
        _ => T("grass"),
    };

    private void Build(int selected = 0)
    {
        List<MenuItem> items =
        [
            .. Farm.Fields.Select((field, i) => new MenuItem(T("Field {0}: {1}", i + 1, Name(field)), () => Scenes.Push(new FieldScene(Context, i)))),
            new MenuItem(T("Back"), () => Scenes.Pop()),
        ];
        Menu = new Menu(Context.Sounds, [.. items]) { Selected = Math.Min(selected, items.Count - 1) };
    }

    protected override void OnUncovered() => Build(Menu.Selected);
}

/// <summary>What one field can become, with what it feeds and costs.</summary>
internal sealed class FieldScene : PanelScene
{
    private readonly int _field;

    public FieldScene(GameContext context, int field)
        : base(context)
    {
        _field = field;
        var farm = context.Farm.Farm;
        Menu = new Menu(
            context.Sounds,
            [
                .. Enum.GetValues<Field>().Where(f => f != farm.Fields[field]).Select(kind => new MenuItem(Choice(kind), () => Set(kind)) { Dimmed = farm.Money < Farm.FieldCost(kind) }),
                new MenuItem(T("Back"), () => Scenes.Pop()),
            ]);
    }

    protected override string Title => T("Field {0}", _field + 1);

    protected override IReadOnlyList<PanelLine> Lines => [new(T("Now: {0}", MeadowScene.Name(Context.Farm.Farm.Fields[_field])), Palette.Accent)];

    private static string Choice(Field kind) => kind switch
    {
        Field.Herbs => T("Sow herbs and flowers: feeds 2 cows, richer milk  ·  €{0}", Meadow.SowCost),
        Field.Birds => T("Leave it for the meadow birds: feeds no cows"),
        _ => T("Grass: feeds 3 cows"),
    };

    private void Set(Field kind)
    {
        if (Context.Farm.Farm.SetField(_field, kind))
        {
            Context.Farm.Save();
            Context.Sounds.Play(Sound.LevelUp);
            Scenes.Pop();
        }
        else
        {
            Context.Sounds.Play(Sound.Error);
        }
    }
}
