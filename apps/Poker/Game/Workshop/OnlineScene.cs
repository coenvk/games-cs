using System.Text;
using FontStashSharp;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Games.Input;
using Games.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Poker;
using Poker.Arena;
using Poker.Online;
using static Games.UI.Tr;

namespace Games.Workshop;

/// <summary>
/// The poker league on the game server, from the workshop. Two tabs (Tab switches): the Ranking (place, bot, owner,
/// rating with its range and a mark while provisional, matches, won/drawn/lost, bb/100 with its margin) and My bots:
/// each bot with whether its runner is connected, its line of the ranking and its recent matches, and for the chosen
/// one Go online (the poker runner for it, a child process that keeps running until stopped or the app quits), Show
/// token, New token, Practice against a house bot and Open a match (downloaded, into the results screen). Register a
/// bot takes a name from the bots folder or a typed one. The first time, it asks for an owner name and makes the owner;
/// the tokens are kept on this computer (see <see cref="OnlineAccounts"/>). When the server cannot be reached the view
/// says so; the rest of the workshop does not need it. Requests run off the UI thread, one at a time.
/// </summary>
internal sealed class OnlineScene : Scene
{
    private const int MaxName = 32;
    private static readonly Color Problem = new(236, 120, 108);
    private static readonly Color Good = new(120, 200, 140);
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(5);

    private readonly WorkshopContext _workshop;
    private readonly GameContext _context;
    private readonly OnlineSession _session;
    private readonly StringBuilder _typed = new();

    private Menu _menu;
    private Tab _tab = Tab.Ranking;
    private Mode _mode = Mode.Main;
    private Typing _typing;
    private IReadOnlyList<RankingRowJson> _ranking = [];
    private MineJson? _mine;
    private string? _bot;
    private string? _shownToken;
    private string? _offline;
    private (string Text, bool IsProblem)? _note;
    private Task<Action>? _pending;
    private string? _waiting;
    private TimeSpan _sinceRefresh;
    private int _rankingFirst;

    public OnlineScene(WorkshopContext workshop)
    {
        ArgumentNullException.ThrowIfNull(workshop);
        _workshop = workshop;
        _context = workshop.Game;
        _session = workshop.OnlineSession;
        _menu = new Menu(_context.Sounds, new MenuItem(T("Back"), () => Scenes.Pop()));
        Refresh();
    }

    private enum Tab
    {
        Ranking,
        Mine,
    }

    private enum Mode
    {
        Main,
        Bot,
        PickFile,
        PickHouseBot,
        PickMatch,
        Typing,
        Token,
        Offline,
    }

    private enum Typing
    {
        Owner,
        Bot,
    }

    public override bool IsOverlay => true;

    private OnlineAccount Account => _session.Account;

    private MyBotJson? Selected => _mine?.Bots.FirstOrDefault(b => string.Equals(b.Name, _bot, StringComparison.OrdinalIgnoreCase));

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (_pending is { IsCompleted: true } done)
        {
            _pending = null;
            _waiting = null;
            try
            {
                done.GetAwaiter().GetResult()();
            }
            catch (OnlineProblem e)
            {
                Failed(e);
            }
        }

        if (_mode == Mode.Typing)
        {
            Type(input);
            return;
        }

        if (_context.Actions.WasPressed(input, GameAction.Back))
        {
            Leave();
            return;
        }

        if (_mode is Mode.Main && input.WasPressed(Keys.Tab))
        {
            Show(_tab == Tab.Ranking ? Tab.Mine : Tab.Ranking);
            return;
        }

        if (_mode is Mode.Main && _tab == Tab.Ranking)
        {
            if (input.WasPressed(Keys.PageDown))
            {
                _rankingFirst = Math.Min(_rankingFirst + 10, Math.Max(0, _ranking.Count - 1));
            }
            else if (input.WasPressed(Keys.PageUp))
            {
                _rankingFirst = Math.Max(0, _rankingFirst - 10);
            }
        }

