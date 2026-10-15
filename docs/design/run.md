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

Flow: Main menu → Hideout → portal → **pick a first skill** → survive → boss →
results (choose one of three items) → Hideout.

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
| every 60 s | A **swarm**: a ring of fast enemies closing in from all sides. |
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
- **Spawn rate** `1.1 + 1.35·m` per second, **alive cap** `115 + 24·m`.
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

Killing the Colossus used to end the run. Now it opens a **portal home** (step on it
to leave with a victory) and the waves come back: the danger clock keeps running, a
boss arrives every two minutes (Herald and Colossus alternate, without clearing the
arena), and every minute adds to the end-of-run reward. Falling in the endless phase
still counts as a victory — the player already won; what they risk by staying is the
time they would have spent, not the win. The autoplay bot takes the portal at once
so balance numbers stay comparable with earlier passes.

## Experience and pickups

- Enemies drop experience gems worth their `ExperienceValue` (1–5, elites ×6,
  Herald 60). The gem sprite changes with value (green/blue/red).
- Gems inside the player's **Pickup Radius** (stat, base 80 px) are pulled in with
  acceleration, so a big radius feels like a vacuum rather than a drift.
- **Gem cap:** above 300 gems on the floor, new experience merges into an existing
  gem instead of spawning another (Vampire Survivors does the same). Keeps the
  late floor readable and cheap.
- Curve: `5 + 3·(L−1) + 0.45·(L−1)²` to go from level L to L+1 — about twenty levels
  in a full run.
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
during the run is kept too. Dying is not a punishment beyond ending the run: in a
game whose persistent progression is gear, taking the gear away on death would make
every death a double loss.

## Run-only bonuses

Every run character gets **+60 maximum life** and **+8 mana regeneration** on top of
the persistent character. The base numbers (70 life, ~6 mana/s) were tuned for the
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
