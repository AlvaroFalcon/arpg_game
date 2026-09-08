# Affixes

Status legend, matching `docs/design/skills.md`:
- **[DECIDED]** — settled, implement as written.
- **[PROPOSED]** — recommendation awaiting the project owner's confirmation.

The random modifiers that roll on equipment. `docs/design/items.md` owns rarity,
budgets and the drop pipeline; `docs/design/stats.md` owns what affixes attach to.
This document is the pool itself, the tier maths, and the authoring shape.

## The two pools **[DECIDED]**

**Temper** supplies the leading word of an item's name ("*Blazing* Iron Wand").
**Sigil** supplies the trailing clause ("Iron Wand *of Vigor*"). Full rationale,
including why the terminology is not "prefix/suffix", is in `items.md`.

A 4-affix budget is **max 2 Temper + 2 Sigil**. That cap is the whole reason the
split exists: it stops an item rolling four variations of the same stat, and gives
future crafting a granularity to work with.

Rough division of labour, not a hard rule: Temper leans offensive and additive,
Sigil leans defensive and utility. Both pools carry both — a pool that mapped
cleanly onto offence/defence would just be two stats wearing a costume.

## Authoring shape **[DECIDED]**

```
AffixDefinition : Resource
  Id, DisplayWord            // "Blazing" or "of Vigor"
  Pool                       // Temper | Sigil
  StatId, ModifierType       // what it modifies, and how

  Condition : SkillTags      // all-of, empty = always
  ConditionAny : SkillTags   // any-of, empty = no any-of requirement

  MinIlvl                    // tier 1 gate; tier 7 always gates at item level 120
  MaxIlvl?                   // optional hard cutoff, used sparingly
  ValueAtT1 (min,max)
  ValueAtT7 (min,max)
  Curve
  TierOverrides[]            // optional per-tier escape hatch

  Weight                     // relative roll frequency
  AllowedItemClasses         // empty = any
```

Tiers 2–6 interpolate between T1 and T7, and so do their **gates**: tier 1 unlocks
at `MinIlvl`, tier 7 at item level 120, with the rest spaced evenly between. A
uniform per-tier step was tried first and is wrong — with a fixed step of 17, tier
7 gates at `MinIlvl + 102`, so every affix introduced above item level 18 could
never reach its own top tier inside the 1–120 range (Titanic, at `MinIlvl` 30,
gated tier 7 at 132). A top roll the player can never see is strictly worse than
not authoring it. Interpolating means a late-unlocking affix climbs its tiers
faster, which is the intended reading of "seven tiers over item level 1–120". Hand-authoring seven rows per affix is
~7x the data entry and makes every rebalance a mass edit; the override array keeps
control where a specific affix does not fit the curve.

**Adding an affix that targets an existing stat is a `.tres` file and nothing
else** — no code, no recompile. That property is the point of this shape and
should not be traded away.

## Condition semantics **[DECIDED]**

`StatModifier.Condition` was specified in `skills.md` seam 4 as a *required-tag
set* — all-of. That is correct for "increased Fire Spell Damage" (needs Fire
**and** Spell) and wrong for "increased Elemental Damage" (needs Fire **or** Cold
**or** Lightning). Both are ordinary affixes, so both semantics are needed.

`StatModifier` gains a second field:

```
applies = Condition ⊆ tags
          AND (ConditionAny == None OR (ConditionAny ∩ tags) ≠ ∅)
```

Two fields rather than a mode enum, because an affix can legitimately need both at
once ("increased Elemental Spell Damage": `Condition = Spell`,
`ConditionAny = Fire|Cold|Lightning`).

**`ActorStats.GetModifiers` must actually apply this.** Today it returns every
modifier on the stat and ignores `query.Tags` entirely — dormant rather than
broken, since nothing yet constructs a conditional modifier. Every conditional
affix below depends on that method being implemented.

## Attributes **[DECIDED]**

Three: **Strength, Dexterity, Intelligence.** Base 10 each. They are real stats in
the registry, and they grant derived bonuses — an attribute that is only a number
is a filler affix players learn to ignore.

