using System.Globalization;
using Jentl.Core.Input;
using Poker;
using Microsoft.Xna.Framework;
using Games.UI;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>Who plays poker, from the Card games menu.</summary>
internal sealed class PokerSetupScene : PanelScene
{
    public PokerSetupScene(GameContext context)
        : base(context)
    {
        PokerSetup[] setups = [PokerSetup.VersusBots(3), PokerSetup.VersusBots(5), PokerSetup.VersusBots(1), PokerSetup.PassAndPlay(2), PokerSetup.PassAndPlay(3), PokerSetup.PassAndPlay(4)];
        Menu = new Menu(
            context.Sounds,
            [
                .. setups.Select(s => new MenuItem(s.Name, () => Scenes.Replace(new PokerScene(context, s), Navigation.Fade()))),
                new MenuItem(T("Play online"), () => Scenes.Replace(Online(context))),
                new MenuItem(T("Back"), () => Scenes.Pop()),
            ]);
    }

    protected override string Title => "Texas Hold'em";

    protected override IReadOnlyList<PanelLine> Lines => [new(T("No limit. 1,000 chips each; last one with chips wins."), Palette.Muted)];

    /// <summary>The lobby of Hold'em rooms on the game server: 2 to 6 seats, bots in the empty ones.</summary>
    private static RoomLobbyScene Online(GameContext context) => new(
        context,
        T("Hold'em online"),
        HoldemOnline.Game,
        [2, 3, 4, 5, 6],
        (client, room) => new PokerOnlineScene(context, client, room));
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

/// <summary>A panel over an online table that keeps it talking to the server while it shows.</summary>
internal abstract class PokerOnlinePanel(GameContext context, PokerOnlineScene game) : PanelScene(context)
{
    protected PokerOnlineScene Game { get; } = game;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        Game.KeepAlive(elapsed);
        base.Update(elapsed, input);
    }
}

internal sealed class PokerOnlinePauseScene : PokerOnlinePanel
{
    public PokerOnlinePauseScene(GameContext context, PokerOnlineScene game)
        : base(context, game)
    {
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("Resume"), () => Scenes.Pop()),
            new MenuItem(T("Options"), () => Scenes.Push(new OptionsScene(context))),
            new MenuItem(T("Leave the room"), () => game.Leave()));
    }

    protected override string Title => T("Paused");

    protected override IReadOnlyList<PanelLine> Lines =>
        [new(T("Online room {0}", Game.Code), Palette.Muted), new(T("The game goes on without you while you're here."), Palette.Muted)];
}

/// <summary>The online game is over: someone has every chip, or no person has any.</summary>
internal sealed class PokerOnlineEndScene : PokerOnlinePanel
{
    public PokerOnlineEndScene(GameContext context, PokerOnlineScene game)
        : base(context, game)
    {
        Menu = new Menu(
            context.Sounds,
            new MenuItem(T("New game"), () =>
            {
                Scenes.Pop();
                game.NewGame();
            }),
            new MenuItem(T("Main menu"), () => game.Leave()));
    }

    protected override string Title => Game.State?.Champion is { } champion
        ? champion == Game.Seat ? T("You win the table!") : T("{0} wins the table", Game.Names[champion])
        : T("You're out");

    protected override IReadOnlyList<PanelLine> Lines =>
    [
        new(N(Game.State?.HandNumber ?? 0, "After {0} hand", "After {0} hands"), Palette.Muted),
        .. Enumerable.Range(0, Game.Names.Count).OrderByDescending(Game.Stack)
            .Select(s => new PanelLine(string.Create(CultureInfo.InvariantCulture, $"{Game.Names[s]}  {Game.Stack(s)}"), Palette.Text)),
    ];

    protected override void OnBack() => Scenes.Pop();
}

/// <summary>The connection is gone or the room closed: the table can only be left.</summary>
internal sealed class PokerGoneScene : PokerOnlinePanel
{
    private readonly string _reason;

    public PokerGoneScene(GameContext context, PokerOnlineScene game, string reason)
        : base(context, game)
    {
        _reason = reason;
        Menu = new Menu(context.Sounds, new MenuItem(T("Main menu"), () => game.Leave()));
    }

    protected override string Title => T("Game interrupted");

    protected override IReadOnlyList<PanelLine> Lines => [new(_reason, Palette.Text)];

    protected override void OnBack() => Game.Leave();
}
