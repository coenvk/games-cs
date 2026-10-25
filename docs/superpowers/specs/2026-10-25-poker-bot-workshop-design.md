# Poker bot workshop: design

## Intent

A place to write a poker bot, test it, and see how it does, in the spirit of pokerwars.io (which seems to have
stopped). It grows out of Card Table's Texas Hold'em. Bots are plugged in, play against the house bots or each other,
and the app shows where the bot wins and loses: the hands played out with chips on the table, a fast batch of
thousands of hands for a trustworthy result, replays of single hands, and statistics per bot.

There are two projects, in this order:

1. **Bot workshop (this document):** everything on one desktop, for writing and testing your own bot.
2. **Online competition (a later design):** bots from different people play on the game server and climb a
   ranking. It must be possible to build later without changing bots: the bot contract in this document is the one
   the server will use.

The owner writes bots in C#; bots in any other language must work too.

## Goals

- Write a bot in C# (a class in a DLL) or in any language (a program speaking JSON lines), and plug it in without
  rebuilding the app.
- Watch a table of bots play No-Limit Hold'em with visible chips, at a chosen speed, pausing and stepping.
- Run thousands of hands without drawing them and get a result per bot that luck can't fake: big blinds won per
  100 hands with an error margin, using duplicate deals.
- Replay any hand of a match, and export hand histories in the common text format.
- See statistics per bot (how loose, how aggressive, how it does at showdown).
- Keep playing at the table as a person: against house bots as now, and against your own bot.

## Non-goals

- Online play between bots (project 2), accounts, real money, chat.
- Other variants (Omaha, Limit) and multi-table tournaments, cash games with rebuys.
- Running bots in the browser: the workshop is desktop only. The web build keeps today's Hold'em (with the new
  chips, which are drawn by shared code).
- A bot-writing tutorial beyond the starter kits and the protocol document.

## Anti-goals (would make it a failure even if it works)

- A bot can learn anything it must not: another seat's hole cards, the deck, the seed, or another bot's memory. The
  state a bot gets is built only from what that seat may see.
- A slow, hung or crashing bot freezes or crashes the app. Every outside bot is a separate process, waited for off the
  UI thread under a time limit, and a misbehaving bot is folded, not obeyed.
- A result that is mostly luck is shown as if it were certain. Batch results always carry their error margin, and
  duplicate deals are the default for comparing bots.
- Rules that differ between watching, batches and (later) the server. One engine decides everything.

## What exists

- `src/Poker`: a framework-free No-Limit Hold'em engine: `HoldemTable` (a sit-and-go: stacks, blinds doubling every
  N hands, seats busting out, a champion), `HoldemHand` (one hand: streets, legal actions, side pots, showdown,
  `PokerEvent`s), `HandEvaluator`, and `PokerBot` (Monte Carlo equity against pot odds, with tightness, aggression
  and bluff settings). Seeds make deals repeatable.
- `apps/CardTable`: Hold'em against 1, 3 or 5 bots, or pass-the-device for 2 to 4 people; chips are shown as numbers.
- Jentl: `Jentl.Multiplayer` (`ITurnBasedGame`, `IBot`, `Match` for local tables with human hand-overs and bot delays),
  `Jentl.Net` (rooms over WebSockets, leaderboards), `Jentl.Cards` (card drawing and dealing), `Jentl.Collect`
  (leaderboards).

## What gets built

### The bot contract (decided)

One contract, two ways to plug in, the same later online.

**What a bot is told when a match starts:** the match settings (table size, blinds, starting stack, format), its
seat, the time limit, and a seed of its own for its randomness (different for every bot and every run, derived from
the match seed), so a match of well-behaved bots can be run again exactly. The seed is part of the contract from the
start because the online project can't add it later.

**What a bot is told when it must act** (the "decision"): only what its seat may see.

- The match: hand number, blinds and the level, the seats (name, stack, whether still in the hand, in the sit-and-go,
  or out), the button, the small and big blind seats, and which seat is the bot.
