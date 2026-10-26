# Poker bots online: the ranking

Put your poker bot on a game server and it plays ranked heads-up matches against other people's bots, all day,
without you watching. The server keeps a ranking. This page covers putting a bot online, what the server checks,
how to read the ranking, and, for whoever runs a server, the settings, Docker, a TLS proxy, backups and banning.
Writing the bot itself is in [poker-bots.md](poker-bots.md). People playing Hold'em together on the same server is in
[Playing online with friends](#playing-online-with-friends).

A bot runs on its owner's computer, never on the server. A small program, the **poker runner**, connects to the server
and relays the server's messages to your bot and its answers back: the server runs nothing of yours.

## Putting a bot online

### From the workshop

**Poker > Bot workshop > Online** has two tabs (Tab switches between them):

- **Ranking**: place, bot, owner, rating with its range, matches, won/drawn/lost and bb/100 with its margin. A `*` marks
  a provisional bot (see below).
- **My bots**: your bots with whether their runner is connected, their line of the ranking and their recent matches.

The first time, it asks for a name as an owner and makes the owner. Then:

1. **Register a bot**: choose a bot from your bots folder (or type a name). A bot name is 3 to 24 characters:
   letters, digits, spaces, `_`, `.` and `-`, no space at either end. Names are unique across the league, and
   a name is the bot's identity in the ranking.
2. Choose the bot, then **Go online**. The app starts the poker runner for it, a child process that plays matches until
   you choose **Stop the runner** or quit the app. Its status shows under the bot.
3. **Show token** shows the bot's token and copies it to the clipboard (there is no clipboard in some places: then copy
   it from the screen). **New token** makes a new one and the old one stops working; it is copied too. A runner still
   connected with the old token, here or anywhere else, is cut off at once (it shows "refused: token replaced"). If the
   bot is running here, its runner is restarted with the new token.
4. **Practice against a house bot** plays one unranked match against one of the house bots, to see that your bot works
   online before it meets others. A practice match is not rated and is yours alone to see.
5. **Open a match** downloads one of the bot's recent matches into the results screen, where you can step through
   the hands.

The app keeps your owner token and your bots' tokens in `workshop/online.json` in the Poker save folder. If that file
cannot be read it is moved aside (the view says where) and you start without tokens: make a new owner, or put the old
file back. If the server does not know your owner token (it lost its data, say, or it is another server than the one you
registered on), the view says so and keeps the token: **Try again** once the server has its data back. **Make a new
owner** there replaces it, but the old owner and its bots' tokens stay in `online.json` as a previous owner, and
**Use the owner from before** takes it back. A banned owner is told so. Everything in the Online view needs the server; when it cannot be reached, the view says so and the rest of
the workshop works as before. Which server is used is set the same way as for Uno (`--server`, `JENTL_SERVER`).

### From the command line, on any computer

```
dotnet run --project tools/PokerRunner -- <bot.json|bot.dll> --token <token> [--server wss://host/bots] [--bot <TypeName>]
```

- `<bot.json|bot.dll>` is a `*.bot.json` file (any language) or a C# bot DLL; a DLL with more than one bot takes
  `--bot <TypeName>`.
- `--token` is the bot's token (from Show token, or from registering).
- `--server` is the server's runner address, `wss://host/bots`. Without it the runner uses `JENTL_SERVER` (the game
  server's address, `/play` replaced by `/bots`), and without that, `ws://localhost:5080/bots`.

The runner prints what it does and reconnects on its own when the connection drops. Ctrl+C stops it.
Exit codes: **0** stopped with Ctrl+C, **1** wrong usage, **2** the path is no bot (or the bot host is missing),
**3** the server refused the token.

**Keep tokens to yourself.** The token is passed to the runner as `--token`, so any other user of the same computer can
see it in the process list. Whoever has a bot's token can play as that bot. If one leaks, choose **New token**: a runner
connected with the old one is cut off and cannot connect again.

### Registering without the app

The app does this for you; the endpoints are plain JSON over HTTP. An owner is known by the `X-Owner-Token` header.

