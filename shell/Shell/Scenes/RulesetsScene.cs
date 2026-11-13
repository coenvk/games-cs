using Jentl.Cards.Rulesets;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// Card games made from rulesets: Jentl's examples of one kind (solitaires, or games for players). A ruleset file of your
/// own opens with <c>--ruleset</c>.
/// </summary>
internal sealed class RulesetsScene : PanelScene
{
    /// <param name="open">Makes the scene that plays a chosen ruleset.</param>
    public RulesetsScene(GameContext context, bool solitaire, Func<CardRuleset, Scene> open)
        : base(context)
    {
        var rulesets = RulesetExamples.Names.Select(RulesetExamples.Load).Where(r => r.IsSolitaire == solitaire).ToList();
        Menu = new Menu(
            context.Sounds,
            [
                .. rulesets.Select(ruleset => new MenuItem(ruleset.Name, () => Scenes.Push(open(ruleset), Navigation.Fade()))),
                new MenuItem(T("Back"), () => Scenes.Pop()),
            ]);
    }

    // Ruleset names stay as they are in every language, like the other games'.
    protected override string Title => T("From a ruleset");

    protected override IReadOnlyList<PanelLine> Lines => [new PanelLine(T("Each game here is a ruleset file read by the card kit"), Palette.Muted)];

    /// <summary>
    /// The ruleset given with <c>--ruleset</c>, or null; a mistake in it is written to the console (with where it is)
    /// and the game starts at its menu instead.
    /// </summary>
    public static CardRuleset? FromOptions(GameContext context)
    {
        if (context.Options.Ruleset is not { } json)
        {
            return null;
        }

        try
        {
            return CardRuleset.Parse(json);
        }
        catch (InvalidDataException mistake)
        {
            Console.Error.WriteLine($"The ruleset can't be read: {mistake.Message}");
            return null;
        }
    }
}