| Attribute | Grants, per point |
|---|---|
| Strength | +2 Maximum Life, +1 Armour |
| Dexterity | +0.5% increased Critical Strike Chance *(temporary — see below)* |
| Intelligence | +2 Maximum Mana, +0.1 Mana Regeneration |

### Dexterity is deliberately temporary **[DECIDED]**

Dexterity should grant **Evasion and Accuracy**. Neither exists: `stats.md` defers
evasion with reasons (it is not one stat but accuracy + evasion + a hit-chance roll
on every attack, plus an entropy-vs-random decision, and it puts RNG in front of
every hit).

Rather than ship Dexterity as a dead stat, it grants crit chance until evasion
lands, at which point **this derivation moves** and the crit association is
dropped. Recorded here so that when it changes it reads as the plan rather than a
balance patch. Nothing else should come to depend on Dex granting crit.

### Derivations need a recompute hook

`Stat` has no change notification, so "Strength grants Life" cannot be a live
formula. Derived modifiers are recalculated at known moments — equip, unequip, and
any other event that can change an attribute — and are applied as ordinary
`StatModifier`s tagged with a shared attribute-derivation `Source`, so recompute is
"remove by source, re-add". No new mechanism; it reuses
`RemoveModifiersFromSource`.

## The Temper pool **[PROPOSED]**

| Word | Modifies | Type | T1 | T7 | Min ilvl | Slots |
|---|---|---|---|---|---|---|
| Heavy | Damage | Flat | 2–4 | 45–70 | 1 |
| Blazing | Damage *(Fire)* | Flat | 3–5 | 55–85 | 1 |
| Frozen | Damage *(Cold)* | Flat | 3–5 | 55–85 | 1 |
| Charged | Damage *(Lightning)* | Flat | 2–7 | 40–100 | 1 |
| Savage | Damage | Increased | 8–12% | 45–60% | 1 |
| Arcane | Damage *(Spell)* | Increased | 10–15% | 55–75% | 5 |
| Brutal | Damage *(Attack)* | Increased | 10–15% | 55–75% | 5 |
| Volatile | Damage *(any elemental)* | Increased | 9–14% | 50–68% | 12 |
| Vigorous | Maximum Life | Flat | 12–20 | 110–150 | 1 |
| Titanic | Maximum Life | Increased | 3–5% | 12–16% | 30 |
| Plated | Armour | Flat | 15–25 | 250–400 | 1 | *armour slots only* |
| Mighty | Strength | Flat | 3–6 | 35–50 | 1 |
| Agile | Dexterity | Flat | 3–6 | 35–50 | 1 |
| Studious | Intelligence | Flat | 3–6 | 35–50 | 1 |

### Slot restrictions **[DECIDED]**

`AllowedItemClasses` is empty (any slot) for every affix except two, where an
unrestricted roll would be nonsense rather than merely weak:

- **Plated** (flat Armour) — the five armour slots only: Offhand, Helmet, Body
  Armour, Gloves, Boots. Armour on a ring or a wand has no fiction behind it.
- **of Swiftness** (movement speed) — Boots only, the genre-standard home for it.

Everything else rolls anywhere its stat makes sense, which is nearly everything.
Restricting more affixes is a balance lever available later; restricting fewer is
not, once players have items.

Charged has a deliberately wider roll range than Blazing/Frozen at the same power
budget — a genre convention that gives lightning its own feel without a separate
mechanic.

## The Sigil pool **[PROPOSED]**

