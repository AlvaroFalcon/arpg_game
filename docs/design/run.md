# The Run (survivor layer)

> **Superseded as the core loop (2026-10-11)** by the Mirror Shard endgame —
> see [endgame.md](endgame.md). Everything below still describes the code as it
> is; whether the survivor mode survives as an optional "Survival Rift" is an open
> question there.

Status: **implemented** (first pass). Owner decision this builds on: *"a game like
Vampire Survivors but with Path of Exile-style character progression: enter an
area, enemies and bosses spawn, gain experience, level up, pick upgrades that change
your build, and earn gear at the end of runs."*

## What it is

A run is one trip into the **Ashen Arena**: about seven and a half minutes of
escalating waves, a mid-run boss, and a final boss. Inside a run the character
levels from 1 to ~20 and builds itself from level-up cards
([progression.md](progression.md)). The run build is thrown away at the end; the
**gear** the run produced is kept ([menus.md](menus.md) → Profile). That split is
the whole design: *survivor-style power curve inside a run, ARPG-style persistent
character across runs.*

Flow: Main menu → Hideout → portal → **start pick** (a skill card: the class's
skills for a skill-less character, otherwise a new class skill or a level for one
it brought — progression.md -> "The start pick") → survive → boss → results
(choose one of three items, if the run earned one) → Hideout.

## The arena

- 80×60 tiles of 32 px (2560×1920 world px), built at runtime from the EasyPixel
  tileset (`assets/tiles/ashen_arena`): stone floor, brick wall rim, lava moat
  outside, a dim rune circle at the centre where the final boss appears.
- **No obstacles.** Enemies chase in straight lines with no pathfinding; a pillar
  in a field of 150 slimes produces stuck clumps that read as bugs, and kiting in
  open space is the genre's core skill. Revisit if enemies ever get real pathing.
- Walls are `StaticBody2D` on the World layer: they stop the player and enemies,
  and they eat projectiles.
- The camera is limited to the arena plus a few tiles of lava, so the player
  always knows where the edge is.

## The timeline — `RunDirector`

| Time | What happens |
|---|---|
| 0:00 | Slimes only. ~1.1 spawns/s. Danger 1. |
| 0:25 | Bats join (fast, weaving). |
| 1:10 | Bone casters join (ranged). |
| 1:55 | Ember brutes join (telegraphed charge). |
| 2:30 | Volatile imps join (run in and explode). |
| 1:30, then every 45 s → 14 s | An announced **elite** (bigger, tougher, glows gold, 6× XP, can drop gear). The gap shrinks by 4 s a minute. |
| from 2:00 | Any ordinary spawn may come as an elite: 0.5 % per minute past two, up to 4 %. |
| every 60 s → 30 s | A **swarm**: a ring of fast enemies closing in from all sides. Every 60 s at first, then more often (`60 − 5·m` s, never under 30 s); 16 enemies plus one every 20 s; from 4:00 a second, wider ring of another kind follows ("two waves"). |
| 3:00 | **The Bone Herald** (mid boss). Normal spawns drop to 30 % while it lives. |
| 5:30 | **The Bone Herald returns** (if the first one is dead). |
| 7:00 | **The Ember Colossus**. All normal enemies burn away (no XP), gems on the floor fly to the player, and the fight is one-on-one plus what the boss summons. |
| Colossus dies | **Victory** — and a portal home opens where it fell. The run does not end: see *The endless phase*. |

### The danger level (2026-10-14)

Owner feedback: the Rift got easy in the second half — enemy life grew linearly
while a build grows multiplicatively, elites and bosses were rare, and the floor
felt sparse. Everything now hangs off one clock, the **danger level** (1 for the
first minute, +1 every minute), shown on the HUD as the right-hand bar:

- **Enemy life** `×(1 + 0.38·m)·1.06^m` and **damage** `×(1 + 0.13·m)·1.03^m` at
  spawn time (m = minutes). The compounding factor is what keeps a finished build
  from outscaling the endless phase: ×5.5 life at 7:00, ×16 at 15:00. Life still
  grows faster than damage, because the player's damage scales with their build and
  their life mostly does not. Bosses scale with the square root of the life
  multiplier and not at all on damage ([bosses.md](bosses.md)).