- The hand: its own two hole cards, the board, the street, the pots (main and side pots with their amounts and who
  can win each), each seat's bet this street, the amount to call, and every action in this hand so far in order.
- The legal actions: fold (always), check or call (and the call amount), raise (the smallest and largest "raise to"
  amounts), all in. A raise below the minimum is not legal unless it is all in.
- The time it has to answer.

**What a bot answers:** `fold`, `check`, `call`, `raise` with a "raise to" amount, or `all-in`.

**What a bot is told without having to act** (so it can model its opponents): a hand starting (its seat, the
button, stacks, blinds), and a hand ending (every action, the board, who showed what at showdown, who won which pot),
and the match ending (final standings). It may ignore these.

**Rules the app enforces:**

- A time limit per decision, 1 second by default and adjustable per match; a bot also gets a few seconds to start.
- A late, malformed or illegal answer is replaced: check if checking is free, otherwise fold. Each replacement is
  counted and shown with the bot's results ("timed out 3 times, illegal 1"). A raise amount outside the range is
  illegal, not clamped.
- A bot that crashes or exits is folded every time it is asked for the rest of the match, and the match carries on.

### Every outside bot runs in its own process (decided by the owner)

Nothing a bot does can harm the app: a bot is a separate process, and the app only ever exchanges messages with it.
A hung bot is folded at its time limit and, if it stays silent, stopped; a crashing bot only ends its own process.
Only the house bots run inside the app (they are trusted and fast).

- **Any language:** a program that reads one JSON object per line on standard input and writes one per line on
  standard output. A bot is described by a small file in the bots folder (name, command, arguments, working
  directory), so `python bot.py` or `node bot.js` both work. The first exchange is a hello (protocol version, the
  bot's name); after that the app sends the match start, events and decisions, and the bot answers each decision.
- **C#:** a public class implementing `IPokerBot` from the bot SDK, built into a DLL. The app runs it through the
  **bot host**, a small program (part of the workshop) that loads the DLL and speaks the JSON-lines protocol for it, so
  a C# bot is just another process to the app. A rebuilt DLL is picked up at the next match (each match starts fresh
  hosts). The SDK has the same types as the JSON contract (records for the match start, decision, events and answer),
  so a C# bot and a JSON bot see the same thing.
- **Debugging a C# bot:** the starter kit includes a test harness, a console program that plays the bot against
  house bots in the same process, so breakpoints work in the IDE without the app; attaching a debugger to the bot host
  also works.
- Later, online: a small runner program connects either kind of bot (a command, or a DLL through the bot host) to the
  game server over a WebSocket and relays the same messages. Bots don't change.

### Bot lifetime (decided)

- Every seat gets its own bot process, also when the same bot sits in several seats, so no two seats share memory.
- A bot process lives for one run of a match. Duplicate deals are played as whole runs: a run of the deck sequence,
  then the same sequence again with seats swapped (heads-up) or rotated (ring), each run with fresh bot processes. A
  bot may learn its opponents within a run but never carries anything from one copy of a deck to another.
- Batches play one table at a time by default, so a bot's time limit is not eaten by the batch itself; running tables
  side by side may be offered as an option with a warning.

### The bot SDK (decided)

