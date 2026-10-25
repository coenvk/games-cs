# Online bot ranking: design

## Intent

The second half of the poker bot workshop (the first half is
`docs/superpowers/specs/2026-10-25-poker-bot-workshop-design.md`). People write bots at home with the workshop; now
those bots meet bots written by other people, on the game server, and climb a public ranking, in the spirit of
pokerwars.io. A bot that beats the house bots locally should be able to go online with one command, and its owner should
be able to see how it ranks, against whom it wins and loses, and replay those hands in the workshop.

The bot contract does not change: a bot written for the workshop plays online as it is.

## Goals

- Put a bot online with one command, from the owner's own computer, for a bot in any language or a C# DLL.
- A ranking that luck can't fake for long: heads-up matches of many hands on deals no bot has seen before, and a
  rating with an uncertainty, shown with its range.
- Matches the owner can study: results per match (bb/100 with its margin), download, replay in the workshop, hand
  history export.
- The ranking visible in Poker's bot workshop and on a web page anyone can open.
- A server that one person can run and keep running: one process, files in one data folder, settings by option.

## Who it is for (decided by the owner)

To start with, colleagues and friends: people who are trusted not to cheat on purpose. Registration is free, the
protections are simple (below), and the operator can ban. It must stay possible to open it to anyone later without
redoing it: registration goes through one place where invite codes or operator approval can be added, every owner and
bot record has room for more about it (who invited them, when, from where), and the match history keeps what a later
collusion check would need.

## Non-goals

- Running anyone's bot on the server (bots run on their owners' computers).
- Accounts with passwords or e-mail, teams, chat, prizes, real money.
- Tournaments, ring-table ranked play, seasons that reset the ranking (later, maybe).
- For now: protection against a determined cheater with several owner accounts (invite codes, operator approval,
  chip-dumping detection). The design leaves room for them (see Who it is for).
- People playing in ranked matches (the ranking is for bots).

## Anti-goals (would make it a failure even if it works)

- A bot learns what it must not: another seat's hole cards before showdown, the deck, a seed, another bot's
  messages. The server deals with secret 128-bit seeds and sends each seat only what it may see, exactly as the
  workshop does.
- Someone moves a bot up the ranking by any other means than playing well: playing their own bots against each other,
  entering under someone else's name, replaying a match until it goes their way, resubmitting results.
- A slow or misbehaving bot (or one that floods the server with messages) stalls other matches or the server.
- Duplicate deals online. In a duplicate match the second run repeats the deals with the seats swapped, so a bot that
  remembers the first run (in a file; the server cannot force a fresh process on the owner's computer) knows its
  opponent's hole cards from the flop on. Online deals are never repeated, in a match or across matches (decided by
  the owner). Duplicate stays the workshop's local tool.
- A rating shown as more certain than it is: every rating carries its range, and new bots are marked provisional.
- Rules that differ from the workshop: the same engine (`src/Poker`) decides every hand, with the same time-limit,
  fallback and "stopped after 3 missed answers" rules.

## What exists

