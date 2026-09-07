# Stat System

## What it is

A minimal modifier-stack system for numeric player stats, living under
`scripts/Stats/`. Three pieces:

- `StatModifier` — one modification: a `ModifierType` (`Flat`, `Increased`, `More`),
  a `float Value`, and an optional `object? Source` tag.
- `Stat` — a `BaseValue` plus a list of `StatModifier`s, with a computed `Value`
  property.
- `ActorStats` — a `[GlobalClass] Node` holding a combat entity's live stats.
  *(Renamed from `PlayerStats` during M1 implementation — see the resolved open
  question below. It now exposes `MovementSpeed`, `Life`/`Mana` resource pools,
  and the four combat modifier-carrier stats, entity-agnostic so an enemy scene
  can use the exact same node.)*

This is intentionally small. It is *not* a full RPG stat/attribute system — there's
no stat registry, no per-stat resource definitions, no serialization. It's just the
modifier math, built now because the movement feature needed a `MovementSpeed`
value to read, and because getting the formula right early avoids having to
retrofit it once real itemization exists.

## The formula

```
Value = (BaseValue + sum of Flat mods)
        * (1 + sum of Increased mods / 100)
        * product over each More mod of (1 + moreMod.Value / 100)
```

This mirrors Path of Exile's actual modifier system:

- **Flat** modifiers add directly to the base value before any percentage is
  applied (e.g. "+10 to Movement Speed" — not really a PoE example, but the
  mechanic is general).