- Lives in games-cs as a small framework-free library (the contract's types and `IPokerBot`), packed as a NuGet
  package locally the way Jentl's packages are packed for games-cs; the C# template references that package.
- The protocol version in the hello (and the SDK's version for C# bots, checked by the bot host) keeps old bots
  working: within a major version, messages only gain fields, and bots ignore fields they don't know.
- Publishing the SDK for other people belongs to the online project.

### Starter kits and documents (decided)

- A C# bot template project referencing the bot SDK, with a working example bot, building straight into the bots
  folder.
- A Python example speaking the JSON protocol, with no dependencies beyond the standard library.
- A protocol document: every message with an example, the rules above, and how to test.

### House bots (decided)

- Today's `PokerBot` in a few named styles (tight-careful, loose-pushy, balanced).
- Yardsticks: always call, always raise (the minimum, or all in when that is all it can do), and random. "Beats
  always-call over 10,000 duplicate hands" is the first milestone a new bot should reach.

### Running matches (decided)

- **Watch:** a sit-and-go at one table of 2 to 9 seats, any mix of bots and at most one person, drawn with chips.
  Speed: pause, step one action, 1x, 4x, and instant (to the end of the hand). Decisions run in the background; the
  table never waits on a bot longer than its time limit.
- **Batch:** many hands without drawing, as fast as the bots allow, with a progress bar and a cancel. Two formats:
  - *Duplicate heads-up:* two bots play the same deck twice with seats swapped, for a set number of hand pairs.
  - *Duplicate ring:* at a table of N, each deck is dealt N times with the seats rotated so every bot sits in every
    seat.
  In batches stacks reset every hand (a fixed number of big blinds, 100 by default) and blinds stay the same, so
  every hand is worth the same and results are in big blinds won per 100 hands. A batch can also run as a sit-and-go
  series (many sit-and-gos), reporting finishing places.
- Every match has a seed and can be run again exactly, with the same bots.

### Results, replay and statistics (decided)

- **Results:** per bot, big blinds won per 100 hands with a 95% margin, hands played, and the bot's time-outs,
  illegal answers and crashes. For sit-and-go series: average finishing place and how often each place.
- **Hand histories:** every hand of a match is kept (in memory during the match, saved with the match result), can be
  replayed step by step at the table with all hole cards shown, and exported in the PokerStars-style text format that
  poker tools read.
- **Statistics per bot:** VPIP (voluntarily put money in before the flop), PFR (raised before the flop), aggression
  factor (bets and raises against calls), went to showdown, won at showdown.

### The table and its chips (decided)

- Chips drawn as stacks in denomination colours at every seat, the bet in front of each seat, the pot (and each side
  pot separately) in the middle; bets slide into the pot at the end of each street and the pot slides to the winner.
- The dealer button and small and big blind markers.
- Seats for 2 to 9 players; the existing person-against-bots table also gets the chips.
- At showdown the hands are named, as now.

### What goes into Jentl (decided), so other games get it too

- **Jentl.Cards:** chip stacks and pots: breaking an amount into denominations, drawing stacks, sliding animations.
- **Jentl.Multiplayer:** a match runner that asks agents for moves with a time limit off the UI thread, replaces late
  or illegal moves through the game's own rule, and counts what went wrong; seeded, headless batches; duplicate
  seating (the same deal with seats rotated).
- **Jentl.Net (or a new small package):** the agent protocol: JSON lines over standard input and output to a
  separate process, with a hello and version, time limits, stopping a silent process, and crash handling, written so
  the same messages can later travel over a WebSocket. Uno could get outside bots the same way later.
- **Jentl.Collect:** a rating (such as Elo) for the online project to rank bots; it can wait for project 2.

Poker-specific things (the contract's fields, the house bots, hand-history export, poker statistics) stay in
games-cs.

## Constraints

- .NET 10, C#. The poker engine and the bot SDK stay framework-free.
- Desktop only for the workshop (MonoGame). The web build must keep compiling and keep today's Hold'em.
- Everything runs offline; no server needed for project 1.
- Deterministic: the same seed and the same bots give the same deals (bots' own randomness aside).
- Existing Card Table Hold'em keeps working: rules, keyboard controls, translations (English and Dutch).

## Left to the builder

- Exact JSON field names and message shapes, as long as they cover the fields above and are documented with
  examples.
- Folder locations (the bots folder next to the save files is the expected place), the bot description file's
  format, and how the bots list looks.
- Chip denominations and colours, animation timings, and the layout of seats for each table size.
- How results, statistics and replays are laid out on screen, and where they sit in the menus.
- How hand histories are stored on disk.
- Whether the house bots also implement `IPokerBot` (it would prove the contract) while running in the app.
- How the bot host is shipped next to the desktop app (a second program in the same folder is the expected way).
