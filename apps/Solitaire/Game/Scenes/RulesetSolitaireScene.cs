using Jentl.Cards;
using Jentl.Cards.Rulesets;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;

namespace Games.Scenes;

/// <summary>
/// Any solitaire written as a ruleset: the card kit deals and plays it, and <see cref="RulesetLayout"/> puts its piles
/// where the ruleset says. Everything else (buttons, hints, undo, automatic finishing, winning) is the screen every
/// card game here has.
/// </summary>
internal sealed class RulesetSolitaireScene : CardGameScene
{
    private readonly RulesetGame _rules;

    public RulesetSolitaireScene(GameContext context, CardRuleset ruleset, int? deal = null)
        : this(context, ruleset, PickDeal(context, deal, 999_999))
    {
    }

    private RulesetSolitaireScene(GameContext context, CardRuleset ruleset, int deal)
        : this(context, new RulesetGame(ruleset, deal), deal)
    {
    }

    private RulesetSolitaireScene(GameContext context, RulesetGame rules, int deal)
        : base(context, rules.Ruleset.Name, deal, rules.Game, "goal") => _rules = rules;

    protected override void LayOut(Rectangle area) => RulesetLayout.Solitaire(View, _rules, area);

    protected override CardGameScene CreateNewDeal() => new RulesetSolitaireScene(Context, _rules.Ruleset);

    // Enter taps the first pile that deals, the stock.
    protected override void OnConfirm()
    {
        if (_rules.Table.Piles.FirstOrDefault(p => _rules.RuleOf(p).Click is not null) is { } stock)
        {
            Play(new ClickPile(stock, stock.Count - 1));
        }
    }
}