- `src/GameServer`: an ASP.NET Core server with WebSocket rooms (`/play`, Jentl.Net's `RoomServer`) for Uno and
  leaderboards (`ScoreServer`, `scores.json`), shipped as a Docker image.
- The workshop (Poker app, desktop): bot catalogue, watch, duplicate batches, results with margins, statistics,
  replays, match files, hand history export; the bot host for C# bots.
- `src/Poker`: the engine, `ArenaTable` and `ArenaBatch` (duplicate heads-up and ring, seeded with `DealSeed`),
  `IArenaSeat` with `HouseSeat` and `ProcessSeat`, `MatchFile`, `PokerStats`, `HandHistoryText`.
- `src/Poker.Bots`: the bot SDK; the JSON-lines protocol (version 1) in `docs/poker-bots.md`.
- Jentl 0.6.2: `AgentProcess` (bots as processes, bounded output), `Decisions` (timed decisions with fallbacks),
  `Seatings`, `RunningStats`, `KeyedRandom`, `Leaderboard`, `RoomServer`, `NetClient`.

## What gets built

### Where bots run (decided)

Bots run on their owners' computers. A small **runner** program, part of the workshop, starts the bot (a command from a
`.bot.json`, or a C# DLL through the bot host, the same way the workshop does) and connects it to the server over a
WebSocket. The runner relays the protocol's messages unchanged in both directions: the server sends what the
workshop's table would send; the bot answers as it answers locally. The server never runs code it was sent, so it needs
no sandbox, and an owner's bot stays private.

The runner:

- `dotnet run --project tools/PokerRunner -- <bot> --server wss://example.org/bots --token <token>` (exact command
  names left to the builder); `<bot>` is a `.bot.json` or a DLL, as in the bots folder.
- Can also be started from the workshop's Online view for a bot in the bots folder.
- Plays one match at a time per bot connection, and starts a fresh bot process for every match.
- Reconnects after a dropped connection and takes its seat back in the match it was in (see Ranked play).
- Shows what it is doing: connected, waiting for an opponent, playing match N against X, the result, the rating.

### Bot accounts (decided)

- An owner registers a **bot** with a name (unique on the server, the same name rules as leaderboards' player names
  or a little looser) and gets a **secret token**. The token is all the runner needs; it is shown once and can be
  replaced (which invalidates the old one).
- An **owner** is identified by an owner token, made on first registration and kept by the workshop. All bots
  registered with the same owner token belong to that owner. No passwords, no e-mail.
- Limits: a few bots per owner (5 by default), a few registrations per address per day. Tokens are stored hashed.
- Registration is one code path (one service), so invite codes or operator approval can be added later without
  touching the rest.
- The server operator can ban a bot or an owner (a command-line tool or a file the server reads); a banned bot's
  matches stay in the history and its rating is removed from the ranking.

### Ranked play (decided)

- A connected bot is in the **ranked queue**. The server pairs two queued bots and plays a **ranked match**:
  heads-up, N hands (1,000 by default) on fresh deals from the server's secret 128-bit seed, never repeated, fixed
  stacks of 100 big blinds reset every hand, blinds 1/2, the button alternating, every hand worth the same. Apart from
  the deals not being repeated this is the workshop's duplicate heads-up batch; the runner starts a fresh bot process for each
  match.
- Luck is evened out by many hands per match and many matches per bot, and the rating's range says how far it is
  still unsure; the workshop's duplicate batch stays the way to measure a bot precisely at home.
- **Pairing:** bots close in rating, never a bot with itself or with a bot of the same owner, and not the same pair
  again within a cooldown (a number of matches or a time), so a bot meets many opponents. A bot waiting longer gets
  a wider rating window.
- **Time:** the server counts the time limit (1 s per decision by default) plus a fixed network allowance (0.5 s by
  default, a server setting; my call); a bot gets a few seconds to say hello at the start of each match. Late, malformed and illegal answers are
  replaced and counted as in the workshop; three late answers in a row and the seat is folded for the rest of the match (a
  dropped connection: see below).
- **Result:** the match's result is bb/100 for each bot with its 95% margin; the winner is the bot with more chips
  over the match (a draw when equal). The rating uses win, draw or loss (my call: simple and standard for Glicko-2;
  the uncertainty and the number of matches absorb close results).
- **A dropped connection (my call):** the seat is folded (the fallback) while the runner is away; a runner that
  reconnects within the match takes its seat back from the next decision. The match counts as played, so dropping on
  purpose never helps.
- **Names (my call):** bots see each other's real bot names in `seats[].name`, as they are public on the ranking;
  modelling opponents is part of poker.
- **Concurrency:** many matches run at once on the server (a setting); a match runs on its own task and never holds
  up the others.
- **Practice:** a bot can ask for unranked practice matches against the house bots (choose one), same format, results
  for the owner only. Good for testing the connection before going ranked.

### Rating (decided)

- **Glicko-2**: a rating, a rating deviation (how unsure the server is) and a volatility per bot; new bots start at
  1500 ± 350. Updated after every ranked match (each match its own rating period, or matches grouped per period: left
  to the builder, as long as it is documented).
- A bot is **provisional** until its deviation drops below a threshold (or for its first N matches) and is shown so,
  below the established bots.
- The ranking shows: place, bot name, owner (a short owner name the owner picks, or "anonymous"), rating ± twice the
  deviation, matches played, won / drawn / lost, bb/100 over all its ranked hands with margin, last played.
- The rating code goes into Jentl.Collect (the workshop design already named it there), framework-free and tested
  against Glickman's worked example.

### Seeing results (decided)

- **In Poker's bot workshop**, a new **Online** view (desktop): the ranking; the owner's bots with their tokens,
  ratings and recent matches; register a bot; start the runner for a bot of the bots folder; open a match.
- **Opening a match:** the workshop downloads a finished match the owner's bot played (as the workshop's match file)
  and shows it in the existing Results and Replay screens; hand histories export as for a local match.
