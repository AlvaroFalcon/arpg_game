# Stat System

## What it is

A minimal modifier-stack system for numeric player stats, living under
`scripts/Stats/`. Three pieces:

- `StatModifier` — one modification: a `ModifierType` (`Flat`, `Increased`, `More`),
  a `float Value`, and an optional `object? Source` tag.
- `Stat` — a `BaseValue` plus a list of `StatModifier`s, with a computed `Value`
  property.
- `PlayerStats` — a `[GlobalClass] Node` that currently exposes exactly one stat:
  `MovementSpeed`, built from `[Export] float BaseMovementSpeed = 130f`.

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
than `[GlobalClass]` Godot types. `PlayerStats` *is* a `Node` (`[GlobalClass]`)
because it needs `[Export]` to let base values be tuned from the editor and needs
to live in the Player scene tree so `Player.cs` can `GetNode<PlayerStats>(...)` it.

## Current scope

Only `MovementSpeed` exists today (130 px/s placeholder base value). No other stat
(damage, life, resistances, attack speed, etc.) has been wired up yet — there's no
gear or talent system to feed modifiers in yet either.

## Open questions

- Which stat comes online next — likely whatever combat needs first (probably
  `Life` or a damage stat) once combat design is scoped. That's a design call, not
  an engineering one.
- Should `PlayerStats` eventually hold a generic `Dictionary<string, Stat>` /
  registry instead of one named property per stat, once there are many stats? For
  now, one explicit property per stat keeps call sites (`_playerStats.MovementSpeed.Value`)
  simple and type-safe; revisit if the property list gets unwieldy.
- Damage-over-time, auras, and other timed/conditional modifiers aren't handled by
  this system yet — `Stat` has no concept of a modifier expiring on its own; whatever
  system grants a timed buff will need to call `RemoveModifier`/
  `RemoveModifiersFromSource` itself when the effect ends.
