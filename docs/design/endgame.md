# Endgame: Mirror Shards and Altered Realities

Status: **[IMPLEMENTED]** (2026-10-11). Direction from the owner; the open
questions of the first draft were answered by "go with your recommendations"
(see "Decisions" at the end). This is now the core loop; the survivor run lives
on as the Survival Rift.

Owner brief: *"More like Path of Exile's endgame, without the campaign. Keys —
mirror shards — take you to an altered reality (a dungeon) with a specific
layout. At the end there is always a boss that drops something. On the way,
normal, magic and rare enemies, sometimes very rare ones (another colour) that are
stronger, like mini-bosses. The final boss can drop more shards of the same tier
or one/two tiers higher. Dungeons go up to difficulty 20."*

## The loop

```
Main menu ─► character select ─► Hideout ──(Mirror: pick a shard tier)──► Altered Reality (tier N)
                                    ▲                                         │ rooms + corridors
                                    │                                         │ normal / magic / rare / unique-rare packs
                                    │                                         ▼
                                    └── exit portal ◄── loot, shards (N, N+1, N+2) ◄── Reality boss behind a gate
```

PoE's map loop: the key is consumed, the dungeon generates rewards, the boss is
the guaranteed payout, and the keys it drops keep the loop going and push it
upward. There is no campaign: a new character starts in the hideout, and **tier 1
is always free**, so nobody can lock themselves out of the endgame.

## Mirror Shards (the keys)

- Stored on the character as a count per tier (1–20), not as inventory items —
  `CharacterData.Shards` (scripts/Characters/CharacterData.cs). The first draft
  proposed 1×1 stackable items; counts were simpler and lose nothing until shards
  carry their own modifiers (see "Shard modifiers").
- Spent at **the Mirror of Realities** in the hideout (E): `MirrorPanel` lists
  the 20 tiers with owned counts, shows the tier's numbers, and opens the reality.
- **Boss drop table** (`RealityTiers.RollBossShard`, verified over 20 000 rolls by
  the smoke test):

  | Outcome | Chance |
  |---|---|
  | Same tier | 65 % |
  | Tier +1 | 28 % |
  | Tier +2 | 7 % |

  **A boss always drops at least one shard** (owner's rule, 2026-10-14 — the old
  table had a 25 % "nothing", which could strand a player at their tier), plus a
  15 % chance (scaled by item quantity) of a second same-tier shard: ≈ 1.15 shards
  per clear. Rares (3 %) and unique-rares (15 %) can also drop a
  current-tier shard as a safety net. Tier 20 caps the result.
