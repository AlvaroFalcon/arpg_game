# Run progression (level-ups, skills, supports, passives, keystones)

Status: **implemented** (first pass). Owner brief: *"character progression, not as
big as PoE but with different possible combinations."*

## What it is

Every level-up pauses the run and offers **three cards** (rerollable twice per run).
A card is one of:

| Kind | What it does | Limits |
|---|---|---|
| **New skill** | Adds a skill to the next free slot (1–4). | 4 skills max. |
| **Skill level** | +20 % more damage for that skill. | Level 6 max. |
| **Support** | Attaches a support to one specific skill (PoE support gem). | 3 per skill, one conversion per skill. |
| **Passive** | A stat bonus, rolled at a rarity: Common ×1, Magic ×1.5, Rare ×2.2. | Most passives uncapped; notables capped at 2. |
| **Keystone** | Offered alone, every 5th level. Big upside, real downside. | Each once. |

The run starts with a **free pick of the first skill** (all five offered). The
build is wiped at the end of the run; it is a run build, not the character.

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

## Passives (29)

Elemental/spell/projectile/area damage, life, regen, armour, movement, cast speed,
cooldown recovery, crit chance and multiplier, mana and mana regen, mana cost,
area, projectile speed, pickup radius, experience, ignite and shock chance, skill
duration, life on kill, elemental resistances, dodge recovery, and three notables:
**Volley** (+1 projectile / blade), **Acrobatics** (+1 dodge charge), **Arc
Weaving** (+1 chain).

## Keystones (7)

| Keystone | Upside | Downside |
|---|---|---|
| Glass Cannon | 60 % more damage | 40 % less max life |
| Avatar of Fire | Everything deals Fire, 25 % more damage, +15 % ignite | Cold/Lightning bonuses stop applying |
| Chain Reaction | Kills explode for 20 % of the victim's max life as Fire | — (it is a gamble on clear speed) |
| Phase Dancer | +2 dodge charges, faster recharge, dodging casts a frost nova | 15 % more damage taken |
| Blood Magic | Skills cost Life (never below 40 %), 30 % more life, +3 regen | Life is now your mana |
| Echoing Mind | Every spell repeats once | 35 % less cast speed |
| Vampiric Pact | +5 life on kill, life on kill tripled | No life regeneration |

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

- Gear affixes rolling the new run stats (+1 dodge charge boots, ignite chance
  wands) would tie the two halves together tightly. Not done yet.
- More skills per element (a cold projectile, a lightning area) to make conversion
  supports less mandatory for some builds.
- Numbers are first-pass; see the bot results in the devlog.
