# Paddock: design

A pixel-art car collecting and racing game in the spirit of the mobile game Top Drives, as a new app in games-cs
(desktop with MonoGame, browser with KNI), on the Jentl engine.

## Why

The owner plays Top Drives for about half an hour at a time. The fun, in their words: collecting the cars, then
seeing them race and hoping they win; and events, through which you can sometimes collect new or valuable cars,
which is exciting. Paddock should deliver exactly that: a growing garage, races you set up but don't drive, and
events with entry rules and rare prize cars, played until the tickets run out and you come back later.

## Goals

- Collect cars: about **100 well-made invented cars** to start, each with a rarity class and stats.
- Build a **hand of five** under an **RQ limit** and race it against an opponent's hand across five events.
- Races are **decided by the stats, deterministically**, and then **played out on screen** with pixel cars.
- **Events** with entry rules, a ladder of races and a rare prize car, limited by **tickets** that recharge.
- **Upgrades and fusing**, as in Top Drives: cash upgrades until a wall, then fuse another car in to go on.
- A **campaign** with prize cards, and **packs**, as further ways to get cars.

## Non-goals (for now)

- Real cars, real makers or anything resembling their brands or designs (the site is public; everything is invented).
- Real money, ads, gold or any premium currency.
- Online play, clubs, ladders against other people, a server of any kind.
- Driving the cars yourself. The player builds hands and upgrades; the race is a show.
- Android or iOS apps; desktop and browser only, as for the other games.

## Anti-goals (would make it a failure even if it works)

- Upgrading missing or shallow. The owner named this explicitly: upgrading with cash up to a point where another
  car has to be fused in to reach the next level is part of the game.
- Results that feel random or that don't match what is shown. A result is computed from the stats and the playback
  must agree with it.
- A collection of near-identical cars. 100 cars that each look and feel distinct beats more of them.

## Decided with the owner

### Name

**Paddock.** App folder `apps/Paddock`, like the other apps; a new folder `paddock/` on the Pages site with a card
on the landing page.

### Cars

- About 100 invented cars from invented makers, across eras (roughly 1950s to today) and body types (hatchback,
  saloon, estate, coupé, roadster, muscle car, SUV, pickup, rally car, supercar, hypercar, van, ...).
- Each car is a card with a **rarity class F, E, D, C, B, A, S** (from its RQ) and an **RQ** (strength rating).
- Stats: top speed, 0-100 km/h, handling, weight, drive (FWD, RWD, 4WD), tyres (slick, performance, standard,
  all-surface, off-road), ground clearance (low, mid, high). Plus year, maker, body type, country (for entry rules).
- **Pixel art drawn in code**, like Mossbrook's tileset (`tools/RpgArt`): body shapes with their own proportions,
  colours and era details. Each car needs a top-down sprite (circuits), a side view (drag races and the card).

### Card look