| Request | Does |
| --- | --- |
| `POST /api/poker/owners` `{"name":"..."}` | makes an owner; answers `{"ownerToken":"..."}` |
| `POST /api/poker/bots` `{"name":"..."}` | makes a bot for the owner; answers `{"name":"...","botToken":"..."}` |
| `POST /api/poker/bots/{name}/token` | gives the bot a new token (`{"botToken":"..."}`); the old one stops working |
| `POST /api/poker/bots/{name}/practice` `{"houseBot":"..."}` | starts a practice match; answers 202 |
| `GET /api/poker/mine` | the owner's bots, their ranking lines, recent matches and whether they are connected |
| `GET /api/poker/matches/{id}` | one match, as its owner may see it |
| `GET /api/poker/bots/{name}` | a bot's rating and its recent ranked matches (public); 404 for a banned bot, as the ranking leaves it out |
| `GET /api/poker/ranking` | the ranking (public); the same as the page at `/poker` |

A refusal is status 400 with `{"error":"..."}`: "not a name", "name taken", "unknown owner", "unknown bot",
"too many registrations today", "N bots already" and the like; a ban is 403, `"banned"` for a banned owner (and so
all of their bots) and `"bot banned"` for a banned bot whose owner is not. `/api/poker/mine` answers 401
(`"unknown owner"`) for an owner token the server does not know, and 403 (`"banned"`) for a banned owner. A match
download answers 404 for anything that is not yours, whether the match exists or not. A request body may be 16 KB at
most; a longer one is refused with 413.

## What the server does with your bot

- A bot waits in a queue while its runner is connected. The server pairs bots that are close in rating (the window grows
  the longer a bot waits, so nobody waits for ever), never two bots of the same owner, and not the same two again
  right after each other.
- A ranked match is heads-up, 1,000 hands by default, 100 big blinds each, blinds 1/2, with **fresh deals** from a
  secret seed: no deal is ever repeated. After a match the bot goes back in the queue.
- A bot plays one match at a time. A bot that disconnects leaves the queue.

### The rules of a match

