# Classes and ascendancies

Status: **[IMPLEMENTED]** (2026-10-11). Structure from the owner; names and
mechanics were proposed and accepted ("go with your recommendations").

Owner brief: *"A starting class that later evolves into Assassin or Ninja; a
warrior starting class that evolves into Barbarian, Paladin or Samurai; a starting
mage — what would you call it? — that evolves into Ascendant, Warlock or Sage."*

## Structure

Three base classes with a home attribute, a starting skill and support, a class
resource, and a start on **one shared passive tree**. After the tier-5 reality
boss the character picks **one ascendancy** of its class — PoE's model.

| Base class | Attribute | Ascendancies |
|---|---|---|
| **Rogue** | Dexterity | Assassin · Ninja |
| **Warrior** | Strength | Barbarian · Paladin · Samurai |
| **Arcanist** | Intelligence | Ascendant · Warlock · Sage |

Code: `ClassCatalog` (definitions), `CharacterData` (the persistent character),
`CharacterBuild` (applies everything to the player), `PassiveTree`,
`AscendancyCatalog` + `AscendancyEffects`, `ClassResource` — all in
scripts/Characters/.

## Naming the base classes

**Mage → "Arcanist"** (*Arcanista*): a student of raw arcane power, which sits
under all three paths — the Ascendant transcends it, the Warlock bargains for
darker power, the Sage masters it through knowledge. Rejected: *Initiate* (weak as
a name you play for 100 hours), *Adept*, *Occultist* (pre-empts the Warlock),
*Sorcerer* (generic), *Erudite* (too close to Sage).

**Dex starter → "Rogue"** (*Pícaro*), the natural root of both a poison-and-crits
Assassin and a mobility-and-shadow Ninja. Rejected: *Shade*.

## The base classes

| | Rogue | Warrior | Arcanist |
|---|---|---|---|
| Fantasy | quick, precise, evasive | tough, close-range, heavy hits | ranged spells, elements |
| STR / DEX / INT | 12 / 22 / 10 | 22 / 10 / 8 | 8 / 10 / 22 |
| Bonus | +10 life, **+1 dodge charge** | +35 life | +25 mana |
| Starting gems | Double Strike + Faster Casting | Cleave + Increased Area | Fireball + Spell Echo |
| Class resource | **Combo** (max 10): consecutive hits build stacks of attack speed and crit; they fall away when you stop hitting | **Rage** (max 30): dealing and taking hits fills it; grants damage and damage reduction; fades out of combat | **Arcane Charges** (max 5): each spell cast grants one (cast speed, spell damage); drain slowly |

Per level: +4 life, +1.5 mana on top of the attributes.

**Melee** was new: `MeleeSkillDefinition` (an arc or a delayed line in front of
the caster, optional multiple strikes and knockback) reusing the hit-profile
pipeline. Five melee/dex skills were added — Cleave, Double Strike, Sunder (line),
Ground Slam (big arc, knockback), Throwing Knives (projectile) — plus three
supports (Multistrike, Melee Splash, Brutality) and three weapon bases (sword,
axe, dagger). Melee hits harder than ranged per cast to pay for the risk (see the
tuning table in docs/design/endgame.md).

## Persistent character

- Level 1–70, experience 40·L^1.6 per level, from kills in realities and the
  Survival Rift. Each level: one passive point.
- Up to 6 characters per profile (`Profile`, user://profile.json, version 2;
  version-1 saves migrate into one Arcanist).
- **Gems**: skills and supports are owned items (`GemData`). Skill gems level
  (to 20, 60·L^1.8 experience) from half the experience of the kills made while
  equipped; supports never level. Duplicate skill drops melt into gem experience.
- **Loadout**: 4 slots, each one skill gem plus support sockets — 2, +1 at level
  12, +1 at level 28 (+1 more for the Sage). The skills panel (**K**) sockets gems;
  supports only list when they fit the targeted skill.

## Passive tree (P)

One tree, three starts (PoE: any class can go anywhere; less content to author):

