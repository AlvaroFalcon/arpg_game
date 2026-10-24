# Run progression (level-ups, skills, supports, passives, keystones)

Status: **implemented** (first pass). Owner brief: *"character progression, not as
big as PoE but with different possible combinations."*

## What it is

Every level-up pauses the run and offers **three cards**. A card is one of:

| Kind | What it does | Limits |
|---|---|---|
| **New skill** | Adds a skill to the next free slot. | 4 skills max. |
| **Skill level** | +20 % more damage for that skill. | Level 6 max. |
| **Support** | Attaches a support to one specific skill (PoE support gem). | 3 per skill, one conversion per skill. |
| **Power-up** | A stat bonus, rolled at a rarity: Common ×1, Magic ×1.5, Rare ×2.2. | Most uncapped; notables capped at 2. |
| **Keystone** | Lesser at levels 5, 15, 25…; great at 10, 20, 30…, offered alone. | Each once. |

The run starts with a **skill pick** ("The start pick" below), and two **per-card
rerolls** last the whole run. The build is wiped at the end of the run; it is a run
build, not the character — but the character's passive tree and ascendancy count
too ("The character in the Rift").

## Offer composition (2026-10-17)

Owner feedback: the cards offered far too many passive-tree keystones, and the
levels in between should be mostly skills (new ones, and levelling owned ones until
they are maxed) and power-ups. Every number below lives in **`OfferRules`**
(scripts/Progression/OfferRules.cs) and nowhere else.

| Level | Cards | Rule |
|---|---|---|
| Start of the run | 3 skill cards | Skill-less character: skills of its class, its starting skill always among them. Otherwise: a new class skill or a level for a skill it brought. |
| 5, 15, 25, 35… | 3 **lesser** keystones | `FirstLesserKeystone` 5, every `KeystoneInterval` 10. Falls back to an ordinary level once none are left. |
| 10, 20, 30, 40… | 3 **great** keystones | `FirstGreatKeystone` 10, every 10. Same fallback. |
| Every other level | 3 cards, a category per card | At least one **skill** card while one exists (`GuaranteeSkillCard`); the other cards roll a category by weight, then an entry by its own weight. |

| Category (ordinary levels) | Weight | Entries and their weights |
|---|---|---|
| **Skill** | 35 | New skill 3 while the build has fewer than two skills, else 1; ×3 for a skill of the character's class (`ClassSkillMultiplier`). Skill level 1.6 each. |
| **Power-up** | 50 | Each passive's own weight (1 by default; Might 1.6, Quickening 1.4, Magnetism 0.9, Volley 0.35…), only those whose `Requires` holds (fire damage needs a fire skill, Ferocity an attack). |
| **Support** | 15 | 1 per legal (support, owned skill) pair. |

A category with nothing left drops out and the others share its weight. No two cards
on screen are ever the same thing (identity, not rarity: two "Might" cards of
different rarities count as the same). The resulting mix, measured by
RiftProgressionTest over 1 152 ordinary cards of a one-skill build: **57 % skills, 32 %
power-ups, 11 % supports**.

Why a category roll first: the old pool was one weighted list where 25-odd passives
(weight ~1 each) outweighed everything else, so the "skill" half of the build came
late and supports almost never. Choosing the category first keeps the mix stable
however much content a category holds, and makes the table above the whole story.
The guaranteed skill card is what makes "levels are mostly skills" true even in a
row that rolls two power-ups.

Alternatives considered: fixed slots (card 1 always a skill, card 2 a power-up,
card 3 a wild card — readable, but a row is then predictable and a maxed build
shows dead slots); a pity counter for skills (more state, same effect as the
guarantee); keystones as a fourth card on keystone levels (the owner wanted fewer
keystones, not keystones everywhere).

## Keystone cadence: lesser and great (2026-10-17)

Every fifth level used to offer three of the same eleven keystones. Now keystones
come in two tiers (`KeystoneDefinition.Tier`):

