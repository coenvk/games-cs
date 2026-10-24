using System.Globalization;
using Microsoft.Xna.Framework;
using Sokoban.Desktop.UI;
using Jentl.Core.UI;
using static Sokoban.Desktop.UI.Tr;

namespace Sokoban.Desktop.Scenes;

/// <summary>Who plays poker, from the Card games menu.</summary>
internal sealed class PokerSetupScene : PanelScene
{
    public PokerSetupScene(GameContext context)
        : base(context)
    {
        PokerSetup[] setups = [PokerSetup.VersusBots(3), PokerSetup.VersusBots(5), PokerSetup.VersusBots(1), PokerSetup.PassAndPlay(2), PokerSetup.PassAndPlay(3), PokerSetup.PassAndPlay(4)];
        Menu = new Menu(context.Sounds, [.. setups.Select(s => new MenuItem(s.Name, () => Scenes.Replace(new PokerScene(context, s), Navigation.Fade()))), new MenuItem(T("Back"), () => Scenes.Pop())]);
    }

    protected override string Title => "Texas Hold'em";

    protected override IReadOnlyList<PanelLine> Lines => [new(T("No limit. 1,000 chips each; last one with chips wins."), Palette.Muted)];
}

/// <summary>Hides the table until the next person has the device.</summary>
internal sealed class PokerHandoverScene : PanelScene
{
    private readonly string _name;

    public PokerHandoverScene(GameContext context, PokerScene game, string name)
        : base(context)
    {
        _name = name;
        Menu = new Menu(context.Sounds, new MenuItem(T("I'm {0}", name), () =>
        {
            game.ConfirmHandover();
            Scenes.Pop();
        }));
    }

    protected override string Title => T("Pass the device");

    protected override IReadOnlyList<PanelLine> Lines => [new(T("It's {0}'s turn.", _name), Palette.Text), new(T("Others, look away!"), Palette.Muted)];

    protected override void OnBack()
    {
    }
}

/// <summary>The game is over: someone has every chip, or no human has any left.</summary>
internal sealed class PokerEndScene : PanelScene
{
    private readonly PokerScene _game;

    public PokerEndScene(GameContext context, PokerScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("New game"), () =>
            {
                Scenes.Pop();
                game.NewGame();
            }),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => _game.Table.Champion is { } champion
        ? _game.Players[champion].Name == T("You") ? T("You win the table!") : T("{0} wins the table", _game.Players[champion].Name)
        : T("You're out");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(N(_game.Table.HandNumber, "After {0} hand", "After {0} hands"), Palette.Muted),
        .. _game.Players.OrderByDescending(p => _game.Table.Stacks[p.Seat])
            .Select(p => new PanelLine(string.Create(CultureInfo.InvariantCulture, $"{p.Name}  {_game.Table.Stacks[p.Seat]}"), Palette.Text)),
    ];

    protected override void OnBack() => Scenes.Pop();
}

internal sealed class PokerPauseScene : PanelScene
{
    private readonly PokerScene _game;

    public PokerPauseScene(GameContext context, PokerScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("New game"), () =>
            {
                Scenes.Pop();
                game.NewGame();
            }),
            new MenuItem(T("Options"), () => Scenes.Push(new OptionsScene(context))),
            new MenuItem(T("Main menu"), () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => T("Paused");

    protected override IReadOnlyList<PanelLine> Lines => [new(_game.Setup.Name, Palette.Muted)];
}
