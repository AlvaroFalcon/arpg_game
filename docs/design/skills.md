# Skills & Combat (third pass)

Status legend used below:
- **[DECIDED]** — settled, implement as written.
- **[PROPOSED]** — recommendation from a design pass, awaiting the project owner's
  confirmation. Listed again under Open Questions.

> **Third pass note.** Changes from pass two, in order of how much they move:
>
> 1. **The right-click conflict is resolved and now [DECIDED]** — LMB defaults to the
>    built-in Move occupant, RMB is a normal skill slot, `force_move` is added on a
>    key. This changes shipped code and `docs/design/movement.md`.
> 2. **Attack/cast speed is now derived from the `Attack`/`Spell` tags, not from an
>    explicit `SpeedStat` field.** Pass two rejected this; pass three reverses that
>    rejection. The `SpeedStat` field is deleted before it was ever built. See
>    [Attack speed and cast speed](#attack-speed-and-cast-speed-tag-derived).
> 3. **Cooldowns are no longer deferred.** Cooldown is a per-skill authored field in
>    the first slice, with a `CooldownRecovery` stat and a charges-shaped runtime
>    state. See [Cooldowns](#cooldowns-decided).
> 4. **A sixth seam is added: `CastModifier`.** Working through the owner's own
>    "projectiles split in two on hit" example exposed that pass two's seams assume
>    upgrades are *numeric modifiers on actor stats*, when the upgrades that actually
>    define builds are *behaviour transforms on the per-cast parameter block*. Those
>    are a different type and they need their own pipeline stage and ordering rule.
>    See [Seam 6](#seam-6-castmodifier--behaviour-transforms-decided).
> 5. **A build-depth deep dive** with a concrete recommendation, marked
>    **[PROPOSED]** throughout. See [Where build depth comes from](#where-build-depth-comes-from-proposed).
> 6. **A prioritised plan** across roughly four milestones. See
>    [Priority order](#priority-order-decided).

## What it is

A skill is a **piece of data**, not a piece of the player. Any entity — a player of
any class, or an enemy — can be handed the same skill definition and use it. That
reuse requirement is the constraint the whole design is built around, and it's what
dictates the three-layer split below.

### The three layers **[DECIDED]**

1. **Skill definition** — immutable authored data (a Godot `Resource`, saved as
   `.tres`). Contains only things that are true about the skill regardless of who
   casts it: display name/icon, base damage, base use time, which speed stat scales
   it, cost entries, tags, and the archetype-specific parameters. Authored once,
   referenced by player loadouts and enemy loadouts alike.

   A definition may **never** reference: an input action, the player node, the
   mouse, a specific collision layer, or a specific faction. If it does, it isn't
   reusable and the constraint is broken. This is the single rule to enforce in
   review.

2. **Cast context** — the per-cast bundle assembled by whoever is casting: the
   caster node, the caster's stat/modifier source, the origin position, the aim
   direction, the caster's faction, **a mutable copy of the skill's numeric
   parameters, and a mutable copy of its tags**. This is the seam that makes the
   same definition work for both sides — the player fills `AimDirection` from the
   mouse cursor, an enemy fills it from the direction to its current target — and it
   is also the seam every future skill-modifies-skill mechanism writes through (see
   [Seams](#seams-for-skills-affecting-skills)).

3. **Skill user (executor)** — a component that lives on any combat entity. Owns
   the loadout, owns the per-skill use timers, builds the cast context, pays costs,
   and triggers the cast. Player and enemy both have one; the only difference is
   what feeds it (input vs AI).

The practical test for the whole design: an enemy should be able to use the exact
same `.tres` file as the player, with the only difference being the aim source and
the faction stamp. If adding an enemy caster later requires editing a skill
definition, the split was wrong.

### Faction / friendly fire **[DECIDED]**

Every combat entity has a faction (`Player` / `Enemy` / `Neutral`). The cast context
carries the caster's faction; the projectile is stamped with it at spawn; a hit only
resolves against a target of a *different* faction.

This is deliberately part of the runtime context rather than baked into the
definition, precisely so one "Fireball" asset serves both sides without a duplicate
"enemy version." (Implementation may use physics layers, a runtime check, or both —
that's the godot-expert's call; the design constraint is only "one definition, both
sides, no duplication.")

## Skill identity: archetype vs tags **[DECIDED]**

The owner asked for "labels or types" so that "upgrades enhance certain skills."
Those are **two different things** and conflating them is the mistake PoE made
(where `Attack` and `Spell` are simultaneously cosmetic keywords and hard-coded
engine forks). Keep them strictly separate:

| | **Archetype** | **Tags** |
|---|---|---|
| Count per skill | **Exactly one** | **Many** |
| Set | Closed; adding one means writing code | Open-ish; adding one is data |
| Purpose | **Decides which executor runs** — the behaviour | **Declarative labels for filtering** |
| Who reads it | The skill user, to dispatch | Conditional modifiers, upgrades, UI filters |
| Examples | `Projectile`, `MeleeStrike`, `AreaBlast`, `SelfBuff`, `Movement`, `Channelled` | `Projectile`, `Spell`, `Attack`, `Melee`, `Fire`, `Cold`, `Area`, `Duration`, `Minion`, `Movement`, `Channelled` |

**The rule (sharpened in pass three): *executors* must not branch on tags. *Scaling
and modifier selection* must.** If you find an `if (tags.Has(Fire))` inside a
behaviour executor, a tag has leaked into being an archetype and the two need
re-separating. But `if (tags.Has(Spell))` inside a one-line pure function that picks
which speed stat scales the timer is not a violation — it is the tag system doing its
job. Pass two stated the rule as the broader "code never branches on tags," which
made the tag-derived speed stat look illegal when it isn't; see
[Attack speed and cast speed](#attack-speed-and-cast-speed-tag-derived).

The distinction that matters: **does branching on the tag change what the skill
*does*, or only what it's *worth*?** Behaviour → archetype. Value → tags.

### Worked example, and two sharpenings **[DECIDED]**

The owner's own check-of-understanding, confirmed and then corrected in two places.

**Confirmed, exactly right:** a skill that fires a fireball is archetype
`Projectile`, tags `Fire, Projectile, Spell`. A passive or item that says
*"Projectiles split in two on hit"* grabs it because it carries the `Projectile`
tag, and it grabs **every** other skill carrying that tag with no per-skill
authoring. That is the whole point of tags.

**Sharpening 1 — tags don't "determine the skill type," the archetype does.** Using
the same Fireball: its **archetype** is `Projectile`, and that is what decides *which
executor runs* — spawn a thing, move it in a straight line, resolve a hit. Its
**tags** `Fire, Projectile, Spell` decide *what is allowed to grab it*. The word
`Projectile` legitimately appears in both and means two different things there: as an
archetype it means "this is executed by the projectile code"; as a tag it means
"modifiers that care about projectiles apply here." A melee cleave that throws a
shockwave would be archetype `MeleeStrike` with the `Projectile` tag — the executor
is melee, but projectile modifiers still apply. Change a skill's archetype and it
behaves differently; change its tags and it only scales differently.

**Sharpening 2 — `Trigger` is two different things wearing one word.**

- **A trigger** (the causal machinery) is *not* a tag. "When you crit, also cast
  Frost Nova" is a rule that lives on a modifier, an item, or an upgrade, listens on
  [seam 5](#the-six-seams-decided) (`Hit` / `CastFired` events) and *issues a cast*.
  It's the thing that causes a cast, so it can't be a label on the skill being cast.
- **`Triggered`** *is* a real tag, but it is a **per-cast provenance tag, not an
  authored one.** It is stamped onto the cast context by the skill user when the cast
  came from a trigger rather than from a button press. PoE does exactly this, and it
  is what lets mods like "Triggered skills deal more damage" or "Triggered skills
  have increased cooldown recovery" exist at all. A skill definition should **never**
  author `Triggered` on itself — it doesn't know how it was invoked.

  This is the first concrete justification for **[seam 2](#the-six-seams-decided)**
  (a mutable per-cast *copy* of the tag set). Without it there is nowhere to put
  `Triggered`.

**Sharpening 3 — `Damage over time` is both, and they are different layers.**

- **As a tag** (`Duration` or a dedicated `DoT`) on the definition: "this skill's
  identity involves damage over time," so a DoT-focused passive cluster can filter
  for it. Descriptive.
- **As a property of the damage payload**: the payload has to distinguish "30 damage,
  now" from "30 damage per second, for 4 seconds." That's a delivery kind, not a
  label.

They must both exist because they don't line up. A fire skill can deal a hit *and*
apply a burn: the skill carries the `DoT` tag, but only *part* of its damage is
over-time. A modifier reading "+40% Damage over Time" that filtered on the skill's
tag would wrongly buff the hit portion too — that specific bug is a long-running
source of confusion in PoE. **Decision: the damage payload carries a
`DamageDeliveryKind` (`Hit` | `OverTime`) field, authored now, with only `Hit`
produced in the first slice.** One enum, reserved. The tag stays purely descriptive.

Notes:
- Archetype is expressed as the **`SkillDefinition` subclass** the `.tres` is an
  instance of (`ProjectileSkillDefinition : SkillDefinition`). That is the idiomatic
  Godot answer and it gives each archetype its own `[Export]`ed fields, so a melee
  skill's inspector isn't cluttered with projectile speed.
- Tags live on the base class. **Recommended representation: a `[Flags]` enum**
  (fast, typo-proof, editor checkboxes) rather than strings. Trade-off: capped at 64
  values and adding one needs a 2-second recompile; strings would be fully
  data-driven but silently accept typos. For a solo project the enum is the better
  trade, and enum → string is a mechanical migration if the cap is ever a problem.
- The same word appearing in both columns (`Projectile`, `Movement`) is fine and
  expected. They mean different things: the archetype says *how it executes*, the
  tag says *what modifiers may grab it*. A melee skill that launches a shockwave
  could have archetype `MeleeStrike` and carry the `Projectile` tag.

### Initial tag set **[DECIDED — as a starting point, not a commitment]**

`Attack`, `Spell`, `Projectile`, `Melee`, `Area`, `Duration`, `Movement`,
`Channelled`, `Minion`, plus damage-type tags when damage types exist.

Getting this list wrong is **cheap**: adding, removing, or re-tagging is editing
`.tres` files, and the worst consequence is a rebalance. Do not spend design time
perfecting it. (Getting the *interaction mechanism* wrong is the expensive part —
see [Seams](#seams-for-skills-affecting-skills).)

## How skills interact with the stat system

**The skill owns the base number; the caster owns the modifiers.** **[DECIDED]**

Final damage for a cast is computed per-cast into an ephemeral `Stat`:

```
base    = skillDefinition.BaseDamage
result  = (base + caster's Flat damage mods)
          * (1 + sum of caster's Increased damage mods / 100)
          * product of caster's More damage mods
```

i.e. exactly the existing `Stat` formula from `docs/design/stats.md`, but with the
base coming from the skill rather than from a stat the actor owns.

**Added damage effectiveness (2026-10-15).** The Flat sum is multiplied by the
skill's `AddedDamageEffectiveness` before it joins the base
(`Stat.ComputeFormula(base, mods, effectiveness)`), PoE's rule: flat damage from
gear is worth the same "+10" to every skill, so a fast or many-hit skill with a
small base gained far more from it than a slow heavy one. Values
(`resources/skills/run/*.tres`, shown in the gem tooltip when not 100 %):

| Skill | Base | Effectiveness |
|---|---|---|
| Spark, Throwing Knives | 12, 8 | 50 % |
| Blades | 14 | 60 % |
| Double Strike | 15 | 70 % |
| Fireball, Frost Nova, Cleave | 22, 20, 24 | 100 % |
| Ground Slam, Sunder | 34, 36 | 130 % |
| Meteor | 50 | 150 % |

Increased and More are untouched: only flat damage depends on the skill. A level-1 goblin
and a level-80 player firing the same `Fireball.tres` get the same base and
different totals, because the modifier stack differs.

**Skills scale only by modifiers.** **[DECIDED]** There is no per-skill level, no
per-skill damage curve, and no per-class multiplier on a skill. Everything that
makes a skill stronger is a modifier on the caster (or, later, a modifier attached
to that skill instance). Consequence: two characters of different classes casting
the same skill differ *only* by their modifier stacks — which is the next point.

**Classes differ only by which tree they get and where they start on it.**
**[DECIDED]** No class multiplies a skill's damage; no skill is intrinsically
"better on a Ranger." A class is a starting position plus tree access, and
everything downstream of that is modifiers. This is the simplest model that
supports both "classes feel different" and "one skill definition serves everyone,"
and it means class balance is a passive-tree problem rather than a per-skill
spreadsheet.

### Attack speed and cast speed (tag-derived)

#### What these stats are actually for — in plain terms

Every skill has a **base use time**: "this skill takes 0.5 seconds before you can use
it again." Attack speed and cast speed are **multipliers that shrink that number**.
At 1.0 (no bonuses at all) a 0.5s skill fires twice a second. At 1.5 it fires three
times a second. That is the entire mechanism — they don't change how hard a hit
lands, they change **how often you land one**, which is why they end up being among
the strongest damage stats in the genre.

**Why two of them instead of one.** Because a stat that everybody wants equally is
not a build decision — it's a number that should have been baked into the base. If
there were a single `UseSpeed`, then "+15% use speed" on a glove would be exactly as
good for every character in the game, and picking it up would be automatic rather
than interesting. Splitting it in two means the bow character wants attack speed and
the fire mage wants cast speed, the *same affix* is best-in-slot for one and dead
weight for the other, and a hybrid build has to split its budget and be worse at
both. **The player-facing consequence: an attack-speed build makes your attacks
faster but not your spells.** That asymmetry is what turns gear from "is this number
bigger" into "does this fit my build," which is the whole reason the loot loop is
interesting.

Cost, stated honestly: it doubles the relevant stat surface immediately, and every
future speed affix has to pick a side. That's a permanent tax on affix authoring and
it is the price of the differentiation above.

#### The mechanism **[DECIDED]**

An actor carries **`AttackSpeed` and `CastSpeed` as independent modifier carriers**,
each a `Stat` with `BaseValue = 1.0`:

```
effectiveUseTime = definition.BaseUseTime / <the resolved speed stat>.Value
```

#### How a skill declares which one it uses: **derived from tags** **[DECIDED — reverses pass two]**

```
if tags has Attack  -> AttackSpeed
else if tags has Spell -> CastSpeed
else                -> none (use time is unscaled; divide by 1)
```

**There is no `SpeedStat` field on the definition.** Pass two specified one and
rejected tag-derivation; pass three reverses that. The reversal is not a concession —
the pass-two reasoning was wrong in two places:

1. **"It forces every skill into being an attack or a spell."** It doesn't, unless
   the derivation is written as a total function. A skill tagged neither is simply
   not scaled by either, which is precisely what `SpeedStat = None` meant — expressed
   as an *absence* rather than as a magic value. Absence is the cleaner encoding: you
   can't author `None` by accident and you can't forget to author it.
2. **Pass two admitted it needed an "authoring convention, not enforced by code"**
   that the tag and the field agree. A convention you have to remember is a bug
   generator. A derivation you can't get wrong isn't one.

The decisive argument, though, is a player-facing one:

> **The tag is the contract the player reads.** If a skill's tooltip says `Spell` and
> the player buys cast speed, cast speed must work. Under the explicit-field model,
> the tag (which every affix and passive filters on) and the field (which only the
> timer reads) are two independent notions of "is this a spell," they can silently
> disagree, and **no UI would ever surface the disagreement.** A skill tagged
> `Attack` that secretly scales with cast speed is a lie the player has no way to
> detect. Deriving both from one source makes the lie impossible to author.

This is PoE's actual model, and it's the owner's own instinct, and it is right.

**Is deriving behaviour-relevant scaling from tags a violation of "code branches on
archetype"?** It is a violation of pass two's *wording*, and the wording was too
broad. The rule's real purpose is that **adding a tag to a skill must never silently
change what the skill does**, because that's what makes tags safe to edit freely.
Selecting which multiplier divides a timer isn't behaviour — it's scaling, and
scaling by tags is what the entire tag system exists for. Every conditional modifier
in this game will branch on tags; that's [seams 3 and 4](#the-six-seams-decided). See
the sharpened rule under
[Skill identity](#skill-identity-archetype-vs-tags-decided).

**Where the derivation lives:** in one small pure function
(`ResolveSpeedStat(tags)`), called by the skill user when it computes the use timer.
**Not in any executor.** One place, one function, trivially testable.

**Edge cases, specified:**

- **Tagged neither** (dash, warcry, toggle): no speed scaling. Correct by default —
  otherwise a cast-speed build accidentally halves the downtime on your dash, which
  is a real balance leak. These skills are governed by
  [cooldown](#cooldowns-decided) instead, which is a second reason cooldowns moved
  into the first slice.
- **Tagged both `Attack` and `Spell`**: **an authoring validation error.** They are a
  partition, not overlapping descriptors, and a genuine hybrid should be a *third*
  category (its own stat), not an overlap. Runtime fallback if a bad asset ships
  anyway: `Attack` wins — arbitrary, but it must be deterministic rather than
  dependent on tag iteration order.
- **Channelled skills** are still the awkward case: they aren't "one use every N
  seconds," they're "a tick rate while held." When channelling lands, the resolver
  grows one branch checked *before* the other two (`Channelled` → `ChannelRate`).
  Additive; the resolver being a function rather than a field is exactly what makes
  it additive.

**Cost of this reversal, named:** the `Attack` and `Spell` tags become
**structural** — re-tagging a skill now changes its scaling, so pass two's claim that
"getting the tag list wrong is cheap, it's just `.tres` editing" is no longer true
for *those two*. Mitigation is a documented split:

- **Structural tags** — have mechanical meaning beyond filtering. Currently `Attack`,
  `Spell`. Likely joined later by `Channelled`, `Movement`, `Minion`. Changing these
  changes behaviour; treat them like schema.
- **Descriptive tags** — pure filter labels: `Fire`, `Cold`, `Area`, `Duration`,
  `Projectile`, `Melee`. Free to add, remove, re-tag; worst case is a rebalance.

**Note for `docs/design/stats.md`:** this *removes* one of the two pressures toward a
keyed stat registry — `SpeedStat` no longer stores a stat identifier in authored
data. `GetModifiers(query)` still takes a stat identifier as an argument, so the
pressure is halved, not eliminated.

### What is explicitly simplified away (first pass)

- **No weapon damage / damage effectiveness.** PoE attacks scale off the equipped
  weapon's damage, with a per-skill effectiveness percentage. There is no equipment
  yet, so every skill is "spell-like": its own base. The definition should reserve
  the concept (a flag/field for "derives base from weapon") but not implement it.
- **No damage types, no conversion, no resistances.** One undifferentiated damage
  number. Widening the damage payload later is additive, not a rewrite.
- **No crit, no accuracy/evasion.** Every hit lands for the computed amount.
- **No support gems** — and see [Divergence](#deliberate-divergence-from-poe) for
  why they may never exist here. The structural hook is kept regardless.

### Damage payload **[DECIDED]**

A hit passes a **damage payload object** (amount, source actor, source faction,
skill tags), not a bare float. Damage-taken modifiers, DoT, status effects, on-hit
triggers, and damage numbers all need somewhere to hang, and every one of them is a
rewrite if the interface is `TakeDamage(float)`.

## Resource costs **[DECIDED]**

The first pass assumed mana and deferred spending. **Both of those are reversed.**
Cost is authored per skill, the *resource type is part of the authoring*, and
spending is in the first slice.

### The shape

A definition carries **a list of cost entries**, each:

```
CostEntry {
    ResourceType  Resource   // Mana, Life, Energy, ...
    float         Amount
    CostMode      Mode       // Flat | PercentOfMax | PercentOfCurrent
}
```

**Why a list and not a single cost.** Hybrid costs are a real design space (Last
Epoch's health-and-mana skills, D2's blood skills, Warframe's dual costs), and more
importantly **a list is the shape a cost-converting modifier needs**. "Your spells
cost Life instead of Mana" (PoE's Blood Magic), "your skills cost 20% less Mana,"
"add a Life cost to gain more damage" are all transforms over a collection. Cost
resolution therefore happens **per-cast, into the mutable parameter block on the
cast context** — never read straight off the definition — for exactly the same
reason projectile parameters do. A list of one costs one loop iteration.

**Why three cost modes.**
- **Flat** is right for mana. Mana pools grow modestly with progression, so a flat
  number stays a real budget across the whole game. This is what D2, PoE, and Grim
  Dawn all use.
- **PercentOfMax** is right for life, and it is why life costs in ARPGs are almost
  always percentage-based. Life pools scale enormously with gear; a flat 40 life
  cost is lethal at level 5 and literally free at level 80. A percentage keeps the
  risk constant across the whole game, which is the entire point of a life-cost
  skill.
- **PercentOfCurrent** is the self-limiting variant: the cost shrinks as you get
  hurt, so it can never spiral. It's the "always leaves you alive" pattern, useful
  for a skill you want to be spammable-but-draining rather than a gamble.

### Insufficient resource

**The cast is blocked. All-or-nothing, never partial.** A cost is payable only if,
for every entry, `current - amount >= FloorFor(resourceType)`.

- **Mana floor: 0.** **Life floor: 1.**

**A skill can never kill its own caster.** This is PoE's rule and it is correct: a
self-kill from holding your attack button reads as a bug rather than as a
consequence, and it makes life-cost skills unusable rather than interestingly risky.
The interesting version of "life costs are dangerous" is that you're left at 5% life
in a fight, not that you died to your own button.

A failed cast **returns a reason** (`InsufficientResource(type)`, `StillRecovering`,
`SlotEmpty`) rather than silently doing nothing. UI, sound, and screen-flash
feedback all need it, and adding a return value to a `void` method later touches
every call site. Nothing consumes it in the first slice.

### Resources are per-actor, and enemies mostly don't have them

An actor owns a **pool per resource type it actually has** (current, max, regen).
An enemy may have none at all. So: **what happens when an actor with no mana pool is
handed a mana-costing skill?**

**Decision: an explicit actor-level `IgnoresResourceCosts` flag, defaulting to
`true` for enemies and `false` for players.** When set, cost resolution is skipped
entirely.

Rejected alternatives:
- *Missing pool = zero, so the cast is blocked.* This is the silent-failure trap:
  an enemy would simply never attack and nothing would tell you why. It also breaks
  the one-definition-serves-both constraint at the first contact with reality.
- *Missing pool = infinite, cast freely.* This is the right **behaviour** but the
  wrong **mechanism**, because it also silently gives a free ride to a *player*
  character that's missing a pool through an authoring mistake.

The flag makes the behaviour explicit and keeps the door open: a boss that should
budget its mana sets the flag `false` and gets a real pool. And it states the design
position clearly — **costs are a player-side budgeting and build-choice mechanic;
enemies are paced by AI cadence and cooldowns instead.** That is how essentially
every ARPG actually works (D2 monsters do not manage mana).

### Why this moved into the first slice

The first pass deferred spending because a resource bar needs UI that doesn't exist.
That reasoning is now outweighed:

1. **The loadout grew from four slots to six.** Without cost pressure, the
   highest-DPS skill is always the correct button and the other five slots are
   decoration. Six slots makes that worse, not better. Cost is what turns a loadout
   into a decision.
2. **The cost model is now genuinely structural** — a list, three modes, per-cast
   resolution, an actor-level opt-out. Authoring a shape you never execute is how
   you discover in pass three that the shape was wrong.
3. **Spending is cheap; only the UI is expensive.** First slice ships the pool, the
   spend, the block, and a debug text readout. No bar art, no polish.

First slice ships **one pool (Mana) on the player**, with a flat regen-per-second
placeholder stat so you can't permanently brick yourself. `Life` and `Energy` are
supported by the model but no shipped skill uses them yet.

## Rate levers: use time, cooldown, cast time

| Lever | First slice | Why |
|---|---|---|
| **Use time** (scaled by `AttackSpeed`/`CastSpeed`/nothing) | **IN** | Without a rate limit a held button fires every physics frame and nothing is balanceable or legible. |
| **Resource cost** | **IN** | Promoted in pass two. |
| **Cooldown** (independent of use time) | **IN** *(promoted in pass three — pass two deferred it)* | Owner override. It is the *only* rate governor for skills tagged neither `Attack` nor `Spell` (dash, warcry, toggle), and those are the second and third skills anyone will want. |
| **Cooldown recovery stat** | **IN** | Otherwise cooldown-gated skills are the only skills in the game that don't scale, and they die of irrelevance by mid-game. |
| **Charges** (banking multiple uses) | **OUT**, but the runtime state is shaped for it | One `int` and a different-shaped timer now; a save-schema and UI change later. |
| **Cast time / animation lock** | **DEFERRED** | No animations exist, so a lock reads as an unexplained freeze. See the movement decision below. |

### Cooldowns **[DECIDED]**

`Cooldown` (seconds, default `0` = none) is an authored field on every skill
definition, in the first slice.

#### Cooldown and use time are independent levers

They answer different questions and a skill may have both, either, or (almost) neither.

| | **Use time** | **Cooldown** |
|---|---|---|
| What it means | How long this use *occupies you* — the minimum gap between two consecutive uses of this skill | A lockout on *this skill only*, running independently of everything else you do |
| Scaled by | `AttackSpeed` / `CastSpeed` (tag-derived) | `CooldownRecovery` only |
| Governs | The **sustained rate** of a held button | **Burst availability** |
| Typical owner | Your spammable primary (Fireball, basic attack) | Utility: dash, warcry, big nuke, defensive |

**How they interact.** The skill user gates a cast on
`max(useTimeRemaining, cooldownRemaining)`. Consequences worth knowing before
authoring numbers:

- **Long cooldown + short use time** — the intended shape for a dash or a blink. The
  use time is essentially zero, so pressing it feels instantaneous; the cooldown is
  what makes it precious. This is the case cooldowns exist for.
- **Short cooldown + long use time** — the cooldown expires during the recovery and
  is therefore *invisible*. Not illegal, but a design smell: two levers where one is
  doing nothing. **Worth an authoring warning, not a prohibition** — a cooldown
  shorter than the use time might be intentional as future-proofing against cast
  speed, but nine times out of ten it's a mistake.
- **Which one gates a hold-to-repeat input:** whichever is longer at that moment.
  Since cast speed shortens use time but *not* cooldown, a build stacking cast speed
  will push its spammable skills toward being cooldown-limited, and that transition is
  a real (and good) diminishing-returns curve — it's what stops attack/cast speed from
  being infinitely stackable.
- **Neither is not allowed for anything that fires.** `UseTime > 0 || Cooldown > 0`
  is an **authoring invariant to validate**; violating it means a held button casts
  every physics frame. (A pure toggle/aura, which isn't repeated at all, is the only
  legitimate exception, and none exist yet.)

#### `CooldownRecovery` stat **[DECIDED]**

A fifth actor stat: `CooldownRecovery`, a modifier carrier with `BaseValue = 1.0`.

```
effectiveCooldown = definition.Cooldown / CooldownRecovery.Value
```

**Why it must exist:** "skills scale only by modifiers" is already decided. If
cooldown is the one number with no modifier carrier, then cooldown-gated skills are
the only skills in the game that don't get better as the build grows — so they become
strictly worse over time and end up unequippable. That is exactly what happened to
PoE's warcries for years before they were reworked. Every game in this lane
(PoE, D4 aspects, Grim Dawn, Hell Clock) treats cooldown reduction as a top-tier
build stat; Hell Clock guides specifically call out stacking cooldown recovery as an
early priority.

**Recovery *rate* (a divisor), not reduction (a subtraction). This is firm.**
"-X% cooldown" hits 100% and breaks the game; a rate divisor is asymptotic and can
never reach zero no matter how much is stacked. PoE moved from the former to the
latter for this exact reason. The player-facing text can still read "20% faster
cooldown recovery" — this is about the maths, not the words.

**One global stat now, not per-tag.** Tag-scoping (`+30% Cooldown Recovery for
Movement skills`) comes free later through
[seam 3](#the-six-seams-decided)'s query object. No extra work today.

#### Cooldown starts on **cast start** **[DECIDED]**

The frame the cast is committed and costs are paid, not when the cast finishes.

With zero cast time the two are identical, which is exactly why it should be decided
now while it's free. Three reasons:

1. **It's what the player expects.** The number they watch starts counting down the
   moment they press the button. On-end means a 1s cast with a 5s cooldown is really a
   6s cycle, and players read that as the cooldown being a lie.
2. **On-end double-dips cast speed.** Faster cast → cooldown starts earlier → shorter
   total cycle. Cast speed would then secretly be cooldown reduction, which is
   precisely the kind of hidden coupling the attack/cast speed split exists to avoid.
3. Precedent: PoE starts cooldowns on use.

**No `CooldownStart` enum field is authored.** Channelled skills will eventually want
"cooldown starts when the channel ends," but that's a one-line change in one
executor. Compare with charges below, which changes the *shape of runtime state* and
therefore does get a field now. The asymmetry is deliberate: reserve fields for things
that change data shape, not for things that change one line of logic.

#### Charges — out of scope, but the state shape is decided **[DECIDED]**

Charges (banking N uses of a skill that recharge on a timer) are **not implemented in
the first slice**, but two things exist now:

- `MaxCharges` (int, default `1`) — an authored field on the definition.
- The per-skill runtime cooldown state on the `SkillInstance` is
  **`(int chargesAvailable, float rechargeTimer)`**, not a bare
  `float cooldownRemaining`.

**Why the state shape must be right now:** every place that reads cooldown state —
the cast gate, the debug readout, later the hotbar UI, later the save file — reads
that shape. With `MaxCharges = 1` the behaviour is identical to a plain cooldown.
With the bare float, adding charges later is a change to all of those at once.
Cost today: one int and a slightly different decrement.

**Why charges are worth planning for at all:** they're what make a movement skill feel
generous instead of stingy. One dash on a 4-second cooldown and two dashes on an
8-second recharge have identical uptime, but the second lets a player double-dash out
of a bad spot and *then* be punished for it — the failure is their choice rather than
the cooldown's. Precedent: PoE (Flame Dash), Last Epoch, D4 (Dash/Teleport), and
essentially every modern action game with a dodge.

## The loadout and input

### Six slots, uniform semantics **[DECIDED]**

**Six equipped skills: keys `1`–`4`, plus LMB and RMB.** Implemented as an N-slot
loadout with N configured to 6 — binding and slot count stay separate concerns, so a
7th slot or a class-specific count remains a config change.

**The mouse slots are not a different kind of slot.** They hold any skill, aim the
same way (all six aim at the cursor), and obey the same timers and costs. Considered
and rejected: making the mouse slots semantically special ("sustained/spammable
only", or "mouse = attacks, keys = utility"). Two reasons —

1. Role-restricted loadouts have a bad track record. D3 shipped exactly this
   (skills locked to categories per slot), players hated it, and "Elective Mode"
   removing the restriction became one of the most universally-enabled options in
   the game. Restricting *what may go where* buys legibility for new players and
   costs the entire expressive point of a loadout.
2. There's no mechanical difference to encode. The cursor is the aim source for all
   six.

There *is* a real **ergonomic** asymmetry worth designing around rather than
encoding: LMB and RMB are the only buttons a player can hold comfortably while also
holding WASD and aiming. They will naturally attract the sustained, held-down
skills, and the number keys will attract the punctuated ones. That's a fact to write
skills *for*, not a rule to enforce.

Also flagged: **once any clickable UI exists, mouse-slot input must be gated on "the
cursor is not over UI"**, or opening an inventory panel means every click also fires
a skill. Trivial to add, catastrophic to forget. Not in the first slice because
there is no UI, but it is the first thing UI work must handle.

### The right-click conflict — resolved **[DECIDED — was [PROPOSED] in pass two]**

**RMB was `move_to`.** It drove the hold-to-path movement documented in
`docs/design/movement.md` and implemented in `scripts/Player.cs`. Making RMB a skill
slot takes that binding away, so this is not a skills-only decision: it modifies a
system that is built, documented, and working. The owner has confirmed the pass-two
recommendation, so it is now decided and `docs/design/movement.md` has been updated
to match.

**The decision, in three parts:**

**1. Click-to-move becomes the default occupant of the LMB slot, not a hardcoded
input action.** LMB starts bound to a built-in **Move** command; RMB starts empty
and is the first slot a player fills with a real skill. This is PoE's answer (LMB
defaults to a move/attack action; binding a skill there replaces it) rather than
Diablo's (where LMB/RMB are skill slots and move is fused into the default LMB
attack). PoE's version is better here because this project's movement is
*pathfinding*, not move-and-attack — fusing move with an attack skill would mean
every attack drags you toward the cursor.

**2. Add a dedicated `force_move` action (hold), suggested default `Space`.** This
is non-optional, not a nicety. The moment a player binds a skill over LMB they lose
click-to-move entirely, and `force_move` is the escape hatch. Precedent: PoE's
force-move keybind, D3's force-move, D4's "Move" action. It also solves the inverse
problem (you want to walk over a corpse/item without picking it up or attacking).

**3. This project's WASD is the thing that makes all of it safe.** In PoE1, losing
click-to-move would be crippling because it's the *only* movement. Here, WASD is a
first-class, always-available, always-overriding movement mode (`movement.md`
explicitly gives it unconditional priority). That means the worst case — a player
binds skills to both mouse buttons and never sets `force_move` — is "you play with
WASD like a twin-stick game," which is a perfectly good way to play. This
substantially de-risks the change.

**Concrete changes this implies** — a spec for godot-expert, no code written here:

`project.godot` `[input]`:
- **Delete `move_to`.** Nothing should keep a binding named after the old model.
- **Add seven actions:** `skill_slot_1`, `skill_slot_2`, `skill_slot_3`,
  `skill_slot_4` (keys `1`–`4`), `skill_slot_lmb` (mouse button 1),
  `skill_slot_rmb` (mouse button 2), `force_move` (suggested default `Space`).
- Name the mouse actions after the **slot**, not after the mouse button's current
  meaning — `skill_slot_lmb` stays correct when the player rebinds what's in it.
- `deadzone` on mouse-button actions should match the existing `move_to` value
  (`0.5`) rather than the keyboard `0.2`; it's inert for buttons but keeps the file
  consistent.

`scripts/Player.cs`:
- `Input.IsActionPressed("move_to")` (line 47) is replaced. The path-follow branch
  now runs when **either** of: the LMB loadout slot currently resolves to
  `BuiltInAction.Move` **and** `skill_slot_lmb` is held; **or** `force_move` is held.
  Both use the same mouse world position as the target, so it is one boolean
  feeding the existing retarget line, not two code paths.
- The WASD-overrides-everything branch (lines 29–45) is **unchanged**. Its
  unconditional priority is what makes this change safe.
- `Player.cs` should not grow the loadout logic itself — the "what is in the LMB
  slot" question belongs to the skill user component, and `Player.cs` should ask it.
  This keeps `Player.cs` a movement controller rather than becoming the god object.

`docs/design/movement.md`: **already updated** in this pass — the
"Path-follow (hold right mouse button)" description and the contested-binding open
question are both rewritten.

**Not in scope of this change:** rebinding UI, saved bindings, and the "cursor is
over UI" gate (no UI exists). The action names above are chosen so that none of
those require renaming anything later.

### Should Move just *be* a skill?

Tempting, and it would be elegant: every slot resolves identically, and PoE
literally ships a "Move Only" bindable action. **My call: no, not in the first
slice — it's an overreach — but keep the seam that makes it possible later.**

Why it's an overreach now: a cast is a **discrete event gated by a timer**; movement
is **continuous state** (retarget every physics frame while held, and the path
survives release). Forcing Move through the cast pipeline means the skill user needs
held-continuous-action machinery on day one — which is precisely the channelled-skill
machinery being deferred. Move also has no use time, no cost, no faction, no aim
lock, and no damage payload; it would be a definition where 90% of the fields are
meaningless, which is how a data model rots.

**The cheap middle path, which is the actual recommendation:** a loadout slot holds
either a skill instance **or** a built-in action sentinel (`BuiltInAction.Move`).
The input layer asks the loadout what's in the slot and either calls the existing
movement code or casts. That's a small discriminated union in the loadout, roughly
fifteen lines, and it means the *binding and slot* shape is already correct. If
channelled skills later make the pipeline continuous-friendly, Move can migrate into
being a real definition without touching the loadout, the input map, or saved
bindings.

### Input behaviour

- **Hold to repeat, not press to fire.** **[DECIDED]** Polled with `IsActionPressed`
  each frame, gated by the per-skill use timer. Genre standard (D2/PoE/Grim Dawn all
  let you hold to keep attacking); requiring a discrete press at 4+ casts per second
  is a wrist injury and adds no expression. Cost: rules out charge-up or tap-vs-hold
  distinctions on those bindings, and an occasional extra cast on the release frame.
  This now applies to the mouse slots too, which makes the UI gate above more urgent.
- **Per-skill use timers, no global cooldown.** **[DECIDED]** Holding two bindings
  fires both at their own rates. This is exactly the behaviour resource costs exist
  to police — and now that costs land in the same slice, they will police it
  immediately rather than a pass later.
- **Movement is FREE during casting.** **[DECIDED]** No lock, no slow, no root. You
  can fire while moving and the projectile leaves from your current position in the
  aimed direction. Rationale: no animations exist, so a lock reads as an unexplained
  freeze; and free-movement casting is a legitimate, shipped design (D3).

  **This is easy to add and hard to remove — flagged, not hedged.** Once players are
  used to firing at full speed while kiting, adding commitment later reads as a nerf,
  and the counterplay vocabulary of telegraphs and punish windows never gets built
  because there's no player-side commitment to punish. The escape hatch is a per-skill
  `MovementDuringUse` field (`Free` / `Slowed X%` / `Rooted`) so the decision can be
  made per skill later rather than globally — **author the field now, default it to
  `Free`, don't implement the other two values yet.** The owner's position is "we can
  change it later," and the field is what makes that true.

## Linear projectile — first skill spec

### Aiming **[DECIDED]**

- Direction = normalized vector from the cast origin to the **mouse world position**,
  for the player. Enemies use direction to their current target. Both go through the
  cast context's `AimDirection`, so the projectile code is identical.
- **Direction is sampled once, at the frame the cast fires, and locked.** No homing,
  no re-aim, no in-flight correction. Predictable, readable, rewards positioning, and
  matches the genre's projectile feel (D2, PoE, Grim Dawn).
- **No aim snapping / auto-aim** in the first pass. If playtesting says missing feels
  bad, or if controller support ever happens, aim assist belongs in the *player's aim
  source*, not in the skill — so it's addable without touching the skill system at
  all. This is a direct payoff of the context seam.
- Origin = caster position plus a small forward offset (authored on the definition)
  so projectiles don't spawn inside the caster's own collider. Later this becomes a
  muzzle/hand marker; the offset field is the placeholder for it.

### Travel **[DECIDED]**

- Constant velocity, straight line, `Speed` px/s from the definition. No gravity,
  no acceleration, no drag.
- **Range** (px) is the authored, design-facing number — players reason in distance,
  not seconds. A hard max-lifetime derived from `range / speed` (with a small margin)
  exists purely as a leak guard.

### Collision and hit resolution **[DECIDED]**

- Collides with: hostile hurtboxes (different faction only) and world/terrain
  obstacles (despawn on impact).
- Does **not** collide with: same-faction entities, other projectiles.
- On hit: apply the damage payload **once per target**, then despawn (pierce is 0 by
  default).
- **Track already-hit targets in a set even at pierce 0.** It's two lines now and it
  is the thing that makes pierce and chain correct later (a pierce/chain projectile
  must never hit the same target twice).

## Seams for skills affecting skills

This section answers the owner's central question directly:

> *"Is it a blocker to implementation that I don't know yet HOW skills affect each
> other?"*

**Short answer: no — with two specific exceptions, both of which are cheap enough to
just do now.** The reason it isn't a blocker is that the *taxonomy* and the
*interaction mechanism* are separable, and they have wildly different costs of being
wrong.

**The taxonomy is not a blocker at all.** Which tags exist, what they're called, and
which skills carry which — all of that is `.tres` editing. Getting it wrong costs a
rebalance, never a rewrite. Do not spend design time on it now.

**The interaction mechanism is where architecture lives** — and you don't need to
*choose* it, you only need to leave the right holes. Every mechanism you might
plausibly pick later (tag-conditional passives, support-gem-style transforms,
aura/buff application, triggered effects, per-skill upgrade trees) ultimately does
one of two things: **rewrite a skill's numbers before it fires**, or **listen for
something and react**. So there are exactly two families of seam, plus two schema
decisions that must be made now because they're painful to retrofit.

> **Pass three correction.** The sentence above — "rewrite a skill's numbers before it
> fires, or listen and react" — was too narrow, and the owner's own example exposed
> it. The upgrades that actually define builds mostly **rewrite a skill's
> *behaviour***, not its numbers, and behaviour transforms are a different *type* from
> `StatModifier` with a different application rule. Hence a sixth seam. See the
> [walkthrough](#walkthrough-projectiles-split-in-two-on-hit-decided) below.

### The six seams **[DECIDED]**

1. **A per-cast mutable parameter block, distinct from the immutable definition.**
   Definition → copy into runtime params → (later: something transforms them) →
   execute. **This is the single non-negotiable.** Without it there is nowhere for
   any upgrade to live, and mutating a shared Godot `Resource` at runtime corrupts it
   for every other caster of that skill. Every interaction mechanism writes here.

2. **Tags live on the cast context, not just the definition.** A per-cast *copy* of
   the tag set, because upgrades can add tags ("your projectiles also count as
   Area", damage conversion adding a type tag). One line, same reasoning as (1).

3. **Modifier lookup takes a query object, not a bare property read.** This is the
   minimum structural commitment and it's the one people skip. Instead of
   `caster.Stats.DamageMods`, the call is:

   ```
   caster.Stats.GetModifiers(query)   // query carries: which stat, the cast's tags,
                                      // later: actor state, the target, ...
   ```

   In the first slice the implementation **ignores everything but the stat** and
   returns all modifiers. Then "+40% increased Projectile Damage" is a filter added
   inside one method. Without the query object, adding conditions later means
   changing every call site that ever reads a modifier — which by then is
   everything. Cost today: a struct with two fields.

4. **`StatModifier` carries an optional condition, not just a number.** Today it is
   `(Type, Value, Source)`. Add an optional condition field (initially: a required
   tag set, empty = always applies). Same argument as (3): retrofitting means
   touching every site that ever *constructs* a modifier, and by the time gear and
   passives exist that's a large number of places. This is a change to
   `scripts/Stats/StatModifier.cs` — flagged for godot-expert, not written here.

5. **Cast and hit emit events, with zero listeners.** `CastStarted`, `CastFired`,
   `Hit`. "Skills affect other skills" splits into two flavours: *modifier*-flavoured
   (handled by 1–4) and *trigger*-flavoured — "when you crit, also cast X," D2's
   synergies, Last Epoch's "your Fireball also casts Meteor." The trigger flavour
   needs somewhere to listen, and if casting is a straight-line call there is
   nowhere. A C# event or Godot signal on the skill user costs nothing and closes
   the last open architectural door.

   Note: this is also where the `Triggered` provenance tag gets stamped — the
   listener that issues the follow-up cast marks that cast's context, using seam 2.

6. **`CastModifier` — a behaviour transform type distinct from `StatModifier`, applied
   at a named pipeline stage with a defined order.** New in pass three; see below.

### Seam 6: `CastModifier` — behaviour transforms **[DECIDED]**

#### Walkthrough: "projectiles split in two on hit" **[DECIDED]**

The owner's own first instinct for what an upgrade looks like. Tracing it end to end
is what produced this seam, so it's worth doing in full.

**Step 1 — where does it come from?** A passive node, an item affix, a socketed
artifact, a per-skill tree node. Architecturally this **does not matter**; all of them
end up as "a thing the caster owns that wants to alter a cast." Good — that means the
still-open upgrade-shape question doesn't block this.

**Step 2 — what does it apply to?** "Projectiles" = *casts whose tag set contains
`Projectile`*. That predicate is **identical** to the one a conditional `StatModifier`
uses, so [seam 4](#the-six-seams-decided)'s condition field and
[seam 3](#the-six-seams-decided)'s query object are shared machinery. Nothing new
needed. ✅

**Step 3 — what does it *do*? This is where pass two runs out of road.** It is **not
a `StatModifier`.** A `StatModifier` is `(StatId, Flat|Increased|More, Value)` applied
to an actor-owned `Stat` through the Flat/Increased/More formula. "Splits in two on
hit" is none of those things:

- There is no actor stat called "split." Splitting isn't a property of the character,
  it's a property of *this cast*.
- The Flat/Increased/More formula is meaningless for it. "+50% increased splitting"
  is not a sentence.
- Crucially, **writing the number does nothing on its own.** Setting `SplitCount = 2`
  only has an effect if the projectile executor contains code that reads it and
  spawns two children. A `StatModifier` never needs the consumer to know about it;
  a behaviour transform always does.

So there are **two kinds of modifier in this game**, and pass two only modelled one:

| | **`StatModifier`** | **`CastModifier`** *(new)* |
|---|---|---|
| Target | An actor-owned `Stat` | The **per-cast parameter block / tag set** (seams 1 and 2) |
| Shape | `(StatId, Flat/Inc/More, Value)` | `Apply(CastContext)` — an operation, not a number |
| Aggregation | The F/I/M formula; **order-independent** | Sequential; **order-dependent** |
| Examples | `+20 flat damage`, `+15% increased Cast Speed`, `+30% Cooldown Recovery` | `+1 projectile`, `+1 pierce`, `projectiles split on hit`, `add the Fire tag`, `pay 50% of the mana cost as life instead`, `MovementDuringUse := Rooted` |
| Player memory | "my numbers went up" | **"my projectiles fork"** |

That last row is the design argument. **Players remember behaviour transforms and
forget numeric ones.** Nobody's build identity is "+12% damage"; every build identity
in every game in this lane is a behaviour transform — PoE's supports, Last Epoch's
specialization nodes, D4's aspects, Hell Clock's unique relics, Hades' boons. Pass
two's seams were built for the forgettable kind.

**Step 4 — where does it write?** Into [seam 1](#the-six-seams-decided), the per-cast
mutable parameter block. That seam was already non-negotiable and it's already
correct. What's missing is **the stage**: pass two said "definition → copy → *(later:
something transforms them)* → execute," and that parenthesis is doing a lot of
unearned work. It needs to be a real, named stage that exists (and runs over an empty
list) from day one:

```
definition
  -> copy params + copy tags into CastContext
  -> COLLECT applicable CastModifiers (query by tags/state — seam 3)
  -> APPLY them in priority order        <-- the missing stage
  -> resolve costs, resolve numbers via StatModifiers
  -> execute
```

**Step 5 — the ordering problem, which is real and must be decided now.**
`StatModifier`s are order-independent by construction (that's what the F/I/M formula
buys). `CastModifier`s are **not**: `+1 projectile` then `double projectiles` gives 4,
the reverse gives 3. Leaving order emergent from list-insertion order produces
nondeterministic balance bugs that are miserable to diagnose.

**Decision: `CastModifier` carries an `int Priority`; application is a stable sort by
priority, tie-broken by a fixed source order (skill-attached, then item, then
passive).** Convention: additive-count modifiers occupy a lower priority band than
multiplicative ones, so "+1 arrow" always resolves before "double your arrows." Cost
today: one int and a `Sort`.

**Step 6 — the constraint this creates on executors. This is the important
consequence.**

> **Archetype executors must be written as parameterised capability sets, not as
> minimal implementations of the skill that exists today.**

The projectile executor should be authored from day one knowing the parameter list it
will eventually own — count, spread, pierce, chain, split/fork, bounce, return, homing,
size, speed, range, on-hit-explode — with every one of them **off by default** (`0` /
`false`) and only `count=1, pierce=0` actually implemented in the first slice. Adding
a capability later is then a branch in one file with an input that already exists,
rather than a change to the param block, the modifier type, and the UI at once.

The honest limit: **not every behaviour transform can be a parameter.** "Projectiles
split" can. "Projectiles orbit you instead of travelling" cannot — that's a different
executor, i.e. a different archetype. The line between "parameter on an existing
archetype" and "new archetype" is a judgement call made per-transform, and there is no
clever architecture that erases it.

**Step 7 — two concrete details this specific effect needs**, called out because they
are cheap now and painful later:

- **A generation cap.** Split-on-hit is exponential if the children can also split.
  The projectile instance carries a `ForksRemaining` counter, decremented on fork;
  children inherit the decremented value. PoE solved this by making Fork fire once by
  default for exactly this reason. **This is a field on the projectile runtime state,
  not on the definition.**
  > **Correction, found while implementing fork.** Elsewhere this doc says chain and
  > fork are both "Redirect with a decremented counter". That holds for **chain** —
  > one projectile continues toward a new target, which a single `NewDirection`
  > expresses fine. It does not hold for **fork**, which turns one projectile into
  > *two*; there is no single new direction to return. `OnHitOutcomeKind` therefore
  > has a fourth member, **`Fork`**: the parent despawns and the executor spawns the
  > children. The seam itself was right — on-hit resolution being an explicit
  > inspectable value is exactly what made adding a fourth kind a two-line change —
  > only the sentence describing it was wrong.
- **The already-hit set.** Pass two already decided to track hit targets in a set even
  at pierce 0. That decision turns out to be what stops a split child from immediately
  re-hitting the parent's target and looping. It was justified for pierce; it's
  load-bearing for split too. Keep it.

**Step 8 — what shape of upgrade did the owner just describe, without meaning to?**
"Projectiles split in two on hit" is scoped to a **tag**, not to a skill. That is the
"tag-keyed upgrade card" option from
[Divergence item 3](#3-support-gems--diverge-dont-commit-to-sockets-opinion), and it
is the *cheapest* upgrade axis to author because one card covers every projectile
skill that will ever exist. That observation drives the build-depth recommendation
below.

#### What the first slice actually builds for seam 6

Small. Deliberately:

- An abstract `CastModifier` with an `Apply(CastContext)` method, an `int Priority`,
  and an optional tag/state condition (reusing seam 4's condition type). **Zero
  subclasses.**
- The collect-and-apply stage in the cast pipeline, running over an empty list.
- The projectile parameter block authored with its full intended capability list,
  everything defaulted off.

That is roughly twenty lines and it is the difference between the build-depth plan
being additive and being a rewrite.

### The three things that genuinely are blocking

Said plainly, because "keep it flexible" isn't an answer:

- **A loadout slot must hold a `SkillInstance`, not a bare `SkillDefinition`
  reference.** A `SkillInstance` is a tiny per-owner mutable object wrapping a
  definition reference. Pass two said "one class with one field"; pass three makes it
  **three fields**, and the extras are the point:

  ```
  SkillInstance {
      SkillDefinition       Definition
      List<ISkillUpgrade>   Attached        // empty. artifacts / tree nodes / gems all live here
      (int charges, float rechargeTimer)    // cooldown state, charge-shaped from day one
  }
  ```

  Every upgrade shape that attaches to a *specific equipped skill* — a socketed
  artifact, a per-skill tree, a level, a bound item aspect — needs that list, and if
  the loadout is `List<SkillDefinition>` then adding it later is a schema change to
  the loadout, to save data, and to every UI that displays a slot. **Cost today: one
  class, three fields, an empty list. Cost later: a migration.**

- **Seam 3 (the modifier query) has to exist before there are many call sites.** It
  is cheap today and it gets monotonically more expensive with every system that
  reads a modifier. This is the one thing in this document where "we'll do it later"
  actually is a trap.

- **Seam 6 (the `CastModifier` type and its pipeline stage), new in pass three.**
  Same argument as seam 3, one level up: it is twenty lines while the pipeline is a
  straight line, and it is a rewrite of the cast path once artifacts, tree nodes and
  item aspects all want to write into it. It is also the seam that the entire
  build-depth plan below stands on — *every* candidate upgrade shape needs it, so
  building it does not commit to any of them.

Everything else — what the tags are, whether upgrades are gems or trees or items,
whether conditions are tag-based or state-based, what triggers exist — can be
decided after the first slice ships and after the game has been played. **Not
knowing how skills affect each other does not block implementation. Not leaving
these seams does.**

## Deliberate divergence from PoE

The owner's stated intent: *"I don't want to fully copy PoE, I want to change a few
things and make my own thing."* Taken seriously, here is where this design is
PoE-by-default and where divergence is actually worth it. Each entry is an opinion,
not a menu.

### 1. Flat / Increased / More — **keep it. Do not diverge.** (Opinion, firm.)

Already implemented in `scripts/Stats/Stat.cs`, so changing it has real cost — but
that's not the main reason. The main reason is that this formula isn't really "PoE's
formula," it's the best-known solution to a general problem: how do hundreds of small
bonuses stack without exponential blowup, while a handful of rare effects still feel
huge? Purely additive pools go flat and samey at scale — Diablo 3 shipped additive,
found everything felt identical, and rebuilt around multiplicative buckets; Diablo 4
repeated the mistake and repaired it the same way in Season 4. Purely multiplicative
explodes. Additive-pool-plus-independent-multipliers is the stable middle, and
several studios converged on it independently.

Divergence buys nothing player-visible — **players never see the formula, they see
the numbers.** What actually makes it *feel* like PoE is the **vocabulary**.

**Concrete cheap divergence: rename the player-facing words.** Keep `Flat` /
`Increased` / `More` as the internal enum (implemented, zero benefit to churning),
but pick your own affix language for anything a player reads — e.g. "Added 12 Fire
Damage" / "+15% Fire Damage" / "Amplifies Fire Damage by 20%". Costs a naming pass,
buys real identity distance. **Suggestion.**

### 2. Tag-conditional modifiers — **keep the mechanism, diverge on the grain.**
(Suggestion, and the most promising one here.)

Some form of "modifier with a condition" is close to inevitable; D2, Grim Dawn, Last
Epoch and PoE all have one. What differs is *what the condition is about*. PoE's
identity problem is that its conditions are almost entirely **taxonomic** — you
build by stacking keywords, and build-making becomes spreadsheet work in a wiki tab.

**The divergence worth exploring: make most interesting conditions about *game
state* rather than about skill taxonomy.** Concretely, conditions like:

- "while you have not been hit in the last 3 seconds"
- "while above 80% mana" / "while below 35% life"
- "against an enemy you have already hit this cast"
- "while moving" / "while standing still"
- "for 2 seconds after using a movement skill"

The feel is different in a way players notice: your build is defined by **how you
play** rather than by **which keyword you stacked**. Precedent outside the ARPG
lane: Hades' boons, Risk of Rain 2's items, Monster Hunter's skill conditions; inside
it, Last Epoch's "while channelling" mods are the closest thing.

Costs, honestly: state conditions must be re-evaluated as state changes, which means
`Stat` needs an evaluation context rather than being a pure function of its modifier
list, and it needs to not recompute on every read once there are many. They're also
harder to display and harder to balance (a condition that's true 95% of the time is
just a flat bonus with extra words).

**Crucially, exploring this is free right now** — it rides on exactly the same
`ModQuery` seam (seam 3). The query just carries actor state alongside tags. So this
does not need deciding before the first slice; it needs the seam, which is already
required for other reasons.

Tag-conditional modifiers should still exist. The recommendation is a **small tag
set (8–12) doing the coarse work**, and state conditions carrying the identity —
rather than PoE's hundreds of keywords doing everything.

### 3. Support gems — **diverge. Don't commit to sockets.** (Opinion.)

Support gems are the single most recognizable PoE mechanic; adopting them makes the
game read as a PoE clone more than any other decision available. They're also the
most expensive thing on this list to *maintain*: every support × every skill is a
compatibility matrix, and PoE spends enormous ongoing effort on it. For a solo
project that's a content treadmill you'd be signing up for permanently.

Keep the **structural hook** (seam 1) regardless — every upgrade shape writes
through it. But the shape itself is open. Ranked:

- **Per-skill upgrade trees (Last Epoch's model)** — each skill has its own small
  tree; you may only specialize a limited number of skills. **This is my
  recommendation.** It fits everything else that's now decided: six chunky slots,
  skills scale only by modifiers, classes differ by trees and starting points. It
  makes the loadout choice enormously meaningful (specializing is a commitment), it
  gives each skill a distinct identity without a combinatorial matrix, and it makes
  "upgrades that only enhance certain skills" — the owner's own phrasing — the
  *default* case rather than a special one. Cost: authoring is per-skill and doesn't
  amortize, so a new skill is a bigger job.
- **Skill-transforming loot (D3 legendary powers / D4 aspects)** — items that rewrite
  a specific skill. Ties skills to the loot loop the game will need anyway. Cost:
  builds are gated by drops, which is a feel-bad if the drop rate is wrong.
- **Tag-keyed upgrade "cards"** — an upgrade attaches to a *tag*, not a socket:
  "all your Projectile skills fork." Cheaper than gems because one card covers many
  skills, and it reuses the tag system you're building anyway. This is the
  closest-to-supports option while still being visibly its own thing.

**Pass three revisits this and partly reverses it.** The recommendation below is a
socket system, which is structurally support-gems-with-the-serial-numbers-filed-off.
The reasoning for the reversal — and the specific change that defuses the
maintenance objection — is in
[Where build depth comes from](#where-build-depth-comes-from-proposed). It remains
**[PROPOSED]**.

### 4. The six-slot loadout — **keep six, apply pressure with costs, not rules.**
(Opinion.)

Worth naming honestly: going 4 → 6 walks *toward* PoE, not away. PoE's failure mode
is that with many bound skills most become set-and-forget and combat collapses to one
button. Six slots raises the bar for making each slot matter — which is the third
independent reason cost belongs in the first slice.

Considered and rejected as the divergence: role-constrained slots (one damage skill
max, mouse slots restricted by category). D3 shipped that and removed it; it costs
expression and buys only new-player legibility. The right pressure is **resource
cost, cooldowns, and skills that are genuinely good at different jobs** — mechanics,
not rules.

### 5. Skill supplies the base, character supplies the scaling — **keep, but know
what it costs.** (Already decided; noted here for completeness.)

This is PoE's spell model. The alternative — D2/D3's, where skill damage is a
percentage of weapon damage — ties power to loot, which is arguably more
ARPG-appropriate. The reserved "derives base from weapon" flag on the definition is
the door, but walking through it later is a rebalance of every skill, not a code
change.

## Where build depth comes from **[PROPOSED]**

> **Everything in this section is [PROPOSED].** The owner explicitly said they don't
> have an answer yet and want options. Nothing here is decided; nothing here ships in
> the first slice. What it *does* do is establish that the seams above are sufficient
> for every option considered, plus one addition ([seam 6](#seam-6-castmodifier--behaviour-transforms-decided))
> that is already decided for other reasons.

Stated goal, taken at face value: **PoE-level build flexibility.** Two honest things
up front. First, PoE's flexibility is bought with thousands of authored nodes,
hundreds of gems, ~1500 uniques, and a full studio maintaining the interactions
forever — that specific route is not available to one person. Second, and more
usefully: **that isn't actually where PoE's flexibility comes from.** It comes from
having several axes of choice that *multiply*, and only one of PoE's axes is
genuinely large. The rest of this section is about which axes to buy.

### The actual mechanism: enumerated axes vs combinatorial axes

This is the decision criterion, so it goes first.

- An **enumerated** axis gives you roughly *N* build identities for *N* units of
  authoring. Passive tree nodes, unique items, per-skill tree nodes, paragon glyphs.
  Linear return on effort. You can balance it, because you can read all of it.
- A **combinatorial** axis lets the player pick *k* things from *N*, giving
  ~C(N, k) identities for *N* units of authoring. Support gems, dual classes, Hades'
  boon-per-slot, socketed artifacts. **Superlinear return on effort.** You *cannot*
  balance it — you can only bound it and patch outliers. PoE does not balance its
  combinations either; it nerfs the ones that get out of hand.

> **For a solo developer, build depth per authoring hour is the entire decision, and
> combinatorial axes win by a wide margin. The price is that balance becomes emergent
> rather than curated, and you have to accept that knowingly.** If that trade is
> unacceptable, the realistic ceiling is a Grim-Dawn-sized game made by a studio, not
> a PoE-sized one made alone.

### The design space, with precedents

For each game: what the axes are, and how they multiply.

**Path of Exile** — three axes.
1. *Passive tree* (~1500 nodes, of which maybe 120 notables/keystones actually shape
   a build). **Enumerated.**
2. *Skill gem + up to 5 support gems*, from ~250 actives × ~250 supports.
   **Combinatorial, and it is the only truly combinatorial axis PoE has.**
3. *Gear affixes + ~1500 uniques*, many transformative. **Enumerated.**

The product is huge, but note where it comes from: axis 2 supplies the multiplication
and axes 1 and 3 supply the *specific* interactions that make a given multiplication
worth finding. PoE's real cost centre is axis 2's compatibility matrix — every
support × every active is a pair someone has to make not-broken.

**Last Epoch** — three axes.
1. *Class → one of 3 masteries + class passive trees.* Enumerated, moderate.
2. **Per-skill specialization trees** — you may specialize only **5 skills**, each
   with its own ~20-node tree containing genuinely transformative nodes ("Fireball
   now fires 3 projectiles in a cone", "Fireball ignites"). Enumerated, but
   **exceptionally high value per node**, because a node authored for one skill can
   assume everything about that skill — *there is no compatibility matrix at all.*
3. *Crafted affixes (deterministic) + uniques.* Enumerated but auto-generated.

The 5-specialization cap is the load-bearing part: it's a **commitment device**. Depth
comes as much from what you can't take as from what you can.

**Grim Dawn** — three axes, and the highest-leverage one in the genre.
1. **Dual mastery: pick 2 of 9 classes.** 36 combinations from 9 authored trees.
   **Combinatorial, and by far the best ratio on this page.**
2. *Devotion tree* (~55 constellations), cross-class, with proc skills you bind to
   triggers — a second combinatorial layer (which constellation, bound to which
   trigger).
3. *Gear affixes, item-granted skills, sets, components/augments.*

**Diablo 4** — three axes.
1. *Skill tree*, small and class-locked, with 2–3 upgrade variants per skill (a
   miniature per-skill choice).
2. **Legendary aspects** — extractable from the item that dropped them and slottable
   into any item. ~12 slots from a pool of ~250. This is D4's answer to "attach an
   artifact," and the extraction step is what makes it feel like *building* rather
   than *waiting for a drop*.
3. *Paragon boards + glyphs* — endgame, mostly numeric, enumerated.

**Hades** — run-scoped, and the best build-feel-per-authored-item in this list.
1. **Boons: ~8 gods × ~6 core boons, each attaching to one of 5 slots** (Attack,
   Special, Cast, Dash, Call). Any god's boon can fill its slot, plus duo boons
   requiring specific pairs. ~30 core items produce thousands of felt builds.
2. *Weapon aspects* (4 per weapon), *Mirror of Night* (small, persistent).

The lesson worth stealing: **attach upgrades to a slot/role rather than to an
enumerated list of specific abilities, and the same authored boon works forever.**

**Hell Clock** (Rogue Snail, 2025) — the owner's reference; researched rather than
recalled, sources at the end of this section. Six axes, which is a lot.
1. *Active skills* — start with four (Attack, Split Shot, Spectral Knives, Shadow
   Dash), more unlock through Acts and boss kills, permanently purchased for 100 Soul
   Stones each. Players run roughly 3–5. Skills carry **tags** — Projectile, Spell,
   Melee, Marksman, Area of Effect, Single Target, Mobility, Enchantment, Prayer,
   Summon — which determine what scales them, and they have **both mana costs and
   cooldowns** (both of which *increase* as the skill levels).
2. **Blessings of Proficiency** — run-scoped, drafted 3-at-a-time on each level-up,
   filling **per-skill upgrade slots capped by rarity: 4 Common, 3 Magic, 2 Rare, 1
   Epic per skill.** Gold rerolls the draft. Because the draft pool is weighted to
   your equipped skills, **equipping fewer skills concentrates your upgrades** — the
   game's own guides recommend running 2–3 skills for that reason. That is a
   beautiful piece of design: the commitment device is *emergent from the draft*
   rather than being an authored cap.
3. **Relics** — a **character-wide Reliquary**, laid out on a **spatial grid** (Small
   1×1, Large 1×2, Grand 1×4, Exalted 2×2), with Common/Magic/Rare/Unique rarities,
   primary affixes (Damage, %Life, %Mana, **Cooldown Recovery**, Crit Damage) and
   secondary affixes (resistances, Soul Stone gain, Conviction, Life, Mana). Persistent
   across runs, upgradeable 5 times with Soul Stones, craftable with Clockmaker's
   Tools (replace or lock an affix). **Unique relics are the transformative ones** —
   *Blunderbuss* merges Split Shot's projectiles into one slug; *Stolen Weapons* lets
   minions use Split Shot; *Broken Mirror* grants a permanent clone that attacks with
   Repeater. Three swappable relic loadouts.
4. **The Great Bell** — persistent, atlas-style passive tree. Notably it contains
   *Reliquary size* nodes, i.e. it's partly a meta-axis that buys you more of axis 3.
5. **Constellations** — a second tree unlocked in Act 2, three devotion paths (Fury,
   Discipline, Faith), which also bias which unique relics are offered.
6. *Gear*, one item per slot, three loadouts.

**What reviewers say works and doesn't** — worth more than the mechanics list:
- Works: the relic effects visibly generate build ideas; min-maxers find it
  "daunting but hugely exciting"; the combat sandbox is fun *specifically when you can
  watch your build play out*.
- Doesn't: **"most relics are inconsequential or only benefit one specific ability,"**
  duplicates pile up enormously, and the early game has too few relics and too few
  reliquary slots to have build variety at all. And the sharpest one:
  **stripped of the build depth, the core combat is flat** — "little more than frantic
  clicking and potion-chugging."

Two lessons for this project, and they're the most valuable things in this section:

> **(a) A character-wide upgrade item that names a specific skill is the worst of both
> worlds.** It's an enumerated authored item whose value is conditional on an
> unrelated choice you already made, so most drops are dead. That is precisely the
> Hell Clock relic complaint, and it's structural, not a tuning problem.
>
> **(b) Do not let the build systems be the only source of fun.** If combat is flat
> without them, the game is unplayable for the first several hours and unshippable as
> a demo. This is a direct argument for the priority order below: hit feedback and a
> second/third skill come *before* any upgrade axis.

*(Sources for the Hell Clock material are listed at the end of this section. Note one
conflict: at least one outlet reports the proficiency caps as "1 Epic, 2 Rare, 3
Magic, 6 Common"; the wiki and Maxroll both say 1 Epic / 2 Rare / 3 Magic / 4 Common.
The exact numbers don't matter here — the *shape* does.)*

### Recommendation for this project

Three axes, deliberately few, chosen for ratio rather than for resemblance to PoE.
Plus one strong optional fourth.

#### Axis A — Character passive tree *(enumerated, small, mostly numeric)*

Not 1500 nodes. Target **150–250 nodes, ~85% pure numbers and ~15% notables with a
real effect.** Classes are a starting position and tree access, which is
[already decided](#how-skills-interact-with-the-stat-system).

Its job is **not** to create build identity. It's to (i) create the resource-spending
gradient that makes pathing a decision, and (ii) enforce specialisation by being too
small to take everything. Numeric nodes are the cheapest content in the game to
author and the least interesting — which is fine, that's what connective tissue is.

Honest cost: it is a permanent balance surface, it is the single biggest authoring job
in the project, and it is the thing that most makes the game *look* like PoE. It
should be authored **last** of the three axes, for reasons in the
[priority order](#priority-order-decided).

#### Axis B — Artifact sockets on skills *(combinatorial — this is where the leverage is)*

This is where the owner's "attach special artifacts" idea goes, and the specific shape
matters a great deal. The owner asked the right question: attach to a **skill**, a
**slot**, a **tag**, or the **character**? Answering each:

| Attach to | Buys | Costs |
|---|---|---|
| **Character** (Hell Clock relics, D4 aspects, PoE tree) | Cheapest to build and author; simple UI | Produces "my *build* does X" not "this *skill* does X" — weakest per-skill identity. And if the effect names a specific skill, it's dead weight for everyone else — the Hell Clock relic problem |
| **Tag** ("all Projectile skills fork") | **Best amortization available**: one card covers every projectile skill that will ever exist. Reads clearly. It is literally how the owner phrased their own example | Can't express skill-*specific* identity; pushes builds toward mono-tag stacking, which is PoE's spreadsheet failure mode — if all your power is `Projectile` cards, you take only projectile skills and the six-slot loadout collapses |
| **Skill instance** (Last Epoch specialization, PoE sockets) | Strongest identity; real commitment; makes the six-slot loadout choice matter enormously, which is the stated goal of six slots + costs | Authoring doesn't amortize (per-skill trees), *or* you inherit PoE's compatibility matrix (generic supports × every skill) |
| **Slot** (Hades boons) | Investment survives swapping the skill, so experimenting is free | In a persistent ARPG that's arguably *too* forgiving — no commitment, and commitment is what makes a build feel like yours |

**Recommendation: attach to the skill instance, but make the socketed things
tag-scoped generic modifiers.** Combine the two middle rows rather than picking one.

Concretely, what the player does:

> Each equipped skill instance has **artifact sockets** — start at 2, grow to 3–4 via
> the tree or via an explicit "specialize" action. **Artifacts** are dropped and
> crafted items carrying a `CastModifier` (behaviour), a `StatModifier` (numbers), or
> both. Every artifact declares a **tag requirement**, and it only sockets into a skill
> whose tags satisfy it.
>
> > *Forking Shard* — requires `Projectile` — *this skill's projectiles split into 2
> > on their first hit; −25% projectile damage.*
> >
> > *Kindling Core* — requires `Fire` — *this skill's hits also apply a burn for 30%
> > of the damage dealt over 3s.*
> >
> > *Afterimage* — requires `Movement` — *+1 charge; −20% cooldown recovery.*

Why this specific shape:

1. **The tag requirement *is* the amortization.** One `Forking Shard` works in every
   projectile skill you ever write. You implement the fork capability once in the
   projectile executor and author the artifact once; the felt combinations are
   *artifacts × compatible skills*. That's the combinatorial win, from a small
   authored set.
2. **The socket is the commitment.** 6 slots × 2–3 sockets ≈ 15 artifact decisions in
   a build, from a pool of maybe 60. Realistically a few hundred genuinely distinct
   builds out of ~60 authored items — which is plenty, and is roughly Hades' ratio.
3. **It has no compatibility matrix**, which was pass two's whole objection to support
   gems. Compatibility is *declared by tags* rather than being a per-pair authored
   relationship. PoE has to think about "Melee Splash + Cyclone" specifically; here,
   if the effect is a `CastModifier` writing a parameter the archetype already owns,
   compatibility is structural.
4. **It avoids the Hell Clock dead-relic failure** because artifacts are *never* named
   after a specific skill. Every drop is usable by *some* skill you might run.
5. It is recognizably not support gems: no gem levelling, no quality, no socket
   colours, no link colours, and the gate is tags the player can already read on the
   skill rather than a parallel colour economy to learn.

**Costs, owned rather than hidden:**

- **This reverses pass two's recommendation against support gems.** It is structurally
  a support system. The reason it changes: pass two's objection was the compatibility
  matrix and the permanent content treadmill, and tag-gating plus parameterised
  executors removes the matrix. What remains true is that this is the PoE-est axis
  available and the owner should choose it knowing that.
- **The residual maintenance burden is real and must be handled explicitly:** every
  new archetype has to implement the capabilities that existing artifacts assume, or
  those artifacts silently do nothing when socketed into it. **Requirement: an
  artifact declares which capability it needs, an archetype declares which
  capabilities it supports, and a mismatch is a validation error, not a silent no-op.**
  Silent no-ops in a socket system are the single worst bug class you can ship — the
  player cannot tell a dead artifact from a weak one.
- **The number of behaviour capabilities per archetype is the real budget for build
  depth.** N capabilities × M skills sharing the archetype = N×M felt upgrades from N
  implementations. This is the number to grow deliberately.

#### Axis C — Gear affixes *(enumerated but auto-generated — nearly free)*

Standard prefix/suffix rolls over the existing `StatModifier` machinery, made
conditional via seams 3 and 4. An affix is a row in a table: authoring cost is close
to zero, and it's the loot loop the game structurally needs anyway.

**Keep behaviour off gear.** The separation that keeps the whole thing legible:

> **Gear = numbers and conditions. Artifacts = behaviour. Tree = numbers, plus a
> handful of keystones.**

This is also where [divergence item 2](#2-tag-conditional-modifiers--keep-the-mechanism-diverge-on-the-grain-suggestion)
pays off: game-state conditions ("while you haven't been hit for 3s", "while above 80%
mana") on gear affixes cost nothing extra — they ride seam 3 — and they're where the
game's identity can differ from PoE without authoring a single new system.

#### Axis D — Dual class *(optional, and the highest-leverage item in this document)*

**Strong suggestion, not a recommendation, because it constrains axis A's structure.**

It is already decided that classes differ *only* by which tree they get and where they
start. If that's true, letting a character take **two** class trees is nearly free
content-wise — it's a UI problem and a balance problem, not an authoring problem.
**6 authored class trees → 15 build identities.** Grim Dawn built an entire genre
reputation on exactly this with 9 → 36.

Nothing else on this page comes close on build-feel per authoring hour.

**But it forks a decision that must be made before the tree is authored, not after:**

- **One shared web** (PoE-style, classes are entry points into one graph) — cannot
  support dual class meaningfully, because there's nothing separate to combine.
- **Separate per-class trees** (Last Epoch / Grim Dawn) — dual class becomes available
  for free.

That is a structural fork with no cheap undo. It doesn't need deciding now, but it
must be decided **before any tree authoring starts**, and it should be on the record
as such.

### What a finished build looks like

Concrete enough to argue with:

> **Fire Fork Ranger.**
> Loadout: LMB = *Move* · RMB = **Fireball** · 1 = **Dash** · 2 = **Fire Wall** ·
> 3 = **Warcry** · 4 = empty.
>
> **Fireball's 3 sockets:** *Forking Shard* (splits into 2 on first hit, −25%
> projectile damage) · *Kindling Core* (hits also burn; requires `Fire`) ·
> *Overdraw* (+40% projectile speed and range, +30% mana cost).
> **Dash's 2 sockets:** *Afterimage* (+1 charge, −20% cooldown recovery) ·
> *Ember Trail* (leaves a burning trail; requires `Movement`).
>
> **Tree:** Ranger start → projectile & attack-speed cluster → crosses into
> Pyromancer's fire cluster (axis D) → keystone *Kindled*: while you have ignited an
> enemy in the last 3 seconds, **more** Fire damage.
>
> **Gear:** rings and gloves rolled for `+% Fire Damage`,
> `+% Projectile Damage while you have not been hit recently`, `+% Cooldown Recovery`.
>
> **The identity:** splitting shots that ignite, held together by a state condition
> that rewards opening aggressively and then kiting. You feel it as a playstyle, not
> as a number.

**The point of the sketch:** exactly **two** pieces of that build required bespoke
code — the projectile *fork* capability and the *trail* capability. Everything else is
data over machinery that already exists. That ratio is the whole argument.

### What this means for the first slice

**Almost nothing here ships now.** No sockets, no artifacts, no tree, no drops. What it
constrains:

1. **[Seam 6](#seam-6-castmodifier--behaviour-transforms-decided) must exist** — the
   `CastModifier` type, the pipeline stage, the ordering rule. *Every* option above
   needs it, so building it commits to none of them. Already decided, for this reason
   among others.
2. **`SkillInstance` must carry an `Attached` list**, not just a definition reference.
   Artifacts, per-skill tree nodes, gems and bound aspects all live in exactly that
   list. Already folded into the blocking-items list above.
3. **The projectile parameter block should be authored with its full intended
   capability list, everything off.** Free now, awkward later.
4. **Nothing else.** Sockets, artifact items, drops, crafting, the tree, dual class —
   none of them constrain the first slice, and the "artifact declares a capability /
   archetype declares its capabilities" validation isn't needed until there are two
   archetypes and one artifact.

### Hell Clock sources

- [Hell Clock Beginner's Guide — Maxroll](https://maxroll.gg/hell-clock/getting-started/hell-clock-beginners-guide)
- [Hell Clock Relic Guide — Maxroll](https://maxroll.gg/hell-clock/getting-started/hell-clock-relic-guide)
- [Hell Clock Skills Guide — Maxroll](https://maxroll.gg/hell-clock/getting-started/hell-clock-skills-guide)
- [Hell Clock Blessings Guide — Maxroll](https://maxroll.gg/hell-clock/getting-started/hell-clock-blessings-guide)
- [Proficiencies — Hell Clock Wiki](https://hellclock.wiki.gg/wiki/Proficiencies)
- [Great Bell Passive Tree Guide — Maxroll](https://maxroll.gg/hell-clock/guides/great-bell-passive-tree-guide)
- [Best Tips To Start Playing Hell Clock — TheGamer](https://www.thegamer.com/hell-clock-beginner-tips-guide/)
- [Hell Clock Review — Wolf's Gaming Blog](https://wolfsgamingblog.com/2025/08/11/hell-clock-review-the-grind-the-glory-and-the-grim-reapers-clock/)
- [Hell Clock Review — GameRant](https://gamerant.com/hell-clock-review/)
- [Hell Clock Review — NoobFeed](https://www.noobfeed.com/reviews/hell-clock-review)

## Priority order **[DECIDED]**

The owner asked for a prioritised plan. This is it, ordered, with dependencies. It
supersedes any implicit ordering elsewhere in this document.

The ordering principle: **structurally load-bearing things first, then the minimum
needed to *evaluate* the game, then content, then depth axes.** Anything that can be
decided after playing is deliberately pushed behind playing.

### Milestone 1 — "One skill, one dummy, numbers on screen"

The first combat slice. Order *within* it matters more than usual.

| # | Item | Why here | Depends on |
|---|---|---|---|
| 1 | **Input map + `Player.cs` rebinding**: drop `move_to`, add the six skill-slot actions + `force_move`, LMB slot defaults to `BuiltInAction.Move` | First, because it changes *already-shipped* code and everything else attaches to the new input layer. Doing it last means writing the skill user against a binding scheme you're about to replace | Nothing — decided this pass |
| 2 | **Stats plumbing**: entity-agnostic stat holder; `Life`, `Mana`, damage-mod carrier, `AttackSpeed`, `CastSpeed`, `CooldownRecovery`; `Stat` computing against an externally supplied base; `GetModifiers(query)`; condition field on `StatModifier` | Second because it's the most expensive thing in the project to retrofit and *everything* reads it. There is nothing playable at the end of this step — resist skipping it for that reason | 1 (loosely) |
| 3 | **Damage payload + health/damage receiver + death + the inert target dummy** | Third because it's *a target to shoot*. Step 4 has nowhere to land without it. Deliberately dumb: no AI, no movement, no retaliation | 2 |
| 4 | **Skill data model**: `SkillDefinition` base + `ProjectileSkillDefinition`, tags as a flags enum, `SkillInstance` (def + empty `Attached` list + charge-shaped cooldown state), cost entries, `Cooldown` + `MaxCharges`, `MovementDuringUse` | Authoring only, nothing executes. Cheap, and it's the schema everything downstream assumes | 2 |
| 5 | **Skill user / executor**: loadout, per-skill timers gating on `max(useTime, cooldown)`, cost payment, cast context assembly (mutable params **and** mutable tags), the empty `CastModifier` stage, cast/hit events with zero listeners, typed failure reasons, `ResolveSpeedStat(tags)` | The architectural centre. Fifth because it needs 2, 3 and 4 to exist before it's testable | 2, 3, 4 |
| 6 | **The projectile executor** — written as a capability set with everything off (count=1, spread=0, pierce=0, splits=0, chain=0, homing=off), only the trivial path implemented | This is the cheapest possible moment to shape it right; see [seam 6 step 6](#seam-6-castmodifier--behaviour-transforms-decided) | 5 |
| 7 | **Debug readout + minimal hit feedback** (a flash, a floating number, life/mana as text) | **Not polish — this is the instrument.** Without it you cannot tell whether 0.5s use time and 20 damage feel right, and every number authored from here on is guessed. Cheap and non-negotiable | 3, 6 |
| 8 | **Play it, for an hour, seriously** | A real work item, and the input to milestone 2 | 7 |

### Milestone 2 — "It fights back"

| # | Item | Why here | Depends on |
|---|---|---|---|
| 9 | **Skills 2 and 3**: a dash (tagged neither `Attack` nor `Spell`, cooldown + charges, `Movement` tag) and a melee or area skill (second archetype) | Six slots and cost pressure are *unevaluable* with one skill. The dash proves the cooldown lever and the no-speed-stat path; the area skill is the first real test of the archetype abstraction | 6 |
| 10 | **One hostile enemy**: pursues, has a contact or telegraphed attack, dies | The thing that makes kiting, cost pressure and cooldowns mean anything. "Does combat feel good" is unanswerable before it exists. But it comes *after* multiple skills, because one enemy vs one skill teaches you nothing | 3, 9 |
| 11 | **An enemy casting the same `Fireball.tres` the player uses** | The validation of this architecture's central claim. Do it early and cheaply: if one definition doesn't actually serve both sides, everything downstream is built on a lie. Should be a small job — if it isn't, that *is* the finding | 5, 10 |
| 12 | **Play again, and answer: does free-movement-while-casting feel right?** If not, implement the `Slowed`/`Rooted` values of `MovementDuringUse` here | The canonical decide-after-playing question. It's flagged as easy-to-add-hard-to-remove, so it needs an explicit revisit rather than drifting into permanence | 10 |

### Milestone 3 — "Numbers get interesting"

| # | Item | Why here | Depends on |
|---|---|---|---|
| 13 | **Make `GetModifiers(query)` actually read tags**; ship 3–5 tag-conditional modifiers applied via a debug command | Cheap now the seam exists, and it's the first proof the tag system does anything at all | 2, 5 |
| 14 | **The first real `CastModifier`s** — implement 2–3 projectile capabilities (extra projectile, pierce, fork) and apply them by debug command | **Validates the seam-6 finding before an item system is built on top of it.** If cast modifiers turn out awkward, find out here applying three by hand, not after authoring sixty artifacts | 6, 13 |
| 15 | **Damage types + resistances** | "Fire build" is one of the cheapest build identities to author and the payload is already an object, so this is additive. Needed before any depth axis is meaningful | 3 |
| 16 | **Life/mana bars, a hotbar, and the "cursor is over UI" input gate** | Here because there's finally enough to display, and because the gate becomes mandatory the moment a clickable panel exists. Debug text is genuinely sufficient until now | 7 |

### Milestone 4 — "Build depth, axis by axis"

Order here is the counter-intuitive part and the most important thing in this section.

| # | Item | Why here | Depends on |
|---|---|---|---|
| 17 | **Gear + affixes first — before the tree** | Reuses `StatModifier` + seams 3/4 with almost no new machinery, gives the loot loop the game needs anyway, and it is the *cheapest possible way to find out which modifiers are actually fun*. Tree nodes are just curated affixes, so you want the affix vocabulary first | 13, 15 |
| 18 | **Artifacts + sockets** (axis B, if adopted) | After gear because it reuses the item pipeline; after #14 because it is the productisation of cast modifiers | 14, 17 |
| 19 | **The passive tree** (axis A) | Last of the three axes, and the single biggest authoring job in the project. By now the good modifiers are known, tags do something, and the affix vocabulary exists — the tree becomes mostly assembly rather than guesswork | 17, 18 |
| 20 | **Skill acquisition** — how skills are obtained at all | Deliberately last of the open design questions, because the right answer depends on how scarce skills should be, which depends on how much artifacts amplify each one | 18 |

### Explicitly not now, and why

- **Any upgrade item, socket, gem or tree.** The seams are free; the systems are not.
  Nothing in milestones 1–3 gets better for having them.
- **Channelled skills and `ChannelRate`.** Needs a continuous-cast pipeline; nothing
  yet demands one.
- **Crit, accuracy/evasion.** Pure additions to a payload object that already exists.
- **Save/load, inventory, skill acquisition.** None of it changes a seam.
- **Auras/toggles and "do they occupy the six slots?"** Answer it when there is an aura.
- **Dual class.** No code until there is a tree — but see the structural fork above.

### Where enthusiasm will pull work earlier than it should, and why that hurts

Flagged specifically, because these are the plausible failure modes:

- **The passive tree.** It's the most enjoyable thing in the project to *design* and
  the most expensive thing to *author*, and every node in it is a guess until
  modifiers, tags and conditions have been played with. Building it at milestone 2
  means authoring ~200 nodes of numbers you will then rebalance from scratch — and
  it *feels* like enormous progress the whole time, which is what makes it dangerous.
  It goes after gear.
- **The artifact/socket system, right now.** It's the exciting idea from this pass and
  it is genuinely the right long-term axis — and it is genuinely wrong to build before
  step 14. Designing socket rules against zero played evidence about which
  capabilities feel good means designing the interesting part blind.
- **More skills, too early.** Skills 2 and 3 are load-bearing (they force the second
  archetype and the cooldown lever). Skills 4 through 10 are content, and every one
  authored before the upgrade system exists is one you re-tune afterwards.
- **Enemy AI depth.** A dumb chaser is enough for a long time. Telegraphs, attack
  patterns and AI state machines are their own discipline, and they only pay off once
  player-side commitment exists to punish — i.e. after item 12.
- **Art and animation.** Every "a cast lock would read as an unexplained freeze"
  argument in this document is contingent on there being no animation, which makes
  animation look load-bearing. It isn't. What blocks evaluation is *hit feedback* —
  a flash and a number — and that's item 7.

### Design decisions and when they must be made

**Already settled (this pass):** the RMB binding · cooldown shape, recovery stat,
start-on-cast, charge-shaped state · tag-derived speed stat · the `CastModifier` seam
and its ordering rule · `DamageDeliveryKind` on the payload.

**Must be decided before specific code is written:**

| Decision | Deadline | Why it can't wait |
|---|---|---|
| ~~What capability list does the projectile archetype claim?~~ | ~~Before item 6~~ | **Closed during M1** — owner confirmed the doc's suggested list; see "Open questions" below |
| Does gear carry behaviour, or only numbers? | Before item 17 | Determines whether affixes need the `CastModifier` path. *Recommendation: numbers only* |
| Do sockets belong to the skill instance, the slot, or the character? | Before item 18 | Changes the data model and the entire UI. *Recommendation: skill instance* |
| One shared passive web, or separate per-class trees? | Before item 19 | Determines whether dual class is ever possible. No cheap undo |

**Can wait until after playing:** movement-during-use · taxonomic vs game-state
conditions on modifiers · which tags exist · skill acquisition · whether auras occupy
the six slots · damage types.

## Trade-offs and doors this closes

- **Skill-carries-base-damage** makes weapon/gear-driven damage the exception rather
  than the default. See divergence item 5.
- **Definitions as Godot `Resource`s** buys editor authoring and trivial sharing
  between player and enemies, but Resources are *shared instances* — runtime mutation
  leaks across every caster. That is precisely why the per-cast runtime parameter copy
  is mandatory rather than optional. Secondary cost: `.tres` files produce noisy diffs.
- **No cast lock / full mobility while casting** is easy to add and hard to remove.
  See the input section — the `MovementDuringUse` field is the planned escape hatch.
- **Hold-to-repeat** closes off charge/tap-hold mechanics on all six bindings, and
  makes the "cursor is over UI" gate mandatory once any UI exists.
- **Costs blocking rather than degrading** means a player who mismanages mana simply
  stops working for a moment. That's the genre standard but it is a hard stop; a
  degrading alternative (weaker cast at low resource) would be smoother and is not
  chosen, because "did my skill fire or not" must be unambiguous.
- **`IgnoresResourceCosts` defaulting true for enemies** means enemy pacing is
  entirely an AI-cadence problem. When enemy AI is designed, that's where the
  throttle has to live; there is no automatic resource-based limiter.
- **Balancing against a stationary dummy** systematically over-values single-target
  burst and under-values area damage, kiting, and anything that cares about enemy
  movement. Numbers produced in the first pass are placeholders and explicitly
  non-binding.
- **Separate attack and cast speed** doubles the relevant stat surface immediately
  and means every future speed-affecting modifier must pick a side — which is the
  point, but it's also a permanent tax on affix authoring.
- **Tag-derived speed** makes `Attack` and `Spell` *structural* tags: re-tagging a
  skill now changes its scaling, so "tags are cheap to change" no longer holds for
  those two. It also forbids a skill being both, which will eventually be a
  constraint someone wants to break. See the structural/descriptive tag split.
- **Cooldown recovery as a rate divisor** means cooldown reduction can never reach
  zero, which is correct, but it also means the *first* 20% feels much better than
  the tenth 20% — diminishing returns that players sometimes read as the stat being
  weak. Tooltip wording has to carry that; the maths won't.
- **Cooldown starting on cast start** couples nothing to cast speed, deliberately.
  When cast times exist, a long-cast skill will have part of its cooldown burning
  during the cast, which shortens the *effective* downtime. That's intended and worth
  remembering when authoring the first long cast.
- **`CastModifier` being order-dependent** is a permanent balance hazard that
  `StatModifier` doesn't have. The `Priority` field bounds it; it does not remove it.
  Every new cast modifier is a question of "where in the order does this go," and
  getting that wrong produces bugs that look like balance problems.
- **Executors as capability sets** means the projectile executor will accumulate
  branches over time and eventually be the most complex file in the combat code. That
  is the accepted price of behaviour transforms being data-driven; the alternative
  (one executor per behaviour combination) is worse.

## Alternatives considered and rejected

- **Skills as subclasses / one script per skill.** Rejected. It's the fastest way to
  ship the first projectile and the worst way to ship the tenth: behaviour and data
  fuse together, designers can't tune without touching code, and the "same skill for
  player and enemy" constraint gets satisfied by inheritance hierarchies that
  eventually need multiple inheritance. (Note: *archetypes* are definition
  subclasses, which is different — a handful of behaviour families, not a class per
  skill.)
- **Skills owned by the player, with a separate enemy attack system.** Rejected — it
  is the direct violation of the stated hard constraint, and it doubles the content
  pipeline forever.
- **A "Fireball (Enemy)" duplicate definition per faction.** Rejected in favour of
  faction living in the cast context. Duplicates drift.
- **One unified `UseSpeed` stat.** Rejected by owner decision — a speed stat everyone
  wants equally isn't a build decision.
- **An explicit `SpeedStat` field on the definition.** *Rejected in pass three
  (it was pass two's decision).* It creates two independent, silently divergent
  notions of "is this a spell" — the tag that every affix reads and the field that
  only the timer reads — and no UI would ever surface the disagreement. Tag-derivation
  makes that class of bug unauthorable. `None` is better expressed as the absence of
  both tags than as a magic value.
- **A `CooldownStart` enum on the definition.** Rejected — cooldown starts on cast
  start, full stop. Channelled skills will want otherwise, but that's one line in one
  executor, not a change to data shape. Contrast `MaxCharges`, which *does* get a
  field, because it changes the shape of runtime state everything reads.
- **"-X% cooldown" as subtractive reduction.** Rejected in favour of a recovery
  *rate* divisor. Subtractive reduction reaches 100% and breaks; a divisor is
  asymptotic. PoE made this exact migration.
- **Expressing behaviour transforms as `StatModifier`s.** Rejected — "+50% increased
  splitting" is not a sentence. They are a separate type (`CastModifier`) with a
  separate application rule, because they are order-dependent and because they
  require the consumer (the executor) to know about them.
- **Character-attached upgrade items that name a specific skill** (the Hell Clock
  relic shape). Rejected pre-emptively for the build-depth plan — it's an enumerated
  authored item whose value is conditional on an unrelated earlier choice, so most
  drops are dead weight. Reviewers name this as Hell Clock's main build-system
  complaint. Artifacts should gate on *tags*, never on a skill name.
- **A single scalar mana cost.** Rejected — hybrid costs and cost-converting
  modifiers both need a collection, and life costs need percentage modes.
- **Letting a skill's own cost kill the caster.** Rejected — reads as a bug, and
  makes life-cost skills unusable rather than risky.
- **"No mana pool means the cast is blocked."** Rejected — silent failure, and it
  breaks the one-definition-both-sides constraint on first contact with an enemy.
- **Move as a full skill definition.** Rejected *for now* — discrete-event pipeline
  vs continuous-state behaviour; 90% of the fields would be meaningless. The
  `BuiltInAction` sentinel in the slot keeps the door open.
- **Mouse slots with special semantics (spammable-only, category-locked).**
  Rejected — D3 shipped role-locked slots and removed them.
- **Global cooldown (GCD).** Rejected. GCDs pace MMO-style rotations and make cast
  times legible; with no cast times and an ARPG feel, it adds only latency.
- **Cast-on-press only.** Rejected — hold-to-repeat is the genre standard.
- **Homing / auto-targeting projectiles as the default.** Rejected — it removes the
  aiming expression that makes a linear projectile worth having as the first skill.
  Homing should be a *specific skill's* behaviour later, not the baseline.

## Milestone scope (the first combat slice)

**In scope:**
- Entity-agnostic stat holder (`PlayerStats` generalised so enemies share it).
- Health/damage model: current + max life, a damage receiver taking the payload
  object, death.
- **Mana pool** (current/max + flat regen placeholder) on the player.
- **Cost model**: list of cost entries, `Flat`/`PercentOfMax`/`PercentOfCurrent`,
  atomic spend, floor rule (life can't drop below 1), `IgnoresResourceCosts` actor
  flag, typed cast-failure reason. *(New — promoted from "authored but not spent.")*
- **`AttackSpeed` and `CastSpeed`** as separate modifier-carrier stats
  (`BaseValue = 1.0`), selected by a `ResolveSpeedStat(tags)` function. **No
  `SpeedStat` field.** *(Pass three — reverses pass two.)*
- **`CooldownRecovery`** as a modifier-carrier stat (`BaseValue = 1.0`), applied as a
  divisor. *(Pass three — new.)*
- **Cooldown**: `Cooldown` and `MaxCharges` (default 1) authored on definitions;
  per-instance runtime state shaped as `(chargesAvailable, rechargeTimer)`; the cast
  gate is `max(useTimeRemaining, cooldownRemaining)`; cooldown starts on cast start.
  Charge *behaviour* beyond `MaxCharges = 1` is not implemented. *(Pass three —
  promoted from deferred by owner override.)*
- Skill definition base class + **one archetype subclass** (`ProjectileSkillDefinition`),
  with tags authored as a flags enum. *(Archetype/tag split now explicit.)*
- **`SkillInstance` wrapper** in loadout slots, carrying the definition reference, an
  **empty `Attached` upgrade list**, and the charge-shaped cooldown state. *(Small,
  but blocking if skipped.)*
- Cast context with mutable per-cast parameter block **and mutable tag copy**.
- **`GetModifiers(query)`-shaped modifier lookup** (query ignores tags for now) and
  an **optional condition field on `StatModifier`** (unused). *(Small, and the
  one genuine "do it now or pay later.")*
- **`CastModifier` abstract type with `Priority` + the collect-and-apply pipeline
  stage, running over an empty list. Zero subclasses.** *(Pass three — new, ~20
  lines, seam 6.)*
- **`DamageDeliveryKind` (`Hit` | `OverTime`) on the damage payload**; only `Hit` is
  produced. *(Pass three — new, one enum.)*
- **Cast/hit events with zero listeners.**
- **Six-slot loadout**: keys 1–4 + LMB + RMB, hold-to-repeat, per-skill timers.
- **Input rebinding**: delete `move_to`; add `skill_slot_1`–`4`, `skill_slot_lmb`,
  `skill_slot_rmb`, `force_move`; LMB slot defaults to `BuiltInAction.Move`.
  *(Now **[DECIDED]** — modifies `Player.cs` and `movement.md`, both updated.)*
- `MovementDuringUse` field on definitions, authored, defaulting to `Free`; the other
  values not implemented.
- One skill: linear projectile, with its executor written as a **capability set with
  everything off** (count, spread, pierce, split, chain, homing all defaulted to
  0/false; only the trivial path implemented). *(Pass three — a shape requirement,
  not extra work.)*
- One inert **target dummy**: has life, takes damage, dies. No AI, no movement, no
  retaliation, no pathing.
- Minimal hit feedback (a flash and/or a floating number) plus a debug text readout
  of life and mana. Not cosmetic polish — it's the only instrument for judging
  whether numbers feel right.

**Explicitly deferred:** enemy AI and pursuit; enemies actually casting (the shape
supports it, don't build it); charges beyond `MaxCharges = 1`; cast time and animation
locking; `Slowed`/`Rooted` movement-during-use values; resource bar UI and any hotbar
UI beyond debug text; the "cursor over UI" input gate (no UI exists yet); life/energy
as costs on a shipped skill; channelled skills and `ChannelRate`; any modifier
actually *reading* tags or conditions; **any concrete `CastModifier` subclass**;
artifacts, sockets, gems, per-skill trees or any other upgrade shape; skill
acquisition, inventory, the passive tree, gear; crit; accuracy/evasion; damage types
and resistances; AoE and melee archetypes; auras/toggles.

**Changed vs pass two:** cooldown + `CooldownRecovery` + charge-shaped state moved
**in** (owner override); the `SpeedStat` field moved **out**, replaced by tag
derivation; `CastModifier` + its pipeline stage moved **in**; `DamageDeliveryKind`
moved **in**; `SkillInstance` grew from one field to three; the input rebinding went
from `[PROPOSED]` to `[DECIDED]`.

See [Priority order](#priority-order-decided) for the sequence *within* this slice —
it matters, particularly that the input rebinding goes first and the stats plumbing
second.

## Consequences for the stat system

Combat forces these stats online (this answers the open question in
`docs/design/stats.md`):

1. **`Life`** — current + maximum. Required by anything that can die, and now also a
   potential *cost* resource.
2. **`Mana`** — current + maximum + flat regen. Promoted into the first slice.
3. **A damage modifier carrier** — note the actor does *not* own a `Damage` stat with
   a base value; the base comes from the skill. What the actor owns is the modifier
   stack applied to a skill's base at cast time.
4. **`AttackSpeed`** and **`CastSpeed`** — two separate multiplier stats,
   `BaseValue = 1.0`, modifier carriers. Which one a skill uses is **derived from its
   `Attack`/`Spell` tags**, not stored on the definition.
5. **`CooldownRecovery`** — a fifth stat, `BaseValue = 1.0`, modifier carrier, applied
   as a **divisor** (`effectiveCooldown = Cooldown / CooldownRecovery.Value`) so it
   can never reach zero. *(Pass three.)*

Four of these five (damage, attack speed, cast speed, cooldown recovery) are
**"modifier carrier with no meaningful base of its own."** That's a different shape
from `MovementSpeed` and it is now overwhelmingly the dominant shape — which
strengthens the case for `Stat` being able to compute against an externally supplied
base.

It also forces the structural change already flagged: `PlayerStats` is
player-specific, and the reuse constraint means enemies need the same stat surface.
The stat holder has to become entity-agnostic. Exact naming and node layout is an
engineering call for the godot-expert.

**Pressure toward a keyed stat registry, revised in pass three.** Pass two named two
pressures; one is gone. `SpeedStat` no longer exists, so no stat identifier is stored
in authored data. What remains is `GetModifiers(query)`, which takes a stat identifier
*as an argument* — named C# properties can't be passed around as values without a
parallel enum, so some keying mechanism is still needed. Halved, not eliminated. Not
urgent at five stats; relevant well before fifteen.

## Fourth pass: seam 6 goes live **[DECIDED — implemented]**

The run layer (docs/design/progression.md) cashed in several seams that were built
ahead of need:

- **Seam 6 / CastModifier:** support gems (`Progression/SupportGem`) are the first
  real subclasses, attached to `SkillInstance.Attached`; keystones add caster-wide
  ones through `SkillUser.GlobalModifiers`. `CollectCastModifiers` now gathers both.
  `CastContext` gained per-cast knobs the transforms write into (damage/area/
  duration/use-time/cooldown multipliers, repeats, extra count, ailment bonuses,
  a life-cost floor).
- **Seam 1 (per-cast costs):** Blood Magic rewrites Mana cost entries into Life
  entries per cast — exactly the case the mutable cost list was for.
- **"One definition serves both sides":** the Bone Caster enemy fires a
  `ProjectileSkillDefinition` through its own `SkillUser`.
- **New archetypes:** `AreaSkillDefinition` (instant or delayed circle, on the
  caster or a target point — boss slams use the same runtime, `Combat/AreaBurst`)
  and `OrbitSkillDefinition` (`Combat/OrbitBlades`). Executors build a `HitProfile`
  once per cast (damage, type, ailment chances, knockback) and every hit-dealer
  builds payloads from it.
- **Chains** are implemented (PoE order: pierce, then fork, then chain).
- **Skill levels:** `SkillInstance.Level`, +20 % more damage per level.
- `SkillDefinition` gained presentation fields: `Description`, `Icon`, `Frames`.

## Open questions

**Closed in pass three:** the RMB binding (decided) · when cooldowns land (now, in the
first slice) · whether the speed stat is tag-derived (yes) · whether behaviour
upgrades need their own type (yes — seam 6).

**Closed during M1 implementation:** **What capability list does the projectile
archetype claim?** The owner confirmed the doc's suggested list, then added one:
count, spread angle, pierce, splits, chains, **bounces**, homing strength, size,
speed, range, on-hit-explode. All eleven are authored as `[Export]` fields on
`ProjectileSkillDefinition` (mirrored on the per-cast `ProjectileParams` block),
all defaulted off (`0`/`false`, except `Size`/`Count` which default to `1` since
those are multiplier/count capabilities whose "off" value isn't zero). Only
`Count = 1, Pierce = 0` — i.e. the trivial single-projectile, no-pierce path — is
actually read by the executor (`Skills.SkillUser` / `Combat.Projectile`) in this
slice; the rest are inert authored data until a later milestone wires them in.

**Renamed: `Splits` -> `Forks`, and `Count` + `SpreadDegrees` + `Forks` are now
implemented.** Three notes from wiring them up:

1. **"Fork", not "split".** In genre vocabulary a *fork* happens **on hit** (the
   projectile becomes two that continue past the target) and a *split* happens **at
   cast** (one press, N projectiles). This doc's original phrasing — "projectiles
   split in two on hit" — describes a fork. Renaming frees "split" for the at-cast
   case, which is what `Count` already does, so the two capabilities stop competing
   for one word.
2. **`OnHitOutcomeKind` gained a fourth member, `Fork`** — see the correction under
   [Seam 6](#seam-6-castmodifier--behaviour-transforms-decided) step 7 below. Fork is
   not a `Redirect`.
3. **Pierce outranks fork** when a skill has both: the projectile passes through and
   keeps its fork budget for a later hit, rather than forking on the first hit while
   pierce is unspent. Arbitrary but deterministic, and it matches PoE (a piercing
   projectile doesn't fork on the targets it pierces).

Still inert after this pass: `Chains`, `Bounces`, `HomingStrength`, `Size`,
`OnHitExplode`, plus `MaxCharges` and the non-`Free` values of `MovementDuringUse`.

**A note on how the fork test was validated, because the first version of it was
worthless.** The original "fork must not cascade" check counted live projectiles in
empty space, and it **passed with the bug deliberately reintroduced** (children
inheriting an undecremented counter) — because the two fork children fly apart and
never hit anything again, so nothing could ever fork twice regardless of the counter.
The fixed test places a second dummy on a child's flight path: correct behaviour
leaves 1 projectile, the bug leaves 3, and the bug was re-introduced to confirm the
test now reports FAIL. This is the journal's standing lesson — a test that has never
been seen to fail isn't evidence — recurring in a form that looked convincing.

**`Bounces` — added by the owner after seeing the list, and it exposes an open
design question.** The stated intent was "projectiles bounce after reaching screen
size." The field is authored as `int Bounces` (0 = off, despawn on first terrain
impact) and is inert like the rest, but *what* it bounces off is genuinely
undecided and should be settled before it is implemented:

- **Off world geometry / arena walls** — the conventional answer, and the one the
  field is documented as. It composes with everything else: bounce is a
  `Redirect` outcome against terrain, `Chains` is a `Redirect` outcome toward an
  enemy, so both ride [seam 6](#seam-6-castmodifier--behaviour-transforms-decided)
  and `OnHitOutcome` with no new machinery. The catch: the arena currently has no
  walls, only a floor polygon and a navmesh, so there is nothing to bounce off
  until collidable geometry exists.
- **Off the screen edge** — what the phrasing literally asks for, and it is a
  different thing. The camera follows the player, so the screen edge is not a game
  object: two players standing in different places would see the same projectile
  bounce at different world positions, and an ultrawide monitor would get a
  materially different skill. Genre precedent is essentially absent for this in
  ARPGs (it belongs to fixed-screen arena shooters). **Recommendation: bounce off
  geometry, not the viewport.**
- **Interaction with `Range`:** today `Range` despawns a projectile after N px of
  travel. Does a bounce reset the travelled distance, or keep accumulating? Keeping
  it accumulating is the safer default (a bouncing projectile has the same total
  lifetime budget, so bounce trades reach for coverage rather than being strictly
  better). Not decided.

Remaining, in rough order of when an answer is needed:

1. **What shape do skill upgrades take?** The pass-three recommendation is
   [Axis B: tag-gated artifacts socketed into skill instances](#axis-b--artifact-sockets-on-skills-combinatorial--this-is-where-the-leverage-is),
   marked **[PROPOSED]**. It reverses pass two's "recommend against sockets." Nothing
   is blocked until priority item 18.
2. **Does gear carry behaviour, or only numbers?** *Recommendation: numbers only;
   behaviour belongs to artifacts.* Needed before priority item 17.
3. **One shared passive web, or separate per-class trees?** Determines whether
   [dual class (axis D)](#axis-d--dual-class-optional-and-the-highest-leverage-item-in-this-document)
   is ever available — the highest build-feel-per-authoring-hour option in this
   document. No cheap undo. Needed before priority item 19, and it should not be
   drifted into.
4. **How are skills acquired?** Owner still deferring, and pass three agrees it should
   be *last* of the design questions: the right answer depends on how scarce skills
   should be, which depends on how much artifacts amplify each one.
5. **Taxonomic vs game-state conditions on modifiers.** Both ride seam 3. The
   recommendation is a small tag set doing coarse work plus state conditions carrying
   the identity. Decide after the game has been played.
6. **Do auras/toggles occupy the six slots, or a separate bar?** Answer when an aura
   exists. Note a pure toggle is the only legitimate exception to the
   `UseTime > 0 || Cooldown > 0` invariant.
7. **Does Move eventually become a real skill definition?** Only once channelled /
   continuous skills make the pipeline suitable. The `BuiltInAction` sentinel keeps
   the door open; no reason to walk through it early.
8. **Global vs per-skill use timers once cast times exist.** Per-skill is fine while
   casts are instant; reopens the moment a skill has a windup.
9. **Should the `Priority` bands for `CastModifier` be an enum rather than a raw
   int?** Trivial now, and the answer will be obvious once there are five of them.
   Flagged only so it isn't discovered as a surprise.

## Rework of 2026-10-30 **[IMPLEMENTED]**

Owner's brief (a written list, `docs/design/classes.md` -> "The six evolutions" for
the class half): Fireball slower and bursting on impact; Spark lightning-like, much
slower, bouncing off walls and passing through one enemy; skills upgraded in a new
way — an NPC, **Escaldón**, a tree per skill, levels bought with gold up to 20; and
*"review the skills' cooldowns and mana costs: Fireball at the start of the game with
the Arcanist is almost free to use, when a level 1 should have to manage mana"*.

### Mana and cooldowns

Before: a level-1 Arcanist had 119 mana and 7.2 mana/s of regeneration; Fireball cost
3 mana per 0.45 s cast (6.7/s), so it could be cast forever.

- **Regeneration:** base 1.5/s (was 5), +0.05 per Intelligence (was 0.1), **+0.15 per
  character level** past 1. A level-1 Thaumaturge regenerates 2.6/s against Fireball's
  10/s nonstop: a full bar lasts about 16 s of nonstop casting, then about one cast
  every 2.3 s (`MANA-FIREBALL-NOT-FREE`). At level 21 regeneration is about 5.6/s and
  the pool larger: roughly half of nonstop casting is sustained. The mana flask (2),
  passives, gear and the Elementalist close the gap.
- **Costs grow with the gem:** +2% per level past 1 (`SkillInstance.CostPerLevel`; a
  level-20 gem costs about 40% more), against +12% damage per level.
- **New costs and cooldowns** (level 1; mana / cooldown):

| Skill | Before | Now |
|---|---|---|
| Fireball | 3, 22 damage, cast 0.45 s | 6, 30 damage (+ its burst), cast 0.6 s |
| Spark | 3 | 5, cast 0.45 s |
| Frost Shards | 4 | 6 |
| Frost Nova | 6 / 1.6 s | 10 / 3 s |
| Meteor | 10 / 2.8 s | 18 / 5 s |
| Blades | 8 / 3.6 s | 12 / 6 s |
| Cleave · Double Strike · Storm Strike | 2 · 2 · 2 | 3 · 2 · 4 |
| Glacial Hammer · Throwing Knives | 3 · 3 | 5 · 4 |
| Ground Slam | 4 / — | 7 / 1 s |
| Sunder | 4 / — | 8 / 1.5 s |

Attacks stay cheaper than spells: they pay with the risk of being in melee. The
Survival Rift ignores mana, but its auto-cast interval includes the cooldowns, so
Frost Nova, Meteor and Blades fire less often there now.

**Measured with the bot** (`--mode=reality --class=arcanist --tier=5 --level=auto
--runs=2`, same machine): before the rework, normal monsters died in 0.15–0.17 s
(median) and 1 of 2 realities was cleared. A first pass (regeneration without the
per-level growth, +5% cost per gem level, Fireball 8 mana) made it 1.8–2.6 s and 0 of
2 — mana-starved, too harsh past the first levels. The final numbers: 0.5–0.65 s and
**2 of 2 cleared** (tier 5, then tier 6). Killing is slower than when mana was free,
on purpose; the monster life table (`MonsterScaling`) was calibrated with free
casting and gem levels from kills, so recalibrate it with the bot across tiers before
fine-tuning.

### Fireball and Spark

- **Fireball:** 340 px/s (was 520), 26 damage. Where it lands, on an enemy or a wall,
  it **bursts** in a 55 px circle for half of its hit (`ExplodeRadius`,
  `ExplodeDamageFraction` on `ProjectileSkillDefinition`; the blast is an `AreaBurst`
  added after the overlap callback). The target takes the hit and the blast.
- **Spark:** 260 px/s (was 640), range 720, two sparks. Its flight **zigzags** through
  random knots up to 12 px either side (`Zigzag`), faces where it actually goes, and
  leaves a short fading trail, so it reads as lightning without changing the sprite.
  It **passes through one enemy** (pierce 1, chains moved to its tree) and **bounces
  off walls** three times (`Bounces`, player projectiles only).
  `FIREBALL-BURSTS`, `SPARK-PIERCES-ONE`, `SPARK-BOUNCES-AND-ZIGZAGS` in the new
  `SkillReworkTest`. A co-op client draws a guest's spark flying straight (only the
  host zigzags it); positions resync on each bounce.

### Skill upgrade trees

**Escaldón** (a new hideout station on the east side of the plaza; placeholder model:
a weapon rack and the old Assassin sprite) opens `SkillTreePanel`: every skill gem
the character owns on the left, the chosen one on the right with its level, the gold
to raise it and its tree.

- **Levels for gold, up to 20.** Level L → L+1 costs `30 · 1.45^(L−1)` gold (rounded
  to 5): 30 for level 2, about 1,600 for level 11, 24,000 for level 20; about 77,000
  for a gem from 1 to 20 — reachable, not given. Gems **no longer learn from kills**,
  and a dropped copy of a skill you own turns into gold (a third of its next level).
- **Points:** every level past 1 is a point for that gem's tree (`GemData.Tree`, saved
  with the gem). Left click spends one; right click takes one back for 10 gold × the
  gem's level.
- **Shape:** three named branches per skill. A branch is a lesser node (usually three
  ranks: 8% / 16% / 25%), a second lesser node that needs **2 points in the first**,
  and at the end a notable that needs 2 points in the second — sometimes **two
  notables of which you take one** (Volley or Frostfire, Ricochet or Arc Blast...).
  Every tree has at least 19 ranks for the 19 points a level-20 gem has, most have
  more, so two players' Fireballs can differ.
- **What nodes do** (cast transforms, `SkillTreeModifier` over `CastContext`, applied
  with the supports): more damage, area, speed, cost, cooldown, duration; ignite,
  shock, freeze and **bleed** chances (bleeding is physical damage over time,
  `Ailments`); pierce, chains, wall bounces, more projectiles, bursts on hit;
  **Detonation** (kills burst for 30% of the hit); conversions to fire, cold,
  lightning or **physical** (more damage and bleeding); echoes and extra strikes; for
  curses: spread on death, more targets, stronger effect, vulnerability, bursting
  bearers.
- **Data:** `scripts/Skills/SkillTrees.cs` (the draft for all sixteen skills, with
  shared node helpers); rules in `SkillTreeRules` (raise, lower, exclusivity,
  sanitising a loaded gem). A first draft, to tune once played.
  Checks: `SKILLTREE-EVERY-SKILL`, `-GOLD-CURVE`, `-RULES`, `-APPLIES`, `-SANITIZE`,
  `ESCALDON` (EndgameSmokeTest).

Rejected: points from character level (would tie skill power to the character
again), a single shared skill tree (no decisions per skill), and free respecs (the
cost of a point is what makes the choice mean something; it is kept small).

Open questions: a
visual pass on the tree (icons per node); whether Escaldón should also sell respecs
of a whole tree at once.

### Support gems parked (2026-10-31) **[IMPLEMENTED]**

The open question above, answered for now: the upgrade trees convert, multiply and
add projectiles, so supports doubled up on the same job. They are **switched off, not
deleted** — one flag, `Features.SupportGems` (`scripts/Core/Features.cs`), false:

- No support drops in realities, none is on sale at Odran's (the Skills / Supports
  toggle hides; `GemShop.Catalog` still lists them, `GemShop.Offers` is what is on
  sale), no support card in the Survival Rift (and a `g:` card key is refused from
  co-op peers), and a new character starts with its skill only.
- The skills panel (K) shows no sockets and is titled "Skills".
- Old saves keep their support gems and sockets untouched, but `CharacterBuild` does
  not attach them, so they do nothing. The autoplay bot's reference character gets
  none either (the monster life table should be recalibrated without them).
- Everything else stays: `UpgradeCatalog.Supports`, `SupportGem`, `RunBuild.AddSupport`,
  sockets in `CharacterData`, the network fields. The tests that cover the support
  rules (GEMSHOP-*) switch the flag on around themselves; `SUPPORTS-PARKED`,
  `NO-SUPPORT-CARDS-WHILE-PARKED` and `CLASS-BUILD` check the parked state.

Bringing them back: set the flag to true and revisit the copy that was made
conditional on it. A future workaround (e.g. supports as tree nodes, or as a rarer
drop that adds a branch) is still open.