- **Spawn rate** `1.1 + 1.35·m + 0.09·m²` per second, **alive cap** `115 + 24·m + 1.4·m²` (at most 400). The quadratic terms were added on 2026-10-17 (owner: "more enemy density as time goes on, and more swarms"): about the same in the first minutes (5.9/s vs 5.2/s at three minutes), noticeably thicker later (12.4/s vs 9.2/s at six, ~24/s at ten in the endless phase). The cap of 400 keeps the crowd within the frame budget (performance.md).
- **Elites** as in the table: more often, and mixed into ordinary spawns.
- **Monster level** `1 + 2·m` (persistent-character experience per kill).

Enemies spawn just outside the camera so they walk in instead of popping in on top
of the player.

### The director (2026-10-14)

From the same review of spawning techniques (endgame.md -> "Spawning"), the Rift got a
small **director** in the spirit of Left 4 Dead's, on top of the danger curve:

- **Intensity waves**: smooth 1D simplex noise over time (one octave, period ≈ a
  minute), smoothstepped and mapped to ×0.55–1.5. The pressure surges and eases on its
  own — about every half minute to a minute — instead of a flat ramp or a visible
  timer. Crossing ×1.3 announces **"A surge rises"** and the danger gauge reads
  "Surge!".
- **Stress**: a player under 60 % life gets ×0.85, under 35 % ×0.65 — a breather,
  never a stop.
- **A budget**: the spawn rate (still the danger curve's enemies per second, ×1.3 to
  turn it into points) fills a budget that is spent by **cost** — slime and bat 1,
  imp 1.5, caster 2, brute 3. A heavy pick is saved up for rather than skipped.
- **Edge spawns**: a random point on the edges of the actual view rectangle plus a
  margin (sides weighted by length), clamped into the arena and re-rolled if the
  clamp pulled it on screen — enemies never pop in where the player can see.
- **The leash**: every half second, an ordinary enemy more than a full view diagonal
  behind is moved back to the view's edge, so the horde follows the player instead
  of trailing off. Bosses never move.

Swarms, elites and bosses keep their own timers. Autoplay bot after the change (3
random-skill runs): died at 4:47, won at 8:07 and 7:50 — within the spread of the
previous pass (5:03, a 7:52 win, 4:16), with more kills: the lulls let the build catch
up, the surges push back.

### The endless phase

Killing the Colossus used to end the run. Now it asks first (see "Cash-out offer and the
infinite Rift" below); keeping going opens a **portal home** (step on it to be asked once
more, then leave with a victory) and the waves come back: the danger clock keeps running, a
boss arrives every two minutes (Herald and Colossus alternate, without clearing the
arena), and every minute adds to the end-of-run reward. Falling in the endless phase
still counts as a victory — the player already won; what they risk by staying is the
time they would have spent, not the win. The autoplay bot takes the portal at once
so balance numbers stay comparable with earlier passes.

### Cash-out offer and the infinite Rift **[IMPLEMENTED 2026-10-18]**

Owner request (translated): *"Make the Survival Rift infinite. When you reach the
current cap (the Colossus kill) the game proposes a reward: take it and leave, or
decline and continue, so the reward keeps improving. Then you can leave whenever you
want, and the reward is shown at that moment."*

**Findings** (`review-rift-endless.png`, `review-rift-results.png`):

- The endless phase already exists, but the only sign of it is the gauge line
  "Portal open · stay for more rewards" and a portal somewhere in the arena. The
  reward is invisible until the results screen: you can't tell what staying is
  worth, or what you'd walk away with now.
- The choice is never asked. The Colossus dies, a 3 s banner says "A portal home
  opens — or stay for greater rewards", and the waves come back at once.
- Leaving is by walking into the portal (easy to do by accident while kiting
  around it) or Esc → "Abandon run", whose name sounds like a loss although after the
  Colossus it pays a victory.
