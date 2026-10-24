using System.Globalization;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban.Desktop.Input;
using Sokoban.Desktop.UI;
using Uno;
using Jentl.Core.UI;

namespace Sokoban.Desktop.Scenes;

/// <summary>Who plays, from the Card games menu.</summary>
internal sealed class UnoSetupScene : PanelScene
{
    public UnoSetupScene(GameContext context)
        : base(context)
    {
        UnoSetup[] setups = [UnoSetup.VersusBots(3), UnoSetup.VersusBots(2), UnoSetup.VersusBots(1), UnoSetup.PassAndPlay(2), UnoSetup.PassAndPlay(3), UnoSetup.PassAndPlay(4)];
        Menu = new Menu(context.Sounds, [.. setups.Select(s => new MenuItem(s.Name, () => Scenes.Replace(new UnoScene(context, s), Navigation.Fade()))), new MenuItem("Back", () => Scenes.Pop())]);
    }

    protected override string Title => "Uno";

    protected override IReadOnlyList<PanelLine> Lines => [new("Match colour or number; first to 500 points wins.", Palette.Muted)];
}

/// <summary>The colour a wild calls.</summary>
internal sealed class UnoColourScene : PanelScene
{
    public UnoColourScene(GameContext context, UnoScene game, UnoCard card)
        : base(context)
    {
        Menu = new Menu(context.Sounds, [.. Enum.GetValues<UnoColor>().Select(c => new MenuItem(c.ToString(), () =>
        {
            Scenes.Pop();
            game.PlayWild(card, c);
        })), new MenuItem("Cancel", () => Scenes.Pop())]);
    }

    protected override string Title => "Pick a colour";
}

/// <summary>Hides the table until the next person has the device.</summary>
internal sealed class UnoHandoverScene : PanelScene
{
    private readonly string _name;

    public UnoHandoverScene(GameContext context, UnoScene game, string name)
        : base(context)
    {
        _name = name;
        Menu = new Menu(context.Sounds, new MenuItem($"I'm {name}", () =>
        {
            game.ConfirmHandover();
            Scenes.Pop();
        }));
    }

    protected override string Title => "Pass the device";

    protected override IReadOnlyList<PanelLine> Lines => [new($"It's {_name}'s turn.", Palette.Text), new("Others, look away!", Palette.Muted)];

    // There is no backing out of a handover; the menu is the only way on.
    protected override void OnBack()
    {
    }
}

/// <summary>The round's winner and points, and the scores so far.</summary>
internal sealed class UnoRoundScene : PanelScene
{
    private readonly UnoScene _game;

    public UnoRoundScene(GameContext context, UnoScene game)
        : base(context)
    {
        _game = game;
        var over = game.Match.Champion is not null;
        Menu = new Menu(
            context.Sounds,
            new MenuItem(over ? "New match" : "Next round", () =>
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
            new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => _game.Match.Champion is { } champion
        ? $"{_game.Players[champion].Name} {(_game.Players[champion].Name == "You" ? "win" : "wins")} the match!"
        : "Round over";

    protected override IReadOnlyList<PanelLine> Lines
    {
        get
        {
            var match = _game.Match;
            var winner = match.Round.Winner!.Value;
            var lines = new List<PanelLine>
            {
                new(string.Create(CultureInfo.InvariantCulture, $"{_game.Players[winner].Name}: +{match.ScoreRound()} points"), Palette.Accent),
            };
            lines.AddRange(_game.Players.OrderByDescending(p => match.Scores[p.Seat])
                .Select(p => new PanelLine(string.Create(CultureInfo.InvariantCulture, $"{p.Name}  {match.Scores[p.Seat]}"), Palette.Text)));
            return lines;
        }
    }
}

internal sealed class UnoPauseScene : PanelScene
{
    private readonly UnoScene _game;

    public UnoPauseScene(GameContext context, UnoScene game)
        : base(context)
    {
        _game = game;
        Menu = new Menu(
            context.Sounds,
            new MenuItem("Resume", () => Scenes.Pop()),
            new MenuItem("New match", () =>
            {
                Scenes.Pop();
                game.NewMatch();
            }),
            new MenuItem("Options", () => Scenes.Push(new OptionsScene(context))),
            new MenuItem("Main menu", () => Navigation.ToMainMenu(Scenes, context)));
    }

    protected override string Title => "Paused";

    protected override IReadOnlyList<PanelLine> Lines => [new(_game.Setup.Name, Palette.Muted)];
}
