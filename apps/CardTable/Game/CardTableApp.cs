using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.Scenes;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games;

/// <summary>Uno at one table: against bots, passing the device round, or online.</summary>
internal sealed class CardTableApp : IGameApp
{
    public string Title => "Card Table";

    public string SaveName => "CardTable";

    public void Load(GameContext context, GraphicsDevice device) => CardTableArt.Load(context, device);

    public Scene MainMenu(GameContext context) => new TitleScene(
        context,
        title =>
        [
            new("Uno", () => title.Open(new UnoSetupScene(context), fade: false)),
            new(T("From a ruleset"), () => title.Open(new RulesetsScene(context, solitaire: false, ruleset => new RulesetTableScene(context, ruleset)), fade: false)),
        ],
        () => T("Uno against bots or friends, here or online"));

    // A ruleset from the command line opens over the menu.
    public IReadOnlyList<Scene> Start(GameContext context) =>
        RulesetsScene.FromOptions(context) is { IsSolitaire: false } ruleset
            ? [MainMenu(context), new RulesetTableScene(context, ruleset)]
            : [MainMenu(context)];
}
