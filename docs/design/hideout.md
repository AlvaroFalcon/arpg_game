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

Code: `UI/TownMap.cs`. The ground is drawn as one texture, one pixel per tile.
M was also added to the map key everywhere, so it opens the overlay map in
realities too, alongside Tab.

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