The **full card** (as in Top Drives), chosen from mockups: frame and header in the rarity colour, class letter and RQ
at the top, the car's side-view picture, name and maker with year, then top speed, 0-100, handling and weight, and
tags for drive, tyres and clearance. Also shows the car's tune (e.g. "3.3.2") once upgraded. Used in the garage,
when picking a hand, and before a race. On a phone a hand of five scrolls or shrinks to fit (builder's call).

### Races

Words: a **match** is five **rounds**; a round is one race on one track. An **event** (below) is the ticketed ladder
of matches.

- A **match**: the player's hand of five against an opponent's five, one pairing per round, five rounds.
- **Like Top Drives** (owner confirmed): before the match the player sees the five tracks with their conditions
  **and the opponent's five cars**, then **orders their own five cars**, which decides which car runs which track.
  Matching the right car to the right track is the player's skill.
- The match is won by **winning more rounds**; if even, the **larger total winning margin** decides. A round can't
  draw: equal times go to the car with the better deciding stat (builder's call how exactly).
- Each round is a **track** with a **condition** (dry, wet, dirt, gravel, snow, ice, sand). Tracks include drag
  strips (1/4 mile, 1/2 mile), circuits, twisty roads, city streets, hill climbs, off-road. The track's shape
  decides which stats matter (straights reward top speed, corners handling, slow corners acceleration); the
  condition decides which tyres, drive and clearance work, so a weaker car with the right tyres can win.
- The result of each event is **computed up front from the stats, the upgrades, the track and the condition, and is
  the same every time**. The playback is driven by that computation, so what you see agrees with the result.
- After each round, show the winner and the two or three stats that decided it.
- **Each round** plays for roughly 10-20 seconds (drag races 6-8 s): simulated time is compressed into playback
  time, every round is animated. A **skip button** (owner asked for it) jumps to the round's result; skipping the
  rest of the match is the builder's call.

### What a race looks like

Chosen from animated mockups:

- **Circuits and other tracks: top-down, close up, following the player's car**, cropping the circuit. A mini-map in
  a corner shows the whole track and both cars; the gap ("AHEAD +0.4s") and the track and condition at the top;
  weather on screen (rain, puddles, spray).
- **Drag races: side view**, both cars in lanes, distance markers, finish line, time.
- **Race camera setting**: *Fixed* (north-up; the car turns) or *Turn with car* (the view rotates so the road runs
  up the screen, the car low on the screen, the camera easing into corners). The owner wants this as an option.
  Default *Fixed*, because pixel art stays crisp without rotation (my call; Jentl's `Camera2D` already rotates).

### Events and the limit

- Two or three events run at once, changing every few days on a **fixed calendar built into the game** (derived from
  the date), so desktop and browser show the same events on the same day. No server.
- Each event has **entry rules** (era, tyres, body type, country, maker, maximum RQ, ...) and is a **ladder of about
  10-15 races** against stronger opponents, with rewards at milestones and a **rare prize car** at the end that is
  hard to get otherwise.
- **The owner wants a limit**: each event has its own **tickets**, about 5, one back every 30 minutes; a race costs a
  ticket whether won or lost. Out of tickets means done with that event for now. Recharge runs on the wall clock,
  also while the game is closed.
- The **campaign** has no limit but pays only cash and its own prize cards, so events stay the exciting part.

### Upgrades and fusing

- Every car has **three stages**. Within a stage, cash buys upgrades in three areas: **engine** (top speed,
  acceleration), **weight** (acceleration, handling), **chassis** (handling, grip). Each upgrade is dearer than the
  last.
- **Each stage has a budget of 8 upgrade points, at most 3 per area** (as in Top Drives; my call, settling the
  reviewer's question). So 3.3.2, 3.2.3 and 2.3.3 are all a "full" stage, and tunes differ even at the cap.
- When the stage's 8 points are spent, the car hits a wall: to reach the next stage, **fuse** another car of the same
  rarity class into it; that car is consumed. This is what makes duplicates and spare pack cars valuable.
- The **tune** is cumulative across stages and carries over when fusing: a car shows e.g. "3.3.2" after stage 1 and
  "9.9.6" fully upgraded. Two copies of a car can be tuned for different tracks. A tune can be reset and paid for
  again (my guess, accepted); a reset keeps the stage reached.
- **RQ never changes with upgrades**: an upgraded car fits the same hand but races better.

### Economy and progress

- **One currency: cash**, earned from races; spent on upgrades, fuses' costs if any, and packs.
- **Packs** bought with cash or won as rewards give random cars, weighted by rarity.
- Player level from XP; the **RQ limit for hands grows with level**, opening up better cars over time.
- Saved on the device like the other games (`SaveStore` on desktop, `localStorage` in the browser).

## Mockups

The mockups the owner chose from are in `paddock-mockups/` next to this document (`car-card.html`: style A, the full
card; `race-view-v2.html`: close-up top-down circuit and side-view drag race; `race-view-v3.html`: fixed against
turning camera). They are HTML fragments made for the brainstorming viewer; open them in a browser to see the
animations (styling is partly missing outside the viewer).

## Constraints

- Lives in games-cs as `apps/Paddock` with `Game/`, `Assets/`, `Desktop/` and `Web/`, using `shell/` like the other
  apps (title screen, options, audio, translations, controls). English and Dutch, like the rest.
- **Engine rule from the owner: anything Jentl lacks that Paddock needs goes into the Jentl engine (the jentl-cs
  repository), not into Paddock.** That means a new Jentl version (packages) and games-cs moving to it. Candidates
  the builder will likely meet (to be judged when building): following a path or spline with speed and heading,
  wall-clock recharging counters (Jentl's `Scheduler` is frame-based), weighted random draws for packs, a
  track/race camera helper, pixel-art drawing helpers shared with `tools/RpgArt`.
- Game rules (cars, races, upgrades, events, economy) framework-free in a library under `src/` with xUnit tests, as
  for the other games. Deterministic: same inputs, same results, also between desktop and browser.
- Works with mouse, touch, keyboard and gamepad, like the other apps; phone-sized browser windows included.
- Build rules of the repo: warnings as errors, analyzers, BOM and LF, `dotnet format whitespace`.

## Left to the builder

- The race formula itself: how stats, upgrades, track shape and condition turn into times, within the decisions
  above. It should be explainable (top 2-3 deciding stats) and reward matching cars to tracks.
- The exact roster of 100 cars, makers, names, stats and their balance across classes; the art generator.
- Track list and layouts (how many, how they are defined), the campaign's shape (countries, stages, prize cards),
  event calendar contents and entry rules, opponent hands per difficulty.
- Numbers: upgrade costs and gains, payouts, pack prices and odds, XP curve and RQ limits, ticket counts and timing.
- Screen layouts beyond the card and race views (garage, hand builder, event screen, upgrade screen), music and
  sounds, and how much of Top Drives' depth (e.g. tyre wear, ground clearance penalties) the formula models.
- Order of work and what ships first.
