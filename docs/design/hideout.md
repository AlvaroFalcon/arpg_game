# The hideout town

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
- **North gate**: the **Survival Rift** portal, with lamp posts along the road.
- **Canal** across the south, crossed by three bridges, leading to the **temple**
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

## Props and stations

Everything standing in town is a **prop** with an id, a kind and a position: seven
**stations** (Mirror, Odran, Ysolde, the forge, stash, altar, Survival Rift) and the
**decorations**. Stations open their panel with E when you stand near them. Sprites
stand on their prop's origin (2D draws them upwards with a sprite offset; the voxel
view stands the model on the same point), so moving a prop moves the whole thing.

New pixel-art props (all voxelised by the art pipeline): forge, stash chest, market
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

## Saving

The town is the **account's**, like the stash: `Profile.Hideout` (saved in
`profile.json`) keeps every prop's position, painted cells and storage. An empty
record is filled with the town as designed on the first visit, and stations added in
later versions are added to old records where the design puts them.

## Open questions

- Ysolde's prices are guesses, as are all crafting prices; gold sinks need play.
- Should decorations be rotatable or flippable?
- More shop content: walls/fences you can build, lights that glow in the voxel view,
  rugs as flat props, seasonal items.
- Should different characters get different hideout "themes", or is one shared town right?
- NPCs deserve their own sprites rather than ascendancy ones.
