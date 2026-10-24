using Games.UI;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Paddock;
using static Games.UI.Tr;

namespace Games.Race;

/// <summary>The words after a round and after the match: who won, by how much, and why.</summary>
internal static class RoundSummary
{
    public static (string Title, IReadOnlyList<PanelLine> Lines) Round(MatchResult result, int round)
    {
        ArgumentNullException.ThrowIfNull(result);
        var r = result.Rounds[round];
        var won = r.Winner == 0;
        var title = won ? T("You win round {0}", round + 1) : T("Round {0} goes to them", round + 1);
        var mine = result.Rounds.Take(round + 1).Count(x => x.Winner == 0);
        var lines = new List<PanelLine> { new(T("By {0:0.00} s", r.Margin), won ? new Color(136, 255, 136) : new Color(255, 136, 136)) };
        if (r.Deciding.Count > 0)
        {
            lines.Add(new PanelLine(T("Decided by: {0}", string.Join(", ", r.Deciding.Select(Words.Of))), Palette.Text));
        }

        lines.Add(new PanelLine(T("Score {0} – {1}", mine, round + 1 - mine), Palette.Muted));
        return (title, lines);
    }

    public static (string Title, IReadOnlyList<PanelLine> Lines) Match(MatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var mine = result.Rounds.Count(r => r.Winner == 0);
        var title = result.PlayerWon ? T("You win the match!") : T("You lose the match");
        return (title, [new PanelLine(T("Rounds {0} – {1}", mine, result.Rounds.Count - mine), Palette.Text)]);
    }
}
