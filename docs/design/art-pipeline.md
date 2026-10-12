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
- **Explosions.** A dying monster breaks its current voxel frame into cubes of its
  own colours that burst, bounce and shrink away (`VoxelDebris`). Heavy telegraphed
  hits (radius ≥ 55 px) kick up floor cubes and knock bricks off walls they reach.
- **HD-2D look** (Forward+): key light with shadows, a warm light on the player,
  coloured lights over lava/ichor/water, glow, tilt-shift depth of field (top and
  bottom of the screen blur), a touch of saturation. Walls between the camera and
  the player dither away (`voxel_wall.gdshader`) so you never lose your character.
- **Off switch.** Settings → *Voxel graphics (HD-2D)*; headless runs (tests, the
  autoplay bot) never create the stage.

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

## Alternatives considered

- **MCP-driven drawing in the live editor.** The nicer workflow for a human session
  (watch it draw, undo with Ctrl+Z); the code path here produces the same documents
  and is reproducible in CI.
- **Hand-placed PNGs with no source format.** Rejected: the `.epx.json` sources keep
  layers/frames/animations editable.
