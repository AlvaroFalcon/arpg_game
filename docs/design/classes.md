# Classes and ascendancies

Status: **[DECIDED]** class structure from the owner (2026-10-11); names and
mechanics **[PROPOSED]**. Depends on the persistent-character decision in
docs/design/endgame.md → "What happens to the survivor run".

Owner brief: *"A starting class that later evolves into Assassin or Ninja; a
warrior starting class that evolves into Barbarian, Paladin or Samurai; a starting
mage — what would you call it? — that evolves into Ascendant, Warlock or Sage."*

## Structure

Three base classes, each with a home attribute (the attributes already exist:
Strength, Dexterity, Intelligence — docs/design/stats.md), a starting skill, a
starting weapon type, and a compact passive tree. At an endgame milestone the
character picks **one ascendancy** of its class, which adds a small, powerful set
of nodes on top — PoE's ascendancy model.

| Base class | Attribute | Ascendancies |
|---|---|---|
| **Rogue** *(name proposed)* | Dexterity | Assassin · Ninja |
| **Warrior** | Strength | Barbarian · Paladin · Samurai |
| **Arcanist** *(name proposed)* | Intelligence | Ascendant · Warlock · Sage |

## Naming the base classes

**Mage → recommended: "Arcanist"** (*Arcanista*). It is a student of raw arcane
power, which sits well under all three paths: the Ascendant transcends it, the
Warlock bargains for darker power, the Sage masters it through knowledge.
Alternatives considered: *Initiate* (good "beginner" feel but weak as a class name
you play for 100 hours), *Adept*, *Occultist* (pre-empts the Warlock),
*Sorcerer* (generic), *Erudite* (too close to Sage).

**Dex starter → recommended: "Rogue"** (*Pícaro*), the natural root of both a
poison-and-crits Assassin and a mobility-and-shadow Ninja. Alternative: *Shade*
(more flavour, less instantly readable).

**Strength starter: "Warrior"** (*Guerrero*), as given.

## The base classes **[PROPOSED]**

| | Rogue | Warrior | Arcanist |
|---|---|---|---|
| Fantasy | quick, precise, evasive | tough, close-range, heavy hits | ranged spells, elements |
| Base life / mana | medium / medium | high / low | low / high |
| Starting skill | Twin Daggers (fast melee) or Throwing Knives (projectile) | Cleave (melee arc) | Fireball (existing) |
| Weapon types | daggers, claws, bows | axes, maces, swords, shields | wands, staves, offhand foci |
| Class mechanic | **Combo**: consecutive hits build stacks that empower the next skill | **Rage**: taking and dealing damage fills Rage, which grants damage and reduces damage taken | **Charges**: spells grant arcane charges that boost cast speed/damage; spent by finishers |
| Dodge | 3 charges (best mobility) | 2 charges, longer i-frames | 2 charges |

Melee is new: the skill system has a `Melee` tag and `DerivesBaseFromWeapon` flag
reserved since the first combat slice (docs/design/skills.md) — Warrior and Rogue
are where they get implemented, as a new `MeleeSkillDefinition` archetype (an arc
or line hitbox in front of the caster) reusing the hit-profile pipeline.

## Ascendancies **[PROPOSED identities]**

Each ascendancy: ~6 nodes, 2 of them notable keystone-like effects, and one
signature skill or mechanic. Proposed identities, chosen so the three paths of a
class feel different rather than three flavours of "more damage":

**Rogue**
- **Assassin** — crits and poison. Executes low-life enemies, critical strikes
  apply stacking poison, bonus damage against rares and bosses. *Single-target
  specialist.*
- **Ninja** — mobility and shadow clones. Dodging leaves a clone that taunts and
  explodes, extra dodge charges, smoke bomb skill. *Evasion and area through movement.*

**Warrior**
- **Barbarian** — two-handed fury. More Rage, life leech at high Rage, slams leave
  fissures. *Glass-ish berserker.*
- **Paladin** — holy defence. Auras that buff and heal, block chance, holy damage
  conversion, a consecration area. *The tank / sustain path.*
- **Samurai** — precision and timing. Parry window on the dodge (dodging an attack
  just in time empowers the next strike), iaido-style charged strikes. *Skill-
  expressive melee, built on the dodge system.*

**Arcanist**
- **Ascendant** — elemental mastery. Elemental conversion freedom, element
  overload (alternating elements stack bonuses), higher resistance caps. *Pure
  elemental caster.*
- **Warlock** — curses and dark pacts. Curses on enemies, life-for-power trades
  (Blood Magic grows from here), minion-like summoned familiars. *Damage over time
  and debuffs.*
- **Sage** — knowledge and control. Extra support slots per skill, spell echo and
  cooldown mastery, time-slow fields. *The build-enabler: more supports, more tricks.*

## How it plugs into what exists

- **Passive tree**: the card system's passives and keystones
  (`Progression/UpgradeCatalog.cs`) are the content library for the trees. Keystones
  like Glass Cannon, Blood Magic, Phase Dancer move into the relevant class tree
  or ascendancy (Blood Magic → Warlock, Phase Dancer → Ninja).
- **Stats**: every node is a `StatModifier` with the character as source (same
  mechanism as gear), so respecs are "remove by source, re-apply".
- **Skills**: available from class + gear (skill gems or skill slots) instead of
  level-up cards; supports attach exactly as now (`SkillInstance.Attached`).
- **Ascendancy unlock**: **[PROPOSED]** defeat a tier-5 reality boss to unlock the
  choice; a second trial at tier 12 grants the remaining ascendancy points.

## Art (EasyPixel)

Three base-class sprites (idle/walk/attack) plus a palette/accessory variant per
ascendancy (cape, mask, halo, horns) rather than eight full sprites — cheap with
the `recolor`/`patch` helpers in `tools/art`.

## Open questions for the owner

1. Names: Arcanist and Rogue OK? (Spanish UI later: Arcanista / Pícaro / Guerrero.)
2. Should a class be **locked** to its attribute/weapons, or only *start* there
   (PoE: any class can go anywhere on the tree)? Recommendation: one shared tree
   with three starting points — maximum build freedom, less content to author.
3. Respec cost: free, gold, or a currency item? Recommendation: gold (gives gold its
   first sink).
4. Unlock moment for ascendancies (tier 5 proposed).
