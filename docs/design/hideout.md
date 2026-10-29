# The hideout town

> **Keys changed 2026-10-29** (docs/design/menus.md -> "Controls"): interact is **F** (was E), pick-up is **G** (was F), flasks are **1/2** (were Q/R), skills are LMB/RMB/Q/E. Older text below may still name the old keys.


**Status: [IMPLEMENTED 2026-10-13] first pass.** The owner asked for the hideout to
become a city: a central plaza with the Mirror and the merchant, the forge inside a
smithy, an edit mode to rearrange the props (the Mirror, the forge...), and a shop
selling floor tiles and decorations so players can build their own atmosphere.

Code: `scripts/Hideout.cs` (props, stations, edit hook), `scripts/World/HideoutTown.cs`
(map, tiles, collision, navigation), `scripts/World/HideoutCatalog.cs` (floors,
props, the default layout), `scripts/World/HideoutData.cs` (the saved arrangement),
`scripts/UI/HideoutEditor.cs`, `scripts/UI/DecorShopPanel.cs`. Art:
`tools/art/sprites/town.ts`. Tests: `EndgameSmokeTest` `HIDEOUT-*`. Captures:
`RunCapture --only=town`.

## The town as designed

A walled 50×36-cell town (32 px cells), drawn as text in `HideoutTown.Map`:

- **Plaza** (centre): a mosaic ring around the **Mirror of Realities**, with **Odran
  the Trader**'s striped stall and **Ysolde's Furnishings** facing each other, four
  braziers and two benches. New characters arrive here.
- **Smithy** (west): a walled workshop with oak floors, the **Artificer's forge**
  (hearth and anvil, with the Artificer beside it), a weapon rack and barrels. Its
  door faces the plaza.
- **Training yard** (east): grass with three training dummies.
- **North gate**: the **Survival Rift** portal, with lamp posts along the road. In
  the dark alley east of it stands **Arehucaz**, the shard dealer
  ([endgame.md](endgame.md) -> "Arehucaz").
- **Canal** across the south (animated water, see art-pipeline.md -> "Liquids"), crossed by three bridges, leading to the **temple**
  (marble, the **Altar of Ascension** and a hero statue), the **garden** (trees and a
  well) and the **vault** (royal carpet, the **stash**).
- Houses: solid sandstone blocks around the north side, so the town has a skyline
  in the voxel view.

The ground uses a new `town` tileset (cobblestone, canal water, sandstone walls,
the plaza mosaic) and `town_floors` (marble, oak planks, grass, royal carpet, a gilded
mosaic). Walls and water collide; click-to-move uses a navigation mesh built from
one quad per walkable cell (shared corners keep it connected), so paths go round
buildings and through doors. A sunny lighting mood and blue canal glow replace the
reality moods in the voxel view.

Rejected: reusing the reality layout format (rooms and corridors). A town is
buildings, doors and a canal, which a hand-drawn character map says directly.

## Finding your way

The hideout HUD (docs/design/menus.md -> "Hideout HUD") explains the town: a town
directory with arrows and click-to-walk, an E prompt card over the nearest station,
and a town guide that opens on the first visit.

### Walking to a station **[IMPLEMENTED 2026-10-16]**

Clicking a station in the directory (also the guide's "Show me", the altar
shortcut on the character plate, and a station on the big map) walks you there.
That walk used Godot's navigation mesh, built from floor cells only. It knew
nothing about the stalls, the forge or the decorations standing on those cells,
and its edges ran flush against walls, so walks pushed into props and caught on
corners (owner report). Now these walks use **precomputed distance fields**
(`World/TownPaths.cs`), the technique the realities already use for monster
pathing:

- **Grid:** a 16 px grid (half a tile) offset so cell centres land on tile
  centres, so a one-tile door has a free cell down its middle. A cell is blocked
  if the player's own circle (one pixel larger) would touch anything the player
  collides with: a physics query, so walls, the canal and every prop with a body
  count without a list to maintain. The portal's mouth is blocked too, because
  walking into it enters the Rift.
