# Stat System

Status legend, matching `docs/design/skills.md`:
- **[DECIDED]** — settled, implement as written.
- **[PROPOSED]** — recommendation awaiting the project owner's confirmation.

> **Second pass note.** The first pass built the modifier maths for one stat
> (`MovementSpeed`) and left everything else open. This pass was forced by
> `docs/design/items.md`: affixes need somewhere to attach, and with five stats —
> none of which is Maximum Life — a 4-affix budget split 2 Temper / 2 Sigil rolls
> near-duplicates on every Rare. Changes, in order of how much they move:
>
> 1. **`ActorStats` becomes a keyed registry.** The first pass said "revisit if the
>    property list gets unwieldy… relevant well before fifteen." This pass lands
>    around eighteen. See [The registry](#the-registry-decided).
> 2. **`Life`/`Mana` maximums become modifiable stats.** They were `ResourcePool`
>    fields with no modifier stack, which made "+40 to Maximum Life" — the single
>    most common affix in the genre — inexpressible. See
>    [Resource pools](#resource-pools-become-stat-backed-decided).
> 3. **Damage gains types, and `DamagePayload` stops being one float.** Mitigation
>    can't exist without knowing what kind of damage arrived. See
>    [Damage types](#damage-types-decided).
> 4. **A defensive layer**: Armour for physical, per-element resistances with a cap
>    and overcap, and penetration as the counterplay. See
>    [Mitigation](#mitigation-decided).
> 5. **Critical strikes.** See [Crit](#critical-strikes-decided).
> 6. **Evasion/accuracy and leech are explicitly deferred**, with reasons. See
>    Open Questions.
>
> One thing this pass explicitly does *not* change, because the first pass got it
> right: **"+30% increased Fire Damage" is not a stat.** It is a `StatModifier` on
> `Damage` carrying `Condition = SkillTags.Fire`, resolved through the existing
> `ModifierQuery.Tags`. Seam 4 was built for this and it works — adding
> `FireDamage`, `ColdDamage`, … as separate stats would duplicate the whole
> conditional-modifier mechanism badly.

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

## The registry **[DECIDED]**

`ActorStats` stores stats in a `Dictionary<StatId, Stat>` and exposes
`Stat Get(StatId id)`. The named properties stay, but become thin accessors over
the registry:

```csharp
public Stat MovementSpeed => Get(StatId.MovementSpeed);
```

Why both: the registry is what `GetModifiers(query)` needs (a stat identifier has
to be passable as a value), while the named accessors keep existing call sites
like `_stats.MovementSpeed.Value` readable and typo-proof, and cost one line each.
This is the compromise the first pass deferred; at eighteen stats the storage has
to be keyed, but nothing forces the *call sites* to become stringly-typed.

### Base values move to a `StatBlock` resource

One `[Export] float BaseX` per stat does not survive eighteen stats, and Godot
4.1 cannot export a dictionary keyed by an enum. Base values come from a
`StatBlock` Resource — an array of `(StatId, float)` entries — assigned per entity
archetype:

```
StatBlock : Resource
  Entries[] : { StatId Id, float BaseValue }
```

`ActorStats` keeps `[Export] StatBlock? Bases`, falls back to per-stat defaults
for anything unlisted, and the existing `Faction` / `IgnoresResourceCosts`
`[Export]`s stay as they are. A side benefit worth having: an enemy archetype
becomes a `.tres` rather than a hand-tuned scene, which is what the "no enemy
scene exists yet to prove ActorStats is entity-agnostic" open question needs.

## Resource pools become stat-backed **[DECIDED]**

`ResourcePool.Max` stops being a settable float and becomes a read of a backing
`Stat`:

| Pool | Max stat | Regen stat |
|---|---|---|
| Life | `MaxLife` | `LifeRegen` |
| Mana | `MaxMana` | `ManaRegen` |

`Current` stays a plain mutable float on the pool — it is state, not a modifier
stack.

### What happens to `Current` when `Max` changes

**`Current` is preserved absolutely and clamped to the new `Max`.** Equipping
+40 Maximum Life does not heal you; unequipping it clamps you down, and can leave
you at full life but lower.

Rejected: preserving the *percentage*. It reads as friendlier, but it means
swapping a life roll in and out is a free heal, and any gear-swap macro becomes a
healing skill. D2 and PoE both preserve absolute current for exactly this reason.

The clamp on unequip must never kill: it floors at 1, not 0. Dying from taking
off a hat is a bug report, not a mechanic.

## Damage types **[DECIDED]**

Four: **Physical, Fire, Cold, Lightning**.

`SkillTags` gains `Lightning` (it already has `Fire` and `Cold`). `Physical` is
the untyped default and needs no tag.

**Chaos is deliberately excluded.** In PoE, Chaos exists mainly to have a damage
type that bypasses Energy Shield; with no ES in this project it would be a fifth
colour with no mechanical identity. Adding it later costs one enum entry and one
resistance, so there is nothing to buy by adding it now.

Lightning earns its slot rather than padding: three elements is the genre-standard
triangle, and it leaves room for a shock-style ailment to be distinct from
ignite/chill when ailments are designed.

### `DamagePayload` carries a breakdown, not a float

```csharp
public sealed class DamagePayload
{
    IReadOnlyList<(DamageType Type, float Amount)> Components { get; }
    float Total { get; }        // computed, keeps damage numbers and existing callers working
    ...
}
```

Mitigation cannot be written without knowing what arrived, and a single float
forces every mitigation decision to be made at the *source* instead of at the
target — which is backwards, since the target owns armour and resistances.

`Total` staying available means `TargetDummy`'s floating damage numbers and the
existing tests keep working unchanged.

## Mitigation **[DECIDED]**

Applied in `Health.TakeDamage`, which is the right owner: the target knows its own
defences, and this keeps every skill ignorant of who it is hitting.

### Physical: Armour

```
reduction = armour / (armour + 10 * physicalDamageOfThisHit)
```

PoE's formula. Chosen over the alternatives because it is the only one that stays
sane without a hard cap: armour is strong against many small hits and weak against
single large ones, which is a real defensive-build decision rather than a number
that either does nothing or trivialises everything.

Rejected: **flat damage reduction** (unbounded — enough of it makes you immune to
whole damage brackets), and **D2's AC-to-hit** (that is an evasion mechanic wearing
armour's name, and evasion is deferred — see Open Questions).

### Elemental: resistances

```
effective  = clamp(resistance - penetration, floor: -100%, cap: 75%)
taken      = amount * (1 - effective)
```

- **Cap 75%**, the genre standard.
- **Overcap is tracked, not discarded.** A character at 120% Fire Resistance shows
  75% effective and 45% overcap. Penetration and future resistance-lowering effects
  eat the overcap first. Without this, resistance above the cap is worthless and
  players cannot prepare for content that lowers it — PoE's entire "cap your
  resists, then overcap them" gearing loop depends on this being visible.
- **Resistance can go negative**, floored at −100% (taking double damage). Floored
  so a stack of penetration can never invert damage into healing.
- **Penetration is per element** and is the counterplay to resistance. It applies
  at hit time and is not itself capped.

## Critical strikes **[DECIDED]**

- `CritChance` — base 5%, additive percentage.
- `CritMultiplier` — base 150%, a multiplier on the whole hit.

Rolled once per hit, applied **before** mitigation, and stamped onto the payload so
damage numbers and future on-crit effects can read it. Applying crit before
mitigation is what makes armour meaningfully worse against crits, which is the
interaction that gives crit builds and armour stacking something to say to each
other.

## Attributes and derived bonuses **[DECIDED — third pass]**

Three attributes, base 10 each: **Strength, Dexterity, Intelligence**. They are
ordinary registry stats, and they grant derived bonuses — an attribute that is
only a number is a filler affix players learn to ignore.

| Attribute | Grants, per point |
|---|---|
| Strength | +2 Maximum Life, +1 Armour |
| Dexterity | +0.5% increased Critical Strike Chance *(temporary)* |
| Intelligence | +2 Maximum Mana, +0.1 Mana Regeneration |

Dexterity should grant **Evasion and Accuracy**, both of which this document
defers. Rather than ship a dead stat, it grants crit chance until evasion lands,
at which point the derivation moves and the crit association is dropped. Nothing
else may come to depend on Dexterity granting crit.

Derivations are recalculated at known moments (equip, unequip, anything that can
change an attribute) and applied as ordinary `StatModifier`s under a shared
attribute-derivation `Source`, so recompute is "remove by source, re-add".
`Stat` has no change notification, so a live formula is not available; this reuses
`RemoveModifiersFromSource` rather than adding a mechanism.

## Adding a damage type **[DECIDED — third pass]**

A fourth element is reserved and undesigned. Keeping it cheap is a structural
requirement, not a hope.

`DamageMitigation.MitigateElemental` already takes plain floats and knows nothing
about which element it is mitigating, so the maths needs no change. The one
hardcoded place is the per-type `switch` in `Health.TakeDamage`. **That switch
becomes a table** — `DamageType → (resistStatId, penetrationStatId)` — so a new
element costs: one `DamageType` entry, two `StatId`s, one table row, and one affix
`.tres`.

Physical stays outside the table: it mitigates through Armour, not a
resistance/penetration pair.

## Condition semantics: all-of and any-of **[DECIDED — third pass]**

Seam 4 specified `StatModifier.Condition` as a *required-tag set* — all-of. That
is right for "increased Fire Spell Damage" (Fire **and** Spell) and wrong for
"increased Elemental Damage" (Fire **or** Cold **or** Lightning). Both are ordinary
affixes, so `StatModifier` gains a second field:

```
applies = Condition ⊆ tags
          AND (ConditionAny == None OR (ConditionAny ∩ tags) ≠ ∅)
```

Two fields rather than a mode flag, because one affix can need both at once
("increased Elemental Spell Damage").

**`ActorStats.GetModifiers` must now actually apply this.** It currently returns
every modifier on the stat and ignores `query.Tags` — dormant rather than broken,
since nothing constructs a conditional modifier yet. Implementing it is what
switches seam 4 on for the first time.

## The stat list after this pass **[DECIDED]**

| Stat | Base | Notes |
|---|---|---|
| `MovementSpeed` | 130 | px/s |
| `Damage` | — | modifier carrier; base comes from the skill |
| `AttackSpeed` | 1.0 | multiplier, tag-derived per skill |
| `CastSpeed` | 1.0 | multiplier, tag-derived per skill |
| `CooldownRecovery` | 1.0 | divisor |
| `MaxLife` | 50 | backs the Life pool |
| `LifeRegen` | 0 | per second |
| `MaxMana` | 50 | backs the Mana pool |
| `ManaRegen` | 5 | per second |
| `Armour` | 0 | physical mitigation |
| `FireResistance` | 0 | cap 75%, floor −100% |
| `ColdResistance` | 0 | |
| `LightningResistance` | 0 | |
| `FirePenetration` | 0 | uncapped |
| `ColdPenetration` | 0 | |
| `LightningPenetration` | 0 | |
| `CritChance` | 0.05 | |
| `CritMultiplier` | 1.5 | |
| `Strength` | 10 | +2 life, +1 armour per point |
| `Dexterity` | 10 | temporary derivation, see above |
| `Intelligence` | 10 | +2 mana, +0.1 mana regen per point |
| `ManaCostMultiplier` | 1.0 | reduced by negative Increased modifiers |
| `ThornsDamage` | 0 | flat physical returned when hit |

Twenty-three after the third pass; the first eighteen. That is the number that forces the registry, and it is also roughly the
number of distinct affixes `docs/design/items.md` needs before a 2 Temper / 2 Sigil
budget stops rolling duplicates.


## Fourth pass: run stats **[DECIDED — implemented]**

The survivor run (docs/design/run.md, docs/design/progression.md) appended 14
stats, values 23–36, following this enum's rule of never renumbering:

| Stat | Base | Consumed by |
|---|---|---|
| DamageTaken | 1.0 | `Health.TakeDamage`, after mitigation (keystones, shock) |
| PickupRadius | 80 | `Run/Pickup` |
| ExperienceGain | 1.0 | `RunState.AddExperience` |
| AreaOfEffect | 1.0 | area and orbit executors |
| ProjectileSpeed | 1.0 | projectile executor |
| DodgeCharges | 2 | `Dodge` |
| DodgeRecovery | 1.0 | `Dodge` |
| ExtraProjectiles | 0 | projectile and orbit executors |
| IgniteChance / ShockChance | 0 | hit profiles → `Ailments` |
| SkillDuration | 1.0 | orbit executor |
| LifeOnKill | 0 | `RunState` on kill |
| ExtraChains / ExtraPierce | 0 | projectile executor |

The multiplier-shaped ones are base 1.0 so "no bonuses" is a no-op, the same
convention ManaCostMultiplier uses. None of them is rolled by an affix yet; adding
one is a single affix `.tres`.

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

### Open questions added by the second pass

- **Evasion and accuracy — deferred, not rejected.** It is a second complete
  mechanic: an accuracy stat, an evasion stat, a hit-chance roll on every attack,
  and a decision about entropy-based versus pure-random hit determination (PoE uses
  entropy specifically so players do not experience long miss streaks). It also puts
  RNG in front of every hit, which interacts with everything else in this document.
  Armour plus resistances is enough defensive layering to build gear on; add this
  only if defences feel one-dimensional in play.
- **Leech — deferred.** Instant leech is degenerate and every game that shipped it
  later replaced it with a rate. Doing it correctly means a leech-rate system with
  per-instance caps, which is its own design pass. Flask charges already cover
  sustain for now.
- **Ailments (ignite / chill / freeze / shock) — not designed.** The `Duration` tag
  and `DamageDeliveryKind.OverTime` exist and are the seams these will use. Note
  that the Hoarfront majestic in `docs/design/items.md` ("area skills leave chilled
  ground") assumes a chill exists, so this blocks that item, not the milestone.
- **Energy Shield — not planned.** Its absence is why Chaos damage is excluded; if
  ES is ever added, revisit that together.
- **Damage conversion** (physical → fire, etc.) — not designed. It is a
  `CastModifier`-shaped transform on the payload rather than a stat, so it belongs
  with the skills seams, not here.
- **Does `Stat` need to compute against an externally supplied base?** The first
  pass noted that four of five stats were "modifier carriers with no base of their
  own". After this pass the ratio inverts — most stats have real bases — which
  weakens that pressure rather than strengthening it. Left as-is.

### Open questions added by the third pass

- **Thorns needs a recursion guard.** Reflected damage must never itself reflect,
  or two thorns-carrying entities hitting each other will hang the game. The
  returned payload is stamped so `Health` skips the thorns step for it.
- **Nothing is calibrated.** There is no character leveling system — no XP, no
  level — and `MaxLife` base is a flat 50 placeholder. `docs/design/affixes.md`
  assumes a curve that does not exist (base life ≈ 40 + 8/level). Every value in
  both documents rescales when leveling is designed.
