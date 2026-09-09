# Items, Loot & Drops (first pass)

Status legend, matching `docs/design/skills.md`:
- **[DECIDED]** — settled, implement as written.
- **[PROPOSED]** — recommendation awaiting the project owner's confirmation. Repeated
  under Open Questions.

## What it is

Everything that can hit the ground and be picked up, plus the data model behind it.
Three drop families in the first pass — **currency**, **equipment**, **flask refill
orbs** — sharing one drop/label/filter pipeline and diverging only at pickup.

The whole design is built backwards from one constraint: **an in-game item filter is
coming later**, and a filter can only act on fields it can see at drop time.
Everything below either feeds the filter's vocabulary or stays deliberately out of
its way.

## Rarity **[DECIDED]**

Five tiers. Colour is the *default* presentation, produced by the built-in filter
(see [Item filter](#item-filter-decided)) — it is never hardcoded in the label node.

| Rarity | Colour | Affixes | Extra |
|---|---|---|---|
| Common | White | 0 | base stats only |
| Magic | Blue | 1–2 | |
| Rare | Yellow | 3–4 | |
| Majestic | Pink | 4 | + 1 majestic effect |
| Set | Orange | 4 | + set bonuses at 2 and 4 pieces |

### Why five, and why the top is a fork not a ladder

An earlier pass proposed seven (adding Junk/grey and Uncommon/green). Both were cut:
a tier only earns its place if the player *does something different* when they see
that colour, and Junk/Common both meant "ignore", Uncommon/Magic both meant "one or
two affixes". Reference points: Diablo II has ~5 meaningful tiers, PoE has 4. Junk
could return if a salvage/crafting sink is ever added to give it a job — without a
sink it is a tier whose only purpose is to be filtered out.

Majestic and Set are **siblings, not steps**. Majestic is solo power (one item, one
build-defining effect). Set is commitment power (weak alone, strong at 4 pieces).
This mirrors D2's unique-vs-set split and it works. Two consequences to hold onto:

- Set drop weight cannot be derived from "it's further along the ladder" — weight it
  by how central set-hunting should be to the endgame loop.
- **Set pieces are supposed to feel underwhelming in isolation.** That's the design
  paying for the 4-piece payoff, not a tuning bug.

**An item is never both Set and Majestic.** Otherwise a 4-piece set carrying four
majestic effects is the only build in the game.

## Sets **[DECIDED]**

Set membership is a *field*, not a separate axis:

```
Rarity  → Common | Magic | Rare | Majestic | Set     (colour + affix budget)
SetId?  → null, or which set this piece belongs to
```

`SetDefinition` is a Resource holding the member list, the 2-piece bonus and the
4-piece bonus. The item just points at it.

- Set pieces roll **4 random affixes** like any other high-rarity item. Only the set
  bonuses are fixed.
- Bonuses at **2 and 4 pieces only**. No 3-piece bonus (D2 does partial-count
  bonuses; rejected here — it flattens the decision, since the 3rd piece stops being
  a real commitment step).

An earlier pass argued Set should be a separate archetype axis from rarity, on the
grounds that Set affixes would be authored rather than rolled. Because the owner's
model rolls them normally, that objection dissolves and the single-enum-plus-field
model is strictly simpler. Recorded because the reasoning matters if the model ever
changes.

## Affixes: Temper and Sigil **[DECIDED]**

Two pools, separate caps, exactly the mechanic every ARPG uses:

- **Temper** — supplies the leading word of the generated name ("*Blazing* Iron Wand")
- **Sigil** — supplies the trailing clause ("Iron Wand *of Vigor*")

A 4-affix budget means **max 2 Temper + 2 Sigil**. That cap is the only reason the
split exists: it stops an item rolling four variations of the same stat, and it gives
future crafting a granularity to work with ("add a Sigil", "reroll Tempers").

### Why not "prefix/suffix"

Mechanically identical; the terminology is a direct lift from PoE (and D2, Grim Dawn,
Last Epoch). Renamed for identity. *Temper* = worked into the metal, fits the leading
physical position. *Sigil* = marked onto it, fits the trailing magical position. Both
are single words that read correctly in UI copy: "Add a Temper", "No Sigil slots
remaining". Rejected alternatives: **Forge/Ward** (implies an offense/defense split
that doesn't exist — both pools carry both), **Brand/Bane** (good flavour, but "Bane"
reads as a drawback).

Rejected more broadly: **a single flat affix pool with per-category caps.** Same
anti-homogenisation outcome, but it needs a category taxonomy and cap table to
achieve what two pools give for free, and it has no natural hook for name generation.

### How affixes reach stats

An affix emits `StatModifier`s tagged with the item instance as `Source`.
`ActorStats.RemoveModifiersFromSource(item)` already exists in `docs/design/stats.md`
for precisely this — unequip removes every modifier the item granted without tracking
individual modifier objects. No new plumbing.

## Affix tiers and item level **[DECIDED]**

- **Item level range: 1–120.** Starts at 1, not 0 — a 0 sentinel is indistinguishable
  from an uninitialised item and will eventually ship as a bug where uninitialised
  drops roll bottom-tier only.
- **7 tiers per affix**, roughly 17 item levels per band.
- Item level comes from monster/area level at drop time, and is **immutable
  thereafter**.

Precedent: D2 gates affixes with `levelreq`/`maxlevel`; PoE runs ~8–10 tiers over
item level 1–86. Seven over 1–120 sits comfortably between them.

### Min-ilvl is a wall, max-ilvl is a slope

- **Minimum item level per tier is a hard gate.** T7 Fire Damage cannot appear below
  its threshold. This is the progression.
- **Low tiers are weight-decayed at high item level, not removed.** If every tier
  hard-cut on the way up, then at item level 120 only T7 could roll, every endgame
  drop would be perfect, and a perfect roll would stop being exciting because it had
  become the floor. The chase would die exactly where it should peak.
- **An explicit hard `MaxIlvl` per affix stays available**, used sparingly for
  affixes that genuinely should disappear (low-tier resistances, flat life on
  endgame bases).

Tuning target: at item level 120, roughly 60% of rolls land in the top two tiers.
Adjust the decay curve, not the tier count.

### Authoring: curve, not 7 hand-written rows

```
AffixDefinition : Resource
  Id, StatId, Pool (Temper | Sigil), Tags
  MinIlvl, IlvlStep            // or explicit per-tier gates
  ValueAtT1 (min,max), ValueAtT7 (min,max), Curve
  MaxIlvl?                     // optional hard cutoff
  TierOverrides[]              // optional, per-tier escape hatch
```

Tiers 2–6 interpolate. Hand-authoring 7 rows × affix count × item class is ~7x the
data entry and makes every rebalance a mass edit; the override array keeps full
control where a specific affix doesn't fit the curve.

### Tiers are player-visible

`AffixTiers[]` and `HighestAffixTier` go in the drop descriptor, and the tier shows
in the item tooltip. Two reasons: "show me items with any T1–T2 affix" is the single
most-wanted filter rule in the genre, and hiding tiers is a known mistake — PoE hides
them and effectively the entire playerbase installed third-party tools to read them.
D4 and Last Epoch show them natively.

## Base items **[DECIDED]**

```
BaseItemDefinition : Resource
  Id, DisplayName, ItemClass
  Width, Height
  BaseStats           // wand damage, armour value, ...
  RequiredLevel
  AllowedAffixTags    // which affixes may roll here
```

Base stats scale by base type — "Iron Wand" vs "Ebony Wand" — and affixes sit on top.
Without this, every wand is identical and the only progression is affix rolls. Small
to add now, ugly to retrofit once items exist in saves.

## Equipment slots **[DECIDED]**

Nine: **Weapon, Offhand, Helmet, Body Armour, Gloves, Boots, Belt, Amulet, Ring x2.**

Chosen over a leaner six because a 4-piece set has to leave room for the rest of a
build — at six slots a set consumes two thirds of the character and there is
nowhere left for a Majestic to matter. Chosen over a PoE-like eleven because every
extra slot is more base items to author and more drops needed before a build feels
filled out, and nothing in the design needs a quiver or gear-slotted flasks yet.

Two ring slots specifically: rings are the natural home for the small, stackable
affixes (resistances, flat damage), and having two of them is what lets resistance
capping be a solvable puzzle rather than a single unlucky slot.

## The first base items **[PROPOSED]**

One per slot. More bases are pure authoring once the rolling machinery works.

| Base | Slot | Cells | Base stats | Req |
|---|---|---|---|---|
| Iron Wand | Weapon | 1×2 | +3–6 Damage *(Spell)* | 1 |
| Oak Buckler | Offhand | 2×2 | 12 Armour | 1 |
| Leather Cap | Helmet | 2×2 | 8 Armour | 1 |
| Ringmail Vest | Body Armour | 2×3 | 30 Armour | 1 |
| Rawhide Gloves | Gloves | 2×2 | 6 Armour | 1 |
| Rawhide Boots | Boots | 2×2 | 6 Armour | 1 |
| Rope Belt | Belt | 2×1 | — | 1 |
| Copper Amulet | Amulet | 1×1 | — | 1 |
| Copper Ring | Ring | 1×1 | — | 1 |

Belt, amulet and ring carry no base stats deliberately — they are pure affix
carriers, which is genre-standard and gives them a distinct role.

**Weapon base damage is granted as a flat `Damage` modifier**, not read by attack
skills. `docs/design/skills.md` puts base damage on the *skill*, so a weapon's own
damage has no consumer today; granting a modifier works with what exists and needs
no new machinery. The PoE model — attack skills reading weapon damage — remains
available later and is a real path, not a patch on this one.

## Grid inventory **[DECIDED]**

- **Item data stores `Width` × `Height` ints.** Wand 1×2, body armour 2×2, two-hander
  2×4.
- **Grid algorithms operate on a boolean occupancy mask** (`bool[w,h]`) generated from
  that rect. Placement, collision, and rotation code never learns the word
  "rectangle".

That second point is the whole trick: non-rectangular items become pure data later,
with no algorithm rewrite, and it costs nothing today.

**Non-rectangular items are not shipping.** L-shapes (Resident Evil 4's attaché case,
Backpack Battles, Save Room) turn inventory into a puzzle minigame — excellent when
inventory Tetris *is* the game, bad in an ARPG where the player manages inventory
dozens of times an hour between packs. It also breaks auto-placement: "pick it up and
it finds a slot" is trivially solvable for rects and NP-annoying for arbitrary shapes.
D2 and PoE both stayed rectangular for exactly this reason.

## Inventory interaction **[DECIDED]**

The data and placement rules are above; this is how the player actually touches it.

- **Grid is 12x5** — 60 cells. A 2x3 body armour costs six, a 1x2 wand two, so a full
  bag is roughly 10-20 items. Matches PoE's dimensions, which is a well-tested
  size: big enough not to nag between packs, small enough that clearing it is a
  real decision.
- **Toggled with `i`** (a new `toggle_inventory` input action). The world keeps
  running behind it — this is an ARPG, not a menu game, and a pausing inventory
  encourages mid-fight sorting.
- **Pickup routes into the grid**, first free space in reading order. If nothing
  fits, the item stays on the ground rather than being destroyed or silently
  swallowed.
- **No rotation.** D2 and PoE both omit it; it doubles placement search for a
  cosmetic amount of extra packing.
- **No stacking.** Nothing stackable exists — currency is a wallet counter, not a
  grid item.

### Pickup is manual for items, automatic for currency **[DECIDED]**

Walking over a piece of equipment must **not** pick it up. Only currency
auto-collects, per each `CurrencyDefinition`'s own `AutoPickup` flag.

This was wrong in milestone 2c and it is a genuine design bug, not a preference:
auto-collecting gear fills a 60-cell grid with vendor trash within a couple of
packs, and it takes the decision of *whether this is worth carrying* — the whole
point of a limited grid — away from the player. Every ARPG with a grid inventory
makes item pickup deliberate for exactly this reason.

**Equipment is picked up by clicking it** — its ground label or its sprite, the
D2/PoE interaction. If the inventory has no room, the click fails and the item
stays where it is.

#### How this interacts with filters

`items.md`'s standing rule is that **filters control display, never behaviour**: a
hidden gold pile is still auto-collected. Manual pickup puts pressure on that rule,
because an item whose label is hidden has nothing obvious to click.

The resolution: **the item's sprite stays clickable even when its label is
hidden.** Hiding a label removes the reading clutter, not the item. A filter can
never make loot unobtainable — it can only make it quieter.

### Closing panels **[DECIDED]**

`Escape` closes the topmost open panel, and `i` still toggles the inventory
specifically. Any future panel joins the same stack rather than binding its own
close key.

### UI must consume its own input **[DECIDED]**

Clicks and drags over an open panel must not reach the world. Dragging an item
currently also issues a move order to the character underneath, which is a
straightforward input-routing bug: the movement controller reads the mouse without
first asking whether the UI wanted it.

The fix belongs in the input layer, not in the movement code guessing about UI
state — `Control` nodes consume what they handle, and the movement controller
ignores clicks the GUI has taken.

### Item tooltips **[DECIDED]**

Hovering an item — in the grid, in an equipment slot, or on the ground — shows its
name in its rarity colour, its base type, item level, and its rolled affixes with
their tier numbers.

Showing the tier is deliberate and matches `affixes.md`: PoE hides tiers and its
entire playerbase installed third-party tools to see them. A cell that currently
shows a colour and a clipped word ("Ringm", "Leath") is not enough to make a
keep-or-drop decision, which is the decision the inventory exists to pose.

### The equipment strip **[DECIDED]**

Nine labelled cells beside the grid, one per slot. Deliberately not a paper-doll
figure — that is art scope, and the strip conveys the same information.

It exists because without it the feature is half a loop: milestone 2b let items be
equipped but never taken off, so an inventory that could only equip would make
swapping one-way and unequipping impossible. Shipping a grid without somewhere to
drag out of would be shipping something the player cannot fully use.

- **Drag** between grid and slots, and within the grid to rearrange.
- **Right-click an item in the grid** equips it into the first valid free slot, or
  swaps with the occupied one — the shortcut that carries the common case, since
  dragging every upgrade across the screen gets old fast.
- **A displaced item returns to the grid space the incoming item vacated**, which is
  always free and always large enough, so a swap can never fail for lack of room.

Removes the milestone-2b auto-equip-on-pickup bridge, which existed only so gear was
usable before this screen existed.

## Currency **[DECIDED]**

Gold is instance 1 of N, built generically from the start:

```
CurrencyDefinition : Resource
  Id, DisplayName, Colour
  AutoPickup (bool)
  StackFormat        // "{0} Gold"

Player.Wallet : Dictionary<CurrencyId, long>
```

Currency is **never a grid item** — it's a counter, collected by walking over it.
Rejected: PoE's model where currency items occupy inventory slots and *are* the
crafting economy. That only pays for its cost if item-based crafting currency is the
economy; this project is taking the Diablo model.

`AutoPickup` is per-currency rather than a global rule, because a future crafting
currency may want manual pickup or stash routing, and discovering that after gold has
hardwired walk-over collection into the pickup path is a rewrite.

**Currency drops still get a ground label.** "Hide gold piles under 500" is a real
filter rule the player will want.

### Filters control display, never behaviour **[DECIDED]**

A hidden gold pile is still auto-collected on walk-over. The moment hiding something
also stops the player getting it, filters become a trap that silently costs loot for
reasons the player can't see. PoE holds this line strictly; so does this project.

## Flasks and refill orbs **[DECIDED]**

Flasks hold charges. Charges come from two sources:

1. **Refill orbs** that drop from kills — walked over, restore N charges.
2. **Passive regeneration over time.**

Orbs restore *charges*, not health. The player still chooses when to quaff. Rejected:
D3's health globes, which healed directly on contact and thereby deleted the potion
decision entirely.

Two constraints:

- **An orb is not consumed when the flask is already full.** Auto-pickup is suppressed
  at max charges and the orb stays on the ground; otherwise orbs are wasted by walking
  through a fight.
- **Time regeneration must be slower than sustained combat drain.** If passive regen
  alone keeps the flask topped up, orbs are worthless and the flask stops being a
  resource decision.

## Majestic effects **[DECIDED — shape]**

A majestic effect is a **behaviour transform**, not a numeric modifier, so it routes
through Seam 6 of `docs/design/skills.md` — it is a `CastModifier` subclass. That seam
already exists in code (`scripts/Skills/CastModifier.cs`) with a `Priority` ordering
field and a `Condition => SkillTags` predicate, and `SkillUser.TryCast` already runs
the collect-and-apply stage over an empty list. Majestic is that seam's first
consumer; no new machinery.

Rules:

- **Effects target by tag, never by skill name.** `Condition = SkillTags.Projectile`,
  not `if (skill.Id == "fireball")`. Tag-targeting is what makes an effect a build
  enabler rather than a single-skill buff nobody builds around.
- **Fixed pairing.** Each effect belongs to a specific named base item ("Emberwake,
  Ebony Wand"). Rejected: D3's model where any legendary rolls a random power from a
  pool — more combinations, but items lose identity and become interchangeable, and
  Sets already supply the hunt-a-named-thing loop that fixed pairing reinforces.
- **Authored pool, never procedurally generated.** Behaviour transforms cannot be
  meaningfully tested if generated.
- **Downsides are allowed and encouraged.** PoE uniques routinely carry real
  drawbacks; it creates build tension and stops effects being strictly-additive fun.
- **Uncapped.** Any number of majestic effects may be active simultaneously — the
  owner's explicit call: chaotic combinations are part of the appeal. The accepted
  cost is a combinatorial balance/QA surface, the same one PoE pays permanently. The
  one mitigation kept: every effect declares a `Priority` band per the existing
  `CastModifier` convention (additive-count effects below multiplicative ones), so
  stacked combos resolve **deterministically**. Chaos is a feature; irreproducible
  chaos is a bug report nobody can close.
- `MajesticEffectId` goes in the drop descriptor so filters can target specific
  effects.
- **Slot restriction applies** — offensive effects belong on weapons/gloves/rings,
  not on every base. Exact mapping is an open question.

### First-pass effect pool **[PROPOSED]**

Base names are placeholders until a base-item list exists.

| Item | Base | Condition | Effect | Downside |
|---|---|---|---|---|
| Emberwake | Wand | `Projectile` | Projectiles leave burning ground for 3s | −30% projectile speed |
| Splitmark | Bow | `Projectile` | Projectiles fork once on first hit | −25% projectile damage |
| Detonator's Grasp | Gloves | `Projectile` | Projectiles explode in a small area on hit | +100% skill cost |
| Hoarfront | Body Armour | `Cold` | Area skills leave chilled ground | Fire damage dealt is halved |
| Tremorstep | Boots | `Melee` | Melee hits release a shockwave behind the target | −25% melee range |
| Echo Crown | Helmet | `Area` | Area skills repeat once after 0.4s | Each cast deals 60% damage |
| The Long Hour | Amulet | `Duration` | Duration effects last +150% | A duration cannot be refreshed before it expires |
| Deadlock | Staff | `Channelled` | Channelling builds charges; releasing detonates them | Cannot move while channelling |
| Carrion Pact | Ring | `Minion` | Minions explode on death | −40% minion life |
| Ghostwalk | Boots | `Movement` | Movement skills trigger a nova at the destination | Movement skills gain a 3s cooldown |

**Echo Crown is the combo enabler.** A repeat effect multiplies with everything else
in the pool, so it sits in the highest priority band and resolves last. Expect it in
most builds — acceptable if intended, but worth knowing before it happens.

**Deliberately excluded: tag-rewriting effects** ("your Spells are also Attacks").
`SkillTags.Attack`/`Spell` are documented as *structural* in `scripts/Combat/SkillTags.cs`
— they determine which speed stat scales a skill's use time — so an item that flips
them changes behaviour rather than filtering. Memorable, but a footgun to build before
the rest of the system is stable. See Open Questions.

## Drop pipeline **[DECIDED]**

Three families with three different lifetimes:

| Family | On ground | On pickup | Persists |
|---|---|---|---|
| Currency | label + sprite | increment wallet | no |
| Equipment | label + sprite | into grid inventory | yes |
| Flask orb | sprite | restore charges | no |

The shared contract is deliberately narrow:

```csharp
IDroppable {
    ItemDescriptor Descriptor { get; }   // what the filter reads
    void OnPickup(Player player);        // what actually happens
}
```

They share the **drop / spawn / label / filter** path and diverge only at
`OnPickup`. Currency and flask orbs must not be forced through an inventory
abstraction neither of them needs.

Drop tables are separate again: weighted entries plus tags, resolved against a
`DropContext` (monster level, area level, magic find, player). Adding gems, keys, or
maps later means a new `IDroppable` plus table entries — no spawner changes.

Name generation varies per family — Magic is `Temper + base + Sigil`, Rare draws from
word pools, Majestic and Set are authored fixed names, currency is `"{amount} Gold"` —
so it lives behind `Descriptor.DisplayName`. The label never switches on type.

## Item filter **[DECIDED]**

The governing rule:

> A filter can only act on fields it can see. Whatever is not in the descriptor at
> drop time can never be filtered on.

So the vocabulary is fixed **before** items exist:

```
ItemDescriptor
  ItemClass, BaseTypeId, Rarity
  ItemLevel, AreaLevel
  AffixCount, AffixTiers[], HighestAffixTier
  SetId?, MajesticEffectId?
  SocketCount, StackSize
  Width, Height
  DisplayName
  CurrencyId?, Amount?
  Tags            // escape hatch for anything not anticipated
```

### Rarity colour is filter output, not label state

```
Roll item → ItemDescriptor → FilterChain.Evaluate(desc) → DisplayStyle → spawn label
```

If the label read `rarity.Colour` directly, a user filter wanting "show Common items
in red because I'm crafting" would have to fight the label. Instead a **built-in
default filter** assigns rarity colours, and user filters simply prepend rules.
Currency gets a default rule too, so there is no special case anywhere in label code.

```
DisplayStyle   // all nullable, so rules cascade and merge
  TextColour?, BackgroundColour?, BorderColour?
  FontSize?, Sound?, Beam?
  Hidden
```

**First matching rule wins** (PoE's semantics). Documented here so it is never
ambiguous.

### The descriptor is immutable after drop

There are **no unidentified items**. Rejected deliberately: unidentified state means
an item's descriptor changes after it drops, which forces filter re-evaluation and a
restyle pass, and it hides from the filter exactly the fields players most want to
filter on. Dropping the mechanic removes an entire class of problem.

### Label rendering

Dozens of labels appear at once when a pack dies. Required from day one: label
pooling, world→screen projection, and vertical stacking to avoid overlap.

**[PROPOSED]** Labels are hold-a-key-to-show (D2/PoE) rather than always-on (D3).
Hold-key scales far better with loot volume and suits a filter-centric game.

## Build order **[PROPOSED]**

*(Milestones 1 through 2c are implemented. 2d and 2e were added from playtest
feedback.)*

1. **Drop pipeline** — `IDroppable`, `ItemDescriptor`, `FilterChain`, labels, and
   currency/gold. No equipment yet; currency alone proves the seam.
2. **Equipment** — base items, Temper/Sigil rolling with tiers, grid inventory,
   equip → `ActorStats` modifiers via `Source` tagging.
4. **Inventory usability (2d)** — UI input consumption, manual item pickup,
   `Escape` to close, hover tooltips.
5. **Inventory sort (2e)** — a sort button. Quality of life, and a natural home for
   the packing logic the occupancy mask already provides.
6. **Flasks** — charges, time regen, refill orbs.
7. **Sets and Majestics** — `SetDefinition`, `CastModifier` subclasses.

## Open questions

1. **Majestic effect pool** — the 10 effects above are [PROPOSED]; confirm, cut, or
   replace.
2. **Slot-to-effect restriction mapping** — agreed in principle, not yet specified.
   The slot list is now settled (see [Equipment slots](#equipment-slots-decided));
   what remains is which effect conditions may roll on which slot.
3. **Tag-rewriting majestic effects** — excluded for now; revisit once the cast
   pipeline has more consumers.
4. **Junk/grey rarity** — cut for lack of a sink. Returns only alongside a
   salvage/crafting system.
5. **Set drop weighting relative to Majestic** — they are siblings, so weight is a
   deliberate choice, not a derivation.
6. **Sockets** — `SocketCount` is reserved in the descriptor; no socket system is
   designed.
7. **Base item stats for body armour** — drafted as 2×3 rather than the 2×2 square
   originally sketched, so body armour is the largest non-weapon item and slots
   feel distinct from a 2×2 helmet. Revert if the square was deliberate.
8. **Affix targets** — resolved by the second pass of `docs/design/stats.md`, which
   exists because this document needed somewhere for affixes to attach. Eighteen
   stats, which is roughly the number a 2 Temper / 2 Sigil budget needs before it
   stops rolling duplicates.
7. **Crafting** — Temper/Sigil exists partly to give crafting a granularity to work
   with, but no crafting system is designed.
8. **Character level cap vs item level 120** — item level range should exceed the
   character cap so endgame areas can drop above-level gear. Cap not yet decided.
9. **Label visibility** — hold-key vs always-on is [PROPOSED].