- **Lesser** — a narrow trade that bends one part of the build, with numbers a size
  smaller: Ricochet, Juggernaut, Prismatic Bulwark, Iron Will, and four made for the
  Rift's cards: **Scavenger** (100 % increased pickup radius, 20 % increased
  experience; 10 % less damage), **Overclock** (skills fire 35 % more often; 20 %
  less damage), **Shield Conduit** (+60 energy shield; 15 % less maximum life),
  **Wind Runner** (20 % more movement speed, +1 dodge charge; 10 % more damage
  taken). Eight in all.
- **Great** — build-defining, changes how the whole build works: Glass Cannon,
  Avatar of Fire, Avatar of Frost, Echoing Mind, Chain Reaction, Phase Dancer,
  Vampiric Pact. Seven.
- **Blood Magic** stays a passive-tree keystone only (`InRift = false`): skills cost
  nothing in the Rift, so its downside would be gone.

A full 7:30 run (about level 20) sees two lesser and two great keystone rows; the
endless phase adds one of each per ten levels — four of each by level 40, so neither
pool runs dry in a long run. The split follows Path of Exile's notable/keystone and
Last Epoch's minor/major idea: a run gets a small bend early (level 5) and its
identity at 10, instead of being asked to rebuild itself every five levels.

Rejected: keeping every-five-levels with fewer keystones per row (still too often);
great keystones only (level 5 would be an ordinary level and the first real choice
would wait until level 10 — with ~20 levels in a run that is half of it).

## The start pick (2026-10-17)

Every solo run opens with a skill card pick (`RunBuild.RollStartOffers`):

- **A character with skills** keeps the skills of its loadout, but they **start at
  level 1 with no supports** (`RunBuild.AdoptCharacterSkills`; the gems keep their
  levels — only the live skill changes). The pick offers three skill cards: a new
  skill of its class (while it has fewer than four) or a level for one it brought.
- **A skill-less character** gets three skills **of its class**
  (`ClassDefinition.RiftSkills`), its starting skill always among them:

| Class | Rift skills (first = always offered) |
|---|---|
| Warrior | Cleave, Ground Slam, Sunder, Double Strike, Glacial Hammer |
| Rogue | Double Strike, Throwing Knives, Ethereal Blades, Storm Strike |
| Arcanist | Fireball, Spark, Frost Nova, Meteor, Ethereal Blades, Frost Shards |

Later new-skill cards favour the class pool ×3 but never lock others out: a warrior
can still pick up Spark. Supports are not carried over because the run is about
growing a build from level 1, and the supports are a card category of their own; the
character's investment carries over through the tree and the ascendancy instead.