- Shards drop as floor pickups with a light beam coloured by band (white 1–5,
  yellow 6–10, orange 11–15, red 16–20, PoE's map colours).

### Arehucaz, the shard dealer (2026-10-14)

A tall hooded figure in a ragged black robe stands in the dark alley by the hideout's
north gate, violet eyes in the hood and Mirror Shards glinting on a cloth at his
feet. He sells shards for gold (`ShardVendor`, scripts/World/ShardVendor.cs;
`ShardVendorPanel`):

- **Only tiers the character has cleared** (2 to `HighestTierCleared`; tier 1 is
  free at the Mirror anyway). Buying never takes you past your own best tier — he
  is insurance against a dry spell of boss shards, not a shortcut.
- **Five at a time**, per character. The stock comes back after **ten more cleared
  realities**, or at once for **10,000 gold**.
- **Price** `50 + 25·tier + 4·tier²`: 79 gold at tier 2, 700 at tier 10, 2,150 at
  tier 20 — about one or two boss kills' worth of gold at the same tier.

Alternatives considered: an account-wide stock (rejected — shards and the tier record
are per character, and a shared stock would let a main feed an alt past tiers the alt
never cleared); a timer-based restock (rejected — the owner asked for "after 10
maps", which also ties it to playing rather than waiting).

### Shard modifiers ("Empower")

At the Mirror, a shard can be **empowered for gold** (15 + 12·tier, re-rollable):
it rolls 1–3 modifiers (more at higher tiers) that make the reality harder and
pay in item quantity and rarity (`RealityModifier`, scripts/World/RealityLaunch.cs):

| Modifier | Effect | Quantity | Rarity |
|---|---|---|---|
| Hardened | monsters +40 % life | +15 % | +10 % |
| Searing | monsters +25 % damage | +15 % | +15 % |
| Swift | monsters +20 % movement | +10 % | +10 % |
| Hordes | larger, more frequent packs | +25 % | +5 % |
| Elite | more magic and rare packs | +15 % | +20 % |
| Volatile | a quarter of normals explode on death | +12 % | +10 % |
| Frail | players 15 % less max life | +12 % | +12 % |
| Exposed | players −20 % elemental resistances | +10 % | +10 % |

This gives gold its second sink (after respecs) and makes "how much risk do I
want" a decision made before entering, PoE-style.

## Tiers 1–20

One number drives everything (`RealityTiers`, scripts/World/RealityTheme.cs):

| | Tier 1 | Tier 10 | Tier 20 |
|---|---|---|---|
| Monster level / item level | 1 | 41 | 85 |
| Monster life multiplier | ×1 | ×4.6 | ×25 |
| Monster damage multiplier | ×1 | ×2.1 | ×5 |
| Magic / rare pack chance | 45 % / 30 % | 62 % / 49 % | 80 % / 70 % |
| Unique-rare chance per spot | 12 % | 25 % | 40 % |
| Grey (normal) pack size | 5–8 | 8–13 | 12–19 |
| Second grey pack at a spot | 15 % | 39 % | 65 % |
| Magic per magic pack · rares per rare pack | 3–4 · 1 | 4–5 · 1 | 5–7 · 2 (from tier 12) |
| Portals (lives) | 3 | 3 | 1 (from tier 15) |

Life and damage multipliers are exponential (25^((t−1)/19), 5^((t−1)/19)): each
tier is the same *relative* step. Monster life outpaces damage for the same
reason as in the survivor run — player damage scales with gear and gems, player
life much less. Item level follows monster level, so the authored affix tiers map
onto tiers 1–20 without new data.

**Portals**: dying costs a portal and respawns you at the entrance (a boss fight
in progress resets: the gate opens and the boss goes back to sleep). The last
portal lost collapses the reality and the shard is gone. Three portals at low
tiers, one from tier 15: the stakes rise with the tier.

## Mirror jewels (2026-10-14)

The Mirror's second tab is a **jewel tree** (`JewelTree`, scripts/World/MirrorJewels.cs;
`JewelTreeView`). Seventy sockets: **the Mirror's Heart** in the middle, six
branches of eleven and three bridges between neighbouring branches. Each branch is
a spine of six small sockets ending in a notable, a side path of two small sockets
ending in a second notable, and one dead-end small socket:

| Branch | Small socket | Spine notable | Side notable |
|---|---|---|---|
| Abundance | +3 % item quantity | Overflowing Glass: +10 % quantity | Hoarder's Reflection: bosses drop 1 extra item |
| Gilded Glass | +5 % item rarity | Prismatic: +15 % rarity | Crowned Spoils: boss items roll 30 % rarer |
| Shardbound | +4 % extra boss shard | Shard-Sworn: +20 % extra boss shard | Ascending Echo: +15 % boss shard one tier higher |
| Teeming | +3 % more monsters | Swarming Reflections: +10 % monsters | Dangerous Company: +12 % magic and rare packs |
| Avarice | +6 % gold | Midas' Mirror: +25 % gold | Tithe of Kings: bosses drop double gold |
| Artifice | +5 % crafting orbs | Crucible: +20 % orbs | Insight: +10 % character experience |

Bridges give 60 % of both neighbours' small stat. A full tree is roughly +45 % item
quantity, +65 % rarity, +80 % extra-shard chance, +40 % monsters and +80 % gold.

- **Socketing**: a jewel goes into a socket next to the Heart or to a filled
  socket. Any jewel can come back out, free, as long as everything else stays
  connected — jewels are the scarce thing, so re-planning shouldn't cost.
- **Drops**: a reality boss drops a jewel with `30 % + 1 %·tier`. The first **40**
  jewels come from any boss; jewels 41–55 only from **tier 12+** bosses, 56–69 only
  from **tier 17+**. 69 jewels fill the tree, then no more drop.
- **Account-wide** (`Profile.Jewels`), like the Mirror itself and orbs: the tree
  is the account's long-term progression across characters.
- **Applies in Altered Realities only** (not the tutorial, not the Survival Rift):
  quantity/rarity add to the shard modifiers' bonuses, density scales grey pack
  size and the chance of a second pack, and the rest hooks the boss and loot drops.

Alternatives considered: jewels as items with random stats socketed into fixed
slots (PoE's atlas jewels) — more loot to sort for a system the owner described as
a tree that "keeps giving benefits"; jewels as plain points on a passive tree
without the socket look — the same rules, but the jewels then disappear from view.
Open question: should jewels ever have qualities (a "perfect" jewel doubling its
socket)?

## Altered Realities (the dungeons)

### Layouts: room graphs, rasterised

A reality is a **hand-designed layout** with randomised details. The first draft
proposed drawing layouts as ASCII grids; the first one drawn that way turned out
disconnected in two places, which is exactly the class of mistake a text grid
invites. Layouts are instead authored as **graphs of rooms and corridors**
(`LayoutCatalog`, scripts/World/LayoutCatalog.cs):

- **Rooms** have a fixed position and size in 64 px cells, a shape (rect or
  circle), pack markers (`p` normal, `m` magic, `r` rare, `u` possible
  unique-rare), pillars and hazard pools.
- **Corridors** connect two rooms (L-shaped, width 3 by default). Corridors that
  share a `Group` are alternatives: one of them is built per reality, so the same
  place has different routes.
- `LayoutBuilder.Rasterize` turns the graph into a walkable grid; pack spots,
  pillars and pools land at random inside their rooms. Connected by
  construction — and the smoke test still checks 40 seeds per layout.
- `LayoutBuilder.BuildScene` paints it with the theme's tileset (floors, hazard,
  wall, rune circle in the boss room) and builds wall collision (World layer) and
  hazard collision (a new Hazard layer that blocks walking but not projectiles).

The three layouts:

| Reality | Theme | Roster | Boss |
|---|---|---|---|
| The Ashen Halls | burning ruins, lava | slime, bat, imp, brute, caster | The Ember Colossus |
| The Bone Crypt | catacombs, ichor pits | **skeleton**, bat, caster, brute | The Bone Herald |
| The Drowned Mirror | flooded temple, deep water | **drowned thrall**, **siren**, bat, imp | **Nereth, the Drowned Reflection** |

Bold = new for this milestone (docs/design/enemies.md, bosses.md).

### Navigation: one flow field, not the NavigationServer

The first draft assumed the NavigationServer. Realities use a **flow field**
instead (`FlowField`, scripts/World/FlowField.cs): every 0.25 s a BFS from the
player's cell over the walkable grid; each monster steers toward the neighbouring
cell with the lowest distance. One BFS serves every monster, it cannot fail to
find a path that exists, and it sidesteps the NavigationServer shutdown hang the
toolchain notes warn about. The boss gate simply marks its cells unwalkable.

### Packs

Packs are placed and **dormant** until the player comes within aggro range (or a
pack-mate is hit), then wake together. A failed magic/rare roll still leaves a
normal pack, so rooms are never empty.

**Density (2026-10-14).** Owner feedback: maps were too easy and too empty. From
tier 1 to 20 grey monsters grow the most — packs ×2.4 and a growing chance of a
second pack at each spot, roughly ×4 grey monsters overall — while magic and rare
monsters grow gently (the stronger ones climb, but stay a minority). Grey monsters
are what make clearing feel like clearing; more rares would mostly make it slower.
See the tier table above (`RealityTiers.NormalPackSize`, `ExtraPackChance`,
`MagicPackSize`, `RaresPerPack`).

### Monster rarity

PoE's vocabulary, colour-coded on the monster and its name plate (`MonsterRarity`,
scripts/Enemies/MonsterRarity.cs):