- **92 nodes** generated from data: the three class starts on an inner circle, an
  18-node attribute ring, **nine themed branches** (Elemental, Spellcraft, Mana,
  Precision, Agility, Projectiles, Brawn, Melee, Fortitude) and **three hybrid
  branches** (Ailments, Duelist, Battlemage), each a run of small nodes, a notable
  and (for the nine) a **keystone**, plus cross-links so the outer tree is a web.
- Keystones are the old level-up keystones: Avatar of Fire, Echoing Mind, Blood
  Magic, Phase Dancer, Chain Reaction, Vampiric Pact, Glass Cannon, and the new
  Iron Will.
- Allocation needs adjacency; **refunds cost gold** (5 + 3·level) and are refused
  if they would orphan other nodes. The panel pans (drag) and zooms (wheel).

## Ascendancies

Eight nodes each, **4 points from the tier-5 trial, +2 from tier 12** — every
character skips two nodes, so two Assassins can still differ. Chosen at the
**Altar of Ascension** in the hideout (permanent choice); nodes refund for gold.
Each ascendancy has its own sprite variant.

**Rogue**
- **Assassin** — Executioner (more damage to low-life enemies), Venom (crits
  poison, stacking), Mark the Prey, Deadly, Toxic Blades, Ruthless, Cold Blood,
  Predator. *Single-target specialist.*
- **Ninja** — Shadow Clone (dodging leaves a decoy monsters attack), Shadowstep,
  Evasive, Endless Roll (+dodge charge), Light Feet, Swift, Shuriken Rain, Fleeting
  Kill. *Evasion and area through movement.*

**Warrior**
- **Barbarian** — Unending Rage, Bloodthirst (leech), Fissures (slams leave
  fissures), Bloodlust, Thick Skin, Frenzy, Wrath (Rage grants attack speed), War
  Cry. *Berserker.*
- **Paladin** — Bulwark of Faith (block), Consecration (healing ground), Radiance,
  Fortress, Purity, Holy Aura, Renewal, Martyr. *The tank / sustain path.*
- **Samurai** — Perfect Parry (dodging a hit early in the roll refunds the charge
  and empowers the next hit), Iaido (first strike after a pause), Bushido,
  Focus, Discipline, Wide Arc, Folded Steel, Flowing Water. *Skill-expressive
  melee built on the dodge.*

**Arcanist**
- **Ascendant** — Elemental Overload (alternating elements stack), Prismatic,
  Piercing Elements, Mastery, Attunement, Conflux, Battery, Surge. *Pure
  elemental caster.*
- **Warlock** — Hex (curses), Familiar (a summoned caster), Hellfire, Dark Pact,
  Siphon, Decay, Wither, Soul Hunger. *Damage over time and debuffs.*
- **Sage** — Insight (+1 support socket), Resonance (echoes), Time Field, Patience,
  Wisdom, Focus, Clarity, Ward. *The build-enabler.*

Behaviour nodes plug into the combat code through `DamageHooks` (hit
multipliers, 0 = avoided), the global hit/kill events, and per-frame effects;
stat nodes are plain `StatModifier`s with the build as source, so a respec is
"remove by source, re-apply".

## Art (EasyPixel)

Sprites for the three classes and the eight ascendancies (tools/art/sprites/
classes.ts) — palette and accessory variants (hood, horns, halo, mask) built with
the `recolor`/`patch` helpers rather than eight unrelated sprites.

## Decisions

- Names: Arcanist and Rogue.
- One shared tree with three starts (not class-locked trees).
- Respecs cost gold — gold's first sink.
- Ascendancy unlock at the tier-5 boss, second trial at tier 12.

## Open questions

1. Spanish UI (Arcanista / Pícaro / Guerrero) — the UI is English for now.
2. Weapon-type restrictions per skill (melee skills currently work with any
   weapon; `DerivesBaseFromWeapon` is still reserved).
3. Class balance beyond the bot's floor: Rogue is the weakest gearless starter,
   Arcanist the strongest; revisit with real play and gear.
