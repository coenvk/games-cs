# Poker Bot Workshop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A desktop workshop in Card Table for writing, plugging in and testing No-Limit Hold'em bots (C# or any
language), with chips on the table, watch and batch modes, duplicate deals, replays, hand-history export and statistics.

**Architecture:** Three layers. (1) Engine (`../jentl`, Jentl 0.6.0): chips (`Jentl.Cards` model, `Jentl.Core`
drawing), JSON-lines agent processes (`Jentl.Net`), timed decisions with fallbacks and duplicate seatings
(`Jentl.Multiplayer`), running statistics (`Jentl.Collect`). (2) Rules, framework-free: the bot SDK `src/Poker.Bots`
(contract types, JSON, `IPokerBot`, a stdin/stdout runner; no Jentl dependency), and in `src/Poker` the contract
views, house bots, arena (seats, one table, batches), hand records, export and statistics; plus `tools/PokerBotHost`,
which runs a C# bot DLL as a process. (3) App: chips on the shared Hold'em table, and desktop-only workshop scenes in
`apps/CardTable`.

**Tech Stack:** C# 14 / .NET 10, MonoGame DesktopGL, KNI (web), xUnit + FluentAssertions, System.Text.Json
(source-generated contexts), `System.Diagnostics.Process`, `AssemblyLoadContext`.

**Spec:** `docs/superpowers/specs/2026-10-25-poker-bot-workshop-design.md`. Read it; the plan argues from it.

## Global Constraints

- **Every outside bot runs in its own process** (C# through `PokerBotHost`); only house bots run in the app.
- **One process per seat per run**; duplicate deals are whole runs with fresh processes; batches play one table at a
  time by default.
- Bots see only what their seat may see: never another seat's hole cards, the deck or the seed.
- Time limit per decision **1 second** by default (adjustable per match); a few seconds (**5 s**) to start and answer
  the hello.
- A late, malformed or illegal answer becomes **check if checking is free, otherwise fold**, and is counted. A raise
  outside the legal range is illegal, not clamped. A bot that crashes, exits or never starts is folded every time
  for the rest of the run.
- Protocol: JSON lines on stdin/stdout, **protocol version 1**; within a major version messages only gain fields and
  bots ignore unknown fields and message types. Each bot gets its own seed at match start.
- Batches: stacks reset every hand to **100 big blinds**, fixed blinds; results in **big blinds won per 100 hands
  with a 95% margin**; duplicate is the default for comparing bots.
- Workshop is desktop only (gated by a platform flag); the web build must compile and keeps today's Hold'em, with chips.
- Existing Card Table Hold'em keeps its rules and controls; all new text in English and Dutch
  (`shell/Assets/lang/nl.json`, checked by `tests/Apps.Tests`).
- **Engine rule (owner):** anything Jentl lacks goes into Jentl (`../jentl`); every engine change bumps the version
  (`Directory.Build.props` there, `<JentlVersion>` in `dependencies.props` here), then `build/jentl.sh`.
- Repo rules: TreatWarningsAsErrors, analyzers, files UTF-8 with BOM and LF; run `dotnet format whitespace` before
  each commit. Commits: no `Co-Authored-By` or "Generated with" lines. Work on a branch; no pushing (the owner pushes).

## Review Focus

1. A bot that answers after its time limit: the late answer must never be taken as the answer to the next decision
   (Task 3 and Task 12 test it).
2. A bot that prints debug text on stdout: a line that isn't a valid message is a malformed answer (folded or checked,
   counted), not a crash (Task 10 tests it).
3. A bot command that can't start (python missing, wrong path) or a DLL without exactly one bot: the seat shows the
   problem and is folded; the match still runs (Tasks 10, 11, 12 test it).
4. A rebuilt bot DLL while the app runs: the next match uses the new build, and the DLL file is never locked by the
   app or the host (Task 11 tests it).
5. Several all-ins of different sizes at a 9-seat table with chips from a folded player: pots and who can win each
   match the engine's payout (Task 6 tests it).

---

## Part 1: Engine (Jentl 0.6.0)

Work on a branch `poker-workshop` in `../jentl`.

### Task 1: Chips model (`Jentl.Cards`)

**Files:**
- Create: `../jentl/src/Jentl.Cards/Chips.cs`
- Test: `../jentl/tests/Jentl.Cards.Tests/ChipsTests.cs`

**Interfaces:**
- Produces: `readonly record struct ChipCount(int Value, int Count)`; `readonly record struct ChipStack(int Value, int Count)`;
  `static class Chips` with `IReadOnlyList<int> Denominations` = `[1, 5, 25, 100, 500, 1000, 5000, 25000]`,
  `IReadOnlyList<ChipCount> Break(int amount, IReadOnlyList<int>? denominations = null)` (fewest chips, largest first,
  greedy), `IReadOnlyList<ChipStack> Stacks(IReadOnlyList<ChipCount> chips, int maxPerStack)` (splits counts above the
  maximum into several stacks of one value, largest values first).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Break_UsesTheFewestChips_LargestFirst() =>
    Chips.Break(1_637).Should().Equal(new ChipCount(1000, 1), new ChipCount(500, 1), new ChipCount(100, 1), new ChipCount(25, 1), new ChipCount(5, 2), new ChipCount(1, 2));
[Fact] public void Break_OfNothing_IsEmpty() => Chips.Break(0).Should().BeEmpty();
[Fact] public void Break_OfANegativeAmount_Throws() => ((Action)(() => Chips.Break(-1))).Should().Throw<ArgumentOutOfRangeException>();
[Fact] public void Stacks_SplitTallStacks() =>
    Chips.Stacks([new ChipCount(100, 23), new ChipCount(5, 2)], maxPerStack: 10).Should().Equal(new ChipStack(100, 10), new ChipStack(100, 10), new ChipStack(100, 3), new ChipStack(5, 2));
