# Endgame: Mirror Shards and Altered Realities

Status: **[DECIDED]** direction from the owner (2026-10-11), details **[PROPOSED]**
where marked. Supersedes the survivor run (docs/design/run.md) as the core loop;
see "What happens to the survivor run" below.

Owner brief: *"More like Path of Exile's endgame, without the campaign. Keys —
mirror shards — take you to an altered reality (a dungeon) with a specific
layout. At the end there is always a boss that drops something. On the way,
normal, magic and rare enemies, sometimes very rare ones (another colour) that are
stronger, like mini-bosses. The final boss can drop more shards of the same tier
or one/two tiers higher. Dungeons go up to difficulty 20."*

## The loop

```
Hideout ──(insert a Mirror Shard into the Mirror)──► Altered Reality (tier N)
   ▲                                                      │  layout: rooms + corridors
   │                                                      │  packs: normal / magic / rare / unique-rare
   │                                                      ▼
   └──── loot, shards (tier N, N+1, N+2) ◄──── Reality Boss (always at the end)
```

It is PoE's map loop: the key is consumed, the dungeon is the reward-generating
activity, the boss is the guaranteed payout, and the keys it drops are what keep
the loop going and push it upward. There is no campaign: a new character starts in
the hideout with a tier-1 shard.

## Mirror Shards (the keys) **[DECIDED]**

- An item that lives in the inventory (stackable by tier, 1×1). Tier **1–20**.
- Used at **the Mirror** in the hideout (replaces the portal): opens an Altered
  Reality of that tier. One shard = one attempt; dying ends the attempt and the
  shard is spent **[PROPOSED]** (PoE-style stakes; alternative: N portals per
  shard, see open questions).
- **Shard drops from the reality boss** **[PROPOSED numbers]**:

  | Outcome | Chance |
  |---|---|
  | No shard | 25 % |
  | Same tier | 45 % |
  | Tier +1 | 25 % |
  | Tier +2 | 5 % |

  Expected value ≈ 1.1 shards per clear, so a player who keeps clearing keeps
  playing, and progress upward is steady but not guaranteed. Rare and unique-rare
  monsters also have a small chance to drop a shard of the current tier (~3 % /
  ~15 %) as a safety net against dry streaks.
- Tier 20 is the cap: tier-20 clears only drop tier-20 shards (and better loot).
- **Shard modifiers** **[PROPOSED, later milestone]**: like PoE map mods —
  higher-rarity shards carry modifiers (monsters deal extra fire, players have less
  regeneration, extra rare packs…) that raise difficulty and item quantity/rarity.
  This is where the existing affix system gets reused for keys.

## Tiers 1–20 **[PROPOSED numbers]**

One number drives everything:

| | Tier 1 | Tier 10 | Tier 20 |
|---|---|---|---|
| Monster level / item level | 1 | 45 | 85 |
| Monster life multiplier | ×1 | ×6 | ×25 |
| Monster damage multiplier | ×1 | ×2.5 | ×5 |
| Pack count | 1× | 1.3× | 1.6× |
| Magic / rare pack chance | low | medium | high |
| Unique-rare chance per reality | 5 % | 15 % | 30 % |
| Boss mechanics | phase 1–2 | full | full + enraged extras |

Monster life outpaces damage (same reasoning as the survivor run: player damage
scales with gear, player life much less). Item level follows monster level, so the
affix tiers already authored (up to ilvl 120) map naturally onto tiers 1–20.

## Altered Realities (the dungeons)

### Layout **[DECIDED: a specific layout per reality; PROPOSED: how]**

A reality is a **hand-designed layout template** — a graph of rooms and
corridors — with randomised details (which room holds which pack, side rooms, a
few alternate connections). Same template = recognisably the same place, so
players learn a reality the way PoE players learn a map, while runs still differ.

- Built from a tile grid (TileMap) with walls, so it needs **navigation**: enemies
  in corridors must path around corners. This is the first real use of the
  NavigationServer since the movement milestone (docs/design/movement.md).
- A layout = entrance room → 4–8 combat rooms/corridors (some optional branches)
  → boss arena behind a gate. Target length 3–5 minutes at the right tier.
- Each layout has a **theme** (tileset + monster roster + boss). First set
  **[PROPOSED]**:

  | Reality | Theme | Roster | Boss |
  |---|---|---|---|
  | Ashen Halls | burning ruins (existing tileset) | slime, bat, imp, brute | The Ember Colossus |
  | Bone Crypt | catacombs | casters, skeleton melee, bats | The Bone Herald |
  | Drowned Mirror | flooded temple | new | new |

  The two bosses we already have become reality bosses with no rework beyond
  scaling by tier.
- **Authoring** **[PROPOSED]**: layouts as text grids (one char per tile/marker:
  `#` wall, `.` floor, `E` entrance, `B` boss arena, `P` pack spawn, `R` rare
  spawn, `D` door), the same "draw it as text" approach the EasyPixel pipeline
  uses. Easy to diff, easy for an agent to author, and a later in-editor tool can
  write the same format.