The rules are those of [poker-bots.md](poker-bots.md#rules), with the clock of the server: a bot gets the time limit
(1 second) plus an allowance for the network (0.5 seconds) for each decision.

- **A late, malformed or illegal answer is replaced** by a check if checking is free, else a fold, and it is counted.
- **Answering "call" when there is nothing to call is a check**, and legal. Answering "check" when there is a bet
  to call is illegal, as is a raise outside the range: look at `legal` in the decision, not at your idea of the pot.
- **Hello.** A bot gets 10 seconds to say hello at the start of each match, ample for a C# bot started
  through the bot host and starting cold; still, keep start-up light (no heavy tables built on start, nothing loaded from the
  network).
- **Disconnected.** While a bot's runner is disconnected, its seat is folded, and those folds show as **illegal** in the
  results, not as late. They do not count towards the "3 late in a row" stop. A runner that reconnects takes its seat
  back, and the match counts.
- A bot that does not answer 3 decisions in a row is stopped for that match, as in the workshop.
- **A runner that drops while its bot is still starting** keeps that bot process when it reconnects (the server sends
  the start again; the runner sees it is for the match it already has), so a slow start and a dropped connection do not
  add up to a missed hello.

### Online limits

The server limits what a runner sends, and the runner keeps to those limits itself, so that a bot that prints too much
loses lines, not its connection:

- **Line length.** A message from the runner may be at most **4,096 bytes**: one line of your bot's, in the relay's
  JSON (quotes and backslashes in it count twice). Real answers are well under 100 bytes. A longer line, a big debug
  print say, is dropped by the runner, which says so once ("dropped a line from the bot: longer than 4096 bytes").
  Offline, in the workshop, a line may be 1 MiB.
- **Extra lines.** Every message the server sends lets the bot send one line back. Besides those, the runner sends at
  most **100 lines a second** (in bursts of up to 100); the server would cut a runner off above 200. A bot that prints
  a debug line for every message it gets therefore loses some of its lines in a fast match, which can be an answer:
  that decision then counts as late. The runner says so once ("dropped lines from the bot: more than 100 a second
  besides its answers").
- Lines the bot writes while the runner is disconnected wait (the last 1,024) and are sent within these limits once it
  is back.

Put debug output on stderr: it stays on your computer and costs nothing online.

## Reading the ranking

The ranking is the page at `/poker` of the server (for example `https://example.org/poker`), and JSON at
`/api/poker/ranking`. In the app it is the Ranking tab.

- The **rating** is Glicko-2, starting at 1500. A match counts as one rating period for both bots.
- The **range** (the "±") says how unsure the rating still is: the true strength is probably within it. A new bot has a wide
  range that narrows with every match.
- A **provisional** bot, one whose rating deviation is still above 100 (a range above 200), is marked and listed below
  the established ones, without a place.
- **bb/100** is the big blinds won per 100 hands over all its ranked hands, with its 95% margin.
- Luck is real in poker. Deals are never repeated in ranked matches, so luck evens out only over many hands and many
  matches: do not read much into a few results. To measure a bot precisely at home, use the workshop's **duplicate batch**,
  which plays the same deals from both seats.

## What an owner sees of a match

Download (Open a match) gives the owner, for a match of one of their bots: every action, the board, their own bot's hole
cards, and the cards shown at a showdown. The opponent's other cards and the deal seeds are never sent. An
opponent's problem text shows as "had a problem". Public pages show ranked matches only; practice matches are for their
owner.

## Running a server

```
dotnet run --project src/GameServer
```

It listens on `http://0.0.0.0:5080` (set `ASPNETCORE_URLS` or `--urls`) and serves Uno rooms at `/play`, runners at
`/bots`, the API and the page at `/poker`. Each setting is an option (`--poker-hands 500`) or an environment variable:

| Option | Variable | Default | Means |
| --- | --- | --- | --- |
| `poker-data` | `JENTL_POKER_DATA` | `poker-data` | the data folder |
| `poker-hands` | `JENTL_POKER_HANDS` | 1000 | hands in a ranked match |
| `poker-time-limit-ms` | `JENTL_POKER_TIME_LIMIT_MS` | 1000 | a bot's time for a decision |
| `poker-allowance-ms` | `JENTL_POKER_ALLOWANCE_MS` | 500 | added to it for the network |
| `poker-concurrent` | `JENTL_POKER_CONCURRENT` | 8 | matches running at once |
| `poker-bots-per-owner` | `JENTL_POKER_BOTS_PER_OWNER` | 5 | bots an owner may register |
| `poker-registrations-per-day` | `JENTL_POKER_REGISTRATIONS_PER_DAY` | 10 | owners and bots registered from one address in a day |
| `trust-proxy` | `JENTL_TRUST_PROXY` | false | true behind a proxy: the client's address is read from `X-Forwarded-For` |

A server started inside the repository creates `poker-data/` there (it is in `.gitignore`).

**The data folder** holds `owners.json`, `bots.json`, `ratings.json`, `matches.json` (the index of the matches) and a `matches/` folder with
one file per match. It holds only hashes of the tokens, but treat it as private, and **back it up** (copy it while the server is
stopped, or at least copy it as a whole). If `owners.json` or `bots.json` (or another file) cannot be read at start-up
it is moved to `name.broken-<time>` next to it, the server starts empty for that file, and it logs a warning
naming the file: restore from it or from a backup before more is written.

**Docker.** Build from the repository root after `build/jentl.sh`:

```
docker build -f src/GameServer/Dockerfile -t game-server .
docker run -p 5080:5080 -v poker-data:/app/poker-data game-server
```

The image keeps the data in `/app/poker-data` (a volume); without a volume it is lost with the container.

**Behind a TLS proxy.** Runners and the app connect with `wss://` (the proxy terminates TLS and forwards WebSockets to
port 5080). Set `JENTL_TRUST_PROXY=true`, so that the registration limit sees the client's address from `X-Forwarded-For`
and not the proxy's. Only do this when the server is reachable through the proxy alone: with the setting on, anyone
who reaches the server directly can claim any address.

### Banning

```
dotnet run --project src/GameServer -- ban <bot-or-owner>
```

A bot is named by its name; an owner by its id, which is in `owners.json` in the data folder. Banning an owner bans all
its bots. A banned bot is not matched any more, cannot get a new token, and its runner is refused when it connects (a
ban made while the server runs, from its own code, cuts the runner off at once with "refused: banned"). Use `--poker-data`
(or `JENTL_POKER_DATA`) if the data is not in `poker-data`. **Stop the server first**: a running server keeps its own
copy of the owners and bots and would overwrite the ban when it next saves.

## Playing online with friends

The same server hosts Hold'em between people: **Poker > Texas Hold'em > Play online** makes a room for 2 to 6 at the
server's `/play` address (the one Uno's rooms use), shows its four-digit code to share, and house bots take the empty
seats. Nothing needs switching on: every server that runs the ranking hosts the rooms too. The server deals each game
from a new secret seed and sends each device only its own hole cards, plus the cards shown at a showdown.

- **On a home network.** Run the server on one machine (`dotnet run --project src/GameServer`, or the Docker image)
  and start the desktop app on each computer with `--server ws://HOST:5080/play`, or with `JENTL_SERVER` set to that
  address, where HOST is the server machine's name or address.
- **Through Tailscale.** On the server machine, `tailscale serve --bg 5080` puts the server on your tailnet over HTTPS
  (WebSockets included), at `https://HOST.TAILNET.ts.net`; the apps then use `--server wss://HOST.TAILNET.ts.net/play`.
  Every device signed in to the tailnet can play, wherever it is.
- **In the browser.** A page served over HTTPS may only open `wss://` connections, so the browser version needs the
  Tailscale address (or a server behind a TLS proxy, see below): add `?server=wss://HOST.TAILNET.ts.net/play` to the
  page address.

## Hosting it

`deploy/` holds everything to host the server on a machine of your own: the game server and [Caddy](https://caddyserver.com)
(which fetches and renews the TLS certificate and forwards everything, WebSockets included) in one Docker Compose file.
Only Caddy is reachable from outside (ports 80 and 443); the game server's port 5080 stays on the compose network, which is
what `JENTL_TRUST_PROXY=true` (set by the compose file) requires.

1. **A host.** Any VPS with Docker and the Compose plugin, and ports 80 and 443 open (also 443/udp for HTTP/3).
   Step 4 also needs the **.NET SDK 10 and git** on the host: `build/jentl.sh` packs the Jentl engine there (it clones
   `coenvk/jentl-cs` at the version in `dependencies.props`, unless a checkout sits in `../jentl`), and the image build
   copies the result (`.jentl-packages`). On a host with Docker alone, run `bash build/jentl.sh` on another machine and
   copy the `.jentl-packages` folder into the checkout on the host.
2. **DNS.** Point an `A` record (and `AAAA` if the host has IPv6) of your domain at the host's address. Caddy can only get a
   certificate once that resolves.
3. **Settings.** On the host, in a checkout of this repository, copy `deploy/.env.example` to `deploy/.env` and set
   `DOMAIN` in it. The poker settings in it are commented out with their defaults; see the table above. `deploy/.env` is
   in `.gitignore`.
4. **Start it.** The image is built from the repository, after the engine has been packed:

   ```
   bash build/jentl.sh
   docker compose -f deploy/docker-compose.yml up -d --build
   ```

5. **Check it.** Open `https://DOMAIN/poker`: the ranking page. `docker compose -f deploy/docker-compose.yml logs -f` shows
   the logs (a certificate that cannot be obtained shows up in Caddy's).
6. **Connect.** A runner uses `--server wss://DOMAIN/bots`. The workshop app and the Uno client use the server's address,
   `--server wss://DOMAIN/play` or `JENTL_SERVER=wss://DOMAIN/play`.

**Where the data is.** The poker data and `scores.json` are in the named volume `games_poker-data` (and Caddy's
certificates in `games_caddy-data`); they survive `down` and rebuilds, but not `down -v`.

**Backups.** `bash deploy/backup.sh [folder]` writes `games-data-<date>.tar.gz` (default folder `backups/`) with the
whole data volume, mounted read-only. Run it from cron, and copy the files off the host. To restore, stop the server, unpack, start it:

```
docker compose -f deploy/docker-compose.yml stop game-server
docker run --rm -v games_poker-data:/data -v "$PWD/backups":/backup alpine tar xzf /backup/<file> -C /data
docker compose -f deploy/docker-compose.yml start game-server
```

**Updating.** `git pull`, `bash build/jentl.sh`, then `docker compose -f deploy/docker-compose.yml up -d --build` again.
The data stays.

**Banning on a hosted server.** The server must be stopped first (see Banning). The container's entrypoint is the game
server, so the arguments after the service name are its own:

```
docker compose -f deploy/docker-compose.yml stop game-server
docker compose -f deploy/docker-compose.yml run --rm --no-deps game-server ban <bot-or-owner>
docker compose -f deploy/docker-compose.yml start game-server
```

The one-off container has the same environment and volume, so it finds the data folder by itself.

### Switching on the link on the Pages site

`build/site/index.html` has a hidden link to the ranking. Once the server is up, set the `href` of `#poker-ranking` to
`https://DOMAIN/poker`, remove the `hidden` attribute from the `<p class="league">` around it, and push; the Pages
workflow publishes it.
