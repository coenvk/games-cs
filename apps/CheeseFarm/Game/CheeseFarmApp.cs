using CheeseFarm;
using FontStashSharp;
using Games.Scenes;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.Storage;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using static Games.UI.Tr;

namespace Games;

/// <summary>
/// Cheese Farm: a farm in the east of the Netherlands, its cows, its cheese kitchen, its ripening cellar and its shop.
/// Make a cheese in four steps, let it ripen on the racks (turning it every other day), sell it to whoever wants it,
/// and grow the farm.
/// </summary>
internal sealed class CheeseFarmApp : IGameApp
{
    public string Title => T("Cheese Farm");

    public string SaveName => "CheeseFarm";

    public IReadOnlyCollection<string>? Moods => [Music.Village, Music.Kitchen, Music.Cellar];

    public void Load(GameContext context, GraphicsDevice device) => context.Add(new FarmStore(context.Store, context.Deal));

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new(context.Farm.Started ? T("Continue") : T("Play"), () => title.Open(new FarmScene(context))),
            .. context.Farm.Started ? [new MenuItem(T("New farm"), () => title.Open(new NewFarmScene(context), fade: false))] : Array.Empty<MenuItem>(),
        ],
        () => context.Farm.Started ? T("Day {0} on the farm", context.Farm.Farm.Day) : T("Make cheese, let it ripen, sell it at the farm"));

    public IReadOnlyList<Scene> Start(GameContext context) => [MainMenu(context)];
}

/// <summary>The farm being played, kept in farm.json after every change.</summary>
internal sealed class FarmStore
{
    private readonly SavedFile<FarmSave> _file;
    private readonly int? _seed;

    public FarmStore(ISaveStore? store, int? seed)
    {
        _seed = seed;
        _file = SavedFile<FarmSave>.Load(store, "farm.json", json => new FarmSave(CheeseFarm.Farm.FromJson(json)), s => s.Farm.ToJson(), () => new FarmSave(Fresh()));
    }

    public Farm Farm => _file.Value.Farm;

    public bool Started => Farm.Day > 1 || Farm.WheelsMade > 0;

    public void Save() => _file.Save();

    /// <summary>Starts again with a new farm.</summary>
    public void Restart()
    {
        _file.Value.Farm = Fresh();
        Save();
    }

    private Farm Fresh() => new(_seed ?? Environment.TickCount);
}

internal sealed class FarmSave(Farm farm)
{
    public Farm Farm { get; set; } = farm;
}

/// <summary>Names and colours of the farm's things, in the player's language.</summary>
internal static class Cheese
{
    public static string Name(Ripeness ripeness) => ripeness switch
    {
        Ripeness.Young => T("Young"),
        Ripeness.YoungMatured => T("Young matured"),
        Ripeness.Matured => T("Matured"),
        Ripeness.ExtraMatured => T("Extra matured"),
        Ripeness.Old => T("Old"),
        _ => T("Very old"),
    };

    public static string Name(Flavour flavour) => flavour switch
    {
        Flavour.Cumin => T("Cumin"),
        Flavour.Nettle => T("Nettle"),
        Flavour.Herbs => T("Herbs"),
        _ => T("Plain"),
    };

    public static string Name(CheeseSize size) => size switch
    {
        CheeseSize.Baby => T("baby cheese"),
        CheeseSize.Big => T("big wheel"),
        _ => T("farm wheel"),
    };

    /// <summary>How big a cheese is drawn against a farm wheel.</summary>
    public static float Scale(CheeseSize size) => size switch
    {
        CheeseSize.Baby => 0.7f,
        CheeseSize.Big => 1.25f,
        _ => 1,
    };

    public static string Stars(int stars) => new string('★', stars) + new string('☆', 5 - stars);

    /// <summary>The rind's colour: pale when young, deepening to amber as it ripens.</summary>
    public static Color Rind(Ripeness ripeness) => ripeness switch
    {
        Ripeness.Young => new Color(246, 222, 120),
        Ripeness.YoungMatured => new Color(240, 204, 92),
        Ripeness.Matured => new Color(232, 182, 64),
        Ripeness.ExtraMatured => new Color(222, 158, 48),
        Ripeness.Old => new Color(204, 128, 40),
        _ => new Color(178, 100, 34),
    };

    /// <summary>The flecks a flavour shows in the cheese, or null for plain.</summary>
    public static Color? Flecks(Flavour flavour) => flavour switch
    {
        Flavour.Cumin => new Color(110, 72, 40),
        Flavour.Nettle => new Color(70, 120, 60),
        Flavour.Herbs => new Color(90, 130, 70),
        _ => null,
    };
}

/// <summary>Cheese Farm's part of the context.</summary>
internal static class FarmContext
{
    extension(GameContext context)
    {
        public FarmStore Farm => context.Get<FarmStore>();
    }
}

/// <summary>Asks before a new farm replaces the one being played.</summary>
internal sealed class NewFarmScene : PanelScene
{
    public NewFarmScene(GameContext context)
        : base(context) => Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Start a new farm"), () =>
            {
                context.Farm.Restart();
                Scenes.Replace(new FarmScene(context), Navigation.Fade());
            }),
            new MenuItem(T("Keep this farm"), () => Scenes.Pop()));

    protected override string Title => T("A new farm?");

    protected override IReadOnlyList<PanelLine> Lines => [new(T("The cows, the cellar and the money start again."), Palette.Muted)];
}

/// <summary>Buttons the keyboard can press too: 1 for the first, 2 for the second, and so on.</summary>
internal sealed class KeyedButtons
{
    private static readonly Keys[] Digits =
    [
        Keys.D1, Keys.D2, Keys.D3,
        Keys.D4, Keys.D5, Keys.D6,
        Keys.D7, Keys.D8, Keys.D9,
    ];

    private readonly ButtonBar _bar = new();
    private Button[] _buttons = [];

    public IReadOnlyList<Button> Buttons => _buttons;

    public void Set(params Button[] buttons)
    {
        _buttons = buttons;
        _bar.Set(buttons);
    }

    /// <summary>Presses a tapped button, or the one whose number was pressed; whether one was.</summary>
    public bool Update(InputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_bar.Update(input))
        {
            return true;
        }

        for (var i = 0; i < Math.Min(Digits.Length, _buttons.Length); i++)
        {
            if (input.WasPressed(Digits[i]))
            {
                _buttons[i].Press();
                return true;
            }
        }

        return false;
    }

    public void Draw(SpriteBatch batch, SpriteFontBase font, Rectangle area) => _bar.Draw(batch, font, area);
}