        // The ranking and the bots' state keep themselves up to date while they are shown.
        _sinceRefresh += elapsed;
        if (_mode is Mode.Main or Mode.Bot && _sinceRefresh >= RefreshEvery && _pending is null && Account.OwnerToken is not null)
        {
            Refresh(quietly: true);
        }

        _menu.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var fonts = _context.Fonts;
        batch.FillRectangle(viewport, Palette.Dim);
        var margin = fonts.Px(24);
        var panel = new Rectangle(viewport.X + margin, viewport.Y + margin, viewport.Width - 2 * margin, viewport.Height - 2 * margin);
        batch.FillRectangle(panel, Palette.Panel);
        batch.FillRectangle(new Rectangle(panel.X, panel.Y, panel.Width, 6), Palette.Accent);
        var padding = fonts.Px(28);
        var y = panel.Y + fonts.Px(20);
        batch.DrawText(fonts.Heading, T("Online"), new Vector2(panel.Center.X, y), Palette.Text, Align.Center);
        y += (int)fonts.Heading.LineHeight;
        var small = fonts.Small;
        batch.DrawText(small, T("Poker league at {0}", _session.Api.Authority), new Vector2(panel.Center.X, y), Palette.Muted, Align.Center, shadow: false);
        y += (int)small.LineHeight + fonts.Px(12);

        var bottom = panel.Bottom - padding / 2;
        var status = _waiting ?? _note?.Text;
        if (status is not null)
        {
            var lines = small.Wrap(status, panel.Width - 2 * padding);
            bottom -= lines.Count * ((int)small.LineHeight + 2);
            var at = bottom;
            foreach (var line in lines)
            {
                batch.DrawText(small, line, new Vector2(panel.Center.X, at), _waiting is null && _note?.IsProblem == true ? Problem : Palette.Accent, Align.Center, shadow: false);
                at += (int)small.LineHeight + 2;
            }

            bottom -= fonts.Px(8);
        }

        if (_mode is Mode.Main or Mode.Bot)
        {
            DrawTabs(batch, new Vector2(panel.X + padding, y));
            y += (int)fonts.Body.LineHeight + fonts.Px(10);
        }

