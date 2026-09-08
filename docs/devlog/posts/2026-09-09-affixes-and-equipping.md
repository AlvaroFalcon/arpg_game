# Two tests that passed while describing the wrong game

The [last post](2026-09-08-items-and-currency.md) shipped the drop pipeline
with currency alone, deliberately: prove the seam with gold piles before
building anything equipment-shaped on top of it. This one is the payoff —
base items, the affix system, and equipping — and it isn't a story about a
pipeline holding up. It's a story about two tests that were airtight about
the code they were checking and quietly wrong about the game underneath it,
back to back, in the same shape both times.

## Why the stat system had to be rebuilt before affixes could exist

Before any of this, `ActorStats` had five stats — `MovementSpeed`, `Damage`,
`AttackSpeed`, `CastSpeed`, `CooldownRecovery` — and none of them was Maximum
Life. `docs/design/items.md` needed affixes to attach to *something*, and a
4-affix budget split 2 Temper / 2 Sigil against five stats would have rolled
near-duplicate items on almost every Rare. There just wasn't enough surface
area to homogenize.

So `docs/design/stats.md` got a second pass before a single affix existed:
Life and Mana maximums became real modifiable stats instead of plain floats
on `ResourcePool` (the single most common affix in the genre, "+X to Maximum
Life," was previously inexpressible), damage gained four types so mitigation
could exist, and a defensive layer landed — Armour via PoE's
`armour / (armour + 10 * damage)`, resistances capped at 75% with overcap
tracked rather than discarded, crit rolling and applying *before*
mitigation so armour is meaningfully worse against crit builds. A third pass
right after that added the three attributes. The first pass on this system,
written back when movement was the only thing that needed a stat, predicted
a keyed registry would be "relevant well before fifteen." It landed at
eighteen, then twenty-three.

That's the part worth sitting with: `ActorStats` went from five named
properties to a `Dictionary<StatId, Stat>` registry specifically *because*
itemization needed room to breathe, not because the stat system outgrew
itself on its own timeline. The named accessors stayed —
`stats.Armour.Value` still reads exactly like it always did — but underneath,
`StatId` is now a real value that can be passed to `GetModifiers(query)`,
which named C# properties never could be.

One detail from that pass is worth calling out on its own, because it comes
back later in a different guise: `StatId`'s values are pinned explicitly
rather than left to implicit ordinals.

```csharp
public enum StatId
{
    MovementSpeed = 0,
    Damage = 1,
    // ...
    MaxLife = 5,
    // ...
}
```

A `StatBlock .tres` serialises this enum as its integer — `Id = 5`, not
`Id = "MaxLife"` — so inserting a member anywhere but the end would silently
rebind every already-authored StatBlock to the wrong stat. A dummy's "105
maximum life" becomes "105 life regeneration," with no error and no crash,
just numbers quietly landing on the wrong meaning. `EquipmentSlot` picked up
the identical rule a session later for the identical reason.

## Temper and Sigil, and the cap that's the whole point

`docs/design/affixes.md` splits the pool in two: **Temper** supplies an
item's leading name word ("*Blazing* Iron Wand"), **Sigil** the trailing
clause ("Iron Wand *of Vigor*"). The terminology swap from the genre-standard
prefix/suffix is cosmetic — the mechanic underneath is identical to every
ARPG that's shipped this. What isn't cosmetic is the cap: a 4-affix budget is
**max 2 Temper + 2 Sigil**, and that cap is the entire reason two pools exist
instead of one flat list. Without it, a Rare could roll four flavours of
"more damage" and read as a worse item than a Magic with one well-chosen
affix.

`AffixRoller.Roll` enforces the split per-pick rather than as a separate
up-front partition — a candidate is only eligible if its pool still has room
— and enforces something else at the same time that the design doc calls out
as the real anti-duplication mechanism: selection is **without replacement
by stat**. One item never rolls the same stat twice, which is what actually
stops "Vigorous … of Vigor"-style near-duplicates; the pool split alone
wouldn't have caught that, since Vigorous is Temper and of Vigor is Sigil.

## Adding an affix is a `.tres` file, and nothing else

The property the whole authoring shape exists to protect: an affix
targeting an existing stat costs zero code and zero recompiles.

```csharp
[Export] public string Id = "";
[Export] public AffixPoolKind Pool = AffixPoolKind.Temper;
[Export] public StatId Stat = StatId.Damage;
[Export] public ModifierType Type = ModifierType.Flat;
[Export] public SkillTags Condition = SkillTags.None;
[Export] public SkillTags ConditionAny = SkillTags.None;
[Export] public int MinIlvl = 1;
[Export] public float ValueAtT1Min, ValueAtT1Max, ValueAtT7Min, ValueAtT7Max;
[Export] public Array<AffixTierOverride> TierOverrides = new();
[Export] public Array<ItemClass> AllowedItemClasses = new();
```

`AffixRoller` has exactly one hardcoded per-affix branch in the whole
system — `IsAllResistances`, the escape hatch for "of Resilience," the one
affix that writes three stats (Fire, Cold, and Lightning Resistance) from a
single roll. It's called out explicitly in the code as *the* deliberate
exception, which is a useful way to author a rule: not "nothing is
special-cased," but "here is the one thing that is, and here's why."

The result: 28 affixes exist today as data. Eighteen of them target a stat
directly with a plain `Flat`/`Increased` modifier and needed literally no
code beyond the `.tres` file itself.

## The tier-gate bug: correct about the code, wrong about the game

This is the bug worth the most attention, because the test that should have
caught it didn't, and it's worth being precise about *why* it didn't.

Affixes have seven tiers. Tiers 2–6 interpolate their value range between the
authored T1 and T7 numbers — nothing surprising there. What's less obvious is
that the *unlock gate* for each tier has to interpolate too, and the first
version of `AffixDefinition` didn't do that. It gated every tier with a
uniform step: `MinIlvl + (tier - 1) * IlvlStep`, with `IlvlStep` fixed at 17
for every affix regardless of where it started.

`AffixSmokeTest` had an assertion for exactly this:

```csharp
Check("TIER-GATE-BELOW-MINILVL-IS-ZERO", ofRuin.MaxReachableTier(1) == 0, ...);
```

and, in the earlier version, something like `MaxReachableTier(122) == 7` for
an affix seeded at a representative `MinIlvl`. It passed. It was *supposed*
to pass — the arithmetic was internally consistent, `MinIlvl + 102` really
does equal the number the test expected. The problem is what that arithmetic
implies for every affix that doesn't start at `MinIlvl` 1: with a flat
17-level step, tier 7 gates at `MinIlvl + 102`. Item level caps at 120. So
any affix introduced above item level 18 has a T7 gate past 120 — which
means, inside the actual 1–120 range the game runs in, that top tier can
never be rolled. Not rarely. Never.

Titanic (the Increased-Maximum-Life Temper affix) is seeded at `MinIlvl` 30.
Its T7 gate under the old formula was 132 — twelve levels past the ceiling
of the entire item-level range. Every Titanic affix that ever dropped would
have quietly capped at T6 forever, and nothing in the test suite would have
said so, because the test was checking the formula against itself, not
against the fact that item level tops out at 120. A top roll a player can
never reach is strictly worse than not authoring the tier at all — it's a
number in the design doc that lies.

The fix interpolates the gate itself, from `MinIlvl` to the fixed maximum of
120:

```csharp
public int MinIlvlForTier(int tier) =>
    MinIlvl + Mathf.RoundToInt((tier - 1) * (MaxItemLevel - MinIlvl) / 6f);
```

Tier 1 gates at `MinIlvl`, tier 7 always gates at exactly 120, and everything
in between is spaced proportionally — which means a *late*-unlocking affix
climbs its own tiers faster than an early one, rather than every affix
marching in identical 17-level lockstep regardless of where it starts. That
reads, correctly, as the intended meaning of "seven tiers over item level
1–120": every affix gets the full seven tiers across the range it's actually
eligible for, not seven tiers plus a dead zone nobody can reach.

## Seam 4 finally switches on

`StatModifier.Condition` was specified back when the skill system was
designed, as an all-of tag requirement — right for "increased Fire Spell
Damage" (needs Fire *and* Spell), wrong for "increased Elemental Damage"
(needs Fire *or* Cold *or* Lightning). Both are ordinary affixes in the pool,
so `StatModifier` picked up a second field, `ConditionAny`:

```csharp
public bool AppliesTo(SkillTags tags)
{
    bool allOf = (Condition & tags) == Condition;
    bool anyOf = ConditionAny == SkillTags.None || (ConditionAny & tags) != 0;
    return allOf && anyOf;
}
```

The more interesting fact isn't the new field, it's what got activated
alongside it: `ActorStats.GetModifiers` had been ignoring `query.Tags`
entirely since the method was first written — every modifier on a stat
matched every query, unconditionally. That wasn't a bug sitting undiscovered;
it was *dormant*, because nothing before this session had ever constructed a
conditional modifier for it to filter incorrectly. The affix pool is what
finally exercises the seam that had been sitting there, correctly shaped and
doing nothing, since the skill system's design pass. It's a nice case of a
seam earning its keep on a delay rather than being wasted effort.

## A positive control, because a passing exclusion test can lie too

"of Resilience" grants all three elemental resistances from one affix. That
makes it naturally exclusive with the three single-element resistance
affixes — if it rolls, none of Fire/Cold/Lightning Resistance should also
roll on the same item, since it already covers all three. `AffixSmokeTest`
checks this as `RESILIENCE-EXCLUSIVE-WHEN-PRESENT`, and it's paired
deliberately with a second check:

```csharp
Check("SINGLE-RESISTANCES-CAN-CO-OCCUR", sawTwoSinglesTogether,
    "positive control: two of the 3 single-element resistances (different stats) " +
    "must be able to roll together at least once across 100 tries — otherwise the " +
    "exclusion above could just be a blanket 'one resistance affix maximum' bug");
```

Without that second check, `RESILIENCE-EXCLUSIVE-WHEN-PRESENT` would pass
identically whether the roller correctly excludes only Resilience's
overlapping neighbours, *or* incorrectly excludes any second resistance
affix regardless of which stat it targets. Both bugs produce the same green
checkmark on the first assertion. The positive control is what tells them
apart — it fails specifically if the exclusion has silently over-broadened
into "at most one resistance affix, full stop," which is exactly the kind of
mistake that's easy to introduce while implementing the narrower rule and
easy for a single exclusion test to miss entirely.

## Equipping, and attributes that did nothing until you picked something up

The second bug of the session is the tier-gate bug's twin, structurally: a
test passed, the code did exactly what it was written to do, and the game
was still wrong.

`Equipment` is the equip/unequip component — sibling to `ActorStats`,
`Health`, and `SkillUser` the same way everything else here is composed. The
part worth showing is the order of operations inside `TryEquip`, because
it's the actual point of the milestone beyond "equipping exists at all":

```csharp
if (_slots.TryGetValue(slot, out EquipmentItem? previous))
{
    _stats.RemoveModifiersFromSource(previous);
    displaced = previous;
}

_slots[slot] = item;
ApplyItemModifiers(item);

_stats.RecomputeAttributeDerivations();
ClampResourcePools();
```

The displaced item's modifiers come off, the new item's go on, and *only
then* does the attribute recompute run — so an item granting +20 Strength
feeds its derived Maximum Life and Armour bonus in the same call that
equipped it, not a physics frame later where a test (or a player glancing at
their character sheet) could catch it in an inconsistent intermediate state.

`RecomputeAttributeDerivations` itself, though, had a real bug: it was
called only from equip and unequip. That was a deliberate choice, made to
avoid disturbing test numbers that were already calibrated against a
no-attributes baseline. It was also backwards — bending the game to fit the
tests rather than the other way around. The actual effect in play: a fresh
player spawned with 50 Maximum Life instead of 70, because base Strength 10
grants +2 Life per point and nothing was calling the recompute until the
first item went on. Worse, two otherwise identical actors carried different
stats purely depending on whether *either* had ever equipped something, once,
at any point — a piece of history leaking into a value that should be a pure
function of current stats.

The fix is one call, in `ActorStats._Ready`, placed before the resource
pools are constructed (they seed `Current` from `Max`'s *value*, so deriving
afterward would spawn every actor at partial life):

```csharp
RecomputeAttributeDerivations();

Life = new ResourcePool(Get(StatId.MaxLife), Get(StatId.LifeRegen), Get(StatId.MaxLife).Value);
```

The interesting decision isn't the fix, it's what happened to the tests that
had been calibrated against the wrong baseline. They got updated, not the
game. Target dummies picked up an explicit `NoAttributes` StatBlock so their
authored life totals ("105 life") mean exactly what their names claim rather
than 105 plus a hidden Strength-derived offset; three assertions were
inverted with detail strings explaining what changed, rather than deleted,
so they keep functioning as tripwires against the old behaviour reappearing.
The Thorns test in particular had drifted furthest — it had quietly started
measuring armour mitigation on top of the reflected damage instead of the
reflected damage alone, because the attacker and target both carried an
undocumented derived-armour offset the original numbers never accounted for.

Both bugs — the tier gate and the lazy derivation — share the same shape: a
test asserted something true of the code as written, and the code as written
just happened not to match what the game needed to be true. Neither was
caught by a failing test; both were caught by tracing what the number would
actually mean in play and noticing it didn't add up.

## Deliberately temporary, and labelled as such

Two decisions in this milestone are explicitly not the final shape, and are
marked that way in both the design doc and the code so a future session
doesn't mistake scaffolding for a commitment.

**Dexterity grants Critical Strike Chance.** It should grant Evasion and
Accuracy, but neither exists yet — evasion is a second complete mechanic
(an accuracy stat, an evasion stat, a hit-chance roll on every attack, and an
entropy-vs-pure-random decision about how misses distribute) that
`docs/design/stats.md` defers on purpose rather than rushing. Shipping
Dexterity with no grant at all would make it a dead stat players learn to
skip past, so it borrows crit chance until evasion lands, at which point this
one derivation moves and nothing else in the codebase is allowed to have
grown a dependency on Dexterity granting crit.

**Auto-equip on pickup** is the equivalent bridge on the equipment side.
There's no grid inventory yet, so walking over a dropped item auto-equips it
into the first free matching slot rather than going into an inventory that
doesn't exist. If every valid slot is already full, the item is left exactly
where it is — still visible, still pickable later — rather than vanishing or
displacing something worn. `EquipmentItem.OnPickup`'s own doc comment calls
this "a milestone 2b bridge, not a design decision," and names the exact
thing that replaces it: when the grid inventory lands, this method's body
goes back to "into the inventory," full stop.

## What's still unfinished, honestly

Affix values are uncalibrated, and can't be calibrated yet — there's no
character leveling system, no XP, no level, and `MaxLife`'s base is a flat
50 placeholder. Every number in `docs/design/affixes.md` assumes a life/mana
curve that doesn't exist and will rescale once one does. The grid inventory
doesn't exist, which is why auto-equip is standing in for it. Set bonuses and
Majestic effects are both designed on paper — Majestic as a `CastModifier`
behaviour transform, Set as a field on `Rarity` rather than a separate
axis — but neither has a line of code yet.

There's also no equipment screenshot in this post. The three images
attached to the last one are all currency drops, and there's no
equipment-specific capture tool built yet — everything here was verified
through `EquipSmokeTest`'s console output and `DebugReadout`'s live numbers,
not through a rendered frame. Capturing one would need a windowed run (never
`--headless` — the dummy renderer produces an empty image, a lesson this
project has already paid for twice) showing a rolled item on the ground, the
pickup, and the stat readout changing in response, the same shape the
existing `SkillCapture`/`DropCapture` tools already use.

## What's next

Milestone 2c: the grid inventory, which is what actually retires auto-equip
rather than just wrapping it more carefully. After that, the weighted drop
table with a real `DropContext` — monster level, area level, magic find —
so equipment stops arriving from a single hardcoded roll call and starts
arriving from something resembling a loot table.
