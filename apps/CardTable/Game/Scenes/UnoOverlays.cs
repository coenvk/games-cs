using System.Globalization;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using Uno;
using Jentl.Core.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>Who plays, from the Card games menu.</summary>
internal sealed class UnoSetupScene : PanelScene
{
    public UnoSetupScene(GameContext context)
        : base(context)
    {
        UnoSetup[] setups = [UnoSetup.VersusBots(3), UnoSetup.VersusBots(2), UnoSetup.VersusBots(1), UnoSetup.PassAndPlay(2), UnoSetup.PassAndPlay(3), UnoSetup.PassAndPlay(4)];
        Menu = new Menu(context.Sounds, [.. setups.Select(s => new MenuItem(s.Name, () => Scenes.Replace(new UnoScene(context, s), Navigation.Fade()))), new MenuItem(T("Play online"), () => Scenes.Replace(new RoomLobbyScene(context, T("Uno online"), UnoOnline.Game, [2, 3, 4], (client, room) => new UnoScene(context, new OnlineUnoTable(client, room.Code, room.Seat))))), new MenuItem(T("Back"), () => Scenes.Pop())]);
    }

    protected override string Title => T("Uno");

    protected override IReadOnlyList<PanelLine> Lines => [new(T("Match colour or number; first to 500 points wins."), Palette.Muted)];
}

/// <summary>The colour a wild calls.</summary>
internal sealed class UnoColourScene : UnoPanel
{
    public UnoColourScene(GameContext context, UnoScene game, UnoCard card)
        : base(context, game)
    {
        Menu = new Menu(context.Sounds, [.. Enum.GetValues<UnoColor>().Select(c => new MenuItem(UnoScene.ColorName(c), () =>
        {
            Scenes.Pop();
            game.PlayWild(card, c);
        })), new MenuItem(T("Cancel"), () => Scenes.Pop())]);
    }

    protected override string Title => T("Pick a colour");
}

/// <summary>Hides the table until the next person has the device.</summary>
internal sealed class UnoHandoverScene : PanelScene
{
    private readonly string _name;

    public UnoHandoverScene(GameContext context, UnoScene game, string name)
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

    // There is no backing out of a handover; the menu is the only way on.
    protected override void OnBack()
    {
    }
}

/// <summary>The round's winner and points, and the scores so far.</summary>
internal sealed class UnoRoundScene : UnoPanel
{
    private readonly int _round;

    public UnoRoundScene(GameContext context, UnoScene game)
        : base(context, game)
    {
        var view = game.Table.View!;
        _round = view.RoundNumber;
        var over = view.Champion is not null;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(over ? T("New match") : T("Next round"), () =>
            {
                Scenes.Pop();
                if (over)
                {
                    game.NewMatch();
                }
                else
                {
                    game.NextRound();
                }
            }),
            new MenuItem(T("Main menu"), () => game.Leave()));
    }

    protected override string Title => Game.Table.View is { Champion: { } champion } view
        ? (champion == view.Seat && Game.Table.IsOnline) || Name(champion) == T("You") ? T("You win the match!") : T("{0} wins the match!", Name(champion))
        : T("Round over");

    protected override IReadOnlyList<PanelLine> Lines
    {
        get
        {
            if (Game.Table.View is not { Winner: { } winner } view)
            {
                return [];
            }

            var lines = new List<PanelLine>
            {
                new(T("{0}: +{1} points", Name(winner), view.RoundPoints), Palette.Accent),
            };
            lines.AddRange(Enumerable.Range(0, view.Players).OrderByDescending(s => view.Scores[s])
                .Select(s => new PanelLine(string.Create(CultureInfo.InvariantCulture, $"{Name(s)}  {view.Scores[s]}"), Palette.Text)));
            return lines;
        }
    }

    public override void Update(TimeSpan elapsed, InputState input)
    {
        base.Update(elapsed, input);

        // Online, someone else may deal the next round (or the server does, after a while).
        if (Game.Table.View is { } view && view.RoundNumber != _round && Scenes.Top == this)
        {
            Scenes.Pop();
        }
    }
}

internal sealed class UnoPauseScene : UnoPanel
{
    public UnoPauseScene(GameContext context, UnoScene game)
        : base(context, game)
    {
        Menu = game.Table.IsOnline
            ? new Menu(
                context.Sounds,
                new MenuItem(T("Resume"), () => Scenes.Pop()),
                new MenuItem(T("Options"), () => Scenes.Push(new OptionsScene(context))),
                new MenuItem(T("Leave the room"), () => game.Leave()))
            : new Menu(
                context.Sounds,
                new MenuItem(T("Resume"), () => Scenes.Pop()),
                new MenuItem(T("New match"), () =>
                {
                    Scenes.Pop();
                    game.NewMatch();
                }),
                new MenuItem(T("Options"), () => Scenes.Push(new OptionsScene(context))),
                new MenuItem(T("Main menu"), () => game.Leave()));
    }

    protected override string Title => T("Paused");

    protected override IReadOnlyList<PanelLine> Lines =>
        Game.Table.IsOnline
            ? [new(Game.Table.Name, Palette.Muted), new(T("The game goes on without you while you're here."), Palette.Muted)]
            : [new(Game.Table.Name, Palette.Muted)];
}

/// <summary>An online table that can't go on: the connection dropped or the room closed.</summary>
internal sealed class UnoGoneScene : UnoPanel
{
    private readonly string _reason;

    public UnoGoneScene(GameContext context, UnoScene game, string reason)
        : base(context, game)
    {
        _reason = reason;
        Menu = new Menu(context.Sounds, new MenuItem(T("Main menu"), () => game.Leave()));
    }

    protected override string Title => T("Game interrupted");

    protected override IReadOnlyList<PanelLine> Lines => [new(_reason, Palette.Text)];

    protected override void OnBack() => Game.Leave();
}

/// <summary>A panel over the Uno table that keeps an online game talking to the server while it shows.</summary>
internal abstract class UnoPanel(GameContext context, UnoScene game) : PanelScene(context)
{
    protected UnoScene Game { get; } = game;

    public override void Update(TimeSpan elapsed, InputState input)
    {
        Game.KeepAlive(elapsed);
        base.Update(elapsed, input);
    }

    protected string Name(int seat) => seat < Game.Names.Count ? Game.Names[seat] : T("Player {0}", seat + 1);
}