```

- [ ] **Step 2: Run** `dotnet test tests/Jentl.Cards.Tests --filter ChipsTests` — Expected: FAIL (no `Chips`).
- [ ] **Step 3: Implement** `Chips` as in Interfaces.
- [ ] **Step 4: Run** the same — Expected: PASS.
- [ ] **Step 5: Commit** `git add -A && git commit -m "Cards: chips, broken into denominations and stacks"`

### Task 2: Chip drawing and flights (`Jentl.Core`)

**Files:**
- Create: `../jentl/src/Jentl.Core/Cards/ChipRenderer.cs`, `../jentl/src/Jentl.Core/Cards/ChipFlight.cs`
- Test: `../jentl/tests/Jentl.Core.Tests/ChipTests.cs`

**Interfaces:**
- Consumes: `Chips`, `ChipStack` (Task 1); `Tween`/easing in `Jentl.Core.Graphics`.
- Produces: `static class ChipRenderer` with `Color ColorOf(int value)` (a distinct colour per default denomination:
  1 white, 5 red, 25 green, 100 black, 500 purple, 1000 yellow, 5000 orange, 25000 light blue; other values grey),
  `void DrawAmount(SpriteBatch batch, Vector2 bottomCentre, int amount, float chipWidth, int maxPerStack = 10)` (stacks
  side by side, each chip a flat ellipse with an edge stripe, drawn with the batch's pixel texture), and
  `float Width(int amount, float chipWidth, int maxPerStack = 10)`; `sealed class ChipFlight(int amount, Vector2 from,
  Vector2 to, TimeSpan duration)` with `Vector2 Position`, `bool IsDone`, `void Update(TimeSpan elapsed)` (eased in-out).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void EveryDenomination_HasItsOwnColour() =>
    Chips.Denominations.Select(ChipRenderer.ColorOf).Distinct().Should().HaveCount(Chips.Denominations.Count);
[Fact] public void AFlight_StartsAtItsOrigin_EndsAtItsTarget()
{
    var flight = new ChipFlight(50, new Vector2(0, 0), new Vector2(100, 40), TimeSpan.FromSeconds(0.4));
    flight.Position.Should().Be(new Vector2(0, 0));
    flight.Update(TimeSpan.FromSeconds(0.2));
    flight.Position.X.Should().BeInRange(1, 99);
    flight.Update(TimeSpan.FromSeconds(0.3));
    (flight.IsDone, flight.Position).Should().Be((true, new Vector2(100, 40)));
}
[Fact] public void Width_GrowsWithTheNumberOfStacks() =>
    ChipRenderer.Width(1_637, 10).Should().BeGreaterThan(ChipRenderer.Width(1_000, 10));
```

- [ ] **Step 2: Run** `dotnet test tests/Jentl.Core.Tests --filter ChipTests` — Expected: FAIL.
- [ ] **Step 3: Implement** as in Interfaces (drawing is not unit-tested; it is checked on screen in Task 15).
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -am "Core: chip stacks drawn in denomination colours, and chips that fly"` (add new files first).

### Task 3: Agent processes (`Jentl.Net`)

**Files:**
- Create: `../jentl/src/Jentl.Net/AgentProcess.cs`, `../jentl/tests/Jentl.Net.TestAgent/` (console project:
  `Program.cs`, `Jentl.Net.TestAgent.csproj`)
- Modify: `../jentl/tests/Jentl.Net.Tests/Jentl.Net.Tests.csproj` (reference the test agent with
  `ReferenceOutputAssembly="false"` and copy its output next to the tests), the engine solution file
- Test: `../jentl/tests/Jentl.Net.Tests/AgentProcessTests.cs`

**Interfaces:**
- Produces: `sealed record AgentCommand(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory = null)`;
  `sealed class AgentProcess : IDisposable` with `static AgentProcess Start(AgentCommand command)` (never throws: a
  process that can't start is created already exited with `Problem` set), `void Send(string line)`,
  `Task<string?> ReadAsync(TimeSpan limit)` (the next line, or null on time-out or exit),
  `Task<string?> AskAsync(string line, TimeSpan limit)` (**first discards every line already received**, then sends
  and reads), `bool HasExited`, `string? Problem` ("could not start: …", "exited with code 3", "timed out"),
  `IReadOnlyList<string> ErrorOutput` (the last 50 lines of stderr), `void Stop()` (kills the process tree);
  `Dispose` stops it.
- The test agent: `Jentl.Net.TestAgent <mode>` with modes `echo` (answers each line with the same line), `slow:<ms>`
  (waits, then echoes), `crash` (reads one line, writes to stderr "boom", exits 3), `silent` (reads, never answers),
  `chatty` (writes "hello world" before each echo).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Ask_GetsTheAnswer() { using var a = Start("echo"); (await a.AskAsync("hi", OneSecond)).Should().Be("hi"); }
[Fact] public async Task ALateAnswer_IsNeverTakenForTheNextQuestion()
{
    using var a = Start("slow:300");
    (await a.AskAsync("first", TimeSpan.FromMilliseconds(50))).Should().BeNull();
    await Task.Delay(400);                                   // "first" arrives late
    (await a.AskAsync("second", OneSecond)).Should().Be("second");
}
[Fact] public async Task ACrash_EndsInAProblem_WithItsErrorOutput()
{
    using var a = Start("crash");
    (await a.AskAsync("x", OneSecond)).Should().BeNull();
    (a.HasExited, a.Problem).Should().Be((true, "exited with code 3"));
    a.ErrorOutput.Should().Contain("boom");
}
[Fact] public void ACommandThatCannotStart_IsAProblem_NotAnException()
{
    using var a = AgentProcess.Start(new AgentCommand("no-such-program-anywhere", []));
    a.HasExited.Should().BeTrue();
    a.Problem.Should().StartWith("could not start");
}
[Fact] public async Task ASilentAgent_TimesOut_AndCanBeStopped()
{
    using var a = Start("silent");
    (await a.AskAsync("x", TimeSpan.FromMilliseconds(100))).Should().BeNull();
    a.Stop();
    a.HasExited.Should().BeTrue();
}
```

(`Start(mode)` runs the copied test agent with `dotnet Jentl.Net.TestAgent.dll <mode>`; `OneSecond` is a field.)

- [ ] **Step 2: Run** `dotnet test tests/Jentl.Net.Tests --filter AgentProcessTests` — Expected: FAIL.
- [ ] **Step 3: Implement** with `Process` (redirected stdin/stdout/stderr, UTF-8, `EnableRaisingEvents`), lines
  from stdout queued in a `Channel<string>`, stderr kept in a ring of 50, `Kill(entireProcessTree: true)` in `Stop`.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Net: agents in their own process, talking JSON lines with time limits"` (add files first).

### Task 4: Timed decisions, duplicate seatings, running statistics