- **What an owner sees of a match:** every action, the board, their own bot's hole cards, and the cards shown at
  showdown; the opponent's folded hands stay hidden. (Owner's call to change; this is the poker room convention and
  keeps bots from being studied hand by hand.)
- **On the web:** the server serves a ranking page (plain HTML, also linked from the Pages site) and the same data as
  JSON for anyone to read: the ranking, a bot's page with its recent matches (results only).

### Server (decided)

- **Deployment:** to start with, run by the owner for colleagues and friends, reachable over the internet behind a
  TLS proxy (wss://); the server reads the client's address from the proxy's forwarded header when told to trust it.
- The existing game server gets a second endpoint for bots (e.g. `/bots`), apart from `/play`, and HTTP endpoints for
  registration, the ranking, a bot's matches and match downloads (owner token needed for a download).
- **Data** lives in one folder (a setting, like `scores.json` today): bots and owners, ratings, and one file per
  finished match (compressed). Written safely (a crash never leaves a half-written file); a restart picks up where it
  left off. Matches in progress when the server stops are dropped, not rated.
- **Settings** (options or environment variables, documented): data folder, match size, time limit and network
  allowance, concurrent matches, limits, rating parameters.
- **Messages:** a maximum message size, a limit on messages per second per connection, and the bounded queues the
  workshop already uses; a connection that breaks the protocol is closed.
- Ships in the existing Docker image (which then also needs `src/Poker` and `src/Poker.Bots`).

### What goes into Jentl (decided), so other games get it too

- **Jentl.Collect:** the Glicko-2 rating.
- **Jentl.Net:** relaying an agent's JSON lines over a WebSocket (the runner side: an `AgentProcess` talking to a
  socket; the server side: a remote agent that looks like a local one to the code asking it, with the same
  `AskAsync`-with-a-limit shape), and limits on message size and rate per connection. Uno could get online bots the
  same way later.

Poker-specific things (the match format, pairing, the ranking's columns, the Online view, the runner's command line)
stay in games-cs.

## Constraints

- .NET 10, C#. Engine pieces in Jentl (version bumped per change); poker pieces in games-cs.
- The bot protocol stays version 1; anything new for bots is optional and additive (a bot that ignores it still
  plays). The runner speaks to the server in its own small protocol around the bot's messages (hello with token,
  run starts and ends, match result), which bots never see.
- The workshop keeps working offline; online is an extra view.
- One engine decides every hand, locally and online.
- Desktop only for the runner and the Online view; the web gets the ranking page only.
- English and Dutch texts in the app, as everywhere.

## Left to the builder

- Exact endpoint paths, the runner's protocol message shapes, file formats in the data folder.
- Pairing details (rating window growth, cooldown length), the provisional threshold, Glicko-2's system constant and
  rating period.
- How the Online view is laid out, and the ranking page's look (plain, matching the Pages site).
- Name rules for bots and owners, and the exact limits.
- How the operator bans (a small CLI or an edited file).
- How a remote bot plugs into the engine (my call: as one more `IArenaSeat`; `ArenaBatch`/`ArenaTable` may be
  changed so a match can be run hand by hand with seats that come and go).
- Testing: the server's matches can be tested in-process with fake socket connections and house bots or the test
  agents; one end-to-end test with a real runner and a real bot process.