| Rarity | Colour | Life | Damage | Mods | XP | Notes |
|---|---|---|---|---|---|---|
| Normal | white | ×1 | ×1 | 0 | ×1 | the bulk of every pack |
| Magic | blue | ×2 | ×1.15 | 1–2 | ×3 | packs of 3–4 |
| Rare | yellow | ×5 | ×1.3 | 2–3 | ×10 | leads normals; generated name ("Bitterhowl") |
| Unique-rare | violet | ×12 | ×1.6 | 3–4 + signature | ×30 | mini-boss with magic escort; titled name ("Drezzik, the Hollow King") |

Mods: Hasted, Armoured, Fire-touched, Vampiric, Volatile (telegraphed death
explosion), Frost Aura, Splitter, Regenerating, Empowered, Barrage (ranged only).
Unique-rares also cast a **signature** attack from the boss toolkit (slam, rain of
circles, orb nova, triple lane) under the same telegraph rules as bosses.

### The boss

Always at the end, in its own room, asleep until you step in — then **the gate
closes behind you** (bars drawn across the corridor, flow field blocked). On
death: 3–4 items (rarity-biased), the shard roll, a 70 % gem drop, a pile of
gold, and the **exit portal** home. Reality bosses get a per-theme life multiplier
on top of tier scaling (see "Tuning").

### Drops and pickups

