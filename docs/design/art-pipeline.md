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

## Alternatives considered

- **MCP-driven drawing in the live editor.** The nicer workflow for a human session
  (watch it draw, undo with Ctrl+Z); the code path here produces the same documents
  and is reproducible in CI.
- **Hand-placed PNGs with no source format.** Rejected: the `.epx.json` sources keep
  layers/frames/animations editable.
