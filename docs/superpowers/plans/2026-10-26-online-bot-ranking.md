# Online Bot Ranking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Poker bots from different people play ranked heads-up matches on the game server and climb a Glicko-2
ranking; owners run their bots at home through a runner, and see the ranking and their matches in Poker's workshop
and on a web page.

**Architecture:** Three layers. (1) Engine (`../jentl`, Jentl 0.6.3): Glicko-2 (`Jentl.Collect`); in `Jentl.Net` an
`IAgent` interface (what `AgentProcess` already is), a relay protocol that carries an agent's JSON lines over a
WebSocket, the runner side (`AgentRelay`: starts processes and relays) and the server side (`RemoteAgent`, an
`IAgent` whose lines come from a socket, and `RelayHost`, which authenticates sockets and attaches them to agents).
(2) games-cs rules: `src/Poker/Arena/AgentSeat` (today's `ProcessSeat`, over any `IAgent`), owner views of a match;
in `src/GameServer/Poker` the registry of owners and bots, ratings, the ranking, pairing, ranked and practice matches,
the match store, HTTP and WebSocket endpoints, a ranking page. (3) Programs and app: `tools/PokerRunner` (the
runner's command line), an Online view in Poker's bot workshop, docs, Docker image.

**Tech Stack:** C# 14 / .NET 10, ASP.NET Core minimal APIs and WebSockets, `WebApplicationFactory` for integration
tests, xUnit + FluentAssertions, System.Text.Json (source-generated contexts), `SHA256`, `GZipStream`, `HttpClient`
(desktop app), MonoGame (app).

**Spec:** `docs/superpowers/specs/2026-10-26-online-bot-ranking-design.md`. Read it; the plan argues from it. The
bot protocol is `docs/poker-bots.md` (version 1, unchanged).

## Global Constraints

- **Bots run on their owners' computers**; the server never runs code it was sent. The runner starts a **fresh bot
  process for every match**.
- **Online deals are never repeated**, in a match or across matches: every match gets a fresh `DealSeed.New()`
  (128 bits), secret on the server. No duplicate format online.
- Ranked match: **heads-up, 1,000 hands** (setting), **fixed stacks of 100 big blinds** reset every hand, **blinds
  1/2**, button alternating; result bb/100 with its 95% margin per bot; winner = more chips over the match, draw when
  equal; rating input win 1 / draw 0.5 / loss 0.
- Time: **1 s per decision + 0.5 s network allowance** (settings); **5 s** to say hello at match start. Late,
  malformed and illegal answers are replaced (check if free, else fold) and counted; **3 late answers in a row**
  stop the seat for the rest of the match. **While a runner is disconnected** its seat is folded without counting
  towards the 3; a runner that reconnects takes the seat back from the next decision; the match counts.
- Pairing: never a bot with itself or with a bot of the **same owner**; close in rating; not the same pair again
  within a cooldown.
- Glicko-2: new bots **1500 ± 350**, volatility 0.06, system constant τ = 0.5; each ranked match is its own rating
  period; a bot is **provisional while its deviation is above 100** (shown below established bots).
- Owners and bots: no passwords or e-mail; an **owner token** and per-bot **bot tokens**, random 32 bytes as
  base64url, **stored hashed** (SHA-256); bot tokens shown once and replaceable. **5 bots per owner**, **10
  registrations per address per day** (settings). Registration goes through **one service** (`Registration`) so
  invite codes or approval can be added later. Records keep room for more fields (created at, address).
- An owner sees of a match: every action, the board, **their own bot's hole cards** and **cards shown at
  showdown**; the opponent's other hole cards never leave the server.
- Bots see each other's real bot names in `seats[].name`.
- Bot protocol stays **version 1**; the runner–server relay protocol is separate and bots never see it.
- Server data in **one folder** (setting `poker-data`, env `JENTL_POKER_DATA`, default `poker-data`); writes go to a
  temporary file then replace (as `SaveStore`); matches gzip-compressed, one file each; a match in progress at
  shutdown is dropped, not rated.
- Behind a TLS proxy: forwarded headers honoured only when the setting `trust-proxy` is true.
- Desktop only for the runner and the Online view; the web gets the ranking page only. English and Dutch texts
  (`shell/Assets/lang/nl.json`, checked by `tests/Apps.Tests`).
- **Engine rule (owner):** anything Jentl lacks goes into Jentl (`../jentl`); every engine change bumps the version
  (`Directory.Build.props` there, `<JentlVersion>` in `dependencies.props` here), then `build/jentl.sh`.
- Repo rules: TreatWarningsAsErrors, analyzers, files UTF-8 with BOM and LF; `dotnet format whitespace` before each
  commit. Commits: no `Co-Authored-By` or "Generated with" lines. Work on a branch; no pushing (the owner pushes).

## Review Focus

1. A runner that drops mid-match and reconnects: its seat folds while away without being stopped by the 3-missed
   rule, resumes from the next decision, and the match is rated (Tasks 6 and 10 test it).
2. Only two bots of the same owner in the queue: they are never paired, however long they wait (Task 9 tests it).
3. The same bot token connected from two runners at once: the newer connection takes over, the older is told
   "connected elsewhere" and closed, and a match in progress keeps one seat, not two (Task 4 tests it).
4. A server restart: owners, bots, ratings and finished matches are still there; the match that was running is
   neither rated nor stored (Tasks 7, 8 and 10 test it).
5. A runner that floods the server (too many or too large messages): its connection is closed and another match on
   the server carries on to its end (Tasks 4 and 11 test it).

---

## Part 1: Engine (Jentl 0.6.3)

### Task 1: Glicko-2 (`Jentl.Collect`)

**Files:**
- Create: `../jentl/src/Jentl.Collect/Glicko2.cs`
- Test: `../jentl/tests/Jentl.Collect.Tests/Glicko2Tests.cs`

**Interfaces:**
- Produces: `readonly record struct GlickoRating(double Rating, double Deviation, double Volatility)` with
  `static GlickoRating New => new(1500, 350, 0.06)`; `readonly record struct GlickoResult(GlickoRating Opponent,
  double Score)` (1 win, 0.5 draw, 0 loss); `static class Glicko2` with `GlickoRating Update(GlickoRating player,
  IReadOnlyList<GlickoResult> results, double tau = 0.5)` (no results: only the deviation grows, as the paper says;
  the deviation never exceeds 350).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void GlickmansExample_GivesHisNumbers()
{
    var player = new GlickoRating(1500, 200, 0.06);
    var after = Glicko2.Update(player, [new(new(1400, 30, 0.06), 1), new(new(1550, 100, 0.06), 0), new(new(1700, 300, 0.06), 0)]);
    after.Rating.Should().BeApproximately(1464.06, 0.01);
    after.Deviation.Should().BeApproximately(151.52, 0.01);
    after.Volatility.Should().BeApproximately(0.05999, 0.00001);
}
[Fact] public void NoGames_OnlyWidensTheDeviation()
{
    var after = Glicko2.Update(new GlickoRating(1600, 50, 0.06), []);
    after.Rating.Should().Be(1600);
    after.Deviation.Should().BeGreaterThan(50);
}
[Fact] public void TheDeviation_NeverPassesItsStart() =>
    Glicko2.Update(GlickoRating.New, []).Deviation.Should().Be(350);
[Fact] public void AWin_RaisesTheRating_AndNarrowsTheDeviation()
{
    var after = Glicko2.Update(GlickoRating.New, [new(GlickoRating.New, 1)]);
    after.Rating.Should().BeGreaterThan(1500);
    after.Deviation.Should().BeLessThan(350);
}
```

- [ ] **Step 2: Run** `dotnet test tests/Jentl.Collect.Tests --filter Glicko2` (in `../jentl`) — Expected: FAIL (no
  `Glicko2`).
- [ ] **Step 3: Implement** Glickman's algorithm (scale 173.7178, the Illinois iteration for the volatility with
  ε = 0.000001).
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Collect: Glicko-2 ratings"`

### Task 2: `IAgent` (`Jentl.Net`)

**Files:**
- Create: `../jentl/src/Jentl.Net/IAgent.cs`
- Modify: `../jentl/src/Jentl.Net/AgentProcess.cs` (implements it)
- Test: `../jentl/tests/Jentl.Net.Tests/AgentProcessTests.cs`

**Interfaces:**
- Produces: `interface IAgent : IDisposable` with exactly the members `AgentProcess` has today: `bool HasExited`,
  `string? Problem`, `IReadOnlyList<string> ErrorOutput`, `void Send(string line)`, `Task<string?> ReadAsync(TimeSpan
  limit)`, `Task<string?> AskAsync(string line, TimeSpan limit)`, `void Stop()`; plus `bool IsAway => false` (a
  default member: true while a remote agent's connection is down; it may come back).

- [ ] **Step 1: Write the failing test** `[Fact] public void AnAgentProcess_IsAnAgent() =>
  AgentProcess.Start(EchoAgent()).Should().BeAssignableTo<IAgent>().Which.IsAway.Should().BeFalse();` (use the
  file's existing test-agent command helper).
- [ ] **Step 2: Run** `dotnet test tests/Jentl.Net.Tests --filter AnAgentProcess_IsAnAgent` — Expected: FAIL to
  compile.
- [ ] **Step 3: Implement** the interface and `AgentProcess : IAgent`; XML docs move to the interface (`<inheritdoc/>`
  on the class).
- [ ] **Step 4: Run** `dotnet test tests/Jentl.Net.Tests` — Expected: PASS (all).
- [ ] **Step 5: Commit** `git commit -m "Net: IAgent, what an agent process is to the code that asks it"`

### Task 3: Relay protocol and the runner side (`Jentl.Net`)

**Files:**
- Create: `../jentl/src/Jentl.Net/Relay.cs` (messages and JSON), `../jentl/src/Jentl.Net/AgentRelay.cs`,
  `../jentl/src/Jentl.Net/SocketLines.cs` (text frames as lines over a `WebSocket`, with limits)
- Test: `../jentl/tests/Jentl.Net.Tests/AgentRelayTests.cs` (an in-memory socket pair:
  `WebSocket.CreateFromStream` over two connected duplex pipes, in a test helper `SocketPair`)

**Interfaces:**
- Produces, `Relay.cs`: JSON objects with `"type"` (camelCase fields, source-generated `RelayJson` context):
  - runner → server: `RelayHello(string Token, int Version)` (`"hello"`, `Version` = `Relay.Version` = 1),
    `RelayLine(string Text)` (`"line"`: one line the agent wrote), `RelayExited(string? Problem)` (`"exited"`).
  - server → runner: `RelayWelcome(string Name)` (`"welcome"`), `RelayRefused(string Reason)` (`"refused"`; the
    server then closes), `RelayStart` (`"start"`: start a fresh agent process), `RelayLine(string Text)` (`"line"`:
    a line for the agent), `RelayStop` (`"stop"`: stop the agent), `RelayStatus(JsonElement Status)` (`"status"`:
    something for the runner to show, in the game's own JSON).
  - `static string Relay.Write(object message)`, `static object? Relay.Read(string text)` (null for unknown types or
    bad JSON).
- Produces, `SocketLines.cs`: `sealed class SocketLines(WebSocket socket, int maxLength = 65536, int maxPerSecond =
  200) : IDisposable` with `void Send(string text)` (queued, never blocks), `IAsyncEnumerable<string>
  ReadAllAsync(CancellationToken)` (ends when the socket closes; a message longer than `maxLength` or more than
  `maxPerSecond` in a second closes the socket with a policy-violation status and sets `string? Problem`),
  `Task CloseAsync(string reason)`.
- Produces, `AgentRelay.cs`: `sealed class AgentRelay(Func<CancellationToken, Task<WebSocket>> connect, string token,
  AgentCommand command) : IDisposable` with `Task RunAsync(CancellationToken cancel)` (connects, sends hello, then:
  `start` starts `AgentProcess.Start(command)` (stopping one still running), `line` sends to it, its output lines go
  back as `line`, its exit as `exited`, `stop` stops it; after a dropped connection it reconnects with backoff 1, 2,
  4 … 30 s and keeps the running agent; `refused` ends `RunAsync` with that reason), `event Action<string>? Status`
  (human-readable: "connected as X", "refused: …", "reconnecting in 4 s", and each `status` message's JSON as text).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Messages_RoundTrip() =>
    Relay.Read(Relay.Write(new RelayHello("t", 1))).Should().Be(new RelayHello("t", 1));
[Fact] public void UnknownMessages_ReadAsNull() => Relay.Read("""{"type":"nope"}""").Should().BeNull();
[Fact] public async Task TheRelay_SaysHello_StartsTheAgent_AndPassesLinesBothWays()
{
    using var pair = SocketPair.Create();
    using var relay = new AgentRelay(_ => Task.FromResult(pair.Client), "token", EchoAgent());
    var run = relay.RunAsync(Cancel);
    var server = new SocketLines(pair.Server);
    var lines = server.ReadAllAsync(Cancel).GetAsyncEnumerator();
    (await Next(lines)).Should().Be(new RelayHello("token", 1));
    server.Send(Relay.Write(new RelayWelcome("Bot")));
    server.Send(Relay.Write(new RelayStart()));
    server.Send(Relay.Write(new RelayLine("ping")));
    (await Next(lines)).Should().Be(new RelayLine("ping"), "the echo agent answers with the same line");
}
[Fact] public async Task Refused_EndsTheRun() { /* server sends RelayRefused("unknown token"); RunAsync completes, Status saw "refused: unknown token" */ }
[Fact] public async Task AFlood_ClosesTheSocket()
{
    using var pair = SocketPair.Create();
    var server = new SocketLines(pair.Server, maxLength: 100);
    var read = server.ReadAllAsync(Cancel).ToListAsync(Cancel);
    await new SocketLines(pair.Client).SendNowAsync(new string('x', 101));   // test helper on the client side
    (await read).Should().BeEmpty();
    server.Problem.Should().Contain("too long");
}
```

  (`Next` reads one line and `Relay.Read`s it; `EchoAgent()` is the existing test agent in echo mode.)
- [ ] **Step 2: Run** `dotnet test tests/Jentl.Net.Tests --filter "AgentRelay|Relay"` — Expected: FAIL to compile.
- [ ] **Step 3: Implement**; the agent's lines go back in the order it wrote them; `AgentRelay` never throws out of
  `RunAsync` for a socket error (it reconnects).
- [ ] **Step 4: Run** `dotnet test tests/Jentl.Net.Tests` — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Net: an agent's lines relayed over a WebSocket, the runner side"`

### Task 4: The server side: `RemoteAgent` and `RelayHost` (`Jentl.Net`)

**Files:**
- Create: `../jentl/src/Jentl.Net/RemoteAgent.cs`, `../jentl/src/Jentl.Net/RelayHost.cs`
- Test: `../jentl/tests/Jentl.Net.Tests/RemoteAgentTests.cs`

**Interfaces:**
- Consumes: Task 2 `IAgent`, Task 3 messages and `SocketLines`.
- Produces: `sealed class RemoteAgent : IAgent` (made by the host for one bot): `string Name`, `bool IsAway` (no
  socket attached), `bool HasExited` (after `Stop`, or after the runner sent `exited` for the current process),
  `string? Problem` ("stopped", or the runner's `exited` problem), `ErrorOutput` (empty), `Send` (queued to the
  socket as `line`; dropped while away), `ReadAsync`/`AskAsync` (as `AgentProcess`: `AskAsync` discards lines that
  came before it; both return null at the limit, and at once while away), `void Restart()` (sends `start`: a fresh
  process for a new match; clears `HasExited` and buffered lines), `Stop()` (sends `stop`), `void Status(JsonElement
  status)`.
- Produces: `sealed class RelayHost(Func<string, string?> nameOfToken)` (token → bot name, null for unknown) with
  `Task RunAsync(WebSocket socket, CancellationToken aborted)` (reads hello within 5 s; refuses an unknown token or a
  wrong version with `refused`; else sends `welcome`, attaches the socket to the bot's agent (made on first use) and
  relays until it closes; a second socket for the same bot takes over and the first gets `refused("connected
  elsewhere")`), `RemoteAgent? Agent(string name)`, `event Action<string>? Connected`, `event Action<string>?
  Disconnected` (by bot name), `int MaxLength = 65536`, `int MaxPerSecond = 200`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task AnUnknownToken_IsRefused() { /* host with nameOfToken => null; client sends hello; gets RelayRefused("unknown token"); socket closes */ }
[Fact] public async Task AKnownToken_IsWelcomed_AndItsAgentAnswers()
{
    // host with "t" => "Bot"; an AgentRelay with the echo agent connects; after Connected("Bot"):
    var agent = host.Agent("Bot")!;
    agent.Restart();
    (await agent.AskAsync("""{"type":"x"}""", TimeSpan.FromSeconds(5))).Should().Be("""{"type":"x"}""");
}
[Fact] public async Task WhileAway_AskAnswersNullAtOnce()
{
    var agent = /* a bot whose runner disconnected */;
    agent.IsAway.Should().BeTrue();
    var watch = Stopwatch.StartNew();
    (await agent.AskAsync("x", TimeSpan.FromSeconds(5))).Should().BeNull();
    watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
}
[Fact] public async Task AReconnect_TakesTheSameAgentBack() { /* disconnect, reconnect with same token: same RemoteAgent instance, IsAway false, answers again */ }
[Fact] public async Task ASecondConnection_TakesOver_AndTheFirstIsToldWhy() { /* two relays, same token: first sees Status "refused: connected elsewhere"; Agent("Bot") answers through the second */ }
[Fact] public async Task AFloodingRunner_IsClosed_AndAnotherBotStillAnswers() { /* bot A sends > MaxPerSecond lines; its socket closes (Disconnected("A")); bot B still answers an AskAsync */ }
```

- [ ] **Step 2: Run** `dotnet test tests/Jentl.Net.Tests --filter RemoteAgent` — Expected: FAIL to compile.
- [ ] **Step 3: Implement**; lines from a socket that was taken over are ignored; thread-safe (sockets attach and
  detach on their own tasks).
- [ ] **Step 4: Run** `dotnet test tests/Jentl.Net.Tests` — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Net: remote agents, whose lines come from a runner over a WebSocket"`

### Task 5: Engine docs and release 0.6.3

**Files:**
- Modify: `../jentl/Directory.Build.props` (`<Version>0.6.3</Version>`), `../jentl/docs/kits.md` (rows in the
  multiplayer/agents table for `IAgent`, `AgentRelay`, `RemoteAgent`, `RelayHost`, `SocketLines`; a row in the
  Collect table for `Glicko2`; a short example: a runner relaying a bot, and a server asking a remote agent),
  `../jentl/README.md` if it lists packages' contents.
- Then in games-cs: `<JentlVersion>0.6.3</JentlVersion>` in `dependencies.props`, `bash build/jentl.sh`.

- [ ] **Step 1: Run** in `../jentl`: `dotnet test` and `dotnet format whitespace --verify-no-changes` — Expected: all
  green.
- [ ] **Step 2: Commit** (jentl) `git commit -m "Glicko-2, remote agents over a relay (0.6.3)"`; (games-cs) `git
  commit -m "Use Jentl 0.6.3"` after `dotnet build Games.slnx` passes.

## Part 2: games-cs

### Task 6: `AgentSeat`: any agent at the table, away without being stopped

**Files:**
- Rename/Modify: `src/Poker/Arena/ProcessSeat.cs` → `src/Poker/Arena/AgentSeat.cs` (`sealed class AgentSeat(IAgent
  agent, string name) : IArenaSeat`), and keep `ProcessSeat` as a static factory `ProcessSeat.Start(AgentCommand
  command, string name) => new AgentSeat(AgentProcess.Start(command), name)`; update callers
  (`apps/Poker/Game/Workshop/WorkshopContext.cs`, tests).
- Test: `tests/Poker.Tests/Arena/SeatTests.cs`

**Interfaces:**
- Consumes: Task 2 `IAgent.IsAway`.
- Produces: `AgentSeat` with everything `ProcessSeat` had (hello, ids, `MaxMissed = 3`, problems), and: while
  `agent.IsAway`, `AskAsync` returns null at once and does **not** count towards `MaxMissed`; `bool IsAway =>
  agent.IsAway` on `AgentSeat` (not on `IArenaSeat`); `StartAsync` on an away agent waits for it up to the limit.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task AnAwayAgent_IsFolded_ButNotStopped()
{
    var agent = new FakeAgent { IsAway = true };      // test double implementing IAgent
    using var seat = new AgentSeat(agent, "Remote");
    for (var i = 0; i < 5; i++) (await seat.AskAsync(ADecision(i), Cancel)).Should().BeNull();
    seat.IsGone.Should().BeFalse("being away is not three late answers");
    agent.IsAway = false; agent.Answer = """{"type":"action","action":"call"}""";
    (await seat.AskAsync(ADecision(5), Cancel)).Should().NotBeNull();
}
[Fact] public async Task ThreeLateAnswers_WhileConnected_StillStopTheSeat() { /* FakeAgent never answers, not away: gone after 3 */ }
```

- [ ] **Step 2: Run** `dotnet test tests/Poker.Tests --filter "AnAwayAgent|ThreeLateAnswers"` — Expected: FAIL.
- [ ] **Step 3: Implement**; all existing `ProcessSeat` tests keep passing through `ProcessSeat.Start`.
- [ ] **Step 4: Run** `dotnet test Games.slnx` — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Poker: AgentSeat seats any agent, and an agent that is away is folded, not stopped"`

### Task 7: Owners, bots and tokens (`src/GameServer/Poker`)

**Files:**
- Create: `src/GameServer/Poker/PokerData.cs` (the data folder: safe writes, JSON contexts),
  `src/GameServer/Poker/Registry.cs`, `src/GameServer/Poker/Registration.cs`
- Modify: `src/GameServer/GameServer.csproj` (references `src/Poker`, `src/Poker.Bots`)
- Test: `tests/GameServer.Tests/Poker/RegistryTests.cs` (temporary data folder per test)

**Interfaces:**
- Produces: `sealed class PokerData(string folder)` with `T? Read<T>(string name, JsonTypeInfo<T> info)`, `void
  Write<T>(string name, T value, JsonTypeInfo<T> info)` (temporary file then replace), `string MatchesFolder`.
- Produces: `sealed record Owner(string Id, string TokenHash, string Name, DateTimeOffset Created, string? Address,
  bool Banned)`; `sealed record BotAccount(string Name, string OwnerId, string TokenHash, DateTimeOffset Created,
  bool Banned)`; `sealed class Registry(PokerData data, TimeProvider clock)` (loads `owners.json`, `bots.json`;
  thread-safe) with `Owner? OwnerOfToken(string token)`, `BotAccount? BotOfToken(string token)`, `BotAccount?
  Bot(string name)`, `IReadOnlyList<BotAccount> BotsOf(string ownerId)`, `void Ban(string botOrOwner)`.
- Produces: `sealed class Registration(Registry registry, RegistrationLimits limits, TimeProvider clock)` with
  `RegisterResult NewOwner(string name, string? address)` → `(Owner, string Token)` or a refusal;
  `RegisterResult NewBot(string ownerToken, string botName, string? address)` → `(BotAccount, string Token)` or a
  refusal ("name taken", "5 bots already", "too many registrations today", "unknown owner", "banned", "not a
  name"); `string? NewBotToken(string ownerToken, string botName)` (replaces; the old token stops working);
  `sealed record RegistrationLimits(int BotsPerOwner = 5, int PerAddressPerDay = 10)`. Names: 3–24 characters,
  letters, digits, space, `-`, `_`, `.`; unique ignoring case. Tokens: 32 random bytes, base64url; only
  SHA-256 hashes are stored.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ABot_IsFoundByItsToken_AndTheTokenIsNotStored()
{
    var owner = registration.NewOwner("Coen", "1.2.3.4").Value;
    var bot = registration.NewBot(owner.Token, "Shark", "1.2.3.4").Value;
    registry.BotOfToken(bot.Token)!.Name.Should().Be("Shark");
    File.ReadAllText(Path.Combine(folder, "bots.json")).Should().NotContain(bot.Token);
}
[Fact] public void ANewToken_ReplacesTheOld() { /* NewBotToken: old token → null, new → Shark */ }
[Fact] public void ASixthBot_IsRefused() { /* 5 ok, 6th refusal "5 bots already" */ }
[Fact] public void NamesAreUnique_IgnoringCase() { /* "Shark" then "shark": "name taken" */ }
[Fact] public void TooManyRegistrationsFromOneAddress_AreRefused() { /* 10 ok, 11th same address same day refused; next day (FakeTimeProvider) ok */ }
[Fact] public void ARestart_KeepsOwnersAndBots() { /* new Registry over the same folder finds the bot by token */ }
[Fact] public void ABannedBot_IsNotFoundByItsToken() { /* Ban("Shark"): BotOfToken → null, Bot("Shark").Banned true */ }
```

- [ ] **Step 2: Run** `dotnet test tests/GameServer.Tests --filter Registry` — Expected: FAIL to compile.
- [ ] **Step 3: Implement** (`Microsoft.Extensions.TimeProvider.Testing` for the tests' clock if it is not referenced
  yet).
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Game server: poker owners and bots, with hashed tokens and limits"`

### Task 8: Ratings and the ranking

**Files:**
- Create: `src/GameServer/Poker/Ratings.cs`
- Test: `tests/GameServer.Tests/Poker/RatingsTests.cs`

**Interfaces:**
- Consumes: Task 1 `Glicko2`; Task 7 `PokerData`, `Registry`.
- Produces: `sealed record BotRecord(string Bot, GlickoRating Rating, int Matches, int Won, int Drawn, int Lost,
  long Hands, long NetChips, DateTimeOffset? LastPlayed)` (`NetChips` in chips at blinds 1/2, so bb/100 = NetChips /
  2 / Hands × 100; a running sum of squares per hand is kept too for the margin: `double SumSquares`);
  `sealed class Ratings(PokerData data, Registry registry)` (`ratings.json`) with `BotRecord Of(string bot)`, `void
  Record(RankedOutcome outcome)` (both bots updated from their ratings *before* the match, one rating period;
  saved at once), `IReadOnlyList<RankingRow> Ranking()`; `sealed record RankedOutcome(string BotA, string BotB, double
  ScoreA, long HandsPlayed, IReadOnlyList<int> NetPerHandA)`; `sealed record RankingRow(int? Place, string Bot,
  string Owner, double Rating, double Range, bool Provisional, int Matches, int Won, int Drawn, int Lost, double
  BbPer100, double Margin, DateTimeOffset? LastPlayed)` — `Range` = 2 × deviation; established bots (deviation ≤
  100) first by rating with places 1, 2, …; provisional bots after them by rating with `Place` null; banned bots
  left out.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AWin_MovesBothBots_FromTheirRatingsBefore() { /* A beats B: A > 1500, B < 1500, symmetric amounts, both deviations < 350 */ }
[Fact] public void NewBots_AreProvisional_AndListedAfterEstablishedOnes() { /* a bot with deviation 90 is Place 1; a new bot has Place null and comes after */ }
[Fact] public void BbPer100_AndItsMargin_ComeFromAllHands() { /* NetPerHand [2, -2, 4, 0] at bb 2: 50 bb/100; margin from RunningStats */ }
[Fact] public void ARestart_KeepsTheRatings() { /* new Ratings over the folder: same Rating */ }
[Fact] public void BannedBots_AreLeftOut() { }
```

- [ ] **Step 2: Run** `dotnet test tests/GameServer.Tests --filter Ratings` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Game server: Glicko-2 ratings and the poker ranking"`

### Task 9: Pairing

**Files:**
- Create: `src/GameServer/Poker/Matchmaker.cs`
- Test: `tests/GameServer.Tests/Poker/MatchmakerTests.cs`

**Interfaces:**
- Consumes: Task 7 `BotAccount` (owner), Task 8 `Ratings.Of`.
- Produces: `sealed class Matchmaker(Func<string, (string Owner, double Rating)> lookup, TimeProvider clock,
  MatchmakerSettings settings)` with `void Enqueue(string bot)`, `void Remove(string bot)`, `IReadOnlyList<(string
  A, string B)> Pair()` (call regularly; removes the paired bots from the queue), `void Played(string a, string b)`;
  `sealed record MatchmakerSettings(double Window = 200, double WindowGrowthPerMinute = 100, int Cooldown = 3)`.
  Rules: two bots pair when neither is the other, their owners differ, their ratings are within the wider of their
  two windows (window + growth × minutes waited), and neither played the other in its last `Cooldown` matches
  (unless one of them has waited 10 minutes and no one else fits); the bot that has waited longest is paired first,
  with the closest rating.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void BotsOfOneOwner_AreNeverPaired() { /* A, B same owner, enqueued; advance clock 1 day; Pair() empty */ }
[Fact] public void ABot_IsNeverPairedWithItself() { /* Enqueue("A") twice; Pair() empty */ }
[Fact] public void FarRatings_PairOnceTheWindowHasGrown() { /* 1500 vs 1800: empty at 0 min, a pair after 1 minute (200 + 100 ≥ 300) */ }
[Fact] public void TheLongestWaiting_IsPairedFirst_WithTheClosest() { /* A waits longest at 1500; B 1550, C 1700: (A, B) */ }
[Fact] public void ARecentOpponent_WaitsForTheCooldown() { /* A and B played; with C available at a fitting rating A pairs with C; with only B, after 10 minutes, (A, B) */ }
```

- [ ] **Step 2: Run** `dotnet test tests/GameServer.Tests --filter Matchmaker` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Game server: pairing poker bots by rating, never by owner"`

### Task 10: Ranked and practice matches, and the match store

**Files:**
- Create: `src/GameServer/Poker/PokerLeague.cs` (runs it all: queue, matches, outcomes), `src/GameServer/Poker/
  MatchStore.cs`, `src/Poker/Arena/OwnerView.cs`
- Test: `tests/GameServer.Tests/Poker/LeagueTests.cs`, `tests/Poker.Tests/Arena/OwnerViewTests.cs`

**Interfaces:**
- Consumes: Task 4 `RelayHost`/`RemoteAgent`, Task 6 `AgentSeat`, Tasks 7–9; `ArenaTable`, `ArenaSettings`,
  `MatchResult.FromTable`, `MatchFile.Write/Read`, `HouseBots`.
- Produces: `sealed record LeagueSettings(int Hands = 1000, TimeSpan? TimeLimit = null /* 1 s */, TimeSpan?
  Allowance = null /* 0.5 s */, int ConcurrentMatches = 8)`; the ranked table is `new ArenaSettings(
  TableFormat.FixedStacks, Hands, DealSeed.New(), TimeLimit + Allowance, SmallBlind: 1, StackBigBlinds: 100)`.
- Produces: `sealed class PokerLeague(RelayHost relays, Registry registry, Ratings ratings, Matchmaker matchmaker,
  MatchStore store, LeagueSettings settings, TimeProvider clock) : IDisposable` with `void Connected(string bot)`
  (enqueue), `void Disconnected(string bot)` (dequeue; a running match goes on), `bool Practise(string bot, string
  houseBot)` (an unranked match against a house bot instead of the queue), `Task TickAsync()` (pairs and starts
  matches up to `ConcurrentMatches`, each on its own task), `IReadOnlyCollection<string> Playing`, `event
  Action<StoredMatch>? Finished`. A match: `agent.Restart()` for each remote bot, seats `new AgentSeat(agent,
  bot.Name)` (house bot seats as in the workshop), `ArenaTable.RunAsync`, then `MatchResult.FromTable`; a ranked
  match is stored and rated (`RankedOutcome` with score 1/0.5/0 by `SeatResult.Net`), a practice match is stored
  for its owner only and not rated; status to each runner (`RemoteAgent.Status`) at start ("playing X, ranked, 1000
  hands") and end (result, new rating). `Dispose` cancels running matches: they are neither stored nor rated.
- Produces: `sealed record StoredMatch(string Id, DateTimeOffset At, bool Ranked, string BotA, string BotB, int
  NetA, int NetB, long Hands)`; `sealed class MatchStore(PokerData data)` with `string Save(MatchResult match, bool
  ranked)` (id = time + random, `matches/<id>.json.gz`, an index `matches.json` of `StoredMatch`),
  `IReadOnlyList<StoredMatch> Of(string bot, int count)`, `MatchResult? Load(string id)`.
- Produces: `static class OwnerView` in `src/Poker` with `MatchResult For(MatchResult match, string bot)`: every
  hand's hole cards of seats other than `bot`'s are removed unless shown at showdown (as `HandRecord` keeps them);
  everything else unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheOwnersView_HidesFoldedOpponentCards_AndKeepsShownOnes() { /* a two-hand match: hand 1 opponent folds (cards gone), hand 2 showdown (cards kept); own cards always kept */ }
[Fact] public async Task TwoConnectedBots_PlayARankedMatch_ThatIsStoredAndRated()
{
    // league with Hands = 20; a RelayHost fed by in-memory socket pairs (a `SocketPair` helper in GameServer.Tests,
    // like Jentl's test helper) with AgentRelays running Poker.Tests.EchoBot in a mode that calls
    await RunUntil(() => store.Of("A", 10).Count == 1);
    ratings.Of("A").Matches.Should().Be(1); ratings.Of("B").Matches.Should().Be(1);
    store.Load(store.Of("A", 1)[0].Id)!.Hands.Should().HaveCount(20);
}
[Fact] public async Task EveryMatch_DealsFromAFreshSeed() { /* two matches between the same bots: different Settings.Seed, different first hole cards */ }
[Fact] public async Task ARunnerThatDropsAndComesBack_KeepsItsSeat_AndTheMatchIsRated()
{
    // during a 50-hand match, disconnect A's relay for a few hands, then reconnect with the same token
    // the match ends, A's result has Gone false, Late > 0, and both ratings moved
}
[Fact] public async Task APracticeMatch_IsNotRated() { }
[Fact] public async Task StoppingTheLeague_DropsTheRunningMatch() { /* Dispose mid-match: nothing stored, ratings unchanged; a new league over the same folder has the earlier matches */ }
```

- [ ] **Step 2: Run** `dotnet test tests/GameServer.Tests --filter League` and `dotnet test tests/Poker.Tests
  --filter OwnerView` — Expected: FAIL.
- [ ] **Step 3: Implement**; one bot plays one match at a time; a ranked match's two seats are never the same bot.
- [ ] **Step 4: Run** `dotnet test Games.slnx` — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Game server: ranked and practice poker matches, stored and rated"`

### Task 11: Endpoints, settings and the ranking page

**Files:**
- Modify: `src/GameServer/Program.cs` (settings, services, endpoints, a hosted service ticking the league),
  `src/GameServer/Dockerfile` (copies `src/Poker`, `src/Poker.Bots`; a volume for `/app/poker-data`)
- Create: `src/GameServer/Poker/PokerEndpoints.cs`, `src/GameServer/Poker/RankingPage.cs`, `src/GameServer/Poker/
  PokerJson.cs`
- Test: `tests/GameServer.Tests/Poker/PokerEndpointTests.cs` (`WebApplicationFactory<Program>` with a temporary
  data folder and `Hands = 10`)

**Interfaces:**
- Consumes: Tasks 4, 7–10.
- Produces, settings (option or environment variable): `poker-data`/`JENTL_POKER_DATA`, `poker-hands`,
  `poker-time-limit-ms`, `poker-allowance-ms`, `poker-concurrent`, `poker-bots-per-owner`, `poker-registrations-per-day`,
  `trust-proxy` (forwarded headers).
- Produces, endpoints (JSON, camelCase):
  - `WS /bots`: `RelayHost.RunAsync` (Connected → `PokerLeague.Connected`; a status line at welcome with the bot's
    rating).
  - `POST /api/poker/owners {name}` → `{ownerToken}`; `POST /api/poker/bots {name}` with header `X-Owner-Token` →
    `{name, botToken}`; `POST /api/poker/bots/{name}/token` (owner) → `{botToken}`; refusals as `400 {error}`.
  - `GET /api/poker/ranking` → `RankingRow[]`; `GET /api/poker/bots/{name}` → `{record, recent: StoredMatch[]}`;
    `GET /api/poker/mine` (owner) → `{bots: [{name, row, recent, connected}]}`; `GET /api/poker/matches/{id}`
    (owner of one of its bots) → the match file JSON from `OwnerView.For` (else 404); `POST
    /api/poker/bots/{name}/practice {houseBot}` (owner).
  - `GET /poker`: the ranking page (plain HTML, styled like `build/site`, the table of `Ranking()`; provisional
    bots marked; no script needed).
- `ban` from the command line: `dotnet run --project src/GameServer -- ban <bot-or-owner>` edits the data folder and
  exits.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task RegisteringAnOwnerAndABot_GivesTokens() { }
[Fact] public async Task TheRanking_ListsRatedBots_AndThePageShowsThem() { /* two bots via relays play a 10-hand match; /api/poker/ranking has both; /poker contains both names */ }
[Fact] public async Task AMatchDownload_NeedsItsOwner_AndHidesTheOpponentsFoldedCards() { /* other owner: 404; owner: 200, opponent folded hands have no cards */ }
[Fact] public async Task AFloodingRunner_DoesNotStopAnotherMatch() { /* bots C, D play; runner E floods /bots and is closed; C–D match is stored */ }
[Fact] public async Task TheUnoRooms_StillWork() { /* the existing /play test passes unchanged */ }
```

- [ ] **Step 2: Run** `dotnet test tests/GameServer.Tests --filter PokerEndpoint` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test tests/GameServer.Tests`; `docker build -f src/GameServer/Dockerfile .` if Docker
  is available (else note it) — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Game server: poker bot endpoints, settings and a ranking page"`

## Part 3: Runner, app and docs

### Task 12: `tools/PokerRunner`

**Files:**
- Create: `tools/PokerRunner/PokerRunner.csproj`, `tools/PokerRunner/Program.cs`
- Modify: `Games.slnx`; `apps/Poker/Desktop/Poker.Desktop.csproj` (built and copied next to the app, and published,
  like the bot host)
- Test: `tests/GameServer.Tests/Poker/RunnerEndToEndTests.cs`

**Interfaces:**
- Consumes: Task 3 `AgentRelay`; `BotCatalog` (to turn a `.bot.json` or DLL into an `AgentCommand`, the DLL through
  the bot host next to the runner).
- Produces: `PokerRunner <bot.json|bot.dll> --token <token> [--server wss://host/bots]` (default
  `ws://localhost:5080/bots`, or `JENTL_SERVER`'s host with `/bots`); prints each status line with a time; exit
  code 0 on Ctrl+C, 2 for a bad bot path, 3 when refused. `static class RunnerCommand` with `AgentCommand?
  Resolve(string path, string hostDll, out string? problem)` (unit-testable).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public async Task ARealRunner_WithARealBot_PlaysARankedMatch()
{
    // WebApplicationFactory server (Hands = 10); register two bots; start two AgentRelays as the runner does
    // (RunnerCommand.Resolve on bots/python/example.bot.json, skipped when python3 is missing, and on Poker.SampleBots' GoodBot DLL)
    // connect with factory.Server.CreateWebSocketClient(); wait until /api/poker/ranking shows both with 1 match
}
[Fact] public void Resolve_ExplainsABadPath() => RunnerCommand.Resolve("nope.txt", Host, out var p).Should().BeNull();
```

- [ ] **Step 2: Run** `dotnet test tests/GameServer.Tests --filter Runner` — Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS; `dotnet run --project tools/PokerRunner -- nope.txt --token x` exits 2.
- [ ] **Step 5: Commit** `git commit -m "Poker runner: put a bot online with one command"`

### Task 13: The workshop's Online view

**Files:**
- Create: `apps/Poker/Game/Workshop/OnlineScene.cs`, `apps/Poker/Game/Workshop/OnlineClient.cs`
- Modify: `apps/Poker/Game/Workshop/WorkshopScene.cs` (an "Online" item), `WorkshopContext.cs` (the runner
  command, `OnlineClient`), `shell/Assets/lang/nl.json`
- Test: `tests/GameServer.Tests/Poker/OnlineClientTests.cs` if `OnlineClient` can live in a framework-free place
  (then put it in `src/Poker/Online/OnlineClient.cs`, tested against the `WebApplicationFactory` server); the scene
  itself by screenshots.

**Interfaces:**
- Consumes: Task 11 endpoints, Task 12 runner, `ResultsScene`/`ReplayScene` (a downloaded match opens there).
- Produces: `sealed class OnlineClient(HttpClient http, Uri server)` with `Task<string> NewOwnerAsync(string name)`,
  `Task<(string Name, string Token)> NewBotAsync(string ownerToken, string name)`, `Task<string>
  NewBotTokenAsync(string ownerToken, string name)`, `Task<IReadOnlyList<RankingRowJson>> RankingAsync()`,
  `Task<MineJson> MineAsync(string ownerToken)`, `Task<MatchResult> MatchAsync(string ownerToken, string id)`, with
  its own JSON records for the answers (`RankingRowJson`, `MineJson`, … matching Task 11's shapes; the server's
  `RankingRow` lives in `src/GameServer` and is not referenced); errors
  as `OnlineProblem` exceptions with the server's text. The server is `GameContext.GameServer` with `http(s)`.
- The Online view: tabs Ranking (place, bot, owner, rating ± range, provisional mark, matches, W/D/L, bb/100 ±
  margin) and My bots (each bot: connected or not, its row, recent matches; Register a bot (name from the bots
  folder's list or typed), Show token (once, copy to clipboard), New token, Go online (starts the runner for that bot
  as a child process, stopped when the app quits, like `RunningBots`), Practice against a house bot, Open a match).
  The owner token is kept in `workshop/online.json` in the save folder; first use asks for an owner name and makes
  one. Offline or server down: the view says so and the rest of the workshop works.

- [ ] **Step 1: Write the failing tests** (`OnlineClient` against the test server: new owner, new bot, ranking,
  download of an owned match).
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** client and scene; Dutch texts for every new `T(...)`.
- [ ] **Step 4: Run** `dotnet test Games.slnx`; screenshots (scripted keys, `--save-dir`, a local server with two
  example bots online): the ranking, My bots, a downloaded match's results — Expected: PASS, screenshots readable.
- [ ] **Step 5: Commit** `git commit -m "Poker: an Online view in the bot workshop"`

### Task 14: Docs, site and packaging

**Files:**
- Create: `docs/poker-online.md` (putting a bot online, the runner, tokens, what the server checks, the ranking and
  how to read it, what an owner sees of a match, running a server: settings, Docker volume, TLS proxy, banning,
  backups of the data folder)
- Modify: `docs/poker-bots.md` (a pointer to it), `README.md` (Poker section and the game server section),
  `build/site/index.html` (a link to the server's `/poker` page when a server address is configured, else none —
  the builder's call how), `bots/csharp` and `bots/python` READMEs if they exist (a line on going online)

- [ ] **Step 1: Run** `dotnet build Games.slnx` — Expected: PASS (docs only otherwise).
- [ ] **Step 2: Commit** `git commit -m "Poker online: docs"`

### Task 15: Final check

- [ ] **Step 1: Run** in `../jentl`: `dotnet test`, `dotnet format whitespace --verify-no-changes`; in games-cs:
  `dotnet test Games.slnx`, `dotnet format whitespace Games.slnx --verify-no-changes`, `dotnet build apps/Poker/Web`,
  `dotnet build apps/CardTable/Web` — Expected: all green.
- [ ] **Step 2:** Run a local server, put `bots/python/example.bot.json` and MyBot online with the runner, watch a
  ranked match finish, see both in the Online view and on `/poker`, open the match in the workshop — Expected: as
  described.
- [ ] **Step 3:** Hand over: what was built, screenshots, the commands; the owner pushes and tags `v0.6.3`.