| Word | Modifies | Type | T1 | T7 | Min ilvl | Slots |
|---|---|---|---|---|---|---|
| of Vigor | Life Regeneration | Flat | 1–2/s | 14–20/s | 1 |
| of Warding | Fire Resistance | Flat | 6–10% | 40–46% | 1 |
| of Insulation | Cold Resistance | Flat | 6–10% | 40–46% | 1 |
| of Grounding | Lightning Resistance | Flat | 6–10% | 40–46% | 1 |
| of Resilience | All Resistances | Flat | 3–5% | 18–22% | 20 |
| of the Mind | Maximum Mana | Flat | 10–18 | 90–125 | 1 |
| of Haste | Attack Speed | Increased | 4–7% | 15–19% | 8 |
| of Focus | Cast Speed | Increased | 4–7% | 15–19% | 8 |
| of Recovery | Cooldown Recovery | Increased | 5–8% | 18–24% | 15 |
| of Efficiency | Mana Cost Multiplier | Increased *(negative)* | −4…−7% | −18…−24% | 12 |
| of Precision | Critical Strike Chance | Increased | 10–15% | 55–75% | 10 |
| of Ruin | Critical Strike Multiplier | Flat | 8–12% | 30–38% | 20 |
| of Swiftness | Movement Speed | Increased | 3–5% | 12–15% | 15 | *Boots only* |
| of Thorns | Thorns Damage | Flat | 3–6 | 40–65 | 5 |

**of Precision is *Increased* crit chance; of Ruin is *Flat* crit multiplier.**
Swapping those two is the classic ARPG balance bug — increased-multiplier scales
catastrophically, flat chance does nothing at low base.

**of Efficiency** is a negative Increased modifier on a multiplier stat with base
1.0, rather than a "Reduced" modifier type. `ModifierType` has three members and
reduction is just negative increase; adding a fourth would be a synonym.

**of Resilience** writes to three stats from one affix. It is the only entry that
does, and it is why `AffixDefinition` may eventually need a list of
`(StatId, ModifierType)` pairs rather than one — see Open Questions.

## Thorns needs a recursion guard **[DECIDED]**

Thorns returns damage to the attacker when hit. Two thorns-carrying entities
hitting each other recurse. **Reflected damage never itself reflects** — the
returned payload is stamped so `Health` skips the thorns step for it. This is not
an optimisation; without it a pair of rare monsters can hang the game.

## Name generation **[DECIDED]**

| Rarity | Name |
|---|---|
| Common | base name — "Iron Wand" |
| Magic | Temper + base + Sigil — "Blazing Iron Wand of Vigor" |
| Rare | generated two-word name + base — "Corpse Bite, Iron Wand" |
| Majestic / Set | authored fixed name |

A Rare rolls 3–4 affixes but has only one Temper and one Sigil slot's worth of
name, so it takes a generated name from an authored word pool instead, decoupled
from its affixes. The pool itself is not authored yet.

## Roll procedure **[DECIDED]**

1. Pick the base item; its `AllowedAffixTags` and item class filter the pool.
2. Roll affix count from rarity (Common 0, Magic 1–2, Rare 3–4, Majestic/Set 4).
3. Split within the 2 Temper / 2 Sigil cap.
4. For each pick: filter to affixes whose `MinIlvl` ≤ item level, weight by
   `Weight` and the tier weight-decay from `items.md`, choose without replacement
   **by stat**, so one item never rolls the same stat twice.
5. Roll a tier per affix, then a value within that tier's range.
6. Build the `ItemDescriptor`, including `AffixTiers` and `HighestAffixTier` so
   filters can act on them.

Step 4's "without replacement by stat" is what stops "Vigorous … of Vigor" style
near-duplicates that the pool split alone does not prevent.

## Open questions

1. **Affix values are uncalibrated.** There is no character leveling system — no
   XP, no level — and `MaxLife` base is a flat 50 placeholder. The numbers above
   assume a curve that does not exist: base life ≈ 40 + 8/level (≈840 at 100),
   base mana ≈ 40 + 4/level. Every value here rescales when leveling is designed.
2. **Multi-stat affixes.** "of Resilience" writes three resistances. Either
   `AffixDefinition` carries a list of `(StatId, ModifierType, values)` or
   all-resistance affixes are special-cased. The list is more honest; deferred
   until a second multi-stat affix exists to justify it.
3. **The Rare name word pool** is not authored.
4. **Weights are not set.** Every affix currently has equal weight, which will
   make the pool feel flat. Weighting is a balance pass, not a design one.
5. **A fourth resistance is reserved but undesigned.** See `stats.md` →
   "Adding a damage type".
6. **Dexterity's derivation is temporary** — see above.
