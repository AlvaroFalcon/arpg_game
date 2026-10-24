# Enemies

Status: **implemented** (first roster). All art drawn with EasyPixel
(`tools/art/sprites/enemies.ts`).

## Shared behaviour — `Enemies/Enemy.cs`

- `CharacterBody2D`, floating motion. Physics layer **EnemyFaction**, mask
  **World | EnemyFaction**: enemies push each other apart (no single-file conga
  lines) but **pass through the player** — the player's layer is now PlayerFaction
  only. Being body-blocked into a corner by slimes reads as unfair in this genre;
  being hurt by touching them does not.
- **Contact damage** by distance check (`ContactRadius`), once per `ContactCooldown`
  (0.8 s) per enemy.
- **Knockback** from hits that carry it (projectiles 40, novas 260, meteors 160),
  reduced by `KnockbackResistance`; bosses are immovable.
- Hit flash, damage numbers in a column by the target (Full / Compact / Off in settings; menus.md -> "Damage numbers"), ailment tint (orange
  ignite, yellow shock, blue chill), a dust puff on death.
- Base stats come from exports (`BaseLife`, `BaseSpeed`, `BaseArmour`), not a
  StatBlock per enemy; attributes are zeroed so Strength's derived life does not
  pad every monster.
- **Scaling** at spawn: life `×(1 + 0.38·min)`, damage `×(1 + 0.13·min)`.
- **Elites**: ×1.35 size, gold tint, 4× life, +15 % speed, +25 % damage, 6× XP,
  60 % knockback resistance, guaranteed gold, 50 % life orb, 30 % gear drop.
- Everything hostile joins the `enemies` group; the player joins `players`.
  `CombatQuery` answers "nearest hostile" / "hostiles in radius" from those groups
  (chains, auto-aim, areas, boss bullets).

## Roster

| Enemy | Behaviour | Life | Speed | Threat | Unlocks |
|---|---|---|---|---|---|
| Ash Slime | Walks straight at you. | 18 | 72 | 6 contact | 0:00 |
| Cinder Bat | Fast, weaves side to side so swarms read as swarms. | 10 | 128 | 4 contact | 0:25 |
| Bone Caster | Keeps 200–320 px away, circles, **glows for 0.45 s** then fires a slow bolt through the same `SkillUser` pipeline the player uses. | 26 | 76 | 8 bolt | 1:10 |
| Ember Brute | Lumbers in; within 280 px **draws its charge lane on the ground for 0.8 s**, then charges 380 px (1.6× contact damage while charging). | 95 (40 armour) | 56 | 9 / 14 | 1:55 |
| Volatile Imp | Sprints in; within 70 px stops and **draws its blast radius for 0.9 s**, then explodes (22 fire, 80 px). Killing it while primed cancels the blast. | 15 | 150 | 22 blast | 2:30 |

Added for the endgame realities (docs/design/endgame.md), same scenes/classes:

| Enemy | Reality | Behaviour | Life | Speed | Threat |
|---|---|---|---|---|---|
| Crypt Skeleton | Bone Crypt | Chaser with a slight weave; rattles in fast. | 24 | 92 | 7 contact |
| Drowned Thrall | Drowned Mirror | Slow bloated chaser, armoured, resists knockback. | 46 (20 armour) | 58 | 11 contact |
| Siren | Drowned Mirror | Ranged like the Caster, fires a cold **Tide Bolt**. | 24 | 84 | 9 cold bolt |

In realities every monster also has a **rarity** (normal/magic/rare/unique-rare,
with mods), a **monster level** from the tier, and starts **dormant** with its
pack until the player comes close; movement follows the reality's flow field
around walls (docs/design/endgame.md -> "Navigation").

Design rule shared with the bosses: **every big hit is drawn where it will land
before it lands.** Slimes and bats are the exception — their "telegraph" is their
body.

## Alternatives considered

- **Pathfinding for every enemy.** Not needed in an open arena and expensive at
  150+ bodies; straight-line chase with physical separation is the genre norm.
- **StatBlock `.tres` per enemy.** Kept as a pattern for dummies, but one number per
  enemy in the scene is easier to tune for now.

## Open questions

- Enemy affixes on elites (PoE rare-monster mods: hasted, extra projectiles,
  vampiric) would make elites more interesting than "bigger".
- A ranged enemy with a ground-targeted attack (a mortar) to punish standing still.