Items, **gems** (skill or support, docs/design/classes.md) and **shards** drop
on the floor. Gem pickups: a new skill joins the character; a duplicate skill
melts into gem experience for the best copy owned; supports stack to 4 copies
(one per loadout slot) and a fifth becomes gold.

### Reality HUD

`RealityHud`: character name/level/class and experience bar; vitals, dodge
charges and the class resource (Combo / Rage / Arcane Charges); the skill bar;
top-right the reality name, tier, monster level, portals left, cleared %, timer,
modifiers and a **minimap** of explored floor (boss room in red, entrance in
blue); the boss bar with phase markers when the gate closes; banners and a stack
of loot/level toasts.

## What happened to the survivor run

- **Kept** everything systemic: enemies, bosses, telegraphs, dodge, skills,
  supports, ailments, items/affixes, hideout, profile, menus, art pipeline.
- **Replaced** the per-run level-up cards with a **persistent character**:
  character level (1–70, XP 40·L^1.6), a passive point per level spent in one
  shared tree, and skills/supports as **gems** in a 4-slot loadout
  (docs/design/classes.md). The card library became the tree's content.
- **Kept as an activity**: the survivor mode is the **Survival Rift**, entered
  from the hideout portal or the Mirror panel. It now uses the character's
  loadout (no starting pick if skills are equipped) and pays out gear, gold and
  character experience.

## Tuning (autoplay bot, reality mode)

`scripts/autoplay.sh --mode=reality --class=<id> --tier=1 --runs=6` plays
realities back to back with a fresh in-memory character that never equips gear
(so it measures the "levels and gems only" floor), moving up a tier per clear.
Findings and changes:

| Finding | Change |
|---|---|
| Colossus and Nereth took 6–8 min against a level-4–9 melee character; the Herald took ~2 | Reality boss life multipliers: Colossus ×0.45, Nereth ×0.5 (Herald ×2.6, it was authored as a mid-run boss) |
| Melee starters dealt half to a quarter of Fireball+Echo's damage while taking far more risk | Double Strike 9→15, Cleave 14→24, Ground Slam 20→34, Sunder 22→36 |
| Auto-aim picked the nearest monster even behind a wall (and a sleeping boss behind its gate) | Auto-aim in realities requires line of sight on the grid; the boss preference only applies to an awake boss in sight |

After the pass (gearless, from tier 1): **Warrior** 6/6 clears to tier 7,
**Rogue** 4/6 to tier 5, **Arcanist** clears steadily to ~tier 9 and starts
losing portals and timing out around tiers 10–13 at character level 16–21 —
which is where gear is meant to take over. The bot kites near-perfectly at
range, so ranged numbers are a lower bound on difficulty for real players.

## Milestones (all six done)

1. Shards, Mirror, tier scaling, drop table — done.
2. Monster rarity, mods, name plates, unique-rares — done.
3. Layouts (room graphs), flow field, boss gate, minimap — done.
4. Persistent character, three classes, gems, skills panel, passive tree — done.
5. Ascendancies and the altar — done.
6. Content: two more realities and tilesets, three monsters, Nereth, shard
   modifiers — done. Boss-specific unique items remain open.

## Alternatives considered

- **Procedural dungeons (Diablo-style generation).** Rejected per the brief ("a
  specific layout"): handcrafted graphs with randomised details give learnable
  places and are far easier to balance and telegraph around.
- **ASCII-grid layouts.** Tried and dropped (see "Layouts").
- **NavigationServer pathing.** Dropped for a single flow field (see "Navigation").
- **Lose the shard on any death (PoE's old rule).** Replaced by portals: three at
  low tiers is forgiving while learning a boss, one from tier 15 keeps the stakes.
- **Infinite tiers (greater rifts).** Rejected: 20 tiers is a finishable goal;
  shard modifiers are the scaling knob beyond raw tier.

## Decisions (owner: "go with your recommendations")

- Death penalty: 3 portals, 1 from tier 15.
- Survivor run: kept as the Survival Rift.
- Persistent levelling with a shared passive tree and gems, not per-run cards.
- Unique-rare colour: violet (orange stays free for unique *items*).
- Three layouts at first; tier 1 always free; respecs cost gold.

## Open questions

1. Boss-specific unique items (the brief's "some kind of drop" is covered by the
   loot table; uniques would make each boss worth farming).
2. Shards as items with their own rolled modifiers (today the modifiers are
   rolled at the Mirror) — worth it once trading or stash tabs exist.
3. A fourth+ layout per theme so a tier doesn't always repeat the same three.
