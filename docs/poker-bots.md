# Writing a poker bot

The bot workshop (Card Table > Bot workshop, desktop only) plays your bot at a Texas Hold'em table: you watch it play
against the house bots or other bots of yours, or run thousands of hands without drawing them to find out whether it is
any good. A bot is either

- a **program in any language** that reads JSON lines on stdin and writes JSON lines on stdout (the starter kit in
  `bots/python` is one), or
- a **C# class** implementing `IPokerBot` from the `Poker.Bots` package, built into a DLL (the starter kit in
  `bots/csharp`).

Both play by the same messages. A C# bot is run by a small host program that speaks the protocol for it, so to the app
it is just another program.

Contents: [Quick start](#quick-start), [The protocol](#the-protocol), [Rules](#rules), [The bots folder](#the-bots-folder),
[Building a C# bot](#building-a-c-bot), [Testing and debugging](#testing-and-debugging),
[First milestone](#first-milestone-beat-always-call).

## Quick start

**Python** (3.8 or newer, nothing to install):

1. Find the bots folder (see [The bots folder](#the-bots-folder)); the workshop's **Open bots folder** opens it.
2. Copy `bots/python/example_bot.py` and `bots/python/example.bot.json` into it.
3. In the workshop choose **Refresh**: "Python example" is listed under your bots. Seat it at **Watch a match**.

**C#** (.NET 10 SDK):

```
build/pokersdk.sh                 # once: packs the SDK into .poker-packages
dotnet build bots/csharp/MyBot    # builds MyBot.dll straight into the bots folder
dotnet run --project bots/csharp/MyBot.Harness   # plays it against the house bots, in this process
```

Then **Refresh** in the workshop: "MyBot" is listed. Edit `bots/csharp/MyBot/MyBot.cs` and build again; the next match
uses the new DLL.

## The protocol

Every message is **one line of JSON** (a single object, no line breaks inside it, UTF-8, `\n` at the end) with a
`"type"`. The app writes to your bot's stdin and reads your answers from its stdout. Version 1 is described here.

Cards are two characters: a rank from `23456789TJQKA` and a suit from `shdc` (spades, hearts, diamonds, clubs), so `As`
is the ace of spades and `Td` the ten of diamonds. Seats are numbered from 0. Chips are whole numbers. The examples
below are real lines, recorded from a three-handed sit-and-go.

### The conversation

```
app -> bot   hello
bot -> app   hello            (within 5 seconds)
app -> bot   match-start
             then, for every hand:
app -> bot   hand-start
app -> bot   decision         (only when it is your turn; answer each with an action)
bot -> app   action
app -> bot   hand-end
app -> bot   match-end        (the bot may exit after this)
```

Everything you need to decide is in each `decision`, so a bot does not have to remember anything. Remember things
anyway (what the others did in earlier hands) if you want to model your opponents.

### hello (both ways)

The app speaks first, then your bot answers with the same message and its name. The table does not use that name: it
shows the bot's name from the bots folder (the `"name"` in its `.bot.json`, or else the file name; for a C# bot the DLL's
file name), numbered when the same bot sits twice. The name in your hello is for the bot's own identification and logs.

```json
{"type":"hello","protocol":1}
{"type":"hello","protocol":1,"name":"Always call"}
```

If you answer with another protocol version the bot stays at the table but is folded every time it is asked, for the rest of the run, and shows the problem
("speaks protocol 2, the app speaks 1"); it still pays the blinds. The same goes for a bot that has not answered
within 5 seconds of the app starting it.

### match-start (app to bot)

```json
{"type":"match-start","seat":0,"seats":3,"format":"sit-and-go","startingStack":1000,"smallBlind":10,"bigBlind":20,"timeLimitMs":1000,"seed":-4838477332953278127}
```

| Field | |
|---|---|
| `seat` | your seat number |
| `seats` | how many seats the table has (2 to 9) |
| `format` | `"sit-and-go"` (play until one player has all the chips, blinds going up) or `"fixed-stacks"` (every hand starts from the starting stack, so each hand counts on its own; this is what duplicate batches use) |
| `startingStack`, `smallBlind`, `bigBlind` | the chips everyone starts with, and the blinds at the start of the match |
| `timeLimitMs` | how long you get for each decision |
| `seed` | **your own** seed, for your own randomness; it differs per seat and per run. It is never the seed the cards are dealt from: a bot never learns the deck. It is a 64-bit integer, possibly negative. JavaScript bots: numbers above 2^53 lose precision when parsed with `JSON.parse`; read it as a `BigInt` (or take the loss, the seed is only for randomness). |

### hand-start (app to bot)

```json
{"type":"hand-start","hand":1,"button":0,"smallBlind":10,"bigBlind":20,"seats":[{"seat":0,"name":"Alice","stack":1000,"state":"active"},{"seat":1,"name":"Bob","stack":1000,"state":"active"},{"seat":2,"name":"Cleo","stack":1000,"state":"active"}]}
```

| Field | |
|---|---|
| `hand` | the hand number, counting from 1 |
| `button` | the seat holding the dealer button |
| `smallBlind`, `bigBlind` | the blinds for this hand |
| `seats` | every seat: `seat`, `name`, `stack` (before the blinds are posted) and `state` (`"active"`, or `"out"` for a player who has busted) |

### decision (app to bot)

It is your turn. Answer with an `action`.

```json
{"type":"decision","id":6,"hand":1,"street":"flop","you":2,"hole":["Jd","7h"],"board":["Tc","Th","Ks"],"button":0,"smallBlindSeat":1,"bigBlindSeat":2,"smallBlind":10,"bigBlind":20,"level":0,"seats":[{"seat":0,"name":"Alice","stack":1000,"bet":0,"state":"folded"},{"seat":1,"name":"Bob","stack":960,"bet":0,"state":"active"},{"seat":2,"name":"Cleo","stack":960,"bet":0,"state":"active"}],"pots":[{"amount":80,"seats":[1,2]}],"toCall":0,"legal":{"fold":true,"check":true,"minRaiseTo":20,"maxRaiseTo":960,"allIn":true},"actions":[{"street":"preflop","seat":0,"action":"fold","amount":0,"total":0},{"street":"preflop","seat":1,"action":"call","amount":10,"total":20},{"street":"preflop","seat":2,"action":"raise","amount":20,"total":40},{"street":"preflop","seat":1,"action":"call","amount":20,"total":40},{"street":"flop","seat":1,"action":"check","amount":0,"total":0}],"timeLimitMs":1000}
```

| Field | |
|---|---|
| `id` | counts up through the match; echo it in your answer |
| `hand`, `street` | the hand number; `"preflop"`, `"flop"`, `"turn"` or `"river"` |
| `you` | your seat |
| `hole`, `board` | your two hole cards; the community cards so far (none, three, four or five) |
| `button`, `smallBlindSeat`, `bigBlindSeat` | where the button and the blinds are |
| `smallBlind`, `bigBlind`, `level` | the blinds now, and the blind level (it goes up as a sit-and-go goes on; it starts at 0) |
| `seats` | every seat: `seat`, `name`, `stack` (behind), `bet` (chips put in on this street), `state` (`"active"`, `"folded"`, `"all-in"` or `"out"`) |
| `pots` | the main pot and any side pots: `amount` and the `seats` that can win it; chips from earlier streets included |
| `toCall` | the chips you must add to stay in; 0 when you can check |
| `legal` | what you may do (below) |
| `actions` | everything that happened this hand so far, oldest first: `street`, `seat`, `action` (`"fold"`, `"check"`, `"call"`, `"raise"` or `"all-in"`), `amount` (the chips it added), `total` (that seat's bet on the street afterwards). The blinds are not in the list: see `smallBlindSeat`, `bigBlindSeat` and each seat's `bet`. |
| `timeLimitMs` | how long you have to answer |

`legal` has `fold` (always true), `check` (true when nothing is to be called), `call` (the chips a call costs, at most
your stack; left out when there is nothing to call), `minRaiseTo` and `maxRaiseTo` (the smallest and largest raise, left
out when you cannot raise) and `allIn` (true when you may push everything in).

### action (bot to app)

```json
{"type":"action","action":"fold","id":6}
{"type":"action","action":"check","id":6}
{"type":"action","action":"call","id":6}
{"type":"action","action":"raise","to":60,"id":6}
{"type":"action","action":"all-in","id":6}
```

`raise` is **raise to**: `to` is your total bet on this street, not the extra chips, and must lie between
`legal.minRaiseTo` and `legal.maxRaiseTo` (raising to the maximum is going all in). `id` is the decision's `id`; it is
optional, but send it: an answer carrying an `id` other than the current decision's is ignored, so a late answer to an
earlier decision can never be taken for the one being asked now.

### hand-end (app to bot)

```json
{"type":"hand-end","hand":1,"board":["Tc","Th","Ks","4s","Kd"],"actions":[{"street":"preflop","seat":0,"action":"fold","amount":0,"total":0},{"street":"preflop","seat":1,"action":"call","amount":10,"total":20},{"street":"preflop","seat":2,"action":"raise","amount":20,"total":40},{"street":"preflop","seat":1,"action":"call","amount":20,"total":40},{"street":"flop","seat":1,"action":"check","amount":0,"total":0},{"street":"flop","seat":2,"action":"raise","amount":20,"total":20},{"street":"flop","seat":1,"action":"call","amount":20,"total":20},{"street":"turn","seat":1,"action":"check","amount":0,"total":0},{"street":"turn","seat":2,"action":"raise","amount":20,"total":20},{"street":"turn","seat":1,"action":"call","amount":20,"total":20},{"street":"river","seat":1,"action":"check","amount":0,"total":0},{"street":"river","seat":2,"action":"raise","amount":20,"total":20},{"street":"river","seat":1,"action":"call","amount":20,"total":20}],"shown":[{"seat":1,"cards":["Qd","3d"],"rank":"two pair"},{"seat":2,"cards":["Jd","7h"],"rank":"two pair"}],"won":[{"seat":1,"amount":200}],"pots":[{"amount":200,"seats":[1,2],"winners":[{"seat":1,"amount":200}]}],"stacks":[1000,1100,900]}
```

| Field | |
|---|---|
| `hand`, `board` | the hand number and the community cards that were dealt |
| `actions` | everything that happened, oldest first, as in `decision` |
| `shown` | the hands revealed at showdown (`seat`, `cards`, `rank`); empty when everyone else folded |
| `won` | what each winning seat collected (`seat`, `amount`) |
| `pots` | each pot: `amount`, the `seats` that contended for it and its `winners` (`seat`, `amount`; several on a split) |
| `stacks` | every seat's stack after the hand, indexed by seat number |

### match-end (app to bot)

```json
{"type":"match-end","standings":[{"seat":0,"name":"Alice","stack":3000,"place":1},{"seat":1,"name":"Bob","stack":0,"place":2},{"seat":2,"name":"Cleo","stack":0,"place":3}]}
```

`standings` has every seat with its `name`, its `place` (1 is the winner) and its `stack`. In a **sit-and-go** `stack` is
the chips the seat finished with. In a **fixed-stacks** match every hand starts from the same stack, so the standing
`stack` is the seat's **net**: the chips it won or lost over the match (negative for a loss):

```json
{"type":"match-end","standings":[{"seat":0,"name":"Alice","stack":-2030,"place":2},{"seat":1,"name":"Bob","stack":2030,"place":1}]}
```

## Rules

- **Transport.** One JSON object per line on stdin and stdout, UTF-8. Flush stdout after every line you write.
- **Hello.** The first exchange is the hello. A bot that has not answered within 5 seconds, or answers with another
  protocol version, stays at the table but is folded every time (it still pays the blinds) and shows its problem.
- **Time limit.** You get `timeLimitMs` for each decision (1 second by default; the match setup can change it). It is
  given in `match-start` and in every `decision`. Other messages need no answer.
- **Late, malformed or illegal answers** are replaced: the app checks when checking is free and folds otherwise. Each
  replacement is counted and shown with the bot's results ("late 3, illegal 1"). Illegal means an action that is not in
  `legal` right now, an action name the app does not know, or a raise outside `[minRaiseTo, maxRaiseTo]` (a raise to
  `maxRaiseTo` is all in, and fine). A malformed line (not a valid message) is skipped like any other stray line, so
  when no valid answer follows, the decision counts as late, not illegal.
- **Crashes.** A bot that crashes or exits is folded every time it is asked for the rest of the run, and the match goes
  on. The seat shows what went wrong, with the last line the bot printed on stderr.
- **Lines that are not protocol messages are ignored**, in both directions. A stray debug print on stdout does not break
  a bot, but prefer stderr: it is captured, and its last line is shown with the bot's problem.
- **Ids.** An action may echo the decision's `id`; an action whose `id` does not match is ignored.
- **Processes.** Every seat is its own process, started fresh for every run: a bot that sits in two seats is two
  processes that share nothing, and nothing carries over from one run to the next. In a duplicate batch a run plays the
  same deals as the one before with the seats swapped, and your bot starts from scratch in each.
- **Seeds.** `match-start` carries your bot's own seed. Use it for all of your randomness: a match of bots that do is
  played out the same way when it is run again with the same seed. A bot never learns the deck, another seat's hole
  cards (before a showdown) or another bot's seed.
- **Versions.** Within major version 1 messages only gain fields. **Ignore fields and message types you do not know**
  (the Python starter kit does; `BotRunner` does for C#), and your bot keeps working when the app is updated.

## The bots folder

The workshop looks for bots in the `bots` folder of the Card Table save folder, and shows it (and a button that opens
it) in the workshop:

| System | Bots folder |
|---|---|
| macOS | `~/Library/Application Support/CardTable/bots` |
| Linux | `~/.local/share/CardTable/bots` (under `$XDG_DATA_HOME` if you have set it) |
| Windows | `%LOCALAPPDATA%\CardTable\bots` |

The list is read when the workshop opens, when you choose **Refresh** and when you come back to it. What it finds:

- **A program bot** is a `*.bot.json`:

  ```json
  {
    "name": "Python example",
    "command": "python3",
    "args": ["example_bot.py"],
    "workingDirectory": "."
  }
  ```

  `command` is started with `args` in `workingDirectory`, which is relative to the `.bot.json` and by default the
  folder it is in. A `command` with a path separator in it (`./run.sh`, `bin/mybot`) is resolved against the working
  directory; a bare name (`python3`, `node`) is looked up on the `PATH`. Without a `name`, the file name stands in for
  it. A description that cannot be read is listed with what is wrong. On Windows Python is usually `python` (or the
  launcher: `"command": "py", "args": ["-3", "example_bot.py"]`), not `python3`. A program that cannot be started shows
  "could not start" at its seat.
- **A C# bot** is any `*.dll` in the folder except `Poker.Bots.dll`, listed under its file name. The bot host loads the
  DLL and runs the **one** public class in it that implements `IPokerBot` and has a public constructor without
  parameters. A DLL with none, or with more than one, is a problem shown at its seat ("has 2 bots: ...; name one"). The
  app has no way to name one of several yet: build one bot per DLL. (A `.bot.json` cannot run a DLL either; use the
  DLL itself.) A bot's dependencies are loaded from the DLL's own folder, but every `*.dll` in the bots folder is listed
  as a bot, so a dependency DLL placed there shows up as an entry that "has no bot". Avoid that by building a
  bot that needs no extra DLLs, or accept the extra entries. `Poker.Bots` itself is
  the host's, so a bot built against a different major version of the SDK is refused.

## Building a C# bot

`bots/csharp/MyBot` is a class library to start from: it references the `Poker.Bots` 1.0.0 package, has a small example
bot with comments, and installs its DLL into the bots folder after every build.

```
build/pokersdk.sh                   # packs src/Poker.Bots into .poker-packages (see nuget.config); once, and after SDK changes
dotnet build bots/csharp/MyBot      # builds, and copies MyBot.dll (and its .pdb) into the bots folder
dotnet build bots/csharp/MyBot -p:PokerBotsFolder=/some/other/folder    # install somewhere else
```

The folder defaults to the Card Table save folder's `bots` (the table above), computed for your system in the project
file. Building the harness builds MyBot too, so it installs MyBot into the bots folder as well. `Poker.Bots.dll` is not copied: the bot host brings its own. If you repack the SDK under the same version, NuGet
keeps its cached copy; clear it with `dotnet nuget locals global-packages --clear`.

To make your own bot elsewhere: a class library (`net10.0`) with `<PackageReference Include="Poker.Bots" Version="1.0.0" />`
(add `.poker-packages` as a package source), one public class implementing `IPokerBot` (only `Name` and `Decide` are
required; `OnMatchStart`, `OnHandStart`, `OnHandEnd` and `OnMatchEnd` are optional hooks), and copy the DLL into the
bots folder. A rebuilt DLL is picked up by the next match: the host reads it into memory, so rebuilding while the
workshop is open is fine.

## Testing and debugging

**C# in a debugger: the harness.** `bots/csharp/MyBot.Harness` plays MyBot against each house bot, heads-up, in a
duplicate batch (every deal twice, seats swapped), **in the same process**, so a breakpoint in `MyBot.cs` stops the match
and you can step through your code in your IDE; no app, no bot host.

```
dotnet run --project bots/csharp/MyBot.Harness           # 2,000 deals per opponent
dotnet run --project bots/csharp/MyBot.Harness -- 500    # a shorter run
```

It prints a table, one row per house bot:

```
Opponent         bb/100 ± margin   hands
Always call        +169.2 ± 11.4    4000
Always raise     +1828.8 ± 123.7    4000
Random             +220.9 ± 95.7    4000
Tight              -159.3 ± 51.5    4000
Balanced           -155.6 ± 61.6    4000
Loose              -181.0 ± 70.1    4000
```

`bb/100` is the big blinds your bot won per 100 hands, with the 95% margin of error; `hands` counts the hands your bot was
dealt into (two runs per deal, so twice the deals). A result is only worth believing when the margin is smaller than the
number.

**Any language: run the bot by hand.** A bot is a program that reads lines: start it in a terminal and paste messages.
Lines from this document work as they are:

```
$ python3 bots/python/example_bot.py
{"type":"hello","protocol":1}
{"type":"hello","protocol":1,"name":"Python example"}
{"type":"decision","id":1,...}                        (paste a decision)
{"type":"action","action":"call","id":1}
```

Feed it a whole file with `python3 bots/python/example_bot.py < messages.txt` (save the lines you pasted).

**In the app.** **Watch a match** shows what every bot did, step by step (P pauses, N steps), with a red note when a bot
was late, or its answer illegal. When a bot cannot play, its seat shows why, with the last line it printed on stderr:
print your own diagnostics there (`print(..., file=sys.stderr)`, `Console.Error.WriteLine`). **Run a batch** plays many
hands fast. **Past matches** lists the saved ones, where any hand can be replayed step by step.

## First milestone: beat Always call

A bot that checks and calls whatever it holds is the weakest sensible opponent; losing to it means something is wrong.
Beat it over **10,000 hands in a duplicate heads-up batch**, with a result that is positive by more than its margin.
The 10,000 is the number the form asks for: each of those deals is played twice with the seats swapped, so a bot is
dealt into 20,000 hands.

- Any language: in the workshop choose **Run a batch**, format **Duplicate heads-up**, your bot against **Always call**,
  hands **10000**, and read the result.
- C#: `dotnet run --project bots/csharp/MyBot.Harness -- 10000`, and read the "Always call" row.

Then try the others: Random, Always raise, and the three bots that weigh their chances, Tight, Balanced and Loose.