- The results screen at 16:12 is already full (five cards, text clipped at the
  bottom of each card), so any new reward panel must be compact.

#### The flow

1. **The Colossus dies.** As today: "Victory" banner, `victory` sound, gems fly to
   the player. New: the director **holds** (no spawns, no endless boss timer) for
   1.2 s, then the **cash-out panel** opens. Solo: the tree pauses, as for level-up
   cards. The danger clock is paused with the tree, so deciding costs nothing.
2. **Take it and leave** → the run ends as a victory: the results screen as today,
   with exactly the reward the panel showed.
3. **Keep going** → the panel closes, the game resumes with 1 s of invulnerability
   (`PartyRules.ResumeGraceSeconds`, as after the cards), the banner "The Rift
   deepens" (ember), and the endless phase starts as today: the portal opens where
   the Colossus fell, bosses every two minutes, the danger clock running.
4. **Leaving later**, three ways, all showing the same panel in *leave* mode with the
   reward at that moment:
   - **the portal**: walking into it opens the panel (it never ends the run by
     itself). It re-arms when you step out of its radius. A world label over it
     reads "Portal home: walk in to leave with your reward".
   - **Esc → Leave with reward** (the pause menu's "Abandon run" button is relabelled
     once the Colossus is dead; before that it stays "Abandon run").
   - (no extra key: Esc and the portal are enough, and every free key near WASD is
     taken.)
   In leave mode, "Keep going" is labelled **Not yet** and Esc means Not yet.