**Files:**
- Create: `../jentl/src/Jentl.Multiplayer/Decisions.cs`, `../jentl/src/Jentl.Multiplayer/Seatings.cs`,
  `../jentl/src/Jentl.Collect/RunningStats.cs`
- Test: `../jentl/tests/Jentl.Multiplayer.Tests/DecisionTests.cs` (create the test project if there is none, like the
  other engine test projects), `../jentl/tests/Jentl.Collect.Tests/RunningStatsTests.cs`

**Interfaces:**
- Produces: `enum Fault { None, Late, Illegal, Gone }`; `readonly record struct Decided<TMove>(TMove Move, Fault Fault)`;
  `static class Decisions` with `Task<Decided<TMove>> DecideAsync<TMove>(Func<CancellationToken, Task<TMove?>> ask,
  TimeSpan limit, Func<TMove, bool> isLegal, Func<TMove> fallback, Func<bool>? isGone = null)` where TMove : class
  (null answer before the limit is Illegal, after it Late; an exception from `ask` is Gone; `isGone` true is Gone
  without asking); `static class Seatings` with `IReadOnlyList<int[]> Duplicate(int seats)` (element *r* is the
  seating of run *r*: `seating[seat] = bot`; 2 seats → `[[0,1],[1,0]]`; *n* seats → the *n* rotations);
  `sealed class RunningStats` with `void Add(double value)`, `int Count`, `double Mean`, `double StandardDeviation`
  (sample), `double Margin95` (1.96 × sd / √n; 0 below two values).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task ALegalAnswerInTime_IsTaken() =>
    (await Decisions.DecideAsync(_ => Task.FromResult<string?>("call"), OneSecond, m => m == "call", () => "fold")).Should().Be(new Decided<string>("call", Fault.None));
[Fact] public async Task AnIllegalAnswer_IsReplaced() =>
    (await Decisions.DecideAsync(_ => Task.FromResult<string?>("raise"), OneSecond, m => m == "call", () => "fold")).Should().Be(new Decided<string>("fold", Fault.Illegal));
[Fact] public async Task ALateAnswer_IsReplaced_AndTheAskIsCancelled()
{
    var cancelled = false;
    var d = await Decisions.DecideAsync(async c => { try { await Task.Delay(1000, c); } catch (OperationCanceledException) { cancelled = true; } return "call"; }, TimeSpan.FromMilliseconds(50), _ => true, () => "fold");
    d.Should().Be(new Decided<string>("fold", Fault.Late));
    await Task.Delay(100); cancelled.Should().BeTrue();
}
[Fact] public async Task AThrowingAgent_IsGone() =>
    (await Decisions.DecideAsync<string>(_ => throw new IOException(), OneSecond, _ => true, () => "fold")).Fault.Should().Be(Fault.Gone);