- **Increased/Reduced** modifiers all sum together into one additive pool, then
  that pool is applied as a single multiplier. Two `+10% Increased` modifiers give
  `+20%` total, not `+21%` (they don't compound with each other).
- **More/Less** modifiers are each their own independent multiplier and *do*
  compound with each other and with the Increased/Reduced pool. Two `+10% More`
  modifiers give `*1.1 * 1.1 = *1.21`, not `*1.2`.

## Why mirror PoE's system specifically (rather than a simpler additive-only model)

This game is explicitly drawing on the ARPG/PoE genre for itemization depth later
(gear, talents, buffs/debuffs all stacking on the same stats). PoE's Flat →
Increased → More layering is a well-understood, well-balanced way to let "big"
effects (More/Less, usually from skill mechanics or rare uniques) stay separately
impactful and multiplicative, while routine incremental bonuses (Increased, usually
from gear affixes/passives) stack predictably and additively without runaway
compounding. Building the *math* correctly now — even with only one stat wired up
— means equipment and talent systems can plug straight into `AddModifier` /
`RemoveModifier` / `RemoveModifiersFromSource` later without a rewrite of the
underlying formula.

`RemoveModifiersFromSource(object source)` exists specifically so a future
equipment/talent piece can tag its own modifiers with e.g. `this` (the item
instance) when granting them, then call `RemoveModifiersFromSource(this)` on
unequip/unlearn without having to track which modifier objects it added.

## Why plain C# classes, not Godot `Resource`/`Node` for `Stat`/`StatModifier`

`Stat` and `StatModifier` are pure data + math with no need for the scene tree,
signals, or editor serialization at this stage, so they're plain C# classes rather
than `[GlobalClass]` Godot types. `ActorStats` *is* a `Node` (`[GlobalClass]`)
because it needs `[Export]` to let base values be tuned from the editor and needs
to live in a combat entity's scene tree (a child named "Stats") so its
`SkillUser`/`Health`/controller siblings can `GetNode<ActorStats>("Stats")` it.

## Current scope

Only `MovementSpeed` exists today (130 px/s placeholder base value). No other stat
(damage, life, resistances, attack speed, etc.) has been wired up yet — there's no
gear or talent system to feed modifiers in yet either.

## Open questions

- ~~Which stat comes online next~~ — **answered** by the combat design pass, see
  `docs/design/skills.md`. Combat forces **five** (updated by the third combat pass):
  `Life` (current + maximum), `Mana` (current + maximum + flat regen), a **damage
  modifier carrier**, **`AttackSpeed` + `CastSpeed` as two separate stats**, and
  **`CooldownRecovery`**.
  - The single unified `UseSpeed` from the first combat pass is **dead** — attack
    speed and cast speed are different stats. Both are multipliers with
    `BaseValue = 1.0`; effective use time is `skill.BaseUseTime / speedStat.Value`.
  - **Which one a skill uses is now *derived from its `Attack`/`Spell` tags*, not
    stored on the definition.** *(Changed by the third combat pass — pass two
    specified an explicit `SpeedStat` field and pass three deleted it before it was
    built.)* The resolution is one small pure function: `Attack` → `AttackSpeed`,
    else `Spell` → `CastSpeed`, else no speed scaling. `Attack + Spell` together is an
    authoring validation error. The reason for the reversal: an explicit field creates
    two independent notions of "is this a spell" — the tag every affix reads and the
    field only the timer reads — which can silently disagree with no UI able to
    surface it. Full argument in `docs/design/skills.md` → *Attack speed and cast
    speed*.
  - **`CooldownRecovery`** *(new in the third combat pass)* — a multiplier with
    `BaseValue = 1.0`, applied as a **divisor**:
    `effectiveCooldown = skill.Cooldown / CooldownRecovery.Value`. It must be a rate
    divisor rather than a subtractive "-X% cooldown" so that stacking can never reach
    zero; PoE made exactly this migration. Player-facing wording can still say "20%
    faster cooldown recovery" — this is about the maths. It exists at all because
    "skills scale only by modifiers" is decided, so without it cooldown-gated skills
    would be the only skills in the game that don't scale and would die of
    irrelevance.
  - `Mana` was promoted into the first combat slice because resource cost is now
    part of it. Note costs are **not mana-only**: a skill authors its own cost as a
    *list* of `(ResourceType, Amount, Mode)` entries, so `Life` and future resources
    are cost sources too. `Life` therefore has a second role beyond "can die."
  - **Four of the five are "modifier carrier with no meaningful base of its own"**
    (damage, attack speed, cast speed, cooldown recovery). The actor does *not* own a
    `Damage` stat with a base value — the base comes from the skill definition and the
    actor supplies only the Flat/Increased/More stack applied at cast time. That shape
    is now overwhelmingly the dominant one rather than the exception, which
    strengthens the case for `Stat` being able to compute against an externally
    supplied base.
- **`StatModifier` needs an optional condition field, added now while it's cheap.**
  The skills pass requires that a modifier be able to carry *when it applies* (initially
  a required-tag set; empty = always), not just `(Type, Value, Source)`. Nothing reads
  it in the first slice. The reason to add it early is that retrofitting touches every
  site that *constructs* a modifier, and by the time gear and passives exist that's a
  large number of places. See `docs/design/skills.md` → Seams.
- **`StatModifier` is not the only kind of modifier — there is a second, disjoint
  type.** *(New in the third combat pass.)* `StatModifier` handles numbers on
  actor-owned stats via the Flat/Increased/More formula, and it is order-independent
  by construction. The upgrades that actually define builds — "projectiles split in
  two on hit", "+1 projectile", "add the Fire tag", "pay half the mana cost as life" —
  are **behaviour transforms on a per-cast parameter block**, not numbers on a stat.
  Those live in a separate `CastModifier` type owned by the skill system, they are
  **order-dependent** (hence a `Priority` field), and they never touch `Stat` or
  `ActorStats`. **Nothing in `scripts/Stats/` should grow to accommodate them.** The
  only shared machinery is the *condition* type (the tag/state predicate), which both
  kinds use. Full walkthrough in `docs/design/skills.md` → *Seam 6*.
- **Modifier lookup must take a query object, not be a bare property read.**
  `stats.GetModifiers(query)` where the query carries at minimum "which stat" and
  "the cast's tags" (and later actor state, target, etc.). The first-slice
  implementation may ignore everything but the stat. This is the one item the skills
  pass flags as genuinely expensive to defer: without it, adding conditional
  modifiers later means changing every call site that reads a modifier.
- ~~**`PlayerStats` needs to become entity-agnostic.**~~ — **resolved during M1
  implementation.** `PlayerStats` was renamed to `ActorStats` (a straight rename,
  not a base/subclass split — there was nothing player-specific left to split
  out once `MovementSpeed`, `Life`, `Mana`, `Damage`, `AttackSpeed`, `CastSpeed`
  and `CooldownRecovery` are all named properties any combat entity can have).
  `Faction` and `IgnoresResourceCosts` are `[Export]` fields defaulting to
  `Faction.Neutral` / `false` respectively, so every scene (player or enemy) sets
  them explicitly rather than inheriting a hidden bias. No enemy scene exists yet
  to prove this out end-to-end — that's still real, if likely small, validation
  work for milestone 2 (see `docs/design/skills.md` priority item 11).
- Should `ActorStats` eventually hold a generic `Dictionary<string, Stat>` /
  registry instead of one named property per stat, once there are many stats? For
  now, one explicit property per stat keeps call sites (`_stats.MovementSpeed.Value`)
  simple and type-safe; revisit if the property list gets unwieldy.
  The second combat pass named **two** pressures toward a keyed registry; the third
  pass removed one of them. `SpeedStat` no longer exists (speed is derived from tags),
  so no stat identifier is stored in authored data. What remains is
  `GetModifiers(query)`, which takes a *stat identifier as an argument* — named C#
  properties can't be passed around as values without a parallel enum, so some keying
  mechanism is still needed. Halved, not eliminated. Still not urgent at five stats;
  relevant well before fifteen.
- Damage-over-time, auras, and other timed/conditional modifiers aren't handled by
  this system yet — `Stat` has no concept of a modifier expiring on its own; whatever
  system grants a timed buff will need to call `RemoveModifier`/
  `RemoveModifiersFromSource` itself when the effect ends.
  - Related, and decided by the third combat pass: **damage-over-time is a property of
    the damage *payload*, not of this stat system.** The payload carries a
    `DamageDeliveryKind` (`Hit` | `OverTime`) so that a "+% Damage over Time" modifier
    can filter on how the damage is delivered rather than on the skill's tags — a skill
    can deal a hit *and* apply a burn, and a tag-only filter would wrongly buff both.
    Nothing in `scripts/Stats/` changes for this; it's noted here so the distinction
    isn't rediscovered later.
