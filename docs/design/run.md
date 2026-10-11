# The Run (survivor layer)

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
| 0:00 | Slimes only. ~1.1 spawns/s. |
| 0:25 | Bats join (fast, weaving). |
| 1:10 | Bone casters join (ranged). |
| 1:55 | Ember brutes join (telegraphed charge). |
| 2:30 | Volatile imps join (run in and explode). |
| 1:30, then every 45 s | An **elite** (bigger, tougher, glows gold, 6× XP, can drop gear). |
| every 60 s | A **swarm**: a ring of fast enemies closing in from all sides. |
| 3:00 | **The Bone Herald** (mid boss). Normal spawns drop to 30 % while it lives. |
| 7:00 | **The Ember Colossus**. All normal enemies burn away (no XP), gems on the floor fly to the player, and the fight is one-on-one plus what the boss summons. |
| Colossus dies | Victory → results. |

Spawn rate grows linearly: `1.1 + 1.15·minutes` per second, capped by an alive cap
of `110 + 18·minutes`. Enemies spawn just outside the camera so they walk in instead
of popping in on top of the player. Enemy life scales `×(1 + 0.38·minutes)` and
damage `×(1 + 0.13·minutes)` at spawn time — life faster than damage, because the
player's damage scales with their build and their life mostly does not. Bosses
scale with the square root of the life multiplier and not at all on damage; their
numbers are authored for the minute they appear ([bosses.md](bosses.md)).

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

## Rewards

At the end of every run — victory or not — the results screen offers **three
items, keep one**. Item level is `3 + 5·minutes + 8·bosses killed`; rarity skews up
with time survived, bosses killed and victory. Everything picked up from the ground
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