- **One field per station:** Dijkstra over 8 neighbours (10/14, no corner
  cutting) from every free cell within 60 px of the station, i.e. the spots it can
  be used from, inside the E prompt's 80 px. For the Rift, the goal is the space
  in front of the gate instead. All fields are built about three physics frames
  after the town loads and again when the editor closes (the props may have
  moved). The town is about 100×70 cells, so all eight take a few milliseconds.
- **Walking:** the player steers each physics frame toward the farthest cell
  down the field (up to 40 ahead) that a straight shape cast of its circle can
  reach. That gives straight lines across open ground and turns only at corners
  (`Player.AutoWalk`).
- **Ending a walk:** WASD, a click on the ground, a dodge, an item pickup or a
  panel opening ends it, as before.
- **Safety nets:** with no progress along the field for a second, the walk stops
  with a "Blocked" banner. A station walled in by decorations gets "No way there"
  instead of a futile push.

Only these station walks use the fields. Ordinary click-to-move, in town and
everywhere else, keeps the navigation mesh (owner decision).

Rejected: fixing the navigation mesh (carving props out, insetting it by the
player's radius). That is standard, but Godot 4.1 has no agent radius for 2D
baking, so it would mean a hand-built mesh that is harder to maintain than a
grid. Hand-placed waypoints were also rejected, since they break as soon as the
player rearranges the town.

Tests: EndgameSmokeTest `HIDEOUT-WALKS-REACH-EVERY-STATION` (all eight stations
from the plaza, into E range, never into the portal) and
`HIDEOUT-WALK-AVOIDS-NEW-PROP` (a well dropped on the route to the forge).

### Town map **[IMPLEMENTED 2026-10-16]**

- **Minimap:** sits at the top of the town directory. It shows the whole town
  (it fits, so it never scrolls): the ground by quarter, the canal, every station
  as a dot in its colour, you as an arrow, and the route of a station walk in
  progress. Clicking it opens the big map.
- **Big map (M or Tab):** the same map full-screen over a dark veil. Each station
  is its own pixel-art icon (`tools/art/sprites/mapicons.ts`: the mirror, a coin
  purse for Odran, a shard for Arehucaz, a potted plant for Ysolde, an anvil for
  the forge, a chest, the altar's orb, the rift) in a badge ringed with its
  colour, with its name above (below when it would collide with a neighbour's).
  Decorations show as dots. Hovering a station puts what it does in the header,
  and **clicking it closes the map and starts the walk**. M, Tab or Esc closes
  it, and the town keeps going underneath.
- **Friends (2026-10-23):** in a party, both maps show everyone home with you
  as a dot in their party colour, named on the big map (multiplayer.md ->
  "Allies on the maps").

Code: `UI/TownMap.cs`. The ground is drawn as one texture, one pixel per tile.
M was also added to the map key everywhere, so it opens the overlay map in
realities too, alongside Tab.

### Training dummies **[IMPLEMENTED 2026-10-23]**

The town's dummies are punching bags (owner report: they could die and drop loot).
`TargetDummy.Immortal` (set by `Hideout.Dummy()`) tops their life back up inside the
`Damaged` handler, before `Health` checks for death, so no hit of any size kills one;
`Died` never fires, so there is no kill event, XP or revive. The hideout also removes
their `DropSpawner`/`EquipmentDropSpawner`, so nothing can drop even if that changes.
Damage numbers and the hit flash still show. In co-op every machine builds its own
dummies and only the host resolves damage, so the same flag covers the shared town.
The Training Grounds sandbox and the drop tests use the scene's default (mortal) dummy.
Test: `EndgameSmokeTest` `HIDEOUT-DUMMIES-IMMORTAL`.

## Props and stations

Everything standing in town is a **prop** with an id, a kind and a position: eight
**stations** (Mirror, Odran, Ysolde, Arehucaz, the forge, stash, altar, Survival
Rift) and the
**decorations**. Stations open their panel with E when you stand near them. Sprites
stand on their prop's origin (2D draws them upwards with a sprite offset; the voxel
view stands the model on the same point), so moving a prop moves the whole thing.

New pixel-art props (all voxelised by the art pipeline): Arehucaz (a hooded figure
in a ragged black robe, violet eyes, a lantern of cold violet fire on a crooked
staff, shards glinting on a cloth at his feet), forge, stash chest, market
stall, brazier, lamp post, two banners, potted plant, crate, barrel, bench, flower
bed, weapon rack, oak tree, well, hero statue. NPCs reuse class sprites: Odran is
the Paladin, the Artificer the Sage and Ysolde the Ascendant.

## Edit mode **[IMPLEMENTED]**

H (or Esc → Edit hideout) opens it. The world keeps running and WASD still walks,
so you can look around while you arrange things; clicks go to the editor, never to
movement. Gold footprints mark stations and blue ones decorations.

- **Move**: drag any prop, stations included. Props only land on open ground
  (never in walls or the canal) and snap to an 8 px grid. Right-click a decoration
  to put it in storage; stations can't be stored.
- **Floors**: choose a floor you own tiles of, paint cells with the left button
  (each cell uses one tile; replacing a bought tile returns it to storage). The
  right button restores the town's own floor and refunds the tile. Walls and water
  can't be painted.
- **Decorations**: choose a stored decoration and click to place it.

Done, H or Esc saves. Painted cells rebuild their voxel terrain a quarter of a
second after painting pauses (`VoxelStage.RebuildTerrain`).

## Shop **[IMPLEMENTED]**

Ysolde sells:

| Floors (packs of 10) | Gold | Decorations | Gold |
|---|---|---|---|
| Grass | 30 | Crate, barrel | 25 |
| Oak planks | 40 | Potted plant | 40 |
| Ashen / crypt / drowned stone | 50 | Flower bed | 50 |
| Marble | 60 | Banners | 60 |
| Royal carpet | 80 | Bench | 70 |
| Plaza mosaic | 100 | Lamp post | 80 |
| Gilded mosaic | 150 | Weapon rack | 90 |
| | | Training dummy | 100 |
| | | Brazier | 120 |
| | | Oak tree | 150 |
| | | Well | 300 |
| | | Hero statue | 400 |

Purchases go into the hideout's storage; you place them in edit mode. The floors of
the three reality tilesets are on sale too, so a corner of town can look like a
crypt or the drowned mirror.

## Odran's Skill gems tab **[IMPLEMENTED 2026-10-18]**

Owner request (translated): *"Odran gets a new tab to buy skills: all of the game's
skills for now, unlocked by character level. Support gems too. First approach, you
have freedom; you may add new skills."*

### Findings that shape it

- Odran's panel (`review-odran-trade.png`) is a 1010×560 frame whose right column
  (Wares, four shards) uses less than half its height. There is room for a tab bar
  without growing the frame.
- Gems are not bag items in this game: they live in the character's gem collection
  (`CharacterData.Gems`) and are socketed from the skills panel (K). A bought gem
  goes there, like a picked-up one, not into the inventory grid.
- The skills panel (`review-skills-panel.png`) never says where gems come from. It
  gets one line (see "Small fixes" below).

### Layout (1280×720)

The frame stays 1010×560. Under the title, two tab buttons like the Mirror's
(160×40, the open one with a gold border): **Trade** (today's panel, unchanged) and
**Skill gems**. The Skill gems tab button carries an ember dot while there are gems
that unlocked since the character last opened it (`CharacterData.GemShopSeenLevel`,
saved, default 0).

```
┌ Odran the Trader ─────────────────────────────────────────────── [Close (Esc)] ┐
│ [ Trade ] [ Skill gems • ]                                                      │
│ ┌ 600 ─────────────────────────────────────┐ ┌ 360 ─────────────────────────┐ │
│ │ [Skills 13] [Supports 20]   ☐ Only what I can buy │ [icon 48] Spark           │ │
│ │ All · Attack · Spell · Projectile · Melee · Area · │ Skill gem · Spell ·       │ │
│ │ Fire · Cold · Lightning · My class                 │ Projectile · Lightning    │ │
│ │ ┌ list, rows 52 px, scrolls ────────────┐ │ │ Favoured by: Arcanist        │ │
│ │ │[ic] Fireball        Arcanist   Owned · Lv 7 │ │ Fires crackling sparks…     │ │
│ │ │[ic] Spark           Arcanist   200 g        │ │ Damage 12 · use 0.40 s      │ │
│ │ │[ic] Throwing Knives Rogue      200 g        │ │ Unlocked at level 4          │ │
│ │ │[ic] Meteor          Arcanist   Level 16     │ │ [ Buy · 200 g ]  (primary)   │ │
│ │ └──────────────────────────────────────┘ │ │ reason / result line         │ │
│ └──────────────────────────────────────────┘ └──────────────────────────────┘ │
│ Gold 2,400 · Gems go to your skills (K) at level 1, not to your bag.            │
└─────────────────────────────────────────────────────────────────────────────────┘
```

- **Kind toggle**: Skills / Supports with their counts (two 130×30 buttons).
- **Filter chips** (one active at a time, 26 px tall, 12 px text). Skills: All,
  Attack, Spell, Projectile, Melee, Area, Fire, Cold, Lightning, **My class** (the
  class's `RiftSkills`). Supports: All, **Fits my skills** (supports whose
  `SupportDefinition.Accepts` is true for at least one skill the character owns),
  Projectile, Area, Melee, Spell, Attack (the support's `RequiresAny`).
- **"Only what I can buy"** (off by default): hides locked, owned and unaffordable
  rows. Off by default so the whole catalogue, locked rows included, shows the
  shape of what's coming.
- **Rows** (52 px): the gem's icon at 40 px (skills: `SkillDefinition.Icon`;
  supports: `icon_support`), the name at 16 px (skill orange `(1, 0.6, 0.35)`,
  support green `(0.45, 1, 0.55)`, the pickup colours), its tags at 12 px dim, the
  favoured classes as small pills, and the state at the right:
  - `200 g` in gold (buyable), in ember if you can't afford it;
  - `Owned · Lv 7` in green-dim (skills you have);
  - `2 / 6 owned · 60 g` (supports);
  - `Level 16` in dim text with a small lock, the row at 55 % opacity (locked);
  - a `NEW` tag for gems unlocked since the last visit.
  Sorted by unlock level, then name. The first row is selected on open.
- **Detail pane** (360 px): icon 48, name 22 px, "Skill gem · Spell · Projectile ·
  Lightning", "Favoured by: Arcanist", the description (the skill resource's
  `Description` / the support's `Description`), for skills the base numbers
  (damage, use time, cooldown), for supports "Fits your: Fireball, Spark" or, in
  ember, "None of your skills can use this yet". Then the unlock line ("Unlocks at
  character level 16 · you are 9" / "Unlocked at level 4") and the primary
  **Buy · 200 g** (`UiTheme.PrimaryButton`, 300×44). Under it, the reason when it's
  disabled, or the result after buying.
- **States of Buy**:
  - locked: disabled, "Unlocks at character level 16";
  - skill owned: disabled, "You have this skill. Skill gems level up as you use them.";
  - supports at the cap: disabled, "You have 6 of 6, one per loadout slot";
  - not enough gold: disabled, "Not enough gold: 140 more";
  - bought: the row flips to Owned at once, `pickup` sound, gold updates, and the
    pane reads "Bought Spark. It's in your skills (K) at level 1." with a secondary
    **Open skills (K)** button (closes Odran, opens the skills panel; two modals
    are never stacked).
- **Keys**: ↑/↓ moves the selection, Enter buys the selected gem, Esc closes.
  Tabs by click (Ctrl+Tab is not worth a binding).
- **Feedback** within a frame for every click; errors next to the Buy button,
  never only in a status line far away.

### What you get, and buying twice

- A gem at **level 1** in the character's collection (`CharacterData.AddGem(id,
  support)`), saved when the panel closes (`MerchantPanel.OnClosed` already captures
  the profile).
- **Skill gems: one copy.** A second purchase is refused rather than melted into gem
  experience (which is what a duplicate drop does). Buying levels with gold would
  turn gold into skill power, a balance change the owner should decide (open
  question).
- **Supports: up to 6 copies** (`GemLoot.MaxSupportCopies`, one per loadout slot),
  each at the full price. (endgame.md says supports stack to 4; the code's cap is 6,
  the number of loadout slots. This plan follows the code.)
- Drops are unchanged: bosses still drop gems, duplicates still melt.

### Unlock levels and prices

Skills cost **50 gold × unlock level**; supports **20 + 40 × unlock level**. Level 1
gems are cheap enough to buy right after the introduction (the Odran quest needs
one: quests.md).

| Skill gem | Tags | Favoured by | Unlocks at | Price |
|---|---|---|---|---|
| Fireball | Spell, Projectile, Fire | Arcanist | 1 | 50 |
| Double Strike | Attack, Melee | Rogue, Warrior | 1 | 50 |
| Cleave | Attack, Melee, Area | Warrior | 1 | 50 |
| Spark | Spell, Projectile, Lightning | Arcanist | 4 | 200 |
| Throwing Knives | Attack, Projectile | Rogue | 4 | 200 |
| Ground Slam | Attack, Melee, Area | Warrior | 4 | 200 |
| **Frost Shards** (new) | Spell, Projectile, Cold | Arcanist | 6 | 300 |
| Ethereal Blades | Spell, Area, Duration | Rogue, Arcanist | 8 | 400 |
| Frost Nova | Spell, Area, Cold | Arcanist | 8 | 400 |
| **Storm Strike** (new) | Attack, Melee, Lightning | Rogue | 10 | 500 |
| Sunder | Attack, Melee, Area | Warrior | 12 | 600 |
| **Glacial Hammer** (new) | Attack, Melee, Area, Cold | Warrior | 14 | 700 |
| Meteor | Spell, Area, Fire | Arcanist | 16 | 800 |

| Support gems | Unlock | Price |
|---|---|---|
| Faster Casting, Increased Area, Spell Echo (the classes' starting supports) | 1 | 60 |
| Pierce, Faster Projectiles, Melee Splash, Extended Duration | 2 | 100 |
| Multiple Projectiles, Fork, Chain, Multistrike, Blade Storm | 6 | 260 |
| Ignition, Overcharge, Cold Conversion, Fire Conversion, Lightning Conversion | 10 | 420 |
| Concentrated Effect, Elemental Focus, Brutality | 14 | 580 |

Why these: starting gems at level 1 so every class can try the others' basics;
damage multipliers (Concentrated Effect, Elemental Focus, Brutality) last because
they are the biggest single jumps; conversions at 10, where the tree's elemental
wheels start being reachable. "Favoured by" is the classes' `RiftSkills` list, so it
also tells you which skills the class's Rift cards prefer. Nothing is class-locked.

### Three new skills

Gaps in the first ten: Cold has only Frost Nova (no cold projectile for the Avatar of
Frost keystone and the freeze wheel), there is no Lightning attack (the Rogue's
lightning wheel and Overcharge have no melee user), and the Rogue has the thinnest
list (three skills, two shared). All three use existing archetypes, so each is a
`.tres` resource, one `UpgradeCatalog.Skills` entry, a `ClassCatalog.RiftSkills`
entry and an icon; no new code paths.

| Skill | Archetype | Tags (bits) | Numbers (first pass) | Notes |
|---|---|---|---|---|
| **Frost Shards** | `ProjectileSkillDefinition` | Spell, Projectile, Cold (1030) | 3 shards, 24° fan, speed 620, range 480, base damage 9 each, use time 0.45 s, mana 4 | reuses `assets/sprites/projectiles/frost_shard.tres`; cold always chills; Arcanist `RiftSkills` |
| **Storm Strike** | `MeleeSkillDefinition` (arc) | Attack, Melee, Lightning (4105) | range 85, arc 60°, 1 strike, base damage 20, use time 0.40 s | shocks like any Lightning hit; Rogue `RiftSkills` (and fits the Warrior's Duelist border) |
| **Glacial Hammer** | `MeleeSkillDefinition` (arc) | Attack, Melee, Area, Cold (1049) | range 95, arc 110°, base damage 30, use time 0.70 s, knockback 120 | cold always chills; Warrior `RiftSkills` |

Icons: three 16×16 icons in `tools/art/sprites/icons.ts` (`icon_frost_shards`,
`icon_storm_strike`, `icon_glacial_hammer`), the existing style; rebuild the art and
re-import. Damage numbers are a starting point for the autoplay bot
(`scripts/autoplay.sh --mode=reality --class=<id>`); the reference is the existing
skill of the same archetype (Throwing Knives, Double Strike, Cleave). Add them to
progression.md's skills table when implemented.

### Code

- `scripts/Characters/GemShop.cs` (new, static, no nodes):
  `record GemOffer(string Id, bool Support, int Level)` with `Price`; `Offers` (the
  tables above); `Find`; `Owned(c, offer)`; `CannotBuy(c, wallet, offer)` returning
  the reason strings above or null; `TryBuy(c, wallet, offer, out message)` (spends
  gold from the wallet, `c.AddGem(id, support)`, raises `gem_bought`);
  `FavouredBy(skillId)`; `FitsAny(c, support)`.
- `MerchantPanel`: a tab row; today's body becomes the Trade tab; a new
  `GemShopView : VBoxContainer` (`scripts/UI/GemShopView.cs`) builds the Skill gems
  tab. `MerchantPanel.ShowTab(MerchantTab)` for the quest dialogue's "Open the Skill
  gems tab".
- `HideoutCatalog`: Odran's `Blurb` becomes "Sell gear, buy crafting shards and skill
  gems." (the town guide and the big map read it); `Verb` stays "Trade".

### Small fixes in the same pass

- Skills panel (K), under the gem collection, a dim line: "More gems: reality bosses
  drop them, and Odran sells every one (Skill gems tab)."

### Acceptance and tests (EndgameSmokeTest `GEMSHOP-*`)

| Check | What |
|---|---|
| `GEMSHOP-COVERS-CATALOG` | every `UpgradeCatalog.Skills` and `Supports` id has exactly one offer, and every offer exists in the catalog |
| `GEMSHOP-LOCKED` | a level-3 character: Meteor's row is locked, Buy disabled, reason "Unlocks at character level 16" |
| `GEMSHOP-BUY-SKILL` | buying Spark at level 4 with 500 gold: wallet −200, a level-1 Spark gem in `Gems`, the row shows Owned, the `gem_bought` event fires |
| `GEMSHOP-NO-DUPLICATE-SKILL` | a second Spark purchase is refused and the wallet is unchanged |
| `GEMSHOP-SUPPORT-CAP` | the seventh copy of a support is refused ("6 of 6") |
| `GEMSHOP-NOT-ENOUGH-GOLD` | the reason says how much more |
| `GEMSHOP-FITS-FILTER` | a Fireball-only character: "Fits my skills" lists Pierce and Spell Echo, not Multistrike |
| `GEMSHOP-SAVED` | after closing the panel the profile JSON has the new gem |
| `GEMSHOP-NEW-DOT` | levelling past an unlock shows the tab's dot; opening the tab clears it |
| `SKILLS-NEW-RESOURCES` | the three new skills load, cast in the sandbox and deal damage (SkillsSmokeTest) |

Capture: `RunCapture --only=crafting` gains `odran-gems-skills.png`,
`odran-gems-supports.png` and `odran-gems-bought.png`.

### As built (deviations and additions)

- Code: `scripts/Characters/GemShop.cs` (rules, as planned: `GemOffer`, `Offers`, `Find`,
  `Owned`, `CannotBuy`, `TryBuy`, `FavouredBy`, `FitsAny`, plus `FitNames`, `NewCount`,
  `Locked`), `scripts/UI/GemShopView.cs` (the tab) and `MerchantPanel`
  (`MerchantTab`, `ShowTab`, the ember dot, `Character` override for tests).
  `CharacterData.GemShopSeenLevel` is saved (`SaveCharacter.GemShopSeenLevel`, clamped on load).
- **Event:** `GemShop.TryBuy` raises `GameEvents.GemBought` (key = gem id, value 1 for a
  support) for the quest system (`Core/GameEvents.cs`, see quests.md).
- Rows are sorted by unlock level, then name (so Cleave comes before Double Strike at
  level 1). The `NEW` tag is hidden on rows already owned. The lock is a drawn glyph (the
  default font has no padlock). Starting skills the character already has show "Owned · Lv N".
- Keys: Up/Down/Enter are handled in `_Input` while the tab is visible and all its
  buttons take no focus, so Enter can't double-buy.
- Skills panel (K): the dim line "More gems: reality bosses drop them, and Odran sells every
  one (Skill gems tab)." is under the collection. Odran's `Blurb` reads "Sell gear, buy
  crafting shards and skill gems."
- The three new skills are in `UpgradeCatalog.Skills` and therefore also offered as
  Survival Rift new-skill cards; each is in its class's `RiftSkills` (the Arcanist's Frost
  Shards, the Rogue's Storm Strike, the Warrior's Glacial Hammer). Icons are in
  `tools/art/sprites/endgame.ts` (not `icons.ts`, next to the other melee icons).
  progression.md's skills table lists them.
- Tests: `GEMSHOP-*` and `ALTAR-CONFIRM` in `EndgameSmokeTest` (`EndgameSmokeTest.GemShop.cs`),
  plus `GEMSHOP-AFFORDABLE-AFTER-INTRO` (the introduction's boss alone drops 70 gold, a
  level-1 gem costs at most 60) and `SKILLS-NEW-RESOURCES` per skill in `SkillsSmokeTest`.

### Open questions

1. Prices and unlock levels are first guesses; the gold economy hasn't been tuned
   (crafting.md open question).
2. Should a skill gem you own be buyable again as gem experience (a gold sink that
   levels gems)? Default: no.
3. Should the shop ever sell gems above level 1 (PoE's vendors don't)? Default: no.

## Saving

The town is the **account's**, like the stash: `Profile.Hideout` (saved in
`profile.json`) keeps every prop's position, painted cells and storage. An empty
record is filled with the town as designed on the first visit, and stations added in
later versions are added to old records where the design puts them.

## Co-op: whose town **[IMPLEMENTED 2026-10-18]**

In a party (docs/design/multiplayer.md -> "Shared hideout") everyone walks **the
host's town**: the host sends its props and floors, and each guest's `Hideout`
shows a copy (`Data`) while keeping its own account's town (`OwnTown`) for
everything that belongs to the account:

- **Saving.** `SaveLayout` on a guest only captures the player (gear, wallet); it
  never writes the host's town over the guest's own `Profile.Hideout`. Tested with
  guests whose saves really write (`HideoutCoopTest` `HIDEOUT-GUEST-SAVE-SAFE`).
- **Ysolde.** A guest shopping in a friend's town buys for **their own** storage
  ("You're in Ana's town: everything you buy is delivered to your own town's
  storage"). Nobody buys for the host.
- **The guide.** It lists the host's stations, but "seen" is the guest's own flag.
- **Every NPC works on the guest's own character and wallet** (Odran, the forge,
  the altar, the stash, quests): nothing is replicated, the town is the only thing
  shared.
- The host's town follows edits live: a guest's props and floors are rebuilt in
  place (`Hideout.ReplaceLayout`), the player stays where they are.

### Edit permission and the lock

Only the host edits by default; a guest's H (and the pause menu's Edit hideout)
says "Only Ana can edit this town — ask them to allow it". The host allows a friend
in the party overlay ("Allow editing", per friend, this party only). **One editor at
a time**, the host included ("Bo is editing the town — wait until they're done"). A
guest edits a copy of the host's town **with the host's storage** (move, store,
place from storage, pave and unpave with stocked tiles); Done sends it to the host,
which keeps it only if the editor could have made it: the same stations, every
moved or new prop on open ground, nothing appearing or vanishing (for each
decoration and tile, placed + stored is unchanged —
`HideoutLayoutRules.ValidateEdit`). Then it is saved as the host's town and sent to
everyone. A refused edit tells the guest why; revoking the right closes their
editor without keeping anything.

## Open questions

- Ysolde's prices are guesses, as are all crafting prices; gold sinks need play.
- Should decorations be rotatable or flippable?
- More shop content: walls/fences you can build, lights that glow in the voxel view,
  rugs as flat props, seasonal items.
- Should different characters get different hideout "themes", or is one shared town right?
- NPCs deserve their own sprites rather than ascendancy ones.