[Fact] public void HeadsUp_SwapsSeats() => Seatings.Duplicate(2).Should().BeEquivalentTo(new[] { new[] { 0, 1 }, new[] { 1, 0 } }, o => o.WithStrictOrdering());
[Fact] public void Ring_PutsEveryBotInEverySeatOnce()
{
    var runs = Seatings.Duplicate(4);
    runs.Should().HaveCount(4);
    Enumerable.Range(0, 4).Should().OnlyContain(seat => runs.Select(r => r[seat]).Distinct().Count() == 4);
}
[Fact] public void Stats_GiveMeanAndMargin()
{
    var s = new RunningStats(); foreach (var v in new[] { 2.0, 4, 4, 4, 5, 5, 7, 9 }) s.Add(v);
    (s.Count, s.Mean).Should().Be((8, 5.0));
    s.StandardDeviation.Should().BeApproximately(2.138, 0.001);
    s.Margin95.Should().BeApproximately(1.96 * 2.138 / Math.Sqrt(8), 0.001);
}
```

- [ ] **Step 2: Run** `dotnet test tests/Jentl.Multiplayer.Tests` and `tests/Jentl.Collect.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement** (`RunningStats` by Welford's method).
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Multiplayer: timed decisions with fallbacks, duplicate seatings; Collect: running statistics"`

### Task 5: Engine docs and release 0.6.0

**Files:** Modify `../jentl/docs/kits.md` (a "Chips" row in the card kit, an "Agents in their own process" part in
Online multiplayer, the decisions and seatings in Multiplayer, `RunningStats` in Collecting), `../jentl/README.md`
(package rows), `../jentl/Directory.Build.props` (`<Version>0.6.0</Version>`).

- [ ] **Step 1:** Update the docs (one short example each, as the existing sections do).
- [ ] **Step 2: Run** `dotnet test` and `dotnet format whitespace --verify-no-changes` in `../jentl` — Expected: all pass, no changes.
- [ ] **Step 3: Commit** `git commit -am "Chips, agent processes, timed decisions, duplicate seatings (0.6.0)"`
- [ ] **Step 4:** In games-cs (branch `poker-workshop`): `<JentlVersion>0.6.0</JentlVersion>` in `dependencies.props`,
  run `build/jentl.sh` — Expected: "Packed Jentl 0.6.0".

## Part 2: Rules, SDK and arena (framework-free)

### Task 6: Pots, and hand series with fixed stacks

**Files:**
- Modify: `src/Poker/HoldemHand.cs` (public pots; `Award` uses them)
- Create: `src/Poker/HandSeries.cs`
- Test: `tests/Poker.Tests/PotsTests.cs`, `tests/Poker.Tests/HandSeriesTests.cs`

**Interfaces:**
- Produces: `sealed record Pot(int Amount, IReadOnlyList<int> Seats)`; `HoldemHand.Pots` (`IReadOnlyList<Pot>`: the
  main pot first, then side pots; levels whose live contenders are the same are merged; available at any moment, not
  only at showdown); `sealed class HandSeries(int seats, int seed, int stackBigBlinds = 100, int smallBlind = 10)` with
  `HoldemHand Deal(int index)` (every stack `stackBigBlinds × big blind`, button `index % seats`, the deck from a seed
  that depends only on `seed` and `index`), `int BigBlind`.

- [ ] **Step 1: Write the failing tests**
  - `Pots_WithThreeAllInsOfDifferentSizes_AndAFoldedStake`: 4 seats with stacks 100, 300, 600, 1000; seat 3 bets 400,
    seats 0-2 go all in, seat 3 calls… build it with `Apply` from a fixed seed; assert `Pots` amounts and seats, and
    that after the hand `Winnings.Values.Sum()` equals `Pots.Sum(p => p.Amount)`.
  - `Pots_BeforeAnyAllIn_IsOnePot`: after the blinds of a 3-seat hand, `Pots` is one pot of 30 for seats 0-2.
  - `TheSameSeries_DealsTheSameCards`: two `HandSeries(6, seed: 9)` deal hand 4 with equal hole cards per seat and
    equal boards after checking down.
  - `EveryHand_StartsWith100BigBlinds_AndTheButtonMoves`: hands 0-2 have stacks 2000 (before blinds) and buttons 0, 1, 2.
- [ ] **Step 2: Run** `dotnet test tests/Poker.Tests --filter "PotsTests|HandSeriesTests"` — Expected: FAIL.
- [ ] **Step 3: Implement** (pots from commitment levels as `Award` does now; `Award` loops over `Pots`).
- [ ] **Step 4: Run** `dotnet test tests/Poker.Tests` — Expected: PASS (the existing showdown tests still pass).
- [ ] **Step 5: Commit** `git commit -m "Poker: pots at any moment, hand series with fixed stacks"` (add files first).

### Task 7: The bot SDK (`src/Poker.Bots`)

**Files:**
- Create: `src/Poker.Bots/Poker.Bots.csproj` (framework-free, no Jentl reference, `PackageId` `Poker.Bots`, version
  `1.0.0`), `src/Poker.Bots/Messages.cs`, `src/Poker.Bots/IPokerBot.cs`, `src/Poker.Bots/BotRunner.cs`,
  `tests/Poker.Bots.Tests/` (project + tests); add both to `Games.slnx`.
- Test: `tests/Poker.Bots.Tests/ProtocolTests.cs`, `tests/Poker.Bots.Tests/BotRunnerTests.cs`

**Interfaces (namespace `PokerBots`):**
- Cards are two-character codes: rank `23456789TJQKA` + suit `shdc` ("As", "Td").
- Messages, each a JSON object with `"type"` (System.Text.Json, camelCase, polymorphic on `type`):
  - app → bot: `hello` `{ protocol: 1 }`; `match-start` `{ seat, seats, format ("sit-and-go"|"fixed-stacks"),
    startingStack, smallBlind, bigBlind, timeLimitMs, seed }`; `hand-start` `{ hand, button, smallBlind, bigBlind,
    seats: [{ seat, name, stack, state }] }`; `decision` (below); `hand-end` `{ hand, board, actions, shown: [{ seat,
    cards, rank }], won: [{ seat, amount }], stacks }`; `match-end` `{ standings: [{ seat, name, stack, place }] }`.
  - bot → app: `hello` `{ protocol: 1, name }`; `action` `{ action: "fold"|"check"|"call"|"raise"|"all-in", to? }`.
  - `decision` `{ hand, street ("preflop"|"flop"|"turn"|"river"), you, hole, board, button, smallBlindSeat,
    bigBlindSeat, smallBlind, bigBlind, level, seats: [{ seat, name, stack, bet, state ("active"|"folded"|"all-in"|"out") }],
    pots: [{ amount, seats }], toCall, legal: { fold: true, check, call?, minRaiseTo?, maxRaiseTo?, allIn }, actions:
    [{ street, seat, action, amount, total }], timeLimitMs }`.
- Records: `Hello`, `MatchStart`, `HandStart`, `Decision`, `HandEnd`, `MatchEnd`, `BotAction` (with `static BotAction
  Fold/Check/Call/AllIn` and `Raise(int to)`), and their parts; `static class Protocol` with `const int Version = 1`,
  `string Write(Message m)`, `Message? Read(string line)` (null for anything that isn't a known message).
- `interface IPokerBot { string Name { get; } void OnMatchStart(MatchStart m) {} void OnHandStart(HandStart h) {}
  BotAction Decide(Decision d); void OnHandEnd(HandEnd h) {} void OnMatchEnd(MatchEnd m) {} }` (default no-ops).
- `static class BotRunner` with `int Run(IPokerBot bot, TextReader input, TextWriter output, TextWriter errors)`:
  answers `hello`, passes the others on, writes one `action` per `decision` and flushes, ignores unknown lines,
  returns 0 at `match-end` or end of input; an exception in `Decide` is written to `errors` and no answer is sent.

- [ ] **Step 1: Write the failing tests**
  - `EveryMessage_RoundTrips` (one instance of each record → `Write` → `Read` → equal).
  - `UnknownFieldsAndTypes_AreIgnored`: `Read("""{"type":"decision","hand":1,...,"future":3}""")` succeeds;
    `Read("""{"type":"later-message"}""")` is null; `Read("hello world")` is null.
  - `TheRunner_AnswersHelloAndDecisions`: feed `hello`, `match-start`, `decision`, `match-end` lines to a bot that
    always calls; output lines are `{"type":"hello","protocol":1,"name":"Caller"}` and `{"type":"action","action":"call"}`;
    `Run` returns 0.
  - `ABotThatThrows_SendsNothing_AndSaysWhy`: output has the hello only; errors contain the exception message.
- [ ] **Step 2: Run** `dotnet test tests/Poker.Bots.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement** as in Interfaces.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker bot SDK: the contract, its JSON and a stdin/stdout runner"` (add files first).

### Task 8: What a bot sees, and checking its answer (`src/Poker/BotView.cs`)

**Files:**
- Modify: `src/Poker/Poker.csproj` (reference `src/Poker.Bots`)
- Create: `src/Poker/BotView.cs`
- Test: `tests/Poker.Tests/BotViewTests.cs`

**Interfaces:**
- Consumes: SDK records (Task 7), `HoldemHand`, `Pots` (Task 6).
- Produces: `sealed record ActionRecord(Street Street, int Seat, PokerAction Action, int Amount, int Total)`;
  `static class BotView` with `string Code(Card card)`, `Decision Decision(HoldemHand hand, int seat, int handNumber,
  int level, IReadOnlyList<string> names, IReadOnlyList<ActionRecord> actions, TimeSpan limit)`,
  `HandStart HandStart(...)`, `HandEnd HandEnd(HoldemHand hand, int handNumber, IReadOnlyList<ActionRecord> actions)`
  (only cards shown at showdown), `PokerAction? ToAction(BotAction answer, HoldemHand hand, int seat)` (null when
  illegal: check facing a bet, call with nothing to call, raise below `MinRaiseTo` unless it is all in, above
  `MaxRaiseTo`, or when raising isn't possible; fold always legal), `PokerAction Fallback(HoldemHand hand, int seat)`
  (check if free, else fold).

- [ ] **Step 1: Write the failing tests**
  - `ADecision_ShowsOnlyTheSeatsOwnCards`: 6-seat hand, `Protocol.Write(BotView.Decision(..., seat: 2, ...))` contains
    seat 2's two codes and none of the other seats' hole codes.
  - `ADecision_GivesTheLegalAmounts`: facing a raise to 60 with big blind 20: `toCall`, `legal.call`, `minRaiseTo` 100,
    `maxRaiseTo` = bet + stack, `check` false.
  - `IllegalAnswers_AreNull` (theory): check facing a bet, call with nothing to call, raise to min−1, raise above max,
    raise when only calling is possible.
  - `AShortAllIn_IsLegal`: raise to everything when it is below the minimum raise → `AllIn`.
  - `Fallback_ChecksWhenFree_FoldsOtherwise`.
  - `HandEnd_ShowsOnlyShownCards`: a hand won by a fold shows no hole cards.
- [ ] **Step 2: Run** `dotnet test tests/Poker.Tests --filter BotViewTests` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker: what a bot may see, and checking what it answers"`

### Task 9: House bots

**Files:**
- Create: `src/Poker/HouseBots.cs`
- Test: `tests/Poker.Tests/HouseBotTests.cs`

**Interfaces:**
- Consumes: `IPokerBot`, `Decision`, `BotAction` (Task 7); `PokerBot.Equity` (existing, static).
- Produces: `CallingBot`, `RaisingBot` (min raise, or all in when that is all it can do, or call when it can't
  raise), `RandomBot(int seed)` (uniform over the legal kinds, raise amounts uniform in range), `EquityBot(EquityStyle
  style, int seed)` with `enum EquityStyle { Tight, Balanced, Loose }` (the existing bot's logic on a `Decision`: hole,
  board, live opponents, pot and to-call; tightness/aggression/bluff per style as `PokerScene` uses them now);
  `static class HouseBots` with `IReadOnlyList<(string Name, Func<int, IPokerBot> Make)> All` (seed in, bot out) and
  names "Always call", "Always raise", "Random", "Tight", "Balanced", "Loose".

- [ ] **Step 1: Write the failing test** `HouseBots_OnlyEverAnswerLegally`: for every house bot, play 300 hands of a
  `HandSeries(6, seed: 3)` with that bot in every seat, asking through `BotView.Decision` and checking with
  `BotView.ToAction`; no answer is null. Plus `TheCallingBot_Calls` and `TheRaisingBot_RaisesTheMinimum`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker: house bots on the bot contract"`

### Task 10: Seats and the bots folder

**Files:**
- Create: `src/Poker/Arena/IArenaSeat.cs`, `src/Poker/Arena/HouseSeat.cs`, `src/Poker/Arena/ProcessSeat.cs`,
  `src/Poker/Arena/BotCatalog.cs`
- Modify: `src/Poker/Poker.csproj` (reference `Jentl.Net`)
- Test: `tests/Poker.Tests/SeatTests.cs`, `tests/Poker.Tests/BotCatalogTests.cs`

**Interfaces:**
- Consumes: `AgentProcess`, `AgentCommand` (Task 3), `Decisions`, `Fault` (Task 4), SDK (Task 7).
- Produces: `interface IArenaSeat : IDisposable { string Name { get; } string? Problem { get; } bool IsGone { get; }
  Task StartAsync(MatchStart start, TimeSpan limit); void Notify(Message message); Task<BotAction?> AskAsync(Decision
  decision, CancellationToken cancel); }` (`AskAsync` returns null for anything that isn't an `action`; it throws
  `IOException` when the bot is gone); `HouseSeat(IPokerBot bot)`; `ProcessSeat(AgentCommand command, string name)`
  (hello with `Protocol.Version`, a wrong protocol major is a problem: "speaks protocol 2, the app speaks 1");
  `enum BotKind { House, Program, CSharp }`; `sealed record BotEntry(string Name, BotKind Kind, AgentCommand? Command,
  string Source)`; `static class BotCatalog` with `IReadOnlyList<BotEntry> Scan(string folder, AgentCommand host)`:
  every `*.bot.json` (`{ "name", "command", "args": [], "workingDirectory" }`, the working directory relative to the
  file, default its folder) and every `*.dll` that is not the SDK (`Poker.Bots.dll`) as a C# bot run by `host` with the
  DLL path appended; unreadable descriptions become entries with `Source` naming the problem and no command.

- [ ] **Step 1: Write the failing tests**
  - `Catalog_FindsProgramsAndDlls` (temp folder with `bot.bot.json`, `Mine.dll`, `Poker.Bots.dll`, `broken.bot.json`).
  - `AProcessSeat_PlaysThroughTheProtocol`: a `ProcessSeat` on the Task 3 test agent can't speak the protocol, so use
    the SDK's runner through a tiny test bot console (`tests/Poker.Tests.EchoBot`, built and copied like Task 3's
    agent, running `BotRunner.Run` with a calling bot; argument `chatty` makes it print "thinking…" on stdout before
    each answer): `AskAsync` returns `BotAction.Call`.
  - `DebugPrints_AreNotAnswers`: with `chatty`, the seat skips lines that aren't messages until its answer, within
    the limit, and still returns `BotAction.Call`.
  - `ABotThatCannotStart_IsGone_WithItsProblem` (command "no-such-program").
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** (`ProcessSeat.AskAsync` reads lines until an `action` arrives or the token cancels).
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker: seats for house bots and bot processes, and the bots folder"`

### Task 11: The bot host for C# bots (`tools/PokerBotHost`)

**Files:**
- Create: `tools/PokerBotHost/PokerBotHost.csproj` (console, references `src/Poker.Bots`), `tools/PokerBotHost/Program.cs`,
  `tests/Poker.SampleBots/` (class library referencing `src/Poker.Bots`: `GoodBot` (calls), `SlowBot` (sleeps 5 s),
  `ThrowingBot`, `ExitingBot` (`Environment.Exit(4)`)); add to `Games.slnx`.
- Test: `tests/Poker.Tests/BotHostTests.cs` (the test project copies the host and the sample DLL next to itself)

**Interfaces:**
- Produces: `PokerBotHost <bot.dll> [TypeName]`: loads the DLL **from a byte stream** into a collectible
  `AssemblyLoadContext` that resolves `Poker.Bots` to the host's own copy (so the file is never locked); picks the
  named type, or the only public non-abstract `IPokerBot` with a parameterless constructor; checks that the DLL's
  `Poker.Bots` reference has the host's major version; then `BotRunner.Run(bot, stdin, stdout, stderr)`. Exit codes:
  0 done, 2 no single bot ("Mine.dll has no bot" / "has 2 bots: A, B; name one"), 3 SDK major mismatch.

- [ ] **Step 1: Write the failing tests**
  - `TheHost_RunsACSharpBot`: `ProcessSeat` with `dotnet PokerBotHost.dll Poker.SampleBots.dll GoodBot` answers a
    decision with `Call`.
  - `ADllWithSeveralBots_NeedsAName`: no type name → seat is gone with a problem containing "name one".
  - `ARebuiltDll_CanBeReplacedWhileItsBotRuns`: copy the sample DLL to a temp file, start a seat on it, then overwrite
    the file (`File.Copy(..., overwrite: true)` succeeds).
  - `AnExitingBot_EndsOnlyItsOwnProcess`: `ExitingBot` → seat gone, test process fine.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker bot host: C# bots in their own process"`

### Task 12: One table (`src/Poker/Arena/ArenaTable.cs`)

**Files:**
- Create: `src/Poker/Arena/ArenaTable.cs`, `src/Poker/Arena/HandRecord.cs`
- Test: `tests/Poker.Tests/ArenaTableTests.cs`

**Interfaces:**
- Consumes: seats (Task 10), `BotView` (Task 8), `Decisions` (Task 4), `HoldemTable`, `HandSeries` (Task 6).
- Produces: `enum TableFormat { SitAndGo, FixedStacks }`; `sealed record ArenaSettings(TableFormat Format, int Hands,
  int Seed, TimeSpan TimeLimit, int StartingStack = 1000, int SmallBlind = 10, int StackBigBlinds = 100)` (`Hands` is
  the maximum for a sit-and-go and the count for fixed stacks; each seat's seed is derived from `Seed` and its seat);
  `sealed record HandRecord(int Number, int Seed, int Button, int SmallBlind, int BigBlind, IReadOnlyList<string> Names,
  IReadOnlyList<int> StartStacks, IReadOnlyList<IReadOnlyList<Card>> Holes, IReadOnlyList<ActionRecord> Actions,
  IReadOnlyList<Card> Board, IReadOnlyList<Pot> Pots, IReadOnlyDictionary<int, int> Won, IReadOnlySet<int> Shown,
  IReadOnlyList<(int Seat, Fault Fault)> Faults)`; `sealed record SeatResult(int Seat, string Name, int Net, int
  Hands, int Late, int Illegal, bool Gone, string? Problem, int? Place)`; `sealed class ArenaTable(IReadOnlyList<IArenaSeat>
  seats, ArenaSettings settings) : IDisposable` with `Task StartAsync()`, `Task<IReadOnlyList<PokerEvent>> StepAsync(
  CancellationToken cancel)` (one decision, or dealing the next hand; returns the engine's events), `bool IsOver`,
  `HoldemHand? Hand`, `IReadOnlyList<HandRecord> Hands`, `IReadOnlyList<SeatResult> Results()`, `Task RunAsync(
  CancellationToken cancel)` (steps to the end).
- A gone seat is never asked again: `Decisions.DecideAsync` with `isGone`, the fallback from `BotView.Fallback`.

- [ ] **Step 1: Write the failing tests** (with a `FakeSeat` that answers from a function and can be late or throw):
  - `ALateSeat_IsFoldedOrChecked_AndCounted`: a seat that never answers within 50 ms: every one of its decisions is
    the fallback; `Results()` shows `Late` = the number of its decisions.
  - `ALateAnswer_DoesNotLeakIntoTheNextDecision`: the fake answers "raise to max" 100 ms after a 50 ms limit; the next
    decision gets the fake's next answer, never the stale one.
  - `AnIllegalRaise_IsCountedAndReplaced`.
  - `AGoneSeat_IsNeverAskedAgain`.
  - `TheSameSeedAndHouseBots_PlayTheSameMatch`: two runs of 50 fixed-stack hands with seeded `EquityBot`s give equal
    `HandRecord`s.
  - `ASitAndGo_EndsWithPlaces`: four calling bots and one raising bot; `IsOver`, places 1-5 each used once, nets sum to 0.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** (events to seats: `hand-start` before each hand, `hand-end` after; `match-end` at the end;
  every seat gets `match-start` in `StartAsync` with its own seed).
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker arena: one table of bots under time limits"`

### Task 13: Hand histories and statistics

**Files:**
- Create: `src/Poker/Arena/HandHistoryText.cs`, `src/Poker/Arena/PokerStats.cs`, `src/Poker/Arena/MatchFile.cs`
- Test: `tests/Poker.Tests/HandHistoryTests.cs`, `tests/Poker.Tests/PokerStatsTests.cs`

**Interfaces:**
- Consumes: `HandRecord`, `SeatResult` (Task 12).
- Produces: `static class HandHistoryText` with `string Write(HandRecord hand, string table, DateTimeOffset at)`
  (PokerStars style: `PokerStars Hand #<seed><number>: Hold'em No Limit (<sb>/<bb>) - <date>`, `Table '<table>'
  9-max Seat #<button+1> is the button`, `Seat n: Name (stack in chips)`, blinds lines, `*** HOLE CARDS ***`, `Dealt to`
  lines for every seat, actions `Name: folds|checks|calls x|bets x|raises x to y`, `*** FLOP *** [Ah Kd 2c]`, `***
  TURN ***`, `*** RIVER ***`, `*** SHOW DOWN ***`, `*** SUMMARY ***` with `Total pot`); `sealed record BotStats(int
  Hands, double Vpip, double Pfr, double AggressionFactor, double WentToShowdown, double WonAtShowdown)`; `static class
  PokerStats` with `BotStats For(IEnumerable<HandRecord> hands, string name)` (VPIP: hands where it put money in
  preflop beyond a forced blind; PFR: hands it raised preflop; AF: (bets + raises) / calls after the flop, 0 without
  calls; WTSD: of hands it saw the flop, how many it reached showdown; W$SD: of those, how many it won something);
  `static class MatchFile` with `string Write(MatchResult)` and `MatchResult Read(string)` (`sealed record MatchResult(
  ArenaSettings Settings, DateTimeOffset At, IReadOnlyList<string> Bots, IReadOnlyList<BotResult> Results,
  IReadOnlyList<HandRecord> Hands)` and `sealed record BotResult(string Name, double BigBlindsPer100, double Margin95,
  int Hands, int Late, int Illegal, int Gone, IReadOnlyList<int> Places)`; a watched match's `MatchResult` has one
  `BotResult` per seat with margin 0).

- [ ] **Step 1: Write the failing tests**: a hand-built `HandRecord` (3 seats, flop, a raise, showdown) exported
  contains the exact lines `*** FLOP *** [Ah Kd 2c]`, `Bob: raises 40 to 60`, `*** SUMMARY ***`; `PokerStats` on four
  hand-built records gives the expected VPIP/PFR/AF/WTSD/W$SD values (write the numbers into the test); `MatchFile`
  round-trips.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker arena: hand histories, statistics and match files"`

### Task 14: Batches and duplicate (`src/Poker/Arena/ArenaBatch.cs`)

**Files:**
- Create: `src/Poker/Arena/ArenaBatch.cs`
- Test: `tests/Poker.Tests/ArenaBatchTests.cs`

**Interfaces:**
- Consumes: `ArenaTable` (Task 12), `Seatings`, `RunningStats` (Task 4).
- Consumes also: `MatchResult`, `BotResult` (Task 13).
- Produces: `enum BatchFormat { DuplicateHeadsUp, DuplicateRing, SitAndGoSeries }`; `sealed record BatchSettings(
  BatchFormat Format, int Hands, int Seed, TimeSpan TimeLimit, bool Parallel = false)` (`Hands` per run for duplicate,
  the number of sit-and-gos for a series); `sealed class ArenaBatch(
  IReadOnlyList<Func<IArenaSeat>> bots, BatchSettings settings)` with `Task<MatchResult> RunAsync(IProgress<double>
  progress, CancellationToken cancel)`. Each run gets fresh seats from the factories, seated by
  `Seatings.Duplicate(bots.Count)`; duplicate runs use the same `Seed`, so hand *i* has the same deck in every run.
  A bot's sample per deck is the sum, over all runs, of its net on that deck, in big blinds; bb/100 = mean × 100 /
  (number of runs) and the margin likewise from `RunningStats`. A cancelled batch returns what was played so far.

- [ ] **Step 1: Write the failing tests**
  - `IdenticalDeterministicBots_ComeOutEven`: two `CallingBot`s, duplicate heads-up, 200 hands per run: both bb/100
    exactly 0 and margin 0.
  - `DuplicateRing_SeatsEveryBotEverywhere`: 3 different house bots; the records show each bot in each seat for hand 0.
  - `MoreHands_NarrowTheMargin`: `EquityBot(Balanced)` against `RandomBot`, 100 then 800 hands per run: the margin shrinks.
  - `ASeries_ReportsPlaces`: 5 sit-and-gos of 3 bots: every bot's `Places` has 5 entries.
  - `Cancelling_StopsAndKeepsWhatWasPlayed`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test tests/Poker.Tests` — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker arena: batches with duplicate deals and margins"`

## Part 3: The app

### Task 15: Chips on the Hold'em table, 2 to 9 seats (shared desktop and web)

**Files:**
- Create: `apps/CardTable/Game/Poker/PokerTableView.cs`
- Modify: `apps/CardTable/Game/Scenes/PokerScene.cs` (draws through the view)
- Test: `tests/Apps.Tests/PokerTableLayoutTests.cs` (or the closest existing app test project that can see the game
  code; if none can, test the layout function from `src/Poker` instead and keep the drawing in the app)

**Interfaces:**
- Consumes: `ChipRenderer`, `ChipFlight` (Task 2), `HoldemHand.Pots` (Task 6).
- Produces: `sealed class PokerTableView` with `static Vector2[] SeatPositions(int seats, Rectangle felt)` (seat 0
  bottom centre, then clockwise on the felt's ellipse), `void Draw(SpriteBatch batch, Rectangle viewport, HoldemHand
  hand, IReadOnlyList<string> names, Func<int, bool> faceUp, IReadOnlyDictionary<int, string> lastAction)` (each seat:
  name, stack as chips and number, its bet in front of it; the pot and each side pot separately in the middle; dealer
  button and small/big blind markers), `void Update(TimeSpan elapsed, IReadOnlyList<PokerEvent> events)` (starts chip
  flights: bets to the pot at the end of a street, the pot to the winner on `PotWon`), `bool IsAnimating`.

- [ ] **Step 1: Write the failing test** `SeatPositions_AreDistinct_SeatZeroAtTheBottom` for 2 to 9 seats.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement**, and switch `PokerScene` to the view (its own controls, log and overlays stay).
- [ ] **Step 4: Run** the test — Expected: PASS. Then screenshot the desktop table against 5 bots
  (`shot.sh`-style, Card Table → Texas Hold'em) and check chips, side pots and the button by eye; `dotnet build
  apps/CardTable/Web` — Expected: succeeds.
- [ ] **Step 5: Commit** `git commit -m "Card Table: chips on the Hold'em table, 2 to 9 seats"`

### Task 16: Workshop menu, bots list and match setup (desktop only)

**Files:**
- Modify: `shell/Shell/GameContext.cs` (`PlatformFeatures.CanRunBots`: desktop true, browser false),
  `apps/CardTable/Game/CardTableApp.cs` (a "Bot workshop" title entry when `CanRunBots`),
  `apps/CardTable/Desktop/*.csproj` (build `tools/PokerBotHost` and copy it next to the app)
- Create: `apps/CardTable/Game/Workshop/WorkshopScene.cs`, `apps/CardTable/Game/Workshop/MatchSetupScene.cs`,
  `apps/CardTable/Game/Workshop/WorkshopContext.cs` (`sealed class WorkshopContext(GameContext game)`: `BotsFolder`
  `<save dir>/bots`, `MatchesFolder` `<save dir>/workshop/matches`, `HistoriesFolder` `<save dir>/workshop/histories`,
  `AgentCommand Host` (the copied `PokerBotHost` next to the app), `IReadOnlyList<BotEntry> Bots()` (house bots, then
  the folder's), `IArenaSeat Seat(BotEntry bot, int seed)`)

**Interfaces:**
- Consumes: `BotCatalog`, `BotEntry`, `HouseBots` (Tasks 9-10), `ArenaSettings`, `BatchSettings` (Tasks 12, 14).
- Produces: `WorkshopScene` (the bots: house bots and the folder's, with their problems; Refresh; Open bots folder
  (opens it in the system's file browser); Watch a match; Run a batch; Past matches); `MatchSetupScene(WorkshopContext,
  bool batch)` (a form: table size 2-9, a bot per seat (a choice row over house bots, folder bots and, when watching,
  "You" at most once), format (watch: sit-and-go or fixed stacks; batch: duplicate heads-up (2 seats only), duplicate
  ring, sit-and-go series), hands, time limit (0.25, 0.5, 1, 2, 5 s), seed (shown, "New seed"), Start).

- [ ] **Step 1:** Implement; build `apps/CardTable/Desktop` and `apps/CardTable/Web` — Expected: both succeed, the web
  title has no workshop entry.
- [ ] **Step 2:** Screenshot the workshop and the setup with a `*.bot.json` and a sample DLL in a temporary save
  folder (`--save-dir`) — Expected: both bots listed; a broken description shows its problem.
- [ ] **Step 3: Commit** `git commit -m "Card Table: the bot workshop, its bots and match setup (desktop)"`

### Task 17: Watching a match

**Files:**
- Create: `apps/CardTable/Game/Workshop/WatchScene.cs`, `apps/CardTable/Game/Workshop/HumanSeat.cs`

**Interfaces:**
- Consumes: `ArenaTable` (Task 12), `PokerTableView` (Task 15), seats (Task 10).
- Produces: `WatchScene(WorkshopContext, ArenaTable table)`: steps the table off the UI thread (one `StepAsync` at a
  time, never two), feeds the events to the view; controls Pause, Step, 1x (an action every 650 ms), 4x (every 160 ms),
  Instant (to the end of the hand); a log of actions with faults marked ("Mine: late, folds"); at the end, the match
  is saved with `MatchFile` and the results scene opens. `HumanSeat : IArenaSeat` completes `AskAsync` when the
  person acts with the existing Hold'em controls (no time limit for people).

- [ ] **Step 1:** Implement; run a watched sit-and-go of 6 house bots and one with a `SlowBot` (sample DLL in the save
  folder), screenshot mid-hand — Expected: chips move, the slow bot's late folds show in the log, the app stays responsive.
- [ ] **Step 2: Commit** `git commit -m "Card Table: watching bots play, with speed controls"`

### Task 18: Batches, results, statistics and replays

**Files:**
- Create: `apps/CardTable/Game/Workshop/BatchScene.cs`, `apps/CardTable/Game/Workshop/ResultsScene.cs`,
  `apps/CardTable/Game/Workshop/ReplayScene.cs`, `apps/CardTable/Game/Workshop/MatchesScene.cs`

**Interfaces:**
- Consumes: `ArenaBatch`, `MatchResult`, `MatchFile`, `PokerStats`, `HandHistoryText` (Tasks 13-14).
- Produces: `BatchScene` (progress bar, hands per second, Cancel; saves the match); `ResultsScene(MatchResult)` (a
  table per bot: bb/100 "± margin", hands, late/illegal/gone, VPIP, PFR, AF, WTSD, W$SD, and for series average place
  and places; buttons Replay hands, Export hand histories (writes `<histories>/<match>.txt` and says where), Back);
  `ReplayScene(MatchResult, int hand)` (the hand on `PokerTableView` with every hole card shown; Previous/Next action,
  Previous/Next hand); `MatchesScene` (past matches, newest first, opening `ResultsScene`).

- [ ] **Step 1:** Implement; run a 500-hand duplicate heads-up of "Balanced" against "Always call", screenshot the
  results and a replay — Expected: numbers with margins, faults 0, replay steps through.
- [ ] **Step 2: Commit** `git commit -m "Card Table: batches, results with statistics, and hand replays"`

### Task 19: Starter kits, protocol document, SDK package, translations, README

**Files:**
- Create: `build/pokersdk.sh` (packs `src/Poker.Bots` into `.poker-packages`, like `build/jentl.sh`),
  `bots/csharp/MyBot/MyBot.csproj` + `MyBot.cs` (references package `Poker.Bots` 1.0.0 from `.poker-packages`; builds
  into `$(PokerBotsFolder)`, defaulting to the desktop save folder's `bots`), `bots/csharp/MyBot.Harness/` (console:
  plays MyBot in-process against the house bots, duplicate heads-up 2,000 hands each, prints bb/100 ± margin; uses
  `src/Poker` by project reference), `bots/python/example_bot.py` + `bots/python/example.bot.json`,
  `docs/poker-bots.md` (the protocol with an example of every message, the rules, the bots folder, how to test and debug)
- Modify: `shell/Assets/lang/nl.json` (every new text), `README.md` (a Bot workshop part under Texas Hold'em),
  `.gitignore` (`.poker-packages/`)

- [ ] **Step 1:** Write them; run `build/pokersdk.sh`, `dotnet build bots/csharp/MyBot`, `dotnet run --project
  bots/csharp/MyBot.Harness` — Expected: the harness prints a result per house bot.
- [ ] **Step 2:** Run the python example in a watched match (if `python3` is present) — Expected: it plays; without
  python the seat shows "could not start".
- [ ] **Step 3: Run** `dotnet test Games.slnx` — Expected: all pass, including `Apps.Tests` translations.
- [ ] **Step 4: Commit** `git commit -m "Poker bots: starter kits for C# and Python, the protocol document, Dutch texts"`

### Task 20: Final check

- [ ] **Step 1: Run** in `../jentl`: `dotnet test`, `dotnet format whitespace --verify-no-changes`; in games-cs:
  `dotnet test Games.slnx`, `dotnet format whitespace Games.slnx --verify-no-changes`, `dotnet build apps/CardTable/Web`
  — Expected: all green.
- [ ] **Step 2:** Play one human hand against bots on the old table (desktop) — Expected: same rules and controls, with chips.
- [ ] **Step 3:** Hand over: what was built, screenshots, the commands for the starter kits; the owner pushes and tags
  `v0.6.0`.
