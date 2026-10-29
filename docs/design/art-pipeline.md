# Art pipeline (EasyPixel)

Status: **implemented**. Owner brief: *"menus, levels, characters/enemies/bosses and
skills can be designed with our tool EasyPixel."*

## What it is

All game art is made with [EasyPixel](https://github.com/AlvaroFalcon/EasyPixel).
Instead of driving the Electron app through its MCP server (not available in an
unattended cloud session), `tools/art` runs **EasyPixel's own DOM-free core**
(`packages/core`, pinned to a commit and fetched by `npm run setup`) from Node:

- Sprites are authored as `draw_grid` rows — one string per row, one character per
  pixel, a legend from characters to hex colours — exactly the format the MCP
  `draw_grid` tool takes. Small helpers mirror halves (`sym`), shift, patch and
  recolour rows to derive animation frames.
- `gridToPixels`, `addFrame`, `addTag` build an ordinary EasyPixel document; the
  editable source is saved as `assets/art_src/**/<name>.epx.json`.
- EasyPixel's **Godot exporter** writes the spritesheet PNG, a `SpriteFrames`
  `.tres` (one animation per tag) and a ready `AnimatedSprite2D` `.tscn` into
  `assets/sprites/...`; the arena tileset goes through `exportGodotTileset`.

```bash
cd tools/art && npm install && npm run setup   # once
npm run build                                   # re-export everything
npx tsx build.ts --preview /tmp/sheet.png       # 4× contact sheet for review
```

After re-exporting, open the editor (or run `--editor --quit` headless) once so
Godot re-imports changed PNGs.

## Editing art by hand

Open any `assets/art_src/**.epx.json` in EasyPixel, edit, and use *File → Export to
Godot* into the matching `assets/sprites/...` folder (same base name). Note that
`npm run build` regenerates from the TypeScript sources and would overwrite a hand
edit; once a sprite is being edited by hand, delete it from `tools/art/sprites/` so
the app becomes its source of truth.

## Contents

Player (idle + walk), five enemies, two bosses (idle + cast/wind-up), six
projectiles, three XP gems, a life orb, a chest, nine 16×16 icons (five skills,
support, passive, keystone, dodge) and the Ashen Arena tileset (4 floor, 2 lava,
wall, rune). Sprites are drawn at 1× and scaled ×2 (bosses ×3–3.5) in game, with
Nearest filtering project-wide.

## High-definition anime characters **[IMPLEMENTED 2026-10-12]**

The owner found the 16×24 characters too small to read and asked for more
definition in a Japanese anime style, plus a portrait per character on the
select screen.

- **Double resolution, same screen size.** Player characters are now 32×48 and the
  Player's `Sprite` scale went from ×2 to ×1, so the hitbox and every 2D overlay are
  unchanged. The voxel actor already derives its scale from the sprite's
  `GlobalScale`, so the figures just gained detail (inflate depth cap raised to 16).
- **Painted, not typed.** At 32×48 hand-typed rows stop scaling, so
  `tools/art/paint.ts` adds a tiny raster painter (ellipses, polygons, lines, an
  outline pass) and `tools/art/sprites/anime.ts` builds every class from one
  parametric chibi: a `Look` picks hair style, armour, weapon, extras (hat, halo,
  horns, mask, scarf, cape…) and a palette. Big eyes with two highlights, blush,
  pointed chin, ~2.5-head proportions. Adding an ascendancy is one `Look` entry.
- **Portraits.** The same `Look` paints a 64×64 bust into `assets/portraits/`.
  `UiTheme.Portrait` frames it in gold and is used by character select, the
  class-creation cards and the Ascendancy altar (falls back to the sprite's idle
  frame).
- **Faces, not scalps.** Upright voxel actors cancel the stage's depth stretch and
  lean back `VoxelActor.LeanDegrees` (30°) from the camera, most of the way to an
  HD-2D billboard (a 40° lean would face it exactly). Without that, the 40° camera
  plus the stretched depth showed mostly the tops of heads.
- **Closer camera.** The hideout and realities zoom the player camera to
  `Player.CloseZoom` (1.5); the Survival Rift stays at 1:1 so hordes stay in view.

### Four directions **[IMPLEMENTED 2026-10-12]**

For the fake-3D look the owner wanted characters to face up, down and sideways.
Each character sheet now holds three views of the same seven poses (21 frames):
the front (`idle`, `walk`, `attack` — unchanged names, so every existing caller
still works), the back (`idle_up`, `walk_up`, `attack_up`: hair or hood, cape over
the shoulders, the weapon behind the body; painted like the front and mirrored so
the weapon hand lands on the correct side) and a profile facing right
(`idle_side`, ...; the game mirrors it for left) with a real stride and the near
arm swinging. `Player.UpdateFacing` faces the aim (the cursor), walking or
standing (owner feedback 2026-10-16: walking used to face the movement direction).
A dash faces where it goes, and a co-op puppet faces where it moves since its aim
isn't replicated. A little hysteresis keeps diagonals steady. Sprites without the
directional animations (monsters) keep the old left/right flip.

The voxel export now writes 16-bit indices; with 21 frames per character the
player models total ~53 MB raw but ~7 MB compressed (git and the release zip both
compress them). EasyPixel already greedy-meshes, so there was nothing to merge.

Monsters, bosses and tiles are still the 1× art; they get the same treatment once
the owner signs off on the character style.

## Voxels — the HD-2D view **[IMPLEMENTED 2026-10-12]**

Owner brief: *"EasyPixel now exports voxels; use them as the game's main graphics
instead of flat 2D sprites, so enemies, the floor and walls can explode. Take the
look to an HD-2D like Octopath Traveler, with the same camera we use now."*

**Pipeline.** EasyPixel 0.2.0 (pinned to `bf08d5d`) adds a voxel export
(`documentToVoxels` with `extrude`/`inflate`/`layers`/`slices` modes, greedy
meshing, `.glb`/`.vox` writers). `tools/art/voxels.ts` uses it:

- every animated sprite (player, classes and ascendancies, monsters, bosses,
  props, projectiles) → one `.glb` in `assets/voxels/...` with **one node per
  animation frame** (`f<index>`, the frame's index in the EasyPixel document),
  built in `inflate` mode so figures are puffy rather than flat cards
  (thickness ≈ half the sprite width, 3–12 voxels);
- every tileset → one `.glb` of terrain pieces built from the tile pixels:
  `floor0-3` and `rune` (2-voxel slabs), `hazard0-1` (1-voxel slabs, emissive
  materials), `wall` (a 22-voxel-tall block whose sides repeat the tile's brick
  rows and whose top is a lighter cap).

One art pixel is 1/16 m, so a 16 px tile is one metre and the 2D scale (sprites
×2, 32 world px per metre) maps exactly.

```bash
cd tools/art && npx tsx voxels.ts          # all voxel models (or a name filter)
```

**Runtime (`scripts/View3D/`).** The game still *simulates* in 2D; the voxel
world is a presentation layer underneath the 2D canvas:

- `HdView` (autoload) attaches a `VoxelStage` to each scene, turns its TileMaps
  into voxel terrain (MultiMeshes) and mirrors every `AnimatedSprite2D` that has a
  voxel model with a `VoxelActor` (also sprites spawned later).
- **Same camera.** An orthographic `Camera3D` pitched 40° follows the active
  `Camera2D` at the same zoom, and the 3D world is stretched in depth by
  1/sin(pitch). The ground plane therefore lands on screen exactly where the 2D
  world was drawn, so every 2D overlay — telegraphs, rarity plates, labels, damage
  numbers, the gate bars — still lines up, with no gameplay code changed. Things
  with height (walls, figures) rise up the screen like 2D sprites did.
- `VoxelActor` follows its sprite's owner each frame (feet position, facing via
  FlipH, frame → voxel mesh, scale, visibility); projectiles lie flat and yaw with
  their heading at chest height. The 2D sprite is made transparent but keeps
  animating, so all existing sprite-driving code still works. Hit flashes and tints
  become a colour overlay.
- **Explosions.** A dying boss breaks its current voxel frame into cubes of its
  own colours that burst, bounce and shrink away (`VoxelDebris`). Ordinary monsters
  only crumble (16 cubes at about a third of the speed, gone in under a second): the
  owner found full bursts on every pack death too much. Heavy telegraphed
  hits (radius ≥ 55 px) kick up floor cubes and knock bricks off walls they reach.
- **HD-2D look** (Forward+): key light with shadows, a warm light on the player,
  coloured lights over lava/ichor/water, glow, tilt-shift depth of field (top and
  bottom of the screen blur), a touch of saturation. Walls that hide the player
  dither away (`voxel_wall.gdshader`) so you never lose your character. The test is
  done in view space (orthographic camera: view x/y is the screen, z is depth): a
  wall fragment fades only if it is closer to the camera than the player's chest
  and inside an ellipse around them on screen. The first version faded any nearby
  wall on the camera's side, so a wall you merely stood beside turned to noise.
- **Off switch.** Settings → *Voxel graphics (HD-2D)*; headless runs (tests, the
  autoplay bot) never create the stage.

### Liquids **[IMPLEMENTED 2026-10-14]**

Owner feedback: the water looked like a flat painted floor. Liquid tiles keep their
one-voxel slab (the bed), and `VoxelStage.AddLiquidSurface` lays an instanced,
subdivided quad over every liquid tile with `resources/shaders/voxel_liquid.gdshader`:

- **Waves in world space**: two layers of fbm noise scrolling in different
  directions displace the vertices and bend the normal, so the surface rolls and
  catches the sun without seams between tiles.
- **Caustics**: thin bright ridges where a drifting fbm crosses its bands.
- **Shore foam**: each instance's custom data says which of its four sides touch
  land; the shader draws a lapping, noisy foam line along those edges and darkens
  the middle of wide pools (depth without a depth texture).
- **One shader, three liquids** (`VoxelStage.LiquidLook`): clear blue water (the
  Drowned Mirror, the hideout canal), slow glowing lava (Ashen Halls) and a sickly
  green ooze (Bone Crypt). The level picks it through `SetMood`.

**Living lava (2026-10-14).** Owner: "make the lava as alive as the water". The
shader has a `style` switch: lava is now **dark crust plates** (cellular noise,
drifting apart slowly) warmed red near their edges over **molten cracks** that burn
bright and breathe, with **bubbles** swelling and popping, and a cooled rim at the
shore; its emission follows the cracks, so in Forward+ the glow comes off the
cracks, not the crust. Ooze gained slow fat bubbles. Liquid lights flicker by type:
lava hard and fast, ooze pulsing slowly, water barely shimmering.

**Caverns** (endgame.md -> "Caverns"): wall blocks rise to uneven heights (smooth
noise plus a little per-block jitter), and glowing clusters — small emissive
shards leaning on the rock, each with a soft flickering light, at least seven metres
apart, at most thirty per map — line the cave walls: embers (Ashen Depths),
ghost-lights (Bone Warrens), crystals (Drowned Grotto). The ambient light is a
little darker in caverns so the glows carry the scene.

**Rounded shorelines (2026-10-14).** Owner: "the lava and the water are squares".
The shoreline no longer follows the tiles. `AddLiquidSurface` writes the liquid
tiles into a mask (one texel per tile), blurs it with a 5-tap Gaussian, and the
shader cuts the surface where the blurred mask crosses one half, wobbled by
world-space noise: square steps become smooth, rounded banks. Liquid quads also
cover the floor tiles bordering the liquid so inside corners can fill out, and the
bed under liquid is ordinary floor, seen where the bank curves in. Two earlier
attempts were dropped: rounding each tile from its eight neighbours left the map's
two-tile steps as straight runs; a per-tile marching-squares field did the same
at the larger scale. The tile sheet was not the way: in the voxel view the 2D
tiles are hidden, and gameplay collision stays per cell (the visual bank can sit up
to about a third of a tile off it).

Opaque on purpose: transparent liquid would need sorting against the voxel
terrain and showed the slab through it; the shore foam and colour depth carry the
"it's deep" read instead. Rejected: a screen-space refraction shader (no depth
texture in Compatibility, and heavy on the software renderer the captures use).

Captures: Forward+ renders in the cloud session through Mesa's software Vulkan
(`VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json ... --rendering-driver
vulkan`); the Compatibility renderer works but drops shadows, glow and DOF.

**Alternatives considered.**
- *Porting gameplay to 3D nodes.* Rejected for now: weeks of rework across
  physics, telegraphs and UI for the same play; the bridge gives the look today and
  leaves that door open.
- *HD-2D billboards (`AnimatedSprite3D`, EasyPixel's other new export).* Closer to
  Octopath's characters, but the brief asked for voxels that can explode; the
  billboard export stays available if characters should go back to flat sprites.
- *Voxelising at runtime in C#.* Would avoid the `.glb` files, but the owner asked
  for EasyPixel's export; keeping it in the pipeline also means the models can be
  opened in Blender/MagicaVoxel.

**Open questions.** Perspective camera (true Octopath framing) would need the
overlays re-projected; per-voxel destructible walls (today walls only shed chips);
hand-authored voxel props (`slices` mode) for the hideout and realities.

### Cave walls **[IMPLEMENTED 2026-10-15]**

Owner feedback: the cavern walls "don't join up — each block looks like its own
piece, worst on curves like the boss entrance". They were the tileset's brick
wall block, one per wall cell, each with its own random height: the tops
stepped, every block drew its own mortar edges, and a diagonal (the arena's
ring) became a staircase of cubes.

Caverns now build their walls as **one rock mesh** (`CaveWalls`,
scripts/View3D/CaveWalls.cs; `VoxelStage.AddCaveRock`):

- **Outline by marching squares** over the cell centres. A straight wall still
  ends exactly on the cell edge, where its collision is; a convex corner is cut
  on the diagonal and an inside corner filled, so curves read as one chamfered
  face (the floor is drawn under wall cells for the cut corners). A diagonal
  pair of rock cells joins into one piece.
- **Heights from a continuous field** (two noise octaves around the tile wall's
  height) sampled at the vertices, so neighbouring pieces share their edges —
  no steps. The tops are lit flat per triangle: faceted rock.
- **Solid mass**: every cell of the map's rectangle that isn't open ground is
  rock, including the empty cells deep inside the rock that used to show as
  black pits.
- **`voxel_rock.gdshader`** colours it in world space on a 1/8 m grid (the
  scene's voxel size): blotches of three tones taken from the tileset wall's
  own palette (darkest, middle, lightest — each theme keeps its stone), darker
  sides with wavy strata, a lighter top. It keeps the wall dither in front of
  the player (same uniforms as `voxel_wall.gdshader`).

Room layouts (the introduction, the hideout) keep the brick blocks: built
walls are supposed to look built. Screenshots: `cave-*-gate.png` (the arena
entrance), `cave-*-overview.png`.

## HD-2D with flat figures — sprites in the voxel world **[DECIDED 2026-10-29]**

Owner decision after a side-by-side comparison of three presentations on the same
Ashen Depths spot (`RunCapture --only=look -- --look=voxel|2d|billboard`):

1. **All voxel** (what shipped on 2026-10-12): figures are inflated to voxels.
   The scenery is the richest, but generated or hand-cleaned character art loses
   its detail (faces and small shapes soften) and is never seen as drawn.
2. **All 2D** (the "Voxel graphics" setting off): figures are crisp, but the
   caverns never had 2D art: flat brick walls, old square lava. Every environment
   would have to be repainted with front-facing walls.
3. **HD-2D, flat figures** (chosen, "sin duda"): the voxel world stays (terrain,
   cave walls, liquids, lights, shadows, depth of field) and everything that moves
   — characters, monsters, bosses, effects — is its **2D sprite as a camera-facing
   plane** in that world, lit and shadowed by it. This is Octopath Traveler's
   recipe, and the art pipeline gets simpler: a sheet generated with
   `tools/comfyui/spritegen.py` (ComfyUI) or cleaned in Aseprite is what the
   player sees, frame for frame.

**Art sources from now on.** Figures and effects: PNG sheets from
`tools/comfyui` (concept → Qwen Image Edit 2511 + svntax's 32×48 combat LoRA →
hand cleanup in Aseprite → `spritegen.py pack-sheet`), 32×48 per frame for
player characters. Floors, liquids and projectiles: `spritegen.py tile` /
`projectile` (FLUX.2 Klein + a pixel-art LoRA, the game's own art as the style
reference, palette locked to it); recipes and approved seeds in
`tools/comfyui/env.json`. The voxel export (`tools/art/voxels.ts`) keeps building
the terrain from the tilesets.

**Prototype.** `VoxelActor.Billboard` (static, off by default) shows each figure as
a `Sprite3D` of its current 2D frame (engine billboard, nearest filtering, shaded,
alpha-cut so it casts a shadow) instead of its voxel mesh.

**Migration plan** (each step reviewed by the owner before the next):

1. Make flat figures the real path: a per-sprite mode instead of the static flag,
   the overhead life bar raised to clear the figure's head, hit flashes and tints
   on the `Sprite3D`, the wall dither checked against flat figures, downed co-op
   players lying flat, tests and the perf bench.
2. The Arcanist's approved sheet into `assets/sprites/player/` (idle still, walk,
   attack, hurt; `_side`, `_up`), then the other classes and the ascendancies.
3. Monsters and bosses through the same workflow (their frame sizes to decide).
4. Projectiles from `spritegen.py projectile` (flat on the ground, as now); hits and
   explosions with 2D/3D particles.
5. Floors and liquids per biome from `spritegen.py tile`.

**Open questions.** Props (barrels, braziers): flat figures or keep voxels as part
of the scenery? Death bursts (`VoxelDebris`) break the current voxel frame into
cubes: keep a voxel export only for debris, or burst the sprite's pixels? Monsters
facing four directions or keep the left/right flip? Keep the "Voxel graphics"
off switch (classic 2D) or drop it once flat figures ship?

## Alternatives considered

- **MCP-driven drawing in the live editor.** The nicer workflow for a human session
  (watch it draw, undo with Ctrl+Z); the code path here produces the same documents
  and is reproducible in CI.
- **Hand-placed PNGs with no source format.** Rejected: the `.epx.json` sources keep
  layers/frames/animations editable.