The run owns the skills once it has started: `CharacterBuild.RunOwnsSkills` makes a
character level-up mid-run rebuild stats, tree and ascendancy without re-equipping
the loadout (it used to wipe the run's skills, levels and supports), and gem
experience still goes to the gems without changing the live skill's level.

**Co-op (2026-10-17):** every player gets the start pick, every run, whether or not
the character brought skills. It is the host's first level-up round: the whole party
picks at once, the game is held (clock stopped, no monsters) until everyone has chosen
or the 10 s timer takes the first card, then the run begins (multiplayer.md ->
"Level-ups"). `RunPlayer.AwaitingStartPick` is gone.

## Rerolls (2026-10-17)

Two per run (`OfferRules.RerollsPerRun`), each replacing **one** card
(`RunBuild.TryReroll`): the new card follows the same rules as its row (a keystone
row rerolls into a keystone of the same tier, the start pick into another skill
card) and is never one already on screen, the replaced one included. A charge is
only spent when a replacement exists. UI: docs/design/menus.md -> "Level-up and
results".

Why per card: rerolling all three threw away the card the player wanted to keep and
made the reroll a gamble on the whole row; one card at a time turns it into a
decision ("I like these two, show me something instead of this one").

## The character in the Rift (2026-10-17)

The persistent character's **passive tree and ascendancy apply in the Rift**: the
run player carries the `CharacterBuild` (class attributes and bonuses, every
allocated node, ascendancy nodes and their behaviours, the class resource) with its
own modifier source, and the run build's cards stack on top with theirs — the same
"remove by source" design, so nothing is stripped or applied twice.
RiftProgressionTest checks it in a real Run scene: every stat line of every
allocated node is on the run player exactly once, the Barbarian's nodes are live,
and the Rift's own +60 life sits alongside.

Double counting is prevented where it could happen:

- a **keystone from the tree** (`ks_*` nodes) is never offered as a card
  (`RunBuild.HasKeystone` checks both), and a replayed key for it is refused;
- a keystone's forced element (Avatar of Fire on the tree) counts for the card
  gates (`EffectiveTags`), so fire passives are offered to a tree Avatar;
- gems: the live skill starts at level 1 (above), not at the gem's level on top of
  the run's skill levels.

Balance, autoplay bot (`scripts/autoplay.sh --class=warrior --level=20 [--tree=off]
[--ascend=1] --runs=3`, 2026-10-17): all nine level-20 warrior runs won. Damage
taken: tree off 347–482, tree on 204–522, tree + four Barbarian nodes 55–162; the
Colossus fell at 8:04–14:41 with no clear order (random cards dominate). The
ascendancy is felt; a level-20 character finds the Rift easy because the Rift does
not scale with character level (open question).

## Energy Shield (2026-10-17)

A new stat (`StatId.EnergyShield`, flat, base 0) for the "shield" power-up the
owner asked for: hits take it before life (ignites and other already-resolved
damage don't), it recharges at 50 % of its maximum per second after 2 s without
damage (`Health.ShieldRechargeDelay`, `ShieldRechargePerSecond`), and newly gained
shield arrives charged. The HUD draws it as a pale band over the life bar. Cards:
**Aegis** (+20, scales with rarity) and the lesser keystone Shield Conduit. Named
"energy shield" rather than "ward" because co-op called a choosing player "warded"
(until 2026-10-17, when co-op level-ups started pausing the game instead).

## Why it is shaped like this

The combinations live in the *interaction* between the four layers:

- **Skills** decide the shape of damage (projectile, area, orbit, targeted).
- **Supports** change *how* a skill behaves — more projectiles, chaining, forking,
  repeats, conversion to another element. This is where PoE's depth comes from, and
  the engine already had the seam for it: support gems are the first real
  `CastModifier` subclass (docs/design/skills.md → *Seam 6*), living on
  `SkillInstance.Attached` and running in the per-cast collect-and-apply stage.
- **Passives** scale what the skills and supports have become. Elemental damage
  passives are only offered once a skill actually deals that element (after
  conversions), so cards stay relevant.
- **Keystones** bend the whole build (Avatar of Fire turns everything Fire; Blood
  Magic changes the resource; Echoing Mind doubles every spell at a cast-speed cost).

A Spark build can go "Spark + Chain + Lightning passives + shock chance", or convert
Spark to Cold with Cold Conversion and lean on chill; a Meteor build can take Spell
Echo + Concentrated Effect; Avatar of Fire makes Frost Nova a fire nova and every
fire passive applies to it. None of this needed a special case: it falls out of tags
→ damage type resolution → tag-conditioned modifiers, which were already the design.

## The skills

| Skill | Archetype | Tags | Notes |
|---|---|---|---|
| Fireball | Projectile | Spell, Projectile, Fire | Reliable single target, ignites with chance. |
| Spark | Projectile | Spell, Projectile, Lightning | 2 sparks, chain 1. Shock with chance. |
| Frost Nova | Area (self) | Spell, Area, Cold | Cooldown 2.2 s, knockback, always chills. |
| Ethereal Blades | Orbit | Spell, Area, Duration | Physical. Protects your space. |
| Meteor | Area (targeted, 0.7 s delay) | Spell, Area, Fire | Huge hit, small area, cooldown 2.8 s. |
| Frost Shards | Projectile | Spell, Projectile, Cold | 3 shards in a 24° fan, 9 damage each, 0.45 s. Cold always chills. (2026-10-18, Odran sells it from level 6.) |
| Storm Strike | Melee (arc) | Attack, Melee, Lightning | 85 range, 60° arc, 20 damage, 0.40 s. Shocks like any Lightning hit. (Level 10.) |
| Glacial Hammer | Melee (arc) | Attack, Melee, Area, Cold | 95 range, 110° arc, 30 damage, 0.70 s, knockback. Cold always chills. (Level 14.) |

(The three added on 2026-10-18 are in `UpgradeCatalog.Skills`, so the Rift offers them
too; each is in its class's `RiftSkills`: hideout.md -> "Odran's Skill gems tab".)

Two archetypes were added: `AreaSkillDefinition` (instant or delayed circle,
centred on the caster or a target point — the same archetype boss slams use) and
`OrbitSkillDefinition`. Projectile **chains** were implemented (they were authored
but inert): PoE's order — pierce, then fork, then chain.

## Supports (17)

Multiple Projectiles, Pierce, Fork, Chain, Faster Projectiles, Increased Area,
Concentrated Effect, Spell Echo, Faster Casting, Cold/Fire/Lightning Conversion,
Ignition, Overcharge, Elemental Focus, Extended Duration, Blade Storm. Each declares
which skills accept it by tags (`RequiresAny` / `Excludes`), so a non-projectile
never sees Pierce. Priority bands: conversions 5, additive counts 10, repeats 20,
multiplicative 30 — so "+2 projectiles" resolves before anything multiplies.

## Power-ups (passives, 32)

**Might** (+10 % increased damage, any skill), elemental/spell/projectile/area/
**melee** damage (each only once a skill has that tag), life, regen, armour,
movement, cast speed (spells) and **Ferocity** (+8 % attack speed, attacks),
**Quickening** (+12 % auto-cast speed — skills fire more often, run.md -> "Cast timers"),
cooldown recovery, crit chance and multiplier, area, projectile speed, pickup
radius, experience, ignite and shock chance, skill duration, life on kill,
**Bloodthirst** (+0.5 % life leech), **Aegis** (+20 energy shield), elemental
resistances, dodge recovery, and three notables: **Volley** (+1 projectile / blade),
**Acrobatics** (+1 dodge charge), **Arc Weaving** (+1 chain). The mana cards
(Clarity, Deep Well, Efficiency) were removed on 2026-10-17: skills cost nothing in
the Rift, so they were dead cards.

## Keystones (great and lesser — see "Keystone cadence")

| Keystone | Tier | Upside | Downside |
|---|---|---|---|
| Glass Cannon | Great | 60 % more damage | 40 % less max life |
| Avatar of Fire | Great | Everything deals Fire, 25 % more damage, +15 % ignite | Cold/Lightning bonuses stop applying |
| Avatar of Frost | Great | Everything deals Cold, 25 % more damage, +15 % freeze chance | Fire/Lightning bonuses stop applying |
| Chain Reaction | Great | Kills explode for 20 % of the victim's max life as Fire | — (it is a gamble on clear speed) |
| Phase Dancer | Great | +2 dodge charges, faster recharge, dodging casts a frost nova | 15 % more damage taken |
| Echoing Mind | Great | Every spell repeats once | 35 % less cast speed |
| Vampiric Pact | Great | +5 life on kill, life on kill tripled | No life regeneration |
| Blood Magic | Tree only | Skills cost Life (never below 40 %), 30 % more life, +3 regen | Life is now your mana |
| Ricochet | Lesser | Player projectiles bounce off walls up to 2 times (`ProjectileParams.Bounces`, set by a global cast modifier) | 15 % less projectile damage |
| Juggernaut | Lesser | 50 % more Armour | 10 % less attack speed |
| Prismatic Bulwark | Lesser | 25 % less Elemental damage taken | 15 % more Physical damage taken |
| Iron Will | Lesser | 20 % less damage taken | 15 % less movement speed |
| Scavenger | Lesser (Rift) | 100 % increased pickup radius, 20 % increased experience | 10 % less damage |
| Overclock | Lesser (Rift) | Skills fire 35 % more often | 20 % less damage |
| Shield Conduit | Lesser (Rift) | +60 energy shield | 15 % less maximum life |
| Wind Runner | Lesser (Rift) | 20 % more movement speed, +1 dodge charge | 10 % more damage taken |

**Max-life cards heal by what they add.** Vitality (+15 life, 5 % increased maximum life) raises current life by exactly the rise of the maximum, so picking it at full life leaves you full. It used to add a flat +15 to current life while the increased part raised the ceiling further, so every card left a gap; with no regeneration (Vampiric Pact) that gap stayed until a kill. Only the gain is healed, never a refill, and never while downed.

Chain Reaction's explosions (and the Warrior's Decay) go off on the frame after
the kill, so a cascade spreads through a pack one hop per frame (~16 ms) as a
visible ripple instead of resolving at once (performance.md -> "Mass deaths").

## Ailments

Elements carry an identity through ailments (`Combat/Ailments.cs`): **Cold always
chills** (30 % slow, 2 s), **Fire ignites** with chance (90 % of the hit over 3 s;
stronger replaces weaker), **Lightning shocks** with chance (+25 % damage taken,
3 s). Chances come from stats plus supports, travel on the `DamagePayload`, and are
applied by the target. Elemental Focus trades them away for damage.

## Run stats

The run layer added 14 stats (appended, never renumbered — see `StatId`): Damage
Taken, Pickup Radius, Experience Gain, Area of Effect, Projectile Speed, Dodge
Charges, Dodge Recovery, Extra Projectiles, Ignite Chance, Shock Chance, Skill
Duration, Life on Kill, Extra Chains, Extra Pierce. All are ordinary registry stats,
so gear affixes can roll them later with zero new plumbing.

## Implementation notes

- `Progression/OfferRules.cs` holds every number of the offer table.
- `Progression/RunBuild.cs` owns the build and rolls offers; every modifier it
  grants carries one `Source`, so the run is removed in one call before the profile
  is saved.
- `Progression/UpgradeCatalog.cs` holds all content in code (lambdas over the stat
  and cast-context API — a Resource cannot hold those). Move to `.tres` once a
  tuning pass wants editor authoring.

## Alternatives considered

- **A passive tree.** Too big for a run that lasts seven minutes and resets; cards
  give PoE's flavour of choice without a tree screen. A small persistent tree is a
  candidate for meta-progression later.
- **Weapons that level (Vampire Survivors' evolutions).** Supports already provide
  the "this skill becomes something else" moment, and reuse PoE's vocabulary.

## Open questions

- The Rift ignores character level: a level-20 character with its tree wins every
  bot run. Scale the Rift's monsters with character level, add difficulty tiers
  (run.md open question), or accept it as the "relaxed" mode?
- Should the character's **supports** come into the Rift with its skills (as cards
  already taken, or as a free first support)? Today only the skills do, at level 1.
- The start pick for a character with four or more skills is skill levels only;
  fine, or should it offer a power-up instead?
- Lesser keystones are Rift-only; should the passive tree get lesser keystones too
  (on the middle ring), so the two halves share the vocabulary?
- Gear affixes rolling the new run stats (+1 dodge charge boots, ignite chance
  wands) would tie the two halves together tightly. Not done yet.
- More skills per element (a cold projectile, a lightning area) to make conversion
  supports less mandatory for some builds.
- Numbers are first-pass; see the bot results in the devlog.