        var menuWidth = panel.Width * 3 / 10;
        var content = new Rectangle(panel.X + padding + menuWidth, y, panel.Width - 2 * padding - menuWidth, bottom - y);
        _menu.Draw(batch, small, panel.X + padding + menuWidth / 2, y, bottom - y);
        switch (_mode)
        {
            case Mode.Offline:
                DrawText(batch, content, [(T("The server cannot be reached: {0}", _offline ?? ""), Problem), (T("The rest of the workshop works without it. Try again when the server is up."), Palette.Muted)]);
                break;
            case Mode.Typing:
                DrawTyping(batch, content);
                break;
            case Mode.Token:
                DrawToken(batch, content);
                break;
            case Mode.Main when _tab == Tab.Ranking:
                DrawRanking(batch, content);
                break;
            default:
                DrawBot(batch, content);
                break;
        }
    }

    /// <summary>"Ranking" and "My bots", the shown one marked.</summary>
    private void DrawTabs(SpriteBatch batch, Vector2 at)
    {
        var font = _context.Fonts.Body;
        foreach (var (tab, label) in new[] { (Tab.Ranking, T("Ranking")), (Tab.Mine, T("My bots")) })
        {
            var shown = tab == _tab;
            batch.DrawText(font, label, at, shown ? Palette.Accent : Palette.Muted, shadow: false);
            var width = font.MeasureString(label).X;
            if (shown)
            {
                batch.FillRectangle(new Rectangle((int)at.X, (int)(at.Y + font.LineHeight), (int)width, 3), Palette.Accent);
            }

            at.X += width + _context.Fonts.Px(32);
        }

        batch.DrawText(_context.Fonts.Small, T("Tab switches"), at with { Y = at.Y + _context.Fonts.Px(8) }, Palette.Muted, shadow: false);
    }

    // ---- What the player does ----

    private void Leave()
    {
        switch (_mode)
        {
            case Mode.Main or Mode.Offline:
                Scenes.Pop();
                break;
            case Mode.Bot:
                Main();
                break;
            default:
                _shownToken = null;
                if (_bot is null)
                {
                    Main();
                }
                else
                {
                    BotMenu();
                }

                break;
        }
    }

    private void Show(Tab tab)
    {
        _tab = tab;
        Main();
    }

    /// <summary>The tab's menu: on My bots, a row per bot first.</summary>
    private void Main()
    {
        _mode = Mode.Main;
        _bot = null;
        _shownToken = null;
        if (Account.OwnerToken is null && _offline is null)
        {
            StartTyping(Typing.Owner);
            return;
        }

        List<MenuItem> items = [];
        if (_tab == Tab.Mine)
        {
            items.AddRange((_mine?.Bots ?? []).Select(b => new MenuItem(BotLabel(b), () => Choose(b.Name))));
            items.Add(new MenuItem(T("Register a bot"), PickFile));
            items.Add(new MenuItem(T("Ranking"), () => Show(Tab.Ranking)));
        }
        else
        {
            items.Add(new MenuItem(T("My bots"), () => Show(Tab.Mine)));
        }

        items.Add(new MenuItem(T("Refresh"), () => Refresh()));
        items.Add(new MenuItem(T("Back"), () => Scenes.Pop()));
        _menu = new Menu(_context.Sounds, [.. items]);
    }

    private static string BotLabel(MyBotJson bot) => bot.Connected ? $"{bot.Name}  ·  {T("online")}" : bot.Name;

    private void Choose(string bot)
    {
        _bot = bot;
        BotMenu();
    }

    /// <summary>What can be done with the chosen bot.</summary>
    private void BotMenu()
    {
        _mode = Mode.Bot;
        _shownToken = null;
        var bot = _bot!;
        var running = _session.Runner(bot) is { IsRunning: true };
        _menu = new Menu(
            _context.Sounds,
            running ? new MenuItem(T("Stop the runner"), () => StopRunner(bot)) : new MenuItem(T("Go online"), () => GoOnline(bot)),
            new MenuItem(T("Show token"), () => ShowToken(bot)),
            new MenuItem(T("New token"), () => NewToken(bot)),
            new MenuItem(T("Practice against a house bot"), PickHouseBot),
            new MenuItem(T("Open a match"), PickMatch),
            new MenuItem(T("Back"), Main));
    }

    /// <summary>
    /// The bots in the bots folder that can run: to register one (or a name typed instead), or to choose the one
    /// <see cref="_bot"/> goes online as when none has its name.
    /// </summary>
    private void PickFile()
    {
        _mode = Mode.PickFile;
        var registering = _bot is null;
        var bots = _workshop.Bots().Where(b => b.Kind != BotKind.House && b.Command is not null).ToList();
        List<MenuItem> items = [.. bots.Select(b => new MenuItem(b.Name, () =>
        {
            if (registering)
            {
                Register(b.Name, b.Source);
            }
            else
            {
                Account.BotFiles[_bot!] = b.Source;
                Keep();
                GoOnline(_bot!);
            }
        }))];
        if (registering)
        {
            items.Add(new MenuItem(T("Type a name"), () => StartTyping(Typing.Bot)));
        }

        items.Add(new MenuItem(T("Back"), Leave));
        _menu = new Menu(_context.Sounds, [.. items]);
        if (bots.Count == 0)
        {
            Note(registering ? T("No bots in the bots folder: type a name, or put a bot there first.") : T("No bots in the bots folder to run."), problem: !registering);
        }
    }

    private void PickHouseBot()
    {
        _mode = Mode.PickHouseBot;
        var bot = _bot!;
        _menu = new Menu(
            _context.Sounds,
            [
                .. HouseBots.All.Select(h => new MenuItem(h.Name, () => Practise(bot, h.Name))),
                new MenuItem(T("Back"), Leave),
            ]);
    }

    private void PickMatch()
    {
        var recent = Selected?.Recent ?? [];
        if (recent.Count == 0)
        {
            Note(T("{0} has played no match yet.", _bot!), problem: true);
            return;
        }

        _mode = Mode.PickMatch;
        var bot = _bot!;
        _menu = new Menu(
            _context.Sounds,
            [
                .. recent.Select(m => new MenuItem(MatchLine(m, bot), () => OpenMatch(m.Id))),
                new MenuItem(T("Back"), Leave),
            ]);
    }

    private void StartTyping(Typing typing)
    {
        _mode = Mode.Typing;
        _typing = typing;
        _typed.Clear();
        if (typing == Typing.Owner)
        {
            _typed.Append(_context.PlayerName);
        }

        _menu = new Menu(_context.Sounds, new MenuItem(T("Back"), Leave));
    }

    /// <summary>Letters, digits, spaces and a few signs; Backspace takes one back, Enter is done, Escape cancels.</summary>
    private void Type(InputState input)
    {
        if (input.WasPressed(Keys.Escape))
        {
            if (_typing == Typing.Owner)
            {
                Scenes.Pop();
            }
            else
            {
                Leave();
            }

            return;
        }

        if (input.WasPressed(Keys.Enter))
        {
            var name = _typed.ToString().Trim();
            if (name.Length > 0 && _pending is null)
            {
                if (_typing == Typing.Owner)
                {
                    NewOwner(name);
                }
                else
                {
                    Register(name, null);
                }
            }

            return;
        }

        if (input.WasPressed(Keys.Back) && _typed.Length > 0)
        {
            _typed.Length--;
            return;
        }

        var shift = input.IsDown(Keys.LeftShift) || input.IsDown(Keys.RightShift);
        for (var key = Keys.A; key <= Keys.Z; key++)
        {
            if (input.WasPressed(key))
            {
                Append((char)((shift ? 'A' : 'a') + (key - Keys.A)));
            }
        }

        for (var key = Keys.D0; key <= Keys.D9; key++)
        {
            if (input.WasPressed(key) || input.WasPressed(Keys.NumPad0 + (key - Keys.D0)))
            {
                Append((char)('0' + (key - Keys.D0)));
            }
        }

        if (input.WasPressed(Keys.Space))
        {
            Append(' ');
        }
        else if (input.WasPressed(Keys.OemMinus))
        {
            Append(shift ? '_' : '-');
        }
        else if (input.WasPressed(Keys.OemPeriod))
        {
            Append('.');
        }
    }

    private void Append(char c)
    {
        if (_typed.Length < MaxName)
        {
            _typed.Append(c);
        }
    }

    // ---- Requests ----

    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread, saying <paramref name="waiting"/> meanwhile; what it returns is
    /// done on the UI thread when it is over. Nothing starts while another request is under way.
    /// </summary>
    private void Request(string? waiting, Func<Task<Action>> work)
    {
        if (_pending is not null)
        {
            return;
        }

        _waiting = waiting;
        _pending = Task.Run(work);
    }

    private void Failed(OnlineProblem problem)
    {
        if (problem.IsUnreachable)
        {
            _offline = problem.Message;
            _mode = Mode.Offline;
            _note = null;
            _menu = new Menu(_context.Sounds, new MenuItem(T("Try again"), () => Refresh()), new MenuItem(T("Back"), () => Scenes.Pop()));
            return;
        }

        if (problem.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            // The server's data was started afresh, say: the token kept here names no owner there.
            Account.OwnerToken = null;
            StartTyping(Typing.Owner);
            Note(T("The server does not know this computer's owner token. Make a new owner to go on."), problem: true);
            return;
        }

        Note(T("The server said: {0}", problem.Message), problem: true);
    }

    private void Note(string text, bool problem = false) => _note = (text, problem);

    /// <summary>Keeps the tokens on disk, or says it could not.</summary>
    private void Keep()
    {
        if (!_session.Save())
        {
            Note(T("Could not keep the tokens in {0}.", _workshop.Shown(_workshop.OnlineAccountsFile)), problem: true);
        }
    }

    /// <summary>Reads the ranking and the player's bots again; quietly: without saying so, and keeping the note.</summary>
    private void Refresh(bool quietly = false)
    {
        _sinceRefresh = TimeSpan.Zero;
        var owner = Account.OwnerToken;
        var client = _session.Client;
        Request(quietly ? null : T("Asking the server"), async () =>
        {
            var ranking = await client.RankingAsync().ConfigureAwait(false);
            var mine = owner is null ? null : await client.MineAsync(owner).ConfigureAwait(false);
            return () =>
            {
                _ranking = ranking;
                _mine = mine;
                _rankingFirst = Math.Min(_rankingFirst, Math.Max(0, ranking.Count - 1));
                var wasOffline = _mode == Mode.Offline;
                _offline = null;
                if (owner is null && _mode != Mode.Typing)
                {
                    StartTyping(Typing.Owner);
                }
                else if (wasOffline || _mode == Mode.Main)
                {
                    var selected = _menu.Selected;
                    Main();
                    _menu.Selected = Math.Min(selected, _menu.Items.Count - 1);
                }
                else if (_mode == Mode.Bot && Selected is null)
                {
                    Main();
                }
                else if (_mode == Mode.Bot)
                {
                    var selected = _menu.Selected;
                    BotMenu();
                    _menu.Selected = selected;
                }
            };
        });
    }

    private void NewOwner(string name)
    {
        var client = _session.Client;
        Request(T("Making the owner {0}", name), async () =>
        {
            var token = await client.NewOwnerAsync(name).ConfigureAwait(false);
            return () =>
            {
                Account.OwnerToken = token;
                Account.BotTokens.Clear();
                Account.BotFiles.Clear();
                Keep();
                Note(T("Welcome, {0}. Register a bot to put it online.", name));
                _tab = Tab.Mine;
                Main();
                Refresh(quietly: true);
            };
        });
    }

    private void Register(string name, string? file)
    {
        var client = _session.Client;
        var owner = Account.OwnerToken!;
        Request(T("Registering {0}", name), async () =>
        {
            var (registered, token) = await client.NewBotAsync(owner, name).ConfigureAwait(false);
            return () =>
            {
                Account.BotTokens[registered] = token;
                if (file is not null)
                {
                    Account.BotFiles[registered] = file;
                }

                Keep();
                Note(T("{0} is registered; its token is kept on this computer. Choose it, then Go online.", registered));
                _tab = Tab.Mine;
                Main();
                Refresh(quietly: true);
            };
        });
    }

    /// <summary>Starts the runner for <paramref name="bot"/> with the bot file it was registered with or one of its name, or asks which file.</summary>
    private void GoOnline(string bot)
    {
        if (!Account.BotTokens.TryGetValue(bot, out var token))
        {
            Note(T("This computer has no token for {0}: choose New token.", bot), problem: true);
            BotMenu();
            return;
        }

        var file = Account.BotFiles.GetValueOrDefault(bot) is { } kept && File.Exists(kept) ? kept
            : _workshop.Bots().FirstOrDefault(b => b.Kind != BotKind.House && b.Command is not null && string.Equals(b.Name, bot, StringComparison.OrdinalIgnoreCase))?.Source;
        if (file is null)
        {
            Note(T("Which bot in the bots folder plays as {0}?", bot));
            PickFile();
            return;
        }

        var runner = _workshop.Runner;
        try
        {
            _session.Start(bot, runner with { Arguments = [.. runner.Arguments, file] }, token);
            Note(T("{0} is going online from {1}.", bot, Path.GetFileName(file)));
        }
        catch (ObjectDisposedException)
        {
            // The app is quitting.
        }

        BotMenu();
    }

    private void StopRunner(string bot)
    {
        _session.Stop(bot);
        Note(T("The runner of {0} is stopped.", bot));
        BotMenu();
    }

    private void ShowToken(string bot)
    {
        if (!Account.BotTokens.TryGetValue(bot, out var token))
        {
            Note(T("This computer has no token for {0}: choose New token.", bot), problem: true);
            return;
        }

        _shownToken = token;
        _mode = Mode.Token;
        _menu = new Menu(_context.Sounds, new MenuItem(T("Done"), Leave));
    }

    private void NewToken(string bot)
    {
        var client = _session.Client;
        var owner = Account.OwnerToken!;
        Request(T("Asking for a new token"), async () =>
        {
            var token = await client.NewBotTokenAsync(owner, bot).ConfigureAwait(false);
            return () =>
            {
                Account.BotTokens[bot] = token;
                Keep();
                var restarted = _session.Runner(bot) is { IsRunning: true };
                if (restarted)
                {
                    GoOnline(bot);
                }

                Note(restarted ? T("{0} has a new token; its runner here was started again with it.", bot) : T("{0} has a new token; the old one no longer works.", bot));
                ShowToken(bot);
            };
        });
    }

    private void Practise(string bot, string houseBot)
    {
        var client = _session.Client;
        var owner = Account.OwnerToken!;
        Request(T("Asking for a practice match"), async () =>
        {
            await client.PractiseAsync(owner, bot, houseBot).ConfigureAwait(false);
            return () =>
            {
                Note(T("{0} plays {1} now; the match shows under its recent matches when it is over.", bot, houseBot));
                BotMenu();
            };
        });
    }

    /// <summary>Downloads the match, keeps it in the workshop's online folder and shows its results.</summary>
    private void OpenMatch(string id)
    {
        var client = _session.Client;
        var owner = Account.OwnerToken!;
        var folder = _workshop.OnlineMatchesFolder;
        Request(T("Downloading the match"), async () =>
        {
            var match = await client.MatchAsync(owner, id).ConfigureAwait(false);
            var path = Path.Combine(folder, string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");
            try
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(path, MatchFile.Write(match)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                path = null;
            }

            return () => Scenes.Push(_workshop.MatchResults(match, path));
        });
    }

    // ---- Drawing ----

    private void DrawText(SpriteBatch batch, Rectangle area, IEnumerable<(string Text, Color Color)> lines)
    {
        var font = _context.Fonts.Small;
        var y = area.Y;
        foreach (var (text, color) in lines)
        {
            foreach (var part in font.Wrap(text, area.Width))
            {
                if (y + font.LineHeight > area.Bottom)
                {
                    return;
                }

                batch.DrawText(font, part, new Vector2(area.X, y), color, shadow: false);
                y += (int)font.LineHeight + 2;
            }

            y += _context.Fonts.Px(6);
        }
    }

    private void DrawTyping(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        var question = _typing == Typing.Owner ? T("Your name as an owner in the league") : T("The bot's name in the league");
        var y = area.Y;
        foreach (var line in fonts.Small.Wrap(question, area.Width))
        {
            batch.DrawText(fonts.Small, line, new Vector2(area.X, y), Palette.Muted, shadow: false);
            y += (int)fonts.Small.LineHeight + 2;
        }

        y += fonts.Px(8);
        var field = new Rectangle(area.X, y, Math.Min(area.Width, fonts.Px(640)), (int)fonts.Body.LineHeight + fonts.Px(16));
        batch.FillRectangle(field, Palette.Background);
        batch.DrawText(fonts.Body, _typed + "_", new Vector2(field.X + fonts.Px(12), field.Y + fonts.Px(8)), Palette.Text, shadow: false);
        y = field.Bottom + fonts.Px(12);
        var hint = _typing == Typing.Owner
            ? T("Type a name and press Enter. Your bots are listed under it; a token kept on this computer makes you the owner.")
            : T("Type a name and press Enter.");
        DrawText(batch, new Rectangle(area.X, y, area.Width, area.Bottom - y), [(hint, Palette.Muted)]);
    }

    private void DrawToken(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        var y = area.Y;
        batch.DrawText(fonts.Body, T("Token of {0}", _bot ?? ""), new Vector2(area.X, y), Palette.Text, shadow: false);
        y += (int)fonts.Body.LineHeight + fonts.Px(8);
        var token = _shownToken ?? "";
        var font = fonts.Body;
        while (font.MeasureString(token).X > area.Width - fonts.Px(24) && font.LineHeight > 12)
        {
            font = fonts.Sized(font.LineHeight * 0.9f);
        }

        var box = new Rectangle(area.X, y, area.Width, (int)font.LineHeight + fonts.Px(16));
        batch.FillRectangle(box, Palette.Background);
        batch.DrawText(font, token, new Vector2(box.X + fonts.Px(12), box.Y + fonts.Px(8)), Palette.Accent, shadow: false);
        y = box.Bottom + fonts.Px(12);
        DrawText(batch, new Rectangle(area.X, y, area.Width, area.Bottom - y),
        [
            (T("With it, the poker runner puts this bot online from another computer: PokerRunner <bot> --token <token> --server {0}", _session.RunnerServer), Palette.Muted),
            (T("It is shown only now. Whoever has it can play as your bot; New token makes it useless."), Palette.Muted),
        ]);
    }

    private void DrawRanking(SpriteBatch batch, Rectangle area)
    {
        var fonts = _context.Fonts;
        if (_ranking.Count == 0)
        {
            DrawText(batch, area, [(_pending is null ? T("No bot has played a ranked match yet.") : T("Asking the server"), Palette.Muted)]);
            return;
        }

        var mine = (_mine?.Bots ?? []).Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] header = ["#", T("Bot"), T("Owner"), T("Rating"), T("Matches"), T("W/D/L"), "bb/100"];
        var rows = _ranking.Select(r => new[]
        {
            r.Place?.ToString(Tr.Culture) ?? "–",
            r.Bot,
            r.Owner,
            $"{r.Rating.ToString("0", Tr.Culture)} ± {r.Range.ToString("0", Tr.Culture)}{(r.Provisional ? " *" : "")}",
            r.Matches.ToString(Tr.Culture),
            $"{r.Won}/{r.Drawn}/{r.Lost}",
            $"{r.BbPer100.ToString("+0.0;-0.0;0.0", Tr.Culture)} ± {r.Margin.ToString("0.0", Tr.Culture)}",
        }).ToList();

        var font = fonts.Small;
        var footnote = T("* provisional: the rating is not settled yet (listed after the others)");
        var line = (int)font.LineHeight + fonts.Px(6);
        var visible = Math.Max(1, (area.Height - 2 * line) / line - 1);
        var first = Math.Clamp(_rankingFirst, 0, Math.Max(0, rows.Count - visible));
        var gap = fonts.Px(14);
        var widths = Enumerable.Range(0, header.Length)
            .Select(c => (int)MathF.Ceiling(rows.Select(r => r[c]).Append(header[c]).Max(t => font.MeasureString(t).X)))
            .ToArray();
        widths[1] = Math.Min(widths[1], fonts.Px(220));
        widths[2] = Math.Min(widths[2], fonts.Px(160));
        var y = area.Y;
        Row(header, Palette.Accent);
        y += line;
        batch.FillRectangle(new Rectangle(area.X, y - fonts.Px(4), Math.Min(area.Width, widths.Sum() + gap * (widths.Length - 1)), 1), Palette.PanelLight);
        for (var r = first; r < rows.Count && r < first + visible; r++)
        {
            Row(rows[r], mine.Contains(_ranking[r].Bot) ? Palette.Accent : Palette.Text);
            y += line;
        }

        var more = rows.Count > first + visible ? "  ·  " + T("{0} more (Page Down)", rows.Count - first - visible) : "";
        batch.DrawText(font, footnote + more, new Vector2(area.X, y + fonts.Px(6)), Palette.Muted, shadow: false);

        // The bot and owner columns cut short; the numbers right-aligned.
        void Row(string[] cells, Color color)
        {
            var x = area.X;
            for (var c = 0; c < cells.Length; c++)
            {
                var text = c is 1 or 2 ? Cut(font, cells[c], widths[c]) : cells[c];
                var at = c is 1 or 2 ? x : x + widths[c] - font.MeasureString(text).X;
                batch.DrawText(font, text, new Vector2(at, y), color, shadow: false);
                x += widths[c] + gap;
            }
        }
    }

    /// <summary>The chosen bot (or, on the list, the one the menu is on): its state, its line of the ranking and its recent matches.</summary>
    private void DrawBot(SpriteBatch batch, Rectangle area)
    {
        var bots = _mine?.Bots ?? [];
        var bot = Selected ?? (_mode == Mode.Main && _menu.Selected < bots.Count ? bots[_menu.Selected] : null);
        if (bot is null)
        {
            DrawText(batch, area,
            [
                (N(bots.Count, "{0} bot registered", "{0} bots registered"), Palette.Text),
                (T("Register a bot from the bots folder, then choose it and Go online: the runner plays its matches while this app is open."), Palette.Muted),
            ]);
            return;
        }

        List<(string, Color)> lines = [(bot.Name, Palette.Text)];
        lines.Add(bot.Connected ? (T("Connected to the server"), Good) : (T("Not connected"), Palette.Muted));
        if (_session.Runner(bot.Name) is { } runner)
        {
            lines.Add(runner.IsRunning
                ? (T("Runner on this computer: {0}", runner.Status ?? T("starting")), Palette.Text)
                : (T("The runner here stopped: {0}", runner.Status ?? runner.LastError ?? runner.Problem ?? ""), Problem));
        }

        if (bot.Row is { } row)
        {
            var place = row.Place is { } p ? T("place {0}", p) : T("provisional");
            lines.Add((T("Rating {0} ± {1}, {2}", row.Rating.ToString("0", Tr.Culture), row.Range.ToString("0", Tr.Culture), place), Palette.Text));
            lines.Add((N(row.Matches, "{0} ranked match", "{0} ranked matches") + "  ·  " + T("won {0}, drawn {1}, lost {2}", row.Won, row.Drawn, row.Lost), Palette.Text));
            lines.Add((T("{0} bb/100 ± {1}", row.BbPer100.ToString("+0.0;-0.0;0.0", Tr.Culture), row.Margin.ToString("0.0", Tr.Culture)), Palette.Text));
        }
        else
        {
            lines.Add((T("No ranked match yet"), Palette.Muted));
        }

        lines.Add((T("Recent matches"), Palette.Accent));
        lines.AddRange(bot.Recent.Count == 0 ? [(T("None yet"), Palette.Muted)] : bot.Recent.Select(m => (MatchLine(m, bot.Name), Palette.Muted)));
        DrawText(batch, area, lines);
    }

    /// <summary>"26-10 14:03 · vs Beta Bot · +120 chips · ranked" from the side of <paramref name="bot"/>.</summary>
    private static string MatchLine(StoredMatchJson match, string bot)
    {
        var first = string.Equals(match.BotA, bot, StringComparison.OrdinalIgnoreCase);
        var net = first ? match.NetA : match.NetB;
        return string.Join(
            "  ·  ",
            match.At.ToLocalTime().ToString("g", Tr.Culture),
            T("vs {0}", first ? match.BotB : match.BotA),
            T("{0} chips", net.ToString("+0;-0;0", Tr.Culture)),
            match.Ranked ? T("ranked") : T("practice"));
    }

    private static string Cut(SpriteFontBase font, string text, int width)
    {
        if (font.MeasureString(text).X <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && font.MeasureString(cut + "…").X > width)
        {
            cut = cut[..^1];
        }

        return cut + "…";
    }
}