5. **Falling in the endless phase** stays what it is today: the run ends as a
   victory with the reward you had at that moment (run.md's rule, "dying is not a
   punishment beyond ending the run"). The results title gets a subtitle: "You fell
   at 13:40 in the endless phase. The reward is yours." Open question 1 proposes a
   riskier rule.
6. The autoplay bot takes the reward at once (`RunLaunchOptions`), so balance
   numbers stay comparable with earlier passes, as it does with the portal today.

#### The panel

`UI/CashOutPanel.cs`, a `CanvasLayer` at layer 22 (over the HUD, under the item
tooltip), `ProcessMode.Always`. Dim 0.7 over the arena, frame 760×430, centred.

```
┌──────────────────────────────── 760 ───────────────────────────────┐
│                    The Colossus falls             (34 px, gold)     │
│   Take your reward home now, or stay: the Rift keeps getting        │
│   harder, and what it pays keeps growing.        (15 px, text)      │
│ ┌ [1] Take it and leave ───── 350 ┐ ┌ [2] Keep going ───── 350 ┐    │
│ │ (gold border)                    │ │ (ember border)            │    │
│ │ [coin]  1,240 gold               │ │ Next: tier II at 10:00    │    │
│ │ [orb]   12 crafting orbs         │ │   keep 2 items            │    │
│ │ [card]  4 item cards, keep 1     │ │ Every minute: more gold,  │    │
│ │ [ilvl]  item level 59 · rarity   │ │   orbs, item level, rarity│    │
│ │         ×2.9                     │ │ A boss every 2 minutes    │    │
│ │                                  │ │ Leave any time: the portal│    │
│ │                                  │ │   or Esc                  │    │
│ │                                  │ │ If you fall, you keep the │    │
│ │                                  │ │   reward you had.         │    │
│ └──────────────────────────────────┘ └───────────────────────────┘    │
│        Survived 7:18 · 3 bosses · 412 kills · Reward tier I (13 px)  │
└──────────────────────────────────────────────────────────────────────┘
```

- Two **cards** of equal weight (350×250, like the level-up cards: whole card
  clickable, a key cap in the corner). Neither is "the" primary: both are
  legitimate, and the screen exists to ask. Hover lights the border.
- Keys: **1** takes, **2** keeps going. The cards own the keyboard while open
  (`LevelUpScreen`'s rule): every other key is swallowed, and the first 0.45 s
  show the cards dimmed and ignore input, so a dodge pressed as the boss died
  can't pick. At the Colossus offer, Esc does nothing (it's a choice); in leave
  mode Esc = Not yet.
- Item cards line: "4 item cards, keep 1" when items are earned; the orbs line
  counts the orbs (their kinds are rolled at the end, as today).
- Sounds: opening `ui`; Take → the results' `victory`; Keep going → `boss_warn`
  at −8 dB under the "The Rift deepens" banner.

#### The reward on the HUD (endless phase)

- **Reward panel**, top right under the kill counter (x 1006, y 56, 260×92, dark
  glass, gold top border), `HudLayout.Register(panel, "rift_reward", "Rift reward")`:
  ```
  REWARD · TIER II                       (13 px, gold caps)
  1,240 gold · 12 orbs                   (15 px)
  4 cards · keep 2 · item level 59       (13 px)
  [██████████░░░░░░] Tier III at 12:00: 5 cards   (bar 240×6, 12 px dim)
  Leave: walk into the portal, or Esc    (11 px dim)
  ```
  Updated four times a second from `RunRewards.Preview` (no RNG). Hidden before
  the Colossus.
- **Danger gauge** line: "Reward tier II · next at 12:00" replaces "Portal open ·
  stay for more rewards".
- **A new tier**: toast "Reward tier III: 5 item cards" (the Rift's pickup toast
  spot), `levelup` at −8 dB, the panel's border flashes gold for 0.6 s.

#### Reward tiers

Tiers I–IV are **today's steps** in `RunRewards` (no number changes); the tiers name
them so the HUD can show "next". Tier V onwards is new and is what makes the reward
keep improving past 15:00 in a way you can see. Gold, orb count, item level and the
rarity multiplier keep growing continuously as today (formulas under "Rewards").

| Tier | Reached at | What the tier adds |
|---|---|---|
| I · Victory | the Colossus kill | the victory bonus (+150 gold, +2 orbs, +0.3 rarity); 4 item cards (from 7:00), keep 1 |
| II | 10:00 | keep 2 |
| III | 12:00 | 5 item cards |
| IV | 15:00 | keep 3 |
| V | 20:00 | 1 card is guaranteed Rare or better |
| VI | 25:00 | 2 cards guaranteed Rare or better |
| VII, VIII, IX | 30:00, 35:00, 40:00 | 3, 4, 5 cards guaranteed Rare or better |
| X and up | every 5:00 after 40:00 | +1 Ascendant Orb each |

Five cards stay the maximum: the results screen fits five at 1280×720 (and already
clips text at five; the card text should drop the "Hold Alt" hint line in results to
fit). `RunRewards` gains `Tier`, `NextTierAt`, `NextTierText`, `GuaranteedRare`,
`BonusAscendant` and a `Preview(elapsed, bosses, kills, victory)` that returns the
numbers without rolling orb kinds. `For` keeps its current results; the guaranteed
cards re-roll their rarity up to Rare in `ResultsScreen.RollRewards`.

#### Co-op

Keep it simple, and reuse what players already know from the level-up rounds:

- **The offer is a party round** (`RiftHost.Rounds`, a new round kind
  `RoundCashOut = 3` with the row `["leave", "stay"]` for everyone). The game pauses
  for everyone, each player chooses on the same panel, the top shows "1 / 3 chosen"
  and a **15 s** countdown (`PartyRules.CashOutTimeout`), red from 5 s with the
  tick, exactly like the cards.
- **The party leaves only if everyone chooses to leave.** Otherwise everyone stays.
  A player who timed out counts as "leave" (if they're away, leaving is what's safe
  for them, and it can't drag the others home).
- After choosing: "You chose to leave. The party goes home only if everyone does."
  When the round closes with a stay: a toast "The party stays (Ana chose to keep
  going). You can still leave alone: Esc → Leave the party, with your reward."
- **Leaving later**: a client's Esc → **Leave the party** opens the panel in leave
  mode ("You leave alone; the party keeps playing") and then takes today's path
  (`Session.Leave`, own results and rewards). The host's Esc → **End the run for
  everyone** opens it with "Everyone goes home with their reward" and ends the run
  as a victory for all (today's host path). The portal keeps today's party rule:
  when every living player stands in it, the run ends as a victory for all (no panel:
  standing in it together is the decision); a player standing in it alone sees "1 / 3
  in the portal" over it.
- Rewards stay per player, computed on each machine from the shared numbers.

Why a vote and not "the host decides": the host already has the power to end the
run; a vote makes the offer mean the same on every screen, and the round machinery,
the counter and the timer already exist and are understood.

#### Code notes

- `RunDirector.OnColossusKilled`: set `ColossusDefeated`, hold spawning and the
  endless boss timer, and raise `CashOutOffered` instead of opening the portal at
  once; `BeginEndless()` (called on "Keep going") does what `OnColossusKilled` does
  today after the banner (portal, timers, music).
- `RunDirector.CheckExitPortal`: solo → open the panel in leave mode (once per entry)
  instead of `TakeExitPortal`; co-op → unchanged.
- `RunController.OnPauseLeave` and the `PauseMenu` labels as above.
- `RunHud`: the reward panel, the gauge line, the tier toast.
- Autoplay: `RunLaunchOptions.AutoCashOut = true` for the bot.

#### Acceptance and tests (RunSmokeTest / RiftProgressionTest `CASHOUT-*`)

| Check | What |
|---|---|
| `CASHOUT-OFFER` | killing the Colossus opens the panel within 1.5 s; the tree is paused; the director spawns nothing while it is open |
| `CASHOUT-TAKE` | key 1 ends the run as a victory; the results' gold, orb count, cards and keep equal the panel's numbers |
| `CASHOUT-STAY` | key 2 unpauses; the portal exists; spawning resumes; the reward panel shows tier I and "Tier II at 10:00" |
| `CASHOUT-INPUT-GUARD` | a key in the first 0.45 s does nothing; Space never picks |
| `CASHOUT-PORTAL` | walking into the portal opens the panel in leave mode and doesn't end the run; Not yet closes it; re-entering after stepping out reopens it |
| `CASHOUT-PAUSE` | after the Colossus the pause menu reads "Leave with reward" and opens the panel |
| `CASHOUT-TIERS` | `RunRewards.Preview` at 9:59 → tier I, 10:00 → II, 20:00 → V with 1 guaranteed Rare, 45:00 → X with 1 Ascendant Orb |
| `CASHOUT-PREVIEW-MATCHES` | `Preview` and `For` agree on gold, orb count, cards, keep, item level and rarity at random times |
| `CASHOUT-DEATH` | dying at 13:40 in the endless phase gives a victory with the reward at 13:40 and the "You fell…" subtitle |
| `CASHOUT-COOP` (CoopSmokeTest) | the offer opens a round on host and client; host leave + client stay → the run continues; both leave → victory for both; a client that doesn't answer counts as leave |
| `CASHOUT-AUTOPLAY` | the bot run ends at the Colossus with a victory |

Capture: `RunCapture --only=update` gains `rift-cashout-offer.png`,
`rift-cashout-leave.png`, `rift-reward-panel.png` and `coop-cashout.png`
(plus `coop-cashout-chosen.png`).

#### As built (deviations and additions)

- **Code:** `UI/CashOutPanel.cs` (layer 22), `RunRewards` (`Tier`, `TierAt`, `TierStart`,
  `NextTierAt`, `NextTierText`, `GuaranteedRare`, `BonusAscendant`, `OrbCount`,
  `Preview`, `TierToast`; the tier X+ Ascendant Orbs are in `For`'s `Orbs` list),
  `RunDirector` (`CashOutOffered`, `BeginEndless`, `AwaitingCashOut`, `PortalEntered`,
  `AutoCashOut`), `RunHud` (reward panel, gauge line, tier toast), `PauseMenu`
  (`LeaveTextNow`), `RunState.Fell`.
- **The hold** is a 1.2 s `SceneTreeTimer` started at the kill (`CashOutHoldSeconds`),
  not part of the director's clock; it pauses with the tree, so a level-up that opens
  in those 1.2 s simply delays the panel. The victory banner now reads "Victory / The
  Ember Colossus has fallen"; "The Rift deepens" (ember, `boss_warn` at -8 dB) and the
  portal come with Keep going. `TakeExitPortal()` is now only "end the run as a
  victory" (the panel's Take, the bot's backstop); walking into the portal solo raises
  `PortalEntered`, and the controller opens the panel in leave mode.
- **The numbers on the panel are the payout:** `CashOutPanel.Shown` comes from `Preview`
  at the moment the panel opens, the tree is paused, and `For` runs in the same call
  that ends the run (`CASHOUT-TAKE` compares gold, orbs, cards and keep).
- **HUD reward panel** sizes itself to its five lines (about 100 px, not exactly 92)
  and shows a placeholder in the HUD editor (`rift_reward`).
- **Autoplay:** `RunLaunchOptions.AutoCashOut` (also `--auto-cash-out=0|1`) ends the run
  as a victory on the kill, as the portal used to; the bot sets it for every run.
- **Results:** the item-card text of five cards drops the "Hold Alt" line (and is 14 px,
  cards 272 px tall); the "Every minute survived..." line is dropped at five cards. The
  subtitle "You fell at 13:40 in the endless phase. The reward is yours." shows when
  `RunState.Fell` and the run was a victory.
- **Co-op:** `PartyRules.CashOutTimeout` (15 s), `RiftProtocol.RoundCashOut = 3` (the
  `PartyRound` kind; a client's `ClientPick` carries "leave" or "stay"), `NetSession.Protocol`
  is now 4. The host validates the answer (open round, once, only "leave" or "stay"; a
  client row report for this round is refused). Anyone staying keeps the whole party in;
  silence counts as leave. The party's stay toast, the one-second resume grace and the
  pause on every machine are the level-up round's. `LastCashOutLeft`, `LastCashOutStayer`,
  `LastCashOutPicks`, `OpenCashOutRoundForTest` and `CashOutTimeoutOverride` are hooks for
  tests. Leave mode over a running co-op game (a client's Esc → Leave the party, the
  host's End the run for everyone) cannot pause: the panel holds the local player still
  (`Player.ControlsEnabled`) instead.
- **Not built:** the "1 / 3 in the portal" count over the portal. The portal's caption in
  co-op says "the whole party walks in to leave"; the rule is unchanged.
- **Tests:** `CASHOUT-TIERS`, `-PREVIEW-MATCHES`, `-OFFER`, `-INPUT-GUARD`, `-TAKE`,
  `-STAY`, `-TIER-TOAST`, `-PAUSE`, `-PORTAL`, `-LEAVE-PAYS`, `-DEATH`, `-AUTOPLAY` in
  `RiftProgressionTest` (`RiftProgressionTest.CashOut.cs`; the run lives under a
  pausable holder node, since a child of the always-processing test node never pauses);
  `CASHOUT-COOP-*` in `CoopSmokeTest` (`CoopSmokeTest.CashOut.cs`).

#### Owner decisions (2026-10-17)

1. **Death:** solo, falling ends the run with the reward reached; co-op, a downed
   player gets back up after 10 s and the run ends only when everyone is down at
   once, with the reward reached (multiplayer.md -> "Deaths"). The "keep only the
   last tier" rule stays unused.
2. Co-op vote: as built — if anyone stays, the party stays.
3. Tier V+ rewards and prices: keep the first guesses until they've been played.

## Experience and pickups

- Enemies drop experience gems worth their `ExperienceValue` (1–5, elites ×6,
  Herald 60). The gem sprite changes with value (green/blue/red).
- Gems inside the player's **Pickup Radius** (stat, base 80 px) are pulled in with
  acceleration, so a big radius feels like a vacuum rather than a drift.
- **Gem cap:** above 300 gems on the floor, new experience merges into an existing
  gem instead of spawning another (Vampire Survivors does the same). Keeps the
  late floor readable and cheap.
- Curve: `5 + 3·(L−1) + 0.55·(L−1)²` to go from level L to L+1 — about twenty levels
  in a full run. (This line said 0.45 until 2026-10-17; the code has been 0.55. The
  doc now matches the code; no number changed.)
- **Co-op (2026-10-17):** one party bar instead of one per player. Every player's
  gems fill it, and it needs the curve × the party size, so the party levels at about
  the solo pace. Each level is a round of picks for everyone, with the whole game
  paused (multiplayer.md -> "Level-ups").
- Rarer drops: **life orbs** (heal 25 % of max life; 1.2 % from normal enemies,
  50 % from elites) and **gold coins** (5 % / always from elites).
- **Gear** drops on the ground through the existing item pipeline (label, filter,
  click to pick up): elites 30 %, bosses 2 items each.
- **Crafting orbs** (the same orbs as realities, [crafting.md](crafting.md)): 0.4 %
  per ordinary kill, 25 % per elite, 3 per boss. They go to the account like any orb.
- **Drops spawn from a queue.** Gems, life orbs, gold, crafting orbs and gear are
  rolled on the kill frame but appear a few per frame (at most 2 items or 8
  pickups a frame, `LootQueue`), so a mass kill's drops trickle in over a few
  frames instead of costing one frame. Nothing is lost; power-ups still appear at
  once. Same queue in realities (performance.md -> "Mass deaths").
- **Power-ups** (rare, at most one of each on the floor; elites are 40× likelier to
  drop one):
  - **Cataclysm** (red burst, 0.09 % per kill) — every non-boss enemy on screen
    dies at once, with experience and drops as normal; bosses on screen lose 10 % of
    their life. Its victims never drop another Cataclysm.
  - **Lodestone** (horseshoe magnet, 0.13 % per kill) — every experience gem and
    gold coin on the map flies to the player, however far.
  Each flashes the screen and shows a short line above the life cluster.

## Rewards

At the end of every run — victory or not — the results screen pays out by time
survived (`RunRewards`):

| | Formula | 4:00 death | 7:30 win | 16:00 endless |
|---|---|---|---|---|
| Gold | `15·m^1.6 + kills/20 (+150 victory)` | ~150 | ~600 | ~1,600 |
| Crafting orbs | `⌊m/1.5⌋ + bosses (+2 victory)` | 3 | 10 | 19 |
| Item cards | 3, +1 at 7:00, +1 at 12:00 | 3 | 4 | 5 |
| Items kept | 1, +1 at 10:00, +1 at 15:00 | 1 | 1 | 3 |

Item level is `3 + 5·minutes + 8·bosses killed`. Rarity rolls the `RiftReward`
cascade (items.md -> "Drop tables (cascade)") with a multiplier of
`1 + 0.12·minutes + 0.15·bosses (+0.3 victory)`: ×2.65 for a 7:30 win with three
bosses (≈ 40 % Rare per card), ×4.2 after sixteen endless minutes (≈ 63 %). Elites and
bosses dropping gear mid-run use the `RiftElite` / `RiftBoss` tables, +5 % per minute. Gold and orbs go straight into the wallet
before the character is saved. Everything picked up from the ground
during the run is kept too. A kept item that finds the bag full goes to **Pending
items** in the hideout stash (items.md -> "Pending items") instead of being lost.

### Minimum for an item (2026-10-17)

Owner report: starting a Rift and leaving at once still paid an item. Item cards
now need the run to have **lasted 60 s or killed 30 monsters**
(`RunRewards.MinItemSeconds`, `MinItemKills` — either is enough; co-op counts the
party's kills). Below that the results screen shows no cards and says why: "No item
this time: a Rift pays an item after 60 s or 30 kills (this one: 10 s, 3 kills)".
Gold and orbs keep their formulas, which already start from about zero (a 10-second
run pays 1 gold and no orb). Confirmed by the owner (2026-10-17).

Why these numbers: an ordinary first minute kills 50–80 monsters with auto-cast, so
any real attempt clears both bars long before the first elite; dying in the first
minute is rare enough that the kill bar covers it. An item at 1:00 rolls item level
~8 at ×1.1 rarity, so standing still for a minute to farm it is not worth anyone's
time. Alternatives: scaling the item count from zero (a fractional item has no
good presentation), a time-only rule (punishes a fast death in a hot start), a
kills-only rule (a stand-still player with auto-cast reaches it anyway). Dying is not a punishment beyond ending the run: in a
game whose persistent progression is gear, taking the gear away on death would make
every death a double loss.

## Run-only bonuses

Every run character gets **+60 maximum life** and **+8 mana regeneration** on top of
the persistent character — whose class, passive tree and ascendancy apply in the
Rift as well (progression.md -> "The character in the Rift"). The base numbers (70 life, ~6 mana/s) were tuned for the
dummy sandbox; a survivor run is far denser. These modifiers have their own source
and are stripped (with the whole run build) before the profile is saved.

## Controls

WASD move · mouse aim · **Space dodge** ([dodge.md](dodge.md)) · I inventory ·
Esc pause. Skills **auto-cast** by default (survivor convention: the run is about
positioning). Settings → *Auto-cast* off makes slots fire only while LMB (all) or
1–4 (one) is held. *Auto-aim* aims at the nearest enemy instead of the cursor.
There is no click-to-move inside a run: LMB/RMB are cleared, and WASD + dodge is the
whole movement kit. Click-to-move still works in the hideout and the sandbox, where
`force_move` moved from Space to **Shift** (Space is the dodge everywhere).

### Controls in the endgame era (2026-10-12)

The Survival Rift keeps the genre's rule: **every equipped skill fires on its own,
auto-aimed, and costs nothing** — there is no mana bar here.

**Cast timers (2026-10-14).** Skills used to fire the moment their cooldown allowed,
which made every skill feel like a machine gun and left no room to improve. Each
skill now has an **auto-cast interval**: `max(0.5 s, 1.5 × use time + cooldown)` (Fireball: 0.6 s instead of 0.4 s),
divided by the new **Auto-cast Speed** stat times cast speed (spells) or attack
speed (attacks). The skill bar's sweep shows the timer. Level-up cards improve it:
**Quickening** (+12 % auto-cast speed per pick) joins the passive pool, and the
existing cast/attack speed cards now count too. Skills start staggered so a fresh
build doesn't fire everything on the same frame.

**Life-only HUD.** With no mana the right half of the cluster was empty and the
controls hint sat across the life bar. The right-hand bar is now the **danger
gauge** — the level, filling towards the next one, and what is coming ("Bone Herald
in 0:42", "Portal open") — and the hint moved above the cluster. In realities and the
hideout skills are cast by hand instead: the six loadout slots are bound to
**LMB, RMB, 1, 2, 3, 4**, each button firing only its own slot at the cursor
(an empty LMB keeps click-to-move). See docs/design/menus.md -> "Controls".

## Tuning — the autoplay bot

`scripts/autoplay.sh --runs=3 [--skill=spark]` plays full runs headless and
unthrottled (~3× real time): it flees nearby enemies, steps out of anything red on
the floor, dodges hits about to land, collects gems and picks level-up cards at
random. It logs a status line every 30 s and a result per run. It plays like an
average player with no knowledge of the boss scripts, so the target is: *usually
reaches the Colossus, sometimes beats it.*

## Alternatives considered

- **Endless mode / 30-minute runs (Vampire Survivors).** Rejected for now: the
  ARPG half wants frequent run ends so gear arrives often. Seven minutes is long
  enough to build something and short enough to "one more run".
- **Several maps.** Deferred: one arena first, done properly.
- **Manual-only casting.** Kept as an option, not the default — with 150 enemies on
  screen, aiming every cast competes with dodging, and dodging is the skill
  expression the bosses test.

## Open questions

- A second biome/arena with its own enemy roster and boss.
- Meta-progression beyond gear (gold has no sink yet — a vendor or crafting bench
  in the hideout is the obvious first one).
- Should difficulty tiers exist (higher tiers: denser, tougher, better item levels)?
  The director's numbers are all in one place to make that cheap.
