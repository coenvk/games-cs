using System.Globalization;
using System.Net;
using System.Text;

namespace GameServer.Poker;

/// <summary>
/// The ranking as a plain web page, in the colours of the Pages site: one table, established bots by place, then the
/// provisional ones marked so. No script; every name is HTML-encoded, since owners pick them.
/// </summary>
public static class RankingPage
{
    private const string Head = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Poker bot ranking</title>
            <style>
                :root { --background: #1c2130; --panel: #29304a; --panel-light: #3a435e; --text: #f2eee4; --muted: #96a0ba; --accent: #ffc72c; }
                body { margin: 0; min-height: 100vh; background: var(--background); color: var(--text); font: 1rem/1.5 system-ui, sans-serif; }
                main { max-width: 64rem; margin: 0 auto; padding: 2.5rem 1rem 3rem; }
                h1 { margin: 0; color: var(--accent); font-size: clamp(2rem, 6vw, 3rem); }
                .intro { margin: 0.5rem 0 2rem; color: var(--muted); }
                .scroll { overflow-x: auto; border-radius: 0.5rem; background: var(--panel); border-top: 4px solid var(--accent); }
                table { width: 100%; border-collapse: collapse; }
                th, td { padding: 0.5rem 0.75rem; text-align: left; white-space: nowrap; }
                th { color: var(--muted); font-weight: 600; }
                td.number, th.number { text-align: right; font-variant-numeric: tabular-nums; }
                tbody tr:nth-child(odd) { background: var(--panel-light); }
                tr.provisional td { color: var(--muted); }
                .tag { font-size: 0.8rem; color: var(--accent); }
                a { color: var(--accent); }
                footer { margin-top: 2.5rem; color: var(--muted); font-size: 0.9rem; }
            </style>
        </head>
        <body>
            <main>
                <h1>Poker bot ranking</h1>
                <p class="intro">Heads-up no-limit Hold'em between bots that run on their owners' computers. Ratings are Glicko-2;
                    a bot is provisional until its range is 200 or less. bb/100 is over all its ranked hands, with its 95% margin.</p>
        """;

    private const string Foot = """
                <footer>The same data as JSON: <a href="/api/poker/ranking">/api/poker/ranking</a>.</footer>
            </main>
        </body>
        </html>
        """;

    /// <summary>The page for <paramref name="ranking"/>, as <see cref="Ratings.Ranking"/> orders it.</summary>
    public static string Html(IReadOnlyList<RankingRow> ranking)
    {
        ArgumentNullException.ThrowIfNull(ranking);
        var html = new StringBuilder(Head);
        if (ranking.Count == 0)
        {
            html.AppendLine("        <p>No bot has played a ranked match yet.</p>");
        }
        else
        {
            html.AppendLine("        <div class=\"scroll\"><table>");
            html.AppendLine("            <thead><tr><th class=\"number\">#</th><th>Bot</th><th>Owner</th><th class=\"number\">Rating</th>"
                + "<th class=\"number\">Matches</th><th class=\"number\">Won / drawn / lost</th><th class=\"number\">bb/100</th><th>Last played</th></tr></thead>");
            html.AppendLine("            <tbody>");
            foreach (var row in ranking)
            {
                html.AppendLine(Row(row));
            }

            html.AppendLine("            </tbody>");
            html.AppendLine("        </table></div>");
        }

        return html.Append(Foot).ToString();
    }

    private static string Row(RankingRow row)
    {
        var bot = WebUtility.HtmlEncode(row.Bot);
        var link = "/api/poker/bots/" + Uri.EscapeDataString(row.Bot);
        var owner = WebUtility.HtmlEncode(string.IsNullOrEmpty(row.Owner) ? "anonymous" : row.Owner);
        var place = row.Place is { } p ? p.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var tag = row.Provisional ? " <span class=\"tag\">provisional</span>" : string.Empty;
        var played = row.LastPlayed is { } at ? at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""                <tr{(row.Provisional ? " class=\"provisional\"" : string.Empty)}><td class="number">{place}</td><td><a href="{WebUtility.HtmlEncode(link)}">{bot}</a>{tag}</td><td>{owner}</td><td class="number">{row.Rating:0} ± {row.Range:0}</td><td class="number">{row.Matches}</td><td class="number">{row.Won} / {row.Drawn} / {row.Lost}</td><td class="number">{row.BbPer100:+0.0;-0.0;0.0} ± {row.Margin:0.0}</td><td>{played}</td></tr>""");
    }
}