### Monster rarity **[DECIDED tiers, PROPOSED numbers]**

Exactly PoE's vocabulary, colour-coded on the monster and its name plate:

| Rarity | Colour | Life | Mods | XP / loot | Notes |
|---|---|---|---|---|---|
| Normal | white | ×1 | 0 | ×1 | the bulk of every pack |
| Magic | blue | ×2 | 1–2 | ×3 | packs of 3–5 |
| Rare | yellow | ×5 | 3–4 | ×10 | leads a pack of normals; name plate and life bar |
| **Unique-rare** ("very rare") | **purple/violet** **[PROPOSED colour]** | ×12 | 4–5 + one signature ability | ×30 | a mini-boss: always alone with a small escort, telegraphed abilities like a boss, guaranteed good drop |

**Monster mods** reuse the stat/modifier system: *Hasted* (+movement/attack speed),
*Armoured*, *Fire-touched* (adds fire damage, fire res), *Extra projectiles*,
*Vampiric* (life on hit), *Volatile* (explodes on death — telegraphed), *Frost
aura* (chills nearby), *Splitter* (spawns two smaller copies). Each mod is a small
data entry (StatModifiers + optional behaviour flag), shown under the name.
Unique-rares get one signature ability drawn from the boss toolkit (a slam, a lane,
a nova) — the telegraph rules of docs/design/bosses.md apply to them too.

The current "elite" becomes **Rare**; the survivor director's escalating timeline
is replaced by placed packs in the layout.

### The boss **[DECIDED]**

Always at the end, behind a gate in its own arena (so the fight is readable and
packs don't wander in). On death it drops: shards (table above), 2–4 items at the
reality's item level with a rarity bias, currency, and — later — a chance at a
boss-specific unique item.

## What happens to the survivor run **[PROPOSED — needs owner confirmation]**

The survivor run was a structure, not just content. Recommendation:

- **Keep**: enemies, bosses, telegraph system, dodge, skills, supports, ailments,
  items/affixes, hideout, profile, menus, art pipeline — everything carries over.
- **Change**: the 7-minute timeline and wave director → placed packs in a layout.
- **Change**: the per-run level-up cards. In PoE's endgame the character's level
  and build are *persistent*. Recommendation: character level becomes persistent
  (levels 1–~60), each level grants a passive point spent in a compact class tree
  (docs/design/classes.md), and **skills and supports become items** (gems socketed
  in gear, or a simpler "skill slots + support slots" panel), so builds are made
  between realities, not during them. The card code (offers, rarities, keystones)
  becomes the content of the passive tree.
- **Optional keep**: a "Survival Rift" special shard that launches the existing
  survivor mode as an alternative activity (cheap, since it already works).

## Milestones **[PROPOSED]**

1. **Shards + Mirror + tier scaling** — shard item, the Mirror in the hideout,
   tier → monster level/scaling/item level, boss drop table. Reality = the current
   arena with placed packs (no layout yet). *Playable loop end-to-end.*
2. **Monster rarity** — magic/rare/unique-rare, mods, name plates, colours, loot
   and XP multipliers.
3. **Layouts** — text-grid layout format, tile-built realities, navigation,
   doors/boss gate, first layout (Ashen Halls), minimap-lite.
4. **Persistent character + classes** — character level, the three base classes,
   skill/support items, passive points (docs/design/classes.md).
5. **Ascendancies** — unlocked by an endgame trial (e.g. first tier-5 boss);
   eight ascendancies.
6. **Content** — two more realities and bosses, shard modifiers, unique items.

Each milestone stays shippable and gets its design doc update and smoke test, as
before.

## Alternatives considered

- **Procedural dungeons (Diablo-style random generation).** Rejected per the brief
  ("a specific layout"): handcrafted templates with randomised details give
  learnable places and are far easier to balance and telegraph around.
- **Infinite tiers (Diablo greater rifts).** Rejected: 20 tiers is a goal you can
  finish; scaling past it can come later as shard modifiers.
- **Shards as currency (no inventory item).** Rejected: as items they can carry
  modifiers, be traded/stashed later, and use the existing item/filter UI.

## Open questions for the owner

1. **Death penalty**: lose the shard on death (PoE, high stakes) or a number of
   attempts per shard (e.g. 3 portals)? Recommendation: 3 portals at low tiers,
   1 portal from tier 15 (stakes rise with tier).
2. **Survivor run**: drop it, or keep it as a special "Survival Rift" shard?
3. **Persistent levelling vs per-run cards** (see above) — this decides how
   classes and ascendancies plug in.
4. Unique-rare colour: purple/violet proposed (PoE's uniques are orange — keeping
   orange free for unique *items* avoids confusion).
5. How many layouts per release target? Recommendation: 3 at first.
